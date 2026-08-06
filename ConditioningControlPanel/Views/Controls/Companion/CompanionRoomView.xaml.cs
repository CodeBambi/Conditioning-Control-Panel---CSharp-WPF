using System;
using System.Windows;
using System.Windows.Controls;
using Border = System.Windows.Controls.Border;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// "Her Room" — the composed Companion tab. See the XAML header for the zone map and the
    /// motion budget.
    ///
    /// <para>The view owns exactly three behaviours, all of them page-level by nature:</para>
    /// <list type="bullet">
    ///   <item><b>the navigator</b> — it implements <see cref="ICompanionRoomNavigator"/> and hands
    ///   itself to the viewmodel, which fans it out to the zones that carry cross-page links;</item>
    ///   <item><b>the shelf collapse</b> — a threshold flip with hysteresis, never a binding to
    ///   ActualWidth;</item>
    ///   <item><b>clock parking</b> — this app hides tabs rather than unloading them, so a hidden
    ///   room would otherwise keep a composition clock running forever.</item>
    /// </list>
    ///
    /// <para>Everything else belongs to a zone. If something here starts reaching into one, it is a
    /// sign the zone's contract is missing a property.</para>
    /// </summary>
    public partial class CompanionRoomView : UserControl, ICompanionRoomNavigator
    {
        private ICompanionRoomVm? _claimed;
        private bool _isShelfStacked;
        private bool _shelfApplied;

        public CompanionRoomView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public ICompanionRoomVm? ViewModel
        {
            get => DataContext as ICompanionRoomVm;
            set => DataContext = value;
        }

        /// <summary>
        /// True while the shelf is one column. Exposed for the tests and the preview harness — the
        /// collapse is the only piece of layout on this page that a screenshot cannot prove.
        /// </summary>
        internal bool IsShelfStacked => _isShelfStacked;

        // =====================================================================================
        //  lifetime
        // =====================================================================================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            // A one-time ActualWidth read — a value, not a binding. SizeChanged owns it from here.
            ApplyShelfLayout(ActualWidth);
            Claim(ViewModel);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Never leave a torn-down page reachable from a live viewmodel: a deep link fired from
            // a stale navigator would scroll a tree that is no longer on screen.
            Claim(null);
            ParkClocks();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ICompanionRoomVm old && ReferenceEquals(old.Navigator, this))
                old.Navigator = null;

            Claim(ViewModel);
        }

        private void Claim(ICompanionRoomVm? vm)
        {
            if (_claimed != null && !ReferenceEquals(_claimed, vm) && ReferenceEquals(_claimed.Navigator, this))
                _claimed.Navigator = null;

            _claimed = vm;
            if (vm != null) vm.Navigator = this;
        }

        // =====================================================================================
        //  compat seam (design §6 disposition table, last row; §7.10)
        // =====================================================================================

        /// <summary>
        /// The hidden elements the MainWindow partials write to by name.
        ///
        /// <para>Every other row of the disposition table lands in a zone; this one had nowhere to
        /// go, and the wiring pass would have hit ~100 <c>MainWindow.Patreon.cs</c> /
        /// <c>MainWindow.CompanionTab.cs</c> references with nothing to bind. The elements
        /// themselves live on the zone that owns the thing they describe (status texts and the
        /// quick-controls help button on the hero, the settings help button on the Workshop), and
        /// these passthroughs give the partials the single accessor path <c>CompanionTab</c> gave
        /// them before — so the swap is a rename, not a rewrite.</para>
        ///
        /// <para>All of them are permanently collapsed. They exist to be written to, never seen:
        /// the status they carry is drawn properly by the hero's pills.</para>
        /// </summary>
        internal TextBlock TxtDetachStatusCompanion => HeroZone.TxtDetachStatusCompanion;

        /// <inheritdoc cref="TxtDetachStatusCompanion"/>
        internal TextBlock TxtCompanionStatus => HeroZone.TxtCompanionStatus;

        /// <inheritdoc cref="TxtDetachStatusCompanion"/>
        internal Button HelpBtnQuickControls => HeroZone.HelpBtnQuickControls;

        /// <inheritdoc cref="TxtDetachStatusCompanion"/>
        internal Button HelpBtnCompanionSettings => WorkshopZone.HelpBtnCompanionSettings;

        /// <summary>
        /// The Brain-Parasite drain flash on the hero's companion XP bar. Unlike the three above
        /// this one IS seen — it is the pink pulse <c>MainWindow.Companion.cs</c> fires on every
        /// drain — and it is here for the same reason: the partial that animates it looks it up
        /// through the tab.
        /// </summary>
        internal Border PrgCompanion0FlashOverlay => HeroZone.PrgCompanion0FlashOverlay;

        // =====================================================================================
        //  clock parking
        // =====================================================================================

        /// <summary>
        /// The tab is switched by visibility in this app, not by unloading, so the zones' own
        /// Unloaded handlers never fire when the user moves to another tab. Without this the
        /// portrait keeps breathing and the wire cursor keeps blinking behind whatever the user is
        /// actually looking at.
        /// </summary>
        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible && visible) ResumeClocks();
            else ParkClocks();
        }

        private void ResumeClocks()
        {
            try
            {
                HeroZone.RefreshAmbientState();
                // Both zones re-read their own viewmodel rather than being told what to do: the
                // state may well have changed while the tab was hidden (a reply landed, the dial
                // moved), and a resume that restores the state at park time would be a lie.
                AwarenessZone.SyncCursorBlink();
                ChatZone.SyncThinking();
            }
            catch (InvalidOperationException)
            {
                // Decorative animation may never be the reason a tab fails to show.
            }
        }

        private void ParkClocks()
        {
            try
            {
                HeroZone.StopAmbientLoop();
                AwarenessZone.StopCursorBlink();
                // Z2's thinking dots are three RepeatBehavior=Forever clones. Send a message and
                // switch tabs while the reply is in flight (or stalled, or retrying) and they run
                // behind whatever the user is looking at — precisely what this method exists for.
                ChatZone.StopThinking();
            }
            catch (InvalidOperationException) { /* already torn down */ }
        }

        // =====================================================================================
        //  the shelf's one-column collapse
        // =====================================================================================

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) ApplyShelfLayout(e.NewSize.Width);
        }

        /// <summary>
        /// Moves the right-hand column below the left one under
        /// <see cref="CompanionShelfLayout.StackBelow"/> and back up above
        /// <see cref="CompanionShelfLayout.UnstackAbove"/>.
        ///
        /// <para>Two properties make this safe to drive from SizeChanged. It only writes on a real
        /// threshold crossing, so a drag across the band cannot turn into a write per frame; and the
        /// hysteresis gap means the width the new layout produces can never push it straight back
        /// over the line it just crossed, which is how this pattern usually oscillates.</para>
        /// </summary>
        internal void ApplyShelfLayout(double width)
        {
            bool stack = CompanionShelfLayout.ShouldStack(_isShelfStacked, width);
            if (_shelfApplied && stack == _isShelfStacked) return;

            _isShelfStacked = stack;
            _shelfApplied = true;

            if (stack)
            {
                ShelfGutterColumn.Width = new GridLength(0);
                ShelfRightColumn.Width = new GridLength(0);
                Grid.SetColumnSpan(ShelfLeft, 3);
                Grid.SetRow(ShelfRight, 1);
                Grid.SetColumn(ShelfRight, 0);
                Grid.SetColumnSpan(ShelfRight, 3);
            }
            else
            {
                ShelfGutterColumn.Width = new GridLength(CompanionShelfLayout.GutterWidth);
                ShelfRightColumn.Width = new GridLength(2, GridUnitType.Star);
                Grid.SetColumnSpan(ShelfLeft, 1);
                Grid.SetRow(ShelfRight, 0);
                Grid.SetColumn(ShelfRight, 2);
                Grid.SetColumnSpan(ShelfRight, 1);
            }
        }

        // =====================================================================================
        //  ICompanionRoomNavigator — the cross-zone links
        // =====================================================================================

        /// <inheritdoc/>
        public void RevealEngineRoom() => EngineZone.ExpandAndReveal();

        /// <inheritdoc/>
        public void RevealWorkshop(string? cellTitle = null) => WorkshopZone.ExpandAndReveal(cellTitle);

        /// <inheritdoc/>
        /// <remarks>
        /// Scroll first, focus second, both deferred one turn at
        /// <see cref="DispatcherPriority.Normal"/> — Loaded priority is starved in this app, and the
        /// card may not have been arranged yet when the pill is clicked from a freshly opened tab.
        /// </remarks>
        public void FocusAwareness()
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                TryFocusAwareness();
                return;
            }
            dispatcher.BeginInvoke(new Action(TryFocusAwareness), DispatcherPriority.Normal);
        }

        private void TryFocusAwareness()
        {
            try
            {
                AwarenessZone.BringIntoView();
                AwarenessZone.Focus();
            }
            catch (InvalidOperationException)
            {
                // Torn down mid-scroll. A deep link that arrives late is not worth a crash.
            }
        }
    }

    /// <summary>
    /// When the shelf is one column instead of two (design §2: "two-column shelf collapses to one
    /// below ~1100px").
    ///
    /// <para>Its own type because it is the page's only real decision and it must be unit-testable:
    /// a collapse that oscillates is a hang, and a hang is not something a render smoke test finds.
    /// The thresholds are asymmetric on purpose — see <see cref="ShouldStack"/>.</para>
    /// </summary>
    public static class CompanionShelfLayout
    {
        /// <summary>Below this width the right column moves under the left one.</summary>
        public const double StackBelow = 1100;

        /// <summary>
        /// And it only comes back up above this one. The 60px gap is the hysteresis: with a single
        /// threshold, a width sitting exactly on the line would flip the layout, the new layout
        /// would report a width on the other side of it, and the two would trade places forever.
        /// </summary>
        public const double UnstackAbove = 1160;

        /// <summary>The gutter column's width while the shelf is two columns.</summary>
        public const double GutterWidth = 16;

        /// <summary>
        /// The next stacked-ness given the current one and a width. Deliberately takes the current
        /// state: the answer depends on it, which is what hysteresis means.
        ///
        /// <para>A non-positive or non-finite width (a control that has not been arranged, a
        /// measure pass with infinity) changes nothing — the caller keeps whatever it had rather
        /// than snapping to a column count on a number that means "unknown".</para>
        /// </summary>
        public static bool ShouldStack(bool isStacked, double width)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) return isStacked;
            return isStacked ? width < UnstackAbove : width < StackBelow;
        }
    }
}
