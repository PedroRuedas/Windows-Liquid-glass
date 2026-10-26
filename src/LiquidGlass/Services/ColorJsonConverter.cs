using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace LiquidGlass.Services;

/// <summary>Serializa cores como <c>#AARRGGBB</c>.</summary>
internal sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Colors.White;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch (FormatException)
        {
            return Colors.White;
        }
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(string.Create(CultureInfo.InvariantCulture, $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}"));
    }
}
