/* sound.js - the slot's cues, a thin adapter over the room's one kit (shared/sound/kit.js). The API station.js
 * and the checks know is unchanged: arm, thud, win, climb, hush, token, rise, almost, suspend, dispose, trace,
 * plus melt(). The race's mp3 chimes are retired: every cue is synthesised in the kit, so the slot ships no
 * licensed samples. The kit's context is the room's (armed on its first gesture, closed when the room settles);
 * this adapter only plays on it and takes its own voices back on a skip, a suspend and a dispose (Law VI).
 *
 * The floor's rules, as the slot spends them: a pay lands a win by tier that only rises (chime -> small,
 * two -> mid, thud -> big, reveal -> hero), THE CHIME LADDER rolls with THE BANK's count-up, a no-pay spin's last
 * stop is THE SETTLE (soft, felt, never a fail), A1's tease is the riser and A2's almost resolves quietly. */

import { kit } from '../../shared/sound/kit.js';

const WIN_TIER = { chime: 'small', two: 'mid', thud: 'big', reveal: 'hero' };
const LEVEL = { thud: 0.5, settle: 0.05, chime: 0.34, token: 0.12, rise: 0.2 };

export function createSound(k = kit) {
  let suspended = false, disposed = false, tokens = 0;
  const trace = [];   // the last cues with their page time, for dev.html and CDP checks
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());
  function note(name, semis, level, inMs = 0) { trace.push({ name, semis, level, at: Math.round(now()), in: Math.round(inMs) }); if (trace.length > 60) trace.shift(); }
  const live = () => !disposed && !suspended;
  /** Every voice this station may have scheduled ahead. */
  const quiet = () => { k.stop('riser'); k.stop('ladder'); k.stop('breath'); };

  const api = {
    /** Wake the kit inside a gesture (a press or a pull). */
    arm() { if (live()) k.arm(); },
    /** THE THUD for reel `i` (0..2), rising left to right; the last stop of a no-pay spin is THE SETTLE. */
    thud(i, muted = false) {
      const semis = muted ? -5 : (i - 1) * 2;
      note(muted ? 'thud-muted' : 'thud', semis, muted ? LEVEL.settle : LEVEL.thud);
      if (!live()) return;
      if (muted) k.play('settle'); else k.play('thud', { semis });
    },
    /** THE WIN by tier, `sound` from feel.recipe, `semis` from feel.ladderSemis (the streak's root). */
    win(sound, semis) {
      const tier = WIN_TIER[sound];
      note(sound, semis, tier ? LEVEL.chime : 0);
      if (!live() || !tier) return;
      k.play('win', { tier, semis });
      tokens = 0;
    },
    /** THE CHIME LADDER climbing across THE BANK's rollup (playbook A3). `plan` is feel.ladderPlan: step 0 is
     *  the landing note win() has already played, so only the steps after it sound, pentatonic up from `base`.
     *  They are scheduled ahead, and hush() takes back whatever has not sounded yet. */
    climb(plan, base = 0) {
      api.hush();
      const steps = (Array.isArray(plan) ? plan : []).slice(1);
      if (!steps.length) return;
      for (const s of steps) note('climb', base + s.semis, LEVEL.chime * 0.8, s.at);
      if (!live()) return;
      k.play('ladder', { plan: steps, semis: base });
    },
    /** Law VI: a skip silences the rest of the climb; it never plays a faster version of it. */
    hush() { k.stop('ladder'); },
    /** A token landing (THE BANK), each a rung higher; the last one gets the mini-thud. */
    token(last) {
      note(last ? 'bank-thud' : 'token', last ? 5 : 12, last ? LEVEL.thud * 0.6 : LEVEL.token);
      if (!live()) return;
      if (last) { k.play('token', { last: true }); tokens = 0; } else k.play('token', { i: tokens++ });
    },
    /** A1 THE ANTICIPATION REEL: the riser across reel 3's hold. Quiet under Calm, and it still plays under
     *  reduced motion: the hold is pace, not animation. */
    rise(ms, calm = false) {
      const level = calm ? 0.5 : 1;
      note('rise', 0, LEVEL.rise * level);
      if (!live()) return;
      k.stop('riser'); k.play('riser', { ms, level });
    },
    /** A2 THE ALMOST: a quiet resolve on the same frame as the settle (Law X, one beat). Never a fall. */
    almost(calm = false) {
      const level = calm ? 0.4 : 0.7;
      note('almost', 0, 0.06 * level);
      if (live()) k.play('almost', { level });
    },
    /** A melt landing: a deep slow breath under it (6 s). */
    melt() { note('melt', 0, 0.12); if (live()) k.play('breath'); },
    suspend(on) {
      suspended = !!on;
      if (suspended) quiet();   // the room suspends the kit itself; this only takes the slot's own voices back
    },
    dispose() { disposed = true; quiet(); },
    trace,
  };
  return api;
}
