// Ported from ConditioningControlPanel/MainWindow/MainWindow.ProfileSpiral.cs (WPF 7.1.5).
//
// The Descent's two second doors into the Spiral Room: the plate on your own Trainer Card and the
// row in the account menu. Both show only while the server has shipped this account a descent block
// and the migration is not withholding the spiral; both paint a SpiralGlyph from that block.
//
// WireProfileSpiral (App startup, once DescentService is built) feeds the block provider and repaints both
// doors on BlockChanged. The withheld provider stays unset: the migration ceremony is retired (auto-restore),
// so nothing withholds the spiral on this head. With no block the doors stay hidden, as WPF hides them.
//
// Not ported: nothing calls RefreshSpiralGlyphMotion yet (SEAM(f-shell): the motion level change in Settings
// should, as WPF CmbMotionLevel_SelectionChanged does; SpiralGlyph already re-reads it on show/hide).

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF App.Descent?.Current. Null provider or null block = no spiral for this account.</summary>
        internal static Func<DescentBlock?>? ProfileSpiralBlock;

        /// <summary>WPF App.DescentMigration?.SpiralWithheld.</summary>
        internal static Func<bool>? ProfileSpiralWithheld;

        private static DescentBlock? SpiralBlock()
        {
            try { return ProfileSpiralBlock?.Invoke(); } catch { return null; }
        }

        /// <summary>A rule that throws withholds: the door with the fewest promises.</summary>
        private static bool SpiralWithheld()
        {
            try { return ProfileSpiralWithheld?.Invoke() == true; } catch { return true; }
        }

        /// <summary>The Spiral Room reads the same two answers the doors do.</summary>
        internal static bool HasSpiralBlock => SpiralBlock() is not null;
        internal static bool SpiralIsWithheld => SpiralWithheld();

        private static DescentService? _spiralWiredTo;

        /// <summary>WPF WireProfileSpiral: App.Descent's block feeds both doors, and a block that arrives or
        /// is withdrawn repaints them on the live shell. Once per service.</summary>
        internal static void WireProfileSpiral(DescentService? descent)
        {
            if (descent is null || ReferenceEquals(_spiralWiredTo, descent)) return;
            _spiralWiredTo = descent;
            ProfileSpiralBlock = () => descent.Current;
            descent.BlockChanged += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { Current?.OnSpiralBlockChanged(); }
                catch (Exception ex) { Log.Debug("OnSpiralBlockChanged: {E}", ex.Message); }
            });
        }

        /// <summary>WPF OnSpiralBlockChanged: a block that arrives or is withdrawn repaints both doors.</summary>
        internal void OnSpiralBlockChanged()
        {
            RefreshProfileSpiralPlate();
            RefreshProfileMenuSpiral();
        }

        /// <summary>WPF RefreshSpiralGlyphMotion: both glyphs re-read the motion level.</summary>
        internal void RefreshSpiralGlyphMotion()
        {
            try
            {
                ProfilePage?.FindControl<SpiralGlyph>("ProfileSpiralGlyph")?.RefreshMotion();
                Named<SpiralGlyph>("ProfileMenuSpiralGlyph")?.RefreshMotion();
            }
            catch (Exception ex) { Log.Debug("RefreshSpiralGlyphMotion: {E}", ex.Message); }
        }

        /// <summary>
        /// Show or hide the Trainer Card's spiral plate and paint the glyph inside it. Both gates are
        /// re-tested every time: a block withdrawn mid-session takes the plate with it, and so does
        /// searching for somebody else.
        /// </summary>
        internal void RefreshProfileSpiralPlate()
        {
            var page = ProfilePage;
            var plate = page?.FindControl<Border>("ProfileSpiralPlate");
            if (page is null || plate is null) return;
            try
            {
                var block = SpiralBlock();
                var show = block is not null && _profileViewingSelf && !SpiralWithheld();
                plate.IsVisible = show;
                if (show) page.FindControl<SpiralGlyph>("ProfileSpiralGlyph")?.Apply(block);
            }
            catch (Exception ex) { Log.Debug("RefreshProfileSpiralPlate: {E}", ex.Message); }
        }

        /// <summary>
        /// Show or hide the account menu's spiral row and paint its summary. Called when the menu
        /// opens, so a block that changed while the popup was closed is picked up on the way in.
        /// </summary>
        internal void RefreshProfileMenuSpiral()
        {
            var row = Named<Button>("ProfileMenuSpiralRow");
            if (row is null) return;
            try
            {
                var block = SpiralBlock();
                var show = block is not null && !SpiralWithheld();
                row.IsVisible = show;
                if (!show || block is null) return;

                Named<SpiralGlyph>("ProfileMenuSpiralGlyph")?.Apply(block);
                if (Named<TextBlock>("ProfileMenuSpiralSummary") is { } summary)
                    summary.Text = BuildSpiralSummary(block);
            }
            catch (Exception ex) { Log.Debug("RefreshProfileMenuSpiral: {E}", ex.Message); }
        }

        /// <summary>"Day 12 · Downward": the devotion day count and the stage's name, nothing else.</summary>
        internal static string BuildSpiralSummary(DescentBlock block)
        {
            var day = string.Format(Loc.Get("programs_card_day"), block.DevotionDays);
            return day + " · " + DescentStageCopy.Name(block.Stage?.N ?? 0);
        }

        /// <summary>The Spiral Room, from either door. No gate is re-tested: the tab re-reads them on entry.</summary>
        internal void OpenSpiralMapFromProfile()
        {
            try { ShowTab(SpiralRoom.TabKey); }
            catch (Exception ex) { Log.Debug("OpenSpiralMapFromProfile: {E}", ex.Message); }
        }

        private void ProfileMenuSpiral_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Same close path every other menu item uses, then the door.
            CloseProfileBubbleMenu();
            OpenSpiralMapFromProfile();
        }
    }
}
