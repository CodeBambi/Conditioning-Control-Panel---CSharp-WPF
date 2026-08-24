using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE BANK (House Book) on XP: the seventh event moment, and the only recurring one.
    ///
    /// <para><b>The move.</b> Value spawns at its SOURCE as 4-10 tokens, flies a slight arc to the
    /// counter in the header, and the counter ticks up as each token lands - a mini-thud, a scale
    /// pop and one flash across the XP bar on the last. The whole point is that XP stops being a
    /// number that silently changed and becomes something that visibly arrived from somewhere.</para>
    ///
    /// <para><b>Why it needs a banker.</b> XP in this app does not arrive as events, it arrives as
    /// weather: a flash, a mantra, an attention check and a bubble pop can all settle inside half a
    /// second, and thirty call sites feed <c>AddXP</c>. Celebrating each one is noise.
    /// <see cref="BankAccumulator"/> pools awards into rare, deliberate flights (1.5s collection
    /// window, 3s floor between launches) and this file is only its shell: anchors, a canvas, a
    /// counter and the rails that guarantee the display ends up on the truth no matter what.</para>
    ///
    /// <para><b>Only completions fly.</b> Pooling made the noise tidy; it did not make it rare. The
    /// guest list is <see cref="BankAccumulator.IsBankable"/> - a quest, a session, a lock card, the
    /// counting game - and every other source is refused at <see cref="OnBankXpAwarded"/>, where the
    /// display hold its <c>XPChanged</c> armed is handed straight back so the ordinary odometer
    /// tween runs. Weather gets no tokens, no thud, no hold. Because the moment now costs something
    /// to reach, it is dialled LOUDER than it was when it fired every few seconds: more tokens, a
    /// bigger pop, and the bar flash below.</para>
    ///
    /// <para><b>The counter is HELD, the ledger is not.</b> House Law I: <c>settings.PlayerXP</c>
    /// is written the instant the award lands, exactly as before - nothing in this file touches the
    /// ledger, reads back into it, or can delay it. What is staged is the READOUT: while a pot is
    /// open or a flight is in the air, <see cref="TryHoldXpDisplay"/> swallows the odometer tween
    /// and remembers the target instead, and the tokens are what release it. Every path out of that
    /// hold ends with the display standing on the ledger's number (see
    /// <see cref="ReleaseXpHold"/>), which is why the failsafes below are not optional decoration.</para>
    ///
    /// <para><b>Arming, and the one award that always leaks.</b> The hold has to be armed BEFORE
    /// <c>OnXPChanged</c> runs, because that is what tweens the counter - so this file subscribes
    /// <see cref="OnBankXpChanged"/> to <c>XPChanged</c> immediately ahead of the window's own
    /// handler (see the wiring in MainWindow.xaml.cs) and resolves the arm on the
    /// <c>XPAwarded</c> that ProgressionService always raises straight afterwards, on the same call
    /// stack. If that ordering ever slipped, the failure is one award's worth of XP arriving early
    /// rather than a broken counter - <see cref="BankCounterScript.Target"/> clamps a flight to the
    /// live ledger precisely so an already-credited award cannot make the tokens overshoot.</para>
    ///
    /// <para><b>The landing flourish.</b> The last token also flashes <c>XPBarFlashOverlay</c>
    /// through <c>FlashOverlay</c> - the same overlay and the same animation the level-up bloom and
    /// the Brain Parasite drain already use, so there is exactly one owner of that opacity and
    /// nothing to fight. <c>XPBarSheen</c> is deliberately left alone: ChromeFx's ambient loop owns
    /// its opacity AND its gradient stops, and a second driver would stamp on a forever-repeating
    /// storyboard.</para>
    ///
    /// <para><b>Rails.</b> Every hook here is fire-and-forget safe and wrapped. THE BANK is skipped
    /// outright - the whole staging path, not just the particles - whenever
    /// <c>EventFxAllowed</c> says no (reduced motion, no particle budget, window inactive or
    /// minimised), so under MotionLevel Reduced/Off the XP display behaves byte-identically to how
    /// it did before this file existed. No idle clock: the poll runs only while a pot is open or a
    /// flight is alive, and the token canvas holds no timer between flights.</para>
    ///
    /// <para><b>Deliberately unstaged:</b> the profile bubble's own mini XP readout
    /// (<c>OnBubbleXPChanged</c>, MainWindow.ProfileBubble.cs). It may briefly lead the held
    /// counter by a pot, which is fine - it is a 20px secondary readout, not the moment - and
    /// staging it would double this file's surface for nothing.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- DIALS ----------------------------------------------------------------
        // Collection window and launch cooldown are NOT here: they are BankAccumulator.WindowMs
        // and BankAccumulator.CooldownMs, next to the logic that spends them. Token count, stagger,
        // duration and arc bow live in BankFlightPlan for the same reason.

        /// <summary>
        /// Slack around the origin-to-target bounding box, in window pixels. The tokens' bezier
        /// control point bows off the straight line by up to 22% of the distance travelled, and the
        /// glow sprite is drawn ~36px wide at its largest (a 9px core at AmbientFxCanvas's 4.0 halo
        /// scale) - 100px holds both without the arc ever clipping on the surface's edge. Grown
        /// with the glow when the flight became a rare celebration; the old 80 was sized for a 24px
        /// halo and would have shaved the fattest tokens against the box edge.
        /// </summary>
        private const double BankBoxPadPx = 100;

        /// <summary>
        /// Hard ceiling on either edge of the token surface. Not a feel dial - a failsafe. Anchor
        /// rectangles come out of <c>TransformToVisual</c>, and a detached or mid-layout element can
        /// hand back coordinates in the tens of thousands; a Skia surface sized from that is a
        /// multi-hundred-megabyte allocation. Anything past this is treated as a bad anchor.
        /// </summary>
        private const double BankBoxMaxPx = 2600;

        /// <summary>
        /// How far left of the XP track the neutral origin sits, in window pixels. Used only when
        /// even the profile bubble cannot be mapped: it puts the spawn out in the header's empty
        /// space so the tokens still travel a legible distance to the counter.
        /// </summary>
        private const double BankFallbackOriginDx = -180;

        /// <summary>Poll interval for pot ripening, the flight watchdog and the hold self-heal.</summary>
        private const int BankPollMs = 250;

        /// <summary>
        /// Longest the counter may stay held for one flight before the shell stops believing in it.
        /// Generously past the worst legal envelope (10 tokens: ~720ms of stagger plus a 650ms
        /// flight), because this is the rail that catches an outcome nobody predicted, not a timer
        /// anything is meant to run up against.
        /// </summary>
        private const double BankWatchdogMs = 6000;

        /// <summary>
        /// Counter pop on the last landing. It used to be small (1.12) because the flight fired
        /// every few seconds and a 11px readout jumping that often reads as a twitch. Now that only
        /// a completion gets here it is allowed to be a catch: a bigger throw, and a longer, softer
        /// spring so the extra travel settles instead of snapping.
        /// </summary>
        private const double BankPopScale = 1.28;
        private const double BankPopOutMs = 90;
        private const double BankPopSpringMs = 300;

        /// <summary>Spring overshoot on the way back down. Rises with the pop so the settle keeps its weight.</summary>
        private const double BankPopSpringAmplitude = 0.9;

        /// <summary>Mini-thud, ChaosSfx-style: the override slot ships EMPTY and the fallback carries it.</summary>
        private const string BankThudOverride = "ui/bank_thud.mp3";
        private const string BankThudFallback = "bubbles/Pop2.mp3";
        /// <summary>
        /// Quiet on purpose. At 0.35 the thud was a recurring app noise; the flight is now a rare
        /// completion cue and a rare cue does not have to shout to be heard.
        /// </summary>
        private const float BankThudScale = 0.22f;
        private const string BankThudTag = "ui-bank";

        // ---- state -----------------------------------------------------------------

        private BankAccumulator? _bank;
        private DispatcherTimer? _bankPoll;

        /// <summary>Monotonic clock for the accumulator and the watchdog. Never <c>DateTime.Now</c>.</summary>
        private readonly Stopwatch _bankClock = new();

        /// <summary>
        /// THE BANK's own token surface, separate from <c>_eventBurstLayer</c>. A burst is a fixed
        /// box centred on one anchor; a flight is a line between two, so this one is re-sized per
        /// flight to the box it actually needs.
        /// </summary>
        private AmbientFxCanvas? _bankLayer;
        private bool _bankLayerFailed;

        /// <summary>Where the layer's top-left currently sits in EventFxHost coordinates.</summary>
        private Point _bankLayerOrigin;

        private ScaleTransform? _bankPopTransform;

        /// <summary>
        /// Set on the XP path (possibly off the UI thread) to say "an award is landing right now";
        /// cleared by the <c>XPAwarded</c> that always follows. It is the ONLY thing that
        /// distinguishes an award's display update from an ordinary refresh (a profile load, a tab
        /// switch), which must never be held.
        /// </summary>
        private volatile bool _bankAwardPending;

        private bool _bankHolding;
        private double _bankHeldXp;
        private double _bankHeldNeeded;
        private int _bankHeldLevel;

        private bool _bankFlightLive;
        private double _bankFlightStartedMs;

        /// <summary>The pot the live flight is carrying - the figure its last token must deliver.</summary>
        private double _bankFlightPot;

        /// <summary>Counter value the live flight is counting up from, and the one it is standing on.</summary>
        private double _bankFlightFrom;
        private double _bankFlightShown;
        private int _bankFlightTokens;

        /// <summary>
        /// Bumped before every launch and every abort, and captured by each landing callback.
        /// Force-landing a flight fires its outstanding callbacks immediately (AmbientFxCanvas's
        /// contract), so without this a dying flight's tail would step the counter of the live one.
        /// </summary>
        private int _bankFlightId;

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires THE BANK. Called from the constructor IMMEDIATELY BEFORE the window subscribes
        /// <c>OnXPChanged</c>, and the order matters: multicast handlers run in subscription order,
        /// so <see cref="OnBankXpChanged"/> arming the hold has to be in front of the handler that
        /// tweens the counter. Safe to call twice.
        /// </summary>
        internal void InitializeBankFx()
        {
            try
            {
                if (_bank != null) return;

                _bankClock.Restart();
                _bank = new BankAccumulator(() => _bankClock.Elapsed.TotalMilliseconds);

                if (App.Progression != null)
                {
                    App.Progression.XPChanged += OnBankXpChanged;
                    App.Progression.XPAwarded += OnBankXpAwarded;
                }

                // Focus-state silence: a pot collected before the user walked away is dropped, not
                // queued. Piggy-backing on the window's own events rather than ChromeFx's funnel
                // keeps this file removable in one piece.
                Deactivated += OnBankWindowStateish;
                StateChanged += OnBankWindowStateish;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeBankFx failed - THE BANK disabled"); }
        }

        /// <summary>
        /// Unwires THE BANK and settles anything it was holding. Called from the window's cleanup
        /// beside the other progression unsubscribes.
        /// </summary>
        internal void ShutdownBankFx()
        {
            try
            {
                if (App.Progression != null)
                {
                    App.Progression.XPChanged -= OnBankXpChanged;
                    App.Progression.XPAwarded -= OnBankXpAwarded;
                }
                Deactivated -= OnBankWindowStateish;
                StateChanged -= OnBankWindowStateish;

                _bankAwardPending = false;
                // The window is going away, so there is nobody left to show a corrected number to:
                // land the flight and drop the hold without touching the display.
                AbortBankFlight(writeTruth: false);
                _bank?.Reset();
                StopBankPoll();
                _bankClock.Stop();
            }
            catch (Exception ex) { App.Logger?.Debug("ShutdownBankFx: {E}", ex.Message); }
        }

        private void OnBankWindowStateish(object? sender, EventArgs e)
        {
            try
            {
                if (IsActive && WindowState != WindowState.Minimized) return;
                _bank?.Reset();
                AbortBankFlight(writeTruth: true);
                StopBankPoll();
            }
            catch (Exception ex) { App.Logger?.Debug("BankFx activation: {E}", ex.Message); }
        }

        // ============================== the XP path ==============================

        /// <summary>
        /// Arms the hold. Runs on whatever thread awarded the XP - which is why it does nothing but
        /// set a flag: <c>IsActive</c>, <c>IsVisible</c> and the rest of the gate are DependencyObject
        /// reads and throw off the UI thread. The real decision is taken in
        /// <see cref="TryHoldXpDisplay"/>, which is always on the dispatcher.
        /// </summary>
        private void OnBankXpChanged(object? sender, double xp) => _bankAwardPending = true;

        /// <summary>
        /// The award itself, with its size, its source and whether it crossed a level. Resolves the
        /// arm one way or the other: into a pot, or into an immediate release. Marshalled to the
        /// dispatcher because a good half of the thirty award sites are service threads.
        /// </summary>
        private void OnBankXpAwarded(object? sender, ProgressionService.XpAward award)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    // BeginInvoke, never Invoke: this is a decoration on the end of an award and it
                    // must not be able to block the thread that earned it.
                    Dispatcher.BeginInvoke(new Action(() => OnBankXpAwarded(sender, award)));
                    return;
                }

                _bankAwardPending = false;
                // AbortBankFlight, not a bare ReleaseXpHold, on each of the three give-up paths
                // below. Dropping the hold on its own is only honest when nothing is in the air, and
                // none of them can promise that: a flight lives well over a second and awards arrive
                // by the handful inside one. A flight whose hold has been released still steps the
                // counter on every remaining landing, off a target recomputed from _lastXpShown -
                // which the truthful write has just moved - so the readout runs BACKWARDS off the
                // ledger and finishes on the flight's own number with nothing left to correct it.
                if (_bank == null) { AbortBankFlight(writeTruth: true); return; }

                // One burst per moment. CelebrateLevelUp will normally have ended whatever was in
                // the air before this runs (AddXP raises LevelUp ahead of XPChanged - see
                // MainWindow.EventFx.cs) and OnLevelUp's UpdateLevelDisplay has already written the
                // post-level number; all that is left here is to make sure the pot this award
                // poisoned is gone and the speculative hold from its own XPChanged is released. The
                // abort is repeated rather than assumed: this file should not be correct only for as
                // long as another one keeps firing in a particular order.
                if (award.LeveledUp)
                {
                    _bank.OnAward(award.Amount, award.Source, true);
                    AbortBankFlight(writeTruth: true);
                    return;
                }

                // Focus-state silence and reduced motion: the award bypasses THE BANK entirely
                // rather than being pooled for later. This gate is not only about the user walking
                // away, which is the case the window events already cover - MotionFx.AllowParticles
                // reads PerformanceProfile.CurrentTier, and that counts LIVE flash windows and
                // bubbles, so a flash burst crossing the tier threshold can shut THE BANK off in the
                // middle of a flight. Which is exactly why the flight is ended here rather than
                // merely unhooked from the counter it is still driving.
                if (!EventFxAllowed)
                {
                    _bank.Reset();
                    AbortBankFlight(writeTruth: true);
                    return;
                }

                // THE GATE. This is the first line on the XP path that knows what the XP was FOR,
                // which is why the decision is taken here and not in TryHoldXpDisplay (that runs
                // one event earlier, with only "something is landing" to go on).
                if (!BankAccumulator.IsBankable(award.Source))
                {
                    // Nothing else owns the readout: hand it straight back. The release writes the
                    // ledger through the ordinary path, which sees no hold and tweens exactly as it
                    // did before this file existed - no tokens, no thud, no wait. Doing it HERE and
                    // not leaning on the poll's self-heal is the whole difference between "weather
                    // bypasses THE BANK" and "weather stutters for up to 250ms first".
                    if (!_bank.HasOpenPot && !_bankFlightLive)
                    {
                        ReleaseXpHold(writeTruth: true);
                        return;
                    }

                    // A completion is already collecting or in the air and owns the counter. Two
                    // things are deliberately NOT done: the hold is not released (a live flight
                    // keeps stepping the counter off a target recomputed from _lastXpShown, so
                    // releasing under it runs the readout BACKWARDS - see the note above), and the
                    // award is not added to the pot (the tokens would then be seen to deliver XP
                    // that was not the completion's). It rides out on the flight's own release to
                    // truth, which by then includes it.
                    StartBankPoll();
                    return;
                }

                var flight = _bank.OnAward(award.Amount, award.Source, false);
                if (flight != null)
                {
                    LaunchBankFlight(flight);
                    return;
                }

                // Nothing was opened by this award (a zero or non-finite amount is ignored outright)
                // and nothing is in the air: there is no future event that would ever release the
                // hold this award armed, so release it here rather than leaning on the watchdog.
                if (!_bank.HasOpenPot && !_bankFlightLive)
                {
                    ReleaseXpHold(writeTruth: true);
                    return;
                }

                StartBankPoll();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("OnBankXpAwarded: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); } catch { }
            }
        }

        /// <summary>
        /// The hook <c>AnimateXpDisplay</c> consults before it tweens anything. Returns true when
        /// THE BANK owns the readout right now, in which case the caller records nothing and draws
        /// nothing - the target is remembered here and the tokens deliver it.
        ///
        /// <para>Three states meet in this method. An ordinary refresh (no award pending, no hold)
        /// falls straight through and the file may as well not exist. An award landing while a pot
        /// or a flight is live extends the existing hold with the newer target. An award landing
        /// with no hold yet takes the gate - and this is the ONLY place the gate can be read,
        /// because it is the only one of the three that is guaranteed to be on the UI thread.</para>
        /// </summary>
        /// <param name="xp">The ledger's XP for this level - the value being withheld.</param>
        /// <param name="xpNeeded">The level's cap, for the "<c>n / cap XP</c>" format.</param>
        /// <param name="level">The level that <paramref name="xp"/> belongs to.</param>
        /// <returns>True if the caller should skip its tween entirely.</returns>
        internal bool TryHoldXpDisplay(double xp, double xpNeeded, int level)
        {
            try
            {
                if (!_bankHolding)
                {
                    if (!_bankAwardPending) return false;
                    if (!EventFxAllowed) { _bankAwardPending = false; return false; }

                    // A level change wraps the readout; there is nothing coherent to hold across it.
                    if (_bankHeldLevel != level && _bankFlightLive) return false;

                    _bankHolding = true;
                }

                _bankHeldXp = xp;
                _bankHeldNeeded = xpNeeded;
                _bankHeldLevel = level;

                // The poll is what heals a hold whose award never resolved into a pot.
                StartBankPoll();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("TryHoldXpDisplay: {E}", ex.Message);
                _bankHolding = false;
                return false;
            }
        }

        /// <summary>
        /// Ends the hold. When <paramref name="writeTruth"/> the display is put back on the ledger
        /// through the ordinary path, which by then sees no hold and behaves exactly as it does for
        /// any other award. Passing false is for the two callers who know a truthful write is
        /// already coming (a level-up's own UpdateLevelDisplay) or that nobody will ever see one
        /// (window teardown).
        ///
        /// <para>The arm is suppressed across the write, because the write has to LAND. Otherwise
        /// <see cref="TryHoldXpDisplay"/> is free to re-arm on an award still marshalling in from a
        /// worker thread and swallow the very refresh that puts the counter back on the ledger - and
        /// since every caller that gives up stops the poll immediately afterwards, the readout would
        /// be left standing on a stale number with no clock left to heal it. It is re-armed on the
        /// way out, so a genuine award still gets its moment - re-armed and never cleared, because
        /// an award landing on a worker thread DURING the write arms the flag itself and restoring
        /// a remembered false would throw that arm away.</para>
        /// </summary>
        private void ReleaseXpHold(bool writeTruth)
        {
            bool wasHolding = _bankHolding;
            _bankHolding = false;
            if (!wasHolding || !writeTruth) return;

            bool armed = _bankAwardPending;
            _bankAwardPending = false;
            try { UpdateLevelDisplay(); }
            catch (Exception ex) { App.Logger?.Debug("ReleaseXpHold: {E}", ex.Message); }
            finally { if (armed) _bankAwardPending = true; }
        }

        // ============================== the poll ==============================

        /// <summary>
        /// Starts the only clock this feature owns, and only while there is something for it to
        /// resolve: a pot ripening, a flight to watchdog, or a hold to heal. EventFx's whole appeal
        /// is that it costs nothing when it is not happening, and a 250ms timer parked forever on
        /// an idle app would spend that for a feature that fires every few minutes.
        /// </summary>
        private void StartBankPoll()
        {
            try
            {
                _bankPoll ??= CreateBankPoll();
                if (_bankPoll != null && !_bankPoll.IsEnabled) _bankPoll.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("StartBankPoll: {E}", ex.Message); }
        }

        private DispatcherTimer? CreateBankPoll()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(BankPollMs),
            };
            timer.Tick += OnBankPollTick;
            return timer;
        }

        private void StopBankPoll()
        {
            try { _bankPoll?.Stop(); }
            catch (Exception ex) { App.Logger?.Debug("StopBankPoll: {E}", ex.Message); }
        }

        private void OnBankPollTick(object? sender, EventArgs e)
        {
            try
            {
                if (_bank == null) { StopBankPoll(); return; }

                // FAILSAFE - the watchdog. A flight that has not reported its last landing long
                // after the longest legal envelope is not coming back; the counter is not allowed
                // to wait on it.
                if (_bankFlightLive &&
                    _bankClock.Elapsed.TotalMilliseconds - _bankFlightStartedMs > BankWatchdogMs)
                {
                    App.Logger?.Debug("[BANK] watchdog fired - flight never landed, snapping the counter");
                    AbortBankFlight(writeTruth: true);
                }

                if (!_bankFlightLive)
                {
                    var flight = _bank.Tick();
                    if (flight != null) LaunchBankFlight(flight);
                }

                if (_bankFlightLive || _bank.HasOpenPot) return;

                // FAILSAFE - the self-heal. Nothing is open, nothing is flying: any hold still
                // standing belongs to an award that never became a pot.
                ReleaseXpHold(writeTruth: true);
                StopBankPoll();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("OnBankPollTick: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); StopBankPoll(); } catch { }
            }
        }

        // ============================== the flight ==============================

        /// <summary>
        /// Launches one BANK moment: resolve the two anchors, size the surface to the line between
        /// them, and hand the tokens to the canvas. Anything that cannot be resolved cancels the
        /// moment and releases the counter - a flight is a decoration, and a decoration that cannot
        /// be drawn must never cost the user their number.
        ///
        /// <para>Every refusal below goes out through <see cref="AbortBankFlight"/> rather than a
        /// bare release. Almost always there is nothing in the air to abort and the two are the same
        /// thing; the exception is the one case worth writing for, a previous flight that stalled
        /// (its canvas stopped ticking) and is sitting on the counter waiting for the watchdog. The
        /// accumulator's cooldown can hand this method a new pot inside that window, and letting the
        /// stalled flight keep the readout while its successor is refused is how a stale number
        /// survives.</para>
        /// </summary>
        private void LaunchBankFlight(BankAccumulator.Flight flight)
        {
            try
            {
                if (flight == null || !EventFxAllowed) { AbortBankFlight(writeTruth: true); return; }

                // No hold means no withheld value, and a flight whose tokens deliver a number that
                // is already on screen is decoration pretending to be a payout. (Also guards the
                // "n / cap XP" denominator, which only exists once something has been held.)
                if (!_bankHolding || _bankHeldNeeded <= 0) { AbortBankFlight(writeTruth: true); return; }

                var host = EventFxHost;
                if (host == null) { AbortBankFlight(writeTruth: true); return; }

                if (!TryResolveBankTarget(host, out var target) ||
                    !TryResolveBankOrigin(host, flight.DominantSource, out var origin))
                {
                    AbortBankFlight(writeTruth: true);
                    return;
                }

                var layer = EnsureBankLayer(host, origin, target);
                if (layer == null) { AbortBankFlight(writeTruth: true); return; }

                // A second flight force-lands the first from inside BankTokens. Bumping the id here,
                // BEFORE that happens, is what makes the dying flight's tail callbacks inert.
                int id = ++_bankFlightId;

                _bankFlightLive = true;
                _bankFlightStartedMs = _bankClock.Elapsed.TotalMilliseconds;
                _bankFlightPot = flight.XpSum;
                _bankFlightTokens = Math.Max(1, flight.TokenCount);
                _bankFlightFrom = BankCounterScript.StartValue(_lastXpShown, _lastXpLevelShown, _bankHeldLevel);
                _bankFlightShown = _bankFlightFrom;

                StartBankPoll();

                layer.BankTokens(new Point(origin.X - _bankLayerOrigin.X, origin.Y - _bankLayerOrigin.Y),
                                 new Point(target.X - _bankLayerOrigin.X, target.Y - _bankLayerOrigin.Y),
                                 _bankFlightTokens, FxTheme.GlowColor,
                                 (index, isLast) => OnBankTokenLanded(id, index, isLast));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("LaunchBankFlight: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); } catch { }
            }
        }

        /// <summary>
        /// One token arriving at the counter. Steps the readout by its share of the pot; on the last
        /// landing it puts the counter exactly on the pot's figure, thuds, pops, fills the bar and
        /// hands the display back.
        /// </summary>
        private void OnBankTokenLanded(int flightId, int index, bool isLast)
        {
            try
            {
                // A force-landed predecessor settling its debts. Its callbacks are honest, they are
                // just no longer about anything on screen.
                if (flightId != _bankFlightId || !_bankFlightLive) return;

                double truth = _bankHolding ? _bankHeldXp : _lastXpShown;
                double target = BankCounterScript.Target(_bankFlightFrom, _bankFlightPot, truth);
                double value = BankCounterScript.StepValue(_bankFlightFrom, target, index, _bankFlightTokens);

                StepBankCounter(value, isLast);
                if (!isLast) return;

                _bankFlightLive = false;
                PlayBankThud();
                PopXpCounter();
                FillXpBarTo(value, _bankHeldNeeded);
                FlashXpBarOnBankLanding();

                // The pot that was collecting while this flight was in the air keeps the hold: its
                // own tokens are what will be seen to deliver it.
                if (_bank?.HasOpenPot != true)
                {
                    ReleaseXpHold(writeTruth: true);
                    StopBankPoll();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("OnBankTokenLanded: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); } catch { }
            }
        }

        /// <summary>
        /// Ends the live flight now and, unless told otherwise, puts the counter back on the ledger.
        /// The id is bumped first so the force-land's callbacks - which fire synchronously from
        /// inside <c>Stop()</c> - cannot step a counter that is already being corrected.
        /// </summary>
        private void AbortBankFlight(bool writeTruth)
        {
            try
            {
                _bankFlightId++;
                _bankFlightLive = false;
                try { _bankLayer?.Stop(); }
                catch (Exception ex) { App.Logger?.Debug("AbortBankFlight stop: {E}", ex.Message); }
                ReleaseXpHold(writeTruth);
            }
            catch (Exception ex) { App.Logger?.Debug("AbortBankFlight: {E}", ex.Message); }
        }

        // ============================== the counter ==============================

        /// <summary>
        /// One tick of the odometer, on its own short clock rather than the display's 700ms one -
        /// the House Book asks for a TICK per landing, and a 700ms tween restarted every 70ms never
        /// reaches any of its targets. <c>_lastXpShown</c> is kept in step so that whatever writes
        /// the display next (a release, a level-up, a tab switch) counts from where the tokens left
        /// it instead of jumping.
        /// </summary>
        private void StepBankCounter(double value, bool isLast)
        {
            try
            {
                if (TxtXP == null) return;

                string format = "{0:F0} / " + ((int)_bankHeldNeeded) + " XP";
                MotionFx.Odometer(TxtXP, _bankFlightShown, value, format,
                                  BankCounterScript.StepMs(isLast) / 1000.0);

                _bankFlightShown = value;
                _lastXpShown = value;
                _lastXpLevelShown = _bankHeldLevel;
            }
            catch (Exception ex) { App.Logger?.Debug("StepBankCounter: {E}", ex.Message); }
        }

        /// <summary>
        /// The catch: a scale pop on the XP readout as the last token lands, out fast and sprung
        /// back. TxtXP carries no transform in XAML, so one is installed on first use rather than
        /// adding a RenderTransform to the header for a moment most launches never reach.
        /// </summary>
        private void PopXpCounter()
        {
            try
            {
                if (!MotionFx.AllowTransitions || TxtXP == null) return;

                if (_bankPopTransform == null)
                {
                    if (TxtXP.RenderTransform != null && TxtXP.RenderTransform != Transform.Identity
                        && !TxtXP.RenderTransform.Value.IsIdentity)
                        return;   // somebody else owns it; a pop is not worth clobbering a layout
                    _bankPopTransform = new ScaleTransform(1, 1);
                    TxtXP.RenderTransformOrigin = new Point(0.5, 0.5);
                    TxtXP.RenderTransform = _bankPopTransform;
                }

                var pop = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(BankPopOutMs + BankPopSpringMs),
                };
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(BankPopScale,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BankPopOutMs)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BankPopOutMs + BankPopSpringMs)))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = BankPopSpringAmplitude },
                });
                _bankPopTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pop, HandoffBehavior.SnapshotAndReplace);
                _bankPopTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pop, HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception ex) { App.Logger?.Debug("PopXpCounter: {E}", ex.Message); }
        }

        /// <summary>
        /// The bar half of the catch: one flash across the XP fill as the last token lands, so the
        /// arrival is felt at bar scale and not only in an 11px number.
        ///
        /// <para>It reuses <c>FlashOverlay</c> on <c>XPBarFlashOverlay</c> - the same overlay and
        /// the same 250ms auto-reversing opacity animation the level-up bloom and the Brain
        /// Parasite drain already drive - precisely so that overlay keeps exactly ONE owner. The
        /// two moments cannot collide anyway (house law: a level-up aborts the flight before its
        /// own bloom), and if they ever did, the loser is a restarted 250ms fade rather than two
        /// storyboards fighting over one property.</para>
        ///
        /// <para><c>XPBarSheen</c> is left strictly alone. ChromeFx's ambient loop owns its opacity
        /// and its three gradient stops on a forever-repeating clock; anything this file did to it
        /// would either be stamped on by the next sweep or kill the sweep outright.</para>
        ///
        /// <para>Gated on <c>MotionFx.AllowTransitions</c> like the counter pop. Reaching here at
        /// all already required <c>EventFxAllowed</c>, so this is belt-and-braces for the one case
        /// where the tier moved mid-flight.</para>
        /// </summary>
        private void FlashXpBarOnBankLanding()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                FlashOverlay(XPBarFlashOverlay);
            }
            catch (Exception ex) { App.Logger?.Debug("FlashXpBarOnBankLanding: {E}", ex.Message); }
        }

        /// <summary>
        /// The mini-thud, ChaosSfx's override-then-fallback shape: drop a
        /// <c>Resources/sounds/ui/bank_thud.mp3</c> in (or ship one in a mod) and it wins
        /// automatically; until then the bubble pop carries it, quietly. Silent no-op on any
        /// failure - a missing sound must never be the thing that breaks an award.
        /// </summary>
        private static void PlayBankThud()
        {
            try
            {
                float master = (float)(App.Settings?.Current?.MasterVolume ?? 0) / 100f;
                float volume = Math.Clamp(master * BankThudScale, 0f, 1f);
                if (volume <= 0f) return;

                foreach (var candidate in new[] { BankThudOverride, BankThudFallback })
                {
                    string path;
                    try { path = ModResourceResolver.ResolveAudioPath(candidate); }
                    catch { continue; }
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    App.Audio?.PlayOneShot(path, volume, BankThudTag);
                    return;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("PlayBankThud: {E}", ex.Message); }
        }

        // ============================== anchors + the surface ==============================

        /// <summary>
        /// Where the tokens are going: the middle of the XP readout itself. Mapped through
        /// <c>TransformToVisual</c> and never by layout arithmetic - TxtXP lives inside the
        /// 1489x901 Viewbox, so its own coordinates are design-canvas units and the transform is
        /// what carries the scale.
        /// </summary>
        private bool TryResolveBankTarget(FrameworkElement host, out Point target)
            => TryBankAnchorCenter(host, TxtXP, out target);

        /// <summary>
        /// Where the value came from, first visible wins - the same spirit as
        /// <c>FireBurstAtFirstVisible</c>.
        ///
        /// <para>Since THE BANK only flies for completions, in practice this resolves one of four
        /// sources - and three of them have an honest on-screen home. The fourth (LockCard) is its
        /// own top-level window that this window's FX layer cannot reach into, so it spawns from the
        /// profile bubble instead: the "you" that earned it is the only honest origin for XP with no
        /// visible source, and it sits in the same header strip as the counter, so the flight still
        /// reads as a delivery. The switch is left total rather than trimmed to the four - an
        /// ineligible source can still arrive here if <c>IsBankable</c> grows, and a fallback that
        /// already works is cheaper than a fallback that has to be remembered.</para>
        /// </summary>
        private bool TryResolveBankOrigin(FrameworkElement host, XPSource source, out Point origin)
        {
            FrameworkElement? mapped = source switch
            {
                // The session rack. Sessions are the only feature whose XP arrives in one large,
                // deliberate lump, and its tab is the one a subject actually looks at afterwards.
                XPSource.Session => NavAnchorForTab("presets"),
                // The quest rail, the same anchor CelebrateQuestComplete bursts on - so the tokens
                // spill out of the card that just ticked over rather than from nowhere.
                XPSource.Quest => NavAnchorForTab("quests"),
                // Both bubble games are Studio rack modules (HostBubblePop / HostBubbleCount), so
                // the Studio row is the door their XP came through.
                XPSource.Bubble or XPSource.BubbleCount => NavAnchorForTab("studio"),
                _ => null,
            };

            if (TryBankAnchorCenter(host, mapped, out origin)) return true;
            if (TryBankAnchorCenter(host, BtnProfileBubble, out origin)) return true;

            // Last resort: a fixed point out in the header's empty space, left of the track. Better
            // than no flight at all, and it still travels the header the way a real origin would.
            if (TryBankAnchorBounds(host, XPBarTrack, out var track))
            {
                origin = new Point(track.Left + BankFallbackOriginDx, track.Top + track.Height / 2);
                return IsFiniteBankPoint(origin);
            }

            origin = default;
            return false;
        }

        private bool TryBankAnchorCenter(FrameworkElement host, FrameworkElement? anchor, out Point center)
        {
            if (TryBankAnchorBounds(host, anchor, out var bounds))
            {
                center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                return IsFiniteBankPoint(center);
            }
            center = default;
            return false;
        }

        private bool TryBankAnchorBounds(FrameworkElement host, FrameworkElement? anchor, out Rect bounds)
        {
            bounds = default;
            try
            {
                if (anchor == null || !anchor.IsVisible) return false;
                if (anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0) return false;

                // Throws when the two are not connected (an anchor on a detached tab) - hence the wrap.
                bounds = anchor.TransformToVisual(host)
                               .TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
                return !bounds.IsEmpty && IsFiniteBankPoint(bounds.TopLeft) && IsFiniteBankPoint(bounds.BottomRight);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("TryBankAnchorBounds: {E}", ex.Message);
                return false;
            }
        }

        private static bool IsFiniteBankPoint(Point p)
            => !double.IsNaN(p.X) && !double.IsInfinity(p.X)
            && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y);

        /// <summary>
        /// Builds (once) and re-frames (per flight) the token surface: the bounding box of origin
        /// and target plus <see cref="BankBoxPadPx"/>, moved by Margin exactly like the burst layer.
        ///
        /// <para>The one way it differs from <c>EnsureEventBurstLayer</c> is that its SIZE moves.
        /// A burst is a fixed box round a single anchor; a flight is a line between two that can be
        /// most of the window apart or barely a hand's width, and a fixed box big enough for the
        /// worst case would raster the whole window for every flight. The price is one forced
        /// layout pass per resize, which is affordable at a floor of one flight every three seconds
        /// and is skipped entirely whenever the box happens to come out the same.</para>
        /// </summary>
        private AmbientFxCanvas? EnsureBankLayer(FrameworkElement host, Point origin, Point target)
        {
            if (_bankLayerFailed) return null;
            try
            {
                double left = Math.Min(origin.X, target.X) - BankBoxPadPx;
                double top = Math.Min(origin.Y, target.Y) - BankBoxPadPx;
                double width = Math.Abs(origin.X - target.X) + BankBoxPadPx * 2;
                double height = Math.Abs(origin.Y - target.Y) + BankBoxPadPx * 2;

                // A box this big means an anchor mapped to nonsense, not a long flight.
                if (width > BankBoxMaxPx || height > BankBoxMaxPx) return null;

                if (_bankLayer == null)
                {
                    var created = new AmbientFxCanvas
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        IsHitTestVisible = false,
                    };
                    if (host is Panel panel) panel.Children.Add(created);
                    else { _bankLayerFailed = true; return null; }
                    _bankLayer = created;
                }

                bool resized = Math.Abs(_bankLayer.Width - width) > 0.5
                            || Math.Abs(_bankLayer.Height - height) > 0.5
                            || _bankLayer.ActualWidth <= 1 || _bankLayer.ActualHeight <= 1;

                _bankLayer.Width = width;
                _bankLayer.Height = height;
                // Negative margins are legal and are exactly what is wanted near a window edge: the
                // box hangs off and clips.
                _bankLayer.Margin = new Thickness(left, top, 0, 0);
                _bankLayerOrigin = new Point(left, top);

                // BankTokens reads ActualWidth, which is stale until the new size has been arranged.
                if (resized) _bankLayer.UpdateLayout();

                return _bankLayer;
            }
            catch (Exception ex)
            {
                _bankLayerFailed = true;
                App.Logger?.Warning(ex, "EnsureBankLayer failed - THE BANK disabled");
                return null;
            }
        }
    }
}
