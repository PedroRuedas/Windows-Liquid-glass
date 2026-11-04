using System.Collections.Concurrent;

namespace LiquidGlass.Services;

/// <summary>
/// Thread de fundo em apartamento STA para chamadas ao shell (ícones, lista de apps, recentes)
/// que demorariam na thread da interface. Uma tarefa por vez, na ordem em que chegaram.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        return completion.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(1));
        _queue.Dispose();
    }

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }
}
