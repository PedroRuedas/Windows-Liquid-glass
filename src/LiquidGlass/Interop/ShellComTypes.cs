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
/// PROPVARIANT reduzido: só lemos strings (VT_LPWSTR e VT_BSTR) e booleanos (VT_BOOL).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    private const ushort VtBstr = 8;
    private const ushort VtBool = 11;
    private const ushort VtLpwstr = 31;

    [FieldOffset(0)]
    private readonly ushort _valueType;

    [FieldOffset(8)]
    private readonly nint _pointer;

    [FieldOffset(8)]
    private readonly short _boolean;

    public readonly string? AsString() => _valueType switch
    {
        VtLpwstr => Marshal.PtrToStringUni(_pointer),
        VtBstr => Marshal.PtrToStringBSTR(_pointer),
        _ => null,
    };

    public readonly bool AsBoolean() => _valueType == VtBool && _boolean != 0;
}

/// <summary>Objeto COM que se grava e se lê de um fluxo, como os atalhos dentro das listas de atalhos.</summary>
[ComImport]
[Guid("00000109-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistStream
{
    void GetClassID(out Guid classId);

    [PreserveSig]
    int IsDirty();

    void Load(System.Runtime.InteropServices.ComTypes.IStream stream);
}

/// <summary>
/// Resolvedor interno que a barra de tarefas usa para descobrir o AppID de uma janela ou atalho
/// (explícito ou deduzido do executável). Não é documentado, mas é estável desde o Windows 8.
/// </summary>
[ComImport]
[Guid("DE25675A-72DE-44b4-9373-05170450C140")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationResolver
{
    [PreserveSig]
    int GetAppIDForShortcut(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] out string appId);

    [PreserveSig]
    int GetAppIDForShortcutObject(nint link, nint item, [MarshalAs(UnmanagedType.LPWStr)] out string appId);

    [PreserveSig]
    int GetAppIDForWindow(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] out string appId, out int pinningPrevented, out int explicitAppId, out int embeddedShortcutValid);
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
