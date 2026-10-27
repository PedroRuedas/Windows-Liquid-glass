using LiquidGlass.Services;

namespace LiquidGlass.Views;

/// <summary>
/// Abre e fecha o cubo das áreas de trabalho (<see cref="WorkspaceCube"/>), na visão (Win+Tab) ou na
/// troca rápida (Ctrl+Win+setas). Cada uso cria um cubo novo, destruído ao terminar: fechado, ele não
/// ocupa memória.
/// </summary>
public sealed class WorkspaceOverview : IDisposable
{
    private readonly Func<OverviewContext> _createContext;
    private readonly GlobalInputHook _input;
    private readonly EfficiencyMode _efficiency;
    private readonly Action<bool> _keepDockVisible;
    private WorkspaceCube? _cube;

    /// <param name="keepDockVisible">Segura a dock na tela enquanto a visão está aberta.</param>
    public WorkspaceOverview(
        Func<OverviewContext> createContext,
        GlobalInputHook input,
        EfficiencyMode efficiency,
        Action<bool> keepDockVisible)
    {
        _createContext = createContext;
        _input = input;
        _efficiency = efficiency;
        _keepDockVisible = keepDockVisible;
    }

    public bool IsOpen => _cube is { IsOverviewOpen: true };

    /// <summary>O cubo em uso, se houver (para as demonstrações do Debug).</summary>
    internal WorkspaceCube? Current => _cube;

    public void Toggle()
    {
        if (IsOpen)
        {
            _cube!.ZoomIn();
        }
        else
        {
            Cube().OpenOverview();
        }

        UpdateCapture();
    }

    /// <summary>Fecha a visão (por exemplo, ao clicar num ícone da dock).</summary>
    public void Close()
    {
        if (IsOpen)
        {
            _cube!.ZoomIn();
            UpdateCapture();
        }
    }

    /// <summary>Ctrl+Win+seta: gira o cubo até a área vizinha, com ou sem a visão aberta.</summary>
    public void Switch(int direction)
    {
        Cube().Rotate(direction);
        UpdateCapture();
    }

    public void HandleKey(OverviewKey key)
    {
        _cube?.HandleKey(key);
        UpdateCapture();
    }

    public void Dispose() => _cube?.Close();

    private WorkspaceCube Cube()
    {
        if (_cube is null)
        {
            _cube = new WorkspaceCube(_createContext());
            _cube.Closed += OnCubeClosed;
            _efficiency.SetActive(this, active: true);
        }

        return _cube;
    }

    private void OnCubeClosed(object? sender, EventArgs e)
    {
        if (sender == _cube)
        {
            _cube = null;
            _efficiency.SetActive(this, active: false);
            UpdateCapture();
        }
    }

    /// <summary>Esc, setas e Enter só são desviados dos apps enquanto a visão está aberta.</summary>
    private void UpdateCapture()
    {
        _input.CaptureOverviewKeys = IsOpen;
        _keepDockVisible(IsOpen);
    }
}
