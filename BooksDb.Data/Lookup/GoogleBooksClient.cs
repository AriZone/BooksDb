using System.Text.Json;
using System.Text.RegularExpressions;

namespace BooksDb.Data.Lookup;

/// <summary>
/// Google Books metadata source — primary for English-language books.
/// Mirrors BnClient (static HttpClient, returns the shared BnResult shape so it
/// drops straight into the existing picker / ApplyBnResult flow).
/// Keyless: the volumes search endpoint needs no API key (IP-throttled quota).
/// </summary>
public sealed class GoogleBooksClient
{
    private static readonly HttpClient Http = CreateHttp();
    
    private readonly string _key;

    public GoogleBooksClient(SettingsStore settings)
    {
        _key = settings.Current.GoogleBooksApiKey ?? "";
        //Console.Error.WriteLine($"[GoogleBooks] key loaded: {(_key.Length == 0 ? "NONE" : $"{_key.Length} chars")}");
    }

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient
        {
            BaseAddress = new Uri("https://www.googleapis.com/"),
            Timeout     = TimeSpan.FromSeconds(15),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("BooksDb/1.0 (personal catalog)");
        return h;
    }
    
    public Task<IReadOnlyList<BnResult>> SearchByIsbnAsync(string isbn, CancellationToken ct = default)
        => QueryAsync($"q=isbn:{Uri.EscapeDataString(Digits(isbn))}", ct);

    public Task<IReadOnlyList<BnResult>> SearchByTitleAsync(string title, string? author, CancellationToken ct = default)
    {
        var q = "intitle:" + title.Trim();
        if (!string.IsNullOrWhiteSpace(author))
            q += " inauthor:" + author.Trim();
        return QueryAsync($"q={Uri.EscapeDataString(q)}", ct);
    }

    private async Task<IReadOnlyList<BnResult>> QueryAsync(string query, CancellationToken ct)
    {
        var key  = string.IsNullOrWhiteSpace(_key) ? "" : $"&key={Uri.EscapeDataString(_key)}";
        var json = await Http.GetStringAsync($"books/v1/volumes?{query}&maxResults=10{key}", ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<BnResult>();

        var list = new List<BnResult>();
        foreach (var item in items.EnumerateArray())
            if (item.TryGetProperty("volumeInfo", out var vi) && vi.ValueKind == JsonValueKind.Object)
                list.Add(MapVolume(vi));
        return list;
    }

    private static BnResult MapVolume(JsonElement vi)
    {
        var title = Str(vi, "title");
        var sub   = Str(vi, "subtitle");
        var full  = !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(sub)
            ? $"{title} : {sub}" : (title ?? sub ?? "");

        var pages = vi.TryGetProperty("pageCount", out var pc) && pc.ValueKind == JsonValueKind.Number
            ? pc.GetInt32() : (int?)null;

        string? isbn13 = null, isbn10 = null;
        if (vi.TryGetProperty("industryIdentifiers", out var ids) && ids.ValueKind == JsonValueKind.Array)
            foreach (var id in ids.EnumerateArray())
            {
                var type = Str(id, "type");
                var val  = Digits(Str(id, "identifier") ?? "");
                if (type == "ISBN_13" && isbn13 is null && val.Length == 13) isbn13 = val;
                else if (type == "ISBN_10" && isbn10 is null && val.Length == 10) isbn10 = val;
            }

        var contribs = new List<BnContributor>();
        if (vi.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
            foreach (var a in authors.EnumerateArray())
            {
                var name = a.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var (last, first) = SplitName(name);
                contribs.Add(new BnContributor { LastName = last, FirstName = first, RoleName = "Author" });
            }

        var cats = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (vi.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
            foreach (var c in categories.EnumerateArray())
                foreach (var part in (c.GetString() ?? "")
                         .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (seen.Add(part)) cats.Add(part);

        return new BnResult
        {
            Title        = full,
            Publisher    = Str(vi, "publisher"),
            City         = null,                       // Google Books carries no place of publication
            Year         = ParseYear(Str(vi, "publishedDate")),
            Pages        = pages,
            Isbn13       = isbn13,
            Isbn10       = isbn10,
            LanguageName = MapLang(Str(vi, "language")),
            Contributors = DedupeAuthors(contribs),
            Categories   = cats,
        };
    }

    // ---------- helpers ----------
    
    // Post-nominal credentials Google sometimes appends — dropped (not part of identity).
    private static readonly HashSet<string> Credentials = new(StringComparer.OrdinalIgnoreCase)
    {
        "phd", "md", "dphil", "dds", "do", "rn", "msc", "ma", "ba", "bsc", "llb", "llm",
        "esq", "edd", "psyd",
    };

    // Generational suffixes — kept and attached to the surname (they disambiguate real people).
    private static readonly HashSet<string> Generational = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "sr", "ii", "iii", "iv",
    };

    private static (string last, string? first) SplitName(string name)
    {
        name = name.Trim();
        string? gen = null;

        // A comma here is usually a trailing credential ("Jacobs, PhD") or generational suffix
        // ("Davis, Jr."), not a "Last, First" inversion. Classify the tail.
        var comma = name.IndexOf(',');
        if (comma >= 0)
        {
            var head = name[..comma].Trim();
            var tail = name[(comma + 1)..].Trim();
            if (IsCredential(tail))        name = head;                                  // drop
            else if (IsGenerational(tail)) { name = head; gen = CanonicalSuffix(tail); } // keep
            else return (head, string.IsNullOrWhiteSpace(tail) ? null : tail);           // genuine "Last, First"
        }

        // Peel trailing suffix tokens in natural order ("Sammy Davis Jr.", "John Smith PhD").
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count >= 2)
        {
            var t = parts[^1];
            if (gen is null && IsGenerational(t)) { gen = CanonicalSuffix(t); parts.RemoveAt(parts.Count - 1); }
            else if (IsCredential(t))             { parts.RemoveAt(parts.Count - 1); }
            else break;
        }
        name = string.Join(' ', parts);

        if (name.Length == 0) return (gen ?? "", null);

        var sp    = name.LastIndexOf(' ');
        var last  = sp < 0 ? name : name[(sp + 1)..].Trim();
        var first = sp < 0 ? null : name[..sp].Trim();
        if (gen is not null) last = $"{last} {gen}";
        return (last, string.IsNullOrWhiteSpace(first) ? null : first);
    }

    private static bool   IsCredential(string t)   => !t.Contains(' ') && Credentials.Contains(LettersLower(t));
    private static bool   IsGenerational(string t) => !t.Contains(' ') && Generational.Contains(LettersLower(t));
    private static string LettersLower(string t)   => new(t.Where(char.IsLetter).ToArray());

    private static string CanonicalSuffix(string t) => LettersLower(t).ToLowerInvariant() switch
    {
        "jr" => "Jr.", "sr" => "Sr.", "ii" => "II", "iii" => "III", "iv" => "IV", _ => t,
    };

    private static string? MapLang(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToLowerInvariant();
        return c switch
        {
            "pl" or "pol" => "Polish",
            "en" or "eng" => "English",
            "de" or "ger" or "deu" => "German",
            "fr" or "fre" or "fra" => "French",
            "ru" or "rus" => "Russian",
            _ => null,
        };
    }
    
    // Google's author lists sometimes include a bare-surname duplicate of a fuller name
    // (e.g. ["John E. Mack", "Mack"]) or an exact repeat. Drop a surname-only entry when a
    // same-surname entry that has a first name is present, plus any exact display duplicates.
    private static List<BnContributor> DedupeAuthors(List<BnContributor> authors)
    {
        if (authors.Count < 2) return authors;

        var fullerSurnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in authors)
            if (!string.IsNullOrWhiteSpace(a.FirstName))
                fullerSurnames.Add(a.LastName);

        var result = new List<BnContributor>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in authors)
        {
            if (string.IsNullOrWhiteSpace(a.FirstName) && fullerSurnames.Contains(a.LastName))
                continue;                       // bare surname fragment of a fuller name → drop
            if (!seen.Add(a.Display))
                continue;                       // exact duplicate → drop
            result.Add(a);
        }
        return result;
    }

    private static int? ParseYear(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"\d{4}");
        return m.Success && int.TryParse(m.Value, out var y) ? y : null;
    }

    private static string Digits(string s) => new(s.Where(c => char.IsDigit(c) || c is 'X' or 'x').ToArray());

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}