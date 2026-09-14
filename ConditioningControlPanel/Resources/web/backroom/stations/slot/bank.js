/* bank.js - THE BANK (Law XII) for the slot, lane F1. Value leaves where it was won and flies to where it
 * is kept: 3-7 tokens (4 on lite) arc from payout_spawn to the SP readout, 560 ms each, 70 ms stagger, the
 * readout ticks per landing (never before, Law X) and the last one lands with a mini-thud. A spend runs it
 * reversed: the tokens leave the readout, it ticks down as each one leaves, and they land in payout_tray.
 *
 * The count-up is proportional (playbook A3): the tokens never change, but a bigger win keeps the readout
 * counting past their landing, over `rollupMs`, and the mini-thud waits for the end of that count.
 *
 * Law I: this is a picture of numbers tape.js already settled. It only decides what the readout SAYS
 * between `from` and `to`, and skip() puts it on `to` at once (Back, suspend, a newer bank).
 * Law VI: reduced motion takes the STATE, not a faster flight: no tokens, the readout is on `to` with a
 * declared lit look for a moment, and the cue still plays. Driven by rAF on performance.now, not CSS. */

import { FEEL, tickValues, rollupTicks, rollupAt, bankFlightMs } from './feel.js';

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
    if (flying) { raf = requestAnimationFrame(frame); return; }
    // The rollup's tail (playbook A3): the tokens are down, and on a big win the readout carries on counting
    // to the settled value over the rest of the rollup. onLand(kind, true) says "landed, still counting", so
    // the mini-thud waits for the end (Law X: one gesture, one beat).
    const age = now - r.start;
    if (age < r.total) {
      if (!r.rolled) { r.rolled = true; r.tailFrom = r.shown; onLand(r.kind, true); }
      const v = rollupAt(r.tailFrom, r.settled, (age - r.flight) / Math.max(1, r.total - r.flight));
      if (v !== r.shown) { r.shown = v; onTick(v, r.kind, true); }
      raf = requestAnimationFrame(frame);
      return;
    }
    if (r.shown !== r.settled) { r.shown = r.settled; onTick(r.settled, r.kind, true); }   // Law I: it lands on the tape's number
    onLand(r.kind, false);
    finish();
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
    /** kind 'pay' | 'spend'. from()/to() give client px each frame (the cabinet may still be moving).
     *  `rollupMs` (playbook A3) is how long the READOUT counts; it never changes the token count or their
     *  flight, only how far past it the count carries on. 0 or less keeps the old flat count-up. */
    start({ kind, n, fromValue, toValue, from, to, rollupMs = 0 }) {
      const flight = bankFlightMs(n), total = Math.max(flight, Number(rollupMs) || 0);
      if (run) {
        // Brake 2: one hero per beat. A pay landing inside a running pay merges into it (its last tick
        // lands on the newer total); anything else settles the running one first.
        if (run.kind === kind && kind === 'pay') {
          const landed = run.tokens.filter(t => t.landed).length, left = run.tokens.length - landed;
          run.settled = toValue;   // Law I: the tail and skip() both aim at the newer total
          if (left > 0) run.values = [...run.values.slice(0, landed), ...tickValues(run.shown, toValue, left)];
          return 'merged';
        }
        this.skip();
      }
      const values = rollupTicks(fromValue, toValue, n, total);
      if (reduced) { onTick(toValue, kind, true); onLand(kind, false); if (onDone) onDone(kind); return 'state'; }
      const origin = layer.getBoundingClientRect();
      const tokens = Array.from({ length: n }, () => {
        const el = document.createElement('i');
        el.className = `slot-token slot-token-${kind}`;
        el.style.opacity = '0';
        layer.append(el);
        return { el, landed: false };
      });
      const rel = f => () => { const p = f() || { x: origin.width / 2, y: origin.height / 2 }; return { x: p.x - origin.left, y: p.y - origin.top }; };
      run = { kind, values, tokens, shown: fromValue, from: rel(from), to: rel(to), start: performance.now(),
              settled: Math.round(Number(toValue) || 0), flight, total, rolled: false, tailFrom: fromValue };
      raf = requestAnimationFrame(frame);
      return 'flying';
    },
    /** Settle at once: the readout goes straight to the settled value, no travel, no cue (Law VI, Brake 7).
     *  It reads `settled`, never the tick ladder, so a rollup that was cut short can never leave the readout
     *  short or count a value twice (Law I). `land` takes the whole settled STATE, mini-thud and +N included:
     *  that is what a lever press, a new spin or reduced motion want. Back and suspend leave quietly. */
    skip({ land = false } = {}) {
      const r = run;
      if (!r) return;
      onTick(r.settled, r.kind, true);
      if (land) onLand(r.kind, false);
      finish();
    },
    dispose() { run = null; if (raf) cancelAnimationFrame(raf); raf = 0; },
  };
}
