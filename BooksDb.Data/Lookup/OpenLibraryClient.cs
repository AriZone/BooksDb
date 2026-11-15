using System.Text.Json;
using System.Text.RegularExpressions;

namespace BooksDb.Data.Lookup;

/// <summary>
/// Open Library metadata source. ISBN-only (the Books "data" endpoint is keyed by
/// identifier — there is no useful title search), keyless, and returns the shared
/// BnResult shape so it drops straight into the same picker / ApplyBnResult flow as
/// BN and Google. Coverage is uneven, but it often carries a place of publication
/// (City) that the other two lack.
/// </summary>
public sealed class OpenLibraryClient
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient
        {
            BaseAddress = new Uri("https://openlibrary.org/"),
            Timeout     = TimeSpan.FromSeconds(10),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("BooksDb/1.0 (personal catalog)");
        return h;
    }

    public async Task<IReadOnlyList<BnResult>> SearchByIsbnAsync(string isbn, CancellationToken ct = default)
    {
        var clean = Digits(isbn).ToUpperInvariant();
        if (clean.Length is not (10 or 13)) return Array.Empty<BnResult>();

        var bibkey = $"ISBN:{clean}";
        var json = await Http.GetStringAsync(
            $"api/books?bibkeys={Uri.EscapeDataString(bibkey)}&jscmd=data&format=json", ct);

        using var doc = JsonDocument.Parse(json);
        // Not found → Open Library returns "{}" (200), so the bibkey is simply absent.
        if (!doc.RootElement.TryGetProperty(bibkey, out var data) || data.ValueKind != JsonValueKind.Object)
            return Array.Empty<BnResult>();

        // The "data" view carries no language; the edition record does. Read it (defaults to English).
        var language = await ReadLanguageAsync(clean, ct);

        return new[] { MapData(data, language) };
    }

    private static BnResult MapData(JsonElement d, string? languageName)
    {
        var title = Str(d, "title");
        var sub   = Str(d, "subtitle");
        var full  = !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(sub)
            ? $"{title} : {sub}" : (title ?? sub ?? "");

        // authors are an array of { name, url } objects.
        var contribs = new List<BnContributor>();
        if (d.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
            foreach (var a in authors.EnumerateArray())
            {
                var name = NameOf(a);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var (last, first) = SplitName(name!);
                contribs.Add(new BnContributor { LastName = last, FirstName = first, RoleName = "Author" });
            }

        var pages = d.TryGetProperty("number_of_pages", out var np) && np.ValueKind == JsonValueKind.Number
            ? np.GetInt32() : (int?)null;

        string? isbn13 = null, isbn10 = null;
        if (d.TryGetProperty("identifiers", out var ids) && ids.ValueKind == JsonValueKind.Object)
        {
            isbn13 = FirstId(ids, "isbn_13");
            isbn10 = FirstId(ids, "isbn_10");
        }

        var cats = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (d.TryGetProperty("subjects", out var subjects) && subjects.ValueKind == JsonValueKind.Array)
            foreach (var s in subjects.EnumerateArray())
            {
                var name = NameOf(s);
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name!)) cats.Add(name!);
            }

        return new BnResult
        {
            Title        = full,
            Publisher    = FirstName(d, "publishers"),
            City         = FirstName(d, "publish_places"),
            Year         = ParseYear(Str(d, "publish_date")),
            Pages        = pages,
            Isbn13       = isbn13,
            Isbn10       = isbn10,
            LanguageName = languageName,
            Contributors = contribs,
            Categories   = cats,
        };
    }

    // Language isn't part of the Books "data" view, so read it from the edition record
    // (/isbn/{isbn}.json → languages: [{ "key": "/languages/eng" }]). Open Library omits it
    // for most English editions, so a missing language defaults to English; a recorded but
    // unrecognised language is left alone (so the form's current language stands).
    private static async Task<string?> ReadLanguageAsync(string isbn, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync($"isbn/{isbn}.json", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("languages", out var langs) &&
                langs.ValueKind == JsonValueKind.Array && langs.GetArrayLength() > 0)
            {
                foreach (var l in langs.EnumerateArray())
                {
                    var mapped = MapLang(LastSegment(Str(l, "key")));
                    if (mapped is not null) return mapped;   // recorded and recognised
                }
                return null;   // recorded but not one we map → don't guess, leave form as-is
            }
        }
        catch
        {
            // network / parse problem — fall through to the English default
        }
        return "English";   // no language recorded → assume English
    }

    // ---------- helpers ----------

    // First "name" from an array-of-objects property (publishers / publish_places).
    private static string? FirstName(JsonElement parent, string prop)
    {
        if (parent.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
            {
                var name = NameOf(e);
                if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();
            }
        return null;
    }

    // Accept either a bare string or a { "name": ... } object.
    private static string? NameOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Object => Str(e, "name"),
        _                    => null,
    };

    // First value from identifiers.<kind> (array of strings).
    private static string? FirstId(JsonElement identifiers, string kind)
    {
        if (identifiers.TryGetProperty(kind, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                {
                    var v = Digits(e.GetString() ?? "");
                    if (v.Length > 0) return v;
                }
        return null;
    }

    private static (string last, string? first) SplitName(string name)
    {
        name = name.Trim();

        var comma = name.IndexOf(',');
        if (comma >= 0)   // "Last, First"
        {
            var last  = name[..comma].Trim();
            var first = name[(comma + 1)..].Trim();
            return (last, string.IsNullOrWhiteSpace(first) ? null : first);
        }

        var sp = name.LastIndexOf(' ');
        if (sp < 0) return (name, null);
        var f = name[..sp].Trim();
        return (name[(sp + 1)..].Trim(), string.IsNullOrWhiteSpace(f) ? null : f);
    }

    private static string? LastSegment(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim('/').Split('/').Last();

    private static string? MapLang(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return code.Trim().ToLowerInvariant() switch
        {
            "pl" or "pol"          => "Polish",
            "en" or "eng"          => "English",
            "de" or "ger" or "deu" => "German",
            "fr" or "fre" or "fra" => "French",
            "ru" or "rus"          => "Russian",
            _                      => null,
        };
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