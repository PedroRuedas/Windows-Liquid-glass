using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using LiquidGlass.Models;
using LiquidGlass.Resources;
using LiquidGlass.Services;

namespace LiquidGlass.Tests;

[Collection(LanguageCollection.Name)]
public sealed partial class LocalizationTests
{
    private static readonly ResourceManager Resources = new("LiquidGlass.Resources.Strings", typeof(Strings).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    [Fact]
    public void BothLanguagesHaveTheSameTexts()
    {
        var portuguese = Texts(CultureInfo.InvariantCulture);
        var english = Texts(CultureInfo.GetCultureInfo("en"));

        Assert.Empty(portuguese.Keys.Except(english.Keys));
        Assert.Empty(english.Keys.Except(portuguese.Keys));
    }

    [Fact]
    public void TranslationsKeepTheSamePlaceholders()
    {
        var portuguese = Texts(CultureInfo.InvariantCulture);
        var english = Texts(CultureInfo.GetCultureInfo("en"));

        var mismatched = portuguese.Keys
            .Where(key => english.ContainsKey(key))
            .Where(key => !Placeholders(portuguese[key]).SequenceEqual(Placeholders(english[key])))
            .ToList();

        Assert.Empty(mismatched);
    }

    [Fact]
    public void EveryTextInStringsHasAnEntry()
    {
        var keys = Texts(CultureInfo.InvariantCulture).Keys.ToHashSet();
        var members = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .Concat(typeof(Strings).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.ReturnType == typeof(string) && !method.IsSpecialName && method.Name != nameof(Strings.Lookup))
                .Select(method => method.Name))
            .ToList();

        Assert.NotEmpty(members);
        Assert.All(members, member => Assert.Contains(member, keys));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EverySettingsPageHasANameAndKeywords(string culture)
    {
        using var language = culture == "en-US" ? Language.English() : Language.Portuguese();
        var pages = (IEnumerable)typeof(StartMenuCatalog)
            .GetField("SettingsPages", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        foreach (var page in pages)
        {
            var name = (string)page.GetType().GetProperty("Name")!.GetValue(page)!;
            var keywords = (string)page.GetType().GetProperty("Keywords")!.GetValue(page)!;
            Assert.DoesNotContain("SettingsPage", name, StringComparison.Ordinal);
            Assert.DoesNotContain("SettingsPage", keywords, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(AppLanguage.Automatic, "pt-BR", "pt-BR")]
    [InlineData(AppLanguage.Automatic, "pt-PT", "pt-PT")]
    [InlineData(AppLanguage.Automatic, "en-GB", "en-GB")]
    [InlineData(AppLanguage.Automatic, "es-ES", "en-US")]
    [InlineData(AppLanguage.Automatic, "de-DE", "en-US")]
    [InlineData(AppLanguage.Portuguese, "en-US", "pt-BR")]
    [InlineData(AppLanguage.Portuguese, "pt-PT", "pt-PT")]
    [InlineData(AppLanguage.English, "pt-BR", "en-US")]
    [InlineData(AppLanguage.English, "en-AU", "en-AU")]
    public void LanguageFollowsWindowsOrTheChoice(AppLanguage choice, string windows, string expected)
    {
        Assert.Equal(expected, Strings.Resolve(choice, CultureInfo.GetCultureInfo(windows)).Name);
    }

    [Fact]
    public void TextsAndFormattingFollowTheLanguage()
    {
        using (Language.Portuguese())
        {
            Assert.Equal("Nova janela", Strings.NewWindow);
            Assert.Equal("Bateria: 78%", Strings.BatteryPercent(78));
            Assert.Equal("2 h 05 min restantes", Strings.BatteryHoursLeft(2, 5));
            Assert.Equal("Atualizar para a versão 1.2.3", Strings.MenuUpdateTo(new Version(1, 2, 3)));
        }

        using (Language.English())
        {
            Assert.Equal("New window", Strings.NewWindow);
            Assert.Equal("Battery: 78%", Strings.BatteryPercent(78));
            Assert.Equal("2 h 05 min remaining", Strings.BatteryHoursLeft(2, 5));
            Assert.Equal("Update to version 1.2.3", Strings.MenuUpdateTo(new Version(1, 2, 3)));
        }
    }

    private static Dictionary<string, string> Texts(CultureInfo culture)
    {
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static List<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal).ToList();

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
