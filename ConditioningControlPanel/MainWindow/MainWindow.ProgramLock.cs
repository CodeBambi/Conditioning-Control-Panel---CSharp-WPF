using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Program feature lock" - while a Training Program session is running, the day's
    /// feature mix belongs to the program, not to the user. Flipping Flashes / Videos /
    /// Subliminals / minigames mid-session quietly wrecks the day the program built, so
    /// the user-facing on/off controls are locked for the duration.
    ///
    /// THREE RULES this file exists to enforce:
    ///
    /// 1. PROGRAM SESSIONS ONLY. A preset session (MainWindow.Presets.cs) or a remote
    ///    session (MainWindow.RemoteControl.cs) sets App.IsSessionRunning too, and those
    ///    leave the toggles alone. ProgramService.IsProgramSession is the discriminator -
    ///    the same one the Programs tab uses to claim a session as today's, so the two can
    ///    never disagree.
    ///
    /// 2. DERIVE, NEVER LATCH. IsProgramFeatureLockActive is a computed property read from
    ///    live engine state every time. Nothing here remembers "we locked" - so a crash, an
    ///    abort, a withdraw, a window close, or a StopSession that raised its events out of
    ///    order cannot strand the user with a permanently dead Dashboard. Every accessor
    ///    fails OPEN (returns "not locked") on any exception for the same reason.
    ///
    /// 3. NEVER A SAFETY CONTROL. Stop / panic / No-Panic / Strict Lock / Withdraw / exit /
    ///    audio volume are untouched here and must stay that way. Locking a way out is a
    ///    much worse bug than the one this fixes.
    ///
    /// The lock does NOT suppress the session's own writes. SessionEngine.ApplySessionSettings
    /// keeps pushing the prescribed values into settings and the checkboxes keep re-rendering
    /// them - a disabled CheckBox still updates when set programmatically. The user sees what
    /// the program chose, they just cannot overrule it.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Marks the banner we inject into a feature popup so we can find it again.</summary>
        private const string ProgramLockBannerTag = "__programFeatureLockBanner";

        /// <summary>
        /// The UserControl currently hosted by <see cref="_activeFeaturePopup"/>, tracked so
        /// the lock can be re-derived onto an already-open popup when a session starts or ends.
        /// </summary>
        private UserControl? _activeFeaturePopupContent;

        /// <summary>
        /// True while a Training Program session is live. Fully derived - see rule 2 above.
        ///
        /// Deliberately keyed off the ENGINE's own liveness rather than App.IsSessionRunning:
        /// the engine is the single source of truth for "a session is on screen right now",
        /// and CurrentSession is nulled at the end of StopSession, so both halves of this
        /// test go false together on every exit path.
        /// </summary>
        internal bool IsProgramFeatureLockActive
        {
            get
            {
                try
                {
                    var engine = _sessionEngine;
                    if (engine == null || !engine.IsRunning) return false;
                    return App.Programs?.IsProgramSession(engine.CurrentSession) == true;
                }
                catch
                {
                    // Fail open. A stuck lock is worse than a missing one.
                    return false;
                }
            }
        }

        /// <summary>
        /// "First Week is running this - Day 3". Never blank while the lock is active: falls
        /// back to the program title alone, then to a generic line, because a greyed-out
        /// toggle with no explanation reads as a bug.
        /// </summary>
        internal string ProgramFeatureLockReason
        {
            get
            {
                try
                {
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
                    return Loc.Get("program_lock_reason_generic");
                }
            }
        }

        /// <summary>
        /// Last state actually painted, so the once-per-second heartbeat can skip redundant
        /// repaints. This is a render cache, NOT a latch: the state above is re-derived on
        /// every single call, and any caller that might be racing a real change (session
        /// start/stop, tab switch) passes force:true and repaints regardless.
        /// </summary>
        private bool? _programLockPainted;

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
        internal void RefreshProgramFeatureLock(bool force = true)
        {
            try
            {
                var locked = IsProgramFeatureLockActive;
                if (!force && _programLockPainted == locked) return;
                _programLockPainted = locked;
                var reason = locked ? ProgramFeatureLockReason : null;

                ApplyProgramLockRibbon(locked, reason);
                ApplyProgramLockToFeatureCards(locked, reason);
                ApplyProgramLockToLegacyDashboard(locked, reason);
                ApplyProgramLockToFeaturePopup(_activeFeaturePopupContent);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ProgramLock] RefreshProgramFeatureLock failed");
            }
        }

        /// <summary>
        /// The "why". A pink pill across the top of the dashboard mosaic naming the program
        /// and the day.
        ///
        /// It is an OVERLAY (RowSpan/ColumnSpan over the whole mosaic, VerticalAlignment=Top,
        /// IsHitTestVisible=False), so it adds exactly zero height to the fixed 1489x901
        /// design canvas and cannot push anything past WPF's silent layout clip. It also does
        /// not swallow clicks - the tiles underneath still open their config popups, because
        /// being locked out of changing the mix is not a reason to be locked out of READING it.
        /// </summary>
        private void ApplyProgramLockRibbon(bool locked, string? reason)
        {
            var dash = SettingsTab;
            if (dash?.ProgramFeatureLockRibbon == null) return;

            if (!locked)
            {
                dash.ProgramFeatureLockRibbon.Visibility = Visibility.Collapsed;
                return;
            }

            if (dash.TxtProgramFeatureLock != null)
                dash.TxtProgramFeatureLock.Text = reason ?? Loc.Get("program_lock_reason_generic");
            dash.ProgramFeatureLockRibbon.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Dashboard mosaic tiles. Left-click still opens the tile's popup; only the
        /// right-click quick-toggle is refused (see OnFeatureCardToggleRequested). The
        /// tooltip carries the reason so a refused right-click is never mysterious.
        ///
        /// FeatureCard.IsLocked is deliberately NOT used: it drops the tile to 35% opacity
        /// and suppresses the pink "this feature is on" ring, which would hide exactly the
        /// information the user needs during a program session.
        /// </summary>
        private void ApplyProgramLockToFeatureCards(bool locked, string? reason)
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
            // mode and the monitor layout - configuration, not the day's feature mix, and
            // No-Panic is a safety control.
        }

        private static void SetCardLockTooltip(Features.FeatureCard? card, bool locked, string? reason)
        {
            if (card == null) return;
            if (locked) card.ToolTip = reason;
            else card.ClearValue(FrameworkElement.ToolTipProperty);
        }

        /// <summary>
        /// The pre-mosaic dashboard checkboxes. They live inside the Collapsed
        /// SettingsTab.LegacyDashboardHost and are not reachable today, but MainWindow still
        /// reads and re-syncs them, so they are locked for consistency: if that host is ever
        /// shown again it must not become a bypass.
        /// </summary>
        private void ApplyProgramLockToLegacyDashboard(bool locked, string? reason)
        {
            var dash = SettingsTab;
            if (dash == null) return;

            SetToggleLock(dash.ChkFlashEnabled, locked, reason);
            SetToggleLock(dash.ChkVideoEnabled, locked, reason);
            SetToggleLock(dash.ChkSubliminalEnabled, locked, reason);
            SetToggleLock(dash.ChkMiniGameEnabled, locked, reason);
            SetToggleLock(dash.ChkAudioWhispers, locked, reason);
            SetToggleLock(dash.ChkClickable, locked, reason);
            SetToggleLock(dash.ChkCorruption, locked, reason);
            SetToggleLock(dash.ChkFlashGlow, locked, reason);
            SetToggleLock(dash.ChkHydraLinked, locked, reason);
        }

        /// <summary>
        /// A locked feature popup: the master enable toggle greys out and a banner names the
        /// program and day above it. Every other control in the popup stays live - the
        /// program prescribes WHICH features run, and tuning how a running feature feels is
        /// still the user's (and the ramp's) business.
        ///
        /// Both directions are applied, so a session that ends while the popup is open
        /// re-enables it in place. No latch.
        /// </summary>
        private void ApplyProgramLockToFeaturePopup(UserControl? content)
        {
            if (content == null) return;

            try
            {
                var locked = IsProgramFeatureLockActive;
                var reason = locked ? ProgramFeatureLockReason : null;

                // Uniform convention across every Features/*FeatureControl.xaml: the master
                // on/off is named ChkEnable. FindName only searches the control's own
                // namescope, so this cannot reach a same-named control elsewhere in the app.
                if (content.FindName("ChkEnable") is CheckBox chk)
                    SetToggleLock(chk, locked, reason);

                if (content.Content is Panel root)
                {
                    var existing = FindProgramLockBanner(root);
                    if (locked && existing == null)
                        root.Children.Insert(0, BuildProgramLockBanner(reason));
                    else if (locked && existing != null)
                        UpdateProgramLockBanner(existing, reason);
                    else if (!locked && existing != null)
                        root.Children.Remove(existing);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ProgramLock] Failed to apply lock to feature popup");
            }
        }

        private static Border? FindProgramLockBanner(Panel root)
        {
            foreach (var child in root.Children)
                if (child is Border b && (b.Tag as string) == ProgramLockBannerTag) return b;
            return null;
        }

        private static void UpdateProgramLockBanner(Border banner, string? reason)
        {
            if (banner.Child is StackPanel sp)
                foreach (var child in sp.Children)
                    if (child is TextBlock tb && (tb.Tag as string) == ProgramLockBannerTag)
                    {
                        tb.Text = reason ?? Loc.Get("program_lock_reason_generic");
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
        private static Border BuildProgramLockBanner(string? reason)
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
                Tag = ProgramLockBannerTag,
                Text = reason ?? Loc.Get("program_lock_reason_generic"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4))
            };
            stack.Children.Add(text);

            return new Border
            {
                Tag = ProgramLockBannerTag,
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
        /// Greys a toggle and gives it the reason as its tooltip. ClearValue on unlock rather
        /// than "= true": the control may be disabled for some OTHER reason (entitlement
        /// gating disables whole containers), and hard-writing true would silently promote
        /// the user past that gate.
        /// </summary>
        private static void SetToggleLock(Control? control, bool locked, string? reason)
        {
            if (control == null) return;
            if (locked)
            {
                control.IsEnabled = false;
                control.ToolTip = reason;
            }
            else
            {
                control.ClearValue(UIElement.IsEnabledProperty);
                control.ClearValue(FrameworkElement.ToolTipProperty);
            }
        }

        /// <summary>
        /// Call from any handler that is about to change the day's feature mix. Returns true
        /// when the change must be refused; also flashes the ribbon so the refusal is visible
        /// rather than a dead click.
        /// </summary>
        internal bool RefuseIfProgramFeatureLocked(string what)
        {
            if (!IsProgramFeatureLockActive) return false;

            App.Logger?.Information("[ProgramLock] Refused feature change '{What}' - {Reason}",
                what, ProgramFeatureLockReason);
            PulseProgramLockRibbon();
            return true;
        }

        /// <summary>
        /// Draws the eye to the "why" pill after a refused click. Purely cosmetic - wrapped
        /// because an animation on a control that is mid-teardown must never take the app down.
        /// </summary>
        private void PulseProgramLockRibbon()
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
