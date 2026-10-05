using System.IO;
using NAudio.Wave;

namespace MicRec.Library;

/// <summary>Removes a time range from an audio file and writes the remainder as a new 16-bit PCM WAV, leaving the original untouched.</summary>
public static class AudioCutter
{
    public static string CutRange(string sourcePath, double startSeconds, double endSeconds)
    {
        using var reader = new AudioFileReader(sourcePath);
        int channels = reader.WaveFormat.Channels;
        int sampleRate = reader.WaveFormat.SampleRate;
        long cutStartFrame = (long)(startSeconds * sampleRate);
        long cutEndFrame = (long)(endSeconds * sampleRate);

        string destPath = BuildDestPath(sourcePath);
        var format = new WaveFormat(sampleRate, 16, channels);

        try
        {
            using (var writer = new WaveFileWriter(destPath, format))
            {
                var buffer = new float[channels * 4096];
                var pcmBuffer = new byte[buffer.Length * 2];
                long frame = 0;
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    int frameCount = read / channels;
                    int idx = 0;
                    for (int f = 0; f < frameCount; f++)
                    {
                        long currentFrame = frame + f;
                        if (currentFrame >= cutStartFrame && currentFrame < cutEndFrame)
                            continue; // this is the part being cut out

                        for (int ch = 0; ch < channels; ch++)
                        {
                            float s = Math.Clamp(buffer[f * channels + ch], -1f, 1f);
                            short v = (short)(s * short.MaxValue);
                            pcmBuffer[idx++] = (byte)(v & 0xFF);
                            pcmBuffer[idx++] = (byte)((v >> 8) & 0xFF);
                        }
                    }
                    if (idx > 0)
                        writer.Write(pcmBuffer, 0, idx);
                    frame += frameCount;
                }

                if (writer.Length == 0)
                    throw new InvalidOperationException("Po vystřižení by nezůstal žádný zvuk.");
            }
        }
        catch
        {
            File.Delete(destPath);
            throw;
        }

        return destPath;
    }

    private static string BuildDestPath(string sourcePath)
    {
        string dir = Path.GetDirectoryName(sourcePath)!;
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string candidate = Path.Combine(dir, $"{baseName}_střih.wav");
        int i = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{baseName}_střih{i}.wav");
            i++;
        }
        return candidate;
    }
}
