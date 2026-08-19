/* ============================================================================
 * games/daily-trigger/ladder.js - THE DISTRACTION LADDER.
 *
 * The dossier's whole difficulty model in one module: **misses ARE the dial.**
 *
 *   rung = tierStart(gradeTier) + missCount        (capped by rungCap(gradeTier))
 *
 * Deterministic - never RNG - so the punishment feels earned and the share grid's
 * storm line is an honest brag. GRADE TIERS RAISE EFFECT DIALS FIRST: tierStart
 * and the reachable ceiling both climb with the tier, and only Year 4 starts at
 * the top rung on row 1. Classic difficulty (phrase days, forced hard mode) comes
 * second, in index.js.
 *
 *   rung 0  ambient_field floor only
 *   rung 1  + sub_flash at slow cadence (candidate pollution begins)
 *   rung 2  + bubble_field decoys drifting over the board
 *   rung 3  + wash pulses (pink / spiral), one reused element per kind
 *   rung 4  + glitch_swap on keycap GLYPHS (input-honest, pointer-exempt)
 *   rung 5  + row_drift on the keyboard rows and decoy whispers
 *
 * CEILINGS, NEVER ABSOLUTES: every strength here is a *request*. The engine takes
 * min(requested, clamped channel) and multiplies strobe-class output by the
 * photosensitivity guard, so this file can only ever spend up to what the player
 * has permitted. Raising a dial is `engine.setHeat`, never a hardcoded number.
 *
 * INPUT-TRUST LAW (DECISIONS #9): the keyboard is a click-precision surface, so
 * nothing clickable is ever laid over it - bubbles are `clickSafe`, and
 * flash_burst / gif_burst are not in this game's manifest at all.
 * ==========================================================================*/

/** The top rung. Six rows of misses cannot climb past it. */
export const RUNG_MAX = 5;

/** Where the ladder STARTS per grade tier (Year 1..4). Dossier's tierStart. */
export function tierStartFor(tier) {
  const t = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  return [0, 1, 2, 5][t - 1];
}

/** The highest rung reachable per tier: Year 1 never sees the top two rungs. */
export function rungCapFor(tier) {
  const t = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  return [3, 4, RUNG_MAX, RUNG_MAX][t - 1];
}

/** The rung for a miss count, before/after both caps. */
export function rungFor(tier, misses) {
  const m = Math.max(0, Math.round(Number(misses) || 0));
  return Math.min(rungCapFor(tier), tierStartFor(tier) + m);
}

/** Heat (the ONE engine scalar) for a rung. Higher tiers breathe higher. */
export function heatFor(tier, rung) {
  const t = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  const r = Math.max(0, Math.min(RUNG_MAX, Number(rung) || 0));
  const base = 0.06 + 0.05 * (t - 1);
  const h = base + r * 0.16;
  return h < 0 ? 0 : h > 1 ? 1 : h;
}

/** Effect family -> the storm badge that family contributes to the share grid. */
export const STORM_BADGES = Object.freeze({
  sub_flash: '\u{1F4AB}',      // dizzy / whisper words
  bubble_field: '\u{1FAE7}',   // bubbles
  wash: '\u{1F300}',           // cyclone (the spiral wash)
  glitch_swap: '⚡',       // high voltage
  row_drift: '\u{1F3A2}',      // roller coaster
  audio_trigger: '\u{1F509}',  // speaker
});

/**
 * @param {Object} o
 * @param {Object} o.engine        the shell's allowlisted engine handle
 * @param {number} o.tier          grade tier 1..4
 * @param {Function=} o.log
 * @param {boolean=} o.reduced     reduced motion / motionLevel 0
 * @param {Object=} o.targets      {keyRows(), keycaps(), onGlitchSwap(els, variant), exempt()}
 * @param {string[]=} o.pollution  other bank words to whisper (candidate pollution)
 * @param {Function=} o.roll       tagged roll (seeded); defaults to a fixed 0.5
 */
export function createLadder({ engine, tier, log, reduced, targets, pollution, roll } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const eng = engine || null;
  const T = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  const tgt = targets || {};
  const words = Array.isArray(pollution) ? pollution.slice() : [];
  const rollFn = typeof roll === 'function' ? roll : () => 0.5;
  const soft = !!reduced;

  const timers = new Set();
  const live = new Set();          // effect kinds currently contributing
  const started = new Set();       // one-shot rung entries already applied
  let rung = tierStartFor(T);
  let misses = 0;
  let stopped = false;
  let paused = false;
  let washFlip = 0;
  let polluteAt = 0;

  const after = (ms, fn) => {
    const id = setTimeout(() => { timers.delete(id); if (!stopped && !paused) { try { fn(); } catch (e) { say('ladder timer: ' + ((e && e.message) || e)); } } }, Math.max(16, ms));
    timers.add(id);
    return id;
  };
  const every = (ms, fn) => {
    const tick = () => { fn(); after(ms, tick); };
    return after(ms, tick);
  };
  const clearTimers = () => { for (const id of Array.from(timers)) clearTimeout(id); timers.clear(); };

  /** Every engine call in this file goes through here: a thrown effect must
   *  never take the class down with it (the shell guards too - belt and braces). */
  function fire(kind, opts) {
    if (!eng || stopped) return null;
    try { return eng.fire(kind, opts || {}); }
    catch (e) { say('fire(' + kind + ') failed: ' + ((e && e.message) || e)); return null; }
  }
  function sustain(kind, opts) {
    if (!eng || stopped) return null;
    try { return eng.sustain(kind, opts || {}); }
    catch (e) { say('sustain(' + kind + ') failed: ' + ((e && e.message) || e)); return null; }
  }
  function stop(kind) {
    if (!eng) return;
    try { eng.stop(kind); } catch (e) { /* noop */ }
  }
  function heat(h) {
    if (!eng) return;
    try { eng.setHeat(h); } catch (e) { say('setHeat failed: ' + ((e && e.message) || e)); }
  }

  /* ---------------------- the rungs -------------------------------------- */
  function rung0() {
    // The floor: soft ambient motes behind the board. Density defaults to the
    // clamped bgIntensity channel, so a player who capped it sees nothing.
    sustain('ambient_field', { kind: 'motes' });
    live.add('ambient_field');
  }

  function rung1() {
    // sub_flash as a cadence stream (engine's additive alias). The word pool is
    // the SHARED subliminal vocabulary and may legally be empty - the engine then
    // shows image cards or silently skips. Candidate pollution rides on top.
    sustain('sub_flash', { variant: 'whisper' });
    live.add('sub_flash');
  }

  function rung2() {
    sustain('bubble_field', {
      clickSafe: true,             // INPUT-TRUST LAW: never clickable over the keyboard
      variant: 'drift',
      max: soft ? 4 : 9,
    });
    live.add('bubble_field');
  }

  function rung3() { washPulse(); }

  function rung4() {
    // Keycap glyph glitching. The engine only dresses the transition; index.js
    // swaps the GLYPH at the midpoint and never the hitbox letter.
    every(soft ? 9000 : 5200 + Math.floor(rollFn('glitchgap') * 2600), keyGlitch);
    keyGlitch();
  }

  function rung5() {
    const rows = typeof tgt.keyRows === 'function' ? (tgt.keyRows() || []) : [];
    if (rows.length) {
      sustain('row_drift', { targets: rows, axis: 'x', variant: 'sway', amplitudeMult: soft ? 0.4 : 1 });
      live.add('row_drift');
    }
    every(7000 + Math.floor(rollFn('whispergap') * 4000), whisper);
  }

  const RUNGS = [rung0, rung1, rung2, rung3, rung4, rung5];

  /* ---------------------- individual beats ------------------------------- */
  function washPulse() {
    // ONE reused element per kind: re-triggering refreshes the fade deadline
    // instead of piling DOM (GROUND-RULES §6).
    const variant = (washFlip++ % 2) === 0 ? 'pink' : 'spiral';
    sustain('wash', { variant, holdMs: soft ? 1400 : 2400 });
    live.add('wash');
  }

  function keyGlitch() {
    const caps = typeof tgt.keycaps === 'function' ? (tgt.keycaps() || []) : [];
    if (!caps.length) return;
    const pick = [];
    const n = Math.min(caps.length, soft ? 2 : 3);
    for (let i = 0; i < n; i++) {
      const el = caps[Math.floor(rollFn('glitchkey') * caps.length)];
      if (el && pick.indexOf(el) < 0) pick.push(el);
    }
    if (!pick.length) return;
    const exempt = typeof tgt.exempt === 'function' ? tgt.exempt() : null;
    const targetsNow = exempt ? pick.filter((el) => el !== exempt) : pick;
    if (!targetsNow.length) return;
    fire('glitch_swap', {
      targets: targetsNow,
      exempt: exempt || undefined,
      seconds: soft ? 0.3 : 0.7,
      variant: soft ? 'crossfade' : undefined,
      onSwap: (variant) => {
        if (typeof tgt.onGlitchSwap === 'function') {
          try { tgt.onGlitchSwap(targetsNow, variant); } catch (e) { say('glyph swap: ' + ((e && e.message) || e)); }
        }
      },
    });
    live.add('glitch_swap');
  }

  function whisper() {
    // A decoy whisper of ANOTHER bank word, through the ducking hierarchy. The
    // text ride is sub_flash's; this is the audio half of the same lie.
    fire('audio_trigger', { name: 'whisper', level: 0.45, bus: 'voice', duck: 'voice' });
    live.add('audio_trigger');
    pollute();
  }

  /** One candidate-pollution flash: a real bank word that is NOT today's answer. */
  function pollute() {
    if (!words.length) return null;
    const w = words[polluteAt % words.length];
    polluteAt += 1;
    live.add('sub_flash');
    return fire('sub_flash', { text: w.toUpperCase(), variant: 'scatter' });
  }

  /* ---------------------- the public surface ----------------------------- */
  function applyTo(next) {
    const to = Math.min(rungCapFor(T), Math.max(0, next));
    heat(heatFor(T, to));
    for (let r = 0; r <= to; r++) {
      if (started.has(r)) continue;
      started.add(r);
      try { RUNGS[r](); } catch (e) { say('rung ' + r + ' failed: ' + ((e && e.message) || e)); }
    }
    rung = to;
    return rung;
  }

  const api = {
    get rung() { return rung; },
    get misses() { return misses; },
    get tierStart() { return tierStartFor(T); },
    get cap() { return rungCapFor(T); },
    get heat() { return heatFor(T, rung); },

    /** Apply the opening rung for this tier (Year 4 starts under pressure). */
    open() { return applyTo(tierStartFor(T)); },

    /** A wrong row: climb exactly one rung, deterministically. */
    miss() {
      misses += 1;
      const before = rung;
      const now = applyTo(tierStartFor(T) + misses);
      // Already at the ceiling? The pressure still lands: refresh the wash and
      // (from rung 3) fire one extra pollution flash, so row 6 is not quieter
      // than row 4 just because the ladder ran out of rungs.
      if (now === before && now >= 3) { washPulse(); pollute(); }
      return now;
    },

    /** The gentle, telegraphed taste of the twist (grade_tier 2+, once a class). */
    tasteOfTwist() { return pollute(); },

    /** Effect families that were live when the class ended -> share badges. */
    families() { return Array.from(live); },
    stormBadges() {
      const out = [];
      for (const k of Array.from(live)) {
        const b = STORM_BADGES[k];
        if (b && out.indexOf(b) < 0) out.push(b);
      }
      return out;
    },

    /** Fail-state dressing: crt/scanline under the forced reveal. */
    detentionDressing() {
      sustain('crt', { variant: 'scanline', level: soft ? 0.25 : 0.6 });
      live.add('crt');
    },

    /** Ceremony bloom: wash + particles behind the absorbed word. */
    absorbDressing(kind) {
      sustain('wash', { variant: 'pink', holdMs: 3200 });
      sustain('ambient_field', { kind: kind || 'confetti' });
      live.add('wash');
    },

    pause(on) {
      paused = !!on;
      if (paused) clearTimers();
      else if (!stopped && rung >= 4) applyTo(rung);   // re-arm the interval rungs
    },

    /** Fade everything (never a snap) and forget the timers. */
    stopAll() {
      stopped = true;
      clearTimers();
      for (const k of ['row_drift', 'bubble_field', 'wash', 'ambient_field', 'crt', 'sub_flash', 'gif_rain']) stop(k);
      live.clear();
    },
  };
  return api;
}

export default createLadder;
