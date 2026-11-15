using Dapper;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public sealed class PendingCaptureRepository
{
    private readonly Database     _db;
    private readonly PictureStore _store;

    public PendingCaptureRepository(Database db, PictureStore store)
    {
        _db    = db;
        _store = store;
    }

    /// <summary>Insert one staged capture plus its photos (ordinal = list order; [0] = primary). Returns the new id.</summary>
    public long Add(PendingCapture capture, IReadOnlyList<string> photoPaths)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        var id = conn.ExecuteScalar<long>(
            """
            INSERT INTO PendingCaptures
                (SessionId, RawIsbn, Isbn13, Shelf, CapturedAt, ReceivedAt, Status)
            VALUES
                (@SessionId, @RawIsbn, @Isbn13, @Shelf, @CapturedAt, @ReceivedAt, @Status);
            SELECT last_insert_rowid();
            """,
            new
            {
                capture.SessionId,
                capture.RawIsbn,
                capture.Isbn13,
                capture.Shelf,
                capture.CapturedAt,
                capture.ReceivedAt,
                capture.Status
            }, tx);

        for (int i = 0; i < photoPaths.Count; i++)
            conn.Execute(
                "INSERT INTO PendingCapturePhotos (CaptureId, Path, Ordinal) VALUES (@id, @path, @ord);",
                new { id, path = photoPaths[i], ord = i }, tx);

        tx.Commit();
        return id;
    }

    /// <summary>All captures still awaiting review, oldest first, each with its photos loaded in order.</summary>
    public IReadOnlyList<PendingCapture> GetPending()
    {
        using var conn = _db.Open();

        var captures = conn.Query<PendingCapture>(
            """
            SELECT CaptureId, SessionId, RawIsbn, Isbn13, Shelf, CapturedAt, ReceivedAt, Status
            FROM   PendingCaptures
            WHERE  Status = 'pending'
            ORDER BY ReceivedAt, CaptureId;
            """).AsList();

        foreach (var c in captures)
            c.Photos = conn.Query<string>(
                "SELECT Path FROM PendingCapturePhotos WHERE CaptureId = @id ORDER BY Ordinal, PhotoId;",
                new { id = c.CaptureId }).AsList();

        return captures;
    }

    public int CountPending()
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM PendingCaptures WHERE Status = 'pending';");
    }
    
    /// <summary>Pending captures that carry an ISBN — the "review &amp; make a book" queue.</summary>
    public IReadOnlyList<PendingCapture> GetPendingWithIsbn()
    {
        using var conn = _db.Open();
        var captures = conn.Query<PendingCapture>(
            """
            SELECT CaptureId, SessionId, RawIsbn, Isbn13, Shelf, CapturedAt, ReceivedAt, Status
            FROM   PendingCaptures
            WHERE  Status = 'pending' AND Isbn13 IS NOT NULL
            ORDER BY ReceivedAt, CaptureId;
            """).AsList();

        foreach (var c in captures)
            c.Photos = conn.Query<string>(
                "SELECT Path FROM PendingCapturePhotos WHERE CaptureId = @id ORDER BY Ordinal, PhotoId;",
                new { id = c.CaptureId }).AsList();

        return captures;
    }

    public int CountPendingWithIsbn()
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM PendingCaptures WHERE Status = 'pending' AND Isbn13 IS NOT NULL;");
    }

    /// <summary>Individual loose photos from ISBN-less pending captures — the unassigned "pocket".</summary>
    public IReadOnlyList<PocketPhoto> GetPocketPhotos()
    {
        using var conn = _db.Open();
        return conn.Query<PocketPhoto>(
            """
            SELECT p.PhotoId, p.CaptureId, p.Path
            FROM   PendingCapturePhotos p
            JOIN   PendingCaptures      c ON c.CaptureId = p.CaptureId
            WHERE  c.Status = 'pending' AND c.Isbn13 IS NULL
            ORDER BY c.ReceivedAt, p.CaptureId, p.Ordinal, p.PhotoId;
            """).AsList();
    }

    public int CountPocket()
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM   PendingCapturePhotos p
            JOIN   PendingCaptures      c ON c.CaptureId = p.CaptureId
            WHERE  c.Status = 'pending' AND c.Isbn13 IS NULL;
            """);
    }
    
    /// <summary>After selected staged photos have been copied onto a book, delete their rows and files.
    /// Any capture left with no photos is marked committed so it leaves the pocket.</summary>
    public void RemovePocketPhotos(IReadOnlyList<long> photoIds)
    {
        if (photoIds.Count == 0) return;

        List<PocketPhoto> rows;
        using (var conn = _db.Open())
        {
            using var tx = conn.BeginTransaction();

            rows = conn.Query<PocketPhoto>(
                "SELECT PhotoId, CaptureId, Path FROM PendingCapturePhotos WHERE PhotoId IN @ids;",
                new { ids = photoIds }, tx).AsList();

            conn.Execute("DELETE FROM PendingCapturePhotos WHERE PhotoId IN @ids;", new { ids = photoIds }, tx);

            foreach (var capId in rows.Select(r => r.CaptureId).Distinct())
            {
                var remaining = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PendingCapturePhotos WHERE CaptureId = @id;", new { id = capId }, tx);
                if (remaining == 0)
                    conn.Execute("UPDATE PendingCaptures SET Status = 'committed' WHERE CaptureId = @id;",
                        new { id = capId }, tx);
            }

            tx.Commit();
        }

        foreach (var r in rows)
        {
            try { File.Delete(_store.AbsolutePath(r.Path)); } catch { /* best effort */ }
        }
        foreach (var dir in rows.Select(r => Path.GetDirectoryName(_store.AbsolutePath(r.Path)))
                     .Where(d => !string.IsNullOrEmpty(d)).Distinct())
        {
            try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch { /* best effort */ }
        }
    }

    /// <summary>Mark a capture committed or discarded after review.</summary>
    public void SetStatus(long captureId, string status)
    {
        using var conn = _db.Open();
        conn.Execute(
            "UPDATE PendingCaptures SET Status = @status WHERE CaptureId = @captureId;",
            new { captureId, status });
    }

    /// <summary>Mark a capture committed and remove its staged photo files. Call AFTER the caller has
    /// copied those files onto the book — this only cleans up the staging copies.</summary>
    public void MarkCommitted(long captureId)
    {
        SetStatus(captureId, "committed");
        DeleteStagedFiles(captureId);
    }

    /// <summary>Drop a capture from the queue and remove its staged photo files.</summary>
    public void Discard(long captureId)
    {
        SetStatus(captureId, "discarded");
        DeleteStagedFiles(captureId);
    }

    /// <summary>Delete a capture's staged photo files and their now-empty group directory (best effort).</summary>
    public void DeleteStagedFiles(long captureId)
    {
        List<string> rels;
        using (var conn = _db.Open())
            rels = conn.Query<string>(
                "SELECT Path FROM PendingCapturePhotos WHERE CaptureId = @id ORDER BY Ordinal, PhotoId;",
                new { id = captureId }).AsList();

        foreach (var rel in rels)
        {
            try { File.Delete(_store.AbsolutePath(rel)); } catch { /* best effort */ }
        }
        foreach (var dir in rels.Select(r => Path.GetDirectoryName(_store.AbsolutePath(r)))
                                .Where(d => !string.IsNullOrEmpty(d))
                                .Distinct())
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { /* best effort */ }
        }
    }
}
