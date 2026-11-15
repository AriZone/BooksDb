using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.Graphics;

namespace BooksDb.Android;

/// <summary>One selectable capture proportion. Ratio is width:height (portrait); null = full frame.
/// Short is the compact label for the cycle button.</summary>
public sealed record AspectOption(string Id, string Name, string Short, double? Ratio);

public static class CaptureAspect
{
    // Curated set — extend freely later. Ratios are width / height (portrait, &lt; 1).
    public static readonly IReadOnlyList<AspectOption> Options = new[]
    {
        new AspectOption("full",  "Full frame (no crop)",          "Full",  null),
        new AspectOption("a5",    "A5 / B6 (\u22481:\u221a2)",        "A4",    148.0 / 210.0),
        new AspectOption("2:3",   "2:3 \u2014 paperback / hardcover", "2:3",   2.0 / 3.0),
        new AspectOption("comic", "BD album (\u22480.65)",         "BD",    6.625 / 10.25),
        new AspectOption("5:8",   "5:8 \u2014 mass-market paperback", "5:8",   5.0 / 8.0),
    };

    public static AspectOption FromId(string? id)
        => Options.FirstOrDefault(o => o.Id == id) ?? Options[0];

    public static double? RatioFor(string? id) => FromId(id).Ratio;

    /// <summary>Next option in the list, looping back to the first — drives the cycle button.</summary>
    public static AspectOption Next(string? id)
    {
        var currentId = FromId(id).Id;
        int i = 0;
        for (int k = 0; k < Options.Count; k++)
            if (Options[k].Id == currentId) { i = k; break; }
        return Options[(i + 1) % Options.Count];
    }

    /// <summary>
    /// Centre-crop a JPEG to the given width:height ratio (null/non-positive =&gt; returned unchanged).
    /// The input is expected to be upright already — CameraPreview bakes the capture rotation into the
    /// pixels before this runs — so this only crops, never rotates. Any failure falls back to the
    /// original bytes: a bad crop must never lose the photo.
    /// </summary>
    public static byte[] Crop(byte[] jpeg, double? ratio)
    {
        if (ratio is not { } target || target <= 0) return jpeg;

        Bitmap? src = null, cropped = null;
        try
        {
            src = BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length);
            if (src is null) return jpeg;

            int w = src.Width, h = src.Height;
            double current = (double)w / h;

            int cw, ch;
            if (current > target) { ch = h; cw = (int)Math.Round(h * target); }   // too wide  -> trim sides
            else                  { cw = w; ch = (int)Math.Round(w / target); }   // too tall  -> trim top/bottom
            cw = Math.Max(1, Math.Min(cw, w));
            ch = Math.Max(1, Math.Min(ch, h));
            int x = (w - cw) / 2, y = (h - ch) / 2;

            cropped = Bitmap.CreateBitmap(src, x, y, cw, ch);

            using var ms = new MemoryStream();
            cropped.Compress(Bitmap.CompressFormat.Jpeg!, 90, ms);
            return ms.ToArray();
        }
        catch
        {
            return jpeg;
        }
        finally
        {
            if (cropped is not null && !ReferenceEquals(cropped, src)) cropped.Recycle();
            src?.Recycle();
        }
    }
}