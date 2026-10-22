using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EagleShot.Core;

/// <summary>
/// Uploads images to Imgur or to a user-defined HTTP endpoint (ShareX-style custom uploader).
/// </summary>
public static class UploadService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static bool IsConfigured(AppSettings s) => s.Uploader switch
    {
        UploaderKind.Imgur => !string.IsNullOrWhiteSpace(s.ImgurClientId),
        UploaderKind.Custom => Uri.TryCreate(s.CustomUploader.RequestUrl, UriKind.Absolute, out _),
        _ => false
    };

    /// <returns>The public URL of the uploaded image.</returns>
    public static async Task<string> UploadAsync(byte[] data, string fileName, string mimeType, AppSettings settings)
    {
        return settings.Uploader switch
        {
            UploaderKind.Imgur => await UploadImgurAsync(data, fileName, mimeType, settings.ImgurClientId),
            UploaderKind.Custom => await UploadCustomAsync(data, fileName, mimeType, settings.CustomUploader),
            _ => throw new InvalidOperationException("No uploader is configured. Choose one in Settings → Upload.")
        };
    }

    private static async Task<string> UploadImgurAsync(byte[] data, string fileName, string mimeType, string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Imgur Client ID is empty. Set it in Settings → Upload.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.imgur.com/3/image");
        request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", clientId.Trim());
        request.Content = BuildContent(data, fileName, mimeType, "image");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Imgur returned {(int)response.StatusCode}: {Shorten(body)}");

        return ExtractJsonPath(body, "data.link")
               ?? throw new InvalidOperationException("Imgur response did not contain a link.");
    }

    private static async Task<string> UploadCustomAsync(byte[] data, string fileName, string mimeType, CustomUploaderSettings cfg)
    {
        if (!Uri.TryCreate(cfg.RequestUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Custom uploader URL is invalid.");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        foreach (var line in (cfg.Headers ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int idx = line.IndexOf(':');
            if (idx <= 0) continue;
            request.Headers.TryAddWithoutValidation(line[..idx].Trim(), line[(idx + 1)..].Trim());
        }
        request.Content = BuildContent(data, fileName, mimeType,
            string.IsNullOrWhiteSpace(cfg.FileFormName) ? "file" : cfg.FileFormName.Trim());

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Upload failed ({(int)response.StatusCode}): {Shorten(body)}");

        if (string.IsNullOrWhiteSpace(cfg.UrlJsonPath))
            return body.Trim();

        return ExtractJsonPath(body, cfg.UrlJsonPath)
               ?? throw new InvalidOperationException($"'{cfg.UrlJsonPath}' was not found in the response.");
    }

    private static MultipartFormDataContent BuildContent(byte[] data, string fileName, string mimeType, string field)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        return new MultipartFormDataContent { { file, field, fileName } };
    }

    /// <summary>Resolves paths like "data.link" or "files[0].url".</summary>
    public static string? ExtractJsonPath(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(part, @"^([^\[]*)((\[\d+\])*)$");
                var name = m.Success ? m.Groups[1].Value : part;
                if (name.Length > 0)
                {
                    if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out el)) return null;
                }
                foreach (Match idx in Regex.Matches(m.Groups[2].Value, @"\[(\d+)\]"))
                {
                    int i = int.Parse(idx.Groups[1].Value);
                    if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() <= i) return null;
                    el = el[i];
                }
            }
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Shorten(string s) => s.Length > 200 ? s[..200] + "…" : s;
}
