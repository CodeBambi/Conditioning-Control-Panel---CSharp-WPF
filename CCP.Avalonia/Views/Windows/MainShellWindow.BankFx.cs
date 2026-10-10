// PORTED from WPF 7.1.5 MainWindow/MainWindow.BankFx.cs. THE BANK (House Book) on XP: the seventh
// event moment, and the only recurring one.
//
// The move. Value spawns at its SOURCE as 4-10 tokens, flies a slight arc to the counter in the
// header, and the counter ticks up as each token lands: a thud, a scale pop and one flash across the
// XP bar on the last. XP stops being a number that silently changed and becomes something that
// visibly arrived from somewhere.
//
// Only completions fly (BankAccumulator.IsBankable: a quest, a session, a lock card, the counting
// game). Every other source is weather and tweens the counter exactly as before. Awards pool for
// BankAccumulator.WindowMs and flights start at least CooldownMs apart.
//
// The counter is HELD, the ledger is not. settings.PlayerXP is written the instant the award lands
// (Core ProgressionBank); what is staged is the READOUT. While a pot is open or a flight is in the
// air TryHoldXpDisplay swallows UpdateLevelDisplay's readout and bar and remembers the target, and
// the tokens release it. Every path out of the hold ends with the display on the ledger's number
// (ReleaseXpHold), which is why the failsafes (the watchdog, the poll's self-heal, the aborts) are
// not optional.
//
// Differences from WPF, each forced by this head:
//   - Core raises one event per award (ProgressionBank.Awarded, after LevelUp) where WPF raised
//     XPChanged then XPAwarded. OnBankAward arms the hold, repaints (the hold swallows it) and
//     resolves, in that order, on the UI thread: the same sequence in one call.
//   - a level-up is read off the level itself (it moved since the last award).
//   - the source is the award's name ("Quest", "Session", ...): Core has no XPSource enum.
//   - the counter's pop and steps are FxTrack / the header's own odometer; no Animation on a Transform.
// The profile bubble's own mini readout is deliberately unstaged, as in WPF.
using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Progression;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ---- DIALS (WPF BankFx.cs:84-130) ----
        private const double BankBoxPadPx = 100;
        private const double BankBoxMaxPx = 2600;
        private const double BankFallbackOriginDx = -180;
        private const int BankPollMs = 250;
        private const double BankWatchdogMs = 6000;
        internal const double BankPopScale = 1.28, BankPopOutMs = 90, BankPopSpringMs = 300, BankPopSpringAmplitude = 0.9;
        private const string BankThudOverride = "ui/bank_thud.mp3", BankThudFallback = "bubbles/Pop2.mp3", BankThudTag = "ui-bank";
        private const float BankThudScale = 0.22f;

        // ---- state ----
        private BankAccumulator? _bank;
        private DispatcherTimer? _bankPoll;
        private readonly Stopwatch _bankClock = new();
        private AmbientFxCanvas? _bankLayer;
        private bool _bankLayerFailed;
        private Point _bankLayerOrigin;
        private ScaleTransform? _bankPopTransform;
        private FxTrack.Run? _bankPop;
        private bool _bankAwardPending, _bankHolding, _bankFlightLive;
        private double _bankHeldXp, _bankHeldNeeded, _bankFlightStartedMs, _bankFlightPot, _bankFlightFrom, _bankFlightShown;
        private int _bankHeldLevel, _bankFlightTokens, _bankFlightId, _bankSeenLevel = -1;

        /// <summary>Test seams: the banker's clock, the gate, and what the shell is doing.</summary>
        internal Func<double>? BankClockOverride { get; set; }
        internal bool? BankGateOverride { get; set; }
        internal bool BankHolding => _bankHolding;
        internal bool BankFlightLive => _bankFlightLive;
        internal bool BankPollRunning => _bankPoll?.IsEnabled == true;
        internal AmbientFxCanvas? BankLayer => _bankLayer;
        internal double BankPopScaleNow => _bankPopTransform?.ScaleX ?? 1;
        internal FxTrack.Run? BankPopRun => _bankPop;
        internal void BankPollTickForTests() => OnBankPollTick();

        private double BankNowMs => BankClockOverride?.Invoke() ?? _bankClock.Elapsed.TotalMilliseconds;

        /// <summary>WPF EventFxAllowed (EventFx.cs:118): particles allowed, the window shown, not minimised, active.
        /// Reduced motion and Off skip THE BANK outright, the whole staging path.</summary>
        private bool BankFxAllowed
        {
            get
            {
                try
                {
                    if (BankGateOverride is { } forced) return forced;
                    return Env.AllowParticles && IsVisible && WindowState != WindowState.Minimized && IsActive;
                }
                catch { return false; }
            }
        }

        // ============================== lifecycle ==============================

        internal void InitializeBankFx()
        {
            try
            {
                if (_bank != null) return;
                _bankClock.Restart();
                _bank = new BankAccumulator(() => BankNowMs);
                _bankSeenLevel = CoreSettings.Current.PlayerLevel;
                // Focus-state silence: a pot collected before the user walked away is dropped, not queued.
                Deactivated += OnBankWindowStateish;
                PropertyChanged += OnBankWindowProperty;
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeBankFx failed - THE BANK disabled"); }
        }

        internal void ShutdownBankFx()
        {
            try
            {
                Deactivated -= OnBankWindowStateish;
                PropertyChanged -= OnBankWindowProperty;
                _bankAwardPending = false;
                // The window is going away: land the flight and drop the hold without touching the display.
                AbortBankFlight(writeTruth: false);
                _bank?.Reset();
                StopBankPoll();
                _bankClock.Stop();
            }
            catch (Exception ex) { Log.Debug("ShutdownBankFx: {E}", ex.Message); }
        }

        private void OnBankWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty) OnBankWindowStateish(sender, EventArgs.Empty);
        }

        private void OnBankWindowStateish(object? sender, EventArgs e)
        {
            try
            {
                if (BankGateOverride == true) return;
                if (IsActive && WindowState != WindowState.Minimized) return;
                _bank?.Reset();
                AbortBankFlight(writeTruth: true);
                StopBankPoll();
            }
            catch (Exception ex) { Log.Debug("BankFx activation: {E}", ex.Message); }
        }

        // ============================== the XP path ==============================

        /// <summary>One award, on the UI thread (HookLevelDisplay posts it): arm, repaint, resolve.</summary>
        internal void OnBankAward(double amount, string source)
        {
            try
            {
                int level = CoreSettings.Current.PlayerLevel;
                bool leveledUp = _bankSeenLevel >= 0 && level != _bankSeenLevel;
                _bankSeenLevel = level;

                if (_bank == null)
                {
                    AbortBankFlight(writeTruth: true);
                    UpdateLevelDisplay();
                    return;
                }

                // One burst per moment: the level-up owns it. The pot this award poisoned is dropped and
                // whatever was in the air is ended on the truth.
                if (leveledUp)
                {
                    _bank.OnAward(amount, source, true);
                    AbortBankFlight(writeTruth: true);
                    UpdateLevelDisplay();
                    return;
                }

                // Arm the hold BEFORE the repaint: UpdateLevelDisplay is what tweens the counter.
                _bankAwardPending = true;
                UpdateLevelDisplay();
                _bankAwardPending = false;

                // Focus-state silence and reduced motion: the award bypasses THE BANK entirely. The flight is
                // ended here rather than merely unhooked from the counter it is still driving.
                if (!BankFxAllowed)
                {
                    _bank.Reset();
                    AbortBankFlight(writeTruth: true);
                    return;
                }

                // THE GATE: the first line on the XP path that knows what the XP was FOR.
                if (!BankAccumulator.IsBankable(source))
                {
                    // Nothing else owns the readout: hand it straight back, so weather tweens at once.
                    if (!_bank.HasOpenPot && !_bankFlightLive) { ReleaseXpHold(writeTruth: true); return; }
                    // A completion is collecting or in the air and owns the counter: the hold stays (releasing
                    // under a live flight runs the readout BACKWARDS) and the award stays out of the pot. It
                    // rides out on the flight's own release to truth.
                    StartBankPoll();
                    return;
                }

                var flight = _bank.OnAward(amount, source, false);
                if (flight != null) { LaunchBankFlight(flight); return; }

                // Nothing opened and nothing in the air: no future event would release this hold.
                if (!_bank.HasOpenPot && !_bankFlightLive) { ReleaseXpHold(writeTruth: true); return; }
                StartBankPoll();
            }
            catch (Exception ex)
            {
                Log.Debug("OnBankAward: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); } catch { }
            }
        }

        /// <summary>First refusal on the XP readout (WPF AnimateXpDisplay). False for every ordinary refresh
        /// and for the whole of motion Reduced / Off.</summary>
        internal bool TryHoldXpDisplay(double xp, double xpNeeded, int level)
        {
            try
            {
                if (!_bankHolding)
                {
                    if (!_bankAwardPending) return false;
                    if (!BankFxAllowed) { _bankAwardPending = false; return false; }
                    // A level change wraps the readout; there is nothing coherent to hold across it.
                    if (_bankHeldLevel != level && _bankFlightLive) return false;
                    _bankHolding = true;
                }
                _bankHeldXp = xp;
                _bankHeldNeeded = xpNeeded;
                _bankHeldLevel = level;
                StartBankPoll();   // the poll heals a hold whose award never resolved into a pot
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("TryHoldXpDisplay: {E}", ex.Message);
                _bankHolding = false;
                return false;
            }
        }

        /// <summary>The hold ends; with <paramref name="writeTruth"/> the display lands on the ledger.</summary>
        private void ReleaseXpHold(bool writeTruth)
        {
            bool wasHolding = _bankHolding;
            _bankHolding = false;
            if (!wasHolding || !writeTruth) return;
            bool armed = _bankAwardPending;
            _bankAwardPending = false;
            try { UpdateLevelDisplay(); }
            catch (Exception ex) { Log.Debug("ReleaseXpHold: {E}", ex.Message); }
            finally { if (armed) _bankAwardPending = true; }
        }

        // ============================== the poll ==============================

        private void StartBankPoll()
        {
            try
            {
                if (_bankPoll == null)
                {
                    _bankPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(BankPollMs) };
                    _bankPoll.Tick += (_, _) => OnBankPollTick();
                }
                if (!_bankPoll.IsEnabled) _bankPoll.Start();
            }
            catch (Exception ex) { Log.Debug("StartBankPoll: {E}", ex.Message); }
        }

        private void StopBankPoll()
        {
            try { _bankPoll?.Stop(); }
            catch (Exception ex) { Log.Debug("StopBankPoll: {E}", ex.Message); }
        }

        private void OnBankPollTick()
        {
            try
            {
                if (_bank == null) { StopBankPoll(); return; }

                // FAILSAFE, the watchdog: a flight that has not reported its last landing long after the
                // longest legal envelope is not coming back; the counter is not allowed to wait on it.
                if (_bankFlightLive && BankNowMs - _bankFlightStartedMs > BankWatchdogMs)
                {
                    Log.Debug("[BANK] watchdog fired - flight never landed, snapping the counter");
                    AbortBankFlight(writeTruth: true);
                }

                if (!_bankFlightLive && _bank.Tick() is { } flight) LaunchBankFlight(flight);
                if (_bankFlightLive || _bank.HasOpenPot) return;

                // FAILSAFE, the self-heal: nothing is open, nothing is flying, so any hold still standing
                // belongs to an award that never became a pot.
                ReleaseXpHold(writeTruth: true);
                StopBankPoll();
            }
            catch (Exception ex)
            {
                Log.Debug("OnBankPollTick: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); StopBankPoll(); } catch { }
            }
        }

        // ============================== the flight ==============================

        private void LaunchBankFlight(BankAccumulator.Flight flight)
        {
            try
            {
                if (!BankFxAllowed) { AbortBankFlight(writeTruth: true); return; }
                if (!_bankHolding || _bankHeldNeeded <= 0) { AbortBankFlight(writeTruth: true); return; }
                if (Named<Grid>("EventFxHost") is not { } host) { AbortBankFlight(writeTruth: true); return; }
                if (!TryBankAnchorCenter(host, Named<TextBlock>("TxtXP"), out var target)
                    || !TryResolveBankOrigin(host, flight.DominantSource, out var origin))
                {
                    AbortBankFlight(writeTruth: true);
                    return;
                }
                var layer = EnsureBankLayer(host, origin, target);
                if (layer == null) { AbortBankFlight(writeTruth: true); return; }

                int id = ++_bankFlightId;
                _bankFlightLive = true;
                _bankFlightStartedMs = BankNowMs;
                _bankFlightPot = flight.XpSum;
                _bankFlightTokens = Math.Max(1, flight.TokenCount);
                _bankFlightFrom = BankCounterScript.StartValue(_lastXpShown, _lastXpLevelShown, _bankHeldLevel);
                _bankFlightShown = _bankFlightFrom;
                StartBankPoll();

                layer.BankTokens(new Point(origin.X - _bankLayerOrigin.X, origin.Y - _bankLayerOrigin.Y),
                                 new Point(target.X - _bankLayerOrigin.X, target.Y - _bankLayerOrigin.Y),
                                 _bankFlightTokens, Env.GlowColor,
                                 (index, isLast) => OnBankTokenLanded(id, index, isLast));
            }
            catch (Exception ex)
            {
                Log.Debug("LaunchBankFlight: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); } catch { }
            }
        }

        private void OnBankTokenLanded(int flightId, int index, bool isLast)
        {
            try
            {
                if (flightId != _bankFlightId || !_bankFlightLive) return;
                double truth = _bankHolding ? _bankHeldXp : _lastXpShown;
                double target = BankCounterScript.Target(_bankFlightFrom, _bankFlightPot, truth);
                double value = BankCounterScript.StepValue(_bankFlightFrom, target, index, _bankFlightTokens);

                StepBankCounter(value, isLast);
                if (!isLast) return;

                _bankFlightLive = false;
                PlayBankThud();
                PopXpCounter();
                // the bar lands on what the tokens delivered, not on the ledger (a second pot may be collecting)
                _xpFraction = Math.Min(1.0, _bankHeldNeeded > 0 ? value / _bankHeldNeeded : 0);
                FillXpBar(animate: true);
                if (Env.AllowTransitions) FlashOverlay(Named<Border>("XPBarFlashOverlay"));

                if (_bank?.HasOpenPot != true)
                {
                    ReleaseXpHold(writeTruth: true);
                    StopBankPoll();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("OnBankTokenLanded: {E}", ex.Message);
                try { AbortBankFlight(writeTruth: true); } catch { }
            }
        }

        private void AbortBankFlight(bool writeTruth)
        {
            try
            {
                _bankFlightId++;
                _bankFlightLive = false;
                try { _bankLayer?.Stop(); }
                catch (Exception ex) { Log.Debug("AbortBankFlight stop: {E}", ex.Message); }
                ReleaseXpHold(writeTruth);
            }
            catch (Exception ex) { Log.Debug("AbortBankFlight: {E}", ex.Message); }
        }

        // ============================== the counter ==============================

        /// <summary>One landing: the readout counts to the value this token delivered (BankCounterScript.StepMs).</summary>
        private void StepBankCounter(double value, bool isLast)
        {
            try
            {
                if (Named<TextBlock>("TxtXP") is not { } txt) return;
                RunXpOdometer(txt, _bankFlightShown, value, _bankHeldNeeded, BankCounterScript.StepMs(isLast) / 1000.0);
                _bankFlightShown = value;
                _lastXpShown = value;
                _lastXpLevelShown = _bankHeldLevel;
            }
            catch (Exception ex) { Log.Debug("StepBankCounter: {E}", ex.Message); }
        }

        /// <summary>The catch on the last landing: out to 1.28 in 90 ms (quad out), back over 300 ms on a soft
        /// spring (BackEase out 0.9). A readout whose transform somebody else owns is left alone.</summary>
        private void PopXpCounter()
        {
            try
            {
                if (!Env.AllowTransitions || Named<TextBlock>("TxtXP") is not { } txt) return;
                if (_bankPopTransform == null)
                {
                    if (txt.RenderTransform != null) return;
                    _bankPopTransform = new ScaleTransform(1, 1);
                    txt.RenderTransformOrigin = RelativePoint.Center;
                    txt.RenderTransform = _bankPopTransform;
                }
                var scale = _bankPopTransform;
                _bankPop?.Finish();
                double total = BankPopOutMs + BankPopSpringMs;
                _bankPop = FxTrack.Play(total, ms => scale.ScaleX = scale.ScaleY = FxTrack.Keys(ms,
                        (0, 1, null), (BankPopOutMs, BankPopScale, FxTrack.QuadOut), (total, 1, u => FxTrack.BackOut(u, BankPopSpringAmplitude))),
                    () => scale.ScaleX = scale.ScaleY = 1);
            }
            catch (Exception ex) { Log.Debug("PopXpCounter: {E}", ex.Message); }
        }

        private static void PlayBankThud()
        {
            try
            {
                float volume = Math.Clamp(CoreSettings.Current.MasterVolume / 100f * BankThudScale, 0f, 1f);
                if (volume <= 0f) return;
                foreach (var rel in new[] { BankThudOverride, BankThudFallback })
                {
                    var path = CoreModArt.AudioOverridePath(rel)
                        ?? ContentLocator.Resolve(System.IO.Path.Combine("Resources", "sounds", rel.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                    if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
                    CoreAudio.PlayOneShot(path, volume, BankThudTag);
                    return;
                }
            }
            catch (Exception ex) { Log.Debug("PlayBankThud: {E}", ex.Message); }
        }

        // ============================== anchors ==============================

        /// <summary>Where the tokens spawn: the rail row of the thing that paid (a session, a quest, the bubble
        /// games), else the profile bubble, else out in the header's empty space left of the XP track.</summary>
        private bool TryResolveBankOrigin(Control host, string source, out Point origin)
        {
            Control? mapped = source switch
            {
                "Session" => NavAnchorForTab("presets"),
                "Quest" => NavAnchorForTab("quests"),
                "Bubble" or "BubbleCount" => NavAnchorForTab("studio"),
                _ => null,
            };
            if (TryBankAnchorCenter(host, mapped, out origin)) return true;
            if (TryBankAnchorCenter(host, Named<Button>("BtnProfileBubble"), out origin)) return true;
            if (TryBankAnchorBounds(host, Named<Border>("XPBarTrack"), out var track))
            {
                origin = new Point(track.Left + BankFallbackOriginDx, track.Top + (track.Height / 2));
                return IsFiniteBankPoint(origin);
            }
            origin = default;
            return false;
        }

        private static bool TryBankAnchorCenter(Control host, Control? anchor, out Point center)
        {
            if (TryBankAnchorBounds(host, anchor, out var bounds))
            {
                center = bounds.Center;
                return IsFiniteBankPoint(center);
            }
            center = default;
            return false;
        }

        private static bool TryBankAnchorBounds(Control host, Control? anchor, out Rect bounds)
        {
            bounds = default;
            try
            {
                if (anchor == null || !anchor.IsEffectivelyVisible) return false;
                if (anchor.Bounds.Width <= 0 || anchor.Bounds.Height <= 0) return false;
                if (anchor.TranslatePoint(default, host) is not { } topLeft) return false;
                if (anchor.TranslatePoint(new Point(anchor.Bounds.Width, anchor.Bounds.Height), host) is not { } bottomRight) return false;
                bounds = new Rect(topLeft, bottomRight);
                return bounds.Width > 0 && bounds.Height > 0 && IsFiniteBankPoint(bounds.TopLeft) && IsFiniteBankPoint(bounds.BottomRight);
            }
            catch (Exception ex)
            {
                Log.Debug("TryBankAnchorBounds: {E}", ex.Message);
                return false;
            }
        }

        private static bool IsFiniteBankPoint(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

        /// <summary>The token surface: one small canvas on the event host, sized to the origin-to-target box
        /// plus the arc's slack, moved for each flight. It holds no timer between flights.</summary>
        private AmbientFxCanvas? EnsureBankLayer(Panel host, Point origin, Point target)
        {
            if (_bankLayerFailed) return null;
            try
            {
                double left = Math.Min(origin.X, target.X) - BankBoxPadPx;
                double top = Math.Min(origin.Y, target.Y) - BankBoxPadPx;
                double width = Math.Abs(origin.X - target.X) + (BankBoxPadPx * 2);
                double height = Math.Abs(origin.Y - target.Y) + (BankBoxPadPx * 2);
                if (width > BankBoxMaxPx || height > BankBoxMaxPx) return null;

                if (_bankLayer == null)
                {
                    _bankLayer = new AmbientFxCanvas
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        IsHitTestVisible = false,
                    };
                    host.Children.Add(_bankLayer);
                }
                bool resized = Math.Abs(_bankLayer.Width - width) > 0.5 || Math.Abs(_bankLayer.Height - height) > 0.5
                            || _bankLayer.Bounds.Width <= 1 || _bankLayer.Bounds.Height <= 1;
                _bankLayer.Width = width;
                _bankLayer.Height = height;
                _bankLayer.Margin = new Thickness(left, top, 0, 0);
                _bankLayerOrigin = new Point(left, top);
                if (resized) _bankLayer.UpdateLayout();
                return _bankLayer;
            }
            catch (Exception ex)
            {
                _bankLayerFailed = true;
                Log.Warning(ex, "EnsureBankLayer failed - THE BANK disabled");
                return null;
            }
        }
    }
}
