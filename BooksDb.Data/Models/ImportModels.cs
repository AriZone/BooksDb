namespace BooksDb.Data.Models;

public sealed class DedupKey
{
    public string  Title         { get; init; } = "";
    public int?    YearPublished { get; init; }
    public string? Isbn13        { get; init; }
    public string? Isbn10        { get; init; }
}

public sealed class ImportResult
{
    public int    Imported    { get; init; }
    public int    SkippedHard { get; init; }   // ISBN match
    public int    SkippedSoft { get; init; }   // title(+year) match
    public string ResultPath  { get; init; } = "";
}