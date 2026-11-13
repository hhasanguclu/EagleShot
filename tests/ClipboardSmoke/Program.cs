using System.Diagnostics;
using EagleShot.Core;
using SkiaSharp;

// Puts a test image on the clipboard the way EagleShot does, then reads it back with
// PowerShell (what terminals such as Claude Code use) and .NET's WinForms clipboard.
if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Windows only - skipped.");
    return 0;
}

const int w = 120, h = 80;
using var sk = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
using (var canvas = new SKCanvas(sk))
{
    canvas.Clear(new SKColor(30, 58, 95));
    canvas.DrawRect(10, 10, 40, 30, new SKPaint { Color = SKColors.Red });
}
var png = SKImage.FromBitmap(sk).Encode(SKEncodedImageFormat.Png, 100).ToArray();
var pixels = new PixelBuffer(sk.Bytes, w, h);
var file = Path.Combine(Path.GetTempPath(), "eagleshot-clipboard-test.png");
File.WriteAllBytes(file, png);

if (!ClipboardService.CopyImageWin32(png, pixels, file))
{
    Console.WriteLine("FAIL: CopyImageWin32 returned false");
    return 1;
}

string Ps(string script)
{
    var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var a in new[] { "-NoProfile", "-STA", "-Command", script }) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
    p.WaitForExit();
    return output.Trim();
}

var image = Ps("$i = Get-Clipboard -Format Image; if ($i) { \"$($i.Width)x$($i.Height)\" } else { 'none' }");
var winforms = Ps("Add-Type -AssemblyName System.Windows.Forms; $i = [System.Windows.Forms.Clipboard]::GetImage(); if ($i) { \"$($i.Width)x$($i.Height)\" } else { 'none' }");
var files = Ps("Get-Clipboard -Format FileDropList | ForEach-Object { $_.FullName }");
var formats = Ps("Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Clipboard]::GetDataObject().GetFormats() -join ', '");

Console.WriteLine($"Get-Clipboard -Format Image : {image}");
Console.WriteLine($"WinForms Clipboard.GetImage : {winforms}");
Console.WriteLine($"FileDropList                : {files}");
Console.WriteLine($"Formats                     : {formats}");

bool ok = image == $"{w}x{h}" && winforms == $"{w}x{h}" && files.Equals(file, StringComparison.OrdinalIgnoreCase);
Console.WriteLine(ok ? "PASS" : "FAIL");
return ok ? 0 : 1;
