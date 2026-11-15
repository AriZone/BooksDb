using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BooksDb.Android;

/// <summary>
/// Review gallery for a single collection (one ISBN's photos, or the unassigned batch). Step 1 is a
/// read-only thumbnail grid; selection / delete / fullscreen arrive in later steps.
/// </summary>
public partial class GalleryViewModel : ObservableObject
{
    private readonly CapturedItem _item;

    public string Title { get; }
    public ObservableCollection<GalleryPhoto> Photos { get; } = new();

    [ObservableProperty] private bool _selectionMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;
    public string SelectionText => $"{SelectedCount} selected";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullscreen))]
    [NotifyPropertyChangedFor(nameof(FullscreenCheckVisible))]
    private GalleryPhoto? _fullscreen;

    public bool IsFullscreen => Fullscreen is not null;

    // The fullscreen mark circle shows only while previewing and not zoomed in.
    public bool FullscreenCheckVisible => IsFullscreen && !IsZoomed;

    // Larger decode for the fullscreen view (the grid thumb is only 320px wide).
    [ObservableProperty] private Bitmap? _fullImage;

    // Thumbnail tile size, recomputed by the view as the pinch gesture changes the column count
    // (1–4 columns, default 3). Defaults keep the original 112×150 look until the first layout pass.
    [ObservableProperty] private double _tileWidth = 112;
    [ObservableProperty] private double _tileHeight = 150;

    // Pinch-zoom state lives in the view (it owns the transform); the VM just tracks whether we're
    // zoomed (for the back hierarchy + hiding the mark circle) and asks the view to reset.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullscreenCheckVisible))]
    private bool _isZoomed;
    public event Action? ResetZoomRequested;

    /// <summary>Raised when the gallery should close and the Shell should return to the Helper.</summary>
    public event Action? Done;

    /// <summary>Raised when a delete empties the collection — the Shell drops it and returns to Helper.</summary>
    public event Action<CapturedItem>? CollectionEmptied;

    public GalleryViewModel(CapturedItem item)
    {
        _item = item;
        Title = string.IsNullOrWhiteSpace(item.Isbn) ? "Unassigned photos" : item.Isbn;
        foreach (var p in item.Photos)
            Photos.Add(new GalleryPhoto(p));
        _ = LoadThumbsAsync();
    }

    public string CountText => $"{Photos.Count} photo{(Photos.Count == 1 ? "" : "s")}";

    private async Task LoadThumbsAsync()
    {
        foreach (var gp in Photos)
        {
            var data = gp.Source.Data;
            var bmp = await Task.Run(() =>
            {
                try { using var ms = new MemoryStream(data); return Bitmap.DecodeToWidth(ms, 320); }
                catch { return (Bitmap?)null; }
            });
            if (bmp is not null) gp.Thumb = bmp;   // back on the UI thread after await
        }
    }

    /// <summary>Handle the hardware back button internally (fullscreen / selection levels). Returns
    /// false when nothing was consumed, i.e. the Shell should leave the gallery. Extended in later steps.</summary>
    public bool TryBack()
    {
        if (Fullscreen is not null)
        {
            if (IsZoomed) { ResetZoomRequested?.Invoke(); return true; }   // back: reset zoom
            CloseFullscreen();                                            // back: exit fullscreen
            return true;
        }
        if (SelectionMode) { ExitSelection(); return true; }
        return false;
    }

    // --- Fullscreen preview ---------------------------------------------------------------------

    public void OpenFullscreen(GalleryPhoto gp)
    {
        Fullscreen = gp;
        IsZoomed = false;
        ResetZoomRequested?.Invoke();   // clear any leftover transform from a previous photo
        _ = LoadFullAsync(gp);
    }

    private async Task LoadFullAsync(GalleryPhoto gp)
    {
        var data = gp.Source.Data;
        var bmp = await Task.Run(() =>
        {
            try { using var ms = new MemoryStream(data); return Bitmap.DecodeToWidth(ms, 1440); }
            catch { return (Bitmap?)null; }
        });

        if (Fullscreen == gp)   // still showing the same photo
        {
            var old = FullImage;
            FullImage = bmp;
            old?.Dispose();
        }
        else bmp?.Dispose();    // user already closed / switched
    }

    public void CloseFullscreen()
    {
        Fullscreen = null;
        IsZoomed = false;
        ResetZoomRequested?.Invoke();
        var img = FullImage;
        FullImage = null;
        img?.Dispose();
    }

    // Step to the neighbouring photo in fullscreen. No looping: returns false at the ends.
    public bool ShowNext()
    {
        if (Fullscreen is null) return false;
        int i = Photos.IndexOf(Fullscreen);
        if (i < 0 || i >= Photos.Count - 1) return false;   // already at the last
        OpenFullscreen(Photos[i + 1]);
        return true;
    }

    public bool ShowPrev()
    {
        if (Fullscreen is null) return false;
        int i = Photos.IndexOf(Fullscreen);
        if (i <= 0) return false;                            // already at the first
        OpenFullscreen(Photos[i - 1]);
        return true;
    }

    // --- Selection mode -------------------------------------------------------------------------

    /// <summary>Toggle the current fullscreen photo's mark. The first mark enters selection mode, so on
    /// closing fullscreen the thumbnail grid is already showing the checks.</summary>
    public void ToggleFullscreenCheck()
    {
        if (Fullscreen is null) return;
        if (SelectionMode) Toggle(Fullscreen);
        else EnterSelection(Fullscreen);
    }

    public void EnterSelection(GalleryPhoto first)
    {
        SelectionMode = true;
        foreach (var gp in Photos) { gp.ShowCircle = true; gp.IsSelected = false; }
        first.IsSelected = true;
        Recount();
    }

    public void ExitSelection()
    {
        SelectionMode = false;
        foreach (var gp in Photos) { gp.ShowCircle = false; gp.IsSelected = false; }
        SelectedCount = 0;
    }

    public void Toggle(GalleryPhoto gp)
    {
        if (!SelectionMode) return;
        gp.IsSelected = !gp.IsSelected;
        Recount();
    }

    private void Recount() => SelectedCount = Photos.Count(p => p.IsSelected);

    /// <summary>Delete the checked photos (called after the native confirm), renumber the rest, and
    /// drop the whole collection if it empties.</summary>
    public void DeleteSelected()
    {
        var toRemove = Photos.Where(p => p.IsSelected).ToList();
        if (toRemove.Count == 0) return;

        foreach (var gp in toRemove)
        {
            _item.Photos.Remove(gp.Source);   // the real collection → Helper row updates live
            gp.Thumb?.Dispose();
            Photos.Remove(gp);
        }

        for (int i = 0; i < _item.Photos.Count; i++)   // renumber, closing gaps
            _item.Photos[i].Number = i + 1;

        ExitSelection();
        OnPropertyChanged(nameof(CountText));

        if (_item.Photos.Count == 0)
        {
            CollectionEmptied?.Invoke(_item);
            Close();
        }
    }

    public void Close() => Done?.Invoke();

    /// <summary>Dispose decoded bitmaps when leaving — these are full image frames, not cheap.</summary>
    public void Cleanup()
    {
        FullImage?.Dispose();
        FullImage = null;
        foreach (var gp in Photos)
        {
            gp.Thumb?.Dispose();
            gp.Thumb = null;
        }
        Photos.Clear();
    }
}

/// <summary>One photo in the gallery: the underlying capture plus a decoded thumbnail and (later) its
/// selection state.</summary>
public partial class GalleryPhoto : ObservableObject
{
    public CapturedPhoto Source { get; }

    [ObservableProperty] private Bitmap? _thumb;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _showCircle;

    public GalleryPhoto(CapturedPhoto source) => Source = source;
}
