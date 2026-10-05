using System.IO;

namespace MicRec.Library;

public sealed class RecordingItem
{
    public required string FullPath { get; init; }
    public required DateTime Created { get; init; }
    public required TimeSpan Duration { get; init; }
    public required long SizeBytes { get; init; }
    public required string RelativeFolder { get; init; }

    public string FileName => Path.GetFileName(FullPath);

    public string CreatedDisplay => Created.ToString("dd.MM.yyyy HH:mm:ss");

    public string DurationDisplay => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    public string SizeDisplay => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024.0 / 1024.0:0.0} MB"
        : $"{SizeBytes / 1024.0:0} KB";
}
