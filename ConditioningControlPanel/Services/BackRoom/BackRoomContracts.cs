using System.Collections.Generic;

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
public sealed record BackRoomMediaDeal(int Seed, IReadOnlyList<BackRoomGif> Gifs, IReadOnlyList<BackRoomWord> Words);

/// <summary>One dealt GIF. <see cref="Url"/> is only ever on <c>https://ccp.assets/</c> (the user's
/// folders, read-only) or <c>https://ccp.game/</c> (fallback art), never a file path.</summary>
/// <param name="Key">Opaque key the page hands back (<c>g0</c>).</param>
/// <param name="W">Pixel width, 0 when unknown.</param>
/// <param name="H">Pixel height, 0 when unknown.</param>
/// <param name="Src"><c>pool</c> or <c>fallback</c>.</param>
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
    /// <summary>The feature's own toggle is off; never forced on.</summary>
    Toggle,
    /// <summary>MotionLevel rules removed it.</summary>
    Motion,
    /// <summary>Not part of the Calm recipe.</summary>
    Calm,
    /// <summary>Waited more than 4 s behind a running hero window.</summary>
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
/// The media feed (C4, <c>BackRoomMedia.cs</c>). Deals local animated GIFs, deduped by full path,
/// plus pool words. With no pool GIF at all it deals the four fallback loops; with at least one it
/// deals only real ones (10.13.C). Words fill from the presets.
/// </summary>
public interface IBackRoomMedia
{
    /// <summary>Deal the sit-down media for <paramref name="station"/>, shuffled with <paramref name="seed"/>:
    /// up to <paramref name="count"/> GIFs (1..13, the cards table asks for 13) and four words.</summary>
    BackRoomMediaDeal Deal(string station, int seed, int count = 4);
}
