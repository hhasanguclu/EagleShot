using System;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace EagleShot.Core;

/// <summary>
/// Full-screen capture through xdg-desktop-portal (org.freedesktop.portal.Screenshot),
/// the standard way to read the screen on Wayland (GNOME, KDE, wlroots, …).
/// </summary>
public static class PortalScreenshot
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";

    /// <returns>Path of the PNG written by the portal, or null if unavailable or denied.</returns>
    public static async Task<string?> CaptureAsync(TimeSpan timeout)
    {
        var address = Address.Session ?? throw new InvalidOperationException("No D-Bus session bus.");
        using var connection = new Connection(address);
        await connection.ConnectAsync();
        var sender = (connection.UniqueName ?? "").TrimStart(':').Replace('.', '_');
        var token = "eagleshot" + Guid.NewGuid().ToString("N")[..12];
        var requestPath = $"{PortalPath}/request/{sender}/{token}";

        var response = new TaskCompletionSource<(uint Code, string? Uri)>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe before calling to avoid missing a fast response.
        using var subscription = await connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Interface = "org.freedesktop.portal.Request",
                Member = "Response",
                Path = requestPath
            },
            (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                uint code = reader.ReadUInt32();
                var results = reader.ReadDictionaryOfStringToVariantValue();
                return (code, results.TryGetValue("uri", out var uri) ? uri.GetString() : null);
            },
            (Exception? ex, (uint Code, string? Uri) value, object? _, object? _) =>
            {
                if (ex == null) response.TrySetResult(value);
                else response.TrySetException(ex);
            },
            ObserverFlags.None, null, null, false);

        await connection.CallMethodAsync(CreateScreenshotCall(connection, token),
            (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString().ToString(), null);

        var finished = await Task.WhenAny(response.Task, Task.Delay(timeout));
        if (finished != response.Task) return null;

        var (code, uri) = await response.Task;
        if (code != 0 || string.IsNullOrEmpty(uri)) return null; // 1 = cancelled, 2 = other error

        var path = new Uri(uri).LocalPath;
        return File.Exists(path) ? path : null;
    }

    private static MessageBuffer CreateScreenshotCall(Connection connection, string token)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(PortalService, PortalPath, "org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}", MessageFlags.None);
        writer.WriteString(""); // parent window
        var dict = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("handle_token");
        writer.WriteVariantString(token);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("interactive");
        writer.WriteVariantBool(false);
        writer.WriteDictionaryEnd(dict);
        return writer.CreateMessage();
    }
}
