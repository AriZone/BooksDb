using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using BooksDb.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public sealed partial class PocketItem : ObservableObject
{
    public long    PhotoId   { get; init; }
    public long    CaptureId { get; init; }
    public string  Path      { get; init; } = "";
    [ObservableProperty] private Bitmap? _thumb;
}

public partial class PocketDialog : Window
{
    private readonly PendingCaptureRepository _captures = null!;
    private readonly PictureRepository _pictures = null!;
    private readonly PictureStore _store = null!;
    private readonly ObservableCollection<PocketItem> _items = new();
    private readonly Dictionary<long, Bitmap> _thumbCache = new();

    public PocketDialog() => InitializeComponent();   // designer

    public PocketDialog(PendingCaptureRepository captures, PictureRepository pictures, PictureStore store)
    {
        InitializeComponent();
        _captures = captures;
        _pictures = pictures;
        _store    = store;
        PocketList.ItemsSource = _items;
        PocketList.SelectionChanged += (_, _) =>
        {
            var any = PocketList.SelectedItems is { Count: > 0 };
            AssignButton.IsEnabled = any;
            RemoveButton.IsEnabled = any;
            RotateLeftButton.IsEnabled  = any;
            RotateRightButton.IsEnabled = any;
        };
        _ = ReloadAsync();   // don't block the window from opening
    }

    private async Task ReloadAsync()
    {
        _items.Clear();
        AssignButton.IsEnabled = false;
        RemoveButton.IsEnabled = false;
        RotateLeftButton.IsEnabled  = false;
        RotateRightButton.IsEnabled = false;

        var rows = _captures.GetPocketPhotos();   // cheap DB read
        EmptyText.IsVisible  = rows.Count == 0;
        PocketList.IsVisible = rows.Count > 0;

        // Drop cached thumbnails for photos that are gone (assigned/removed).
        var live = rows.Select(r => r.PhotoId).ToHashSet();
        foreach (var id in _thumbCache.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _thumbCache[id].Dispose();
            _thumbCache.Remove(id);
        }

        // Decode only what we don't already have — in parallel, off the UI thread.
        var todo = rows.Where(r => !_thumbCache.ContainsKey(r.PhotoId)).ToList();
        if (todo.Count > 0)
        {
            var decoded = await Task.Run(() =>
            {
                var map = new Dictionary<long, Bitmap>();
                Parallel.ForEach(todo,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    p =>
                    {
                        try
                        {
                            using var s = ImageOrient.ThumbStream(_store.AbsolutePath(p.Path), 160);
                            var bmp = Bitmap.DecodeToWidth(s, 160);
                            lock (map) map[p.PhotoId] = bmp;
                        }
                        catch { /* skip unreadable */ }
                    });
                return map;
            });

            foreach (var kv in decoded) _thumbCache[kv.Key] = kv.Value;
        }

        // Build the list in order from the cache — instant on Refresh/Remove (nothing to decode).
        foreach (var p in rows)
        {
            _thumbCache.TryGetValue(p.PhotoId, out var thumb);
            _items.Add(new PocketItem
            {
                PhotoId   = p.PhotoId,
                CaptureId = p.CaptureId,
                Path      = p.Path,
                Thumb     = thumb,
            });
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        foreach (var b in _thumbCache.Values) b.Dispose();
        _thumbCache.Clear();
        base.OnClosed(e);
    }

    private async void OnAssign(object? sender, RoutedEventArgs e)
    {
        var selected = PocketList.SelectedItems?.Cast<PocketItem>().ToList() ?? new List<PocketItem>();
        if (selected.Count == 0) return;

        var book = await BookPickerDialog.ShowAsync(this);
        if (book is null) return;

        bool first = _pictures.GetForBook(book.Id).Count == 0;
        foreach (var item in selected)
        {
            try
            {
                _pictures.Add(book.Id, _store.AbsolutePath(item.Path), makePrimary: first);
                first = false;
            }
            catch { /* skip a bad file, keep the rest */ }
        }

        _captures.RemovePocketPhotos(selected.Select(s => s.PhotoId).ToList());
        await ReloadAsync();
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        var selected = PocketList.SelectedItems?.Cast<PocketItem>().ToList() ?? new List<PocketItem>();
        if (selected.Count == 0) return;

        var n = selected.Count;
        var ok = await MessageDialog.ConfirmAsync(this, "Remove photos",
            $"Remove {n} photo{(n == 1 ? "" : "s")} from the pocket? This permanently deletes the staged file{(n == 1 ? "" : "s")}.");
        if (!ok) return;

        _captures.RemovePocketPhotos(selected.Select(s => s.PhotoId).ToList());
        await ReloadAsync();
    }

    private async void OnPhotoDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not PocketItem item) return;

        var paths = _items.Select(p => _store.AbsolutePath(p.Path)).ToList();
        if (paths.Count == 0) return;

        var start = _items.IndexOf(item);
        if (start < 0) start = 0;

        var changed = await PictureViewerWindow.ShowAsync(this, paths, start, "Unassigned photo", allowRotate: true);
        if (changed)
        {
            // a rotation in the viewer edited files in place; rebuild every thumbnail (the viewer
            // reports only that *something* changed, not which photo).
            foreach (var b in _thumbCache.Values) b.Dispose();
            _thumbCache.Clear();
            await ReloadAsync();
        }
    }

    private async void OnRotateLeft(object? sender, RoutedEventArgs e)  => await RotateSelectedAsync(clockwise: false);
    private async void OnRotateRight(object? sender, RoutedEventArgs e) => await RotateSelectedAsync(clockwise: true);

    private async Task RotateSelectedAsync(bool clockwise)
    {
        var selected = PocketList.SelectedItems?.Cast<PocketItem>().ToList() ?? new List<PocketItem>();
        if (selected.Count == 0) return;

        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = false;   // guard re-entry mid-rotate

        await Task.Run(() =>
        {
            foreach (var it in selected)
                try { ImageOrient.RotateAndSave(_store.AbsolutePath(it.Path), clockwise); }
                catch { /* skip a bad file, keep the rest */ }
        });

        await RefreshThumbsAsync(selected);

        // selection is preserved (same item instances), so re-enable for repeat rotations (e.g. 180°)
        var any = PocketList.SelectedItems is { Count: > 0 };
        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = any;
    }

    // Re-decode the given items' thumbnails in place. The PocketItem instances are reused, so the
    // ListBox selection survives — only each tile's Thumb swaps to the freshly-rotated bitmap.
    private async Task RefreshThumbsAsync(IReadOnlyList<PocketItem> items)
    {
        var decoded = await Task.Run(() =>
        {
            var map = new Dictionary<long, Bitmap>();
            foreach (var it in items)
            {
                try
                {
                    using var s = ImageOrient.ThumbStream(_store.AbsolutePath(it.Path), 160);
                    map[it.PhotoId] = Bitmap.DecodeToWidth(s, 160);
                }
                catch { /* leave the existing thumb in place */ }
            }
            return map;
        });

        foreach (var it in items)
        {
            if (!decoded.TryGetValue(it.PhotoId, out var bmp)) continue;
            _thumbCache.TryGetValue(it.PhotoId, out var old);
            _thumbCache[it.PhotoId] = bmp;
            it.Thumb = bmp;     // swap the display to the new bitmap first…
            old?.Dispose();     // …then free the old one
        }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = ReloadAsync();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner)
    {
        var captures = App.Services.GetRequiredService<PendingCaptureRepository>();
        var pictures = App.Services.GetRequiredService<PictureRepository>();
        var store    = App.Services.GetRequiredService<PictureStore>();
        return new PocketDialog(captures, pictures, store).ShowDialog(owner);
    }
}
