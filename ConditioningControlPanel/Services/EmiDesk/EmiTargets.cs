using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The ring's target catalogue: every door EMI can open, in DEFAULT ORDER.
///
/// Catalogue order is load-bearing twice over. It breaks score ties, and before any usage exists at
/// all it IS the ring: the first six available entries are what a brand new user sees. That is why
/// arcademy, loom, fyp, sessions, flashes and videos sit at the top.
/// </summary>
public static class EmiTargets
{
    // ---- small helpers the entries are built from -------------------------------

    private static MainWindow? Mw
    {
        get
        {
            try { return App.MainWindowRef ?? Application.Current?.MainWindow as MainWindow; }
            catch { return null; }
        }
    }

    /// <summary>Bring the app forward and navigate. A tab opened behind a tray icon is not opened.</summary>
    private static void Nav(string tabKey)
    {
        var mw = Mw;
        if (mw == null) { Log.Debug("[EmiDesk] no main window, cannot show tab {Tab}", tabKey); return; }
        try { mw.ShowFromTray(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowFromTray failed"); }
        mw.ShowTab(tabKey);
    }

    /// <summary>Bring the app forward and focus a Studio rack module.</summary>
    private static void Rack(string rackKey)
    {
        var mw = Mw;
        if (mw == null) { Log.Debug("[EmiDesk] no main window, cannot open rack {Rack}", rackKey); return; }
        try { mw.ShowFromTray(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowFromTray failed"); }
        mw.OpenStudioModule(rackKey);
    }

    private static bool PremiumOk(string labelKey, string? dailyKey)
    {
        try
        {
            var name = Loc.Get(labelKey);
            return dailyKey == null
                ? TierGate.RequiresPremium(name).Allowed
                : TierGate.RequiresPremium(name, dailyKey).Allowed;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] premium probe failed"); return false; }
    }

    private static bool LabOk(string labelKey, string? dailyKey)
    {
        try
        {
            var name = Loc.Get(labelKey);
            return dailyKey == null
                ? TierGate.RequiresLab(name).Allowed
                : TierGate.RequiresLab(name, dailyKey).Allowed;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] lab probe failed"); return false; }
    }

    /// <summary>The locked-card click: the app's own refusal toast, never a sales pitch of her own.</summary>
    private static void PremiumPrompt(string labelKey, string? dailyKey)
    {
        try
        {
            var name = Loc.Get(labelKey);
            if (dailyKey == null) TierGate.DemandPremium(name);
            else TierGate.DemandPremium(name, dailyKey);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] premium prompt failed"); }
    }

    private static void LabPrompt(string labelKey, string? dailyKey)
    {
        try
        {
            var name = Loc.Get(labelKey);
            if (dailyKey == null) TierGate.DemandLab(name);
            else TierGate.DemandLab(name, dailyKey);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] lab prompt failed"); }
    }

    private static readonly Func<bool> Always = () => true;
    private static readonly Func<bool> Never = () => false;

    // ============================================================================
    // the catalogue
    // ============================================================================

    private static readonly List<EmiTarget> _all = Build();

    /// <summary>Every target, in default order.</summary>
    public static IReadOnlyList<EmiTarget> All => _all;

    /// <summary>Catalogue position, the suggester's tie-break. Unknown ids sort last.</summary>
    public static int OrderOf(string? id)
    {
        if (string.IsNullOrEmpty(id)) return int.MaxValue;
        for (int i = 0; i < _all.Count; i++)
            if (string.Equals(_all[i].Id, id, StringComparison.Ordinal)) return i;
        return int.MaxValue;
    }

    /// <summary>Look a target up by id, or null.</summary>
    public static EmiTarget? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return _all.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
    }

    // ============================================================================
    // opens that did not come from the ring
    // ============================================================================
    //
    // The suggester ranks doors by how often they are OPENED, not by how often a card is
    // clicked, so the nav rail, the mosaic, the Ctrl+K palette, a hotkey and a bark link all
    // have to score too. Rather than scatter NoteOpen through the UI, the two chokepoints every
    // one of those paths already funnels through call in here.
    //
    // The maps are deliberately partial. A tab with no card in the catalogue scores nothing
    // (Home, Quests, Achievements, the Studio itself), and three keys are missing on purpose:
    // "fyp" and "justdrop" never reach the bottom of ShowTab (they are intercepted into their
    // own launchers, which count there), while "gradedintake" is the quiz LANDING page whose
    // start button calls IntakeHostService.Launch - counting both would score one sitting twice.
    // "spiral" as a TAB key is the Spiral Room, a different thing from the spiral OVERLAY card.

    private static readonly Dictionary<string, string> _tabTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["presets"] = "sessions",
        ["companion"] = "companion",
        ["awareness"] = "awareness",
        ["remotecontrol"] = "remote",
        ["bambitakeover"] = "takeover",
        ["lockdown"] = "lockdown",
        // Polish 12 (2026-10-07): "exclusives" redirects before the counter, "premium" is the page.
        ["premium"] = "vault",
        ["discord"] = "profile",
        ["appsettings"] = "settings",
        ["progression"] = "progression",
    };

    private static readonly Dictionary<string, string> _rackTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flash"] = "flashes",
        ["video"] = "videos",
        ["subliminal"] = "subliminals",
        ["bubbles"] = "bubbles",
        ["spiral"] = "loom",          // the Studio's Spiral module IS the Loom editor
        ["pinkfilter"] = "pinkfilter",
        ["braindrain"] = "braindrain",
        ["mindwipe"] = "mindwipe",
    };

    /// <summary>A tab was navigated to by any route. Score the card that points at it, if any.</summary>
    public static void NoteTabOpened(string? tabKey)
    {
        if (string.IsNullOrEmpty(tabKey)) return;
        try
        {
            if (_tabTargets.TryGetValue(tabKey, out var id)) App.EmiDesk?.NoteOpen(id);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] tab open not counted for {Tab}", tabKey);
        }
    }

    /// <summary>A Studio rack module was opened. Score the module, never the Studio.</summary>
    public static void NoteRackOpened(string? rackKey)
    {
        if (string.IsNullOrEmpty(rackKey)) return;
        try
        {
            if (_rackTargets.TryGetValue(rackKey, out var id)) App.EmiDesk?.NoteOpen(id);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] rack open not counted for {Rack}", rackKey);
        }
    }

    // Art, hue and order live in Core's EmiDoors table; this is the WPF half: is the door there,
    // does the tier gate refuse it, and how it opens. Every open goes through Pick.
    private static List<EmiTarget> Build() => EmiDoors.Build(id =>
        Door(id) is { } d ? (d.Available, d.Locked, () => Pick(id, d.Locked, d.Open)) : null);

    private static (Func<bool> Available, Func<bool> Locked, Action Open) D(Func<bool> available, Func<bool> locked, Action open)
        => (available, locked, open);

    private static (Func<bool> Available, Func<bool> Locked, Action Open)? Door(string id) => id switch
    {
        // The door is a build flag AND a Lab gate, and Launch owns both plus the audio-only refusal.
        "arcademy" => D(() => Arcademy.ArcademyHostService.DoorAvailable,
            () => !LabOk("emi_desk_target_arcademy", null),
            () => Arcademy.ArcademyHostService.Launch()),

        // The ONE Loom entry is the Studio rack's Spiral module, never a second editor window.
        // PlayTabView.Cards.cs makes the same call for the same reason.
        "loom" => D(Always, Never, () => Rack("spiral")),

        // ShowTab("fyp") is intercepted into OpenFypFeed, which demands premium itself.
        "fyp" => D(Always, () => !PremiumOk("emi_desk_target_fyp", "fyp"), () => Mw?.OpenFypFeed()),

        "sessions" => D(Always, Never, () => Nav("presets")),
        "flashes" => D(Always, Never, () => Rack("flash")),

        // ALWAYS AVAILABLE, NEVER LOCKED, and both halves of that carry weight. The book IS the
        // manual: a tier gate on it would lock a first-run user out of the explanation of the
        // thing they cannot use yet. It is also the one door that opens with NOTHING on disk -
        // EmiCodex fails soft to a native reader.
        "codex" => D(Always, Never, () => EmiBook.Open()),

        "videos" => D(Always, Never, () => Rack("video")),
        "dtrh" => D(Always, () => !LabOk("emi_desk_target_dtrh", "dtrh"), () => Chaos.DtrhHostService.Launch()),
        // Same rule as the launcher tile: Prime, or a free account with this week's pass unspent.
        // Basic has no unlimited runs, so a refusal goes to the Prime offer (ShowGate).
        "intake" => D(Always, () => !(App.IntakePass?.CanStartIntake ?? false), () => Quiz.IntakeHostService.Launch()),
        "subliminals" => D(Always, Never, () => Rack("subliminal")),
        "bubbles" => D(Always, Never, () => Rack("bubbles")),

        // The one card that is not navigation: it fires the overlay where the user already is.
        "spiral" => D(Always, Never, FireSpiral),

        "pinkfilter" => D(Always, Never, () => Rack("pinkfilter")),
        "braindrain" => D(Always, Never, () => Rack("braindrain")),
        "mindwipe" => D(Always, Never, () => Rack("mindwipe")),
        "awareness" => D(Always, () => !PremiumOk("emi_desk_target_awareness", "awareness"), () => Nav("awareness")),
        "remote" => D(Always, () => !PremiumOk("emi_desk_target_remote", "remote"), () => Nav("remotecontrol")),
        "takeover" => D(Always, () => !PremiumOk("emi_desk_target_takeover", "takeover"), () => Nav("bambitakeover")),
        "lockdown" => D(Always, () => !PremiumOk("emi_desk_target_lockdown", null), () => Nav("lockdown")),
        "vault" => D(Always, Never, () => Nav("premium")),
        "goon" => D(Always, Never, () => GoonGame.GoonHostService.Launch()),

        // The Back Room: free for everyone, so never locked.
        "backroom" => D(Always, Never, () => BackRoom.BackRoomHostService.Launch()),

        // The shop is withheld on most accounts; ShowTab owns the refusal, IsAvailable keeps the
        // card out of the ring entirely rather than offering a door that answers with a log line.
        "justdrop" => D(() => JustDrop.JustDropService.DoorAvailable, Never, () => Nav("justdrop")),

        "companion" => D(Always, Never, () => Nav("companion")),
        "progression" => D(Always, Never, () => Nav("progression")),
        "profile" => D(Always, Never, () => Nav("discord")),
        "settings" => D(Always, Never, () => Nav("appsettings")),
        _ => null,
    };

    /// <summary>
    /// The spiral card does not navigate anywhere: it drops the spiral overlay on whatever the user
    /// is already looking at, for six seconds, at their configured opacity with a floor so the card
    /// never looks broken on a 0 percent setting.
    /// </summary>
    private static void FireSpiral()
    {
        try
        {
            double opacity = 0.35;
            try
            {
                int pct = App.Settings?.Current?.SpiralOpacity ?? 35;
                opacity = Math.Max(0.15, Math.Min(1.0, pct / 100.0));
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] spiral opacity read failed"); }
            App.Overlay?.ShowOverlayTimed("spiral", 6000, opacity);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] spiral card failed to fire");
        }
    }

    // ============================================================================
    // the pick
    // ============================================================================

    /// <summary>
    /// Everything a card click has to do, in the order the brief locks: the door opens FIRST, so her
    /// reaction rides the navigation instead of delaying it, and only then does the counter move and
    /// the moment fire.
    ///
    /// A locked card opens the app's own refusal instead and is NOT counted as an open: a padlock
    /// you bounced off is not a feature you use, and counting it would let the ring fill itself with
    /// doors you cannot walk through.
    /// </summary>
    private static void Pick(string id, Func<bool> lockedProbe, Action open)
    {
        bool locked;
        try { locked = lockedProbe(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] lock probe threw at pick for {Target}", id); locked = true; }

        try
        {
            if (locked) ShowGate(id);
            else open();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] target {Target} failed to open", id);
        }

        try
        {
            var desk = App.EmiDesk;
            if (desk == null) return;

            if (locked)
            {
                desk.Fire("lockedCardTapped", new { target = id });
                return;
            }

            desk.NoteOpen(id);
            bool top = EmiSuggester.TopSlotIs(id);
            desk.Fire(string.Equals(id, "arcademy", StringComparison.Ordinal) ? "arcademyFromRing" : "ringPick",
                new { target = id, pickIsTop = top });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] pick bookkeeping failed for {Target}", id);
        }
    }

    /// <summary>Show the app's normal refusal for a locked card. She never says the word.</summary>
    private static void ShowGate(string id)
    {
        string key = "emi_desk_target_" + id;
        switch (id)
        {
            case "arcademy": LabPrompt(key, null); break;
            case "dtrh": LabPrompt(key, "dtrh"); break;
            case "intake": LabPrompt(key, null); break;
            case "fyp": PremiumPrompt(key, "fyp"); break;
            case "awareness": PremiumPrompt(key, "awareness"); break;
            case "remote": PremiumPrompt(key, "remote"); break;
            case "takeover": PremiumPrompt(key, "takeover"); break;
            default: PremiumPrompt(key, null); break;
        }
    }
}
