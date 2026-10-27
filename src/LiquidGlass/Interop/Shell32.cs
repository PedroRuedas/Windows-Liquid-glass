using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

internal static class Shell32
{
    public const uint AbmNew = 0;
    public const uint AbmRemove = 1;
    public const uint AbmQueryPos = 2;
    public const uint AbmSetPos = 3;
    public const uint AbmGetState = 4;
    public const uint AbmGetTaskbarPos = 5;
    public const uint AbmSetState = 10;

    public const int AbnPosChanged = 1;
    public const int AbnFullScreenApp = 2;

    public const int AbsAutoHide = 0x1;
    public const int AbsAlwaysOnTop = 0x2;

    public const uint AbeLeft = 0;
    public const uint AbeTop = 1;
    public const uint AbeRight = 2;
    public const uint AbeBottom = 3;

    public const uint SigdnNormalDisplay = 0;
    public const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    public const int SiigbfResizeToFit = 0x00;
    public const int SiigbfIconOnly = 0x04;

    public static readonly PropertyKey AppUserModelId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);
    public static readonly PropertyKey LinkTargetParsingPath = new("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25", 2);
    public static readonly PropertyKey Title = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 2);

    /// <summary>Marca as linhas divisórias entre as tarefas de uma lista de atalhos.</summary>
    public static readonly PropertyKey IsDestListSeparator = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 6);

    private static readonly Guid ShellItemId = typeof(IShellItem).GUID;
    private static readonly Guid ImageFactoryId = typeof(IShellItemImageFactory).GUID;
    private static readonly Guid PropertyStoreId = typeof(IPropertyStore).GUID;

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public int Size;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public User32.Rect Rect;
        public nint Param;
    }

    [DllImport("shell32.dll")]
    public static extern nint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        nint bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object item);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(
        string path,
        nint bindContext,
        int flags,
        ref Guid interfaceId,
        out IPropertyStore store);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid interfaceId, out IPropertyStore store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("shlwapi.dll")]
    [return: MarshalAs(UnmanagedType.Interface)]
    private static extern System.Runtime.InteropServices.ComTypes.IStream? SHCreateMemStream(byte[] data, uint length);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, [Out] char[] output, uint length, nint reserved);

    /// <summary>Fluxo COM em memória com uma cópia de <paramref name="data"/>.</summary>
    public static System.Runtime.InteropServices.ComTypes.IStream? CreateMemoryStream(byte[] data)
    {
        return SHCreateMemStream(data, (uint)data.Length);
    }

    /// <summary>Resolve textos indiretos ("@shell32.dll,-123", "@{pacote?ms-resource://...}").</summary>
    public static string? LoadIndirectString(string source)
    {
        var buffer = new char[1024];
        if (SHLoadIndirectString(source, buffer, (uint)buffer.Length, 0) != 0)
        {
            return null;
        }

        var length = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, length < 0 ? buffer.Length : length);
    }

    /// <summary>Lê uma string de um repositório de propriedades sem liberá-lo.</summary>
    public static string? GetString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0)
        {
            return null;
        }

        var text = value.AsString();
        _ = PropVariantClear(ref value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static bool GetBoolean(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0)
        {
            return false;
        }

        var flag = value.AsBoolean();
        _ = PropVariantClear(ref value);
        return flag;
    }

    public static AppBarData CreateAppBarData(nint hwnd = 0)
    {
        return new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Window = hwnd };
    }

    public static IShellItem? CreateShellItem(string parsingName)
    {
        return TryCreate<IShellItem>(parsingName, ShellItemId);
    }

    public static IShellItemImageFactory? CreateImageFactory(string parsingName)
    {
        return TryCreate<IShellItemImageFactory>(parsingName, ImageFactoryId);
    }

    public static string? GetDisplayName(string parsingName)
    {
        var item = CreateShellItem(parsingName);
        if (item is null)
        {
            return null;
        }

        try
        {
            item.GetDisplayName(SigdnNormalDisplay, out var name);
            return name;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    public static string? GetFileProperty(string parsingName, PropertyKey key)
    {
        var interfaceId = PropertyStoreId;
        if (SHGetPropertyStoreFromParsingName(parsingName, 0, 0, ref interfaceId, out var store) != 0)
        {
            return null;
        }

        return ReadString(store, key);
    }

    public static string? GetWindowProperty(nint hwnd, PropertyKey key)
    {
        var interfaceId = PropertyStoreId;
        if (SHGetPropertyStoreForWindow(hwnd, ref interfaceId, out var store) != 0)
        {
            return null;
        }

        return ReadString(store, key);
    }

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        try
        {
            return GetString(store, key);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static T? TryCreate<T>(string parsingName, Guid interfaceId)
        where T : class
    {
        try
        {
            SHCreateItemFromParsingName(parsingName, 0, ref interfaceId, out var item);
            return item as T;
        }
        catch (Exception exception) when (exception is COMException or FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }
}
