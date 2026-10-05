using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MicRec.Dialogs;
using MicRec.Library;
using NAudio.Wave;
using MicRec.Audio;
using MicRec.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MicRec;

public partial class LibraryPanel : UserControl
{
    public string RootPath { get; private set; } = "";


    private PreviewPlayback? _previewPlayer;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private CancellationTokenSource? _waveformCancellation;
    private Task _waveformTask = Task.CompletedTask;
    private readonly SemaphoreSlim _waveformGate = new(1, 1);
    public bool IsPreviewPlaying => _previewPlayer?.IsPlaying == true;
    public double PlaybackLevel => _previewPlayer?.Level ?? 0;
    private string? _currentlyPlayingPath;

    public LibraryPanel()
    {
        InitializeComponent();
        _previewTimer.Tick += PreviewTimer_Tick;
    }

    /// <summary>New recordings are saved in the chosen library root.</summary>
    public string CurrentFolder => RootPath;

    public void Initialize(string rootPath)
    {
        RootPath = rootPath;
        Directory.CreateDirectory(rootPath);

        RefreshList();
    }

    public void Refresh() => RefreshList();

    public void Shutdown() => StopPreviewInternal();

    private void RefreshList()
    {

        string search = SearchBox.Text?.Trim() ?? "";
        bool searching = !string.IsNullOrEmpty(search);

        var items = LibraryManager.ListRecordings(RootPath, RootPath, recursive: true);
        if (searching)
            items = items.Where(i => i.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        RecordingsGrid.ItemsSource = items;
        UpdateSelectionStatus();
    }

    private static string CountLabel(int n)
    {
        if (n == 1) return "1 nahrávka";
        if (n is >= 2 and <= 4) return $"{n} nahrávky";
        return $"{n} nahrávek";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private Window? OwnerWindow => Window.GetWindow(this);

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string parent = RootPath;
        var dlg = new PromptDialog("Nová složka", "Název nové složky:") { Owner = OwnerWindow };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            Directory.CreateDirectory(Path.Combine(parent, dlg.ResultText));
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow, $"Složku nelze vytvořit: {ex.Message}", "MicRec", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshList();
    }

    private static RecordingItem? ItemFromSender(object sender) => (sender as FrameworkElement)?.Tag as RecordingItem;

    private void SelectInGrid(string path)
    {
        var item = RecordingsGrid.Items.Cast<RecordingItem>().FirstOrDefault(i => i.FullPath == path);
        if (item == null) return;
        RecordingsGrid.SelectedItem = item;
        RecordingsGrid.ScrollIntoView(item);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        var item = ItemFromSender(sender);
        if (item != null)
            PlayFile(item.FullPath, item.FileName);
    }

    private void RecordingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? origin = e.OriginalSource as DependencyObject;
        while (origin != null && origin is not DataGridRow)
        {
            if (origin is Button) return;
            origin = origin is Visual ? VisualTreeHelper.GetParent(origin) : LogicalTreeHelper.GetParent(origin);
        }
        if (origin is DataGridRow row && row.Item is RecordingItem item)
            PlayFile(item.FullPath, item.FileName);
    }

    private void PlayFile(string path, string displayName)
    {
        bool keepWaveform = path == _currentlyPlayingPath && PreviewTimeline.Data != null;
        StopPreviewInternal(!keepWaveform);
        try
        {
            _previewPlayer = new PreviewPlayback(path);
            _previewPlayer.Ended += PreviewPlayer_Ended;
            _currentlyPlayingPath = path;
            NowPlayingText.Text = displayName;
            PreviewPanel.Visibility = Visibility.Visible;
            PreviewTimeline.Duration = _previewPlayer.Duration.TotalSeconds;
            PreviewTimeline.Position = 0;
            PreviewPlayPauseButton.Content = "Ⅱ  Pauza";
            PlaybackStateText.Text = "Přehrávání";
            StopPreviewButton.IsEnabled = true;
            if (!keepWaveform)
            {
                PreviewTimeline.Data = null;
                PreviewTimeline.Placeholder = "Načítám zvukovou vlnu…";
                _waveformTask = LoadWaveformAsync(path);
            }
            _previewPlayer.Play();
            _previewTimer.Start();
            UpdatePlaybackTime();
        }
        catch (Exception ex)
        {
            StopPreviewInternal();
            MessageBox.Show(OwnerWindow, $"Soubor nelze přehrát: {ex.Message}", "MicRec", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadWaveformAsync(string path)
    {
        _waveformCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _waveformCancellation = cancellation;
        try
        {
            await _waveformGate.WaitAsync(cancellation.Token);
            WaveformData data;
            try { data = await Task.Run(() => WaveformData.Read(path, cancellation.Token), cancellation.Token); }
            finally { _waveformGate.Release(); }
            if (!cancellation.IsCancellationRequested && _currentlyPlayingPath == path)
                PreviewTimeline.Data = data;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && _currentlyPlayingPath == path)
            {
                PreviewTimeline.Placeholder = "Křivku nelze načíst. Časovou osu lze dál používat.";
                PreviewTimeline.ToolTip = ex.Message;
                PreviewTimeline.InvalidateVisual();
            }
        }
        finally
        {
            if (ReferenceEquals(_waveformCancellation, cancellation)) _waveformCancellation = null;
            cancellation.Dispose();
        }
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_previewPlayer != null) PreviewTimeline.Position = _previewPlayer.Position.TotalSeconds;
            PlaybackEye.SetSignal(PlaybackLevel, IsPreviewPlaying, _previewPlayer?.IsPaused == true);
            UpdatePlaybackTime();
            if (_previewPlayer == null && PlaybackEye.Energy < .001) _previewTimer.Stop();
        }
        catch (Exception ex) { PreviewFailure(ex); }
    }

    private void UpdatePlaybackTime() => PlaybackTimeText.Text =
        $"{WaveformTimeline.FormatTime(PreviewTimeline.Position)} / {WaveformTimeline.FormatTime(PreviewTimeline.Duration)}";

    private void PreviewPlayer_Ended(object? sender, StoppedEventArgs e)
    {
        var player = sender as PreviewPlayback;
        int generation = player?.Generation ?? -1;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(player, _previewPlayer) || player?.Generation != generation) return;
            if (e.Exception != null) { PreviewFailure(e.Exception); return; }
            ReleasePlayer();
            PreviewTimeline.Position = PreviewTimeline.Duration;
            PreviewPlayPauseButton.Content = "▶  Přehrát znovu";
            PlaybackStateText.Text = "Dohráno";
            StopPreviewButton.IsEnabled = false;
            UpdatePlaybackTime();
        }));
    }

    private void PreviewPlayPause_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_previewPlayer == null)
            {
                if (_currentlyPlayingPath != null) PlayFile(_currentlyPlayingPath, Path.GetFileName(_currentlyPlayingPath));
            }
            else if (_previewPlayer.IsPlaying)
            {
                _previewPlayer.Pause();
                PreviewPlayPauseButton.Content = "▶  Pokračovat";
                PlaybackStateText.Text = "Pozastaveno";
            }
            else
            {
                if (_previewPlayer.Position >= _previewPlayer.Duration) _previewPlayer.Seek(TimeSpan.Zero);
                _previewPlayer.Play();
                PreviewPlayPauseButton.Content = "Ⅱ  Pauza";
                PlaybackStateText.Text = "Přehrávání";
            }
        }
        catch (Exception ex) { PreviewFailure(ex); }
    }

    private void PreviewTimeline_SeekRequested(object? sender, double seconds)
    {
        if (_currentlyPlayingPath == null) return;
        try
        {
            if (_previewPlayer == null)
            {
                _previewPlayer = new PreviewPlayback(_currentlyPlayingPath);
                _previewPlayer.Ended += PreviewPlayer_Ended;
                PreviewPlayPauseButton.Content = "▶  Přehrát";
                PlaybackStateText.Text = "Připraveno";
            }
            _previewPlayer.Seek(TimeSpan.FromSeconds(seconds));
            PreviewTimeline.Position = _previewPlayer.Position.TotalSeconds;
            StopPreviewButton.IsEnabled = true;
            _previewTimer.Start();
            UpdatePlaybackTime();
        }
        catch (Exception ex) { PreviewFailure(ex); }
    }

    private void PreviewFailure(Exception ex)
    {
        StopPreviewInternal();
        StatusText.Text = $"Přehrávání se nezdařilo: {ex.Message}";
    }

    private void PreviewTimeline_SelectionChanged(object? sender, EventArgs e)
    {
        var start = PreviewTimeline.SelectionStart;
        var end = PreviewTimeline.SelectionEnd;
        bool has = start.HasValue && end.HasValue && end.Value > start.Value;
        CutSelectionButton.IsEnabled = has && _currentlyPlayingPath != null;
        CutSelectionButton.Content = has
            ? $"✂  Vystřihnout ({WaveformTimeline.FormatTime(end!.Value - start!.Value)})"
            : "✂  Vystřihnout";
    }

    private async void CutSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_currentlyPlayingPath == null) return;
        var start = PreviewTimeline.SelectionStart;
        var end = PreviewTimeline.SelectionEnd;
        if (start is not double s || end is not double en || en <= s) return;

        string source = _currentlyPlayingPath;
        CutSelectionButton.IsEnabled = false;
        StatusText.Text = "Stříhám…";
        try
        {
            string newPath = await Task.Run(() => AudioCutter.CutRange(source, s, en));
            RefreshList();
            SelectInGrid(newPath);
            StatusText.Text = $"Vystřiženo do nové nahrávky: {Path.GetFileName(newPath)}";
            PlayFile(newPath, Path.GetFileName(newPath)); // switch the preview to the result so the change is obvious
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow, $"Střih se nezdařil: {ex.Message}", "MicRec", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateSelectionStatus();
        }
    }

    private void StopPreview_Click(object sender, RoutedEventArgs e) => StopPreviewInternal(false);

    private void ReleasePlayer()
    {
        var player = _previewPlayer;
        _previewPlayer = null;
        if (player == null) return;
        player.Ended -= PreviewPlayer_Ended;
        player.Dispose();
    }

    private void StopPreviewInternal(bool clear = true)
    {
        ReleasePlayer();
        PreviewTimeline.Position = 0;
        PreviewPlayPauseButton.Content = "▶  Přehrát";
        PlaybackStateText.Text = "Zastaveno";
        StopPreviewButton.IsEnabled = false;
        if (clear)
        {
            _waveformCancellation?.Cancel();
            _waveformCancellation = null;
            _previewTimer.Stop();
            _currentlyPlayingPath = null;
            NowPlayingText.Text = "";
            PreviewPanel.Visibility = Visibility.Collapsed;
            PreviewTimeline.Data = null;
            PreviewTimeline.Duration = 0;
        }
        UpdatePlaybackTime();
    }

    private void RecordingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionStatus();

    private void UpdateSelectionStatus()
    {
        if (StatusText == null) return;
        int selected = RecordingsGrid.SelectedItems.Count;
        StatusText.Text = CountLabel(RecordingsGrid.Items.Count) + (selected > 0 ? $"  ·  Vybráno: {selected}" : "");
    }

    private void RecordingsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node != null && node is not DataGridRow) node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        if (node is DataGridRow row && !row.IsSelected)
        {
            RecordingsGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private List<RecordingItem> ItemsFromSelection(object sender)
    {
        var clicked = ItemFromSender(sender);
        if (clicked == null) return new();
        return RecordingsGrid.SelectedItems.Contains(clicked)
            ? RecordingsGrid.SelectedItems.Cast<RecordingItem>().ToList() : new() { clicked };
    }
    private void RecordingContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var first = (MenuItem)menu.Items[0];
        int count = ItemsFromSelection(first).Count;
        first.Header = count > 1 ? "Přehrát tuto nahrávku" : "Přehrát";
        ((MenuItem)menu.Items[1]).Header = count > 1 ? $"Převést vybrané ({count})…" : "Převést na…";
        ((MenuItem)menu.Items[2]).IsEnabled = count <= 1;
        ((MenuItem)menu.Items[3]).Header = count > 1 ? $"Přesunout vybrané ({count})…" : "Přesunout do…";
        ((MenuItem)menu.Items[5]).Header = count > 1 ? $"Smazat vybrané ({count})" : "Smazat";
    }

    private async Task StopPreviewForFileAsync(string path)
    {
        if (!string.Equals(path, _currentlyPlayingPath, StringComparison.OrdinalIgnoreCase)) return;
        var pending = _waveformTask;
        StopPreviewInternal();
        await pending; // Wait for the background decoder to release the file before moving/deleting it.
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var items = ItemsFromSelection(sender);
        if (items.Count == 0) return;
        var dlg = new ConvertDialog(items.Count == 1 ? items[0].FileName : $"{items.Count} vybraných nahrávek") { Owner = OwnerWindow };
        if (dlg.ShowDialog() != true) return;
        var errors = new List<string>();
        foreach (var item in items)
        {
            string destPath = Path.ChangeExtension(item.FullPath, AudioConverter.GetExtension(dlg.SelectedFormat));
            if (string.Equals(destPath, item.FullPath, StringComparison.OrdinalIgnoreCase)) continue;
            StatusText.Text = $"Převádím {item.FileName}…";
            try
            {
                if (File.Exists(destPath)) throw new IOException("Cílový soubor již existuje; nebyl přepsán.");
                await Task.Run(() => AudioConverter.Convert(item.FullPath, destPath, dlg.SelectedFormat));
            }
            catch (Exception ex) { errors.Add($"{item.FileName}: {ex.Message}"); }
        }
        RefreshList();
        ReportBatchErrors("Některé nahrávky se nepodařilo převést", errors);
    }
    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var item = ItemFromSender(sender);
        if (item == null)
            return;

        string baseName = Path.GetFileNameWithoutExtension(item.FileName);
        var dlg = new PromptDialog("Přejmenovat nahrávku", "Nový název (bez přípony):", baseName) { Owner = OwnerWindow };
        if (dlg.ShowDialog() != true)
            return;

        string newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, dlg.ResultText + Path.GetExtension(item.FileName));
        try
        {
            await StopPreviewForFileAsync(item.FullPath);
            File.Move(item.FullPath, newPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow, $"Přejmenování selhalo: {ex.Message}", "MicRec", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshList();
    }

    private async void Move_Click(object sender, RoutedEventArgs e)
    {
        var items = ItemsFromSelection(sender);
        if (items.Count == 0) return;
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = items.Count > 1 ? $"Vyberte cílovou složku pro {items.Count} nahrávek" : "Vyberte cílovou složku",
            UseDescriptionForTitle = true,
            SelectedPath = RootPath,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var errors = new List<string>();
        foreach (var item in items)
        {
            try
            {
                string destination = Path.Combine(dlg.SelectedPath, item.FileName);
                if (string.Equals(destination, item.FullPath, StringComparison.OrdinalIgnoreCase)) continue;
                await StopPreviewForFileAsync(item.FullPath);
                File.Move(item.FullPath, destination); // Never overwrite an existing recording.
            }
            catch (Exception ex) { errors.Add($"{item.FileName}: {ex.Message}"); }
        }
        RefreshList();
        ReportBatchErrors("Některé nahrávky se nepodařilo přesunout", errors);
    }

    private void ReportBatchErrors(string title, List<string> errors)
    {
        if (errors.Count == 0) return;
        string details = string.Join(Environment.NewLine, errors.Take(8));
        if (errors.Count > 8) details += $"\n… a dalších {errors.Count - 8}.";
        MessageBox.Show(OwnerWindow, details, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        var item = ItemFromSender(sender);
        if (item == null)
            return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
        catch { /* best effort */ }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var items = ItemsFromSelection(sender);
        if (items.Count == 0) return;
        string prompt = items.Count == 1 ? $"Opravdu smazat nahrávku „{items[0].FileName}“?"
            : $"Opravdu smazat {items.Count} vybraných nahrávek?\n\n" + string.Join("\n", items.Take(6).Select(i => i.FileName))
              + (items.Count > 6 ? $"\n… a dalších {items.Count - 6}." : "");
        if (MessageBox.Show(OwnerWindow, prompt, "MicRec", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var errors = new List<string>();
        foreach (var item in items)
        {
            try
            {
                await StopPreviewForFileAsync(item.FullPath);
                File.Delete(item.FullPath);
            }
            catch (Exception ex) { errors.Add($"{item.FileName}: {ex.Message}"); }
        }
        RefreshList();
        ReportBatchErrors("Některé nahrávky se nepodařilo smazat", errors);
    }
}



