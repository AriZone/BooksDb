using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BooksDb.Data;
using BooksDb.Data.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BooksDb.Desktop;

/// <summary>
/// Embedded LAN listener (Kestrel) that receives book captures from the phone "Helper"
/// and stages them in PendingCaptures. Runs only while the desktop app is open.
/// Desktop-head only — deliberately kept out of the shared project so the Android head
/// never inherits an ASP.NET Core dependency it can't use.
/// </summary>
public static class CaptureServer
{
    private static WebApplication? _app;

    public static void Start(IServiceProvider services)
    {
        // Settings live in ~/.config/BooksDb/settings.json; SettingsStore loads them at
        // startup and exposes the snapshot via .Current.
        var settings = services.GetRequiredService<SettingsStore>().Current;
        var port  = settings.CapturePort;
        var token = settings.CaptureToken;

        if (port <= 0 || string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine(
                "[CaptureServer] disabled — set CapturePort and a non-empty CaptureToken in settings.json to enable.");
            return;
        }

        // Singletons from the app's DI container, captured for the request handlers.
        var captures = services.GetRequiredService<PendingCaptureRepository>();
        var store    = services.GetRequiredService<PictureStore>();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();                    // keep the desktop console clean
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");   // bind on the LAN

        var app = builder.Build();

        // bearer-token gate on every request
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.Authorization.ToString() != $"Bearer {token}")
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next(ctx);
        });

        // connectivity check for the phone
        app.MapGet("/ping", () => Results.Ok(new { ok = true, pending = captures.CountPending() }));

        // Stage one book capture: multipart/form-data with text fields (sessionId, isbn,
        // shelf, capturedAt) and zero or more file parts named "photos", primary-first.
        // We read the form manually (not via [FromForm] parameters) so the request never
        // trips ASP.NET's antiforgery filter — this is a token-authenticated machine endpoint.
        app.MapPost("/captures", async (HttpRequest req) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "expected multipart/form-data" });

            var form    = await req.ReadFormAsync();
            var rawIsbn = Blank(form["isbn"].ToString());

            var capture = new PendingCapture
            {
                SessionId  = Blank(form["sessionId"].ToString()),
                RawIsbn    = rawIsbn,
                Isbn13     = Isbn.To13(rawIsbn),                  // canonical key; null if absent/invalid
                Shelf      = Blank(form["shelf"].ToString()),
                CapturedAt = Blank(form["capturedAt"].ToString()),
                ReceivedAt = DateTimeOffset.Now.ToString("o"),
                Status     = "pending",
            };

            // Stage uploaded photos under <BaseDir>/captures_staging/<group>/NN.ext.
            // Relative paths are stored in PendingCapturePhotos (same convention as BookPictures)
            // and resolved through PictureStore on commit; the group dir is removed on commit/discard.
            var relPaths = new List<string>();
            if (form.Files.Count > 0)
            {
                var group  = Guid.NewGuid().ToString("N");
                var relDir = Path.Combine("captures_staging", group);
                Directory.CreateDirectory(store.AbsolutePath(relDir));

                var ordinal = 1;
                foreach (var file in form.Files)
                {
                    if (file.Length <= 0) continue;
                    var ext = NormalizeImageExt(file.FileName, file.ContentType);
                    var rel = Path.Combine(relDir, $"{ordinal:D2}{ext}");
                    await using (var fs = File.Create(store.AbsolutePath(rel)))
                        await file.CopyToAsync(fs);
                    relPaths.Add(rel);
                    ordinal++;
                }
            }

            var id = captures.Add(capture, relPaths);
            Console.WriteLine(
                $"[CaptureServer] staged #{id}  isbn13={capture.Isbn13 ?? "—"}  shelf={capture.Shelf ?? "—"}  photos={relPaths.Count}");
            return Results.Ok(new { captureId = id, isbn13 = capture.Isbn13, photos = relPaths.Count });
        });

        app.StartAsync().GetAwaiter().GetResult();   // binds the socket, returns immediately
        _app = app;
        Console.WriteLine($"[CaptureServer] listening on http://0.0.0.0:{port}");
    }

    public static void Stop()
    {
        var app = _app;
        _app = null;
        if (app is null) return;

        // Stop() runs on the main thread AFTER Avalonia's dispatcher loop has ended, but the
        // Avalonia SynchronizationContext is still current here. Blocking on StopAsync().GetResult()
        // deadlocks — the continuation is posted back to that dead UI context and never runs.
        // Bridge through Task.Run (no sync context on the pool thread) and bound it so shutdown
        // can never hang.
        try
        {
            Task.Run(async () =>
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }).Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* ignore shutdown races / timeout */ }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Keep staged files to a sane, known image extension regardless of what the client names them.
    private static string NormalizeImageExt(string? fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png") return ext;
        return string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
    }
}
