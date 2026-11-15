namespace BooksDb.Data.Models;

public sealed class PocketPhoto
{
    public long   PhotoId   { get; init; }
    public long   CaptureId { get; init; }
    public string Path      { get; init; } = "";
}