using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel
{
    // Leaderboard tab: ranked roster, tier bands, podium, sticky "you" bar.
    public partial class MainWindow
    {
        #region Leaderboard

        /// <summary>
        /// The board in canonical rank order. <see cref="Services.LeaderboardEntry.Rank"/> is
        /// assigned here once per refresh and is NEVER re-assigned by an alternate sort — a
        /// row's Rank has to keep meaning "standing", or the tier bands and the delta arrows
        /// start lying (that class of mistake is what produced ccp-bugs #693).
        /// </summary>
        private List<Services.LeaderboardEntry> _leaderboardRanked = new();

        /// <summary>Display sort: rank | name | level | xp | achievements | streak.</summary>
        private string _leaderboardSortKey = "rank";

        /// <summary>Client-side roster filter: all | online | patrons | og.</summary>
        private string _leaderboardFilter = "all";

        /// <summary>Client-side roster search over display names.</summary>
        private string _leaderboardSearch = "";

        /// <summary>
        /// The local player's previous rank, captured BEFORE the snapshot is re-recorded.
        /// See <see cref="RankLeaderboardEntries"/> for why the ordering matters.
        /// </summary>
        private int? _youPreviousRank;
        private bool _youPreviousRankKnown;

        /// <summary>Number of achievements that count toward the roster's "X / Y" column.</summary>
        private static int EarnableAchievementCount =>
            Models.Achievement.All.Values.Count(a => !a.IsHidden);

        // ------------------------------------------------------------------
        // Toolbar handlers
        // ------------------------------------------------------------------

        internal async void BtnRefreshLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            await RefreshLeaderboardAsync();
        }

        /// <summary>
        /// Column-legend sorting. This replaces the old GridViewColumnHeader.Click handler,
        /// which mapped English header strings to server sort fields and re-fetched the board
        /// on every click. Sorting is now purely client-side: the v3 endpoint always returns
        /// the same XP-ordered slice regardless of sort_by, so the round trip bought nothing.
        /// </summary>
        internal void LeaderboardSortHeader_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading || App.Leaderboard == null) return;
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string key) return;

            _leaderboardSortKey = key;
            RebuildLeaderboardView();
        }

        internal void LeaderboardSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not System.Windows.Controls.TextBox box) return;

            _leaderboardSearch = box.Text ?? "";
            RebuildLeaderboardView();
        }

        internal void LeaderboardFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not System.Windows.Controls.Primitives.ToggleButton chip || chip.Tag is not string tag) return;

            _leaderboardFilter = tag;
            RebuildLeaderboardView();
        }

        /// <summary>
        /// "Jump to me". Two outcomes, and the button now SHOWS which one it took instead of
        /// leaving the user to read a status line:
        ///
        /// • in the loaded slice — animate-scroll the roster until the row sits CENTRED in the
        ///   viewport, then flare it. Centred, not merely "in view": the old
        ///   <c>ScrollIntoView</c> does a minimal scroll and stops the instant the row is
        ///   technically inside the viewport, which parks it flush against the bottom edge
        ///   where the sticky you-bar slices it in half.
        ///
        /// • outside it — animate to the last row, bounce off the end of travel like an
        ///   overscroll, then flare the sticky you-bar. "You're not up there, you're down
        ///   here", said with motion. There is no second page to send them to, so the old
        ///   "not on this page" wording promised a picker that was never built (#693).
        /// </summary>
        internal void BtnJumpToMe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Every click owns the FX outright: kill whatever the previous one left running
                // so a double-click can't leave two scroll clocks fighting or a glow stranded on
                // a recycled container.
                CancelJumpToMeFx();

                var list = LeaderboardTab?.LstLeaderboard;
                if (list?.ItemsSource == null)
                {
                    // No slice loaded at all (offline, or the fetch failed). Don't be a dead
                    // button: the cached server rank is still worth reporting, and if even that's
                    // missing OutsideBoardMessage() says so out loud instead of doing nothing.
                    // No FX here — there is nothing on screen to point at.
                    SetLeaderboardStatus(OutsideBoardMessage());
                    return;
                }

                var myId = App.UnifiedUserId;
                Services.LeaderboardEntry? me = null;
                if (!string.IsNullOrEmpty(myId))
                {
                    me = list.ItemsSource.OfType<Services.LeaderboardEntry>()
                        .FirstOrDefault(x => !string.IsNullOrEmpty(x.UnifiedId) && x.UnifiedId == myId);
                }

                var serverRank = App.Leaderboard?.YourRank ?? 0;
                var inRange = serverRank > 0 && serverRank <= Services.LeaderboardService.FetchLimit;

                // #693 fallback. "Couldn't find the row" is not "isn't on the board": the scan
                // above keys off UnifiedId, so a server row that ships a null/blank unified_id
                // for the local user fails it while that very row sits on screen. An in-range
                // server rank is proof the row is in the slice, so try to locate it by canonical
                // Rank before deciding the user is off the board — bouncing them to the bottom
                // while their own row is visible would contradict what they're looking at.
                if (me == null && inRange)
                {
                    me = list.ItemsSource.OfType<Services.LeaderboardEntry>()
                        .FirstOrDefault(x => x.Rank == serverRank);
                }

                if (me != null)
                {
                    JumpToMyRow(list, me);
                    return;
                }

                // The wording still goes up regardless of which FX plays: the motion is
                // additive, not a replacement, and someone reading rather than watching still
                // gets the number.
                SetLeaderboardStatus(OutsideBoardMessage());

                // Only a rank genuinely past the fetch limit earns the bounce. An in-range rank
                // whose row we simply couldn't locate (filtered out, or the #693 blank id with no
                // matching Rank either) gets the status line and nothing else — faking a
                // "you're way down there" gesture would be a lie.
                if (serverRank > Services.LeaderboardService.FetchLimit)
                    BounceToBoardEnd(list);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Jump-to-me failed");
            }
        }

        private void SetLeaderboardStatus(string text)
        {
            if (LeaderboardTab?.TxtLeaderboardStatus != null)
                LeaderboardTab.TxtLeaderboardStatus.Text = text;
        }

        // ------------------------------------------------------------------
        // Jump-to-me FX
        //
        // Everything below is transient decoration hung on live visuals: an Effect on a
        // container, a RenderTransform on the roster's ItemsPresenter, an animation clock on a
        // ScrollViewer. Two rules keep it from leaking:
        //
        //  1. Every decorated element is remembered in _lbFxDecorated and stripped again the
        //     moment its animation completes AND on the next click. The roster virtualises with
        //     VirtualizationMode=Recycling, so a container that keeps an Effect or a transform
        //     hands that state to whatever row it is recycled onto next — a stuck pulse would
        //     migrate onto a stranger's row on the next scroll.
        //  2. Every callback is fired by an animation clock, i.e. can land after the window is
        //     gone. They all check the dispatcher (CLAUDE.md async/threading #6, #8), carry a
        //     generation stamp so a stale clock can't clobber a newer jump, and swallow.
        // ------------------------------------------------------------------

        /// <summary>Bumped by every jump; stale animation callbacks compare against it and bail.</summary>
        private int _lbFxGeneration;

        /// <summary>Live visuals currently carrying a temporary Effect / RenderTransform.</summary>
        private readonly List<FrameworkElement> _lbFxDecorated = new();

        /// <summary>The roster's ScrollViewer while a scroll animation is on it.</summary>
        private ScrollViewer? _lbFxScroller;

        /// <summary>ClipToBounds as we found it, restored after the overscroll bounce.</summary>
        private bool? _lbFxClipWas;

        /// <summary>
        /// <see cref="ScrollViewer.VerticalOffset"/> is read-only and written through the scroll
        /// info, so it cannot be animated. This shim is the standard workaround: animate a plain
        /// double attached to the ScrollViewer and let the change callback forward every tick to
        /// ScrollToVerticalOffset.
        /// </summary>
        private static readonly DependencyProperty LbScrollOffsetProperty =
            DependencyProperty.RegisterAttached(
                "LbScrollOffset", typeof(double), typeof(MainWindow),
                new PropertyMetadata(0.0, OnLbScrollOffsetChanged));

        private static void OnLbScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (d is not ScrollViewer sv) return;
                if (Application.Current?.Dispatcher == null) return;
                if (Application.Current.Dispatcher.HasShutdownStarted) return;
                sv.ScrollToVerticalOffset((double)e.NewValue);
            }
            catch { }
        }

        /// <summary>Accent of the board currently on screen: gold for All-Time, the mod accent otherwise.</summary>
        private Color LeaderboardAccentColor()
        {
            try
            {
                if (_leaderboardMode == "all-time") return (Color)ColorConverter.ConvertFromString("#FFD700");
                return (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4");
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString("#FF69B4");
            }
        }

        private static T? FindVisualDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var deep = FindVisualDescendant<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }

        // ---------------- in the slice: centred glide + row flare ----------------

        /// <summary>
        /// Glide the roster until <paramref name="me"/> is centred, then flare it.
        /// </summary>
        private void JumpToMyRow(ListView list, Services.LeaderboardEntry me)
        {
            list.SelectedItem = me;

            var sv = FindVisualDescendant<ScrollViewer>(list);
            if (sv == null)
            {
                // Tab has never been rendered, so there is no ScrollViewer to drive. The old
                // minimal scroll beats nothing; the flare still fires once containers exist.
                list.ScrollIntoView(me);
                QueueCenterAndPulse(list, null, me);
                return;
            }

            var start = sv.VerticalOffset;

            // ScrollIntoView is used here ONLY to realise the container: with Recycling on, the
            // ListViewItem for `me` does not exist until something scrolls near it, and an
            // unrealised container has no position and no height to centre on. It runs
            // synchronously once containers have been generated, which is why the teleport it
            // performs can be undone below before a single frame is rendered.
            list.ScrollIntoView(me);
            list.UpdateLayout();

            var target = TryMeasureCenteredOffset(sv, list, me);
            if (target == null)
            {
                // Still not realised. Fall back to the documented pattern — retry at Loaded
                // priority, once layout has definitely run — and settle without the glide rather
                // than visibly jumping twice.
                QueueCenterAndPulse(list, sv, me);
                return;
            }

            // Undo ScrollIntoView's minimal scroll. This is not a wasted move: putting the
            // viewport back where the user actually was is what gives the animation something to
            // travel, so the eye can follow the board rather than being teleported.
            sv.ScrollToVerticalOffset(start);

            var gen = _lbFxGeneration;
            AnimateScrollOffset(sv, start, target.Value, 520, () => PulseMyRow(list, me, gen, retry: true));
        }

        /// <summary>
        /// Where the ScrollViewer has to sit for <paramref name="item"/>'s row to land in the
        /// MIDDLE of the viewport, clamped to the real extent so a row near either end simply
        /// parks at its end of travel instead of demanding an impossible offset.
        ///
        /// Measured off the realised container rather than computed from an index times a row
        /// height: the roster interleaves non-selectable tier-band rows with real ones, so row
        /// pitch is not uniform and any index arithmetic drifts.
        ///
        /// Null means "not measurable yet" — the container isn't realised, or hasn't been
        /// measured — and both make the transform below meaningless.
        /// </summary>
        private static double? TryMeasureCenteredOffset(ScrollViewer sv, ListView list, object item)
        {
            try
            {
                if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row) return null;
                if (!row.IsVisible || row.ActualHeight <= 0 || sv.ViewportHeight <= 0) return null;

                var top = row.TransformToAncestor(sv).Transform(new Point(0, 0)).Y;
                var wanted = Math.Max(0, (sv.ViewportHeight - row.ActualHeight) / 2.0);
                var offset = sv.VerticalOffset + (top - wanted);
                return Math.Max(0, Math.Min(offset, sv.ScrollableHeight));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Deferred "centre it and flare it" for the case where the container wasn't realised in time.</summary>
        private void QueueCenterAndPulse(ListView list, ScrollViewer? sv, Services.LeaderboardEntry me)
        {
            var gen = _lbFxGeneration;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                try
                {
                    if (gen != _lbFxGeneration) return;
                    if (Application.Current?.Dispatcher == null) return;
                    if (Application.Current.Dispatcher.HasShutdownStarted) return;

                    if (sv != null)
                    {
                        var target = TryMeasureCenteredOffset(sv, list, me);
                        if (target != null) sv.ScrollToVerticalOffset(target.Value);
                    }
                    PulseMyRow(list, me, gen, retry: false);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug(ex, "Jump-to-me deferred centring failed");
                }
            }));
        }

        /// <summary>Focus + flare the local user's realised row.</summary>
        private void PulseMyRow(ListView list, Services.LeaderboardEntry me, int gen, bool retry)
        {
            try
            {
                if (gen != _lbFxGeneration) return;

                if (list.ItemContainerGenerator.ContainerFromItem(me) is not ListViewItem row)
                {
                    if (retry) QueueCenterAndPulse(list, null, me);
                    return;
                }

                // Focus is what the old implementation did and it still matters for keyboard
                // users. The row is already fully in view at this point, so the BringIntoView
                // that focus triggers is a no-op and can't undo the centring.
                row.Focus();
                PulseElement(row, LeaderboardAccentColor(), glow: 34, pop: 0.012, gen: gen);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "Jump-to-me row pulse failed");
            }
        }

        // ---------------- outside the slice: bounce off the end + flare the you-bar ----------------

        /// <summary>
        /// Glide to the last row and rubber-band off the end of travel, then flare the sticky
        /// you-bar. The gesture is the message: the board runs out, and the user's own standing
        /// is the thing pinned below it.
        /// </summary>
        private void BounceToBoardEnd(ListView list)
        {
            try
            {
                var sv = FindVisualDescendant<ScrollViewer>(list);
                if (sv == null) { PulseYouBar(_lbFxGeneration); return; }

                var gen = _lbFxGeneration;
                var start = sv.VerticalOffset;
                var end = sv.ScrollableHeight;

                if (end - start < 1)
                {
                    // Already parked at the end (or the list is short enough not to scroll at
                    // all) — no travel to animate, go straight to the bounce.
                    PlayOverscrollBounce(sv, gen);
                    return;
                }

                AnimateScrollOffset(sv, start, end, 480, () =>
                {
                    // Extent is only an estimate while rows are virtualised, and it firms up as
                    // rows realise on the way down, so re-assert the end before bouncing off it.
                    sv.ScrollToBottom();
                    PlayOverscrollBounce(sv, gen);
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "Jump-to-me bounce failed");
            }
        }

        /// <summary>
        /// Fake an overscroll at the bottom of the roster.
        ///
        /// A ScrollViewer clamps its own offset, so there is no such thing as scrolling PAST the
        /// last row — the rubber band has to be faked by translating the presented content and
        /// letting an ElasticEase settle it back. ClipToBounds is forced on for the duration so
        /// the displaced rows slide under the legend header and the you-bar instead of spilling
        /// over them; the original value is restored on the way out.
        /// </summary>
        private void PlayOverscrollBounce(ScrollViewer sv, int gen)
        {
            try
            {
                if (gen != _lbFxGeneration) return;

                var content = sv.Content as FrameworkElement ?? FindVisualDescendant<ItemsPresenter>(sv);
                if (content == null) { PulseYouBar(gen); return; }

                _lbFxScroller = sv;
                _lbFxClipWas ??= sv.ClipToBounds;
                sv.ClipToBounds = true;

                var slide = new TranslateTransform();
                content.RenderTransform = slide;
                _lbFxDecorated.Add(content);

                var y = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                y.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                y.KeyFrames.Add(new EasingDoubleKeyFrame(-BounceOvershootPx,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                y.KeyFrames.Add(new EasingDoubleKeyFrame(0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)),
                    new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }));

                y.Completed += (_, _) =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        if (Application.Current.Dispatcher.HasShutdownStarted) return;
                        if (gen != _lbFxGeneration) return;

                        ClearFxDecoration(content, gen);
                        if (_lbFxClipWas.HasValue) { sv.ClipToBounds = _lbFxClipWas.Value; _lbFxClipWas = null; }
                        PulseYouBar(gen);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug(ex, "Jump-to-me bounce landing failed");
                    }
                };

                slide.BeginAnimation(TranslateTransform.YProperty, y);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "Jump-to-me overscroll bounce failed");
            }
        }

        /// <summary>How far the roster is dragged past its last row, in DIPs, before it springs back.</summary>
        private const double BounceOvershootPx = 18;

        /// <summary>
        /// Flare the sticky you-bar. Same visual language as the row flare so the two outcomes
        /// of the button read as the same gesture aimed at two different places; the rank number
        /// and its delta arrow also pop, because those are the two glyphs the bounce just
        /// promised to show.
        /// </summary>
        private void PulseYouBar(int gen)
        {
            try
            {
                if (gen != _lbFxGeneration) return;

                var tab = LeaderboardTab;
                if (tab?.YouBar == null || tab.YouBar.Visibility != Visibility.Visible) return;

                var accent = LeaderboardAccentColor();
                PulseElement(tab.YouBar, accent, glow: 30, pop: 0, gen: gen);
                if (tab.TxtYouRankNumber != null) PulseElement(tab.TxtYouRankNumber, accent, glow: 20, pop: 0.22, gen: gen);
                if (tab.TxtYouDelta != null) PulseElement(tab.TxtYouDelta, accent, glow: 12, pop: 0.16, gen: gen);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "Jump-to-me you-bar pulse failed");
            }
        }

        // ---------------- shared FX primitives ----------------

        /// <summary>
        /// One-shot "look here" flare: a double glow pulse plus an optional hair-thin scale pop.
        ///
        /// It has to read ON TOP of a row that is already tinted with the accent and already
        /// carries a 3px accent left rule (the persistent IsCurrentUser treatment), so a
        /// background or border flash would only restate what is already there. A drop shadow is
        /// additive light — nothing else in the roster glows — and, crucially, it leaves the
        /// row's own brushes alone: those come from an ItemContainerStyle DataTrigger, and an
        /// animation on Background would out-rank that trigger and hold the row in a wrong
        /// colour after the FX ended.
        ///
        /// The Effect and the transform are torn down again in the completion callback (see the
        /// recycling note at the top of this section).
        /// </summary>
        private void PulseElement(FrameworkElement target, Color accent, double glow, double pop, int gen)
        {
            if (target == null) return;

            var effect = new DropShadowEffect
            {
                Color = accent,
                ShadowDepth = 0,
                BlurRadius = 0,
                Opacity = 0
            };
            target.Effect = effect;

            ScaleTransform? scale = null;
            if (pop > 0)
            {
                scale = new ScaleTransform(1, 1);
                target.RenderTransformOrigin = new Point(0.5, 0.5);
                target.RenderTransform = scale;
            }

            if (!_lbFxDecorated.Contains(target)) _lbFxDecorated.Add(target);

            var t0 = KeyTime.FromTimeSpan(TimeSpan.Zero);
            var t1 = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130));
            var t2 = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340));
            var t3 = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520));
            var t4 = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(980));

            var blur = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(0, t0));
            blur.KeyFrames.Add(new EasingDoubleKeyFrame(glow, t1, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            blur.KeyFrames.Add(new EasingDoubleKeyFrame(glow * 0.3, t2, new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            blur.KeyFrames.Add(new EasingDoubleKeyFrame(glow, t3, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            blur.KeyFrames.Add(new EasingDoubleKeyFrame(0, t4, new QuadraticEase { EasingMode = EasingMode.EaseIn }));

            var opacity = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, t0));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, t1, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.40, t2, new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, t3, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, t4, new QuadraticEase { EasingMode = EasingMode.EaseIn }));

            // One handler, on the animation that finishes last, does all the cleanup.
            opacity.Completed += (_, _) =>
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) return;
                    if (Application.Current.Dispatcher.HasShutdownStarted) return;
                    ClearFxDecoration(target, gen);
                }
                catch { }
            };

            effect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
            effect.BeginAnimation(DropShadowEffect.OpacityProperty, opacity);

            if (scale != null)
            {
                var pump = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                pump.KeyFrames.Add(new LinearDoubleKeyFrame(1, t0));
                pump.KeyFrames.Add(new EasingDoubleKeyFrame(1 + pop, t1, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                pump.KeyFrames.Add(new EasingDoubleKeyFrame(1, t3, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pump);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pump);
            }
        }

        /// <summary>Strip the temporary Effect/transform from one element, unless a newer jump owns it.</summary>
        private void ClearFxDecoration(FrameworkElement target, int gen)
        {
            if (gen != _lbFxGeneration) return; // a newer jump already re-decorated (or cleaned) it
            StripFxDecoration(target);
            _lbFxDecorated.Remove(target);
        }

        private static void StripFxDecoration(FrameworkElement target)
        {
            try
            {
                // ClearValue rather than "= null": these properties can legitimately be set by a
                // style, and clearing the LOCAL value is what puts the element back exactly the
                // way we found it.
                target.ClearValue(UIElement.EffectProperty);
                target.ClearValue(UIElement.RenderTransformProperty);
                target.ClearValue(UIElement.RenderTransformOriginProperty);
            }
            catch { }
        }

        /// <summary>
        /// Tear down every piece of jump FX. Called at the top of every click (so a rapid
        /// double-click can't run two of them at once) — the generation bump alone is what
        /// makes all the in-flight callbacks no-op.
        /// </summary>
        private void CancelJumpToMeFx()
        {
            _lbFxGeneration++;

            try
            {
                if (_lbFxScroller != null)
                {
                    _lbFxScroller.BeginAnimation(LbScrollOffsetProperty, null);
                    if (_lbFxClipWas.HasValue) _lbFxScroller.ClipToBounds = _lbFxClipWas.Value;
                    _lbFxScroller = null;
                }
                _lbFxClipWas = null;

                for (int i = 0; i < _lbFxDecorated.Count; i++) StripFxDecoration(_lbFxDecorated[i]);
                _lbFxDecorated.Clear();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "Jump-to-me FX teardown failed");
            }
        }

        /// <summary>
        /// Animate the roster's scroll offset from <paramref name="from"/> to <paramref name="to"/>
        /// and invoke <paramref name="onArrived"/> once it lands. EaseOut, and short: this is a
        /// "follow me" gesture, not a transition, and anything over ~600ms stops feeling like a
        /// response to a click.
        /// </summary>
        private void AnimateScrollOffset(ScrollViewer sv, double from, double to, double milliseconds, Action? onArrived)
        {
            var gen = _lbFxGeneration;

            void Land()
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) return;
                    if (Application.Current.Dispatcher.HasShutdownStarted) return;
                    if (gen != _lbFxGeneration) return;

                    // Park the shim on the final value BEFORE dropping the clock: hand the
                    // animation off in the other order and the property falls back to its base
                    // value, which snaps the list straight back to `from`.
                    sv.SetValue(LbScrollOffsetProperty, to);
                    sv.BeginAnimation(LbScrollOffsetProperty, null);
                    sv.ScrollToVerticalOffset(to);

                    onArrived?.Invoke();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug(ex, "Leaderboard scroll animation landing failed");
                }
            }

            if (Math.Abs(to - from) < 0.5) { Land(); return; }

            sv.SetValue(LbScrollOffsetProperty, from);

            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) => Land();

            _lbFxScroller = sv;
            sv.BeginAnimation(LbScrollOffsetProperty, anim);
        }

        /// <summary>
        /// Status line for a player whose row we couldn't point at. "Couldn't find the row" is NOT
        /// the same as "isn't on the board": the row scan and the pink IsCurrentUser highlight both
        /// key off UnifiedId, so a server row that ships a null/blank unified_id for the local user
        /// sends us here while that row sits on screen, highlighted. Announcing "outside the top
        /// 200" then contradicts what the user is looking at. So only a rank past FetchLimit earns
        /// the outside-the-board wording; an in-range rank just states the standing.
        /// </summary>
        private static string OutsideBoardMessage()
        {
            var rank = App.Leaderboard?.YourRank;
            if (rank is not > 0) return Loc.Get("label_your_rank_unavailable");

            var total = App.Leaderboard?.YourTotal;

            if (rank.Value <= Services.LeaderboardService.FetchLimit)
            {
                return total is > 0
                    ? Loc.GetF("label_your_rank_0_of_1", rank.Value, total.Value)
                    : Loc.GetF("label_your_rank_0", rank.Value);
            }

            return total is > 0
                ? Loc.GetF("label_rank_outside_board_3", rank.Value, total.Value, Services.LeaderboardService.FetchLimit)
                : Loc.GetF("label_rank_outside_board_2", rank.Value, Services.LeaderboardService.FetchLimit);
        }

        /// <summary>
        /// Populate the "Your rank: #N" badge. The server rank is the ONLY acceptable source, and
        /// there is deliberately no fallback to the row's own Rank: a row's Rank is a slice offset
        /// rather than a standing. Falling back to it is what produced ccp-bugs #693, where the
        /// players sitting at global rank 5 and rank 205 were both told they were `#5`. A
        /// wrong-but-plausible number is worse than none - the user has no way to tell it's wrong -
        /// so when the server omits your_rank we collapse the badge and show nothing at all.
        /// </summary>
        private void UpdateYourRankDisplay()
        {
            try
            {
                var txt = LeaderboardTab?.TxtYourRank;
                if (txt == null) return;

                var serverRank = App.Leaderboard?.YourRank;
                var rank = serverRank is > 0 ? serverRank.Value : 0;

                if (rank > 0)
                {
                    var total = App.Leaderboard?.YourTotal;
                    txt.Text = total is > 0
                        ? Loc.GetF("label_your_rank_0_of_1", rank, total.Value)
                        : Loc.GetF("label_your_rank_0", rank);
                    txt.Visibility = Visibility.Visible;
                }
                else
                {
                    txt.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to update your-rank display");
            }
        }

        internal async void BtnLeaderboardMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string mode && mode != _leaderboardMode)
            {
                _leaderboardMode = mode;
                UpdateLeaderboardModeButtons();
                // Both boards open in canonical rank order (which is XP order on the server's
                // sorted set) so the podium and the tier bands line up with the ranks.
                _leaderboardSortKey = "rank";
                await RefreshLeaderboardAsync();
            }
        }

        private void UpdateLeaderboardModeButtons()
        {
            try
            {
                if (LeaderboardTab?.BtnLeaderboardMonthly == null || LeaderboardTab.BtnLeaderboardAllTime == null) return;

                var isAllTime = _leaderboardMode == "all-time";
                var gold = (Color)ColorConverter.ConvertFromString("#FFD700");
                var pink = (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4");
                var dark = Application.Current.Resources["DarkerBg"] is Color dbg ? dbg : (Color)ColorConverter.ConvertFromString("#1A1A2E");
                var inactive = Application.Current.Resources["AccentTintedBg"] is Color itbg ? itbg : (Color)ColorConverter.ConvertFromString("#352545");

                if (isAllTime)
                {
                    LeaderboardTab.BtnLeaderboardMonthly.Background = new SolidColorBrush(inactive);
                    LeaderboardTab.BtnLeaderboardMonthly.Foreground = new SolidColorBrush(pink);
                    LeaderboardTab.BtnLeaderboardAllTime.Background = new SolidColorBrush(gold);
                    LeaderboardTab.BtnLeaderboardAllTime.Foreground = new SolidColorBrush(dark);
                }
                else
                {
                    LeaderboardTab.BtnLeaderboardMonthly.Background = new SolidColorBrush(pink);
                    LeaderboardTab.BtnLeaderboardMonthly.Foreground = new SolidColorBrush(Colors.White);
                    LeaderboardTab.BtnLeaderboardAllTime.Background = new SolidColorBrush(inactive);
                    LeaderboardTab.BtnLeaderboardAllTime.Foreground = new SolidColorBrush(pink);
                }

                // Update All-Time button border color
                var allTimeBorder = LeaderboardTab.BtnLeaderboardAllTime.Template?.FindName("AllTimeBorder", LeaderboardTab.BtnLeaderboardAllTime) as Border;
                if (allTimeBorder != null)
                    allTimeBorder.BorderBrush = new SolidColorBrush(isAllTime ? gold : pink);

                // The header's season block owns the countdown; tell it which board we're on.
                LeaderboardTab.SetLeaderboardMode(isAllTime);

                // Apply accent theme to rows and the sticky "you" bar
                ApplyLeaderboardTheme(isAllTime ? gold : pink);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error updating leaderboard mode buttons");
            }
        }

        /// <summary>
        /// Re-tint the roster for the active board. Monthly runs on the mod accent, All-Time on
        /// gold. This used to also rebuild a GridViewColumnHeader style; the GridView is gone, so
        /// the only container left to theme is the roster's ListViewItem.
        /// </summary>
        private void ApplyLeaderboardTheme(Color accent)
        {
            if (LeaderboardTab?.LstLeaderboard == null) return;

            var accentBrush = new SolidColorBrush(accent);
            var tint20 = new SolidColorBrush(Color.FromArgb(0x20, accent.R, accent.G, accent.B));
            var tint40 = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            var hoverBrush = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B));
            var selectedBrush = new SolidColorBrush(Color.FromArgb(0x3C, accent.R, accent.G, accent.B));

            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(ForegroundProperty, accentBrush));
            itemStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new Setter(FontFamilyProperty, new FontFamily("Segoe UI")));
            itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            // OG gold-tinted row (always gold regardless of mode)
            var ogTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("IsSeason0Og"), Value = true };
            ogTrigger.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xD7, 0x00))));
            itemStyle.Triggers.Add(ogTrigger);

            // The local user's own row gets an accent tint + left rule (added after OG so it wins).
            var meTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("IsCurrentUser"), Value = true };
            meTrigger.Setters.Add(new Setter(BackgroundProperty, tint20));
            meTrigger.Setters.Add(new Setter(BorderBrushProperty, accentBrush));
            meTrigger.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
            itemStyle.Triggers.Add(meTrigger);

            var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(BackgroundProperty, hoverBrush));
            itemStyle.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(BackgroundProperty, selectedBrush));
            itemStyle.Triggers.Add(selectedTrigger);

            // Tier bands are inert separators. Added LAST so they beat hover/selection.
            var bandTrigger = new DataTrigger { Binding = new System.Windows.Data.Binding("IsBand"), Value = true };
            bandTrigger.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            bandTrigger.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            bandTrigger.Setters.Add(new Setter(FocusableProperty, false));
            bandTrigger.Setters.Add(new Setter(IsHitTestVisibleProperty, false));
            itemStyle.Triggers.Add(bandTrigger);

            LeaderboardTab.LstLeaderboard.ItemContainerStyle = itemStyle;

            // Sticky "you" bar follows the same accent.
            if (LeaderboardTab.YouBar != null)
            {
                LeaderboardTab.YouBar.Background = tint20;
                LeaderboardTab.YouBar.BorderBrush = tint40;
            }
        }

        internal void BtnLeaderboardDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string discordId && !string.IsNullOrEmpty(discordId))
            {
                try
                {
                    // Use rundll32 to force opening in default browser - this bypasses app URL handlers
                    var url = $"https://discord.com/users/{discordId}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "rundll32",
                        Arguments = $"url.dll,FileProtocolHandler {url}",
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to open Discord profile for user {DiscordId}", discordId);
                }
            }
        }

        internal void LstLeaderboard_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Get the double-clicked item
            if (LeaderboardTab?.LstLeaderboard?.SelectedItem is Services.LeaderboardEntry entry && !string.IsNullOrEmpty(entry.DisplayName))
            {
                App.Logger?.Information("Leaderboard double-click: Opening profile for {DisplayName}", entry.DisplayName);

                // Switch to Discord tab (which contains the Profile Viewer)
                ShowTab("discord");

                // Set the search text and display the profile
                if (DiscordTab.TxtProfileSearch != null)
                {
                    DiscordTab.TxtProfileSearch.Text = entry.DisplayName;
                }
                SearchAndDisplayProfile(entry.DisplayName);
            }
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------

        private async Task RefreshLeaderboardAsync(string? sortBy = null)
        {
            if (App.Leaderboard == null || LeaderboardTab?.TxtLeaderboardStatus == null || LeaderboardTab.BtnRefreshLeaderboard == null) return;

            LeaderboardTab.TxtLeaderboardStatus.Text = Loc.Get("label_syncing");
            LeaderboardTab.BtnRefreshLeaderboard.IsEnabled = false;

            try
            {
                // Sync local stats to cloud first so leaderboard shows latest data
                var profileSync = App.ProfileSync;
                var syncEnabled = profileSync?.IsSyncEnabled == true;
                App.Logger?.Information("Leaderboard refresh: IsSyncEnabled={SyncEnabled}, ProfileSync={HasProfileSync}, Patreon={HasPatreon}, Authenticated={IsAuth}",
                    syncEnabled,
                    profileSync != null,
                    App.Patreon != null,
                    App.Patreon?.IsAuthenticated);

                if (syncEnabled && profileSync != null)
                {
                    App.Logger?.Information("Syncing profile before leaderboard refresh...");
                    App.Achievements?.Save(); // Save any pending achievements first
                    await profileSync.SyncProfileAsync();
                    App.Logger?.Information("Profile sync completed");
                }

                LeaderboardTab.TxtLeaderboardStatus.Text = Loc.Get("label_loading_2");
                var success = await App.Leaderboard.RefreshAsync(sortBy, _leaderboardMode);

                if (success)
                {
                    // Assign canonical ranks + bake rank deltas, then render.
                    RankLeaderboardEntries();
                    ApplyLeaderboardSort(sortBy ?? "rank");

                    LeaderboardTab.TxtLeaderboardStatus.Text =
                        Loc.GetF("lb_online_and_total", App.Leaderboard.OnlineUsers, App.Leaderboard.TotalUsers);

                    // Season name + countdown are derived locally by the tab.
                    LeaderboardTab.SetLeaderboardMode(_leaderboardMode == "all-time");

                    // Show/hide Trophy Case stats based on skill unlock
                    UpdateTrophyCaseColumns();

                    // Bark hook: react to the user's standing when the board loads.
                    try { App.Bark?.NotifyLeaderboardViewed(App.Leaderboard.YourRank ?? 0, App.Leaderboard.TotalUsers); } catch { }
                }
                else
                {
                    LeaderboardTab.TxtLeaderboardStatus.Text = App.Leaderboard.LastRefreshError ?? Loc.Get("label_failed_to_load");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error refreshing leaderboard");
                LeaderboardTab.TxtLeaderboardStatus.Text = Loc.Get("label_error_loading_leaderboard");
            }
            finally
            {
                LeaderboardTab.BtnRefreshLeaderboard.IsEnabled = true;
            }
        }

        /// <summary>
        /// Assign the canonical rank to every fetched entry and bake in the rank delta.
        ///
        /// Order matters and is load-bearing: <see cref="LeaderboardRankSnapshotService.RecordIfDue"/>
        /// updates the in-memory cache as well as disk, so every GetPreviousRank issued after it
        /// returns the value we just wrote and every arrow renders "no change". So we (1) rank,
        /// (2) READ every previous rank eagerly into the row, (3) only then re-record. Nothing
        /// looks the previous rank up lazily from a binding — a virtualized row realises on
        /// scroll, i.e. long after step 3.
        /// </summary>
        private void RankLeaderboardEntries()
        {
            if (App.Leaderboard?.Entries == null) { _leaderboardRanked = new List<Services.LeaderboardEntry>(); return; }

            var isAllTime = _leaderboardMode == "all-time";

            // Rank by the same key the server's sorted set uses, so a row's Rank agrees with
            // the server-provided YourRank instead of drifting from it.
            var ordered = isAllTime
                ? App.Leaderboard.Entries.OrderByDescending(x => x.TotalXpEarned).ThenByDescending(x => x.HighestLevelEver).ToList()
                : App.Leaderboard.Entries.OrderByDescending(x => x.Xp).ThenByDescending(x => x.Level).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Rank = i + 1;
                ordered[i].IsAllTimeView = isAllTime;
            }

            // (2) READ - eager, before anything writes a new snapshot.
            var myId = App.UnifiedUserId;
            _youPreviousRankKnown = false;
            _youPreviousRank = null;

            foreach (var entry in ordered)
            {
                if (string.IsNullOrEmpty(entry.UnifiedId))
                {
                    // Nothing to key a snapshot on — a muted dash, not a false "NEW".
                    entry.ApplyRankDelta(null, known: false);
                    continue;
                }

                int? previous = null;
                try { previous = LeaderboardRankSnapshotService.GetPreviousRank(_leaderboardMode, entry.UnifiedId); }
                catch (Exception ex) { App.Logger?.Debug(ex, "Rank snapshot lookup failed"); }

                // A null previous rank is the normal case, not an error: the snapshot keeps only
                // the top 500 and drops anything without a unified id.
                entry.ApplyRankDelta(previous, known: true);
            }

            if (!string.IsNullOrEmpty(myId))
            {
                _youPreviousRankKnown = true;
                try { _youPreviousRank = LeaderboardRankSnapshotService.GetPreviousRank(_leaderboardMode, myId); }
                catch { _youPreviousRankKnown = false; }
            }

            // (3) WRITE - only now.
            try { LeaderboardRankSnapshotService.RecordIfDue(_leaderboardMode, ordered); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Failed to record leaderboard rank snapshot"); }

            _leaderboardRanked = ordered;
        }

        /// <summary>
        /// Change the display sort and re-render. Note this no longer re-numbers ranks — see
        /// <see cref="_leaderboardRanked"/>.
        /// </summary>
        private void ApplyLeaderboardSort(string sortBy)
        {
            _leaderboardSortKey = string.IsNullOrEmpty(sortBy) ? "rank" : sortBy;
            RebuildLeaderboardView();
        }

        // ------------------------------------------------------------------
        // Rendering
        // ------------------------------------------------------------------

        /// <summary>
        /// Apply filter + search + sort, build the podium, inject tier bands and push the
        /// heterogeneous ItemsSource at the roster.
        /// </summary>
        private void RebuildLeaderboardView()
        {
            var tab = LeaderboardTab;
            if (tab?.LstLeaderboard == null) return;

            try
            {
                IEnumerable<Services.LeaderboardEntry> query = _leaderboardRanked;

                switch (_leaderboardFilter)
                {
                    case "online": query = query.Where(x => x.IsOnline); break;
                    case "patrons": query = query.Where(x => x.EffectivePatreonTier > 0); break;
                    case "og": query = query.Where(x => x.IsSeason0Og); break;
                }

                var search = _leaderboardSearch.Trim();
                if (search.Length > 0)
                {
                    query = query.Where(x => (x.DisplayName ?? "").IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
                }

                var view = _leaderboardSortKey switch
                {
                    "name" => query.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    "level" => query.OrderByDescending(x => x.LevelColumnValue).ThenBy(x => x.Rank).ToList(),
                    "achievements" => query.OrderByDescending(x => x.AchievementsCount).ThenBy(x => x.Rank).ToList(),
                    "streak" => query.OrderByDescending(x => x.HighestStreak).ThenBy(x => x.Rank).ToList(),
                    _ => query.OrderBy(x => x.Rank).ToList(),
                };

                // Podium + tier bands only make sense on the untouched, rank-ordered board.
                var isCanonical = _leaderboardFilter == "all"
                                  && search.Length == 0
                                  && (_leaderboardSortKey is "rank" or "xp");

                var showPodium = isCanonical && view.Count >= 3;

                if (tab.PodiumHost != null)
                {
                    if (showPodium)
                    {
                        // Silver, gold, bronze — #1 sits in the middle.
                        tab.PodiumHost.ItemsSource = new List<Services.LeaderboardEntry> { view[1], view[0], view[2] };
                        tab.PodiumHost.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        tab.PodiumHost.ItemsSource = null;
                        tab.PodiumHost.Visibility = Visibility.Collapsed;
                    }
                }

                var display = new List<object>(view.Count + 8);
                var lastBand = -1;

                for (int i = showPodium ? 3 : 0; i < view.Count; i++)
                {
                    var entry = view[i];

                    if (isCanonical)
                    {
                        var band = TierIndexForRank(entry.Rank);
                        // Band 0 (ranks 1-3) never gets a divider — the podium is the divider.
                        if (band != lastBand)
                        {
                            if (band > 0) display.Add(BuildTierBand(band));
                            lastBand = band;
                        }
                    }

                    display.Add(entry);
                }

                tab.LstLeaderboard.ItemsSource = display;

                if (tab.TxtLeaderboardEmpty != null)
                {
                    tab.TxtLeaderboardEmpty.Visibility = view.Count == 0 && _leaderboardRanked.Count > 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                UpdateYourRankDisplay();
                UpdateYouBar();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to rebuild leaderboard view");
            }
        }

        // ------------------------------------------------------------------
        // Tier bands
        // ------------------------------------------------------------------

        /// <summary>0 = 1-3, 1 = 4-10, 2 = 11-25, 3 = 26-50, 4 = 51-100, 5 = 101-200, 6 = 201+.</summary>
        private static int TierIndexForRank(int rank)
        {
            if (rank <= 3) return 0;
            if (rank <= 10) return 1;
            if (rank <= 25) return 2;
            if (rank <= 50) return 3;
            if (rank <= 100) return 4;
            if (rank <= 200) return 5;
            return 6;
        }

        private static readonly string[] TierNameKeys =
        {
            "lb_tier_dissolved", "lb_tier_hollowed", "lb_tier_spiralbound",
            "lb_tier_sunken", "lb_tier_pliant", "lb_tier_drifting", "lb_tier_blinking"
        };

        private static readonly string[] TierSubKeys =
        {
            "lb_tier_dissolved_sub", "lb_tier_hollowed_sub", "lb_tier_spiralbound_sub",
            "lb_tier_sunken_sub", "lb_tier_pliant_sub", "lb_tier_drifting_sub", "lb_tier_blinking_sub"
        };

        private static readonly int[] TierLowerBounds = { 1, 4, 11, 26, 51, 101, 201 };
        private static readonly int[] TierUpperBounds = { 3, 10, 25, 50, 100, 200, 0 };

        private static LeaderboardTierBand BuildTierBand(int index)
        {
            index = Math.Clamp(index, 0, TierNameKeys.Length - 1);

            var name = Loc.Get(TierNameKeys[index]);
            var range = TierUpperBounds[index] > 0
                ? Loc.GetF("lb_tier_range", TierLowerBounds[index], TierUpperBounds[index])
                : Loc.GetF("lb_tier_range_open", TierLowerBounds[index]);

            return new LeaderboardTierBand
            {
                HeaderText = $"{name}   ·   {range}",
                Subtitle = Loc.Get(TierSubKeys[index])
            };
        }

        // ------------------------------------------------------------------
        // Sticky "you" bar
        // ------------------------------------------------------------------

        /// <summary>
        /// Fill the always-visible "you" bar. It has to work for a player at rank #684 who is
        /// nowhere in the fetched top-200 slice, so the rank number comes from
        /// <see cref="LeaderboardService.YourRank"/> and never from a row's own Rank.
        /// </summary>
        private void UpdateYouBar()
        {
            var tab = LeaderboardTab;
            if (tab?.YouBar == null) return;

            try
            {
                var serverRank = App.Leaderboard?.YourRank;
                var rank = serverRank is > 0 ? serverRank.Value : 0;
                var name = App.UserDisplayName;

                if (rank <= 0 && string.IsNullOrWhiteSpace(name))
                {
                    tab.YouBar.Visibility = Visibility.Collapsed;
                    return;
                }
                tab.YouBar.Visibility = Visibility.Visible;

                // Am I inside the fetched slice?
                var myId = App.UnifiedUserId;
                Services.LeaderboardEntry? me = null;
                int myIndex = -1;
                if (!string.IsNullOrEmpty(myId))
                {
                    for (int i = 0; i < _leaderboardRanked.Count; i++)
                    {
                        if (_leaderboardRanked[i].UnifiedId == myId) { me = _leaderboardRanked[i]; myIndex = i; break; }
                    }
                }

                // --- rank + delta ---
                if (tab.TxtYouRankNumber != null)
                    tab.TxtYouRankNumber.Text = rank > 0 ? rank.ToString() : "–";

                if (tab.TxtYouDelta != null)
                {
                    if (!_youPreviousRankKnown || rank <= 0)
                    {
                        tab.TxtYouDelta.Text = "–";
                        tab.TxtYouDelta.Foreground = (Brush)FindResource("TextDimBrush");
                    }
                    else if (_youPreviousRank is not > 0)
                    {
                        tab.TxtYouDelta.Text = Loc.Get("lb_delta_new");
                        tab.TxtYouDelta.Foreground = (Brush)FindResource("PinkSoftBrush");
                    }
                    else
                    {
                        var moved = _youPreviousRank.Value - rank;
                        if (moved > 0)
                        {
                            tab.TxtYouDelta.Text = "▲" + moved;
                            tab.TxtYouDelta.Foreground = (Brush)FindResource("SuccessGreenBrush");
                        }
                        else if (moved < 0)
                        {
                            tab.TxtYouDelta.Text = "▼" + (-moved);
                            tab.TxtYouDelta.Foreground = (Brush)FindResource("DangerBrush");
                        }
                        else
                        {
                            tab.TxtYouDelta.Text = "–";
                            tab.TxtYouDelta.Foreground = (Brush)FindResource("TextDimBrush");
                        }
                    }
                }

                // --- identity ---
                if (tab.TxtYouName != null) tab.TxtYouName.Text = name ?? "";
                if (tab.TxtYouInitials != null) tab.TxtYouInitials.Text = Services.LeaderboardEntry.BuildInitials(name);
                if (tab.EllYouAvatar != null) tab.EllYouAvatar.Fill = Services.LeaderboardEntry.BuildAvatarBrush(name);

                // --- level / xp ---
                var isAllTime = _leaderboardMode == "all-time";
                if (tab.TxtYouLevel != null)
                {
                    // Outside the fetched slice we have to source the level locally. The All-Time
                    // board's level column is highest_level_ever (headed "Peak"), so falling back to
                    // the current level there prints a smaller number under a header promising the
                    // opposite. AppSettings tracks the same value: ProgressionService raises it on
                    // level-up and ProfileSyncService overwrites it from the server, which is
                    // authoritative - so it survives the season resets that make PlayerLevel drop.
                    var localLevel = isAllTime
                        ? (App.Settings?.Current?.HighestLevelEver ?? 0)
                        : (App.Settings?.Current?.PlayerLevel ?? 0);
                    var level = me?.LevelColumnValue ?? localLevel;
                    tab.TxtYouLevel.Text = level > 0 ? level.ToString() : "–";
                }

                if (tab.TxtYouXp != null)
                {
                    if (me != null) tab.TxtYouXp.Text = me.XpColumnDisplay;
                    else if (!isAllTime && App.Settings?.Current != null && App.Progression != null)
                    {
                        // Outside the slice: the board's "xp" is total accumulated XP, which is
                        // exactly what GetTotalXP reconstructs from the local level + progress.
                        var total = App.Progression.GetTotalXP(App.Settings.Current.PlayerLevel, App.Settings.Current.PlayerXP);
                        tab.TxtYouXp.Text = FormatCompact(total);
                    }
                    else tab.TxtYouXp.Text = "–";
                }

                // --- achievements ---
                var earned = me?.AchievementsCount ?? (App.Achievements?.GetUnlockedCount() ?? 0);
                var earnable = Math.Max(1, EarnableAchievementCount);
                if (tab.TxtYouAchievements != null) tab.TxtYouAchievements.Text = $"{earned} / {earnable}";
                if (tab.BarYouAchievements != null)
                {
                    tab.BarYouAchievements.Maximum = earnable;
                    tab.BarYouAchievements.Value = Math.Min(earned, earnable);
                }

                // --- gap line ---
                if (tab.TxtYouGap != null)
                {
                    string? gap = null;
                    if (me != null)
                    {
                        if (me.Rank <= 1 || myIndex <= 0)
                        {
                            gap = Loc.Get("lb_gap_leader");
                        }
                        else
                        {
                            var above = _leaderboardRanked[myIndex - 1];
                            var diff = above.XpColumnValue - me.XpColumnValue;
                            gap = diff > 0
                                ? Loc.GetF("lb_gap_to_next", diff.ToString("N0"), above.DisplayName, above.Rank)
                                : Loc.Get("lb_gap_unknown");
                        }
                    }

                    tab.TxtYouGap.Text = gap ?? "";
                    tab.TxtYouGap.Visibility = string.IsNullOrEmpty(gap) ? Visibility.Collapsed : Visibility.Visible;
                }

                // --- top N% ---
                if (tab.TxtYouPercent != null)
                {
                    var pct = App.Leaderboard?.GetPlayerPercentile() ?? 0;
                    if (pct > 0)
                    {
                        tab.TxtYouPercent.Text = Loc.GetF("lb_top_percent", pct);
                        tab.TxtYouPercent.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        tab.TxtYouPercent.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to update leaderboard 'you' bar");
            }
        }

        private static string FormatCompact(double value)
        {
            if (value >= 1_000_000) return $"{value / 1_000_000.0:F1}M";
            if (value >= 1_000) return $"{value / 1_000.0:F1}k";
            return ((int)value).ToString();
        }

        /// <summary>
        /// Show or hide the Trophy Case stats (Streak column + Best Session tooltip line) based
        /// on whether the viewer has unlocked the skill. Name kept: MainWindow.Enhancements.cs
        /// calls this the moment the skill is purchased.
        /// </summary>
        private void UpdateTrophyCaseColumns()
        {
            try
            {
                if (LeaderboardTab == null) return;

                var hasTrophyCase = App.SkillTree?.HasSkill("trophy_case") == true;
                LeaderboardTab.ShowTrophyStats = hasTrophyCase;

                App.Logger?.Debug("Trophy Case stats visibility updated: {Visible}", hasTrophyCase);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error updating Trophy Case stats");
            }
        }

        #endregion
    }
}
