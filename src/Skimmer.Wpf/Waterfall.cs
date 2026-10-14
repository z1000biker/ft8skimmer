using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Skimmer.Core.Dsp;

namespace Skimmer.Wpf;

/// <summary>Waterfall 0–3 kHz του ήχου της τρέχουσας ζώνης. Τα δεδομένα έρχονται από το νήμα DSP.</summary>
public sealed class Waterfall
{
    public const int Width = 512, Height = 240;
    private const int FftSize = 2048, Hop = 1024; // 5,86 Hz/bin, ~12 γραμμές/s
    public WriteableBitmap Bitmap { get; } = new(Width, Height, 96, 96, PixelFormats.Bgr32, null);

    private readonly int[] _pixels = new int[Width * Height];
    private readonly float[] _ring = new float[FftSize];
    private int _fill, _sinceLast, _wpos;
    private readonly float[] _lin = new float[FftSize];
    private float _floor = -10, _span = 35;
    private readonly int[] _palette = BuildPalette();
    private readonly object _lock = new();
    private bool _dirty;

    /// <summary>Καλείται από το νήμα DSP.</summary>
    public void Push(float[] audio)
    {
        lock (_lock)
        {
            foreach (var s in audio)
            {
                _ring[_wpos] = s;
                _wpos = (_wpos + 1) % FftSize;
                if (_fill < FftSize) _fill++;
                if (++_sinceLast >= Hop && _fill == FftSize) { _sinceLast = 0; AddLine(); }
            }
        }
    }

    /// <summary>Οριζόντια γραμμή-σημάδι στην αλλαγή ζώνης.</summary>
    public void Marker(Color c)
    {
        lock (_lock)
        {
            _fill = 0; _sinceLast = 0; // ο ήχος της προηγούμενης ζώνης δεν ανακατεύεται με τη νέα
        }
        lock (_lock)
        {
            Scroll();
            int col = c.R << 16 | c.G << 8 | c.B;
            for (int x = 0; x < Width; x++) _pixels[x] = col;
            _dirty = true;
        }
    }

    private void AddLine()
    {
        for (int i = 0; i < FftSize; i++) _lin[i] = _ring[(_wpos + i) % FftSize];
        var db = Fft.PowerDb(_lin, 12_000, 3000); // 512 bins
        // αυτόματη στάθμη: θόρυβος ≈ διάμεσος
        var sorted = (float[])db.Clone(); Array.Sort(sorted);
        float median = sorted[sorted.Length / 2];
        _floor = 0.95f * _floor + 0.05f * (median - 3);
        Scroll();
        for (int x = 0; x < Width && x < db.Length; x++)
        {
            float v = (db[x] - _floor) / _span;
            int idx = (int)(Math.Clamp(v, 0, 1) * 255);
            _pixels[x] = _palette[idx];
        }
        _dirty = true;
    }

    private void Scroll() => Array.Copy(_pixels, 0, _pixels, Width, Width * (Height - 1));

    /// <summary>Καλείται από το νήμα UI (CompositionTarget/Timer).</summary>
    public void Render()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            Bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), _pixels, Width * 4, 0);
            _dirty = false;
        }
    }

    private static int[] BuildPalette()
    {
        // μαύρο → σκούρο μπλε → κυανό → κίτρινο → λευκό
        (float p, byte r, byte g, byte b)[] stops = [(0, 0, 0, 0), (0.3f, 10, 20, 120), (0.55f, 0, 170, 200), (0.8f, 250, 220, 30), (1, 255, 255, 255)];
        var pal = new int[256];
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            int k = 0; while (k < stops.Length - 2 && t > stops[k + 1].p) k++;
            var (p0, r0, g0, b0) = stops[k]; var (p1, r1, g1, b1) = stops[k + 1];
            float f = (t - p0) / (p1 - p0);
            pal[i] = (int)(r0 + (r1 - r0) * f) << 16 | (int)(g0 + (g1 - g0) * f) << 8 | (int)(b0 + (b1 - b0) * f);
        }
        return pal;
    }
}
