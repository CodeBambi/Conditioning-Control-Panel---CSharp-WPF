using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    /// the shape of it. Three slots stamped gold is "today is done", one pink square with a sliver
    /// of fill along its bottom is "you are mid-quest", a dashed ghost is "not rolled yet". The
    /// tilt is the point: a card that is perfectly square reads as a button, and a card knocked a
    /// few degrees off reads as something that was stamped onto the panel.</para>
    ///
    /// <para><b>Layout contract - this must not cost the row a single pixel.</b> The host
    /// (<c>QuestStampHost</c>, MainWindow.xaml) is a new <i>Auto</i> column at index 0 of the XP
    /// bar's left grid, ahead of the LVL chip. The bar's own column is star-width, so the cluster
    /// measuring ~71px simply shortens the bar by 71px and nothing else in the row moves - and
    /// collapsing the host (no QuestService) hands all of it straight back. Vertically the host's
    /// Height is PINNED at 22 in XAML: the rotated squares' bounding boxes run a couple of px past
    /// that, and a pinned height means the overhang draws (ClipToBounds is false) without ever
    /// entering the row's measure. A stat pill is ~23px tall, so the row's height is still decided
    /// by the pills exactly as it was before this file existed.</para>
    ///
    /// <para><b>Built in code, rebuilt whole.</b> Four elements is cheaper to re-create than to
    /// diff, and every repaint starts from live quest state rather than from the accumulation of
    /// whatever transitions happened - the same discipline as
    /// <see cref="RefreshSpiralRailEntry"/>. Nothing here is templated, so no converter or resource
    /// needs to exist in Window.Resources for it.</para>
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

        // ---- PALETTE (frozen; these repaint on every quest tick) -------------------

        private static readonly Brush QuestStampGoldStroke = Frozen(Color.FromArgb(0xCC, 0xFF, 0xD7, 0x00));
        private static readonly Brush QuestStampGoldFill = Frozen(Color.FromArgb(0x30, 0xFF, 0xD7, 0x00));
        private static readonly Brush QuestStampGoldInk = Frozen(Color.FromRgb(0xFF, 0xD7, 0x00));

        private static readonly Brush QuestStampPinkStroke = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
        private static readonly Brush QuestStampPanelFill = Frozen(Color.FromRgb(0x25, 0x25, 0x42));

        private static readonly Brush QuestStampPurpleStroke = Frozen(Color.FromRgb(0xB5, 0x7E, 0xDC));
        private static readonly Brush QuestStampPurpleFill = Frozen(Color.FromRgb(0x2A, 0x1F, 0x3D));

        private static readonly Brush QuestStampGhostStroke = Frozen(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly Brush QuestStampGhostFill = Frozen(Color.FromArgb(0x22, 0x1A, 0x1A, 0x2E));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private bool _questStampsWired;

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
                    host.Children.Clear();
                    host.ToolTip = null;
                    host.Visibility = Visibility.Collapsed;
                    return;
                }

                host.Visibility = Visibility.Visible;
                host.Children.Clear();

                int completedToday = quests.GetDailyQuestsCompletedToday();
                var dailyDef = quests.GetCurrentDailyDefinition();
                var dailyActive = quests.Progress?.DailyQuest;
                var weeklyDef = quests.GetCurrentWeeklyDefinition();
                var weeklyActive = quests.Progress?.WeeklyQuest;

                for (int slot = 0; slot < QuestService.MaxDailyQuestsPerDay; slot++)
                {
                    UIElement stamp;
                    if (slot < completedToday)
                    {
                        // STAMPED. Gold outline, gold wash, a check - dimmed, because a finished
                        // slot is a record rather than a thing asking for attention.
                        stamp = BuildQuestStamp(
                            QuestStampDailySize, QuestStampOffsets[slot], QuestStampAngles[slot],
                            QuestStampGoldStroke, QuestStampGoldFill, dashed: false,
                            glyph: "✓", glyphBrush: QuestStampGoldInk, glyphBold: true,
                            fillFraction: 1.0, fillBrush: QuestStampGoldStroke, opacity: 0.8);
                    }
                    else if (slot == completedToday && dailyDef != null && dailyActive != null)
                    {
                        // LIVE. Pink outline on the panel colour, the quest's own icon, and a
                        // sliver of pink along the bottom edge for how far in it is.
                        stamp = BuildQuestStamp(
                            QuestStampDailySize, QuestStampOffsets[slot], QuestStampAngles[slot],
                            QuestStampPinkStroke, QuestStampPanelFill, dashed: false,
                            glyph: dailyDef.Icon, glyphBrush: QuestStampPinkStroke, glyphBold: false,
                            fillFraction: QuestStampFraction(dailyActive.CurrentProgress, dailyDef.TargetValue),
                            fillBrush: QuestStampPinkStroke, opacity: 1.0);
                    }
                    else
                    {
                        // NOT ROLLED. A dashed ghost - present enough to say "there is a third
                        // one", quiet enough not to look like a thing you can act on.
                        stamp = BuildQuestStamp(
                            QuestStampDailySize, QuestStampOffsets[slot], QuestStampAngles[slot],
                            QuestStampGhostStroke, QuestStampGhostFill, dashed: true,
                            glyph: null, glyphBrush: null, glyphBold: false,
                            fillFraction: 0, fillBrush: null, opacity: 0.5);
                    }
                    host.Children.Add(stamp);
                }

                // The weekly: bigger, purple, and it keeps its own fill even while the dailies
                // churn underneath it.
                bool weeklyDone = weeklyActive?.IsCompleted == true;
                UIElement weekly;
                if (weeklyDef == null || weeklyActive == null)
                {
                    weekly = BuildQuestStamp(
                        QuestStampWeeklySize, QuestStampOffsets[3], QuestStampAngles[3],
                        QuestStampGhostStroke, QuestStampGhostFill, dashed: true,
                        glyph: null, glyphBrush: null, glyphBold: false,
                        fillFraction: 0, fillBrush: null, opacity: 0.5);
                }
                else if (weeklyDone)
                {
                    weekly = BuildQuestStamp(
                        QuestStampWeeklySize, QuestStampOffsets[3], QuestStampAngles[3],
                        QuestStampGoldStroke, QuestStampGoldFill, dashed: false,
                        glyph: "✓", glyphBrush: QuestStampGoldInk, glyphBold: true,
                        fillFraction: 1.0, fillBrush: QuestStampGoldStroke, opacity: 0.85);
                }
                else
                {
                    weekly = BuildQuestStamp(
                        QuestStampWeeklySize, QuestStampOffsets[3], QuestStampAngles[3],
                        QuestStampPurpleStroke, QuestStampPurpleFill, dashed: false,
                        glyph: weeklyDef.Icon, glyphBrush: QuestStampPurpleStroke, glyphBold: false,
                        fillFraction: QuestStampFraction(weeklyActive.CurrentProgress, weeklyDef.TargetValue),
                        fillBrush: QuestStampPurpleStroke, opacity: 1.0);
                }
                host.Children.Add(weekly);

                host.ToolTip = BuildQuestStampTooltip(
                    completedToday, dailyDef, dailyActive, weeklyDef, weeklyActive);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[QuestStamps] repaint failed: {E}", ex.Message);
            }
        }

        private static double QuestStampFraction(int current, int target)
        {
            if (target <= 0) return 0;
            return Math.Max(0, Math.Min(1.0, (double)current / target));
        }

        /// <summary>
        /// One stamp. A Grid rather than a Border because the ghost state needs a dashed outline,
        /// and only a Shape carries StrokeDashArray - so the outline is a rounded Rectangle and the
        /// glyph and the progress sliver are siblings stacked on top of it.
        /// </summary>
        private static UIElement BuildQuestStamp(
            double size, double left, double angle,
            Brush stroke, Brush fill, bool dashed,
            string? glyph, Brush? glyphBrush, bool glyphBold,
            double fillFraction, Brush? fillBrush, double opacity)
        {
            var cell = new Grid
            {
                Width = size,
                Height = size,
                Opacity = opacity,
                Margin = new Thickness(left, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle),
                IsHitTestVisible = false,   // the HOST owns the click, gaps included
            };

            var plate = new Rectangle
            {
                RadiusX = 3,
                RadiusY = 3,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1.5,
            };
            if (dashed)
            {
                plate.StrokeDashArray = new DoubleCollection { 2, 1.6 };
                plate.StrokeThickness = 1.0;
            }
            cell.Children.Add(plate);

            if (!string.IsNullOrWhiteSpace(glyph))
            {
                cell.Children.Add(new TextBlock
                {
                    Text = glyph,
                    FontSize = size >= QuestStampWeeklySize ? 12 : 10.5,
                    FontWeight = glyphBold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = glyphBrush ?? Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                });
            }

            if (fillFraction > 0 && fillBrush != null)
            {
                double track = size - (QuestStampFillInset * 2);
                cell.Children.Add(new Border
                {
                    Height = QuestStampFillHeight,
                    Width = Math.Max(1.0, track * fillFraction),
                    CornerRadius = new CornerRadius(1),
                    Background = fillBrush,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(QuestStampFillInset, 0, 0, QuestStampFillInset),
                });
            }

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

        private static string BuildQuestStampTooltip(
            int completedToday,
            QuestDefinition? dailyDef, ActiveQuest? dailyActive,
            QuestDefinition? weeklyDef, ActiveQuest? weeklyActive)
        {
            var sb = new StringBuilder();
            sb.Append(Loc.Get("tooltip_quest_stamps"));

            sb.Append('\n').Append('\n')
              .Append(Loc.Get("quest_daily")).Append("  ")
              .Append(completedToday).Append('/').Append(QuestService.MaxDailyQuestsPerDay);
            if (dailyDef != null && dailyActive != null && completedToday < QuestService.MaxDailyQuestsPerDay)
            {
                sb.Append('\n').Append(dailyDef.Icon).Append(' ').Append(QuestStampName(dailyDef))
                  .Append("  ").Append(dailyActive.CurrentProgress).Append('/').Append(dailyDef.TargetValue);
            }

            sb.Append('\n').Append('\n').Append(Loc.Get("quest_weekly"));
            if (weeklyDef != null && weeklyActive != null)
            {
                sb.Append('\n').Append(weeklyDef.Icon).Append(' ').Append(QuestStampName(weeklyDef))
                  .Append("  ").Append(weeklyActive.CurrentProgress).Append('/').Append(weeklyDef.TargetValue);
            }

            return sb.ToString();
        }

        // ---- INTERACTION ----------------------------------------------------------

        /// <summary>The whole cluster is one target: click goes to the Quests tab through the same
        /// funnel the nav button uses, so bark rules and door expansion behave identically.</summary>
        private void QuestStamps_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
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
            try { MotionFx.HoverLift(QuestStampHost, false); } catch { }
        }
    }
}
