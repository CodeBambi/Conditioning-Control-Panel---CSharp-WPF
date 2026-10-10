using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// The pure half of Piece by Piece's online pictures (2026-09-28): who may fetch, from which
/// niches, and what share of a deal the online pictures take. The fetch itself is the Goon
/// Game's pool (<see cref="GoonOnlineMedia.ForGame"/>), the niche grammar and the stored-blob
/// rules are <see cref="GoonOnlineMediaRules"/>; nothing here does I/O or reads App statics.
///
/// <para>CONSENT. Remote pictures are fetched only when the player's own switch
/// (<c>PbpMediaOnline</c>) is on AND either the app-wide online source is consented
/// (<c>MediaSource</c> not local and <c>HasRemoteMediaConsent</c>) or the player picked a flavour
/// inside the game. Since 2026-09-30 (owner: "record this choice and don't make them pick
/// again") the pick is SAVED and stands as this game's own opt-in in every later window too
/// (<see cref="SavedOptIn"/>); it is still never written back as app-wide consent, and "no
/// online pictures" is saved the same way. Everything else is the player's own library.</para>
/// </summary>
internal static class PbpMediaRules
{
    /// <summary>Has the player made the one-time picture choice: an explicit pick from the page,
    /// or (saved before the flag existed) a stored flavour or "own pictures only". The page asks
    /// at the first start while this is false, and never again once it is true.</summary>
    public static bool HasSavedChoice(bool chosen, bool pbpMediaOnline, string? flavour)
        => chosen || !pbpMediaOnline || GoonOnlineMediaRules.CleanFlavour(flavour) != "";

    /// <summary>The saved pick is the chess game's opt-in at boot: the switch on and a real
    /// flavour stored. Chess only; never app-wide consent.</summary>
    public static bool SavedOptIn(bool pbpMediaOnline, string? flavour)
        => GoonOnlineMediaRules.IsSessionOptIn(pbpMediaOnline, GoonOnlineMediaRules.CleanFlavour(flavour));

    /// <summary>Share of a deal the online pictures take when the player picked a flavour in the
    /// game (and the app-wide source is not "online", which is all online).</summary>
    public const int PickedSharePct = 70;

    /// <summary>The app-wide online source is on and consented.</summary>
    public static bool AppWideOnline(string? mediaSource, bool remoteConsent)
        => remoteConsent && !string.Equals(mediaSource ?? "local", "local", StringComparison.OrdinalIgnoreCase);

    /// <summary>May the host fetch anything at all for the chess game.</summary>
    public static bool FetchAllowed(bool pbpMediaOnline, bool sessionOptIn, string? mediaSource, bool remoteConsent)
        => pbpMediaOnline && (sessionOptIn || AppWideOnline(mediaSource, remoteConsent));

    /// <summary>
    /// The niches to fetch: the stored flavour's niches when one is picked, else the app-wide
    /// Scrolller selection when the app-wide source is consented, else none. Always cleaned and
    /// capped at <see cref="GoonOnlineMediaRules.MaxSubs"/>. Empty when fetching is not allowed.
    /// </summary>
    public static List<string> ChannelsFor(bool pbpMediaOnline, bool sessionOptIn, string? mediaSource,
        bool remoteConsent, string? flavour, IEnumerable<string>? flavourSubs, IEnumerable<string>? appChannels)
    {
        if (!FetchAllowed(pbpMediaOnline, sessionOptIn, mediaSource, remoteConsent)) return new List<string>();
        var picked = GoonOnlineMediaRules.CleanFlavour(flavour);
        if (picked != "") return GoonOnlineMediaRules.CleanSubs(flavourSubs);
        // No flavour: only the app-wide consent can open the door, and then the app's own niches.
        return AppWideOnline(mediaSource, remoteConsent)
            ? GoonOnlineMediaRules.CleanSubs(appChannels)
            : new List<string>();
    }

    /// <summary>
    /// Percentage of draws that come from the online set when both sets can answer. An app-wide
    /// "online" source is all online; an app-wide "mixed" source keeps its own ratio unless the
    /// player picked a flavour in the game this session, which asks for the pictures at
    /// <see cref="PickedSharePct"/>. The page falls back to whichever side has pictures.
    /// </summary>
    public static int SharePct(string? mediaSource, int remoteRatio, bool sessionOptIn)
    {
        var src = (mediaSource ?? "local").ToLowerInvariant();
        if (src == "online") return 100;
        if (src == "mixed" && !sessionOptIn) return Math.Clamp(remoteRatio, 5, 95);
        return PickedSharePct;
    }
}
