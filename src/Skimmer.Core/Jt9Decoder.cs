using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Skimmer.Core;

public sealed record DecodeLine(int Snr, double Dt, int AudioHz, string Message);

public interface IFt8Decoder
{
    Task<IReadOnlyList<DecodeLine>> DecodeAsync(float[] audio12k, DateTime slotUtc, string tag, CancellationToken ct);
}

/// <summary>Καλεί το jt9 του WSJT-X (jt9.exe στα Windows) πάνω σε WAV 15 s.</summary>
public sealed partial class Jt9Decoder : IFt8Decoder
{
    private readonly string _exe;
    private readonly string _work;
    public int Depth { get; set; } = 3;
    /// <summary>Αν οριστεί, κρατά αντίγραφο κάθε WAV εκεί (για έλεγχο στο WSJT-X).</summary>
    public string? KeepWavDir { get; set; }

    public Jt9Decoder(string jt9Path, string workDir)
    {
        _exe = jt9Path; _work = workDir;
        Directory.CreateDirectory(_work);
    }

    // 000001 -10  0.0 1500 ~  CQ SV1EEX KM17
    [GeneratedRegex(@"^\s*(\d{4,6})\s+(-?\d+)\s+(-?\d+\.\d+)\s+(\d+)\s+~\s+(.+?)\s*$")]
    private static partial Regex LineRx();

    public static DecodeLine? ParseLine(string line)
    {
        var m = LineRx().Match(line);
        if (!m.Success) return null;
        var msg = Regex.Replace(m.Groups[5].Value, @"\s{2,}.*$", ""); // κόβει σημαιοδότες όπως "a1" στο τέλος
        return new DecodeLine(
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
            msg.Trim());
    }

    public async Task<IReadOnlyList<DecodeLine>> DecodeAsync(float[] audio, DateTime slotUtc, string tag, CancellationToken ct)
    {
        // ξεχωριστός φάκελος ανά ζώνη: το jt9 γράφει δικά του αρχεία στον φάκελο δεδομένων
        string dir = Path.Combine(_work, tag);
        Directory.CreateDirectory(dir);
        string wav = Path.Combine(dir, slotUtc.ToString("yyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".wav");
        WavWriter.WriteNormalized(wav, audio, 12_000);

        var psi = new ProcessStartInfo(_exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        foreach (var a in new[] { "-8", "-d", Depth.ToString(CultureInfo.InvariantCulture), "-a", dir, "-t", dir, wav })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start jt9");
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        try
        {
            if (KeepWavDir is not null)
            {
                Directory.CreateDirectory(KeepWavDir);
                File.Copy(wav, Path.Combine(KeepWavDir, tag + "_" + Path.GetFileName(wav)), true);
            }
            File.Delete(wav);
        }
        catch { /* αδιάφορο */ }

        var res = new List<DecodeLine>();
        foreach (var line in stdout.Split('\n'))
            if (ParseLine(line) is { } d) res.Add(d);
        return res;
    }
}
