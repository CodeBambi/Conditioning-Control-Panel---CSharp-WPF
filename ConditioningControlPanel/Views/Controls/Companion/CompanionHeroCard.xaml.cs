using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z1 — the Companion Card. See the XAML header for the visual spec.
    ///
    /// <para>This control owns the Companion tab's <b>single ambient loop</b>: the portrait ring
    /// breathing 1.000 ↔ 1.015. The FX plan allows exactly one Forever storyboard per tab, and
    /// this is where it is spent — nothing else on the page may add another. The loop stops on
    /// unload so a detached or hidden tab is not still animating.</para>
    /// </summary>
    public partial class CompanionHeroCard : UserControl
    {
        private Storyboard? _breathe;

        public CompanionHeroCard()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
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

            // DispatcherPriority.Normal, never Loaded — Loaded is starved in this app and the
            // breathe would silently never start.
            Dispatcher.BeginInvoke(new Action(StartAmbientLoop), DispatcherPriority.Normal);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => StopAmbientLoop();

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
