namespace Skimmer.Core;

/// <summary>
/// Αναπαραγωγή αρχείου IQ 8 bit (μορφή rtl_sdr, 960 kS/s) με εικονικό χρόνο.
/// Χρήσιμο για δοκιμές χωρίς δέκτη. Αγνοεί τις αλλαγές συχνότητας.
/// </summary>
public sealed class RawIqFileSource : IIqSource
{
    private readonly string _path;
    private FileStream? _fs;

    public RawIqFileSource(string path, DateTime startUtc, bool paced = false)
    {
        _path = path; VirtualStartUtc = startUtc; Paced = paced;
    }

    public int SampleRate => 960_000;
    public bool IsRealtime => false;
    public DateTime VirtualStartUtc { get; }
    /// <summary>Αν true, διαβάζει με ρυθμό πραγματικού χρόνου.</summary>
    public bool Paced { get; }
    public long CenterHz { get; private set; }

    public void Open() => _fs = File.OpenRead(_path);
    public void SetCenterFrequency(long hz) => CenterHz = hz;

    public int Read(byte[] buffer)
    {
        int n = _fs!.Read(buffer, 0, buffer.Length);
        if (Paced && n > 0) Thread.Sleep(TimeSpan.FromSeconds(n / 2.0 / SampleRate));
        return n;
    }

    public void Dispose() => _fs?.Dispose();
}
