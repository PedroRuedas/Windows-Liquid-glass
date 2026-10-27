using System.Globalization;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>
/// Estado da bateria do computador. Não consulta periodicamente: o Windows avisa a janela da dock
/// quando a carga ou a fonte de energia mudam.
/// </summary>
public sealed partial class BatteryService : ObservableObject, IDisposable
{
    private const int PowerBroadcastStatusChange = 0x000A;
    private const int PowerBroadcastSettingChange = 0x8013;
    private const byte NoSystemBattery = 128;
    private const byte UnknownStatus = 255;
    private const byte ChargingFlag = 8;
    private const byte BatterySaverFlag = 1;

    private static readonly Guid BatteryPercentageRemaining = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
    private static readonly Guid AcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

    private readonly List<nint> _notifications = [];
    private HwndSource? _source;

    [ObservableProperty]
    private bool _isPresent;

    [ObservableProperty]
    private int _percent;

    [ObservableProperty]
    private bool _isPluggedIn;

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private bool _isSaverOn;

    /// <summary>Tempo estimado até acabar a carga; <c>null</c> na tomada ou quando o Windows não sabe.</summary>
    [ObservableProperty]
    private TimeSpan? _remaining;

    public void Attach(HwndSource source)
    {
        _source = source;
        source.AddHook(WndProc);
        foreach (var setting in new[] { BatteryPercentageRemaining, AcDcPowerSource })
        {
            var guid = setting;
            var handle = User32.RegisterPowerSettingNotification(source.Handle, ref guid, 0);
            if (handle != 0)
            {
                _notifications.Add(handle);
            }
        }

        Refresh();
    }

    /// <summary>Descrição curta para o rótulo do ícone ("Bateria: 78% · 2 h 10 min restantes").</summary>
    public string Describe()
    {
        var state = this switch
        {
            { IsCharging: true } => "carregando",
            { IsPluggedIn: true, Percent: >= 99 } => "carregada",
            { IsPluggedIn: true } => "na tomada",
            { Remaining: { } left } => FormatRemaining(left),
            _ => null,
        };

        var text = string.Create(CultureInfo.CurrentCulture, $"Bateria: {Percent}%");
        if (IsSaverOn)
        {
            text += " · economia de energia";
        }

        return state is null ? text : $"{text} · {state}";
    }

    public void Refresh()
    {
        if (Kernel32.GetPowerStatus() is not { } status)
        {
            return;
        }

        var hasBattery = status.BatteryFlag != NoSystemBattery && status.BatteryFlag != UnknownStatus && status.BatteryLifePercent <= 100;
#if DEBUG
        // Teste em computadores sem bateria: LIQUIDGLASS_FAKE_BATTERY=57 ou "57,charging" ou "12,saver".
        if (Environment.GetEnvironmentVariable("LIQUIDGLASS_FAKE_BATTERY") is { Length: > 0 } fake)
        {
            var parts = fake.Split(',');
            hasBattery = true;
            status.BatteryLifePercent = byte.Parse(parts[0], CultureInfo.InvariantCulture);
            status.AcLineStatus = (byte)(parts.Contains("charging") ? 1 : 0);
            status.BatteryFlag = (byte)(parts.Contains("charging") ? ChargingFlag : 0);
            status.SystemStatusFlag = (byte)(parts.Contains("saver") ? BatterySaverFlag : 0);
            status.BatteryLifeTime = parts.Contains("charging") ? -1 : status.BatteryLifePercent * 150;
        }
#endif

        IsPresent = hasBattery;
        if (!hasBattery)
        {
            return;
        }

        Percent = status.BatteryLifePercent;
        IsPluggedIn = status.AcLineStatus == 1;
        IsCharging = IsPluggedIn && (status.BatteryFlag & ChargingFlag) != 0;
        IsSaverOn = (status.SystemStatusFlag & BatterySaverFlag) != 0;
        Remaining = !IsPluggedIn && status.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(status.BatteryLifeTime) : null;
    }

    public void Dispose()
    {
        foreach (var handle in _notifications)
        {
            User32.UnregisterPowerSettingNotification(handle);
        }

        _notifications.Clear();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private static string FormatRemaining(TimeSpan left)
    {
        return left.TotalHours >= 1
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)left.TotalHours} h {left.Minutes:00} min restantes")
            : string.Create(CultureInfo.CurrentCulture, $"{Math.Max(left.Minutes, 1)} min restantes");
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == User32.WmPowerBroadcast && (int)wParam is PowerBroadcastStatusChange or PowerBroadcastSettingChange)
        {
            Refresh();
        }

        return 0;
    }
}
