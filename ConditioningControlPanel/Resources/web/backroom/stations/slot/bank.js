/* bank.js - THE BANK (Law XII) for the slot, lane F1. Value leaves where it was won and flies to where it
 * is kept: 3-7 tokens (4 on lite) arc from payout_spawn to the SP readout, 560 ms each, 70 ms stagger, the
 * readout ticks per landing (never before, Law X) and the last one lands with a mini-thud. A spend runs it
 * reversed: the tokens leave the readout, it ticks down as each one leaves, and they land in payout_tray.
 *
 * Law I: this is a picture of numbers tape.js already settled. It only decides what the readout SAYS
 * between `from` and `to`, and skip() puts it on `to` at once (Back, suspend, a newer bank).
 * Law VI: reduced motion takes the STATE, not a faster flight: no tokens, the readout is on `to` with a
 * declared lit look for a moment, and the cue still plays. Driven by rAF on performance.now, not CSS. */

import { FEEL, tickValues } from './feel.js';

const ARC_PX = 70;   // how far the path bows above the straight line

export function createBank({ layer, reduced, onTick, onLand, onDone }) {
  let run = null, raf = 0;

  function frame(now) {
    raf = 0;
    const r = run;
    if (!r) return;
    let flying = false;
    r.tokens.forEach((tok, i) => {
      const q = (now - r.start - i * FEEL.BANK_STAGGER_MS) / FEEL.BANK_FLY_MS;
      const tick = () => { r.shown = r.values[i]; onTick(r.values[i], r.kind); };
      if (r.kind === 'spend' && q >= 0 && !tok.left) { tok.left = true; tick(); }   // reversed: it ticks down as each one leaves
      if (q >= 1) {
        if (!tok.landed) {
          tok.landed = true; tok.el.remove();
          if (r.kind === 'pay') tick();   // Law X: the counter ticks as the token lands
          if (i === r.tokens.length - 1) onLand(r.kind);
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
    if (onDone) onDone(r.kind);
  }

  return {
    get busy() { return !!run; },
    get kind() { return run ? run.kind : null; },
    /** kind 'pay' | 'spend'. from()/to() give client px each frame (the cabinet may still be moving). */
    start({ kind, n, fromValue, toValue, from, to }) {
      if (run) {
        // Brake 2: one hero per beat. A pay landing inside a running pay merges into it (its last tick
        // lands on the newer total); anything else settles the running one first.
        if (run.kind === kind && kind === 'pay') {
          const landed = run.tokens.filter(t => t.landed).length;
          run.values = [...run.values.slice(0, landed), ...tickValues(run.shown, toValue, run.tokens.length - landed)];
          return 'merged';
        }
        this.skip();
      }
      const values = tickValues(fromValue, toValue, n);
      if (reduced) { onTick(toValue, kind, true); onLand(kind); if (onDone) onDone(kind); return 'state'; }
      const origin = layer.getBoundingClientRect();
      const tokens = Array.from({ length: n }, () => {
        const el = document.createElement('i');
        el.className = `slot-token slot-token-${kind}`;
        el.style.opacity = '0';
        layer.append(el);
        return { el, landed: false };
      });
      const rel = f => () => { const p = f() || { x: origin.width / 2, y: origin.height / 2 }; return { x: p.x - origin.left, y: p.y - origin.top }; };
      run = { kind, values, tokens, shown: fromValue, from: rel(from), to: rel(to), start: performance.now() };
      raf = requestAnimationFrame(frame);
      return 'flying';
    },
    /** Settle at once: every remaining tick lands on its final value, no cue (Law VI). */
    skip() {
      const r = run;
      if (!r) return;
      onTick(r.values[r.values.length - 1], r.kind, true);
      finish();
    },
    dispose() { run = null; if (raf) cancelAnimationFrame(raf); raf = 0; },
  };
}
