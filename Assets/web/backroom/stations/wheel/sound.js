/* sound.js - the wheel's cues, a thin adapter over the room's one kit (shared/sound/kit.js). The API station.js
 * knows is unchanged: arm, tick, thud, win, token, suspend, dispose, trace, plus slowing(). The race's mp3 chimes
 * are retired: every cue is synthesised in the kit. The kit's context is the room's; this adapter only plays on it
 * and takes its own voices back on a suspend and a dispose (Law VI).
 *
 * The floor's rules, as the wheel spends them: a peg is THE CLACK, the long last turn is the riser, the pointer
 * settling is THE THUD (or THE SETTLE on a snooze, soft and never a fail), the party is a win by tier that only
 * rises (chime -> small, two -> mid, thud -> big, reveal -> hero), and EMI's snooze is a sigh.
 *
 * THE CHIME LADDER (CONTRACT 10.22.D). The landing cue used to be the whole party: one flat note, whatever
 * the rung. `climb()` is the slot's move, on the shared plan (shared/win/ladder.js): the notes after the
 * landing one roll with THE BANK's count-up, a semitone a step, capped at 7, an octave down while melted,
 * never a step closer than the 6 Hz floor. `hush()` takes back whatever has not sounded yet, so Law VI's
 * exits silence the rest of the climb instead of playing a faster version of it. */

import { kit } from '../../shared/sound/kit.js';

const WIN_TIER = { chime: 'small', two: 'mid', thud: 'big', reveal: 'hero' };
const LEVEL = { thud: 0.5, settle: 0.05, chime: 0.34, tick: 0.1, token: 0.12, rise: 0.2 };

export function createSound(k = kit) {
  let suspended = false, disposed = false, tokens = 0;
  const trace = [];
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());
  const note = (name, semis, level, inMs = 0) => { trace.push({ name, semis, level, at: Math.round(now()), in: Math.round(inMs) }); if (trace.length > 80) trace.shift(); };
  const live = () => !disposed && !suspended;

  return {
    trace,
    start() { if (live()) k.play('wheel-start'); },
    /** Wake the kit inside a gesture (a press or a drag). */
    arm() { if (live()) k.arm(); },
    /** THE CLACK on a peg crossing, `semis` from feel.tick. */
    tick(semis) { note('tick', semis, LEVEL.tick); if (live()) k.play('wheel-peg', { semis }); },
    /** The long last turn: the riser while the wheel slows into its slice, resolving as it lands. */
    slowing(ms = 2000) { note('rise', 0, LEVEL.rise); if (live()) { k.stop('riser'); k.play('riser', { ms }); } },
    /** THE THUD when the pointer settles; `muted` (Snooze) is THE SETTLE (Brake 6: never silence, never a fail). */
    thud(muted = false) {
      note(muted ? 'thud-muted' : 'thud', muted ? -5 : 0, muted ? LEVEL.settle : LEVEL.thud);
      if (!live()) return;
      if (muted) k.play('settle'); else k.play('thud');
    },
    /** The landing's party cue from feel.recipe: chime | two | thud | reveal | snooze. */
    win(sound) {
      note(sound, 0, LEVEL.chime);
      if (!live()) return;
      if (sound === 'snooze') k.play('sigh', { at: 0.18 });   // EMI's yawn
      else if (WIN_TIER[sound]) { k.play('win', { tier: WIN_TIER[sound] }); tokens = 0; }
    },
    /** THE CHIME LADDER climbing across THE BANK's rollup (10.22.D). `plan` is ladder.js's ladderPlan: step 0
     *  is the landing note win() has already played, so only the steps after it sound, up from `base`
     *  (plan.octave: 0, or -12 in a focus state). Scheduled ahead; hush() takes back what has not sounded. */
    climb(plan, base = 0) {
      this.hush();
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
    suspend(on) { suspended = !!on; if (suspended) { k.stop('riser'); k.stop('ladder'); k.stop('wheel-start'); k.stop('wheel-peg'); } },
    dispose() { disposed = true; k.stop('riser'); k.stop('ladder'); k.stop('wheel-start'); k.stop('wheel-peg'); },
  };
}
