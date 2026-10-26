using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LiquidGlass.Converters;

/// <summary>Mostra o elemento só quando o índice selecionado é igual ao parâmetro.</summary>
public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var expected = int.Parse((string)parameter, CultureInfo.InvariantCulture);
        return value is int index && index == expected ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
