using System.Windows;
using LiquidGlass.Models;
using LiquidGlass.Views;

namespace LiquidGlass.Tests;

public class WindowPreviewLayoutTests
{
    private static readonly Size FullHd = new(1920, 1080);

    [Fact]
    public void FewWindowsKeepTheFullSize()
    {
        var (scale, count) = WindowPreviewLayout.Fit([FullHd, FullHd], DockEdge.Bottom, new Size(1800, 800));

        Assert.Equal(1, scale);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ManyWindowsWrapIntoLinesBeforeShrinking()
    {
        var windows = Enumerable.Repeat(FullHd, 30).ToList();

        var (scale, count) = WindowPreviewLayout.Fit(windows, DockEdge.Bottom, new Size(1830, 850));

        Assert.Equal(1, scale);
        Assert.Equal(30, count);
    }

    [Theory]
    [InlineData(DockEdge.Bottom, 1830, 850)]
    [InlineData(DockEdge.Left, 1700, 950)]
    [InlineData(DockEdge.Right, 1700, 950)]
    public void EvenMoreWindowsShrinkUntilAllFit(DockEdge edge, double width, double height)
    {
        var windows = Enumerable.Repeat(FullHd, 60).ToList();

        var (scale, count) = WindowPreviewLayout.Fit(windows, edge, new Size(width, height));

        Assert.Equal(60, count);
        Assert.InRange(scale, WindowPreviewLayout.MinScale, 0.99);
    }

    [Fact]
    public void TooManyWindowsShowWhatFitsAtTheSmallestSize()
    {
        var windows = Enumerable.Repeat(FullHd, 500).ToList();

        var (scale, count) = WindowPreviewLayout.Fit(windows, DockEdge.Bottom, new Size(1830, 850));

        Assert.Equal(WindowPreviewLayout.MinScale, scale);
        Assert.InRange(count, 1, 499);
    }

    [Fact]
    public void WindowWithoutSizeStillGetsACard()
    {
        var size = WindowPreviewLayout.ThumbnailSize(new Size(0, 0), DockEdge.Bottom, 1);

        Assert.True(size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width));
    }
}
