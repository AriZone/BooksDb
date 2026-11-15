using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BooksDb.Android;

public partial class HelperView : UserControl
{
    public HelperView()
    {
        InitializeComponent();
        Camera.BarcodeDetected += OnBarcode;

        // (Re)apply saved settings whenever the view appears, so changed defaults take effect.
        Loaded += (_, _) =>
        {
            var s = SettingsStore.Load();
            var opt = CaptureAspect.FromId(s.CaptureAspectRatio);
            Camera.SetCropRatio(opt.Ratio);
            Camera.SetScanInterval(s.ScanIntervalMs);
            AspectButton.Content = opt.Short;

            Camera.SetFastStill(s.CaptureFastStill);
            FastStillButton.Content = s.CaptureFastStill ? "X" : "AF";
        };
    }

    // Left-swipe a collection row → open its review gallery. Right-swipe is reserved (per-collection
    // send, later). A horizontal drag is distinguished from tap (no movement) and long-press (Holding).
    private Avalonia.Point _swipeStart;
    private DateTime _swipeStartTime;
    private bool _swiping;
    private bool _decided;        // direction resolved for this drag
    private bool _horizontal;     // resolved as a horizontal swipe (we own the pointer)
    private CapturedItem? _swipeItem;

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _swipeStart = e.GetPosition(this);
        _swipeStartTime = DateTime.UtcNow;
        _swiping = true;
        _decided = false;
        _horizontal = false;
        _swipeItem = (sender as Control)?.DataContext as CapturedItem;
    }

    // Resolve direction early. Once a drag is clearly horizontal, call PreventGestureRecognition() so the
    // ListBox's ScrollGestureRecognizer stops panning (capture alone doesn't — the recognizer pans
    // independently), and capture the pointer to the row. Clearly-vertical drags are left to scroll.
    private void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_swiping || _decided) return;
        var p = e.GetPosition(this);
        double adx = Math.Abs(p.X - _swipeStart.X), ady = Math.Abs(p.Y - _swipeStart.Y);
        if (adx < 8 && ady < 8) return;                  // not enough movement to judge yet

        if (adx > ady * 1.3 && sender is Control c)      // clearly horizontal → claim the drag
        {
            _decided = true;
            _horizontal = true;
            e.PreventGestureRecognition();               // freeze the list; this drag is ours
            e.Pointer.Capture(c);
        }
        else if (ady > adx)                              // clearly vertical → leave it to the list
        {
            _decided = true;
        }
        // otherwise still ambiguous (diagonal) → keep waiting for a clearer direction
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_swiping) return;
        bool horizontal = _horizontal;
        var item = _swipeItem;
        var end = e.GetPosition(this);
        double dx = end.X - _swipeStart.X, dy = end.Y - _swipeStart.Y;
        double dt = (DateTime.UtcNow - _swipeStartTime).TotalMilliseconds;
        ResetSwipe();

        if (!horizontal || item is null) return;               // vertical/idle → not a swipe
        if (DataContext is not HelperViewModel vm) return;
        if (dt > 700) return;                                  // too slow to be a flick
        if (Math.Abs(dx) < 60) return;                         // not far enough
        if (Math.Abs(dx) < Math.Abs(dy) * 1.5) return;         // mostly vertical → ignore
        if (dx < 0) vm.RequestGallery(item);                   // left → gallery
        else        ConfirmSend(vm, item);                     // right → confirm, then send
    }

    private void OnRowCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetSwipe();

    // Right-swipe → confirm, then send just this collection to the desktop (it's removed on success).
    private void ConfirmSend(HelperViewModel vm, CapturedItem item)
    {
        var activity = MainActivity.Current;
        if (activity is null) { _ = vm.SendCollectionAsync(item); return; }   // no UI host → just send

        var what = string.IsNullOrEmpty(item.Isbn) ? "the unassigned photos" : $"ISBN {item.Isbn}";
        var n = item.Photos.Count;
        var b = new global::Android.App.AlertDialog.Builder(activity);
        b.SetTitle("Send to database?");
        b.SetMessage($"Send {what} ({n} photo{(n == 1 ? "" : "s")}) to the desktop?");
        b.SetPositiveButton("Yes", (EventHandler<global::Android.Content.DialogClickEventArgs>)(async (_, _) => await vm.SendCollectionAsync(item)));
        b.SetNegativeButton("No", (EventHandler<global::Android.Content.DialogClickEventArgs>)((_, _) => { }));
        b.Show();
    }

    private void ResetSwipe()
    {
        _swiping = false;
        _decided = false;
        _horizontal = false;
        _swipeItem = null;
    }

    private void OnBarcode(string isbn)
    {
        if (DataContext is HelperViewModel vm)
            vm.Isbn = isbn;   // already on the UI thread (posted by CameraPreview)
    }

    // Long-press a collection row → native confirm → remove just that collection.
    private void OnCollectionHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started) return;
        if (sender is not Control c || c.DataContext is not CapturedItem item) return;
        if (DataContext is not HelperViewModel vm) return;

        var activity = MainActivity.Current;
        if (activity is null) return;

        var what = string.IsNullOrEmpty(item.Isbn) ? "the unassigned photos" : $"ISBN {item.Isbn}";
        var n = item.Photos.Count;
        var b = new global::Android.App.AlertDialog.Builder(activity);
        b.SetTitle("Delete this collection?");
        b.SetMessage($"Remove {what} ({n} photo{(n == 1 ? "" : "s")}) from the buffer.");
        b.SetPositiveButton("Yes", (EventHandler<global::Android.Content.DialogClickEventArgs>)((_, _) => vm.RemoveCollection(item)));
        b.SetNegativeButton("No", (EventHandler<global::Android.Content.DialogClickEventArgs>)((_, _) => { }));
        b.Show();
    }

    private void OnCycleAspect(object? sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Load();
        var next = CaptureAspect.Next(s.CaptureAspectRatio);

        s.CaptureAspectRatio = next.Id;
        SettingsStore.Save(s);             // persist so captures and the saved default both follow it

        Camera.SetCropRatio(next.Ratio);   // live overlay
        AspectButton.Content = next.Short;
    }

    // X = fast still (minimize-latency, skips the AF/AE wait); AF = full metering (sharp). Same
    // tap-to-flip pattern as the proportions button: persist, apply live, relabel.
    private void OnToggleFastStill(object? sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Load();
        s.CaptureFastStill = !s.CaptureFastStill;
        SettingsStore.Save(s);

        Camera.SetFastStill(s.CaptureFastStill);
        FastStillButton.Content = s.CaptureFastStill ? "X" : "AF";
    }

    private async void OnCapture(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HelperViewModel vm) return;
        var ratio = CaptureAspect.RatioFor(SettingsStore.Load().CaptureAspectRatio);
        var bytes = await Camera.CapturePhotoAsync(ratio);
        if (bytes is null) { vm.Status = "Capture failed."; return; }
        vm.AttachPhoto(bytes);
    }

    private async void OnCaptureLoose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HelperViewModel vm) return;
        var ratio = CaptureAspect.RatioFor(SettingsStore.Load().CaptureAspectRatio);
        var bytes = await Camera.CapturePhotoAsync(ratio);
        if (bytes is null) { vm.Status = "Capture failed."; return; }
        vm.AttachLoosePhoto(bytes);
    }
}