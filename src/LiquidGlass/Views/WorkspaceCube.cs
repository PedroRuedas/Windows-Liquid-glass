using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using LiquidGlass.Controls;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;
using LiquidGlass.Services;

namespace LiquidGlass.Views;

/// <summary>O que o cubo das áreas de trabalho precisa do resto da dock.</summary>
/// <param name="Reserve">Espaço da dock nas bordas do monitor, em pixels físicos.</param>
public sealed record OverviewContext(
    VirtualDesktopService Desktops,
    WindowTracker Tracker,
    WallpaperService Wallpaper,
    nint DockHandle,
    Thickness Reserve,
    double DpiScale,
    double AnimationSpeed);

/// <summary>
/// As áreas de trabalho num cubo 3D, como a extensão Desktop Cube do GNOME (Fedora): cada área é uma
/// face, todas em volta de um círculo, com as janelas flutuando à frente do papel de parede.
/// <list type="bullet">
/// <item>Visão (Win+Tab ou o botão da dock): a câmera se afasta e o cubo aparece. Arrastar com o mouse
/// gira o cubo livremente (e inclina na vertical); ao soltar, ele encaixa na área mais próxima,
/// levando em conta o embalo. Clique numa face para entrar nela; setas e roda do mouse giram uma face.</item>
/// <item>Troca rápida (Ctrl+Win+setas): o cubo gira até a área vizinha, recuando um pouco no meio.</item>
/// </list>
/// As faces são retratos tirados ao abrir: a área atual vem da tela; as outras, das próprias janelas
/// (que o Windows desenha mesmo escondidas). A janela só existe durante o uso e leva os retratos com ela.
/// </summary>
public sealed class WorkspaceCube : Window
{
    private const double FieldOfView = 60;
    private const double OverviewZoom = 2.05;
    private const double OverviewPitch = 12;
    private const double QuickPeak = 0.55;
    private const double WindowLift = 0.2;
    private const double DragThreshold = 6;
    private const double DragDegreesPerScreen = 220;
    private const int MinSides = 4;

    /// <summary>Afastamento mínimo das janelas: coplanares à face, as duas camadas piscariam.</summary>
    private const double LayerEpsilon = 0.0008;

    private static readonly TimeSpan ZoomDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SnapDuration = TimeSpan.FromMilliseconds(520);
    private static readonly TimeSpan QuickDuration = TimeSpan.FromMilliseconds(820);
    private static readonly TimeSpan VelocityWindow = TimeSpan.FromMilliseconds(90);

    /// <summary>
    /// Espera depois de o cubo cobrir a tela antes de trocar a área de verdade: a janela leva alguns
    /// quadros para aparecer, e trocar antes deixaria a área nova piscar na tela.
    /// </summary>
    private static readonly TimeSpan SwitchDelay = TimeSpan.FromMilliseconds(90);
    private static readonly Brush DotBrush = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    private readonly OverviewContext _context;
    private readonly User32.Rect _monitor;
    private readonly List<Guid> _desktops;
    private readonly int _sides;
    private readonly double _apothem;
    private readonly double _halfHeight;
    private readonly double _viewDistance;
    private readonly PerspectiveCamera _camera = new()
    {
        FieldOfView = FieldOfView,
        LookDirection = new Vector3D(0, 0, -1),
        UpDirection = new Vector3D(0, 1, 0),
    };

    private readonly AxisAngleRotation3D _yawRotation = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _pitchRotation = new(new Vector3D(1, 0, 0), 0);
    private readonly AmbientLight _ambient = new(Colors.White);
    private readonly DirectionalLight _keyLight = new(Colors.Black, new Vector3D(0, -0.15, -1));
    private readonly DiffuseMaterial _baseMaterial = new(Brushes.Black);
    private readonly Model3DGroup _layers = new();
    private readonly List<Face> _faces = [];
    private readonly Dictionary<Model3D, Face> _faceOfModel = [];
    private readonly Viewport3D _viewport = new();
    private readonly Grid _root = new() { Background = Brushes.Black };
    private readonly Grid _pill;
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new()
    {
        Foreground = Brushes.White,
        FontFamily = TextFont,
        FontSize = 13,
        Margin = new Thickness(0, 7, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private readonly Tween _progress = new();
    private readonly Tween _yaw = new();
    private readonly Queue<(TimeSpan Time, double X)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private HwndSource? _source;
    private Action? _onShown;
    private bool _isShownOnScreen;
    private TimeSpan _shownAt;
    private bool _isSwitchPending;
    private bool _isClosing;
    private bool _isClosed;
    private bool _isRendering;
    private int _currentDesktop;
    private int _labelFace = -1;
    private int[] _layerOrder = [];
    private double _pitch = OverviewPitch;
    private bool _isPressed;
    private bool _isDragging;
    private Point _pressPoint;
    private double _dragYaw;
    private double _dragPitch;

    public WorkspaceCube(OverviewContext context)
    {
        _context = context;
        _monitor = User32.GetPrimaryMonitorInfo().Monitor;
        var current = context.Desktops.GetCurrent() ?? Guid.Empty;
        _desktops = [.. context.Desktops.GetDesktops()];
        if (!_desktops.Contains(current))
        {
            _desktops = [current];
        }

        _currentDesktop = _desktops.IndexOf(current);
        _sides = Math.Max(_desktops.Count, MinSides);
        _apothem = 1 / Math.Tan(Math.PI / _sides);
        _halfHeight = (double)_monitor.Height / Math.Max(_monitor.Width, 1);
        _viewDistance = 1 / Math.Tan(FieldOfView / 2 * Math.PI / 180);
        _yaw.Set(FaceYaw(_currentDesktop));

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        Title = "Áreas de trabalho";

        _pill = CreatePill();
        BuildScene();
        LoadTextures();
        Content = _root;

        _root.MouseLeftButtonDown += OnMouseDown;
        _root.MouseMove += OnMouseMove;
        _root.MouseLeftButtonUp += OnMouseUp;
        _root.MouseWheel += OnMouseWheel;
        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
    }

    /// <summary>A visão está aberta (ou abrindo); falso durante a troca rápida e ao fechar.</summary>
    public bool IsOverviewOpen => !_isClosing && _progress.Target > 0;

    private double Speed => Math.Max(_context.AnimationSpeed, 0.1);

    /// <summary>Win+Tab: a câmera se afasta e o cubo fica livre para girar.</summary>
    public void OpenOverview()
    {
        WhenShown(() =>
        {
            _isClosing = false;
            _progress.Start(1, ZoomDuration / Speed, Ease.OutCubic);
            StartRendering();
        });
    }

    /// <summary>Entra na área da frente (encaixando antes, se o cubo estiver entre duas faces).</summary>
    public void ZoomIn()
    {
        if (_isClosing)
        {
            return;
        }

        var (face, yaw) = NearestFace(_yaw.Target);
        EnterFace(face, yaw);
    }

    /// <summary>Gira uma face para a direita (+1) ou para a esquerda (-1), dando a volta no círculo.</summary>
    public void Rotate(int direction)
    {
        WhenShown(() =>
        {
            if (!IsOverviewOpen)
            {
                // Troca rápida: recua no meio do giro e volta à tela cheia no fim.
                _isClosing = true;
                _progress.Start(QuickPeak, QuickDuration / Speed / 2, Ease.OutQuad);
                _progress.Then(0, QuickDuration / Speed / 2, Ease.InOutQuad);
            }

            var duration = (IsOverviewOpen ? SnapDuration : QuickDuration) / Speed;
            var start = _yaw.Target;
            if (_desktops.Count < 2)
            {
                // Uma área só: o cubo ensaia o giro e volta.
                _yaw.Start(start - (direction * 9), duration / 3, Ease.OutQuad);
                _yaw.Then(start, duration * 2 / 3, Ease.OutCubic);
            }
            else
            {
                var next = (((_currentDesktop + direction) % _desktops.Count) + _desktops.Count) % _desktops.Count;
                SwitchTo(next);
                _yaw.Start(DirectionalYaw(next, direction, start), duration, IsOverviewOpen ? Ease.OutCubic : Ease.InOutCubic);
            }

            StartRendering();
        });
    }

    /// <summary>Demonstração (Debug): gira e inclina como um arrasto parado no meio.</summary>
    [Conditional("DEBUG")]
    internal void DemoTurn(double yaw, double pitch)
    {
        _yaw.Set(_yaw.Value + yaw);
        _pitch = pitch;
        Render();
    }

    /// <summary>Demonstração (Debug): salva o quadro atual em %AppData%\LiquidGlass.</summary>
    [Conditional("DEBUG")]
    internal void SaveDemoFrame(string name)
    {
        var bitmap = new RenderTargetBitmap((int)_root.ActualWidth, (int)_root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } };
        using var file = File.Create(System.IO.Path.Combine(AppDataPaths.Root, name + ".png"));
        encoder.Save(file);
    }

    public void HandleKey(OverviewKey key)
    {
        switch (key)
        {
            case OverviewKey.Escape:
            case OverviewKey.Enter:
                ZoomIn();
                break;
            case OverviewKey.Left:
                Rotate(-1);
                break;
            case OverviewKey.Right:
                Rotate(1);
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        StopRendering();
        base.OnClosed(e);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static GeometryModel3D Mesh(Point3D[] corners, Material material, Material? back, Transform3D transform)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = [.. corners],
            TextureCoordinates = [new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0)],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
        };

        return new GeometryModel3D(mesh, material) { BackMaterial = back, Transform = transform };
    }

    private static ImageBrush ToBrush(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap);
        brush.Freeze();
        return brush;
    }

    private double FaceYaw(int face) => -face * 360.0 / _sides;

    /// <summary>Ângulo que deixa a face de frente, na volta do círculo mais perto de <paramref name="yaw"/>.</summary>
    private double NearestYaw(int face, double yaw)
    {
        var faceYaw = FaceYaw(face);
        return faceYaw + (360 * Math.Round((yaw - faceYaw) / 360));
    }

    /// <summary>Ângulo da face seguindo o giro na direção pedida (para a direita o ângulo diminui).</summary>
    private double DirectionalYaw(int face, int direction, double from)
    {
        var faceYaw = FaceYaw(face);
        return direction > 0
            ? faceYaw + (360 * Math.Floor((from - faceYaw - 1e-6) / 360))
            : faceYaw + (360 * Math.Ceiling((from - faceYaw + 1e-6) / 360));
    }

    /// <summary>A área (face com área de trabalho) mais perto de ficar de frente.</summary>
    private (int Face, double Yaw) NearestFace(double yaw)
    {
        var best = (Face: _currentDesktop, Yaw: NearestYaw(_currentDesktop, yaw));
        for (var face = 0; face < _desktops.Count; face++)
        {
            var candidate = NearestYaw(face, yaw);
            if (Math.Abs(candidate - yaw) < Math.Abs(best.Yaw - yaw))
            {
                best = (face, candidate);
            }
        }

        return best;
    }

    private Grid CreatePill()
    {
        var glass = new LiquidGlassSurface
        {
            Style = (Style)FindResource("PopupGlassSurface"),
            CornerRadius = 24,
            Backdrop = _context.Wallpaper.Backdrop,
            BackdropOrigin = _context.Wallpaper.MonitorOrigin,
        };

        var content = new StackPanel { Margin = new Thickness(24, 12, 24, 11), Children = { _dots, _label } };
        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, (_context.Reserve.Bottom / _context.DpiScale) + 30),
            IsHitTestVisible = false,
            Opacity = 0,
            Children = { glass, content },
        };
    }

    /// <summary>Uma face por área, em volta do eixo vertical; faces sobrando (menos de 4 áreas) criam uma nova.</summary>
    private void BuildScene()
    {
        var h = _halfHeight;
        var r = _apothem;
        var corners = new[] { new Point3D(-1, -h, r), new Point3D(1, -h, r), new Point3D(1, h, r), new Point3D(-1, h, r) };
        var empty = new DiffuseMaterial(EmptyFaceBrush());
        var bases = new Model3DGroup();

        for (var index = 0; index < _sides; index++)
        {
            var placement = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), index * 360.0 / _sides));
            var isReal = index < _desktops.Count;
            var face = new Face(index)
            {
                // Sem lado de dentro: visto de cima, o cubo é um anel de painéis, como no vídeo.
                Base = Mesh(corners, isReal ? _baseMaterial : empty, null, placement),
                IsReal = isReal,
            };

            face.Layer = Mesh(corners, face.LayerMaterial, null, new Transform3DGroup { Children = { face.Lift, placement } });
            bases.Children.Add(face.Base);
            _faceOfModel[face.Base] = face;
            _faceOfModel[face.Layer] = face;
            _faces.Add(face);
        }

        // As camadas das janelas (com transparência) vêm depois das faces opacas, como o 3D do WPF exige.
        var cube = new Model3DGroup
        {
            Transform = new Transform3DGroup
            {
                Children = { new RotateTransform3D(_yawRotation), new RotateTransform3D(_pitchRotation) },
            },
            Children = { bases, _layers },
        };

        _viewport.Camera = _camera;
        _viewport.Children.Add(new ModelVisual3D { Content = new Model3DGroup { Children = { _ambient, _keyLight, cube } } });

        _root.Children.Add(new Image { Source = _context.Wallpaper.Backdrop, Stretch = Stretch.Fill });
        _root.Children.Add(new Rectangle
        {
            Fill = new RadialGradientBrush(Color.FromArgb(0x14, 0, 0, 0), Color.FromArgb(0x99, 0, 0, 0)) { RadiusX = 0.75, RadiusY = 0.75 },
        });
        _root.Children.Add(_viewport);
        _root.Children.Add(_pill);
        UpdateLabel(_currentDesktop);
        Render();
    }

    /// <summary>Face sobrando: o papel de parede em vidro fosco com um "+" para criar uma área.</summary>
    private DrawingBrush EmptyFaceBrush()
    {
        var bounds = new Rect(0, 0, _monitor.Width, _monitor.Height);
        var group = new DrawingGroup();
        if (_context.Wallpaper.Backdrop is { } backdrop)
        {
            group.Children.Add(new ImageDrawing(backdrop, bounds));
        }

        group.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0x59, 0x14, 0x14, 0x18)), null, new RectangleGeometry(bounds)));
        var arm = bounds.Height * 0.07;
        var thickness = arm * 0.16;
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var plus = new GeometryGroup
        {
            Children =
            {
                new RectangleGeometry(new Rect(center.X - arm, center.Y - (thickness / 2), arm * 2, thickness), thickness / 2, thickness / 2),
                new RectangleGeometry(new Rect(center.X - (thickness / 2), center.Y - arm, thickness, arm * 2), thickness / 2, thickness / 2),
            },
        };

        group.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), null, plus));
        var text = new FormattedText(
            "Nova área de trabalho",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(TextFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            bounds.Height * 0.035,
            Brushes.White,
            1.0);
        group.Children.Add(new GeometryDrawing(
            Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            null,
            text.BuildGeometry(new Point(center.X - (text.Width / 2), center.Y + (arm * 1.5)))));

        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Fundo comum (área de trabalho com os ícones) e a camada de janelas de cada área. A área atual
    /// sai da tela na hora; as outras são desenhadas em segundo plano e aparecem quando ficam prontas.
    /// </summary>
    private void LoadTextures()
    {
        // A dock não entra nos retratos: ela continua de verdade por cima do cubo.
        User32.SetWindowDisplayAffinity(_context.DockHandle, User32.WdaExcludeFromCapture);
        var monitor = new Int32Rect(_monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height);
        _baseMaterial.Brush = ToBrush(ScreenCapture.Desktop(monitor), monitor.Width, monitor.Height);

        var current = new List<Int32Rect>();
        var others = _desktops.ToDictionary(id => id, _ => new List<nint>());
        var area = new Rect(0, 0, monitor.Width, monitor.Height);
        foreach (var window in _context.Tracker.EnumerateAllDesktops())
        {
            if (User32.IsIconic(window.Handle))
            {
                continue;
            }

            if (!Dwmapi.IsCloaked(window.Handle))
            {
                if (Dwmapi.GetVisibleBounds(window.Handle) is { } visible)
                {
                    var rect = new Rect(visible.Left - monitor.X, visible.Top - monitor.Y, visible.Width, visible.Height);
                    rect.Intersect(area);
                    if (!rect.IsEmpty && rect.Width >= 1 && rect.Height >= 1)
                    {
                        current.Add(new Int32Rect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
                    }
                }
            }
            else if (_context.Desktops.GetWindowDesktop(window.Handle) is { } id && others.TryGetValue(id, out var list))
            {
                list.Add(window.Handle);
            }
        }

        _faces[_currentDesktop].LayerMaterial.Brush = ToBrush(ScreenCapture.ScreenWindows(monitor, current), monitor.Width, monitor.Height);
        User32.SetWindowDisplayAffinity(_context.DockHandle, User32.WdaNone);
        for (var index = 0; index < _desktops.Count; index++)
        {
            if (index != _currentDesktop && others[_desktops[index]] is { Count: > 0 } windows)
            {
                // A ordem da lista é de cima para baixo; desenhadas de baixo para cima, as da frente cobrem as de trás.
                windows.Reverse();
                _ = LoadLayerAsync(_faces[index], windows, monitor);
            }
        }
    }

    private async Task LoadLayerAsync(Face face, List<nint> windows, Int32Rect monitor)
    {
        var pixels = await Task.Run(() => ScreenCapture.PrintWindows(monitor, windows));
        if (!_isClosed)
        {
            face.LayerMaterial.Brush = ToBrush(pixels, monitor.Width, monitor.Height);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this);
        User32.AddExtendedStyle(_source.Handle, User32.WsExToolWindow | User32.WsExNoActivate);

        // Nasce fora da tela: uma janela opaca pisca preta até o primeiro quadro ficar pronto.
        User32.SetWindowPos(
            _source.Handle, User32.HwndTopMost, _monitor.Right + 64, _monitor.Top, _monitor.Width, _monitor.Height,
            User32.SwpNoActivate);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        User32.SetWindowPos(
            _source!.Handle, User32.HwndTopMost, _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height,
            User32.SwpNoActivate);
        User32.SetWindowPos(_context.DockHandle, User32.HwndTopMost, 0, 0, 0, 0, User32.SwpNoMove | User32.SwpNoSize | User32.SwpNoActivate);
        _isShownOnScreen = true;
        _shownAt = _clock.Elapsed;

        var pending = _onShown;
        _onShown = null;
        pending?.Invoke();
    }

    /// <summary>Animações começam só com o cubo na tela: antes disso o relógio correria sem quadros.</summary>
    private void WhenShown(Action action)
    {
        if (_isShownOnScreen)
        {
            action();
            return;
        }

        _onShown += action;
        if (_source is null)
        {
            Show();
        }
    }

    /// <summary>Troca a área do Windows por baixo do cubo (ela fica pronta quando o cubo fecha).</summary>
    private void SwitchTo(int face)
    {
        if (face == _currentDesktop || face >= _desktops.Count)
        {
            return;
        }

        _currentDesktop = face;
        _isSwitchPending = true;
        StartRendering();
    }

    private void SwitchWhenCovered()
    {
        if (_isSwitchPending && _isShownOnScreen && _clock.Elapsed - _shownAt >= SwitchDelay)
        {
            _isSwitchPending = false;
            _context.Desktops.Switch(_desktops[_currentDesktop]);
        }
    }

    private void EnterFace(int face, double yaw)
    {
        _isClosing = true;
        SwitchTo(face);
        _yaw.Start(yaw, ZoomDuration / Speed, Ease.OutCubic);
        _progress.Start(0, ZoomDuration / Speed, Ease.InOutCubic);
        StartRendering();
    }

    private void AddDesktop()
    {
        if (_desktops.Count >= _sides || _context.Desktops.Add() is not { } id)
        {
            return;
        }

        var face = _faces[_desktops.Count];
        _desktops.Add(id);
        face.IsReal = true;
        face.Base.Material = _baseMaterial;
        UpdateLabel(_labelFace, force: true);
        EnterFace(face.Index, NearestYaw(face.Index, _yaw.Value));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsOverviewOpen)
        {
            return;
        }

        e.Handled = true;
        _isPressed = true;
        _isDragging = false;
        _pressPoint = e.GetPosition(_root);
        _dragYaw = _yaw.Value;
        _dragPitch = _pitch;
        _samples.Clear();
        _samples.Enqueue((_clock.Elapsed, _pressPoint.X));
        _root.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        var position = e.GetPosition(_root);
        var delta = position - _pressPoint;
        if (!_isDragging && delta.Length < DragThreshold)
        {
            return;
        }

        if (!_isDragging)
        {
            // Pegar o cubo no meio de um encaixe interrompe o encaixe.
            _isDragging = true;
            _dragYaw = _yaw.Value;
            _pressPoint = position;
            delta = default;
        }

        var degreesPerDip = DragDegreesPerScreen / Math.Max(_root.ActualWidth, 1);
        _yaw.Set(_dragYaw + (delta.X * degreesPerDip));
        _pitch = Math.Clamp(_dragPitch + (delta.Y * degreesPerDip * 0.5), -8, 40);
        _samples.Enqueue((_clock.Elapsed, position.X));
        while (_samples.Count > 2 && _clock.Elapsed - _samples.Peek().Time > VelocityWindow)
        {
            _samples.Dequeue();
        }

        Render();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        _root.ReleaseMouseCapture();
        e.Handled = true;
        if (!_isDragging)
        {
            Click(e.GetPosition(_viewport));
            return;
        }

        _isDragging = false;

        // O embalo do arrasto conta: um giro rápido passa da face mais próxima.
        var (firstTime, firstX) = _samples.Peek();
        var elapsed = (_clock.Elapsed - firstTime).TotalSeconds;
        var velocity = elapsed > 0.005 ? (e.GetPosition(_root).X - firstX) / elapsed : 0;
        var degreesPerDip = DragDegreesPerScreen / Math.Max(_root.ActualWidth, 1);
        var projected = _yaw.Value + (velocity * degreesPerDip * 0.2);
        var (face, yaw) = NearestFace(projected);
        SwitchTo(face);
        _yaw.Start(yaw, SnapDuration / Speed, Ease.OutCubic);
        StartRendering();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsOverviewOpen && !_isPressed)
        {
            Rotate(e.Delta > 0 ? -1 : 1);
        }
    }

    /// <summary>Clique numa face entra nela; numa face vazia cria uma área; fora do cubo, fecha.</summary>
    private void Click(Point point)
    {
        Face? hit = null;
        VisualTreeHelper.HitTest(
            _viewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult mesh && _faceOfModel.TryGetValue(mesh.ModelHit, out var face))
                {
                    hit = face;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (hit is null)
        {
            ZoomIn();
        }
        else if (!hit.IsReal)
        {
            AddDesktop();
        }
        else
        {
            EnterFace(hit.Index, NearestYaw(hit.Index, _yaw.Value));
        }
    }

    private void StartRendering()
    {
        if (!_isRendering)
        {
            _isRendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void StopRendering()
    {
        if (_isRendering)
        {
            _isRendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var animating = _progress.Update() | _yaw.Update();
        Render();
        SwitchWhenCovered();
        if (animating || _isPressed || _isSwitchPending)
        {
            return;
        }

        StopRendering();
        if (_isClosing && _progress.Value <= 0)
        {
            Close();
        }
    }

    /// <summary>Posiciona câmera, giro, luz e janelas para o quadro atual.</summary>
    private void Render()
    {
        var progress = Math.Clamp(_progress.Value, 0, 1);
        _yawRotation.Angle = _yaw.Value;
        _pitchRotation.Angle = _pitch * progress;

        // Em 0 a face da frente cobre a tela exatamente (é a própria área de trabalho).
        _camera.Position = new Point3D(0, 0, _apothem + (_viewDistance * (1 + ((OverviewZoom - 1) * progress))));

        // Afastado, as faces de lado ficam mais escuras que a da frente, como no vídeo.
        var ambient = (byte)Math.Round(255 * (1 - (0.38 * progress)));
        var key = (byte)Math.Round(255 * 0.38 * progress);
        _ambient.Color = Color.FromRgb(ambient, ambient, ambient);
        _keyLight.Color = Color.FromRgb(key, key, key);

        var lift = LayerEpsilon + (WindowLift * progress);
        foreach (var face in _faces)
        {
            face.Lift.OffsetZ = lift;
        }

        SortLayers();
        _pill.Opacity = Math.Clamp((progress - 0.4) / 0.6, 0, 1);
        UpdateLabel(NearestFace(_yaw.Value).Face);
    }

    /// <summary>Camadas transparentes precisam ser desenhadas de trás para frente.</summary>
    private void SortLayers()
    {
        var order = _faces
            .OrderBy(face => Math.Cos(((face.Index * 360.0 / _sides) + _yaw.Value) * Math.PI / 180))
            .Select(face => face.Index)
            .ToArray();

        if (order.SequenceEqual(_layerOrder))
        {
            return;
        }

        _layerOrder = order;
        _layers.Children.Clear();
        foreach (var index in order)
        {
            _layers.Children.Add(_faces[index].Layer);
        }
    }

    private void UpdateLabel(int face, bool force = false)
    {
        if (face == _labelFace && !force)
        {
            return;
        }

        _labelFace = face;
        _label.Text = string.Create(CultureInfo.CurrentCulture, $"Área de trabalho {face + 1}");
        _dots.Children.Clear();
        for (var index = 0; index < _desktops.Count; index++)
        {
            _dots.Children.Add(new Border
            {
                Width = index == face ? 18 : 7,
                Height = 7,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3.5),
                Background = index == face ? Brushes.White : DotBrush,
            });
        }
    }

    /// <summary>Curvas de animação.</summary>
    private static class Ease
    {
        public static double OutQuad(double t) => 1 - ((1 - t) * (1 - t));

        public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);

        public static double InOutQuad(double t) => t < 0.5 ? 2 * t * t : 1 - (Math.Pow((-2 * t) + 2, 2) / 2);

        public static double InOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);
    }

    /// <summary>Valor animado por tempo, avaliado a cada quadro.</summary>
    private sealed class Tween
    {
        private readonly Stopwatch _clock = new();
        private double _from;
        private double _stageTarget;
        private TimeSpan _duration;
        private Func<double, double> _easing = Ease.OutQuad;
        private (double Target, TimeSpan Duration, Func<double, double> Easing)? _next;

        public double Value { get; private set; }

        /// <summary>Onde a animação termina (depois das encadeadas).</summary>
        public double Target { get; private set; }

        public void Set(double value)
        {
            Value = Target = _stageTarget = value;
            _clock.Reset();
            _next = null;
        }

        public void Start(double target, TimeSpan duration, Func<double, double> easing)
        {
            _from = Value;
            Target = _stageTarget = target;
            _duration = duration;
            _easing = easing;
            _next = null;
            _clock.Restart();
        }

        /// <summary>Encadeia outra animação para quando esta terminar.</summary>
        public void Then(double target, TimeSpan duration, Func<double, double> easing)
        {
            _next = (target, duration, easing);
            Target = target;
        }

        /// <summary>Avança a animação; devolve <c>true</c> enquanto ela não terminou.</summary>
        public bool Update()
        {
            if (!_clock.IsRunning)
            {
                return false;
            }

            var t = _duration <= TimeSpan.Zero ? 1 : Math.Clamp(_clock.Elapsed / _duration, 0, 1);
            Value = _from + ((_stageTarget - _from) * _easing(t));
            if (t < 1)
            {
                return true;
            }

            _clock.Reset();
            if (_next is { } next)
            {
                _next = null;
                Start(next.Target, next.Duration, next.Easing);
                return true;
            }

            return false;
        }
    }

    private sealed class Face(int index)
    {
        public int Index { get; } = index;

        public bool IsReal { get; set; }

        public required GeometryModel3D Base { get; init; }

        public GeometryModel3D Layer { get; set; } = null!;

        public DiffuseMaterial LayerMaterial { get; } = new(Brushes.Transparent);

        public TranslateTransform3D Lift { get; } = new(0, 0, LayerEpsilon);
    }
}
