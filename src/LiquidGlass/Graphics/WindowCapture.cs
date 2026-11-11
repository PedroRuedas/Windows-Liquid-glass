using System.Runtime.InteropServices;
using System.Windows;
using LiquidGlass.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace LiquidGlass.Graphics;

/// <summary>
/// O dispositivo Direct3D compartilhado pelas capturas de janelas. Toda cópia passa pelo mesmo
/// contexto, então quem o usa chama de uma thread só (a do <see cref="Services.LiveBackdrop"/>).
/// </summary>
internal sealed class CaptureDevice : IDisposable
{
    private CaptureDevice(ID3D11Device device, ID3D11DeviceContext context, IDirect3DDevice winRtDevice)
    {
        Device = device;
        Context = context;
        WinRtDevice = winRtDevice;
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    public IDirect3DDevice WinRtDevice { get; }

    /// <summary>
    /// A captura sem a borda amarela exige o Windows 11 e a permissão "sem borda", que o Windows dá
    /// aos apps da área de trabalho. Sem ela, a dock não captura nada: uma borda amarela piscando em
    /// volta das janelas seria pior do que o papel de parede.
    /// </summary>
    public static async Task<bool> IsBorderlessCaptureAllowedAsync()
    {
        try
        {
            return GraphicsCaptureSession.IsSupported()
                && ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsBorderRequired))
                && await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless) == Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed;
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or InvalidCastException or TypeLoadException)
        {
            return false;
        }
    }

    public static CaptureDevice? TryCreate()
    {
        try
        {
            var result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                out ID3D11Device device,
                out ID3D11DeviceContext context);
            if (result.Failure || device is null || context is null)
            {
                return null;
            }

            using var dxgi = device.QueryInterface<IDXGIDevice>();
            return new CaptureDevice(device, context, GraphicsCaptureInterop.CreateDevice(dxgi.NativePointer));
        }
        catch (Exception exception) when (exception is COMException or SharpGen.Runtime.SharpGenException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        (WinRtDevice as IDisposable)?.Dispose();
        Context.Dispose();
        Device.Dispose();
    }

    private static class ApiInformation
    {
        public static bool IsPropertyPresent(string? type, string property) =>
            type is not null && Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(type, property);
    }
}

/// <summary>
/// Captura contínua de uma janela (Windows.Graphics.Capture): o que ela mostra mesmo quando a dock
/// está por cima dela. Guarda o quadro mais recente na placa de vídeo e copia para a memória só o
/// trecho que a dock pede.
/// </summary>
internal sealed class WindowCapture : IDisposable
{
    private const DirectXPixelFormat Format = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    private readonly CaptureDevice _device;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private SizeInt32 _poolSize;
    private ID3D11Texture2D? _latest;
    private ID3D11Texture2D? _staging;
    private bool _closed;

    private WindowCapture(CaptureDevice device, GraphicsCaptureItem item)
    {
        _device = device;
        _item = item;
        _poolSize = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device.WinRtDevice, Format, 2, _poolSize);
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = false;
        _session.IsBorderRequired = false;
        _item.Closed += (_, _) => _closed = true;
        _session.StartCapture();
    }

    /// <summary>A janela foi fechada (ou a captura parou de vez).</summary>
    public bool IsClosed => _closed;

    /// <summary>Tamanho do conteúdo do último quadro, em pixels físicos (zero antes do primeiro).</summary>
    public SizeInt32 ContentSize { get; private set; }

    public static WindowCapture? TryStart(CaptureDevice device, nint hwnd)
    {
        if (GraphicsCaptureInterop.CreateItemForWindow(hwnd) is not { } item)
        {
            return null;
        }

        try
        {
            return new WindowCapture(device, item);
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Guarda o quadro mais novo, se chegou algum. Devolve se a imagem mudou.</summary>
    public bool Update()
    {
        Direct3D11CaptureFrame? newest = null;
        try
        {
            while (_pool.TryGetNextFrame() is { } frame)
            {
                newest?.Dispose();
                newest = frame;
            }
        }
        catch (Exception exception) when (exception is COMException or ObjectDisposedException)
        {
            _closed = true;
        }

        if (newest is null)
        {
            return false;
        }

        using (newest)
        {
            var size = newest.ContentSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return false;
            }

            using var texture = new ID3D11Texture2D(GraphicsCaptureInterop.GetTexture(newest.Surface));
            var description = texture.Description;
            if (_latest is null || _latest.Description.Width != description.Width || _latest.Description.Height != description.Height)
            {
                _latest?.Dispose();
                _latest = _device.Device.CreateTexture2D(new Texture2DDescription(Vortice.DXGI.Format.B8G8R8A8_UNorm, description.Width, description.Height, 1, 1, BindFlags.None));
            }

            _device.Context.CopyResource(_latest, texture);
            ContentSize = new SizeInt32 { Width = Math.Min(size.Width, (int)description.Width), Height = Math.Min(size.Height, (int)description.Height) };

            // A janela mudou de tamanho: os próximos quadros vêm no tamanho novo.
            if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
            {
                _poolSize = size;
                _pool.Recreate(_device.WinRtDevice, Format, 2, size);
            }
        }

        return true;
    }

    /// <summary>
    /// Copia <paramref name="region"/> (em pixels do último quadro) para <paramref name="pixels"/>,
    /// BGRA com <paramref name="region"/>.Width × 4 bytes por linha. Falso se ainda não há quadro.
    /// </summary>
    public bool Read(Int32Rect region, byte[] pixels)
    {
        if (_latest is null || region.Width <= 0 || region.Height <= 0
            || region.X < 0 || region.Y < 0 || region.X + region.Width > ContentSize.Width || region.Y + region.Height > ContentSize.Height)
        {
            return false;
        }

        var stagingSize = _staging?.Description is { } current ? ((int)current.Width, (int)current.Height) : (0, 0);
        if (stagingSize.Item1 < region.Width || stagingSize.Item2 < region.Height)
        {
            // Cresce para caber o trecho novo e não encolhe: a área muda a cada ampliação dos ícones.
            _staging?.Dispose();
            _staging = _device.Device.CreateTexture2D(new Texture2DDescription(
                Vortice.DXGI.Format.B8G8R8A8_UNorm,
                (uint)Math.Max(region.Width, stagingSize.Item1),
                (uint)Math.Max(region.Height, stagingSize.Item2),
                1,
                1,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read));
        }

        var staging = _staging!;
        var context = _device.Context;
        context.CopySubresourceRegion(
            staging, 0, 0, 0, 0, _latest, 0, new Vortice.Mathematics.Box(region.X, region.Y, 0, region.X + region.Width, region.Y + region.Height, 1));
        var mapped = context.Map(staging, 0, MapMode.Read);
        try
        {
            var rowBytes = region.Width * 4;
            for (var y = 0; y < region.Height; y++)
            {
                Marshal.Copy(mapped.DataPointer + (nint)(y * mapped.RowPitch), pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        return true;
    }

    public void Dispose()
    {
        _session.Dispose();
        _pool.Dispose();
        _latest?.Dispose();
        _staging?.Dispose();
    }
}
