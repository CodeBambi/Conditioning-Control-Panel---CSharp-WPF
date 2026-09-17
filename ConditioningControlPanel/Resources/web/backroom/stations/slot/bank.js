/* bank.js - THE BANK (Law XII) for the slot, lane F1. Value leaves where it was won and flies to where it
 * is kept: 3-7 tokens (4 on lite) arc from payout_spawn to the SP readout, 560 ms each, 70 ms stagger, the
 * readout ticks per landing (never before, Law X) and the last one lands with a mini-thud. A spend runs it
 * reversed: the tokens leave the readout, it ticks down as each one leaves, and they land in payout_tray.
 *
 * THIS FILE IS NOW THE SLOT'S HALF OF THAT MOVE AND ONLY THE SLOT'S HALF (CONTRACT 10.22.C). The token
 * count, the flight clock, the value ladder, the rollup tail, Brake 2's merge and Law VI's skip are
 * shared/win/bank.js's `createBankRun` - one engine for four stations, and node:test can hold it because it
 * owns no clock and no elements. What is left here is what is genuinely the slot's: the
 * `<i class="slot-token">` elements, the layer they live in, the rect they are measured against, and the rAF
 * that drives them. The wheel's tokens are `wheel-token`s in a wheel's layer and the roulette and the cards
 * will want their own, which is why the DOM stayed per station and the maths did not.
 *
 * The count-up is proportional (playbook A3): the tokens never change, but a bigger win keeps the readout
 * counting past their landing, over `rollupMs` (station.js hands it `plan.partyMs`), and the mini-thud waits
 * for the end of that count.
 *
 * Law I: this is a picture of numbers tape.js already settled. It only decides what the readout SAYS
 * between `from` and `to`, and skip() puts it on `to` at once (Back, suspend, a newer bank).
 * Law VI: reduced motion takes the STATE, not a faster flight: no tokens, the readout is on `to` with a
 * declared lit look for a moment, and the cue still plays. Driven by rAF on performance.now, not CSS.
 *
 * TRAP: the run's `step()` reports `tokens: []` the moment nothing is in the air any more - including the
 * very frame the LAST one lands. The elements have to come down on that frame or a six-second jackpot
 * rollup leaves a stale token sitting where it landed for the whole climb. `draw()` is where that is kept. */

import { createBankRun, tokenAt, BANK } from '../../shared/win/bank.js';

const ARC_PX = BANK.ARC_PX;   // how far the path bows above the straight line

export function createBank({ layer, reduced, onTick, onLand, onDone }) {
  let run = null, raf = 0;

  /** The engine's events, in its order, played onto the station's callbacks. `tail` says the tick came from
   *  the rollup's own count-up rather than a token: no pop, no second gesture (Law X). */
  function play(events) {
    for (const e of events) {
      if (e.type === 'tick') onTick(e.value, e.kind, !!e.tail);
      else if (e.type === 'land') onLand(e.kind, !!e.counting);
      else if (e.type === 'done') finish();
    }
  }

  function dropTokens(r) {
    for (let i = 0; i < r.els.length; i += 1) { if (r.els[i]) { r.els[i].remove(); r.els[i] = null; } }
  }

  /** This frame's tokens. Anything the engine has stopped reporting is down, and comes off at once. */
  function draw(tokens, ms) {
    const r = run;
    if (!r) return;
    if (!tokens.length) { dropTokens(r); return; }
    const from = r.from(), to = r.to();
    for (const t of tokens) {
      const el = r.els[t.i];
      if (!el) continue;
      if (t.state === 'landed') { el.remove(); r.els[t.i] = null; continue; }
      const p = tokenAt(t.i, ms, from, to, { arc: ARC_PX });
      el.style.opacity = String(p.opacity);
      if (p.opacity) el.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px) scale(${p.scale.toFixed(3)})`;
    }
  }

  function frame(now) {
    raf = 0;
    const r = run;
    if (!r) return;
    const s = r.run.step(now);
    draw(s.tokens, now - r.start);   // the picture first: a 'done' in the events tears the layer down under it
    play(s.events);
    if (run === r && !s.done) raf = requestAnimationFrame(frame);
  }

  function finish() {
    const r = run;
    run = null;
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    if (!r) return;
    dropTokens(r);
    if (onDone) onDone(r.run.kind);
  }

  return {
    get busy() { return !!run; },
    get kind() { return run ? run.run.kind : null; },
    /** kind 'pay' | 'spend'. from()/to() give client px each frame (the cabinet may still be moving).
     *  `rollupMs` (playbook A3) is how long the READOUT counts; it never changes the token count or their
     *  flight, only how far past it the count carries on. 0 or less keeps the old flat count-up. */
    start({ kind, n, fromValue, toValue, from, to, rollupMs = 0 }) {
      if (run) {
        // Brake 2: one hero per beat. A pay landing inside a running pay merges into it (the tokens still in
        // the air are re-aimed and `settled` moves with them); anything else settles the running one first.
        // The engine answers null for a merge onto a spend or a finished run, so this asks rather than tells.
        if (kind === 'pay' && run.run.merge(toValue) === 'merged') return 'merged';
        this.skip();
      }
      // Law VI: reduced motion is the settled STATE, so there is no run to keep and nothing to fly. One
      // step() hands back the whole of it at once - the value, the mini-thud, the tear-down.
      if (reduced) {
        const solo = createBankRun({ kind, n, fromValue, toValue, rollupMs, reduced: true });
        play(solo.step(0).events.filter(e => e.type !== 'done'));   // nothing was built, so nothing is torn down
        if (onDone) onDone(solo.kind);
        return 'state';
      }
      const start = performance.now();
      const origin = layer.getBoundingClientRect();
      const els = Array.from({ length: n }, () => {
        const el = document.createElement('i');
        el.className = `slot-token slot-token-${kind}`;
        el.textContent = '✦';
        el.style.opacity = '0';
        layer.append(el);
        return el;
      });
      // The layer is the frame the tokens are drawn in; from()/to() are measured in client px every frame,
      // because the cabinet may still be moving under them, and come back into it here.
      const rel = f => () => { const p = f() || { x: origin.width / 2, y: origin.height / 2 }; return { x: p.x - origin.left, y: p.y - origin.top }; };
      run = { els, from: rel(from), to: rel(to), start,
              run: createBankRun({ kind, n, fromValue, toValue, rollupMs, startMs: start }) };
      raf = requestAnimationFrame(frame);
      return 'flying';
    },
    /** Settle at once: the readout goes straight to the settled value, no travel, no cue (Law VI, Brake 7).
     *  The engine reads `settled`, never the tick ladder, so a rollup cut short can neither leave the readout
     *  short nor count a value twice (Law I). `land` takes the whole settled STATE, mini-thud and +N
     *  included: what a lever press, a new spin or reduced motion want. Back and suspend leave quietly. */
    skip({ land = false } = {}) {
      if (!run) return;
      play(run.run.skip({ land }));
    },
    dispose() { const r = run; run = null; if (raf) cancelAnimationFrame(raf); raf = 0; if (r) dropTokens(r); },
  };
}
