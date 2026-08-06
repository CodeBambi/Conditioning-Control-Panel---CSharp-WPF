using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z4 — Make her yours. The three-tier personality UX given one address: interview CTA on top,
    /// a read-only trait glance, preset chips, the spice switch, and a quiet expert door.
    ///
    /// <para>Never gated. The interview is the conversion surface, so a free user gets all of it.</para>
    ///
    /// <para><see cref="IsInterviewAvailable"/> false is the pre-Train-3 sleeping card: the
    /// spotlight is there and shimmers, but the button is unfilled and the copy says interviews
    /// start next update.</para>
    /// </summary>
    public interface IMakeHerYoursVm : INotifyPropertyChanged
    {
        // ---- interview CTA ----
        bool IsInterviewAvailable { get; }
        /// <summary>Already interviewed — the card compresses to a chip row.</summary>
        bool IsInterviewed { get; }
        string InterviewTitle { get; }
        string InterviewBody { get; }
        string InterviewCtaLabel { get; }
        /// <summary>"Interviewed 2026-08-12 · re-interview me~".</summary>
        string InterviewedLine { get; }
        /// <summary>"she's been drafting questions for you…" (dormant only).</summary>
        string InterviewDormantCopy { get; }

        // ---- trait glance (read-only; the dashboard lives one click down) ----
        bool AreTraitsAvailable { get; }
        IReadOnlyList<ITraitGaugeVm> Traits { get; }
        /// <summary>Frame / Quirk / Explicitness chips.</summary>
        IReadOnlyList<string> TraitChips { get; }

        // ---- presets ----
        IReadOnlyList<IPresetChipVm> Presets { get; }

        // ---- spice ----
        /// <summary>Slut Mode, restyled as a small flame toggle. Two-way.</summary>
        bool IsSpiceOn { get; set; }
        string SpiceTitle { get; }
        string SpiceSubtitle { get; }

        // ---- readout ----
        /// <summary>"Active: Trait profile · compiled from your interview".</summary>
        string ActivePersonalityLine { get; }
        /// <summary>A hand-edited custom prompt is active, so the sliders are disconnected.</summary>
        bool CanResetPersonality { get; }
        string ResetLabel { get; }

        ICommand StartInterviewCommand { get; }
        ICommand OpenTraitDashboardCommand { get; }
        ICommand ResetPersonalityCommand { get; }
        ICommand ViewCompiledPromptCommand { get; }
        ICommand ForkPromptCommand { get; }
        ICommand CommunityPromptsCommand { get; }
    }
}
