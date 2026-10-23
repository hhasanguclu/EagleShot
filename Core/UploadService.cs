using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var url = ExpandInput(cfg.RequestUrl, fileName);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Custom uploader URL is invalid.");

        var method = string.Equals(cfg.Method, "PUT", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Put
                   : string.Equals(cfg.Method, "PATCH", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Patch
                   : HttpMethod.Post;
        using var request = new HttpRequestMessage(method, uri);
        foreach (var (name, value) in ParseLines(cfg.Headers))
            request.Headers.TryAddWithoutValidation(name, ExpandInput(value, fileName));

        if (string.Equals(cfg.Body, "Binary", StringComparison.OrdinalIgnoreCase))
        {
            var content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            request.Content = content;
        }
        else
        {
            var form = BuildContent(data, fileName, mimeType,
                string.IsNullOrWhiteSpace(cfg.FileFormName) ? "file" : cfg.FileFormName.Trim());
            foreach (var (name, value) in ParseLines(cfg.Arguments))
                form.Add(new StringContent(ExpandInput(value, fileName)), name);
            request.Content = form;
        }

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Upload failed ({(int)response.StatusCode}): {Shorten(body)}");

        if (string.IsNullOrWhiteSpace(cfg.UrlJsonPath))
            return body.Trim();

        // Either a plain JSON path ("data.link") or a ShareX URL template ("{json:data.link}")
        if (cfg.UrlJsonPath.Contains('{') || cfg.UrlJsonPath.Contains('$'))
            return ExpandOutput(cfg.UrlJsonPath, body);

        return ExtractJsonPath(body, cfg.UrlJsonPath)
               ?? throw new InvalidOperationException($"'{cfg.UrlJsonPath}' was not found in the response.");
    }

    private static IEnumerable<(string Name, string Value)> ParseLines(string? text)
    {
        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int idx = line.IndexOf(':');
            if (idx <= 0) continue;
            yield return (line[..idx].Trim(), line[(idx + 1)..].Trim());
        }
    }

    /// <summary>Replaces ShareX input placeholders such as {filename}.</summary>
    private static string ExpandInput(string text, string fileName) => (text ?? "")
        .Replace("{filename}", fileName).Replace("$filename$", fileName)
        .Replace("{random}", Guid.NewGuid().ToString("N")[..8]).Replace("$random$", Guid.NewGuid().ToString("N")[..8]);

    /// <summary>Expands ShareX output syntax: {json:path}, $json:path$, {response}, $response$, {regex:pattern}.</summary>
    public static string ExpandOutput(string template, string body)
    {
        string result = Regex.Replace(template, @"[\{\$]json:([^\}\$]+)[\}\$]", m => ExtractJsonPath(body, m.Groups[1].Value) ?? "");
        result = Regex.Replace(result, @"[\{\$]regex:([^\}\$]+)[\}\$]", m =>
        {
            var match = Regex.Match(body, m.Groups[1].Value);
            return match.Success ? (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value) : "";
        });
        return result.Replace("{response}", body.Trim()).Replace("$response$", body.Trim()).Trim();
    }

    /// <summary>Imports a ShareX custom uploader (.sxcu) file.</summary>
    public static CustomUploaderSettings ImportSxcu(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string Str(string name, string fallback = "") =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
        string Pairs(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
                ? string.Join("\n", v.EnumerateObject().Select(p => $"{p.Name}: {(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}"))
                : "";

        var body = Str("Body", "MultipartFormData");
        if (body is not ("MultipartFormData" or "Binary"))
            throw new InvalidOperationException($"Body type \"{body}\" isn't supported (only MultipartFormData and Binary).");

        return new CustomUploaderSettings
        {
            Name = Str("Name", "Custom uploader"),
            RequestUrl = Str("RequestURL"),
            Method = Str("RequestMethod", "POST"),
            Body = body,
            FileFormName = Str("FileFormName", "file"),
            Headers = Pairs("Headers"),
            Arguments = Pairs("Arguments"),
            UrlJsonPath = Str("URL", "{response}")
        };
    }

    /// <summary>Shortens a link with is.gd (no account needed).</summary>
    public static async Task<string> ShortenAsync(string url)
    {
        var response = await Http.GetStringAsync("https://is.gd/create.php?format=simple&url=" + Uri.EscapeDataString(url));
        var shortUrl = response.Trim();
        if (!shortUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("URL shortener error: " + Shorten(shortUrl));
        return shortUrl;
    }

    /// <summary>Uploads any file (images, videos, documents) to the configured destination.</summary>
    public static async Task<string> UploadFileAsync(string path, AppSettings settings)
    {
        var data = await File.ReadAllBytesAsync(path);
        var name = Path.GetFileName(path);
        var mime = MimeFor(path);
        if (settings.Uploader == UploaderKind.Imgur && !mime.StartsWith("image/"))
            throw new InvalidOperationException("Imgur only accepts images. Use a custom uploader for other files.");
        var url = await UploadAsync(data, name, mime, settings);
        if (settings.ShortenUrls)
        {
            try { url = await ShortenAsync(url); } catch { /* keep the long link */ }
        }
        return url;
    }

    public static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".txt" or ".log" or ".md" => "text/plain",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };

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
