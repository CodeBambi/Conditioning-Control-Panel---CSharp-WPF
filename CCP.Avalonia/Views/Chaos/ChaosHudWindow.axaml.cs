using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// Thin left-edge HUD for a Chaos run. Collapsed it shows a compact strip (clock,
    /// score, multiplier); on hover it slides out the full roguelite stack (boons,
    /// curses, shields, multiplier breakdown, payload feed, controls). The window only
    /// paints its left column - the rest is alpha-0 and click-through, so the desktop
    /// stays fully usable during a run.
    ///
    /// PORTED from ConditioningControlPanel/Chaos/ChaosHudWindow.xaml.cs. What changed and why:
    ///
    ///  - <c>ChaosRunState</c> and <c>ChaosModeService</c> are WPF-head services, so the window
    ///    binds to <see cref="ChaosHudState"/> at the bottom of this file instead - the same
    ///    property names and the same computed text, filled with sample values that hit every
    ///    visual branch (a hot streak, a low-focus warning, filled toy AND accessory groups,
    ///    run picks, modifiers, a feed). ponytail: needs <c>ChaosRunState</c> (and
    ///    <c>ChaosSidebarBoon</c>) from ConditioningControlPanel/Services/Chaos/ChaosModels.cs and
    ///    <c>ChaosModeService</c> from ConditioningControlPanel/Services/Chaos/ChaosModeService.cs.
    ///    Those paths are where every "needs ChaosX" note below points; they are not repeated.
    ///  - <b>Win32.</b> The WPF window P/Invoked <c>GetWindowLong</c>/<c>SetWindowLong</c> to add
    ///    <c>WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c>, and <c>SetWindowPos(HWND_TOPMOST)</c> via
    ///    <c>ChaosWindowZ</c>. Both map without a shim: the ex-styles are
    ///    <c>ShowInTaskbar="False"</c> + <c>ShowActivated="False"</c> in the XAML, and the
    ///    topmost re-assert is <c>Topmost</c>. No <c>DllImport</c> survives - a net8.0 head has
    ///    no user32. <c>X11Overlay.SetClickThrough</c> is deliberately NOT called: this HUD is
    ///    interactive, and WPF never set <c>WS_EX_TRANSPARENT</c> on it either (the unpainted
    ///    alpha-0 region is click-through by itself on both platforms).
    ///  - <b>Animation.</b> Every <c>DoubleAnimation</c>/<c>ColorAnimation</c> here is transient
    ///    juice (streak punch and shake, score pop, shield flash and pop, focus breath and blink,
    ///    the ready-glow breath, the clock's end-rush blink, the multiplier punch, the 180ms panel
    ///    slide). All of them now run, through the tween pump near the bottom of this file rather
    ///    than through Avalonia's keyframe <c>Animation</c>: each one drives a <c>Transform</c>,
    ///    a <c>DropShadowEffect</c> or a <c>SolidColorBrush</c> that this code-behind holds, and
    ///    <c>Animation.RunAsync</c> on a <c>Transform</c> throws <c>InvalidCastException</c>
    ///    (<c>TransformAnimator</c> casts its target to <c>Visual</c> and takes ownership of that
    ///    visual's <c>RenderTransform</c> - which would fight the <c>TransformGroup</c> assembled
    ///    in the ctor and the jitter timer that writes into it). Verified, not assumed.
    ///    The one animation that was already a timer - the hot-streak jitter - is ported as-is.
    ///    Still missing: the heat-bar shimmer, which is a brush <c>RelativeTransform</c> and has
    ///    no Avalonia twin at all (see <see cref="StartLustShimmer"/>).
    ///  - <b>Settings.</b> The persisted edge (<c>ChaosHudOnRight</c>) and the panic-key hint
    ///    read and write <c>CoreSettings.Current</c>, one for one with the <c>App.Settings</c>
    ///    pair WPF used, so the side switch survives a restart again.
    ///  - <c>Mouse.GetPosition</c> has no Avalonia twin (there is no global cursor query), so the
    ///    collapse grace tests the LAST SEEN pointer position instead; see CursorInHudGrace.
    ///  - <c>SystemParameters.WorkArea</c> -> <c>Screens</c>; <c>Left</c>/<c>Top</c> ->
    ///    <c>Position</c> (physical pixels, so the DIP sizes are scaled by <c>screen.Scaling</c>).
    ///  - <c>Visibility</c> -> <c>IsVisible</c>; <c>IsMouseOver</c> -> <c>IsPointerOver</c>;
    ///    <c>MouseEnter/Leave/MouseLeftButtonDown</c> -> <c>PointerEntered/Exited/Pressed</c>;
    ///    <c>FrameworkElement</c> -> <c>Control</c>; <c>ActualWidth</c> -> <c>Bounds.Width</c>;
    ///    <c>FindResource</c> -> <c>this.FindResource</c>; <c>App.Logger</c> -> Serilog's static
    ///    <c>Log</c>; <c>FontWeights.X</c> -> <c>FontWeight.X</c>; <c>ShadowDepth="0"</c> ->
    ///    <c>OffsetX/OffsetY = 0</c>.
    /// </summary>
    public partial class ChaosHudWindow : Window
    {
        private readonly ChaosHudState _state;
        private bool _expanded;
        private bool _closed;

        private int _lastShields;

        // ---- named parts (WPF got these as generated fields; Avalonia looks them up) ----
        private readonly Border _strip, _panel, _portraitHost, _sideLeftBtn, _sideRightBtn, _sideLeftBtn2, _sideRightBtn2;
        private readonly TextBlock _txtStripClock, _txtStripScore, _txtStreakLbl, _txtStreakNum, _txtStripMult,
                                   _rippleStripText, _txtActWave, _txtRunTime, _txtPanelScore, _txtPanelMult,
                                   _txtShields, _txtPauseHint, _txtHero,
                                   _sideLeftGlyph, _sideRightGlyph, _sideLeftGlyph2, _sideRightGlyph2,
                                   _hdrStack, _hdrResistance, _hdrFocus, _hdrPockets, _hdrAccessories,
                                   _hdrConditioning, _hdrModifiers, _hdrFeed;
        private readonly StackPanel _streakBlock, _focusStripBlock, _rippleStripBlock, _focusPanelBlock;
        private readonly Grid _rowStreak, _rowDifficulty, _rowLust, _rowMantras, _pauseChoiceRow;
        private readonly ProgressBar _focusStripBar, _focusPanelBar, _barRunProgress, _barLust;
        private readonly Image _portrait;
        private readonly Button _btnHero, _btnCloseMode;

        // Named RenderTransforms in the WPF XAML. Avalonia's FindControl only finds Controls, so
        // these are built here and attached, rather than declared in the markup.
        private readonly ScaleTransform _streakScale = new(1, 1);
        private readonly RotateTransform _streakRot = new(0);
        private readonly TranslateTransform _streakJitter = new(0, 0);
        private readonly TranslateTransform _panelSlide = new(-HIDDEN_OFFSET, 0);

        /// <summary>Render/discovery constructor: sample state, panel pinned open. The collapsed
        /// strip is only 116px of a 300px window, so the expanded panel is what a headless render
        /// has to prove - every templated control this view owns lives inside it.</summary>
        internal ChaosHudWindow() : this(ChaosHudState.Sample())
        {
            SetPreRunExpanded(true);
            // A headless render is two layout passes with no timer ticks, so the 180ms open would
            // otherwise be caught at whatever frame it happened to be on - park it open instead.
            StopTween("panel");
            _panelSlide.X = 0;
        }

        public ChaosHudWindow(ChaosHudState state)
        {
            AvaloniaXamlLoader.Load(this);

            _strip = this.FindControl<Border>("Strip")!;
            _panel = this.FindControl<Border>("Panel")!;
            _portraitHost = this.FindControl<Border>("PortraitHost")!;
            _sideLeftBtn = this.FindControl<Border>("SideLeftBtn")!;
            _sideRightBtn = this.FindControl<Border>("SideRightBtn")!;
            _sideLeftBtn2 = this.FindControl<Border>("SideLeftBtn2")!;
            _sideRightBtn2 = this.FindControl<Border>("SideRightBtn2")!;
            _txtStripClock = this.FindControl<TextBlock>("TxtStripClock")!;
            _txtStripScore = this.FindControl<TextBlock>("TxtStripScore")!;
            _txtStreakLbl = this.FindControl<TextBlock>("TxtStreakLbl")!;
            _txtStreakNum = this.FindControl<TextBlock>("TxtStreakNum")!;
            _txtStripMult = this.FindControl<TextBlock>("TxtStripMult")!;
            _rippleStripText = this.FindControl<TextBlock>("RippleStripText")!;
            _txtActWave = this.FindControl<TextBlock>("TxtActWave")!;
            _txtRunTime = this.FindControl<TextBlock>("TxtRunTime")!;
            _txtPanelScore = this.FindControl<TextBlock>("TxtPanelScore")!;
            _txtPanelMult = this.FindControl<TextBlock>("TxtPanelMult")!;
            _txtShields = this.FindControl<TextBlock>("TxtShields")!;
            _txtPauseHint = this.FindControl<TextBlock>("TxtPauseHint")!;
            _txtHero = this.FindControl<TextBlock>("TxtHero")!;
            _sideLeftGlyph = this.FindControl<TextBlock>("SideLeftGlyph")!;
            _sideRightGlyph = this.FindControl<TextBlock>("SideRightGlyph")!;
            _sideLeftGlyph2 = this.FindControl<TextBlock>("SideLeftGlyph2")!;
            _sideRightGlyph2 = this.FindControl<TextBlock>("SideRightGlyph2")!;
            _hdrStack = this.FindControl<TextBlock>("HdrStack")!;
            _hdrResistance = this.FindControl<TextBlock>("HdrResistance")!;
            _hdrFocus = this.FindControl<TextBlock>("HdrFocus")!;
            _hdrPockets = this.FindControl<TextBlock>("HdrPockets")!;
            _hdrAccessories = this.FindControl<TextBlock>("HdrAccessories")!;
            _hdrConditioning = this.FindControl<TextBlock>("HdrConditioning")!;
            _hdrModifiers = this.FindControl<TextBlock>("HdrModifiers")!;
            _hdrFeed = this.FindControl<TextBlock>("HdrFeed")!;
            _streakBlock = this.FindControl<StackPanel>("StreakBlock")!;
            _focusStripBlock = this.FindControl<StackPanel>("FocusStripBlock")!;
            _rippleStripBlock = this.FindControl<StackPanel>("RippleStripBlock")!;
            _focusPanelBlock = this.FindControl<StackPanel>("FocusPanelBlock")!;
            _rowStreak = this.FindControl<Grid>("RowStreak")!;
            _rowDifficulty = this.FindControl<Grid>("RowDifficulty")!;
            _rowLust = this.FindControl<Grid>("RowLust")!;
            _rowMantras = this.FindControl<Grid>("RowMantras")!;
            _pauseChoiceRow = this.FindControl<Grid>("PauseChoiceRow")!;
            _focusStripBar = this.FindControl<ProgressBar>("FocusStripBar")!;
            _focusPanelBar = this.FindControl<ProgressBar>("FocusPanelBar")!;
            _barRunProgress = this.FindControl<ProgressBar>("BarRunProgress")!;
            _barLust = this.FindControl<ProgressBar>("BarLust")!;
            _portrait = this.FindControl<Image>("Portrait")!;
            _btnHero = this.FindControl<Button>("BtnHero")!;
            _btnCloseMode = this.FindControl<Button>("BtnCloseMode")!;

            _streakBlock.RenderTransform = new TransformGroup
            { Children = { _streakScale, _streakRot, _streakJitter } };
            _panel.RenderTransform = _panelSlide;

            // WPF: Topmost = ChaosWindowZ.BornTopmost, which is its PinTopmost field, which
            // ChaosModeService.StartRun sets from AppSettings.ChaosPinOnTop. That setting is in
            // Core and this window only exists during a run, so the read below IS the value WPF
            // would be holding - a player who turned pin-on-top off is no longer overruled by the
            // XAML's Topmost="True" (which stays as the pinned default). On X11 Avalonia maps
            // Topmost to _NET_WM_STATE_ABOVE, which is the right mechanism.
            Topmost = CoreSettings.Current.ChaosPinOnTop;
            _state = state;
            DataContext = state;

            // Muscle Memory capstone feedback: pulse the resistance hearts whenever they GROW
            // (regen or a boon) so the player always knows a point came back. Window outlives no
            // run (closed in CleanupAfterRun), so no unsubscribe bookkeeping is needed.
            _lastShields = state.Shields;
            state.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ChaosHudState.FocusLow))
                {
                    SetFocusLowVisual(state.FocusLow);
                    return;
                }
                if (args.PropertyName == nameof(ChaosHudState.Combo))
                {
                    OnComboChanged(state.Combo);
                    return;
                }
                if (args.PropertyName == nameof(ChaosHudState.ScoreText))
                {
                    PulseScore();
                    return;
                }
                if (args.PropertyName == nameof(ChaosHudState.TotalMultText))
                {
                    OnMultiplierChanged();
                    return;
                }
                if (args.PropertyName == nameof(ChaosHudState.RippleReady))
                {
                    SetRippleReadyVisual(state.RippleReady);
                    return;
                }
                if (args.PropertyName == nameof(ChaosHudState.ClockText))
                {
                    UpdateClockEndRush();
                    return;
                }
                if (args.PropertyName != nameof(ChaosHudState.Shields)) return;
                int now = state.Shields;
                bool grew = now > _lastShields;
                _lastShields = now;
                if (grew) { PulseShields(); FlashShields(gain: true); }
            };
            SetFocusLowVisual(state.FocusLow);
            SetRippleReadyVisual(state.RippleReady);
            _lastCombo = state.Combo;                          // seeding must not read as a GAIN
            OnComboChanged(state.Combo);                       // seed the tier visuals
            OnMultiplierChanged();                             // seed the multiplier size/heat
            StartLustShimmer();                                // sweeping sheen on the heat bar

            // The pointer position the grace test reads. Avalonia cannot ask the platform where
            // the cursor is, so the window has to remember where it last saw it.
            PointerMoved += (_, e) => _lastPointer = e.GetPosition(this);

            // Never outlive the window. The collapse re-check RE-ARMS itself while the cursor sits in
            // the grace halo (see Hud_MouseLeave), and ChaosModeService closes the HUD without ever
            // calling Collapse() - so without this, a run that ends with the cursor near the sidebar
            // leaves a 220ms timer (and the whole visual tree it roots) alive forever, once per run.
            Closed += (_, _) =>
            {
                _closed = true;
                _tweens.Clear();
                _tweenTimer?.Stop();
                _streakJitterTimer?.Stop();
                _collapseRecheck?.Stop();
                _openDwell?.Stop();
            };

            // Top-anchored and ~60% of the work-area height, so it doesn't span the whole
            // screen (shrinks from the bottom up). Left/right edge is chosen by ApplySide.
            Height = WorkAreaDip().Height * 0.6;
            if (double.IsNaN(Height) || Height < 200) Height = 780;   // headless, or no screen yet
            ApplySide(CoreSettings.Current.ChaosHudOnRight);
            LoadPortrait();
            AttachHudTips();
        }

        /// <summary>The work area in DIPs, or an empty rect when there is no screen to ask (a
        /// headless render). WPF read <c>SystemParameters.WorkArea</c>, which is already in DIPs;
        /// Avalonia's <c>Screen.WorkingArea</c> is physical pixels, hence the scaling.</summary>
        private Rect WorkAreaDip()
        {
            try
            {
                var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
                if (screen is null) return default;
                double s = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                var wa = screen.WorkingArea;
                return new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
            }
            catch (Exception ex)
            {
                Log.Debug("ChaosHud work area: {E}", ex.Message);
                return default;
            }
        }

        /// <summary>A glint that sweeps the heat bar.
        /// <para>ponytail: still a stub, but NOT for the reason the old note gave. The gate is
        /// here - <c>ChaosSkiaFxOverlay.Enabled</c> is just <c>AppSettings.ChaosSkiaFxEnabled</c>,
        /// which is in Core (<c>CCP.Core/Models/AppSettings.cs</c>), and the pump below could
        /// drive the sweep. What is missing is the sweep's mechanism: WPF translated the
        /// five-stop <c>LinearGradientBrush</c>'s <c>RelativeTransform</c>, and an Avalonia brush
        /// has no <c>RelativeTransform</c> at all. Re-doing it means either re-offsetting the
        /// <c>GradientStop</c>s every frame or sliding <c>StartPoint</c>/<c>EndPoint</c> with
        /// <c>SpreadMethod.Repeat</c> - and neither is worth shipping unverified, because a brush
        /// that does not invalidate on the change is silently inert. The bar keeps its flat XAML
        /// orange, which is the settled frame of that sweep.</para></summary>
        private void StartLustShimmer()
        {
        }

        /// <summary>Themed hover cards for every sidebar element - exact numbers, lexicon voice.
        /// One text per concept, attached to both its strip and panel surfaces.</summary>
        private void AttachHudTips()
        {
            try
            {
                const string TIP_CLOCK =
                    "how long you've been down this descent (minutes:seconds).";
                const string TIP_SCORE =
                    "every pop and snap pays base points x the multiplier stack. at the recap the score banks into emotes ✦.";
                const string TIP_MULT =
                    "the whole stack multiplied out: streak x difficulty x lust x mantras (sins can stretch it further). every payout is scaled by this.";
                const string TIP_STREAK =
                    "+1 per pop or snap. each point adds +0.08x to the stack, capped at x6.0. a treat left to rot HALVES it; an unblocked trigger ZEROES it. it heats up at 5 / 10 / 20 / 35.";
                const string TIP_FOCUS =
                    "the defuse fuel. a hold costs 30 (15 per bound half). treats refill +10, rabbits and heavy drops +15, a denied tease +10. max 100, you fall in with 50. pressing a live bubble with less than 30 detonates it in your grip. snaps during a freeze are free.";
                const string TIP_RESIST =
                    "each ♥ absorbs one trigger: the effect still washes past, but your streak and lust survive (some sins demand 2). with none left, a trigger zeroes both. you fall in with 0 — charms, hearts and mantras grant it.";
                const string TIP_LUST =
                    "climbs while you perform (each snap +0.07) and pays up to x2.0 at full burn — the orange bar. an unblocked trigger cools it to zero.";
                const string TIP_RIPPLE =
                    "the right-click wave. cast it near the bubbles: treats pop paid, trances snap clean, rabbits get flung. one charge, gathered back over time — READY means it's in your hand.";

                AttachTip(_txtStripClock, "the fall", TIP_CLOCK);
                AttachTip(_txtStripScore, "score", TIP_SCORE);
                AttachTip(_txtStripMult, "the multiplier", TIP_MULT);
                AttachTip(_streakBlock, "streak", TIP_STREAK);
                AttachTip(_focusStripBlock, "focus", TIP_FOCUS);
                AttachTip(_rippleStripBlock, "the ripple", TIP_RIPPLE);

                AttachTip(_txtPanelScore, "score", TIP_SCORE);
                AttachTip(_txtPanelMult, "the multiplier", TIP_MULT);
                AttachTip(_txtActWave, "where you are", "the current act and loop of this descent. loops end with a draft; the last one ends the fall.");
                AttachTip(_hdrStack, "the multiplier stack", TIP_MULT);
                AttachTip(_rowStreak, "streak", TIP_STREAK);
                AttachTip(_rowDifficulty, "difficulty", "set by the pill you picked: Gentle x1.0, Teasing x1.3, Relentless x1.7, Inescapable x2.2.");
                AttachTip(_rowLust, "lust", TIP_LUST);
                AttachTip(_barLust, "lust", TIP_LUST);
                AttachTip(_rowMantras, "mantras", "every x-multiplier mantra you took this run, multiplied together. the picks themselves are listed under CONDITIONING.");
                AttachTip(_hdrResistance, "resistance", TIP_RESIST);
                AttachTip(_txtShields, "resistance", TIP_RESIST);
                AttachTip(_hdrFocus, "focus", TIP_FOCUS);
                AttachTip(_focusPanelBlock, "focus", TIP_FOCUS);
                AttachTip(_hdrPockets, "toys", "the active toys you took down — two pockets at most. hover a tile for its card; before the fall starts, clicking a tile takes it off.");
                AttachTip(_hdrAccessories, "accessories", "the accessories you wore down — two at most. hover a tile for its card; before the fall starts, clicking a tile takes it off.");
                AttachTip(_hdrConditioning, "conditioning", "the mantras and sins you accepted this run, in draft order. hover each for what it does.");
                AttachTip(_hdrModifiers, "modifiers", "your trained habits — always on, every descent. switch them at the Dollhouse, not here.");
                AttachTip(_hdrFeed, "the feed", "the last few things that happened down here, newest first.");
            }
            catch (Exception ex) { Log.Debug("AttachHudTips: {E}", ex.Message); }
        }

        /// <summary>ponytail: local twin of Services/Chaos/ChaosTips.Attach (title + wrapped body,
        /// pink title), hoist when that service moves to Core. The chrome it set per-tooltip is
        /// the ToolTip selector in this window's Styles instead; ToolTipService's show delay and
        /// duration have no Avalonia twin worth a converter here.</summary>
        private static void AttachTip(Control target, string title, string desc)
        {
            var sp = new StackPanel { MaxWidth = 260 };
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
            });
            sp.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xE0, 0xE0, 0xF0)),
            });
            ToolTip.SetTip(target, sp);
        }

        /// <summary>
        /// Sidebar portrait slot. WPF resolved art by convention through <c>ChaosArt.Resolve</c>
        /// and collapsed the host when no file was present.
        /// <para>ponytail: needs <c>Services/Chaos/ChaosArt.cs</c>, still in the WPF head because
        /// it returns <c>System.Windows.Media.ImageSource</c>. Only the RESOLUTION half is
        /// portable (<c>ChaosArt.PathFor</c> / <c>FilePath</c>, returning an absolute path each
        /// head decodes itself) - that is the half to move, as <c>CoreModArt</c> did for mod art.
        /// Whoever moves it: <c>Roots()</c> yields <c>App.UserAssetsPath</c> (= UserData/assets)
        /// and the callers then append <c>assets/Chaos/...</c>, so the user root probes a doubled
        /// <c>assets</c> segment; decide whether that is the intended layout before copying it.
        /// Until then this is the no-art path - what a fresh install renders anyway.</para>
        /// </summary>
        private void LoadPortrait()
        {
            _portrait.Source = null;
            _portraitHost.IsVisible = false;
        }

        private bool _pinnedOpen;   // pre-run loadout glance: panel stays open until SINK fires

        // Hover-expand swaps which surface is under a stationary cursor (strip hides, the
        // panel slides in over 180ms, tooltips pop their own window), so a spurious pointer-exit
        // can fire mid-transition - collapsing instantly flaps the sidebar open/shut until the
        // timing settles. So: never collapse within a grace window of opening, and treat every
        // leave as a debounced "re-check, then fold if truly gone".
        private const double OPEN_DWELL_MS = 1000;   // rest this long on the strip before it opens
        private const double EXPAND_GRACE_MS = 1000;
        private const double LEAVE_RECHECK_MS = 220;
        private DateTime _expandedAt;
        private DispatcherTimer? _collapseRecheck;
        private DispatcherTimer? _openDwell;
        private Point _lastPointer;

        // Opening is intent-gated: a brush-past on the way to a bubble near the edge shouldn't fling
        // the panel open. Hovering the strip ARMS a ~1s dwell; it expands only if the cursor is still
        // on the strip when it fires. Leaving (or clicking) cancels it — a click opens at once.
        private void Hud_MouseEnter(object? sender, PointerEventArgs e)
        {
            _lastPointer = e.GetPosition(this);
            _collapseRecheck?.Stop();
            if (_expanded || _pinnedOpen) return;
            if (_openDwell == null)
            {
                _openDwell = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(OPEN_DWELL_MS) };
                _openDwell.Tick += (_, _) =>
                {
                    _openDwell!.Stop();
                    if (_expanded || _pinnedOpen) return;
                    if (!_strip.IsPointerOver) return;   // moved on before the dwell elapsed
                    Expand();
                };
            }
            _openDwell.Stop();
            _openDwell.Start();
        }

        private void Strip_MouseLeave(object? sender, PointerEventArgs e) => _openDwell?.Stop();

        // Click the strip to open immediately — no need to wait out the dwell if you mean it. Clicks
        // on the side switch mark themselves handled, so they don't fall through to here.
        private void Strip_Click(object? sender, PointerPressedEventArgs e)
        {
            _openDwell?.Stop();
            if (!_expanded && !_pinnedOpen) Expand();
        }

        // The actual expand (was the body of Hud_MouseEnter): strip hides, the translucent panel
        // slides in from its parked off-edge offset.
        private void Expand()
        {
            _collapseRecheck?.Stop();
            if (_expanded) return;
            _expanded = true;
            _expandedAt = DateTime.UtcNow;
            _panel.IsVisible = true;
            _strip.IsVisible = false;   // the panel is translucent — don't let the strip bleed through
            Animate(0);
        }

        private void Hud_MouseLeave(object? sender, PointerEventArgs e)
        {
            _lastPointer = e.GetPosition(this);
            if (_pinnedOpen || !_expanded) return;
            double sinceOpen = (DateTime.UtcNow - _expandedAt).TotalMilliseconds;
            double wait = Math.Max(LEAVE_RECHECK_MS, EXPAND_GRACE_MS - sinceOpen);
            if (_collapseRecheck == null)
            {
                _collapseRecheck = new DispatcherTimer();
                _collapseRecheck.Tick += (_, _) =>
                {
                    try
                    {
                        _collapseRecheck!.Stop();
                        // Belt-and-braces against a stale tick that slipped past Closed: a dead
                        // window's laid-out bounds still contain the last pointer position, so
                        // CursorInHudGrace would say "still on the HUD" and re-arm us forever.
                        if (_closed) { _expanded = false; return; }
                        if (_pinnedOpen || !_expanded) return;
                        // #1050 field diagnostics: one Debug line per re-check so a repro log says which
                        // of the two questions disagreed (hit-test vs geometry) and where the panel was.
                        bool over = _panel.IsPointerOver, grace = CursorInHudGrace();
                        Log.Debug("ChaosHud collapse re-check: expanded={E} slideX={X:F0} cursor={C} panelOver={O} inGrace={G}",
                            _expanded, _panelSlide.X, _lastPointer, over, grace);
                        if (grace)
                        {
                            // Still on (or within the hysteresis halo of) the HUD. Keep POLLING rather than
                            // returning: the halo extends past the window, so no further pointer-exit will
                            // ever arrive to re-arm us, and a cursor parked just outside the edge would
                            // otherwise pin the sidebar open forever.
                            _collapseRecheck.Interval = TimeSpan.FromMilliseconds(LEAVE_RECHECK_MS);
                            _collapseRecheck.Start();
                            return;
                        }
                        Collapse();
                    }
                    catch (Exception ex) { Log.Debug("ChaosHud collapse re-check failed: {E}", ex.Message); }
                };
            }
            _collapseRecheck.Stop();
            _collapseRecheck.Interval = TimeSpan.FromMilliseconds(wait);
            _collapseRecheck.Start();
        }

        /// <summary>
        /// How far outside the HUD's own bounds the cursor still counts as "on the HUD" for the
        /// collapse grace. DIPs.
        /// </summary>
        private const double LEAVE_GRACE_MARGIN_DIP = 26;

        /// <summary>
        /// #1050 (sidebar folds itself away while the cursor is still on it). The exact trigger has NOT
        /// been reproduced in-house - it is reported against the Locked mod (Circe), but nothing in the
        /// tree themes this window per mod, so treat the mod as incidental until a repro log says
        /// otherwise (the re-check now logs one Debug line per tick for exactly that).
        ///
        /// <para>What IS wrong with the old test is structural: it asked
        /// <c>Panel.IsMouseOver || Strip.IsMouseOver</c>, and both are HIT-TEST questions, which answer
        /// "is this element the pointer's current target" - not "is the pointer on the HUD". They go
        /// false while the pointer has not moved at all whenever something else takes the hit: a
        /// tooltip popping its own window under the cursor (every boon tile carries one), or the
        /// panel's own slide transform still being mid-flight so its hit region is partly off-edge.
        /// <see cref="_strip"/> is worse than useless there: <see cref="Expand"/> hides it, and a
        /// hidden element is never hit-tested, so that half of the condition was dead the whole time
        /// it was expanded.</para>
        ///
        /// <para>So ask GEOMETRY instead - is the cursor inside the union of the two elements'
        /// laid-out bounds, grown by a hysteresis margin - which no popup, transform or visibility
        /// flip can answer wrongly.</para>
        ///
        /// <para>ponytail: WPF asked <c>Mouse.GetPosition(this)</c>, which answers even while the
        /// pointer is outside the window. Avalonia has no global cursor query, so this reads the
        /// LAST SEEN pointer position instead. Inside the window they agree; once the pointer has
        /// left, the halo test is answered against where it crossed the edge rather than where it
        /// is now, so a cursor that leaves and stops dead just outside can hold the panel open for
        /// one extra re-check tick. Needs a platform cursor probe to close.</para>
        /// </summary>
        private bool CursorInHudGrace()
        {
            try
            {
                return CursorInGrace(ElementBoundsInWindow(_panel), ElementBoundsInWindow(_strip),
                                     _lastPointer, LEAVE_GRACE_MARGIN_DIP);
            }
            catch
            {
                // Never collapse on a transform/layout hiccup: the pinned-open and dwell paths can
                // reopen it, but a spurious collapse mid-run is what was reported.
                return true;
            }
        }

        /// <summary>An element's bounds in this window's coordinate space, honouring render transforms
        /// (the panel rides a TranslateTransform). An empty rect when the element is not laid out.
        /// Visibility is deliberately NOT consulted: expanding HIDES the strip, but the strip's slot is
        /// still the physical edge zone the cursor is allowed to sit in.</summary>
        private Rect ElementBoundsInWindow(Control? el)
        {
            if (el == null || el.Bounds.Width <= 0 || el.Bounds.Height <= 0) return default;
            try
            {
                var origin = el.TranslatePoint(new Point(0, 0), this);
                if (origin is null) return default;
                return new Rect(origin.Value.X, origin.Value.Y, el.Bounds.Width, el.Bounds.Height);
            }
            catch { return default; }
        }

        /// <summary>Pure half of <see cref="CursorInHudGrace"/>: is <paramref name="cursor"/> inside the
        /// union of two (possibly empty) rects grown by <paramref name="margin"/> on every side? Empty
        /// rects contribute nothing, and two empty rects answer false. Avalonia's Rect is a struct with
        /// no Rect.Empty sentinel, so "empty" is a zero-or-negative extent here.</summary>
        internal static bool CursorInGrace(Rect a, Rect b, Point cursor, double margin)
        {
            if (margin < 0) margin = 0;
            static bool Empty(Rect r) => r.Width <= 0 || r.Height <= 0;
            var union = Empty(a) ? b : (Empty(b) ? a : a.Union(b));
            if (Empty(union)) return false;
            return union.Inflate(margin).Contains(cursor);
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            // 180ms uneased slide off the edge; the panel is only hidden once it has left, which
            // is what WPF's Completed handler did (and why the re-expand check is repeated there).
            double from = _panelSlide.X;
            StartTween("panel", 180, t => _panelSlide.X = from + (_hiddenX - from) * t, done: () =>
            {
                if (_expanded) return;
                _panel.IsVisible = false;
                _strip.IsVisible = true;
            });
        }

        // ---- left/right side switch (the strip's under-clock toggle) ----
        private const double HIDDEN_OFFSET = 300;   // = panel width: how far off-edge the collapsed panel parks
        private bool _onRight;
        private double _hiddenX = -HIDDEN_OFFSET;    // collapsed-panel rest offset; flips sign with the side

        /// <summary>Park the whole HUD on the chosen screen edge and mirror its chrome (rounded corner,
        /// borders, slide direction, switch highlight). Only ever called while collapsed — the switch
        /// lives on the strip, which is hidden once expanded — so re-seating the slide offset is safe.</summary>
        private void ApplySide(bool onRight)
        {
            _onRight = onRight;
            _hiddenX = onRight ? HIDDEN_OFFSET : -HIDDEN_OFFSET;

            // WPF set Left directly (DIPs). Avalonia positions in physical pixels, so the window
            // width has to be scaled before it can be subtracted from the work-area edge.
            try
            {
                var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
                if (screen is not null)
                {
                    var wa = screen.WorkingArea;
                    double s = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                    int x = onRight ? wa.X + wa.Width - (int)(Width * s) : wa.X;
                    Position = new PixelPoint(x, wa.Y);
                }
            }
            catch (Exception ex) { Log.Debug("ChaosHud position: {E}", ex.Message); }

            var align = onRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _strip.HorizontalAlignment = align;
            _panel.HorizontalAlignment = align;

            // Mirror the one rounded inner corner + the painted right/bottom borders to the new edge.
            _strip.CornerRadius = _panel.CornerRadius = onRight
                ? new CornerRadius(0, 0, 0, 18) : new CornerRadius(0, 0, 18, 0);
            _strip.BorderThickness = _panel.BorderThickness = onRight
                ? new Thickness(8, 0, 0, 8) : new Thickness(0, 0, 8, 8);

            // Keep the collapsed panel parked off the ACTIVE edge. A slide still in flight would
            // drag it back to the old edge's offset, so it is stopped before the seat is re-set.
            if (!_expanded) { StopTween("panel"); _panelSlide.X = _hiddenX; }

            // Both switches (strip + expanded-header) stay in sync — whichever is visible reads right.
            StyleSwitch(_sideLeftBtn, _sideRightBtn, _sideLeftGlyph, _sideRightGlyph, onRight);
            StyleSwitch(_sideLeftBtn2, _sideRightBtn2, _sideLeftGlyph2, _sideRightGlyph2, onRight);
        }

        private void StyleSwitch(Border leftBtn, Border rightBtn, TextBlock leftGlyph, TextBlock rightGlyph, bool onRight)
        {
            var pink = this.FindResource("Pink") as IBrush ?? Brushes.HotPink;
            var dim = this.FindResource("TextDim") as IBrush ?? Brushes.Gray;
            leftBtn.Background = onRight ? Brushes.Transparent : pink;
            rightBtn.Background = onRight ? pink : Brushes.Transparent;
            leftGlyph.Foreground = onRight ? dim : Brushes.Black;
            rightGlyph.Foreground = onRight ? Brushes.Black : dim;
        }

        private void SideLeft_Click(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;   // don't fall through to Strip_Click (which would expand the panel)
            if (!_onRight) return;
            ApplySide(false);
            PersistSide(false);
        }

        private void SideRight_Click(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            if (_onRight) return;
            ApplySide(true);
            PersistSide(true);
        }

        /// <summary>Remember the chosen edge across restarts. Compare-before-write, as WPF did:
        /// the switch handlers already early-out on a no-op, but a debounced save is not free.</summary>
        private void PersistSide(bool onRight)
        {
            try
            {
                if (CoreSettings.Current.ChaosHudOnRight == onRight) return;
                CoreSettings.Current.ChaosHudOnRight = onRight;
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Debug("ChaosHud side persist: {E}", ex.Message); }
        }

        /// <summary>Pin the panel open for the pre-run loadout glance (FALL IN → countdown), then
        /// release it when the run begins — it folds away unless the mouse is parked on it.</summary>
        public void SetPreRunExpanded(bool pinned)
        {
            _pinnedOpen = pinned;
            if (pinned)
            {
                _expanded = true;
                _panel.IsVisible = true;
                _strip.IsVisible = false;
                Animate(0);
            }
            else if (!_panel.IsPointerOver)
            {
                Collapse();
            }
        }

        /// <summary>Pocket Watch gate: the run clock + its fill bar only exist for players wearing
        /// the charm — without it, how long you've been under stays a mystery. The final-10s red
        /// flash rides the same gate: no watch, no countdown knowledge to flash.</summary>
        public void SetClockVisible(bool on)
        {
            _clockVisible = on;
            _txtRunTime.IsVisible = _barRunProgress.IsVisible = on;
        }

        private bool _clockVisible;
        private bool _endRushOn;

        /// <summary>Pocket Watch only: the last ten seconds of the descent turn the clocks red —
        /// the run gets a visible finale instead of stopping mid-streak. A Relapse extension that
        /// pushes the clock back out restores the calm look. Both clocks blink while it runs.</summary>
        private void UpdateClockEndRush()
        {
            try
            {
                double remaining = _state.RunDurationSec - _state.ElapsedSec;
                bool rush = _clockVisible && _state.ElapsedSec > 0 && remaining <= 10;
                if (rush == _endRushOn) return;
                _endRushOn = rush;
                var red = Color.FromRgb(0xFF, 0x5A, 0x5A);
                if (rush)
                {
                    _txtStripClock.Foreground = new SolidColorBrush(red);
                    _txtRunTime.Foreground = new SolidColorBrush(red);
                    _barRunProgress.Foreground = new SolidColorBrush(red);
                    StartTween("clockblink", 420, t => _txtStripClock.Opacity = _txtRunTime.Opacity = 1.0 - 0.75 * t,
                               repeats: -1, alternate: true);
                }
                else
                {
                    StopTween("clockblink");
                    _txtStripClock.Opacity = _txtRunTime.Opacity = 1.0;
                    _txtStripClock.Foreground = Brushes.White;
                    _txtRunTime.Foreground = this.FindResource("TextDim") as IBrush ?? Brushes.Gray;
                    _barRunProgress.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x7D, 0xBD));
                }
            }
            catch (Exception ex) { Log.Debug("ChaosHud end rush: {E}", ex.Message); }
        }

        /// <summary>READY carries a soft cyan glow on the strip's ripple readout; charging is dim.
        /// The dim/white split was a WPF DataTrigger on RippleReady, which has no Avalonia twin
        /// without a converter, so it is applied here alongside the glow.</summary>
        private void SetRippleReadyVisual(bool ready)
        {
            try
            {
                // Always drop the running breath first. WPF had to do this to stop a Forever
                // animation pinning a detached effect (and its render target) alive; here it
                // stops the closure writing into an effect the control no longer shows.
                StopTween("ripple");
                if (ready)
                {
                    _rippleStripText.Foreground = Brushes.White;
                    var glow = new DropShadowEffect
                    {
                        Color = Color.FromRgb(0x7A, 0xE0, 0xFF),
                        BlurRadius = 12,
                        OffsetX = 0,
                        OffsetY = 0,
                        Opacity = 0.3,
                    };
                    _rippleStripText.Effect = glow;
                    StartTween("ripple", 950, t => glow.Opacity = 0.25 + 0.70 * t,
                               new SineEaseOut(), repeats: -1, alternate: true);
                }
                else
                {
                    _rippleStripText.Foreground = this.FindResource("TextDim") as IBrush ?? Brushes.Gray;
                    _rippleStripText.Effect = null;
                }
            }
            catch (Exception ex) { Log.Debug("ChaosHud ripple visual: {E}", ex.Message); }
        }

        private bool _cursorOnLive;

        /// <summary>The cursor is resting on a live bubble (service-polled, 4x/s): both focus
        /// bars glow — "check your fuel first" lands exactly when the decision is being made.</summary>
        public void SetCursorOnLive(bool on)
        {
            if (on == _cursorOnLive) return;
            _cursorOnLive = on;
            try
            {
                foreach (var el in new Control[] { _focusStripBlock, _focusPanelBlock })
                    el.Effect = on
                        ? new DropShadowEffect
                          { Color = Color.FromRgb(0x7A, 0xE0, 0xFF), BlurRadius = 14, OffsetX = 0, OffsetY = 0, Opacity = 0.85 }
                        : null;
            }
            catch (Exception ex) { Log.Debug("ChaosHud cursor-on-live: {E}", ex.Message); }
        }

        /// <summary>Mirror the manual pause from EVERY entry point (HUD buttons or the panic key):
        /// paused pins the panel open on the continue-or-wake-up choice with the panic hint under
        /// it; resuming hands the panel back to hover. Never runs pre-run (pause needs a live field).</summary>
        public void SetPausedUi(bool paused)
        {
            try
            {
                _btnHero.IsVisible = !paused;
                _pauseChoiceRow.IsVisible = paused;
                var settings = CoreSettings.Current;
                _txtPauseHint.Text = settings.PanicKeyEnabled
                    ? $"⏸ HELD · {settings.PanicKey} again wakes you up"
                    : "⏸ HELD · the hole waits";
                _txtPauseHint.IsVisible = paused;
                _pinnedOpen = paused;
                if (paused)
                {
                    _expanded = true;
                    _panel.IsVisible = true;
                    _strip.IsVisible = false;
                    Animate(0);
                }
                else if (!_panel.IsPointerOver)
                {
                    Collapse();
                }
            }
            catch (Exception ex) { Log.Debug("ChaosHud paused UI: {E}", ex.Message); }
        }

        private bool _preRunMode;

        /// <summary>Warren-phase sidebar: the hero button reads FALL IN and starts the run from here;
        /// on the in-run HUD it reads PAUSE (and pausing asks continue-or-wake-up).</summary>
        public void SetHeroMode(bool preRun)
        {
            _preRunMode = preRun;
            _txtHero.Text = preRun ? "▶ FALL IN" : "⏸ PAUSE";
            _btnHero.IsVisible = true;
            _btnCloseMode.IsVisible = preRun;
            _pauseChoiceRow.IsVisible = false;
        }

        /// <summary>A pocket tile was clicked: a filled tile unequips its boon (the service ignores
        /// it once SINK has fired); an empty "+" tile brings the Warren forward on Enhancements.
        /// ponytail: needs ChaosModeService (UnequipFromSidebar / OpenWarrenAt), wired when the
        /// Chaos services move to Core. The tile id is already carried on Tag, as in WPF.</summary>
        private void PocketTile_Click(object? sender, PointerPressedEventArgs e)
        {
            var id = (sender as Control)?.Tag as string;
            Log.Debug("ChaosHud pocket tile clicked: {Id}", string.IsNullOrEmpty(id) ? "(empty slot)" : id);
        }

        /// <summary>Slide the panel to <paramref name="toX"/> over 180ms, eased out.</summary>
        private void Animate(double toX)
        {
            double from = _panelSlide.X;
            StartTween("panel", 180, t => _panelSlide.X = from + (toX - from) * t, new QuadraticEaseOut());
        }

        private bool _focusLowShown;

        /// <summary>Focus below a defuse's cost: both bars dim and the fill runs red — a readable
        /// "don't touch the live ones" warning. Restores full opacity the moment focus recovers.</summary>
        private void SetFocusLowVisual(bool low)
        {
            if (low == _focusLowShown) return;
            _focusLowShown = low;
            try
            {
                foreach (var el in new Control[] { _focusStripBlock, _focusPanelBlock })
                    ApplyFocusSteadyVisual(el);
                // Danger tint: below a defuse's price the fill itself runs red, not just dim —
                // the 30-mark tick on the bar shows exactly where healthy starts again.
                var target = low ? Color.FromRgb(0xE0, 0x45, 0x45)
                                 : Color.FromRgb(0x5A, 0xC8, 0xFA);
                foreach (var bar in new[] { _focusStripBar, _focusPanelBar })
                    bar.Foreground = new SolidColorBrush(target);
            }
            catch (Exception ex) { Log.Debug("ChaosHud focus-low visual: {E}", ex.Message); }
        }

        /// <summary>The steady focus-bar look for the current state: the 650ms low-focus breath
        /// between 0.75 and 0.35, or full opacity. Also what a one-shot flash hands the bars back
        /// to when it ends - which is why the flash shares this element's tween key.</summary>
        private void ApplyFocusSteadyVisual(Control el)
        {
            var key = FocusKey(el);
            if (_focusLowShown)
                StartTween(key, 650, t => el.Opacity = 0.75 - 0.40 * t,
                           new QuadraticEaseInOut(), repeats: -1, alternate: true);
            else
            {
                StopTween(key);
                el.Opacity = 1.0;
            }
        }

        /// <summary>One tween slot per focus block, shared by the breath and the flash so the two
        /// can never run into each other on the same Opacity.</summary>
        private string FocusKey(Control el) => ReferenceEquals(el, _focusStripBlock) ? "focus:strip" : "focus:panel";

        /// <summary>A NO FOCUS press just detonated a live bubble: three hard blinks on both focus
        /// bars so the eye lands on WHY, then the steady visual resumes.</summary>
        public void FlashFocusBar()
        {
            try
            {
                foreach (var el in new Control[] { _focusStripBlock, _focusPanelBlock })
                {
                    var target = el;   // captured per iteration, not by the loop variable
                    StartTween(FocusKey(el), 110, t => target.Opacity = 1.0 - 0.88 * t,
                               repeats: 6, alternate: true, done: () => ApplyFocusSteadyVisual(target));
                }
            }
            catch (Exception ex) { Log.Debug("ChaosHud focus flash: {E}", ex.Message); }
        }

        // ======================= streak juice (Balatro-style) =======================
        // The strip's STREAK readout heats through color tiers as the combo climbs, jitters
        // and glows when hot, punches on every gain and shakes hard on a drop. Driven from
        // the state PropertyChanged hook in the ctor — no service code involved.

        private int _streakTier;
        private int _lastCombo;
        private DispatcherTimer? _streakJitterTimer;
        private readonly Random _streakRng = new();

        private static readonly Color[] StreakTierColors =
        {
            Color.FromRgb(0xFF, 0xFF, 0xFF),   // 0: calm white
            Color.FromRgb(0xFF, 0xE0, 0x66),   // 1: warm gold   (5+)
            Color.FromRgb(0xFF, 0xA9, 0x4D),   // 2: orange      (10+)
            Color.FromRgb(0xFF, 0x5E, 0x5E),   // 3: red         (20+)
            Color.FromRgb(0xFF, 0x2E, 0x88),   // 4: fever pink  (35+)
        };

        private static int StreakTierFor(int combo)
            => combo >= 35 ? 4 : combo >= 20 ? 3 : combo >= 10 ? 2 : combo >= 5 ? 1 : 0;

        private void OnComboChanged(int combo)
        {
            try
            {
                bool gained = combo > _lastCombo;
                bool dropped = combo < _lastCombo;
                _lastCombo = combo;
                int prevTier = _streakTier;
                _streakTier = StreakTierFor(combo);
                var tierColor = StreakTierColors[_streakTier];

                // Settle visuals for the tier: number, color, size, glow. The brush and the glow
                // are fresh per change and held locally, so the flashes below animate THIS pair
                // and never a brush a later change has already replaced.
                _txtStreakNum.Text = "x" + combo;
                _txtStreakNum.FontSize = 28.8 + _streakTier * 3.0;   // ~20% larger: 28.8 → 40.8 at fever
                var brush = new SolidColorBrush(tierColor);
                _txtStreakNum.Foreground = brush;
                _txtStreakLbl.Foreground = _streakTier >= 2
                    ? new SolidColorBrush(Color.FromArgb(0xCC, tierColor.R, tierColor.G, tierColor.B))
                    : this.FindResource("TextDim") as IBrush ?? Brushes.Gray;
                var tierGlow = _streakTier >= 2
                    ? new DropShadowEffect
                      { Color = tierColor, BlurRadius = 8 + _streakTier * 4, OffsetX = 0, OffsetY = 0, Opacity = 0.9 }
                    : null;
                _txtStreakNum.Effect = tierGlow;

                if (gained)
                {
                    // White-hot flash settling into the tier colour + a spring punch.
                    StartTween("streak:color", 260, t => brush.Color = LerpColor(Colors.White, tierColor, t));
                    double from = 1.30 + _streakTier * 0.06;
                    StartTween("streak:scale", 340,
                               t => _streakScale.ScaleX = _streakScale.ScaleY = from + (1.0 - from) * t,
                               new BackEaseOut());
                    // Crossing into a hotter tier: bloom the glow wide, then let it contract.
                    if (_streakTier > prevTier && tierGlow is not null)
                    {
                        double rest = tierGlow.BlurRadius;
                        StartTween("streak:glow", 440, t => tierGlow.BlurRadius = rest + 26 * (1.0 - t),
                                   new CubicEaseOut());
                    }
                }
                else if (dropped)
                {
                    // Red flash settling into wherever the streak landed + a hard side shake.
                    var red = Color.FromRgb(0xFF, 0x38, 0x38);
                    StartTween("streak:color", 650, t => brush.Color = LerpColor(red, tierColor, t), delayMs: 120);
                    StartTween("streak:shake", 450, t => _streakJitter.X = ShakeOffsetAt(t));
                    StartTween("streak:scale", 380,
                               t => _streakScale.ScaleX = _streakScale.ScaleY = 0.80 + 0.20 * t,
                               new BackEaseOut());
                }

                UpdateStreakJitter();
            }
            catch (Exception ex) { Log.Debug("ChaosHud combo changed: {E}", ex.Message); }
        }

        /// <summary>Hot streaks (tier 2+) vibrate: tiny random offsets + a wobble of rotation,
        /// amplitude scaling with the tier. The timer only runs while hot. This one ports as-is —
        /// WPF drove it from a DispatcherTimer too, not from a storyboard.</summary>
        private void UpdateStreakJitter()
        {
            bool hot = _streakTier >= 2;
            if (hot)
            {
                if (_streakJitterTimer == null)
                {
                    _streakJitterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
                    _streakJitterTimer.Tick += (_, _) =>
                    {
                        try
                        {
                            // A drop-shake owns X while it runs. WPF got this free (an animation
                            // outranks a local set); here the two writers have to be sequenced.
                            if (_tweens.ContainsKey("streak:shake")) return;
                            double amp = (_streakTier - 1) * 0.9;             // 0.9 / 1.8 / 2.7 px
                            _streakJitter.X = (_streakRng.NextDouble() * 2 - 1) * amp;
                            _streakJitter.Y = (_streakRng.NextDouble() * 2 - 1) * amp;
                            _streakRot.Angle = (_streakRng.NextDouble() * 2 - 1) * (_streakTier - 1) * 1.6;
                        }
                        catch { }
                    };
                }
                if (!_streakJitterTimer.IsEnabled) _streakJitterTimer.Start();
            }
            else
            {
                _streakJitterTimer?.Stop();
                _streakJitter.X = 0; _streakJitter.Y = 0; _streakRot.Angle = 0;
            }
        }

        /// <summary>Colour flash on the resistance hearts: bright blue when a point lands, red
        /// when a hit arrives that resistance couldn't pay. Holds the flash colour for 160ms, then
        /// fades back to the hearts' XAML pink over 650ms. The stub before this ignored
        /// <paramref name="gain"/> entirely and painted pink either way, so a hit that emptied
        /// resistance flashed nothing at all.</summary>
        public void FlashShields(bool gain)
        {
            try
            {
                var hot = gain ? Color.FromRgb(0x5A, 0xC8, 0xFA) : Color.FromRgb(0xFF, 0x38, 0x38);
                var pink = Color.FromRgb(0xFF, 0x6E, 0xC7);
                var brush = new SolidColorBrush(hot);
                _txtShields.Foreground = brush;
                StartTween("shield:color", 650, t => brush.Color = LerpColor(hot, pink, t), delayMs: 160);
            }
            catch (Exception ex) { Log.Debug("ChaosHud shield flash: {E}", ex.Message); }
        }

        /// <summary>Brief elastic scale pop on the resistance hearts (a regen/gain just landed).
        /// Avalonia's ElasticEaseOut has no Oscillations/Springiness knobs, so the bounce reads
        /// looser than WPF's 2/5 - the arc and the 420ms are the same.</summary>
        private void PulseShields()
        {
            try
            {
                if (_txtShields.RenderTransform is not ScaleTransform st) return;
                StartTween("shield:pop", 420, t => st.ScaleX = st.ScaleY = 1.35 - 0.35 * t, new ElasticEaseOut());
            }
            catch (Exception ex) { Log.Debug("ChaosHud shield pulse: {E}", ex.Message); }
        }

        private DateTime _lastScorePulseUtc;

        /// <summary>A quick scale pop on the score when it ticks up. Throttled so the per-pop score
        /// storm doesn't machine-gun it — it reads as a lively climb, not a seizure.</summary>
        private void PulseScore()
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastScorePulseUtc).TotalMilliseconds < 220) return;
                _lastScorePulseUtc = now;
                if (_txtStripScore.RenderTransform is not ScaleTransform st)
                {
                    st = new ScaleTransform(1, 1);
                    _txtStripScore.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
                    _txtStripScore.RenderTransform = st;   // grow from the left edge
                }
                StartTween("score:pop", 240, t => st.ScaleX = st.ScaleY = 1.16 - 0.16 * t, new BackEaseOut());
            }
            catch (Exception ex) { Log.Debug("ChaosHud score pulse: {E}", ex.Message); }
        }

        // ---- Multiplier hero number: the rounded Fredoka digits grow bigger AND heat up (purple →
        // gold, with bloom) the higher the total multiplier climbs. ----
        private static readonly Color _multCold = Color.FromRgb(0xD2, 0x4D, 0xFF);   // mind-purple, x1
        private static readonly Color _multHot = Color.FromRgb(0xFF, 0xC8, 0x3D);    // jackpot gold, peak

        private static Color LerpColor(Color a, Color b, double t)
        {
            byte L(byte x, byte y) => (byte)(x + (y - x) * t);
            return Color.FromRgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
        }

        private double _lastMultShown = 1.0;
        private DateTime _lastMultPulseUtc;
        private SolidColorBrush? _multBrushStrip, _multBrushPanel;
        private DropShadowEffect? _multGlowStrip, _multGlowPanel;

        /// <summary>Resize + recolour the multiplier readout for the current total. <c>t</c> maps
        /// x1→x7 onto 0→1: scale climbs to ~1.65x, the colour lerps purple→gold, the bloom swells,
        /// and a gain punches the number (throttled, so the per-pop storm doesn't machine-gun it).</summary>
        private void OnMultiplierChanged()
        {
            try
            {
                double mult = _state.TotalMult;
                double t = Math.Clamp((mult - 1.0) / 6.0, 0, 1);
                double target = 1.0 + 0.65 * t;

                bool increased = mult > _lastMultShown + 0.001;
                _lastMultShown = mult;
                var now = DateTime.UtcNow;
                bool punch = increased && (now - _lastMultPulseUtc).TotalMilliseconds >= 170;
                if (punch) _lastMultPulseUtc = now;

                var col = LerpColor(_multCold, _multHot, t);
                var weight = t > 0.66 ? FontWeight.Black : t > 0.33 ? FontWeight.Bold : FontWeight.SemiBold;
                ApplyMult("strip", _txtStripMult, ref _multBrushStrip, ref _multGlowStrip, target, t, col, weight, punch);
                ApplyMult("panel", _txtPanelMult, ref _multBrushPanel, ref _multGlowPanel, target, t, col, weight, punch);
            }
            catch (Exception ex) { Log.Debug("ChaosHud multiplier changed: {E}", ex.Message); }
        }

        /// <summary>The brush and the glow are kept per readout and MUTATED, never replaced: the
        /// scale tween and the next state tick both have to land on the same objects, and the XAML
        /// foreground is an immutable brush that cannot be recoloured in place.</summary>
        private void ApplyMult(string key, TextBlock tb, ref SolidColorBrush? brush, ref DropShadowEffect? glow,
                               double target, double t, Color col, FontWeight weight, bool punch)
        {
            if (tb == null) return;
            if (tb.RenderTransform is not ScaleTransform st)
            {
                st = new ScaleTransform(1, 1);
                tb.RenderTransform = st;   // origin set to 50%,50% in XAML
            }
            double from = punch ? target * 1.16 : st.ScaleX;
            StartTween("mult:" + key, punch ? 330 : 200,
                       p => st.ScaleX = st.ScaleY = from + (target - from) * p, new BackEaseOut());

            if (brush is null) { brush = new SolidColorBrush(); tb.Foreground = brush; }
            brush.Color = col;
            tb.FontWeight = weight;

            if (glow is null) { glow = new DropShadowEffect { OffsetX = 0, OffsetY = 0 }; tb.Effect = glow; }
            glow.Color = col;
            glow.BlurRadius = 4 + 20 * t;
            glow.Opacity = 0.45 + 0.45 * t;
        }

        /// <summary>ponytail: needs ChaosModeService (StartRunFromSidebar / ToggleManualPause),
        /// wired when the Chaos services move to Core. The pre-run/in-run split is kept so the
        /// button's own two modes still read correctly.</summary>
        private void BtnHero_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("ChaosHud hero button: {Mode}", _preRunMode ? "fall in" : "pause");
        }

        /// <summary>ponytail: needs ChaosModeService (ToggleManualPause).</summary>
        private void BtnResume_Click(object? sender, RoutedEventArgs e) => SetPausedUi(false);

        /// <summary>ponytail: needs ChaosModeService (RequestStop).</summary>
        private void BtnExit_Click(object? sender, RoutedEventArgs e) => Close();

        /// <summary>Pre-run ✖ beside FALL IN: leave the rabbit hole entirely (Warren + sidebar).
        /// ponytail: needs ChaosModeService (CloseWarrenPhase).</summary>
        private void BtnCloseMode_Click(object? sender, RoutedEventArgs e) => Close();

        /// <summary>Re-assert the HUD to the top of the topmost band without stealing focus, so it
        /// stays visible over a mandatory video that a chaos payload raised mid-run.
        ///
        /// <para>WPF did this with <c>SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)</c> through
        /// <c>ChaosWindowZ</c>. Avalonia already maps <c>Topmost</c> to the X11 equivalent
        /// (<c>_NET_WM_STATE_ABOVE</c>), and toggling it re-asserts the state, so no shim is
        /// needed. Ordering against our OTHER overlays would be
        /// <c>X11Overlay.RestackAbove</c>; this HUD never did that on Windows either.
        ///
        /// <para>The demote branch is real now. <c>ChaosWindowZ.RaiseTopmost</c> reads
        /// <c>PinTopmost</c> - and ONLY that, never <c>DesktopMode</c> - to choose between
        /// re-asserting and dropping the window out of the topmost band; <c>PinTopmost</c> is set
        /// from <c>AppSettings.ChaosPinOnTop</c> in <c>ChaosModeService.StartRun</c>, and that
        /// setting is in Core. So the un-pinned run demotes here exactly as it does on
        /// Windows.</para></summary>
        public void RaiseToTopmost()
        {
            try
            {
                if (!CoreSettings.Current.ChaosPinOnTop) { Topmost = false; return; }
                Topmost = false; Topmost = true;
            }
            catch (Exception ex) { Log.Debug("ChaosHud raise: {E}", ex.Message); }
        }

        // ========================= the tween pump =========================
        // WPF drove this window's juice with BeginAnimation on Transforms, DropShadowEffects and
        // SolidColorBrushes the code-behind held. Avalonia's keyframe Animation cannot target
        // those: Animation.RunAsync(someTransform) throws InvalidCastException, because
        // TransformAnimator casts its target to Visual and then owns that visual's
        // RenderTransform - which would evict the TransformGroup the ctor assembles and fight the
        // 45ms jitter timer writing into it. So one shared timer steps a small table of
        // (duration, easing, apply) closures instead. That is also what the WPF original meant by
        // "a code-behind animation, no XAML storyboard, per the chaos render-thread contract".
        // ponytail: a 16ms dispatcher pump, not the compositor - it wakes the UI thread while
        // anything is moving and idles otherwise. If the HUD ever costs frames, the upgrade is
        // Avalonia Animations on the VISUALS (Opacity works there today; the transforms would
        // first have to become TransformOperations on RenderTransform instead of a group).

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

        /// <summary>Where a tween sits after <paramref name="elapsed"/> ms: the 0..1 position of
        /// the current pass, reversed on odd passes when alternating. <paramref name="finished"/>
        /// says the passes have run out, and the returned value is then the position it settles
        /// on - so an alternating tween with an odd pass count settles back at 0, and everything
        /// else settles at 1. Pure, and the only arithmetic in the pump worth getting wrong.</summary>
        internal static double TweenPhase(double elapsed, double durationMs, int repeats, bool alternate, out bool finished)
        {
            if (durationMs <= 0) { finished = repeats >= 0; return 1.0; }
            double raw = elapsed / durationMs;
            if (repeats >= 0 && raw >= repeats)
            {
                finished = true;
                return alternate && (repeats - 1) % 2 == 1 ? 0.0 : 1.0;
            }
            finished = false;
            int pass = (int)raw;
            double phase = raw - pass;
            return alternate && pass % 2 == 1 ? 1.0 - phase : phase;
        }

        /// <summary>Start (or restart) a named animation, applying its from-value at once. Re-using
        /// a key REPLACES the running tween, which is what WPF's BeginAnimation did to a second
        /// animation on the same property.</summary>
        private void StartTween(string key, double durationMs, Action<double> apply, Easing? ease = null,
                                int repeats = 1, bool alternate = false, double delayMs = 0, Action? done = null)
        {
            if (_closed) return;
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
            catch (Exception ex) { Log.Debug("ChaosHud tween {Key} seed: {E}", key, ex.Message); _tweens.Remove(key); return; }

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
        /// assigned. Dropping a forever-tween is what stops it writing into a detached effect.</summary>
        private void StopTween(string key) => _tweens.Remove(key);

        private void StepTweens(double dtMs)
        {
            foreach (var pair in _tweens.ToList())
            {
                var tw = pair.Value;
                tw.Elapsed += dtMs;
                if (tw.Elapsed < tw.Delay) continue;
                double t = TweenPhase(tw.Elapsed - tw.Delay, tw.Duration, tw.Repeats, tw.Alternate, out bool finished);
                try { tw.Apply(tw.Ease?.Ease(t) ?? t); }
                catch (Exception ex)
                {
                    // Loud, not swallowed: an animation that throws every frame is otherwise an
                    // inert control that reviews clean.
                    Log.Warning("ChaosHud tween {Key} failed, dropped: {E}", pair.Key, ex.Message);
                    finished = true;
                }
                if (!finished) continue;
                _tweens.Remove(pair.Key);
                try { tw.Done?.Invoke(); }
                catch (Exception ex) { Log.Debug("ChaosHud tween {Key} completion: {E}", pair.Key, ex.Message); }
            }
            if (_tweens.Count == 0) _tweenTimer?.Stop();
        }

        /// <summary>The streak-drop shake as a curve: WPF's eight linear key frames
        /// (0, -9, 8, -6, 5, -3, 2, 0 px) sampled at <paramref name="t"/> in 0..1.</summary>
        internal static double ShakeOffsetAt(double t)
        {
            double[] xs = { 0, -9, 8, -6, 5, -3, 2, 0 };
            double p = Math.Clamp(t, 0, 1) * (xs.Length - 1);
            int i = Math.Min((int)p, xs.Length - 2);
            return xs[i] + (xs[i + 1] - xs[i]) * (p - i);
        }
    }

    /// <summary>
    /// What the HUD binds to. The WPF window binds <c>ChaosRunState</c> (a WPF-head service);
    /// this carries the same property NAMES and the same computed text so the two XAMLs diff
    /// cleanly, and the sample values below hit every visual branch the view has - a hot streak
    /// (tier 3), a low-focus warning, both loadout groups filled, run picks, modifiers and a feed.
    ///
    /// The three WPF <c>Visibility</c> properties on the boon model become bools, because
    /// Avalonia binds <c>IsVisible</c> to a bool directly (CLAUDE.md).
    /// ponytail: needs ChaosRunState, wired when the Chaos services move to Core.
    /// </summary>
    public sealed class ChaosHudState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));

        private double _elapsedSec;
        public double ElapsedSec
        {
            get => _elapsedSec;
            set { _elapsedSec = value; OnChanged(); OnChanged(nameof(RunTimeText)); OnChanged(nameof(ClockText)); OnChanged(nameof(RunProgress)); }
        }
        public int RunDurationSec { get; set; } = 900;
        public double RunProgress => RunDurationSec <= 0 ? 0 : Math.Clamp(ElapsedSec / RunDurationSec, 0, 1);
        public string RunTimeText => $"{(int)ElapsedSec / 60:00}:{(int)ElapsedSec % 60:00} / {RunDurationSec / 60:00}:{RunDurationSec % 60:00}";
        public string ClockText => $"{(int)ElapsedSec / 60:00}:{(int)ElapsedSec % 60:00}";

        public int ActIndex { get; set; } = 2;
        public int WaveIndex { get; set; } = 3;
        public int WaveCount { get; set; } = 5;
        public string ActWaveText => $"DEPTH {ToRoman(ActIndex)} · LOOP {WaveIndex}/{WaveCount}";

        private double _score = 18420;
        /// <summary>Notifies, unlike the auto-property it replaces: the strip and panel readouts
        /// bind ScoreText, and the window's score pop hangs off that same notification - so a
        /// silent setter left the number frozen and the pop unreachable.</summary>
        public double Score
        {
            get => _score;
            set { _score = value; OnChanged(); OnChanged(nameof(ScoreText)); }
        }
        public string ScoreText => $"{(int)Score:N0}";

        private int _combo = 12;
        public int Combo
        {
            get => _combo;
            set { _combo = Math.Max(0, value); OnChanged(); OnChanged(nameof(ComboMult)); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); }
        }

        private double _heat = 0.35;
        public double Heat
        {
            get => _heat;
            set { _heat = Math.Clamp(value, 0, 1); OnChanged(); OnChanged(nameof(HeatMult)); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); }
        }

        private int _shields = 2;
        public int Shields
        {
            get => _shields;
            set { _shields = Math.Max(0, value); OnChanged(); OnChanged(nameof(ShieldText)); }
        }
        public int StartingShields { get; set; } = 3;
        public string ShieldText => string.Concat(Enumerable.Repeat("♥", Shields))
                                  + string.Concat(Enumerable.Repeat("♡", Math.Max(0, StartingShields - Shields)));

        public double FocusMax => 100;
        private double _focus = 22;
        public double Focus
        {
            get => _focus;
            set { _focus = Math.Clamp(value, 0, FocusMax); OnChanged(); OnChanged(nameof(FocusText)); OnChanged(nameof(FocusLow)); }
        }
        public string FocusText => $"{(int)Focus}";
        /// <summary>Below a defuse's price (30). The sample sits here on purpose: the low-focus
        /// visual is a state the render has to show.</summary>
        public bool FocusLow => Focus < 30;

        private double _rippleCooldown;
        public double RippleCooldown
        {
            get => _rippleCooldown;
            set { _rippleCooldown = value; OnChanged(); OnChanged(nameof(RippleReady)); OnChanged(nameof(RippleText)); }
        }
        public bool RippleReady => RippleCooldown <= 0;
        public string RippleText => RippleReady ? "READY" : $"{Math.Ceiling(RippleCooldown):0}s";

        public double ComboMult => Math.Min(1.0 + Combo * 0.08, 6.0);
        public double DifficultyMult { get; set; } = 1.3;
        public double HeatMult => 1.0 + Heat * 1.0;   // up to x2 at full heat
        private double _boonMult = 1.2;
        public double BoonMult
        {
            get => _boonMult;
            set { _boonMult = value; OnChanged(); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); }
        }
        public double TotalMult => ComboMult * DifficultyMult * HeatMult * BoonMult;
        public string TotalMultText => $"x{TotalMult:0.0}";

        public ObservableCollection<string> RecentEvents { get; } = new();
        public ObservableCollection<ChaosHudBoon> ActiveSidebarToys { get; } = new();
        public ObservableCollection<ChaosHudBoon> ActiveSidebarAccessories { get; } = new();
        public ObservableCollection<ChaosHudBoon> RunPickTiles { get; } = new();
        public ObservableCollection<ChaosHudBoon> RunModifiers { get; } = new();

        // The three WPF Style.Triggers that asked a collection's Count, as bools the XAML binds
        // IsVisible to directly.
        public bool HasToys => ActiveSidebarToys.Count > 0;
        public bool HasAccessories => ActiveSidebarAccessories.Count > 0;
        public bool ShowGroupSeam => HasToys && HasAccessories;

        private static string ToRoman(int n) => n switch
        {
            1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
            _ => n.ToString(),
        };

        /// <summary>Sample data for the render proof and the design-time view.</summary>
        public static ChaosHudState Sample()
        {
            var s = new ChaosHudState { ElapsedSec = 372 };
            s.ActiveSidebarToys.Add(new ChaosHudBoon
            {
                // ponytail: "toy_wand" / "acc_collar" are invented sample ids, not real boon ids
                // (those are e_stim, the_spanker, tail_plug, gold_digger ... - see
                // ChaosBoonColors.cs). So neither tile is in the family-colour table and both
                // render on the category fallback, which means the render CANNOT prove
                // AccentBrush's lookup. Swap in real ids when ChaosSidebarBoon lands and the
                // sample can be built from the real catalogue instead of guessed at.
                Id = "toy_wand", Glyph = "🪄", Name = "Wand", Level = 3,
                Desc = "a long press winds it up; let go and the nearest treat pops paid.",
                Flavor = "it hums when you hold it too long.",
                Extra = "capstone: pops two at once above streak x20.",
            });
            s.ActiveSidebarAccessories.Add(new ChaosHudBoon
            {
                Id = "acc_collar", Glyph = "⛓", Name = "Collar", Level = 2,
                Desc = "start each descent with one extra ♥.",
                Flavor = "snug.",
            });
            s.RunPickTiles.Add(new ChaosHudBoon
            {
                Glyph = "✦", Name = "good girls count", Desc = "x1.2 to the whole stack.",
            });
            s.RunPickTiles.Add(new ChaosHudBoon
            {
                Glyph = "☠", Name = "the sink", IsCurse = true,
                Desc = "loadout locks the moment you fall in.",
                Flavor = "no take-backs.",
            });
            s.RunModifiers.Add(new ChaosHudBoon { Glyph = "◈", Name = "steady hands", IsModifier = true, Desc = "focus refills 10% faster." });
            s.RunModifiers.Add(new ChaosHudBoon { Glyph = "❂", Name = "deep breath", IsModifier = true, Desc = "the first trigger of a loop is free." });
            s.RecentEvents.Add("treat popped · +240");
            s.RecentEvents.Add("streak x12");
            s.RecentEvents.Add("rabbit flung");
            s.RecentEvents.Add("tease denied · focus +10");
            return s;
        }
    }

    /// <summary>One tile in the HUD: a pocket toy, an accessory, a run pick or a modifier.
    /// The port of <c>ChaosSidebarBoon</c>, minus the WPF Visibility properties (bools here) and
    /// with Avalonia brushes. The payload-family colour lookup is real now: the head's
    /// <c>AccentBrush</c> calls <c>ChaosBoonColors.BrushForOrDefault</c> over the category
    /// fallback - not visible from ChaosHudWindow.xaml.cs, which is why the old note said the
    /// fallback palette WAS the lookup - and that table is copied into ChaosBoonColors.cs beside
    /// this file. ponytail: still needs <c>ChaosSidebarBoon</c> itself
    /// (ConditioningControlPanel/Services/Chaos/ChaosModels.cs) to carry real run data into these
    /// tiles; every value below is sample.</summary>
    public sealed class ChaosHudBoon
    {
        public string Id { get; init; } = "";
        public IImage? Icon { get; init; }
        public string Glyph { get; init; } = "◈";
        public string Name { get; init; } = "";
        public int Level { get; init; }
        public string Desc { get; init; } = "";
        public string Flavor { get; init; } = "";
        /// <summary>Capstone line for the hover card (gold). Empty = hidden.</summary>
        public string Extra { get; init; } = "";
        public bool IsCurse { get; init; }
        /// <summary>Owned always-on upgrade (the MODIFIERS list) — purple tile.</summary>
        public bool IsModifier { get; init; }
        /// <summary>An unfilled pocket slot (dim "+" tile shown during the pre-run loadout glance).</summary>
        public bool IsEmptySlot { get; init; }
        public string LevelText => $"L{Level}";

        // ---- hover card + tile accents ----
        public string TipTitle => Level > 0 ? $"{Name} · L{Level}" : Name;
        public bool HasLevelBadge => Level > 0;
        public bool HasDesc => !string.IsNullOrEmpty(Desc);
        public bool HasFlavor => !string.IsNullOrEmpty(Flavor);
        public bool HasExtra => !string.IsNullOrEmpty(Extra);

        /// <summary>The category fallback, then the payload-based colour language over it -
        /// <c>ChaosSidebarBoon.AccentBrush</c>'s exact shape, including its "an empty slot keeps
        /// the fallback" branch. The lookup used to be missing, so every tile drew its category
        /// colour and a mapped boon never showed its family.</summary>
        public IBrush AccentBrush
        {
            get
            {
                var fallback = IsEmptySlot ? EmptyAccent : IsModifier ? ModAccent
                             : IsCurse ? CurseAccent : Level > 0 ? PocketAccent : BoonAccent;
                return IsEmptySlot ? fallback : ChaosBoonColors.BrushForOrDefault(Id, fallback);
            }
        }
        public IBrush TileBackBrush =>
            IsEmptySlot ? Brushes.Transparent : IsModifier ? ModBack : IsCurse ? CurseBack : Level > 0 ? PocketBack : BoonBack;
        public double TileOpacity => IsEmptySlot ? 0.55 : 1.0;

        private static IBrush Frozen(Color c) => new ImmutableSolidColorBrush(c);
        private static readonly IBrush EmptyAccent = Frozen(Color.FromArgb(0x60, 0xB8, 0xB8, 0xD0));
        private static readonly IBrush PocketAccent = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
        private static readonly IBrush BoonAccent = Frozen(Color.FromRgb(0x9C, 0xE8, 0xA0));
        private static readonly IBrush CurseAccent = Frozen(Color.FromRgb(0xFF, 0x8A, 0x8A));
        private static readonly IBrush ModAccent = Frozen(Color.FromRgb(0x8B, 0x5C, 0xF6));
        private static readonly IBrush PocketBack = Frozen(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));
        private static readonly IBrush BoonBack = Frozen(Color.FromArgb(0x2E, 0x9C, 0xE8, 0xA0));
        private static readonly IBrush CurseBack = Frozen(Color.FromArgb(0x2E, 0xFF, 0x8A, 0x8A));
        private static readonly IBrush ModBack = Frozen(Color.FromArgb(0x2E, 0x8B, 0x5C, 0xF6));
    }
}
