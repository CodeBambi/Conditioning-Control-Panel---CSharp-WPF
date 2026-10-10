using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

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
            // WPF MakeHerYoursView ctor: under Companion v2 the Train 3 preview tag and interview card go.
            // The spotlight goes through IsInterviewSpotlightShown: a local IsVisible would lose to its binding.
            if (ConditioningControlPanel.Services.Companion.CompanionExperience.IsV2Enabled)
                this.FindControl<Control>("PreviewTrainTag")!.IsVisible = false;
            DataContext = ViewModel = new MakeHerYoursViewModel(this);
            Loaded += OnLoaded;
        }

        public MakeHerYoursViewModel ViewModel { get; }

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
    /// Z4 over the live personality - port of WPF MakeHerYoursRuntimeVm
    /// (ConditioningControlPanel/Views/Controls/Companion/Runtime/CompanionDepthRuntimeVms.cs:29).
    /// Interview and trait glance are Train 3 and dormant on WPF too. Writes go through the shell
    /// (<see cref="Windows.MainShellWindow.SetSlutModeAsync"/> / ActivatePersonalityPresetAsync) so the
    /// explicit-content acknowledgement gate runs, and every write re-reads what actually happened.
    /// </summary>
    public sealed class MakeHerYoursViewModel : INotifyPropertyChanged
    {
        private readonly global::Avalonia.Visual? _host;
        private readonly ObservableCollection<PresetChip> _presets = new();
        private bool _isSpiceOn;
        private string _activeLine = string.Empty;
        private bool _canReset;
        private bool _suppressChipEcho;

        public MakeHerYoursViewModel(global::Avalonia.Visual? host = null)
        {
            _host = host;
            StartInterviewCommand = new CompanionRelayCommand(() => { }, () => false);
            OpenTraitDashboardCommand = new CompanionRelayCommand(OpenPromptEditor);
            ViewCompiledPromptCommand = new CompanionRelayCommand(OpenPromptEditor);
            ForkPromptCommand = new CompanionRelayCommand(OpenPromptEditor);
            ResetPersonalityCommand = new CompanionRelayCommand(() =>
            {
                // WPF BtnDeactivatePrompt_Click -> CommunityPromptService.DeactivatePrompt.
                PersonalityService.ClearCustomPromptOverride(CoreSettings.Current);
                CoreSettings.Save();
                Sync();
            });
            Sync();
        }

        internal Windows.MainShellWindow? Shell =>
            _host == null ? null : TopLevel.GetTopLevel(_host) as Windows.MainShellWindow;

        // ---- strings the markup used to get from {loc:Str} ----
        public string LocPersonalityTitle => Loc.Get("companion_personality_title");
        public string LocTagTrain3 => Loc.Get("companion_tag_train3");
        public string LocReinterview => Loc.Get("companion_personality_reinterview");
        public string LocAdjust => Loc.Get("companion_personality_adjust");
        public string LocTraitsTip => Loc.Get("companion_personality_traits_tip");
        public string LocViewPrompt => Loc.Get("companion_personality_view_prompt");
        public string LocFork => Loc.Get("companion_personality_fork");
        public string LocCommunity => Loc.Get("companion_personality_community");

        // ---- interview / trait glance: Train 3, dormant (WPF returns false / empty too) ----
        public bool IsInterviewAvailable => false;
        public bool IsInterviewed => false;
        public bool IsInterviewSpotlightShown =>
            !IsInterviewed && !ConditioningControlPanel.Services.Companion.CompanionExperience.IsV2Enabled;
        public string InterviewTitle => Loc.Get("companion_personality_interview_title");
        public string InterviewBody => Loc.Get("companion_personality_interview_body_1");
        public string InterviewCtaLabel => Loc.Get("companion_personality_interview_cta");
        public string InterviewedLine => string.Empty;
        public string InterviewDormantCopy => Loc.Get("companion_personality_interview_dormant");
        public bool AreTraitsAvailable => false;
        public IReadOnlyList<TraitGauge> Traits { get; } = Array.Empty<TraitGauge>();
        public IReadOnlyList<string> TraitChips { get; } = Array.Empty<string>();

        public IReadOnlyList<PresetChip> Presets => _presets;

        // ---- spice ----
        public bool IsSpiceOn
        {
            get => _isSpiceOn;
            set
            {
                if (_isSpiceOn == value) return;
                // Never write settings from the setter: the gate can refuse, and Sync reads back the
                // settings file. Track the toggle's value meanwhile, or the binding drops Sync's
                // read-back as unchanged and the toggle stays on after Cancel.
                _isSpiceOn = value;
                _ = SetSpiceAsync(value);
            }
        }

        private async Task SetSpiceAsync(bool value)
        {
            if (Shell is { } shell) await shell.SetSlutModeAsync(value);
            Sync();
        }

        public string SpiceTitle => Loc.Get("companion_personality_spice_title");
        public string SpiceSubtitle => Loc.Get("companion_personality_spice_subtitle");

        // ---- readout ----
        public string ActivePersonalityLine { get => _activeLine; private set { if (_activeLine == value) return; _activeLine = value; Raise(); } }
        public bool CanResetPersonality { get => _canReset; private set { if (_canReset == value) return; _canReset = value; Raise(); } }
        public string ResetLabel => Loc.Get("companion_personality_reset");

        public ICommand StartInterviewCommand { get; }
        public ICommand OpenTraitDashboardCommand { get; }
        public ICommand ResetPersonalityCommand { get; }
        public ICommand ViewCompiledPromptCommand { get; }
        public ICommand ForkPromptCommand { get; }
        // ponytail: WPF BtnBrowsePrompts_Click opens the community prompt browser
        // (App.CommunityPrompts, WPF-head CommunityPromptService); no browser exists on this head, so the
        // link stays a null command (disabled) rather than a dead button that looks alive.
        public ICommand? CommunityPromptsCommand => null;

        /// <summary>Re-reads settings and the personality service (WPF MakeHerYoursRuntimeVm.SyncCore).</summary>
        public void Sync()
        {
            try { SyncCore(); }
            catch (Exception ex) { Log.Warning(ex, "Companion room: personality sync failed"); }
        }

        private void SyncCore()
        {
            var settings = CoreSettings.Current;
            _isSpiceOn = settings.SlutModeEnabled;
            Raise(nameof(IsSpiceOn));

            RebuildPresets();

            var communityId = settings.ActiveCommunityPromptId;
            if (!string.IsNullOrEmpty(communityId))
            {
                // ponytail: WPF shows the installed prompt's NAME (CommunityPromptService.GetInstalledPrompt,
                // head-only); this head has no installed-prompt store yet, so the id stands in.
                ActivePersonalityLine = Loc.GetF("companion_personality_active_custom_fmt", communityId);
                CanResetPersonality = true;
                return;
            }

            if (settings.CompanionPrompt?.UseCustomPrompt == true)
            {
                ActivePersonalityLine = Loc.GetF("companion_personality_active_custom_fmt", Loc.Get("label_custom_edited"));
                CanResetPersonality = false;
                return;
            }

            var active = PersonalityService.Shared.GetActivePreset();
            var name = active == null ? string.Empty : DisplayName(active.Name);
            ActivePersonalityLine = Loc.GetF("companion_personality_active_preset_fmt", name);
            CanResetPersonality = false;
        }

        private static string DisplayName(string name) => CoreMods.Service?.GetPersonalityDisplayName(name) ?? name;

        private void RebuildPresets()
        {
            var activeId = PersonalityService.Shared.GetActivePreset()?.Id;
            var wanted = new List<PresetChip>();
            foreach (var preset in PersonalityService.Shared.GetAllPresets())
            {
                if (preset == null || string.IsNullOrEmpty(preset.Id)) continue;
                // The preset's own one-liner rides along as the chip's tooltip (a mod writes it).
                var about = string.IsNullOrWhiteSpace(preset.Description) ? null : DisplayName(preset.Description).Trim();
                wanted.Add(new PresetChip(preset.Id, DisplayName(preset.Name),
                    string.Equals(preset.Id, activeId, StringComparison.Ordinal), about));
            }

            // Writing IsSelected here REPORTS what the service did; it must not round-trip into
            // ActivatePersonalityPresetAsync and re-open a gate the user may have just cancelled.
            var previousEcho = _suppressChipEcho;
            _suppressChipEcho = true;
            try
            {
                if (wanted.Count == _presets.Count && wanted.Select((w, i) => w.Id == _presets[i].Id
                        && w.Label == _presets[i].Label && w.Description == _presets[i].Description).All(x => x))
                {
                    for (int i = 0; i < wanted.Count; i++) _presets[i].IsSelected = wanted[i].IsSelected;
                    return;
                }

                foreach (var stale in _presets) stale.PropertyChanged -= OnChipChanged;
                _presets.Clear();
                foreach (var chip in wanted)
                {
                    chip.PropertyChanged += OnChipChanged;
                    _presets.Add(chip);
                }
            }
            finally
            {
                _suppressChipEcho = previousEcho;
            }
        }

        private void OnChipChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressChipEcho || e.PropertyName != nameof(PresetChip.IsSelected)) return;
            if (sender is not PresetChip chip) return;
            if (!chip.IsSelected)
            {
                // A second click on the active chip would read "no preset"; the compiled personality is unchanged.
                Sync();
                return;
            }
            _ = ActivateAsync(chip.Id);
        }

        private async Task ActivateAsync(string id)
        {
            if (Shell is { } shell) await shell.ActivatePersonalityPresetAsync(id);
            // Reads back what happened: a cancelled acknowledgement leaves the old preset active.
            Sync();
        }

        private async void OpenPromptEditor()
        {
            try
            {
                if (Shell is { } shell) await shell.OpenCompanionPromptEditorAsync();
            }
            catch (Exception ex) { Log.Warning(ex, "Companion room: prompt editor failed"); }
            Sync();
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

        public PresetChip(string id, string label, bool selected = false, string? description = null)
        {
            Id = id;
            Label = label;
            _isSelected = selected;
            Description = description;
        }

        public string Id { get; }
        public string Label { get; }
        /// <summary>The preset's own one-liner - the chip's tooltip, off when null (WPF DataTrigger).</summary>
        public string? Description { get; }
        public bool HasDescription => Description != null;

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
