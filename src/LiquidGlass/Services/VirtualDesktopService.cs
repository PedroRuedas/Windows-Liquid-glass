using System.Runtime.InteropServices;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>
/// Lista, troca, cria e remove as áreas de trabalho virtuais do Windows e move janelas entre elas.
/// Usa a interface interna do Explorer quando ela existe nesta versão do Windows; sem ela, a dock
/// mostra só a área atual e deixa a troca de áreas com o próprio Windows.
/// </summary>
public sealed class VirtualDesktopService
{
    private const int DisconnectedServer = unchecked((int)0x80010108);
    private const int ServerUnavailable = unchecked((int)0x800706BA);
    private const int ServerDied = unchecked((int)0x80010012);

    private IVirtualDesktopManager? _manager;
    private IVirtualDesktopManagerInternal? _internal;
    private IApplicationViewCollection? _views;
    private bool _internalFailed;

    /// <summary>A troca e a lista de áreas estão disponíveis nesta versão do Windows.</summary>
    public bool IsAvailable => Internal() is not null;

    public IReadOnlyList<Guid> GetDesktops()
    {
        return Call(manager =>
        {
            var array = manager.GetDesktops();
            var desktops = new List<Guid>();
            var interfaceId = typeof(IVirtualDesktop).GUID;
            for (var index = 0; index < array.GetCount(); index++)
            {
                desktops.Add(((IVirtualDesktop)array.GetAt(index, ref interfaceId)).GetId());
            }

            return (IReadOnlyList<Guid>)desktops;
        }) ?? [];
    }

    public Guid? GetCurrent() => Call(manager => (Guid?)manager.GetCurrentDesktop().GetId());

    /// <summary>
    /// Área da janela; <see cref="Guid.Empty"/> para janelas fixadas em todas as áreas (como a dock).
    /// </summary>
    public Guid? GetWindowDesktop(nint hwnd)
    {
        _manager ??= Create<IVirtualDesktopManager>(VirtualDesktopInterop.VirtualDesktopManager);
        return _manager is not null && _manager.GetWindowDesktopId(hwnd, out var id) == 0 ? id : null;
    }

    /// <summary>Troca de área sem a animação do Windows (a dock desenha a própria).</summary>
    public bool Switch(Guid desktop) => Call(manager => Find(manager, desktop) is { } target && Do(() => manager.SwitchDesktop(target)));

    public Guid? Add() => Call(manager => (Guid?)manager.CreateDesktop().GetId());

    /// <summary>Remove a área; as janelas dela vão para a vizinha, como na Visão de Tarefas.</summary>
    public bool Remove(Guid desktop)
    {
        return Call(manager =>
        {
            var desktops = GetDesktops();
            var index = desktops.ToList().IndexOf(desktop);
            if (index < 0 || desktops.Count < 2 || Find(manager, desktop) is not { } target)
            {
                return false;
            }

            var fallback = Find(manager, desktops[index == 0 ? 1 : index - 1]);
            return fallback is not null && Do(() => manager.RemoveDesktop(target, fallback));
        });
    }

    /// <summary>Move a janela de outro app para a área indicada.</summary>
    public bool MoveWindow(nint hwnd, Guid desktop)
    {
        return Call(manager =>
        {
            _views ??= QueryShell<IApplicationViewCollection>(typeof(IApplicationViewCollection).GUID);
            if (_views is null || Find(manager, desktop) is not { } target || _views.GetViewForHwnd(hwnd, out var view) != 0 || view == 0)
            {
                return false;
            }

            try
            {
                manager.MoveViewToDesktop(view, target);
                return true;
            }
            finally
            {
                Marshal.Release(view);
            }
        });
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }

    private static IVirtualDesktop? Find(IVirtualDesktopManagerInternal manager, Guid id)
    {
        try
        {
            return manager.FindDesktop(ref id);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static T? Create<T>(Guid classId)
        where T : class
    {
        try
        {
            return Type.GetTypeFromCLSID(classId) is { } type ? Activator.CreateInstance(type) as T : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static T? QueryShell<T>(Guid service)
        where T : class
    {
        if (Create<IShellServiceProvider>(VirtualDesktopInterop.ImmersiveShell) is not { } shell)
        {
            return null;
        }

        var interfaceId = typeof(T).GUID;
        if (shell.QueryService(ref service, ref interfaceId, out var pointer) != 0 || pointer == 0)
        {
            return null;
        }

        try
        {
            return Marshal.GetObjectForIUnknown(pointer) as T;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private IVirtualDesktopManagerInternal? Internal()
    {
        if (_internal is null && !_internalFailed)
        {
            _internal = QueryShell<IVirtualDesktopManagerInternal>(VirtualDesktopInterop.VirtualDesktopManagerInternalService);
            _internalFailed = _internal is null;
        }

        return _internal;
    }

    /// <summary>
    /// Executa a chamada na interface interna. Se o Explorer reiniciou, os objetos antigos ficam
    /// desconectados: eles são recriados e a chamada é repetida uma vez.
    /// </summary>
    private T? Call<T>(Func<IVirtualDesktopManagerInternal, T> call)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (Internal() is not { } manager)
            {
                return default;
            }

            try
            {
                return call(manager);
            }
            catch (COMException exception) when (exception.HResult is DisconnectedServer or ServerUnavailable or ServerDied)
            {
                _internal = null;
                _views = null;
                _manager = null;
            }
            catch (COMException)
            {
                return default;
            }
            catch (InvalidCastException)
            {
                // Interface com outro formato nesta versão do Windows: desiste dela de vez.
                _internal = null;
                _internalFailed = true;
                return default;
            }
        }

        return default;
    }
}
