using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z7 — the Engine Room drawer. See the XAML header for the visual spec.
    /// </summary>
    public partial class EngineRoomDrawer : UserControl
    {
        public EngineRoomDrawer()
        {
            InitializeComponent();
            // The live-actions feed is height-capped and scrolls internally; without this a wheel
            // notch over it never reaches the page — see CompanionWheelRelay.
            CompanionWheelRelay.Attach(LiveActionsFeed);
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IEngineRoomDrawerVm? ViewModel
        {
            get => DataContext as IEngineRoomDrawerVm;
            set => DataContext = value;
        }

        /// <summary>
        /// The hero AI pill's deep link: expand the drawer, then scroll it into view.
        ///
        /// <para>The BringIntoView is deferred one dispatcher turn so it runs after the Expander's
        /// body has been measured — otherwise it scrolls to the collapsed height. The priority is
        /// <see cref="DispatcherPriority.Normal"/> and never <c>Loaded</c>: Loaded-priority work is
        /// starved in this app and the scroll would silently never happen.</para>
        /// </summary>
        public void ExpandAndReveal()
        {
            var vm = ViewModel;
            if (vm != null) vm.IsExpanded = true;
            else Drawer.IsExpanded = true;

            var dispatcher = Dispatcher;
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { BringIntoView(); }
                catch (InvalidOperationException) { /* torn down mid-scroll */ }
            }), DispatcherPriority.Normal);
        }

        /// <summary>
        /// Pushes the typed key into the viewmodel.
        ///
        /// <para><c>PasswordBox.Password</c> is not a dependency property — deliberately, in WPF —
        /// so there is no binding to do this. That is the whole point: the key travels one way, from
        /// the box to the settings file, and nothing on this page can read it back.</para>
        /// </summary>
        private void CustomKeyBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ViewModel is { } vm && sender is PasswordBox box) vm.CustomApiKey = box.Password;
        }
    }
}
