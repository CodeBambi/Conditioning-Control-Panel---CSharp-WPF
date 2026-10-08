using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// WPF MainWindow.Presets.cs:35 SetupHelpButtons: every tab "?" gets the interactive HelpPopover
    /// for its HelpContentService section. WPF reaches each button through a generated field; most
    /// Avalonia hosts load without InitializeComponent (and some are built late, inside drawers and
    /// workshop cells), so the same table is keyed by the button's x:Name and applied when the
    /// button loads. FeatureCard/SplitFeatureCard and the Studio rack panels attach their own.
    /// </summary>
    public partial class MainShellWindow
    {
        /// <summary>x:Name -> section id, in WPF MainWindow.Presets.cs:52-121 order.</summary>
        internal static readonly IReadOnlyDictionary<string, string> HelpButtonSections = new Dictionary<string, string>
        {
            ["HelpBtnBrowser"] = "Browser",
            ["HelpBtnAudio"] = "Audio",
            ["HelpBtnQuickLinks"] = "QuickLinks",
            ["HelpBtnPresets"] = "Presets",
            ["HelpBtnSessions"] = "Sessions",
            ["HelpBtnSessionDetails"] = "SessionDetails",
            ["HelpBtnQuests"] = "Quests",
            ["HelpBtnQuestStats"] = "QuestStats",
            ["HelpBtnRoadmap"] = "Roadmap",
            ["HelpBtnRoadmapStats"] = "RoadmapStats",
            ["HelpBtnAssets"] = "Assets",
            ["HelpBtnPacks"] = "ContentPacks",
            ["HelpBtnAssetBrowser"] = "AssetBrowser",
            ["HelpBtnQuiz"] = "Quiz",
            ["HelpBtnPlayGazeMinigame"] = "GazeMinigame",
            ["HelpBtnPlayFocusGaze"] = "FocusGaze",
            ["HelpBtnKeywordTriggers"] = "KeywordTriggers",
            ["HelpBtnScreenOcr"] = "ScreenOcr",
            ["HelpBtnRemoteControl"] = "RemoteControl",
            ["HelpBtnGetBackToMe"] = "GetBackToMe",
            ["HelpBtnAchievements"] = "Achievements",
            ["HelpBtnCompanions"] = "Companions",
            ["HelpBtnPrompts"] = "CommunityPrompts",
            ["HelpBtnVideoLinks"] = "HypnotubeLinks",
            ["HelpBtnCompanionSettings"] = "CompanionSettings",
            ["HelpBtnQuickControls"] = "QuickControls",
            ["HelpBtnAiChat"] = "AiChat",
            ["HelpBtnAwareness"] = "WindowAwareness",
            ["HelpBtnHaptics"] = "Haptics",
            ["HelpBtnBlinkTrainer"] = "BlinkTrainer",
            ["HelpBtnVideoHapticSync"] = "VideoHapticSync",
            ["HelpBtnDiscordProfile"] = "DiscordProfile",
            ["HelpBtnLeaderboard"] = "Leaderboard",
        };

        private static bool _helpButtonsHooked;

        private void InitializeHelpButtons()
        {
            if (_helpButtonsHooked) return;
            _helpButtonsHooked = true;
            LoadedEvent.AddClassHandler<Button>((button, _) => AttachHelpButton(button));
        }

        internal static void AttachHelpButton(Button button)
        {
            HelpContent content;
            if (button.Name == "BtnIntakePassHelp") content = BuildIntakePassHelpContent();
            else if (button.Name is { } name && HelpButtonSections.TryGetValue(name, out var sectionId))
                content = HelpContentService.GetContent(sectionId);
            else return;
            if (HelpPopover.IsAttached(button)) return; // re-attaching would close a pinned card on every load
            ToolTip.SetTip(button, null); // popover and ToolTip must never double-render (WPF Presets.cs:130)
            HelpPopover.Attach(button, content);
        }

        /// <summary>WPF MainWindow.UiUpdates.cs:1730. Its punch-card tip is gated on
        /// IntakePunchCardService.UiEnabled, a const false, so it is left out.</summary>
        private static HelpContent BuildIntakePassHelpContent() => new()
        {
            SectionId = "IntakePass",
            Icon = "★",
            Title = Loc.Get("help_intake_pass_title"),
            WhatItDoes = Loc.Get("help_intake_pass_what"),
            Tips = new List<string> { Loc.Get("help_intake_pass_tip_free"), Loc.Get("help_intake_pass_tip_patron") },
            HowItWorks = Loc.Get("help_intake_pass_how"),
        };
    }
}
