using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// One remote content rail for the For You feed. Scrolller is the first implementation;
/// the abstraction exists because any single upstream can rot, get acquired or start
/// charging — the feed must never have exactly one. The local library is the degenerate
/// source (the init manifest built by <see cref="FypAssetManifest"/>) and deliberately
/// does not flow through this interface: it is synchronous, complete and reset-free.
///
/// THE BRIGHT LINE (owner decision, 2026-08-10): implementations run ON THE USER'S
/// DEVICE and fetch straight from the provider. Nothing here may ever route media
/// through CC Labs infrastructure — no proxying, no caching, no re-serving. See
/// planning/fyp-online/DESIGN.md.
/// </summary>
internal interface IFeedSource
{
    /// <summary>Stable id, also the entry-id prefix (e.g. "scrolller").</summary>
    string Id { get; }

    /// <summary>
    /// Fetch the next page for one channel (for Scrolller a channel is a subreddit).
    /// Returns null on hard failure; an empty page with a null iterator means the
    /// channel is exhausted for this rotation.
    /// </summary>
    Task<FeedPage?> FetchPageAsync(FeedChannelState channel, CancellationToken ct);
}

/// <summary>One fetched page of feed entries plus the cursor to continue from.</summary>
internal sealed class FeedPage
{
    public List<FypAssetManifest.Entry> Entries { get; init; } = new();
    public string? NextIterator { get; init; }
}

/// <summary>
/// Mutable per-channel rotation state owned by <see cref="FypOnlineCoordinator"/>.
/// </summary>
internal sealed class FeedChannelState
{
    /// <summary>Channel key — the subreddit name, no "r/" prefix.</summary>
    public string Name = "";

    /// <summary>Pagination cursor; null = start (RANDOM sort makes restarts harmless).</summary>
    public string? Iterator;

    /// <summary>Alternates VIDEO/GIF so gif-heavy communities still fill the feed
    /// (scrolller serves "GIFs" as silent webm/mp4 — video tiles to us either way).</summary>
    public bool NextFilterIsGif;

    /// <summary>A filter that returned zero usable items twice stops being asked for.</summary>
    public bool VideoFilterDead;
    public bool GifFilterDead;
    public int EmptyVideoPages;
    public int EmptyGifPages;

    /// <summary>Consecutive hard failures; the channel is skipped after 3 (reset on success).</summary>
    public int Failures;

    /// <summary>The subreddit didn't resolve at all — never ask again this session.</summary>
    public bool Dead;
}
