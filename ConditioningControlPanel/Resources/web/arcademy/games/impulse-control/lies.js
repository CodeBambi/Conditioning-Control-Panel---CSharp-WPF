/* ============================================================================
 * games/impulse-control/lies.js - THE ENGINE LIES. The class's soul.
 *
 * Five typed lie set-pieces (dossier "The twist"), each a descriptor, each logged
 * so the debrief can attribute an error to the exact lie that induced it:
 *
 *   1 false_cue        the GO sting plays over a NO-GO           (audio_trigger)
 *   2 commitment_trap  a GO glitches into its twin mid-press     (glitch_swap)
 *   3 priming_flash    the GO face whispers before a NO-GO       (sub_flash)
 *   4 peripheral_decoy fake stimuli drift outside the aperture   (bubble_field
 *                      + row_drift at tier 4, which slides the aperture itself)
 *   5 inverse_audio    the ERROR buzzer on a CLEAN GO            (audio_trigger)
 *                      DECISIONS #7: tier 4 ONLY, its own descriptor, and the
 *                      debrief ALWAYS attributes it ("that buzzer lied").
 *
 * TIER GATING (dossier distraction.byGrade): tier 1 lies not at all - it is an
 * honest test. Tier 2 gets ONE telegraphed, debriefed false cue (SYNTHESIS #2's
 * taste-of-the-twist) plus priming and decoys. Tier 3 arms false cues and
 * commitment traps properly. Tier 4 adds the aperture slide and the inverse lie.
 *
 * WHY THE ROLLS LIVE HERE AND NOT IN THE DIRECTOR. The shell hands a class an
 * allowlisted engine handle with setHeat/fire/sustain/stop/setpiece/beat/ceremony
 * only - `setPhase()` and `armTail()` are NOT on it, so a game cannot drive the
 * director's phase or arm its forceTail, and with a null engine `setpiece()`
 * returns undefined. So this module keeps the descriptor semantics itself
 * (eligiblePhases / perBeatChance / oncePerRunGate / minGap / maxPerPhase /
 * forceTail + one-lie-per-beat) on its own seeded rng, and still REGISTERS every
 * descriptor with engine.setpiece() so the engine owns the canonical record and
 * executes the effect through the descriptor's run(). Identical behaviour with or
 * without the engine, one roll site, deterministic per seed. (Reported as a
 * shared-layer gap: exposing setPhase/armTail would let this collapse.)
 *
 * ONE LIE PER BEAT. A beat is one lie-eligible stimulus. At most one non-cosmetic
 * lie fires on it (the decoy surge is cosmetic and may ride along), anti-clump
 * spaced by minGap, capped per phase by maxPerPhase.
 *
 * WHEN a lie lands matters as much as whether. The beat is RESOLVED at the start of
 * the foreperiod (so a lie can precede its stimulus) but each lie is SCHEDULED by
 * its own `offset(fore)`: the false sting lands ~200ms before onset, where it can
 * actually pull a finger; the prime lands ~450ms before, where it can pre-load a
 * motor plan; the commitment trap lands inside the presentation. Fire them all at
 * beat time instead and the debrief attributes nothing, because nothing was still
 * live when the finger moved.
 *
 * `influenceMs` is how long a lie stays LIVE for attribution - a sting's motor pull
 * is short, a subliminal prime works on the stimulus after it, a decoy field is
 * live as long as it is up. `activeAt()` reads it: the dossier's "which effect was
 * live in the 400ms before the error", one step more honest.
 * ==========================================================================*/

/** How long after a lie event an error still counts as INDUCED (dossier: 400ms). */
export const ATTRIBUTION_MS = 400;

/** The commitment trap fires this long after stimulus onset (inside the commit point). */
export const TRAP_DELAY_MS = Object.freeze([150, 300]);

/** Grace for an aborted press on a trap ("almost had you"), dossier near-miss #2. */
export const ABORT_GRACE_MS = 80;

/**
 * The descriptor table. `chance` is per lie-eligible beat, by tier.
 * `needs` is the stimulus class the lie requires - a lie that does not fit the
 * stimulus never rolls at all, so a fitted roll is never wasted (the director
 * would have burned the beat's gate on an impossible lie).
 */
export const LIES = Object.freeze([
  // minTier 2, chance 0 at tier 2: the descriptor has to EXIST at tier 2 for the
  // taste-of-the-twist to force it, but it never rolls until tier 3.
  Object.freeze({
    key: 'false_cue', minTier: 2, needs: 'nogo', minGap: 2, maxPerPhase: 3,
    chance: { 3: 0.22, 4: 0.30 }, forceTail: true,
    offset: (fore) => Math.max(80, (fore || 800) - 200), influenceMs: 500,
  }),
  Object.freeze({
    key: 'commitment_trap', minTier: 3, needs: 'go', minGap: 3, maxPerPhase: 2,
    chance: { 3: 0.18, 4: 0.26 }, forceTail: true,
    offset: (fore) => (fore || 800) + 150, influenceMs: 600,
  }),
  Object.freeze({
    key: 'priming_flash', minTier: 2, needs: 'nogo', minGap: 2, maxPerPhase: 3,
    chance: { 2: 0.12, 3: 0.20, 4: 0.26 }, forceTail: true,
    offset: (fore) => Math.max(60, (fore || 800) - 450), influenceMs: 1000,
  }),
  Object.freeze({
    key: 'peripheral_decoy', minTier: 2, needs: null, minGap: 4, maxPerPhase: 2,
    chance: { 2: 0.15, 3: 0.20, 4: 0.25 }, cosmetic: true, forceTail: false,
    offset: () => 0, influenceMs: 1600,
  }),
  // DECISIONS #7. Never rolls on a beat: it is forced from a CLEAN GO hit (a
  // buzzer on a miss would not be a lie), once per run, and discharged in the
  // composure hold if it never fired.
  Object.freeze({
    key: 'inverse_audio', minTier: 4, needs: 'go', minGap: 0, maxPerPhase: 1,
    chance: {}, oncePerRunGate: true, forceTail: true,
    offset: () => 0, influenceMs: 500,
  }),
]);

import { IC_LEX } from './lex.js';

const clamp01 = (n) => { const v = Number(n); return !Number.isFinite(v) ? 0 : v < 0 ? 0 : v > 1 ? 1 : v; };

/**
 * @param {Object} o
 * @param {Object} o.engine       the allowlisted engine handle (may be a null object)
 * @param {number} o.tier         grade_tier 1..4
 * @param {Function} o.rng        seeded 0..1 stream (determinism)
 * @param {Function} o.now        () => ms clock (shared with the class runner)
 * @param {Function} o.t          ctx.lexicon
 * @param {Function=} o.log
 * @param {boolean=} o.allowInverse   the per-game consent knob (ic_inverse_audio)
 * @param {boolean=} o.reduced        reduced motion / motionLevel 0
 * @param {boolean=} o.coarse         coarse pointer (no aperture slide)
 * @param {Object} o.hooks        { stimEl(), apertureEl(), primeText(), swapToTwin(rec),
 *                                  onFired(event), decoyBump(on) }
 */
export function createLies({ engine, tier, rng, now, t, log, allowInverse, reduced, coarse, hooks } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  /* ONE resolver: IC_LEX is the canonical English for every ic_ row, so a caller's
     inline literal never shadows it (that shadowing ate a debrief line once). */
  const lex = (k, f) => {
    const dflt = IC_LEX[k] != null ? IC_LEX[k] : f;
    try { return typeof t === 'function' ? t(k, dflt) : (dflt || k); }
    catch (e) { return dflt || k; }
  };
  const rand = typeof rng === 'function' ? rng : Math.random;
  const clock = typeof now === 'function' ? now : () => Date.now();
  const h = hooks || {};
  const gradeTier = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  const eng = engine || null;

  /* Which lies exist at this tier at all. The inverse lie also needs its knob. */
  const active = LIES.filter((d) => {
    if (gradeTier < d.minTier) return false;
    if (d.key === 'inverse_audio' && allowInverse === false) return false;
    return true;
  });

  const state = new Map();     // key -> {firedRun, firedPhase, lastBeat}
  const st = (key) => {
    let s = state.get(key);
    if (!s) { s = { firedRun: 0, firedPhase: 0, lastBeat: -Infinity }; state.set(key, s); }
    return s;
  };

  const events = [];           // the interference log: {kind, atMs, recIndex, telegraphed, forced}
  const handles = new Map();   // key -> engine setpiece handle (when the engine is live)
  let phase = 'baseline';
  let beatIndex = 0;
  let taste = null;            // tier-2 taste-of-the-twist bookkeeping

  /* ------------------------------------------------------------------ effects */
  /** Each lie's actual engine call. Returns a short note for the log. */
  const RUN = {
    false_cue(rec, opts) {
      // The GO sting, over a NO-GO. Same waveform family as the real cue by
      // contract (the mod ships both), which is what makes the lie credible.
      const gentle = !!(opts && opts.telegraphed);
      fire('audio_trigger', {
        name: 'sting',
        level: gentle ? 0.38 : 0.55,
        bus: 'fx',
        duck: 'voice',
      });
      return 'go-sting over a NO-GO';
    },
    commitment_trap(rec) {
      const el = typeof h.stimEl === 'function' ? h.stimEl() : null;
      if (!el) return 'no stimulus node';
      // The engine NEVER moves a hitbox or a glyph: WE swap the content at the
      // transition midpoint, through onSwap. Reduced motion -> strobe-free
      // crossfade (still a lie, no flash).
      fire('glitch_swap', {
        targets: [el],
        variant: reduced ? 'crossfade' : undefined,
        seconds: 0.35,
        exempt: typeof h.apertureEl === 'function' ? h.apertureEl() : undefined,
        onSwap: () => { try { if (typeof h.swapToTwin === 'function') h.swapToTwin(rec); } catch (e) { say('trap swap: ' + (e && e.message)); } },
      });
      return 'GO swapped to its twin mid-presentation';
    },
    priming_flash(rec) {
      const text = typeof h.primeText === 'function' ? h.primeText() : null;
      // Never prime with content the player disabled: no word/glyph face to
      // whisper (media-only block, empty vocabulary) -> the lie simply skips.
      if (!text) return null;
      fire('sub_flash', {
        text,
        variant: 'whisper',
        anchor: typeof h.apertureEl === 'function' ? h.apertureEl() : undefined,
      });
      return 'GO face primed before a NO-GO';
    },
    peripheral_decoy() {
      if (typeof h.decoyBump === 'function') { try { h.decoyBump(true); } catch (e) { /* noop */ } }
      return 'decoy surge in the periphery';
    },
    inverse_audio() {
      // The meanest item in the batch: the ERROR buzzer on a CLEAN GO.
      fire('audio_trigger', { name: 'stamp_bad', level: 0.7, bus: 'fx', duck: 'voice' });
      return 'error buzzer on a clean GO';
    },
  };

  function fire(kind, opts) {
    if (!eng || typeof eng.fire !== 'function') return null;
    try { return eng.fire(kind, opts); } catch (e) { say('lie ' + kind + ' threw: ' + (e && e.message)); return null; }
  }

  /* ------------------------------------------------- descriptor registration */
  for (const d of active) {
    const descriptor = {
      key: 'ic_' + d.key,
      // The rolls happen here (see header), so the director never rolls: 0.
      perBeatChance: 0,
      oncePerRunGate: !!d.oncePerRunGate,
      forceTail: !!d.forceTail,
      maxPerPhase: d.maxPerPhase,
      minGap: d.minGap,
      cosmetic: !!d.cosmetic,
      eligiblePhases: () => phase === 'assess' || phase === 'hold',
      run: (c) => (RUN[d.key] ? RUN[d.key]((c && c.rec) || null, c || {}) : null),
    };
    let handle = null;
    if (eng && typeof eng.setpiece === 'function') {
      try { handle = eng.setpiece(descriptor) || null; } catch (e) { handle = null; }
    }
    handles.set(d.key, { descriptor, handle });
  }
  if (!active.length) say('tier ' + gradeTier + ': no lies (honest test)');

  /* ------------------------------------------------------------------ firing */
  function invoke(d, rec, opts) {
    const bundle = handles.get(d.key);
    const o = Object.assign({ rec }, opts || {});
    let note = null;
    if (bundle && bundle.handle && typeof bundle.handle.force === 'function') {
      // The engine's director owns the record and calls our run().
      let res = null;
      try { res = bundle.handle.force(o); } catch (e) { say('setpiece force threw: ' + (e && e.message)); }
      note = (res && res.result) || null;
      if (note == null && res === null) note = bundle.descriptor.run(o);
    } else {
      note = bundle ? bundle.descriptor.run(o) : null;
    }
    if (note == null) return null;      // the lie declined (nothing safe to do)

    const ev = {
      kind: d.key,
      atMs: clock(),
      influenceMs: d.influenceMs || ATTRIBUTION_MS,
      recIndex: rec ? rec.i : -1,
      phase,
      telegraphed: !!(opts && opts.telegraphed),
      forced: !!(opts && opts.forced),
      note,
      label: lex('ic_lie_' + d.key, d.key.replace(/_/g, ' ')),
    };
    events.push(ev);
    if (typeof h.onFired === 'function') { try { h.onFired(ev); } catch (e) { /* noop */ } }
    say('lie fired: ' + d.key + (ev.telegraphed ? ' (telegraphed)' : '') + ' on #' + ev.recIndex);
    return ev;
  }

  /**
   * Commit a lie to a stimulus. The gates and the record are marked NOW (so the
   * trial counts as a lie trial and the anti-clump rules see it), and the effect
   * is SCHEDULED for the moment it can actually work on the player. A lie that
   * declines when it runs (nothing safe to whisper, no stimulus node) hands its
   * gate slot back.
   */
  function execute(d, rec, opts) {
    const s = st(d.key);
    s.firedRun += 1;
    s.firedPhase += 1;
    s.lastBeat = beatIndex;
    if (rec) rec.lie = d.key;

    const delay = typeof d.offset === 'function'
      ? Math.max(0, Math.round(d.offset(rec && rec.foreperiodMs))) : 0;

    if (!delay || typeof h.schedule !== 'function') {
      const ev = invoke(d, rec, opts);
      if (!ev) {
        s.firedRun -= 1; s.firedPhase -= 1;
        if (rec) rec.lie = null;
      }
      return ev;
    }
    h.schedule(delay, () => {
      const ev = invoke(d, rec, opts);
      if (!ev) {
        s.firedRun = Math.max(0, s.firedRun - 1);
        s.firedPhase = Math.max(0, s.firedPhase - 1);
        if (rec) rec.lie = null;
      }
    });
    return { kind: d.key, scheduled: true, delay, telegraphed: !!(opts && opts.telegraphed) };
  }

  function blocked(d) {
    const s = st(d.key);
    if (d.oncePerRunGate && s.firedRun > 0) return 'once-per-run';
    if (s.firedPhase >= d.maxPerPhase) return 'max-per-phase';
    if (beatIndex - s.lastBeat < d.minGap) return 'min-gap';
    return null;
  }

  const api = {
    /** The interference log (the debrief's raw material). */
    get events() { return events.slice(); },
    get tier() { return gradeTier; },
    /** Lie kinds this tier can produce at all (tests + diagnostics). */
    kinds() { return active.map((d) => d.key); },
    counts() {
      const out = {};
      for (const [k, s] of state) out[k] = s.firedRun;
      return out;
    },
    countOf(kind) { return (state.get(kind) || { firedRun: 0 }).firedRun; },
    get total() { return events.length; },

    /** Phase changes reset the per-phase clump counters (director semantics). */
    setPhase(next) {
      if (next === phase) return phase;
      phase = String(next);
      for (const s of state.values()) s.firedPhase = 0;
      return phase;
    },
    get phase() { return phase; },

    /**
     * SYNTHESIS #2 taste-of-the-twist: from tier 2, EXACTLY ONE telegraphed,
     * gentle, always-debriefed false cue per class. Tier 2 has no rolled false
     * cues at all (LIES.false_cue starts at tier 3), so this is the whole of the
     * player's first meeting with the game's identity - which is the point.
     * `plan(records)` picks the target deterministically; `maybeTelegraph`/`beat`
     * do the rest.
     */
    planTaste(records) {
      if (gradeTier !== 2) return null;
      const candidates = (records || []).filter((r) => r.phase === 'assess' && r.cls === 'nogo' && !r.isFirst);
      if (!candidates.length) return null;
      // Middle of the class, seeded: never the first trial (nothing learned yet)
      // and never the last (no room left to debrief it in-round).
      const pool = candidates.slice(Math.floor(candidates.length * 0.25), Math.max(1, Math.floor(candidates.length * 0.8)) || 1);
      const list = pool.length ? pool : candidates;
      const target = list[Math.floor(clamp01(rand()) * list.length)] || candidates[0];
      taste = { recIndex: target.i, telegraphedAt: 0, fired: false };
      say('taste-of-the-twist armed on stimulus #' + target.i);
      return taste;
    },
    /** True when this record is the tier-2 taste target (the chrome telegraphs it). */
    isTasteTarget(rec) { return !!(taste && rec && rec.i === taste.recIndex && !taste.fired); },

    /**
     * One beat = one lie-eligible stimulus, resolved at the START of its
     * foreperiod (so a lie can precede the stimulus, which is the whole trick).
     * @returns {Array} the events that fired (0..n, at most one non-cosmetic)
     */
    beat(rec) {
      if (!rec) return [];
      beatIndex += 1;
      const fired = [];

      /* the tier-2 taste is forced, gently, and always debriefed */
      if (taste && !taste.fired && rec.i === taste.recIndex) {
        const d = LIES.find((x) => x.key === 'false_cue');
        const ev = execute(d, rec, { telegraphed: true, forced: true, gentle: true });
        if (ev) { taste.fired = true; fired.push(ev); return fired; }
      }

      if (!rec.lieEligible) return fired;      // the plain-share ramp: lies are events
      if (phase !== 'assess' && phase !== 'hold') return fired;

      /* Candidates in a SEEDED SHUFFLE, not table order. Several lies want the
         same stimulus class (a false cue and a prime both need a NO-GO) and only
         one can own the beat, so a fixed order would let the first entry starve
         the rest for a whole class - the shuffled-bag lesson, applied to the
         taxonomy itself. */
      const candidates = [];
      for (const d of active) {
        if (d.key === 'inverse_audio') continue;                 // forced only
        if (clamp01(d.chance[gradeTier] || 0) <= 0) continue;
        if (d.needs && rec.cls !== d.needs) continue;
        if (blocked(d)) continue;
        candidates.push(d);
      }
      for (let i = candidates.length - 1; i > 0; i--) {
        const j = Math.min(i, Math.floor(rand() * (i + 1)));
        const t2 = candidates[i]; candidates[i] = candidates[j]; candidates[j] = t2;
      }

      let gateUsed = false;
      for (const d of candidates) {
        if (!d.cosmetic && gateUsed) continue;
        if (rand() >= clamp01(d.chance[gradeTier] || 0)) continue;
        const ev = execute(d, rec, null);
        if (!ev) continue;
        fired.push(ev);
        if (!d.cosmetic) gateUsed = true;
      }
      return fired;
    },

    /**
     * The inverse audio lie (DECISIONS #7). Called from a CLEAN, in-window GO hit
     * at tier 4 only. `force` skips the roll - that is the composure-hold tail
     * discharge, so a tier-4 class always gets its one buzzer and its debrief
     * line rather than sometimes hiding the set-piece behind a dice roll.
     */
    maybeInverseAudio(rec, opts) {
      const d = LIES.find((x) => x.key === 'inverse_audio');
      if (!d || active.indexOf(d) < 0) return null;
      if (blocked(d)) return null;
      const forced = !!(opts && opts.force);
      if (!forced && rand() >= 0.35) return null;
      return execute(d, rec, { forced });
    },
    /** Did the tier-4 set-piece still not discharge? (the hold's tail check) */
    inverseArmed() {
      const d = LIES.find((x) => x.key === 'inverse_audio');
      return !!(d && active.indexOf(d) >= 0 && st('inverse_audio').firedRun === 0);
    },

    /**
     * ATTRIBUTION. Which lie was live in the ATTRIBUTION_MS before `atMs`?
     * This is the product: an error with an answer here is INDUCED (the machine
     * got you), an error without one is CLEAN (yours).
     */
    activeAt(atMs) {
      const at = Number(atMs) || clock();
      let best = null;
      for (const ev of events) {
        const dt = at - ev.atMs;
        const window = ev.influenceMs || ATTRIBUTION_MS;
        if (dt >= -60 && dt <= window) {
          if (!best || ev.atMs > best.atMs) best = ev;
        }
      }
      return best;
    },

    /** A trap that swapped this stimulus within the abort grace (near-miss #2). */
    trapJustSwapped(rec, atMs) {
      const at = Number(atMs) || clock();
      for (let n = events.length - 1; n >= 0; n--) {
        const ev = events[n];
        if (ev.kind !== 'commitment_trap') continue;
        if (rec && ev.recIndex !== rec.i) continue;
        return (at - ev.atMs) <= (TRAP_DELAY_MS[1] + ATTRIBUTION_MS);
      }
      return false;
    },

    /** Aperture slide is tier 4 only, and never on a coarse pointer (the dossier's
     *  portability rule: a parked finger cannot decay off-target). */
    apertureSlideAllowed() { return gradeTier >= 4 && !coarse && !reduced; },

    destroy() {
      for (const { handle } of handles.values()) {
        try { if (handle && typeof handle.unregister === 'function') handle.unregister(); } catch (e) { /* noop */ }
      }
      handles.clear();
    },
  };

  return api;
}

export default createLies;
