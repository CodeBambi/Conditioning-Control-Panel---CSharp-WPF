using System;
using System.Collections.Generic;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IMakeHerYoursVm"/>.
    /// </summary>
    public sealed class MockMakeHerYoursVm : CompanionObservable, IMakeHerYoursVm
    {
        private bool _isSpiceOn;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockMakeHerYoursVm()
        {
            Traits = new ITraitGaugeVm[]
            {
                new CompanionTraitGauge(Loc.Get("companion_personality_trait_dominance"), 40),
                new CompanionTraitGauge(Loc.Get("companion_personality_trait_tease"), 50)
            };
            TraitChips = new[] { "Frame: Bestie", "Quirk: sparkly ✨", "Spicy", "Chatty" };
            Presets = BuildPresets();
            StartInterviewCommand = CompanionRelayCommand.NoOp("personality.interview");
            OpenTraitDashboardCommand = CompanionRelayCommand.NoOp("personality.dashboard");
            ResetPersonalityCommand = CompanionRelayCommand.NoOp("personality.reset");
            ViewCompiledPromptCommand = CompanionRelayCommand.NoOp("personality.viewPrompt");
            ForkPromptCommand = CompanionRelayCommand.NoOp("personality.fork");
            CommunityPromptsCommand = CompanionRelayCommand.NoOp("personality.community");

            // Preset chips behave as one radio group — including the second click on the chip that
            // is already active, which a ToggleButton would otherwise turn into "no preset
            // selected" while the compiled personality behind it is unchanged.
            foreach (var p in Presets)
            {
                p.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(IPresetChipVm.IsSelected)) return;
                    if (!p.IsSelected)
                    {
                        bool anySelected = false;
                        foreach (var other in Presets) if (other.IsSelected) { anySelected = true; break; }
                        if (!anySelected) p.IsSelected = true;
                        return;
                    }
                    foreach (var other in Presets)
                        if (!ReferenceEquals(other, p)) other.IsSelected = false;
                };
            }
        }

        public bool IsInterviewAvailable { get; init; }
        public bool IsInterviewed { get; init; }
        public string InterviewTitle { get; init; } =
            Loc.Get("companion_personality_interview_title");

        /// <summary>
        /// Two staged keys joined here rather than one key with an escaped newline: language
        /// files in this repo may not carry literal line breaks, and two sentences handed to a
        /// translator separately cannot be welded into one by accident.
        /// </summary>
        public string InterviewBody { get; init; } =
            Loc.Get("companion_personality_interview_body_1") + "\n" +
            Loc.Get("companion_personality_interview_body_2");
        public string InterviewCtaLabel { get; init; } =
            Loc.Get("companion_personality_interview_cta");
        /// <summary>
        /// The compressed chip. The two verbs from the design's chip row ("re-interview me~",
        /// "adjust her") are real buttons in the view, so this string carries the date only.
        /// </summary>
        public string InterviewedLine { get; init; } =
            string.Format(Loc.Get("companion_personality_interviewed_fmt"), "2026-08-12");
        public string InterviewDormantCopy { get; init; } =
            Loc.Get("companion_personality_interview_dormant");

        public bool AreTraitsAvailable { get; init; }
        public IReadOnlyList<ITraitGaugeVm> Traits { get; init; }
        public IReadOnlyList<string> TraitChips { get; init; }

        /// <summary>Concrete type so the ctor can wire the radio-group behaviour.</summary>
        public IReadOnlyList<CompanionPresetChip> Presets { get; }

        IReadOnlyList<IPresetChipVm> IMakeHerYoursVm.Presets => Presets;

        public bool IsSpiceOn
        {
            get => _isSpiceOn;
            set => Set(ref _isSpiceOn, value);
        }

        public string SpiceTitle { get; init; } =
            Loc.Get("companion_personality_spice_title");
        public string SpiceSubtitle { get; init; } =
            Loc.Get("companion_personality_spice_subtitle");
        public string ActivePersonalityLine { get; init; } =
            string.Format(Loc.Get("companion_personality_active_preset_fmt"),
                          Loc.Get("companion_personality_preset_sweet_bestie"));
        public bool CanResetPersonality { get; init; }
        public string ResetLabel { get; init; } =
            Loc.Get("companion_personality_reset");

        public ICommand StartInterviewCommand { get; }
        public ICommand OpenTraitDashboardCommand { get; }
        public ICommand ResetPersonalityCommand { get; }
        public ICommand ViewCompiledPromptCommand { get; }
        public ICommand ForkPromptCommand { get; }
        public ICommand CommunityPromptsCommand { get; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>Pre-Train 3 (the ship state): sleeping interview card, presets in bridge mode.</summary>
        public static MockMakeHerYoursVm Dormant() => new();

        /// <summary>Train 3 landed: the interview is live and the trait glance has values.</summary>
        public static MockMakeHerYoursVm Live() => new()
        {
            IsInterviewAvailable = true,
            AreTraitsAvailable = true,
            ActivePersonalityLine = Loc.Get("companion_personality_active_traits"),
            CanResetPersonality = true
        };

        /// <summary>Already interviewed — the spotlight compresses to a chip row.</summary>
        public static MockMakeHerYoursVm Interviewed() => new()
        {
            IsInterviewAvailable = true,
            IsInterviewed = true,
            AreTraitsAvailable = true,
            ActivePersonalityLine = Loc.Get("companion_personality_active_traits"),
            CanResetPersonality = true
        };

        /// <summary>A hand-edited prompt is active, so the sliders are disconnected and say so.</summary>
        public static MockMakeHerYoursVm HandEdited() => new()
        {
            IsInterviewAvailable = true,
            AreTraitsAvailable = false,
            ActivePersonalityLine = string.Format(
                Loc.Get("companion_personality_active_custom_fmt"), "My Domme v3"),
            CanResetPersonality = true
        };

        /// <summary>
        /// The preset chips. Ids are stable keys (they name the compiled personality); the
        /// labels come back through the staged loc layer as companion_personality_preset_&lt;id&gt;.
        /// </summary>
        private static IReadOnlyList<CompanionPresetChip> BuildPresets()
        {
            string[] ids =
            {
                "sweet_bestie", "playful_tease", "strict_domme", "hypno_guide",
                "bimbo_coach", "drone_handler", "bratty_rival"
            };

            var chips = new List<CompanionPresetChip>(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                chips.Add(new CompanionPresetChip(
                    ids[i],
                    Loc.Get($"companion_personality_preset_{ids[i]}"),
                    selected: i == 0));
            }
            return chips;
        }
    }
}
