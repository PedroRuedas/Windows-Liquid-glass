using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int Width;
    public int Height;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(string formatId, int propertyId)
    {
        FormatId = new Guid(formatId);
        PropertyId = propertyId;
    }
}

/// <summary>
/// PROPVARIANT reduzido: só lemos valores do tipo string (VT_LPWSTR).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    private const ushort VtLpwstr = 31;

    [FieldOffset(0)]
    private readonly ushort _valueType;

    [FieldOffset(8)]
    private readonly nint _pointer;

    public readonly string? AsString() => _valueType == VtLpwstr ? Marshal.PtrToStringUni(_pointer) : null;
}

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint bindContext, ref Guid handler, ref Guid interfaceId, out nint result);

    void GetParent(out IShellItem parent);

    void GetDisplayName(uint nameType, [MarshalAs(UnmanagedType.LPWStr)] out string name);
}

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(NativeSize size, int flags, out nint bitmap);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);
}
