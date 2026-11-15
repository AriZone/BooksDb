//using System;
//using System.Collections.Generic;
//using System.IO;
using Dapper;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public sealed class PictureRepository
{
    private readonly Database     _db;
    private readonly PictureStore _store;

    public PictureRepository(Database db, PictureStore store)
    {
        _db    = db;
        _store = store;
    }

    public IReadOnlyList<BookPicture> GetForBook(long bookId)
    {
        using var conn = _db.Open();
        return conn.Query<BookPicture>(
            "SELECT PictureId AS Id, BookId, Path, IsPrimary FROM BookPictures WHERE BookId = @bookId ORDER BY IsPrimary DESC, PictureId;",
            new { bookId }).AsList();
    }

    public string AbsolutePath(BookPicture p) => _store.AbsolutePath(p.Path);

    // Resolve a bare relative path (e.g. a staged capture photo) against the picture root.
    public string AbsolutePath(string relative) => _store.AbsolutePath(relative);

    public long Add(long bookId, string sourceFile, bool makePrimary)
    {
        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        var rel = "pictures/" + Guid.NewGuid().ToString("N") + ext;
        ImageOrient.SaveOriented(sourceFile, _store.AbsolutePath(rel));   // pictures/ dir is ensured by PictureStore

        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        if (makePrimary)
            conn.Execute("UPDATE BookPictures SET IsPrimary = 0 WHERE BookId = @bookId;", new { bookId }, tx);

        var id = conn.ExecuteScalar<long>(
            "INSERT INTO BookPictures (BookId, Path, IsPrimary) VALUES (@bookId, @rel, @primary); SELECT last_insert_rowid();",
            new { bookId, rel, primary = makePrimary ? 1 : 0 }, tx);

        tx.Commit();
        return id;
    }

    public void Delete(long pictureId)
    {
        using var conn = _db.Open();
        var rel = conn.ExecuteScalar<string?>("SELECT Path FROM BookPictures WHERE PictureId = @pictureId;", new { pictureId });
        conn.Execute("DELETE FROM BookPictures WHERE PictureId = @pictureId;", new { pictureId });
        if (rel is not null)
        {
            try { File.Delete(_store.AbsolutePath(rel)); } catch { /* best effort */ }
        }
    }

    public void SetPrimary(long bookId, long pictureId)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();
        conn.Execute("UPDATE BookPictures SET IsPrimary = 0 WHERE BookId = @bookId;", new { bookId }, tx);
        conn.Execute("UPDATE BookPictures SET IsPrimary = 1 WHERE PictureId = @pictureId;", new { pictureId }, tx);
        tx.Commit();
    }
    
    public void DiscardFiles(IEnumerable<BookPicture> pictures, bool deleteFromDisk)
    {
        foreach (var p in pictures)
        {
            var abs = _store.AbsolutePath(p.Path);
            if (!File.Exists(abs)) continue;
            try
            {
                if (deleteFromDisk) File.Delete(abs);
                else                _store.Archive(abs);
            }
            catch { /* best effort: never let file cleanup block a delete */ }
        }
    }
}