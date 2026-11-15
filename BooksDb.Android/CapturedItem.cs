using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BooksDb.Android;

public class CapturedItem
{
    public string Isbn { get; }
    public string Shelf { get; }
    public ObservableCollection<CapturedPhoto> Photos { get; } = new();

    public string Header =>
        string.IsNullOrWhiteSpace(Isbn)  ? "Unassigned photos"
        : string.IsNullOrWhiteSpace(Shelf) ? Isbn
        : $"{Isbn}  :  {Shelf}";

    public CapturedItem(string isbn, string shelf)
    {
        Isbn  = isbn;
        Shelf = shelf;
    }
}

public partial class CapturedPhoto : ObservableObject
{
    public byte[] Data { get; }

    /// <summary>Backing file name in the capture store (set once the JPEG is persisted to disk;
    /// null only for a photo that has not been written yet). Used to map memory ⇄ disk on delete.</summary>
    public string? FileName { get; set; }

    // Renumberable so the gallery can close gaps after a delete; Label re-derives and the Helper
    // row's photo list updates live.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private int _number;

    public string Label => $"→  Photo {Number}  ({Data.Length / 1024} KB)";

    public CapturedPhoto(byte[] data, int number)
    {
        Data    = data;
        _number = number;
    }
}