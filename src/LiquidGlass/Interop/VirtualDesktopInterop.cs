using System.Runtime.InteropServices;

namespace LiquidGlass.Interop;

/// <summary>
/// Interfaces COM das áreas de trabalho virtuais. <see cref="IVirtualDesktopManager"/> é documentada;
/// as demais são internas do Explorer (Windows 11 24H2 em diante) e mudam entre versões, por isso
/// quem as usa sempre trata a falha e segue sem elas.
/// </summary>
internal static class VirtualDesktopInterop
{
    public static readonly Guid ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    public static readonly Guid VirtualDesktopManagerInternalService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    public static readonly Guid VirtualDesktopManager = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
}

[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid service, ref Guid interfaceId, out nint result);
}

[ComImport]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    int GetCount();

    [return: MarshalAs(UnmanagedType.Interface)]
    object GetAt(int index, ref Guid interfaceId);
}

[ComImport]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint hwnd, out int isOnCurrent);

    [PreserveSig]
    int GetWindowDesktopId(nint hwnd, out Guid desktopId);
}

[ComImport]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktop
{
    [return: MarshalAs(UnmanagedType.Bool)]
    bool IsViewVisible(nint view);

    Guid GetId();
}

/// <summary>A ordem dos métodos é a da tabela virtual do Explorer; não reordene.</summary>
[ComImport]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManagerInternal
{
    int GetCount();

    void MoveViewToDesktop(nint view, IVirtualDesktop desktop);

    [return: MarshalAs(UnmanagedType.Bool)]
    bool CanViewMoveDesktops(nint view);

    IVirtualDesktop GetCurrentDesktop();

    IObjectArray GetDesktops();

    [PreserveSig]
    int GetAdjacentDesktop(IVirtualDesktop from, int direction, out IVirtualDesktop desktop);

    void SwitchDesktop(IVirtualDesktop desktop);

    void SwitchDesktopAndMoveForegroundView(IVirtualDesktop desktop);

    IVirtualDesktop CreateDesktop();

    void MoveDesktop(IVirtualDesktop desktop, int index);

    void RemoveDesktop(IVirtualDesktop desktop, IVirtualDesktop fallback);

    IVirtualDesktop FindDesktop(ref Guid desktopId);
}

[ComImport]
[Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationViewCollection
{
    [PreserveSig]
    int GetViews(out IObjectArray views);

    [PreserveSig]
    int GetViewsByZOrder(out IObjectArray views);

    [PreserveSig]
    int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IObjectArray views);

    [PreserveSig]
    int GetViewForHwnd(nint hwnd, out nint view);
}
