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

    /// <summary>
    /// Does this channel exist upstream, and how much video does it hold? One cheap request
    /// (limit 1) on the same politeness gate as <see cref="FetchPageAsync"/>. Lives on the
    /// source rather than in the pickers because "what counts as a channel" is the source's
    /// vocabulary: a custom sub the user types must be answered by the same API that would
    /// later serve it, never by a guess.
    /// </summary>
    Task<SubProbe> ProbeSubAsync(string sub, CancellationToken ct);
}

/// <summary>
/// The answer to "is r/&lt;sub&gt; real?". Three distinct outcomes, and the pickers render all
/// three differently, so they must not be collapsed: <c>Ok</c> = it resolves (with its video
/// count, possibly 0 = stills only); <c>!Ok</c> with no <c>Error</c> = it does not exist
/// upstream (a user-facing "no such sub", worth persisting); <c>Error</c> = we could not ask
/// (transport/GraphQL), which says nothing about the sub and must never be cached as a verdict.
/// </summary>
internal sealed class SubProbe
{
    public bool Ok { get; init; }
    public int? VideoCount { get; init; }
    public string? Error { get; init; }
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
    /// <summary>Static webp/jpg/png only, from the provider's PICTURE filter. Intake
    /// captcha grids, wall cards (matches the web intake, which queries PICTURE).</summary>
    Image,
    /// <summary>Stills lifted from the provider's GIF filter — what the web feed calls
    /// "GIFs" (its only non-VIDEO GalleryFilter; scrolller serves GIF posts as silent
    /// webm/mp4 plus STATIC webp/jpg posters, and an image surface takes the poster).
    /// Flash images fetch with this and nothing else — owner decision 2026-08-12,
    /// mirroring the web's filter usage exactly. Entries come out as
    /// <see cref="RemoteMediaFormats.TypeImage"/>, same shape as <see cref="Image"/>.</summary>
    GifStill,
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

    /// <summary>Consecutive null <c>getSubreddit</c> answers on a FIRST page. One null is a bad
    /// minute, not a missing sub: on 2026-09-06 the API answered null for every request it got
    /// for over a minute, r/EroticHypnosis and r/BambiSleep included, and those resolved fine
    /// again straight after. Only a run of them says the sub is not there. Reset by
    /// <see cref="NoteSuccess"/>.</summary>
    public int NullStrikes;

    // ---- Exhaustion (the dry-channel fix, 2026-08-23) -----------------------------
    // A drained iterator comes back null and the next request restarts the sub from the top
    // under RANDOM sort, so a small community (r/censoredporn: 64 videos, r/BambiSleep: 9)
    // silently re-serves ids the consumer already holds — forever, at one request per 1.1s.
    // Nothing marked that state before: Dead only ever meant "does not resolve". So count
    // what we have actually handed out and stop asking once the channel repeats itself.

    /// <summary>Entry ids this channel has served this session. Capped: a channel that has
    /// already produced <see cref="ServedIdCap"/> distinct ids is not the tiny-community case
    /// this guard exists for, and an unbounded set in an all-night session is a leak.</summary>
    public readonly HashSet<string> ServedIds = new(StringComparer.Ordinal);

    /// <summary>Past this many distinct ids we stop tracking and treat everything as fresh.</summary>
    public const int ServedIdCap = 2000;

    /// <summary>How long an exhausted channel sits out before it is asked again — long enough
    /// that a sub which gained posts mid-session still refreshes, short enough that a niche
    /// re-picked half an hour later is not permanently written off.</summary>
    public static readonly TimeSpan ExhaustionCooldown = TimeSpan.FromMinutes(10);

    /// <summary>The channel has nothing left we have not already served.</summary>
    public bool Exhausted;

    /// <summary>When <see cref="Exhausted"/> was raised.</summary>
    public DateTime ExhaustedAtUtc;

    /// <summary>Consecutive pages that arrived non-empty but 100% already-served.</summary>
    public int DryPages;

    /// <summary>True while the channel is sitting out its exhaustion cooldown. Batch selection
    /// skips it exactly like <see cref="Dead"/> / <see cref="InBackoff"/>.</summary>
    public bool InExhaustion => Exhausted && DateTime.UtcNow < ExhaustedAtUtc + ExhaustionCooldown;

    /// <summary>Mark an id as served. Returns true when it is FRESH (not seen before on this
    /// channel this session) — call it exactly once per entry, it mutates.</summary>
    public bool NoteServed(string id)
        => ServedIds.Count >= ServedIdCap || ServedIds.Add(id);

    /// <summary>The channel repeated itself: sit out <see cref="ExhaustionCooldown"/>.</summary>
    public void NoteExhausted()
    {
        Exhausted = true;
        ExhaustedAtUtc = DateTime.UtcNow;
        DryPages = 0;
    }

    /// <summary>Cooldown served: put the channel back in the rotation. <see cref="ServedIds"/>
    /// is deliberately KEPT — the consumer still holds those clips, so counting them as fresh
    /// again would just re-arm the same repeat loop the flag exists to stop.</summary>
    public void ReviveIfDue()
    {
        if (!Exhausted || InExhaustion) return;
        Exhausted = false;
        DryPages = 0;
    }

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
        NullStrikes = 0;
        BackoffStep = 0;
        NextTryAtUtc = DateTime.MinValue;
        LastError = null;
    }
}
