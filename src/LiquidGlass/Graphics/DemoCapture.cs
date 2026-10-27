using System.Diagnostics;
using System.Windows;
using LiquidGlass.Services;

namespace LiquidGlass.Graphics;

/// <summary>
/// Demonstrações (Debug): fotografa a tela em volta de elementos abertos e salva em
/// %AppData%\LiquidGlass. A captura é do que o DWM compôs, então mostra também as miniaturas de
/// janelas e o vidro dos popups, que nenhum RenderTargetBitmap enxergaria.
/// </summary>
internal static class DemoCapture
{
    private const double Margin = 20;

    [Conditional("DEBUG")]
    public static void Save(string name, params FrameworkElement?[] elements)
    {
        var area = Rect.Empty;
        foreach (var element in elements)
        {
            if (element is not null && PresentationSource.FromVisual(element) is not null)
            {
                area.Union(new Rect(element.PointToScreen(new Point(0, 0)), element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight))));
            }
        }

        if (area.IsEmpty)
        {
            return;
        }

        area.Inflate(Margin, Margin);
        using var bitmap = new System.Drawing.Bitmap((int)area.Width, (int)area.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen((int)area.X, (int)area.Y, 0, 0, bitmap.Size);
        }

        bitmap.Save(Path.Combine(AppDataPaths.Root, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }
}
