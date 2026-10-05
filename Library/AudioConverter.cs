using NAudio.Lame;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MicRec.Library;

public enum ConvertFormat
{
    Mp3_128,
    Mp3_192,
    Mp3_320,
    Wma,
}

public static class AudioConverter
{
    public static readonly ConvertFormat[] AllFormats =
    {
        ConvertFormat.Mp3_128,
        ConvertFormat.Mp3_192,
        ConvertFormat.Mp3_320,
        ConvertFormat.Wma,
    };

    public static string GetExtension(ConvertFormat format) =>
        format == ConvertFormat.Wma ? ".wma" : ".mp3";

    public static string GetLabel(ConvertFormat format) => format switch
    {
        ConvertFormat.Mp3_128 => "MP3 – 128 kb/s",
        ConvertFormat.Mp3_192 => "MP3 – 192 kb/s (doporučeno)",
        ConvertFormat.Mp3_320 => "MP3 – 320 kb/s (nejvyšší kvalita)",
        ConvertFormat.Wma => "WMA (Windows Media Audio)",
        _ => format.ToString(),
    };

    /// <summary>Converts a WAV recording to the chosen format. Runs synchronously — call from a background thread.</summary>
    public static void Convert(string sourceWavPath, string destinationPath, ConvertFormat format)
    {
        switch (format)
        {
            case ConvertFormat.Mp3_128:
                ConvertToMp3(sourceWavPath, destinationPath, 128);
                break;
            case ConvertFormat.Mp3_192:
                ConvertToMp3(sourceWavPath, destinationPath, 192);
                break;
            case ConvertFormat.Mp3_320:
                ConvertToMp3(sourceWavPath, destinationPath, 320);
                break;
            case ConvertFormat.Wma:
                ConvertToWma(sourceWavPath, destinationPath);
                break;
        }
    }

    private static void ConvertToMp3(string sourceWavPath, string destinationPath, int bitRateKbps)
    {
        using var reader = new AudioFileReader(sourceWavPath);
        using var writer = new LameMP3FileWriter(destinationPath, reader.WaveFormat, bitRateKbps);
        reader.CopyTo(writer);
    }

    private static bool _mfStarted;

    private static void ConvertToWma(string sourceWavPath, string destinationPath)
    {
        if (!_mfStarted)
        {
            MediaFoundationApi.Startup();
            _mfStarted = true;
        }
        using var reader = new AudioFileReader(sourceWavPath);
        MediaFoundationEncoder.EncodeToWma(reader, destinationPath);
    }
}
