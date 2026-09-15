/* shared/hypno/voice.js and the host half of callout.word(): what the page sends, what the four ack
 * sources mean, and the chain waiting on a long clip. CONTRACT 10.21. */

import { test, mock } from 'node:test';
import assert from 'node:assert/strict';
import { createVoice, readAck, ACK_MS, MAX_DURATION_MS } from '../voice.js';
import { createCallout, WORD_MS, WORD_GAP_MS, MAX_WORD_HOLD_MS } from '../callout.js';

/* ------------------------------------------------------------------ the fakes */
function fakeBridge(answer) {
  const sent = [];
  return {
    sent,
    mintId: () => 'tok' + sent.length,
    send(m) { sent.push(m); },
    request(msg, replyType, match, timeoutMs, fallback) {
      sent.push(msg);
      const a = typeof answer === 'function' ? answer(msg) : answer;
      return Promise.resolve(a === undefined ? fallback : a);
    },
  };
}
function fakeDoc() {
  const doc = { head: null };
  const el = (tag) => {
    const node = {
      tag, ownerDocument: doc, children: [], parent: null, style: {}, attrs: {}, className: '', textContent: '',
      append(...kids) { for (const k of kids) { k.parent = node; node.children.push(k); } },
      remove() { if (node.parent) { node.parent.children = node.parent.children.filter((c) => c !== node); node.parent = null; } },
      setAttribute(k, v) { node.attrs[k] = v; }, getAttribute(k) { return node.attrs[k]; },
      animate(frames, opts) { return { frames, opts, cancel() {} }; },
      querySelectorAll(sel) {
        const classes = sel.split(',').map((s) => s.trim().replace(/^\./, ''));
        const out = [];
        (function walk(n) { for (const c of n.children) { if (classes.some((k) => c.className.split(' ').includes(k))) out.push(c); walk(c); } })(node);
        return out;
      },
      getBoundingClientRect() { return { left: 0, top: 0, width: 800, height: 600 }; },
    };
    return node;
  };
  doc.createElement = el;
  doc.head = el('head');
  return doc;
}
function fakeSpeech() {
  const spoken = [];
  globalThis.SpeechSynthesisUtterance = class { constructor(t) { this.text = t; } };
  globalThis.speechSynthesis = { cancel() {}, speak(u) { spoken.push({ text: u.text, rate: u.rate }); } };
  return { spoken, restore() { delete globalThis.SpeechSynthesisUtterance; delete globalThis.speechSynthesis; } };
}
function harness(voice) {
  const doc = fakeDoc(), mount = doc.createElement('div');
  const ctx = { fxTunnel: () => {}, gates: { tunnel: false, subliminal: true }, reduced: false };
  return createCallout({ mount, ctx, cues: { play: () => {} }, lex: (k, f) => f, voice });
}
const wordsOf = (c) => c.debug().voice;

/* ------------------------------------------------------------------ the adapter */
test('no host: createVoice is null, so the caller stays on speechSynthesis', () => {
  assert.equal(createVoice({ hosted: false }), null);
  assert.equal(createVoice({ hosted: true, bridge: {} }), null, 'a bridge with no request() is no host either');
});

test('word.speak carries the text, the reversal draw and the seed, and never leaks anything else', async () => {
  const b = fakeBridge({ source: 'clip', durationMs: 640 });
  const v = createVoice({ bridge: b, hosted: true });
  const ack = await v.speak({ text: 'Let Go', reversed: true, seed: 4294967295 });
  assert.deepEqual(ack, { source: 'clip', durationMs: 640 });
  const msg = b.sent[0];
  assert.equal(msg.type, 'word.speak');
  assert.equal(msg.text, 'Let Go');
  assert.equal(msg.reversed, true);
  assert.equal(msg.seed, 4294967295);
  assert.deepEqual(Object.keys(msg).sort(), ['reversed', 'seed', 'text', 'token', 'type']);
  v.stop();
  assert.deepEqual(b.sent.at(-1), { type: 'word.stop' });
});

test('readAck: only the four sources survive, none forces a zero duration, a wild number is clamped', () => {
  assert.deepEqual(readAck({ source: 'preset', durationMs: 900 }), { source: 'preset', durationMs: 900 });
  assert.deepEqual(readAck({ source: 'tts', durationMs: 1e9 }), { source: 'tts', durationMs: MAX_DURATION_MS });
  assert.deepEqual(readAck({ source: 'clip', durationMs: NaN }), { source: 'clip', durationMs: 0 });
  assert.deepEqual(readAck({ source: 'shout', durationMs: 500 }), { source: 'none', durationMs: 0 });
  assert.deepEqual(readAck(null), { source: 'none', durationMs: 0 });
  assert.ok(ACK_MS > 0 && ACK_MS < 6000, 'the host answers well inside a request timeout');
});

/* ------------------------------------------------------------------ callout.word with a host */
test('a host that speaks keeps the page quiet; an ack of none sends the word back to speechSynthesis', async () => {
  const sp = fakeSpeech();
  try {
    const said = [];
    const c = harness({ available: true, speak(o) { said.push(o); return Promise.resolve({ source: o.text === 'SINK' ? 'none' : 'tts', durationMs: 300 }); }, stop() {} });
    c.word('DROP', { seed: 7 });
    await new Promise((r) => setTimeout(r, 0));
    assert.deepEqual(said.map((x) => x.text), ['DROP'], 'the host was asked');
    assert.deepEqual(sp.spoken, [], 'and the browser voice stayed out of it');
    assert.equal(wordsOf(c).at(-1).source, 'tts');

    c.word('SINK', { seed: 7 });
    await new Promise((r) => setTimeout(r, 0));
    assert.equal(sp.spoken.length, 1, 'none means the page says it itself');
    assert.equal(sp.spoken[0].text, 'SINK');
    c.dispose();
  } finally { sp.restore(); }
});

test('a host that throws or rejects falls back to speechSynthesis rather than going silent', async () => {
  const sp = fakeSpeech();
  try {
    const a = harness({ available: true, speak() { throw new Error('gone'); }, stop() {} });
    a.word('DROP', { seed: 1 });
    assert.equal(sp.spoken.length, 1, 'a throw is spoken on this very frame');
    a.dispose();
    const b = harness({ available: true, speak() { return Promise.reject(new Error('gone')); }, stop() {} });
    b.word('RELAX', { seed: 1 });
    await new Promise((r) => setTimeout(r, 0));
    assert.equal(sp.spoken.length, 2, 'a rejection is spoken once it settles');
    b.dispose();
  } finally { sp.restore(); }
});

test('cancel stops the host line too (Law VI)', () => {
  let stops = 0;
  const c = harness({ available: true, speak: () => Promise.resolve({ source: 'clip', durationMs: 200 }), stop() { stops++; } });
  c.word('DROP', { seed: 1 });
  c.cancel();
  assert.ok(stops >= 1, 'word.stop went out');
  c.dispose();
});

test('a clip longer than WORD_MS holds the next word of the chain, and MAX_WORD_HOLD_MS is the ceiling', async () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  try {
    const seen = [];
    const c = harness({
      available: true,
      speak(o) { seen.push({ text: o.text, at: Date.now() }); return Promise.resolve({ source: 'clip', durationMs: 2000 }); },
      stop() {},
    });
    c.word('DROP', { chain: ['RELAX'], seed: 3 });
    await Promise.resolve(); await Promise.resolve();   // the first ack settles
    mock.timers.tick(WORD_GAP_MS);
    assert.equal(seen.length, 1, 'the second word is still waiting on the 2 s clip at ' + WORD_GAP_MS + ' ms');
    mock.timers.tick(2000 - WORD_GAP_MS);
    await Promise.resolve();
    assert.equal(seen.length, 2, 'and lands when the clip is done');
    assert.equal(seen[1].at - seen[0].at, 2000);
    assert.ok(MAX_WORD_HOLD_MS >= WORD_MS, 'the ceiling is at least one word long');
    c.dispose();
  } finally { mock.timers.reset(); }
});
