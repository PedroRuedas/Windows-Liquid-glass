using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiquidGlass.Models;

namespace LiquidGlass.Views;

/// <summary>
/// Esconde a dock para fora da tela quando o mouse se afasta e a traz de volta quando ele encosta
/// na borda. A animação usa a margem do conteúdo para que o vidro acompanhe o movimento.
/// </summary>
internal sealed class AutoHideController
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(700);

    private readonly FrameworkElement _content;
    private readonly Func<bool> _isCursorAtEdge;
    private readonly Func<bool> _shouldStayVisible;
    private readonly DispatcherTimer _timer;
    private DateTime _lastActivity = DateTime.Now;
    private bool _isEnabled;

    public AutoHideController(FrameworkElement content, Func<bool> isCursorAtEdge, Func<bool> shouldStayVisible)
    {
        _content = content;
        _isCursorAtEdge = isCursorAtEdge;
        _shouldStayVisible = shouldStayVisible;
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Poll();
    }

    public bool IsHidden { get; private set; }

    public DockEdge Edge { get; set; }

    /// <summary>Distância, em DIPs, que o conteúdo percorre para sair da tela.</summary>
    public double HiddenDistance { get; set; }

    public double AnimationSpeed { get; set; } = 1;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            _lastActivity = DateTime.Now;
            if (value)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                Reveal();
            }
        }
    }

    public void Stop() => _timer.Stop();

    public void Reveal()
    {
        IsHidden = false;
        Animate(new Thickness(0), new CubicEase { EasingMode = EasingMode.EaseOut }, 0.28);
    }

    public void Conceal()
    {
        IsHidden = true;
        var distance = HiddenDistance;
        var margin = Edge switch
        {
            DockEdge.Left => new Thickness(-distance, 0, distance, 0),
            DockEdge.Right => new Thickness(distance, 0, -distance, 0),
            _ => new Thickness(0, distance, 0, -distance),
        };
        Animate(margin, new CubicEase { EasingMode = EasingMode.EaseIn }, 0.32);
    }

    private void Poll()
    {
        var now = DateTime.Now;
        if (_shouldStayVisible() || (IsHidden && _isCursorAtEdge()))
        {
            _lastActivity = now;
            if (IsHidden)
            {
                Reveal();
            }
        }
        else if (!IsHidden && now - _lastActivity > HideDelay)
        {
            Conceal();
        }
    }

    private void Animate(Thickness to, IEasingFunction easing, double seconds)
    {
        var animation = new ThicknessAnimation(to, TimeSpan.FromSeconds(seconds / Math.Max(AnimationSpeed, 0.1)))
        {
            EasingFunction = easing,
        };
        _content.BeginAnimation(FrameworkElement.MarginProperty, animation);
    }
}
