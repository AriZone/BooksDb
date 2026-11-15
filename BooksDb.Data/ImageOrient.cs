//using System.IO;
using SkiaSharp;

namespace BooksDb.Data;

/// <summary>
/// EXIF-orientation handling. Avalonia/Skia ignores the EXIF orientation tag when rendering, so
/// we bake the rotation into the pixels — both when storing a picture (SaveOriented) and when
/// decoding one for display (OrientedStream). Already-upright images are passed through untouched.
/// </summary>
public static class ImageOrient
{
    /// <summary>
    /// Store <paramref name="sourceFile"/> as <paramref name="destFile"/>: bake in EXIF orientation,
    /// downscale so the long edge is at most <paramref name="maxEdge"/> px, and re-encode as JPEG at
    /// <paramref name="quality"/>. This is the single ingest sink for catalog pictures, so every photo
    /// (book capture, pocket assignment, manual add) is normalized the same way.
    /// </summary>
    public static void SaveOriented(string sourceFile, string destFile, int maxEdge = 2400, int quality = 85)
    {
        using var codec = SKCodec.Create(sourceFile);
        if (codec is null)
        {
            File.Copy(sourceFile, destFile);   // undecodable — store as-is rather than lose it
            return;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            File.Copy(sourceFile, destFile);
            return;
        }

        var origin = codec.EncodedOrigin;

        // Bake EXIF orientation into the pixels (Reorient returns a new bitmap; upright stays `decoded`).
        SKBitmap upright = origin == SKEncodedOrigin.TopLeft ? decoded : Reorient(decoded, origin);
        bool uprightIsNew = !ReferenceEquals(upright, decoded);

        SKBitmap? scaled = null;
        try
        {
            SKBitmap toEncode = upright;

            int longEdge = Math.Max(upright.Width, upright.Height);
            if (longEdge > maxEdge)
            {
                float f = maxEdge / (float)longEdge;
                int w = Math.Max(1, (int)Math.Round(upright.Width  * f));
                int h = Math.Max(1, (int)Math.Round(upright.Height * f));
                scaled = upright.Resize(new SKSizeI(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (scaled is not null) toEncode = scaled;
            }

            using var image = SKImage.FromBitmap(toEncode);
            using var data  = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            using var fs    = File.Create(destFile);
            data.SaveTo(fs);
        }
        finally
        {
            scaled?.Dispose();
            if (uprightIsNew) upright.Dispose();
        }
    }

    /// <summary>Open <paramref name="path"/> as an upright stream. Upright/undecodable images stream
    /// their original bytes; rotated ones are reoriented into an in-memory JPEG. Caller owns the
    /// returned stream.</summary>
    public static Stream OrientedStream(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null || codec.EncodedOrigin == SKEncodedOrigin.TopLeft)
            return File.OpenRead(path);

        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
            return File.OpenRead(path);

        using var upright = Reorient(bitmap, codec.EncodedOrigin);
        using var image   = SKImage.FromBitmap(upright);
        using var data    = image.Encode(SKEncodedImageFormat.Jpeg, 92);

        var ms = new MemoryStream((int)data.Size);
        data.SaveTo(ms);
        ms.Position = 0;
        return ms;
    }
    
    /// <summary>
    /// Decode a small, upright thumbnail cheaply: the JPEG is downsampled *during* decode (native
    /// DCT scaling, 1/2…1/8) so full resolution is never touched, then EXIF orientation is baked into
    /// the already-small bitmap. Returns a PNG stream. Falls back to <see cref="OrientedStream"/> on any
    /// failure, so correctness never depends on the fast path. Caller owns the stream.
    /// </summary>
    public static Stream ThumbStream(string path, int targetWidth)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null) return OrientedStream(path);

            var origin = codec.EncodedOrigin;
            var info   = codec.Info;

            // 90°/270° origins swap width<->height when displayed.
            bool swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                                or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            int displayWidthSrc = swaps ? info.Height : info.Width;
            if (displayWidthSrc <= 0) return OrientedStream(path);

            // Largest power-of-two downscale that keeps displayed width >= target.
            int sample = 1;
            while (displayWidthSrc / (sample * 2) >= targetWidth && sample < 8) sample *= 2;

            var scaled     = codec.GetScaledDimensions(1f / sample);
            var decodeInfo = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

            var small  = new SKBitmap(decodeInfo);
            var result = codec.GetPixels(decodeInfo, small.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                small.Dispose();
                return OrientedStream(path);
            }

            using (small)
            {
                using var upright = origin == SKEncodedOrigin.TopLeft ? null : Reorient(small, origin);
                using var image   = SKImage.FromBitmap(upright ?? small);
                using var data    = image.Encode(SKEncodedImageFormat.Png, 100);

                var ms = new MemoryStream((int)data.Size);
                data.SaveTo(ms);
                ms.Position = 0;
                return ms;
            }
        }
        catch
        {
            return OrientedStream(path);   // proven full-res path as a safety net
        }
    }

    private static SKBitmap Reorient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        SKBitmap result;
        switch (origin)
        {
            case SKEncodedOrigin.BottomRight:                 // 180°
                result = new SKBitmap(bitmap.Width, bitmap.Height);
                using (var c = new SKCanvas(result))
                {
                    c.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f);
                    c.DrawBitmap(bitmap, 0, 0);
                }
                return result;

            case SKEncodedOrigin.RightTop:                    // 90° CW — EXIF 6, phone portrait
                result = new SKBitmap(bitmap.Height, bitmap.Width);
                using (var c = new SKCanvas(result))
                {
                    c.Translate(result.Width, 0);
                    c.RotateDegrees(90);
                    c.DrawBitmap(bitmap, 0, 0);
                }
                return result;

            case SKEncodedOrigin.LeftBottom:                  // 270° CW
                result = new SKBitmap(bitmap.Height, bitmap.Width);
                using (var c = new SKCanvas(result))
                {
                    c.Translate(0, result.Height);
                    c.RotateDegrees(270);
                    c.DrawBitmap(bitmap, 0, 0);
                }
                return result;

            default:
                // Mirrored/transposed origins (2,4,5,7) are rare for camera photos — leave as-is.
                return bitmap.Copy();
        }
    }
    
    /// <summary>Crop <paramref name="sourcePath"/> to the given pixel rect and write JPEG to
    /// <paramref name="destPath"/> (may be the same file). Out-of-range rects are clamped.</summary>
    public static void CropAndSave(string sourcePath, string destPath, int x, int y, int w, int h, int quality = 85)
    {
        SKBitmap? src;
        using (var input = File.OpenRead(sourcePath))
            src = SKBitmap.Decode(input);
        if (src is null) return;

        byte[] bytes;
        using (src)
        {
            var rect = SKRectI.Intersect(new SKRectI(x, y, x + w, y + h), new SKRectI(0, 0, src.Width, src.Height));
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using var cropped = new SKBitmap();
            if (!src.ExtractSubset(cropped, rect)) return;

            using var image = SKImage.FromBitmap(cropped);
            using var data  = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            bytes = data.ToArray();
        }
        File.WriteAllBytes(destPath, bytes);   // file is closed above, so in-place is safe
    }
    
    /// <summary>Rotate <paramref name="path"/> 90° in place (clockwise or counter-clockwise) and
    /// re-encode as JPEG. Decodes through <see cref="OrientedStream"/>, so the rotation is applied to
    /// the upright image the viewer shows, regardless of any EXIF tag.</summary>
    public static void RotateAndSave(string path, bool clockwise, int quality = 90)
    {
        SKBitmap? src;
        using (var s = OrientedStream(path))
            src = SKBitmap.Decode(s);
        if (src is null) return;

        byte[] bytes;
        using (src)
        {
            using var result = new SKBitmap(src.Height, src.Width);   // 90° swaps width/height
            using (var c = new SKCanvas(result))
            {
                if (clockwise) { c.Translate(result.Width, 0);  c.RotateDegrees(90);  }
                else           { c.Translate(0, result.Height); c.RotateDegrees(270); }
                c.DrawBitmap(src, 0, 0);
            }
            using var image = SKImage.FromBitmap(result);
            using var data  = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            bytes = data.ToArray();
        }
        File.WriteAllBytes(path, bytes);   // read streams are closed above, so in-place is safe
    }
    
}