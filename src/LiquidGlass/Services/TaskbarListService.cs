using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>Estado da barra de progresso de um app, como em ITaskbarList3::SetProgressState.</summary>
public enum TaskbarProgressState
{
    None = 0,
    Indeterminate = 1,
    Normal = 2,
    Error = 4,
    Paused = 8,
}

/// <summary>
/// Recebe o que os apps mandam para os ícones da barra de tarefas pelo ITaskbarList3: o selo
/// (Discord, Telegram e Teams mostram ali as mensagens não lidas) e a barra de progresso
/// (downloads, cópias, instaladores).
/// </summary>
/// <remarks>
/// A implementação do ITaskbarList que roda dentro de cada app procura a janela que recebe essas
/// mensagens na propriedade "TaskbandHWND" da barra do Windows (Shell_TrayWnd). A dock aponta essa
/// propriedade para uma janela própria enquanto a barra do Windows está oculta, e devolve o valor
/// original ao sair, depois de um encerramento forçado (na abertura seguinte ou no --quit) e quando
/// a barra do Windows volta a aparecer. Apps da Microsoft Store mostram selos por outro sistema,
/// que não chega aqui.
/// </remarks>
public sealed class TaskbarListService : IDisposable
{
    public const double ProgressMaximum = 65534;

    private const string Property = "TaskbandHWND";
    private const string TrayClass = "Shell_TrayWnd";
    private const int WmUser = 0x0400;
    private const int SetProgressValue = WmUser + 64;
    private const int SetProgressState = WmUser + 65;
    private const int SetOverlayIcon = WmUser + 79;

    private static readonly string SavedPath = Path.Combine(AppDataPaths.Root, "taskband.txt");

    private readonly Dictionary<nint, ImageSource> _overlays = [];
    private readonly Dictionary<nint, (TaskbarProgressState State, double Value)> _progress = [];
    private HwndSource? _window;
    private nint _original;
    private bool _isAttached;

    /// <summary>As janelas dos apps abertos, para pedir que repitam o selo e o progresso que já tinham.</summary>
    public Func<IEnumerable<nint>>? OpenWindows { get; set; }

    /// <summary>Um app mudou o selo ou o progresso da janela informada.</summary>
    public event EventHandler<nint>? Changed;

    /// <summary>A propriedade aponta para a dock e as mensagens chegam aqui.</summary>
    public bool IsAttached => _isAttached;

    public ImageSource? OverlayFor(nint window) => _overlays.GetValueOrDefault(window);

    public (TaskbarProgressState State, double Value) ProgressFor(nint window) =>
        _progress.TryGetValue(window, out var progress) ? progress : (TaskbarProgressState.None, 0);

    /// <summary>Liga (barra do Windows oculta) ou desliga, devolvendo a propriedade à barra do Windows.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled && !_isAttached)
        {
            Attach();
        }
        else if (!enabled && _isAttached)
        {
            Detach();
        }
    }

    /// <summary>O Explorer reiniciou: a barra nova não tem a propriedade, então ela é posta de novo.</summary>
    public void OnTaskbarCreated()
    {
        if (_isAttached)
        {
            _isAttached = false;
            Attach();
        }
    }

    /// <summary>
    /// Depois de um encerramento forçado, a propriedade pode ter ficado apontando para uma janela
    /// que não existe mais, e a barra do Windows perderia selos e progresso: devolve o original.
    /// </summary>
    public static void RecoverAfterCrash()
    {
        if (!File.Exists(SavedPath))
        {
            return;
        }

        var tray = User32.FindWindow(TrayClass, null);
        if (tray != 0)
        {
            var current = User32.GetProp(tray, Property);
            if (current == 0 || !User32.IsWindow(current))
            {
                var saved = long.TryParse(File.ReadAllText(SavedPath), out var value) ? (nint)value : 0;
                Restore(tray, saved);
            }
        }

        TryDelete();
    }

    public void Dispose()
    {
        if (_isAttached)
        {
            Detach();
        }

        _window?.RemoveHook(WndProc);
        _window?.Dispose();
        _window = null;
    }

    private void Attach()
    {
        var tray = User32.FindWindow(TrayClass, null);
        if (tray == 0)
        {
            return;
        }

        _window ??= CreateWindow();
        var current = User32.GetProp(tray, Property);
        _original = current == _window.Handle ? _original : current;
        try
        {
            File.WriteAllText(SavedPath, ((long)_original).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
        }

        _isAttached = User32.SetProp(tray, Property, _window.Handle);
        Graphics.DemoCapture.Log($"taskbarlist: bandeja {tray:X}, original {_original:X}, nossa {_window.Handle:X}, ligado {_isAttached}");
        if (_isAttached)
        {
            AskAppsToResend();
        }
    }

    /// <summary>
    /// Os apps mandam selo e progresso quando a barra avisa que criou o botão deles
    /// ("TaskbarButtonCreated") e repetem tudo quando recebem o aviso de novo. Os que criam o
    /// ITaskbarList a cada uso passam a falar com a dock na hora; os que guardam o mesmo desde que
    /// abriram (como os apps WPF) continuam falando com a barra do Windows até serem reabertos.
    /// </summary>
    private void AskAppsToResend()
    {
        if (OpenWindows?.Invoke() is not { } windows)
        {
            return;
        }

        var message = User32.RegisterWindowMessage("TaskbarButtonCreated");
        if (message == 0)
        {
            return;
        }

        var count = 0;
        foreach (var window in windows)
        {
            User32.PostMessage(window, message, 0, 0);
            count++;
        }

        Graphics.DemoCapture.Log($"taskbarlist: aviso reenviado a {count} janelas");
    }

    private void Detach()
    {
        var tray = User32.FindWindow(TrayClass, null);
        if (tray != 0)
        {
            Restore(tray, _original);
        }

        _isAttached = false;
        TryDelete();
        var changed = _overlays.Keys.Concat(_progress.Keys).Distinct().ToList();
        _overlays.Clear();
        _progress.Clear();
        foreach (var window in changed)
        {
            Changed?.Invoke(this, window);
        }
    }

    /// <summary>
    /// Deixa a propriedade como estava: sem ela, se não existia (é o normal no Windows 11); com o
    /// valor original, se ele ainda existe; e, se ele sumiu (o Explorer reiniciou), com a janela da
    /// lista de tarefas da barra do Windows, se houver uma.
    /// </summary>
    private static void Restore(nint tray, nint original)
    {
        var target = original == 0 ? 0 : User32.IsWindow(original) ? original : FindTaskSwitcher(tray);
        if (target != 0)
        {
            User32.SetProp(tray, Property, target);
        }
        else
        {
            User32.RemoveProp(tray, Property);
        }
    }

    private static nint FindTaskSwitcher(nint tray)
    {
        var rebar = User32.FindWindowEx(tray, 0, "ReBarWindow32", null);
        return rebar == 0 ? 0 : User32.FindWindowEx(rebar, 0, "MSTaskSwWClass", null);
    }

    private static void TryDelete()
    {
        try
        {
            File.Delete(SavedPath);
        }
        catch (IOException)
        {
        }
    }

    private HwndSource CreateWindow()
    {
        // Janela só de mensagens: não aparece em lugar nenhum, só recebe o que os apps enviam.
        var window = new HwndSource(new HwndSourceParameters("LiquidGlass.TaskbarList") { ParentWindow = User32.HwndMessage, WindowStyle = 0 });
        window.AddHook(WndProc);
        return window;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {

        switch (message)
        {
            case SetOverlayIcon:
                if (lParam == 0)
                {
                    _overlays.Remove(wParam);
                }
                else if (ToImage(lParam) is { } icon)
                {
                    _overlays[wParam] = icon;
                }

                break;
            case SetProgressValue:
                var (state, _) = ProgressFor(wParam);
                _progress[wParam] = (state == TaskbarProgressState.None ? TaskbarProgressState.Normal : state, Math.Clamp(lParam / ProgressMaximum, 0, 1));
                break;
            case SetProgressState:
                var newState = (TaskbarProgressState)(int)lParam;
                if (newState == TaskbarProgressState.None)
                {
                    _progress.Remove(wParam);
                }
                else
                {
                    _progress[wParam] = (newState, ProgressFor(wParam).Value);
                }

                break;
            default:
                return 0;
        }

        handled = true;
        Changed?.Invoke(this, wParam);
        return 0;
    }

    /// <summary>O ícone é do app; a cópia é feita na hora, antes de ele destruí-lo.</summary>
    private static BitmapSource? ToImage(nint icon)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
