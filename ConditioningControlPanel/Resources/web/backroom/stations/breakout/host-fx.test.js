/* node --test host-fx.test.js - the host effect moments, throttles and the no-host case. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHostFx, GAP_S } from './host-fx.js';

function make(opts = {}) {
  const sent = [];
  let t = 0;
  const fx = (id, symbols, args) => { sent.push({ id, symbols, args }); return Promise.resolve({ fired: [] }); };
  const host = createHostFx({ fx, clock: () => t, ...opts });
  return { host, sent, tick: (s) => { t += s; } };
}

test('every moment maps to a room primitive with the picture key in symbols', () => {
  const { host, sent, tick } = make();
  assert.equal(host.wall(1, 'g3'), true);
  assert.deepEqual(sent[0], { id: 'fx.wash', symbols: ['g3'], args: { color: '#ff5fa2', strength: 0.55 } });
  tick(30);
  host.breakout('g0');
  assert.equal(sent.at(-1).args.strength, 1);
  host.jackpot('g1');
  assert.deepEqual(sent.at(-1), { id: 'fx.gif_from', symbols: ['g1'], args: { ms: 1600, scale: 0.6 } });
  tick(30);
  host.shatterWall({ x: 10.4, y: 20, w: 300, h: 450.6 }, 'g2');
  assert.deepEqual(sent.at(-1).args, { from: { x: 10, y: 20, w: 300, h: 451 }, ms: 1500, scale: 0.8 });
  host.mantra('s2');
  assert.deepEqual(sent.at(-1), { id: 'fx.sub_pair', symbols: ['s2'], args: { wordsShown: true } });
  tick(30);
  host.crack();
  assert.deepEqual(sent.slice(-2).map(x => x.id), ['fx.loom_spiral', 'fx.wash']);
  assert.equal(sent.at(-2).args.preset, 'screen');
});

test('a bad box grows from the centre and a missing key sends no symbols', () => {
  const { host, sent } = make();
  host.shatterWall(null, null);
  assert.deepEqual(sent[0].symbols, []);
  assert.equal(sent[0].args.from, undefined);
});

test('each id has its own gap: a second wash inside the gap is dropped, a gif_from is not', () => {
  const { host, sent, tick } = make();
  assert.equal(host.wall(1, 'g0'), true);
  assert.equal(host.wall(2, 'g1'), false, 'inside the wash gap');
  assert.equal(host.jackpot('g0'), true, 'another id is free');
  tick(GAP_S['fx.wash']);
  assert.equal(host.wall(3, 'g2'), true);
  assert.equal(sent.length, 3);
});

test('reduced motion halves washes and skips the spiral; no host means no calls and no throw', () => {
  const { host, sent } = make({ reduced: true });
  host.crack();
  assert.deepEqual(sent.map(x => x.id), ['fx.wash']);
  assert.equal(sent[0].args.strength, 0.45);
  const none = createHostFx({ fx: null });
  assert.equal(none.wall(1, 'g0'), false);
  assert.equal(none.crack(), false);
  const thrower = createHostFx({ fx: () => { throw new Error('host gone'); } });
  assert.equal(thrower.breakout('g0'), false);
});
