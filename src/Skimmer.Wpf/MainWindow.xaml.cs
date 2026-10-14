using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Skimmer.Core;

namespace Skimmer.Wpf;

public partial class MainWindow : Window
{
    private static readonly double[] R828DGains =
        [0.0, 0.9, 1.4, 2.7, 3.7, 7.7, 8.7, 12.5, 14.4, 15.7, 16.6, 19.7, 20.7, 22.9, 25.4,
         28.0, 29.7, 32.8, 33.8, 36.4, 37.2, 38.6, 40.2, 42.1, 43.4, 43.9, 44.5, 48.0, 49.6];

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<BandItem> _bands = new();
    private readonly ObservableCollection<SpotRow> _rows = new();
    private readonly Waterfall _wf = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Dictionary<string, Dictionary<string, DateTime>> _heard = new();

    private SkimmerEngine? _engine;
    private PskReporterClient? _psk;
    private bool _mapReady;
    private int _totalDecodes;
    private string _lastLog = "";

    public MainWindow()
    {
        InitializeComponent();
        foreach (var b in Bands.All)
            _bands.Add(new BandItem(b, _settings.Bands.Contains(b.Name)));
        BandList.ItemsSource = _bands;
        Decodes.ItemsSource = _rows;
        WfImage.Source = _wf.Bitmap;

        GainBox.Items.Add("Auto");
        foreach (var g in R828DGains) GainBox.Items.Add(g.ToString("0.0", CultureInfo.InvariantCulture));
        GainBox.SelectedItem = _settings.GainDb < 0 ? "Auto"
            : R828DGains.MinBy(g => Math.Abs(g - _settings.GainDb)).ToString("0.0", CultureInfo.InvariantCulture);

        CallBox.Text = _settings.MyCall;
        GridBox.Text = _settings.MyGrid;
        PpmBox.Text = _settings.Ppm.ToString(CultureInfo.InvariantCulture);
        BiasTeeBox.IsChecked = _settings.BiasTee;
        DwellBox.SelectedIndex = _settings.DwellSlots switch { 2 => 1, 4 => 2, _ => 0 };
        Jt9Box.Text = _settings.Jt9Path;
        PskBox.IsChecked = _settings.PskReporter;
        PskTestBox.IsChecked = _settings.PskReporterTest;
        UpdateTitle();
        CallBox.TextChanged += (_, _) => UpdateTitle();

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        Loaded += async (_, _) => await InitMapAsync();
        Closing += (_, _) => { StopEngine(); SaveSettings(); };
    }

    private void UpdateTitle()
    {
        var call = CallBox.Text.Trim().ToUpperInvariant();
        Title = $"RTL-SDR V4 FT8 Skimmer · {call}";
        CallLabel.Text = call;
    }

    // ---------------- χάρτης ----------------

    private async Task InitMapAsync()
    {
        try
        {
            string web = Path.Combine(AppSettings.Folder, "web");
            Directory.CreateDirectory(web);
            using (var res = typeof(MainWindow).Assembly.GetManifestResourceStream("Skimmer.Wpf.map.html")!)
            using (var fs = File.Create(Path.Combine(web, "map.html")))
                res.CopyTo(fs);

            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppSettings.Folder, "webview"));
            await Map.EnsureCoreWebView2Async(env);
            // εικονικός διακομιστής: δίνει κανονικό origin ώστε να φορτώνουν τα πλακίδια χάρτη
            Map.CoreWebView2.SetVirtualHostNameToFolderMapping("skimmer.local", web, CoreWebView2HostResourceAccessKind.Allow);
            Map.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (e.TryGetWebMessageAsString() == "ready") { _mapReady = true; _ = PushHomeAsync(); }
            };
            Map.CoreWebView2.Navigate("https://skimmer.local/map.html");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Map failed to load (WebView2 Runtime missing?): " + ex.Message;
        }
    }

    private async Task PushHomeAsync()
    {
        if (!_mapReady) return;
        var bands = Bands.All.Select(b => new { name = b.Name, color = b.Color });
        await Map.ExecuteScriptAsync($"setBands({JsonSerializer.Serialize(bands)})");
        var grid = GridBox.Text.Trim();
        if (Maidenhead.IsValid(grid))
        {
            var (lat, lon) = Maidenhead.ToLatLon(grid);
            await Map.ExecuteScriptAsync(string.Create(CultureInfo.InvariantCulture,
                $"setHome({lat},{lon},{JsonSerializer.Serialize(CallBox.Text.Trim().ToUpperInvariant())})"));
        }
    }

    private void MapSpot(Spot s)
    {
        if (!_mapReady || s.Sender is null || s.Grid is null) return;
        var (lat, lon) = Maidenhead.ToLatLon(s.Grid);
        var json = JsonSerializer.Serialize(new
        {
            call = s.Sender, band = s.Band.Name, lat, lon, snr = s.Snr, km = s.DistanceKm,
            msg = s.Message, time = s.SlotUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        });
        _ = Map.ExecuteScriptAsync($"addSpot({json})");
    }

    // ---------------- έναρξη / διακοπή ----------------

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is not null) { StopEngine(); return; }

        var call = CallBox.Text.Trim().ToUpperInvariant();
        var grid = GridBox.Text.Trim();
        if (!Ft8Message.IsCall(call)) { Warn("Invalid callsign."); return; }
        if (!Maidenhead.IsValid(grid)) { Warn("Invalid locator (e.g. KM17 or KM17tw)."); return; }
        if (!File.Exists(Jt9Box.Text)) { Warn("jt9.exe not found. Install WSJT-X or browse to it."); return; }
        var selected = _bands.Where(b => b.Selected).Select(b => b.Band).ToList();
        if (selected.Count == 0) { Warn("Select at least one band."); return; }

        SaveSettings();
        try
        {
            var src = new RtlSdrSource(_settings.DeviceIndex)
            {
                GainTenthsDb = _settings.GainDb < 0 ? null : (int)Math.Round(_settings.GainDb * 10),
                PpmCorrection = _settings.Ppm,
                BiasTee = _settings.BiasTee,
            };
            var opts = new SkimmerOptions { MyCall = call, MyGrid = grid };
            var dec = new Jt9Decoder(_settings.Jt9Path, Path.Combine(AppSettings.Folder, "jt9"));
            _engine = new SkimmerEngine(src, new BandSchedule(selected, _settings.DwellSlots), dec, opts);

            _engine.SpotDecoded += s => Dispatcher.BeginInvoke(() => OnSpot(s));
            _engine.SlotStarted += (b, t) => Dispatcher.BeginInvoke(() => OnSlot(b));
            _engine.SlotDecoded += (b, t, n) => Dispatcher.BeginInvoke(() =>
                _lastLog = $"{t:HH:mm:ss} {b.Name}: {n} decodes");
            _engine.Audio += (_, a) => _wf.Push(a);
            _engine.Log += m => Dispatcher.BeginInvoke(() => _lastLog = m);

            if (_settings.PskReporter)
            {
                _psk = new PskReporterClient(call, grid, $"RtlFt8Skimmer {GetType().Assembly.GetName().Version?.ToString(3)}",
                    _settings.PskReporterTest);
                _psk.Log += m => Dispatcher.BeginInvoke(() => _lastLog = m);
            }

            _engine.Start();
            foreach (var b in _bands) b.Editable = false;
            StartButton.Content = "■ Stop";
            _ = PushHomeAsync();
        }
        catch (DllNotFoundException)
        {
            StopEngine();
            Warn("rtlsdr.dll is missing. Copy rtlsdr.dll and libusb-1.0.dll (x64) from the rtl-sdr-blog release next to the executable.");
        }
        catch (Exception ex)
        {
            StopEngine();
            Warn(ex.Message);
        }
    }

    private void StopEngine()
    {
        try { _engine?.Dispose(); } catch { /* ήδη κλειστό */ }
        _engine = null;
        _psk?.Dispose();
        _psk = null;
        foreach (var b in _bands) { b.Editable = true; b.Active = false; }
        StartButton.Content = "▶ Start";
    }

    private void SaveSettings()
    {
        _settings.MyCall = CallBox.Text.Trim().ToUpperInvariant();
        _settings.MyGrid = GridBox.Text.Trim();
        _settings.Jt9Path = Jt9Box.Text.Trim();
        _settings.GainDb = GainBox.SelectedItem is string g && double.TryParse(g, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1;
        _settings.Ppm = int.TryParse(PpmBox.Text, out var p) ? p : 0;
        _settings.BiasTee = BiasTeeBox.IsChecked == true;
        _settings.DwellSlots = DwellBox.SelectedIndex switch { 1 => 2, 2 => 4, _ => 1 };
        _settings.PskReporter = PskBox.IsChecked == true;
        _settings.PskReporterTest = PskTestBox.IsChecked == true;
        _settings.Bands = _bands.Where(b => b.Selected).Select(b => b.Band.Name).ToList();
        try { _settings.Save(); } catch { /* μη κρίσιμο */ }
    }

    private void BrowseJt9_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "jt9.exe|jt9.exe|Executables|*.exe", Title = "Select jt9.exe (WSJT-X\\bin)" };
        if (dlg.ShowDialog(this) == true) Jt9Box.Text = dlg.FileName;
    }

    private void Warn(string m) => MessageBox.Show(this, m, "RtlFt8Skimmer", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ---------------- συμβάντα μηχανής ----------------

    private void OnSlot(Band b)
    {
        foreach (var it in _bands) it.Active = it.Band == b;
        WfLabel.Text = $"Waterfall · {b.Name} · {b.DialHz / 1e6:0.000} MHz";
        _wf.Marker((Color)ColorConverter.ConvertFromString(b.Color));
    }

    private void OnSpot(Spot s)
    {
        _totalDecodes++;
        var me = CallBox.Text.Trim().ToUpperInvariant();
        _rows.Insert(0, new SpotRow(s, me));
        while (_rows.Count > 3000) _rows.RemoveAt(_rows.Count - 1);

        if (s.Sender is not null)
        {
            if (!_heard.TryGetValue(s.Band.Name, out var d)) _heard[s.Band.Name] = d = new();
            d[s.Sender] = DateTime.UtcNow;
        }
        _psk?.Add(s);
        MapSpot(s);
    }

    private void OnTick()
    {
        _wf.Render();
        var now = DateTime.UtcNow;
        foreach (var it in _bands)
        {
            int n = 0;
            if (_heard.TryGetValue(it.Band.Name, out var d))
            {
                foreach (var k in d.Where(kv => now - kv.Value > TimeSpan.FromMinutes(30)).Select(kv => kv.Key).ToList()) d.Remove(k);
                n = d.Count;
            }
            it.Count = n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "";
        }
        if (_engine is null) { StatusText.Text = "Ready. " + _lastLog; return; }
        double sec = (now - DateTime.UnixEpoch).TotalSeconds % 15;
        int stations = _heard.Values.SelectMany(x => x.Keys).Distinct().Count();
        StatusText.Text = $"{now:HH:mm:ss} UTC · slot {sec,4:0.0}/15 s · {_engine.CurrentBand?.Name ?? "-"} · " +
                          $"stations 30′: {stations} · total: {_totalDecodes} · {_lastLog}";
    }
}

public sealed class BandItem : INotifyPropertyChanged
{
    public BandItem(Band band, bool selected)
    {
        Band = band; _selected = selected;
        Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(band.Color));
        Brush.Freeze();
    }

    public Band Band { get; }
    public string Name => Band.Name;
    public Brush Brush { get; }

    private bool _selected, _active, _editable = true;
    private string _count = "";
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public bool Active { get => _active; set => Set(ref _active, value); }
    public bool Editable { get => _editable; set => Set(ref _editable, value); }
    public string Count { get => _count; set => Set(ref _count, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class SpotRow
{
    public SpotRow(Spot s, string myCall)
    {
        Time = s.SlotUtc.ToString("HHmmss", CultureInfo.InvariantCulture);
        Band = s.Band.Name;
        Snr = s.Snr;
        Dt = s.Dt.ToString("0.0", CultureInfo.InvariantCulture);
        Hz = s.AudioHz;
        Message = s.Message;
        Km = s.DistanceKm is double k ? k.ToString("0", CultureInfo.InvariantCulture) : "";
        var tok = s.Message.Split(' ');
        ForMe = myCall.Length > 0 && tok.Length > 0 && tok[0].Trim('<', '>') == myCall;
        IsCq = s.Message.StartsWith("CQ ", StringComparison.Ordinal);
    }

    public string Time { get; }
    public string Band { get; }
    public int Snr { get; }
    public string Dt { get; }
    public int Hz { get; }
    public string Message { get; }
    public string Km { get; }
    public bool ForMe { get; }
    public bool IsCq { get; }
}
