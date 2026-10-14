namespace Skimmer.Core.Dsp;

/// <summary>
/// Από IQ 960 kS/s σε ήχο USB 12 kS/s (0–3 kHz), όπως τον θέλει ο αποκωδικοποιητής FT8.
/// Ο δέκτης συντονίζεται OffsetHz κάτω από τη συχνότητα dial ώστε να αποφεύγεται η αιχμή DC.
/// Αλυσίδα: μετατόπιση κατά −(Offset+1500) → ÷8 → ÷10 → οξύ χαμηλοπερατό ±1520 Hz → μετατόπιση +1500 → πραγματικό μέρος.
/// </summary>
public sealed class UsbDemodulator
{
    public const int AudioRate = 12_000;
    private readonly int _inRate;
    private readonly ComplexDecimator _d1, _d2, _d3;
    private double _ncoPhase, _ncoStep, _upPhase;
    private readonly double _upStep;
    private float[] _i = [], _q = [], _i1 = [], _q1 = [], _i2 = [], _q2 = [], _i3 = [], _q3 = [];

    public UsbDemodulator(int inputRate, double offsetHz)
    {
        if (inputRate != 960_000) throw new ArgumentException("Only 960000 S/s is supported.");
        _inRate = inputRate;
        _d1 = new ComplexDecimator(FirDesign.LowPass(64, 30_000, inputRate), 8);   // → 120 kS/s
        _d2 = new ComplexDecimator(FirDesign.LowPass(129, 2_600, 120_000), 10);     // → 12 kS/s
        _d3 = new ComplexDecimator(FirDesign.LowPass(255, 1_540, AudioRate), 1);    // οξύ φίλτρο πλευρικής
        SetOffset(offsetHz);
        _upStep = 2 * Math.PI * 1500.0 / AudioRate;
    }

    /// <summary>Αντιστροφή φάσματος (συζυγές IQ), για διαδρομές που αναστρέφουν το σήμα.</summary>
    public bool InvertSpectrum { get; set; }

    public void SetOffset(double offsetHz) => _ncoStep = -2 * Math.PI * (offsetHz + 1500.0) / _inRate;

    public void Reset()
    {
        _d1.Reset(); _d2.Reset(); _d3.Reset();
    }

    /// <summary>Επεξεργάζεται bytes IQ, προσθέτει δείγματα ήχου στη λίστα εξόδου.</summary>
    public void Process(ReadOnlySpan<byte> iq, List<float> audioOut)
    {
        int n = iq.Length / 2;
        Ensure(ref _i, n); Ensure(ref _q, n);
        Ensure(ref _i1, n / 8 + 2); Ensure(ref _q1, n / 8 + 2);
        Ensure(ref _i2, n / 80 + 2); Ensure(ref _q2, n / 80 + 2);
        Ensure(ref _i3, n / 80 + 2); Ensure(ref _q3, n / 80 + 2);

        double ph = _ncoPhase, st = _ncoStep;
        float qs = InvertSpectrum ? -1f / 128f : 1f / 128f;
        for (int k = 0; k < n; k++)
        {
            float x = (iq[2 * k] - 127.4f) * (1 / 128f);
            float y = (iq[2 * k + 1] - 127.4f) * qs;
            float c = (float)Math.Cos(ph), s = (float)Math.Sin(ph);
            _i[k] = x * c - y * s;
            _q[k] = x * s + y * c;
            ph += st;
            if (ph < -Math.PI) ph += 2 * Math.PI;
        }
        _ncoPhase = ph;

        int n1 = _d1.Process(_i.AsSpan(0, n), _q.AsSpan(0, n), _i1, _q1);
        int n2 = _d2.Process(_i1.AsSpan(0, n1), _q1.AsSpan(0, n1), _i2, _q2);
        int n3 = _d3.Process(_i2.AsSpan(0, n2), _q2.AsSpan(0, n2), _i3, _q3);

        for (int k = 0; k < n3; k++)
        {
            // πραγματικό μέρος του (I+jQ)·e^{+jωt}: μόνο οι θετικές συχνότητες 0..3 kHz
            audioOut.Add(_i3[k] * (float)Math.Cos(_upPhase) - _q3[k] * (float)Math.Sin(_upPhase));
            _upPhase += _upStep;
            if (_upPhase > Math.PI) _upPhase -= 2 * Math.PI;
        }
    }

    private static void Ensure(ref float[] a, int n) { if (a.Length < n) a = new float[n]; }
}
