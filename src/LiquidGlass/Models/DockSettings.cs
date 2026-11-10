using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiquidGlass.Models;

/// <summary>
/// Todas as preferências da dock. Cada alteração é aplicada ao vivo e salva automaticamente.
/// </summary>
public sealed partial class DockSettings : ObservableObject
{
    // Layout
    [ObservableProperty]
    private DockEdge _edge = DockEdge.Bottom;

    [ObservableProperty]
    private double _iconSize = 44;

    /// <summary>Uma dock em cada monitor, todas com os mesmos apps (como a barra do Windows em todas as telas).</summary>
    [ObservableProperty]
    private bool _showOnAllMonitors;

    [ObservableProperty]
    private double _iconSpacing = 16;

    /// <summary>Comprimento mínimo do vidro, em fração da borda da tela (0 = só envolve os ícones).</summary>
    [ObservableProperty]
    private double _dockLength = 0.5;

    [ObservableProperty]
    private double _edgeMargin = 8;

    [ObservableProperty]
    private double _roundness = 1.0;

    [ObservableProperty]
    private bool _showLabels = true;

    [ObservableProperty]
    private bool _showRunningIndicators = true;

    /// <summary>Miniaturas das janelas do app ao parar o mouse sobre o ícone, para alternar entre elas.</summary>
    [ObservableProperty]
    private bool _showWindowPreviews = true;

    [ObservableProperty]
    private bool _showStartButton = true;

    [ObservableProperty]
    private bool _showRecycleBin = true;

    [ObservableProperty]
    private bool _showTray = true;

    /// <summary>Só aparece em computadores com bateria.</summary>
    [ObservableProperty]
    private bool _showBattery = true;

    [ObservableProperty]
    private bool _showWorkspacesButton = true;

    [ObservableProperty]
    private bool _showNetwork = true;

    // Vidro
    [ObservableProperty]
    private double _refraction = 0.7;

    [ObservableProperty]
    private double _blur = 6;

    [ObservableProperty]
    private double _chromaticAberration = 0.35;

    [ObservableProperty]
    private double _highlight = 0.8;

    [ObservableProperty]
    private double _saturation = 1.25;

    [ObservableProperty]
    private Color _tintColor = Color.FromRgb(0xFF, 0xFF, 0xFF);

    [ObservableProperty]
    private double _tintStrength = 0.14;

    /// <summary>O vidro pode seguir o modo claro/escuro ou a cor de destaque do Windows.</summary>
    [ObservableProperty]
    private GlassTintMode _tintMode = GlassTintMode.Custom;

    [ObservableProperty]
    private double _shadow = 0.35;

    // Animações
    [ObservableProperty]
    private bool _magnificationEnabled = true;

    [ObservableProperty]
    private double _magnification = 1.7;

    [ObservableProperty]
    private double _magnificationRange = 3;

    [ObservableProperty]
    private bool _bounceOnLaunch = true;

    [ObservableProperty]
    private bool _bounceOnAttention = true;

    [ObservableProperty]
    private double _animationSpeed = 1.0;

    /// <summary>Fechar e minimizar no estilo do GNOME, no lugar das animações do Windows.</summary>
    [ObservableProperty]
    private bool _animateWindows = true;

    /// <summary>Ctrl+Win+setas giram o cubo das áreas de trabalho.</summary>
    [ObservableProperty]
    private bool _animateWorkspaceSwitch = true;

    // Comportamento
    [ObservableProperty]
    private bool _autoHide;

    [ObservableProperty]
    private TaskbarMode _taskbarMode = TaskbarMode.Hidden;

    [ObservableProperty]
    private bool _launchAtStartup;

    /// <summary>Win+Tab abre o cubo das áreas de trabalho em vez da Visão de Tarefas.</summary>
    [ObservableProperty]
    private bool _winTabOpensOverview = true;

    /// <summary>Selos (mensagens não lidas) e barras de progresso que os apps mandam para a barra de tarefas.</summary>
    [ObservableProperty]
    private bool _showBadges = true;

    /// <summary>Win+1…9 abrem os apps da dock, na ordem em que aparecem.</summary>
    [ObservableProperty]
    private bool _winNumberKeys = true;

    /// <summary>O botão Iniciar e a tecla Windows abrem o menu Iniciar de vidro no lugar do menu do Windows.</summary>
    [ObservableProperty]
    private bool _useGlassStartMenu = true;

    /// <summary>Os fixados do menu Iniciar de vidro já foram trazidos do menu do Windows (só na primeira vez).</summary>
    [ObservableProperty]
    private bool _startPinsImported;

    /// <summary>Procura versões novas ao abrir a dock e a cada poucas horas.</summary>
    [ObservableProperty]
    private bool _checkForUpdates = true;

    /// <summary>
    /// Idioma da interface; vale ao abrir a dock. Fica fora de <see cref="CopyPreferencesFrom"/>:
    /// "Restaurar padrões" não deve trocar o idioma de quem o escolheu.
    /// </summary>
    [ObservableProperty]
    private AppLanguage _language = AppLanguage.Automatic;

    /// <summary>
    /// Ordem dos apps abertos não fixados, arrastada pelo usuário (veja <see cref="Services.RunningOrder"/>).
    /// Fora de <see cref="CopyPreferencesFrom"/>, como os fixados.
    /// </summary>
    [ObservableProperty]
    private List<string> _runningOrder = [];

    /// <summary>
    /// Ícones escolhidos pelo usuário: a chave é a do app (<see cref="ViewModels.DockItemViewModel.IconKey"/>)
    /// e o valor, a cópia da imagem em %AppData%\LiquidGlass\icons. Trocado inteiro a cada mudança, para salvar.
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, string> _customIcons = [];

    public ObservableCollection<PinnedApp> PinnedApps { get; set; } = [];

    /// <summary>Apps fixados no menu Iniciar de vidro, na ordem da grade.</summary>
    public ObservableCollection<PinnedApp> StartPins { get; set; } = [];

    /// <summary>
    /// Folga do vidro em volta dos ícones, em DIPs. Fica em 7 a partir de 32 px e cresce com ícones
    /// grandes; com ícones menores encolhe junto, para a dock poder ficar bem fina.
    /// </summary>
    public static double GlassPaddingFor(double iconSize) => Math.Max(Math.Min(7, iconSize * 0.22), iconSize * 0.16);

    /// <summary>Copia todos os valores visuais e de comportamento (não os apps fixados).</summary>
    public void CopyPreferencesFrom(DockSettings other)
    {
        Edge = other.Edge;
        IconSize = other.IconSize;
        ShowOnAllMonitors = other.ShowOnAllMonitors;
        IconSpacing = other.IconSpacing;
        DockLength = other.DockLength;
        EdgeMargin = other.EdgeMargin;
        Roundness = other.Roundness;
        ShowLabels = other.ShowLabels;
        ShowRunningIndicators = other.ShowRunningIndicators;
        ShowWindowPreviews = other.ShowWindowPreviews;
        ShowStartButton = other.ShowStartButton;
        ShowRecycleBin = other.ShowRecycleBin;
        ShowTray = other.ShowTray;
        ShowBattery = other.ShowBattery;
        ShowWorkspacesButton = other.ShowWorkspacesButton;
        ShowNetwork = other.ShowNetwork;
        Refraction = other.Refraction;
        Blur = other.Blur;
        ChromaticAberration = other.ChromaticAberration;
        Highlight = other.Highlight;
        Saturation = other.Saturation;
        TintColor = other.TintColor;
        TintStrength = other.TintStrength;
        TintMode = other.TintMode;
        Shadow = other.Shadow;
        MagnificationEnabled = other.MagnificationEnabled;
        Magnification = other.Magnification;
        MagnificationRange = other.MagnificationRange;
        BounceOnLaunch = other.BounceOnLaunch;
        BounceOnAttention = other.BounceOnAttention;
        AnimationSpeed = other.AnimationSpeed;
        AnimateWindows = other.AnimateWindows;
        AnimateWorkspaceSwitch = other.AnimateWorkspaceSwitch;
        AutoHide = other.AutoHide;
        TaskbarMode = other.TaskbarMode;
        LaunchAtStartup = other.LaunchAtStartup;
        WinTabOpensOverview = other.WinTabOpensOverview;
        WinNumberKeys = other.WinNumberKeys;
        ShowBadges = other.ShowBadges;
        UseGlassStartMenu = other.UseGlassStartMenu;
        CheckForUpdates = other.CheckForUpdates;
    }
}
