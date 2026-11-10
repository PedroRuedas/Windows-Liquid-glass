using LiquidGlass.Models;
using LiquidGlass.Services;

namespace LiquidGlass.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void UserFolderIsHidden()
    {
        var log = @"em C:\Users\maria\AppData\Roaming\LiquidGlass\settings.json e file:///C:/Users/maria/Documents/x.txt";

        var anonymized = Diagnostics.Anonymize(log, @"C:\Users\maria");

        Assert.DoesNotContain("maria", anonymized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\AppData\Roaming", anonymized, StringComparison.Ordinal);
        Assert.Contains("%USERPROFILE%/Documents", anonymized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Windows 10 Pro", "24H2", "26100", 2314, "Windows 11 Pro 24H2 (build 26100.2314)")]
    [InlineData("Windows 10 Home", "22H2", "19045", 5011, "Windows 10 Home 22H2 (build 19045.5011)")]
    [InlineData(null, null, "22000", 0, "Windows (build 22000.0)")]
    public void WindowsElevenIsNamedCorrectly(string? product, string? display, string build, int revision, string expected)
    {
        Assert.Equal(expected, Diagnostics.DescribeWindows(product, display, build, revision));
    }

    [Fact]
    public void ReportHasTheMainSections()
    {
        var report = Sta.RunAndReturn(() => Diagnostics.Build(new DockSettings()));

        Assert.Contains("### Liquid Glass Dock", report, StringComparison.Ordinal);
        Assert.Contains("### Monitores", report, StringComparison.Ordinal);
        Assert.Contains("### Desempenho", report, StringComparison.Ordinal);
        Assert.Contains("Memória privada", report, StringComparison.Ordinal);
        Assert.Contains("### Preferências", report, StringComparison.Ordinal);
        Assert.Contains("### Últimos erros", report, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName + @"\", report, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 42, "0 min 42 s")]
    [InlineData(1, 5, "1 h 01 min")]
    public void DurationsReadNaturally(int hours, int seconds, string expected)
    {
        var span = hours > 0 ? TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, Diagnostics.FormatDuration(span));
    }

    [Theory]
    [InlineData("Calculadora", "C")]
    [InlineData("área de trabalho", "A")]
    [InlineData("Ícones", "I")]
    [InlineData("7-Zip", "#")]
    [InlineData("  Notepad", "N")]
    [InlineData("", "#")]
    [InlineData(null, "#")]
    public void AllAppsAreGroupedByTheirInitial(string? name, string expected)
    {
        Assert.Equal(expected, StartMenuCatalog.Initial(name));
    }
}
