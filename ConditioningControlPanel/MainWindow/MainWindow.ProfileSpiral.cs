using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE SPIRAL'S TWO PROFILE DOORS — the Trainer Card plate and the header
    /// bubble menu's row. Both are a <see cref="Controls.SpiralGlyph"/> plus a
    /// caption, both open the SPIRAL ROOM (<c>ShowTab("spiral")</c>), and both are
    /// dark until the server says otherwise.
    ///
    /// THEY ARE SECOND DOORS, NOT LAUNCHERS (2026-08-16). They used to open
    /// SpiralMapWindow; that window retired when the map became a tab, so these two
    /// now navigate to it like every other rail row does. Their gates are unchanged -
    /// what changed is only where the click lands, and the tab re-reads every gate on
    /// entry so a stale click cannot walk past the ceremony.
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
        /// "Day 12 · Downward" - the devotion day count and the stage's NAME, and
        /// nothing else. The day label reuses the existing localized "Day {0}" string
        /// rather than minting an English one.
        ///
        /// <para><b>The names are ruled now.</b> This used to read "Day 12 · III"
        /// because the stage names were an open owner decision with no loc keys, and
        /// inventing copy here would have been the client saying something the ceremony
        /// had not agreed to. The owner locked the seven on 2026-08-11, web has been
        /// shipping them since (cclabs-web src/lib/descent/stages.ts), and they are
        /// localized here as <c>descent_stage_N_name</c> - so the numeral is no longer
        /// the honest answer, it is only the smaller one. The rail badge keeps its
        /// numeral because a 40px circle has room for a glyph and not for a word;
        /// anywhere a word fits, the word wins.</para>
        /// </summary>
        private static string BuildSpiralSummary(DescentBlock block)
        {
            string day = string.Format(Loc.Get("programs_card_day"), block.DevotionDays);
            return day + " · " + DescentStageCopy.Name(block.Stage?.N ?? 0);
        }

        // ============================== the door ==============================

        /// <summary>
        /// The Spiral Room, from either surface. Forwarded to by DiscordTabView's plate handler and
        /// by the menu row's click.
        ///
        /// <para><b>It used to open a window.</b> <c>SpiralMapWindow</c> retired on 2026-08-16 when
        /// the map became a tab, and these two surfaces became SECOND DOORS into it rather than
        /// launchers of their own. Their visibility gates are unchanged (block present and not
        /// withheld); what changed is only where the click lands.</para>
        ///
        /// <para>No gate is re-tested here on purpose: the tab re-reads all of them on entry, so a
        /// stale click on a plate that should have retracted lands on the fog or the waiting room
        /// rather than on nothing happening at all.</para>
        /// </summary>
        internal void OpenSpiralMapFromProfile()
        {
            try { ShowTab(SpiralRoom.TabKey); }
            catch (Exception ex) { App.Logger?.Debug("OpenSpiralMapFromProfile: {E}", ex.Message); }
        }

        private void ProfileMenuSpiral_Click(object sender, RoutedEventArgs e)
        {
            // Same close path every other menu item uses, then the door.
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
            OpenSpiralMapFromProfile();
        }

        // ============================== the first light ==============================
        //
        // THE PLATE PULSE RETIRED, 2026-08-16. PlayFirstLightHighlight used to breathe the Trainer
        // Card's spiral plate for two beats and then open the map WINDOW. There is no window any
        // more: the reveal plays inside the Spiral Room itself (Views/Tabs/SpiralTabView.cs), which
        // means the sequence no longer walks the user to the profile tab to point at a plate before
        // taking them somewhere else. It takes them straight into the room and opens it in front of
        // them, which is what the owner asked for and one fewer hop to do it in.
        //
        // The plate and the menu row STAY. They keep their existing gates (block present, not
        // withheld) and they are second doors into the tab - see OpenSpiralMapFromProfile above.
        // What is gone is only the animation that used to introduce them.

    }
}
