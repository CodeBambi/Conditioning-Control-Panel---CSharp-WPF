/* ============================================================================
 * games/sort/pressure.js - THE SURGE. The casino lights the room and the
 * trickster lies about it; this file is what the room DOES to you as your CHAIN
 * climbs. It is the pitch's section 7 ladder, and it is driven by ONE number:
 * the rung, which is nothing but your own clean streak.
 *
 * THE LADDER (cumulative; the rung is index.js's, never ours)
 *   r0   the clean room. The stack breathes, the ring turns, and that is all.
 *   r1   BREATH      a pink wash breathes behind the stack     [wash:pink]
 *   r2   BURST       gif bursts ride your PERFECTs             [gif_burst]
 *   r3   WHEEL       the full spiral wash wakes behind the     [wash:spiral,
 *                    stack and HOLDS                            sustainForever]
 *   r4   SHUDDER     the wall glitch-shudders on every THUD    [game-local class]
 *   r5   RAIN        gif rain falls on the thud                [gif_rain]
 *   r6   SUBS+CHROMA a sub-flash stream and the crt turns      [sub_flash, crt]
 *   r7   FLASHES     flash bursts ride the PERFECTs            [flash_burst]
 *   r8   THE FLOOD   the wall you sorted takes the whole stage [wall.flood +
 *                    and the cards fly INTO it                  ambient_field,
 *                                                               gif_rain]
 *
 * STEPPING DOWN IS A FADE, NEVER A SNAP. A wrong swipe costs one rung, and the
 * storm behind it walks down after a 1.5s hysteresis - long enough that a
 * single mistake at rung 7 does not strobe the room off and back on, short
 * enough that the room is honest about where you are standing.
 *
 * THE WASH TRAP (CLAUDE.md trap 33): `engine.stop('wash')` kills EVERY wash
 * variant, and two live here at once (pink and spiral). A rung is stepped DOWN
 * by re-triggering its own variant at a whisper alpha with a short hold, so it
 * fades on its own deadline and takes nothing else with it. `stop('wash')` is
 * called from end() and destroy() and NOWHERE else.
 *
 * NODE BUDGET: ONE class name on the stage and one on the wall. Everything
 * visual is the ENGINE's, through index.js's welded facade (clickSafe on every
 * burst, the tier's audio ceiling, effectsConsumed enforced, null while frozen).
 * This file creates no elements at all.
 *
 * TABLE LAW AUDIT (House Rules)
 *   I   ledger honest - reads `rung` off the events it is handed and writes
 *       nothing. It renders no text and invents no lexicon row.
 *   II  input honest  - no node of ours exists, the stack is never covered by
 *       anything that could take a press, and THE FLOOD moves the card that
 *       has ALREADY been committed, never the one under the finger.
 *   III never still   - from rung 1 the room breathes.
 *   V   seeded        - per-tag mulberry32 off seed+'|sort-pressure|<tag>',
 *       append-only. No Math.random anywhere.
 *   VI  exits sacred  - bgIntensity 0 or `sort_bg_fade` 0 disarms the whole
 *       ladder; reduced motion keeps the washes and drops everything that
 *       flies; every timer rides the game's registry (so a freeze kills them);
 *       end() and destroy() stop every sustain and put the wall back.
 *   VII lexicon       - this file renders no text.
 *
 * THE PLAYER'S OWN DIAL. `sort_bg_fade` is the class's one setting and it means
 * "how brightly the background burns". Zero is not a dimmer, it is a REFUSAL:
 * at zero this deck never fires anything. Above zero it scales the wash alphas
 * against the default of 0.35, and the engine's own ceilings clamp the rest -
 * a player can turn the storm down, never up past the caps vector.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';
import { CHAIN } from './chain.js';

export const SURGE = Object.freeze({
  /** What each rung switches ON, cumulative. Index = rung. */
  ADDS: Object.freeze([
    Object.freeze([]),
    Object.freeze(['breath']),
    Object.freeze(['burst']),
    Object.freeze(['wheel']),
    Object.freeze(['shudder']),
    Object.freeze(['rain']),
    Object.freeze(['subs', 'chroma']),
    Object.freeze(['flashes']),
    Object.freeze(['flood']),
  ]),
  /** Stepping DOWN waits this long and fades. Stepping UP is immediate. */
  HYST_MS: 1500,
  /** The wash bands, low heat to high. */
  BREATH_ALPHA: Object.freeze([0.1, 0.34]),
  FLOOD_BREATH_ALPHA: Object.freeze([0.26, 0.5]),
  BREATH_HOLD_MS: 2600,
  WHEEL_ALPHA: Object.freeze([0.16, 0.4]),
  /** The step-down whisper: low enough to end a hold, never a stop(). */
  WHISPER_ALPHA: 0.02,
  WHISPER_HOLD_MS: 260,
  /** crt levels. */
  CHROMA_LEVEL: Object.freeze([0.3, 0.62]),
  /** gif_burst on a PERFECT: the chance it rides, and how big. */
  BURST_CHANCE: Object.freeze([0.45, 0.9]),
  BURST_VMIN: Object.freeze([0.22, 0.36]),
  BURST_HOLD_MS: Object.freeze([700, 1150]),
  /** gif_rain on a thud. */
  RAIN_MS: Object.freeze([900, 1500]),
  FLOOD_RAIN_MS: 4200,
  /** flash_burst on a PERFECT, from rung 7. */
  FLASH_CHANCE: Object.freeze([0.4, 0.85]),
  /** THE FLOOD's ambient field. */
  FLOOD_EMBERS: Object.freeze([0.3, 0.62]),
  /** The wall's shudder class, and how long it stands. */
  SHUDDER_MS: 260,
  /** Repaint the held washes only when the heat has actually moved. */
  HEAT_STEP: 0.06,
  /** The class setting this deck answers to, and its shipped default. */
  FADE_KEY: 'sort_bg_fade',
  FADE_DEFAULT: 0.35,
});

/* ------------------------------------------------------------------ tools -- */
function clamp01(v) { const n = Number(v); return !isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, x) { return band[0] + (band[1] - band[0]) * clamp01(x); }
function addCls(node, c) { try { if (node && node.classList) node.classList.add(c); } catch (e) { /* noop */ } }
function delCls(node, c) { try { if (node && node.classList) node.classList.remove(c); } catch (e) { /* noop */ } }

/** The rung a surge should be standing on. Pure, so the suite can pin it. */
export function surgeRung(rung) {
  const r = Math.round(Number(rung) || 0);
  return r < 0 ? 0 : r > CHAIN.MAX_RUNG ? CHAIN.MAX_RUNG : r;
}
/** Everything switched on at or below a rung, in ladder order. Pure. */
export function addsUpTo(rung) {
  const out = [];
  for (let i = 0; i <= surgeRung(rung); i++) for (const a of SURGE.ADDS[i]) out.push(a);
  return out;
}

/* ============================================================================
 * THE DECK
 * ==========================================================================*/
export function create(o) {
  const bag = o || {};
  const ctx = bag.ctx || {};
  const bus = bag.bus || { on() { return () => {}; } };
  const readState = typeof bag.S === 'function' ? bag.S : () => null;
  const timers = bag.timers || null;
  const engine = bag.engine || null;
  const reduced = !!bag.reduced;
  const say = typeof bag.log === 'function' ? bag.log : () => {};

  const armedBase = !!timers && typeof timers.after === 'function' && !!engine;

  /* ---- the player's dial, read ONCE: a setting that moved mid-class would be
     a storm that changed its mind, and the room is not the settings page ----- */
  const fade = (() => {
    try {
      const s = ctx.settings || {};
      const v = Object.prototype.hasOwnProperty.call(s, SURGE.FADE_KEY)
        ? Number(s[SURGE.FADE_KEY]) : SURGE.FADE_DEFAULT;
      return isFinite(v) ? Math.max(0, Math.min(0.8, v)) : SURGE.FADE_DEFAULT;
    } catch (e) { return SURGE.FADE_DEFAULT; }
  })();
  /** A scale against the shipped default, never above 1: the caps are the roof. */
  const fadeScale = Math.min(1, fade / SURGE.FADE_DEFAULT);
  const refused = fade <= 0.001;

  function capsOk() {
    if (refused) return false;
    let v = null;
    try {
      const ch = engine && typeof engine.channels === 'function' ? engine.channels() : null;
      if (ch && ch.bgIntensity != null) v = Number(ch.bgIntensity);
    } catch (e) { /* noop */ }
    if (v == null) {
      try { if (ctx.caps && ctx.caps.bgIntensity != null) v = Number(ctx.caps.bgIntensity); }
      catch (e) { /* noop */ }
    }
    if (v == null || !isFinite(v)) return true;
    return v > 0.001;
  }

  /* ---- seeded streams ----------------------------------------------------- */
  const seed = (() => { try { return String((readState() || {}).seed || 'sort'); } catch (e) { return 'sort'; } })();
  const seedBase = seed + '|sort-pressure|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- state -------------------------------------------------------------- */
  let destroyed = false;
  let started = false;
  let stopped = false;
  let paused = false;
  let heat = 0.2;
  let lastPaintHeat = -1;
  let rung = 0;
  let wantRung = 0;
  let downTimer = 0;
  let shudderTimer = 0;
  const on = new Set();
  const fired = {};
  const offs = [];

  const halted = () => destroyed || stopped || paused || !armedBase || !capsOk();
  function after(ms, fn) {
    if (!armedBase || destroyed) return 0;
    try { return timers.after(ms, () => { if (!destroyed) fn(); }); }
    catch (e) { return 0; }
  }
  function cancel(id) {
    if (!id || !timers) return;
    try { if (typeof timers.clear === 'function') timers.clear(id); } catch (e) { /* noop */ }
  }
  function nodes() { const s = readState(); return (s && s.nodes) || null; }
  function wall() { const s = readState(); return (s && s.wall) || null; }

  /* ---- the engine, counted ------------------------------------------------ */
  function count(k) { fired[k] = (fired[k] || 0) + 1; }
  function fire(kind, opts) {
    if (halted()) return null;
    count(kind); if (opts && opts.variant) count(kind + ':' + opts.variant);
    try { return engine.fire(kind, opts || {}); } catch (e) { return null; }
  }
  function sustain(kind, opts) {
    if (halted()) return null;
    count(kind); if (opts && opts.variant) count(kind + ':' + opts.variant);
    try { return engine.sustain(kind, opts || {}); } catch (e) { return null; }
  }
  function stop(kind) {
    if (!armedBase) return;
    count('stop:' + kind);
    try { engine.stop(kind); } catch (e) { /* noop */ }
  }

  /* ============================================================= THE WASHES */
  function breathOn(flood) {
    sustain('wash', {
      variant: 'pink',
      alpha: lerp(flood ? SURGE.FLOOD_BREATH_ALPHA : SURGE.BREATH_ALPHA, heat) * fadeScale,
      holdMs: SURGE.BREATH_HOLD_MS,
      sustainForever: true,
    });
  }
  function wheelOn() {
    sustain('wash', {
      variant: 'spiral',
      alpha: lerp(SURGE.WHEEL_ALPHA, heat) * fadeScale,
      sustainForever: true,
    });
  }
  /** THE STEP-DOWN. Never stop('wash') - trap 33, and two washes live here. */
  function whisperOut(variant) {
    sustain('wash', { variant, alpha: SURGE.WHISPER_ALPHA, holdMs: SURGE.WHISPER_HOLD_MS });
  }

  /* ============================================================== THE FLOOD */
  function flood(state) {
    const n = nodes();
    const w = wall();
    if (state) {
      /* the wall stops being a backdrop: it takes the stage, and the room's own
         sheet turns a committed card's flight INTO it rather than off-screen */
      try { if (w && typeof w.flood === 'function') w.flood(true); } catch (e) { /* noop */ }
      try { if (w && typeof w.show === 'function') w.show(CHAIN.MAX_RUNG, false); } catch (e) { /* noop */ }
      if (n) addCls(n.stage, 'is-flood');
      if (!reduced) {
        sustain('ambient_field', { kind: 'embers', density: lerp(SURGE.FLOOD_EMBERS, heat) * fadeScale });
        sustain('gif_rain', { durationMs: SURGE.FLOOD_RAIN_MS, variant: 'downpour', strength: heat, clickSafe: true });
      }
      breathOn(true);
      return;
    }
    try { if (w && typeof w.flood === 'function') w.flood(false); } catch (e) { /* noop */ }
    if (n) delCls(n.stage, 'is-flood');
    stop('ambient_field');
    stop('gif_rain');
    if (on.has('breath')) breathOn(false);
  }

  /* ============================================================== THE RUNGS */
  function applyAdd(add, up) {
    if (up) {
      on.add(add);
      if (add === 'breath') breathOn(false);
      else if (add === 'wheel') wheelOn();
      else if (add === 'subs' && !reduced) sustain('sub_flash', {});
      else if (add === 'chroma') sustain('crt', { level: lerp(SURGE.CHROMA_LEVEL, heat) * fadeScale, variant: 'chroma' });
      else if (add === 'flood') flood(true);
      /* burst / shudder / rain / flashes are EVENT driven: nothing to switch on */
      return;
    }
    on.delete(add);
    if (add === 'breath') whisperOut('pink');
    else if (add === 'wheel') whisperOut('spiral');
    else if (add === 'subs') stop('sub_flash');
    else if (add === 'chroma') stop('crt');
    else if (add === 'flood') flood(false);
  }

  function setRung(r) {
    const to = surgeRung(r);
    if (to === rung) return;
    const up = to > rung;
    if (up) { for (let k = rung + 1; k <= to; k++) for (const a of SURGE.ADDS[k]) applyAdd(a, true); }
    else { for (let k = rung; k > to; k--) for (const a of SURGE.ADDS[k]) applyAdd(a, false); }
    rung = to;
    say('pressure: rung ' + rung + ' (' + (up ? 'up' : 'down') + ') [' + Array.from(on).join(' ') + ']');
  }

  /**
   * The rung moved. UP is immediate - you earned it this second. DOWN waits out
   * the 1.5s the room spends fading its own ladder, so a mistake costs a rung
   * and not a strobe.
   */
  function want(r) {
    wantRung = surgeRung(r);
    if (!started || stopped || destroyed) return;
    if (wantRung > rung) { cancel(downTimer); downTimer = 0; setRung(wantRung); return; }
    if (wantRung < rung) {
      if (downTimer) return;
      downTimer = after(SURGE.HYST_MS, () => {
        downTimer = 0;
        if (wantRung < rung) setRung(wantRung);
      });
      return;
    }
    cancel(downTimer); downTimer = 0;
  }

  /** Held washes follow the heat, but only when the heat has actually moved. */
  function repaintHeat() {
    if (Math.abs(heat - lastPaintHeat) < SURGE.HEAT_STEP) return;
    lastPaintHeat = heat;
    if (on.has('breath')) breathOn(on.has('flood'));
    if (on.has('wheel')) wheelOn();
    if (on.has('chroma')) sustain('crt', { level: lerp(SURGE.CHROMA_LEVEL, heat) * fadeScale, variant: 'chroma' });
  }

  /* =============================================================== THE WIRE */
  /** THE LATE-BUILD GUARD: the deal arms us if start() ran before we existed. */
  function ensureStarted() {
    if (started || destroyed || stopped) return;
    api.start();
  }

  function onDeal(ev) {
    ensureStarted();
    if (ev && ev.rung != null) want(ev.rung);
  }

  function onRung(ev) {
    if (!ev) return;
    want(ev.to);
  }

  /** A PERFECT is what the bursts ride: the payoff rides the skill. */
  function onPerfect() {
    if (halted() || reduced) return;
    if (on.has('burst') && roll('burst-go') < lerp(SURGE.BURST_CHANCE, heat)) {
      fire('gif_burst', {
        count: 1,
        sizePx: Math.round(lerp(SURGE.BURST_VMIN, heat) * 900),
        holdMs: Math.round(lerp(SURGE.BURST_HOLD_MS, heat)),
        strength: heat,
      });
    }
    if (on.has('flashes') && roll('flash-go') < lerp(SURGE.FLASH_CHANCE, heat)) {
      fire('flash_burst', { count: 1, strength: heat });
    }
  }

  /** THE THUD is the wall's beat, so the wall's own weather hangs off it. */
  function onLand() {
    if (halted()) return;
    if (on.has('shudder') && !reduced) {
      const w = wall();
      const el = w ? w.el : null;
      if (el) {
        delCls(el, 'is-shudder');
        try { void (el.offsetWidth); } catch (e) { /* DOM double */ }
        addCls(el, 'is-shudder');
        count('shudder');
        cancel(shudderTimer);
        shudderTimer = after(SURGE.SHUDDER_MS, () => delCls(el, 'is-shudder'));
      }
    }
    if (on.has('rain') && !on.has('flood') && !reduced) {
      fire('gif_rain', {
        durationMs: Math.round(lerp(SURGE.RAIN_MS, heat)),
        strength: heat,
        clickSafe: true,
        variant: 'steady',
      });
    }
  }

  /* ================================================================ THE API */
  const api = {
    start() {
      if (destroyed || started) return;
      started = true;
      stopped = false;
      const s = readState();
      if (s) { heat = clamp01(s.heat); rung = 0; }
      say('pressure: armed (fade ' + fade.toFixed(2)
        + (refused ? ', REFUSED' : '')
        + (capsOk() ? '' : ', CAPPED')
        + (reduced ? ', reduced' : '') + ')');
      if (s) want(s.rung);
    },

    setHeat(h) {
      const v = Number(h);
      heat = isFinite(v) ? clamp01(v) : heat;
      if (halted()) return;
      repaintHeat();
    },

    pause() { paused = true; },

    resume() {
      paused = false;
      /* a held wash whose element the engine froze comes back at the alpha the
         rung is standing on, not at the one it was born with */
      lastPaintHeat = -1;
      repaintHeat();
    },

    /**
     * The bell. Everything this deck ever asked for is put away here, and this
     * is the ONE place stop('wash') is allowed (trap 33): nothing is left to
     * take down with it.
     */
    end() {
      if (stopped) return;
      stopped = true;
      cancel(downTimer); downTimer = 0;
      cancel(shudderTimer); shudderTimer = 0;
      const n = nodes();
      const w = wall();
      if (n) delCls(n.stage, 'is-flood');
      if (w) { delCls(w.el, 'is-shudder'); try { if (typeof w.flood === 'function') w.flood(false); } catch (e) { /* noop */ } }
      if (armedBase) {
        stop('wash'); stop('crt'); stop('gif_rain');
        stop('ambient_field'); stop('sub_flash');
      }
      on.clear();
      rung = 0;
      say('pressure: stood down (' + Object.keys(fired).length + ' kinds touched)');
    },

    destroy() {
      if (!destroyed) { try { api.end(); } catch (e) { /* noop */ } }
      destroyed = true;
      for (const off of offs) { try { off(); } catch (e) { /* noop */ } }
      offs.length = 0;
    },

    diagnostics() {
      return {
        armed: armedBase && capsOk(),
        started, stopped, paused, destroyed, reduced, refused,
        fade, fadeScale, heat, rung, wantRung,
        stepping: !!downTimer,
        on: Array.from(on),
        fired: Object.assign({}, fired),
      };
    },
  };

  offs.push(bus.on('deal', onDeal));
  offs.push(bus.on('rung', onRung));
  offs.push(bus.on('perfect', onPerfect));
  offs.push(bus.on('land', onLand));
  offs.push(bus.on('end', () => { try { api.end(); } catch (e) { /* noop */ } }));

  return api;
}

export default { SURGE, create, surgeRung, addsUpTo };
