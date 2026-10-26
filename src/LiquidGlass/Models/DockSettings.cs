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

    [ObservableProperty]
    private bool _showStartButton = true;

    [ObservableProperty]
    private bool _showRecycleBin = true;

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

    // Comportamento
    [ObservableProperty]
    private bool _autoHide;

    [ObservableProperty]
    private TaskbarMode _taskbarMode = TaskbarMode.Hidden;

    [ObservableProperty]
    private bool _launchAtStartup;

    public ObservableCollection<PinnedApp> PinnedApps { get; set; } = [];

    /// <summary>Copia todos os valores visuais e de comportamento (não os apps fixados).</summary>
    public void CopyPreferencesFrom(DockSettings other)
    {
        Edge = other.Edge;
        IconSize = other.IconSize;
        IconSpacing = other.IconSpacing;
        DockLength = other.DockLength;
        EdgeMargin = other.EdgeMargin;
        Roundness = other.Roundness;
        ShowLabels = other.ShowLabels;
        ShowRunningIndicators = other.ShowRunningIndicators;
        ShowStartButton = other.ShowStartButton;
        ShowRecycleBin = other.ShowRecycleBin;
        Refraction = other.Refraction;
        Blur = other.Blur;
        ChromaticAberration = other.ChromaticAberration;
        Highlight = other.Highlight;
        Saturation = other.Saturation;
        TintColor = other.TintColor;
        TintStrength = other.TintStrength;
        Shadow = other.Shadow;
        MagnificationEnabled = other.MagnificationEnabled;
        Magnification = other.Magnification;
        MagnificationRange = other.MagnificationRange;
        BounceOnLaunch = other.BounceOnLaunch;
        BounceOnAttention = other.BounceOnAttention;
        AnimationSpeed = other.AnimationSpeed;
        AutoHide = other.AutoHide;
        TaskbarMode = other.TaskbarMode;
        LaunchAtStartup = other.LaunchAtStartup;
    }
}
