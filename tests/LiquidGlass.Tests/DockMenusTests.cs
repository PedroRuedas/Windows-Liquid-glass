using LiquidGlass.Models;
using LiquidGlass.Views;

namespace LiquidGlass.Tests;

public class DockMenusTests
{
    [Theory]
    [InlineData("Nova janela")]
    [InlineData("Abrir nova janela")]
    [InlineData("New window")]
    [InlineData("Open new window")]
    [InlineData("Nueva ventana")]
    [InlineData("Nouvelle fenêtre")]
    [InlineData("Nouvelle fenetre")]
    [InlineData("Neues Fenster")]
    [InlineData("Nuova finestra")]
    public void RecognizesNewWindowTasks(string title)
    {
        Assert.True(DockMenus.IsNewWindowTask(Entry(title)));
    }

    [Theory]
    [InlineData("Nova janela anônima")]
    [InlineData("Nova janela InPrivate")]
    [InlineData("New private window")]
    [InlineData("Nova guia")]
    [InlineData("")]
    public void IgnoresOtherTasks(string title)
    {
        Assert.False(DockMenus.IsNewWindowTask(Entry(title)));
    }

    private static JumpListEntry Entry(string title) => new(title, ReadOnlyMemory<byte>.Empty, null);
}
