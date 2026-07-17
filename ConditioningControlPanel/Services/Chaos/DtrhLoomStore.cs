using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// File authority for THE LOOM (DtRH crafting Part 2): the player-made spiral GIFs.
/// Files land as <c>loom_&lt;slug&gt;.gif</c> (+ a <c>loom_&lt;slug&gt;.json</c> params sidecar
/// for re-editing) directly in the CCP Spirals library folder
/// (<c>%APPDATA%/ConditioningControlPanel/Spirals</c>) — the SpiralFeatureControl
/// library enumerates that folder, so a saved loom spiral appears there with ZERO
/// changes to the overlay side. Validation is C#-authoritative: slug whitelist
/// (traversal impossible), a 12-file cap, size ceilings, GIF magic + trailer.
/// </summary>
internal static class DtrhLoomStore
{
    public const int MaxSpirals = 12;
    private const int MaxBase64Chars = 8 * 1024 * 1024;      // chars of base64 accepted
    private const int MaxGifBytes = 8 * 1024 * 1024;         // decoded ceiling (worker soft-caps at 6MB)
    private const string Prefix = "loom_";

    private static readonly Regex SlugRe = new("^[a-z0-9_-]{1,24}$", RegexOptions.Compiled);

    /// <summary>Raised after a successful Save or Delete, so open UI (the
    /// SpiralFeatureControl library) can refresh without polling the folder.
    /// Raised on the caller's thread - WebView2 bridge messages arrive on the
    /// UI thread, but subscribers should still marshal defensively.</summary>
    public static event Action? Changed;

    public static string SpiralsFolder => Path.Combine(App.UserDataPath, "Spirals");

    /// <summary>Sanitize a display name into the slug the files use. Null when nothing survives.</summary>
    public static string? Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var s = name.ToLowerInvariant().Trim();
        s = Regex.Replace(s, "[^a-z0-9_-]+", "-").Trim('-');
        if (s.Length > 24) s = s[..24];
        return SlugRe.IsMatch(s) ? s : null;
    }

    /// <summary>All saved loom spirals: (slug, gif path, params-json or null).</summary>
    public static List<(string Slug, string GifPath, string? ParamsJson)> List()
    {
        var outp = new List<(string, string, string?)>();
        try
        {
            if (!Directory.Exists(SpiralsFolder)) return outp;
            foreach (var gif in Directory.EnumerateFiles(SpiralsFolder, Prefix + "*.gif").OrderBy(p => p))
            {
                var slug = Path.GetFileNameWithoutExtension(gif)[Prefix.Length..];
                if (!SlugRe.IsMatch(slug)) continue;
                string? pjson = null;
                var sidecar = Path.Combine(SpiralsFolder, Prefix + slug + ".json");
                try { if (File.Exists(sidecar)) pjson = File.ReadAllText(sidecar); } catch { }
                outp.Add((slug, gif, pjson));
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhLoomStore.List: {E}", ex.Message); }
        return outp;
    }

    /// <summary>
    /// Validate + write one spiral. Returns (ok, slug, error) where error is one of the
    /// page-known codes: bad-name / exists / cap-reached / too-big / bad-gif / io-failed.
    /// </summary>
    public static (bool Ok, string? Slug, string? Error) Save(string? name, string? gifBase64, JToken? parameters, bool overwrite)
    {
        var slug = Slugify(name);
        if (slug == null) return (false, null, "bad-name");
        if (string.IsNullOrEmpty(gifBase64) || gifBase64.Length > MaxBase64Chars) return (false, slug, "too-big");

        try
        {
            Directory.CreateDirectory(SpiralsFolder);
            var gifPath = Path.Combine(SpiralsFolder, Prefix + slug + ".gif");
            bool exists = File.Exists(gifPath);
            if (exists && !overwrite) return (false, slug, "exists");
            if (!exists && List().Count >= MaxSpirals) return (false, slug, "cap-reached");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(gifBase64); }
            catch { return (false, slug, "bad-gif"); }
            if (bytes.Length > MaxGifBytes) return (false, slug, "too-big");
            if (!LooksLikeGif(bytes)) return (false, slug, "bad-gif");

            // atomic-ish: temp write, then move over
            var tmp = gifPath + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, gifPath, overwrite: true);
            try
            {
                File.WriteAllText(Path.Combine(SpiralsFolder, Prefix + slug + ".json"),
                    (parameters ?? new JObject()).ToString());
            }
            catch { /* sidecar is a convenience (re-edit); the gif is the artifact */ }
            App.Logger?.Information("DtrhLoomStore: saved spiral {Slug} ({Bytes} bytes)", slug, bytes.Length);
            try { Changed?.Invoke(); } catch { /* a broken subscriber must not fail the save */ }
            return (true, slug, null);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhLoomStore.Save({Slug}) failed: {E}", slug, ex.Message);
            return (false, slug, "io-failed");
        }
    }

    /// <summary>Delete a spiral (+ sidecar). Clears Settings.SpiralPath if it pointed at it,
    /// so the overlay falls back to the built-in instead of a dead file.</summary>
    public static (bool Ok, string? Error) Delete(string? slug)
    {
        if (slug == null || !SlugRe.IsMatch(slug)) return (false, "bad-name");
        try
        {
            var gifPath = Path.Combine(SpiralsFolder, Prefix + slug + ".gif");
            // defense in depth: the whitelist already forbids traversal, but verify anyway
            if (!Path.GetFullPath(gifPath).StartsWith(Path.GetFullPath(SpiralsFolder), StringComparison.OrdinalIgnoreCase))
                return (false, "bad-name");
            if (!File.Exists(gifPath)) return (false, "io-failed");

            File.Delete(gifPath);

            // only after the gif is truly gone: if the overlay pointed at it, fall
            // back to the built-in (a failed delete must not reset the setting)
            try
            {
                var s = App.Settings?.Current;
                if (s != null && !string.IsNullOrEmpty(s.SpiralPath)
                    && string.Equals(Path.GetFullPath(s.SpiralPath), Path.GetFullPath(gifPath), StringComparison.OrdinalIgnoreCase))
                {
                    s.SpiralPath = "";   // back to the built-in default
                }
            }
            catch { }

            var sidecar = Path.Combine(SpiralsFolder, Prefix + slug + ".json");
            try { if (File.Exists(sidecar)) File.Delete(sidecar); } catch { }
            App.Logger?.Information("DtrhLoomStore: deleted spiral {Slug}", slug);
            try { Changed?.Invoke(); } catch { /* a broken subscriber must not fail the delete */ }
            return (true, null);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhLoomStore.Delete({Slug}) failed: {E}", slug, ex.Message);
            return (false, "io-failed");
        }
    }

    /// <summary>GIF87a/89a magic + 0x3B trailer — enough to reject arbitrary bytes.</summary>
    private static bool LooksLikeGif(byte[] b)
    {
        if (b.Length < 16) return false;
        if (b[0] != (byte)'G' || b[1] != (byte)'I' || b[2] != (byte)'F' || b[3] != (byte)'8') return false;
        if (b[4] != (byte)'7' && b[4] != (byte)'9') return false;
        if (b[5] != (byte)'a') return false;
        return b[^1] == 0x3B;
    }
}
