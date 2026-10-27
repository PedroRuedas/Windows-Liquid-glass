using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Views;

namespace LiquidGlass.Services;

/// <summary>
/// Animações de janela no estilo do GNOME para as janelas de todos os apps:
/// <list type="bullet">
/// <item>Minimizar: "lâmpada mágica", a janela escorre em funil para dentro do ícone dela na dock
/// (e se desenrola de lá ao restaurar pela dock). O efeito usa um retrato da janela numa malha lisa
/// (<see cref="GenieWindow"/>); sem retrato (janela minimizada por outro caminho), fatias de
/// miniaturas do DWM fazem o mesmo efeito.</item>
/// <item>Fechar: a janela encolhe um pouco enquanto some.</item>
/// </list>
/// A animação nativa do Windows é desligada só na janela e só no instante em que a da dock vai
/// tocar: ao clicar em minimizar ou fechar (aviso do <see cref="GlobalInputHook"/>) ou nas ações da
/// própria dock. Atalhos de teclado e outros caminhos continuam com a animação do Windows.
/// </summary>
public sealed class WindowAnimator : IDisposable
{
    private static readonly TimeSpan MinimizeDuration = TimeSpan.FromMilliseconds(520);

    /// <summary>Retratos guardados para restaurar com o efeito (cada um ocupa alguns MB).</summary>
    private const int MaxRestoreImages = 3;
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>Quanto tempo um clique em minimizar ou fechar espera a janela reagir.</summary>
    private static readonly TimeSpan ArmTimeout = TimeSpan.FromSeconds(3);

    private readonly DockSettings _settings;
    private readonly EfficiencyMode _efficiency;
    private readonly List<RestoreImage> _restoreImages = [];
    private readonly User32.WinEventProc _minimizeProc;
    private readonly User32.WinEventProc _closeProc;
    private readonly Dictionary<nint, Armed> _armed = [];
    private readonly Dictionary<uint, List<nint>> _closeHooks = [];
    private readonly List<Flight> _flights = [];
    private readonly DispatcherTimer _expiry = new() { Interval = TimeSpan.FromSeconds(1) };
    private ThumbnailOverlay? _overlay;
    private nint _minimizeHook;
    private bool _isRendering;

    public WindowAnimator(DockSettings settings, EfficiencyMode efficiency)
    {
        _settings = settings;
        _efficiency = efficiency;
        _minimizeProc = OnMinimizeStart;
        _closeProc = OnWindowGone;
        _expiry.Tick += (_, _) => ExpireArms();
    }

    private enum ArmKind
    {
        Minimize,
        Close,
    }

    /// <summary>Retângulo do ícone da janela na dock, em pixels físicos da tela.</summary>
    public Func<nint, Rect?>? IconLocator { get; set; }

    public bool IsEnabled
    {
        get => _minimizeHook != 0;
        set
        {
            if (value == IsEnabled)
            {
                return;
            }

            if (value)
            {
                _minimizeHook = User32.SetWinEventHook(
                    User32.EventSystemMinimizeStart, User32.EventSystemMinimizeStart, 0, _minimizeProc, 0, 0,
                    User32.WinEventOutOfContext | User32.WinEventSkipOwnProcess);
            }
            else
            {
                User32.UnhookWinEvent(_minimizeHook);
                _minimizeHook = 0;
                foreach (var hwnd in _armed.Keys.ToList())
                {
                    Disarm(hwnd);
                }
            }
        }
    }

    private double Speed => Math.Max(_settings.AnimationSpeed, 0.1);

    /// <summary>Traz a janela para frente; minimizada, ela sai do ícone da dock até o lugar dela.</summary>
    public void Activate(nint hwnd)
    {
        if (!IsEnabled || !User32.IsIconic(hwnd) || RestoredBounds(hwnd) is not { } target)
        {
            AppLauncher.Activate(hwnd);
            return;
        }

        if (TakeRestoreImage(hwnd) is { } restore)
        {
            PlayRestore(hwnd, restore, target);
            return;
        }

        if (CreateGenie(hwnd, target) is not { } flight)
        {
            AppLauncher.Activate(hwnd);
            return;
        }

        // A lâmpada mágica de trás para frente: a janela sai do ícone e se desenrola até o lugar dela.
        // A janela de verdade aparece no último instante, por baixo das fatias, e a troca não aparece.
        Dwmapi.SetTransitionsDisabled(hwnd, true);
        var restored = false;
        flight.IsReversed = true;
        flight.OnProgress = time =>
        {
            if (!restored && time <= 0.02)
            {
                restored = true;
                AppLauncher.Activate(hwnd);
            }
        };
        flight.OnCompleted = () =>
        {
            if (!restored)
            {
                AppLauncher.Activate(hwnd);
            }

            Dwmapi.SetTransitionsDisabled(hwnd, false);
        };
        Fly(flight);
    }

    public void Minimize(nint hwnd)
    {
        if (IsEnabled)
        {
            Arm(hwnd, ArmKind.Minimize);
        }

        AppLauncher.Minimize(hwnd);
    }

    public void Close(nint hwnd)
    {
        if (IsEnabled)
        {
            Arm(hwnd, ArmKind.Close);
        }

        AppLauncher.Close(hwnd);
    }

    /// <summary>O usuário apertou minimizar ou fechar na barra de título de uma janela.</summary>
    public void OnCaptionButtonPressed(nint hwnd, CaptionButton button)
    {
        if (IsEnabled)
        {
            Arm(hwnd, button == CaptionButton.Close ? ArmKind.Close : ArmKind.Minimize);
        }
    }

    public void Dispose()
    {
        IsEnabled = false;
        _expiry.Stop();
        foreach (var flight in _flights)
        {
            flight.Release();
        }

        _flights.Clear();
        _overlay?.Dispose();
    }

    private static Rect ToRect(User32.Rect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Rect Deflate(Rect rect, Thickness inset) => new(
        rect.X + inset.Left,
        rect.Y + inset.Top,
        Math.Max(rect.Width - inset.Left - inset.Right, 1),
        Math.Max(rect.Height - inset.Top - inset.Bottom, 1));

    /// <summary>Restaurar com o retrato tirado ao minimizar: a malha sai do ícone até o lugar da janela.</summary>
    private void PlayRestore(nint hwnd, RestoreImage restore, Rect target)
    {
        var visible = Deflate(target, restore.Inset);
        var genie = new GenieWindow(restore.Image, visible, IconBounds(hwnd, visible), _settings.Edge, MinimizeDuration / Speed, isReversed: true);
        var token = new object();
        var restored = false;
        Dwmapi.SetTransitionsDisabled(hwnd, true);
        _efficiency.SetActive(token, active: true);
        genie.Play(
            onProgress: time =>
            {
                // A janela de verdade aparece por baixo no fim, já do tamanho do retrato.
                if (!restored && time <= 0.03)
                {
                    restored = true;
                    AppLauncher.Activate(hwnd);
                }
            },
            onCompleted: () =>
            {
                if (!restored)
                {
                    AppLauncher.Activate(hwnd);
                }

                Dwmapi.SetTransitionsDisabled(hwnd, false);
                _efficiency.SetActive(token, active: false);
            });
    }

    private RestoreImage? TakeRestoreImage(nint hwnd)
    {
        var index = _restoreImages.FindIndex(entry => entry.Window == hwnd);
        if (index < 0)
        {
            return null;
        }

        var entry = _restoreImages[index];
        _restoreImages.RemoveAt(index);
        return entry;
    }

    private void KeepRestoreImage(RestoreImage image)
    {
        _restoreImages.RemoveAll(entry => entry.Window == image.Window || !User32.IsWindow(entry.Window));
        _restoreImages.Add(image);
        if (_restoreImages.Count > MaxRestoreImages)
        {
            _restoreImages.RemoveAt(0);
        }
    }

    /// <summary>Onde a janela minimizada vai aparecer, em pixels físicos (com as bordas invisíveis).</summary>
    internal static Rect? RestoredBounds(nint hwnd)
    {
        var placement = new User32.WindowPlacement { Length = System.Runtime.InteropServices.Marshal.SizeOf<User32.WindowPlacement>() };
        if (!User32.GetWindowPlacement(hwnd, ref placement))
        {
            return null;
        }

        // A posição normal é relativa à área de trabalho do monitor principal, não à tela.
        var monitor = User32.GetPrimaryMonitorInfo();
        if ((placement.Flags & User32.WpfRestoreToMaximized) != 0)
        {
            return Rect.Inflate(ToRect(monitor.WorkArea), 8, 8);
        }

        var normal = ToRect(placement.NormalPosition);
        normal.Offset(monitor.WorkArea.Left - monitor.Monitor.Left, monitor.WorkArea.Top - monitor.Monitor.Top);
        return normal;
    }

    private Rect IconBounds(nint hwnd, Rect window)
    {
        if (IconLocator?.Invoke(hwnd) is { IsEmpty: false } icon)
        {
            return icon;
        }

        // Sem ícone conhecido: um ponto na base da janela.
        return new Rect(window.X + (window.Width / 2), window.Bottom, 1, 1);
    }

    /// <summary>Uma miniatura por fatia da janela, todas na janela de animação.</summary>
    private Flight? CreateGenie(nint hwnd, Rect window)
    {
        _overlay ??= new ThumbnailOverlay();
        var first = Dwmapi.RegisterThumbnail(_overlay.Handle, hwnd);
        if (first == 0 || Dwmapi.QuerySourceSize(first) is not { } size)
        {
            Dwmapi.UnregisterThumbnail(first);
            return null;
        }

        var genie = new Genie(window, IconBounds(hwnd, window), new Size(size.Width, size.Height), _settings.Edge);
        var thumbnails = new List<nint> { first };
        while (thumbnails.Count < genie.StripCount)
        {
            thumbnails.Add(Dwmapi.RegisterThumbnail(_overlay.Handle, hwnd));
        }

        return new Flight(thumbnails, genie, MinimizeDuration / Speed);
    }

    private void Arm(nint hwnd, ArmKind kind)
    {
        if (!User32.IsWindow(hwnd) || User32.IsIconic(hwnd))
        {
            return;
        }

        Disarm(hwnd);
        User32.GetWindowRect(hwnd, out var bounds);
        var armed = new Armed(kind, ToRect(bounds), DateTime.UtcNow + ArmTimeout);
        if (kind == ArmKind.Close)
        {
            // A janela some antes de qualquer aviso; o retrato é tirado agora, enquanto ela está na tela.
            if (Dwmapi.GetVisibleBounds(hwnd) is not { Width: > 0, Height: > 0 } visible)
            {
                return;
            }

            armed = armed with { Bounds = ToRect(visible), Snapshot = Capture(visible) };
            HookClose(hwnd);
        }

        else if (Dwmapi.GetVisibleBounds(hwnd) is { Width: > 0, Height: > 0 } visible)
        {
            // O retrato é tirado agora, com a janela na tela, e a janela do efeito já fica desenhada
            // fora da tela: ao minimizar, o efeito começa no mesmo quadro em que a janela some.
            var visibleRect = ToRect(visible);
            var snapshot = Capture(visible);
            var genie = new GenieWindow(snapshot, visibleRect, IconBounds(hwnd, visibleRect), _settings.Edge, MinimizeDuration / Speed, isReversed: false);
            genie.Prepare();
            var inset = new Thickness(
                visible.Left - bounds.Left,
                visible.Top - bounds.Top,
                bounds.Right - visible.Right,
                bounds.Bottom - visible.Bottom);
            armed = armed with { Snapshot = snapshot, Genie = genie, Inset = inset };
        }

        Dwmapi.SetTransitionsDisabled(hwnd, true);
        _armed[hwnd] = armed;
        _expiry.Start();
    }

    private static BitmapSource Capture(User32.Rect visible)
    {
        var region = new Int32Rect(visible.Left, visible.Top, visible.Width, visible.Height);
        var pixels = ScreenCapture.Screen(region, 1, out var width, out var height);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private void Disarm(nint hwnd)
    {
        if (_armed.Remove(hwnd, out var armed))
        {
            armed.Genie?.Close();
            Dwmapi.SetTransitionsDisabled(hwnd, false);
            if (armed.Kind == ArmKind.Close)
            {
                UnhookClose(hwnd);
            }
        }

        if (_armed.Count == 0)
        {
            _expiry.Stop();
        }
    }

    private void ExpireArms()
    {
        var now = DateTime.UtcNow;
        foreach (var (hwnd, armed) in _armed.ToList())
        {
            if (armed.Deadline <= now || !User32.IsWindow(hwnd))
            {
                Disarm(hwnd);
            }
        }
    }

    /// <summary>Avisos de "janela sumiu" só do processo dono da janela, e só enquanto ela está armada.</summary>
    private void HookClose(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var processId);
        if (_closeHooks.ContainsKey(processId))
        {
            return;
        }

        _closeHooks[processId] =
        [
            User32.SetWinEventHook(User32.EventObjectDestroy, User32.EventObjectDestroy, 0, _closeProc, processId, 0, User32.WinEventOutOfContext),
            User32.SetWinEventHook(User32.EventObjectHide, User32.EventObjectHide, 0, _closeProc, processId, 0, User32.WinEventOutOfContext),
            User32.SetWinEventHook(User32.EventObjectCloaked, User32.EventObjectCloaked, 0, _closeProc, processId, 0, User32.WinEventOutOfContext),
        ];
    }

    private void UnhookClose(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var processId);
        var stillArmed = _armed.Any(pair => pair.Value.Kind == ArmKind.Close && ProcessOf(pair.Key) == processId);
        if (stillArmed || !_closeHooks.Remove(processId, out var hooks))
        {
            return;
        }

        foreach (var hook in hooks)
        {
            User32.UnhookWinEvent(hook);
        }
    }

    private static uint ProcessOf(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var processId);
        return processId;
    }

    private void OnMinimizeStart(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint threadId, uint time)
    {
        if (!_armed.TryGetValue(hwnd, out var armed) || armed.Kind != ArmKind.Minimize)
        {
            return;
        }

        _armed.Remove(hwnd);
        if (armed is { Genie: { } genie, Snapshot: { } snapshot })
        {
            var token = new object();
            _efficiency.SetActive(token, active: true);
            genie.Play(onCompleted: () =>
            {
                Dwmapi.SetTransitionsDisabled(hwnd, false);
                _efficiency.SetActive(token, active: false);
                KeepRestoreImage(new RestoreImage(hwnd, snapshot, armed.Inset));
            });
            return;
        }

        if (CreateGenie(hwnd, armed.Bounds) is not { } flight)
        {
            Dwmapi.SetTransitionsDisabled(hwnd, false);
            return;
        }

        flight.OnCompleted = () => Dwmapi.SetTransitionsDisabled(hwnd, false);
        Fly(flight);
    }

    private void OnWindowGone(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint threadId, uint time)
    {
        if (objectId != User32.ObjIdWindow || childId != 0 || !_armed.TryGetValue(hwnd, out var armed) || armed.Kind != ArmKind.Close)
        {
            return;
        }

        // Ao esconder (apps que vão para a bandeja), a janela continua existindo: a animação nativa volta.
        _armed.Remove(hwnd);
        Dwmapi.SetTransitionsDisabled(hwnd, false);
        UnhookClose(hwnd);
        if (armed.Snapshot is { } snapshot)
        {
            CloseGhost.Play(snapshot, armed.Bounds, CloseDuration / Speed);
        }
    }

    private void Fly(Flight flight)
    {
        _flights.Add(flight);
        _efficiency.SetActive(_flights, active: true);
        _overlay!.Show();
        Step(flight, 0);
        if (!_isRendering)
        {
            _isRendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        foreach (var flight in _flights.ToList())
        {
            var t = Math.Clamp(flight.Clock.Elapsed / flight.Duration, 0, 1);
            Step(flight, t);
            if (t >= 1)
            {
                _flights.Remove(flight);
                flight.Release();
                flight.OnCompleted?.Invoke();
            }
        }

        if (_flights.Count == 0)
        {
            _isRendering = false;
            CompositionTarget.Rendering -= OnRendering;
            _overlay?.Hide();
            _efficiency.SetActive(_flights, active: false);
        }
    }

    private void Step(Flight flight, double t)
    {
        var time = flight.IsReversed ? 1 - t : t;

        // Já dentro do ícone, a janela some em vez de ficar como um risco por cima dele.
        var opacity = (byte)Math.Round(Math.Clamp((1 - time) / 0.12, 0, 1) * 255);
        var index = 0;
        foreach (var (destination, source) in flight.Genie.Layout(time))
        {
            var visible = destination.Width >= 0.5 && destination.Height >= 0.5;
            var sourceRect = new User32.Rect
            {
                Left = source.X,
                Top = source.Y,
                Right = source.X + source.Width,
                Bottom = source.Y + source.Height,
            };

            Dwmapi.UpdateThumbnail(flight.Thumbnails[index++], _overlay!.ToClient(ToNative(destination)), opacity, visible, sourceRect);
        }

        flight.OnProgress?.Invoke(time);
    }

    private static User32.Rect ToNative(Rect rect) => new()
    {
        Left = (int)Math.Round(rect.Left),
        Top = (int)Math.Round(rect.Top),
        Right = (int)Math.Round(rect.Right),
        Bottom = (int)Math.Round(rect.Bottom),
    };

    private sealed record Armed(ArmKind Kind, Rect Bounds, DateTime Deadline)
    {
        public BitmapSource? Snapshot { get; init; }

        /// <summary>Lâmpada mágica pronta (fora da tela) desde o clique, à espera da janela minimizar.</summary>
        public GenieWindow? Genie { get; init; }

        /// <summary>Bordas invisíveis da janela (entre o retângulo dela e a parte visível).</summary>
        public Thickness Inset { get; init; }
    }

    private sealed record RestoreImage(nint Window, BitmapSource Image, Thickness Inset);

    private sealed class Flight(List<nint> thumbnails, Genie genie, TimeSpan duration)
    {
        public List<nint> Thumbnails { get; } = thumbnails;

        public Genie Genie { get; } = genie;

        public TimeSpan Duration { get; } = duration;

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        /// <summary>Restaurar: o mesmo efeito, do ícone para a janela.</summary>
        public bool IsReversed { get; set; }

        /// <summary>Recebe o tempo do efeito (0 = janela, 1 = no ícone).</summary>
        public Action<double>? OnProgress { get; set; }

        public Action? OnCompleted { get; set; }

        public void Release()
        {
            foreach (var thumbnail in Thumbnails)
            {
                Dwmapi.UnregisterThumbnail(thumbnail);
            }

            Thumbnails.Clear();
        }
    }
}
