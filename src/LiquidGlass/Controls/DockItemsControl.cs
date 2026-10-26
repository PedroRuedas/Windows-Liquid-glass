using System.Windows;
using System.Windows.Controls;

namespace LiquidGlass.Controls;

/// <summary>Lista de itens da dock; cada item é envolvido diretamente por um <see cref="DockItemView"/>.</summary>
public sealed class DockItemsControl : ItemsControl
{
    protected override DependencyObject GetContainerForItemOverride() => new DockItemView();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is DockItemView;
}
