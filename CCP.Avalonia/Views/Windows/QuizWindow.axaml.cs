using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IOPath = System.IO.Path;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The ten-question AI personality quiz: category select, a generating screen, one question at
    /// a time with progress dots and a running score, the archetype result, and an error screen.
    ///
    /// PORTED from ConditioningControlPanel/Windows/QuizWindow.xaml.cs. What survives verbatim is
    /// everything that only touches this window: the five-panel state machine, the animated
    /// background gradient, the progress dots, the category-button builder, the answer hover and
    /// flash, the loading-dots ticker and the whole surrender easter egg. Deviations:
    ///
    ///  - <b>Wired against the seams:</b> <c>App.Settings</c> is <see cref="CoreSettings"/> (the
    ///    <c>UseLocalAi</c> error wording and the whole <c>LatestQuiz*</c> companion hand-off),
    ///    <c>App.Audio</c> is <see cref="CoreAudio"/> (the giggle, the chime, GOOD GIRL, the result
    ///    sting and the surrender duck/unduck) and <c>App.Logger</c> is Serilog's static
    ///    <c>Log</c>. The question flow, scoring, timers, formatting and visual state are the WPF
    ///    original's.
    ///  - <c>QuizService</c> (<c>ConditioningControlPanel/Services/Quiz/QuizService.cs</c>),
    ///    <c>QuizSessionGenerator</c> and <c>SessionFileService</c> are still in the WPF head, so
    ///    the AI round trip, the category store, the history file and the session export are what
    ///    remains stubbed; each stub names its exact symbol.
    ///    <c>QuizQuestion</c> and <c>QuizResult</c> are copied below, trimmed to what this view
    ///    reads (the TextEditorDialog / QuizReportWindow / QuizCategoryEditorWindow precedent);
    ///    <c>QuizCategoryDefinition</c>, <c>QuizAnswerRecord</c> and <c>QuizCategory</c> are already
    ///    declared beside this class by those two ports and are REUSED, not re-declared.
    ///  - NAudio's drone loop, <c>App.Flash</c>/<c>Bubbles</c>/<c>Subliminal</c>/<c>MindWipe</c> and
    ///    <c>App.AvatarWindow</c> stay head-only; the <c>LoopStream</c> wrapper and the WaveOutEvent
    ///    pool went with them. There is no XP call to port: WPF awards quiz XP through
    ///    <c>QuizService.RaiseQuizCompleted</c> -> <c>GamificationBridge</c>, both head-side, so a
    ///    <c>CoreProgression.AddXP</c> here would be a second, divergent copy of bridge logic.
    ///  - <c>Microsoft.Win32.SaveFileDialog</c> -> Avalonia's <c>IStorageProvider</c> is available,
    ///    but there is no session to export while the generator is stubbed, so the handler is too.
    ///  - WPF <c>Storyboard</c>/<c>DoubleAnimation</c> -> Avalonia <c>Animation</c>, run against the
    ///    control (fade) or against its <c>Transform</c> (score pulse, surrender shake).
    ///  - <c>Visibility</c> -> <c>IsVisible</c>; <c>FontWeights.X</c> -> <c>FontWeight.X</c>;
    ///    <c>Panel.SetZIndex</c> -> the <c>ZIndex</c> property; <c>DragMove()</c> ->
    ///    <c>BeginMoveDrag(e)</c>; <c>ShowDialog()</c> is async, so the two category-editor handlers
    ///    became <c>async void</c>.
    ///  - The public ctor loses its defaults: with a parameterless render ctor beside it,
    ///    <c>new QuizWindow()</c> would be ambiguous (CS0121). Callers pass both arguments.
    /// </summary>
    public partial class QuizWindow : Window
    {
        public static bool IsOpen { get; private set; }

        /// <summary>Score percentage that counts as a "perfect" run for the quiz achievements
        /// (top_of_the_class, honor_roll). Below 100 on purpose - see ShowResult.</summary>
        private const double PerfectScorePercent = 90;

        private QuizQuestion? _currentQuestion;
        private bool _isProcessing;
        private bool _isTrickQuestion;
        private bool _isSurrenderEasterEgg;
        private QuizQuestion? _savedNextQuestion;
        /// <summary>What WPF read off <c>QuizService.CurrentCategoryDefinition</c>: the category
        /// this run was started over, which the result panel's companion hand-off needs.</summary>
        private QuizCategoryDefinition? _currentCategoryDefinition;
        private long _surrenderDuckGen;
        private readonly DispatcherTimer _loadingDotsTimer;
        private int _loadingDotCount;
        private readonly Ellipse[] _progressDots = new Ellipse[10];
        private readonly List<QuizAnswerRecord> _answerHistory = new();
        private int _totalScore;
        private int _questionNumber;

        private static string[] LoadingFlavors => new[]
        {
            Loc.Get("quiz_loading_1"),
            Loc.Get("quiz_loading_2"),
            Loc.Get("quiz_loading_3"),
            Loc.Get("quiz_loading_4"),
            Loc.Get("quiz_loading_5"),
            Loc.Get("quiz_loading_6"),
            Loc.Get("quiz_loading_7"),
            Loc.Get("quiz_loading_8"),
            Loc.Get("quiz_loading_9"),
            Loc.Get("quiz_loading_10")
        };

        private static readonly Random _random = new();

        private static readonly string[] GiggleFiles = new[]
        {
            "giggle1.MP3", "giggle2.MP3", "giggle3.MP3", "giggle4.MP3",
            "giggle5.mp3", "giggle6.wav", "giggle7.mp3", "giggle8.mp3"
        };
        private static readonly string[] ChimeFiles = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };

        private static readonly (string Question, string Answer)[] TrickQuestions = new[]
        {
            ("Do you like to let go and obey?", "Yes"),
            ("Are you a good girl?", "Obviously"),
            ("Do you want to go deeper?", "Yes please"),
            ("Is it easier when you don't think?", "Mmhmm"),
            ("Do you enjoy being told what to do?", "Absolutely"),
            ("Would you like to surrender control?", "Yes"),
        };

        // Background gradient animation
        private readonly DispatcherTimer _gradientTimer;
        private double _gradientPhase;
        private GradientStop? _bgStop0, _bgStop1, _bgStop2;

        // Dark atmospheric versions of the app palette (pink / magenta / purple / violet / indigo)
        private static readonly Color[] _gradientPalette = new[]
        {
            Color.FromRgb(0x30, 0x06, 0x1A), // Deep hot pink
            Color.FromRgb(0x2A, 0x08, 0x22), // Deep magenta
            Color.FromRgb(0x1A, 0x0A, 0x2E), // Deep indigo (original bg)
            Color.FromRgb(0x0E, 0x08, 0x30), // Deep blue-violet
            Color.FromRgb(0x18, 0x06, 0x32), // Deep purple
            Color.FromRgb(0x22, 0x0A, 0x2A), // Deep fuchsia
        };

        // Named controls, resolved once. Avalonia's generated x:Name fields are only emitted for
        // the compiled-XAML path; FindControl is what every ported view here uses.
        private readonly Border _titleBar, _backgroundBorder, _glowOverlay;
        private readonly Border _answerA, _answerB, _answerC, _answerD, _btnTrySession;
        private readonly Grid _categorySelectPanel, _loadingPanel, _questionPanel, _resultPanel, _errorPanel;
        private readonly Grid _rootGrid, _questionContentGrid;
        private readonly StackPanel _categoryButtonsPanel, _progressDotsPanel, _answersPanel, _trendPanel;
        private readonly TextBlock _txtLoadingDots, _txtLoadingFlavor, _txtQuestion, _scoreText;
        private readonly TextBlock _txtAnswerA, _txtAnswerB, _txtAnswerC, _txtAnswerD;
        private readonly TextBlock _txtFinalScore, _txtScoreLabel, _txtProfileText, _txtError;
        private readonly TextBlock _txtTrySessionIcon, _txtTrySessionLabel, _txtTrendHeader;
        private readonly Button _btnMaximizeTitleBar;

        /// <summary>Render/design constructor: the windowed first screen, which is the state the
        /// quiz actually opens in when it is not fullscreen - title bar, headline and the category
        /// list. Internal, so no production caller can ship it.</summary>
        internal QuizWindow() : this(false, false) { }

        public QuizWindow(bool fullscreen, bool playDrone)
        {
            IsOpen = true;

            // ponytail: needs AvatarWindow.IsMuted / SetMuteAvatar from
            // ConditioningControlPanel/Windows/AvatarWindow.xaml.cs - WPF muted the avatar for the
            // whole run so her z-order work could not cover the quiz, and restored the previous
            // state on close. This head's avatar is AvatarTubeWindow, which carries no mute yet.

            AvaloniaXamlLoader.Load(this);
            // ponytail: the playDrone track (Resources/sounds/"00 Bimbo Drone.mp3") is a LOOP over a
            // NAudio WaveOutEvent plus App.Audio.ApplyPreferredDevice. CoreAudio offers PlayOneShot
            // only, which expresses neither, so this waits on a looping-playback seam. Deliberately
            // NOT faked with a one-shot: the drone would play once and go silent while the quiz ran
            // on, which is a worse lie than no drone at all.
            _ = playDrone;

            _rootGrid = this.FindControl<Grid>("RootGrid")!;
            _titleBar = this.FindControl<Border>("TitleBar")!;
            _backgroundBorder = this.FindControl<Border>("BackgroundBorder")!;
            _glowOverlay = this.FindControl<Border>("GlowOverlay")!;
            _categorySelectPanel = this.FindControl<Grid>("CategorySelectPanel")!;
            _loadingPanel = this.FindControl<Grid>("LoadingPanel")!;
            _questionPanel = this.FindControl<Grid>("QuestionPanel")!;
            _resultPanel = this.FindControl<Grid>("ResultPanel")!;
            _errorPanel = this.FindControl<Grid>("ErrorPanel")!;
            _questionContentGrid = this.FindControl<Grid>("QuestionContentGrid")!;
            _categoryButtonsPanel = this.FindControl<StackPanel>("CategoryButtonsPanel")!;
            _progressDotsPanel = this.FindControl<StackPanel>("ProgressDotsPanel")!;
            _answersPanel = this.FindControl<StackPanel>("AnswersPanel")!;
            _trendPanel = this.FindControl<StackPanel>("TrendPanel")!;
            _answerA = this.FindControl<Border>("AnswerA")!;
            _answerB = this.FindControl<Border>("AnswerB")!;
            _answerC = this.FindControl<Border>("AnswerC")!;
            _answerD = this.FindControl<Border>("AnswerD")!;
            _btnTrySession = this.FindControl<Border>("BtnTrySession")!;
            _txtLoadingDots = this.FindControl<TextBlock>("TxtLoadingDots")!;
            _txtLoadingFlavor = this.FindControl<TextBlock>("TxtLoadingFlavor")!;
            _txtQuestion = this.FindControl<TextBlock>("TxtQuestion")!;
            _scoreText = this.FindControl<TextBlock>("ScoreText")!;
            _txtAnswerA = this.FindControl<TextBlock>("TxtAnswerA")!;
            _txtAnswerB = this.FindControl<TextBlock>("TxtAnswerB")!;
            _txtAnswerC = this.FindControl<TextBlock>("TxtAnswerC")!;
            _txtAnswerD = this.FindControl<TextBlock>("TxtAnswerD")!;
            _txtFinalScore = this.FindControl<TextBlock>("TxtFinalScore")!;
            _txtScoreLabel = this.FindControl<TextBlock>("TxtScoreLabel")!;
            _txtProfileText = this.FindControl<TextBlock>("TxtProfileText")!;
            _txtError = this.FindControl<TextBlock>("TxtError")!;
            _txtTrySessionIcon = this.FindControl<TextBlock>("TxtTrySessionIcon")!;
            _txtTrySessionLabel = this.FindControl<TextBlock>("TxtTrySessionLabel")!;
            _txtTrendHeader = this.FindControl<TextBlock>("TxtTrendHeader")!;
            _btnMaximizeTitleBar = this.FindControl<Button>("BtnMaximizeTitleBar")!;

            // The four strings the code-behind also assigns. They carry no {loc:Str} in markup,
            // because Avalonia keeps a binding alive under a local value where WPF cleared it -
            // see the header comment in the .axaml.
            _txtLoadingDots.Text = Loc.Get("label_generating_2");
            _txtLoadingFlavor.Text = Loc.Get("label_the_quiz_master_is_thinking_up_something_devi");
            _txtTrySessionIcon.Text = Loc.Get("label_text_8");
            _txtTrySessionLabel.Text = Loc.Get("label_generating_your_session");

            if (fullscreen)
            {
                WindowState = WindowState.Maximized;
                _titleBar.IsVisible = false;
            }
            else
            {
                WindowState = WindowState.Normal;
                Topmost = false;
            }

            _loadingDotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _loadingDotsTimer.Tick += LoadingDotsTimer_Tick;

            _gradientTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _gradientTimer.Tick += GradientTimer_Tick;

            // Handlers live here rather than in markup, per the porting convention.
            Loaded += (_, _) => Window_Loaded();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) CleanupAndClose(); };

            _titleBar.PointerPressed += TitleBar_PointerPressed;
            _btnMaximizeTitleBar.Click += (_, _) => ToggleMaximize();
            this.FindControl<Button>("BtnCloseTitleBar")!.Click += (_, _) => CleanupAndClose();
            this.FindControl<Button>("BtnPlayAgain")!.Click += (_, _) => BtnPlayAgain_Click();
            this.FindControl<Button>("BtnCloseResult")!.Click += (_, _) => CleanupAndClose();
            this.FindControl<Button>("BtnTryAgain")!.Click += (_, _) => BtnPlayAgain_Click();

            foreach (var answer in new[] { _answerA, _answerB, _answerC, _answerD })
            {
                answer.PointerPressed += Answer_Click;
                answer.PointerEntered += Answer_PointerEntered;
                answer.PointerExited += Answer_PointerExited;
            }

            _btnTrySession.PointerPressed += (_, _) => BtnTrySession_Click();
        }

        private void Window_Loaded()
        {
            BuildProgressDots();
            BuildCategoryButtons();

            // Start glow pulse animation (the style in .axaml carries the keyframes)
            _glowOverlay.Classes.Add("pulse");

            // Initialize animated background gradient
            _bgStop0 = new GradientStop(_gradientPalette[0], 0.0);
            _bgStop1 = new GradientStop(_gradientPalette[2], 0.5);
            _bgStop2 = new GradientStop(_gradientPalette[4], 1.0);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
            };
            brush.GradientStops.Add(_bgStop0);
            brush.GradientStops.Add(_bgStop1);
            brush.GradientStops.Add(_bgStop2);
            _backgroundBorder.Background = brush;
            _gradientTimer.Start();
        }

        private void BuildProgressDots()
        {
            _progressDotsPanel.Children.Clear();
            for (int i = 0; i < 10; i++)
            {
                var dot = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(3, 0, 3, 0)
                };
                _progressDots[i] = dot;
                _progressDotsPanel.Children.Add(dot);
            }
        }

        private void BuildCategoryButtons()
        {
            _categoryButtonsPanel.Children.Clear();
            var categories = GetAllCategories();
            foreach (var cat in categories)
            {
                var color = Colors.White;
                try { color = Color.Parse(cat.Color); }
                catch { }

                var border = new Border
                {
                    Cursor = new Cursor(StandardCursorType.Hand),
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(20, 16, 20, 16),
                    Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1.5),
                    Tag = cat
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = cat.Name,
                    Foreground = new SolidColorBrush(color),
                    FontWeight = FontWeight.Bold,
                    FontSize = 26
                });
                stack.Children.Add(new TextBlock
                {
                    Text = cat.Description,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
                    FontSize = 17,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                Grid.SetColumn(stack, 0);
                grid.Children.Add(stack);

                // Edit button for custom categories
                if (!cat.IsBuiltIn)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var editBtn = new TextBlock
                    {
                        Text = "Edit",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80)),
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = new Cursor(StandardCursorType.Hand),
                        Margin = new Thickness(10, 0, 0, 0),
                        Tag = cat
                    };
                    editBtn.PointerPressed += EditCategoryButton_Click;
                    editBtn.PointerEntered += (s, _) => { if (s is TextBlock t) t.Foreground = Brushes.White; };
                    editBtn.PointerExited += (s, _) => { if (s is TextBlock t) t.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80)); };
                    Grid.SetColumn(editBtn, 1);
                    grid.Children.Add(editBtn);
                }

                border.Child = grid;

                border.PointerPressed += DynamicCategoryButton_Click;
                border.PointerEntered += CategoryButton_PointerEntered;
                border.PointerExited += CategoryButton_PointerExited;

                _categoryButtonsPanel.Children.Add(border);
            }

            // "+ Create Custom" button
            var createBorder = new Border
            {
                Cursor = new Cursor(StandardCursorType.Hand),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 4, 0, 12),
                Padding = new Thickness(20, 14, 20, 14),
                Background = new SolidColorBrush(Colors.Transparent),
                // Dashed border via VisualBrush not easily done in code, use dotted look
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1.5),
            };

            var createStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            createStack.Children.Add(new TextBlock
            {
                Text = "+ Create Custom Category",
                Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x88)),
                FontWeight = FontWeight.SemiBold,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            createBorder.Child = createStack;
            createBorder.PointerPressed += CreateCategoryButton_Click;
            createBorder.PointerEntered += (s, _) =>
            {
                if (s is Border b) b.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
            };
            createBorder.PointerExited += (s, _) =>
            {
                if (s is Border b) b.Background = new SolidColorBrush(Colors.Transparent);
            };

            _categoryButtonsPanel.Children.Add(createBorder);
        }

        private async void CreateCategoryButton_Click(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            var editor = new QuizCategoryEditorWindow(null);
            if (await editor.ShowDialog<bool>(this) && editor.Result != null)
            {
                // ponytail: needs QuizService.SaveCustomCategory from
                // ConditioningControlPanel/Services/Quiz/QuizService.cs to write the new category
                // into custom_quiz_categories.json. The rebuild below still runs, so the list
                // refreshes (from the stub).
                BuildCategoryButtons();
            }
        }

        private async void EditCategoryButton_Click(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Control el || el.Tag is not QuizCategoryDefinition catDef) return;

            var editor = new QuizCategoryEditorWindow(catDef);
            if (await editor.ShowDialog<bool>(this))
            {
                // ponytail: needs QuizService.SaveCustomCategory from
                // ConditioningControlPanel/Services/Quiz/QuizService.cs when editor.Result is
                // non-null. If Result is null, it was deleted (handled inside editor)
                BuildCategoryButtons();
            }
        }

        private void UpdateProgressDots(int currentQuestion)
        {
            for (int i = 0; i < 10; i++)
            {
                if (i < currentQuestion - 1)
                {
                    // Completed - bright pink
                    _progressDots[i].Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
                }
                else if (i == currentQuestion - 1)
                {
                    // Current - white
                    _progressDots[i].Fill = Brushes.White;
                }
                else
                {
                    // Future - dim
                    _progressDots[i].Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                }
            }
        }

        private void UpdateScore(int score)
        {
            _scoreText.Text = Loc.GetF("quiz_score", score);

            // The WPF ScorePulseStoryboard: ScaleX/Y 1 -> 1.15 at 150ms -> 1.0 at 300ms.
            if (_scoreText.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                _scoreText.RenderTransform = scale;
            }
            var pulse = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                Children =
                {
                    Frame(0.0, (ScaleTransform.ScaleXProperty, 1.0), (ScaleTransform.ScaleYProperty, 1.0)),
                    Frame(0.5, (ScaleTransform.ScaleXProperty, 1.15), (ScaleTransform.ScaleYProperty, 1.15)),
                    Frame(1.0, (ScaleTransform.ScaleXProperty, 1.0), (ScaleTransform.ScaleYProperty, 1.0)),
                }
            };
            _ = pulse.RunAsync(scale);
        }

        /// <summary>One Avalonia KeyFrame at <paramref name="cue"/>, with the given setters.</summary>
        private static KeyFrame Frame(double cue, params (AvaloniaProperty Property, object Value)[] setters)
        {
            var frame = new KeyFrame { Cue = new Cue(cue) };
            foreach (var (property, value) in setters)
                frame.Setters.Add(new Setter(property, value));
            return frame;
        }

        // ============ STATE TRANSITIONS ============

        private void ShowPanel(Control panel)
        {
            _categorySelectPanel.IsVisible = false;
            _loadingPanel.IsVisible = false;
            _questionPanel.IsVisible = false;
            _resultPanel.IsVisible = false;
            _errorPanel.IsVisible = false;

            panel.IsVisible = true;
        }

        private void ShowLoading(string? flavorText = null)
        {
            _loadingDotCount = 0;
            _txtLoadingDots.Text = Loc.Get("label_generating_3");
            _txtLoadingFlavor.Text = flavorText ?? LoadingFlavors[_random.Next(LoadingFlavors.Length)];
            _loadingDotsTimer.Start();
            ShowPanel(_loadingPanel);
            PlayRandomGiggle();
        }

        private static void ShuffleAnswers(QuizQuestion question)
        {
            var n = question.Answers.Length;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (question.Answers[i], question.Answers[j]) = (question.Answers[j], question.Answers[i]);
                (question.Points[i], question.Points[j]) = (question.Points[j], question.Points[i]);
            }
        }

        private void ShowQuestion(QuizQuestion question)
        {
            _currentQuestion = question;
            _loadingDotsTimer.Stop();

            UpdateProgressDots(question.Number);
            UpdateScore(_totalScore);

            ShuffleAnswers(question);
            _txtQuestion.Text = question.QuestionText;
            _txtAnswerA.Text = question.Answers[0];
            _txtAnswerB.Text = question.Answers[1];
            _txtAnswerC.Text = question.Answers[2];
            _txtAnswerD.Text = question.Answers[3];

            SetAnswersEnabled(true);
            ShowPanel(_questionPanel);

            // Animate question in
            AnimateQuestionIn();
        }

        private void ShowResult(QuizResult result)
        {
            _loadingDotsTimer.Stop();

            var catDef = _currentCategoryDefinition;

            // ponytail: needs QuizHistoryEntry + QuizService.SaveEntry from
            // ConditioningControlPanel/Services/Quiz/QuizService.cs to append this run to
            // quiz_history.json. Head-side, so nothing is recorded and savedEntry stays null -
            // which is also what gates BuildTrendDisplay and the session generation below, exactly
            // as it does in WPF on a save failure.

            // Latest quiz result for the companion hand-off. WPF mutates settings here and lets
            // the next debounced save carry it; mirrored, so no extra write is introduced.
            try
            {
                var settings = CoreSettings.Current;
                settings.LatestQuizCategoryId = catDef?.Id ?? result.Category.ToString();
                settings.LatestQuizScorePercentage = result.MaxScore > 0
                    ? (int)Math.Round((double)result.TotalScore / result.MaxScore * 100) : 0;
                settings.LatestQuizProfileText = result.ProfileText;

                // Extract archetype from profile text
                var archetypeMatch = System.Text.RegularExpressions.Regex.Match(
                    result.ProfileText, @"You are a (.+?)\.");
                settings.LatestQuizArchetype = archetypeMatch.Success
                    ? archetypeMatch.Groups[1].Value : "";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "QuizWindow: Failed to save quiz result to settings");
            }

            _txtFinalScore.Text = Loc.GetF("quiz_final_score", result.TotalScore, result.MaxScore);

            var percentage = result.MaxScore > 0 ? (double)result.TotalScore / result.MaxScore * 100 : 0;
            _txtScoreLabel.Text = percentage switch
            {
                >= 90 => Loc.Get("quiz_result_90"),
                >= 75 => Loc.Get("quiz_result_75"),
                >= 60 => Loc.Get("quiz_result_60"),
                >= 40 => Loc.Get("quiz_result_40"),
                >= 20 => Loc.Get("quiz_result_20"),
                _ => Loc.Get("quiz_result_0")
            };

            _txtProfileText.Text = result.ProfileText;

            // ponytail: needs QuizService.RaiseQuizCompleted from
            // ConditioningControlPanel/Services/Quiz/QuizService.cs - the static event
            // GamificationBridge listens on for the quiz achievements (and, through it, the XP
            // award). perfect = >= PerfectScorePercent, passed = >= 60, both defined here because
            // the quiz has no native pass/perfect concept; "perfect" is deliberately not full
            // marks, because several categories score on a profile curve where the top answer is
            // subjective. Both the event and the bridge are head-side; calling CoreProgression.AddXP
            // from here instead would be a second, divergent copy of the bridge's award rules.
            _ = PerfectScorePercent;

            BuildTrendDisplay();

            ShowPanel(_resultPanel);
            PlayResultSound();
        }

        private void BtnTrySession_Click()
        {
            // ponytail: needs QuizSessionGenerator (ConditioningControlPanel/Services/Quiz/
            // QuizSessionGenerator.cs) and SessionFileService (ConditioningControlPanel/Services/
            // Session/SessionFileService.cs) to build and export the quiz-shaped Session, plus a
            // save-file picker - Avalonia's IStorageProvider covers that half. Both services are
            // head-side. The border stays IsHitTestVisible="False", so this cannot fire.
        }

        /// <summary>
        /// The "your journey" line under the result. ponytail: needs QuizService.LoadHistory /
        /// GetScoreTrend / TrendKey / DisplayName from
        /// ConditioningControlPanel/Services/Quiz/QuizService.cs, plus the QuizHistoryEntry this
        /// run would have been saved as. All head-side. The header and panel stay hidden until
        /// then, exactly as they do on a first quiz, so nothing draws half-built - a trend line
        /// over one invented number would be a control that lies about the user's history.
        /// </summary>
        private void BuildTrendDisplay()
        {
            _trendPanel.Children.Clear();
            _txtTrendHeader.IsVisible = false;
        }

        private void ShowError(string message)
        {
            _loadingDotsTimer.Stop();
            _txtError.Text = message;
            ShowPanel(_errorPanel);
        }

        /// <summary>
        /// Provider-aware "couldn't generate" message. Local-AI users got the cloud
        /// "daily limit" line even though the quiz now runs on their Ollama instance
        /// (#334), which sent them chasing a limit that doesn't apply — point them at
        /// Ollama instead.
        /// </summary>
        private static string QuizGenerationFailedMessage()
        {
            bool useLocal = CoreSettings.Current.CompanionPrompt?.UseLocalAi == true;
            return useLocal
                ? "Couldn't generate the quiz. Make sure Ollama is running and your model is pulled (Companion → AI), then try again."
                : "Couldn't generate the quiz. The AI might be busy or you've hit your daily limit. Try again in a moment.";
        }

        // ============ ANIMATIONS ============

        private void GradientTimer_Tick(object? sender, EventArgs e)
        {
            _gradientPhase += 0.008;

            // Slowly rotate the gradient angle
            var angle = _gradientPhase * 0.3;
            if (_backgroundBorder.Background is LinearGradientBrush brush)
            {
                brush.StartPoint = new RelativePoint(0.5 + 0.5 * Math.Cos(angle), 0.5 + 0.5 * Math.Sin(angle), RelativeUnit.Relative);
                brush.EndPoint = new RelativePoint(0.5 - 0.5 * Math.Cos(angle), 0.5 - 0.5 * Math.Sin(angle), RelativeUnit.Relative);
            }

            // Each stop cycles through the palette at a different rate
            if (_bgStop0 != null) _bgStop0.Color = SampleGradientColor(_gradientPhase * 0.9);
            if (_bgStop1 != null) _bgStop1.Color = SampleGradientColor(_gradientPhase * 1.1 + 2.1);
            if (_bgStop2 != null) _bgStop2.Color = SampleGradientColor(_gradientPhase * 0.7 + 4.2);
        }

        private static Color SampleGradientColor(double phase)
        {
            // Map sine wave (oscillates 0..1) to a position in the palette
            var t = (Math.Sin(phase) + 1.0) / 2.0;
            var index = t * (_gradientPalette.Length - 1);
            var i = Math.Clamp((int)index, 0, _gradientPalette.Length - 2);
            var frac = index - i;

            var c1 = _gradientPalette[i];
            var c2 = _gradientPalette[i + 1];
            return Color.FromRgb(
                (byte)(c1.R + (c2.R - c1.R) * frac),
                (byte)(c1.G + (c2.G - c1.G) * frac),
                (byte)(c1.B + (c2.B - c1.B) * frac));
        }

        private void AnimateQuestionIn()
        {
            _questionContentGrid.Opacity = 0;
            _answersPanel.Opacity = 0;

            // Question fade in
            var questionAnim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(400),
                Easing = new QuadraticEaseOut(),
                FillMode = FillMode.Forward,
                Children = { Frame(0.0, (Visual.OpacityProperty, 0.0)), Frame(1.0, (Visual.OpacityProperty, 1.0)) }
            };
            _ = questionAnim.RunAsync(_questionContentGrid);

            // Answers staggered fade in
            var answersAnim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                Delay = TimeSpan.FromMilliseconds(200),
                Easing = new QuadraticEaseOut(),
                FillMode = FillMode.Forward,
                Children = { Frame(0.0, (Visual.OpacityProperty, 0.0)), Frame(1.0, (Visual.OpacityProperty, 1.0)) }
            };
            _ = answersAnim.RunAsync(_answersPanel);
        }

        // ============ EVENT HANDLERS ============

        private async void DynamicCategoryButton_Click(object? sender, PointerPressedEventArgs e)
        {
            if (_isProcessing) return;

            if (sender is not Control border || border.Tag is not QuizCategoryDefinition catDef) return;

            _isProcessing = true;
            _answerHistory.Clear();
            ShowLoading("Preparing your quiz...");

            try
            {
                var question = await StartQuizAsync(catDef);
                if (question != null)
                {
                    ShowQuestion(question);
                }
                else
                {
                    ShowError(QuizGenerationFailedMessage());
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "QuizWindow: Failed to start quiz");
                ShowError("Something went wrong starting the quiz. Please try again.");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async void Answer_Click(object? sender, PointerPressedEventArgs e)
        {
            if (_isProcessing || _currentQuestion == null) return;

            // Surrender easter egg intercept — before any answer recording
            if (_isSurrenderEasterEgg)
            {
                _isSurrenderEasterEgg = false;
                _isProcessing = true;
                SetAnswersEnabled(false);
                try
                {
                    await HandleSurrenderClickAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "QuizWindow: Surrender easter egg failed");
                    try { ExitSurrenderMode(); } catch { }
                    if (_savedNextQuestion != null)
                    {
                        ShowQuestion(_savedNextQuestion);
                        _savedNextQuestion = null;
                    }
                }
                _isProcessing = false;
                return;
            }

            if (sender is not Control border || border.Tag == null) return;

            var answerIndex = int.Parse(border.Tag.ToString()!);
            var points = _currentQuestion.Points[answerIndex];

            // Record this answer for history
            _answerHistory.Add(new QuizAnswerRecord
            {
                QuestionNumber = _currentQuestion.Number,
                QuestionText = _currentQuestion.QuestionText,
                AllAnswers = (string[])_currentQuestion.Answers.Clone(),
                AllPoints = (int[])_currentQuestion.Points.Clone(),
                ChosenIndex = answerIndex,
                PointsEarned = points
            });

            _isProcessing = true;
            SetAnswersEnabled(false);

            // Flash the selected answer
            await FlashSelectedAnswer(border);
            if (_isTrickQuestion)
            {
                _isTrickQuestion = false;
                PlayGoodGirl();
            }
            else
            {
                PlayRandomChime();
            }
            // ponytail: TriggerRandomEffect - App.Flash.TriggerFlashOnce / App.Bubbles.SpawnOnce /
            // App.Subliminal.FlashSubliminal for the four-way roll, plus App.MindWipe.AudioFileCount
            // and TriggerOnce for the independent ~25% mindwipe. All four are desktop-wide overlay
            // windows on ConditioningControlPanel/App.xaml.cs with no seam and no Avalonia twin;
            // porting one branch alone would make the roll silently uneven rather than absent.

            var questionNum = _questionNumber;

            try
            {
                if (questionNum >= 10)
                {
                    // Last question - get result
                    ShowLoading("Analyzing your personality...");
                    var result = await SubmitFinalAnswerAndGetResultAsync(answerIndex, points);
                    if (result != null)
                    {
                        ShowResult(result);
                    }
                    else
                    {
                        ShowError("Couldn't generate your result. Please try again.");
                    }
                }
                else
                {
                    // Get next question
                    ShowLoading();
                    var nextQuestion = await SubmitAnswerAndGetNextAsync(answerIndex, points);
                    if (nextQuestion != null)
                    {
                        // Easter egg: ~2% chance surrender screen (checked first, takes priority)
                        if (_random.Next(50) == 0)
                        {
                            _savedNextQuestion = nextQuestion;
                            _isSurrenderEasterEgg = true;
                            try
                            {
                                EnterSurrenderMode();
                                return; // finally block sets _isProcessing = false
                            }
                            catch (Exception ex2)
                            {
                                Log.Error(ex2, "QuizWindow: EnterSurrenderMode failed");
                                _isSurrenderEasterEgg = false;
                                _savedNextQuestion = null;
                                // Fall through to show the real question normally
                            }
                        }

                        // Easter egg: ~5% chance to replace with trick question
                        if (_random.Next(20) == 0)
                        {
                            nextQuestion = CreateTrickQuestion(nextQuestion.Number);
                            _isTrickQuestion = true;
                        }
                        ShowQuestion(nextQuestion);
                    }
                    else
                    {
                        ShowError("Couldn't generate the next question. The AI might be unavailable.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "QuizWindow: Failed to process answer");
                ShowError("Something went wrong. Please try again.");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private static async Task FlashSelectedAnswer(Control border)
        {
            if (border is Border b)
            {
                b.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
            }
            await Task.Delay(500);
        }

        private void SetAnswersEnabled(bool enabled)
        {
            var opacity = enabled ? 1.0 : 0.5;
            foreach (var answer in new[] { _answerA, _answerB, _answerC, _answerD })
            {
                answer.IsHitTestVisible = enabled;
                answer.Opacity = opacity;
            }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            try { BeginMoveDrag(e); } catch { }
        }

        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                _btnMaximizeTitleBar.Content = "☐";
            }
            else
            {
                WindowState = WindowState.Maximized;
                _btnMaximizeTitleBar.Content = "❐";
            }
        }

        private void BtnPlayAgain_Click()
        {
            // ponytail: needs QuizService.Reset() from
            // ConditioningControlPanel/Services/Quiz/QuizService.cs to clear the AI conversation
            // history; the local counters below are its stand-in for everything this window owns.
            _totalScore = 0;
            _questionNumber = 0;
            _currentCategoryDefinition = null;
            _currentQuestion = null;
            _isSurrenderEasterEgg = false;
            _savedNextQuestion = null;
            ShowPanel(_categorySelectPanel);
        }

        // ============ HOVER EFFECTS ============

        private void CategoryButton_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            }
        }

        private void CategoryButton_PointerExited(object? sender, PointerEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            }
        }

        private void Answer_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is Border border && border.IsHitTestVisible)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
            }
        }

        private void Answer_PointerExited(object? sender, PointerEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            }
        }

        // ============ LOADING ANIMATION ============

        private void LoadingDotsTimer_Tick(object? sender, EventArgs e)
        {
            _loadingDotCount = (_loadingDotCount + 1) % 4;
            _txtLoadingDots.Text = Loc.Get("label_generating_3") + new string('.', _loadingDotCount);
        }

        // ============ AUDIO ============

        /// <summary>
        /// The WPF body against the seam: <c>App.Audio.PlayOneShot</c> is
        /// <see cref="CoreAudio.PlayOneShot"/> and <c>App.Settings.Current.MasterVolume</c> is
        /// <see cref="CoreSettings"/>. Silent on this head today for the two reasons that are both
        /// the WPF no-op branch: Resources/sounds is Content in the WPF head and is not laid down
        /// beside CCP.Avalonia, so <c>File.Exists</c> misses; and nothing seeds
        /// <c>CoreAudio.PlayOneShotProvider</c>, so the seam fires its finished callback and
        /// returns. Both are honest silence, not a lie about playback.
        /// </summary>
        private static void PlaySound(string fileName, float multiplier)
        {
            try
            {
                var path = IOPath.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", fileName);
                if (!System.IO.File.Exists(path)) return;
                CoreAudio.PlayOneShot(path, GetVolume(multiplier), "quiz-sfx");
            }
            catch (Exception ex)
            {
                Log.Debug("QuizWindow: sound {File} failed: {Error}", fileName, ex.Message);
            }
        }

        /// <summary>Master volume on the app's ^1.5 perceptual curve - the WPF formula verbatim.</summary>
        private static float GetVolume(float multiplier)
        {
            var master = CoreSettings.Current.MasterVolume / 100f;
            return (float)Math.Pow(master * multiplier, 1.5);
        }

        private static void PlayRandomGiggle() => PlaySound(GiggleFiles[_random.Next(GiggleFiles.Length)], 0.5f);

        private static void PlayRandomChime() => PlaySound(ChimeFiles[_random.Next(ChimeFiles.Length)], 0.5f);

        private static void PlayGoodGirl() => PlaySound("GOOD GIRL.mp3", 0.5f);

        private static void PlayResultSound() => PlaySound("result.mp3", 1f);

        // ============ QUIZ SERVICE STUBS ============

        /// <summary>
        /// ponytail: needs QuizService.GetAllCategories() from
        /// ConditioningControlPanel/Services/Quiz/QuizService.cs - GetBuiltInCategories() (five
        /// definitions carrying ~600 lines of system prompt) plus LoadCustomCategories() over
        /// custom_quiz_categories.json. Both head-side. The placeholder below keeps the same shape;
        /// the last entry is deliberately NOT built-in so the "Edit" affordance is exercised too.
        /// </summary>
        private static List<QuizCategoryDefinition> GetAllCategories() => new()
        {
            new() { Id = "sissy", Name = "Sissy", Description = "How far down the pink road are you?", Color = "#FF69B4", IsBuiltIn = true },
            new() { Id = "bambi", Name = "Bambi", Description = "Wide eyes, empty head, happy girl.", Color = "#E91E63", IsBuiltIn = true },
            new() { Id = "obedience", Name = "Obedience", Description = "How quickly do you stop asking why?", Color = "#9B59B6", IsBuiltIn = true },
            new() { Id = "mindlessness", Name = "Mindlessness", Description = "What is left when the thinking stops?", Color = "#3498DB", IsBuiltIn = true },
            new() { Id = "submission", Name = "Submission", Description = "Who is holding the leash today?", Color = "#E67E22", IsBuiltIn = true },
            new() { Id = "custom_sample", Name = "Velvet Fog", Description = "A slow, warm slide into agreeable emptiness.", Color = "#2ECC71", IsBuiltIn = false },
        };

        /// <summary>
        /// ponytail: needs QuizService.StartQuizAsync(catDef) from
        /// ConditioningControlPanel/Services/Quiz/QuizService.cs - the AI round trip (proxy or
        /// Ollama, wrapped by SafetyComposer and screened by the moderation layer) that returns
        /// question 1. Head-side, so this returns null, which is the WPF "couldn't generate" path:
        /// the error panel and its provider-aware copy stay exercised.
        ///
        /// <para>The bookkeeping AROUND the round trip is the service's own and is restored here,
        /// so the flow is right the moment a real question arrives: StartQuizAsync zeroes TotalScore
        /// and sets QuestionNumber to 1 as it hands back question 1. Leaving QuestionNumber at 0,
        /// as the first cut did, puts the "last question" test one answer late.</para>
        /// </summary>
        private Task<QuizQuestion?> StartQuizAsync(QuizCategoryDefinition catDef)
        {
            _currentCategoryDefinition = catDef;
            _totalScore = 0;
            _questionNumber = 1;
            return Task.FromResult<QuizQuestion?>(null);
        }

        /// <summary>ponytail: needs QuizService.SubmitAnswerAndGetNextAsync from
        /// ConditioningControlPanel/Services/Quiz/QuizService.cs. The scoring and the question
        /// counter below are the service's, verbatim.</summary>
        private Task<QuizQuestion?> SubmitAnswerAndGetNextAsync(int answerIndex, int points)
        {
            _ = answerIndex;
            if (_questionNumber >= 10) return Task.FromResult<QuizQuestion?>(null);
            _totalScore += points;
            _questionNumber++;
            return Task.FromResult<QuizQuestion?>(null);
        }

        /// <summary>ponytail: needs QuizService.SubmitFinalAnswerAndGetResultAsync from
        /// ConditioningControlPanel/Services/Quiz/QuizService.cs - the second AI round trip that
        /// writes the archetype profile.</summary>
        private Task<QuizResult?> SubmitFinalAnswerAndGetResultAsync(int answerIndex, int points)
        {
            _ = answerIndex;
            _totalScore += points;
            return Task.FromResult<QuizResult?>(null);
        }

        private static QuizQuestion CreateTrickQuestion(int number)
        {
            var (question, answer) = TrickQuestions[_random.Next(TrickQuestions.Length)];
            return new QuizQuestion
            {
                Number = number,
                QuestionText = question,
                Answers = new[] { answer, answer, answer, answer },
                Points = new[] { 4, 4, 4, 4 }
            };
        }

        // ============ SURRENDER EASTER EGG ============

        private void EnterSurrenderMode()
        {
            // Duck audio heavily. WPF also muted the drone here; there is no drone on this head
            // (see the ctor note), so there is nothing to mute.
            _surrenderDuckGen = CoreAudio.DuckGeneration;
            CoreAudio.Duck(95);

            // Stop timers
            _loadingDotsTimer.Stop();
            _gradientTimer.Stop();

            // Set deep red/black background
            if (_bgStop0 != null) _bgStop0.Color = Color.FromRgb(0x40, 0x00, 0x00);
            if (_bgStop1 != null) _bgStop1.Color = Color.FromRgb(0x20, 0x00, 0x00);
            if (_bgStop2 != null) _bgStop2.Color = Color.FromRgb(0x0A, 0x00, 0x00);

            // Set ominous question text
            _txtQuestion.Text = Loc.Get("label_do_you_surrender_completely");
            _txtQuestion.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x20, 0x20));

            // Hide answers B, C, D
            _answerB.IsVisible = false;
            _answerC.IsVisible = false;
            _answerD.IsVisible = false;

            // Restyle answer A as a giant "YES" button
            if (_answerA.Child is Grid answerAGrid && answerAGrid.Children.Count > 0
                && answerAGrid.Children[0] is TextBlock letterLabel)
            {
                letterLabel.IsVisible = false;
            }
            _txtAnswerA.Text = Loc.Get("label_yes");
            _txtAnswerA.FontSize = 42;
            _txtAnswerA.FontWeight = FontWeight.ExtraBold;
            _txtAnswerA.Foreground = Brushes.White;
            _txtAnswerA.TextAlignment = TextAlignment.Center;
            _answerA.Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x00, 0x00));
            _answerA.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x20, 0x20));
            _answerA.Padding = new Thickness(20, 24, 20, 24);

            // Hide progress dots and score for cleaner look
            _progressDotsPanel.IsVisible = false;
            _scoreText.IsVisible = false;

            SetAnswersEnabled(true);
            ShowPanel(_questionPanel);
            AnimateQuestionIn();
        }

        private async Task HandleSurrenderClickAsync()
        {
            // Screen shake animation (~300ms)
            var transform = new TranslateTransform();
            _questionPanel.RenderTransform = transform;
            var shake = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                FillMode = FillMode.Forward,
                Children =
                {
                    Frame(0.0, (TranslateTransform.XProperty, 0.0)),
                    Frame(40.0 / 300, (TranslateTransform.XProperty, 8.0)),
                    Frame(80.0 / 300, (TranslateTransform.XProperty, -8.0)),
                    Frame(120.0 / 300, (TranslateTransform.XProperty, 6.0)),
                    Frame(160.0 / 300, (TranslateTransform.XProperty, -6.0)),
                    Frame(200.0 / 300, (TranslateTransform.XProperty, 4.0)),
                    Frame(240.0 / 300, (TranslateTransform.XProperty, -4.0)),
                    Frame(260.0 / 300, (TranslateTransform.XProperty, 2.0)),
                    Frame(1.0, (TranslateTransform.XProperty, 0.0)),
                }
            };
            _ = shake.RunAsync(transform);
            await Task.Delay(300);

            // Create "I KNOW" overlay
            var overlay = CreateSurrenderOverlay();
            _rootGrid.Children.Add(overlay);

            // Fade in
            overlay.Opacity = 0;
            var fadeIn = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(150),
                FillMode = FillMode.Forward,
                Children = { Frame(0.0, (Visual.OpacityProperty, 0.0)), Frame(1.0, (Visual.OpacityProperty, 1.0)) }
            };
            _ = fadeIn.RunAsync(overlay);
            await Task.Delay(1500);

            // Fade out (with timeout safety in case window closes mid-animation)
            var fadeOut = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                FillMode = FillMode.Forward,
                Children = { Frame(0.0, (Visual.OpacityProperty, 1.0)), Frame(1.0, (Visual.OpacityProperty, 0.0)) }
            };
            await Task.WhenAny(fadeOut.RunAsync(overlay), Task.Delay(500));

            try { _rootGrid.Children.Remove(overlay); } catch { }

            // Revert everything and show real question
            ExitSurrenderMode();

            if (_savedNextQuestion != null)
            {
                ShowQuestion(_savedNextQuestion);
                _savedNextQuestion = null;
            }
        }

        private void ExitSurrenderMode()
        {
            // Unduck audio (the drone volume restore has nothing to restore here).
            CoreAudio.Unduck(_surrenderDuckGen);

            // Restart gradient timer (resumes normal palette cycling)
            _gradientTimer.Start();

            // Restore question text color
            _txtQuestion.Foreground = Brushes.White;

            // Restore answers B, C, D
            _answerB.IsVisible = true;
            _answerC.IsVisible = true;
            _answerD.IsVisible = true;

            // Restore answer A styling
            if (_answerA.Child is Grid answerAGrid && answerAGrid.Children.Count > 0
                && answerAGrid.Children[0] is TextBlock letterLabel)
            {
                letterLabel.IsVisible = true;
            }
            _txtAnswerA.FontSize = 22;
            _txtAnswerA.FontWeight = FontWeight.Normal;
            _txtAnswerA.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xE0));
            _txtAnswerA.TextAlignment = TextAlignment.Left;
            _answerA.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            _answerA.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            _answerA.Padding = new Thickness(20, 16, 20, 16);

            // Restore progress dots and score
            _progressDotsPanel.IsVisible = true;
            _scoreText.IsVisible = true;

            // Clear shake transform
            _questionPanel.RenderTransform = null;
        }

        private static Grid CreateSurrenderOverlay()
        {
            var grid = new Grid
            {
                Background = Brushes.Black,
                IsHitTestVisible = true,
                ZIndex = 9999
            };
            Grid.SetRowSpan(grid, 2);

            var text = new TextBlock
            {
                Text = "I KNOW",
                FontSize = 120,
                FontWeight = FontWeight.ExtraBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(text);
            return grid;
        }

        // ============ CLEANUP ============

        /// <summary>
        /// Force close all quiz windows (used by panic button)
        /// </summary>
        public static void ForceCloseAll()
        {
            try
            {
                var lifetime = global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                foreach (var window in lifetime?.Windows.OfType<QuizWindow>().ToList() ?? new List<QuizWindow>())
                {
                    try { window.Close(); } catch { }
                }
            }
            catch { }
        }

        private void CleanupAndClose()
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            IsOpen = false;

            _loadingDotsTimer.Stop();
            _gradientTimer.Stop();
            // ponytail: needs the drone's WaveOutEvent/AudioFileReader pair to stop and dispose -
            // nothing starts one here yet (see the ctor note) - and AvatarWindow.SetMuteAvatar from
            // ConditioningControlPanel/Windows/AvatarWindow.xaml.cs to restore the avatar's
            // pre-quiz mute state. Both head-side.

            // Deliberate divergence from WPF: it ducks to 95 in EnterSurrenderMode and only unducks
            // in ExitSurrenderMode, so closing the window (Escape) while the surrender screen is up
            // leaves the whole app ducked with no window left to undo it. This layer is what brings
            // the duck to this head, so it does not bring that with it. Unduck is generation-scoped,
            // so a stale generation is a no-op.
            if (_isSurrenderEasterEgg) CoreAudio.Unduck(_surrenderDuckGen);

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// One generated question. Copied from ConditioningControlPanel/Services/Quiz/QuizService.cs:
    /// the type lives in the WPF head, not in CCP.Core, and neither may be touched by this port.
    /// ponytail: delete when the quiz types move to Core.
    /// </summary>
    public class QuizQuestion
    {
        public int Number { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string[] Answers { get; set; } = new string[4];
        public int[] Points { get; set; } = new int[4];
    }

    /// <inheritdoc cref="QuizQuestion"/>
    /// <remarks>Trimmed to the fields the result panel reads.</remarks>
    public class QuizResult
    {
        public QuizCategory Category { get; set; }
        public int TotalScore { get; set; }
        public int MaxScore { get; set; }
        public string ProfileText { get; set; } = string.Empty;
    }
}
