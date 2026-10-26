using System.Windows;

namespace LiquidGlass.Controls;

public delegate void ContentBoundsChangedHandler(object sender, ContentBoundsChangedEventArgs e);

/// <summary>Informa a área ocupada pelos ícones (no tamanho base), em coordenadas do painel.</summary>
public sealed class ContentBoundsChangedEventArgs(RoutedEvent routedEvent, object source, Rect bounds)
    : RoutedEventArgs(routedEvent, source)
{
    public Rect Bounds { get; } = bounds;

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ((ContentBoundsChangedHandler)genericHandler)(genericTarget, this);
    }
}
