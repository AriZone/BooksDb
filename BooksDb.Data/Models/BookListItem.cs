namespace BooksDb.Data.Models;

public sealed class BookListItem
{
    public long    Id            { get; init; }
    public string  Title         { get; init; } = "";
    public int?    YearPublished { get; init; }
    public string? Isbn13        { get; init; }
    public string? Language      { get; init; }
    public string? Authors       { get; init; }
    public string? LentTo        { get; init; }
    public string? Shelf         { get; init; }
    public string? CoverPath     { get; init; }
    public string? MainCategory { get; init; }
}