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
        private DateTime _lastClickTime = DateTime.MinValue;
        private bool _isInputVisible = false;
        private readonly List<DateTime> _rapidClickTimestamps = new(); // Track clicks for 50-in-1-minute trigger
        private bool _isShowingChatHistory = false;
        // Note: Whispers mute state is now read from App.Settings.Current.SubAudioEnabled
        private bool _isBrowserPaused = false; // Browser audio paused state

        // ===== P1.4 moderation counter / chat cooldown =====
        private DispatcherTimer? _cooldownTickTimer;
        private string? _normalChatPlaceholder;
        private DateTime? _cooldownEndsAt;
        
        // Interaction counter for 1-in-4 logic
        private int _interactionCount = 0;
        private DateTime _lastInteractionTime = DateTime.MinValue;
        private int _animationRefreshClickCount = 0;

        private void ImgAvatar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;

            // Refresh animation every 4 clicks to prevent stuck animations
            _animationRefreshClickCount++;
            if (_animationRefreshClickCount >= 4)
            {
                _animationRefreshClickCount = 0;
                RefreshAvatarAnimation();
            }

            // Track rapid clicks for 50-in-1-minute "Bambi Cum and Collapse" trigger
            _rapidClickTimestamps.Add(now);
            // Remove clicks older than 1 minute
            _rapidClickTimestamps.RemoveAll(t => (now - t).TotalSeconds > 60);

            // Check if 50+ clicks in the last minute
            if (_rapidClickTimestamps.Count >= 50)
            {
                _rapidClickTimestamps.Clear(); // Reset to prevent repeat triggers
                TriggerBambiCumAndCollapse();
            }

            // Track for Neon Obsession achievement (20 rapid clicks)
            App.Achievements?.TrackAvatarClick();

            // Bark hook: rolling 60s click count drives the click-escalation eggs.
            try { App.Bark?.NotifyAvatarClicked(); } catch { }

            // Animated avatar: a click rotates to a rare affectionate emote (3s cooldown). No-op for
            // static/portrait avatars or while cooling down.
            try { CirceClickEmote(); } catch { }

            // 1 in 25 chance to play a pop sound
            if (_random.Next(25) == 0)
            {
                PlayAvatarPopSound();
            }

            // Log click count for debugging
            var clickCount = App.Achievements?.Progress.AvatarClickCount ?? 0;
            App.Logger?.Debug("Avatar clicked! Count: {Count}/20, RapidClicks: {RapidCount}/50", clickCount, _rapidClickTimestamps.Count);

            // Double-click detection — open chat input if AI available, otherwise activity comment
            if ((now - _lastClickTime).TotalMilliseconds < 300)
            {
                if (_isMuted)
                {
                    // Show brief muted indicator so user knows she's not broken
                    ShowMutedIndicator();
                }
                else if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
                {
                    // Open the chat input panel (same as "Talk to" menu item)
                    ShowInputPanel();
                }
                else if (_isGiggling || _isWaitingForAi)
                {
                    App.Logger?.Debug("Skipping double-click - message still showing");
                }
                else if ((now - _lastInteractionTime).TotalSeconds >= 1.5)
                {
                    _lastInteractionTime = now;
                    _ = TriggerActivityCommentAsync();
                }
            }
            _lastClickTime = now;

            // Visual feedback - glow pulse on the drop shadow effect
            // Pulse whichever avatar is currently visible
            var activeAvatar = _useAnimatedAvatar ? ImgAvatarAnimated : ImgAvatar;
            if (activeAvatar.Effect is System.Windows.Media.Effects.DropShadowEffect dropShadow)
            {
                // Pulse the blur radius for glow effect (longer duration for visibility)
                var blurPulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 20,
                    To = 60,
                    Duration = TimeSpan.FromMilliseconds(200),
                    AutoReverse = true,
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                };
                dropShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, blurPulse);

                // Also pulse the opacity for a brighter flash
                var opacityPulse = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.6,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    AutoReverse = true,
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                };
                dropShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, opacityPulse);
            }

            // Bouncy squash-and-settle on every click.
            PlayClickBounce();
        }

        /// <summary>
        /// A quick springy bounce of the whole avatar on click: pop up to ~1.15x then settle back to
        /// 1.0 with an elastic ease. Runs on a dedicated ScaleTransform (AvatarBounceScale) that wraps
        /// all avatar layers, so it composes with — and never fights — the 60fps float/breathing writes.
        /// FillBehavior.Stop returns the transform to its 1.0 local value when done.
        /// </summary>
        private void PlayClickBounce()
        {
            if (AvatarBounceScale == null) return;

            var kf = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
            {
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            kf.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                1.015, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
                new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }));
            kf.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)),
                new System.Windows.Media.Animation.ElasticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                    Oscillations = 1,
                    Springiness = 7
                }));

            AvatarBounceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, kf);
            AvatarBounceScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, kf.Clone());
        }

        private void ImgAvatar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Close input panel on right-click
            HideInputPanel();
        }

        /// <summary>
        /// Trigger a comment based on current activity or random thought (Double-click action)
        /// </summary>
        private async Task TriggerActivityCommentAsync()
        {
            // 1. Trigger Mode Enabled: Always prioritize Custom Triggers
            if (App.Settings?.Current?.TriggerModeEnabled == true)
            {
                var triggers = App.Settings?.Current?.CustomTriggers;
                if (triggers != null && triggers.Count > 0)
                {
                    var trigger = triggers[_random.Next(triggers.Count)];
                    GigglePriority(trigger, aiGenerated: false);
                    return;
                }
            }

            // 2. Trigger Mode Disabled: Use 1-in-4 logic
            // 3/4 times -> Default Preset Phrase
            // 1/4 times -> Try AI/Context
            
            _interactionCount++;

            if (_interactionCount % 4 != 0)
            {
                // Show standard random Bambi phrase
                GigglePriority(GetRandomBambiPhrase(), aiGenerated: false);
                return;
            }

            // --- AI / Awareness Logic (1 in 4 chance) ---

            // Fallback defaults
            string reaction = GetRandomBambiPhrase();
            bool isAiAvailable = App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true;
            bool gotAiResponse = false;

            // Get current awareness context
            var awareness = App.WindowAwareness;
            var category = awareness?.CurrentActivity ?? ActivityCategory.Unknown;
            var detectedName = awareness?.CurrentDetectedName ?? "";
            var serviceName = awareness?.CurrentServiceName ?? "";
            var pageTitle = awareness?.CurrentPageTitle ?? "";

            // Decision: Comment on activity OR random thought?
            // If Unknown/Idle, do random thought.
            // If recognized, do activity comment.
            
            bool isRecognizedActivity = category != ActivityCategory.Unknown && category != ActivityCategory.Idle;

            if (isRecognizedActivity)
            {
                // Try AI Activity Comment
                if (isAiAvailable && App.Ai != null)
                {
                    try
                    {
                        // Show quick thinking indicator
                        if (!_isGiggling) Giggle("Hmm...");

                        var aiReaction = await App.Ai.GetAwarenessReactionAsync(detectedName, category.ToString(), serviceName, pageTitle);
                        if (!string.IsNullOrEmpty(aiReaction))
                        {
                            reaction = aiReaction;
                            gotAiResponse = true;
                        }
                        else
                        {
                            // Fallback to preset if AI returns empty
                            reaction = GetPhraseForCategory(category, detectedName);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to get AI awareness reaction on double-click");
                        reaction = GetPhraseForCategory(category, detectedName);
                    }
                }
                else
                {
                    // No AI, use preset
                    reaction = GetPhraseForCategory(category, detectedName);
                }
            }
            else
            {
                // Unrecognized/Idle/Desktop -> Random Thought
                if (isAiAvailable && App.Ai != null)
                {
                    try
                    {
                        // Show quick thinking indicator
                        if (!_isGiggling) Giggle("Hmm...");

                        // R2-NEW-H-1: migrate to typed AI API. Refusals are silently
                        // dropped on this non-chat surface (the user didn't directly
                        // prompt — a POLICY bubble out of nowhere is jarring). The
                        // downstream guard in AiService already logged via ModerationLog.
                        // IsAiGenerated propagates so canned fallbacks don't wear the badge.
                        var aiResult = await App.Ai.GetBambiReplyExAsync("Say something random and ditzy about what we're doing (or not doing) right now.");
                        if (aiResult.Refusal != null)
                        {
                            // Silent drop — fall back to preset behaviour below.
                        }
                        else if (!string.IsNullOrEmpty(aiResult.Text))
                        {
                            reaction = aiResult.Text;
                            gotAiResponse = aiResult.IsAiGenerated;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to get AI random thought on double-click");
                    }
                }
            }

            // Double bounce for AI responses to attract attention
            if (gotAiResponse)
            {
                PlayDoubleBounce();
            }

            // Display the result with priority. The badge only fires when we actually got an
            // AI-generated reaction — preset fallbacks are unmarked.
            GigglePriority(reaction, aiGenerated: gotAiResponse);
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Report user activity to autonomy service
            App.Autonomy?.ReportUserActivity();

            // Close input panel when clicking outside of it
            if (_isInputVisible)
            {
                // Check if the click is outside the input panel
                var clickedElement = e.OriginalSource as DependencyObject;
                if (clickedElement != null && !IsDescendantOf(clickedElement, InputPanel))
                {
                    HideInputPanel();
                }
            }
        }

        private bool IsDescendantOf(DependencyObject element, DependencyObject parent)
        {
            while (element != null)
            {
                if (element == parent) return true;
                // ContentElements (e.g. Run, Hyperlink) are not part of the visual tree —
                // VisualTreeHelper.GetParent throws "X is not a Visual or Visual3D" on them.
                // Fall back to LogicalTreeHelper for those.
                element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                    : System.Windows.LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private void HideInputPanel()
        {
            if (_isInputVisible)
            {
                _isInputVisible = false;
                InputPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void MenuItemDismiss_Click(object sender, RoutedEventArgs e)
        {
            // Hide the sprite and reattach to main window UI
            App.Logger?.Information("User dismissed avatar - hiding and reattaching");

            // Reattach if detached
            if (!_isAttached)
            {
                Attach();
            }

            // Hide the tube
            HideTube();
        }

        /// <summary>
        /// Shows a speech bubble immediately with priority (for AI responses).
        /// Clears any pending queue and interrupts current bubble.
        /// Also clears the AI waiting flag.
        /// </summary>
        /// <param name="text">Text to display</param>
        /// <param name="playSound">Whether to play giggle sound (default true for AI responses)</param>
        // ============================================================
        // CHAT HISTORY MODE
        // ============================================================

        private void MenuItemShowChatHistory_Click(object sender, RoutedEventArgs e)
        {
            EnterChatHistoryMode();
        }

        private void BtnCloseChatHistory_Click(object sender, RoutedEventArgs e)
        {
            ExitChatHistoryMode();
        }

        private void AvatarTubeWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isShowingChatHistory)
            {
                ExitChatHistoryMode();
                e.Handled = true;
            }
        }

        private void EnterChatHistoryMode()
        {
            // Cancel any in-flight bubble timers — chat history takes over the bubble.
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            StopThinkingAnimation();
            _isWaitingForAi = false;
            _isGiggling = false;

            _isShowingChatHistory = true;

            // Show empty-state hint when there are no captured messages yet.
            TxtChatHistoryEmpty.Visibility = ChatHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Swap bubble content: hide single-message view, show chat history.
            SpeechScroller.Visibility = Visibility.Collapsed;
            ChatHistoryView.Visibility = Visibility.Visible;
            // Hide the per-message AI badge when showing the chat history list (mixed AI + user lines).
            if (AiBadge != null) AiBadge.Visibility = Visibility.Collapsed;

            // Enlarge bubble for the chat history layout.
            SpeechBubble.MaxWidth = 600;

            SpeechBubble.UpdateLayout();
            SpeechBubble.Visibility = Visibility.Visible;

            // Auto-scroll to most recent message.
            Dispatcher.BeginInvoke(new Action(() => ChatHistoryScroller.ScrollToBottom()),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ExitChatHistoryMode()
        {
            _isShowingChatHistory = false;
            ChatHistoryView.Visibility = Visibility.Collapsed;
            SpeechScroller.Visibility = Visibility.Visible;
            SpeechBubble.MaxWidth = 380; // Restore default bubble width.
            SpeechBubble.Visibility = Visibility.Collapsed;
        }

        private void ToggleInputPanel()
        {
            _isInputVisible = !_isInputVisible;
            InputPanel.Visibility = _isInputVisible ? Visibility.Visible : Visibility.Collapsed;

            if (_isInputVisible)
            {
                FocusInputAfterLayout();
            }
        }

        private void ShowInputPanel()
        {
            _isInputVisible = true;
            InputPanel.Visibility = Visibility.Visible;
            FocusInputAfterLayout();
        }

        /// <summary>
        /// Public entry point for opening the avatar chat input (used by Ctrl+T keybindings
        /// on this window and on MainWindow). Marshals to the UI thread because the
        /// keybinding handler may run from MainWindow's dispatcher; the avatar window
        /// could be on a different one if it's been reparented.
        /// </summary>
        public void OpenChatInput()
        {
            if (Dispatcher.CheckAccess()) ShowInputPanel();
            else Dispatcher.BeginInvoke(new Action(ShowInputPanel));
        }

        /// <summary>
        /// Routed command bound to Ctrl+T on this window (and on MainWindow via
        /// App.AvatarWindow?.OpenChatInput()). Opens the chat input panel.
        /// </summary>
        public static readonly RoutedUICommand OpenChatCommand =
            new RoutedUICommand("Open Avatar Chat", "OpenChat", typeof(AvatarTubeWindow));

        private void OpenChatCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            OpenChatInput();
        }

        /// <summary>
        /// Rebuilds the chat-shortcut KeyBinding on a window from the user's setting.
        /// Removes any prior binding bound to <see cref="OpenChatCommand"/> first so
        /// repeated calls don't stack duplicates. Safe to call from any thread; falls
        /// back to defaults if the setting is empty or unparseable.
        /// </summary>
        public static void ApplyChatShortcutTo(Window window)
        {
            if (window == null) return;

            var s = App.Settings?.Current?.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.ChatShortcutKey) ? "T" : s!.ChatShortcutKey;
            var modsName = s?.ChatShortcutModifiers ?? "Control";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.T;
            if (!TryParseModifiers(modsName, out var mods)) mods = ModifierKeys.Control;

            // Remove any existing chat-shortcut bindings.
            for (int i = window.InputBindings.Count - 1; i >= 0; i--)
            {
                if (window.InputBindings[i] is KeyBinding kb && kb.Command == OpenChatCommand)
                    window.InputBindings.RemoveAt(i);
            }

            // KeyGesture rejects letter keys without Ctrl/Alt (e.g. Shift+T alone)
            // and a handful of other unusual combos. Fall back to Ctrl+T rather
            // than crashing the click handler.
            try
            {
                window.InputBindings.Add(new KeyBinding(OpenChatCommand, key, mods));
            }
            catch (NotSupportedException)
            {
                App.Logger?.Warning("ApplyChatShortcutTo: rejected combo {Mods}+{Key}, falling back to Ctrl+T", mods, key);
                try
                {
                    window.InputBindings.Add(new KeyBinding(OpenChatCommand, Key.T, ModifierKeys.Control));
                }
                catch { }
            }
        }

        /// <summary>"Ctrl+T" / "Alt+Shift+B" — for the hero card button label.</summary>
        public static string FormatChatShortcut()
        {
            var s = App.Settings?.Current?.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.ChatShortcutKey) ? "T" : s!.ChatShortcutKey;
            var modsName = s?.ChatShortcutModifiers ?? "Control";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.T;
            if (!TryParseModifiers(modsName, out var mods)) mods = ModifierKeys.Control;

            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private static bool TryParseModifiers(string s, out ModifierKeys result)
        {
            result = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(s)) return true;
            foreach (var part in s.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var mk))
                    result |= mk;
                else
                    return false;
            }
            return true;
        }

        public static string SerializeModifiers(ModifierKeys m)
        {
            if (m == ModifierKeys.None) return "";
            var parts = new List<string>();
            if ((m & ModifierKeys.Control) != 0) parts.Add("Control");
            if ((m & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((m & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((m & ModifierKeys.Windows) != 0) parts.Add("Windows");
            return string.Join(",", parts);
        }

        /// <summary>
        /// Reliably moves keyboard focus into the chat input. The avatar tube is a
        /// transparent, borderless WS_EX_TOOLWINDOW — Windows' focus-stealing prevention
        /// silently rejects <see cref="Window.Activate"/> in that configuration, so we
        /// bypass it via AttachThreadInput before SetForegroundWindow. Then the focus
        /// calls are deferred to Input priority so the panel is fully laid out by the
        /// time we try to put the cursor in the textbox.
        /// </summary>
        private void FocusInputAfterLayout()
        {
            // ContextIdle runs after all pending input events (mouse-up from a
            // double-click, etc.) have been processed. Using the higher Input priority
            // raced with the second click's mouse-up and intermittently lost focus.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ForceForegroundWindow();
                    TxtUserInput.Focus();
                    Keyboard.Focus(TxtUserInput);
                    TxtUserInput.SelectAll();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AvatarTube: focus chat input failed: {Error}", ex.Message);
                }
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// Forces this window to the foreground regardless of focus-stealing prevention,
        /// using the AttachThreadInput technique. Required for tool windows
        /// (WS_EX_TOOLWINDOW) which Windows otherwise refuses to bring forward when
        /// requested by an app that doesn't currently own the foreground.
        /// </summary>
        private void ForceForegroundWindow()
        {
            // An ATTACHED tube is part of the main window and rides at its z-level. This raise is the
            // one remaining manual override that can lift it out of that pair, and over a game host
            // (Bureau / Intake / DtRH) it would re-create the interleaved half-and-half composite the
            // sibling-slotting fix in ChaosWebViewHost.ApplyNativeOwner exists to prevent. A DETACHED
            // tube is a self-contained topmost widget and keeps the old behaviour.
            if (!IsDetached && ChaosWebViewHost.AnyHostActive)
            {
                App.Logger?.Debug("AvatarTube: skipped foreground raise - attached tube stays at main's level while a game host is up");
                return;
            }

            // First try the WPF-friendly path. On the rare occasion it succeeds we save
            // the Win32 round trip; when it fails it's a no-op and we fall through.
            try { Activate(); } catch { }

            var hWnd = _tubeHandle != IntPtr.Zero ? _tubeHandle : new WindowInteropHelper(this).Handle;
            if (hWnd == IntPtr.Zero) return;

            var fg = GetForegroundWindow();
            if (fg == hWnd) return;

            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint myThread = GetCurrentThreadId();

            if (fgThread == 0 || fgThread == myThread)
            {
                SetForegroundWindow(hWnd);
                return;
            }

            // Briefly share input state with the foreground thread so SetForegroundWindow
            // is allowed through. Always detach in finally — leaving threads attached
            // wedges keyboard input across the whole desktop.
            bool attached = false;
            try
            {
                attached = AttachThreadInput(myThread, fgThread, true);
                SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached)
                {
                    try { AttachThreadInput(myThread, fgThread, false); } catch { }
                }
            }
        }

        private void TxtUserInput_KeyDown(object sender, KeyEventArgs e)
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

        private void BtnSendChat_Click(object sender, RoutedEventArgs e)
        {
            _ = SendChatMessageAsync();
        }

        private async Task SendChatMessageAsync()
        {
            var input = TxtUserInput.Text?.Trim();
            if (string.IsNullOrEmpty(input)) return;

            // P1.4 — chat cooldown gate. When the moderation counter is in cooldown
            // we swallow the send silently (no new bubble, no AI call). The countdown
            // text is already rendered into the input box; user can see why.
            var counterState = App.ModerationCounter?.GetState();
            if (counterState?.CooldownActive == true)
            {
                App.Logger?.Information("AvatarTubeWindow: chat send swallowed (cooldown active, ends={End})",
                    counterState.CooldownEndsAt);
                return;
            }

            TxtUserInput.Text = "";
            ToggleInputPanel();

            // EMIT hook for GamificationBridge companion-chat achievements. Fired once
            // per genuine user send (past the cooldown gate, non-empty input), before
            // the moderation/AI path so it counts the attempt regardless of outcome.
            App.Companion?.NotifyUserMessageSent();

            // P2/H5: user input is NOT added to chat history yet. If the moderation
            // guard refuses below we throw the input away — the prohibited text must
            // not remain visible in the in-memory history view. AddToChatHistory is
            // called only after the AI call returns with a non-refusal result.

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai != null && App.Ai.IsAvailable)
            {
                try
                {
                    // Animated thinking bubble: rotates phrases + dots while we wait.
                    // Sets _isWaitingForAi internally so other giggles don't interrupt.
                    StartThinkingAnimation();

                    // P2/C4: typed result. IsAiGenerated tells us whether the pink "AI"
                    // badge should appear (true only for a genuine LLM reply; cloud
                    // fallback / offline / login-required / local-Ollama-down all return
                    // IsAiGenerated=false so the bubble appears unbadged).
                    var result = await App.Ai.GetBambiReplyExAsync(input);

                    if (result.Refusal != null)
                    {
                        // Refused — render the POLICY badge + the localized refusal string
                        // instead of the normal AI bubble. The user's prohibited input is
                        // dropped without ever entering chat history (P2/H5). The textbox
                        // was already cleared above, so there's nothing left for the user
                        // to re-send by accident.
                        PlayDoubleBounce();
                        ShowModerationRefusalBubble(result.Refusal.Source);
                    }
                    else
                    {
                        // Allowed — NOW persist the user prompt to chat history (P2/H5).
                        AddToChatHistory(input, isUser: true);

                        // Double bounce to attract attention, then show AI response.
                        // aiGenerated flag flows through so canned fallbacks don't wear
                        // the AI badge (P2/C4 — audit smoke-test #1).
                        PlayDoubleBounce();
                        GigglePriority(result.Text, aiGenerated: result.IsAiGenerated);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI reply");
                    // Exception is NOT a moderation refusal — the user's input was a
                    // legitimate send that failed for an infrastructure reason. Persist
                    // it so the conversation transcript stays coherent.
                    AddToChatHistory(input, isUser: true);
                    GigglePriority(GetRandomBambiPhrase(), aiGenerated: false); // Clears _isWaitingForAi
                }
            }
            else
            {
                // No AI configured / disabled — still a legitimate send, persist the input
                // and respond with a preset phrase (no AI badge, no moderation in this path).
                AddToChatHistory(input, isUser: true);
                Giggle(GetRandomBambiPhrase());
            }
        }

        // ============================================================
        // CONTEXT MENU HANDLERS
        // ============================================================

        private void AvatarContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // Use Dispatcher to ensure UI updates are processed
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateQuickMenuState();
                UpdateContextMenuForState();
                RefreshEmoteMenuItemsForRemoteState();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        // When a remote controller is connected, swap the 5 normally-locked items
        // (Engine / TriggerMode / BambiTakeover / Personality / Mute) for the 5
        // emote preset items. Existing items' IsEnabled gating is left untouched —
        // they're just Collapsed in remote mode, so their disabled state doesn't
        // matter. Re-runs on every Opened so preset edits show up immediately.
        private void RefreshEmoteMenuItemsForRemoteState()
        {
            try
            {
                var remoteActive = App.RemoteControl?.ControllerConnected == true;
                var originalVis = remoteActive ? Visibility.Collapsed : Visibility.Visible;
                var emoteVis = remoteActive ? Visibility.Visible : Visibility.Collapsed;

                if (MenuItemEngine != null) MenuItemEngine.Visibility = originalVis;
                if (MenuItemTriggerMode != null) MenuItemTriggerMode.Visibility = originalVis;
                if (MenuItemBambiTakeover != null) MenuItemBambiTakeover.Visibility = originalVis;
                if (MenuItemPersonality != null) MenuItemPersonality.Visibility = originalVis;
                if (MenuItemMute != null) MenuItemMute.Visibility = originalVis;

                var emoteItems = new[] { MenuItemEmote1, MenuItemEmote2, MenuItemEmote3, MenuItemEmote4, MenuItemEmote5 };
                foreach (var mi in emoteItems)
                {
                    if (mi != null) mi.Visibility = emoteVis;
                }

                if (!remoteActive) return;

                var presets = App.Settings?.Current?.RemoteEmotePresets;
                if (presets == null) return;

                for (int i = 0; i < emoteItems.Length && i < presets.Count; i++)
                {
                    var mi = emoteItems[i];
                    if (mi == null) continue;
                    var p = presets[i];
                    var icon = string.IsNullOrEmpty(p.Icon) ? "" : p.Icon + "  ";
                    mi.Header = icon + (p.Text ?? "");
                    mi.Tag = p;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Avatar] Emote menu refresh failed");
            }
        }

        private async void MenuItemEmote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not Models.EmotePreset preset) return;
            if (string.IsNullOrWhiteSpace(preset.Text)) return; // label-less slot — silent no-op
            // Route through MainWindow.SendEmoteAndReportAsync so this surface picks up
            // the centralized avatar speech-bubble feedback (step 3.6). Null status target
            // means inline status text is skipped — appropriate for a context menu that
            // closes on click. Rate-limit / session-ended errors remain silent here.
            if (_parentWindow is MainWindow mw)
            {
                await mw.SendEmoteAndReportAsync(preset.Text, preset.Icon ?? "", "preset", null);
            }
            else if (App.RemoteControl != null)
            {
                // Fallback: parent isn't MainWindow (shouldn't happen, but be defensive).
                await App.RemoteControl.SendEmoteAsync(preset.Text, preset.Icon ?? "", "preset");
            }
        }

        /// <summary>
        /// Shows a speech bubble for emote feedback. isPending=true renders "Sending...";
        /// isPending=false renders Sent: "<text>" (text truncated to 40 chars).
        ///
        /// Behavior:
        ///   - Skips silently if the avatar is currently waiting on / showing an AI bubble
        ///     (don't fight the conversational surface).
        ///   - Otherwise interrupts any in-flight preset speech and shows immediately.
        ///   - Does NOT add to chat history (this is transient remote-emote feedback,
        ///     not a conversational turn).
        ///   - Plays no audio (preset source but suppressed sound).
        ///   - Uses Dispatcher under the hood — safe from any thread but expected to be
        ///     called from the UI thread (SendEmoteAndReportAsync runs on UI thread).
        /// </summary>
        public void ShowEmoteFeedback(string text, bool isPending)
        {
            try
            {
                RunOnAvatar(() =>
                {
                    // Don't fight the AI bubble or interrupt a pending AI response.
                    if (_isWaitingForAi || _isShowingAiBubble) return;

                    var safe = (text ?? "").Trim();
                    if (safe.Length > 40) safe = safe.Substring(0, 40) + "...";
                    var content = isPending ? "Sending..." : $"Sent: \"{safe}\"";

                    // Clear any in-flight preset speech so the bubble updates instantly.
                    _speechTimer?.Stop();
                    _speechDelayTimer?.Stop();
                    _speechQueue.Clear();
                    _isGiggling = false;

                    ShowGiggle(content, playSound: false, source: SpeechSource.Preset);

                    // Bump the bubble lifetime by 1 second. The default
                    // (App.Settings.BubbleDurationSeconds, ~2s) is tuned for
                    // ambient preset speech but feels too fast for transient
                    // "Sending..." / "Sent: ..." emote feedback. ShowGiggle
                    // has just called _speechTimer.Start() with no elapsed
                    // time, so setting Interval to (current + 1s) cleanly
                    // extends the lifetime by exactly 1 second.
                    if (_speechTimer != null)
                    {
                        _speechTimer.Interval = _speechTimer.Interval + TimeSpan.FromSeconds(1);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Avatar] ShowEmoteFeedback failed");
            }
        }

        private void MenuItemDetach_Click(object sender, RoutedEventArgs e)
        {
            Detach();
        }

        private void MenuItemAttach_Click(object sender, RoutedEventArgs e)
        {
            // Show and activate the parent window first. These touch the MAIN window's UI, so run
            // them on the parent's own dispatcher (own-thread mode: the avatar lives on another thread).
            if (_parentWindow != null)
            {
                void ShowParent()
                {
                    _parentWindow.Show();
                    _parentWindow.WindowState = WindowState.Normal;
                    _parentWindow.Activate();
                }
                if (_parentWindow.Dispatcher.CheckAccess()) ShowParent();
                else _parentWindow.Dispatcher.BeginInvoke(new Action(ShowParent));
            }

            Attach();
        }

        private void MenuItemShrink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isAttached && _currentScale > MinScale)
                {
                    _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);
                    ApplyScale();
                    UpdateResizeMenuState();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Menu shrink error: {Error}", ex.Message);
            }
        }

        private void MenuItemGrow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isAttached && _currentScale < MaxScale)
                {
                    _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
                    ApplyScale();
                    UpdateResizeMenuState();
                    // Clamp position after resize to keep avatar visible
                    Dispatcher.BeginInvoke(new Action(ClampAvatarPosition), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Menu grow error: {Error}", ex.Message);
            }
        }

        private void MenuItemEngine_Click(object sender, RoutedEventArgs e)
        {
            if (_parentWindow is MainWindow mainWindow)
            {
                // Mirror BtnStart_Click's guards. This menu used to call StopEngine()
                // directly, so a right-click on the avatar could kill the engine during
                // lockdown or a strict-locked video — defeating the lock and crashing
                // the app in the mid-playback LibVLC teardown (#479).
                if (App.RemoteControl?.ControllerConnected == true) return;

                // Use Flash.IsRunning as proxy for engine state
                if (App.Flash?.IsRunning == true)
                {
                    if (IsEngineStopLocked())
                    {
                        Giggle("nuh-uh~ no stopping now, you're locked in~");
                        return;
                    }
                    mainWindow.StopEngine();
                    Giggle("Engine stopped~");
                }
                else
                {
                    mainWindow.StartEngine();
                    Giggle("Engine started! *giggles*");
                }
                UpdateQuickMenuState();
            }
        }

        /// <summary>
        /// True when stopping the engine must be refused: lockdown mode, a strict-locked
        /// mandatory video, or a strict-locked bubble-count game is in progress (#479).
        /// </summary>
        private static bool IsEngineStopLocked() =>
            App.Lockdown?.IsActive == true ||
            App.Video?.IsStrictActive == true ||
            (App.BubbleCount?.IsBusy == true && App.Settings?.Current?.BubbleCountStrictLock == true);

        private void MenuItemTriggerMode_Click(object sender, RoutedEventArgs e)
        {
            var current = App.Settings?.Current?.TriggerModeEnabled ?? false;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.TriggerModeEnabled = !current;
                App.Settings.Save();
                RestartTriggerTimer();
                UpdateQuickMenuState();

                // Sync MainWindow UI
                if (_parentWindow is MainWindow mainWindow)
                {
                    mainWindow.SyncTriggerModeUI(!current);
                }

                Giggle(!current ? "Trigger mode ON~" : "Trigger mode off~");
            }
        }

        private void MenuItemBambiTakeover_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Check Patreon requirement
            if (App.Patreon?.HasPremiumAccess != true)
            {
                Giggle("This is Patreon only~");
                return;
            }

            // Lockdown mode: a RUNNING Takeover cannot be disabled while locked in (#514).
            if (settings.AutonomyModeEnabled && App.Autonomy?.IsEnabled == true && App.Lockdown?.IsActive == true)
            {
                Giggle("Lockdown is active~ you can't turn me off~");
                return;
            }

            // Auto-grant consent when enabling from avatar menu
            // (user is explicitly choosing to enable, so consent is implied)
            if (!settings.AutonomyConsentGiven)
            {
                settings.AutonomyConsentGiven = true;
            }

            var current = settings.AutonomyModeEnabled;
            settings.AutonomyModeEnabled = !current;
            App.Settings.Save();

            // Start/stop autonomy service
            if (!current)
            {
                App.Autonomy?.Start();
                Giggle(App.Mods?.GetAutonomyOnPhrase() ?? "Bambi takes over~ *giggles*");
            }
            else
            {
                App.Autonomy?.Stop();
                Giggle("Takeover mode off~");
            }

            // Sync main window checkbox
            App.Logger?.Information("AvatarTubeWindow: Syncing checkbox, _parentWindow type={Type}, !current={NewValue}",
                _parentWindow?.GetType().Name ?? "null", !current);
            if (_parentWindow is MainWindow mainWindow)
            {
                App.Logger?.Information("AvatarTubeWindow: Calling SyncAutonomyCheckbox({NewValue})", !current);
                mainWindow.SyncAutonomyCheckbox(!current);
            }
            else
            {
                App.Logger?.Warning("AvatarTubeWindow: _parentWindow is not MainWindow!");
            }

            UpdateQuickMenuState();
        }

        private void MenuItemTalkToBambi_Click(object sender, RoutedEventArgs e)
        {
            // Show input panel for user to type to companion
            ShowInputPanel();
        }

        /// <summary>
        /// Populates the personality submenu with all available presets.
        /// Shows "Custom Prompt" indicator when custom prompts are active.
        /// </summary>
        private void PopulatePersonalityMenu()
        {
            MenuItemPersonality.Items.Clear();

            // Check if custom prompt is active
            var customPromptActive = App.Settings?.Current?.CompanionPrompt?.UseCustomPrompt == true;

            // Dark background for submenu items
            var darkBg = (SolidColorBrush)Application.Current.Resources["PanelBgBrush"];

            if (customPromptActive)
            {
                // Show custom prompt indicator
                MenuItemPersonality.Header = Loc.Get("label_personality_custom_prompt");
                MenuItemPersonality.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange for custom

                // Add info item
                var infoItem = new MenuItem
                {
                    Header = Loc.Get("menu_custom_prompt_active"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                    Background = darkBg,
                    IsEnabled = false
                };
                MenuItemPersonality.Items.Add(infoItem);

                // Add separator
                MenuItemPersonality.Items.Add(new Separator { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")) });

                // Add option to disable custom prompt
                var disableItem = new MenuItem
                {
                    Header = Loc.Get("menu_disable_custom_prompt"),
                    Foreground = new SolidColorBrush(Colors.White),
                    Background = darkBg
                };
                disableItem.Click += (s, e) =>
                {
                    if (App.Settings?.Current?.CompanionPrompt != null)
                    {
                        App.Settings.Current.CompanionPrompt.UseCustomPrompt = false;
                        App.Settings.Save();
                        UpdateQuickMenuState();
                        Giggle(Loc.Get("avatar_back_to_presets"));
                    }
                };
                MenuItemPersonality.Items.Add(disableItem);

                return;
            }

            // Normal preset menu
            MenuItemPersonality.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")); // Pink default

            // Slut Mode used to be its own preset in this list, but it now lives as a
            // toggle on the Companion tab that swaps the active preset's personality
            // text with its SlutModePersonality variant. Filter the legacy preset out.
            var presets = (App.Personality?.GetAllPresets() ?? new List<PersonalityPreset>())
                .Where(p => p.Id != PersonalityPresets.SlutModeId)
                .ToList();
            var activeId = App.Settings?.Current?.ActivePersonalityPresetId ?? PersonalityPresets.BambiSpriteId;

            foreach (var preset in presets)
            {
                var menuItem = new MenuItem
                {
                    Header = GetPersonalityMenuHeader(preset, activeId),
                    Tag = preset.Id,
                    Background = darkBg,
                    Foreground = preset.Id == activeId
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")) // Pink for active
                        : new SolidColorBrush(Colors.White)
                };

                menuItem.Click += PersonalityMenuItem_Click;
                MenuItemPersonality.Items.Add(menuItem);
            }

            // Update parent menu header with mode-aware name
            var activePreset = App.Personality?.GetActivePreset();
            var displayName = App.Mods?.GetPersonalityDisplayName(activePreset?.Name ?? "BambiSprite") ?? activePreset?.Name ?? "BambiSprite";
            MenuItemPersonality.Header = Loc.GetF("avatar_personality_format", displayName);
        }

        private string GetPersonalityMenuHeader(PersonalityPreset preset, string activeId)
        {
            var check = preset.Id == activeId ? "☑" : "☐";
            var displayName = App.Mods?.GetPersonalityDisplayName(preset.Name) ?? preset.Name;
            return $"{check} {displayName}";
        }

        private void PersonalityMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string presetId)
            {
                var preset = App.Personality?.GetPresetById(presetId);
                if (preset == null) return;

                // CCBill AI Addendum: gate explicit presets behind an age + content-policy
                // acknowledgement dialog. The SlutMode state used for the rule check is the
                // current value (we're not flipping it here, only selecting a preset).
                var slutModeOn = App.Settings?.Current?.SlutModeEnabled == true;
                if (Services.ExplicitContentGate.RequiresAcknowledgement(preset, slutModeOn))
                {
                    var promptSettings = App.Settings?.Current?.CompanionPrompt;
                    if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(promptSettings))
                    {
                        var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                        var ok = dlg.ShowDialog() == true;
                        if (!ok) return; // Cancel: revert (no-op, since we hadn't switched yet).
                        if (promptSettings != null)
                        {
                            Services.ExplicitContentGate.MarkAcknowledged(promptSettings);
                            App.Settings?.Save();
                        }
                    }
                }

                // Set the new personality
                if (App.Personality?.SetActivePreset(presetId) == true)
                {
                    UpdateQuickMenuState();
                    // Use the mod-aware display name (the menu header already does this via
                    // GetPersonalityDisplayName) so the confirmation bubble shows e.g. "Circe"
                    // instead of the raw base preset name "BambiSprite" under the Locked mod.
                    var shownName = App.Mods?.GetPersonalityDisplayName(preset.Name) ?? preset.Name;
                    var confirm = $"Now using {shownName}~ *giggles*";
                    Giggle(App.Mods?.MakeModAware(confirm) ?? confirm);
                }
            }
        }

        private void MenuItemMute_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            UpdateQuickMenuState();

            // Mute silences her VOICE only — do NOT collapse the speech bubble (#445). Any in-flight
            // bubble stays; future bubbles still show (audio is gated in ShowGiggle).

            // Persist to settings
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.AvatarMuted = _isMuted;
                App.Settings.Save();
            }

            // Sync to MainWindow UI
            if (_parentWindow is MainWindow mainWindow)
            {
                mainWindow.SyncQuickControlsUI(muteAvatar: _isMuted);
            }
        }

        /// <summary>
        /// Sets the avatar's mute state from an external caller (the "mute"/"unmute" voice commands)
        /// and refreshes the quick-menu labels — mirrors what <see cref="MenuItemMute_Click"/> does on a
        /// manual toggle, minus the settings write (the caller owns that). UpdateQuickMenuState also
        /// re-reads SubAudioEnabled, so the "mute whispers" menu label refreshes here too.
        /// </summary>
        public void ApplyMuteState(bool avatarMuted)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ApplyMuteState(avatarMuted))); return; }
            _isMuted = avatarMuted;
            // Mute is audio-only; keep the speech bubble visible (#445).
            UpdateQuickMenuState();
        }

        private void MenuItemMuteWhispers_Click(object sender, RoutedEventArgs e)
        {
            // Toggle SubAudioEnabled setting (mute = disabled)
            var currentEnabled = App.Settings?.Current?.SubAudioEnabled ?? false;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioEnabled = !currentEnabled;
                App.Settings.Save();
            }

            UpdateQuickMenuState();

            // Sync to MainWindow UI (Settings tab and Companion tab)
            if (_parentWindow is MainWindow mainWindow)
            {
                mainWindow.SyncWhispersUI(!currentEnabled);
            }
        }

        private async void MenuItemPauseBrowser_Click(object sender, RoutedEventArgs e)
        {
            _isBrowserPaused = !_isBrowserPaused;

            try
            {
                // Access the browser through MainWindow
                if (_parentWindow is MainWindow mainWindow)
                {
                    var webView = mainWindow.GetBrowserWebView();
                    if (webView?.CoreWebView2 != null)
                    {
                        if (_isBrowserPaused)
                        {
                            // Mute browser audio using WebView2's IsMuted property
                            webView.CoreWebView2.IsMuted = true;
                            // Also try to pause any playing audio/video elements
                            await webView.CoreWebView2.ExecuteScriptAsync(@"
                                document.querySelectorAll('audio, video').forEach(el => el.pause());
                            ");
                        }
                        else
                        {
                            // Unmute browser and resume
                            webView.CoreWebView2.IsMuted = false;
                            await webView.CoreWebView2.ExecuteScriptAsync(@"
                                document.querySelectorAll('audio, video').forEach(el => el.play());
                            ");
                        }
                    }

                    // Sync to MainWindow UI
                    mainWindow.SyncQuickControlsUI(pauseBrowser: _isBrowserPaused);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to toggle browser audio: {Error}", ex.Message);
            }

            UpdateQuickMenuState();
        }

        /// <summary>
        /// Updates the quick menu items to reflect current state
        /// </summary>
        public void UpdateQuickMenuState()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(UpdateQuickMenuState)); return; }
            // Talk to companion - mode-aware label
            var talkToLabel = App.Mods?.GetTalkToLabel() ?? Loc.Get("menu_talk_to_companion");
            var chatAvailable = App.Ai?.IsAvailable == true;
            MenuItemTalkToBambi.IsEnabled = chatAvailable;
            if (chatAvailable)
            {
                MenuItemTalkToBambi.Header = Loc.GetF("menu_talk_to_format", talkToLabel);
                MenuItemTalkToBambi.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")); // Pink
            }
            else
            {
                MenuItemTalkToBambi.Header = Loc.GetF("menu_talk_to_locked_format", talkToLabel);
                MenuItemTalkToBambi.Foreground = new SolidColorBrush(Color.FromRgb(155, 89, 182)); // Purple for Patreon
            }

            // Engine state (use Flash.IsRunning as proxy)
            var engineRunning = App.Flash?.IsRunning == true;
            MenuItemEngine.Header = engineRunning ? Loc.Get("menu_stop_engine") : Loc.Get("menu_start_engine");
            MenuItemEngine.Foreground = engineRunning ? new SolidColorBrush(Color.FromRgb(255, 99, 71)) : new SolidColorBrush(Color.FromRgb(144, 238, 144));

            // Trigger mode
            var triggerOn = App.Settings?.Current?.TriggerModeEnabled == true;
            MenuItemTriggerMode.Header = triggerOn ? Loc.Get("menu_trigger_mode_on") : Loc.Get("menu_trigger_mode_off");
            MenuItemTriggerMode.Foreground = triggerOn ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) : new SolidColorBrush(Colors.White);

            // Takeover (Patreon only) - mode-aware name
            var takeoverAvailable = App.Patreon?.HasPremiumAccess == true;
            var takeoverOn = App.Settings?.Current?.AutonomyModeEnabled == true;
            var takeoverName = App.Mods?.GetTakeoverLabel() ?? Loc.Get("menu_takeover");
            MenuItemBambiTakeover.Header = takeoverOn ? Loc.GetF("menu_takeover_on_format", takeoverName) : Loc.GetF("menu_takeover_off_format", takeoverName);
            MenuItemBambiTakeover.Foreground = takeoverOn ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")) : new SolidColorBrush(Colors.White);
            MenuItemBambiTakeover.IsEnabled = takeoverAvailable;
            if (!takeoverAvailable)
            {
                MenuItemBambiTakeover.Header = Loc.GetF("menu_takeover_locked_format", takeoverName);
                MenuItemBambiTakeover.Foreground = new SolidColorBrush(Color.FromRgb(155, 89, 182)); // Purple for Patreon
            }

            // Personality menu
            PopulatePersonalityMenu();

            // Mute avatar
            MenuItemMute.Header = _isMuted ? Loc.Get("menu_mute_avatar_on") : Loc.Get("menu_mute_avatar_off");
            MenuItemMute.Foreground = _isMuted ? new SolidColorBrush(Color.FromRgb(255, 99, 71)) : new SolidColorBrush(Colors.White);

            // Mute whispers (inverted - muted when SubAudioEnabled is false)
            var whispersMuted = App.Settings?.Current?.SubAudioEnabled != true;
            MenuItemMuteWhispers.Header = whispersMuted ? Loc.Get("menu_mute_whispers_on") : Loc.Get("menu_mute_whispers_off");
            MenuItemMuteWhispers.Foreground = whispersMuted ? new SolidColorBrush(Color.FromRgb(255, 99, 71)) : new SolidColorBrush(Colors.White);

            // Pause browser
            MenuItemPauseBrowser.Header = _isBrowserPaused ? Loc.Get("menu_resume_browser") : Loc.Get("menu_pause_browser");
            MenuItemPauseBrowser.Foreground = _isBrowserPaused ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) : new SolidColorBrush(Colors.White);

            // Lock most options when remote controlled (keep talk, attach/detach, resize)
            if (App.RemoteControl?.ControllerConnected == true)
            {
                var lockedBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x70));
                MenuItemEngine.IsEnabled = false;
                MenuItemEngine.Header = Loc.Get("label_start_engine");
                MenuItemEngine.Foreground = lockedBrush;
                MenuItemTriggerMode.IsEnabled = false;
                MenuItemTriggerMode.Foreground = lockedBrush;
                MenuItemBambiTakeover.IsEnabled = false;
                MenuItemBambiTakeover.Foreground = lockedBrush;
                MenuItemPersonality.IsEnabled = false;
                MenuItemPersonality.Foreground = lockedBrush;
                MenuItemMute.IsEnabled = false;
                MenuItemMute.Foreground = lockedBrush;
                MenuItemMuteWhispers.IsEnabled = false;
                MenuItemMuteWhispers.Foreground = lockedBrush;
                MenuItemPauseBrowser.IsEnabled = false;
                MenuItemPauseBrowser.Foreground = lockedBrush;
            }
            else
            {
                // Remote controller disconnected: re-enable everything the lock block disables.
                // Without this the items stay stuck un-clickable after exiting remote control, because
                // the normal section above only refreshes their Header/Foreground, never IsEnabled.
                // (Foreground is already restored above; Takeover stays gated on Patreon access.)
                // Engine item stays disabled while a stop would be refused anyway
                // (lockdown / strict-locked video or bubble game, #479) — but only
                // when the engine is running; starting is always allowed.
                MenuItemEngine.IsEnabled = !(App.Flash?.IsRunning == true && IsEngineStopLocked());
                MenuItemTriggerMode.IsEnabled = true;
                MenuItemBambiTakeover.IsEnabled = takeoverAvailable;
                MenuItemPersonality.IsEnabled = true;
                MenuItemMute.IsEnabled = true;
                MenuItemMuteWhispers.IsEnabled = true;
                MenuItemPauseBrowser.IsEnabled = true;
            }
        }

        /// <summary>
        /// Gets whether the avatar is currently muted
        /// </summary>
        public bool IsMuted => _isMuted;

        /// <summary>
        /// Set mute avatar state from MainWindow
        /// </summary>
        public void SetMuteAvatar(bool isMuted)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetMuteAvatar(isMuted))); return; }
            _isMuted = isMuted;
            // Mute is audio-only; keep the speech bubble visible (#445).
            UpdateQuickMenuState();
        }

        /// <summary>
        /// Set mute whispers state from MainWindow (toggles SubAudioEnabled)
        /// </summary>
        public void SetMuteWhispers(bool isMuted)
        {
            // isMuted = true means disable whispers (SubAudioEnabled = false)
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SubAudioEnabled = !isMuted;
                App.Settings.Save();
            }
            UpdateQuickMenuState();
        }

        /// <summary>
        /// Refreshes the personality menu to reflect current selection.
        /// Called when personality changes from another source.
        /// </summary>
        public void RefreshPersonalityMenu()
        {
            UpdateQuickMenuState();
        }

        /// <summary>
        /// Set browser paused state from MainWindow (just updates UI, MainWindow handles actual browser control)
        /// </summary>
        public void SetBrowserPaused(bool isPaused)
        {
            _isBrowserPaused = isPaused;
            UpdateQuickMenuState();
        }
    }
}
