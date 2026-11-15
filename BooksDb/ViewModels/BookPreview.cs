using Avalonia.Media;

namespace BooksDb.ViewModels;

// Fully-formatted, read-only snapshot of a book for the selection preview pane.
public sealed class BookPreview
{
    public required string Title { get; init; }
    public string? Byline        { get; init; }   // "King (Author), Kowalski (Translator)"
    public string? PublisherLine { get; init; }   // "Publisher · City · 2021"
    public string? Language      { get; init; }
    public string? Pages         { get; init; }   // "342 pages"
    public string? Isbn          { get; init; }
    public string? Categories    { get; init; }
    public string? Shelf         { get; init; }
    public string? LentTo        { get; init; }
    public string  ReadStatus    { get; init; } = "";
    public string? Description   { get; init; }
    public string? Notes         { get; init; }
    public string? Authors { get; init; }
    public bool    HasAuthors => !string.IsNullOrWhiteSpace(Authors);
    public Color PlaceholderColor { get; init; }

    public bool HasByline        => !string.IsNullOrWhiteSpace(Byline);
    public bool HasPublisherLine => !string.IsNullOrWhiteSpace(PublisherLine);
    public bool HasLanguage      => !string.IsNullOrWhiteSpace(Language);
    public bool HasPages         => !string.IsNullOrWhiteSpace(Pages);
    public bool HasIsbn          => !string.IsNullOrWhiteSpace(Isbn);
    public bool HasCategories    => !string.IsNullOrWhiteSpace(Categories);
    public bool HasShelf         => !string.IsNullOrWhiteSpace(Shelf);
    public bool HasLentTo        => !string.IsNullOrWhiteSpace(LentTo);
    public bool HasDescription   => !string.IsNullOrWhiteSpace(Description);
    public bool HasNotes         => !string.IsNullOrWhiteSpace(Notes);
}