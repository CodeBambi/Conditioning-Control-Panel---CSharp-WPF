using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// "Her Room" — the composed Companion tab. See the XAML header for the zone map.
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionRoomView.xaml.cs.
    /// All three page-level behaviours the WPF view owns now cross: the navigator, the shelf
    /// collapse and the clock parking. What is still missing is the viewmodel half - ICompanionRoomVm
    /// and its eight zone interfaces are in the WPF head, so ViewModel/Claim and the compat-seam
    /// passthroughs (TxtDetachStatusCompanion, the sixteen AiPermissionsGrid names, ...) have no
    /// source to read; every zone seeds itself from CoreSettings instead.</para>
    /// </summary>
    public partial class CompanionRoomView : UserControl, ICompanionRoomNavigator
    {
        private bool _isShelfStacked;
        private bool _shelfApplied;

        public CompanionRoomView()
        {
            InitializeComponent();
            // A one-time Bounds read at Loaded — a value, not a binding. SizeChanged owns it from here.
            Loaded += (_, _) => ApplyShelfLayout(Bounds.Width);
            SizeChanged += (_, e) => { if (e.WidthChanged) ApplyShelfLayout(e.NewSize.Width); };

        }

        // =====================================================================================
        //  effective-visibility watch
        // =====================================================================================

        private readonly List<IDisposable> _visibilityWatch = new();

        /// <summary>
        /// WPF hung the clock parking off <c>OnIsVisibleChanged</c>, which reports EFFECTIVE
        /// visibility. Avalonia's <c>IsVisible</c> is this control's LOCAL flag and stays true
        /// under a hidden ancestor, and the shell switches tabs by flipping
        /// <c>CompanionTab.IsVisible</c> two levels up (MainShellWindow.axaml) - so watching our
        /// own property would never fire on a tab switch, which is the only case that matters.
        /// <c>IsEffectivelyVisibleChanged</c> is the exact twin and Avalonia 12 keeps it INTERNAL
        /// (see the same note in Controls/VatGlassCanvas.cs and Controls/TakeoverOrb.cs), so this
        /// watches the chain by hand: our own flag plus every visual ancestor's, re-reading
        /// <see cref="Visual.IsEffectivelyVisible"/> on any of them. Rebuilt on attach because the
        /// chain does not exist before it and can differ after a re-parent.
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            foreach (var v in this.GetSelfAndVisualAncestors())
                _visibilityWatch.Add(v.GetObservable(IsVisibleProperty).Subscribe(new VisibilityObserver(this)));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            foreach (var d in _visibilityWatch) d.Dispose();
            _visibilityWatch.Clear();
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>One subscription per link in the chain, all answering the same question.</summary>
        private sealed class VisibilityObserver : IObserver<bool>
        {
            private readonly CompanionRoomView _owner;
            public VisibilityObserver(CompanionRoomView owner) => _owner = owner;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(bool value)
            {
                if (_owner.IsEffectivelyVisible) _owner.ResumeClocks();
                else _owner.ParkClocks();
            }
        }

        // ponytail: ICompanionRoomVm (Hero/Chat/Memory/... zone interfaces + Navigator) lives in the
        // WPF head; each ported zone seeds its own viewmodel. ViewModel/Claim return when it moves.

        /// <summary>
        /// True while the shelf is one column. Exposed for the tests and the preview harness — the
        /// collapse is the only piece of layout on this page that a screenshot cannot prove.
        /// </summary>
        internal bool IsShelfStacked => _isShelfStacked;

        // =====================================================================================
        //  clock parking
        // =====================================================================================

        /// <summary>
        /// The tab is switched by visibility in this app, not by unloading, so the zones' own
        /// Unloaded handlers never fire when the user moves to another tab. Without this the
        /// portrait keeps breathing and the wire cursor keeps blinking behind whatever the user is
        /// actually looking at.
        /// </summary>
        private void ResumeClocks()
        {
            try
            {
                HeroZone.RefreshAmbientState();
                // The zone re-reads its own state rather than being told what to do: it may well
                // have changed while the tab was hidden, and a resume that restored the state at
                // park time would be a lie.
                AwarenessZone.SyncCursorBlink();
                // ponytail: WPF also calls ChatZone.SyncThinking() here. This head's
                // ChatThresholdView has no thinking clock to sync - WPF's dots are three
                // RepeatBehavior=Forever Storyboards (CmpThinkingDotsStoryboard) and the port
                // draws the dots static. Add the call when that animation exists here.
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
            }
            catch (InvalidOperationException) { /* already torn down */ }
        }

        // =====================================================================================
        //  the shelf's one-column collapse
        // =====================================================================================

        /// <summary>
        /// Moves the right-hand column below the left one under
        /// <see cref="CompanionShelfLayout.StackBelow"/> and back up above
        /// <see cref="CompanionShelfLayout.UnstackAbove"/>. Only writes on a real threshold
        /// crossing, and the hysteresis gap means the width the new layout produces can never push
        /// it straight back over the line it just crossed.
        /// </summary>
        internal void ApplyShelfLayout(double width)
        {
            bool stack = CompanionShelfLayout.ShouldStack(_isShelfStacked, width);
            if (_shelfApplied && stack == _isShelfStacked) return;

            _isShelfStacked = stack;
            _shelfApplied = true;

            var gutter = Shelf.ColumnDefinitions[1];
            var right = Shelf.ColumnDefinitions[2];
            if (stack)
            {
                gutter.Width = new GridLength(0);
                right.Width = new GridLength(0);
                Grid.SetColumnSpan(ShelfLeft, 3);
                Grid.SetRow(ShelfRight, 1);
                Grid.SetColumn(ShelfRight, 0);
                Grid.SetColumnSpan(ShelfRight, 3);
            }
            else
            {
                gutter.Width = new GridLength(CompanionShelfLayout.GutterWidth);
                right.Width = new GridLength(2, GridUnitType.Star);
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
        /// <remarks>Scroll first, focus second, both deferred one turn at Normal priority — Loaded
        /// priority is starved in this app, and the card may not have been arranged yet.</remarks>
        public void FocusAwareness()
        {
            Dispatcher.UIThread.Post(() =>
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
            }, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// What a zone may ask the page to do. Implemented by <see cref="CompanionRoomView"/>. Port of
    /// the head's ICompanionRoomVm.cs (the navigator half; the viewmodel half stays until its eight
    /// zone interfaces exist here).
    /// </summary>
    public interface ICompanionRoomNavigator
    {
        /// <summary>Expands the Engine Room drawer and scrolls it into view.</summary>
        void RevealEngineRoom();

        /// <summary>Scrolls "What she can see" into view and gives it the keyboard focus.</summary>
        void FocusAwareness();

        /// <summary>Expands the Workshop and, when <paramref name="cellTitle"/> names one of its
        /// pigeonholes, brings that cell into view.</summary>
        void RevealWorkshop(string? cellTitle = null);
    }

    /// <summary>
    /// When the shelf is one column instead of two ("two-column shelf collapses to one below
    /// ~1100px"). Its own type because it is the page's only real decision and must be
    /// unit-testable: a collapse that oscillates is a hang. Verbatim from the WPF head.
    /// </summary>
    public static class CompanionShelfLayout
    {
        /// <summary>Below this width the right column moves under the left one.</summary>
        public const double StackBelow = 1100;

        /// <summary>And it only comes back up above this one. The 60px gap is the hysteresis.</summary>
        public const double UnstackAbove = 1160;

        /// <summary>The gutter column's width while the shelf is two columns.</summary>
        public const double GutterWidth = 16;

        /// <summary>
        /// The next stacked-ness given the current one and a width. A non-positive or non-finite
        /// width changes nothing — the caller keeps whatever it had rather than snapping to a
        /// column count on a number that means "unknown".
        /// </summary>
        public static bool ShouldStack(bool isStacked, double width)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) return isStacked;
            return isStacked ? width < UnstackAbove : width < StackBelow;
        }
    }
}
