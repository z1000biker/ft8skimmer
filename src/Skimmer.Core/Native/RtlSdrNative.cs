using System.Runtime.InteropServices;

namespace Skimmer.Core.Native;

/// <summary>
/// P/Invoke για το librtlsdr. Στα Windows χρειάζεται το rtlsdr.dll (και libusb-1.0.dll)
/// από τις εκδόσεις του rtl-sdr-blog, το μόνο που χειρίζεται σωστά τον upconverter HF του V4.
/// </summary>
internal static partial class RtlSdrNative
{
    private const string Lib = "rtlsdr";

    [LibraryImport(Lib)] internal static partial uint rtlsdr_get_device_count();
    [LibraryImport(Lib)] internal static partial IntPtr rtlsdr_get_device_name(uint index);
    [LibraryImport(Lib)] internal static partial int rtlsdr_open(out IntPtr dev, uint index);
    [LibraryImport(Lib)] internal static partial int rtlsdr_close(IntPtr dev);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_center_freq(IntPtr dev, uint freq);
    [LibraryImport(Lib)] internal static partial uint rtlsdr_get_center_freq(IntPtr dev);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_freq_correction(IntPtr dev, int ppm);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_sample_rate(IntPtr dev, uint rate);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_tuner_gain_mode(IntPtr dev, int manual);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_tuner_gain(IntPtr dev, int gainTenthsDb);
    [LibraryImport(Lib)] internal static partial int rtlsdr_get_tuner_gains(IntPtr dev, int[]? gains);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_agc_mode(IntPtr dev, int on);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_direct_sampling(IntPtr dev, int on);
    [LibraryImport(Lib)] internal static partial int rtlsdr_set_bias_tee(IntPtr dev, int on);
    [LibraryImport(Lib)] internal static partial int rtlsdr_reset_buffer(IntPtr dev);
    [LibraryImport(Lib)] internal static unsafe partial int rtlsdr_read_sync(IntPtr dev, byte* buf, int len, out int nRead);
}
