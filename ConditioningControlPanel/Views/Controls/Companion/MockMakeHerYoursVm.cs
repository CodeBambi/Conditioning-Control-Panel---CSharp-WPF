using System;
using System.Collections.Generic;
using System.Windows.Input;

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
                new CompanionTraitGauge("Dominance", 40),
                new CompanionTraitGauge("Tease", 50)
            };
            TraitChips = new[] { "Frame: Bestie", "Quirk: sparkly ✨", "Spicy", "Chatty" };
            Presets = BuildPresets();
            StartInterviewCommand = CompanionRelayCommand.NoOp("personality.interview");
            OpenTraitDashboardCommand = CompanionRelayCommand.NoOp("personality.dashboard");
            ResetPersonalityCommand = CompanionRelayCommand.NoOp("personality.reset");
            ViewCompiledPromptCommand = CompanionRelayCommand.NoOp("personality.viewPrompt");
            ForkPromptCommand = CompanionRelayCommand.NoOp("personality.fork");
            CommunityPromptsCommand = CompanionRelayCommand.NoOp("personality.community");

            // Preset chips behave as one radio group.
            foreach (var p in Presets)
            {
                p.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(IPresetChipVm.IsSelected) || !p.IsSelected) return;
                    foreach (var other in Presets)
                        if (!ReferenceEquals(other, p)) other.IsSelected = false;
                };
            }
        }

        public bool IsInterviewAvailable { get; init; }
        public bool IsInterviewed { get; init; }
        public string InterviewTitle { get; init; } = "✨ Let her interview you";
        public string InterviewBody { get; init; } =
            "12 questions · 90 seconds · no typing.\nShe writes herself around your answers.";
        public string InterviewCtaLabel { get; init; } = "Start~";
        /// <summary>
        /// The compressed chip. The two verbs from the design's chip row ("re-interview me~",
        /// "adjust her") are real buttons in the view, so this string carries the date only.
        /// </summary>
        public string InterviewedLine { get; init; } = "Interviewed 2026-08-12";
        public string InterviewDormantCopy { get; init; } =
            "she's been drafting questions for you… interviews start next update.";

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

        public string SpiceTitle { get; init; } = "Slut Mode";
        public string SpiceSubtitle { get; init; } = "same girl, spicier right now";
        public string ActivePersonalityLine { get; init; } = "Active: Sweet bestie preset";
        public bool CanResetPersonality { get; init; }
        public string ResetLabel { get; init; } = "reset";

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
            ActivePersonalityLine = "Active: Trait profile · compiled from your interview",
            CanResetPersonality = true
        };

        /// <summary>Already interviewed — the spotlight compresses to a chip row.</summary>
        public static MockMakeHerYoursVm Interviewed() => new()
        {
            IsInterviewAvailable = true,
            IsInterviewed = true,
            AreTraitsAvailable = true,
            ActivePersonalityLine = "Active: Trait profile · compiled from your interview",
            CanResetPersonality = true
        };

        /// <summary>A hand-edited prompt is active, so the sliders are disconnected and say so.</summary>
        public static MockMakeHerYoursVm HandEdited() => new()
        {
            IsInterviewAvailable = true,
            AreTraitsAvailable = false,
            ActivePersonalityLine = "Custom: “My Domme v3” (hand-edited — sliders disconnected)",
            CanResetPersonality = true
        };

        private static IReadOnlyList<CompanionPresetChip> BuildPresets() => new[]
        {
            new CompanionPresetChip("sweet_bestie", "Sweet bestie", selected: true),
            new CompanionPresetChip("playful_tease", "Playful tease"),
            new CompanionPresetChip("strict_domme", "Strict domme"),
            new CompanionPresetChip("hypno_guide", "Hypno guide"),
            new CompanionPresetChip("bimbo_coach", "Bimbo coach"),
            new CompanionPresetChip("drone_handler", "Drone handler"),
            new CompanionPresetChip("bratty_rival", "Bratty rival")
        };
    }
}
