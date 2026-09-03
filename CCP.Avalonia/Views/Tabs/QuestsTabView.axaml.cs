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
    ///    plus a theme swap on the two buttons; only its trailing RefreshRoadmapUI() needs a
    ///    service, and that is the stubbed part.
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
            // ponytail: needs RoadmapService (App.Roadmap), wired when it moves to Core. The panel
            // shows its authored placeholders until then.
        }

        private void OnTrackClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            var tag = (sender as Button)?.Tag as string;
            BtnTrack1.Theme = TabTheme(tag == "EmptyDoll" ? "TabButtonActive" : "TabButton");
            BtnTrack2.Theme = TabTheme(tag == "ObedientPuppet" ? "TabButtonActive" : "TabButton");
            BtnTrack3.Theme = TabTheme(tag == "SluttyBlowdoll" ? "TabButtonActive" : "TabButton");
            // ponytail: needs RoadmapService (App.Roadmap) to repaint the nodes for the new track,
            // wired when it moves to Core.
        }

        private ControlTheme? TabTheme(string key) =>
            Resources.TryGetResource(key, null, out var value) ? value as ControlTheme : null;

        // ---- STUBS ----------------------------------------------------------------

        // ponytail: needs QuestService, wired when it moves to Core.
        private void OnDailyCardRerollRequested(object? sender, EventArgs e) { }

        // ponytail: needs QuestService, wired when it moves to Core.
        private void RerollWeekly() { }

        // ponytail: needs QuestStreakService, wired when it moves to Core.
        private void FixStreak() { }

        // ---- STREAK CALENDAR ------------------------------------------------------

        /// <summary>
        /// Placeholder streak strip: seven day pips across the canvas, the first four stamped.
        /// The WPF original paints this from MainWindow on every quest refresh; painting a sample
        /// here keeps the 50px band from rendering as an unexplained blank in the render proof.
        /// ponytail: needs QuestStreakService for the real days, wired when it moves to Core.
        /// </summary>
        private void PaintStreakCalendar()
        {
            StreakCalendarCanvas.Children.Clear();

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
