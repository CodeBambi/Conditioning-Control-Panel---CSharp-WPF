// Self-contained pass over the juice maths (ui/juice.js) and the node-safety of
// its DOM half (ui/juiceDom.js).
//
//   node Resources/web/goon/test/selftest-juice.js
//
// What is asserted:
//   1. Easing curves start at 0, end at 1; outBack and THUD overshoot, cubics do not.
//   2. cubicBezier matches the linear curve and the house THUD numbers.
//   3. Count-ups land exactly, never overshoot, and scale their length with the jump.
//   4. The spring converges and survives a huge dt.
//   5. Keyframe builders: pop/squash/shake return to rest, calm is opacity only.
//   6. Stagger never exceeds its ceiling; bursts respect tier and calm; seeded bursts repeat.
//   7. Arc points start and end on the endpoints and lift above both.
//   8. Pentatonic ladder: no semitone clash, octave wrap, gain inside 0.03..0.13.
//   9. juiceDom imports and every helper is a no-op without a DOM.
//  10. The router still swaps instantly when the section has no WAAPI (node, stubs).

import * as J from '../ui/juice.js';
import * as D from '../ui/juiceDom.js';

let n = 0;
let failures = 0;
function ok(cond, what, detail) {
  n++;
  if (!cond) { failures++; console.error('FAIL: ' + what + (detail !== undefined ? ' (' + detail + ')' : '')); }
}
const near = (a, b, eps = 1e-3) => Math.abs(a - b) < eps;

// 1. easing
for (const [name, f] of Object.entries(J.ease)) {
  ok(near(f(0), 0), name + '(0) = 0', f(0));
  ok(near(f(1), 1), name + '(1) = 1', f(1));
  ok(near(f(-3), f(0)) && near(f(7), f(1)), name + ' clamps its input');
}
let backMax = 0;
for (let t = 0; t <= 1; t += 0.01) backMax = Math.max(backMax, J.ease.outBack(t));
ok(backMax > 1.05, 'outBack overshoots', backMax);
let cubicMax = 0;
for (let t = 0; t <= 1; t += 0.01) cubicMax = Math.max(cubicMax, J.ease.outCubic(t));
ok(cubicMax <= 1 + 1e-9, 'outCubic never overshoots', cubicMax);

// 2. bezier
const lin = J.cubicBezier(0, 0, 1, 1);
ok(near(lin(0.3), 0.3) && near(lin(0.77), 0.77), 'cubicBezier(0,0,1,1) is linear');
let thudMax = 0;
for (let t = 0; t <= 1; t += 0.01) thudMax = Math.max(thudMax, J.thud(t));
ok(thudMax > 1.05 && thudMax < 1.2, 'THUD overshoots by a readable amount', thudMax);
ok(near(J.thud(1), 1) && near(J.thud(0), 0), 'THUD lands on 0 and 1');
ok(J.THUD_MS === 340 && J.SHIVER_MS === 250 && J.REVEAL_MS === 620, 'house timings are the house timings');
ok(J.ENTER_MS >= 150 && J.ENTER_MS <= 350 && J.EXIT_MS >= 120 && J.EXIT_MS <= 250, 'enter/exit inside the brief band');

// 3. count up
ok(J.countUpValue(0, 100, 0, 600) === 0, 'count-up starts at from');
ok(J.countUpValue(0, 100, 600, 600) === 100, 'count-up lands on to');
ok(J.countUpValue(0, 100, 9999, 600) === 100, 'count-up holds after the end');
let prev = -1, mono = true, over = false;
for (let e = 0; e <= 600; e += 7) {
  const v = J.countUpValue(0, 100, e, 600);
  if (v < prev) mono = false;
  if (v > 100) over = true;
  prev = v;
}
ok(mono && !over, 'count-up is monotonic and never overshoots');
ok(J.countUpValue(50, 10, 600, 600) === 10, 'count-down works too');
ok(J.countUpMs(0, 1) < J.countUpMs(0, 80), 'a bigger jump counts longer');
ok(J.countUpMs(0, 1e6) <= 1100, 'a huge jump is capped', J.countUpMs(0, 1e6));
ok(J.countUpMs(0, 0) >= 260, 'the floor holds for a zero jump');

// 4. spring
let st = { x: 0, v: 0 };
for (let i = 0; i < 240; i++) st = J.springStep(st, 1, 1 / 60);
ok(J.springSettled(st, 1), 'spring settles on its target within 4 s', JSON.stringify(st));
const big = J.springStep({ x: 0, v: 0 }, 1, 5);
ok(Number.isFinite(big.x) && Math.abs(big.x) < 2, 'a slept tab (dt 5 s) does not launch the spring', big.x);

// 5. frames
const pin = J.popInFrames();
ok(pin[0].opacity === 0 && pin[pin.length - 1].transform === 'scale(1)', 'pop-in ends at rest');
ok(J.popInFrames({ calm: true }).every((f) => !('transform' in f)), 'calm pop-in is opacity only');
ok(J.popOutFrames({ calm: true }).every((f) => !('transform' in f)), 'calm pop-out is opacity only');
ok(J.popOutFrames({ dx: 5, dy: -9 })[1].opacity === 0, 'pop-out ends invisible');
const sq = J.squashFrames(0.12);
ok(sq[0].transform === 'scale(1, 1)' && sq[sq.length - 1].transform === 'scale(1, 1)', 'squash starts and ends at rest');
ok(J.squashFrames(9).some((f) => f.transform === 'scale(1.3, 0.7)'), 'squash amount is clamped to 0.3');
const sh = J.shakeFrames(4);
ok(sh[0].transform === 'translate(0px, 0px)' && sh[sh.length - 1].transform === 'translate(0px, 0px)', 'shake returns home');
ok(sh[1].transform.startsWith('translate(4px'), 'first swing is the amplitude in real px', sh[1].transform);
ok(J.shakeFrames(999)[1].transform.startsWith('translate(16px'), 'shake is capped at 16 px');

// 6. stagger, bursts
const sd = J.staggerDelays(30, { step: 45, max: 360 });
ok(sd.length === 30 && sd[0] === 0 && sd[29] <= 360, 'a long cascade stays under its ceiling', sd[29]);
ok(J.staggerDelays(3).join(',') === '0,45,90', 'a short cascade uses the full step');
ok(J.staggerDelays(0).length === 0 && J.staggerDelays(1)[0] === 0, 'stagger edge counts');
ok(J.burstCount(14, { calm: true }) === 0, 'calm spawns no particles');
ok(J.burstCount(14, { lite: true }) === 6 && J.burstCount(4, { lite: true }) >= 1, 'lite tier gets fewer, never none');
ok(J.burstCount(500) <= 32, 'a burst is capped');
const b1 = J.burstParticles(10, { rnd: J.seededRng(7) });
const b2 = J.burstParticles(10, { rnd: J.seededRng(7) });
ok(JSON.stringify(b1) === JSON.stringify(b2), 'a seeded burst repeats exactly');
ok(b1.length === 10 && b1.every((p) => Math.hypot(p.dx, p.dy) >= 89 && p.ms > 0), 'particles fly at least their distance');
const angles = b1.map((p) => Math.atan2(p.dy, p.dx));
const quadrants = new Set(angles.map((a) => Math.floor(((a + Math.PI) / (Math.PI / 2))) % 4));
ok(quadrants.size === 4, 'a full burst covers every quadrant');
const cone = J.burstParticles(8, { rnd: J.seededRng(3), arc: Math.PI / 2, bias: -Math.PI / 2 });
ok(cone.every((p) => p.dy < 0), 'a narrowed upward cone only goes up');

// 7. arcs
const pts = J.arcPoints({ x: 0, y: 500 }, { x: 800, y: 300 }, { steps: 10 });
ok(pts.length === 11, 'arc has steps + 1 points');
ok(pts[0].x === 0 && pts[0].y === 500 && pts[10].x === 800 && pts[10].y === 300, 'arc starts and ends on its endpoints');
ok(Math.min(...pts.map((p) => p.y)) < 300, 'arc lifts above both endpoints');
ok(pts[10].scale < pts[0].scale, 'a thrown thing shrinks into the distance');

// 8. pitch
const semis = Array.from({ length: 10 }, (_, i) => J.pentaSemis(i));
ok(semis.join(',') === '0,2,4,7,9,12,14,16,19,21', 'pentatonic ladder wraps by the octave', semis.join(','));
let clash = false;
for (const a of J.PENTA) for (const b of J.PENTA) if (Math.abs(a - b) === 1) clash = true;
ok(!clash, 'no semitone inside the ladder');
ok(near(J.pentaHz(0), 523.2511, 1e-3) && near(J.pentaHz(5), 1046.5022, 1e-3), 'C5 root, C6 one octave up');
for (let i = 0; i < 20; i++) {
  const g = J.rungGain(i);
  if (!(g >= 0.03 && g <= 0.13)) ok(false, 'rung gain inside the house band', i + ':' + g);
}
ok(J.rungGain(8) < J.rungGain(0), 'gain tilts down as the ladder climbs');

// 9. DOM half is inert under node
ok(D.isCalm() === false && D.isLite() === false, 'no DOM: not calm, not lite');
ok(D.play(null, [], {}) === null && D.play({}, [], {}) === null, 'play() without WAAPI is null, never a throw');
ok(D.burst(10, 10) === 0, 'burst without a DOM spawns nothing');
ok(D.juiceLayer() === null, 'no layer without a body');
let wrote = null;
const stop = D.countUp({ set textContent(v) { wrote = v; } }, 0, 42, { ms: 500 });
ok(wrote === '42' && typeof stop === 'function', 'count-up without rAF lands the final number at once', wrote);
let resolved = false;
await D.flyArc(null, null, null).then(() => { resolved = true; });
ok(resolved, 'flyArc with nothing to fly resolves');
await D.popOut(null, { ms: 1 });
ok(true, 'popOut on nothing resolves');

// 10. router: a section without WAAPI swaps instantly (the self-tests' stub DOM)
{
  const sections = new Map();
  const mk = (id) => ({ id, hidden: true, children: [], scrollTop: 0, replaceChildren() { this.children = []; }, getAttribute() { return null; } });
  const { SCREEN_IDS, createRouter } = await import('../ui/router.js');
  for (const id of Object.values(SCREEN_IDS)) sections.set(id, mk(id));
  globalThis.document = {
    getElementById: (id) => sections.get(id) || null,
    documentElement: { setAttribute() {}, getAttribute() { return null; } },
  };
  let mounted = 0;
  const scr = { mount() { mounted++; return { unmount() {} }; } };
  const r = createRouter({ screens: { title: scr, lobby: scr } });
  r.show('title');
  r.show('lobby');
  ok(sections.get('scr-title').hidden === true && sections.get('scr-lobby').hidden === false,
    'stub DOM: the old screen is hidden the moment the new one shows');
  r.hide();
  ok(sections.get('scr-lobby').hidden === true, 'stub DOM: hide() hides at once');
  ok(mounted === 2 && r.current === null, 'router mounted twice and is empty after hide');
  r.dispose();
  delete globalThis.document;
}

if (failures) {
  console.error('selftest-juice: ' + (n - failures) + '/' + n + ' checks passed ' + failures + ' FAILURE(S)');
  process.exitCode = 1;
} else {
  console.log('selftest-juice: ' + n + '/' + n + ' checks passed');
}
