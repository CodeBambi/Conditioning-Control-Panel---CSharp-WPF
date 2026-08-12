using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Scrolller (scrolller.com) as a For You feed source: a Reddit media aggregator with an
/// unofficial GraphQL API. Everything here was live-verified 2026-08-10:
///
///   * Endpoint is <c>POST https://api.scrolller.com/admin</c> with a JSON body of
///     <c>{query, variables, authorization:null}</c> (the /api/v2/graphql path serves the
///     website shell now; gallery-dl's extractor is the reference client).
///   * <c>SubredditQuery</c> alone both resolves a subreddit AND paginates its media via
///     <c>children.iterator</c> — the separate children query's input schema has drifted,
///     so we never use it. <c>sortBy: RANDOM</c> + iterator gives non-overlapping pages.
///   * <c>mediaSources</c> mixes real video (webm/mp4), a <c>_thumb</c> webm and webp/jpg
///     stills; the video pick is "largest non-thumb webm/mp4". CDN hotlinks with no Referer
///     (HTTP 206, range OK) and webm is Chromium-native, so entries play as-is.
///
/// Stills (added 2026-08-12 for the app-wide remote-media work) come from the third
/// <c>PICTURE</c> filter and map to <c>Type = "image"</c> entries, picked by the same
/// largest-under-the-cap rule. Video entries additionally carry a
/// <see cref="FypAssetManifest.Entry.PosterUrl"/> lifted from the stills in the same post.
///
/// REMOTE STILLS ARE STATIC, FULL STOP (live-verified 2026-08-12, planning Appendix A).
/// The <c>GIF</c> filter is a misnomer for "animated content" — it returns webm/mp4
/// renditions plus static webp/jpg posters and ZERO <c>.gif</c> across three subreddits;
/// six of those webp posters were byte-checked and are plain VP8 with no <c>ANIM</c> chunk.
/// Real <c>.gif</c> shows up only incidentally under <c>PICTURE</c> and far too thinly to
/// pool. Don't write anything here that hopes a remote still will animate.
///
/// Runs entirely on the user's device (see <see cref="IFeedSource"/>'s bright line) and
/// mirrors gallery-dl's politeness: one request per second, modest page sizes.
/// </summary>
internal sealed class ScrolllerSource : IFeedSource
{
    public string Id => "scrolller";

    private const string Endpoint = "https://api.scrolller.com/admin";
    private const int PageLimit = 30;
    private const int MaxSourceWidth = 1920;          // don't stream 4K into a feed tile
    private const int MaxPosterWidth = 960;           // a poster is a placeholder, not the media
    private static readonly TimeSpan MinRequestGap = TimeSpan.FromSeconds(1.1);

    // Field subset of the reference SubredditQuery — GraphQL lets us ask for less.
    private const string SubredditQuery = @"
query SubredditQuery($url: String!, $iterator: String, $sortBy: GallerySortBy, $filter: GalleryFilter, $limit: Int!) {
    getSubreddit(data: {url: $url, iterator: $iterator, filter: $filter, limit: $limit, sortBy: $sortBy}) {
        id url title isNsfw videoCount
        children {
            iterator
            items { id title subredditTitle hasAudio mediaSources { url width height isOptimized } }
        }
    }
}";

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://scrolller.com");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://scrolller.com/");
        return c;
    }

    public async Task<FeedPage?> FetchPageAsync(FeedChannelState channel, FeedMediaKind kind, CancellationToken ct)
    {
        var filter = ChooseFilter(channel, kind);
        if (filter == null) return new FeedPage();   // every filter this kind could use is retired

        var variables = new JObject
        {
            ["url"] = "/r/" + channel.Name,
            ["iterator"] = channel.Iterator != null ? channel.Iterator : JValue.CreateNull(),
            ["sortBy"] = "RANDOM",
            ["filter"] = filter,
            ["limit"] = PageLimit,
        };

        var data = await RequestGraphQlAsync(SubredditQuery, variables, ct).ConfigureAwait(false);
        if (data == null) return null;

        var sub = data["getSubreddit"];
        if (sub == null || sub.Type == JTokenType.Null)
        {
            // The subreddit doesn't exist on scrolller (or was removed): not an error,
            // a dead channel. Caller retires it for the session.
            channel.Dead = true;
            App.Logger?.Information("[FYP online] r/{Sub} does not resolve on scrolller — channel retired", channel.Name);
            return new FeedPage();
        }

        var items = sub["children"]?["items"] as JArray;
        var iterator = (string?)sub["children"]?["iterator"];
        var entries = new List<FypAssetManifest.Entry>();
        bool stills = filter == "PICTURE";
        if (items != null)
        {
            foreach (var item in items)
            {
                var e = MapItem(item, channel.Name, stills);
                if (e != null) entries.Add(e);
            }
        }

        if (entries.Count == 0)
        {
            if (filter == "GIF" && ++channel.EmptyGifPages >= 2) channel.GifFilterDead = true;
            if (filter == "VIDEO" && ++channel.EmptyVideoPages >= 2) channel.VideoFilterDead = true;
            if (filter == "PICTURE" && ++channel.EmptyPicturePages >= 2) channel.PictureFilterDead = true;
        }

        return new FeedPage { Entries = entries, NextIterator = iterator };
    }

    /// <summary>
    /// Which scrolller filter this request asks for, or null when nothing the caller can
    /// render is left alive on the channel. Mutates the channel's rotation bit exactly the
    /// way the video path always has.
    /// </summary>
    private static string? ChooseFilter(FeedChannelState channel, FeedMediaKind kind)
    {
        switch (kind)
        {
            // Stills have only one source filter, so there is nothing to rotate.
            case FeedMediaKind.Image:
                return channel.PictureFilterDead ? null : "PICTURE";

            // VIDEO and GIF alternate so gif-heavy subreddits still produce (scrolller
            // GIFs are silent webm — video entries to the feed either way). A filter that
            // comes back empty twice is retired for the channel.
            case FeedMediaKind.Video:
            {
                if (channel.VideoFilterDead && channel.GifFilterDead) return null;
                string filter = channel.VideoFilterDead ? "GIF"
                              : channel.GifFilterDead ? "VIDEO"
                              : channel.NextFilterIsGif ? "GIF" : "VIDEO";
                channel.NextFilterIsGif = !channel.NextFilterIsGif;
                return filter;
            }

            // Three-way rotation, skipping whatever is retired. The cursor is per-channel
            // (FeedChannelState.AnyRotation) so a channel's filter order doesn't depend on
            // how often the other channels happened to be polled.
            default:
            {
                bool video = !channel.VideoFilterDead;
                bool gif = !channel.GifFilterDead;
                bool picture = !channel.PictureFilterDead;
                if (!video && !gif && !picture) return null;
                int tick = channel.AnyRotation++;
                if (channel.AnyRotation >= 3) channel.AnyRotation = 0;
                for (int i = 0; i < 3; i++)
                {
                    switch ((tick + i) % 3)
                    {
                        case 0: if (video) return "VIDEO"; break;
                        case 1: if (gif) return "GIF"; break;
                        default: if (picture) return "PICTURE"; break;
                    }
                }
                return null;
            }
        }
    }

    /// <summary>One scrolller post → one feed entry, or null when it has no source of the
    /// requested shape. Id shape "scrolller/&lt;sub&gt;/&lt;postId&gt;" — path-style on purpose:
    /// segIds are "id:k", so ids must never contain ':', and the middle segment is what
    /// lets clip-viewed reports attribute dwell back to the channel.</summary>
    /// <param name="stills">true for a PICTURE page (map to a static "image" entry),
    /// false for VIDEO/GIF (map to a "video" entry with a poster).</param>
    private static FypAssetManifest.Entry? MapItem(JToken item, string sub, bool stills)
    {
        var sources = item["mediaSources"] as JArray;
        if (sources == null || sources.Count == 0) return null;

        var best = stills
            ? PickSource(sources, RemoteMediaFormats.IsRemoteImage, MaxSourceWidth)
            : PickSource(sources, RemoteMediaFormats.IsRemoteVideo, MaxSourceWidth);
        if (best == null) return null;

        long postId = (long?)item["id"] ?? 0;
        if (postId <= 0) return null;

        var title = ((string?)item["title"] ?? "").Trim();
        if (title.Length > 90) title = title[..87] + "...";

        var entry = new FypAssetManifest.Entry
        {
            Id = $"scrolller/{sub}/{postId}",
            Url = (string?)best["url"] ?? "",
            // scrolller "GIFs" arrive as silent webm/mp4, so the only two shapes an entry
            // can take are a clip and a static still.
            Type = stills ? RemoteMediaFormats.TypeImage : RemoteMediaFormats.TypeVideo,
            Filename = title.Length > 0 ? title : $"r/{sub}",
            Folder = "r/" + sub,
            Width = (int?)best["width"],
            Height = (int?)best["height"],
            Origin = RemoteMediaFormats.OriginOnline,
            // Every post carries webp/jpg stills alongside its renditions, so a video tile
            // gets a poster for free. A still entry IS the poster.
            PosterUrl = stills
                ? null
                : (string?)PickSource(sources, RemoteMediaFormats.IsRemoteImage, MaxPosterWidth)?["url"],
        };

        // The source and the validator every consumer gates on must agree; if they ever
        // don't, the entry is dropped here rather than dying downstream as a black tile.
        if (!RemoteMediaFormats.Validate(entry, out var reason))
        {
            App.Logger?.Debug("[FYP online] dropped r/{Sub} post {Post}: {Reason}", sub, postId, reason);
            return null;
        }
        return entry;
    }

    /// <summary>
    /// Largest acceptable rendition at or under <paramref name="maxWidth"/>; a source over
    /// the cap only wins when nothing smaller exists. Shared by the video pick, the still
    /// pick and the poster pick so the three can never drift apart.
    ///
    /// <c>_thumb</c> sources are skipped for every shape: on the video side it's the
    /// preview loop rather than the clip, and on the still side it's a grid thumbnail that
    /// would look like a downscale bug at flash size. (Divergence from the mobile port,
    /// whose <c>pickPosterUrl</c> neither skips thumbs nor is order-independent.)
    /// </summary>
    private static JToken? PickSource(JArray sources, Func<string?, bool> accept, int maxWidth)
    {
        JToken? best = null;
        int bestWidth = -1;
        foreach (var s in sources)
        {
            var url = (string?)s["url"];
            if (string.IsNullOrEmpty(url)) continue;
            if (!accept(url) || url.Contains("_thumb", StringComparison.OrdinalIgnoreCase)) continue;
            int w = (int?)s["width"] ?? 0;
            bool better = best == null
                || (w <= maxWidth && (bestWidth > maxWidth || w > bestWidth))
                || (w > maxWidth && bestWidth > maxWidth && w < bestWidth);
            if (better) { best = s; bestWidth = w; }
        }
        return best;
    }

    /// <summary>POST one GraphQL operation, globally throttled to ~1 req/s. Returns the
    /// "data" object, or null on transport/HTTP/GraphQL-error failure (logged).</summary>
    private static async Task<JObject?> RequestGraphQlAsync(string query, JObject variables, CancellationToken ct)
    {
        await RequestGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = MinRequestGap - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
            _lastRequestUtc = DateTime.UtcNow;

            var body = new JObject
            {
                ["query"] = query,
                ["variables"] = variables,
                ["authorization"] = JValue.CreateNull(),
            };
            using var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(Endpoint, content, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                App.Logger?.Warning("[FYP online] scrolller API HTTP {Code}: {Body}",
                    (int)resp.StatusCode, text.Length > 200 ? text[..200] : text);
                return null;
            }
            var parsed = JObject.Parse(text);
            if (parsed["errors"] is JArray { Count: > 0 } errors)
            {
                App.Logger?.Warning("[FYP online] scrolller GraphQL error: {E}",
                    (string?)errors[0]?["message"] ?? "unknown");
                return null;
            }
            return parsed["data"] as JObject;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            App.Logger?.Warning("[FYP online] scrolller request failed: {E}", ex.Message);
            return null;
        }
        finally { RequestGate.Release(); }
    }
}
