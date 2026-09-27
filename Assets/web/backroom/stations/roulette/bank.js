/* ============================================================================
 * bank.js - THE BANK at the Velvet Vortex (Law XII, CONTRACT 10.22), the ELEMENT half.
 *
 * "Value leaves where it was won and flies to where it is kept. Nothing ever just changes." The roulette
 * broke that outright: station.js took ctx.spReadout.owe() and nothing else, so a pay made the SP number
 * redraw where it stood. Now a handful of tokens leaves the PAYING CHIPS on the mat and arcs to the chip
 * that keeps them, the readout ticking as each one lands (Law X) and the last one landing with a mini-thud.
 *
 * THE MATHS, THE CLOCK AND THE EVENT ORDER ARE NOT HERE. shared/win/bank.js owns all three for all four
 * stations - the token count, the flight, the tick ladder, the rollup tail, the merge and the skip - and it
 * is pure, so node:test holds it. This file is the half that a pure engine cannot have: the <i> elements,
 * the layer's rect, the rAF loop and the two points measured every frame. The slot and the wheel keep their
 * own element halves for the same reason; only the token class and where it flies from differ.
 *
 * WHY `from` TAKES AN INDEX. The slot pays out of one spawn point. Here a spin can pay three cells at once,
 * so token i leaves the chip feel.tokenSpots dealt it: the handful comes off the mat spread out, and every
 * chip that paid is seen to send something.
 *
 *   Law VI   skip({ land: false }) is Back, suspend and close: the readout settles, nothing sounds.
 *            skip({ land: true }) is the whole settled state, mini-thud included - a new spin, reduced
 *            motion. Reduced motion never gets a faster flight: `reduced` gives it the STATE (the engine's
 *            'state' mode), no tokens at all, and the cue still plays.
 *   Law X    the readout ticks on a LANDING, never before, and the mini-thud waits out the count-up.
 *   Brake 2  a pay landing inside a running pay MERGES into it; parties never stack.
 *
 * TRAP: the layer's rect and both flight points are read EVERY FRAME. The mat is a canvas that relays on a
 * resize and, seated in the room (mat-3d.js), a projection through a camera that is still moving - a point
 * cached at the start of the flight would send the tokens somewhere the chip no longer is.
 * ==========================================================================*/

import { createBankRun, tokenAt } from '../../shared/win/bank.js';

/**
 * @param {Object} o
 * @param {Element} o.layer    the .roul-tokens layer; tokens are positioned inside its own box
 * @param {Function} o.onTick  (value, tail) - the readout now says `value`. tail = it came from the rollup's
 *                             count-up, not a token: no pop, no second gesture (Law X).
 * @param {Function} o.onLand  (counting) - the tokens are down. counting = still counting, HOLD the mini-thud.
 * @param {Function} o.onDone  () - tear the tokens down, the readout goes back to the room's own rule.
 */
export function createBank({ layer, onTick, onLand, onDone }) {
  let run = null, raf = 0, els = [], startMs = 0, src = null, dst = null;

  /** A client point in the layer's own box; the middle of the layer when the caller has nothing to offer. */
  function rel(p, box) {
    if (!p || !Number.isFinite(p.x) || !Number.isFinite(p.y)) return { x: box.width / 2, y: box.height / 2 };
    return { x: p.x - box.left, y: p.y - box.top };
  }
  function draw(tokens, now) {
    const box = layer.getBoundingClientRect(), to = rel(dst(), box), ms = now - startMs;
    for (const tk of tokens) {
      const e = els[tk.i];
      if (!e) continue;
      const a = tokenAt(tk.i, ms, rel(src(tk.i), box), to);
      if (!a.opacity) { e.style.opacity = '0'; continue; }
      e.style.opacity = '1';
      e.style.transform = `translate(${a.x.toFixed(1)}px, ${a.y.toFixed(1)}px) scale(${a.scale.toFixed(3)})`;
    }
  }
  /** The engine's events, in its order. 'done' tears the run down, so nothing after it can fire. */
  function play(events) {
    for (const ev of events) {
      if (ev.type === 'tick') onTick(ev.value, !!ev.tail);
      else if (ev.type === 'land') onLand(!!ev.counting);
      else if (ev.type === 'done') { finish(); if (onDone) onDone(); }
    }
  }
  function finish() {
    run = null; src = null; dst = null;
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    for (const e of els) e.remove();
    els = [];
  }
  function frame(now) {
    raf = 0;
    if (!run) return;
    const res = run.step(now);
    if (res.tokens.length) draw(res.tokens, now);
    play(res.events);
    if (run) raf = requestAnimationFrame(frame);
  }

  return {
    get busy() { return !!run; },
    /**
     * A pay takes off. `from(i)` and `to()` give CLIENT px and are called every frame.
     * `rollupMs` is plan.partyMs: how long the READOUT keeps counting past the tokens (it never changes the
     * count or the flight). `reduced` is Law VI - hand it `plan.bank === 0` and the engine takes the state.
     * -> 'merged' (Brake 2, it joined a flight already in the air) | 'state' | 'flying'
     */
    start({ n, fromValue, toValue, from, to, rollupMs = 0, reduced = false }) {
      if (run) {
        // Brake 2: one hero per beat. The tokens already down keep what they ticked; the ones still in the
        // air are re-aimed at the newer total. They keep the chips they left - they are already flying.
        const how = run.merge(toValue);
        if (how) { dst = to; return how; }
        this.skip({ land: true });
      }
      const count = Math.max(1, Math.trunc(Number(n)) || 1);
      startMs = performance.now();
      src = from; dst = to;
      run = createBankRun({ kind: 'pay', n: count, fromValue, toValue, rollupMs, reduced, startMs });
      if (run.mode === 'state') { play(run.step(startMs).events); return 'state'; }   // Law VI: no tokens, the cue still plays
      els = Array.from({ length: count }, () => {
        const e = document.createElement('i');
        e.className = 'roul-token';
        e.style.opacity = '0';
        layer.append(e);
        return e;
      });
      raf = requestAnimationFrame(frame);
      return 'flying';
    },
    /** Law VI. `land` takes the whole settled state, mini-thud included; Back and suspend leave quietly. */
    skip({ land = false } = {}) {
      if (!run) return;
      play(run.skip({ land }));
      finish();
    },
    dispose() { finish(); },
    /** Test seam for dev.html and the checks. */
    debug() { return run ? { flying: true, n: run.n, shown: run.shown, settled: run.settled, mode: run.mode } : null; },
  };
}
