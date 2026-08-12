using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Behaviors;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-3a of the FX overhaul: the per-tab hero passes for <b>Presets</b>, <b>Quests</b> and
    /// <b>Achievements</b>. Nothing in here touches the shell (PR-1, MainWindow.ChromeFx.cs) or the
    /// dashboard (PR-2, MainWindow.DashboardFx.cs).
    ///
    /// One focal loop per tab, and only where the plan asks for one:
    ///   * Presets      - a slow sheen on the SELECTED card. That is the tab's only clock.
    ///   * Quests       - none added. The season-banner shimmer already exists (and was tier-gated
    ///                    in PR-0); a second loop here would be two heroes on one tab.
    ///   * Achievements - none, deliberately. The grid stays calm; a wall of ~60 tiles is the one
    ///                    surface where ambient motion reads as noise rather than life.
    /// Everything else on these three tabs is event- or hover-driven and costs nothing while idle.
    ///
    /// House rules (FX plan section 2) this file obeys:
    ///   * two clocks - interaction motion 80-400ms, ambient 12s. Nothing in between.
    ///   * the one ambient clock asks <see cref="TabFxAmbientAllowed"/> (window focus + the
    ///     reduced-motion setting + the performance tier) BEFORE it starts, and parks otherwise.
    ///     It is deliberately self-gated rather than riding ApplyChromeFxLoops: this file must not
    ///     edit MainWindow.ChromeFx.cs, and depending on the order two Activated handlers happen to
    ///     run in would be a race.
    ///   * colour only ever from <see cref="FxTheme"/> - the sheen re-tints on ModChanged.
    ///   * <c>element.BeginAnimation</c> only. Storyboard.SetTargetName silently no-ops across the
    ///     tab UserControl name scopes (that is what broke the season shimmer once already).
    ///   * every callback wrapped. Decoration may never be why a tab throws.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Rise for a full-width session row on hover. Rows stretch edge to edge inside a
        /// ScrollViewer, so the shared 1.02 scale would push ~5px past the viewport on each side and
        /// WPF would clip it without a word. A 2px rise reads as the same "lift" and cannot clip -
        /// the rows carry an 8px bottom margin.</summary>
        private const double SessionRowLiftPx = 2.0;
        private const int CardLiftMs = 150;

        /// <summary>Holo-foil tilt on an unlocked achievement tile: the shared 1.02 lift plus a
        /// hair of rotation towards the side the pointer came in on. 0.8 degrees on a 100px tile is
        /// ~0.7px of corner travel, well inside the tile style's 4px margin.</summary>
        private const double AchievementTiltDegrees = 0.8;
        private const int AchievementTiltMs = 160;

        private const double PresetCardCornerRadius = 6;
        private const double SessionCardCornerRadius = 8;

        // ---- state -----------------------------------------------------------------

        private bool _tabFxWindowHooked;
        private bool _presetsFxInitialized;
        private bool _questsFxInitialized;
        private bool _achievementsFxInitialized;

        private CardSheenAdorner? _cardSheen;
        private FrameworkElement? _cardSheenHost;
        /// <summary>Kept alongside the adorner: selecting a preset rebuilds the whole card row, so
        /// by the time we take the sheen down its host may already be out of the tree and
        /// GetAdornerLayer would hand back null.</summary>
        private AdornerLayer? _cardSheenLayer;
        private bool _presetsTabVisible;

        /// <summary>Last known quest fill fractions, so the bars can be re-applied once the track
        /// actually has a width (RefreshQuestUI runs before the tab has ever been laid out).</summary>
        private double _dailyQuestFraction = -1;
        private double _weeklyQuestFraction = -1;
        private bool _questTracksHooked;

        private readonly HashSet<FrameworkElement> _achievementTilesUnlocked = new();
        /// <summary>
        /// Hover source (the card) → the element the tilt actually transforms. On the redesigned
        /// achievement cards those are two different things: the pointer enters the CARD, but the
        /// rotation lands on the badge art, because rotating the card would fight the reward
        /// drawer sliding open underneath the pointer. Doubles as the "already wired" set.
        /// </summary>
        private readonly Dictionary<FrameworkElement, FrameworkElement> _achievementTiltTargets = new();

        /// <summary>
        /// Hover source (the card) → the badge ART itself, which gets the 1.06 "hover pop". A third
        /// element again: the tilt lands on the badge HOST so the foil catches the light, and the
        /// pop lands on the Image inside it, so the two compose instead of overwriting one
        /// another's transform. The card's Chrome border clips, so the zoom stays inside the
        /// rounded frame.
        /// </summary>
        private readonly Dictionary<FrameworkElement, FrameworkElement> _achievementPopTargets = new();

        /// <summary>The single gate for this file's one ambient loop.</summary>
        private bool TabFxAmbientAllowed =>
            IsActive && WindowState != WindowState.Minimized && MotionFx.AllowAmbientLoops;

        /// <summary>
        /// True when the tab that just became visible is the one being navigated TO. PR-1's
        /// transition briefly re-shows the OUTGOING tab so it can fade out (MainWindow.ChromeFx.cs,
        /// FadeOutgoingTab), which raises IsVisibleChanged=true on a tab we are leaving - and
        /// re-running an entrance stagger on something mid-fade-out is a visible flicker.
        /// ShowTab parks the incoming key in _pendingTabKey before it touches any Visibility, so it
        /// is the one field that tells the two apart.
        /// </summary>
        private bool IsIncomingTab(string key) =>
            string.Equals(_pendingTabKey, key, StringComparison.OrdinalIgnoreCase);

        // ============================== shared plumbing ==============================

        /// <summary>
        /// Window focus + mod-switch hooks, wired once from whichever of the three tabs is opened
        /// first. Both only ever re-evaluate the card sheen.
        /// </summary>
        private void HookTabFxWindowEvents()
        {
            if (_tabFxWindowHooked) return;
            _tabFxWindowHooked = true;
            try
            {
                Activated += TabFx_WindowStateish;
                Deactivated += TabFx_WindowStateish;
                StateChanged += TabFx_WindowStateish;
                if (App.Mods != null) App.Mods.ModChanged += TabFx_ModChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("HookTabFxWindowEvents: {E}", ex.Message); }
        }

        private void TabFx_WindowStateish(object? sender, EventArgs e)
        {
            try { RefreshCardSheen(); }
            catch (Exception ex) { App.Logger?.Debug("TabFx_WindowStateish: {E}", ex.Message); }
        }

        /// <summary>A mod switch re-tints the sheen by rebuilding it - the adorner reads FxTheme
        /// once, at construction, so that nothing has to sample a colour per frame.</summary>
        private void TabFx_ModChanged(object? sender, ModPackage mod)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(() => TabFx_ModChanged(sender, mod)));
                    return;
                }
                DetachCardSheen();
                RefreshCardSheen();
            }
            catch (Exception ex) { App.Logger?.Debug("TabFx_ModChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// Gives a card the transforms the shared motion helpers need, without ever clobbering one
        /// it authored itself. A TransformGroup rather than a bare ScaleTransform because
        /// <see cref="MotionFx.StaggerIn"/> wants a TranslateTransform on the SAME element and
        /// whichever helper ran second would otherwise replace the first one's transform outright.
        /// </summary>
        private static TransformGroup? EnsureCardTransforms(FrameworkElement card, bool withRotate = false)
        {
            if (card == null) return null;
            if (card.RenderTransform is TransformGroup existing) return existing;
            var current = card.RenderTransform;
            if (current != null && current != Transform.Identity && !current.Value.IsIdentity) return null;
            if (current is ScaleTransform or RotateTransform or SkewTransform or TranslateTransform) return null;

            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(1, 1));
            if (withRotate) group.Children.Add(new RotateTransform(0));
            group.Children.Add(new TranslateTransform());
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.RenderTransform = group;
            return group;
        }

        private static T? FindTransform<T>(FrameworkElement? card) where T : Transform
        {
            if (card?.RenderTransform is not TransformGroup group) return null;
            foreach (var child in group.Children)
                if (child is T hit) return hit;
            return null;
        }

        /// <summary>Press feedback on a button, wired once. Interaction clock, so it survives
        /// Reduced and only disappears at Off (MotionFx handles that).</summary>
        private void WireButtonPress(ButtonBase? button)
        {
            if (button == null) return;
            try
            {
                button.PreviewMouseLeftButtonDown -= FxButton_PressDown;
                button.PreviewMouseLeftButtonUp -= FxButton_PressUp;
                button.MouseLeave -= FxButton_PressUp;
                button.PreviewMouseLeftButtonDown += FxButton_PressDown;
                button.PreviewMouseLeftButtonUp += FxButton_PressUp;
                button.MouseLeave += FxButton_PressUp;
            }
            catch (Exception ex) { App.Logger?.Debug("WireButtonPress: {E}", ex.Message); }
        }

        private void FxButton_PressDown(object sender, MouseButtonEventArgs e) => PressFx(sender, true);

        private void FxButton_PressUp(object sender, MouseEventArgs e) => PressFx(sender, false);

        private void PressFx(object sender, bool down)
        {
            try
            {
                if (sender is FrameworkElement fe) MotionFx.PressSquish(fe, down);
            }
            catch (Exception ex) { App.Logger?.Debug("PressFx: {E}", ex.Message); }
        }

        // ============================== 1. Presets ==============================

        /// <summary>
        /// Called from PresetsTabView's IsVisibleChanged - the tab's own file, so ShowTab (which
        /// this PR must not edit) keeps working exactly as it did and still drives the FX.
        /// </summary>
        internal void OnPresetsTabVisibilityChanged(bool visible)
        {
            try
            {
                if (!visible)
                {
                    // Leaving the tab: drop the clock outright rather than leave it ticking behind
                    // a collapsed UserControl.
                    _presetsTabVisible = false;
                    DetachCardSheen();
                    return;
                }
                if (!IsIncomingTab("presets")) return;  // the outgoing tab's fade-out re-show
                _presetsTabVisible = true;

                // DATA BEFORE FX. A session drafted while this tab sat collapsed (the Graded
                // Intake ducks the whole window for the run) must be on the list before the
                // stagger animates it in - see SyncCustomSessionsFromDisk / #614.
                SyncCustomSessionsFromDisk();

                InitializePresetsFx();
                StaggerPresetCards();
                RefreshCardSheen();
            }
            catch (Exception ex) { App.Logger?.Debug("OnPresetsTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializePresetsFx()
        {
            if (_presetsFxInitialized) return;
            _presetsFxInitialized = true;
            try
            {
                HookTabFxWindowEvents();

                // The four built-in session rows are static XAML, so they are wired once and never
                // again. The dynamic ones (custom sessions, preset cards) are wired where they are
                // built - see PrepareSessionRowFx / PreparePresetCardFx.
                var panel = PresetsTab?.SessionsPanel;
                if (panel != null)
                    foreach (var row in panel.Children.OfType<Border>())
                        PrepareSessionRowFx(row);

                WireButtonPress(PresetsTab?.BtnLoadPreset);
                WireButtonPress(PresetsTab?.BtnStartSession);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializePresetsFx failed"); }
        }

        /// <summary>
        /// Hover lift on a preset card (100x70, so the shared 1.02 scale fits). Called from
        /// CreatePresetCard, which rebuilds the whole row on every selection change.
        /// </summary>
        internal void PreparePresetCardFx(FrameworkElement card)
        {
            if (card == null) return;
            try
            {
                EnsureCardTransforms(card);
                card.MouseEnter += PresetCard_MouseEnter;
                card.MouseLeave += PresetCard_MouseLeave;
            }
            catch (Exception ex) { App.Logger?.Debug("PreparePresetCardFx: {E}", ex.Message); }
        }

        private void PresetCard_MouseEnter(object sender, MouseEventArgs e)
        {
            try { MotionFx.HoverLift(sender as FrameworkElement, true); }
            catch (Exception ex) { App.Logger?.Debug("PresetCard_MouseEnter: {E}", ex.Message); }
        }

        private void PresetCard_MouseLeave(object sender, MouseEventArgs e)
        {
            try { MotionFx.HoverLift(sender as FrameworkElement, false); }
            catch (Exception ex) { App.Logger?.Debug("PresetCard_MouseLeave: {E}", ex.Message); }
        }

        /// <summary>Hover lift on a full-width session row - a rise, not a scale. See
        /// <see cref="SessionRowLiftPx"/> for why.</summary>
        internal void PrepareSessionRowFx(FrameworkElement row)
        {
            if (row == null) return;
            try
            {
                EnsureCardTransforms(row);
                row.MouseEnter += SessionRow_MouseEnter;
                row.MouseLeave += SessionRow_MouseLeave;
            }
            catch (Exception ex) { App.Logger?.Debug("PrepareSessionRowFx: {E}", ex.Message); }
        }

        private void SessionRow_MouseEnter(object sender, MouseEventArgs e) => LiftSessionRow(sender, true);

        private void SessionRow_MouseLeave(object sender, MouseEventArgs e) => LiftSessionRow(sender, false);

        private void LiftSessionRow(object sender, bool on)
        {
            try
            {
                var slide = FindTransform<TranslateTransform>(sender as FrameworkElement);
                if (slide == null || slide.IsFrozen) return;
                double to = on ? -SessionRowLiftPx : 0;
                if (!MotionFx.AllowTransitions)
                {
                    slide.BeginAnimation(TranslateTransform.YProperty, null);
                    slide.Y = to;
                    return;
                }
                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(CardLiftMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                slide.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("LiftSessionRow: {E}", ex.Message); }
        }

        /// <summary>
        /// Entrance stagger on the cards themselves. PR-1's tab choreography already staggers the
        /// tab's three top-level containers (the left column, the splitter, the details panel) and
        /// nothing inside them, so this adds the missing layer without animating anything twice.
        /// Only on a tab show - re-running it every time a selection rebuilds the preset row would
        /// make clicking a card re-animate the whole strip.
        /// </summary>
        private void StaggerPresetCards()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                var cards = new List<FrameworkElement>();
                if (PresetsTab?.PresetCardsPanel != null)
                    cards.AddRange(PresetsTab.PresetCardsPanel.Children.OfType<FrameworkElement>());
                if (PresetsTab?.SessionsPanel != null)
                    cards.AddRange(PresetsTab.SessionsPanel.Children.OfType<Border>()
                        .Where(b => b.Visibility == Visibility.Visible));
                if (cards.Count == 0) return;
                foreach (var card in cards) EnsureCardTransforms(card);
                MotionFx.StaggerIn(cards);
            }
            catch (Exception ex) { App.Logger?.Debug("StaggerPresetCards: {E}", ex.Message); }
        }

        /// <summary>
        /// Moves the sheen onto whatever is selected now (and takes it off everything else). Called
        /// on selection, on tab show, on window activation and after a mod switch.
        /// </summary>
        internal void RefreshCardSheen()
        {
            try
            {
                if (!_presetsTabVisible || !TabFxAmbientAllowed)
                {
                    DetachCardSheen();
                    return;
                }

                var target = FindSelectedCard();
                if (target == null || !target.IsVisible)
                {
                    DetachCardSheen();
                    return;
                }
                if (ReferenceEquals(target, _cardSheenHost) && _cardSheen != null) return;

                DetachCardSheen();
                var layer = AdornerLayer.GetAdornerLayer(target);
                if (layer == null)
                {
                    // No adorner layer yet (the card has not been rendered). Nothing to do - the
                    // next selection or tab show tries again.
                    App.Logger?.Debug("RefreshCardSheen: no adorner layer for the selected card");
                    return;
                }

                double radius = ReferenceEquals(PresetsTab?.PresetCardsPanel, target.Parent)
                    ? PresetCardCornerRadius
                    : SessionCardCornerRadius;
                var sheen = new CardSheenAdorner(target, radius);
                layer.Add(sheen);
                sheen.Start();
                _cardSheen = sheen;
                _cardSheenHost = target;
                _cardSheenLayer = layer;
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshCardSheen: {E}", ex.Message); }
        }

        private void DetachCardSheen()
        {
            try
            {
                var sheen = _cardSheen;
                var layer = _cardSheenLayer;
                _cardSheen = null;
                _cardSheenHost = null;
                _cardSheenLayer = null;
                if (sheen == null) return;
                sheen.Stop();
                layer?.Remove(sheen);
            }
            catch (Exception ex) { App.Logger?.Debug("DetachCardSheen: {E}", ex.Message); }
        }

        /// <summary>The card the details panel is currently describing, matched by Tag - the same
        /// key SessionCard_Click and CreatePresetCard already store there.</summary>
        private FrameworkElement? FindSelectedCard()
        {
            var tab = PresetsTab;
            if (tab == null) return null;

            if (_selectedPreset != null)
                return ByTag(tab.PresetCardsPanel, _selectedPreset.Id);

            if (_selectedSession != null)
                return ByTag(tab.SessionsPanel, _selectedSession.Id)
                    ?? ByTag(tab.CustomSessionsPanel, _selectedSession.Id);

            return null;

            static FrameworkElement? ByTag(Panel? panel, string? id)
            {
                if (panel == null || string.IsNullOrEmpty(id)) return null;
                return panel.Children.OfType<FrameworkElement>()
                            .FirstOrDefault(c => c.Tag as string == id);
            }
        }

        // ============================== 2. Quests ==============================

        internal void OnQuestsTabVisibilityChanged(bool visible)
        {
            try
            {
                if (!visible || !IsIncomingTab("quests")) return;
                InitializeQuestsFx();
                // First visit: the tracks have never been measured, so RefreshQuestUI (which runs
                // moments after this) will store the fractions and land nothing. The tracks'
                // SizeChanged hook seats the fills the instant layout gives them a width. This
                // call is for every later visit, where the width is already known.
                ApplyQuestProgressBars(animate: true);
            }
            catch (Exception ex) { App.Logger?.Debug("OnQuestsTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializeQuestsFx()
        {
            if (_questsFxInitialized) return;
            _questsFxInitialized = true;
            try
            {
                HookTabFxWindowEvents();
                // Both reroll buttons wear the OutlineButton style, whose template ALREADY carries
                // the plan's press squish (scale 0.97 over 80ms, on its inner border). Adding
                // MotionFx.PressSquish on top would compound the two transforms into a ~6% squish,
                // so the reroll buttons are deliberately left alone. Retuning the shared style is
                // not this PR's to make - Resources/Theme is off-limits here.

                if (!_questTracksHooked)
                {
                    _questTracksHooked = true;
                    if (QuestsTab?.DailyProgressTrack != null)
                        QuestsTab.DailyProgressTrack.SizeChanged += QuestTrack_SizeChanged;
                    if (QuestsTab?.WeeklyProgressTrack != null)
                        QuestsTab.WeeklyProgressTrack.SizeChanged += QuestTrack_SizeChanged;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeQuestsFx failed"); }
        }

        /// <summary>A resize is not progress - re-seat the fills without a tween.</summary>
        private void QuestTrack_SizeChanged(object sender, SizeChangedEventArgs e)
            => ApplyQuestProgressBars(animate: false);

        /// <summary>
        /// Records how full each quest bar should be. Called from RefreshQuestUI instead of it
        /// assigning Width directly, so the fill can be tweened (and re-applied once the track has
        /// a width). No cap bloom: neither bar has a cap/end-glow element, and the plan is explicit
        /// that we do not invent one just to have something to bloom.
        /// </summary>
        private void SetQuestProgress(double? dailyFraction, double? weeklyFraction)
        {
            try
            {
                if (dailyFraction.HasValue) _dailyQuestFraction = Clamp01(dailyFraction.Value);
                if (weeklyFraction.HasValue) _weeklyQuestFraction = Clamp01(weeklyFraction.Value);
                ApplyQuestProgressBars(animate: true);
            }
            catch (Exception ex) { App.Logger?.Debug("SetQuestProgress: {E}", ex.Message); }

            static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v));
        }

        private void ApplyQuestProgressBars(bool animate)
        {
            try
            {
                Apply(QuestsTab?.DailyProgressTrack, QuestsTab?.DailyProgressFill, _dailyQuestFraction);
                Apply(QuestsTab?.WeeklyProgressTrack, QuestsTab?.WeeklyProgressFill, _weeklyQuestFraction);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyQuestProgressBars: {E}", ex.Message); }

            void Apply(FrameworkElement? track, FrameworkElement? fill, double fraction)
            {
                if (track == null || fill == null || fraction < 0) return;
                double available = track.ActualWidth;
                if (available <= 0) return;
                double to = available * fraction;
                double from = double.IsNaN(fill.Width) ? 0 : fill.Width;
                if (!animate)
                {
                    fill.BeginAnimation(FrameworkElement.WidthProperty, null);
                    fill.Width = to;
                    return;
                }
                if (Math.Abs(to - from) < 0.5) return;
                MotionFx.BarFill(fill, from, to);
            }
        }

        // ============================== 3. Achievements ==============================

        internal void OnAchievementsTabVisibilityChanged(bool visible)
        {
            try
            {
                if (!visible || !IsIncomingTab("achievements")) return;
                if (!_achievementsFxInitialized)
                {
                    _achievementsFxInitialized = true;
                    HookTabFxWindowEvents();
                }
                StaggerAchievementTiles();
            }
            catch (Exception ex) { App.Logger?.Debug("OnAchievementsTabVisibilityChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// Entrance stagger on the tiles. MotionFx caps the stagger at 6 slots, so a 60-tile wall
        /// still lands in ~500ms instead of crawling in row by row. Only the free grid: the patron
        /// grid is below the fold on arrival and is often behind its lock scrim.
        /// </summary>
        private void StaggerAchievementTiles()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                var grid = AchievementsTab?.AchievementGrid;
                if (grid == null || grid.Children.Count == 0) return;
                var tiles = grid.Children.OfType<FrameworkElement>()
                                .Where(t => t.Visibility == Visibility.Visible).ToList();
                if (tiles.Count == 0) return;
                foreach (var tile in tiles) EnsureCardTransforms(tile, withRotate: true);
                MotionFx.StaggerIn(tiles);
            }
            catch (Exception ex) { App.Logger?.Debug("StaggerAchievementTiles: {E}", ex.Message); }
        }

        /// <summary>
        /// Wires a tile's hover treatment once, at grid-build time, and records whether it is
        /// unlocked. Locked tiles deliberately get nothing - a blurred "???" that tilts invitingly
        /// is the UI flirting about something you cannot have.
        /// </summary>
        /// <param name="tile">The element the pointer enters (the card).</param>
        /// <param name="tiltTarget">
        /// What the lift/tilt transforms, when that is not the tile itself. The reward cards pass
        /// their badge-art host: the card must stay still so the drawer can slide open under a
        /// stationary pointer.
        /// </param>
        /// <param name="popTarget">
        /// The badge art that gets the hover pop, when there is one. Kept separate from
        /// <paramref name="tiltTarget"/> so the pop and the tilt never share a transform.
        /// </param>
        internal void PrepareAchievementTileFx(FrameworkElement tile, bool unlocked,
                                               FrameworkElement? tiltTarget = null,
                                               FrameworkElement? popTarget = null)
        {
            if (tile == null) return;
            try
            {
                SetAchievementTileUnlocked(tile, unlocked);
                if (_achievementTiltTargets.ContainsKey(tile)) return;
                var target = tiltTarget ?? tile;
                EnsureCardTransforms(target, withRotate: true);
                _achievementTiltTargets[tile] = target;
                if (popTarget != null) _achievementPopTargets[tile] = popTarget;
                tile.MouseEnter += AchievementTile_MouseEnter;
                tile.MouseLeave += AchievementTile_MouseLeave;
            }
            catch (Exception ex) { App.Logger?.Debug("PrepareAchievementTileFx: {E}", ex.Message); }
        }

        /// <summary>Called when a tile's unlock state changes so the tilt starts (or stops) being
        /// offered without rebuilding the grid.</summary>
        internal void SetAchievementTileUnlocked(FrameworkElement tile, bool unlocked)
        {
            if (tile == null) return;
            try
            {
                if (unlocked) _achievementTilesUnlocked.Add(tile);
                else if (_achievementTilesUnlocked.Remove(tile))
                    TiltAchievementTile(TiltTargetFor(tile), 0, false);
            }
            catch (Exception ex) { App.Logger?.Debug("SetAchievementTileUnlocked: {E}", ex.Message); }
        }

        private FrameworkElement? TiltTargetFor(FrameworkElement tile)
            => _achievementTiltTargets.TryGetValue(tile, out var target) ? target : tile;

        private void AchievementTile_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement tile || !_achievementTilesUnlocked.Contains(tile)) return;
                var target = TiltTargetFor(tile);
                // Tilt towards the side the pointer entered on - the foil catches the light from
                // where you came from, which is what sells it as a surface rather than a sticker.
                double sign = 1;
                var p = e.GetPosition(tile);
                if (tile.ActualWidth > 0) sign = p.X < tile.ActualWidth / 2 ? -1 : 1;
                MotionFx.HoverLift(target, true);
                TiltAchievementTile(target, sign * AchievementTiltDegrees, true);
                if (_achievementPopTargets.TryGetValue(tile, out var art)) HoverPop.Enter(art);
            }
            catch (Exception ex) { App.Logger?.Debug("AchievementTile_MouseEnter: {E}", ex.Message); }
        }

        private void AchievementTile_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement tile) return;
                var target = TiltTargetFor(tile);
                MotionFx.HoverLift(target, false);
                TiltAchievementTile(target, 0, true);
                if (_achievementPopTargets.TryGetValue(tile, out var art)) HoverPop.Leave(art);
            }
            catch (Exception ex) { App.Logger?.Debug("AchievementTile_MouseLeave: {E}", ex.Message); }
        }

        private void TiltAchievementTile(FrameworkElement? tile, double degrees, bool animate)
        {
            try
            {
                var rotate = FindTransform<RotateTransform>(tile);
                if (rotate == null || rotate.IsFrozen) return;
                if (!animate || !MotionFx.AllowTransitions)
                {
                    rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    rotate.Angle = degrees;
                    return;
                }
                var anim = new DoubleAnimation(degrees, TimeSpan.FromMilliseconds(AchievementTiltMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("TiltAchievementTile: {E}", ex.Message); }
        }
    }
}
