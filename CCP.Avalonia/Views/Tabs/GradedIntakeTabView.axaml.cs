using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Services;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
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
    /// painted together from <c>MainWindow.RefreshGradedIntakeGate</c>, which is the only thing
    /// that should ever touch their visibility. That host does not exist on this head, so both
    /// keep the authored starting state from the markup (hidden), exactly as WPF does before the
    /// host's first refresh pass.
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
            LocalizationManager.Instance.LanguageChanged += (_, _) =>
                Dispatcher.UIThread.Post(() => ShowFrequency((int)Math.Round(SliderPopQuizFrequency.Value)));

            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            RefreshPastQuizzes();   // WPF refreshes on tab navigation (MainWindow.TabNavigation.cs:548)
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the settings instance; repaint from the new one.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

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

        // ponytail: the web view is NOT the blocker - this head ships Views/Controls/WebHost, a
        // real Avalonia.Controls.WebView with a navigation gate and InvokeScriptAsync. What is
        // missing is everything around it: Services/Quiz/IntakeHostService (the window and the
        // page protocol), Services/IntakePassService (which spends the pass) and CoreAi for the
        // tier gate in front of both - see ConditioningControlPanel/MainWindow/MainWindow.Lab.cs
        // :148. Nothing here can start a run, and a button that opens nothing is better than one
        // that pretends the pass was spent.
        private void BtnStartIntake_Click(object? sender, RoutedEventArgs e) { }

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
                    await Dialogs.MessageDialog.ShowAsync(owner, "Login Required",
                        Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"));
                return;
            }

            var quizWindow = new Windows.QuizWindow(ChkQuizFullscreen.IsChecked == true, ChkQuizDrone.IsChecked == true);
            quizWindow.Closed += (_, _) => RefreshPastQuizzes();
            quizWindow.Show();
        }

        /// <summary>WPF MainWindow.Lab.cs:468, including its early return while BtnStartQuiz is hidden.</summary>
        internal void RefreshPastQuizzes()
        {
            try
            {
                if (!BtnStartQuiz.IsVisible) return;

                var history = QuizStore.LoadHistory();
                PastQuizzesList.Children.Clear();
                TxtPastQuizzesHeader.IsVisible = PastQuizzesPanel.IsVisible = history.Count > 0;
                if (history.Count == 0) return;

                foreach (var cat in history.Select(QuizStore.TrendKey).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var trend = QuizStore.GetScoreTrend(history, cat);
                    if (trend == null) continue;

                    var latestEntry = history.FirstOrDefault(h =>
                        string.Equals(QuizStore.TrendKey(h), cat, StringComparison.OrdinalIgnoreCase));
                    var archetype = "";
                    if (latestEntry != null)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(latestEntry.ProfileText, @"You are a (.+?)\.");
                        if (match.Success) archetype = match.Groups[1].Value;
                    }

                    var arrow = trend.Direction switch
                    {
                        TrendDirection.Up => "\u2191",
                        TrendDirection.Down => "\u2193",
                        TrendDirection.Flat => "\u2192",
                        _ => ""
                    };
                    var catDisplay = latestEntry != null ? QuizStore.DisplayName(latestEntry) : cat;
                    var trendLabel = trend.Direction == TrendDirection.FirstQuiz
                        ? $"{catDisplay}: {trend.LatestPercent}%"
                        : $"{catDisplay}: {trend.LatestPercent}% {arrow}{Math.Abs(trend.DeltaPercent)}%";
                    if (!string.IsNullOrEmpty(archetype))
                        trendLabel += $" · {archetype}";

                    PastQuizzesList.Children.Add(new TextBlock
                    {
                        Text = trendLabel,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(8, 3, 8, 3)
                    });
                }

                foreach (var entry in history)
                {
                    var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;
                    var row = new Border
                    {
                        Cursor = new Cursor(StandardCursorType.Hand),
                        Padding = new Thickness(8, 5, 8, 5),
                        Background = Brushes.Transparent,
                        Child = new TextBlock
                        {
                            Text = $"{entry.TakenAt:MMM d}  ·  {QuizStore.DisplayName(entry)}  ·  {entry.TotalScore}/{entry.MaxScore} ({pct}%)",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                            FontSize = 11.5
                        }
                    };
                    var captured = entry;
                    row.PointerPressed += (_, _) =>
                    {
                        var lifetime = Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                        foreach (var w in lifetime?.Windows.OfType<Windows.QuizReportWindow>().ToList() ?? new())
                            w.Close();
                        var report = new Windows.QuizReportWindow(captured);
                        if (TopLevel.GetTopLevel(this) is Window owner) report.Show(owner); else report.Show();
                    };
                    row.PointerEntered += (s, _) => { if (s is Border b) b.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)); };
                    row.PointerExited += (s, _) => { if (s is Border b) b.Background = Brushes.Transparent; };
                    PastQuizzesList.Children.Add(row);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MainWindow: Failed to refresh past quizzes");
            }
        }

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
