/* The room side of CONTRACT 10.13 (room/loader.js ctx): gates, onSettings, fx args and the token on the
 * promise, fx-release, fx-tunnel and the media count, through the real bridge on a fake chrome.webview. */

import { test } from 'node:test';
import assert from 'node:assert/strict';

const posted = [];
const listeners = [];
globalThis.window = { chrome: { webview: {
  addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
  postMessage(m) { posted.push(m); },
} } };
globalThis.document = { createElement: () => ({ className: '', dataset: {}, remove() {} }) };
const emit = (data) => listeners.forEach((fn) => fn({ data }));

const bridge = await import('../../../bridge.js');
const { createLoader } = await import('../../../room/loader.js');
bridge.markInitialized();

const settingsFns = new Set();
const state = { sp: 5, reduced: false, motion: 'full', intensity: 'normal', gates: Object.freeze({ flash: true, subliminal: true, spiral: false, brainDrain: true }) };
const loader = createLoader({
  layer: { appendChild() {} }, state, lex: (k, f) => f, onSp: () => () => {},
  onSettings: (fn) => { settingsFns.add(fn); return () => settingsFns.delete(fn); },
  spReadout: null, standUp() {}, log() {},
});
const row = { id: 'cards', state: 'live', entry: 'shared/hypno/tests/probe-station.js' };
const last = (type) => posted.filter((m) => m.type === type).at(-1);

test('a live station gets the hypno ctx members', async () => {
  assert.equal(await loader.open(row), 'live');
  const { ctx, keys } = globalThis.__probe;
  for (const k of ['fx', 'fxRelease', 'fxTunnel', 'media', 'gates', 'onSettings']) assert.ok(keys.includes(k), k);
  assert.deepEqual({ ...ctx.gates }, { flash: true, subliminal: true, spiral: false, brainDrain: true });
  assert.ok(Object.isFrozen(ctx.gates));
  state.gates = Object.freeze({ flash: false, subliminal: true, spiral: true, brainDrain: true });
  assert.equal(ctx.gates.flash, false, 'gates is a live getter');
});

test('fx carries args and its token synchronously; the ack resolves the same promise', async () => {
  const { ctx } = globalThis.__probe;
  const p = ctx.fx('fx.gif_from', ['g2'], { from: { x: 1, y: 2, w: 60, h: 44 }, ms: 3400 });
  assert.match(p.token, /^[0-9a-f]{32}$/);
  const m = last('fx');
  assert.deepEqual(m, { type: 'fx', token: p.token, fxId: 'fx.gif_from', station: 'cards', symbols: ['g2'], args: { from: { x: 1, y: 2, w: 60, h: 44 }, ms: 3400 } });
  emit({ type: 'fx-ack', token: p.token, fired: ['gif-from'], skipped: [] });
  assert.deepEqual((await p).fired, ['gif-from']);
  ctx.fx('fx.wash');
  assert.ok(!('args' in last('fx')) && !('symbols' in last('fx')), 'no args, no symbols: neither field is sent');
});

test('fx-release and fx-tunnel post without a reply', () => {
  const { ctx } = globalThis.__probe;
  ctx.fxRelease('abc'); ctx.fxRelease(''); ctx.fxRelease(7);
  assert.deepEqual(posted.filter((m) => m.type === 'fx-release'), [{ type: 'fx-release', token: 'abc', station: 'cards' }]);
  ctx.fxTunnel(0.6234); ctx.fxTunnel(3); ctx.fxTunnel(NaN);
  assert.deepEqual(posted.filter((m) => m.type === 'fx-tunnel').map((m) => m.level), [0.623, 1]);
  assert.equal(last('fx-tunnel').station, 'cards');
});

test('media sends count only when it is an integer 1..13', () => {
  const { ctx } = globalThis.__probe;
  ctx.media({ count: 13 }); assert.equal(last('media-request').count, 13);
  ctx.media({ count: 14 }); assert.ok(!('count' in last('media-request')));
  ctx.media({ count: 2.5 }); assert.ok(!('count' in last('media-request')));
  ctx.media(); assert.ok(!('count' in last('media-request')));
  assert.equal(last('media-request').station, 'cards');
});

test('onSettings subscribes, unsubscribes, and a forgotten subscription goes with the station', async () => {
  const { ctx } = globalThis.__probe;
  const got = [];
  const off = ctx.onSettings((f) => got.push(f));
  ctx.onSettings((f) => got.push(f));
  assert.equal(settingsFns.size, 2);
  off();
  assert.equal(settingsFns.size, 1);
  await loader.close();
  assert.equal(settingsFns.size, 0, 'close drops what the station left subscribed');
  assert.ok(posted.some((m) => m.type === 'station-close' && m.station === 'cards'));
});
