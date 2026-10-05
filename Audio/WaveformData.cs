using NAudio.Wave;

namespace MicRec.Audio;

/// <summary>Bounded, per-channel peak envelope. Decodes on a worker, never on the UI thread.</summary>
public sealed record WaveformData(TimeSpan Duration, float[][] Min, float[][] Max)
{
    public static WaveformData Read(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new AudioFileReader(path);
        int channels = reader.WaveFormat.Channels;
        long frames = reader.Length / (sizeof(float) * channels);
        int bins = (int)Math.Clamp(frames, 1, 4096);
        int lanes = Math.Min(2, channels);
        var min = Enumerable.Range(0, lanes).Select(_ => new float[bins]).ToArray();
        var max = Enumerable.Range(0, lanes).Select(_ => new float[bins]).ToArray();
        var buffer = new float[4096 * channels];
        long frame = 0;
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int sample = 0; sample + channels <= count; sample += channels, frame++)
            {
                int bin = (int)Math.Min(bins - 1, frame * bins / Math.Max(1, frames));
                for (int lane = 0; lane < lanes; lane++)
                {
                    float value = float.IsFinite(buffer[sample + lane]) ? Math.Clamp(buffer[sample + lane], -1, 1) : 0;
                    min[lane][bin] = Math.Min(min[lane][bin], value);
                    max[lane][bin] = Math.Max(max[lane][bin], value);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new WaveformData(reader.TotalTime, min, max);
    }
}
