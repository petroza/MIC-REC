using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MicRec.Audio;

namespace MicRec.Rendering;

/// <summary>Draws the bar-style spectrum onto a Canvas each UI tick.</summary>
public static class EqualizerRenderer
{
    private static readonly Brush BarBrush = CreateBarBrush();

    private static Brush CreateBarBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x84, 0x97, 0x85), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xBE, 0xA1, 0x68), 0.75));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xC8, 0x62, 0x50), 1));
        b.Freeze();
        return b;
    }

    public static void Render(Canvas canvas, MeterSnapshot snap)
    {
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        canvas.Children.Clear();
        if (w <= 1 || h <= 1)
            return;

        int n = snap.Bands.Length;
        if (n == 0) return;
        double barWidth = w / n;

        for (int i = 0; i < n; i++)
        {
            double bh = Math.Max(2, snap.Bands[i] * h);
            var rect = new Rectangle
            {
                Width = Math.Max(1, barWidth - 4),
                Height = bh,
                Fill = BarBrush,
                RadiusX = 2,
                RadiusY = 2,
            };
            Canvas.SetLeft(rect, i * barWidth + 2);
            Canvas.SetTop(rect, h - bh);
            canvas.Children.Add(rect);
        }
    }
}
