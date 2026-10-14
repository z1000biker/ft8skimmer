using System.Text.RegularExpressions;

namespace Skimmer.Core;

public static partial class Maidenhead
{
    [GeneratedRegex("^[A-R]{2}[0-9]{2}([A-X]{2})?$", RegexOptions.IgnoreCase)]
    private static partial Regex GridRx();

    public static bool IsValid(string? g) => g is not null && GridRx().IsMatch(g);

    /// <summary>Κέντρο του τετραγώνου σε μοίρες (lat, lon).</summary>
    public static (double Lat, double Lon) ToLatLon(string grid)
    {
        if (!IsValid(grid)) throw new ArgumentException($"Μη έγκυρο locator: {grid}");
        grid = grid.ToUpperInvariant();
        double lon = (grid[0] - 'A') * 20 - 180 + (grid[2] - '0') * 2;
        double lat = (grid[1] - 'A') * 10 - 90 + (grid[3] - '0');
        if (grid.Length >= 6)
        {
            lon += (grid[4] - 'A') * (2.0 / 24) + 1.0 / 24;
            lat += (grid[5] - 'A') * (1.0 / 24) + 0.5 / 24;
        }
        else { lon += 1; lat += 0.5; }
        return (lat, lon);
    }

    public static double DistanceKm(string a, string b)
    {
        var (la1, lo1) = ToLatLon(a); var (la2, lo2) = ToLatLon(b);
        double r = Math.PI / 180, dLa = (la2 - la1) * r, dLo = (lo2 - lo1) * r;
        double h = Math.Sin(dLa / 2) * Math.Sin(dLa / 2) +
                   Math.Cos(la1 * r) * Math.Cos(la2 * r) * Math.Sin(dLo / 2) * Math.Sin(dLo / 2);
        return 6371 * 2 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
