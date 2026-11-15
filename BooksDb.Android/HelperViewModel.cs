using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BooksDb.Android;

public partial class HelperViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _isbn = "";

    [ObservableProperty] private string _shelf = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private CapturedItem? _selectedCaptured;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    public ObservableCollection<CapturedItem> Captured { get; } = new();

    // --- Disk persistence -------------------------------------------------------------------------
    // Each captured JPEG is written to disk off-thread the moment it is taken, and a tiny JSON index
    // is rewritten on every buffer change. The on-disk copy is therefore always current, so a crash
    // or process kill loses at most the single in-flight frame. The buffer is rebuilt from disk at
    // launch — which is what makes it survive crashes, not just a clean exit.
    private readonly CaptureStore _store = new();
    private bool _restoring;   // suppresses index writes while we rebuild the buffer at startup

    public HelperViewModel()
    {
        RestoreSession();
        Captured.CollectionChanged += OnCapturedChanged;
    }

    /// <summary>Rebuild the buffer from the on-disk index at launch. Tolerant: a missing or
    /// half-written photo file is skipped, and a corrupt index just yields an empty buffer — a bad
    /// dump must never block startup.</summary>
    private void RestoreSession()
    {
        _restoring = true;
        try
        {
            var index = _store.LoadIndex();
            var keep = new HashSet<string>();
            if (index is not null)
            {
                foreach (var col in index.Collections)
                {
                    var item = new CapturedItem(col.Isbn ?? "", col.Shelf ?? "");
                    int n = 1;
                    foreach (var name in col.Photos)
                    {
                        var bytes = _store.ReadPhoto(name);
                        if (bytes is null) continue;                 // tolerate a missing/half-written file
                        item.Photos.Add(new CapturedPhoto(bytes, n++) { FileName = name });
                        keep.Add(name);
                    }
                    Captured.Add(item);                              // preserve order; empty collections survive too
                    item.Photos.CollectionChanged += OnPhotosChanged;
                }
            }
            _store.SweepOrphans(keep);                              // drop files no longer referenced
            if (Captured.Count > 0)
            {
                SelectedCaptured = Captured.FirstOrDefault(c => !string.IsNullOrEmpty(c.Isbn)) ?? Captured[0];
                Status = $"Restored {Captured.Count} collection{(Captured.Count == 1 ? "" : "s")} from last session.";
            }
        }
        catch { /* never let a bad dump block startup */ }
        finally { _restoring = false; }
    }

    // Captured-level changes: (un)subscribe each item's photo list, delete a removed collection's
    // files, and re-persist the index. Send() and RemoveCollection() both Remove(item) → flow here.
    private void OnCapturedChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (CapturedItem it in e.NewItems)
                it.Photos.CollectionChanged += OnPhotosChanged;

        if (e.OldItems is not null)
            foreach (CapturedItem it in e.OldItems)
            {
                it.Photos.CollectionChanged -= OnPhotosChanged;
                _store.EnqueueDelete(it.Photos.Select(p => p.FileName));
            }

        PersistIndex();
    }

    // Photo-level changes inside a collection (capture adds; gallery delete removes gp.Source, which is
    // the same CapturedPhoto). Renumber-on-delete mutates Number on the existing object, so it does NOT
    // raise a collection change and won't churn the index here.
    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            _store.EnqueueDelete(e.OldItems.OfType<CapturedPhoto>().Select(p => p.FileName));

        PersistIndex();
    }

    // Snapshot the whole buffer (UI thread) and hand it to the writer, which serializes + writes it
    // atomically off-thread. The buffer is small, so rewriting it on every change is cheap.
    private void PersistIndex()
    {
        if (_restoring) return;
        var root = new CaptureStore.IndexRoot();
        foreach (var it in Captured)
        {
            var ic = new CaptureStore.IndexCollection { Isbn = it.Isbn, Shelf = it.Shelf };
            foreach (var p in it.Photos)
                if (!string.IsNullOrEmpty(p.FileName)) ic.Photos.Add(p.FileName!);
            root.Collections.Add(ic);
        }
        _store.EnqueueIndex(root);
    }

    /// <summary>Raised when a collection row is swiped left — the Shell opens its review gallery.</summary>
    public event Action<CapturedItem>? GalleryRequested;

    public void RequestGallery(CapturedItem item) => GalleryRequested?.Invoke(item);

    /// <summary>Drop every pending capture/photo (the unsent buffer).</summary>
    public void ClearBuffer()
    {
        foreach (var it in Captured)
            it.Photos.CollectionChanged -= OnPhotosChanged;   // Clear() raises Reset (no OldItems) → unsubscribe here
        Captured.Clear();
        SelectedCaptured = null;
        _store.ClearAll();                                    // wipe files + index from disk
        Status = "Buffer cleared.";
    }

    /// <summary>Drop a single collection (one ISBN book or the unassigned-photos batch).</summary>
    public void RemoveCollection(CapturedItem item)
    {
        Captured.Remove(item);
        Status = Captured.Count == 0 ? "Buffer empty." : "Collection removed.";
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var v = Isbn.Trim();
        if (v.Length == 0) return;

        var existing = Captured.FirstOrDefault(c => c.Isbn == v);
        if (existing is not null)
        {
            SelectedCaptured = existing;     // already in the batch → re-activate, don't duplicate
            Status = $"Book {v} is already in the batch — selected.";
        }
        else
        {
            var item = new CapturedItem(v, Shelf.Trim());
            Captured.Insert(0, item);
            SelectedCaptured = item;         // newest book becomes the photo target
        }
        Isbn = "";
    }
    
    /// <summary>Capture into a single ISBN-less "loose" batch (created on first use) → desktop pocket.</summary>
    [RelayCommand]
    public void AttachLoosePhoto(byte[] bytes)
    {
        var item = Captured.FirstOrDefault(c => string.IsNullOrEmpty(c.Isbn));
        if (item is null)
        {
            item = new CapturedItem("", "");
            Captured.Insert(0, item);
        }
        SelectedCaptured = item;
        var photo = new CapturedPhoto(bytes, item.Photos.Count + 1) { FileName = _store.NewPhotoName() };
        _store.EnqueueWrite(photo.FileName!, bytes);   // write the JPEG before the index references it
        item.Photos.Add(photo);                        // → OnPhotosChanged re-persists the index
        Isbn = "";   // switching to loose mode → drop any pending/scanned ISBN
        Status = $"Loose photo added — {item.Photos.Count} in this batch.";
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(Isbn);

    /// <summary>Attach a freshly captured JPEG to the active book. Does nothing without one
    /// (e.g. after a loose photo switched the active context to the unassigned batch).</summary>
    public void AttachPhoto(byte[] bytes)
    {
        if (SelectedCaptured is not { } item || string.IsNullOrWhiteSpace(item.Isbn))
        {
            Status = "Scan or add a book first, then capture its photos.";
            return;
        }
        var photo = new CapturedPhoto(bytes, item.Photos.Count + 1) { FileName = _store.NewPhotoName() };
        _store.EnqueueWrite(photo.FileName!, bytes);   // write the JPEG before the index references it
        item.Photos.Add(photo);                        // → OnPhotosChanged re-persists the index
        Status = $"Attached photo to {item.Isbn} — {item.Photos.Count} so far.";
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        var settings = SettingsStore.Load();
        var baseUrl  = settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Status = "Set the desktop address in Settings (⚙) first.";
            return;
        }
        if (Captured.Count == 0)
        {
            Status = "Nothing to send.";
            return;
        }

        IsSending = true;
        Status = "Sending…";

        var sessionId = Guid.NewGuid().ToString("N");
        var items = Captured.ToList();   // snapshot; remove from Captured as each succeeds
        int sent = 0, failed = 0, photosSent = 0;

        foreach (var item in items)
        {
            var photos = item.Photos.Select(p => p.Data).ToList();
            var ok = await CaptureSender.SendAsync(
                baseUrl, settings.Token, sessionId, item.Isbn, item.Shelf, photos);
            if (ok) { Captured.Remove(item); sent++; photosSent += photos.Count; }
            else failed++;
        }

        IsSending = false;
        var summary = $"Sent {sent} ({photosSent} photo{(photosSent == 1 ? "" : "s")})";
        Status = failed == 0
            ? $"{summary}."
            : $"{summary}, {failed} failed — check the address and that the desktop is running.";
    }

    /// <summary>Send a single collection (swipe-right on its row → native confirm). Mirrors the bulk
    /// <see cref="Send"/>: on success the collection is removed, which also deletes its files from disk
    /// and rewrites the index via the CollectionChanged hook.</summary>
    public async Task SendCollectionAsync(CapturedItem item)
    {
        if (IsSending) return;

        var settings = SettingsStore.Load();
        var baseUrl  = settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Status = "Set the desktop address in Settings (⚙) first.";
            return;
        }

        IsSending = true;
        Status = "Sending…";

        var sessionId = Guid.NewGuid().ToString("N");
        var photos = item.Photos.Select(p => p.Data).ToList();
        var ok = await CaptureSender.SendAsync(
            baseUrl, settings.Token, sessionId, item.Isbn, item.Shelf, photos);

        IsSending = false;
        if (ok)
        {
            Captured.Remove(item);   // → OnCapturedChanged deletes its files + re-persists the index
            if (ReferenceEquals(SelectedCaptured, item)) SelectedCaptured = null;
            Status = $"Sent {photos.Count} photo{(photos.Count == 1 ? "" : "s")}.";
        }
        else
        {
            Status = "Send failed — check the address and that the desktop is running.";
        }
    }

    private bool CanSend() => !IsSending;
}