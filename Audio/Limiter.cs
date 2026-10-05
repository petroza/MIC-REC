namespace MicRec.Audio;

/// <summary>
/// Real-time automatic gain control + soft-knee limiter.
/// Continuously nudges the program level toward <see cref="TargetDb"/> and guarantees
/// the output never exceeds <see cref="CeilingDb"/>, even when the AGC overshoots.
/// Operates in-place on interleaved stereo float buffers.
/// </summary>
public sealed class Limiter
{
    public bool Enabled { get; set; } = true;
    public double TargetDb { get; set; } = -18.0;
    public double CeilingDb { get; set; } = -1.0;
    public double AttackMs { get; set; } = 30;
    public double ReleaseMs { get; set; } = 300;
    public double MaxGainDb { get; set; } = 24;
    public double MinGainDb { get; set; } = -24;

    private double _envelopeDb = -60;
    private double _gainDb;
    private int _sampleRate = 48000;

    public double CurrentGainDb => _gainDb;

    public void Configure(int sampleRate)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        _envelopeDb = -60;
        _gainDb = 0;
    }

    /// <summary>buffer holds frameCount stereo frames (2 floats each), processed in-place.</summary>
    public void Process(float[] buffer, int frameCount)
    {
        double ceilingLinear = Math.Pow(10, CeilingDb / 20.0);

        if (!Enabled)
        {
            for (int f = 0; f < frameCount; f++)
            {
                buffer[f * 2] = (float)SoftClip(buffer[f * 2], ceilingLinear);
                buffer[f * 2 + 1] = (float)SoftClip(buffer[f * 2 + 1], ceilingLinear);
            }
            return;
        }

        double attackCoef = Math.Exp(-1.0 / (0.001 * AttackMs * _sampleRate));
        double releaseCoef = Math.Exp(-1.0 / (0.001 * ReleaseMs * _sampleRate));

        for (int f = 0; f < frameCount; f++)
        {
            int li = f * 2, ri = f * 2 + 1;
            float l = buffer[li], r = buffer[ri];

            double drive = Math.Max(Math.Abs(l), Math.Abs(r));
            double instDb = 20 * Math.Log10(drive + 1e-9);

            if (instDb > _envelopeDb)
                _envelopeDb = attackCoef * _envelopeDb + (1 - attackCoef) * instDb;
            else
                _envelopeDb = releaseCoef * _envelopeDb + (1 - releaseCoef) * instDb;

            double desiredGainDb = Math.Clamp(TargetDb - _envelopeDb, MinGainDb, MaxGainDb);
            _gainDb += 0.05 * (desiredGainDb - _gainDb); // smooth to avoid zipper noise

            double gainLinear = Math.Pow(10, _gainDb / 20.0);

            buffer[li] = (float)SoftClip(l * gainLinear, ceilingLinear);
            buffer[ri] = (float)SoftClip(r * gainLinear, ceilingLinear);
        }
    }

    private static double SoftClip(double x, double ceiling)
    {
        double sign = x < 0 ? -1 : 1;
        double ax = Math.Abs(x);
        double knee = ceiling * 0.9;
        if (ax <= knee)
            return x;

        double over = (ax - knee) / (ceiling * 0.5);
        double compressed = knee + ceiling * 0.1 * Math.Tanh(over);
        return sign * Math.Min(compressed, ceiling);
    }
}
