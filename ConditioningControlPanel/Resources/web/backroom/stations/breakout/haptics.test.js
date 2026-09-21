import test from 'node:test';
import assert from 'node:assert/strict';
import { planPulse, createScheduler, createHaptics, GAP_MS, NUDGE } from './haptics.js';

const COLOUR = { state: 'colour', sat: 0.8 }, GREY = { state: 'grey', sat: 0 };

function rig(opts = {}) {
  const sent = [], buzz = [], rumble = [];
  let t = 1000;
  const pad = { vibrationActuator: { playEffect: (kind, o) => { rumble.push({ kind, ...o }); return Promise.resolve(); }, reset: () => { rumble.push({ kind: 'reset' }); } } };
  const nav = { vibrate: (ms) => { buzz.push(ms); return true; }, getGamepads: () => [null, pad] };
  const h = createHaptics({ post: (m) => sent.push(m), nav, clock: () => t, ...opts });
  return { h, sent, buzz, rumble, at: (ms) => { t = ms; }, by: (ms) => { t += ms; } };
}

test('brick pulses climb with the combo and stay light', () => {
  const a = planPulse('hit', { kind: 'brick', combo: 1 }, COLOUR), b = planPulse('hit', { kind: 'brick', combo: 8 }, COLOUR), c = planPulse('hit', { kind: 'brick', combo: 99 }, COLOUR);
  assert.equal(a.level, 0.12);
  assert.ok(b.level > a.level && c.level > b.level);
  assert.equal(c.level, 0.5);
  assert.ok(planPulse('hit', { kind: 'paddle' }, COLOUR).level < a.level);
  assert.equal(planPulse('hit', { kind: 'wall' }, COLOUR), null);
});

test('the big moments out-rank and out-last the chatter, breakout is the strongest', () => {
  const brick = planPulse('hit', { kind: 'brick', combo: 20 }, COLOUR), perfect = planPulse('perfect', { streak: 1 }, COLOUR);
  assert.ok(perfect.priority > brick.priority);
  assert.ok(planPulse('perfect', { streak: 4 }, COLOUR).level > perfect.level);
  let top = 0;
  for (const name of ['jackpot', 'lastBrick', 'wall', 'shatterWall']) {
    const p = planPulse(name, {}, COLOUR);
    assert.ok(p.priority > perfect.priority && p.ms >= 350 && p.level >= 0.75, name);
    top = Math.max(top, p.level);
  }
  const out = planPulse('breakout', {}, GREY);
  assert.ok(out.level >= top && out.priority === 4);
  assert.equal(planPulse('word', { fired: false }, COLOUR), null);
  assert.ok(planPulse('word', { fired: true }, COLOUR).level >= 0.4);
  assert.equal(planPulse('powerWarn', {}, COLOUR), null);
});

test('grey is quiet: a faint brick tick and nothing else, except the way in and the way out', () => {
  const tick = planPulse('hit', { kind: 'brick', combo: 30 }, GREY);
  assert.ok(tick.level <= 0.1);
  for (const name of ['perfect', 'bubblePop', 'capture', 'powerCatch', 'jackpot', 'wall', 'lastBrick', 'word', 'launch'])
    assert.equal(planPulse(name, { fired: true }, GREY), null, name);
  assert.equal(planPulse('hit', { kind: 'paddle' }, GREY), null);
  assert.equal(planPulse('relapse', {}, GREY).tag, 'thud');
  assert.ok(planPulse('breakoutStart', {}, GREY));
});

test('the scheduler holds about three pulses a second', () => {
  const s = createScheduler();
  let sent = 0;
  for (let t = 0; t < 1000; t += 16) if (s.offer({ level: 0.2 + (t % 7) / 100, ms: 60, priority: 1 }, t)) sent++;
  assert.ok(sent >= 3 && sent <= 4, 'sent ' + sent);
});

test('a bigger moment interrupts a smaller one, never the other way round', () => {
  const s = createScheduler();
  assert.ok(s.offer({ level: 0.2, ms: 90, priority: 1 }, 0));
  assert.equal(s.offer({ level: 0.5, ms: 120, priority: 2 }, 50), null, 'even an interrupt waits 100 ms');
  assert.ok(s.offer({ level: 0.5, ms: 120, priority: 2 }, 120));
  assert.ok(s.offer({ level: 0.9, ms: 800, priority: 3 }, 230));
  assert.equal(s.offer({ level: 0.3, ms: 90, priority: 1 }, 700), null, 'chatter under a long pulse is dropped');
  assert.equal(s.offer({ level: 0.85, ms: 600, priority: 3 }, 700), null, 'an equal does not cut it off');
  assert.ok(s.offer({ level: 1, ms: 1200, priority: 4 }, 700));
});

test('a repeated level is nudged one toy step so the 1 s same-level debounce cannot eat it', () => {
  const s = createScheduler();
  const a = s.offer({ level: 0.12, ms: 90, priority: 1 }, 0), b = s.offer({ level: 0.12, ms: 90, priority: 1 }, GAP_MS);
  assert.equal(a.level, 0.12);
  assert.equal(b.level, Math.round((0.12 + NUDGE) * 1000) / 1000);
  assert.equal(s.offer({ level: 0.12, ms: 90, priority: 1 }, GAP_MS * 2).level, 0.12, 'the third differs from the nudged second already');
  const top = createScheduler();
  top.offer({ level: 1, ms: 50, priority: 1 }, 0);
  assert.equal(top.offer({ level: 1, ms: 50, priority: 1 }, 400).level, 1 - NUDGE);
  assert.equal(top.offer({ level: 0.3, ms: 50, priority: 1 }, 5000).level, 0.3, 'an old level needs no nudge');
});

test('the host sink posts the documented shape, the phone and the pad take the same pulse', () => {
  const r = rig();
  r.h.onEvent('perfect', { streak: 1 }, COLOUR);
  assert.deepEqual(r.sent, [{ type: 'haptic', station: 'breakout', level: 0.45, ms: 120, tag: 'perfect' }]);
  assert.equal(r.buzz.length, 1);
  assert.ok(r.buzz[0] > 0 && r.buzz[0] <= 120);
  assert.equal(r.rumble[0].kind, 'dual-rumble');
  assert.equal(r.rumble[0].duration, 120);
  assert.equal(r.rumble[0].strongMagnitude, 0.45);
});

test('ctx.bridge.send is the host path when nothing is injected, and a missing bridge is harmless', () => {
  const got = [];
  const h = createHaptics({ ctx: { bridge: { send: (m) => got.push(m) } }, nav: {}, clock: () => 0 });
  h.onEvent('capture', {}, COLOUR);
  assert.equal(got[0].type, 'haptic');
  assert.doesNotThrow(() => createHaptics({ ctx: {}, nav: {}, clock: () => 0 }).onEvent('capture', {}, COLOUR));
});

test('lastBrick then wall: the second big moment waits for its slot instead of vanishing', () => {
  const r = rig();
  r.h.onEvent('lastBrick', {}, COLOUR);
  r.h.onEvent('wall', {}, COLOUR);
  assert.deepEqual(r.sent.map((m) => m.tag), ['lastBrick']);
  r.by(200); r.h.frame(COLOUR);
  assert.equal(r.sent.length, 1);
  r.by(200); r.h.frame(COLOUR);
  assert.deepEqual(r.sent.map((m) => m.tag), ['lastBrick', 'wall']);
});

test('relapse is one heavy thud, then silence', () => {
  const r = rig();
  r.h.onEvent('relapseStart', {}, COLOUR);
  r.by(500); r.h.onEvent('relapse', {}, GREY);
  r.by(100); r.h.onEvent('hit', { kind: 'brick', combo: 1 }, GREY);
  assert.deepEqual(r.sent.map((m) => m.tag), ['thud']);
  r.by(1500); r.h.onEvent('hit', { kind: 'brick', combo: 1 }, GREY);
  assert.deepEqual(r.sent.map((m) => m.tag), ['thud', 'grey']);
});

test('breakoutStart is a rising pair', () => {
  const r = rig();
  r.h.onEvent('breakoutStart', {}, GREY);
  r.by(380); r.h.frame(GREY);
  assert.deepEqual(r.sent.map((m) => m.tag), ['rise', 'rise2']);
  assert.ok(r.sent[1].level > r.sent[0].level);
});

test('stop clears every sink at once, drops what was waiting, and is free to call every frame', () => {
  const r = rig();
  r.h.onEvent('lastBrick', {}, COLOUR);
  r.h.onEvent('wall', {}, COLOUR);
  r.h.stop(); r.h.stop(); r.h.stop();
  assert.deepEqual(r.sent.filter((m) => m.tag === 'stop'), [{ type: 'haptic', station: 'breakout', level: 0, ms: 0, tag: 'stop' }]);
  assert.equal(r.buzz.at(-1), 0);
  assert.equal(r.rumble.at(-1).kind, 'reset');
  r.by(2000); r.h.frame(COLOUR);
  assert.equal(r.sent.filter((m) => m.tag === 'wall').length, 0, 'the queued wall died with the stop');
  const fresh = rig();
  fresh.h.stop();
  assert.equal(fresh.sent.length, 0, 'nothing played, nothing to stop');
});

test('disabled is inert and destroyed is inert', () => {
  const off = rig({ enabled: false });
  off.h.onEvent('breakout', {}, COLOUR); off.h.frame(COLOUR, 1); off.h.stop(); off.h.destroy();
  assert.equal(off.sent.length + off.buzz.length + off.rumble.length, 0);
  const r = rig();
  r.h.onEvent('capture', {}, COLOUR);
  r.h.destroy(); r.by(5000); r.h.onEvent('breakout', {}, COLOUR); r.h.frame(COLOUR);
  assert.deepEqual(r.sent.map((m) => m.tag), ['capture', 'stop']);
});
