using System.Windows.Media;

namespace LiquidGlass.Models;

/// <summary>Combinações prontas dos parâmetros do vidro.</summary>
public sealed record GlassPreset(
    string Name,
    double Refraction,
    double Blur,
    double ChromaticAberration,
    double Highlight,
    double Saturation,
    Color TintColor,
    double TintStrength,
    double Shadow)
{
    public static IReadOnlyList<GlassPreset> All { get; } =
    [
        new("Cristalino", 0.7, 6, 0.35, 0.8, 1.25, Colors.White, 0.14, 0.35),
        new("Fosco", 0.35, 22, 0.1, 0.6, 1.1, Colors.White, 0.28, 0.3),
        new("Escuro", 0.6, 10, 0.25, 0.55, 1.15, Color.FromRgb(0x10, 0x10, 0x14), 0.45, 0.45),
        new("Prisma", 1.0, 3, 1.0, 1.0, 1.5, Colors.White, 0.06, 0.3),
    ];

    public void ApplyTo(DockSettings settings)
    {
        settings.Refraction = Refraction;
        settings.Blur = Blur;
        settings.ChromaticAberration = ChromaticAberration;
        settings.Highlight = Highlight;
        settings.Saturation = Saturation;
        settings.TintColor = TintColor;
        settings.TintStrength = TintStrength;
        settings.Shadow = Shadow;
    }
}
