using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;
using LiquidGlass.Models;

namespace LiquidGlass.Views;

/// <summary>
/// Desenha a "lâmpada mágica" com um retrato da janela esticado numa malha contínua: as bordas do
/// funil saem lisas e a figura inteira muda no mesmo quadro (com fatias de miniaturas do DWM, cada
/// fatia chegava num quadro e as bordas ficavam em degraus).
/// <para>
/// A transparência vem do próprio DWM (moldura estendida sobre a janela toda), não do caminho de
/// janelas em camadas do WPF, que copia a tela inteira da placa de vídeo para a memória a cada
/// quadro. Assim a animação roda na taxa do monitor mesmo em janelas maximizadas.
/// </para>
/// A janela nasce fora da tela, já desenhada, e só entra no lugar quando o efeito começa: não há
/// quadro vazio entre a janela de verdade sumir e o efeito aparecer.
/// </summary>
internal sealed class GenieWindow : Window
{
    private const int Rows = 96;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DoNotRound = 1;
    private const int ColorNone = unchecked((int)0xFFFFFFFE);

    private readonly Genie _genie;
    private readonly Rect _bounds;
    private readonly bool _isReversed;
    private readonly TimeSpan _duration;
    private readonly MeshGeometry3D _mesh = new();
    private readonly Viewport3D _viewport = new();
    private readonly Stopwatch _clock = new();
    private HwndSource? _source;
    private bool _isRendered;
    private bool _isPlaying;
    private bool _isFinished;
    private Action<double>? _onProgress;
    private Action? _onCompleted;

    /// <param name="window">Onde a janela fica na tela, em pixels físicos (a área do retrato).</param>
    /// <param name="icon">Ícone da janela na dock, em pixels físicos.</param>
    /// <param name="isReversed">Restaurar: do ícone para a janela.</param>
    public GenieWindow(BitmapSource image, Rect window, Rect icon, DockEdge edge, TimeSpan duration, bool isReversed)
    {
        _bounds = Rect.Inflate(Rect.Union(window, icon), 2, 2);
        _bounds = new Rect(Math.Floor(_bounds.X), Math.Floor(_bounds.Y), Math.Ceiling(_bounds.Width), Math.Ceiling(_bounds.Height));
        _genie = new Genie(Offset(window), Offset(icon), new Size(image.PixelWidth, image.PixelHeight), edge);
        _isReversed = isReversed;
        _duration = duration;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;

        var brush = new ImageBrush(image);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        _mesh.TriangleIndices = Triangles();
        _viewport.Camera = new OrthographicCamera(
            new Point3D(_bounds.Width / 2, -_bounds.Height / 2, 10),
            new Vector3D(0, 0, -1),
            new Vector3D(0, 1, 0),
            _bounds.Width);
        _viewport.Children.Add(new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children = { new AmbientLight(Colors.White), new GeometryModel3D(_mesh, material) { BackMaterial = material } },
            },
        });

        Content = _viewport;
        Update(_isReversed ? 1 : 0);
        SourceInitialized += OnSourceInitialized;
        ContentRendered += (_, _) =>
        {
            _isRendered = true;
            if (_isPlaying)
            {
                Start();
            }
        };
    }

    /// <summary>Cria a janela fora da tela e desenha o primeiro quadro, para o efeito começar sem espera.</summary>
    public void Prepare()
    {
        if (_source is null)
        {
            Show();
        }
    }

    /// <summary>Começa o efeito (assim que a janela tiver o primeiro quadro pronto).</summary>
    /// <param name="onProgress">Recebe o tempo do efeito: 0 = janela, 1 = no ícone.</param>
    public void Play(Action<double>? onProgress = null, Action? onCompleted = null)
    {
        _onProgress = onProgress;
        _onCompleted = onCompleted;
        _isPlaying = true;
        if (_source is null)
        {
            Show();
        }
        else if (_isRendered)
        {
            Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        base.OnClosed(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private static Int32Collection Triangles()
    {
        var indices = new Int32Collection(Rows * 6);
        for (var row = 0; row < Rows; row++)
        {
            var first = row * 2;
            indices.Add(first);
            indices.Add(first + 2);
            indices.Add(first + 1);
            indices.Add(first + 1);
            indices.Add(first + 2);
            indices.Add(first + 3);
        }

        indices.Freeze();
        return indices;
    }

    private Rect Offset(Rect rect) => new(rect.X - _bounds.X, rect.Y - _bounds.Y, rect.Width, rect.Height);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this);
        _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(_source.Handle, ref margins);
        var corners = DoNotRound;
        DwmSetWindowAttribute(_source.Handle, DwmwaWindowCornerPreference, ref corners, sizeof(int));
        var border = ColorNone;
        DwmSetWindowAttribute(_source.Handle, DwmwaBorderColor, ref border, sizeof(int));
        User32.AddExtendedStyle(_source.Handle, User32.WsExToolWindow | User32.WsExNoActivate | User32.WsExTransparent);

        var screen = User32.GetVirtualScreen();
        User32.SetWindowPos(
            _source.Handle, User32.HwndTopMost, screen.Right + 64, (int)_bounds.Y, (int)_bounds.Width, (int)_bounds.Height,
            User32.SwpNoActivate);
    }

    private void Start()
    {
        if (_clock.IsRunning || _isFinished)
        {
            return;
        }

        User32.SetWindowPos(
            _source!.Handle, User32.HwndTopMost, (int)_bounds.X, (int)_bounds.Y, (int)_bounds.Width, (int)_bounds.Height,
            User32.SwpNoActivate);
        _clock.Start();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var t = Math.Clamp(_clock.Elapsed / _duration, 0, 1);
        var time = _isReversed ? 1 - t : t;
        Update(time);
        _onProgress?.Invoke(time);
        if (t < 1)
        {
            return;
        }

        // Um quadro a mais antes de sumir: a janela de verdade (ao restaurar) já está por baixo.
        if (!_isFinished)
        {
            _isFinished = true;
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _onCompleted?.Invoke();
        Close();
    }

    private void Update(double time)
    {
        var positions = new Point3DCollection((Rows + 1) * 2);
        var texture = new PointCollection((Rows + 1) * 2);
        foreach (var (start, end, startTexture, endTexture) in _genie.Outline(time, Rows))
        {
            positions.Add(new Point3D(start.X, -start.Y, 0));
            positions.Add(new Point3D(end.X, -end.Y, 0));
            texture.Add(startTexture);
            texture.Add(endTexture);
        }

        positions.Freeze();
        texture.Freeze();
        _mesh.Positions = positions;
        _mesh.TextureCoordinates = texture;

        // Já dentro do ícone, a janela some em vez de ficar como um risco por cima dele.
        _viewport.Opacity = Math.Clamp((1 - time) / 0.12, 0, 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}
