using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The ring's catalogue on this head: Core <see cref="EmiDoors"/> (ids, art, hues, order) with the
    /// openers this head has. WPF twin: ConditioningControlPanel/Services/EmiDesk/EmiTargets.cs, whose
    /// availability, lock probe and opener this mirrors door for door.
    ///
    /// <para>The game doors (arcademy, dtrh, goon, backroom) and the intake open through the launcher's own
    /// entry (MainShellWindow.LaunchCardGame), so its sign-in ask, leash gate and tier refusal answer; a
    /// locked one still opens that door, which refuses in its own words.</para>
    ///
    /// <para>ponytail: HIDDEN, not faked, until this head has their surface: fyp and justdrop (shell
    /// WindowKeys with no window here) and spiral (the timed overlay card, WPF ShowOverlayTimed). A null from <see cref="Door"/> keeps the card out of the ring the
    /// same way an unavailable door does. Also missing: the moments WPF's Pick fires
    /// (<c>ringPick</c>, <c>lockedCardTapped</c>) - there is no <c>App.EmiDesk.Fire</c> on this head.</para>
    /// </summary>
    internal static class EmiTargets
    {
        // Declared before All: static initialisers run in text order, and All's doors capture these.
        private static readonly Func<bool> Always = () => true;
        private static readonly Func<bool> Never = () => false;

        public static IReadOnlyList<EmiTarget> All { get; } = EmiDoors.Build(id =>
            Door(id) is { } d ? (d.Available, d.Locked, () => Pick(id, d.Locked, d.Open)) : null);

        public static EmiTarget? Find(string? id) =>
            All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

        private static (Func<bool> Available, Func<bool> Locked, Action Open) Free(Action open) => (Always, Never, open);

        /// <summary>A premium door: WPF's PremiumOk probe and PremiumPrompt refusal, over Core TierGate.</summary>
        private static (Func<bool> Available, Func<bool> Locked, Action Open) Premium(string id, string? daily, Action open)
        {
            string name() => Loc.Get("emi_desk_target_" + id);
            return (Always,
                () => !(daily == null ? TierGate.RequiresPremium(name()) : TierGate.RequiresPremium(name(), daily)).Allowed,
                open);
        }

        private static (Func<bool> Available, Func<bool> Locked, Action Open)? Door(string id) => id switch
        {
            "arcademy" => (() => ConditioningControlPanel.Services.Arcademy.ArcademyHostService.DoorAvailable, () => GameLocked(id), () => Game(id)),
            "dtrh" or "goon" or "backroom" or "intake" => (Always, () => GameLocked(id), () => Game(id)),
            "loom" => Free(() => Rack("spiral")),
            "sessions" => Free(() => Nav("presets")),
            "flashes" => Free(() => Rack("flash")),
            "codex" => Free(() => EmiDeskService.Instance.Window?.OpenBook()),
            "videos" => Free(() => Rack("video")),
            "subliminals" => Free(() => Rack("subliminal")),
            "bubbles" => Free(() => Rack("bubbles")),
            "pinkfilter" => Free(() => Rack("pinkfilter")),
            "braindrain" => Free(() => Rack("braindrain")),
            "mindwipe" => Free(() => Rack("mindwipe")),
            "awareness" => Premium(id, "awareness", () => Nav("awareness")),
            "remote" => Premium(id, "remote", () => Nav("remotecontrol")),
            "takeover" => Premium(id, "takeover", () => Nav("bambitakeover")),
            "lockdown" => Premium(id, null, () => Nav("lockdown")),
            "vault" => Free(() => Nav("exclusives")),
            "companion" => Free(() => Nav("companion")),
            "progression" => Free(() => Nav("progression")),
            "profile" => Free(() => Nav("discord")),
            "settings" => Free(() => Nav("appsettings")),
            _ => null,
        };

        /// <summary>Doors whose own entry raises the refusal (the launcher's gate), so a locked pick still opens it.</summary>
        internal static bool GateOwnsRefusal(string id) => id is "arcademy" or "dtrh" or "goon" or "backroom" or "intake";

        /// <summary>The launcher card's padlock probe (WPF LabOk / IntakePass.CanStartIntake).</summary>
        private static bool GameLocked(string id) =>
            ConditioningControlPanel.Services.Launcher.LauncherCards.Find(id) is { } card && LauncherWindow.LockedFor(card);

        /// <summary>Test seam: the game door (default: the panel card's launcher entry).</summary>
        internal static Func<string, bool> LaunchGame { get; set; } = id => Shell?.LaunchCardGame(id) == true;

        private static void Game(string id)
        {
            if (!LaunchGame(id)) Log.Debug("[EmiDesk] no game door for {Target} on this head", id);
        }

        private static MainShellWindow? Shell =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainShellWindow;

        private static void Nav(string tab)
        {
            var mw = Shell;
            if (mw == null) { Log.Debug("[EmiDesk] no main window, cannot show tab {Tab}", tab); return; }
            try { mw.ShowFromTray(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowFromTray failed"); }
            mw.ShowTab(tab);
        }

        private static void Rack(string rack)
        {
            var mw = Shell;
            if (mw == null) { Log.Debug("[EmiDesk] no main window, cannot open rack {Rack}", rack); return; }
            try { mw.ShowFromTray(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowFromTray failed"); }
            mw.OpenStudioModule(rack);
        }

        /// <summary>WPF EmiTargets.Pick: a locked door asks for the tier, an open one opens and scores.</summary>
        private static void Pick(string id, Func<bool> lockedProbe, Action open)
        {
            bool locked;
            try { locked = lockedProbe(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] lock probe threw at pick for {Target}", id); locked = true; }

            try
            {
                if (locked && GateOwnsRefusal(id)) { open(); return; }   // its own gate says why; never scored
                if (locked)
                {
                    var name = Loc.Get("emi_desk_target_" + id);
                    string? daily = id is "awareness" or "remote" or "takeover" ? id : null;
                    if (daily == null) TierGate.DemandPremium(name); else TierGate.DemandPremium(name, daily);
                    return;
                }
                open();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] target {Target} failed to open", id); }

            try { EmiState.NoteUsage(id); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pick bookkeeping failed for {Target}", id); }
        }
    }
}
