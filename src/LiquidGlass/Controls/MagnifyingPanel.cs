using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiquidGlass.Models;

namespace LiquidGlass.Controls;

/// <summary>
/// Painel da dock: alinha os itens ao longo de uma borda e amplia os que estão perto do cursor,
/// como no macOS. O ponto sob o cursor permanece fixo enquanto a dock cresce para os dois lados.
/// </summary>
public sealed class MagnifyingPanel : Panel
{
    public static readonly DependencyProperty EdgeProperty = RegisterLayout(nameof(Edge), typeof(DockEdge), DockEdge.Bottom);

    public static readonly DependencyProperty ItemSizeProperty = RegisterLayout(nameof(ItemSize), typeof(double), 52.0);

    public static readonly DependencyProperty SpacingProperty = RegisterLayout(nameof(Spacing), typeof(double), 6.0);

    public static readonly DependencyProperty MagnificationProperty = RegisterAnimated(nameof(Magnification), 1.7);

    public static readonly DependencyProperty MagnificationRangeProperty = RegisterAnimated(nameof(MagnificationRange), 3.0);

    public static readonly DependencyProperty AnimationSpeedProperty = RegisterAnimated(nameof(AnimationSpeed), 1.0);

    /// <summary>Posição do cursor ao longo da dock, em coordenadas do painel; <c>NaN</c> quando fora.</summary>
    public static readonly DependencyProperty CursorPositionProperty = RegisterAnimated(nameof(CursorPosition), double.NaN);

    /// <summary>Quanto o item está "presente" (0 a 1). Usado para animar entrada e saída.</summary>
    public static readonly DependencyProperty PresenceProperty = DependencyProperty.RegisterAttached(
        "Presence",
        typeof(double),
        typeof(MagnifyingPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>Itens não ampliáveis (separadores) usam o próprio tamanho desejado.</summary>
    public static readonly DependencyProperty IsMagnifiableProperty = DependencyProperty.RegisterAttached(
        "IsMagnifiable",
        typeof(bool),
        typeof(MagnifyingPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly RoutedEvent ContentBoundsChangedEvent = EventManager.RegisterRoutedEvent(
        "ContentBoundsChanged",
        RoutingStrategy.Bubble,
        typeof(ContentBoundsChangedHandler),
        typeof(MagnifyingPanel));

    private const double Stiffness = 16.0;
    private const double SettleThreshold = 0.0005;

    private readonly Dictionary<UIElement, double> _scales = [];
    private double _anchor = double.NaN;
    private bool _isAnimating;
    private TimeSpan _lastFrame;
    private Rect _lastContentBounds = Rect.Empty;

    public DockEdge Edge
    {
        get => (DockEdge)GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    public double ItemSize
    {
        get => (double)GetValue(ItemSizeProperty);
        set => SetValue(ItemSizeProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double Magnification
    {
        get => (double)GetValue(MagnificationProperty);
        set => SetValue(MagnificationProperty, value);
    }

    public double MagnificationRange
    {
        get => (double)GetValue(MagnificationRangeProperty);
        set => SetValue(MagnificationRangeProperty, value);
    }

    public double AnimationSpeed
    {
        get => (double)GetValue(AnimationSpeedProperty);
        set => SetValue(AnimationSpeedProperty, value);
    }

    public double CursorPosition
    {
        get => (double)GetValue(CursorPositionProperty);
        set => SetValue(CursorPositionProperty, value);
    }

    private bool IsVertical => Edge != DockEdge.Bottom;

    public static double GetPresence(DependencyObject element) => (double)element.GetValue(PresenceProperty);

    public static void SetPresence(DependencyObject element, double value) => element.SetValue(PresenceProperty, value);

    public static bool GetIsMagnifiable(DependencyObject element) => (bool)element.GetValue(IsMagnifiableProperty);

    public static void SetIsMagnifiable(DependencyObject element, bool value) => element.SetValue(IsMagnifiableProperty, value);

    public static void AddContentBoundsChangedHandler(DependencyObject element, ContentBoundsChangedHandler handler)
    {
        (element as UIElement)?.AddHandler(ContentBoundsChangedEvent, handler);
    }

    public static void RemoveContentBoundsChangedHandler(DependencyObject element, ContentBoundsChangedHandler handler)
    {
        (element as UIElement)?.RemoveHandler(ContentBoundsChangedEvent, handler);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double along = 0;
        double cross = 0;

        foreach (UIElement child in InternalChildren)
        {
            var (childAlong, childCross) = MeasureChild(child);
            along += childAlong;
            cross = Math.Max(cross, childCross);
        }

        along += TotalSpacing();
        var availableAlong = IsVertical ? availableSize.Height : availableSize.Width;
        if (!double.IsInfinity(availableAlong))
        {
            along = Math.Max(along, availableAlong);
        }

        return ToSize(along, cross);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var panelAlong = IsVertical ? finalSize.Height : finalSize.Width;
        var panelCross = IsVertical ? finalSize.Width : finalSize.Height;
        var layout = ComputeLayout(panelAlong);

        var position = layout.Start;
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            var length = layout.Lengths[index];
            var thickness = layout.Thicknesses[index];
            var crossPosition = Edge == DockEdge.Left ? 0 : panelCross - thickness;

            child.Arrange(ToRect(position, crossPosition, length, thickness));
            position += length + layout.Gaps[index];
        }

        var baseCross = Edge == DockEdge.Left ? 0 : panelCross - ItemSize;
        RaiseContentBounds(ToRect(layout.Start, baseCross, layout.Length, ItemSize));
        return finalSize;
    }

    protected override void OnVisualChildrenChanged(DependencyObject? visualAdded, DependencyObject? visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        if (visualRemoved is UIElement removed)
        {
            _scales.Remove(removed);
        }
    }

    private static DependencyProperty RegisterLayout(string name, Type type, object defaultValue)
    {
        return DependencyProperty.Register(
            name,
            type,
            typeof(MagnifyingPanel),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsMeasure));
    }

    private static DependencyProperty RegisterAnimated(string name, double defaultValue)
    {
        return DependencyProperty.Register(
            name,
            typeof(double),
            typeof(MagnifyingPanel),
            new FrameworkPropertyMetadata(defaultValue, (d, _) => ((MagnifyingPanel)d).StartAnimation()));
    }

    private (double Along, double Cross) MeasureChild(UIElement child)
    {
        var presence = Math.Clamp(GetPresence(child), 0, 1);

        if (!GetIsMagnifiable(child))
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var along = (IsVertical ? child.DesiredSize.Height : child.DesiredSize.Width) * presence;
            return (along, ItemSize);
        }

        var size = ItemSize * CurrentScale(child) * presence;
        child.Measure(new Size(size, size));
        return (size, size);
    }

    private double CurrentScale(UIElement child) => _scales.TryGetValue(child, out var scale) ? scale : 1.0;

    private double TotalSpacing()
    {
        double total = 0;
        for (var index = 0; index < InternalChildren.Count - 1; index++)
        {
            total += GapAfter(index);
        }

        return total;
    }

    private double GapAfter(int index)
    {
        if (index >= InternalChildren.Count - 1)
        {
            return 0;
        }

        return Spacing * Math.Clamp(GetPresence(InternalChildren[index + 1]), 0, 1);
    }

    private Layout ComputeLayout(double panelAlong)
    {
        var count = InternalChildren.Count;
        var layout = new Layout(count);

        double baseLength = 0;
        double length = 0;
        for (var index = 0; index < count; index++)
        {
            var child = InternalChildren[index];
            var presence = Math.Clamp(GetPresence(child), 0, 1);
            double along, cross, scale;

            if (GetIsMagnifiable(child))
            {
                scale = CurrentScale(child);
                along = cross = ItemSize * scale * presence;
            }
            else
            {
                scale = 1.0;
                along = (IsVertical ? child.DesiredSize.Height : child.DesiredSize.Width) * presence;
                cross = ItemSize;
            }

            layout.Lengths[index] = along;
            layout.Thicknesses[index] = cross;
            layout.BaseLengths[index] = along / scale;
            layout.Gaps[index] = GapAfter(index);
            baseLength += layout.BaseLengths[index] + layout.Gaps[index];
            length += along + layout.Gaps[index];
        }

        var baseStart = (panelAlong - baseLength) / 2;
        layout.Length = length;
        layout.Start = baseStart - AnchorGrowth(layout, baseStart);
        layout.Start = Math.Clamp(layout.Start, 0, Math.Max(panelAlong - length, 0));
        layout.BaseStart = baseStart;
        return layout;
    }

    /// <summary>
    /// Quanto a dock cresce antes do ponto de ancoragem. Mantém o ponto do cursor imóvel:
    /// a parte ampliada à esquerda dele empurra o começo da dock para trás.
    /// </summary>
    private double AnchorGrowth(Layout layout, double baseStart)
    {
        if (double.IsNaN(_anchor))
        {
            return 0;
        }

        var anchor = _anchor - baseStart;
        double basePosition = 0;
        double growth = 0;

        for (var index = 0; index < layout.Lengths.Length; index++)
        {
            var baseLength = layout.BaseLengths[index];
            if (anchor <= basePosition)
            {
                break;
            }

            var fraction = Math.Clamp((anchor - basePosition) / Math.Max(baseLength, 0.001), 0, 1);
            growth += (layout.Lengths[index] - baseLength) * fraction;
            basePosition += baseLength + layout.Gaps[index];
        }

        return growth;
    }

    private void StartAnimation()
    {
        if (!double.IsNaN(CursorPosition))
        {
            _anchor = CursorPosition;
        }

        if (_isAnimating)
        {
            return;
        }

        _isAnimating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        _isAnimating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var elapsed = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        if (elapsed <= 0)
        {
            return;
        }

        var blend = 1 - Math.Exp(-Math.Min(elapsed, 0.1) * Stiffness * Math.Max(AnimationSpeed, 0.1));
        var changed = false;
        var settled = true;

        foreach (var (child, target) in ComputeTargets())
        {
            var current = CurrentScale(child);
            var next = current + ((target - current) * blend);
            if (Math.Abs(target - next) < SettleThreshold)
            {
                next = target;
            }
            else
            {
                settled = false;
            }

            if (next != current)
            {
                _scales[child] = next;
                changed = true;
            }
        }

        if (changed)
        {
            InvalidateMeasure();
        }

        if (settled)
        {
            if (double.IsNaN(CursorPosition))
            {
                _anchor = double.NaN;
            }

            StopAnimation();
        }
    }

    /// <summary>Escala desejada de cada item, a partir da distância até o cursor no layout sem ampliação.</summary>
    private IEnumerable<(UIElement Child, double Target)> ComputeTargets()
    {
        var panelAlong = IsVertical ? ActualHeight : ActualWidth;
        var layout = ComputeLayout(panelAlong);
        var range = Math.Max(MagnificationRange, 0.5) * ItemSize;
        var peak = Math.Max(Magnification, 1.0);
        var position = layout.BaseStart;

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            var baseLength = layout.BaseLengths[index];
            var center = position + (baseLength / 2);
            position += baseLength + layout.Gaps[index];

            if (!GetIsMagnifiable(child) || double.IsNaN(CursorPosition))
            {
                yield return (child, 1.0);
                continue;
            }

            var distance = Math.Abs(CursorPosition - center) / range;
            var falloff = distance >= 1 ? 0 : (Math.Cos(distance * Math.PI) + 1) / 2;
            yield return (child, 1 + ((peak - 1) * falloff));
        }
    }

    private void RaiseContentBounds(Rect bounds)
    {
        if (bounds == _lastContentBounds)
        {
            return;
        }

        _lastContentBounds = bounds;
        RaiseEvent(new ContentBoundsChangedEventArgs(ContentBoundsChangedEvent, this, bounds));
    }

    private Size ToSize(double along, double cross) => IsVertical ? new Size(cross, along) : new Size(along, cross);

    private Rect ToRect(double along, double cross, double length, double thickness)
    {
        return IsVertical ? new Rect(cross, along, thickness, length) : new Rect(along, cross, length, thickness);
    }

    private sealed class Layout(int count)
    {
        public double[] Lengths { get; } = new double[count];

        public double[] BaseLengths { get; } = new double[count];

        public double[] Thicknesses { get; } = new double[count];

        public double[] Gaps { get; } = new double[count];

        public double Start { get; set; }

        public double BaseStart { get; set; }

        public double Length { get; set; }
    }
}
