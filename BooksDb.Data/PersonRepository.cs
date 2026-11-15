using Dapper;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public sealed class PersonRepository
{
    private readonly Database _db;
    public PersonRepository(Database db) => _db = db;

    public IReadOnlyList<PersonSuggestion> Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<PersonSuggestion>();

        using var conn = _db.Open();
        return conn.Query<PersonSuggestion>(
                """
                SELECT  PersonId AS Id,
                        FirstName,
                        LastName,
                        COALESCE(SortName, LastName) AS Display
                FROM    People
                WHERE   npl_fold(LastName) LIKE @q OR npl_fold(SortName) LIKE @q OR npl_fold(FirstName) LIKE @q
                ORDER BY Display
                LIMIT 20;
                """,
                new { q = PolishText.Fold(text.Trim()) + "%" })
            .AsList();
    }
}