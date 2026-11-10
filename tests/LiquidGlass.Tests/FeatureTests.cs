using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiquidGlass.Graphics;
using LiquidGlass.Interop;
using LiquidGlass.Models;
using LiquidGlass.Services;

namespace LiquidGlass.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void CustomTintKeepsTheUserChoice()
    {
        var (color, strength) = WindowsTheme.TintFor(GlassTintMode.Custom, Colors.Pink, 0.2, isLight: true, Colors.Blue);

        Assert.Equal(Colors.Pink, color);
        Assert.Equal(0.2, strength);
    }

    [Fact]
    public void WindowsThemeTintFollowsLightAndDark()
    {
        Assert.Equal(Colors.White, WindowsTheme.TintFor(GlassTintMode.WindowsTheme, Colors.Pink, 0.2, isLight: true, Colors.Blue).Color);
        Assert.Equal(WindowsTheme.DarkTint, WindowsTheme.TintFor(GlassTintMode.WindowsTheme, Colors.Pink, 0.2, isLight: false, Colors.Blue).Color);
    }

    [Fact]
    public void AccentTintUsesTheAccentColor()
    {
        Assert.Equal(Colors.Orange, WindowsTheme.TintFor(GlassTintMode.Accent, Colors.Pink, 0.2, isLight: false, Colors.Orange).Color);
    }

    [Fact]
    public void RegistryColorsAreAbgr()
    {
        // 0xFF D4 78 00 no registro é o azul padrão do Windows (#0078D4).
        Assert.Equal(Color.FromRgb(0x00, 0x78, 0xD4), WindowsTheme.FromAbgr(0xFFD47800));
    }
}

public sealed class WallpaperPlacementTests
{
    private static readonly User32.Rect Monitor = new() { Left = 1920, Top = 0, Right = 3840, Bottom = 1080 };
    private static readonly User32.Rect Virtual = new() { Left = 0, Top = 0, Right = 3840, Bottom = 1080 };
    private static readonly Size Wide = new(4000, 1000);

    [Fact]
    public void FillCoversTheMonitorAndCropsTheRest()
    {
        var place = WallpaperRenderer.Place(WallpaperPosition.Fill, Wide, Monitor, Virtual);

        Assert.Equal(1080, place.Height, 3);
        Assert.True(place.Width >= 1920);
        Assert.Equal((1920 - place.Width) / 2, place.X, 3);
    }

    [Fact]
    public void FitShowsTheWholeImage()
    {
        var place = WallpaperRenderer.Place(WallpaperPosition.Fit, Wide, Monitor, Virtual);

        Assert.Equal(1920, place.Width, 3);
        Assert.Equal(480, place.Height, 3);
        Assert.Equal(300, place.Y, 3);
    }

    [Fact]
    public void CenterKeepsTheOriginalSize()
    {
        var place = WallpaperRenderer.Place(WallpaperPosition.Center, new Size(1600, 860), Monitor, Virtual);

        Assert.Equal(new Rect(160, 110, 1600, 860), place);
    }

    [Fact]
    public void SpanShowsThisMonitorsPartOfTheImage()
    {
        // A imagem cobre os dois monitores; o da direita vê a metade direita dela.
        var place = WallpaperRenderer.Place(WallpaperPosition.Span, new Size(3840, 1080), Monitor, Virtual);

        Assert.Equal(new Rect(-1920, 0, 3840, 1080), place);
    }
}

public sealed class MediaMatchingTests
{
    [Theory]
    [InlineData("Spotify.exe", null, @"C:\Users\x\AppData\Roaming\Spotify\Spotify.exe", true)]
    [InlineData("Chrome", null, @"C:\Program Files\Google\Chrome\Application\chrome.exe", true)]
    [InlineData("MSEdge", null, @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", true)]
    [InlineData("308046B0AF4A39CB", "308046B0AF4A39CB", @"C:\Program Files\Mozilla Firefox\firefox.exe", true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", null, true)]
    [InlineData("Spotify.exe", null, @"C:\Program Files\VideoLAN\VLC\vlc.exe", false)]
    [InlineData("", null, @"C:\a\app.exe", false)]
    public void SessionsAreMatchedToTheirApp(string source, string? appId, string? executable, bool expected)
    {
        Assert.Equal(expected, MediaService.Matches(source, [appId], [executable]));
    }
}

public sealed class StackTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LiquidGlassStackTest-" + Guid.NewGuid().ToString("N"));

    public StackTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void NewestItemsComeFirstAndHiddenOnesAreSkipped()
    {
        var old = Write("antigo.txt", DateTime.Now.AddDays(-3));
        var recent = Write("recente.txt", DateTime.Now.AddMinutes(-1));
        var hidden = Write("oculto.txt", DateTime.Now);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(_folder, "Pasta"));
        Directory.SetLastWriteTime(Path.Combine(_folder, "Pasta"), DateTime.Now.AddDays(-1));
        Write("Atalho.lnk", DateTime.Now.AddDays(-2));

        var entries = StackService.Load(_folder);

        Assert.Equal(["recente.txt", "Pasta", "Atalho", "antigo.txt"], entries.Select(entry => entry.Name));
        Assert.True(entries[1].IsFolder);
        Assert.Equal(old, entries[3].Path);
        Assert.Equal(recent, entries[0].Path);
    }

    [Fact]
    public void OnlyRealFoldersBecomeStacks()
    {
        Assert.True(StackService.IsFolder(_folder));
        Assert.False(StackService.IsFolder(Path.Combine(_folder, "nao-existe")));
        Assert.False(StackService.IsFolder("shell:RecycleBinFolder"));
        Assert.False(StackService.IsFolder(null));
    }

    [Fact]
    public void AMissingFolderIsAnEmptyStack()
    {
        Assert.Empty(StackService.Load(Path.Combine(_folder, "nao-existe")));
    }

    private string Write(string name, DateTime modified)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTime(path, modified);
        return path;
    }
}

public sealed class ShortcutTests
{
    [Theory]
    [InlineData(0x31u, 0)]
    [InlineData(0x35u, 4)]
    [InlineData(0x39u, 8)]
    public void NumberKeysMapToDockPositions(uint key, int expected)
    {
        Assert.Equal(expected, GlobalInputHook.AppKeyIndex(key));
    }

    [Theory]
    [InlineData(0x30u)]
    [InlineData(0x41u)]
    [InlineData(0x61u)]
    public void OtherKeysAreLeftToWindows(uint key)
    {
        Assert.Null(GlobalInputHook.AppKeyIndex(key));
    }
}

public sealed class CustomIconTests
{
    [Fact]
    public void ImagesAreCopiedAndLoaded()
    {
        var source = Path.Combine(Path.GetTempPath(), "LiquidGlassIconTest-" + Guid.NewGuid().ToString("N") + ".png");
        var pixels = new byte[64 * 64 * 4];
        Array.Fill<byte>(pixels, 200);
        using (var file = File.Create(source))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4)));
            encoder.Save(file);
        }

        string? stored = null;
        try
        {
            stored = CustomIcons.Import(source);

            Assert.NotNull(stored);
            Assert.StartsWith(CustomIcons.Folder, stored, StringComparison.OrdinalIgnoreCase);
            File.Delete(source);
            Assert.Equal(64, CustomIcons.Load(stored)?.PixelWidth);
        }
        finally
        {
            File.Delete(source);
            CustomIcons.Delete(stored);
        }

        Assert.False(File.Exists(stored));
    }

    [Fact]
    public void NonImagesAreRejected()
    {
        var source = Path.Combine(Path.GetTempPath(), "LiquidGlassIconTest-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(source, "não é uma imagem");
        try
        {
            Assert.Null(CustomIcons.Import(source));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void ProgramsGiveTheirIcon()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        var stored = Sta.RunAndReturn(() => CustomIcons.Import(notepad));
        try
        {
            Assert.NotNull(stored);
            Assert.EndsWith(".png", stored, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(CustomIcons.Load(stored));
        }
        finally
        {
            CustomIcons.Delete(stored);
        }
    }

    [Fact]
    public void OnlyCopiesInTheIconFolderAreDeleted()
    {
        var outside = Path.Combine(Path.GetTempPath(), "LiquidGlassKeep-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(outside, "x");
        try
        {
            CustomIcons.Delete(outside);
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }
}
