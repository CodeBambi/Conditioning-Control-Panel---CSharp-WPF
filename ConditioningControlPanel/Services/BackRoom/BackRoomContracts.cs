using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.BackRoom;

// THE BACK ROOM: shared shapes. Every client lane (host C1, slot relay C2, fx C3, media C4) codes
// against these and nothing else, so the lanes can land in any order. The source of truth is
// Resources\web\backroom\CONTRACT.md; if this file and the contract disagree, the contract wins
// and this file is fixed to match. Shapes only, no implementations.

/// <summary>
/// Media dealt for one sit-down (CONTRACT section 5). Dealt once and kept until the player stands
/// up, so a reel cell never changes face mid-tape. Symbol ids map by index: <c>gif0..gif3</c> =
/// <see cref="Gifs"/>[0..3], <c>sub0..sub3</c> = <see cref="Words"/>[0..3]. Nothing in a deal is
/// ever sent to the server; only keys go back to the host in <c>fx.symbols</c>.
/// </summary>
/// <param name="Seed">Shuffle seed echoed to the page in the <c>media</c> message.</param>
/// <param name="Gifs">Up to the requested count of pool GIFs, or the four fallback loops when the pool has none.</param>
/// <param name="Words">Up to four words, shortfall filled from the presets.</param>
/// <param name="Source">Where the pictures actually came from once <c>auto</c> and the consent collapse
/// were resolved: <c>local</c>, <c>online</c>, <c>mixed</c> or <c>bundled</c> (10.13.C). Echoed to the
/// page in the <c>media</c> message so the room's Options can show what it GOT rather than what it
/// asked for - a player who picked online with a dry warm pool is looking at their own folders and
/// deserves to be told so. Defaulted, so a host or a rig that predates the amendment still compiles.</param>
public sealed record BackRoomMediaDeal(int Seed, IReadOnlyList<BackRoomGif> Gifs, IReadOnlyList<BackRoomWord> Words,
    string Source = "local");

/// <summary>One dealt GIF. <see cref="Url"/> is only ever on <c>https://ccp.assets/</c> (the user's
/// folders, read-only) or <c>https://ccp.game/</c> (fallback art), never a file path.</summary>
/// <param name="Key">Opaque key the page hands back (<c>g0</c>).</param>
/// <param name="W">Pixel width, 0 when unknown.</param>
/// <param name="H">Pixel height, 0 when unknown.</param>
/// <param name="Src"><c>pool</c> (a file in the user's folders), <c>online</c> (remote content,
/// materialized under the assets temp folder so it carries a <c>ccp.assets</c> url like any other) or
/// <c>fallback</c> (built-in art). The page's wall filter keys off <c>fallback</c> and nothing else.
/// An <c>online</c> item is a still at a station and may be a CLIP on the wall (a <c>.webm</c> /
/// <c>.mp4</c> url): the page routes it on the extension (<c>room\gif.js</c> -> <c>room\clip-source.js</c>,
/// WebView2 decodes it natively), so a clip needed no fourth <c>Src</c> and no change to this wire.</param>
public sealed record BackRoomGif(string Key, string Url, int W, int H, string Src);

/// <summary>One dealt subliminal word. For <c>preset</c> words <see cref="Text"/> is already
/// resolved through the lexicon (Law VII).</summary>
/// <param name="Key">Opaque key the page hands back (<c>s0</c>).</param>
/// <param name="Src"><c>pool</c> or <c>preset</c>.</param>
public sealed record BackRoomWord(string Key, string Text, string Src);

/// <summary>Why a primitive did not play (CONTRACT section 2.2, <c>fx-ack.skipped[].why</c>).
/// Serialised lower-case on the wire.</summary>
public enum BackRoomFxSkipReason
{
    // The Back Room is an authored show (2026-09-15): no setting ever skips a primitive, so the old
    // toggle / motion / calm reasons are gone. Only these two remain.
    /// <summary>Waited more than 4 s behind a running hero window, or inside a Brake gap (a wash within
    /// 360 ms of the last, a second gif-from while one shows).</summary>
    Busy,
    /// <summary>The fx id or primitive is not known to this host (page shipped ahead).</summary>
    Unknown,
}

/// <summary>One skipped primitive in an ack.</summary>
/// <param name="Prim">Primitive name, e.g. <c>gif-rain</c>, or the fx id when the whole id is unknown.</param>
public sealed record BackRoomFxSkip(string Prim, BackRoomFxSkipReason Why);

/// <summary>What actually played for one <c>fx</c> request; becomes the <c>fx-ack</c> reply.</summary>
/// <param name="Fired">Primitive names that started.</param>
/// <param name="Skipped">Primitives that did not, each with a reason.</param>
public sealed record BackRoomFxAck(IReadOnlyList<string> Fired, IReadOnlyList<BackRoomFxSkip> Skipped);

/// <summary>Setting <c>AppSettings.BackRoomFxIntensity</c> (CONTRACT section 4). Default
/// <see cref="Normal"/>. <see cref="Calm"/> is also forced whenever MotionLevel is not Full.
/// Full never breaks the Brake: no strobe over 6 Hz, one hero at a time, toggles still win.</summary>
public enum BackRoomFxIntensity
{
    Calm,
    Normal,
    Full,
}

/// <summary>
/// The effect dispatcher (C3, <c>BackRoomFx.cs</c>). Resolves a global fx id into primitives on
/// existing services, applying toggles, motion, intensity and the one-hero rule on the host side.
/// </summary>
public interface IBackRoomFx
{
    /// <summary>Fire <paramref name="fxId"/> (e.g. <c>fx.gif_storm</c>) for <paramref name="station"/>.
    /// <paramref name="symbolKeys"/> are symbol ids from the tape; they resolve only against
    /// <paramref name="deal"/>, and an unknown key is replaced with a random dealt item, so nothing
    /// from the page is ever used as a path. Never throws: an unknown id acks as skipped
    /// <see cref="BackRoomFxSkipReason.Unknown"/>.</summary>
    BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal);

    /// <summary>Hypno v3 (10.13.B): fire with the page's validated-on-arrival <paramref name="args"/>, remembering
    /// <paramref name="token"/> so a later <c>fx-release</c> can fade what it holds. A host that predates the
    /// amendment ignores both.</summary>
    BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal,
        BackRoomFxArgs? args, string? token) => Fire(fxId, station, symbolKeys, deal);

    /// <summary><c>fx-release</c>: fade out whatever that token still holds on screen.</summary>
    void Release(string token, string station) { }

    /// <summary><c>fx-tunnel</c>: the wanted tunnel vision level 0..1 (the host gates, halves under Calm, throttles).</summary>
    void Tunnel(string station, double level) { }

    /// <summary><c>station-close</c>: release every hold and the tunnel that station started.</summary>
    void ReleaseStation(string station) { }

    /// <summary>Stop every running primitive the room started (on <c>suspend</c> or <c>close</c>).</summary>
    void CancelAll();
}

/// <summary>
/// The media feed (C4, <c>BackRoomMedia.cs</c>). Deals animated GIFs from the user's folders, deduped
/// by full path, or remote content from a warm pool, or the built-in loops, depending on the
/// effective media source (10.13.C). With nothing real to deal it deals the four fallback loops; with
/// at least one real picture it deals only real ones. Words fill from the presets.
///
/// <para>Remote content splits on the station: the WALL (<c>station: "room"</c>) is dealt playable
/// clips topped up with stills, and a chair is dealt stills only - thirteen video decoders for one
/// sit-down is reckless and the slot's reel textures cannot take a webm anyway. The ladder only ever
/// goes one way: clips, then stills, then the user's folders, then the bundled loops, never an empty
/// deal.</para>
/// </summary>
public interface IBackRoomMedia
{
    /// <summary>Deal the sit-down media for <paramref name="station"/>, shuffled with <paramref name="seed"/>:
    /// up to <paramref name="count"/> GIFs (1..13, the cards table asks for 13) and four words. The
    /// synchronous deal: it serves whatever a warm remote pool already holds and never waits on one.</summary>
    BackRoomMediaDeal Deal(string station, int seed, int count = 4);

    /// <summary>As <see cref="Deal"/>, and what the protocol calls. <paramref name="source"/> is the
    /// optional <c>media-request.source</c> override, already whitelisted by the bridge; null or
    /// <c>auto</c> means the room's own setting decides. An implementation may top a remote pool up
    /// here, which is why this is the async one; the default just runs the sync deal.</summary>
    Task<BackRoomMediaDeal> DealAsync(string station, int seed, int count = 4, string? source = null,
        CancellationToken ct = default) => Task.FromResult(Deal(station, seed, count));

    /// <summary>Room open: start filling whatever pool the feed warms, so the first sit-down is a
    /// memory read rather than a network round trip. Safe to call when the room is local-only.</summary>
    void WarmForRoomOpen() { }

    /// <summary>Room closed: hand back anything the warm pool materialized.</summary>
    void ReleaseWarmPool() { }

    /// <summary>After a deal that could not use the remote pool (cold, or short of <c>count</c>): wait,
    /// unbounded but cancellable, for the batch in flight to finish, and say whether the pool now holds
    /// anything. False at once when nothing is warming. The bridge turns a true into a <c>media-warm</c>
    /// frame so the page re-deals the moment the pictures exist, the way the web shim's
    /// <c>br-media-changed</c> does after its warm - the alternative was the wall's own 72 s refresh.</summary>
    Task<bool> WaitForWarmAsync(CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>What the host did with one <c>word.speak</c> (CONTRACT 10.21). Becomes the
/// <c>word-ack</c> reply.</summary>
/// <param name="Source">
/// <c>clip</c>   the player's own audio for that phrase (a trigger's PlayAudio action, the active mod's
///               flashes_audio, or Resources/sub_audio);
/// <c>preset</c> a bundled Back Room word clip (Resources/Audio/backroom/words/words.json);
/// <c>tts</c>    rendered here by Windows speech;
/// <c>none</c>   nothing played, so the page falls back to its own speechSynthesis.
/// </param>
/// <param name="DurationMs">How long the audio runs, 0 when nothing played. The page holds the next
/// word of a chain until this has elapsed (callout.js WORD_GAP_MS is the floor).</param>
public sealed record BackRoomVoiceAck(string Source, int DurationMs);

/// <summary>
/// The spoken subliminal word (<c>BackRoomVoice.cs</c>). One voice per room; a new
/// <see cref="Speak"/> cuts the previous line, exactly as the page's speechSynthesis did.
/// Never throws: everything it cannot do acks <c>none</c> and the page speaks for itself.
/// </summary>
public interface IBackRoomVoice
{
    /// <summary>Say <paramref name="text"/> now, through the app's chosen audio output device.
    /// <paramref name="reversed"/> is the easter egg: the decoded samples play backwards, which is
    /// the real thing the page could only fake by spelling the word backwards.
    /// <paramref name="seed"/> is the outcome's seed, so a replayed outcome picks the same clip.</summary>
    BackRoomVoiceAck Speak(string text, bool reversed, int seed);

    /// <summary>Law VI: cancel, suspend and leave drop the line at once.</summary>
    void Stop();
}
