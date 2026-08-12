using System;
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
    /// <param name="kind">What the caller can actually render. The FYP feed wants
    /// <see cref="FeedMediaKind.Video"/>; flashes and other still surfaces want
    /// <see cref="FeedMediaKind.Image"/>. A source that cannot serve the requested kind
    /// returns an empty page rather than substituting the other.</param>
    Task<FeedPage?> FetchPageAsync(FeedChannelState channel, FeedMediaKind kind, CancellationToken ct);
}

/// <summary>
/// What a consumer can render. This is a CONSUMPTION contract, not a provider taxonomy:
/// scrolller's own "GIF" filter yields silent webm/mp4 plus static posters and never a
/// real .gif (live-verified 2026-08-12), so animated stills are not an option here.
/// </summary>
internal enum FeedMediaKind
{
    /// <summary>Either — the source rotates whatever fills fastest.</summary>
    Any,
    /// <summary>webm/mp4 only. Everything the FYP feed has ever asked for.</summary>
    Video,
    /// <summary>Static webp/jpg/png only. Flashes, captcha grids, wall cards.</summary>
    Image,
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

    /// <summary>Turn-taking cursor for <see cref="FeedMediaKind.Any"/>, which rotates over
    /// three filters and so cannot ride the two-state <see cref="NextFilterIsGif"/> bit.
    /// Per-channel on purpose: a process-global tick would make one channel's rotation
    /// depend on how often the others were polled.</summary>
    public int AnyRotation;

    /// <summary>A filter that returned zero usable items twice stops being asked for.</summary>
    public bool VideoFilterDead;
    public bool GifFilterDead;
    public bool PictureFilterDead;
    public int EmptyVideoPages;
    public int EmptyGifPages;
    public int EmptyPicturePages;

    /// <summary>Consecutive hard failures; the channel is skipped after 3 (reset on success).</summary>
    public int Failures;

    /// <summary>The subreddit didn't resolve at all — never ask again this session.</summary>
    public bool Dead;

    // ---- Transient-failure backoff -------------------------------------------------
    // Ported from the web spiral-express port, which is the most evolved of the four
    // scrolller clients: a channel that fails transiently gets an escalating cooldown
    // instead of being retired outright, so a flaky minute doesn't permanently shrink
    // the pool for the session. Only <see cref="Dead"/> is forever.

    /// <summary>Escalating cooldown steps in seconds; index is <see cref="BackoffStep"/>.</summary>
    public static readonly int[] BackoffSeconds = { 2, 6, 15, 30, 60 };

    /// <summary>How far up <see cref="BackoffSeconds"/> this channel has climbed. Reset on
    /// any successful fetch.</summary>
    public int BackoffStep;

    /// <summary>UTC instant before which this channel must not be asked again.
    /// <see cref="DateTime.MinValue"/> = ask freely.</summary>
    public DateTime NextTryAtUtc;

    /// <summary>Why the channel last failed, for logs and the options UI. Null when healthy.</summary>
    public string? LastError;

    /// <summary>True when the channel is serving a cooldown right now.</summary>
    public bool InBackoff => DateTime.UtcNow < NextTryAtUtc;

    /// <summary>Record a transient failure and climb one backoff step.</summary>
    public void NoteFailure(string reason)
    {
        LastError = reason;
        Failures++;
        int secs = BackoffSeconds[Math.Min(BackoffStep, BackoffSeconds.Length - 1)];
        if (BackoffStep < BackoffSeconds.Length - 1) BackoffStep++;
        NextTryAtUtc = DateTime.UtcNow.AddSeconds(secs);
    }

    /// <summary>Record a successful fetch: clears the cooldown ladder.</summary>
    public void NoteSuccess()
    {
        Failures = 0;
        BackoffStep = 0;
        NextTryAtUtc = DateTime.MinValue;
        LastError = null;
    }
}
