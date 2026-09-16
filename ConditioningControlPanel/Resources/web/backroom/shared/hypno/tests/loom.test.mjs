/* shared/hypno/loom.js, the parts that do not need a GPU: presets, the angle -> phase rule, backing sizes. */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizeParams2, loopMs2 } from '../../../../arcademy/engine/loom/loomField.js';
import { LOOM_PRESETS, LOOM_BACKING, phaseForAngle, phaseAt, spanRad, backingFor, createLoomKit } from '../index.js';

const NAMES = ['backs', 'hub', 'whirl', 'wake', 'screen', 'candy', 'pinwheel', 'ribbon', 'mint', 'star'];
const TAU = Math.PI * 2;

test('ten presets, frozen, already normalised by the real loomField', () => {
  assert.deepEqual(Object.keys(LOOM_PRESETS).sort(), [...NAMES].sort());
  assert.ok(Object.isFrozen(LOOM_PRESETS));
  for (const n of NAMES) {
    const q = LOOM_PRESETS[n];
    assert.ok(Object.isFrozen(q) && Object.isFrozen(q.layer) && Object.isFrozen(q.layer.colors), n + ' deep frozen');
    assert.deepEqual(normalizeParams2(q), JSON.parse(JSON.stringify(q)), n + ' is a fixed point of normalizeParams2');
    assert.equal(q.layer.direction, 1, n + ' layer 1 pulls inward (direction 1)');
  }
  assert.deepEqual(LOOM_BACKING, { long: 512, small: 256 });
  assert.ok(Object.isFrozen(LOOM_BACKING));
});

test('the presets are the mockup values from CONTRACT 10.13.D', () => {
  const pick = (l) => [l.arms, l.turns, Math.round(l.duty * 100) / 100, l.style, l.direction, l.colors.join(' '), l.bandMode];
  const { backs, hub, whirl, wake, screen } = LOOM_PRESETS;
  assert.deepEqual(pick(backs.layer), [4, 2, 0.5, 'log', 1, '#ff69b4 #8a5cff', 'hard']);
  assert.deepEqual([backs.bg.kind, backs.bg.color, backs.glow, backs.speed], ['solid', '#14060f', 0.25, 2]);
  assert.deepEqual(pick(hub.layer), [3, 1.5, 0.55, 'golden', 1, '#e8c27a #ff5fa2 #9b6bff', 'hard']);
  assert.deepEqual([hub.bg.color, hub.glow, hub.speed], ['#1a0f2b', 0.35, 3]);
  assert.deepEqual(pick(whirl.layer), [2, 2.5, 0.45, 'ribbon', 1, '#5fffd0 #9b6bff', 'hard']);
  assert.deepEqual([whirl.bg.color, whirl.glow, whirl.wobble.amp, whirl.wobble.freq, whirl.wobble.cycles], ['#1c1230', 0.4, 0.12, 3, 1]);
  assert.deepEqual(pick(wake.layer), [4, 3, 0.5, 'log', 1, '#5fffd0 #3a1f5c', 'hard']);
  assert.deepEqual([wake.bg.color, wake.glow, wake.wobble.amp, wake.wobble.freq, wake.wobble.cycles, wake.speed], ['#0a0614', 0.45, 0.15, 2, 1, 2]);
  assert.deepEqual(pick(screen.layer), [6, 2.5, 0.5, 'log', 1, '#ff5fa2 #9b6bff #5fffd0', 'gradient']);
  assert.deepEqual([screen.layer2.enabled, ...pick(screen.layer2)], [true, 3, 1.5, 0.3, 'log', 1, '#e8c27a', 'hard']);
  assert.deepEqual([screen.bg.kind, screen.bg.color, screen.bg.outer, screen.glow, screen.pulse.amp, screen.pulse.cycles, screen.speed],
    ['radial', '#14060f', '#08040e', 0.5, 0.08, 1, 1]);
  for (const n of ['backs', 'hub', 'whirl', 'wake']) assert.equal(LOOM_PRESETS[n].layer2.enabled, false, n + ' has one layer');
});

test('phaseForAngle gives the phase whose layer-1 rotation is the angle, modulo the symmetry span', () => {
  for (const n of NAMES) {
    const L = LOOM_PRESETS[n].layer, span = spanRad(L);
    for (const rad of [0, 0.3, 1, -0.7, 5.5, 40, -123.4]) {
      const p = phaseForAngle(n, rad);
      assert.ok(p >= 0 && p < 1, `${n} ${rad}: phase in [0,1)`);
      const rot = L.direction * p * span;                // loomField layerRotationRad
      const d = (((rot - rad) % span) + span) % span;
      assert.ok(d < 1e-9 || span - d < 1e-9, `${n} ${rad}: rotation ${rot} == angle mod ${span}`);
    }
    assert.ok(phaseForAngle(n, 0.05) > phaseForAngle(n, 0), n + ': turning clockwise grows the phase (direction 1)');
  }
  assert.equal(phaseForAngle('nope', 1), 0);
  assert.equal(phaseForAngle('hub', NaN), 0);
});

test('the span rule is loomField symmetrySpanRad: colours dividing the arms repeat by colours x arm span', () => {
  assert.equal(spanRad(LOOM_PRESETS.backs.layer), (TAU / 4) * 2);     // 4 arms, 2 colours
  assert.equal(spanRad(LOOM_PRESETS.hub.layer), TAU);                  // 3 arms, 3 colours
  assert.equal(spanRad(LOOM_PRESETS.whirl.layer), TAU);                // 2 arms, 2 colours
  assert.equal(spanRad(LOOM_PRESETS.screen.layer), (TAU / 6) * 3);    // 6 arms, 3 colours
  assert.equal(spanRad({ arms: 3, colors: ['#000000', '#ffffff'] }), TAU);   // odd arms, 2 colours: a full turn
});

test('phaseAt walks one loop in loopMs2', () => {
  for (const n of NAMES) {
    const loop = loopMs2(LOOM_PRESETS[n]);
    assert.equal(phaseAt(n, 0), 0);
    assert.ok(Math.abs(phaseAt(n, loop / 4) - 0.25) < 1e-9);
    assert.ok(Math.abs(phaseAt(n, loop * 3 + loop / 2) - 0.5) < 1e-9);
    assert.ok(phaseAt(n, -loop / 4) > 0.7);
  }
});

test('backingFor: long side capped at 512, aspect quantised to 0.05', () => {
  assert.deepEqual(backingFor(1920, 1080, 512), { w: 512, h: 284 });   // 1.777 -> 1.8
  assert.deepEqual(backingFor(60, 84, 256), { w: 179, h: 256 });        // 0.714 -> 0.7
  assert.deepEqual(backingFor(100, 100, 9999), { w: 512, h: 512 });
  assert.deepEqual(backingFor(0, 0, 256), { w: 256, h: 256 });
  const a = backingFor(59, 84, 256), b = backingFor(60, 84, 256);
  assert.deepEqual(a, b, 'a one-pixel wobble in a card size keeps the same store');
});

test('without a document the kit draws nothing and never throws', () => {
  const kit = createLoomKit({ still: true });
  assert.equal(kit.webgl, false);
  assert.equal(typeof Object.getOwnPropertyDescriptor(kit, 'webgl').get, 'function', 'webgl is live, not a value copied at creation');
  assert.equal(kit.draw({ drawImage() { throw new Error('no'); } }, 'hub', 0, 0, 10, 10), false);
  assert.equal(kit.paint({ width: 8, height: 8, getContext: () => null }, 'hub'), false);
  kit.setStill(false);
  kit.dispose();
  assert.equal(kit.draw({}, 'hub', 0, 0, 10, 10), false);
});
