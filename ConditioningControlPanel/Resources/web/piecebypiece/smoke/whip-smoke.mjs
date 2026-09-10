/* ============================================================================
 * smoke/whip-smoke.mjs - node checks for the bishop's whip timeline.
 * No browser and no dependencies: run it with
 *
 *   node smoke/whip-smoke.mjs
 *
 * Pins the shape of board/whip.js: the wind draws back and only back, the
 * crack lands inside the snap with the tip past the victim, the stride starts
 * at the crack and stands the bishop on his square inside the 440 line
 * (PBP-BEATS beat 3), the ring dies out, the shiver is bounded and short, the
 * stages come in order. Exits non-zero on the first failure.
 * ==========================================================================*/

import { WHIP_TUNING as W, whipBend, whipTimes, shiverAt, whipStandSec, whipTotalSec } from '../board/whip.js';

let passed = 0;
function ok(cond, what) {
  if (!cond) { console.log('FAIL ' + what); process.exit(1); }
  passed++;
}

const k = whipTimes(W);

// The wind: negative, and never going the other way before the snap.
let prev = 0;
for (let t = 0; t < k.wind; t += 0.002) {
  const { bend, stage } = whipBend(t);
  ok(stage === 'wind', 'stage is wind at ' + t.toFixed(3));
  ok(bend <= 0 && bend <= prev + 1e-9, 'wind only draws back at ' + t.toFixed(3));
  prev = bend;
}
ok(Math.abs(whipBend(k.wind - 1e-6).bend + W.windAmp) < 0.01, 'the wind reaches windAmp');

// The snap: from drawn-back to past the victim, crossing zero near crackAt.
let crossed = null;
let peak = -Infinity;
for (let t = k.wind; t < k.snap; t += 0.0005) {
  const { bend, stage } = whipBend(t);
  ok(stage === 'snap', 'stage is snap at ' + t.toFixed(3));
  if (crossed == null && bend >= 0) crossed = t;
  peak = Math.max(peak, bend);
}
ok(crossed != null, 'the tip crosses the victim');
ok(Math.abs(crossed - k.crack) < 0.01, 'the crossing is at the crack (' + crossed.toFixed(3) + ' vs ' + k.crack.toFixed(3) + ')');
ok(Math.abs(peak - W.snapAmp) < 0.01, 'the snap reaches snapAmp');

// The ring: decays, and is quiet by its end.
const ringEnd = whipBend(k.ring - 1e-6);
ok(ringEnd.stage === 'ring', 'ring stage runs to its end');
ok(Math.abs(ringEnd.bend) < 0.03, 'the ring has died out by its end (' + ringEnd.bend.toFixed(3) + ')');
ok(whipBend(k.done + 0.01).stage === 'done' && whipBend(k.done + 0.01).bend === 0, 'then done, and still');

// The 440 line, and the order of things.
ok(k.step === k.crack, 'the stride starts at the crack');
ok(k.stand === k.crack + W.stepSec, 'the bishop stands one stride after the crack');
ok(whipStandSec() <= 0.44, 'the bishop is on his square inside the 440 line (' + (whipStandSec() * 1000).toFixed(0) + ' ms)');
ok(k.wind < k.crack && k.crack < k.snap && k.snap < k.ring, 'stages are in order');
ok(whipTotalSec() < 1.0, 'the tentacle is still under a second (' + whipTotalSec().toFixed(3) + ')');
ok(W.approachSec + k.crack + W.tipSec <= whipStandSec() + 0.001, 'the victim hits the board no later than the bishop lands');
ok(W.approachSec + k.crack + W.tipSec + W.rollSec + 0.7 + 0.55 <= 2.11, 'the victim is on the rim inside the 2110 ceiling');
ok(whipBend(-1).bend === 0, 'before the landing there is no bend');

// The shiver: short, small, gone.
ok(shiverAt(-0.01) === 0 && shiverAt(W.shiverSec) === 0, 'the shiver is zero outside its window');
let peakShiver = 0;
for (let u = 0; u < W.shiverSec; u += 0.001) peakShiver = Math.max(peakShiver, Math.abs(shiverAt(u)));
ok(peakShiver <= W.shiverAmp + 1e-9 && peakShiver > W.shiverAmp * 0.5, 'the shiver stays within shiverAmp');
ok(W.shiverSec <= 0.25 + 1e-9, 'the shiver is 250 ms or less');

console.log(passed + ' checks passed');
console.log('beats (from the move): stand-off land ' + (W.approachSec * 1000).toFixed(0) + ' | crack ' + ((W.approachSec + k.crack) * 1000).toFixed(0)
  + ' | on the square ' + (whipStandSec() * 1000).toFixed(0) + ' | tentacle still ' + (whipTotalSec() * 1000).toFixed(0)
  + ' | victim down ' + ((W.approachSec + k.crack + W.tipSec) * 1000).toFixed(0) + ' | rolled ' + ((W.approachSec + k.crack + W.tipSec + W.rollSec) * 1000).toFixed(0) + ' ms');
