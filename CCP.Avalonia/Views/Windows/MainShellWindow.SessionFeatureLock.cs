// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SessionFeatureLock.cs (531 lines).
//
// "Session feature lock" - while a session is running, the prescribed dose belongs to the
// session, not to the user. Read the WPF file's class remarks for the four rules; they are the
// design, and none of them changed in the port. The two that shape THIS file:
//
//   DERIVE, NEVER LATCH. IsSessionFeatureLockActive is computed from live engine state on every
//   read, and every accessor fails OPEN on any exception. Nothing here remembers "we locked", so
//   a crash or an out-of-order stop cannot strand the user with a permanently dead UI.
//
//   NEVER A SAFETY CONTROL. Stop / panic / No-Panic / Strict Lock / Withdraw / exit / volume are
//   untouched, and must stay that way.
//
// WHAT IS REAL HERE. The lock's whole derivation and both refusal gates, plus the paint on the
// two surfaces this head carries:
//   - IsSessionFeatureLockActive reads CoreSession.IsEngineRunning. That seam is the exact
//     mapping: WPF asks _sessionEngine.IsRunning && CurrentSession != null, and the seam's
//     unseeded answer (false = not running) is the truth for a head with no engine, so the lock
//     is simply never active here rather than being fake-active.
//   - The ribbon (SettingsTabView.ProgramFeatureLockRibbon / TxtProgramFeatureLock, both in the
//     ported XAML) shows and hides with the derived state and carries the reason.
//   - The Studio rack's feature panels (StudioTabView.HostedFeaturePanels) get the master
//     ChkEnable greyed, the reason as a tooltip, and the lock banner inserted above them - the
//     same painter WPF uses for the dashboard popups, in both directions, idempotent, banner
//     found by Tag before insert so repaints cannot stack it.
//
// WHAT IS NOT, and why - not one of these is "wired when a service moves to Core":
//   - The per-dial marker sweep. WPF marks each dosage dial in XAML with
//     features:SessionLock.Owned and finds them with Features.SessionLock.FindOwnedControls.
//     That attached property was DROPPED in the port (every ported Features/*FeatureControl.axaml
//     says so in its header, e.g. CCP.Avalonia/Views/Features/FlashFeatureControl.axaml:11), so
//     there is nothing to sweep. CONSEQUENCE, stated plainly: on this head a running session
//     greys the master enable and shows the banner, but the individual dose sliders under it stay
//     draggable. Restoring the full lock needs the attached property ported first - it belongs in
//     CCP.Avalonia/Features/SessionLock.cs, which does not exist.
//   - ApplySessionLockToTabs, for the same reason plus a second one: it sweeps GradedIntakeTab,
//     AwarenessTab and HapticsTab by marker only. Those three views exist here
//     (CCP.Avalonia/Views/Tabs/{GradedIntake,Awareness,Haptics}TabView.axaml) and are reachable
//     through Named<T>, so this method is one line away the moment the marker lands.
//   - ApplySessionLockToFeaturePopup's OTHER caller: _activeFeaturePopupContent, the dashboard
//     tile popup. The popup host is MainShellWindow.TakeoverUi.cs / FeatureSettingsPopup, still a
//     stub, so nothing sets that field and the painter is called for the rack only.
//   - IsProgramSessionActive and the program half of SessionFeatureLockReason ("First Week is
//     running this - Day 3") need App.Programs (ConditioningControlPanel/Services/
//     ProgramService.cs) for ActiveProgram.Title and Today.DayIndex. The session-generic reason
//     is what every caller gets here, which is the same string WPF falls back to.
//   - PulseSessionLockRibbon is a WPF DoubleAnimationUsingKeyFrames flashing the ribbon after a
//     refused click. Avalonia has no BeginAnimation and a five-keyframe opacity strobe is not
//     worth a hand-rolled Transitions dance - the refusal is still logged and, for the action
//     form, still explained in a dialog. Cosmetic only; named so it is not lost silently.
//   - SetToggleLock's ToolTipService.ShowOnDisabled has no Avalonia twin to need: Avalonia shows
//     a ToolTip on a disabled control already, which is the whole reason WPF needed the flag.
//
// RefuseActionIfSessionLocked's MessageBox becomes Views/Dialogs/MessageDialog, which is async
// and needs an owner. The refusal itself is synchronous and returns immediately - the dialog is
// posted and awaited on its own, exactly as WPF's modal is "after the refusal" in effect.

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Marks the banner we inject into a feature panel so we can find it again.</summary>
        private const string SessionLockBannerTag = "__sessionFeatureLockBanner";

        private static readonly Color LockPink = Color.FromRgb(0xFF, 0x69, 0xB4);

        /// <summary>The Dashboard, resolved the only way a partial of this window may - see the
        /// header of MainShellWindow.TabNavigation.cs.</summary>
        internal Tabs.SettingsTabView? SettingsPage => Named<Tabs.SettingsTabView>("SettingsTab");

        /// <summary>
        /// True while ANY session is live - program, preset or remote. Fully derived from
        /// <see cref="CoreSession.IsEngineRunning"/>; nothing here latches, and a fault answers
        /// "not locked" because a stuck lock is worse than a missing one.
        /// </summary>
        internal bool IsSessionFeatureLockActive
        {
            get
            {
                try { return CoreSession.IsEngineRunning; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Why the dials are grey. Never blank while the lock is active: a greyed-out control
        /// with no explanation reads as a bug.
        /// <para>ponytail: the program-specific line ("First Week is running this - Day 3") needs
        /// App.Programs; this head always gives the session-generic reason, which is WPF's own
        /// fallback string.</para>
        /// </summary>
        internal string SessionFeatureLockReason
        {
            get
            {
                try { return Loc.Get("session_lock_reason"); }
                catch { return "session_lock_reason"; }
            }
        }

        /// <summary>
        /// Last state actually painted, so a heartbeat can skip redundant repaints. A render
        /// cache, NOT a latch - and deliberately left null after a partial paint so the next tick
        /// retries. Play-testing on WPF found why: one throw in one section, plus a cache already
        /// updated, left real dials unlocked for a whole session.
        /// </summary>
        private bool? _sessionLockPainted;

        /// <summary>
        /// Re-derives the locked state and repaints every affected surface. Idempotent and
        /// in-memory only, so it is safe to call as often as you like.
        /// </summary>
        /// <param name="force">true (default) repaints even when the state matches the last
        /// paint. A per-second heartbeat passes false purely to save work.</param>
        internal void RefreshSessionFeatureLock(bool force = true)
        {
            bool locked;
            string? reason;
            try
            {
                locked = IsSessionFeatureLockActive;
                if (!force && _sessionLockPainted == locked) return;
                reason = locked ? SessionFeatureLockReason : null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SessionLock] Could not derive lock state");
                return;
            }

            var allOk = true;
            allOk &= PaintSection("ribbon", () => ApplySessionLockRibbon(locked, reason));
            allOk &= PaintSection("studio", ApplySessionLockToStudioRack);

            // Only remember this paint if it fully succeeded, so a partial one is retried.
            _sessionLockPainted = allOk ? locked : null;
        }

        private static bool PaintSection(string name, Action paint)
        {
            try { paint(); return true; }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SessionLock] Paint section '{Section}' failed", name);
                return false;
            }
        }

        /// <summary>
        /// The "why". A pink pill across the top of the dashboard naming the reason. It is an
        /// overlay in the XAML (RowSpan/ColumnSpan, Top-aligned, IsHitTestVisible=False), so it
        /// adds no height and swallows no clicks: being locked out of CHANGING the dose is not a
        /// reason to be locked out of reading it.
        /// </summary>
        private void ApplySessionLockRibbon(bool locked, string? reason)
        {
            var dash = SettingsPage;
            var ribbon = dash?.ProgramFeatureLockRibbon;
            if (ribbon is null) return;

            if (!locked) { ribbon.IsVisible = false; return; }

            if (dash!.TxtProgramFeatureLock is { } label)
                label.Text = reason ?? SessionFeatureLockReason;
            ribbon.IsVisible = true;
        }

        /// <summary>
        /// The Studio rack hosts the same Features/*FeatureControl panels the dashboard popups
        /// host, so it is a second door onto the prescribed dose and gets the identical painter.
        /// The rack's panels are long-lived (built once, toggled by IsVisible), which is exactly
        /// why this runs on session start/stop rather than on reveal.
        /// </summary>
        private void ApplySessionLockToStudioRack()
        {
            var rack = StudioRack;
            if (rack is null) return;

            foreach (var panel in rack.HostedFeaturePanels)
                ApplySessionLockToFeaturePopup(panel);
        }

        /// <summary>
        /// A locked feature panel: the master enable greys out and a banner names the reason
        /// above it. Both directions are applied, so a session that ends while the panel is on
        /// screen re-enables it in place. No latch.
        /// </summary>
        private void ApplySessionLockToFeaturePopup(UserControl? content)
        {
            if (content is null) return;

            try
            {
                var locked = IsSessionFeatureLockActive;
                var reason = locked ? SessionFeatureLockReason : null;

                // Uniform convention across every Features/*FeatureControl.axaml: the master
                // on/off is named ChkEnable. FindControl searches the control's own namescope
                // only, so this cannot reach a same-named control elsewhere in the app.
                SetToggleLock(content.FindControl<CheckBox>("ChkEnable"), locked, reason);

                if (content.Content is Panel root)
                {
                    var existing = FindSessionLockBanner(root);
                    if (locked && existing is null)
                        root.Children.Insert(0, BuildSessionLockBanner(reason));
                    else if (locked && existing is not null)
                        UpdateSessionLockBanner(existing, reason);
                    else if (!locked && existing is not null)
                        root.Children.Remove(existing);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SessionLock] Failed to apply lock to feature panel");
            }
        }

        private static Border? FindSessionLockBanner(Panel root)
        {
            foreach (var child in root.Children)
                if (child is Border b && (b.Tag as string) == SessionLockBannerTag) return b;
            return null;
        }

        private static void UpdateSessionLockBanner(Border banner, string? reason)
        {
            if (banner.Child is StackPanel sp)
                foreach (var child in sp.Children)
                    if (child is TextBlock tb && (tb.Tag as string) == SessionLockBannerTag)
                    {
                        tb.Text = reason ?? Loc.Get("session_lock_reason");
                        return;
                    }
        }

        /// <summary>
        /// Built in code rather than XAML because the panels live in Views/Features/ and are
        /// shared; injecting one banner here beats editing fourteen files. The strings resolve at
        /// BUILD time, so a language change while it is on screen leaves it stale until the next
        /// repaint - which every session start/stop performs.
        /// <para>The lock glyph is a plain TextBlock: Avalonia draws colour emoji natively, so
        /// WPF's EmojiTextBlock/EmojiToImageSource detour is not needed here.</para>
        /// </summary>
        private static Border BuildSessionLockBanner(string? reason)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            stack.Children.Add(new TextBlock
            {
                Text = "🔒",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new global::Avalonia.Thickness(0, 0, 8, 0),
            });

            stack.Children.Add(new TextBlock
            {
                Tag = SessionLockBannerTag,
                Text = reason ?? Loc.Get("session_lock_reason"),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(LockPink),
            });

            return new Border
            {
                Tag = SessionLockBannerTag,
                Background = new SolidColorBrush(Color.FromArgb(0x33, LockPink.R, LockPink.G, LockPink.B)),
                BorderBrush = new SolidColorBrush(LockPink),
                BorderThickness = new global::Avalonia.Thickness(1),
                CornerRadius = new global::Avalonia.CornerRadius(9),
                Padding = new global::Avalonia.Thickness(12, 8, 12, 8),
                Margin = new global::Avalonia.Thickness(0, 0, 0, 10),
                Child = stack,
            };
        }

        /// <summary>
        /// Greys a control and gives it the reason as its tooltip.
        /// <para>ClearValue on unlock rather than "= true": the control may be disabled for some
        /// OTHER reason (entitlement gating disables whole containers), and hard-writing true
        /// would silently promote the user past that gate.</para>
        /// </summary>
        private static void SetToggleLock(Control? control, bool locked, string? reason)
        {
            if (control is null) return;

            if (locked)
            {
                control.IsEnabled = false;
                ToolTip.SetTip(control, reason ?? Loc.Get("session_lock_reason"));
            }
            else
            {
                control.ClearValue(global::Avalonia.Input.InputElement.IsEnabledProperty);
                // ponytail: WPF's SessionLock.ApplyLockToolTip restores whatever tooltip the
                // control carried before the lock. Without the attached property there is no
                // saved original to restore, so unlocking clears the tip outright. A ported
                // Features/SessionLock.cs would take this over.
                ToolTip.SetTip(control, null);
            }
        }

        /// <summary>
        /// Call from any handler that is about to change the prescribed dose. Returns true when
        /// the change must be refused.
        /// <para>ponytail: WPF also flashes the ribbon (PulseSessionLockRibbon) so the refusal is
        /// visible rather than a dead click. See the header - no Avalonia twin yet.</para>
        /// </summary>
        internal bool RefuseIfSessionFeatureLocked(string what)
        {
            if (!IsSessionFeatureLockActive) return false;

            Log.Information("[SessionLock] Refused feature change '{What}' - {Reason}",
                what, SessionFeatureLockReason);
            return true;
        }

        /// <summary>
        /// Refuses a whole ACTION (as opposed to a single dial) that would overwrite the
        /// prescribed dose wholesale, and explains why in a dialog.
        ///
        /// <para>Greying is the right answer for a dial the user is staring at. It is the wrong
        /// answer for "Remember", "Jump right in", "Load preset" and "Restore from cloud": each
        /// rewrites ~40 prescribed fields in one click, from headers and tabs where the ribbon is
        /// not even on screen. A silently dead button there reads as a broken app.</para>
        ///
        /// <para>Not a safety control: none of these is a way out of a session.</para>
        /// </summary>
        /// <param name="what">Short identifier for the log line, e.g. "remember-recall".</param>
        internal bool RefuseActionIfSessionLocked(string what)
        {
            if (!IsSessionFeatureLockActive) return false;

            var reason = SessionFeatureLockReason;
            Log.Information("[SessionLock] Refused action '{What}' - {Reason}", what, reason);

            // Fire and forget: the refusal is synchronous and must return NOW. Awaiting a modal
            // here would invert WPF's order, where MessageBox.Show blocks after the decision is
            // already made.
            _ = ExplainRefusalAsync(reason);
            return true;
        }

        private async Task ExplainRefusalAsync(string reason)
        {
            try
            {
                await Dialogs.MessageDialog.ShowAsync(
                    this,
                    Loc.Get("session_lock_action_title"),
                    Loc.GetF("session_lock_action_body", reason));
            }
            catch (Exception ex)
            {
                // Never let the explanation itself break the refusal.
                Log.Debug(ex, "[SessionLock] Could not show the refusal dialog");
            }
        }
    }
}
