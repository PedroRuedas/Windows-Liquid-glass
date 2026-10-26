namespace LiquidGlass.Models;

/// <summary>Borda da tela onde a dock fica ancorada.</summary>
public enum DockEdge
{
    Bottom,
    Left,
    Right,
}

/// <summary>O que fazer com a barra de tarefas nativa enquanto a dock está ativa.</summary>
public enum TaskbarMode
{
    Visible,
    AutoHide,
    Hidden,
}

/// <summary>Tipos de item exibidos na dock.</summary>
public enum DockItemKind
{
    Application,
    StartButton,
    RecycleBin,
    Separator,
}
