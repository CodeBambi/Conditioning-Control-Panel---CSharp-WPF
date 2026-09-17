import test from 'node:test';
import assert from 'node:assert/strict';
import { createHud } from '../hud.js';

function mount({ phone = false } = {}) {
  const previousDocument = globalThis.document, previousWindow = globalThis.window, nodes = [];
  class Node {
    constructor(tag) { this.tagName = tag; this.children = []; this.attributes = {}; this.style = {}; this.dataset = {}; this.events = {}; nodes.push(this); }
    classList = { toggle() {}, add() {}, remove() {} };
    append(...nodes) { this.children.push(...nodes); }
    appendChild(node) { this.append(node); }
    setAttribute(key, value) { this.attributes[key] = value; }
    getAttribute(key) { return this.attributes[key]; }
    addEventListener(key, callback) { this.events[key] = callback; }
  }
  globalThis.document = { createElement: tag => new Node(tag), addEventListener() {}, documentElement: new Node('html') };
  globalThis.window = phone ? { __brOptions: {} } : {};
  let listener = null, removed = false;
  const music = { volume: .15, setVolume(value) { this.volume = value; } };
  const quality = { mode: 'auto', setMode(value) { this.mode = value; listener?.(); }, subscribe(fn) { listener = fn; return () => { removed = true; }; } };
  const hud = createHud({ root: new Node('root'), lex: (_, fallback) => fallback, music, quality });
  return { hud, nodes, music, quality, removed: () => removed, restore() { globalThis.document = previousDocument; globalThis.window = previousWindow; } };
}

test('desktop options expose music volume and synchronized quality with cleanup', () => {
  const r = mount();
  try {
    const music = r.nodes.find(node => node.attributes['aria-label'] === 'Music');
    const quality = r.nodes.find(node => node.attributes['aria-label'] === 'Quality');
    assert.equal(music.value, '0.15'); assert.equal(quality.value, 'auto');
    music.value = '.28'; music.events.input(); assert.equal(r.music.volume, .28);
    quality.value = 'performance'; quality.events.change(); assert.equal(r.quality.mode, 'performance');
    r.quality.setMode('full'); assert.equal(quality.value, 'full');
    r.hud.stop(); assert.equal(r.removed(), true);
  } finally { r.restore(); }
});

test('phone shell keeps its own controls without duplicate room rows', () => {
  const r = mount({ phone: true });
  try {
    assert.equal(r.nodes.some(node => node.attributes['aria-label'] === 'Music' || node.attributes['aria-label'] === 'Quality'), false);
    r.hud.stop();
  } finally { r.restore(); }
});
