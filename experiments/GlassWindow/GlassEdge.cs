using System.Windows;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

// Inward-only bevel lighting. This never transforms or resamples the desktop compositor.
public sealed class GlassEdge : FrameworkElement
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double),
        typeof(GlassEdge), new FrameworkPropertyMetadata(32d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public GlassEdge() { IsHitTestVisible = false; }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 18 || ActualHeight < 18) return;
        // Softly fading bands suggest a polished glass cut, sharing the exact outer corner arc.
        for (int inset = 0; inset < 8; inset++)
        {
            var outer = new RectangleGeometry(new Rect(inset, inset, ActualWidth - inset * 2, ActualHeight - inset * 2),
                Math.Max(0, Radius - inset), Math.Max(0, Radius - inset));
            var inner = new RectangleGeometry(new Rect(inset + 1, inset + 1, ActualWidth - (inset + 1) * 2, ActualHeight - (inset + 1) * 2),
                Math.Max(0, Radius - inset - 1), Math.Max(0, Radius - inset - 1));
            byte alpha = (byte)Math.Round(105 * Math.Pow(1 - inset / 8d, 2));
            var light = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(alpha, 255, 255, 255), 0),
                new(Color.FromArgb((byte)(alpha * .2), 255, 255, 255), .32),
                new(Color.FromArgb((byte)(alpha * .42), 5, 10, 20), .58),
                new(Color.FromArgb((byte)(alpha * .7), 255, 255, 255), 1)
            }, new Point(0, 0), new Point(1, 1));
            light.Freeze();
            drawing.DrawGeometry(light, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner));
        }
    }
}
