namespace Skimmer.Core.Dsp;

/// <summary>
/// Γραμμές waterfall 0–3 kHz από τον ήχο 12 kS/s: 512 τιμές 0–255 ανά γραμμή, ~12 γραμμές/s,
/// με αυτόματη στάθμη θορύβου. Ανεξάρτητο από UI, για τον web διακομιστή.
/// </summary>
public sealed class SpectrumLines
{
    public const int Bins = 512;
    private const int FftSize = 2048, Hop = 1024;
    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _lin = new float[FftSize];
    private int _fill, _since, _wpos;
    private float _floor = -10;
    private const float Span = 35;
    private readonly object _lock = new();

    public event Action<byte[]>? Line;

    public void Reset()
    {
        lock (_lock) { _fill = 0; _since = 0; }
    }

    public void Push(float[] audio)
    {
        lock (_lock)
        {
            foreach (var s in audio)
            {
                _ring[_wpos] = s;
                _wpos = (_wpos + 1) % FftSize;
                if (_fill < FftSize) _fill++;
                if (++_since >= Hop && _fill == FftSize) { _since = 0; Emit(); }
            }
        }
    }

    private void Emit()
    {
        for (int i = 0; i < FftSize; i++) _lin[i] = _ring[(_wpos + i) % FftSize];
        var db = Fft.PowerDb(_lin, 12_000, 3000);
        var sorted = (float[])db.Clone(); Array.Sort(sorted);
        _floor = 0.95f * _floor + 0.05f * (sorted[sorted.Length / 2] - 3);
        var line = new byte[Bins];
        for (int x = 0; x < Bins && x < db.Length; x++)
            line[x] = (byte)(Math.Clamp((db[x] - _floor) / Span, 0, 1) * 255);
        Line?.Invoke(line);
    }
}
