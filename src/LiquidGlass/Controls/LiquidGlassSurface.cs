using System.Windows;
using System.Windows.Media;
using LiquidGlass.Effects;

namespace LiquidGlass.Controls;

/// <summary>
/// Superfície de vidro líquido. Desenha o trecho do papel de parede que está exatamente atrás
/// dela e aplica o <see cref="LiquidGlassEffect"/>. A forma ocupa a área do elemento menos
/// <see cref="ShadowPadding"/>, que fica reservado para a sombra.
/// </summary>
public sealed class LiquidGlassSurface : FrameworkElement
{
    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop),
        typeof(ImageSource),
        typeof(LiquidGlassSurface),
        new PropertyMetadata(null, (d, _) => ((LiquidGlassSurface)d).OnBackdropChanged()));

    /// <summary>Canto superior esquerdo do monitor, em pixels físicos, dentro do <see cref="Backdrop"/>.</summary>
    public static readonly DependencyProperty BackdropOriginProperty = DependencyProperty.Register(
        nameof(BackdropOrigin),
        typeof(Point),
        typeof(LiquidGlassSurface),
        new PropertyMetadata(default(Point), (d, _) => ((LiquidGlassSurface)d).UpdateViewbox(force: true)));

    public static readonly DependencyProperty ShadowPaddingProperty = DependencyProperty.Register(
        nameof(ShadowPadding),
        typeof(double),
        typeof(LiquidGlassSurface),
        new PropertyMetadata(16.0, (d, e) => ((LiquidGlassSurface)d)._effect.Padding = (double)e.NewValue));

    public static readonly DependencyProperty CornerRadiusProperty = Forward(nameof(CornerRadius), 22.0, (e, v) => e.CornerRadius = v);

    public static readonly DependencyProperty BezelWidthProperty = Forward(nameof(BezelWidth), 16.0, (e, v) => e.BezelWidth = v);

    public static readonly DependencyProperty RefractionProperty = Forward(nameof(Refraction), 12.0, (e, v) => e.Refraction = v);

    public static readonly DependencyProperty ChromaticAberrationProperty =
        Forward(nameof(ChromaticAberration), 0.3, (e, v) => e.ChromaticAberration = v);

    public static readonly DependencyProperty HighlightProperty = Forward(nameof(Highlight), 0.8, (e, v) => e.Highlight = v);

    public static readonly DependencyProperty ShadowProperty = Forward(nameof(Shadow), 0.3, (e, v) => e.Shadow = v);

    public static readonly DependencyProperty SaturationProperty = Forward(nameof(Saturation), 1.2, (e, v) => e.Saturation = v);

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint),
        typeof(Color),
        typeof(LiquidGlassSurface),
        new PropertyMetadata(Color.FromArgb(36, 255, 255, 255), (d, e) => ((LiquidGlassSurface)d)._effect.Tint = (Color)e.NewValue));

    public static readonly DependencyProperty LightDirectionProperty = DependencyProperty.Register(
        nameof(LightDirection),
        typeof(Vector),
        typeof(LiquidGlassSurface),
        new PropertyMetadata(new Vector(0, -1), (d, e) => ((LiquidGlassSurface)d)._effect.LightDirection = (Vector)e.NewValue));

    private readonly LiquidGlassEffect _effect = new();
    private readonly ImageBrush _brush = new()
    {
        Stretch = Stretch.Fill,
        ViewboxUnits = BrushMappingMode.Absolute,
    };

    private Rect _lastScreenRect = Rect.Empty;

    public LiquidGlassSurface()
    {
        Effect = _effect;
        IsHitTestVisible = true;
        Loaded += (_, _) => LayoutUpdated += OnLayoutUpdated;
        Unloaded += (_, _) => LayoutUpdated -= OnLayoutUpdated;
    }

    public ImageSource? Backdrop
    {
        get => (ImageSource?)GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    public Point BackdropOrigin
    {
        get => (Point)GetValue(BackdropOriginProperty);
        set => SetValue(BackdropOriginProperty, value);
    }

    public double ShadowPadding
    {
        get => (double)GetValue(ShadowPaddingProperty);
        set => SetValue(ShadowPaddingProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double BezelWidth
    {
        get => (double)GetValue(BezelWidthProperty);
        set => SetValue(BezelWidthProperty, value);
    }

    public double Refraction
    {
        get => (double)GetValue(RefractionProperty);
        set => SetValue(RefractionProperty, value);
    }

    public double ChromaticAberration
    {
        get => (double)GetValue(ChromaticAberrationProperty);
        set => SetValue(ChromaticAberrationProperty, value);
    }

    public double Highlight
    {
        get => (double)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    public double Shadow
    {
        get => (double)GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    public double Saturation
    {
        get => (double)GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public Color Tint
    {
        get => (Color)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public Vector LightDirection
    {
        get => (Vector)GetValue(LightDirectionProperty);
        set => SetValue(LightDirectionProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _effect.Size = sizeInfo.NewSize;
        UpdateViewbox(force: false);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Sem fundo ainda (primeira captura em andamento): um vidro neutro evita um "buraco" na dock.
        Brush fill = Backdrop is null ? Brushes.Gray : _brush;
        drawingContext.DrawRectangle(fill, null, new Rect(RenderSize));
    }

    private static DependencyProperty Forward(string name, double defaultValue, Action<LiquidGlassEffect, double> apply)
    {
        return DependencyProperty.Register(
            name,
            typeof(double),
            typeof(LiquidGlassSurface),
            new PropertyMetadata(defaultValue, (d, e) => apply(((LiquidGlassSurface)d)._effect, (double)e.NewValue)));
    }

    private void OnBackdropChanged()
    {
        _brush.ImageSource = Backdrop;
        UpdateViewbox(force: true);
        InvalidateVisual();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateViewbox(force: false);

    /// <summary>Mapeia o recorte do papel de parede para a posição atual do elemento na tela.</summary>
    private void UpdateViewbox(bool force)
    {
        if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } target })
        {
            return;
        }

        var topLeft = PointToScreen(new Point(0, 0));
        var scale = target.TransformToDevice;
        var screenRect = new Rect(
            topLeft.X - BackdropOrigin.X,
            topLeft.Y - BackdropOrigin.Y,
            ActualWidth * scale.M11,
            ActualHeight * scale.M22);

        if (!force && screenRect == _lastScreenRect)
        {
            return;
        }

        _lastScreenRect = screenRect;
        _brush.Viewbox = screenRect;
    }
}
