using System.IO;
using System.Text.Json;

namespace BooksDb.Android;

public sealed class AppSettings
{
    public string BaseUrl { get; set; } = "";
    public string Token { get; set; } = "";
    
    public bool BeepOnScan { get; set; } = true;

    // Id of the chosen capture proportions (see CaptureAspect.Options). "full" = no crop.
    public string CaptureAspectRatio { get; set; } = "full";

    // Capture metering mode. true = fast still (minimize-latency, skips the AF/AE wait);
    // false = full AF (maximize-quality, the sharp default). Toggled from the Helper toolbar (X/AF).
    public bool CaptureFastStill { get; set; } = false;

    // Min ms between barcode decode attempts (≈ 1000/ms fps). 200 ≈ 5 fps. Lower = snappier scan,
    // more CPU/heat; higher = less drain. See BarcodeAnalyzer.
    public int ScanIntervalMs { get; set; } = 200;
}

public static class SettingsStore
{
    private static string FilePath =>
        Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* missing/corrupt → defaults */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(settings)); }
        catch { /* ignore */ }
    }
}