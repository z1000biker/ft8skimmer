using System.Runtime.InteropServices;
using Skimmer.Core.Native;

namespace Skimmer.Core;

public sealed class RtlSdrSource : IIqSource
{
    private IntPtr _dev;
    private readonly object _ctl = new();

    public RtlSdrSource(uint deviceIndex = 0, int sampleRate = 960_000)
    {
        DeviceIndex = deviceIndex;
        SampleRate = sampleRate;
    }

    public uint DeviceIndex { get; }
    public int SampleRate { get; }
    public bool IsRealtime => true;
    public DateTime VirtualStartUtc => DateTime.MinValue;

    /// <summary>Κέρδος σε δέκατα dB· null = αυτόματο (AGC του tuner).</summary>
    public int? GainTenthsDb { get; set; } = 280;
    public int PpmCorrection { get; set; }
    public bool BiasTee { get; set; }

    public static IReadOnlyList<string> ListDevices()
    {
        var n = RtlSdrNative.rtlsdr_get_device_count();
        var list = new List<string>();
        for (uint i = 0; i < n; i++)
            list.Add(Marshal.PtrToStringUTF8(RtlSdrNative.rtlsdr_get_device_name(i)) ?? $"#{i}");
        return list;
    }

    public void Open()
    {
        if (RtlSdrNative.rtlsdr_get_device_count() == 0)
            throw new InvalidOperationException("No RTL-SDR found. Check the WinUSB driver (Zadig).");
        Check(RtlSdrNative.rtlsdr_open(out _dev, DeviceIndex), "open");
        Check(RtlSdrNative.rtlsdr_set_sample_rate(_dev, (uint)SampleRate), "sample rate");
        // Το V4 δεν χρειάζεται direct sampling: ο οδηγός του rtl-sdr-blog ενεργοποιεί μόνος του τον upconverter κάτω από 28,8 MHz.
        RtlSdrNative.rtlsdr_set_direct_sampling(_dev, 0);
        if (PpmCorrection != 0) RtlSdrNative.rtlsdr_set_freq_correction(_dev, PpmCorrection);
        if (GainTenthsDb is int g)
        {
            RtlSdrNative.rtlsdr_set_tuner_gain_mode(_dev, 1);
            RtlSdrNative.rtlsdr_set_tuner_gain(_dev, NearestGain(g));
        }
        else RtlSdrNative.rtlsdr_set_tuner_gain_mode(_dev, 0);
        RtlSdrNative.rtlsdr_set_agc_mode(_dev, 0);
        RtlSdrNative.rtlsdr_set_bias_tee(_dev, BiasTee ? 1 : 0);
        RtlSdrNative.rtlsdr_reset_buffer(_dev);
    }

    private int NearestGain(int want)
    {
        int n = RtlSdrNative.rtlsdr_get_tuner_gains(_dev, null);
        if (n <= 0) return want;
        var gains = new int[n];
        RtlSdrNative.rtlsdr_get_tuner_gains(_dev, gains);
        return gains.MinBy(x => Math.Abs(x - want));
    }

    public void SetCenterFrequency(long hz)
    {
        lock (_ctl) Check(RtlSdrNative.rtlsdr_set_center_freq(_dev, (uint)hz), "set freq");
    }

    public unsafe int Read(byte[] buffer)
    {
        fixed (byte* p = buffer)
        {
            int r = RtlSdrNative.rtlsdr_read_sync(_dev, p, buffer.Length, out int n);
            if (r < 0) throw new IOException($"rtlsdr_read_sync: {r}");
            return n;
        }
    }

    public void Dispose()
    {
        if (_dev != IntPtr.Zero) { RtlSdrNative.rtlsdr_close(_dev); _dev = IntPtr.Zero; }
    }

    private static void Check(int r, string what)
    {
        if (r < 0) throw new InvalidOperationException($"RTL-SDR {what} failed ({r}).");
    }
}
