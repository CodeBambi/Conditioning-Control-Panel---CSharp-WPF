using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.Avatar;
using ConditioningControlPanel.Core.Services.AvatarTube;
using ConditioningControlPanel.Core.Services.Moderation;
using ModerationSource = ConditioningControlPanel.Core.Services.Moderation.ModerationSource;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Avalonia.Views;
using CoreApp = ConditioningControlPanel.CoreApp;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS0169 // Avalonia port: unused stub fields kept for future companion/avatar work
#pragma warning disable CS0414
#pragma warning disable CS0649

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow : Window
    {
        private readonly ILogger<AvatarTubeWindow>? _logger;
        private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService? _settings;
        private readonly IAudioPlayer? _audioPlayer;
        private readonly IProgressionService? _progression;
        private readonly ISessionService? _sessionService;
        private readonly global::ConditioningControlPanel.IModService? _modService;

        // lot-8 C2: event sources for the previously-orphaned companion-reaction handlers
        // (assigned in WireCompanionReactionEvents(), so not readonly).
        private global::ConditioningControlPanel.Core.Services.Video.IVideoService? _videoService;
        private global::ConditioningControlPanel.Core.Services.Flash.IFlashService? _flashService;
        private global::ConditioningControlPanel.Core.Services.Chaos.IBubbleService? _bubbleService;
        private global::ConditioningControlPanel.Core.Services.Companion.ICompanionService? _companionService;
        private global::ConditioningControlPanel.Core.Services.Awareness.IAwarenessService? _awarenessService;
        private global::ConditioningControlPanel.Avalonia.Services.Subliminal.AvaloniaSubliminalService? _subliminalSource;
        private global::ConditioningControlPanel.Avalonia.Services.MindWipe.AvaloniaMindWipeService? _mindWipeSource;

        private readonly Window? _parentWindow;
        private bool _isAttached = true;
        private readonly Random _random = new();

        private readonly DispatcherTimer _poseTimer;
        private Bitmap[] _avatarPoses = Array.Empty<Bitmap>();
        private int _currentPoseIndex;
        private int _currentAvatarSet = 1;
        private int _selectedAvatarSet = 1;
        private int _maxUnlockedSet = 1;
        private bool _useAnimatedAvatar;

        private bool _circeEmoteMode;
        private bool _portraitMode;

        private DispatcherTimer? _cooldownTickTimer;
        private string? _normalChatPlaceholder;
        private DateTime? _cooldownEndsAt;

        private bool _isInputVisible;
        private bool _isShowingChatHistory;

        private readonly Queue<(string text, SpeechSource source, string? emotionLineId, string? mood)> _speechQueue = new();
        private bool _isGiggling;
        private bool _isWaitingForAi;
        private bool _isShowingAiBubble;
        private bool _isMuted;
        private bool _isMouseOverSpeechBubble;
        private bool _isPlayingUninterruptibleClip;

        // Context-menu toggle state (engine/takeover fall back to AppSettings when no service seam is registered)
        private bool _engineEnabled;
        private bool _takeoverEnabled;
        private bool _whispersMuted;
        private bool _browserAudioPaused;
        private DateTime _lastSpeechEndTime = DateTime.MinValue;
        private SpeechSource _lastSpeechSource = SpeechSource.Preset;
        private int _lastSpeechLength;
        private DateTime _lastAiBubbleUtc = DateTime.MinValue;

        private int _presetGiggleCounter;
        private DateTime _lastClickTime = DateTime.MinValue;
        private DateTime _lastInteractionTime = DateTime.MinValue;
        private readonly List<DateTime> _rapidClickTimestamps = new();
        private int _animationRefreshClickCount;
        private int _interactionCount;

        private DispatcherTimer? _speechTimer;
        private DispatcherTimer? _speechDelayTimer;
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _triggerTimer;
        private DispatcherTimer? _randomBubbleTimer;
        private DispatcherTimer? _zOrderRefreshTimer;
        private DispatcherTimer? _thinkingTimer;
        private string _thinkingPhraseBase = string.Empty;
        private int _thinkingTickCount;
        private DispatcherTimer? _typewriterTimer;
        private string _typewriterFullText = string.Empty;
        private int _typewriterIndex;
        private DispatcherTimer? _speechLeadInTimer;
        private DispatcherTimer? _mutedIndicatorTimer;
        private DispatcherTimer? _fullscreenCheckTimer;
        private DispatcherTimer? _floatTimer;
        private DispatcherTimer? _companionGreetingDebounce;
        private IAchievementService? _achievementService;
        private readonly global::ConditioningControlPanel.IBarkService? _barkService;

        private int _flashCounter;
        private int _subliminalCounter;
        private int _bubblePopCounter;
        private int _mindWipeCounter;
        private int _brainDrainCounter;

        private const string NoteClipCaption = "\u2661";
        private const double StartupCooldownSeconds = 3.0;
        private const double MinSpeechDelaySeconds = 2.0;
        private const double AiSpeechBonusSeconds = 5.0;
        private const int LongTextThreshold = 100;
        private const double PerCharDelaySeconds = 0.02;
        private const double SpeechLeadInSeconds = 0.6;
        private const int MaxChatHistorySize = 100;
        private const double MinScale = 0.5;
        private const double MaxScale = 1.5;
        private const double ScaleStep = 0.25;

        private const double DesignWidth = 780;
        private const double DesignHeight = 1020;
        private const double BaseOffsetFromParent = -350;
        private const double VerticalOffset = 20;
        private const double FloatDistance = 4;
        private const double FloatDuration = 2.0;

        private double _scaleFactor = 1.0;
        private double _currentScale = 1.0;
        private double _floatPhase;
        private bool _hiddenForFullscreen;
        private bool _wasAttachedBeforeFullscreen;
        private bool _hiddenForChaos;
        private bool _reattachAfterChaos;
        private bool _chaosRunActive;

        private bool _isDragging;
        private PixelPoint _dragStartWindowPos;
        private global::Avalonia.Point _dragStartPointerPos;

        private bool IsAvatarVisibleOnScreen => IsVisible && !IsEffectivelyMinimized();

        public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

        public bool IsSpeaking => _isGiggling;

        private volatile bool _isSpeakingAudio;
        /// <summary>
        /// True while a spoken companion voice clip is actively playing. Mirrors WPF
        /// AvatarTubeWindow.IsSpeakingAudio so the awareness busy-retry (#463) holds until she is free.
        /// Set when a phrase/bark voice line begins and cleared when audio stops or the bubble times out,
        /// so it always self-heals and never sticks (the shared IAudioPlayer is fire-and-forget).
        /// </summary>
        public bool IsSpeakingAudio => _isSpeakingAudio;
        public int CurrentAvatarSet => _currentAvatarSet;

        public AvatarTubeWindow() : this(null) { }

        public AvatarTubeWindow(Window? parentWindow)
        {
            InitializeComponent();

            // Size the content before the window is shown so SizeToContent doesn't blow up
            // to the unbounded Viewbox desired size.
            CalculateScaleFactor();

            _logger = App.Services.GetRequiredService<ILogger<AvatarTubeWindow>>();
            _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
            _audioPlayer = App.Services.GetService<IAudioPlayer>();
            _progression = App.Services.GetService<IProgressionService>();
            _sessionService = App.Services.GetService<ISessionService>();
            if (_sessionService != null)
                _sessionService.SessionStopped += OnSessionStopped;
            _modService = App.Services.GetService<global::ConditioningControlPanel.IModService>();
            _resourceResolver = App.Services.GetService<AvaloniaModResourceResolver>();

            try { InitTakeoverCountdownBar(); } catch { }
            _portraitService = App.Services.GetService<IAvatarPortraitService>();
            if (_modService != null)
                _modService.ActiveModChanged += OnModChanged;
_parentWindow = parentWindow;
            // Do not set Owner - it causes black-window/minimize artifacts.
            // Z-order and visibility are managed manually via parent events and Win32 topmost toggles.

            _poseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _poseTimer.Tick += (_, _) => PoseTimer_Tick();

            Loaded += (_, _) => ApplyChatShortcutTo(this);
            KeyDown += AvatarTubeWindow_PreviewKeyDown;
            PointerPressed += Window_PointerPressed;
            PointerReleased += Window_PointerReleased;
            PointerMoved += Window_PointerMoved;
            PointerWheelChanged += Window_PointerWheelChanged;

            ChatHistoryList.ItemsSource = ChatHistory;

            int playerLevel = _settings?.Current?.PlayerLevel ?? 1;
            _maxUnlockedSet = GetAvatarSetForLevel(playerLevel);
            _selectedAvatarSet = _settings?.Current?.SelectedAvatarSet ?? _maxUnlockedSet;
            _selectedAvatarSet = Math.Clamp(_selectedAvatarSet, 1, _maxUnlockedSet);
            _currentAvatarSet = _selectedAvatarSet;

            // Fall back if the saved set isn't supported by the active mod.
            var supportedSetsInit = GetUnlockedAvatarSets(playerLevel);
            if (supportedSetsInit.Length > 0 && !supportedSetsInit.Contains(_currentAvatarSet))
            {
                _selectedAvatarSet = supportedSetsInit[supportedSetsInit.Length - 1];
                _currentAvatarSet = _selectedAvatarSet;
            }

            // BambiSleep / SissyHypno ship a single animated emote set and hide the picker.
            if (IsSingleEmoteAvatarMod(out int emoteOnlySetInit))
                _currentAvatarSet = _selectedAvatarSet = emoteOnlySetInit;

            _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);
            _avatarPoses = LoadAvatarPoses(_currentAvatarSet);

            if (!_useAnimatedAvatar && _avatarPoses.Length > 0)
                ImgAvatar.Source = _avatarPoses[0];

            ApplyAvatarTransform(_currentAvatarSet);
            UpdateTitleDisplay(playerLevel);
            UpdateNavigationArrows();

            if (UsePortraitSystem())
                TryEnterPortraitMode();
            else
                TryUpdateCirceEmoteMode();

            // Apply the active mod's tube art and layout offsets on startup.
            SetTubeStyle(!_isAttached);
            ApplyTubeLayoutOffsets();

            WireParentEvents();

            _achievementService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IAchievementService>();
            if (_achievementService != null)
            {
                _achievementService.AchievementUnlocked += OnAchievementUnlocked;
            }

            _barkService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<global::ConditioningControlPanel.IBarkService>();
            if (_barkService is global::ConditioningControlPanel.Avalonia.Services.AvaloniaBarkService abs)
            {
                abs.AvatarClicked += OnBarkAvatarClicked;
                abs.BarkRequested += OnBarkRequested;
            }

            WireCompanionReactionEvents();

            StartIdleTimer();
            StartTriggerTimer();
            StartRandomBubbleTimer();
            WireModerationCounter();

            _logger?.LogInformation("AvatarTubeWindow initialized with avatar set {Set} for level {Level}", _currentAvatarSet, playerLevel);
        }

        /// <summary>
        /// lot-8 C2: subscribe the previously-orphaned companion-reaction handlers to their event
        /// sources, mirroring the WPF AvatarTubeWindow subscription block. Sources are resolved through
        /// DI; each subscription is null-guarded so absent seams (e.g. awareness has no registered
        /// implementation yet) are simply skipped. Field-backed so OnClosed can unsubscribe cleanly.
        /// </summary>
        private void WireCompanionReactionEvents()
        {
            var services = global::ConditioningControlPanel.Avalonia.App.Services;
            if (services == null) return;

            _videoService = services.GetService<global::ConditioningControlPanel.Core.Services.Video.IVideoService>();
            if (_videoService != null)
            {
                _videoService.VideoAboutToStart += OnVideoAboutToStart;
                _videoService.VideoEnded += OnVideoEnded;
            }

            _flashService = services.GetService<global::ConditioningControlPanel.Core.Services.Flash.IFlashService>();
            if (_flashService != null)
            {
                _flashService.FlashAboutToDisplay += OnFlashAboutToDisplay;
                _flashService.FlashClicked += OnFlashClicked;
            }

            _subliminalSource = services.GetService<global::ConditioningControlPanel.Core.Services.Subliminal.ISubliminalService>()
                as global::ConditioningControlPanel.Avalonia.Services.Subliminal.AvaloniaSubliminalService;
            if (_subliminalSource != null)
                _subliminalSource.SubliminalDisplayed += OnSubliminalDisplayed;

            _mindWipeSource = services.GetService<global::ConditioningControlPanel.Core.Services.MindWipe.IMindWipeService>()
                as global::ConditioningControlPanel.Avalonia.Services.MindWipe.AvaloniaMindWipeService;
            if (_mindWipeSource != null)
                _mindWipeSource.MindWipeTriggered += OnMindWipeTriggered;

            _bubbleService = services.GetService<global::ConditioningControlPanel.Core.Services.Chaos.IBubbleService>();
            if (_bubbleService != null)
            {
                _bubbleService.OnBubblePopped += OnBubblePopped;
                _bubbleService.OnBubbleMissed += OnBubbleMissed;
            }

            _companionService = services.GetService<global::ConditioningControlPanel.Core.Services.Companion.ICompanionService>();
            if (_companionService != null)
            {
                // Avatar-line side of level-up (speech bubble). The toast lives in MainWindowViewModel
                // (AddNotification), so this side never raises a toast — no double-fire.
                _companionService.LevelUp += OnCompanionLevelUp;
                _companionService.CompanionSwitched += OnCompanionSwitched;
            }

            if (_progression != null)
                _progression.LevelUp += OnLevelUp;

            // Awareness has no registered implementation in the port yet; guarded so this becomes
            // live automatically once an IAwarenessService seam is registered.
            _awarenessService = services.GetService<global::ConditioningControlPanel.Core.Services.Awareness.IAwarenessService>();
            if (_awarenessService != null)
            {
                _awarenessService.ActivityChanged += OnActivityChanged;
                _awarenessService.StillOnActivity += OnStillOnActivity;
                _awarenessService.Start();
            }
        }

        private void UnwireCompanionReactionEvents()
        {
            if (_videoService != null)
            {
                _videoService.VideoAboutToStart -= OnVideoAboutToStart;
                _videoService.VideoEnded -= OnVideoEnded;
            }
            if (_flashService != null)
            {
                _flashService.FlashAboutToDisplay -= OnFlashAboutToDisplay;
                _flashService.FlashClicked -= OnFlashClicked;
            }
            if (_subliminalSource != null)
                _subliminalSource.SubliminalDisplayed -= OnSubliminalDisplayed;
            if (_mindWipeSource != null)
                _mindWipeSource.MindWipeTriggered -= OnMindWipeTriggered;
            if (_bubbleService != null)
            {
                _bubbleService.OnBubblePopped -= OnBubblePopped;
                _bubbleService.OnBubbleMissed -= OnBubbleMissed;
            }
            if (_companionService != null)
            {
                _companionService.LevelUp -= OnCompanionLevelUp;
                _companionService.CompanionSwitched -= OnCompanionSwitched;
            }
            if (_progression != null)
                _progression.LevelUp -= OnLevelUp;
            if (_awarenessService != null)
            {
                _awarenessService.ActivityChanged -= OnActivityChanged;
                _awarenessService.StillOnActivity -= OnStillOnActivity;
            }
        }

        private void WireParentEvents()
        {
            if (_parentWindow == null) return;
            _parentWindow.PositionChanged += ParentWindow_PositionChanged;
            _parentWindow.PropertyChanged += ParentWindow_PropertyChanged;
            // WPF parity (AvatarTube/AvatarTubeWindow.xaml.cs L189-194): the WPF head also
            // wires parent SizeChanged and Activated to reposition. A resize (user drag,
            // theme/mod relayout) changes ClientSize WITHOUT firing PositionChanged, and
            // Activated is the settled "second chance" pass after minimize/restore and
            // fullscreen exits - without both, the tube rests in a stale spot.
            _parentWindow.SizeChanged += ParentWindow_SizeChanged;
            _parentWindow.Activated += ParentWindow_Activated;
            _parentWindow.Closed += ParentWindow_Closed;
        }

        private bool IsEffectivelyMinimized()
        {
            return _parentWindow?.WindowState == WindowState.Minimized || !IsVisible;
        }

        private void PoseTimer_Tick()
        {
            if (_avatarPoses.Length <= 1 || _useAnimatedAvatar || _portraitMode) return;
            _currentPoseIndex = (_currentPoseIndex + 1) % _avatarPoses.Length;
            if (ImgAvatar != null) ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
        }

        private void ContentViewbox_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // TODO: mirror the explicit SizeToContent=Manual sizing used in WPF to avoid layered-window hangs.
        }

        private void SpeechBubble_MouseEnter(object? sender, PointerEventArgs e)
        {
            _isMouseOverSpeechBubble = true;
        }

        private void SpeechBubble_MouseLeave(object? sender, PointerEventArgs e)
        {
            _isMouseOverSpeechBubble = false;
        }

        private void AvatarTubeWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isShowingChatHistory)
            {
                ExitChatHistoryMode();
                e.Handled = true;
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed && !_isAttached)
            {
                _isDragging = true;
                _dragStartPointerPos = point.Position;
                _dragStartWindowPos = Position;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (_isInputVisible && InputPanel != null)
            {
                var source = e.Source as Visual;
                if (source != null && !IsDescendantOf(source, InputPanel))
                    HideInputPanel();
            }
        }

        private void Window_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(this);
            var delta = pos - _dragStartPointerPos;
            Position = new PixelPoint(
                _dragStartWindowPos.X + (int)delta.X,
                _dragStartWindowPos.Y + (int)delta.Y);
            ClampAvatarPosition();
        }

        private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                e.Pointer.Capture(null);
            }
        }

        private void Window_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (!_isAttached && e.KeyModifiers == KeyModifiers.Control)
            {
                var delta = e.Delta.Y > 0 ? ScaleStep : -ScaleStep;
                double next = Math.Clamp(_currentScale + delta, MinScale, MaxScale);
                if (next != _currentScale)
                {
                    _currentScale = next;
                    ApplyScale();
                    UpdateResizeMenuState();
                }
                e.Handled = true;
            }
        }

        private void BtnCloseChatHistory_Click(object? sender, RoutedEventArgs e)
        {
            ExitChatHistoryMode();
        }

        private void ChatHistoryText_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string text && tb.Inlines != null)
                BuildLinkedInlines(text, tb.Inlines);
        }

        private void TxtUserInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = SendChatMessageAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ToggleInputPanel();
                e.Handled = true;
            }
        }

        private void BtnSendChat_Click(object? sender, RoutedEventArgs e)
        {
            _ = SendChatMessageAsync();
        }

        private void ImgAvatar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                HideInputPanel();
                e.Handled = true;
                return;
            }

            ImgAvatar_MouseLeftButtonDown();
            e.Handled = true;
        }

        private void AvatarContextMenu_Opened(object? sender, EventArgs e)
        {
            UpdateQuickMenuState();
            RefreshEmoteMenuItemsForRemoteState();
        }

        // ===== Menu item click handlers =====
        private void MenuItemTalkToBambi_Click(object? sender, RoutedEventArgs e) => ShowInputPanel();
        private void MenuItemDetach_Click(object? sender, RoutedEventArgs e) => Detach();
        private void MenuItemAttach_Click(object? sender, RoutedEventArgs e) => Attach();
        private void MenuItemShrink_Click(object? sender, RoutedEventArgs e)
        {
            if (!_isAttached && _currentScale > MinScale)
            {
                _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                ApplyScale();
                UpdateResizeMenuState();
            }
        }
        private void MenuItemGrow_Click(object? sender, RoutedEventArgs e)
        {
            if (!_isAttached && _currentScale < MaxScale)
            {
                _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                ApplyScale();
                UpdateResizeMenuState();
            }
        }
        private void MenuItemDismiss_Click(object? sender, RoutedEventArgs e) => HideTube();
        private void MenuItemEngine_Click(object? sender, RoutedEventArgs e)
        {
            // WPF parity (#479, AvatarTubeWindow.ChatInput.cs:947-985): this item starts/stops
            // the conditioning ENGINE (flash/video/subliminal/...), NOT Awareness. The earlier
            // port wired it to IAwarenessEngine/AwarenessModeEnabled, a divergence that also let
            // a right-click kill the engine mid lockdown/strict video (the #479 teardown crash).
            // Guards mirror WPF's BtnStart_Click.

            // Guard 1: a connected remote controller owns the session — do not toggle
            // (ChatInput.cs:957).
            if (App.Services.GetService<IRemoteControlService>()?.ControllerConnected == true)
                return;

            // Flash.IsRunning is WPF's engine-state proxy (ChatInput.cs:959, 1320).
            var flash = App.Services.GetService<global::ConditioningControlPanel.Core.Services.Flash.IFlashService>();
            bool engineRunning = flash?.IsRunning == true;

            // MainWindowViewModel is a DI singleton (the dashboard DataContext). StopEngine is
            // the WPF MainWindow.StopEngine() equivalent (StartStop.cs:270-353: tears down whatever
            // is running, no dialog); StartEngineOnlyRun is the WPF StartEngine() equivalent
            // (App.axaml.cs:346-348). Both self-guard. (#479)
            var vm = App.Services.GetService<MainWindowViewModel>();
            if (engineRunning)
            {
                if (IsEngineStopLocked())
                {
                    Giggle("nuh-uh~ no stopping now, you're locked in~");
                    return;
                }
                vm?.StopEngine();
                Giggle("Engine stopped~");
            }
            else
            {
                vm?.StartEngineOnlyRun();
                Giggle("Engine started! *giggles*");
            }
            UpdateQuickMenuState();
        }

        /// <summary>
        /// True when stopping the engine must be refused: lockdown mode, a strict-locked
        /// mandatory video, or a strict-locked bubble-count game is in progress (#479).
        /// Mirrors WPF AvatarTubeWindow.ChatInput.cs:982-985.
        /// </summary>
        private bool IsEngineStopLocked()
        {
            var settings = _settings?.Current;
            return App.Services.GetService<ILockdownService>()?.IsActive == true
                || App.Services.GetService<global::ConditioningControlPanel.Core.Services.Video.IVideoService>()?.IsStrictActive == true
                || (App.Services.GetService<IBubbleCountService>()?.IsBusy == true
                    && settings?.BubbleCountStrictLock == true);
        }
        private void MenuItemTriggerMode_Click(object? sender, RoutedEventArgs e)
        {
            if (_settings?.Current == null) return;
            _settings.Current.TriggerModeEnabled = !_settings.Current.TriggerModeEnabled;
            _settings.Save();
            RestartTriggerTimer();
            UpdateQuickMenuState();
        }
        private void MenuItemBambiTakeover_Click(object? sender, RoutedEventArgs e)
        {
            var svc = App.Services.GetService<IBambiTakeoverService>();
            bool next;
            if (svc != null)
            {
                next = !svc.IsActive;
                svc.IsActive = next;
            }
            else if (_settings?.Current != null)
            {
                next = !_settings.Current.AutonomyModeEnabled;
                _settings.Current.AutonomyModeEnabled = next;
                if (next && !_settings.Current.AutonomyConsentGiven)
                    _settings.Current.AutonomyConsentGiven = true;
                _settings.Save();
            }
            else
            {
                next = !_takeoverEnabled;
            }
            _takeoverEnabled = next;
            UpdateQuickMenuState();
        }
        private void MenuItemMute_Click(object? sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            Giggle(_isMuted ? "Muted~" : "Unmuted~");
            UpdateQuickMenuState();
        }
        private void MenuItemEmote_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string emote } && _circeEngine?.IsActive == true)
            {
                _circeEngine.PlayEmote(emote, null, emote, null);
            }
        }
        private void MenuItemShowChatHistory_Click(object? sender, RoutedEventArgs e) => EnterChatHistoryMode();
        private void MenuItemMuteWhispers_Click(object? sender, RoutedEventArgs e)
        {
            var svc = App.Services.GetService<IWhisperService>();
            bool muted;
            if (svc != null)
            {
                muted = !svc.IsMuted;
                svc.IsMuted = muted;
            }
            else if (_settings?.Current != null)
            {
                bool currentlyMuted = _settings.Current.SubAudioEnabled != true;
                muted = !currentlyMuted;
                _settings.Current.SubAudioEnabled = !muted;
                _settings.Save();
            }
            else
            {
                muted = !_whispersMuted;
            }
            _whispersMuted = muted;
            UpdateQuickMenuState();
        }
        private void MenuItemPauseBrowser_Click(object? sender, RoutedEventArgs e)
        {
            var svc = App.Services.GetService<IBrowserAudioService>();
            bool next = svc != null ? !svc.IsPaused : !_browserAudioPaused;
            if (svc != null)
                svc.IsPaused = next;
            _browserAudioPaused = next;
            UpdateQuickMenuState();
        }

        private void BtnPrevAvatar_Click(object? sender, PointerPressedEventArgs e)
        {
            var sets = EffectiveAvatarSets();
            int idx = Array.IndexOf(sets, _currentAvatarSet);
            if (idx > 0) SwitchToAvatarSet(sets[idx - 1]);
        }

        private void BtnNextAvatar_Click(object? sender, PointerPressedEventArgs e)
        {
            var sets = EffectiveAvatarSets();
            int idx = Array.IndexOf(sets, _currentAvatarSet);
            if (idx >= 0 && idx < sets.Length - 1) SwitchToAvatarSet(sets[idx + 1]);
        }

        // ===== Open chat command =====
        public static readonly ICommand OpenChatCommand = new RelayCommand(param =>
        {
            if (param is AvatarTubeWindow tube) tube.OpenChatInput();
        });

        public static void ApplyChatShortcutTo(Window window)
        {
            if (window is not AvatarTubeWindow tube) return;
            var settings = CoreApp.Settings?.Current?.CompanionPrompt;
            if (settings == null) return;

            if (!TryParseModifiers(settings.ChatShortcutModifiers, out var modifiers))
                modifiers = KeyModifiers.Control;
            if (!Enum.TryParse<Key>(settings.ChatShortcutKey, true, out var key))
                key = Key.T;

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(key, modifiers),
                Command = OpenChatCommand,
                CommandParameter = tube
            });
        }

        public static string FormatChatShortcut()
        {
            var settings = CoreApp.Settings?.Current?.CompanionPrompt;
            if (settings == null) return "Ctrl+T";
            var mods = SerializeModifiers(TryParseModifiers(settings.ChatShortcutModifiers, out var m) ? m : KeyModifiers.Control);
            return string.IsNullOrEmpty(mods) ? settings.ChatShortcutKey : $"{mods}+{settings.ChatShortcutKey}";
        }

        public void OpenChatInput()
        {
            if (Dispatcher.UIThread.CheckAccess()) ShowInputPanel();
            else Dispatcher.UIThread.Post(ShowInputPanel);
        }

        private void UpdateQuickMenuState()
        {
            if (MenuItemMute != null) MenuItemMute.Header = _isMuted ? "Unmute" : "Mute";
            if (MenuItemTriggerMode != null && _settings?.Current != null)
                MenuItemTriggerMode.Header = _settings.Current.TriggerModeEnabled ? "Stop trigger mode" : "Start trigger mode";

            // Engine start/stop (WPF parity, #479): Flash.IsRunning is the engine-state proxy
            // (AvatarTubeWindow.ChatInput.cs:1320), NOT Awareness — that was the port divergence.
            var flash = App.Services.GetService<global::ConditioningControlPanel.Core.Services.Flash.IFlashService>();
            bool engineRunning = flash?.IsRunning == true;
            _engineEnabled = engineRunning;
            bool remoteControlled = App.Services.GetService<IRemoteControlService>()?.ControllerConnected == true;
            if (MenuItemEngine != null)
            {
                MenuItemEngine.ToggleType = MenuItemToggleType.CheckBox;
                MenuItemEngine.IsChecked = engineRunning;
                MenuItemEngine.Header = engineRunning ? Loc.Get("menu_stop_engine") : Loc.Get("menu_start_engine");
                MenuItemEngine.Foreground = engineRunning ? AppBrush("DangerBrush", new SolidColorBrush(Color.FromRgb(255, 99, 71))) : new SolidColorBrush(Color.FromRgb(144, 238, 144));
                // WPF parity (ChatInput.cs:1361-1363, 1386): disabled while remote-controlled,
                // or while the engine is running AND a stop would be refused (lockdown / strict
                // video / strict bubble game). Starting is always allowed when not remote-controlled.
                MenuItemEngine.IsEnabled = !remoteControlled && !(engineRunning && IsEngineStopLocked());
            }

            // Bambi Takeover toggle
            bool takeoverOn;
            var takeover = App.Services.GetService<IBambiTakeoverService>();
            if (takeover != null)
                takeoverOn = takeover.IsActive;
            else
                takeoverOn = _settings?.Current?.AutonomyModeEnabled ?? _takeoverEnabled;
            _takeoverEnabled = takeoverOn;
            if (MenuItemBambiTakeover != null)
            {
                MenuItemBambiTakeover.ToggleType = MenuItemToggleType.CheckBox;
                MenuItemBambiTakeover.IsChecked = takeoverOn;
                var label = Loc.Get("menu_takeover");
                MenuItemBambiTakeover.Header = takeoverOn ? Loc.GetF("menu_takeover_on_format", label) : Loc.GetF("menu_takeover_off_format", label);
            }

            // Mute whispers toggle
            bool whispersMuted;
            var whisperSvc = App.Services.GetService<IWhisperService>();
            if (whisperSvc != null)
                whispersMuted = whisperSvc.IsMuted;
            else
                whispersMuted = _settings?.Current?.SubAudioEnabled != true;
            _whispersMuted = whispersMuted;
            if (MenuItemMuteWhispers != null)
            {
                MenuItemMuteWhispers.ToggleType = MenuItemToggleType.CheckBox;
                MenuItemMuteWhispers.IsChecked = whispersMuted;
                MenuItemMuteWhispers.Header = whispersMuted ? Loc.Get("menu_mute_whispers_on") : Loc.Get("menu_mute_whispers_off");
                MenuItemMuteWhispers.Foreground = whispersMuted ? AppBrush("DangerBrush", new SolidColorBrush(Color.FromRgb(255, 99, 71))) : AppBrush("TextLightBrush", Brushes.White);
            }

            // Pause browser audio toggle
            bool browserPaused;
            var browserAudio = App.Services.GetService<IBrowserAudioService>();
            if (browserAudio != null)
                browserPaused = browserAudio.IsPaused;
            else
                browserPaused = _browserAudioPaused;
            _browserAudioPaused = browserPaused;
            if (MenuItemPauseBrowser != null)
            {
                MenuItemPauseBrowser.ToggleType = MenuItemToggleType.CheckBox;
                MenuItemPauseBrowser.IsChecked = browserPaused;
                MenuItemPauseBrowser.Header = browserPaused ? Loc.Get("menu_resume_browser") : Loc.Get("menu_pause_browser");
                MenuItemPauseBrowser.Foreground = browserPaused ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) : AppBrush("TextLightBrush", Brushes.White);
            }

            UpdateContextMenuForState();
            UpdateResizeMenuState();
            RefreshEmoteMenuItemsForRemoteState();
        }
        private void UpdateResizeMenuState()
        {
            if (MenuItemShrink != null) MenuItemShrink.IsEnabled = !_isAttached && _currentScale > MinScale;
            if (MenuItemGrow != null) MenuItemGrow.IsEnabled = !_isAttached && _currentScale < MaxScale;
        }
        private void UpdateContextMenuForState()
        {
            if (MenuItemDetach != null) MenuItemDetach.IsVisible = _isAttached;
            if (MenuItemAttach != null) MenuItemAttach.IsVisible = !_isAttached;
            if (MenuItemShrink != null) MenuItemShrink.IsVisible = !_isAttached;
            if (MenuItemGrow != null) MenuItemGrow.IsVisible = !_isAttached;
        }
        private void RefreshEmoteMenuItemsForRemoteState()
        {
            var emoteItems = new[] { MenuItemEmote1, MenuItemEmote2, MenuItemEmote3, MenuItemEmote4, MenuItemEmote5 };
            if (_circeEngine?.IsActive != true)
            {
                foreach (var mi in emoteItems) if (mi != null) mi.IsVisible = false;
                return;
            }
            var pool = new[] { "giggle", "sultry", "wink", "tease", "blowkiss" };
            for (int i = 0; i < emoteItems.Length; i++)
            {
                var mi = emoteItems[i];
                if (mi == null) continue;
                mi.Header = i < pool.Length ? char.ToUpperInvariant(pool[i][0]) + pool[i][1..] : "Emote";
                mi.IsVisible = i < pool.Length;
                mi.Tag = i < pool.Length ? pool[i] : null;
            }
        }

        // ===== Input panel =====
        private void ToggleInputPanel()
        {
            _isInputVisible = !_isInputVisible;
            if (InputPanel != null) InputPanel.IsVisible = _isInputVisible;
            if (_isInputVisible) FocusInputAfterLayout();
        }
        private void ShowInputPanel()
        {
            _isInputVisible = true;
            if (InputPanel != null) InputPanel.IsVisible = true;
            ForceForegroundWindow();
            FocusInputAfterLayout();
        }
        private void HideInputPanel()
        {
            _isInputVisible = false;
            if (InputPanel != null) InputPanel.IsVisible = false;
        }
        private void FocusInputAfterLayout()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    TxtUserInput?.Focus();
                    TxtUserInput?.SelectAll();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("AvatarTube: focus chat input failed: {Error}", ex.Message);
                }
            }, DispatcherPriority.ContextIdle);
        }

        // ===== Chat history =====
        private void EnterChatHistoryMode()
        {
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isShowingChatHistory = true;
            if (TxtChatHistoryEmpty != null)
                TxtChatHistoryEmpty.IsVisible = ChatHistory.Count == 0;
            if (SpeechScroller != null) SpeechScroller.IsVisible = false;
            if (ChatHistoryView != null) ChatHistoryView.IsVisible = true;
            if (AiBadge != null) AiBadge.IsVisible = false;
            if (SpeechBubble != null)
            {
                SpeechBubble.MaxWidth = 600;
                SpeechBubble.IsVisible = true;
            }
            ChatHistoryScroller?.ScrollToEnd();
        }

        private void ExitChatHistoryMode()
        {
            _isShowingChatHistory = false;
            if (ChatHistoryView != null) ChatHistoryView.IsVisible = false;
            if (SpeechScroller != null) SpeechScroller.IsVisible = true;
            if (SpeechBubble != null)
            {
                SpeechBubble.MaxWidth = 380;
                SpeechBubble.IsVisible = false;
            }
        }

        private void AddToChatHistory(string text, bool isUser)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ChatHistory.Add(new ChatMessage { Text = text, IsUser = isUser });
            while (ChatHistory.Count > MaxChatHistorySize) ChatHistory.RemoveAt(0);
        }

        // ===== Chat send =====
        private async Task SendChatMessageAsync()
        {
            var input = TxtUserInput?.Text?.Trim();
            if (string.IsNullOrEmpty(input)) return;

            var counter = App.Services.GetService<IModerationCounter>();
            var counterState = counter?.GetState();
            if (counterState?.CooldownActive == true)
            {
                _logger?.LogInformation("AvatarTubeWindow: chat send swallowed (cooldown active)");
                return;
            }

            if (TxtUserInput != null) TxtUserInput.Text = "";
            ToggleInputPanel();

            bool aiEnabled = _settings?.Current?.AiChatEnabled == true;
            var ai = App.Services.GetService<IAiService>();
            if (aiEnabled && ai != null && ai.IsAvailable)
            {
                try
                {
                    StartThinkingAnimation();
                    var result = await ai.GetBambiReplyExAsync(input);

                    if (result.Refusal != null)
                    {
                        PlayDoubleBounce();
                        ShowModerationRefusalBubble(result.Refusal.Source);
                    }
                    else
                    {
                        AddToChatHistory(input, true);
                        PlayDoubleBounce();
                        GigglePriority(result.Text, aiGenerated: result.IsAiGenerated);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to get AI reply");
                    AddToChatHistory(input, true);
                    GigglePriority(GetRandomBambiPhrase(), aiGenerated: false);
                }
            }
            else
            {
                AddToChatHistory(input, true);
                Giggle(GetRandomBambiPhrase());
            }
        }

        // ===== Speech helpers =====
        private void Giggle(string text, string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return;
            if (_isWaitingForAi || _isShowingAiBubble) return;

            string? emotionLineId = phraseAudioPath != null ? Path.GetFileNameWithoutExtension(phraseAudioPath) : null;

            if (_isGiggling)
            {
                _speechQueue.Enqueue((text, SpeechSource.Preset, emotionLineId, mood));
                return;
            }

            if (!IsSpeechReady())
            {
                _speechQueue.Enqueue((text, SpeechSource.Preset, emotionLineId, mood));
                _isGiggling = true;
                ProcessNextSpeech();
                return;
            }

            _presetGiggleCounter++;
            bool playSound = _presetGiggleCounter % 5 == 0;
            ShowGiggle(text, playSound, SpeechSource.Preset, phraseAudioPath, barkVoice,
                emotionLineId: emotionLineId, mood: mood);
        }

        public void GigglePriority(string text, bool playSound = true, bool aiGenerated = true,
            string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return;
            string? emotionLineId = phraseAudioPath != null ? Path.GetFileNameWithoutExtension(phraseAudioPath) : null;

            if (aiGenerated) _lastAiBubbleUtc = DateTime.UtcNow;
            StopThinkingAnimation();
            _isWaitingForAi = false;
            _speechQueue.Clear();
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isGiggling = false;
            if (aiGenerated) AddToChatHistory(text, false);
            ShowGiggle(text, playSound, aiGenerated ? SpeechSource.AI : SpeechSource.Preset,
                phraseAudioPath, barkVoice, emotionLineId: emotionLineId, mood: mood);
        }

        private void ShowGiggle(string text, bool playSound = false, SpeechSource source = SpeechSource.Preset,
            string? phraseAudioPath = null, bool aiGenerated = false, bool bypassClipLock = false,
            bool barkVoice = false, string? emotionLineId = null, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip && !bypassClipLock) return;
            if (AiBadge != null) AiBadge.IsVisible = aiGenerated;
            if (PolicyBadge != null) PolicyBadge.IsVisible = false;

            if (_isShowingChatHistory)
            {
                _isShowingChatHistory = false;
                if (ChatHistoryView != null) ChatHistoryView.IsVisible = false;
                if (SpeechScroller != null) SpeechScroller.IsVisible = true;
                if (SpeechBubble != null) SpeechBubble.MaxWidth = 380;
            }

            if (_isMuted || !IsAvatarVisibleOnScreen)
            {
                _isGiggling = false;
                _isSpeakingAudio = false;
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = source;
                _lastSpeechLength = text?.Length ?? 0;
                ProcessNextSpeech();
                return;
            }

            _isGiggling = true;
            _isShowingAiBubble = source == SpeechSource.AI;
            StopTypewriter();

            if (_circeEmoteMode)
                CircePlayEmote(emotionLineId, phraseAudioPath, text, mood);

            if (_portraitMode)
                PlayEmotionForLine(emotionLineId, phraseAudioPath, text, mood);

            PopulateSpeechBubble(text);
            AdjustBubbleSize(text);
            if (SpeechBubble != null) SpeechBubble.IsVisible = true;

            StartZOrderRefreshTimer();
            BringAttachedPairToFront();

            Action speak = () =>
            {
                if (!_isGiggling) return;

                StopSpokenAudio();

                if (phraseAudioPath != null)
                {
                    if (barkVoice)
                        PlayBarkVoice(phraseAudioPath);
                    else
                        PlayPhraseAudio(phraseAudioPath);
                }
                else if (playSound)
                {
                    PlayGiggleSound();
                }
                else if (source != SpeechSource.AI)
                {
                    PlayFallbackBubbleSound();
                }

                bool isThinking = source == SpeechSource.AI && _isWaitingForAi;
                bool slowType = source != SpeechSource.AI;
                if (!isThinking)
                    StartTypewriter(text, slowType);
                else
                    PopulateSpeechBubble(text);

                double userSetting = Math.Clamp(_settings?.Current?.BubbleDurationSeconds ?? 2.0, 1.0, 10.0);
                double displayDuration = userSetting;

                if (!isThinking)
                {
                    double typewriterSec = EstimateTypewriterDurationMs(text?.Length ?? 0, slowType) / 1000.0;
                    displayDuration += typewriterSec;

                    if (source == SpeechSource.AI)
                    {
                        const double charsPerSecond = 12.0;
                        const double maxPostTypeSec = 30.0;
                        double readingFloorSec = Math.Min(maxPostTypeSec, (text?.Length ?? 0) / charsPerSecond);
                        double minTotalSec = typewriterSec + Math.Max(userSetting, readingFloorSec);
                        if (minTotalSec > displayDuration) displayDuration = minTotalSec;
                    }
                }

                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };
                var capturedSource = source;
                var capturedLength = text?.Length ?? 0;
                _speechTimer.Tick += (_, _) =>
                {
                    if (_isMouseOverSpeechBubble)
                    {
                        _speechTimer.Interval = TimeSpan.FromSeconds(1);
                        return;
                    }
                    _speechTimer.Stop();
                    StopZOrderRefreshTimer();
                    if (SpeechBubble != null) SpeechBubble.IsVisible = false;
                    _isShowingAiBubble = false;
                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = capturedSource;
                    _lastSpeechLength = capturedLength;
                    _isGiggling = false;
                    _isSpeakingAudio = false;
                    ProcessNextSpeech();
                };
                _speechTimer.Start();
                ResetIdleTimer();
            };

            _speechLeadInTimer?.Stop();
            if (source != SpeechSource.AI)
            {
                int leadInMs = _circeEmoteMode ? (_circeEngine?.AudioLeadInMs ?? 0) : (int)(SpeechLeadInSeconds * 1000);
                if (leadInMs > 0)
                {
                    _speechLeadInTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(leadInMs) };
                    _speechLeadInTimer.Tick += (_, _) =>
                    {
                        _speechLeadInTimer?.Stop();
                        speak();
                    };
                    _speechLeadInTimer.Start();
                }
                else
                {
                    speak();
                }
            }
            else
            {
                speak();
            }
        }

        /// <summary>Public entry point for external services to make the avatar speak a short phrase.</summary>
        public void ShowGiggle(string text) => ShowGiggle(text, playSound: true, source: SpeechSource.Preset);

        private void ProcessNextSpeech()
        {
            if (_speechQueue.Count == 0)
            {
                _isGiggling = false;
                return;
            }

            var (nextText, source, emotionLineId, mood) = _speechQueue.Dequeue();

            double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
            double requiredDelay = CalculateRequiredDelayAfterLastSpeech();
            double remainingDelay = Math.Max(0, requiredDelay - timeSinceLastSpeech);

            if (remainingDelay > 0)
            {
                _speechDelayTimer?.Stop();
                _speechDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(remainingDelay) };
                _speechDelayTimer.Tick += (_, _) =>
                {
                    _speechDelayTimer.Stop();
                    ShowSpeechBySource(nextText, source, emotionLineId, mood);
                };
                _speechDelayTimer.Start();
            }
            else
            {
                ShowSpeechBySource(nextText, source, emotionLineId, mood);
            }
        }

        private void ShowSpeechBySource(string text, SpeechSource source, string? emotionLineId = null, string? mood = null)
        {
            if (source == SpeechSource.Trigger)
                ShowTriggerBubbleImmediate(text);
            else
                ShowGiggle(text, source == SpeechSource.AI, source, mood: mood);
        }

        private void ShowTriggerBubbleImmediate(string text)
        {
            ShowGiggle(text, playSound: false, source: SpeechSource.Trigger);
        }

        private void PopulateSpeechBubble(string text)
        {
            if (TxtSpeech != null && TxtSpeech.Inlines != null)
                BuildLinkedInlines(text, TxtSpeech.Inlines);
        }

        private void AdjustBubbleSize(string text)
        {
            int chars = text?.Length ?? 0;
            double fontSize = chars <= 50 ? 22 : chars <= 120 ? 20 : chars <= 250 ? 18 : 16;
            if (TxtSpeech != null) TxtSpeech.FontSize = fontSize;
            SpeechScroller?.ScrollToHome();
        }

        private void BuildLinkedInlines(string text, InlineCollection target)
        {
            target.Clear();
            if (string.IsNullOrEmpty(text))
            {
                target.Add(new Run(text ?? ""));
                return;
            }

            var segments = FindLinkSegments(text);
            if (segments.Count == 0)
            {
                target.Add(new Run(text));
                return;
            }

            int lastIndex = 0;
            foreach (var segment in segments)
            {
                if (segment.Start > lastIndex)
                    target.Add(new Run(text[lastIndex..segment.Start]));

                var displayText = segment.DisplayText;
                if (string.IsNullOrEmpty(displayText))
                    displayText = segment.Url;

                try
                {
                    var capturedUrl = segment.Url;
                    var button = new HyperlinkButton
                    {
                        Content = new TextBlock
                        {
                            Text = displayText,
                            TextDecorations = TextDecorations.Underline,
                            Foreground = AppBrush("PinkBrush", new SolidColorBrush(Color.FromRgb(255, 105, 180)))
                        },
                        NavigateUri = new Uri(segment.Url),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0),
                        Cursor = new Cursor(StandardCursorType.Hand)
                    };
                    button.Click += (_, _) => OpenUrl(capturedUrl);
                    target.Add(new InlineUIContainer { Child = button });
                }
                catch
                {
                    target.Add(new Run(displayText));
                }

                lastIndex = segment.End;
            }

            if (lastIndex < text.Length)
                target.Add(new Run(text[lastIndex..]));
        }

        private static List<LinkSegment> FindLinkSegments(string text)
        {
            var segments = new List<LinkSegment>();

            // Markdown links: [text](url)
            foreach (Match m in Regex.Matches(text, @"\[([^\]]+)\]\(([^)]+)\)"))
            {
                var url = m.Groups[2].Value.Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    segments.Add(new LinkSegment(m.Index, m.Length, url, m.Groups[1].Value));
                }
            }

            // Raw URLs
            foreach (Match m in Regex.Matches(text, @"https?://[^\s,""'<>]+", RegexOptions.IgnoreCase))
            {
                if (segments.Any(s => m.Index >= s.Start && m.Index < s.End))
                    continue;

                segments.Add(new LinkSegment(m.Index, m.Length, m.Value, m.Value));
            }

            return segments.OrderBy(s => s.Start).ToList();
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open URL: {ex.Message}");
            }
        }

        private sealed record LinkSegment(int Start, int Length, string Url, string DisplayText)
        {
            public int End => Start + Length;
        }

        private void ShowModerationRefusalBubble(ModerationSource source)
        {
            var locKey = source == ModerationSource.Input ? "moderation_input_refusal" : "moderation_output_refusal";
            var text = Loc.Get(locKey);
            if (string.IsNullOrEmpty(text))
                text = source == ModerationSource.Input
                    ? "This message can't be sent under our content policy."
                    : "AI declined to respond.";
            AddToChatHistory(text, false);
            ShowGiggle(text, playSound: false, source: SpeechSource.AI, aiGenerated: false);
            if (AiBadge != null) AiBadge.IsVisible = false;
            if (PolicyBadge != null) PolicyBadge.IsVisible = true;
        }

        private void ShowMutedIndicator()
        {
            if (SpeechBubble?.IsVisible == true) return;
            if (TxtSpeech != null && TxtSpeech.Inlines != null)
            {
                TxtSpeech.Inlines.Clear();
                TxtSpeech.Inlines.Add(new Run("MUTED \U0001F509") { Foreground = AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(180, 180, 200))) });
                TxtSpeech.FontSize = 20;
            }
            if (SpeechBubble != null) SpeechBubble.IsVisible = true;
            _mutedIndicatorTimer?.Stop();
            _mutedIndicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _mutedIndicatorTimer.Tick += (_, _) =>
            {
                _mutedIndicatorTimer.Stop();
                if (SpeechBubble != null) SpeechBubble.IsVisible = false;
            };
            _mutedIndicatorTimer.Start();
        }

        // ===== Idle / trigger / random bubble timers =====
        private void StartIdleTimer()
        {
            _idleTimer?.Stop();
            var interval = _settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();
        }

        public void RestartIdleTimer()
        {
            StartIdleTimer();
        }

        private void ResetIdleTimer()
        {
            StartIdleTimer();
        }

        private void OnIdleTick(object? sender, EventArgs e)
        {
            var configured = _settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
            if (_idleTimer != null && Math.Abs(_idleTimer.Interval.TotalSeconds - configured) > 0.5)
                _idleTimer.Interval = TimeSpan.FromSeconds(configured);

            if (!IsSpeechReady()) return;
            Giggle(GetRandomBambiPhrase());
        }

        private void StartTriggerTimer()
        {
            if (_settings?.Current?.TriggerModeEnabled != true) return;

            _triggerTimer?.Stop();
            var interval = _settings?.Current?.TriggerIntervalSeconds ?? 60;
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _triggerTimer.Tick += OnTriggerTick;
            _triggerTimer.Start();

            // First trigger shortly after window init.
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(2000);
                OnTriggerTick(null, EventArgs.Empty);
            });
        }

        public void RestartTriggerTimer()
        {
            StartTriggerTimer();
        }

        private void OnTriggerTick(object? sender, EventArgs e)
        {
            if (_settings?.Current?.TriggerModeEnabled != true) return;
            if (!IsSpeechReady()) return;
            GiggleFromCategory("Trigger");
        }

        private void StartRandomBubbleTimer()
        {
            if (_settings?.Current?.RandomBubbleEnabled != true) return;

            _randomBubbleTimer?.Stop();
            var interval = _random.Next(180, 301);
            _randomBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _randomBubbleTimer.Tick += OnRandomBubbleTick;
            _randomBubbleTimer.Start();
        }

        public void RestartRandomBubbleTimer()
        {
            StartRandomBubbleTimer();
        }

        private void OnRandomBubbleTick(object? sender, EventArgs e)
        {
            if (_randomBubbleTimer != null)
                _randomBubbleTimer.Interval = TimeSpan.FromSeconds(_random.Next(180, 301));

            if (!IsOurAppForeground()) return;
            SpawnRandomBubble();
        }

        private void SpawnRandomBubble()
        {
            GiggleFromCategory("RandomBubble");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var pixelPos = AvatarBorder.PointToScreen(new global::Avalonia.Point(AvatarBorder.Bounds.Width / 2, AvatarBorder.Bounds.Height / 2));
                        var pos = new global::Avalonia.Point(pixelPos.X, pixelPos.Y);
                        var bubble = new AvatarRandomBubble(pos, _random, OnRandomBubblePopped);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning("RandomBubble: failed to spawn - {Error}", ex.Message);
                    }
                });
            });
        }

        private void OnRandomBubblePopped()
        {
            PlayAvatarPopSound();
            _progression?.AddXP(5, XPSource.AvatarInteraction);
            _achievementService?.TrackBubblePopped();
            Giggle("Good girl! *giggles*");
        }
        private void StopZOrderRefreshTimer()
        {
            _zOrderRefreshTimer?.Stop();
        }
        private void StartZOrderRefreshTimer()
        {
            _zOrderRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _zOrderRefreshTimer.Tick += (_, _) => BringAttachedPairToFront();
            _zOrderRefreshTimer.Start();
        }
        private void StartThinkingAnimation()
        {
            _isWaitingForAi = true;
            _thinkingTimer?.Stop();
            _thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _thinkingTimer.Tick += (_, _) =>
            {
                if (!_isWaitingForAi) { _thinkingTimer?.Stop(); return; }
                PopulateSpeechBubble("Thinking" + new string('.', (_thinkingTickCount++ % 3) + 1));
                if (SpeechBubble != null) SpeechBubble.IsVisible = true;
            };
            _thinkingTimer.Start();
        }
        private void StopThinkingAnimation()
        {
            _isWaitingForAi = false;
            _thinkingTimer?.Stop();
        }

        // ===== Audio =====
        private string? BubbleSoundPath() => _resourceResolver?.ResolveAudioPath("bubbles/Pop.mp3");
        private string? GiggleSoundPath()
        {
            var n = _random.Next(1, 9);
            return _resourceResolver?.ResolveAudioPath($"giggle{n}.mp3");
        }
        private string? PopSoundPath() => _resourceResolver?.ResolveAudioPath("bubbles/Pop.mp3");

        private void PlayFallbackBubbleSound() => PlayAudio(BubbleSoundPath(), 0.5);
        private void PlayGiggleSound() => PlayAudio(GiggleSoundPath(), 0.6);
        // Spoken voice lines drive IsSpeakingAudio (lot-8 C1) so the awareness busy-retry waits for them.
        private void PlayBarkVoice(string path) { _isSpeakingAudio = true; PlayAudio(path, 0.9); }
        private void PlayPhraseAudio(string path) { _isSpeakingAudio = true; PlayAudio(path, 0.8); }
        private void PlayAvatarPopSound() => PlayAudio(PopSoundPath(), 0.5);

        private void PlayAudio(string? path, double volume)
        {
            if (_audioPlayer == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                var master = (_settings?.Current?.MasterVolume ?? 100) / 100.0;
                _audioPlayer.SetVolume(volume * master);
                _ = _audioPlayer.PlayAsync(path);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Audio playback failed for {Path}", path);
            }
        }

        private void StopSpokenAudio()
        {
            _isSpeakingAudio = false;
            try { _audioPlayer?.Stop(); }
            catch { }
        }

        private void StopVoiceLineAudio() => StopSpokenAudio();

        // ===== Helpers =====
        private bool IsSpeechReady()
        {
            if (_isGiggling) return false;
            double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
            return timeSinceLastSpeech >= CalculateRequiredDelayAfterLastSpeech();
        }

        private void PlayClickBounce()
        {
            if (AvatarBounceHost == null) return;
            AnimateBounce(AvatarBounceHost, 12, 120);
        }

        private void PlayDoubleBounce()
        {
            if (AvatarBounceHost == null) return;
            AnimateBounce(AvatarBounceHost, 18, 160, bounces: 2);
        }

        private void AnimateBounce(Control target, double amplitude, int durationMs, int bounces = 1)
        {
            var original = target.RenderTransform;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            long start = Environment.TickCount64;
            timer.Tick += (_, _) =>
            {
                long elapsed = Environment.TickCount64 - start;
                if (elapsed >= durationMs)
                {
                    timer.Stop();
                    target.RenderTransform = original;
                    return;
                }
                double t = elapsed / (double)durationMs;
                double decay = 1 - t;
                double y = -amplitude * Math.Sin(t * Math.PI * bounces) * decay;
                target.RenderTransform = new TranslateTransform(0, y);
            };
            timer.Start();
        }

        private void TriggerBambiCumAndCollapse()
        {
            _logger?.LogInformation("Bambi Cum and Collapse triggered! (50 clicks in 1 minute)");

            try
            {
                var phraseService = App.Services?.GetService<ICompanionPhraseService>();
                var folder = phraseService?.VoiceLineFolder;
                if (!string.IsNullOrEmpty(folder))
                {
                    var collapseFiles = new[] { "come and coll.mp3", "come and coll (1).mp3", "come and coll (2).mp3" };
                    var chosenFile = collapseFiles[_random.Next(collapseFiles.Length)];
                    var audioPath = System.IO.Path.Combine(folder, chosenFile);
                    if (File.Exists(audioPath))
                    {
                        PlayAudio(audioPath, 1.0);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Failed to play Bambi Cum and Collapse audio: {Error}", ex.Message);
            }

            GigglePriority("BAMBI CUM AND COLLAPSE", aiGenerated: false);
        }

        private double CalculateRequiredDelayAfterLastSpeech()
        {
            double delay = MinSpeechDelaySeconds;
            if (_lastSpeechSource == SpeechSource.AI) delay += AiSpeechBonusSeconds;
            if (_lastSpeechLength > LongTextThreshold)
                delay += (_lastSpeechLength - LongTextThreshold) * PerCharDelaySeconds;
            return delay;
        }

        private void WireModerationCounter()
        {
            try
            {
                var counter = App.Services.GetService<IModerationCounter>();
                if (counter == null) return;
                counter.WarningTriggered += OnWarningTriggered;
                counter.CooldownStarted += OnCooldownStarted;
                counter.CooldownEnded += OnCooldownEnded;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to wire moderation counter");
            }
        }

        private void OnWarningTriggered(ModerationCounterState state)
        {
            _logger?.LogWarning("Moderation warning triggered (hits={Hits})", state.HitsInLastTenMinutes);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var dialog = new ContentPolicyWarningDialog(state.HitsInLastTenMinutes);
                    _ = await dialog.ShowDialog<bool?>(this);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to show content-policy warning dialog");
                }
            });
        }
        private void OnCooldownStarted(DateTime endsAt)
        {
            _cooldownEndsAt = endsAt;
            if (TxtUserInput != null) { TxtUserInput.IsEnabled = false; TxtUserInput.Opacity = 0.5; TxtUserInput.Text = ""; }
            if (BtnSendChat != null) { BtnSendChat.IsEnabled = false; BtnSendChat.Opacity = 0.5; }
            _cooldownTickTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cooldownTickTimer.Tick += CooldownTick;
            _cooldownTickTimer.Start();
            CooldownTick(null, EventArgs.Empty);
        }
        private void CooldownTick(object? sender, EventArgs e)
        {
            if (!_cooldownEndsAt.HasValue) { _cooldownTickTimer?.Stop(); return; }
            var remaining = _cooldownEndsAt.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                OnCooldownEnded();
                return;
            }
            if (TxtUserInput != null)
                TxtUserInput.Text = string.Format(Loc.Get("chat_cooldown_active"), (int)Math.Ceiling(remaining.TotalSeconds));
        }
        private void OnCooldownEnded()
        {
            _cooldownEndsAt = null;
            _cooldownTickTimer?.Stop();
            if (TxtUserInput != null) { TxtUserInput.IsEnabled = true; TxtUserInput.Opacity = 1.0; TxtUserInput.Text = ""; }
            if (BtnSendChat != null) { BtnSendChat.IsEnabled = true; BtnSendChat.Opacity = 1.0; }
        }

        // ===== Window lifecycle =====
        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            Topmost = false;
            CalculateScaleFactor();
            Dispatcher.UIThread.Post(() =>
            {
                if (_parentWindow?.IsVisible == true && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    StartFloatingAnimation();
                    BringAttachedPairToFront(true);
                    ShowGreeting();
                }
                if (SpeechBubble != null) SpeechBubble.Margin = new Thickness(0, 0, 125, 550);

                // Re-apply mod tube layout offsets so the bubble/input/title align
                // with the active mod's chamber position.
                ApplyTubeLayoutOffsets();
            }, DispatcherPriority.Normal);
            StartFullscreenDetection();
        }

        private void OnFirstContentRendered(object? sender, EventArgs e)
        {
            Opened -= OnFirstContentRendered;

            _tubeHandle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            _parentHandle = _parentWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

            if (ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                Width = ClientSize.Width;
                Height = ClientSize.Height;
            }
            SizeToContent = SizeToContent.Manual;
            InvalidateVisual();
        }

        protected override void OnClosed(EventArgs e)
        {
            _poseTimer?.Stop();
            _fullscreenCheckTimer?.Stop();
            StopFloatingAnimation();
            _animatedAvatarGif?.Dispose();
            _animatedAvatarGif = null;
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _idleTimer?.Stop();
            _triggerTimer?.Stop();
            _randomBubbleTimer?.Stop();
            _zOrderRefreshTimer?.Stop();
            StopVoiceLineAudio();
            UnwireCompanionReactionEvents();
            if (_parentWindow != null)
            {
                _parentWindow.PositionChanged -= ParentWindow_PositionChanged;
                _parentWindow.PropertyChanged -= ParentWindow_PropertyChanged;
                _parentWindow.SizeChanged -= ParentWindow_SizeChanged;
                _parentWindow.Activated -= ParentWindow_Activated;
                _parentWindow.Closed -= ParentWindow_Closed;
            }
            if (_achievementService != null)
            {
                _achievementService.AchievementUnlocked -= OnAchievementUnlocked;
            }
            if (_barkService is global::ConditioningControlPanel.Avalonia.Services.AvaloniaBarkService abs)
            {
                try { abs.AvatarClicked -= OnBarkAvatarClicked; }
                catch { }
                try { abs.BarkRequested -= OnBarkRequested; }
                catch { }
            }
            if (_modService != null)
            {
                _modService.ActiveModChanged -= OnModChanged;
            }
            if (_sessionService != null)
            {
                _sessionService.SessionStopped -= OnSessionStopped;
            }
            base.OnClosed(e);
        }

        // ===== Parent window event handlers =====
        private void ParentWindow_PositionChanged(object? sender, EventArgs e)
        {
            if (_parentWindow?.WindowState == WindowState.Minimized) return;
            // Follow the parent synchronously for lag-free glue. Do NOT reassert z-order here: WPF's
            // LocationChanged handler doesn't, and calling BringAttachedPairToFront() on every move
            // event issues a SetWindowPos per mouse-move during a drag, which visibly stutters the
            // follow. Z-order is kept fresh by _zOrderRefreshTimer and the attach/activate/reappear
            // paths instead.
            UpdatePosition();
        }

        private void ParentWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // WPF parity: parent SizeChanged -> reposition. Synchronous like PositionChanged
            // so the pair stays glued during a live resize; no z-order work here either.
            if (_parentWindow?.WindowState == WindowState.Minimized) return;
            UpdatePosition();
        }

        private void ParentWindow_Activated(object? sender, EventArgs e)
        {
            // WPF parity: Activated fires right after minimize/restore and exclusive-
            // fullscreen exits, once the -32000 restore parking position has settled -
            // the WPF head uses it as the second-chance anchor + raise pass.
            if (!_isAttached || _hiddenForFullscreen) return;
            if (_settings?.Current?.AvatarEnabled != true) return;
            if (_parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized) return;
            if (!IsVisible) Show();
            UpdatePosition();
            Dispatcher.UIThread.Post(() =>
            {
                if (_isAttached && _parentWindow?.IsVisible == true
                    && _parentWindow.WindowState != WindowState.Minimized)
                {
                    UpdatePosition();
                    BringAttachedPairToFront();
                }
            }, DispatcherPriority.Background);
        }

        private void ParentWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty)
                ParentWindow_StateChanged(sender, e);
            else if (e.Property == Visual.IsVisibleProperty)
                ParentWindow_IsVisibleChanged(sender, e);
        }

        private void ParentWindow_StateChanged(object? sender, EventArgs e)
        {
            if (_parentWindow == null) return;
            switch (_parentWindow.WindowState)
            {
                case WindowState.Minimized:
                    PauseAvatarGif();
                    if (_isAttached) Hide();
                    break;
                case WindowState.Normal:
                case WindowState.Maximized:
                    ResumeAvatarGif();
                    if (_parentWindow.IsVisible && _settings?.Current?.AvatarEnabled == true)
                    {
                        Show();
                        if (_isAttached)
                        {
                            UpdatePosition();
                            BringAttachedPairToFront();
                            // Second pass: at the instant WindowState flips back the platform
                            // can still report the minimized -32000 parking position, which the
                            // out-of-range guard rejects. Re-anchor once the restore settled
                            // (WPF gets this pass for free from its Activated wiring).
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (_isAttached && _parentWindow?.IsVisible == true
                                    && _parentWindow.WindowState != WindowState.Minimized)
                                {
                                    UpdatePosition();
                                    BringAttachedPairToFront();
                                }
                            }, DispatcherPriority.Background);
                        }
                    }
                    break;
            }
        }

        private void ParentWindow_IsVisibleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_parentWindow == null) return;
            bool visible = (bool)e.NewValue!;
            if (visible && _parentWindow.WindowState != WindowState.Minimized && _settings?.Current?.AvatarEnabled == true)
            {
                ResumeAvatarGif();
                Show();
                if (_isAttached) { UpdatePosition(); BringAttachedPairToFront(); }
            }
            else
            {
                PauseAvatarGif();
                if (_isAttached) Hide();
            }
        }

        private void ParentWindow_Closed(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                try { Close(); } catch { /* already closing */ }
            }
            else
            {
                Giggle("Main window closed! Right-click to dismiss~");
            }
        }

        // ===== Public show/hide =====
        public void ShowTube()
        {
            _hiddenForFullscreen = false;
            Show();
            if (_parentWindow?.IsVisible == true && _parentWindow.WindowState != WindowState.Minimized)
            {
                UpdatePosition();
                if (_isAttached) BringAttachedPairToFront();
                // Second anchor pass after the first layout/SizeToContent freeze settles:
                // on the very first Show the synchronous pass above can run against
                // pre-layout geometry (WPF defers the same way at Loaded priority).
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isAttached && _parentWindow?.IsVisible == true
                        && _parentWindow.WindowState != WindowState.Minimized)
                    {
                        UpdatePosition();
                    }
                }, DispatcherPriority.Background);
            }
            StartFloatingAnimation();
        }

        public void HideTube()
        {
            Hide();
        }

        public void SetMuted(bool muted)
        {
            _isMuted = muted;
        }

        private static IBrush AppBrush(string key, IBrush fallback)
        {
            if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is IBrush b)
                return b;
            return fallback;
        }

        // ===== Commands =====
        private class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
public RelayCommand(Action<object?> execute) => _execute = execute;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute(parameter);
        }
    }
}

#pragma warning restore CS0169
#pragma warning restore CS0414
#pragma warning restore CS0649
