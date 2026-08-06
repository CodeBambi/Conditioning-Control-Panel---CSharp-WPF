using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z0 header band + Z1 the Companion Card. See the XAML header for the visual spec.
    ///
    /// <para>This control owns the Companion tab's <b>single ambient loop</b>: the portrait ring
    /// breathing 1.000 ↔ 1.015. The FX plan allows exactly one Forever storyboard per tab, and
    /// this is where it is spent — nothing else on the page may add another.</para>
    ///
    /// <para>The loop is parked in three situations, so a hidden or sleeping hero is never still
    /// burning a composition clock: on unload, while <see cref="ICompanionHeroCardVm.IsCompanionEnabled"/>
    /// is false (the mockup's <c>animation:none</c> asleep state), and whenever the viewmodel is
    /// swapped out.</para>
    /// </summary>
    public partial class CompanionHeroCard : UserControl
    {
        private Storyboard? _breathe;
        private ICompanionHeroCardVm? _observed;

        public CompanionHeroCard()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public ICompanionHeroCardVm? ViewModel
        {
            get => DataContext as ICompanionHeroCardVm;
            set => DataContext = value;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Known Issues #2: never animate before the element is loaded and templated.
            if (!IsLoaded) return;

            Observe(ViewModel);

            // DispatcherPriority.Normal, never Loaded — Loaded is starved in this app and the
            // breathe would silently never start.
            Dispatcher.BeginInvoke(new Action(RefreshAmbientState), DispatcherPriority.Normal);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Observe(null);
            StopAmbientLoop();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Observe(ViewModel);
            RefreshAmbientState();
        }

        /// <summary>
        /// Subscribes to the live viewmodel so the asleep state can park the loop, and — more
        /// importantly — unsubscribes from the previous one. A hero that is re-pointed at a new
        /// companion must not leave a handler rooted in the old viewmodel.
        /// </summary>
        private void Observe(ICompanionHeroCardVm? vm)
        {
            if (ReferenceEquals(_observed, vm)) return;

            if (_observed != null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
            _observed = vm;
            if (_observed != null) _observed.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.PropertyName) &&
                !string.Equals(e.PropertyName, nameof(ICompanionHeroCardVm.IsCompanionEnabled), StringComparison.Ordinal))
                return;

            RefreshAmbientState();
        }

        /// <summary>Starts or parks the breathe to match the current state. Safe to call any time.</summary>
        public void RefreshAmbientState()
        {
            if (IsLoaded && ViewModel?.IsCompanionEnabled != false) StartAmbientLoop();
            else StopAmbientLoop();
        }

        /// <summary>Starts (or restarts) the portrait breathe. Idempotent.</summary>
        public void StartAmbientLoop()
        {
            try
            {
                if (!IsLoaded || _breathe != null) return;
                if (TryFindResource("CmpPortraitBreatheStoryboard") is not Storyboard proto) return;

                var sb = proto.Clone();
                foreach (var tl in sb.Children)
                {
                    if (tl is DoubleAnimation da) Storyboard.SetTarget(da, PortraitRingScale);
                }
                sb.Begin(this, isControllable: true);
                _breathe = sb;
            }
            catch (InvalidOperationException)
            {
                // A failed decorative animation must never take the tab down with it.
                _breathe = null;
            }
        }

        /// <summary>Stops the ambient loop and releases the clock.</summary>
        public void StopAmbientLoop()
        {
            try
            {
                _breathe?.Stop(this);
            }
            catch (InvalidOperationException) { /* already torn down */ }
            finally
            {
                _breathe = null;
            }
        }
    }
}
