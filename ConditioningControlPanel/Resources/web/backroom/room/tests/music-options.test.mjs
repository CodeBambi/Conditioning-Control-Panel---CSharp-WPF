import test from 'node:test';
import assert from 'node:assert/strict';
import { createHud } from '../hud.js';

function mount({ phone = false, levels, media } = {}) {
  const previousDocument = globalThis.document, previousWindow = globalThis.window, nodes = [];
  class Node {
    constructor(tag) { this.tagName = tag; this.children = []; this.attributes = {}; this.style = {}; this.dataset = {}; this.events = {}; nodes.push(this); }
    classList = { toggle() {}, add() {}, remove() {} };
    append(...nodes) { this.children.push(...nodes); }
    appendChild(node) { this.append(node); }
    setAttribute(key, value) { this.attributes[key] = value; }
    getAttribute(key) { return this.attributes[key]; }
    addEventListener(key, callback) { this.events[key] = callback; }
    contains() { return false; }
  }
  globalThis.document = { createElement: tag => new Node(tag), addEventListener() {}, documentElement: new Node('html') };
  globalThis.window = phone ? { __brOptions: {} } : {};
  let listener = null, removed = false;
  const music = { volume: .15, setVolume(value) { this.volume = value; } };
  const quality = { mode: 'auto', setMode(value) { this.mode = value; listener?.(); }, subscribe(fn) { listener = fn; return () => { removed = true; }; } };
  const seen = { preview: [], commit: [], option: [] };
  const state = levels || { sub: 1, sfx: 1, music: .15 };
  const levelsApi = {
    get sub() { return state.sub; }, get sfx() { return state.sfx; }, get music() { return state.music; },
    preview(key, v) { state[key] = v; seen.preview.push([key, v]); },
    commit(key, v) { state[key] = v; seen.commit.push([key, v]); },
  };
  const hud = createHud({ root: new Node('root'), lex: (_, fallback) => fallback, music, quality,
    levels: phone ? undefined : levelsApi, onOption: (key, value) => seen.option.push([key, value]) });
  if (media) hud.options({ intensityChoice: 'normal', tunnel: true, melt: true, media });
  const byLabel = (label) => nodes.find(n => n.attributes['aria-label'] === label);
  return { hud, nodes, byLabel, music, quality, state, seen, removed: () => removed,
    restore() { globalThis.document = previousDocument; globalThis.window = previousWindow; } };
}

const MEDIA = { source: 'online', effective: 'online', subs: ['hypno', 'bimbofication'], off: ['bimbofication'], cap: 8, consented: true };

test('the room carries its own three levels, previewing on drag and committing on release', () => {
  const r = mount({ levels: { sub: .4, sfx: .9, music: .15 } });
  try {
    const sub = r.byLabel('Subliminal'), sfx = r.byLabel('Game sounds'), general = r.byLabel('Music and room');
    assert.equal(sub.value, '0.4'); assert.equal(sfx.value, '0.9'); assert.equal(general.value, '0.15');

    // Dragging is heard but not stored: eighty pointer moves must not be eighty settings writes.
    sub.value = '.25'; sub.events.input();
    assert.deepEqual(r.seen.preview, [['sub', .25]]);
    assert.deepEqual(r.seen.commit, []);
    sub.events.change();
    assert.deepEqual(r.seen.commit, [['sub', .25]]);

    general.value = '.5'; general.events.input(); general.events.change();
    assert.deepEqual(r.seen.commit.at(-1), ['music', .5]);
    r.hud.stop();
  } finally { r.restore(); }
});

test('a host frame repaints the levels, but never the slider the player is holding', () => {
  const r = mount({ levels: { sub: 1, sfx: 1, music: .15 } });
  try {
    r.hud.options({ intensityChoice: 'normal', tunnel: true, melt: true, levels: { sub: .2, sfx: .3, music: .4 } });
    assert.equal(r.byLabel('Subliminal').value, '0.2');
    assert.equal(r.byLabel('Game sounds').value, '0.3');
    assert.equal(r.byLabel('Music and room').value, '0.4');

    const held = r.byLabel('Subliminal');
    globalThis.document.activeElement = held;
    r.hud.options({ intensityChoice: 'normal', tunnel: true, melt: true, levels: { sub: .77, sfx: .3, music: .4 } });
    assert.equal(held.value, '0.2', 'a frame must not yank the handle out from under the pointer');
    r.hud.stop();
  } finally { r.restore(); }
});

test('quality still synchronizes and still cleans up', () => {
  const r = mount();
  try {
    const quality = r.byLabel('Quality');
    assert.equal(quality.value, 'auto');
    quality.value = 'performance'; quality.events.change(); assert.equal(r.quality.mode, 'performance');
    r.quality.setMode('full'); assert.equal(quality.value, 'full');
    r.hud.stop(); assert.equal(r.removed(), true);
  } finally { r.restore(); }
});

test('the picture picker offers every source and reports the press to the host', () => {
  const r = mount({ media: MEDIA });
  try {
    const segs = r.nodes.filter(n => ['auto', 'local', 'online', 'mixed', 'bundled'].includes(n.dataset.value));
    assert.deepEqual(segs.map(n => n.dataset.value), ['auto', 'local', 'online', 'mixed', 'bundled']);
    assert.equal(segs.find(n => n.dataset.value === 'online').attributes['aria-pressed'], 'true');
    segs.find(n => n.dataset.value === 'bundled').events.click();
    assert.deepEqual(r.seen.option.at(-1), ['mediaSource', 'bundled']);
    r.hud.stop();
  } finally { r.restore(); }
});

test('Scrolller is shown refused, not hidden, when online media has no consent', () => {
  const r = mount({ media: { ...MEDIA, consented: false, effective: 'local' } });
  try {
    const seg = (v) => r.nodes.find(n => n.dataset.value === v);
    assert.equal(seg('online').disabled, true);
    assert.equal(seg('mixed').disabled, true);
    assert.equal(seg('local').disabled, false, 'the local sources stay usable');
    r.hud.stop();
  } finally { r.restore(); }
});

test('niches render as individually toggleable and removable pills, disabled ones kept', () => {
  const r = mount({ media: MEDIA });
  try {
    const toggles = r.nodes.filter(n => typeof n.textContent === 'string' && n.textContent.startsWith('r/')
      && n.tagName === 'button');
    assert.deepEqual(toggles.map(n => n.textContent), ['r/hypno', 'r/bimbofication']);
    assert.equal(toggles[0].attributes['aria-pressed'], 'true');
    assert.equal(toggles[1].attributes['aria-pressed'], 'false', 'a disabled niche stays visible and reads off');

    toggles[1].events.click();
    assert.deepEqual(r.seen.option.at(-1), ['mediaSubToggle', 'bimbofication']);
    r.byLabel('Remove r/hypno').events.click();
    assert.deepEqual(r.seen.option.at(-1), ['mediaSubRemove', 'hypno']);
    r.hud.stop();
  } finally { r.restore(); }
});

test('the niche field takes a pasted url, and refuses junk, dupes and an over-full list', () => {
  const r = mount({ media: MEDIA });
  try {
    const field = r.byLabel('Add a niche');
    const form = r.nodes.find(n => n.tagName === 'form');
    const submit = (text) => { field.value = text; form.events.submit({ preventDefault() {} }); };

    submit('https://www.reddit.com/r/Dronification/');
    assert.deepEqual(r.seen.option.at(-1), ['mediaSubAdd', 'Dronification'], 'a pasted url is just a name');
    assert.equal(field.value, '', 'the field clears so the next one can be typed straight in');

    const before = r.seen.option.length;
    submit('no spaces here');
    submit('hypno');            // already added, case-insensitively
    submit('HYPNO');
    assert.equal(r.seen.option.length, before, 'junk and dupes are answered in the room, not over the bridge');

    r.hud.options({ intensityChoice: 'normal', tunnel: true, melt: true, media: { ...MEDIA, cap: 2 } });
    submit('bambisleep');
    assert.equal(r.seen.option.length, before, 'and the cap holds');
    r.hud.stop();
  } finally { r.restore(); }
});

test('the niche editor is hidden unless the effective source actually reads Scrolller', () => {
  const r = mount({ media: { ...MEDIA, source: 'local', effective: 'local' } });
  try {
    const field = r.byLabel('Add a niche');
    // The wrap is the field's grandparent: field -> form -> wrap.
    const wrap = r.nodes.find(n => n.children.some(c => Array.isArray(c.children) && c.children.includes(field)));
    assert.equal(wrap.hidden, true);
    r.hud.options({ intensityChoice: 'normal', tunnel: true, melt: true, media: MEDIA });
    assert.equal(wrap.hidden, false);
    r.hud.stop();
  } finally { r.restore(); }
});

test('phone shell keeps its own controls without duplicate room rows', () => {
  const r = mount({ phone: true });
  try {
    for (const label of ['Music and room', 'Subliminal', 'Game sounds', 'Quality']) {
      assert.equal(r.byLabel(label), undefined, label + ' belongs to the shell sheet on the web');
    }
    r.hud.stop();
  } finally { r.restore(); }
});
