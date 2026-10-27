#if DEBUG
using System.Windows.Threading;

namespace LiquidGlass.Views;

/// <summary>Demonstrações (só no Debug): o painel aberto sem o mouse e fotos dele.</summary>
public partial class WindowPreview
{
    /// <summary>Demonstração (Debug): o painel fica aberto sem o mouse por perto.</summary>
    internal bool IsDemo { get; set; }

    /// <summary>Demonstração (Debug): fotografa o próximo recolhimento no meio do caminho.</summary>
    internal bool CaptureNextCollapse { get; set; }

    /// <summary>Demonstração (Debug): fotografa a tela em volta do painel e do ícone.</summary>
    internal void SaveDemoFrame(string name)
    {
        if (IsOpen)
        {
            Graphics.DemoCapture.Save(name, PopupContent, _anchor);
        }
    }

    private void CaptureCollapse()
    {
        if (!CaptureNextCollapse)
        {
            return;
        }

        CaptureNextCollapse = false;
        var timer = new DispatcherTimer { Interval = DockPopupAnimation.CloseDuration * 0.8 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveDemoFrame("preview-collapsing");
        };
        timer.Start();
    }
}
#endif
