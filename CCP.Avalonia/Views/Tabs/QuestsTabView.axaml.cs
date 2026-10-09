using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Views.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/QuestsTabView.xaml.cs.
    ///
    /// Every handler in the WPF original is a one-line forward to MainWindow, so almost all of
    /// them become stubs here - the quest, roadmap and streak services all still live in the WPF
    /// head. Two exceptions are genuinely view-only and are ported for real:
    ///
    ///  - the sub-tab swap (MainWindow.Roadmap.cs:37/48) is nothing but a panel IsVisible flip
    ///    plus a theme swap on the two buttons; its trailing RefreshRoadmapUI() is ported in
    ///    QuestsTabView.Roadmap.cs against the head's RoadmapService.
    ///  - the track swap (MainWindow.Roadmap.cs:62) is the same shape.
    ///
    /// Dropped outright:
    ///  - <c>IsVisibleChanged</c>: no Avalonia equivalent, and its only job was to tell
    ///    MainWindow the tab became visible so it could fill the quest bars. The cards seat their
    ///    own bars on SizeChanged (see DailyQuestCard), so nothing here needs it yet.
    ///  - <c>HorizontalScrollViewer_PreviewMouseWheel</c>: a WPF tunneling workaround for a
    ///    horizontal-only ScrollViewer swallowing vertical wheel. Avalonia's PointerWheelChanged
    ///    bubbles, and a ScrollViewer with VerticalScrollBarVisibility="Disabled" leaves the
    ///    vertical delta to its parent, so the dead zone the handler existed to fix is not there.
    /// </summary>
    public partial class QuestsTabView : UserControl
    {
        /// <summary>The three daily seats, in column order. The seats themselves are named in
        /// the XAML; this is just the group the reroll forward iterates.</summary>
        private readonly DailyQuestCard[] _dailyCards;

        public QuestsTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and Load leaves every one of them permanently null - a silent no-op
            // that compiles, renders and reviews clean.
            InitializeComponent();

            _dailyCards = new[] { DailyCard0, DailyCard1, DailyCard2 };
            RoadmapScrollContainer.TemplateApplied += (_, _) => _rescanParticles = true; // QuestsTabView.Fx.cs

            BtnQuestSubDaily.Click += (_, _) => ShowDailyWeekly();
            BtnQuestSubRoadmap.Click += (_, _) => ShowRoadmap();
            BtnTrack1.Click += OnTrackClick;
            BtnTrack2.Click += OnTrackClick;
            BtnTrack3.Click += OnTrackClick;
            BtnRerollWeekly.Click += (_, _) => RerollWeekly();
            BtnFixStreak.Click += (_, _) => FixStreak();

            // The three daily seats each own a reroll button; the tab just forwards which seat was
            // pressed. The shell spends the reroll - no quest state is touched down here.
            foreach (var card in _dailyCards)
                card.RerollRequested += OnDailyCardRerollRequested;

            StreakCalendarCanvas.SizeChanged += (_, _) => PaintStreakCalendar();
        }

        // ---- SUB-TABS (view-only, ported for real) --------------------------------

        private void ShowDailyWeekly()
        {
            DailyWeeklyPanel.IsVisible = true;
            RoadmapPanel.IsVisible = false;
            BtnQuestSubDaily.Theme = TabTheme("TabButtonActive");
            BtnQuestSubRoadmap.Theme = TabTheme("TabButton");
        }

        private void ShowRoadmap()
        {
            DailyWeeklyPanel.IsVisible = false;
            RoadmapPanel.IsVisible = true;
            BtnQuestSubDaily.Theme = TabTheme("TabButton");
            BtnQuestSubRoadmap.Theme = TabTheme("TabButtonActive");
            // The view half landed: QuestsTabView.Roadmap.cs paints the header, the lock overlay,
            // the badge and a node per step off the real RoadmapService in Core, exactly where
            // WPF's BtnQuestSubRoadmap_Click calls RefreshRoadmapUI.
            RefreshRoadmapUI();
        }

        private void OnTrackClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Verbatim MainWindow.Roadmap.cs:62 - parse the Tag, and do NOTHING at all when it
            // does not parse. The restyle sits inside that guard for the same reason it does in
            // WPF: an unrecognised Tag must not leave all three buttons drawn inactive over the
            // nodes of a track nobody selected.
            if ((sender as Button)?.Tag is not string tag
                || !Enum.TryParse<Models.RoadmapTrack>(tag, out var track)) return;

            _currentRoadmapTrack = track;

            BtnTrack1.Theme = TabTheme(track == Models.RoadmapTrack.EmptyDoll ? "TabButtonActive" : "TabButton");
            BtnTrack2.Theme = TabTheme(track == Models.RoadmapTrack.ObedientPuppet ? "TabButtonActive" : "TabButton");
            BtnTrack3.Theme = TabTheme(track == Models.RoadmapTrack.SluttyBlowdoll ? "TabButtonActive" : "TabButton");

            RefreshRoadmapUI();
        }

        private ControlTheme? TabTheme(string key) =>
            Resources.TryGetResource(key, null, out var value) ? value as ControlTheme : null;

        // ---- REROLLS (MainWindow.QuestsTab.cs:41/:59) ------------------------------

        private void OnDailyCardRerollRequested(object? sender, EventArgs e)
        {
            var quests = App.Quests;
            if (quests == null || sender is not DailyQuestCard card) return;
            if (quests.RerollDailyQuest(card.Slot)) { RefreshQuestUI(); return; }
            // Out of rerolls is the only refusal worth a dialog; a finished seat stays as it was.
            if (quests.GetRemainingDailyRerolls() > 0) return;
            ShowRerollLimit(CoreEntitlement.HasPremium ? "quest_reroll_daily_none_patron" : "quest_reroll_daily_none_free");
        }

        private void RerollWeekly()
        {
            var quests = App.Quests;
            if (quests == null) return;
            if (quests.RerollWeeklyQuest()) RefreshQuestUI();
            else ShowRerollLimit(CoreEntitlement.HasPremium ? "quest_reroll_weekly_none_patron" : "quest_reroll_weekly_none_free");
        }

        private void ShowRerollLimit(string key)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                _ = Dialogs.MessageDialog.ShowAsync(owner, ConditioningControlPanel.Localization.Loc.Get("quest_reroll_limit_title"), ConditioningControlPanel.Localization.Loc.Get(key));
        }

        // ponytail: there is NO QuestStreakService - the name in the note this replaces does not
        // exist anywhere in the repo. The streak fix is
        // ConditioningControlPanel/Services/Progression/SkillTreeService.cs:423 (UseStreakShield,
        // for the charge count) plus the signed-in check, painted by
        // ConditioningControlPanel/MainWindow/MainWindow.QuestsTab.cs:748. The dates it writes,
        // AppSettings.StreakShieldUsedDates, are already in Core.
        private void FixStreak() { }

        // ---- STREAK CALENDAR ------------------------------------------------------

        /// <summary>
        /// WPF RefreshStreakCalendar (MainWindow.QuestsTab.cs:563): the current month, one node per
        /// day, accent-filled when a daily quest was completed, gold ring on today, 🛡 on shielded
        /// days, joined by an accent line between consecutive completed days. Without a service
        /// (headless render) it paints a seven-pip sample so the band is not a blank.
        /// ponytail: no streak-fix mode (pulsing missed days) - SpendStreakFix is a server round trip.
        /// </summary>
        private void PaintStreakCalendar()
        {
            StreakCalendarCanvas.Children.Clear();
            if (App.Quests is { } quests) { PaintMonth(quests); return; }

            const int days = 7, stamped = 4, size = 26;
            double width = StreakCalendarCanvas.Bounds.Width;
            if (width <= 0) return;

            double step = Math.Min(size + 14, width / days);
            double left = (width - (step * days - (step - size))) / 2;

            for (int i = 0; i < days; i++)
            {
                bool done = i < stamped;
                var pip = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(done ? Color.FromRgb(0xFF, 0xD7, 0x00)
                                                    : Color.FromRgb(0x3D, 0x3D, 0x60)),
                };
                Canvas.SetLeft(pip, left + i * step);
                Canvas.SetTop(pip, 12);
                StreakCalendarCanvas.Children.Add(pip);
            }
        }
    }
}
