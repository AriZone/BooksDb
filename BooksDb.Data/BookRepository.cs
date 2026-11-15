using System.Data;
using Dapper;
using BooksDb.Data.Models;

namespace BooksDb.Data;
        
public sealed class BookRepository
{
    private readonly Database _db;
    public BookRepository(Database db) => _db = db;

    private const string InsertBookSql =
        """
        INSERT INTO Books
            (Title, OriginalTitle, LanguageId, OriginalLanguageId, YearPublished, YearCopyright,
             Edition, OriginalEdition, ISBN13, ISBN10, CoverId, Pages, Description, Shelf, IsRead, Notes,
             PublisherId, CityId, InitialPrice, DateOfBuy, DateOfLastRead)
        VALUES
            (@Title, @OriginalTitle, @LanguageId, @OriginalLanguageId, @YearPublished, @YearCopyright,
             @Edition, @OriginalEdition, @Isbn13, @Isbn10, @CoverId, @Pages, @Description, @Shelf, @IsRead, @Notes,
             @PublisherId, @CityId, @InitialPrice, @DateOfBuy, @DateOfLastRead);
        SELECT last_insert_rowid();
        """;

    public IReadOnlyList<BookListItem> GetAll(string? search, long? categoryId,
                                              BookStatusFilter status = BookStatusFilter.All)
    {
        var q    = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var like = q is null ? null : $"%{PolishText.Fold(q)}%";

        var statusClause = status switch
        {
            BookStatusFilter.Unread => " AND b.IsRead = 0",
            BookStatusFilter.Read   => " AND b.IsRead = 1",
            BookStatusFilter.Lent   => " AND EXISTS (SELECT 1 FROM Loans ln2 WHERE ln2.BookId = b.BookId AND ln2.DateReturned IS NULL)",
            _                       => ""
        };

        using var conn = _db.Open();
        return conn.Query<BookListItem>(
            $"""
            SELECT  b.BookId        AS Id,
                    b.Title         AS Title,
                    b.YearPublished AS YearPublished,
                    b.ISBN13        AS Isbn13,
                    l.Language      AS Language,
                    GROUP_CONCAT(COALESCE(p.FirstName || ' ', '') || p.LastName, ', ') AS Authors,
                    (SELECT br.LastName || ', ' || br.FirstName
                     FROM   Loans ln JOIN Borrowers br ON br.BorrowerId = ln.BorrowerId
                     WHERE  ln.BookId = b.BookId AND ln.DateReturned IS NULL) AS LentTo,
                    b.Shelf AS Shelf,
                    (SELECT bp.Path FROM BookPictures bp
                     WHERE  bp.BookId = b.BookId
                     ORDER BY bp.IsPrimary DESC, bp.PictureId LIMIT 1) AS CoverPath,
                     (SELECT cm.Category FROM BookCategories bcm
                     JOIN   Categories cm ON cm.CategoryId = bcm.CategoryId
                     WHERE  bcm.BookId = b.BookId
                     ORDER BY cm.Category LIMIT 1) AS MainCategory
            FROM        Books b
            LEFT JOIN   Languages l        ON l.LanguageId = b.LanguageId
            LEFT JOIN   BookContributors bc
                   ON   bc.BookId = b.BookId
                  AND   bc.RoleId = (SELECT RoleId FROM Roles WHERE Role = 'Author')
            LEFT JOIN   People p           ON p.PersonId = bc.PersonId
            WHERE   (@q IS NULL
                OR  npl_fold(b.Title)    LIKE @like
                OR  npl_fold(l.Language) LIKE @like
                OR  b.ISBN13 LIKE @like
                OR  CAST(b.YearPublished AS TEXT) LIKE @like
                OR  EXISTS (
                        SELECT 1
                        FROM   BookContributors bc2
                        JOIN   People p2 ON p2.PersonId = bc2.PersonId
                        WHERE  bc2.BookId = b.BookId
                          AND (npl_fold(p2.LastName)  LIKE @like
                            OR npl_fold(p2.FirstName) LIKE @like
                            OR npl_fold(p2.SortName)  LIKE @like)
                    ))
                AND (@categoryId IS NULL OR EXISTS (
                    SELECT 1 FROM BookCategories bcat
                    WHERE  bcat.BookId = b.BookId AND bcat.CategoryId = @categoryId)){statusClause}
            GROUP BY    b.BookId
            ORDER BY    b.Title;
            """,
            new { q, like, categoryId })
            .AsList();
    }

    public BookDetail? GetById(long id)
    {
        using var conn = _db.Open();
        return conn.QueryFirstOrDefault<BookDetail>(
            """
            SELECT b.BookId AS Id, b.Title, b.OriginalTitle, b.LanguageId, b.OriginalLanguageId,
                   b.YearPublished, b.YearCopyright, b.Edition, b.OriginalEdition,
                   b.ISBN13 AS Isbn13, b.ISBN10 AS Isbn10, b.CoverId, b.Pages,
                   b.Description, b.Shelf, b.IsRead, b.Notes,
                   b.PublisherId, pub.Publisher AS PublisherName,
                   b.CityId,      cty.City      AS CityName,
                   b.InitialPrice, b.DateOfBuy, b.DateOfLastRead
            FROM        Books b
            LEFT JOIN   Publishers pub ON pub.PublisherId = b.PublisherId
            LEFT JOIN   Cities     cty ON cty.CityId      = b.CityId
            WHERE       b.BookId = @id;
            """,
            new { id });
    }

    public IReadOnlyList<ContributorRow> GetContributors(long bookId)
    {
        using var conn = _db.Open();
        return conn.Query<ContributorRow>(
            """
            SELECT bc.PersonId AS PersonId,
                   COALESCE(p.SortName, p.LastName) AS PersonName,
                   bc.RoleId   AS RoleId,
                   r.Role      AS RoleName
            FROM        BookContributors bc
            JOIN        People p ON p.PersonId = bc.PersonId
            JOIN        Roles  r ON r.RoleId   = bc.RoleId
            WHERE       bc.BookId = @bookId
            ORDER BY    r.RoleId, PersonName;
            """,
            new { bookId }).AsList();
    }
    
    public IReadOnlyList<string> GetCategories(long bookId)
    {
        using var conn = _db.Open();
        return conn.Query<string>(
            """
            SELECT   c.Category
            FROM     BookCategories bc
            JOIN     Categories c ON c.CategoryId = bc.CategoryId
            WHERE    bc.BookId = @bookId
            ORDER BY c.Category;
            """,
            new { bookId }).AsList();
    }
    
    public IReadOnlyList<string> GetAuthorsNatural(long bookId)
    {
        using var conn = _db.Open();
        return conn.Query<string>(
            """
            SELECT TRIM(COALESCE(p.FirstName  || ' ', '') ||
                        COALESCE(p.SecondName || ' ', '') ||
                        p.LastName)
            FROM   BookContributors bc
            JOIN   People p ON p.PersonId = bc.PersonId
            JOIN   Roles  r ON r.RoleId   = bc.RoleId
            WHERE  bc.BookId = @bookId AND r.Role = 'Author'
            ORDER  BY p.SortName, p.LastName;
            """,
            new { bookId }).AsList();
    }

    public IReadOnlyList<DedupKey> GetDedupKeys()
    {
        using var conn = _db.Open();
        return conn.Query<DedupKey>(
            "SELECT Title, YearPublished, ISBN13 AS Isbn13, ISBN10 AS Isbn10 FROM Books;").AsList();
    }

    /// <summary>Id of the catalog book whose canonical ISBN-13 matches the given ISBN, or null. Matches
    /// the same way as the duplicate warning (canonicalised via Isbn.To13, ISBN-13 preferred over -10).</summary>
    public long? FindIdByIsbn(string? rawIsbn)
    {
        var key = Isbn.To13(rawIsbn);
        if (key is null) return null;

        using var conn = _db.Open();
        foreach (var r in conn.Query("SELECT BookId, ISBN13, ISBN10 FROM Books;"))
        {
            string? i13 = r.ISBN13 as string, i10 = r.ISBN10 as string;
            if ((Isbn.To13(i13) ?? Isbn.To13(i10)) == key)
                return (long)r.BookId;
        }
        return null;
    }

    public long Insert(BookDetail b, IReadOnlyList<ContributorInput> contributors, IReadOnlyList<string> categories)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        var bookId = conn.ExecuteScalar<long>(InsertBookSql, b, tx);

        foreach (var c in contributors) LinkContributor(conn, tx, bookId, c);
        foreach (var cat in categories) LinkCategory(conn, tx, bookId, cat);

        tx.Commit();
        return bookId;
    }

    public void InsertMany(IReadOnlyList<(BookDetail Book, IReadOnlyList<ContributorInput> Contributors,
        IReadOnlyList<string> Categories, IReadOnlyList<PictureInput> Pictures)> items)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        foreach (var (book, contribs, cats, pics) in items)
        {
            var bookId = conn.ExecuteScalar<long>(InsertBookSql, book, tx);
            foreach (var c   in contribs) LinkContributor(conn, tx, bookId, c);
            foreach (var cat in cats)     LinkCategory(conn, tx, bookId, cat);
            foreach (var pic in pics)     LinkPicture(conn, tx, bookId, pic);
        }

        tx.Commit();
    }

    public void Update(BookDetail b, IReadOnlyList<ContributorInput> contributors, IReadOnlyList<string> categories)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        conn.Execute(
            """
            UPDATE Books SET
                Title=@Title, OriginalTitle=@OriginalTitle, LanguageId=@LanguageId,
                OriginalLanguageId=@OriginalLanguageId, YearPublished=@YearPublished, YearCopyright=@YearCopyright,
                Edition=@Edition, OriginalEdition=@OriginalEdition, ISBN13=@Isbn13, ISBN10=@Isbn10,
                CoverId=@CoverId, Pages=@Pages, Description=@Description, Shelf=@Shelf, IsRead=@IsRead, Notes=@Notes,
                PublisherId=@PublisherId, CityId=@CityId, InitialPrice=@InitialPrice,
                DateOfBuy=@DateOfBuy, DateOfLastRead=@DateOfLastRead
            WHERE BookId=@Id;
            """,
            b, tx);

        conn.Execute("DELETE FROM BookContributors WHERE BookId = @Id;", new { b.Id }, tx);
        foreach (var c in contributors) LinkContributor(conn, tx, b.Id, c);

        conn.Execute("DELETE FROM BookCategories WHERE BookId = @Id;", new { b.Id }, tx);
        foreach (var cat in categories) LinkCategory(conn, tx, b.Id, cat);

        tx.Commit();
    }

    public void Delete(long bookId)
    {
        using var conn = _db.Open();
        conn.Execute("DELETE FROM Books WHERE BookId = @bookId;", new { bookId });
    }

    public IReadOnlyList<BookExportRow> GetExportRows()
    {
        using var conn = _db.Open();
    return conn.Query<BookExportRow>(
        """
        SELECT b.Title,
               b.OriginalTitle,
               (SELECT GROUP_CONCAT(COALESCE(p.SortName, p.LastName), '; ')
                FROM BookContributors bc JOIN People p ON p.PersonId = bc.PersonId
                JOIN Roles r ON r.RoleId = bc.RoleId
                WHERE bc.BookId = b.BookId AND r.Role = 'Author')      AS Authors,
               (SELECT GROUP_CONCAT(COALESCE(p.SortName, p.LastName), '; ')
                FROM BookContributors bc JOIN People p ON p.PersonId = bc.PersonId
                JOIN Roles r ON r.RoleId = bc.RoleId
                WHERE bc.BookId = b.BookId AND r.Role = 'Translator')  AS Translators,
               (SELECT GROUP_CONCAT(COALESCE(p.SortName, p.LastName), '; ')
                FROM BookContributors bc JOIN People p ON p.PersonId = bc.PersonId
                JOIN Roles r ON r.RoleId = bc.RoleId
                WHERE bc.BookId = b.BookId AND r.Role = 'Illustrator') AS Illustrators,
               (SELECT GROUP_CONCAT(COALESCE(p.SortName, p.LastName), '; ')
                FROM BookContributors bc JOIN People p ON p.PersonId = bc.PersonId
                JOIN Roles r ON r.RoleId = bc.RoleId
                WHERE bc.BookId = b.BookId AND r.Role = 'Editor')      AS Editors,
               l.Language   AS Language,
               ol.Language  AS OriginalLanguage,
               b.YearPublished, b.YearCopyright,
               b.Edition, b.OriginalEdition,
               pub.Publisher AS Publisher,
               cty.City      AS City,
               b.ISBN13      AS Isbn13,
               b.ISBN10      AS Isbn10,
               cov.Cover     AS Cover,
               b.Pages, b.Shelf, b.IsRead, b.InitialPrice,
               b.DateOfBuy, b.DateOfLastRead,
               b.Description, b.Notes,
               (SELECT GROUP_CONCAT(c.Category, '; ')
                FROM   BookCategories bc JOIN Categories c ON c.CategoryId = bc.CategoryId
                WHERE  bc.BookId = b.BookId) AS Categories,
                (SELECT GROUP_CONCAT(CASE WHEN bp.IsPrimary = 1 THEN '*' || bp.Path ELSE bp.Path END, '; ')
                FROM   BookPictures bp
                WHERE  bp.BookId = b.BookId) AS Pictures
        FROM        Books b
        LEFT JOIN   Languages  l   ON l.LanguageId    = b.LanguageId
        LEFT JOIN   Languages  ol  ON ol.LanguageId   = b.OriginalLanguageId
        LEFT JOIN   Publishers pub ON pub.PublisherId = b.PublisherId
        LEFT JOIN   Cities     cty ON cty.CityId      = b.CityId
        LEFT JOIN   Covers     cov ON cov.CoverId     = b.CoverId
        ORDER BY    b.Title;
        """).AsList();
    }
    
    public int Count()
    {
        using var conn = _db.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Books;");
    }

    public void ReplaceAll(IReadOnlyList<(BookDetail Book, IReadOnlyList<ContributorInput> Contributors,
        IReadOnlyList<string> Categories, IReadOnlyList<PictureInput> Pictures)> items)
    {
        using var conn = _db.Open();
        using var tx   = conn.BeginTransaction();

        conn.Execute("DELETE FROM Books;", transaction: tx);   // cascades to junctions + BookPictures rows

        foreach (var (book, contribs, cats, pics) in items)
        {
            var bookId = conn.ExecuteScalar<long>(InsertBookSql, book, tx);
            foreach (var c   in contribs) LinkContributor(conn, tx, bookId, c);
            foreach (var cat in cats)     LinkCategory(conn, tx, bookId, cat);
            foreach (var pic in pics)     LinkPicture(conn, tx, bookId, pic);
        }

        tx.Commit();
    }

    private static void LinkContributor(IDbConnection conn, IDbTransaction tx, long bookId, ContributorInput c)
    {
        long? personId = c.PersonId;

        if (personId is null && !string.IsNullOrWhiteSpace(c.LastName))
        {
            var first = string.IsNullOrWhiteSpace(c.FirstName) ? null : c.FirstName!.Trim();
            var last  = c.LastName!.Trim();

            personId = conn.ExecuteScalar<long?>(
                "SELECT PersonId FROM People WHERE LastName = @last AND IFNULL(FirstName,'') = IFNULL(@first,'');",
                new { last, first }, tx);

            personId ??= conn.ExecuteScalar<long>(
                """
                INSERT INTO People (FirstName, LastName, SortName)
                VALUES (@first, @last, @sort);
                SELECT last_insert_rowid();
                """,
                new { first, last, sort = first is null ? last : $"{last}, {first}" }, tx);
        }

        if (personId is not null)
            conn.Execute(
                """
                INSERT OR IGNORE INTO BookContributors (BookId, PersonId, RoleId)
                VALUES (@bookId, @personId, @roleId);
                """,
                new { bookId, personId, roleId = c.RoleId }, tx);
    }
    
    private static void LinkCategory(IDbConnection conn, IDbTransaction tx, long bookId, string name)
    {
        var n = name.Trim();
        if (n.Length == 0) return;

        var id = conn.ExecuteScalar<long?>(
            "SELECT CategoryId FROM Categories WHERE Category = @n;", new { n }, tx);
        id ??= conn.ExecuteScalar<long>(
            "INSERT INTO Categories (Category) VALUES (@n); SELECT last_insert_rowid();", new { n }, tx);

        conn.Execute(
            "INSERT OR IGNORE INTO BookCategories (BookId, CategoryId) VALUES (@bookId, @id);",
            new { bookId, id }, tx);
    }
    
    private static void LinkPicture(IDbConnection conn, IDbTransaction tx, long bookId, PictureInput pic)
    {
        conn.Execute(
            "INSERT INTO BookPictures (BookId, Path, IsPrimary) VALUES (@bookId, @path, @primary);",
            new { bookId, path = pic.Path, primary = pic.IsPrimary ? 1 : 0 }, tx);
    }
}