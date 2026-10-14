using System.Collections.Concurrent;

namespace Skimmer.Core;

public sealed class SkimmerOptions
{
    public string MyCall { get; set; } = "SV1EEX";
    public string MyGrid { get; set; } = "KM17";
    /// <summary>Πόσο κάτω από το dial συντονίζεται ο δέκτης (αποφυγή αιχμής DC).</summary>
    public double TuneOffsetHz { get; set; } = 150_000;
    /// <summary>Δευτερόλεπτο του κύκλου όπου αλλάζει ζώνη (οι εκπομπές FT8 τελειώνουν ~13,2 s).</summary>
    public double RetuneAtSecond { get; set; } = 14.0;
    /// <summary>Δευτερόλεπτα που απορρίπτονται μετά την αλλαγή (μεταβατικά PLL/φίλτρων).</summary>
    public double SettleSeconds { get; set; } = 0.15;
}

/// <summary>
/// Η καρδιά του skimmer: διαβάζει IQ, κόβει τον χρόνο σε κύκλους των 15 s, αλλάζει ζώνη
/// πριν από κάθε κύκλο, φτιάχνει ήχο USB και στέλνει κάθε ολοκληρωμένο κύκλο για αποκωδικοποίηση.
/// </summary>
public sealed class SkimmerEngine : IDisposable
{
    public const double SlotSeconds = 15.0;
    private const int AudioRate = Dsp.UsbDemodulator.AudioRate;
    private const int SlotSamples = (int)(SlotSeconds * AudioRate);

    private readonly IIqSource _src;
    private readonly BandSchedule _schedule;
    private readonly IFt8Decoder _decoder;
    private readonly SkimmerOptions _opt;
    private readonly Dsp.UsbDemodulator _demod;
    private readonly BlockingCollection<(byte[] Buf, int Len, DateTime T0)> _iq = new(64);
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Thread? _reader, _worker;

    // κατάσταση κύκλου
    private long _slot = long.MinValue;
    private Band? _slotBand, _tunedBand;
    private float[] _slotAudio = new float[SlotSamples];
    private DateTime _settleUntil;
    private readonly List<float> _audioTmp = new(4096);

    public event Action<Spot>? SpotDecoded;
    public event Action<Band, DateTime>? SlotStarted;
    public event Action<Band, DateTime, int>? SlotDecoded;
    public event Action<Band, float[]>? Audio;
    public event Action<string>? Log;

    public SkimmerEngine(IIqSource source, BandSchedule schedule, IFt8Decoder decoder, SkimmerOptions options)
    {
        _src = source; _schedule = schedule; _decoder = decoder; _opt = options;
        _demod = new Dsp.UsbDemodulator(source.SampleRate, options.TuneOffsetHz);
    }

    public Band? CurrentBand => _slotBand;
    public Task? Completion { get; private set; }
    private readonly TaskCompletionSource _done = new();
    private int _pendingDecodes;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _src.Open();
        Completion = _done.Task;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "iq-reader", Priority = ThreadPriority.AboveNormal };
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "dsp" };
        _worker.Start();
        _reader.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _reader?.Join(2000);
        _worker?.Join(2000);
    }

    private void ReadLoop()
    {
        var ct = _cts!.Token;
        int fs = _src.SampleRate;
        const int len = 1 << 16; // ~34 ms στα 960 kS/s
        long count = 0;
        DateTime anchor = _src.IsRealtime ? DateTime.UtcNow : _src.VirtualStartUtc;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var buf = new byte[len];
                int n = _src.Read(buf);
                if (n <= 0) break;
                int samples = n / 2;
                DateTime t0 = anchor.AddSeconds((double)count / fs);
                if (_src.IsRealtime)
                {
                    // χρονοσήμανση από μετρητή δειγμάτων, με επανευθυγράμμιση αν ξεφύγει (π.χ. χαμένα δείγματα USB)
                    var est = DateTime.UtcNow.AddSeconds(-(double)samples / fs);
                    if (Math.Abs((est - t0).TotalSeconds) > 0.2) { anchor = est.AddSeconds(-(double)count / fs); t0 = est; }
                }
                count += samples;
                _iq.Add((buf, n, t0), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke($"Σφάλμα ανάγνωσης: {ex.Message}"); }
        finally { _iq.CompleteAdding(); }
    }

    private void WorkLoop()
    {
        int fs = _src.SampleRate;
        try
        {
            foreach (var (buf, len, t0) in _iq.GetConsumingEnumerable())
            {
                // υποδιαίρεση σε κομμάτια ώστε η αλλαγή ζώνης να γίνεται κοντά στη σωστή στιγμή
                const int chunk = 8000; // bytes = 4000 δείγματα ≈ 4 ms
                for (int off = 0; off < len; off += chunk)
                {
                    int l = Math.Min(chunk, len - off);
                    DateTime t = t0.AddSeconds((double)(off / 2) / fs);
                    ProcessChunk(buf.AsSpan(off, l), t);
                }
            }
            FinishSlot(); // ό,τι έμεινε
        }
        catch (Exception ex) { Log?.Invoke($"Σφάλμα DSP: {ex}"); }
        finally
        {
            if (Interlocked.CompareExchange(ref _pendingDecodes, 0, 0) == 0) _done.TrySetResult();
            else _ = Task.Run(async () => { while (Volatile.Read(ref _pendingDecodes) > 0) await Task.Delay(50); _done.TrySetResult(); });
        }
    }

    private static long SlotOf(DateTime t) => (long)Math.Floor((t - DateTime.UnixEpoch).TotalSeconds / SlotSeconds);
    private static DateTime SlotStart(long s) => DateTime.UnixEpoch.AddSeconds(s * SlotSeconds);

    private void ProcessChunk(ReadOnlySpan<byte> iq, DateTime t)
    {
        long slot = SlotOf(t);
        double sec = (t - SlotStart(slot)).TotalSeconds;

        if (slot != _slot)
        {
            FinishSlot();
            _slot = slot;
            _slotBand = _schedule.ForSlot(slot);
            _slotAudio = new float[SlotSamples];
            if (_tunedBand != _slotBand) Tune(_slotBand, t); // πρώτη φορά ή καθυστέρηση
            SlotStarted?.Invoke(_slotBand, SlotStart(slot));
        }

        // λίγο πριν το τέλος του κύκλου: συντονισμός στην επόμενη ζώνη
        if (sec >= _opt.RetuneAtSecond)
        {
            var next = _schedule.ForSlot(slot + 1);
            if (next != _tunedBand) Tune(next, t);
            return; // τα δείγματα μετά την αλλαγή δεν ανήκουν σε αυτή τη ζώνη
        }

        if (_tunedBand != _slotBand || t < _settleUntil) return;

        _audioTmp.Clear();
        _demod.Process(iq, _audioTmp);
        if (_audioTmp.Count == 0) return;

        int start = (int)Math.Round(sec * AudioRate);
        for (int k = 0; k < _audioTmp.Count; k++)
        {
            int idx = start + k;
            if ((uint)idx < SlotSamples) _slotAudio[idx] = _audioTmp[k];
        }
        Audio?.Invoke(_slotBand!, _audioTmp.ToArray());
    }

    private void Tune(Band b, DateTime t)
    {
        try { _src.SetCenterFrequency((long)(b.DialHz - _opt.TuneOffsetHz)); }
        catch (Exception ex) { Log?.Invoke($"Αλλαγή συχνότητας: {ex.Message}"); }
        _tunedBand = b;
        _demod.Reset();
        _settleUntil = t.AddSeconds(_opt.SettleSeconds);
    }

    private void FinishSlot()
    {
        if (_slot == long.MinValue || _slotBand is null) return;
        // αγνοεί κύκλους χωρίς αρκετό σήμα (π.χ. ξεκίνημα στη μέση του κύκλου)
        int filled = 0;
        for (int i = 0; i < SlotSamples; i += 120) if (_slotAudio[i] != 0) filled++;
        if (filled < 0.8 * (13.0 / SlotSeconds) * (SlotSamples / 120)) return;

        var audio = _slotAudio; var band = _slotBand; var slotUtc = SlotStart(_slot);
        Interlocked.Increment(ref _pendingDecodes);
        _ = Task.Run(async () =>
        {
            await _decodeGate.WaitAsync();
            try
            {
                var lines = await _decoder.DecodeAsync(audio, slotUtc, band.Name, CancellationToken.None);
                foreach (var d in lines)
                {
                    var (sender, grid) = Ft8Message.Parse(d.Message);
                    double? km = grid != null && Maidenhead.IsValid(_opt.MyGrid) ? Maidenhead.DistanceKm(_opt.MyGrid, grid) : null;
                    SpotDecoded?.Invoke(new Spot(slotUtc, band, d.Snr, d.Dt, d.AudioHz, d.Message, sender, grid, km));
                }
                SlotDecoded?.Invoke(band, slotUtc, lines.Count);
            }
            catch (Exception ex) { Log?.Invoke($"Αποκωδικοποίηση {band.Name}: {ex.Message}"); }
            finally { _decodeGate.Release(); Interlocked.Decrement(ref _pendingDecodes); }
        });
    }

    public void Dispose()
    {
        Stop();
        _src.Dispose();
        _cts?.Dispose();
    }
}
