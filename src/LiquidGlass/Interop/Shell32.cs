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

    public const int SiigbfResizeToFit = 0x00;
    public const int SiigbfIconOnly = 0x04;

    public static readonly PropertyKey AppUserModelId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);
    public static readonly PropertyKey LinkTargetParsingPath = new("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25", 2);

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
            if (store.GetValue(ref key, out var value) != 0)
            {
                return null;
            }

            var text = value.AsString();
            _ = PropVariantClear(ref value);
            return string.IsNullOrWhiteSpace(text) ? null : text;
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
