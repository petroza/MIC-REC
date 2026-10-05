using MicRec.Dsp;

namespace MicRec.Audio;

/// <summary>
/// Keeps a rolling window of mono samples and produces a log-spaced band spectrum,
/// used to drive the equalizer-style level display.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private readonly int _fftSize;
    private readonly double[] _real;
    private readonly double[] _imag;
    private readonly double[] _window;
    private readonly float[] _ring;
    private int _ringPos;
    private readonly object _lock = new();

    private readonly int[] _bandStart;
    private readonly int[] _bandEnd;

    public int BandCount { get; }

    public SpectrumAnalyzer(int sampleRate, int fftSize = 1024, int bandCount = 20)
    {
        _fftSize = fftSize;
        _real = new double[fftSize];
        _imag = new double[fftSize];
        _window = new double[fftSize];
        for (int i = 0; i < fftSize; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (fftSize - 1));

        _ring = new float[fftSize];
        BandCount = bandCount;
        _bandStart = new int[bandCount];
        _bandEnd = new int[bandCount];

        double minFreq = 60;
        double maxFreq = Math.Min(16000, sampleRate / 2.0);
        double logMin = Math.Log10(minFreq);
        double logMax = Math.Log10(maxFreq);
        int halfBins = fftSize / 2;

        for (int b = 0; b < bandCount; b++)
        {
            double f0 = Math.Pow(10, logMin + (logMax - logMin) * b / bandCount);
            double f1 = Math.Pow(10, logMin + (logMax - logMin) * (b + 1) / bandCount);
            int bin0 = Math.Max(1, (int)(f0 / sampleRate * fftSize));
            int bin1 = Math.Max(bin0 + 1, (int)(f1 / sampleRate * fftSize));
            _bandStart[b] = Math.Min(bin0, halfBins - 1);
            _bandEnd[b] = Math.Min(bin1, halfBins);
        }
    }

    public void Push(ReadOnlySpan<float> monoSamples)
    {
        lock (_lock)
        {
            foreach (var s in monoSamples)
            {
                _ring[_ringPos] = s;
                _ringPos = (_ringPos + 1) % _fftSize;
            }
        }
    }

    public float[] ComputeBands()
    {
        lock (_lock)
        {
            for (int i = 0; i < _fftSize; i++)
            {
                int idx = (_ringPos + i) % _fftSize;
                _real[i] = _ring[idx] * _window[i];
                _imag[i] = 0;
            }
        }

        Fft.Forward(_real, _imag);

        var bands = new float[BandCount];
        for (int b = 0; b < BandCount; b++)
        {
            double sum = 0;
            int count = 0;
            for (int k = _bandStart[b]; k < _bandEnd[b]; k++)
            {
                sum += Math.Sqrt(_real[k] * _real[k] + _imag[k] * _imag[k]);
                count++;
            }
            double avg = count > 0 ? sum / count : 0;
            double db = 20 * Math.Log10(avg / (_fftSize / 2.0) + 1e-9);
            double norm = (db + 60) / 60.0; // map -60..0 dB to 0..1
            bands[b] = (float)Math.Clamp(norm, 0, 1);
        }
        return bands;
    }
}
