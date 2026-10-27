using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LiquidGlass.Models;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Controls;

/// <summary>
/// Contêiner de um ícone da dock. Cuida das animações próprias do item: entrada, saída,
/// quique ao abrir um app e quique de atenção.
/// </summary>
[TemplatePart(Name = IconPartName, Type = typeof(FrameworkElement))]
public sealed class DockItemView : ButtonBase
{
    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(
        nameof(Edge),
        typeof(DockEdge),
        typeof(DockItemView),
        new PropertyMetadata(DockEdge.Bottom, (d, _) => ((DockItemView)d).ApplyLift()));

    public static readonly DependencyProperty AnimationSpeedProperty = DependencyProperty.Register(
        nameof(AnimationSpeed),
        typeof(double),
        typeof(DockItemView),
        new PropertyMetadata(1.0));

    public static readonly DependencyProperty ShowLabelProperty = DependencyProperty.Register(
        nameof(ShowLabel),
        typeof(bool),
        typeof(DockItemView),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowIndicatorProperty = DependencyProperty.Register(
        nameof(ShowIndicator),
        typeof(bool),
        typeof(DockItemView),
        new PropertyMetadata(true));

    /// <summary>Com as miniaturas das janelas abertas sobre o ícone, o rótulo não aparece por baixo delas.</summary>
    public static readonly DependencyProperty IsPreviewOpenProperty = DependencyProperty.Register(
        nameof(IsPreviewOpen),
        typeof(bool),
        typeof(DockItemView),
        new PropertyMetadata(false));

    /// <summary>Deslocamento do ícone para longe da borda (quique), em DIPs.</summary>
    public static readonly DependencyProperty LiftProperty = DependencyProperty.Register(
        nameof(Lift),
        typeof(double),
        typeof(DockItemView),
        new PropertyMetadata(0.0, (d, _) => ((DockItemView)d).ApplyLift()));

    private const string IconPartName = "PART_Icon";

    private readonly TranslateTransform _liftTransform = new();
    private DockItemViewModel? _item;
    private BounceKind _bounce;

    static DockItemView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DockItemView), new FrameworkPropertyMetadata(typeof(DockItemView)));
    }

    public DockItemView()
    {
        Loaded += OnLoaded;
        Unloaded += (_, _) => Attach(null);
        DataContextChanged += (_, e) => Attach(e.NewValue as DockItemViewModel);
    }

    private enum BounceKind
    {
        None,
        Launch,
        Attention,
    }

    public DockEdge Edge
    {
        get => (DockEdge)GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    public double AnimationSpeed
    {
        get => (double)GetValue(AnimationSpeedProperty);
        set => SetValue(AnimationSpeedProperty, value);
    }

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    public bool ShowIndicator
    {
        get => (bool)GetValue(ShowIndicatorProperty);
        set => SetValue(ShowIndicatorProperty, value);
    }

    public bool IsPreviewOpen
    {
        get => (bool)GetValue(IsPreviewOpenProperty);
        set => SetValue(IsPreviewOpenProperty, value);
    }

    public double Lift
    {
        get => (double)GetValue(LiftProperty);
        set => SetValue(LiftProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild(IconPartName) is FrameworkElement icon)
        {
            icon.RenderTransform = _liftTransform;
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _item is not null)
        {
            _item.OpenNewCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }

    private static DoubleAnimationUsingKeyFrames CreateBounce(BounceKind kind, double height)
    {
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };

        animation.KeyFrames.Add(new EasingDoubleKeyFrame(height, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.28)), easeOut));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.56)), easeIn));

        if (kind == BounceKind.Attention)
        {
            // Um segundo quique menor e uma pausa, como o pedido de atenção do macOS.
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(height * 0.3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.72)), easeOut));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.88)), easeIn));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6))));
        }
        else
        {
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.62))));
        }

        return animation;
    }

    private void Attach(DockItemViewModel? item)
    {
        if (_item is not null)
        {
            _item.PropertyChanged -= OnItemPropertyChanged;
        }

        _item = item;
        if (_item is not null && IsLoaded)
        {
            _item.PropertyChanged += OnItemPropertyChanged;
            UpdateBounce();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as DockItemViewModel);
        AnimatePresence(_item is { IsRemoving: true } ? 0 : 1, from: 0);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockItemViewModel.IsRemoving):
                AnimatePresence(_item!.IsRemoving ? 0 : 1, from: null);
                break;
            case nameof(DockItemViewModel.IsLaunching):
            case nameof(DockItemViewModel.NeedsAttention):
                UpdateBounce();
                break;
        }
    }

    private void AnimatePresence(double to, double? from)
    {
        var speed = Math.Max(AnimationSpeed, 0.1);
        var growing = to > 0;
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds((growing ? 0.38 : 0.26) / speed),
            EasingFunction = growing
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                : new CubicEase { EasingMode = EasingMode.EaseIn },
        };

        BeginAnimation(MagnifyingPanel.PresenceProperty, animation);
    }

    private void UpdateBounce()
    {
        var kind = _item switch
        {
            { NeedsAttention: true } => BounceKind.Attention,
            { IsLaunching: true } => BounceKind.Launch,
            _ => BounceKind.None,
        };

        if (kind == _bounce)
        {
            return;
        }

        _bounce = kind;
        if (kind == BounceKind.None)
        {
            var settle = new DoubleAnimation(0, TimeSpan.FromSeconds(0.18)) { EasingFunction = new QuadraticEase() };
            BeginAnimation(LiftProperty, settle);
            return;
        }

        var height = Math.Max(ActualHeight, 32) * (kind == BounceKind.Attention ? 0.75 : 0.5);
        var bounce = CreateBounce(kind, height);
        bounce.SpeedRatio = Math.Max(AnimationSpeed, 0.1);
        BeginAnimation(LiftProperty, bounce);
    }

    private void ApplyLift()
    {
        var lift = Lift;
        (_liftTransform.X, _liftTransform.Y) = Edge switch
        {
            DockEdge.Left => (lift, 0.0),
            DockEdge.Right => (-lift, 0.0),
            _ => (0.0, -lift),
        };
    }
}
