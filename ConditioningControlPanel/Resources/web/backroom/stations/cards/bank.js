/* ============================================================================
 * stations/cards/bank.js - THE BANK at the card table (Law XII), lane
 * BR2-rw-cards. A winning hand used to pay by having the SP number change;
 * from here the pay LEAVES THE POT and flies to the room's chip.
 *
 * The maths, the clock, the tick ladder, the rollup tail, the merge and the
 * skip are all shared/win/bank.js and are not repeated here (the slot's and the
 * wheel's own bank.js each carry a copy; this one does not). What lives in this
 * file is only what the engine refuses to own: the elements, the layer, the
 * rAF loop and the client-px measuring, because the token class, the layer and
 * the two endpoints are this station's and nobody else's.
 *
 *   from()   the pot: the bet spot on the felt, measured every frame (the room
 *            camera is still moving while a seated hand settles)
 *   to()     ctx.spReadout.target(), the room's own SP chip
 *
 *   Law VI   skip() settles at once - Back, suspend, close, a new deal. Under
 *            reduced motion the run never flies at all: the engine's 'state'
 *            mode hands back the settled value and the cue on the first step.
 *   Law X    the readout ticks as each token LANDS (onTick), and the mini-thud
 *            waits for the END of the rollup's count (onLand's `counting`).
 *   Brake 2  a second pay inside a running one MERGES; it never stacks.
 * ==========================================================================*/

import { createBankRun, tokenAt } from '../../shared/win/bank.js';

/**
 * createCardsBank({ layer, onTick, onLand, onDone })
 *   layer   the absolutely-positioned element the tokens live in (.cards-tokens)
 *   onTick  (value, kind, tail) the readout now says `value`; `tail` = it came from the count-up, not a token
 *   onLand  (kind, counting)    the tokens are down; `counting` = still counting, so hold the mini-thud
 *   onDone  (kind)              the move is over
 */
export function createCardsBank({ layer, onTick, onLand, onDone }) {
  let run = null, raf = 0, els = [], startMs = 0, origin = null, at = null, lastKind = null;

  /** Client px -> the layer's own box. The layer is inset 0 on the station root, so one measure per run is
   *  enough; the endpoints themselves are read every frame. */
  const rel = (p) => {
    const o = origin || { left: 0, top: 0, width: 0, height: 0 };
    const q = p || { x: o.left + o.width / 2, y: o.top + o.height / 2 };
    return { x: q.x - o.left, y: q.y - o.top };
  };

  function teardown() {
    for (const el of els) el.remove();
    els = []; run = null; at = null;
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
  }

  /** The engine's events, in the order it gave them. `done` tears the tokens down before onDone is told. */
  function play(events) {
    for (const e of events) {
      if (e.type === 'tick') { if (onTick) onTick(e.value, e.kind, !!e.tail); }
      else if (e.type === 'land') { if (onLand) onLand(e.kind, !!e.counting); }
      else if (e.type === 'done') { teardown(); if (onDone) onDone(e.kind); }
    }
  }

  function frame(now) {
    raf = 0;
    const r = run;
    if (!r) return;
    const out = r.step(now);
    // Drawn BEFORE the events are played: a 'done' takes the elements away, and a token drawn after that is
    // a token nobody owns.
    if (out.tokens.length && at) {
      const from = rel(at.from()), to = rel(at.to()), ms = now - startMs;
      for (const tok of out.tokens) {
        const el = els[tok.i];
        if (!el) continue;
        const p = tokenAt(tok.i, ms, from, to);
        el.style.opacity = String(p.opacity);
        if (p.opacity > 0) el.style.transform = `translate(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px) scale(${p.scale.toFixed(3)})`;
      }
    }
    play(out.events);
    if (run) raf = requestAnimationFrame(frame);
  }

  return {
    get busy() { return !!run; },
    get kind() { return run ? run.kind : lastKind; },
    /** The value the run will finish on, whatever happens to the ladder in between (Law I). */
    get settled() { return run ? run.settled : null; },

    /**
     * One pay. `from()` / `to()` give client px each frame. `rollupMs` is `plan.partyMs`: how long the
     * READOUT keeps counting past the landing, never how many tokens fly or how long they take.
     * -> 'merged' | 'state' | 'flying'
     */
    pay({ n, fromValue, toValue, from, to, rollupMs = 0, reduced = false }) {
      if (run && run.merge(toValue) === 'merged') return 'merged';   // Brake 2: one party, re-aimed
      if (run) this.skip({ land: true });
      lastKind = 'pay';
      at = { from, to };
      startMs = performance.now();
      run = createBankRun({ kind: 'pay', n, fromValue, toValue, rollupMs, reduced, startMs });
      if (run.mode === 'state') { play(run.step(startMs)); return 'state'; }   // Law VI: the STATE, no travel
      origin = layer.getBoundingClientRect();
      els = Array.from({ length: run.n }, () => {
        const el = document.createElement('i');
        el.className = 'cards-token';
        el.style.opacity = '0';
        layer.append(el);
        return el;
      });
      raf = requestAnimationFrame(frame);
      return 'flying';
    },

    /** Law VI: the readout goes straight to the settled value, never to the last rung of the tick ladder.
     *  `land` takes the whole settled state, the mini-thud included (a new deal, reduced motion); Back,
     *  suspend and close leave quietly. */
    skip({ land = false } = {}) {
      if (!run) return;
      play(run.skip({ land }));
      teardown();
    },
    dispose() { teardown(); },
  };
}
