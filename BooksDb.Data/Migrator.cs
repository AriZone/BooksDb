using System.Reflection;

namespace BooksDb.Data;

public static class Migrator
{
    public static void EnsureUpToDate(Database db)
    {
        using var conn = db.Open();

        long version;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            version = (long)(cmd.ExecuteScalar() ?? 0L);
        }

        if (version < 1)
        {
            var schema = ReadEmbedded("booksdb_schema.sql");
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = schema;     // creates everything and sets user_version = 1
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        if (version < 2)
        {
            var script = ReadEmbedded("migration_002_pending_captures.sql");
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = script;     // adds PendingCaptures + PendingCapturePhotos, sets user_version = 2
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        // future migrations: if (version < 3) { ...apply v3 script... }
    }

    private static string ReadEmbedded(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}