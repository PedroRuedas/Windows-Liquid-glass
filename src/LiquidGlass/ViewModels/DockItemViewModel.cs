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

    /// <summary>O ícone que o próprio app tem, para voltar a ele quando o personalizado é removido.</summary>
    public ImageSource? DefaultIcon { get; set; }

    /// <summary>
    /// Identifica o app para o ícone personalizado, igual fixado ou só aberto: o AppUserModelId ou o
    /// executável (a mesma chave que agrupa as janelas) e, sem eles, o alvo do atalho.
    /// </summary>
    public string? IconKey => Pin?.AppUserModelId ?? Pin?.ExecutablePath ?? Pin?.Target ?? GroupKey;

    [ObservableProperty]
    private bool _hasCustomIcon;

    /// <summary>O selo que o app pôs no ícone da barra de tarefas (mensagens não lidas, por exemplo).</summary>
    [ObservableProperty]
    private ImageSource? _badge;

    /// <summary>Barra de progresso do app (download, cópia); None quando não há.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private Services.TaskbarProgressState _progressState;

    /// <summary>De 0 a 1.</summary>
    [ObservableProperty]
    private double _progress;

    public bool HasProgress => ProgressState != Services.TaskbarProgressState.None;

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
    private void CloseAll() => _owner.CloseAll(this);

    [RelayCommand]
    private void ActivateWindow(AppWindow window) => _owner.ActivateWindow(this, window);

    [RelayCommand]
    private void ChangeIcon() => _owner.ChangeIcon(this);

    [RelayCommand]
    private void ResetIcon() => _owner.ResetIcon(this);
}
