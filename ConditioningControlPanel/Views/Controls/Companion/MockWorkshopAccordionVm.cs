using System.Collections.Generic;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IWorkshopAccordionVm"/>.
    /// The cells mirror the mockup's six pigeonholes; the wiring pass swaps each row for the real
    /// control while keeping every x:Name the MainWindow partials write to.
    /// </summary>
    public sealed class MockWorkshopAccordionVm : CompanionObservable, IWorkshopAccordionVm
    {
        private bool _isExpanded;

        /// <summary>
        /// The two cells other zones deep-link to by name: the hero's Switch chip asks for the
        /// roster, Z5's "fine-tuning ↓" link asks for the awareness cell.
        ///
        /// <para>The anchors themselves moved to <see cref="CompanionRoomAnchors"/> when the page
        /// was composed — the real viewmodel needs them too, and they cannot come from a mock.
        /// These aliases stay so callers written against the standalone zone keep working.</para>
        /// </summary>
        public const string RosterCellTitle = CompanionRoomAnchors.WorkshopRosterCell;

        /// <inheritdoc cref="RosterCellTitle"/>
        public const string AwarenessCellTitle = CompanionRoomAnchors.WorkshopAwarenessCell;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockWorkshopAccordionVm()
        {
            FocusCellCommand = CompanionRelayCommand.NoOp("workshop.focusCell");
            Cells = BuildCells();
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public string DrawerNote { get; init; } = "nothing was deleted. it just stopped being the front door.";
        public IReadOnlyList<IWorkshopCellVm> Cells { get; init; }
        public ICommand FocusCellCommand { get; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The resting state: closed.</summary>
        public static MockWorkshopAccordionVm Collapsed() => new();

        /// <summary>Opened — what the hero's Switch chip does before scrolling the roster in.</summary>
        public static MockWorkshopAccordionVm Expanded() => new() { IsExpanded = true };

        /// <summary>
        /// An actionable row. Every row that maps to a real dialog/editor in the disposition table
        /// carries a command, because the view lights a row up on hover only when it has one —
        /// an inert row that pretends to be clickable is worse than an honest static line.
        /// </summary>
        private static CompanionWorkshopRow Row(string label, string? value, string tag)
            => new(label, value) { ActivateCommand = CompanionRelayCommand.NoOp(tag) };

        private static IReadOnlyList<IWorkshopCellVm> BuildCells() => new IWorkshopCellVm[]
        {
            // BtnSwitchCompanion + CompanionCard0-4 + the 🎭 personality assignment
            new CompanionWorkshopCell(RosterCellTitle,
                Row("Synthetic Blowdoll", "Lv 12", "workshop.roster.0"),
                Row("Perfect Fuckpuppet", "[100]", "workshop.roster.1"),
                Row("Brainwashed Slavedoll", "[125]", "workshop.roster.2"),
                Row("Platinum Puppet", "[150]", "workshop.roster.3"),
                Row("Bambi Cow", "[75]", "workshop.roster.4"),
                Row("🎭 assign personality", null, "workshop.roster.assign")),

            // idle interval + bubble duration sliders, both shortcut editors, the two switches
            new CompanionWorkshopCell("BEHAVIOR",
                CompanionWorkshopRow.Slider("Idle interval", "120s", 0.38),
                CompanionWorkshopRow.Slider("Bubble time", "2s", 0.20),
                Row("Chat shortcut", "Ctrl+T", "workshop.behavior.chatShortcut"),
                Row("Camera shortcut", "Ctrl+Alt+K", "workshop.behavior.cameraShortcut"),
                Row("Mute whispers", "on", "workshop.behavior.muteWhispers"),
                Row("Pause in browser", "off", "workshop.behavior.pauseBrowser"),
                CompanionWorkshopRow.Caption("idle interval is set by her Proactivity trait — override here")),

            // System B keyword engine: trigger mode + interval + Edit Phrases, then the phrase shelf
            new CompanionWorkshopCell("TRIGGERS & PHRASES",
                Row("Trigger mode · interval", "60s", "workshop.triggers.mode"),
                Row("Edit trigger phrases", null, "workshop.triggers.edit"),
                Row("Manage phrases", "128 active", "workshop.phrases.manage"),
                Row("Phrase presets · save / delete", null, "workshop.phrases.presets")),

            new CompanionWorkshopCell("HER LIBRARY",
                Row("Hypnotube link pool", "Bambi Sleep scope", "workshop.library.pool"),
                Row("+ add link", "14 links", "workshop.library.add")),

            new CompanionWorkshopCell("COMMUNITY",
                Row("Browse shared companions", null, "workshop.community.browse"),
                Row("Import / Export", null, "workshop.community.importExport"),
                Row("Refresh installed", "2", "workshop.community.refresh")),

            // Z5's "fine-tuning ↓" link lands here
            new CompanionWorkshopCell(AwarenessCellTitle,
                CompanionWorkshopRow.Slider("Cooldown", "90s", 0.45),
                CompanionWorkshopRow.Slider("Max cooldown", "off", 0.0),
                Row("Privacy notice · full text", null, "workshop.awareness.privacy"))
        };
    }
}
