using System;
using Android.Gms.Tasks;
using Android.Runtime;
using AndroidX.Camera.Core;
using Xamarin.Google.MLKit.Vision.BarCode;        // BarcodeScanning, BarcodeScannerOptions, IBarcodeScanner
using Xamarin.Google.MLKit.Vision.Barcode.Common; // Barcode (+ format constants)
using Xamarin.Google.MLKit.Vision.Common;         // InputImage

namespace BooksDb.Android;

// Runs ML Kit barcode detection on each CameraX frame; emits ISBN-shaped EAN-13 values.
public class BarcodeAnalyzer : Java.Lang.Object, ImageAnalysis.IAnalyzer
{
    private readonly IBarcodeScanner _scanner;
    private readonly Action<string> _onBarcode;
    private string? _last;

    // Min ms between decode *starts* (≈ 1000/ms fps). CameraX delivers frames back-to-back
    // (KEEP_ONLY_LATEST + close-on-complete), so without a gate the ML Kit decode thread runs at
    // ~100% duty. Default 5 fps; tunable in Settings. volatile so a live change applies next frame.
    private volatile int _minIntervalMs;
    private long _lastRunMs;
    
    // CameraX's Analyzer interface has Java "default" methods; the binding exposes them as default
    // interface members that compile but emit NO Java override -> AbstractMethodError at runtime.
    // Implement them explicitly so real overrides are generated.
    public global::Android.Util.Size? DefaultTargetResolution => null;
    public int TargetCoordinateSystem => 0; // COORDINATE_SYSTEM_ORIGINAL
    public void UpdateTransform(global::Android.Graphics.Matrix? matrix) { }

    public BarcodeAnalyzer(Action<string> onBarcode, int intervalMs = 200)
    {
        _onBarcode = onBarcode;
        _minIntervalMs = intervalMs < 0 ? 0 : intervalMs;
        var options = new BarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatEan13)   // book barcodes are EAN-13 (978/979)
            .Build();
        _scanner = BarcodeScanning.GetClient(options);
    }

    /// <summary>Min ms between decodes. Live-tunable from Settings; applies on the next frame.</summary>
    public int MinIntervalMs
    {
        get => _minIntervalMs;
        set => _minIntervalMs = value < 0 ? 0 : value;
    }

    public void Analyze(IImageProxy? imageProxy)
    {
        if (imageProxy is null) return;

        // Throttle: skip frames that arrive sooner than MinIntervalMs after the last decode *start*.
        // Still Close() immediately on the skip path so CameraX hands over the next latest frame —
        // skipping stays cheap (a compare + close), and only ~5 decodes/sec actually run.
        long now = global::Android.OS.SystemClock.ElapsedRealtime();
        if (now - _lastRunMs < _minIntervalMs) { imageProxy.Close(); return; }
        _lastRunMs = now;

        var mediaImage = imageProxy.Image;
        if (mediaImage is null) { imageProxy.Close(); return; }

        var input = InputImage.FromMediaImage(mediaImage, imageProxy.ImageInfo?.RotationDegrees ?? 0);

        _scanner.Process(input)
            .AddOnSuccessListener(new SuccessListener(this))
            .AddOnCompleteListener(new CompleteListener(imageProxy)); // MUST close, or the preview stalls
    }

    private void HandleResult(Java.Lang.Object? result)
    {
        var list = result?.JavaCast<Java.Util.IList>();

        string? found = null;
        if (list is not null)
        {
            for (int i = 0; i < list.Size(); i++)
            {
                var raw = list.Get(i)?.JavaCast<Barcode>()?.RawValue;
                if (!string.IsNullOrEmpty(raw) && raw.Length == 13
                                               && (raw.StartsWith("978") || raw.StartsWith("979")))
                {
                    found = raw;
                    break;
                }
            }
        }

        if (found is null)
        {
            _last = null;          // nothing in view → re-arm so the same code can scan again
            return;
        }

        if (found != _last)        // still debounced while held steady in frame
        {
            _last = found;
            _onBarcode(found);
        }
    }

    private sealed class SuccessListener : Java.Lang.Object, IOnSuccessListener
    {
        private readonly BarcodeAnalyzer _owner;
        public SuccessListener(BarcodeAnalyzer owner) => _owner = owner;
        public void OnSuccess(Java.Lang.Object? result) => _owner.HandleResult(result);
    }

    private sealed class CompleteListener : Java.Lang.Object, IOnCompleteListener
    {
        private readonly IImageProxy _proxy;
        public CompleteListener(IImageProxy proxy) => _proxy = proxy;
        public void OnComplete(global::Android.Gms.Tasks.Task task) => _proxy.Close();
    }
}