/* ============================================================================
 * chaosField.js - the DtRH gameplay bubble field (M4: the full menagerie).
 * DOM elements moved by a rAF integrator (translate3d), replacing both the WPF
 * per-bubble windows and the Fall's CSS-animation field. Driven by chaosRun.js
 * through spawn() + callbacks, mirroring the C# BubbleService chaos surface:
 *
 *   - treats pop on pointerdown; lives take a 1000ms HOLD channel (fuse pauses
 *     while held; a tap / early release / stray cursor detonates)
 *   - fuse rings: yellow -> flash (<2.4s) -> solid red (<0.8s brink)
 *   - darters (white rabbits): telegraph -> fast bounce x3 -> exit; catch for
 *     slow-mo (the Spanker turns catches into smacks; sweepers mow the field)
 *   - the Tease (any mouse-down triggers it; denial pays), the Brittle (hover
 *     shatters it), the Echo (handled by the run brain via onDetonated), the
 *     Chaperone (live shielded while its escort orbits), the Bound (tethered
 *     pair; the survivor enrages when the window frays)
 *   - accessories as field physics: The Pull / Cam Girl flee, Poppers chain
 *     reaction, Silk Touch hitbox+magnet, Blindfold dimming, Spanker smacks
 *   - toys' field halves: VibePopping sweep, E-Stim discharges, the DVD logo,
 *     Aftermath residue zones (via fieldFx), Size Queen snap rings
 *   - global timeScale: slow-mo 0.12 (darter catch), freeze 0
 *
 * Pop/chime SFX stay in-page (audioBus); chaos stingers go native from chaosRun.
 * ==========================================================================*/

import { MOTION, RING_FLASH_FROM_MS, RING_BRINK_MS, popWordFor,
  DARTER_SPEED_PXS, DARTER_MAX_BOUNCES, BOUND_WINDOW_MS, BOUND_ENRAGE_SPEED_MULT,
  TEASE_CENTER_PULL, CHAPERONE_ORBIT_RADIUS, CHAPERONE_ORBIT_GAP, CHAPERONE_ORBIT_PERIOD_SEC } from './variants.js';
import { makeSfxPlayer } from '../engine/audioBus.js';
import { getLevel } from '../engine/audioLevels.js';

const SFX_BASE = '/dtrh/assets/bubbles/sfx/';
const POP_SFX = ['Pop.mp3', 'Pop2.mp3', 'Pop3.mp3'];
const CHIME_SFX = ['chime1.mp3', 'chime2.mp3', 'chime3.mp3'];

// ---- verb tuning (ChaosTuning.cs) ----
const DEFUSE_HOLD_MS = 1000;     // hold this long over a live one to snap it
const CLICK_THRESHOLD_MS = 180;  // a faster press+release reads as a CLICK
const CHANNEL_MIN_SCALE = 0.55;  // visual shrink floor while channeling

// ---- motion tuning (self-calibrated for the browser field) ----
const CROSS_SEC_MIN = 8, CROSS_SEC_MAX = 13;   // vertical travellers: screen-cross time
const SIDE_SEC_MIN = 9, SIDE_SEC_MAX = 14;     // side drifters: width-cross time
const ROAM_SPEED_MIN = 90, ROAM_SPEED_MAX = 150; // px/s bouncing drift
const SWAY_AMP_MIN = 18, SWAY_AMP_MAX = 42;    // px horizontal wobble

const rand = (a, b) => a + Math.random() * (b - a);
const pickOf = (arr) => arr[(Math.random() * arr.length) | 0];

// Kinds no toy / ripple / sweep / zone may touch - only the hand (or time) ends them.
const TOY_IMMUNE = new Set(['tease', 'brittle', 'freeze', 'darter']);
// Kinds a chain-reaction burst may take with it (benign rewards only).
const CHAINABLE = new Set(['treat', 'golden', 'droplet', 'heart']);

export function createChaosField({ hud, fx, canChannel, onBenignPopped, onFreezeCaught,
  onDefused, onDetonated, onTreatExpired, onChannelBroken,
  onDarterCaught, onTeaseTouched, onTeaseDenied, onBrittleShattered, onBoundEnraged,
  onRabbitSmacked = null }) {
  const layer = document.createElement('div');
  layer.className = 'cf-layer';
  hud.insertBefore(layer, hud.firstChild); // under HUD chrome, above the canvas + fieldFx

  const fxLayer = document.createElement('div');
  fxLayer.className = 'cf-fx';
  hud.appendChild(fxLayer);

  const audio = makeSfxPlayer();
  audio.preload([...POP_SFX, ...CHIME_SFX].map((f) => SFX_BASE + f));
  const playPop = (vol = 0.25) => audio.play(SFX_BASE + pickOf(POP_SFX), vol * getLevel('fx'));
  const playChime = (vol = 0.3) => audio.play(SFX_BASE + pickOf(CHIME_SFX), vol * getLevel('fx'));

  const live = new Set();
  const dvds = [];       // bouncing logos (Porn DVD toy + Intrusive Thoughts)
  const boundPairs = new Map();   // pairId -> { members:[], windowLeftMs:null }
  let timeScale = 1;     // slow-mo dial; 0 while frozen
  let frozen = false;
  let held = false;      // pause/draft/video-cover: clock + motion + input all stop
  let inputLocked = false;
  let channelSeconds = 0;   // lifetime "time holding on" (banked to meta at run end)

  // ---- accessory physics knobs (set once by the run brain at attach) ----
  const phys = {
    cursorPull: 0,        // px/s toward the cursor (negative = Cam Girl flee wins)
    rabbitHoming: false,
    spanker: false, spankGrow: 1.0,
    chainReach: 1.0,      // Poppers: >1 = pops burst neighbours
    hitboxScale: 1.0, magnet: false,   // Silk Touch
    dimOpacity: 1.0,      // Blindfold: plain bubbles render at this opacity
    residueZones: false,  // Aftermath drafted: poll fx.inResidue per bubble
    rabbitTrailSec: 0,    // Tail-Plug: rabbits drag a popping sparkle trail
    pumpPull: 0,          // The Pump (toy): temporary suction, sweet kinds only
  };
  let vibeOn = false, vibeHover = false;

  const W = () => window.innerWidth, H = () => window.innerHeight;
  let curX = W() / 2, curY = H() / 2;
  window.addEventListener('pointermove', onPointerMove, { passive: true });
  function onPointerMove(e) {
    curX = e.clientX; curY = e.clientY;
    if (vibeOn) fx.vibePoint(curX, curY);
  }

  // ---- float text -----------------------------------------------------------
  function floatText(text, x, y, cls) {
    const el = document.createElement('div');
    el.className = `cf-pop ${cls || ''}`;
    el.textContent = text;
    el.style.left = `${x}px`;
    el.style.top = `${y}px`;
    fxLayer.appendChild(el);
    el.addEventListener('animationend', () => el.remove(), { once: true });
  }

  const isPlain = (b) => b.spec.kind === 'treat' || b.spec.kind === 'live';
  const isShielded = (b) => b.escort && b.escort.state === 'live';

  // ---- spawn ------------------------------------------------------------------
  function spawn(spec) {
    const size = spec.sizePx;
    const wrap = document.createElement('div');
    wrap.className = `cf-bubble cf-bubble--${spec.kind}`;
    if (spec.isEcho) wrap.classList.add('cf-bubble--echo');
    if (spec.isSweeper) wrap.classList.add('cf-bubble--sweeper');
    wrap.style.width = wrap.style.height = `${size}px`;

    const body = document.createElement('div');
    body.className = 'cf-bubble-body';
    if (spec.sprite) body.style.backgroundImage = `url('${spec.sprite}')`;
    else body.style.background = `radial-gradient(circle at 32% 30%, rgba(255,255,255,0.75), ${spec.tint} 58%, rgba(0,0,0,0.35))`;
    body.style.setProperty('--tint', spec.tint);
    if (spec.label) {
      const lab = document.createElement('span');
      lab.className = 'cf-bubble-label';
      lab.textContent = spec.label;
      body.appendChild(lab);
    }
    wrap.appendChild(body);

    // Blindfold: plain bubbles dim to a whisper; pickups stay bright.
    if (phys.dimOpacity < 1 && (spec.kind === 'treat' || spec.kind === 'live')) {
      wrap.style.opacity = String(phys.dimOpacity);
      wrap.dataset.dim = '1';
    }

    let ring = null;
    if (spec.kind === 'live') {
      ring = document.createElement('div');
      ring.className = 'cf-fuse-ring';
      wrap.appendChild(ring);
    }
    const chan = document.createElement('div');
    chan.className = 'cf-chan-ring';
    chan.style.display = 'none';
    wrap.appendChild(chan);

    const b = {
      spec, wrap, body, ring, chan,
      size,
      x: 0, y: 0,
      state: 'live',
      fuseLeft: spec.fuseMs,
      fuseTotal: Math.max(1, spec.fuseMs),
      ttlLeft: spec.treatLifeMs || 0,
      swayT: rand(0, Math.PI * 2),
      swayAmp: rand(SWAY_AMP_MIN, SWAY_AMP_MAX),
      swaySpeed: rand(1.2, 2.2),
      channel: null,          // { elapsed } while a hold is running (real-time ms)
      ringPhase: '',
      // darter state
      telegraphLeft: spec.telegraphMs || 0,
      lifeLeft: spec.lifetimeMs || 0,
      activeAt: 0, bounces: 0, exiting: false,
      smacked: false, sweeper: !!spec.isSweeper,
      // brittle state
      armLeft: spec.armMs || 0,
      // links
      escort: null,           // (on a chaperone live) its orbiting escort
      orbit: null,            // (on the escort) { host, angle }
      enraged: false,
    };

    // motion setup
    const m = spec.motion;
    if (m === MOTION.FloatUp || m === MOTION.RainDown) {
      b.baseX = rand(0.03, 0.88) * W();
      b.x = b.baseX;
      b.vy = (H() + size * 2) / rand(CROSS_SEC_MIN, CROSS_SEC_MAX) * spec.speedMult * (m === MOTION.FloatUp ? -1 : 1);
      b.y = m === MOTION.FloatUp ? H() + size * 0.2 : -size * 1.2;
    } else if (m === MOTION.SideDrift) {
      const fromLeft = Math.random() < 0.5;
      b.vx = (W() + size * 2) / rand(SIDE_SEC_MIN, SIDE_SEC_MAX) * spec.speedMult * (fromLeft ? 1 : -1);
      b.x = fromLeft ? -size : W();
      b.baseY = rand(0.12, 0.72) * H();
      b.y = b.baseY;
    } else { // RoamBounce
      b.x = rand(0.1, 0.8) * W();
      b.y = rand(0.1, 0.7) * H();
      const a = rand(0, Math.PI * 2);
      const sp = rand(ROAM_SPEED_MIN, ROAM_SPEED_MAX) * spec.speedMult;
      b.vx = Math.cos(a) * sp;
      b.vy = Math.sin(a) * sp;
    }
    // Pinned spawns (echo children, droplets, called rabbits) materialise in place.
    if (spec.spawnAt) {
      b.x = Math.min(W() - size / 2, Math.max(size / 2, spec.spawnAt.x));
      b.y = Math.min(H() - size / 2, Math.max(size / 2, spec.spawnAt.y));
      b.baseX = b.x;
      b.baseY = b.y;
    }
    // Darters idle at their telegraph point until the flare ends.
    if (spec.kind === 'darter') {
      if (!spec.spawnAt) { b.x = rand(0.15, 0.85) * W(); b.y = rand(0.15, 0.8) * H(); }
      b.vx = 0; b.vy = 0;
      wrap.classList.add('is-telegraph');
    }
    // The Tease roams gently; the center pull happens per frame.
    if (spec.kind === 'tease') {
      const a = rand(0, Math.PI * 2);
      b.vx = Math.cos(a) * 40; b.vy = Math.sin(a) * 40;
    }
    place(b);

    // Bound pair bookkeeping + a loose side-by-side start.
    if (spec.boundPairId != null) {
      let pair = boundPairs.get(spec.boundPairId);
      if (!pair) { pair = { members: [], windowLeftMs: null }; boundPairs.set(spec.boundPairId, pair); }
      pair.members.push(b);
      if (pair.members.length === 2) {
        const a0 = pair.members[0];
        b.x = Math.min(W() - size, Math.max(size, a0.x + 250));
        b.y = a0.y;
        // loosely mirrored drift
        b.vx = -a0.vx; b.vy = a0.vy;
        place(b);
      }
    }

    // ---- input ---------------------------------------------------------------
    wrap.addEventListener('pointerdown', (e) => {
      if (e.button != null && e.button !== 0) return;
      e.preventDefault();
      e.stopPropagation();
      pressOn(b, e.clientX, e.clientY);
    });
    wrap.addEventListener('pointerup', () => endChannel(b, false));
    wrap.addEventListener('pointercancel', () => endChannel(b, false));
    wrap.addEventListener('pointerleave', () => endChannel(b, false));
    // Hover verbs: the Brittle shatters at a touch of the cursor; a running
    // vibe buzz pops everything brushed.
    wrap.addEventListener('pointerenter', () => {
      if (b.state !== 'live' || held || inputLocked) return;
      if (b.spec.kind === 'brittle') { tryShatter(b); return; }
      if (vibeOn && (vibeHover || anyButtonDown)) sweepPop(b, 'vibe');
    });
    wrap.addEventListener('pointermove', () => {
      if (vibeOn && b.state === 'live' && !held && !inputLocked
          && b.spec.kind !== 'brittle' && (vibeHover || anyButtonDown)) sweepPop(b, 'vibe');
    });

    live.add(b);
    layer.appendChild(wrap);
    return b;
  }

  let anyButtonDown = false;
  window.addEventListener('pointerdown', (e) => { if (e.button === 0 || e.button === 2) anyButtonDown = true; }, true);
  window.addEventListener('pointerup', () => { anyButtonDown = false; }, true);

  /** Route a primary press on a bubble to its verb. */
  function pressOn(b, x, y) {
    if (b.state !== 'live' || held || inputLocked) return;
    const k = b.spec.kind;
    if (k === 'tease') {
      // Any mouse-down triggers it - that's the whole trap.
      b.state = 'popped'; live.delete(b);
      b.wrap.classList.add('is-detonate');
      b.wrap.style.pointerEvents = 'none';
      window.setTimeout(() => b.wrap.remove(), 700);
      try { onTeaseTouched(b.spec); } catch (err) { /* ignore */ }
      return;
    }
    if (k === 'brittle') { tryShatter(b); return; }
    if (k === 'darter') {
      if (b.telegraphLeft > 0 || b.sweeper) return;   // not active yet / sweepers can't be caught
      if (phys.spanker) { smack(b, x, y); return; }
      const quick = (performance.now() - b.activeAt) <= (b.spec.quickWindowMs || 500);
      popVisual(b, 0.3);
      floatText(quick ? 'QUICK!' : '🐇', x, y, 'cf-pop--freeze');
      try { onDarterCaught(b.spec, quick); } catch (err) { /* ignore */ }
      return;
    }
    if (k === 'live') {
      if (isShielded(b)) {
        // The Chaperone holds: every pop path bounces off while the escort lives.
        b.wrap.classList.remove('is-shield-bounce');
        void b.wrap.offsetWidth;
        b.wrap.classList.add('is-shield-bounce');
        floatText('SHIELDED', b.x, b.y - b.size * 0.4, 'cf-pop--word');
        return;
      }
      if (canChannel(b.spec)) beginChannel(b);
      else { try { onChannelBroken(b.spec, 'nofocus'); } catch (err) { /* ignore */ } detonate(b); }
      return;
    }
    popBenign(b, x, y, 'pointer');
  }

  // Silk Touch: presses that land NEAR a bubble still count - the layer-level
  // fallback grows every hitbox by phys.hitboxScale and lets a near-miss on a
  // live one start the channel (magnet).
  layer.addEventListener('pointerdown', (e) => {
    if (e.button != null && e.button !== 0) return;
    if (held || inputLocked) return;
    if (phys.hitboxScale <= 1 && !phys.magnet) return;
    if (e.target !== layer) return;   // a real bubble already took it
    let best = null, bestD = Infinity;
    for (const b of live) {
      if (b.state !== 'live') continue;
      const dx = b.x - e.clientX, dy = b.y - e.clientY;
      const d = Math.hypot(dx, dy);
      const reach = (b.size / 2) * Math.max(phys.hitboxScale, phys.magnet && b.spec.kind === 'live' ? 1.35 : 1);
      if (d <= reach && d < bestD) { best = b; bestD = d; }
    }
    if (best) { e.preventDefault(); pressOn(best, e.clientX, e.clientY); }
  });

  function place(b) {
    b.wrap.style.transform = `translate3d(${b.x - b.size / 2}px, ${b.y - b.size / 2}px, 0)`;
  }

  // ---- verbs ---------------------------------------------------------------------
  // No pointer capture: the cursor straying off the bubble must fire pointerleave
  // (capture would swallow it) - that stray IS the "you let go" break, like C#.
  function beginChannel(b) {
    b.channel = { elapsed: 0 };
    b.chan.style.display = '';
    b.wrap.classList.add('is-channeling');
  }

  function endChannel(b, complete) {
    if (!b.channel || b.state !== 'live') return;
    const ms = b.channel.elapsed;
    b.channel = null;
    b.chan.style.display = 'none';
    b.wrap.classList.remove('is-channeling');
    b.body.style.transform = '';
    if (complete || held) return; // defuse() already ran / paused field lets go clean
    try { onChannelBroken(b.spec, ms < CLICK_THRESHOLD_MS ? 'click' : 'release'); } catch (err) { /* ignore */ }
    detonate(b);
  }

  // Sparkle burst (ported from the WPF BubbleService pop): shards fly outward
  // from the pop point with a little gravity, shrinking + fading (CSS-driven).
  function sparkleBurst(x, y, kind) {
    const rich = kind === 'golden' || kind === 'droplet' || kind === 'heart' || kind === 'prism';
    const n = rich ? 13 : 9;
    const color = rich ? '#ffe27a' : '#ffd9ef';
    for (let i = 0; i < n; i++) {
      const p = document.createElement('div');
      p.className = 'cf-spark';
      const ang = (Math.PI * 2 * i) / n + rand(-0.35, 0.35);
      const dist = rand(40, 108) * (rich ? 1.25 : 1);
      p.style.left = `${x}px`;
      p.style.top = `${y}px`;
      p.style.color = color;
      p.style.setProperty('--dx', `${Math.cos(ang) * dist}px`);
      p.style.setProperty('--dy', `${Math.sin(ang) * dist}px`);
      p.style.setProperty('--fall', `${rand(28, 64)}px`);
      fxLayer.appendChild(p);
      p.addEventListener('animationend', () => p.remove(), { once: true });
    }
  }

  /** Drift-off: fade out in place instead of blinking off the screen edge. No
   * callbacks (the bubble simply left the field), matched to cfFadeOut's length. */
  function fadeOut(b) {
    if (b.state !== 'live') return;
    b.state = 'popped';
    live.delete(b);
    unlink(b);
    b.wrap.classList.add('is-fade');
    b.wrap.style.pointerEvents = 'none';
    window.setTimeout(() => b.wrap.remove(), 1040);
  }

  function popVisual(b, sfxVol = 0.25) {
    b.state = 'popped';
    live.delete(b);
    unlink(b);
    b.wrap.classList.add('is-pop');
    b.wrap.style.pointerEvents = 'none';
    sparkleBurst(b.x, b.y, b.spec.kind);
    playPop(sfxVol);
    b.wrap.addEventListener('animationend', () => b.wrap.remove(), { once: true });
    window.setTimeout(() => b.wrap.remove(), 600); // belt-and-suspenders
  }

  function popBenign(b, x, y, src = 'pointer') {
    if (b.state !== 'live') return;
    const word = popWordFor(b.spec.variantId);
    popVisual(b);
    if (b.spec.kind === 'freeze') {
      floatText(word, x, y, 'cf-pop--freeze');
      try { onFreezeCaught(b.spec); } catch (err) { /* ignore */ }
      return;
    }
    if (word) floatText(word, x, y, b.spec.kind === 'golden' || b.spec.kind === 'droplet' ? 'cf-pop--gold' : 'cf-pop--word');
    try { onBenignPopped(b.spec, x, y, src); } catch (err) { /* ignore */ }
    // Poppers: the burst ripples outward through whatever it overlaps.
    if (phys.chainReach > 1) chainFrom(b);
  }

  function chainFrom(origin) {
    for (const n of Array.from(live)) {
      if (n.state !== 'live' || !CHAINABLE.has(n.spec.kind)) continue;
      const dx = n.x - origin.x, dy = n.y - origin.y;
      const reach = ((n.size + origin.size) / 2) * phys.chainReach * 0.5 + origin.size / 2;
      if (dx * dx + dy * dy <= reach * reach) {
        window.setTimeout(() => { if (n.state === 'live') popBenign(n, n.x, n.y, 'chain'); }, 70);
      }
    }
  }

  /** VibePopping sweep / hover: everything brushed pops; live ones snap clean. */
  function sweepPop(b, src) {
    if (b.state !== 'live' || TOY_IMMUNE.has(b.spec.kind)) return;
    if (b.spec.kind === 'live') { if (!isShielded(b)) defuse(b, false); }
    else popBenign(b, b.x, b.y, src);
  }

  function defuse(b, viaChannel) {
    if (b.state !== 'live') return;
    const fuseSecLeft = Math.max(0, b.fuseLeft) / 1000;
    const bx = b.x, by = b.y;
    if (b.channel) { b.channel = null; endChannelVisual(b); }
    popVisual(b, 0.2);
    floatText('SNAP', bx, by - b.size * 0.2, 'cf-pop--snap');
    noteBoundDefused(b);
    try { onDefused(b.spec, fuseSecLeft, viaChannel, bx, by); } catch (err) { /* ignore */ }
  }
  function endChannelVisual(b) {
    b.chan.style.display = 'none';
    b.wrap.classList.remove('is-channeling');
    b.body.style.transform = '';
  }

  function detonate(b) {
    if (b.state !== 'live') return;
    b.state = 'popped';
    live.delete(b);
    if (b.channel) { b.channel = null; endChannelVisual(b); }
    unlink(b);
    noteBoundTriggered(b);
    b.wrap.classList.add('is-detonate');
    b.wrap.style.pointerEvents = 'none';
    b.wrap.addEventListener('animationend', () => b.wrap.remove(), { once: true });
    window.setTimeout(() => b.wrap.remove(), 700);
    const word = popWordFor(b.spec.variantId);
    if (word) floatText(word, b.x, b.y - b.size * 0.2, 'cf-pop--bad');
    try { onDetonated(b.spec, b.x, b.y); } catch (err) { /* ignore */ }
  }

  function expire(b) {
    if (b.state !== 'live') return;
    b.state = 'popped';
    live.delete(b);
    unlink(b);
    b.wrap.classList.add('is-fade');
    b.wrap.style.pointerEvents = 'none';
    window.setTimeout(() => b.wrap.remove(), 1040);
    try { onTreatExpired(b.spec); } catch (err) { /* ignore */ }
  }

  /** Remove silently: exits, dissolving escorts, teardown - no callbacks. */
  function vanish(b) {
    if (b.state !== 'live') return;
    b.state = 'popped';
    live.delete(b);
    unlink(b);
    b.wrap.remove();
  }

  // ---- the Brittle -----------------------------------------------------------------
  function tryShatter(b) {
    if (b.state !== 'live' || b.armLeft > 0) return;
    if (frozen || held || inputLocked) return;   // a frozen field is safe to cross
    b.state = 'popped';
    live.delete(b);
    b.wrap.classList.add('is-shatter');
    b.wrap.style.pointerEvents = 'none';
    window.setTimeout(() => b.wrap.remove(), 700);
    floatText(popWordFor('brittle'), b.x, b.y - b.size * 0.2, 'cf-pop--bad');
    try { onBrittleShattered(b.spec); } catch (err) { /* ignore */ }
  }

  // ---- the Chaperone -----------------------------------------------------------------
  /** Link a spawned live + escort (called by the run brain right after both spawn()s). */
  function linkChaperone(liveB, escortB) {
    liveB.escort = escortB;
    liveB.wrap.classList.add('is-shielded');
    escortB.orbit = { host: liveB, angle: rand(0, Math.PI * 2) };
  }

  function unlink(b) {
    // Escort resolved -> the live is alone now, a standard defusable bubble.
    if (b.orbit && b.orbit.host && b.orbit.host.escort === b) {
      b.orbit.host.escort = null;
      b.orbit.host.wrap.classList.remove('is-shielded');
    }
    // Live resolved first -> its escort dissolves quietly.
    if (b.escort && b.escort.state === 'live') {
      const e = b.escort;
      b.escort = null;
      e.wrap.classList.add('is-fade');
      window.setTimeout(() => vanish(e), 300);
    }
  }

  // ---- the Bound -----------------------------------------------------------------
  function pairOf(b) { return b.spec.boundPairId != null ? boundPairs.get(b.spec.boundPairId) : null; }

  function noteBoundDefused(b) {
    const pair = pairOf(b);
    if (!pair) return;
    const other = pair.members.find((m) => m !== b);
    if (other && other.state === 'live') pair.windowLeftMs = BOUND_WINDOW_MS;   // the clock starts NOW
    else boundPairs.delete(b.spec.boundPairId);                                // both down - clean
  }

  function noteBoundTriggered(b) {
    const pair = pairOf(b);
    if (!pair) return;
    const other = pair.members.find((m) => m !== b);
    if (other && other.state === 'live') enrage(other);
    boundPairs.delete(b.spec.boundPairId);
  }

  function enrage(b) {
    if (b.enraged || b.state !== 'live') return;
    b.enraged = true;
    b.fuseLeft = Math.max(600, b.fuseLeft / 2);
    b.vx = (b.vx || 20) * BOUND_ENRAGE_SPEED_MULT;
    b.vy = (b.vy || 20) * BOUND_ENRAGE_SPEED_MULT;
    b.wrap.classList.add('is-enraged');
    try { onBoundEnraged(b.spec); } catch (err) { /* ignore */ }
  }

  // ---- the Spanker -----------------------------------------------------------------
  function smack(b, x, y) {
    // Turn it away from the hand, swell it once, speed it up - and now it mows.
    const dx = b.x - x, dy = b.y - y;
    const d = Math.max(1, Math.hypot(dx, dy));
    const sp = Math.max(120, Math.hypot(b.vx, b.vy)) * 1.18;
    b.vx = (dx / d) * sp;
    b.vy = (dy / d) * sp;
    if (onRabbitSmacked) { try { onRabbitSmacked(!b.smacked); } catch (err) { /* ignore */ } }
    if (!b.smacked) {
      b.smacked = true;
      const grow = Math.min(3.0, phys.spankGrow);
      b.size *= grow;
      b.wrap.style.width = b.wrap.style.height = `${b.size}px`;
    }
    b.sweeper = true;
    b.bounces = 0;   // a smacked rabbit gets its bounces back
    b.wrap.classList.add('cf-bubble--sweeper');
    floatText('SMACK', x, y, 'cf-pop--word');
    playPop(0.3);
  }

  // ---- E-Stim -----------------------------------------------------------------
  /** Discharge lightning from (x,y) into up to maxTargets bubbles within reach.
   * Treats pop paid, lives snap clean. chain = the capstone: struck bubbles
   * re-discharge onward. Returns how many were struck (0 = the charge keeps). */
  function dischargeAt(x, y, { maxTargets = 3, reach = 600, chain = false, depth = 0 } = {}) {
    if (depth > 6) return 0;
    const targets = [];
    for (const b of live) {
      if (b.state !== 'live' || TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) continue;
      const dx = b.x - x, dy = b.y - y;
      const d = Math.hypot(dx, dy);
      if (d <= reach && d > 4) targets.push({ b, d });
    }
    targets.sort((a, z) => a.d - z.d);
    const struck = targets.slice(0, maxTargets);
    struck.forEach(({ b }, i) => {
      fx.addBolt(x, y, b.x, b.y);
      window.setTimeout(() => {
        if (b.state !== 'live') return;
        const bx = b.x, by = b.y;
        if (b.spec.kind === 'live') defuse(b, false);
        else popBenign(b, bx, by, 'estim');
        if (chain) dischargeAt(bx, by, { maxTargets: 3, reach: reach * 0.8, chain, depth: depth + 1 });
      }, 60 + i * 70);
    });
    return struck.length;
  }

  // ---- the DVD logo (Porn DVD toy + Intrusive Thoughts) ------------------------------
  function launchDvd({ durationSec = 10, speed = 1.0, scale = 1.0, count = 1, splitBounces = 0, text = null } = {}) {
    for (let i = 0; i < count; i++) spawnDvd({ durationSec, speed, scale, splitBounces, text });
  }

  function spawnDvd({ durationSec, speed, scale, splitBounces, text, x = null, y = null }) {
    const el = document.createElement('div');
    el.className = text ? 'cf-dvd cf-dvd--text' : 'cf-dvd';
    if (text) el.textContent = text;
    else {
      const img = document.createElement('img');
      img.src = 'https://ccp.art/announce/dvd.png';
      img.alt = '';
      img.addEventListener('error', () => { el.textContent = '📀 PORN DVD'; el.classList.add('cf-dvd--text'); img.remove(); });
      el.appendChild(img);
    }
    el.style.setProperty('--dvdscale', String(scale));
    layer.appendChild(el);
    const w = 190 * scale, h = 90 * scale;
    const a = rand(0.4, 0.9) * (Math.random() < 0.5 ? 1 : -1);
    const sp = 320 * speed;
    const d = {
      el, w, h,
      x: x != null ? x : rand(0.2, 0.8) * W(),
      y: y != null ? y : rand(0.2, 0.7) * H(),
      vx: Math.cos(a) * sp, vy: Math.sin(a) * sp,
      lifeLeft: durationSec, bounces: 0, splitsLeft: splitBounces,
      speed, scale, text,
    };
    dvds.push(d);
    return d;
  }

  function updateDvds(ts) {
    if (!dvds.length) return;
    const w = W(), h = H();
    for (let i = dvds.length - 1; i >= 0; i--) {
      const d = dvds[i];
      d.lifeLeft -= ts;
      if (d.lifeLeft <= 0) { d.el.remove(); dvds.splice(i, 1); continue; }
      d.x += d.vx * ts;
      d.y += d.vy * ts;
      let bounced = false;
      if (d.x < d.w / 2) { d.x = d.w / 2; d.vx = Math.abs(d.vx); bounced = true; }
      else if (d.x > w - d.w / 2) { d.x = w - d.w / 2; d.vx = -Math.abs(d.vx); bounced = true; }
      if (d.y < d.h / 2) { d.y = d.h / 2; d.vy = Math.abs(d.vy); bounced = true; }
      else if (d.y > h - d.h / 2) { d.y = h - d.h / 2; d.vy = -Math.abs(d.vy); bounced = true; }
      if (bounced) {
        d.bounces++;
        // Casting Couch: the logo splits on its first bounces - one becomes two, then four.
        if (d.splitsLeft > 0) {
          d.splitsLeft--;
          spawnDvd({ durationSec: d.lifeLeft, speed: d.speed, scale: d.scale, splitBounces: 0, text: d.text, x: d.x, y: d.y });
        }
      }
      d.el.style.transform = `translate3d(${d.x - d.w / 2}px, ${d.y - d.h / 2}px, 0)`;
      // Mow: treats pop, lives snap, immune kinds slide off.
      for (const b of Array.from(live)) {
        if (b.state !== 'live' || TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) continue;
        if (Math.abs(b.x - d.x) < (d.w + b.size) / 2 && Math.abs(b.y - d.y) < (d.h + b.size) / 2) {
          if (b.spec.kind === 'live') defuse(b, false);
          else popBenign(b, b.x, b.y, 'dvd');
        }
      }
    }
  }

  const dvdActive = () => dvds.length > 0;

  // ---- the Ripple ------------------------------------------------------------------
  /** Any live bubble within reach of (x,y)? The cast is only offered near the field. */
  function nearAny(x, y, reach) {
    for (const b of live) {
      const dx = b.x - x, dy = b.y - y;
      if (dx * dx + dy * dy <= reach * reach) return true;
    }
    return false;
  }

  /** Expanding wavefront: treats pop PAID, lives snap clean (no focus cost).
   * treatsOnly = the Size Queen snap ring. Tease/Brittle/freeze/rabbits stay
   * cursor-only; hits land as the ring passes each bubble. */
  function castRipple(x, y, radiusPx, lifeMs, { treatsOnly = false } = {}) {
    const ring = document.createElement('div');
    ring.className = 'cf-ripple';
    ring.style.left = `${x}px`;
    ring.style.top = `${y}px`;
    ring.style.setProperty('--r', `${radiusPx}px`);
    ring.style.setProperty('--life', `${lifeMs}ms`);
    fxLayer.appendChild(ring);
    ring.addEventListener('animationend', () => ring.remove(), { once: true });

    const hits = [];
    for (const b of live) {
      if (TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) continue;
      if (treatsOnly && b.spec.kind === 'live') continue;
      const dx = b.x - x, dy = b.y - y;
      const d = Math.hypot(dx, dy);
      if (d <= radiusPx + b.size / 2) hits.push({ b, at: Math.max(0, (d / radiusPx)) * lifeMs });
    }
    for (const h of hits) {
      window.setTimeout(() => {
        if (h.b.state !== 'live') return;
        if (h.b.spec.kind === 'live') defuse(h.b, false);
        else popBenign(h.b, h.b.x, h.b.y, 'ripple');
      }, h.at);
    }
    return hits.length;
  }

  // ---- per-frame integration ---------------------------------------------------------
  function update(dt) {
    if (held) { fx.draw(dt); return; }
    const ts = dt * timeScale;
    const w = W(), h = H();
    const tethers = [];

    for (const b of Array.from(live)) {
      // channel runs on REAL time (a frozen field still channels - that's the
      // freeze's reward); the fuse holds while the hand is on it either way.
      if (b.channel) {
        b.channel.elapsed += dt * 1000;
        channelSeconds += dt;
        const p = Math.min(1, b.channel.elapsed / DEFUSE_HOLD_MS);
        b.chan.style.setProperty('--cdeg', `${p * 360}`);
        b.body.style.transform = `scale(${1 - (1 - CHANNEL_MIN_SCALE) * p})`;
        if (p >= 1) { defuse(b, true); }
        continue;   // the grip holds it still: no motion, no fuse, no rot
      } else if (b.spec.kind === 'live' && b.fuseLeft > 0) {
        b.fuseLeft -= ts * 1000;
        if (b.fuseLeft <= 0) { detonate(b); continue; }
      }

      // fuse ring phases
      if (b.ring && b.spec.kind === 'live') {
        const frac = Math.max(0, b.fuseLeft) / b.fuseTotal;
        b.ring.style.setProperty('--deg', `${frac * 360}`);
        const phase = b.fuseLeft <= RING_BRINK_MS ? 'brink'
          : b.fuseLeft <= RING_FLASH_FROM_MS ? 'flash' : 'burn';
        if (phase !== b.ringPhase) {
          b.ringPhase = phase;
          b.ring.classList.toggle('is-flash', phase === 'flash');
          b.ring.classList.toggle('is-brink', phase === 'brink');
          b.wrap.classList.toggle('is-brink', phase === 'brink');
        }
      }

      // treat rot (the Tease's TTL is its denial clock)
      if (b.ttlLeft > 0) {
        b.ttlLeft -= ts * 1000;
        if (b.ttlLeft <= 0) {
          if (b.spec.kind === 'tease') {
            b.state = 'popped'; live.delete(b);
            b.wrap.classList.add('is-fade');
            window.setTimeout(() => b.wrap.remove(), 1040);
            floatText('DENIED', b.x, b.y - b.size * 0.2, 'cf-pop--gold');
            try { onTeaseDenied(b.spec); } catch (err) { /* ignore */ }
          } else expire(b);
          continue;
        }
        if (b.ttlLeft < 1200 && b.spec.kind !== 'tease') b.wrap.style.opacity = String(Math.max(0.25, b.ttlLeft / 1200) * (b.wrap.dataset.dim ? phys.dimOpacity : 1));
      }

      // brittle arming
      if (b.armLeft > 0) { b.armLeft -= dt * 1000; if (b.armLeft <= 0) b.wrap.classList.add('is-armed'); }

      // Aftermath residue: the crackle pops whatever drifts through.
      if (phys.residueZones && !TOY_IMMUNE.has(b.spec.kind) && !isShielded(b) && fx.inResidue(b.x, b.y)) {
        if (b.spec.kind === 'live') { defuse(b, false); continue; }
        popBenign(b, b.x, b.y, 'zone');
        continue;
      }

      // ---- motion ----
      const k = b.spec.kind;
      if (k === 'darter') {
        if (!updateDarter(b, dt, ts, w, h)) continue;
        place(b);
        continue;
      }
      if (b.orbit) {   // chaperone escort rides its live
        const host = b.orbit.host;
        if (host.state !== 'live') { vanish(b); continue; }
        b.orbit.angle += (Math.PI * 2 / CHAPERONE_ORBIT_PERIOD_SEC) * ts;
        const r = Math.max(CHAPERONE_ORBIT_RADIUS, host.size / 2 + b.size / 2 + CHAPERONE_ORBIT_GAP);
        b.x = host.x + Math.cos(b.orbit.angle) * r;
        b.y = host.y + Math.sin(b.orbit.angle) * r;
        place(b);
        continue;
      }

      const m = b.spec.motion;
      if (k === 'tease') {
        // wants attention: a wiggle + a slow pull toward the screen's center
        b.swayT += b.swaySpeed * ts * 2;
        const cx = w / 2, cy = h / 2;
        const d = Math.max(1, Math.hypot(cx - b.x, cy - b.y));
        b.x += ((cx - b.x) / d) * TEASE_CENTER_PULL * ts + b.vx * ts * 0.4 + Math.sin(b.swayT) * 18 * ts;
        b.y += ((cy - b.y) / d) * TEASE_CENTER_PULL * ts + b.vy * ts * 0.4 + Math.cos(b.swayT * 1.3) * 14 * ts;
        place(b);
        continue;
      }

      // The Pull / Cam Girl: a drift bias toward (or away from) the cursor.
      if (phys.cursorPull !== 0 && (k === 'treat' || k === 'live' || k === 'golden' || k === 'heart' || k === 'droplet' || k === 'prism')) {
        const dx = curX - b.x, dy = curY - b.y;
        const d = Math.max(24, Math.hypot(dx, dy));
        b.x += (dx / d) * phys.cursorPull * ts;
        b.y += (dy / d) * phys.cursorPull * ts;
        if (b.baseX != null) b.baseX += (dx / d) * phys.cursorPull * ts;
        if (b.baseY != null) b.baseY += (dy / d) * phys.cursorPull * ts;
      }

      // The Pump (toy): a hard temporary suction - the sweet kinds only, lives
      // stay exactly where they were (the toy never drags danger to your hand).
      if (phys.pumpPull > 0 && (k === 'treat' || k === 'golden' || k === 'heart' || k === 'droplet' || k === 'prism')) {
        const dx = curX - b.x, dy = curY - b.y;
        const d = Math.max(24, Math.hypot(dx, dy));
        b.x += (dx / d) * phys.pumpPull * ts;
        b.y += (dy / d) * phys.pumpPull * ts;
        if (b.baseX != null) b.baseX += (dx / d) * phys.pumpPull * ts;
        if (b.baseY != null) b.baseY += (dy / d) * phys.pumpPull * ts;
      }

      if (m === MOTION.FloatUp || m === MOTION.RainDown) {
        b.y += b.vy * ts;
        b.swayT += b.swaySpeed * ts;
        b.x = b.baseX + Math.sin(b.swayT) * b.swayAmp;
        if ((m === MOTION.FloatUp && b.y < -b.size) || (m === MOTION.RainDown && b.y > h + b.size)) {
          if (k === 'treat' || k === 'golden') expire(b);
          else fadeOut(b);   // freeze/live/heart/droplet/brittle dissolve, never blink off
          continue;
        }
      } else if (m === MOTION.SideDrift) {
        b.x += b.vx * ts;
        b.swayT += b.swaySpeed * ts;
        b.y = b.baseY + Math.sin(b.swayT) * (b.swayAmp * 1.2);
        if (b.x < -b.size || b.x > w + b.size) {
          if (k === 'treat' || k === 'golden') expire(b);
          else fadeOut(b);
          continue;
        }
      } else { // RoamBounce
        b.x += b.vx * ts;
        b.y += b.vy * ts;
        const r = b.size / 2;
        if (b.x < r) { b.x = r; b.vx = Math.abs(b.vx); }
        else if (b.x > w - r) { b.x = w - r; b.vx = -Math.abs(b.vx); }
        if (b.y < r) { b.y = r; b.vy = Math.abs(b.vy); }
        else if (b.y > h - r) { b.y = h - r; b.vy = -Math.abs(b.vy); }
      }
      place(b);

      // Bound tethers (draw while both halves live)
      if (b.spec.boundPairId != null) {
        const pair = boundPairs.get(b.spec.boundPairId);
        if (pair && pair.members.length === 2 && pair.members[0] === b) {
          const other = pair.members[1];
          if (other.state === 'live' && b.state === 'live') {
            tethers.push({ ax: b.x, ay: b.y, bx: other.x, by: other.y, fraying: pair.windowLeftMs != null });
          }
        }
      }
    }

    // Bound window: the second must come down inside it, or the survivor enrages.
    for (const [pairId, pair] of Array.from(boundPairs)) {
      if (pair.windowLeftMs == null) continue;
      pair.windowLeftMs -= ts * 1000;
      if (pair.windowLeftMs <= 0) {
        const survivor = pair.members.find((m) => m.state === 'live');
        if (survivor) enrage(survivor);
        boundPairs.delete(pairId);
      }
    }

    updateDvds(ts);
    fx.setTethers(tethers);
    fx.draw(dt);
  }

  /** Darter frame: telegraph flare -> fast dart, 3 bounces, then fly off. Returns
   * false when the bubble resolved this frame. Sweepers mow what they cross. */
  function updateDarter(b, dt, ts, w, h) {
    if (b.telegraphLeft > 0) {
      b.telegraphLeft -= dt * 1000;   // the flare runs on real time
      if (b.telegraphLeft <= 0) {
        b.wrap.classList.remove('is-telegraph');
        b.activeAt = performance.now();
        const a = rand(0, Math.PI * 2);
        b.vx = Math.cos(a) * DARTER_SPEED_PXS;
        b.vy = Math.sin(a) * DARTER_SPEED_PXS;
      }
      return true;
    }
    b.lifeLeft -= ts * 1000;
    if (b.lifeLeft <= 0) { vanish(b); return false; }   // harmless if it gets away

    // Rabbit homing (The Pull): it flies at you instead of past you.
    if (phys.rabbitHoming && !b.sweeper) {
      const dx = curX - b.x, dy = curY - b.y;
      const d = Math.max(1, Math.hypot(dx, dy));
      const sp = Math.hypot(b.vx, b.vy);
      b.vx += (dx / d) * sp * 1.6 * ts;
      b.vy += (dy / d) * sp * 1.6 * ts;
      const sp2 = Math.max(1, Math.hypot(b.vx, b.vy));
      b.vx = (b.vx / sp2) * sp;
      b.vy = (b.vy / sp2) * sp;
    }

    b.x += b.vx * ts;
    b.y += b.vy * ts;
    const r = b.size / 2;
    if (b.bounces < DARTER_MAX_BOUNCES) {
      let bounced = false;
      if (b.x < r) { b.x = r; b.vx = Math.abs(b.vx); bounced = true; }
      else if (b.x > w - r) { b.x = w - r; b.vx = -Math.abs(b.vx); bounced = true; }
      if (b.y < r) { b.y = r; b.vy = Math.abs(b.vy); bounced = true; }
      else if (b.y > h - r) { b.y = h - r; b.vy = -Math.abs(b.vy); bounced = true; }
      if (bounced) b.bounces++;
    } else if (b.x < -b.size || b.x > w + b.size || b.y < -b.size || b.y > h + b.size) {
      vanish(b);   // flew off after its third bounce
      return false;
    }

    // Tail-Plug: the rabbit drags a sparkling trail that pops what it grazes.
    if (phys.rabbitTrailSec > 0) {
      fx.addSparkle(b.x, b.y);
      for (const n of Array.from(live)) {
        if (n === b || n.state !== 'live' || TOY_IMMUNE.has(n.spec.kind) || isShielded(n)) continue;
        const dx = n.x - b.x, dy = n.y - b.y;
        if (dx * dx + dy * dy <= 46 * 46) {
          if (n.spec.kind === 'live') defuse(n, false);
          else popBenign(n, n.x, n.y, 'trail');
        }
      }
    }

    // Sweepers (GG rabbits / smacked rabbits) mow what they cross.
    if (b.sweeper) {
      for (const n of Array.from(live)) {
        if (n === b || n.state !== 'live' || TOY_IMMUNE.has(n.spec.kind) || isShielded(n)) continue;
        const dx = n.x - b.x, dy = n.y - b.y;
        const reach = (n.size + b.size) / 2 * 0.8;
        if (dx * dx + dy * dy <= reach * reach) {
          if (n.spec.kind === 'live') defuse(n, false);
          else popBenign(n, n.x, n.y, 'sweep');
        }
      }
    }
    return true;
  }

  // ---- field-wide state -------------------------------------------------------------
  function setFrozen(v) {
    v = !!v;
    if (v === frozen) return;
    frozen = v;
    timeScale = v ? 0 : (pendingTimeScale || 1);
    layer.classList.toggle('cf-frozen', v);
    if (v) {
      for (const b of live) b.wrap.classList.add('cf-shudder');
      window.setTimeout(() => { for (const b of live) b.wrap.classList.remove('cf-shudder'); }, 260);
    }
  }

  let pendingTimeScale = 1;   // slow-mo survives across a freeze

  function clearAll(withBurst = false) {
    for (const b of live) {
      if (withBurst) { b.wrap.classList.add('is-pop'); window.setTimeout(() => b.wrap.remove(), 500); }
      else b.wrap.remove();
    }
    live.clear();
    boundPairs.clear();
    for (const d of dvds) d.el.remove();
    dvds.length = 0;
    fx.setTethers([]);
    fx.clear();
  }

  /** Snap Field toy: every live bubble snaps at once (each PAYING - not the
   * silent janitor wipe). Capstone clears everything through paying paths. */
  function snapAll(everything = false) {
    for (const b of Array.from(live)) {
      if (b.state !== 'live' || TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) continue;
      if (b.spec.kind === 'live') defuse(b, false);
      else if (everything) popBenign(b, b.x, b.y, 'toy');
    }
  }

  function dispose() {
    clearAll();
    window.removeEventListener('pointermove', onPointerMove);
    layer.remove();
    fxLayer.remove();
  }

  return {
    spawn,
    update,
    floatText,
    playChime,
    castRipple,
    nearAny,
    clearAll,
    snapAll,
    dischargeAt,
    launchDvd,
    dvdActive,
    linkChaperone,
    dispose,
    setFrozen,
    isFrozen: () => frozen,
    setTimeScale(f) { pendingTimeScale = Math.max(0, f); if (!frozen) timeScale = pendingTimeScale; },
    setHeld(v) { held = !!v; layer.classList.toggle('cf-held', held); },
    setInputLocked(v) { inputLocked = !!v; },
    setVibe(on, hoverPops = false) { vibeOn = !!on; vibeHover = !!hoverPops; fx.setVibe(vibeOn); },
    phys,   // the run brain writes the accessory knobs here once at start
    count: () => live.size,
    freezeCount: () => { let n = 0; for (const b of live) if (b.spec.kind === 'freeze') n++; return n; },
    liveThreatCount: () => { let n = 0; for (const b of live) if (b.spec.kind === 'live') n++; return n; },
    channelSeconds: () => channelSeconds,
    cursor: () => ({ x: curX, y: curY }),
    minFuseSec: () => {
      let min = null;
      for (const b of live) {
        if (b.spec.kind !== 'live') continue;
        const s = Math.max(0, b.fuseLeft) / 1000;
        if (min == null || s < min) min = s;
      }
      return min;
    },
    /** Wave 2 (the Wand): pop every treat within the beam's radius of (x,y).
     * includeSpecials (capstone) also takes golden/heart/droplet/prism. Lives,
     * teases and brittles ignore the wand entirely. Returns pops. */
    wandSweep(x, y, radiusPx, includeSpecials) {
      let n = 0;
      for (const b of [...live]) {
        if (b.state !== 'live' || TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) continue;
        const k = b.spec.kind;
        const ok = k === 'treat'
          || (includeSpecials && (k === 'golden' || k === 'heart' || k === 'droplet' || k === 'prism'));
        if (!ok) continue;
        if (Math.hypot(b.x - x, b.y - y) > radiusPx + b.size / 2) continue;
        popBenign(b, b.x, b.y, 'wand');
        n++;
      }
      return n;
    },
    /** Wave 2 (Sticky Fingers): pop the treats whose center the held 3D card's
     * screen rect covers. Returns pops (src 'card' pays the accessory bonus). */
    sweepRect(rect) {
      if (!rect) return 0;
      let n = 0;
      for (const b of [...live]) {
        if (b.state !== 'live' || TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) continue;
        if (b.spec.kind !== 'treat') continue;
        if (b.x < rect.left || b.x > rect.right || b.y < rect.top || b.y > rect.bottom) continue;
        popBenign(b, b.x, b.y, 'card');
        n++;
      }
      return n;
    },
    /** Wave 2 (Static weather): a stray bolt pops a random treat FOR the
     * player - or, the sour case, zaps a random live fuse down to half.
     * Returns { x, y, kind } for the 3D strike mirror, or null (nothing hit). */
    stormStrike(zapLife) {
      if (zapLife) {
        const lives = [...live].filter((b) => b.spec.kind === 'live' && b.fuseLeft > 2000);
        if (!lives.length) return null;
        const b = lives[(Math.random() * lives.length) | 0];
        b.fuseLeft *= 0.5;
        floatText('ZAP', b.x, b.y - b.size * 0.2, 'cf-pop--bad');
        return { x: b.x, y: b.y, kind: 'life' };
      }
      const treats = [...live].filter((b) => b.spec.kind === 'treat');
      if (!treats.length) return null;
      const b = treats[(Math.random() * treats.length) | 0];
      const bx = b.x, by = b.y;
      popBenign(b, bx, by, 'storm');
      return { x: bx, y: by, kind: 'treat' };
    },
  };
}
