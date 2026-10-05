using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicRec.Audio;

public sealed record RecordingQuality(string Name, int SampleRate, int BitsPerSample)
{
    public override string ToString() => Name;
}

public sealed class MeterSnapshot
{
    public float PeakL, PeakR, RmsL, RmsR;
    public float[] Bands = Array.Empty<float>();
    public double GainDb;
}

/// <summary>
/// Captures the microphone via WASAPI, runs it through the limiter/AGC, feeds the
/// spectrum analyzer for the on-screen equalizer, and writes a broadcast-quality PCM WAV.
/// Pausing stops writing to the file (and to the time counter) but keeps the meters live.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public static readonly RecordingQuality[] Qualities =
    {
        new("48 kHz / 24 bit · Doporučeno", 48000, 24),
        new("48 kHz / 16 bit · PCM", 48000, 16),
        new("44,1 kHz / 16 bit · CD", 44100, 16),
    };

    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffered;
    private ISampleProvider? _resampled;
    private WaveFileWriter? _writer;
    private readonly Limiter _limiter = new();
    private SpectrumAnalyzer? _spectrum;

    private float[] _procBuffer = new float[8192];
    private byte[] _pcmBuffer = Array.Empty<byte>();

    private volatile bool _isRecording;
    private volatile bool _isPaused;

    private readonly object _meterLock = new();
    private double _peakL, _peakR, _rmsL, _rmsR;
    private float[] _lastBands = Array.Empty<float>();

    private long _framesWritten;
    private int _writerSampleRate = 48000;

    public bool LimiterEnabled { get => _limiter.Enabled; set => _limiter.Enabled = value; }
    public double TargetDb { get => _limiter.TargetDb; set => _limiter.TargetDb = value; }
    public bool IsRecording => _isRecording;
    public bool IsPaused => _isPaused;
    public string? CurrentFilePath { get; private set; }

    public TimeSpan Elapsed => TimeSpan.FromSeconds(_writerSampleRate > 0 ? (double)_framesWritten / _writerSampleRate : 0);

    public static List<MMDevice> GetInputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return new List<MMDevice>(enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active));
    }

    public static MMDevice? GetDefaultInputDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch { return null; }
    }

    public void Start(MMDevice device, string filePath, RecordingQuality quality)
    {
        Stop();

        _capture = new WasapiCapture(device, false, 50);
        var captureFormat = _capture.WaveFormat;

        var pushFormat = WaveFormat.CreateIeeeFloatWaveFormat(captureFormat.SampleRate, 2);
        _buffered = new BufferedWaveProvider(pushFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
            ReadFully = false,
        };

        ISampleProvider sampleProvider = _buffered.ToSampleProvider();
        if (captureFormat.SampleRate != quality.SampleRate)
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, quality.SampleRate);
        _resampled = sampleProvider;

        _limiter.Configure(quality.SampleRate);
        _spectrum = new SpectrumAnalyzer(quality.SampleRate);
        _writerSampleRate = quality.SampleRate;
        _framesWritten = 0;

        var pcmFormat = new WaveFormat(quality.SampleRate, quality.BitsPerSample, 2);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _writer = new WaveFileWriter(filePath, pcmFormat);
        CurrentFilePath = filePath;

        _capture.DataAvailable += OnDataAvailable;
        _isRecording = true;
        _isPaused = false;
        _capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture == null || _buffered == null || _resampled == null)
            return;

        var captureFormat = _capture.WaveFormat;
        int srcChannels = Math.Max(1, captureFormat.Channels);
        bool isFloat = captureFormat.Encoding == WaveFormatEncoding.IeeeFloat && captureFormat.BitsPerSample == 32;
        bool isPcm16 = captureFormat.Encoding == WaveFormatEncoding.Pcm && captureFormat.BitsPerSample == 16;
        int bytesPerSample = captureFormat.BitsPerSample / 8;
        if (bytesPerSample <= 0)
            return;

        int frames = e.BytesRecorded / (bytesPerSample * srcChannels);
        if (frames <= 0)
            return;

        var stereo = new float[frames * 2];
        int bi = 0;
        for (int f = 0; f < frames; f++)
        {
            float l, r;
            if (isFloat)
            {
                l = BitConverter.ToSingle(e.Buffer, bi);
                r = srcChannels >= 2 ? BitConverter.ToSingle(e.Buffer, bi + 4) : l;
            }
            else if (isPcm16)
            {
                l = BitConverter.ToInt16(e.Buffer, bi) / 32768f;
                r = srcChannels >= 2 ? BitConverter.ToInt16(e.Buffer, bi + 2) / 32768f : l;
            }
            else
            {
                l = r = 0f;
            }
            stereo[f * 2] = l;
            stereo[f * 2 + 1] = r;
            bi += bytesPerSample * srcChannels;
        }

        var stereoBytes = new byte[stereo.Length * 4];
        Buffer.BlockCopy(stereo, 0, stereoBytes, 0, stereoBytes.Length);
        _buffered.AddSamples(stereoBytes, 0, stereoBytes.Length);

        int safety = 0;
        int totalRead;
        do
        {
            totalRead = _resampled.Read(_procBuffer, 0, _procBuffer.Length);
            if (totalRead > 0)
                ProcessChunk(_procBuffer, totalRead);
            safety++;
        } while (totalRead > 0 && safety < 20);
    }

    private void ProcessChunk(float[] buffer, int floatCount)
    {
        int frameCount = floatCount / 2;
        if (frameCount <= 0)
            return;

        _limiter.Process(buffer, frameCount);

        double peakL = 0, peakR = 0, sumSqL = 0, sumSqR = 0;
        var mono = new float[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            float l = buffer[f * 2];
            float r = buffer[f * 2 + 1];
            peakL = Math.Max(peakL, Math.Abs(l));
            peakR = Math.Max(peakR, Math.Abs(r));
            sumSqL += (double)l * l;
            sumSqR += (double)r * r;
            mono[f] = (l + r) * 0.5f;
        }
        double rmsL = Math.Sqrt(sumSqL / frameCount);
        double rmsR = Math.Sqrt(sumSqR / frameCount);

        _spectrum?.Push(mono);
        var bands = _spectrum?.ComputeBands() ?? Array.Empty<float>();

        lock (_meterLock)
        {
            _peakL = peakL; _peakR = peakR; _rmsL = rmsL; _rmsR = rmsR;
            _lastBands = bands;
        }

        if (_isRecording && !_isPaused && _writer != null)
            WriteToFile(buffer, frameCount);
    }

    private void WriteToFile(float[] buffer, int frameCount)
    {
        int bits = _writer!.WaveFormat.BitsPerSample;
        int bytesPerSample = bits / 8;
        int needed = frameCount * 2 * bytesPerSample;
        if (_pcmBuffer.Length < needed)
            _pcmBuffer = new byte[needed];

        int idx = 0;
        for (int f = 0; f < frameCount; f++)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                float s = Math.Clamp(buffer[f * 2 + ch], -1f, 1f);
                if (bits == 16)
                {
                    short v = (short)(s * short.MaxValue);
                    _pcmBuffer[idx++] = (byte)(v & 0xFF);
                    _pcmBuffer[idx++] = (byte)((v >> 8) & 0xFF);
                }
                else
                {
                    int v = (int)(s * 8388607f);
                    _pcmBuffer[idx++] = (byte)(v & 0xFF);
                    _pcmBuffer[idx++] = (byte)((v >> 8) & 0xFF);
                    _pcmBuffer[idx++] = (byte)((v >> 16) & 0xFF);
                }
            }
        }
        _writer.Write(_pcmBuffer, 0, idx);
        _framesWritten += frameCount;
    }

    public void Pause() => _isPaused = true;
    public void Resume() => _isPaused = false;

    public void Stop()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); } catch { /* device may already be gone */ }
            _capture.Dispose();
            _capture = null;
        }
        _writer?.Dispose();
        _writer = null;
        _buffered = null;
        _resampled = null;
        _spectrum = null;
        _isRecording = false;
        _isPaused = false;
        CurrentFilePath = null;
    }

    public MeterSnapshot GetSnapshot()
    {
        lock (_meterLock)
        {
            return new MeterSnapshot
            {
                PeakL = (float)_peakL,
                PeakR = (float)_peakR,
                RmsL = (float)_rmsL,
                RmsR = (float)_rmsR,
                Bands = _lastBands,
                GainDb = _limiter.CurrentGainDb,
            };
        }
    }

    public void Dispose() => Stop();
}

