using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Skimmer.Core;
using Skimmer.Core.Dsp;

namespace Skimmer.Web;

/// <summary>
/// Κρατά τη μηχανή του skimmer και τους συνδεδεμένους browsers.
/// Όλα τα γεγονότα (κύκλοι, αποκωδικοποιήσεις, γραμμές waterfall) στέλνονται σε κάθε client μέσω WebSocket.
/// </summary>
public sealed class SkimmerHost : IDisposable
{
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<Guid, Channel<(bool Binary, byte[] Data)>> _clients = new();
    private readonly LinkedList<object> _recent = new();
    private readonly Dictionary<string, Dictionary<string, DateTime>> _heard = new();
    private readonly SpectrumLines _spectrum = new();
    private readonly Func<WebSettings, IIqSource>? _sourceFactory;
    private SkimmerEngine? _engine;
    private PskReporterClient? _psk;
    private int _total;
    private string _log = "";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SkimmerHost(Func<WebSettings, IIqSource>? sourceFactory = null)
    {
        _sourceFactory = sourceFactory;
        Settings = WebSettings.Load();
        _spectrum.Line += line => Broadcast(true, line);
    }

    public WebSettings Settings { get; private set; }
    public bool Running => _engine is not null;
    public string? CurrentBand => _engine?.CurrentBand?.Name;

    public void UpdateSettings(WebSettings s)
    {
        lock (_lock)
        {
            if (Running) throw new InvalidOperationException("Stop the skimmer before changing settings.");
            Settings = s;
            try { s.Save(); } catch (Exception ex) { SetLog("Could not save settings: " + ex.Message); }
        }
        BroadcastStatus();
    }

    public void Start()
    {
        lock (_lock)
        {
            if (Running) return;
            var s = Settings;
            var call = s.MyCall.Trim().ToUpperInvariant();
            if (call == "N0CALL" || !Ft8Message.IsCall(call)) throw new ArgumentException("Enter your own callsign first.");
            if (!Maidenhead.IsValid(s.MyGrid)) throw new ArgumentException("Enter your Maidenhead locator (e.g. JN58 or JN58td).");
            if (!File.Exists(s.Jt9Path)) throw new ArgumentException($"jt9 not found at {s.Jt9Path}. Install WSJT-X (e.g. 'sudo apt install wsjtx').");
            var bands = Bands.All.Where(b => s.Bands.Contains(b.Name)).ToList();
            if (bands.Count == 0) throw new ArgumentException("Select at least one band.");

            IIqSource src = _sourceFactory?.Invoke(s) ?? new RtlSdrSource(s.DeviceIndex)
            {
                GainTenthsDb = s.GainDb < 0 ? null : (int)Math.Round(s.GainDb * 10),
                PpmCorrection = s.Ppm,
                BiasTee = s.BiasTee,
            };
            var opts = new SkimmerOptions { MyCall = call, MyGrid = s.MyGrid, InvertSpectrum = s.InvertSpectrum };
            var dec = new Jt9Decoder(s.Jt9Path, Path.Combine(Path.GetTempPath(), "rtlft8skimmer-jt9"));
            var engine = new SkimmerEngine(src, new BandSchedule(bands, s.DwellSlots), dec, opts);

            engine.SpotDecoded += OnSpot;
            engine.SlotStarted += (b, t) =>
            {
                _spectrum.Reset();
                Broadcast(new { t = "slot", band = b.Name, color = b.Color, dial = b.DialHz, utc = t });
                BroadcastStatus();
            };
            engine.SlotDecoded += (b, t, n) => SetLog($"{t:HH:mm:ss} {b.Name}: {n} decodes");
            engine.Audio += (_, a) => _spectrum.Push(a);
            engine.Log += SetLog;

            if (s.PskReporter)
            {
                _psk = new PskReporterClient(call, s.MyGrid, $"RtlFt8Skimmer {typeof(SkimmerHost).Assembly.GetName().Version?.ToString(3)}", s.PskReporterTest);
                _psk.Log += SetLog;
            }

            try { engine.Start(); }
            catch (DllNotFoundException ex) { engine.Dispose(); _psk?.Dispose(); _psk = null; throw new InvalidOperationException(ex.Message); }
            catch { engine.Dispose(); _psk?.Dispose(); _psk = null; throw; }
            _engine = engine;
            SetLog("Started.");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { _engine?.Dispose(); } catch { /* ήδη κλειστό */ }
            _engine = null;
            _psk?.Dispose();
            _psk = null;
        }
        SetLog("Stopped.");
    }

    public Task? EngineCompletion => _engine?.Completion;

    private void OnSpot(Spot s)
    {
        double? lat = null, lon = null;
        if (s.Grid is not null) (lat, lon) = Maidenhead.ToLatLon(s.Grid);
        var me = Settings.MyCall.Trim().ToUpperInvariant();
        var first = s.Message.Split(' ')[0].Trim('<', '>');
        var dto = new
        {
            t = "spot",
            utc = s.SlotUtc.ToString("HH:mm:ss"),
            band = s.Band.Name, color = s.Band.Color,
            snr = s.Snr, dt = Math.Round(s.Dt, 1), hz = s.AudioHz, freq = s.FrequencyHz,
            msg = s.Message, call = s.Sender, grid = s.Grid, lat, lon,
            km = s.DistanceKm is double k ? Math.Round(k) : (double?)null,
            forMe = first == me, cq = s.Message.StartsWith("CQ ", StringComparison.Ordinal),
        };
        lock (_lock)
        {
            _total++;
            _recent.AddLast(dto);
            while (_recent.Count > 500) _recent.RemoveFirst();
            if (s.Sender is not null)
            {
                if (!_heard.TryGetValue(s.Band.Name, out var d)) _heard[s.Band.Name] = d = new();
                d[s.Sender] = DateTime.UtcNow;
            }
        }
        _psk?.Add(s);
        Broadcast(dto);
        BroadcastStatus();
    }

    private void SetLog(string m) { _log = m; BroadcastStatus(); }

    public object Status()
    {
        var now = DateTime.UtcNow;
        Dictionary<string, int> perBand;
        int stations;
        lock (_lock)
        {
            foreach (var d in _heard.Values)
                foreach (var k in d.Where(kv => now - kv.Value > TimeSpan.FromMinutes(30)).Select(kv => kv.Key).ToList())
                    d.Remove(k);
            perBand = _heard.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            stations = _heard.Values.SelectMany(x => x.Keys).Distinct().Count();
        }
        return new
        {
            t = "status", running = Running, band = CurrentBand, call = Settings.MyCall, grid = Settings.MyGrid,
            stations, total = _total, perBand, log = _log,
        };
    }

    public object Hello()
    {
        object[] recent;
        lock (_lock) recent = _recent.ToArray();
        return new
        {
            t = "hello",
            version = typeof(SkimmerHost).Assembly.GetName().Version?.ToString(3),
            bands = Bands.All.Select(b => new { name = b.Name, color = b.Color, dial = b.DialHz }),
            settings = Settings,
            recent,
        };
    }

    // ---------------- WebSocket ----------------

    public async Task HandleClientAsync(WebSocket ws, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var ch = Channel.CreateBounded<(bool, byte[])>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        _clients[id] = ch;
        try
        {
            await SendAsync(ws, false, Encode(Hello()), ct);
            await SendAsync(ws, false, Encode(Status()), ct);
            var reader = Task.Run(async () =>
            {
                var buf = new byte[1024];
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                }
            }, ct);
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var readTask = ch.Reader.ReadAsync(ct).AsTask();
                var done = await Task.WhenAny(readTask, reader);
                if (done == reader) break;
                var (bin, data) = await readTask;
                await SendAsync(ws, bin, data, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _clients.TryRemove(id, out _);
            if (ws.State == WebSocketState.Open)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }

    private static Task SendAsync(WebSocket ws, bool binary, byte[] data, CancellationToken ct) =>
        ws.SendAsync(data, binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, ct);

    private static byte[] Encode(object o) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o, Json));

    private void Broadcast(object o) => Broadcast(false, Encode(o));
    private void BroadcastStatus() => Broadcast(Status());

    private void Broadcast(bool binary, byte[] data)
    {
        foreach (var ch in _clients.Values) ch.Writer.TryWrite((binary, data));
    }

    public void Dispose() => Stop();
}
