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

        private static IReadOnlyList<IWorkshopCellVm> BuildCells() => new IWorkshopCellVm[]
        {
            new CompanionWorkshopCell("ROSTER",
                new CompanionWorkshopRow("Synthetic Blowdoll", "Lv 12"),
                new CompanionWorkshopRow("Perfect Fuckpuppet", "[100]"),
                new CompanionWorkshopRow("Brainwashed Slavedoll", "[125]"),
                new CompanionWorkshopRow("Platinum Puppet", "[150]"),
                new CompanionWorkshopRow("Bambi Cow", "[75]"),
                CompanionWorkshopRow.Caption("🎭 assign personality")),

            new CompanionWorkshopCell("BEHAVIOR",
                CompanionWorkshopRow.Slider("Idle interval", "120s", 0.38),
                CompanionWorkshopRow.Slider("Bubble time", "2s", 0.20),
                new CompanionWorkshopRow("Chat shortcut", "Ctrl+T"),
                new CompanionWorkshopRow("Camera shortcut", "Ctrl+Alt+K"),
                new CompanionWorkshopRow("Mute whispers · Pause browser"),
                CompanionWorkshopRow.Caption("idle interval is set by her Proactivity trait — override here")),

            new CompanionWorkshopCell("TRIGGERS & PHRASES",
                new CompanionWorkshopRow("Trigger mode · interval", "60s"),
                new CompanionWorkshopRow("Edit phrases"),
                new CompanionWorkshopRow("Manage phrases", "128 active"),
                new CompanionWorkshopRow("Phrase presets · save / delete")),

            new CompanionWorkshopCell("HER LIBRARY",
                new CompanionWorkshopRow("Hypnotube link pool", "Bambi Sleep scope"),
                new CompanionWorkshopRow("14 links · + add link")),

            new CompanionWorkshopCell("COMMUNITY",
                new CompanionWorkshopRow("Browse shared companions"),
                new CompanionWorkshopRow("Import / Export"),
                new CompanionWorkshopRow("Installed", "2")),

            new CompanionWorkshopCell("AWARENESS FINE-TUNING",
                CompanionWorkshopRow.Slider("Cooldown", "90s", 0.45),
                CompanionWorkshopRow.Slider("Max cooldown", "off", 0.0),
                new CompanionWorkshopRow("Privacy notice · full text"))
        };
    }
}
