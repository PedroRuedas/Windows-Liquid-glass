using System.Text.Json;
using System.Windows.Threading;
using LiquidGlass.Interop;
using LiquidGlass.Models;

namespace LiquidGlass.Services;

/// <summary>
/// Esconde ou mostra a barra de tarefas nativa e sempre devolve o estado original ao sair.
/// O estado original fica salvo em disco para ser restaurado mesmo depois de um encerramento forçado.
/// </summary>
public sealed class TaskbarController : IDisposable
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

    private readonly DispatcherTimer _enforceTimer;
    private TaskbarMode _mode = TaskbarMode.Visible;

    public TaskbarController()
    {
        RecoverFromPreviousSession();

        // O Explorer volta a mostrar a barra em algumas situações (tecla Windows, reinício do shell).
        _enforceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _enforceTimer.Tick += (_, _) => EnforceHidden();
    }

    public void Apply(TaskbarMode mode)
    {
        _mode = mode;
        if (mode == TaskbarMode.Visible)
        {
            Restore();
            return;
        }

        SaveOriginalState();
        SetAutoHide(true);
        SetTaskbarsVisible(mode != TaskbarMode.Hidden);

        if (mode == TaskbarMode.Hidden)
        {
            _enforceTimer.Start();
        }
        else
        {
            _enforceTimer.Stop();
        }
    }

    /// <summary>Reaplica o modo atual (por exemplo, depois que o Explorer reinicia).</summary>
    public void Reapply() => Apply(_mode);

    /// <summary>Devolve a barra de tarefas exatamente como estava antes do app.</summary>
    public void Restore()
    {
        _enforceTimer.Stop();
        var state = LoadOriginalState();
        if (state is null)
        {
            return;
        }

        SetAutoHide(state.AutoHide);
        SetTaskbarsVisible(true);
        File.Delete(AppDataPaths.TaskbarState);
    }

    public void Dispose() => Restore();

    private static void RecoverFromPreviousSession()
    {
        var state = LoadOriginalState();
        if (state is not null)
        {
            SetAutoHide(state.AutoHide);
            SetTaskbarsVisible(true);
            File.Delete(AppDataPaths.TaskbarState);
        }
    }

    private static void SaveOriginalState()
    {
        if (File.Exists(AppDataPaths.TaskbarState))
        {
            return;
        }

        var state = new TaskbarState(IsAutoHideEnabled());
        AppDataPaths.EnsureRoot();
        File.WriteAllText(AppDataPaths.TaskbarState, JsonSerializer.Serialize(state));
    }

    private static TaskbarState? LoadOriginalState()
    {
        try
        {
            return File.Exists(AppDataPaths.TaskbarState)
                ? JsonSerializer.Deserialize<TaskbarState>(File.ReadAllText(AppDataPaths.TaskbarState))
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private static bool IsAutoHideEnabled()
    {
        var data = Shell32.CreateAppBarData(FindPrimaryTaskbar());
        var state = (int)Shell32.SHAppBarMessage(Shell32.AbmGetState, ref data);
        return (state & Shell32.AbsAutoHide) != 0;
    }

    private static void SetAutoHide(bool enabled)
    {
        var data = Shell32.CreateAppBarData(FindPrimaryTaskbar());
        data.Param = enabled ? Shell32.AbsAutoHide : Shell32.AbsAlwaysOnTop;
        Shell32.SHAppBarMessage(Shell32.AbmSetState, ref data);
    }

    private static void SetTaskbarsVisible(bool visible)
    {
        var command = visible ? User32.SwShowNoActivate : User32.SwHide;
        foreach (var taskbar in FindTaskbars())
        {
            User32.ShowWindow(taskbar, command);
        }
    }

    private static IEnumerable<nint> FindTaskbars()
    {
        var primary = FindPrimaryTaskbar();
        if (primary != 0)
        {
            yield return primary;
        }

        nint secondary = 0;
        while ((secondary = User32.FindWindowEx(0, secondary, SecondaryTaskbarClass, null)) != 0)
        {
            yield return secondary;
        }
    }

    /// <summary>
    /// A barra do Explorer. O serviço de bandeja da dock também cria uma janela "Shell_TrayWnd"
    /// (para receber os ícones), então ignoramos as janelas deste processo.
    /// </summary>
    private static nint FindPrimaryTaskbar()
    {
        nint window = 0;
        while ((window = User32.FindWindowEx(0, window, PrimaryTaskbarClass, null)) != 0)
        {
            User32.GetWindowThreadProcessId(window, out var processId);
            if (processId != Environment.ProcessId)
            {
                return window;
            }
        }

        return 0;
    }

    private void EnforceHidden()
    {
        if (_mode == TaskbarMode.Hidden && FindTaskbars().Any(User32.IsWindowVisible))
        {
            SetTaskbarsVisible(false);
        }
    }

    private sealed record TaskbarState(bool AutoHide);
}
