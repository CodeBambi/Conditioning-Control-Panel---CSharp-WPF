using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
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
        private readonly StackPanel _dailyWeeklyPanel, _roadmapPanel;
        private readonly Button _btnSubDaily, _btnSubRoadmap;
        private readonly Button _btnTrack1, _btnTrack2, _btnTrack3;
        private readonly Button _btnRerollWeekly, _btnFixStreak;
        private readonly Canvas _streakCanvas;
        private readonly DailyQuestCard[] _dailyCards;

        public QuestsTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _dailyWeeklyPanel = this.FindControl<StackPanel>("DailyWeeklyPanel")!;
            _roadmapPanel = this.FindControl<StackPanel>("RoadmapPanel")!;
            _btnSubDaily = this.FindControl<Button>("BtnQuestSubDaily")!;
            _btnSubRoadmap = this.FindControl<Button>("BtnQuestSubRoadmap")!;
            _btnTrack1 = this.FindControl<Button>("BtnTrack1")!;
            _btnTrack2 = this.FindControl<Button>("BtnTrack2")!;
            _btnTrack3 = this.FindControl<Button>("BtnTrack3")!;
            _btnRerollWeekly = this.FindControl<Button>("BtnRerollWeekly")!;
            _btnFixStreak = this.FindControl<Button>("BtnFixStreak")!;
            _streakCanvas = this.FindControl<Canvas>("StreakCalendarCanvas")!;

            _dailyCards = new[]
            {
                this.FindControl<DailyQuestCard>("DailyCard0")!,
                this.FindControl<DailyQuestCard>("DailyCard1")!,
                this.FindControl<DailyQuestCard>("DailyCard2")!,
            };

            _btnSubDaily.Click += (_, _) => ShowDailyWeekly();
            _btnSubRoadmap.Click += (_, _) => ShowRoadmap();
            _btnTrack1.Click += OnTrackClick;
            _btnTrack2.Click += OnTrackClick;
            _btnTrack3.Click += OnTrackClick;
            _btnRerollWeekly.Click += (_, _) => RerollWeekly();
            _btnFixStreak.Click += (_, _) => FixStreak();

            // The three daily seats each own a reroll button; the tab just forwards which seat was
            // pressed. The shell spends the reroll - no quest state is touched down here.
            foreach (var card in _dailyCards)
                card.RerollRequested += OnDailyCardRerollRequested;

            _streakCanvas.SizeChanged += (_, _) => PaintStreakCalendar();
        }

        // ---- SUB-TABS (view-only, ported for real) --------------------------------

        private void ShowDailyWeekly()
        {
            _dailyWeeklyPanel.IsVisible = true;
            _roadmapPanel.IsVisible = false;
            _btnSubDaily.Theme = TabTheme("TabButtonActive");
            _btnSubRoadmap.Theme = TabTheme("TabButton");
        }

        private void ShowRoadmap()
        {
            _dailyWeeklyPanel.IsVisible = false;
            _roadmapPanel.IsVisible = true;
            _btnSubDaily.Theme = TabTheme("TabButton");
            _btnSubRoadmap.Theme = TabTheme("TabButtonActive");
            // ponytail: needs RoadmapService (App.Roadmap), wired when it moves to Core. The panel
            // shows its authored placeholders until then.
        }

        private void OnTrackClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            var tag = (sender as Button)?.Tag as string;
            _btnTrack1.Theme = TabTheme(tag == "EmptyDoll" ? "TabButtonActive" : "TabButton");
            _btnTrack2.Theme = TabTheme(tag == "ObedientPuppet" ? "TabButtonActive" : "TabButton");
            _btnTrack3.Theme = TabTheme(tag == "SluttyBlowdoll" ? "TabButtonActive" : "TabButton");
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
            _streakCanvas.Children.Clear();

            const int days = 7, stamped = 4, size = 26;
            double width = _streakCanvas.Bounds.Width;
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
                _streakCanvas.Children.Add(pip);
            }
        }
    }
}
