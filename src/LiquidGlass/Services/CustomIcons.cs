using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiquidGlass.Services;

/// <summary>
/// Ícones escolhidos pelo usuário para os apps da dock. A imagem é copiada para
/// %AppData%\LiquidGlass\icons, então continua valendo mesmo se o arquivo original sumir.
/// </summary>
public static class CustomIcons
{
    /// <summary>Extensões aceitas: imagens (usadas como estão) e programas ou bibliotecas (o ícone deles).</summary>
    public static readonly string[] ImageExtensions = [".png", ".ico", ".jpg", ".jpeg", ".bmp"];

    public static readonly string[] ProgramExtensions = [".exe", ".dll", ".lnk"];

    public static string Folder { get; } = Path.Combine(AppDataPaths.Root, "icons");

    /// <summary>
    /// Guarda uma cópia do ícone e devolve o caminho dela. De um programa, guarda o ícone dele em
    /// PNG. Nulo se o arquivo não for um ícone que dê para usar.
    /// </summary>
    public static string? Import(string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        Directory.CreateDirectory(Folder);
        var name = Guid.NewGuid().ToString("N");

        if (ImageExtensions.Contains(extension))
        {
            if (Load(source) is null)
            {
                return null;
            }

            var copy = Path.Combine(Folder, name + extension);
            File.Copy(source, copy);
            return copy;
        }

        if (ProgramExtensions.Contains(extension) && IconService.LoadIcon(source, 256) is { } icon)
        {
            var png = Path.Combine(Folder, name + ".png");
            using var file = File.Create(png);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(icon));
            encoder.Save(file);
            return png;
        }

        return null;
    }

    /// <summary>A imagem pronta para a dock (num .ico, o maior tamanho), ou nulo se não der para ler.</summary>
    public static BitmapSource? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames.OrderByDescending(candidate => candidate.PixelWidth * candidate.PixelHeight).First();
            if (frame.PixelWidth > 256 || frame.PixelHeight > 256)
            {
                // A dock nunca mostra mais que 256 px: imagens grandes só gastariam memória.
                var scale = 256.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
                frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            }

            frame.Freeze();
            return frame;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Apaga a cópia guardada, se ela estiver na pasta de ícones.</summary>
    public static void Delete(string? path)
    {
        if (path is null || !Path.GetFullPath(path).StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
