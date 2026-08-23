using System.Text.Json.Nodes;
using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// <c>init.settings.localAssets</c> — the asset provider's LOCAL inventory, built host-side
/// because the page cannot enumerate a virtual host (<c>ArcademyHostService.BuildLocalAssets</c>,
/// <c>:656-691</c>; the build contract explicitly allows the manifest to ride
/// <c>init.settings</c>, <c>:639-645</c>).
///
/// <para><b>Two pools, not one, and the split is the ported detail</b>: <c>gifs</c> and
/// <c>stills</c> off the SAME <c>images/</c> folder (<c>:665-671</c>), sampled to
/// <see cref="Sample"/> each by upstream's partial Fisher-Yates (<c>:675-684</c>) so a large pool
/// costs a fixed-size init rather than a growing one.</para>
///
/// <para><b>The deselection blacklist is honoured here</b> (<c>:662</c>, <c>:669</c>: "unchecking
/// an image hides it from the Arcademy too") through the port's own shared predicate,
/// <see cref="DtrhUserMedia.IsAssetActive"/> — the same one DTRH, intake and goon route through,
/// so one uncheck keeps meaning one thing everywhere.</para>
/// </summary>
public static class ArcademyLocalAssets
{
    /// <summary>Per-pool sample size (<c>ArcademyHostService.LocalAssetSample</c>, <c>:654</c>).</summary>
    public const int Sample = 60;

    /// <summary>The still extensions upstream accepts beside <c>.gif</c> (<c>:668</c>).</summary>
    private static readonly string[] StillExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Build the two pools. Never throws — upstream logs and moves on
    /// (<c>:649-650</c>), because an unreadable media folder must not cost the page its boot.</summary>
    public static JsonObject Build(string? userMediaRoot, string mediaOrigin,
        HashSet<string>? disabled = null, bool useWhitelist = false)
    {
        var gifs = new List<string>();
        var stills = new List<string>();
        try
        {
            if (userMediaRoot is { Length: > 0 })
            {
                var root = Path.GetFullPath(userMediaRoot);
                var images = DtrhUserMedia.ImagesFolder(root);
                if (Directory.Exists(images))
                {
                    foreach (var file in Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        var isGif = ext == ".gif";
                        if (!isGif && !StillExtensions.Contains(ext)) continue;
                        if (!DtrhUserMedia.IsAssetActive(disabled, root, file, useWhitelist)) continue;
                        (isGif ? gifs : stills).Add(ToAssetUrl(mediaOrigin, root, file));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Upstream's posture (:649-650): a broken pool degrades to whatever was collected,
            // never to a failed boot. Counts-only logging is the caller's; no name is ever logged.
        }

        return new JsonObject
        {
            ["gifs"] = ToArray(Take(gifs)),
            ["stills"] = ToArray(Take(stills)),
        };
    }

    private static JsonArray ToArray(List<string> urls)
    {
        var array = new JsonArray();
        foreach (var url in urls)
        {
            array.Add(JsonValue.Create(url));
        }

        return array;
    }

    /// <summary>Upstream's partial Fisher-Yates: a random slice without shuffling the whole list
    /// (<c>:675-684</c>).</summary>
    private static List<string> Take(List<string> pool)
    {
        var take = Math.Min(Sample, pool.Count);
        for (var i = 0; i < take; i++)
        {
            var j = Random.Shared.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.GetRange(0, take);
    }

    /// <summary>The loopback url for one pool file — upstream's <c>ToAssetsUrl</c> shape
    /// (<c>:693-698</c>: root-relative, segment-escaped), against this port's
    /// <c>/umedia/</c> route rather than <c>https://ccp.assets/</c>.</summary>
    private static string ToAssetUrl(string mediaOrigin, string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return $"{mediaOrigin}/umedia/{escaped}";
    }
}
