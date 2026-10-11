using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GlassDesk.GlassWindow;

public sealed class AnimatedTilePanel : Panel
{
    private readonly Dictionary<UIElement, Point> _positions = [];
    public int Columns { get; set; } = 7;
    public double RowHeight { get; set; } = 108;
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Columns * 130 : availableSize.Width;
        foreach (UIElement child in InternalChildren) child.Measure(new Size(width / Columns, RowHeight));
        return new Size(width, Math.Ceiling(InternalChildren.Count / (double)Columns) * RowHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = finalSize.Width / Columns;
        var active = new HashSet<UIElement>();
        for (int index = 0; index < InternalChildren.Count; index++)
        {
            UIElement child = InternalChildren[index]; active.Add(child);
            var next = new Point(index % Columns * width, index / Columns * RowHeight);
            var transform = child.RenderTransform as TranslateTransform ?? new TranslateTransform();
            child.RenderTransform = transform;
            child.Arrange(new Rect(next, new Size(width, RowHeight)));
            if (_positions.TryGetValue(child, out var previous) && next != previous)
            {
                double x = previous.X + transform.X - next.X;
                double y = previous.Y + transform.Y - next.Y;
                Animate(transform, TranslateTransform.XProperty, x);
                Animate(transform, TranslateTransform.YProperty, y);
            }
            _positions[child] = next;
        }
        foreach (var removed in _positions.Keys.Where(child => !active.Contains(child)).ToArray()) _positions.Remove(removed);
        return finalSize;
    }
    private static void Animate(TranslateTransform transform, DependencyProperty property, double from)
    {
        if (!SystemParameters.ClientAreaAnimation) { transform.BeginAnimation(property, null); transform.SetValue(property, 0d); return; }
        transform.BeginAnimation(property, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
