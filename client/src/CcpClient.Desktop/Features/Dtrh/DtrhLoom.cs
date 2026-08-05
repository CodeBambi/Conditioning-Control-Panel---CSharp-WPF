using System.Text.Json;
using System.Text.RegularExpressions;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// THE LOOM (b4; dtrh-admission.md §7): the saved-spiral store — the greenfield port of
/// WPF DtrhLoomStore. Files land as <c>loom_&lt;slug&gt;.gif</c> (+ a
/// <c>loom_&lt;slug&gt;.json</c> params sidecar for re-editing) in
/// <c>&lt;dataDir&gt;/Spirals</c> (DtrhLoomStore.cs:28 parity — App.UserDataPath/Spirals).
///
/// Store shape decision (Step-1 consult): the WPF folder discipline — temp-then-move
/// atomic writes (:85-87), slug whitelist (traversal impossible), the 12-file cap, size
/// ceilings, GIF magic + trailer validation. REJECTED alternative: SP-005 adaptation —
/// SP-005 is a JSON-document store (schema/migration journal); Loom artifacts are binary
/// GIFs with a convenience sidecar, no schema/migration need, and a JSON index would
/// invent a structure the WPF evidence doesn't have.
///
/// Media-logging rule (packet honesty framing c): the slug is a user-authored filename —
/// logs carry presence+shape ONLY (byte counts, list counts, error codes), never slugs
/// or names (a tightening over WPF, which logs the slug — recorded).
/// </summary>
public sealed partial class DtrhLoom
{
    /// <summary>The 12-spiral cap (DtrhLoomStore.cs:21; the page mirrors it, loomStudio.js:301).</summary>
    public const int MaxSpirals = 12;

    private const int MaxBase64Chars = 8 * 1024 * 1024; // chars of base64 accepted (:22)
    private const int MaxGifBytes = 8 * 1024 * 1024;    // decoded ceiling (:23; the worker soft-caps at 6MB)
    private const string Prefix = "loom_";

    [GeneratedRegex("^[a-z0-9_-]{1,24}$")]
    private static partial Regex SlugRegex();

    private readonly string _folder;
    private readonly Action<string> _log;

    public DtrhLoom(string spiralsFolder, Action<string> log)
    {
        _folder = spiralsFolder;
        _log = log;
    }

    public string Folder => _folder;

    /// <summary>Sanitize a display name into the slug the files use
    /// (DtrhLoomStore.Slugify :31-38). Null when nothing survives.</summary>
    public static string? Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var s = name.ToLowerInvariant().Trim();
        s = Regex.Replace(s, "[^a-z0-9_-]+", "-").Trim('-');
        if (s.Length > 24) s = s[..24];
        return SlugRegex().IsMatch(s) ? s : null;
    }

    /// <summary>One saved spiral: slug + the params sidecar JSON (null when absent).</summary>
    public sealed record Entry(string Slug, string? ParamsJson);

    /// <summary>All saved spirals in file order (DtrhLoomStore.List :45-63).</summary>
    public IReadOnlyList<Entry> List()
    {
        var outp = new List<Entry>();
        try
        {
            if (!Directory.Exists(_folder)) return outp;
            foreach (var gif in Directory.EnumerateFiles(_folder, Prefix + "*.gif").OrderBy(p => p, StringComparer.Ordinal))
            {
                var slug = Path.GetFileNameWithoutExtension(gif)[Prefix.Length..];
                if (!SlugRegex().IsMatch(slug)) continue;
                string? pjson = null;
                var sidecar = Path.Combine(_folder, Prefix + slug + ".json");
                try { if (File.Exists(sidecar)) pjson = File.ReadAllText(sidecar); } catch { /* sidecar is a convenience */ }
                outp.Add(new Entry(slug, pjson));
            }
        }
        catch (Exception ex)
        {
            _log($"dtrh-loom: list failed ({ex.GetType().Name})");
        }

        return outp;
    }

    /// <summary>The result of a save/delete (WPF returns tuples; the greenfield types it).</summary>
    public sealed record Result(bool Ok, string? Slug, string? Error);

    /// <summary>
    /// Validate + write one spiral (DtrhLoomStore.Save :65-104). Error codes are the
    /// page-known vocabulary: bad-name / too-big / exists / cap-reached / bad-gif /
    /// io-failed (loomStudio.js arms overwrite on 'exists', :113).
    /// </summary>
    public Result Save(string? name, string? gifBase64, JsonElement? parameters, bool overwrite)
    {
        var slug = Slugify(name);
        if (slug is null)
        {
            _log("dtrh-loom: save refused (bad-name)");
            return new Result(false, null, "bad-name");
        }

        if (string.IsNullOrEmpty(gifBase64) || gifBase64.Length > MaxBase64Chars)
        {
            _log("dtrh-loom: save refused (too-big, base64 length out of bounds)");
            return new Result(false, slug, "too-big");
        }

        try
        {
            Directory.CreateDirectory(_folder);
            var gifPath = Path.Combine(_folder, Prefix + slug + ".gif");
            var exists = File.Exists(gifPath);
            if (exists && !overwrite)
            {
                _log("dtrh-loom: save refused (exists — page arms overwrite)");
                return new Result(false, slug, "exists");
            }

            if (!exists && List().Count >= MaxSpirals)
            {
                _log("dtrh-loom: save refused (cap-reached, 12)");
                return new Result(false, slug, "cap-reached");
            }

            byte[] bytes;
            try { bytes = Convert.FromBase64String(gifBase64); }
            catch
            {
                _log("dtrh-loom: save refused (bad-gif, base64 decode)");
                return new Result(false, slug, "bad-gif");
            }

            if (bytes.Length > MaxGifBytes)
            {
                _log("dtrh-loom: save refused (too-big, decoded ceiling)");
                return new Result(false, slug, "too-big");
            }

            if (!LooksLikeGif(bytes))
            {
                _log("dtrh-loom: save refused (bad-gif, magic/trailer)");
                return new Result(false, slug, "bad-gif");
            }

            // atomic-ish (DtrhLoomStore.cs:85-87): temp write, then move over.
            var tmp = gifPath + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, gifPath, overwrite: true);
            try
            {
                File.WriteAllText(
                    Path.Combine(_folder, Prefix + slug + ".json"),
                    parameters is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : "{}");
            }
            catch { /* sidecar is a convenience (re-edit); the gif is the artifact (:88-92) */ }

            _log($"dtrh-loom: saved spiral ({bytes.Length} bytes)"); // presence+shape — never the slug
            return new Result(true, slug, null);
        }
        catch (Exception ex)
        {
            _log($"dtrh-loom: save failed ({ex.GetType().Name}) — io-failed");
            return new Result(false, slug, "io-failed");
        }
    }

    /// <summary>Delete a spiral (+ sidecar) (DtrhLoomStore.Delete :107-143). The WPF
    /// SpiralPath setting reset is N/A — the greenfield has no spiral-overlay setting
    /// (named limit, Step-1 design).</summary>
    public Result Delete(string? slug)
    {
        if (slug is null || !SlugRegex().IsMatch(slug))
        {
            _log("dtrh-loom: delete refused (bad-name)");
            return new Result(false, null, "bad-name");
        }

        try
        {
            var gifPath = Path.Combine(_folder, Prefix + slug + ".gif");
            // defense in depth (:113-114): the whitelist already forbids traversal.
            if (!Path.GetFullPath(gifPath).StartsWith(Path.GetFullPath(_folder), StringComparison.OrdinalIgnoreCase))
            {
                _log("dtrh-loom: delete refused (path escapes the store)");
                return new Result(false, null, "bad-name");
            }

            if (!File.Exists(gifPath))
            {
                _log("dtrh-loom: delete refused (io-failed — no such spiral)");
                return new Result(false, slug, "io-failed");
            }

            File.Delete(gifPath);
            var sidecar = Path.Combine(_folder, Prefix + slug + ".json");
            try { if (File.Exists(sidecar)) File.Delete(sidecar); } catch { /* best-effort */ }
            _log("dtrh-loom: deleted spiral"); // presence only
            return new Result(true, slug, null);
        }
        catch (Exception ex)
        {
            _log($"dtrh-loom: delete failed ({ex.GetType().Name}) — io-failed");
            return new Result(false, slug, "io-failed");
        }
    }

    /// <summary>The on-disk GIF path for a slug, or null (bad slug / no such file).
    /// DtrhLoomStore.GifPathFor :114-123 parity — the ONLY path source for loom-reveal;
    /// page strings never become paths.</summary>
    public string? GifPathFor(string? slug)
    {
        if (slug is null || !SlugRegex().IsMatch(slug)) return null;
        try
        {
            var p = Path.Combine(_folder, Prefix + slug + ".gif");
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    /// <summary>GIF87a/89a magic + 0x3B trailer, ≥16 bytes (DtrhLoomStore.LooksLikeGif
    /// :146-155) — enough to reject arbitrary bytes.</summary>
    public static bool LooksLikeGif(byte[] b)
    {
        if (b.Length < 16) return false;
        if (b[0] != (byte)'G' || b[1] != (byte)'I' || b[2] != (byte)'F' || b[3] != (byte)'8') return false;
        if (b[4] != (byte)'7' && b[4] != (byte)'9') return false;
        if (b[5] != (byte)'a') return false;
        return b[^1] == 0x3B;
    }
}
