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
    this.style = { setProperty(k, v) { this[k] = v; } }; this.clientWidth = 1376; this.clientHeight = 768;
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

const CLOSE_UP_ONLY = { annex_shot_monitors2: { screens: [{name:'cam1',bbox:[0,0,100,100]}] } };

async function fixture(file, overrides = {}, quadFile = CLOSE_UP_ONLY) {
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
    CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init && init.detail; } },
    fetch: async () => ({ json: async () => quadFile }),
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

/* ---- the wide shot's screens, the wall between slides, one door (2026-09-30) ---- */

const BOTH_SHOTS = {
  annex_shot_monitors2: { screens: [
    {name:'cam1',bbox:[0,0,100,100]}, {name:'cam8',bbox:[5,5,5,5]}, {name:'laptop',bbox:[40,60,20,10]},
  ] },
  annex_pixel_establishing2: { screens: [{name:'est_cam1',bbox:[1,2,3,4]}, {name:'est_laptop',bbox:[5,6,7,8]}] },
};
const tick = () => new Promise(resolve => setImmediate(resolve));
const findNode = (n, pred) => pred(n) ? n : n.children.map(c => findNode(c, pred)).find(Boolean) || null;
const press = (lab, label) => findNode(lab.root, n => n.attrs['aria-label'] === label).dispatchEvent({type:'click'});
function spyWall(calls, ids = ['cam1', 'cam8', 'laptop']) {
  const tiles = Object.fromEntries(ids.map(id => [id, new Node()]));
  return { root:new Node(), tiles, start() { calls.push('start'); }, stop() { calls.push('stop'); }, destroy() {} };
}
const maskOf = (f, file) => f.images.find(i => String(i.src).endsWith(file));

test('the wide shot runs the same wall through its own mask and quads, not chroma green', async () => {
  const calls = []; const wall = spyWall(calls);
  const f = await fixture('lab.js', {'./cams.js':{createCamWall:()=>wall}}, BOTH_SHOTS);
  const lab = f.api.createAnnexLab({lite:true});
  await tick();
  assert.ok(maskOf(f, 'lab-wide-mask.png'), 'the wide shot decodes its own mask');
  assert.ok(maskOf(f, 'lab-monitors-mask.png'), 'the close-up mask is warmed before anyone walks up');
  assert.equal(wall.tiles.cam1.style.left, '1px');
  assert.equal(wall.tiles.cam1.style.height, '4px');
  assert.equal(wall.tiles.laptop.style.top, '6px');
  const feeds = wall.tiles.cam1.parentNode;
  assert.ok(feeds.classList.contains('al-feeds-wide') && feeds.classList.contains('is-waiting'));
  maskOf(f, 'lab-wide-mask.png').onload(); await tick();
  assert.ok(!feeds.classList.contains('is-waiting'));
  assert.deepEqual(calls, ['start']);
  lab.destroy();
});
test('one wall runs across the wide shot and the close-up, and stops anywhere else', async () => {
  const calls = []; const wall = spyWall(calls);
  const f = await fixture('lab.js', {'./cams.js':{createCamWall:()=>wall}}, BOTH_SHOTS);
  const lab = f.api.createAnnexLab({lite:true});
  await tick(); f.images.forEach(i => i.onload()); await tick();
  lab.escapeStep(); // descent
  press(lab, 'the monitors'); await tick();
  assert.equal(wall.tiles.cam1.style.left, '0px');
  assert.equal(wall.tiles.cam8.style.left, '581px', 'the close-up keeps its proven cam8 glass');
  assert.ok(wall.tiles.cam1.parentNode.classList.contains('al-feeds-monitors'));
  lab.escapeStep(); await tick(); // back to the wide shot
  assert.equal(wall.tiles.cam1.style.left, '1px');
  assert.ok(!calls.includes('stop'), 'monitors that were on the whole time do not power down between slides');
  press(lab, 'the desk');
  assert.deepEqual(calls.filter(c => c === 'stop'), ['stop']);
  lab.destroy();
});
test('a slide with no screens on file stops the wall the other slide left running', async () => {
  const calls = []; const wall = spyWall(calls, ['cam1']);
  const f = await fixture('lab.js', {'./cams.js':{createCamWall:()=>wall}}); // close-up quads only
  const lab = f.api.createAnnexLab({lite:true});
  await tick(); lab.escapeStep(); // descent
  press(lab, 'the monitors'); maskOf(f, 'lab-monitors-mask.png').onload(); await tick();
  assert.deepEqual(calls, ['start']);
  lab.escapeStep(); // the wide shot has no screens on file here
  assert.deepEqual(calls, ['start', 'stop']);
  lab.destroy();
});
test('stepping back to the wide shot plays one door, not two', async () => {
  const f = await fixture('lab.js', {'./cams.js':{createCamWall:()=>spyWall([])}});
  const cues = [];
  f.doc.addEventListener('arcademy-sfx', e => cues.push(e.detail.name));
  const lab = f.api.createAnnexLab({lite:true});
  await tick(); lab.escapeStep(); // descent
  press(lab, 'the desk'); cues.length = 0;
  findNode(lab.root, n => n.classList.contains('al-back')).dispatchEvent({type:'click'});
  assert.deepEqual(cues.filter(c => c === 'door'), ['door'], 'the step-back pill');
  press(lab, 'the desk'); cues.length = 0;
  lab.escapeStep();
  assert.deepEqual(cues.filter(c => c === 'door'), ['door'], 'Esc');
  lab.destroy();
});

const readWeb = rel => readFile(new URL('../../ConditioningControlPanel/Resources/web/arcademy/' + rel, import.meta.url), 'utf8');
const stripCss = css => css.replace(/\/\*[\s\S]*?\*\//g, '');
test('no campus-plan box rule outside the hub can move an Annex feed', async () => {
  /* The feeds wear `campus-plan` for the [data-game] fills. A rule that sizes,
   * places or transforms the plan ELEMENT is the hub's business and must say
   * `.campus-stage`: the 16:9 pillarbox once slid every feed half a screen. */
  const BOX = /(?:^|;)\s*(left|right|top|bottom|inset|width|height|transform|aspect-ratio)\s*:\s*([^;]+)/g;
  const FULL = new Set(['left:0', 'right:0', 'top:0', 'bottom:0', 'inset:0', 'width:100%', 'height:100%']);
  let checked = 0;
  for (const [, sel, body] of stripCss(await readWeb('styles.css')).matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    for (const s of sel.split(',').map(x => x.trim())) {
      if (!/\.campus-plan$/.test(s) || /\.campus-stage\b/.test(s)) continue;
      for (const [, prop, val] of body.matchAll(BOX)) {
        checked++;
        const decl = prop + ':' + val.trim().replace(/\s+/g, '');
        assert.ok(FULL.has(decl), `"${s}" sets ${decl} on every campus-plan, Annex feeds included`);
      }
    }
  }
  assert.ok(checked > 0, 'the base plan rule was found and read');
  assert.match(stripCss(await readWeb('annex/cams.css')), /\.cam-tile svg\.campus-plan\s*\{[^}]*transform:\s*none/);
});
test('on a phone every Annex hotspot is visible without hovering', async () => {
  const css = stripCss(await readWeb('annex/lab.css'));
  assert.match(css, /html\.arc-mobile \.al-hot\s*\{[^}]*box-shadow/);
  assert.match(css, /html\.arc-mobile \.al-hot \.al-hot-tag\s*\{[^}]*opacity:\s*1/);
});
test('a name tag may outgrow its box, but it is never cut and never leaves the plane', async () => {
  const f = await fixture('lab.js', {'./cams.js':{createCamWall:()=>spyWall([])}});
  const lab = f.api.createAnnexLab({lite:true});
  await tick();
  const hot = label => findNode(lab.root, n => n.attrs['aria-label'] === label);
  /* the monitors box is [10, 90, 325, 480] and the stairs [1075, 235, 130, 195], on a 1376 plane */
  assert.equal(hot('the monitors').style['--tag-lo'], '-164.5px');
  assert.equal(hot('the monitors').style['--tag-hi'], '1195.5px');
  assert.equal(hot('the stairs').style['--tag-hi'], '228px');
  const css = stripCss(await readWeb('annex/lab.css'));
  assert.match(css, /\.al-hot \.al-hot-tag\s*\{[^}]*translateX\(clamp\(var\(--tag-lo/);
  for (const [, sel, body] of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!sel.includes('al-hot-tag')) continue;
    assert.doesNotMatch(body, /overflow\s*:\s*hidden|text-overflow|max-width/, `${sel.trim()} cuts the name short`);
  }
  lab.destroy();
});
