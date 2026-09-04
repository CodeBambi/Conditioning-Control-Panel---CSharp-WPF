using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// Training Programs tab. Pure view, exactly as on WPF: every button handler there is a
    /// one-line forward to the MainWindow partial (MainWindow.ProgramsTab.cs), which owns the
    /// service reads and the refresh.
    ///
    /// PORTED from ConditioningControlPanel/Views/Tabs/ProgramsTabView.xaml.cs.
    ///  - Every forwarding handler becomes a stub: the partial it forwards to is a
    ///    <c>System.Windows.Window</c> on the WPF head and the program service is not in Core yet.
    ///    Names are kept identical so the wiring diffs cleanly when it lands.
    ///  - <c>SessionBarHost_SizeChanged</c> is genuinely view-only and is ported for real.
    ///  - The four state panels are seeded with sample data below, because nothing on this head
    ///    fills them.
    /// </summary>
    public partial class ProgramsTabView : UserControl
    {
        public ProgramsTabView()
        {
            AvaloniaXamlLoader.Load(this);
            SeedPlaceholders();
        }

        // ---- HANDLERS -------------------------------------------------------------

        // ponytail: each of these forwards to MainWindow on WPF (Window.GetWindow(this) is
        // MainWindow mw -> mw.<same name>). ProgramService is in CCP.Core now, so the blocker is
        // no longer the type - it is that MainShellWindow.ProgramsTab.cs is still a stub and this
        // head constructs no ProgramService instance. See the header of that file.
        private void BtnProgramEnroll_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramPauseResume_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramWithdraw_Click(object? sender, RoutedEventArgs e) { }
        private void BtnStartTodaySession_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramSubmitRitual_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramRestart_Click(object? sender, RoutedEventArgs e) { }
        private void BtnProgramDismissGraduated_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>
        /// Keeps the session bar's clip a rounded rect at its live size. A Border's ClipToBounds
        /// clips to the layout RECTANGLE, not the corner radius, so without this the sweeping sheen
        /// would poke square corners past the bar's rounded ends. Pure view concern, so it lives
        /// here - ported as-is from the WPF original.
        /// </summary>
        private void SessionBarHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is Border host && host.Bounds.Width > 0 && host.Bounds.Height > 0)
            {
                host.Clip = new RectangleGeometry(new Rect(0, 0, host.Bounds.Width, host.Bounds.Height))
                {
                    RadiusX = 8,
                    RadiusY = 8,
                };
            }
        }

        // ---- PLACEHOLDER DATA -----------------------------------------------------

        /// <summary>
        /// Sample content for all four state panels.
        ///
        /// On WPF exactly ONE of Browse / Run / Lapsed / Graduated is ever visible - MainWindow
        /// picks it from the live enrollment. That partial is not on this head, so leaving the
        /// original's IsVisible="False" untouched would render the tab as a header over nothing and
        /// leave ~900 lines of item templates completely unproven. All four are shown here instead,
        /// so --render-all draws every template in the file.
        ///
        /// ponytail: needs a live ProgramService instance plus the MainShellWindow.ProgramsTab
        /// partial. The service itself is in CCP.Core as of the program-service layer, and the real
        /// browse list is ProgramService.Library (BuiltInPrograms.All()); when the run/lapsed/
        /// graduated builders land, delete this method and the sample carriers under it and the
        /// panels go back to being driven one at a time.
        /// </summary>
        private void SeedPlaceholders()
        {
            var accent = new SolidColorBrush(Color.Parse("#FF69B4"));
            var muted = new SolidColorBrush(Color.Parse("#A9A3C2"));
            var light = new SolidColorBrush(Color.Parse("#F2ECFF"));
            var glass = new SolidColorBrush(Color.Parse("#33FFFFFF"));
            var gold = new SolidColorBrush(Color.Parse("#FFD700"));

            // ---- BROWSE ----------------------------------------------------------
            Find<StackPanel>("ProgramsBrowsePanel").IsVisible = true;
            Find<ItemsControl>("ProgramLibraryList").ItemsSource = new List<ProgramBrowseItem>
            {
                new()
                {
                    ProgramId = "spiral_descent", Icon = "🌀", Title = "The Spiral",
                    Subtitle = "Fourteen days of pattern work",
                    Pitch = "One session a day, a little longer each time. The spiral does the rest.",
                    LengthLabel = "14 days", TierLabel = "FREE",
                    TierBrush = light, TierBackground = new SolidColorBrush(Color.Parse("#333DFF9E")),
                    AccentBrush = accent, ActionText = "Enroll",
                },
                new()
                {
                    ProgramId = "soft_focus", Icon = "💗", Title = "Soft Focus",
                    Subtitle = "A gentle seven-day intake",
                    Pitch = "Short sessions, no boss days, one day off allowed. The place to start.",
                    LengthLabel = "7 days", TierLabel = "FREE",
                    TierBrush = light, TierBackground = new SolidColorBrush(Color.Parse("#333DFF9E")),
                    AccentBrush = new SolidColorBrush(Color.Parse("#7BD3FF")), ActionText = "Enroll",
                },
                new()
                {
                    ProgramId = "deep_dive", Icon = "🔒", Title = "Deep Dive",
                    Subtitle = "Twenty-eight days, strict only",
                    Pitch = "The long arc. Boss days every seventh, no days off, one attempt.",
                    LengthLabel = "28 days", TierLabel = "PATRON",
                    TierBrush = gold, TierBackground = new SolidColorBrush(Color.Parse("#33FFD700")),
                    AccentBrush = new SolidColorBrush(Color.Parse("#C08BFF")),
                    ActionText = "Locked", IsActionEnabled = false, IsLocked = true,
                    ReasonText = "Needs an active patron tier.", ReasonVisible = true,
                    CardOpacity = 0.72,
                },
            };

            // ---- RUN -------------------------------------------------------------
            Find<StackPanel>("ProgramsRunPanel").IsVisible = true;

            Find<Border>("RunAccentBar").Background = accent;
            Find<TextBlock>("TxtRunProgramTitle").Text = "The Spiral";
            var chapter = Find<TextBlock>("TxtRunChapterName");
            chapter.Text = "Descent";
            chapter.Foreground = accent;
            Find<TextBlock>("TxtRunDayCounter").Text = Loc.GetF("programs_day_counter", 6, 14);

            Find<Border>("RunStrictBadge").IsVisible = true;
            Find<Border>("RunAttemptBadge").IsVisible = true;
            Find<TextBlock>("TxtRunAttempt").Text = Loc.GetF("programs_attempt", 2);

            Find<TextBlock>("TxtRunStatDone").Text = "6 / 14";
            Find<TextBlock>("TxtRunStatPerfect").Text = "4";
            Find<TextBlock>("TxtRunStatDaysOff").Text = "1";

            // The pause/resume caption is Content on WPF; here it is a TextBlock inside the button,
            // because Avalonia would read the "_" in the loc key as an access key (CLAUDE.md trap 1).
            Find<TextBlock>("TxtProgramPauseResume").Text = Loc.Get("btn_program_pause");

            Find<Border>("RunChapterRewardChip").IsVisible = true;
            Find<TextBlock>("TxtRunChapterReward").Text = "A spiral palette for the overlay";

            // The rail: 14 nodes, five done, today is the sixth. The done segment is sized in star
            // units so it ends exactly on today's node centre - (5 + 0.5) / 14 of the rail.
            var railFill = Find<Border>("RailProgressFill");
            railFill.Background = accent;
            // RailDoneColumn / RailRestColumn keep their x:Name for a clean diff against the WPF
            // file, but a ColumnDefinition is not a Control, so FindControl cannot reach it -
            // the fill's own parent Grid owns exactly those two columns.
            if (railFill.Parent is Grid railTrack && railTrack.ColumnDefinitions.Count == 2)
            {
                railTrack.ColumnDefinitions[0].Width = new GridLength(5.5, GridUnitType.Star);
                railTrack.ColumnDefinitions[1].Width = new GridLength(8.5, GridUnitType.Star);
            }
            Find<ItemsControl>("ProgramDayStrip").ItemsSource = BuildDayPips(accent, muted, light);

            // Today's hero band: the accent wash the WPF code builds from the program accent.
            Find<Rectangle>("TodayHeroGlow").Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.Parse("#40FF69B4"), 0),
                    new GradientStop(Color.Parse("#00FF69B4"), 1),
                },
            };

            Find<Border>("TodayBossBadge").IsVisible = true;
            Find<TextBlock>("TxtTodayTitle").Text = "Sink into the pattern";
            Find<TextBlock>("TxtTodayBlurb").Text =
                "Longer than yesterday, and the spiral does not stop for the flashes any more. " +
                "Sit through it. Something pretty turns up near the end.";

            Find<StackPanel>("TodayLayersPanel").IsVisible = true;
            var templateName = Find<TextBlock>("TxtTodayTemplateName");
            templateName.Text = "Deep Soak";
            templateName.Foreground = accent;
            Find<TextBlock>("TxtTodayTemplateBlurb").Text =
                "A long, slow ramp. Flash rate and spiral speed climb together for the whole session.";
            Find<ItemsControl>("TodayLayerList").ItemsSource = new List<ProgramLayerChip>
            {
                new() { Label = "Spiral", Tip = "Spiral overlay, full session",
                        BorderBrush = glass, LabelBrush = light, AccentBrush = accent },
                new() { Label = "Flashes", Tip = "Flash images, ramping",
                        BorderBrush = glass, LabelBrush = light, AccentBrush = accent },
                new() { Label = "Subliminals", Tip = "Subliminal text - new today",
                        BorderBrush = accent, LabelBrush = light, AccentBrush = accent,
                        NewForeground = Brushes.Black, NewVisible = true },
                new() { Label = "Pink filter", Tip = "Screen tint - new today",
                        BorderBrush = accent, LabelBrush = light, AccentBrush = accent,
                        NewForeground = Brushes.Black, NewVisible = true },
            };

            Find<Border>("TodayRewardChip").IsVisible = true;
            Find<TextBlock>("TxtTodayReward").Text = "Unlocks the Descent mantra pack";

            Find<TextBlock>("TxtTodaySessionMinutes").Text = Loc.GetF("programs_session_minutes", 35);
            Find<Grid>("TodaySessionProgressRow").IsVisible = true;
            Find<ProgressBar>("TodaySessionProgressBar").Value = 42;
            var clock = Find<TextBlock>("TxtTodaySessionProgress");
            clock.Text = "14:22";
            clock.Foreground = accent;
            clock.IsVisible = true;

            Find<Border>("TodayAmbientRow").IsVisible = true;
            Find<TextBlock>("TxtTodayAmbient").Text = "Keep the spiral running in the background while you work.";
            Find<TextBlock>("TxtTodayAmbientProgress").Text = Loc.GetF("programs_ambient_progress", 18, 40);

            Find<Border>("TodayTasksDonePill").IsVisible = true;
            Find<TextBlock>("TxtTodayTasksDone").Text = Loc.GetF("programs_tasks_done_count", 1, 3);
            Find<ItemsControl>("TodayTaskList").ItemsSource = BuildTasks(accent, muted, light, glass);
            Find<TextBlock>("TxtRitualPrivacyNote").IsVisible = true;

            Find<ItemsControl>("TodayUpNextList").ItemsSource = new List<ProgramUpNextItem>
            {
                new() { DayLabel = "Day 7", Title = "The long soak", DayBrush = accent,
                        Meta = "45 minutes · Complete 3 lock cards · Boss day",
                        Glyph = "👑", GlyphTip = "Boss day", GlyphVisible = true },
                new() { DayLabel = "Day 8", Title = "Rest and repeat", DayBrush = muted,
                        Meta = "25 minutes · One session", RowOpacity = 0.8 },
                new() { DayLabel = "Day 9", Title = "Deeper still", DayBrush = muted,
                        Meta = "40 minutes · One session · Reward",
                        Glyph = "🎁", GlyphTip = "Reward day", GlyphVisible = true,
                        RowOpacity = 0.62 },
            };
            Find<TextBlock>("TxtTodayCloses").Text = Loc.GetF("programs_closes_at", "4:00 AM");
            Find<TextBlock>("TxtTodayClosesNote").Text = Loc.Get("programs_closes_note");
            Find<TextBlock>("TxtTodayStreak").Text = Loc.GetF("programs_streak_many", 5);

            // ---- LAPSED ----------------------------------------------------------
            Find<StackPanel>("ProgramsLapsedPanel").IsVisible = true;
            Find<TextBlock>("TxtLapsedBody").Text = Loc.GetF("programs_lapsed_body", "The Spiral", 3);

            // ---- GRADUATED -------------------------------------------------------
            Find<StackPanel>("ProgramsGraduatedPanel").IsVisible = true;
            Find<TextBlock>("TxtGraduatedSub").Text = Loc.GetF("programs_graduated_sub", "The Spiral");
            Find<TextBlock>("TxtGraduatedStats").Text = Loc.GetF("programs_graduated_stats", 2, 11, 14);
        }

        /// <summary>Fourteen rail nodes: five done, today, then the horizon.</summary>
        private static List<ProgramDayPip> BuildDayPips(IBrush accent, IBrush muted, IBrush light)
        {
            var pips = new List<ProgramDayPip>();
            var glow = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.Parse("#B0FF69B4"), 0),
                    new GradientStop(Color.Parse("#00FF69B4"), 1),
                },
            };

            for (int day = 1; day <= 14; day++)
            {
                bool done = day <= 5, today = day == 6;
                bool boss = day % 7 == 0;

                pips.Add(new ProgramDayPip
                {
                    DayIndex = day,
                    Label = done ? "✓" : day.ToString(),
                    Tip = $"Day {day}",
                    Fill = done ? accent : Brushes.Transparent,
                    Stroke = today ? accent : muted,
                    PipBorderThickness = new Thickness(today ? 2 : 1),
                    LabelBrush = done ? Brushes.Black : today ? light : muted,
                    LabelWeight = today ? FontWeight.Bold : FontWeight.Normal,
                    NodeSize = today ? 38 : done ? 32 : 26,
                    LabelSize = today ? 13 : 11,
                    PipOpacity = done || today ? 1.0 : 0.7,
                    IsCurrent = today,
                    GlowBrush = glow,
                    GlowVisible = today,
                    IgniteBrush = glow,
                    RewardGlyph = boss ? "👑" : day == 9 ? "🎁" : "",
                    RewardVisible = boss || day == 9,
                    RewardTip = boss ? "Boss day" : "Reward day",
                });
            }
            return pips;
        }

        /// <summary>One done task, one counted task and one ritual, so all three card states draw.</summary>
        private static List<ProgramTaskItem> BuildTasks(IBrush accent, IBrush muted, IBrush light, IBrush glass)
            => new()
            {
                new()
                {
                    TaskId = "session", Description = "Run today's session start to finish",
                    HowTo = "Verified by the session engine.", HowToVisible = false,
                    StatusGlyph = "✓", StatusBrush = accent, GlyphVisible = true,
                    CardBorderBrush = accent, DoneChipVisible = true,
                    DoneChipForeground = Brushes.Black, TextBrush = light,
                },
                new()
                {
                    TaskId = "bubbles", Description = "Pop forty bubbles while the spiral runs",
                    HowTo = "Verified by Bubble Pop. Any mode counts.", HowToVisible = true,
                    StatusGlyph = "○", StatusBrush = muted, GlyphVisible = true,
                    CardBorderBrush = glass, TextBrush = light,
                    ProgressText = "26 / 40", BarVisible = true,
                    ProgressStar = new GridLength(26, GridUnitType.Star),
                    RemainderStar = new GridLength(14, GridUnitType.Star),
                    AccentBrush = accent,
                },
                new()
                {
                    TaskId = "ritual", Description = "Take the evening photo",
                    HowTo = "Ritual task. Nothing leaves this machine.", HowToVisible = true,
                    StatusGlyph = "○", StatusBrush = muted, GlyphVisible = true,
                    CardBorderBrush = glass, TextBrush = light,
                    BadgeText = "OPTIONAL", BadgeVisible = true,
                    SubmitVisible = true,
                },
            };

        private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;
    }

    // -------------------------------------------------------------------------------------------
    // Presentation rows, copied from ConditioningControlPanel/Views/Tabs/ProgramsTabItems.cs.
    //
    // Same names, same order, same members. Two mechanical changes throughout: every
    // System.Windows.Visibility becomes a bool named <X>Visible, because Avalonia binds IsVisible
    // to a bool directly; and System.Windows.Media.Brush / ImageSource become Avalonia's IBrush /
    // IImage. ponytail: the WPF file stays the source of truth until Views/Tabs moves to Core, at
    // which point one of these two copies is deleted.
    // -------------------------------------------------------------------------------------------

    /// <summary>One program on the browse list (nothing enrolled).</summary>
    public class ProgramBrowseItem
    {
        public string ProgramId { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Pitch { get; set; } = "";
        public string LengthLabel { get; set; } = "";

        public string TierLabel { get; set; } = "";
        public IBrush TierBrush { get; set; } = Brushes.Gray;
        public IBrush TierBackground { get; set; } = Brushes.Transparent;

        public IBrush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>The program's banner strip for the card's header band. Null hides the band.</summary>
        public IImageBrushSource? BannerArt { get; set; }
        public bool BannerVisible { get; set; }

        /// <summary>
        /// The program's art, as the OPACITY MASK for the crest's accent-filled Rectangle - never as
        /// an Image source. Program art ships as white RGB with its luminance in the ALPHA channel,
        /// so accent Fill + this as the mask is what makes bright source read as full accent.
        /// </summary>
        public IBrush? ArtMask { get; set; }

        /// <summary>Accent halo behind the crest. Radial, built in code from the accent.</summary>
        public IBrush ArtGlowBrush { get; set; } = Brushes.Transparent;

        /// <summary>Shows the art crest. Hidden is the normal no-art state, not a failure.</summary>
        public bool ArtVisible { get; set; }

        /// <summary>Inverse of <see cref="ArtVisible"/>: the bare 44px glyph the card showed before
        /// the crest existed. Exactly one of the two is ever visible.</summary>
        public bool IconOnlyVisible { get; set; } = true;

        /// <summary>Premium program the user cannot currently take - the ✨ locked treatment.</summary>
        public bool IsLocked { get; set; }

        public string ActionText { get; set; } = "";
        public bool IsActionEnabled { get; set; } = true;

        public string ReasonText { get; set; } = "";
        public bool ReasonVisible { get; set; }

        public double CardOpacity { get; set; } = 1.0;
    }

    /// <summary>One node on the whole-program reward track.</summary>
    public class ProgramDayPip
    {
        public int DayIndex { get; set; }
        public string Label { get; set; } = "";
        public string Tip { get; set; } = "";

        public IBrush Fill { get; set; } = Brushes.Transparent;
        public IBrush Stroke { get; set; } = Brushes.Gray;
        public Thickness PipBorderThickness { get; set; } = new Thickness(1);
        public IBrush LabelBrush { get; set; } = Brushes.Gray;
        public double PipOpacity { get; set; } = 1.0;
        public FontWeight LabelWeight { get; set; } = FontWeight.Normal;

        /// <summary>Node diameter. Today is the largest, done days middle, future days smallest.</summary>
        public double NodeSize { get; set; } = 30;
        public double LabelSize { get; set; } = 11;

        /// <summary>Today's node. Drives the node's size/colour treatment only.</summary>
        public bool IsCurrent { get; set; }

        /// <summary>
        /// Today's node AND motion allowed. On WPF a DataTrigger read this once to start the
        /// breathing-glow storyboard; the storyboards are dropped on this head, so nothing reads it
        /// yet - kept so the carrier still matches the original one for one.
        /// </summary>
        public bool Breathe { get; set; }
        public IBrush GlowBrush { get; set; } = Brushes.Transparent;
        public bool GlowVisible { get; set; }

        /// <summary>
        /// One-shot: this day just flipped to complete, so its node flares. Same story as
        /// <see cref="Breathe"/> - the flare Ellipse is still in the template, unanimated.
        /// </summary>
        public bool Ignite { get; set; }

        /// <summary>Fill of the ignite flare. The program accent as a radial, built in code.</summary>
        public IBrush IgniteBrush { get; set; } = Brushes.Transparent;

        /// <summary>Milestone treatment: boss crown / reward gift under the node.</summary>
        public string RewardGlyph { get; set; } = "";
        public bool RewardVisible { get; set; }
        public string RewardTip { get; set; } = "";
    }

    /// <summary>One task row inside today's panel.</summary>
    public class ProgramTaskItem
    {
        public string TaskId { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>
        /// The plain "how do I actually do this" line under the flavour text: the exact feature the
        /// verifier draws credit from and what has to happen. Hidden once the task is complete.
        /// </summary>
        public string HowTo { get; set; } = "";
        public bool HowToVisible { get; set; }

        public string StatusGlyph { get; set; } = "";
        public IBrush StatusBrush { get; set; } = Brushes.Gray;

        /// <summary>
        /// The app's own product icon for whatever this task is verified by (Resources/features/*).
        /// Null for tasks with no feature behind them - rituals, ambient work - in which case
        /// <see cref="IconVisible"/> is false and the status glyph carries the row on its own.
        /// </summary>
        public IImage? Icon { get; set; }
        public bool IconVisible { get; set; }

        /// <summary>Inverse of <see cref="IconVisible"/>: the glyph carries the icon slot alone.</summary>
        public bool GlyphVisible { get; set; } = true;

        public string ProgressText { get; set; } = "";

        /// <summary>Counted tasks (TargetValue &gt; 1) show the mini progress bar; others hide it.</summary>
        public bool BarVisible { get; set; }

        /// <summary>
        /// Star widths for the mini bar's filled/remaining columns. Pre-computed GridLengths like
        /// every other value here - the template binds ColumnDefinition.Width straight to them.
        /// </summary>
        public GridLength ProgressStar { get; set; } = new GridLength(0, GridUnitType.Star);
        public GridLength RemainderStar { get; set; } = new GridLength(1, GridUnitType.Star);

        /// <summary>Mini bar fill - the program accent, resolved in code.</summary>
        public IBrush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>Accent when the task is done, the plain glass border otherwise.</summary>
        public IBrush CardBorderBrush { get; set; } = Brushes.Transparent;

        /// <summary>The ✓ chip in the card's top-right corner.</summary>
        public bool DoneChipVisible { get; set; }

        /// <summary>
        /// Ink for that ✓, picked from the chip's own accent fill rather than fixed near-white - a
        /// pale program or mod accent rendered the tick white on white.
        /// </summary>
        public IBrush DoneChipForeground { get; set; } = Brushes.White;

        /// <summary>
        /// True only on the rebuild immediately after the task flipped to complete. On WPF this
        /// drove the card's pop storyboard, which is dropped here.
        /// </summary>
        public bool JustCompleted { get; set; }

        public string BadgeText { get; set; } = "";
        public bool BadgeVisible { get; set; }

        /// <summary>Ritual tasks get the photo picker; auto-verified ones never do.</summary>
        public bool SubmitVisible { get; set; }

        public double RowOpacity { get; set; } = 1.0;
        public IBrush TextBrush { get; set; } = Brushes.White;
    }

    /// <summary>
    /// One feature layer today's session turns on ("Bubbles", "Pink filter"), shown under the day
    /// blurb. Built from the day's session TEMPLATE, not from the live engine.
    /// </summary>
    public class ProgramLayerChip
    {
        public string Label { get; set; } = "";

        /// <summary>Full text on hover, including the "new today" note when the layer is new.</summary>
        public string Tip { get; set; } = "";

        /// <summary>
        /// The app's own product icon for the feature (Resources/features/*). Hidden when the PNG
        /// does not resolve; the chip is then a plain labelled pill.
        /// </summary>
        public IImage? Icon { get; set; }
        public bool IconVisible { get; set; }

        /// <summary>Accent when the layer is new today, the plain glass border otherwise.</summary>
        public IBrush BorderBrush { get; set; } = Brushes.Transparent;
        public IBrush LabelBrush { get; set; } = Brushes.White;

        /// <summary>Fill of the NEW pill. The program accent, resolved in code.</summary>
        public IBrush AccentBrush { get; set; } = Brushes.Gray;

        /// <summary>
        /// Ink for the NEW pill's label, picked from the accent behind it rather than fixed
        /// near-white - a pale accent rendered the word white on white.
        /// </summary>
        public IBrush NewForeground { get; set; } = Brushes.White;

        /// <summary>The NEW pill: this layer was not in the previous day's session.</summary>
        public bool NewVisible { get; set; }
    }

    /// <summary>One upcoming day in the run view's "up next" column.</summary>
    public class ProgramUpNextItem
    {
        public string DayLabel { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Pre-joined "45 minutes · Complete 3 lock cards · Boss day".</summary>
        public string Meta { get; set; } = "";

        /// <summary>Accent on a boss day, muted otherwise.</summary>
        public IBrush DayBrush { get; set; } = Brushes.Gray;

        public string Glyph { get; set; } = "";
        public string GlyphTip { get; set; } = "";
        public bool GlyphVisible { get; set; }

        /// <summary>Further-out days sit back a little, so the list reads as a horizon.</summary>
        public double RowOpacity { get; set; } = 1.0;
    }
}
