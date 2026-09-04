using System;
using Avalonia;
using Avalonia.Controls;
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

        // ponytail: the classic AI quiz, ConditioningControlPanel/MainWindow/MainWindow.Lab.cs.
        // QuizWindow is NOT unported, as this note used to say - CCP.Avalonia/Views/Windows/
        // QuizWindow.axaml.cs keeps its own score, sounds and companion hand-off, and the AI half
        // is CoreAi and does answer. What is left is MainWindow.Lab.cs's own preamble around it
        // (the tier door and the question build). Moot either way: the button is IsVisible="False"
        // in the markup on both heads, so nothing can reach this today.
        private void BtnStartQuiz_Click(object? sender, RoutedEventArgs e) { }

        // ponytail: needs ConditioningControlPanel/Services/Quiz/PopQuizService.cs, which shows a
        // WPF PopQuizWindow. Head-side by construction and unported; not a settings write, so
        // CoreSettings is no help here.
        private void BtnTestPopQuiz_Click(object? sender, RoutedEventArgs e) { }

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
