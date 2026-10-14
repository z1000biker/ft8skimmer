using System.Text.Json;

namespace Skimmer.Web;

public sealed class WebSettings
{
    public string MyCall { get; set; } = "N0CALL";
    public string MyGrid { get; set; } = "";
    public string Jt9Path { get; set; } = FindJt9();
    public uint DeviceIndex { get; set; }
    /// <summary>Κέρδος σε dB· αρνητικό = αυτόματο.</summary>
    public double GainDb { get; set; } = 28.0;
    public int Ppm { get; set; }
    public bool BiasTee { get; set; }
    public bool InvertSpectrum { get; set; }
    public int DwellSlots { get; set; } = 1;
    public bool PskReporter { get; set; }
    public bool PskReporterTest { get; set; } = true;
    public List<string> Bands { get; set; } = ["40m", "20m", "15m", "10m"];

    public static string Folder
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = !string.IsNullOrEmpty(xdg) ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(baseDir, "rtlft8skimmer");
        }
    }

    private static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static WebSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WebSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* χαλασμένο αρχείο: προεπιλογές */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    private static string FindJt9()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var p = Path.Combine(dir, "jt9");
            if (File.Exists(p)) return p;
        }
        return "/usr/bin/jt9";
    }
}
