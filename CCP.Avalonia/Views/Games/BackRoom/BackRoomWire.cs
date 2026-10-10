using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using CoreIntensity = ConditioningControlPanel.Services.BackRoom.BackRoomFxIntensity;

namespace ConditioningControlPanel.Avalonia.Views.Games.BackRoom;

/// <summary>
/// The host's settings projection for the Back Room and Breakout pages (WPF BackRoomHostService:
/// GatesWire, MediaWire, AudioWire, IntensityWire, ApplyRoomOption, SettingsFrameProperties), with no
/// window in it so the suite can hold each shape to the WPF one. Back Room options are AppSettings and
/// never synced.
/// </summary>
internal static class BackRoomWire
{
    /// <summary>The room lexicon's prefix: <c>init.lex</c> carries every en.json key that starts with it (10.13).</summary>
    internal const string LexPrefix = "br_";

    /// <summary><c>init.lex</c>: each key once, in the current language (English where it has no row).</summary>
    internal static Dictionary<string, string> Lex(IEnumerable<string> keys, Func<string, string> get)
        => keys.Distinct(StringComparer.Ordinal).ToDictionary(k => k, get, StringComparer.Ordinal);

    /// <summary><c>gates</c> (10.13.A, 10.14): the four hypno gates are ALWAYS on (owner, 2026-09-18: no CCP
    /// toggle gates Back Room effects); only the room's own tunnel and melt switches vary.</summary>
    internal static object GatesWire(AppSettings? s) => new
    {
        flash = true,
        subliminal = true,
        spiral = true,
        brainDrain = true,
        tunnel = s?.BackRoomTunnel ?? false,
        melt = s?.BackRoomMelt ?? false,
    };

    /// <summary><c>intensityChoice</c> (10.14): the player's own Calm / Normal / Full, shown even while
    /// <c>intensity</c> is forced to calm below MotionLevel Full.</summary>
    internal static string IntensityChoiceWire(AppSettings? s)
        => (s?.BackRoomFxIntensity ?? CoreIntensity.Normal).ToString().ToLowerInvariant();

    internal static string MotionWire(MotionLevel m) => m.ToString().ToLowerInvariant();

    /// <summary>The effective intensity: calm whenever MotionLevel is not Full (CONTRACT section 4).</summary>
    internal static string IntensityWire(AppSettings? s, MotionLevel motion)
        => motion != MotionLevel.Full ? "calm" : IntensityChoiceWire(s);

    /// <summary><c>media</c>: what the room's picture picker shows. <c>effective</c> is "auto" resolved against
    /// the app with the consent collapse (the one authority, <see cref="BackRoomMedia.EffectiveMediaSource"/>).</summary>
    internal static object MediaWire(AppSettings? s) => new
    {
        source = s?.BackRoomMediaSource ?? "auto",
        effective = BackRoomMedia.EffectiveMediaSource(s),
        subs = (s?.BackRoomMediaSubs ?? new List<string>()).ToArray(),
        off = (s?.BackRoomMediaSubsOff ?? new List<string>()).ToArray(),
        cap = AppSettings.BackRoomMediaSubCap,
        consented = s?.HasRemoteMediaConsent ?? false,
        ratio = s?.RemoteMediaRatio ?? 30,
    };

    /// <summary><c>audio</c>: the room's own three levels as 0..1. Not the app's volumes (10.21). Breakout reads its own.</summary>
    internal static object AudioWire(AppSettings? s, bool breakout = false) => new
    {
        sub = SubVolume(s, breakout) / 100.0,
        sfx = ((breakout ? s?.BreakoutSfxVolume : s?.BackRoomSfxVolume) ?? 100) / 100.0,
        music = ((breakout ? s?.BreakoutMusicVolume : s?.BackRoomMusicVolume) ?? 15) / 100.0,
    };

    /// <summary>The spoken word's level for whichever page is open: the room's, or Breakout's own.</summary>
    internal static int SubVolume(AppSettings? s, bool breakout)
        => (breakout ? s?.BreakoutSubVolume : s?.BackRoomSubVolume) ?? 100;

    /// <summary>The <c>settings</c> frame (WPF SettingsMessage), sent in full every time.</summary>
    internal static object SettingsMessage(AppSettings? s, bool breakoutPage, object? breakout)
    {
        var motion = s?.MotionLevel ?? MotionLevel.Full;
        return new
        {
            type = "settings", motion = MotionWire(motion), intensity = IntensityWire(s, motion),
            breakout,
            breakoutStandalone = breakoutPage,
            reduced = motion != MotionLevel.Full, gates = GatesWire(s), intensityChoice = IntensityChoiceWire(s),
            invertLook = s?.BackRoomInvertLook ?? false,
            media = MediaWire(s), audio = AudioWire(s, breakoutPage),
        };
    }

    /// <summary>Settings whose change pushes a full <c>settings</c> frame (WPF SettingsFrameProperties). Not the
    /// panel's Flash / Subliminal / Spiral / Brain Drain toggles: the room's gates do not follow them.</summary>
    internal static readonly HashSet<string> SettingsFrameProperties = new(StringComparer.Ordinal)
    {
        nameof(AppSettings.MotionLevel), nameof(AppSettings.BackRoomFxIntensity),
        nameof(AppSettings.BackRoomTunnel), nameof(AppSettings.BackRoomMelt),
        nameof(AppSettings.BackRoomInvertLook),
        nameof(AppSettings.BackRoomMediaSource), nameof(AppSettings.BackRoomMediaSubs),
        nameof(AppSettings.BackRoomMediaSubsOff),
        nameof(AppSettings.BackRoomSubVolume), nameof(AppSettings.BackRoomSfxVolume),
        nameof(AppSettings.BackRoomMusicVolume),
        nameof(AppSettings.BreakoutSubVolume), nameof(AppSettings.BreakoutSfxVolume),
        nameof(AppSettings.BreakoutMusicVolume),
        nameof(AppSettings.MediaSource), nameof(AppSettings.RemoteMediaRatio),
    };

    /// <summary>A <c>room-option</c> the bridge already validated, written to the settings (10.14). The caller
    /// saves. A standalone Breakout page keeps its own three levels (owner, 2026-09-27).</summary>
    internal static void ApplyRoomOption(AppSettings s, BackRoomBridge.RoomOption option, bool breakout = false)
    {
        if (breakout)
        {
            switch (option.Key)
            {
                case BackRoomBridge.OptionSubVolume when option.Level is { } bsub: s.BreakoutSubVolume = bsub; return;
                case BackRoomBridge.OptionSfxVolume when option.Level is { } bsfx: s.BreakoutSfxVolume = bsfx; return;
                case BackRoomBridge.OptionMusicVolume when option.Level is { } bmus: s.BreakoutMusicVolume = bmus; return;
            }
        }
        switch (option.Key)
        {
            case BackRoomBridge.OptionTunnel: s.BackRoomTunnel = option.On; break;
            case BackRoomBridge.OptionMelt: s.BackRoomMelt = option.On; break;
            case BackRoomBridge.OptionInvertLook: s.BackRoomInvertLook = option.On; break;
            case BackRoomBridge.OptionWelcomeSeen: s.BackRoomWelcomeSeen = option.On; break;
            case BackRoomBridge.OptionIntensity when option.Intensity is { } i:
                s.BackRoomFxIntensity = i switch
                {
                    BackRoomFxIntensity.Calm => CoreIntensity.Calm,
                    BackRoomFxIntensity.Full => CoreIntensity.Full,
                    _ => CoreIntensity.Normal,
                };
                break;
            case BackRoomBridge.OptionMediaSource when option.Text is { } src: s.BackRoomMediaSource = src; break;
            case BackRoomBridge.OptionSubVolume when option.Level is { } sub: s.BackRoomSubVolume = sub; break;
            case BackRoomBridge.OptionSfxVolume when option.Level is { } sfx: s.BackRoomSfxVolume = sfx; break;
            case BackRoomBridge.OptionMusicVolume when option.Level is { } mus: s.BackRoomMusicVolume = mus; break;
            case BackRoomBridge.OptionSubAdd when option.Text is { } add: AddNiche(s, add); break;
            case BackRoomBridge.OptionSubRemove when option.Text is { } drop: RemoveNiche(s, drop); break;
            case BackRoomBridge.OptionSubToggle when option.Text is { } flip: ToggleNiche(s, flip); break;
            default: return;
        }
    }

    // The room's niche list (10.13.C): the host is the only writer, one niche per press, case-insensitive,
    // the cap enforced here as well as in the room. A niche switched off KEEPS its place in the list.
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void AddNiche(AppSettings s, string name)
    {
        var subs = new List<string>(s.BackRoomMediaSubs);
        if (subs.Exists(x => Same(x, name)) || subs.Count >= AppSettings.BackRoomMediaSubCap) return;
        subs.Add(name);
        s.BackRoomMediaSubs = subs;
    }

    private static void RemoveNiche(AppSettings s, string name)
    {
        var subs = new List<string>(s.BackRoomMediaSubs);
        if (subs.RemoveAll(x => Same(x, name)) == 0) return;
        s.BackRoomMediaSubs = subs;
        var off = new List<string>(s.BackRoomMediaSubsOff);
        if (off.RemoveAll(x => Same(x, name)) > 0) s.BackRoomMediaSubsOff = off;
    }

    private static void ToggleNiche(AppSettings s, string name)
    {
        if (!s.BackRoomMediaSubs.Exists(x => Same(x, name))) return;
        var off = new List<string>(s.BackRoomMediaSubsOff);
        if (off.RemoveAll(x => Same(x, name)) == 0) off.Add(name);
        s.BackRoomMediaSubsOff = off;
    }

    /// <summary>The Back Room slot (CONTRACT 10.24, WPF ChasterHooks.SlotLineRow): the melt line books the melt
    /// row, the jackpot line wipes the tab, every other line books nothing.</summary>
    internal static string? SlotLineRow(string? line) => line switch
    {
        "melt" => "melt",
        "emi3" => ConditioningControlPanel.Services.Chaster.CircesTab.JackpotEventId,
        _ => null,
    };
}
