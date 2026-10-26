using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LiquidGlass.Converters;

/// <summary>Verdadeiro quando as duas cores são iguais (marca a amostra de cor selecionada).</summary>
public sealed class ColorsEqualConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values is [Color first, Color second] && first == second;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
