using Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EagleShot.Core;

/// <summary>
/// Records a screen region to MP4 (or GIF) with ffmpeg:
/// gdigrab on Windows, avfoundation on macOS, x11grab on Linux.
/// </summary>
public sealed class ScreenRecorder
{
    private readonly Process _process;
    private readonly string _videoPath;
    private readonly string? _gifPath;
    private readonly string _ffmpeg;
    private readonly List<string> _log = new();
    private bool _stopping;

    public DateTime StartedAt { get; } = DateTime.Now;
    public bool IsGif => _gifPath != null;
    public PixelRect Region { get; }

    /// <summary>Raised (on a worker thread) if ffmpeg exits on its own, with its last error output.</summary>
    public event Action<string>? Failed;

    private ScreenRecorder(Process process, string ffmpeg, string videoPath, string? gifPath, PixelRect region)
    {
        _process = process;
        _ffmpeg = ffmpeg;
        _videoPath = videoPath;
        _gifPath = gifPath;
        Region = region;
    }

    public static string? FindFfmpeg() => ToolLocator.Find("ffmpeg", App.Settings.Recording.FfmpegPath);

    /// <param name="region">Region in physical screen pixels.</param>
    /// <param name="outputPath">Final file (.mp4 or .gif).</param>
    public static async Task<ScreenRecorder> StartAsync(PixelRect region, bool gif, string outputPath)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("ffmpeg was not found. " + ToolLocator.InstallHint("ffmpeg"));
        var cfg = App.Settings.Recording;

        // H.264 with yuv420p needs even dimensions
        region = new PixelRect(region.X, region.Y, Math.Max(2, region.Width & ~1), Math.Max(2, region.Height & ~1));
        int fps = gif ? Math.Clamp(cfg.GifFps, 5, 30) : Math.Clamp(cfg.VideoFps, 5, 60);

        var videoPath = gif ? Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid():N}.mp4") : outputPath;
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        args.AddRange(await InputArgsAsync(ffmpeg, region, fps, cfg.ShowCursor));
        args.AddRange(new[]
        {
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", gif ? "18" : "23",
            "-pix_fmt", "yuv420p", "-movflags", "+faststart", videoPath
        });

        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg could not be started.");
        var recorder = new ScreenRecorder(process, ffmpeg, videoPath, gif ? outputPath : null, region);
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (recorder._log)
            {
                recorder._log.Add(e.Data);
                if (recorder._log.Count > 20) recorder._log.RemoveAt(0);
            }
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (!recorder._stopping) recorder.Failed?.Invoke(recorder.LastError());
        };

        // Fail fast if ffmpeg rejects the input (no permission, bad device, Wayland…)
        await Task.Delay(700);
        if (process.HasExited)
            throw new InvalidOperationException("Recording could not start: " + recorder.LastError());
        return recorder;
    }

    private static async Task<IEnumerable<string>> InputArgsAsync(string ffmpeg, PixelRect r, int fps, bool cursor)
    {
        var size = $"{r.Width}x{r.Height}";
        if (OperatingSystem.IsWindows())
        {
            return new[]
            {
                "-f", "gdigrab", "-draw_mouse", cursor ? "1" : "0", "-framerate", fps.ToString(),
                "-offset_x", r.X.ToString(), "-offset_y", r.Y.ToString(), "-video_size", size, "-i", "desktop"
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            int screen = await MacScreenDeviceAsync(ffmpeg);
            return new[]
            {
                "-f", "avfoundation", "-capture_cursor", cursor ? "1" : "0", "-framerate", fps.ToString(),
                "-i", $"{screen}:none", "-vf", $"crop={r.Width}:{r.Height}:{r.X}:{r.Y}"
            };
        }

        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrEmpty(display))
            throw new InvalidOperationException("Screen recording on Wayland isn't supported yet. Log in with an X11 (Xorg) session to record.");
        return new[]
        {
            "-f", "x11grab", "-draw_mouse", cursor ? "1" : "0", "-framerate", fps.ToString(),
            "-video_size", size, "-i", $"{display}+{r.X},{r.Y}"
        };
    }

    /// <summary>avfoundation lists cameras first; find the index of "Capture screen 0".</summary>
    private static async Task<int> MacScreenDeviceAsync(string ffmpeg)
    {
        var result = await ToolLocator.RunAsync(ffmpeg, new[] { "-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", "" }, TimeSpan.FromSeconds(10));
        var m = Regex.Match(result.StdErr, @"\[(\d+)\] Capture screen 0");
        return m.Success ? int.Parse(m.Groups[1].Value) : 1;
    }

    private string LastError()
    {
        lock (_log)
            return _log.Count == 0 ? "ffmpeg stopped unexpectedly." : string.Join("\n", _log.TakeLast(3));
    }

    /// <summary>Stops recording and returns the finished file (converted to GIF if requested).</summary>
    public async Task<string> StopAsync()
    {
        _stopping = true;
        if (!_process.HasExited)
        {
            try
            {
                // "q" makes ffmpeg finalize the file properly
                await _process.StandardInput.WriteAsync("q");
                await _process.StandardInput.FlushAsync();
            }
            catch { }

            var exited = _process.WaitForExitAsync();
            if (await Task.WhenAny(exited, Task.Delay(10000)) != exited)
                try { _process.Kill(true); } catch { }
        }

        if (!File.Exists(_videoPath) || new FileInfo(_videoPath).Length == 0)
            throw new InvalidOperationException("The recording is empty. " + LastError());

        if (_gifPath == null) return _videoPath;

        try
        {
            int maxWidth = Math.Max(160, App.Settings.Recording.GifMaxWidth);
            int fps = Math.Clamp(App.Settings.Recording.GifFps, 5, 30);
            var filter = $"fps={fps},scale='min({maxWidth},iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5";
            var result = await ToolLocator.RunAsync(_ffmpeg,
                new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", _videoPath, "-vf", filter, _gifPath },
                TimeSpan.FromMinutes(5));
            if (result.ExitCode != 0 || !File.Exists(_gifPath))
                throw new InvalidOperationException("GIF conversion failed: " + result.StdErr.Trim());
            return _gifPath;
        }
        finally
        {
            try { File.Delete(_videoPath); } catch { }
        }
    }

    public void Cancel()
    {
        _stopping = true;
        try { if (!_process.HasExited) _process.Kill(true); } catch { }
        Task.Delay(500).ContinueWith(_ =>
        {
            try { File.Delete(_videoPath); } catch { }
        });
    }

    /// <summary>Extracts a preview frame for the history gallery.</summary>
    public static async Task<string?> CreateThumbnailAsync(string videoPath)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg == null) return null;
        var dir = Path.Combine(AppPaths.ConfigFolder, "thumbnails");
        Directory.CreateDirectory(dir);
        var thumb = Path.Combine(dir, $"{Guid.NewGuid():N}.png");
        try
        {
            var r = await ToolLocator.RunAsync(ffmpeg,
                new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", videoPath, "-frames:v", "1", "-vf", "scale=480:-2", thumb },
                TimeSpan.FromSeconds(20));
            return r.ExitCode == 0 && File.Exists(thumb) ? thumb : null;
        }
        catch
        {
            return null;
        }
    }
}
