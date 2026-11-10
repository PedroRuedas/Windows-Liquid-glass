using LiquidGlass.Services;

namespace LiquidGlass.Tests;

public sealed class RunningOrderTests
{
    [Fact]
    public void AfterARestartAppsComeBackInTheSavedOrder()
    {
        var saved = new[] { "code.exe", "chrome.exe", "spotify.exe" };

        var arranged = RunningOrder.Arrange([], ["spotify.exe", "code.exe", "chrome.exe"], key => key, saved);

        Assert.Equal(["code.exe", "chrome.exe", "spotify.exe"], arranged);
    }

    [Fact]
    public void AppsWithoutASavedPlaceGoToTheEnd()
    {
        var arranged = RunningOrder.Arrange([], ["notepad.exe", "chrome.exe", "paint.exe"], key => key, ["chrome.exe"]);

        Assert.Equal(["chrome.exe", "notepad.exe", "paint.exe"], arranged);
    }

    [Fact]
    public void AReopenedAppReturnsToItsPlaceWithoutMovingTheOthers()
    {
        var saved = new[] { "code.exe", "chrome.exe", "spotify.exe" };

        var arranged = RunningOrder.Arrange(["code.exe", "spotify.exe"], ["chrome.exe"], key => key, saved);

        Assert.Equal(["code.exe", "chrome.exe", "spotify.exe"], arranged);
    }

    [Fact]
    public void KeysIgnoreCase()
    {
        var arranged = RunningOrder.Arrange([], [@"C:\APPS\B.EXE", @"c:\apps\a.exe"], key => key, [@"C:\Apps\A.exe", @"C:\Apps\B.exe"]);

        Assert.Equal([@"c:\apps\a.exe", @"C:\APPS\B.EXE"], arranged);
    }

    [Fact]
    public void RememberKeepsClosedAppsInPlace()
    {
        // "chrome" está fechado; "code" e "spotify" trocaram de lugar.
        var saved = new[] { "code.exe", "chrome.exe", "spotify.exe" };

        var remembered = RunningOrder.Remember(saved, ["spotify.exe", "code.exe"]);

        Assert.Equal(["spotify.exe", "chrome.exe", "code.exe"], remembered);
    }

    [Fact]
    public void RememberAddsAppsSeenForTheFirstTime()
    {
        var remembered = RunningOrder.Remember(["code.exe"], ["paint.exe", "code.exe"]);

        Assert.Equal(["paint.exe", "code.exe"], remembered);
    }

    [Fact]
    public void RememberDropsClosedAppsBeyondTheCapacity()
    {
        var saved = Enumerable.Range(0, RunningOrder.Capacity).Select(index => $"app{index}.exe").ToList();

        var remembered = RunningOrder.Remember(saved, ["new.exe"]);

        Assert.Equal(RunningOrder.Capacity, remembered.Count);
        Assert.Contains("new.exe", remembered);
        Assert.Equal("app0.exe", remembered[0]);
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App\app.exe")]
    [InlineData("hwnd:1234", null)]
    [InlineData(null, null)]
    public void OnlyStableKeysAreSaved(string? groupKey, string? expected)
    {
        Assert.Equal(expected, RunningOrder.KeyOf(groupKey));
    }
}
