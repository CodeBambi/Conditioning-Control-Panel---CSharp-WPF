using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE QUEST STAMPS: the four tilted squares that sit at the far left of the header XP row and
    /// say, without a click, how today went - three daily slots and the week's one.
    ///
    /// <para><b>Why stamps.</b> The Quests tab already tells the whole story; the header only needs
    /// the shape of it. A slot stamped gold with a check is "done", a plate wearing the quest's own
    /// art with a sliver of fill along its bottom is "you are mid-quest", a dashed ghost is "not
    /// rolled yet". The tilt is the point: a card that is perfectly square reads as a button, and a
    /// card knocked a few degrees off reads as something that was stamped onto the panel.</para>
    ///
    /// <para><b>The art is the identity.</b> Each live stamp is filled with that quest's own
    /// picture - the same mode-aware art the Quests tab shows, decoded once and shared through
    /// MainWindow.GetQuestArt. At 20px a picture is not readable as a picture, and it is not meant
    /// to be: it is meant to be RECOGNISABLE, so that the plate in the header and the card on the
    /// tab are visibly the same quest. The icon glyph is the fallback for a quest with no art.</para>
    ///
    /// <para><b>Hover zooms.</b> Each stamp is its own hit-test target now (the host still owns the
    /// click). Hovering one lifts and straightens it and opens a detail card under the cluster with
    /// the full art, the ask, the bar and - the whole question a header stamp is asked - how much
    /// is still missing. See <see cref="ShowStampPopup"/>.</para>
    ///
    /// <para><b>Layout contract - this must not cost the row a single pixel.</b> The host
    /// (<c>QuestStampHost</c>, MainWindow.xaml) is a new <i>Auto</i> column at index 0 of the XP
    /// bar's left grid, ahead of the LVL chip. The bar's own column is star-width, so the cluster
    /// measuring ~71px simply shortens the bar by 71px and nothing else in the row moves - and
    /// collapsing the host (no QuestService) hands all of it straight back. Vertically the host's
    /// Height is PINNED at 22 in XAML: the rotated squares' bounding boxes run a couple of px past
    /// that, and a pinned height means the overhang draws (ClipToBounds is false) without ever
    /// entering the row's measure. A stat pill is ~23px tall, so the row's height is still decided
    /// by the pills exactly as it was before this file existed. The hover zoom is a RenderTransform,
    /// which by definition happens after measure - a zoomed stamp cannot move the row either.</para>
    ///
    /// <para><b>Built in code, rebuilt whole.</b> Four elements is cheaper to re-create than to
    /// diff, and every repaint starts from live quest state rather than from the accumulation of
    /// whatever transitions happened - the same discipline as
    /// <see cref="RefreshSpiralRailEntry"/>. Nothing here is templated, so no converter or resource
    /// needs to exist in Window.Resources for it. A repaint that lands mid-hover re-applies the
    /// hover to the newly built stamp rather than dropping the player out of it.</para>
    ///
    /// <para><b>Rails.</b> All four subscriptions (three quest events + language) arrive off the UI
    /// thread or during teardown, so every handler marshals to the Dispatcher, bails on
    /// <c>HasShutdownStarted</c> (CLAUDE.md rule 8) and is wrapped. Wiring is once-only and is
    /// released on <c>Closed</c>.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- DIALS ----------------------------------------------------------------

        /// <summary>Edge of a daily stamp, in px. Kept under the host's pinned 22 so the rotated
        /// bounding box only just overhangs.</summary>
        private const double QuestStampDailySize = 20;

        /// <summary>The weekly stamp is deliberately a touch bigger than a daily one - the other
        /// half of "this one is different" (the first half is the purple).</summary>
        private const double QuestStampWeeklySize = 23;

        /// <summary>Left edge of each stamp inside the host. The 16px step against a 20px stamp is
        /// what produces the 4px overlap; the squares are meant to look dropped, not gridded.</summary>
        private static readonly double[] QuestStampOffsets = { 0, 16, 32, 48 };

        /// <summary>Tilt per stamp, degrees. Alternating signs and no two equal - a repeated angle
        /// immediately reads as a pattern instead of as four separate stampings.</summary>
        private static readonly double[] QuestStampAngles = { -4, 3, -2, 5 };

        /// <summary>Inset of the progress sliver from the stamp's edge. A Border/Rectangle does not
        /// clip its neighbours to a CornerRadius, so the fill has to be pulled in far enough that a
        /// square corner cannot poke out past the rounded outline.</summary>
        private const double QuestStampFillInset = 2.5;

        private const double QuestStampFillHeight = 2.5;

        /// <summary>How far a hovered stamp swells. Held to ~1.5 on purpose: the detail card is
        /// what the player actually reads, and a stamp big enough to read on its own would be big
        /// enough to cover the LVL chip sitting immediately to its right.</summary>
        private const double QuestStampHoverScale = 1.5;

        /// <summary>Width of the hover card, and of the bar inside it. Fixed rather than measured
        /// so the fill can be seated the instant the card is painted - a popup has no layout pass
        /// to wait for and no SizeChanged worth hooking.</summary>
        private const double QuestStampPopWidth = 300;
        private const double QuestStampPopTrackWidth = QuestStampPopWidth - 28;

        // ---- PALETTE (frozen; these repaint on every quest tick) -------------------

        private static readonly Brush QuestStampGoldStroke = Frozen(Color.FromArgb(0xCC, 0xFF, 0xD7, 0x00));
        private static readonly Brush QuestStampGoldFill = Frozen(Color.FromArgb(0x30, 0xFF, 0xD7, 0x00));
        private static readonly Brush QuestStampGoldInk = Frozen(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly Brush QuestStampGoldWash = Frozen(Color.FromArgb(0x88, 0x8A, 0x6D, 0x00));

        private static readonly Brush QuestStampPinkStroke = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
        private static readonly Brush QuestStampPanelFill = Frozen(Color.FromRgb(0x25, 0x25, 0x42));
        private static readonly Brush QuestStampDarkFill = Frozen(Color.FromRgb(0x1A, 0x1A, 0x2E));

        private static readonly Brush QuestStampPurpleStroke = Frozen(Color.FromRgb(0xB5, 0x7E, 0xDC));
        private static readonly Brush QuestStampPurpleFill = Frozen(Color.FromRgb(0x2A, 0x1F, 0x3D));

        private static readonly Brush QuestStampGhostStroke = Frozen(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly Brush QuestStampGhostFill = Frozen(Color.FromArgb(0x22, 0x1A, 0x1A, 0x2E));

        // Light on purpose. The quest art is mostly dark neon on black already, and a scrim heavy
        // enough to guarantee contrast for the outline turned four different pictures into four
        // identical dark squares - which defeats the whole reason for putting art on them.
        private static readonly Brush QuestStampArtScrim = Frozen(Color.FromArgb(0x2E, 0x0A, 0x0A, 0x18));
        private static readonly Brush QuestStampDoneInk = Frozen(Color.FromRgb(0x00, 0xE6, 0x76));
        private static readonly Brush QuestStampMutedInk = Frozen(Color.FromRgb(0xA0, 0xA0, 0xC0));
        private static readonly Brush QuestStampWhiteInk = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush QuestStampChipFill = Frozen(Color.FromArgb(0xB0, 0x1A, 0x1A, 0x2E));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private bool _questStampsWired;

        // ---- STATE ----------------------------------------------------------------

        /// <summary>
        /// One stamp's worth of live quest state. Built by the repaint and kept until the next one,
        /// because the hover card is painted from the same object the plate was - there is no
        /// second lookup that could disagree with what is on screen.
        /// </summary>
        private sealed class QuestStampInfo
        {
            public string Key = "";              // "d0".."d2", "w"
            public string Label = "";            // chip text on the hover card
            public bool IsWeekly;
            public QuestDefinition? Def;
            public ActiveQuest? Quest;
            public bool Completed;
            public double Fraction;
            public FrameworkElement? Cell;       // the plate itself, for the zoom
        }

        private readonly Dictionary<string, QuestStampInfo> _stampInfos = new(StringComparer.Ordinal);

        /// <summary>Completion state as of the last repaint, so a slot flipping to done can be
        /// popped exactly once instead of on every tick that follows it.</summary>
        private readonly Dictionary<string, bool> _stampCompletedSeen = new(StringComparer.Ordinal);

        private string? _hoveredStampKey;

        // The hover card. Created on first hover and reused - a Popup is a window, and four of
        // them (or one per repaint) is not something to hand the compositor for a header trinket.
        private Popup? _stampPopup;
        private Border? _stampPopCard;
        private TranslateTransform? _stampPopSlide;
        private Image? _stampPopArt;
        private Border? _stampPopArtScrim;
        private TextBlock? _stampPopKind;
        private TextBlock? _stampPopXp;
        private TextBlock? _stampPopIcon;
        private TextBlock? _stampPopName;
        private TextBlock? _stampPopDesc;
        private Border? _stampPopTrack;
        private Border? _stampPopFill;
        private TextBlock? _stampPopProgress;
        private TextBlock? _stampPopRemaining;
        private Border? _stampPopDone;

        // ---- WIRING ---------------------------------------------------------------

        /// <summary>
        /// Called once from the window's construction path (MainWindow.xaml.cs), next to the other
        /// header surfaces. Safe to call before quests have generated: the first repaint just draws
        /// three ghosts and hides itself if there is no service at all.
        /// </summary>
        private void InitializeQuestStamps()
        {
            if (_questStampsWired) return;
            _questStampsWired = true;

            try
            {
                var quests = App.Quests;
                if (quests != null)
                {
                    quests.QuestCompleted += OnQuestStampsQuestCompleted;
                    quests.QuestProgressChanged += OnQuestStampsProgressChanged;
                    quests.QuestsRefreshed += OnQuestStampsRefreshed;
                }
                LocalizationManager.Instance.LanguageChanged += OnQuestStampsLanguageChanged;

                // A popup is a window: it does not travel with its placement target when the main
                // window moves, and it has no business outliving a deactivation. Both just close it.
                Deactivated += OnQuestStampsDismiss;
                LocationChanged += OnQuestStampsDismiss;

                // A mod switch can resolve the same quest to a different file, so the decode cache
                // shared by the stamps and the Quests tab has to be dropped before either repaints
                // from it. This is the only listener for it: the stamps are the one quest surface
                // that is on screen no matter which tab is.
                if (App.Mods != null) App.Mods.ModChanged += OnQuestStampsModChanged;

                // Same release pattern as MainWindow.JustDrop.cs: the events outlive the window.
                Closed += (_, _) =>
                {
                    try
                    {
                        var q = App.Quests;
                        if (q != null)
                        {
                            q.QuestCompleted -= OnQuestStampsQuestCompleted;
                            q.QuestProgressChanged -= OnQuestStampsProgressChanged;
                            q.QuestsRefreshed -= OnQuestStampsRefreshed;
                        }
                        LocalizationManager.Instance.LanguageChanged -= OnQuestStampsLanguageChanged;
                        Deactivated -= OnQuestStampsDismiss;
                        LocationChanged -= OnQuestStampsDismiss;
                        if (App.Mods != null) App.Mods.ModChanged -= OnQuestStampsModChanged;
                        if (_stampPopup != null) _stampPopup.IsOpen = false;
                    }
                    catch { /* teardown */ }
                };

                RefreshQuestStamps();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[QuestStamps] cluster could not be wired: {E}", ex.Message);
            }
        }

        private void OnQuestStampsQuestCompleted(object? sender, QuestCompletedEventArgs e) => QueueQuestStampRepaint();
        private void OnQuestStampsProgressChanged(object? sender, QuestProgressEventArgs e) => QueueQuestStampRepaint();
        private void OnQuestStampsRefreshed(object? sender, EventArgs e) => QueueQuestStampRepaint();
        private void OnQuestStampsLanguageChanged(object? sender, EventArgs e) => QueueQuestStampRepaint();
        private void OnQuestStampsDismiss(object? sender, EventArgs e) => HideStampPopup();

        /// <summary>
        /// Mod switched: throw away the decoded quest art and repaint everything that was showing
        /// it. The Quests tab is only repainted when it is actually up - ShowTab refreshes it on
        /// entry anyway, and the cache is already empty by then.
        /// </summary>
        private void OnQuestStampsModChanged(object? sender, ModPackage mod)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                if (!dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => OnQuestStampsModChanged(sender, mod)));
                    return;
                }

                ClearQuestArtCache();
                HideStampPopup();
                RefreshQuestStamps();
                if (QuestsTab?.IsVisible == true) RefreshQuestUI();
            }
            catch (Exception ex) { App.Logger?.Debug("[QuestStamps] mod switch: {E}", ex.Message); }
        }

        /// <summary>
        /// Marshal + guard. Every caller is a service event that can land on a worker thread, and
        /// quest progress in particular can tick several times a second during a session - the
        /// repaint is four elements, so it is cheaper to just do it than to throttle it.
        /// </summary>
        private void QueueQuestStampRepaint()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(RefreshQuestStamps));
            }
            catch { /* fire-and-forget */ }
        }

        // ---- PAINT ----------------------------------------------------------------

        /// <summary>
        /// Rebuild the four stamps from live quest state. Computed from scratch every time, so any
        /// arrival order - a midnight roll, a reroll, a 3/3 finish, a logout wiping the service -
        /// lands on a correct cluster.
        /// </summary>
        internal void RefreshQuestStamps()
        {
            var host = QuestStampHost;
            if (host == null) return;

            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

                var quests = App.Quests;
                if (quests == null)
                {
                    // No service: collapse so the star-width bar reclaims the whole row.
                    HideStampPopup();
                    host.Children.Clear();
                    _stampInfos.Clear();
                    host.ToolTip = null;
                    host.Visibility = Visibility.Collapsed;
                    return;
                }

                host.Visibility = Visibility.Visible;
                host.Children.Clear();
                _stampInfos.Clear();

                // The three daily seats come straight off the board. Under the old one-at-a-time
                // daily this had to be inferred from a completed COUNT - slots below it were drawn
                // done and slots above it drawn as ghosts, because only one of them existed. All
                // three exist now, so each plate is simply its own quest.
                var board = quests.GetDailySlots();
                for (int slot = 0; slot < QuestService.MaxDailyQuestsPerDay; slot++)
                {
                    var quest = slot < board.Count ? board[slot].Quest : null;
                    var def = slot < board.Count ? board[slot].Definition : null;
                    var info = new QuestStampInfo
                    {
                        Key = "d" + slot,
                        Label = $"{Loc.Get("quest_daily")} {slot + 1}",
                        IsWeekly = false,
                        Def = def,
                        Quest = quest,
                        Completed = quest?.IsCompleted == true,
                        Fraction = quest != null && def != null
                            ? QuestStampFraction(quest.CurrentProgress, def.TargetValue) : 0,
                    };
                    AddStamp(host, info, QuestStampDailySize, QuestStampOffsets[slot], QuestStampAngles[slot]);
                }

                // The weekly: bigger, purple, and it keeps its own fill even while the dailies
                // churn underneath it.
                var weeklyDef = quests.GetCurrentWeeklyDefinition();
                var weeklyActive = quests.Progress?.WeeklyQuest;
                var weeklyInfo = new QuestStampInfo
                {
                    Key = "w",
                    Label = Loc.Get("quest_weekly"),
                    IsWeekly = true,
                    Def = weeklyDef,
                    Quest = weeklyActive,
                    Completed = weeklyActive?.IsCompleted == true,
                    Fraction = weeklyActive != null && weeklyDef != null
                        ? QuestStampFraction(weeklyActive.CurrentProgress, weeklyDef.TargetValue) : 0,
                };
                AddStamp(host, weeklyInfo, QuestStampWeeklySize, QuestStampOffsets[3], QuestStampAngles[3]);

                // The aggregated string tooltip this host used to carry is gone: the hover card
                // says the same things per stamp, with the art and the bar, and two tips fighting
                // over the same pointer is worse than either.
                host.ToolTip = null;

                RestoreStampHover();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[QuestStamps] repaint failed: {E}", ex.Message);
            }
        }

        /// <summary>Build one plate, record it, and pop it if this is the tick it got stamped.</summary>
        private void AddStamp(Panel host, QuestStampInfo info, double size, double left, double angle)
        {
            var cell = BuildQuestStamp(info, size, left, angle);
            info.Cell = cell;
            _stampInfos[info.Key] = info;
            host.Children.Add(cell);

            // Only a TRANSITION pops. The first paint of a session finds slots that were finished
            // yesterday-evening already stamped, and popping those would be a celebration for
            // something the player did before the app was open.
            bool known = _stampCompletedSeen.TryGetValue(info.Key, out var wasDone);
            _stampCompletedSeen[info.Key] = info.Completed;
            if (info.Completed && known && !wasDone) PlayStampStampedPop(cell);
        }

        private static double QuestStampFraction(int current, int target)
        {
            if (target <= 0) return 0;
            return Math.Max(0, Math.Min(1.0, (double)current / target));
        }

        /// <summary>
        /// One stamp. A Grid rather than a Border because the ghost state needs a dashed outline,
        /// and only a Shape carries StrokeDashArray - so the outline is a rounded Rectangle and the
        /// art, the glyph and the progress sliver are siblings stacked on top of it.
        ///
        /// <para>The plate's fill is the quest's own art when there is any: an ImageBrush on the
        /// rounded Rectangle, so the picture is clipped to the plate's corners for free. The art is
        /// decoded once and shared (MainWindow.GetQuestArt), which matters because this method runs
        /// four times per quest tick.</para>
        /// </summary>
        private FrameworkElement BuildQuestStamp(QuestStampInfo info, double size, double left, double angle)
        {
            bool ghost = info.Def == null || info.Quest == null;
            bool done = info.Completed;

            Brush stroke = ghost ? QuestStampGhostStroke
                         : done ? QuestStampGoldStroke
                         : info.IsWeekly ? QuestStampPurpleStroke
                         : QuestStampPinkStroke;

            Brush plainFill = ghost ? QuestStampGhostFill
                            : done ? QuestStampGoldFill
                            : info.IsWeekly ? QuestStampPurpleFill
                            : QuestStampPanelFill;

            var art = ghost ? null : GetQuestArt(info.Def!);
            double opacity = ghost ? 0.5 : done ? 0.85 : 1.0;

            // The zoom needs its own scale in front of the tilt, and the tilt needs to be able to
            // relax toward 0 while hovered, so both transforms are named on the group.
            var scale = new ScaleTransform(1, 1);
            var rotate = new RotateTransform(angle);
            var cell = new Grid
            {
                Width = size,
                Height = size,
                Opacity = opacity,
                Margin = new Thickness(left, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { scale, rotate } },
                Background = Brushes.Transparent,   // the plate's own gaps still hover
                Tag = info.Key,
            };

            var plate = new Rectangle
            {
                RadiusX = 3,
                RadiusY = 3,
                Fill = plainFill,
                Stroke = stroke,
                StrokeThickness = 1.5,
            };
            if (ghost)
            {
                plate.StrokeDashArray = new DoubleCollection { 2, 1.6 };
                plate.StrokeThickness = 1.0;
            }
            else if (art != null)
            {
                var brush = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                plate.Fill = brush;
            }
            cell.Children.Add(plate);

            if (art != null)
            {
                // Scrim. The art is arbitrary and the outline, the sliver and the check all have to
                // stay readable over a bright one.
                cell.Children.Add(new Rectangle
                {
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = done ? QuestStampGoldWash : QuestStampArtScrim,
                    IsHitTestVisible = false,
                });
            }

            // Glyph. The check always shows on a finished plate; the quest icon only stands in for
            // art that is missing, because an icon printed over a picture at 20px is mud.
            string? glyph = done ? "✓" : art == null && !ghost ? info.Def!.Icon : null;
            if (!string.IsNullOrWhiteSpace(glyph))
            {
                cell.Children.Add(new TextBlock
                {
                    Text = glyph,
                    FontSize = size >= QuestStampWeeklySize ? 12 : 10.5,
                    FontWeight = done ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = done ? QuestStampGoldInk : stroke,
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false,
                });
            }

            double fraction = done ? 1.0 : info.Fraction;
            if (fraction > 0 && !ghost)
            {
                double track = size - (QuestStampFillInset * 2);
                cell.Children.Add(new Border
                {
                    Height = QuestStampFillHeight,
                    Width = Math.Max(1.0, track * fraction),
                    CornerRadius = new CornerRadius(1),
                    Background = done ? QuestStampGoldStroke : stroke,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(QuestStampFillInset, 0, 0, QuestStampFillInset),
                    IsHitTestVisible = false,
                });
            }

            // Each plate is its own target now. The host keeps the click (MouseLeftButtonUp bubbles
            // straight through this Grid), so navigation still works from the gaps between stamps.
            cell.MouseEnter += Stamp_MouseEnter;
            cell.MouseLeave += Stamp_MouseLeave;
            return cell;
        }

        /// <summary>
        /// Quest names come from the definition's own loc key, which only exists for the ~20 quests
        /// that ship in the language files - a server-rolled quest would otherwise render its raw
        /// key. Falls back to the definition's English name, mod-aware, exactly as the Quests tab
        /// does.
        /// </summary>
        private static string QuestStampName(QuestDefinition def)
        {
            var name = def.LocalizedName;
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("quest_", StringComparison.Ordinal))
                name = def.Name;
            return App.Mods?.MakeModAware(name) ?? name;
        }

        private static string QuestStampDescription(QuestDefinition def)
        {
            var desc = def.LocalizedDescription;
            if (string.IsNullOrWhiteSpace(desc) || desc.StartsWith("quest_", StringComparison.Ordinal))
                desc = def.Description;
            return App.Mods?.MakeModAware(desc) ?? desc;
        }

        // ---- FX -------------------------------------------------------------------

        /// <summary>
        /// The moment a slot flips to done: an overshoot pop plus a gold flash that fades and then
        /// detaches itself. Attached rather than resident because a DropShadowEffect is a
        /// pixel-shader pass for as long as it exists, and these four plates repaint constantly.
        /// </summary>
        private static void PlayStampStampedPop(FrameworkElement cell)
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                if (cell.RenderTransform is not TransformGroup group) return;
                if (group.Children.Count == 0 || group.Children[0] is not ScaleTransform scale) return;

                var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
                pop.KeyFrames.Add(new LinearDoubleKeyFrame(0.7, KeyTime.FromPercent(0)));
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.35, KeyTime.FromPercent(0.45),
                    new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }));
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

                var flash = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0xD7, 0x00),
                    ShadowDepth = 0,
                    BlurRadius = 16,
                    Opacity = 0.9,
                };
                cell.Effect = flash;
                var fade = new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(650));
                fade.Completed += (_, _) => { try { cell.Effect = null; } catch { } };
                flash.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
            }
            catch (Exception ex) { App.Logger?.Debug("[QuestStamps] pop failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Swell one plate and let its tilt relax toward straight. The z-index bump is what stops
        /// the zoomed stamp from being covered by the one dropped on top of it - the cluster
        /// overlaps by 4px at rest.
        /// </summary>
        private static void ZoomStamp(FrameworkElement? cell, double restAngle, bool on)
        {
            try
            {
                if (cell == null) return;
                Panel.SetZIndex(cell, on ? 20 : 0);

                if (cell.RenderTransform is not TransformGroup group || group.Children.Count < 2) return;
                if (group.Children[0] is not ScaleTransform scale) return;
                if (group.Children[1] is not RotateTransform rotate) return;

                double toScale = on ? QuestStampHoverScale : 1.0;
                double toAngle = on ? restAngle * 0.25 : restAngle;

                if (!MotionFx.AllowTransitions)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    scale.ScaleX = scale.ScaleY = toScale;
                    rotate.Angle = toAngle;
                    return;
                }

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = TimeSpan.FromMilliseconds(on ? 170 : 200);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(toScale, dur) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(toScale, dur) { EasingFunction = ease });
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(toAngle, dur) { EasingFunction = ease });
            }
            catch (Exception ex) { App.Logger?.Debug("[QuestStamps] zoom failed: {E}", ex.Message); }
        }

        private static double QuestStampRestAngle(string key) => key switch
        {
            "d0" => QuestStampAngles[0],
            "d1" => QuestStampAngles[1],
            "d2" => QuestStampAngles[2],
            _ => QuestStampAngles[3],
        };

        // ---- HOVER CARD -----------------------------------------------------------

        /// <summary>
        /// Build the detail card once. Everything on it is set from code in
        /// <see cref="PaintStampPopup"/>; nothing here reads a resource, because a standalone
        /// Popup is not under MainWindow's resource scope.
        /// </summary>
        private void EnsureStampPopup()
        {
            if (_stampPopup != null) return;

            _stampPopSlide = new TranslateTransform(0, -6);

            _stampPopArt = new Image { Stretch = Stretch.UniformToFill };
            _stampPopArtScrim = new Border
            {
                Height = 48,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 1),
                    },
                },
            };

            _stampPopKind = new TextBlock
            {
                Foreground = QuestStampGoldInk,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
            };
            _stampPopXp = new TextBlock
            {
                Foreground = QuestStampPinkStroke,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI Emoji"),
            };

            _stampPopDone = new Border
            {
                Background = Frozen(Color.FromArgb(0x99, 0x2D, 0x4A, 0x2D)),
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "✓",
                    Foreground = QuestStampDoneInk,
                    FontSize = 60,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2 },
                },
            };

            var artFrame = new Border
            {
                Height = 132,
                CornerRadius = new CornerRadius(11, 11, 0, 0),
                Background = QuestStampDarkFill,
                ClipToBounds = true,
                Child = new Grid
                {
                    Children =
                    {
                        _stampPopArt,
                        _stampPopArtScrim,
                        Chip(_stampPopKind, HorizontalAlignment.Left),
                        Chip(_stampPopXp, HorizontalAlignment.Right),
                        _stampPopDone,
                    },
                },
            };

            _stampPopIcon = new TextBlock
            {
                FontSize = 15,
                Foreground = QuestStampWhiteInk,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            _stampPopName = new TextBlock
            {
                Foreground = QuestStampWhiteInk,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _stampPopDesc = new TextBlock
            {
                Foreground = QuestStampMutedInk,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 9),
            };

            _stampPopFill = new Border
            {
                Background = QuestStampGoldInk,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            };
            _stampPopTrack = new Border
            {
                Height = 10,
                Width = QuestStampPopTrackWidth,
                Background = QuestStampDarkFill,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _stampPopFill,
            };

            _stampPopProgress = new TextBlock
            {
                Foreground = QuestStampGoldInk,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _stampPopRemaining = new TextBlock
            {
                Foreground = QuestStampMutedInk,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var counts = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            counts.Children.Add(_stampPopProgress);
            counts.Children.Add(_stampPopRemaining);

            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(_stampPopIcon);
            title.Children.Add(_stampPopName);

            var body = new StackPanel { Margin = new Thickness(14, 11, 14, 12) };
            body.Children.Add(title);
            body.Children.Add(_stampPopDesc);
            body.Children.Add(_stampPopTrack);
            body.Children.Add(counts);
            body.Children.Add(new TextBlock
            {
                Text = Loc.Get("tooltip_quest_stamps"),
                Foreground = QuestStampMutedInk,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 9, 0, 0),
                Opacity = 0.75,
            });

            var stack = new Grid();
            stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(artFrame, 0);
            Grid.SetRow(body, 1);
            stack.Children.Add(artFrame);
            stack.Children.Add(body);

            _stampPopCard = new Border
            {
                Width = QuestStampPopWidth,
                CornerRadius = new CornerRadius(12),
                Background = QuestStampPanelFill,
                BorderThickness = new Thickness(1.5),
                BorderBrush = QuestStampPinkStroke,
                Child = stack,
                RenderTransform = _stampPopSlide,
                // A transient popup is the one place a shadow is worth its shader pass: it exists
                // for as long as a pointer rests on a 20px square.
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 5,
                    Opacity = 0.55,
                },
                // The card is a read-out, never a target. Without this the popup's own window
                // would swallow the pointer the moment it slid under it.
                IsHitTestVisible = false,
            };

            _stampPopup = new Popup
            {
                PlacementTarget = QuestStampHost,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 10,
                HorizontalOffset = -6,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.None,
                Child = _stampPopCard,
            };

            static Border Chip(UIElement content, HorizontalAlignment side) => new()
            {
                Background = QuestStampChipFill,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8),
                HorizontalAlignment = side,
                VerticalAlignment = VerticalAlignment.Top,
                Child = content,
            };
        }

        /// <summary>Paint the card from one stamp's state. The "to go" line is the reason this card
        /// exists: a bar says how far in, never how much is left.</summary>
        private void PaintStampPopup(QuestStampInfo info)
        {
            EnsureStampPopup();
            if (_stampPopCard == null) return;

            bool ghost = info.Def == null || info.Quest == null;
            Brush accent = info.Completed ? QuestStampDoneInk
                         : ghost ? QuestStampGhostStroke
                         : info.IsWeekly ? QuestStampPurpleStroke
                         : QuestStampPinkStroke;

            _stampPopCard.BorderBrush = accent;
            _stampPopKind!.Text = info.Label.ToUpperInvariant();
            _stampPopKind.Foreground = accent;

            if (ghost)
            {
                _stampPopArt!.Source = null;
                _stampPopDone!.Visibility = Visibility.Collapsed;
                _stampPopIcon!.Text = "";
                _stampPopName!.Text = info.IsWeekly ? "No weekly quest yet" : "No quest in this slot";
                _stampPopDesc!.Text = "It will be here after the next roll.";
                _stampPopXp!.Text = "";
                _stampPopFill!.Width = 0;
                _stampPopProgress!.Text = "";
                _stampPopRemaining!.Text = "";
                return;
            }

            var def = info.Def!;
            var quest = info.Quest!;

            _stampPopArt!.Source = GetQuestArt(def);
            _stampPopDone!.Visibility = info.Completed ? Visibility.Visible : Visibility.Collapsed;
            _stampPopIcon!.Text = def.Icon;
            _stampPopName!.Text = QuestStampName(def);
            _stampPopDesc!.Text = QuestStampDescription(def);

            var (xp, bonus) = ComputeQuestXpDisplay(def);
            _stampPopXp!.Text = string.IsNullOrEmpty(bonus) ? $"\U0001f381 {xp} XP" : $"\U0001f381 {xp} XP  {bonus}";
            _stampPopXp.Foreground = accent;

            // A completed quest reads 100% whatever its counter says - trackers that arrive in
            // lumps (whole minutes) routinely overshoot the target.
            double fraction = info.Completed ? 1.0 : info.Fraction;
            int current = info.Completed ? Math.Max(quest.CurrentProgress, def.TargetValue) : quest.CurrentProgress;

            _stampPopFill!.Background = info.Completed ? QuestStampDoneInk : accent;
            _stampPopFill.Width = Math.Max(0, QuestStampPopTrackWidth * fraction);
            _stampPopProgress!.Foreground = info.Completed ? QuestStampDoneInk : accent;
            _stampPopProgress.Text = $"{current} / {def.TargetValue}";

            int left = Math.Max(0, def.TargetValue - quest.CurrentProgress);
            _stampPopRemaining!.Text = info.Completed ? "done" : left > 0 ? $"{left} to go" : "";
        }

        private void ShowStampPopup(QuestStampInfo info, bool animate)
        {
            try
            {
                EnsureStampPopup();
                if (_stampPopup == null || _stampPopCard == null) return;

                PaintStampPopup(info);
                _stampPopup.IsOpen = true;

                if (!animate || !MotionFx.AllowTransitions)
                {
                    _stampPopCard.Opacity = 1;
                    if (_stampPopSlide != null) _stampPopSlide.Y = 0;
                    return;
                }

                _stampPopCard.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
                _stampPopSlide?.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-7, 0, TimeSpan.FromMilliseconds(180))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            catch (Exception ex) { App.Logger?.Debug("[QuestStamps] card open failed: {E}", ex.Message); }
        }

        private void HideStampPopup()
        {
            try
            {
                if (_stampPopup != null) _stampPopup.IsOpen = false;
            }
            catch { /* teardown */ }
        }

        // ---- INTERACTION ----------------------------------------------------------

        private void Stamp_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement cell || cell.Tag is not string key) return;
                if (!_stampInfos.TryGetValue(key, out var info)) return;

                if (_hoveredStampKey != null && _hoveredStampKey != key &&
                    _stampInfos.TryGetValue(_hoveredStampKey, out var previous))
                {
                    ZoomStamp(previous.Cell, QuestStampRestAngle(previous.Key), false);
                }

                _hoveredStampKey = key;
                ZoomStamp(cell, QuestStampRestAngle(key), true);
                // Moving between stamps repaints the open card instead of re-playing its entrance.
                ShowStampPopup(info, animate: _stampPopup?.IsOpen != true);
            }
            catch (Exception ex) { App.Logger?.Debug("[QuestStamps] hover in: {E}", ex.Message); }
        }

        private void Stamp_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement cell || cell.Tag is not string key) return;
                ZoomStamp(cell, QuestStampRestAngle(key), false);
                if (_hoveredStampKey != key) return;     // already moved on to another stamp
                _hoveredStampKey = null;

                // DEFERRED close. The stamps overlap by 4px, so sliding from one to the next raises
                // this Leave immediately before the neighbour's Enter - closing the card here would
                // replay its entrance for a pointer that never left the cluster. One turn of the
                // input queue is enough for that Enter to have claimed the key back.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_hoveredStampKey == null) HideStampPopup();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch (Exception ex) { App.Logger?.Debug("[QuestStamps] hover out: {E}", ex.Message); }
        }

        /// <summary>
        /// A repaint that lands mid-hover has just thrown away the element the pointer was over.
        /// Rather than hope WPF re-raises MouseEnter on the replacement, the hover is re-applied to
        /// the newly built stamp and the open card repainted from its fresh state - which is also
        /// how the card stays live while progress ticks under it.
        /// </summary>
        private void RestoreStampHover()
        {
            if (_hoveredStampKey == null) return;

            if (QuestStampHost?.IsMouseOver == true && _stampInfos.TryGetValue(_hoveredStampKey, out var info))
            {
                ZoomStamp(info.Cell, QuestStampRestAngle(info.Key), true);
                if (_stampPopup?.IsOpen == true) PaintStampPopup(info);
                else ShowStampPopup(info, animate: true);
                return;
            }

            _hoveredStampKey = null;
            HideStampPopup();
        }

        /// <summary>The whole cluster is one target: click goes to the Quests tab through the same
        /// funnel the nav button uses, so bark rules and door expansion behave identically. The
        /// per-stamp hover sits on top of this - the plates do not handle the click, so it bubbles
        /// here from a stamp and from the gaps between them alike.</summary>
        private void QuestStamps_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                HideStampPopup();
                ShowTab("quests");
                e.Handled = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[QuestStamps] navigation failed: {E}", ex.Message);
            }
        }

        private void QuestStamps_MouseEnter(object sender, MouseEventArgs e)
        {
            try { MotionFx.HoverLift(QuestStampHost, true); } catch { }
        }

        private void QuestStamps_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                MotionFx.HoverLift(QuestStampHost, false);
                if (_hoveredStampKey != null && _stampInfos.TryGetValue(_hoveredStampKey, out var info))
                    ZoomStamp(info.Cell, QuestStampRestAngle(info.Key), false);
                _hoveredStampKey = null;
                HideStampPopup();
            }
            catch { }
        }
    }
}
