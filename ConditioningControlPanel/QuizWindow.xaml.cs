using System;
using System.Threading;
using IOPath = System.IO.Path;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using NAudio.Wave;

namespace ConditioningControlPanel
{
    public partial class QuizWindow : Window
    {
        /// <summary>WaveStream wrapper that loops the source indefinitely.</summary>
        private class LoopStream : WaveStream
        {
            private readonly WaveStream _source;
            public LoopStream(WaveStream source) => _source = source;
            public override WaveFormat WaveFormat => _source.WaveFormat;
            public override long Length => _source.Length;
            public override long Position
            {
                get => _source.Position;
                set => _source.Position = value;
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int totalRead = 0;
                while (totalRead < count)
                {
                    int read = _source.Read(buffer, offset + totalRead, count - totalRead);
                    if (read == 0)
                    {
                        if (_source.Position == 0) break; // empty source
                        _source.Position = 0;
                    }
                    totalRead += read;
                }
                return totalRead;
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) _source.Dispose();
                base.Dispose(disposing);
            }
        }

        private QuizService? _quizService;
        private QuizQuestion? _currentQuestion;
        private bool _isProcessing;
        private bool _isFullscreen;
        private bool _isTrickQuestion;
        private bool _isSurrenderEasterEgg;
        private QuizQuestion? _savedNextQuestion;
        private long _surrenderDuckGen;
        private readonly DispatcherTimer _loadingDotsTimer;
        private int _loadingDotCount;
        private readonly Ellipse[] _progressDots = new Ellipse[10];
        private List<QuizAnswerRecord> _answerHistory = new();

        private static readonly string[] LoadingFlavors = new[]
        {
            "The quiz master is thinking up something devious...",
            "Crafting the perfect question just for you...",
            "Analyzing your psyche...",
            "Preparing to read you like an open book...",
            "This one's going to be interesting...",
            "Calibrating the spice level...",
            "The deeper we go, the more revealing it gets...",
            "Almost there... don't get nervous...",
            "Your answers are telling a story...",
            "One moment while I think of something naughty..."
        };

        private static readonly Random _random = new();

        // Audio device pool (same pattern as BubbleService)
        private static readonly Queue<WaveOutEvent> _audioPool = new();
        private static readonly object _audioPoolLock = new();
        private const int MAX_POOLED_DEVICES = 2;

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

        // Looping drone audio
        private readonly bool _playDrone;
        private WaveOutEvent? _droneOutput;
        private LoopStream? _droneLoop;
        private AudioFileReader? _droneReader;

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

        public QuizWindow(bool fullscreen = true, bool playDrone = false)
        {
            InitializeComponent();
            _isFullscreen = fullscreen;
            _playDrone = playDrone;

            if (Application.Current.MainWindow is Window mainWin && mainWin.IsLoaded)
            {
                Owner = mainWin;
            }

            if (fullscreen)
            {
                WindowState = WindowState.Maximized;
                TitleBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                WindowState = WindowState.Normal;
                Topmost = false;
            }

            _loadingDotsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _loadingDotsTimer.Tick += LoadingDotsTimer_Tick;

            _gradientTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _gradientTimer.Tick += GradientTimer_Tick;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            BuildProgressDots();

            // Start glow pulse animation
            if (TryFindResource("GlowPulseStoryboard") is Storyboard glowSb)
            {
                glowSb.Begin();
            }

            // Initialize animated background gradient
            _bgStop0 = new GradientStop(_gradientPalette[0], 0.0);
            _bgStop1 = new GradientStop(_gradientPalette[2], 0.5);
            _bgStop2 = new GradientStop(_gradientPalette[4], 1.0);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(_bgStop0);
            brush.GradientStops.Add(_bgStop1);
            brush.GradientStops.Add(_bgStop2);
            BackgroundBorder.Background = brush;
            _gradientTimer.Start();

            if (_playDrone) StartDrone();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CleanupAndClose();
            }
        }

        private void BuildProgressDots()
        {
            ProgressDotsPanel.Children.Clear();
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
                ProgressDotsPanel.Children.Add(dot);
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
                    _progressDots[i].Fill = new SolidColorBrush(Colors.White);
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
            ScoreText.Text = $"Score: {score}";

            if (TryFindResource("ScorePulseStoryboard") is Storyboard sb)
            {
                sb.Begin();
            }
        }

        // ============ STATE TRANSITIONS ============

        private void ShowPanel(FrameworkElement panel)
        {
            CategorySelectPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            QuestionPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;

            panel.Visibility = Visibility.Visible;
        }

        private void ShowLoading(string? flavorText = null)
        {
            _loadingDotCount = 0;
            TxtLoadingDots.Text = "Generating";
            TxtLoadingFlavor.Text = flavorText ?? LoadingFlavors[_random.Next(LoadingFlavors.Length)];
            _loadingDotsTimer.Start();
            ShowPanel(LoadingPanel);
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
            UpdateScore(_quizService?.TotalScore ?? 0);

            ShuffleAnswers(question);
            TxtQuestion.Text = question.QuestionText;
            TxtAnswerA.Text = question.Answers[0];
            TxtAnswerB.Text = question.Answers[1];
            TxtAnswerC.Text = question.Answers[2];
            TxtAnswerD.Text = question.Answers[3];

            SetAnswersEnabled(true);
            ShowPanel(QuestionPanel);

            // Animate question in
            AnimateQuestionIn();
        }

        private void ShowResult(QuizResult result)
        {
            _loadingDotsTimer.Stop();

            // Save to quiz history
            try
            {
                var entry = new QuizHistoryEntry
                {
                    TakenAt = DateTime.Now,
                    Category = result.Category,
                    TotalScore = result.TotalScore,
                    MaxScore = result.MaxScore,
                    ProfileText = result.ProfileText,
                    Answers = new List<QuizAnswerRecord>(_answerHistory)
                };
                QuizService.SaveEntry(entry);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "QuizWindow: Failed to save quiz history");
            }

            TxtFinalScore.Text = $"{result.TotalScore} / {result.MaxScore}";

            var percentage = result.MaxScore > 0 ? (double)result.TotalScore / result.MaxScore * 100 : 0;
            TxtScoreLabel.Text = percentage switch
            {
                >= 90 => "You're completely hopeless (in the best way)",
                >= 75 => "You're in deep and loving it",
                >= 60 => "You know exactly who you are",
                >= 40 => "Curious little thing, aren't you?",
                >= 20 => "Just testing the waters... for now",
                _ => "Maybe next time you'll be more honest"
            };

            TxtProfileText.Text = result.ProfileText;

            ShowPanel(ResultPanel);
            PlayResultSound();
        }

        private void ShowError(string message)
        {
            _loadingDotsTimer.Stop();
            TxtError.Text = message;
            ShowPanel(ErrorPanel);
        }

        // ============ ANIMATIONS ============

        private void GradientTimer_Tick(object? sender, EventArgs e)
        {
            _gradientPhase += 0.008;

            // Slowly rotate the gradient angle
            var angle = _gradientPhase * 0.3;
            if (BackgroundBorder.Background is LinearGradientBrush brush)
            {
                brush.StartPoint = new Point(0.5 + 0.5 * Math.Cos(angle), 0.5 + 0.5 * Math.Sin(angle));
                brush.EndPoint = new Point(0.5 - 0.5 * Math.Cos(angle), 0.5 - 0.5 * Math.Sin(angle));
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
            QuestionContentGrid.Opacity = 0;
            AnswersPanel.Opacity = 0;

            // Question fade in
            var questionAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            QuestionContentGrid.BeginAnimation(OpacityProperty, questionAnim);

            // Answers staggered fade in
            var answersAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            AnswersPanel.BeginAnimation(OpacityProperty, answersAnim);
        }

        // ============ EVENT HANDLERS ============

        private async void CategoryButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isProcessing) return;

            var border = sender as FrameworkElement;
            var tag = border?.Tag?.ToString();
            if (tag == null) return;

            if (!Enum.TryParse<QuizCategory>(tag, out var category)) return;

            _isProcessing = true;
            _answerHistory.Clear();
            ShowLoading("Preparing your quiz...");

            _quizService?.Dispose();
            _quizService = new QuizService();

            try
            {
                var question = await _quizService.StartQuizAsync(category);
                if (question != null)
                {
                    ShowQuestion(question);
                }
                else
                {
                    ShowError("Couldn't generate the quiz. The AI might be busy or you've hit your daily limit. Try again in a moment.");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "QuizWindow: Failed to start quiz");
                ShowError("Something went wrong starting the quiz. Please try again.");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async void Answer_Click(object sender, MouseButtonEventArgs e)
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
                    App.Logger?.Error(ex, "QuizWindow: Surrender easter egg failed");
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

            var border = sender as FrameworkElement;
            if (border?.Tag == null) return;

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
            await FlashSelectedAnswer(border, answerIndex);
            if (_isTrickQuestion)
            {
                _isTrickQuestion = false;
                PlayGoodGirl();
            }
            else
            {
                PlayRandomChime();
            }
            TriggerRandomEffect();

            var questionNum = _quizService?.QuestionNumber ?? 0;

            try
            {
                if (questionNum >= 10)
                {
                    // Last question - get result
                    ShowLoading("Analyzing your personality...");
                    var result = await _quizService!.SubmitFinalAnswerAndGetResultAsync(answerIndex, points);
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
                    var nextQuestion = await _quizService!.SubmitAnswerAndGetNextAsync(answerIndex, points);
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
                                App.Logger?.Error(ex2, "QuizWindow: EnterSurrenderMode failed");
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
                App.Logger?.Error(ex, "QuizWindow: Failed to process answer");
                ShowError("Something went wrong. Please try again.");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task FlashSelectedAnswer(FrameworkElement border, int index)
        {
            if (border is System.Windows.Controls.Border b)
            {
                b.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
            }
            await Task.Delay(500);
        }

        private void SetAnswersEnabled(bool enabled)
        {
            var opacity = enabled ? 1.0 : 0.5;
            AnswerA.IsHitTestVisible = enabled;
            AnswerB.IsHitTestVisible = enabled;
            AnswerC.IsHitTestVisible = enabled;
            AnswerD.IsHitTestVisible = enabled;
            AnswerA.Opacity = opacity;
            AnswerB.Opacity = opacity;
            AnswerC.Opacity = opacity;
            AnswerD.Opacity = opacity;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void BtnCloseTitleBar_Click(object sender, RoutedEventArgs e)
        {
            CleanupAndClose();
        }

        private void BtnPlayAgain_Click(object sender, RoutedEventArgs e)
        {
            _quizService?.Reset();
            _currentQuestion = null;
            _isSurrenderEasterEgg = false;
            _savedNextQuestion = null;
            ShowPanel(CategorySelectPanel);
        }

        private void BtnCloseResult_Click(object sender, RoutedEventArgs e)
        {
            CleanupAndClose();
        }

        // ============ HOVER EFFECTS ============

        private void CategoryButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            }
        }

        private void CategoryButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            }
        }

        private void Answer_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border && border.IsHitTestVisible)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4));
            }
        }

        private void Answer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            }
        }

        // ============ LOADING ANIMATION ============

        private void LoadingDotsTimer_Tick(object? sender, EventArgs e)
        {
            _loadingDotCount = (_loadingDotCount + 1) % 4;
            TxtLoadingDots.Text = "Generating" + new string('.', _loadingDotCount);
        }

        // ============ AUDIO ============

        private static WaveOutEvent GetPooledDevice()
        {
            lock (_audioPoolLock)
            {
                if (_audioPool.Count > 0)
                    return _audioPool.Dequeue();
            }
            return new WaveOutEvent();
        }

        private static void ReturnDevice(WaveOutEvent device)
        {
            lock (_audioPoolLock)
            {
                if (_audioPool.Count < MAX_POOLED_DEVICES)
                    _audioPool.Enqueue(device);
                else
                    device.Dispose();
            }
        }

        private static void PlaySoundAsync(string path, float volume)
        {
            Task.Run(() =>
            {
                WaveOutEvent? outputDevice = null;
                AudioFileReader? audioFile = null;
                try
                {
                    audioFile = new AudioFileReader(path) { Volume = volume };
                    outputDevice = GetPooledDevice();
                    outputDevice.Init(audioFile);
                    outputDevice.Play();

                    while (outputDevice.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Quiz audio playback failed: {Error}", ex.Message);
                }
                finally
                {
                    audioFile?.Dispose();
                    if (outputDevice != null)
                    {
                        try { outputDevice.Stop(); } catch { }
                        ReturnDevice(outputDevice);
                    }
                }
            });
        }

        private static float GetVolume(float multiplier = 1f)
        {
            var master = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
            return (float)Math.Pow(master * multiplier, 1.5);
        }

        private static void PlayRandomGiggle()
        {
            var soundsPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds");
            var file = GiggleFiles[_random.Next(GiggleFiles.Length)];
            var path = IOPath.Combine(soundsPath, file);
            if (System.IO.File.Exists(path))
                PlaySoundAsync(path, GetVolume(0.5f));
        }

        private static void PlayRandomChime()
        {
            var soundsPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds");
            var file = ChimeFiles[_random.Next(ChimeFiles.Length)];
            var path = IOPath.Combine(soundsPath, file);
            if (System.IO.File.Exists(path))
                PlaySoundAsync(path, GetVolume(0.5f));
        }

        private static void TriggerRandomEffect()
        {
            try
            {
                // Primary effect: pick one at random
                switch (_random.Next(4))
                {
                    case 0: // Flash burst from active image set
                        App.Flash?.TriggerFlashOnce();
                        break;
                    case 1: // Bubble burst (2-3 bubbles)
                        var bubbleCount = _random.Next(2, 4);
                        for (int i = 0; i < bubbleCount; i++)
                            App.Bubbles?.SpawnOnce();
                        break;
                    case 2: // Subliminal from active pool
                        App.Subliminal?.FlashSubliminal();
                        break;
                    // case 3: nothing — keeps it unpredictable
                }

                // Independent mindwipe roll (~25%)
                if (_random.Next(4) == 0 && (App.MindWipe?.AudioFileCount ?? 0) > 0)
                    App.MindWipe!.TriggerOnce();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("TriggerRandomEffect failed: {Error}", ex.Message);
            }
        }

        private void StartDrone()
        {
            try
            {
                var path = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "00 Bimbo Drone.mp3");
                if (!System.IO.File.Exists(path)) return;

                _droneReader = new AudioFileReader(path) { Volume = GetVolume(0.35f) };
                _droneLoop = new LoopStream(_droneReader);
                _droneOutput = new WaveOutEvent();
                _droneOutput.Init(_droneLoop);
                _droneOutput.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Quiz drone playback failed: {Error}", ex.Message);
                StopDrone();
            }
        }

        private void StopDrone()
        {
            try { _droneOutput?.Stop(); } catch { }
            _droneOutput?.Dispose();
            _droneOutput = null;
            // LoopStream.Dispose cascades to _droneReader, so dispose loop only
            _droneLoop?.Dispose();
            _droneLoop = null;
            _droneReader = null;
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
            // Duck audio heavily + mute drone if playing
            _surrenderDuckGen = App.Audio?.DuckGeneration ?? 0;
            App.Audio?.Duck(95);
            if (_droneOutput != null && _droneReader != null)
                _droneReader.Volume = 0f;

            // Stop timers
            _loadingDotsTimer.Stop();
            _gradientTimer.Stop();

            // Set deep red/black background
            if (_bgStop0 != null) _bgStop0.Color = Color.FromRgb(0x40, 0x00, 0x00);
            if (_bgStop1 != null) _bgStop1.Color = Color.FromRgb(0x20, 0x00, 0x00);
            if (_bgStop2 != null) _bgStop2.Color = Color.FromRgb(0x0A, 0x00, 0x00);

            // Set ominous question text
            TxtQuestion.Text = "Do you surrender completely?";
            TxtQuestion.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x20, 0x20));

            // Hide answers B, C, D
            AnswerB.Visibility = Visibility.Collapsed;
            AnswerC.Visibility = Visibility.Collapsed;
            AnswerD.Visibility = Visibility.Collapsed;

            // Restyle answer A as a giant "YES" button
            if (AnswerA.Child is Grid answerAGrid && answerAGrid.Children.Count > 0
                && answerAGrid.Children[0] is TextBlock letterLabel)
            {
                letterLabel.Visibility = Visibility.Collapsed;
            }
            TxtAnswerA.Text = "YES";
            TxtAnswerA.FontSize = 42;
            TxtAnswerA.FontWeight = FontWeights.ExtraBold;
            TxtAnswerA.Foreground = new SolidColorBrush(Colors.White);
            TxtAnswerA.TextAlignment = TextAlignment.Center;
            AnswerA.Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x00, 0x00));
            AnswerA.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x20, 0x20));
            AnswerA.Padding = new Thickness(20, 24, 20, 24);

            // Hide progress dots and score for cleaner look
            ProgressDotsPanel.Visibility = Visibility.Collapsed;
            ScoreText.Visibility = Visibility.Collapsed;

            SetAnswersEnabled(true);
            ShowPanel(QuestionPanel);
            AnimateQuestionIn();
        }

        private async Task HandleSurrenderClickAsync()
        {
            // Screen shake animation (~300ms)
            var transform = new TranslateTransform();
            QuestionPanel.RenderTransform = transform;
            var shakeAnim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(300)
            };
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240))));
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
            shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            transform.BeginAnimation(TranslateTransform.XProperty, shakeAnim);
            await Task.Delay(300);

            // Create "I KNOW" overlay
            var overlay = CreateSurrenderOverlay();
            if (Content is not Grid rootGrid)
            {
                ExitSurrenderMode();
                return;
            }
            rootGrid.Children.Add(overlay);

            // Fade in
            overlay.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            overlay.BeginAnimation(OpacityProperty, fadeIn);
            await Task.Delay(1500);

            // Fade out (with timeout safety in case window closes mid-animation)
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var tcs = new TaskCompletionSource<bool>();
            fadeOut.Completed += (_, _) => tcs.TrySetResult(true);
            overlay.BeginAnimation(OpacityProperty, fadeOut);
            await Task.WhenAny(tcs.Task, Task.Delay(500));

            try { rootGrid.Children.Remove(overlay); } catch { }

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
            // Unduck audio + restore drone volume
            App.Audio?.Unduck(_surrenderDuckGen);
            if (_droneOutput != null && _droneReader != null)
                _droneReader.Volume = GetVolume(0.35f);

            // Restart gradient timer (resumes normal palette cycling)
            _gradientTimer.Start();

            // Restore question text color
            TxtQuestion.Foreground = new SolidColorBrush(Colors.White);

            // Restore answers B, C, D
            AnswerB.Visibility = Visibility.Visible;
            AnswerC.Visibility = Visibility.Visible;
            AnswerD.Visibility = Visibility.Visible;

            // Restore answer A styling
            if (AnswerA.Child is Grid answerAGrid && answerAGrid.Children.Count > 0
                && answerAGrid.Children[0] is TextBlock letterLabel)
            {
                letterLabel.Visibility = Visibility.Visible;
            }
            TxtAnswerA.FontSize = 22;
            TxtAnswerA.FontWeight = FontWeights.Normal;
            TxtAnswerA.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xE0));
            TxtAnswerA.TextAlignment = TextAlignment.Left;
            AnswerA.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            AnswerA.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            AnswerA.Padding = new Thickness(20, 16, 20, 16);

            // Restore progress dots and score
            ProgressDotsPanel.Visibility = Visibility.Visible;
            ScoreText.Visibility = Visibility.Visible;

            // Clear shake transform
            QuestionPanel.RenderTransform = null;
        }

        private static Grid CreateSurrenderOverlay()
        {
            var grid = new Grid
            {
                Background = new SolidColorBrush(Colors.Black),
                IsHitTestVisible = true
            };
            Grid.SetRowSpan(grid, 2);
            Panel.SetZIndex(grid, 9999);

            var text = new TextBlock
            {
                Text = "I KNOW",
                FontSize = 120,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(text);
            return grid;
        }

        private static void PlayGoodGirl()
        {
            var path = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "GOOD GIRL.mp3");
            if (System.IO.File.Exists(path))
                PlaySoundAsync(path, GetVolume(0.5f));
        }

        private static void PlayResultSound()
        {
            var soundsPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds");
            var path = IOPath.Combine(soundsPath, "result.mp3");
            if (System.IO.File.Exists(path))
                PlaySoundAsync(path, GetVolume());
        }

        // ============ CLEANUP ============

        private void CleanupAndClose()
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _loadingDotsTimer.Stop();
            _gradientTimer.Stop();
            StopDrone();
            _quizService?.Dispose();
            _quizService = null;
            base.OnClosed(e);
        }
    }
}
