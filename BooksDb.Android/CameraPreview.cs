using System;
using Android.Content;
using Android.Content.PM;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Java.Util.Concurrent;
using System.Threading.Tasks;
using AndroidX.Camera.Core;
using Android.Views;
using Android.Widget;
using Android.Graphics;

namespace BooksDb.Android;

public class CameraPreview : NativeControlHost
{
    private LifecycleCameraController? _controller;
    private BarcodeAnalyzer? _analyzer;
    private PreviewView? _previewView;
    private ILifecycleOwner? _owner;
    private Context? _context;
    private IExecutorService? _analysisExecutor;
    private bool _bound;
    private bool _dozing;
    // Manual doze survives view recreation (the gallery detaches HelperView, destroying this control;
    // returning builds a fresh one). Static so the new instance restores the user's black-screen state.
    private static bool s_persistDozed;
    private AspectOverlayView? _overlay;
    private FrameLayout? _frame;
    private global::Android.Views.View? _dozeCover;
    private global::Android.Views.View? _touch;
    private global::Android.Media.ToneGenerator? _tone; 
    // Capture metering mode, set live from the Helper toolbar (X/AF toggle).
    private bool _fastStill;   // true = minimize-latency (skips the AF/AE wait); false = maximize-quality
    public void SetFastStill(bool fast) => _fastStill = fast;

    public event Action<string>? BarcodeDetected;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _context = ((AndroidViewControlHandle)parent).View.Context!;

        var previewView = new PreviewView(_context);
        previewView.SetImplementationMode(PreviewView.ImplementationMode.Compatible);
        _previewView = previewView;

        _controller = new LifecycleCameraController(_context);
        _owner = FindLifecycleOwner(_context);

        if (SettingsStore.Load().BeepOnScan)
        {
            try { _tone = new global::Android.Media.ToneGenerator(global::Android.Media.Stream.Music, 100); }
            catch { _tone = null; }   // beep is optional — never let it break scanning
        }
        // beep off → _tone stays null → _tone?.StartTone(...) is a no-op

        _analysisExecutor = Executors.NewSingleThreadExecutor();
        _analyzer = new BarcodeAnalyzer(raw =>
        {
            try { _tone?.StartTone(global::Android.Media.Tone.PropBeep2, 240); } catch { }
            Dispatcher.UIThread.Post(() => BarcodeDetected?.Invoke(raw));
        }, SettingsStore.Load().ScanIntervalMs);
        _controller.SetImageAnalysisAnalyzer(_analysisExecutor!, _analyzer);

        previewView.Controller = _controller;

        // Composite a native framing overlay on top of the preview. (An Avalonia overlay over a
        // NativeControlHost loses the z-order fight on Android, so the guide must be a native view.)
        var frame = new FrameLayout(_context);
        frame.AddView(previewView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _overlay = new AspectOverlayView(_context)
        {
            Ratio = CaptureAspect.RatioFor(SettingsStore.Load().CaptureAspectRatio),
        };
        frame.AddView(_overlay, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // Tap-to-doze "screen saver": a black cover (topmost, normally hidden) plus a tap handler on
        // the frame. Dozing unbinds the camera — sensor, preview and the per-frame analyzer all stop,
        // which is the bulk of the idle heat/drain; tapping again rebinds. On AMOLED the black cover
        // also drops display draw to near nothing. (Children above are non-interactive, so the tap
        // falls through to the clickable frame in both states.)
        _dozeCover = new global::Android.Views.View(_context) { Visibility = ViewStates.Gone };
        _dozeCover.SetBackgroundColor(Color.Black);
        frame.AddView(_dozeCover, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // A click handler on the frame itself doesn't fire — the PreviewView (a TextureView in
        // Compatible mode) swallows the DOWN before it reaches the parent. A dedicated transparent
        // catcher added LAST (top-most, clickable) reliably receives the tap in both states. Nothing
        // inside the preview needs touch; Capture/Photo/Add live outside this native control.
        _touch = new global::Android.Views.View(_context) { Clickable = true };
        _touch.Click += (_, _) => ToggleDoze();
        frame.AddView(_touch, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // Restore the prior doze state. Entering the gallery tears this control down entirely (camera,
        // analyzer and preview all released — the strongest battery saving); on return we rebuild here.
        // If the user had manually dozed before, come back dozed (black, no camera) rather than live.
        MainActivity.Resumed += TryBind;
        _dozing = s_persistDozed;
        if (_dozing)
        {
            if (_dozeCover is not null) _dozeCover.Visibility = ViewStates.Visible;   // stay black; don't bind
        }
        else
        {
            TryBind();
        }

        _frame = frame;
        return new AndroidViewControlHandle(frame);
    }
    
    /// <summary>
    /// Capture one photo. This is the single capture path: a full-sensor still via CameraX TakePicture,
    /// uncapped (full resolution), with the chosen proportion applied in the same decode/encode pass.
    /// The metering mode (fast vs full AF) follows the toolbar X/AF toggle. Kept as a thin alias over
    /// CaptureStillAsync so the OnCapture / OnCaptureLoose call seam in HelperView never has to change.
    /// </summary>
    public Task<byte[]?> CapturePhotoAsync(double? cropRatio = null)
        => CaptureStillAsync(cropRatio, int.MaxValue);

    /// <summary>
    /// Full-resolution still via CameraX TakePicture. Runs the AF/AE precapture sequence (metering mode
    /// set per the X/AF toggle), reads the sensor frame, downsamples to longEdgeCap, applies the chosen
    /// proportion in the same pass and returns an upright JPEG. longEdgeCap = int.MaxValue = full res.
    /// </summary>
    public Task<byte[]?> CaptureStillAsync(double? cropRatio = null, int longEdgeCap = 2400)
    {
        var tcs = new TaskCompletionSource<byte[]?>();

        if (_controller is null || !_bound || _context is null)
        {
            tcs.SetResult(null);
            return tcs.Task;
        }

        // Capture in-memory (not to a file): the ImageProxy hands back the sensor-native pixels with
        // the tilt-based rotation kept *out* of them — it lives only in ImageInfo.RotationDegrees,
        // which we ignore. The file path instead lets CameraController bake its tilt guess into the
        // pixels, which is what kept landing shots sideways no matter what we did afterwards.
        // The preview crops the camera frame to fill the on-screen box (FILL_CENTER); the in-memory
        // capture doesn't, so pass the box aspect and replicate that crop — otherwise the saved photo
        // is wider than what the overlay framed.
        double viewAspect = 0;
        if (_frame is { } f && f.Width > 0 && f.Height > 0)
            viewAspect = (double)f.Width / f.Height;

        var executor = ContextCompat.GetMainExecutor(_context);   // callback on UI thread
        // Fast still (X) = skip the 3A (AF/AE) wait; full AF = full metering. Applied per shot so the
        // toolbar toggle takes effect immediately, regardless of when the controller finished binding.
        _controller.ImageCaptureMode = _fastStill
            ? ImageCapture.CaptureModeMinimizeLatency
            : ImageCapture.CaptureModeMaximizeQuality;
        _controller.TakePicture(executor, new CapturedCallback(tcs, viewAspect, cropRatio, longEdgeCap));
        return tcs.Task;
    }

    private sealed class CapturedCallback : ImageCapture.OnImageCapturedCallback
    {
        private readonly TaskCompletionSource<byte[]?> _tcs;
        private readonly double _viewAspect;
        private readonly double? _cropRatio;
        private readonly int _cap;

        public CapturedCallback(TaskCompletionSource<byte[]?> tcs, double viewAspect, double? cropRatio, int cap)
        {
            _tcs = tcs;
            _viewAspect = viewAspect;
            _cropRatio = cropRatio;
            _cap = cap;
        }

        public override void OnCaptureSuccess(IImageProxy? image)
        {
            if (image is null) { _tcs.TrySetResult(null); return; }
            try
            {
                // JPEG-format capture: all bytes live in plane 0.
                var planes = image.GetPlanes();
                if (planes is null || planes.Length == 0) { _tcs.TrySetResult(null); return; }

                var buffer = planes[0].Buffer!;
                var bytes  = new byte[buffer.Remaining()];
                buffer.Get(bytes);

                // Decode downsampled — a full-res sensor frame can OOM the decode, which silently
                // returns null. A book cover needs nothing past ~2400px on the long edge (the desktop
                // caps there too).
                var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
                int longEdge = System.Math.Max(bounds.OutWidth, bounds.OutHeight);
                int sample = 1;
                while (longEdge > 0 && longEdge / sample > _cap) sample *= 2;

                var opts = new BitmapFactory.Options { InSampleSize = sample };
                using var src = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, opts);
                if (src is null) { _tcs.TrySetResult(bytes); return; }

                // Sensor-native pixels are landscape; books are always shot upright, so a fixed 90°
                // turns every capture portrait regardless of how the phone was tilted. If a device
                // captures books upside-down, change PostRotate(90) to PostRotate(-90).
                Bitmap upright;
                if (src.Width > src.Height)
                {
                    var m = new Matrix();
                    m.PostRotate(90);
                    upright = Bitmap.CreateBitmap(src, 0, 0, src.Width, src.Height, m, true)!;
                }
                else
                {
                    upright = src;   // already portrait
                }

                // Replicate the preview's FILL_CENTER crop: centre-crop the upright image to the
                // on-screen box aspect, so the saved photo shows exactly what the box — and the overlay
                // drawn on it — framed.
                Bitmap framed = FillCrop(upright, _viewAspect);

                // Apply the chosen capture proportion in the SAME pass (FillCrop is the same centre-crop
                // math). Doing it here saves a whole second decode+JPEG-encode of the ~2400px image that
                // a follow-up crop step would otherwise cost — the bulk of the post-shutter delay.
                Bitmap final = (_cropRatio is { } r && r > 0) ? FillCrop(framed, r) : framed;

                using var ms = new System.IO.MemoryStream();
                final.Compress(Bitmap.CompressFormat.Jpeg!, 92, ms);
                if (!ReferenceEquals(final, framed) && !ReferenceEquals(final, upright) && !ReferenceEquals(final, src)) final.Recycle();
                if (!ReferenceEquals(framed, upright) && !ReferenceEquals(framed, src)) framed.Recycle();
                if (!ReferenceEquals(upright, src)) upright.Recycle();
                _tcs.TrySetResult(ms.ToArray());
            }
            catch { _tcs.TrySetResult(null); }
            finally { try { image.Close(); } catch { } }
        }

        public override void OnError(ImageCaptureException? exception)
            => _tcs.TrySetResult(null);
    }

    /// <summary>Update the framing guide drawn over the live preview (null = full frame, no guide).</summary>
    public void SetCropRatio(double? ratio)
    {
        if (_overlay is null) return;
        _overlay.Ratio = ratio;
        _overlay.PostInvalidate();
    }

    /// <summary>Live-update the barcode scan interval (ms between decodes) from settings.</summary>
    public void SetScanInterval(int ms)
    {
        if (_analyzer is not null) _analyzer.MinIntervalMs = ms;
    }

    // Centre-crop to the given width:height aspect. Returns src unchanged when no crop is needed or the
    // aspect is unknown. Shared by the fast preview grab and the still-capture path.
    private static Bitmap FillCrop(Bitmap src, double aspect)
    {
        if (aspect <= 0) return src;

        double imgAspect = (double)src.Width / src.Height;
        int cw, ch;
        if (imgAspect > aspect) { ch = src.Height; cw = (int)System.Math.Round(src.Height * aspect); }
        else                    { cw = src.Width;  ch = (int)System.Math.Round(src.Width / aspect); }

        cw = System.Math.Max(1, System.Math.Min(cw, src.Width));
        ch = System.Math.Max(1, System.Math.Min(ch, src.Height));
        if (cw == src.Width && ch == src.Height) return src;

        int x = (src.Width - cw) / 2, y = (src.Height - ch) / 2;
        return Bitmap.CreateBitmap(src, x, y, cw, ch)!;
    }

    // Centred framing rectangle of the chosen ratio, with the area outside dimmed so the user can
    // see exactly what the auto-crop will keep. Drawn natively so it sits above the camera surface.
    private sealed class AspectOverlayView : global::Android.Views.View
    {
        private readonly Paint _stroke;
        private readonly Paint _scrim;

        public double? Ratio { get; set; }

        public AspectOverlayView(Context context) : base(context)
        {
            var density = Resources?.DisplayMetrics?.Density ?? 2f;
            _stroke = new Paint { Color = Color.White, AntiAlias = true, StrokeWidth = 2.5f * density };
            _stroke.SetStyle(Paint.Style.Stroke!);
            _scrim = new Paint { Color = Color.Argb(110, 0, 0, 0) };
        }

        protected override void OnDraw(global::Android.Graphics.Canvas canvas)
        {
            base.OnDraw(canvas);
            if (Ratio is not { } r || r <= 0) return;

            float vw = Width, vh = Height;
            if (vw <= 0 || vh <= 0) return;

            // largest centred rect of ratio r (width/height) that fits the view
            float rw = vw, rh = vw / (float)r;
            if (rh > vh) { rh = vh; rw = vh * (float)r; }
            float left = (vw - rw) / 2f, top = (vh - rh) / 2f, right = left + rw, bottom = top + rh;

            canvas.DrawRect(0, 0, vw, top, _scrim);        // dim outside the frame
            canvas.DrawRect(0, bottom, vw, vh, _scrim);
            canvas.DrawRect(0, top, left, bottom, _scrim);
            canvas.DrawRect(right, top, vw, bottom, _scrim);
            canvas.DrawRect(left, top, right, bottom, _stroke);   // frame outline
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        MainActivity.Resumed -= TryBind;
        _controller?.ClearImageAnalysisAnalyzer();
        _controller?.Unbind();
        _controller = null;
        _analyzer = null;
        _previewView = null;
        _analysisExecutor?.Shutdown();
        _analysisExecutor = null;
        _tone?.Release();
        _tone = null;
        _bound = false;
        _dozing = false;
        _overlay = null;
        _frame = null;
        _dozeCover = null;
        _touch = null;
        base.DestroyNativeControlCore(control);
    }

    // --- Tap-to-doze screen saver ---------------------------------------------------------------

    private void ToggleDoze()
    {
        if (_controller is null) return;
        if (_dozing) Wake();
        else Doze();
    }

    private void Doze()
    {
        _dozing = true;
        s_persistDozed = true;
        try { _controller?.Unbind(); } catch { }   // releases the camera: sensor + preview + analyzer
        _bound = false;
        if (_dozeCover is not null) _dozeCover.Visibility = ViewStates.Visible;
    }

    private void Wake()
    {
        _dozing = false;
        s_persistDozed = false;
        if (_dozeCover is not null) _dozeCover.Visibility = ViewStates.Gone;
        TryBind();   // reopens the camera; the analyzer config survives an unbind, so scanning resumes
    }

    private void TryBind()
    {
        if (_bound || _dozing || _controller is null || _owner is null || _context is null) return;
        if (ContextCompat.CheckSelfPermission(_context, global::Android.Manifest.Permission.Camera)
            != Permission.Granted) return;

        _controller.BindToLifecycle(_owner);
        _bound = true;
    }

    private static ILifecycleOwner? FindLifecycleOwner(Context? context)
    {
        while (context is not null)
        {
            if (context is ILifecycleOwner owner) return owner;
            context = (context as ContextWrapper)?.BaseContext;
        }
        return null;
    }
}