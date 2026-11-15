using System.Text;

namespace BooksDb.Data;

/// <summary>
/// ISBN cleaning, validation, and normalization (ISBN-10 ⇄ ISBN-13).
/// Pure functions, no I/O. Used authoritatively by the desktop at capture ingest/commit
/// (store both forms, dedupe on the canonical 13) and optionally by the phone to validate
/// a manually-typed ISBN on the spot.
/// </summary>
public static class Isbn
{
    /// <summary>Strip separators; keep digits and a trailing X (uppercased). Null if nothing usable.</summary>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '0' and <= '9') sb.Append(ch);
            else if (ch is 'X' or 'x') sb.Append('X');
            // hyphens, spaces, and anything else are dropped
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>True if the input cleans to a valid ISBN-10 or ISBN-13.</summary>
    public static bool IsValid(string? raw)
    {
        var s = Clean(raw);
        return s is not null && (IsValid10(s) || IsValid13(s));
    }

    /// <summary>Normalize any valid ISBN to ISBN-13 (the canonical dedupe key). Null if invalid.</summary>
    public static string? To13(string? raw)
    {
        var s = Clean(raw);
        if (s is null) return null;
        if (IsValid13(s)) return s;
        if (IsValid10(s)) return From10To13(s);
        return null;
    }

    /// <summary>
    /// The ISBN-10 form, if one exists. Null when the input is invalid, or when it is a
    /// 979-prefixed ISBN-13 (those have no ISBN-10 equivalent).
    /// </summary>
    public static string? To10(string? raw)
    {
        var s = Clean(raw);
        if (s is null) return null;
        if (IsValid10(s)) return s;
        if (IsValid13(s) && s.StartsWith("978")) return From13To10(s);
        return null;
    }

    // ---- internals ----

    private static bool IsValid10(string s)
    {
        if (s.Length != 10) return false;
        int sum = 0;
        for (int i = 0; i < 10; i++)
        {
            int v;
            if (s[i] is >= '0' and <= '9') v = s[i] - '0';
            else if (s[i] == 'X' && i == 9) v = 10;   // X is only legal as the check digit
            else return false;
            sum += (10 - i) * v;                       // weights 10,9,…,1
        }
        return sum % 11 == 0;
    }

    private static bool IsValid13(string s)
    {
        if (s.Length != 13) return false;
        int sum = 0;
        for (int i = 0; i < 13; i++)
        {
            if (s[i] is < '0' or > '9') return false;  // no X anywhere in a 13
            sum += (i % 2 == 0 ? 1 : 3) * (s[i] - '0'); // weights 1,3,1,3,…
        }
        return sum % 10 == 0;
    }

    private static string From10To13(string isbn10)
    {
        var core = "978" + isbn10[..9];                // drop the 10's check digit → 12 digits
        return core + Ean13CheckDigit(core);
    }

    private static string From13To10(string isbn13)
    {
        var core9 = isbn13.Substring(3, 9);            // drop "978", keep the next 9
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (10 - i) * (core9[i] - '0');  // weights 10,9,…,2
        int check = (11 - (sum % 11)) % 11;
        return core9 + (check == 10 ? "X" : check.ToString());
    }

    private static char Ean13CheckDigit(string twelveDigits)
    {
        int sum = 0;
        for (int i = 0; i < 12; i++) sum += (i % 2 == 0 ? 1 : 3) * (twelveDigits[i] - '0');
        int check = (10 - (sum % 10)) % 10;
        return (char)('0' + check);
    }
}