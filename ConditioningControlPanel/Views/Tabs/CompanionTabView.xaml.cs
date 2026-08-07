using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Companion tab. See the XAML header for what this file stopped being.
    ///
    /// <para><b>The compat seam.</b> Roughly 110 named elements on the old tab were written to by
    /// seven MainWindow partials, and the design doc (§6 last row, §7.10) is explicit that every one
    /// of those x:Names has to survive the redesign. The properties below are that promise: each
    /// resolves to the SAME control, now living in the zone or Workshop cell the disposition table
    /// puts it in. The partials keep the accessor path they always had —
    /// <c>CompanionTab.SliderIdleIntervalCompanion</c> still means the idle slider — so the swap is
    /// a rename, not a rewrite.</para>
    ///
    /// <para>Controls the disposition table marks <b>superseded</b> are NOT here. They were removed,
    /// not hidden, and their write sites in the partials now go through
    /// <see cref="CompanionRoomRuntimeVm"/> instead. That asymmetry is the point: a passthrough for
    /// a control the design deleted would have kept the old surface alive behind the new one.</para>
    /// </summary>
    public partial class CompanionTabView : UserControl
    {
        private readonly CompanionRoomRuntimeVm _vm;

        public CompanionTabView()
        {
            InitializeComponent();

            // The window is resolved lazily: this constructor runs while the tab is still being
            // parsed into MainWindow's tree, where Window.GetWindow(this) is null.
            _vm = new CompanionRoomRuntimeVm(() => Window.GetWindow(this) as MainWindow);
            Room.ViewModel = _vm;

            // FX lifecycle. Hooked here rather than in ShowTab so the tab owns its own decoration;
            // the room parks its own clocks off the same visibility change.
            IsVisibleChanged += CompanionTabView_IsVisibleChanged;
        }

        /// <summary>The page's runtime viewmodel — what the MainWindow partials now drive.</summary>
        internal CompanionRoomRuntimeVm Vm => _vm;

        private void CompanionTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnCompanionTabVisibilityChanged(IsVisible);
        }

        // =====================================================================================
        //  compat passthroughs — the kept controls, by their original names
        // =====================================================================================

        // ---- hidden status/help elements (design §6, last row) ----
        internal TextBlock TxtDetachStatusCompanion => Room.TxtDetachStatusCompanion;
        internal TextBlock TxtCompanionStatus => Room.TxtCompanionStatus;
        internal Button HelpBtnQuickControls => Room.HelpBtnQuickControls;
        internal Button HelpBtnCompanionSettings => Room.HelpBtnCompanionSettings;

        /// <summary>The hero XP bar's drain flash overlay (MainWindow.Companion.cs).</summary>
        internal Border PrgCompanion0FlashOverlay => Room.PrgCompanion0FlashOverlay;

        // ---- Z8 · ROSTER ----
        internal Border CompanionCard0 => _vm.Shelf.Roster.CompanionCard0;
        internal Border CompanionCard1 => _vm.Shelf.Roster.CompanionCard1;
        internal Border CompanionCard2 => _vm.Shelf.Roster.CompanionCard2;
        internal Border CompanionCard3 => _vm.Shelf.Roster.CompanionCard3;
        internal Border CompanionCard4 => _vm.Shelf.Roster.CompanionCard4;

        internal TextBlock TxtCompanion0Name => _vm.Shelf.Roster.TxtCompanion0Name;
        internal TextBlock TxtCompanion1Name => _vm.Shelf.Roster.TxtCompanion1Name;
        internal TextBlock TxtCompanion2Name => _vm.Shelf.Roster.TxtCompanion2Name;
        internal TextBlock TxtCompanion3Name => _vm.Shelf.Roster.TxtCompanion3Name;
        internal TextBlock TxtCompanion4Name => _vm.Shelf.Roster.TxtCompanion4Name;

        internal TextBlock TxtCompanion0Level => _vm.Shelf.Roster.TxtCompanion0Level;
        internal TextBlock TxtCompanion1Level => _vm.Shelf.Roster.TxtCompanion1Level;
        internal TextBlock TxtCompanion2Level => _vm.Shelf.Roster.TxtCompanion2Level;
        internal TextBlock TxtCompanion3Level => _vm.Shelf.Roster.TxtCompanion3Level;
        internal TextBlock TxtCompanion4Level => _vm.Shelf.Roster.TxtCompanion4Level;

        internal TextBlock TxtCompanion0Lock => _vm.Shelf.Roster.TxtCompanion0Lock;
        internal TextBlock TxtCompanion1Lock => _vm.Shelf.Roster.TxtCompanion1Lock;
        internal TextBlock TxtCompanion2Lock => _vm.Shelf.Roster.TxtCompanion2Lock;
        internal TextBlock TxtCompanion3Lock => _vm.Shelf.Roster.TxtCompanion3Lock;
        internal TextBlock TxtCompanion4Lock => _vm.Shelf.Roster.TxtCompanion4Lock;

        internal TextBlock TxtCompanion0Prompt => _vm.Shelf.Roster.TxtCompanion0Prompt;
        internal TextBlock TxtCompanion1Prompt => _vm.Shelf.Roster.TxtCompanion1Prompt;
        internal TextBlock TxtCompanion2Prompt => _vm.Shelf.Roster.TxtCompanion2Prompt;
        internal TextBlock TxtCompanion3Prompt => _vm.Shelf.Roster.TxtCompanion3Prompt;
        internal TextBlock TxtCompanion4Prompt => _vm.Shelf.Roster.TxtCompanion4Prompt;

        internal Button HelpBtnCompanions => _vm.Shelf.Roster.HelpBtnCompanions;

        // ---- Z8 · BEHAVIOR ----
        internal TextBlock TxtIdleIntervalCompanion => _vm.Shelf.Behavior.TxtIdleIntervalCompanion;
        internal Slider SliderIdleIntervalCompanion => _vm.Shelf.Behavior.SliderIdleIntervalCompanion;
        internal TextBlock TxtBubbleDurationCompanion => _vm.Shelf.Behavior.TxtBubbleDurationCompanion;
        internal Slider SliderBubbleDurationCompanion => _vm.Shelf.Behavior.SliderBubbleDurationCompanion;
        internal TextBlock TxtChatShortcutLabel => _vm.Shelf.Behavior.TxtChatShortcutLabel;
        internal TextBlock TxtCameraShortcutLabel => _vm.Shelf.Behavior.TxtCameraShortcutLabel;
        internal CheckBox ChkMuteWhispersCompanion => _vm.Shelf.Behavior.ChkMuteWhispersCompanion;
        internal CheckBox ChkPauseBrowserCompanion => _vm.Shelf.Behavior.ChkPauseBrowserCompanion;
        internal CheckBox ChkVoiceLinesCompanion => _vm.Shelf.Behavior.ChkVoiceLinesCompanion;

        // ---- Z8 · TRIGGERS & PHRASES ----
        internal CheckBox ChkTriggerModeCompanion => _vm.Shelf.Triggers.ChkTriggerModeCompanion;
        internal StackPanel TriggerSettingsPanelCompanion => _vm.Shelf.Triggers.TriggerSettingsPanelCompanion;
        internal Slider SliderTriggerIntervalCompanion => _vm.Shelf.Triggers.SliderTriggerIntervalCompanion;
        internal TextBlock TxtTriggerIntervalCompanion => _vm.Shelf.Triggers.TxtTriggerIntervalCompanion;
        internal TextBlock TxtPhraseCount => _vm.Shelf.Triggers.TxtPhraseCount;
        internal ComboBox CmbPhrasePresets => _vm.Shelf.Triggers.CmbPhrasePresets;

        // ---- Z8 · HER LIBRARY ----
        internal TextBlock TxtHypnotubeModeLabel => _vm.Shelf.Library.TxtHypnotubeModeLabel;
        internal StackPanel VideoLinkPoolPanel => _vm.Shelf.Library.VideoLinkPoolPanel;
        internal TextBlock TxtNoVideoLinks => _vm.Shelf.Library.TxtNoVideoLinks;
        internal Button HelpBtnVideoLinks => _vm.Shelf.Library.HelpBtnVideoLinks;

        // ---- Z8 · COMMUNITY ----
        internal Button BtnRefreshPrompts => _vm.Shelf.Community.BtnRefreshPrompts;
        internal StackPanel InstalledPromptsPanel => _vm.Shelf.Community.InstalledPromptsPanel;
        internal TextBlock TxtNoInstalledPrompts => _vm.Shelf.Community.TxtNoInstalledPrompts;
        internal Button HelpBtnPrompts => _vm.Shelf.Community.HelpBtnPrompts;

        // ---- Z8 · AWARENESS FINE-TUNING ----
        internal Border AwarenessSettingsPanel => _vm.Shelf.Awareness.AwarenessSettingsPanel;
        internal Slider SliderAwarenessCooldown => _vm.Shelf.Awareness.SliderAwarenessCooldown;
        internal TextBlock TxtAwarenessCooldown => _vm.Shelf.Awareness.TxtAwarenessCooldown;
        internal Slider SliderAwarenessCooldownMax => _vm.Shelf.Awareness.SliderAwarenessCooldownMax;
        internal TextBlock TxtAwarenessCooldownMax => _vm.Shelf.Awareness.TxtAwarenessCooldownMax;
        internal Button BtnPrivacySpoiler => _vm.Shelf.Awareness.BtnPrivacySpoiler;
        internal TextBlock TxtPrivacyDetails => _vm.Shelf.Awareness.TxtPrivacyDetails;
        internal Button HelpBtnAwareness => _vm.Shelf.Awareness.HelpBtnAwareness;

        /// <summary>
        /// The awareness cell itself, for the one thing that is not a named element: the v2 intensity
        /// dial reads its state back from settings rather than being written control-by-control.
        /// </summary>
        internal Views.Controls.Companion.Runtime.WorkshopAwarenessCell AwarenessCell => _vm.Shelf.Awareness;
    }
}
