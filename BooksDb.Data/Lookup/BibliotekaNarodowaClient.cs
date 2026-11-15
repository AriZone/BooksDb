//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
//using System.Threading;
//using System.Threading.Tasks;

namespace BooksDb.Data.Lookup;

public sealed class BibliotekaNarodowaClient
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient
        {
            BaseAddress = new Uri("https://data.bn.org.pl/"),
            Timeout     = TimeSpan.FromSeconds(15),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("BooksDb/1.0 (personal catalog)");
        return h;
    }

    public Task<IReadOnlyList<BnResult>> SearchByIsbnAsync(string isbn, CancellationToken ct = default)
        => QueryAsync($"isbnIssn={Uri.EscapeDataString(Digits(isbn))}", ct);

    public Task<IReadOnlyList<BnResult>> SearchByTitleAsync(string title, string? author, CancellationToken ct = default)
    {
        var q = $"title={Uri.EscapeDataString(title.Trim())}&kind={Uri.EscapeDataString("książka")}";
        if (!string.IsNullOrWhiteSpace(author))
            q += $"&author={Uri.EscapeDataString(author.Trim())}";
        return QueryAsync(q, ct);
    }

    private async Task<IReadOnlyList<BnResult>> QueryAsync(string query, CancellationToken ct)
    {
        var json = await Http.GetStringAsync($"api/networks/bibs.json?{query}&limit=10", ct);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("bibs", out var bibs) || bibs.ValueKind != JsonValueKind.Array)
            return Array.Empty<BnResult>();

        var list = new List<BnResult>();
        foreach (var bib in bibs.EnumerateArray())
            list.Add(MapBib(bib));
        return list;
    }

    // ---------- MARC mapping ----------

    private sealed class MarcField
    {
        public string Tag = "";
        public string? Control;                                 // set for 00x control fields
        public List<(string Code, string Value)> Subfields = new();
        public string? Sub(string code) => Subfields.FirstOrDefault(s => s.Code == code).Value;
    }

    private static BnResult MapBib(JsonElement bib)
    {
        if (!bib.TryGetProperty("marc", out var marc) || marc.ValueKind != JsonValueKind.Object)
            return MapFromConvenience(bib);

        var fields = ParseFields(marc);

        // Title — 245 |a (: |b)
        var f245 = fields.FirstOrDefault(f => f.Tag == "245");
        var title = f245 is null ? "" : BuildTitle(f245);
        if (string.IsNullOrWhiteSpace(title))
            title = ConvenienceString(bib, "title") ?? "";

        // Original title — 246 with |i mentioning "oryginał"
        string? origTitle = null;
        foreach (var f in fields.Where(f => f.Tag == "246"))
        {
            var i = f.Sub("i");
            if (i is not null && i.ToLowerInvariant().Contains("oryginał"))
            {
                origTitle = StripPunct(f.Sub("a"));
                break;
            }
        }

        var edition = StripPunct(fields.FirstOrDefault(f => f.Tag == "250")?.Sub("a"));

        // Publisher / place / year — prefer 264, fall back to 260
        var pub = fields.FirstOrDefault(f => f.Tag == "264") ?? fields.FirstOrDefault(f => f.Tag == "260");
        var publisher = StripPunct(pub?.Sub("b"));
        var city      = StripPunct(pub?.Sub("a"));
        var year      = ParseYear(pub?.Sub("c")) ?? Year008(GetControl(fields, "008"));

        var pages = ParseFirstInt(fields.FirstOrDefault(f => f.Tag == "300")?.Sub("a"));

        string? isbn13 = null, isbn10 = null;
        foreach (var f in fields.Where(f => f.Tag == "020"))
        {
            var norm = NormalizeIsbn(f.Sub("a"));
            if (norm.Length == 13 && isbn13 is null) isbn13 = norm;
            else if (norm.Length == 10 && isbn10 is null) isbn10 = norm;
        }

        var f041 = fields.FirstOrDefault(f => f.Tag == "041");
        var langName     = MapLang(f041?.Sub("a") ?? Lang008(GetControl(fields, "008")));
        var origLangName = MapLang(f041?.Sub("h"));

        var contribs = new List<BnContributor>();
        void AddPerson(MarcField f, bool main)
        {
            var a = f.Sub("a");
            if (string.IsNullOrWhiteSpace(a)) return;
            var role = Role(f.Sub("e"));
            if (role is null) { if (!main) return; role = "Author"; }   // keep 100 as Author; skip stray 700s
            var (last, first) = SplitName(a);
            contribs.Add(new BnContributor { LastName = last, FirstName = first, RoleName = role });
        }
        foreach (var f in fields.Where(f => f.Tag == "100")) AddPerson(f, true);
        foreach (var f in fields.Where(f => f.Tag == "700")) AddPerson(f, false);
        
        var genres = new List<string>();
        var seenGenre = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields.Where(f => f.Tag == "655"))
        {
            var a = StripPunct(f.Sub("a"));
            if (!string.IsNullOrWhiteSpace(a) && seenGenre.Add(a)) genres.Add(a);
        }

        return new BnResult
        {
            Title = title, OriginalTitle = origTitle,
            Publisher = publisher, City = city, Year = year, Pages = pages, Edition = edition,
            Isbn13 = isbn13, Isbn10 = isbn10,
            LanguageName = langName, OriginalLanguageName = origLangName,
            Contributors = contribs,
            Categories = genres,
        };
    }

    private static List<MarcField> ParseFields(JsonElement marc)
    {
        var list = new List<MarcField>();
        if (!marc.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var entry in fields.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in entry.EnumerateObject())       // single property: the tag
            {
                var mf  = new MarcField { Tag = prop.Name };
                var val = prop.Value;
                if (val.ValueKind == JsonValueKind.String)
                    mf.Control = val.GetString();
                else if (val.ValueKind == JsonValueKind.Object
                         && val.TryGetProperty("subfields", out var subs) && subs.ValueKind == JsonValueKind.Array)
                    foreach (var sub in subs.EnumerateArray())
                        if (sub.ValueKind == JsonValueKind.Object)
                            foreach (var sp in sub.EnumerateObject())
                                mf.Subfields.Add((sp.Name, sp.Value.GetString() ?? ""));
                list.Add(mf);
                break;
            }
        }
        return list;
    }

    private static string? GetControl(List<MarcField> fields, string tag)
        => fields.FirstOrDefault(f => f.Tag == tag)?.Control;

    private static string BuildTitle(MarcField f245)
    {
        var a = StripPunct(f245.Sub("a"));
        var b = StripPunct(f245.Sub("b"));
        if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)) return $"{a} : {b}";
        return a ?? b ?? "";
    }

    private static (string last, string? first) SplitName(string a)
    {
        a = a.Trim().TrimEnd(',', '.', ' ');
        var idx = a.IndexOf(',');
        if (idx < 0) return (a.Trim(), null);
        var last  = a[..idx].Trim();
        var first = a[(idx + 1)..].Trim();
        first = Regex.Replace(first, @"\s*\([^)]*\)\s*$", "").TrimEnd('.', ',', ' ').Trim();
        return (last, string.IsNullOrWhiteSpace(first) ? null : first);
    }

    private static string? Role(string? e)
    {
        var t = (e ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return "Author";   // bare added entry → (co-)author
        if (t.Contains("tłumacz") || t.Contains("tlumacz") || t.Contains("przekład") || t.Contains("przeklad")) return "Translator";
        if (t.Contains("ilustr")) return "Illustrator";
        if (t.Contains("redakt") || t.Contains("redakcja") || t.Contains("oprac")) return "Editor";
        if (t.Contains("autor")) return "Author";
        return null;   // posłowie, wstęp, recenzent… → not one of our roles, skip
    }

    private static string? MapLang(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToLowerInvariant();
        if (c.Length > 3) c = c[..3];
        return c switch
        {
            "pol" => "Polish",
            "eng" => "English",
            "ger" or "deu" => "German",
            "fre" or "fra" => "French",
            "rus" => "Russian",
            _ => null,
        };
    }

    private static string? Lang008(string? f008) => f008 is { Length: >= 38 } ? f008.Substring(35, 3) : null;
    private static int?    Year008(string? f008) => f008 is { Length: >= 11 } && int.TryParse(f008.Substring(7, 4), out var y) ? y : null;

    private static int? ParseYear(string? s)
    {
        if (s is null) return null;
        var m = Regex.Match(s, @"\d{4}");
        return m.Success && int.TryParse(m.Value, out var y) ? y : null;
    }

    private static int? ParseFirstInt(string? s)
    {
        if (s is null) return null;
        var m = Regex.Match(s, @"\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    private static string NormalizeIsbn(string? s)
    {
        if (s is null) return "";
        var sb = new StringBuilder();
        foreach (var ch in s)
            if (char.IsDigit(ch)) sb.Append(ch);
            else if (ch is 'X' or 'x') sb.Append('X');
        var t = sb.ToString();
        if (t.Length >= 13) return t[..13];
        if (t.Length >= 10) return t[..10];
        return t;
    }

    private static string Digits(string s) => new(s.Where(c => char.IsDigit(c) || c is 'X' or 'x').ToArray());

    private static string? StripPunct(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.TrimEnd().TrimEnd('/', ':', ';', ',', '=', '.', ' ').Trim();

    private static BnResult MapFromConvenience(JsonElement bib) => new()
    {
        Title     = ConvenienceString(bib, "title") ?? "",
        Publisher = ConvenienceString(bib, "publisher"),
        City      = ConvenienceString(bib, "placePublication"),
        Year      = ParseYear(ConvenienceString(bib, "publicationYear")),
    };

    private static string? ConvenienceString(JsonElement bib, string name)
        => bib.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? StripPunct(v.GetString()) : null;
}