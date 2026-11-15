using Dapper;
//using System.Collections.Generic;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public sealed class LoanRepository
{
    private readonly Database _db;
    public LoanRepository(Database db) => _db = db;

    public void Lend(long bookId, long borrowerId, string dateLent, string? notes)
    {
        using var conn = _db.Open();
        conn.Execute(
            "INSERT INTO Loans (BookId, BorrowerId, DateLent, Notes) VALUES (@bookId, @borrowerId, @dateLent, @notes);",
            new { bookId, borrowerId, dateLent, notes });
    }

    public void Return(long bookId, string dateReturned, string? notes)
    {
        using var conn = _db.Open();
        conn.Execute(
            """
            UPDATE Loans
               SET DateReturned = @dateReturned,
                   Notes        = COALESCE(@notes, Notes)
             WHERE BookId = @bookId AND DateReturned IS NULL;
            """,
            new { bookId, dateReturned, notes });
    }
    
    public IReadOnlyList<LoanHistoryItem> GetHistory(long bookId)
    {
        using var conn = _db.Open();
        return conn.Query<LoanHistoryItem>(
            """
            SELECT br.LastName || ', ' || br.FirstName AS Borrower,
                   ln.DateLent     AS DateLent,
                   ln.DateReturned AS DateReturned,
                   ln.Notes        AS Notes
            FROM   Loans ln
            JOIN   Borrowers br ON br.BorrowerId = ln.BorrowerId
            WHERE  ln.BookId = @bookId
            ORDER BY ln.DateReturned IS NULL DESC, ln.DateLent DESC, ln.LoanId DESC;
            """,
            new { bookId }).AsList();
    }
}