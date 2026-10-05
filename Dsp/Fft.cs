namespace MicRec.Dsp;

/// <summary>Iterative in-place Cooley-Tukey FFT. Length must be a power of two.</summary>
public static class Fft
{
    public static void Forward(double[] real, double[] imag)
    {
        int n = real.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double curWr = 1, curWi = 0;
                int half = len / 2;
                for (int k = 0; k < half; k++)
                {
                    double ur = real[i + k], ui = imag[i + k];
                    double vr = real[i + k + half] * curWr - imag[i + k + half] * curWi;
                    double vi = real[i + k + half] * curWi + imag[i + k + half] * curWr;

                    real[i + k] = ur + vr;
                    imag[i + k] = ui + vi;
                    real[i + k + half] = ur - vr;
                    imag[i + k + half] = ui - vi;

                    double nextWr = curWr * wr - curWi * wi;
                    double nextWi = curWr * wi + curWi * wr;
                    curWr = nextWr;
                    curWi = nextWi;
                }
            }
        }
    }
}
