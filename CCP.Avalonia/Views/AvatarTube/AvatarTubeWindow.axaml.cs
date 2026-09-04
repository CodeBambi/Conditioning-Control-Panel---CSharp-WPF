using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
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
    /// <para><b>Members from the partials that DID cross</b>, because other ported files ask for
    /// them by name and each is self-contained: the chat-shortcut trio
    /// (<see cref="FormatChatShortcut"/>, <see cref="SerializeModifiers"/>,
    /// <see cref="ApplyChatShortcutTo"/>, from ChatInput.cs - the setting they read is in Core and
    /// Avalonia's <c>KeyBindings</c> replaces WPF's <c>InputBindings</c>);
    /// <see cref="RefreshTubeLayout"/> with its margin maths (from Avatar.cs + Speech.cs, which
    /// TubeFitDialog hot-refreshes the live tube with); the art chain (<see cref="SetTubeStyle"/>,
    /// <see cref="LoadAvatarPoses"/>, <see cref="ApplyAvatarTransform"/>,
    /// <see cref="UpdateTitleDisplay"/>, from Avatar.cs + Windowing.cs); and
    /// <see cref="GigglePriority"/> (Speech.cs), which <b>does</b> speak now that
    /// <c>CoreAudio.PlayOneShot</c> exists - see its own remarks for the four things it drops.</para>
    ///
    /// <para><b>ponytail: the eight partials still did not come.</b> This layer ports the .xaml.cs
    /// plus the members named above. The 60fps float loop (Avatar.cs), the speech QUEUE and the bark
    /// engine (Speech.cs), chat and the AI pipeline (ChatInput.cs), Circe emotes (CirceEmotes.cs),
    /// reactions (Reactions.cs), the candle (DescentFuse.cs) and ALL of the
    /// attach/detach/scale/position/fullscreen windowing (Windowing.cs) stay in the WPF head.
    /// The whole <c>App.*</c> service-subscription block (Video, BubbleCount, Flash, Subliminal,
    /// Bubbles, Achievements, Progression, Companion, WindowAwareness, MindWipe, BrainDrain,
    /// ModerationCounter, Mods) is still one stub. Twelve of those have no seam at all; the two
    /// that do are no help to it - <c>CoreMods</c> raises ModChanged but not the avatar events
    /// these handlers wanted, and <c>CoreProgression</c> is write-only (AddXP, no level event).</para>
    ///
    /// <para><b>The tube does not track a face.</b> Nothing here reads a camera. The webcam face
    /// tracker that drives the avatar's gaze on the WPF head is a device, so it stays in a head;
    /// this window renders art and speaks, and that is all it claims.</para>
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

        // Pose cycling (static avatars only). Four PNGs per set; a set with fewer than two that
        // actually loaded never starts the timer, which is the WPF rule verbatim.
        private readonly DispatcherTimer _poseTimer;
        private int _currentPoseIndex;
        private readonly int _currentAvatarSet = Math.Max(1, CoreSettings.Current.SelectedAvatarSet);
        private Bitmap?[] _avatarPoses = new Bitmap?[4];

        // The bubble's auto-hide. One timer, replaced per bubble; the hover hold re-arms it at 1s.
        private DispatcherTimer? _speechTimer;

        /// <summary>When the last GENUINE AI reply went up. The bark system's chat-suppression
        /// window reads this (WPF: IsCompanionBusy); bark output passes aiGenerated:false and so
        /// deliberately does not move it.</summary>
        private DateTime _lastAiBubbleUtc = DateTime.MinValue;

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
        private readonly TextBlock _txtSpeech;
        private readonly Border _policyBadge;
        private readonly Image _imgTubeFrame;
        private readonly Image _imgAvatar;
        private readonly Border _avatarBorder;
        private readonly TextBlock _txtAvatarTitle;
        private readonly TextBlock _txtAvatarLevel;

        /// <summary>Render-proof constructor: no parent window, and the states a reviewer cannot
        /// otherwise see (chat log, input panel, candle, Takeover bar) turned on with
        /// sample data. <c>internal</c> so no production caller can ship the sample.</summary>
        internal AvatarTubeWindow() : this(null)
        {
            ChatHistory.Add(new ChatMessage { Text = "hi bambi, are you there?", IsUser = true });
            ChatHistory.Add(new ChatMessage { Text = "always, sweetie. i've been waiting for you to say something.", IsUser = false });
            ChatHistory.Add(new ChatMessage { Text = "what should we do tonight?", IsUser = true });

            _inputPanel.IsVisible = true;
            _txtUserInput.Text = "type something…";
            this.FindControl<Grid>("FuseCandleHost")!.IsVisible = true;
            this.FindControl<Border>("TakeoverCountdownBar")!.IsVisible = true;
            // Runs the layout maths in the render (OnOpened never fires headless), so the frame
            // proves ApplyTubeLayoutOffsets + ApplySpeechBubblePlacement execute and land on the
            // XAML defaults rather than only that they compile.
            RefreshTubeLayout();
            // A real spoken line, so the frame carries the glass, the avatar, the caption AND the
            // priority bubble with its AI badge. The bubble and the chat log share one slot, so
            // showing this one hides the log; the log's own frame is the earlier layer's PNG.
            GigglePriority("always, sweetie. i've been waiting for you to say something.",
                           playSound: false, aiGenerated: true);
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
            _txtSpeech = this.FindControl<TextBlock>("TxtSpeech")!;
            _policyBadge = this.FindControl<Border>("PolicyBadge")!;
            _imgTubeFrame = this.FindControl<Image>("ImgTubeFrame")!;
            _imgAvatar = this.FindControl<Image>("ImgAvatar")!;
            _avatarBorder = this.FindControl<Border>("AvatarBorder")!;
            _txtAvatarTitle = this.FindControl<TextBlock>("TxtAvatarTitle")!;
            _txtAvatarLevel = this.FindControl<TextBlock>("TxtAvatarLevel")!;

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
            // The quick menu's own way into the log - WPF's MenuItemShowChatHistory_Click. Looked up
            // without the null-forgiving `!` because this is the first CHILD of the ContextMenu the
            // ctor reaches, and a name-scope miss inside a menu would throw here and drop the whole
            // window out of --render-all rather than lose one menu item.
            var showLog = this.FindControl<MenuItem>("MenuItemShowChatHistory");
            if (showLog != null) showLog.Click += (_, _) => ShowChatHistory();
            else Log.Warning("AvatarTubeWindow: MenuItemShowChatHistory not found; chat log unreachable from the menu");
            _btnSendChat.Click += (_, _) => SendChat();
            _txtUserInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) SendChat(); };

            _avatarBorder.PointerPressed += OnAvatarPointerPressed;
            this.FindControl<Border>("BtnPrevAvatar")!.PointerPressed += (_, _) => SelectAvatarSet(-1);
            this.FindControl<Border>("BtnNextAvatar")!.PointerPressed += (_, _) => SelectAvatarSet(+1);
            this.FindControl<ContextMenu>("AvatarContextMenu")!.Opened += (_, _) => UpdateQuickMenuState();

            // Pose switching for static avatars. ApplyAvatarSet below starts it only when more than
            // one pose actually loaded - a set that ships one PNG has nothing to rotate between.
            _poseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _poseTimer.Tick += (_, _) => AdvancePose();

            // The art. WPF did all of this in its constructor off App.Settings / App.Mods; every
            // read it needs is in Core now (CoreSettings for the chosen set, CoreModArt for a mod's
            // replacement PNG, CoreMods for the persona name), so the tube draws its glass, its
            // avatar and its caption instead of rendering as an empty frame.
            SetTubeStyle(!_isAttached);
            ApplyAvatarSet();
            // The caption is Loc-driven and set from code (a persona name has no static key), so it
            // has to be re-run rather than bound - see the porting note about {loc:Str} and .Text.
            LocalizationManager.Instance.LanguageChanged += OnTubeLanguageChanged;

            // ponytail: still needs AvatarTubeWindow.Avatar.cs for the ANIMATED avatar (level 20+
            // GIF sets, which need an Avalonia GIF decoder) and the emotive-portrait crossfade. The
            // set ARROWS are blocked on the companion coupling, NOT on the set list - see
            // SelectAvatarSet. The static four-pose path below is the one every set-1 user is on.

            // ponytail: the ~14 App.* services the WPF constructor subscribed to (Video, BubbleCount,
            // Flash, Subliminal, Bubbles, Achievements, Progression, Companion, WindowAwareness,
            // MindWipe, BrainDrain, ModerationCounter, Mods, MainWindow.EngineStopped). Two of those
            // names DO have seams and still do not help: CoreMods raises ModChanged but none of the
            // avatar-set handlers hang off it, and CoreProgression is write-only (AddXP, no level
            // event), so the caption cannot re-read itself when she levels. The other twelve are
            // head-side services in ConditioningControlPanel/Services/ with no seam at all, and
            // their handlers live in the Reactions/Speech partials that did not port.

            // ponytail: the WPF ctor also started four timers here - a 2s greeting, the idle-giggle,
            // the trigger and the random-bubble loops. Deliberately NOT started: every one of them
            // calls into a partial this layer does not have, and --render-all constructs ~180 windows
            // in one process, where a stray timer firing at a closed window is a flaky failure.

            // WPF did this from Loaded on this window; same here. See ApplyChatShortcutTo for why
            // the binding on THIS window is the lesser half.
            Loaded += (_, _) => ApplyChatShortcutTo(this);

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

            // The live tube owns the static chat command for as long as it is open. WPF routed the
            // RoutedUICommand up the tree to whichever window handled it; there is no routed-command
            // tree here, so the command holds one sink and the open tube is it.
            OpenChatSink = OpenChatInput;

            // WPF's OnLoaded ran this as part of the layout pass; it is idempotent and it is what
            // puts the avatar, title, input panel, Takeover bar and speech bubble on the mod's
            // chamber rather than on the stock one.
            RefreshTubeLayout();

            // ponytail: needs AvatarTubeWindow.Windowing.cs. WPF's OnLoaded also ran
            // CalculateScaleFactor / UpdatePosition / StartFloatingAnimation / RestoreSavedPlacement
            // / StartFullscreenDetection and InitTakeoverCountdownBar. Screens.ScreenFromPoint +
            // screen.WorkingArea/Scaling are the replacements for GetDpiForMonitor /
            // MonitorFromPoint / SystemParameters.WorkArea when that partial ports.
        }

        protected override void OnClosed(EventArgs e)
        {
            // Never leave the static command pointing at a closed window. Delegate == compares
            // target and method, which is what makes "is the sink still MINE?" answerable at all;
            // ReferenceEquals would be false every time because the conversion allocates.
            if (OpenChatSink == (Action)OpenChatInput) OpenChatSink = null;

            // Every timer this window starts is stopped here. --render-all constructs ~180 windows
            // in one process, and a tick against a torn-down visual tree is exactly the flaky
            // failure the constructor's note refuses to risk.
            LocalizationManager.Instance.LanguageChanged -= OnTubeLanguageChanged;
            _poseTimer.Stop();
            _speechTimer?.Stop();
            _cooldownTickTimer?.Stop();
            _possessionGlitchTimer?.Stop();
            base.OnClosed(e);
        }

        // =========================================================================================
        //  Locating the live tube. WPF's App.AvatarWindow twin: the WPF head keeps a field on its
        //  Application subclass, which is exactly the static service locator Core is not allowed to
        //  have, so this head asks the window list instead.
        // =========================================================================================

        /// <summary>
        /// The tube that is currently OPEN, or null. Avalonia's desktop lifetime populates
        /// <c>Windows</c> on <c>Show()</c> and drops the entry on close, so this cannot go stale the
        /// way a hand-maintained static field can - there is no lifecycle bookkeeping to get wrong,
        /// and a constructed-but-never-shown tube (the <c>--render-view</c> path) is correctly not
        /// "live". Null on a headless render and before the shell builds one, which every caller
        /// treats as "there is nothing to refresh".
        /// </summary>
        public static AvatarTubeWindow? Live =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.Windows.OfType<AvatarTubeWindow>().FirstOrDefault();

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
            // The log TAKES OVER the bubble, so the running line's auto-hide has to be cancelled
            // first: that Tick sets IsVisible = false unconditionally and would close the log under
            // the user a few seconds after they opened it. WPF's EnterChatHistoryMode kills the
            // same two timers for the same reason.
            _speechTimer?.Stop();
            _isGiggling = false;

            _isShowingChatHistory = true;

            // Show empty-state hint when there are no captured messages yet.
            _txtChatHistoryEmpty.IsVisible = ChatHistory.Count == 0;

            // Swap bubble content: hide single-message view, show chat history.
            _speechScroller.IsVisible = false;
            _chatHistoryView.IsVisible = true;
            // Hide the per-message AI badge when showing the chat history list (mixed AI + user lines).
            _aiBadge.IsVisible = false;

            // Enlarge the bubble for the chat-history layout and RE-PLACE it: the attached margin
            // is derived from MaxWidth (a 600px bubble gives the seam up rather than hang off the
            // canvas), so widening without recomputing lands the panel clipped. The old note here
            // blamed Windowing.cs; ApplySpeechBubblePlacement came across with RefreshTubeLayout.
            _speechBubble.MaxWidth = 600;
            ApplySpeechBubblePlacement();
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
                // ponytail: the CLASS is not the blocker - ModerationCounter and its
                // CooldownStarted / CooldownEnded events are in
                // CCP.Core/Services/Moderation/ModerationCounter.cs already. What is missing is the
                // app's SINGLE instance (App.ModerationCounter, built in App.xaml.cs) and a seam
                // for it: a second instance here would keep a second sliding window and split the
                // persisted moderation-counter.json, letting a cooldown be dodged by opening the
                // tube - which is exactly why CoreModerationLog exists rather than a `new`. So the
                // cooldown is ended locally instead of by probing GetState().
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
            ShowCurrentPose();
        });

        /// <summary>
        /// Next pose that actually loaded. WPF walked the four slots blindly and let a null Source
        /// blank the avatar for a beat; skipping the holes keeps a set that ships two PNGs cycling
        /// between those two instead of flickering to empty every other tick.
        /// <para>ponytail: WPF's PoseTimer_Tick also picks a TALKING pose while a bubble is up.
        /// That lives in Avatar.cs with the pose-to-mouth mapping and did not port.</para>
        /// </summary>
        private void AdvancePose()
        {
            for (int i = 1; i <= _avatarPoses.Length; i++)
            {
                int next = (_currentPoseIndex + i) % _avatarPoses.Length;
                if (_avatarPoses[next] == null) continue;
                _currentPoseIndex = next;
                ShowCurrentPose();
                return;
            }
        }

        private void ShowCurrentPose()
        {
            var pose = _avatarPoses[Math.Clamp(_currentPoseIndex, 0, _avatarPoses.Length - 1)];
            if (pose != null) _imgAvatar.Source = pose;
        }

        /// <summary>Gets the current avatar set number</summary>
        public int CurrentAvatarSet => _currentAvatarSet;

        /// <summary>
        /// True while ANY speech bubble (AI or ordinary "Preset" bark/chatter) is currently being
        /// displayed. Unlike <see cref="HasBubbleUp"/> this also covers non-AI bubbles, so the bark
        /// system can avoid stacking ordinary barks behind one that's already on screen.
        /// <para>Written by <see cref="GigglePriority"/> and cleared by the auto-hide, so it is a
        /// live answer now rather than a permanent false.</para>
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
        //  Tube layout. PORTED from AvatarTubeWindow.Avatar.cs (RefreshTubeLayout /
        //  ApplyTubeLayoutOffsets), Speech.cs (ApplySpeechBubblePlacement) and the constants in
        //  Windowing.cs. TubeFitDialog calls RefreshTubeLayout to hot-refresh the live tube.
        // =========================================================================================

        /// <summary>Design reference width the XAML is drawn at.</summary>
        private const double DesignWidth = 780;

        /// <summary>Transparent right margin of the tube frame, MEASURED off tube.png's alpha
        /// bounds - see the derivation in AvatarTubeWindow.Windowing.cs. Everything right of it is
        /// alpha-0, i.e. click-through, which is why the tube's RECT may overlap main's rail.</summary>
        private const double TubeArtRightPadding = 353;

        /// <summary>Canvas px of OPAQUE art allowed over main's left edge. 0 = flush against the
        /// door rail; 60 is the ceiling. The seam is a HIT-TEST budget, not just a look.</summary>
        private const double SeamOverlapOverMain = 0;

        /// <summary>12px of daylight so a short bubble does not read as glued to main's frame.</summary>
        private const double AttachedBubbleSeamGap = 12;

        private const double AttachedBubbleRightMargin =
            TubeArtRightPadding - SeamOverlapOverMain + AttachedBubbleSeamGap;

        /// <summary>Attached = riding beside main. Seeded from the state the user left the tube
        /// in, which is what makes a detached user's layout and glass come back detached.
        /// ponytail: the attach/detach GESTURE is Windowing.cs and did not port, so the tube stays
        /// in the state it starts in - it just no longer always starts attached.</summary>
        private bool _isAttached = !CoreSettings.Current.AvatarTubeDetached;

        /// <summary>
        /// Re-applies the tube layout after the user edits it (Mod Manager -> Tube Fit). Public so
        /// the dialog can hot-refresh the live tube without a mod reload. Self-marshalling: the
        /// dialog's OK handler may not be on the UI thread.
        /// </summary>
        public void RefreshTubeLayout() => RunOnAvatar(() =>
        {
            try { ApplyTubeLayoutOffsets(); }
            catch (Exception ex) { Log.Warning("RefreshTubeLayout failed: {Error}", ex.Message); }
        });

        /// <summary>
        /// Applies the active mod's tube layout offsets to the avatar, title, input panel, Takeover
        /// bar and speech bubble. A mod's tube art may put the glass cylinder somewhere else, so the
        /// offset shifts every element horizontally to line up with the chamber the author drew.
        /// <para>The margins are the WPF ones verbatim, and with the offsets at 0 they are exactly
        /// the XAML defaults - so an unseeded head lays out identically to today.</para>
        /// </summary>
        private void ApplyTubeLayoutOffsets()
        {
            // Deviation: WPF used LayoutTransform, which Avalonia has no per-control equivalent for
            // (its twin is a LayoutTransformControl wrapper). RenderTransform about the feet is the
            // same visual for a bottom-aligned avatar; only the parent's measured size differs, and
            // AvatarBorder's size is margin-driven anyway.
            var scale = EffAvatarScale();
            var transform = Math.Abs(scale - 1.0) > 0.001 ? new ScaleTransform(scale, scale) : null;
            foreach (var name in new[] { "ImgAvatar", "ImgAvatarAnimated", "ImgAvatarAnimatedB" })
            {
                var img = this.FindControl<Image>(name);
                if (img == null) continue;
                img.RenderTransformOrigin = new RelativePoint(0.5, 1.0, RelativeUnit.Relative);
                img.RenderTransform = transform;
            }

            // When the mod only overrides the ATTACHED tube image, force the attached layout in the
            // detached state too - otherwise the avatar lands outside the chamber the mod author
            // drew (bug report #172).
            var useAttachedLayout = _isAttached || ModOverridesAttachedTubeOnly();

            var avatarBorder = this.FindControl<Border>("AvatarBorder");
            var titleBox = this.FindControl<Border>("TitleBox");
            var takeoverBar = this.FindControl<Border>("TakeoverCountdownBar");

            if (useAttachedLayout)
            {
                var dx = EffAvatarOffsetX();
                var dy = EffAvatarOffsetY();
                if (avatarBorder != null) avatarBorder.Margin = new Thickness(5, 100, 126 - dx, 210 + dy);
                if (titleBox != null) titleBox.Margin = new Thickness(0, 0, 121 - dx, 180);
                _inputPanel.Margin = new Thickness(0, 0, 126 - dx, 520);
                if (takeoverBar != null) takeoverBar.Margin = new Thickness(0, 0, 116 - dx, 246);
            }
            else
            {
                var dx = EffAvatarDetachedOffsetX();
                var dy = EffAvatarDetachedOffsetY();
                // Detached nudge: 20px higher and net 5px left (the element is centred, so the
                // offset is (L-R)/2).
                if (avatarBorder != null) avatarBorder.Margin = new Thickness(5, 100, 436 - dx, 228 + dy);
                if (titleBox != null) titleBox.Margin = new Thickness(0, 0, 416 - dx, 193);
                _inputPanel.Margin = new Thickness(0, 0, 426 - dx, 520);
                // Keep the Takeover bar glued to the pod; it is not in the XAML's attached-only
                // default, so without this it floats at the attached spot whenever the tube
                // detaches (#464).
                if (takeoverBar != null) takeoverBar.Margin = new Thickness(0, 0, 416 - dx, 264);
            }

            ApplySpeechBubblePlacement();
        }

        /// <summary>
        /// Places the speech bubble. Attached it is anchored by its RIGHT edge on the seam and grows
        /// leftward, because an OPAQUE pixel right of the tube art is NOT click-through: it swallows
        /// the click and main's door rail goes dead for as long as she is talking (v6.8.6). A bubble
        /// too wide to fit left of the seam gives the seam up rather than hang off the canvas and get
        /// clipped - an unreadable bubble is the worse bug. Detached there is nothing underneath to
        /// protect, so that mode keeps the centred placement it has always had.
        /// </summary>
        private void ApplySpeechBubblePlacement()
        {
            var useAttached = _isAttached || ModOverridesAttachedTubeOnly();
            var dx = useAttached ? EffAvatarOffsetX() : EffAvatarDetachedOffsetX();

            double right;
            if (useAttached)
            {
                // A mod's avatar offset may pull the bubble LEFT with the art it belongs to, never
                // right - the seam is main's, not the mod's.
                right = Math.Max(AttachedBubbleRightMargin, AttachedBubbleRightMargin - dx);

                var maxWidth = _speechBubble.MaxWidth;
                if (double.IsFinite(maxWidth) && maxWidth > 0)
                    right = Math.Min(right, Math.Max(0, DesignWidth - maxWidth));
            }
            else
            {
                right = 425 - dx;
            }

            _speechBubble.HorizontalAlignment = useAttached ? HorizontalAlignment.Right
                                                            : HorizontalAlignment.Center;
            _speechBubble.Margin = new Thickness(0, 0, right, 550);
        }

        /// <summary>
        /// The layout in force for the active mod: the user's Tube Fit override wins, else the mod
        /// manifest's own <c>tubeLayout</c>, else null (all defaults). ModService.EffectiveTubeLayout
        /// verbatim - both halves are in Core, which is what TubeFitDialog already reads, so the
        /// dialog's preview and the live tube can no longer disagree about the same mod.
        /// </summary>
        private static ModTubeLayout? EffectiveTubeLayout()
        {
            var id = CoreMods.ActiveModId;
            if (CoreSettings.Current.TubeLayoutOverridesByMod?.TryGetValue(id, out var user) == true && user != null)
                return user;
            return CoreMods.InstalledMods.TryGetValue(id, out var pkg) ? pkg?.Manifest?.TubeLayout : null;
        }

        // The clamps are ModService.GetAvatar*'s, verbatim: a mod manifest is author-written JSON,
        // so an out-of-range number must be pinned here rather than thrown off the canvas.
        //
        // ponytail: WPF's EffAvatar* (AvatarTubeWindow.CirceEmotes.cs) ADD a running Circe emote's
        // per-clip nudge on top of these. Emote mode needs an Avalonia WebP/GIF decoder and did not
        // port, so the emote term is the neutral one it has when no emote set is animating - which
        // is the state every non-Circe mod is in permanently.
        private static double EffAvatarScale() => Math.Clamp(EffectiveTubeLayout()?.AvatarScale ?? 1.0, 0.1, 3.0);
        private static int EffAvatarOffsetX() => Math.Clamp(EffectiveTubeLayout()?.AvatarOffsetX ?? 0, -1000, 1000);
        private static int EffAvatarOffsetY() => Math.Clamp(EffectiveTubeLayout()?.AvatarOffsetY ?? 0, -500, 500);
        private static int EffAvatarDetachedOffsetX() => Math.Clamp(EffectiveTubeLayout()?.AvatarDetachedOffsetX ?? 0, -1000, 1000);
        private static int EffAvatarDetachedOffsetY() => Math.Clamp(EffectiveTubeLayout()?.AvatarDetachedOffsetY ?? 0, -500, 500);

        /// <summary>
        /// True when the mod replaces tube.png but not tube2.png - then the detached state uses the
        /// mod's attached pane AND the attached margins, or the avatar lands outside the chamber the
        /// author drew (bug report #172). <see cref="CoreModArt"/> answers both halves, so this is
        /// the WPF predicate rather than the old hard-coded false; with no mod layer up both are
        /// false and the detached layout stays detached, exactly as before.
        /// </summary>
        private static bool ModOverridesAttachedTubeOnly()
            => CoreModArt.HasOverride("tube.png") && !CoreModArt.HasOverride("tube2.png");

        // =========================================================================================
        //  Tube glass and avatar art. PORTED from AvatarTubeWindow.Windowing.cs (SetTubeStyle) and
        //  Avatar.cs (LoadAvatarPoses / ApplyAvatarTransform / UpdateTitleDisplay).
        // =========================================================================================

        /// <summary>
        /// Points ImgTubeFrame at tube.png or tube2.png - the mod's replacement if it ships one,
        /// else this head's own shipped copy. A mod that overrides only the attached pane owns both
        /// states (see <see cref="ModOverridesAttachedTubeOnly"/>).
        ///
        /// <para>ponytail: the MIDNIGHT glass pair is deliberately not here. WPF gates it on
        /// <c>ArcademyHostService.WalletOwnsSku(SkuTubeMidnight)</c> - a purchased cosmetic - and
        /// this head has no wallet seam, so the only answers available are "always show it" or
        /// "never". Showing a player glass they have not bought is the worse of the two lies, so
        /// the standard pair it is until an entitlement seam exists.</para>
        /// </summary>
        public void SetTubeStyle(bool useAlternative) => RunOnAvatar(() =>
        {
            try
            {
                if (useAlternative && ModOverridesAttachedTubeOnly()) useAlternative = false;
                var name = useAlternative ? "tube2.png" : "tube.png";
                var art = ModArt.TryLoad(name);
                if (art != null) _imgTubeFrame.Source = art;
                Log.Information("Tube style changed to: {Style}", name);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to change tube style"); }
        });

        /// <summary>Repaint the glass in place, without touching attach state - WPF's
        /// RefreshTubeGlass, which the Companion workshop cell calls after a settings flip.</summary>
        public void RefreshTubeGlass() => SetTubeStyle(!_isAttached);

        /// <summary>Loads this set's four poses, shows the first that exists, sizes the border for
        /// the set and captions the title box, then starts the idle rotation only when there is
        /// more than one pose to rotate between.</summary>
        private void ApplyAvatarSet()
        {
            _avatarPoses = LoadAvatarPoses(_currentAvatarSet);
            _currentPoseIndex = 0;
            for (int i = 0; i < _avatarPoses.Length; i++)
                if (_avatarPoses[i] != null) { _currentPoseIndex = i; break; }
            ShowCurrentPose();

            ApplyAvatarTransform(_currentAvatarSet);
            UpdateTitleDisplay();

            int loaded = 0;
            foreach (var pose in _avatarPoses) if (pose != null) loaded++;
            if (loaded > 1) _poseTimer.Start();
        }

        /// <summary>
        /// The four pose PNGs for a set - set 1 is <c>avatar_pose{n}.png</c>, the rest
        /// <c>avatar{set}_pose{n}.png</c> - falling back per slot to set 1's same pose, which is the
        /// chain WPF's LoadAvatarPoses walks. A slot nothing resolves stays null and is skipped by
        /// the rotation rather than blanking the avatar.
        /// </summary>
        private static Bitmap?[] LoadAvatarPoses(int setNumber)
        {
            var poses = new Bitmap?[4];
            string prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
            for (int i = 0; i < poses.Length; i++)
            {
                poses[i] = ModArt.TryLoad($"{prefix}{i + 1}.png");
                if (poses[i] == null && setNumber > 1) poses[i] = ModArt.TryLoad($"avatar_pose{i + 1}.png");
            }
            return poses;
        }

        /// <summary>
        /// Per-set framing: sets 2+ read 12% bigger and 10px right, and Locked's set 1 ("The Lure")
        /// is drawn smaller than its siblings so it gets 6%. WPF used LayoutTransform; the note on
        /// <see cref="ApplyTubeLayoutOffsets"/> covers why RenderTransform is the twin here.
        /// </summary>
        private void ApplyAvatarTransform(int setNumber)
        {
            _avatarBorder.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            if (setNumber > 1)
            {
                _avatarBorder.RenderTransform = new TransformGroup
                {
                    Children = { new ScaleTransform(1.12, 1.12), new TranslateTransform(10, 0) }
                };
            }
            else if (CoreMods.ActiveModId == BuiltInMods.LockedId)
                _avatarBorder.RenderTransform = new ScaleTransform(1.06, 1.06);
            else
                _avatarBorder.RenderTransform = null;
        }

        /// <summary>
        /// Captions the title box: the persona's own name and level for the sets that have one,
        /// else the legacy avatar title for that set. Both go through
        /// <c>CoreMods.MakeModAware</c>, which is what lets a .ccpmod rename the companion in her
        /// own tube the way it renames her everywhere else.
        /// <para>Set from code rather than bound because a persona name has no static loc key, so
        /// this re-runs on LanguageChanged - see <see cref="OnTubeLanguageChanged"/>.</para>
        /// <para>ponytail: WPF's third branch captions from the emotive-portrait manifest's SKIN
        /// title. Portrait mode needs the crossfade layer that did not port.</para>
        /// </summary>
        private void UpdateTitleDisplay()
        {
            var companionId = CompanionForAvatarSet(_currentAvatarSet);
            if (companionId.HasValue)
            {
                var def = CompanionDefinition.GetById(companionId.Value);
                var name = def.GetDisplayName(CoreSettings.Current.SlutModeEnabled);
                _txtAvatarTitle.Text = CoreMods.MakeModAware(name).ToUpperInvariant();

                CoreSettings.Current.CompanionProgressData.TryGetValue((int)companionId.Value, out var progress);
                _txtAvatarLevel.IsVisible = true;
                _txtAvatarLevel.Text = progress?.IsMaxLevel == true
                    ? Loc.Get("avatar_level_max")
                    : Loc.GetF("avatar_level_format", progress?.Level ?? 1);
                return;
            }

            int titleIndex = Math.Clamp(_currentAvatarSet - 1, 0, AvatarTitleKeys.Length - 1);
            _txtAvatarTitle.Text = CoreMods.MakeModAware(Loc.Get(AvatarTitleKeys[titleIndex]));
            // Sets 1-2 are generic sprites: showing a level there reads as a PERSONA level and is
            // the confusion WPF hides it for.
            _txtAvatarLevel.IsVisible = false;
        }

        private void OnTubeLanguageChanged(object? sender, EventArgs e) => RunOnAvatar(UpdateTitleDisplay);

        /// <summary>Avatar-set titles, in set order. Loc keys, from AvatarTubeWindow.Avatar.cs.</summary>
        private static readonly string[] AvatarTitleKeys =
        {
            "avatar_title_basic_bimbo",           // Set 1
            "avatar_title_dumb_airhead",          // Set 2
            "avatar_title_synthetic_blowdoll",    // Set 3
            "avatar_title_perfect_fuckpuppet",    // Set 4
            "avatar_title_brainwashed_slavedoll", // Set 5
            "avatar_title_platinum_puppet",       // Set 6
            "avatar_title_bambi_cow",             // Set 7
        };

        /// <summary>The persona an avatar set belongs to; null for sets 1-2, which are generic
        /// sprites with no companion behind them. From AvatarTubeWindow.Avatar.cs.</summary>
        private static CompanionId? CompanionForAvatarSet(int setNumber) => setNumber switch
        {
            3 => CompanionId.OGBambiSprite,
            4 => CompanionId.CultBunny,
            5 => CompanionId.BrainParasite,
            6 => CompanionId.BambiTrainer,
            7 => CompanionId.BimboCow,
            _ => null,
        };

        // =========================================================================================
        //  Speech. PORTED from AvatarTubeWindow.Speech.cs - the PRIORITY path only.
        // =========================================================================================

        /// <summary>
        /// Say a line now, cutting off whatever was on screen. The companion's interrupt path: an
        /// AI reply, a scripted ceremony line, a high-priority bark. Keeps the full WPF signature so
        /// every existing call site compiles unchanged.
        ///
        /// <para><b>What it does.</b> Cancels the running bubble, appends the line to the chat log,
        /// shows or hides the AI badge from <paramref name="aiGenerated"/> (the CCBill addendum's
        /// visible-labelling rule - a canned phrase must never wear it), plays the voice, renders
        /// the bubble and hides it again after the user's own Bubble Duration, held open while the
        /// pointer is over it. An uninterruptible recorded clip refuses it outright, as on WPF.</para>
        ///
        /// <para><b>What it drops, and why each is safe to drop rather than fake.</b></para>
        /// <list type="bullet">
        ///   <item>The speech QUEUE and its post-line delay. Priority speech CLEARS the queue on
        ///         WPF, so the priority path never reads it; there is nothing here to enqueue
        ///         behind, and the delay only spaces lines this head cannot yet emit.</item>
        ///   <item>The typewriter. Cosmetic, and WPF adds its runtime to the display duration - so
        ///         dropping it shortens the window rather than truncating the line. The reading
        ///         floor below is kept, which is the half that protects a long reply.</item>
        ///   <item>The lead-in timer and <paramref name="mood"/>. Both exist to time the avatar's
        ///         emotive-portrait pose swap against the voice; that system did not port, so a
        ///         lead-in would be a pause with nothing happening in it.</item>
        ///   <item>EMI Desk's <c>NoteAvatarSpeaking</c> / <c>AvatarMuted</c>. Her service is
        ///         head-side; her mute is a SECOND mute on top of the user's own, so leaving it out
        ///         cannot silence a line that should sound, only fail to silence one she would
        ///         have. Named in the blocked list.</item>
        /// </list>
        ///
        /// <para><b>ponytail: two lines in quick succession can overlap.</b> WPF cuts the previous
        /// voiceline with <c>StopSpokenAudio</c>, which needs an <c>AudioPlaybackHandle</c>;
        /// <c>CoreAudio.PlayOneShot</c> is fire-and-forget and returns none. The bubble still
        /// preempts correctly - this is audio only, and it is audible rather than silent, which is
        /// why it ships as a note instead of as a dropped voiceline.</para>
        /// </summary>
        public void GigglePriority(string text, bool playSound = true, bool aiGenerated = true,
                                   string? phraseAudioPath = null, bool barkVoice = false,
                                   string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return;
            RunOnAvatar(() =>
            {
                try
                {
                    // Only a GENUINE AI reply anchors the bark system's chat-suppression window;
                    // bark output passes aiGenerated:false and must not suppress the next bark.
                    if (aiGenerated) _lastAiBubbleUtc = DateTime.UtcNow;

                    _speechTimer?.Stop();
                    AddToChatHistory(text, isUser: false);

                    // The chat log owns the bubble while it is up - take it back before rendering.
                    if (_isShowingChatHistory)
                    {
                        _isShowingChatHistory = false;
                        _chatHistoryView.IsVisible = false;
                        _speechScroller.IsVisible = true;
                    }

                    _aiBadge.IsVisible = aiGenerated;
                    _policyBadge.IsVisible = false;   // mutually exclusive with the AI badge
                    _isListeningBubble = false;

                    // Mute silences her VOICE and keeps the text (#445) - a muted companion that
                    // also stopped showing bubbles read as completely broken.
                    if (!IsMuted) PlaySpeechAudio(playSound, phraseAudioPath, barkVoice);

                    _txtSpeech.Text = text;
                    _speechBubble.MaxWidth = 380;
                    ApplySpeechBubblePlacement();
                    _speechBubble.IsVisible = true;
                    _isGiggling = true;
                    _isShowingAiBubble = true;

                    StartBubbleHideTimer(text);
                    Log.Debug("Companion says ({Chars} chars, ai={Ai}): {Text}", text.Length, aiGenerated, text);
                }
                catch (Exception ex) { Log.Warning(ex, "AvatarTube GigglePriority failed"); }
            });
        }

        /// <summary>
        /// True while the companion is mid-chat: an AI bubble is on screen, or a genuine AI reply
        /// landed within <paramref name="windowMs"/>. The bark system asks this to avoid talking
        /// over a conversation.
        /// <para>ponytail: WPF also returns true while an AI request is IN FLIGHT
        /// (<c>_isWaitingForAi</c>), which is ChatInput.cs's inference pipeline. This head never
        /// sets that flag, so the window opens when the reply lands rather than when it is asked
        /// for - narrower, never wider, so no bark is let through that WPF would have held.</para>
        /// </summary>
        public bool IsCompanionBusy(int windowMs)
        {
            if (_isShowingAiBubble) return true;
            return windowMs > 0 && (DateTime.UtcNow - _lastAiBubbleUtc).TotalMilliseconds < windowMs;
        }

        /// <summary>The user's avatar mute. WPF mirrors this setting into a field the quick menu
        /// flips; reading the setting itself is the same answer with nothing to keep in sync.</summary>
        public bool IsMuted => CoreSettings.Current.AvatarMuted;

        /// <summary>
        /// Auto-hide, at the user's Bubble Duration (1-10s). A long line gets an ESL-friendly
        /// reading floor of ~12 chars/sec capped at 30s, so a 200-char reply is not gone in two
        /// seconds (bug #193). Hovering the bubble holds it open, re-checked every second.
        /// </summary>
        private void StartBubbleHideTimer(string text)
        {
            double seconds = Math.Clamp(CoreSettings.Current.BubbleDurationSeconds, 1.0, 10.0);
            seconds = Math.Max(seconds, Math.Min(30.0, text.Length / 12.0));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                if (_isMouseOverSpeechBubble) { timer.Interval = TimeSpan.FromSeconds(1); return; }
                timer.Stop();
                _speechBubble.IsVisible = false;
                _isShowingAiBubble = false;
                _isGiggling = false;
            };
            _speechTimer = timer;
            timer.Start();
        }

        /// <summary>
        /// The voice for one bubble, with WPF's volume curves verbatim: a bark voiceline at
        /// master^1.5 * 0.85, a phrase clip at * 0.56, the canned giggle at * 0.7. MasterVolume 0
        /// means "attempt no audio at all" (the mute egg), so it returns before touching a file.
        /// <para>"Mute Voice Lines" (#846) silences only the spoken VO and drops back to a sound
        /// cue, so she still reads as present - the single choke point every voiced line funnels
        /// through on WPF. The cue is the giggle; WPF's PlayFallbackBubbleSound picks between the
        /// giggles and the "um" set, and that coin flip lives in Reactions.cs.</para>
        /// </summary>
        private void PlaySpeechAudio(bool playSound, string? phraseAudioPath, bool barkVoice)
        {
            try
            {
                var master = CoreSettings.Current.MasterVolume / 100f;
                if (master <= 0f) return;
                var curved = (float)Math.Pow(master, 1.5);

                if (!string.IsNullOrEmpty(phraseAudioPath))
                {
                    if (barkVoice && CoreSettings.Current.CompanionVoiceLinesMuted)
                    {
                        PlayGiggleSound(curved);
                        return;
                    }
                    if (!File.Exists(phraseAudioPath)) return;
                    CoreAudio.PlayOneShot(phraseAudioPath!, curved * (barkVoice ? 0.85f : 0.56f),
                                          barkVoice ? "bark-voice" : "phrase-audio");
                    return;
                }

                if (playSound) PlayGiggleSound(curved);
            }
            catch (Exception ex) { Log.Debug("AvatarTube speech audio failed: {Error}", ex.Message); }
        }

        /// <summary>
        /// One of giggle5-8. Bambi Sleep suppresses the canned "hehehe" outright - it sounds cheap
        /// next to that mod's real voiceline barks, so a clip-less bubble there stays silent.
        ///
        /// <para><b>ponytail: this head ships no <c>Resources/sounds</c>.</b> Only a MOD's override
        /// resolves today, so a stock install is silent here. That is a missing asset link in
        /// CCP.Avalonia.csproj, not a missing seam - and File.Exists means a miss is silence rather
        /// than a bogus path handed to the audio service, which is WPF's own behaviour for
        /// giggle6 (it ships as .wav, and the shipped-file lookup only ever asks for .mp3).</para>
        /// </summary>
        private void PlayGiggleSound(float curvedVolume)
        {
            if (CoreMods.ActiveModId.Contains("bambi", StringComparison.OrdinalIgnoreCase)) return;

            var name = $"giggle{5 + _random.Next(4)}.mp3";
            var path = CoreModArt.OverridePath($"sounds/{name}")
                       ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", name);
            if (!File.Exists(path)) return;
            CoreAudio.PlayOneShot(path, curvedVolume * 0.7f, "giggle");
        }

        // =========================================================================================
        //  Chat shortcut. PORTED from AvatarTubeWindow.ChatInput.cs. DevicesSettingsSection and the
        //  companion hero card read the label; the capture dialog writes the setting back through
        //  SerializeModifiers.
        // =========================================================================================

        /// <summary>
        /// Where <see cref="OpenChatCommand"/> lands. WPF used a <c>RoutedUICommand</c> and let the
        /// routed-command tree find whichever window handled it; Avalonia has no routed commands, so
        /// the open tube claims this in <c>OnOpened</c> and releases it in <c>OnClosed</c>. Volatile
        /// and null-tolerant for the same reason every seam here is: with no tube open the shortcut
        /// must do nothing, not throw into a keypress handler.
        /// </summary>
        private static volatile Action? OpenChatSink;

        /// <summary>The command the chat-shortcut KeyBinding is bound to on every window that
        /// carries it. Always executable - the sink decides whether there is anything to open.</summary>
        public static readonly ICommand OpenChatCommand = new OpenChatCommandImpl();

        private sealed class OpenChatCommandImpl : ICommand
        {
            // Never raised: enablement does not change, so Avalonia's one-shot CanExecute read is
            // the whole story here (see the porting note about CanExecuteChanged).
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter)
            {
                try { OpenChatSink?.Invoke(); } catch { /* a hotkey must never take the app down */ }
            }
        }

        /// <summary>Opens the chat input panel and puts the caret in it.</summary>
        public void OpenChatInput() => RunOnAvatar(() =>
        {
            _inputPanel.IsVisible = true;
            // Input priority, not inline: the panel has just become visible and is not laid out yet,
            // so focusing it in the same beat silently does nothing.
            Dispatcher.UIThread.Post(() => _txtUserInput.Focus(), DispatcherPriority.Input);
        });

        /// <summary>
        /// Rebuilds the chat-shortcut KeyBinding on a window from the user's setting. Removes any
        /// prior binding for <see cref="OpenChatCommand"/> first, so repeated calls do not stack
        /// duplicates. Falls back to Ctrl+T on an empty or unparseable setting.
        /// <para>The binding on the TUBE is the lesser half: the tube is
        /// <c>ShowActivated="False"</c> and rarely holds focus, so the binding that actually fires
        /// is the one the shell puts on itself - WPF calls this for MainWindow too.</para>
        /// <para>Deviation: WPF caught <c>NotSupportedException</c> from <c>KeyGesture</c>, which
        /// rejects a bare letter. Avalonia's <c>KeyGesture</c> accepts any pair, so there is nothing
        /// to catch; the capture dialog already refuses a modifier-less letter at the source.</para>
        /// </summary>
        public static void ApplyChatShortcutTo(Window? window)
        {
            if (window == null) return;

            var (key, mods) = CurrentChatShortcut();

            for (int i = window.KeyBindings.Count - 1; i >= 0; i--)
                if (ReferenceEquals(window.KeyBindings[i].Command, OpenChatCommand))
                    window.KeyBindings.RemoveAt(i);

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(key, mods),
                Command = OpenChatCommand,
            });
        }

        /// <summary>"Ctrl+T" / "Alt+Shift+B" — for the hero card button and the Devices row.</summary>
        public static string FormatChatShortcut()
        {
            var (key, mods) = CurrentChatShortcut();

            var parts = new List<string>();
            if ((mods & KeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((mods & KeyModifiers.Meta) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        /// <summary>
        /// The stored shortcut, defaulted. One reader for both the label and the binding so they can
        /// never disagree about what an unparseable setting means.
        /// </summary>
        private static (Key Key, KeyModifiers Mods) CurrentChatShortcut()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.ChatShortcutKey) ? "T" : s!.ChatShortcutKey;
            var modsName = s?.ChatShortcutModifiers ?? "Control";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.T;
            if (!TryParseModifiers(modsName, out var mods)) mods = KeyModifiers.Control;
            return (key, mods);
        }

        /// <summary>
        /// Parses the stored "Control,Shift" form. Hand-mapped rather than
        /// <c>Enum.TryParse&lt;KeyModifiers&gt;</c>: Avalonia calls the Windows key <c>Meta</c>, so a
        /// settings file written on the WPF head - which stores "Windows" - would fail to parse and
        /// silently drop the whole combo back to Ctrl+T.
        /// </summary>
        private static bool TryParseModifiers(string s, out KeyModifiers result)
        {
            result = KeyModifiers.None;
            if (string.IsNullOrWhiteSpace(s)) return true;
            foreach (var part in s.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "control": case "ctrl": result |= KeyModifiers.Control; break;
                    case "alt": result |= KeyModifiers.Alt; break;
                    case "shift": result |= KeyModifiers.Shift; break;
                    case "windows": case "win": case "meta": result |= KeyModifiers.Meta; break;
                    case "none": break;
                    default: return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The stored form, written back by the capture dialog. Still serializes <c>"Windows"</c>
        /// rather than Avalonia's <c>"Meta"</c>, so one settings file keeps working on both heads.
        /// </summary>
        public static string SerializeModifiers(KeyModifiers m)
        {
            if (m == KeyModifiers.None) return "";
            var parts = new List<string>();
            if ((m & KeyModifiers.Control) != 0) parts.Add("Control");
            if ((m & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((m & KeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((m & KeyModifiers.Meta) != 0) parts.Add("Windows");
            return string.Join(",", parts);
        }

        // =========================================================================================
        //  Stubs. Each one is a handler the WPF XAML wired inline whose body lives in a partial
        //  this layer does not port, or reaches a service that is not in Core.
        // =========================================================================================

        /// <summary>Left click bounces her and fires a reaction bark; right click opens the menu.</summary>
        private void OnAvatarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) return; // Avalonia opens the ContextMenu itself

            // The reaction bark, the one line of WPF's ImgAvatar_MouseLeftButtonDown
            // (AvatarTubeWindow.ChatInput.cs:75) that crosses. It feeds the rolling 60s click
            // count behind the click-escalation eggs.
            CoreBark.NotifyAvatarClicked();

            // ponytail: the rest of that handler stays head-side and none of it is a Core move -
            // the 4-click animation refresh, the 50-clicks-in-60s collapse trigger,
            // App.Achievements.TrackAvatarClick, CirceClickEmote and the 1-in-25 pop sound all
            // reach App.* or the animated-avatar pipeline. So does BounceAvatar (the click squash,
            // AvatarTubeWindow.Avatar.cs), which here would be a hand-stepped tween on
            // AvatarBounceHost's RenderTransform, not an Animation.
        }

        /// <summary>Send whatever is in the input box to the companion.</summary>
        private void SendChat()
        {
            var input = _txtUserInput.Text;
            if (string.IsNullOrWhiteSpace(input)) return;
            AddToChatHistory(input, isUser: true);
            _txtUserInput.Text = string.Empty;
            // ponytail: needs ConditioningControlPanel/AvatarTube/AvatarTubeWindow.ChatInput.cs
            // (the moderation guard, the AI request, the typewriter reply and TTS) plus
            // the app's single App.ModerationCounter instance for the cooldown CooldownTick above
            // paints - the counter CLASS is in Core, the instance and its seam are not; see that
            // method. Nothing is INFERRED here, so nothing leaves the process and there is no
            // request for the guard to refuse; the user's line lands in the local log and that is
            // the whole of what this does. CoreModerationLog is the seam for the RECORD half when
            // there is a request to record - it is not the counter, and cannot gate one.
        }

        /// <summary>Step through the unlocked avatar sets with the title-box arrows.</summary>
        private void SelectAvatarSet(int delta)
        {
            // The old note here named App.Mods.IsAvatarSetSupported / GetCustomAvatarSets as the
            // blocker. That is STALE: both are one-liners over ModManifest.SupportedAvatarSets and
            // .CustomAvatarSets (ModService.cs:1268/1289), and the whole manifest is in Core -
            // CoreMods.InstalledMods[ActiveModId].Manifest answers both today. The list of sets is
            // not what is missing.
            //
            // ponytail: what is missing is the COMPANION COUPLING. WPF's SwitchToAvatarSet
            // (AvatarTubeWindow.Avatar.cs:395) persists SelectedAvatarSet and switches the active
            // companion in the same beat for sets 4+, because the tube's caption reads the persona
            // behind the SET. CoreModsHooks.SwitchCompanion is the seam and no head seeds it, so an
            // arrow here would write a shared setting and leave the app's active companion pointing
            // somewhere else - a second writer for one setting, which is the trap this port keeps
            // hitting. Both arrows are IsVisible=False in the XAML (WPF's UpdateNavigationArrows is
            // what reveals them), so nothing reaches this today: the tube shows
            // CoreSettings.Current.SelectedAvatarSet and stays on it.
        }

        /// <summary>Refresh the context menu's checkmarks and the remote-emote item swap.</summary>
        private void UpdateQuickMenuState()
        {
            // Deliberately inert, and the reason is NOT the one an earlier note gave. Settings and
            // two seams would in fact answer several of WPF's labels today - CoreSession.IsEngineRunning
            // for the Engine item (WPF proxies it off App.Flash.IsRunning), CoreAi.IsAvailable for the
            // "talk to" item, TriggerModeEnabled / AutonomyModeEnabled / AvatarMuted / SubAudioMuted
            // for the rest, and RemoteEmotePresets for the emote swap.
            //
            // What is missing is the OTHER half: not one of these MenuItems has a Click handler on
            // this head, because every action behind them - MainWindow's engine start/stop, the
            // Takeover gate, the personality submenu, the browser pause, App.RemoteControl's emote
            // send - is head-side. Retitling an item to "STOP ENGINE" in red while clicking it does
            // nothing is strictly worse than the static label it carries now: the menu would report
            // live state it cannot act on. Restore the labels WITH their handlers, not before.
            //
            // ponytail: needs MainWindow.StartEngine / StopEngine with ChatInput.cs's #479 guards
            // (IsEngineStopLocked plus App.Lockdown.NotifyEscapeAttempt - the tube's Stop is the
            // same escape as main's and must count the same), App.Patreon.HasPremiumAccess,
            // App.RemoteControl.ControllerConnected, and ChatInput.cs's PopulatePersonalityMenu.
            //
            // The Mute item is the one that LOOKS free - IsMuted already reads
            // CoreSettings.Current.AvatarMuted and GigglePriority honours it, so a two-line flip
            // would work. It is left out anyway: WPF's MenuItemMute_Click ends with
            // MainWindow.SyncQuickControlsUI, and the companion room's own mute switch
            // (MainShellWindow.CompanionRoom.cs SetAvatarMuted) reads the setting once at load. A
            // flip here would leave that switch showing the opposite of the truth until the shell
            // is rebuilt, which is a control lying about state in a file this layer does not own.
        }
    }
}
