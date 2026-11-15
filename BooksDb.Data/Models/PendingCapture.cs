namespace BooksDb.Data.Models;

/// <summary>
/// A book captured on the phone, staged on the desktop awaiting review/commit.
/// Lives in the PendingCaptures tray until you complete its fields and commit it
/// into the catalog (or discard it).
/// </summary>
public sealed class PendingCapture
{
    public long CaptureId { get; set; }

    /// <summary>Phone-side session id, used to group captures in the tray.</summary>
    public string? SessionId { get; set; }

    /// <summary>ISBN exactly as scanned or typed on the phone (10 or 13, or null).</summary>
    public string? RawIsbn { get; set; }

    /// <summary>Canonical ISBN-13 computed at ingest (Isbn.To13). Dedupe/display key. Null if none/invalid.</summary>
    public string? Isbn13 { get; set; }

    /// <summary>Shelf inherited from the phone's sticky field. Null = unshelved (fill in on commit).</summary>
    public string? Shelf { get; set; }

    /// <summary>Phone-side capture time (ISO 8601).</summary>
    public string? CapturedAt { get; set; }

    /// <summary>Desktop ingest time (ISO 8601).</summary>
    public string ReceivedAt { get; set; } = "";

    /// <summary>pending | committed | discarded.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Loaded by the repository (not a column). Ordinal order; [0] = primary/front.</summary>
    public IReadOnlyList<string> Photos { get; set; } = Array.Empty<string>();
}