using Microsoft.Data.Sqlite;

namespace BooksDb.Data;

public sealed class Database
{
    private string _connectionString = "";

    /// <summary>Filesystem path of the database currently in use.</summary>
    public string CurrentPath { get; private set; } = "";

    public Database(string dbPath) => SwitchTo(dbPath);

    /// <summary>Point the connection factory at a different database file. The directory is
    /// created if needed; the file itself is created on the first <see cref="Open"/> (callers
    /// run <c>Migrator.EnsureUpToDate</c> to build/upgrade the schema). Connections are opened
    /// per call, so this takes effect immediately and never disturbs an in-flight connection.</summary>
    public void SwitchTo(string dbPath)
    {
        var full = Path.GetFullPath(dbPath);
        var dir  = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        CurrentPath = full;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource   = full,
            Mode         = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys  = true,          // issues PRAGMA foreign_keys=ON on every open
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();

        // diacritic/case folding for accent-insensitive LIKE search
        conn.CreateFunction("npl_fold", (string? s) => PolishText.Fold(s), isDeterministic: true);

        return conn;
    }

    public static string DefaultPath() => Path.Combine(DefaultDirectory(), "books.db");

    /// <summary>The app's config directory (~/.config/BooksDb on Linux, %AppData%\BooksDb on
    /// Windows) — the default home for new databases, and where settings/pictures live.</summary>
    public static string DefaultDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BooksDb");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
