using System.Windows.Media;
using LiquidGlass.Models;
using Microsoft.Win32;

namespace LiquidGlass.Services;

/// <summary>
/// Modo claro ou escuro e cor de destaque do Windows, para o vidro acompanhar o tema. Lido do
/// registro, onde o próprio Windows guarda as escolhas de Configurações → Personalização → Cores.
/// </summary>
public static class WindowsTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    /// <summary>Tom do vidro no modo escuro, o mesmo do estilo "Escuro".</summary>
    public static readonly Color DarkTint = Color.FromRgb(0x10, 0x10, 0x14);

    /// <summary>O modo da barra de tarefas e do menu Iniciar (o "modo do Windows", não o dos apps).</summary>
    public static bool IsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
    }

    /// <summary>A cor de destaque escolhida; azul padrão do Windows se não houver.</summary>
    public static Color Accent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
        if (key?.GetValue("AccentColor") is int abgr)
        {
            return FromAbgr(unchecked((uint)abgr));
        }

        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    /// <summary>O registro guarda a cor como 0xAABBGGRR.</summary>
    public static Color FromAbgr(uint abgr) =>
        Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));

    /// <summary>
    /// O tom que o vidro usa: o das preferências, ou o que o tema do Windows pede. No modo claro, o
    /// vidro fica mais branco; no escuro, mais fumê; com a cor de destaque, levemente tingido por ela.
    /// </summary>
    public static (Color Color, double Strength) TintFor(GlassTintMode mode, Color custom, double customStrength, bool isLight, Color accent) => mode switch
    {
        GlassTintMode.WindowsTheme => isLight ? (Colors.White, 0.32) : (DarkTint, 0.42),
        GlassTintMode.Accent => (accent, isLight ? 0.26 : 0.34),
        _ => (custom, customStrength),
    };
}
