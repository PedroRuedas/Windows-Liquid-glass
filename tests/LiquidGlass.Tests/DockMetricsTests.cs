using LiquidGlass.Models;

namespace LiquidGlass.Tests;

public class DockMetricsTests
{
    [Theory]
    [InlineData(32, 7)]
    [InlineData(40, 7)]
    [InlineData(44, 7.04)]
    [InlineData(96, 15.36)]
    public void GlassPaddingIsUnchangedFrom32Up(double iconSize, double expected)
    {
        Assert.Equal(expected, DockSettings.GlassPaddingFor(iconSize), 3);
    }

    [Fact]
    public void SmallIconsMakeAThinnerDock()
    {
        var thickness16 = 16 + (DockSettings.GlassPaddingFor(16) * 2);

        Assert.True(DockSettings.GlassPaddingFor(16) < 4);
        Assert.True(thickness16 < 24);
    }

    [Fact]
    public void GlassPaddingNeverShrinksWhileIconsGrow()
    {
        var previous = 0.0;
        for (var size = 16; size <= 96; size++)
        {
            var padding = DockSettings.GlassPaddingFor(size);
            Assert.True(padding >= previous, $"{size} px");
            previous = padding;
        }
    }
}
