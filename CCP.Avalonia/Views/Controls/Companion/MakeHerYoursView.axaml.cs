using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z4 — Make her yours. See the XAML header for the visual spec.
    ///
    /// <para>The only code here fires the interview spotlight's shimmer sweep — a ONE-SHOT on tab
    /// load, not a loop. The mockup's CSS animates it forever; the FX plan does not allow a second
    /// ambient loop on this tab, so it plays once when the card appears and once again whenever
    /// <see cref="PlayIntro"/> is called (e.g. after an interview completes).</para>
    ///
    /// <para>Ported from the WPF code-behind. The Storyboard is gone: WPF cloned
    /// CmpShimmerSweepStoryboard and retargeted it at the named TranslateTransform. On Avalonia an
    /// Animation whose setters target a transform must run against the Visual, which would clobber
    /// the SkewTransform sharing that TransformGroup, so the sweep is a DoubleTransition on the
    /// transform itself — same 1.4s cubic ease, same 0.25s delay, same one-shot semantics, and no
    /// InvalidCastException class to fall into.</para>
    /// </summary>
    public partial class MakeHerYoursView : UserControl
    {
        private bool _introPlayed;

        public MakeHerYoursView()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new MakeHerYoursViewModel();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_introPlayed) return;
            _introPlayed = true;
            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.UIThread.Post(PlayIntro, DispatcherPriority.Normal);
        }

        /// <summary>Sweeps the spotlight highlight across the interview card exactly once.</summary>
        public void PlayIntro()
        {
            // The card is collapsed once she has been interviewed; nothing to sweep.
            var card = this.FindControl<Border>("InterviewCard");
            var shimmer = this.FindControl<Border>("InterviewShimmer");
            if (card is null || shimmer is null) return;

            // x:Name is illegal on a Transform in Avalonia, so the shift is reached through the group.
            if (shimmer.RenderTransform is not TransformGroup group) return;
            var shift = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (shift is null) return;

            // Park at the start with no transition attached, then attach and set the end value so
            // the sweep runs once from -90. One-time Bounds read at Loaded — a value, not a binding.
            shift.Transitions = null;
            shift.X = -90;
            shift.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.XProperty,
                    Duration = TimeSpan.FromSeconds(1.4),
                    Delay = TimeSpan.FromSeconds(0.25),
                    Easing = new CubicEaseInOut()
                }
            };

            shimmer.Opacity = 1;
            shift.X = card.Bounds.Width > 1 ? card.Bounds.Width + 90 : 480;
        }
    }

    /// <summary>
    /// The view's data contract and its strings, in one class. See AchievementsTabViewModel for
    /// why the strings cannot stay as {loc:Str}.
    ///
    /// <para>The WPF view binds to <c>IMakeHerYoursVm</c>, which lives in the head alongside
    /// <c>MockMakeHerYoursVm</c> and cannot cross. This class is that interface's shape as a
    /// concrete type — compiled bindings need one anyway — seeded with the mock's
    /// <c>Dormant()</c> exhibit, which is the shipping pre-Train-3 state.</para>
    /// </summary>
    public sealed class MakeHerYoursViewModel : INotifyPropertyChanged
    {
        private bool _isSpiceOn;

        public MakeHerYoursViewModel()
        {
            Traits = new[]
            {
                new TraitGauge(Loc.Get("companion_personality_trait_dominance"), 40),
                new TraitGauge(Loc.Get("companion_personality_trait_tease"), 50)
            };
            TraitChips = new[] { "Frame: Bestie", "Quirk: sparkly ✨", "Spicy", "Chatty" };
            Presets = BuildPresets();

            // Preset chips behave as one radio group — including the second click on the chip that
            // is already active, which a ToggleButton would otherwise turn into "no preset
            // selected" while the compiled personality behind it is unchanged.
            foreach (var p in Presets)
            {
                p.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(PresetChip.IsSelected)) return;
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

        // ---- strings the markup used to get from {loc:Str} ----
        public string LocPersonalityTitle => Loc.Get("companion_personality_title");
        public string LocTagTrain3 => Loc.Get("companion_tag_train3");
        public string LocReinterview => Loc.Get("companion_personality_reinterview");
        public string LocAdjust => Loc.Get("companion_personality_adjust");
        public string LocTraitsTip => Loc.Get("companion_personality_traits_tip");
        public string LocViewPrompt => Loc.Get("companion_personality_view_prompt");
        public string LocFork => Loc.Get("companion_personality_fork");
        public string LocCommunity => Loc.Get("companion_personality_community");

        // ---- interview CTA ----
        public bool IsInterviewAvailable { get; init; }
        /// <summary>Already interviewed — the card compresses to a chip row.</summary>
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

        // ---- trait glance (read-only; the dashboard lives one click down) ----
        public bool AreTraitsAvailable { get; init; }
        public IReadOnlyList<TraitGauge> Traits { get; }
        /// <summary>Frame / Quirk / Explicitness chips.</summary>
        public IReadOnlyList<string> TraitChips { get; }

        // ---- presets ----
        public IReadOnlyList<PresetChip> Presets { get; }

        // ---- spice ----
        /// <summary>Slut Mode, restyled as a small flame toggle. Two-way.</summary>
        public bool IsSpiceOn
        {
            get => _isSpiceOn;
            set { if (_isSpiceOn == value) return; _isSpiceOn = value; Raise(); }
        }

        public string SpiceTitle { get; init; } =
            Loc.Get("companion_personality_spice_title");
        public string SpiceSubtitle { get; init; } =
            Loc.Get("companion_personality_spice_subtitle");

        // ---- readout ----
        public string ActivePersonalityLine { get; init; } =
            string.Format(Loc.Get("companion_personality_active_preset_fmt"),
                          Loc.Get("companion_personality_preset_sweet_bestie"));
        /// <summary>A hand-edited custom prompt is active, so the sliders are disconnected.</summary>
        public bool CanResetPersonality { get; init; }
        public string ResetLabel { get; init; } =
            Loc.Get("companion_personality_reset");

        // UNWIRED. Every one of these opens a host-owned surface — the interview flow, the trait
        // dashboard, the compiled-prompt viewer, the fork editor, the community browser — and none
        // of those exist on this head yet. Null, deliberately, rather than a NoOp that would let a
        // dead button look alive.
        public ICommand? StartInterviewCommand => null;
        public ICommand? OpenTraitDashboardCommand => null;
        public ICommand? ResetPersonalityCommand => null;
        public ICommand? ViewCompiledPromptCommand => null;
        public ICommand? ForkPromptCommand => null;
        public ICommand? CommunityPromptsCommand => null;

        /// <summary>
        /// The preset chips. Ids are stable keys (they name the compiled personality); the
        /// labels come back through the staged loc layer as companion_personality_preset_&lt;id&gt;.
        /// </summary>
        private static IReadOnlyList<PresetChip> BuildPresets()
        {
            string[] ids =
            {
                "sweet_bestie", "playful_tease", "strict_domme", "hypno_guide",
                "bimbo_coach", "drone_handler", "bratty_rival"
            };

            var chips = new List<PresetChip>(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                chips.Add(new PresetChip(
                    ids[i],
                    Loc.Get($"companion_personality_preset_{ids[i]}"),
                    selected: i == 0));
            }
            return chips;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One read-only trait gauge. Port of CompanionTraitGauge / ITraitGaugeVm.</summary>
    public sealed class TraitGauge
    {
        public TraitGauge(string label, int value)
        {
            Label = label;
            Value = value < 0 ? 0 : (value > 100 ? 100 : value);
        }

        public string Label { get; }

        /// <summary>0..100, shown as the right-hand number.</summary>
        public int Value { get; }

        /// <summary>0..1, feeds the star-width fill column.</summary>
        public double Fraction => Value / 100.0;
    }

    /// <summary>A preset chip in Z4. Port of CompanionPresetChip / IPresetChipVm.</summary>
    public sealed class PresetChip : INotifyPropertyChanged
    {
        private bool _isSelected;

        public PresetChip(string id, string label, bool selected = false)
        {
            Id = id;
            Label = label;
            _isSelected = selected;
        }

        public string Id { get; }
        public string Label { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
