namespace BooksDb.Data.Models;

public sealed class BookPicture
{
    public long   Id        { get; init; }
    public long   BookId    { get; init; }
    public string Path      { get; init; } = "";   // relative to the data dir, e.g. "pictures/ab12….jpg"
    public bool   IsPrimary { get; init; }
}