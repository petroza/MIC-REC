using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MicRec.Audio;

namespace MicRec.Controls;

public sealed class WaveformTimeline : FrameworkElement
{
    private WaveformData? _data;
    private double _duration, _position;
    private bool _dragging;
    private double _dragPosition;
    private bool _selecting;
    private double _selectionAnchor;
    private double? _selectionStart, _selectionEnd;
    private Geometry[] _geometry = Array.Empty<Geometry>();
    private static readonly Brush Background = Brush("#0D1012"), Wave = Brush("#81998A"), Played = Brush("#C77A63"), Muted = Brush("#929691");
    private static readonly Brush SelectionFill = BrushAlpha("#C86250", 60);
    private static readonly Pen GridPen = new(Brush("#282E2F"), 1), CursorPen = new(Brush("#E6AD89"), 1), SelectionEdgePen = new(Brush("#C86250"), 1.5);
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? SelectionChanged;
    public bool IsScrubbing => _dragging;
    public double Duration { get => _duration; set { _duration = Math.Max(0, value); InvalidateVisual(); } }
    public double Position { get => _position; set { _position = Math.Clamp(value, 0, Duration); InvalidateVisual(); } }
    public WaveformData? Data { get => _data; set { _data = value; ClearSelection(); Rebuild(); } }
    public string Placeholder { get; set; } = "Vyberte nahrávku a spusťte přehrávání.";
    public double? SelectionStart => _selectionStart;
    public double? SelectionEnd => _selectionEnd;

    public WaveformTimeline()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        ClipToBounds = true;
        ToolTip = "Kliknutím nebo tažením posuňte přehrávání. Ctrl + tažení: označit úsek ke střihu. Šipky: ±5 s, Ctrl + šipky: ±1 s. Home / End: začátek / konec.";
        System.Windows.Automation.AutomationProperties.SetName(this, "Zvuková vlna a časová osa přehrávání");
    }

    public void ClearSelection()
    {
        if (_selectionStart == null && _selectionEnd == null) return;
        _selectionStart = null;
        _selectionEnd = null;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private double PlotWidth => Math.Max(1, ActualWidth - 44);
    public double SecondsAt(double x) => Math.Clamp((x - 22) / PlotWidth, 0, 1) * Duration;
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); Rebuild(); }

    private void Rebuild()
    {
        if (_data == null || ActualWidth <= 44) { _geometry = Array.Empty<Geometry>(); InvalidateVisual(); return; }
        int lanes = _data.Min.Length;
        double laneHeight = Math.Max(1, (ActualHeight - 27) / lanes);
        int pixels = Math.Max(1, (int)PlotWidth);
        _geometry = new Geometry[lanes];
        for (int channel = 0; channel < lanes; channel++)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                int bins = _data.Min[channel].Length;
                double center = 26 + laneHeight * (channel + .5), amplitude = Math.Max(1, laneHeight / 2 - 5);
                for (int pixel = 0; pixel < pixels; pixel++)
                {
                    int first = pixel * bins / pixels, end = Math.Min(bins, Math.Max(first + 1, (pixel + 1) * bins / pixels));
                    float min = 0, max = 0;
                    for (int bin = first; bin < end; bin++) { min = Math.Min(min, _data.Min[channel][bin]); max = Math.Max(max, _data.Max[channel][bin]); }
                    double x = 22 + pixel, top = center - max * amplitude, bottom = center - min * amplitude;
                    ctx.BeginFigure(new Point(x, top), true, true);
                    ctx.LineTo(new Point(x + 1, top), true, false); ctx.LineTo(new Point(x + 1, Math.Max(top + .6, bottom)), true, false); ctx.LineTo(new Point(x, Math.Max(top + .6, bottom)), true, false);
                }
            }
            geo.Freeze(); _geometry[channel] = geo;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Background, null, new Rect(RenderSize), 4, 4);
        if (ActualWidth <= 44 || ActualHeight <= 27) return;
        double shownPosition = _dragging ? _dragPosition : Position;
        double cursorX = 22 + (Duration > 0 ? shownPosition / Duration : 0) * PlotWidth;
        int ticks = Math.Max(2, (int)(PlotWidth / 110));
        for (int i = 0; i <= ticks; i++)
        {
            double x = 22 + PlotWidth * i / ticks;
            dc.DrawLine(GridPen, new Point(x, 22), new Point(x, ActualHeight));
            var label = Text(FormatTime(Duration * i / ticks), 10);
            dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 2, Math.Max(2, ActualWidth - label.Width - 2)), 5));
        }
        dc.DrawLine(GridPen, new Point(0, 24), new Point(ActualWidth, 24));
        if (_data != null)
        {
            for (int lane = 0; lane < _geometry.Length; lane++)
            {
                double center = 26 + (ActualHeight - 27) / _geometry.Length * (lane + .5);
                dc.DrawLine(GridPen, new Point(22, center), new Point(ActualWidth - 22, center));
                dc.DrawText(Text(_geometry.Length == 1 ? "M" : lane == 0 ? "L" : "R", 9), new Point(6, center - 6));
                dc.DrawGeometry(Wave, null, _geometry[lane]);
                dc.PushClip(new RectangleGeometry(new Rect(0, 25, cursorX, Math.Max(0, ActualHeight - 25))));
                dc.DrawGeometry(Played, null, _geometry[lane]); dc.Pop();
            }
        }
        else
        {
            var label = Text(Placeholder, 11);
            dc.DrawText(label, new Point(Math.Max(24, (ActualWidth - label.Width) / 2), (ActualHeight + 24 - label.Height) / 2));
        }
        if (_selectionStart.HasValue && _selectionEnd.HasValue && Duration > 0)
        {
            double sx = 22 + _selectionStart.Value / Duration * PlotWidth;
            double ex = 22 + _selectionEnd.Value / Duration * PlotWidth;
            dc.DrawRectangle(SelectionFill, null, new Rect(sx, 25, Math.Max(0, ex - sx), Math.Max(0, ActualHeight - 25)));
            dc.DrawLine(SelectionEdgePen, new Point(sx, 21), new Point(sx, ActualHeight));
            dc.DrawLine(SelectionEdgePen, new Point(ex, 21), new Point(ex, ActualHeight));
        }
        if (Duration > 0)
        {
            dc.DrawLine(CursorPen, new Point(cursorX, 23), new Point(cursorX, ActualHeight));
            dc.DrawRoundedRectangle(CursorPen.Brush, null, new Rect(cursorX - 3, 21, 6, 7), 1, 1);
        }
        if (IsKeyboardFocused) dc.DrawRoundedRectangle(null, CursorPen, new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 4, 4);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Duration <= 0) return;
        Focus();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _selecting = true;
            _selectionAnchor = SecondsAt(e.GetPosition(this).X);
            _selectionStart = _selectionAnchor;
            _selectionEnd = _selectionAnchor;
            CaptureMouse();
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        _dragging = true; _dragPosition = SecondsAt(e.GetPosition(this).X); CaptureMouse(); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_selecting)
        {
            double t = SecondsAt(e.GetPosition(this).X);
            _selectionStart = Math.Min(_selectionAnchor, t);
            _selectionEnd = Math.Max(_selectionAnchor, t);
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_dragging) { _dragPosition = SecondsAt(e.GetPosition(this).X); InvalidateVisual(); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_selecting)
        {
            _selecting = false;
            ReleaseMouseCapture();
            // A near-zero-length Ctrl-click clears the selection instead of leaving a stray point.
            if (_selectionEnd - _selectionStart < 0.05)
            {
                _selectionStart = null;
                _selectionEnd = null;
            }
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (!_dragging) return;
        double target = SecondsAt(e.GetPosition(this).X);
        _dragging = false; ReleaseMouseCapture(); SeekRequested?.Invoke(this, target); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); _dragging = false; _selecting = false; InvalidateVisual(); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && (_selectionStart.HasValue || _selectionEnd.HasValue))
        {
            ClearSelection();
            e.Handled = true;
            return;
        }
        if (Duration <= 0) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1 : 5;
        double? target = e.Key switch { Key.Left => Position - step, Key.Right => Position + step, Key.Home => 0, Key.End => Duration, _ => null };
        if (target.HasValue) { SeekRequested?.Invoke(this, Math.Clamp(target.Value, 0, Duration)); e.Handled = true; }
    }
    public static string FormatTime(double seconds) { var time = TimeSpan.FromSeconds(Math.Max(0, seconds)); return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}"; }
    private FormattedText Text(string text, double size) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), size, Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private static Brush Brush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    private static Brush BrushAlpha(string color, byte alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
