using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// One conversational line in the tube's chat-history view.
    ///
    /// PORTED from AvatarTubeWindow.ChatMessage (AvatarTubeWindow.Speech.cs), where it is a nested
    /// public class. Top-level here so the compiled-binding <c>x:DataType</c> in the DataTemplate
    /// can name it without the nested-type syntax; it deletes and re-points at the Core type the
    /// moment the speech pipeline moves.
    /// </summary>
    public sealed class ChatMessage
    {
        public string Text { get; set; } = string.Empty;
        public bool IsUser { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string TimeLabel => Timestamp.ToString("HH:mm");
    }

    /// <summary>
    /// The companion's tube: a frameless, click-through-adjacent overlay that rides beside the main
    /// window and holds the avatar, her speech bubble, the chat input, the chat log, the Takeover
    /// countdown, THE FUSE's candle and the Possession note card.
    ///
    /// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.xaml.cs. What crossed, and
    /// what did not:
    ///
    /// <para><b>Win32.</b> The WPF file P/Invokes user32 in three places and every one of them maps
    /// or drops:</para>
    /// <list type="bullet">
    ///   <item><c>SetWindowLong(GWL_EXSTYLE, WS_EX_TOOLWINDOW)</c> (hide from Alt+Tab) ->
    ///         <c>ShowInTaskbar="False"</c> in the XAML, plus <c>ShowActivated="False"</c> for the
    ///         WS_EX_NOACTIVATE half. Declarative, so nothing to call.</item>
    ///   <item><c>SetWindowLongPtr(GWL_HWNDPARENT)</c> - native ownership, which is what glued the
    ///         tube directly above main in z-order - -> <see cref="X11Overlay.RestackAbove"/> in
    ///         <c>OnOpened</c>. Called unconditionally: the shim returns false off X11.</item>
    ///   <item><c>SetWindowPos(SWP_FRAMECHANGED)</c>, <c>HwndSource.AddHook(WndProc)</c>,
    ///         <c>WindowInteropHelper</c>, <c>DisableProcessWindowsGhosting</c> - all dropped. They
    ///         exist to flush a LAYERED window's cached frame and to intercept WM_ messages; X11 has
    ///         neither the cache nor the message pump, so there is nothing to work around.</item>
    /// </list>
    ///
    /// <para><b>ponytail: the restack is a one-shot.</b> Win32 ownership is PERSISTENT - the window
    /// manager re-applies it every time main is raised. <c>_NET_RESTACK_WINDOW</c> is a single
    /// request, so the tube can fall behind main after the user raises main again. Fixing that needs
    /// the shim to grow a "follow this window" mode, which is its own layer.</para>
    ///
    /// <para><b>ponytail: no own-thread mode.</b> WPF ran this window on its own STA thread behind
    /// <c>AppSettings.AvatarOwnThread</c>, which is why every public method starts with a
    /// <c>Dispatcher.CheckAccess()</c> self-marshal. Avalonia has one UI thread per process, so
    /// <see cref="RunOnAvatar"/> marshals to <c>Dispatcher.UIThread</c> and the setting has no
    /// meaning here. The guards are kept so callers from a worker thread still behave.</para>
    ///
    /// <para><b>ponytail: the eight partials did not come.</b> This layer ports the .xaml.cs only.
    /// Avatar loading and the 60fps float loop (Avatar.cs), speech and barks (Speech.cs), chat and
    /// the AI pipeline (ChatInput.cs), Circe emotes (CirceEmotes.cs), reactions (Reactions.cs), the
    /// candle (DescentFuse.cs) and ALL of the attach/detach/scale/position/fullscreen windowing
    /// (Windowing.cs) stay in the WPF head. Everything the WPF constructor called into them is a
    /// stub below, and the whole <c>App.*</c> service-subscription block (Video, BubbleCount, Flash,
    /// Subliminal, Bubbles, Achievements, Progression, Companion, WindowAwareness, MindWipe,
    /// BrainDrain, ModerationCounter, Mods) is one stub: none of those services are in Core yet.</para>
    /// </summary>
    public partial class AvatarTubeWindow : Window
    {
        private readonly Window? _parentWindow;
        private readonly Random _random = new();

        // Chat history: only the conversational pair (user prompts + AI replies), not random
        // preset/trigger chatter. Capped at MaxChatHistorySize entries.
        private const int MaxChatHistorySize = 100;
        public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

        // Bubble-state flags. In WPF these are written across Speech.cs / ChatInput.cs; here they
        // only back IsSpeaking / HasBubbleUp, which EMI Desk and the bark system ask about.
        private bool _isGiggling;
        private bool _isListeningBubble;
        private bool _isShowingChatHistory;
        private bool _isShowingAiBubble;
        private bool _isPlayingUninterruptibleClip;

        // Pose cycling (static avatars only). The poses themselves load in Avatar.cs.
        private readonly DispatcherTimer _poseTimer;
        private int _currentPoseIndex;
        private readonly int _currentAvatarSet = 1;

        // Moderation cooldown (P1.4).
        private DateTime? _cooldownEndsAt;
        private DispatcherTimer? _cooldownTickTimer;

        private readonly Border _speechBubble;
        private readonly ScrollViewer _speechScroller;
        private readonly Grid _chatHistoryView;
        private readonly ScrollViewer _chatHistoryScroller;
        private readonly Border _aiBadge;
        private readonly TextBlock _txtChatHistoryEmpty;
        private readonly ItemsControl _chatHistoryList;
        private readonly Border _inputPanel;
        private readonly TextBox _txtUserInput;
        private readonly Button _btnSendChat;
        private readonly Grid _possessionGlitchLayer;
        private readonly Border _possessionNoteCard;
        private readonly TextBlock _txtPossessionNote;

        /// <summary>Render-proof constructor: no parent window, and the states a reviewer cannot
        /// otherwise see (chat log, input panel, candle, Takeover bar) turned on with
        /// sample data. <c>internal</c> so no production caller can ship the sample.</summary>
        internal AvatarTubeWindow() : this(null)
        {
            ChatHistory.Add(new ChatMessage { Text = "hi bambi, are you there?", IsUser = true });
            ChatHistory.Add(new ChatMessage { Text = "always, sweetie. i've been waiting for you to say something.", IsUser = false });
            ChatHistory.Add(new ChatMessage { Text = "what should we do tonight?", IsUser = true });

            ShowChatHistory();
            _inputPanel.IsVisible = true;
            _txtUserInput.Text = "type something…";
            this.FindControl<Grid>("FuseCandleHost")!.IsVisible = true;
            this.FindControl<Border>("TakeoverCountdownBar")!.IsVisible = true;
            // Note: the AI and POLICY badges are single-message-mode only (ShowChatHistory hides the
            // AI one, exactly as the WPF original does), so they are not in this frame. Both were
            // rendered separately during the port to confirm they draw with real strings.
        }

        public AvatarTubeWindow(Window? parentWindow)
        {
            AvaloniaXamlLoader.Load(this);

            _parentWindow = parentWindow;
            // Don't set Owner - in WPF it caused black window artifacts during minimize, and the
            // z-order pairing is done natively instead (see OnOpened).

            _speechBubble = this.FindControl<Border>("SpeechBubble")!;
            _speechScroller = this.FindControl<ScrollViewer>("SpeechScroller")!;
            _chatHistoryView = this.FindControl<Grid>("ChatHistoryView")!;
            _chatHistoryScroller = this.FindControl<ScrollViewer>("ChatHistoryScroller")!;
            _aiBadge = this.FindControl<Border>("AiBadge")!;
            _txtChatHistoryEmpty = this.FindControl<TextBlock>("TxtChatHistoryEmpty")!;
            _chatHistoryList = this.FindControl<ItemsControl>("ChatHistoryList")!;
            _inputPanel = this.FindControl<Border>("InputPanel")!;
            _txtUserInput = this.FindControl<TextBox>("TxtUserInput")!;
            _btnSendChat = this.FindControl<Button>("BtnSendChat")!;
            _possessionGlitchLayer = this.FindControl<Grid>("PossessionGlitchLayer")!;
            _possessionNoteCard = this.FindControl<Border>("PossessionNoteCard")!;
            _txtPossessionNote = this.FindControl<TextBlock>("TxtPossessionNote")!;

            // Bind chat history list to the rolling collection of conversational messages.
            _chatHistoryList.ItemsSource = ChatHistory;

            // Esc closes chat history mode if open. (WPF: PreviewKeyDown; Avalonia tunnels with
            // AddHandler(..., RoutingStrategies.Tunnel), but a window-level KeyDown is enough here.)
            KeyDown += OnWindowKeyDown;

            // Handlers the WPF XAML wired inline. Every one either only touches this view or is a
            // stub below; none reaches a service.
            _speechBubble.PointerEntered += (_, _) => _isMouseOverSpeechBubble = true;
            _speechBubble.PointerExited += (_, _) => _isMouseOverSpeechBubble = false;
            this.FindControl<Button>("BtnCloseChatHistory")!.Click += (_, _) => HideChatHistory();
            _btnSendChat.Click += (_, _) => SendChat();
            _txtUserInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) SendChat(); };

            var avatarBorder = this.FindControl<Border>("AvatarBorder")!;
            avatarBorder.PointerPressed += OnAvatarPointerPressed;
            this.FindControl<Border>("BtnPrevAvatar")!.PointerPressed += (_, _) => SelectAvatarSet(-1);
            this.FindControl<Border>("BtnNextAvatar")!.PointerPressed += (_, _) => SelectAvatarSet(+1);
            this.FindControl<ContextMenu>("AvatarContextMenu")!.Opened += (_, _) => UpdateQuickMenuState();

            // Setup pose switching timer (only for static avatars). Created, never started: the
            // poses it would cycle load in AvatarTubeWindow.Avatar.cs.
            _poseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _poseTimer.Tick += (_, _) => AdvancePose();

            // ponytail: needs App.Settings / App.Mods / App.Companion and AvatarTubeWindow.Avatar.cs.
            // WPF read PlayerLevel here, picked the avatar set, loaded the poses (static or animated),
            // applied the per-set transform, entered emotive-portrait or Circe-emote mode, refreshed
            // the tube art from the active mod and reloaded the mod's video links. None of that is in
            // Core, so the avatar layers render empty and the title box keeps its XAML defaults.

            // ponytail: needs the ~14 App.* services the WPF constructor subscribed to (Video,
            // BubbleCount, Flash, Subliminal, Bubbles, Achievements, Progression, Companion,
            // WindowAwareness, MindWipe, BrainDrain, ModerationCounter, Mods, MainWindow.EngineStopped),
            // wired when they move to Core. Their handlers all live in the Reactions/Speech partials.

            // ponytail: the WPF ctor also started four timers here - a 2s greeting, the idle-giggle,
            // the trigger and the random-bubble loops. Deliberately NOT started: every one of them
            // calls into a partial this layer does not have, and --render-all constructs ~180 windows
            // in one process, where a stray timer firing at a closed window is a flaky failure.

            Log.Information("AvatarTubeWindow initialized with avatar set {Set}", _currentAvatarSet);
        }

        private bool _isMouseOverSpeechBubble;

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Ensure NOT topmost when attached (starts attached). Window.Topmost is Avalonia's
            // _NET_WM_STATE_ABOVE, which is the correct replacement for HWND_TOPMOST.
            Topmost = false;

            // The z-order pairing the WPF head got from native (GWL_HWNDPARENT) ownership. Safe to
            // call unconditionally - the shim returns false off X11 and on the headless render.
            if (_parentWindow is not null)
                X11Overlay.RestackAbove(this, _parentWindow);

            // ponytail: needs AvatarTubeWindow.Windowing.cs. WPF's OnLoaded also ran
            // CalculateScaleFactor / UpdatePosition / StartFloatingAnimation /
            // ApplySpeechBubblePlacement / RestoreSavedPlacement / StartFullscreenDetection and
            // InitTakeoverCountdownBar. Screens.ScreenFromPoint + screen.WorkingArea/Scaling are the
            // replacements for GetDpiForMonitor / MonitorFromPoint / SystemParameters.WorkArea when
            // that partial ports; nothing here needs them yet.
        }

        // =========================================================================================
        //  Thread marshalling. WPF's own-thread mode (AppSettings.AvatarOwnThread) has no Avalonia
        //  twin - one UI thread per process - so these all target Dispatcher.UIThread.
        // =========================================================================================

        internal void RunOnAvatar(Action action, DispatcherPriority priority = default)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action, priority);
        }

        public void ShowSafe() => RunOnAvatar(() =>
        {
            try { Show(); } catch (Exception ex) { Log.Debug("AvatarTube ShowSafe failed: {Error}", ex.Message); }
        });

        public void CloseSafe() => RunOnAvatar(() =>
        {
            try { Close(); } catch (Exception ex) { Log.Debug("AvatarTube CloseSafe failed: {Error}", ex.Message); }
        });

        // =========================================================================================
        //  Chat history view
        // =========================================================================================

        /// <summary>Swap the bubble into chat-log mode. Ported from ChatInput.cs's ShowChatHistory
        /// (the only part of that partial the XAML's own controls need).</summary>
        public void ShowChatHistory()
        {
            _isShowingChatHistory = true;

            // Show empty-state hint when there are no captured messages yet.
            _txtChatHistoryEmpty.IsVisible = ChatHistory.Count == 0;

            // Swap bubble content: hide single-message view, show chat history.
            _speechScroller.IsVisible = false;
            _chatHistoryView.IsVisible = true;
            // Hide the per-message AI badge when showing the chat history list (mixed AI + user lines).
            _aiBadge.IsVisible = false;

            // Enlarge bubble for the chat history layout.
            // ponytail: WPF followed this with ApplySpeechBubblePlacement(), which recomputes the
            // margin from MaxWidth so a 600px bubble does not hang off the canvas. That method is in
            // AvatarTubeWindow.Windowing.cs, so the bubble keeps its XAML margin here.
            _speechBubble.MaxWidth = 600;
            _speechBubble.IsVisible = true;

            // Auto-scroll to most recent message.
            Dispatcher.UIThread.Post(() => _chatHistoryScroller.ScrollToEnd(), DispatcherPriority.Background);
        }

        /// <summary>Back to single-message mode, and take the bubble down with it.</summary>
        public void HideChatHistory()
        {
            _isShowingChatHistory = false;
            _chatHistoryView.IsVisible = false;
            _speechScroller.IsVisible = true;
            _speechBubble.MaxWidth = 380; // Restore default bubble width.
            _speechBubble.IsVisible = false;
        }

        private void AddToChatHistory(string text, bool isUser)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ChatHistory.Add(new ChatMessage { Text = text, IsUser = isUser });
            while (ChatHistory.Count > MaxChatHistorySize) ChatHistory.RemoveAt(0);
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isShowingChatHistory)
            {
                HideChatHistory();
                e.Handled = true;
            }
        }

        // =========================================================================================
        //  Moderation cooldown (P1.4). Fully portable: it only enables/disables two controls and
        //  paints a countdown into the input box.
        // =========================================================================================

        private void OnCooldownStarted(DateTime endsAt)
        {
            _cooldownEndsAt = endsAt;
            try
            {
                _txtUserInput.IsEnabled = false;
                _txtUserInput.Opacity = 0.5;
                _txtUserInput.Text = string.Empty;
                _btnSendChat.IsEnabled = false;
                _btnSendChat.Opacity = 0.5;

                _cooldownTickTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _cooldownTickTimer.Tick -= CooldownTick;
                _cooldownTickTimer.Tick += CooldownTick;
                _cooldownTickTimer.Start();
                CooldownTick(null, EventArgs.Empty); // initial paint
                Log.Information("AvatarTubeWindow: chat cooldown engaged until {End}", endsAt);
            }
            catch (Exception ex) { Log.Warning(ex, "AvatarTubeWindow: OnCooldownStarted failed"); }
        }

        private void CooldownTick(object? sender, EventArgs e)
        {
            if (!_cooldownEndsAt.HasValue) { _cooldownTickTimer?.Stop(); return; }
            var remaining = _cooldownEndsAt.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                // ponytail: needs App.ModerationCounter - WPF probed GetState() here so the counter
                // itself raised CooldownEnded. Without it, end the cooldown locally.
                OnCooldownEnded();
                return;
            }
            try
            {
                _txtUserInput.Text = Loc.GetF("chat_cooldown_active", (int)Math.Ceiling(remaining.TotalSeconds));
            }
            catch { /* best-effort painter */ }
        }

        private void OnCooldownEnded()
        {
            _cooldownEndsAt = null;
            _cooldownTickTimer?.Stop();
            try
            {
                _txtUserInput.IsEnabled = true;
                _txtUserInput.Opacity = 1.0;
                _txtUserInput.Text = string.Empty;
                _btnSendChat.IsEnabled = true;
                _btnSendChat.Opacity = 1.0;
                Log.Information("AvatarTubeWindow: chat cooldown ended");
            }
            catch (Exception ex) { Log.Warning(ex, "AvatarTubeWindow: OnCooldownEnded failed"); }
        }

        // =========================================================================================
        //  Pose animation
        // =========================================================================================

        public void StartPoseAnimation() => RunOnAvatar(() => _poseTimer.Start());
        public void StopPoseAnimation() => RunOnAvatar(() => _poseTimer.Stop());
        public void SetPoseInterval(TimeSpan interval) => _poseTimer.Interval = interval;

        public void SetPose(int poseNumber) => RunOnAvatar(() =>
        {
            if (poseNumber < 1 || poseNumber > 4) return;
            _currentPoseIndex = poseNumber - 1;
            // ponytail: needs _avatarPoses from AvatarTubeWindow.Avatar.cs (LoadAvatarPoses reads
            // the active mod's PNG set). Without it there is no Bitmap to assign to ImgAvatar.
        });

        private void AdvancePose()
        {
            // ponytail: needs AvatarTubeWindow.Avatar.cs's PoseTimer_Tick, which also drives the
            // talking/idle pose choice off the speech state.
            _currentPoseIndex = (_currentPoseIndex + 1) % 4;
        }

        /// <summary>Gets the current avatar set number</summary>
        public int CurrentAvatarSet => _currentAvatarSet;

        /// <summary>
        /// True while ANY speech bubble (AI or ordinary "Preset" bark/chatter) is currently being
        /// displayed. Unlike <see cref="HasBubbleUp"/> this also covers non-AI bubbles, so the bark
        /// system can avoid stacking ordinary barks behind one that's already on screen.
        /// </summary>
        public bool IsSpeaking => _isGiggling;

        /// <summary>
        /// True while ANYTHING is in the tube's bubble slot: a spoken line, the listening dots, the
        /// chat history view or an uninterruptible recorded clip. Broader than
        /// <see cref="IsSpeaking"/> on purpose - EMI Desk asks this (via
        /// <c>EmiDeskService.TubeBubbleLive</c>) to decide whether the tube is visually busy, and
        /// the listening indicator counts as busy even though it is not speech.
        /// </summary>
        internal bool HasBubbleUp =>
            _isGiggling || _isListeningBubble || _isShowingChatHistory
            || _isShowingAiBubble || _isPlayingUninterruptibleClip;

        // =========================================================================================
        //  POSSESSION - the two things the haunted-UI layer is allowed to do to the tube.
        //  Read Services/Possession/POSSESSION.md ("Companion wave") first.
        //
        //  Both are deliberately dumb: they draw into their OWN overlay elements and never touch
        //  ImgAvatar / ImgAvatarB / the animated pair, the pose timer, the emote crossfade or the
        //  portrait float loop. The haunt must never be able to leave the companion looking wrong
        //  after a lockdown ends, and the only way to guarantee that is to never write to the real
        //  pipeline in the first place.
        // =========================================================================================

        /// <summary>Possession ember (#FF8A5C). Ember means Possession, only - crimson is the theme.</summary>
        private static readonly IBrush PossessionEmberBrush =
            new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x5C));

        private DispatcherTimer? _possessionGlitchTimer;

        /// <summary>
        /// R4 "glitchportrait": for <paramref name="ms"/> milliseconds the portrait tears into three
        /// horizontal bands that slip a few pixels sideways, ember-tinted, then snaps back.
        ///
        /// <para>The copies live in PossessionGlitchLayer, a sibling of the avatar images inside
        /// AvatarBounceHost, so each one inherits the SAME layout slot as the real portrait and lines
        /// up with it without any measuring. The bands are clipped before they are translated
        /// (Clip is applied in local space, ahead of RenderTransform), which is what makes them read
        /// as a tear rather than three ghosts.</para>
        ///
        /// <para>No-op when no portrait is loaded, when the tube has not laid out yet, or when the
        /// window is on its way down. Safe to call again while one is still running.</para>
        /// </summary>
        public void GlitchPortrait(int ms) => RunOnAvatar(() =>
        {
            try
            {
                var layer = _possessionGlitchLayer;
                if (layer == null) return;

                var src = CurrentPortraitImage();
                var source = src?.Source;
                if (src == null || source == null) return;

                double w = src.Bounds.Width, h = src.Bounds.Height;
                if (w < 8 || h < 8) return;

                ClearGlitchPortrait();

                const int slices = 3;
                double band = h / slices;
                for (int i = 0; i < slices; i++)
                {
                    // Alternating, so the tear reads as a horizontal SLIP and not as a lean.
                    double dx = (i % 2 == 0) ? 6 : -6;
                    double dy = (i % 2 == 0) ? -1 : 1;

                    var cell = new Grid
                    {
                        Width = w,
                        Height = h,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false,
                        Clip = new RectangleGeometry(new Rect(0, i * band, w, band)),
                        RenderTransform = new TranslateTransform(dx, dy)
                    };
                    cell.Children.Add(new Image
                    {
                        Source = source,
                        Stretch = src.Stretch,
                        Width = w,
                        Height = h,
                        IsHitTestVisible = false
                    });
                    cell.Children.Add(new Rectangle
                    {
                        Fill = PossessionEmberBrush,
                        Opacity = 0.20,
                        Width = w,
                        Height = h,
                        IsHitTestVisible = false
                    });
                    layer.Children.Add(cell);
                }

                layer.IsVisible = true;

                _possessionGlitchTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Clamp(ms, 60, 1000))
                };
                _possessionGlitchTimer.Tick += (_, _) => ClearGlitchPortrait();
                _possessionGlitchTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Debug("AvatarTube GlitchPortrait failed: {Error}", ex.Message);
                ClearGlitchPortrait();
            }
        });

        /// <summary>Take the glitch down. Safe any number of times, from any thread.</summary>
        public void ClearGlitchPortrait() => RunOnAvatar(() =>
        {
            try
            {
                if (_possessionGlitchTimer != null)
                {
                    _possessionGlitchTimer.Stop();
                    _possessionGlitchTimer = null;
                }
                var layer = _possessionGlitchLayer;
                if (layer == null) return;
                layer.Children.Clear();
                layer.IsVisible = false;
            }
            catch (Exception ex) { Log.Debug("AvatarTube ClearGlitchPortrait failed: {Error}", ex.Message); }
        });

        /// <summary>
        /// Whichever avatar Image is actually on screen right now, topmost first: the two animated
        /// layers (Circe webp / GIF sets), then the emotive-portrait crossfade layer, then the plain
        /// pose image. Opacity is checked as well as visibility because the crossfade layers stay
        /// visible at Opacity 0 between emotes.
        /// </summary>
        private Image? CurrentPortraitImage()
        {
            foreach (var name in new[] { "ImgAvatarAnimatedB", "ImgAvatarAnimated", "ImgAvatarB", "ImgAvatar" })
            {
                try
                {
                    var img = this.FindControl<Image>(name);
                    if (img == null) continue;
                    if (!img.IsVisible) continue;
                    if (img.Opacity < 0.5) continue;
                    if (img.Source == null) continue;
                    return img;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// R4 "leave": the warden puts a note where she was standing. Idempotent - calling it twice
        /// just rewrites the card - and a no-op when the card never made it into the tree.
        /// </summary>
        public void ShowPossessionNote(string text) => RunOnAvatar(() =>
        {
            try
            {
                var card = _possessionNoteCard;
                if (card == null || string.IsNullOrWhiteSpace(text)) return;

                _txtPossessionNote.Text = text;
                // A small random lean, so it reads as something that was PUT there.
                if (card.RenderTransform is RotateTransform rot) rot.Angle = _random.Next(2) == 0 ? -3 : 3;

                // ponytail: WPF ran a 260ms cubic-ease opacity Storyboard here. Avalonia's twin is a
                // Transitions entry, which would also animate the hide below; the note is set
                // straight to full instead, which is the same end state without a second code path.
                card.Opacity = 1;
                card.IsVisible = true;
            }
            catch (Exception ex) { Log.Debug("AvatarTube ShowPossessionNote failed: {Error}", ex.Message); }
        });

        /// <summary>Take the note down. Safe when there never was one, and safe to call twice.</summary>
        public void HidePossessionNote() => RunOnAvatar(() =>
        {
            try
            {
                var card = _possessionNoteCard;
                if (card == null || !card.IsVisible) return;
                card.Opacity = 0;
                card.IsVisible = false;
            }
            catch (Exception ex) { Log.Debug("AvatarTube HidePossessionNote failed: {Error}", ex.Message); }
        });

        // =========================================================================================
        //  Stubs. Each one is a handler the WPF XAML wired inline whose body lives in a partial
        //  this layer does not port, or reaches a service that is not in Core.
        // =========================================================================================

        /// <summary>Left click bounces her and fires a reaction bark; right click opens the menu.</summary>
        private void OnAvatarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) return; // Avalonia opens the ContextMenu itself
            // ponytail: needs AvatarTubeWindow.Avatar.cs (BounceAvatar) and Reactions.cs
            // (ImgAvatar_MouseLeftButtonDown -> click bark + pose change), wired when they move to Core.
        }

        /// <summary>Send whatever is in the input box to the companion.</summary>
        private void SendChat()
        {
            var input = _txtUserInput.Text;
            if (string.IsNullOrWhiteSpace(input)) return;
            AddToChatHistory(input, isUser: true);
            _txtUserInput.Text = string.Empty;
            // ponytail: needs AvatarTubeWindow.ChatInput.cs (moderation guard, AI inference,
            // typewriter reply, TTS) and App.ModerationCounter, wired when they move to Core. The
            // user's line still lands in the log so the view is honest about what it did.
        }

        /// <summary>Step through the unlocked avatar sets with the title-box arrows.</summary>
        private void SelectAvatarSet(int delta)
        {
            // ponytail: needs App.Settings (SelectedAvatarSet) and AvatarTubeWindow.Avatar.cs
            // (GetUnlockedAvatarSets / LoadAvatarPoses / ApplyAvatarTransform / UpdateTitleDisplay).
        }

        /// <summary>Refresh the context menu's checkmarks and the remote-emote item swap.</summary>
        private void UpdateQuickMenuState()
        {
            // ponytail: needs App.Settings.Current.RemoteEmotePresets, App.Engine, App.Companion and
            // AvatarTubeWindow.Reactions.cs's AvatarContextMenu_Opened, which retitles every item
            // from live state on each open.
        }
    }
}
