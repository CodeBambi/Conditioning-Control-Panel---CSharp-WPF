using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE SPIRAL ROOM's rail row, and the one door every other door goes through.
    ///
    /// <para><b>Why the row is not simply always there.</b> A rail entry is a promise that there is
    /// somewhere to go. A fuse-dark account with no descent block — every install on today's server —
    /// has nowhere to go, so the row is Collapsed and the You door measures exactly as it did before
    /// this feature existed. It appears in two eras and wears a different word in each: an ellipsis
    /// during the fog (naming the room would spoil it) and its real name once the spiral is open.</para>
    ///
    /// <para><b>The visibility arithmetic is not here.</b> It is
    /// <see cref="SpiralRoom.StateFor(Models.AppSettings, DescentFusePhase, bool, bool, bool)"/>, the
    /// same pure function the tab itself paints from — so the row and the room it points at cannot
    /// disagree about whether there is anything to show.</para>
    ///
    /// <para><b>Three signals, all already flowing.</b> <c>PhaseChanged</c> moves the fog era's
    /// edges, <c>BlockChanged</c> carries both the block AND the withhold (which has no event of its
    /// own by design — DescentMigrationService re-raises this one), and <c>LanguageChanged</c> is
    /// needed because the label is written imperatively: a <c>{loc:Str}</c> binding cannot express
    /// "an ellipsis during the fog", and the first imperative write would have killed the binding
    /// anyway.</para>
    /// </summary>
    public partial class MainWindow
    {
        private bool _spiralRoomWired;

        /// <summary>
        /// Wire the rail row up and paint it. Called once from the MainWindow constructor beside the
        /// other fuse initializers; idempotent, and inert on an account with no fuse and no block —
        /// three event subscriptions and one Collapsed write.
        /// </summary>
        private void InitializeSpiralRoom()
        {
            if (_spiralRoomWired) return;
            _spiralRoomWired = true;

            try
            {
                var fuse = App.DescentCountdown;
                if (fuse != null) fuse.PhaseChanged += OnSpiralRoomPhaseChanged;
                if (App.Descent != null) App.Descent.BlockChanged += OnSpiralRoomBlockChanged;
                LocalizationManager.Instance.LanguageChanged += OnSpiralRoomLanguageChanged;

                RefreshSpiralRailEntry();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] room rail could not be wired: {E}", ex.Message);
            }
        }

        private void OnSpiralRoomPhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e)
            => RefreshSpiralRailEntry();

        private void OnSpiralRoomBlockChanged(object? sender, EventArgs e)
            => RefreshSpiralRailEntry();

        private void OnSpiralRoomLanguageChanged(object? sender, EventArgs e)
            => RefreshSpiralRailEntry();

        /// <summary>
        /// Show, hide and name the Spiral row. Computed from scratch every time, so arriving at any
        /// state — including a kill switch dropping the fuse straight to Dark, or a block being
        /// withdrawn mid-session — lands on a correct row rather than on the accumulation of
        /// whatever transitions happened.
        /// </summary>
        internal void RefreshSpiralRailEntry()
        {
            // Nav rework (2026-10-06): the Spiral row left the rail. The You strip draws the
            // pill from NavSections (hidden: true), and the fuse chip on the rail stays the way
            // in during the fog era. Kept as the one place the fuse events land, so a fog-era
            // pill reveal can hook in here.
        }

        /// <summary>The rail row's click. One line, because the tab does all the deciding.</summary>
        private void BtnNavSpiral_Click(object sender, RoutedEventArgs e) => ShowTab(SpiralRoom.TabKey);

        /// <summary>
        /// THE FIRST LIGHT's landing (CONTRACT-FUSE-0816 §2.4). Called by
        /// <c>DescentShowDirector</c> after it has brought this window forward: navigate through the
        /// real door — <see cref="ShowTab"/>, the same path the rail, the barks and the palette all
        /// use — and then let the room play its reveal.
        ///
        /// <para>ShowTab first, and the ordering matters: <c>OnTabShown</c> paints the ordinary state
        /// (which at this moment is the waiting room or the canvas), and the reveal then takes the
        /// surface from it. Doing it the other way round would have the tab's own entry repaint
        /// stomp the reveal one frame after it started.</para>
        /// </summary>
        internal void BeginSpiralFirstLight()
        {
            try
            {
                ShowTab(SpiralRoom.TabKey);
                SpiralTab?.BeginFirstLight();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Spiral] the first light could not open in the room");
            }
        }
    }
}
