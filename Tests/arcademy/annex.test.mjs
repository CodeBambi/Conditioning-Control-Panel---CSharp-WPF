import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { annexProgress } from '../../ConditioningControlPanel/Resources/web/arcademy/core/annex-progress.js';

const roster = Array.from({ length: 10 }, (_, i) => ({ key: String(i) }));
test('Annex opens at 75 stars, including partial and missing cards', () => {
  const card = key => ({ punches: key < 7 ? 10 : key === '7' ? 4 : 0 });
  assert.equal(annexProgress(roster, card).eligible, false);
  assert.equal(annexProgress(roster, card, { gameKey: '7', card: { punches: 5 } }).eligible, true);
  assert.equal(annexProgress(roster, () => ({ punches: 8 })).eligible, true);
  assert.equal(annexProgress([], card).eligible, false);
});
test('pending mint uses max, never double counts or adds retired cards', () => {
  const read = () => ({ punches: 7 });
  assert.equal(annexProgress(roster, read, { gameKey: '0', to: 10 }).stars, 73);
  assert.equal(annexProgress(roster, read, { gameKey: 'retired', to: 10 }).stars, 70);
  assert.equal(annexProgress(roster, () => ({ punches: Infinity })).stars, 0);
  assert.equal(annexProgress(roster, () => ({ punches: 100 })).stars, 100);
});

class Node {
  constructor(tag = 'div') {
    this.tag = tag; this.children = []; this.attrs = {}; this.dataset = {}; this.events = {};
    this.style = { setProperty() {} }; this.clientWidth = 1376; this.clientHeight = 768;
    this.className = '';
    this.classList = {
      contains: x => this.className.split(' ').includes(x),
      add: (...xs) => { this.className = [...new Set([...this.className.split(' '), ...xs])].join(' '); },
      remove: (...xs) => { this.className = this.className.split(' ').filter(x => !xs.includes(x)).join(' '); },
      toggle: (x, yes) => { if (yes ?? !this.classList.contains(x)) this.classList.add(x); else this.classList.remove(x); },
    };
  }
  setAttribute(k, v) { this.attrs[k] = v; if (k === 'class') this.className = v; }
  appendChild(n) { n.remove(); this.children.push(n); n.parentNode = this; return n; }
  remove() { if (this.parentNode) this.parentNode.children = this.parentNode.children.filter(n => n !== this); this.parentNode = null; }
  set textContent(v) { for (const n of this.children) n.parentNode = null; this.children = []; this.text = v; }
  get textContent() { return this.text || ''; }
  addEventListener(k, fn) { this.events[k] = fn; }
  removeEventListener(k) { delete this.events[k]; }
  dispatchEvent(e) { this.events[e.type]?.(e); }
  querySelector(s) { return this.children.find(n => n.classList.contains(s.slice(1))) || this.children.map(n => n.querySelector(s)).find(Boolean) || null; }
  getContext() { return { createImageData: (w,h) => ({data:new Uint8ClampedArray(w*h*4)}), putImageData() {}, drawImage() {}, getImageData: () => ({data:new Uint8ClampedArray(4)}) }; }
  toDataURL() { return 'data:image/png;base64,AA=='; }
}

async function fixture(file, overrides = {}) {
  let id = 0, now = 0;
  const timers = new Map(), frames = new Map(), images = [];
  const doc = new Node(); doc.documentElement = new Node(); doc.head = new Node(); doc.hidden = false;
  doc.createElement = tag => new Node(tag); doc.createElementNS = (_, tag) => new Node(tag);
  doc.getElementById = () => null;
  const context = vm.createContext({ document: doc, window: new Node(), URL, Uint8ClampedArray,
    performance: { now: () => now }, matchMedia: () => ({ matches: false }),
    setTimeout: (fn, delay) => { timers.set(++id, {fn, at: now + delay}); return id; },
    clearTimeout: id => timers.delete(id), requestAnimationFrame: fn => { frames.set(++id, fn); return id; },
    cancelAnimationFrame: id => frames.delete(id),
    Image: class { constructor() { images.push(this); this.naturalWidth = 1; this.naturalHeight = 1; } },
    fetch: async () => ({ json: async () => ({ annex_shot_monitors2: { screens: [{name:'cam1',bbox:[0,0,100,100]}] } }) }),
  });
  const keys = ['daily_trigger','deja_vu','impulse_control','lost_and_found','the_deep_end','sort','echo','instant_recall'];
  const rooms = Object.fromEntries(keys.map(key => [key, {rect:[200,240,100,100],side:'n',door:250,nameEn:key}]));
  const deps = {
    '../shell/campus.js': { ROOMS: rooms }, '../shell/ghosts.js': { buildSprite: () => new Node() },
    '../core/rng.js': { makeRng: () => () => 0.5 }, '../core/lexicon.js': { t: (_, f) => f },
    './os.js': { createAnnexOs: () => ({root:new Node(), fit() {}, destroy() {}}), parseRuns: () => [] },
    './docs.js': { MASCOT_PAGES: [], INTAKE_SHEET: {} }, './charts.js': { renderChart: () => new Node() },
    ...overrides,
  };
  const url = new URL('../../ConditioningControlPanel/Resources/web/arcademy/annex/' + file, import.meta.url);
  const mod = new vm.SourceTextModule(await readFile(url, 'utf8'), {context, initializeImportMeta: meta => { meta.url = url.href; }});
  await mod.link(name => new vm.SyntheticModule(Object.keys(deps[name]), function() {
    for (const [k,v] of Object.entries(deps[name])) this.setExport(k,v);
  }, {context}));
  await mod.evaluate();
  return { api: mod.namespace, doc, frames, timers, images,
    frame(t) { now = t; const callbacks = [...frames.values()]; frames.clear(); callbacks.forEach(fn => fn(t)); },
    flush() { const callbacks = [...timers.values()]; timers.clear(); callbacks.forEach(({fn,at}) => { now = at; fn(); }); },
  };
}

test('camera wall stops all work on stop/destroy and cannot restart after destroy', async () => {
  const f = await fixture('cams.js'); const wall = f.api.createCamWall();
  wall.start(); assert.equal(f.frames.size, 1); assert.ok(f.timers.size > 0);
  wall.stop(); assert.equal(f.frames.size + f.timers.size, 0);
  wall.start(); wall.destroy(); wall.start(); assert.equal(f.frames.size + f.timers.size, 0);
});
test('reduced motion has a static cast and a one-second clock, no animation frames', async () => {
  const f = await fixture('cams.js'); const wall = f.api.createCamWall({lite:true});
  wall.start(); f.flush();
  assert.equal(f.frames.size, 0); assert.equal(f.timers.size, 1);
  assert.ok(Object.values(wall.tiles).every(t => t.classList.contains('an-lite')));
  wall.destroy(); assert.equal(f.timers.size, 0);
});
test('hidden camera wall pauses, resumes once, and stays stopped when hidden', async () => {
  const f = await fixture('cams.js'); const wall = f.api.createCamWall(); wall.start();
  f.doc.hidden = true; f.doc.dispatchEvent({type:'visibilitychange'});
  assert.equal(f.frames.size + f.timers.size, 0);
  f.doc.hidden = false; f.doc.dispatchEvent({type:'visibilitychange'}); f.doc.dispatchEvent({type:'visibilitychange'});
  assert.equal(f.frames.size, 1);
  wall.destroy();
});
test('late mask cannot restart departed slide; revisit reuses decoded mask', async () => {
  let starts = 0;
  const wall = {root:new Node(), tiles:{cam1:new Node()}, start(){starts++;},stop(){},destroy(){}};
  const f = await fixture('lab.js', {'./cams.js':{createCamWall:()=>wall}});
  const lab = f.api.createAnnexLab({lite:true});
  const click = label => {
    const find = n => n.attrs['aria-label'] === label ? n : n.children.map(find).find(Boolean);
    find(lab.root).dispatchEvent({type:'click'});
  };
  click('the monitors');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.images.length, 1);
  lab.escapeStep(); // descent
  lab.escapeStep(); // monitor close-up
  f.images[0].onload(); await new Promise(resolve => setImmediate(resolve));
  assert.equal(starts, 0);
  click('the monitors'); await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.images.length, 1); assert.equal(starts, 1);
  lab.destroy();
});
