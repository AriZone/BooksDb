//using System;
//using System.Collections.Generic;
using Dapper;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public sealed class BorrowerRepository
{
    private readonly Database _db;
    public BorrowerRepository(Database db) => _db = db;

    public IReadOnlyList<LookupItem> Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LookupItem>();
        using var conn = _db.Open();
        return conn.Query<LookupItem>(
            """
            SELECT BorrowerId AS Id,
                   LastName || ', ' || FirstName AS Name
            FROM   Borrowers
            WHERE  npl_fold(LastName) LIKE @q OR npl_fold(FirstName) LIKE @q
            ORDER BY LastName, FirstName
            LIMIT 20;
            """,
            new { q = PolishText.Fold(text.Trim()) + "%" }).AsList();
    }

    public long ResolveOrCreate(string first, string last)
    {
        var f = first.Trim();
        var l = last.Trim();
        using var conn = _db.Open();
        var id = conn.ExecuteScalar<long?>(
            "SELECT BorrowerId FROM Borrowers WHERE FirstName = @f AND LastName = @l;",
            new { f, l });
        return id ?? conn.ExecuteScalar<long>(
            "INSERT INTO Borrowers (FirstName, LastName) VALUES (@f, @l); SELECT last_insert_rowid();",
            new { f, l });
    }
}