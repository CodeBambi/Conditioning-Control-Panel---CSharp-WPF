/* shared/hypno/moments.js against the mock host: the MOMENTS table, gates (never dropping a step, 2026-09-15), holdScreen, holds, the tunnel throttle. */

import { test, mock } from 'node:test';
import assert from 'node:assert/strict';
import { MOMENTS, wheelSize, strengthK, wheelTurnLevel, rouletteRunLevel, pocketColor, boxAround, createMoments } from '../index.js';
import { createMockHost } from './mock-host.js';

const IDS = ['wheel.turn', 'wheel.land.quiet', 'wheel.land.flash', 'wheel.land.gif', 'wheel.land.jackpot',
  'cards.sit', 'cards.bloom', 'cards.win', 'cards.lose', 'cards.push',
  'cards.deal', 'cards.hit', 'cards.double', 'cards.split', 'cards.reveal', 'cards.bust', 'cards.dealer_bust', 'cards.streak', 'cards.sweep',
  'roulette.run', 'roulette.wake', 'roulette.land.miss', 'roulette.land.win', 'roulette.land.big'];
const RECT = { x: 612.4, y: 188, w: 60, h: 44 };
const calls = (host) => host.fx.map((r) => [r.fxId, r.symbols || null, r.args]);

test('cards table beats: light steps play under holdScreen, fullscreen ones wait; words ride as dealt keys; gates dress', () => {
  const host = createMockHost();
  const m = createMoments(host.ctx, { station: 'cards' });
  m.holdScreen(true);
  let r = m.play('cards.deal', { words: ['s2'] });
  assert.equal(r.held, false, 'a light-only moment is not held');
  r = m.play('cards.split', { words: ['s3', 's0', 'bad', 'g1'] });
  r = m.play('cards.double');
  r = m.play('cards.reveal');
  assert.deepEqual(calls(host), [['fx.sub_single', ['s2'], {}], ['fx.sub_single', ['s3', 's0'], {}], ['fx.gif_burst', null, {}], ['fx.gif_burst', null, {}]]);
  assert.deepEqual(m.play('cards.bust').page, []);
  host.clear();
  r = m.play('cards.streak', { gif: 'g4', words: ['s0', 's1'] });
  assert.equal(r.held, true, 'the streak is fullscreen: held while a decision is open');
  assert.equal(host.fx.length, 0);
  m.holdScreen(false);
  m.play('cards.streak', { gif: 'g4', words: ['s0', 's1'] });
  m.play('cards.dealer_bust', { gif: 'g7' });
  m.play('cards.sweep', { gif: 'g1' });
  assert.deepEqual(calls(host), [
    ['fx.sub_pair', ['s0', 's1'], {}], ['fx.wash', ['g4'], { color: '#5fffd0', strength: 0.9 }],
    ['fx.gif_burst', null, {}], ['fx.wash', ['g7'], { color: '#5fffd0', strength: 0.8 }],
    ['fx.gif_storm', null, {}], ['fx.wash', ['g1'], { color: '#e8c27a', strength: 1 }],
  ]);
  for (const id of ['cards.dealer_bust', 'cards.streak', 'cards.sweep']) assert.deepEqual(MOMENTS[id].page, ['win_tunnel', 'chip_vortex']);
  host.clear();
  host.settings({ gates: { flash: false, subliminal: false, spiral: true, brainDrain: true } });
  m.play('cards.hit', { words: ['s1'] }); m.play('cards.reveal'); m.play('cards.sweep', { gif: 'g1' });
  assert.equal(host.fx.length, 4, 'gates off drop nothing on the page: the whisper, the burst, the storm and the wash are all posted');
  assert.ok(host.fx.every((r) => r.ack.fired.length === 0 && r.ack.skipped[0].why === 'toggle'), 'and the host is the one that skips them, per toggle');
  m.dispose();
});

test('MOMENTS has exactly the ids of 10.13.F plus the cards table beats, frozen', () => {
  assert.deepEqual(Object.keys(MOMENTS).sort(), [...IDS].sort());
  assert.ok(Object.isFrozen(MOMENTS) && Object.isFrozen(MOMENTS['wheel.land.jackpot'].host[0].args));
});

test('wheelSize and the small helpers', () => {
  assert.equal(wheelSize({ jackpot: true, pay: 400 }), 'jackpot');
  assert.equal(wheelSize({ jackpotWon: true, pay: 400 }), 'jackpot', 'the wheel page reads jackpot as jackpotWon');
  assert.equal(wheelSize({ snoozed: true, pay: 0 }), 'quiet');
  for (const pay of [1, 2, 3]) assert.equal(wheelSize({ pay }), 'quiet');
  for (const pay of [5, 8, 12]) assert.equal(wheelSize({ pay }), 'flash');
  for (const pay of [20, 40, 100]) assert.equal(wheelSize({ pay }), 'gif');
  assert.equal(wheelSize({ pay: 40, jackpotFallback: true }), 'gif', 'Dazed from a gated jackpot is a gif landing');
  assert.equal(wheelSize(null), 'quiet');
  assert.equal(strengthK({ intensity: 'calm' }), 0.5);
  assert.equal(strengthK({ reduced: true, intensity: 'full' }), 0.5);
  assert.equal(strengthK({ intensity: 'full' }), 1);
  assert.equal(wheelTurnLevel(1), 0.85);
  assert.equal(rouletteRunLevel(0), 0.75 * 0.35);
  assert.equal(rouletteRunLevel(-12), 0.75);
  assert.equal(pocketColor(0, [1, 3]), '#5fffd0');
  assert.equal(pocketColor(3, [1, 3]), '#ff5fa2');
  assert.equal(pocketColor(4, [1, 3]), '#9b6bff');
  assert.deepEqual(boxAround(100, 50), { x: 70, y: 28, w: 60, h: 44 });
});

test('the wheel landings fire the table, Normal args, the caller colour, rect and key', () => {
  const host = createMockHost();
  const m = createMoments(host.ctx, { station: 'wheel' });
  assert.deepEqual(m.play('wheel.land.quiet'), { tokens: [], page: ['quiet_room'], held: false });
  let r = m.play('wheel.land.flash', { color: '#ff5fa2', from: RECT, gif: 'g1' });
  assert.equal(r.tokens.length, 1);
  assert.deepEqual(calls(host), [['fx.wash', null, { strength: 0.55, color: '#ff5fa2' }]]);
  host.clear();
  r = m.play('wheel.land.gif', { color: '#9b6bff', from: RECT, gif: 'g3' });
  assert.deepEqual(calls(host), [
    ['fx.wash', null, { strength: 0.9, color: '#9b6bff' }],
    ['fx.gif_from', ['g3'], { ms: 3400, from: { x: 612, y: 188, w: 60, h: 44 } }],
  ]);
  assert.deepEqual(r.tokens, host.fx.map((x) => x.token));
  host.clear();
  r = m.play('wheel.land.jackpot', { color: '#9b6bff', from: RECT, gif: 'g0' });
  assert.deepEqual(calls(host), [
    ['fx.loom_spiral', null, { preset: 'screen', ms: 4200, alpha: 0.9 }],
    ['fx.gif_from', ['g0'], { ms: 4600, scale: 0.46, from: { x: 612, y: 188, w: 60, h: 44 } }],
    ['fx.wash', null, { color: '#e8c27a', strength: 1 }],
  ], 'a fixed table colour (brass) wins over the caller colour');
  assert.deepEqual(r.page, ['quiet_room', 'reveal']);
  host.clear();
  m.play('wheel.land.gif', { color: 'red', from: { x: 1, y: 1, w: 2, h: 2 }, gif: '../x.gif' });
  assert.deepEqual(calls(host), [['fx.wash', null, { strength: 0.9 }], ['fx.gif_from', null, { ms: 3400 }]], 'bad colour, rect and key are dropped');
  assert.deepEqual(m.play('wheel.nope'), { tokens: [], page: [], held: false });
  assert.equal(host.fx.length, 2, 'an unknown id fires nothing');
});

test('gates never drop a step (2026-09-15): every step is posted with every gate off; the host skips per toggle', () => {
  const host = createMockHost({ gates: { flash: false, spiral: false, brainDrain: false, tunnel: false, subliminal: false } });
  const m = createMoments(host.ctx, { station: 'wheel' });
  const r = m.play('wheel.land.jackpot', { from: RECT, gif: 'g0' });
  assert.equal(r.tokens.length, 3, 'the spiral, the picture and the wash are all posted');
  assert.deepEqual(r.page, ['quiet_room', 'reveal']);
  m.play('roulette.wake'); m.play('roulette.land.big', { color: '#ff5fa2', from: RECT, gif: 'g1' });
  assert.deepEqual(host.fx.map((x) => x.fxId), ['fx.loom_spiral', 'fx.gif_from', 'fx.wash', 'fx.loom_spiral', 'fx.wash', 'fx.gif_from']);
  assert.ok(host.fx.every((x) => x.ack.fired.length === 0), 'the mock host, like the real one, fires none of them');
  m.tunnel(0.8);
  assert.deepEqual(host.tunnel.map((x) => x.level), [0.8], 'tunnel off: the level still posts (the host decides)');
  host.settings({ gates: { flash: true } });
  m.play('wheel.land.jackpot', { from: RECT, gif: 'g0' });
  assert.equal(host.fx.length, 9, 'a settings frame changes nothing on the page');
  m.dispose();
});

test('fx-tunnel and the haze post whatever the brainDrain and tunnel gates say; a settings frame with tunnel off changes nothing', async () => {
  const drainOff = createMockHost({ intensity: 'full', gates: { brainDrain: false } });
  const a = createMoments(drainOff.ctx, { station: 'roulette' });
  a.play('roulette.run'); a.tunnel(0.6);
  assert.deepEqual(drainOff.fx.map((x) => x.fxId), ['fx.haze'], 'brainDrain off: the haze is still posted (the host skips it)');
  assert.equal(drainOff.fx[0].ack.fired.length, 0);
  assert.deepEqual(drainOff.tunnel.map((x) => x.level), [0.6], 'the run posts its tunnel');
  a.dispose();

  const tunnelOff = createMockHost({ intensity: 'full', gates: { tunnel: false } });
  const b = createMoments(tunnelOff.ctx, { station: 'roulette' });
  b.play('roulette.run'); b.tunnel(0.6); b.play('cards.lose');
  await new Promise((r) => setTimeout(r, 120));
  assert.deepEqual(tunnelOff.fx.map((x) => x.fxId), ['fx.haze'], 'the haze holds');
  assert.ok(tunnelOff.tunnel.length > 0 && tunnelOff.tunnel.every((x) => x.applied === false), 'tunnel off: the level and the losing breath still post; the host applies none');
  b.dispose();

  const live = createMockHost();
  const c = createMoments(live.ctx, { station: 'cards' });
  c.play('cards.lose');
  await new Promise((r) => setTimeout(r, 160));
  assert.ok(live.tunnel.some((x) => x.level > 0), 'the losing edges breathe');
  live.settings({ gates: { tunnel: false } });
  const n = live.tunnel.length;
  await new Promise((r) => setTimeout(r, 160));
  assert.ok(live.tunnel.length > n && c.breathing(), 'a settings frame with tunnel off does not stop the breath');
  c.dispose();
  assert.equal(live.tunnel.at(-1).level, 0, 'dispose closes it');
});

test('cards: holdScreen stops host fx and the tunnel; bloom then win drops the picture and raises the wash', () => {
  const host = createMockHost();
  const m = createMoments(host.ctx, { station: 'cards' });
  assert.deepEqual(m.play('cards.sit').page, ['sit_fan']);
  m.holdScreen(true);
  const r = m.play('cards.win', { gif: 'g4' });
  assert.equal(r.held, true);
  assert.deepEqual(r.page, ['win_tunnel', 'chip_vortex']);
  m.tunnel(0.7);
  assert.equal(host.fx.length + host.tunnel.length, 0, 'nothing fullscreen while a decision is open');
  m.holdScreen(false);
  m.play('cards.win', { gif: 'g4' });
  assert.deepEqual(calls(host), [['fx.wash', ['g4'], { color: '#5fffd0', strength: 0.7 }]]);
  host.clear();
  m.play('cards.bloom', { from: RECT, gif: 'g0' });
  m.play('cards.win', { gif: 'g9' });
  assert.deepEqual(calls(host), [
    ['fx.gif_from', ['g0'], { ms: 4000, from: { x: 612, y: 188, w: 60, h: 44 } }],
    ['fx.wash', null, { color: '#ff5fa2', strength: 0.8 }],
    ['fx.wash', null, { color: '#5fffd0', strength: 0.9 }],
  ]);
  host.clear();
  m.play('cards.win', { gif: 'g9' });
  assert.deepEqual(calls(host)[0][1], ['g9'], 'the next win (no bloom) has its picture again');
  m.play('cards.push');
  assert.equal(host.fx.length, 1);
});

test('cards.lose breathes the tunnel 0.75 x sin(PI p) over 2600 ms, throttled to 10 a second, then 0', () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  try {
    const host = createMockHost();
    const m = createMoments(host.ctx, { station: 'cards' });
    m.play('cards.lose');
    for (let t = 0; t < 3000; t += 50) mock.timers.tick(50);
    const lv = host.tunnel.map((x) => x.level);
    assert.ok(Math.max(...lv) >= 0.74 && Math.max(...lv) <= 0.75, 'peaks at 0.75: ' + Math.max(...lv));
    assert.equal(lv.at(-1), 0, 'lets go');
    const at = host.tunnel.map((x) => x.at);
    assert.ok(at.every((a, i) => i === 0 || a - at[i - 1] >= 100), 'at most one post per 100 ms');
    assert.ok(host.tunnel.length <= 28, host.tunnel.length + ' posts over 2.6 s');
    m.dispose();
  } finally { mock.timers.reset(); }
});

test('tunnel re-posts every second while it holds a level, and holdScreen or cancel drops it to 0 at once', () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  try {
    const host = createMockHost();
    const m = createMoments(host.ctx, { station: 'wheel' });
    m.tunnel(0.5);
    for (let i = 0; i < 70; i++) { mock.timers.tick(50); m.tunnel(0.5); }   // 3.5 s of the same level every frame
    assert.equal(host.tunnel.length, 4, 'the first post plus a keep-alive each second: ' + host.tunnel.map((x) => x.at - 1000));
    m.tunnel(0.6); m.tunnel(0.7);
    mock.timers.tick(120);
    assert.deepEqual(host.tunnel.slice(-2).map((x) => x.level), [0.6, 0.7], 'a later level in the same 100 ms is posted when the gap is up');
    m.cancel();
    assert.equal(host.tunnel.at(-1).level, 0);
    const n = host.tunnel.length;
    mock.timers.tick(3000);
    assert.equal(host.tunnel.length, n, 'no keep-alive after 0');
    m.dispose();
  } finally { mock.timers.reset(); }
});

test('roulette: run holds haze at Full only, a wake holds the spiral, every landing releases them first', () => {
  const host = createMockHost({ intensity: 'full' });
  const m = createMoments(host.ctx, { station: 'roulette' });
  const run = m.play('roulette.run');
  assert.deepEqual(run.page, ['lighthouse', 'fret_rattle', 'velvet_wake']);
  const wake = m.play('roulette.wake');
  assert.deepEqual(calls(host), [['fx.haze', null, { hold: true }], ['fx.loom_spiral', null, { preset: 'wake', hold: true, alpha: 0.65 }]]);
  assert.equal(m.debug().holds, 2);
  const big = m.play('roulette.land.big', { color: '#ff5fa2', from: RECT, gif: 'g2', wake: true });
  assert.deepEqual(host.release.map((x) => x.token).sort(), [...run.tokens, ...wake.tokens].sort(), 'released before the landing fires');
  assert.ok(host.calls.findIndex((c) => c.type === 'fx-release') < host.calls.findIndex((c) => c.fxId === 'fx.gif_from'));
  assert.deepEqual(big.page, ['chips_in', 'pulled_pair']);
  assert.deepEqual(calls(host).slice(2), [['fx.wash', null, { strength: 1, color: '#ff5fa2' }], ['fx.gif_from', ['g2'], { ms: 3600, from: { x: 612, y: 188, w: 60, h: 44 } }]]);
  host.clear();
  m.play('roulette.run');
  m.play('roulette.land.miss');
  assert.equal(host.release.length, 1);
  assert.deepEqual(m.play('roulette.land.win', { color: '#9b6bff' }).page, ['chips_in']);

  const normal = createMockHost();
  const n = createMoments(normal.ctx, { station: 'roulette' });
  assert.deepEqual(n.play('roulette.run').page, ['lighthouse', 'fret_rattle'], 'no velvet wake below Full');
  assert.equal(normal.fx.length, 0, 'no haze below Full');
  assert.deepEqual(n.play('roulette.land.big', { gif: 'g1' }).page, ['chips_in'], 'no pulled pair without a wake');
});

test('a gate going off releases nothing it holds; dispose releases everything and unsubscribes', () => {
  const host = createMockHost({ intensity: 'full' });
  const m = createMoments(host.ctx, { station: 'roulette' });
  m.play('roulette.run'); m.play('roulette.wake');
  host.settings({ gates: { spiral: false } });
  assert.equal(host.release.length, 0, 'spiral off: the wake spiral keeps its hold (the host is the enforcer)');
  m.dispose();
  assert.deepEqual(host.release.map((r) => host.fx.find((f) => f.token === r.token).fxId).sort(), ['fx.haze', 'fx.loom_spiral'], 'dispose releases both');
  host.settings({ gates: { brainDrain: false } });
  assert.equal(host.release.length, 2);
  assert.deepEqual(m.play('roulette.wake'), { tokens: [], page: [], held: false });
});

test('roulette.run played every frame holds one haze; the table strength wins over the caller', () => {
  const host = createMockHost({ intensity: 'full' });
  const m = createMoments(host.ctx, { station: 'roulette' });
  for (let i = 0; i < 30; i++) m.play('roulette.run');
  m.play('roulette.wake'); m.play('roulette.wake');
  assert.deepEqual(host.fx.map((x) => x.fxId), ['fx.haze', 'fx.loom_spiral'], 'one hold per fx id while it is held');
  m.play('roulette.land.win', { color: '#9b6bff', strength: 0.2 });
  assert.deepEqual(calls(host).at(-1), ['fx.wash', null, { strength: 0.6, color: '#9b6bff' }], 'the ladder stays 0.6');
  m.play('roulette.run');
  assert.equal(host.fx.at(-1).fxId, 'fx.haze', 'a released haze can be held again');
  m.play('wheel.land.jackpot', { color: '#9b6bff', strength: 0.3 });
  assert.deepEqual(calls(host).at(-1), ['fx.wash', null, { color: '#e8c27a', strength: 1 }]);
  m.dispose();
});

test('a settings frame below Full, or reduced motion, releases a running haze hold and keeps the spiral', () => {
  for (const patch of [{ intensity: 'normal' }, { intensity: 'calm' }, { reduced: true }]) {
    const host = createMockHost({ intensity: 'full' });
    const m = createMoments(host.ctx, { station: 'roulette' });
    m.play('roulette.run'); m.play('roulette.wake');
    host.settings({ gates: { flash: true } });
    assert.equal(host.release.length, 0, 'a Full frame keeps both holds');
    host.settings(patch);
    assert.deepEqual(host.release.map((r) => host.fx.find((f) => f.token === r.token).fxId), ['fx.haze'], JSON.stringify(patch));
    assert.equal(m.debug().holds, 1);
    m.dispose();
  }
});

test('a ctx without the hypno members skips everything and never throws', () => {
  const m = createMoments({ intensity: 'normal' }, { station: 'wheel' });
  assert.deepEqual(m.play('wheel.land.gif', { from: RECT, gif: 'g0' }), { tokens: [], page: ['quiet_room'], held: false });
  m.tunnel(0.5); m.holdScreen(true); m.release(['x']); m.cancel(); m.dispose();
});
