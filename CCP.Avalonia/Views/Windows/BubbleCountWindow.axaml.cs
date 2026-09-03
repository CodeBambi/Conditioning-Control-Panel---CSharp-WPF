using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Difficulty of a bubble-count game.
    ///
    /// ponytail: verbatim copy of <c>BubbleCountService.Difficulty</c>
    /// (ConditioningControlPanel/Services/BubbleCountService.cs:23). The service is WPF-side and
    /// this head may not reference that project, so the enum is duplicated rather than the game
    /// losing its difficulty. Delete this and use the real one the moment BubbleCountService
    /// moves to CCP.Core.
    /// </summary>
    public enum Difficulty { Easy, Medium, Hard }

    /// <summary>
    /// Bubble Count Challenge - watch video, count bubbles, enter total.
    ///
    /// PORTED from ConditioningControlPanel/Windows/BubbleCountWindow.xaml.cs. The WPF original is
    /// 1862 lines and roughly six sevenths of it is Windows-only media plumbing that this head has
    /// no counterpart for. What SURVIVES the port intact is the game itself: difficulty -> target
    /// count, the spawn cadence, the shared single-timer bubble animation, the pop lifecycle, the
    /// shared multi-window state, ESC-to-skip, the strict-mode lock and the completion contract.
    ///
    /// <para><b>Win32 -> Avalonia/X11.</b> Every P/Invoke in the original is gone; none of
    /// user32/shcore is referenced here.</para>
    /// <list type="bullet">
    ///   <item><c>SetWindowLong(GWL_EXSTYLE, WS_EX_TOOLWINDOW)</c> to hide from Alt+Tab ->
    ///         <c>ShowInTaskbar="False"</c> in the XAML.</item>
    ///   <item><c>SetWindowPos(HWND_TOPMOST)</c> (ForceTopmost, and the same call in CountBubble)
    ///         -> <c>Topmost="True"</c>. Avalonia maps that to <c>_NET_WM_STATE_ABOVE</c>, which is
    ///         the correct X11 mechanism, so nothing extra is needed (see X11Overlay's own note).
    ///         The bubble-above-game ordering, which HWND_TOPMOST alone never guaranteed either,
    ///         is pinned explicitly with <see cref="X11Overlay.RestackAbove"/>.</item>
    ///   <item><c>IsHitTestVisible=false</c> on a bubble window only stops Avalonia's own hit
    ///         testing, not the compositor's, so a bubble would still eat clicks meant for the
    ///         video. <see cref="X11Overlay.SetClickThrough"/> is the real equivalent of the
    ///         original's implicit click-through and is applied after Show().</item>
    ///   <item><c>MonitorFromPoint</c> + <c>GetDpiForMonitor</c> (GetDpiForScreen) -> Avalonia's
    ///         <c>Screen.Scaling</c>. The division by DPI that the original does on Left/Top also
    ///         goes: WPF's Left/Top are DIPs, Avalonia's <c>Window.Position</c> is a PixelPoint in
    ///         PHYSICAL pixels, which is what <c>Screen.WorkingArea</c> already is.</item>
    ///   <item><c>NoClickRaiseHook</c> (an HwndSource hook returning MA_NOACTIVATE for
    ///         WM_MOUSEACTIVATE) has NO equivalent - see the stub below for what is lost.</item>
    ///   <item><c>DisableProcessWindowsGhosting</c>-class calls: none in this file to drop.</item>
    /// </list>
    ///
    /// <para><b>WebView2.</b> The original has no <c>wv2:WebView2</c> element in its XAML; browser
    /// mode builds a <c>BrowserVideoSurface</c> (a WebView2 host) in code and does
    /// <c>VideoContainer.Children.Add(...)</c>. The port mirrors that exactly with
    /// <see cref="WebHost"/> - see <see cref="StartBrowserPlayback"/>. Everything the original did
    /// through CoreWebView2 (the shared environment, InitAsync, Post, the WebMessage pump,
    /// ProcessFailed) is a ponytail stub; the wrapper exposes none of it yet.</para>
    ///
    /// <para><b>Wired:</b> the pop sound (<c>CoreAudio.PlayOneShot</c> at the WPF
    /// <c>(master * bubbles) ^ 1.5</c> volume), the monitor set (<c>DualMonitorEnabled</c> from
    /// <c>CoreSettings</c>, screens from <c>ScreenList.Enumerate</c>) and the result window, so a
    /// finished game asks for the count and resolves on the answer instead of being written off.</para>
    ///
    /// <para><b>Stubbed, all service-shaped:</b> the whole LibVLC path (VideoService lease,
    /// CreateManagedPlayer/ReleaseManagedPlayer, the wedge watchdog, the native poison cooldown,
    /// the bounded pumped Stop() batch, VideoView attach/detach and every message-pump wait that
    /// only existed to keep HwndHost teardown safe), BrowserVideoEngine/BrowserVideoGate,
    /// ModResourceResolver, App.Achievements and VideoDiag. Each is marked at its site.</para>
    ///
    /// <para><c>Loaded</c> became <c>Opened</c>: WPF's Loaded fires synchronously inside Show(),
    /// which ShowOnAllMonitors' completion de-duplication leans on, and Avalonia's Opened is the
    /// event with that timing (Loaded is posted after layout).</para>
    /// </summary>
    public partial class BubbleCountWindow : Window
    {
        private readonly string _videoPath;
        private readonly Difficulty _difficulty;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        private readonly Screen? _screen;
        private readonly bool _isPrimary;
        /// <summary>This game plays out-of-process in a web view instead of a leased LibVLC player.
        /// Nothing else about the game changes: bubbles, counting, difficulty, the result window,
        /// the strict lock and the XP flow are shared verbatim.</summary>
        private readonly bool _useBrowser;

        private readonly Random _random = new();
        private readonly List<CountBubble> _activeBubbles = new();
        private DispatcherTimer? _bubbleSpawnTimer;
        private DispatcherTimer? _safetyTimer;
        // Single shared animation timer for ALL count bubbles, replacing the previous
        // two-DispatcherTimers-per-bubble model (up to ~24 concurrent timers).
        private DispatcherTimer? _bubbleAnimTimer;
        private const double BubbleAnimTickMs = 30;

        private int _bubbleCount = 0;
        private int _targetBubbleCount = 0;
        private double _videoDurationSeconds = 30;
        private bool _videoEnded = false;
        private bool _gameCompleted = false;

        /// <summary>The bubble artwork, or null to draw the gradient ellipse fallback.
        /// Always null today - see <see cref="LoadBubbleImage"/>.</summary>
        private IImage? _bubbleImage;

        // The web view surface, browser mode only. Mutually exclusive with the LibVLC player the
        // original also carried here: a game is one engine or the other.
        private WebHost? _browserSurface;

        // Multi-monitor support - static shared state
        private static readonly object _cleanupLock = new();
        private static bool _isCleaningUp = false;
        private static readonly List<BubbleCountWindow> _allWindows = new();
        private static int _sharedBubbleCount = 0;
        private static int _sharedTargetCount = 0;

        /// <summary>
        /// Routing decision for the game being started, taken ONCE in <see cref="ShowOnAllMonitors"/>
        /// and copied into every window's <c>_useBrowser</c> as it is constructed. A static handover
        /// rather than a constructor argument so the public signature does not change.
        /// </summary>
        private static bool _nextGameUsesBrowser;

        /// <summary>Fallback duration when the metadata cache has never seen this video.</summary>
        private const double FallbackDurationSeconds = 30;

        private readonly Grid _videoContainer;
        private readonly TextBlock _txtDifficulty, _txtStrict, _txtEscHint;

        /// <summary>Duration of the last played video in seconds (shared for XP scaling)</summary>
        internal static double LastVideoDurationSeconds { get; private set; } = 30;

        /// <summary>
        /// Render/design constructor: a game window with no game running, so --render-all can draw
        /// it. Everything it does beyond the real ctor exists because starting a game headlessly
        /// would be worse than useless: <see cref="OnOpened"/> aborts on a nonexistent video file
        /// and closes the window mid-render, and the safety/spawn timers it starts would outlive
        /// this view and tick through every LATER view in the same --render-all process.
        /// </summary>
        internal BubbleCountWindow() : this("sample.mp4", Difficulty.Medium, false, _ => { })
        {
            Opened -= OnOpened;
            // Not "the game was won" - "no game is running on this window", which is what every
            // read of this flag actually asks. Keeps the ESC handler and the focus-reclaim loop
            // inert during the render.
            _gameCompleted = true;
            Width = 1280;
            Height = 720;
            // The browser seam, shown the way browser mode shows it. On a machine with no web
            // engine (every CI runner, every headless render) WebHost draws its fallback panel,
            // so the proof is a legible "no web view here" rather than a black rectangle.
            _videoContainer.Children.Add(new WebHost());
        }

        public BubbleCountWindow(string videoPath, Difficulty difficulty,
            bool strictMode, Action<bool> onComplete,
            Screen? screen = null, bool isPrimary = true)
        {
            AvaloniaXamlLoader.Load(this);

            _videoContainer = this.FindControl<Grid>("VideoContainer")!;
            _txtDifficulty = this.FindControl<TextBlock>("TxtDifficulty")!;
            _txtStrict = this.FindControl<TextBlock>("TxtStrict")!;
            _txtEscHint = this.FindControl<TextBlock>("TxtEscHint")!;

            _videoPath = videoPath;
            _difficulty = difficulty;
            _strictMode = strictMode;
            _onComplete = onComplete;
            // Screens can be empty under a headless platform, so this stays nullable and every
            // read of it is guarded. WPF's Screen.PrimaryScreen! could not be null.
            _screen = screen ?? Screens?.Primary ?? Screens?.All.FirstOrDefault();
            _isPrimary = isPrimary;
            _useBrowser = _nextGameUsesBrowser;

            // Set difficulty display. A local value, not a {loc:Str} binding - see the XAML.
            _txtDifficulty.Text = $" ({difficulty})";

            // Handle strict mode
            if (_strictMode)
            {
                _txtStrict.IsVisible = true;
                _txtEscHint.IsVisible = false;
            }

            // Initial small position on target screen (will maximize after show).
            // No DPI division here, unlike WPF: Position is in physical pixels and so is Bounds.
            if (_screen != null)
            {
                Position = new PixelPoint(_screen.Bounds.X + 100, _screen.Bounds.Y + 100);
            }
            Width = 400;
            Height = 300;

            // Load bubble image
            LoadBubbleImage();

            // Key handler
            KeyDown += OnKeyDown;

            // Reclaim focus when stolen by other windows (only primary needs focus)
            if (_isPrimary)
            {
                Deactivated += (s, e) =>
                {
                    if (!_gameCompleted && !_videoEnded)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!_gameCompleted && !_videoEnded)
                            {
                                // SetForegroundWindow's mapping. Focus() is implied by Activate()
                                // on a window with no focused child.
                                Activate();
                                Focus();
                            }
                        }, DispatcherPriority.Input);
                    }
                };
            }

            // Register window
            _allWindows.Add(this);

            // Start when opened
            Opened += OnOpened;
        }

        /// <summary>
        /// Show bubble count game on all monitors.
        /// </summary>
        /// <param name="onSkipped">Optional "the game never happened" resolution. A skip is NOT a
        /// loss: routing it through <paramref name="onComplete"/> with false would read as a failed
        /// count and, in strict mode, start the WRONG! WATCH AGAIN retry loop for a game the user
        /// never saw. When null, a skip falls back to the completion callback (old behaviour).</param>
        public static void ShowOnAllMonitors(string videoPath, Difficulty difficulty,
            bool strictMode, Action<bool> onComplete, Action? onSkipped = null)
        {
            // The completion callback must reach the caller EXACTLY once. Every window shares this
            // delegate and CloseAllWindows invokes it, which can happen INSIDE the Show() below
            // (Opened runs synchronously there, and a start failure closes the game from it) - so
            // the catch at the bottom would otherwise deliver a second, contradictory completion.
            int completionDelivered = 0;
            Action<bool> complete = ok =>
            {
                if (System.Threading.Interlocked.Exchange(ref completionDelivered, 1) != 0) return;
                onComplete?.Invoke(ok);
            };

            // Reset shared state
            lock (_cleanupLock)
            {
                _isCleaningUp = false;
            }
            var orphanWindows = _allWindows.ToList();
            _allWindows.Clear();
            _sharedBubbleCount = 0;
            _sharedTargetCount = 0;

            // Only reachable when a previous game threw partway through its show/teardown;
            // normally the list is already empty.
            // ponytail: the WPF original also stopped and released every LEASED LibVLC player here
            // before closing the windows, because detaching a live VideoView is the historic
            // multi-monitor crash. There is no player and no VideoService on this head, so the
            // window loop is all that is left of it. Restore when VideoService moves to Core.
            foreach (var window in orphanWindows)
            {
                try
                {
                    window._safetyTimer?.Stop();
                    window._bubbleSpawnTimer?.Stop();
                    window.Close();
                }
                catch { }
            }

            // ponytail: needs BrowserVideoGate/BrowserVideoEngine to pick the engine for this file,
            // and VideoService.NativePoisonCooldownRemainingMs to refuse to start on a LibVLC
            // instance a wedged Stop() already poisoned. Both are WPF-side services. Until they
            // move, every game takes the browser path - which is the only one this head can host.
            _nextGameUsesBrowser = true;

            // One screen or all of them, per the user's setting - the same question
            // BubbleCountResultWindow.ShowOnAllMonitors asks, answered from the same place.
            //
            // Avalonia has no screen list without a TopLevel, so the primary is built first and its
            // Screens is what the set is drawn from; WPF read App.GetAllScreensCached() up front.
            // An empty enumeration (headless, or no platform impl yet) is the single-screen path,
            // NOT WPF's onComplete(false): here empty means "no topology reported", not "no display".
            try
            {
                var primaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, complete, null, true);
                primaryWindow.Show();
                primaryWindow.WindowState = WindowState.Maximized;

                if (CoreSettings.Current.DualMonitorEnabled)
                {
                    var all = Features.ScreenList.Enumerate(primaryWindow);
                    // Drawn from the SAME list the loop filters, never from primaryWindow._screen:
                    // that came from Screens?.Primary in the ctor and Screen does not promise
                    // reference equality across reads, so a mismatch would open a second fullscreen
                    // game on the primary display.
                    var primary = all.FirstOrDefault(s => s.IsPrimary) ?? all.FirstOrDefault();
                    foreach (var screen in all.Where(s => s != primary))
                    {
                        var secondary = new BubbleCountWindow(videoPath, difficulty, strictMode, complete, screen, false);
                        secondary.Show();
                        secondary.WindowState = WindowState.Maximized;
                    }
                }

                // Activate the primary LAST so it owns the keyboard, exactly as WPF does.
                // ForceTopmost's SetWindowPos(HWND_TOPMOST) is gone: Topmost="True" in the XAML is
                // the same thing, applied by the platform rather than after the fact.
                primaryWindow.Activate();
            }
            catch
            {
                // No-op when the window already delivered one from inside Show().
                complete(false);
            }
        }

        /// <summary>
        /// Force close all bubble count windows (used by panic button)
        /// </summary>
        public static void ForceCloseAll()
        {
            lock (_cleanupLock)
            {
                if (_isCleaningUp) return;
                _isCleaningUp = true;
            }

            try
            {
                var windowsCopy = _allWindows.ToList();
                _allWindows.Clear();
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        window._safetyTimer?.Stop();
                        window._bubbleSpawnTimer?.Stop();
                        window.Close();
                    }
                    catch { }
                }

                // ponytail: needs VideoService (the leased-player stop batch and the wedge
                // watchdog disarm) and App.BubbleCount.ResetBusyState(). Both WPF-side.
            }
            finally
            {
                lock (_cleanupLock)
                {
                    _isCleaningUp = false;
                }
            }
        }

        /// <summary>
        /// Check if any bubble count window is currently open
        /// </summary>
        public static bool IsAnyOpen() => _allWindows.Count > 0;

        private void OnOpened(object? sender, EventArgs e)
        {
            // ponytail: WPF registered this window's HWND with VideoService here
            // (RegisterManagedWindow) so a wedged UI thread could still be rescued, and armed the
            // wedge watchdog before the first window existed. Both are VideoService, and both only
            // guard an in-process LibVLC decoder - browser mode never armed them at all.

            // ponytail: the WPF original installed an HwndSource hook returning MA_NOACTIVATE for
            // WM_MOUSEACTIVATE. LOST BEHAVIOUR, no X11 equivalent in the head today: a click on
            // the game window will raise it inside the keep-above band, and the count bubbles -
            // separate topmost windows - can disappear behind it until the next spawn restacks
            // them. Needs an X11Overlay capability (deny focus-on-click / _NET_WM_USER_TIME), and
            // this layer may not edit X11Overlay.cs, so it is its own change.

            try
            {
                // ponytail: needs a file-existence check against the real media root. Kept as-is;
                // the abort path below is the game's only defence against a missing clip.
                if (string.IsNullOrWhiteSpace(_videoPath))
                {
                    if (_isPrimary) CloseAllWindows(false);
                    return;
                }

                // Browser mode owns the whole start-up sequence. Everything AFTER playback start
                // (bubbles, counting, result window, strict lock, XP) is shared with the LibVLC
                // path below and in CloseAllWindows.
                if (_useBrowser)
                {
                    StartBrowserPlayback();
                    return;
                }

                // ponytail: the LibVLC path. Needs LibVLCSharp (Windows-only on this repo's
                // packaging) plus VideoService.CreateManagedPlayer / the Media+Play sequence /
                // EndReached+EncounteredError+LengthChanged wiring / App.Audio's device routing /
                // App.Settings' MasterVolume. None of it exists off the WPF head, so a non-browser
                // game cannot start here and is resolved rather than left on a black screen.
                if (_isPrimary) CloseAllWindows(false);
            }
            catch
            {
                if (_isPrimary) CloseAllWindows(false);
            }
        }

        /// <summary>
        /// Duration for the game clock. WPF read a metadata cache and queued a background parse on
        /// a miss; the value is corrected the moment the player reports its real length.
        /// </summary>
        private static double ResolveVideoDurationSeconds(string path)
        {
            // ponytail: needs VideoMetadataCache (WPF-side). Always the fallback until it moves.
            return FallbackDurationSeconds;
        }

        /// <summary>
        /// Replace the estimated duration with the one the player actually reports, and re-scale
        /// the game clock around it. Only the primary owns the clock.
        /// </summary>
        private void AdoptRealDuration(double seconds)
        {
            if (!_isPrimary || _videoEnded || _isCleaningUp) return;
            if (seconds <= 0 || Math.Abs(seconds - _videoDurationSeconds) < 0.5) return;

            _videoDurationSeconds = seconds;
            LastVideoDurationSeconds = seconds;
            CalculateTargetBubbles();
            _sharedTargetCount = _targetBubbleCount;
            StartSafetyTimer(_videoDurationSeconds);
        }

        #region Browser engine

        private void StartBrowserPlayback()
        {
            // ponytail: needs BrowserVideoEngine.BuildPageUrl to map the clip onto the player
            // page's virtual host. Without it there is no page to navigate to, so the surface is
            // built (that IS the port of VideoContainer.Children.Add(_browserSurface)) and left
            // showing WebHost's fallback rather than a black rectangle.
            _browserSurface = new WebHost();
            _videoContainer.Children.Add(_browserSurface);

            // ponytail: needs CoreWebView2. The original then did, in order:
            //   _browserSurface.Message += OnBrowserMessage        (WebMessageReceived pump:
            //       playing / timeupdate / ended / error / key reports drive the whole game clock)
            //   _browserSurface.ProcessFailed += OnBrowserProcessFailed
            //   _browserSurface.Post(new { type = "load", url, volume, muted, blurBackground,
            //       hideCursor, startAtMs, sinkLabel })                (ExecuteScript/PostWebMessage)
            //   await BrowserVideoEngine.SharedEnvironmentAsync()   (CoreWebView2Environment)
            //   await surface.InitAsync(env, mappings, startUrl, host)
            //       (EnsureCoreWebView2Async + SetVirtualHostNameToFolderMapping + Navigate)
            // WebHost exposes only Source, so none of it can be expressed yet and none of it is
            // invented here. Consequences: no audio routing, no first-frame watch, no page keys,
            // and the clip never actually plays - the safety timer below is what ends the game.

            if (_isPrimary)
            {
                _videoDurationSeconds = ResolveVideoDurationSeconds(_videoPath);
                LastVideoDurationSeconds = _videoDurationSeconds;

                CalculateTargetBubbles();
                _sharedTargetCount = _targetBubbleCount;

                StartSafetyTimer(_videoDurationSeconds);
                StartBubbleSpawning();
            }
            else
            {
                _targetBubbleCount = _sharedTargetCount;
            }
        }

        /// <summary>Keys over a focused web view go to the page, not to this window, so the page
        /// reported them back and they were replayed here.</summary>
        private void OnBrowserKey(string key)
        {
            if (key == "Escape" && !_strictMode && !_gameCompleted && !_isCleaningUp)
            {
                _gameCompleted = true;
                CloseAllWindows(false);
            }
        }

        /// <summary>The clip could not be played. A secondary just loses its mirror; the primary
        /// ends the game.</summary>
        private void OnBrowserFailure(string reason, bool blameFile)
        {
            if (!_isPrimary) return;
            if (_gameCompleted || _isCleaningUp) return;
            _gameCompleted = true;
            CloseAllWindows(false);
        }

        /// <summary>Unhook, stop the clip and dispose the surface. Called from OnClosed, which
        /// every teardown path reaches.</summary>
        private void DisposeBrowserSurface()
        {
            var surface = _browserSurface;
            if (surface == null) return;
            _browserSurface = null;
            // ponytail: needs CoreWebView2 - the original detached Message/ProcessFailed, posted
            // {type="stop"} and Dispose()d the WebView2, which is what actually ends the browser
            // process. Removing the control from the tree is all WebHost allows today, so a
            // WebKitGTK process may outlive the window until the wrapper grows a Dispose.
            try { _videoContainer.Children.Remove(surface); } catch { }
        }

        #endregion

        private void CalculateTargetBubbles()
        {
            double baseRate = _difficulty switch
            {
                Difficulty.Easy => 3,
                Difficulty.Medium => 5,
                Difficulty.Hard => 8,
                _ => 5
            };

            var scaledCount = (baseRate / 30.0) * _videoDurationSeconds;
            var variance = scaledCount * 0.2;
            _targetBubbleCount = (int)Math.Round(scaledCount + (_random.NextDouble() * variance * 2 - variance));
            _targetBubbleCount = Math.Max(3, _targetBubbleCount);
        }

        private void LoadBubbleImage()
        {
            // ponytail: needs Services.ModResourceResolver (mod-overridable "bubble.png"), and the
            // WPF fallback was a pack:// URI into the WPF assembly's Resources - neither exists
            // here, and this layer may not add an AvaloniaResource to the csproj. _bubbleImage
            // stays null, so every bubble draws CountBubble's gradient-ellipse fallback, which the
            // WPF original also drew whenever the image failed to load.
            _bubbleImage = null;
        }

        private void StartSafetyTimer(double videoDurationSeconds)
        {
            _safetyTimer?.Stop();

            var timeoutSeconds = videoDurationSeconds + 5;

            _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            _safetyTimer.Tick += (s, e) =>
            {
                _safetyTimer?.Stop();
                if (!_videoEnded && !_isCleaningUp)
                {
                    OnVideoEnded();
                }
            };
            _safetyTimer.Start();
        }

        private void StartBubbleSpawning()
        {
            if (!_isPrimary) return;

            var intervalMs = (_videoDurationSeconds * 1000) / Math.Max(1, _targetBubbleCount);

            _bubbleSpawnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs * 0.7)
            };

            _bubbleSpawnTimer.Tick += (s, e) =>
            {
                if (_sharedBubbleCount < _targetBubbleCount && !_videoEnded && !_isCleaningUp)
                {
                    if (_random.NextDouble() < 0.7 || _sharedBubbleCount < _targetBubbleCount / 2)
                    {
                        SpawnBubbleOnAllWindows();
                    }
                }
            };

            // Delay bubble spawning until layout is complete. WPF used
            // Task.Delay(1500).ContinueWith(dispatcher.Invoke); RunOnce is the same thing without
            // the thread hop, and the null check is what makes a close during the delay a no-op.
            DispatcherTimer.RunOnce(() =>
            {
                if (_videoEnded || _isCleaningUp || _bubbleSpawnTimer == null) return;
                _bubbleSpawnTimer.Start();
                SpawnBubbleOnAllWindows();
            }, TimeSpan.FromMilliseconds(1500));
        }

        private void SpawnBubbleOnAllWindows()
        {
            if (_sharedBubbleCount >= _targetBubbleCount) return;
            _sharedBubbleCount++;
            _bubbleCount = _sharedBubbleCount;

            // Random position (relative 0-1)
            var relX = _random.NextDouble() * 0.7 + 0.15;
            var relY = _random.NextDouble() * 0.5 + 0.25;
            var size = _random.Next(120, 225);

            // Spawn on ONE random window - posted at Background priority so it never competes with
            // the video surface's own drawing, exactly as in WPF.
            var windows = _allWindows.ToList();
            if (windows.Count > 0)
            {
                var randomWindow = windows[_random.Next(windows.Count)];
                Dispatcher.UIThread.Post(() => randomWindow.SpawnBubbleAt(relX, relY, size),
                    DispatcherPriority.Background);
            }
        }

        private void SpawnBubbleAt(double relX, double relY, int size)
        {
            try
            {
                if (_screen == null) return;

                // Convert relative position to screen coordinates. WorkingArea excludes the panel,
                // and it is in PHYSICAL pixels - as is Window.Position - so the WPF original's
                // per-component DPI division is gone. The bubble's Width/Height are still DIPs,
                // hence the scaling on the half-size centring offset only.
                var area = _screen.WorkingArea;
                var scale = _screen.Scaling;
                var screenX = (int)Math.Round(area.X + relX * area.Width - size * scale / 2.0);
                var screenY = (int)Math.Round(area.Y + relY * area.Height - size * scale / 2.0);

                // Sound plays on pop (in StartPopping), not on spawn

                // Bubble is a separate window (doesn't block the video surface's rendering)
                var bubble = new CountBubble(_bubbleImage, size, screenX, screenY, _random,
                    PlayPopSound, this);
                _activeBubbles.Add(bubble);
                EnsureBubbleAnimTimer();
            }
            catch { }
        }

        /// <summary>Lazily start the shared animation timer once there are bubbles to animate.</summary>
        private void EnsureBubbleAnimTimer()
        {
            if (_bubbleAnimTimer != null) return;
            _bubbleAnimTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(BubbleAnimTickMs)
            };
            _bubbleAnimTimer.Tick += AnimateAllCountBubbles;
            _bubbleAnimTimer.Start();
        }

        /// <summary>Advances every active count bubble and reaps finished ones. Stops the timer
        /// when no bubbles remain (it restarts on the next spawn).</summary>
        private void AnimateAllCountBubbles(object? sender, EventArgs e)
        {
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                if (i >= _activeBubbles.Count) continue;
                var bubble = _activeBubbles[i];
                bubble.Tick(BubbleAnimTickMs);
                if (bubble.IsFinished)
                {
                    _activeBubbles.RemoveAt(i);
                    bubble.Dispose();
                }
            }

            if (_activeBubbles.Count == 0)
            {
                _bubbleAnimTimer?.Stop();
                _bubbleAnimTimer = null;
            }
        }

        /// <summary>
        /// One of the three pop samples, at (master * bubbles) ^ 1.5 - the WPF formula verbatim.
        /// Unseeded CoreAudio is a no-op that still returns, so a head with no audio backend just
        /// pops silently rather than stalling the animation tick that calls this.
        /// </summary>
        private void PlayPopSound()
        {
            var soundIndex = _random.Next(3);
            var masterVolume = CoreSettings.Current.MasterVolume / 100f;
            var bubblesVolume = CoreSettings.Current.BubblesVolume / 100f;

            try
            {
                var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var popPath = Path.Combine(soundsPath, popFiles[soundIndex]);

                var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5);
                CoreAudio.PlayOneShot(popPath, volume, "bubblecount-pop");
            }
            catch { }
        }

        private void OnVideoEnded()
        {
            if (_videoEnded || _isCleaningUp) return;
            _videoEnded = true;

            _safetyTimer?.Stop();
            _bubbleSpawnTimer?.Stop();

            // Mark all windows as ended
            foreach (var window in _allWindows.ToList())
            {
                window._videoEnded = true;
                window._bubbleSpawnTimer?.Stop();
                // ponytail: needs CoreWebView2 - the original posted {type="pause"} to each page
                // here, because the game windows are only HIDDEN for the result screen and a clip
                // still running would keep playing behind it.
            }

            // Clear remaining bubbles on all windows (bubbles are separate windows now).
            // Dispose directly: the shared animation timer that would otherwise finish their
            // pop-out is about to stop, so close them now to avoid orphaned windows.
            foreach (var window in _allWindows.ToList())
            {
                foreach (var bubble in window._activeBubbles.ToArray())
                {
                    bubble.Dispose();
                }
                window._activeBubbles.Clear();
                window._bubbleAnimTimer?.Stop();
                window._bubbleAnimTimer = null;
            }

            // ponytail: needs App.Achievements.TrackVideoWatched(_videoDurationSeconds) for the
            // primary. The XP the game earns is awarded by the result window, not here.

            // Show result window (only from primary)
            if (_isPrimary)
            {
                ShowResultWindow();
            }
        }

        /// <summary>
        /// Hide the game and ask for the count. Verbatim WPF: the game windows are HIDDEN, not
        /// closed, so the result window's own multi-monitor set can be torn down independently and
        /// its answer still resolves this game through the shared completion callback.
        /// </summary>
        private void ShowResultWindow()
        {
            foreach (var window in _allWindows.ToList())
            {
                try { window.Hide(); } catch { }
            }

            BubbleCountResultWindow.ShowOnAllMonitors(
                _sharedBubbleCount,
                _strictMode,
                success =>
                {
                    _gameCompleted = true;
                    CloseAllWindows(success);
                });
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_gameCompleted && !_isCleaningUp)
            {
                _gameCompleted = true;
                CloseAllWindows(false);
            }
        }

        private void CloseAllWindows(bool success)
        {
            lock (_cleanupLock)
            {
                if (_isCleaningUp) return;
                _isCleaningUp = true;
            }

            try
            {
                // ponytail: needs VideoService. The WPF original stopped every leased player off
                // the dispatcher under a bounded, message-pumping wait, detached each VideoView,
                // pumped 50ms more, and only then closed the windows and released the players
                // (disposing the ones that stopped, quarantining the ones that wedged). All of it
                // existed to make HwndHost teardown survivable; there is no HwndHost and no player
                // here, so the window loop is the whole of it.
                var windowsCopy = _allWindows.ToList();
                _allWindows.Clear();
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        window._safetyTimer?.Stop();
                        window._bubbleSpawnTimer?.Stop();
                        window.Close();
                    }
                    catch { }
                }

                // Invoke completion callback
                _onComplete?.Invoke(success);
            }
            finally
            {
                lock (_cleanupLock)
                {
                    _isCleaningUp = false;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _safetyTimer?.Stop();
            _safetyTimer = null;
            _bubbleSpawnTimer?.Stop();
            // Nulled, not just stopped: StartBubbleSpawning's 1500ms deferred start reads this
            // field and must become a no-op on a window that has already closed.
            _bubbleSpawnTimer = null;
            _bubbleAnimTimer?.Stop();
            _bubbleAnimTimer = null;
            // Closing the window alone would leave the browser surface alive.
            DisposeBrowserSurface();

            foreach (var bubble in _activeBubbles)
            {
                bubble.Dispose();
            }
            _activeBubbles.Clear();

            _allWindows.Remove(this);

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Individual bubble for the counting game - its own window, so it never shares a visual tree
    /// with the video surface.
    ///
    /// PORTED from the CountBubble class in the same WPF file. The Win32 in it
    /// (<c>SetWindowPos(HWND_TOPMOST)</c>) is gone: <c>Topmost</c> is the platform's own answer.
    /// What DOES need the shim is the pair the original got for free from Windows - being
    /// transparent to the mouse, and sitting above the game window specifically rather than
    /// somewhere in the keep-above band. Both are applied after Show(), because both need a
    /// mapped X11 window, and both are no-ops off X11 by design.
    /// </summary>
    internal class CountBubble : IDisposable
    {
        private readonly Window _window;
        private readonly Image _imageElement;

        private readonly Action? _playSound;

        private double _scale = 0.1;
        private readonly double _targetScale = 1.0;
        private double _opacity = 1.0;
        private double _rotation = 0;
        private bool _isPopping = false;
        private bool _isDisposed = false;
        private double _lifeRemainingMs;

        /// <summary>True once the pop animation has fully faded out; the owning window
        /// removes and disposes finished bubbles on its shared animation tick.</summary>
        public bool IsFinished { get; private set; }

        public CountBubble(IImage? image, int size, int screenX, int screenY,
            Random random, Action? playSound, Window owner)
        {
            _playSound = playSound;
            _rotation = random.Next(360);

            _imageElement = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Source = image,
                RenderTransformOrigin = RelativePoint.Center
            };

            if (image == null)
            {
                // WPF drew this through a DrawingGroup/DrawingContext; Avalonia's DrawingImage
                // takes a GeometryDrawing directly, same ellipse, same stroke, same gradient.
                var gradientBrush = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(200, 255, 182, 193), 0),
                        new GradientStop(Color.FromArgb(100, 255, 105, 180), 1),
                    }
                };
                _imageElement.Source = new DrawingImage
                {
                    Drawing = new GeometryDrawing
                    {
                        Brush = gradientBrush,
                        Pen = new Pen(Brushes.White, 2),
                        Geometry = new EllipseGeometry(new Rect(5, 5, size - 10, size - 10)),
                    }
                };
            }

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(_scale, _scale));
            transformGroup.Children.Add(new RotateTransform(_rotation));
            _imageElement.RenderTransform = transformGroup;

            // Create separate window for bubble (doesn't share a visual tree with the video)
            _window = new Window
            {
                // WindowDecorations, not the SystemDecorations the .axaml files still use:
                // Avalonia 12 obsoleted the old property and the enum it took no longer resolves
                // from C#. Same meaning - no title bar, no border.
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                CanResize = false,
                IsHitTestVisible = false,
                Width = size,
                Height = size,
                Position = new PixelPoint(screenX, screenY),
                Content = _imageElement
            };

            _window.Show();

            // IsHitTestVisible only silences Avalonia's own hit testing; the compositor would still
            // send the click here. This is the WS_EX_TRANSPARENT the original got implicitly.
            X11Overlay.SetClickThrough(_window, true);
            // Topmost puts the bubble in the keep-above band; it does not order it against the game
            // window, which is topmost too. This pins it directly above its owner - the ordering
            // the original's SetWindowPos(HWND_TOPMOST)-per-bubble was reaching for.
            X11Overlay.RestackAbove(_window, owner);

            // Lifespan - bubble stays for 1-1.5 seconds then pops. Driven by the owning
            // window's single shared animation timer (see BubbleCountWindow.AnimateAllCountBubbles)
            // rather than per-bubble timers, which previously meant ~2 DispatcherTimers per bubble.
            _lifeRemainingMs = 1000 + random.Next(500);
        }

        /// <summary>
        /// Advance this bubble one frame. Called by the owning window's shared timer with the
        /// elapsed milliseconds since the last tick. Sets <see cref="IsFinished"/> when done.
        /// </summary>
        public void Tick(double dtMs)
        {
            if (_isDisposed) return;

            try
            {
                if (!_isPopping)
                {
                    _lifeRemainingMs -= dtMs;
                    if (_lifeRemainingMs <= 0) StartPopping();
                }

                if (_isPopping)
                {
                    _scale += 0.08;
                    _opacity -= 0.12;
                    _rotation += 5;

                    if (_opacity <= 0)
                    {
                        IsFinished = true;
                        return;
                    }
                }
                else
                {
                    if (_scale < _targetScale)
                    {
                        _scale = Math.Min(_targetScale, _scale + 0.1);
                    }
                    _rotation += 0.5;
                }

                _window.Opacity = Math.Max(0, _opacity);

                if (_imageElement.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
                {
                    if (tg.Children[0] is ScaleTransform st)
                    {
                        st.ScaleX = _scale;
                        st.ScaleY = _scale;
                    }
                    if (tg.Children[1] is RotateTransform rt)
                    {
                        rt.Angle = _rotation;
                    }
                }
            }
            catch { }
        }

        private void StartPopping()
        {
            if (_isPopping || _isDisposed) return;
            _isPopping = true;
            _playSound?.Invoke();
        }

        public void ForcePop()
        {
            if (_isDisposed) return;
            StartPopping();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try { _window.Close(); } catch { }
        }
    }
}
