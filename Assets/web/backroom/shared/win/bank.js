/* bank.js - THE BANK (Law XII), ONE copy, for all four stations (lane BR2-spine, CONTRACT 10.22).
 *
 * Value leaves where it was won and flies to where it is kept. Nothing ever just changes. The roulette
 * breaks that outright today (the SP number simply redraws) and the cards have nothing at all, so the move
 * had to come out of the slot before it could be spent anywhere else.
 *
 * stations/slot/bank.js and stations/wheel/bank.js are two copies of this move - the wheel's own header says
 * "the slot's bank is the pattern". This file is the engine both of them are: the token count, the flight
 * clock, the value ladder, the rollup tail, the merge and the skip. ADDITIVE: neither station file is
 * touched here and both still work; the slot and wheel lanes retrofit their call sites onto this.
 *
 * PURE. No DOM, no rAF, no audio, no timers - the caller owns the frame loop and hands `now` in, the same
 * way stations/slot/feel.js is pure so node:test can hold it. That is also what keeps it DOM-agnostic
 * enough for the slot's REVERSED SPEND: the engine never knows where a token flies from, only that a
 * `spend` ticks the readout as each token LEAVES and a `pay` ticks it as each one LANDS.
 *
 *   Law I     a picture of numbers the tape already settled. `settled` is the only value it ever lands on;
 *             the tick ladder in between is decoration and may be cut short at any frame.
 *   Law VI    skip() puts the readout on the settled value at once - Back, suspend, close, a newer bank,
 *             a lever press. `reduced` never gets a faster flight, it gets the STATE and the cue.
 *   Law X     the readout ticks when the tokens LAND, never before, and the mini-thud waits for the end of
 *             the count-up, not the end of the flight.
 *   Brake 2   a pay landing inside a running pay MERGES into it. Parties never stack.
 *   Brake 8   a lite board gets 4 tokens, never 7. The sound still carries every beat.
 *
 * TRAP: `step()` is edge-triggered. It returns the events that happened SINCE the last call, so calling it
 * twice on one frame is safe (the second returns nothing) but skipping frames merges ticks - which is what
 * you want, a dropped frame must never leave the readout short.
 */

import { CFX, bankCount } from '../../../arcademy/shell/counterfx.js';

export const BANK = Object.freeze({
  FLY_MS: CFX.BANK_FLY_MS,          // 560 ms a token, arc from source to counter
  STAGGER_MS: CFX.BANK_STAGGER_MS,  // 70 ms apart
  MIN: CFX.BANK_MIN,                // 3 tokens at the least...
  MAX: CFX.BANK_MAX,                // ...7 at the most...
  MAX_LITE: CFX.BANK_MAX_LITE,      // ...4 on a board that asked for less (Brake 8)
  ARC_PX: 70,                       // how far the path bows above the straight line (both stations' number)
  COUNTUP_MS: CFX.COUNTUP_MS,       // the flat count-up, 500 ms, when nothing asked for a rollup
});

/* ----------------------------------------------------------------------------
 * THE COUNTS AND THE CLOCK
 * -------------------------------------------------------------------------- */

/** THE BANK's token count for a WIN, by rung. 3 at tier 1, 7 at the hero, capped at 4 on lite.
 *  Both station files compute exactly this; it is the same arithmetic in one place. */
export function winTokens(tier, lite = false) {
  const t = Math.max(0, Math.min(4, Math.floor(Number(tier) || 0)));
  const hi = lite ? BANK.MAX_LITE : BANK.MAX;
  return Math.max(BANK.MIN, Math.min(hi, t >= 4 ? BANK.MAX : t + 2));
}

/** THE BANK's token count for a SPEND, by price (counterfx's reversed count: 3 under 60, 7 at 1,000). */
export const spendTokens = (cost, lite = false) => bankCount(cost, lite);

/** The whole flight of `n` tokens, first leaving to last landing. */
export const bankFlightMs = n => BANK.FLY_MS + Math.max(0, (n | 0) - 1) * BANK.STAGGER_MS;
/** When token `i` lands, in ms from the start. */
export const bankLandMs = i => BANK.FLY_MS + Math.max(0, i | 0) * BANK.STAGGER_MS;
/** Token `i`'s progress at `ms` into the run: below 0 it has not left, 1 and over it is down. */
export const tokenQ = (i, ms) => ((Number(ms) || 0) - Math.max(0, i | 0) * BANK.STAGGER_MS) / BANK.FLY_MS;

/* ----------------------------------------------------------------------------
 * THE VALUE LADDER - what the readout SAYS between `from` and `to`
 * -------------------------------------------------------------------------- */

/** The readout after each landing: evenly stepped, the last one exactly `to` (Law I: it lands on the tape's
 *  number, never on a rounding of it). */
export function tickValues(from, to, n) {
  const a = Math.round(Number(from) || 0), b = Math.round(Number(to) || 0), k = Math.max(1, n | 0);
  return Array.from({ length: k }, (_, i) => (i === k - 1 ? b : Math.round(a + (b - a) * ((i + 1) / k))));
}

/** The count-up curve: a shallow ease-out, so a big roll sprints and then settles. `q >= 1` is exactly `to`. */
export function rollupAt(from, to, q) {
  const a = Math.round(Number(from) || 0), b = Math.round(Number(to) || 0);
  const k = Math.min(1, Math.max(0, Number(q) || 0));
  return k >= 1 ? b : Math.round(a + (b - a) * (1 - (1 - k) ** 2));
}

/** The readout at each landing. With no rollup longer than the flight this is the even ladder above; with
 *  one, each landing reads that moment's count and the tail finishes the job after the tokens are down. */
export function rollupTicks(from, to, n, ms) {
  const k = Math.max(1, n | 0), flight = bankFlightMs(k);
  if (!(Number(ms) > flight)) return tickValues(from, to, k);
  return Array.from({ length: k }, (_, i) => rollupAt(from, to, bankLandMs(i) / Number(ms)));
}

/* ----------------------------------------------------------------------------
 * THE PATH - the only geometry, and it is a function, not an element
 * -------------------------------------------------------------------------- */

/**
 * Token `i` at `ms` into the run, flying `from` -> `to` (plain `{x,y}` in whatever space the caller draws in;
 * both stations hand client px measured every frame, because the cabinet may still be moving).
 *
 * Returns `{ q, x, y, scale, opacity, state }`, `state` one of 'waiting' | 'flying' | 'landed'. A spend hands
 * the readout as `from` and the tray as `to` - the arc is the same one, run the other way, which is why this
 * engine needs no separate reversed path.
 */
export function tokenAt(i, ms, from, to, opts) {
  const arc = (opts && Number(opts.arc)) || BANK.ARC_PX;
  const q = tokenQ(i, ms);
  const a = from || { x: 0, y: 0 }, b = to || { x: 0, y: 0 };
  if (q >= 1) return { q, x: b.x, y: b.y, scale: 0.65, opacity: 0, state: 'landed' };
  if (q < 0) return { q, x: a.x, y: a.y, scale: 1, opacity: 0, state: 'waiting' };
  const s = ((i | 0) % 3) - 1;                       // three lanes, so seven tokens are a handful, not a line
  const x = a.x + s * 10 + (b.x - a.x - s * 10) * q;
  const y = a.y + (b.y - a.y) * q * q - arc * Math.sin(Math.PI * q);
  return { q, x, y, scale: 1 - 0.35 * q, opacity: 1, state: 'flying' };
}

/* ----------------------------------------------------------------------------
 * THE RUN - a pure state machine the caller drives with `now`
 * -------------------------------------------------------------------------- */

/**
 * createBankRun(spec) -> the run.
 *
 *   kind        'pay' (tokens fly TO the readout, it ticks on each LANDING) | 'spend' (tokens fly FROM it,
 *               it ticks as each one LEAVES). Law X reads the same either way: the tick rides the gesture.
 *   n           how many tokens (winTokens / spendTokens)
 *   fromValue   what the readout says now
 *   toValue     what the tape settled on. THE only value the run ever finishes at (Law I).
 *   rollupMs    how long the READOUT keeps counting (plan.partyMs). It never changes the token count or
 *               their flight, only how far past it the count carries. 0 keeps the flat count-up.
 *   reduced     Law VI: no tokens, the settled value at once, the cue still plays. `mode` is 'state'.
 *   startMs     the clock this run started on, in the caller's own time base.
 *
 * The caller loops: `const { shown, tokens, events, done } = run.step(now)`, draws `tokens` through
 * `tokenAt`, and plays `events` in the order they came:
 *
 *   { type: 'tick',  value, kind, tail }   the readout now says `value`. `tail` = it came from the rollup's
 *                                          count-up rather than a token (no pop, no second gesture).
 *   { type: 'land',  kind, counting }      the tokens are down. `counting: true` = the readout is STILL
 *                                          counting, so hold the mini-thud; `counting: false` = the whole
 *                                          move is settled, THIS is the mini-thud frame (Law X).
 *   { type: 'done',  kind }                the run is over, tear the tokens down.
 */
export function createBankRun(spec) {
  const o = spec || {};
  const kind = o.kind === 'spend' ? 'spend' : 'pay';
  const n = Math.max(1, Math.floor(Number(o.n) || 1));
  const reduced = !!o.reduced;
  const startMs = Number(o.startMs) || 0;
  const fromValue = Math.round(Number(o.fromValue) || 0);
  const flight = bankFlightMs(n);
  const total = Math.max(flight, Number(o.rollupMs) || 0);

  let settled = Math.round(Number(o.toValue) || 0);
  let values = rollupTicks(fromValue, settled, n, total);
  let shown = fromValue;
  let done = false;
  let rolled = false;          // the 'land, still counting' edge has fired
  let tailFrom = fromValue;    // where the rollup's tail picks the count up from
  const fired = Array.from({ length: n }, () => false);   // per token: landed (pay) / left (spend)

  const tokenState = ms => Array.from({ length: n }, (_, i) => {
    const q = tokenQ(i, ms);
    return { i, q, state: q >= 1 ? 'landed' : q < 0 ? 'waiting' : 'flying' };
  });

  /** The settled STATE, with no travel: what reduced motion and every exit take (Law VI). */
  function settleEvents(land) {
    shown = settled;
    // Brake 9: the tick fires even when the readout already said it, so the text is repainted whatever the
    // caller did with the last frame.
    const out = [{ type: 'tick', value: settled, kind, tail: true }];
    if (land) out.push({ type: 'land', kind, counting: false });
    out.push({ type: 'done', kind });
    done = true;
    return out;
  }

  return {
    kind, n, flight, total,
    /** 'state' = reduced motion, there is nothing to fly; 'flying' = the real move. */
    mode: reduced ? 'state' : 'flying',
    get shown() { return shown; },
    get settled() { return settled; },
    get done() { return done; },
    /** The tick ladder, for a test or a log. Frozen after a merge re-aims it. */
    get values() { return values.slice(); },

    /**
     * One frame. `now` is in the same base as `startMs`. Returns
     * `{ shown, tokens, events, flying, done }` - `tokens` is empty once everything is down or in 'state'.
     */
    step(now) {
      if (done) return { shown, tokens: [], events: [], flying: false, done: true };
      if (reduced) return { shown: settled, tokens: [], events: settleEvents(true), flying: false, done: true };

      const ms = (Number(now) || 0) - startMs;
      const events = [];
      let flying = false;

      for (let i = 0; i < n; i++) {
        const q = tokenQ(i, ms);
        // A spend ticks DOWN as each token leaves; a pay ticks UP as each one lands (Law X either way).
        const edge = kind === 'spend' ? q >= 0 : q >= 1;
        if (edge && !fired[i]) { fired[i] = true; shown = values[i]; events.push({ type: 'tick', value: values[i], kind, tail: false }); }
        if (q < 1) flying = true;
      }
      if (flying) return { shown, tokens: tokenState(ms), events, flying: true, done: false };

      // The tokens are down. On a big win the readout carries on counting to the settled value over the
      // rest of the rollup, and the mini-thud waits for the END of that count (Law X, one gesture one beat).
      if (ms < total) {
        if (!rolled) { rolled = true; tailFrom = shown; events.push({ type: 'land', kind, counting: true }); }
        const v = rollupAt(tailFrom, settled, (ms - flight) / Math.max(1, total - flight));
        if (v !== shown) { shown = v; events.push({ type: 'tick', value: v, kind, tail: true }); }
        return { shown, tokens: [], events, flying: false, done: false };
      }
      if (shown !== settled) { shown = settled; events.push({ type: 'tick', value: settled, kind, tail: true }); }
      events.push({ type: 'land', kind, counting: false });
      events.push({ type: 'done', kind });
      done = true;
      return { shown, tokens: [], events, flying: false, done: true };
    },

    /**
     * Brake 2: a second pay landing inside a running pay merges into this one instead of stacking a party on
     * top of it. The tokens already down keep the values they ticked; the ones still in the air are re-aimed
     * at the newer total, and `settled` moves so the tail and skip() both land on it (Law I).
     * Returns 'merged'. A spend never merges - the caller settles the running move first.
     */
    merge(toValue) {
      if (done || kind !== 'pay') return null;
      const landed = fired.filter(Boolean).length, left = n - landed;
      settled = Math.round(Number(toValue) || 0);
      if (left > 0) values = [...values.slice(0, landed), ...tickValues(shown, settled, left)];
      rolled = false;
      tailFrom = shown;
      return 'merged';
    },

    /**
     * Settle at once (Law VI). The readout goes straight to `settled` - never to the last rung of the tick
     * ladder, so a run cut mid-rollup can neither leave the readout short nor count a value twice.
     * `land: true` takes the whole settled state, mini-thud and "+N" included: what a lever press, a new
     * spin and reduced motion want. Back, suspend and close leave quietly with `land: false`.
     */
    skip(opts) {
      if (done) return [];
      return settleEvents(!!(opts && opts.land));
    },
  };
}
