using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BooksDb.Data;

namespace BooksDb.ViewModels;

/// <summary>
/// Bounded, thread-safe LRU cache of decoded cover thumbnails for the shelf wall.
/// Keyed by a book's relative cover path. Decoding runs off the UI thread; concurrent
/// requests for the same cover share a single decode; the same Bitmap instance is handed
/// to every tile that shows that cover. Never throws — a miss/failure yields null.
/// </summary>
public sealed class CoverCache
{
    private sealed class Entry { public string Key = ""; public Bitmap? Bitmap; }

    private readonly PictureStore _store;
    private readonly int _capacity;
    private readonly int _decodeWidth;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();                 // most-recently used at the front
    private readonly Dictionary<string, Task<Bitmap?>> _inflight = new();

    public CoverCache(PictureStore store, int capacity = 300, int decodeWidth = 200)
    {
        _store       = store;
        _capacity    = capacity;
        _decodeWidth = decodeWidth;
    }

    /// <summary>Synchronous fast path — true (and the bitmap) if it's already decoded.</summary>
    public bool TryGet(string? coverPath, out Bitmap? bitmap)
    {
        bitmap = null;
        if (string.IsNullOrEmpty(coverPath)) return false;
        lock (_gate)
        {
            if (!_map.TryGetValue(coverPath, out var node)) return false;
            _lru.Remove(node);
            _lru.AddFirst(node);                                     // touch
            bitmap = node.Value.Bitmap;
            return true;
        }
    }

    /// <summary>Returns the cover thumbnail, decoding once off-thread on a miss.</summary>
    public Task<Bitmap?> GetAsync(string? coverPath)
    {
        if (string.IsNullOrEmpty(coverPath)) return Task.FromResult<Bitmap?>(null);
        lock (_gate)
        {
            if (_map.TryGetValue(coverPath, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return Task.FromResult(node.Value.Bitmap);
            }
            if (_inflight.TryGetValue(coverPath, out var pending))
                return pending;

            var task = DecodeAndStoreAsync(coverPath);
            _inflight[coverPath] = task;
            return task;
        }
    }

    private async Task<Bitmap?> DecodeAndStoreAsync(string key)
    {
        Bitmap? bmp = null;
        try { bmp = await Task.Run(() => Decode(_store.AbsolutePath(key))); }
        catch { bmp = null; }

        lock (_gate)
        {
            _inflight.Remove(key);
            if (bmp is null) return null;

            if (_map.TryGetValue(key, out var existing))             // lost a decode race
            {
                bmp.Dispose();                                       // never shown → safe to dispose
                return existing.Value.Bitmap;
            }

            var node = new LinkedListNode<Entry>(new Entry { Key = key, Bitmap = bmp });
            _lru.AddFirst(node);
            _map[key] = node;
            Evict();
        }
        return bmp;
    }

    private Bitmap? Decode(string absPath)
    {
        try
        {
            using var fs = ImageOrient.OrientedStream(absPath);
            return Bitmap.DecodeToWidth(fs, _decodeWidth);
        }
        catch { return null; }
    }

    private void Evict()   // caller holds _gate
    {
        while (_map.Count > _capacity)
        {
            var oldest = _lru.Last;
            if (oldest is null) break;
            _lru.RemoveLast();
            _map.Remove(oldest.Value.Key);
            // Drop the reference only — let GC reclaim. Disposing here could hit a bitmap a
            // recycled tile still references; the count cap already bounds live memory.
        }
    }

    /// <summary>Drop everything (e.g. after a bulk library change). Tiles re-request on next view.</summary>
    public void Clear()
    {
        lock (_gate) { _lru.Clear(); _map.Clear(); }
    }
}