using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LiquidGlass.Effects;

/// <summary>
/// Efeito de pixel shader que transforma um recorte do papel de parede em vidro líquido.
/// Os parâmetros espelham os registradores de <c>Shaders/LiquidGlass.fx</c>.
/// </summary>
public sealed class LiquidGlassEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidGlassEffect), 0);

    public static readonly DependencyProperty SizeProperty = RegisterConstant(nameof(Size), typeof(Size), new Size(1, 1), 0);

    public static readonly DependencyProperty PaddingProperty = RegisterConstant(nameof(Padding), typeof(double), 0.0, 1);

    public static readonly DependencyProperty CornerRadiusProperty = RegisterConstant(nameof(CornerRadius), typeof(double), 20.0, 2);

    public static readonly DependencyProperty BezelWidthProperty = RegisterConstant(nameof(BezelWidth), typeof(double), 14.0, 3);

    public static readonly DependencyProperty RefractionProperty = RegisterConstant(nameof(Refraction), typeof(double), 10.0, 4);

    public static readonly DependencyProperty ChromaticAberrationProperty =
        RegisterConstant(nameof(ChromaticAberration), typeof(double), 0.3, 5);

    public static readonly DependencyProperty TintProperty = RegisterConstant(nameof(Tint), typeof(Color), Color.FromArgb(30, 255, 255, 255), 6);

    public static readonly DependencyProperty HighlightProperty = RegisterConstant(nameof(Highlight), typeof(double), 0.8, 7);

    public static readonly DependencyProperty ShadowProperty = RegisterConstant(nameof(Shadow), typeof(double), 0.3, 8);

    public static readonly DependencyProperty SaturationProperty = RegisterConstant(nameof(Saturation), typeof(double), 1.2, 9);

    public static readonly DependencyProperty LightDirectionProperty =
        RegisterConstant(nameof(LightDirection), typeof(Vector), new Vector(0, -1), 10);

    private static readonly PixelShader Shader = new()
    {
        UriSource = new Uri("pack://application:,,,/LiquidGlass;component/Effects/Shaders/LiquidGlass.ps"),
    };

    public LiquidGlassEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(SizeProperty);
        UpdateShaderValue(PaddingProperty);
        UpdateShaderValue(CornerRadiusProperty);
        UpdateShaderValue(BezelWidthProperty);
        UpdateShaderValue(RefractionProperty);
        UpdateShaderValue(ChromaticAberrationProperty);
        UpdateShaderValue(TintProperty);
        UpdateShaderValue(HighlightProperty);
        UpdateShaderValue(ShadowProperty);
        UpdateShaderValue(SaturationProperty);
        UpdateShaderValue(LightDirectionProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public Size Size
    {
        get => (Size)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double Padding
    {
        get => (double)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
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

    public Color Tint
    {
        get => (Color)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
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

    public Vector LightDirection
    {
        get => (Vector)GetValue(LightDirectionProperty);
        set => SetValue(LightDirectionProperty, value);
    }

    private static DependencyProperty RegisterConstant(string name, Type type, object defaultValue, int register)
    {
        return DependencyProperty.Register(
            name,
            type,
            typeof(LiquidGlassEffect),
            new UIPropertyMetadata(defaultValue, PixelShaderConstantCallback(register)));
    }
}
