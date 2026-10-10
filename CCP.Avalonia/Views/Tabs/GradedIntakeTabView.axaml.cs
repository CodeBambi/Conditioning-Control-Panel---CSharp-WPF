using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// "Graded Intake" — the banded-descent intake, promoted out of the Lab into its own
    /// Exclusives page. Nothing about the feature changed in the move: the same
    /// controls, handlers and settings are simply hosted here instead of on the Lab card.
    /// Pop Quiz rode along because it lived inside the same card, but it is NOT premium and
    /// deliberately sits outside <c>GradedIntakeGate</c>.
    ///
    /// The gate is no longer a plain t1 lock: free accounts get one run a week, so
    /// <c>GradedIntakeGate</c> (with its swappable copy) and <c>GradedIntakePassBanner</c> are
    /// painted together from <see cref="RefreshGradedIntakeGate"/> (WPF MainWindow.Lab.cs:343),
    /// which is the only thing that should ever touch their visibility. It lives on the view here:
    /// it runs on attach (WPF: every Exclusives navigation) and on the pass's PassStateChanged,
    /// (WPF Lab.cs:435). That event fires on a spend, on either provider's TierChanged (hooked in
    /// AccountSeed.Seed) and on sign-in/out (MainShellWindow.UpdateQuickLoginUI, WPF Patreon.cs:294).
    ///
    /// On WPF every handler below is a one-line hop to the identically named <c>MainWindow</c>
    /// method. THE POP-QUIZ PAIR IS NOT A HOP ANY MORE: <c>PopQuizEnabled</c> and
    /// <c>PopQuizFrequency</c> are both in Core, so the two live editors are written here against
    /// <see cref="CoreSettings"/> instead of forwarding to a shell that does not exist
    /// (MainShellWindow.Lab.cs says the same thing from the other end). They are seeded from
    /// settings on attach with the <c>_isLoading</c> guard, because Avalonia raises
    /// IsCheckedChanged/ValueChanged on a programmatic set exactly as WPF raised Checked, and a
    /// seed without it saves the defaults over the user's file.
    /// </summary>
    public partial class GradedIntakeTabView : UserControl
    {
        /// <summary>Set while the two editors below are being seeded from settings.</summary>
        private bool _isLoading = true;

        public GradedIntakeTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and the seed below reads three of them.
            InitializeComponent();
            Helpers.ModArt.BindFeaturePlates(this, "features/lab_quiz_hero.png", GradedIntakeHeroArtHost, null);

            // The frequency readout is written from code (WPF: "{val}/session hr"), so it has to be
            // rewritten when the language changes or the {loc:Str} binding still living under that
            // local value would put the seeded "2/session hr" back in the old language.

            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            RefreshPastQuizzes();   // WPF refreshes on tab navigation (MainWindow.TabNavigation.cs:548)
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            App.IntakePass.PassStateChanged += OnIntakePassStateChanged;
            SyncFromSettings();
            RefreshGradedIntakeGate();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            App.IntakePass.PassStateChanged -= OnIntakePassStateChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            ShowFrequency((int)Math.Round(SliderPopQuizFrequency.Value));
            RefreshGradedIntakeGate();
        });

        // A cloud restore or a factory reset swaps the settings instance; repaint from the new one.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        // WPF Lab.cs:444: the pass is spent (or entitlement lands) off the UI thread.
        private void OnIntakePassStateChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(RefreshGradedIntakeGate);

        /// <summary>
        /// WPF MainWindow.Lab.cs:343. Premium and Available open the page (Available also shows the
        /// pass banner); Spent and NeedsLogin show the gate with their own copy and disable the
        /// launch zone behind it. Pop Quiz sits outside the gated Border and stays reachable.
        /// </summary>
        internal void RefreshGradedIntakeGate()
        {
            try
            {
                var state = App.IntakePass.State;
                var open = state == IntakePassState.Premium || state == IntakePassState.Available;

                GradedIntakeGate.IsVisible = !open;
                PremiumGateFx.Attach(GradedIntakeGate);   // WPF MainWindow.Lab.cs:371
                GradedIntakeGatedContent.IsEnabled = open;
                GradedIntakePassBanner.IsVisible = state == IntakePassState.Available;

                if (state == IntakePassState.NeedsLogin)
                {
                    SetGateCopy(Loc.Get("intake_gate_login_headline"), Loc.Get("intake_gate_login_body"),
                        Loc.Get("intake_gate_login_cta"));
                }
                else if (state == IntakePassState.Spent)
                {
                    SetGateCopy(Loc.Get("intake_gate_spent_headline"), SpentBody(),
                        Loc.Get("intake_gate_spent_cta"));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("RefreshGradedIntakeGate failed: {E}", ex.Message);
            }
        }

        /// <summary>Two keys rather than one with a {0}: DaysUntilNextPass floors at 1 (WPF Lab.cs:391).</summary>
        private static string SpentBody()
        {
            var days = IntakePassService.DaysUntilNextPass;
            return days == 1 ? Loc.Get("intake_gate_spent_body_one_day") : Loc.GetF("intake_gate_spent_body", days);
        }

        private void SetGateCopy(string headline, string body, string cta)
        {
            TxtGradedIntakeGateHeadline.Text = headline;
            TxtGradedIntakeGateBody.Text = body;
            if (BtnGradedIntakeGateUnlock.Content is TextBlock label) label.Text = cta;
        }

        /// <summary>
        /// The pop-quiz half of WPF's <c>MainWindow.LoadSettingsToUI</c> (MainWindow.xaml.cs:3552),
        /// which is where these two controls were seeded from.
        /// </summary>
        internal void SyncFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkPopQuizEnabled.IsChecked = s.PopQuizEnabled;
                SliderPopQuizFrequency.Value = s.PopQuizFrequency;
                ShowFrequency(s.PopQuizFrequency);
            }
            finally { _isLoading = false; }
        }

        /// <summary>WPF writes the English literal; <c>label_0_session_hr</c> is its formatted twin.</summary>
        private void ShowFrequency(int perHour) =>
            TxtPopQuizFrequency.Text = Loc.GetF("label_0_session_hr", perHour);

        /// <summary>WPF MainWindow.Lab.cs:148: the pass gate and the AI gate in front of the launch.</summary>
        internal async void BtnStartIntake_Click(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            var pass = App.IntakePass;
            if (!pass.CanStartIntake)
            {
                if (pass.State == IntakePassState.NeedsLogin)
                {
                    if (owner != null)
                        await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_login_required"),
                            Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"));
                }
                else
                {
                    App.Notifications.Show(SpentBody(), Helpers.NotificationType.Warning,
                        TimeSpan.FromSeconds(8), Loc.Get("intake_gate_spent_cta"),
                        () => (owner as Windows.MainShellWindow)?.OpenAppSettingsSection("account"));
                }
                return;
            }

            if (!CoreAi.IsAvailable)
            {
                if (owner != null)
                    await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_login_required"),
                        Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"));
                return;
            }

            // WPF Lab.cs:195: a first-ever run does not duck the control panel (it reads as a crash).
            OpenIntake(owner, duckMain: IntakePunchCardState.ReadEverCompletedIntake(CorePaths.UserData));
        }

        /// <summary>WPF IntakeHostService.Launch: one live run at a time, focused if already open;
        /// built in the remembered window mode unless this is a recovery relaunch.</summary>
        internal static Windows.IntakeHostWindow OpenIntake(Window? owner, bool duckMain, bool recovery = false)
        {
            var shell = owner as Windows.MainShellWindow;
            // The window registry, not lifetime.Windows (null under tests); a closing window has already left it.
            if (Windows.IntakeHostWindow.Snapshot().FirstOrDefault() is { } live)
            {
                live.Activate();
                return live;
            }
            var intake = new Windows.IntakeHostWindow { DuckTarget = duckMain ? owner : null };
            if (!recovery && CoreSettings.Current.IntakeFullscreen) intake.WindowState = WindowState.FullScreen;
            intake.Relaunch = () => OpenIntake(owner, duckMain, recovery: true);
            intake.Drafted += (session, path) => OnSessionDrafted(shell, session, path);
            intake.Load(Platform.WebAssetServer.Shared);
            intake.Show();
            return intake;
        }

        /// <summary>WPF IntakeHostService.OnQuizResult after the draft: Sessions list refresh, then the
        /// "Run it now" toast. Separate try blocks, as there: a refresh failure never hides the toast.</summary>
        internal static void OnSessionDrafted(Windows.MainShellWindow? shell, Session session, string path)
        {
            var presets = shell?.Named<PresetsTabView>("PresetsTab");
            try { presets?.RegisterExternallySavedSession(session, path); }
            catch (Exception ex) { Log.Warning(ex, "IntakeHost: drafted session saved but the Sessions list refresh failed"); }

            var sessionId = session.Id;
            App.Notifications.Show(
                $"Session drafted: \"{session.Name}\" - run it to stamp your punch card.",
                Helpers.NotificationType.Success, TimeSpan.FromSeconds(12), "Run it now",
                () =>
                {
                    if (shell == null || presets?.RevealSession(sessionId) is not { } found) return;
                    shell.ShowTab("presets");
                    shell.BtnStartSession_Click(found);
                });
        }

        /// <summary>WPF MainWindow.Lab.cs:116. The button is IsVisible="False" on both heads ("pending
        /// removal"), so no user reaches this; kept wired so unhiding it is one attribute.</summary>
        internal async void BtnStartQuiz_Click(object? sender, RoutedEventArgs e)
        {
            var lifetime = Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.Windows.OfType<Windows.QuizWindow>().FirstOrDefault() is { } existing)
            {
                existing.Activate();
                return;
            }

            if (!CoreAi.IsAvailable)
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                    await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_login_required"),
                        Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"));
                return;
            }

            var quizWindow = new Windows.QuizWindow(ChkQuizFullscreen.IsChecked == true, ChkQuizDrone.IsChecked == true);
            quizWindow.Closed += (_, _) => RefreshPastQuizzes();
            quizWindow.Show();
        }

        /// <summary>WPF MainWindow.Lab.cs:468. Bails while BtnStartQuiz is hidden (the classic quiz is
        /// pending removal on both heads); unhide it and the trend rows + report rows light up.</summary>
        internal void RefreshPastQuizzes()
        {
            if (!BtnStartQuiz.IsVisible) return;
            try
            {
                var history = QuizStore.LoadHistory();
                PastQuizzesList.Children.Clear();
                TxtPastQuizzesHeader.IsVisible = PastQuizzesPanel.IsVisible = history.Count > 0;
                if (history.Count == 0) return;

                // Trend per TrendKey, not the enum: custom categories collapse to Sissy (#518/#521).
                foreach (var cat in history.Select(QuizStore.TrendKey).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var trend = QuizStore.GetScoreTrend(history, cat);
                    if (trend == null) continue;
                    var latest = history.FirstOrDefault(h =>
                        string.Equals(QuizStore.TrendKey(h), cat, StringComparison.OrdinalIgnoreCase));
                    var archetype = "";
                    if (latest != null)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(latest.ProfileText, @"You are a (.+?)\.");
                        if (m.Success) archetype = m.Groups[1].Value;
                    }
                    var arrow = trend.Direction switch
                    {
                        TrendDirection.Up => "\u2191",
                        TrendDirection.Down => "\u2193",
                        TrendDirection.Flat => "\u2192",
                        _ => ""
                    };
                    var catDisplay = latest != null ? QuizStore.DisplayName(latest) : cat;
                    var label = trend.Direction == TrendDirection.FirstQuiz
                        ? $"{catDisplay}: {trend.LatestPercent}%"
                        : $"{catDisplay}: {trend.LatestPercent}% {arrow}{Math.Abs(trend.DeltaPercent)}%";
                    if (!string.IsNullOrEmpty(archetype)) label += $" · {archetype}";
                    PastQuizzesList.Children.Add(new TextBlock
                    {
                        Text = label,
                        Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xFF, 0x69, 0xB4)),
                        FontSize = 11,
                        FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
                        Margin = new Thickness(8, 3, 8, 3)
                    });
                }

                foreach (var entry in history)
                {
                    var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;
                    // A Button (not WPF's mouse-only Border) so the row is keyboard-reachable (P17).
                    var row = new Button
                    {
                        Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                        Padding = new Thickness(8, 5, 8, 5),
                        Background = global::Avalonia.Media.Brushes.Transparent,
                        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                        Content = new TextBlock
                        {
                            Text = $"{entry.TakenAt:MMM d}  ·  {QuizStore.DisplayName(entry)}  ·  {entry.TotalScore}/{entry.MaxScore} ({pct}%)",
                            Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0xA0, 0xA0, 0xB8)),
                            FontSize = 11.5
                        }
                    };
                    var captured = entry;
                    row.Click += (_, _) => OpenReport(captured);
                    PastQuizzesList.Children.Add(row);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GradedIntakeTabView: Failed to refresh past quizzes");
            }
        }

        /// <summary>WPF MainWindow.Lab.cs:560: one report at a time, owned by the shell.</summary>
        internal Windows.QuizReportWindow OpenReport(QuizHistoryEntry entry)
        {
            s_report?.Close();   // this is its only opener, so one static is "every open report"
            var report = s_report = new Windows.QuizReportWindow(entry);
            if (TopLevel.GetTopLevel(this) is Window owner) report.Show(owner); else report.Show();
            return report;
        }
        private static Windows.QuizReportWindow? s_report;

        // WPF MainWindow.Lab.cs:600 → PopQuizService.TestPopQuiz.
        private void BtnTestPopQuiz_Click(object? sender, RoutedEventArgs e) =>
            Windows.PopQuizHost.Instance.Scheduler.Show(isTest: true);

        private void ChkPopQuizEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var want = ChkPopQuizEnabled.IsChecked == true;
            if (CoreSettings.Current.PopQuizEnabled == want) return;
            CoreSettings.Current.PopQuizEnabled = want;
            CoreSettings.Save();
            Log.Information("Pop quiz set to {Enabled}", want);
        }

        private void SliderPopQuizFrequency_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            var val = (int)Math.Round(e.NewValue);
            // The readout follows the thumb even while seeding - it is a view of the slider, not a
            // second setting, and WPF repaints it from LoadSettingsToUI for the same reason.
            ShowFrequency(val);

            if (_isLoading) return;
            if (CoreSettings.Current.PopQuizFrequency == val) return;
            CoreSettings.Current.PopQuizFrequency = val;
            CoreSettings.Save();
        }

        /// <summary>Gate CTA. Serves both closed states: the shared App Info &amp; Data popup is
        /// where signing in lives as well as where the tiers are, so NeedsLogin and Spent can share
        /// one destination even though their button labels differ. WPF's <c>BtnGateUnlock_Click</c>
        /// is <c>ShowAppInfoPopup()</c> → <c>ShowTab("appsettings")</c> + FocusSection("account"),
        /// which is exactly <see cref="Windows.MainShellWindow.OpenAppSettingsSection"/> here.
        ///
        /// <para>The gate itself is left hidden by the markup until RefreshGradedIntakeGate exists
        /// (see the class note), so today this is reachable only from a preview.</para></summary>
        private void BtnGI_GateUnlock_Click(object? sender, RoutedEventArgs e)
            => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OpenAppSettingsSection("account");
    }
}
