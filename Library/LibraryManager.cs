using System.IO;
using NAudio.Wave;

namespace MicRec.Library;

public static class LibraryManager
{
    private static readonly string[] SupportedExtensions = { ".wav", ".mp3", ".wma" };

    public static List<RecordingItem> ListRecordings(string rootPath, string folderPath, bool recursive)
    {
        var result = new List<RecordingItem>();
        if (!Directory.Exists(folderPath))
            return result;

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folderPath, "*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        catch { return result; }

        foreach (var file in files)
        {
            TimeSpan duration = TimeSpan.Zero;
            try
            {
                // AudioFileReader picks the right decoder (WAV/MP3/WMA) from the file extension.
                using var reader = new AudioFileReader(file);
                duration = reader.TotalTime;
            }
            catch { /* file may still be recording / locked */ }

            var info = new FileInfo(file);
            string dir = Path.GetDirectoryName(file) ?? rootPath;
            string relative = Path.GetRelativePath(rootPath, dir);
            if (relative == ".") relative = "";

            result.Add(new RecordingItem
            {
                FullPath = file,
                Created = info.LastWriteTime,
                Duration = duration,
                SizeBytes = info.Length,
                RelativeFolder = relative,
            });
        }

        return result.OrderByDescending(r => r.Created).ToList();
    }
}
