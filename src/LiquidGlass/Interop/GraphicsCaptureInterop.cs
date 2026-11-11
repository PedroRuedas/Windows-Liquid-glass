using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LiquidGlass.Interop;

/// <summary>
/// A ponte entre o Direct3D 11 (Vortice) e as APIs WinRT de captura: criar o item de captura de uma
/// janela, embrulhar o dispositivo Direct3D para o WinRT e abrir a textura de um quadro capturado.
/// </summary>
internal static class GraphicsCaptureInterop
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, [In] ref Guid iid);

        nint CreateForMonitor(nint monitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface([In] ref Guid iid);
    }

    /// <summary>O item de captura de uma janela; nulo se o Windows recusar (janela fechada, protegida).</summary>
    public static GraphicsCaptureItem? CreateItemForWindow(nint hwnd)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = QueryInterface<IGraphicsCaptureItemInterop>(factory.ThisPtr);
        try
        {
            var iid = GraphicsCaptureItemIid;
            var item = interop.CreateForWindow(hwnd, ref iid);
            try
            {
                return GraphicsCaptureItem.FromAbi(item);
            }
            finally
            {
                Marshal.Release(item);
            }
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(interop);
        }
    }

    /// <summary>Embrulha o dispositivo Direct3D 11 (o ponteiro de um IDXGIDevice) para as APIs WinRT.</summary>
    public static IDirect3DDevice CreateDevice(nint dxgiDevice)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>A textura (ID3D11Texture2D, com uma referência a liberar) por trás de uma superfície capturada.</summary>
    public static nint GetTexture(IDirect3DSurface surface)
    {
        var abi = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var access = QueryInterface<IDirect3DDxgiInterfaceAccess>(abi);
            try
            {
                var iid = Texture2DIid;
                return access.GetInterface(ref iid);
            }
            finally
            {
                Marshal.ReleaseComObject(access);
            }
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    private static T QueryInterface<T>(nint unknown)
        where T : class
    {
        var iid = typeof(T).GUID;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var pointer));
        try
        {
            return (T)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
