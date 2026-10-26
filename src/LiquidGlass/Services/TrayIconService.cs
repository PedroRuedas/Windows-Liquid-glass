using System.ComponentModel;
using ManagedShell;
using ManagedShell.WindowsTray;

namespace LiquidGlass.Services;

/// <summary>
/// Recebe os ícones da bandeja do sistema (apps em segundo plano) usando a ManagedShell. Ela registra
/// uma janela de bandeja própria e repassa tudo ao Explorer, então a bandeja nativa continua funcionando.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly ShellConfig Config = ShellManager.DefaultShellConfig with
    {
        EnableTasksService = false,
        AutoStartTasksService = false,
        EnableTrayService = true,
        AutoStartTrayService = true,
    };

    private ShellManager? _shell;

    /// <summary>Disparado quando o serviço liga ou desliga (a coleção de ícones muda de instância).</summary>
    public event EventHandler? IconsChanged;

    /// <summary>Ícones visíveis da bandeja; <c>null</c> enquanto o serviço está desligado.</summary>
    public ICollectionView? Icons => _shell?.NotificationArea.AllIcons;

    public bool IsEnabled
    {
        get => _shell is not null;
        set
        {
            if (value == IsEnabled)
            {
                return;
            }

            if (value)
            {
                _shell = new ShellManager(Config);
            }
            else
            {
                _shell?.Dispose();
                _shell = null;
            }

            IconsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => IsEnabled = false;
}
