using LiquidGlass.Models;
using LiquidGlass.Services;

namespace LiquidGlass.Tests;

[Collection(LanguageCollection.Name)]
public sealed class StartMenuCatalogTests
{
    [Theory]
    [InlineData("Calculadora", "calc", 3)]
    [InlineData("Visual Studio Code", "code", 2)]
    [InlineData("Ferramenta de Captura", "captu", 2)]
    [InlineData("Configurações", "configuracoes", 3)]
    [InlineData("Notepad++", "pad", 1)]
    [InlineData("Paint", "word", 0)]
    [InlineData("Wi-Fi", "wifi", 3)]
    public void ScoreRanksStartThenWordThenAnywhere(string name, string query, int expected)
    {
        Assert.Equal(expected, StartMenuCatalog.Score(name, query));
    }

    [Fact]
    public void AppsStartingWithTheQueryComeFirst()
    {
        var apps = new[] { "Notepad++", "Bloco de notas", "Notas Autoadesivas" }
            .Select(name => new StartEntry(name, name, StartEntryKind.App, name))
            .ToList();

        var results = StartMenuCatalog.SearchApps(apps, "nota", 6);

        Assert.Equal(["Notas Autoadesivas", "Bloco de notas"], results.Select(entry => entry.Name));
    }

    [Fact]
    public void SettingsAreFoundByRelatedWords()
    {
        using (Language.Portuguese())
        {
            var results = StartMenuCatalog.SearchSettings("papel de parede", 3);

            Assert.Equal("Plano de fundo", results[0].Name);
            Assert.Equal("ms-settings:personalization-background", results[0].Target);
        }

        using (Language.English())
        {
            var results = StartMenuCatalog.SearchSettings("wallpaper", 3);

            Assert.Equal("Background", results[0].Name);
            Assert.Equal("ms-settings:personalization-background", results[0].Target);
        }
    }

    [Fact]
    public void RelativeTimeReadsLikeTheWindowsStartMenu()
    {
        var now = new DateTime(2026, 11, 4, 10, 0, 0);
        using var language = Language.Portuguese();

        Assert.Equal("Agora", StartMenuCatalog.RelativeTime(now.AddSeconds(-20), now));
        Assert.Equal("Há 5 min", StartMenuCatalog.RelativeTime(now.AddMinutes(-5), now));
        Assert.Equal("Há 20 h", StartMenuCatalog.RelativeTime(now.AddHours(-20), now));
        Assert.Equal("Ontem", StartMenuCatalog.RelativeTime(new DateTime(2026, 11, 3, 8, 0, 0), now));
        Assert.Equal("27 de out", StartMenuCatalog.RelativeTime(new DateTime(2026, 10, 27, 15, 0, 0), now));
        Assert.Equal("2 de jan de 2025", StartMenuCatalog.RelativeTime(new DateTime(2025, 1, 2), now));
    }

    [Fact]
    public void RelativeTimeInEnglish()
    {
        var now = new DateTime(2026, 11, 4, 10, 0, 0);
        using var language = Language.English();

        Assert.Equal("Now", StartMenuCatalog.RelativeTime(now.AddSeconds(-20), now));
        Assert.Equal("5 min ago", StartMenuCatalog.RelativeTime(now.AddMinutes(-5), now));
        Assert.Equal("20 h ago", StartMenuCatalog.RelativeTime(now.AddHours(-20), now));
        Assert.Equal("Yesterday", StartMenuCatalog.RelativeTime(new DateTime(2026, 11, 3, 8, 0, 0), now));
        Assert.Equal("Oct 27", StartMenuCatalog.RelativeTime(new DateTime(2026, 10, 27, 15, 0, 0), now));
        Assert.Equal("Jan 2, 2025", StartMenuCatalog.RelativeTime(new DateTime(2025, 1, 2), now));
    }

    [Theory]
    [InlineData("ms-gamingoverlay--startuptips-TitleId=1.lnk", true)]
    [InlineData("ms-settingssound (4).lnk", true)]
    [InlineData("https--claude.ai-artifact.lnk", true)]
    [InlineData("Trabalho-Projeto de BD.lnk", false)]
    [InlineData("msword notes.lnk", false)]
    public void ProtocolShortcutsAreNotRecentFiles(string name, bool expected)
    {
        Assert.Equal(expected, StartMenuCatalog.IsProtocolShortcut(name));
    }

    [Theory]
    [InlineData(@"C:\Users\pedro\Documents\tomorrows-end\Library\PackageCache\com.unity\Calculate.cs", true)]
    [InlineData(@"C:\Users\pedro\Documents\app\node_modules\calc\index.js", true)]
    [InlineData(@"C:\Users\pedro\Documents\repo\.git\config", true)]
    [InlineData(@"C:\Users\pedro\Documents\game\Assets\Player.cs.meta", true)]
    [InlineData(@"C:\Users\pedro\Documents\Trabalho Semestral.doc", false)]
    [InlineData(@"C:\Users\pedro\Downloads\Trabalho-Projeto de BD-Petshop-CCO5P.pdf", false)]
    public void CachesAndHiddenFoldersAreNotSearchResults(string path, bool expected)
    {
        Assert.Equal(expected, StartMenuCatalog.IsNoise(path));
    }

    [Fact]
    public void LayoutWithoutPinsGivesNoPins()
    {
        Assert.Empty(StartMenuCatalog.ParseStartLayout("""{"pinnedList":"[0]{packagedAppId:string?,desktopAppLink:string?}\n"}"""));
        Assert.Empty(StartMenuCatalog.ParseStartLayout("{}"));
    }
}
