using Avalonia.Media.Imaging;

namespace BooksDb.ViewModels;

public sealed class PictureItem
{
    public long    Id         { get; init; }
    public bool    IsPrimary  { get; init; }
    public Bitmap? Image      { get; init; }
    public string? SourcePath { get; init; }   // absolute path to the full-res file, for the popup viewer
}