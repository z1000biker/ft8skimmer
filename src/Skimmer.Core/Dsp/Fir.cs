namespace Skimmer.Core.Dsp;

public static class FirDesign
{
    /// <summary>Χαμηλοπερατό windowed-sinc (Blackman), κέρδος DC = 1.</summary>
    public static float[] LowPass(int taps, double cutoffHz, double sampleRate)
    {
        var h = new float[taps];
        double fc = cutoffHz / sampleRate, m = taps - 1, sum = 0;
        var tmp = new double[taps];
        for (int n = 0; n < taps; n++)
        {
            double x = n - m / 2;
            double sinc = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
            double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / m) + 0.08 * Math.Cos(4 * Math.PI * n / m);
            tmp[n] = sinc * w;
            sum += tmp[n];
        }
        for (int n = 0; n < taps; n++) h[n] = (float)(tmp[n] / sum);
        return h;
    }
}

/// <summary>Μιγαδικό FIR με αποδεκατισμό (υπολογίζει μόνο τις εξόδους που κρατά).</summary>
public sealed class ComplexDecimator
{
    private readonly float[] _h;
    private readonly int _factor;
    private readonly float[] _bi, _bq; // διπλό κυκλικό buffer για συνεχή ανάγνωση
    private int _pos, _phase;

    public ComplexDecimator(float[] taps, int factor)
    {
        _h = taps; _factor = factor;
        _bi = new float[taps.Length * 2];
        _bq = new float[taps.Length * 2];
    }

    public void Reset()
    {
        Array.Clear(_bi); Array.Clear(_bq); _pos = 0; _phase = 0;
    }

    /// <summary>Επεξεργάζεται count δείγματα· γράφει εξόδους στο outI/outQ και επιστρέφει το πλήθος τους.</summary>
    public int Process(ReadOnlySpan<float> inI, ReadOnlySpan<float> inQ, Span<float> outI, Span<float> outQ)
    {
        int L = _h.Length, o = 0;
        for (int k = 0; k < inI.Length; k++)
        {
            _bi[_pos] = _bi[_pos + L] = inI[k];
            _bq[_pos] = _bq[_pos + L] = inQ[k];
            _pos = (_pos + 1) % L;
            if (++_phase < _factor) continue;
            _phase = 0;
            // το παλαιότερο δείγμα βρίσκεται στο _pos· τα συντελεστές είναι συμμετρικοί, άρα η σειρά δεν έχει σημασία
            var si = _bi.AsSpan(_pos, L);
            var sq = _bq.AsSpan(_pos, L);
            float ai = 0, aq = 0;
            for (int t = 0; t < L; t++) { ai += si[t] * _h[t]; aq += sq[t] * _h[t]; }
            outI[o] = ai; outQ[o] = aq; o++;
        }
        return o;
    }
}
