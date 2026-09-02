using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z0 header band + Z1 the Companion Card. See the XAML header for the visual spec.
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionHeroCard.xaml.cs.
    ///
    /// <para>This control owns the Companion tab's <b>single ambient loop</b>: the portrait ring
    /// breathing 1.000 ↔ 1.015. The FX plan allows exactly one forever animation per tab, and
    /// this is where it is spent — nothing else on the page may add another.</para>
    ///
    /// <para>The loop is parked in three situations, so a hidden or sleeping hero is never still
    /// burning a composition clock: on unload, while <see cref="CompanionHeroCardViewModel.IsCompanionEnabled"/>
    /// is false (the mockup's <c>animation:none</c> asleep state), and whenever the viewmodel is
    /// swapped out.</para>
    ///
    /// <para>The WPF original also owns the portrait's optical centring (a pixel probe of the
    /// bust's opaque bounds) and its mod repaint (<c>App.Mods.ModChanged</c>). Both are stubbed
    /// here — see <see cref="ApplyAvatarArt"/> and <see cref="CentrePortrait"/>.</para>
    /// </summary>
    public partial class CompanionHeroCard : UserControl
    {
        private CompanionHeroCardViewModel? _observed;

        public CompanionHeroCard()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new CompanionHeroCardViewModel();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public CompanionHeroCardViewModel? ViewModel
        {
            get => DataContext as CompanionHeroCardViewModel;
            set => DataContext = value;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            Observe(ViewModel);

            // ponytail: needs App.Mods (ModChanged -> ApplyAvatarArt), wired when it moves to Core.

            // DispatcherPriority.Normal, never Loaded — Loaded is starved in this app and the
            // breathe would silently never start.
            Dispatcher.UIThread.Post(RefreshAmbientState, DispatcherPriority.Normal);
            Dispatcher.UIThread.Post(ApplyAvatarArt, DispatcherPriority.Normal);
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            Observe(null);
            StopAmbientLoop();
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            Observe(ViewModel);
            RefreshAmbientState();
            CentrePortrait();
        }

        /// <summary>
        /// Subscribes to the live viewmodel so the asleep state can park the loop, and — more
        /// importantly — unsubscribes from the previous one. A hero that is re-pointed at a new
        /// companion must not leave a handler rooted in the old viewmodel.
        /// </summary>
        private void Observe(CompanionHeroCardViewModel? vm)
        {
            if (ReferenceEquals(_observed, vm)) return;

            if (_observed != null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
            _observed = vm;
            if (_observed != null) _observed.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            bool all = string.IsNullOrEmpty(name);

            if (all || string.Equals(name, nameof(CompanionHeroCardViewModel.Portrait), StringComparison.Ordinal))
                CentrePortrait();

            if (all || string.Equals(name, nameof(CompanionHeroCardViewModel.IsCompanionEnabled), StringComparison.Ordinal))
                RefreshAmbientState();
        }

        /// <summary>Starts or parks the breathe to match the current state. Safe to call any time.</summary>
        public void RefreshAmbientState()
        {
            if (IsLoaded && ViewModel?.IsCompanionEnabled != false) StartAmbientLoop();
            else StopAmbientLoop();
        }

        /// <summary>
        /// Starts (or restarts) the portrait breathe. Idempotent. The animation itself is the
        /// <c>Ellipse.ring.breathe</c> style in the XAML (CmpPortraitBreatheStoryboard's numbers);
        /// the class is the clock.
        /// </summary>
        public void StartAmbientLoop()
        {
            if (!IsLoaded) return;
            this.FindControl<Ellipse>("PortraitRing")?.Classes.Add("breathe");
        }

        /// <summary>Stops the ambient loop and releases the clock.</summary>
        public void StopAmbientLoop()
            => this.FindControl<Ellipse>("PortraitRing")?.Classes.Remove("breathe");

        // =====================================================================================
        //  the portrait: mod repaint + optical centring
        // =====================================================================================

        /// <summary>
        /// Re-reads her bust and re-centres it in the ring. The WPF version calls
        /// <c>CompanionHeroRuntimeVm.Sync()</c> first (which resolves the active mod's pose-1
        /// through <c>ModResourceResolver</c>).
        /// </summary>
        internal void ApplyAvatarArt()
        {
            // ponytail: needs CompanionHeroRuntimeVm / ModResourceResolver, wired when they move to Core.
            CentrePortrait();
        }

        /// <summary>
        /// Points the portrait brush at a square centred on the art's own opaque bounds.
        /// </summary>
        private void CentrePortrait()
        {
            // ponytail: the WPF ink probe (OpaqueBounds/InkViewbox, PortraitInkFill=0.86,
            // alpha floor 8, 96px probe) reads BitmapSource pixels. On Avalonia it is
            // Bitmap.CopyPixels into ImageBrush.SourceRect; port it with the first real bust —
            // the placeholder viewmodel ships Portrait=null and the vector disc needs no centring.
        }
    }

    /// <summary>
    /// The view's data contract, in one concrete class: compiled bindings need one, and the WPF
    /// <c>ICompanionHeroCardVm</c> / <c>MockCompanionHeroCardVm</c> live in the head and cannot
    /// cross. Seeded with the mock's <c>Default()</c> exhibit: AI live, awareness on, mood token
    /// still dormant (pre-Train 4), header entitled. Every string comes through CCP.Core's
    /// <see cref="Loc"/> by the key the WPF mock/runtime uses, so a missing key shows as itself.
    /// </summary>
    public sealed class CompanionHeroCardViewModel : INotifyPropertyChanged
    {
        private bool _isMuted;
        private bool _isCompanionShown = true;

        public CompanionHeroCardViewModel()
        {
            ChatCommand = new RelayCommand(() => { });
            SwitchCommand = new RelayCommand(() => { });
            DetachCommand = new RelayCommand(() => { });
            ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
            ToggleShownCommand = new RelayCommand(() => IsCompanionShown = !IsCompanionShown);
            OpenEngineRoomCommand = new RelayCommand(() => { });
            FocusAwarenessCommand = new RelayCommand(() => { });
            WakeCommand = new RelayCommand(() => { });
            Header = new CompanionHeaderViewModel();
            // ponytail: every command above is a no-op; they deep-link into the Companion room
            // (Engine Room, Workshop roster, awareness cell, chat) which is head-owned navigation
            // with no Avalonia counterpart yet.
        }

        // ---- identity ----
        public string Name { get; init; } = "Bambi";
        public string ModName { get; init; } = "BAMBI SLEEP";
        public string Flavor { get; init; } = "Gains bonus XP from Pink Filter intensity. Currently plotting something.";
        /// <summary>Companion bust. Null renders the gradient placeholder disc.</summary>
        public IImage? Portrait { get; init; }

        // ---- state ----
        public bool IsCompanionEnabled { get; init; } = true;
        public bool IsAiLive { get; init; } = true;
        public bool IsAiLocked { get; init; }
        public bool IsAwarenessOpen { get; init; } = true;
        public string AiPillText { get; init; } = Loc.Get("companion_hero_pill_ai_cloud");
        public string AwarenessPillText { get; init; } = Loc.Get("companion_hero_pill_eyes_broad");
        public string AsleepCopy { get; init; } = Loc.Get("companion_hero_asleep_copy");

        // ---- daily mood token (Train 4) ----
        public bool IsMoodLive { get; init; }
        public string MoodGlyph { get; init; } = "✧";
        public string MoodWord { get; init; } = Loc.Get("companion_hero_mood_asleep");
        public string MoodCaption { get; init; } = Loc.Get("companion_hero_mood_caption_dormant");

        // ---- progression (placeholder numbers = the WPF mock's artboard) ----
        public int Level { get; init; } = 41;
        public double XpFraction { get; init; } = 0.62;
        /// <summary>Interpolated in the runtime VM too, never a loc key.</summary>
        public string XpLabel { get; init; } = "341 / 550 XP";
        public string NextLevelLabel { get; init; } = Loc.GetF("companion_hero_next_level_fmt", 42);

        // ---- quick actions ----
        public string ChatShortcutHint { get; init; } = "Ctrl+T";

        public bool IsMuted
        {
            get => _isMuted;
            set => Set(ref _isMuted, value);
        }

        public bool IsCompanionShown
        {
            get => _isCompanionShown;
            set => Set(ref _isCompanionShown, value);
        }

        public ICommand ChatCommand { get; }
        public ICommand SwitchCommand { get; }
        public ICommand DetachCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleShownCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }
        public ICommand FocusAwarenessCommand { get; }
        public ICommand WakeCommand { get; }

        /// <summary>Z0 band. Null collapses it, for a host that draws its own page header.</summary>
        public CompanionHeaderViewModel? Header { get; init; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Z0 — the header band: title, subtitle, tutorial chip and the AI-entitlement plate. The
    /// shape of the WPF <c>ICompanionHeaderVm</c>, seeded from <c>MockCompanionHeaderVm.Entitled()</c>.
    /// </summary>
    public sealed class CompanionHeaderViewModel : INotifyPropertyChanged
    {
        public CompanionHeaderViewModel()
        {
            TutorialCommand = new RelayCommand(() => { });
            OpenPatreonCommand = new RelayCommand(() => { });
            // ponytail: no-ops — the tutorial and the Patreon tab are head-owned navigation.
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

        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    /// <summary>The one command shape these placeholders need: run a delegate, always enabled.</summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action _run;
        public RelayCommand(Action run) => _run = run;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
