using System.IO;
using System.Text.Json;

namespace Skimmer.Wpf;

public sealed class AppSettings
{
    public string MyCall { get; set; } = "SV1EEX";
    public string MyGrid { get; set; } = "KM17";
    public string Jt9Path { get; set; } = FindJt9();
    public uint DeviceIndex { get; set; }
    /// <summary>Κέρδος σε dB· αρνητικό = αυτόματο.</summary>
    public double GainDb { get; set; } = 28.0;
    public int Ppm { get; set; }
    public bool BiasTee { get; set; }
    public int DwellSlots { get; set; } = 1;
    public bool PskReporter { get; set; }
    public bool PskReporterTest { get; set; } = true;
    public List<string> Bands { get; set; } = ["40m", "20m", "15m", "10m"];

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RtlFt8Skimmer");
    private static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
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
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            @"C:\WSJT\wsjtx\bin\jt9.exe",
            Path.Combine(pf, "wsjtx", "bin", "jt9.exe"),
            Path.Combine(pf, "WSJT-X", "bin", "jt9.exe"),
            Path.Combine(AppContext.BaseDirectory, "jt9.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
