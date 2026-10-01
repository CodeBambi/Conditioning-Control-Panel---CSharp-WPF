using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// One thing a ring card can point at.
///
/// A target is deliberately a bag of delegates rather than a switch: the ring never learns what a
/// feature IS, only how to ask whether it exists (<see cref="IsAvailable"/>), whether the tier gate
/// would refuse it (<see cref="IsLocked"/>) and how to open it (<see cref="Open"/>). Every door in
/// the app therefore lands here as three lambdas and nothing else in the widget changes.
/// </summary>
/// <param name="Id">Stable id. It is the usage key, the pin key and the <c>ringPick</c> payload, so
/// it must never be renamed once shipped: renaming one silently resets that feature's score.</param>
/// <param name="LabelKey">Localization key, always <c>emi_desk_target_&lt;id&gt;</c>.</param>
/// <param name="ThumbPath">Resource-relative art path (<c>features/loom.png</c>), resolved through
/// <c>ModResourceResolver</c> so a .ccpmod can reskin the card. Null means "no art exists, paint the
/// hue tile instead", and no shipped target may use it: <c>EmiRingCatalogueTests</c> demands art on
/// every card, because a flat block beside five illustrated ones reads as a broken card.</param>
/// <param name="Hue">0xRRGGBB. The flat tile colour a card falls back to when its art fails to load at
/// runtime, and the tint through a medallion plate. Plain bits so Core carries no colour type; each
/// head converts at the brush.</param>
/// <param name="IsAvailable">False HIDES the card completely (a dark door, a withheld shop). Not the
/// same as locked: unavailable means the feature is not part of this build or this account at all,
/// locked means it exists and the tier gate says no.</param>
/// <param name="IsLocked">True paints the padlock and routes the click to the tier gate prompt.</param>
/// <param name="Open">Opens the feature. Always goes through <c>Pick</c>, which owns the usage
/// counter and the moments.</param>
/// <param name="ThumbIsIcon">True when <paramref name="ThumbPath"/> is a square icon (a nav door
/// medallion) rather than scene art: the card draws it centred on a plate instead of cover-cropping
/// it. See <c>EmiCardFace</c>.</param>
public sealed record EmiTarget(
    string Id,
    string LabelKey,
    string? ThumbPath,
    uint Hue,
    Func<bool> IsAvailable,
    Func<bool> IsLocked,
    Action Open,
    bool ThumbIsIcon = false)
{
    /// <summary>The card's visible name.</summary>
    public string Label
    {
        get
        {
            try { return Loc.Get(LabelKey); }
            catch { return Id; }
        }
    }

    /// <summary><see cref="IsAvailable"/> wrapped: a probe that throws hides the card.</summary>
    public bool Available
    {
        get
        {
            try { return IsAvailable(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] availability probe threw for {Target}", Id); return false; }
        }
    }

    /// <summary><see cref="IsLocked"/> wrapped: a probe that throws reads as locked, never as free.</summary>
    public bool Locked
    {
        get
        {
            try { return IsLocked(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] lock probe threw for {Target}", Id); return true; }
        }
    }
}

/// <summary>
/// The ring's door table: every target's id, art and hue, in DEFAULT ORDER. The half of the
/// catalogue both heads share; each head's <c>EmiTargets</c> answers the other half (is it there,
/// is it locked, how to open it) through <see cref="Build"/>.
///
/// <para>Order is load-bearing twice over. It breaks score ties, and before any usage exists at all
/// it IS the ring: the first six available entries are what a brand new user sees.</para>
/// </summary>
public static class EmiDoors
{
    /// <summary>One row: the stable id, its resource-relative art, its 0xRRGGBB tile hue, and
    /// whether the art is a square medallion drawn on a plate.</summary>
    public sealed record Door(string Id, string ThumbPath, uint Hue, bool ThumbIsIcon = false);

    public static readonly IReadOnlyList<Door> All = new Door[]
    {
        // ---- the six she shows a brand new user, in this order ------------------
        // The Arcademy has no Resources/features art; its own VN entrance plate is the same pixel
        // palette as the other five cards and crops cleanly to the card.
        new("arcademy", "web/arcademy/art/vn/vn-01-entrance-gates.png", 0xFF69B4),
        new("loom", "features/loom.png", 0x6FD3FF),
        new("fyp", "features/fyp.png", 0xB980FF),
        new("sessions", "features/deeper.png", 0x8C9EFF),
        new("flashes", "features/flash.png", 0xFF8FA3),
        // SIXTH, by owner decision (2026-08-30): the manual belongs in a brand new user's ring, and
        // videos is the card that steps down to seventh for it. No book PNG was ever drawn, so the
        // manual wears the "New Features" plate.
        new("codex", "features/4new.png", 0xE6D3A8),
        new("videos", "features/mandatory_videos.png", 0x8B2C6A),

        // ---- the rest of the doors ----------------------------------------------
        new("dtrh", "features/dtrh.png", 0x8CF5C8),
        new("intake", "features/lab_quiz_hero.png", 0xFFC65C),
        new("subliminals", "features/subliminal.png", 0x7FE3FF),
        new("bubbles", "features/Bubble_pop.png", 0xFFA8D8),
        new("spiral", "features/spiral_overlay.png", 0xFF69B4),
        new("pinkfilter", "features/Pink_filter.png", 0xFF9EC4),
        new("braindrain", "features/brain_drain.png", 0x9B7CE8),
        new("mindwipe", "features/Mind_Wipers.png", 0x6E7BC8),
        new("awareness", "features/awareness.png", 0xFFC65C),
        new("remote", "features/remote_control.png", 0x7FE3FF),
        new("takeover", "features/takeover.png", 0xE85CA8),
        new("lockdown", "lockdown_icon.png", 0xC84B4B),
        new("vault", "features/vault.png", 0xD8B46A),
        new("goon", "features/goon_game.png", 0x76C893),
        // The Back Room has no Resources/features art either and borrows its own room render.
        new("backroom", "web/backroom/room/backroom_final.png", 0xB95CD8),
        new("justdrop", "features/justdrop.png", 0xFFB36B),

        // ---- rooms with no card art: their nav rail medallion on a plate ---------
        // These shipped as flat hue tiles (owner report 2026-09-15). Each room already has a face,
        // its door medallion in the nav rail, and the favourites rail wears the same one.
        // "progression" is a permanent alias that lands on Home, hence the home door.
        new("companion", "nav/door_companion.png", 0xB980FF, true),
        new("progression", "nav/door_home.png", 0x8CF5C8, true),
        new("profile", "nav/door_you.png", 0x7FE3FF, true),
        new("settings", "nav/door_settings.png", 0x9A9AB8, true),
    };

    /// <summary>
    /// The head's catalogue, in table order. <paramref name="wire"/> answers each id with its three
    /// delegates, or null when the door has no surface on that head: a null HIDES the card, the
    /// same as an unavailable door, rather than offering a door that answers with a log line.
    /// </summary>
    public static List<EmiTarget> Build(Func<string, (Func<bool> Available, Func<bool> Locked, Action Open)?> wire)
    {
        var list = new List<EmiTarget>(All.Count);
        foreach (var d in All)
            if (wire(d.Id) is { } w)
                list.Add(new EmiTarget(d.Id, "emi_desk_target_" + d.Id, d.ThumbPath, d.Hue,
                    w.Available, w.Locked, w.Open, d.ThumbIsIcon));
        return list;
    }
}
