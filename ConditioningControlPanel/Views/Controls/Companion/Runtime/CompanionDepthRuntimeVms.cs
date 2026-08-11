using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    // =====================================================================================
    //  Z4 · Z5 · Z6 — the three right-column cards, wired to what exists today.
    // =====================================================================================

    /// <summary>
    /// Z4 — Make her yours.
    ///
    /// <para>Never gated: the interview is the conversion surface, so a free user gets all of it.
    /// The interview and the trait dashboard are Train 3, so those two sit in their designed
    /// sleeping states; everything else on the card is live today.</para>
    ///
    /// <para>The preset chips are the headline win here — the seven presets were previously
    /// reachable only from the avatar's right-click menu, and this is the first time they are
    /// discoverable. They activate through MainWindow so the explicit-content acknowledgement gate
    /// runs exactly as it does in that menu; a second activation path that skipped it would be a
    /// compliance hole, not a shortcut.</para>
    /// </summary>
    internal sealed class MakeHerYoursRuntimeVm : CompanionObservable, IMakeHerYoursVm
    {
        private readonly CompanionRuntimeContext _ctx;

        /// <summary>
        /// Observable, not a plain list. <see cref="RebuildPresets"/> refills it in place, and WPF
        /// suppresses an <c>ItemsSource</c> change whose new value is reference-equal to the old one,
        /// so a PropertyChanged raise alone left the WrapPanel rendering the chip objects built in
        /// the constructor for the lifetime of the tab. The chips are ungrouped ToggleButtons bound
        /// TwoWay to <c>IsSelected</c>, so a stale row lights every preset the user ever clicked —
        /// and a cancelled explicit-content acknowledgement reported a preset as active that
        /// <c>App.Personality</c> never switched to, which is the one thing
        /// <see cref="OnChipChanged"/>'s read-back exists to prevent.
        /// </summary>
        private readonly ObservableCollection<IPresetChipVm> _presets = new();

        private bool _isSpiceOn;
        private string _activeLine = string.Empty;
        private bool _canReset;
        private bool _suppressChipEcho;

        public MakeHerYoursRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;

            StartInterviewCommand = new CompanionRelayCommand(() => { }, () => false);
            OpenTraitDashboardCommand = new CompanionRelayCommand(OpenPromptEditor);
            ViewCompiledPromptCommand = new CompanionRelayCommand(OpenPromptEditor);
            ForkPromptCommand = new CompanionRelayCommand(OpenPromptEditor);
            CommunityPromptsCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => w.BtnBrowsePrompts_Click(this, CompanionRuntimeContext.Routed())));
            ResetPersonalityCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w =>
                {
                    w.BtnDeactivatePrompt_Click(this, CompanionRuntimeContext.Routed());
                    Sync();
                }));

            Sync();
        }

        // ---- interview: Train 3 ----
        public bool IsInterviewAvailable => false;
        public bool IsInterviewed => false;
        public string InterviewTitle => Loc.Get("companion_personality_interview_title");
        public string InterviewBody => Loc.Get("companion_personality_interview_body_1");
        public string InterviewCtaLabel => Loc.Get("companion_personality_interview_cta");
        public string InterviewedLine => string.Empty;
        public string InterviewDormantCopy => Loc.Get("companion_personality_interview_dormant");

        // ---- trait glance: Train 3 ----
        public bool AreTraitsAvailable => false;
        public IReadOnlyList<ITraitGaugeVm> Traits { get; } = Array.Empty<ITraitGaugeVm>();
        public IReadOnlyList<string> TraitChips { get; } = Array.Empty<string>();

        public IReadOnlyList<IPresetChipVm> Presets => _presets;

        // ---- spice ----
        public bool IsSpiceOn
        {
            get => _isSpiceOn;
            set
            {
                if (_isSpiceOn == value) return;
                // The gate can refuse (the acknowledgement dialog is cancellable), so the field is
                // NOT set from the setter — MainWindow decides, and Sync reads back what actually
                // happened. A toggle that reports a state the settings file disagrees with is how a
                // content gate gets bypassed by a UI bug.
                _ctx.WithWindow(w => w.SetSlutMode(value));
                Sync();
                Raise(nameof(IsSpiceOn));
            }
        }

        public string SpiceTitle => Loc.Get("companion_personality_spice_title");
        public string SpiceSubtitle => Loc.Get("companion_personality_spice_subtitle");

        // ---- readout ----
        public string ActivePersonalityLine { get => _activeLine; private set => Set(ref _activeLine, value); }
        public bool CanResetPersonality { get => _canReset; private set => Set(ref _canReset, value); }
        public string ResetLabel => Loc.Get("companion_personality_reset");

        public ICommand StartInterviewCommand { get; }
        public ICommand OpenTraitDashboardCommand { get; }
        public ICommand ResetPersonalityCommand { get; }
        public ICommand ViewCompiledPromptCommand { get; }
        public ICommand ForkPromptCommand { get; }
        public ICommand CommunityPromptsCommand { get; }

        /// <summary>Re-reads the preset list, the active readout and the spice switch.</summary>
        public void Sync() => CompanionRuntimeContext.Guarded(SyncCore, "personality sync");

        private void SyncCore()
        {
            var settings = App.Settings?.Current;
            _isSpiceOn = settings?.SlutModeEnabled == true;
            Raise(nameof(IsSpiceOn));

            RebuildPresets();

            // Absorbs TxtActivePromptName + BtnDeactivatePrompt from the old Phrases accordion.
            var communityId = settings?.ActiveCommunityPromptId;
            if (!string.IsNullOrEmpty(communityId))
            {
                var prompt = App.CommunityPrompts?.GetInstalledPrompt(communityId);
                ActivePersonalityLine = Loc.GetF(
                    "companion_personality_active_custom_fmt", prompt?.Name ?? communityId!);
                CanResetPersonality = true;
                return;
            }

            if (settings?.CompanionPrompt?.UseCustomPrompt == true)
            {
                ActivePersonalityLine = Loc.GetF(
                    "companion_personality_active_custom_fmt",
                    Localization.Loc.Get("label_custom_edited"));
                CanResetPersonality = false;
                return;
            }

            var active = App.Personality?.GetActivePreset();
            var name = active == null
                ? string.Empty
                : App.Mods?.GetPersonalityDisplayName(active.Name) ?? active.Name;
            ActivePersonalityLine = Loc.GetF("companion_personality_active_preset_fmt", name);
            CanResetPersonality = false;
        }

        /// <summary>
        /// Re-projects the preset row.
        ///
        /// <para>Chip objects are kept across a Sync whose preset SET is unchanged and only their
        /// selection is rewritten. That is not a micro-optimisation: the common Sync is the one
        /// <see cref="OnChipChanged"/> fires from inside the ToggleButton's own <c>IsChecked</c>
        /// write, and tearing the container down under it is the sort of reentrancy that turns a
        /// click into a crash. A genuinely different set — a community prompt installed, a mod
        /// swapped — replaces the chips, unsubscribing the ones being discarded so the row cannot
        /// accumulate dead handlers.</para>
        /// </summary>
        private void RebuildPresets()
        {
            var all = App.Personality?.GetAllPresets() ?? new List<PersonalityPreset>();
            var activeId = App.Personality?.GetActivePreset()?.Id;

            var wanted = new List<(string Id, string Label, bool Selected)>();
            foreach (var preset in all)
            {
                if (preset == null || string.IsNullOrEmpty(preset.Id)) continue;
                var label = App.Mods?.GetPersonalityDisplayName(preset.Name) ?? preset.Name;
                wanted.Add((preset.Id, label,
                    string.Equals(preset.Id, activeId, StringComparison.Ordinal)));
            }

            // Writing IsSelected here is us REPORTING what App.Personality did. Letting it round-trip
            // back through OnChipChanged would re-enter ActivatePersonalityPreset and re-open the
            // acknowledgement dialog the user may have just cancelled.
            var previousEcho = _suppressChipEcho;
            _suppressChipEcho = true;
            try
            {
                if (SameChipSet(wanted))
                {
                    for (int i = 0; i < wanted.Count; i++) _presets[i].IsSelected = wanted[i].Selected;
                    return;
                }

                foreach (var stale in _presets) stale.PropertyChanged -= OnChipChanged;
                _presets.Clear();
                foreach (var (id, label, selected) in wanted)
                {
                    var chip = new CompanionPresetChip(id, label, selected);
                    chip.PropertyChanged += OnChipChanged;
                    _presets.Add(chip);
                }
            }
            finally
            {
                _suppressChipEcho = previousEcho;
            }
        }

        /// <summary>True when the live chips already carry exactly these ids and labels, in order.</summary>
        private bool SameChipSet(List<(string Id, string Label, bool Selected)> wanted)
        {
            if (wanted.Count != _presets.Count) return false;
            for (int i = 0; i < wanted.Count; i++)
            {
                if (!string.Equals(_presets[i].Id, wanted[i].Id, StringComparison.Ordinal)) return false;
                if (!string.Equals(_presets[i].Label, wanted[i].Label, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private void OnChipChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_suppressChipEcho) return;
            if (e.PropertyName != nameof(IPresetChipVm.IsSelected)) return;
            if (sender is not IPresetChipVm chip || !chip.IsSelected) return;

            _suppressChipEcho = true;
            try
            {
                _ctx.WithWindow(w => w.ActivatePersonalityPreset(chip.Id));
            }
            finally
            {
                _suppressChipEcho = false;
            }
            // Reads back what actually happened: a cancelled acknowledgement leaves the old preset
            // active, and the chip row has to show that rather than the click.
            Sync();
        }

        private void OpenPromptEditor() =>
            _ctx.WithWindow(w =>
            {
                w.BtnCustomizeCompanion_Click(this, CompanionRuntimeContext.Routed());
                Sync();
            });
    }

    // Z5 — What she can see moved to its own file (Runtime/AwarenessPrivacyRuntimeVm.cs) when
    // Train 2 made it a real surface: a live wire view, two list editors, the seen-app and
    // known-app rows, retention, pause and the wipe do not belong in a file of small cards.

    /// <summary>
    /// Z6 — Her attention.
    ///
    /// <para>Train 1 has no daily token budget: the server still speaks in <c>requests_remaining</c>
    /// and the client mirrors it as a request COUNT. So the gauge is bound to that count, which
    /// makes it truthful today, and the in-voice copy ladder already avoids the word "tokens"
    /// entirely — it counts chats. When doc 01 §5.4's token budget lands, only
    /// <see cref="ReadBudget"/> changes.</para>
    ///
    /// <para>An unlimited provider (local Ollama, or a BYO endpoint with no cap set) has no meter to
    /// draw. The card reports full and says so in the detail line rather than inventing a ration for
    /// a model running on the user's own machine.</para>
    /// </summary>
    internal sealed class AttentionGaugeRuntimeVm : CompanionObservable, IAttentionGaugeVm
    {
        private readonly CompanionRuntimeContext _ctx;

        private double _fraction = 1.0;
        private int _remaining;
        private bool _unlimited;
        private bool _isDetailShown;

        public AttentionGaugeRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;
            UpsellCommand = new CompanionRelayCommand(() => _ctx.WithWindow(w => w.ShowTab("patreon")));
            ToggleDetailCommand = new CompanionRelayCommand(() => IsDetailShown = !IsDetailShown);
            Sync();
        }

        public double Fraction
        {
            get => _fraction;
            private set
            {
                if (!Set(ref _fraction, value)) return;
                Raise(nameof(BarFraction));
                Raise(nameof(IsSpent));
                Raise(nameof(StateCopy));
                Raise(nameof(DetailLine));
                Raise(nameof(FloorNote));
                Raise(nameof(ShowFloorNote));
                Raise(nameof(ShowUpsell));
            }
        }

        public double BarFraction => AttentionCopy.BarFractionFor(Fraction);
        public bool IsSpent => AttentionCopy.IsSpent(Fraction);
        public string StateCopy => Loc.Get(AttentionCopy.CopyKeyFor(Fraction));

        public string DetailLine => _unlimited
            ? Loc.Get("companion_attention_detail_unlimited")
            : Loc.GetF("companion_attention_detail_fmt", _remaining);

        public string FloorNote => Loc.Get("companion_attention_floor_note");
        public bool ShowFloorNote => AttentionCopy.ShowFloorNote(Fraction);

        public bool IsDetailShown
        {
            get => _isDetailShown;
            set => Set(ref _isDetailShown, value);
        }

        public bool ShowUpsell => !_unlimited && AttentionCopy.ShowUpsell(Fraction)
                                  && App.Patreon?.HasAiAccess != true;

        public string UpsellCopy => Loc.Get("companion_attention_upsell");

        public ICommand UpsellCommand { get; }
        public ICommand ToggleDetailCommand { get; }

        /// <summary>Re-reads the request mirror.</summary>
        public void Sync() => CompanionRuntimeContext.Guarded(() =>
        {
            var (remaining, limit) = ReadBudget();
            _remaining = remaining;
            _unlimited = limit <= 0;
            Fraction = _unlimited ? 1.0 : FractionFor(remaining, limit);
        }, "attention sync");

        /// <summary>0..1, clamped. Its own function so the gauge's arithmetic is testable.</summary>
        internal static double FractionFor(int remaining, int limit)
        {
            if (limit <= 0) return 1.0;
            double f = (double)remaining / limit;
            return f < 0 ? 0 : (f > 1 ? 1 : f);
        }

        /// <summary>
        /// The client mirror of <c>requests_remaining</c>, plus the ceiling it is measured against.
        /// A non-positive ceiling means "unlimited" — the local provider reports -1 remaining, and a
        /// BYO endpoint with <c>DailyRequestLimit == 0</c> has no cap at all.
        /// </summary>
        private static (int Remaining, int Limit) ReadBudget()
        {
            int remaining = App.Ai?.DailyRequestsRemaining ?? 0;
            if (remaining < 0) return (0, 0);

            var settings = App.Settings?.Current;
            var provider = settings?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud;
            return provider switch
            {
                AiProviderType.Local => (0, 0),
                AiProviderType.OpenAiCompatible => (remaining, settings?.CompanionPrompt?.DailyRequestLimit ?? 0),
                _ => (remaining, Services.AiService.EffectiveDailyLimit)
            };
        }
    }
}
