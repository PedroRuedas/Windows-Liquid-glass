using System.Globalization;
using System.Windows.Data;
using LiquidGlass.Services;

namespace LiquidGlass.Converters;

/// <summary>Agrupa "Todos os apps" pela inicial do nome (veja <see cref="StartMenuCatalog.Initial"/>).</summary>
public sealed class InitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        StartMenuCatalog.Initial(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
