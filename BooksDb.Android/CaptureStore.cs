using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BooksDb.Android;

/// <summary>
/// Disk persistence for the unsent capture buffer. Each captured JPEG is written to app-specific
/// external storage (<c>Android/data/&lt;pkg&gt;/files/captures</c> — no runtime permission, reachable
/// over USB/ADB, removed on uninstall) the moment it is taken, and a small <c>index.json</c> records the
/// collections and their photo order. Every write runs on a single serialized background task, so the
/// capture path never blocks and parallel writes never contend. Persistence is best-effort: a failed
/// write is swallowed rather than allowed to crash the app. The buffer is restored from disk at next
/// launch, which is why it survives crashes and process kills — not just a clean exit.
/// </summary>
public sealed class CaptureStore
{
    // ---- on-disk index model (kept tiny; Version lets the format evolve later) -------------------
    public sealed class IndexCollection
    {
        public string Isbn { get; set; } = "";
        public string Shelf { get; set; } = "";
        public List<string> Photos { get; set; } = new();
    }

    public sealed class IndexRoot
    {
        public int Version { get; set; } = 1;
        public List<IndexCollection> Collections { get; set; } = new();
    }

    private readonly string _dir;
    private readonly string _indexPath;

    // Unbounded so an enqueue never blocks the UI/capture thread; a single reader drains it in order.
    private readonly Channel<Action> _jobs =
        Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

    public CaptureStore()
    {
        var external = global::Android.App.Application.Context.GetExternalFilesDir("captures")?.AbsolutePath;
        _dir = external
            ?? Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "captures");
        System.IO.Directory.CreateDirectory(_dir);
        _indexPath = Path.Combine(_dir, "index.json");
        _ = Task.Run(RunWriterAsync);   // long-lived serialized writer
    }

    public string DirectoryPath => _dir;

    public string NewPhotoName() => Guid.NewGuid().ToString("N") + ".jpg";

    private string PathFor(string name) => Path.Combine(_dir, name);

    private async Task RunWriterAsync()
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync())
        {
            try { job(); }
            catch { /* persistence is best-effort — never take the app down over a disk hiccup */ }
        }
    }

    /// <summary>Queue an already-encoded JPEG to be written to disk.</summary>
    public void EnqueueWrite(string name, byte[] data)
    {
        var path = PathFor(name);
        _jobs.Writer.TryWrite(() => File.WriteAllBytes(path, data));
    }

    /// <summary>Queue an atomic rewrite of the index from a snapshot built by the caller (UI thread).</summary>
    public void EnqueueIndex(IndexRoot snapshot)
    {
        // Serialize now, off the live buffer, so the writer thread never reads UI-owned collections.
        var json = JsonSerializer.Serialize(snapshot);
        _jobs.Writer.TryWrite(() =>
        {
            var tmp = _indexPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _indexPath, overwrite: true);   // temp + rename → never a half-written index
        });
    }

    /// <summary>Queue deletion of specific photo files (nulls/blanks ignored).</summary>
    public void EnqueueDelete(IEnumerable<string?> names)
    {
        var paths = names.Where(n => !string.IsNullOrEmpty(n)).Select(n => PathFor(n!)).ToList();
        if (paths.Count == 0) return;
        _jobs.Writer.TryWrite(() =>
        {
            foreach (var p in paths)
                try { if (File.Exists(p)) File.Delete(p); } catch { }
        });
    }

    /// <summary>Queue a full wipe of the capture directory (in-app "Clear buffer").</summary>
    public void ClearAll()
    {
        _jobs.Writer.TryWrite(() =>
        {
            try { foreach (var f in System.IO.Directory.GetFiles(_dir)) File.Delete(f); }
            catch { }
        });
    }

    // ---- startup reads (synchronous; the writer queue is idle at launch) -------------------------

    /// <summary>Load the index, or null if absent/corrupt.</summary>
    public IndexRoot? LoadIndex()
    {
        try
        {
            return File.Exists(_indexPath)
                ? JsonSerializer.Deserialize<IndexRoot>(File.ReadAllText(_indexPath))
                : null;
        }
        catch { return null; }
    }

    /// <summary>Read one photo's bytes, or null if missing/unreadable (tolerant restore).</summary>
    public byte[]? ReadPhoto(string name)
    {
        try
        {
            var p = PathFor(name);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }
        catch { return null; }
    }

    /// <summary>Delete any *.jpg in the directory not in <paramref name="keep"/> (drops files orphaned by
    /// a crash between a photo delete and the index rewrite). The index file itself is never touched.</summary>
    public void SweepOrphans(HashSet<string> keep)
    {
        try
        {
            foreach (var f in System.IO.Directory.GetFiles(_dir, "*.jpg"))
                if (!keep.Contains(Path.GetFileName(f)))
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }
}
