// PORTED from WPF 7.1.5 MainWindow/MainWindow.QuestStamps.cs: the stamp cluster in the header,
// one plate per daily quest (three) plus the weekly (bigger, purple). Sizes, offsets, tilts,
// colours and timings are WPF's.
//
// A plate wears the quest's own art (QuestsTabView.GetQuestArt), a light scrim, the check when
// done (gold), the quest icon when there is no art, and a progress sliver along its foot. A
// slot flipping to done pops once (0.7 -> 1.35 at 45% -> 1 over 420 ms) with a gold flash that
// fades over 650 ms and removes itself. Hovering a plate swells it to 1.5 and relaxes its tilt
// to a quarter (170 ms in, 200 ms out, cubic out) and opens the detail card under the cluster
// (fade 150 ms, 7 px slide 180 ms); the whole cluster lifts 2% on hover (MotionFx.HoverLift).
//
// Avalonia differences, on purpose (the head's FX law):
//  - the gold flash is a sibling Border wearing a BoxShadow whose Opacity fades, never an Effect;
//  - transforms ride Helpers/TransformTween, never Animation.RunAsync(aTransform);
//  - the card opens BELOW the cluster (a popup over its trigger flickers);
//  - WPF's BackEase on the pop is carried as its keyframes (linear between), the overshoot being
//    the 1.35 key itself.
// The card leads with the quest's art strip (132 px, the kind and the reward as chips on it, the green
// check over it when done). A mod switch drops the decoded art and repaints (DropArtCache, wired to
// CoreMods.ModChanged here; the shell's mod re-skin may call it too, it is safe to call twice).
// Not ported: the card's drop shadow (WPF DropShadowEffect blur 20; the popup has no room around the card).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ---- dials (WPF) ------------------------------------------------------------
        internal const double QuestStampDailySize = 20;
        internal const double QuestStampWeeklySize = 23;
        internal static readonly double[] QuestStampOffsets = { 0, 16, 32, 48 };
        internal static readonly double[] QuestStampAngles = { -4, 3, -2, 5 };
        private const double QuestStampFillInset = 2.5;
        private const double QuestStampFillHeight = 2.5;
        internal const double QuestStampHoverScale = 1.5;
        private const double QuestStampPopWidth = 300;
        private const double QuestStampPopTrackWidth = QuestStampPopWidth - 28;

        // ---- palette (WPF) ----------------------------------------------------------
        private static IBrush StampInk(uint argb) => new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromUInt32(argb));
        private static readonly IBrush QuestStampGoldStroke = StampInk(0xCCFFD700);
        private static readonly IBrush QuestStampGoldFill = StampInk(0x30FFD700);
        private static readonly IBrush QuestStampGoldInk = StampInk(0xFFFFD700);
        private static readonly IBrush QuestStampGoldWash = StampInk(0x888A6D00);
        private static readonly IBrush QuestStampPinkStroke = StampInk(0xFFFF69B4);
        private static readonly IBrush QuestStampPanelFill = StampInk(0xFF252542);
        private static readonly IBrush QuestStampDarkFill = StampInk(0xFF1A1A2E);
        private static readonly IBrush QuestStampPurpleStroke = StampInk(0xFFB57EDC);
        private static readonly IBrush QuestStampPurpleFill = StampInk(0xFF2A1F3D);
        private static readonly IBrush QuestStampGhostStroke = StampInk(0xFF3D3D60);
        private static readonly IBrush QuestStampGhostFill = StampInk(0x221A1A2E);
        private static readonly IBrush QuestStampArtScrim = StampInk(0x2E0A0A18);
        private static readonly IBrush QuestStampDoneInk = StampInk(0xFF00E676);
        private static readonly IBrush QuestStampMutedInk = StampInk(0xFFA0A0C0);
        private static readonly IBrush QuestStampWhiteInk = StampInk(0xFFFFFFFF);

        private sealed class QuestStampInfo
        {
            public string Key = "";              // "d0".."d2", "w"
            public string Label = "";
            public bool IsWeekly;
            public QuestDefinition? Def;
            public ActiveQuest? Quest;
            public bool Completed;
            public double Fraction;
            public Panel? Cell;
            public ScaleTransform? Scale;
            public RotateTransform? Rotate;
        }

        private readonly Dictionary<string, QuestStampInfo> _stampInfos = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _stampCompletedSeen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DispatcherTimer[]> _stampTweens = new(StringComparer.Ordinal);
        private string? _hoveredStampKey;
        private bool _questStampsWired;
        private QuestService? _questStampsService;
        private readonly ScaleTransform _stampHostScale = new(1, 1);
        private DispatcherTimer? _stampHostTween;

        private Popup? _stampPopup;
        private Border? _stampPopCard;
        private readonly TranslateTransform _stampPopSlide = new();
        private DispatcherTimer? _stampPopTween;
        private TextBlock? _stampPopKind, _stampPopXp, _stampPopName, _stampPopDesc, _stampPopProgress, _stampPopRemaining, _stampPopIcon;
        private Image? _stampPopArt;
        private Border? _stampPopDone;
        private static readonly IBrush QuestStampChipFill = StampInk(0xB01A1A2E);
        private Border? _stampPopFill;

        // ---- test seams -------------------------------------------------------------
        internal int QuestStampCount => _stampInfos.Count;
        internal int QuestStampPops { get; private set; }
        internal int QuestStampFlashesLive { get; private set; }
        internal string? HoveredQuestStamp => _hoveredStampKey;
        internal bool QuestStampCardPainted => _stampPopCard != null;
        internal string? QuestStampCardName => _stampPopName?.Text;
        internal Image? QuestStampCardArt => _stampPopArt;
        internal bool QuestStampCardDoneShown => _stampPopDone?.IsVisible == true;
        internal int QuestStampArtDrops { get; private set; }
        internal double QuestStampCardOpacity => _stampPopCard?.Opacity ?? 0;
        internal Panel? QuestStampCell(string key) => _stampInfos.TryGetValue(key, out var i) ? i.Cell : null;
        internal (double Scale, double Angle) QuestStampPose(string key) =>
            _stampInfos.TryGetValue(key, out var i) && i.Scale != null && i.Rotate != null ? (i.Scale.ScaleX, i.Rotate.Angle) : (double.NaN, double.NaN);
        internal void HoverQuestStampForTests(string key, bool on)
        {
            if (!_stampInfos.TryGetValue(key, out var i) || i.Cell == null) return;
            if (on) StampEnter(i.Cell); else StampLeave(i.Cell);
        }

        /// <summary>Test seam: forces the motion verdict instead of asking the head.</summary>
        internal bool? QuestStampMotionOverride { get; set; }
        private bool StampMotion => QuestStampMotionOverride ?? AmbientFxCanvas.Env.AllowTransitions;

        // ---- wiring -----------------------------------------------------------------

        /// <summary>
        /// Mod switched (WPF OnQuestStampsModChanged): the same quest can resolve to a different picture, so
        /// the decoded art the stamps and the Quests tab share is dropped, the card closes and everything
        /// that showed the art repaints. The one door for it: the shell's mod re-skin calls this too.
        /// Safe to call twice and from any thread.
        /// </summary>
        internal void DropArtCache()
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(DropArtCache); return; }
                QuestsTabView.ClearQuestArtCache();
                QuestStampArtDrops++;
                HideStampPopup();
                RefreshQuestStamps();
                if (Named<QuestsTabView>("QuestsTab") is { IsVisible: true } questsTab) questsTab.RefreshQuestUI();
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] mod switch: {E}", ex.Message); }
        }

        /// <summary>Called from the constructor; wires again on Opened / Activated until the quest
        /// service exists (it can start after the first window on this head).</summary>
        internal void InitializeQuestStamps()
        {
            try
            {
                if (!_questStampsWired)
                {
                    _questStampsWired = true;
                    if (Named<Grid>("QuestStampHost") is { } host) host.RenderTransform = _stampHostScale;
                    LocalizationManager.Instance.LanguageChanged += OnQuestStampsLanguageChanged;
                    // WPF :207-211: the stamps are the one quest surface on screen whatever the tab, so the mod
                    // switch is heard here.
                    EventHandler<ConditioningControlPanel.Models.ModPackage> modChanged = (_, _) => DropArtCache();
                    CoreMods.ModChanged += modChanged;
                    Closed += (_, _) => CoreMods.ModChanged -= modChanged;
                    Opened += (_, _) => InitializeQuestStamps();
                    Activated += (_, _) => { if (!ReferenceEquals(_questStampsService, App.Quests)) InitializeQuestStamps(); };
                    // A popup is a window: it does not travel with the main window and has no
                    // business outliving a deactivation.
                    Deactivated += (_, _) => HideStampPopup();
                    PositionChanged += (_, _) => HideStampPopup();
                    Closed += (_, _) =>
                    {
                        try
                        {
                            UnhookQuestStampService();
                            LocalizationManager.Instance.LanguageChanged -= OnQuestStampsLanguageChanged;
                            HideStampPopup();
                            foreach (var set in _stampTweens.Values) foreach (var t in set) t.Stop();
                            _stampHostTween?.Stop();
                            _stampPopTween?.Stop();
                        }
                        catch { /* teardown */ }
                    };
                }
                if (!ReferenceEquals(_questStampsService, App.Quests))
                {
                    UnhookQuestStampService();
                    _questStampsService = App.Quests;
                    if (_questStampsService is { } q)
                    {
                        q.QuestCompleted += OnQuestStampsQuestCompleted;
                        q.QuestProgressChanged += OnQuestStampsProgressChanged;
                        q.QuestsRefreshed += OnQuestStampsRefreshed;
                    }
                }
                RefreshQuestStamps();
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] cluster could not be wired: {E}", ex.Message); }
        }

        private void UnhookQuestStampService()
        {
            if (_questStampsService is not { } q) return;
            q.QuestCompleted -= OnQuestStampsQuestCompleted;
            q.QuestProgressChanged -= OnQuestStampsProgressChanged;
            q.QuestsRefreshed -= OnQuestStampsRefreshed;
            _questStampsService = null;
        }

        private void OnQuestStampsQuestCompleted(object? sender, QuestCompletedEventArgs e) => QueueQuestStampRepaint();
        private void OnQuestStampsProgressChanged(object? sender, QuestProgressEventArgs e) => QueueQuestStampRepaint();
        private void OnQuestStampsRefreshed(object? sender, EventArgs e) => QueueQuestStampRepaint();
        private void OnQuestStampsLanguageChanged(object? sender, EventArgs e) => QueueQuestStampRepaint();

        private void QueueQuestStampRepaint()
        {
            try { Dispatcher.UIThread.Post(RefreshQuestStamps); }
            catch { /* fire-and-forget */ }
        }

        // ---- paint ------------------------------------------------------------------

        /// <summary>Rebuild the four stamps from live quest state, from scratch every time.</summary>
        internal void RefreshQuestStamps()
        {
            if (Named<Grid>("QuestStampHost") is not { } host) return;
            try
            {
                foreach (var set in _stampTweens.Values) foreach (var t in set) t.Stop();
                _stampTweens.Clear();
                QuestStampFlashesLive = 0;
                foreach (var old in host.Children.Where(c => c is not Popup).ToArray()) host.Children.Remove(old);
                _stampInfos.Clear();

                var quests = _questStampsService ?? App.Quests;
                if (quests == null)
                {
                    // No service: collapse so the star-width bar reclaims the whole row.
                    HideStampPopup();
                    host.IsVisible = false;
                    return;
                }
                host.IsVisible = true;

                var board = quests.GetDailySlots();
                for (int slot = 0; slot < 3; slot++)
                {
                    var quest = slot < board.Count ? board[slot].Quest : null;
                    var def = slot < board.Count ? board[slot].Definition : null;
                    AddStamp(host, new QuestStampInfo
                    {
                        Key = "d" + slot,
                        Label = $"{Loc.Get("quest_daily")} {slot + 1}",
                        Def = def,
                        Quest = quest,
                        Completed = quest?.IsCompleted == true,
                        Fraction = quest != null && def != null ? QuestStampFraction(quest.CurrentProgress, def.TargetValue) : 0,
                    }, QuestStampDailySize, QuestStampOffsets[slot], QuestStampAngles[slot]);
                }

                var weeklyDef = quests.GetCurrentWeeklyDefinition();
                var weeklyActive = quests.Progress?.WeeklyQuest;
                AddStamp(host, new QuestStampInfo
                {
                    Key = "w",
                    Label = Loc.Get("quest_weekly"),
                    IsWeekly = true,
                    Def = weeklyDef,
                    Quest = weeklyActive,
                    Completed = weeklyActive?.IsCompleted == true,
                    Fraction = weeklyActive != null && weeklyDef != null ? QuestStampFraction(weeklyActive.CurrentProgress, weeklyDef.TargetValue) : 0,
                }, QuestStampWeeklySize, QuestStampOffsets[3], QuestStampAngles[3]);

                RestoreStampHover();
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] repaint failed: {E}", ex.Message); }
        }

        private void AddStamp(Panel host, QuestStampInfo info, double size, double left, double angle)
        {
            var cell = BuildQuestStamp(info, size, left, angle);
            info.Cell = cell;
            _stampInfos[info.Key] = info;
            host.Children.Add(cell);

            // Only a TRANSITION pops: a slot found already done on the first paint stays quiet.
            bool known = _stampCompletedSeen.TryGetValue(info.Key, out var wasDone);
            _stampCompletedSeen[info.Key] = info.Completed;
            if (info.Completed && known && !wasDone) PlayStampStampedPop(info);
        }

        internal static double QuestStampFraction(int current, int target) =>
            target <= 0 ? 0 : Math.Max(0, Math.Min(1.0, (double)current / target));

        private Panel BuildQuestStamp(QuestStampInfo info, double size, double left, double angle)
        {
            bool ghost = info.Def == null || info.Quest == null;
            bool done = info.Completed;
            IBrush stroke = ghost ? QuestStampGhostStroke : done ? QuestStampGoldStroke : info.IsWeekly ? QuestStampPurpleStroke : QuestStampPinkStroke;
            IBrush plainFill = ghost ? QuestStampGhostFill : done ? QuestStampGoldFill : info.IsWeekly ? QuestStampPurpleFill : QuestStampPanelFill;
            var art = ghost ? null : QuestsTabView.GetQuestArt(info.Def!);

            info.Scale = new ScaleTransform(1, 1);
            info.Rotate = new RotateTransform(angle);
            var cell = new Panel
            {
                Width = size,
                Height = size,
                Opacity = ghost ? 0.5 : done ? 0.85 : 1.0,
                Margin = new Thickness(left, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { info.Scale, info.Rotate } },
                Background = Brushes.Transparent,   // the plate's own gaps still hover
                Tag = info.Key,
            };

            var plate = new Rectangle { RadiusX = 3, RadiusY = 3, Fill = plainFill, Stroke = stroke, StrokeThickness = 1.5 };
            if (ghost)
            {
                plate.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 2, 1.6 };
                plate.StrokeThickness = 1.0;
            }
            else if (art != null)
            {
                plate.Fill = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            }
            cell.Children.Add(plate);

            if (art != null)
                cell.Children.Add(new Rectangle { RadiusX = 3, RadiusY = 3, Fill = done ? QuestStampGoldWash : QuestStampArtScrim, IsHitTestVisible = false });

            string? glyph = done ? "✓" : art == null && !ghost ? info.Def!.Icon : null;
            if (!string.IsNullOrWhiteSpace(glyph))
                cell.Children.Add(new TextBlock
                {
                    Text = glyph,
                    FontSize = size >= QuestStampWeeklySize ? 12 : 10.5,
                    FontWeight = done ? FontWeight.Bold : FontWeight.Normal,
                    Foreground = done ? QuestStampGoldInk : stroke,
                    FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false,
                });

            double fraction = done ? 1.0 : info.Fraction;
            if (fraction > 0 && !ghost)
                cell.Children.Add(new Border
                {
                    Height = QuestStampFillHeight,
                    Width = Math.Max(1.0, (size - QuestStampFillInset * 2) * fraction),
                    CornerRadius = new CornerRadius(1),
                    Background = done ? QuestStampGoldStroke : stroke,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(QuestStampFillInset, 0, 0, QuestStampFillInset),
                    IsHitTestVisible = false,
                });

            cell.PointerEntered += (s, _) => StampEnter((Panel)s!);
            cell.PointerExited += (s, _) => StampLeave((Panel)s!);
            return cell;
        }

        private static string QuestStampText(string localized, string plain)
        {
            var text = string.IsNullOrWhiteSpace(localized) || localized.StartsWith("quest_", StringComparison.Ordinal) ? plain : localized;
            return CoreMods.MakeModAware(text) ?? text;
        }

        // ---- fx ---------------------------------------------------------------------

        private void TrackStampTween(string key, params DispatcherTimer[] timers)
        {
            if (_stampTweens.Remove(key, out var old)) foreach (var t in old) t.Stop();
            if (timers.Length > 0) _stampTweens[key] = timers;
        }

        /// <summary>The moment a slot flips to done: an overshoot pop plus a gold flash that fades
        /// and then removes itself.</summary>
        private void PlayStampStampedPop(QuestStampInfo info)
        {
            try
            {
                if (!StampMotion || info.Cell == null || info.Scale == null) return;
                QuestStampPops++;
                var pop = TransformTween.Run(info.Scale, TimeSpan.FromMilliseconds(420),
                    new (double, AvaloniaProperty, double)[]
                    {
                        (0, ScaleTransform.ScaleXProperty, 0.7), (0.45, ScaleTransform.ScaleXProperty, 1.35), (1, ScaleTransform.ScaleXProperty, 1.0),
                        (0, ScaleTransform.ScaleYProperty, 0.7), (0.45, ScaleTransform.ScaleYProperty, 1.35), (1, ScaleTransform.ScaleYProperty, 1.0),
                    });

                // WPF: DropShadowEffect gold, blur 16, 0.9 -> 0 over 650 ms, then detached. Here a
                // sibling behind the plate wearing the same glow as a BoxShadow.
                var cell = info.Cell;
                var flash = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    IsHitTestVisible = false,
                    Opacity = 0.9,
                    BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromRgb(0xFF, 0xD7, 0x00) }),
                };
                cell.Children.Insert(0, flash);
                QuestStampFlashesLive++;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                DispatcherTimer? fade = null;
                fade = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
                {
                    double p = Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds / 650, 0, 1);
                    flash.Opacity = Math.Clamp(0.9 * (1 - p), 0, 1);
                    if (p < 1 && cell.Parent != null) return;
                    fade!.Stop();
                    cell.Children.Remove(flash);
                    QuestStampFlashesLive = Math.Max(0, QuestStampFlashesLive - 1);
                });
                fade.Start();
                TrackStampTween(info.Key, pop, fade);
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] pop failed: {E}", ex.Message); }
        }

        /// <summary>Swell one plate and let its tilt relax toward straight; the z-index bump keeps
        /// the zoomed stamp over the one dropped on top of it.</summary>
        private void ZoomStamp(QuestStampInfo? info, bool on)
        {
            try
            {
                if (info?.Cell == null || info.Scale == null || info.Rotate == null) return;
                info.Cell.ZIndex = on ? 20 : 0;
                double rest = QuestStampRestAngle(info.Key);
                double toScale = on ? QuestStampHoverScale : 1.0;
                double toAngle = on ? rest * 0.25 : rest;
                if (!StampMotion)
                {
                    TrackStampTween(info.Key);
                    info.Scale.ScaleX = info.Scale.ScaleY = toScale;
                    info.Rotate.Angle = toAngle;
                    return;
                }
                var dur = TimeSpan.FromMilliseconds(on ? 170 : 200);
                double s0 = info.Scale.ScaleX, a0 = info.Rotate.Angle;
                TrackStampTween(info.Key,
                    TransformTween.Run(info.Scale, dur, new (double, AvaloniaProperty, double)[]
                    {
                        (0, ScaleTransform.ScaleXProperty, s0), (1, ScaleTransform.ScaleXProperty, toScale),
                        (0, ScaleTransform.ScaleYProperty, s0), (1, ScaleTransform.ScaleYProperty, toScale),
                    }, new CubicEaseOut()),
                    TransformTween.Run(info.Rotate, dur, new (double, AvaloniaProperty, double)[]
                    {
                        (0, RotateTransform.AngleProperty, a0), (1, RotateTransform.AngleProperty, toAngle),
                    }, new CubicEaseOut()));
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] zoom failed: {E}", ex.Message); }
        }

        internal static double QuestStampRestAngle(string key) => key switch
        {
            "d0" => QuestStampAngles[0],
            "d1" => QuestStampAngles[1],
            "d2" => QuestStampAngles[2],
            _ => QuestStampAngles[3],
        };

        /// <summary>WPF MotionFx.HoverLift on the whole cluster: 1.02 over 150 ms, snapped at Off.</summary>
        private void LiftStampHost(bool on)
        {
            _stampHostTween?.Stop();
            double to = on ? MotionTimings.HoverLiftScale : 1.0;
            if (!StampMotion) { _stampHostScale.ScaleX = _stampHostScale.ScaleY = to; return; }
            double from = _stampHostScale.ScaleX;
            _stampHostTween = TransformTween.Run(_stampHostScale, TimeSpan.FromMilliseconds(MotionTimings.HoverMs),
                new (double, AvaloniaProperty, double)[]
                {
                    (0, ScaleTransform.ScaleXProperty, from), (1, ScaleTransform.ScaleXProperty, to),
                    (0, ScaleTransform.ScaleYProperty, from), (1, ScaleTransform.ScaleYProperty, to),
                }, new QuadraticEaseOut());
        }

        // ---- the hover card ---------------------------------------------------------

        private void EnsureStampPopup()
        {
            if (_stampPopup != null || Named<Grid>("QuestStampHost") is not { } host) return;
            // WPF EnsureStampPopup (MainWindow.QuestStamps.cs:632-815): sizes, margins and order are the original's.
            _stampPopArt = new Image { Stretch = Stretch.UniformToFill };
            var artScrim = new Border
            {
                Height = 48,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 1) },
                },
            };
            _stampPopKind = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.Bold };
            _stampPopXp = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI") };
            // the green check over a finished quest's art (no Effect: WPF's 8 px text shadow is not carried)
            _stampPopDone = new Border
            {
                Background = StampInk(0x992D4A2D),
                IsVisible = false,
                Child = new TextBlock
                {
                    Text = "\u2713", Foreground = QuestStampDoneInk, FontSize = 60, FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            static Border Chip(Control content, HorizontalAlignment side) => new()
            {
                Background = QuestStampChipFill, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Top, Child = content,
            };
            var artFrame = new Border
            {
                Height = 132,
                CornerRadius = new CornerRadius(11, 11, 0, 0),
                Background = QuestStampDarkFill,
                ClipToBounds = true,
                Tag = "stamp-card-art",
                Child = new Panel
                {
                    Children = { _stampPopArt, artScrim, Chip(_stampPopKind, HorizontalAlignment.Left), Chip(_stampPopXp, HorizontalAlignment.Right), _stampPopDone },
                },
            };

            _stampPopIcon = new TextBlock
            {
                FontSize = 15, Foreground = QuestStampWhiteInk, FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            };
            _stampPopName = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, Foreground = QuestStampWhiteInk, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            _stampPopDesc = new TextBlock { FontSize = 11.5, Foreground = QuestStampMutedInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 9) };
            _stampPopFill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _stampPopProgress = new TextBlock { FontSize = 12, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Left };
            _stampPopRemaining = new TextBlock { FontSize = 11, Foreground = QuestStampMutedInk, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var track = new Border
            {
                Height = 10,
                Width = QuestStampPopTrackWidth,
                CornerRadius = new CornerRadius(4),
                Background = QuestStampDarkFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _stampPopFill,
            };
            var title = new DockPanel();
            DockPanel.SetDock(_stampPopIcon, Dock.Left);
            title.Children.Add(_stampPopIcon);
            title.Children.Add(_stampPopName);
            var body = new StackPanel
            {
                Margin = new Thickness(14, 11, 14, 12),
                Children =
                {
                    title, _stampPopDesc, track,
                    new Panel { Margin = new Thickness(0, 7, 0, 0), Children = { _stampPopProgress, _stampPopRemaining } },
                    new TextBlock
                    {
                        Text = ConditioningControlPanel.Localization.Loc.Get("tooltip_quest_stamps"), Foreground = QuestStampMutedInk, FontSize = 10,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0), Opacity = 0.75,
                    },
                },
            };
            _stampPopCard = new Border
            {
                Width = QuestStampPopWidth,
                CornerRadius = new CornerRadius(12),
                Background = QuestStampPanelFill,
                BorderThickness = new Thickness(1.5),
                RenderTransform = _stampPopSlide,
                IsHitTestVisible = false,
                Child = new StackPanel { Children = { artFrame, body } },
            };
            _stampPopup = new Popup
            {
                PlacementTarget = host,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 8,
                IsLightDismissEnabled = false,
                IsHitTestVisible = false,
                Child = _stampPopCard,
            };
            host.Children.Add(_stampPopup);   // a Popup needs a place in the tree; it takes no room
        }

        private void PaintStampPopup(QuestStampInfo info)
        {
            EnsureStampPopup();
            if (_stampPopCard == null) return;
            bool ghost = info.Def == null || info.Quest == null;
            IBrush accent = info.Completed ? QuestStampDoneInk : ghost ? QuestStampGhostStroke : info.IsWeekly ? QuestStampPurpleStroke : QuestStampPinkStroke;
            _stampPopCard.BorderBrush = accent;
            _stampPopKind!.Text = info.Label.ToUpperInvariant();
            _stampPopKind.Foreground = accent;
            if (ghost)
            {
                // WPF's own (unkeyed) lines for an empty seat.
                _stampPopArt!.Source = null;
                _stampPopDone!.IsVisible = false;
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
            _stampPopArt!.Source = QuestsTabView.GetQuestArt(def);
            _stampPopDone!.IsVisible = info.Completed;
            _stampPopIcon!.Text = def.Icon;
            _stampPopName!.Text = QuestStampText(def.LocalizedName, def.Name);
            _stampPopDesc!.Text = QuestStampText(def.LocalizedDescription, def.Description);
            var (xp, bonus) = QuestsTabView.ComputeQuestXpDisplay(def, CoreSettings.Current);
            _stampPopXp!.Text = string.IsNullOrEmpty(bonus) ? $"\U0001f381 {xp} XP" : $"\U0001f381 {xp} XP  {bonus}";
            _stampPopXp.Foreground = accent;
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
                _stampPopTween?.Stop();
                try { _stampPopup.IsOpen = true; } catch (Exception ex) { Log.Debug("[QuestStamps] card open: {E}", ex.Message); }
                if (!animate || !StampMotion)
                {
                    _stampPopCard.Opacity = 1;
                    _stampPopSlide.Y = 0;
                    return;
                }
                // WPF: opacity 0 -> 1 in 150 ms, slide -7 -> 0 in 180 ms cubic out.
                _stampPopCard.Opacity = 0;
                _stampPopSlide.Y = -7;
                var card = _stampPopCard;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                DispatcherTimer? timer = null;
                timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
                {
                    double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    card.Opacity = Math.Clamp(ms / 150, 0, 1);
                    double p = Math.Clamp(ms / 180, 0, 1);
                    _stampPopSlide.Y = -7 * Math.Pow(1 - p, 3);
                    if (p >= 1) timer!.Stop();
                });
                _stampPopTween = timer;
                timer.Start();
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] card open failed: {E}", ex.Message); }
        }

        private void HideStampPopup()
        {
            try
            {
                _stampPopTween?.Stop();
                if (_stampPopup != null) _stampPopup.IsOpen = false;
            }
            catch { /* teardown */ }
        }

        // ---- interaction ------------------------------------------------------------

        private void StampEnter(Panel cell)
        {
            try
            {
                if (cell.Tag is not string key || !_stampInfos.TryGetValue(key, out var info)) return;
                if (_hoveredStampKey != null && _hoveredStampKey != key && _stampInfos.TryGetValue(_hoveredStampKey, out var previous))
                    ZoomStamp(previous, false);
                _hoveredStampKey = key;
                ZoomStamp(info, true);
                // Moving between stamps repaints the open card instead of re-playing its entrance.
                ShowStampPopup(info, animate: _stampPopup?.IsOpen != true);
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] hover in: {E}", ex.Message); }
        }

        private void StampLeave(Panel cell)
        {
            try
            {
                if (cell.Tag is not string key) return;
                if (_stampInfos.TryGetValue(key, out var info)) ZoomStamp(info, false);
                if (_hoveredStampKey != key) return;     // already moved on to another stamp
                _hoveredStampKey = null;
                // Deferred close: the stamps overlap by 4 px, so this Leave lands right before the
                // neighbour's Enter; one turn of the input queue lets that Enter claim the key back.
                Dispatcher.UIThread.Post(() => { if (_hoveredStampKey == null) HideStampPopup(); }, DispatcherPriority.Input);
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] hover out: {E}", ex.Message); }
        }

        /// <summary>A repaint that lands mid-hover re-applies the hover to the new plate.</summary>
        private void RestoreStampHover()
        {
            if (_hoveredStampKey == null) return;
            if (Named<Grid>("QuestStampHost")?.IsPointerOver == true && _stampInfos.TryGetValue(_hoveredStampKey, out var info))
            {
                ZoomStamp(info, true);
                if (_stampPopup?.IsOpen == true) PaintStampPopup(info);
                else ShowStampPopup(info, animate: true);
                return;
            }
            _hoveredStampKey = null;
            HideStampPopup();
        }

        /// <summary>The whole cluster is one target: a click goes to the Quests tab.</summary>
        private void QuestStamps_Click(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e)
        {
            try
            {
                HideStampPopup();
                e.Handled = true;
                ShowTab("quests");
            }
            catch (Exception ex) { Log.Debug("[QuestStamps] navigation failed: {E}", ex.Message); }
        }

        private void QuestStamps_MouseEnter(object? sender, global::Avalonia.Input.PointerEventArgs e)
        {
            try { LiftStampHost(true); } catch { }
        }

        private void QuestStamps_MouseLeave(object? sender, global::Avalonia.Input.PointerEventArgs e)
        {
            try
            {
                LiftStampHost(false);
                if (_hoveredStampKey != null && _stampInfos.TryGetValue(_hoveredStampKey, out var info)) ZoomStamp(info, false);
                _hoveredStampKey = null;
                HideStampPopup();
            }
            catch { }
        }
    }
}
