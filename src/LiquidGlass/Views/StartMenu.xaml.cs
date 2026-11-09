using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LiquidGlass.Controls;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Services;

namespace LiquidGlass.Views;

/// <summary>
/// Menu Iniciar de vidro: pesquisa (apps, Configurações e arquivos), apps fixados e arquivos
/// recentes. Abre junto ao botão Iniciar da dock, pelo botão ou pela tecla Windows, e fecha ao
/// clicar fora, com Esc ou abrindo algo. Diferente dos outros painéis da dock, recebe o foco do
/// teclado: é preciso digitar na pesquisa.
/// </summary>
public partial class StartMenu : Window, IDisposable
{
    private const int RecentCount = 6;
    private const int AppResults = 6;
    private const int SettingResults = 3;
    private const int FileResults = 5;
    private const int IconPixels = 64;
    private const double ShadowPadding = 14;
    private const double Gap = 6;
    private const double ScreenMargin = 8;

    /// <summary>A lista de apps instalados é refeita em segundo plano quando ficou velha.</summary>
    private static readonly TimeSpan AppsRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly DockSettings _settings;
    private readonly EfficiencyMode _efficiency;
    private readonly ScreenBackdrop _backdrop = new();
    private readonly StaWorker _worker = new("Menu Iniciar");
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<StartEntry>> _iconsLoading = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<StartEntry> _pins = [];
    private readonly ObservableCollection<StartEntry> _recent = [];
    private readonly ObservableCollection<StartEntry> _results = [];
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private List<StartEntry> _apps = [];
    private Task<List<StartEntry>>? _appsLoading;
    private DateTime _appsLoadedAt = DateTime.MinValue;
    private Task? _importing;
    private CancellationTokenSource? _fileSearch;
    private int _searchVersion;
    private DockEdge _edge;
    private bool _isClosing;

    public StartMenu(DockSettings settings, EfficiencyMode efficiency)
    {
        InitializeComponent();
        _settings = settings;
        _efficiency = efficiency;

        // Fundo próprio: um menu do botão direito aberto por cima recaptura o compartilhado.
        Glass.SetBinding(LiquidGlassSurface.BackdropProperty, new Binding(nameof(ScreenBackdrop.Image)) { Source = _backdrop });
        Glass.SetBinding(LiquidGlassSurface.BackdropOriginProperty, new Binding(nameof(ScreenBackdrop.Origin)) { Source = _backdrop });

        Pins.ItemsSource = _pins;
        Recent.ItemsSource = _recent;
        var results = new ListCollectionView(_results);
        results.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StartEntry.Group)));
        Results.ItemsSource = results;

        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            _ = SearchAsync();
        };
        _settings.StartPins.CollectionChanged += OnStartPinsChanged;
        SourceInitialized += (_, _) => User32.AddExtendedStyle(new WindowInteropHelper(this).Handle, User32.WsExToolWindow);
        Deactivated += (_, _) => CloseAnimated();
        Closed += (_, _) =>
        {
            _settings.StartPins.CollectionChanged -= OnStartPinsChanged;
            _fileSearch?.Cancel();
            Dispose();
        };

        // Prepara em segundo plano o que a primeira abertura vai mostrar.
        _ = LoadUserNameAsync();
        _importing = ImportPinsOnceAsync();
        RefreshPins();
        EnsureApps();
    }

    /// <summary>Encerra a thread de fundo; chamado ao fechar a janela.</summary>
    public void Dispose()
    {
        _worker.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Aberto e não fechando.</summary>
    public bool IsMenuOpen => IsVisible && !_isClosing;

    /// <summary>Abre junto ao <paramref name="anchor"/> (o botão Iniciar ou a dock) ou fecha se já estiver aberto.</summary>
    public void Toggle(FrameworkElement anchor, DockEdge edge)
    {
        if (IsMenuOpen)
        {
            CloseAnimated();
        }
        else
        {
            Open(anchor, edge);
        }
    }

    public void Open(FrameworkElement anchor, DockEdge edge)
    {
        if (_isClosing)
        {
            // Reabrindo durante o recolhimento: some de vez antes de capturar o fundo de novo.
            Hide();
            _isClosing = false;
        }

        _edge = edge;
        SearchBox.Text = string.Empty;
        ShowHome();

        var region = Place(anchor, edge);
        _backdrop.CaptureRegion(region);

        RefreshPins();
        _ = LoadRecentAsync();
        EnsureApps();

        _efficiency.SetActive(this, active: true);
        Show();
        TakeFocus();
        DockPopupAnimation.Open(this, PopupContent, edge);
    }

    /// <summary>Fecha encolhendo de volta para a dock.</summary>
    public void CloseAnimated()
    {
        if (!IsVisible || _isClosing)
        {
            return;
        }

        _isClosing = true;
        _searchDelay.Stop();
        _fileSearch?.Cancel();
        DockPopupAnimation.Collapse(PopupContent, _edge, () =>
        {
            if (_isClosing)
            {
                Hide();
                _isClosing = false;
                _efficiency.SetActive(this, active: false);
            }
        });
    }

    /// <summary>Sem um pai que o deixe, o Windows só dá o foco a quem recebeu a última entrada; preso à thread dele, pode.</summary>
    private void TakeFocus()
    {
        Activate();
        var handle = new WindowInteropHelper(this).Handle;
        var foreground = User32.GetForegroundWindow();
        if (foreground != handle && foreground != 0)
        {
            var other = User32.GetWindowThreadProcessId(foreground, out _);
            var mine = Kernel32.GetCurrentThreadId();
            if (other != mine && User32.AttachThreadInput(mine, other, true))
            {
                try
                {
                    User32.BringWindowToTop(handle);
                    User32.SetForegroundWindow(handle);
                }
                finally
                {
                    User32.AttachThreadInput(mine, other, false);
                }
            }

            Activate();
        }

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>
    /// Posiciona a janela do lado da dock voltado para o meio da tela, centralizada na âncora e
    /// dentro da área de trabalho. Devolve o retângulo ocupado, em pixels físicos.
    /// </summary>
    private Rect Place(FrameworkElement anchor, DockEdge edge)
    {
        var scale = VisualTreeHelper.GetDpi(anchor).DpiScaleX;
        var topLeft = anchor.PointToScreen(new Point(0, 0));
        var bottomRight = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
        var bounds = new Rect(topLeft, bottomRight);
        var work = User32.GetPrimaryMonitorInfo().WorkArea;
        var (width, height) = (Width * scale, Height * scale);
        var (shadow, gap, margin) = (ShadowPadding * scale, Gap * scale, ScreenMargin * scale);
        var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        var (x, y) = edge switch
        {
            DockEdge.Left => (bounds.Right + gap - shadow, center.Y - (height / 2)),
            DockEdge.Right => (bounds.Left - gap - width + shadow, center.Y - (height / 2)),
            _ => (center.X - (width / 2), bounds.Top - gap - height + shadow),
        };

        x = Math.Clamp(x, work.Left + margin - shadow, Math.Max(work.Left, work.Right - margin - width + shadow));
        y = Math.Clamp(y, work.Top + margin - shadow, Math.Max(work.Top, work.Bottom - margin - height + shadow));
        Left = x / scale;
        Top = y / scale;
        return new Rect(x, y, width, height);
    }

    private async Task ImportPinsOnceAsync()
    {
        if (_settings.StartPinsImported)
        {
            return;
        }

        var imported = await _worker.Run(StartMenuCatalog.ImportWindowsPins);
        if (imported.Count == 0)
        {
            imported = [.. PinnedAppFactory.Defaults()];
        }

        // A grade é refeita uma vez só, no fim, e não a cada app trazido.
        _settings.StartPins.CollectionChanged -= OnStartPinsChanged;
        foreach (var pin in imported)
        {
            _settings.StartPins.Add(pin);
        }

        _settings.StartPins.CollectionChanged += OnStartPinsChanged;
        _settings.StartPinsImported = true;
        RefreshPins();
    }

    private void EnsureApps()
    {
        if (_appsLoading is { IsCompleted: false } || DateTime.Now - _appsLoadedAt < AppsRefreshInterval)
        {
            return;
        }

        _appsLoading = _worker.Run(StartMenuCatalog.LoadApps);
        _appsLoading.ContinueWith(
            loaded =>
            {
                if (loaded.IsCompletedSuccessfully)
                {
                    _apps = loaded.Result;
                    _appsLoadedAt = DateTime.Now;
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnStartPinsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshPins();

    private void RefreshPins()
    {
        _pins.Clear();
        foreach (var pin in _settings.StartPins)
        {
            _pins.Add(WithIcon(StartMenuCatalog.ToEntry(pin)));
        }

        PinsEmpty.Visibility = _pins.Count == 0 && _importing is { IsCompleted: true } ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadRecentAsync()
    {
        var recent = await _worker.Run(() => StartMenuCatalog.LoadRecent(RecentCount, DateTime.Now));
        _recent.Clear();
        foreach (var entry in recent)
        {
            _recent.Add(WithIcon(entry));
        }

        RecentEmpty.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Põe o ícone no item: na hora, se já foi carregado; senão, quando a thread de fundo o trouxer.</summary>
    private StartEntry WithIcon(StartEntry entry)
    {
        if (_icons.TryGetValue(entry.IconSource, out var cached))
        {
            entry.Icon = cached;
            return entry;
        }

        // Um carregamento por ícone: quem pedir o mesmo enquanto ele vem entra na espera.
        if (_iconsLoading.TryGetValue(entry.IconSource, out var waiting))
        {
            waiting.Add(entry);
            return entry;
        }

        _iconsLoading[entry.IconSource] = [entry];
        _ = LoadIconAsync(entry.IconSource);
        return entry;
    }

    private async Task LoadIconAsync(string source)
    {
        var icon = await _worker.Run<ImageSource?>(() => IconService.LoadIcon(source, IconPixels));
        _icons[source] = icon;
        if (_iconsLoading.Remove(source, out var waiting))
        {
            foreach (var entry in waiting)
            {
                entry.Icon = icon;
            }
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _searchDelay.Stop();
        _fileSearch?.Cancel();
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            _searchVersion++;
            ShowHome();
            return;
        }

        _searchDelay.Start();
    }

    private void ShowHome()
    {
        _results.Clear();
        HomePanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Apps e Configurações aparecem na hora (ficam na memória); os arquivos vêm do índice do
    /// Windows numa thread própria e entram depois, se a pesquisa ainda for a mesma.
    /// </summary>
    private async Task SearchAsync()
    {
        var query = SearchBox.Text.Trim();
        var version = ++_searchVersion;
        if (_apps.Count == 0 && _appsLoading is { } loading)
        {
            await loading.ContinueWith(_ => { }, TaskScheduler.Default);
            if (version != _searchVersion)
            {
                return;
            }
        }

        _results.Clear();
        foreach (var entry in StartMenuCatalog.SearchApps(_apps, query, AppResults).Concat(StartMenuCatalog.SearchSettings(query, SettingResults)))
        {
            _results.Add(WithIcon(entry));
        }

        HomePanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
        Results.SelectedIndex = _results.Count > 0 ? 0 : -1;
        UpdateNoResults(query, searching: true);

        _fileSearch?.Cancel();
        var cancel = new CancellationTokenSource();
        _fileSearch = cancel;
        var files = await RunOnNewSta(() => StartMenuCatalog.SearchFiles(query, FileResults, cancel.Token));
        if (version != _searchVersion || cancel.IsCancellationRequested)
        {
            return;
        }

        foreach (var file in files)
        {
            _results.Add(WithIcon(file));
        }

        if (Results.SelectedIndex < 0 && _results.Count > 0)
        {
            Results.SelectedIndex = 0;
        }

        UpdateNoResults(query, searching: false);
    }

    private void UpdateNoResults(string query, bool searching)
    {
        NoResults.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Text = searching ? "Procurando…" : $"Nada encontrado para \"{query}\".";
    }

    /// <summary>
    /// A pesquisa de arquivos pode demorar e não se deixa interromper no meio: cada uma tem a
    /// própria thread, para não segurar ícones e recentes na fila da thread de fundo.
    /// </summary>
    private static Task<T> RunOnNewSta<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        { IsBackground = true, Name = "Pesquisa de arquivos", Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when SearchBox.Text.Length > 0:
                SearchBox.Text = string.Empty;
                e.Handled = true;
                break;
            case Key.Escape:
                CloseAnimated();
                e.Handled = true;
                break;
            case Key.Down or Key.Up when ResultsPanel.IsVisible && _results.Count > 0:
                var next = Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                Results.SelectedIndex = Math.Clamp(next, 0, _results.Count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter when ResultsPanel.IsVisible:
                if (Results.SelectedItem is StartEntry selected)
                {
                    Launch(selected);
                }

                e.Handled = true;
                break;
        }
    }

    private void OnEntryClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StartEntry entry })
        {
            Launch(entry);
            e.Handled = true;
        }
    }

    private void Launch(StartEntry entry)
    {
        CloseAnimated();
        if (!AppLauncher.Launch(entry.Target))
        {
            MessageBox.Show($"Não foi possível abrir \"{entry.Name}\".", "Liquid Glass Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnEntryMenu(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StartEntry entry } element)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = element, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(MenuItem("Abrir", () => Launch(entry)));
        var pinIndex = IndexOfPin(entry);
        if (entry.Kind == StartEntryKind.App)
        {
            if (pinIndex >= 0)
            {
                if (pinIndex > 0)
                {
                    menu.Items.Add(MenuItem("Mover para o início", () => _settings.StartPins.Move(pinIndex, 0)));
                }

                menu.Items.Add(MenuItem("Desafixar do Iniciar", () => _settings.StartPins.RemoveAt(pinIndex)));
            }
            else
            {
                menu.Items.Add(MenuItem("Fixar no Iniciar", () => _settings.StartPins.Add(StartMenuCatalog.ToPin(entry))));
            }
        }
        else if (entry.Kind == StartEntryKind.File)
        {
            menu.Items.Add(MenuItem("Abrir local do arquivo", () =>
            {
                CloseAnimated();
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.Target}\"") { UseShellExecute = true })?.Dispose();
            }));
        }

        ScreenBackdrop.Shared.CaptureAroundCursor();
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Numa conta de domínio o nome pode demorar a vir: sai da thread da interface.</summary>
    private async Task LoadUserNameAsync()
    {
        UserName.Text = Environment.UserName;
        UserName.Text = await _worker.Run(PowerActions.UserDisplayName);
    }

    private void OnPowerClick(object sender, MouseButtonEventArgs e)
    {
        OpenPowerMenu(atMouse: true);
        e.Handled = true;
    }

    /// <summary>Menu do botão de energia: a sessão primeiro (bloquear, sair), depois a energia.</summary>
    private ContextMenu OpenPowerMenu(bool atMouse)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = PowerButton,
            Placement = atMouse ? System.Windows.Controls.Primitives.PlacementMode.MousePoint : System.Windows.Controls.Primitives.PlacementMode.Top,
        };
        menu.Items.Add(PowerItem("", "Bloquear", PowerActions.Lock));
        menu.Items.Add(PowerItem("", "Sair", PowerActions.SignOut));
        menu.Items.Add(new Separator());
        menu.Items.Add(PowerItem("", "Suspender", PowerActions.Sleep));
        if (PowerActions.CanHibernate)
        {
            menu.Items.Add(PowerItem("", "Hibernar", PowerActions.Hibernate));
        }

        menu.Items.Add(PowerItem("", "Desligar", PowerActions.Shutdown));
        menu.Items.Add(PowerItem("", "Reiniciar", PowerActions.Restart));

        ScreenBackdrop.Shared.CaptureAroundCursor();
        menu.IsOpen = true;
        return menu;
    }

    private MenuItem PowerItem(string glyph, string text, Func<bool> action)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Width = 26,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 14,
            Text = glyph,
        });
        header.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        return MenuItem(header, () =>
        {
            CloseAnimated();
            if (!action())
            {
                MessageBox.Show($"O Windows não deixou \"{text}\" agora.", "Liquid Glass Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private int IndexOfPin(StartEntry entry)
    {
        for (var index = 0; index < _settings.StartPins.Count; index++)
        {
            if (string.Equals(_settings.StartPins[index].Target, entry.Target, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static MenuItem MenuItem(object header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
