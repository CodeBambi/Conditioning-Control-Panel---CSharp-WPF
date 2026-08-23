/* ============================================================================
 * engine/index.js — THE DISTRACTION ENGINE (BUILD-CONTRACT §5).
 *
 * CCP's own effects are the Arcademy's difficulty slider. One shared engine,
 * consumed by every class through intensity dials it can spend but never raise.
 *
 * ---------------------------------------------------------------------------
 * const engine = createEngine({ mount, caps, masterIntensity, effectIntensity,
 *                               rng, words, assets, motionLevel, reducedMotion,
 *                               bus, effectsConsumed, seed, gameKey });
 *
 *   engine.setHeat(h01)          ONE scalar -> the whole channel vector
 *   engine.fire(kind, opts)      'glitch_swap'|'sub_flash'|'flash_burst'|'gif_burst'|'audio_trigger'
 *   engine.sustain(kind, opts)   'row_drift'|'bubble_field'|'wash'|'gif_rain'|'ambient_field'|'crt'
 *   engine.stop(kind)            fade a sustained effect (never a snap)
 *   engine.setpiece(descriptor)  {key, eligiblePhases, perBeatChance, oncePerRunGate, forceTail}
 *   engine.beat(opts)            advance the beat clock (gate reset + garnish draw)
 *   engine.ceremony(kind, opts)  'stamp'|'streak_meter'|'jackpot'|'near_miss'
 *   engine.suspend(on)           drop EVERYTHING now (panic / video / audio-only)
 *   engine.dispose()
 *
 * Additive helpers (documented deviations are listed in the final build report;
 * these are ADDITIONS, the pinned surface above is unchanged):
 *   engine.setPhase(p)           phase key/index for eligiblePhases + anti-clump
 *   engine.armTail(on)           arm forceTail for the class's last beat
 *   engine.intensityCurve(p01,o) DTRH per-phase sawtooth (also on curves.js)
 *   engine.plainShare(i01,floor) DTRH plain-share ramp (.80 -> .30)
 *   engine.isPlainBeat(i01)      rolls the plain-share on the seeded rng
 *   engine.cadenceMs(kind)       current heat-scaled cadence for sub_flash /
 *                                bubble_field / flash_burst (ms, Infinity = off)
 *   engine.rewardRoll(opts)      the variable-ratio canon (schedule.js)
 *   engine.garnish(opts)         draw + apply one garnish from the shuffled bag
 *   engine.escapeGuard(opts)     the 6-interaction / 5s guard for a game-owned
 *                                input-blocking surface
 *   engine.channels()            the CLAMPED channel vector (read-only)
 *   engine.diagnostics()         live node counts, setpiece stats, refusals
 *   engine.sustain('sub_flash')  additive alias: drives fire('sub_flash') on the
 *                                heat-scaled cadence (games asked for a rung, not
 *                                a loop of their own); stop('sub_flash') ends it
 *
 * ---------------------------------------------------------------------------
 * PARAMETER SCALE: every `strength` / `alpha` / `level` / `density` opt accepts
 * EITHER Intake's 0..1 magnitude OR DTRH's 0-100 strength — anything above 1 is
 * read as a percentage (so strength:20 === strength:0.2). `durationMult` (0.1..10)
 * is DTRH's freeze/slow-mo stretch and is never a percentage.
 *
 * THE CEILING RULE (read before using any `strength` / `alpha` / `level` opt):
 * every one of those opts asks for AT MOST the current clamped channel value —
 * `min(requested, channels[ch])`. Setting strength:1 does not raise anything;
 * the way a game turns effects up is engine.setHeat(). If the player has capped
 * a channel to 0 the primitive returns null and renders nothing at all.
 *
 * ---------------------------------------------------------------------------
 * OPTS PER KIND (game builders: this is your reference)
 *
 * fire('glitch_swap', {
 *   targets: Element|Element[]|NodeList   REQUIRED-ish (no targets = no-op)
 *   variant?: 'crossfade'|'rgbsplit'|'vhsroll'|'datamosh'   (else pooled, no-repeat)
 *   strength?: 0..1        <= channels.bgIntensity (ceiling rule); null = spend it all
 *   seconds?: number       shudder hold in seconds (default 0.6, hard cap 12s)
 *   durationMult?: number  0.1..10 stretch (freeze/slow-mo parity)
 *   onSwap?: (variantName) => void   called at the transition MIDPOINT — the game
 *                          swaps its own content there. The engine NEVER moves a
 *                          hitbox or a glyph: input honesty is yours to keep.
 *   exempt?: Element       never dressed (e.g. the key under the finger)
 *   sfx?: false            suppress the transition sfx; sfxName?: string
 * }) -> { kind, variant, durMs, cancel() }
 *
 * fire('sub_flash', {
 *   text?: string          overrides the word pool
 *   image?: true|false     force / forbid the image card (default: 50/50 when
 *                          words exist, image-only when the word pool is EMPTY,
 *                          and a silent skip when neither is available)
 *   url?: string           explicit image url (else the asset pool)
 *   strength?: 0..1        <= channels.subDensity (ceiling rule)
 *   alpha?: 0..1           <= the ceiling-derived alpha; a cap of 0 = silent skip
 *   variant?: 'whisper'|'centre'|'scatter'|'stamp'
 *   anchor?: Element       mount the card inside a game element instead of the layer
 *   sfx?: true|string      emit a whisper cue (ducks the voice bus)
 * }) -> { kind, variant, text, durMs } | null
 *
 * fire('flash_burst', {
 *   count?: number         default = burstCountForHeat(heat) (1..10 window)
 *   clickSafe?: boolean    INPUT-TRUST LAW: true over any click/tap-precision
 *                          surface -> pointer-events:none decoration only
 *   clickable?: boolean    default true when !clickSafe; clickable bursts carry
 *                          the escape guard (6 interactions / 5s -> cleared)
 *   onPop?: (node) => void; onForceComplete?: (why) => void
 *   hydraGen?: 0..5        pop-splits (default 1, 0 on coarse/low-motion)
 *   sizePx?, holdMs?, alpha?, x?, y?, url?, assetKind?: 'still'|'loop'
 *   variant?: 'single'|'scatter'|'double'|'hydra'
 * }) -> { kind, variant, count, clickSafe, clickable, live, cancel(), escape } | null
 *
 * fire('gif_burst', { ...same as flash_burst, but never clickable })
 *   node cap 10 (flash_burst is 20, or 3 on a coarse-pointer/low-motion device).
 *
 * fire('audio_trigger', {
 *   name: string           sfx id (the shell/host owns the audio element)
 *   level?: 0..1           pre-caps comfort level (default .6)
 *   bus?: 'fx'|'voice'|'tutorial'|'drops'|'music'
 *   duck?: 'voice'|'spotlight'|'voiceUnderSpotlight'|true
 *                          emits a duck request under DTRH's .4/.25/.15 policy,
 *                          scaled by the player's duckDepth cap
 *   duckMs?: number
 *   pitch?: 0.5..2         pass-through; shell/audio.js owns the clamp
 *   url?: string           A CLIP: a same-origin ccp.* media url the mixer plays
 *                          through the bus INSTEAD of synthesising `name`. The
 *                          level is still this call's clamped one, so a clip can
 *                          never outrun the channel. A host that cannot play it
 *                          falls back to the `name` recipe.
 *   key?: string           the clip's VOICE SLOT: a re-fire on the same key cuts
 *                          the one still playing (no pile-up on a fast sequence)
 *   maxMs?, fadeMs?: number  truncate + fade the clip (default 1200 / 180)
 * }) -> { kind, name, level, duck }
 *
 * sustain('row_drift', {
 *   targets: Element[]     the rows/columns to slide
 *   axis?: 'x'|'y'; amplitudeMult?, speedMult?: number; stagger?: false
 *   variant?: 'sway'|'slide'|'shear'
 * })  — reduced motion / motionLevel 0 -> slow opacity breathing, no transforms.
 *
 * sustain('bubble_field', {
 *   max?: number (<=18); alpha?, sizeMult?, cadenceMs?: number
 *   clickSafe?: boolean    true = decoration (no pointer events)
 *   onPop?: (node) => void — makes bubbles clickable AND arms the escape guard
 *   onForceComplete?: (why) => void; media?: true (draw stills into the bubbles)
 *   variant?: 'drift'|'rise'|'swarm'
 * })
 *
 * sustain('wash', {
 *   variant?: 'pink'|'spiral'|'drain'|'sublim'   (the ONE element per kind)
 *   strength?: 0..1; alpha?: 0..1; holdMs?: number; durationMult?: number
 *   url?: string           spiral image (else the injected spiralUrl provider)
 *   sustainForever?: true  hold until stop('wash') instead of fading on a deadline
 *   sfx?: true|string
 * })  — re-triggering the same kind REFRESHES the deadline; DOM never piles.
 *
 * sustain('gif_rain', {
 *   durationMs?, durationMult?, strength?, alpha?: number
 *   clickSafe?: boolean (default true); onPop?: (node) => void
 *   assetKind?: 'loop'|'still'; variant?: 'light'|'steady'|'downpour'
 * })  — node cap 14; skipped entirely under reduced motion.
 *
 * sustain('ambient_field', {
 *   kind?: 'motes'|'confetti'|'petals'|'embers'|'specks'|'glints'|'ash'|'bubbles'|'goldleaf'|'coins'
 *   density?: 0..1 (default = channels.bgIntensity); count?, alpha?, color?
 * })
 *
 * sustain('crt', { level?: 0..1, variant?: 'scanline'|'chroma'|'bloom' })
 *
 * ceremony('stamp',        { label, tone?:'good'|'bad'|'gild', variant?, durMs?, sfx? })
 * ceremony('streak_meter', { streak, anchor? })
 * ceremony('jackpot',      { intensity?, sfx?, garnish?:false })
 * ceremony('near_miss',    { intensity? })
 *
 * setpiece({ key, eligiblePhases?, perBeatChance, oncePerRunGate?, forceTail?,
 *            maxPerPhase?, minGap?, cosmetic?, run? }) -> { key, force(ctx), unregister() }
 *
 * beat({ garnish?:boolean, ctx?:any }) -> { beat, phase, fired[], garnish }
 *
 * ---------------------------------------------------------------------------
 * SEAMS. The engine holds NO audio handle and no bridge handle. It dispatches
 * CustomEvents on `document`:
 *   'arcademy-sfx' { name, level, bus, duck? }   audio + duck requests
 *   'arcademy-fx'  { kind, variant }             what just fired (diagnostics/telemetry)
 *   'arcademy-log' { msg }                       the only diagnostic channel without devtools
 * An optional `bus` ({emit(name, detail)} or a function) is called in addition.
 * ==========================================================================*/

import {
  DEFAULT_CAPS, capsFrom, clampToCaps, clampIntensity, clampEffectIntensity,
  heatToChannels, clamp01, pct01,
} from '../core/caps.js';
import { makeRng } from '../core/rng.js';
import {
  channelsToVisual, intensityCurve, plainShare, isPlainBeat, NODE_CAPS,
} from './curves.js';
import { createVariantPool, createGarnishBag, VARIANTS, GARNISH_MIN_INTENSITY, GARNISH_MIN_HEAT } from './variants.js';
import { createSetpieceDirector } from './setpieces.js';
import { createRewardSchedule, RewardMode } from './schedule.js';
import { createOneshots } from './oneshots.js';
import { createSustained } from './sustained.js';
import { createCeremonies } from './ceremonies.js';
import { createEscapeGuard } from './escape.js';
import { injectStyle } from './style.js';
import { hasDom, hasWindow, createTimers, probeReducedMotion, probeCoarsePointer } from './util.js';

export const FIRE_KINDS = Object.freeze(['glitch_swap', 'sub_flash', 'flash_burst', 'gif_burst', 'audio_trigger']);
export const SUSTAIN_KINDS = Object.freeze(['row_drift', 'bubble_field', 'wash', 'gif_rain', 'ambient_field', 'crt']);
export const CEREMONY_KINDS = Object.freeze(['stamp', 'streak_meter', 'jackpot', 'near_miss']);

/** Bundled mono-pink placeholder tiles — the floor under every asset draw. */
const PLACEHOLDERS = ['ae-ph-1.svg', 'ae-ph-2.svg', 'ae-ph-3.svg', 'ae-ph-4.svg', 'ae-ph-5.svg', 'ae-ph-6.svg']
  .map((f) => {
    try { return new URL('../provider/assets/' + f, import.meta.url).href; } catch { return '../provider/assets/' + f; }
  });

export function createEngine(options = {}) {
  const opts = options || {};
  const capsVec = capsFrom(opts.caps || DEFAULT_CAPS, opts.masterIntensity);
  const effectI = clampEffectIntensity(opts.effectIntensity);
  const rng = typeof opts.rng === 'function' ? opts.rng : makeRng(opts.seed || ('arcademy|' + (opts.gameKey || 'class')));
  const seed = opts.seed == null ? ('arcademy|' + (opts.gameKey || 'class')) : String(opts.seed);
  const motionLevel = Number.isFinite(opts.motionLevel) ? Math.max(0, Math.min(2, opts.motionLevel | 0)) : 2;
  const reducedMotion = opts.reducedMotion == null ? probeReducedMotion() : !!opts.reducedMotion;
  const coarse = probeCoarsePointer();
  const allow = Array.isArray(opts.effectsConsumed) && opts.effectsConsumed.length ? opts.effectsConsumed.slice() : null;
  const wordsIn = Array.isArray(opts.words) ? opts.words.filter((w) => typeof w === 'string' && w.trim()) : [];

  let heat = 0;
  let channels = clampToCaps(heatToChannels(0), capsVec);
  let suspended = false;
  let disposed = false;
  const refusals = new Set();     // allowlist misses, logged ONCE each

  const timers = createTimers();
  const variantPools = new Map();
  const garnishBag = createGarnishBag(rng);
  const schedule = createRewardSchedule({ seed: seed + '|reward', mode: RewardMode.VariableRatio });
  const director = createSetpieceDirector({ rng, log: (m) => log(m) });

  /* ---- asset pool (never blocks a draw) ---------------------------------- */
  let pool = null;
  let poolPending = false;
  const local = { loop: [], still: [] };

  function bindAssets(a) {
    if (!a) return;
    if (typeof a.next === 'function') { pool = a; return; }
    if (typeof a.claim === 'function' && !poolPending) {
      poolPending = true;
      try {
        Promise.resolve(a.claim(opts.assetSpec || { loops: 8, stills: 8, targets: 0, canvasSafe: false }))
          .then((p) => { if (p && typeof p.next === 'function' && !disposed) pool = p; })
          .catch(() => { /* silent local fallthrough (FlashService posture) */ });
      } catch { /* ignore */ }
    }
  }
  bindAssets(opts.assets);

  /** Synchronous url draw: the pool is non-blocking by contract, and if there is
   *  no pool at all we still return a bundled placeholder. Never throws, never
   *  returns a pending promise. */
  function assetUrlSync(kind) {
    const k = kind === 'loop' || kind === 'gif' ? 'loop' : 'still';
    if (pool) {
      try {
        const got = pool.next(k);
        if (got && got.url) return got.url;
      } catch { /* fall through */ }
    }
    if (local[k] && local[k].length) return local[k][Math.floor(rng() * local[k].length)];
    if (!PLACEHOLDERS.length) return null;
    return PLACEHOLDERS[Math.floor(rng() * PLACEHOLDERS.length)];
  }
  function spiralUrl() {
    if (typeof opts.spiralUrl === 'function') { try { return opts.spiralUrl(); } catch { return null; } }
    return null;   // CSS conic-gradient spiral is the built-in look
  }

  /* ---- seams ------------------------------------------------------------- */
  function emit(name, detail) {
    if (hasDom()) {
      try { document.dispatchEvent(new CustomEvent(name, { detail })); } catch { /* ignore */ }
    }
    const bus = opts.bus;
    if (!bus) return;
    // three accepted bus shapes: a plain function, an {emit} pub-sub, or any
    // EventTarget (the shell passes `window`) — nobody has to adapt to us.
    if (typeof bus === 'function') { try { bus(name, detail); } catch { /* ignore */ } }
    else if (typeof bus.emit === 'function') { try { bus.emit(name, detail); } catch { /* ignore */ } }
    else if (typeof bus.dispatchEvent === 'function' && bus !== document) {
      try { bus.dispatchEvent(new CustomEvent(name, { detail })); } catch { /* ignore */ }
    }
  }
  function log(msg) { emit('arcademy-log', { msg: 'engine: ' + msg }); }
  function fx(kind, variant) { emit('arcademy-fx', { kind, variant: variant || null }); }
  function sfxRaw(detail) { emit('arcademy-sfx', detail); }
  function sfx(name, level, extra) {
    const detail = { name, level: clampIntensity(level == null ? 0.5 : level, capsVec), bus: (extra && extra.bus) || 'fx' };
    if (extra && extra.duck) {
      const policy = { voice: 0.4, spotlight: 0.25, voiceUnderSpotlight: 0.15 }[extra.duck];
      if (policy != null) detail.duck = { target: extra.duck, mult: 1 - (1 - policy) * clamp01(channels.duckDepth), ms: 250 };
    }
    sfxRaw(detail);
  }

  /* ---- the layer (lazy, so headless import never touches the DOM) -------- */
  const layers = { root: null, back: null, mid: null, front: null };
  function ensureLayer() {
    if (!hasDom() || layers.root) return layers.root;
    injectStyle();
    const mount = opts.mount && opts.mount.appendChild ? opts.mount : (document.body || null);
    if (!mount) return null;
    const root = document.createElement('div');
    root.className = 'ae-layer';
    root.setAttribute('data-ae-layer', opts.gameKey || 'arcademy');
    const back = document.createElement('div'); back.className = 'ae-back';
    const mid = document.createElement('div'); mid.className = 'ae-mid';
    const front = document.createElement('div'); front.className = 'ae-front';
    root.appendChild(back); root.appendChild(mid); root.appendChild(front);
    // the engine must never steal the game's input surface
    try {
      const pos = hasWindow() && window.getComputedStyle ? window.getComputedStyle(mount).position : 'static';
      if (pos === 'static' && mount.style) mount.style.position = 'relative';
    } catch { /* ignore */ }
    mount.appendChild(root);
    layers.root = root; layers.back = back; layers.mid = mid; layers.front = front;
    return root;
  }

  /* ---- ctx handed to the primitive families ------------------------------ */
  const motionScalar = () => (reducedMotion || motionLevel === 0 ? 0 : (motionLevel === 1 ? 0.6 : 1));
  const ctx = {
    get layers() { ensureLayer(); return layers; },
    timers, rng,
    heat: () => heat,
    channels: () => channels,
    visual: () => channelsToVisual(channels),
    /** caps + master (never an absolute). */
    magnitude: (v) => clampIntensity(v, capsVec),
    /**
     * THE CEILING RULE. A channel's clamped value IS the ceiling for every effect
     * that rides it: a game may ask for less, never for more (GROUND-RULES §5 —
     * "global values are ceilings"). `requested` null -> spend the whole ceiling.
     * A ceiling at (or under) 0.001 means the player capped this channel OFF, and
     * the primitive must refuse to render at all.
     */
    ceiling(channel, requested) {
      const c = clamp01(channels[channel]);
      return requested == null ? c : Math.min(pct01(requested), c);
    },
    /** 0..1 or DTRH 0-100, normalised. */
    pct: pct01,
    /** caps + master + the photosensitivity guard: STROBE-CLASS ONLY. */
    strobe: (v) => clamp01(clampIntensity(v, capsVec) * effectI),
    motion: motionScalar,
    reduced: () => reducedMotion || motionLevel === 0,
    lite: () => coarse || motionLevel <= 1,
    words: () => wordsIn,
    assetUrlSync,
    spiralUrl,
    sfx, sfxRaw, fx, log,
    variant(kind, intensity, force) {
      let p = variantPools.get(kind);
      if (!p) { p = createVariantPool(VARIANTS[kind] || [kind], rng); variantPools.set(kind, p); }
      return p.pick(intensity, force);
    },
    nodes(targets) {
      if (!targets) return [];
      if (Array.isArray(targets)) return targets.filter(Boolean);
      if (typeof targets.length === 'number' && typeof targets !== 'string') return Array.prototype.slice.call(targets).filter(Boolean);
      return [targets];
    },
    forceGarnish(names, intensity) { applyGarnish(garnishBag.force(names), intensity); },
  };

  const oneshots = createOneshots(ctx);
  const sustained = createSustained(ctx);
  const ceremonies = createCeremonies(ctx);

  /* ---- garnish rotation -------------------------------------------------- */
  function applyGarnish(name, intensity) {
    if (!name) return null;
    const i = clamp01(intensity == null ? channels.bgIntensity : intensity);
    if (name === 'sublim') return oneshots.sub_flash({ strength: i, variant: 'centre' });
    const variant = name === 'drain' ? 'drain' : name;                  // pink | spiral | drain
    return sustained.sustain('wash', { variant, strength: i });
  }

  /* ---- allowlist --------------------------------------------------------- */
  function permitted(kind) {
    if (!allow) return true;
    if (allow.includes(kind)) return true;
    if (!refusals.has(kind)) {
      refusals.add(kind);
      log('refused "' + kind + '": not in this class manifest (effectsConsumed)');
    }
    return false;
  }

  /* ---- facade ------------------------------------------------------------ */
  function setHeat(h01) {
    heat = clamp01(h01);
    channels = clampToCaps(heatToChannels(heat), capsVec);
    if (!suspended) sustained.retuneAll();
    return channels;
  }

  function fire(kind, o) {
    if (disposed || suspended) return null;
    if (!FIRE_KINDS.includes(kind)) { log('unknown fire kind "' + kind + '"'); return null; }
    if (!permitted(kind)) return null;
    if (kind !== 'audio_trigger') ensureLayer();
    const fn = oneshots[kind];
    try { return fn(o || {}); } catch (e) { log(kind + ' failed: ' + (e && e.message)); return null; }
  }

  /** Additive: sub_flash as a cadence-driven stream (see header note). */
  const subStream = { timer: 0, opts: null };
  function startSubStream(o) {
    subStream.opts = o || {};
    const tick = () => {
      if (disposed || suspended || !subStream.opts) return;
      oneshots.sub_flash(subStream.opts);
      const ms = cadenceMs('sub_flash');
      if (!Number.isFinite(ms)) { subStream.timer = 0; return; }
      subStream.timer = timers.after(Math.max(180, ms * (0.75 + rng() * 0.5)), tick);
    };
    const ms = cadenceMs('sub_flash');
    if (!Number.isFinite(ms)) return null;
    subStream.timer = timers.after(Math.max(180, ms * (0.5 + rng() * 0.5)), tick);
    return { kind: 'sub_flash', variant: 'stream', stop() { if (subStream.timer) timers.cancel(subStream.timer); subStream.timer = 0; subStream.opts = null; } };
  }

  function sustain(kind, o) {
    if (disposed || suspended) return null;
    if (kind === 'sub_flash') {
      if (!permitted('sub_flash')) return null;
      ensureLayer();
      if (subStream.timer) { subStream.opts = o || {}; return { kind: 'sub_flash', variant: 'stream', stop() { if (subStream.timer) timers.cancel(subStream.timer); subStream.timer = 0; } }; }
      return startSubStream(o);
    }
    if (!SUSTAIN_KINDS.includes(kind)) { log('unknown sustain kind "' + kind + '"'); return null; }
    if (!permitted(kind)) return null;
    ensureLayer();
    try { return sustained.sustain(kind, o || {}); } catch (e) { log(kind + ' failed: ' + (e && e.message)); return null; }
  }

  function stop(kind) {
    if (kind === 'sub_flash') {
      if (subStream.timer) timers.cancel(subStream.timer);
      subStream.timer = 0; subStream.opts = null;
      return true;
    }
    return sustained.stop(kind, false);
  }

  function setpiece(descriptor) {
    if (!descriptor || !descriptor.key) { log('setpiece needs a key'); return null; }
    return director.register(descriptor);
  }

  function beat(o = {}) {
    if (disposed || suspended) return { beat: director.beatIndex, phase: director.phase, fired: [], garnish: null };
    const res = director.beat(o.ctx || { engine: api, heat });
    let garnish = null;
    if (o.garnish) {
      const i = clamp01(o.intensity == null ? channels.bgIntensity : o.intensity);
      if (i >= GARNISH_MIN_INTENSITY && heat >= GARNISH_MIN_HEAT) {
        const avail = ['pink', 'drain', 'spiral'];
        if (wordsIn.length || assetUrlSync('still')) avail.push('sublim');
        garnish = garnishBag.draw(avail);
        applyGarnish(garnish, i);
      }
    }
    return { beat: res.beat, phase: res.phase, fired: res.fired, gateUsed: res.gateUsed, garnish };
  }

  function ceremony(kind, o) {
    if (disposed || suspended) return null;
    if (!CEREMONY_KINDS.includes(kind)) { log('unknown ceremony "' + kind + '"'); return null; }
    ensureLayer();
    try { return ceremonies[kind](o || {}); } catch (e) { log('ceremony ' + kind + ' failed: ' + (e && e.message)); return null; }
  }

  /** Current heat-scaled cadence in ms (Infinity = that channel is silent). */
  function cadenceMs(kind) {
    const v = channelsToVisual(channels);
    if (kind === 'sub_flash') return v.subMs;
    if (kind === 'bubble_field') return v.bubbleMs;
    if (kind === 'flash_burst' || kind === 'gif_burst') return v.flashMs;
    return Infinity;
  }

  function suspend(on) {
    const want = !!on;
    if (want === suspended) return suspended;
    suspended = want;
    if (suspended) {
      // EVERYTHING, NOW: hide the layer, stop sustains, kill every timer/rAF.
      if (layers.root) layers.root.classList.add('ae-suspended');
      try { sustained.stopAll(true); } catch { /* ignore */ }
      if (subStream.timer) { timers.cancel(subStream.timer); subStream.timer = 0; }
      timers.kill();
      oneshots.reset();
      ceremonies.reset();
      log('suspended');
    } else {
      if (layers.root) layers.root.classList.remove('ae-suspended');
      log('resumed');
    }
    return suspended;
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    try { sustained.stopAll(true); } catch { /* ignore */ }
    timers.dispose();
    if (layers.root) { try { layers.root.remove(); } catch { /* ignore */ } }
    layers.root = layers.back = layers.mid = layers.front = null;
    if (pool && typeof pool.release === 'function') { try { pool.release(); } catch { /* ignore */ } }
    pool = null;
    log('disposed');
  }

  const api = {
    // --- the pinned surface (BUILD-CONTRACT §5) ---------------------------
    setHeat, fire, sustain, stop, setpiece, beat, ceremony, suspend, dispose,
    // --- additive helpers -------------------------------------------------
    setPhase: (p) => director.setPhase(p),
    armTail: (on) => director.armTail(on),
    intensityCurve: (p01, o) => intensityCurve(p01, o),
    plainShare: (i01, floor, early) => plainShare(i01, floor, early),
    isPlainBeat: (i01, floor, early) => isPlainBeat(i01, rng(), floor, early),
    cadenceMs,
    rewardRoll: (o = {}) => schedule.roll(Object.assign({ heat }, o)),
    garnish: (o = {}) => applyGarnish(garnishBag.draw(o.avail || ['pink', 'drain', 'spiral', 'sublim']), o.intensity),
    escapeGuard: (o = {}) => createEscapeGuard(Object.assign({ timers }, o)),
    channels: () => Object.assign({}, channels),
    visual: () => channelsToVisual(channels),
    words: () => wordsIn.slice(),
    // --- introspection ----------------------------------------------------
    get heat() { return heat; },
    get suspended() { return suspended; },
    get disposed() { return disposed; },
    get caps() { return Object.assign({}, capsVec); },
    get effectIntensity() { return effectI; },
    get motionLevel() { return motionLevel; },
    get reducedMotion() { return reducedMotion || motionLevel === 0; },
    get manifest() { return allow ? allow.slice() : null; },
    diagnostics() {
      return {
        heat,
        channels: Object.assign({}, channels),
        live: Object.assign({}, oneshots.live, sustained.live),
        caps: NODE_CAPS,
        sustains: [...sustained.active.keys()],
        setpieces: director.stats(),
        beat: director.beatIndex,
        phase: director.phase,
        refused: [...refusals],
        timers: timers.size,
        suspended, disposed,
      };
    },
  };
  return api;
}

export default createEngine;
