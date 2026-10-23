using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpHook;
using SharpHook.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EagleShot.Core;

/// <summary>
/// ShareX-style scrolling capture: grabs a region, scrolls the window under it with simulated
/// mouse-wheel events, and stitches the frames by matching their overlapping rows.
/// </summary>
public static class ScrollingCapture
{
    private const int MaxHeight = 30000;

    /// <param name="captureRegion">Region inside the full-screen capture bitmap (capture pixels).</param>
    /// <param name="screenPoint">Where to put the mouse so the wheel scrolls the right window (screen pixels).</param>
    public static async Task<WriteableBitmap?> RunAsync(PixelRect captureRegion, PixelPoint screenPoint,
        int maxScrolls, int delayMs, Action<int>? progress, CancellationToken token)
    {
        var simulator = new EventSimulator();
        simulator.SimulateMouseMovement((short)screenPoint.X, (short)screenPoint.Y);
        await Task.Delay(150, token);

        var first = await GrabAsync(captureRegion);
        if (first == null) return null;

        int width = first.Width, height = first.Height;
        var rows = new List<byte[]>();
        for (int y = 0; y < height; y++) rows.Add(Row(first, y));

        var previous = first;
        short direction = 3; // wheel "clicks" per step; the sign is flipped if nothing moves
        bool directionChecked = false;
        int unchanged = 0;

        for (int i = 0; i < maxScrolls && !token.IsCancellationRequested; i++)
        {
            simulator.SimulateMouseWheel(direction, MouseWheelScrollDirection.Vertical, MouseWheelScrollType.UnitScroll);
            await Task.Delay(Math.Max(100, delayMs), token);

            var current = await GrabAsync(captureRegion);
            if (current == null) break;

            int shift = FindShift(previous, current, out bool identical);
            if (identical || shift <= 0)
            {
                if (!directionChecked)
                {
                    // The platform scrolled the other way (or not at all): try the opposite sign once
                    direction = (short)-direction;
                    directionChecked = true;
                    continue;
                }
                if (++unchanged >= 2) break; // reached the end
                continue;
            }

            directionChecked = true;
            unchanged = 0;
            // The last `shift` rows of the new frame are content we haven't seen yet
            for (int y = height - shift; y < height; y++) rows.Add(Row(current, y));
            previous = current;
            progress?.Invoke(rows.Count);
            if (rows.Count >= MaxHeight) break;
        }

        return Build(rows, width);
    }

    private static async Task<PixelBuffer?> GrabAsync(PixelRect region)
    {
        var capture = await Task.Run(ScreenCaptureService.CaptureFullScreen);
        if (capture == null) return null;
        using var cropped = ImageHelper.Crop(capture.Bitmap, region);
        capture.Bitmap.Dispose();
        return ImageHelper.ReadPixels(cropped);
    }

    private static byte[] Row(PixelBuffer b, int y)
    {
        var row = new byte[b.Stride];
        Buffer.BlockCopy(b.Data, y * b.Stride, row, 0, b.Stride);
        return row;
    }

    /// <summary>
    /// Finds how many rows the content moved up between two frames by comparing row hashes.
    /// The right edge is ignored because scrollbars change between frames.
    /// </summary>
    public static int FindShift(PixelBuffer prev, PixelBuffer cur, out bool identical)
    {
        int h = Math.Min(prev.Height, cur.Height);
        int ignoreRight = Math.Min(24, prev.Width / 8);
        var p = RowHashes(prev, ignoreRight, out var pUniform);
        var c = RowHashes(cur, ignoreRight, out _);

        identical = true;
        for (int y = 0; y < h; y++)
            if (p[y] != c[y]) { identical = false; break; }
        if (identical) return 0;

        int bestShift = -1, bestScore = 0;
        int minOverlap = Math.Max(8, h / 10);
        for (int s = 1; s <= h - minOverlap; s++)
        {
            int overlap = h - s, matches = 0, informative = 0, mismatches = 0;
            for (int i = 0; i < overlap; i++)
            {
                if (p[s + i] == c[i])
                {
                    matches++;
                    if (!pUniform[s + i]) informative++;
                }
                else if (++mismatches > overlap / 20) break; // allow a little noise (cursor, animations)
            }
            if (mismatches > overlap / 20 || informative < 4) continue;
            // Prefer the shift explained by the most non-blank rows
            if (informative > bestScore)
            {
                bestScore = informative;
                bestShift = s;
            }
        }
        return bestShift;
    }

    private static ulong[] RowHashes(PixelBuffer b, int ignoreRight, out bool[] uniform)
    {
        var hashes = new ulong[b.Height];
        uniform = new bool[b.Height];
        int usable = Math.Max(1, b.Width - ignoreRight) * 4;
        for (int y = 0; y < b.Height; y++)
        {
            ulong hash = 14695981039346656037UL;
            int o = y * b.Stride;
            uint firstPixel = BitConverter.ToUInt32(b.Data, o);
            bool same = true;
            for (int x = 0; x < usable; x += 4)
            {
                uint px = BitConverter.ToUInt32(b.Data, o + x);
                if (px != firstPixel) same = false;
                hash = (hash ^ px) * 1099511628211UL;
            }
            hashes[y] = hash;
            uniform[y] = same;
        }
        return hashes;
    }

    private static WriteableBitmap Build(List<byte[]> rows, int width)
    {
        var wb = new WriteableBitmap(new PixelSize(width, rows.Count), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        for (int y = 0; y < rows.Count; y++)
            Marshal.Copy(rows[y], 0, fb.Address + y * fb.RowBytes, Math.Min(rows[y].Length, fb.RowBytes));
        return wb;
    }
}
