using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiquidGlass.Models;

namespace LiquidGlass.ViewModels;

/// <summary>Um ícone da dock: app fixado e/ou em execução, botão Iniciar, Lixeira ou separador.</summary>
public sealed partial class DockItemViewModel : ObservableObject
{
    private readonly DockViewModel _owner;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPinned))]
    private PinnedApp? _pin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private IReadOnlyList<AppWindow> _windows = [];

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private bool _needsAttention;

    [ObservableProperty]
    private bool _isRemoving;

    public DockItemViewModel(DockViewModel owner, DockItemKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    public DockItemKind Kind { get; }

    /// <summary>Quando o último pedido de abertura começou (para garantir ao menos um quique).</summary>
    public DateTime LaunchStartedAt { get; set; }

    /// <summary>Chave de agrupamento das janelas quando o item não está fixado.</summary>
    public string? GroupKey { get; set; }

    public bool IsApplication => Kind == DockItemKind.Application;

    public bool IsSeparator => Kind == DockItemKind.Separator;

    public bool IsPinned => Pin is not null;

    public bool IsRunning => Windows.Count > 0;

    [RelayCommand]
    private void Activate() => _owner.Activate(this);

    [RelayCommand]
    private void OpenNew() => _owner.OpenNew(this);

    [RelayCommand]
    private void TogglePin() => _owner.TogglePin(this);

    [RelayCommand]
    private void CloseAll() => DockViewModel.CloseAll(this);

    [RelayCommand]
    private void ActivateWindow(AppWindow window) => DockViewModel.ActivateWindow(this, window);
}
