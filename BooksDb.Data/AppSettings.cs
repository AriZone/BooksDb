namespace BooksDb.Data;

public sealed class AppSettings
{
    // true  = delete a book's image files from disk when the book is deleted
    // false = move them into the "pictures_orphaned" folder instead
    public bool DeletePictureFilesOnBookDelete { get; set; } = false;
    
    // Capture listener (Helper companion). Empty token => listener stays off.
    public int    CapturePort  { get; set; } = 8787;
    public string CaptureToken { get; set; } = "";
    
    // Google Books API key (optional). Empty => keyless, which Google throttles to 429 almost immediately.
    public string GoogleBooksApiKey { get; set; } = "";
    
    // Desktop window geometry. RememberWindow gates both save and restore.
    public bool    RememberWindow  { get; set; } = true;
    public double? WindowWidth     { get; set; }
    public double? WindowHeight    { get; set; }
    public bool    WindowMaximized { get; set; }
    
    public int PreviewDelayMs { get; set; } = 200;   // selection-preview debounce; 0 = instant

    // Currently-open database. Empty => the default (books.db in the config dir).
    public string DatabasePath { get; set; } = "";
    
}