using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using WpfPoint = System.Windows.Point;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Lab.GazeMinigame
{
    /// <summary>
    /// First playable Lab minigame. Side-by-side image/video pairs; user must
    /// hold gaze on the "correct" pack content. Looking at the noise side too
    /// long → WRONG flash + beep. Holding correct for full asset duration →
    /// GOOD GIRL flash. Self-contained: owns its packs, settings, playback,
    /// and result tracking. Owner=MainWindow handles process lifecycle.
    /// </summary>
    public partial class GazeMinigameWindow : System.Windows.Window
    {
        private enum GameSide { None, Left, Right }
        private enum AssetType { Image, Video }
        private enum RoundOutcome { Correct, Wrong, Timeout }

        private sealed record RoundSpec(AssetType Type, string CorrectPath, string NoisePath,
                                        GameSide CorrectSide, int DurationSec);

        private sealed class RoundResult
        {
            public int Index;
            public AssetType Type;
            public RoundOutcome Outcome;
            public double CorrectMs;
            public double WrongMs;
        }

        private readonly List<AssetPack> _packs = new();
        private GazeMinigameSettings _settings = new();

        private List<RoundSpec> _rounds = new();
        private readonly List<RoundResult> _results = new();
        private int _currentRoundIdx = -1;
        private DateTime _roundStartedAt;
        // Grace window at the start of each round: gaze accumulation AND the
        // duration timeout are both paused until UtcNow >= this. Lets the user
        // visually find the correct asset if their pre-round gaze happened to
        // land on the noise side (especially under WrongHoldMs=0 strict mode
        // where one frame on the wrong side instantly fails the round).
        private DateTime _roundIgnoreGazeUntil;
        private const int GraceMs = 1000;
        private double _correctMs;
        private double _wrongMs;
        private GameSide _currentSide = GameSide.None;
        private bool _faceLost;
        private DispatcherTimer? _roundTicker;

        private VlcMediaPlayer? _leftPlayer, _rightPlayer;
        private VideoView? _leftVideoView, _rightVideoView;
        // Per-player "is this player still owned by the active round?" flags.
        // Captured by the Playing-event lambda so a late-firing event (after
        // we've moved on to the next round and started disposing) no-ops
        // instead of touching a disposed native MediaPlayer (→ AV crash).
        private bool[]? _leftPlayerAlive, _rightPlayerAlive;

        private bool _gameRunning;
        private bool _webcamSubscribed;

        // Saved windowed state so ToggleFullscreen() can restore cleanly.
        private WindowStyle _savedStyle;
        private WindowState _savedState;
        private ResizeMode _savedResize;
        private bool _isFullscreen;

        public GazeMinigameWindow()
        {
            InitializeComponent();
            _settings = GazeMinigameSettings.Load();
            ApplySettingsToSliders();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Screen routing
        // ─────────────────────────────────────────────────────────────────────

        private void ShowScreen(Grid screen)
        {
            TitleScreen.Visibility = screen == TitleScreen ? Visibility.Visible : Visibility.Collapsed;
            ReadyScreen.Visibility = screen == ReadyScreen ? Visibility.Visible : Visibility.Collapsed;
            CountdownScreen.Visibility = screen == CountdownScreen ? Visibility.Visible : Visibility.Collapsed;
            GameplayScreen.Visibility = screen == GameplayScreen ? Visibility.Visible : Visibility.Collapsed;
            ResultsScreen.Visibility = screen == ResultsScreen ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Title screen
        // ─────────────────────────────────────────────────────────────────────

        private void BtnSelectAssets_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AssetPackSelectorDialog(_packs) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _packs.Clear();
                _packs.AddRange(dlg.SelectedPacks);
            }
            UpdateTitleScreen();
        }

        private void UpdateTitleScreen()
        {
            if (_packs.Count >= 2)
            {
                BtnGoToReady.IsEnabled = true;
                TxtTitleStatus.Text = $"{_packs.Count} packs loaded — first is correct, {_packs.Count - 1} noise.";
            }
            else
            {
                BtnGoToReady.IsEnabled = false;
                TxtTitleStatus.Text = "Pick at least 2 asset packs to begin.";
            }
        }

        private void BtnGoToReady_Click(object sender, RoutedEventArgs e)
        {
            BuildReadyRecap();
            HideReadyBanner();
            ShowScreen(ReadyScreen);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Ready screen
        // ─────────────────────────────────────────────────────────────────────

        private void ApplySettingsToSliders()
        {
            SliderImageCount.Value = _settings.ImageCount;
            SliderVideoCount.Value = _settings.VideoCount;
            SliderImageDur.Value = _settings.ImageDurationSec;
            SliderVideoDur.Value = _settings.VideoMaxDurationSec;
            SliderPassTime.Value = _settings.PassTimeSec;
            TxtImageCountVal.Text = _settings.ImageCount.ToString();
            TxtVideoCountVal.Text = _settings.VideoCount.ToString();
            TxtImageDurVal.Text = _settings.ImageDurationSec.ToString();
            TxtVideoDurVal.Text = _settings.VideoMaxDurationSec.ToString();
            TxtPassTimeVal.Text = _settings.PassTimeSec.ToString();
            UpdatePassTimeWarning();

            // Vibration combo
            SelectComboByTag(CboVibration, _settings.VibrationMode.ToString());
            UpdateVibrationStatus();

            // Reward effect combo
            SelectComboByTag(CboReward, _settings.RewardEffect.ToString());
            UpdateRewardAudioVisibility();

            // Audio file combo (populate from bundled list)
            CboRewardAudio.Items.Clear();
            foreach (var f in GazeMinigameSettings.BundledAudioFiles)
            {
                var item = new ComboBoxItem { Content = f, Tag = f };
                CboRewardAudio.Items.Add(item);
                if (string.Equals(f, _settings.RewardAudioFile, StringComparison.OrdinalIgnoreCase))
                    item.IsSelected = true;
            }
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            foreach (var obj in combo.Items)
            {
                if (obj is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    cbi.IsSelected = true;
                    return;
                }
            }
        }

        private void UpdateVibrationStatus()
        {
            if (TxtVibrationStatus == null) return;
            if (_settings.VibrationMode == GazeVibrationMode.None)
            {
                TxtVibrationStatus.Text = "";
                return;
            }
            TxtVibrationStatus.Text = (App.Haptics?.IsConnected == true)
                ? "Haptic device connected."
                : "Haptic device not connected — setting saved but no vibration will fire.";
        }

        private void UpdateRewardAudioVisibility()
        {
            if (RewardAudioRow != null)
                RewardAudioRow.Visibility = _settings.RewardEffect == GazeRewardEffect.Audio ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CboVibration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboVibration?.SelectedItem is ComboBoxItem cbi
                && Enum.TryParse<GazeVibrationMode>(cbi.Tag?.ToString(), out var mode))
            {
                _settings.VibrationMode = mode;
                UpdateVibrationStatus();
            }
        }

        private void CboReward_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboReward?.SelectedItem is ComboBoxItem cbi
                && Enum.TryParse<GazeRewardEffect>(cbi.Tag?.ToString(), out var effect))
            {
                _settings.RewardEffect = effect;
                UpdateRewardAudioVisibility();
            }
        }

        private void CboRewardAudio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboRewardAudio?.SelectedItem is ComboBoxItem cbi && cbi.Tag is string fileName)
                _settings.RewardAudioFile = fileName;
        }

        private void SliderImageCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { _settings.ImageCount = (int)e.NewValue; if (TxtImageCountVal != null) TxtImageCountVal.Text = _settings.ImageCount.ToString(); }
        private void SliderVideoCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { _settings.VideoCount = (int)e.NewValue; if (TxtVideoCountVal != null) TxtVideoCountVal.Text = _settings.VideoCount.ToString(); }
        private void SliderImageDur_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { _settings.ImageDurationSec = (int)e.NewValue; if (TxtImageDurVal != null) TxtImageDurVal.Text = _settings.ImageDurationSec.ToString(); UpdatePassTimeWarning(); }
        private void SliderVideoDur_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { _settings.VideoMaxDurationSec = (int)e.NewValue; if (TxtVideoDurVal != null) TxtVideoDurVal.Text = _settings.VideoMaxDurationSec.ToString(); UpdatePassTimeWarning(); }
        private void SliderPassTime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { _settings.PassTimeSec = (int)e.NewValue; if (TxtPassTimeVal != null) TxtPassTimeVal.Text = _settings.PassTimeSec.ToString(); UpdatePassTimeWarning(); }

        private void UpdatePassTimeWarning()
        {
            // Soft warning if the pass-time exceeds an asset's max display time:
            // the user literally can't pass that asset type before it times out.
            // Soft (not hard-clamp) so users can experiment freely.
            if (TxtPassTimeWarn == null) return;
            var problems = new System.Collections.Generic.List<string>();
            if (_settings.ImageCount > 0 && _settings.PassTimeSec > _settings.ImageDurationSec)
                problems.Add($"images ({_settings.ImageDurationSec}s)");
            if (_settings.VideoCount > 0 && _settings.PassTimeSec > _settings.VideoMaxDurationSec)
                problems.Add($"videos ({_settings.VideoMaxDurationSec}s)");
            if (problems.Count > 0)
            {
                TxtPassTimeWarn.Text = $"Pass time exceeds {string.Join(" and ", problems)} display time — those rounds will time out before you can pass.";
                TxtPassTimeWarn.Visibility = Visibility.Visible;
            }
            else
            {
                TxtPassTimeWarn.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildReadyRecap()
        {
            ReadyPackRecap.Children.Clear();
            for (int i = 0; i < _packs.Count; i++)
            {
                var p = _packs[i];
                var isCorrect = i == 0;
                var roleColor = isCorrect ? Color.FromRgb(0xFF, 0x69, 0xB4) : Color.FromRgb(0xFF, 0x80, 0x80);

                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x40, roleColor.R, roleColor.G, roleColor.B)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = isCorrect ? "CORRECT" : "noise",
                        Foreground = new SolidColorBrush(roleColor),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                    },
                });
                row.Children.Add(new TextBlock
                {
                    Text = p.Name,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{p.ImageCount} img · {p.VideoCount} vid",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                ReadyPackRecap.Children.Add(row);
            }
        }

        private void BtnReadyBack_Click(object sender, RoutedEventArgs e)
        {
            ShowScreen(TitleScreen);
        }

        private void ShowReadyBanner(string text, bool showCalibrateAction = false)
        {
            TxtReadyBanner.Text = text;
            BtnReadyBannerAction.Visibility = showCalibrateAction ? Visibility.Visible : Visibility.Collapsed;
            ReadyBanner.Visibility = Visibility.Visible;
        }

        private void HideReadyBanner()
        {
            ReadyBanner.Visibility = Visibility.Collapsed;
            BtnReadyBannerAction.Visibility = Visibility.Collapsed;
        }

        private void BtnReadyBannerAction_Click(object sender, RoutedEventArgs e)
        {
            // Currently the only banner action is "open calibration".
            var dlg = new WebcamCalibrationWindow { Owner = this };
            dlg.ShowDialog();
            HideReadyBanner();
        }

        private void BtnStartGame_Click(object sender, RoutedEventArgs e)
        {
            // Webcam preconditions, in order. Each failure stays on Ready with
            // a friendly banner — no modal interruption mid-flow.
            if (App.Settings?.Current?.WebcamConsentGiven != true)
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven)
                {
                    ShowReadyBanner("Camera consent is required for the gaze minigame.");
                    return;
                }
            }

            if (App.Webcam == null)
            {
                ShowReadyBanner("Webcam service is not available.");
                return;
            }

            if (!App.Webcam.IsRunning && !App.Webcam.Start())
            {
                ShowReadyBanner($"Couldn't start the webcam (state: {App.Webcam.State}). Check that no other app is using the camera.");
                return;
            }

            if (App.Webcam.Calibration == null)
            {
                ShowReadyBanner("No gaze calibration loaded yet. Run a 5-point calibration first so the minigame can tell which side you're looking at.", showCalibrateAction: true);
                return;
            }

            // All good — generate rounds, persist settings, advance.
            try
            {
                _rounds = GenerateRounds();
            }
            catch (InvalidOperationException ex)
            {
                ShowReadyBanner(ex.Message);
                return;
            }
            if (_rounds.Count == 0)
            {
                ShowReadyBanner("Set at least one image or video round before starting.");
                return;
            }

            _settings.Save();
            HideReadyBanner();
            BeginCountdown();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Round generation
        // ─────────────────────────────────────────────────────────────────────

        private List<RoundSpec> GenerateRounds()
        {
            var correct = _packs[0];
            var noise = _packs.Skip(1).ToList();
            var rng = Random.Shared;

            // Validate per-type availability up front so we can give a precise
            // banner message instead of failing mid-round.
            if (_settings.ImageCount > 0)
            {
                if (correct.ImageCount == 0)
                    throw new InvalidOperationException("The correct pack has no images. Pick a pack with images, or set Image count to 0.");
                if (noise.All(p => p.ImageCount == 0))
                    throw new InvalidOperationException("None of the noise packs have images. Add a noise pack with images, or set Image count to 0.");
            }
            if (_settings.VideoCount > 0)
            {
                if (correct.VideoCount == 0)
                    throw new InvalidOperationException("The correct pack has no videos. Pick a pack with videos, or set Video count to 0.");
                if (noise.All(p => p.VideoCount == 0))
                    throw new InvalidOperationException("None of the noise packs have videos. Add a noise pack with videos, or set Video count to 0.");
            }

            var specs = new List<RoundSpec>();
            for (int i = 0; i < _settings.ImageCount; i++)
                specs.Add(BuildSpec(AssetType.Image, correct, noise, rng));
            for (int i = 0; i < _settings.VideoCount; i++)
                specs.Add(BuildSpec(AssetType.Video, correct, noise, rng));

            // Shuffle so videos and images are interleaved.
            for (int i = specs.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (specs[i], specs[j]) = (specs[j], specs[i]);
            }
            return specs;
        }

        private RoundSpec BuildSpec(AssetType type, AssetPack correct, List<AssetPack> noise, Random rng)
        {
            var correctPaths = type == AssetType.Image ? correct.ImagePaths : correct.VideoPaths;
            // Pick a noise pack that has at least one of the needed type.
            var validNoise = noise.Where(p => (type == AssetType.Image ? p.ImageCount : p.VideoCount) > 0).ToList();
            var noisePack = validNoise[rng.Next(validNoise.Count)];
            var noisePaths = type == AssetType.Image ? noisePack.ImagePaths : noisePack.VideoPaths;

            var correctPath = correctPaths[rng.Next(correctPaths.Count)];
            var noisePath = noisePaths[rng.Next(noisePaths.Count)];
            var side = rng.Next(2) == 0 ? GameSide.Left : GameSide.Right;
            var dur = type == AssetType.Image ? _settings.ImageDurationSec : _settings.VideoMaxDurationSec;
            return new RoundSpec(type, correctPath, noisePath, side, dur);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Countdown
        // ─────────────────────────────────────────────────────────────────────

        private async void BeginCountdown()
        {
            // True fullscreen for the duration of gameplay so the gaze
            // half-mapping is unambiguous (window covers the whole monitor,
            // taskbar included → left half of window == left half of screen).
            // F11 toggles back during gameplay if the user wants windowed.
            EnterFullscreen();

            ShowScreen(CountdownScreen);
            EnsureWebcamSubscribed();

            foreach (var label in new[] { "3", "2", "1", "GO" })
            {
                TxtCountdown.Text = label;
                await System.Threading.Tasks.Task.Delay(700);
            }

            _results.Clear();
            _currentRoundIdx = -1;
            ShowScreen(GameplayScreen);
            AdvanceRound();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Gameplay loop
        // ─────────────────────────────────────────────────────────────────────

        private void AdvanceRound()
        {
            DisposeCurrentRoundPlayers();
            _currentRoundIdx++;
            if (_currentRoundIdx >= _rounds.Count)
            {
                EndSession();
                return;
            }

            var spec = _rounds[_currentRoundIdx];
            TxtRoundInfo.Text = $"Round {_currentRoundIdx + 1} / {_rounds.Count}  ({spec.Type}, {spec.DurationSec}s)";

            // Map "correct" / "noise" onto the actual left/right panes.
            var leftPath = spec.CorrectSide == GameSide.Left ? spec.CorrectPath : spec.NoisePath;
            var rightPath = spec.CorrectSide == GameSide.Left ? spec.NoisePath : spec.CorrectPath;

            LeftPane.Children.Clear();
            RightPane.Children.Clear();

            if (spec.Type == AssetType.Image)
            {
                var leftImg = BuildImageView(leftPath);
                var rightImg = BuildImageView(rightPath);
                LeftPane.Children.Add(leftImg);
                RightPane.Children.Add(rightImg);
                AnimateAssetIn(leftImg);
                AnimateAssetIn(rightImg);
                // Sparkle burst is image-only: on video rounds the LibVLC
                // VideoView is a native HWND that masks WPF children once it
                // starts rendering, so spawned sparkles would disappear the
                // moment the Playing event fires. Skip rather than spawn-and-hide.
                SpawnSparkleBurst(LeftPane);
                SpawnSparkleBurst(RightPane);
            }
            else
            {
                _leftVideoView = new VideoView { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                _rightVideoView = new VideoView { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                LeftPane.Children.Add(_leftVideoView);
                RightPane.Children.Add(_rightVideoView);
                AnimateAssetIn(_leftVideoView);
                AnimateAssetIn(_rightVideoView);

                _leftPlayerAlive = new[] { true };
                _rightPlayerAlive = new[] { true };
                _leftPlayer = StartVideo(_leftVideoView, leftPath, _leftPlayerAlive);
                _rightPlayer = StartVideo(_rightVideoView, rightPath, _rightPlayerAlive);
            }

            // Reset per-round state.
            _correctMs = 0;
            _wrongMs = 0;
            _currentSide = GameSide.None;
            _roundStartedAt = DateTime.UtcNow;
            _roundIgnoreGazeUntil = DateTime.UtcNow.AddMilliseconds(GraceMs);
            _gameRunning = true;
            StartRoundTicker();
        }

        private static System.Windows.Controls.Image BuildImageView(string path)
        {
            var img = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeMinigame: failed to load image {Path}", path);
            }
            return img;
        }

        private VlcMediaPlayer? StartVideo(VideoView view, string path, bool[] alive)
        {
            var libvlc = VideoService.SharedLibVLC;
            if (libvlc == null)
            {
                App.Logger?.Warning("GazeMinigame: VideoService.SharedLibVLC is null — cannot play videos");
                return null;
            }
            try
            {
                var media = new Media(libvlc, new Uri(path));
                var player = new VlcMediaPlayer(libvlc) { Volume = 0 };
                view.MediaPlayer = player;

                // Random offset for variety: jump to a position somewhere in the
                // first 50% of the video once playback has actually started.
                // The `alive` flag is flipped to false the instant we begin
                // disposing this player; checking it here avoids touching a
                // disposed native MediaPlayer when LibVLC's event thread fires
                // Playing late (a managed try/catch can't catch native AVs).
                player.Playing += (_, _) =>
                {
                    if (!alive[0]) return;
                    try
                    {
                        player.Position = (float)(Random.Shared.NextDouble() * 0.5);
                    }
                    catch { /* short videos may reject Position; ignore */ }
                };

                player.Play(media);
                media.Dispose();   // MediaPlayer holds its own ref
                return player;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeMinigame: failed to start video {Path}", path);
                return null;
            }
        }

        // Safe LibVLC teardown ordering (mirrors VideoService.CloseAll, see
        // Services/VideoService.cs:1898-2040). The hard-won lesson there:
        //   STOP first, then DETACH, then DISPOSE — with message-pump waits in
        //   between so LibVLC's UI-thread callbacks (rendering, frame-ready)
        //   can drain. Detaching VideoView.MediaPlayer while the player is
        //   still rendering crashes natively (esp. with two side-by-side
        //   videos sharing the dispatcher).
        //
        // synchronous=true is used by Window_Closing so disposal completes
        // before the AppDomain starts unloading native LibVLC libraries (the
        // 2026-04-26 01:45 DllNotFoundException was that exact race).
        private void DisposeCurrentRoundPlayers(bool synchronous = false)
        {
            // 1. Mark the players as no-longer-owned so any in-flight Playing
            //    event lambdas no-op instead of touching freed natives.
            if (_leftPlayerAlive != null) _leftPlayerAlive[0] = false;
            if (_rightPlayerAlive != null) _rightPlayerAlive[0] = false;
            _leftPlayerAlive = null;
            _rightPlayerAlive = null;

            // 2. Snapshot + clear refs so re-entry can't see them.
            var leftPlayer = _leftPlayer;
            var rightPlayer = _rightPlayer;
            var leftView = _leftVideoView;
            var rightView = _rightVideoView;
            _leftPlayer = null;
            _rightPlayer = null;
            _leftVideoView = null;
            _rightVideoView = null;

            if (leftPlayer == null && rightPlayer == null) return;

            // 3. Stop players FIRST (parallel, with timeout). Stop must come
            //    before VideoView detach — otherwise we yank the HWND binding
            //    while LibVLC is mid-render.
            var stopTasks = new List<Task>(2);
            if (leftPlayer  != null) stopTasks.Add(Task.Run(() => { try { leftPlayer.Stop();  } catch { } }));
            if (rightPlayer != null) stopTasks.Add(Task.Run(() => { try { rightPlayer.Stop(); } catch { } }));
            try { Task.WaitAll(stopTasks.ToArray(), TimeSpan.FromMilliseconds(500)); } catch { }

            // 4. Pump messages so LibVLC's UI-thread callbacks finish before
            //    we touch the VideoView binding.
            WaitWithMessagePump(200);

            // 5. Detach view→player on the UI thread. Safe now: player stopped.
            if (leftView  != null) { try { leftView.MediaPlayer  = null; } catch { } }
            if (rightView != null) { try { rightView.MediaPlayer = null; } catch { } }

            // 6. Dispose. Synchronous on window-close so AppDomain teardown
            //    doesn't race LibVLC native cleanup; async during gameplay so
            //    we don't stall the UI between rounds.
            if (synchronous)
            {
                WaitWithMessagePump(150);
                try { leftPlayer?.Dispose(); }  catch { }
                try { rightPlayer?.Dispose(); } catch { }
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(150);
                    try { leftPlayer?.Dispose(); }  catch { }
                    try { rightPlayer?.Dispose(); } catch { }
                });
            }
        }

        // Pump WPF messages while waiting so LibVLC's UI-thread callbacks can
        // run. Plain Thread.Sleep on the UI thread would deadlock against
        // those callbacks. Mirrors VideoService.WaitWithMessagePump.
        private static void WaitWithMessagePump(int milliseconds)
        {
            var endTime = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(
                        DispatcherPriority.Background,
                        new Action(() => { }));
                }
                catch { return; }
                Thread.Sleep(10);
            }
        }

        private void StartRoundTicker()
        {
            _roundTicker?.Stop();
            _roundTicker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _roundTicker.Tick += RoundTicker_Tick;
            _roundTicker.Start();
        }

        private void StopRoundTicker()
        {
            _roundTicker?.Stop();
            _roundTicker = null;
        }

        private void RoundTicker_Tick(object? sender, EventArgs e)
        {
            if (!_gameRunning) return;
            // Round-start grace: pause BOTH gaze accumulation and the duration
            // timeout. The duration is the visible-and-decisional window — the
            // ~1s warm-up shouldn't eat into it. _roundStartedAt is also
            // effectively shifted by ignoring elapsed time during grace.
            if (DateTime.UtcNow < _roundIgnoreGazeUntil)
            {
                _roundStartedAt = DateTime.UtcNow;
                return;
            }
            var spec = _rounds[_currentRoundIdx];

            // Accumulate time on whichever side the gaze is currently on.
            // Pause both accumulators if face is lost — camera glitches
            // shouldn't penalize the user.
            const double tickMs = 50;
            bool tickedWrong = false;
            if (!_faceLost)
            {
                if (_currentSide == spec.CorrectSide) _correctMs += tickMs;
                else if (_currentSide != GameSide.None) { _wrongMs += tickMs; tickedWrong = true; }
            }

            // Decision thresholds.
            //   Win  = PassTime seconds of correct-side gaze accumulated
            //   Lose = noise-side glance (default WrongHoldMs=0 → strict; >0
            //          turns it into a saccade-filter dwell time). Gated on
            //          tickedWrong so the trivially-true "_wrongMs >= 0" can't
            //          fire from frame 1 before any wrong gaze happened.
            //   Timeout = asset reaches its max display duration without either
            var passTimeMs = _settings.PassTimeSec * 1000.0;
            if (_correctMs >= passTimeMs)
            {
                CompleteRound(RoundOutcome.Correct);
                return;
            }
            if (tickedWrong && _wrongMs >= _settings.WrongHoldMs)
            {
                CompleteRound(RoundOutcome.Wrong);
                return;
            }

            var maxDisplayMs = spec.DurationSec * 1000.0;
            var elapsedTotal = (DateTime.UtcNow - _roundStartedAt).TotalMilliseconds;
            if (elapsedTotal >= maxDisplayMs)
            {
                CompleteRound(RoundOutcome.Timeout);
            }
        }

        private async void CompleteRound(RoundOutcome outcome)
        {
            _gameRunning = false;
            StopRoundTicker();

            var spec = _rounds[_currentRoundIdx];
            _results.Add(new RoundResult
            {
                Index = _currentRoundIdx,
                Type = spec.Type,
                Outcome = outcome,
                CorrectMs = _correctMs,
                WrongMs = _wrongMs,
            });

            // Tear down assets BEFORE animating the feedback card in: gives a
            // clean black backdrop AND avoids the LibVLC airspace problem
            // (native VideoView HWND would paint over any in-window WPF text).
            // Reward effects / shake fire first, while assets are still
            // visible — the visceral cue reads against the asset, then blackout.
            switch (outcome)
            {
                case RoundOutcome.Correct:
                    FireRewardEffect(_settings.RewardEffect);
                    if (_settings.VibrationMode == GazeVibrationMode.OnCorrect) FireVibration("reward");
                    DisposeCurrentRoundPlayers();
                    LeftPane.Children.Clear();
                    RightPane.Children.Clear();
                    PlayJingle();
                    await ShowFullscreenFeedbackAsync("GOOD GIRL", Color.FromRgb(0xFF, 0x69, 0xB4));
                    break;
                case RoundOutcome.Wrong:
                    if (_settings.VibrationMode == GazeVibrationMode.OnWrong) FireVibration("punish");
                    await ShakeGameplayAsync();
                    DisposeCurrentRoundPlayers();
                    LeftPane.Children.Clear();
                    RightPane.Children.Clear();
                    await ShowFullscreenFeedbackAsync("WRONG", Color.FromRgb(0xFF, 0x40, 0x40));
                    break;
                case RoundOutcome.Timeout:
                    DisposeCurrentRoundPlayers();
                    LeftPane.Children.Clear();
                    RightPane.Children.Clear();
                    await System.Threading.Tasks.Task.Delay(500);   // silent buffer
                    break;
            }

            AdvanceRound();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Reward / vibration triggers
        // ─────────────────────────────────────────────────────────────────────

        private void FireRewardEffect(GazeRewardEffect effect)
        {
            try
            {
                switch (effect)
                {
                    case GazeRewardEffect.None:
                        break;
                    case GazeRewardEffect.Flashes:
                        // TriggerFlashOnce honors the user's CCP-wide flash settings (count/duration/size).
                        App.Flash?.TriggerFlashOnce();
                        break;
                    case GazeRewardEffect.Bubbles:
                        // SpawnOnce produces a single bubble; loop a small burst with tiny stagger.
                        for (int i = 0; i < 5; i++) App.Bubbles?.SpawnOnce();
                        break;
                    case GazeRewardEffect.Audio:
                        PlayRewardAudio(_settings.RewardAudioFile);
                        break;
                    case GazeRewardEffect.MindWipe:
                        App.MindWipe?.TriggerOnce();
                        break;
                    case GazeRewardEffect.OverlayPulse:
                        App.Overlay?.PulseOverlays();
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeMinigame: reward effect '{Effect}' threw", effect);
            }
        }

        private static void FireVibration(string tag)
        {
            // TriggerSubliminalPatternAsync no-ops when the haptic service is
            // disabled or no device is connected — safe to fire-and-forget.
            try { _ = App.Haptics?.TriggerSubliminalPatternAsync(tag); }
            catch (Exception ex) { App.Logger?.Warning(ex, "GazeMinigame: vibration trigger threw"); }
        }

        private static void PlayRewardAudio(string fileName)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources", "AwarenessPresets", "audio", fileName);
                if (!System.IO.File.Exists(path))
                {
                    App.Logger?.Warning("GazeMinigame: reward audio missing '{Path}'", path);
                    return;
                }

                // Self-disposing playback — matches the keyword-trigger pattern
                // (NAudio AudioFileReader + WaveOutEvent), short clip so we
                // don't bother with a manager/pool.
                var reader = new NAudio.Wave.AudioFileReader(path);
                var player = new NAudio.Wave.WaveOutEvent();
                var master = (App.Settings?.Current?.MasterVolume ?? 100) / 100.0f;
                reader.Volume = (float)Math.Pow(master, 1.5);   // same curve as PlayTriggerAudio
                player.PlaybackStopped += (_, _) =>
                {
                    try { player.Dispose(); reader.Dispose(); } catch { }
                };
                player.Init(reader);
                player.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeMinigame: PlayRewardAudio failed");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Inter-round feedback card
        // ─────────────────────────────────────────────────────────────────────

        // Full-screen feedback card. Caller MUST clear panes + dispose video
        // players before calling — otherwise LibVLC's native HWND will paint
        // over this in-window TextBlock.
        private async Task ShowFullscreenFeedbackAsync(string text, Color color)
        {
            TxtFeedback.Text = text;
            TxtFeedback.Foreground = new SolidColorBrush(color);
            FeedbackShadow.Color = color;

            FeedbackCard.Visibility = Visibility.Visible;
            var fadeIn  = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100));
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
            FeedbackCard.BeginAnimation(OpacityProperty, fadeIn);
            await Task.Delay(1400);                     // 100ms fade-in + 1300ms hold
            FeedbackCard.BeginAnimation(OpacityProperty, fadeOut);
            await Task.Delay(120);                      // fade-out + tiny tail
            // Detach the running animation so the local Opacity assignment
            // below isn't clobbered by the held animation value.
            FeedbackCard.BeginAnimation(OpacityProperty, null);
            FeedbackCard.Opacity = 0;
            FeedbackCard.Visibility = Visibility.Collapsed;
        }

        // Reuses the existing PlayRewardAudio NAudio path; chime.wav is
        // already bundled at Resources/AwarenessPresets/audio/chime.wav.
        // Plays unconditionally on correct rounds, independent of the user's
        // configured RewardEffect (which has its own opt-in audio path).
        private static void PlayJingle() => PlayRewardAudio("chime.wav");

        // Brief horizontal shake of the panes container on wrong outcomes.
        // Animates the existing GameplayShake TranslateTransform on
        // GameplayPairGrid (defined in XAML) — NOT Window.Left, which doesn't
        // move while the window is WindowState.Maximized.
        private async Task ShakeGameplayAsync()
        {
            var anim = new DoubleAnimationUsingKeyFrames();
            // 9 keyframes, 40ms apart, ±10px → ±3px → 0. ~360ms total.
            int[] offsets = { -10, 10, -8, 8, -5, 5, -3, 3, 0 };
            double t = 0;
            foreach (var o in offsets)
            {
                anim.KeyFrames.Add(new LinearDoubleKeyFrame(o, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
                t += 40;
            }
            GameplayShake.BeginAnimation(TranslateTransform.XProperty, anim);
            await Task.Delay((int)t);
            GameplayShake.BeginAnimation(TranslateTransform.XProperty, null);
            GameplayShake.X = 0;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Asset entry effects
        // ─────────────────────────────────────────────────────────────────────

        // Fade-in + subtle scale-up on the asset element. Works cleanly for
        // WPF Image; for VideoView the WPF opacity animation only shows during
        // the brief moment before LibVLC's native HWND starts rendering
        // (acceptable v1 limitation — the sparkle burst is image-only too).
        private static void AnimateAssetIn(FrameworkElement el)
        {
            el.Opacity = 0;
            var scale = new ScaleTransform(0.95, 0.95);
            el.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
            el.RenderTransform = scale;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var grow = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(300))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            el.BeginAnimation(UIElement.OpacityProperty, fade);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        // Pink sparkle particles bursting outward from the pane center.
        // Self-cleaning: each particle removes itself from the host's Children
        // when its fade-out animation completes, so Children doesn't pile up
        // across rounds.
        private static void SpawnSparkleBurst(Grid host)
        {
            const int count = 12;
            var rng = Random.Shared;
            var pink = Color.FromRgb(0xFF, 0x69, 0xB4);
            for (int i = 0; i < count; i++)
            {
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(pink),
                    Effect = new DropShadowEffect
                    {
                        Color = pink,
                        BlurRadius = 12,
                        ShadowDepth = 0,
                        Opacity = 0.9,
                    },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
                var t = new TranslateTransform();
                dot.RenderTransform = t;
                host.Children.Add(dot);

                double angle = rng.NextDouble() * Math.PI * 2;
                double dist  = 80 + rng.NextDouble() * 60;     // 80–140 px
                double dx    = Math.Cos(angle) * dist;
                double dy    = Math.Sin(angle) * dist;
                int dur      = 450 + rng.Next(0, 150);          // 450–600 ms

                var animX = new DoubleAnimation(0, dx, TimeSpan.FromMilliseconds(dur))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var animY = new DoubleAnimation(0, dy, TimeSpan.FromMilliseconds(dur))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(dur))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

                var dotRef = dot;
                fade.Completed += (_, _) => { try { host.Children.Remove(dotRef); } catch { } };

                t.BeginAnimation(TranslateTransform.XProperty, animX);
                t.BeginAnimation(TranslateTransform.YProperty, animY);
                dot.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Webcam events
        // ─────────────────────────────────────────────────────────────────────

        private void EnsureWebcamSubscribed()
        {
            if (_webcamSubscribed || App.Webcam == null) return;
            App.Webcam.OnGazeSide += OnGazeSideChanged;
            App.Webcam.OnFaceLost += OnFaceLost;
            App.Webcam.OnFaceFound += OnFaceFound;
            _webcamSubscribed = true;
        }

        private void UnsubscribeWebcam()
        {
            if (!_webcamSubscribed || App.Webcam == null) return;
            App.Webcam.OnGazeSide -= OnGazeSideChanged;
            App.Webcam.OnFaceLost -= OnFaceLost;
            App.Webcam.OnFaceFound -= OnFaceFound;
            _webcamSubscribed = false;
        }

        // Use OnGazeSide rather than the screen-projected OnGazeMove because:
        //  • OnGazeSide already runs through the calibrated LeftRefVec /
        //    RightRefVec classifier with hysteresis bands (enter ~17.5%,
        //    leave ~7.5% of L/R spread) — it doesn't oscillate when gaze
        //    hovers near the screen midline.
        //  • OnGazeSide also passes through the 3-frame stability filter,
        //    suppressing single-frame saccade noise that would otherwise flip
        //    _currentSide to the wrong half and instantly fail the round
        //    under WrongHoldMs=0 strict mode.
        //  • No dependency on window-screen position (Left/Top), which can
        //    misreport during the maximize/fullscreen state dance.
        private void OnGazeSideChanged(GazeSide side)
        {
            if (!_gameRunning) return;
            _currentSide = side switch
            {
                GazeSide.Left  => GameSide.Left,
                GazeSide.Right => GameSide.Right,
                _              => GameSide.None,
            };
        }

        private void OnFaceLost() => _faceLost = true;
        private void OnFaceFound() => _faceLost = false;

        // ─────────────────────────────────────────────────────────────────────
        //  Results
        // ─────────────────────────────────────────────────────────────────────

        private void EndSession()
        {
            _gameRunning = false;
            DisposeCurrentRoundPlayers();
            StopRoundTicker();
            // Drop fullscreen so the user can read the results in their normal
            // windowed environment (taskbar back, chrome restored).
            ExitFullscreen();
            BuildResultsList();
            ShowScreen(ResultsScreen);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Fullscreen
        // ─────────────────────────────────────────────────────────────────────

        private void EnterFullscreen()
        {
            if (_isFullscreen) return;
            _savedStyle = WindowStyle;
            _savedState = WindowState;
            _savedResize = ResizeMode;

            // WindowState dance: WPF won't honor a WindowState change to
            // Maximized while WindowStyle=None unless we step through Normal.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;

            // Float above any other Topmost windows (e.g. AvatarTubeWindow when
            // attached) for the duration of gameplay. WPF orders Topmost
            // windows by activation, so re-activate after flipping the flag.
            Topmost = true;
            Activate();

            _isFullscreen = true;
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen) return;
            Topmost = false;
            WindowState = WindowState.Normal;
            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;
            WindowState = _savedState;
            _isFullscreen = false;
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen) ExitFullscreen(); else EnterFullscreen();
        }

        private void BuildResultsList()
        {
            ResultsList.Children.Clear();

            int correct = _results.Count(r => r.Outcome == RoundOutcome.Correct);
            int wrong   = _results.Count(r => r.Outcome == RoundOutcome.Wrong);
            int timeout = _results.Count(r => r.Outcome == RoundOutcome.Timeout);
            TxtResultsHeadline.Text = $"{correct} correct  ·  {wrong} wrong  ·  {timeout} no-decision";

            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                var color = r.Outcome switch
                {
                    RoundOutcome.Correct => Color.FromRgb(0x80, 0xE0, 0x80),
                    RoundOutcome.Wrong   => Color.FromRgb(0xFF, 0x80, 0x80),
                    _ => Color.FromRgb(0xCC, 0xCC, 0xCC),
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(new TextBlock
                {
                    Text = $"#{i + 1,2}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontFamily = new FontFamily("Consolas"),
                    Width = 40,
                });
                row.Children.Add(new TextBlock
                {
                    Text = r.Type.ToString(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    FontFamily = new FontFamily("Consolas"),
                    Width = 60,
                });
                row.Children.Add(new TextBlock
                {
                    Text = r.Outcome.ToString().ToUpperInvariant(),
                    Foreground = new SolidColorBrush(color),
                    FontWeight = FontWeights.Bold,
                    Width = 110,
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"correct {r.CorrectMs:F0}ms · wrong {r.WrongMs:F0}ms",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                });
                ResultsList.Children.Add(row);
            }
        }

        private void BtnResultsClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnResultsPlayAgain_Click(object sender, RoutedEventArgs e)
        {
            _results.Clear();
            _currentRoundIdx = -1;
            ShowScreen(ReadyScreen);   // back to settings, not all the way to title
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape) return;

            if (_gameRunning)
            {
                var result = System.Windows.MessageBox.Show(this,
                    "Quit the current session? Your progress for this run will be lost.",
                    "Gaze minigame",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) Close();
                return;
            }

            // Outside of gameplay, ESC closes (after exiting fullscreen if needed).
            ExitFullscreen();
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _gameRunning = false;
            StopRoundTicker();
            // synchronous=true so Stop+detach+Dispose all complete before the
            // window's HWND dies and (potentially) before app shutdown unloads
            // native LibVLC. Without this, a fire-and-forget Dispose Task can
            // race AppDomain teardown — that's the 2026-04-26 01:45 native
            // DllNotFoundException in the crash log.
            DisposeCurrentRoundPlayers(synchronous: true);
            UnsubscribeWebcam();
        }
    }
}
