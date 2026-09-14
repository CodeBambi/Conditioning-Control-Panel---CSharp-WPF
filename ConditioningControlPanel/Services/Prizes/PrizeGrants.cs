using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// The Back Room prize counter's grant ids (owner redesign, 2026-09-13) plus the static facade the
/// prize effect lanes call. The ids are a wire contract with the server, which is the only authority
/// on who owns what; appending is safe, renaming one strands every account that already owns it.
///
/// <para>The static facade keeps the signatures of the temporary seam on feat/fx-base so those lanes
/// compile unchanged, and forwards to the <see cref="OwnershipService"/> App attaches at startup.</para>
/// </summary>
public static class PrizeGrants
{
    public const string JackpotRemix = "fx.jackpot_remix";
    public const string FlashDriftBounce = "fx.flash.drift_bounce";
    public const string FlashPendulum = "fx.flash.pendulum";
    public const string BubbleRain = "fx.bubble.rain";
    public const string BubbleSpiralIn = "fx.bubble.spiral_in";

    /// <summary>Racing Thoughts original tracks: prefix and highest track number; see <see cref="RacingTrack"/>.</summary>
    public const string RacingTrackPrefix = "rt.original.";
    public const int RacingTrackMax = 10;

    /// <summary>
    /// Server-only: the Discord High Roller role. Listed so the id has one spelling in the
    /// codebase, but the client NEVER gates anything on it and it is not in <see cref="All"/>.
    /// </summary>
    public const string DiscordHighRoller = "discord.high_roller";

    /// <summary>The 16 grants the client gates on, in roster order.</summary>
    public static readonly IReadOnlyList<string> All = BuildAll();

    private static OwnershipService? _service;

    /// <summary>
    /// Raised whenever the attached service raises <see cref="OwnershipService.OwnershipChanged"/>,
    /// so on the same thread (the WPF UI thread in the app).
    /// </summary>
    public static event Action? GrantsChanged;

    /// <summary>True when the current account owns <paramref name="grantId"/>; false before App attaches the service.</summary>
    public static bool IsGranted(string grantId) => _service?.IsGranted(grantId) ?? false;

    /// <summary>Pure matcher: does one override <paramref name="pattern"/> (exact id, <c>prefix.*</c> or <c>*</c>) cover <paramref name="grantId"/>?</summary>
    public static bool Matches(string pattern, string grantId)
        => !string.IsNullOrEmpty(grantId)
           && OwnershipService.IsValidPattern(pattern)
           && OwnershipService.MatchesOverride(new[] { pattern }, grantId);

    /// <summary>Raise <see cref="GrantsChanged"/> by hand (tests, and the adapter below).</summary>
    public static void RaiseGrantsChanged() => GrantsChanged?.Invoke();

    /// <summary>Point the facade at <paramref name="service"/> (App does this once at construction).</summary>
    internal static void Attach(OwnershipService? service)
    {
        if (_service != null) _service.OwnershipChanged -= OnOwnershipChanged;
        _service = service;
        if (service != null) service.OwnershipChanged += OnOwnershipChanged;
    }

    private static void OnOwnershipChanged(object? sender, OwnershipChangedEventArgs e) => RaiseGrantsChanged();

    /// <summary>
    /// "rt.original.NN" for Racing Thoughts original track <paramref name="trackNum"/>
    /// (0 to <see cref="RacingTrackMax"/>, always two digits).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Outside 0 to 10.</exception>
    public static string RacingTrack(int trackNum)
    {
        if (trackNum < 0 || trackNum > RacingTrackMax)
            throw new ArgumentOutOfRangeException(nameof(trackNum), trackNum, "Racing Thoughts original tracks run 0 to 10.");
        return RacingTrackPrefix + trackNum.ToString("00", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> BuildAll()
    {
        var all = new List<string> { JackpotRemix, FlashDriftBounce, FlashPendulum, BubbleRain, BubbleSpiralIn };
        for (var i = 0; i <= RacingTrackMax; i++) all.Add(RacingTrack(i));
        return all.AsReadOnly();
    }
}
