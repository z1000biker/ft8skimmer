namespace Skimmer.Core;

public sealed record Band(string Name, long DialHz, string Color);

public static class Bands
{
    /// <summary>Συχνότητες dial FT8 (USB). Κάτω από 28,8 MHz το V4 χρησιμοποιεί τον upconverter.</summary>
    public static readonly IReadOnlyList<Band> All =
    [
        new("160m",  1_840_000, "#8c564b"),
        new("80m",   3_573_000, "#9467bd"),
        new("60m",   5_357_000, "#7f7f7f"),
        new("40m",   7_074_000, "#1f77b4"),
        new("30m",  10_136_000, "#17becf"),
        new("20m",  14_074_000, "#2ca02c"),
        new("17m",  18_100_000, "#bcbd22"),
        new("15m",  21_074_000, "#ff7f0e"),
        new("12m",  24_915_000, "#e377c2"),
        new("10m",  28_074_000, "#d62728"),
        new("6m",   50_313_000, "#000000"),
    ];

    public static Band? ByName(string name) => All.FirstOrDefault(b => b.Name == name);
}

/// <summary>
/// Ποια ζώνη ακούμε σε κάθε κύκλο 15 s.
/// Οι σταθμοί FT8 εκπέμπουν σε άρτιους ή περιττούς κύκλους. Με απλή εναλλαγή και άρτιο πλήθος ζωνών,
/// κάθε ζώνη θα ακουγόταν πάντα στην ίδια ισοτιμία και θα χανόταν ο μισός κόσμος.
/// Γι' αυτό, όταν χρειάζεται, η σειρά μετατοπίζεται κατά ένα σε κάθε γύρο.
/// </summary>
public sealed class BandSchedule
{
    private readonly IReadOnlyList<Band> _bands;
    private readonly int _dwell;

    public BandSchedule(IReadOnlyList<Band> bands, int dwellSlots = 1)
    {
        if (bands.Count == 0) throw new ArgumentException("Select at least one band.");
        _bands = bands; _dwell = Math.Max(1, dwellSlots);
    }

    public IReadOnlyList<Band> Bands => _bands;

    public Band ForSlot(long slotIndex)
    {
        int n = _bands.Count;
        long step = slotIndex / _dwell;          // ένα «βήμα» = dwell συνεχόμενοι κύκλοι στην ίδια ζώνη
        bool rotate = _dwell % 2 == 1 && n % 2 == 0;
        long round = step / n;
        long idx = (step % n + (rotate ? round : 0)) % n;
        return _bands[(int)idx];
    }
}
