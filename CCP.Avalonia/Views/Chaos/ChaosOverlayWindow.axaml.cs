using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
// ContentLocator: Core's two-root probe (install dir, then the downloaded content pack). The
// chaos cue resolver ends on exactly this call in the head.
using ConditioningControlPanel.Services;
// ChaosConversation / ChaosConversationLine / ChaosSpeaker are ALREADY in Core, so the story card
// binds the real narrative model rather than a stand-in.
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// Centered full-screen topmost overlay used by Chaos Mode for three transient
    /// moments: the 3·2·1·GO countdown (click-through, desktop stays usable), the
    /// between-waves boon draft, and the end-of-run results (both interactive with a
    /// dim backdrop). Bubbles + HUD live in their own windows; this is only shown
    /// when one of these modes is active.
    ///
    /// PORTED from ConditioningControlPanel/Chaos/ChaosOverlayWindow.xaml.cs. Deviations:
    ///
    /// <para><b>Win32.</b> Every P/Invoke in the original is gone; `user32`/`kernel32` do not
    /// exist off Windows and this head is net8.0. The mapping actually used:</para>
    /// <list type="bullet">
    ///   <item><c>SetWindowLong(GWL_EXSTYLE, WS_EX_TRANSPARENT)</c> ->
    ///         <see cref="X11Overlay.SetClickThrough"/>, which is the X11 twin (an empty XFixes
    ///         input region). It returns false off X11 and the call site does not branch.</item>
    ///   <item><c>WS_EX_TOOLWINDOW</c> -> <c>ShowInTaskbar="False"</c> in the XAML;
    ///         <c>WS_EX_NOACTIVATE</c> -> <c>ShowActivated="False"</c>. Both are now static
    ///         window properties rather than a style re-applied on every mode change - which is
    ///         a behaviour difference worth naming: WPF cleared NOACTIVATE for the draft and
    ///         recap so those could take focus, and here the window is simply never activated on
    ///         show and <see cref="BringToFront"/> calls <c>Activate()</c> instead.</item>
    ///   <item><c>SetForegroundWindow</c>-style raising -> <c>Topmost</c> toggle + <c>Activate()</c>,
    ///         unchanged from the original's own fallback path.</item>
    ///   <item><c>SetWindowsHookEx(WH_MOUSE_LL)</c> + <c>Services.GlobalKeyboardHook</c> ->
    ///         <b>stubbed, and the behaviour is lost.</b> See
    ///         <see cref="InstallCountdownSkipHooks"/>.</item>
    ///   <item><c>GetModuleHandle</c> / <c>Process.MainModule</c> -> dropped; they only existed
    ///         to feed the hook install.</item>
    /// </list>
    ///
    /// <para><b>Animation.</b> The original's <c>BeginAnimation</c>s all run: the
    /// countdown step's pop and fade, the staged card reveal, the unchosen cards' dissolve, the
    /// chosen card's elastic burst, its glow bloom, its art-border pulse and its button bounce, the
    /// recap verdict's delayed fade, the reward chips' staggered pop and cue, the rank card's fade,
    /// the story card's fade in and out, the portrait's slide-in, the dialogue box's per-line
    /// re-settle, the chevron's idle bounce and the two ken-burns drifts. They do NOT run through
    /// Avalonia's keyframe <c>Animation</c> - see the animation-pump region for why that throws on
    /// a code-behind-held transform - but off one shared 16ms <c>DispatcherTimer</c>, the shape
    /// <c>ChaosHudWindow</c> landed first. Two fidelity gaps, both noted at their call sites:
    /// Avalonia's <c>BackEaseOut</c> and <c>ElasticEaseOut</c> expose no Amplitude/Oscillations, so
    /// those arcs read a little looser than WPF's.</para>
    ///
    /// <para>The portrait slide-in is written and guarded exactly as WPF guards it, on a non-null
    /// portrait; with no <c>ChaosArt</c> here that resolves null and nothing slides, which is the
    /// same branch the original takes for an unbacked portrait id. The ken-burns drift runs off the
    /// backdrop the CALLER passes, not <c>ChaosArt</c>, so it is motion over real content whenever
    /// there is any.</para>
    ///
    /// <para>The <c>DispatcherTimer</c>s are not stubs either: the countdown steps, the staged card
    /// reveal, the auto-resume tick, the post-pick confirm beat and the score tally are logic, and
    /// they port one for one.</para>
    ///
    /// <para><b>Services.</b> Five of these are no longer stubs, because they turned out to be
    /// content or a settings read rather than a service: <c>ChaosSfx</c> (ContentLocator +
    /// CoreAudio + the master-volume curve), <c>ChaosTips</c> (40 lines of control composition),
    /// <c>ChaosBoonColors</c> (an id table, now ChaosBoonColors.cs beside this file),
    /// <c>ChaosRanks</c>'s two members (shipped copy) and <c>ChaosWindowZ.BornTopmost</c>
    /// (<c>AppSettings.ChaosPinOnTop</c>). <c>ChaosArt</c>, <c>ChaosMeta</c>,
    /// <c>ChaosLessons</c>, <c>ChaosGlyphs</c>, <c>ChaosNarrator</c>,
    /// <c>ChaosAnnouncerOverlay</c>, <c>ChaosModeService</c> and <c>RevealService</c>
    /// still live in the WPF
    /// head - all under ConditioningControlPanel/Services/Chaos/ except <c>ChaosWindowZ</c> and
    /// <c>ChaosAnnouncerOverlay</c>, which are ConditioningControlPanel/Chaos/ - and this project
    /// may not reference it. They are stubbed in the Stubs region below,
    /// shaped so every call site ports unchanged. <c>ChaosBoon</c>, <c>ChaosRarity</c>,
    /// <c>ChaosRank</c> and the run snapshot are local stand-ins for the same reason;
    /// <c>ChaosConversation</c> and friends are already in Core and are used for real, as is
    /// <c>ChaosMetaState</c> — the recap reads the real save MODEL; only its store is head-side.</para>
    ///
    /// <para><b>The parameterless constructor draws a sample recap.</b> WPF's showed an empty
    /// transparent window - every panel starts collapsed and a service drives it - and there is no
    /// service on this head to drive it, so <c>--render-all</c> would prove nothing. Same choice
    /// as ChaosSlotPickerWindow. Drop <see cref="ShowSampleRecap"/> when ChaosModeService lands.</para>
    /// </summary>
    public partial class ChaosOverlayWindow : Window
    {
        private Action<ChaosBoon?>? _onBoonPick;
        private bool _clickThrough = true;

        // ---- boon draft reveal/selection state ----
        private sealed class DraftCard
        {
            public Border Card = null!;
            public Button Pick = null!;
            public ScaleTransform Scale = null!;
            public ChaosBoon Boon = null!;
            public Border Art = null!;                 // artwork square (placeholder until per-boon art ships)
            public SolidColorBrush ArtBorder = null!;  // its thick border brush — flashed on pick
            public ScaleTransform PickScale = null!;   // the ACCEPT button's own scale, bounced on pick
            public string Key = "";                    // tween namespace, so one card's motion never steals another's
        }
        private readonly List<DraftCard> _draftCards = new();
        private DispatcherTimer? _revealTimer;
        private int _revealIndex;
        private ChaosBoon? _selectedBoon;
        private bool _selectionMade;

        public Action? OnRunAgain;
        public Action? OnDismissed;

        // XAML parts. WPF generated these fields; Avalonia's compiled loader does too, but
        // FindControl keeps the port readable next to the original and matches the other
        // ported Chaos windows.
        private readonly Rectangle _backdrop;
        private readonly Viewbox _countdownBox;
        private readonly TextBlock _countdownText;
        private readonly Border _draftPanel;
        private readonly TextBlock _draftTitle;
        private readonly StackPanel _boonCardHost;
        private readonly Button _btnSkipBoon;
        private readonly Button _btnReroll;
        private readonly TextBlock _btnRerollText;
        private readonly TextBlock _draftCountdown;
        private readonly Border _resultsPanel;
        private readonly Image _resultsHero;
        private readonly StackPanel _resultsBody;
        private readonly Button _btnRunAgain;
        private readonly Button _btnClose;
        private readonly TextBlock _btnCloseText;
        private readonly Button _btnDollhouse;
        private readonly Button _btnAdjust;
        private readonly Grid _storyCardPanel;
        private readonly Image _storyBg;
        private readonly Image _storyPortrait;
        private readonly TextBlock _storyName;
        private readonly TextBlock _storyTitle;
        private readonly TextBlock _storyText;
        private readonly Border _storyBox;
        private readonly TextBlock _storyAdvance;

        private T Part<T>(string name) where T : Control => this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"ChaosOverlayWindow: no '{name}' in the XAML");

        public ChaosOverlayWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _backdrop = Part<Rectangle>("Backdrop");
            _countdownBox = Part<Viewbox>("CountdownBox");
            _countdownText = Part<TextBlock>("CountdownText");
            _draftPanel = Part<Border>("DraftPanel");
            _draftTitle = Part<TextBlock>("DraftTitle");
            _boonCardHost = Part<StackPanel>("BoonCardHost");
            _btnSkipBoon = Part<Button>("BtnSkipBoon");
            _btnReroll = Part<Button>("BtnReroll");
            _btnRerollText = Part<TextBlock>("BtnRerollText");
            _draftCountdown = Part<TextBlock>("DraftCountdown");
            _resultsPanel = Part<Border>("ResultsPanel");
            _resultsHero = Part<Image>("ResultsHero");
            _resultsBody = Part<StackPanel>("ResultsBody");
            _btnRunAgain = Part<Button>("BtnRunAgain");
            _btnClose = Part<Button>("BtnClose");
            _btnCloseText = Part<TextBlock>("BtnCloseText");
            _btnDollhouse = Part<Button>("BtnDollhouse");
            _btnAdjust = Part<Button>("BtnAdjust");
            _storyCardPanel = Part<Grid>("StoryCardPanel");
            _storyBg = Part<Image>("StoryBg");
            _storyPortrait = Part<Image>("StoryPortrait");
            _storyName = Part<TextBlock>("StoryName");
            _storyTitle = Part<TextBlock>("StoryTitle");
            _storyText = Part<TextBlock>("StoryText");
            _storyBox = Part<Border>("StoryBox");
            _storyAdvance = Part<TextBlock>("StoryAdvance");

            _btnSkipBoon.Click += BtnSkipBoon_Click;
            _btnReroll.Click += BtnReroll_Click;
            _btnRunAgain.Click += BtnRunAgain_Click;
            _btnClose.Click += BtnClose_Click;
            _btnDollhouse.Click += BtnDollhouse_Click;
            _btnAdjust.Click += BtnAdjust_Click;

            Topmost = ChaosWindowZ.BornTopmost;   // Free Desktop runs aren't pinned above other apps
            SizeToPrimaryScreen();
            // WPF applied the ex-styles from SourceInitialized. The X11 twin needs the window to
            // have a handle too, so it runs from Opened - the earliest point TryGetPlatformHandle
            // returns an XID.
            Opened += (_, _) => ApplyExStyles();
            // Story-card press-forward: click anywhere on the card, or Space/Enter/→ while it's up.
            _storyCardPanel.PointerPressed += (_, e) => { e.Handled = true; AdvanceStory(); };
            KeyDown += OnStoryKey;

            ShowSampleRecap();
        }

        /// <summary>WPF read <c>SystemParameters.PrimaryScreenWidth/Height</c> and pinned the
        /// window at 0,0. The twin is <c>Screens.Primary</c>, whose Bounds are PHYSICAL pixels -
        /// dividing by Scaling is what keeps a 150% desktop from getting a window half off the
        /// glass. Guarded, because headless and a not-yet-attached window both have no screen.</summary>
        private void SizeToPrimaryScreen()
        {
            try
            {
                var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
                if (screen is null) return;
                double w = screen.Bounds.Width / screen.Scaling;
                double h = screen.Bounds.Height / screen.Scaling;
                if (w < 640 || h < 480 || w > 8192 || h > 8192) return;   // nonsense, keep the XAML size
                Position = screen.Bounds.Position;
                Width = w;
                Height = h;
            }
            catch (Exception ex) { Log.Debug("ChaosOverlayWindow: no screen to size to ({E})", ex.Message); }
        }

        // ============================ countdown ============================

        private DispatcherTimer? _countdownTimer;
        private Action? _countdownComplete;
        private bool _countdownFinished;

        /// <summary>Show the GO countdown. <paramref name="shortFlash"/> uses a single 1s "GO!" flash
        /// (RunAgain); otherwise the full 3·2·1·GO. Skippable on click/keypress in both cases.</summary>
        public void ShowCountdown(Action onComplete, bool shortFlash = false)
        {
            string[] steps = shortFlash ? new[] { "SINK" } : new[] { "3", "2", "1", "SINK" };
            int interval = shortFlash ? ChaosModeService.ChaosRestartCountdownMs : 750;
            ShowCountdownSteps(steps, interval, onComplete);
        }

        /// <summary>A short "Ready? :3" → "GO!" beat after a mantra pick, using the same flashing
        /// countdown display as the run start, so play resumes with a moment to settle. Skippable.</summary>
        public void ShowReadyGo(Action onComplete)
            => ShowCountdownSteps(new[] { "ready? :3", "SINK" }, 800, onComplete);

        private void ShowCountdownSteps(string[] steps, int interval, Action onComplete)
        {
            SetClickThrough(true);
            _backdrop.IsVisible = false;
            _draftPanel.IsVisible = false;
            _resultsPanel.IsVisible = false;
            _countdownBox.IsVisible = true;

            _countdownComplete = onComplete;
            _countdownFinished = false;

            int i = 0;
            ShowCountdownStep(steps[0]);

            _countdownTimer?.Stop();
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
            _countdownTimer.Tick += (_, _) =>
            {
                i++;
                if (i < steps.Length) ShowCountdownStep(steps[i]);
                else FinishCountdown();
            };
            _countdownTimer.Start();
            InstallCountdownSkipHooks();
        }

        /// <summary>Complete the countdown immediately (timer end, or a skip click/keypress).</summary>
        private void FinishCountdown()
        {
            if (_countdownFinished) return;
            _countdownFinished = true;
            _countdownTimer?.Stop();
            _countdownTimer = null;
            RemoveCountdownSkipHooks();
            _countdownBox.IsVisible = false;
            var cb = _countdownComplete;
            _countdownComplete = null;
            cb?.Invoke();
        }

        /// <summary>
        /// <b>Stubbed, and this is a real loss, not a formality.</b> WPF installed a low-level
        /// <c>WH_MOUSE_LL</c> hook plus <c>Services.GlobalKeyboardHook</c> for the length of the
        /// countdown, because the countdown window is deliberately click-through and therefore
        /// cannot receive input itself. That made ANY click or keypress anywhere on the desktop
        /// skip straight to "SINK".
        ///
        /// <para>ponytail: this is the ONE thing in this window that needs a capability
        /// <see cref="X11Overlay"/> does not have - it exposes <c>SetClickThrough</c> and
        /// <c>RestackAbove</c> and nothing else. Naming the missing member so a later layer does
        /// not have to rediscover it: <c>X11Overlay.WatchRawInput(Action onAnyPress) : IDisposable</c>,
        /// an <c>XISelectEvents</c> for <c>XI_RawButtonPress | XI_RawKeyPress</c> on the root
        /// window of its OWN display connection, posting to the UI thread. That is its own layer -
        /// a raw-input listener cannot be proved by a render, and it must be exercised inside a
        /// nested compositor, never against the live session. It is also X11-only by construction:
        /// Wayland has no equivalent at all, so on that session the countdown stays unskippable
        /// whatever gets built.</para>
        ///
        /// <para><b>Until then the countdown cannot be skipped and always runs its full
        /// 3·2·1·SINK duration.</b> Nothing else about it changes: the timer, the steps, the
        /// completion callback and now the per-step pop and fade are all live.</para>
        /// </summary>
        private void InstallCountdownSkipHooks() { }

        /// <summary>Paired with <see cref="InstallCountdownSkipHooks"/>; nothing to unhook.</summary>
        private void RemoveCountdownSkipHooks() { }

        private void ShowCountdownStep(string text)
        {
            ChaosSfx.Play(text == "SINK" ? "sink" : "countdown_tick", text == "SINK" ? 0.6f : 0.45f);
            _countdownText.Text = text;
            _countdownText.Foreground = text == "SINK" ? new SolidColorBrush(Color.FromRgb(120, 255, 160)) : Brushes.White;
            // Each step lands big and pops down to rest, fading up from near-transparent.
            // Fidelity gap: WPF's BackEase carried Amplitude 0.4; Avalonia's BackEaseOut exposes no
            // amplitude, so the overshoot reads a little looser. Same arrival, same duration.
            if (_countdownText.RenderTransform is ScaleTransform cs)
                StartTween("countdown:pop", 350, t => cs.ScaleX = cs.ScaleY = 1.5 + (1.0 - 1.5) * t, new BackEaseOut());
            StartTween("countdown:fade", 200, t => _countdownBox.Opacity = 0.2 + 0.8 * t);
        }

        // ============================ boon draft ============================

        private DispatcherTimer? _autoResumeTimer;
        private DispatcherTimer? _confirmTimer;     // post-pick beat before the draft commits itself
        private int _autoResumeRemainingSec;
        private int _draftWave;
        private Func<(List<ChaosBoon> options, int rerollsLeft)?>? _rerollFunc;   // Taking Chances
        /// <summary>Skip reveal state captured per deal: true = timeout skips (+1 resistance),
        /// false = the table has no skip and a timeout autopicks a card.</summary>
        private bool _skipAllowed = true;

        public void ShowBoonDraft(int waveJustCleared, List<ChaosBoon> options, Action<ChaosBoon?> onPick, int autoResumeSec = 0,
                                  int rerollsLeft = 0, Func<(List<ChaosBoon> options, int rerollsLeft)?>? onReroll = null)
        {
            _onBoonPick = onPick;
            _selectedBoon = null;
            _selectionMade = false;
            _draftWave = waveJustCleared;
            _rerollFunc = onReroll;
            _btnReroll.IsVisible = rerollsLeft > 0 && onReroll != null;
            _btnRerollText.Text = rerollsLeft > 1 ? $"🎲 tempt fate again ({rerollsLeft} left)" : "🎲 tempt fate again";
            _autoResumeTimer?.Stop();
            _autoResumeTimer = null;
            _confirmTimer?.Stop();
            _confirmTimer = null;
            _autoResumeRemainingSec = autoResumeSec;
            SetClickThrough(false);
            _countdownBox.IsVisible = false;
            _resultsPanel.IsVisible = false;
            _backdrop.IsVisible = true;
            _draftPanel.IsVisible = true;
            BringToFront();

            ChaosSfx.Play("cards_in", 0.5f);   // the fan whoosh under the per-card reveals

            bool hasSin = options.Exists(o => o.IsCurse);
            _draftTitle.Text = hasSin
                ? $"LOOP {waveJustCleared} CLEARED · CHOOSE A MANTRA... OR DON'T"
                : $"LOOP {waveJustCleared} CLEARED · CHOOSE A MANTRA";
            _draftCountdown.Text = "";
            // Happy path: the skip affordance stays hidden until its reveal flips (run 3).
            // Before that an untouched draft AUTOPICKS instead of skipping (see StartAutoResume).
            _skipAllowed = RevealService.IsUnlocked(RevealIds.DraftSkip);
            _btnSkipBoon.IsVisible = _skipAllowed;
            if (_skipAllowed && !ChaosMeta.State.SeenSkipDebut)
            {
                ChaosMeta.State.SeenSkipDebut = true;
                ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("you're allowed to refuse now.", ChaosAnnounceKind.Willpower);
            }

            // Build every card hidden, then reveal them one at a time (each with a per-rarity cue:
            // a bright "dling" for rare, a dull "thud" otherwise). Picks are disabled until revealed.
            _boonCardHost.Children.Clear();
            _draftCards.Clear();
            foreach (var boon in options)
            {
                var dc = BuildBoonCard(boon);
                dc.Key = "card:" + _draftCards.Count;
                dc.Card.Opacity = 0;
                dc.Scale.ScaleX = dc.Scale.ScaleY = 0.7;
                dc.Pick.IsEnabled = false;
                _draftCards.Add(dc);
                _boonCardHost.Children.Add(dc.Card);
            }

            _revealTimer?.Stop();
            _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _revealTimer.Tick += (_, _) =>
            {
                if (_revealIndex >= _draftCards.Count)
                {
                    _revealTimer?.Stop();
                    if (!_selectionMade)
                    {
                        if (_autoResumeRemainingSec > 0) StartAutoResume();
                        else _draftCountdown.Text = "the field holds. take your time.";
                    }
                    return;
                }
                RevealCard(_draftCards[_revealIndex]);
                _revealIndex++;
            };
            // First card lands immediately; the rest follow on the timer.
            _revealIndex = 0;
            if (_draftCards.Count > 0) { RevealCard(_draftCards[0]); _revealIndex = 1; }
            _revealTimer.Start();
        }

        private void RevealCard(DraftCard dc)
        {
            dc.Pick.IsEnabled = true;
            // The card fades up and pops out of its 0.7 build state. Both tweens are keyed per card
            // so a fast re-deal cannot leave one mid-fade; either way they settle at 1, which is
            // what keeps an invisible-but-clickable card impossible.
            StartTween(dc.Key + ":fade", 220, t => dc.Card.Opacity = t);
            StartTween(dc.Key + ":pop", 300, t => dc.Scale.ScaleX = dc.Scale.ScaleY = 0.7 + 0.3 * t, new BackEaseOut());
            if (dc.Boon.IsCurse) ChaosSfx.Play("sin_reveal", 0.55f);   // a sin lands with its own drone
            else ChaosSfx.PlayBoonReveal(dc.Boon.Rarity == ChaosRarity.Rare);
        }

        private void HideDraft()
        {
            _revealTimer?.Stop();
            _revealTimer = null;
            _autoResumeTimer?.Stop();
            _autoResumeTimer = null;
            _confirmTimer?.Stop();
            _confirmTimer = null;
            // Stop every per-card tween BEFORE dropping the effects and brushes they write into.
            // This is the leak the WPF original guarded here: its chosen card's Forever
            // ColorAnimation otherwise kept the clock, the brush and the effect render-target alive
            // on every single pick. The pump's equivalent is a forever tween holding a closure over
            // a discarded SolidColorBrush and keeping the 16ms timer awake for the window's life.
            foreach (var c in _draftCards)
            {
                try
                {
                    StopTween(c.Key + ":fade"); StopTween(c.Key + ":pop");
                    StopTween(c.Key + ":dissolve"); StopTween(c.Key + ":burst");
                    StopTween(c.Key + ":bloom"); StopTween(c.Key + ":pulse");
                    StopTween(c.Key + ":bounce");
                    if (c.Card != null) c.Card.Effect = null;
                    if (c.Art != null) c.Art.Effect = null;
                }
                catch { }
            }
            _draftCards.Clear();
            _selectedBoon = null;
            _selectionMade = false;
            _rerollFunc = null;
            _btnReroll.IsVisible = false;
            _draftPanel.IsVisible = false;
            _backdrop.IsVisible = false;
            SetClickThrough(true);
        }

        /// <summary>Taking Chances: spend a reroll and re-deal the table (no-op once a pick is made).</summary>
        private void BtnReroll_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectionMade) return;
            var pick = _onBoonPick;
            var result = _rerollFunc?.Invoke();
            if (pick == null || result == null) { _btnReroll.IsVisible = false; return; }
            ChaosSfx.PlayBoonReveal(true);
            ShowBoonDraft(_draftWave, result.Value.options, pick, _autoResumeRemainingSec,
                          result.Value.rerollsLeft, _rerollFunc);
        }

        /// <summary>Auto-resume: an untouched draft ticks down, then resolves itself so an
        /// unattended run never freezes forever. With the skip revealed it auto-takes the SKIP
        /// (+1 shield); before that (runs 1 and 2) it AUTOPICKS a random card — the hole chooses.
        /// Any pick cancels it (see SelectBoon).</summary>
        private void StartAutoResume()
        {
            string CountText() => _skipAllowed
                ? $"auto-resist in {_autoResumeRemainingSec}s. pick to keep playing"
                : $"it chooses in {_autoResumeRemainingSec}s. pick first if you'd rather";
            _draftCountdown.Text = CountText();
            _autoResumeTimer?.Stop();
            _autoResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoResumeTimer.Tick += (_, _) =>
            {
                _autoResumeRemainingSec--;
                if (_selectionMade) { _autoResumeTimer?.Stop(); return; }
                if (_autoResumeRemainingSec <= 0)
                {
                    _autoResumeTimer?.Stop();
                    if (_skipAllowed) ChooseBoon(null);   // auto-skip → +1 shield + ChaosBoonSkipped fired by the service
                    else AutopickRandom();                 // no skip yet: it chose for you
                    return;
                }
                _draftCountdown.Text = CountText();
            };
            _autoResumeTimer.Start();
        }

        /// <summary>The timed-out, skipless table picks for the player: a random revealed card
        /// runs the normal pick beat (glow + commit), with its own bark.</summary>
        private void AutopickRandom()
        {
            if (_selectionMade || _draftCards.Count == 0) return;
            var revealed = _draftCards.FindAll(dc => dc.Pick.IsEnabled);
            var pool = revealed.Count > 0 ? revealed : _draftCards;
            var chosen = pool[Random.Shared.Next(pool.Count)];
            CoreBark.NotifyChaosDraftAutopick();
            SelectBoon(chosen);
        }

        private DraftCard BuildBoonCard(ChaosBoon boon)
        {
            // Sins red, synergy duos gold (their partner gear is equipped), plain mantras green.
            var accent = boon.IsCurse ? Color.FromRgb(255, 120, 120)
                       : boon.RequiresAny != null || boon.RequiresAll != null ? Color.FromRgb(255, 215, 0)
                       : Color.FromRgb(156, 232, 160);
            accent = ChaosBoonColors.ForOrDefault(boon.Id, accent);   // payload-based color language
            var accentBrush = new SolidColorBrush(accent);

            var panel = new StackPanel { Width = 190 };

            // Artwork square on top of the card — a thick accent border (flashed on pick) around the
            // boon's art. Real art at assets/Chaos/boons/{id}.png is used when present; until then a
            // placeholder (dark fill + the boon's glyph) stands in.
            var artBorderBrush = new SolidColorBrush(accent);
            var artFill = ChaosArt.Resolve("boons", boon.Id);
            var art = new Border
            {
                Width = 190,
                Height = 190,
                BorderBrush = artBorderBrush,
                BorderThickness = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                RenderTransformOrigin = RelativePoint.Center,
                Background = artFill != null
                    ? new ImageBrush { Source = artFill, Stretch = Stretch.UniformToFill }
                    : new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)),
            };
            if (artFill == null)
                art.Child = new TextBlock
                {
                    Text = boon.IsCurse ? "☠" : "◈",
                    FontSize = 70,
                    Foreground = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            ChaosTips.Attach(art, boon.Name, boon.Desc, accent: accent, flavor: boon.Flavor);
            panel.Children.Add(art);

            panel.Children.Add(new TextBlock
            {
                Text = (boon.IsCurse ? "☠ " : "◈ ") + boon.Name.ToUpperInvariant(),
                Foreground = accentBrush,
                FontWeight = FontWeight.Bold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            panel.Children.Add(new TextBlock
            {
                Text = boon.Desc,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 50,
                Margin = new Thickness(0, 0, 0, 8),
            });
            if (!string.IsNullOrEmpty(boon.Flavor))
                panel.Children.Add(new TextBlock
                {
                    Text = boon.Flavor,
                    FontStyle = FontStyle.Italic,
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 8),
                });
            panel.Children.Add(new TextBlock
            {
                Text = $"{RarityDots(boon.Rarity)} {boon.Rarity}",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 208)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10),
            });
            var pickScale = new ScaleTransform(1, 1);
            // The caption is a TextBlock child rather than Content: Avalonia parses "_" in a
            // button's Content as an access key, and SelectBoon rewrites this one to "✓ CHOSEN".
            var pickText = new TextBlock { Text = boon.IsCurse ? "GIVE IN" : "ACCEPT", HorizontalAlignment = HorizontalAlignment.Center };
            var pick = new Button
            {
                Content = pickText,
                Padding = new Thickness(0, 8, 0, 8),
                Background = accentBrush,
                Foreground = Brushes.Black,
                FontWeight = FontWeight.Bold,
                BorderThickness = new Thickness(0),
                // Avalonia's Button does not fill its StackPanel slot the way WPF's did, so the
                // pick button would sit narrow and left-hugging under a 190pt card.
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = pickScale,
            };
            panel.Children.Add(pick);

            var scale = new ScaleTransform(1, 1);
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(8),
                Child = panel,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = scale,
            };

            var dc = new DraftCard { Card = card, Pick = pick, Scale = scale, Boon = boon, Art = art, ArtBorder = artBorderBrush, PickScale = pickScale };
            pick.Click += (_, _) => SelectBoon(dc);
            // The art square picks too — same gating as the button (disabled until revealed).
            art.Cursor = new Cursor(StandardCursorType.Hand);
            art.PointerReleased += (_, _) => { if (dc.Pick.IsEnabled) SelectBoon(dc); };
            return dc;
        }

        private static string RarityDots(ChaosRarity r) => r switch
        {
            ChaosRarity.Common => "◆",
            ChaosRarity.Uncommon => "◆◆",
            ChaosRarity.Rare => "◆◆◆",
            _ => "◆"
        };

        /// <summary>A card was picked: dissolve the others, highlight + bounce this one, then auto-commit after a beat.</summary>
        private void SelectBoon(DraftCard chosen)
        {
            if (_selectionMade) return;
            _selectionMade = true;
            _selectedBoon = chosen.Boon;
            _revealTimer?.Stop();
            _autoResumeTimer?.Stop();
            // Mantras get the warm confirm; sins and skips have their own cues (service-side).
            if (!chosen.Boon.IsCurse) ChaosSfx.PlayBoonPicked();   // WPF's extra `chosen != null` is dead: the parameter is not nullable

            foreach (var dc in _draftCards)
            {
                dc.Pick.IsEnabled = false;
                if (dc == chosen) continue;
                // Dissolve the unchosen cards (already-invisible unrevealed ones just stay gone).
                // The fade starts from wherever the card actually IS - a card still mid-reveal
                // would otherwise jump to full opacity before dissolving.
                StopTween(dc.Key + ":fade"); StopTween(dc.Key + ":pop");
                double from = dc.Card.Opacity, fromScale = dc.Scale.ScaleX;
                StartTween(dc.Key + ":dissolve", 260, t =>
                {
                    dc.Card.Opacity = from + (0.0 - from) * t;
                    dc.Scale.ScaleX = dc.Scale.ScaleY = fromScale + (0.8 - fromScale) * t;
                }, new QuadraticEaseIn());
            }

            // Snap the chosen card to fully shown, then brighten its border + add a glow.
            StopTween(chosen.Key + ":fade"); StopTween(chosen.Key + ":pop");
            chosen.Card.Opacity = 1;
            chosen.Scale.ScaleX = chosen.Scale.ScaleY = 1;
            // Pick flourish: an elastic burst so the chosen card pops as it locks in.
            // Fidelity gap: WPF set Oscillations 2 / Springiness 5; Avalonia's ElasticEaseOut has
            // neither knob, so the wobble is the framework's own.
            StartTween(chosen.Key + ":burst", 540,
                t => chosen.Scale.ScaleX = chosen.Scale.ScaleY = 1.22 + (1.0 - 1.22) * t, new ElasticEaseOut());
            // Highlight in the boon's family color (falls back to the old green/red if unmapped).
            var hi = ChaosBoonColors.ForOrDefault(chosen.Boon.Id,
                chosen.Boon.IsCurse ? Color.FromRgb(255, 150, 150) : Color.FromRgb(120, 255, 170));
            chosen.Card.BorderBrush = new SolidColorBrush(hi);
            chosen.Card.BorderThickness = new Thickness(3);
            var cardGlow = new DropShadowEffect { Color = hi, BlurRadius = 28, OffsetX = 0, OffsetY = 0, Opacity = 0.9 };
            chosen.Card.Effect = cardGlow;
            // ...with a one-shot bloom shockwave: the glow blooms wide, then contracts to rest.
            StartTween(chosen.Key + ":bloom", 520, t => cardGlow.BlurRadius = 64 + (28 - 64) * t, new CubicEaseOut());

            // The chosen art square: thick border in the highlight colour with a matching glow, its
            // colour pulsing bright<->accent until the draft tears down (HideDraft stops it).
            chosen.Art.BorderThickness = new Thickness(5);
            chosen.Art.Effect = new DropShadowEffect { Color = hi, BlurRadius = 24, OffsetX = 0, OffsetY = 0, Opacity = 0.95 };
            var pulseFrom = chosen.ArtBorder.Color;
            StartTween(chosen.Key + ":pulse", 240, t => chosen.ArtBorder.Color = LerpColor(pulseFrom, hi, t),
                       new QuadraticEaseInOut(), repeats: -1, alternate: true);

            // Slight bounce on the chosen button.
            StartTween(chosen.Key + ":bounce", 360,
                t => chosen.PickScale.ScaleX = chosen.PickScale.ScaleY = 1.15 + (1.0 - 1.15) * t, new BackEaseOut());
            if (chosen.Pick.Content is TextBlock chosenText) chosenText.Text = "✓ CHOSEN";

            // No Continue button: hold the glowing pick for a short beat, then commit on its own.
            _btnSkipBoon.IsVisible = false;
            _btnReroll.IsVisible = false;   // the die is cast — no rerolling a made choice
            _draftCountdown.Text = "";
            _confirmTimer?.Stop();
            _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _confirmTimer.Tick += (_, _) =>
            {
                _confirmTimer?.Stop();
                _confirmTimer = null;
                ChooseBoon(_selectedBoon);
            };
            _confirmTimer.Start();
        }

        private void ChooseBoon(ChaosBoon? boon)
        {
            var cb = _onBoonPick;
            _onBoonPick = null;
            if (cb == null) return;
            HideDraft();
            cb(boon);
        }

        private void BtnSkipBoon_Click(object? sender, RoutedEventArgs e) => ChooseBoon(null);

        // ============================ results ============================

        public void ShowResults(RunSummary s, double baseXp, double skillMult, double finalXp, long previousBest, int sparksEarned,
                                ChaosRank? rankUp = null)
        {
            SetClickThrough(false);
            _countdownBox.IsVisible = false;
            _draftPanel.IsVisible = false;
            _backdrop.IsVisible = true;
            _resultsPanel.IsVisible = true;
            BringToFront();

            _resultsHero.Source = ChaosArt.ResolveRecap();   // null = the gradient wash shows instead

            // PB / delta-vs-best (best already updated by AwardRunRewards; compare run score to the prior best).
            double score = s.Score;
            double pbDelta = score - previousBest;
            bool isPb = score > previousBest;
            _btnCloseText.Text = isPb ? "wake up (you'll be back)" : "wake up";

            // Breaking the surface; a PB earns its fanfare once the whoosh has landed.
            ChaosSfx.Play("surface", 0.55f);
            if (isPb)
            {
                var pbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                pbTimer.Tick += (_, _) => { pbTimer.Stop(); ChaosSfx.Play("pb_fanfare", 0.6f); };
                pbTimer.Start();
            }

            var dim = new SolidColorBrush(Color.FromRgb(170, 170, 200));
            var gold = new SolidColorBrush(Color.FromRgb(255, 215, 90));
            var pink = new SolidColorBrush(Color.FromRgb(255, 105, 180));

            _resultsBody.Children.Clear();

            // Row of three stat chips: how deep, how clean, how long.
            _resultsBody.Children.Add(ChipRow(
                StatChip("DEPTH", $"{Roman(s.ActIndex)} · L{s.WaveIndex}"),
                StatChip("BEST STREAK", $"x{s.BestCombo}"),
                StatChip("SURVIVED", $"{(int)s.ElapsedSec / 60:00}:{(int)s.ElapsedSec % 60:00}")));

            AddResultLine($"snapped {s.Defused} · triggered {s.Detonated} · effects fired {s.EffectsFired}",
                12, dim, FontWeight.Normal);

            _resultsBody.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(70, 255, 105, 180)), Margin = new Thickness(0, 10, 0, 10) });

            // Score + the compulsion hook (PB / delta-vs-best). The score tallies up from
            // zero under a soft tick; the verdict line fades in as the number lands — which
            // puts a PB's reveal right on the 900ms fanfare above.
            var scoreLine = AddResultLine("score 0", 24, Brushes.White, FontWeight.Bold);
            AnimateScoreTally(scoreLine, score);
            var verdict = isPb
                ? AddResultLine($"★ NEW BEST  (+{pbDelta:N0} over {previousBest:N0})", 14, gold, FontWeight.Bold)
                : AddResultLine($"best {previousBest:N0}   ({pbDelta:N0} vs best)", 12, dim, FontWeight.Normal);
            // Held dark, then faded in at 820ms - on the beat of the score tally landing, which is
            // what puts a PB's reveal on the 900ms fanfare above.
            verdict.Opacity = 0;
            StartTween("verdict", 300, t => verdict.Opacity = t, delayMs: 820);

            _resultsBody.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(70, 255, 105, 180)), Margin = new Thickness(0, 10, 0, 10) });

            // The take-home: XP and drops, side by side. Glyph canon: 🕰 xp, ✦ drops, 🪙 gold —
            // the run award banks as DROPS (gold only ever lands instantly, mid-run).
            // The chips pop in as their own beat once the score tally has landed.
            var takeHome = ChipRow(
                StatChip("XP", $"{ChaosGlyphs.Xp} {finalXp:N0}", pink, $"base {baseXp:N0} x{skillMult:0.0}"),
                StatChip("EMOTES", $"{ChaosGlyphs.Drops} {sparksEarned:N0}", gold, "banked in the dollhouse"));
            _resultsBody.Children.Add(takeHome);
            PopRewardChips(takeHome, firstDelayMs: 900);

            // First completion ("first fall"): name the one-time bonus already inside the haul.
            if (ChaosMeta.State.RunsCompleted == 1)
                AddResultLine($"{ChaosGlyphs.Drops} +{ChaosMeta.FIRST_FALL_BONUS} first fall, counted in",
                    11, gold, FontWeight.Normal);

            // Run 2 done: one quiet nudge toward her corner (the gold has somewhere to go now).
            if (ChaosMeta.State.RunsCompleted == 2)
                AddResultLine("she set up a small corner in the toybox.", 11, dim, FontWeight.Normal);

            // The next goal: one line bridging the haul into the Warren — what the drops are FOR.
            // Hidden on the scripted first fall (the dollhouse hasn't been introduced yet).
            if (ChaosMeta.State.RunsCompleted >= 2 && ChaosMeta.NextGoal() is { } goal)
            {
                string line = goal.Affordable
                    ? $"{ChaosGlyphs.Drops} ready: {goal.Name.ToUpperInvariant()} waits in the toybox"
                    : goal.LessonId != null && ChaosLessons.ById(goal.LessonId) is { } lesson
                        ? $"next: {goal.Name.ToUpperInvariant()} — {lesson.Text} ({ChaosLessons.Progress(goal.LessonId)}/{lesson.Target})"
                        : $"{ChaosGlyphs.Drops} {goal.Cost - ChaosMeta.State.Sparks:N0} more until {goal.Name.ToUpperInvariant()}";
                AddResultLine(line, 11, goal.Affordable ? gold : dim, FontWeight.Normal);
            }

            // The door: from the first completed fall onward, the recap always offers the dollhouse
            // (and the setup shortcut beside it — FALL DEEPER repeats; this one tweaks first).
            _btnDollhouse.IsVisible = ChaosMeta.State.RunsCompleted >= 1;
            _btnAdjust.IsVisible = _btnDollhouse.IsVisible;

            // Bark over the results (+ PB fields for the compulsion line).
            CoreBark.NotifyChaosResultsShown(score, ChaosMeta.State.BestScore, pbDelta, isPb,
                s.Defused, s.Detonated, s.BestCombo, s.Difficulty);

            // Rank spine: once the tally has settled, the quiet rank-up beat.
            if (rankUp.HasValue) ScheduleRankCard(rankUp.Value);
        }

        // ============================ rank card ============================

        private DispatcherTimer? _rankBeatTimer;
        private DispatcherTimer? _rankCardTimer;

        /// <summary>
        /// The rank-up beat, after the score tally has landed: the announcer murmurs the
        /// [LOCKED] "it noticed." line, then the bare rank card fades into the recap —
        /// no header, no fanfare. Bark, LastRankSeen and the reveal sync land with the card.
        /// </summary>
        private void ScheduleRankCard(ChaosRank rank)
        {
            _rankBeatTimer?.Stop();
            _rankBeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            _rankBeatTimer.Tick += (_, _) =>
            {
                _rankBeatTimer?.Stop();
                _rankBeatTimer = null;
                if (!_resultsPanel.IsVisible) return;
                ChaosAnnouncerOverlay.Announce("it noticed.", ChaosAnnounceKind.Temptation, artKey: "it_noticed");
                _rankCardTimer?.Stop();
                _rankCardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
                _rankCardTimer.Tick += (_, _) =>
                {
                    _rankCardTimer?.Stop();
                    _rankCardTimer = null;
                    ShowRankCard(rank);
                };
                _rankCardTimer.Start();
            };
            _rankBeatTimer.Start();
        }

        private void ShowRankCard(ChaosRank rank)
        {
            if (!_resultsPanel.IsVisible) return;

            // Bare and quiet: the rank word, huge and lowercase, one dim line under it.
            var card = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            card.Children.Add(new TextBlock
            {
                Text = ChaosRanks.NameLower(rank),
                FontSize = 46,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            });
            card.Children.Add(new TextBlock
            {
                Text = ChaosRanks.Line(rank),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 200)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            _resultsBody.Children.Add(card);
            card.Opacity = 0;
            StartTween("rankcard", 700, t => card.Opacity = t);
            // Stays bare and quiet by design — just a low velvet thump under the fade.
            ChaosSfx.Play("rank_settle", 0.6f);

            CoreBark.NotifyChaosRankUp(ChaosRanks.NameLower(rank));
            ChaosMeta.State.LastRankSeen = (int)rank;
            ChaosMeta.Save();
            RevealService.Sync("rank_up");
        }

        private TextBlock AddResultLine(string text, double size, IBrush color, FontWeight weight)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = color,
                FontWeight = weight,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3),
                TextWrapping = TextWrapping.Wrap,
            };
            _resultsBody.Children.Add(tb);
            return tb;
        }

        /// <summary>Tally the score line from zero with a soft tick underneath (~800ms,
        /// cubic ease-out so the big digits land early and the tail settles gently).
        ///
        /// <para>One deviation from WPF: the final text is written BEFORE the timer starts, so a
        /// surface that is measured rather than watched - the headless render - shows the real
        /// score instead of "score 0". The timer overwrites it on its first tick and lands on the
        /// same value.</para></summary>
        private void AnimateScoreTally(TextBlock line, double score)
        {
            const int DURATION_MS = 800, FRAME_MS = 33, TICK_EVERY_MS = 90;
            line.Text = $"score {score:N0}";
            if (score <= 0 || _renderSample) return;

            int elapsed = 0, lastTick = -TICK_EVERY_MS;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
            timer.Tick += (_, _) =>
            {
                elapsed += FRAME_MS;
                if (elapsed >= DURATION_MS || !_resultsPanel.IsVisible)
                {
                    timer.Stop();
                    line.Text = $"score {score:N0}";
                    return;
                }
                double p = elapsed / (double)DURATION_MS;
                double eased = 1 - Math.Pow(1 - p, 3);
                line.Text = $"score {score * eased:N0}";
                if (elapsed - lastTick >= TICK_EVERY_MS)
                {
                    lastTick = elapsed;
                    ChaosSfx.Play("count_tick", 0.45f);
                }
            };
            timer.Start();
        }

        /// <summary>The take-home chips pop in one by one from Opacity 0 with a BackEase scale and
        /// a soft cue each, starting after the score tally has landed. 150ms apart, verbatim.</summary>
        private void PopRewardChips(Grid row, int firstDelayMs)
        {
            int i = 0;
            foreach (var child in row.Children)
            {
                if (child is not Border chip) continue;
                int delay = firstDelayMs + i * 150;
                i++;

                var sc = new ScaleTransform(0.6, 0.6);
                chip.RenderTransformOrigin = RelativePoint.Center;   // a bare 0.5,0.5 pair is ABSOLUTE px here
                chip.RenderTransform = sc;
                chip.Opacity = 0;

                string key = "chip:" + i;
                StartTween(key + ":fade", 180, t => chip.Opacity = t, delayMs: delay);
                StartTween(key + ":pop", 320, t => sc.ScaleX = sc.ScaleY = 0.6 + 0.4 * t, new BackEaseOut(), delayMs: delay);
                // WPF gave each chip its own DispatcherTimer for the cue, 60ms into the pop. Same
                // beat off the shared pump: an empty tween whose only job is when it finishes.
                StartTween(key + ":cue", 1, _ => { }, delayMs: delay + 60,
                           done: () => { if (_resultsPanel.IsVisible) ChaosSfx.Play("chip_pop", 0.5f); });
            }
        }

        /// <summary>Equal-width row of stat chips for the recap card.</summary>
        private static Grid ChipRow(params Border[] chips)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            for (int i = 0; i < chips.Length; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                chips[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
                Grid.SetColumn(chips[i], i);
                row.Children.Add(chips[i]);
            }
            return row;
        }

        /// <summary>One recap stat chip: small dim label, bold value, optional sub-line.</summary>
        private static Border StatChip(string label, string value, IBrush? valueBrush = null, string? sub = null)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 138, 178)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 19,
                FontWeight = FontWeight.Bold,
                Foreground = valueBrush ?? Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
            if (sub != null)
                stack.Children.Add(new TextBlock
                {
                    Text = sub,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 148, 186)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0),
                });
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0x22, 0x1E, 0x3E)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Child = stack,
            };
        }

        private static string Roman(int n) => n switch
        {
            <= 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString()
        };

        private void BtnRunAgain_Click(object? sender, RoutedEventArgs e) { OnRunAgain?.Invoke(); }
        private void BtnClose_Click(object? sender, RoutedEventArgs e) { Close(); }

        /// <summary>The recap's door: dismiss the recap, then open the Dollhouse (same single-
        /// instance discipline as the Lab card's entry).</summary>
        private void BtnDollhouse_Click(object? sender, RoutedEventArgs e) => OpenHubAt(null);

        /// <summary>Straight to run setup — recap → Settings tab without hunting through the hub.</summary>
        private void BtnAdjust_Click(object? sender, RoutedEventArgs e) => OpenHubAt("run");

        /// <summary>
        /// PORTED with two things missing, both because their owners are not on this head:
        /// <c>App.Chaos.IsRunning</c> (the guard that stops the hub opening mid-run) and
        /// <c>ChaosHubWindow.Current</c> plus <c>App.MainWindowRef</c> (the single-instance
        /// registry and the owner). ponytail: needs ChaosService + the window registry, wired when
        /// they move to Core - until then this opens a fresh hub every time.
        /// </summary>
        private void OpenHubAt(string? tab)
        {
            Close();   // OnDismissed → the service tears the run windows down first
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var hub = new ChaosHubWindow();
                    hub.Show();
                    if (tab != null) hub.NavigateTo(tab);
                }
                catch (Exception ex) { Log.Warning("Recap dollhouse door failed ({E})", ex.Message); }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _closed = true;
            // The three forever tweens (the boon pulse, the two ken-burns drifts) would otherwise
            // keep the 16ms pump awake on a dead window for the process's life.
            _tweens.Clear();
            _tweenTimer?.Stop(); _tweenTimer = null;
            try { RemoveCountdownSkipHooks(); } catch { }
            _rankBeatTimer?.Stop(); _rankBeatTimer = null;
            _rankCardTimer?.Stop(); _rankCardTimer = null;
            OnDismissed?.Invoke();
        }

        // ============================ story card ============================

        private List<ChaosConversationLine>? _storyLines;
        private int _storyIndex;
        private Action? _onConversationComplete;
        private bool _storyClosing;

        /// <summary>
        /// Open a conversation as a character card: backdrop-as-bg, portrait slide-in, dialogue box,
        /// press-forward through the lines (each line ducks the bed via <c>ChaosNarrator</c>).
        /// Reuses the draft/recap interactive (non-click-through) state. <paramref name="onComplete"/>
        /// fires after the close animation (resumes the field for a run card / closes a standalone hub card).
        /// </summary>
        public void ShowConversation(ChaosConversation convo, IImage? backdrop, Action? onComplete)
        {
            if (convo == null || convo.Lines.Count == 0) { onComplete?.Invoke(); return; }
            _onConversationComplete = onComplete;
            _storyLines = convo.Lines;
            _storyIndex = 0;
            _storyClosing = false;

            // background plate
            _storyBg.Source = backdrop;
            _storyBg.IsVisible = backdrop != null;

            // full-bleed hero + its entrance side
            var portrait = ChaosArt.Resolve("portraits", convo.PortraitId);
            _storyPortrait.Source = portrait;
            _storyPortrait.IsVisible = portrait != null;
            _storyPortrait.HorizontalAlignment = convo.PortraitOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;

            // speaker name + optional title
            _storyName.Text = SpeakerName(convo.Speaker);
            _storyTitle.Text = convo.Title ?? "";
            _storyTitle.IsVisible = !string.IsNullOrEmpty(convo.Title);

            // take over the screen (interactive, no dim rect — the card has its own bg)
            SetClickThrough(false);
            _countdownBox.IsVisible = false;
            _draftPanel.IsVisible = false;
            _resultsPanel.IsVisible = false;
            _backdrop.IsVisible = false;
            _storyCardPanel.IsVisible = true;
            // Keyed the same as the close fade below, deliberately: WPF's BeginAnimation on
            // OpacityProperty REPLACED the running animation, and its Completed then never fired.
            // Opening a card while the previous one is still fading out must drop that fade AND
            // its teardown, or the old card's completion tears the new conversation down mid-line.
            StartTween("story:fade", 180, t => _storyCardPanel.Opacity = t);
            BringToFront();

            // Portrait slide-in (snappy, settles). Guarded exactly as WPF guards it: with no
            // ChaosArt on this head the portrait resolves null and is hidden, so nothing slides -
            // the same branch the original takes when a portrait id has no art behind it. When
            // ChaosArt lands, this runs with no further edit.
            if (portrait != null)
            {
                double fromX = convo.PortraitOnLeft ? -260 : 260;
                StartTween("story:portraitfade", 220, t => _storyPortrait.Opacity = t);
                StartTween("story:portrait", 290, t => SetPortraitOffset(fromX + (0 - fromX) * t), new BackEaseOut());
            }
            else SetPortraitOffset(0);   // the XAML parks it at -260; don't leave a hidden control off-screen

            // Idle bounce on the advance chevron, alternating forever until the card closes.
            if (_storyAdvance.RenderTransform is TranslateTransform chev)
                StartTween("story:chevron", 520, t => chev.X = 6 * t, new SineEaseInOut(), repeats: -1, alternate: true);

            StartBgPan();
            ChaosSfx.Play("cards_in", 0.4f);
            ShowStoryLine(0);
        }

        private void SetPortraitOffset(double x)
        {
            if (_storyPortrait.RenderTransform is TranslateTransform t) t.X = x;
        }

        private void ShowStoryLine(int i)
        {
            if (_storyLines == null || i >= _storyLines.Count) { CloseConversation(); return; }
            var line = _storyLines[i];
            _storyText.FontStyle = line.Emphasis ? FontStyle.Italic : FontStyle.Normal;
            _storyText.Text = line.Text;

            // Dialogue box re-settle on each line (fade + a small scale pop), so a swapped line
            // reads as a new beat rather than as text changing under the reader.
            StartTween("story:boxfade", 150, t => _storyBox.Opacity = t);
            if (_storyBox.RenderTransform is ScaleTransform bs)
                StartTween("story:boxpop", 180, t => bs.ScaleX = bs.ScaleY = 0.97 + 0.03 * t, new BackEaseOut());

            // duck the bed + play the line's clip (placeholder ok → text-only still ducks). NO auto-advance —
            // the scene waits for the user to press forward.
            ChaosNarrator.PlayCardLine(line.AudioKey, line.Text);
        }

        /// <summary>A slow ken-burns drift on the background so the scene breathes: a 16s zoom and
        /// a 22s pan, both alternating forever on deliberately mismatched periods so the two never
        /// resynchronise. Only started when a caller actually passed a backdrop - the plate is the
        /// caller's image, not ChaosArt's, so this is motion over real content whenever there is
        /// any, and nothing at all when there is not.</summary>
        private void StartBgPan()
        {
            if (!_storyBg.IsVisible || _storyBg.RenderTransform is not TransformGroup g) return;
            var scale = g.Children.OfType<ScaleTransform>().FirstOrDefault();
            var trans = g.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (scale != null)
                StartTween("story:bgzoom", 16000, t => scale.ScaleX = scale.ScaleY = 1.08 + 0.08 * t,
                           new SineEaseInOut(), repeats: -1, alternate: true);
            if (trans != null)
                StartTween("story:bgpan", 22000, t => trans.X = -26 + 52 * t,
                           new SineEaseInOut(), repeats: -1, alternate: true);
        }

        private void StopBgPan()
        {
            StopTween("story:bgzoom");
            StopTween("story:bgpan");
        }

        /// <summary>Press-forward (user click / key only): step to the next line, or close after the last.</summary>
        private void AdvanceStory()
        {
            if (!_storyCardPanel.IsVisible || _storyClosing) return;
            _storyIndex++;
            if (_storyLines == null || _storyIndex >= _storyLines.Count) { CloseConversation(); return; }
            ChaosSfx.Play("ui_click", 0.3f);
            ShowStoryLine(_storyIndex);
        }

        /// <summary>Fade the card out, then tear down from the fade's completion - the same
        /// ordering as WPF's <c>Completed</c> handler, which matters: the caller's continuation
        /// resumes the field, and running it before the card has actually left would show the
        /// bubbles through a still-opaque plate.</summary>
        private void CloseConversation()
        {
            if (_storyClosing) return;
            _storyClosing = true;
            StopTween("story:chevron");
            StopBgPan();
            ChaosNarrator.EndCard();   // unduck + drop the speaking/bark hold

            double from = _storyCardPanel.Opacity;
            StartTween("story:fade", 220, t => _storyCardPanel.Opacity = from + (0 - from) * t, done: () =>
            {
                _storyCardPanel.IsVisible = false;
                _storyCardPanel.Opacity = 1;
                _storyBg.Source = null;
                _storyPortrait.Source = null;
                _storyLines = null;
                SetClickThrough(true);
                var cb = _onConversationComplete;
                _onConversationComplete = null;
                try { cb?.Invoke(); } catch (Exception ex) { Log.Debug("Story onComplete: {E}", ex.Message); }
            });
        }

        private void OnStoryKey(object? sender, KeyEventArgs e)
        {
            if (!_storyCardPanel.IsVisible) return;
            // WPF listed Key.Enter and Key.Return separately; in Avalonia they are the same member.
            if (e.Key is Key.Space or Key.Enter or Key.Right)
            {
                e.Handled = true;
                AdvanceStory();
            }
        }

        private static string SpeakerName(ChaosSpeaker s) => s switch
        {
            ChaosSpeaker.Madam => "The Madam",
            ChaosSpeaker.Rabbit => "The Rabbit",
            ChaosSpeaker.Hatter => "The Hatter",
            ChaosSpeaker.Doll => "The Doll",
            ChaosSpeaker.Enemy => "???",
            _ => "",
        };

        // ============================ click-through ============================

        private void SetClickThrough(bool on)
        {
            _clickThrough = on;
            ApplyExStyles();
        }

        /// <summary>
        /// WPF OR'd <c>WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> into the window's
        /// extended style and cleared the first two for the interactive modes. Only the first has a
        /// runtime twin here: <see cref="X11Overlay.SetClickThrough"/>, an empty XFixes input region,
        /// which is the same behaviour and is equally reversible. TOOLWINDOW and NOACTIVATE moved to
        /// <c>ShowInTaskbar="False"</c> / <c>ShowActivated="False"</c> in the XAML, where they are
        /// fixed for the window's life rather than toggled per mode.
        ///
        /// <para>Called unconditionally, on every head: the shim returns false when there is no X11
        /// display (Windows, Wayland, the headless render) rather than throwing.</para>
        /// </summary>
        private void ApplyExStyles() => X11Overlay.SetClickThrough(this, _clickThrough);

        /// <summary>Re-assert top of the topmost band so the draft/results sit above any
        /// payload window (flash/overlay/video) that fired just before a wave boundary.</summary>
        private void BringToFront()
        {
            // Story: pin to the top of the topmost band (toggle forces a re-raise). Free Desktop: bring
            // the draft/results forward this once (Activate/Focus) but don't lock above other apps.
            //
            // The WPF original did exactly this - Topmost toggle, then Activate/Focus - with no
            // P/Invoke, so it ports as-is. Ordering against the OTHER Chaos overlays is
            // X11Overlay.RestackAbove's job and is driven by the service that owns the window band,
            // not from here.
            try
            {
                if (ChaosWindowZ.BornTopmost) { Topmost = false; Topmost = true; }
                else Topmost = false;
                Activate(); Focus();
            }
            catch { }
        }

        // ============================ render sample ============================

        /// <summary>
        /// Sample recap so <c>--render-view</c> / <c>--render-all</c> draw something real. The WPF
        /// window opens blank - every panel collapsed, a service drives it - and no such service is
        /// on this head, so without this the proof would be a transparent rectangle.
        /// ponytail: delete when ChaosModeService moves to Core and can drive the window for real.
        /// </summary>
        /// <summary>True only while <see cref="ShowSampleRecap"/> is running: it stops the score
        /// tally ticking, which under the headless render would otherwise freeze the PNG on a
        /// mid-tween number. A real caller gets the tally.</summary>
        private bool _renderSample;

        private void ShowSampleRecap()
        {
            _renderSample = true;
            try { ShowSampleRecapCore(); }
            finally { _renderSample = false; }
        }

        private void ShowSampleRecapCore() => ShowResults(
            new RunSummary
            {
                Score = 18_420,
                ActIndex = 3,
                WaveIndex = 4,
                BestCombo = 27,
                ElapsedSec = 512,
                Defused = 214,
                Detonated = 9,
                EffectsFired = 63,
                Difficulty = "Deep",
            },
            baseXp: 1_240, skillMult: 1.6, finalXp: 1_984, previousBest: 19_500, sparksEarned: 340);

        // ============================ animation pump ============================

        // Avalonia's keyframe Animation cannot drive these: every one of them writes a
        // ScaleTransform, a TranslateTransform, a DropShadowEffect or a SolidColorBrush this
        // code-behind holds, and Animation.RunAsync(aTransform) throws InvalidCastException -
        // TransformAnimator casts its target to Visual and then owns that visual's
        // RenderTransform. So one shared 16ms DispatcherTimer steps a table of
        // (delay, duration, easing, apply) closures, the shape wire/71 landed for ChaosHudWindow.
        // ponytail: second copy of that pump - lift both into a shared TweenPump when a third
        // window needs one. The phase arithmetic is NOT copied: ChaosHudWindow.TweenPhase is
        // internal static and already carries its own assertions, so it is called.

        private sealed class Tween
        {
            public double Elapsed;              // ms since Start, delay included
            public double Delay;                // ms held on the from-value before moving
            public double Duration = 1;         // ms per pass
            public int Repeats = 1;             // -1 = forever
            public bool Alternate;              // reverse every other pass
            public Easing? Ease;
            public Action<double> Apply = _ => { };
            public Action? Done;
        }

        private readonly Dictionary<string, Tween> _tweens = new();
        private DispatcherTimer? _tweenTimer;
        private DateTime _tweenLastTick;
        private bool _closed;

        /// <summary>Start (or restart) a named animation, applying its from-value at once. Re-using
        /// a key REPLACES the running tween, which is what WPF's <c>BeginAnimation</c> did to a
        /// second animation on the same property.
        ///
        /// <para>Under <see cref="_renderSample"/> nothing is queued: the tween is applied at its
        /// SETTLED value and its completion runs inline. The headless render pumps the dispatcher
        /// twice and never ticks a timer, so without this the recap PNG would freeze on every
        /// entrance state - an invisible verdict line and half-size reward chips.</para></summary>
        private void StartTween(string key, double durationMs, Action<double> apply, Easing? ease = null,
                                int repeats = 1, bool alternate = false, double delayMs = 0, Action? done = null)
        {
            if (_closed) return;
            if (_renderSample)
            {
                // ease(1) == 1 for every easing, and an alternating forever tween settles where it
                // started, so the settled value is the same arithmetic without a clock.
                try { apply(repeats < 0 && alternate ? 0 : 1); done?.Invoke(); }
                catch (Exception ex) { Log.Debug("ChaosOverlay tween {Key} settle: {E}", key, ex.Message); }
                return;
            }
            _tweens[key] = new Tween
            {
                Delay = delayMs,
                Duration = durationMs,
                Repeats = repeats,
                Alternate = alternate,
                Ease = ease,
                Apply = apply,
                Done = done,
            };
            try { apply(ease?.Ease(0) ?? 0); }
            catch (Exception ex) { Log.Debug("ChaosOverlay tween {Key} seed: {E}", key, ex.Message); _tweens.Remove(key); return; }

            if (_tweenTimer is null)
            {
                _tweenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _tweenTimer.Tick += (_, _) =>
                {
                    var now = DateTime.UtcNow;
                    double dt = (now - _tweenLastTick).TotalMilliseconds;
                    _tweenLastTick = now;
                    StepTweens(dt);
                };
            }
            if (!_tweenTimer.IsEnabled) { _tweenLastTick = DateTime.UtcNow; _tweenTimer.Start(); }
        }

        /// <summary>Drop a named animation without applying anything: the caller paints the settled
        /// visual itself, exactly as WPF cleared with <c>BeginAnimation(prop, null)</c> and then
        /// assigned. Dropping a forever-tween is what stops it writing into a discarded brush -
        /// the leak the WPF original's <c>HideDraft</c> comment is about.</summary>
        private void StopTween(string key) => _tweens.Remove(key);

        private void StepTweens(double dtMs)
        {
            foreach (var pair in _tweens.ToList())
            {
                var tw = pair.Value;
                tw.Elapsed += dtMs;
                if (tw.Elapsed < tw.Delay) continue;
                double t = ChaosHudWindow.TweenPhase(tw.Elapsed - tw.Delay, tw.Duration, tw.Repeats, tw.Alternate, out bool finished);
                try { tw.Apply(tw.Ease?.Ease(t) ?? t); }
                catch (Exception ex)
                {
                    // Loud, not swallowed: an animation that throws every frame is otherwise an
                    // inert control that reviews clean.
                    Log.Warning("ChaosOverlay tween {Key} failed, dropped: {E}", pair.Key, ex.Message);
                    finished = true;
                }
                if (!finished) continue;
                _tweens.Remove(pair.Key);
                try { tw.Done?.Invoke(); }
                catch (Exception ex) { Log.Debug("ChaosOverlay tween {Key} completion: {E}", pair.Key, ex.Message); }
            }
            if (_tweens.Count == 0) _tweenTimer?.Stop();
        }

        private static Color LerpColor(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ============================ stubs ============================
        //
        // Everything below stands in for a WPF-head service so the call sites above port
        // unchanged. ponytail: each one is wired for real when its owner moves to Core; the
        // shapes here are exactly the members this view calls, nothing more.

        /// <summary>Stand-in for <c>Services.Chaos.ChaosBoon</c>, carrying only the fields the
        /// draft reads. ponytail: needs ChaosModels, wired when it moves to Core.</summary>
        public sealed class ChaosBoon
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string Desc { get; init; } = "";
            public string? Flavor { get; init; }
            public ChaosRarity Rarity { get; init; } = ChaosRarity.Common;
            public bool IsCurse { get; init; }
            public string[]? RequiresAny { get; init; }
            public string[]? RequiresAll { get; init; }
        }

        /// <summary>Mirrors <c>Services.Chaos.ChaosRarity</c>.</summary>
        public enum ChaosRarity { Common, Uncommon, Rare }

        /// <summary>Mirrors <c>Services.Chaos.ChaosRank</c>.</summary>
        public enum ChaosRank { Curious = 0, Tempted = 1, Slipping = 2, Entranced = 3, Devoted = 4, Claimed = 5 }

        /// <summary>The end-of-run snapshot the recap reads. WPF took the live
        /// <c>ChaosRunState</c> (an INotifyPropertyChanged model with ~40 members); the recap only
        /// ever reads these nine, and <c>s.Config.Difficulty.ToString()</c> flattens to a string.
        /// ponytail: needs ChaosRunState, wired when it moves to Core.</summary>
        public sealed class RunSummary
        {
            public double Score { get; init; }
            public int ActIndex { get; init; }
            public int WaveIndex { get; init; }
            public int BestCombo { get; init; }
            public double ElapsedSec { get; init; }
            public int Defused { get; init; }
            public int Detonated { get; init; }
            public int EffectsFired { get; init; }
            public string Difficulty { get; init; } = "";
        }

        private enum ChaosAnnounceKind { Willpower, Temptation }

        /// <summary>
        /// Not a stub any more: all thirteen cues in this window fire for real. The head's
        /// <c>Services/Chaos/ChaosSfx.cs</c> is a resolve plus a one-shot, and BOTH halves are
        /// portable now - <see cref="ContentLocator"/> is in Core and is the exact fallback that
        /// class ends on, <see cref="CoreAudio.PlayOneShot"/> is the seam its
        /// <c>App.Audio.PlayOneShot("chaos-sfx")</c> becomes, and the master-volume curve reads
        /// <c>CoreSettings.Current.MasterVolume</c>, which is the same property WPF reads. The
        /// candidate lists and scales below are copied cue for cue.
        ///
        /// <para>ponytail: the one half still missing is the MOD OVERRIDE. WPF resolves through
        /// <c>ModResourceResolver.ResolveAudioPath</c>, which probes the active mod's
        /// <c>resources/sounds/</c> (with a .wav/.mp3 swap) before falling back to
        /// ContentLocator. <c>CoreModArt.OverridePath</c> is NOT that seam - App.xaml.cs seeds it
        /// from <c>ModResourceResolver.ResolveUri</c>, the IMAGE chain - so a mod's replacement
        /// cue is not heard here. It needs a sounds twin of that provider in
        /// CCP.Core/CoreModArt.cs, seeded from <c>ResolveAudioPath</c>; a stock install already
        /// sounds right.</para>
        ///
        /// <para>Unseeded <c>CoreAudio</c> is silence, which is what this head is until a backend
        /// lands - and exactly what the stub gave. Nothing here can be louder than the user's
        /// master volume, and a missing file is still a silent no-op.</para>
        /// </summary>
        private static class ChaosSfx
        {
            /// <summary>Generic one-shot: <c>Resources/sounds/chaos/{name}.mp3</c>, silent when
            /// the asset is absent.</summary>
            public static void Play(string key, float volume) => PlayFirstAvailable(new[] { $"chaos/{key}.mp3" }, volume);

            /// <summary>A bright "dling" for rare, a dull "thud" otherwise - with the same
            /// bundled fallbacks and the same two scales as the head.</summary>
            public static void PlayBoonReveal(bool rare) => PlayFirstAvailable(
                rare ? new[] { "chaos/dling.mp3", "chime1.mp3" }
                     : new[] { "chaos/thud.mp3", "bubbles/Pop2.mp3" },
                rare ? 0.6f : 0.65f);

            public static void PlayBoonPicked() => PlayFirstAvailable(new[] { "chaos/boon_pick.mp3", "chime2.mp3" }, 0.7f);

            /// <summary>First candidate that exists on disk wins; a miss in all of them is
            /// silence, never an exception.</summary>
            private static void PlayFirstAvailable(string[] candidates, float scale)
            {
                try
                {
                    foreach (var rel in candidates)
                    {
                        // Reject traversal before touching the disk, as ResolveAudioPath does.
                        if (rel.Contains("..") || System.IO.Path.IsPathRooted(rel)) continue;
                        var path = ContentLocator.Resolve(System.IO.Path.Combine(
                            "Resources", "sounds", rel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
                        CoreAudio.PlayOneShot(path, Volume(scale), "chaos-sfx");
                        return;
                    }
                }
                catch (Exception ex) { Log.Debug("ChaosSfx resolve failed: {E}", ex.Message); }
            }

            /// <summary>The head's own curve: master volume times the cue's scale.</summary>
            private static float Volume(float scale)
            {
                try { return Math.Clamp(CoreSettings.Current.MasterVolume / 100f * scale, 0f, 1f); }
                catch { return scale; }
            }
        }

        private static class ChaosArt
        {
            // Bitmap, not IImage: an Image wants IImage and an ImageBrush wants
            // IImageBrushSource, and Bitmap is both - which is what the real ChaosArt hands back.
            public static Bitmap? Resolve(string folder, string id) => null;
            public static Bitmap? ResolveRecap() => null;
        }

        /// <summary>Real now. <c>Services/Chaos/ChaosTips.cs</c> is 40 lines of WPF control
        /// composition and nothing else - no service, no state - so it ports as-is and the draft
        /// cards get their hover card back instead of nothing. The chrome it set per-tooltip is
        /// the <c>ToolTip</c> selector in this window's Styles instead, exactly as
        /// <c>ChaosHudWindow.AttachTip</c> does it; <c>ToolTipService</c>'s show delay and duration
        /// have no Avalonia twin worth a converter here.
        /// <para>ponytail: this and <c>ChaosHudWindow.AttachTip</c> are now the same builder in two
        /// files. Collapse them into one <c>Views/Chaos/ChaosTips.cs</c> the moment a third caller
        /// appears - not before, because the head's own class is the source both are copying and a
        /// third copy is where drift starts.</para></summary>
        private static class ChaosTips
        {
            public static void Attach(Control target, string title, string? desc,
                                      string? extra = null, Color? accent = null, string? flavor = null)
            {
                var a = accent ?? Color.FromRgb(0xFF, 0x69, 0xB4);
                var sp = new StackPanel { MaxWidth = 260 };
                sp.Children.Add(new TextBlock
                {
                    Text = title, FontWeight = FontWeight.Bold, FontSize = 13,
                    Foreground = new SolidColorBrush(a),
                });
                if (!string.IsNullOrWhiteSpace(desc))
                    sp.Children.Add(new TextBlock
                    {
                        Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 3, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xE0, 0xE0, 0xF0)),
                    });
                if (!string.IsNullOrWhiteSpace(flavor))
                    sp.Children.Add(new TextBlock
                    {
                        Text = flavor, FontStyle = FontStyle.Italic, FontSize = 11,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
                    });
                if (!string.IsNullOrWhiteSpace(extra))
                    sp.Children.Add(new TextBlock
                    {
                        Text = extra, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 3, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                    });
                ToolTip.SetTip(target, sp);
            }
        }

        // ChaosBoonColors is no longer stubbed here: the head's class is a pure id -> family
        // colour table, so it is copied whole into ChaosBoonColors.cs beside this file and the
        // two call sites above now bind to that. Draft cards get their real family colour.

        private static class ChaosNarrator
        {
            public static void PlayCardLine(string? audioKey, string text) { }
            public static void EndCard() { }
        }

        private static class ChaosAnnouncerOverlay
        {
            public static void Announce(string text, ChaosAnnounceKind kind, string? artKey = null) { }
        }

        /// <summary>Real now, not a stand-in. The head's <c>ChaosWindowZ.BornTopmost</c> is its
        /// <c>PinTopmost</c> field, and the ONE place that writes it is
        /// <c>ChaosModeService.StartRun</c>: <c>PinTopmost = App.Settings.Current.ChaosPinOnTop</c>
        /// (reset to true when the run ends). That setting is in Core, and these windows only exist
        /// during a run, so reading it here IS the value WPF would be holding. A player who turned
        /// pin-on-top off was previously ignored - the XAML's <c>Topmost="True"</c> won.
        /// <para>ponytail: <c>ChaosWindowZ.DesktopMode</c> is NOT reproduced and is not needed for
        /// this - <c>RaiseTopmost</c> never reads it; it branches on <c>PinTopmost</c> alone.
        /// What stays head-side is the Win32 <c>SetWindowPos</c> re-assert itself, which Avalonia's
        /// <c>Topmost</c> covers on X11 (<c>_NET_WM_STATE_ABOVE</c>).</para></summary>
        private static class ChaosWindowZ
        {
            public static bool BornTopmost => CoreSettings.Current.ChaosPinOnTop;
        }

        private static class ChaosModeService
        {
            public const int ChaosRestartCountdownMs = 1000;
        }

        private static class RevealIds
        {
            public const string DraftSkip = "draft_skip";
        }

        private static class RevealService
        {
            public static bool IsUnlocked(string id) => true;
            public static void Sync(string id) { }
        }

        private sealed class MetaGoal
        {
            public string Name = "";
            public long Cost;
            public bool Affordable;
            public string? LessonId;
        }

        private sealed class MetaLesson
        {
            public string Text = "";
            public int Target;
        }

        private static class ChaosLessons
        {
            public static MetaLesson? ById(string id) => null;
            public static int Progress(string id) => 0;
        }

        /// <summary>The save model is ALREADY in Core (<see cref="ChaosMetaState"/>), so the
        /// recap reads the real one rather than a five-field copy of it — the five members below
        /// are all this view touches. What is still missing is the STORE: <c>ChaosMetaStore</c>
        /// loads and writes chaos_meta.json in the WPF head, so this instance is a fresh
        /// in-memory state seeded to a played save, and <see cref="Save"/> is a no-op.
        /// ponytail: needs a ChaosMeta seam; the swap is then this one initializer.</summary>
        private static class ChaosMeta
        {
            public const int FIRST_FALL_BONUS = 100;
            public static readonly ChaosMetaState State = new()
            {
                RunsCompleted = 3,
                BestScore = 17_050,
                Sparks = 1_120,
            };
            public static void Save() { }
            public static MetaGoal? NextGoal() => new() { Name = "porcelain mask", Cost = 1_500, Affordable = true };
        }

        private static class ChaosGlyphs
        {
            public const string Drops = "✦";
            public const string Xp = "🕰";
        }

        /// <summary>Not a stub any more. Both members this view calls are shipped COPY rather
        /// than behaviour, so they are reproduced verbatim from
        /// ConditioningControlPanel/Services/Chaos/ChaosRanks.cs instead of stood in for.
        /// <c>Line</c> returned <c>""</c> before, which drew an empty row inside the rank card.
        /// ponytail: the REST of that class - <c>For</c>, <c>Thresholds</c>, <c>RankSpecifics</c>,
        /// <c>CapstoneLockedTip</c> - reads <c>ChaosMeta.State</c> and stays head-side. This view
        /// calls none of it.</summary>
        private static class ChaosRanks
        {
            /// <summary>The head's <c>ChaosRanks.NameLower</c> is this exact mapping for all six
            /// ranks.</summary>
            public static string NameLower(ChaosRank r) => r.ToString().ToLowerInvariant();

            /// <summary>The one dim line under the bare rank word on the rank card. Ships
            /// verbatim, as it does in the head.</summary>
            public static string Line(ChaosRank r) => r switch
            {
                ChaosRank.Tempted   => "tempted. three times down. you can stop calling it curiosity.",
                ChaosRank.Slipping  => "slipping. the climb out takes longer every time. you noticed. you came anyway.",
                ChaosRank.Entranced => "entranced. you don't fall anymore. you arrive.",
                ChaosRank.Devoted   => "devoted. the dollhouse keeps a room warm for you now. it always knew it would.",
                ChaosRank.Claimed   => "claimed. it stopped counting your visits a long time ago. so did you.",
                _                   => "",
            };
        }

    }
}
