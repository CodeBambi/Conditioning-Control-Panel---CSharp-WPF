using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z5 — What she can see. See the XAML header for the visual spec.
    ///
    /// <para>Ported from the WPF code-behind. The dial itself is entirely declarative; this file
    /// only runs the two decorative clocks - the wire cursor blink (alive only while the frame is
    /// live) and the dormant block's one-shot shimmer - and both stop on unload so a hidden tab
    /// is not still animating.</para>
    ///
    /// <para>Not ported: the 1.5 s refresh timer. It existed to call
    /// <c>AwarenessPrivacyRuntimeVm.Sync()</c>, and that runtime viewmodel reads the awareness
    /// service, which is still in the WPF head. The mock exhibits are static, so a timer here
    /// would tick at nothing.</para>
    /// </summary>
    public partial class AwarenessPrivacyView : UserControl
    {
        /// <summary>The WPF storyboard's 1.2 s cycle: 0.6 s on, 0.6 s off.</summary>
        private static readonly TimeSpan BlinkHalfPeriod = TimeSpan.FromMilliseconds(600);

        private DispatcherTimer? _blink;
        private bool _introPlayed;
        private AwarenessPrivacyViewModel? _observed;

        public AwarenessPrivacyView()
        {
            // Before Load: the $parent[...].WipeConfirm bindings read it once at parse time and
            // WipeConfirm never raises a change, so a later assignment leaves them bound to null.
            WipeConfirm = new MemoryForgetConfirm();
            AvaloniaXamlLoader.Load(this);
            DataContext = AwarenessPrivacyViewModel.Live(this);
            Loaded += OnLoaded;
            DataContextChanged += (_, _) =>
            {
                Observe(ViewModel);
                WipeConfirm.Bind(ViewModel?.WipeCommand);
            };
            Unloaded += (_, _) =>
            {
                StopCursorBlink();
                WipeConfirm.Disarm();
                Observe(null);
            };
        }

        /// <summary>
        /// The wipe's two-step, in the same inline shape the memory diary uses: the destructive command
        /// runs only from <c>ConfirmCommand</c>, only while armed, and re-binding always disarms. This
        /// erases everything she has noticed, so it may never be one click.
        /// </summary>
        public MemoryForgetConfirm WipeConfirm { get; }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public AwarenessPrivacyViewModel? ViewModel
        {
            get => DataContext as AwarenessPrivacyViewModel;
            set => DataContext = value;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            Observe(ViewModel);
            WipeConfirm.Bind(ViewModel?.WipeCommand);
            // Normal, never Loaded — DispatcherPriority.Loaded is starved in this app.
            Dispatcher.UIThread.Post(() =>
            {
                SyncCursorBlink();
                if (!_introPlayed) { _introPlayed = true; PlayIntro(); }
            }, DispatcherPriority.Normal);
        }

        /// <summary>
        /// Follows <see cref="AwarenessPrivacyViewModel.IsWireLive"/>. The dial is a live control:
        /// turning her eyes on after the card has loaded has to start the cursor, and turning them
        /// off has to stop it, or the blink is decided once at load and then lies.
        /// </summary>
        private void Observe(AwarenessPrivacyViewModel? vm)
        {
            if (ReferenceEquals(_observed, vm)) return;
            if (_observed != null) _observed.PropertyChanged -= OnVmPropertyChanged;
            _observed = vm;
            if (_observed != null) _observed.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AwarenessPrivacyViewModel.IsWireLive)) return;
            Dispatcher.UIThread.Post(SyncCursorBlink, DispatcherPriority.Normal);
        }

        /// <summary>
        /// Starts or stops the blink to match the viewmodel. Public because the room calls it when
        /// the tab becomes visible again — this app hides tabs rather than unloading them.
        /// </summary>
        public void SyncCursorBlink()
        {
            if (ViewModel?.IsWireLive ?? false) StartCursorBlink();
            else StopCursorBlink();
        }

        /// <summary>Starts the wire cursor blink. Idempotent; a no-op when the frame is not live.</summary>
        public void StartCursorBlink()
        {
            if (!IsLoaded || _blink != null) return;
            var cursor = this.FindControl<Rectangle>("WireCursor");
            if (cursor is null) return;

            // WPF flipped Visibility Visible/Hidden; Hidden keeps layout, which Opacity does here
            // while IsVisible stays bound to the viewmodel.
            _blink = new DispatcherTimer(BlinkHalfPeriod, DispatcherPriority.Normal,
                (_, _) => cursor.Opacity = cursor.Opacity > 0.5 ? 0 : 1);
            _blink.Start();
        }

        /// <summary>Stops the cursor blink and leaves the cursor visible.</summary>
        public void StopCursorBlink()
        {
            _blink?.Stop();
            _blink = null;
            var cursor = this.FindControl<Rectangle>("WireCursor");
            if (cursor is not null) cursor.Opacity = 1;
        }

        /// <summary>Sweeps the dormant block's shimmer once. No-op when Train 2 is live.</summary>
        public void PlayIntro()
        {
            if (!IsLoaded) return;
            if (!(ViewModel?.IsDormant ?? false)) return;
            var host = this.FindControl<Border>("DormantHost");
            var shimmer = this.FindControl<Border>("DormantShimmer");
            if (host is null || shimmer is null) return;

            // x:Name is illegal on a Transform in Avalonia, so the shift is reached through the group.
            if (shimmer.RenderTransform is not TransformGroup group) return;
            var shift = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (shift is null) return;

            // Same sweep as MakeHerYoursView: park with no transition, then attach and set the end
            // value so it runs once. One-time Bounds read at Loaded — a value, not a binding.
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
            shift.X = host.Bounds.Width > 1 ? host.Bounds.Width + 90 : 420;
        }

        /// <summary>
        /// The one command on this card that needs a window: the per-app title allow list opens the
        /// already-ported picker. The list it hands back is applied to the viewmodel's exhibit
        /// only; nothing here writes settings.
        /// </summary>
        internal async void OpenAllowPicker()
        {
            var vm = ViewModel;
            if (vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
            var listed = vm.TitleAllowList.Select(c => c.Label).ToList();
            var candidates = vm.SeenApps.Select(c => c.Label).ToList();
            var dialog = new AwarenessAppPickerDialog(AwarenessListKind.TitleAllow, listed, candidates);
            await dialog.ShowDialog(owner);
            if (dialog.Result is { } picked) vm.SetTitleAllowList(picked);
        }
    }

    /// <summary>Mirror of the head's dial enum, <c>CompanionVmPrimitives.cs:AwarenessIntensity</c>.
    /// NOT <c>Services/Awareness/AwarenessIntensity.cs</c>, which is a different enum
    /// (Off/Subtle/Chatty/Unhinged) with the same name.</summary>
    // ponytail: local twin; delete when CompanionVmPrimitives.cs moves to Core.
    public enum AwarenessIntensity
    {
        Off,
        BroadStrokes,
        Everything
    }

    /// <summary>
    /// One chip class for all three chip lists. The WPF file has <c>IDenyChipVm</c> (Label,
    /// RemoveCommand) and <c>IAwarenessAppChipVm</c> (Label, ActionTip, ActionCommand); the
    /// DataTemplates only ever read one side, so one type with both faces is the smaller port.
    /// </summary>
    public sealed class AwarenessChip
    {
        public AwarenessChip(string label, ICommand command, string actionTip = "")
        {
            Label = label;
            RemoveCommand = command;
            ActionCommand = command;
            ActionTip = actionTip;
        }

        public string Label { get; }
        public string ActionTip { get; }
        public ICommand RemoveCommand { get; }
        public ICommand ActionCommand { get; }
    }

    /// <summary>
    /// Ported verbatim from the WPF head's <c>MemoryForgetConfirm</c>: a two-step arm/confirm gate
    /// in front of a destructive command.
    /// </summary>
    public sealed class MemoryForgetConfirm : INotifyPropertyChanged
    {
        private ICommand? _target;
        private bool _isArmed;
        private readonly RelayCommand _arm;
        private readonly RelayCommand _confirm;

        public MemoryForgetConfirm()
        {
            _arm = new RelayCommand(Arm, () => CanArm);
            _confirm = new RelayCommand(Confirm, () => IsArmed);
            CancelCommand = new RelayCommand(Disarm);
        }

        public bool IsArmed
        {
            get => _isArmed;
            private set
            {
                if (_isArmed == value) return;
                _isArmed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsArmed)));
                _confirm.RaiseCanExecuteChanged();
            }
        }

        public bool CanArm => _target != null && _target.CanExecute(null);
        public int ConfirmedCount { get; private set; }

        public ICommand ArmCommand => _arm;
        public ICommand ConfirmCommand => _confirm;
        public ICommand CancelCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Bind(ICommand? forgetEverything)
        {
            _target = forgetEverything;
            IsArmed = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanArm)));
            _arm.RaiseCanExecuteChanged();
        }

        public void Disarm() => IsArmed = false;

        private void Arm()
        {
            if (!CanArm) return;
            IsArmed = true;
        }

        private void Confirm()
        {
            if (!IsArmed) return;
            var target = _target;

            // Disarm before executing: the strip disappears on the first click, so a second one
            // lands on the restored footer instead of running the wipe again.
            IsArmed = false;
            if (target == null || !target.CanExecute(null)) return;

            ConfirmedCount++;
            target.Execute(null);
        }
    }

    /// <summary>
    /// The view's data contract, as a concrete type for compiled bindings. The WPF view binds to
    /// <c>IAwarenessPrivacyVm</c>, whose mock and runtime implementations both live in the head
    /// (the runtime one reads the awareness service). This is that interface's shape seeded with
    /// the mock's exhibits; <see cref="Live"/> is what the view shows by default.
    /// </summary>
    public sealed class AwarenessPrivacyViewModel : INotifyPropertyChanged
    {
        private AwarenessIntensity _intensity = AwarenessIntensity.BroadStrokes;
        private bool _allowPageTitles;
        private bool _isJsonExpanded;
        private int _retentionDays = 30;
        private IReadOnlyList<AwarenessChip> _titleAllowList = Array.Empty<AwarenessChip>();

        public AwarenessPrivacyViewModel(AwarenessPrivacyView? view = null)
        {
            // ponytail: chips are static exhibits; the remove/hide/forget commands are no-ops until
            // the awareness ledger moves to Core.
            var noop = new RelayCommand(() => { });
            DenyList = new[]
            {
                new AwarenessChip(Loc.Get("companion_awareness_deny_passwords"), noop),
                new AwarenessChip(Loc.Get("companion_awareness_deny_banking"), noop),
                new AwarenessChip(Loc.Get("companion_awareness_deny_email"), noop)
            };
            SeenApps = new[]
            {
                new AwarenessChip("Chrome", noop, Loc.Get("companion_awareness_seen_tip")),
                new AwarenessChip("Discord", noop, Loc.Get("companion_awareness_seen_tip")),
                new AwarenessChip("Steam", noop, Loc.Get("companion_awareness_seen_tip"))
            };
            KnownApps = new[]
            {
                new AwarenessChip("YouTube", noop, Loc.Get("companion_awareness_forget_tip")),
                new AwarenessChip("Discord", noop, Loc.Get("companion_awareness_forget_tip"))
            };

            AddDenyCommand = noop;            // ponytail: needs the deny-list editor, wired when awareness moves to Core
            AllowPerAppCommand = new RelayCommand(() => view?.OpenAllowPicker());
            ToggleJsonCommand = new RelayCommand(() => IsJsonExpanded = !IsJsonExpanded);
            PauseCommand = noop;              // ponytail: needs the awareness service
            WipeCommand = noop;               // ponytail: needs the awareness ledger
            FineTuningCommand = noop;         // ponytail: needs ICompanionRoomNavigator (workshop deep link)
            ReviewConsentCommand = noop;      // ponytail: needs the v2 consent dialog
        }

        public AwarenessIntensity Intensity
        {
            get => _intensity;
            set { if (Set(ref _intensity, value)) Raise(nameof(DialHint)); }
        }

        public string DialHint => _intensity switch
        {
            AwarenessIntensity.Off => Loc.Get("companion_awareness_dial_hint_off"),
            AwarenessIntensity.BroadStrokes => Loc.Get("companion_awareness_dial_hint_broad"),
            _ => Loc.Get("companion_awareness_dial_hint_everything")
        };

        public bool IsLegacyPipeline { get; init; }

        public string IncognitoCopy => Loc.Get(IsLegacyPipeline
            ? "companion_awareness_incognito_legacy"
            : "companion_awareness_incognito");

        public string LegacyHead => Loc.Get("companion_awareness_legacy_head");
        public string LegacyBody => Loc.Get("companion_awareness_legacy_body");
        public string LegacyAction => Loc.Get("companion_awareness_legacy_action");
        public ICommand ReviewConsentCommand { get; }

        public bool IsEverythingAvailable { get; init; }

        /// <summary>WPF: a DataTrigger sets the tooltip only while the stop is locked.</summary>
        public string? EverythingLockedTip => IsEverythingAvailable
            ? null
            : Loc.Get("companion_awareness_everything_locked_tip");

        public string WireLine { get; init; } = "[ fun · Chrome · 22m ]";
        public bool IsWireLive { get; init; } = true;
        public string WireCaption { get; init; } = Loc.Get("companion_awareness_wire_caption");
        public string DormantCopy { get; init; } = Loc.Get("companion_awareness_dormant_copy");
        public bool IsDormant { get; init; }

        public string WireJson { get; init; } =
            "{\n  \"v\": 1,\n  \"cluster\": \"site_video\",\n  \"app\": \"YouTube\",\n" +
            "  \"visits_today\": 4,\n  \"minutes_today\": 45,\n  \"dwell\": \"15-30m\"\n}";
        public bool HasWireJson => !string.IsNullOrWhiteSpace(WireJson);
        public string WireJsonEmptyCopy { get; init; } = Loc.Get("companion_awareness_wire_json_empty");

        public bool IsJsonExpanded
        {
            get => _isJsonExpanded;
            set { if (Set(ref _isJsonExpanded, value)) Raise(nameof(JsonToggleLabel)); }
        }

        public string JsonToggleLabel => Loc.Get(IsJsonExpanded
            ? "companion_awareness_wire_json_hide"
            : "companion_awareness_wire_json_show");

        public IReadOnlyList<AwarenessChip> DenyList { get; }
        public string AddDenyLabel { get; init; } = Loc.Get("companion_awareness_add_deny");

        public IReadOnlyList<AwarenessChip> TitleAllowList
        {
            get => _titleAllowList;
            private set { if (Set(ref _titleAllowList, value)) Raise(nameof(HasTitleAllowList)); }
        }
        public bool HasTitleAllowList => TitleAllowList.Count > 0;
        public string TitleAllowLabel { get; init; } = Loc.Get("companion_awareness_allow_label");

        public IReadOnlyList<AwarenessChip> SeenApps { get; }
        public bool HasSeenApps => SeenApps.Count > 0;
        public string SeenAppsLabel { get; init; } = Loc.Get("companion_awareness_seen_label");

        public IReadOnlyList<AwarenessChip> KnownApps { get; }
        public bool HasKnownApps => KnownApps.Count > 0;
        public string KnownAppsLabel { get; init; } = Loc.Get("companion_awareness_known_label");

        public bool AllowPageTitles
        {
            get => _allowPageTitles;
            set => Set(ref _allowPageTitles, value);
        }

        public string PageTitlesLabel { get; init; } = Loc.Get("companion_awareness_page_titles_hidden");

        public int RetentionDays
        {
            get => _retentionDays;
            set { if (Set(ref _retentionDays, value)) Raise(nameof(RetentionLabel)); }
        }

        public string RetentionLabel => Loc.GetF("companion_awareness_retention_fmt", RetentionDays);

        public bool IsPaused { get; init; }
        public string PauseLabel => Loc.Get(IsPaused
            ? "companion_awareness_pause_resume"
            : "companion_awareness_pause");

        public string WipeLabel { get; init; } = Loc.Get("companion_awareness_wipe");

        public ICommand AddDenyCommand { get; }
        public ICommand AllowPerAppCommand { get; }
        public ICommand FineTuningCommand { get; }
        public ICommand ToggleJsonCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand WipeCommand { get; }

        /// <summary>What the picker handed back becomes the allow chips, each removable (no-op).</summary>
        public void SetTitleAllowList(IEnumerable<string> apps)
        {
            var noop = new RelayCommand(() => { });
            TitleAllowList = apps.Select(a => new AwarenessChip(a, noop)).ToList();
        }

        // ------------------------------- state exhibits -------------------------------

        public static AwarenessPrivacyViewModel Live(AwarenessPrivacyView? view = null) => new(view)
        {
            IsEverythingAvailable = true
        };

        public static AwarenessPrivacyViewModel Dormant(AwarenessPrivacyView? view = null) => new(view)
        {
            IsEverythingAvailable = false,
            IsDormant = true,
            IsWireLive = false,
            WireLine = "[ her eyes are closed ]",
            WireJson = string.Empty
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    /// <summary>The smallest ICommand: runs a delegate, with an optional CanExecute predicate.</summary>
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action _run;
        private readonly Func<bool>? _can;
        public RelayCommand(Action run, Func<bool>? can = null) { _run = run; _can = can; }
        public bool CanExecute(object? parameter) => _can?.Invoke() ?? true;
        public void Execute(object? parameter) { if (CanExecute(parameter)) _run(); }
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
