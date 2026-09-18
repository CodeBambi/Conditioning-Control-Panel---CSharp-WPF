using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The settings side of the launcher's "Game media" dialog, kept pure so it can be tested
/// without a window. One call writes the app-wide source, the mixed share and the niche list,
/// and points the Back Room back at "follow the app" so that every room reads the same choice.
///
/// <para>The dialog's promise is "set it once and every room follows". DTRH, the race and the
/// Goon game build their pools through <c>DtrhAssetManifest</c>, the Arcademy reads the same
/// two settings directly, and the Back Room resolves <c>auto</c> and an empty sub list to the
/// app-wide values. So the only way the promise can break is a room override left behind,
/// which is why this resets the room's overrides on every apply rather than only on request.</para>
/// </summary>
public static class LauncherMediaSettings
{
    public const string SourceLocal = "local";
    public const string SourceOnline = "online";
    public const string SourceMixed = "mixed";

    /// <summary>Every source the dialog can set, in the order its chips show them.</summary>
    public static readonly string[] Sources = { SourceLocal, SourceOnline, SourceMixed };

    public static bool IsKnownSource(string? source)
        => source != null && Sources.Contains(source, StringComparer.Ordinal);

    /// <summary>
    /// Writes the choice into <paramref name="s"/>. Returns the niche list that was actually
    /// stored, which is <paramref name="niches"/> with unknown ids dropped and, if nothing is
    /// left, the catalogue's first niche: the coordinator already falls back to that niche when
    /// the list is empty, so storing it makes the dialog show what the games will really use.
    /// </summary>
    /// <exception cref="ArgumentException">An unknown source. The settings property would
    /// silently degrade it to local, and a dialog chip must never be able to do that.</exception>
    public static IReadOnlyList<string> Apply(AppSettings s, string source, int ratio, IList<string>? niches)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (!IsKnownSource(source))
            throw new ArgumentException($"Unknown media source '{source}'", nameof(source));

        var kept = KeepAtLeastOne(niches);

        s.MediaSource = source;
        s.RemoteMediaRatio = ratio;   // the property clamps to 5..95
        s.FypOnlineNiches = new List<string>(kept);

        // The room follows the app again. Its own picker can re-aim it later; this dialog is the
        // "one source for every room" surface, so a stale room override is exactly the bug.
        s.BackRoomMediaSource = "auto";
        s.BackRoomMediaSubs = new List<string>();
        s.BackRoomMediaSubsOff = new List<string>();

        return kept;
    }

    /// <summary>Known catalogue ids only, in catalogue order, never empty.</summary>
    public static List<string> KeepAtLeastOne(IEnumerable<string>? niches)
    {
        var wanted = new HashSet<string>(niches ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var kept = FypOnlineCoordinator.Catalog
            .Where(n => wanted.Contains(n.Id))
            .Select(n => n.Id)
            .ToList();
        if (kept.Count == 0) kept.Add(FypOnlineCoordinator.Catalog[0].Id);
        return kept;
    }
}
