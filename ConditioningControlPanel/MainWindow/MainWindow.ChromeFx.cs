using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-1 of the FX overhaul: the *chrome* pass. Everything in here decorates the shell every
    /// tab shares - tab transitions, the nav rail's rows, the START CTA, the XP bar and the
    /// support banner/marquee - and nothing in here touches an individual tab's content.
    ///
    /// House rules this file obeys (FX plan section 2):
    ///   * Two clocks. Interaction motion is 80-400ms (tab choreography, hover, sheen passes);
    ///     ambient motion is 3-8s+ loops (glow breath, XP gloss). Nothing in between.
    ///   * Every ambient clock asks <see cref="MotionFx.AllowAmbientLoops"/> (which folds in the
    ///     reduced-motion setting AND the performance tier) BEFORE it starts, parks at a static
    ///     resting state otherwise, and stops when the window is deactivated or minimised.
    ///   * Colour only ever comes from FxTheme / the Fx* dynamic resources, so a mod switch
    ///     re-tints the chrome without a single literal hex here.
    ///   * Animations are started with <c>element.BeginAnimation</c>, never
    ///     Storyboard.SetTargetName - that silently no-ops across the tab UserControl namescopes.
    ///   * Every callback is wrapped; none of this may ever be the reason the shell throws.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        private const int TabFadeOutMs = 100;
        private const int TabFadeInMs = 200;
        private const double TabSlidePx = 12;
        private const double NavIconHoverScale = 1.06;
        private const int NavIconHoverMs = 150;
        private const double NavGlowMinOpacity = 0.6;
        private const double NavGlowMaxOpacity = 1.0;
        private const double NavGlowBreathSeconds = 3.4;
        private const double StartGlowMinOpacity = 0.32;
        private const double StartGlowMaxOpacity = 0.72;
        private const double StartGlowBreathSeconds = 4.6;
        private const double StartSheenSeconds = 0.85;
        private const int StartSheenIntervalSeconds = 8;
        private const double BannerSheenSeconds = 0.75;
        /// <summary>Banner messages rotate every 4s; a sheen on every rotation would be an
        /// ambient strobe rather than a "something changed" cue, so passes are spaced out.</summary>
        private const int BannerSheenMinGapSeconds = 20;
        private const double XpSheenSeconds = 6.0;

        // Nav order, slide direction and the WebView2 airspace list are pure decisions and live in
        // Services/ChromeFxNav.cs so they can be tested without a window.

        // ---- state -----------------------------------------------------------------

        private bool _chromeFxInitialized;
        private bool _chromeFxWindowActive = true;
        private string _pendingTabKey = "settings";
        private string _activeTabKey = "settings";
        private UIElement? _activeTabElement;
        private Button? _navGlowButton;
        private Button? _navGlowDoor;
        private FrameworkElement? _navActiveBar;
        private DispatcherTimer? _startSheenTimer;
        private DateTime _lastBannerSheenUtc = DateTime.MinValue;
        private DispatcherTimer? _staggerCleanupTimer;
        private List<FrameworkElement>? _staggeredElements;
        private double _lastXpShown = double.NaN;
        private int _lastXpLevelShown = -1;

        /// <summary>
        /// Every clickable row in the nav rail: seven door headers then the entries, rail order.
        /// Null-tolerant: a couple are conditionally present.
        ///
        /// <para>This list is the ONLY thing that subscribes a rail row to the icon hover nudge
        /// (§2.1). A row left out of it still gets the style's hover background, so the omission
        /// reads as "that button feels slightly dead" rather than as a bug - keep it in sync with
        /// the DoorEntries* panels in MainWindow.xaml whenever a row is added.</para>
        /// </summary>
        private IEnumerable<Button> NavButtons
        {
            get
            {
                var all = new[]
                {
                    DoorHome, DoorStudio, DoorCompanion, DoorPlay, DoorYou, DoorLibrary, DoorJustDrop, DoorSettings,
                    BtnSettings,
                    BtnNavStudio, BtnPresets, BtnNavHaptics,
                    BtnCompanion, BtnNavBambiTakeover, BtnNavSheListening, BtnNavAwareness,
                    BtnLab, BtnDeeper, BtnPatreonExclusives, BtnNavGradedIntake, BtnNavLockdown,
                    BtnNavBlinkTrainer, BtnNavRemoteControl, BtnAvailableSubjects,
                    BtnDiscordTab, BtnQuests, BtnAchievements, BtnEnhancements, BtnPrograms, BtnLeaderboard,
                    BtnOpenAssetsTop, BtnNavMods, BtnNavCatalogue, BtnNavPhrases, BtnNavMediaLog,
                    // Withheld by default: the hover FX subscribe harmlessly to a Collapsed row,
                    // and the row is revealed in place when the server flag lands.
                    BtnNavJustDrop,
                };
                return all.Where(b => b != null)!;
            }
        }

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires the chrome FX up once, after the window is loaded. Safe to call twice.
        /// </summary>
        internal void InitializeChromeFx()
        {
            if (_chromeFxInitialized) return;
            _chromeFxInitialized = true;
            try
            {
                // The dashboard is on screen at startup without ever going through AnimateTabIn,
                // so seed the transition state or the first switch has nothing to fade out.
                _activeTabElement ??= SettingsTab;

                foreach (var btn in NavButtons)
                {
                    btn.MouseEnter += NavButton_MouseEnter;
                    btn.MouseLeave += NavButton_MouseLeave;
                }

                Activated += OnChromeFxWindowStateish;
                Deactivated += OnChromeFxWindowStateish;
                StateChanged += OnChromeFxWindowStateish;

                _startSheenTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(StartSheenIntervalSeconds),
                };
                _startSheenTimer.Tick += (_, __) => SweepStartSheen();

                // Build the event burst surface now rather than on the first level-up. It costs one
                // small Skia bitmap and no clock, and it means the first event moment never has to
                // wait for a Loaded event to make its canvas eligible to tick.
                EnsureEventBurstLayer();

                ApplyNavActiveGlow(NavButtonForTab(_activeTabKey));
                ApplyChromeFxLoops();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeChromeFx failed"); }
        }

        private void OnChromeFxWindowStateish(object? sender, EventArgs e)
        {
            try
            {
                var active = IsActive && WindowState != WindowState.Minimized;
                if (active == _chromeFxWindowActive) return;
                _chromeFxWindowActive = active;
                ApplyChromeFxLoops();
            }
            catch (Exception ex) { App.Logger?.Debug("ChromeFx activation: {E}", ex.Message); }
        }

        /// <summary>
        /// Re-reads the palette and re-evaluates every chrome loop. Called from
        /// RefreshThemeAwareElements (so a mod switch re-tints the glow live) and from the
        /// reduced-motion picker (so lowering the setting stops running loops immediately).
        /// </summary>
        internal void RefreshChromeFx()
        {
            if (!_chromeFxInitialized) return;
            try
            {
                ApplyNavActiveGlow(_navGlowButton);
                ApplyChromeFxLoops();
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshChromeFx: {E}", ex.Message); }
        }

        /// <summary>The single gate for every chrome ambient loop: window focus + motion + tier.</summary>
        private bool ChromeAmbientAllowed => _chromeFxWindowActive && MotionFx.AllowAmbientLoops;

        private void ApplyChromeFxLoops()
        {
            ApplyNavGlowBreath();
            ApplyStartButtonGlow();
            ApplyXpSheen();
            // PR-2's dashboard loops ride the same funnel rather than hooking Activated /
            // Deactivated / StateChanged a second time. No-op until InitializeDashboardFx runs.
            ApplyDashboardFxLoops();

            try
            {
                if (_startSheenTimer == null) return;
                if (ChromeAmbientAllowed)
                {
                    if (!_startSheenTimer.IsEnabled) _startSheenTimer.Start();
                }
                else
                {
                    _startSheenTimer.Stop();
                    ParkSheen(StartSheen, StartSheenSlide);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyChromeFxLoops: {E}", ex.Message); }
        }

        // ============================== 1. tab transitions ==============================

        /// <summary>
        /// Tab entrance choreography. ShowTab parks the incoming tab key in
        /// <see cref="_pendingTabKey"/> just before it runs, so the ~25 existing call sites stay
        /// as they are and still get a direction.
        ///
        /// Off      -> instant swap, no clocks at all.
        /// Reduced  -> plain crossfade (the one thing reduced motion keeps).
        /// Full     -> outgoing 100ms fade, incoming 12px directional slide + 200ms fade,
        ///             then a capped entrance stagger on the tab's top-level cards.
        /// </summary>
        private void AnimateTabIn(UIElement tab)
        {
            var key = _pendingTabKey;
            var fromKey = _activeTabKey;
            var outgoing = _activeTabElement;
            _activeTabElement = tab;
            _activeTabKey = key;

            try
            {
                CancelStaggerCleanup();

                var level = MotionFx.Level;
                if (level == MotionLevel.Off)
                {
                    // Instant swap. Clear any clock the previous switch left holding a value.
                    tab.BeginAnimation(OpacityProperty, null);
                    tab.Opacity = 1;
                    ResetTabSlide(tab);
                    CollapseOutgoingTab(outgoing, tab);
                    return;
                }

                // Leaving a WebView2 tab, the browser HWND ignores the fade and would sit fully
                // opaque on top of the incoming tab for the whole 100ms. Cut straight away instead.
                if (ChromeFxNav.IsAirspaceTab(fromKey)) CollapseOutgoingTab(outgoing, tab);
                else FadeOutgoingTab(outgoing, tab);

                tab.Opacity = 0;
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(TabFadeInMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                tab.BeginAnimation(OpacityProperty, fade);

                // Reduced keeps the crossfade and drops the parallax; WebView2 tabs drop it too,
                // because the native browser HWND cannot follow a RenderTransform.
                if (level == MotionLevel.Reduced || ChromeFxNav.IsAirspaceTab(key))
                {
                    ResetTabSlide(tab);
                    return;
                }

                SlideTabIn(tab, fromKey, key);
                StaggerTabCards(tab, key);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AnimateTabIn: {E}", ex.Message);
                try { tab.Opacity = 1; } catch { }
            }
        }

        /// <summary>
        /// 12px directional slide. Nav order decides the axis: a tab further DOWN the rail
        /// enters from the right, one further up enters from the left, so the movement still
        /// reads as forward/back through the IA. Anything off the rail gets a plain rise
        /// instead of a guessed direction.
        /// </summary>
        private void SlideTabIn(UIElement tab, string fromKey, string toKey)
        {
            if (tab is not FrameworkElement fe) return;
            var slide = EnsureTabTranslate(fe);
            if (slide == null) return; // the tab authored its own transform - do not fight it

            var (dx, dy) = ChromeFxNav.EntranceOffset(fromKey, toKey, TabSlidePx);
            bool horizontal = dx != 0;

            var anim = new DoubleAnimation(horizontal ? dx : dy, 0, TimeSpan.FromMilliseconds(TabFadeInMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };

            if (horizontal)
            {
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.Y = 0;
                slide.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            else
            {
                slide.BeginAnimation(TranslateTransform.XProperty, null);
                slide.X = 0;
                slide.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        /// <summary>
        /// Quick fade-out on the tab we just left. ShowTab has already collapsed it, so it gets
        /// shown again for the length of the fade and re-collapsed on completion - a layout pass
        /// never lands in between, so there is no flicker. It is made hit-test invisible for the
        /// duration: an Opacity-0 element still swallows clicks, and that would be a real bug if
        /// a completion callback were ever dropped.
        /// </summary>
        private void FadeOutgoingTab(UIElement? outgoing, UIElement incoming)
        {
            if (outgoing is not FrameworkElement fe || ReferenceEquals(outgoing, incoming)) return;
            try
            {
                fe.IsHitTestVisible = false;
                fe.Visibility = Visibility.Visible;
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(TabFadeOutMs));
                fade.Completed += (_, __) =>
                {
                    try
                    {
                        // The user came back to it inside 100ms: its own entrance owns it now.
                        if (ReferenceEquals(fe, _activeTabElement)) return;
                        fe.BeginAnimation(OpacityProperty, null);
                        fe.Opacity = 1;
                        fe.IsHitTestVisible = true;
                        fe.Visibility = Visibility.Collapsed;
                    }
                    catch (Exception ex) { App.Logger?.Debug("Tab fade-out completion: {E}", ex.Message); }
                };
                fe.BeginAnimation(OpacityProperty, fade);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FadeOutgoingTab: {E}", ex.Message);
                try { CollapseOutgoingTab(outgoing, incoming); } catch { }
            }
        }

        private static void CollapseOutgoingTab(UIElement? outgoing, UIElement incoming)
        {
            if (outgoing is not FrameworkElement fe || ReferenceEquals(outgoing, incoming)) return;
            fe.BeginAnimation(OpacityProperty, null);
            fe.Opacity = 1;
            fe.IsHitTestVisible = true;
            fe.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Installs a TranslateTransform only when the element has no transform of its own -
        /// clobbering an authored RenderTransform is how layout silently breaks, so we decline
        /// to animate instead.
        /// </summary>
        private static TranslateTransform? EnsureTabTranslate(FrameworkElement element)
        {
            if (element.RenderTransform is TranslateTransform existing) return existing;
            var current = element.RenderTransform;
            if (current != null && current != Transform.Identity && !current.Value.IsIdentity) return null;
            if (current is TransformGroup or ScaleTransform or RotateTransform or SkewTransform) return null;
            var slide = new TranslateTransform();
            element.RenderTransform = slide;
            return slide;
        }

        private static void ResetTabSlide(UIElement tab)
        {
            if (tab is not FrameworkElement fe) return;
            if (fe.RenderTransform is not TranslateTransform t) return;
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.X = t.Y = 0;
        }

        /// <summary>
        /// Entrance stagger on the incoming tab's top-level cards (MotionFx caps it at 6 slots,
        /// so a long list never crawls in). The animations are cleared once they land, so the
        /// held Opacity value can never stop later code from dimming one of those cards.
        /// </summary>
        private void StaggerTabCards(UIElement tab, string key)
        {
            try
            {
                var cards = FindStaggerTargets(tab);
                if (cards.Count == 0) return;
                MotionFx.StaggerIn(cards);
                _staggeredElements = cards;

                _staggerCleanupTimer ??= new DispatcherTimer();
                _staggerCleanupTimer.Stop();
                // 6 stagger slots x 40ms + the 260ms rise, plus a little slack.
                _staggerCleanupTimer.Interval = TimeSpan.FromMilliseconds(6 * 40 + 260 + 120);
                _staggerCleanupTimer.Tick -= StaggerCleanupTimer_Tick;
                _staggerCleanupTimer.Tick += StaggerCleanupTimer_Tick;
                _staggerCleanupTimer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("StaggerTabCards({Tab}): {E}", key, ex.Message); }
        }

        private void StaggerCleanupTimer_Tick(object? sender, EventArgs e) => CancelStaggerCleanup();

        private void CancelStaggerCleanup()
        {
            try
            {
                _staggerCleanupTimer?.Stop();
                var items = _staggeredElements;
                _staggeredElements = null;
                if (items == null) return;
                foreach (var item in items)
                {
                    // Deliberately narrower than MotionFx.Stop: clear exactly what StaggerIn
                    // started. Stop() also clears a Width animation, and a card that happens to
                    // own one (a bar, a reveal) must not lose it to an entrance cleanup.
                    item.BeginAnimation(UIElement.OpacityProperty, null);
                    item.Opacity = 1;
                    if (item.RenderTransform is TranslateTransform t)
                    {
                        t.BeginAnimation(TranslateTransform.YProperty, null);
                        t.Y = 0;
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("CancelStaggerCleanup: {E}", ex.Message); }
        }

        /// <summary>
        /// Walks down through single-child wrappers (Border / ScrollViewer / a Grid with one
        /// child) to the first panel that actually holds a row of cards, and returns those.
        /// Logical descent only - no visual-tree walk, so it works before the tab has ever been
        /// measured. Elements the app is deliberately dimming or hiding are left alone.
        /// </summary>
        private static List<FrameworkElement> FindStaggerTargets(DependencyObject? node)
        {
            var empty = new List<FrameworkElement>();
            for (int depth = 0; depth < 8 && node != null; depth++)
            {
                switch (node)
                {
                    case ContentControl cc when cc.Content is DependencyObject inner:
                        node = inner;
                        continue;
                    case Decorator dec when dec.Child != null:
                        node = dec.Child;
                        continue;
                    case Panel panel:
                        var children = panel.Children.OfType<FrameworkElement>()
                            .Where(c => c.Visibility == Visibility.Visible && c.Opacity > 0.99)
                            .ToList();
                        if (children.Count == 1) { node = children[0]; continue; }
                        if (children.Count == 0) return empty;
                        return children.Take(8).ToList();
                    default:
                        return empty;
                }
            }
            return empty;
        }

        // ============================== 2. nav buttons ==============================

        /// <summary>Hover: a small nudge on the button's icon only. The button itself keeps its
        /// existing IsMouseOver background trigger, and BtnPrograms/BtnDeeper keep the
        /// ScaleTransform their attention pulse owns - we never touch the button's transform.</summary>
        private void NavButton_MouseEnter(object sender, MouseEventArgs e) => NudgeNavIcon(sender, true);

        private void NavButton_MouseLeave(object sender, MouseEventArgs e) => NudgeNavIcon(sender, false);

        private void NudgeNavIcon(object sender, bool on)
        {
            try
            {
                if (sender is not Button btn) return;
                // Known crash source: animating through a template that has not been applied yet.
                if (!btn.IsLoaded || btn.Template == null) return;
                if (btn.Content is not Panel panel) return;
                var children = panel.Children.OfType<FrameworkElement>().ToList();
                // Most nav buttons are [emoji Image][label]; the Exclusives launcher draws its
                // star as a leading TextBlock instead, so fall back to the first child when the
                // button clearly has an icon-plus-label shape.
                var icon = children.FirstOrDefault(c => c is Image or Viewbox)
                           ?? (children.Count > 1 && children[0] is TextBlock ? children[0] : null);
                if (icon == null) return;

                var scale = EnsureIconScale(icon);
                if (scale == null) return;

                double to = on ? NavIconHoverScale : 1.0;
                if (!MotionFx.AllowTransitions)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = to;
                    return;
                }

                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(NavIconHoverMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("NudgeNavIcon: {E}", ex.Message); }
        }

        private static ScaleTransform? EnsureIconScale(FrameworkElement icon)
        {
            if (icon.RenderTransform is ScaleTransform existing) return existing;
            var current = icon.RenderTransform;
            if (current != null && current != Transform.Identity && !current.Value.IsIdentity) return null;
            if (current is TransformGroup or TranslateTransform or RotateTransform or SkewTransform) return null;
            var scale = new ScaleTransform(1, 1);
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = scale;
            return scale;
        }

        /// <summary>The rail entry that owns a tab key. Since Phase 1 every reachable key has
        /// its own row, so nothing borrows the Exclusives launcher's highlight any more.</summary>
        private Button? NavButtonForTab(string? tab) => (tab ?? string.Empty).ToLowerInvariant() switch
        {
            "settings" or "progression" => BtnSettings,
            "studio" => BtnNavStudio,
            "presets" => BtnPresets,
            // Phase 4: haptics is a Studio rack module, but it keeps its own rail entry and its
            // own tab key, so it keeps lighting its own row - not the rack's.
            "haptics" => BtnNavHaptics,
            "companion" => BtnCompanion,
            "bambitakeover" => BtnNavBambiTakeover,
            "shelistening" => BtnNavSheListening,
            "awareness" => BtnNavAwareness,
            // Phase 6: one rail row, two keys. "play" is the live key; "lab" is the permanent
            // alias that ShowTab still answers to, and it has to light the same row or a legacy
            // caller leaves the indicator wherever it was.
            "play" or "lab" => BtnLab,
            "deeper" => BtnDeeper,
            "exclusives" => BtnPatreonExclusives,
            "gradedintake" => BtnNavGradedIntake,
            "lockdown" => BtnNavLockdown,
            "blinktrainer" => BtnNavBlinkTrainer,
            "remotecontrol" => BtnNavRemoteControl,
            "availablesubjects" => BtnAvailableSubjects,
            "discord" => BtnDiscordTab,
            "quests" => BtnQuests,
            "achievements" => BtnAchievements,
            "enhancements" => BtnEnhancements,
            "programs" => BtnPrograms,
            "leaderboard" => BtnLeaderboard,
            "assets" => BtnOpenAssetsTop,
            "justdrop" => BtnNavJustDrop,
            // The pinned Settings door is its own entry: header and destination in one row.
            "appsettings" => DoorSettings,
            _ => null,
        };

        /// <summary>
        /// Moves the active-tab indicator: a 3px accent bar on the row's left edge plus a
        /// tinted row background, on the entry AND on its door header (so a collapsed door
        /// still says where you are). Both are template parts of the shared NavRailButton
        /// template - "NavActiveBar" and "NavActiveFill" - not separate elements, so nothing
        /// here depends on the rail's layout.
        ///
        /// This replaced an outer DropShadow, which only ever existed because the old 32px
        /// header strip had no spare vertical room for a real indicator (PLAN decision #12).
        /// The method name is unchanged: ~2 call sites plus RefreshChromeFx depend on it.
        /// </summary>
        private void ApplyNavActiveGlow(Button? active)
        {
            try
            {
                if (_navGlowButton != null && !ReferenceEquals(_navGlowButton, active))
                    SetNavIndicator(_navGlowButton, false);
                _navGlowButton = active;

                var door = NavDoorHeaderForTab(_activeTabKey);
                if (_navGlowDoor != null && !ReferenceEquals(_navGlowDoor, door))
                    SetNavIndicator(_navGlowDoor, false);
                _navGlowDoor = door;
                if (door != null) SetNavIndicator(door, true);

                _navActiveBar = active == null ? null : SetNavIndicator(active, true);
                ApplyNavGlowBreath();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyNavActiveGlow: {E}", ex.Message); }
        }

        /// <summary>
        /// Paints (or clears) the indicator parts of one rail button and hands back the bar so
        /// the breath has something to animate. Template parts, so ApplyTemplate has to have
        /// run - it is called here rather than assumed, because InitializeChromeFx seeds the
        /// indicator before the rail has necessarily been through a render pass.
        /// </summary>
        private static FrameworkElement? SetNavIndicator(Button btn, bool on)
        {
            try
            {
                btn.ApplyTemplate();
                var bar = btn.Template?.FindName("NavActiveBar", btn) as FrameworkElement;
                var fill = btn.Template?.FindName("NavActiveFill", btn) as FrameworkElement;
                if (bar != null)
                {
                    // Always drop the breath clock first: a held Opacity would otherwise
                    // outlive the indicator and dim the next row that reuses this part.
                    bar.BeginAnimation(UIElement.OpacityProperty, null);
                    bar.Opacity = NavGlowMaxOpacity;
                    bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                }
                if (fill != null) fill.Opacity = on ? 1 : 0;
                return on ? bar : null;
            }
            catch { return null; }
        }

        /// <summary>Gentle breath on the active indicator bar only - no other row holds a clock.</summary>
        private void ApplyNavGlowBreath()
        {
            try
            {
                var bar = _navActiveBar;
                if (bar == null) return;
                if (!ChromeAmbientAllowed)
                {
                    bar.BeginAnimation(UIElement.OpacityProperty, null);
                    bar.Opacity = NavGlowMaxOpacity;
                    return;
                }
                var anim = new DoubleAnimation(NavGlowMinOpacity, NavGlowMaxOpacity,
                                               TimeSpan.FromSeconds(NavGlowBreathSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                bar.BeginAnimation(UIElement.OpacityProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyNavGlowBreath: {E}", ex.Message); }
        }

        // ============================== 3. START button ==============================

        /// <summary>
        /// Slow glow breath on the app's one always-on hero CTA. Opacity-only on the pre-baked
        /// glow layer behind the button - an animated BlurRadius would re-rasterise the blur
        /// kernel every frame, which is the expensive thing. The layer is collapsed outright
        /// (not just faded to 0) when the tier disallows glow, so its Effect is never rendered.
        /// </summary>
        private void ApplyStartButtonGlow()
        {
            try
            {
                if (StartGlowLayer == null) return;
                var tier = PerformanceProfile.CurrentTier;
                bool wanted = PerformanceProfile.AllowGlow(tier) && MotionFx.Level != MotionLevel.Off;
                if (!wanted)
                {
                    StartGlowLayer.BeginAnimation(OpacityProperty, null);
                    StartGlowLayer.Opacity = 0;
                    StartGlowLayer.Visibility = Visibility.Collapsed;
                    return;
                }

                StartGlowLayer.Visibility = Visibility.Visible;
                if (StartGlowShadow is { IsFrozen: false })
                    StartGlowShadow.BlurRadius = Math.Min(22, PerformanceProfile.MaxGlowBlurRadius(tier));

                if (!ChromeAmbientAllowed)
                {
                    StartGlowLayer.BeginAnimation(OpacityProperty, null);
                    StartGlowLayer.Opacity = StartGlowMinOpacity;
                    return;
                }
                var anim = new DoubleAnimation(StartGlowMinOpacity, StartGlowMaxOpacity,
                                               TimeSpan.FromSeconds(StartGlowBreathSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                StartGlowLayer.BeginAnimation(OpacityProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyStartButtonGlow: {E}", ex.Message); }
        }

        /// <summary>
        /// Keeps the sheen band inside the button's rounded rect. A plain ClipToBounds would be a
        /// square clip and the band's corners would poke past the CornerRadius=8 button edge.
        /// </summary>
        private void StartSheenHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement host) return;
                if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) { host.Clip = null; return; }
                host.Clip = new RectangleGeometry(
                    new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
            }
            catch (Exception ex) { App.Logger?.Debug("StartSheenHost_SizeChanged: {E}", ex.Message); }
        }

        /// <summary>One 0.85s pass of the START sheen, fired by the 8s ambient timer.</summary>
        private void SweepStartSheen()
        {
            try
            {
                if (!ChromeAmbientAllowed) return;
                // While a session runs the CTA is a red STOP button. Leave it alone: a hero sheen
                // there would be the app advertising the exit.
                if (_isRunning) return;
                SweepSheen(StartSheenHost, StartSheen, StartSheenSlide, StartSheenSeconds, 0.30);
            }
            catch (Exception ex) { App.Logger?.Debug("SweepStartSheen: {E}", ex.Message); }
        }

        /// <summary>
        /// Shared one-shot sheen: slide an angled band across the host and fade it in and out so
        /// it never pops at either edge. Interaction-clock length, so it is allowed under Reduced.
        /// </summary>
        private void SweepSheen(FrameworkElement? host, FrameworkElement? band,
                                TranslateTransform? slide, double seconds, double peakOpacity)
        {
            if (host == null || band == null || slide == null) return;
            double width = host.ActualWidth;
            if (width <= 0 || !band.IsVisible) return;
            double bandWidth = double.IsNaN(band.Width) || band.Width <= 0 ? 80 : band.Width;

            var travel = new DoubleAnimation(-bandWidth, width + bandWidth * 0.25,
                                             TimeSpan.FromSeconds(seconds))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(travel, AmbientFrameRate);
            slide.BeginAnimation(TranslateTransform.XProperty, travel);

            var alpha = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
            alpha.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            alpha.KeyFrames.Add(new LinearDoubleKeyFrame(peakOpacity,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * 0.20))));
            alpha.KeyFrames.Add(new LinearDoubleKeyFrame(peakOpacity,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * 0.75))));
            alpha.KeyFrames.Add(new LinearDoubleKeyFrame(0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            Timeline.SetDesiredFrameRate(alpha, AmbientFrameRate);
            band.BeginAnimation(OpacityProperty, alpha);
        }

        private static void ParkSheen(FrameworkElement? band, TranslateTransform? slide)
        {
            try
            {
                if (band != null)
                {
                    band.BeginAnimation(OpacityProperty, null);
                    band.Opacity = 0;
                }
                if (slide != null)
                {
                    slide.BeginAnimation(TranslateTransform.XProperty, null);
                    slide.X = 0;
                }
            }
            catch { }
        }

        // ============================== 4. XP bar ==============================

        /// <summary>
        /// Tweens the XP readout instead of snapping it, and fills the bar instead of jumping it.
        /// The level-up flash (XPBarFlashOverlay) is deliberately NOT wired in as MotionFx's cap
        /// bloom - it already exists and is driven by the level-up path; double-driving its
        /// opacity would fight that.
        /// </summary>
        private void AnimateXpDisplay(double xp, double xpNeeded, int level)
        {
            try
            {
                if (TxtXP != null)
                {
                    double from = (!double.IsNaN(_lastXpShown) && level == _lastXpLevelShown) ? _lastXpShown : 0;
                    if (MotionFx.AllowTransitions && Math.Abs(xp - from) >= 0.5)
                    {
                        // The denominator is baked into the format so each tween step still reads
                        // "<n> / <needed> XP" - the odometer only counts the numerator.
                        MotionFx.Odometer(TxtXP, from, xp, "{0:F0} / " + ((int)xpNeeded) + " XP");
                    }
                    else
                    {
                        // Nothing to count: write the text outright. Going through the odometer's
                        // snap path would be a no-op whenever the attached value already equals
                        // the target (a fresh profile at 0 XP), leaving the XAML placeholder up.
                        TxtXP.BeginAnimation(MotionFx.OdometerValueProperty, null);
                        MotionFx.SetOdometerValue(TxtXP, xp);
                        TxtXP.Text = $"{(int)xp} / {(int)xpNeeded} XP";
                    }
                }
                _lastXpShown = xp;
                _lastXpLevelShown = level;

                if (XPBar != null)
                {
                    var container = XPBar.Parent as FrameworkElement;
                    var available = container?.ActualWidth ?? 0;
                    if (available > 0)
                    {
                        var progress = Math.Min(1.0, xpNeeded > 0 ? xp / xpNeeded : 0);
                        double fromWidth = double.IsNaN(XPBar.Width) ? 0 : XPBar.Width;
                        MotionFx.BarFill(XPBar, fromWidth, progress * available);
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("AnimateXpDisplay: {E}", ex.Message); }
        }

        /// <summary>Slow gloss travelling along the XP fill. Three stop offsets on one 6s clock.</summary>
        private void ApplyXpSheen()
        {
            try
            {
                if (XPBarSheen == null || XPSheenLead == null || XPSheenCore == null || XPSheenTail == null)
                    return;

                if (!ChromeAmbientAllowed)
                {
                    foreach (var stop in new[] { XPSheenLead, XPSheenCore, XPSheenTail })
                        stop.BeginAnimation(GradientStop.OffsetProperty, null);
                    XPBarSheen.Opacity = 0;
                    return;
                }

                XPBarSheen.Opacity = 1;
                Sweep(XPSheenLead, -0.30);
                Sweep(XPSheenCore, -0.15);
                Sweep(XPSheenTail, 0.00);

                void Sweep(GradientStop stop, double start)
                {
                    var anim = new DoubleAnimation(start, start + 1.30, TimeSpan.FromSeconds(XpSheenSeconds))
                    {
                        RepeatBehavior = RepeatBehavior.Forever,
                    };
                    Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                    stop.BeginAnimation(GradientStop.OffsetProperty, anim);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyXpSheen: {E}", ex.Message); }
        }

        // ============================== 5. support banner ==============================

        /// <summary>
        /// One sheen pass over the support banner when its message changes. Throttled, because
        /// the rotation is on a 4s timer and a pass every 4s would read as an ambient strobe
        /// rather than "this line is new". <paramref name="force"/> is for a genuinely new
        /// message (a server announcement), which always earns its pass.
        /// </summary>
        internal void SweepBannerSheen(bool force = false)
        {
            try
            {
                if (!_chromeFxInitialized || !_chromeFxWindowActive) return;
                if (!MotionFx.AllowTransitions) return;
                var now = DateTime.UtcNow;
                if (!force && (now - _lastBannerSheenUtc).TotalSeconds < BannerSheenMinGapSeconds) return;
                _lastBannerSheenUtc = now;
                SweepSheen(BannerSheenHost, BannerSheen, BannerSheenSlide, BannerSheenSeconds, 0.22);
            }
            catch (Exception ex) { App.Logger?.Debug("SweepBannerSheen: {E}", ex.Message); }
        }
    }
}
