using System.Globalization;
using Skimmer.Core;
using Skimmer.Web;

// RtlFt8Skimmer για Linux: διακομιστής με περιβάλλον στον browser.
//   rtlft8skimmer [--port 8073] [--bind 0.0.0.0] [--start]
//   rtlft8skimmer --iq-file capture.u8 --iq-start 2026-10-14T12:00:00Z   (αναπαραγωγή χωρίς δέκτη)

string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        rtlft8skimmer - multiband FT8 skimmer for the RTL-SDR Blog V4

          --port N        HTTP port (default 8073)
          --bind ADDR     listen address (default 0.0.0.0, use 127.0.0.1 for local only)
          --start         start receiving immediately with the saved settings
          --iq-file PATH  play back a raw 8-bit IQ file at 960 kS/s instead of the dongle
          --iq-start UTC  UTC time of the first sample in --iq-file
          --iq-paced      play the IQ file back in real time

        Settings: ~/.config/rtlft8skimmer/settings.json (also editable in the web page)
        """);
    return;
}

int port = int.TryParse(Arg("--port"), out var p) ? p : 8073;
string bind = Arg("--bind") ?? "0.0.0.0";

Func<WebSettings, IIqSource>? factory = null;
if (Arg("--iq-file") is { } iqFile)
{
    var start = Arg("--iq-start") is { } s
        ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
        : DateTime.UtcNow;
    bool paced = args.Contains("--iq-paced");
    factory = _ => new RawIqFileSource(iqFile, start, paced);
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://{bind}:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(new SkimmerHost(factory));
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

string page;
using (var res = typeof(SkimmerHost).Assembly.GetManifestResourceStream("index.html")!)
using (var rd = new StreamReader(res)) page = rd.ReadToEnd();

app.MapGet("/", () => Results.Content(page, "text/html; charset=utf-8"));
app.MapGet("/api/status", (SkimmerHost h) => Results.Json(h.Status(), SkimmerHost.Json));
app.MapGet("/api/settings", (SkimmerHost h) => Results.Json(h.Settings, SkimmerHost.Json));
app.MapPost("/api/settings", (SkimmerHost h, WebSettings s) =>
{
    try { h.UpdateSettings(s); return Results.Ok(); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/start", (SkimmerHost h) =>
{
    try { h.Start(); return Results.Ok(); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/stop", (SkimmerHost h) => { h.Stop(); return Results.Ok(); });
app.Map("/ws", async (HttpContext ctx, SkimmerHost h) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await h.HandleClientAsync(ws, ctx.RequestAborted);
});

var host = app.Services.GetRequiredService<SkimmerHost>();
if (args.Contains("--start"))
{
    try { host.Start(); }
    catch (Exception ex) { Console.Error.WriteLine("Could not start: " + ex.Message); }
}
app.Lifetime.ApplicationStopping.Register(host.Stop);

Console.WriteLine($"RtlFt8Skimmer {typeof(SkimmerHost).Assembly.GetName().Version?.ToString(3)} - open http://{(bind == "0.0.0.0" ? "localhost" : bind)}:{port}/");
app.Run();
