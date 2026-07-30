using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Session feature lock" - while a session is running, the prescribed dose belongs to the
    /// session, not to the user. Both halves are locked: WHICH features run (the master on/off
    /// toggles) and HOW MUCH they run (the dosage dials - rates, counts, opacities, scales).
    ///
    /// FOUR RULES this file exists to enforce:
    ///
    /// 1. ANY SESSION, NOT JUST A PROGRAM. This used to be Training-Program-only, on the theory
    ///    that a preset session was the user's own tool and tuning it mid-run was their business.
    ///    That was wrong, and in a way that made the UI lie twice over:
    ///      - SessionEngine.UpdateRampingValues rewrites the ramped dials (flash opacity/freq,
    ///        bubble freq) about once a second, so a dial the user drags down snaps back within
    ///        a tick. The control was never really theirs; it just looked like it.
    ///      - SessionEngine.RestoreSettings restores the pre-session snapshot when the session
    ///        ends, so ANY edit made during a session is discarded wholesale at the end.
    ///    An editable-looking dial whose value is overwritten in one second and thrown away at
    ///    the end is worse than a greyed-out one. Greying it out is just telling the truth.
    ///    The reason string still names the program when there is one - it is better copy - but
    ///    the lock itself no longer cares how the session was started.
    ///
    /// 2. DERIVE, NEVER LATCH. IsSessionFeatureLockActive is a computed property read from live
    ///    engine state every time. Nothing here remembers "we locked" - so a crash, an abort, a
    ///    withdraw, a window close, or a StopSession that raised its events out of order cannot
    ///    strand the user with a permanently dead Dashboard. Every accessor fails OPEN (returns
    ///    "not locked") on any exception for the same reason. Note it keys off the ENGINE's own
    ///    liveness rather than the App.IsSessionRunning flag: the engine nulls CurrentSession at
    ///    the end of StopSession, so both halves of the test go false together on every exit
    ///    path, whereas a stuck global flag would have nothing to clear it.
    ///
    /// 3. NEVER A SAFETY CONTROL. Stop / panic / No-Panic / Strict Lock / Withdraw / exit /
    ///    audio volume are untouched here and must stay that way. Locking a way out is a much
    ///    worse bug than the one this fixes.
    ///
    /// 4. DOSAGE IS LOCKED, COMFORT IS NOT. The split is declared per-control in XAML via
    ///    <see cref="Features.SessionLock"/>.Owned, next to the control it describes, so a dial
    ///    added later cannot be silently forgotten by a list living over here. Volumes,
    ///    click-to-dismiss, hydra, the "solid mode" renderer choice and monitor placement stay
    ///    live for the whole session - they change how the same dose feels, not how large it is.
    ///
    /// The lock does NOT suppress the session's own writes. SessionEngine.ApplySessionSettings
    /// and UpdateRampingValues keep pushing the prescribed values into settings and the controls
    /// keep re-rendering them - a disabled CheckBox or Slider still updates when set
    /// programmatically. The user watches the ramp happen, they just cannot overrule it.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Marks the banner we inject into a feature popup so we can find it again.</summary>
        private const string SessionLockBannerTag = "__sessionFeatureLockBanner";

        /// <summary>
        /// The UserControl currently hosted by <see cref="_activeFeaturePopup"/>, tracked so
        /// the lock can be re-derived onto an already-open popup when a session starts or ends.
        /// </summary>
        private UserControl? _activeFeaturePopupContent;

        /// <summary>
        /// True while ANY session is live - program, preset or remote. Fully derived; see rules
        /// 1 and 2 above.
        /// </summary>
        internal bool IsSessionFeatureLockActive
        {
            get
            {
                try
                {
                    var engine = _sessionEngine;
                    return engine != null && engine.IsRunning && engine.CurrentSession != null;
                }
                catch
                {
                    // Fail open. A stuck lock is worse than a missing one.
                    return false;
                }
            }
        }

        /// <summary>
        /// True only for a Training Program session. Used purely to pick better copy for the
        /// reason string - the lock itself does not branch on this (rule 1).
        /// </summary>
        private bool IsProgramSessionActive
        {
            get
            {
                try
                {
                    var engine = _sessionEngine;
                    if (engine == null || !engine.IsRunning) return false;
                    return App.Programs?.IsProgramSession(engine.CurrentSession) == true;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// "First Week is running this - Day 3", or the session-generic line when a plain preset
        /// session is running. Never blank while the lock is active: a greyed-out control with no
        /// explanation reads as a bug.
        /// </summary>
        internal string SessionFeatureLockReason
        {
            get
            {
                try
                {
                    if (!IsProgramSessionActive) return Loc.Get("session_lock_reason");

                    var svc = App.Programs;
                    var title = svc?.ActiveProgram?.Title;
                    var day = svc?.Today?.DayIndex ?? 0;

                    if (!string.IsNullOrWhiteSpace(title) && day > 0)
                        return Loc.GetF("program_lock_reason", title!, day);
                    if (!string.IsNullOrWhiteSpace(title))
                        return Loc.GetF("program_lock_reason_no_day", title!);
                    return Loc.Get("program_lock_reason_generic");
                }
                catch
                {
                    return Loc.Get("session_lock_reason");
                }
            }
        }

        /// <summary>
        /// Last state actually painted, so the once-per-second heartbeat can skip redundant
        /// repaints. This is a render cache, NOT a latch: the state above is re-derived on
        /// every single call, and any caller that might be racing a real change (session
        /// start/stop, tab switch) passes force:true and repaints regardless.
        /// </summary>
        private bool? _sessionLockPainted;

        /// <summary>
        /// Re-derives the locked state and repaints every affected control. Safe to call as
        /// often as you like - it is idempotent and reads only in-memory state. Called from
        /// session start/stop, from the per-second session progress tick, and on every switch
        /// to the Dashboard, so no single missed event can leave the UI out of step.
        /// </summary>
        /// <param name="force">
        /// true (default) repaints even if the derived state matches what was last painted.
        /// The per-second heartbeat passes false purely to save work.
        /// </param>
        /// <remarks>
        /// Every section is isolated and the render cache is only updated once they have ALL run.
        /// Play-test found why that matters: a throw inside the legacy-dashboard section aborted the
        /// whole method, so the tabs and the open popup were never painted - and because the cache
        /// had already been set to the new state, the once-per-second heartbeat's
        /// "same as last paint, skip" check meant it never retried. One exception in a vestigial
        /// hidden container left real dials unlocked for the entire session. A partial paint must
        /// degrade to "retry next tick", never to "silently give up".
        /// </remarks>
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
                App.Logger?.Warning(ex, "[SessionLock] Could not derive lock state");
                return;
            }

            var allOk = true;
            allOk &= PaintSection("ribbon", () => ApplySessionLockRibbon(locked, reason));
            allOk &= PaintSection("cards", () => ApplySessionLockToFeatureCards(locked, reason));
            allOk &= PaintSection("legacy", () => ApplySessionLockToLegacyDashboard(locked, reason));
            allOk &= PaintSection("tabs", () => ApplySessionLockToTabs(locked, reason));
            allOk &= PaintSection("popup", () => ApplySessionLockToFeaturePopup(_activeFeaturePopupContent));

            // Only remember this paint if it fully succeeded, so the heartbeat retries a partial one.
            _sessionLockPainted = allOk ? locked : null;
        }

        private static bool PaintSection(string name, Action paint)
        {
            try
            {
                paint();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[SessionLock] Paint section '{Section}' failed", name);
                return false;
            }
        }

        /// <summary>
        /// The "why". A pink pill across the top of the dashboard mosaic naming the program
        /// and the day (or just "you're in a session" for a plain preset session).
        ///
        /// It is an OVERLAY (RowSpan/ColumnSpan over the whole mosaic, VerticalAlignment=Top,
        /// IsHitTestVisible=False), so it adds exactly zero height to the fixed 1489x901
        /// design canvas and cannot push anything past WPF's silent layout clip. It also does
        /// not swallow clicks - the tiles underneath still open their config popups, because
        /// being locked out of changing the dose is not a reason to be locked out of READING it.
        /// </summary>
        private void ApplySessionLockRibbon(bool locked, string? reason)
        {
            var dash = SettingsTab;
            if (dash?.ProgramFeatureLockRibbon == null) return;

            if (!locked)
            {
                dash.ProgramFeatureLockRibbon.Visibility = Visibility.Collapsed;
                return;
            }

            if (dash.TxtProgramFeatureLock != null)
                dash.TxtProgramFeatureLock.Text = reason ?? Loc.Get("session_lock_reason");
            dash.ProgramFeatureLockRibbon.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Dashboard mosaic tiles. Left-click still opens the tile's popup; only the
        /// right-click quick-toggle is refused (see OnFeatureCardToggleRequested). The
        /// tooltip carries the reason so a refused right-click is never mysterious.
        ///
        /// FeatureCard.IsLocked is deliberately NOT used: it drops the tile to 35% opacity
        /// and suppresses the pink "this feature is on" ring, which would hide exactly the
        /// information the user needs during a session.
        /// </summary>
        private void ApplySessionLockToFeatureCards(bool locked, string? reason)
        {
            var dash = SettingsTab;
            if (dash == null) return;

            SetCardLockTooltip(dash.CardFlash, locked, reason);
            SetCardLockTooltip(dash.CardVisuals, locked, reason);
            SetCardLockTooltip(dash.CardVideo, locked, reason);
            SetCardLockTooltip(dash.CardSubliminal, locked, reason);
            SetCardLockTooltip(dash.CardSpiral, locked, reason);
            SetCardLockTooltip(dash.CardPinkFilter, locked, reason);
            SetCardLockTooltip(dash.CardBubblePop, locked, reason);
            SetCardLockTooltip(dash.CardLockCard, locked, reason);
            SetCardLockTooltip(dash.CardBubbleCount, locked, reason);
            SetCardLockTooltip(dash.CardBouncingText, locked, reason);
            SetCardLockTooltip(dash.CardMindWipe, locked, reason);
            // CardSystem is intentionally absent: the System popup owns No-Panic, offline
            // mode and the monitor layout - configuration, not the prescribed dose, and
            // No-Panic is a safety control.
        }

        private static void SetCardLockTooltip(Features.FeatureCard? card, bool locked, string? reason)
        {
            if (card == null) return;
            // Save/restore rather than a bare ClearValue - see SessionLock.ApplyLockToolTip.
            Features.SessionLock.ApplyLockToolTip(card, locked, reason);
        }

        /// <summary>
        /// The pre-mosaic dashboard checkboxes. They live inside the Collapsed
        /// SettingsTab.LegacyDashboardHost and are not reachable today, but MainWindow still
        /// reads and re-syncs them, so they are locked for consistency: if that host is ever
        /// shown again it must not become a bypass.
        ///
        /// ChkHydraLinked, ChkClickable, ChkCorruption (hydra mode) and ChkFlashGlow (cosmetic)
        /// are deliberately absent - comfort, not dosage (rule 4). The pre-mosaic version of
        /// this list did lock the last two, which contradicted hydra-stays-editable; the mosaic
        /// popups treat them as comfort, and these two paths must not disagree.
        /// </summary>
        private void ApplySessionLockToLegacyDashboard(bool locked, string? reason)
        {
            var dash = SettingsTab;
            if (dash == null) return;

            SetToggleLock(dash.ChkFlashEnabled, locked, reason);
            SetToggleLock(dash.ChkVideoEnabled, locked, reason);
            SetToggleLock(dash.ChkSubliminalEnabled, locked, reason);
            SetToggleLock(dash.ChkMiniGameEnabled, locked, reason);
            SetToggleLock(dash.ChkAudioWhispers, locked, reason);

            // The dosage SLIDERS in the same collapsed host. Previously only the checkboxes above
            // were painted, which left this method half-doing its stated job: if the legacy host
            // is ever shown again, an unlocked rate slider is just as much a bypass as an
            // unlocked enable.
            SetToggleLock(dash.SliderPerMin, locked, reason);
            SetToggleLock(dash.SliderImages, locked, reason);
            SetToggleLock(dash.SliderSize, locked, reason);
            SetToggleLock(dash.SliderOpacity, locked, reason);
            SetToggleLock(dash.SliderPerHour, locked, reason);
            SetToggleLock(dash.SliderSubPerMin, locked, reason);
            SetToggleLock(dash.SliderFrames, locked, reason);
            SetToggleLock(dash.SliderSubOpacity, locked, reason);
        }

        /// <summary>
        /// Dials that live on ordinary TABS rather than in a dashboard feature popup.
        ///
        /// The dashboard mosaic is not the only door to the prescribed dose:
        ///  - Graded Intake owns Pop Quiz, which is a real SessionSettings field the engine
        ///    snapshots and restores - so editing it mid-session both took effect AND was
        ///    silently discarded at the end, the exact double lie rule 1 describes.
        ///  - Awareness and Haptics have master toggles that the premium-rail chips already
        ///    refuse to flip mid-session. Leaving the tab checkboxes live made that refusal
        ///    decorative: it was one tab-click away from meaningless.
        ///
        /// These tabs are constructed at load and merely Collapsed, so their controls exist and
        /// can be painted whether or not the tab is currently on screen - which is why this is
        /// driven off the session start/stop paint rather than a tab-show hook.
        ///
        /// Marked in XAML exactly like the popups (features:SessionLock.Owned), so the same
        /// sweep finds them and the same one rule governs the whole app.
        /// </summary>
        private void ApplySessionLockToTabs(bool locked, string? reason)
        {
            // ProgressionTab is a complete second copy of the dose dials with live handlers,
            // unreachable today only because MainWindow.xaml declares it Collapsed and no ShowTab
            // case reveals it. Included so that un-collapsing it can never silently reopen the
            // bypass - the markers and the sweep have to land together or they are both inert.
            foreach (var tab in new UserControl?[]
                     { GradedIntakeTab, AwarenessTab, HapticsTab, ProgressionTab })
            {
                if (tab == null) continue;
                foreach (var owned in Features.SessionLock.FindOwnedControls(tab))
                    SetToggleLock(owned, locked, reason);
            }
        }

        /// <summary>
        /// A locked feature popup: the master enable toggle and every dial the session
        /// prescribes grey out, and a banner names the reason above them. Controls that only
        /// affect comfort stay live - see rule 4; the split is declared in each
        /// Features/*FeatureControl.xaml via features:SessionLock.Owned.
        ///
        /// Both directions are applied, so a session that ends while the popup is open
        /// re-enables everything in place. No latch.
        /// </summary>
        private void ApplySessionLockToFeaturePopup(UserControl? content)
        {
            if (content == null) return;

            try
            {
                var locked = IsSessionFeatureLockActive;
                var reason = locked ? SessionFeatureLockReason : null;

                // Uniform convention across every Features/*FeatureControl.xaml: the master
                // on/off is named ChkEnable. FindName only searches the control's own
                // namescope, so this cannot reach a same-named control elsewhere in the app.
                // Kept alongside the attached-property sweep below as a belt-and-braces
                // default: a popup whose author forgot to mark anything still locks its
                // master switch. SetToggleLock is idempotent, so double-covering is free.
                if (content.FindName("ChkEnable") is CheckBox chk)
                    SetToggleLock(chk, locked, reason);

                // The dosage dials, declared per-control in XAML.
                foreach (var owned in Features.SessionLock.FindOwnedControls(content))
                    SetToggleLock(owned, locked, reason);

                if (content.Content is Panel root)
                {
                    var existing = FindSessionLockBanner(root);
                    if (locked && existing == null)
                        root.Children.Insert(0, BuildSessionLockBanner(reason));
                    else if (locked && existing != null)
                        UpdateSessionLockBanner(existing, reason);
                    else if (!locked && existing != null)
                        root.Children.Remove(existing);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[SessionLock] Failed to apply lock to feature popup");
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
        /// Built in code rather than XAML because the popup content controls live in
        /// Features/ and are shared by every tile; injecting one banner here beats editing
        /// fourteen files. Note the strings are resolved at BUILD time, which means a
        /// language change while a popup is open leaves this banner stale - the popup is
        /// recreated on every open, so it self-corrects the moment it is reopened.
        /// </summary>
        private static Border BuildSessionLockBanner(string? reason)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            // WPF cannot draw COLR/CPAL colour-emoji fonts, so every emoji in this app goes
            // through EmojiTextBlock / EmojiToImageSource.
            stack.Children.Add(new Helpers.EmojiTextBlock
            {
                Text = "🔒",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var text = new TextBlock
            {
                Tag = SessionLockBannerTag,
                Text = reason ?? Loc.Get("session_lock_reason"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4))
            };
            stack.Children.Add(text);

            return new Border
            {
                Tag = SessionLockBannerTag,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = stack
            };
        }

        /// <summary>
        /// Greys a control and gives it the reason as its tooltip.
        ///
        /// The tooltip is applied through SessionLock.ApplyLockToolTip, which both sets
        /// ToolTipService.ShowOnDisabled (WPF hides tooltips on disabled controls, so without it
        /// the explanation this whole feature is built around never appears - that was the state
        /// of the shipped program lock) and preserves any tooltip the control already had.
        ///
        /// ClearValue on unlock rather than "= true": the control may be disabled for some
        /// OTHER reason (entitlement gating disables whole containers), and hard-writing true
        /// would silently promote the user past that gate.
        /// </summary>
        private static void SetToggleLock(Control? control, bool locked, string? reason)
        {
            if (control == null) return;
            if (locked) control.IsEnabled = false;
            else control.ClearValue(UIElement.IsEnabledProperty);

            Features.SessionLock.ApplyLockToolTip(control, locked, reason);
        }

        /// <summary>
        /// Call from any handler that is about to change the prescribed dose. Returns true
        /// when the change must be refused; also flashes the ribbon so the refusal is visible
        /// rather than a dead click.
        /// </summary>
        internal bool RefuseIfSessionFeatureLocked(string what)
        {
            if (!IsSessionFeatureLockActive) return false;

            App.Logger?.Information("[SessionLock] Refused feature change '{What}' - {Reason}",
                what, SessionFeatureLockReason);
            PulseSessionLockRibbon();
            return true;
        }

        /// <summary>
        /// Refuses a whole ACTION (as opposed to a single dial) that would overwrite the
        /// prescribed dose wholesale, and explains why in a dialog.
        ///
        /// Greying is the right answer for a dial the user is staring at on the dashboard. It is
        /// the wrong answer for these: "Remember", "Jump right in", "Load preset" and "Restore
        /// from cloud" each rewrite ~40 prescribed fields in one click, and they are triggered
        /// from headers and other tabs where the dashboard's explanation ribbon is not even on
        /// screen. A silently dead button there reads as a broken app, so this says it out loud.
        ///
        /// Not a safety control: none of these is a way out of a session. Stop, panic, No-Panic
        /// and Withdraw are all untouched (rule 3).
        /// </summary>
        /// <param name="what">Short identifier for the log line, e.g. "remember-recall".</param>
        internal bool RefuseActionIfSessionLocked(string what)
        {
            if (!IsSessionFeatureLockActive) return false;

            App.Logger?.Information("[SessionLock] Refused action '{What}' - {Reason}",
                what, SessionFeatureLockReason);
            PulseSessionLockRibbon();

            try
            {
                MessageBox.Show(
                    Loc.GetF("session_lock_action_body", SessionFeatureLockReason),
                    Loc.Get("session_lock_action_title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { /* never let the explanation itself break the refusal */ }

            return true;
        }

        /// <summary>
        /// Draws the eye to the "why" pill after a refused click. Purely cosmetic - wrapped
        /// because an animation on a control that is mid-teardown must never take the app down.
        /// </summary>
        private void PulseSessionLockRibbon()
        {
            try
            {
                var ribbon = SettingsTab?.ProgramFeatureLockRibbon;
                if (ribbon == null || ribbon.Visibility != Visibility.Visible) return;

                var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(560) };
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.25, KeyTime.FromPercent(0.25)));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.5)));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.25, KeyTime.FromPercent(0.75)));
                pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)));
                // FillBehavior.Stop + ClearValue leaves no animation clock holding Opacity
                // hostage afterwards.
                pulse.FillBehavior = FillBehavior.Stop;
                pulse.Completed += (_, _) =>
                {
                    try { ribbon.BeginAnimation(UIElement.OpacityProperty, null); } catch { }
                };
                ribbon.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            catch { /* cosmetic only */ }
        }
    }
}
