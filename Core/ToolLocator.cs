using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EagleShot.Core;

/// <summary>
/// Finds external command line tools (ffmpeg, tesseract). Apps started from Finder or the
/// Start menu don't always inherit the shell PATH, so common install folders are searched too.
/// </summary>
public static class ToolLocator
{
    public static string? Find(string name, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".exe" : name;

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(ExtraFolders(name));

        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static string[] ExtraFolders(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new[] { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin" };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new[]
            {
                Path.Combine(AppContext.BaseDirectory),
                Path.Combine(AppPaths.ConfigFolder, "tools"),
                Path.Combine(pf, "ffmpeg", "bin"),
                Path.Combine(pf, "Tesseract-OCR"),
                Path.Combine(local, "Programs", "Tesseract-OCR"),
                Path.Combine(local, "Microsoft", "WinGet", "Links"),
                @"C:\ffmpeg\bin",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims"),
                @"C:\ProgramData\chocolatey\bin",
            };
        }

        return new[] { "/usr/local/bin", "/usr/bin", "/snap/bin" };
    }

    public static string InstallHint(string name) => (name, RuntimeInformation.IsOSPlatform(OSPlatform.Windows), RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) switch
    {
        ("ffmpeg", true, _) => "Install it with \"winget install Gyan.FFmpeg\" or set its path in Settings → Recording.",
        ("ffmpeg", _, true) => "Install it with \"brew install ffmpeg\" or set its path in Settings → Recording.",
        ("ffmpeg", _, _) => "Install it with your package manager, e.g. \"sudo apt install ffmpeg\".",
        ("tesseract", true, _) => "Install Tesseract OCR (winget install UB-Mannheim.TesseractOCR) or set its path in Settings → Tools.",
        ("tesseract", _, true) => "Install it with \"brew install tesseract tesseract-lang\".",
        _ => "Install it with your package manager, e.g. \"sudo apt install tesseract-ocr\"."
    };

    public record RunResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>Runs a tool and collects its output.</summary>
    public static async Task<RunResult> RunAsync(string exe, string[] args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        var exited = proc.WaitForExitAsync();
        if (await Task.WhenAny(exited, Task.Delay(timeout)) != exited)
        {
            try { proc.Kill(true); } catch { }
            throw new TimeoutException($"{Path.GetFileName(exe)} did not finish in time.");
        }
        return new RunResult(proc.ExitCode, await stdout, await stderr);
    }
}
