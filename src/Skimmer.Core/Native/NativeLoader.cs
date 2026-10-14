using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Skimmer.Core.Native;

/// <summary>
/// Φορτώνει το rtlsdr ΜΟΝΟ από τον φάκελο της εφαρμογής.
/// Χωρίς αυτό τα Windows ψάχνουν και στο PATH και μπορεί να πιάσουν άλλη έκδοση
/// (π.χ. του SatDump), ασύμβατη με το libusb που βρίσκεται δίπλα της.
/// </summary>
internal static class NativeLoader
{
    [ModuleInitializer]
    internal static void Init() =>
        NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (name != "rtlsdr") return IntPtr.Zero;
        string dir = AppContext.BaseDirectory;
        string[] files = OperatingSystem.IsWindows()
            ? ["rtlsdr.dll"]
            : ["librtlsdr.so.0", "librtlsdr.so", "librtlsdr.dylib"];
        foreach (var f in files)
        {
            var full = Path.Combine(dir, f);
            if (File.Exists(full)) return NativeLibrary.Load(full);
        }
        if (OperatingSystem.IsWindows())
            throw new DllNotFoundException(
                $"rtlsdr.dll not found in {dir}. Copy x64/rtlsdr.dll from rtl-sdr-blog Release.zip (v1.3.6) there.");
        // Linux/macOS: βιβλιοθήκη του συστήματος (τα πακέτα χρόνου εκτέλεσης δεν έχουν το σκέτο librtlsdr.so)
        foreach (var f in new[] { "librtlsdr.so.2", "librtlsdr.so.0", "librtlsdr.so", "librtlsdr.dylib", "librtlsdr.0.dylib" })
            if (NativeLibrary.TryLoad(f, out var h)) return h;
        throw new DllNotFoundException("librtlsdr not found. Use the bundled copy or install librtlsdr (2.0.1 or newer for V4 support).");
    }
}
