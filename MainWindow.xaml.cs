using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MicRec.Audio;
using MicRec.Rendering;
using NAudio.CoreAudioApi;

namespace MicRec;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const double MeterMaxHeight = 172;

    private static readonly System.Windows.Media.Brush GreenBrush = Freeze(Color.FromRgb(0x84, 0x97, 0x85));
    private static readonly System.Windows.Media.Brush YellowBrush = Freeze(Color.FromRgb(0xBE, 0xA1, 0x68));
    private static readonly System.Windows.Media.Brush RedBrush = Freeze(Color.FromRgb(0xC8, 0x62, 0x50));

    private readonly AudioEngine _engine = new();
    private DispatcherTimer? _uiTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    private static System.Windows.Media.Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var devices = AudioEngine.GetInputDevices();
        DeviceCombo.ItemsSource = devices;
        var defaultDevice = AudioEngine.GetDefaultInputDevice();
        var match = defaultDevice != null ? devices.FirstOrDefault(d => d.ID == defaultDevice.ID) : null;
        DeviceCombo.SelectedItem = match ?? devices.FirstOrDefault();

        QualityCombo.ItemsSource = AudioEngine.Qualities;
        QualityCombo.SelectedIndex = 0;

        string defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MicRec nahrávky");
        OutputFolderBox.Text = defaultFolder;
        LibraryTab.Initialize(defaultFolder);

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        _uiTimer?.Stop();
        LibraryTab.Shutdown();
        _engine.Dispose();
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        var snap = _engine.GetSnapshot();
        EqualizerRenderer.Render(EqCanvas, snap);

        MeterL.Height = NormalizeDb(snap.PeakL) * MeterMaxHeight;
        MeterR.Height = NormalizeDb(snap.PeakR) * MeterMaxHeight;
        MeterL.Fill = MeterColor(snap.PeakL);
        MeterR.Fill = MeterColor(snap.PeakR);

        GainText.Text = $"{snap.GainDb:+0.0;-0.0;0.0} dB";

        if (_engine.IsRecording)
            ElapsedText.Text = _engine.Elapsed.ToString(@"hh\:mm\:ss");

        UpdateStatusEye(snap);
    }

    private void UpdateStatusEye(MeterSnapshot snap)
    {
        bool recording = _engine.IsRecording;
        bool paused = recording && _engine.IsPaused;
        double level = recording ? Math.Max(snap.RmsL, snap.RmsR) : LibraryTab.PlaybackLevel;
        RecordingEye.SetSignal(level, recording || LibraryTab.IsPreviewPlaying, paused);
        SignalHint.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
    }
    private static double NormalizeDb(double linearPeak)
    {
        double db = 20 * Math.Log10(linearPeak + 1e-9);
        double norm = (db + 50) / 50.0; // -50..0 dBFS -> 0..1
        return Math.Clamp(norm, 0, 1);
    }

    private static System.Windows.Media.Brush MeterColor(double linearPeak)
    {
        double db = 20 * Math.Log10(linearPeak + 1e-9);
        if (db > -1) return RedBrush;
        if (db > -6) return YellowBrush;
        return GreenBrush;
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not MMDevice device)
        {
            MessageBox.Show(this, "Nejprve vyberte mikrofon.", "MicRec", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var quality = QualityCombo.SelectedItem as RecordingQuality ?? AudioEngine.Qualities[0];

        string folder = LibraryTab.CurrentFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, "Vyberte výstupní složku.", "MicRec", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Složku nelze vytvořit: {ex.Message}", "MicRec", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string filePath = Path.Combine(folder, $"Nahravka_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wav");

        try
        {
            _engine.LimiterEnabled = ChkLimiter.IsChecked == true;
            _engine.TargetDb = TargetSlider.Value;
            _engine.Start(device, filePath, quality);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Nahrávání se nepodařilo spustit: {ex.Message}", "MicRec", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        BtnStart.IsEnabled = false;
        BtnPause.IsEnabled = true;
        BtnPause.Content = "Ⅱ  Pauza";
        BtnStop.IsEnabled = true;
        DeviceCombo.IsEnabled = false;
        QualityCombo.IsEnabled = false;
        OutputFolderBox.IsEnabled = false;
        BtnBrowse.IsEnabled = false;
        StatusText.Text = "Nahrávám…";
        FileText.Text = $"Nahrávám do: {filePath}";
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_engine.IsRecording)
            return;

        if (_engine.IsPaused)
        {
            _engine.Resume();
            BtnPause.Content = "Ⅱ  Pauza";
            StatusText.Text = "Nahrávám…";
        }
        else
        {
            _engine.Pause();
            BtnPause.Content = "▶  Pokračovat";
            StatusText.Text = "Pozastaveno";
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        string? path = _engine.CurrentFilePath;
        _engine.Stop();

        BtnStart.IsEnabled = true;
        BtnPause.IsEnabled = false;
        BtnPause.Content = "Ⅱ  Pauza";
        BtnStop.IsEnabled = false;
        DeviceCombo.IsEnabled = true;
        QualityCombo.IsEnabled = true;
        OutputFolderBox.IsEnabled = true;
        BtnBrowse.IsEnabled = true;
        StatusText.Text = "Zastaveno";
        ElapsedText.Text = "00:00:00";
        if (path != null)
            FileText.Text = $"Uloženo: {path}";

        LibraryTab.Refresh();
    }

    private void ChkLimiter_Changed(object sender, RoutedEventArgs e)
    {
        _engine.LimiterEnabled = ChkLimiter.IsChecked == true;
    }

    private void TargetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _engine.TargetDb = e.NewValue;
        if (TargetLabel != null)
            TargetLabel.Text = $"{e.NewValue:0} dBFS";
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Vyberte kořenovou složku knihovny nahrávek",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputFolderBox.Text)
                ? OutputFolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputFolderBox.Text = dlg.SelectedPath;
            LibraryTab.Initialize(dlg.SelectedPath);
        }
    }

    private void OutputFolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        string path = OutputFolderBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path) && path != LibraryTab.RootPath)
        {
            try
            {
                Directory.CreateDirectory(path);
                LibraryTab.Initialize(path);
            }
            catch { /* leave as typed; user will get an error on Start instead */ }
        }
    }
}



