using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="ICompanionHeaderVm"/>.
    ///
    /// <para>Copy comes back through <see cref="Loc.Get"/> rather than as literals so the mock
    /// exercises the same key path the shipped viewmodel uses: if a key is missing from the
    /// language files the designer shows the raw key, which is the failure we want visible.</para>
    /// </summary>
    public sealed class MockCompanionHeaderVm : CompanionObservable, ICompanionHeaderVm
    {
        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockCompanionHeaderVm()
        {
            TutorialCommand = CompanionRelayCommand.NoOp("header.tutorial");
            OpenPatreonCommand = CompanionRelayCommand.NoOp("header.patreon");
        }

        public string Title { get; init; } = Loc.Get("companion_header_title");
        public string Subtitle { get; init; } = Loc.Get("companion_header_subtitle");
        public string TutorialLabel { get; init; } = Loc.Get("companion_header_tutorial");

        public bool HasAiAccess { get; init; } = true;

        public string AiPlateLabel { get; init; } = Loc.Get("companion_header_plate_ai");
        public string NextTierPlateLabel { get; init; } = Loc.Get("companion_header_plate_next");
        public string TeaserRibbonLabel { get; init; } = Loc.Get("companion_header_teaser");

        public ICommand TutorialCommand { get; }
        public ICommand OpenPatreonCommand { get; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>Lab subscriber: the AI plate is lit and there is nothing to sell.</summary>
        public static MockCompanionHeaderVm Entitled() => new();

        /// <summary>
        /// Free / logged-out: the plate dims and the Vault teaser ribbon appears next to it. The
        /// page below stays fully alive — barks are free and the hero never looks broken.
        /// </summary>
        public static MockCompanionHeaderVm FreeTier() => new() { HasAiAccess = false };
    }
}
