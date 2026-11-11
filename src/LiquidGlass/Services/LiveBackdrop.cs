using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>Uma janela na frente da dock: o identificador e o retângulo visível, em pixels físicos.</summary>
internal readonly record struct WindowOver(nint Handle, Int32Rect Visible, Int32Rect Frame);

/// <summary>
/// Fundo ao vivo da dock. Enquanto nenhuma janela passa por baixo do vidro, a dock refrata o
/// papel de parede (que não custa nada). Quando uma janela entra na área da dock, o Windows passa
/// a mandar a imagem dela (Windows.Graphics.Capture, sem a borda amarela) e o vidro refrata o
/// conteúdo real, como a dock do macOS. A dock nunca captura a si mesma: ela lê cada janela
/// separadamente, então continua aparecendo em prints e gravações de tela.
/// </summary>
public sealed class LiveBackdrop : IDisposable
{
    /// <summary>Meia resolução, como o papel de parede: o fundo é desfocado e fica igual com ¼ da memória.</summary>
    private const int Downscale = 2;

    /// <summary>Janelas sobrepostas além dessas ficam de fora (raro: a de cima quase sempre cobre as outras).</summary>
    private const int MaxWindows = 4;

    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(33);

    /// <summary>Sem janela por baixo por esse tempo, a dock libera o Direct3D (dezenas de MB do driver de vídeo).</summary>
    private static readonly TimeSpan DeviceIdleTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Uma janela que não pôde ser capturada só é tentada de novo depois disso.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private static readonly HashSet<string> IgnoredClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private readonly WallpaperService _wallpaper;
    private readonly Dispatcher _dispatcher;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly object _gate = new();
    private readonly byte[][] _buffers = [[], []];
    private Thread? _thread;
    private ManualResetEventSlim? _stop;
    private Int32Rect _region = Int32Rect.Empty;
    private int _blurRadius;
    private int _settingsVersion;
    private int _uiBusy;
    private WriteableBitmap? _bitmap;

    /// <summary>Trecho lido de uma janela, reaproveitado entre quadros (só na thread da captura).</summary>
    private byte[] _scratch = [];

    internal LiveBackdrop(WallpaperService wallpaper, Dispatcher dispatcher)
    {
        _wallpaper = wallpaper;
        _dispatcher = dispatcher;
    }

    /// <summary>A imagem mudou ou o fundo ao vivo ligou/desligou (sempre na thread da interface).</summary>
    public event EventHandler? Changed;

    /// <summary>O fundo ao vivo, enquanto há janela por baixo da dock; nulo quando vale o papel de parede.</summary>
    public ImageSource? Image { get; private set; }

    /// <summary>Canto superior esquerdo de <see cref="Image"/> na tela, em pixels físicos.</summary>
    public Point Origin { get; private set; }

    /// <summary>Este Windows não permite capturar sem a borda amarela: o fundo ao vivo fica desligado.</summary>
    public static bool IsUnavailable { get; private set; }

    /// <summary>Janelas capturadas agora, por todas as docks (para o diagnóstico).</summary>
    public static int ActiveWindows => _activeWindows;

    /// <summary>Tempo médio para montar um quadro do fundo, em milissegundos (para o diagnóstico).</summary>
    public static double AverageFrameMilliseconds { get; private set; }

    private static int _activeWindows;

    private static readonly HashSet<nint> LoggedWindows = [];

    public bool IsEnabled => _thread is not null;

    /// <summary>Liga ou desliga a captura. Desligado, a dock volta ao papel de parede na hora.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled)
        {
            return;
        }

        if (enabled)
        {
            _stop = new ManualResetEventSlim();
            _thread = new Thread(Run) { IsBackground = true, Name = "LiquidGlass.LiveBackdrop", Priority = ThreadPriority.BelowNormal };
            _thread.Start(_stop);
            return;
        }

        _stop?.Set();
        _thread = null;
        _stop = null;
        Publish(null);
    }

    /// <summary>A área do vidro na tela, em pixels físicos; vazia quando a dock está escondida.</summary>
    public void SetRegion(Int32Rect region)
    {
        lock (_gate)
        {
            if (region != _region)
            {
                _region = region;
                _settingsVersion++;
            }
        }
    }

    /// <summary>O mesmo desfoque do papel de parede (em pixels de tela).</summary>
    public void SetBlur(double blur)
    {
        lock (_gate)
        {
            var radius = (int)Math.Round(Math.Max(blur, 0) / Downscale);
            if (radius != _blurRadius)
            {
                _blurRadius = radius;
                _settingsVersion++;
            }
        }
    }

    public void Dispose() => SetEnabled(false);

    /// <summary>
    /// As janelas que a dock precisa mostrar, de baixo para cima, a partir da lista da frente para
    /// trás: só as que tocam a região, e nada abaixo de uma janela que a cobre inteira.
    /// </summary>
    internal static List<WindowOver> SelectWindows(IEnumerable<WindowOver> frontToBack, Int32Rect region)
    {
        var selected = new List<WindowOver>();
        foreach (var window in frontToBack)
        {
            if (Intersect(window.Visible, region) is not { } overlap)
            {
                continue;
            }

            selected.Add(window);
            if (overlap == region || selected.Count == MaxWindows)
            {
                break;
            }
        }

        selected.Reverse();
        return selected;
    }

    /// <summary>
    /// Mistura um trecho capturado (BGRA pré-multiplicado, resolução cheia) sobre o fundo em meia
    /// resolução. <paramref name="x"/> e <paramref name="y"/> dizem onde o trecho começa dentro do
    /// fundo, em pixels de tela; cada pixel do fundo é a média de 2×2 pixels do trecho.
    /// </summary>
    internal static void BlendHalf(byte[] target, int targetWidth, int targetHeight, byte[] source, int sourceWidth, int sourceHeight, int x, int y)
    {
        var firstColumn = Math.Max((x + 1) / 2, 0);
        var lastColumn = Math.Min((x + sourceWidth) / 2, targetWidth);
        var firstRow = Math.Max((y + 1) / 2, 0);
        var lastRow = Math.Min((y + sourceHeight) / 2, targetHeight);
        var stride = sourceWidth * 4;

        for (var row = firstRow; row < lastRow; row++)
        {
            var top = (((2 * row) - y) * stride) + (((2 * firstColumn) - x) * 4);
            var output = ((row * targetWidth) + firstColumn) * 4;
            for (var column = firstColumn; column < lastColumn; column++, top += 8, output += 4)
            {
                var bottom = top + stride;
                var alpha = source[top + 3] + source[top + 7] + source[bottom + 3] + source[bottom + 7];
                if (alpha == 0)
                {
                    continue;
                }

                var keep = (4 * 255) - alpha;
                for (var channel = 0; channel < 3; channel++)
                {
                    var sum = source[top + channel] + source[top + 4 + channel] + source[bottom + channel] + source[bottom + 4 + channel];
                    target[output + channel] = (byte)(((sum * 255) + (target[output + channel] * keep)) / (4 * 255));
                }

                target[output + 3] = 255;
            }
        }
    }

    internal static Int32Rect? Intersect(Int32Rect a, Int32Rect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right > left && bottom > top ? new Int32Rect(left, top, right - left, bottom - top) : null;
    }

    private void Run(object? state)
    {
        var stop = (ManualResetEventSlim)state!;
        CaptureDevice? device = null;
        var accessChecked = false;
        var idleSince = DateTime.UtcNow;

        var captures = new Dictionary<nint, WindowCapture>();
        var failed = new Dictionary<nint, DateTime>();
        var lastWindows = new List<WindowOver>();
        var lastVersion = -1;
        object? lastWallpaper = null;
        var buffer = 0;
        var stopwatch = new Stopwatch();
        double averageMs = 0;

        try
        {
            var interval = IdleInterval;
            while (!stop.Wait(interval))
            {
                Int32Rect region;
                int blur, version;
                lock (_gate)
                {
                    (region, blur, version) = (_region, _blurRadius, _settingsVersion);
                }

                var wallpaper = _wallpaper.Pixels;
                var windows = region.IsEmpty || wallpaper is null ? [] : SelectWindows(WindowsFrontToBack(region), region);
                if (windows.Count > 0 && device is null)
                {
                    // A permissão e o dispositivo (com o driver de vídeo que ele carrega) só são pedidos
                    // quando há o que capturar: a dock parada não carrega nada disso.
                    if (!accessChecked && !CaptureDevice.IsBorderlessCaptureAllowedAsync().GetAwaiter().GetResult())
                    {
                        IsUnavailable = true;
                        return;
                    }

                    accessChecked = true;
                    device = CaptureDevice.TryCreate();
                    DemoCapture.Log($"fundo ao vivo: Direct3D criado para {windows.Count} janela(s): {string.Join(", ", windows.Select(window => User32.GetWindowClassName(window.Handle)))}");
                    if (device is null)
                    {
                        IsUnavailable = true;
                        return;
                    }
                }

                if (device is not null)
                {
                    SyncCaptures(device, captures, failed, windows);
                }

                if (windows.Count == 0 || captures.Count == 0)
                {
                    interval = IdleInterval;
                    if (device is not null && captures.Count == 0 && DateTime.UtcNow - idleSince > DeviceIdleTimeout)
                    {
                        device.Dispose();
                        device = null;
                        DemoCapture.Log("fundo ao vivo: Direct3D liberado");
                    }

                    if (lastWindows.Count > 0)
                    {
                        lastWindows = [];
                        Publish(null);
                    }

                    continue;
                }

                interval = ActiveInterval;
                idleSince = DateTime.UtcNow;
                stopwatch.Restart();
                var changed = version != lastVersion || !ReferenceEquals(wallpaper, lastWallpaper) || !windows.SequenceEqual(lastWindows);
                foreach (var capture in captures.Values)
                {
                    changed |= capture.Update();
                }

                // Sem quadro novo, sem janela mexendo e com a interface ainda ocupada: nada a refazer.
                if (!changed || Volatile.Read(ref _uiBusy) != 0)
                {
                    continue;
                }

                buffer = 1 - buffer;
                if (Compose(region, blur, wallpaper!, windows, captures, ref _buffers[buffer]) is not { } frame)
                {
                    continue;
                }

                (lastWindows, lastVersion, lastWallpaper) = (windows, version, wallpaper);
                averageMs = (averageMs * 0.9) + (stopwatch.Elapsed.TotalMilliseconds * 0.1);
                AverageFrameMilliseconds = averageMs;
                Volatile.Write(ref _uiBusy, 1);
                var pixels = _buffers[buffer];
                _dispatcher.BeginInvoke(() =>
                {
                    Publish(frame, pixels);
                    Volatile.Write(ref _uiBusy, 0);
                });
            }
        }
#pragma warning disable CA1031 // Um erro aqui (driver de vídeo reiniciado, janela que some no meio da cópia) não pode derrubar a dock.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // Desliga só o fundo ao vivo: a dock volta ao papel de parede e continua funcionando.
            LogError(exception);
            Publish(null);
        }
        finally
        {
            foreach (var capture in captures.Values)
            {
                capture.Dispose();
            }

            Interlocked.Add(ref _activeWindows, -captures.Count);
            device?.Dispose();
            DemoCapture.Log($"fundo ao vivo: parou; média de {averageMs:0.00} ms por quadro");
        }
    }

    private static void LogError(Exception exception)
    {
        try
        {
            AppDataPaths.EnsureRoot();
            File.AppendAllText(AppDataPaths.CrashLog, $"[{DateTime.Now:O}] Fundo ao vivo desligado: {exception}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Registrar é secundário.
        }
    }

    /// <summary>Abre a captura das janelas novas e fecha a das que saíram de baixo da dock.</summary>
    private static void SyncCaptures(CaptureDevice device, Dictionary<nint, WindowCapture> captures, Dictionary<nint, DateTime> failed, List<WindowOver> windows)
    {
        foreach (var (hwnd, capture) in captures.ToList())
        {
            if (capture.IsClosed || !windows.Any(window => window.Handle == hwnd))
            {
                capture.Dispose();
                captures.Remove(hwnd);
                Interlocked.Decrement(ref _activeWindows);
            }
        }

        var now = DateTime.UtcNow;
        foreach (var window in windows)
        {
            if (captures.ContainsKey(window.Handle) || (failed.TryGetValue(window.Handle, out var when) && now - when < RetryDelay))
            {
                continue;
            }

            if (WindowCapture.TryStart(device, window.Handle) is { } capture)
            {
                captures[window.Handle] = capture;
                failed.Remove(window.Handle);
                Interlocked.Increment(ref _activeWindows);
            }
            else
            {
                failed[window.Handle] = now;
            }
        }
    }

    /// <summary>Papel de parede com as janelas por cima, desfocado, em meia resolução.</summary>
    private Int32Rect? Compose(Int32Rect region, int blur, WallpaperPixels wallpaper, List<WindowOver> windows, Dictionary<nint, WindowCapture> captures, ref byte[] pixels)
    {
        // Alinhado aos pixels do papel de parede (que está em meia resolução a partir do monitor).
        var originX = (int)wallpaper.Origin.X;
        var originY = (int)wallpaper.Origin.Y;
        var left = originX + (((region.X - originX) / Downscale) * Downscale);
        var top = originY + (((region.Y - originY) / Downscale) * Downscale);
        var width = (region.X + region.Width - left + Downscale - 1) / Downscale;
        var height = (region.Y + region.Height - top + Downscale - 1) / Downscale;
        var wallpaperColumn = (left - originX) / Downscale;
        var wallpaperRow = (top - originY) / Downscale;
        width = Math.Min(width, wallpaper.Width - wallpaperColumn);
        height = Math.Min(height, wallpaper.Height - wallpaperRow);
        if (width <= 0 || height <= 0 || wallpaperColumn < 0 || wallpaperRow < 0)
        {
            return null;
        }

        if (pixels.Length != width * height * 4)
        {
            pixels = new byte[width * height * 4];
        }

        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(wallpaper.Pixels, (((wallpaperRow + row) * wallpaper.Width) + wallpaperColumn) * 4, pixels, row * width * 4, width * 4);
        }

        var area = new Int32Rect(left, top, width * Downscale, height * Downscale);
        var drawn = false;
        foreach (var window in windows)
        {
            if (!captures.TryGetValue(window.Handle, out var capture) || capture.ContentSize.Width <= 0
                || Intersect(window.Visible, area) is not { } visible)
            {
                continue;
            }

            // O quadro cobre o retângulo visível da janela ou, em alguns apps, o retângulo inteiro.
            var frame = capture.ContentSize.Width == window.Frame.Width && capture.ContentSize.Height == window.Frame.Height
                ? window.Frame
                : window.Visible;
            LogFirstFrame(window, capture);
            if (_scratch.Length < visible.Width * visible.Height * 4)
            {
                _scratch = new byte[visible.Width * visible.Height * 4];
            }

            if (capture.Read(new Int32Rect(visible.X - frame.X, visible.Y - frame.Y, visible.Width, visible.Height), _scratch))
            {
                BlendHalf(pixels, width, height, _scratch, visible.Width, visible.Height, visible.X - left, visible.Y - top);
                drawn = true;
            }
        }

        if (!drawn)
        {
            return null;
        }

        BoxBlur.Apply(pixels, width, height, blur);
        return new Int32Rect(left, top, width, height);
    }

    [Conditional("DEBUG")]
    private static void LogFirstFrame(WindowOver window, WindowCapture capture)
    {
        if (LoggedWindows.Add(window.Handle))
        {
            DemoCapture.Log($"fundo ao vivo: janela {window.Handle} visível {window.Visible} moldura {window.Frame} quadro {capture.ContentSize.Width}x{capture.ContentSize.Height}");
        }
    }

    /// <summary>Janelas visíveis de outros apps sobre a região, da frente para trás.</summary>
    private List<WindowOver> WindowsFrontToBack(Int32Rect region)
    {
        var windows = new List<WindowOver>();
        User32.EnumWindows(
            (hwnd, _) =>
            {
                if (!User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd)
                    || (User32.GetExtendedStyle(hwnd) & User32.WsExTransparent) != 0
                    || User32.IsFullyTransparent(hwnd)
                    || User32.GetWindowThreadProcessId(hwnd, out var process) == 0 || process == _ownProcessId
                    || Dwmapi.IsCloaked(hwnd)
                    || Dwmapi.GetVisibleBounds(hwnd) is not { } visible || visible.Width <= 0 || visible.Height <= 0
                    || !User32.GetWindowRect(hwnd, out var frame))
                {
                    return true;
                }

                var rect = new Int32Rect(visible.Left, visible.Top, visible.Width, visible.Height);
                if (Intersect(rect, region) is not null && !IgnoredClasses.Contains(User32.GetWindowClassName(hwnd)))
                {
                    windows.Add(new WindowOver(hwnd, rect, new Int32Rect(frame.Left, frame.Top, frame.Width, frame.Height)));
                }

                return true;
            },
            0);
        return windows;
    }

    /// <summary>Mostra o quadro novo (ou volta ao papel de parede com <c>null</c>), na thread da interface.</summary>
    private void Publish(Int32Rect? frame, byte[]? pixels = null)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Publish(frame, pixels));
            return;
        }

        if (frame is not { } area || pixels is null || !IsEnabled)
        {
            if (Image is not null)
            {
                Image = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != area.Width || _bitmap.PixelHeight != area.Height)
        {
            // DPI reduzido na mesma proporção: em DIPs a imagem tem o tamanho da área na tela.
            const double Dpi = 96.0 / Downscale;
            _bitmap = new WriteableBitmap(area.Width, area.Height, Dpi, Dpi, PixelFormats.Bgra32, null);
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, area.Width, area.Height), pixels, area.Width * 4, 0);
        var origin = new Point(area.X, area.Y);
        if (!ReferenceEquals(Image, _bitmap) || origin != Origin)
        {
            Origin = origin;
            Image = _bitmap;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
