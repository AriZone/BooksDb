using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace BooksDb.Android;

public static class CaptureSender
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<bool> SendAsync(string baseUrl, string token, string sessionId,
        string isbn, string shelf, IReadOnlyList<byte[]> photos)
    {
        try
        {
            var url = baseUrl.TrimEnd('/') + "/captures";

            using var form = new MultipartFormDataContent
            {
                { new StringContent(sessionId), "sessionId" },
                { new StringContent(isbn), "isbn" },
                { new StringContent(shelf ?? ""), "shelf" },
                { new StringContent(DateTimeOffset.UtcNow.ToString("o")), "capturedAt" },
            };

            for (int i = 0; i < photos.Count; i++)
            {
                var part = new ByteArrayContent(photos[i]);
                part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                form.Add(part, "photos", $"photo{i + 1}.jpg");   // first part = primary on the desktop
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await Http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}