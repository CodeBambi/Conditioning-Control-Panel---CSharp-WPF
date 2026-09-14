/* bank.js - THE BANK (Law XII) for the wheel. Value leaves the landed slice and flies to the SP readout:
 * 3-7 tokens (4 on lite), 560 ms each, 70 ms stagger, the readout ticks per landing (never before, Law X), the
 * last one lands with a mini-thud. The slot's bank (stations/slot/bank.js on the feel branch) is the pattern;
 * the wheel only ever pays, so there is no reversed spend here.
 *
 * Law I: a picture of a number the server already settled. skip() puts the readout on the final value at once.
 * Law VI: reduced motion takes the STATE: no tokens, the final value, a lit readout, the cue still plays. */

import { FEEL, tickValues } from './feel.js';

const ARC_PX = 70;

export function createBank({ layer, reduced, onTick, onLand, onDone }) {
  let run = null, raf = 0;

  function frame(now) {
    raf = 0;
    const r = run;
    if (!r) return;
    let flying = false;
    r.tokens.forEach((tok, i) => {
      const q = (now - r.start - i * FEEL.BANK_STAGGER_MS) / FEEL.BANK_FLY_MS;
      if (q >= 1) {
        if (!tok.landed) {
          tok.landed = true; tok.el.remove();
          onTick(r.values[i], false);
          if (i === r.tokens.length - 1) onLand();
        }
        return;
      }
      flying = true;
      if (q < 0) { tok.el.style.opacity = '0'; return; }
      const from = r.from(), to = r.to(), s = (i % 3) - 1;
      const x = from.x + s * 10 + (to.x - from.x - s * 10) * q, y = from.y + (to.y - from.y) * q * q - ARC_PX * Math.sin(Math.PI * q);
      tok.el.style.opacity = '1';
      tok.el.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px) scale(${(1 - 0.35 * q).toFixed(3)})`;
    });
    if (flying) raf = requestAnimationFrame(frame);
    else finish();
  }

  function finish() {
    const r = run;
    run = null;
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    if (!r) return;
    r.tokens.forEach(t => t.el.remove());
    if (onDone) onDone();
  }

  return {
    get busy() { return !!run; },
    /** from()/to() give client px each frame. Returns 'flying' or 'state' (reduced). */
    start({ n, fromValue, toValue, from, to }) {
      if (run) this.skip();
      if (reduced) { onTick(toValue, true); onLand(); if (onDone) onDone(); return 'state'; }
      const origin = layer.getBoundingClientRect();
      const tokens = Array.from({ length: n }, () => {
        const el = document.createElement('i');
        el.className = 'wheel-token';
        el.style.opacity = '0';
        layer.append(el);
        return { el, landed: false };
      });
      const rel = f => () => { const p = f() || { x: origin.width / 2, y: origin.height / 2 }; return { x: p.x - origin.left, y: p.y - origin.top }; };
      run = { values: tickValues(fromValue, toValue, n), tokens, from: rel(from), to: rel(to), start: performance.now() };
      raf = requestAnimationFrame(frame);
      return 'flying';
    },
    /** Settle at once on the final value, no cue (Law VI). */
    skip() {
      const r = run;
      if (!r) return;
      onTick(r.values[r.values.length - 1], true);
      finish();
    },
    dispose() { run = null; if (raf) cancelAnimationFrame(raf); raf = 0; },
  };
}
