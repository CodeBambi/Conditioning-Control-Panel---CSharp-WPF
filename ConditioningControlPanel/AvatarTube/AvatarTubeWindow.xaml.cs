using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using XamlAnimatedGif;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    public partial class AvatarTubeWindow : Window
    {
        private readonly Window _parentWindow;
        private bool _isAttached = true;
        private readonly Random _random = new();

        // --- Own-thread support (AppSettings.AvatarOwnThread) ------------------------------------
        // When the flag is on this window lives on its own STA thread, so its Dispatcher != the main
        // dispatcher. Marshal all avatar-UI work to THIS window's own dispatcher. When the flag is off
        // (Dispatcher == Application.Current.Dispatcher) this is a no-op drop-in for DispatcherHelper.RunOnUI.
        internal void RunOnAvatar(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            var d = Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            if (d.CheckAccess()) action();
            else d.BeginInvoke(action, priority);
        }

        // WPF Window.Show()/Close() are thread-affine and can't carry a self-marshal guard, so external
        // (main-thread) callers go through these. No-op marshal when the avatar shares the main thread.
        public void ShowSafe()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ShowSafe)); return; }
            try { Show(); } catch (Exception ex) { App.Logger?.Debug("AvatarTube ShowSafe failed: {Error}", ex.Message); }
        }
        public void CloseSafe()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(CloseSafe)); return; }
            try { Close(); } catch (Exception ex) { App.Logger?.Debug("AvatarTube CloseSafe failed: {Error}", ex.Message); }
        }

        // Immutable snapshot of the parent (main) window's geometry, written on the MAIN thread by the
        // parent-position handler and read on the AVATAR thread by UpdatePosition. An atomic reference
        // swap (volatile) — never read _parentWindow's dependency properties off its own thread.
        private sealed record ParentGeom(double Left, double Top, double Width, double Height, bool Minimized, bool Visible);
        private volatile ParentGeom? _parentGeom;
        /// <summary>Refresh the cached parent geometry. Reads the parent's dependency properties on the
        /// parent's OWN thread (sync if already there, else a non-blocking BeginInvoke), so it is safe to
        /// call from the avatar thread.</summary>
        internal void CaptureParentGeom()
        {
            var pd = _parentWindow?.Dispatcher;
            if (pd == null || pd.HasShutdownStarted) return;
            if (!pd.CheckAccess()) { pd.BeginInvoke(new Action(CaptureParentGeom)); return; }
            try
            {
                _parentGeom = new ParentGeom(_parentWindow.Left, _parentWindow.Top,
                    _parentWindow.ActualWidth, _parentWindow.ActualHeight,
                    _parentWindow.WindowState == WindowState.Minimized,
                    _parentWindow.IsVisible);
                // Seed the parent HWND here (on the parent's own thread) so BringAttachedPairToFront
                // never has to touch WindowInteropHelper from the avatar thread.
                if (_parentHandle == IntPtr.Zero)
                {
                    var h = new System.Windows.Interop.WindowInteropHelper(_parentWindow).Handle;
                    if (h != IntPtr.Zero) _parentHandle = h;
                }
            }
            catch { /* window may be closing */ }
        }

        /// <summary>Cache-based "parent is visible and not minimised" — safe to read on the avatar thread.
        /// Seeds the cache on first use. Defaults to false until the first capture lands.</summary>
        private bool ParentVisibleNotMinimized
        {
            get
            {
                var g = _parentGeom;
                if (g == null)
                {
                    // Seed it. When the avatar shares the parent thread (flag off) this runs synchronously,
                    // so re-read and return the real value — no one-call glitch vs. the old direct read.
                    // On the avatar thread (flag on) the capture is async, so default to false this once.
                    CaptureParentGeom();
                    g = _parentGeom;
                    if (g == null) return false;
                }
                return g.Visible && !g.Minimized;
            }
        }

        public AvatarTubeWindow(Window parentWindow)
        {
            InitializeComponent();

            // Apply the user-configured chat shortcut keybinding (Ctrl+T by default).
            Loaded += (_, _) => ApplyChatShortcutTo(this);

            // Bind chat history list to the rolling collection of conversational messages.
            ChatHistoryList.ItemsSource = ChatHistory;

            // Esc closes chat history mode if open.
            PreviewKeyDown += AvatarTubeWindow_PreviewKeyDown;

            _parentWindow = parentWindow;
            // Don't set Owner - it causes black window artifacts during minimize
            // We manage visibility manually via event handlers instead

            // Determine which avatar set to load based on player level
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            _maxUnlockedSet = GetAvatarSetForLevel(playerLevel);

            // Load user's saved avatar selection, or use max unlocked
            _selectedAvatarSet = App.Settings?.Current?.SelectedAvatarSet ?? _maxUnlockedSet;
            // Clamp to valid range (1 to max unlocked)
            _selectedAvatarSet = Math.Clamp(_selectedAvatarSet, 1, _maxUnlockedSet);
            _currentAvatarSet = _selectedAvatarSet;

            // Fall back if the saved set isn't supported by the active mod (e.g. a level was retired,
            // like Circe's set 5) — otherwise a stale selection would load an unsupported avatar.
            var supportedSetsInit = GetUnlockedAvatarSets(playerLevel);
            if (supportedSetsInit.Length > 0 && !supportedSetsInit.Contains(_currentAvatarSet))
            {
                _selectedAvatarSet = supportedSetsInit[supportedSetsInit.Length - 1];
                _currentAvatarSet = _selectedAvatarSet;
            }

            // Mods with a single animated emote avatar (BambiSleep, Sissy): lock to that set, ignoring
            // the saved/level-based selection — there's no picker, just the one animated avatar.
            if (IsSingleEmoteAvatarMod(out int emoteOnlySetInit))
                _currentAvatarSet = _selectedAvatarSet = emoteOnlySetInit;

            // Check if this avatar set has an animated version available
            _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);

            // Load avatar poses for the appropriate set
            _avatarPoses = LoadAvatarPoses(_currentAvatarSet);

            // Set initial avatar (animated or static)
            if (_useAnimatedAvatar)
            {
                LoadAnimatedAvatar(_currentAvatarSet);
            }
            else if (_avatarPoses.Length > 0)
            {
                ImgAvatar.Source = _avatarPoses[0];
            }

            // Apply size/position adjustments for non-basic avatars
            ApplyAvatarTransform(_currentAvatarSet);

            // Initialize title box display
            UpdateTitleDisplay(playerLevel);
            UpdateNavigationArrows();

            // Setup pose switching timer (only for static avatars)
            _poseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _poseTimer.Tick += PoseTimer_Tick;
            if (!_useAnimatedAvatar && _avatarPoses.Length > 1)
                _poseTimer.Start();

            // Emotive portrait avatar: if the active mod ships avatar_manifest.json, take over the
            // avatar with the 79-pose emotive system. No-op (keeps the legacy 4-pose path) otherwise.
            TryEnterPortraitMode();

            // Circe's Lock pose-1: take over with animated WebP emotes (overrides the above).
            TryUpdateCirceEmoteMode();

            // Subscribe to parent window events
            _parentWindow.LocationChanged += ParentWindow_PositionChanged;
            _parentWindow.SizeChanged += ParentWindow_PositionChanged;
            _parentWindow.StateChanged += ParentWindow_StateChanged;
            _parentWindow.IsVisibleChanged += ParentWindow_IsVisibleChanged;
            _parentWindow.Activated += ParentWindow_Activated;
            _parentWindow.PreviewMouseDown += ParentWindow_PreviewMouseDown;
            _parentWindow.Closed += ParentWindow_Closed;
            
            // Get handles when loaded
            Loaded += OnLoaded;

            // AllowsTransparency=True + SizeToContent=WidthAndHeight + Viewbox
            // creates a layered window whose surface is sized at Show() before
            // the content has been measured. Without a forced refresh after
            // first composition, the surface stays blank until WM_NCCALCSIZE
            // fires from a user window-move — the bug the user reported as
            // "tube doesn't render until I move main". ContentRendered fires
            // ONCE after the first paint, so it's the right place to flush
            // the layered surface via a SizeToContent toggle.
            ContentRendered += OnFirstContentRendered;

            // Refresh tube image from mod on startup (XAML hardcodes pack:// URI)
            SetTubeStyle(!_isAttached);

            // Apply tube layout offsets for current mod
            ApplyTubeLayoutOffsets();

            // Load the active mod's video links on startup (otherwise the known-video table
            // keeps its hardcoded defaults until the user switches mods, so a themed mod's
            // links wouldn't be clickable on a plain boot).
            ReloadVideoLinks();

            // Subscribe to mod changes to refresh tube, avatars, and titles
            if (App.Mods != null)
            {
                App.Mods.ModChanged += (s, mod) =>
                {
                    if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnModChanged())); return; }
                    OnModChanged();
                };
            }

            // Initialize context menu state
            UpdateQuickMenuState();

            // Subscribe to mouse wheel and keyboard for resizing when detached
            PreviewMouseWheel += Window_PreviewMouseWheel;
            PreviewKeyDown += Window_PreviewKeyDown;

            // Keep tube in front during position changes when attached
            LocationChanged += (s, e) => { if (_isAttached) BringAttachedPairToFront(); };

            // When tube gets activated (e.g. after topmost video closes), redirect to parent
            Activated += TubeWindow_Activated;

            // Wire up video service events for companion speech (1.3s before video)
            if (App.Video != null)
            {
                App.Video.VideoAboutToStart += OnVideoAboutToStart;
                App.Video.VideoEnded += OnVideoEnded;
            }

            // Lock-card reaction is now owned by BarkService (Fork D 50/50 coin flip): it is the
            // sole subscriber to LockCardCompleted and invokes PlayLockCardAiReactionAsync on heads.
            // This window no longer self-subscribes.

            // Wire up game completion events
            if (App.BubbleCount != null)
            {
                App.BubbleCount.GameCompleted += OnGameCompleted;
                App.BubbleCount.GameFailed += OnGameFailed;
            }

            // Wire up flash service events for pre-announcement
            if (App.Flash != null)
            {
                App.Flash.FlashAboutToDisplay += OnFlashAboutToDisplay;
                App.Flash.FlashClicked += OnFlashClicked;
                App.Flash.FlashAudioPlaying += OnFlashAudioPlaying;
            }

            // Wire up subliminal service events for acknowledgment
            if (App.Subliminal != null)
            {
                App.Subliminal.SubliminalDisplayed += OnSubliminalDisplayed;
            }

            // Wire up bubble service events for occasional pop acknowledgment
            if (App.Bubbles != null)
            {
                App.Bubbles.OnBubblePopped += OnBubblePopped;
                App.Bubbles.OnBubbleMissed += OnBubbleMissed;
            }

            // Wire up achievement events
            if (App.Achievements != null)
            {
                App.Achievements.AchievementUnlocked += OnAchievementUnlocked;
            }

            // Wire up progression events
            if (App.Progression != null)
            {
                App.Progression.LevelUp += OnLevelUp;
            }

            // Wire up companion events (v5.3 companion leveling)
            if (App.Companion != null)
            {
                App.Companion.CompanionLevelUp += OnCompanionLevelUp;
                App.Companion.CompanionSwitched += OnCompanionSwitched;
            }

            // Wire up window awareness events (opt-in feature)
            if (App.WindowAwareness != null)
            {
                App.WindowAwareness.ActivityChanged += OnActivityChanged;
                App.WindowAwareness.StillOnActivity += OnStillOnActivity;
                // Start awareness if enabled
                App.WindowAwareness.Start();
            }

            // Wire up MindWipe events (occasional reactions)
            if (App.MindWipe != null)
            {
                App.MindWipe.MindWipeTriggered += OnMindWipeTriggered;
            }

            // Wire up BrainDrain events (occasional reactions)
            if (App.BrainDrain != null)
            {
                App.BrainDrain.BrainDrainTriggered += OnBrainDrainTriggered;
            }

            // Wire up engine stop event from MainWindow
            if (_parentWindow is MainWindow mainWindow)
            {
                mainWindow.EngineStopped += OnEngineStopped;
            }

            // Show greeting after a short delay (2 seconds after window loads)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                greetingTimer.Tick += (s, e) =>
                {
                    greetingTimer.Stop();
                    ShowGreeting();
                };
                greetingTimer.Start();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Start idle timer for random giggles
            StartIdleTimer();

            // Start trigger timer if enabled
            StartTriggerTimer();

            // Start random bubble timer if enabled
            StartRandomBubbleTimer();

            // Handle clicks outside the input panel to close it
            PreviewMouseDown += Window_PreviewMouseDown;

            // P1.4 — wire moderation counter for warning modal + chat cooldown.
            WireModerationCounter();

            App.Logger?.Information("AvatarTubeWindow initialized with avatar set {Set} for level {Level}",
                _currentAvatarSet, playerLevel);
        }

        private void WireModerationCounter()
        {
            var counter = App.ModerationCounter;
            if (counter == null) return;

            counter.WarningTriggered += state =>
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnWarningTriggered(state))); return; }
                OnWarningTriggered(state);
            };
            counter.CooldownStarted += endsAt =>
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnCooldownStarted(endsAt))); return; }
                OnCooldownStarted(endsAt);
            };
            counter.CooldownEnded += () =>
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnCooldownEnded())); return; }
                OnCooldownEnded();
            };
        }

        private void OnWarningTriggered(Services.Moderation.ModerationCounterState state)
        {
            try
            {
                var dlg = new ContentPolicyWarningDialog(state.HitsInLastTenMinutes)
                {
                    Owner = _parentWindow
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarTubeWindow: failed to show ContentPolicyWarningDialog");
            }
        }

        private void OnCooldownStarted(DateTime endsAt)
        {
            _cooldownEndsAt = endsAt;
            try
            {
                _normalChatPlaceholder ??= TxtUserInput?.Tag as string ?? string.Empty;
                if (TxtUserInput != null)
                {
                    TxtUserInput.IsEnabled = false;
                    TxtUserInput.Opacity = 0.5;
                    TxtUserInput.Text = string.Empty;
                }
                if (BtnSendChat != null)
                {
                    BtnSendChat.IsEnabled = false;
                    BtnSendChat.Opacity = 0.5;
                }

                _cooldownTickTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _cooldownTickTimer.Tick -= CooldownTick;
                _cooldownTickTimer.Tick += CooldownTick;
                _cooldownTickTimer.Start();
                CooldownTick(null, EventArgs.Empty); // initial paint
                App.Logger?.Information("AvatarTubeWindow: chat cooldown engaged until {End}", endsAt);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarTubeWindow: OnCooldownStarted failed");
            }
        }

        private void CooldownTick(object? sender, EventArgs e)
        {
            if (!_cooldownEndsAt.HasValue) { _cooldownTickTimer?.Stop(); return; }
            var remaining = _cooldownEndsAt.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                // Probe state to trigger CooldownEnded event in the counter.
                _ = App.ModerationCounter?.GetState();
                return;
            }
            try
            {
                if (TxtUserInput != null)
                {
                    TxtUserInput.Text = string.Format(
                        Localization.Loc.Get("chat_cooldown_active"),
                        (int)Math.Ceiling(remaining.TotalSeconds));
                }
            }
            catch { /* best-effort painter */ }
        }

        private void OnCooldownEnded()
        {
            _cooldownEndsAt = null;
            _cooldownTickTimer?.Stop();
            try
            {
                if (TxtUserInput != null)
                {
                    TxtUserInput.IsEnabled = true;
                    TxtUserInput.Opacity = 1.0;
                    TxtUserInput.Text = string.Empty;
                }
                if (BtnSendChat != null)
                {
                    BtnSendChat.IsEnabled = true;
                    BtnSendChat.Opacity = 1.0;
                }
                App.Logger?.Information("AvatarTubeWindow: chat cooldown ended");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarTubeWindow: OnCooldownEnded failed");
            }
        }

        private void OnFirstContentRendered(object? sender, EventArgs e)
        {
            // One-shot: clear the subscription so this only runs after the
            // very first composition.
            ContentRendered -= OnFirstContentRendered;
            try
            {
                // The window has now auto-sized (SizeToContent=WidthAndHeight) to the correct
                // dimensions against the composed Viewbox. Capture that size and turn auto-sizing
                // OFF permanently.
                //
                // WHY: this is a layered window (AllowsTransparency=True). While SizeToContent is
                // active, WPF hooks HwndSource.OnLayoutUpdated → Resize, and for a layered window that
                // resize runs a SYNCHRONOUS HwndTarget.OnResize → MediaContext.CompleteRender() — a
                // blocking present that waits on the render thread. When the render thread is busy
                // (e.g. the avatar emote GIF crossfade: two ~1.8 MB layers at 15 fps with blurred drop
                // shadows on a CPU-composited surface), that present deadlocks and the whole app hangs
                // ("not responding"), then Windows force-closes it. Diagnosed 2026-06-10 from live hang
                // dumps: every emote-mode mod/set switch could freeze the UI in CompleteRender.
                //
                // The window's size is driven explicitly by ContentViewbox.Width/Height (DPI × user
                // scale) — see CalculateScaleFactor()/ApplyScale() — so auto-sizing is unnecessary.
                // The toggle below also doubles as the original first-paint re-measure that flushes the
                // layered surface (it was previously toggled Manual→back; now it stays Manual).
                if (ActualWidth > 0 && ActualHeight > 0)
                {
                    Width = ActualWidth;
                    Height = ActualHeight;
                }
                SizeToContent = SizeToContent.Manual;

                // Belt-and-suspenders: also flush the Win32 frame so the
                // layered window picks up any cached style/size deltas from
                // the SetWindowLong(WS_EX_TOOLWINDOW) call in OnLoaded.
                if (_tubeHandle != IntPtr.Zero)
                {
                    SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                }

                InvalidateVisual();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AvatarTube first-paint kick failed: {Error}", ex.Message);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _tubeHandle = new WindowInteropHelper(this).Handle;
            // NOTE: _parentHandle is read on the PARENT thread inside HookParent() below — reading the
            // parent window's handle here would VerifyAccess-throw when this Loaded runs on the avatar thread.

            // Once auto-sizing is turned off after first paint (OnFirstContentRendered), the window
            // no longer follows its content, so keep its size pinned to the explicitly-sized Viewbox.
            // This is what lets us run SizeToContent=Manual and avoid the layered-window
            // OnResize→CompleteRender hang (see OnFirstContentRendered for the full rationale).
            ContentViewbox.SizeChanged += (_, __) =>
            {
                if (SizeToContent != SizeToContent.Manual) return; // startup auto-size phase: let WPF do it
                if (double.IsNaN(ContentViewbox.Width) || ContentViewbox.Width <= 0) return;
                if (Width != ContentViewbox.Width) Width = ContentViewbox.Width;
                if (Height != ContentViewbox.Height) Height = ContentViewbox.Height;
            };

            // Hook window messages (minimal hook, no z-order forcing)
            _hwndSource = HwndSource.FromHwnd(_tubeHandle);
            _hwndSource?.AddHook(WndProc);

            // Start the Takeover countdown bar (only ticks while Takeover is enabled)
            InitTakeoverCountdownBar();

            // Hook the parent window's messages too. The keep-on-top timer polls at
            // Background priority and gets starved exactly when AI speech is busy
            // (GIF animation, text streaming, effects firing) — so the bubble can sit
            // behind main for noticeably longer than the 300ms tick. Reacting to the
            // parent's own WM_WINDOWPOSCHANGED lifts the tube back the instant main
            // moves up in z-order, with no polling gap.
            // Read the parent handle + install the parent-message hook ON the parent's dispatcher:
            // HwndSource is thread-affine, and reading the parent handle off its own thread VerifyAccess-
            // throws. Use BeginInvoke (ASYNC) in own-thread mode so this Loaded callback never blocks on the
            // main thread — during the own-thread bootstrap the main thread is blocked on the ready handshake
            // waiting for us to finish showing, so a synchronous Invoke here would deadlock. Inline (sync)
            // when the avatar shares the parent's thread (flag off) — identical to the original behaviour.
            if (_parentWindow != null)
            {
                void HookParent()
                {
                    try
                    {
                        _parentHandle = new WindowInteropHelper(_parentWindow).Handle;
                        if (_parentHandle != IntPtr.Zero)
                        {
                            _parentHwndSource = HwndSource.FromHwnd(_parentHandle);
                            _parentHwndSource?.AddHook(ParentWndProc);
                        }
                    }
                    catch (Exception ex) { App.Logger?.Debug("AvatarTube parent hook install failed: {Error}", ex.Message); }
                }
                if (_parentWindow.Dispatcher.CheckAccess()) HookParent();
                else _parentWindow.Dispatcher.BeginInvoke(new Action(HookParent));
            }

            // Hide from Alt+Tab by adding WS_EX_TOOLWINDOW style.
            // SetWindowLong caches frame data; without a follow-up SetWindowPos
            // with SWP_FRAMECHANGED the layered window (AllowsTransparency=True)
            // doesn't get a WM_NCCALCSIZE/WM_PAINT pass and the tube stays
            // invisible until the user moves the window. Forcing the frame
            // recalc here gives it that initial paint.
            int exStyle = GetWindowLong(_tubeHandle, GWL_EXSTYLE);
            SetWindowLong(_tubeHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            SetWindowPos(_tubeHandle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // Ensure NOT topmost when attached (starts attached)
            Topmost = false;

            // Calculate scale factor based on screen size and DPI
            CalculateScaleFactor();

            // Defer position update to ensure layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ParentVisibleNotMinimized)
                {
                    UpdatePosition();
                    StartFloatingAnimation();
                    // Force on first show: at startup foreground may not have transferred
                    // to us yet, so the gated raise would bail and the tube would come up
                    // behind the main window.
                    BringAttachedPairToFront(force: true);
                }

                            // Reset bubble position to ensure correct placement after layout
                            // Anchored at bottom, grows upward. Margin = left, top, right, bottom
                            var initUseAttached = _isAttached || ModOverridesAttachedTubeOnly();
                            var initDx = initUseAttached
                                ? EffAvatarOffsetX()
                                : EffAvatarDetachedOffsetX();
                            var initRight = initUseAttached ? 125 - initDx : 425 - initDx;
                            SpeechBubble.Margin = new Thickness(0, 0, initRight, 550);
                        }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Start fullscreen detection timer
            StartFullscreenDetection();
        }

        public void StartPoseAnimation()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(StartPoseAnimation)); return; }
            _poseTimer.Start();
        }
        public void StopPoseAnimation()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(StopPoseAnimation)); return; }
            _poseTimer.Stop();
        }

        public void SetPose(int poseNumber)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetPose(poseNumber))); return; }
            if (poseNumber < 1 || poseNumber > 4) return;
            if (_avatarPoses.Length == 0) return;
            _currentPoseIndex = poseNumber - 1;
            ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
        }

        public void SetPoseInterval(TimeSpan interval)
        {
            _poseTimer.Interval = interval;
        }
        
        /// <summary>
        /// Gets the current avatar set number
        /// </summary>
        public int CurrentAvatarSet => _currentAvatarSet;

        /// <summary>
        /// True while ANY speech bubble (AI or ordinary "Preset" bark/chatter) is currently being
        /// displayed. Unlike <see cref="IsCompanionBusy"/> this also covers non-AI bubbles, so the bark
        /// system can avoid stacking ordinary barks behind one that's already on screen — otherwise they
        /// queue and, by the time the queue drains, comment on something that happened seconds ago.
        /// </summary>
        public bool IsSpeaking => _isGiggling;
    }
}
