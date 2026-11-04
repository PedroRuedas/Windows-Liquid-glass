using LiquidGlass.Services;

namespace LiquidGlass.Tests;

public sealed class StartKeyDetectorTests
{
    private const uint LeftWin = 0x5B;
    private const uint RightWin = 0x5C;
    private const uint KeyE = 0x45;
    private const uint Tab = 0x09;

    [Fact]
    public void WindowsKeyAloneOpensStart()
    {
        var detector = new StartKeyDetector();

        Assert.False(detector.OnKey(LeftWin, isDown: true));
        Assert.True(detector.OnKey(LeftWin, isDown: false));
    }

    [Fact]
    public void RightWindowsKeyAlsoOpensStart()
    {
        var detector = new StartKeyDetector();

        detector.OnKey(RightWin, isDown: true);
        Assert.True(detector.OnKey(RightWin, isDown: false));
    }

    [Fact]
    public void ShortcutWithWindowsKeyDoesNotOpenStart()
    {
        var detector = new StartKeyDetector();

        detector.OnKey(LeftWin, isDown: true);
        detector.OnKey(KeyE, isDown: true);
        detector.OnKey(KeyE, isDown: false);
        Assert.False(detector.OnKey(LeftWin, isDown: false));
    }

    [Fact]
    public void AutoRepeatWhileHoldingDoesNotResetTheShortcut()
    {
        var detector = new StartKeyDetector();

        detector.OnKey(LeftWin, isDown: true);
        detector.OnKey(Tab, isDown: true);
        detector.OnKey(LeftWin, isDown: true);
        Assert.False(detector.OnKey(LeftWin, isDown: false));
    }

    [Fact]
    public void KeyReleasedFromBeforeDoesNotCount()
    {
        var detector = new StartKeyDetector();

        // Soltar uma tecla apertada antes da Windows não é um atalho com ela.
        detector.OnKey(LeftWin, isDown: true);
        detector.OnKey(KeyE, isDown: false);
        Assert.True(detector.OnKey(LeftWin, isDown: false));
    }

    [Fact]
    public void ModifierHeldBeforeWindowsKeyDoesNotOpenStart()
    {
        var detector = new StartKeyDetector();

        detector.OnKey(LeftWin, isDown: true, otherModifiersDown: true);
        Assert.False(detector.OnKey(LeftWin, isDown: false));
    }

    [Fact]
    public void ReleaseWithoutPressDoesNotOpenStart()
    {
        var detector = new StartKeyDetector();

        Assert.False(detector.OnKey(LeftWin, isDown: false));
    }

    [Fact]
    public void NextPressStartsFresh()
    {
        var detector = new StartKeyDetector();

        detector.OnKey(LeftWin, isDown: true);
        detector.OnKey(KeyE, isDown: true);
        detector.OnKey(LeftWin, isDown: false);

        detector.OnKey(LeftWin, isDown: true);
        Assert.True(detector.OnKey(LeftWin, isDown: false));
    }
}
