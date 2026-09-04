using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Lock Card window — the user must type (or speak) a phrase a number of times before the
    /// cover comes off the screen.
    ///
    /// PORTED from ConditioningControlPanel/Windows/LockCardWindow.xaml.cs. What survives is
    /// everything that only touches this view: the phrase/repeat state machine, the #734 anti-cheat
    /// gate, the progress bar, the encouragement line, the pulse/shake feedback and the voice
    /// panel's visuals. What does NOT survive, and why:
    ///
    ///  - <b>Every Win32 call is gone.</b> <c>SetWindowPos(HWND_TOPMOST)</c> is the XAML's
    ///    <c>Topmost="True"</c>; <c>SetForegroundWindow</c> is <c>Activate()</c>;
    ///    <c>WS_EX_TOOLWINDOW</c> is <c>ShowInTaskbar="False"</c>;
    ///    <c>SetLayeredWindowAttributes</c> + <c>AllowsTransparency</c> is
    ///    <c>TransparencyLevelHint="Transparent"</c> + <c>SystemDecorations="None"</c>.
    ///    <c>DisableProcessWindowsGhosting</c>, <c>WindowInteropHelper</c>/<c>HwndSource</c> and the
    ///    <c>WM_DPICHANGED</c> swallow-hook are dropped outright — see the losses listed on
    ///    <see cref="ShowOnAllMonitors"/>.
    ///  - <b>The multi-monitor cover set is real</b> — <c>DualMonitorEnabled</c> from
    ///    <c>CoreSettings</c>, screens from <c>ScreenList.Enumerate</c>, the primary owning the
    ///    keyboard and the rest mirroring it. The <b>pool and the dead-man's switch are not</b>:
    ///    both are hwnds and a background thread force-dropping a layered window at the Win32
    ///    level, so a wedged UI thread here leaves the cover up. See <see cref="ShowOnAllMonitors"/>.
    ///  - <b>Settings are wired</b>: the five per-user card colours and the panic-key name come
    ///    from <c>CoreSettings.Current</c>, with the mod pack's accent from <c>CoreMods</c>, so a
    ///    recoloured card draws correctly here.
    ///  - <b>Services still in the WPF head</b>: the speech LISTEN loop (CoreSpeech carries
    ///    capability only, never a RecognizePhraseAsync), App.Autonomy (mic hand-off),
    ///    App.Progression / App.Achievements / App.LockCard (XP, achievements, completion notify)
    ///    and App.PanicHook (no global keyboard hook exists on this head at all).
    ///
    /// Placeholder session state is applied in the constructor so the headless render shows a live
    /// card rather than an empty one.
    /// </summary>
    public partial class LockCardWindow : Window
    {
        // ponytail: needs ConditioningControlPanel/Services/LockCard/LockCardService.cs, which is
        // pinned to the head by App.* and the WPF window itself. Placeholder card so the render
        // (and any manual run) shows the real layout with real strings.
        private const int SampleRepeats = 3;

        /// <summary>
        /// The visible set. Drives <see cref="IsAnyOpen"/> (which BubbleCountResultWindow's mercy
        /// flow polls every 500 ms), the mirror sync and every "all windows" fan-out below.
        /// Populated by <see cref="ShowOnAllMonitors"/> only - NOT by the constructor, or a
        /// --render-all pass would leave phantom cards in it and IsAnyOpen would lie.
        /// </summary>
        private static readonly List<LockCardWindow> _allWindows = new();

        /// <summary>Exactly one card owns the keyboard; every other monitor is a read-only mirror
        /// that echoes what the primary types.</summary>
        private bool _isPrimary = true;

        /// <summary>A test card awards no XP and notifies no service - WPF's own gate.</summary>
        private bool _isTest;

        // Per-session config (mutable: the WPF original pooled windows and reconfigured on reuse).
        private string _phrase = "";
        private int _requiredRepeats;
        private bool _strictMode;
        private bool _voiceMode;
        private int _completedRepeats;
        private bool _isCompleted;
        private DispatcherTimer? _closeTimer;

        // ── Anti-cheat: the phrase must actually be TYPED (#734) ───────────────
        // Blocking the clipboard keys alone was never enough, and undo was the bigger hole:
        // RegisterSuccessfulRepeat clears the box, so Ctrl+Z put the whole phrase straight back and
        // re-fired the match. Belt and braces: the input is hardened (no paste, no undo, no
        // clipboard gestures) AND a repeat is only accepted once the user has produced at least as
        // many characters as the phrase is long.
        private int _keystrokes;          // characters genuinely entered since the last accepted repeat
        private int _lastInputLength;     // previous TxtInput.Text length, for the growth fail-safe
        private bool _sawTextInput;       // the text-input handler already credited this change

        // Achievement tracking (kept so the counters the stubbed services want are already correct).
        private DateTime _startTime;
        private int _totalErrors;
        private int _totalCharsTyped;

        // WPF named nine SolidColorBrushes and a ScaleTransform directly. Avalonia only name-scopes
        // StyledElements, so each is reached through the control that owns it.
        private readonly TextBlock _txtTitle, _txtPhrase, _txtVoiceState, _txtVoiceHeard,
                                   _txtProgress, _txtStrict, _txtHint, _txtEscHint;
        private readonly TextBox _txtInput;
        private readonly Border _cardBorder, _inputBorder, _voiceLevelFill,
                                _progressBar, _progressBarContainer;
        private readonly StackPanel _voicePanel, _completionPanel;
        private readonly Grid _mainGrid;

        public LockCardWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _mainGrid = this.FindControl<Grid>("MainGrid")!;
            _cardBorder = this.FindControl<Border>("CardBorder")!;
            _txtTitle = this.FindControl<TextBlock>("TxtTitle")!;
            _txtPhrase = this.FindControl<TextBlock>("TxtPhrase")!;
            _inputBorder = this.FindControl<Border>("InputBorder")!;
            _txtInput = this.FindControl<TextBox>("TxtInput")!;
            _voicePanel = this.FindControl<StackPanel>("VoicePanel")!;
            _txtVoiceState = this.FindControl<TextBlock>("TxtVoiceState")!;
            _voiceLevelFill = this.FindControl<Border>("VoiceLevelFill")!;
            _txtVoiceHeard = this.FindControl<TextBlock>("TxtVoiceHeard")!;
            _txtProgress = this.FindControl<TextBlock>("TxtProgress")!;
            _progressBarContainer = this.FindControl<Border>("ProgressBarContainer")!;
            _progressBar = this.FindControl<Border>("ProgressBar")!;
            _txtStrict = this.FindControl<TextBlock>("TxtStrict")!;
            _txtHint = this.FindControl<TextBlock>("TxtHint")!;
            _completionPanel = this.FindControl<StackPanel>("CompletionPanel")!;
            _txtEscHint = this.FindControl<TextBlock>("TxtEscHint")!;

            // ── Input hardening (#734) ─────────────────────────────────────────
            // Kills every paste route at the source — Ctrl+V, Shift+Insert, the context menu and
            // drag-drop. Programmatic Text sets do NOT raise it, so a mirror sync is unaffected.
            _txtInput.AddHandler(TextBox.PastingFromClipboardEvent, (_, e) =>
            {
                e.Handled = true;
                RejectCheat("paste");
            });

            // No undo stack ⇒ Ctrl+Z can't resurrect the phrase RegisterSuccessfulRepeat cleared.
            _txtInput.IsUndoEnabled = false;

            // WPF's PreviewKeyDown / PreviewTextInput are Avalonia's tunnelling KeyDown / TextInput.
            // Both MUST tunnel: the bubbling versions run after TextBox's own handling, which is
            // exactly the bug #734 fixed on the WPF side.
            _txtInput.AddHandler(KeyDownEvent, TxtInput_PreviewKeyDown, RoutingStrategies.Tunnel);
            _txtInput.AddHandler(TextInputEvent, (_, e) =>
            {
                _keystrokes += Math.Max(1, e.Text?.Length ?? 1);
                _sawTextInput = true;
            }, RoutingStrategies.Tunnel);

            _txtInput.TextChanged += (_, _) => TxtInput_TextChanged();
            KeyDown += (_, e) => Window_KeyDown(e);
            Loaded += (_, _) => OnShown();

            Configure(Loc.Get("label_good_girls_obey"), SampleRepeats, strictMode: false, voiceMode: false);
        }

        /// <summary>
        /// Bind a TextBlock to a localization key rather than assigning <c>.Text</c>. Avalonia keeps
        /// the XAML's {loc:Str} binding alive under a local value, so a plain assignment is undone
        /// on the next language change (see CLAUDE.md). Same binding the markup extension builds.
        /// </summary>
        private static void SetLocalized(TextBlock target, string key) =>
            target[!TextBlock.TextProperty] = new Binding($"[{key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
            };

        /// <summary>
        /// Apply per-session configuration: phrase, repeat count, strict/voice mode, and reset every
        /// transient piece of card state. The WPF original also positioned the window on a specific
        /// <c>System.Windows.Forms.Screen</c> in device pixels via <c>SetWindowPos</c>; here the
        /// window is simply <c>WindowState="Maximized"</c> on its screen.
        /// </summary>
        private void Configure(string phrase, int repeats, bool strictMode, bool voiceMode)
        {
            _closeTimer?.Stop();
            _closeTimer = null;
            _completedRepeats = 0;
            _isCompleted = false;
            ResetKeystrokeGate();

            _phrase = phrase;
            _requiredRepeats = repeats;
            _strictMode = strictMode;
            // The capability half is answerable now (CoreSpeech.IsAvailable, MicConsentGiven) but
            // the answer would be a lie: CoreSpeech is a capability seam with no listen call, so a
            // voice card that said yes would trap the user behind a mic that never replies. Voice
            // stays off until a recognition seam exists, not until settings move.
            // ponytail: needs a CoreSpeech listen call (RecognizePhraseAsync / LevelChanged /
            // PartialTranscript) plus App.Autonomy for the mic hand-off.
            _voiceMode = false;
            if (voiceMode)
                Log.Information("LockCardWindow: voice mode requested but no recognition seam on this head — falling back to typing");

            _txtPhrase.Text = phrase;

            // Clear any pulse/shake transform and reset input + panels to the fresh (unsolved) look.
            _cardBorder.RenderTransform = null;
            _txtInput.IsEnabled = true;
            _txtInput.Clear();
            _completionPanel.IsVisible = false;
            _txtHint.IsVisible = true;
            _txtHint.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            _txtVoiceHeard.Text = "I heard: …";

            // Swap the input affordance for the voice panel when solving by voice.
            if (_voiceMode)
            {
                _inputBorder.IsVisible = false;
                _voicePanel.IsVisible = true;
                _txtTitle.Text = "SAY IT TO UNLOCK";
                SetVoiceStateColor(VoicePink);
                SetVoiceLevel(0);
            }
            else
            {
                _inputBorder.IsVisible = true;
                _voicePanel.IsVisible = false;
                SetLocalized(_txtTitle, "label_type_to_unlock_2");
            }

            UpdateProgress();

            if (_strictMode) SetLocalized(_txtStrict, "label_strict");
            else _txtStrict.Text = "";
            RefreshEscHint();

            // Exactly one card owns the keyboard and every other monitor mirrors it, as in WPF.
            ApplyInputAffordance();
            ApplyColors();
        }

        /// <summary>
        /// Input affordance for this card. Exactly one card (the primary) owns the keyboard; every
        /// other monitor is a read-only mirror that echoes what the primary types.
        /// </summary>
        private void ApplyInputAffordance()
        {
            if (!_isPrimary)
            {
                _txtInput.IsReadOnly = true;
                _txtInput.Focusable = false;
                SetLocalized(_txtHint, "label_input_synced_from_primary_monitor");
                if (_voiceMode) _txtVoiceState.Text = "🎤 Speak on the main monitor";
                return;
            }

            _txtInput.IsReadOnly = false;
            _txtInput.Focusable = true;
            if (_voiceMode)
            {
                _txtVoiceState.Text = "🎤 Listening…";
                _txtHint.Text = "Say the phrase out loud, clearly.";
            }
            else SetLocalized(_txtHint, "label_type_the_phrase_exactly_as_shown_above");
        }

        /// <summary>
        /// Give this card the caret. In voice mode there is no visible textbox, so the Window itself
        /// takes focus (keeps Esc and the key handler live); in typing mode the input does.
        /// </summary>
        private void FocusInput()
        {
            if (_voiceMode) Focus(); else _txtInput.Focus();
        }

        /// <summary>
        /// Runs on every show. <c>SetWindowPos(HWND_TOPMOST)</c> + <c>SetForegroundWindow</c> become
        /// the XAML's <c>Topmost="True"</c> plus <c>Activate()</c>; the device-pixel re-cover
        /// (ApplyPhysicalBounds) is gone with the hwnd it needed.
        /// </summary>
        private void OnShown()
        {
            _startTime = DateTime.Now;
            _totalErrors = 0;
            _totalCharsTyped = 0;

            // A mirror must never take the foreground: it would pull focus off the one card the
            // user can actually type into.
            if (_isPrimary)
            {
                Activate();
                FocusInput();
            }

            Log.Information("Lock Card shown - Phrase: {Phrase}, Repeats: {Repeats}, Strict: {Strict}, Voice: {Voice}",
                _phrase, _requiredRepeats, _strictMode, _voiceMode);
        }

        /// <summary>
        /// A strict card refuses to close until it is solved. This is where WPF's Alt+F4 defence
        /// actually lived: the <c>KeyDown</c> branch only covered the in-process key, while the WM's
        /// own close gesture (titlebar, Alt+F4, a task switcher) arrives as a close request. On X11
        /// that is <c>WM_DELETE_WINDOW</c>, which Avalonia surfaces here as a cancellable
        /// <c>Closing</c> — the same hook, so strict mode keeps its teeth off Windows.
        ///
        /// Deregistration from the visible set happens in <see cref="OnClosed"/> rather than here,
        /// since a cancelled close must not leave the set. ponytail: the WPF override also
        /// refreshed the watchdog snapshot, un-poisoned its hwnd, pulled the card out of the
        /// keep-alive pool and released App.InteractionQueue's LockCard slot (#462) — all four are
        /// Win32 or service, see <see cref="ShowOnAllMonitors"/>.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // In strict mode, only allow closing if completed.
            if (_strictMode && !_isCompleted)
            {
                e.Cancel = true;
                ShakeCard();
                return;
            }

            _closeTimer?.Stop();
            _voiceMode = false;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // --render-all closes the window externally and a live tick would outlive the view in
            // that shared process.
            _closeTimer?.Stop();
            _closeTimer = null;
            _allWindows.Remove(this);
            base.OnClosed(e);
        }

        // ── Anti-cheat ─────────────────────────────────────────────────────────

        /// <summary>
        /// Swallow the clipboard / undo gestures before the TextBox's own handling can act on them.
        /// This MUST be the TUNNELLING handler: on the bubbling one, TextBox has already run
        /// Paste/Undo and marked the event handled — that was the #734 bug.
        /// </summary>
        private void TxtInput_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (!IsBlockedInputGesture(e.Key, e.KeyModifiers)) return;

            // Auto-repeat: still swallowed, but WITHOUT the feedback. Holding Ctrl+Z fires ~30 of
            // these a second and each RejectCheat starts an animation on a fullscreen window.
            // The first press already shook the card, which is all the "no" the user needs.
            // ponytail: Avalonia's KeyEventArgs has no IsRepeat, so the throttle is by elapsed time
            // instead — same effect, one shake per burst.
            var now = DateTime.UtcNow;
            var repeat = now - _lastRejectAt < TimeSpan.FromMilliseconds(400);
            _lastRejectAt = now;

            e.Handled = true;
            if (!repeat) RejectCheat($"key {e.KeyModifiers}+{e.Key}");
        }

        private DateTime _lastRejectAt = DateTime.MinValue;

        /// <summary>
        /// Every keyboard gesture that could put text into the box without typing it, or take text
        /// out of it: clipboard (Ctrl+C/V/X, Ctrl+Insert, Shift+Insert, Shift+Delete), select-all,
        /// and undo/redo. Pure so it can be unit-tested. Escape is deliberately NOT here: it is an
        /// exit, not a way to cheat text into the box — whether it closes the card is decided once,
        /// in <see cref="Window_KeyDown"/> via <see cref="EscClosesCard"/>.
        /// </summary>
        internal static bool IsBlockedInputGesture(Key key, KeyModifiers mods)
        {
            // HasFlag rather than ==, so Ctrl+Shift+V (paste as plain text) is caught too.
            //
            // ...but NOT with Alt down: AltGr arrives as Ctrl+Alt, so on Polish, Croatian and
            // US-International layouts AltGr+{C,V,X,A,Z,Y} are how you type ć/ź/ą/ż and friends.
            // Blocking those made any phrase containing them literally unsolvable.
            if (mods.HasFlag(KeyModifiers.Control) && !mods.HasFlag(KeyModifiers.Alt) &&
                (key == Key.C || key == Key.V || key == Key.X || key == Key.A ||
                 key == Key.Z || key == Key.Y || key == Key.Insert))
                return true;

            // Legacy clipboard gestures: Shift+Insert = paste, Shift+Delete = cut.
            if (mods.HasFlag(KeyModifiers.Shift) && (key == Key.Insert || key == Key.Delete))
                return true;

            return false;
        }

        /// <summary>
        /// The semantic half of the anti-cheat: a repeat only counts once the user has produced at
        /// least as many characters as the phrase is long. Any route that fills the box in one shot
        /// (paste, undo, a drop) leaves the credit far short of this, so the match is refused.
        /// </summary>
        internal static bool HasTypedEnough(int keystrokes, int phraseLength) => keystrokes >= phraseLength;

        /// <summary>
        /// Keystroke credit for a text change the tunnelling TextInput handler did NOT account for:
        /// the full growth of the box, not a single character.
        ///
        /// The old "+1 only if it grew by exactly one" rule silently bricked every bulk-but-
        /// legitimate input route — a CJK IME commits the whole composed phrase in one change with
        /// no TextInput at all, as do voice typing and the emoji picker. The gate then saw 0
        /// keystrokes for a full-phrase match, wiped the box, and the card became unsolvable.
        ///
        /// This costs no cheat resistance: pasting is cancelled before it reaches the box, undo is
        /// off, and every blocked gesture is handled before the TextBox acts.
        /// </summary>
        internal static int CreditFailSafeGrowth(bool sawTextInput, int previousLength, int currentLength)
            => (!sawTextInput && currentLength > previousLength) ? currentLength - previousLength : 0;

        /// <summary>
        /// Visible "no" for a blocked shortcut or a rejected bulk insert. Deliberately wordless (a
        /// shake of the card) so there is no new user-facing string to localize.
        /// </summary>
        private void RejectCheat(string reason)
        {
            Log.Debug("LockCardWindow: blocked cheat attempt ({Reason})", reason);
            try { ShakeCard(); } catch { }
        }

        /// <summary>Zero the typing credit. Called wherever the input is cleared.</summary>
        private void ResetKeystrokeGate()
        {
            _keystrokes = 0;
            _lastInputLength = _txtInput?.Text?.Length ?? 0;
            _sawTextInput = false;
        }

        // ── Typing ─────────────────────────────────────────────────────────────

        private void TxtInput_TextChanged()
        {
            // A mirror's Text is set programmatically by SyncInputToAllWindows, so it is never
            // credited as typing and never judged as a match.
            if (_isCompleted || !_isPrimary) return;

            var input = _txtInput.Text ?? "";

            // Fail-safe keystroke accounting: the tunnelling TextInput handler is the counter of
            // record, but if an input method delivers text without raising it, credit the growth.
            _keystrokes += CreditFailSafeGrowth(_sawTextInput, _lastInputLength, input.Length);
            _sawTextInput = false;
            _lastInputLength = input.Length;

            _totalCharsTyped++;

            // Check for errors (input doesn't match phrase prefix)
            if (input.Length > 0)
            {
                var expectedPrefix = _phrase.Substring(0, Math.Min(input.Length, _phrase.Length));
                if (!string.Equals(input, expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _totalErrors++;
                }
            }

            SyncInputToAllWindows(input);

            if (string.Equals(input.Trim(), _phrase, StringComparison.OrdinalIgnoreCase))
            {
                // The gate lives at THIS call site only. The spoken-solve path calls
                // RegisterSuccessfulRepeat directly and must never be gated on typing.
                if (HasTypedEnough(_keystrokes, _phrase.Length))
                {
                    RegisterSuccessfulRepeat();
                }
                else
                {
                    // A full-phrase match that nobody typed: some insertion route got past the
                    // hardening above. Refuse it and make the user type it for real.
                    Log.Information(
                        "Lock Card: rejected an untyped match ({Keys} keystroke(s) for a {Len}-char phrase) (#734)",
                        _keystrokes, _phrase.Length);
                    _txtInput.Clear();
                    SyncInputToAllWindows("");
                    ResetKeystrokeGate();
                    RejectCheat("untyped match");
                }
            }
        }

        /// <summary>Shared completion step for one correct repeat. UI thread only.</summary>
        private void RegisterSuccessfulRepeat()
        {
            if (_isCompleted) return;

            _completedRepeats++;

            // Clear input for next repeat (no-op/harmless in voice mode).
            _txtInput.Clear();
            SyncInputToAllWindows("");
            // The next repeat has to be earned from scratch — the clear Ctrl+Z used to undo.
            ResetKeystrokeGate();

            var encouragement = _completedRepeats < _requiredRepeats ? GetEncouragement() : null;
            foreach (var window in Fanout())
            {
                window._completedRepeats = _completedRepeats;
                window.UpdateProgress();
                window.PulseCard();
                if (encouragement == null) continue;
                window._txtHint.Text = encouragement;
                window._txtHint.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100));
            }

            if (_completedRepeats >= _requiredRepeats) CompleteCard();
        }

        private void CompleteCard()
        {
            var completionTime = (DateTime.Now - _startTime).TotalSeconds;

            // The XP award, WPF's body verbatim including the !_isTest gate and the strict 1.5x
            // multiplier. App.Progression is CoreProgression here; the WPF call is already a
            // null-conditional fire-and-forget, and unseeded (this head today) the seam is the same
            // no-op, so restoring it cannot mis-award - it only starts working the day a head seeds
            // progression. XPSource.LockCard crosses as its member NAME: the enum is declared inside
            // Services/Companion/CompanionService.cs, which cannot move, so CoreProgression takes a
            // string and the seeding head parses it.
            if (!_isTest)
            {
                var xpAmount = (50 * _requiredRepeats) + 200;
                if (_strictMode) xpAmount = (int)(xpAmount * 1.5);
                CoreProgression.AddXP(xpAmount, "LockCard");
            }

            // ponytail: the other two calls stay stubbed and have no Core seam to reach through.
            // App.Achievements.TrackLockCardCompletion(completionTime, _totalCharsTyped,
            //   _totalErrors, _requiredRepeats) - ConditioningControlPanel/Services/Progression/AchievementService.cs
            // App.LockCard.NotifyCompleted(_phrase, _totalErrors, _requiredRepeats) (!_isTest only)
            //   - ConditioningControlPanel/Services/LockCard/LockCardService.cs
            Log.Information("Lock Card completed - {Repeats} repeats in {Time:F1}s with {Errors} errors{Test}",
                _requiredRepeats, completionTime, _totalErrors, _isTest ? " (TEST)" : "");

            foreach (var window in Fanout())
            {
                window._isCompleted = true;
                window._txtInput.IsEnabled = false;
                window._txtHint.IsVisible = false;
                window._completionPanel.IsVisible = true;
            }

            // Auto-close after delay
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer?.Stop();
                CloseAllWindows();
            };
            _closeTimer.Start();
        }

        /// <summary>
        /// The windows this one speaks for: the whole visible set (a snapshot - a fan-out can close
        /// windows), or just itself when this card was never registered, which is the design
        /// constructor's standalone window. Every "all windows" operation goes through this, so a
        /// standalone card still progresses instead of silently doing nothing.
        /// </summary>
        private List<LockCardWindow> Fanout() =>
            _allWindows.Contains(this) ? _allWindows.ToList() : new List<LockCardWindow> { this };

        /// <summary>Echo the primary's box onto every mirror. A programmatic Text set raises
        /// TextChanged but not TextInput, and the mirror's own handler returns immediately on
        /// <c>!_isPrimary</c>, so nothing here is credited as typing.</summary>
        private void SyncInputToAllWindows(string input)
        {
            foreach (var window in Fanout())
                if (window != this && !window._isCompleted)
                    window._txtInput.Text = input;
        }

        private void UpdateProgress()
        {
            _txtProgress.Text = Loc.GetF("lockcard_progress", _completedRepeats, _requiredRepeats);

            // Update progress bar width based on actual container width
            var progressPercent = _requiredRepeats > 0 ? (double)_completedRepeats / _requiredRepeats : 0;
            var maxWidth = _progressBarContainer.Bounds.Width > 0 ? _progressBarContainer.Bounds.Width : 200;
            _progressBar.Width = maxWidth * progressPercent;
        }

        private string GetEncouragement()
        {
            var remaining = _requiredRepeats - _completedRepeats;
            var messages = new[]
            {
                Loc.GetF("lockcard_encourage_1", remaining),
                Loc.GetF("lockcard_encourage_2", remaining),
                Loc.GetF("lockcard_encourage_3", remaining),
                Loc.GetF("lockcard_encourage_4", remaining),
                Loc.GetF("lockcard_encourage_5", remaining)
            };

            return messages[_completedRepeats % messages.Length];
        }

        // ── Escape ─────────────────────────────────────────────────────────────

        /// <summary>
        /// #875: is there a panic escape from this card RIGHT NOW? On WPF the panic key rides a
        /// <c>WH_KEYBOARD_LL</c> hook that can be absent while the setting says yes, so the answer
        /// was <c>PanicKeyEnabled &amp;&amp; PanicHook.IsInstalled</c>.
        /// The settings half is live now (<c>PanicKeyEnabled</c> is in Core); the hook half is not.
        /// ponytail: needs a panic-hook seam. There is no global-hook equivalent in this head at
        /// all (<c>SetWindowsHookEx</c> has no X11 twin here), so no panic escape can be live — and
        /// this falls OPEN, exactly as the WPF version does when the hook is gone. Written as the
        /// real conjunction so wiring the hook is a one-symbol change.
        /// </summary>
        private static bool PanicEscapeIsLive => CoreSettings.Current.PanicKeyEnabled && PanicHookIsInstalled;

        /// <summary>ponytail: no global keyboard hook on this head — see <see cref="PanicEscapeIsLive"/>.</summary>
        private const bool PanicHookIsInstalled = false;

        /// <summary>
        /// #875: does Esc close THIS card? Non-strict cards: yes. Strict cards: only while a panic
        /// escape is actually live, because otherwise Esc is the sole remaining exit and strict mode
        /// must never become an inescapable trap. Every failure mode here falls open, not shut.
        /// </summary>
        private bool EscClosesCard => !_strictMode || !PanicEscapeIsLive;

        /// <summary>
        /// #875: paint the bottom hint from the exit this card honours RIGHT NOW, rather than
        /// promising a key that will do nothing.
        /// </summary>
        private void RefreshEscHint()
        {
            if (_txtEscHint == null) return;
            if (EscClosesCard) SetLocalized(_txtEscHint, "label_press_esc_to_close");
            // Unreachable while PanicEscapeIsLive is false, kept — with the real key name now that
            // settings are in Core — so the branch cannot rot while the hook half is missing.
            else _txtEscHint.Text = Loc.GetF("label_strict_only_panic_key_closes", CoreSettings.Current.PanicKey);
        }

        private void Window_KeyDown(KeyEventArgs e)
        {
            // Esc closes the card unless strict mode is on AND a panic escape is genuinely live to
            // cover it. Strict means "type it out, or panic out" — never "no exit".
            if (e.Key == Key.Escape && !_isCompleted)
            {
                if (EscClosesCard)
                {
                    Log.Information("Lock Card closed via ESC (strict={Strict})", _strictMode);
                    CloseAllWindows();
                }
                else
                {
                    // Refused. The gate is live, so repaint the hint now that the user has told us
                    // they are looking for the exit.
                    RefreshEscHint();
                }
            }

            // The WPF original also swallowed Alt+F4 here via e.SystemKey. Avalonia has no
            // SystemKey and the WM eats Alt+F4 before it is ever a key event, so the block moved to
            // where the close actually arrives: OnClosing, which cancels an unsolved strict card
            // for the WM gesture AND for Alt+F4. Same defence, one hook instead of two.

            // Backstop only: covers keys pressed while the input doesn't have focus (voice mode).
            // The real block is TxtInput_PreviewKeyDown. Do NOT move the Esc branch above into a
            // tunnelling handler — it is the deliberate exit and has to stay where nothing upstream
            // can swallow it.
            if (IsBlockedInputGesture(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
            }
        }

        // ── Colours ────────────────────────────────────────────────────────────

        /// <summary>
        /// Repaint the cover, the card, the phrase, the input and the glow from the five per-user
        /// settings (LockCardBackgroundColor / TextColor / InputBackgroundColor / InputTextColor /
        /// AccentColor), falling back to the mod pack's accent exactly as WPF does.
        ///
        /// <para>WPF named nine SolidColorBrushes in its XAML and mutated <c>.Color</c> on each;
        /// Avalonia cannot name a brush (AVLN2000), so each is reached through the control that
        /// owns it and replaced wholesale. Same surfaces, same order, same fallbacks — a setting
        /// left empty lands on the literal the WPF code used, which is also what the .axaml
        /// paints, so an unconfigured card looks exactly as it did before this ran.</para>
        /// </summary>
        private void ApplyColors()
        {
            try
            {
                var settings = CoreSettings.Current;
                var modAccent = ParseColor(CoreMods.AccentColorHex, Color.FromRgb(0xFF, 0x69, 0xB4));

                var bgColor = ParseColor(settings.LockCardBackgroundColor, Color.FromRgb(26, 26, 46));
                _cardBorder.Background = new SolidColorBrush(bgColor);

                // The cover is a semi-transparent version of the card background.
                _mainGrid.Background = new SolidColorBrush(Color.FromArgb(230, bgColor.R, bgColor.G, bgColor.B));

                var textColor = ParseColor(settings.LockCardTextColor, modAccent);
                _txtPhrase.Foreground = new SolidColorBrush(textColor);
                _txtTitle.Foreground = new SolidColorBrush(textColor);

                _inputBorder.Background = new SolidColorBrush(
                    ParseColor(settings.LockCardInputBackgroundColor, Color.FromRgb(37, 37, 66)));
                _txtInput.Foreground = new SolidColorBrush(
                    ParseColor(settings.LockCardInputTextColor, Colors.White));

                var accentColor = ParseColor(settings.LockCardAccentColor, modAccent);
                _inputBorder.BorderBrush = new SolidColorBrush(accentColor);
                _progressBar.Background = new SolidColorBrush(accentColor);

                // The card glow. WPF mutated the DropShadowEffect's Color in place; so does this —
                // Color is a settable styled property on Avalonia's effect too.
                if (_cardBorder.Effect is DropShadowEffect glow) glow.Color = accentColor;
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to apply lock card colors: {Error}", ex.Message);
            }
        }

        internal static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return Color.Parse(hex);
            }
            catch
            {
                return fallback;
            }
        }

        // ── Feedback animations ────────────────────────────────────────────────

        /// <summary>
        /// WPF began a DoubleAnimation with AutoReverse on a ScaleTransform. Avalonia has no
        /// AutoReverse on a code-run Animation, so the return leg is an explicit keyframe.
        /// </summary>
        private void PulseCard()
        {
            var transform = new ScaleTransform(1, 1);
            _cardBorder.RenderTransformOrigin = RelativePoint.Center;
            _cardBorder.RenderTransform = transform;

            var anim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(300),
                Children =
                {
                    Frame(0.0, ScaleTransform.ScaleXProperty, 1.0, ScaleTransform.ScaleYProperty, 1.0),
                    Frame(0.5, ScaleTransform.ScaleXProperty, 1.05, ScaleTransform.ScaleYProperty, 1.05),
                    Frame(1.0, ScaleTransform.ScaleXProperty, 1.0, ScaleTransform.ScaleYProperty, 1.0),
                },
            };
            _ = anim.RunAsync(transform);
        }

        /// <summary>
        /// WPF: -10 → 10 over 50ms, AutoReverse, RepeatBehavior(3). Same shape here as three
        /// iterations of a 100ms -10 → 10 → -10 cycle, with the transform cleared afterwards.
        /// </summary>
        private void ShakeCard()
        {
            var transform = new TranslateTransform();
            _cardBorder.RenderTransform = transform;

            var anim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(100),
                IterationCount = new IterationCount(3),
                Easing = new LinearEasing(),
                Children =
                {
                    Frame(0.0, TranslateTransform.XProperty, -10.0),
                    Frame(0.5, TranslateTransform.XProperty, 10.0),
                    Frame(1.0, TranslateTransform.XProperty, -10.0),
                },
            };
            _ = anim.RunAsync(transform).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() => _cardBorder.RenderTransform = null));
        }

        private static KeyFrame Frame(double cue, AvaloniaProperty p, object v) => new()
        {
            Cue = new Cue(cue),
            Setters = { new Setter(p, v) },
        };

        private static KeyFrame Frame(double cue, AvaloniaProperty p1, object v1, AvaloniaProperty p2, object v2) => new()
        {
            Cue = new Cue(cue),
            Setters = { new Setter(p1, v1), new Setter(p2, v2) },
        };

        // ── Voice solve (speak the phrase) ─────────────────────────────────────
        //
        // ponytail: needs App.Speech (ISpeechService: IsAvailable / IsListening /
        // RecognizePhraseAsync / LevelChanged / PartialTranscript / StopListening) and App.Autonomy
        // (UserDrivenVoiceArmed / StopVoiceInput / RefreshVoiceInputModes) — the whole
        // RunVoiceSolveLoopAsync listen loop, the mic hand-off from the "Hey Bambi" wake/PTT owner
        // and the mid-session fallback all live with those services. What is ported is the panel's
        // visuals, so the moment the service lands the display is already correct.

        private static readonly Color VoicePink = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color VoiceGreen = Color.FromRgb(0x00, 0xE6, 0x76);
        private static readonly Color VoiceAmber = Color.FromRgb(0xF0, 0xB4, 0x29);

        private void SetVoiceLevel(double level)
        {
            if (_voiceLevelFill.RenderTransform is ScaleTransform st)
                st.ScaleX = Math.Min(1.0, Math.Max(0.0, level / 0.2)); // RMS ~0..0.2 -> full bar
        }

        private void SetVoiceHeard(string text) =>
            _txtVoiceHeard.Text = string.IsNullOrWhiteSpace(text) ? "I heard: …" : $"I heard: {text}";

        private void SetVoiceState(string text, Color color)
        {
            _txtVoiceState.Text = text;
            SetVoiceStateColor(color);
        }

        private void SetVoiceStateColor(Color color)
        {
            if (_txtVoiceState.Foreground is SolidColorBrush b) b.Color = color;
            else _txtVoiceState.Foreground = new SolidColorBrush(color);
        }

        /// <summary>Drop back to typed solve if speech dies mid-card, so the user is never stuck.</summary>
        private void FallBackToTextMidSession()
        {
            _voiceMode = false;
            _voicePanel.IsVisible = false;
            _inputBorder.IsVisible = true;
            SetLocalized(_txtTitle, "label_type_to_unlock_2");
            ApplyInputAffordance();
            FocusInput();
            Log.Information("LockCardWindow: fell back to typed solve (speech unavailable mid-card)");
        }

        // ── Lifecycle / the multi-window surface the services call ─────────────

        /// <summary>
        /// The card's own deliberate exits (Esc, the completion timer). <c>_isCompleted</c> is set
        /// first for the same reason WPF's <c>DismissToPool</c> sets it: <see cref="OnClosing"/>
        /// cancels an unsolved strict close, and an Esc that <see cref="EscClosesCard"/> allowed
        /// must not then be refused by that guard.
        /// </summary>
        private void CloseAllWindows()
        {
            var windows = Fanout();
            _allWindows.Clear();
            foreach (var window in windows)
            {
                // Set FIRST on every window: OnClosing cancels an unsolved strict close, so a
                // strict mirror would otherwise refuse and leave an orphaned fullscreen cover.
                window._isCompleted = true;
                try { window.Close(); } catch { }
            }
        }

        /// <summary>
        /// Put a lock card up: one cover on the primary, plus one per further monitor when
        /// <c>DualMonitorEnabled</c> is on, with the primary owning the keyboard and the rest
        /// mirroring it. This does NOT need a service — the WPF version's blocker was Win32, not
        /// LockCardService, and the caller that matters is already on this head:
        /// BubbleCountResultWindow's mercy card shows one here and polls <see cref="IsAnyOpen"/>.
        ///
        /// <para>What the WPF original did that is deliberately NOT reproduced:</para>
        ///  - the keep-alive pool of realized layered windows and its #494 render-thread deadlock
        ///    workaround, both WPF-specific;
        ///  - the background dead-man's switch that force-dropped every cover through
        ///    <c>SetLayeredWindowAttributes</c> + <c>SetWindowPos</c> on raw hwnds. There is no
        ///    equivalent, so a wedged UI thread on this head leaves the cover up. That is the
        ///    single largest behavioural loss in this file;
        ///  - the deferred show across display changes, and
        ///    <c>DisableProcessWindowsGhosting</c> / the <c>WM_DPICHANGED</c> swallow-hook, both
        ///    Windows-only and simply dropped.
        ///
        /// <para>ponytail: the mirror's monitor placement is best effort. A maximized X11 window
        /// ignores <c>Position</c>, so a mirror is un-maximized, moved and re-maximized and the WM
        /// decides — where WPF could insist with <c>SetWindowPos</c> in device pixels.</para>
        /// </summary>
        public static void ShowOnAllMonitors(string phrase, int repeats, bool strictMode, bool isTest = false, bool voiceMode = false)
        {
            // A card is already up. Reconfiguring live covers would drop the lock mid-solve, which
            // is also why the WPF original deferred a second show rather than reusing the set.
            if (_allWindows.Count > 0) return;

            try
            {
                var primary = Build(phrase, repeats, strictMode, voiceMode, isTest, isPrimary: true);
                primary.Show();

                // One cover per monitor when the user asked for it. Avalonia has no screen list
                // without a TopLevel, so the set is drawn off the primary once it exists.
                if (CoreSettings.Current.DualMonitorEnabled)
                {
                    var all = Features.ScreenList.Enumerate(primary);
                    var primaryScreen = all.FirstOrDefault(s => s.IsPrimary) ?? all.FirstOrDefault();
                    foreach (var screen in all.Where(s => s != primaryScreen))
                    {
                        var mirror = Build(phrase, repeats, strictMode, voiceMode, isTest, isPrimary: false);
                        mirror.Show();
                        // The .axaml opens Maximized, and a maximized X11 window ignores Position.
                        // Un-maximize, place it on the target screen, re-maximize: the WM then
                        // maximizes onto the monitor the window sits on. Best effort by definition -
                        // placement is the WM's call here, not a SetWindowPos we can insist on.
                        mirror.WindowState = WindowState.Normal;
                        mirror.Position = new PixelPoint(screen.Bounds.X + 50, screen.Bounds.Y + 50);
                        mirror.WindowState = WindowState.Maximized;
                    }
                }

                // Last, so the card that can be typed into owns the foreground.
                primary.Activate();
                Log.Information("Lock Card shown on {Count} window(s) - {Repeats} repeat(s), strict={Strict}, test={Test}",
                    _allWindows.Count, repeats, strictMode, isTest);
            }
            catch (Exception ex)
            {
                Log.Warning("LockCardWindow.ShowOnAllMonitors failed: {Error}", ex.Message);
                ForceCloseAll();
            }
        }

        /// <summary>
        /// One configured, registered, not-yet-shown card. Registration happens HERE rather than in
        /// the constructor so the design constructor (and --render-all) never join the visible set,
        /// and before <c>Show()</c> so <see cref="IsAnyOpen"/> is already true for a caller that
        /// polls it the moment <see cref="ShowOnAllMonitors"/> returns.
        /// </summary>
        private static LockCardWindow Build(string phrase, int repeats, bool strictMode, bool voiceMode, bool isTest, bool isPrimary)
        {
            var window = new LockCardWindow { _isPrimary = isPrimary, _isTest = isTest };
            window.Configure(phrase, repeats, strictMode, voiceMode);
            _allWindows.Add(window);
            return window;
        }

        /// <summary>Whether a lock card is on screen. BubbleCountResultWindow's mercy flow polls
        /// this every 500 ms to learn when the card has been solved.</summary>
        public static bool IsAnyOpen() => _allWindows.Count > 0;

        /// <summary>Drop every card, solved or not - the panic exit. Strict mode does not survive
        /// this: <c>_isCompleted</c> is set first on each window so OnClosing cannot refuse.</summary>
        public static void ForceCloseAll()
        {
            var windows = _allWindows.ToList();
            _allWindows.Clear();
            foreach (var window in windows)
            {
                window._isCompleted = true;
                try { window.Close(); } catch { }
            }
        }

        /// <summary>
        /// The mic privacy pill: drop every voice-mode card to typed solve so the microphone closes
        /// but the lock still has to be solved. A no-op on purpose — no card on this head is ever
        /// in voice mode (see <see cref="Configure"/>), so there is nothing to drop. Wire it with
        /// the CoreSpeech listen call, not before.
        /// </summary>
        public static void DisableVoiceForAll() { }
    }
}
