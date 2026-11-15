//using System;
//using System.Collections.Generic;
using System.Globalization;
//using System.Linq;
using ClosedXML.Excel;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public sealed class ExcelImporter
{
    private static readonly XLColor RedFill   = XLColor.FromHtml("#F4CCCC");
    private static readonly XLColor GreenFill = XLColor.FromHtml("#D9EAD3");

    private readonly BookRepository   _books;
    private readonly LookupRepository _lookups;
    private readonly PictureStore     _pictures;

    public ExcelImporter(BookRepository books, LookupRepository lookups, PictureStore pictures)
    {
        _books    = books;
        _lookups  = lookups;
        _pictures = pictures;
    }

    public ImportResult ImportAdd(string sourcePath, string resultPath)     => Import(sourcePath, resultPath, replaceAll: false);
    public ImportResult ImportReplace(string sourcePath, string resultPath) => Import(sourcePath, resultPath, replaceAll: true);

    private ImportResult Import(string sourcePath, string resultPath, bool replaceAll)
    {
        using var wb = new XLWorkbook(sourcePath);
        var ws = wb.Worksheets.First();

        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 1; c <= lastCol; c++)
        {
            var name = ws.Cell(1, c).GetString().Trim();
            if (name.Length > 0 && !col.ContainsKey(name)) col[name] = c;
        }

        // dedup index — only built for add mode
        HashSet<string> isbn13 = new(), isbn10 = new(), titleYear = new(), titleOnly = new();
        if (!replaceAll)
        {
            foreach (var k in _books.GetDedupKeys())
            {
                if (!string.IsNullOrWhiteSpace(k.Isbn13)) isbn13.Add(Norm(k.Isbn13!));
                if (!string.IsNullOrWhiteSpace(k.Isbn10)) isbn10.Add(Norm(k.Isbn10!));
                titleYear.Add(TitleYearKey(k.Title, k.YearPublished));
                titleOnly.Add(Norm(k.Title));
            }
        }

        var roles = _lookups.GetRoles().ToDictionary(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase);

        var toInsert = new List<(BookDetail Book, IReadOnlyList<ContributorInput> Contributors,
            IReadOnlyList<string> Categories, IReadOnlyList<PictureInput> Pictures)>();
        
        var decisions = new List<(int Row, string Reason, XLColor? Fill)>();

        for (var r = 2; r <= lastRow; r++)
        {
            var title = GetStr(ws, r, col, "Title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            if (!replaceAll)
            {
                var i13  = GetStr(ws, r, col, "ISBN-13");
                var i10  = GetStr(ws, r, col, "ISBN-10");
                var year = GetInt(ws, r, col, "Year");

                string? reason = null; XLColor? fill = null;
                if ((i13 is not null && isbn13.Contains(Norm(i13))) ||
                    (i10 is not null && isbn10.Contains(Norm(i10))))
                {
                    reason = "duplicate: ISBN"; fill = RedFill;
                }
                else if (year is null ? titleOnly.Contains(Norm(title)) : titleYear.Contains(TitleYearKey(title, year)))
                {
                    reason = year is null ? "duplicate: title" : "duplicate: title+year"; fill = GreenFill;
                }

                if (reason is not null) { decisions.Add((r, reason, fill)); continue; }

                if (i13 is not null) isbn13.Add(Norm(i13));
                if (i10 is not null) isbn10.Add(Norm(i10));
                titleOnly.Add(Norm(title));
                titleYear.Add(TitleYearKey(title, year));
            }

            toInsert.Add((BuildDetail(ws, r, col, title), BuildContributors(ws, r, col, roles),
                BuildCategories(ws, r, col), BuildPictures(ws, r, col)));
            
            decisions.Add((r, "imported", null));
        }

        if (replaceAll) _books.ReplaceAll(toInsert);
        else            _books.InsertMany(toInsert);

        var resultCol = lastCol + 1;
        ws.Cell(1, resultCol).Value = "Result";
        ws.Cell(1, resultCol).Style.Font.Bold = true;
        foreach (var (row, rsn, fill) in decisions)
        {
            ws.Cell(row, resultCol).Value = rsn;
            if (fill is not null)
                ws.Range(row, 1, row, resultCol).Style.Fill.BackgroundColor = fill;
        }
        wb.SaveAs(resultPath);

        return new ImportResult
        {
            Imported    = decisions.Count(d => d.Reason == "imported"),
            SkippedHard = decisions.Count(d => d.Reason == "duplicate: ISBN"),
            SkippedSoft = decisions.Count(d => d.Reason.StartsWith("duplicate: title", StringComparison.Ordinal)),
            ResultPath  = resultPath,
        };
    }

    private BookDetail BuildDetail(IXLWorksheet ws, int r, IDictionary<string, int> col, string title)
    {
        var lang     = GetStr(ws, r, col, "Language");
        var origLang = GetStr(ws, r, col, "Original language");
        var cover    = GetStr(ws, r, col, "Cover");
        var pub      = GetStr(ws, r, col, "Publisher");
        var city     = GetStr(ws, r, col, "City");

        return new BookDetail
        {
            Id                 = 0,
            Title              = title.Trim(),
            OriginalTitle      = GetStr(ws, r, col, "Original title"),
            LanguageId         = _lookups.ResolveOrCreateLanguage(string.IsNullOrWhiteSpace(lang) ? "Polish" : lang),
            OriginalLanguageId = origLang is null ? null : _lookups.ResolveOrCreateLanguage(origLang),
            YearPublished      = GetInt(ws, r, col, "Year"),
            YearCopyright      = GetInt(ws, r, col, "Year ©"),
            Edition            = GetStr(ws, r, col, "Edition"),
            OriginalEdition    = GetStr(ws, r, col, "Original edition"),
            Isbn13             = GetStr(ws, r, col, "ISBN-13"),
            Isbn10             = GetStr(ws, r, col, "ISBN-10"),
            CoverId            = cover is null ? null : _lookups.ResolveOrCreateCover(cover),
            Pages              = GetInt(ws, r, col, "Pages"),
            Description        = GetStr(ws, r, col, "Description"),
            Shelf              = GetStr(ws, r, col, "Shelf"),
            IsRead             = GetBool(ws, r, col, "Read"),
            Notes              = GetStr(ws, r, col, "Notes"),
            PublisherId        = pub is null ? null : _lookups.ResolveOrCreatePublisher(pub),
            CityId             = city is null ? null : _lookups.ResolveOrCreateCity(city),
            InitialPrice       = GetPriceGrosze(ws, r, col, "Price (PLN)"),
            DateOfBuy          = GetStr(ws, r, col, "Bought"),
            DateOfLastRead     = GetStr(ws, r, col, "Last read"),
        };
    }

    private static IReadOnlyList<ContributorInput> BuildContributors(
        IXLWorksheet ws, int r, IDictionary<string, int> col, IDictionary<string, long> roles)
    {
        var list = new List<ContributorInput>();
        AddRole(list, ws, r, col, roles, "Authors",      "Author");
        AddRole(list, ws, r, col, roles, "Translators",  "Translator");
        AddRole(list, ws, r, col, roles, "Illustrators", "Illustrator");
        AddRole(list, ws, r, col, roles, "Editors",      "Editor");
        return list;
    }
    
    private static IReadOnlyList<string> BuildCategories(IXLWorksheet ws, int r, IDictionary<string, int> col)
    {
        var cell = GetStr(ws, r, col, "Categories");
        if (cell is null) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var part in cell.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (seen.Add(part)) list.Add(part);
        return list;
    }
    
    private IReadOnlyList<PictureInput> BuildPictures(IXLWorksheet ws, int r, IDictionary<string, int> col)
    {
        var cell = GetStr(ws, r, col, "Pictures");
        if (cell is null) return Array.Empty<PictureInput>();

        var list = new List<PictureInput>();
        var primaryTaken = false;
        foreach (var raw in cell.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var starred = raw.StartsWith('*');
            var path    = (starred ? raw[1..] : raw).Trim();
            if (path.Length == 0) continue;

            // existence guard: only materialize rows for files actually present in this install
            if (!File.Exists(_pictures.AbsolutePath(path))) continue;

            // cap at one primary (first '*' wins; demote any extras to satisfy the unique index)
            var primary = starred && !primaryTaken;
            if (primary) primaryTaken = true;

            list.Add(new PictureInput { Path = path, IsPrimary = primary });
        }
        return list;
    }

    private static void AddRole(List<ContributorInput> list, IXLWorksheet ws, int r,
        IDictionary<string, int> col, IDictionary<string, long> roles, string columnHeader, string roleName)
    {
        if (!roles.TryGetValue(roleName, out var roleId)) return;
        var cell = GetStr(ws, r, col, columnHeader);
        if (cell is null) return;

        foreach (var part in cell.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (first, last) = ParseName(part);
            if (string.IsNullOrWhiteSpace(last)) continue;
            list.Add(new ContributorInput { PersonId = null, FirstName = first, LastName = last, RoleId = roleId });
        }
    }

    private static (string? First, string Last) ParseName(string s)
    {
        var t = s.Trim();
        var comma = t.IndexOf(',');
        if (comma < 0) return (null, t);
        var last  = t[..comma].Trim();
        var first = t[(comma + 1)..].Trim();
        return (first.Length == 0 ? null : first, last);
    }

    private static string? GetStr(IXLWorksheet ws, int r, IDictionary<string, int> col, string header)
    {
        if (!col.TryGetValue(header, out var c)) return null;
        var s = ws.Cell(r, c).GetString().Trim();
        return s.Length == 0 ? null : s;
    }

    private static int? GetInt(IXLWorksheet ws, int r, IDictionary<string, int> col, string header)
    {
        if (!col.TryGetValue(header, out var c)) return null;
        var cell = ws.Cell(r, c);
        if (cell.TryGetValue<double>(out var d)) return (int)Math.Round(d);
        return int.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static int? GetPriceGrosze(IXLWorksheet ws, int r, IDictionary<string, int> col, string header)
    {
        if (!col.TryGetValue(header, out var c)) return null;
        var cell = ws.Cell(r, c);
        if (cell.TryGetValue<double>(out var d)) return (int)Math.Round(d * 100);
        var s = cell.GetString().Trim().Replace(',', '.');
        if (s.Length == 0) return null;
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var pln)
            ? (int)Math.Round(pln * 100m, MidpointRounding.AwayFromZero) : null;
    }

    private static bool GetBool(IXLWorksheet ws, int r, IDictionary<string, int> col, string header)
    {
        if (!col.TryGetValue(header, out var c)) return false;
        var s = ws.Cell(r, c).GetString().Trim();
        return s.Equals("Yes", StringComparison.OrdinalIgnoreCase)
            || s.Equals("true", StringComparison.OrdinalIgnoreCase)
            || s == "1";
    }

    private static string Norm(string s) => s.Trim().ToLowerInvariant();
    private static string TitleYearKey(string title, int? year) => Norm(title) + "|" + (year?.ToString() ?? "");
}