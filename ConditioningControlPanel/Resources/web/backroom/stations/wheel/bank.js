/* bank.js - THE BANK (Law XII) at the wheel: the ELEMENTS, over the shared engine.
 *
 * The maths, the clock, the tick ladder, the rollup tail and the event order left this file for
 * shared/win/bank.js (CONTRACT 10.22.C). This file and stations/slot/bank.js were the same arithmetic
 * written twice - the header here used to say so in as many words ("the slot's bank is the pattern"). What
 * stays is what is NOT shared and should not be: the `<i class="wheel-token">` elements, the layer's rect and
 * the rel() that turns the station's client px into layer px. The token class differs per station, so the
 * element code does too.
 *
 * The API station.js knows is unchanged - busy, start(), skip(), dispose() - with one addition: `rollupMs`
 * (the plan's partyMs), how long the READOUT keeps counting once the tokens are down. It never changes the
 * token count or the flight, and the mini-thud waits for the END of that count, not the end of the flight
 * (Law X: one gesture, one beat).
 *
 *   Law I   a picture of a number the server already settled. The run only ever finishes on `settled`.
 *   Law VI  reduced motion takes the STATE - no tokens, the final value, a lit readout, the cue still plays -
 *           and skip() settles at once and leaves quietly. Back, suspend and close never get a faster
 *           version of the travel.
 *   Law X   the readout ticks when a token LANDS, never before.
 *   Brake 8 Calm still flies (the caller's `n` is already capped at 4 by the plan). Calm is NOT reduced
 *           motion: a value that just changes is a Law XII break at every motion level, so `reduced` here is
 *           OS reduced motion / Motion off alone - the conflation this pass split.
 *
 * The wheel only ever pays, so the engine's 'spend' kind and its merge() go unspent here: one free spin a day
 * cannot land a second pay inside a running one.
 */

import { createBankRun, tokenAt } from '../../shared/win/bank.js';

/** `reduced` may be a flag or a getter (the station's is live). `now` is the clock seam the tests drive. */
export function createBank({ layer, reduced, onTick, onLand, onDone, now = () => performance.now() }) {
  let run = null, els = [], from = null, to = null, startMs = 0, raf = 0;

  /** Every token element goes; the run is already over or about to be. */
  function clearTokens() {
    for (const el of els) if (el) el.remove();
    els = [];
  }

  function teardown() {
    run = null;
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    clearTokens();
    if (onDone) onDone();
  }

  /** The engine's events, in the order it gave them. `tail` is the rollup's own count-up: no second gesture,
   *  so no token cue (that is what the station's `quiet` flag has always meant). */
  function play(events) {
    for (const e of events) {
      if (e.type === 'tick') onTick(e.value, !!e.tail);
      else if (e.type === 'land') { if (!e.counting) onLand(); }
      else if (e.type === 'done') teardown();
    }
  }

  /** from()/to() are re-measured every frame: the cabinet may still be moving under the tokens. */
  function draw(list, ms) {
    if (!list.length) { clearTokens(); return; }
    const a = from(), b = to();
    for (const { i, state } of list) {
      const el = els[i];
      if (!el) continue;
      if (state === 'landed') { el.remove(); els[i] = null; continue; }
      const p = tokenAt(i, ms, a, b);
      el.style.opacity = String(p.opacity);
      if (p.opacity) el.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px) scale(${p.scale.toFixed(3)})`;
    }
  }

  function frame(at) {
    raf = 0;
    const r = run;
    if (!r) return;
    const out = r.step(at);
    draw(out.tokens, at - startMs);
    play(out.events);
    if (run === r && !out.done) raf = requestAnimationFrame(frame);
  }

  return {
    get busy() { return !!run; },
    /**
     * from()/to() give client px each frame. `rollupMs` is the plan's partyMs (0 keeps the flat count-up).
     * Returns 'flying', or 'state' when reduced motion took the settled value instead (Law VI).
     */
    start({ n, fromValue, toValue, from: src, to: dst, rollupMs = 0 }) {
      if (run) this.skip();
      const off = typeof reduced === 'function' ? reduced() : reduced;
      startMs = now();
      run = createBankRun({ kind: 'pay', n, fromValue, toValue, rollupMs, reduced: !!off, startMs });
      if (off) { play(run.step(startMs).events); return 'state'; }
      const origin = layer.getBoundingClientRect();
      const rel = f => () => { const p = f() || { x: origin.width / 2, y: origin.height / 2 }; return { x: p.x - origin.left, y: p.y - origin.top }; };
      from = rel(src); to = rel(dst);
      els = Array.from({ length: run.n }, () => {
        const el = document.createElement('i');
        el.className = 'wheel-token';
        el.style.opacity = '0';
        layer.append(el);
        return el;
      });
      raf = requestAnimationFrame(frame);
      return 'flying';
    },
    /** Settle at once on the settled value, no mini-thud (Law VI: Back, suspend and close leave quietly). */
    skip() {
      const r = run;
      if (!r) return;
      play(r.skip());
    },
    dispose() { run = null; if (raf) cancelAnimationFrame(raf); raf = 0; clearTokens(); },
  };
}
