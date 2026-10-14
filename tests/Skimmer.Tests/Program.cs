using System.Diagnostics;
using Skimmer.Core;
using Skimmer.Core.Dsp;

// Δοκιμή από άκρη σε άκρη χωρίς υλικό:
// ft8sim → σήματα ήχου → συνθετικό IQ 960 kS/s ανά ζώνη → SkimmerEngine (αλλαγές ζώνης, DSP) → jt9.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {what}"); if (!ok) fails++; }

// ---------- μονάδες ----------
Check(Ft8Message.Parse("CQ SV1EEX KM17") == ("SV1EEX", "KM17"), "CQ απλό");
Check(Ft8Message.Parse("CQ DX JA1XYZ PM95") == ("JA1XYZ", "PM95"), "CQ DX");
Check(Ft8Message.Parse("SV1EEX W1AW FN31") == ("W1AW", "FN31"), "απάντηση με locator");
Check(Ft8Message.Parse("SV1EEX W1AW RR73") == ("W1AW", null), "RR73 δεν είναι locator");
Check(Ft8Message.Parse("SV1EEX <PJ4/K1ABC> -12") == ("PJ4/K1ABC", null), "σύνθετο διακριτικό");
Check(Jt9Decoder.ParseLine("000001 -10  0.0 1500 ~  CQ SV1EEX KM17          ") == new DecodeLine(-10, 0.0, 1500, "CQ SV1EEX KM17"), "ανάλυση γραμμής jt9");
Check(Math.Abs(Maidenhead.DistanceKm("KM17", "FN31") - 7900) < 300, $"απόσταση KM17–FN31 ≈ {Maidenhead.DistanceKm("KM17", "FN31"):F0} km");

foreach (int nb in new[] { 2, 3, 4 })
{
    var bands = Bands.All.Take(nb).ToList();
    var sch = new BandSchedule(bands);
    bool allParities = bands.All(b =>
    {
        var par = Enumerable.Range(0, 4 * nb).Where(s => sch.ForSlot(s) == b).Select(s => s % 2).Distinct().Count();
        return par == 2;
    });
    Check(allParities, $"πρόγραμμα {nb} ζωνών: κάθε ζώνη ακούγεται σε άρτιους και περιττούς κύκλους");
}

using (var psk = new PskReporterClient("SV1EEX", "KM17", "RtlFt8Skimmer 0.1", testServer: true, TimeSpan.FromHours(1)))
{
    var b = Bands.ByName("20m")!;
    var spots = Enumerable.Range(0, 80).Select(i => new Spot(DateTime.UtcNow, b, -10, 0.1, 1000 + i, "CQ X", $"K{i}ABC", "FN31", null)).ToList();
    var pkts = psk.BuildPackets(spots).ToList();
    bool lensOk = pkts.All(p => p.Length <= 1500 && (p[2] << 8 | p[3]) == p.Length && p[0] == 0 && p[1] == 0x0A);
    Check(pkts.Count >= 2 && lensOk, $"PSKReporter: {pkts.Count} πακέτα, σωστά μήκη/κεφαλίδες");
    // κάθε set έχει μήκος πολλαπλάσιο του 4 και τα sets καλύπτουν ακριβώς το πακέτο
    bool setsOk = pkts.All(p =>
    {
        int pos = 16;
        while (pos < p.Length) { int l = p[pos + 2] << 8 | p[pos + 3]; if (l % 4 != 0 || l < 4) return false; pos += l; }
        return pos == p.Length;
    });
    Check(setsOk, "PSKReporter: δομή sets IPFIX");
}

// ---------- από άκρη σε άκρη ----------
string work = Path.Combine(Path.GetTempPath(), "skimtest");
if (Directory.Exists(work)) Directory.Delete(work, true);
Directory.CreateDirectory(work);

var plan = new Dictionary<string, (string Msg, double F0)[]>
{
    ["20m"] = [("CQ JA1XYZ PM95", 800), ("SV1EEX W1AW FN31", 1500), ("CQ DX VK2ABC QF56", 2200)],
    ["40m"] = [("CQ DL1ABC JO62", 1000), ("CQ EA4XYZ IN80", 1900)],
    ["15m"] = [("CQ PY2ABC GG66", 1200)],
};
var audioByDial = new Dictionary<long, float[]>();
foreach (var (bandName, msgs) in plan)
{
    var sum = new float[180_000];
    foreach (var (msg, f0) in msgs)
    {
        var dir = Path.Combine(work, "sim", $"{bandName}_{f0}");
        Directory.CreateDirectory(dir);
        Run("ft8sim", dir, msg, f0.ToString(System.Globalization.CultureInfo.InvariantCulture), "0", "0", "0", "1", "99");
        var a = WavWriter.ReadMono16(Directory.GetFiles(dir, "*.wav")[0]);
        for (int i = 0; i < Math.Min(a.Length, sum.Length); i++) sum[i] += a[i];
    }
    audioByDial[Bands.ByName(bandName)!.DialHz] = sum;
}

var opts = new SkimmerOptions { MyGrid = "KM17" };
var start = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
var synth = new SynthSource(audioByDial, opts.TuneOffsetHz, start, seconds: 6 * 15, snrDb: -12);
var schedule = new BandSchedule([Bands.ByName("20m")!, Bands.ByName("40m")!, Bands.ByName("15m")!]);
var dec = new Jt9Decoder("jt9", Path.Combine(work, "jt9"));
var engine = new SkimmerEngine(synth, schedule, dec, opts);
var got = new List<Spot>();
engine.SpotDecoded += s => { lock (got) got.Add(s); };
engine.SlotDecoded += (b, t, n) => Console.WriteLine($"     κύκλος {t:HH:mm:ss} {b.Name}: {n} αποκωδικοποιήσεις");
engine.Log += Console.WriteLine;
var sw = Stopwatch.StartNew();
engine.Start();
engine.Completion!.Wait();
Console.WriteLine($"     ({sw.Elapsed.TotalSeconds:F1} s για {6 * 15} s σήματος, {synth.Retunes} αλλαγές συχνότητας)");

foreach (var (bandName, msgs) in plan)
    foreach (var (msg, f0) in msgs)
    {
        var hits = got.Where(s => s.Band.Name == bandName && s.Message == msg).ToList();
        Check(hits.Count >= 2 && hits.All(h => Math.Abs(h.AudioHz - f0) <= 3),
            $"{bandName} «{msg}» αποκωδικοποιήθηκε {hits.Count} φορές, {string.Join(",", hits.Select(h => h.AudioHz + "Hz/" + h.Snr + "dB"))}");
    }
Check(!got.Any(s => !plan[s.Band.Name].Any(p => p.Msg == s.Message)), "καμία αποκωδικοποίηση σε λάθος ζώνη");

// αντεστραμμένο φάσμα: χωρίς διόρθωση τίποτα, με InvertSpectrum ξανά όλα
foreach (bool fix in new[] { false, true })
{
    var s2 = new SynthSource(audioByDial, opts.TuneOffsetHz, start, seconds: 2 * 15, snrDb: -12) { Invert = true };
    var o2 = new SkimmerOptions { MyGrid = "KM17", InvertSpectrum = fix };
    var e2 = new SkimmerEngine(s2, new BandSchedule([Bands.ByName("20m")!]), new Jt9Decoder("jt9", Path.Combine(work, "jt9b")), o2);
    int n2 = 0; e2.SpotDecoded += _ => Interlocked.Increment(ref n2);
    e2.Start(); e2.Completion!.Wait();
    Check(fix ? n2 >= 6 : n2 == 0, $"αντεστραμμένο IQ, InvertSpectrum={fix}: {n2} αποκωδικοποιήσεις");
}

Console.WriteLine(fails == 0 ? "\nΌλα εντάξει." : $"\n{fails} αποτυχίες.");
return fails == 0 ? 0 : 1;

static void Run(string exe, string dir, params string[] args)
{
    var psi = new ProcessStartInfo(exe) { WorkingDirectory = dir, RedirectStandardOutput = true };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!; p.StandardOutput.ReadToEnd(); p.WaitForExit();
}

/// <summary>
/// Συνθετικός «δέκτης»: παράγει IQ 960 kS/s με το σήμα της ζώνης στην οποία είναι συντονισμένος
/// (αναλυτικό σήμα USB στη θέση +offset), συν θόρυβο, κβαντισμένο σε 8 bit όπως το RTL-SDR.
/// </summary>
sealed class SynthSource : IIqSource
{
    private readonly Dictionary<long, (float[] Re, float[] Im)> _analytic = new();
    private readonly double _offset, _noise;
    private readonly long _total;
    private long _n;
    private long _center;
    private readonly Random _rng = new(1);
    public int Retunes;
    public bool Invert;

    public SynthSource(Dictionary<long, float[]> audio, double offset, DateTime start, int seconds, double snrDb)
    {
        _offset = offset; VirtualStartUtc = start; _total = (long)seconds * SampleRate;
        // θόρυβος ~19 LSB όπως σε πραγματικό δέκτη· το σήμα ορίζεται από το SNR σε 2500 Hz (σύμβαση WSJT-X)
        _noise = 0.15;
        double nPow2500 = 2 * _noise * _noise * 2500.0 / SampleRate;
        double amp = Math.Sqrt(nPow2500 * Math.Pow(10, snrDb / 10));
        foreach (var (dial, a) in audio)
        {
            // αναλυτικό σήμα (Hilbert με FFT), κανονικοποίηση ισχύος ανά σήμα
            int N = 262_144;
            var re = new float[N]; var im = new float[N];
            Array.Copy(a, re, Math.Min(a.Length, N));
            Fft.Transform(re, im);
            for (int k = 1; k < N / 2; k++) { re[k] *= 2; im[k] *= 2; }
            for (int k = N / 2 + 1; k < N; k++) { re[k] = 0; im[k] = 0; }
            for (int k = 0; k < N; k++) im[k] = -im[k];
            Fft.Transform(re, im);
            for (int k = 0; k < N; k++) { re[k] /= N; im[k] = -im[k] / N; }
            double p = 0; int c = 0;
            for (int k = 0; k < a.Length; k++) if (a[k] != 0) { p += re[k] * re[k] + im[k] * im[k]; c++; }
            float g = (float)(amp / Math.Sqrt(p / Math.Max(1, c)));
            for (int k = 0; k < N; k++) { re[k] *= g; im[k] *= g; }
            _analytic[dial] = (re, im);
        }
    }

    public int SampleRate => 960_000;
    public bool IsRealtime => false;
    public DateTime VirtualStartUtc { get; }
    public void Open() { }
    public void SetCenterFrequency(long hz) { _center = hz; Retunes++; }

    public int Read(byte[] buf)
    {
        if (_n >= _total) return 0;
        int samples = (int)Math.Min(buf.Length / 2, _total - _n);
        _analytic.TryGetValue(_center + (long)_offset, out var sig);
        double w = 2 * Math.PI * _offset / SampleRate;
        for (int k = 0; k < samples; k++, _n++)
        {
            double t = (double)_n / SampleRate;
            double pos = (t % 15.0) * 12_000;
            double i = Gauss() * _noise, q = Gauss() * _noise;
            if (sig.Re != null)
            {
                int p0 = (int)pos; double fr = pos - p0;
                if (p0 + 1 < sig.Re.Length)
                {
                    double ar = sig.Re[p0] * (1 - fr) + sig.Re[p0 + 1] * fr;
                    double ai = sig.Im[p0] * (1 - fr) + sig.Im[p0 + 1] * fr;
                    double ph = w * _n, c = Math.Cos(ph), s = Math.Sin(ph);
                    i += ar * c - ai * s; q += ar * s + ai * c;
                }
            }
            buf[2 * k] = (byte)Math.Clamp(Math.Round(127.4 + 128 * i), 0, 255);
            if (Invert) q = -q;
            buf[2 * k + 1] = (byte)Math.Clamp(Math.Round(127.4 + 128 * q), 0, 255);
        }
        return samples * 2;
    }

    private double Gauss()
    {
        double u1 = 1 - _rng.NextDouble(), u2 = _rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    public void Dispose() { }
}
