using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
/// gira o cubo livremente (e inclina na vertical); ao soltar, ele encaixa na face mais próxima,
/// levando em conta o embalo. Clique numa face para entrar nela; setas e roda do mouse giram uma face.
/// Sempre há uma face "+": parar nela e clicar (ou Enter) cria uma área, e o anel ganha outra "+".</item>
/// <item>Na visão, arrastar uma janela a tira da área dela; soltá-la em outra face a move para lá (na
/// face "+", cria uma área com ela). Segurar na borda da tela, a roda ou as setas giram o cubo durante
/// o arrasto; Esc ou soltar fora do cubo devolvem a janela.</item>
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

    /// <summary>Faixa de cada lado da tela (fração da largura) que gira o cubo durante o arrasto de uma janela.</summary>
    private const double EdgeZone = 0.06;

    /// <summary>Afastamento mínimo das janelas: coplanares à face, as duas camadas piscariam.</summary>
    private const double LayerEpsilon = 0.0008;

    private static readonly TimeSpan ZoomDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SnapDuration = TimeSpan.FromMilliseconds(520);
    private static readonly TimeSpan QuickDuration = TimeSpan.FromMilliseconds(820);
    private static readonly TimeSpan VelocityWindow = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan EdgeDwell = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan EdgeRepeat = TimeSpan.FromMilliseconds(700);

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
    private int _sides;
    private double _apothem;
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
    private readonly Model3DGroup _bases = new();
    private readonly Model3DGroup _layers = new();
    private readonly Model3DGroup _cube = new();
    private readonly ModelVisual3D _scene = new();
    private readonly List<Face> _faces = [];
    private readonly Dictionary<Guid, Shot> _shots = [];
    private DiffuseMaterial? _emptyMaterial;
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

    private readonly Canvas _dragLayer = new() { IsHitTestVisible = false };
    private readonly Image _ghost = new()
    {
        Stretch = Stretch.Fill,
        Visibility = Visibility.Collapsed,
        Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.45 },
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
    private bool _labelDragging;
    private int[] _layerOrder = [];
    private double _pitch = OverviewPitch;
    private bool _isPressed;
    private bool _isDragging;
    private Point _pressPoint;
    private double _dragYaw;
    private double _dragPitch;
    private Point _pointer;
    private Grab? _pressGrab;
    private WindowDrag? _drag;
    private int _demoTurn;

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
        foreach (var desktop in _desktops)
        {
            _shots[desktop] = new Shot();
        }

        SetSides(_desktops.Count);
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
        _root.LostMouseCapture += (_, _) => CancelDrag();
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

        CancelDrag();
        var (face, yaw) = NearestFace(_yaw.Target);
        if (face >= _desktops.Count)
        {
            (face, yaw) = (_currentDesktop, NearestYaw(_currentDesktop, _yaw.Target));
        }

        EnterFace(face, yaw);
    }

    /// <summary>Gira uma face para a direita (+1) ou para a esquerda (-1), dando a volta no círculo.</summary>
    public void Rotate(int direction)
    {
        WhenShown(() =>
        {
            var start = _yaw.Target;
            if (IsOverviewOpen)
            {
                // Na visão, o giro passa por todas as faces, inclusive a "+".
                var next = (((NearestFace(start).Face + direction) % _sides) + _sides) % _sides;
                SwitchTo(next);
                _yaw.Start(DirectionalYaw(next, direction, start), SnapDuration / Speed, Ease.OutCubic);
                StartRendering();
                return;
            }

            // Troca rápida: recua no meio do giro e volta à tela cheia no fim.
            _isClosing = true;
            _progress.Start(QuickPeak, QuickDuration / Speed / 2, Ease.OutQuad);
            _progress.Then(0, QuickDuration / Speed / 2, Ease.InOutQuad);
            var duration = QuickDuration / Speed;
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
                _yaw.Start(DirectionalYaw(next, direction, start), duration, Ease.InOutCubic);
            }

            StartRendering();
        });
    }

    /// <summary>Demonstração (Debug): ângulo entre duas faces vizinhas.</summary>
    internal double DemoFaceStep => 360.0 / _sides;

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

    /// <summary>Demonstração (Debug): pega a janela na face da área atual, como o mouse faria.</summary>
    [Conditional("DEBUG")]
    internal void DemoGrab(nint hwnd)
    {
        var desktop = _desktops[_currentDesktop];
        if (_shots[desktop].Windows.FirstOrDefault(window => window.Handle == hwnd) is { Handle: not 0 } window
            && Project(_currentDesktop, new Point(window.Bounds.X + (window.Bounds.Width / 2.0), window.Bounds.Y + (window.Bounds.Height / 2.0))) is { } point)
        {
            Press(point);
            Drag(point + new Vector(DragThreshold * 3, 0));
        }
    }

    /// <summary>
    /// Demonstração (Debug): gira o cubo uma face com a janela na mão, como a borda da tela faria. Para
    /// a demonstração nunca criar uma área, vai para o lado que tem uma área de verdade; com
    /// <paramref name="back"/>, desfaz o último giro.
    /// </summary>
    [Conditional("DEBUG")]
    internal void DemoRotate(bool back)
    {
        if (!back)
        {
            _demoTurn = _currentDesktop + 1 < _desktops.Count ? 1 : -1;
        }

        Rotate(back ? -_demoTurn : _demoTurn);
    }

    /// <summary>Demonstração (Debug): leva a janela pega até o meio da face da frente.</summary>
    [Conditional("DEBUG")]
    internal void DemoPointAtFront()
    {
        if (Project(NearestFace(_yaw.Value).Face, new Point(_monitor.Width / 2.0, _monitor.Height / 2.0)) is { } point)
        {
            Drag(point);
        }
    }

    /// <summary>Demonstração (Debug): solta a janela onde ela está.</summary>
    [Conditional("DEBUG")]
    internal void DemoDrop() => Release(_pointer);

    public void HandleKey(OverviewKey key)
    {
        switch (key)
        {
            case OverviewKey.Escape when _drag is not null:
                CancelDrag();
                break;
            case OverviewKey.Escape:
                ZoomIn();
                break;
            case OverviewKey.Enter when _drag is not null:
                break;
            case OverviewKey.Enter when NearestFace(_yaw.Target).Face >= _desktops.Count:
                AddDesktop(enter: true);
                break;
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

    private static BitmapSource ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageBrush ToBrush(byte[] pixels, int width, int height)
    {
        var brush = new ImageBrush(ToBitmap(pixels, width, height));
        brush.Freeze();
        return brush;
    }

    private static Int32Rect? Intersect(Int32Rect a, Int32Rect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right > left && bottom > top ? new Int32Rect(left, top, right - left, bottom - top) : null;
    }

    private static bool Contains(Int32Rect rect, Point point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

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

    /// <summary>A face mais perto de ficar de frente (uma área ou a face "+").</summary>
    private (int Face, double Yaw) NearestFace(double yaw)
    {
        var best = (Face: _currentDesktop, Yaw: NearestYaw(_currentDesktop, yaw));
        for (var face = 0; face < _sides; face++)
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

    /// <summary>Uma face por área e sempre mais uma, a "+", para criar outra (no mínimo 4 faces).</summary>
    private void SetSides(int desktopCount)
    {
        _sides = Math.Max(desktopCount + 1, MinSides);
        _apothem = 1 / Math.Tan(Math.PI / _sides);
    }

    private void BuildScene()
    {
        BuildRing();

        // As camadas das janelas (com transparência) vêm depois das faces opacas, como o 3D do WPF exige.
        _cube.Transform = new Transform3DGroup
        {
            Children = { new RotateTransform3D(_yawRotation), new RotateTransform3D(_pitchRotation) },
        };
        _cube.Children.Add(_bases);
        _cube.Children.Add(_layers);

        _viewport.Camera = _camera;
        _scene.Content = new Model3DGroup { Children = { _ambient, _keyLight, _cube } };
        _viewport.Children.Add(_scene);

        _root.Children.Add(new Image { Source = _context.Wallpaper.Backdrop, Stretch = Stretch.Fill });
        _root.Children.Add(new Rectangle
        {
            Fill = new RadialGradientBrush(Color.FromArgb(0x14, 0, 0, 0), Color.FromArgb(0x99, 0, 0, 0)) { RadiusX = 0.75, RadiusY = 0.75 },
        });
        _root.Children.Add(_viewport);
        _root.Children.Add(_pill);
        _dragLayer.Children.Add(_ghost);
        _root.Children.Add(_dragLayer);
        UpdateLabel(_currentDesktop);
        Render();
    }

    /// <summary>
    /// Monta as faces em volta do eixo vertical. Ao crescer (área nova), as janelas já retratadas
    /// continuam nas faces de mesmo número.
    /// </summary>
    private void BuildRing()
    {
        var layerBrushes = _faces.Select(face => face.LayerMaterial.Brush).ToList();
        _faces.Clear();
        _faceOfModel.Clear();
        _bases.Children.Clear();
        _layers.Children.Clear();
        _layerOrder = [];

        var h = _halfHeight;
        var r = _apothem;
        var corners = new[] { new Point3D(-1, -h, r), new Point3D(1, -h, r), new Point3D(1, h, r), new Point3D(-1, h, r) };
        _emptyMaterial ??= new DiffuseMaterial(EmptyFaceBrush());
        for (var index = 0; index < _sides; index++)
        {
            var placement = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), index * 360.0 / _sides));
            var isReal = index < _desktops.Count;
            var face = new Face(index)
            {
                // Sem lado de dentro: visto de cima, o cubo é um anel de painéis, como no vídeo.
                Base = Mesh(corners, isReal ? _baseMaterial : _emptyMaterial, null, placement),
                IsReal = isReal,
            };

            if (index < layerBrushes.Count)
            {
                face.LayerMaterial.Brush = layerBrushes[index];
            }

            face.Layer = Mesh(corners, face.LayerMaterial, null, new Transform3DGroup { Children = { face.Lift, placement } });
            _bases.Children.Add(face.Base);
            _faceOfModel[face.Base] = face;
            _faceOfModel[face.Layer] = face;
            _faces.Add(face);
        }
    }

    /// <summary>Face "+": o papel de parede em vidro fosco com um "+" para criar uma área.</summary>
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

    private Int32Rect MonitorRect => new(_monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height);

    /// <summary>
    /// Fundo comum (área de trabalho com os ícones) e a camada de janelas de cada área. A área atual
    /// sai da tela na hora; as outras são desenhadas em segundo plano e aparecem quando ficam prontas.
    /// Cada área guarda também onde está cada janela, para que ela possa ser arrastada.
    /// </summary>
    private void LoadTextures()
    {
        // A dock não entra nos retratos: ela continua de verdade por cima do cubo.
        User32.SetWindowDisplayAffinity(_context.DockHandle, User32.WdaExcludeFromCapture);
        var monitor = MonitorRect;
        _baseMaterial.Brush = ToBrush(ScreenCapture.Desktop(monitor), monitor.Width, monitor.Height);

        var current = _desktops[_currentDesktop];
        foreach (var window in _context.Tracker.EnumerateAllDesktops())
        {
            if (User32.IsIconic(window.Handle) || Bounds(window.Handle) is not { } bounds)
            {
                continue;
            }

            // A lista vem de cima para baixo, a mesma ordem guardada em cada área.
            var desktop = Dwmapi.IsCloaked(window.Handle) ? _context.Desktops.GetWindowDesktop(window.Handle) : current;
            if (desktop is { } id && _shots.TryGetValue(id, out var shot))
            {
                shot.Windows.Add(new WindowShot(window.Handle, bounds));
            }
        }

        SetLayer(current, ScreenCapture.ScreenWindows(monitor, _shots[current].Windows.Select(window => window.Bounds)));
        User32.SetWindowDisplayAffinity(_context.DockHandle, User32.WdaNone);
        foreach (var desktop in _desktops)
        {
            if (desktop != current && _shots[desktop].Windows.Count > 0)
            {
                _ = RebuildLayerAsync(desktop);
            }
        }
    }

    /// <summary>Parte visível da janela no monitor, em pixels relativos a ele.</summary>
    private Int32Rect? Bounds(nint hwnd)
    {
        if (Dwmapi.GetVisibleBounds(hwnd) is not { } visible)
        {
            return null;
        }

        return Intersect(
            new Int32Rect(visible.Left - _monitor.Left, visible.Top - _monitor.Top, visible.Width, visible.Height),
            new Int32Rect(0, 0, _monitor.Width, _monitor.Height));
    }

    /// <summary>
    /// Redesenha a camada da área com as janelas que ela tem agora, em segundo plano. Um retrato pedido
    /// depois (ou um arrasto começando nela) descarta este quando ele chegar.
    /// </summary>
    private async Task RebuildLayerAsync(Guid desktop)
    {
        var shot = _shots[desktop];
        var version = ++shot.Version;

        // Desenhadas de baixo para cima, as da frente cobrem as de trás.
        var windows = shot.Windows.Select(window => window.Handle).Reverse().ToList();
        var monitor = MonitorRect;
        var pixels = await Task.Run(() => ScreenCapture.PrintWindows(monitor, windows));
        if (!_isClosed && shot.Version == version)
        {
            SetLayer(desktop, pixels);
        }
    }

    private void SetLayer(Guid desktop, byte[] pixels)
    {
        _shots[desktop].Pixels = pixels;
        var index = _desktops.IndexOf(desktop);
        if (index >= 0)
        {
            _faces[index].LayerMaterial.Brush = ToBrush(pixels, _monitor.Width, _monitor.Height);
        }
    }

    /// <summary>O trecho <paramref name="rect"/> de uma camada do tamanho do monitor.</summary>
    private byte[] Crop(byte[] layer, Int32Rect rect)
    {
        var pixels = new byte[rect.Width * rect.Height * 4];
        for (var row = 0; row < rect.Height; row++)
        {
            Buffer.BlockCopy(layer, (((rect.Y + row) * _monitor.Width) + rect.X) * 4, pixels, row * rect.Width * 4, rect.Width * 4);
        }

        return pixels;
    }

    /// <summary>Cópia da camada com <paramref name="pixels"/> por cima, em <paramref name="rect"/>.</summary>
    private byte[] Paste(byte[]? layer, byte[] pixels, Int32Rect rect)
    {
        var result = layer is null ? new byte[_monitor.Width * _monitor.Height * 4] : (byte[])layer.Clone();
        for (var row = 0; row < rect.Height; row++)
        {
            Buffer.BlockCopy(pixels, row * rect.Width * 4, result, (((rect.Y + row) * _monitor.Width) + rect.X) * 4, rect.Width * 4);
        }

        return result;
    }

    /// <summary>
    /// Cópia da camada sem a janela <paramref name="index"/>: o lugar dela fica transparente, menos onde
    /// janelas de cima a cobriam. O que estava atrás dela aparece quando a camada for redesenhada.
    /// </summary>
    private byte[] Erase(byte[] layer, IReadOnlyList<WindowShot> windows, int index)
    {
        var result = (byte[])layer.Clone();
        var rect = windows[index].Bounds;
        for (var row = 0; row < rect.Height; row++)
        {
            Array.Clear(result, (((rect.Y + row) * _monitor.Width) + rect.X) * 4, rect.Width * 4);
        }

        for (var above = 0; above < index; above++)
        {
            if (Intersect(windows[above].Bounds, rect) is { } cover)
            {
                for (var row = 0; row < cover.Height; row++)
                {
                    var offset = (((cover.Y + row) * _monitor.Width) + cover.X) * 4;
                    Buffer.BlockCopy(layer, offset, result, offset, cover.Width * 4);
                }
            }
        }

        return result;
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

    /// <summary>
    /// Cria uma área: a face "+" vira a área nova, o anel ganha outra face "+" e o cubo continua
    /// aberto, já virado para a área nova, para criar mais ou entrar nela com um clique. Sem
    /// <paramref name="enter"/>, a área do Windows não muda (a área nova recebe uma janela arrastada).
    /// </summary>
    private Guid? AddDesktop(bool enter)
    {
        if (_isClosing || _context.Desktops.Add() is not { } id)
        {
            return null;
        }

        var (front, frontYaw) = NearestFace(_yaw.Value);
        var offset = _yaw.Value - frontYaw;
        var index = _desktops.Count;
        _desktops.Add(id);
        _shots[id] = new Shot();
        SetSides(_desktops.Count);
        BuildRing();

        // Na nova divisão do círculo, a face que estava de frente continua de frente.
        _yaw.Set(FaceYaw(front) + offset);
        if (enter)
        {
            SwitchTo(index);
        }

        _yaw.Start(NearestYaw(index, _yaw.Value), SnapDuration / Speed, Ease.OutCubic);
        UpdateLabel(_labelFace, force: true);
        Render();
        StartRendering();
        return id;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsOverviewOpen)
        {
            return;
        }

        e.Handled = true;
        Press(e.GetPosition(_root));
        _root.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => Drag(e.GetPosition(_root));

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        e.Handled = true;
        Release(e.GetPosition(_root));
        _root.ReleaseMouseCapture();
    }

    /// <summary>Botão apertado: ainda não se sabe se é clique, giro do cubo ou arrasto de uma janela.</summary>
    private void Press(Point point)
    {
        _isPressed = true;
        _isDragging = false;
        _pressPoint = _pointer = point;
        _pressGrab = WindowAt(point);
        _dragYaw = _yaw.Value;
        _dragPitch = _pitch;
        _samples.Clear();
        _samples.Enqueue((_clock.Elapsed, _pressPoint.X));
    }

    private void Drag(Point position)
    {
        if (!_isPressed)
        {
            return;
        }

        _pointer = position;
        if (_drag is not null)
        {
            UpdateDropTarget();
            return;
        }

        var delta = position - _pressPoint;
        if (!_isDragging && delta.Length < DragThreshold)
        {
            return;
        }

        if (!_isDragging && _pressGrab is { } grab)
        {
            // Apertou sobre uma janela: ela sai da área e acompanha o mouse, e o cubo fica parado.
            _isDragging = true;
            BeginWindowDrag(grab);
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

    private void Release(Point position)
    {
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        _pointer = position;
        if (_drag is not null)
        {
            _isDragging = false;
            UpdateDropTarget();
            DropWindow();
            return;
        }

        if (!_isDragging)
        {
            Click(position);
            return;
        }

        _isDragging = false;

        // O embalo do arrasto conta: um giro rápido passa da face mais próxima.
        var (firstTime, firstX) = _samples.Peek();
        var elapsed = (_clock.Elapsed - firstTime).TotalSeconds;
        var velocity = elapsed > 0.005 ? (position.X - firstX) / elapsed : 0;
        var degreesPerDip = DragDegreesPerScreen / Math.Max(_root.ActualWidth, 1);
        var projected = _yaw.Value + (velocity * degreesPerDip * 0.2);
        var (face, yaw) = NearestFace(projected);
        SwitchTo(face);
        _yaw.Start(yaw, SnapDuration / Speed, Ease.OutCubic);
        StartRendering();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Com uma janela na mão, a roda gira o cubo para levá-la a uma área de trás.
        if (IsOverviewOpen && (!_isPressed || _drag is not null))
        {
            Rotate(e.Delta > 0 ? -1 : 1);
        }
    }

    /// <summary>Clique numa face entra nela; numa face vazia cria uma área; fora do cubo, fecha.</summary>
    private void Click(Point point)
    {
        if (FaceAt(point) is not { } hit)
        {
            ZoomIn();
        }
        else if (!hit.Face.IsReal)
        {
            AddDesktop(enter: true);
        }
        else
        {
            EnterFace(hit.Face.Index, NearestYaw(hit.Face.Index, _yaw.Value));
        }
    }

    /// <summary>
    /// A face sob o ponto (do <see cref="_viewport"/>) e, nela, o pixel do monitor correspondente: a
    /// textura de cada face cobre o monitor inteiro.
    /// </summary>
    private (Face Face, Point Pixel)? FaceAt(Point point)
    {
        (Face Face, Point Pixel)? hit = null;
        VisualTreeHelper.HitTest(
            _viewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult mesh && _faceOfModel.TryGetValue(mesh.ModelHit, out var face))
                {
                    var uv = mesh.MeshHit.TextureCoordinates;
                    var u = (uv[mesh.VertexIndex1].X * mesh.VertexWeight1) + (uv[mesh.VertexIndex2].X * mesh.VertexWeight2) + (uv[mesh.VertexIndex3].X * mesh.VertexWeight3);
                    var v = (uv[mesh.VertexIndex1].Y * mesh.VertexWeight1) + (uv[mesh.VertexIndex2].Y * mesh.VertexWeight2) + (uv[mesh.VertexIndex3].Y * mesh.VertexWeight3);
                    hit = (face, new Point(u * _monitor.Width, v * _monitor.Height));
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        return hit;
    }

    /// <summary>Onde o ponto do monitor, numa face, aparece na tela agora (no <see cref="_viewport"/>).</summary>
    private Point? Project(int face, Point pixel)
    {
        var local = new Point3D(
            (pixel.X / _monitor.Width * 2) - 1,
            _halfHeight - (pixel.Y / _monitor.Height * 2 * _halfHeight),
            _apothem);

        var world = _cube.Transform.Transform(_faces[face].Layer.Transform.Transform(local));
        return _scene.TransformToAncestor(_viewport) is { } transform && transform.TryTransform(world, out var point) ? point : null;
    }

    /// <summary>
    /// A janela sob o ponto, se der para arrastá-la: com a visão aberta, numa área com retrato pronto
    /// e presa a uma área só (janelas fixadas em todas as áreas não mudam de área).
    /// </summary>
    private Grab? WindowAt(Point point)
    {
        if (_progress.Value < 0.9 || FaceAt(point) is not { } hit || !hit.Face.IsReal)
        {
            return null;
        }

        var desktop = _desktops[hit.Face.Index];
        var shot = _shots[desktop];
        if (shot.Pixels is null)
        {
            return null;
        }

        foreach (var window in shot.Windows)
        {
            if (Contains(window.Bounds, hit.Pixel))
            {
                return _context.Desktops.GetWindowDesktop(window.Handle) is { } id && id != Guid.Empty
                    ? new Grab(desktop, window, hit.Pixel - new Point(window.Bounds.X, window.Bounds.Y))
                    : null;
            }
        }

        return null;
    }

    /// <summary>A janela sai da camada da área (na hora) e passa a seguir o mouse, em tamanho de visão.</summary>
    private void BeginWindowDrag(Grab grab)
    {
        var shot = _shots[grab.Desktop];
        var original = shot.Pixels!;
        var bounds = grab.Window.Bounds;
        var index = shot.Windows.IndexOf(grab.Window);
        _drag = new WindowDrag(grab, index, original, Crop(original, bounds));

        // Na hora, o lugar dela fica vazio; o redesenho mostra o que estava atrás (e descarta um
        // retrato desta área ainda a caminho, que teria a janela de volta).
        SetLayer(grab.Desktop, Erase(original, shot.Windows, index));
        shot.Windows.RemoveAt(index);
        _ = RebuildLayerAsync(grab.Desktop);
        _ghost.Source = ToBitmap(_drag.Pixels, bounds.Width, bounds.Height);
        _ghost.Visibility = Visibility.Visible;
        UpdateDropTarget();
        StartRendering();
        _ = SharpenGhostAsync(_drag);
    }

    /// <summary>
    /// O primeiro retrato da janela é recortado da face e pode trazer pedaços de janelas que estavam
    /// por cima; a própria janela desenhada pelo Windows o substitui assim que fica pronta.
    /// </summary>
    private async Task SharpenGhostAsync(WindowDrag drag)
    {
        var monitor = MonitorRect;
        var bounds = drag.Grab.Window.Bounds;
        var handle = drag.Grab.Window.Handle;
        var layer = await Task.Run(() => ScreenCapture.PrintWindows(monitor, [handle]));
        var center = ((((bounds.Y + (bounds.Height / 2)) * _monitor.Width) + bounds.X + (bounds.Width / 2)) * 4) + 3;
        if (_drag == drag && layer[center] != 0)
        {
            drag.Pixels = Crop(layer, bounds);
            _ghost.Source = ToBitmap(drag.Pixels, bounds.Width, bounds.Height);
        }
    }

    /// <summary>A face sob o mouse vira o destino (menos a própria área da janela) e a janela o acompanha.</summary>
    private void UpdateDropTarget()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        var source = _desktops.IndexOf(drag.Grab.Desktop);
        drag.Target = FaceAt(_pointer) is { } hit && hit.Face.Index != source ? hit.Face.Index : -1;

        // Do tamanho com que as janelas aparecem na face da frente, com o ponto pego sob o mouse.
        var progress = Math.Clamp(_progress.Value, 0, 1);
        var distance = (_viewDistance * (1 + ((OverviewZoom - 1) * progress))) - (WindowLift * progress);
        var scale = _viewDistance / distance / _context.DpiScale;
        var bounds = drag.Grab.Window.Bounds;
        _ghost.Width = bounds.Width * scale;
        _ghost.Height = bounds.Height * scale;
        Canvas.SetLeft(_ghost, _pointer.X - (drag.Grab.Offset.X * scale));
        Canvas.SetTop(_ghost, _pointer.Y - (drag.Grab.Offset.Y * scale));
        _ghost.Opacity = drag.Target >= 0 ? 1 : 0.85;
    }

    /// <summary>Parado perto da borda esquerda ou direita, o cubo gira uma face (e repete enquanto ali).</summary>
    private void ScrollAtEdge()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        var zone = Math.Max(_root.ActualWidth * EdgeZone, 40);
        var side = _pointer.X < zone ? -1 : _pointer.X > _root.ActualWidth - zone ? 1 : 0;
        if (side != drag.EdgeSide)
        {
            drag.EdgeSide = side;
            drag.EdgeSince = _clock.Elapsed;
        }
        else if (side != 0 && _clock.Elapsed - drag.EdgeSince >= EdgeDwell)
        {
            drag.EdgeSince = _clock.Elapsed + EdgeRepeat - EdgeDwell;
            Rotate(side);
        }
    }

    /// <summary>
    /// Solta a janela: numa outra área, ela vai para lá; na face "+", para uma área nova. As duas faces
    /// mudam na hora e são redesenhadas pelo Windows logo depois. Fora do cubo, ela volta.
    /// </summary>
    private void DropWindow()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        var target = drag.Target;
        var destination = target < 0 ? null : target < _desktops.Count ? _desktops[target] : AddDesktop(enter: false);
        if (destination is not { } id || !_context.Desktops.MoveWindow(drag.Grab.Window.Handle, id))
        {
            CancelDrag();
            return;
        }

        EndDrag();
        var window = drag.Grab.Window;
        var shot = _shots[id];
        shot.Windows.Insert(0, window);
        SetLayer(id, Paste(shot.Pixels, drag.Pixels, window.Bounds));
        _ = RebuildLayerAsync(id);
    }

    /// <summary>Devolve a janela arrastada à área dela.</summary>
    private void CancelDrag()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _isPressed = false;
        _isDragging = false;
        EndDrag();
        _root.ReleaseMouseCapture();
        var shot = _shots[drag.Grab.Desktop];
        shot.Windows.Insert(Math.Min(drag.Index, shot.Windows.Count), drag.Grab.Window);
        SetLayer(drag.Grab.Desktop, drag.Original);
        _ = RebuildLayerAsync(drag.Grab.Desktop);
    }

    private void EndDrag()
    {
        _drag = null;
        _ghost.Visibility = Visibility.Collapsed;
        _ghost.Source = null;
        StartRendering();
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
        if (_drag is not null)
        {
            // O cubo pode girar sob a janela parada: o destino muda sem o mouse se mexer.
            ScrollAtEdge();
            UpdateDropTarget();
        }

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
        UpdateLabel(_drag is { } drag ? drag.Target : NearestFace(_yaw.Value).Face);
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

    /// <param name="face">Face em destaque; com uma janela arrastada, o destino dela (-1 sem destino).</param>
    private void UpdateLabel(int face, bool force = false)
    {
        var dragging = _drag is not null;
        if (face == _labelFace && dragging == _labelDragging && !force)
        {
            return;
        }

        _labelFace = face;
        _labelDragging = dragging;
        var isNew = face >= _desktops.Count;
        _label.Text = (dragging, face < 0, isNew) switch
        {
            (true, true, _) => "Solte a janela em outra área de trabalho",
            (true, _, true) => "Soltar aqui cria uma área de trabalho com a janela",
            (true, _, _) => string.Create(CultureInfo.CurrentCulture, $"Mover para a área de trabalho {face + 1}"),
            (_, _, true) => "Nova área de trabalho · clique ou Enter para criar",
            _ => string.Create(CultureInfo.CurrentCulture, $"Área de trabalho {face + 1}"),
        };
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

        _dots.Children.Add(new TextBlock
        {
            Text = "+",
            FontFamily = TextFont,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(5, -6, 2, -4),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isNew ? Brushes.White : DotBrush,
        });
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

    /// <summary>Uma janela no retrato de uma área: onde ela está, em pixels relativos ao monitor.</summary>
    private readonly record struct WindowShot(nint Handle, Int32Rect Bounds);

    /// <summary>Janela sob o botão apertado; <paramref name="Offset"/> é o ponto pego, em pixels dela.</summary>
    private sealed record Grab(Guid Desktop, WindowShot Window, Vector Offset);

    /// <summary>O retrato de uma área: a camada de janelas e as janelas nela, de cima para baixo.</summary>
    private sealed class Shot
    {
        public List<WindowShot> Windows { get; } = [];

        public byte[]? Pixels { get; set; }

        /// <summary>Cresce a cada mudança: um retrato que chega atrasado não sobrescreve um mais novo.</summary>
        public int Version { get; set; }
    }

    private sealed class WindowDrag(Grab grab, int index, byte[] original, byte[] pixels)
    {
        public Grab Grab { get; } = grab;

        /// <summary>Posição da janela na lista da área de origem (de cima para baixo), para devolvê-la.</summary>
        public int Index { get; } = index;

        /// <summary>Camada da área de origem antes do arrasto, para devolver a janela.</summary>
        public byte[] Original { get; } = original;

        /// <summary>A janela, do tamanho de <see cref="WindowShot.Bounds"/>.</summary>
        public byte[] Pixels { get; set; } = pixels;

        /// <summary>Face sob o mouse onde soltar move a janela; -1 se nenhuma.</summary>
        public int Target { get; set; } = -1;

        public int EdgeSide { get; set; }

        public TimeSpan EdgeSince { get; set; }
    }
}
