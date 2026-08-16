using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE SPIRAL'S TWO PROFILE DOORS — the Trainer Card plate and the header
    /// bubble menu's row. Both are a <see cref="Controls.SpiralGlyph"/> plus a
    /// caption, both open <see cref="SpiralMapWindow.ShowMap"/>, and both are dark
    /// until the server says otherwise.
    ///
    /// THE GATE IS BLOCK PRESENCE, PLUS THE MIGRATION WITHHOLD. These surfaces
    /// deliberately do NOT consult SpiralRailHost.FlagEnabled /
    /// AppSettings.DescentSpiralRailEnabled: that flag guards the nav rail's
    /// WebView2 miniature, which is a browser HWND in the middle of every tab
    /// transition and needs its own kill switch. A native 22-44px glyph has no such
    /// cost, so the rollout dial that already withholds the `descent` block from
    /// every account outside it IS the whole gate — no block, no plate, no row, and
    /// the surfaces measure exactly as they did before this file existed (the same
    /// safety property MainWindow.ProfileVat.cs carries).
    ///
    /// THE CARD DOOR HAS A SECOND GATE: it appears only on YOUR OWN card. The
    /// Trainer Card doubles as a profile VIEWER (search a name, the same plates get
    /// repainted with a stranger's level and rank), and a stranger's descent is not
    /// ours to draw - we do not have their block, so anything we drew there would be
    /// our own numbers under their name. <c>_profileViewingSelf</c>
    /// (MainWindow.ProfileCard.SetProfileViewingSelf, the same field that hides the
    /// second-person pin placeholders) is that test, and
    /// <see cref="RefreshProfileSpiralPlate"/> is called from the setter, so every
    /// paint path - own card, searched card, cleared card - lands here.
    ///
    /// The header row has no such gate: the header is always yours.
    ///
    /// THE WITHHOLD IS THE SECOND HALF OF THE GATE (CONTRACT-FUSE-0816 §2.4).
    /// Block presence alone was right until zero armed the auto-promote: from that
    /// night the sync that carries a veteran's migration OFFER also carries their
    /// first block, so presence-only would light these surfaces up beside a ceremony
    /// window that is still asking the question. DescentMigrationService.SpiralWithheld
    /// is that test, and it opens the instant a choice is committed - which is what the
    /// first-light reveal below is standing on when it runs a few seconds later.
    /// </summary>
    public partial class MainWindow
    {
        private bool _profileSpiralWired;

        /// <summary>
        /// Is the spiral being held back from this account? One reader for both surfaces, so
        /// the plate and the menu row cannot disagree about whether tonight's question has been
        /// answered. A missing migration service reads as "not withheld" - that is every install
        /// on today's server, and it is the state these surfaces shipped in.
        /// </summary>
        private static bool SpiralWithheld => App.DescentMigration?.SpiralWithheld == true;

        // ============================== wiring ==============================

        /// <summary>
        /// Subscribe to the block once. Idempotent and safe to call from any refresh
        /// path, which is how it gets installed no matter which surface is touched
        /// first. DescentService raises BlockChanged already marshalled to the UI
        /// thread (see MainWindow.ProfileVat.OnDescentBlockChanged).
        /// </summary>
        private void WireProfileSpiral()
        {
            if (_profileSpiralWired) return;
            _profileSpiralWired = true;
            try
            {
                if (App.Descent != null) App.Descent.BlockChanged += OnSpiralBlockChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("WireProfileSpiral: {E}", ex.Message); }
        }

        /// <summary>Symmetric teardown, from the profile bubble's window-close cleanup.
        /// DescentService outlives this window, so the hook must not.</summary>
        private void UnwireProfileSpiral()
        {
            if (!_profileSpiralWired) return;
            _profileSpiralWired = false;
            try
            {
                if (App.Descent != null) App.Descent.BlockChanged -= OnSpiralBlockChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("UnwireProfileSpiral: {E}", ex.Message); }
        }

        private void OnSpiralBlockChanged(object? sender, EventArgs e)
        {
            RefreshProfileSpiralPlate();
            RefreshProfileMenuSpiral();
        }

        /// <summary>
        /// Re-evaluate both glyphs' ambient breath. There is no app-wide motion-level
        /// event, so this rides the same choke point every other ambient loop does
        /// (MainWindow.UiUpdates.CmbMotionLevel_SelectionChanged).
        /// </summary>
        internal void RefreshSpiralGlyphMotion()
        {
            try
            {
                DiscordTab?.ProfileSpiralGlyph?.RefreshMotion();
                ProfileMenuSpiralGlyph?.RefreshMotion();
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshSpiralGlyphMotion: {E}", ex.Message); }
        }

        // ============================== the card plate ==============================

        /// <summary>
        /// Show or hide the Trainer Card's spiral plate and paint the glyph inside it.
        /// Both gates are re-tested every time: a block withdrawn mid-session takes the
        /// plate with it, and so does searching for somebody else.
        /// </summary>
        internal void RefreshProfileSpiralPlate()
        {
            WireProfileSpiral();
            var plate = DiscordTab?.ProfileSpiralPlate;
            if (plate == null) return;

            try
            {
                var block = App.Descent?.Current;
                bool show = block is not null && _profileViewingSelf && !SpiralWithheld;
                plate.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) return;

                DiscordTab?.ProfileSpiralGlyph?.Apply(block);
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshProfileSpiralPlate: {E}", ex.Message); }
        }

        // ============================== the menu row ==============================

        /// <summary>
        /// Show or hide the account menu's spiral row and paint its summary. Called
        /// from RefreshProfileMenu (MainWindow.ProfileBubble.cs), which is the menu's
        /// single paint choke point - it runs on open as well as on live XP ticks, so
        /// a block that changed while the popup was closed is picked up on the way in.
        /// </summary>
        internal void RefreshProfileMenuSpiral()
        {
            WireProfileSpiral();
            if (ProfileMenuSpiralRow == null) return;

            try
            {
                var block = App.Descent?.Current;
                bool show = block is not null && !SpiralWithheld;
                ProfileMenuSpiralRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show || block is null) return;

                ProfileMenuSpiralGlyph?.Apply(block);
                if (ProfileMenuSpiralSummary != null)
                    ProfileMenuSpiralSummary.Text = BuildSpiralSummary(block);
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshProfileMenuSpiral: {E}", ex.Message); }
        }

        /// <summary>
        /// "Day 12 · III" - the devotion day count and the stage numeral, and nothing
        /// else. The day label reuses the existing localized "Day {0}" string rather
        /// than minting an English one, and the stage stays a Roman numeral because
        /// the stage NAMES (Curious … Eternal) remain an open owner decision with no
        /// loc keys; inventing copy for them here is exactly what the rail refused to
        /// do (see SpiralRailHost.StageNumeral).
        /// </summary>
        private static string BuildSpiralSummary(DescentBlock block)
        {
            string day = string.Format(Loc.Get("programs_card_day"), block.DevotionDays);
            return day + " · " + Controls.SpiralRailHost.StageNumeral(block.Stage?.N ?? 0);
        }

        // ============================== the door ==============================

        /// <summary>The map window, from either surface. Forwarded to by
        /// DiscordTabView's plate handler and by the menu row's click.</summary>
        internal void OpenSpiralMapFromProfile()
        {
            try { SpiralMapWindow.ShowMap(); }
            catch (Exception ex) { App.Logger?.Debug("OpenSpiralMapFromProfile: {E}", ex.Message); }
        }

        private void ProfileMenuSpiral_Click(object sender, RoutedEventArgs e)
        {
            // Same close path every other menu item uses, then the door.
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
            OpenSpiralMapFromProfile();
        }

        // ============================== the first light ==============================

        /// <summary>How long the attention pulse runs before the map opens itself. Two beats of
        /// nine tenths of a second — long enough to be seen crossing the room, short enough that it
        /// is over before it becomes a thing being waited out.</summary>
        private const double FirstLightPulseHalfSeconds = 0.45;

        private const int FirstLightPulseBeats = 2;

        /// <summary>
        /// STEP TWO OF THE FIRST LIGHT (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16): the
        /// Trainer Card's spiral plate has just been unlocked by the commit, and this is the
        /// animation that points at it before the map opens itself.
        ///
        /// <para><b>Opacity and transform ONLY.</b> A scale breath on a RenderTransform and an
        /// opacity dip, both on the plate's own Border. Nothing here touches layout, no Effect is
        /// attached (an Effect's Color is a plain DP that cannot follow a mod switch, which is the
        /// 2026-08-13 sweep's bug class, and a blur pass on a card that is already animating is a
        /// cost with no picture behind it), and the plate's pink comes from the DynamicResource
        /// brushes it already wears — so the "glow" is the mod's own accent breathing rather than a
        /// colour this method invented.</para>
        ///
        /// <para><b>Reduced motion proceeds immediately</b> (contract §0): the plate is simply
        /// there, lit, and the map opens. The reveal is not skipped for anyone — the pulse is, and
        /// the pulse is the one part of the sequence that is pure ambience.</para>
        ///
        /// <para><b>The plate may not be there at all,</b> and that is a legal outcome rather than a
        /// failure: the descent block can still be in flight this soon after the commit, and the
        /// card can be showing a searched profile instead of your own. Either way the callback still
        /// runs — the map's own reveal is the part that matters, and it does not need the plate.</para>
        /// </summary>
        /// <param name="onComplete">Runs on the UI thread when the pulse is over (or at once when
        /// there is nothing to pulse). Never dropped: every path calls it exactly once.</param>
        internal void PlayFirstLightHighlight(Action onComplete)
        {
            var done = false;
            void Finish()
            {
                if (done) return;
                done = true;
                try { onComplete(); }
                catch (Exception ex) { App.Logger?.Debug("[Spiral] first-light continuation threw: {E}", ex.Message); }
            }

            try
            {
                // The withhold opened a moment ago and nothing has repainted since — this is what
                // makes the plate appear at all.
                RefreshProfileSpiralPlate();
                RefreshProfileMenuSpiral();

                var plate = DiscordTab?.ProfileSpiralPlate;
                if (plate == null || plate.Visibility != Visibility.Visible) { Finish(); return; }

                if (Services.MotionFx.Level != Models.MotionLevel.Full) { Finish(); return; }

                var scale = new ScaleTransform(1, 1);
                plate.RenderTransformOrigin = new Point(0.5, 0.5);
                plate.RenderTransform = scale;

                var span = TimeSpan.FromSeconds(FirstLightPulseHalfSeconds);
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

                var breathe = new DoubleAnimation(1.0, 1.10, span)
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(FirstLightPulseBeats),
                    EasingFunction = ease,
                };
                Timeline.SetDesiredFrameRate(breathe, 30);

                var glow = new DoubleAnimation(1.0, 0.45, span)
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(FirstLightPulseBeats),
                    EasingFunction = ease,
                };
                Timeline.SetDesiredFrameRate(glow, 30);

                // The opacity animation carries the completion because it is the one that must be
                // released either way: a plate left holding an animated Opacity would ignore every
                // later refresh that tried to set it.
                glow.Completed += (_, _) =>
                {
                    try
                    {
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        plate.BeginAnimation(OpacityProperty, null);
                        plate.Opacity = 1.0;
                        plate.RenderTransform = null;
                    }
                    catch (Exception ex) { App.Logger?.Debug("[Spiral] first-light pulse release: {E}", ex.Message); }

                    Finish();
                };

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
                plate.BeginAnimation(OpacityProperty, glow);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] first-light highlight failed: {E}", ex.Message);
                Finish();
            }
        }
    }
}
