using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Lab.GazeMinigame;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Lab.GazeMinigame
{
    /// <summary>
    /// First playable Lab minigame. Side-by-side image/video pairs; the user must
    /// hold gaze on the "correct" pack content. Looking at the noise side too
    /// long → WRONG flash + beep. Holding correct for the full asset duration →
    /// GOOD GIRL flash. Self-contained: owns its packs, settings, playback, and
    /// result tracking.
    ///
    /// PORTED from ConditioningControlPanel/Lab/GazeMinigame/GazeMinigameWindow.xaml.cs.
    /// Every screen, the round builder, the difficulty presets, the pass-time warning and the
    /// results list are ported whole; what needs a service is stubbed. Deviations:
    ///
    ///  - <c>GazeMinigameSettings</c>, <c>GazePackRole</c>, <c>GazeVibrationMode</c>,
    ///    <c>GazeRewardEffect</c> and <c>GazePackLibrary</c> still live in the WPF head (only
    ///    <c>AssetPack</c> has moved to Core), so this file carries private twins of the enums
    ///    and of the settings record — presets, clamps and bundled-audio list copied verbatim,
    ///    because the chips, sliders and warning ARE view logic. Load/Save/Discover are stubs.
    ///  - The library is seeded with <see cref="SampleLibrary"/> so the render exercises the
    ///    card builder, both drop zones, the library strip and an enabled Start button.
    ///  - <c>App.Webcam</c> / <c>App.Flash</c> / <c>App.Haptics</c> / <c>App.Bubbles</c> /
    ///    <c>App.MindWipe</c> / <c>App.Overlay</c> / <c>WebcamCalibrationWindow</c>, LibVLC
    ///    (<c>VideoView</c> + the whole stop/detach/dispose dance and its message pump), NAudio
    ///    and XamlAnimatedGif are all head-side; each is a stub. Gaze never changes side, so a
    ///    round resolves on its display cap.
    ///  - <c>MessageBox.Show</c> has no Avalonia equivalent and no package may be added, so the
    ///    "quit the session?" confirmation is not raised (ChaosSlotPickerWindow precedent).
    ///  - <c>FolderBrowserDialog</c> -> <c>StorageProvider.OpenFolderPickerAsync</c>.
    ///  - Mouse* -> Pointer*; Avalonia 12's <c>DoDragDropAsync</c> only takes a
    ///    <see cref="PointerPressedEventArgs"/>, so the drag starts on press and the WPF
    ///    drag-threshold arming (<c>_dragStart</c> / <c>_dragArmed</c>) is gone, as in
    ///    SessionEditorWindow. <c>DataObject</c> -> <c>DataTransfer</c> + a typed
    ///    <see cref="DataFormat"/>.
    ///  - Storyboards -> transitions plus awaited delays; the shake is literally its nine
    ///    40ms keyframes written as a loop.
    ///  - The WPF fullscreen WindowStyle/WindowState dance collapses to
    ///    <c>WindowState.FullScreen</c>.
    ///  - Handlers are wired in the constructor, per the porting convention.
    /// </summary>
    public partial class GazeMinigameWindow : Window
    {
        private enum GameSide { None, Left, Right }
        private enum AssetType { Image, Video }
        private enum RoundOutcome { Correct, Wrong, Timeout }

        /// <summary>ponytail: needs Lab/GazeMinigame/GazeMinigameSettings.cs:GazePackRole,
        /// wired when it moves to Core.</summary>
        private enum GazePackRole { Off, Focus, Ignore }

        /// <summary>ponytail: needs Lab/GazeMinigame/GazeMinigameSettings.cs:GazeVibrationMode,
        /// wired when it moves to Core.</summary>
        private enum GazeVibrationMode { None, OnCorrect, OnWrong }

        /// <summary>ponytail: needs Lab/GazeMinigame/GazeMinigameSettings.cs:GazeRewardEffect,
        /// wired when it moves to Core.</summary>
        private enum GazeRewardEffect { None, Flashes, Bubbles, Audio, MindWipe, OverlayPulse }

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

        /// <summary>
        /// Field-for-field twin of the head's <c>GazeMinigameSettings</c>, minus the JSON
        /// attributes. The presets, the clamps and the bundled-audio list are copied verbatim:
        /// they drive the difficulty chips and the sliders, which are view logic.
        /// ponytail: needs Lab/GazeMinigame/GazeMinigameSettings.cs, wired when it moves to Core.
        /// </summary>
        private sealed class GazeMinigameSettings
        {
            public int ImageCount = 8;
            public int VideoCount = 2;
            public int ImageDurationSec = 5;
            public int VideoMaxDurationSec = 30;
            public int PassTimeSec = 3;
            // 0 = strict mode: any glance at the noise side fires WRONG immediately.
            public int WrongHoldMs;
            public GazeVibrationMode VibrationMode = GazeVibrationMode.None;
            public GazeRewardEffect RewardEffect = GazeRewardEffect.None;
            public string RewardAudioFile = "bell.wav";
            public string Difficulty = "Normal";

            public void ApplyDifficulty(string name)
            {
                switch (name)
                {
                    case "Easy":
                        PassTimeSec = 3; ImageDurationSec = 6; VideoMaxDurationSec = 30;
                        WrongHoldMs = 600; ImageCount = 6; VideoCount = 1;
                        Difficulty = "Easy";
                        break;
                    case "Normal":
                        PassTimeSec = 3; ImageDurationSec = 5; VideoMaxDurationSec = 30;
                        WrongHoldMs = 200; ImageCount = 8; VideoCount = 2;
                        Difficulty = "Normal";
                        break;
                    case "Hard":
                        PassTimeSec = 5; ImageDurationSec = 4; VideoMaxDurationSec = 20;
                        WrongHoldMs = 0; ImageCount = 10; VideoCount = 3;
                        Difficulty = "Hard";
                        break;
                    default:
                        Difficulty = "Custom";
                        break;
                }
                Clamp();
            }

            public static readonly string[] BundledAudioFiles =
            {
                "bell.wav", "chime.wav", "clicker.mp3", "lock-click.mp3"
            };

            public const int ImageCountMin = 0, ImageCountMax = 20;
            public const int VideoCountMin = 0, VideoCountMax = 10;
            public const int ImageDurationMin = 2, ImageDurationMax = 10;
            public const int VideoDurationMin = 10, VideoDurationMax = 120;
            public const int PassTimeMin = 3, PassTimeMax = 30;
            public const int WrongHoldMinMs = 0, WrongHoldMaxMs = 2000;

            /// <summary>ponytail: needs GazeMinigameSettings.Load (Newtonsoft + CorePaths),
            /// wired when it moves to Core. Defaults until then.</summary>
            public static GazeMinigameSettings Load() => new();

            /// <summary>ponytail: needs GazeMinigameSettings.Save, wired when it moves to Core.</summary>
            public void Save() => Clamp();

            private void Clamp()
            {
                ImageCount = Math.Clamp(ImageCount, ImageCountMin, ImageCountMax);
                VideoCount = Math.Clamp(VideoCount, VideoCountMin, VideoCountMax);
                ImageDurationSec = Math.Clamp(ImageDurationSec, ImageDurationMin, ImageDurationMax);
                VideoMaxDurationSec = Math.Clamp(VideoMaxDurationSec, VideoDurationMin, VideoDurationMax);
                PassTimeSec = Math.Clamp(PassTimeSec, PassTimeMin, PassTimeMax);
                WrongHoldMs = Math.Clamp(WrongHoldMs, WrongHoldMinMs, WrongHoldMaxMs);
            }
        }

        // ── XAML parts ──
        private readonly Grid _titleScreen, _countdownScreen, _gameplayScreen, _resultsScreen;
        private readonly Grid _gameplayPairGrid, _leftPane, _rightPane, _feedbackCard, _rewardAudioRow;
        private readonly Border _libraryZone, _focusZone, _ignoreZone, _readyBanner;
        private readonly WrapPanel _libraryPanel, _focusPanel, _ignorePanel;
        private readonly StackPanel _libraryEmptyState, _resultsList;
        private readonly TextBlock _focusPlaceholder, _ignorePlaceholder, _txtSelectionSummary;
        private readonly TextBlock _txtImageCountVal, _txtVideoCountVal, _txtImageDurVal,
                                   _txtVideoDurVal, _txtPassTimeVal, _txtPassTimeWarn,
                                   _txtVibrationStatus, _txtReadyBanner, _txtCountdown,
                                   _txtRoundInfo, _txtFeedback, _txtResultsHeadline;
        private readonly Slider _sliderImageCount, _sliderVideoCount, _sliderImageDur,
                                _sliderVideoDur, _sliderPassTime;
        private readonly ComboBox _cboVibration, _cboReward, _cboRewardAudio;
        private readonly Button _btnDiffEasy, _btnDiffNormal, _btnDiffHard,
                                _btnStartGame, _btnReadyBannerAction;
        private readonly TranslateTransform _gameplayShake;

        private readonly List<AssetPack> _packs = new();
        private GazeMinigameSettings _settings = new();

        // ── Setup screen state ──
        // Discovered + custom packs, keyed by full path, plus the user's current
        // Focus/Ignore/Off assignment. _roles is the single source of truth the
        // gallery renders from and that we persist into the settings.
        private readonly List<AssetPack> _library = new();
        private readonly Dictionary<string, GazePackRole> _roles = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _customPaths = new();

        /// <summary>The drag payload. WPF stuffed a raw path into a DataObject; Avalonia 12
        /// replaced DataObject with DataTransfer and wants a typed DataFormat, so the format
        /// is declared once here and used on both ends. Name copied from the WPF constant.</summary>
        private static readonly DataFormat<string> DragFormat =
            DataFormat.CreateStringApplicationFormat("GazeAssetPackPath");

        // Guard so programmatic slider updates (difficulty presets / load) don't
        // flip the chosen difficulty to "Custom".
        private bool _applyingSliders;

        private List<RoundSpec> _rounds = new();
        private readonly List<RoundResult> _results = new();
        private int _currentRoundIdx = -1;
        private DateTime _roundStartedAt;
        // Grace window at the start of each round: gaze accumulation AND the
        // duration timeout are both paused until UtcNow >= this.
        private DateTime _roundIgnoreGazeUntil;
        private const int GraceMs = 1000;
        private double _correctMs;
        private double _wrongMs;
        private GameSide _currentSide = GameSide.None;
        private bool _faceLost;
        private DispatcherTimer? _roundTicker;

        private bool _gameRunning;

        // Saved windowed state so ToggleFullscreen() can restore cleanly.
        private WindowState _savedState;
        private bool _isFullscreen;

        public GazeMinigameWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _titleScreen = this.FindControl<Grid>("TitleScreen")!;
            _countdownScreen = this.FindControl<Grid>("CountdownScreen")!;
            _gameplayScreen = this.FindControl<Grid>("GameplayScreen")!;
            _resultsScreen = this.FindControl<Grid>("ResultsScreen")!;
            _gameplayPairGrid = this.FindControl<Grid>("GameplayPairGrid")!;
            _leftPane = this.FindControl<Grid>("LeftPane")!;
            _rightPane = this.FindControl<Grid>("RightPane")!;
            _feedbackCard = this.FindControl<Grid>("FeedbackCard")!;
            _rewardAudioRow = this.FindControl<Grid>("RewardAudioRow")!;
            _libraryZone = this.FindControl<Border>("LibraryZone")!;
            _focusZone = this.FindControl<Border>("FocusZone")!;
            _ignoreZone = this.FindControl<Border>("IgnoreZone")!;
            _readyBanner = this.FindControl<Border>("ReadyBanner")!;
            _libraryPanel = this.FindControl<WrapPanel>("LibraryPanel")!;
            _focusPanel = this.FindControl<WrapPanel>("FocusPanel")!;
            _ignorePanel = this.FindControl<WrapPanel>("IgnorePanel")!;
            _libraryEmptyState = this.FindControl<StackPanel>("LibraryEmptyState")!;
            _resultsList = this.FindControl<StackPanel>("ResultsList")!;
            _focusPlaceholder = this.FindControl<TextBlock>("FocusPlaceholder")!;
            _ignorePlaceholder = this.FindControl<TextBlock>("IgnorePlaceholder")!;
            _txtSelectionSummary = this.FindControl<TextBlock>("TxtSelectionSummary")!;
            _txtImageCountVal = this.FindControl<TextBlock>("TxtImageCountVal")!;
            _txtVideoCountVal = this.FindControl<TextBlock>("TxtVideoCountVal")!;
            _txtImageDurVal = this.FindControl<TextBlock>("TxtImageDurVal")!;
            _txtVideoDurVal = this.FindControl<TextBlock>("TxtVideoDurVal")!;
            _txtPassTimeVal = this.FindControl<TextBlock>("TxtPassTimeVal")!;
            _txtPassTimeWarn = this.FindControl<TextBlock>("TxtPassTimeWarn")!;
            _txtVibrationStatus = this.FindControl<TextBlock>("TxtVibrationStatus")!;
            _txtReadyBanner = this.FindControl<TextBlock>("TxtReadyBanner")!;
            _txtCountdown = this.FindControl<TextBlock>("TxtCountdown")!;
            _txtRoundInfo = this.FindControl<TextBlock>("TxtRoundInfo")!;
            _txtFeedback = this.FindControl<TextBlock>("TxtFeedback")!;
            _txtResultsHeadline = this.FindControl<TextBlock>("TxtResultsHeadline")!;
            _sliderImageCount = this.FindControl<Slider>("SliderImageCount")!;
            _sliderVideoCount = this.FindControl<Slider>("SliderVideoCount")!;
            _sliderImageDur = this.FindControl<Slider>("SliderImageDur")!;
            _sliderVideoDur = this.FindControl<Slider>("SliderVideoDur")!;
            _sliderPassTime = this.FindControl<Slider>("SliderPassTime")!;
            _cboVibration = this.FindControl<ComboBox>("CboVibration")!;
            _cboReward = this.FindControl<ComboBox>("CboReward")!;
            _cboRewardAudio = this.FindControl<ComboBox>("CboRewardAudio")!;
            _btnDiffEasy = this.FindControl<Button>("BtnDiffEasy")!;
            _btnDiffNormal = this.FindControl<Button>("BtnDiffNormal")!;
            _btnDiffHard = this.FindControl<Button>("BtnDiffHard")!;
            _btnStartGame = this.FindControl<Button>("BtnStartGame")!;
            _btnReadyBannerAction = this.FindControl<Button>("BtnReadyBannerAction")!;
            // x:Name on a Transform is AVLN2000, so the shake transform is fetched here instead.
            _gameplayShake = (TranslateTransform)_gameplayPairGrid.RenderTransform!;

            // Handlers, all of which were Click="…" / ValueChanged="…" attributes in the WPF XAML.
            this.FindControl<Button>("BtnAddFolder")!.Click += BtnAddFolder_Click;
            this.FindControl<Button>("BtnRescan")!.Click += BtnRescan_Click;
            this.FindControl<Button>("BtnOpenAssets")!.Click += BtnOpenAssets_Click;
            this.FindControl<Button>("BtnResultsClose")!.Click += BtnResultsClose_Click;
            this.FindControl<Button>("BtnResultsPlayAgain")!.Click += BtnResultsPlayAgain_Click;
            _btnDiffEasy.Click += BtnDifficulty_Click;
            _btnDiffNormal.Click += BtnDifficulty_Click;
            _btnDiffHard.Click += BtnDifficulty_Click;
            _btnStartGame.Click += BtnStartGame_Click;
            _btnReadyBannerAction.Click += BtnReadyBannerAction_Click;
            _sliderImageCount.ValueChanged += SliderImageCount_ValueChanged;
            _sliderVideoCount.ValueChanged += SliderVideoCount_ValueChanged;
            _sliderImageDur.ValueChanged += SliderImageDur_ValueChanged;
            _sliderVideoDur.ValueChanged += SliderVideoDur_ValueChanged;
            _sliderPassTime.ValueChanged += SliderPassTime_ValueChanged;
            _cboVibration.SelectionChanged += CboVibration_SelectionChanged;
            _cboReward.SelectionChanged += CboReward_SelectionChanged;
            _cboRewardAudio.SelectionChanged += CboRewardAudio_SelectionChanged;
            KeyDown += Window_KeyDown;
            Closing += Window_Closing;

            // WPF's AllowDrop + Drop/DragOver/DragLeave attributes.
            foreach (var (zone, role) in new[]
                     {
                         (_libraryZone, GazePackRole.Off),
                         (_focusZone, GazePackRole.Focus),
                         (_ignoreZone, GazePackRole.Ignore),
                     })
            {
                var captured = role;
                zone.AddHandler(DragDrop.DragOverEvent, Zone_DragOver);
                zone.AddHandler(DragDrop.DragLeaveEvent, Zone_DragLeave);
                zone.AddHandler(DragDrop.DropEvent, (s, e) => OnZoneDrop(s, e, captured));
            }

            _settings = GazeMinigameSettings.Load();
            ApplySettingsToSliders();
            InitSetupScreen();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Screen routing
        // ─────────────────────────────────────────────────────────────────────

        private void ShowScreen(Grid screen)
        {
            _titleScreen.IsVisible = screen == _titleScreen;
            _countdownScreen.IsVisible = screen == _countdownScreen;
            _gameplayScreen.IsVisible = screen == _gameplayScreen;
            _resultsScreen.IsVisible = screen == _resultsScreen;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Setup screen — discovery, drag-and-drop pack assignment, difficulty
        // ─────────────────────────────────────────────────────────────────────

        private void InitSetupScreen()
        {
            _customPaths.Clear();

            DiscoverLibrary();
            UpdateDifficultyChips();

            // Proactive calibration nudge so the user fixes it before staring at a
            // Start button that would otherwise bounce them. In WPF this was gated on
            // App.Webcam?.Calibration == null.
            // ponytail: needs WebcamTrackingService, wired when it moves to Core.
            ShowReadyBanner("Tip: run a 16-point gaze calibration before playing so the game can tell which side you're looking at.", showCalibrateAction: true);
        }

        /// <summary>Rescan content folders, preserving the current role assignments.</summary>
        private void DiscoverLibrary()
        {
            _library.Clear();
            // ponytail: needs Lab/GazeMinigame/GazePackLibrary.Discover, wired when it moves to
            // Core. Until then the strip shows the sample library so the gallery, both zones and
            // the Start gating all draw.
            _library.AddRange(SampleLibrary());

            foreach (var pack in _library)
            {
                var key = NormPath(pack.Path);
                if (!_roles.ContainsKey(key)) _roles[key] = SampleRole(pack);
            }
            // Forget roles for packs no longer present.
            var live = new HashSet<string>(_library.Select(p => NormPath(p.Path)), StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _roles.Keys.Where(k => !live.Contains(k)).ToList())
                _roles.Remove(stale);

            RenderPacks();
        }

        /// <summary>Placeholder packs standing in for a real discovery pass: one image set, one
        /// mixed set, one video-only set, so both card shapes (thumbnail path and glyph) and all
        /// three panels are exercised by the render.</summary>
        private static IEnumerable<AssetPack> SampleLibrary()
        {
            var root = CorePaths.EffectiveAssets;
            yield return new AssetPack
            {
                Name = "goodgirl",
                Path = System.IO.Path.Combine(root, "images", "goodgirl"),
                ImagePaths = Enumerable.Range(1, 24).Select(i => $"goodgirl-{i:00}.png").ToList(),
            };
            yield return new AssetPack
            {
                Name = "spirals",
                Path = System.IO.Path.Combine(root, "images", "spirals"),
                ImagePaths = Enumerable.Range(1, 11).Select(i => $"spiral-{i:00}.png").ToList(),
                VideoPaths = Enumerable.Range(1, 3).Select(i => $"spiral-{i:00}.mp4").ToList(),
            };
            yield return new AssetPack
            {
                Name = "static-noise",
                Path = System.IO.Path.Combine(root, "videos", "static-noise"),
                VideoPaths = Enumerable.Range(1, 6).Select(i => $"noise-{i:00}.mp4").ToList(),
            };
        }

        /// <summary>ponytail: needs the persisted GazePackRef list; the sample assignment stands
        /// in so Focus, Ignore and the library strip each hold a card.</summary>
        private static GazePackRole SampleRole(AssetPack pack) => pack.Name switch
        {
            "goodgirl" => GazePackRole.Focus,
            "spirals" => GazePackRole.Ignore,
            _ => GazePackRole.Off,
        };

        private static string NormPath(string p)
        {
            try { return System.IO.Path.GetFullPath(p); } catch { return p ?? ""; }
        }

        private GazePackRole RoleOf(AssetPack pack)
            => _roles.TryGetValue(NormPath(pack.Path), out var r) ? r : GazePackRole.Off;

        private void RenderPacks()
        {
            _libraryPanel.Children.Clear();
            _focusPanel.Children.Clear();
            _ignorePanel.Children.Clear();

            foreach (var pack in _library)
            {
                var role = RoleOf(pack);
                var card = BuildPackCard(pack, role);
                switch (role)
                {
                    case GazePackRole.Focus: _focusPanel.Children.Add(card); break;
                    case GazePackRole.Ignore: _ignorePanel.Children.Add(card); break;
                    default: _libraryPanel.Children.Add(card); break;
                }
            }

            bool empty = _library.Count == 0;
            _libraryEmptyState.IsVisible = empty;
            _focusPlaceholder.IsVisible = _focusPanel.Children.Count == 0;
            _ignorePlaceholder.IsVisible = _ignorePanel.Children.Count == 0;

            RefreshSetupState();
        }

        private Border BuildPackCard(AssetPack pack, GazePackRole role)
        {
            var accent = role switch
            {
                GazePackRole.Focus => Color.FromRgb(0xFF, 0x69, 0xB4),
                GazePackRole.Ignore => Color.FromRgb(0xFF, 0x80, 0x80),
                _ => Color.FromRgb(0x55, 0x55, 0x66),
            };

            var card = new Border
            {
                Width = 150,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = pack.Path,
            };
            ToolTip.SetTip(card, pack.Path);

            var stack = new StackPanel();

            // Thumbnail (first image) or a glyph for video-only packs.
            var thumbHost = new Border
            {
                Height = 80,
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x14)),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                ClipToBounds = true,
            };
            // ponytail: needs GazePackLibrary.ThumbnailPath; it is a one-liner over ImagePaths,
            // so it is inlined rather than dragged across.
            var thumbPath = pack.ImagePaths.Count > 0 ? pack.ImagePaths[0] : null;
            if (thumbPath != null)
            {
                try
                {
                    // WPF's BitmapImage.DecodePixelWidth=150 becomes DecodeToWidth.
                    using var fs = System.IO.File.OpenRead(thumbPath);
                    thumbHost.Child = new Image
                    {
                        Source = Bitmap.DecodeToWidth(fs, 150),
                        Stretch = Stretch.UniformToFill,
                    };
                }
                catch
                {
                    thumbHost.Child = GlyphBlock("🖼");
                }
            }
            else
            {
                thumbHost.Child = GlyphBlock("🎬");
            }
            stack.Children.Add(thumbHost);

            var body = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
            body.Children.Add(new TextBlock
            {
                Text = pack.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            body.Children.Add(new TextBlock
            {
                Text = $"{pack.ImageCount} img · {pack.VideoCount} vid",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
            });
            stack.Children.Add(body);

            card.Child = stack;

            // Drag to (re)assign.
            card.PointerPressed += Card_PointerPressed;
            return card;
        }

        private static TextBlock GlyphBlock(string glyph) => new()
        {
            Text = glyph,
            FontSize = 30,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // ── Drag source ──
        // WPF armed a flag on PreviewMouseLeftButtonDown and called DoDragDrop from
        // PreviewMouseMove once the drag threshold was passed. Avalonia 12's DoDragDropAsync
        // only accepts a PointerPressedEventArgs, so the drag starts on press and the
        // threshold arming is gone (same trade SessionEditorWindow made).
        private async void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (sender is not Border card || card.Tag is not string path) return;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragFormat, path));
            e.Handled = true;

            try { await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move); }
            catch (Exception ex) { Log.Warning(ex, "GazeMinigame: drag failed"); }
        }

        // ── Drop targets ──
        private void Zone_DragOver(object? sender, DragEventArgs e)
        {
            bool ok = e.DataTransfer.Contains(DragFormat);
            e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
            if (ok && sender is Border b) b.Opacity = 0.80;
            e.Handled = true;
        }

        private void Zone_DragLeave(object? sender, DragEventArgs e)
        {
            if (sender is Border b) b.Opacity = 1.0;
        }

        private void OnZoneDrop(object? sender, DragEventArgs e, GazePackRole role)
        {
            if (sender is Border b) b.Opacity = 1.0;
            if (e.DataTransfer.TryGetValue(DragFormat) is not string path) return;
            AssignRole(path, role);
            e.Handled = true;
        }

        private void AssignRole(string path, GazePackRole role)
        {
            var key = NormPath(path);
            // Focus is single-select — demote the previous focus back to the library.
            if (role == GazePackRole.Focus)
            {
                foreach (var k in _roles.Keys.Where(k => _roles[k] == GazePackRole.Focus && k != key).ToList())
                    _roles[k] = GazePackRole.Off;
            }
            _roles[key] = role;
            RenderPacks();
            SaveSelection();
        }

        /// <summary>ponytail: needs GazeMinigameSettings.Packs persistence (GazePackRef),
        /// wired when it moves to Core. The in-memory _roles map is already authoritative
        /// for this session.</summary>
        private void SaveSelection() => _settings.Save();

        private void RefreshSetupState()
        {
            var focus = _library.FirstOrDefault(p => RoleOf(p) == GazePackRole.Focus);
            var ignore = _library.Where(p => RoleOf(p) == GazePackRole.Ignore).ToList();

            string? reason = null;
            if (focus == null) reason = "Pick a Focus set and at least one Ignore set.";
            else if (ignore.Count == 0) reason = "Add at least one Ignore set (the distractions).";
            else
            {
                // Both buckets must be able to supply at least one shared content
                // type, or the round builder dead-ends.
                bool sharedImages = focus.ImageCount > 0 && ignore.Any(p => p.ImageCount > 0);
                bool sharedVideos = focus.VideoCount > 0 && ignore.Any(p => p.VideoCount > 0);
                if (!sharedImages && !sharedVideos)
                    reason = "Focus and Ignore don't share a content type — pick sets that both have images, or both have videos.";
            }

            _btnStartGame.IsEnabled = reason == null;
            if (reason == null)
            {
                var ignoreNames = string.Join(", ", ignore.Select(p => p.Name));
                _txtSelectionSummary.Text = $"Focus: {focus!.Name}   ·   Ignore: {ignoreNames}";
                _txtSelectionSummary.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
            }
            else
            {
                _txtSelectionSummary.Text = reason;
                _txtSelectionSummary.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }
        }

        private async void BtnAddFolder_Click(object? sender, RoutedEventArgs e)
        {
            // WinForms' FolderBrowserDialog becomes the platform folder picker.
            try
            {
                var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Pick a folder of images and/or videos",
                    AllowMultiple = false,
                });
                var folder = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
                if (string.IsNullOrWhiteSpace(folder)) return;

                var key = NormPath(folder);
                if (_library.Any(p => NormPath(p.Path) == key))
                {
                    ShowReadyBanner("That folder is already in your library.");
                    return;
                }
                var pack = AssetPack.FromFolder(folder);
                if (pack == null)
                {
                    ShowReadyBanner("No images or videos found in that folder.");
                    return;
                }
                if (!_customPaths.Any(p => NormPath(p) == key)) _customPaths.Add(folder);
                HideReadyBanner();
                DiscoverLibrary();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GazeMinigame: add folder failed");
            }
        }

        private void BtnRescan_Click(object? sender, RoutedEventArgs e)
        {
            HideReadyBanner();
            DiscoverLibrary();
        }

        private void BtnOpenAssets_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = CorePaths.EffectiveAssets;
                System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GazeMinigame: open assets folder failed");
            }
        }

        private void BtnDifficulty_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string name) return;
            _settings.ApplyDifficulty(name);
            _applyingSliders = true;
            ApplySettingsToSliders();
            _applyingSliders = false;
            UpdateDifficultyChips();
            _settings.Save();
        }

        private void UpdateDifficultyChips()
        {
            void Set(Button b, bool on)
            {
                b.Background = new SolidColorBrush(on ? Color.FromRgb(0xFF, 0x69, 0xB4) : Color.FromRgb(0x22, 0x22, 0x2E));
                b.Foreground = on ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                b.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0xFF, 0x69, 0xB4) : Color.FromRgb(0x44, 0x44, 0x4F));
            }
            Set(_btnDiffEasy, _settings.Difficulty == "Easy");
            Set(_btnDiffNormal, _settings.Difficulty == "Normal");
            Set(_btnDiffHard, _settings.Difficulty == "Hard");
        }

        private void MarkDifficultyCustom()
        {
            if (_applyingSliders) return;
            if (_settings.Difficulty != "Custom")
            {
                _settings.Difficulty = "Custom";
                UpdateDifficultyChips();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Advanced settings
        // ─────────────────────────────────────────────────────────────────────

        private void ApplySettingsToSliders()
        {
            bool prev = _applyingSliders;
            _applyingSliders = true;
            ApplySettingsToSlidersCore();
            _applyingSliders = prev;
        }

        private void ApplySettingsToSlidersCore()
        {
            _sliderImageCount.Value = _settings.ImageCount;
            _sliderVideoCount.Value = _settings.VideoCount;
            _sliderImageDur.Value = _settings.ImageDurationSec;
            _sliderVideoDur.Value = _settings.VideoMaxDurationSec;
            _sliderPassTime.Value = _settings.PassTimeSec;
            _txtImageCountVal.Text = _settings.ImageCount.ToString();
            _txtVideoCountVal.Text = _settings.VideoCount.ToString();
            _txtImageDurVal.Text = _settings.ImageDurationSec.ToString();
            _txtVideoDurVal.Text = _settings.VideoMaxDurationSec.ToString();
            _txtPassTimeVal.Text = _settings.PassTimeSec.ToString();
            UpdatePassTimeWarning();

            // Vibration combo
            SelectComboByTag(_cboVibration, _settings.VibrationMode.ToString());
            UpdateVibrationStatus();

            // Reward effect combo
            SelectComboByTag(_cboReward, _settings.RewardEffect.ToString());
            UpdateRewardAudioVisibility();

            // Audio file combo (populate from bundled list). The WPF version forced
            // Foreground=Black because its combo popup was light; Fluent's is dark, so the
            // theme's own foreground is correct here.
            _cboRewardAudio.Items.Clear();
            foreach (var f in GazeMinigameSettings.BundledAudioFiles)
            {
                var item = new ComboBoxItem { Content = f, Tag = f };
                _cboRewardAudio.Items.Add(item);
                if (string.Equals(f, _settings.RewardAudioFile, StringComparison.OrdinalIgnoreCase))
                    _cboRewardAudio.SelectedItem = item;
            }
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            foreach (var obj in combo.Items)
            {
                if (obj is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
        }

        private void UpdateVibrationStatus()
        {
            if (_txtVibrationStatus == null) return;
            if (_settings.VibrationMode == GazeVibrationMode.None)
            {
                _txtVibrationStatus.Text = "";
                return;
            }
            // ponytail: needs App.Haptics.IsConnected, wired when it moves to Core. Until then
            // the honest answer is the not-connected one.
            _txtVibrationStatus.Text = "Haptic device not connected — setting saved but no vibration will fire.";
        }

        private void UpdateRewardAudioVisibility()
        {
            if (_rewardAudioRow != null)
                _rewardAudioRow.IsVisible = _settings.RewardEffect == GazeRewardEffect.Audio;
        }

        private void CboVibration_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_cboVibration?.SelectedItem is ComboBoxItem cbi
                && Enum.TryParse<GazeVibrationMode>(cbi.Tag?.ToString(), out var mode))
            {
                _settings.VibrationMode = mode;
                UpdateVibrationStatus();
            }
        }

        private void CboReward_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_cboReward?.SelectedItem is ComboBoxItem cbi
                && Enum.TryParse<GazeRewardEffect>(cbi.Tag?.ToString(), out var effect))
            {
                _settings.RewardEffect = effect;
                UpdateRewardAudioVisibility();
            }
        }

        private void CboRewardAudio_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_cboRewardAudio?.SelectedItem is ComboBoxItem cbi && cbi.Tag is string fileName)
                _settings.RewardAudioFile = fileName;
        }

        private void SliderImageCount_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        { _settings.ImageCount = (int)e.NewValue; if (_txtImageCountVal != null) _txtImageCountVal.Text = _settings.ImageCount.ToString(); MarkDifficultyCustom(); }
        private void SliderVideoCount_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        { _settings.VideoCount = (int)e.NewValue; if (_txtVideoCountVal != null) _txtVideoCountVal.Text = _settings.VideoCount.ToString(); MarkDifficultyCustom(); }
        private void SliderImageDur_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        { _settings.ImageDurationSec = (int)e.NewValue; if (_txtImageDurVal != null) _txtImageDurVal.Text = _settings.ImageDurationSec.ToString(); UpdatePassTimeWarning(); MarkDifficultyCustom(); }
        private void SliderVideoDur_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        { _settings.VideoMaxDurationSec = (int)e.NewValue; if (_txtVideoDurVal != null) _txtVideoDurVal.Text = _settings.VideoMaxDurationSec.ToString(); UpdatePassTimeWarning(); MarkDifficultyCustom(); }
        private void SliderPassTime_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        { _settings.PassTimeSec = (int)e.NewValue; if (_txtPassTimeVal != null) _txtPassTimeVal.Text = _settings.PassTimeSec.ToString(); UpdatePassTimeWarning(); MarkDifficultyCustom(); }

        private void UpdatePassTimeWarning()
        {
            // Soft warning if the pass-time exceeds an asset's max display time:
            // the user literally can't pass that asset type before it times out.
            // Soft (not hard-clamp) so users can experiment freely.
            if (_txtPassTimeWarn == null) return;
            var problems = new List<string>();
            if (_settings.ImageCount > 0 && _settings.PassTimeSec > _settings.ImageDurationSec)
                problems.Add($"images ({_settings.ImageDurationSec}s)");
            if (_settings.VideoCount > 0 && _settings.PassTimeSec > _settings.VideoMaxDurationSec)
                problems.Add($"videos ({_settings.VideoMaxDurationSec}s)");
            if (problems.Count > 0)
            {
                _txtPassTimeWarn.Text = $"Pass time exceeds {string.Join(" and ", problems)} display time — those rounds will time out before you can pass.";
                _txtPassTimeWarn.IsVisible = true;
            }
            else
            {
                _txtPassTimeWarn.IsVisible = false;
            }
        }

        private void ShowReadyBanner(string text, bool showCalibrateAction = false)
        {
            _txtReadyBanner.Text = text;
            _btnReadyBannerAction.IsVisible = showCalibrateAction;
            _readyBanner.IsVisible = true;
        }

        private void HideReadyBanner()
        {
            _readyBanner.IsVisible = false;
            _btnReadyBannerAction.IsVisible = false;
        }

        private void BtnReadyBannerAction_Click(object? sender, RoutedEventArgs e)
        {
            // Currently the only banner action is "open calibration".
            // ponytail: needs WebcamCalibrationWindow + App.Webcam, wired when they move to Core.
            ShowReadyBanner("Still not calibrated — run a 16-point gaze calibration so the game can read which side you're looking at.", showCalibrateAction: true);
        }

        private void BtnStartGame_Click(object? sender, RoutedEventArgs e)
        {
            // Materialise the gaze packs from the Focus/Ignore assignment: the round
            // builder consumes _packs[0] as the correct target and the rest as noise,
            // so Focus goes first. RefreshSetupState already gated Start on a valid
            // 1-focus + ≥1-ignore + shared-content-type selection, but re-check
            // defensively in case state drifted.
            var focusPack = _library.FirstOrDefault(p => RoleOf(p) == GazePackRole.Focus);
            var ignorePacks = _library.Where(p => RoleOf(p) == GazePackRole.Ignore).ToList();
            if (focusPack == null || ignorePacks.Count == 0)
            {
                ShowReadyBanner("Pick one Focus set and at least one Ignore set first.");
                return;
            }
            _packs.Clear();
            _packs.Add(focusPack);
            _packs.AddRange(ignorePacks);

            // The WPF version gated Start on, in order: webcam consent (WebcamConsentDialog),
            // App.Webcam != null, an off-UI-thread App.Webcam.Start(), and a loaded gaze
            // calibration — each failure staying on this screen with a banner.
            // ponytail: needs WebcamTrackingService, wired when it moves to Core.

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

            // WPF also suspended the main-session FlashService here so its random flashes
            // don't pop over the gaze targets (bug #202), restoring it in Window_Closing.
            // ponytail: needs App.Flash, wired when it moves to Core.

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

            // Auto-clamp the requested counts to what the chosen sets can actually
            // supply. Only when NEITHER type is viable do we surface a clear error.
            if (_settings.ImageCount == 0 && _settings.VideoCount == 0)
                throw new InvalidOperationException("Both round counts are 0. Set at least one image or video round in Advanced settings (or pick a difficulty).");

            bool imagesViable = correct.ImageCount > 0 && noise.Any(p => p.ImageCount > 0);
            bool videosViable = correct.VideoCount > 0 && noise.Any(p => p.VideoCount > 0);
            int imageRounds = imagesViable ? _settings.ImageCount : 0;
            int videoRounds = videosViable ? _settings.VideoCount : 0;

            if (imageRounds == 0 && videoRounds == 0)
                throw new InvalidOperationException("The Focus and Ignore sets don't share any usable content. Pick sets that both have images, or both have videos.");

            var specs = new List<RoundSpec>();
            for (int i = 0; i < imageRounds; i++)
                specs.Add(BuildSpec(AssetType.Image, correct, noise, rng));
            for (int i = 0; i < videoRounds; i++)
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
            // async void: top-level try/catch so a post-await exception (dispatcher shutdown
            // mid-delay, window closed) doesn't bubble up as a crash.
            try
            {
                // True fullscreen for the duration of gameplay so the gaze half-mapping is
                // unambiguous. F11 toggles back during gameplay if the user wants windowed.
                EnterFullscreen();

                ShowScreen(_countdownScreen);
                EnsureWebcamSubscribed();

                foreach (var label in new[] { "3", "2", "1", "GO" })
                {
                    _txtCountdown.Text = label;
                    await Task.Delay(700);
                }

                _results.Clear();
                _currentRoundIdx = -1;
                ShowScreen(_gameplayScreen);
                AdvanceRound();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GazeMinigame: BeginCountdown threw");
            }
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
            _txtRoundInfo.Text = $"Round {_currentRoundIdx + 1} / {_rounds.Count}  ({spec.Type}, {spec.DurationSec}s)";

            // Map "correct" / "noise" onto the actual left/right panes.
            var leftPath = spec.CorrectSide == GameSide.Left ? spec.CorrectPath : spec.NoisePath;
            var rightPath = spec.CorrectSide == GameSide.Left ? spec.NoisePath : spec.CorrectPath;

            _leftPane.Children.Clear();
            _rightPane.Children.Clear();

            if (spec.Type == AssetType.Image)
            {
                var leftImg = BuildImageView(leftPath);
                var rightImg = BuildImageView(rightPath);
                _leftPane.Children.Add(leftImg);
                _rightPane.Children.Add(rightImg);
                AnimateAssetIn(leftImg);
                AnimateAssetIn(rightImg);
                SpawnSparkleBurst(_leftPane);
                SpawnSparkleBurst(_rightPane);
            }
            else
            {
                // ponytail: needs LibVLCSharp's VideoView (and VideoService.SharedLibVLC),
                // wired when video playback moves to Core. A glyph holds the pane meanwhile.
                var leftView = GlyphBlock("🎬");
                var rightView = GlyphBlock("🎬");
                _leftPane.Children.Add(leftView);
                _rightPane.Children.Add(rightView);
                AnimateAssetIn(leftView);
                AnimateAssetIn(rightView);
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

        private static Image BuildImageView(string path)
        {
            var img = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            try
            {
                // ponytail: WPF used XamlAnimatedGif for .gif (BitmapImage renders only the
                // first frame). Avalonia's Bitmap is likewise first-frame-only; wire an
                // animated-GIF decoder when one is available on this head.
                img.Source = new Bitmap(path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GazeMinigame: failed to load image {Path}", path);
            }
            return img;
        }

        /// <summary>
        /// WPF tore down two LibVLC players here with the hard-won STOP → DETACH → DISPOSE
        /// ordering and message-pump waits between each step. Nothing to tear down until
        /// video playback exists on this head.
        /// ponytail: needs LibVLCSharp, wired when video playback moves to Core.
        /// </summary>
        private void DisposeCurrentRoundPlayers(bool synchronous = false) { }

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
            // Round-start grace: pause BOTH gaze accumulation and the duration timeout. The
            // duration is the visible-and-decisional window — the ~1s warm-up shouldn't eat
            // into it.
            if (DateTime.UtcNow < _roundIgnoreGazeUntil)
            {
                _roundStartedAt = DateTime.UtcNow;
                return;
            }
            var spec = _rounds[_currentRoundIdx];

            // Accumulate time on whichever side the gaze is currently on. Pause both
            // accumulators if the face is lost — camera glitches shouldn't penalize the user.
            const double tickMs = 50;
            bool tickedWrong = false;
            if (!_faceLost)
            {
                if (_currentSide == spec.CorrectSide) _correctMs += tickMs;
                else if (_currentSide != GameSide.None) { _wrongMs += tickMs; tickedWrong = true; }
            }

            // Decision thresholds.
            //   Win  = PassTime seconds of correct-side gaze accumulated
            //   Lose = noise-side glance (WrongHoldMs=0 → strict; >0 is a saccade filter).
            //          Gated on tickedWrong so the trivially-true "_wrongMs >= 0" can't fire
            //          from frame 1 before any wrong gaze happened.
            //   Display cap = asset reached its max display duration without either threshold
            //          firing. Resolved from accumulated dwell — never a synthetic Timeout.
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
                // Resolve from observed dwell instead of returning a third "no-decision"
                // state. Lenient on ties (both zero → the user never glanced at noise →
                // Correct).
                var resolved = _correctMs >= _wrongMs ? RoundOutcome.Correct : RoundOutcome.Wrong;
                Log.Information(
                    "GazeMinigame: round {Idx} hit display cap, resolved to {Outcome} (correct={Correct:F0}ms wrong={Wrong:F0}ms passNeeded={Pass:F0}ms)",
                    _currentRoundIdx, resolved, _correctMs, _wrongMs, passTimeMs);
                CompleteRound(resolved);
            }
        }

        private async void CompleteRound(RoundOutcome outcome)
        {
            // async void: see BeginCountdown for reasoning.
            try
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

                // Tear the assets down BEFORE animating the feedback card in: a clean black
                // backdrop, and it avoids the native-video airspace problem. Reward effects /
                // shake fire first, while the assets are still visible.
                switch (outcome)
                {
                    case RoundOutcome.Correct:
                        FireRewardEffect(_settings.RewardEffect);
                        if (_settings.VibrationMode == GazeVibrationMode.OnCorrect) FireVibration("reward");
                        DisposeCurrentRoundPlayers();
                        _leftPane.Children.Clear();
                        _rightPane.Children.Clear();
                        PlayJingle();
                        await ShowFullscreenFeedbackAsync("GOOD GIRL", Color.FromRgb(0xFF, 0x69, 0xB4));
                        break;
                    case RoundOutcome.Wrong:
                        if (_settings.VibrationMode == GazeVibrationMode.OnWrong) FireVibration("punish");
                        await ShakeGameplayAsync();
                        DisposeCurrentRoundPlayers();
                        _leftPane.Children.Clear();
                        _rightPane.Children.Clear();
                        await ShowFullscreenFeedbackAsync("WRONG", Color.FromRgb(0xFF, 0x40, 0x40));
                        break;
                    case RoundOutcome.Timeout:
                        DisposeCurrentRoundPlayers();
                        _leftPane.Children.Clear();
                        _rightPane.Children.Clear();
                        await Task.Delay(500);   // silent buffer
                        break;
                }

                AdvanceRound();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GazeMinigame: CompleteRound threw");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Reward / vibration triggers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// WPF fanned this out to App.Flash.TriggerFlashOnce / App.Bubbles.SpawnOnce ×5 /
        /// PlayRewardAudio / App.MindWipe.TriggerOnce / App.Overlay.PulseOverlays.
        /// ponytail: needs those services, wired when they move to Core.
        /// </summary>
        private static void FireRewardEffect(GazeRewardEffect effect)
            => Log.Debug("GazeMinigame: reward effect {Effect} suppressed (services not on this head)", effect);

        /// <summary>
        /// WPF posted HapticEventKind.GazeReward so the Haptics tab's "Gaze reward" routing row
        /// decides how it feels. ponytail: needs App.Haptics, wired when it moves to Core.
        /// </summary>
        private static void FireVibration(string tag)
            => Log.Debug("GazeMinigame: vibration {Tag} suppressed (haptics not on this head)", tag);

        /// <summary>ponytail: needs NAudio + App.Settings.MasterVolume, wired when audio
        /// playback moves to Core.</summary>
        private static void PlayRewardAudio(string fileName)
            => Log.Debug("GazeMinigame: reward audio {File} suppressed (no audio on this head)", fileName);

        // chime.wav is bundled at Resources/AwarenessPresets/audio/chime.wav and plays on every
        // correct round, independent of the configured RewardEffect.
        private static void PlayJingle() => PlayRewardAudio("chime.wav");

        // ─────────────────────────────────────────────────────────────────────
        //  Inter-round feedback card
        // ─────────────────────────────────────────────────────────────────────

        // Full-screen feedback card. The caller MUST clear the panes first.
        private async Task ShowFullscreenFeedbackAsync(string text, Color color)
        {
            _txtFeedback.Text = text;
            _txtFeedback.Foreground = new SolidColorBrush(color);
            // The WPF DropShadowEffect carried x:Name so its Color could be repointed; naming an
            // effect is AVLN2000 here, so it is rebuilt per outcome instead.
            _txtFeedback.Effect = new DropShadowEffect
            {
                Color = color,
                BlurRadius = 60,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 0.9,
            };

            // WPF's two DoubleAnimations become one transition plus the same waits.
            _feedbackCard.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(100) },
            };
            _feedbackCard.IsVisible = true;
            _feedbackCard.Opacity = 1;
            await Task.Delay(1400);                     // 100ms fade-in + 1300ms hold
            _feedbackCard.Opacity = 0;
            await Task.Delay(120);                      // fade-out + tiny tail
            _feedbackCard.Transitions = null;
            _feedbackCard.Opacity = 0;
            _feedbackCard.IsVisible = false;
        }

        // Brief horizontal shake of the panes container on wrong outcomes. The WPF
        // DoubleAnimationUsingKeyFrames was nine linear frames 40ms apart; that is exactly this
        // loop, so no animation object is needed.
        private async Task ShakeGameplayAsync()
        {
            int[] offsets = { -10, 10, -8, 8, -5, 5, -3, 3, 0 };
            foreach (var o in offsets)
            {
                _gameplayShake.X = o;
                await Task.Delay(40);
            }
            _gameplayShake.X = 0;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Asset entry effects
        // ─────────────────────────────────────────────────────────────────────

        // Fade-in + subtle scale-up on the asset element.
        private static void AnimateAssetIn(Control el)
        {
            var scale = new ScaleTransform(0.95, 0.95);
            el.Opacity = 0;
            el.RenderTransformOrigin = RelativePoint.Center;
            el.RenderTransform = scale;

            var easing = new CubicEaseOut();
            var dur = TimeSpan.FromMilliseconds(300);
            el.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = dur, Easing = easing },
            };
            scale.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = dur, Easing = easing },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = dur, Easing = easing },
            };
            el.Opacity = 1;
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }

        // Pink sparkle particles bursting outward from the pane centre. Self-cleaning: each
        // particle removes itself when its fade-out is done, so Children doesn't pile up.
        private static void SpawnSparkleBurst(Grid host)
        {
            const int count = 12;
            var rng = Random.Shared;
            var pink = Color.FromRgb(0xFF, 0x69, 0xB4);
            for (int i = 0; i < count; i++)
            {
                var t = new TranslateTransform();
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(pink),
                    Effect = new DropShadowEffect
                    {
                        Color = pink,
                        BlurRadius = 12,
                        OffsetX = 0,
                        OffsetY = 0,
                        Opacity = 0.9,
                    },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    RenderTransform = t,
                };
                host.Children.Add(dot);

                double angle = rng.NextDouble() * Math.PI * 2;
                double dist = 80 + rng.NextDouble() * 60;      // 80–140 px
                double dx = Math.Cos(angle) * dist;
                double dy = Math.Sin(angle) * dist;
                int dur = 450 + rng.Next(0, 150);              // 450–600 ms
                var span = TimeSpan.FromMilliseconds(dur);

                t.Transitions = new Transitions
                {
                    new DoubleTransition { Property = TranslateTransform.XProperty, Duration = span, Easing = new CubicEaseOut() },
                    new DoubleTransition { Property = TranslateTransform.YProperty, Duration = span, Easing = new CubicEaseOut() },
                };
                dot.Transitions = new Transitions
                {
                    new DoubleTransition { Property = OpacityProperty, Duration = span, Easing = new CubicEaseIn() },
                };

                t.X = dx;
                t.Y = dy;
                dot.Opacity = 0;

                var dotRef = dot;
                DispatcherTimer.RunOnce(() => { try { host.Children.Remove(dotRef); } catch { } }, span);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Webcam events
        // ─────────────────────────────────────────────────────────────────────

        // WPF subscribed to App.Webcam's OnGazeSide / OnFaceLost / OnFaceFound. OnGazeSide
        // rather than the screen-projected OnGazeMove, because it already runs through the
        // calibrated left/right classifier with hysteresis and a 3-frame stability filter.
        // ponytail: needs WebcamTrackingService, wired when it moves to Core. Until then
        // _currentSide stays None and a round resolves on its display cap.
        // OnFaceFound/OnFaceLost were the only writers of _faceLost; without the service the
        // face is simply never lost.
        private void EnsureWebcamSubscribed() => _faceLost = false;

        private void UnsubscribeWebcam() => _faceLost = false;

        // ─────────────────────────────────────────────────────────────────────
        //  Results
        // ─────────────────────────────────────────────────────────────────────

        private void EndSession()
        {
            _gameRunning = false;
            DisposeCurrentRoundPlayers();
            StopRoundTicker();
            // Drop fullscreen so the user can read the results in their normal windowed
            // environment.
            ExitFullscreen();
            BuildResultsList();
            ShowScreen(_resultsScreen);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Fullscreen
        // ─────────────────────────────────────────────────────────────────────

        // WPF needed a WindowStyle/WindowState/ResizeMode dance (and a re-Activate to order
        // itself above other Topmost windows); Avalonia has a real FullScreen window state.
        private void EnterFullscreen()
        {
            if (_isFullscreen) return;
            _savedState = WindowState;
            WindowState = WindowState.FullScreen;
            Topmost = true;
            Activate();
            _isFullscreen = true;
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen) return;
            Topmost = false;
            WindowState = _savedState == WindowState.FullScreen ? WindowState.Normal : _savedState;
            _isFullscreen = false;
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen) ExitFullscreen(); else EnterFullscreen();
        }

        private void BuildResultsList()
        {
            _resultsList.Children.Clear();

            int correct = _results.Count(r => r.Outcome == RoundOutcome.Correct);
            int wrong = _results.Count(r => r.Outcome == RoundOutcome.Wrong);
            int timeout = _results.Count(r => r.Outcome == RoundOutcome.Timeout);
            // Display-cap rounds resolve to Correct/Wrong by dwell time, so the no-decision
            // bucket should be empty in practice.
            _txtResultsHeadline.Text = timeout > 0
                ? $"{correct} correct  ·  {wrong} wrong  ·  {timeout} no-decision"
                : $"{correct} correct  ·  {wrong} wrong";

            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                var color = r.Outcome switch
                {
                    RoundOutcome.Correct => Color.FromRgb(0x80, 0xE0, 0x80),
                    RoundOutcome.Wrong => Color.FromRgb(0xFF, 0x80, 0x80),
                    _ => Color.FromRgb(0xCC, 0xCC, 0xCC),
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(new TextBlock
                {
                    Text = $"#{i + 1,2}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    Width = 40,
                });
                row.Children.Add(new TextBlock
                {
                    Text = r.Type.ToString(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    Width = 60,
                });
                row.Children.Add(new TextBlock
                {
                    Text = r.Outcome.ToString().ToUpperInvariant(),
                    Foreground = new SolidColorBrush(color),
                    FontWeight = FontWeight.Bold,
                    Width = 110,
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"correct {r.CorrectMs:F0}ms · wrong {r.WrongMs:F0}ms",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 11,
                });
                _resultsList.Children.Add(row);
            }
        }

        private void BtnResultsClose_Click(object? sender, RoutedEventArgs e) => Close();

        private void BtnResultsPlayAgain_Click(object? sender, RoutedEventArgs e)
        {
            _results.Clear();
            _currentRoundIdx = -1;
            // Back to the single setup screen; the prior Focus/Ignore selection and settings
            // are still in place, so the user can just hit Start again.
            HideReadyBanner();
            ShowScreen(_titleScreen);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape) return;

            // WPF raised a MessageBox here ("Quit the current session?") when a game was
            // running. Avalonia has no MessageBox and no package may be added, so the
            // confirmation is not raised — same call ChaosSlotPickerWindow made.
            ExitFullscreen();
            Close();
        }

        private void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            _gameRunning = false;
            StopRoundTicker();
            DisposeCurrentRoundPlayers(synchronous: true);
            UnsubscribeWebcam();
            // WPF also resumed the main-session FlashService here, but only when the engine was
            // still running (bug #221). ponytail: needs App.Flash + App.IsEngineRunning, wired
            // when they move to Core.
        }
    }
}
