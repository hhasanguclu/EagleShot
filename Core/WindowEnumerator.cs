using Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

/// <summary>
/// Lists visible top-level windows (screen pixels, topmost first) so the region selector can
/// snap to them, like ShareX's window capture.
/// </summary>
public static class WindowEnumerator
{
    /// <param name="macScaling">Points → pixels factor on macOS (screen scaling).</param>
    public static IReadOnlyList<PixelRect> GetWindows(double macScaling = 1)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return Win32Windows();
            if (OperatingSystem.IsMacOS()) return MacWindows(macScaling);
            if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                return X11Windows();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window enumeration failed: {ex.Message}");
        }
        return Array.Empty<PixelRect>();
    }

    private static bool Useful(PixelRect r) => r.Width >= 40 && r.Height >= 30;

    // ───────────────────────────── Windows ─────────────────────────────

    private static List<PixelRect> Win32Windows()
    {
        var result = new List<PixelRect>();
        uint ownPid = (uint)Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindowTextLength(hwnd) == 0) return true;
            if ((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80) != 0) return true; // WS_EX_TOOLWINDOW
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return true;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0) return true; // hidden UWP / other desktop

            // Extended frame bounds exclude the invisible resize borders of Windows 10/11
            RECT rc;
            if (DwmGetWindowAttribute(hwnd, 9, out rc, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd, out rc))
                return true;

            var r = new PixelRect(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            if (Useful(r)) result.Add(r);
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    // ───────────────────────────── macOS ─────────────────────────────

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static List<PixelRect> MacWindows(double scaling)
    {
        var result = new List<PixelRect>();
        // kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, front to back
        IntPtr list = CGWindowListCopyWindowInfo(1 | 16, 0);
        if (list == IntPtr.Zero) return result;

        IntPtr keyLayer = CFStr("kCGWindowLayer"), keyBounds = CFStr("kCGWindowBounds"), keyPid = CFStr("kCGWindowOwnerPID");
        try
        {
            long count = CFArrayGetCount(list);
            for (long i = 0; i < count; i++)
            {
                IntPtr dict = CFArrayGetValueAtIndex(list, i);
                if (ReadInt(dict, keyLayer) != 0) continue;           // only normal windows
                if (ReadInt(dict, keyPid) == Environment.ProcessId) continue;

                IntPtr boundsDict = CFDictionaryGetValue(dict, keyBounds);
                if (boundsDict == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(boundsDict, out var b)) continue;

                var r = new PixelRect((int)(b.X * scaling), (int)(b.Y * scaling), (int)(b.Width * scaling), (int)(b.Height * scaling));
                if (Useful(r)) result.Add(r);
            }
        }
        finally
        {
            CFRelease(keyLayer);
            CFRelease(keyBounds);
            CFRelease(keyPid);
            CFRelease(list);
        }
        return result;
    }

    private static IntPtr CFStr(string s) => CFStringCreateWithCString(IntPtr.Zero, s, 0x08000100);

    private static int ReadInt(IntPtr dict, IntPtr key)
    {
        IntPtr num = CFDictionaryGetValue(dict, key);
        return num != IntPtr.Zero && CFNumberGetValue(num, 9, out int v) ? v : -1;
    }

    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, Width, Height; }

    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);
    [DllImport(CoreFoundation)] private static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr obj);

    // ───────────────────────────── X11 ─────────────────────────────

    private const string X11 = "libX11.so.6";

    private static List<PixelRect> X11Windows()
    {
        var result = new List<PixelRect>();
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) return result;
        try
        {
            IntPtr root = XDefaultRootWindow(display);
            // Prefer the window manager's stacking list (client windows, bottom to top)
            var windows = ReadWindowList(display, root, "_NET_CLIENT_LIST_STACKING");
            if (windows.Count == 0)
            {
                // No EWMH window manager: use the root's children (also bottom to top)
                if (XQueryTree(display, root, out _, out _, out IntPtr children, out uint n) != 0 && children != IntPtr.Zero)
                {
                    for (int i = 0; i < n; i++) windows.Add(Marshal.ReadIntPtr(children, i * IntPtr.Size));
                    XFree(children);
                }
            }

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var w = windows[i];
                if (XGetWindowAttributes(display, w, out var attr) == 0 || attr.map_state != 2 || attr.override_redirect != 0) continue;
                XTranslateCoordinates(display, w, root, 0, 0, out int x, out int y, out _);

                // Include the window manager's frame (title bar, borders)
                var ext = ReadCardinals(display, w, "_NET_FRAME_EXTENTS");
                int left = 0, right = 0, top = 0, bottom = 0;
                if (ext.Count == 4) { left = (int)ext[0]; right = (int)ext[1]; top = (int)ext[2]; bottom = (int)ext[3]; }

                var r = new PixelRect(x - left, y - top, attr.width + left + right, attr.height + top + bottom);
                if (Useful(r)) result.Add(r);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
        return result;
    }

    private static List<IntPtr> ReadWindowList(IntPtr display, IntPtr window, string property) =>
        ReadCardinals(display, window, property).ConvertAll(v => new IntPtr(v));

    /// <summary>Reads a 32-bit format property (stored as C longs).</summary>
    private static List<long> ReadCardinals(IntPtr display, IntPtr window, string property)
    {
        var list = new List<long>();
        IntPtr atom = XInternAtom(display, property, 1);
        if (atom == IntPtr.Zero) return list;
        if (XGetWindowProperty(display, window, atom, 0, 4096, 0, IntPtr.Zero, out _, out int format,
                out ulong items, out _, out IntPtr data) != 0 || data == IntPtr.Zero)
            return list;
        try
        {
            if (format == 32)
                for (ulong i = 0; i < items; i++)
                    list.Add(Marshal.ReadIntPtr(data, (int)i * IntPtr.Size).ToInt64());
        }
        finally
        {
            XFree(data);
        }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XWindowAttributes
    {
        public int x, y, width, height, border_width, depth;
        public IntPtr visual, root;
        public int c_class, bit_gravity, win_gravity, backing_store;
        public UIntPtr backing_planes, backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed, map_state;
        public IntPtr all_event_masks, your_event_mask, do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XInternAtom(IntPtr display, string name, int onlyIfExists);
    [DllImport(X11)] private static extern int XGetWindowProperty(IntPtr display, IntPtr w, IntPtr property, long offset, long length, int delete,
        IntPtr reqType, out IntPtr actualType, out int actualFormat, out ulong nItems, out ulong bytesAfter, out IntPtr prop);
    [DllImport(X11)] private static extern int XGetWindowAttributes(IntPtr display, IntPtr w, out XWindowAttributes attr);
    [DllImport(X11)] private static extern int XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest, int x, int y, out int destX, out int destY, out IntPtr child);
    [DllImport(X11)] private static extern int XQueryTree(IntPtr display, IntPtr w, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
    [DllImport(X11)] private static extern int XFree(IntPtr data);
}
