using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Skimmer.Core;

/// <summary>
/// Αποστολή αναφορών στο PSKReporter (IPFIX πάνω από UDP, βλ. pskreporter.info/pskdev.html).
/// Μαζεύει spots και τα στέλνει κάθε 5 λεπτά· διπλότυπα (ίδιος σταθμός, ίδια ζώνη) μέσα στο παράθυρο αγνοούνται.
/// </summary>
public sealed class PskReporterClient : IDisposable
{
    public const string Host = "report.pskreporter.info";
    public const int LivePort = 4739, TestPort = 14739;

    private readonly string _rxCall, _rxGrid, _software;
    private readonly int _port;
    private readonly uint _domainId = (uint)Random.Shared.Next();
    private uint _sequence;
    private readonly object _lock = new();
    private readonly Dictionary<(string, string), Spot> _pending = new();
    private readonly Dictionary<(string, string), DateTime> _sent = new();
    private readonly UdpClient _udp = new();
    private readonly Timer _timer;

    public event Action<string>? Log;

    public PskReporterClient(string rxCall, string rxGrid, string software, bool testServer, TimeSpan? interval = null)
    {
        _rxCall = rxCall.ToUpperInvariant(); _rxGrid = rxGrid; _software = software;
        _port = testServer ? TestPort : LivePort;
        var iv = interval ?? TimeSpan.FromMinutes(5);
        _timer = new Timer(_ => Flush(), null, iv, iv);
    }

    public void Add(Spot s)
    {
        if (s.Sender is null) return;
        var key = (s.Sender, s.Band.Name);
        lock (_lock)
        {
            if (_sent.TryGetValue(key, out var t) && DateTime.UtcNow - t < TimeSpan.FromMinutes(5)) return;
            // κρατά τη νεότερη αναφορά, συμπληρώνοντας locator αν λείπει
            if (_pending.TryGetValue(key, out var old) && s.Grid is null) s = s with { Grid = old.Grid };
            _pending[key] = s;
        }
    }

    public void Flush()
    {
        List<Spot> batch;
        lock (_lock)
        {
            batch = _pending.Values.ToList();
            _pending.Clear();
            var now = DateTime.UtcNow;
            foreach (var s in batch) _sent[(s.Sender!, s.Band.Name)] = now;
            foreach (var k in _sent.Where(kv => now - kv.Value > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList())
                _sent.Remove(k);
        }
        if (batch.Count == 0) return;
        try
        {
            foreach (var pkt in BuildPackets(batch))
                _udp.Send(pkt, pkt.Length, Host, _port);
            Log?.Invoke($"PSKReporter: στάλθηκαν {batch.Count} αναφορές.");
        }
        catch (Exception ex) { Log?.Invoke($"PSKReporter: {ex.Message}"); }
    }

    // ---------- IPFIX ----------

    private static readonly byte[] RxTemplate =
    [
        0x00, 0x03, 0x00, 0x24, 0x99, 0x92, 0x00, 0x03, 0x00, 0x00,
        0x80, 0x02, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F, // receiverCallsign
        0x80, 0x04, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F, // receiverLocator
        0x80, 0x08, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F, // decodingSoftware
        0x00, 0x00,
    ];

    private static readonly byte[] TxTemplate =
    [
        0x00, 0x02, 0x00, 0x3C, 0x99, 0x93, 0x00, 0x07,
        0x80, 0x01, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F, // senderCallsign
        0x80, 0x05, 0x00, 0x04, 0x00, 0x00, 0x76, 0x8F, // frequency (4 bytes)
        0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x76, 0x8F, // sNR (1 byte)
        0x80, 0x0A, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F, // mode
        0x80, 0x03, 0xFF, 0xFF, 0x00, 0x00, 0x76, 0x8F, // senderLocator
        0x80, 0x0B, 0x00, 0x01, 0x00, 0x00, 0x76, 0x8F, // informationSource (1 byte)
        0x00, 0x96, 0x00, 0x04,                         // flowStartSeconds
    ];

    public IEnumerable<byte[]> BuildPackets(IReadOnlyList<Spot> spots)
    {
        const int maxPayload = 1300;
        var rxSet = DataSet(0x9992, w =>
        {
            Str(w, _rxCall); Str(w, _rxGrid); Str(w, _software);
        });

        int i = 0;
        while (i < spots.Count)
        {
            var records = new MemoryStream();
            int count = 0;
            while (i < spots.Count)
            {
                var r = SenderRecord(spots[i]);
                if (records.Length + r.Length > maxPayload && count > 0) break;
                records.Write(r); i++; count++;
            }
            var txSet = WrapSet(0x9993, records.ToArray());

            var body = new MemoryStream();
            body.Write(RxTemplate); body.Write(TxTemplate); body.Write(rxSet); body.Write(txSet);
            var payload = body.ToArray();

            var pkt = new byte[16 + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(pkt.AsSpan(0), 0x000A);
            BinaryPrimitives.WriteUInt16BigEndian(pkt.AsSpan(2), (ushort)pkt.Length);
            BinaryPrimitives.WriteUInt32BigEndian(pkt.AsSpan(4), (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            BinaryPrimitives.WriteUInt32BigEndian(pkt.AsSpan(8), _sequence);
            BinaryPrimitives.WriteUInt32BigEndian(pkt.AsSpan(12), _domainId);
            payload.CopyTo(pkt, 16);
            _sequence += (uint)(count + 1);
            yield return pkt;
        }
    }

    private static byte[] SenderRecord(Spot s)
    {
        var ms = new MemoryStream();
        Str(ms, s.Sender!);
        Span<byte> f = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(f, (uint)s.FrequencyHz); ms.Write(f);
        ms.WriteByte(unchecked((byte)(sbyte)Math.Clamp(s.Snr, -128, 127)));
        Str(ms, "FT8");
        Str(ms, s.Grid ?? "");
        ms.WriteByte(1); // αυτόματη εξαγωγή από αποκωδικοποίηση
        BinaryPrimitives.WriteUInt32BigEndian(f, (uint)new DateTimeOffset(s.SlotUtc, TimeSpan.Zero).ToUnixTimeSeconds());
        ms.Write(f);
        return ms.ToArray();
    }

    private static byte[] DataSet(ushort id, Action<MemoryStream> fill)
    {
        var ms = new MemoryStream(); fill(ms); return WrapSet(id, ms.ToArray());
    }

    private static byte[] WrapSet(ushort id, byte[] content)
    {
        int len = 4 + content.Length;
        int padded = (len + 3) & ~3;
        var r = new byte[padded];
        BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(0), id);
        BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(2), (ushort)padded);
        content.CopyTo(r, 4);
        return r;
    }

    private static void Str(MemoryStream ms, string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        ms.WriteByte((byte)Math.Min(b.Length, 254));
        ms.Write(b, 0, Math.Min(b.Length, 254));
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
        _udp.Dispose();
    }
}
