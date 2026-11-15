using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace BooksDb.Android;

public partial class GalleryView : UserControl
{
    private GalleryViewModel? _vm;
    private readonly ScaleTransform _fsScale = new();
    private readonly TranslateTransform _fsTranslate = new();
    private double _scale = 1, _committedScale = 1, _tx, _ty;

    // One-finger pan state (only while zoomed). We track a pointer count so a second finger
    // cleanly hands off to the PinchGestureRecognizer instead of fighting it.
    private int _activePointers;
    private bool _panning;
    private bool _swipeCandidate;          // single-finger gesture started at fit → may be a paging swipe
    private bool _swiping;                  // confirmed horizontal swipe → captured + recognizers blocked
    private bool _swipeFired;              // already paged this gesture → don't page again until release
    private Point _panLast;
    private Point _downPoint;
    private const double SwipeThreshold = 70;   // px of horizontal travel to page to the next/prev photo

    // Thumbnail-grid column count (1–4, default 3), driven by pinch. Tile size is recomputed from the
    // available width so exactly this many columns fit.
    private int _columns = 3;
    private bool _thumbPinchActive;
    private int _committedColumns = 3;
    private readonly HashSet<IPointer> _thumbContacts = new();   // fingers down on the grid (by identity)
    private bool _multiTouchSeen;                        // ≥2 fingers seen this sequence → no long-press until all lift
    private const double TileAspect = 150.0 / 112.0;   // original tile height : width
    private const double TileMargin = 8.0;             // 4px on each side of every tile

    // Manual long-press for selection. The framework Holding event can't be used here: the grid's
    // PinchGestureRecognizer captures the pointer, which moves Holding's source off the tile. So we
    // run our own timer from the tile's PointerPressed (independent of capture).
    private DispatcherTimer? _holdTimer;
    private GalleryPhoto? _holdTarget;
    private Point _holdStart;
    private bool _holdFired;                            // long-press fired → swallow the trailing Tapped
    private const double HoldMoveCancel = 14;          // px of movement that cancels a pending long-press

    public GalleryView()
    {
        InitializeComponent();
        // Scale about the top-left and translate to keep the pinch focus fixed (focal-point zoom).
        FsImage.RenderTransformOrigin = RelativePoint.TopLeft;
        FsImage.RenderTransform = new TransformGroup { Children = { _fsScale, _fsTranslate } };
        AddHandler(InputElement.PinchEvent, OnPinch);
        AddHandler(InputElement.PinchEndedEvent, OnPinchEnded);
        // Count fingers on the thumbnail grid so a long-press during a (possibly still) two-finger pinch
        // doesn't trip selection. handledEventsToo:true because the scroll recognizer can mark releases handled.
        const RoutingStrategies thumbRoutes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
        ThumbScroll.AddHandler(InputElement.PointerPressedEvent, OnThumbPointerPressed, thumbRoutes, handledEventsToo: true);
        ThumbScroll.AddHandler(InputElement.PointerMovedEvent, OnThumbPointerMoved, thumbRoutes, handledEventsToo: true);
        ThumbScroll.AddHandler(InputElement.PointerReleasedEvent, OnThumbPointerReleased, thumbRoutes, handledEventsToo: true);
        ThumbScroll.AddHandler(InputElement.PointerCaptureLostEvent, OnThumbPointerCaptureLost, thumbRoutes, handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.ResetZoomRequested -= ResetZoom;
        _vm = DataContext as GalleryViewModel;
        if (_vm is not null) _vm.ResetZoomRequested += ResetZoom;
        _columns = 3;                 // each gallery opens at the default 3-column layout
        _thumbContacts.Clear();
        _multiTouchSeen = false;
        RecomputeTileSize();
    }

    // Size the tiles so exactly _columns of them fit the current width (keeping the 112×150 aspect).
    private void RecomputeTileSize()
    {
        if (_vm is null) return;
        double pw = ThumbItems.Bounds.Width;
        if (pw <= 0) pw = ThumbScroll.Bounds.Width;
        if (pw <= 0) return;          // not laid out yet — OnThumbSizeChanged will retry
        double tw = pw / _columns - TileMargin - 0.5;   // 0.5 guards against sub-pixel premature wrap
        if (tw < 1) tw = 1;
        _vm.TileWidth = tw;
        _vm.TileHeight = tw * TileAspect;
    }

    private void OnThumbSizeChanged(object? sender, SizeChangedEventArgs e) => RecomputeTileSize();

    private void OnThumbPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _thumbContacts.Add(e.Pointer);
        if (_thumbContacts.Count >= 2) _multiTouchSeen = true;   // pinch detected → latch until all fingers lift
    }

    private void OnThumbPointerReleased(object? sender, PointerReleasedEventArgs e) => ThumbContactEnded(e.Pointer);
    private void OnThumbPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ThumbContactEnded(e.Pointer);

    // Finger moved far enough (a scroll or swipe, not a hold) → cancel the pending long-press.
    private void OnThumbPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_holdTarget is null) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _holdStart.X) > HoldMoveCancel || Math.Abs(p.Y - _holdStart.Y) > HoldMoveCancel)
            CancelHold();
    }

    // A finger ended — via normal release OR capture-lost (pinch-captured fingers fire the latter).
    // Removal is idempotent, so getting both is harmless.
    private void ThumbContactEnded(IPointer p)
    {
        _thumbContacts.Remove(p);
        if (_thumbContacts.Count == 0) _multiTouchSeen = false;   // every finger up → long-press allowed again
    }

    // Pinch in the fullscreen view → zoom toward the pinch focus (clamped 1×–5×). Pinch on the
    // thumbnail grid → change the column count (bigger thumbs = fewer columns).
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_vm is null) return;

        if (_vm.Fullscreen is null)
        {
            // Thumbnail grid: map the cumulative scale onto a 1–4 column count (out → fewer columns).
            if (!_thumbPinchActive) { _thumbPinchActive = true; _committedColumns = _columns; }
            int target = Math.Clamp((int)Math.Round(_committedColumns / e.Scale), 1, 4);
            if (target != _columns) { _columns = target; RecomputeTileSize(); }
            e.Handled = true;
            return;
        }

        double sNew = Math.Clamp(_committedScale * e.Scale, 1.0, 5.0);
        // ScaleOrigin is already in pixels relative to the overlay (= FsImage's space), fixed at
        // pinch start. (It is NOT 0..1 — the docs' "* Bounds" example is for the old Origin property.)
        var f = e.ScaleOrigin;
        double factor = _scale > 0 ? sNew / _scale : 1.0;
        _tx = f.X - factor * (f.X - _tx);                   // keep f over the same image point
        _ty = f.Y - factor * (f.Y - _ty);
        _scale = sNew;
        Apply();
        _vm.IsZoomed = _scale > 1.001;
        e.Handled = true;
    }

    private void Apply()
    {
        // Keep the scaled image covering the viewport — no drifting into empty space.
        double w = FsImage.Bounds.Width, h = FsImage.Bounds.Height;
        _tx = Math.Clamp(_tx, (1 - _scale) * w, 0);
        _ty = Math.Clamp(_ty, (1 - _scale) * h, 0);
        _fsScale.ScaleX = _fsScale.ScaleY = _scale;
        _fsTranslate.X = _tx;
        _fsTranslate.Y = _ty;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        // A pinch is over → clear all grid touch state. Captured pinch fingers don't reliably deliver
        // their release/capture-lost (Avalonia #12351), so the contact set / latch can't be trusted to
        // self-clear; reset them here so long-press works again. A still-down finger can't re-trigger a
        // long-press without a fresh PointerPressed, so this is safe.
        _thumbPinchActive = false;
        _thumbContacts.Clear();
        _multiTouchSeen = false;
        CancelHold();
        if (_vm?.Fullscreen is null) { e.Handled = true; return; }

        _committedScale = _scale;
        if (_scale <= 1.001) ResetZoom();   // settled back to 1× → fully reset
        // After any pinch the recognizer may swallow a PointerReleased (Avalonia #12351);
        // force a clean slate so a fresh finger-down is needed before panning again.
        _activePointers = 0;
        _panning = false;
        _swipeCandidate = false;
        _swiping = false;
        _swipeFired = false;
        e.Handled = true;
    }

    // Back at zoom > 1 resets to fit; back again exits (handled by the VM's TryBack hierarchy).
    private void ResetZoom()
    {
        _scale = 1;
        _committedScale = 1;
        _tx = _ty = 0;
        _fsScale.ScaleX = _fsScale.ScaleY = 1;
        _fsTranslate.X = _fsTranslate.Y = 0;
        if (_vm is not null) _vm.IsZoomed = false;
    }

    // Press a tile → start the long-press timer (unless a pinch / multi-touch is underway).
    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _holdFired = false;
        if (_thumbContacts.Count >= 2 || _multiTouchSeen || _thumbPinchActive) { CancelHold(); return; }
        if (sender is not Control c || c.DataContext is not GalleryPhoto gp) { CancelHold(); return; }

        _holdTarget = gp;
        _holdStart = e.GetPosition(this);
        _holdTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _holdTimer.Tick -= OnHoldTick;
        _holdTimer.Tick += OnHoldTick;
        _holdTimer.Stop();
        _holdTimer.Start();
    }

    private void OnTilePointerReleased(object? sender, PointerReleasedEventArgs e) => CancelHold();

    private void CancelHold()
    {
        _holdTimer?.Stop();
        _holdTarget = null;
    }

    // The press was held long enough → enter selection (or toggle, if already selecting).
    private void OnHoldTick(object? sender, EventArgs e)
    {
        _holdTimer?.Stop();
        var gp = _holdTarget;
        _holdTarget = null;
        if (gp is null) return;
        if (_thumbContacts.Count >= 2 || _multiTouchSeen || _thumbPinchActive) return;   // became a pinch
        if (DataContext is not GalleryViewModel vm) return;

        _holdFired = true;   // suppress the Tapped that the release will otherwise raise
        if (vm.SelectionMode) vm.Toggle(gp);
        else vm.EnterSelection(gp);
    }

    // Tap a tile's circle → toggle its checkmark. Handled so it doesn't bubble to a tile-body tap.
    private void OnCircleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control c || c.DataContext is not GalleryPhoto gp) return;
        if (DataContext is not GalleryViewModel vm) return;
        vm.Toggle(gp);
    }

    // Tap a tile body → open it fullscreen (works in both normal and selection mode).
    private void OnTileTapped(object? sender, TappedEventArgs e)
    {
        if (_holdFired) { _holdFired = false; return; }   // long-press already handled this gesture
        if (sender is not Control c || c.DataContext is not GalleryPhoto gp) return;
        if (DataContext is not GalleryViewModel vm) return;
        vm.OpenFullscreen(gp);
    }

    private void OnFsCheckTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;   // don't let it bubble to the overlay's dismiss-tap
        if (DataContext is GalleryViewModel vm) vm.ToggleFullscreenCheck();
    }

    private void OnFullscreenTapped(object? sender, TappedEventArgs e)
    {
        if (_scale > 1.001) return;   // zoomed → tap shouldn't dismiss; back resets first
        if (DataContext is GalleryViewModel vm) vm.CloseFullscreen();
    }

    // First finger down → start a gesture. While zoomed it's a pan; at fit it's a candidate paging
    // swipe. A second finger turns both off so the PinchGestureRecognizer owns the gesture.
    // (No pointer capture — that's what conflicts with pinch.)
    private void OnFsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _activePointers++;
        if (_activePointers == 1)
        {
            var p = e.GetPosition(FsOverlay);
            _downPoint = p;
            _panLast = p;
            bool zoomed = _scale > 1.001;
            _panning = zoomed;
            _swipeCandidate = !zoomed;
            _swiping = false;
            _swipeFired = false;
        }
        else
        {
            _panning = false;
            _swipeCandidate = false;
        }
    }

    private void OnFsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activePointers != 1) return;
        var p = e.GetPosition(FsOverlay);

        if (_panning && _scale > 1.001)
        {
            _tx += p.X - _panLast.X;        // image follows the finger
            _ty += p.Y - _panLast.Y;
            _panLast = p;
            Apply();                        // clamps so the image stays covering the viewport
            e.Handled = true;
            return;
        }

        // At fit: a horizontal drag pages. Wait ~10px before claiming so a tap still dismisses, and
        // ignore vertical drags. Once horizontal is confirmed, own the gesture (capture + block other
        // recognizers) so it can't be stolen — same fix as the Helper rows.
        if (!_swipeCandidate || _scale > 1.001) return;

        double dx = p.X - _downPoint.X, dy = p.Y - _downPoint.Y;
        double adx = Math.Abs(dx), ady = Math.Abs(dy);

        if (!_swiping)
        {
            if (adx < 10 && ady < 10) return;       // not clearly a drag yet → leave taps alone
            if (adx <= ady) { _swipeCandidate = false; return; }   // vertical → not a paging swipe
            _swiping = true;                        // horizontal confirmed → take ownership
            e.Pointer.Capture(FsOverlay);
        }
        e.PreventGestureRecognition();
        e.Handled = true;

        if (_swipeFired || _vm is null) return;

        // Page once per finger-down when the horizontal travel passes the threshold.
        if (adx > SwipeThreshold && adx > ady * 1.2)
        {
            if (dx < 0) _vm.ShowNext();   // swipe left  → next  (no-op at the last)
            else        _vm.ShowPrev();   // swipe right → previous (no-op at the first)
            _swipeFired = true;
        }
    }

    private void OnFsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activePointers > 0) _activePointers--;
        if (_activePointers == 0)
        {
            _panning = false;
            _swipeCandidate = false;
            _swiping = false;
            _swipeFired = false;
        }
    }

    private void OnFsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _activePointers = 0;
        _panning = false;
        _swipeCandidate = false;
        _swiping = false;
        _swipeFired = false;
    }

    private void OnDeleteSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GalleryViewModel vm || vm.SelectedCount == 0) return;
        var activity = MainActivity.Current;
        if (activity is null) return;

        int n = vm.SelectedCount;
        var b = new global::Android.App.AlertDialog.Builder(activity);
        b.SetTitle("Delete photos?");
        b.SetMessage($"Delete {n} selected photo{(n == 1 ? "" : "s")}?");
        b.SetPositiveButton("Yes", (EventHandler<global::Android.Content.DialogClickEventArgs>)((_, _) => vm.DeleteSelected()));
        b.SetNegativeButton("No", (EventHandler<global::Android.Content.DialogClickEventArgs>)((_, _) => { }));
        b.Show();
    }
}