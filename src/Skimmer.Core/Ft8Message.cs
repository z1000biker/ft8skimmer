using System.Text.RegularExpressions;

namespace Skimmer.Core;

/// <summary>Εξάγει το διακριτικό του αποστολέα και το locator από ένα κείμενο FT8.</summary>
public static partial class Ft8Message
{
    [GeneratedRegex(@"^(?=.*[0-9])(?=.*[A-Z])[A-Z0-9/]{3,12}$")]
    private static partial Regex CallRx();

    [GeneratedRegex(@"^[A-R]{2}[0-9]{2}$")]
    private static partial Regex Grid4Rx();

    public static bool IsCall(string t) => CallRx().IsMatch(t) && !IsGrid(t);
    public static bool IsGrid(string t) => Grid4Rx().IsMatch(t) && t != "RR73";

    public static (string? Sender, string? Grid) Parse(string message)
    {
        var tok = message.Trim().ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('<', '>'))
            .ToArray();
        if (tok.Length < 2) return (null, null);

        string? grid = IsGrid(tok[^1]) ? tok[^1] : null;

        if (tok[0] == "CQ" || tok[0] == "QRZ" || tok[0].StartsWith("CQ_"))
        {
            // CQ CALL GRID | CQ DX CALL GRID | CQ POTA CALL GRID
            string? sender = tok.Skip(1).Where(IsCall).LastOrDefault();
            return (sender, grid);
        }
        // CALL1 CALL2 ... : αποστολέας είναι ο δεύτερος
        if (tok.Length >= 2 && IsCall(tok[1])) return (tok[1], tok.Length >= 3 ? grid : null);
        return (null, null);
    }
}
