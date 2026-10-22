using System;
using System.IO;
using System.Linq;
using System.Text;

namespace EagleShot.Core;

/// <summary>
/// Expands ShareX-style name patterns, e.g. "EagleShot_%y-%mo-%d_%h-%mi-%s".
/// </summary>
public static class FileNameFormatter
{
    public const string Help =
        "%y year · %mo month · %d day · %h hour · %mi minute · %s second · %ms millisecond · " +
        "%w width · %ht height · %i counter · %rn random · %guid GUID · %un user · %pc computer";

    public static string Format(string pattern, int width, int height, int counter, DateTime? time = null)
    {
        var t = time ?? DateTime.Now;
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "EagleShot_%y-%mo-%d_%h-%mi-%s";

        var sb = new StringBuilder(pattern)
            .Replace("%guid", Guid.NewGuid().ToString("N"))
            .Replace("%rn", RandomString(6))
            .Replace("%ms", t.ToString("fff"))
            .Replace("%mo", t.ToString("MM"))
            .Replace("%mi", t.ToString("mm"))
            .Replace("%ht", height.ToString())
            .Replace("%un", Environment.UserName)
            .Replace("%pc", Environment.MachineName)
            .Replace("%y", t.ToString("yyyy"))
            .Replace("%d", t.ToString("dd"))
            .Replace("%h", t.ToString("HH"))
            .Replace("%s", t.ToString("ss"))
            .Replace("%w", width.ToString())
            .Replace("%i", counter.ToString("D3"));

        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(sb.ToString().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(name) ? "EagleShot" : name;
    }

    /// <summary>Returns a path in <paramref name="folder"/> that doesn't exist yet.</summary>
    public static string UniquePath(string folder, string name, string extension)
    {
        var path = Path.Combine(folder, $"{name}.{extension}");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name} ({i}).{extension}");
        return path;
    }

    private static string RandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, length).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }
}
