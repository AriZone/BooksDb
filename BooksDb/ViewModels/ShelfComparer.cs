namespace BooksDb.ViewModels;

// Physical-shelf order: column letter, then shelf number naturally (A1, A2…A9, A10, B1),
// then within a shelf: front (bare, or numbered -1/-2…-10) → top/flat (-0) → back (-x).
public sealed class ShelfComparer : System.Collections.Generic.IComparer<string?>
{
    public static readonly ShelfComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        bool ea = string.IsNullOrWhiteSpace(a), eb = string.IsNullOrWhiteSpace(b);
        if (ea || eb) return ea == eb ? 0 : (ea ? 1 : -1);          // unshelved sorts last

        var (aA, aN, aG, aO) = Parse(a!);
        var (bA, bN, bG, bO) = Parse(b!);
        int c = string.CompareOrdinal(aA, bA); if (c != 0) return c; // column letter
        c = aN.CompareTo(bN);                   if (c != 0) return c; // shelf number (10 > 9)
        c = aG.CompareTo(bG);                   if (c != 0) return c; // front < top < back
        c = aO.CompareTo(bO);                   if (c != 0) return c; // front ordinal (-10 > -9)
        return string.CompareOrdinal(a, b);                          // stable tiebreak
    }

    private static (string col, int num, int group, int ord) Parse(string shelf)
    {
        var s = shelf.Trim();
        string code = s, suffix = "";
        int dash = s.IndexOf('-');
        if (dash >= 0) { code = s[..dash]; suffix = s[(dash + 1)..]; }

        int i = 0; while (i < code.Length && !char.IsDigit(code[i])) i++;
        string col = code[..i].ToUpperInvariant();
        int num = int.TryParse(code[i..], out var n) ? n : 0;

        int group, ord;
        if (suffix.Length == 0)                   { group = 0; ord = 0; }   // bare front
        else if (int.TryParse(suffix, out var v)) { if (v == 0) { group = 1; ord = 0; }    // -0 top
                                                    else        { group = 0; ord = v; } }  // -1/-2 front
        else if (suffix.Equals("x", System.StringComparison.OrdinalIgnoreCase))
                                                  { group = 2; ord = 0; }   // -x back
        else                                      { group = 3; ord = 0; }   // unknown → after known
        return (col, num, group, ord);
    }
}