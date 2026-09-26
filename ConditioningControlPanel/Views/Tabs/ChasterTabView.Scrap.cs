using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The month's top ten on a torn scrap of the calendar's paper, pinned beside the heads-up
    /// clock (owner, 2026-09-26: "the raffle AND the ladder", "brag rights only"). Always in view,
    /// never a popup. Rank, a name for those who opted in (the server's made-up label in italics
    /// for everyone else) and time added; the player's own row is lit with a highlighter, below a
    /// gap when they sit outside the ten. No board, or no link, is a short line, never an error.
    /// </summary>
    public partial class ChasterTabView
    {
        private const double ScrapTiltDegrees = 2.5;

        private static readonly Brush ScrapInk = Frozen(Color.FromRgb(0x24, 0x1A, 0x2E));
        private static readonly Brush ScrapInkSoft = Frozen(Color.FromArgb(0x99, 0x24, 0x1A, 0x2E));
        private static readonly Brush ScrapMarker = Frozen(Color.FromArgb(0x70, 0xFF, 0x6B, 0x8A));
        private static readonly Brush ScrapTimeInk = Frozen(Color.FromRgb(0x8A, 0x18, 0x30));

        private LadderBoard? _ladderBoard;
        private string? _scrapSignature;
        private bool _ladderBoardLoading;

        private void ScrapInit()
        {
            _loading = true;
            try { ChkLadderName.IsChecked = App.Settings?.Current?.ChasterLadderShowName == true; }
            finally { _loading = false; }
            PaintScrap();
        }

        /// <summary>From LadderOnShown: the scrap settles on its pin, then the board is read.</summary>
        private void ScrapOnShown()
        {
            ScrapSettle();
            if (App.Chaster?.IsLinked != true) { PaintScrap(); return; }
            _ = FetchScrapAsync();
        }

        private async Task FetchScrapAsync()
        {
            var chaster = App.Chaster;
            if (chaster == null || _ladderBoardLoading) return;
            _ladderBoardLoading = true;
            try
            {
                var board = await Task.Run(() => chaster.LadderAsync());
                await Dispatcher.InvokeAsync(() =>
                {
                    _ladderBoard = board;
                    PaintScrap();
                });
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder board"); }
            finally { _ladderBoardLoading = false; }
        }

        private void PaintScrap() => PaintScrap(App.Chaster?.IsLinked == true ? _ladderBoard : null);

        /// <summary>Draws a board on the scrap; null is the quiet line (no board, or not linked).</summary>
        internal void PaintScrap(LadderBoard? board)
        {
            try
            {
                LadderScrapRows.Children.Clear();
                string? quiet = null;
                if (board == null) quiet = Loc.Get("chaster_ladder_off");
                else if (board.Rows.Count == 0 && board.You == null) quiet = Loc.Get("chaster_ladder_empty");
                else
                {
                    foreach (var row in board.Rows) LadderScrapRows.Children.Add(ScrapRow(row));
                    if (ChasterLadder.OwnRowBelow(board) is { } mine)
                    {
                        // the gap: a torn-off dotted ink line, then my own row
                        LadderScrapRows.Children.Add(new TextBlock
                        {
                            Text = "\u00B7 \u00B7 \u00B7", FontSize = 11, Foreground = ScrapInkSoft,
                            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1),
                        });
                        LadderScrapRows.Children.Add(ScrapRow(mine));
                    }
                }
                TxtLadderScrapState.Text = quiet ?? "";
                TxtLadderScrapState.Visibility = quiet == null ? Visibility.Collapsed : Visibility.Visible;

                // A board that moved (a new name, a new rank, more time) nudges the scrap on its pin.
                var sig = board == null ? null : string.Join("|", board.Rows.Concat(board.You is { } y ? new[] { y } : Array.Empty<LadderRow>())
                    .Select(r => $"{r.Rank}:{r.Name}:{r.AddedSeconds}:{r.You}"));
                if (_scrapSignature != null && sig != null && sig != _scrapSignature) ScrapSwing(ScrapTiltDegrees + 2.2, 340);
                if (sig != null) _scrapSignature = sig;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder scrap"); }
        }

        private static FrameworkElement ScrapRow(LadderRow row)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var rank = new TextBlock
            {
                Text = row.Rank.ToString(), FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 11,
                Foreground = ScrapInkSoft, VerticalAlignment = VerticalAlignment.Center,
            };
            var name = new TextBlock
            {
                Text = row.Name, FontSize = 12, Foreground = ScrapInk, TextTrimming = TextTrimming.CharacterEllipsis,
                FontStyle = row.Named ? FontStyles.Normal : FontStyles.Italic,
                FontWeight = row.You ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            };
            var time = new TextBlock
            {
                Text = ChasterLadder.FormatClock(row.AddedSeconds), FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 11, FontWeight = FontWeights.Bold, Foreground = ScrapTimeInk, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(time, 2);
            grid.Children.Add(rank);
            grid.Children.Add(name);
            grid.Children.Add(time);
            // my own row: a pink highlighter swipe, a touch crooked
            return new Border
            {
                Child = grid, Padding = new Thickness(3, 1, 3, 1), CornerRadius = new CornerRadius(2),
                Background = row.You ? ScrapMarker : Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = row.You ? new RotateTransform(-0.8) : Transform.Identity,
            };
        }

        // ============================== motion ==============================

        /// <summary>Page open: the scrap drops onto its pin and settles.</summary>
        private void ScrapSettle()
        {
            if (!MotionFx.AllowTransitions)
            {
                LadderScrapTilt.BeginAnimation(RotateTransform.AngleProperty, null);
                LadderScrapTilt.Angle = ScrapTiltDegrees;
                LadderScrapDrop.BeginAnimation(TranslateTransform.YProperty, null);
                LadderScrapDrop.Y = 0;
                return;
            }
            try
            {
                LadderScrapDrop.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
                LadderScrapTilt.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(ScrapTiltDegrees + 6, ScrapTiltDegrees, TimeSpan.FromMilliseconds(700)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4, EasingMode = EasingMode.EaseOut } });
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster scrap settle"); }
        }

        private void LadderScrap_MouseEnter(object sender, MouseEventArgs e) => ScrapSwing(ScrapTiltDegrees + 0.9, 300);

        private void ScrapSwing(double to, int ms)
        {
            if (MotionFx.Level != MotionLevel.Full) return;
            try
            {
                var swing = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(ScrapTiltDegrees, KeyTime.FromPercent(0)));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromPercent(0.45), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(ScrapTiltDegrees, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));
                swing.Completed += (_, _) =>
                {
                    try { LadderScrapTilt.BeginAnimation(RotateTransform.AngleProperty, null); LadderScrapTilt.Angle = ScrapTiltDegrees; }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                };
                LadderScrapTilt.BeginAnimation(RotateTransform.AngleProperty, swing);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster scrap swing"); }
        }

        // ============================== the opt-in ==============================

        private void ChkLadderName_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            var show = ChkLadderName.IsChecked == true;
            settings.ChasterLadderShowName = show;
            App.Settings?.Save();
            _ = SendLadderNameAsync(show);
        }

        // Best effort: if the server misses it, the next board read sends it again.
        private async Task SendLadderNameAsync(bool show)
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            try
            {
                if (await Task.Run(() => chaster.SetLadderShowNameAsync(show))) await FetchScrapAsync();
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder show name"); }
        }
    }
}
