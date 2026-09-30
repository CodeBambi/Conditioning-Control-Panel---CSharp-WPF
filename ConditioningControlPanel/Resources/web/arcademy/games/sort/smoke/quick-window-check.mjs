/* ============================================================================
 * games/sort/smoke/quick-window-check.mjs - THE QUICK WINDOW (ccp-bugs #1291).
 *
 *   node games/sort/smoke/quick-window-check.mjs   (from Resources/web/arcademy; 0 on pass)
 *
 * Mort's ask: the gold arc pays at the END of a ring, so give the player who
 * knows the card on sight a second way into the same bonus - the first half
 * second after the ring arms. This drives the REAL chain.js and holds it to:
 *
 *   - a swipe inside QUICK_MS reads PERFECT (and QUICK), on every rung;
 *   - the window shuts before the ALMOST band opens, so no swipe is ever both;
 *   - the late gold arc, JUST and ALMOST read exactly as they did before;
 *   - quick:false (a gesture made before the ring armed) never earns it;
 *   - a closed ring is never QUICK.
 * ==========================================================================*/

import { CHAIN, verdictFor, quickUntil, ringMsFor } from '../chain.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

ok(CHAIN.QUICK_MS === 500, 'QUICK_MS is half a second');

for (let rung = 0; rung <= CHAIN.MAX_RUNG; rung++) {
  const ms = ringMsFor(rung);
  const shut = quickUntil(ms);
  const almostOpens = ms * (1 - CHAIN.PERFECT_FRAC - CHAIN.ALMOST_FRAC);
  ok(shut <= almostOpens && shut > 0, `rung ${rung}: quick shuts at ${shut}ms, before ALMOST at ${almostOpens}ms`);

  const first = verdictFor(0, ms);
  ok(first.perfect && first.quick && first.verdict === 'quick', `rung ${rung}: elapsed 0 is a QUICK perfect`);
  const inside = verdictFor(shut - 1, ms);
  ok(inside.perfect && inside.quick, `rung ${rung}: ${shut - 1}ms is still QUICK`);
  const after = verdictFor(shut, ms);
  ok(!after.quick && !after.perfect, `rung ${rung}: ${shut}ms is plain early`);

  // every ms of the ring: no swipe is both QUICK and ALMOST, and none is QUICK in the gold arc
  let clash = 0;
  for (let t = 0; t <= ms + 10; t += 5) {
    const v = verdictFor(t, ms);
    if (v.quick && (v.almost || v.just || v.closed)) clash++;
    if (v.quick && t >= ms * (1 - CHAIN.PERFECT_FRAC)) clash++;
  }
  ok(clash === 0, `rung ${rung}: QUICK never overlaps ALMOST, JUST, the gold arc or a closed ring`);

  const late = verdictFor(ms * (1 - CHAIN.PERFECT_FRAC) + 1, ms);
  ok(late.perfect && !late.quick && late.verdict === 'perfect', `rung ${rung}: the gold arc still reads PERFECT`);
  const just = verdictFor(ms * (1 - CHAIN.JUST_FRAC) + 1, ms);
  ok(just.perfect && just.just && just.verdict === 'just', `rung ${rung}: JUST is unchanged`);
  const almost = verdictFor(almostOpens + 1, ms);
  ok(almost.almost && !almost.perfect, `rung ${rung}: ALMOST is unchanged`);

  const pre = verdictFor(0, ms, { quick: false });
  ok(!pre.quick && !pre.perfect && pre.verdict === 'early', `rung ${rung}: a pre-armed gesture is never QUICK`);
}

ok(!verdictFor(99999, 2400).quick, 'a closed ring is never QUICK');

if (fails) { console.error(fails + ' failure(s)'); process.exit(1); }
console.log('quick window: all green');
