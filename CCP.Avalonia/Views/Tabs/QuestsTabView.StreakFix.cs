using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// "Fix day", ported from WPF MainWindow.QuestsTab.cs: the button's state under the calendar
    /// (RefreshStreakCalendar :737-763), BtnFixStreak_Click / ExitStreakFixMode (fix mode: missed days wear a
    /// pulsing ring that takes the click) and StreakFixDay_Click (confirm, the server spend, the local apply).
    /// The ring pulses on the tab's ambient clock (WPF: a Forever DoubleAnimation 1.0 to 0.3 over 600 ms,
    /// auto-reversed); with ambient loops off it rests at full.
    /// </summary>
    public partial class QuestsTabView
    {
        private static readonly IBrush FixRed = new SolidColorBrush(Color.Parse("#FF5252"));
        private static readonly IBrush FixGreen = new SolidColorBrush(Color.Parse("#00E676"));
        private const double FixPulseSeconds = 0.6;

        private readonly List<Control> _fixRings = new();
        private bool _isStreakFixMode, _streakFixInFlight;
        private IDisposable? _fixStatusTimer;

        /// <summary>Test seams: the server spend and the Yes/No question.</summary>
        internal Func<DateTime, Task<(bool success, string? error, int? credits)>> SpendStreakFix = day => new StreakFix().UseAsync(day);
        internal Func<Window?, string, string, Task<bool>> AskStreakFix = (owner, title, message) =>
            owner == null ? Task.FromResult(false) : Dialogs.MessageDialog.ConfirmAsync(owner, title, message);

        internal bool IsStreakFixMode => _isStreakFixMode;
        internal IReadOnlyList<Control> StreakFixRings => _fixRings;

        /// <summary>RefreshStreakCalendar's tail. The button shows for EVERYONE: fixes are a charge balance every
        /// account earns, owning oopsie_insurance only buys the automatic spend.</summary>
        private void PaintFixButton(bool hasMissedDays)
        {
            var s = CoreSettings.Current;
            int charges = s.StreakFixCharges;
            // Charges are granted and spent server-side only: a signed-out user can neither earn nor use one.
            bool signedIn = !string.IsNullOrEmpty(s.UnifiedId);

            BtnFixStreak.IsVisible = true;
            // Cancel stays clickable even at 0 charges, otherwise fix mode has no exit.
            BtnFixStreak.IsEnabled = _isStreakFixMode || (signedIn && charges > 0 && hasMissedDays);
            var caption = _isStreakFixMode ? Loc.Get("btn_cancel_2") : Loc.GetF("btn_fix_day_with_count", charges);
            if (BtnFixStreak.Content is TextBlock word) word.Text = caption;
            else BtnFixStreak.Content = new TextBlock { Text = caption };
            ToolTip.SetTip(BtnFixStreak,
                !signedIn ? Loc.Get("tooltip_streak_fixes_need_account")
                : charges <= 0 ? Loc.Get("tooltip_no_streak_fixes_left")
                : !hasMissedDays ? Loc.Get("tooltip_no_missed_days_your_streak_is_perfect")
                : Loc.GetF("tooltip_use_streak_fix", charges));
        }

        /// <summary>The pulsing ring over a missed day in fix mode (WPF highlight Rectangle, node + 4, stroke 2).</summary>
        private void AddFixRing(DateTime day, double centerX, double centerY, double nodeSize, IBrush accent)
        {
            double size = nodeSize + 4;
            var ring = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                Background = Brushes.Transparent, BorderBrush = accent, BorderThickness = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand), Tag = day.Date, ZIndex = 3,
            };
            ring.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(ring).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                _ = FixDayAsync(day.Date);
            };
            Canvas.SetLeft(ring, centerX - size / 2);
            Canvas.SetTop(ring, centerY - size / 2);
            StreakCalendarCanvas.Children.Add(ring);
            _fixRings.Add(ring);
        }

        /// <summary>One ambient tick: 1.0 to 0.3 and back, 600 ms a leg. Clamped; rests at 1 with loops off.</summary>
        private void StepFixPulse(double t)
        {
            if (_fixRings.Count == 0) return;
            double u = t % (2 * FixPulseSeconds) / FixPulseSeconds;
            double opacity = Env.AllowAmbientLoops ? Math.Clamp(1.0 - 0.7 * (u <= 1 ? u : 2 - u), 0, 1) : 1;
            foreach (var ring in _fixRings) ring.Opacity = opacity;
        }

        private void ShowFixStatus(string text, IBrush? colour = null)
        {
            _fixStatusTimer?.Dispose();
            _fixStatusTimer = null;
            TxtFixStreakStatus.Text = text;
            if (colour != null) TxtFixStreakStatus.Foreground = colour;
            else if (this.TryFindResource("PinkBrush", out var pink) && pink is IBrush brush) TxtFixStreakStatus.Foreground = brush;
            TxtFixStreakStatus.IsVisible = true;
        }

        /// <summary>BtnFixStreak_Click.</summary>
        private void FixStreak()
        {
            if (_isStreakFixMode) { ExitStreakFixMode(); return; }
            var s = CoreSettings.Current;
            if (s.StreakFixCharges < 1) { ShowFixStatus(Loc.Get("label_no_streak_fixes_left")); return; }
            if (!StreakFix.HasMissedDays(App.Quests?.Progress, DateTime.Today))
            {
                ShowFixStatus(Loc.Get("label_no_broken_streak_you_re_doing_great_sweetie"));
                return;
            }
            _isStreakFixMode = true;
            ShowFixStatus(Loc.Get("label_click_a_missed_day_to_fix_it_free"));
            PaintStreakCalendar();
        }

        private void ExitStreakFixMode()
        {
            _isStreakFixMode = false;
            _fixStatusTimer?.Dispose();
            _fixStatusTimer = null;
            TxtFixStreakStatus.IsVisible = false;
            TxtFixStreakStatus.Text = "";
            PaintStreakCalendar();
        }

        /// <summary>StreakFixDay_Click. One spend at a time: a double click, or day B while day A still waits on
        /// the server, starts nothing.</summary>
        internal async Task FixDayAsync(DateTime fixDate)
        {
            if (_streakFixInFlight) return;
            _streakFixInFlight = true;
            try
            {
                var s = CoreSettings.Current;
                // Re-checked here, not only on entering fix mode: the balance can move in between.
                if (s.StreakFixCharges < 1) { ShowFixStatus(Loc.Get("label_no_streak_fixes_left")); return; }

                if (!await AskStreakFix(TopLevel.GetTopLevel(this) as Window, Loc.Get("label_fix_day_confirm_title"),
                        Loc.GetF("label_fix_day_confirm_body", fixDate.ToString("MMMM d"), s.StreakFixCharges)))
                    return;

                if (string.IsNullOrEmpty(s.UnifiedId))
                {
                    ShowFixStatus(Loc.Get("label_oopsie_insurance_requires_a_cloud_account_ple"), FixRed);
                    return;
                }

                ShowFixStatus(Loc.Get("label_processing"));
                var (success, error, credits) = await SpendStreakFix(fixDate);
                if (!success)
                {
                    ShowFixStatus($"❌ {error ?? Loc.Get("label_streak_fix_failed")}", FixRed);
                    return;
                }

                // The spend is committed on the server: apply it even if fix mode was cancelled meanwhile.
                StreakFix.ApplySpent(s, App.Quests, fixDate, credits);
                CoreSettings.Save();
                Serilog.Log.Information("Streak fix used on {Date} (server-validated), {Remaining} charge(s) left", fixDate, s.StreakFixCharges);

                bool stillFixing = _isStreakFixMode;
                if (stillFixing)
                {
                    _isStreakFixMode = false;
                    ShowFixStatus(Loc.GetF("label_streak_fixed_success", fixDate.ToString("MMMM d")), FixGreen);
                }
                RefreshQuestUI();   // the stats tile and the button caption, not just the calendar
                if (!stillFixing) return;

                // Auto-hide after 3 seconds, back to the accent colour.
                _fixStatusTimer = DispatcherTimer.RunOnce(() =>
                {
                    _fixStatusTimer = null;
                    if (_isStreakFixMode) return;
                    TxtFixStreakStatus.IsVisible = false;
                    if (this.TryFindResource("PinkBrush", out var pink) && pink is IBrush brush) TxtFixStreakStatus.Foreground = brush;
                }, TimeSpan.FromSeconds(3));
            }
            catch (Exception ex) { Serilog.Log.Warning("Streak fix failed: {Error}", ex.Message); }
            finally { _streakFixInFlight = false; }
        }
    }
}
