//using System;
//using System.Collections.Generic;
//using System.Linq;

namespace BooksDb.Data.Lookup;

public sealed class BnContributor
{
    public string  LastName  { get; init; } = "";
    public string? FirstName { get; init; }
    public string  RoleName  { get; init; } = "Author";   // Author / Translator / Illustrator / Editor
    public string  Display   => string.IsNullOrWhiteSpace(FirstName) ? LastName : $"{LastName}, {FirstName}";
}

public sealed class BnResult
{
    public string  Title                { get; init; } = "";
    public string? OriginalTitle        { get; init; }
    public string? Publisher            { get; init; }
    public string? City                 { get; init; }
    public int?    Year                 { get; init; }
    public int?    Pages                { get; init; }
    public string? Edition              { get; init; }
    public string? Isbn13               { get; init; }
    public string? Isbn10               { get; init; }
    public string? LanguageName         { get; init; }   // mapped to our Languages names (e.g. "Polish")
    public string? OriginalLanguageName { get; init; }
    public IReadOnlyList<BnContributor> Contributors { get; init; } = Array.Empty<BnContributor>();
    
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    // Short label for the results picker (next increment).
    public string Summary
    {
        get
        {
            var bits = new List<string>();
            var who = Contributors.FirstOrDefault()?.Display;
            if (!string.IsNullOrWhiteSpace(who))        bits.Add(who);
            if (Year is not null)                       bits.Add(Year.Value.ToString());
            if (!string.IsNullOrWhiteSpace(Publisher))  bits.Add(Publisher!);
            return bits.Count == 0 ? Title : $"{Title} — {string.Join(", ", bits)}";
        }
    }
    public override string ToString() => Summary;
}