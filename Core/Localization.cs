using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EagleShot.Core;

/// <summary>
/// UI translations. English text is the key; Resources/i18n/&lt;code&gt;.json maps it to the
/// translation. Missing entries fall back to English.
/// </summary>
public static class L
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"),
        ("tr", "Türkçe"),
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("pl", "Polski"),
        ("uk", "Українська"),
        ("ru", "Русский"),
    };

    /// <summary>Tesseract language for each UI language, used for automatic OCR.</summary>
    public static readonly IReadOnlyDictionary<string, string> OcrLanguage = new Dictionary<string, string>
    {
        ["en"] = "eng", ["tr"] = "tur", ["zh-CN"] = "chi_sim", ["zh-TW"] = "chi_tra", ["ja"] = "jpn",
        ["ko"] = "kor", ["de"] = "deu", ["fr"] = "fra", ["pl"] = "pol", ["uk"] = "ukr", ["ru"] = "rus",
    };

    private static Dictionary<string, string> _strings = new();

    /// <summary>The active language code, e.g. "en" or "zh-CN".</summary>
    public static string Current { get; private set; } = "en";

    /// <param name="setting">The language from settings; empty means the system language.</param>
    public static void Init(string? setting)
    {
        Current = Languages.Any(l => l.Code == setting) ? setting! : Detect();
        _strings = Load(Current);
        try { CultureInfo.CurrentUICulture = new CultureInfo(Current); } catch { }
    }

    /// <summary>
    /// The UI font list: classic Tahoma for Latin and Cyrillic text, then the system's CJK font
    /// for the active language so Chinese, Japanese and Korean use their own glyph shapes.
    /// </summary>
    public static string FontFamily()
    {
        string cjk = Current switch
        {
            "zh-CN" => "Microsoft YaHei UI, Microsoft YaHei, PingFang SC, Noto Sans CJK SC, Source Han Sans SC, WenQuanYi Zen Hei, ",
            "zh-TW" => "Microsoft JhengHei UI, Microsoft JhengHei, PingFang TC, Noto Sans CJK TC, Source Han Sans TC, WenQuanYi Zen Hei, ",
            "ja" => "Yu Gothic UI, Meiryo UI, Meiryo, Hiragino Sans, Noto Sans CJK JP, Source Han Sans JP, WenQuanYi Zen Hei, ",
            "ko" => "Malgun Gothic, Apple SD Gothic Neo, Noto Sans CJK KR, Source Han Sans KR, WenQuanYi Zen Hei, ",
            _ => ""
        };
        return "Tahoma, " + cjk + "Microsoft Sans Serif, Verdana, DejaVu Sans, Liberation Sans, sans-serif";
    }

    public static string T(string english) =>
        _strings.TryGetValue(english, out var t) && !string.IsNullOrEmpty(t) ? t : english;

    public static string F(string english, params object?[] args)
    {
        try { return string.Format(T(english), args); }
        catch (FormatException) { return string.Format(english, args); }
    }

    private static Dictionary<string, string> Load(string code)
    {
        if (code == "en") return new();
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://EagleShot/Resources/i18n/{code}.json"));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Translations for {code} not loaded: {ex.Message}");
            return new();
        }
    }

    // ───────────────────────────── System language ─────────────────────────────

    public static string Detect()
    {
        foreach (var name in SystemLanguageNames())
        {
            var code = Match(name);
            if (code != null) return code;
        }
        return "en";
    }

    /// <summary>Maps "pt-BR", "zh_Hant_TW", "de-AT"... to one of <see cref="Languages"/>.</summary>
    public static string? Match(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Replace('_', '-').Split('.', '@')[0].Trim().ToLowerInvariant();
        if (n.StartsWith("zh"))
            return n.Contains("hant") || n.EndsWith("-tw") || n.EndsWith("-hk") || n.EndsWith("-mo") ? "zh-TW" : "zh-CN";
        var lang = n.Split('-')[0];
        return Languages.Select(l => l.Code).FirstOrDefault(c => c == lang);
    }

    private static IEnumerable<string> SystemLanguageNames()
    {
        // macOS apps started from Finder have no LANG, so ask the system preferences
        if (OperatingSystem.IsMacOS())
        {
            var mac = MacPreferredLanguage();
            if (mac != null) yield return mac;
        }
        foreach (var v in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = Environment.GetEnvironmentVariable(v);
            if (string.IsNullOrEmpty(value)) continue;
            foreach (var part in value.Split(':')) yield return part;
        }
        yield return CultureInfo.InstalledUICulture.Name;
        yield return CultureInfo.CurrentUICulture.Name;
    }

    private static string? MacPreferredLanguage()
    {
        try
        {
            var psi = new ProcessStartInfo("defaults", "read -g AppleLanguages")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(2000);
            // ( "tr-TR", "en-US" )
            return output.Split('"', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).FirstOrDefault(s => s.Length >= 2 && char.IsLetter(s[0]));
        }
        catch
        {
            return null;
        }
    }

    // ───────────────────────────── Translating built UI ─────────────────────────────

    /// <summary>
    /// Translates the texts of a control and everything inside it: TextBlocks, button and
    /// check box contents, headers, tooltips, watermarks and window titles. Text the user
    /// typed (TextBox.Text) is never touched.
    /// </summary>
    public static void Apply(StyledElement? root)
    {
        if (root == null || Current == "en") return;
        Translate(root);
        if (root is ILogical logical)
            foreach (var child in logical.GetLogicalDescendants())
                if (child is StyledElement e) Translate(e);
    }

    private static void Translate(StyledElement e)
    {
        switch (e)
        {
            case Window w when !string.IsNullOrEmpty(w.Title):
                w.Title = T(w.Title);
                break;
            case TextBox tb:
                if (tb.Watermark != null) tb.Watermark = T(tb.Watermark);
                break;
            case TextBlock t when t.Text != null && t.Inlines is not { Count: > 0 }:
                t.Text = T(t.Text);
                break;
        }
        if (e is HeaderedContentControl { Header: string h } hc) hc.Header = T(h);
        if (e is HeaderedSelectingItemsControl { Header: string hs } hsc) hsc.Header = T(hs);
        if (e is ContentControl { Content: string c } cc) cc.Content = T(c);
        if (e is Control control)
        {
            if (ToolTip.GetTip(control) is string tip) ToolTip.SetTip(control, T(tip));
            if (control.ContextMenu != null) Apply(control.ContextMenu);
            if (control is Button { Flyout: Flyout { Content: StyledElement fc } }) Apply(fc);
        }
    }
}
