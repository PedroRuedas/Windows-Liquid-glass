namespace LiquidGlass.Graphics;

/// <summary>
/// Desfoque por caixas separável: três passadas aproximam um desfoque gaussiano em O(n),
/// independentemente do raio.
/// </summary>
internal static class BoxBlur
{
    private const int BytesPerPixel = 4;

    public static void Apply(byte[] pixels, int width, int height, int radius, int passes = 3)
    {
        if (radius < 1)
        {
            return;
        }

        var buffer = new byte[pixels.Length];
        for (var pass = 0; pass < passes; pass++)
        {
            BlurHorizontal(pixels, buffer, width, height, radius);
            BlurVertical(buffer, pixels, width, height, radius);
        }
    }

    private static void BlurHorizontal(byte[] source, byte[] target, int width, int height, int radius)
    {
        var windowSize = (radius * 2) + 1;
        Parallel.For(0, height, y =>
        {
            var row = y * width * BytesPerPixel;
            Span<int> sums = stackalloc int[BytesPerPixel];

            for (var offset = -radius; offset <= radius; offset++)
            {
                var x = Math.Clamp(offset, 0, width - 1);
                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    sums[channel] += source[row + (x * BytesPerPixel) + channel];
                }
            }

            for (var x = 0; x < width; x++)
            {
                var index = row + (x * BytesPerPixel);
                var leaving = row + (Math.Max(x - radius, 0) * BytesPerPixel);
                var entering = row + (Math.Min(x + radius + 1, width - 1) * BytesPerPixel);

                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    target[index + channel] = (byte)(sums[channel] / windowSize);
                    sums[channel] += source[entering + channel] - source[leaving + channel];
                }
            }
        });
    }

    private static void BlurVertical(byte[] source, byte[] target, int width, int height, int radius)
    {
        var windowSize = (radius * 2) + 1;
        var stride = width * BytesPerPixel;
        Parallel.For(0, width, x =>
        {
            var column = x * BytesPerPixel;
            Span<int> sums = stackalloc int[BytesPerPixel];

            for (var offset = -radius; offset <= radius; offset++)
            {
                var y = Math.Clamp(offset, 0, height - 1);
                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    sums[channel] += source[(y * stride) + column + channel];
                }
            }

            for (var y = 0; y < height; y++)
            {
                var index = (y * stride) + column;
                var leaving = (Math.Max(y - radius, 0) * stride) + column;
                var entering = (Math.Min(y + radius + 1, height - 1) * stride) + column;

                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    target[index + channel] = (byte)(sums[channel] / windowSize);
                    sums[channel] += source[entering + channel] - source[leaving + channel];
                }
            }
        });
    }
}
