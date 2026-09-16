/* sound.js - the slot's cues, a thin adapter over the room's one kit (shared/sound/kit.js). The API station.js
 * and the checks know is unchanged: arm, thud, win, climb, hush, token, rise, almost, suspend, dispose, trace,
 * plus melt(). The race's mp3 chimes are retired: every cue is synthesised in the kit, so the slot ships no
 * licensed samples. The kit's context is the room's (armed on its first gesture, closed when the room settles);
 * this adapter only plays on it and takes its own voices back on a skip, a suspend and a dispose (Law VI).
 *
 * The floor's rules, as the slot spends them: a pay lands a win by tier that only rises (chime -> small,
 * two -> mid, thud -> big, reveal -> hero), THE CHIME LADDER rolls with THE BANK's count-up, a no-pay spin's last
 * stop is THE SETTLE (soft, felt, never a fail), A1's tease is the riser and A2's almost resolves quietly. */

import { kit, DEFAULT_SFX, LEVER_VARIANTS, REEL_VARIANTS } from '../../shared/sound/kit.js';

const WIN_TIER = { chime: 'small', two: 'mid', thud: 'big', reveal: 'hero' };
const LEVEL = { thud: 0.5, settle: 0.05, chime: 0.34, token: 0.12, rise: 0.2, lever: 0.24, roll: 0.09 };

/** The owner's pick, kept where he can change it while playing (shared/sound/audition.html writes it). */
export const VARIANT_KEY = 'br.sfx.variant';
const up = v => String(v == null ? '' : v).trim().toUpperCase();
const asLever = v => (LEVER_VARIANTS.includes(up(v)) ? up(v) : null);
const asReel = v => (REEL_VARIANTS.includes(up(v)) ? up(v) : null);

/**
 * Pure: which lever and which drum to play. The kit's defaults (lever B, reel A) first, then the saved pair
 * (`store`, the localStorage value: {"lever":"A","reel":"D"} or the compact "A/D"), then the URL query
 * (?lever=A&reel=D), which wins so a dev page can try a pair without touching what is saved.
 */
export function pickVariant({ search = '', store = null } = {}) {
  const out = { ...DEFAULT_SFX };
  let saved = store && typeof store === 'object' ? store : null;
  if (typeof store === 'string' && store.trim()) {
    try { const j = JSON.parse(store); if (j && typeof j === 'object') saved = j; } catch (e) { /* not json, try the compact pair */ }
    if (!saved) { const m = up(store).match(/^([A-D])[\s/,-]*([A-D])$/); if (m) saved = { lever: m[1], reel: m[2] }; }
  }
  if (saved) { const l = asLever(saved.lever); if (l) out.lever = l; const r = asReel(saved.reel); if (r) out.reel = r; }
  let q = null;
  try { q = new URLSearchParams(String(search || '')); } catch (e) { q = null; }
  if (q) { const l = asLever(q.get('lever')); if (l) out.lever = l; const r = asReel(q.get('reel')); if (r) out.reel = r; }
  return out;
}

/** The page's own pair: the query and the saved key, read once when the station opens. */
function readVariant() {
  let search = '', store = null;
  try { search = typeof location !== 'undefined' ? location.search : ''; } catch (e) { /* not a page */ }
  try { store = typeof localStorage !== 'undefined' ? localStorage.getItem(VARIANT_KEY) : null; } catch (e) { /* blocked */ }
  return pickVariant({ search, store });
}

export function createSound(k = kit, over = null) {
  let suspended = false, disposed = false, tokens = 0;
  const variant = { ...readVariant(), ...(over && typeof over === 'object' ? pickVariant({ store: over }) : null) };
  const rolling = new Set();                  // reels already turning, so only the first frame traces
  const sent = new Map();                     // reel -> [speed, page time] of the last update sent to the kit
  const trace = [];   // the last cues with their page time, for dev.html and CDP checks
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());
  function note(name, semis, level, inMs = 0) { trace.push({ name, semis, level, at: Math.round(now()), in: Math.round(inMs) }); if (trace.length > 60) trace.shift(); }
  const live = () => !disposed && !suspended;
  /** Every voice this station may have scheduled ahead, the drums included. */
  const quiet = () => { k.stop('riser'); k.stop('ladder'); k.stop('breath'); k.stop('reel'); rolling.clear(); sent.clear(); };

  const api = {
    /** Wake the kit inside a gesture (a press or a pull). */
    arm() { if (live()) k.arm(); },
    /**
     * THE LEVER PULL, the whole gesture, on the frame the lever starts to move (Law VIII: it leans before the
     * tape or the server answers, and it sounds on that same frame).
     */
    lever() {
      note('lever-' + variant.lever, 0, LEVEL.lever);
      if (live()) k.play('lever', { variant: variant.lever });
    },
    /**
     * THE REEL ROLL for reel `i`: the drum starts turning on its first frame and follows its own decel from
     * there (`speed` 1 at full blur, 0 into the stop), so A1's stretched third reel crawls in sound too. The
     * roll ends on the stop frame, in thud() below. Updates are throttled: one per 60 ms or per 2% of speed.
     */
    roll(i, speed = 1) {
      const sp = Math.max(0, Math.min(1, Number(speed) || 0));
      if (!live()) return;
      if (!rolling.has(i)) {
        rolling.add(i); sent.set(i, [sp, now()]);
        note('roll-' + variant.reel, i, LEVEL.roll);
        k.play('reel', { reel: i, variant: variant.reel, speed: sp });
        return;
      }
      const t = now(), was = sent.get(i) || [0, 0];
      if (Math.abs(sp - was[0]) < 0.02 && t - was[1] < 60) return;
      sent.set(i, [sp, t]);
      k.setRollSpeed(i, sp);
    },
    /** THE THUD for reel `i` (0..2), rising left to right; the last stop of a no-pay spin is THE SETTLE.
     *  The drum stops on this same frame and the reel voice's own stop gesture carries the thud (Law X). */
    thud(i, muted = false) {
      const semis = muted ? -5 : (i - 1) * 2;
      note(muted ? 'thud-muted' : 'thud', semis, muted ? LEVEL.settle : LEVEL.thud);
      rolling.delete(i); sent.delete(i);
      k.stop('reel', { reel: i });
      if (!live()) return;
      if (muted) k.play('settle'); else k.play('reelStop', { reel: i, variant: variant.reel });
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
    /** Which pair is playing, for dev.html and the CDP checks. */
    get variant() { return { ...variant }; },
    trace,
  };
  return api;
}
