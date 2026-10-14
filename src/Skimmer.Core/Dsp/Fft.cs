namespace Skimmer.Core.Dsp;

/// <summary>Απλός radix-2 FFT για το waterfall.</summary>
public static class Fft
{
    public static void Transform(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    float ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }

    /// <summary>Φάσμα ισχύος σε dB για 0..maxHz από πραγματικό σήμα με παράθυρο Hann.</summary>
    public static float[] PowerDb(ReadOnlySpan<float> x, int sampleRate, double maxHz)
    {
        int n = x.Length;
        var re = new float[n]; var im = new float[n];
        for (int i = 0; i < n; i++) re[i] = x[i] * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
        Transform(re, im);
        int bins = (int)(maxHz * n / sampleRate);
        var db = new float[bins];
        for (int i = 0; i < bins; i++) db[i] = 10f * MathF.Log10(re[i] * re[i] + im[i] * im[i] + 1e-12f);
        return db;
    }
}
