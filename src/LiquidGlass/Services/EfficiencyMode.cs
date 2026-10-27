using System.Windows.Threading;
using LiquidGlass.Interop;

namespace LiquidGlass.Services;

/// <summary>
/// Coloca a dock no modo de eficiência do Windows (EcoQoS e prioridade abaixo do normal) enquanto
/// ninguém interage com ela. O Windows então a executa nos núcleos e frequências mais econômicos.
/// Qualquer interação (cursor na dock, menu, painel ou personalização aberta) volta ao modo normal
/// na hora, para as animações continuarem fluidas. Ao entrar no modo, a memória ociosa é liberada.
/// </summary>
public sealed class EfficiencyMode : IDisposable
{
    /// <summary>Espera depois da última interação: cobre o fim das animações de saída.</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(3);

    private readonly HashSet<object> _activeSources = [];
    private readonly DispatcherTimer _idleTimer = new() { Interval = IdleDelay };
    private bool _isEfficient;

    public EfficiencyMode()
    {
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            Apply(efficient: true);
        };
        _idleTimer.Start();
    }

    /// <summary>Marca <paramref name="source"/> como em uso (ou não). A dock economiza quando nada está em uso.</summary>
    public void SetActive(object source, bool active)
    {
        var changed = active ? _activeSources.Add(source) : _activeSources.Remove(source);
        if (!changed)
        {
            return;
        }

        _idleTimer.Stop();
        if (_activeSources.Count > 0)
        {
            Apply(efficient: false);
        }
        else
        {
            _idleTimer.Start();
        }
    }

    public void Dispose()
    {
        _idleTimer.Stop();
        Apply(efficient: false);
    }

    private void Apply(bool efficient)
    {
        if (efficient == _isEfficient)
        {
            return;
        }

        _isEfficient = efficient;
        Kernel32.SetEfficiencyMode(efficient);
        if (efficient)
        {
            ReleaseIdleMemory();
        }
    }

    /// <summary>
    /// Parada é quando a dock pode devolver memória sem custo visível: o heap é compactado (e o
    /// espaço livre devolvido ao sistema) e as páginas que só serviram às animações saem da RAM.
    /// Sem isso o processo ficava com ~200 MB depois de abrir menus, painéis e a visão geral.
    /// </summary>
    private static void ReleaseIdleMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Kernel32.TrimWorkingSet();
    }
}
