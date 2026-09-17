import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL('./__phone-fx.js', import.meta.url), 'utf8').replace(/\bimport\(/g, '__import(');
function rig({ performanceMode = false, preview } = {}) {
  let clock = 0, serial = 0, peakImages = 0;
  const timers = new Map(), listeners = new Map(), nodes = [], frames = new Map();
  class Node {
    constructor(tag) { this.tagName = tag.toUpperCase(); this.children = []; this.style = {}; this.dataset = {}; this.isConnected = false; this.className = ''; this.attrs = {}; nodes.push(this); }
    classList = { contains: name => this.className.split(' ').includes(name), add: name => { this.className += ' ' + name; }, remove: name => { this.className = this.className.split(' ').filter(x => x !== name).join(' '); } };
    append(node) { this.children.push(node); node.parent = this; node.isConnected = true; if (this.id === '__fx') peakImages = Math.max(peakImages, this.querySelectorAll('img').length); }
    remove() { this.isConnected = false; if (this.parent) this.parent.children = this.parent.children.filter(n => n !== this); }
    removeAttribute(key) { delete this[key]; }
    setAttribute(key, value) { this.attrs[key] = value; }
    querySelectorAll(selector) { return this.children.filter(n => selector === 'img' ? n.tagName === 'IMG' : true); }
    querySelector(selector) { this.parts ||= {}; return this.parts[selector] ||= new Node(selector); }
    addEventListener() {}
    animate() { return { cancel() {} }; }
    getAnimations() { return []; }
  }
  const body = new Node('body'), stage = new Node('main'); stage.style.filter = '';
  const document = { body, documentElement: new Node('html'), hidden: false,
    createElement: tag => new Node(tag), createElementNS: (_, tag) => new Node(tag),
    querySelector: selector => selector === '#br-stage' ? stage : null,
    querySelectorAll: () => [], addEventListener: (name, callback) => listeners.set(name, callback) };
  const storage = { getItem: key => key === 'br.media.v1' ? '{"mode":"bundled"}' : null, setItem() {} };
  const window = { __backroomQuality: { performance: performanceMode }, addEventListener: (name, callback) => listeners.set(name, callback), dispatchEvent() {} };
  const context = { window, document, localStorage: storage, sessionStorage: storage,
    location: { search: '', href: 'https://preview.example/backroom/index.html', origin: 'https://preview.example' },
    URL, URLSearchParams, Event, console, innerWidth: 400, innerHeight: 800, matchMedia: () => ({ matches: false }),
    performance: { now: () => clock },
    setTimeout: (fn, delay = 0) => { const id = ++serial; timers.set(id, { fn, at: clock + delay }); return id; },
    clearTimeout: id => timers.delete(id), requestAnimationFrame: fn => { const id = ++serial; frames.set(id, fn); return id; }, cancelAnimationFrame: id => frames.delete(id),
    __import: path => path.includes('flash-preview') ? (preview || Promise.resolve(null)) : new Promise(() => {}) };
  vm.runInNewContext(source, context);
  async function advance(ms) {
    const until = clock + ms;
    while (true) {
      const next = [...timers].filter(([, t]) => t.at <= until).sort((a, b) => a[1].at - b[1].at)[0];
      if (!next) break;
      clock = next[1].at; timers.delete(next[0]); next[1].fn(); await Promise.resolve(); await Promise.resolve();
    }
    clock = until; await Promise.resolve(); await Promise.resolve();
  }
  const root = () => nodes.find(node => node.id === '__fx');
  return { window, document, timers, frames, nodes, root, advance, stage, peak: () => peakImages,
    hide() { document.hidden = true; listeners.get('visibilitychange')(); },
    frame(now) { clock = now; const pending = [...frames.values()]; frames.clear(); pending.forEach(fn => fn(now)); } };
}

test('cancellation prevents delayed jackpot recipes and clears all effect work', async () => {
  const r = rig(); r.window.__renderFx({ fxId: 'fx.jackpot' });
  assert.ok(r.timers.size > 0);
  r.window.__fxCancelAll(); await r.advance(20000);
  assert.equal(r.timers.size, 0); assert.equal(r.frames.size, 0); assert.equal(r.root().children.length, 0);
});

test('cancellation during an async flash import cannot resurrect an overlay', async () => {
  let resolvePreview; const r = rig({ preview: new Promise(resolve => { resolvePreview = resolve; }) });
  r.window.__renderFx({ fxId: 'fx.gif_burst' }); await r.advance(0);
  r.window.__fxCancelAll(); resolvePreview(null); await r.advance(20000);
  assert.equal(r.root().children.length, 0); assert.equal(r.timers.size, 0);
});

test('Performance bounds concurrent pictures while the ordinary show keeps its density', async () => {
  const lite = rig({ performanceMode: true }), full = rig();
  for (const r of [lite, full]) { r.window.__renderFx({ fxId: 'fx.gif_storm' }); await r.advance(2500); }
  assert.equal(lite.peak(), 4); assert.ok(full.peak() > 4);
  assert.ok(lite.root().children.some(n => n.tagName === 'IMG'));
});

test('hidden pages cancel effects and refuse new recipes', async () => {
  const r = rig(); r.window.__renderFx({ fxId: 'fx.gif_storm' }); await r.advance(100);
  r.hide(); await r.advance(10000);
  assert.equal(r.root().children.length, 0); assert.equal(r.timers.size, 0);
  assert.equal(r.window.__renderFx({ fxId: 'fx.gif_storm' }).fired.length, 0);
});

test('Performance melt still flows and cancellation restores the scene filter', () => {
  const r = rig({ performanceMode: true }); r.window.__renderFx({ fxId: 'fx.melt' });
  r.frame(100); const noise = r.nodes.find(n => n.tagName === 'FETURBULENCE');
  const first = noise.attrs.baseFrequency;
  r.frame(300); assert.notEqual(noise.attrs.baseFrequency, first); assert.equal(noise.attrs.numOctaves, '1');
  assert.ok(Number(r.nodes.find(n => n.tagName === 'FEDISPLACEMENTMAP').attrs.scale) > 0);
  r.window.__fxCancelAll(); assert.equal(r.stage.style.filter, ''); assert.equal(r.frames.size, 0);
});
