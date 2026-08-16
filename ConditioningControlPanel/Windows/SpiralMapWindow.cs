using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;

// NAMESPACE: root, NOT ConditioningControlPanel.Windows, matching all 41 other files in
// this folder. A `ConditioningControlPanel.Windows` namespace shadows the global WinRT
// `Windows` root inside every file in this assembly — ScreenOcrService's Windows.Graphics
// reference stops compiling the moment one exists. Do not "tidy" this into a folder
// namespace.
namespace ConditioningControlPanel
{
    /// <summary>
    /// THE EXPANDED SPIRAL — the same `/embed/spiral` canvas at ?mode=map, in its own
    /// window (CONTRACTS §9: "the 80px rail miniature and the expanded map are the same
    /// canvas", "click-to-expand opens the map in the same surface").
    ///
    /// ITS OWN WINDOW, DELIBERATELY. The map is the large surface, and a large WebView2
    /// inside a tab is the worst version of the airspace problem this app keeps running
    /// into (VatGlassCanvas's header; SettingsTabView's browser card). A top-level HWND
    /// has no airspace problem: nothing clips it, nothing animates over it, and closing
    /// it takes the browser with it. It is also the shape every other hosted page in the
    /// app already uses.
    ///
    /// SINGLETON, AND OPAQUE. One map at a time — a second click focuses the open one
    /// rather than starting a second browser. AllowsTransparency stays false: a WebView2
    /// does not paint inside a layered window, and this app has the render-thread scar
    /// tissue to prove it.
    ///
    /// FAILS SOFT LIKE THE RAIL: if the embed cannot start, the window closes itself
    /// rather than standing there as an empty rectangle. There is no map without the
    /// canvas, and a blank window is worse than no window.
    ///
    /// TWO DOORS IN (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16). <see cref="ShowMap"/>
    /// is the ordinary one: block-gated, withhold-gated, embed straight away. <see cref="ShowMapFirstLight"/>
    /// is the one that runs ONCE, immediately after a migration choice is committed — the
    /// same window with a four-second reveal laid over it and, crucially, no hard block
    /// gate, because at that moment the block may still be in flight. See the FIRST LIGHT
    /// section below.
    /// </summary>
    public sealed class SpiralMapWindow : Window
    {
        private static SpiralMapWindow? _open;

        /// <summary>~50fps, matching DescentFuseWindow's show clock. The reveal is a handful of
        /// ellipses and some trigonometry; a higher rate would buy nothing a projector could show.</summary>
        private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// How long the first-light window will wait for a descent block before giving up.
        ///
        /// <para>The commit fires an immediate sync (DescentMigrationService.ApplyChoice) and the
        /// block rides the profile poll behind it, so twelve seconds covers a slow network with
        /// room to spare — and the intro itself covers the first four of them. Longer than this and
        /// the user is looking at a held bloom wondering what broke, which is worse than the window
        /// quietly leaving: the spiral is unlocked either way, and every ordinary door into it
        /// works the moment the block lands.</para>
        /// </summary>
        private static readonly TimeSpan FirstLightBlockTimeout = TimeSpan.FromSeconds(12);

        private readonly SpiralEmbedView _embed;

        // ---- first light ----

        private readonly bool _firstLight;
        private readonly SpiralFirstLightVisual? _intro;
        private readonly bool _introReduced;
        private readonly Stopwatch _introClock = new();

        private DispatcherTimer? _introTimer;
        private DispatcherTimer? _blockWait;

        /// <summary>The INTRO clock, advanced by hand so it can be held at the bloom while the
        /// block is still in flight. See SpiralFirstLightTimeline's hold note.</summary>
        private double _introElapsed;

        private double _introLastTick;
        private bool _embedShown;
        private bool _firstLightStarted;

        /// <summary>
        /// TRUE while this window is still waiting for its first block. It is what stops
        /// <see cref="OnBlockChanged"/> closing the reveal: a null block during the wait means "not
        /// yet", not "withdrawn", and those are the same event with opposite meanings. Cleared by
        /// the first block that arrives, after which this window behaves exactly like an ordinary
        /// map — including closing on a withdrawal.
        /// </summary>
        private bool _awaitingFirstBlock;

        private SpiralMapWindow(bool firstLight)
        {
            Title = "The Spiral";
            Width = 900;
            Height = 700;
            MinWidth = 480;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            AllowsTransparency = false;
            UseLayoutRounding = true;
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));

            _firstLight = firstLight;

            _embed = new SpiralEmbedView("map");
            _embed.Failed += (_, _) =>
            {
                try { Close(); } catch { /* already going */ }
            };

            var root = new Grid();
            root.Children.Add(_embed);

            if (firstLight)
            {
                // THE AIRSPACE RULE, and it is the whole reason this window is shaped like this. A
                // WebView2 is a native child HWND: it paints OVER every WPF element in the same
                // window whatever the z-order says, so a reveal drawn on top of a live embed would
                // be invisible. The embed is therefore COLLAPSED and — more importantly — NOT
                // STARTED for the length of the intro.
                //
                // Not starting it is safe and is the pattern the embed already uses: Start() is
                // explicit and separate from construction precisely so a host can decide when the
                // browser exists (SpiralRailHost creates the view and starts it only once the rail
                // is wide enough, and tears the whole thing down when it is not). Nothing in
                // SpiralEmbedView needs to be visible, measured or arranged first: the WebView2
                // control is created inside InitAsync and added to the tree there, and PostState
                // queues until the canvas answers 'spiral:ready'. So the cost of the delay is the
                // browser's boot time landing at the END of the reveal instead of under it, which
                // is exactly the trade the reveal is for.
                _embed.Visibility = Visibility.Collapsed;

                // Reduced motion is the whole-reveal decision and it is taken ONCE, like the show's:
                // a sequence that re-read the gate every frame could change shape halfway through.
                _introReduced = MotionFx.Level != MotionLevel.Full;

                _intro = new SpiralFirstLightVisual(ResolveAccent());
                _intro.Begin(_introReduced);
                root.Children.Add(_intro);

                _awaitingFirstBlock = App.Descent?.Current is null;
            }

            Content = root;

            Loaded += (_, _) =>
            {
                if (_firstLight) StartFirstLight();
                else ShowEmbed();
            };

            // The map is a view of the same block the rail draws; a sync landing while it
            // is open should move the dot rather than wait for a reopen.
            if (App.Descent != null) App.Descent.BlockChanged += OnBlockChanged;

            Closed += (_, _) =>
            {
                if (App.Descent != null) App.Descent.BlockChanged -= OnBlockChanged;
                StopFirstLightTimers();
                _embed.Dispose();
                if (ReferenceEquals(_open, this)) _open = null;
            };
        }

        // ============================== the ordinary map ==============================

        private void ShowEmbed()
        {
            if (_embedShown) return;
            _embedShown = true;

            try
            {
                _embed.Visibility = Visibility.Visible;
                _embed.Start();
                _embed.PostState(App.Descent?.Current);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] map embed start: {E}", ex.Message); }
        }

        private void OnBlockChanged(object? sender, EventArgs e)
        {
            // DescentService already marshalled to the UI thread before raising.
            // A WITHDRAWN block (server dial narrowed, logout) closes the map — every other
            // spiral surface retracts on withdrawal, and a map showing a day-0 spiral for an
            // account the server just stopped serving is a leak, not a view (§2.5).
            try
            {
                var block = App.Descent?.Current;

                if (_awaitingFirstBlock)
                {
                    // THE BLOCK RACE. Until the first block lands, null means "the commit's sync has
                    // not come back yet" — the reveal is deliberately open BEFORE the record exists.
                    // Closing on it here would turn the app's own re-evaluation signal (the withhold
                    // flipping raises BlockChanged with no block behind it) into a window that shuts
                    // in the user's face a second after it opened.
                    if (block is null) return;

                    _awaitingFirstBlock = false;
                    StopBlockWait();
                    App.Logger?.Information("[Spiral] first light: the descent block landed — handing the window to the canvas.");
                    // The frame loop performs the hand-off, so the intro always finishes its fade
                    // rather than being cut off mid-bloom by an arriving sync.
                    return;
                }

                if (block is null) { Close(); return; }
                if (_embedShown) _embed.PostState(block);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] map state: {E}", ex.Message); }
        }

        // ============================== FIRST LIGHT ==============================

        /// <summary>
        /// Start the reveal's clock and, if the block is still in flight, the deadline that stops
        /// this window standing open forever waiting for it.
        /// </summary>
        private void StartFirstLight()
        {
            // Loaded can fire more than once for a window that is re-parented, and a restarted
            // clock would replay the reveal from the fog. Once is once.
            if (_firstLightStarted || _embedShown) return;
            _firstLightStarted = true;

            try
            {
                _introClock.Restart();
                _introLastTick = 0;

                _introTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = FrameInterval };
                _introTimer.Tick += OnIntroFrame;
                _introTimer.Start();

                // Paint frame zero now rather than 20ms from now: the window is already on screen,
                // and one flash of the bare background before the fog arrives is the single artefact
                // an opening window cannot hide.
                OnIntroFrame(this, EventArgs.Empty);

                if (_awaitingFirstBlock)
                {
                    _blockWait = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = FirstLightBlockTimeout };
                    _blockWait.Tick += OnBlockWaitElapsed;
                    _blockWait.Start();
                }

                App.Logger?.Information("[Spiral] first light opened ({Motion}, block {State}).",
                    _introReduced ? "reduced motion" : "full motion",
                    _awaitingFirstBlock ? "still in flight" : "already in hand");
            }
            catch (Exception ex)
            {
                // No clock, no reveal — but the map itself is still worth having, so fall through to
                // the ordinary behaviour rather than closing.
                App.Logger?.Debug("[Spiral] first light could not start its clock: {E}", ex.Message);
                FinishIntro();
            }
        }

        private void OnIntroFrame(object? sender, EventArgs e)
        {
            // CLAUDE.md async rules 6/8: a queued tick can land on a dispatcher that has begun
            // shutting down.
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (_embedShown) return;

            try
            {
                var now = _introClock.Elapsed.TotalSeconds;
                var dt = now - _introLastTick;
                _introLastTick = now;
                if (dt < 0) dt = 0;

                // THE HOLD. The intro runs freely up to the bloom; the last half second — the fade
                // that hands the window over — waits until there is something to hand it to. A user
                // on a slow network watches a held bloom instead of an empty rectangle, and the
                // deadline timer is what stops that hold being forever.
                var holdAt = SpiralFirstLightTimeline.HandoffStartSeconds(_introReduced);
                if (!_awaitingFirstBlock || _introElapsed < holdAt) _introElapsed += dt;

                _intro?.SetFrame(_introElapsed, now);

                if (SpiralFirstLightTimeline.IsComplete(_introElapsed, _introReduced)) FinishIntro();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] first-light tick failed: {E}", ex.Message);
                FinishIntro();
            }
        }

        /// <summary>
        /// The reveal is over: drop the layer and give the window to the canvas. Idempotent, and the
        /// only place the embed is started on the first-light path.
        /// </summary>
        private void FinishIntro()
        {
            if (_embedShown) return;

            StopIntroTimer();

            try
            {
                if (_intro != null)
                {
                    _intro.Visibility = Visibility.Collapsed;
                    if (Content is Grid root) root.Children.Remove(_intro);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] first-light teardown: {E}", ex.Message); }

            ShowEmbed();
        }

        /// <summary>
        /// The block never came. FAIL SOFT, LIKE THE RAIL: close rather than stand there as a lit
        /// rectangle with nothing in it. Nothing is lost — the withhold is already open, so every
        /// ordinary door into the spiral works the moment the block does land.
        /// </summary>
        private void OnBlockWaitElapsed(object? sender, EventArgs e)
        {
            StopBlockWait();
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (!_awaitingFirstBlock) return;

            App.Logger?.Information("[Spiral] first light: no descent block within {Seconds}s — closing the reveal quietly.",
                (int)FirstLightBlockTimeout.TotalSeconds);

            try { Close(); } catch (Exception ex) { App.Logger?.Debug("[Spiral] first-light close: {E}", ex.Message); }
        }

        private void StopIntroTimer()
        {
            if (_introTimer == null) return;
            try
            {
                _introTimer.Stop();
                _introTimer.Tick -= OnIntroFrame;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] first-light timer stop: {E}", ex.Message); }
            _introTimer = null;
        }

        private void StopBlockWait()
        {
            if (_blockWait == null) return;
            try
            {
                _blockWait.Stop();
                _blockWait.Tick -= OnBlockWaitElapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] first-light wait stop: {E}", ex.Message); }
            _blockWait = null;
        }

        private void StopFirstLightTimers()
        {
            StopIntroTimer();
            StopBlockWait();
            try { _introClock.Stop(); } catch { /* teardown race */ }
        }

        /// <summary>
        /// The mod's pink, or the app's own if the resource is missing — the same lookup the fuse
        /// show uses, and read as a Color rather than a Brush because the reveal builds thirty-three
        /// alpha steps from it.
        /// </summary>
        private Color ResolveAccent()
        {
            try
            {
                if (TryFindResource("PinkColor") is Color c) return c;
                if (TryFindResource("PinkBrush") is SolidColorBrush b) return b.Color;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] first-light accent lookup: {E}", ex.Message); }

            return Color.FromRgb(0xFF, 0x69, 0xB4);
        }

        // ============================== the doors ==============================

        /// <summary>Open the map, or focus the one already open.</summary>
        public static void ShowMap()
        {
            try
            {
                // Self-enforcing gate: every call site hangs off block-gated chrome, but the
                // invariant "no block ⇒ no map" belongs to the door, not to the hallway (§2.5).
                if (App.Descent?.Current is null) return;

                // AND "no map while the ceremony is still owed" (CONTRACT-FUSE-0816 §2.4). Same
                // reasoning: the surfaces that lead here are already withheld, but a deep link, a
                // stale click or a future caller must not be able to walk past the question. The
                // withhold opens the moment a choice is committed, which is why the first-light
                // door below does not need to re-test it.
                if (App.DescentMigration?.SpiralWithheld == true)
                {
                    App.Logger?.Debug("[Spiral] ShowMap refused — this account is still owed the migration ceremony.");
                    return;
                }

                Open(firstLight: false);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] ShowMap: {E}", ex.Message);
            }
        }

        /// <summary>
        /// THE FIRST LIGHT (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16) — the map opened FOR
        /// the user, once, seconds after they committed their migration choice, with the reveal
        /// played over it.
        ///
        /// <para><b>It does not gate on the block, and that is the point.</b> The commit fires an
        /// immediate sync but the descent record can easily still be in flight when this runs, and
        /// <see cref="ShowMap"/>'s "no block ⇒ no map" rule would turn the one moment this feature
        /// was built for into nothing happening at all. So this door opens on the choice instead,
        /// plays the intro over the wait, and hands the window to the canvas the moment the block
        /// lands — or closes itself quietly if it never does (<see cref="FirstLightBlockTimeout"/>).
        /// It is the same window and the same singleton either way; a map already on screen is
        /// simply focused, because there is nothing to reveal to somebody already looking at it.
        /// </para>
        ///
        /// <para>Ordinary 900x700 owned window, exactly like the door above — NOT fullscreen, NOT
        /// topmost, NOT layered. It therefore stays out of the panic key's ForceCloseAll list, which
        /// is for surfaces that can cover the screen; Escape reaches the global hook untouched
        /// because this window, like every other, never handles it.</para>
        /// </summary>
        public static void ShowMapFirstLight()
        {
            try
            {
                Open(firstLight: true);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] ShowMapFirstLight: {E}", ex.Message);
            }
        }

        private static void Open(bool firstLight)
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }

            var w = new SpiralMapWindow(firstLight);
            var main = Application.Current?.MainWindow;
            if (main != null && !ReferenceEquals(main, w)) w.Owner = main;
            _open = w;
            w.Show();
        }
    }
}
