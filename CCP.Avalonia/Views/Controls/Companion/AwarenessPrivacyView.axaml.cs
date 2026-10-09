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
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
    /// <para>The 1.5 s refresh (WPF <c>RefreshInterval</c>) re-reads the live viewmodel only while
    /// the card is on screen: started on load when visible and by the room's ResumeClocks, stopped
    /// by ParkClocks and on unload (P01).</para>
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
            DataContext = new AwarenessPrivacyViewModel(
                () => TopLevel.GetTopLevel(this) as MainShellWindow,
                () => this.FindAncestorOfType<CompanionRoomView>()?.RevealWorkshop(CompanionRoomAnchors.WorkshopAwarenessCell));
            Loaded += OnLoaded;
            DataContextChanged += (_, _) =>
            {
                Observe(ViewModel);
                WipeConfirm.Bind(ViewModel?.WipeCommand);
            };
            Unloaded += (_, _) =>
            {
                StopCursorBlink();
                StopRefresh();
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
            if (IsEffectivelyVisible) StartRefresh();
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

        /// <summary>WPF RefreshInterval: the observer's own poll, so the readout never lags it.</summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1500);
        private DispatcherTimer? _refresh;
        internal bool IsRefreshing => _refresh != null;

        /// <summary>Arms the visible-only re-read. Idempotent.</summary>
        public void StartRefresh()
        {
            ViewModel?.Sync();
            if (_refresh != null) return;
            _refresh = new DispatcherTimer(RefreshInterval, DispatcherPriority.Normal, (_, _) => ViewModel?.Sync());
            _refresh.Start();
        }

        /// <summary>Disarms the refresh. Safe when it was never started.</summary>
        public void StopRefresh()
        {
            _refresh?.Stop();
            _refresh = null;
        }
    }

    /// <summary>Mirror of the head's dial enum, <c>CompanionVmPrimitives.cs:AwarenessIntensity</c>.
    /// NOT <c>Services/Awareness/AwarenessIntensity.cs</c>, which is a different enum
    /// (Off/Subtle/Chatty/Unhinged) with the same name.</summary>
    // ponytail: local twin of the enum in ConditioningControlPanel/Views/Controls/Companion/
    // CompanionVmPrimitives.cs (the WPF file, not this head's same-named one, which does not carry
    // it). Delete when that enum reaches Core.
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
