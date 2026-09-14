using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.BackRoom;

// THE BACK ROOM, Hypno v3 (CONTRACT 10.13.B): the optional `args` an `fx` may carry, the colour rule
// that keeps a wash from ever going white, and where a Loom-woven spiral comes from. Pure: nothing
// here touches a window, a service or a clock, so every validation rule is pinned by tests.

/// <summary>A rect in CSS px of the page viewport (<c>getBoundingClientRect</c> space).</summary>
public readonly record struct FxCssRect(double X, double Y, double W, double H);

/// <summary>An opaque 8-bit colour.</summary>
public readonly record struct FxRgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:x2}{G:x2}{B:x2}";
}

/// <summary>
/// The page's <c>args</c>, as sent. Parsing only keeps values of the right JSON type; the ranges and
/// defaults are applied per fx id by <see cref="BackRoomFxPlan"/>, so a field used by one id is never
/// silently reinterpreted by another. Nothing here is ever used as a path or as text on screen.
/// </summary>
public sealed record BackRoomFxArgs(string? Color = null, double? Strength = null, FxCssRect? From = null, int? Ms = null,
    double? Scale = null, string? Preset = null, bool Hold = false, double? Alpha = null)
{
    public static readonly BackRoomFxArgs None = new();

    /// <summary>Lenient: a missing, mistyped or non-finite field reads as absent. Never throws.</summary>
    public static BackRoomFxArgs Parse(JToken? token)
    {
        if (token is not JObject o) return None;
        try
        {
            return new BackRoomFxArgs(
                Color: o["color"] is JValue { Type: JTokenType.String } c ? (string?)c : null,
                Strength: Number(o["strength"]),
                From: Rect(o["from"]),
                Ms: Number(o["ms"]) is { } ms && Math.Abs(ms) < int.MaxValue ? (int)Math.Round(ms) : null,
                Scale: Number(o["scale"]),
                Preset: o["preset"] is JValue { Type: JTokenType.String } p ? (string?)p : null,
                Hold: o["hold"] is JValue { Type: JTokenType.Boolean } h && (bool)h,
                Alpha: Number(o["alpha"]));
        }
        catch { return None; }
    }

    private static double? Number(JToken? t)
    {
        if (t is not JValue { Type: JTokenType.Integer or JTokenType.Float } v) return null;
        double d = v.Value<double>();
        return double.IsFinite(d) ? d : null;
    }

    /// <summary>Finite, with w and h at least <see cref="BackRoomFxPlan.MinFromPx"/>. The upper bound
    /// (the viewport) is only known where the rect is mapped, so it is checked there.</summary>
    private static FxCssRect? Rect(JToken? t)
    {
        if (t is not JObject r) return null;
        if (Number(r["x"]) is not { } x || Number(r["y"]) is not { } y || Number(r["w"]) is not { } w || Number(r["h"]) is not { } h)
            return null;
        if (w < BackRoomFxPlan.MinFromPx || h < BackRoomFxPlan.MinFromPx) return null;
        return new FxCssRect(x, y, w, h);
    }
}

/// <summary>The wash colour rule: <c>^#[0-9a-fA-F]{6}$</c> or the violet default, HSL lightness capped
/// at 0.72 and saturation floored at 0.30, so a wash is never white (Law 5).</summary>
public static class FxColor
{
    public const string DefaultHex = "#9b6bff";
    public const double MaxLightness = 0.72;
    public const double MinSaturation = 0.30;

    private static readonly Regex Hex6 = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

    public static FxRgb Safe(string? hex)
    {
        var src = hex != null && Hex6.IsMatch(hex) ? hex : DefaultHex;
        double r = Convert.ToInt32(src.Substring(1, 2), 16) / 255.0;
        double g = Convert.ToInt32(src.Substring(3, 2), 16) / 255.0;
        double b = Convert.ToInt32(src.Substring(5, 2), 16) / 255.0;

        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, s = 0, hue = 0;
        double d = max - min;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) hue = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) hue = (b - r) / d + 2;
            else hue = (r - g) / d + 4;
            hue /= 6;
        }
        l = Math.Min(l, MaxLightness);
        s = Math.Max(s, MinSaturation);

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return new FxRgb(Channel(p, q, hue + 1.0 / 3), Channel(p, q, hue), Channel(p, q, hue - 1.0 / 3));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
            : t < 0.5 ? q
            : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
            : p;
        return (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
    }
}

/// <summary>
/// Where a fullscreen Back Room spiral comes from (CONTRACT 10.13.B, owner law: every spiral is
/// Loom-woven). In order: the player's own weave, when <c>SpiralPath</c> is a <c>loom_&lt;slug&gt;.gif</c>
/// directly inside the Loom's Spirals library; else the bundled weave for the preset under
/// <c>Resources/web/backroom/shared/hypno/spirals/</c>. The app's own spiral (<c>GetSpiralPath</c>) is
/// never used, because nothing guarantees it was woven.
/// </summary>
public static class BackRoomSpiralSource
{
    public const string Screen = "screen";
    public const string Wake = "wake";

    private static readonly Regex LoomFile = new("^loom_[a-z0-9_-]{1,24}\\.gif$", RegexOptions.CultureInvariant);

    /// <summary><c>screen</c> or <c>wake</c>; anything else is <c>screen</c>.</summary>
    public static string Preset(string? preset) => preset == Wake ? Wake : Screen;

    /// <returns>The file to play, or null when neither exists (the fx is skipped <c>unknown</c>).</returns>
    public static string? Resolve(string? preset, string? spiralPath, string? spiralsFolder, string? webRoot, Func<string, bool> exists)
    {
        try
        {
            if (!string.IsNullOrEmpty(spiralPath) && !string.IsNullOrEmpty(spiralsFolder)
                && LoomFile.IsMatch(Path.GetFileName(spiralPath)))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(spiralPath));
                if (dir != null && string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(spiralsFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                    && exists(Path.GetFullPath(spiralPath)))
                    return Path.GetFullPath(spiralPath);
            }
            if (string.IsNullOrEmpty(webRoot)) return null;
            var bundled = Path.Combine(webRoot, "backroom", "shared", "hypno", "spirals", Preset(preset) + ".gif");
            return exists(bundled) ? bundled : null;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "spiral source probe"); return null; }
    }
}
