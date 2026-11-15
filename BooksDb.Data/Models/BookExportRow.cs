namespace BooksDb.Data.Models;

public sealed class BookExportRow
{
    public string  Title            { get; init; } = "";
    public string? OriginalTitle    { get; init; }
    public string? Authors          { get; init; }
    public string? Translators      { get; init; }
    public string? Illustrators     { get; init; }
    public string? Editors          { get; init; }
    public string  Language         { get; init; } = "";
    public string? OriginalLanguage { get; init; }
    public int?    YearPublished    { get; init; }
    public int?    YearCopyright    { get; init; }
    public string? Edition          { get; init; }
    public string? OriginalEdition  { get; init; }
    public string? Publisher        { get; init; }
    public string? City             { get; init; }
    public string? Isbn13           { get; init; }
    public string? Isbn10           { get; init; }
    public string? Cover            { get; init; }
    public int?    Pages            { get; init; }
    public string? Shelf            { get; init; }
    public bool    IsRead           { get; init; }
    public int?    InitialPrice     { get; init; }
    public string? DateOfBuy        { get; init; }
    public string? DateOfLastRead   { get; init; }
    public string? Description      { get; init; }
    public string? Notes            { get; init; }
    public string? Categories       { get; init; }
    public string? Pictures         { get; init; }
}