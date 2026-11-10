namespace LiquidGlass.Models;

/// <summary>Borda da tela onde a dock fica ancorada.</summary>
public enum DockEdge
{
    Bottom,
    Left,
    Right,
}

/// <summary>Idioma da interface. Em Automático, segue o idioma do Windows.</summary>
public enum AppLanguage
{
    Automatic,
    Portuguese,
    English,
}

/// <summary>De onde vem o tom do vidro.</summary>
public enum GlassTintMode
{
    /// <summary>O tom e a intensidade escolhidos nas preferências.</summary>
    Custom,

    /// <summary>Claro ou escuro, acompanhando o modo do Windows.</summary>
    WindowsTheme,

    /// <summary>A cor de destaque do Windows.</summary>
    Accent,
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
    TrayButton,
    Separator,
    Battery,
    WorkspacesButton,
    Network,
}
