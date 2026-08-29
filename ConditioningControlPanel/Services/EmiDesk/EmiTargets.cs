using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
/// hue tile instead".</param>
/// <param name="Hue">The flat tile colour behind the label when there is no art.</param>
/// <param name="IsAvailable">False HIDES the card completely (a dark door, a withheld shop). Not the
/// same as locked: unavailable means the feature is not part of this build or this account at all,
/// locked means it exists and the tier gate says no.</param>
/// <param name="IsLocked">True paints the padlock and routes the click to the tier gate prompt.</param>
/// <param name="Open">Opens the feature. Always goes through <c>Pick</c>, which owns the usage
/// counter and the moments.</param>
/// <param name="Gate">The premium rail's own enum value where one exists, for callers that want to
/// cross-reference the rail. Null for free doors and for Lab doors (the enum has no Lab members).</param>
public sealed record EmiTarget(
    string Id,
    string LabelKey,
    string? ThumbPath,
    Color Hue,
    Func<bool> IsAvailable,
    Func<bool> IsLocked,
    Action Open,
    PremiumFeature? Gate)
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

    private static Color Tile(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

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
        ["exclusives"] = "vault",
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

    private static List<EmiTarget> Build()
    {
        var list = new List<EmiTarget>();

        void T(string id, string? thumb, Color hue, Func<bool> available, Func<bool> locked,
               Action open, PremiumFeature? gate = null)
        {
            string key = "emi_desk_target_" + id;
            list.Add(new EmiTarget(id, key, thumb, hue, available, locked,
                () => Pick(id, locked, open), gate));
        }

        // ---- the six she shows a brand new user, in this order ------------------

        // The Arcademy has no dashboard art of its own yet, so it is a hue tile. The door is a
        // build flag AND a Lab gate, and Launch owns both plus the audio-only refusal.
        T("arcademy", null, Tile(0xFF, 0x69, 0xB4),
            () => Arcademy.ArcademyHostService.DoorAvailable,
            () => !LabOk("emi_desk_target_arcademy", null),
            () => Arcademy.ArcademyHostService.Launch());

        // The ONE Loom entry is the Studio rack's Spiral module, never a second editor window.
        // PlayTabView.Cards.cs makes the same call for the same reason.
        T("loom", "features/loom.png", Tile(0x6F, 0xD3, 0xFF), Always, Never, () => Rack("spiral"));

        // ShowTab("fyp") is intercepted into OpenFypFeed, which demands premium itself.
        T("fyp", "features/fyp.png", Tile(0xB9, 0x80, 0xFF), Always,
            () => !PremiumOk("emi_desk_target_fyp", "fyp"),
            () => Mw?.OpenFypFeed(), PremiumFeature.Fyp);

        T("sessions", "features/deeper.png", Tile(0x8C, 0x9E, 0xFF), Always, Never, () => Nav("presets"));
        T("flashes", "features/flash.png", Tile(0xFF, 0x8F, 0xA3), Always, Never, () => Rack("flash"));
        T("videos", "features/mandatory_videos.png", Tile(0x8B, 0x2C, 0x6A), Always, Never, () => Rack("video"));

        // ---- the rest of the doors ----------------------------------------------

        T("dtrh", "features/dtrh.png", Tile(0x8C, 0xF5, 0xC8), Always,
            () => !LabOk("emi_desk_target_dtrh", "dtrh"),
            () => Chaos.DtrhHostService.Launch());

        T("intake", "features/lab_quiz_hero.png", Tile(0xFF, 0xC6, 0x5C), Always,
            () => !PremiumOk("emi_desk_target_intake", null),
            () => Quiz.IntakeHostService.Launch(), PremiumFeature.GradedIntake);

        T("subliminals", "features/subliminal.png", Tile(0x7F, 0xE3, 0xFF), Always, Never, () => Rack("subliminal"));
        T("bubbles", "features/Bubble_pop.png", Tile(0xFF, 0xA8, 0xD8), Always, Never, () => Rack("bubbles"));

        // The one card that is not navigation: it fires the overlay where the user already is.
        T("spiral", "features/spiral_overlay.png", Tile(0xFF, 0x69, 0xB4), Always, Never, FireSpiral);

        T("pinkfilter", "features/Pink_filter.png", Tile(0xFF, 0x9E, 0xC4), Always, Never, () => Rack("pinkfilter"));
        T("braindrain", "features/brain_drain.png", Tile(0x9B, 0x7C, 0xE8), Always, Never, () => Rack("braindrain"));
        T("mindwipe", "features/Mind_Wipers.png", Tile(0x6E, 0x7B, 0xC8), Always, Never, () => Rack("mindwipe"));

        T("awareness", "features/awareness.png", Tile(0xFF, 0xC6, 0x5C), Always,
            () => !PremiumOk("emi_desk_target_awareness", "awareness"),
            () => Nav("awareness"), PremiumFeature.Awareness);

        T("remote", "features/remote_control.png", Tile(0x7F, 0xE3, 0xFF), Always,
            () => !PremiumOk("emi_desk_target_remote", "remote"),
            () => Nav("remotecontrol"), PremiumFeature.Remote);

        T("takeover", "features/takeover.png", Tile(0xE8, 0x5C, 0xA8), Always,
            () => !PremiumOk("emi_desk_target_takeover", "takeover"),
            () => Nav("bambitakeover"), PremiumFeature.Takeover);

        T("lockdown", "lockdown_icon.png", Tile(0xC8, 0x4B, 0x4B), Always,
            () => !PremiumOk("emi_desk_target_lockdown", null),
            () => Nav("lockdown"), PremiumFeature.Lockdown);

        T("vault", "features/vault.png", Tile(0xD8, 0xB4, 0x6A), Always, Never, () => Nav("exclusives"));

        T("goon", "features/goon_game.png", Tile(0x76, 0xC8, 0x93), Always, Never,
            () => GoonGame.GoonHostService.Launch());

        // The shop is withheld on most accounts; ShowTab owns the refusal, IsAvailable keeps the
        // card out of the ring entirely rather than offering a door that answers with a log line.
        T("justdrop", "features/justdrop.png", Tile(0xFF, 0xB3, 0x6B),
            () => JustDrop.JustDropService.DoorAvailable, Never, () => Nav("justdrop"));

        // ---- rooms with no card art: hue tiles ----------------------------------

        T("companion", null, Tile(0xB9, 0x80, 0xFF), Always, Never, () => Nav("companion"));
        T("progression", null, Tile(0x8C, 0xF5, 0xC8), Always, Never, () => Nav("progression"));
        T("profile", null, Tile(0x7F, 0xE3, 0xFF), Always, Never, () => Nav("discord"));
        T("settings", null, Tile(0x9A, 0x9A, 0xB8), Always, Never, () => Nav("appsettings"));

        return list;
    }

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
            case "fyp": PremiumPrompt(key, "fyp"); break;
            case "awareness": PremiumPrompt(key, "awareness"); break;
            case "remote": PremiumPrompt(key, "remote"); break;
            case "takeover": PremiumPrompt(key, "takeover"); break;
            default: PremiumPrompt(key, null); break;
        }
    }
}
