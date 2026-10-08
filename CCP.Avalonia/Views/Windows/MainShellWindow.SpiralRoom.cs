// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SpiralRoom.cs: the Spiral rail row's
// visibility and label, read from Core SpiralRoom.StateFor - the same pure function the tab paints
// from, so the row and the room cannot disagree.
//
// ponytail: DescentService (the block, BlockChanged) and DescentMigrationService (SpiralWithheld)
// are WPF-head network services, so this head reads hasBlock=false / withheld=false: the row shows
// only in the fog era (gold ellipsis) and never wears "The Spiral". BeginSpiralFirstLight has no
// caller here (DescentShowDirector is WPF-only) and is not ported until it does.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF SpiralRailAnonymousLabel: one character during the fog, never a word.</summary>
        private const string SpiralRailAnonymousLabel = "…";

        /// <summary>FuseGold, never the mod accent (WPF SpiralRailAnonymousBrush).</summary>
        private static readonly IBrush SpiralRailAnonymousBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x52));

        private bool _spiralRoomWired;

        /// <summary>WPF InitializeSpiralRoom: subscriptions plus one catch-up paint. Idempotent.</summary>
        internal void InitializeSpiralRoom()
        {
            if (_spiralRoomWired) return;
            _spiralRoomWired = true;
            try
            {
                // Both sources outlive the window (a static and an app service), so the handlers
                // go when it closes or every test shell would stay subscribed (P41).
                var fuse = App.DescentCountdown;
                if (fuse != null) fuse.PhaseChanged += OnSpiralRoomPhaseChanged;
                LocalizationManager.Instance.LanguageChanged += OnSpiralRoomLanguageChanged;
                Closed += (_, _) =>
                {
                    if (fuse != null) fuse.PhaseChanged -= OnSpiralRoomPhaseChanged;
                    LocalizationManager.Instance.LanguageChanged -= OnSpiralRoomLanguageChanged;
                };
                RefreshSpiralRailEntry();
            }
            catch (Exception ex) { Log.Debug("[Spiral] room rail could not be wired: {E}", ex.Message); }
        }

        private void OnSpiralRoomPhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e) => RefreshSpiralRailEntry();

        private void OnSpiralRoomLanguageChanged(object? sender, EventArgs e) => RefreshSpiralRailEntry();

        /// <summary>WPF RefreshSpiralRailEntry: show, hide and name the row from scratch.</summary>
        internal void RefreshSpiralRailEntry()
        {
            if (Named<Button>("BtnNavSpiral") is not { } row) return;
            try
            {
                var fuse = App.DescentCountdown;
                var state = SpiralRoom.StateFor(
                    CoreSettings.Current,
                    fuse?.LastAnnouncedPhase ?? DescentFusePhase.Dark,
                    fuse?.IsArmed == true,
                    spiralWithheld: false,   // ponytail: App.DescentMigration is WPF-only
                    hasBlock: false);        // ponytail: App.Descent is WPF-only

                bool show = SpiralRoom.RailEntryVisible(state);
                row.IsVisible = show;
                if (!show || Named<TextBlock>("TxtNavSpiral") is not { } label) return;

                if (SpiralRoom.RailEntryIsAnonymous(state))
                {
                    label.Text = SpiralRailAnonymousLabel;
                    label.Foreground = SpiralRailAnonymousBrush;
                }
                else
                {
                    label.Text = Loc.Get("tab_spiral");
                    label.ClearValue(TextBlock.ForegroundProperty);   // the theme's brush stays live
                }
            }
            catch (Exception ex) { Log.Debug("[Spiral] rail row repaint failed: {E}", ex.Message); }
        }

        /// <summary>The rail row's click. The tab does all the deciding.</summary>
        private void BtnNavSpiral_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab(SpiralRoom.TabKey);
    }
}
