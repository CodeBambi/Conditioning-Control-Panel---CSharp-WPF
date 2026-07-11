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
const VIBE_BRUSH_PX = 14;        // extra brush reach around a bubble's radius for the vibe sweep

// ---- motion tuning (self-calibrated for the browser field) ----
const CROSS_SEC_MIN = 8, CROSS_SEC_MAX = 13;   // vertical travellers: screen-cross time
const SIDE_SEC_MIN = 9, SIDE_SEC_MAX = 14;     // side drifters: width-cross time
const ROAM_SPEED_MIN = 90, ROAM_SPEED_MAX = 150; // px/s bouncing drift
const SWAY_AMP_MIN = 18, SWAY_AMP_MAX = 42;    // px horizontal wobble

// ---- the paddle (a held 3D card sweeping the field) ----
const PADDLE_IMMUNE_MS = 650;         // per-bubble grace after the card touches it
const PADDLE_DEFUSE_WINDOW_MS = 1000; // sliding window for the defuse rate cap
const PADDLE_DEFUSE_CAP = 3;          // max free defuses per window (no cluster-wipe)

const rand = (a, b) => a + Math.random() * (b - a);
const pickOf = (arr) => arr[(Math.random() * arr.length) | 0];

// Kinds no toy / ripple / sweep / zone may touch - only the hand (or time) ends them.
const TOY_IMMUNE = new Set(['tease', 'brittle', 'freeze', 'darter']);
// Kinds a chain-reaction burst may take with it (benign rewards only).
const CHAINABLE = new Set(['treat', 'golden', 'droplet', 'heart']);

export function createChaosField({ hud, fx, canChannel, onBenignPopped, onFreezeCaught,
  onDefused, onDetonated, onTreatExpired, onChannelBroken,
  onDarterCaught, onTeaseTouched, onTeaseDenied, onBrittleShattered, onBoundEnraged,
  onRabbitSmacked = null, onDvdCorner = null }) {
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
  let fieldClockMs = 0;     // monotonic real-time clock for paddle immunity / rate cap
  const paddleDefuses = []; // fieldClockMs timestamps of recent paddle defuses (rate cap)

  // ---- the Wand's ink (drawn for ~2s, lingers 4s; pops whatever touches it) ----
  const ink = [];              // { x, y, lifeMs } - ages on the field clock (a freeze holds it)
  let inkAnchorX = null, inkAnchorY = null;   // last laid point, for segment interpolation
  let inkSpecials = false;     // capstone: the trail takes golden/heart/droplet/prism too
  const inkDefuses = [];       // fieldClockMs stamps of recent ink defuses (rate cap)
  const INK_LIFE_MS = 4000, INK_SPACING_PX = 16, INK_MAX_POINTS = 280, INK_RADIUS_PX = 30;
  const INK_DEFUSE_CAP = 3, INK_DEFUSE_WINDOW_MS = 1500, INK_BOUNCE_IMMUNE_MS = 450;

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
    rippleFlingsRabbits: false, // Riptide duo: the ripple smacks (flings) rabbits it washes over
    dvdElectrified: false, // Short Circuit duo: bouncing logo crackles + arcs chain lightning on bounce
    pumpPull: 0,          // The Pump (toy): temporary suction, sweet kinds only
    trailBounce: false,   // Hopscotch duo: rabbits ricochet off the Wand's ink
    thoughtOrbit: false,  // One-Track Mind duo: intrusive thoughts orbit the cursor
    inkFreeze: false,     // Milking Machine duo: the pump's suction holds the ink fresh
    // ---- THE BIOMES (wave 2) ----
    currents: null,           // Undertow: [{x,y,r,pull}] vortices that drag the field
    currentChannelMult: 1.0,  // Undertow: hold-to-snap takes this much longer inside one
    fuseTickMult: 1.0,        // Vertigo: fuses burn hotter while the world is flipped
  };

  /** Any Undertow current covering (x,y)? (biome mech re-feeds phys.currents) */
  function inCurrent(x, y) {
    if (!phys.currents) return false;
    for (const cz of phys.currents) {
      if (Math.hypot(cz.x - x, cz.y - y) <= cz.r) return true;
    }
    return false;
  }
  let vibeOn = false, vibeWide = false;   // wide = the capstone's doubled brush reach

  const W = () => window.innerWidth, H = () => window.innerHeight;
  let curX = W() / 2, curY = H() / 2;
  window.addEventListener('pointermove', onPointerMove, { passive: true });
  function onPointerMove(e) {
    curX = e.clientX; curY = e.clientY;
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
    // Echo Chamber blooms spawn under the held card: a short grace so the
    // paddle doesn't erase them the same frame they appear.
    if (spec.paddleGraceMs) b.paddleImmuneUntil = fieldClockMs + spec.paddleGraceMs;
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
    // Hover verb: the Brittle shatters at a touch of the cursor. (The vibe sweep
    // is NOT event-driven: fallNav's canvas pointer-capture retargets every
    // enter/move during a mouse hold, so the buzz runs as a per-frame proximity
    // sweep in update() instead - same pattern as the Wand's ink trail.)
    wrap.addEventListener('pointerenter', () => {
      if (b.state !== 'live' || held || inputLocked) return;
      if (b.spec.kind === 'brittle') tryShatter(b);
    });

    live.add(b);
    layer.appendChild(wrap);
    return b;
  }

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
      if (b.telegraphLeft > 0) return;                // not active yet
      // The Spanker keeps working after the first hit: a mowing sweeper can be
      // re-smacked (fresh fling, fresh bounces) as many times as you land it.
      if (phys.spanker) { smack(b, x, y); return; }
      if (b.sweeper) return;                          // sweepers can't be CAUGHT bare-handed
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

  // Pop explosion (mirrors the WPF ChaosSkiaFxOverlay.EmitBurst): a bright
  // white-hot flash core + a crisp expanding shockwave ring + radiating shards,
  // all tinted to the payload colour and additively blended (mix-blend-mode:
  // screen ~ Skia's Plus) so overlapping pops read hot. CSS-driven; the shards
  // keep the little-gravity settle of the original WPF sparkle burst.
  function burstColor(kind) {
    if (kind === 'live') return '#8affaa';   // a live SNAP: cool green release (WPF SnapColor)
    if (kind === 'golden' || kind === 'droplet' || kind === 'heart' || kind === 'prism') return '#ffe27a';
    return '#ff9fd6';
  }
  function sparkleBurst(x, y, kind, size = 110) {
    const rich = kind === 'golden' || kind === 'droplet' || kind === 'heart' || kind === 'prism';
    const snap = kind === 'live';
    const col = burstColor(kind);
    // A live snap is a bigger release; rich treats a touch bigger than a plain pop.
    const scale = snap ? 1.3 : rich ? 1.2 : 1.0;

    // Bright central flash - the "release" (fat, brief, white-hot core).
    const flash = document.createElement('div');
    flash.className = 'cf-burst';
    flash.style.left = `${x}px`;
    flash.style.top = `${y}px`;
    flash.style.color = col;
    flash.style.setProperty('--bsize', `${Math.round(size * 0.95 * scale)}px`);
    fxLayer.appendChild(flash);
    flash.addEventListener('animationend', () => flash.remove(), { once: true });

    // Crisp expanding shockwave ring.
    const ring = document.createElement('div');
    ring.className = 'cf-shock';
    ring.style.left = `${x}px`;
    ring.style.top = `${y}px`;
    ring.style.color = col;
    ring.style.setProperty('--rsize', `${Math.round(size * 1.9 * scale)}px`);
    fxLayer.appendChild(ring);
    ring.addEventListener('animationend', () => ring.remove(), { once: true });

    // Radiating shards (the original sparkle burst), now tinted to the payload.
    const n = rich ? 13 : 9;
    for (let i = 0; i < n; i++) {
      const p = document.createElement('div');
      p.className = 'cf-spark';
      const ang = (Math.PI * 2 * i) / n + rand(-0.35, 0.35);
      const dist = rand(40, 108) * scale;
      p.style.left = `${x}px`;
      p.style.top = `${y}px`;
      p.style.color = col;
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
    sparkleBurst(b.x, b.y, b.spec.kind, b.size);
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

  /** THE BIOMES (Searchlight): the light finds you - every fused bubble within
   * the radius enrages (halved fuse, sprints). Returns how many were caught. */
  function enrageNear(x, y, radiusPx) {
    let n = 0;
    for (const b of live) {
      if (b.spec.kind !== 'live' || b.state !== 'live' || b.enraged || isShielded(b)) continue;
      if (Math.hypot(b.x - x, b.y - y) > radiusPx + b.size / 2) continue;
      enrage(b); n++;
    }
    return n;
  }

  /** THE BIOMES (Vertigo): the flip - every drifter's vertical motion reverses
   * on the spot. One-shot: call again to flip back. New spawns arrive normal
   * either way (the cull is direction-aware, so wrong-way exits stay clean). */
  function flipDrift() {
    for (const b of live) {
      const m = b.spec.motion;
      if (b.state !== 'live' || (m !== MOTION.FloatUp && m !== MOTION.RainDown)) continue;
      b.vy = -b.vy;
    }
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

  // ---- the Wand's ink trail ----------------------------------------------------
  /** A fresh draw window opens (one wand charge). The previous trail may still
   * be fading - charges stack ink, capped at INK_MAX_POINTS oldest-first. */
  function wandInkStart(takesSpecials) {
    inkAnchorX = null; inkAnchorY = null;
    inkSpecials = !!takesSpecials;
  }
  /** Lay ink under the cursor (called per frame while the draw window is open).
   * Interpolates from the last laid point so a fast flick still leaves an
   * unbroken line instead of a dotted one. */
  function wandInkAt(x, y) {
    if (inkAnchorX == null) { pushInk(x, y); return; }
    const dx = x - inkAnchorX, dy = y - inkAnchorY;
    const d = Math.hypot(dx, dy);
    if (d < INK_SPACING_PX) return;
    const steps = Math.min(40, (d / INK_SPACING_PX) | 0);
    const ax = inkAnchorX, ay = inkAnchorY;
    for (let i = 1; i <= steps; i++) pushInk(ax + dx * (i / steps), ay + dy * (i / steps));
  }
  function pushInk(x, y) {
    ink.push({ x, y, lifeMs: INK_LIFE_MS });
    if (ink.length > INK_MAX_POINTS) ink.splice(0, ink.length - INK_MAX_POINTS);
    inkAnchorX = x; inkAnchorY = y;
  }
  function inkHit(b) {
    const reach = INK_RADIUS_PX + b.size / 2;
    for (const p of ink) {
      const dx = b.x - p.x, dy = b.y - p.y;
      if (dx * dx + dy * dy <= reach * reach) return p;
    }
    return null;
  }
  /** Everything that drifts across the shimmering trail: treats pop (the capstone
   * adds the sweet specials), live fuses snap clean (rate-capped like the paddle),
   * and with Hopscotch drafted rabbits RICOCHET - each bounce is a fresh smack. */
  function sweepInk() {
    while (inkDefuses.length && inkDefuses[0] <= fieldClockMs - INK_DEFUSE_WINDOW_MS) inkDefuses.shift();
    for (const b of Array.from(live)) {
      if (b.state !== 'live' || isShielded(b)) continue;
      const k = b.spec.kind;
      if (k === 'darter') {   // Hopscotch only - the trail is a wall, not a hand
        if (!phys.trailBounce || b.telegraphLeft > 0) continue;
        if (b.trailImmuneUntil && fieldClockMs < b.trailImmuneUntil) continue;
        const p = inkHit(b);
        if (p) { smack(b, p.x, p.y); b.trailImmuneUntil = fieldClockMs + INK_BOUNCE_IMMUNE_MS; }
        continue;
      }
      if (TOY_IMMUNE.has(k)) continue;
      if (k === 'live') {
        if (inkDefuses.length >= INK_DEFUSE_CAP || !inkHit(b)) continue;
        defuse(b, false);
        inkDefuses.push(fieldClockMs);
      } else if (k === 'treat' || (inkSpecials && (k === 'golden' || k === 'heart' || k === 'droplet' || k === 'prism'))) {
        if (inkHit(b)) popBenign(b, b.x, b.y, 'ink');
      }
    }
  }

  /** Milking Machine duo: the pump lets go and the whole drawing detonates -
   * everything within reach of any ink point pops/snaps at once, sparkle
   * bursts run the trail's length, and the ink is spent. Returns takes. */
  function inkDetonate() {
    if (!ink.length) return 0;
    let n = 0;
    const reachBase = INK_RADIUS_PX * 3;
    for (const b of Array.from(live)) {
      if (b.state !== 'live' || TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) continue;
      const reach = reachBase + b.size / 2;
      let hit = false;
      for (const p of ink) {
        const dx = b.x - p.x, dy = b.y - p.y;
        if (dx * dx + dy * dy <= reach * reach) { hit = true; break; }
      }
      if (!hit) continue;
      if (b.spec.kind === 'live') defuse(b, false);
      else popBenign(b, b.x, b.y, 'ink');
      n++;
    }
    for (let i = 0; i < ink.length; i += 6) sparkleBurst(ink[i].x, ink[i].y, 'treat', 70);
    ink.length = 0; inkAnchorX = null; inkAnchorY = null;
    fx.setInk(ink);
    return n;
  }

  /** Trust Exercise duo: an expanding sonar ring that briefly lights the
   * Blindfold's dimmed bubbles back to full opacity as it passes over them.
   * It reveals - it never pops; the hand does the taking. */
  function sonarAt(x, y, radiusPx) {
    const ring = document.createElement('div');
    ring.className = 'cf-ripple cf-ripple--sonar';
    ring.style.left = `${x}px`;
    ring.style.top = `${y}px`;
    ring.style.setProperty('--r', `${radiusPx}px`);
    ring.style.setProperty('--life', '620ms');
    fxLayer.appendChild(ring);
    ring.addEventListener('animationend', () => ring.remove(), { once: true });
    for (const b of live) {
      if (b.state !== 'live' || !b.wrap.dataset.dim) continue;
      const d = Math.hypot(b.x - x, b.y - y);
      if (d > radiusPx + b.size / 2) continue;
      window.setTimeout(() => {
        if (b.state !== 'live') return;
        b.revealUntil = fieldClockMs + 1400;
        b.wrap.style.opacity = '1';
      }, (d / radiusPx) * 620);
    }
  }

  /** Midas Ricochet duo: gild plain treats near a popped lucky bubble. A gilded
   * pop tips gold (the run brain reads spec.gilded). Reverts after 2.5s. */
  function gildNear(x, y, radiusPx) {
    let n = 0;
    for (const b of live) {
      if (b.state !== 'live' || b.spec.kind !== 'treat') continue;
      if (Math.hypot(b.x - x, b.y - y) > radiusPx + b.size / 2) continue;
      b.spec.gilded = true;
      b.gildUntil = fieldClockMs + 2500;
      b.wrap.classList.add('cf-gilded');
      n++;
    }
    if (n) sparkleBurst(x, y, 'golden', 150);
    return n;
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
      if (phys.thoughtOrbit && d.text) {
        // One-Track Mind: the thought stops racing past and orbits the cursor -
        // a popping halo the pull feeds. Radius eases in from wherever it was.
        if (d.orbitAngle == null) {
          d.orbitAngle = Math.atan2(d.y - curY, d.x - curX);
          d.orbitR = Math.max(90, Math.min(260, Math.hypot(d.x - curX, d.y - curY)));
        }
        d.orbitR += (130 - d.orbitR) * Math.min(1, ts * 2.2);
        d.orbitAngle += 2.3 * ts;
        d.x = curX + Math.cos(d.orbitAngle) * d.orbitR;
        d.y = curY + Math.sin(d.orbitAngle) * d.orbitR;
      } else {
        d.x += d.vx * ts;
        d.y += d.vy * ts;
        let bounced = false;
        if (d.x < d.w / 2) { d.x = d.w / 2; d.vx = Math.abs(d.vx); bounced = true; }
        else if (d.x > w - d.w / 2) { d.x = w - d.w / 2; d.vx = -Math.abs(d.vx); bounced = true; }
        if (d.y < d.h / 2) { d.y = d.h / 2; d.vy = Math.abs(d.vy); bounced = true; }
        else if (d.y > h - d.h / 2) { d.y = h - d.h / 2; d.vy = -Math.abs(d.vy); bounced = true; }
        if (bounced) {
          d.bounces++;
          // Autoplay: the mythical CORNER shot - the other wall within a hair of the hit.
          const nearX = d.x <= d.w / 2 + 36 || d.x >= w - d.w / 2 - 36;
          const nearY = d.y <= d.h / 2 + 36 || d.y >= h - d.h / 2 - 36;
          if (nearX && nearY && !d.text && onDvdCorner) {
            try { onDvdCorner(d.x, d.y); } catch (err) { /* ignore */ }
          }
          // Casting Couch: the logo splits on its first bounces - one becomes two, then four.
          if (d.splitsLeft > 0) {
            d.splitsLeft--;
            spawnDvd({ durationSec: d.lifeLeft, speed: d.speed, scale: d.scale, splitBounces: 0, text: d.text, x: d.x, y: d.y });
          }
          // Short Circuit duo: the shorted-out logo arcs chain lightning off each wall it hits.
          if (phys.dvdElectrified && performance.now() > (d.arcCdUntil || 0)) {
            d.arcCdUntil = performance.now() + 250;
            dischargeAt(d.x, d.y, { maxTargets: 3, reach: 520, chain: true });
          }
        }
      }
      // Short Circuit duo: crackling static rides the logo while the duo is live.
      d.el.classList.toggle('cf-dvd--electric', !!phys.dvdElectrified);
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
      // Riptide duo: rabbits are normally toy-immune, but the wave smacks them off.
      const isFlingRabbit = phys.rippleFlingsRabbits && !treatsOnly
        && b.spec.kind === 'darter' && b.telegraphLeft <= 0 && !isShielded(b);
      if ((TOY_IMMUNE.has(b.spec.kind) || isShielded(b)) && !isFlingRabbit) continue;
      if (treatsOnly && b.spec.kind === 'live') continue;
      const dx = b.x - x, dy = b.y - y;
      const d = Math.hypot(dx, dy);
      if (d <= radiusPx + b.size / 2) hits.push({ b, at: Math.max(0, (d / radiusPx)) * lifeMs, fling: isFlingRabbit });
    }
    for (const h of hits) {
      window.setTimeout(() => {
        if (h.b.state !== 'live') return;
        if (h.fling) { smack(h.b, x, y); h.b.rippleTrailUntil = performance.now() + 2000; return; }
        if (h.b.spec.kind === 'live') defuse(h.b, false);
        else popBenign(h.b, h.b.x, h.b.y, 'ripple');
      }, h.at);
    }
    return hits.length;
  }

  // ---- per-frame integration ---------------------------------------------------------
  function update(dt) {
    if (held) { fx.draw(dt); return; }
    fieldClockMs += dt * 1000;   // real time: paddle immunity expires even while frozen
    const ts = dt * timeScale;
    const w = W(), h = H();
    const tethers = [];
    // VibePopping: a live buzz trails the cursor. While it buzzes the sweep runs
    // on proximity - hovering alone pops whatever the cursor brushes, even if
    // the bubble is the one doing the moving. The capstone widens the reach.
    const vibeSweeping = vibeOn && !inputLocked;
    if (vibeOn) fx.vibePoint(curX, curY);

    for (const b of Array.from(live)) {
      // Trust Exercise / Midas Ricochet: timed visual states expire on the field clock.
      if (b.revealUntil && fieldClockMs >= b.revealUntil) {
        b.revealUntil = 0;
        if (b.wrap.dataset.dim) b.wrap.style.opacity = String(phys.dimOpacity);
      }
      if (b.gildUntil && fieldClockMs >= b.gildUntil) {
        b.gildUntil = 0;
        b.spec.gilded = false;
        b.wrap.classList.remove('cf-gilded');
      }
      // channel runs on REAL time (a frozen field still channels - that's the
      // freeze's reward); the fuse holds while the hand is on it either way.
      if (b.channel) {
        // the Undertow: holding on inside a current is a real fight
        const drag = phys.currentChannelMult > 1 && inCurrent(b.x, b.y) ? phys.currentChannelMult : 1;
        b.channel.elapsed += (dt * 1000) / drag;
        channelSeconds += dt;
        const p = Math.min(1, b.channel.elapsed / DEFUSE_HOLD_MS);
        b.chan.style.setProperty('--cdeg', `${p * 360}`);
        b.body.style.transform = `scale(${1 - (1 - CHANNEL_MIN_SCALE) * p})`;
        if (p >= 1) { defuse(b, true); }
        continue;   // the grip holds it still: no motion, no fuse, no rot
      } else if (b.spec.kind === 'live' && b.fuseLeft > 0) {
        b.fuseLeft -= ts * 1000 * (phys.fuseTickMult || 1);   // Vertigo: hotter mid-flip
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

      // VibePopping brush: treats pop, lives snap clean (sweepPop guards the
      // toy-immune kinds + shields itself).
      if (vibeSweeping && Math.hypot(b.x - curX, b.y - curY) <= b.size / 2 + VIBE_BRUSH_PX * (vibeWide ? 2.2 : 1)) {
        sweepPop(b, 'vibe');
        if (b.state !== 'live') continue;
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

      // THE BIOMES - the Undertow's currents: slow vortices that drag everything
      // sweet (and the fused) toward their eye, with a sideways swirl folded in
      // so it reads as water, not a magnet. The mech re-feeds positions per tick.
      if (phys.currents && phys.currents.length
          && (k === 'treat' || k === 'live' || k === 'golden' || k === 'heart' || k === 'droplet' || k === 'prism')) {
        for (const cz of phys.currents) {
          const dx = cz.x - b.x, dy = cz.y - b.y;
          const d = Math.hypot(dx, dy);
          if (d > cz.r || d < 20) continue;
          const pull = (cz.pull || 50) * (1 - d / cz.r);
          const fx2 = ((dx / d) * pull + (-dy / d) * pull * 0.7) * ts;
          const fy2 = ((dy / d) * pull + (dx / d) * pull * 0.7) * ts;
          b.x += fx2; b.y += fy2;
          if (b.baseX != null) b.baseX += fx2;
          if (b.baseY != null) b.baseY += fy2;
        }
      }

      if (m === MOTION.FloatUp || m === MOTION.RainDown) {
        b.y += b.vy * ts;
        b.swayT += b.swaySpeed * ts;
        b.x = b.baseX + Math.sin(b.swayT) * b.swayAmp;
        // cull at the edge the bubble is actually travelling toward - after a
        // Vertigo flip a "riser" may be falling (and vice versa), and it should
        // still leave the screen cleanly out the side it exits.
        if (b.vy < 0 ? b.y < -b.size : b.y > h + b.size) {
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

    // the Wand's ink: age on the field clock (a freeze holds the drawing; the
    // Milking Machine's suction holds it fresh too), then sweep it - the trail
    // pops/defuses/bounces whatever drifts across it.
    if (ink.length) {
      if (!phys.inkFreeze) {
        for (let i = ink.length - 1; i >= 0; i--) { if ((ink[i].lifeMs -= ts * 1000) <= 0) ink.splice(i, 1); }
      }
      if (ink.length) sweepInk();
    }

    updateDvds(ts);
    fx.setTethers(tethers);
    fx.setInk(ink);
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

    // Riptide duo: a rabbit the ripple just flung drags a short sparkle wake (cosmetic).
    if (b.rippleTrailUntil && performance.now() < b.rippleTrailUntil) fx.addSparkle(b.x, b.y);

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
    ink.length = 0; inkAnchorX = null; inkAnchorY = null; inkDefuses.length = 0;
    fx.setTethers([]);
    fx.setInk([]);
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

  // ---- discovery spotlight ----------------------------------------------------
  // The lesson/VN reveal delay (chaosRun) rings the just-arrived bubble it's about
  // to explain, so the eye lands on the new thing while it falls into view. The
  // ring is a child of the bubble's wrap, so it tracks the bubble as it drifts and
  // vanishes with it if the bubble pops before the window closes.
  function spotlightBubble(b, ms) {
    if (!b || !b.wrap) return;
    if (b.spotEl) { try { b.spotEl.remove(); } catch (e) {} b.spotEl = null; }
    const el = document.createElement('div');
    el.className = 'cf-spotlight';
    b.wrap.appendChild(el);
    b.spotEl = el;
    requestAnimationFrame(() => { if (b.spotEl === el) el.classList.add('cf-spotlight--in'); });
    window.setTimeout(() => {
      if (b.spotEl !== el) return;
      el.classList.remove('cf-spotlight--in');
      el.classList.add('cf-spotlight--out');
      window.setTimeout(() => { try { el.remove(); } catch (e) {} if (b.spotEl === el) b.spotEl = null; }, 340);
    }, Math.max(400, ms | 0));
  }
  // Ring the newest live bubble matching a discovery codex id (bubble:/pickup:).
  // Best-effort: no-op (returns false) when nothing in-field matches - weather,
  // junctions and 3D tunnel pickups have no field object to ring.
  function highlightDiscovery(codexId, ms = 2000) {
    const m = /^(?:bubble|pickup):(.+)$/.exec(codexId || '');
    if (!m) return false;
    const key = m[1];
    let hit = null;   // Set keeps insertion order, so the last match is the newest
    for (const b of live) {
      if (b.state !== 'live') continue;
      if (b.spec.kind === key || b.spec.variantId === key) hit = b;
    }
    if (!hit) return false;
    spotlightBubble(hit, ms);
    return true;
  }

  return {
    highlightDiscovery,
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
    setVibe(on, wide = false) { vibeOn = !!on; vibeWide = !!wide; fx.setVibe(vibeOn); },
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
    // ---- the Wand's ink trail (the run brain drives the 2s draw window) ----
    wandInkStart,
    wandInkAt,
    wandInkActive: () => ink.length > 0,
    inkDetonate,   // Milking Machine: the pump's end detonates the drawing
    sonarAt,       // Trust Exercise: reveal-ping the Blindfold's dimmed bubbles
    gildNear,      // Midas Ricochet: gild treats near a popped lucky bubble
    enrageNear,    // Searchlight biome: the light finds the fused
    flipDrift,     // Vertigo biome: reverse every drifter's vertical on the spot
    /** Radius pop at (x,y) - the Pump's arrival burst (formerly the Wand's beam).
     * includeSpecials also takes golden/heart/droplet/prism. Lives, teases and
     * brittles ignore it entirely. Returns pops. */
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
    /** The paddle: the held 3D card sweeps the field. Every live bubble whose
     * center the card's screen rect covers is acted on by kind -
     *   benign treats  -> pop (src 'card' pays the Sticky Fingers bonus)
     *   live (fused)    -> free SNAP defuse, but rate-capped so you can't wipe a cluster
     *   rabbits/darters -> flung away from the card center (reuses the Spanker)
     * Each touched bubble gets a short immunity so parking the card doesn't
     * re-trigger it. Returns { popped, defused, flung } for scoring + per-asset
     * attribution. */
    paddleSweep(rect) {
      const tally = { popped: 0, defused: 0, flung: 0 };
      if (!rect) return tally;
      const cx = (rect.left + rect.right) / 2, cy = (rect.top + rect.bottom) / 2;
      // prune the sliding defuse-rate window
      while (paddleDefuses.length && paddleDefuses[0] <= fieldClockMs - PADDLE_DEFUSE_WINDOW_MS) paddleDefuses.shift();
      for (const b of [...live]) {
        if (b.state !== 'live') continue;
        if (b.x < rect.left || b.x > rect.right || b.y < rect.top || b.y > rect.bottom) continue;
        if (b.paddleImmuneUntil && fieldClockMs < b.paddleImmuneUntil) continue;
        const kind = b.spec.kind;
        // rabbits: fling them away from the card center (not the hand-only rule
        // below). Sweepers stay smackable - the paddle can punt a mowing rabbit
        // again and again (the per-bubble immunity window paces the hits).
        if (kind === 'darter') {
          if (b.telegraphLeft > 0) continue; // not catchable yet
          smack(b, cx, cy);
          b.paddleImmuneUntil = fieldClockMs + PADDLE_IMMUNE_MS;
          tally.flung++;
          continue;
        }
        if (TOY_IMMUNE.has(kind) || isShielded(b)) continue; // tease/brittle/freeze: hand-only
        if (kind === 'live') {
          if (paddleDefuses.length >= PADDLE_DEFUSE_CAP) continue; // rate cap: no cluster-wipe
          defuse(b, false);
          paddleDefuses.push(fieldClockMs);
          b.paddleImmuneUntil = fieldClockMs + PADDLE_IMMUNE_MS;
          tally.defused++;
          continue;
        }
        // benign rewards (treat / golden / heart / droplet / prism ...)
        popBenign(b, b.x, b.y, 'card');
        b.paddleImmuneUntil = fieldClockMs + PADDLE_IMMUNE_MS;
        tally.popped++;
      }
      return tally;
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
