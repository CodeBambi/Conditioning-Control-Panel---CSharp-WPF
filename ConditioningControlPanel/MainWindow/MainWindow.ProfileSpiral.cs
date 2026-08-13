using System;
using System.Windows;
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
    /// THE GATE IS BLOCK PRESENCE, AND ONLY BLOCK PRESENCE. These surfaces
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
    /// </summary>
    public partial class MainWindow
    {
        private bool _profileSpiralWired;

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
                bool show = block is not null && _profileViewingSelf;
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
                ProfileMenuSpiralRow.Visibility = block is not null ? Visibility.Visible : Visibility.Collapsed;
                if (block is null) return;

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
    }
}
