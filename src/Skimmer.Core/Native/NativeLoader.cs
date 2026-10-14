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
                $"Δεν βρέθηκε το rtlsdr.dll στον φάκελο {dir}. Αντιγράψτε εκεί τα rtlsdr.dll και libusb-1.0.dll (x64) του rtl-sdr-blog.");
        return IntPtr.Zero; // Linux/macOS: κανονική αναζήτηση συστήματος
    }
}
