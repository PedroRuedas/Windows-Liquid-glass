using System.Windows;
using System.Windows.Media;
using LiquidGlass.Services;

namespace LiquidGlass.Graphics;

/// <summary>
/// Ícone de rede no estilo do macOS, na mesma área dos outros ícones (6,6,88,88 em um quadro de 100):
/// o leque do Wi-Fi com as faixas acesas pela força do sinal, ou "‹···›" para a rede cabeada.
/// </summary>
internal static class NetworkIcon
{
    private static readonly Point Center = new(50, 75);
    private static readonly Brush Lit = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));

    /// <summary>Quantas faixas do Wi-Fi acendem (0 a 3) para um sinal de 0 a 100.</summary>
    public static int Bars(int signal) => signal switch
    {
        >= 67 => 3,
        >= 34 => 2,
        > 0 => 1,
        _ => 0,
    };

    public static ImageSource Create(NetworkKind kind, int bars, bool hasWifi)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(6, 6, 88, 88))));
        if (kind == NetworkKind.Ethernet || (kind == NetworkKind.None && !hasWifi))
        {
            DrawEthernet(group, kind == NetworkKind.Ethernet ? Lit : Dim);
        }
        else
        {
            DrawWifi(group, kind == NetworkKind.Wifi ? Math.Clamp(bars, 1, 3) : 0);
        }

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Um setor no centro e dois arcos, abrindo 90° para cima.</summary>
    private static void DrawWifi(DrawingGroup group, int bars)
    {
        const double Spread = 45;
        var dot = new PathGeometry([new PathFigure(Center, [new LineSegment(Arc(13, -Spread), true), new ArcSegment(Arc(13, Spread), new Size(13, 13), 0, false, SweepDirection.Clockwise, true)], true)]);
        group.Children.Add(new GeometryDrawing(bars >= 1 ? Lit : Dim, null, dot));

        double[] radii = [30, 47];
        for (var index = 0; index < radii.Length; index++)
        {
            var radius = radii[index];
            var arc = new PathGeometry([new PathFigure(Arc(radius, -Spread), [new ArcSegment(Arc(radius, Spread), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true)], false)]);
            var pen = new Pen(bars >= index + 2 ? Lit : Dim, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            group.Children.Add(new GeometryDrawing(null, pen, arc));
        }
    }

    private static void DrawEthernet(DrawingGroup group, Brush brush)
    {
        var pen = new Pen(brush, 7.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var brackets = Geometry.Parse("M 30,33 L 13,50 L 30,67 M 70,33 L 87,50 L 70,67");
        group.Children.Add(new GeometryDrawing(null, pen, brackets));
        foreach (var x in new[] { 38.0, 50.0, 62.0 })
        {
            group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(x, 50), 4.5, 4.5)));
        }
    }

    /// <summary>Ponto a <paramref name="radius"/> do centro, <paramref name="degrees"/> a partir da vertical.</summary>
    private static Point Arc(double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(Center.X + (radius * Math.Sin(radians)), Center.Y - (radius * Math.Cos(radians)));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
