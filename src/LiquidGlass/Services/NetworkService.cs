using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiquidGlass.Services;

/// <summary>Como o computador está conectado.</summary>
public enum NetworkKind
{
    None,
    Wifi,
    Ethernet,
}

/// <summary>
/// Conexão de rede atual: Wi-Fi (nome da rede e força do sinal), cabo ou nenhuma. Não consulta
/// periodicamente: o Windows avisa quando a conexão ou o sinal do Wi-Fi mudam.
/// </summary>
public sealed partial class NetworkService : ObservableObject, IDisposable
{
    private const uint WlanClientVersion = 2;
    private const int InterfaceStateConnected = 1;
    private const int OpcodeCurrentConnection = 7;
    private const uint NotificationSourceAcm = 0x08;
    private const uint NotificationSourceMsm = 0x10;
    private const int InterfaceInfoSize = 532;
    private const int SsidLengthOffset = 520;
    private const int SignalQualityOffset = 576;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly WlanNotificationCallback _wlanCallback;
    private readonly DispatcherTimer _refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private nint _wlan;

    [ObservableProperty]
    private NetworkKind _kind;

    /// <summary>O computador tem adaptador Wi-Fi (mesmo desconectado).</summary>
    [ObservableProperty]
    private bool _hasWifi;

    /// <summary>Qualidade do sinal do Wi-Fi, de 0 a 100.</summary>
    [ObservableProperty]
    private int _signal;

    [ObservableProperty]
    private string? _networkName;

    public NetworkService()
    {
        _wlanCallback = OnWlanNotification;
        _refreshDebounce.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        if (WlanOpenHandle(WlanClientVersion, 0, out _, out _wlan) == 0)
        {
            WlanRegisterNotification(_wlan, NotificationSourceAcm | NotificationSourceMsm, false, _wlanCallback, 0, 0, out _);
        }
        else
        {
            // Sem o serviço de Wi-Fi (computador só com cabo): só os avisos gerais de rede.
            _wlan = 0;
        }

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        Refresh();
    }

    /// <summary>Descrição curta para o rótulo do ícone.</summary>
    public string Describe() => Kind switch
    {
        NetworkKind.Wifi => string.Create(CultureInfo.CurrentCulture, $"Wi-Fi: {NetworkName ?? "conectado"} · sinal {Signal}%"),
        NetworkKind.Ethernet => "Rede cabeada: conectada",
        _ => HasWifi ? "Wi-Fi: desconectado" : "Sem conexão de rede",
    };

    public void Refresh()
    {
        _refreshDebounce.Stop();
        var wifi = ReadWifi(out var hasWifi);
        var kind = wifi is not null ? NetworkKind.Wifi : HasWiredConnection() ? NetworkKind.Ethernet : NetworkKind.None;
#if DEBUG
        // Teste em computadores sem Wi-Fi: LIQUIDGLASS_FAKE_WIFI=70,MinhaRede ou "off".
        if (Environment.GetEnvironmentVariable("LIQUIDGLASS_FAKE_WIFI") is { Length: > 0 } fake)
        {
            hasWifi = true;
            var parts = fake.Split(',', 2);
            wifi = parts[0] == "off" ? null : (int.Parse(parts[0], CultureInfo.InvariantCulture), parts.Length > 1 ? parts[1] : "Rede");
            kind = wifi is null ? NetworkKind.None : NetworkKind.Wifi;
        }
#endif

        HasWifi = hasWifi;
        Signal = wifi?.Signal ?? 0;
        NetworkName = wifi?.Name;
        Kind = kind;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _refreshDebounce.Stop();
        if (_wlan != 0)
        {
            WlanCloseHandle(_wlan, 0);
            _wlan = 0;
        }
    }

    /// <summary>Alguma conexão ativa que não é Wi-Fi, com saída para a rede (gateway).</summary>
    private static bool HasWiredConnection()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Wireless80211)
                && adapter.GetIPProperties().GatewayAddresses.Any(gateway => !gateway.Address.Equals(System.Net.IPAddress.Any)));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint handle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(nint handle, nint reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(nint handle, nint reserved, out nint interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(
        nint handle, ref Guid interfaceGuid, int opcode, nint reserved, out int dataSize, out nint data, out int valueType);

    [DllImport("wlanapi.dll")]
    private static extern int WlanRegisterNotification(
        nint handle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, WlanNotificationCallback callback,
        nint context, nint reserved, out uint previousSource);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(nint memory);

    /// <summary>Rede Wi-Fi conectada (nome e sinal); <paramref name="hasAdapter"/> diz se há adaptador.</summary>
    private (int Signal, string? Name)? ReadWifi(out bool hasAdapter)
    {
        hasAdapter = false;
        if (_wlan == 0 || WlanEnumInterfaces(_wlan, 0, out var list) != 0)
        {
            return null;
        }

        try
        {
            var count = Marshal.ReadInt32(list);
            hasAdapter = count > 0;

            // WLAN_INTERFACE_INFO_LIST: dois inteiros e então os itens (GUID, descrição e estado).
            for (var index = 0; index < count; index++)
            {
                var item = list + 8 + (index * InterfaceInfoSize);
                var guid = Marshal.PtrToStructure<Guid>(item);
                if (Marshal.ReadInt32(item + 16 + 512) != InterfaceStateConnected)
                {
                    continue;
                }

                if (WlanQueryInterface(_wlan, ref guid, OpcodeCurrentConnection, 0, out _, out var data, out _) != 0)
                {
                    continue;
                }

                try
                {
                    // WLAN_CONNECTION_ATTRIBUTES: o SSID e a qualidade do sinal ficam na associação.
                    var length = Math.Clamp(Marshal.ReadInt32(data + SsidLengthOffset), 0, 32);
                    var ssid = new byte[length];
                    Marshal.Copy(data + SsidLengthOffset + 4, ssid, 0, length);
                    var signal = Math.Clamp(Marshal.ReadInt32(data + SignalQualityOffset), 0, 100);
                    return (signal, length > 0 ? Encoding.UTF8.GetString(ssid) : null);
                }
                finally
                {
                    WlanFreeMemory(data);
                }
            }

            return null;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    /// <summary>Vários avisos chegam juntos (troca de rede); uma leitura só depois que eles param.</summary>
    private void ScheduleRefresh() => _dispatcher.BeginInvoke(() =>
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    });

    private void OnNetworkChanged(object? sender, EventArgs e) => ScheduleRefresh();

    private void OnWlanNotification(nint data, nint context) => ScheduleRefresh();

    private delegate void WlanNotificationCallback(nint data, nint context);
}
