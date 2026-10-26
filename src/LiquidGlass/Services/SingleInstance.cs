namespace LiquidGlass.Services;

/// <summary>
/// Garante uma única dock por sessão. Abrir o app de novo envia um pedido à instância existente:
/// mostrar a janela de personalização ou encerrar.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\LiquidGlass.Dock";
    private const string ShowSettingsSignalName = @"Local\LiquidGlass.Dock.ShowSettings";
    private const string QuitSignalName = @"Local\LiquidGlass.Dock.Quit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSettingsSignal;
    private readonly EventWaitHandle _quitSignal;
    private readonly List<RegisteredWaitHandle> _registrations = [];
    private bool _ownsMutex;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitSignalName);
        IsFirstInstance = createdNew;
        _ownsMutex = createdNew;
    }

    public bool IsFirstInstance { get; }

    /// <summary>Os callbacks rodam em uma thread do pool quando outra instância envia um pedido.</summary>
    public void Listen(Action onShowSettings, Action onQuit)
    {
        _registrations.Add(Register(_showSettingsSignal, onShowSettings));
        _registrations.Add(Register(_quitSignal, onQuit));
    }

    public void RequestShowSettings() => _showSettingsSignal.Set();

    public void RequestQuit() => _quitSignal.Set();

    /// <summary>Espera a instância em execução encerrar (ela libera o mutex depois de devolver a barra).</summary>
    public bool WaitForFirstInstanceToExit(TimeSpan timeout)
    {
        try
        {
            if (!_mutex.WaitOne(timeout))
            {
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // A outra instância foi encerrada à força; o mutex passa a ser nosso do mesmo jeito.
        }

        _ownsMutex = true;
        return true;
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Unregister(null);
        }

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _showSettingsSignal.Dispose();
        _quitSignal.Dispose();
    }

    private static RegisteredWaitHandle Register(WaitHandle signal, Action callback)
    {
        return ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => callback(), null, Timeout.Infinite, executeOnlyOnce: false);
    }
}
