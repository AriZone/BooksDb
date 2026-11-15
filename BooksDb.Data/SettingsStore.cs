//using System.IO;
using System.Text.Json;

namespace BooksDb.Data;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettings Current { get; private set; }

    public SettingsStore()
    {
        var dir  = Path.GetDirectoryName(Database.DefaultPath()) ?? Directory.GetCurrentDirectory();
        _path    = Path.Combine(dir, "settings.json");
        Current  = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // A malformed settings.json fails as a whole and silently zeroes *every* setting at
            // once (e.g. one stray comma), so surface it instead of quietly using defaults.
            Console.Error.WriteLine($"[SettingsStore] Could not read {_path}: {ex.Message}");
            Console.Error.WriteLine("[SettingsStore] Using default settings — fix the file and restart.");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Current, Options)); }
        catch { /* a failed settings write shouldn't crash the app */ }
    }
}