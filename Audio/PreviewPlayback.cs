using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicRec.Audio;

/// <summary>UI-thread transport with synchronized decoding and metering on the audio thread.</summary>
public sealed class PreviewPlayback : IDisposable
{
    private readonly MeteredSource _source;
    private IWavePlayer? _output;
    private readonly Func<IWavePlayer> _outputFactory;
    private bool _paused;
    private TimeSpan _origin;
    public int Generation { get; private set; }
    public event EventHandler<StoppedEventArgs>? Ended;
    public TimeSpan Duration => _source.Duration;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output != null && _paused;
    public double Level => IsPlaying ? _source.Level : 0;
    public TimeSpan Position => TimeSpan.FromSeconds(Math.Clamp(_origin.TotalSeconds +
        ((_output as IWavePosition)?.GetPosition() ?? 0) / (double)(_output?.OutputWaveFormat.AverageBytesPerSecond ?? _source.WaveFormat.AverageBytesPerSecond), 0, Duration.TotalSeconds));

    public PreviewPlayback(string path, Func<IWavePlayer>? outputFactory = null)
    {
        _outputFactory = outputFactory ?? (() => new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 2 });
        _source = new MeteredSource(path);
        try { CreateOutput(); }
        catch { _source.Dispose(); throw; }
    }

    private void CreateOutput()
    {
        var output = _outputFactory();
        try { output.Init(new SampleToWaveProvider(_source)); }
        catch { output.Dispose(); throw; }
        _output = output;
        Generation++;
        output.PlaybackStopped += OutputStopped;
    }

    private void OutputStopped(object? sender, StoppedEventArgs e)
    {
        // Ignore completion callbacks from an output replaced by seeking or disposal.
        if (ReferenceEquals(sender, _output)) Ended?.Invoke(this, e);
    }

    public void Play() { _output?.Play(); _paused = false; }
    public void Pause() { _output?.Pause(); _paused = true; }

    public void Seek(TimeSpan position)
    {
        bool resume = IsPlaying;
        ReleaseOutput(); // Flush queued samples so the old sound cannot play after seeking.
        _origin = _source.Seek(position);
        CreateOutput();
        if (resume) Play();
    }

    private void ReleaseOutput()
    {
        var output = _output;
        _output = null;
        if (output == null) return;
        output.PlaybackStopped -= OutputStopped;
        output.Dispose();
    }

    public void Dispose() { ReleaseOutput(); _source.Dispose(); }

    private sealed class MeteredSource : ISampleProvider, IDisposable
    {
        private readonly AudioFileReader _reader;
        private readonly object _sync = new();
        private bool _disposed;
        private float _level;
        public WaveFormat WaveFormat { get; }
        public TimeSpan Duration { get; }
        public double Level => Volatile.Read(ref _level);
        public MeteredSource(string path)
        {
            _reader = new AudioFileReader(path);
            WaveFormat = _reader.WaveFormat;
            Duration = _reader.TotalTime;
        }
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                if (_disposed) return 0;
                int read = _reader.Read(buffer, offset, count);
                double sum = 0;
                for (int i = offset; i < offset + read; i++) sum += buffer[i] * buffer[i];
                Volatile.Write(ref _level, read == 0 ? 0 : (float)Math.Sqrt(sum / read));
                return read;
            }
        }
        public TimeSpan Seek(TimeSpan position)
        {
            lock (_sync)
            {
                _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds));
                Volatile.Write(ref _level, 0);
                return _reader.CurrentTime;
            }
        }
        public void Dispose()
        {
            lock (_sync) { if (_disposed) return; _disposed = true; _reader.Dispose(); }
        }
    }
}


