/* shared/hypno/callout.js on small DOM fakes: the announcer's zoom schedule, the word chain, the tunnel breath,
 * the seeded 1-in-100 reversal, cancel dropping everything at once (Law VI), and the settle's rotating bark. */

import { test, mock } from 'node:test';
import assert from 'node:assert/strict';
import {
  createCallout, wordPlan, tunnelLevelAt, seededRng, DEAD_BARKS, CALLOUT_MS, ZOOM, TIER_VH,
  WORD_IN_MS, WORD_HOLD_MS, WORD_OUT_MS, WORD_MS, WORD_GAP_MS, WORD_TUNNEL_MS, WORD_TUNNEL_LEVEL, REVERSE_ODDS, BARK_MS,
} from '../callout.js';

/* ------------------------------------------------------------------ the fakes */
function fakeDoc() {
  const doc = { head: null };
  const el = (tag) => {
    const anims = [];
    const node = {
      tag, ownerDocument: doc, children: [], parent: null, style: {}, attrs: {}, className: '', textContent: '', anims,
      append(...kids) { for (const k of kids) { k.parent = node; node.children.push(k); } },
      remove() { if (node.parent) { node.parent.children = node.parent.children.filter((c) => c !== node); node.parent = null; } },
      setAttribute(k, v) { node.attrs[k] = v; }, getAttribute(k) { return node.attrs[k]; },
      animate(frames, opts) { const a = { frames, opts, cancelled: false, cancel() { a.cancelled = true; } }; anims.push(a); return a; },
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
const all = (n, cls) => n.querySelectorAll('.' + cls);
function harness(extra = {}) {
  const doc = fakeDoc(), mount = doc.createElement('div');
  const tunnel = [], cues = [];
  const ctx = { fxTunnel: (l) => tunnel.push(l), gates: { tunnel: true, subliminal: true }, reduced: false, ...extra.ctx };
  const c = createCallout({ mount, ctx, cues: { play: (cue, o) => cues.push({ cue, ...o }) }, lex: (k, f) => f, ...extra.opts });
  return { doc, mount, ctx, c, tunnel, cues };
}
function fakeSpeech() {
  const spoken = [];
  let cancels = 0;
  globalThis.SpeechSynthesisUtterance = class { constructor(t) { this.text = t; } };
  globalThis.speechSynthesis = { cancel() { cancels++; }, speak(u) { spoken.push({ text: u.text, rate: u.rate, pitch: u.pitch }); } };
  return { spoken, get cancels() { return cancels; }, restore() { delete globalThis.SpeechSynthesisUtterance; delete globalThis.speechSynthesis; } };
}
const layerOf = (mount) => mount.children.find((c) => c.className === 'br-callout');
const findSeed = (pred) => { for (let s = 0; s < 40000; s++) if (pred(s)) return s; throw new Error('no seed'); };

/* ------------------------------------------------------------------ the announcer */
test('show draws the text centred with the zoom schedule: 60% -> 130% over 1 s, hold 200 ms, dissolve 400 ms, gone at CALLOUT_MS', async () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  try {
    const { mount, c } = harness();
    const r = c.show('br_callout_double_spin', 'Double Spin', { tier: 'big' });
    const layer = layerOf(mount);
    assert.ok(layer, 'the overlay is appended to the mount');
    const [text] = all(layer, 'br-callout-text');
    assert.equal(text.textContent, 'Double Spin');
    assert.equal(text.style.fontSize, TIER_VH.big + 'vh');
    assert.equal(text.getAttribute('data-tier'), 'big');
    assert.ok(/Condensed|Narrow|Impact/.test(text.style.fontFamily), 'a bold condensed stack when the cabinet names no font');
    const zoom = text.anims[0];
    assert.equal(zoom.opts.duration, CALLOUT_MS);
    assert.equal(ZOOM.zoomMs + ZOOM.holdMs + ZOOM.outMs, CALLOUT_MS);
    assert.match(zoom.frames[0].transform, /scale\(0\.6\)/);
    const atHold = zoom.frames.find((f) => f.offset === ZOOM.zoomMs / CALLOUT_MS);
    assert.match(atHold.transform, /scale\(1\.3\)/, 'the zoom lands on 130% at 1 s');
    const outStart = zoom.frames.find((f) => f.offset === (ZOOM.zoomMs + ZOOM.holdMs) / CALLOUT_MS);
    assert.equal(outStart.opacity, 1, 'held to 1.2 s'); assert.equal(zoom.frames.at(-1).opacity, 0, 'dissolved at 1.6 s');
    const ghosts = all(text, 'br-callout-ghost');
    assert.equal(ghosts.length, 2, 'two offset coloured copies for the smear');
    assert.ok(ghosts.every((g) => g.anims[0].frames.some((f) => f.offset === (ZOOM.zoomMs + ZOOM.holdMs) / CALLOUT_MS && f.opacity === 0)), 'the smear starts with the dissolve');
    let done = false; r.done.then(() => { done = true; });
    mock.timers.tick(CALLOUT_MS - 1); assert.equal(all(layer, 'br-callout-text').length, 1);
    mock.timers.tick(1);
    await Promise.resolve();
    assert.ok(done); assert.equal(all(layer, 'br-callout-text').length, 0, 'gone at CALLOUT_MS');
    c.dispose();
  } finally { mock.timers.reset(); }
});

test('tiers: small ~7vh, hero 14vh with the layer shake; a show while one is up replaces it', () => {
  const { mount, c } = harness();
  c.show('a', 'Small');
  const layer = layerOf(mount);
  assert.equal(all(layer, 'br-callout-text')[0].style.fontSize, TIER_VH.small + 'vh');
  assert.equal(layer.anims.length, 0);
  c.show('b', 'JACKPOT', { tier: 'hero' });
  const texts = all(layer, 'br-callout-text');
  assert.equal(texts.length, 1, 'the small one is replaced');
  assert.equal(texts[0].style.fontSize, '14vh');
  assert.equal(layer.anims.length, 1, 'the hero shakes the layer');
  assert.equal(layer.anims[0].opts.duration, 320);
  assert.deepEqual(c.debug().shown.map((s) => s.tier), ['small', 'hero']);
  c.dispose();
});

/* ------------------------------------------------------------------ the word plan */
test('wordPlan: one word is 80 / 500 / 400 ms; a chain sits WORD_GAP_MS apart with hues drifting inside the house flavour', () => {
  const one = wordPlan('DROP', { seed: 7 });
  assert.equal(one.words.length, 1);
  assert.deepEqual([one.words[0].inMs, one.words[0].holdMs, one.words[0].outMs], [WORD_IN_MS, WORD_HOLD_MS, WORD_OUT_MS]);
  assert.equal(one.totalMs, WORD_MS);
  const trio = wordPlan('DROP', { chain: ['RELAX', 'SINK', 'EXTRA'], seed: 7 });
  assert.deepEqual(trio.words.map((w) => w.onsetMs), [0, WORD_GAP_MS, 2 * WORD_GAP_MS], 'three words at most, 500 ms between onsets');
  assert.equal(trio.totalMs, 2 * WORD_GAP_MS + WORD_MS);
  const hues = trio.words.map((w) => w.hue);
  assert.notEqual(hues[0], hues[1]); assert.notEqual(hues[1], hues[2]);
  assert.equal(Math.sign(hues[1] - hues[0]), Math.sign(hues[2] - hues[1]), 'the drift keeps its direction along the chain');
  for (let seed = 0; seed < 200; seed++) {
    for (const w of wordPlan('X', { chain: ['Y', 'Z'], seed }).words) {
      assert.ok(w.hue >= 270 && w.hue <= 335, 'purple to pink: ' + w.hue);
      assert.ok(w.stops.length === 1 || w.stops.length === 2);
    }
  }
  assert.deepEqual(wordPlan('DROP', { seed: 99 }), wordPlan('DROP', { seed: 99 }), 'a replay with the seed is stable');
  assert.notDeepEqual(wordPlan('DROP', { seed: 1 }).words[0].stops, wordPlan('DROP', { seed: 2 }).words[0].stops);
});

test('the reversal easter egg: seeded, about 1 in 100 per word, letters reversed and spoken as the reversed spelling', () => {
  let flips = 0;
  const N = 20000;
  for (let seed = 0; seed < N; seed++) if (wordPlan('DROP', { seed }).words[0].reversed) flips++;
  assert.ok(flips > N / REVERSE_ODDS * 0.6 && flips < N / REVERSE_ODDS * 1.5, `about 1 in ${REVERSE_ODDS}: ${flips} of ${N}`);
  const seed = findSeed((s) => wordPlan('LET GO', { seed: s }).words[0].reversed);
  const p = wordPlan('LET GO', { seed });
  assert.equal(p.words[0].shown, 'OG TEL');
  assert.equal(p.words[0].spoken, 'og tel');
  assert.equal(wordPlan('LET GO', { seed }).words[0].reversed, true, 'the same seed flips again');
  const forced = wordPlan('SINK', { chain: ['DROP'], reversed: true, seed: 3 });
  assert.ok(forced.words.every((w) => w.reversed));
  assert.equal(seededRng(5)(), seededRng(5)());
});

/* ------------------------------------------------------------------ the word on the page */
test('word: clicker, speech (rate 0.85, pitch 0.8, previous cancelled), word cue, the element mirrored on a flip, gone after the last fade', async () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  const sp = fakeSpeech();
  try {
    const { mount, c, cues } = harness();
    const seed = findSeed((s) => { const p = wordPlan('DROP', { chain: ['RELAX'], seed: s }); return !p.words[0].reversed && p.words[1].reversed; });
    const r = c.word('DROP', { chain: ['RELAX'], seed });
    const layer = layerOf(mount);
    let words = all(layer, 'br-callout-word');
    assert.equal(words.length, 1, 'the first word is up on the frame of the call');
    assert.equal(words[0].textContent, 'DROP');
    assert.equal(words[0].style.fontSize, '12vh');
    assert.match(words[0].style.backgroundImage, /linear-gradient/);
    assert.equal(words[0].anims[0].opts.duration, WORD_MS);
    assert.deepEqual(cues.map((x) => x.cue), ['clicker', 'word']);
    assert.equal(cues[1].index, 0);
    assert.deepEqual(sp.spoken, [{ text: 'DROP', rate: 0.85, pitch: 0.8 }]);
    assert.ok(sp.cancels >= 1, 'any previous utterance is cancelled first');
    mock.timers.tick(WORD_GAP_MS);
    words = all(layer, 'br-callout-word');
    assert.equal(words.length, 2, 'the second word overlaps the first one fading');
    assert.equal(words[1].textContent, 'XALER');
    assert.equal(words[1].getAttribute('data-reversed'), '');
    assert.match(words[1].anims[0].frames[0].transform, /scaleX\(-1\)/, 'mirrored');
    assert.deepEqual(sp.spoken[1], { text: 'xaler', rate: 0.7, pitch: 0.8 });
    assert.equal(cues.filter((x) => x.cue === 'word').at(-1).index, 1);
    mock.timers.tick(WORD_MS - WORD_GAP_MS);
    assert.equal(all(layer, 'br-callout-word').length, 1, 'the first word is gone at 980 ms');
    let done = false; r.done.then(() => { done = true; });
    mock.timers.tick(WORD_GAP_MS);
    await Promise.resolve();
    assert.ok(done, 'done after the last word fades');
    assert.equal(all(layer, 'br-callout-word').length, 0);
    c.dispose();
  } finally { sp.restore(); mock.timers.reset(); }
});

test('the tunnel breathes once for the chain: in 500 ms to 0.6, held to the last onset, out 500 ms, through fx-tunnel', () => {
  const one = wordPlan('A', { seed: 1 }), trio = wordPlan('A', { chain: ['B', 'C'], seed: 1 });
  assert.equal(one.tunnel.endMs, 2 * WORD_TUNNEL_MS, 'one word: 1 s');
  assert.equal(trio.tunnel.endMs, 2 * WORD_GAP_MS + WORD_TUNNEL_MS, 'a trio: up, held to the third onset, down');
  assert.equal(tunnelLevelAt(one, 250), WORD_TUNNEL_LEVEL / 2);
  assert.equal(tunnelLevelAt(one, 500), WORD_TUNNEL_LEVEL);
  assert.equal(tunnelLevelAt(one, 750), WORD_TUNNEL_LEVEL / 2);
  assert.equal(tunnelLevelAt(one, 1000), 0);
  assert.equal(tunnelLevelAt(trio, 900), WORD_TUNNEL_LEVEL, 'held between the words');
  assert.equal(tunnelLevelAt(trio, 1250), WORD_TUNNEL_LEVEL / 2);
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  try {
    const { c, tunnel } = harness();
    c.word('A', { chain: ['B', 'C'], seed: 1 });
    for (let t = 0; t < 1800; t += 50) mock.timers.tick(50);
    assert.ok(tunnel.length >= 6, 'levels posted along the breath: ' + tunnel.length);
    const peak = Math.max(...tunnel);
    assert.ok(peak >= 0.55 && peak <= 0.6, 'peaks at 0.6: ' + peak);
    assert.equal(tunnel.at(-1), 0, 'and lands back on 0');
    const log = c.debug().tunnel;
    assert.ok(log.some((e) => e.level === WORD_TUNNEL_LEVEL && e.at - 1000 >= 500 && e.at - 1000 <= 600), 'at 0.6 by 500 ms');
    assert.ok(log.filter((e) => e.level === 0).at(-1).at - 1000 >= 1500, 'not out before the third word has had its 500 ms');
    c.dispose();
  } finally { mock.timers.reset(); }
});

test('the tunnel gate off leaves the words but never posts a level', () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  try {
    const { mount, c, tunnel } = harness({ ctx: { gates: { tunnel: false } } });
    c.word('DROP', { seed: 4 });
    mock.timers.tick(1200);
    assert.deepEqual(tunnel, []);
    assert.equal(c.debug().words.length, 1);
    assert.ok(layerOf(mount));
    c.dispose();
  } finally { mock.timers.reset(); }
});

test('cancel drops the text, every word, the tunnel and the speech at once (Law VI), and dispose removes the overlay', () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  const sp = fakeSpeech();
  try {
    const { mount, c, tunnel } = harness();
    c.show('k', 'Hero', { tier: 'hero' });
    c.word('DROP', { chain: ['RELAX', 'SINK'], seed: 2 });
    c.settle();
    mock.timers.tick(600);
    const layer = layerOf(mount);
    assert.equal(all(layer, 'br-callout-text').length, 3, 'the announcer and two words up');
    assert.ok(tunnel.at(-1) > 0, 'the tunnel is in');
    const before = sp.cancels;
    c.cancel();
    assert.equal(all(layer, 'br-callout-text').length, 0);
    assert.equal(all(layer, 'br-callout-bark').length, 0);
    assert.equal(tunnel.at(-1), 0, 'tunnel 0 on the cancel frame');
    assert.equal(sp.cancels, before + 1, 'speech cancelled');
    assert.equal(c.debug().live.timers, 0, 'no timer left to bring a third word back');
    mock.timers.tick(3000);
    assert.equal(all(layer, 'br-callout-text').length, 0, 'the third word never comes');
    assert.equal(tunnel.at(-1), 0);
    c.dispose();
    assert.equal(layerOf(mount), undefined, 'the overlay is gone');
    assert.equal(c.debug().live.words, 0);
  } finally { sp.restore(); mock.timers.reset(); }
});

/* ------------------------------------------------------------------ the settle */
test('settle: the sweep band, the settle cue, EMI reacts, and the barks rotate through all eight keys', () => {
  mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 1000 });
  try {
    const reacts = [];
    const { mount, c, cues } = harness({ opts: { emi: { react: (k) => reacts.push(k) }, lex: (k, f) => (k === 'br_emi_dead_1' ? 'Localised one' : f) } });
    const r = c.settle();
    const layer = layerOf(mount);
    const [band] = all(layer, 'br-callout-sweep');
    assert.ok(band, 'the light band');
    assert.equal(band.anims[0].opts.duration, 600);
    assert.match(band.anims[0].frames[0].transform, /-110%/); assert.match(band.anims[0].frames[1].transform, /320%/);
    assert.deepEqual(cues.map((x) => x.cue), ['settle']);
    assert.equal(r.bark, 'Localised one', 'the bark goes through the lexicon');
    assert.equal(all(layer, 'br-callout-bark')[0].textContent, 'Localised one');
    assert.deepEqual(reacts, ['wink']);
    mock.timers.tick(600);
    assert.equal(all(layer, 'br-callout-sweep').length, 0, 'the band is gone at 600 ms');
    mock.timers.tick(BARK_MS - 600);
    assert.equal(all(layer, 'br-callout-bark').length, 0, 'the bark fades');
    for (let i = 1; i < 9; i++) c.settle();
    const keys = c.debug().barks.map((b) => b.key);
    assert.deepEqual(keys.slice(0, 8), DEAD_BARKS.map((b) => b.key), 'eight different lines in order');
    assert.equal(keys[8], 'br_emi_dead_1', 'then round again');
    assert.deepEqual(reacts.slice(0, 2), ['wink', 'shrug']);
    assert.ok(DEAD_BARKS.every((b) => !/(lose|loser|lost|fail|bad|sad|sorry)/i.test(b.fallback)), 'never mocking');
    c.dispose();
  } finally { mock.timers.reset(); }
});

test('an emi-idle style { trigger } reacts too, and nothing throws with no emi, no mount, no audio and no speech', () => {
  const trig = [];
  const a = createCallout({ emi: { trigger: (k) => { trig.push(k); return true; } }, cues: { play() {} } });
  a.settle(); a.settle();
  assert.deepEqual(trig, ['look', 'bow']);
  assert.equal(a.debug().barks.length, 2);
  const b = createCallout();
  b.show('x', 'Nothing here'); b.word('DROP', { chain: ['RELAX'] }); b.settle(); b.cancel(); b.dispose();
  assert.equal(b.debug().mount, false);
  assert.equal(b.debug().speech[0].spoken, false, 'speech missing is logged, not thrown');
  a.dispose();
});
