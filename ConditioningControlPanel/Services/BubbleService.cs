using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bubble popping game - bubbles float up from bottom of screen, user pops them by clicking
/// </summary>
public class BubbleService : IDisposable
{
    private const int MAX_BUBBLES = 3;          // per-window fallback cap (SetWindowPos-bound — keep small)
    private const int MAX_BUBBLES_HOST = 40;    // shared-host cap: a dense ambient field is cheap on the Canvas
    // Trigger/effect bubbles ALWAYS render per-window (forceWindowMode) even with the shared host on,
    // and each is a WS_EX_LAYERED window repositioned via SetWindowPos every frame. So they must be
    // capped independently of the (Canvas-cheap) host field cap — otherwise a high trigger chance puts
    // dozens of layered windows on screen and starves desktop-heap/GPU surfaces (freeze cluster #448/#431).
    private const int MAX_TRIGGER_WINDOWS = 4;
    /// <summary>Concurrent ambient cap. The per-window path stays at 3 (each move is a SetWindowPos);
    /// the shared-host path repositions via batched Canvas.SetLeft/Top, so it carries a dense field.</summary>
    private int MaxAmbientBubbles => _ambientHost ? MAX_BUBBLES_HOST : MAX_BUBBLES;
    // ---- Ambient shared-host (dashboard dense field) ----
    // Mirrors the chaos shared-host path so the dashboard bubble game stays solid at a high spawn
    // rate / concurrent cap. Gated by AppSettings.BubbleSharedHost; latched for the Start->Stop session.
    // When off, ambient bubbles keep the proven per-window path and none of this engages.
    private bool _ambientHost;
    private Services.GlobalMouseHook? _ambientHook;
    private readonly List<Bubble> _bubbles = new();
    private readonly Random _random = new();
    private DispatcherTimer? _spawnTimer;
    private DispatcherTimer? _animationTimer; // Single shared animation timer for all bubbles
    private bool _isRunning;
    private BitmapImage? _bubbleImage;
    private string _assetsPath = "";
    // Per-screen DPI is now computed on demand via Bubble.GetDpiForScreen()

    // ---- Avatar "pops it for you" easter egg (ambient effect bubbles only) ----
    // When an ambient effect bubble lingers past 4s, a 10% roll sends the companion to glide over,
    // narrate the effect, pop it 50% louder (firing the payload), then return. One at a time, with a
    // 60s cooldown so it stays a rare surprise. See TryTriggerAvatarBubbleEgg / RunAvatarBubbleEggAsync.
    private bool _avatarEggActive;
    private DateTime _avatarEggCooldownUntil = DateTime.MinValue;
    private CancellationTokenSource? _eggCts;
    private const double AVATAR_EGG_AGE_MS = 4000;
    private const int AVATAR_EGG_CHANCE_PCT = 10;
    private const double AVATAR_EGG_COOLDOWN_SEC = 60;

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public int ActiveBubbles => _bubbles.Count;
    /// <summary>Freeze pickups currently on screen — ChaosModeService caps fresh spawns against this.</summary>
    public int ActiveFreezeBubbles => _bubbles.Count(b => b.Spec?.IsFreeze == true);

    /// <summary>
    /// Snapshot of currently-poppable bubbles for Focus Gaze hit-testing.
    /// Caller iterates in reverse for topmost-first selection.
    /// </summary>
    internal IReadOnlyList<Bubble> GetGazeTargets()
    {
        // Defensive copy so callers can iterate without worrying about
        // _bubbles mutation from the spawn/animation timers.
        var list = new List<Bubble>(_bubbles.Count);
        foreach (var b in _bubbles)
        {
            if (b.CanGazePop) list.Add(b);
        }
        return list;
    }

    private bool _isPaused;

    // ---- Chaos Mode hooks (set by ChaosModeService via BeginChaosMode) ----
    private Action<EffectBubbleSpec>? _chaosOnBenignPop;
    private Action<EffectBubbleSpec, double, bool>? _chaosOnDefuse;   // (spec, fuse seconds at the judge point, via player channel)
    private Func<EffectBubbleSpec, bool>? _chaosCanChannel;           // focus gate: may the player START a defuse channel?
    private Action<EffectBubbleSpec, string>? _chaosOnChannelBroken;  // "click" | "release" | "nofocus" — the trigger follows via onDetonate
    private Action<EffectBubbleSpec>? _chaosOnDetonate;
    private Action<EffectBubbleSpec>? _chaosOnTreatExpired;
    private Action<EffectBubbleSpec>? _chaosOnTeaseTouched;   // The Tease: a mouse-down landed on it
    private Action<EffectBubbleSpec>? _chaosOnTeaseDenied;    // The Tease: it expired untouched (the bonus)
    private Action<EffectBubbleSpec>? _chaosOnBrittleShattered; // The Brittle: the cursor touched the glass
    private Action<int>? _chaosOnEStimArc;   // a charged pop just arced; arg = charges remaining
    private Action<EffectBubbleSpec, bool>? _chaosOnDarterCaught;
    private Action<EffectBubbleSpec>? _chaosOnFreezeCaught;
    private Func<double>? _chaosChainReach;   // Chain Reaction boon: burst reach as a box-multiple (<=1 = off)
    private Func<double>? _chaosHitboxScale;  // Magic Wand boon + Mesmer Reach upgrade: hitbox multiple, sampled at spawn
    private Func<double>? _chaosBubbleOpacity; // Blindfold boon: effect-bubble opacity multiplier, sampled at spawn
    private Func<bool>? _chaosWandShimmer;    // Magic Wand capstone: in-reach bubbles shimmer
    private Func<double>? _chaosCursorPull;   // The Pull: per-frame drift bias toward the cursor (0 = off)
    private Func<bool>? _chaosRabbitHoming;   // The Pull: darters steer toward the cursor
    private Func<bool>? _chaosSpankerOn;      // The Spanker: clicking a darter redirects it instead of catching
    private Func<double>? _chaosSpankGrow;    // The Spanker: growth factor applied per smack
    private Func<bool>? _chaosLiveMagnet;     // Silk Touch: near-miss on a LIVE still touches (wider hit ellipse), sampled at spawn
    private Func<double>? _chaosRabbitTrailSec; // Tail-Plug: trail seconds rabbits drag (0 = off)
    private Func<bool>? _chaosElectrified;      // Electrified Rabbits: spank victims discharge free arcs
    // Cursor sample (physical px) shared by every bubble's shimmer check; written once per anim tick.
    internal static double CursorPxX, CursorPxY;
    internal static bool WandShimmerOn;
    // The Pull / The Spanker — sampled once per anim tick (cheap shared reads in AnimateFrame).
    internal static double ChaosCursorPullNow;
    internal static bool ChaosRabbitHomingNow;
    internal static bool ChaosSpankerOnNow;
    internal static double ChaosSpankGrowNow = 1.0;
    // Blank Eyes: the centre (DIPs) of the chaos bubble that popped most recently — written in
    // Bubble.Pop just before the callbacks fire, read by ChaosModeService to float "+score" there.
    internal static double ChaosLastPopXDip, ChaosLastPopYDip;
    // Same anchor in physical px — Gold Digger droplets and GG sweeper rabbits spawn at the pop.
    internal static double ChaosLastPopXPx, ChaosLastPopYPx;
    // Tail-Plug: seconds of treat-popping trail rabbits drag (0 = boon not taken). Sampled per tick.
    internal static double ChaosRabbitTrailSecNow;
    // the Ripple (right-click): the hook thread decides swallow-vs-pass against this IMMUTABLE
    // snapshot of chaos bubble centres (physical px), rebuilt each anim tick on the UI thread.
    // Reference assignment is atomic; the hook must never touch the live bubble list.
    internal static Point[] ChaosBubbleCentersSnapshot = Array.Empty<Point>();
    /// <summary>Shared-host mode: live clickable bubble hit discs (physical px, centre+radius) for the
    /// mouse-hook left-click swallow decision (off-thread; immutable snapshot rebuilt each tick).</summary>
    internal static (double X, double Y, double R, bool Hold)[] ChaosClickDiscsSnapshot = Array.Empty<(double, double, double, bool)>();
    private bool _sharedHost;   // AppSettings.ChaosBubbleSharedHost, latched for the run
    // Spawn-spike amortization: chaos cadence bursts enqueue their construction here; the anim tick
    // materialises at most MaxSpawnsPerFrame per frame so a burst spreads across frames instead of
    // blocking the UI thread in one synchronous BuildChaosLayers pass (the "frame skip on spawn").
    // Enqueued + drained only on the UI thread (RunOnUI body / anim tick) → no lock needed.
    private readonly Queue<Action> _spawnQueue = new();
    private const int MaxSpawnsPerFrame = 1;
    // Electrified Rabbits (Spanker + E-Stim duo): spank victims discharge free arcs. Sampled per tick.
    internal static bool ChaosElectrifiedNow;
    // VibePopping active skill: while the buzz is on and the mouse button is HELD, the cursor pops
    // everything it sweeps over — live ones snap too. Capstone: hovering alone pops, no hold needed.
    internal static bool VibePopOn;
    internal static bool VibeHoverPops;
    internal static bool VibeMouseHeld;
    // Hold-to-defuse: left-button state sampled once per anim tick — channels poll this
    // instead of relying on per-window MouseUp (which is lost when the cursor strays).
    internal static bool ChaosMouseHeld;
    // Manual pause: chaos bubbles ignore clicks entirely (distinct from the freeze power-up,
    // where popping the held field is the reward).
    internal static bool ChaosInputLocked;
    private bool _chaosActive;
    private bool _chaosFrozen;   // freeze-bubble power-up: holds all bubble motion + fuses in place
    private double _freezeVibrateRemainingMs;   // brief shudder applied to every bubble as the freeze lands
    private double _chaosTimeScale = 1.0;   // darter slow-mo power-up: <1 slows motion + fuses

    public event Action? OnBubblePopped;
    public event Action? OnBubbleMissed;

    public void Start(bool bypassLevelCheck = false, int? frequency = null)
    {
        if (_isRunning) return;

        var settings = App.Settings.Current;

        _isRunning = true;

        _assetsPath = App.UserAssetsPath;

        // Pre-load bubble image
        LoadBubbleImage();

        // Stand up the shared host (if enabled) BEFORE the first synchronous spawn below, so that
        // bubble's Add() finds the host already up. EnsureCreated creates synchronously on the UI thread.
        BeginAmbientHostIfEnabled();

        // Start spawning bubbles based on frequency setting
        var intervalMs = 60000.0 / Math.Max(1, frequency ?? settings.BubblesFrequency); // frequency per minute
        
        _spawnTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _spawnTimer.Tick += (s, e) => SpawnBubble();
        _spawnTimer.Start();

        // Single shared animation timer for all bubbles (32ms = ~30 FPS, sufficient for floating bubbles)
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(32)
        };
        _animationTimer.Tick += AnimateAllBubbles;
        _animationTimer.Start();

        // Spawn first bubble immediately
        SpawnBubble();

        // Update Discord presence
        App.DiscordRpc?.SetBubbleActivity();

        App.Logger?.Information("BubbleService started - {Freq} bubbles/min", settings.BubblesFrequency);
    }

    /// <summary>Freeze/unfreeze all chaos bubble motion + fuse countdowns (the freeze-bubble power-up).
    /// Bubbles still render (their blue freeze aura pulses) — only their motion + fuses are held.</summary>
    public void SetChaosFrozen(bool frozen) => _chaosFrozen = frozen;

    /// <summary>Kick off a brief whole-field shudder (used as the freeze lands). Duration in ms.</summary>
    public void VibrateAllForFreeze(int ms) => _freezeVibrateRemainingMs = Math.Max(0, ms);

    /// <summary>Time-scale for chaos bubble motion + fuses (the darter slow-mo power-up). 1 = normal, &lt;1 = slow.</summary>
    public void SetChaosTimeScale(double scale) => _chaosTimeScale = Math.Clamp(scale, 0.05, 1.0);

    /// <summary>Lock/unlock chaos bubble clicks (manual pause). A locked bubble swallows the click.</summary>
    public void SetChaosInputLocked(bool locked) => ChaosInputLocked = locked;

    /// <summary>VibePopping buzz on/off. While on, holding the mouse button sweeps pops over
    /// everything the cursor passes (live ones snap). <paramref name="hoverPops"/> (the capstone)
    /// drops the hold requirement: hovering alone pops.</summary>
    public void SetVibePop(bool on, bool hoverPops = false)
    {
        VibePopOn = on && _chaosActive;
        VibeHoverPops = on && hoverPops;
        if (!VibePopOn) VibeMouseHeld = false;
    }

    /// <summary>Pop the benign chaos bubble nearest the cursor (VibePopping's opening shot).
    /// Darters/freeze bubbles excluded — catching those stays a manual skill.</summary>
    public void PopNearestBenign()
    {
        if (!_chaosActive || !GetCursorPos(out var cur)) return;
        DispatcherHelper.RunOnUI(() =>
        {
            Bubble? best = null;
            double bestD = double.MaxValue;
            foreach (var b in _bubbles.ToArray())
            {
                if (!b.IsAlive || b.Spec == null) continue;
                if (b.Spec.IsLive || b.Spec.IsDarter || b.Spec.IsFreeze) continue;
                double d = b.DistDipSqToPx(cur.X, cur.Y);
                if (d < bestD) { bestD = d; best = b; }
            }
            best?.Pop();
        });
    }

    /// <summary>Snap (defuse) every live chaos bubble on screen — Freeze Trigger capstone.
    /// <see cref="Bubble.Pop"/> already routes live bubbles through the defuse path (full pay).</summary>
    public void DefuseAllLive()
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            foreach (var b in _bubbles.ToArray())
                if (b.IsAlive && b.Spec != null && b.Spec.IsLive && !b.Spec.IsDarter && !b.Spec.IsFreeze)
                    b.Pop();
        });
    }

    /// <summary>Snap Field capstone: clear the whole field through the PAYING pop paths —
    /// treats pop with payloads, lives snap (same per-bubble rules as the DVD collider).
    /// Darters/freeze pickups are never swept; tease/brittle/chaperone keep their immunity
    /// inside Pop(). PopAllBubbles is the silent wave-janitor wipe (ForceDestroy, zero pay,
    /// zero callbacks) — never route a reward through it.</summary>
    public void PopAllChaosPaid()
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            foreach (var b in _bubbles.ToArray())
                if (b.IsAlive && b.Spec != null && !b.Spec.IsDarter && !b.Spec.IsFreeze)
                    b.Pop();
        });
    }

    /// <summary>Pop every chaos bubble whose box intersects <paramref name="rectDips"/> — the Porn DVD
    /// collider (treats pop with payloads, live ones snap). Darters/freeze bubbles are never swept.
    /// Called from the overlay's own UI-thread timer.</summary>
    public void PopBubblesInRect(Rect rectDips)
    {
        if (!_chaosActive) return;
        // Called by EVERY flying DVD logo every composed frame — the old per-call ToArray()
        // re-allocated the whole field N-logos × refresh-rate times a second. Pop() defers its
        // list removal to the pop animation's completion, so a reverse index scan with a count
        // re-check is safe against anything a pop mutates same-frame.
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            if (i >= _bubbles.Count) continue;
            var b = _bubbles[i];
            if (!b.IsAlive || b.Spec == null) continue;
            if (b.Spec.IsDarter || b.Spec.IsFreeze) continue;
            if (rectDips.IntersectsWith(b.BoundingBox)) b.Pop();
        }
    }

    // ---- E-Stim (charged lightning clicks) ----
    private const int ESTIM_ARCS_PER_POP = 3;     // non-capstone: arcs per charged pop
    private const double ESTIM_RANGE_DIP = 600;   // "close enough" — max arc reach (DIPs)
    private const int ESTIM_HOP_MS = 70;          // delay per hop so the current reads as travel
    private const int ESTIM_CHAIN_CAP = 40;       // capstone ripple sanity cap

    private int _estimChargesLeft;
    private bool _estimFork;

    /// <summary>Charged E-Stim clicks still waiting (0 = not armed). The toy-button glow reads this.</summary>
    public int EStimChargesLeft => _chaosActive ? _estimChargesLeft : 0;

    /// <summary>E-Stim pressed: the player's next <paramref name="charges"/> treat-clicks conduct.
    /// <paramref name="fork"/> (capstone) makes every charged pop a full chain reaction.</summary>
    public void ArmEStim(int charges, bool fork)
    {
        if (!_chaosActive) return;
        _estimChargesLeft = Math.Max(1, charges);
        _estimFork = fork;
    }

    /// <summary>
    /// A genuine player click landed a pop (never chain/sweep/skill pops — see Bubble.PopByClick).
    /// While E-Stim is charged, a click on a treat (the good ones — never rabbits, freeze pickups
    /// or live threats) discharges into nearby suitable bubbles: treats pop, live ones snap.
    /// Non-capstone arcs to the few nearest in reach; the capstone ripples breadth-first through
    /// every suitable bubble close enough to the last one struck. A pop with no conductor in
    /// range keeps its charge — the current waits. UI thread (mouse event).
    /// </summary>
    private void OnChaosBubbleClicked(Bubble source)
    {
        if (!_chaosActive || _estimChargesLeft <= 0) return;
        var spec = source.Spec;
        if (spec == null || spec.IsLive || spec.IsDarter || spec.IsFreeze) return;   // only the good ones conduct

        static Point CenterDip(Bubble b) { var r = b.BoundingBox; return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
        static double DistSq(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }
        static bool Suitable(Bubble b) => b.IsChainable && b.Spec != null && !b.Spec.IsDarter && !b.Spec.IsFreeze;

        var pool = new List<Bubble>();
        foreach (var b in _bubbles.ToArray())
            if (!ReferenceEquals(b, source) && Suitable(b)) pool.Add(b);

        double rangeSq = ESTIM_RANGE_DIP * ESTIM_RANGE_DIP;
        var bolts = new List<(Point From, Point To)>();
        var hits = new List<(Bubble Target, int DelayMs)>();

        if (!_estimFork)
        {
            // Up to N nearest suitable bubbles within reach of the popped one.
            var src = CenterDip(source);
            pool.Sort((x, y) => DistSq(CenterDip(x), src).CompareTo(DistSq(CenterDip(y), src)));
            foreach (var t in pool)
            {
                if (hits.Count >= ESTIM_ARCS_PER_POP) break;
                if (DistSq(CenterDip(t), src) > rangeSq) break;   // sorted — everything after is farther
                bolts.Add((source.CenterPx, t.CenterPx));
                hits.Add((t, ESTIM_HOP_MS * (hits.Count + 1)));
            }
        }
        else
        {
            // Capstone chain reaction: breadth-first ripple — every struck bubble conducts onward
            // to ALL suitable bubbles within its own reach, until the cluster is spent.
            var frontier = new Queue<(Bubble B, int Depth)>();
            frontier.Enqueue((source, 0));
            var taken = new HashSet<Bubble> { source };
            while (frontier.Count > 0 && hits.Count < ESTIM_CHAIN_CAP)
            {
                var (cur, depth) = frontier.Dequeue();
                var curDip = CenterDip(cur);
                foreach (var t in pool)
                {
                    if (taken.Contains(t) || DistSq(CenterDip(t), curDip) > rangeSq) continue;
                    taken.Add(t);
                    bolts.Add((cur.CenterPx, t.CenterPx));
                    hits.Add((t, ESTIM_HOP_MS * (depth + 1)));
                    frontier.Enqueue((t, depth + 1));
                    if (hits.Count >= ESTIM_CHAIN_CAP) break;
                }
            }
        }

        if (bolts.Count == 0) return;   // nothing in reach — the charge holds for the next pop

        _estimChargesLeft--;
        if (_estimChargesLeft <= 0) _estimFork = false;

        foreach (var (target, delayMs) in hits)
        {
            var t = target;
            var hop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)) };
            hop.Tick += (_, _) =>
            {
                hop.Stop();
                try { if (t.IsAlive) t.Pop(); } catch { }
            };
            hop.Start();
        }
        if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Strike(bolts); else ChaosEStimOverlay.Strike(bolts);
        _chaosOnEStimArc?.Invoke(_estimChargesLeft);
    }

    // Free synergy discharges (Electrified Rabbits / Body Buzz): same look as a charged pop,
    // but no charge is consumed and the arcs never chain onward.
    private const double ESTIM_BURST_RANGE_PX = 620;
    private DateTime _lastBurstZap;   // a mowing rabbit fires bursts per victim — keep the crack from machine-gunning

    /// <summary>Discharge free E-Stim bolts from <paramref name="fromPx"/> into up to
    /// <paramref name="maxArcs"/> nearest suitable bubbles (treats pop, live ones snap;
    /// rabbits and freeze pickups don't conduct). UI thread.</summary>
    private void EStimBurstAt(Point fromPx, int maxArcs, double rangePx = ESTIM_BURST_RANGE_PX)
    {
        if (!_chaosActive || maxArcs <= 0) return;
        static double DistSq(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }

        var pool = new List<Bubble>();
        double rangeSq = rangePx * rangePx;
        foreach (var b in _bubbles.ToArray())
            if (b.IsChainable && b.Spec != null && !b.Spec.IsDarter && !b.Spec.IsFreeze
                && DistSq(b.CenterPx, fromPx) <= rangeSq) pool.Add(b);
        if (pool.Count == 0) return;

        pool.Sort((x, y) => DistSq(x.CenterPx, fromPx).CompareTo(DistSq(y.CenterPx, fromPx)));
        var bolts = new List<(Point From, Point To)>();
        for (int i = 0; i < pool.Count && i < maxArcs; i++)
        {
            var target = pool[i];
            bolts.Add((fromPx, target.CenterPx));
            var hop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ESTIM_HOP_MS * (i + 1)) };
            hop.Tick += (_, _) =>
            {
                hop.Stop();
                try { if (target.IsAlive) target.Pop(); } catch { }
            };
            hop.Start();
        }
        if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Strike(bolts); else ChaosEStimOverlay.Strike(bolts);
        var now = DateTime.UtcNow;
        if ((now - _lastBurstZap).TotalMilliseconds >= 140)
        {
            _lastBurstZap = now;
            PlayChaosCue("estim_zap", 0.45f);
        }
    }

    /// <summary>Body Buzz (Poppers + E-Stim duo): an electric shockwave at <paramref name="centerPx"/> —
    /// expanding ring + the current leaping into every bubble inside it. Any thread.</summary>
    public void TriggerEStimShockwave(Point centerPx)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Ripple(centerPx, SHOCKWAVE_RADIUS_PX, 450, strong: false);
            else ChaosFieldFxOverlay.Ripple(centerPx, SHOCKWAVE_RADIUS_PX, 450);
            EStimBurstAt(centerPx, maxArcs: 8, rangePx: SHOCKWAVE_RADIUS_PX);
        });
    }

    private const double SHOCKWAVE_RADIUS_PX = 440;

    private void AnimateAllBubbles(object? sender, EventArgs e)
    {
        // Spawn-spike amortization: materialise at most one queued chaos bubble per frame. A cadence
        // burst (several SpawnChaos* in one dispatcher pass) would otherwise construct N bubbles back-to-
        // back and drop a frame across every window. Drain BEFORE the empty-field early-return below so a
        // freshly-queued bubble still materialises (and gets AnimateFrame this same tick). Each thunk
        // self-handles its own exceptions. (Throttle disabled by leaving the queue empty when not chaos.)
        for (int s = 0; s < MaxSpawnsPerFrame && _chaosActive && _spawnQueue.Count > 0; s++)
            _spawnQueue.Dequeue()();

        // NOTE: a freeze does NOT skip this loop — bubbles must keep rendering so the freeze aura
        // pulses, the shudder plays, and any in-flight pop finishes. Each bubble holds its own
        // motion/fuse while frozen (see Bubble.AnimateFrame).
        if (_freezeVibrateRemainingMs > 0) _freezeVibrateRemainingMs -= 32;
        if (_bubbles.Count == 0)
        {
            // A field clear (wave draft) force-destroys bound halves without their callbacks —
            // don't leave their tether lines hanging over the draft table.
            if (_boundTetherKeys.Count > 0 || _boundFirstResolved.Count > 0) ClearBoundState();
            if (ChaosBubbleCentersSnapshot.Length > 0) ChaosBubbleCentersSnapshot = Array.Empty<Point>();
            // Don't leave a stale disc behind once the field empties — a hook click would otherwise be
            // swallowed where a bubble just was, with nothing to pop.
            if (ChaosClickDiscsSnapshot.Length > 0) ChaosClickDiscsSnapshot = Array.Empty<(double, double, double, bool)>();
            return;
        }

        // Wand shimmer / VibePopping / The Pull / The Spanker: sample the cursor + boon knobs once
        // per tick (one P/Invoke); every bubble reads the shared fields instead of asking Win32 itself.
        WandShimmerOn = _chaosActive && (_chaosWandShimmer?.Invoke() ?? false);
        ChaosCursorPullNow = _chaosActive ? (_chaosCursorPull?.Invoke() ?? 0) : 0;
        ChaosRabbitHomingNow = _chaosActive && (_chaosRabbitHoming?.Invoke() ?? false);
        ChaosSpankerOnNow = _chaosActive && (_chaosSpankerOn?.Invoke() ?? false);
        ChaosSpankGrowNow = _chaosActive ? Math.Max(1.0, _chaosSpankGrow?.Invoke() ?? 1.0) : 1.0;
        ChaosRabbitTrailSecNow = _chaosActive ? Math.Max(0, _chaosRabbitTrailSec?.Invoke() ?? 0) : 0;
        ChaosElectrifiedNow = _chaosActive && (_chaosElectrified?.Invoke() ?? false);
        // The cursor + left-button samples feed wand shimmer, vibe sweeps, The Pull/Cam Girl,
        // AND every hold-to-defuse channel — sample once per tick whenever a chaos run is live
        // (one P/Invoke; ambient bubbles never read these).
        if (_chaosActive && GetCursorPos(out var cur))
        { CursorPxX = cur.X; CursorPxY = cur.Y; }
        ChaosMouseHeld = _chaosActive && (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        if (VibePopOn) VibeMouseHeld = ((GetAsyncKeyState(VK_LBUTTON) | GetAsyncKeyState(VK_RBUTTON)) & 0x8000) != 0;

        // Animate all bubbles in a single pass - iterate by index to avoid allocation
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            if (i < _bubbles.Count)
                _bubbles[i].AnimateFrame();
        }

        TickFieldHazards();   // Size Queen ripples / Aftermath residue / Tail-Plug trails
        TickBoundPairs();     // The Bound: tether lines + the enrage window
        TryTriggerAvatarBubbleEgg();   // companion "I'll pop this one for you" easter egg

        // the Ripple: refresh the hook thread's swallow-decision snapshot (chaos bubbles only).
        if (_chaosActive)
        {
            var centers = new List<Point>(_bubbles.Count);
            foreach (var b in _bubbles)
                if (b.IsAlive && b.Spec != null) centers.Add(b.CenterPx);
            ChaosBubbleCentersSnapshot = centers.ToArray();
        }
        else if (ChaosBubbleCentersSnapshot.Length > 0)
        {
            ChaosBubbleCentersSnapshot = Array.Empty<Point>();
        }

        // Shared-host pop targets: hit discs (physical px) for the mouse-hook swallow decision. Now
        // maintained for BOTH the chaos field and the ambient dashboard host. Only HOST-rendered bubbles
        // go in: a per-window bubble keeps its own WPF click handler and must never also be popped through
        // the hook (double-pop). UsesHost is the invariant that keeps every chaos/ambient x host/per-window
        // combination correct (e.g. chaos running per-window while the ambient host is up).
        if (_chaosActive || _ambientHost)
        {
            var discs = new List<(double, double, double, bool)>(_bubbles.Count);
            foreach (var b in _bubbles)
                if (b.UsesHost && b.HostHitClickable) { var d = b.HitDiscPx; discs.Add((d.X, d.Y, d.R, b.NeedsHoldDefuse)); }
            ChaosClickDiscsSnapshot = discs.ToArray();
        }
        else if (ChaosClickDiscsSnapshot.Length > 0)
        {
            ChaosClickDiscsSnapshot = Array.Empty<(double, double, double, bool)>();
        }
    }

    /// <summary>HOOK THREAD: a left-click landed at this physical-px point. If it's inside a live
    /// clickable bubble's hit disc, swallow it (so the click-through host doesn't also pass the click
    /// to whatever sits behind it) and marshal the real pop to the UI thread. A miss passes through.
    /// Touches only the immutable disc snapshot — never a WPF dependency property. Mirrors the Ripple's
    /// OnRippleRightDown contract.</summary>
    public bool OnSharedHostLeftDown(Point px)
    {
        // Either owner of the shared host may be live: a chaos run, or the ambient dashboard field.
        // The disc snapshot only ever holds host-rendered bubbles, so this is correct for both.
        if (!_chaosActive && !_ambientHost) return false;
        var discs = ChaosClickDiscsSnapshot;
        bool hit = false, needsHold = false;
        foreach (var d in discs)
        {
            double dx = d.X - px.X, dy = d.Y - px.Y;
            if (dx * dx + dy * dy <= d.R * d.R) { hit = true; needsHold = d.Hold; break; }
        }
        if (!hit) return false;
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return false;
        disp.BeginInvoke(new Action(() => PopTopmostAt(px)));
        // Live hold-to-defuse bubbles must NOT swallow: the channel reads the held button via
        // GetAsyncKeyState, which never sees a swallowed low-level click (→ instant detonate). Let the
        // click pass through for those; instant-pop bubbles swallow cleanly (one click, no desktop leak).
        return !needsHold;
    }

    /// <summary>UI THREAD: pop the front-most live clickable bubble under a physical-px point (last
    /// spawned = drawn on top = checked first). Routes through OnPlayerPress like a real click.</summary>
    private void PopTopmostAt(Point px)
    {
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            var b = _bubbles[i];
            // UsesHost guards against hook-popping a per-window bubble (which owns a WPF click handler).
            if (b.UsesHost && b.HostHitClickable && b.ContainsPx(px)) { b.HostHookPop(); return; }
        }
    }

    // ======================= Avatar "I'll pop this one for you" easter egg =======================
    // Runs in the shared animation tick (UI thread). When an ambient effect bubble has lingered past
    // 4s, a 10% one-shot roll sends the companion gliding over to narrate + pop it. Skipped while a
    // fullscreen video is up (we'd be popping an invisible bubble), and rate-limited by a 60s cooldown.

    /// <summary>Per-frame scan: latch the 10% roll on any ambient effect bubble crossing 4s, and if it
    /// hits, hand the bubble to the companion. One egg at a time; the loop runs on the UI thread.</summary>
    private void TryTriggerAvatarBubbleEgg()
    {
        if (_avatarEggActive) return;
        // Ambient pop-game only. A chaos run's treats are also _isTreat-with-payload, so without this
        // the egg would claim/freeze a chaos bubble mid-run and pop it through the chaos callback.
        if (_chaosActive) return;
        var s = App.Settings?.Current;
        if (s?.BubbleAvatarEggEnabled != true || s.BubbleTriggersEnabled != true) return;
        if (App.Video?.IsPlaying == true) return;                       // a fullscreen video covers the bubbles
        if (DateTime.UtcNow < _avatarEggCooldownUntil) return;
        var avatar = App.AvatarWindow;
        if (avatar?.CanPerformBubbleEgg != true) return;

        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            var b = _bubbles[i];
            if (b.RolledForEgg || !b.IsAmbientEffectBubble || b.AgeMs <= AVATAR_EGG_AGE_MS) continue;
            b.RolledForEgg = true;                                      // one-shot latch at the 4s crossing
            if (_random.Next(100) < AVATAR_EGG_CHANCE_PCT)
            {
                _avatarEggActive = true;
                RunAvatarBubbleEggAsync(b);
                break;
            }
        }
    }

    /// <summary>The choreography: claim → detach + glide beside → narrate (bubble + voiceline) →
    /// pop 50% louder (fires the effect) → glide home. Invoked from the timer tick, so awaits resume
    /// on the UI thread (WPF DispatcherSynchronizationContext) — no marshaling needed.</summary>
    private async void RunAvatarBubbleEggAsync(Bubble bubble)
    {
        var avatar = App.AvatarWindow;
        _eggCts?.Dispose();
        _eggCts = new CancellationTokenSource();
        var ct = _eggCts.Token;
        bubble.ClaimForAvatar();
        try
        {
            if (avatar == null) return;

            // 1) glide the companion beside the bubble (auto-detaches if attached; captures restore state)
            await avatar.GlideToBubbleAsync(bubble.CenterPx, bubble.RadiusPx, ct);
            if (ct.IsCancellationRequested || !bubble.IsAlive) return;

            // 2) speak the mod-themed, effect-specific line (the avatar narrates what it's about to fire)
            var line = PickEggVoiceLine(bubble.EffectKindId);
            avatar.GigglePriority(line.Text, playSound: line.Audio != null, aiGenerated: false,
                                  phraseAudioPath: line.Audio, barkVoice: line.Audio != null, mood: "playful");
            await Task.Delay(EstimateSpeechMs(line.Text, line.Audio), ct);
            if (ct.IsCancellationRequested || !bubble.IsAlive
                || Application.Current?.Dispatcher?.HasShutdownStarted == true) return;

            // 3) the companion pops it — 50% louder, and the benign callback fires the effect payload
            bubble.PopByAvatar(1.5f);

            // 3b) Let the payload's presentation surge settle before going home: the pop fires
            // burst sparkles + an overlay/flash show, all damaging layered surfaces at once, and
            // the re-attach then resizes/re-styles the avatar's OWN layered window. That exact
            // collision produced a captured render-thread deadlock (hang_20260713_110759: render
            // thread wedged in CWGXBitmapLockState::LockRead) — give the surge a beat to pass.
            await Task.Delay(2500, ct);
        }
        catch (OperationCanceledException) { /* run ended / shutdown mid-egg */ }
        catch (Exception ex) { App.Logger?.Debug("Avatar bubble egg failed: {E}", ex.Message); }
        finally
        {
            bubble.ReleaseAvatarClaim();
            // 4) send the companion home (re-attach or restore coords); swallow if we're tearing down
            try { if (avatar != null && Application.Current?.Dispatcher?.HasShutdownStarted != true)
                      await avatar.ReturnFromBubbleAsync(CancellationToken.None); }
            catch { }
            _avatarEggActive = false;
            _avatarEggCooldownUntil = DateTime.UtcNow + TimeSpan.FromSeconds(AVATAR_EGG_COOLDOWN_SEC);
        }
    }

    /// <summary>Teardown hook (Stop / PopAllBubbles): cancel any in-flight egg and release the claim so
    /// the claimed bubble can actually be cleared (the claim-pop guard would otherwise strand it).</summary>
    private void CancelAvatarEgg()
    {
        try { _eggCts?.Cancel(); } catch { }
        foreach (var b in _bubbles) if (b.ClaimedByAvatar) b.ReleaseAvatarClaim();
        _avatarEggActive = false;
    }

    /// <summary>Map an effect variant id → its mod-themed voiceline (2 variants each), via the bark
    /// manifest. Falls back to a generic rule, then to inline text if no clip is authored yet.</summary>
    private (string Text, string? Audio) PickEggVoiceLine(string variantId)
    {
        string rule = variantId switch
        {
            "flash"      => "egg_avatar_pop_flash",
            "subliminal" => "egg_avatar_pop_subliminal",
            "pink"       => "egg_avatar_pop_pink",
            "spiral"     => "egg_avatar_pop_spiral",
            "braindrain" => "egg_avatar_pop_braindrain",
            "video"      => "egg_avatar_pop_video",
            "htlink"     => "egg_avatar_pop_gifrain",
            "glitch"     => "egg_avatar_pop_glitch",
            _            => "egg_avatar_pop_generic",
        };
        var pick = App.Bark?.PickVoiceLine(rule) ?? App.Bark?.PickVoiceLine("egg_avatar_pop_generic");
        if (pick.HasValue) return (pick.Value.Text, pick.Value.Audio);
        return ("Ooh — let me pop this one for you~", null);
    }

    /// <summary>Estimate how long to leave the speech bubble up before popping: the clip's real length
    /// (+ lead-in + tail) when voiced, else a reading-speed estimate from the text.</summary>
    private static int EstimateSpeechMs(string text, string? audioPath)
    {
        try
        {
            if (audioPath != null && File.Exists(audioPath))
            {
                using var r = new AudioFileReader(audioPath);
                return (int)Math.Clamp(r.TotalTime.TotalMilliseconds + 900, 1500, 6000);
            }
        }
        catch { }
        return (int)Math.Clamp((text?.Length ?? 0) * 55 + 600, 1500, 4500);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _spawnTimer?.Stop();
        _spawnTimer = null;

        _animationTimer?.Stop();
        _animationTimer = null;

        // DispatcherTimer ticks are synchronous on the UI thread and won't
        // fire after Stop(), so no delay is needed here.

        // Pop all remaining bubbles
        PopAllBubbles();

        // Tear down the shared host + hook (releases our ref; the host survives if chaos still holds one).
        EndAmbientHost();

        // Update Discord presence back to idle (unless another activity takes over)
        App.DiscordRpc?.SetIdleActivity();

        App.Logger?.Information("BubbleService stopped");
    }

    /// <summary>Stand up the ambient shared-host overlay + its left-click hook when BubbleSharedHost is
    /// on. Idempotent for the running session. The host is ref-counted, so it coexists with a chaos run
    /// that takes its own reference.</summary>
    private void BeginAmbientHostIfEnabled()
    {
        if (_ambientHost) return;
        if (App.Settings?.Current?.BubbleSharedHost != true) return;
        _ambientHost = true;
        Bubble.AmbientHostActive = true;
        ChaosBubbleHostOverlay.EnsureCreated();
        try
        {
            // Ambient pops ride a global left-click hook exactly like the chaos field. It self-suppresses
            // while a chaos run is active (chaos owns its own _rippleHook there), so a single click never
            // pops twice; during a chaos run the ambient field is paused + cleared anyway.
            _ambientHook = new Services.GlobalMouseHook
            {
                LeftDown = px => !_chaosActive && OnSharedHostLeftDown(px)
            };
            _ambientHook.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("Ambient bubble hook start: {E}", ex.Message); }
    }

    /// <summary>Release the ambient host reference + dispose the ambient hook. Safe to call when the
    /// host was never started.</summary>
    private void EndAmbientHost()
    {
        if (!_ambientHost) return;
        _ambientHost = false;
        Bubble.AmbientHostActive = false;
        try { _ambientHook?.Dispose(); } catch { }
        _ambientHook = null;
        ChaosBubbleHostOverlay.CloseActive();
        if (ChaosClickDiscsSnapshot.Length > 0) ChaosClickDiscsSnapshot = Array.Empty<(double, double, double, bool)>();
    }

    public void RefreshFrequency()
    {
        if (!_isRunning || _spawnTimer == null) return;

        _spawnTimer.Stop();

        var intervalMs = 60000.0 / Math.Max(1, App.Settings.Current.BubblesFrequency);
        _spawnTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);

        _spawnTimer.Start();

        App.Logger?.Information("BubbleService frequency updated to {Freq} bubbles/min", App.Settings.Current.BubblesFrequency);
    }

    /// <summary>
    /// Pause bubble spawning and clear all active bubbles (for bubble count minigame)
    /// </summary>
    public void PauseAndClear()
    {
        if (!_isRunning) return;

        _isPaused = true;
        _spawnTimer?.Stop();
        PopAllBubbles();

        App.Logger?.Debug("BubbleService paused and cleared for minigame");
    }

    /// <summary>
    /// Resume bubble spawning after pause
    /// </summary>
    public void Resume()
    {
        if (!_isRunning || !_isPaused) return;

        _isPaused = false;
        _spawnTimer?.Start();

        App.Logger?.Debug("BubbleService resumed");
    }

            private void LoadBubbleImage()
            {
                try
                {
                    var resolved = ModResourceResolver.ResolveImage("bubble.png");
                    if (resolved is BitmapImage bmp)
                    {
                        _bubbleImage = bmp.IsFrozen ? bmp : bmp.Clone();
                        if (!_bubbleImage.IsFrozen) _bubbleImage.Freeze();
                    }
                    else
                    {
                        var resourceUri = new Uri("pack://application:,,,/Resources/bubble.png", UriKind.Absolute);
                        _bubbleImage = new BitmapImage();
                        _bubbleImage.BeginInit();
                        _bubbleImage.UriSource = resourceUri;
                        _bubbleImage.CacheOption = BitmapCacheOption.OnLoad;
                        _bubbleImage.EndInit();
                        _bubbleImage.Freeze();
                    }
                    App.Logger?.Debug("Bubble image loaded");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error("Failed to load bubble image: {Error}", ex.Message);
                }
            }
    private void SpawnBubble()
    {
        if (!_isRunning) return;
        // Hold off spawning while a monitor/DPI change settles (freeze cluster — see DisplayChangeCoordinator).
        if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed) return;
        if (_bubbles.Count >= MaxAmbientBubbles)
        {
            App.Logger?.Debug("Max bubbles reached, skipping spawn");
            return;
        }

        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                var settings = App.Settings.Current;
                // Baseline spawn: honor DualMonitorEnabled and let bubbles
                // spawn on all monitors. Gaze-pop interaction on off-cal-
                // screen bubbles is filtered by GazeFocusService.FindBestTarget;
                // mouse-click still works everywhere.
                var screens = settings.DualMonitorEnabled
                    ? App.GetAllScreensCached()
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

                var screen = screens[_random.Next(screens.Length)];
                // Outside sessions, bubbles are always clickable (no UI toggle exists for this setting)
                var isClickable = App.IsSessionRunning ? settings.BubblesClickable : true;
                var bubble = CreateAmbientBubble(screen, isClickable);
                _bubbles.Add(bubble);

                App.Logger?.Debug("Spawned bubble, total: {Count}", _bubbles.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to spawn bubble: {Error}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Spawn a single bubble immediately (for keyword triggers).
    /// Works even when the service isn't continuously running.
    /// </summary>
    public void SpawnOnce()
    {
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                // Hold off while a monitor/DPI change settles (freeze cluster — see DisplayChangeCoordinator).
                if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed) return;

                // Respect the concurrent cap (SpawnBubble does; keyword-triggered spawns must too, or a
                // held-down trigger floods layered windows). Checked before starting the timer.
                if (_bubbles.Count >= MaxAmbientBubbles)
                {
                    App.Logger?.Debug("SpawnOnce: max bubbles reached, skipping trigger spawn");
                    return;
                }

                if (_bubbleImage == null)
                    LoadBubbleImage();

                // Ensure animation timer is running to animate the spawned bubble
                if (_animationTimer == null || !_animationTimer.IsEnabled)
                {
                    _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(32)
                    };
                    _animationTimer.Tick += AnimateAllBubbles;
                    _animationTimer.Start();
                }

                var settings = App.Settings.Current;
                // Baseline spawn: keyword-triggered bubbles follow the same
                // DualMonitorEnabled honoring as the running spawn loop. The
                // gaze-read backstop in GazeFocusService.FindBestTarget keeps
                // gaze-pop strictly on the calibrated screen.
                var screens = settings.DualMonitorEnabled
                    ? App.GetAllScreensCached()
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

                var screen = screens[_random.Next(screens.Length)];
                var isClickable = App.IsSessionRunning ? settings.BubblesClickable : true;
                var bubble = CreateAmbientBubble(screen, isClickable);
                _bubbles.Add(bubble);

                App.Logger?.Debug("SpawnOnce: spawned trigger bubble, total: {Count}", _bubbles.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SpawnOnce: Failed to spawn bubble: {Error}", ex.Message);
            }
        });
    }

    private void OnPop(Bubble bubble) => AwardAmbientPop(bubble);

    /// <summary>The standard ambient-pop reward: lucky roll, pop sound, XP, achievement, haptic.
    /// Shared by plain bubbles (<see cref="OnPop"/>) and trigger bubbles (whose benign-pop path
    /// doesn't run OnPop), so a trigger bubble pays exactly like a normal pop on top of its effect.</summary>
    private void AwardAmbientPop(Bubble bubble)
    {
        // Roll for lucky bubble (5% chance for 10x XP if skill unlocked)
        var multiplier = App.SkillTree?.RollLuckyBubble() ?? 1;
        var isLucky = multiplier > 1;

        // Tell bubble whether it's lucky so it can show the right visual effects
        var hasSparkleBoost = (App.SkillTree?.GetSparkleBoostTier() ?? 0) > 0 && (App.Settings?.Current?.FlashGlowEnabled ?? true);
        bubble.SetLucky(isLucky, hasSparkleBoost);

        // Play appropriate sound (the avatar easter-egg pop comes through 50% louder)
        PlayPopSound(isLucky, bubble.AvatarPopVolumeMult);

        // Don't remove here - let the pop animation play, removal happens in OnDestroy
        OnBubblePopped?.Invoke();

        App.Progression?.AddXP(5 * multiplier, XPSource.Bubble);

        // Track for achievement
        App.Achievements?.TrackBubblePopped();

        // Haptic feedback with combo system
        _ = App.Haptics?.BubblePopAsync();
    }

    // ======================= Trigger Bubbles =======================
    // Opt-in: a configurable share of ambient bubbles spawn as Chaos effect bubbles that fire
    // their payload ON POP (benign — no fuse/defuse). They keep the full chaos look (variant
    // sprite/tint/label) but render per-window and pay the normal ambient pop reward.

    /// <summary>Roll whether the next ambient bubble should be an effect bubble, and if so build
    /// its (benign) spec. Returns null for a plain bubble.</summary>
    private EffectBubbleSpec? RollTriggerSpec()
    {
        var s = App.Settings?.Current;
        if (s?.BubbleTriggersEnabled != true) return null;
        var ids = s.BubbleTriggerVariants;
        if (ids == null || ids.Count == 0) return null;
        if (_random.Next(100) >= Math.Clamp(s.BubbleTriggerChance, 0, 100)) return null;
        return BuildTriggerSpec(ids[_random.Next(ids.Count)]);
    }

    /// <summary>Build a benign effect-bubble spec for one trigger id. The six standard ids reuse
    /// the chaos variant table; "glitch" is the full-screen GIF/image wash (~30%) on glitch.png.</summary>
    private EffectBubbleSpec? BuildTriggerSpec(string id)
    {
        try
        {
            // Honor the dashboard's Motion setting (ChaosMotionMode: "Mixed"/"FloatUp"/"RainDown"/
            // "RoamBounce") so dashboard effect bubbles move like chaos ones — e.g. "Rain" makes them fall.
            // "Mixed" (and any unrecognised value) don't parse → null → the gentle FloatUp default below.
            // Ambient bubbles are treats (TreatLifeMs dissolves them), so even RoamBounce is safe here.
            ChaosMotion? motion = Enum.TryParse<ChaosMotion>(App.Settings?.Current?.ChaosMotionMode, out var mm)
                ? mm : (ChaosMotion?)null;

            // Trigger effects linger longer than the brisk chaos cadence (user feedback:
            // pink filter / glitch wash were too quick on the calm dashboard).
            const double LINGER = 2.5;
            if (id == "glitch")
            {
                return new EffectBubbleSpec
                {
                    VariantId = "glitch",   // loads assets/Chaos/bubbles/glitch.png
                    // Glitch is exempt from LINGER: its payload is a FULL-SCREEN layered wash,
                    // and at 2.5x (~8s) the whole desktop stayed alpha-composited every frame —
                    // the frame drops / crashes reported when popping glitch bubbles under load
                    // (#476). 1.2x (~4s) still reads as a lingering wash on the calm dashboard.
                    Payload = new OverlayPayload("braindrain", braindrainOpacity: 0.30) { Strength = 60, DurationMult = 1.2 },
                    SizePx = 200,
                    Tint = System.Windows.Media.Color.FromRgb(0x9A, 0x40, 0xFF),
                    Label = "GLITCH",
                    IsLive = false,
                    FuseMs = 0,
                    Motion = motion ?? ChaosMotion.FloatUp,
                    TreatLifeMs = 7000,
                };
            }
            var v = ChaosBubbleVariants.All.FirstOrDefault(x => x.Id == id);
            if (v == null) return null;
            var spec = ChaosBubbleVariants.Build(v, intensity: 0.3, motionOverride: motion, ambient: true);
            if (spec.Payload != null) spec.Payload.DurationMult = LINGER;   // longer-lasting overlays/flashes
            return spec;
        }
        catch (Exception ex) { App.Logger?.Debug("BuildTriggerSpec({Id}): {E}", id, ex.Message); return null; }
    }

    /// <summary>Construct an ambient bubble — plain, or (rolled) an effect bubble that fires on pop.</summary>
    private Bubble CreateAmbientBubble(System.Windows.Forms.Screen screen, bool isClickable)
    {
        var spec = RollTriggerSpec();
        // Trigger bubbles are per-window layered windows; keep their concurrent count low regardless of
        // the overall (host-rendered) field cap. Past the ceiling, fall back to a plain bubble so the
        // dashboard stays lively without a layered-window pileup.
        if (spec != null && _bubbles.Count(b => b.IsAmbientEffectBubble) >= MAX_TRIGGER_WINDOWS)
            spec = null;
        if (spec == null)
            return new Bubble(screen, _bubbleImage, _random, OnPop, OnMiss, OnDestroy, isClickable);
        return new Bubble(screen, _bubbleImage, _random, onPop: null, onMiss: OnMiss, onDestroy: OnDestroy,
                          isClickable: isClickable, spec: spec,
                          onBenignPop: b =>
                          {
                              AwardAmbientPop(b);
                              var sw = System.Diagnostics.Stopwatch.StartNew();
                              try { b.Spec?.Payload?.Fire(); } catch { }
                              sw.Stop();
                              if (sw.ElapsedMilliseconds >= 20)
                                  App.Logger?.Information("[POPLAG] payload {Kind} Fire() took {Ms}ms",
                                      b.Spec?.Payload?.DisplayName ?? "?", sw.ElapsedMilliseconds);
                          },
                          forceWindowMode: true);
    }

    private void OnMiss(Bubble bubble)
    {
        // Bubble floated off screen - remove immediately (no animation needed)
        _bubbles.Remove(bubble);
        OnBubbleMissed?.Invoke();
        StopAnimationTimerIfIdle();
    }

    private void OnDestroy(Bubble bubble)
    {
        // Called when bubble is fully destroyed (after pop animation completes)
        _bubbles.Remove(bubble);
        StopAnimationTimerIfIdle();
    }

    /// <summary>
    /// Stop the animation timer if there are no bubbles left and the service isn't running
    /// (cleans up timers started by SpawnOnce when the service isn't actively running)
    /// </summary>
    private void StopAnimationTimerIfIdle()
    {
        if (!_isRunning && !_chaosActive && _bubbles.Count == 0 && _animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer = null;
        }
    }

    // ======================= Chaos Mode API =======================
    // Chaos Mode reuses this service's bubble rendering (real bubble.png, shared
    // 30fps timer, DPI, pooled pop sounds, click-through windows) but spawns
    // *effect* bubbles carrying payloads with a fuse/defuse mechanic. The ambient
    // pop game above is untouched — these only run while a chaos run is active.

    /// <summary>Enter chaos mode: install effect callbacks + ensure the shared animation timer runs.</summary>
    public void BeginChaosMode(Action<EffectBubbleSpec> onBenignPop, Action<EffectBubbleSpec, double, bool> onDefuse, Action<EffectBubbleSpec> onDetonate,
                               Action<EffectBubbleSpec, bool>? onDarterCaught = null, Action<EffectBubbleSpec>? onFreezeCaught = null,
                               Func<double>? chainReach = null, Func<double>? hitboxScale = null,
                               Func<double>? bubbleOpacity = null, Func<bool>? wandShimmer = null,
                               Func<double>? cursorPull = null, Func<bool>? rabbitHoming = null,
                               Func<bool>? spankerOn = null, Func<double>? spankGrow = null,
                               Func<bool>? liveMagnet = null,
                               Action<EffectBubbleSpec>? onTreatExpired = null, Action<int>? onEStimArc = null,
                               Func<double>? rabbitTrailSec = null, Func<bool>? electrifiedRabbits = null,
                               Func<EffectBubbleSpec, bool>? canChannelDefuse = null,
                               Action<EffectBubbleSpec, string>? onChannelBroken = null,
                               Action<EffectBubbleSpec>? onTeaseTouched = null,
                               Action<EffectBubbleSpec>? onTeaseDenied = null,
                               Action<EffectBubbleSpec>? onBoundEnraged = null,
                               Action<EffectBubbleSpec>? onBrittleShattered = null)
    {
        _chaosOnTeaseTouched = onTeaseTouched;
        _chaosOnTeaseDenied = onTeaseDenied;
        _chaosOnBoundEnraged = onBoundEnraged;
        _chaosOnBrittleShattered = onBrittleShattered;
        ClearBoundState();
        _chaosChainReach = chainReach;
        _chaosRabbitTrailSec = rabbitTrailSec;
        _chaosElectrified = electrifiedRabbits;
        ChaosRabbitTrailSecNow = 0;
        ChaosElectrifiedNow = false;
        _ripples.Clear();
        _residues.Clear();
        _playerRipples.Clear();
        ChaosBubbleCentersSnapshot = Array.Empty<Point>();
        _chaosHitboxScale = hitboxScale;
        _chaosBubbleOpacity = bubbleOpacity;
        _chaosWandShimmer = wandShimmer;
        _chaosCursorPull = cursorPull;
        _chaosRabbitHoming = rabbitHoming;
        _chaosSpankerOn = spankerOn;
        _chaosSpankGrow = spankGrow;
        _chaosLiveMagnet = liveMagnet;
        ChaosCursorPullNow = 0; ChaosRabbitHomingNow = false; ChaosSpankerOnNow = false; ChaosSpankGrowNow = 1.0;
        _chaosOnBenignPop = onBenignPop;
        _chaosOnDefuse = onDefuse;
        _chaosCanChannel = canChannelDefuse;
        _chaosOnChannelBroken = onChannelBroken;
        _chaosOnDetonate = onDetonate;
        ChaosMouseHeld = false;
        _chaosOnTreatExpired = onTreatExpired;
        _chaosOnEStimArc = onEStimArc;
        _estimChargesLeft = 0;
        _estimFork = false;
        _chaosOnDarterCaught = onDarterCaught;
        _chaosOnFreezeCaught = onFreezeCaught;
        ChaosInputLocked = false;
        VibePopOn = false;
        VibeHoverPops = false;
        VibeMouseHeld = false;
        _chaosActive = true;
        // Latch the shared-host A/B for the whole run and stand the host window up before the first
        // bubble spawns (it must exist for Bubble's spawn block to Add() the grid).
        _sharedHost = App.Settings?.Current?.ChaosBubbleSharedHost == true;
        if (_sharedHost) ChaosBubbleHostOverlay.EnsureCreated();
        DispatcherHelper.RunOnUI(() =>
        {
            if (_bubbleImage == null) LoadBubbleImage();
            EnsureAnimationTimer();
        });
    }

    /// <summary>Spawn one configured effect bubble (cadence owned by ChaosModeService).</summary>
    public void SpawnChaosBubble(EffectBubbleSpec spec)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            if (!_chaosActive) return;
            if (_bubbleImage == null) LoadBubbleImage();
            EnsureAnimationTimer();   // must run NOW so the tick is live to drain the queue
            _spawnQueue.Enqueue(() =>
            {
                try { _bubbles.Add(CreateChaosBubble(spec, PickScreenFor(spec))); }
                catch (Exception ex) { App.Logger?.Error("SpawnChaosBubble failed: {Error}", ex.Message); }
            });
        });
    }

    /// <summary>The Chaperone: materialise the live + its escort treat ON ONE SCREEN and link
    /// them — the escort orbits the live (shielding it) until one of the two resolves.</summary>
    public void SpawnChaosChaperone(EffectBubbleSpec liveSpec, EffectBubbleSpec escortSpec)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            if (!_chaosActive) return;
            if (_bubbleImage == null) LoadBubbleImage();
            EnsureAnimationTimer();   // must run NOW so the tick is live to drain the queue
            // The linked pair materialises as ONE thunk so the escort always lands on its orbit ring
            // against an existing live (never one frame apart).
            _spawnQueue.Enqueue(() =>
            {
            try
            {
                var screen = PickScreenFor(liveSpec);
                var live = CreateChaosBubble(liveSpec, screen);
                _bubbles.Add(live);
                // The escort materialises already ON its orbit ring (matching the radius the
                // orbit tick uses) — spawning at the live's centre flashed it behind the live.
                var lc = live.CenterPx;
                double dpi = Bubble.GetDpiForScreen(screen);
                double ringDip = Math.Max(ChaosTuning.CHAPERONE_ORBIT_RADIUS_DIP,
                    (liveSpec.SizePx + escortSpec.SizePx) / 2.0 + ChaosTuning.CHAPERONE_ORBIT_GAP_DIP);
                escortSpec.SpawnAtPxX = lc.X + ringDip * dpi;
                escortSpec.SpawnAtPxY = lc.Y;
                // Tighten the live's roam box so the full orbit stays on-screen: the escort
                // overhangs the live's centre by ring + its own radius.
                live.InsetRoamBounds(ringDip + Math.Max(60, escortSpec.SizePx) / 2.0
                                     - Math.Max(60, liveSpec.SizePx) / 2.0);
                var escort = CreateChaosBubble(escortSpec, screen);
                escort.AttachOrbit(live);
                live.AttachEscort(escort);
                _bubbles.Add(escort);
                // Stack the escort above its live: if the windows ever overlap (wand-enlarged
                // hitboxes), the click must land on the escort, not bounce off the shield.
                escort.BringToFront();
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SpawnChaosChaperone failed: {Error}", ex.Message);
            }
            });
        });
    }

    /// <summary>The Bound: materialise a tethered live pair ~250 DIP apart on ONE screen with
    /// loosely mirrored drift. The tether line + the enrage window run on the anim tick.</summary>
    public void SpawnChaosBoundPair(EffectBubbleSpec specA, EffectBubbleSpec specB)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            if (!_chaosActive) return;
            if (_bubbleImage == null) LoadBubbleImage();
            EnsureAnimationTimer();   // must run NOW so the tick is live to drain the queue
            // Both halves materialise as ONE thunk so the tether is never left dangling a frame.
            _spawnQueue.Enqueue(() =>
            {
            try
            {
                var screen = PickScreenFor(specA);
                double dpi = Bubble.GetDpiForScreen(screen);
                var wa = screen.WorkingArea;
                double sepPx = ChaosTuning.BOUND_SEPARATION_DIP * dpi;
                // Anchor with margins so both halves land comfortably on-screen.
                double mx = sepPx / 2 + 120;
                double ax = wa.X + mx + _random.NextDouble() * Math.Max(1, wa.Width - 2 * mx);
                double ay = wa.Y + 160 + _random.NextDouble() * Math.Max(1, wa.Height - 320);
                double ang = _random.NextDouble() * Math.PI;
                double ox = Math.Cos(ang) * sepPx / 2;
                double oy = Math.Sin(ang) * sepPx / 2 * 0.6;   // flatten the spread a little
                specA.SpawnAtPxX = ax - ox; specA.SpawnAtPxY = ay - oy;
                specB.SpawnAtPxX = ax + ox; specB.SpawnAtPxY = ay + oy;
                var a = CreateChaosBubble(specA, screen);
                _bubbles.Add(a);
                var b = CreateChaosBubble(specB, screen);
                b.MirrorVelocityFrom(a);   // loosely mirrored drift — they pull against the thread
                _bubbles.Add(b);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("SpawnChaosBoundPair failed: {Error}", ex.Message);
            }
            });
        });
    }

    /// <summary>Chaos bubbles always play on the HUD's screen — the one with the sidebar, boon
    /// ribbon and active-skill buttons. The HUD anchors to <see cref="SystemParameters.WorkArea"/>,
    /// i.e. the primary monitor, so the run stays on a single screen even when dual-monitor flashes
    /// are enabled (those scatter; the roguelite must not). A pinned spawn (Rabbit Caller's
    /// summon-at-click) still lands on whatever screen its point is on — which is this one anyway,
    /// since that's the only screen the player is clicking.</summary>
    private System.Windows.Forms.Screen PickScreenFor(EffectBubbleSpec spec)
    {
        return spec.SpawnAtPxX is double sax && spec.SpawnAtPxY is double say
            ? System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)sax, (int)say))
            : System.Windows.Forms.Screen.PrimaryScreen!;
    }

    /// <summary>Construct one chaos bubble with the full service callback set (UI thread).</summary>
    private Bubble CreateChaosBubble(EffectBubbleSpec spec, System.Windows.Forms.Screen screen)
    {
        return new Bubble(screen, _bubbleImage, _random, null, null, OnDestroy, isClickable: true,
                    spec: spec,
                    onBenignPop: b => { PlayPopSound(false); if (b.Spec != null) _chaosOnBenignPop?.Invoke(b.Spec); },
                    onDefuse:    b =>
                    {
                        // A completed channel DEFLATES with a soft hiss; instant defuses (toys,
                        // chains, zones) keep the sharp snap. Hiss falls back to snap until the
                        // asset ships (ChaosSfx.ResolvePath is empty for missing cues).
                        bool hiss = b.DefusedViaChannel && Chaos.ChaosSfx.ResolvePath("defuse_hiss").Length > 0;
                        PlayChaosCue(hiss ? "defuse_hiss" : "snap", 0.6f);
                        if (b.Spec != null) _chaosOnDefuse?.Invoke(b.Spec, b.DefuseJudgeFuseSec, b.DefusedViaChannel);
                        if (b.Spec?.IsBoundHalf == true) OnBoundHalfResolved(b, defused: true);
                    },
                    canChannelDefuse: b => b.Spec == null || (_chaosCanChannel?.Invoke(b.Spec) ?? true),
                    onChannelBroken: (b, reason) => { if (b.Spec != null) _chaosOnChannelBroken?.Invoke(b.Spec, reason); },
                    onDetonate:  b =>
                    {
                        if (b.Spec != null) _chaosOnDetonate?.Invoke(b.Spec);
                        if (b.Spec?.IsBoundHalf == true) OnBoundHalfResolved(b, defused: false);
                    },
                    onDarterCaught: b => { PlayChaosCue("rabbit_catch", 0.6f); if (b.Spec != null) _chaosOnDarterCaught?.Invoke(b.Spec, b.WasQuickCatch); },
                    onFreezeCaught: b => { PlayChaosCue("freeze_catch", 0.6f); if (b.Spec != null) _chaosOnFreezeCaught?.Invoke(b.Spec); },
                    isChaosFrozen: () => _chaosFrozen,
                    timeScale: () => _chaosTimeScale,
                    freezeVibrateMs: () => _freezeVibrateRemainingMs,
                    onChainTrigger: ChainPopNeighbors,
                    hitboxScale: _chaosHitboxScale?.Invoke() ?? 1.0,
                    liveMagnet: _chaosLiveMagnet?.Invoke() ?? false,
                    opacityMult: _chaosBubbleOpacity?.Invoke() ?? 1.0,
                    onSpankSweep: SpankSweepFromDarter,
                    onTreatExpired: b => { if (b.Spec != null) _chaosOnTreatExpired?.Invoke(b.Spec); },
                    onClickPop: OnChaosBubbleClicked,
                    onTeaseTouched: b => { if (b.Spec != null) _chaosOnTeaseTouched?.Invoke(b.Spec); },
                    onTeaseDenied: b => { if (b.Spec != null) _chaosOnTeaseDenied?.Invoke(b.Spec); },
                    onBrittleShattered: b => { if (b.Spec != null) _chaosOnBrittleShattered?.Invoke(b.Spec); });
    }

    // ======================= chaos field hazards (run-boon FX) =======================
    // Size Queen ripples, Aftermath residue and Tail-Plug trails all pop bubbles from the
    // shared anim tick — pure px-space distance checks against tiny lists, no extra timers.

    private const double RIPPLE_RADIUS_PX = 430;    // Size Queen: full ring reach
    private const double RIPPLE_LIFE_MS = 550;      // expansion time (matches the drawn ring)
    private const double RESIDUE_RADIUS_PX = 170;   // Aftermath: crackle zone reach
    private const double RESIDUE_LIFE_MS = 2000;
    private const double TRAIL_POP_RADIUS_PX = 46;  // Tail-Plug: per-point brush reach

    private readonly List<(Point CenterPx, double AgeMs)> _ripples = new();
    private readonly List<(Point CenterPx, DateTime Until)> _residues = new();

    /// <summary>the Ripple (right-click): one expanding player wavefront per cast wave. Each
    /// bubble is judged exactly once as the front crosses it (the Hit set), so a slow capstone
    /// wave can't re-fling the same rabbit every tick.</summary>
    private sealed class PlayerRipple
    {
        public Point CenterPx;
        public double AgeMs;
        public double RadiusPx;
        public double LifeMs;
        public readonly HashSet<Bubble> Hit = new();
    }
    private readonly List<PlayerRipple> _playerRipples = new();

    /// <summary>the Ripple: the player's right-click wave — treats pop PAID, lives snap clean,
    /// rabbits get flung (their flying body mows bubbles). Drawn by the field-FX overlay;
    /// the popping happens on the anim tick like every other field hazard.</summary>
    public void TriggerPlayerRipple(Point centerPx, double radiusPx, double lifeMs)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            _playerRipples.Add(new PlayerRipple { CenterPx = centerPx, RadiusPx = radiusPx, LifeMs = Math.Max(100, lifeMs) });
            if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Ripple(centerPx, radiusPx, lifeMs, strong: true);
            else ChaosFieldFxOverlay.SnapRipple(centerPx, radiusPx, lifeMs);
        });
    }

    /// <summary>Size Queen: an expanding ring from a snapped live bubble — pops every treat it
    /// touches as it grows. Draws via the field-FX overlay; popping happens on the anim tick.</summary>
    public void TriggerChaosRipple(Point centerPx)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            _ripples.Add((centerPx, 0));
            if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Ripple(centerPx, RIPPLE_RADIUS_PX, RIPPLE_LIFE_MS, strong: false);
            else ChaosFieldFxOverlay.Ripple(centerPx, RIPPLE_RADIUS_PX, RIPPLE_LIFE_MS);
        });
    }

    /// <summary>Aftermath: a 2s crackling residue zone at a brink-snap — bubbles drifting
    /// through pop themselves (treats pop, live ones snap; rabbits/freeze immune).</summary>
    public void AddChaosResidue(Point centerPx)
    {
        if (!_chaosActive) return;
        DispatcherHelper.RunOnUI(() =>
        {
            _residues.Add((centerPx, DateTime.UtcNow.AddMilliseconds(RESIDUE_LIFE_MS)));
            ChaosFieldFxOverlay.Residue(centerPx, RESIDUE_RADIUS_PX, RESIDUE_LIFE_MS);
        });
    }

    /// <summary>Advance ripples/residue/trails one anim tick and pop whatever they reach.</summary>
    private void TickFieldHazards()
    {
        if (!_chaosActive || _chaosFrozen) return;
        if (_ripples.Count == 0 && _residues.Count == 0 && _playerRipples.Count == 0
            && ChaosRabbitTrailSecNow <= 0) return;

        static double DistSq(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }
        var snapshot = _bubbles.ToArray();

        // Size Queen ripples: only treats pop (the ring is a reward wave, never a threat trigger).
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            var (c, age) = _ripples[i];
            age += 32;
            double r = RIPPLE_RADIUS_PX * Math.Min(1.0, age / RIPPLE_LIFE_MS);
            foreach (var b in snapshot)
            {
                if (!b.IsAlive || b.Spec == null) continue;
                if (b.Spec.IsLive || b.Spec.IsDarter || b.Spec.IsFreeze) continue;
                if (DistSq(b.CenterPx, c) <= r * r) b.Pop();
            }
            if (age >= RIPPLE_LIFE_MS) _ripples.RemoveAt(i);
            else _ripples[i] = (c, age);
        }

        // the Ripple (right-click): the player's wavefront. Treats pop PAID and lives snap
        // clean through their normal Pop() routing; rabbits are FLUNG onward instead (their
        // body mows bubbles from then on — Spank physics, no Spanker needed). The Tease, the
        // Brittle and freeze pickups stay cursor-only, same as every other auto-sweep.
        for (int i = _playerRipples.Count - 1; i >= 0; i--)
        {
            var pr = _playerRipples[i];
            pr.AgeMs += 32;
            double r = pr.RadiusPx * Math.Min(1.0, pr.AgeMs / pr.LifeMs);
            foreach (var b in snapshot)
            {
                if (!b.IsAlive || b.Spec == null || pr.Hit.Contains(b)) continue;
                if (b.Spec.IsFreeze || b.Spec.IsTease || b.Spec.IsBrittle) continue;
                if (DistSq(b.CenterPx, pr.CenterPx) > r * r) continue;
                pr.Hit.Add(b);
                if (b.Spec.IsDarter) b.FlingFrom(pr.CenterPx);
                else b.Pop();
            }
            if (pr.AgeMs >= pr.LifeMs) _playerRipples.RemoveAt(i);
        }

        // Aftermath residue: anything drifting through pops (treats fire, live ones snap),
        // with a little E-Stim crackle from the zone to each victim.
        var now = DateTime.UtcNow;
        for (int i = _residues.Count - 1; i >= 0; i--)
        {
            var (c, until) = _residues[i];
            if (now >= until) { _residues.RemoveAt(i); continue; }
            foreach (var b in snapshot)
            {
                if (!b.IsAlive || b.Spec == null) continue;
                if (b.Spec.IsDarter || b.Spec.IsFreeze) continue;
                if (DistSq(b.CenterPx, c) <= RESIDUE_RADIUS_PX * RESIDUE_RADIUS_PX)
                {
                    if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Strike(new[] { (c, b.CenterPx) });
                    else ChaosEStimOverlay.Strike(new[] { (c, b.CenterPx) });
                    b.Pop();
                }
            }
        }

        // Tail-Plug: every rabbit's recorded trail brushes treats AND live bubbles open.
        if (ChaosRabbitTrailSecNow > 0)
        {
            foreach (var darter in snapshot)
            {
                if (darter.Spec?.IsDarter != true || darter.TrailPoints.Count == 0) continue;
                foreach (var b in snapshot)
                {
                    if (!b.IsAlive || b.Spec == null || ReferenceEquals(b, darter)) continue;
                    if (b.Spec.IsDarter || b.Spec.IsFreeze) continue;
                    foreach (var (px, _) in darter.TrailPoints)
                    {
                        if (DistSq(b.CenterPx, px) <= TRAIL_POP_RADIUS_PX * TRAIL_POP_RADIUS_PX)
                        { b.Pop(); break; }
                    }
                }
            }
        }
    }

    // ======================= The Bound (tethered live pairs) =======================
    // First half defused → a window opens; the second half must COMPLETE inside it or the
    // survivor enrages (trance halves, +40% speed). One half TRIGGERING enrages the other
    // instantly. Toy/chain defuses count as completions (a sweep catching both clears clean).

    private readonly Dictionary<int, DateTime> _boundFirstResolved = new();   // pairId → first-defuse time
    private readonly HashSet<int> _boundTetherKeys = new();                   // tether lines currently drawn
    private readonly Dictionary<int, Bubble> _boundScan = new();              // per-tick scratch
    private Action<EffectBubbleSpec>? _chaosOnBoundEnraged;

    /// <summary>The partner half of <paramref name="except"/>'s pair, if it's still standing.</summary>
    private Bubble? FindBoundPartner(int pairId, Bubble except)
    {
        foreach (var b in _bubbles)
            if (!ReferenceEquals(b, except) && b.IsAlive && !b.IsPopping
                && b.Spec?.IsBoundHalf == true && b.Spec.PairId == pairId)
                return b;
        return null;
    }

    /// <summary>One half of a bound pair just resolved (from the defuse/detonate wrappers).</summary>
    private void OnBoundHalfResolved(Bubble half, bool defused)
    {
        int pairId = half.Spec!.PairId;
        var partner = FindBoundPartner(pairId, half);
        if (partner == null)
        {
            // Pair complete (second inside the window, or after an enrage) — clean the books.
            _boundFirstResolved.Remove(pairId);
            if (_boundTetherKeys.Remove(pairId)) ChaosFieldFxOverlay.ClearTether(pairId);
            // Verb hint: the pair cleared by the player's own hold — the bound lesson is learned.
            if (defused && half.DefusedViaChannel) Chaos.ChaosBubbleHints.MarkLearned("bound");
            return;
        }
        if (!defused)
        {
            // One half triggered: the survivor enrages on the spot.
            _boundFirstResolved.Remove(pairId);
            if (_boundTetherKeys.Remove(pairId)) ChaosFieldFxOverlay.ClearTether(pairId);
            partner.Enrage();
            if (partner.Spec != null) _chaosOnBoundEnraged?.Invoke(partner.Spec);
            return;
        }
        // First defuse of the pair: the window opens.
        if (!_boundFirstResolved.ContainsKey(pairId)) _boundFirstResolved[pairId] = DateTime.UtcNow;
    }

    /// <summary>Per anim tick: redraw tethers for intact pairs; enrage lone survivors whose
    /// window lapsed. The window holds its breath while the field is frozen.</summary>
    private void TickBoundPairs()
    {
        if (!_chaosActive) return;
        // A freeze (power-up or manual pause) holds the enrage CLOCK, not just the check —
        // shift every open window's start forward so frozen seconds never count against the
        // survivor. Without this, a 3.5s freeze would expire the window the instant it ends.
        if (_chaosFrozen && _boundFirstResolved.Count > 0)
        {
            foreach (var key in new List<int>(_boundFirstResolved.Keys))
                _boundFirstResolved[key] = _boundFirstResolved[key].AddMilliseconds(32);
        }
        if (_boundFirstResolved.Count == 0 && _boundTetherKeys.Count == 0)
        {
            // Cheap pre-check: anything bound on the field at all?
            bool any = false;
            foreach (var b in _bubbles)
                if (b.Spec?.IsBoundHalf == true) { any = true; break; }
            if (!any) return;
        }

        _boundScan.Clear();
        foreach (var b in _bubbles)
        {
            if (b.Spec?.IsBoundHalf != true || !b.IsAlive || b.IsPopping) continue;
            int id = b.Spec.PairId;
            if (_boundScan.TryGetValue(id, out var first))
            {
                ChaosFieldFxOverlay.SetTether(id, first.CenterPx, b.CenterPx);
                _boundTetherKeys.Add(id);
                _boundScan.Remove(id);   // drawn — anything left over after the loop is a lone half
            }
            else _boundScan[id] = b;
        }
        foreach (var kv in _boundScan)
        {
            if (_boundTetherKeys.Remove(kv.Key)) ChaosFieldFxOverlay.ClearTether(kv.Key);
            if (!_chaosFrozen && _boundFirstResolved.TryGetValue(kv.Key, out var t)
                && (DateTime.UtcNow - t).TotalMilliseconds > ChaosTuning.BOUND_WINDOW_MS)
            {
                _boundFirstResolved.Remove(kv.Key);
                kv.Value.Enrage();
                if (kv.Value.Spec != null) _chaosOnBoundEnraged?.Invoke(kv.Value.Spec);
            }
        }
    }

    /// <summary>A verb hint was just learned (ChaosBubbleHints): strip its pill from every
    /// bubble currently on the field so the whole screen un-clutters in the same instant.</summary>
    public void HideChaosHints(string key)
    {
        try
        {
            foreach (var b in _bubbles)
                if (b.HintKey == key) b.HideHint();
        }
        catch { }
    }

    /// <summary>Drop every tether + window (field cleared / run over).</summary>
    private void ClearBoundState()
    {
        foreach (var key in _boundTetherKeys) ChaosFieldFxOverlay.ClearTether(key);
        _boundTetherKeys.Clear();
        _boundFirstResolved.Clear();
    }

    /// <summary>A spanked rabbit's body mows plain bubbles (live ones snap, treats pop). Other
    /// darters and freeze pickups are immune. Per-frame from the darter's AnimateFrame.
    /// Electrified Rabbits: each victim also discharges free E-Stim arcs into its neighbours.</summary>
    private void SpankSweepFromDarter(Bubble darter)
    {
        if (!_chaosActive) return;
        var reach = darter.SpankReach;
        foreach (var b in _bubbles.ToArray())
        {
            if (!b.IsAlive || b.Spec == null || ReferenceEquals(b, darter)) continue;
            if (b.Spec.IsDarter || b.Spec.IsFreeze) continue;
            if (reach.IntersectsWith(b.BoundingBox))
            {
                var victimPx = b.CenterPx;
                b.Pop();
                if (ChaosElectrifiedNow) EStimBurstAt(victimPx, ESTIM_ARCS_PER_POP);
            }
        }
    }

    /// <summary>True when any live darter's body overlaps <paramref name="rectDips"/> — the
    /// Intrusive Thoughts capstone's split trigger (read-only; the rabbit is unharmed).</summary>
    public bool AnyDarterIntersects(Rect rectDips)
    {
        if (!_chaosActive) return false;
        foreach (var b in _bubbles)
        {
            if (!b.IsAlive || b.Spec == null || !b.Spec.IsDarter) continue;
            if (rectDips.IntersectsWith(b.BoundingBox)) return true;
        }
        return false;
    }

    /// <summary>Stagger between chain hops (ms) so a cluster ripples outward instead of vanishing in one frame.</summary>
    private const int ChainHopMs = 80;

    /// <summary>
    /// Chain Reaction boon: when a chaos bubble pops, its expanding burst pops any neighbour whose
    /// box overlaps the burst, rippling through a cluster. Each chained pop re-enters here (via the
    /// same <c>onChainTrigger</c> hook), so the cascade is self-propagating; the per-bubble
    /// <c>_isPopping</c> guard stops anything popping twice and ends the chain at the cluster edge.
    /// Darters chain like everything else — a chained darter counts as a catch (slow-mo fires)
    /// and its big burst seeds the next hop. The reach is the boon's level-driven box-multiple
    /// (<=1 = off). Runs on the UI thread (from <see cref="Bubble.Pop"/>).
    /// </summary>
    private void ChainPopNeighbors(Bubble source)
    {
        try
        {
            double reachMult = _chaosChainReach?.Invoke() ?? 0;
            if (reachMult <= 1.0) return;
            if (source.Spec == null) return;
            var reach = source.ChainReach(reachMult);
            foreach (var b in _bubbles.ToArray())
            {
                if (ReferenceEquals(b, source) || !b.IsChainable) continue;
                if (!reach.IntersectsWith(b.BoundingBox)) continue;
                var target = b;
                var hop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ChainHopMs) };
                hop.Tick += (_, _) =>
                {
                    hop.Stop();
                    try { if (target.IsAlive) target.Pop(); } catch { }
                };
                hop.Start();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChainPopNeighbors: {E}", ex.Message); }
    }

    /// <summary>
    /// Lift every live bubble back to the top of the topmost band (focus-stealing free).
    /// Called when a fullscreen window — the mandatory chaos video — is raised mid-run so the
    /// player keeps popping bubbles ABOVE the video instead of having them buried under it.
    /// No-op when no bubbles are present (e.g. the ambient game was paused-and-cleared).
    /// </summary>
    public void BringAllToFront()
    {
        DispatcherHelper.RunOnUI(() =>
        {
            foreach (var b in _bubbles.ToArray())
                b.BringToFront();
        });
    }

    /// <summary>Leave chaos mode: clear effect bubbles + callbacks.</summary>
    public void EndChaosMode()
    {
        _chaosActive = false;
        _spawnQueue.Clear();   // drop any bubbles queued but not yet materialised this run
        // Tear down the shared host (idempotent; its Canvas is cleared on close). Per-bubble Destroy
        // also removes each grid, but live bubbles may still be clearing — CloseActive covers both.
        if (_sharedHost) { ChaosBubbleHostOverlay.CloseActive(); _sharedHost = false; }
        ChaosClickDiscsSnapshot = Array.Empty<(double, double, double, bool)>();
        _chaosFrozen = false;
        _freezeVibrateRemainingMs = 0;
        _chaosTimeScale = 1.0;
        _chaosOnBenignPop = _chaosOnDetonate = null;
        _chaosCanChannel = null;
        _chaosOnChannelBroken = null;
        ChaosMouseHeld = false;
        _chaosOnTreatExpired = null;
        _chaosOnTeaseTouched = null;
        _chaosOnTeaseDenied = null;
        _chaosOnBoundEnraged = null;
        _chaosOnBrittleShattered = null;
        ClearBoundState();
        _chaosOnEStimArc = null;
        _estimChargesLeft = 0;
        _estimFork = false;
        _chaosOnDefuse = null;
        _chaosOnDarterCaught = null;
        _chaosOnFreezeCaught = null;
        _chaosChainReach = null;
        _chaosHitboxScale = null;
        _chaosBubbleOpacity = null;
        _chaosWandShimmer = null;
        _chaosCursorPull = null;
        _chaosRabbitHoming = null;
        _chaosSpankerOn = null;
        _chaosSpankGrow = null;
        _chaosLiveMagnet = null;
        _chaosRabbitTrailSec = null;
        _chaosElectrified = null;
        ChaosCursorPullNow = 0; ChaosRabbitHomingNow = false; ChaosSpankerOnNow = false; ChaosSpankGrowNow = 1.0;
        ChaosRabbitTrailSecNow = 0;
        ChaosElectrifiedNow = false;
        _ripples.Clear();
        _residues.Clear();
        _playerRipples.Clear();
        ChaosBubbleCentersSnapshot = Array.Empty<Point>();
        WandShimmerOn = false;
        VibePopOn = false;
        VibeHoverPops = false;
        VibeMouseHeld = false;
        ChaosInputLocked = false;
        PopAllBubbles();
        DispatcherHelper.RunOnUI(StopAnimationTimerIfIdle);
    }

    /// <summary>
    /// Smallest remaining fuse (in seconds) across live, un-popped chaos bubbles, or null when
    /// nothing is armed. Polled by the Blindfold capstone heartbeat (UI thread, ~4x/s, tiny list).
    /// </summary>
    public double? MinChaosFuseSec()
    {
        double best = double.MaxValue;
        foreach (var b in _bubbles)
        {
            var ms = b.LiveFuseRemainingMs;
            if (ms.HasValue && ms.Value < best) best = ms.Value;
        }
        return best == double.MaxValue ? null : best / 1000.0;
    }

    /// <summary>True while the cursor rests on an armed live chaos bubble. Polled by
    /// ChaosModeService's RunTick (UI thread, 4x/s) for the focus-bar hover cue —
    /// the cursor sample is the same per-tick one every bubble already reads.</summary>
    public bool IsCursorOverLiveChaosBubble()
    {
        if (!_chaosActive) return false;
        foreach (var b in _bubbles)
            if (b.LiveFuseRemainingMs.HasValue && b.CursorInside()) return true;
        return false;
    }

    /// <summary>Soft chime one-shot (reuses the lucky-pop chime files). Tunnel Vision capstone spawn cue.</summary>
    public void PlayChime(float volumeScale = 0.3f)
    {
        try
        {
            var chimeFiles = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };
            var chimePath = ModResourceResolver.ResolveAudioPath(chimeFiles[_random.Next(chimeFiles.Length)]);
            if (!File.Exists(chimePath)) return;
            var masterVolume = App.Settings.Current.MasterVolume / 100f;
            var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
            PlaySoundAsync(chimePath, (float)Math.Pow(masterVolume * bubblesVolume, 1.5) * volumeScale);
        }
        catch (Exception ex) { App.Logger?.Debug("PlayChime: {E}", ex.Message); }
    }

    /// <summary>Chaos bubble-outcome cue: the dedicated chaos SFX when shipped, else the ambient pop.</summary>
    private void PlayChaosCue(string name, float volumeScale)
    {
        var path = Chaos.ChaosSfx.ResolvePath(name);
        if (path.Length > 0) PlayCue(path, volumeScale);
        else PlayPopSound(false);
    }

    /// <summary>Play an arbitrary one-shot cue (absolute path) through the pooled audio devices.
    /// Silent no-op when the file is missing — new chaos cues ship code-first, assets later.</summary>
    public void PlayCue(string absolutePath, float volumeScale = 0.5f)
    {
        try
        {
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) return;
            var masterVolume = App.Settings.Current.MasterVolume / 100f;
            var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
            PlaySoundAsync(absolutePath, (float)Math.Pow(masterVolume * bubblesVolume, 1.5) * volumeScale);
        }
        catch (Exception ex) { App.Logger?.Debug("PlayCue: {E}", ex.Message); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Win32CursorPoint { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32CursorPoint pt);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;   // right button is the comfy sweep choice (no stray desktop clicks)

    /// <summary>Ensure the shared 30fps animation timer is running (used by chaos + SpawnOnce).</summary>
    private void EnsureAnimationTimer()
    {
        if (_animationTimer == null || !_animationTimer.IsEnabled)
        {
            _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(32)
            };
            _animationTimer.Tick += AnimateAllBubbles;
            _animationTimer.Start();
        }
    }

    private void PlayPopSound(bool isLucky = false, float volumeMult = 1f)
    {
        try
        {
            // If lucky bubble, play a random chime sound
            if (isLucky)
            {
                var chimeFiles = new[] { "chime1.mp3", "chime2.mp3", "chime3.mp3" };
                var chimePath = ModResourceResolver.ResolveAudioPath(chimeFiles[_random.Next(chimeFiles.Length)]);
                if (File.Exists(chimePath))
                {
                    var masterVolume = App.Settings.Current.MasterVolume / 100f;
                    var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
                    var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5) * 0.35f * volumeMult;
                    PlaySoundAsync(chimePath, Math.Min(volume, 1f));
                    App.Logger?.Information("🎉 Lucky Bubble! 20x XP!");
                    return;
                }
            }

            // Normal pop sound
            var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
            var chosenPop = popFiles[_random.Next(popFiles.Length)];
            var popPath = ModResourceResolver.ResolveAudioPath("bubbles/" + chosenPop);

            if (File.Exists(popPath))
            {
                var masterVolume = App.Settings.Current.MasterVolume / 100f;
                var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
                var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5) * volumeMult;

                PlaySoundAsync(popPath, Math.Min(volume, 1f));
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to play pop sound: {Error}", ex.Message);
        }
    }

    // Performance: Pool of audio devices to avoid creating new ones for each sound
    private static readonly Queue<WaveOutEvent> _audioDevicePool = new();
    private static readonly object _audioPoolLock = new();
    private const int MAX_POOLED_DEVICES = 4;

    private WaveOutEvent GetPooledAudioDevice()
    {
        lock (_audioPoolLock)
        {
            if (_audioDevicePool.Count > 0)
            {
                return _audioDevicePool.Dequeue();
            }
        }
        // Apply user's chosen output device on construction. Pool is drained when the
        // setting changes (see DrainAudioDevicePool) so we never need to reapply on Get.
        var w = new WaveOutEvent();
        App.Audio?.ApplyPreferredDevice(w);
        return w;
    }

    /// <summary>
    /// Disposes all pooled audio devices. Call after the user changes the output device
    /// setting so the next pop-sound playback re-creates devices on the new endpoint
    /// (DeviceNumber can't be changed once Init() has been called).
    /// </summary>
    public static void DrainAudioDevicePool()
    {
        lock (_audioPoolLock)
        {
            while (_audioDevicePool.Count > 0)
            {
                try { _audioDevicePool.Dequeue().Dispose(); } catch { }
            }
        }
    }

    private void ReturnAudioDevice(WaveOutEvent device)
    {
        lock (_audioPoolLock)
        {
            if (_audioDevicePool.Count < MAX_POOLED_DEVICES)
            {
                _audioDevicePool.Enqueue(device);
            }
            else
            {
                device.Dispose();
            }
        }
    }

    private void PlaySoundAsync(string path, float volume)
    {
        Task.Run(() =>
        {
            WaveOutEvent? outputDevice = null;
            AudioFileReader? audioFile = null;
            try
            {
                audioFile = new AudioFileReader(path);
                audioFile.Volume = volume;

                outputDevice = GetPooledAudioDevice();  // Performance: Reuse pooled device
                outputDevice.Init(audioFile);
                outputDevice.Play();

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Audio playback failed: {Error}", ex.Message);
            }
            finally
            {
                audioFile?.Dispose();
                if (outputDevice != null)
                {
                    try { outputDevice.Stop(); } catch { }
                    ReturnAudioDevice(outputDevice);  // Performance: Return to pool
                }
            }
        });
    }

    public void PopAllBubbles()
    {
        // Cancel any in-flight avatar easter egg first, so its claim is released and the claimed
        // bubble doesn't get stranded by the claim-pop guard during teardown.
        CancelAvatarEgg();
        try
        {
            // Safety check for shutdown scenarios
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                // Direct cleanup without dispatcher - force destroy
                foreach (var bubble in _bubbles.ToArray())
                {
                    try { bubble.ForceDestroy(); } catch { }
                }
                _bubbles.Clear();
                return;
            }

            // Take a copy of bubbles to close
            var bubblesToClose = _bubbles.ToArray();
            _bubbles.Clear();

            // Close on UI thread - use Invoke for synchronous cleanup during stop
            // Since animation timer is stopped, we need to force destroy (no animation)
            dispatcher.Invoke(() =>
            {
                foreach (var bubble in bubblesToClose)
                {
                    try
                    {
                        bubble.ForceDestroy();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Error destroying bubble: {Error}", ex.Message);
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Send); // High priority to complete quickly
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("PopAllBubbles error during shutdown: {Error}", ex.Message);
            // Force clear the list even if popup failed
            _bubbles.Clear();
        }
    }

    public void Dispose()
    {
        Stop();

        // Close pooled bubble window shells (static pool holds hidden HWNDs for the process life).
        try { DispatcherHelper.RunOnUI(Bubble.DrainWindowPool); } catch { }

        // Drain and dispose pooled audio devices (static pool persists across service restarts)
        lock (_audioPoolLock)
        {
            while (_audioDevicePool.Count > 0)
            {
                try { _audioDevicePool.Dequeue().Dispose(); } catch { }
            }
        }
    }
}

/// <summary>
/// Individual bubble that floats upward and can be popped
/// </summary>
internal class Bubble
{
    // Bubble windows are POOLED, not created/closed per bubble. Each bubble used to be its own
    // AllowsTransparency layered Window that was Close()d on death; chaos spawns several per
    // second, and closing hundreds of layered windows per minute floods the WPF finalizer queue
    // faster than it drains, so native HWND/effect/bitmap memory piled up to a 2GB+ OOM hard
    // crash (no managed exception — see chaos-bubble-oom-leak). Reusing a bounded set of hidden
    // window shells eliminates the create/close churn entirely: the expensive native surface is
    // allocated once per slot and recycled. A single full-screen canvas host was rejected because
    // it can't do click-through-except-on-bubbles without a global mouse hook.
    private const int WINDOW_POOL_MAX = 64;
    // Size-aware pool: one stack per quantized square side (128, 256, 384, …). A single shared stack meant
    // renting a shell last sized for a DIFFERENT bucket, forcing a layered-window resize — HwndTarget.OnResize
    // -> synchronous MediaContext.CompleteRender, which wedges the UI thread under a backed-up render thread
    // (the #494 mechanism), on top of the native DIB realloc. Keying by size makes every rental an exact
    // match, so a bubble window is never resized after creation.
    private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Stack<Window>> _windowPool = new();
    private static int _windowPoolCount;   // total idle shells across all buckets, for the WINDOW_POOL_MAX cap

    // Shared frozen near-invisible hit brush — identical on every bubble, so one frozen instance the
    // render thread realizes once beats a fresh SolidColorBrush per spawn (alloc + per-instance realize).
    private static readonly SolidColorBrush s_hitBrush = NewFrozenBrush(Color.FromArgb(1, 0, 0, 0));
    private static SolidColorBrush NewFrozenBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    // Per-screen DPI cache — GetDpiForScreen was a Win32 round-trip (MonitorFromPoint + GetDpiForMonitor)
    // on every spawn; the value never changes for a given monitor during a run.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> s_dpiCache = new();

    /// <summary>Set by BubbleService while the ambient dashboard shared host is standing (Start->Stop).
    /// Lets an ambient bubble (spec == null) opt into shared-host rendering the same way a chaos bubble
    /// does, but driven by the AppSettings.BubbleSharedHost flag instead of the chaos one.</summary>
    internal static bool AmbientHostActive;

    /// <summary>A hidden, reset transparent window shell of exactly <paramref name="bucket"/> px square —
    /// recycled from that bucket or freshly built at that size. Never resized after creation. UI thread only.</summary>
    private static Window RentWindow(int bucket)
    {
        if (_windowPool.TryGetValue(bucket, out var stack))
        {
            while (stack.Count > 0)
            {
                var w = stack.Pop();
                _windowPoolCount--;
                if (w != null) return w;
            }
        }
        return new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null,   // content-only hit-testing (the centred grid); the quantized window margin passes clicks through. See spawn block.
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = bucket,
            Height = bucket,   // sized once, here — the resize that used to happen per-spawn is gone
        };
    }

    /// <summary>Reset a dead bubble's window to a bare hidden shell and return it to its size bucket
    /// (or Close it if the pool is full). Drops Content/Effect so the visual tree + bitmaps release
    /// immediately and nothing roots the old bubble. <paramref name="bucket"/> is the window's square
    /// side (its _winDim) — the shell is never resized, so it stays in exactly this bucket. UI thread only.</summary>
    private static void ReturnWindow(Window? w, int bucket)
    {
        if (w == null) return;
        // Restore the topmost default: a Free Desktop chaos bubble may have set this false, and the
        // pool is shared with ambient bubbles that must always ride on top.
        try { w.Effect = null; w.Content = null; w.Opacity = 0; w.Topmost = true; w.Hide(); } catch { }
        if (_windowPoolCount < WINDOW_POOL_MAX)
        {
            if (!_windowPool.TryGetValue(bucket, out var stack))
            {
                stack = new System.Collections.Generic.Stack<Window>();
                _windowPool[bucket] = stack;
            }
            stack.Push(w);
            _windowPoolCount++;
        }
        else { try { w.Close(); } catch { } }
    }

    /// <summary>Close every pooled shell (service Dispose / app shutdown) — the pool is static
    /// and would otherwise hold its hidden HWNDs for the process lifetime.</summary>
    public static void DrainWindowPool()
    {
        foreach (var stack in _windowPool.Values)
        {
            while (stack.Count > 0)
            {
                try { stack.Pop()?.Close(); } catch { }
            }
        }
        _windowPool.Clear();
        _windowPoolCount = 0;
    }

    private readonly Window? _window;   // null in shared-host mode (the grid lives on ChaosBubbleHostOverlay)
    private readonly bool _useHost;     // AppSettings.ChaosBubbleSharedHost && chaos bubble — see the spawn block
    private readonly FrameworkElement _fxTarget;   // where glow/opacity apply: _window (per-window) or _grid (host)
    private double _winDim;   // the (quantized) square window side this bubble uses; held so AnimateFrame can re-centre without resizing the window (see spawn — resizing churns the layered DIB)
    private System.Windows.Input.MouseButtonEventHandler? _winClickHandler;   // removed on death so the pooled window never roots a dead bubble
    private readonly Random _random;
    private readonly Action<Bubble>? _onPop;
    private readonly Action<Bubble>? _onMiss;
    private readonly Action<Bubble>? _onDestroy;
    private readonly bool _isClickable;

    private double _posX, _posY;
    private double _startX;
    private double _startY;   // SideDrift: the entry height the vertical wobble plays around
    private double _speed;
    private double _timeAlive;
    private double _wobbleOffset;
    private double _angle;
    private double _scale = 1.0;
    private double _fadeAlpha = 1.0;
    private int _animType;
    private bool _isPopping;
    private bool _isAlive = true;
    private bool _isDestroyed = false;
    private bool _isLucky;
    private double _gazeDwellScale = 1.0; // multiplied into render scale during Focus Gaze dwell

    private readonly Image _bubbleImage;
    private readonly int _size;

    // Glassy specular highlight — a soft white blob in the upper-left, the classic glass-marble
    // shine. Shared frozen brush (relative coords, so size-independent); applied to plain round
    // bubbles only (variant sprites like rabbits/golden carry their own art). Gated on the Skia FX flag.
    private static readonly Brush _shineBrush = BuildShineBrush();
    private static Brush BuildShineBrush()
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.34, 0.27),
            Center = new Point(0.34, 0.27),
            RadiusX = 0.32,
            RadiusY = 0.32,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(190, 255, 255, 255), 0.0),
                new GradientStop(Color.FromArgb(70, 255, 255, 255), 0.5),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0),
            }
        };
        b.Freeze();
        return b;
    }

    // Freeze-aura halo — constant icy-blue radial, hidden (opacity 0) on every bubble until a field
    // freeze pulses it. The colour/stops never vary, so share ONE frozen brush across all bubbles
    // (only the per-bubble Ellipse size differs). Saves a RadialGradientBrush + 3 GradientStops per spawn.
    private static readonly Brush _freezeAuraBrush = BuildFreezeAuraBrush();
    private static Brush BuildFreezeAuraBrush()
    {
        var c = Color.FromRgb(150, 210, 255);
        var b = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0,   c.R, c.G, c.B), 0.30));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(190, c.R, c.G, c.B), 0.66));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0,   c.R, c.G, c.B), 1.0));
        b.Freeze();
        return b;
    }

    // Label-glyph drop shadow — constant soft black shadow shared by every labelled bubble's ✖/emoji.
    private static readonly DropShadowEffect _labelShadow = BuildLabelShadow();
    private static DropShadowEffect BuildLabelShadow()
    {
        var e = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8 };
        e.Freeze();
        return e;
    }

    // The Tease's glossy diagonal shine — constant white→transparent linear gradient (the Ellipse size
    // varies, the brush doesn't). Shared frozen; the per-frame shimmer animates the Ellipse Opacity, not the brush.
    private static readonly Brush _teaseShineBrush = BuildTeaseShineBrush();
    private static Brush BuildTeaseShineBrush()
    {
        var b = new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 35);
        b.Freeze();
        return b;
    }
    private double _screenTop;   // mutable: InsetRoamBounds tightens it for chaperone lives
    private readonly Canvas _sparkleCanvas;
    private readonly Grid _grid;
    private List<SparkleParticle>? _sparkles;

    // ---- Chaos effect-bubble extensions (null/inert for ambient bubbles) ----
    private readonly EffectBubbleSpec? _spec;
    private readonly Action<Bubble>? _onBenignPop;
    private readonly Action<Bubble>? _onDefuse;
    private readonly Action<Bubble>? _onDetonate;
    private readonly Action<Bubble>? _onDarterCaught;
    private readonly Action<Bubble>? _onFreezeCaught;
    private readonly Action<Bubble>? _onChainTrigger;   // Chain Reaction boon: fired on pop so the service can sweep neighbours
    private readonly Action<Bubble>? _onSpankSweep;     // The Spanker: fired per frame while spanked so the service can mow bubbles
    private readonly Action<Bubble>? _onTreatExpired;   // treat ran out its screen time → dissolved unpopped (streak cost service-side)
    private readonly Action<Bubble>? _onClickPop;       // a GENUINE player click popped this (E-Stim charges key off it)
    private readonly Func<Bubble, bool>? _canChannelDefuse;        // focus gate: may the player's press start a channel?
    private readonly Action<Bubble, string>? _onChannelBroken;     // a channel failed ("click"/"release"/"nofocus") — the trigger follows
    private readonly Func<bool>? _isChaosFrozen;   // true while the field is frozen (freeze-bubble power-up)
    private readonly Func<double>? _timeScaleFn;   // <1 = slow-mo (darter power-up); 1 = normal speed
    private readonly Func<double>? _freezeVibrateMsFn;   // >0 = whole-field shudder remaining (freeze impact)
    // Global FIELD_PACE folds in here so the one knob slows BOTH ambient (spec==null → 1.0 base) and
    // chaos (slow-mo fn base) bubbles: every motion step (_vx*ts) and countdown (-= 32*ts) reads this.
    // See ChaosTuning.FIELD_PACE for the why (fixed-step anim + perf pass un-starved the timer).
    private double TimeScale =>
        (_spec != null ? Math.Max(0.0, _timeScaleFn?.Invoke() ?? 1.0) : 1.0) * ChaosTuning.FIELD_PACE;

    // ---- hold-to-defuse channel state (live chaos bubbles only) ----
    // The player's hand on a live bubble: press starts a channel (the trance pauses, the bubble
    // shrinks under the hold); holding DEFUSE_HOLD_MS completes it (deflate, full pay, focus
    // spent service-side). Releasing early, straying off the hitbox, or pressing without the
    // focus to pay all TRIGGER the bubble immediately. Toys/chains/zones never channel — they
    // call Pop() directly, which stays the instant defuse path.
    private bool _isChanneling;
    private DateTime _channelStartUtc;
    private double _channelStartFuseSec;   // brink windows are judged at channel START (the fuse pauses)
    private double _channelScale = 1.0;    // 1.0 → CHANNEL_MIN_SCALE as the hold completes
    private bool _isDeflating;             // completed defuse: rapid limp shrink + hiss, no burst

    /// <summary>True when this bubble's defuse came from a completed player channel (vs an
    /// instant toy/chain/zone defuse). Valid inside the defuse callback.</summary>
    public bool DefusedViaChannel { get; private set; }

    /// <summary>Fuse seconds to judge brink bonuses against: the CHANNEL-START fuse for a held
    /// defuse (the trance pauses during the hold), the live fuse for an instant one.</summary>
    public double DefuseJudgeFuseSec => DefusedViaChannel ? _channelStartFuseSec : FuseSecLeft;

    // ---- treat-lifetime state (benign reward bubbles: flash/subliminal/golden) ----
    /// <summary>How long a treat may sit on screen before it dissolves away unpopped.</summary>
    private const double TREAT_LIFETIME_MS = 5000;
    /// <summary>Last window (ms) of a treat's life over which it breathes faster (reuses _dangerFactor).</summary>
    private const double TREAT_FADE_TELEGRAPH_MS = 1500;
    private readonly bool _isTreat;                // benign chaos treat (not live, not darter, not freeze)
    private double _treatLifeRemainingMs;
    private bool _isDissolving;                    // expired treat: quiet shrink+fade instead of the pop burst

    // ---- avatar easter-egg state (ambient effect bubbles only) ----
    // When an ambient effect bubble lingers past 4s, the companion may glide over and pop it for
    // the user. _spawnUtc is REAL wall-clock (unlike _timeAlive, which is a per-frame anim counter).
    private readonly DateTime _spawnUtc = DateTime.UtcNow;
    private bool _rolledForEgg;                     // one-shot: the 10% roll fired once at the 4s crossing
    private bool _claimedByAvatar;                  // the companion owns this bubble: life paused, motion frozen, user-pop suppressed
    private bool _avatarPopRequested;               // the scripted avatar pop is in flight (bypasses the claim-pop guard)
    private float _avatarPopVolumeMult = 1f;        // pop-sound loudness multiplier for the avatar pop (1.5 = +50%)

    // ---- freeze-bubble state ----
    private readonly bool _isFreeze;               // this bubble is the "good" freeze pickup
    private System.Windows.Shapes.Ellipse? _freezeAura;   // blue halo, pulsed while the field is frozen
    private double _freezePulsePhase;              // aura pulse oscillator
    private double _fuseTotalMs;
    private double _fuseRemainingMs;
    /// <summary>Last window (ms) of a live fuse over which the bubble flashes + breathes faster.</summary>
    private const double DANGER_TELEGRAPH_MS = 1200;
    /// <summary>Default transparent margin (DIPs) around the bubble inside its window — room for glow/aura/effects.</summary>
    private const int WindowPad = 34;
    /// <summary>Actual per-bubble margin; darters need far more so their big catch-explosion doesn't clip.</summary>
    private readonly int _winPad;
    private double _dangerFactor;   // 0 outside the telegraph window, ramps to 1 at detonation (visual only)
    private readonly double _dangerWobbleMult = 1.0;   // video/gif-rain breathe 60% calmer near fuse-out
    private System.Windows.Shapes.Ellipse? _fuseRing;
    // Mutable (unfrozen) brush reused for the fuse ring so its colour can be tweaked every
    // frame without allocating a new SolidColorBrush per tick (GC pressure at 30fps).
    private SolidColorBrush? _fuseStrokeBrush;
    private double _vx, _vy;                                   // RoamBounce velocity (DIPs/frame)
    private double _screenBottom, _screenLeft, _screenRight;   // motion bounds (DIPs)

    private bool _hasVariantSprite;   // a per-variant sprite replaced the tinted bubble.png

    // ---- lifetime-boon extensions (neutral defaults; chaos effect bubbles only) ----
    private readonly int _hitSize;         // Magic Wand / Mesmer Reach: enlarged click target (>= _size)
    private readonly double _baseOpacity;  // Blindfold: spawn-time opacity multiplier (1.0 = off)
    private readonly double _dpiScale;     // this screen's DPI scale, kept for cursor px → DIP conversion
    private double _shimmerPhase;          // Magic Wand capstone: in-reach shimmer oscillator

    // ---- darter state (only when _spec.IsDarter) ----
    private readonly bool _isDarter;
    private double _telegraphRemainingMs;
    private double _darterActiveMs;
    private double _darterLifeRemainingMs;
    private System.Windows.Shapes.Ellipse? _telegraphRing;
    private bool _wasQuickCatch;
    private int _darterBounces;            // wall hits so far; after DARTER_MAX_BOUNCES it stops bouncing
    private bool _darterEscaping;          // past its bounces → flying off-screen to despawn
    private double _darterThrobPhase;      // continuous throb oscillator
    private double _darterThrobPunch;      // transient scale bump injected on each wall bounce (decays)
    // ---- The Spanker (rabbits become allies instead of catches) ----
    private bool _isSpanked;               // smacked at least once: pink glow + body pops plain bubbles
    private double _spankGrowth = 1.0;     // one-time level-scaled swell on the FIRST smack only
    private double _spankCooldownMs;       // re-smack guard — the vibe sweep overlaps every frame
    // ---- Tail-Plug (rabbits drag a treat-popping sparkle trail) ----
    private readonly List<(Point Px, DateTime T)> _trailPts = new();
    private Point _lastTrailEmitPx = new(double.MinValue, double.MinValue);

    // ---- The Tease (don't touch it: any press triggers; expiry pays the DENIED bonus) ----
    private readonly bool _isTease;
    private double _teaseLifeRemainingMs;
    private System.Windows.Shapes.Ellipse? _teaseShine;   // glossy highlight — pulsed as the perf-safe shimmer
    private bool _teaseAnimated;                          // holds one of the animated-gif budget slots
    private Image? _teaseImg;                             // webp frame-loop target — Destroy must Detach it (Forever clock pins the Image)
    private readonly Action<Bubble>? _onTeaseTouched;
    private readonly Action<Bubble>? _onTeaseDenied;
    private static string[]? _teaseGifPool;               // cached listing of EffectiveAssetsPath/teasebubble
    private static DateTime _teasePoolScanUtc;
    private static int _teaseAnimatedAlive;               // process-wide animated decode budget

    // ---- The Brittle (glass mine: the cursor alone breaks it; the mimic ghost reveals) ----
    private readonly bool _isBrittle;
    private double _brittleArmRemainingMs;                 // spawn grace before hover can shatter it
    private Canvas? _brittleCracks;                        // hairline cracks — faint until armed
    private readonly Action<Bubble>? _onBrittleShattered;
    private static readonly Dictionary<string, BitmapImage> _teaseStillCache = new();   // display-size stills, reused across spawns

    // ---- The Chaperone (a live bubble shielded by an orbiting escort treat) ----
    private Bubble? _orbitTarget;          // escort only: the live it circles
    private Bubble? _escort;               // chaperone live only: its shield-bearer
    private double _orbitPhase;            // escort orbit angle (radians)
    private System.Windows.Shapes.Ellipse? _shieldRing;   // chaperone live: visible while shielded
    private double _shieldFlashMs;         // bounce feedback: the ring flares briefly
    private DateTime _bounceCueUtc;        // dull-thunk throttle (sweeps hit every frame)
    private int _shimmerEmitCounter;       // escort: faint link sparkle cadence

    /// <summary>Mid-pop/deflate/dissolve — used by partners to read each other's state.</summary>
    internal bool IsPopping => _isPopping;

    /// <summary>The Chaperone: while the escort treat lives, this live bubble is untouchable —
    /// every pop path bounces off it with a shimmer (deliberately the one safe-to-misclick live).</summary>
    internal bool IsChaperoneShielded =>
        _spec?.IsChaperoneLive == true && _escort != null && _escort.IsAlive && !_escort.IsPopping;

    /// <summary>Link the escort onto its live (escort side). UI thread, at pair spawn.</summary>
    internal void AttachOrbit(Bubble target) => _orbitTarget = target;

    /// <summary>The Chaperone's live: shrink the roam box by the escort's orbit overhang so
    /// the shield-bearer never swings off-screen (an unreachable escort = an undefusable live).</summary>
    internal void InsetRoamBounds(double inset)
    {
        if (inset <= 0) return;
        _screenLeft += inset;
        _screenRight -= inset;
        _screenTop += inset;
        _screenBottom -= inset;
        if (_screenRight < _screenLeft) _screenLeft = _screenRight = (_screenLeft + _screenRight) / 2.0;
        // Pull the spawn position inside the tightened box (RoamBounce Y bounds are derived).
        _posX = Math.Clamp(_posX, _screenLeft, Math.Max(_screenLeft, _screenRight));
        double topB = _screenTop + _size + 50, botB = _screenBottom - _size - 50;
        _posY = Math.Clamp(_posY, topB, Math.Max(topB, botB));
    }

    /// <summary>Link the live onto its escort (live side). UI thread, at pair spawn.</summary>
    internal void AttachEscort(Bubble escort) => _escort = escort;

    /// <summary>The Bound: loosely mirrored drift — this half pulls against its partner.</summary>
    internal void MirrorVelocityFrom(Bubble other)
    {
        _vx = -other._vx;
        _vy = -other._vy;
    }

    /// <summary>The Bound: the tether snapped (window lapsed, or the partner triggered) — the
    /// survivor turns furious: remaining trance halves, speed +40%, red flare. Still a normal
    /// defusable live afterwards.</summary>
    internal void Enrage()
    {
        if (!_isAlive || _isDestroyed || _isPopping || _spec?.IsLive != true) return;
        _fuseRemainingMs = Math.Max(600, _fuseRemainingMs / 2);
        _speed *= ChaosTuning.BOUND_ENRAGE_SPEED_MULT;
        _vx *= ChaosTuning.BOUND_ENRAGE_SPEED_MULT;
        _vy *= ChaosTuning.BOUND_ENRAGE_SPEED_MULT;
        try
        {
            _bubbleImage.Effect = new DropShadowEffect
            { Color = Color.FromRgb(0xFF, 0x2D, 0x2D), BlurRadius = 30, ShadowDepth = 0, Opacity = 0.95 };
        }
        catch { }
        ShowChaosLabel("ENRAGED", Color.FromRgb(0xFF, 0x5A, 0x5A));
    }
    // ---- prism shadow pop ("Look at the bright colors...") ----
    private Image? _prismGhost;            // the copied bubble's sprite, revealed under the pop

    /// <summary>Tail-Plug: this rabbit's recent trail points (physical px + birth time).
    /// The service sweeps treats against these each anim tick while the boon holds.</summary>
    internal IReadOnlyList<(Point Px, DateTime T)> TrailPoints => _trailPts;

    /// <summary>The chaos spec this bubble carries (null for ambient pop-game bubbles).</summary>
    public EffectBubbleSpec? Spec => _spec;

    // ---- avatar easter-egg accessors (see RunAvatarBubbleEggAsync) ----
    /// <summary>An ambient effect bubble: a benign treat carrying a payload that fires on pop
    /// (flash/subliminal/pink/spiral/braindrain/video/gif-rain/glitch). The companion may pop these.</summary>
    internal bool IsAmbientEffectBubble =>
        _isTreat && _spec != null && _spec.Payload != null && !_isDarter && !_isFreeze
        && _isAlive && !_isDestroyed && !_isPopping;
    /// <summary>Real wall-clock age in ms (NOT the _timeAlive anim counter).</summary>
    internal double AgeMs => (DateTime.UtcNow - _spawnUtc).TotalMilliseconds;
    /// <summary>One-shot latch so the egg's 10% roll happens once, at the 4s crossing.</summary>
    internal bool RolledForEgg { get => _rolledForEgg; set => _rolledForEgg = value; }
    /// <summary>True while the companion owns this bubble (life paused, motion frozen, user-pop ignored).</summary>
    internal bool ClaimedByAvatar => _claimedByAvatar;
    /// <summary>Pop-burst radius in physical px — lets the avatar land beside, not over, the bubble.</summary>
    internal double RadiusPx => _size / 2.0 * _dpiScale;
    /// <summary>The effect variant id (drives the mod-themed voiceline pick).</summary>
    internal string EffectKindId => _spec?.VariantId ?? "";
    /// <summary>Loudness multiplier the benign-pop path applies to the pop sound (1 for user pops).</summary>
    internal float AvatarPopVolumeMult => _avatarPopVolumeMult;

    /// <summary>Companion claims this bubble: pause its treat-life + freeze its drift + ignore user pops.
    /// Also clears the death-telegraph ramp (a 5s treat is already fading at the 4s claim point) so the
    /// bubble reads as healthy while she glides over and pops it.</summary>
    internal void ClaimForAvatar() { _claimedByAvatar = true; _dangerFactor = 0; }
    /// <summary>Release the claim — drift + remaining treat-life resume on the next frame.</summary>
    internal void ReleaseAvatarClaim() => _claimedByAvatar = false;
    /// <summary>The scripted avatar pop: louder, and allowed through the claim-pop guard.</summary>
    internal void PopByAvatar(float volumeMult)
    {
        _avatarPopVolumeMult = volumeMult;
        _avatarPopRequested = true;
        Pop();
    }

    /// <summary>Eligible to be swept up by a Chain Reaction pop: any live, un-popped chaos bubble
    /// (darters included — a chained darter counts as a catch and fires its slow-mo). A shielded
    /// Chaperone live is excluded — chains and arcs route around it (its escort conducts fine).</summary>
    public bool IsChainable => _isAlive && !_isDestroyed && !_isPopping && _spec != null
                               && !IsChaperoneShielded && !_spec.IsTease && !_spec.IsBrittle;

    /// <summary>This bubble's on-screen box in DIPs (the bubble.png footprint, sans window pad).</summary>
    public Rect BoundingBox => new Rect(_posX, _posY, _size, _size);

    /// <summary>This bubble's centre in physical screen px — the cross-screen-safe currency
    /// (per-screen DIPs diverge under mixed DPI). Feeds E-Stim targeting + its lightning overlay.</summary>
    internal Point CenterPx => new((_posX + _size / 2.0) * _dpiScale, (_posY + _size / 2.0) * _dpiScale);

    /// <summary>Squared distance (DIPs) from this bubble's centre to a physical-pixel point.</summary>
    internal double DistDipSqToPx(double pxX, double pxY)
    {
        double dx = pxX / _dpiScale - (_posX + _size / 2.0);
        double dy = pxY / _dpiScale - (_posY + _size / 2.0);
        return dx * dx + dy * dy;
    }

    // ---- Shared-host pop hit-testing (mouse-hook path; see BubbleService.OnSharedHostLeftDown) ----

    /// <summary>True when this bubble renders on the shared Canvas host (vs. its own pooled layered
    /// window). The hook-pop path targets ONLY host bubbles; per-window bubbles keep their WPF handler,
    /// so this is the guard that prevents a double-pop across the two render paths.</summary>
    internal bool UsesHost => _useHost;

    /// <summary>This bubble is currently a valid left-click pop target.</summary>
    internal bool HostHitClickable => _isClickable && _isAlive && !_isDestroyed && !_isPopping && !_claimedByAvatar;

    /// <summary>This bubble defuses by a HELD press (the channel), not a single click — so the
    /// shared-host hook must NOT swallow its click: the channel reads the held button via
    /// GetAsyncKeyState, which a swallowed low-level click never registers (it would instantly
    /// detonate). Mirrors the live-threat branch of OnPlayerPress (live, non-darter/freeze/tease/brittle).</summary>
    internal bool NeedsHoldDefuse =>
        _spec?.IsLive == true && !_isDarter && !_isFreeze && !_isTease && !_isBrittle;

    /// <summary>Hit disc in PHYSICAL px (centre + radius) for the immutable hook-thread snapshot.</summary>
    internal (double X, double Y, double R) HitDiscPx =>
        ((_posX + _size / 2.0) * _dpiScale, (_posY + _size / 2.0) * _dpiScale, _hitSize / 2.0 * _dpiScale);

    /// <summary>True if a PHYSICAL-px point lands inside this bubble's hitbox.</summary>
    internal bool ContainsPx(Point px) => DistDipSqToPx(px.X, px.Y) <= (_hitSize / 2.0) * (_hitSize / 2.0);

    /// <summary>Pop this bubble from a shared-host hook click (UI thread) — routes exactly like a
    /// real press (tease/brittle/channel/benign all handled by OnPlayerPress).</summary>
    internal void HostHookPop() => OnPlayerPress();

    /// <summary>The box grown by <paramref name="expand"/> about its centre — the reach of its pop burst.</summary>
    public Rect ChainReach(double expand)
    {
        double grow = _size * (expand - 1.0);
        return new Rect(_posX - grow / 2, _posY - grow / 2, _size + grow, _size + grow);
    }

    /// <summary>True if this darter was caught within its quick-catch window. Valid after Pop().</summary>
    public bool WasQuickCatch => _wasQuickCatch;

    /// <summary>A spanked rabbit's pop swath — its (grown) body box.</summary>
    internal Rect SpankReach => ChainReach(Math.Max(1.0, _spankGrowth));

    /// <summary>Remaining fuse (ms) while this is an armed, un-popped live chaos bubble; else null.
    /// Read by <see cref="BubbleService.MinChaosFuseSec"/> for the Blindfold heartbeat.</summary>
    internal double? LiveFuseRemainingMs =>
        _spec?.IsLive == true && _isAlive && !_isDestroyed && !_isPopping ? _fuseRemainingMs : null;

    /// <summary>True while the tick-sampled cursor rests inside this bubble's hitbox (same
    /// reach math as the VibePopping sweep). Read by the HUD's focus-bar hover cue.</summary>
    internal bool CursorInside()
    {
        double dx = BubbleService.CursorPxX / _dpiScale - (_posX + _size / 2.0);
        double dy = BubbleService.CursorPxY / _dpiScale - (_posY + _size / 2.0);
        double reach = _hitSize / 2.0;
        return dx * dx + dy * dy <= reach * reach;
    }

    /// <summary>Fuse seconds left at the moment of the snap (valid inside the defuse callback,
    /// where _isPopping is already set) — feeds the Last Breath brink-bonus check.</summary>
    public double FuseSecLeft => Math.Max(0, _fuseRemainingMs) / 1000.0;

    private struct SparkleParticle
    {
        public double X, Y, VelX, VelY, Alpha, Size;
        public System.Windows.Shapes.Ellipse Shape;
    }

    public bool IsAlive => _isAlive && !_isDestroyed;

    public Bubble(System.Windows.Forms.Screen screen, BitmapImage? image, Random random,
                  Action<Bubble>? onPop, Action<Bubble>? onMiss, Action<Bubble>? onDestroy, bool isClickable = true,
                  EffectBubbleSpec? spec = null,
                  Action<Bubble>? onBenignPop = null, Action<Bubble>? onDefuse = null, Action<Bubble>? onDetonate = null,
                  Action<Bubble>? onDarterCaught = null, Func<bool>? isChaosFrozen = null,
                  Func<double>? timeScale = null, Action<Bubble>? onFreezeCaught = null,
                  Func<double>? freezeVibrateMs = null, Action<Bubble>? onChainTrigger = null,
                  double hitboxScale = 1.0, bool liveMagnet = false, double opacityMult = 1.0, Action<Bubble>? onSpankSweep = null,
                  Action<Bubble>? onTreatExpired = null, Action<Bubble>? onClickPop = null,
                  Func<Bubble, bool>? canChannelDefuse = null, Action<Bubble, string>? onChannelBroken = null,
                  Action<Bubble>? onTeaseTouched = null, Action<Bubble>? onTeaseDenied = null,
                  Action<Bubble>? onBrittleShattered = null, bool forceWindowMode = false)
    {
        _random = random;
        _onPop = onPop;
        _onMiss = onMiss;
        _onDestroy = onDestroy;
        _isClickable = isClickable;
        _spec = spec;
        _onBenignPop = onBenignPop;
        _onDefuse = onDefuse;
        _onDetonate = onDetonate;
        _onDarterCaught = onDarterCaught;
        _onFreezeCaught = onFreezeCaught;
        _onChainTrigger = onChainTrigger;
        _onSpankSweep = onSpankSweep;
        _onTreatExpired = onTreatExpired;
        _onClickPop = onClickPop;
        _canChannelDefuse = canChannelDefuse;
        _onChannelBroken = onChannelBroken;
        _onTeaseTouched = onTeaseTouched;
        _onTeaseDenied = onTeaseDenied;
        _onBrittleShattered = onBrittleShattered;
        _isChaosFrozen = isChaosFrozen;
        _timeScaleFn = timeScale;
        _freezeVibrateMsFn = freezeVibrateMs;
        _isDarter = spec?.IsDarter == true;
        _isFreeze = spec?.IsFreeze == true;
        _isTease = spec?.IsTease == true;
        if (_isTease) _teaseLifeRemainingMs = ChaosTuning.TEASE_LIFE_MS;
        _isBrittle = spec?.IsBrittle == true;
        if (_isBrittle) _brittleArmRemainingMs = ChaosTuning.BRITTLE_ARM_MS;
        // Treats (flash/subliminal/golden) rot: only so long on screen before they dissolve.
        // Hearts don't rot — they drift down once and exit; missing one carries no sting.
        // Gold Digger droplets don't either: pure bonus, no penalty for letting them fall away.
        // Chaperone escorts don't either: the shield mechanic would dismantle itself in 5s.
        // The Tease isn't a treat either — it runs its own expiry (the DENIED bonus, no streak dock).
        // Nor is the Brittle: a dodged glass mine just drifts off-screen, no rot, no sting.
        _isTreat = spec != null && !spec.IsLive && !_isDarter && !_isFreeze
                   && spec.IsHeart != true && spec.IsDroplet != true && spec.IsEscort != true
                   && spec.IsTease != true && spec.IsBrittle != true;
        if (_isTreat) _treatLifeRemainingMs = spec!.TreatLifeMs > 0 ? spec.TreatLifeMs : TREAT_LIFETIME_MS;
        // The two giants read frantic when they breathe at full danger amplitude — calm them 60%.
        if (spec?.VariantId is "video" or "htlink") _dangerWobbleMult = 0.4;

        // Random properties (size/motion overridden for chaos effect bubbles)
        _size = spec != null ? Math.Max(60, (int)Math.Round(spec.SizePx)) : random.Next(150, 250);
        // Magic Wand boon / Mesmer Reach upgrade: enlarge the click target around the visual.
        // Darters/freeze pickups keep their natural hitbox (precision catches stay precision).
        // Blindfold: effect bubbles render translucent; pickups stay fully visible (they're rewards).
        bool plainEffectBubble = spec != null && !_isDarter && !_isFreeze && !spec.IsGolden && !spec.IsHeart
                                 && !spec.IsDroplet && !spec.IsPrism && !spec.IsEscort
                                 && !spec.IsTease    // pickups, prism, escorts + the tease stay fully visible (it must be READ to be denied)
                                 && !spec.IsBrittle; // the Brittle too: an invisible hover-mine would be unfair — and it keeps its NATURAL hitbox (dodging stays precision)
        // Silk Touch magnet: a near-miss on a LIVE still counts as a touch — the invisible hit
        // ellipse reaches ~40% past the sprite (treats keep the plain scale). Same 2.0 ceiling
        // as the wand-enlarged hitbox so the window envelope never grows past the proven max.
        double hitMult = Math.Clamp(hitboxScale, 1.0, 2.0);
        if (liveMagnet && spec?.IsLive == true) hitMult = Math.Clamp(hitMult * 1.4, 1.0, 2.0);
        _hitSize = plainEffectBubble ? Math.Max(_size, (int)Math.Round(_size * hitMult)) : _size;
        _baseOpacity = plainEffectBubble ? Math.Clamp(opacityMult, 0.2, 1.0) : 1.0;
        // Darters explode to ~3x on catch (slower + bigger pop), so give them a generous window
        // so the burst + glow don't clip against the window edge.
        //
        // Normal/chaos bubbles expand about their centre when popped (the pop scale grows to
        // ~1.6x, ~1.9x on a lucky pop). The window must reserve that much headroom around the
        // sprite or the growing image clips the window edge — full-bleed mod art (e.g. Circe)
        // shows this immediately, where sprites with a transparent margin hide it. popPad covers
        // the worst-case pop scale; the hit-ellipse term is kept as a floor (Wand/Mesmer Reach).
        const double PopScaleHeadroom = 1.9;
        int popPad = (int)Math.Ceiling(_size * (PopScaleHeadroom - 1.0) / 2.0);
        _winPad = _isDarter
            ? Math.Max(WindowPad, (int)Math.Round(_size * 1.3))
            : Math.Max(WindowPad + (_hitSize - _size + 1) / 2, popPad);
        _speed = 1.0 + random.NextDouble() * 1.0; // 1.0 to 2.0 px/frame
        if (spec != null) // bigger chaos bubbles drift a little slower (more reachable)
            _speed *= Math.Clamp(1.4 - (_size - 150) / 220.0, 0.6, 1.4);
        if (spec != null) _speed *= Math.Max(0.1, spec.SpeedMult);   // golden bubbles fly
        if (spec != null) _speed *= ChaosTuning.CHAOS_SPEED_MULT;    // chaos pace bump: travel farther before rotting
        // Dashboard speed slider: up to +500% travel (6x) for the ambient game — BOTH plain and
        // trigger bubbles — leaving chaos pacing alone (chaos bubbles carry a spec but not
        // forceWindowMode).
        if (spec == null || forceWindowMode)
        {
            int speedBoost = App.Settings?.Current?.BubbleSpeedBoost ?? 0;
            if (speedBoost > 0) _speed *= 1.0 + Math.Clamp(speedBoost, 0, 500) / 100.0;
        }
        _animType = random.Next(4);
        _wobbleOffset = random.NextDouble() * 100;
        _angle = random.Next(360);

        // Get DPI scale for this specific screen
        var dpiScale = GetDpiForScreen(screen);
        _dpiScale = dpiScale;

        var area = screen.WorkingArea;
        _screenLeft = area.X / dpiScale;
        _screenRight = (area.X + area.Width) / dpiScale - _size;
        _screenTop = area.Y / dpiScale - _size - 50;
        _screenBottom = (area.Y + area.Height) / dpiScale + 50;

        // Position + initial velocity depend on motion (FloatUp is the ambient default).
        var motion = spec?.Motion ?? ChaosMotion.FloatUp;
        _startX = (area.X + random.Next(50, Math.Max(100, area.Width - _size - 50))) / dpiScale;
        _posX = _startX;
        switch (motion)
        {
            case ChaosMotion.RainDown:
                _posY = area.Y / dpiScale - _size;                 // start just above the top
                break;
            case ChaosMotion.RoamBounce:
                _posY = (area.Y + random.Next(50, Math.Max(100, area.Height - _size - 50))) / dpiScale;
                double ang = random.NextDouble() * Math.PI * 2;
                double roamSpeed = _speed * 1.4;
                _vx = Math.Cos(ang) * roamSpeed;
                _vy = Math.Sin(ang) * roamSpeed;
                break;
            case ChaosMotion.SideDrift:
                // Slide in from a random side edge at a comfortable height; _vx carries the
                // crossing speed, the shared wobble plays on Y around _startY.
                _startY = (area.Y + random.Next(120, Math.Max(240, area.Height - _size - 120))) / dpiScale;
                _posY = _startY;
                bool fromLeft = random.Next(2) == 0;
                _posX = _startX = fromLeft ? _screenLeft - _size : _screenRight + _size;
                _vx = (fromLeft ? 1 : -1) * _speed * 1.15;
                break;
            default: // FloatUp
                _posY = (area.Y + area.Height) / dpiScale;          // start at the bottom
                break;
        }

        // Pinned spawn (Rabbit Caller): materialise centred on the given physical-px point,
        // overriding the motion's usual origin. Velocity/bounce behaviour is untouched.
        if (spec?.SpawnAtPxX is double spawnPx && spec.SpawnAtPxY is double spawnPy)
        {
            _startX = _posX = spawnPx / dpiScale - _size / 2.0;
            _posY = spawnPy / dpiScale - _size / 2.0;
        }

        // Live chaos bubbles arm a fuse.
        if (spec != null && spec.IsLive)
            _fuseTotalMs = _fuseRemainingMs = Math.Max(1, spec.FuseMs);

        // Darters: telegraph + active lifetime, and a faster fixed-magnitude velocity.
        if (_isDarter && spec != null)
        {
            _telegraphRemainingMs = Math.Max(0, spec.TelegraphMs);
            _darterLifeRemainingMs = Math.Max(1, spec.LifetimeMs);
            double mag = Math.Sqrt(_vx * _vx + _vy * _vy);
            if (mag < 0.001) { double a = random.NextDouble() * Math.PI * 2; _vx = Math.Cos(a); _vy = Math.Sin(a); mag = 1; }
            _vx = _vx / mag * spec.DarterSpeed;
            _vy = _vy / mag * spec.DarterSpeed;
        }

        // Create bubble image (hit-testing disabled — the Ellipse behind handles clicks)
        _bubbleImage = new Image
        {
            Width = _size,
            Height = _size,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(_bubbleImage, PerformanceProfile.ScalingMode(PerformanceProfile.CurrentTier));

        // Chaos: a per-variant sprite at Assets/Chaos/bubbles/{variant}.png replaces the
        // tinted bubble.png when present (the tint overlay is then skipped). Falls back to
        // the shared bubble image otherwise.
        // GG sweeper rabbits share the "darter" VariantId but wear their own amber sprite so
        // they read as a different rabbit from the catchable pink one (falls back to darter.png).
        var variantSprite = spec == null ? null
            : (spec.IsSweeper ? ChaosArt.Resolve("bubbles", "sweeper") : null)
              ?? ChaosArt.Resolve("bubbles", spec.VariantId);
        if (variantSprite != null)
        {
            _bubbleImage.Source = variantSprite;
            _hasVariantSprite = true;
        }
        else if (image != null)
        {
            _bubbleImage.Source = image;
        }
        else
        {
            // Fallback - create simple ellipse
            var drawing = new DrawingGroup();
            using (var ctx = drawing.Open())
            {
                var gradientBrush = new RadialGradientBrush(
                    Color.FromArgb(180, 200, 220, 255),
                    Color.FromArgb(80, 255, 255, 255));
                ctx.DrawEllipse(gradientBrush, new Pen(Brushes.White, 2), 
                    new Point(_size / 2, _size / 2), _size / 2 - 5, _size / 2 - 5);
            }
            _bubbleImage.Source = new DrawingImage(drawing);
        }

        // Transform for rotation and scale
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new ScaleTransform(1, 1));
        transformGroup.Children.Add(new RotateTransform(0));
        _bubbleImage.RenderTransform = transformGroup;

        // Create invisible hit area ellipse that covers the full bubble
        // This ensures clicks anywhere in the circular bubble area register
        var hitArea = new System.Windows.Shapes.Ellipse
        {
            Width = _hitSize,
            Height = _hitSize,
            Fill = s_hitBrush, // shared frozen near-invisible brush (captures hits; identical every bubble)
            IsHitTestVisible = _isClickable,
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow
        };

        if (_isClickable)
        {
            hitArea.MouseLeftButtonDown += (s, e) =>
            {
                OnPlayerPress();
                e.Handled = true;
            };
        }

        // Sparkle particle canvas (overlays the bubble, non-interactive)
        _sparkleCanvas = new Canvas
        {
            Width = _size,
            Height = _size,
            IsHitTestVisible = false
        };

        // Create container grid with hit area behind the bubble image. Sized to the (possibly
        // wand-enlarged) hit ellipse; the image keeps its own size and centres inside.
        _grid = new Grid
        {
            Width = _hitSize,
            Height = _hitSize,
            Background = Brushes.Transparent,
            IsHitTestVisible = _isClickable,
            // Centre the hit/visual inside the fixed-size window (the window is now sized to a
            // quantized bucket, not snug to the bubble — see the spawn block below).
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _grid.Children.Add(hitArea);         // Hit area first (behind)
        _grid.Children.Add(_bubbleImage);    // Image on top
        // Glassy specular shine over plain round bubbles (variant sprites bring their own art).
        // Chaos bubbles only — the classic (ambient) bubble feature reads as a harsh white glare with it
        // (the bubble.png already has its own highlights), so keep the rim-shine to the Rabbit Hole.
        if (ChaosSkiaFxOverlay.Enabled && !_hasVariantSprite && spec != null)
        {
            var shine = new System.Windows.Shapes.Ellipse
            {
                Width = _size,
                Height = _size,
                Fill = _shineBrush,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _grid.Children.Add(shine);
        }
        BuildChaosLayers();                  // tint + label + fuse ring (no-op for ambient bubbles)
        _grid.Children.Add(_sparkleCanvas);  // Sparkles on top of everything

        // Grid click as backup (only if clickable)
        if (_isClickable)
        {
            _grid.MouseLeftButtonDown += (s, e) =>
            {
                OnPlayerPress();
                e.Handled = true;
            };
        }

        // Single window - clickable or click-through based on setting. Rented from the pool
        // (recycled hidden shell) rather than newly created — see RentWindow / the pool above.
        // Every per-bubble property below is (re)set on each reuse so a recycled shell carries
        // no state from its previous bubble.
        // Shared-host A/B (chaos bubbles only): one Canvas host instead of a Window per bubble.
        // forceWindowMode pins per-window rendering for dashboard trigger bubbles — the shared
        // ChaosBubbleHostOverlay only exists during a real chaos run.
        // Chaos effect bubbles ride the host under ChaosBubbleSharedHost; ambient dashboard bubbles
        // (spec == null) ride it under BubbleSharedHost while the ambient host is standing. Either way
        // forceWindowMode (dashboard trigger bubbles) pins per-window rendering.
        bool chaosHost = spec != null && (App.Settings?.Current?.ChaosBubbleSharedHost ?? false);
        bool ambientHost = spec == null && AmbientHostActive && (App.Settings?.Current?.BubbleSharedHost ?? false);
        _useHost = !forceWindowMode && (chaosHost || ambientHost);

        // Quantized window side (per-window mode); also a harmless notional size in host mode.
        double winNeed = Math.Max(_size, _hitSize) + _winPad * 2;
        _winDim = Math.Ceiling(winNeed / 128.0) * 128.0;

        if (_useHost)
        {
            // SHARED-HOST MODE: no per-bubble Window. The grid is a child of the one
            // ChaosBubbleHostOverlay Canvas, repositioned each frame via Canvas.SetLeft/Top — no
            // SetWindowPos storm, so click input never starves. The host is click-through; pops come
            // from the global mouse hook (BubbleService.OnSharedHostLeftDown), so NO WPF click handlers.
            _window = null;
            _fxTarget = _grid;
            _grid.Opacity = _baseOpacity;
            _grid.Effect = null;
            ChaosBubbleHostOverlay.Add(_grid);
            // Mixed-DPI: the host renders at ONE scale (its majority monitor's). A bubble whose
            // screen runs a different scale compensates with a LayoutTransform, or its visual is
            // drawn displaced + mis-sized from the physical-px hit disc the mouse hook tests —
            // the second-screen "bubbles don't pop" bug on e.g. 125% primary / 100% secondary.
            double hostScale = ChaosBubbleHostOverlay.RenderScale;
            if (hostScale > 0 && Math.Abs(hostScale - _dpiScale) > 0.001)
                _grid.LayoutTransform = new ScaleTransform(_dpiScale / hostScale, _dpiScale / hostScale);
            ChaosBubbleHostOverlay.Place(_grid, (_posX + _size / 2.0 - _hitSize / 2.0) * _dpiScale,
                                                (_posY + _size / 2.0 - _hitSize / 2.0) * _dpiScale);
        }
        else
        {
            // PER-WINDOW MODE (default): one pooled top-level layered Window per bubble. Quantize to a
            // 128px bucket so a recycled shell reuses its DIB back-buffer (per-spawn resize of a layered
            // window reallocs the native DIB — that was the chaos OOM; managed heap flat, GDI climbing).
            // Rent an exact-size shell from the pool: the size-aware pool guarantees Width==Height==_winDim,
            // so a layered-window resize (OnResize -> CompleteRender wedge + DIB realloc) never happens here.
            _window = RentWindow((int)_winDim);
            // Null (not Transparent) background so the quantized margin around the centred grid is NOT
            // hit-testable — clicks there pass through; the click area stays exactly the grid.
            _window.Background = null;
            // Ambient bubbles always topmost; chaos bubbles follow the run mode (Free Desktop keeps them
            // out of the topmost band). ReturnWindow resets this to true so a shell never leaks state.
            _window.Topmost = spec != null ? ChaosWindowZ.BornTopmost : true;
            _window.Left = _posX + _size / 2.0 - _winDim / 2.0;
            _window.Top = _posY + _size / 2.0 - _winDim / 2.0;
            _window.Content = _grid;
            _window.Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow;
            _window.IsHitTestVisible = _isClickable;
            _window.Opacity = _baseOpacity;   // Blindfold dims per-frame; seed so frame 1 isn't full-bright
            _window.Effect = null;
            _fxTarget = _window;

            // Window click as final backup (only if clickable). Stored so Destroy() can detach it —
            // a recycled pooled window must not keep a handler that roots this dead bubble.
            if (_isClickable)
            {
                _winClickHandler = (s, e) => OnPlayerPress();
                _window.MouseLeftButtonDown += _winClickHandler;
            }
        }

        // Tunnel Vision capstone: spotlight rabbits glow gold. Window-level per-window, grid-level on the
        // shared host — _fxTarget abstracts the two. Skipped on the Performance tier; radius capped.
        if (spec?.Spotlight == true)
        {
            var glowTier = PerformanceProfile.CurrentTier;
            if (PerformanceProfile.AllowGlow(glowTier))
            {
                try
                {
                    _fxTarget.Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(0xFF, 0xD7, 0x00),
                        BlurRadius = Math.Min(40, PerformanceProfile.MaxGlowBlurRadius(glowTier)),
                        ShadowDepth = 0,
                        Opacity = 0.85
                    };
                }
                catch { }
            }
        }

        // GG make more GG: sweeper rabbits are born spanked — ally-AMBER glow on the sprite itself
        // (so it works identically in both modes), body mows bubbles, never catchable.
        if (spec?.IsSweeper == true)
        {
            _isSpanked = true;
            _spankGrowth = 1.0;
            try { _bubbleImage.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x8A, 0x14), BlurRadius = 36, ShadowDepth = 0, Opacity = 1.0 }; } catch { }
        }

        // Show + alt-tab hide (per-window mode only — the host is already shown). A recycled shell can be
        // parked on its previous monitor; reveal chaos bubbles at zero opacity, pin to the target screen
        // in physical px, then restore opacity, so the first visible frame is on the right monitor.
        if (!_useHost)
        {
            if (_spec != null)
            {
                double revealOpacity = _window!.Opacity;
                _window.Opacity = 0;
                _window.Show();
                PinWindowToTargetScreen();
                _window.Opacity = revealOpacity;
            }
            else
            {
                _window!.Show();
            }
            HideFromAltTab();
        }

        // Note: Animation is now driven by shared timer in BubbleService.AnimateAllBubbles()
    }

    /// <summary>
    /// Called by BubbleService's shared animation timer (~30 FPS)
    /// </summary>
    public void AnimateFrame()
    {
        // Early exit checks - must be first to avoid any work on destroyed bubbles
        if (!_isAlive || _isDestroyed) return;

        // Hold-to-defuse channel: judged BEFORE the motion chain so it runs even while the
        // field is frozen (defusing during a freeze is the reward — and it costs no focus).
        if (_isChanneling && !_isPopping)
        {
            TickChannel();
            if (!_isAlive || _isDestroyed) return;
        }

        // Frozen field (freeze-bubble power-up): a chaos bubble that isn't mid-pop holds its
        // position + fuse, but still falls through to the visual block so its aura keeps pulsing.
        bool frozen = _spec != null && !_isPopping && _isChaosFrozen?.Invoke() == true;

        if (_isPopping)
        {
            // A resolving chaos bubble is a corpse: its burst can sprawl far past its body (a
            // caught rabbit explodes to ~3x) and the window's opaque pixels keep eating clicks
            // meant for live neighbours underneath. Under slow-mo neighbours barely drift clear
            // of it, which read as "rabbits aren't poppable in slow-mo" — so the moment a bubble
            // starts dying its window goes input-transparent at the Win32 level.
            if (!_corpseClickThrough && _spec != null)
            {
                _corpseClickThrough = true;
                MakeCorpseClickThrough();
                HideHint();   // a dying bubble teaches nothing — drop the verb hint with it
            }

            if (_isDeflating)
            {
                // Completed defuse: it DEFLATES — a rapid limp shrink, wobbling like air
                // escaping, no burst, no spin-up. Starts from the channel's shrunken scale.
                _timeAlive += 0.02;
                _scale = Math.Max(0.05, _scale - 0.085);
                _fadeAlpha -= 0.075;
                _angle += Math.Sin(_timeAlive * 40) * 6;   // limp wobble
            }
            else if (_isDissolving)
            {
                // Expired treat: a quiet dissolve — shrink + fade in place, no burst, no spin.
                _scale = Math.Max(0.1, _scale - 0.02);
                _fadeAlpha -= 0.05;
            }
            else if (_isDarter)
            {
                // Darter explosion: enlarge slowly, then dissipate (slower + bigger than a normal pop).
                _scale += 0.07;
                _fadeAlpha -= 0.030;
            }
            else
            {
                // Pop animation - expand and fade (scaled for 30fps)
                _scale += 0.04;
                _fadeAlpha -= _isLucky ? 0.044 : 0.066; // Lucky pops linger ~50% longer
                _angle += 2;
            }

            // Animate sparkle particles outward
            if (_sparkles != null)
            {
                for (int i = 0; i < _sparkles.Count; i++)
                {
                    var sp = _sparkles[i];
                    sp.X += sp.VelX;
                    sp.Y += sp.VelY;
                    sp.VelY += 0.15; // Slight gravity
                    sp.Alpha -= _isLucky ? 0.04 : 0.06;

                    if (sp.Alpha > 0)
                    {
                        try
                        {
                            Canvas.SetLeft(sp.Shape, sp.X - sp.Size / 2);
                            Canvas.SetTop(sp.Shape, sp.Y - sp.Size / 2);
                            sp.Shape.Opacity = Math.Max(0, sp.Alpha);
                        }
                        catch { }
                    }

                    _sparkles[i] = sp;
                }
            }

            if (_fadeAlpha <= 0)
            {
                Destroy();
                return;
            }
        }
        else if (frozen || _isChanneling || _claimedByAvatar)
        {
            // Held in place — no motion, no fuse tick. The visual block below still runs so the
            // blue freeze aura pulses and the impact shudder plays.
            // A defuse channel pins the bubble the same way: the player is holding it, so it
            // must not drift out of its own hit circle mid-hold and break the channel "for free"
            // (at field speed a live escaped a stationary cursor in under the 1s hold).
            // An avatar-claimed effect bubble is pinned too: its treat-life (decremented in the
            // travel branch below) freezes, so it can't dissolve out from under the companion
            // mid-egg, and it stays put for the avatar to land beside and pop.
        }
        else if (_isDarter)
        {
            // Darter: a speedy glowing orb. Flares in place during the telegraph, then bolts and
            // bounces a few times before running off-screen. It throbs the whole time, with an
            // extra punch on every wall bounce. Slowed by the time-scale (e.g. another darter's slow-mo).
            _timeAlive += 0.02;
            double ts = TimeScale;

            // Continuous throb + decaying bounce-punch, written into _scale (the shared visual block
            // skips its own wobble for darters so this isn't doubled).
            _darterThrobPhase += 0.5 * Math.Max(ts, 0.4);
            if (_darterThrobPunch > 0) _darterThrobPunch = Math.Max(0, _darterThrobPunch - 0.05);
            // Breath + punch are ABSOLUTE (±0.10) on top of the swell — a grown ally shouldn't
            // wobble proportionally harder than a normal rabbit.
            _scale = _spankGrowth + 0.10 * Math.Sin(_darterThrobPhase) + _darterThrobPunch;

            if (_telegraphRemainingMs > 0)
            {
                _telegraphRemainingMs -= 32;
                if (_telegraphRing != null && _spec != null)
                {
                    double tfrac = Math.Clamp(_telegraphRemainingMs / Math.Max(1, _spec.TelegraphMs), 0, 1);
                    if (_telegraphRing.RenderTransform is ScaleTransform tst)
                    { double rs = 1.0 + 0.30 * tfrac; tst.ScaleX = rs; tst.ScaleY = rs; }
                    _telegraphRing.Opacity = 0.85 * tfrac;
                    if (_telegraphRemainingMs <= 0) _telegraphRing.Visibility = Visibility.Collapsed;
                }
                // hold position while telegraphing
            }
            else
            {
                // The quick-catch window tracks field pace too: a slow-mo rabbit has barely
                // dashed in 500 real ms, so the bonus stays catchable relative to its motion.
                _darterActiveMs += 32 * Math.Max(ts, 0.15);

                // The Pull: rabbits fly AT you — steer toward the cursor with a capped turn rate.
                if (BubbleService.ChaosRabbitHomingNow && !_darterEscaping)
                {
                    double hcx = BubbleService.CursorPxX / _dpiScale, hcy = BubbleService.CursorPxY / _dpiScale;
                    double hdx = hcx - (_posX + _size / 2.0), hdy = hcy - (_posY + _size / 2.0);
                    if (hdx * hdx + hdy * hdy > 1)
                    {
                        double want = Math.Atan2(hdy, hdx);
                        double cur = Math.Atan2(_vy, _vx);
                        double diff = want - cur;
                        while (diff > Math.PI) diff -= 2 * Math.PI;
                        while (diff < -Math.PI) diff += 2 * Math.PI;
                        double maxTurn = 0.065 * Math.Max(ts, 0.4);   // ~3.7°/frame, softer in slow-mo
                        double na = cur + Math.Clamp(diff, -maxTurn, maxTurn);
                        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
                        _vx = Math.Cos(na) * spd; _vy = Math.Sin(na) * spd;
                    }
                }

                _posX += _vx * ts;
                _posY += _vy * ts;
                double topB = _screenTop + _size + 50, botB = _screenBottom - _size - 50;

                if (!_darterEscaping)
                {
                    // Reflect off the edges, up to DARTER_MAX_BOUNCES; each hit fires a throb punch.
                    bool hit = false;
                    if (_posX < _screenLeft) { _posX = _screenLeft; _vx = Math.Abs(_vx); hit = true; }
                    else if (_posX > _screenRight) { _posX = _screenRight; _vx = -Math.Abs(_vx); hit = true; }
                    if (_posY < topB) { _posY = topB; _vy = Math.Abs(_vy); hit = true; }
                    else if (_posY > botB) { _posY = botB; _vy = -Math.Abs(_vy); hit = true; }
                    if (hit)
                    {
                        _darterBounces++;
                        _darterThrobPunch = 0.22;   // visible squash-pulse on impact
                        if (_darterBounces >= ChaosBubbleVariants.DARTER_MAX_BOUNCES)
                            _darterEscaping = true; // out of bounces → next edge it just leaves
                    }
                }
                else
                {
                    // Escaping: no more reflection. Despawn once fully past an edge.
                    double pad = _size + 80;
                    if (_posX < _screenLeft - pad || _posX > _screenRight + pad
                        || _posY < topB - pad || _posY > botB + pad)
                    { Destroy(); return; }
                }
            }
            if (_spankCooldownMs > 0) _spankCooldownMs -= 32;
            // The Spanker: a smacked rabbit is an ally — its body pops every plain bubble it crosses.
            if (_isSpanked && !_isPopping) _onSpankSweep?.Invoke(this);

            // Tail-Plug: while the boon holds, every rabbit drags a sparkle trail — record a
            // point each ~40 DIPs of travel (the service pops treats against them; the field-FX
            // overlay mirrors each point as a fading spark). Old points age out here.
            // GG sweepers ALWAYS drag a trail (boon or not) — an amber streak that sells the
            // "different, aggressive rabbit" read; their body already mows, so it's pure flair.
            bool sweeperTrail = _spec?.IsSweeper == true;
            double trailSec = sweeperTrail
                ? Math.Max(0.5, BubbleService.ChaosRabbitTrailSecNow)
                : BubbleService.ChaosRabbitTrailSecNow;
            if (trailSec > 0 && _telegraphRemainingMs <= 0 && !_isPopping)
            {
                var nowPx = CenterPx;
                double tdx = nowPx.X - _lastTrailEmitPx.X, tdy = nowPx.Y - _lastTrailEmitPx.Y;
                double trailGap = ChaosSkiaFxOverlay.Enabled ? 22.0 : 40.0;   // Skia particle trail runs denser
                if (tdx * tdx + tdy * tdy >= trailGap * trailGap * _dpiScale * _dpiScale)
                {
                    _lastTrailEmitPx = nowPx;
                    _trailPts.Add((nowPx, DateTime.UtcNow));
                    if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.TrailDot(nowPx, trailSec, sweeperTrail, tdx, tdy);
                    else ChaosFieldFxOverlay.TrailDot(nowPx, trailSec, warm: sweeperTrail);
                }
                var cutoff = DateTime.UtcNow.AddSeconds(-trailSec);
                while (_trailPts.Count > 0 && _trailPts[0].T < cutoff) _trailPts.RemoveAt(0);
                if (_trailPts.Count > 60) _trailPts.RemoveAt(0);   // hard cap, just in case
            }
            else if (_trailPts.Count > 0) _trailPts.Clear();

            // Life burns at the field's pace: in slow-mo a rabbit moves at ~12% speed, so an
            // unscaled clock would expire it mid-screen having barely travelled — it vanished
            // under the player's cursor and read as "rabbits aren't poppable in slow-mo".
            _darterLifeRemainingMs -= 32 * Math.Max(ts, 0.15);
            if (_darterLifeRemainingMs <= 0) { Destroy(); return; }   // safety backstop only
        }
        else
        {
            // Normal travel animation (scaled for 30fps)
            _timeAlive += 0.02;
            var motion = _spec?.Motion ?? ChaosMotion.FloatUp;
            double ts = TimeScale;   // 1 normally; <1 during a darter slow-mo (chaos bubbles only)

            // The Chaperone's escort rides its live: position comes from the orbit, not the
            // motion table — and when the live resolves first, the escort quietly dissolves
            // (no rot penalty, no callback; it was never a free-standing treat).
            if (_spec?.IsEscort == true)
            {
                if (_orbitTarget == null || !_orbitTarget.IsAlive || _orbitTarget.IsPopping)
                {
                    _isPopping = true;
                    _isDissolving = true;
                    return;
                }
                _orbitPhase += 2 * Math.PI * (0.032 / ChaosTuning.CHAPERONE_ORBIT_PERIOD_SEC) * Math.Max(ts, 0.15);
                double tcx = _orbitTarget._posX + _orbitTarget._size / 2.0;
                double tcy = _orbitTarget._posY + _orbitTarget._size / 2.0;
                // The ring must clear the live's rim or the escort hides behind/inside the big
                // bubble and can't be clicked — scale the radius to the pair, constant is a floor.
                double orbitR = Math.Max(ChaosTuning.CHAPERONE_ORBIT_RADIUS_DIP,
                    (_orbitTarget._size + _size) / 2.0 + ChaosTuning.CHAPERONE_ORBIT_GAP_DIP);
                _posX = tcx + Math.Cos(_orbitPhase) * orbitR - _size / 2.0;
                _posY = tcy + Math.Sin(_orbitPhase) * orbitR - _size / 2.0;
                // Faint shimmer link: a spark drifts somewhere on the line between the two.
                if (++_shimmerEmitCounter % 7 == 0)
                {
                    double lt = _random.NextDouble();
                    var sa = CenterPx; var sb = _orbitTarget.CenterPx;
                    var shimmerPt = new Point(sa.X + (sb.X - sa.X) * lt, sa.Y + (sb.Y - sa.Y) * lt);
                    if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.TrailDot(shimmerPt, 0.35);
                    else ChaosFieldFxOverlay.TrailDot(shimmerPt, 0.35);
                }
                goto Visuals;
            }

            // The Tease ignores its motion row: it wiggles in place and leans toward its
            // screen's center over its life — it wants to be seen (and touched). Its own
            // expiry clock runs here; running out pays the DENIED bonus.
            if (_isTease)
            {
                double ccx = (_screenLeft + _screenRight) / 2.0, ccy = (_screenTop + _screenBottom) / 2.0;
                double ddx = ccx - _posX, ddy = ccy - _posY;
                double dd = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (dd > 8)
                {
                    _posX += ddx / dd * ChaosTuning.TEASE_CENTER_PULL_DIP * ts;
                    _posY += ddy / dd * ChaosTuning.TEASE_CENTER_PULL_DIP * ts;
                }
                _posX += Math.Sin(_timeAlive * 9) * 1.6;
                _posY += Math.Cos(_timeAlive * 7) * 1.2;

                _teaseLifeRemainingMs -= 32 * ts;
                if (_teaseLifeRemainingMs <= 0) { Denied(); return; }
                goto Visuals;
            }

            // Horizontal wobble shared by Float/Rain (gives the lively drift)
            double offset = 0;
            switch (_animType)
            {
                case 0: offset = Math.Sin(_timeAlive * 6) * 25;  _angle = (_angle + 0.34) % 360; break;
                case 1: offset = Math.Sin(_timeAlive * 7.5) * 30; _angle = (_angle + 0.14) % 360; break;
                case 2: offset = Math.Cos(_timeAlive * 5.4) * 25; _angle = (_angle - 0.66) % 360; break;
                case 3: offset = Math.Sin(_timeAlive * 3) * 30 + Math.Cos(_timeAlive * 6) * 15; _angle = (_angle + 0.54) % 360; break;
            }

            // The Echo: a doubled thing never floats clean — its drift stutters and skips.
            if (_spec?.IsEcho == true)
            {
                offset += Math.Sin(_timeAlive * 27) * 3.5;
                if (_random.NextDouble() < 0.05) _timeAlive += 0.05;
            }

            bool exited = false;
            switch (motion)
            {
                case ChaosMotion.RainDown:
                    _posY += _speed * ts;
                    _posX = _startX + offset;
                    if (_posY > _screenBottom) exited = true;
                    break;
                case ChaosMotion.RoamBounce:
                    _posX += _vx * ts;
                    _posY += _vy * ts;
                    if (_posX < _screenLeft) { _posX = _screenLeft; _vx = Math.Abs(_vx); }
                    else if (_posX > _screenRight) { _posX = _screenRight; _vx = -Math.Abs(_vx); }
                    double topB = _screenTop + _size + 50, botB = _screenBottom - _size - 50;
                    if (_posY < topB) { _posY = topB; _vy = Math.Abs(_vy); }
                    else if (_posY > botB) { _posY = botB; _vy = -Math.Abs(_vy); }
                    break;
                case ChaosMotion.SideDrift:
                    _posX += _vx * ts;
                    _posY = _startY + offset;   // the Float/Rain wobble reads vertical here
                    if (_vx > 0 ? _posX > _screenRight + _size : _posX < _screenLeft - _size) exited = true;
                    break;
                default: // FloatUp
                    _posY -= _speed * ts;
                    _posX = _startX + offset;
                    if (_posY < _screenTop) exited = true;
                    break;
            }

            // The Pull: the whole field leans toward the cursor (chaos bubbles only; ambient
            // untouched). Cam Girl flips the sign — bubbles FLEE a nearby cursor (and with both
            // active the two simply cancel toward zero: the tug-of-war is the content).
            double pull = BubbleService.ChaosCursorPullNow;
            if (pull > 0 && _spec != null && !_isPopping)
            {
                double pcx = BubbleService.CursorPxX / _dpiScale, pcy = BubbleService.CursorPxY / _dpiScale;
                double pdx = pcx - (_posX + _size / 2.0), pdy = pcy - (_posY + _size / 2.0);
                double pd = Math.Sqrt(pdx * pdx + pdy * pdy);
                if (pd > 30)   // dead zone — no jitter right under the cursor
                {
                    double step = pull * ts;
                    _posX += pdx / pd * step;
                    _posY += pdy / pd * step;
                    _startX += pdx / pd * step;   // Float/Rain re-base X from _startX — keep the drift
                }
            }
            else if (pull < 0 && _spec != null && !_isPopping)
            {
                // Cam Girl: repulsion only bites when the cursor closes in, and fades with distance
                // — bubbles squirm out from under the pointer rather than racing off-screen.
                const double FLEE_RADIUS = 260;
                double fcx = BubbleService.CursorPxX / _dpiScale, fcy = BubbleService.CursorPxY / _dpiScale;
                double fdx = (_posX + _size / 2.0) - fcx, fdy = (_posY + _size / 2.0) - fcy;
                double fd = Math.Sqrt(fdx * fdx + fdy * fdy);
                if (fd < FLEE_RADIUS && fd > 1)
                {
                    double step = -pull * ts * (1.0 - fd / FLEE_RADIUS);
                    // Clamp inside the screen so the flee can't shove a bubble out of reach for good.
                    _posX = Math.Clamp(_posX + fdx / fd * step, _screenLeft, _screenRight);
                    _posY = Math.Clamp(_posY + fdy / fd * step, _screenTop + _size, _screenBottom - _size);
                    _startX = Math.Clamp(_startX + fdx / fd * step, _screenLeft, _screenRight);   // Float/Rain re-base X from _startX
                }
            }

            // The Brittle: thin glass — the cursor ALONE breaks it (no press needed). The arm
            // grace forgives a spawn under the pointer; a frozen field is solid and safe to
            // cross; manual pause never shatters anything. Reach is the NATURAL visual circle
            // (no Wand/Mesmer enlargement), so dodging stays a precision act.
            if (_isBrittle && !_isPopping)
            {
                if (_brittleArmRemainingMs > 0) _brittleArmRemainingMs -= 32;
                else if (!BubbleService.ChaosInputLocked && _isChaosFrozen?.Invoke() != true)
                {
                    double gx = BubbleService.CursorPxX / _dpiScale - (_posX + _size / 2.0);
                    double gy = BubbleService.CursorPxY / _dpiScale - (_posY + _size / 2.0);
                    double reach = _size / 2.0;
                    if (gx * gx + gy * gy <= reach * reach) { Shatter(); return; }
                }
            }

            // Fuse countdown for live chaos bubbles. A defuse channel PAUSES the trance for
            // the hold's duration (releasing early triggers immediately — it never resumes).
            if (_spec != null && _spec.IsLive && _fuseRemainingMs > 0 && !_isChanneling)
            {
                _fuseRemainingMs -= 32 * ts;   // slow-mo makes live fuses last longer
                double frac = Math.Clamp(_fuseRemainingMs / Math.Max(1, _fuseTotalMs), 0, 1);

                // Near-miss danger telegraph (visual only): in the last DANGER_TELEGRAPH_MS the bubble
                // flashes brighter and "breathes" faster, ramping urgency as the fuse empties. The
                // breathing is applied in the visual block below via _dangerFactor; never touches score.
                double dangerMs = Math.Min(_fuseTotalMs, DANGER_TELEGRAPH_MS);
                _dangerFactor = _fuseRemainingMs < dangerMs
                    ? Math.Clamp(1.0 - _fuseRemainingMs / Math.Max(1, dangerMs), 0, 1)
                    : 0.0;

                if (_fuseRing?.RenderTransform is ScaleTransform fst)
                {
                    double rs = 0.45 + 0.55 * frac;
                    fst.ScaleX = rs; fst.ScaleY = rs;
                    if (_fuseStrokeBrush != null)
                    {
                        // Three readable phases: YELLOW (burning, time to spare) →
                        // YELLOW↔RED flash (act now) → SOLID RED (the brink window the
                        // danger power-ups judge). Short fuses compress proportionally.
                        double brinkMs = Math.Min(_fuseTotalMs * 0.30, ChaosTuning.RING_BRINK_MS);
                        double flashMs = Math.Min(_fuseTotalMs * 0.65, ChaosTuning.RING_FLASH_FROM_MS);
                        if (_fuseRemainingMs <= brinkMs)
                        {
                            // Solid red, with the urgency pulse speeding up to detonation.
                            double pulse = 0.5 + 0.5 * Math.Sin(_timeAlive * (18.0 + _dangerFactor * 34.0));
                            _fuseStrokeBrush.Color = Color.FromRgb(255, 45, 45);
                            _fuseRing!.Opacity = 0.65 + 0.35 * pulse;
                        }
                        else if (_fuseRemainingMs <= flashMs)
                        {
                            // Alternate between the yellow and red poles.
                            double f = 0.5 + 0.5 * Math.Sin(_timeAlive * 14.0);
                            _fuseStrokeBrush.Color = Color.FromRgb(255,
                                (byte)(45 + (210 - 45) * f), (byte)(45 + (40 - 45) * f));
                            _fuseRing!.Opacity = 0.8 + 0.2 * f;
                        }
                        else
                        {
                            _fuseStrokeBrush.Color = Color.FromRgb(255, 210, 40);
                            _fuseRing!.Opacity = 1.0;
                        }
                    }
                }
                if (_fuseRemainingMs <= 0) { Detonate(); return; }
            }

            // Treat lifetime: a benign reward bubble only gets so long on screen. It breathes
            // faster over its final stretch (the same danger ramp live fuses use), then
            // dissolves away unpopped — no payload, and the service docks a streak.
            if (_isTreat)
            {
                _treatLifeRemainingMs -= 32 * ts;
                _dangerFactor = _treatLifeRemainingMs < TREAT_FADE_TELEGRAPH_MS
                    ? Math.Clamp(1.0 - _treatLifeRemainingMs / TREAT_FADE_TELEGRAPH_MS, 0, 1)
                    : 0.0;
                if (_treatLifeRemainingMs <= 0) { Dissolve(); return; }
            }

            if (exited)
            {
                if (_spec == null) { _onMiss?.Invoke(this); Destroy(); return; }
                // Chaos: a live bubble that escaped undefused detonates; a benign one just leaves.
                if (_spec.IsLive) { Detonate(); return; }
                // A Brittle drifting off intact = the dodge succeeded — that IS playing it right.
                if (_isBrittle) Chaos.ChaosBubbleHints.MarkLearned("brittle");
                Destroy();
                return;
            }
        }

        // Update visuals - wrapped in try-catch to handle disposed windows gracefully
        // (the escort orbit path jumps straight here — it skips the motion table entirely)
        Visuals:
        try
        {
            // Double-check we're still alive after calculations
            if (_isDestroyed || !_isAlive) return;

            // Update scale wobble (scaled for 30fps). Darters drive their own throb into _scale,
            // so skip the ambient wobble for them to avoid doubling it. Live bubbles in the danger
            // window breathe faster + deeper as _dangerFactor (from the fuse countdown) ramps to 1.
            double breatheFreq = 7.5 + _dangerFactor * 9.0 * _dangerWobbleMult;
            double breatheAmp = 0.06 + _dangerFactor * 0.0225 * _dangerWobbleMult;
            // The Tease pulses deeper than anything else — it's advertising.
            var wobble = _isDarter ? 0.0
                : _isTease ? 0.10 * Math.Sin(_timeAlive * 5.2)
                : breatheAmp * Math.Sin(_timeAlive * breatheFreq + _wobbleOffset);
            // _channelScale: the hold-to-defuse shrink (1.0 idle; eases to CHANNEL_MIN_SCALE
            // over the hold; the deflate animation inherits the shrunken scale seamlessly).
            var currentScale = (_scale + wobble) * _gazeDwellScale * _channelScale;

            if (_bubbleImage.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    st.ScaleX = currentScale;
                    st.ScaleY = currentScale;
                }
                if (tg.Children[1] is RotateTransform rt)
                {
                    rt.Angle = _angle;
                }
            }

            // Freeze aura: pulse a blue halo while the field is frozen, hide it otherwise.
            if (_freezeAura != null)
            {
                if (_spec != null && _isChaosFrozen?.Invoke() == true)
                {
                    _freezePulsePhase += 0.18;
                    _freezeAura.Opacity = 0.1125 + 0.0875 * Math.Sin(_freezePulsePhase);
                }
                else if (_freezeAura.Opacity > 0) _freezeAura.Opacity = 0;
            }

            // The Brittle's cracks: faint while the arm grace runs, then a sharp glassy pulse
            // — the visible tell that hovering is now lethal. Calm (steady, dimmer) while the
            // field is frozen: solid glass, safe to cross.
            if (_brittleCracks != null)
            {
                bool armed = _brittleArmRemainingMs <= 0;
                bool frozenSafe = _spec != null && _isChaosFrozen?.Invoke() == true;
                _brittleCracks.Opacity = !armed ? 0.25
                    : frozenSafe ? 0.45
                    : 0.70 + 0.25 * Math.Sin(_timeAlive * 6);
            }

            // The Tease's glossy shine slides — the cheap shimmer that also stands in for
            // animation when the gif inside fell back to a still (perf budget).
            if (_teaseShine != null)
                _teaseShine.Opacity = 0.16 + 0.14 * (0.5 + 0.5 * Math.Sin(_timeAlive * 5.2));

            // Chaperone shield ring: steady glow while the escort lives, a bright flare on a
            // bounced press, gone the moment the escort pops.
            if (_shieldRing != null)
            {
                if (IsChaperoneShielded)
                {
                    if (_shieldFlashMs > 0) _shieldFlashMs -= 32;
                    _shieldRing.Opacity = _shieldFlashMs > 0 ? 0.95 : 0.45 + 0.10 * Math.Sin(_timeAlive * 4);
                }
                else if (_shieldRing.Opacity > 0) _shieldRing.Opacity = 0;
            }

            // Freeze-impact shudder: jitter the whole window for ~0.2s as the freeze lands.
            double jx = 0, jy = 0;
            if (_spec != null && (_freezeVibrateMsFn?.Invoke() ?? 0) > 0)
            {
                jx = (_random.NextDouble() - 0.5) * 8;
                jy = (_random.NextDouble() - 0.5) * 8;
            }

            // VibePopping buzz: while the button is held, everything the cursor sweeps over pops
            // itself — live ones snap, darters count as catches (slow-mo fires). Same shared
            // cursor sample as the wand shimmer; Pop()'s own guards handle pause-lock and
            // double-pops. Freeze pickups stay manual (triggering the freeze is a timing choice).
            if (BubbleService.VibePopOn && _spec != null && !_isFreeze && !_isPopping
                && (BubbleService.VibeMouseHeld || BubbleService.VibeHoverPops))
            {
                double bx = BubbleService.CursorPxX / _dpiScale - (_posX + _size / 2.0);
                double by = BubbleService.CursorPxY / _dpiScale - (_posY + _size / 2.0);
                double brushReach = _hitSize / 2.0;
                if (bx * bx + by * by <= brushReach * brushReach) Pop();
            }

            // Blindfold: translucent effect bubbles. Magic Wand capstone: bubbles inside your
            // (enlarged) reach shimmer — cursor was sampled once for the whole tick.
            double opacity = _fadeAlpha * _baseOpacity;
            if (BubbleService.WandShimmerOn && _spec != null && !_isDarter && !_isPopping)
            {
                double cx = BubbleService.CursorPxX / _dpiScale, cy = BubbleService.CursorPxY / _dpiScale;
                double dx = cx - (_posX + _size / 2.0), dy = cy - (_posY + _size / 2.0);
                double reach = _hitSize / 2.0;
                if (dx * dx + dy * dy <= reach * reach)
                {
                    _shimmerPhase += 0.35;
                    opacity *= 0.85 + 0.13 * Math.Sin(_shimmerPhase);
                }
            }
            _fxTarget.Opacity = opacity;
            if (_useHost)
            {
                // Cheap Canvas reposition — no SetWindowPos. Grid (_hitSize) centred on the bubble.
                // PHYSICAL px: the host converts by its own origin/scale (mixed-DPI safe).
                ChaosBubbleHostOverlay.Place(_grid, (_posX + _size / 2.0 - _hitSize / 2.0 + jx) * _dpiScale,
                                                    (_posY + _size / 2.0 - _hitSize / 2.0 + jy) * _dpiScale);
            }
            else
            {
                // Keep the fixed-size window centred on the bubble (window side = _winDim, not _size+pad).
                _window!.Left = _posX + _size / 2.0 - _winDim / 2.0 + jx;
                _window.Top = _posY + _size / 2.0 - _winDim / 2.0 + jy;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Bubble animate error: {Error}", ex.Message);
            Destroy();
        }
    }

    public void SetLucky(bool isLucky, bool hasSparkleBoost)
    {
        _isLucky = isLucky;

        // Apply golden glow for lucky pops (skip the expensive blur under the Performance tier;
        // cap the radius otherwise so a burst of lucky pops doesn't stack many 50px blurs).
        var perfTier = PerformanceProfile.CurrentTier;
        if (isLucky && PerformanceProfile.AllowGlow(perfTier))
        {
            try
            {
                _fxTarget.Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00),
                    BlurRadius = Math.Min(50, PerformanceProfile.MaxGlowBlurRadius(perfTier)),
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
            }
            catch { }
        }

        // Spawn sparkle particles if sparkle boost is unlocked
        if (hasSparkleBoost || isLucky)
        {
            SpawnSparkles(isLucky);
        }
    }

    private void SpawnSparkles(bool isGold)
    {
        var count = isGold ? 16 : 8;
        var color = isGold
            ? System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)
            : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);
        var minSize = isGold ? 4.0 : 3.0;
        var maxSize = isGold ? 8.0 : 6.0;

        _sparkles = new List<SparkleParticle>(count);
        var centerX = _size / 2.0;
        var centerY = _size / 2.0;

        for (int i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var speed = 2.0 + _random.NextDouble() * 4.0;
            var size = minSize + _random.NextDouble() * (maxSize - minSize);

            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                IsHitTestVisible = false
            };

            Canvas.SetLeft(ellipse, centerX - size / 2);
            Canvas.SetTop(ellipse, centerY - size / 2);
            _sparkleCanvas.Children.Add(ellipse);

            _sparkles.Add(new SparkleParticle
            {
                X = centerX,
                Y = centerY,
                VelX = Math.Cos(angle) * speed,
                VelY = Math.Sin(angle) * speed,
                Alpha = 1.0,
                Size = size,
                Shape = ellipse
            });
        }
    }

    /// <summary>
    /// Player mouse-down entry (all three click paths route here). Treats, rabbits and freeze
    /// pickups pop/catch on the press exactly as before. LIVE chaos bubbles changed verb
    /// (2026-06-11): a press starts a hold-to-defuse CHANNEL (if the focus gate allows) — the
    /// trance pauses, the bubble shrinks under the hold, and only a completed hold defuses.
    /// A press without the focus to pay triggers the bubble in your grip.
    /// </summary>
    private void OnPlayerPress()
    {
        // Claimed by the companion mid-easter-egg: the user can't pop it out from under her;
        // only her scripted PopByAvatar gets through (see the Pop() guard).
        if (_claimedByAvatar) return;

        // The Tease: ANY mouse-down is the mistake — click or attempted hold, it triggers.
        // (Hovering never counts; this only runs on a real press.)
        if (_isTease)
        {
            if (!_isAlive || _isPopping || BubbleService.ChaosInputLocked) return;
            TouchTease();
            return;
        }

        // The Brittle: a deliberate press is glass under a finger — it shatters exactly like
        // a hover would (the arm grace forgives accidents, not intent; freeze doesn't either).
        if (_isBrittle)
        {
            if (!_isAlive || _isPopping || BubbleService.ChaosInputLocked) return;
            Shatter();
            return;
        }

        // Everything that isn't a live threat keeps the old one-press behavior.
        if (_spec == null || !_spec.IsLive || _isDarter || _isFreeze) { PopByClick(); return; }

        if (!_isAlive || _isPopping || _isChanneling) return;
        if (BubbleService.ChaosInputLocked) return;   // manual pause swallows every press

        // The Chaperone: while the escort circles, clicks and holds just bounce off —
        // shimmer + dull thunk, NO detonation (the one live that forgives a misclick).
        if (IsChaperoneShielded) { BounceOff(); return; }

        if (_canChannelDefuse?.Invoke(this) == false)
        {
            // Intentionally harsh: never touch a live bubble you can't afford. Distinct
            // label here + a distinct cue/flash service-side so the player learns WHY.
            // Red, and raised above the bubble — the detonation's own effect word pops at
            // the centre an instant later, so the two must not overlap.
            ShowChaosLabel("NO FOCUS", Color.FromRgb(0xFF, 0x46, 0x46), yOffsetDip: -(_size / 2.0 + 30));
            _onChannelBroken?.Invoke(this, "nofocus");
            Detonate();
            return;
        }

        _isChanneling = true;
        _channelStartUtc = DateTime.UtcNow;
        _channelStartFuseSec = FuseSecLeft;
        _channelScale = 1.0;
    }

    /// <summary>
    /// Advance the defuse channel one anim tick (~32ms): the hold must stay pressed AND on the
    /// bubble (the enlarged hit ellipse counts, so Silk Touch applies) for the whole duration.
    /// Runs even while the field is frozen — defusing during a freeze is the reward.
    /// </summary>
    private void TickChannel()
    {
        // Manual pause: the hold quietly CANCELS — no detonation, no completion against the
        // wall clock (a paused field can't be farmed OR lost). The trance is still intact and
        // resumes with the run; the player simply presses again.
        if (BubbleService.ChaosInputLocked)
        {
            _isChanneling = false;
            _channelScale = 1.0;
            return;
        }

        double elapsedMs = (DateTime.UtcNow - _channelStartUtc).TotalMilliseconds;

        double cdx = BubbleService.CursorPxX / _dpiScale - (_posX + _size / 2.0);
        double cdy = BubbleService.CursorPxY / _dpiScale - (_posY + _size / 2.0);
        double reach = _hitSize / 2.0;
        bool onBubble = cdx * cdx + cdy * cdy <= reach * reach;

        if (!BubbleService.ChaosMouseHeld || !onBubble)
        {
            // Released early (or the cursor strayed): no partial refund, the fuse doesn't
            // resume — it fires on release. A sub-threshold press reads as a plain click.
            _isChanneling = false;
            _channelScale = 1.0;
            string reason = !BubbleService.ChaosMouseHeld && elapsedMs < ChaosTuning.CLICK_THRESHOLD_MS ? "click" : "release";
            _onChannelBroken?.Invoke(this, reason);
            Detonate();
            return;
        }

        double t = Math.Clamp(elapsedMs / ChaosTuning.DEFUSE_HOLD_MS, 0, 1);
        _channelScale = 1.0 - (1.0 - ChaosTuning.CHANNEL_MIN_SCALE) * t;

        if (elapsedMs >= ChaosTuning.DEFUSE_HOLD_MS)
        {
            _isChanneling = false;
            CompleteDefuse();
        }
    }

    /// <summary>The hold completed: the bubble DEFLATES (limp shrink, no burst) and pays the
    /// full snap. Focus is deducted service-side inside the defuse callback.</summary>
    private void CompleteDefuse()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        _isDeflating = true;
        DefusedViaChannel = true;
        try
        {
            BubbleService.ChaosLastPopXDip = _posX + _size / 2.0;
            BubbleService.ChaosLastPopYDip = _posY + _size / 2.0;
            var popPx = CenterPx;
            BubbleService.ChaosLastPopXPx = popPx.X;
            BubbleService.ChaosLastPopYPx = popPx.Y;
        }
        catch { }
        ShowChaosLabel("SNAP", SnapColor);
        // First-contact verb hints: a completed hold is the lesson (per-variant key). Bound
        // pairs learn on the PAIR clearing (OnBoundHalfResolved) — one half held isn't enough.
        if (_spec != null && !_spec.IsBoundHalf)
            Chaos.ChaosBubbleHints.MarkLearned(Chaos.ChaosBubbleHints.KeyFor(_spec));
        _onDefuse?.Invoke(this);
        _onChainTrigger?.Invoke(this);   // a snap is still a pop — Poppers may sweep the cluster
    }

    /// <summary>Player-click pop for non-live bubbles: pops as usual, then — only if the pop
    /// actually landed (not swallowed by pause-lock, a spank, or a double-pop guard) — tells
    /// the service it was a genuine click. Chain hops, vibe sweeps, gaze and skill pops all
    /// call <see cref="Pop"/> directly, so E-Stim charges never burn on them.</summary>
    private void PopByClick()
    {
        bool wasPopping = _isPopping;
        Pop();
        if (!wasPopping && _isPopping && _spec != null)
        {
            _onClickPop?.Invoke(this);
            // First-contact verb hints: the click landed as intended — lesson learned forever.
            // KeyFor resolves the archetype (rabbit/freeze/chaperone/golden/.../treat:variant);
            // lives never learn here — their lesson is the completed hold (CompleteDefuse).
            if (!_spec.IsLive) Chaos.ChaosBubbleHints.MarkLearned(Chaos.ChaosBubbleHints.KeyFor(_spec));
        }
    }

    public void Pop()
    {
        if (!_isAlive || _isPopping) return;
        // Claimed by the companion: every pop path is suppressed until SHE pops it via
        // PopByAvatar (which sets _avatarPopRequested). Keeps stray sweeps/chains/clicks from
        // firing the effect early while she's gliding over to do it herself.
        if (_claimedByAvatar && !_avatarPopRequested) return;
        // The Tease is immune to every instant-pop source — sweeps, arcs, chain hops, DVD
        // logos, residue, ripples all slide right off it. Only a direct touch (TouchTease)
        // or its expiry (Denied) ends it.
        if (_isTease) return;
        // The Brittle too: toys never hurt the player, so no sweep/arc/chain may break the
        // glass FOR you. Only your own cursor (hover or press → Shatter) ends it.
        if (_isBrittle) return;
        // Manual pause: the field is held AND untouchable — swallow every click path
        // (hit area, window backup, gaze, chain hops) until the run resumes.
        if (_spec != null && BubbleService.ChaosInputLocked) return;
        // The Spanker: rabbits aren't caught anymore — every pop path (click, vibe sweep,
        // chain hop) smacks them into allies instead. No catch, no slow-mo: that's the trade.
        // GG sweepers are NEVER catchable — a click only ever re-smacks them onward.
        if (_spec != null && _isDarter && (BubbleService.ChaosSpankerOnNow || _spec.IsSweeper)) { Spank(); return; }
        // The Chaperone: EVERY pop path bounces off a shielded live — toys, sweeps, DVD logos
        // and chain hops included. They pop the escort if they touch it; then a second
        // application can take the (now-ordinary) live.
        if (_spec != null && IsChaperoneShielded) { BounceOff(); return; }
        // NOTE: frozen bubbles ARE poppable — the freeze is a reward (a calm window to pop/defuse
        // for free while fuses are paused). The pop animation runs even while the field is frozen.
        _isPopping = true;
        if (_spec != null)
        {
            // Anchor for service-side floating text (Blank Eyes pop scores): this bubble's
            // on-screen centre in DIPs, read by ChaosModeService right after the callback.
            try
            {
                BubbleService.ChaosLastPopXDip = _posX + _size / 2.0;
                BubbleService.ChaosLastPopYDip = _posY + _size / 2.0;
                // Physical-px anchor too — droplet bursts / GG rabbits spawn AT the pop.
                var popPx = CenterPx;
                BubbleService.ChaosLastPopXPx = popPx.X;
                BubbleService.ChaosLastPopYPx = popPx.Y;
                // Additive pop burst on the Skia FX layer, in this bubble's payload colour.
                // A live snap reads as a bigger "release"; teases/brittles burst on their own paths.
                if (ChaosSkiaFxOverlay.Enabled)
                    ChaosSkiaFxOverlay.Burst(popPx, _spec.IsLive ? SnapColor : _spec.Tint, _spec.IsLive ? 1.3 : 1.0);
            }
            catch { }
            // Mimic prism: the shadow pop — the copied bubble ghosts out underneath the burst.
            if (_spec.IsPrism && _prismGhost != null)
            {
                try
                {
                    _prismGhost.Opacity = 0.6;
                    _prismGhost.BeginAnimation(UIElement.OpacityProperty,
                        new System.Windows.Media.Animation.DoubleAnimation(0.6, 0, TimeSpan.FromMilliseconds(650)));
                }
                catch { }
            }
            // Chaos bubble: a live bubble clicked in time is a DEFUSE (reward, no payload);
            // a darter caught is its own reward path; a benign bubble is a treat.
            if (_isDarter)
            {
                _wasQuickCatch = _telegraphRemainingMs <= 0 && _darterActiveMs <= _spec.QuickWindowMs;
                ShowChaosLabel("TIME SLOW", _spec.Tint);                                      // catch → fires the slow-mo power-up
                _onDarterCaught?.Invoke(this);
            }
            else if (_isFreeze) { ShowChaosEffectLabel(); _onFreezeCaught?.Invoke(this); }   // good pickup → fires the freeze power-up
            else if (_spec.IsLive) { ShowChaosLabel("SNAP", SnapColor); _onDefuse?.Invoke(this); }  // snapped in time → no effect, but confirm the catch
            else { ShowChaosEffectLabel(); _onBenignPop?.Invoke(this); }                      // treat → its effect fires
            _onChainTrigger?.Invoke(this);   // Chain Reaction boon: let the burst sweep overlapping neighbours
        }
        else
        {
            _onPop?.Invoke(this);
        }
        // Don't call Destroy() here - let AnimateFrame() handle the burst animation.
    }

    /// <summary>The Spanker: smack this rabbit — new heading (+18% pace per smack, capped),
    /// fresh bounces, hot-pink glow, and a ONE-TIME level-scaled swell on the first smack.
    /// From now on its body pops every plain bubble it crosses. Lifetime is untouched,
    /// so it still expires as usual.</summary>
    private void Spank()
    {
        if (_spankCooldownMs > 0) return;
        _spankCooldownMs = 250;
        double spd = Math.Sqrt(_vx * _vx + _vy * _vy);
        if (spd < 0.01) spd = _spec?.DarterSpeed ?? 9.0;
        // Each smack also stings it faster (+18%), capped at ~2.2x its natural pace.
        spd = Math.Min(spd * 1.18, (_spec?.DarterSpeed ?? 9.0) * 2.2);
        double a = _random.NextDouble() * Math.PI * 2;
        _vx = Math.Cos(a) * spd;
        _vy = Math.Sin(a) * spd;
        _darterEscaping = false;
        _darterBounces = 0;
        _darterThrobPunch = 0.35;
        if (!_isSpanked)
        {
            _isSpanked = true;
            // With the Spanker on, rabbits can never be CAUGHT — so the first smack is what
            // counts toward the rabbit_caller lesson (sweepers are born spanked and never land
            // here). Without this, the scripted first accessory made the lesson impossible.
            ChaosLessonHooks.OnRabbitSpanked();
            // The swell happens ONCE, on the first smack (level-scaled); re-smacks only
            // steer and hurry it — no compounding back up to comedy size.
            _spankGrowth = Math.Max(1.0, BubbleService.ChaosSpankGrowNow);
            // It changes color: the chase-glow deepens to a hot ally-pink.
            try { _bubbleImage.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x14, 0x93), BlurRadius = 34, ShadowDepth = 0, Opacity = 1.0 }; } catch { }
            ShowChaosLabel("SPANKED", Color.FromRgb(0xFF, 0x4D, 0xC4));
        }
    }

    /// <summary>the Ripple: the wave flings this rabbit away from the cast point — directed
    /// (unlike a Spank's random heading) and at full sting pace. The flung rabbit is marked
    /// spanked, so its body mows every plain bubble it crosses for the rest of its life.
    /// No rabbit_caller lesson tick — no Spanker was involved, the water threw it.</summary>
    internal void FlingFrom(Point originPx)
    {
        if (!_isAlive || _isPopping) return;
        double spd = (_spec?.DarterSpeed ?? 9.0) * ChaosTuning.RIPPLE_FLING_SPEED_MULT;
        var c = CenterPx;
        double dx = c.X - originPx.X, dy = c.Y - originPx.Y;
        double a = (dx * dx + dy * dy) < 1
            ? _random.NextDouble() * Math.PI * 2   // dead-centre cast: any way out
            : Math.Atan2(dy, dx);
        _vx = Math.Cos(a) * spd;
        _vy = Math.Sin(a) * spd;
        _darterEscaping = false;
        _darterBounces = 0;
        _darterThrobPunch = 0.45;
        _spankCooldownMs = 250;
        if (!_isSpanked)
        {
            _isSpanked = true;
            _spankGrowth = 1.0;   // no Spanker swell — the wave only throws it
            try { _bubbleImage.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x14, 0x93), BlurRadius = 34, ShadowDepth = 0, Opacity = 1.0 }; } catch { }
            ShowChaosLabel("FLUNG", Color.FromRgb(0x7A, 0xE0, 0xFF));
        }
    }

    /// <summary>The Tease was touched: it triggers — payload + streak consequences live in the
    /// service callback. Bursts like a detonation (it IS one, of your own making).</summary>
    private void TouchTease()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        try
        {
            BubbleService.ChaosLastPopXDip = _posX + _size / 2.0;
            BubbleService.ChaosLastPopYDip = _posY + _size / 2.0;
            var px = CenterPx;
            BubbleService.ChaosLastPopXPx = px.X;
            BubbleService.ChaosLastPopYPx = px.Y;
            if (ChaosSkiaFxOverlay.Enabled)
                ChaosSkiaFxOverlay.Burst(px, Color.FromRgb(0xFF, 0x3D, 0x5A), 1.4);   // risk-red detonation
        }
        catch { }
        ShowChaosLabel("✖", Color.FromRgb(0xFF, 0x3D, 0x5A));
        _onTeaseTouched?.Invoke(this);
    }

    /// <summary>The Brittle broke — the cursor brushed (or pressed) the glass. The mimic's
    /// sprite ghosts out underneath (the prism's shadow pop) so the player SEES what was
    /// sealed inside, and the copied live effect fires via the service callback.</summary>
    private void Shatter()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        try
        {
            BubbleService.ChaosLastPopXDip = _posX + _size / 2.0;
            BubbleService.ChaosLastPopYDip = _posY + _size / 2.0;
            var px = CenterPx;
            BubbleService.ChaosLastPopXPx = px.X;
            BubbleService.ChaosLastPopYPx = px.Y;
        }
        catch { }
        if (_prismGhost != null)
        {
            try
            {
                _prismGhost.Opacity = 0.6;
                _prismGhost.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.6, 0, TimeSpan.FromMilliseconds(650)));
            }
            catch { }
        }
        ShowChaosLabel("SHATTER", Color.FromRgb(0xCF, 0xEC, 0xFF));
        _onBrittleShattered?.Invoke(this);
    }

    /// <summary>The Tease expired untouched: restraint pays. A satisfied gold shimmer-out
    /// (the quiet dissolve animation) — the bonus itself lands in the service callback.</summary>
    private void Denied()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        _isDissolving = true;
        ShowChaosLabel("DENIED", Color.FromRgb(0xFF, 0xD7, 0x00));
        Chaos.ChaosBubbleHints.MarkLearned("tease");   // restraint demonstrated — hint retired
        _onTeaseDenied?.Invoke(this);
    }

    /// <summary>The Chaperone's shield: a press/pop arriving while the escort lives bounces off
    /// — shield shimmer + a dull thunk (throttled: sweeps re-touch every frame), nothing else.</summary>
    private void BounceOff()
    {
        _shieldFlashMs = 320;
        var now = DateTime.UtcNow;
        if ((now - _bounceCueUtc).TotalMilliseconds >= 200)
        {
            _bounceCueUtc = now;
            var path = Chaos.ChaosSfx.ResolvePath("shield_thunk");
            if (path.Length == 0) path = Chaos.ChaosSfx.ResolvePath("toy_denied");
            if (path.Length > 0) App.Bubbles?.PlayCue(path, 0.45f);
        }
    }

    /// <summary>A treat ran out its on-screen lifetime: it dissolves away — no pop, no payload.
    /// The service-side callback docks one streak for letting the reward rot.</summary>
    private void Dissolve()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        _isDissolving = true;
        ShowChaosLabel("FADED", Color.FromRgb(0xB8, 0xB8, 0xD0));
        _onTreatExpired?.Invoke(this);
        // Shrink/fade animation + Destroy handled by AnimateFrame().
    }

    /// <summary>Live chaos bubble reached fuse-out / escaped undefused → fire its payload.</summary>
    private void Detonate()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        // Anchor statics like Pop() does — Echo children and split FX spawn AT the detonation.
        try
        {
            BubbleService.ChaosLastPopXDip = _posX + _size / 2.0;
            BubbleService.ChaosLastPopYDip = _posY + _size / 2.0;
            var detPx = CenterPx;
            BubbleService.ChaosLastPopXPx = detPx.X;
            BubbleService.ChaosLastPopYPx = detPx.Y;
        }
        catch { }
        ShowChaosEffectLabel();   // the live effect is firing → flash its color-coded word at the bubble
        _onDetonate?.Invoke(this);
        // Pop/burst animation + Destroy handled by AnimateFrame().
    }

    /// <summary>Cool "safe" accent for the snap (defuse) label — reads as a good outcome, not a threat.</summary>
    private static readonly Color SnapColor = Color.FromRgb(0x7A, 0xE0, 0xFF);

    /// <summary>
    /// Flash the small, color-coded word at this bubble's on-screen spot — the floating "combat
    /// text" clarity label. Effect-name + bubble tint for an effect that fired (benign pop /
    /// freeze catch / detonate); the call sites pass an explicit word/colour for snap + catch.
    /// No-op for variants without a word, or when on-screen Chaos text is off (gated in ChaosPopText).
    /// </summary>
    private void ShowChaosEffectLabel()
    {
        if (_spec == null) return;
        ShowChaosLabel(ChaosBubbleVariants.PopWordFor(_spec.VariantId), _spec.Tint);
    }

    /// <summary>Flash an explicit word + colour at this bubble's centre.</summary>
    private void ShowChaosLabel(string word, Color color, double yOffsetDip = 0)
    {
        if (_spec == null || string.IsNullOrEmpty(word)) return;
        try
        {
            double cx = _posX + _size / 2.0;
            double cy = _posY + _size / 2.0;
            ChaosPopText.Show(cx, cy + yOffsetDip, word, color);
        }
        catch { }
    }

    /// <summary>Builds the chaos-only visual layers (tint, label, fuse ring). No-op for ambient bubbles.</summary>
    private void BuildChaosLayers()
    {
        if (_spec == null) return;

        // Freeze aura — a soft blue halo behind the bubble, hidden (opacity 0) until the field is
        // frozen, then pulsed in AnimateFrame. Every chaos bubble carries one so the whole field
        // glows icy-blue during a freeze. Sits just above the (invisible) hit area, under the art.
        double auraSize = _size + 6;
        _freezeAura = new System.Windows.Shapes.Ellipse
        {
            Width = auraSize, Height = auraSize,
            Fill = _freezeAuraBrush,   // shared frozen icy-blue halo (size varies, brush doesn't)
            IsHitTestVisible = false,
            Opacity = 0
        };
        _grid.Children.Insert(1, _freezeAura);   // index 0 = hit area; image is index 2 after this

        // Tint overlay — radial so the bubble's highlight still reads through. Skipped when a
        // per-variant sprite is supplying its own art.
        if (!_hasVariantSprite)
        {
            var t = _spec.Tint;
            var tintBrush = new RadialGradientBrush(
                Color.FromArgb(150, t.R, t.G, t.B),
                Color.FromArgb(90, t.R, t.G, t.B))
            { GradientOrigin = new Point(0.35, 0.3) };
            _grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = _size, Height = _size,
                Fill = tintBrush,
                IsHitTestVisible = false,
                Opacity = 0.55
            });
        }

        // The Tease: a glossy dark face wearing a gif clipped inside the bubble circle — the
        // bait. Sits over the tint, under the pulsing ✖ label.
        if (_spec.IsTease) BuildTeaseFace();

        // Label / emoji — skipped when a per-variant sprite is present (the sprite carries its
        // own glyph, so drawing the label too would double the icon). Absent a sprite the bubble
        // falls back to bubble.png + tint + this glyph, so the variant stays readable either way.
        if (!_hasVariantSprite && !string.IsNullOrEmpty(_spec.Label))
        {
            _grid.Children.Add(new TextBlock
            {
                Text = _spec.Label,
                Foreground = Brushes.White,
                FontSize = Math.Max(14, _size * 0.30),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Effect = _labelShadow   // shared frozen black shadow
            });
        }

        // Fuse ring (live only) — shrinks as the fuse runs down; colour phases yellow →
        // yellow/red flash → solid red (brink) so defuse timing reads at a glance.
        if (_spec.IsLive)
        {
            _fuseStrokeBrush = new SolidColorBrush(Color.FromRgb(255, 210, 40));
            _fuseRing = new System.Windows.Shapes.Ellipse
            {
                Width = _size, Height = _size,
                Stroke = _fuseStrokeBrush,
                StrokeThickness = 5,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            _grid.Children.Add(_fuseRing);
        }

        // The Echo: a doubled, ghosted outline — the second one is already in there.
        if (_spec.IsEcho)
        {
            var gc = _spec.Tint;
            _grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = _size, Height = _size,
                Stroke = new SolidColorBrush(Color.FromArgb(110, gc.R, gc.G, gc.B)),
                StrokeThickness = 3,
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform(5, 4),
            });
        }

        // The Chaperone's live: a dashed icy ring marks the shield (per-frame opacity in
        // AnimateFrame tracks the escort; a bounced press flares it).
        if (_spec.IsChaperoneLive)
        {
            _shieldRing = new System.Windows.Shapes.Ellipse
            {
                Width = _size + 12, Height = _size + 12,
                Stroke = new SolidColorBrush(Color.FromRgb(0x9C, 0xE8, 0xFF)),
                StrokeThickness = 3.5,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false,
                Opacity = 0.45,
            };
            _grid.Children.Add(_shieldRing);
        }

        // The Brittle: hairline cracks over the glass + a cold glow (the placeholder read until
        // a dedicated sprite ships). The cracks sit faint through the arm grace and sharpen
        // once a hover can break it (AnimateFrame drives _brittleCracks.Opacity).
        if (_spec.IsBrittle)
        {
            _bubbleImage.Effect = new DropShadowEffect
            { Color = Color.FromRgb(0xBF, 0xE6, 0xFF), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.6 };
            _brittleCracks = new Canvas
            {
                Width = _size, Height = _size,
                IsHitTestVisible = false,
                Opacity = 0.25,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var crackBrush = new SolidColorBrush(Color.FromArgb(0xD8, 0xEC, 0xF7, 0xFF));
            crackBrush.Freeze();
            double c = _size / 2.0;
            for (int i = 0; i < 3; i++)
            {
                // Each crack: a jagged 3-segment polyline from near the centre out to the rim.
                double a = _random.NextDouble() * Math.PI * 2;
                double r1 = _size * 0.12, r2 = _size * 0.27, r3 = _size * 0.44;
                double kink1 = a + (_random.NextDouble() - 0.5) * 0.7;
                double kink2 = kink1 + (_random.NextDouble() - 0.5) * 0.7;
                _brittleCracks.Children.Add(new System.Windows.Shapes.Polyline
                {
                    Points = new PointCollection
                    {
                        new Point(c + Math.Cos(a) * r1,     c + Math.Sin(a) * r1),
                        new Point(c + Math.Cos(kink1) * r2, c + Math.Sin(kink1) * r2),
                        new Point(c + Math.Cos(kink2) * r3, c + Math.Sin(kink2) * r3),
                    },
                    Stroke = crackBrush,
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false,
                });
            }
            _grid.Children.Add(_brittleCracks);
        }

        // Golden lucky bubble: a faint warm glow so it reads as treasure on the way past.
        if (_spec.IsGolden)
            _bubbleImage.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 215, 0), BlurRadius = 20, ShadowDepth = 0, Opacity = 0.55 };

        // Mimic prism + the Brittle: pre-build the copied bubble's "shadow pop" ghost — hidden
        // under the art, revealed the instant it pops/shatters so the player SEES whose soul it wore.
        if ((_spec.IsPrism || _spec.IsBrittle) && !string.IsNullOrEmpty(_spec.MimicVariantId))
        {
            var ghostSprite = ChaosArt.Resolve("bubbles", _spec.MimicVariantId);
            if (ghostSprite != null)
            {
                _prismGhost = new Image
                {
                    Source = ghostSprite,
                    Width = _size * 0.9,
                    Height = _size * 0.9,
                    Stretch = Stretch.Uniform,
                    IsHitTestVisible = false,
                    Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 14, 0, 0),   // peeks out from UNDER the burst
                };
                int imgIdx = _grid.Children.IndexOf(_bubbleImage);
                _grid.Children.Insert(Math.Max(0, imgIdx), _prismGhost);
            }
        }

        // Darter: a soft glow so the fast bounce stays trackable, plus a telegraph flare
        // ring (animated down to lock-on in AnimateFrame). Kept within the window bounds.
        if (_spec.IsDarter)
        {
            var tc = Color.FromRgb(_spec.Tint.R, _spec.Tint.G, _spec.Tint.B);
            _bubbleImage.Effect = new DropShadowEffect { Color = tc, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9 };
            _telegraphRing = new System.Windows.Shapes.Ellipse
            {
                Width = _size, Height = _size,
                Stroke = new SolidColorBrush(tc),
                StrokeThickness = 4,
                IsHitTestVisible = false,
                Opacity = 0.85,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.30, 1.30)
            };
            _grid.Children.Add(_telegraphRing);
        }

        // First-contact verb hint: a small pill floating just under the bubble teaching its
        // interaction ("press and hold to snap" / "click to pop" / "do not touch..."), shown
        // only until the player performs that verb correctly ONCE (persisted in chaos_meta).
        // Spanker rabbits skip it — clicking smacks them, the catch lesson doesn't apply.
        _hintKey = Chaos.ChaosBubbleHints.KeyFor(_spec);
        if (_hintKey != null && !Chaos.ChaosBubbleHints.IsLearned(_hintKey)
            && !(_isDarter && BubbleService.ChaosSpankerOnNow))
        {
            string hintText = Chaos.ChaosBubbleHints.TextFor(_spec);
            if (!string.IsNullOrEmpty(hintText))
            {
                // Sits in the window's pop-headroom pad below the bubble; clamp so the pill
                // never clips the window edge on small bubbles (droplets, escorts).
                double yOff = _size / 2.0 + Math.Clamp(_winPad - 24.0, 2.0, 14.0);
                _hintEl = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xA0, 0x12, 0x0A, 0x18)),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 2, 8, 3),
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = new TranslateTransform(0, yOff),
                    Child = new TextBlock
                    {
                        Text = hintText,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE2, 0xF2)),
                        FontSize = 12.5,
                        FontWeight = FontWeights.SemiBold,
                        IsHitTestVisible = false,
                        TextAlignment = TextAlignment.Center,
                        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.9 }
                    }
                };
                _grid.Children.Add(_hintEl);   // Grid doesn't clip — the pill renders in the pad
                _hintEl.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(800))
                    { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            }
        }
    }

    /// <summary>This bubble's verb-hint learned-key, if it's showing a hint right now.</summary>
    internal string? HintKey => _hintEl != null ? _hintKey : null;

    /// <summary>Drop the verb hint (verb learned elsewhere, or this bubble started dying).</summary>
    internal void HideHint()
    {
        if (_hintEl == null) return;
        try
        {
            _hintEl.BeginAnimation(UIElement.OpacityProperty, null);
            _hintEl.Visibility = Visibility.Collapsed;
        }
        catch { }
        _hintEl = null;
    }

    /// <summary>
    /// The Tease's face: a dark glossy circle wearing a clip from the dedicated
    /// <c>EffectiveAssetsPath/teasebubble</c> pool, ellipse-clipped inside the bubble.
    /// Perf-safe by construction (render-thread deadlock history): at most
    /// TEASE_MAX_ANIMATED bubbles run a real XamlAnimatedGif decode at once — the rest
    /// (and oversized gifs) show a display-size still decoded OFF the UI thread and cached
    /// across spawns, with the sliding shine as their shimmer. No pool folder → just the
    /// glossy face + ✖ (the bubble still reads).
    /// </summary>
    private void BuildTeaseFace()
    {
        // No teasebubble clip available → DON'T lay an opaque dark disc over the sprite (that
        // read as a plain black circle when the pool folder was empty). The tease.png sprite
        // already carries the glossy bubble + neon ✖, so it reads fine on its own.
        string? path = PickTeaseGif();
        if (path == null) return;

        double inner = _size * 0.86;
        var face = new Grid
        {
            Width = inner, Height = inner,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Clip = new EllipseGeometry(new Point(inner / 2, inner / 2), inner / 2, inner / 2),
        };
        face.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = inner, Height = inner,
            Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x07, 0x0C)),
            IsHitTestVisible = false,
        });

        {
            var img = new Image
            {
                Width = inner, Height = inner,
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false,
                Opacity = 0.92,
            };
            RenderOptions.SetBitmapScalingMode(img, PerformanceProfile.ScalingMode(PerformanceProfile.CurrentTier));
            bool animate = false;
            bool isGif = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            bool isAnimatedWebp = !isGif
                && path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                && AnimatedWebp.IsAnimated(path);
            if ((isGif || isAnimatedWebp)
                && _teaseAnimatedAlive < PerformanceProfile.MaxAnimatedTeaseBubbles(PerformanceProfile.CurrentTier))
            {
                long len = 0;
                try { len = new FileInfo(path).Length; } catch { }
                animate = len > 0 && len <= ChaosTuning.TEASE_ANIMATED_MAX_BYTES;
            }
            if (animate)
            {
                _teaseAnimated = true;
                _teaseAnimatedAlive++;
                // Both formats loop via a pre-decoded (off-thread, display-size) frame
                // animation — SKCodec is format-agnostic, so GIFs ride the webp pipeline.
                // GIFs used to go through XamlAnimatedGif, which decoded at native res AND
                // was never torn down on Destroy (only _teaseImg gets Detached): every dead
                // GIF tease leaked its animator + full frame buffer and stayed subscribed to
                // CompositionTarget.Rendering (#486). Held in _teaseImg so Destroy can
                // Detach it: the Forever clock pins the Image until cleared.
                _teaseImg = img;
                AnimatedWebp.AttachAnimation(img, path, Math.Max(64, (int)inner));
            }
            else
            {
                BitmapImage? cached = null;
                lock (_teaseStillCache) _teaseStillCache.TryGetValue(path, out cached);
                if (cached != null) img.Source = cached;
                else
                {
                    int decodeWidth = Math.Max(64, (int)inner);
                    string file = path;
                    Task.Run(() =>
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.DecodePixelWidth = decodeWidth;   // decode at display size — cheap
                            bmp.UriSource = new Uri(file, UriKind.Absolute);
                            bmp.EndInit();
                            if (bmp.CanFreeze) bmp.Freeze();
                            lock (_teaseStillCache)
                            {
                                if (_teaseStillCache.Count > 12) _teaseStillCache.Clear();
                                _teaseStillCache[file] = bmp;
                            }
                            Application.Current?.Dispatcher.BeginInvoke(() => { try { img.Source = bmp; } catch { } });
                        }
                        catch { }
                    });
                }
            }
            face.Children.Add(img);
        }

        // Glossy diagonal shine — pulsed per frame; doubles as the still-image shimmer.
        _teaseShine = new System.Windows.Shapes.Ellipse
        {
            Width = inner, Height = inner,
            IsHitTestVisible = false,
            Opacity = 0.22,
            Fill = _teaseShineBrush,   // shared frozen diagonal shine (per-frame shimmer animates Opacity)
        };
        face.Children.Add(_teaseShine);
        _grid.Children.Add(face);
    }

    /// <summary>One random clip from the teasebubble pool folder (listing cached, rescanned
    /// at most every 2 minutes). Null when the folder is absent or empty.</summary>
    private static string? PickTeaseGif()
    {
        try
        {
            var now = DateTime.UtcNow;
            if (_teaseGifPool == null || (now - _teasePoolScanUtc).TotalSeconds > 120)
            {
                _teasePoolScanUtc = now;
                var dir = Path.Combine(App.EffectiveAssetsPath, "teasebubble");
                if (Directory.Exists(dir))
                {
                    var list = new List<string>();
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext is ".gif" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp") list.Add(f);
                    }
                    _teaseGifPool = list.ToArray();
                }
                else _teaseGifPool = Array.Empty<string>();
            }
            return _teaseGifPool.Length == 0 ? null : _teaseGifPool[Random.Shared.Next(_teaseGifPool.Length)];
        }
        catch { return null; }
    }

    /// <summary>
    /// Whether this bubble can currently be popped via Focus Gaze.
    /// False once popping has started or the bubble has been destroyed.
    /// Live chaos bubbles are excluded (2026-06-11 verb rework): defusing is a HELD channel
    /// paid in focus — a gaze dwell would be a free instant snap around the whole economy.
    /// </summary>
    public bool CanGazePop => _isAlive && !_isPopping && !_isDestroyed && _isClickable && !_claimedByAvatar
                              && (_spec == null || (!_spec.IsLive && !_spec.IsTease && !_spec.IsBrittle));

    /// <summary>
    /// Bubble window bounds in WPF DIPs (matches the coordinate space of
    /// WebcamTrackingService.OnGazeMove samples). Returns Rect.Empty when
    /// the window is unavailable.
    /// </summary>
    public Rect GetGazeBounds()
    {
        try
        {
            // Bubble hit area from geometry, NOT the window rect — the window is now a quantized
            // bucket larger than the bubble, so its rect would over-report the gaze target.
            double cx = _posX + _size / 2.0, cy = _posY + _size / 2.0;
            return new Rect(cx - _hitSize / 2.0, cy - _hitSize / 2.0, _hitSize, _hitSize);
        }
        catch
        {
            return Rect.Empty;
        }
    }

    /// <summary>
    /// Drives a small inflate effect during Focus Gaze dwell. t01 is the
    /// dwell progress in [0, 1]; the bubble's render scale is multiplied
    /// by 1 + t01 * 0.25.
    /// </summary>
    public void SetGazeDwellProgress(double t01)
    {
        if (_isPopping || _isDestroyed) return;
        var clamped = Math.Max(0.0, Math.Min(1.0, t01));
        _gazeDwellScale = 1.0 + clamped * 0.25;
    }

    /// <summary>
    /// Force destroy the bubble immediately without animation.
    /// Used during cleanup when animation timer is stopped.
    /// </summary>
    public void ForceDestroy()
    {
        Destroy();
    }

    private void Destroy()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;
        _isAlive = false;

        // Release this tease's animated-gif budget slot (process-wide cap).
        if (_teaseAnimated) { _teaseAnimated = false; _teaseAnimatedAlive = Math.Max(0, _teaseAnimatedAlive - 1); }
        if (_teaseImg != null) { AnimatedWebp.Detach(_teaseImg); _teaseImg = null; }

        // Drop the visual tree + decoded-bitmap references NOW. Window.Close() alone defers
        // teardown to finalization, which lags badly under chaos's rapid spawn/close churn and
        // was half of the OOM leak (see chaos-bubble-oom-leak). Sprites are shared frozen
        // bitmaps from ChaosArt's cache, so nulling here drops only this bubble's edge to the
        // shared bitmap — never the cached singleton. Stop any XamlAnimatedGif animator on the
        // sprite first so it unsubscribes from CompositionTarget.Rendering.
        try
        {
            if (_bubbleImage != null)
            {
                XamlAnimatedGif.AnimationBehavior.SetSourceUri(_bubbleImage, null);
                _bubbleImage.Source = null;
            }
            // Detach the per-bubble window click handler FIRST — it captures this bubble, so
            // leaving it on a recycled pooled window would root this dead bubble forever.
            if (_winClickHandler != null)
            {
                try { _window.MouseLeftButtonDown -= _winClickHandler; } catch { }
                _winClickHandler = null;
            }
            _grid.Children.Clear();
        }
        catch { }

        if (_useHost)
        {
            // Host mode: pull the grid off the shared Canvas — there's no per-bubble window to recycle.
            try { ChaosBubbleHostOverlay.Remove(_grid); } catch { }
        }
        else
        {
            // Recycle the window shell instead of closing it (no per-bubble HWND churn → no
            // finalizer-queue flood → bounded native memory). ReturnWindow hides + resets it, filing it
            // back under its size bucket (_winDim) so the next same-size rental needs no resize.
            ReturnWindow(_window, (int)_winDim);
        }

        // Notify service to remove from list (after animation completed)
        try { _onDestroy?.Invoke(this); } catch { }
    }

    /// <summary>
    /// Re-assert this bubble's window to the very top of the topmost band without stealing
    /// focus. Used when a fullscreen window (e.g. the mandatory chaos video) is raised mid-run
    /// so the player keeps popping bubbles that sit ABOVE the video. Mirrors the HWND_TOPMOST
    /// "kick" pattern used by OverlayService / the video attention targets.
    /// </summary>
    public void BringToFront()
    {
        if (!_isAlive || _isDestroyed || _window == null) return;
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>
    /// Pin the native window onto the screen this bubble was positioned for, in PHYSICAL pixels.
    /// A pooled shell reused across monitors can otherwise flash for one composed frame at its
    /// previous monitor before WPF relocates it under per-monitor DPI. Position + size only (HWND
    /// order/focus untouched). <c>Left</c>/<c>Top</c> were computed as physical÷<see cref="_dpiScale"/>,
    /// so multiplying back yields the intended virtual-desktop physical coordinate.
    /// </summary>
    private void PinWindowToTargetScreen()
    {
        if (_window == null) return;
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int px = (int)Math.Round(_window.Left * _dpiScale);
            int py = (int)Math.Round(_window.Top * _dpiScale);
            int side = (int)Math.Round(_winDim * _dpiScale);
            SetWindowPos(hwnd, IntPtr.Zero, px, py, side, side, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { }
    }

    #region Win32

    internal static double GetDpiForScreen(System.Windows.Forms.Screen screen)
    {
        // Cached per monitor — the value is fixed for a run, and this ran a Win32 round-trip per spawn.
        var key = screen.DeviceName ?? "primary";
        if (s_dpiCache.TryGetValue(key, out var cached)) return cached;
        double dpi = GetDpiForScreenUncached(screen);
        s_dpiCache[key] = dpi;
        return dpi;
    }

    private static double GetDpiForScreenUncached(System.Windows.Forms.Screen screen)
    {
        try
        {
            uint dpiX = 96, dpiY = 96;
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);

            if (hMonitor != IntPtr.Zero)
            {
                var result = GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                if (result == 0)
                {
                    return dpiX / 96.0;
                }
            }

            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private bool _corpseClickThrough;   // set once when the death animation begins
    private Border? _hintEl;            // first-contact verb hint pill (ChaosBubbleHints)
    private string? _hintKey;           // its learned-set key

    /// <summary>Win32-level click-through for a dying bubble's window, so clicks pass to the
    /// live windows beneath it for the rest of the burst/deflate/dissolve animation.</summary>
    private void MakeCorpseClickThrough()
    {
        if (_window == null) return;   // host mode: no per-bubble window; the hook snapshot already
                                       // excludes dying bubbles, so a corpse never intercepts a pop.
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT);
        }
        catch { }
    }

    private void HideFromAltTab()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            // Clear WS_EX_TRANSPARENT first: a recycled pooled shell may have had it set as a
            // corpse (MakeCorpseClickThrough) in its previous life, and a plain OR would leave a
            // now-clickable bubble stuck click-through (unpoppable). Rebuild the ex-style from a
            // known base every (re)show.
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE) & ~WS_EX_TRANSPARENT;
            var flags = exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            // Non-clickable bubbles must be truly click-through at the Win32 level;
            // WPF's IsHitTestVisible alone doesn't prevent the window from eating clicks.
            if (!_isClickable)
                flags |= WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, flags);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    #endregion
}