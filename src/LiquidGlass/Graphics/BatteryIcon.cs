using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LiquidGlass.Graphics;

/// <summary>
/// Ícone da bateria no estilo do iOS: uma pílula com a carga preenchida e a porcentagem dentro.
/// Branco normalmente, verde carregando, amarelo na economia de energia e vermelho com 20% ou menos.
/// Desenhado na mesma área dos outros ícones (6,6,88,88 em um quadro de 100), sem fundo próprio.
/// </summary>
internal static class BatteryIcon
{
    private static readonly Rect Body = new(12, 31, 70, 38);
    private static readonly Rect Nub = new(85, 43, 5, 14);
    private static readonly Brush Track = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Normal = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Charging = Frozen(Color.FromRgb(0x34, 0xC7, 0x59));
    private static readonly Brush Low = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Brush Saver = Frozen(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static readonly Brush TextBrush = Frozen(Color.FromArgb(0xF0, 0x10, 0x10, 0x14));
    private static readonly Typeface Font = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static ImageSource Create(int percent, bool isCharging, bool isSaverOn)
    {
        percent = Math.Clamp(percent, 0, 100);
        var fill = isCharging ? Charging : isSaverOn ? Saver : percent <= 20 ? Low : Normal;
        var level = new Rect(Body.X, Body.Y, Body.Width * percent / 100, Body.Height);
        var body = new RectangleGeometry(Body, 12, 12);
        var filled = Geometry.Combine(body, new RectangleGeometry(level), GeometryCombineMode.Intersect, null);

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(6, 6, 88, 88))));
        group.Children.Add(new GeometryDrawing(Track, null, body));
        group.Children.Add(new GeometryDrawing(fill, null, filled));
        group.Children.Add(new GeometryDrawing(percent >= 100 ? fill : Track, null, new RectangleGeometry(Nub, 2.5, 2.5)));

        // Trilho claro e número escuro: legível sobre a carga e sobre o trilho, em qualquer nível.
        group.Children.Add(new GeometryDrawing(TextBrush, null, Text(percent.ToString(CultureInfo.CurrentCulture))));

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Geometry Text(string value)
    {
        var formatted = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 27, Brushes.White, 1.0);
        var origin = new Point(
            Body.X + ((Body.Width - formatted.Width) / 2),
            Body.Y + ((Body.Height - formatted.Height) / 2));
        return formatted.BuildGeometry(origin);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
