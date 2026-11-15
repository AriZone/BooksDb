using System.Globalization;
using System.Text;

namespace BooksDb.Data;

public static class PolishText
{
    // Lowercase and strip diacritics so search matches regardless of accents or case.
    //   "Łódź" -> "lodz",  "Żółw" -> "zolw",  "Lem" -> "lem"
    public static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        // ł / Ł don't decompose under NFD (the stroke isn't a combining mark), so map them explicitly.
        s = s.Replace('ł', 'l').Replace('Ł', 'L');

        var nfd = s.Normalize(NormalizationForm.FormD);
        var sb  = new StringBuilder(nfd.Length);
        foreach (var ch in nfd)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);

        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}