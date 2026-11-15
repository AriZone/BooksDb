namespace BooksDb.Data.Models;

public sealed class PictureInput
{
    public string Path      { get; init; } = "";   // relative, e.g. "pictures/ab12….jpg"
    public bool   IsPrimary { get; init; }
}