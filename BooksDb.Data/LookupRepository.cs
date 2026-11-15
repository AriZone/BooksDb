using Dapper;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public sealed class LookupRepository
{
    private readonly Database _db;
    public LookupRepository(Database db) => _db = db;

    public IReadOnlyList<Language> GetLanguages()
    {
        using var conn = _db.Open();
        return conn.Query<Language>(
            "SELECT LanguageId AS Id, Language AS Name FROM Languages ORDER BY Language;").AsList();
    }

    public IReadOnlyList<Cover> GetCovers()
    {
        using var conn = _db.Open();
        return conn.Query<Cover>(
            "SELECT CoverId AS Id, Cover AS Name FROM Covers ORDER BY Cover;").AsList();
    }

    public IReadOnlyList<LookupItem> GetRoles()
    {
        using var conn = _db.Open();
        return conn.Query<LookupItem>(
            "SELECT RoleId AS Id, Role AS Name FROM Roles ORDER BY RoleId;").AsList();
    }

    public IReadOnlyList<LookupItem> SearchPublishers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LookupItem>();
        using var conn = _db.Open();
        return conn.Query<LookupItem>(
            "SELECT PublisherId AS Id, Publisher AS Name FROM Publishers WHERE npl_fold(Publisher) LIKE @q ORDER BY Publisher LIMIT 20;",
            new { q = PolishText.Fold(text.Trim()) + "%" }).AsList();
    }

    public IReadOnlyList<LookupItem> SearchCities(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LookupItem>();
        using var conn = _db.Open();
        return conn.Query<LookupItem>(
            "SELECT CityId AS Id, City AS Name FROM Cities WHERE npl_fold(City) LIKE @q ORDER BY City LIMIT 20;",
            new { q = PolishText.Fold(text.Trim()) + "%" }).AsList();
    }
    
    public IReadOnlyList<LookupItem> SearchCategories(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<LookupItem>();
        using var conn = _db.Open();
        return conn.Query<LookupItem>(
            "SELECT CategoryId AS Id, Category AS Name FROM Categories WHERE npl_fold(Category) LIKE @q ORDER BY Category LIMIT 20;",
            new { q = PolishText.Fold(text.Trim()) + "%" }).AsList();
    }
    
    public IReadOnlyList<LookupItem> AllCategories()
    {
        using var conn = _db.Open();
        return conn.Query<LookupItem>(
            "SELECT CategoryId AS Id, Category AS Name FROM Categories ORDER BY Category;").AsList();
    }

    public long ResolveOrCreatePublisher(string name) => ResolveOrCreate(
        "Publishers", "PublisherId", "Publisher", name);

    public long ResolveOrCreateCity(string name) => ResolveOrCreate(
        "Cities", "CityId", "City", name);

    public long ResolveOrCreateLanguage(string name) => ResolveOrCreate(
        "Languages", "LanguageId", "Language", name);

    public long ResolveOrCreateCover(string name) => ResolveOrCreate(
        "Covers", "CoverId", "Cover", name);

    private long ResolveOrCreate(string table, string idCol, string nameCol, string name)
    {
        var n = name.Trim();
        using var conn = _db.Open();
        var id = conn.ExecuteScalar<long?>(
            $"SELECT {idCol} FROM {table} WHERE {nameCol} = @n;", new { n });
        return id ?? conn.ExecuteScalar<long>(
            $"INSERT INTO {table} ({nameCol}) VALUES (@n); SELECT last_insert_rowid();", new { n });
    }
}