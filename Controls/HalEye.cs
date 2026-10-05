using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace MicRec.Controls;

/// <summary>The lens reacts to measured RMS, with a quick attack and gentle release.</summary>
public sealed class HalEye : FrameworkElement
{
    private static readonly Brush Glass = Radial(("#53201A", 0), ("#211212", .38), ("#090A0B", .78), ("#232022", .94), ("#08090A", 1));
    private static readonly Brush Core = Radial(("#EDBA78", 0), ("#C25435", .24), ("#79251C", .58), ("#260E0D", 1));
    private static readonly Brush Glow = Radial(("#AAAC3821", 0), ("#004F1713", 1));
    private static readonly Brush Amber = Radial(("#D2AE72", 0), ("#56331D", .5), ("#211410", 1));
    private static readonly Brush Outer = Solid("#070809"), Ring = Solid("#181A1C"), Hotspot = Solid("#E7B784");
    private static readonly Pen Rim = new(Solid("#393B3C"), 1), InnerRim = new(Solid("#27292B"), 1), Iris = new(Solid("#38201C"), 1);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime, _energy;
    private bool _paused;
    public double Energy => _energy;

    public void SetSignal(double rms, bool active, bool paused = false)
    {
        double time = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(time - _lastTime, .001, .1);
        _lastTime = time;
        double target = active && !paused && double.IsFinite(rms)
            ? Math.Clamp((20 * Math.Log10(Math.Max(rms, 1e-9)) + 54) / 48, 0, 1) : 0;
        _energy += (target - _energy) * (1 - Math.Exp(-dt / (target > _energy ? .045 : .22)));
        _paused = paused;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 156, size / 156));
        var center = new Point(78, 78);
        dc.DrawEllipse(Outer, Rim, center, 77, 77);
        dc.DrawEllipse(Ring, InnerRim, center, 73, 73);
        dc.DrawEllipse(Glass, null, center, 68, 68);
        dc.PushOpacity(.28 + .65 * _energy);
        dc.DrawEllipse(Glow, null, center, 46 + 19 * _energy, 46 + 19 * _energy);
        dc.Pop();
        dc.DrawEllipse(null, Iris, center, 35, 35);
        dc.PushOpacity(.65 + .35 * _energy);
        dc.DrawEllipse(_paused ? Amber : Core, null, center, 24 + 12 * _energy, 24 + 12 * _energy);
        dc.Pop();
        dc.PushOpacity(.3 + .5 * _energy);
        dc.DrawEllipse(Hotspot, null, center, 3 + 2.5 * _energy, 3 + 2.5 * _energy);
        dc.Pop(); dc.Pop(); dc.Pop();
    }

    private static Brush Solid(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    private static Brush Radial(params (string Color, double Offset)[] stops)
    {
        var brush = new RadialGradientBrush();
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color), offset));
        brush.Freeze(); return brush;
    }
}
