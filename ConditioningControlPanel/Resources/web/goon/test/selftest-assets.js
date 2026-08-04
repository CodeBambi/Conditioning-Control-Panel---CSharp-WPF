// Self-contained sanity pass over the ASSETS tier — ui/assetsStore.js and
// ui/screens/assets.js — driven against a minimal DOM stub and a fake bridge.
//
//   node Resources/web/goon/test/selftest-assets.js
//
// What it proves:
//   1. every module in the tier imports clean under node (an import throw in
//      WebView2 is a SILENT INFINITE LOADER — there are no devtools);
//   2. the screen is actually reachable: index.html ships #scr-assets, the
//      router maps it, boot.js registers it and hands out actions.goAssets;
//   3. every bridge handler type across boot.js + assetsStore.js is unique
//      (bridge.on THROWS on a duplicate, at wiring time, on purpose);
//   4. the progress ring re-declares --gg-deco-play: running, so the effect
//      armor cannot freeze the one surface that reports live work;
//   5. every S.assets.* key the screen reads exists in ui/strings.js (an
//      undefined string renders the word "undefined" at the player);
//   6. filenames are user data and the screen never hands them to markup;
//   7. the store logic: paged list assembly, progress merge, the filter, the
//      ETA maths, the cap clamp and the 720p encoder probe;
//   8. the screen: the standalone answer arrives WITHOUT a spinner, the grid
//      virtualizes (no media off-screen, ≤24 live previews) and the toolbar
//      says what the state actually is.

// ------------------------------------------------------------------ DOM stub

function installDom() {
  function makeStyle() {
    const s = {};
    s.setProperty = (k, v) => { s[k] = v; };
    s.removeProperty = (k) => { delete s[k]; };
    return s;
  }

  function makeNode(tagName) {
    const kids = [];
    const map = new Map();
    const classes = new Set();
    const attrs = new Map();
    const node = {
      tagName: String(tagName || 'div').toUpperCase(),
      nodeType: 1,
      children: kids,
      childNodes: kids,
      parentNode: null,
      isConnected: true,
      hidden: false,
      style: makeStyle(),
      dataset: {},
      value: '',
      disabled: false,
      textContent: '',
      title: '',
      get className() { return Array.from(classes).join(' '); },
      set className(v) { classes.clear(); String(v || '').split(/\s+/).filter(Boolean).forEach((c) => classes.add(c)); },
      classList: {
        add: (...c) => c.forEach((x) => classes.add(x)),
        remove: (...c) => c.forEach((x) => classes.delete(x)),
        toggle: (c, on) => (on ? classes.add(c) : classes.delete(c)),
        contains: (c) => classes.has(c),
      },
      appendChild(child) { if (child) { child.parentNode = node; kids.push(child); } return child; },
      append(...c) { c.forEach((x) => node.appendChild(x)); },
      prepend(child) { if (child) { child.parentNode = node; kids.unshift(child); } return child; },
      removeChild(child) { const i = kids.indexOf(child); if (i >= 0) kids.splice(i, 1); return child; },
      remove() { if (node.parentNode) node.parentNode.removeChild(node); node.parentNode = null; node.isConnected = false; },
      replaceChildren(...c) { kids.length = 0; c.forEach((x) => node.appendChild(x)); },
      replaceChild(next, old) { const i = kids.indexOf(old); if (i >= 0) { kids[i] = next; next.parentNode = node; } return old; },
      contains(other) {
        if (other === node) return true;
        for (const k of kids) if (k && typeof k.contains === 'function' && k.contains(other)) return true;
        return false;
      },
      // setAttribute('class') really does move classList in a browser, and the
      // SVG ring is built with createElementNS + setAttribute — so the stub has
      // to keep the two in step or the ring is invisible to the tests only.
      setAttribute(k, v) { attrs.set(k, String(v)); if (k === 'class') node.className = String(v); },
      getAttribute(k) { return attrs.has(k) ? attrs.get(k) : null; },
      removeAttribute(k) { attrs.delete(k); },
      hasAttribute(k) { return attrs.has(k); },
      addEventListener(type, fn) { if (!map.has(type)) map.set(type, new Set()); map.get(type).add(fn); },
      removeEventListener(type, fn) { const s = map.get(type); if (s) s.delete(fn); },
      dispatchEvent(evt) {
        const s = map.get(evt && evt.type);
        if (s) for (const fn of Array.from(s)) { try { fn(evt); } catch (_e) { /* ignore */ } }
        return true;
      },
      focus() {}, blur() {},
      _listeners: map,
      _classes: classes,
      _attrs: attrs,
    };
    return node;
  }

  const doc = makeNode('#document');
  doc.documentElement = makeNode('html');
  doc.body = makeNode('body');
  doc.activeElement = null;
  const byId = new Map();
  for (const id of ['gg-modal', 'gg-drawer', 'scr-assets', 'scr-title']) {
    const n = makeNode('div');
    n.id = id;
    n.hidden = true;
    byId.set(id, n);
    doc.body.appendChild(n);
  }
  doc.createElement = (tag) => makeNode(tag);
  doc.createElementNS = (_ns, tag) => makeNode(tag);
  doc.createTextNode = (t) => { const n = makeNode('#text'); n.textContent = String(t); return n; };
  doc.getElementById = (id) => byId.get(id) || null;

  const win = makeNode('window');
  win.innerWidth = 1280;
  win.innerHeight = 720;

  globalThis.document = doc;
  globalThis.window = win;
  const rafs = new Set();
  globalThis.requestAnimationFrame = (fn) => {
    const id = setTimeout(() => { rafs.delete(id); try { fn(Date.now()); } catch (_e) { /* ignore */ } }, 8);
    rafs.add(id);
    return id;
  };
  globalThis.cancelAnimationFrame = (id) => { clearTimeout(id); rafs.delete(id); };
  return { doc, win, byId, makeNode };
}

const dom = installDom();

/** A drivable IntersectionObserver: nothing is "seen" until the test says so. */
class FakeIO {
  constructor(cb, opts) { this.cb = cb; this.opts = opts || {}; this.targets = new Set(); FakeIO.all.push(this); }
  observe(t) { this.targets.add(t); }
  unobserve(t) { this.targets.delete(t); }
  disconnect() { this.targets.clear(); this.disconnected = true; }
  /** Fire `isIntersecting` for the first n observed targets (all, by default). */
  intersect(n) {
    const list = Array.from(this.targets).slice(0, n === undefined ? undefined : n);
    this.cb(list.map((t) => ({ target: t, isIntersecting: true })), this);
  }
  leave(targets) {
    this.cb([].concat(targets).map((t) => ({ target: t, isIntersecting: false })), this);
  }
}
FakeIO.all = [];

// ------------------------------------------------------------------ harness

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function findAll(root, className) {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node._classes && node._classes.has(className)) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
}
const findOne = (root, className) => findAll(root, className)[0] || null;
const findTag = (root, tag) => {
  const out = [];
  (function walk(node) {
    if (!node) return;
    if (node.tagName === tag.toUpperCase()) out.push(node);
    for (const kid of node.children || []) walk(kid);
  })(root);
  return out;
};

const fs = await import('node:fs/promises');
const urlMod = await import('node:url');
const read = (p) => fs.readFile(urlMod.fileURLToPath(new URL(p, import.meta.url)), 'utf8');

// ------------------------------------------------------------------ imports
// 1. THE IMPORT SWEEP. Nothing below matters if one of these throws.

const storeMod = await import('../ui/assetsStore.js');
const screenMod = await import('../ui/screens/assets.js');
const titleMod = await import('../ui/screens/title.js');
const routerMod = await import('../ui/router.js');
const stringsMod = await import('../ui/strings.js');
const sheetsMod = await import('../ui/sheets.js');
const zipMod = await import('../ui/zipReader.js');

const {
  createAssetsStore, probeEncode, clampCapBytes, capGb, formatBytes, formatUsage,
  etaMinutes, matchesFilter, normalizeState, pendingInputBytes,
  CAP_MIN_BYTES, CAP_MAX_BYTES, CAP_DEFAULT_BYTES, MAX_COMPRESS_IDS, FILTERS, GB,
} = storeMod;
const { S } = stringsMod;

ok(typeof createAssetsStore === 'function', 'ui/assetsStore.js exports createAssetsStore');
ok(typeof probeEncode === 'function', 'and probeEncode');
ok(typeof screenMod.mount === 'function', 'ui/screens/assets.js exports mount()');
ok(screenMod.default && typeof screenMod.default.mount === 'function', 'and the default {mount} the router takes');
ok(typeof titleMod.mount === 'function', 'ui/screens/title.js still mounts after the menu edit');
ok(typeof sheetsMod.createSheets === 'function', 'ui/sheets.js still imports (the confirm sheets ride it)');
ok(typeof zipMod.readZipMedia === 'function', 'ui/zipReader.js imports clean and exports readZipMedia()');

{
  // The archive reader must cost NOTHING until a player picks a zip, and it
  // must never be the module that takes the page down at import.
  const src = await read('../ui/zipReader.js');
  ok(!/^\s*import\s[^\n]*fflate/m.test(src), 'zipReader loads fflate LAZILY — 90 KB does not ride every page load');
  ok(/import\('\.\.\/vendor\/fflate\/fflate\.module\.js'\)/.test(src), 'by dynamic import, from the vendored copy, never a CDN');
  ok(/\.catch\(\(\) => null\)/.test(src), 'and a lib that will not load is an answer, not a throw');
  const lic = await read('../vendor/fflate/LICENSE');
  ok(/MIT License/.test(lic), 'the vendored fflate ships its MIT license, like vendor/mp4-muxer');
}

// ------------------------------------------------------- 2. reachability

{
  const html = await read('../index.html');
  const routerSrc = await read('../ui/router.js');
  const bootSrc = await read('../boot.js');

  ok(/id="scr-assets"/.test(html), 'index.html ships the ninth section, #scr-assets');
  ok(/id="scr-assets"[^>]*class="gg-screen"/.test(html), 'with the .gg-screen class the router hides');
  ok(/id="scr-assets"[^>]*hidden/.test(html), 'and hidden, like every other screen');
  ok(routerMod.SCREEN_IDS.assets === 'scr-assets', 'router SCREEN_IDS maps assets -> scr-assets',
    String(routerMod.SCREEN_IDS.assets));
  ok(/assets:\s*'scr-assets'/.test(routerSrc), 'and says so in the source, not only at runtime');

  // Every mapped screen must have a section, or router.show() returns null and
  // the menu item is a button that does nothing.
  for (const [name, id] of Object.entries(routerMod.SCREEN_IDS)) {
    ok(html.indexOf('id="' + id + '"') >= 0, 'index.html has a section for screen "' + name + '"', id);
  }

  ok(/\bassets:\s*assetsScreen\b/.test(bootSrc), 'boot.js registers the screen in the router map');
  ok(/import \* as assetsScreen from '\.\/ui\/screens\/assets\.js'/.test(bootSrc), 'and imports it statically');
  ok(/goAssets\s*\(args\)\s*\{/.test(bootSrc), 'boot.js actions.goAssets(args) exists');
  ok(/createAssetsStore\(\{\s*session, logger\s*\}\)/.test(bootSrc), 'buildApp builds the ONE store');
  ok(/ctx = \{[\s\S]{0,400}assets,/.test(bootSrc), 'and hands it to every screen through ctx');
  ok(/assets\?\.pause\?\.\('match'\)/.test(bootSrc) && /assets\?\.resume\?\.\('match'\)/.test(bootSrc),
    'attachMatch pauses and resumes the queue with reason "match"');
  const attach = bootSrc.slice(bootSrc.indexOf('function attachMatch'), bootSrc.indexOf('function armRecapFallback'));
  ok(/assets\?\.pause\?\.\('match'\)/.test(attach),
    'and the hook lives INSIDE attachMatch, so a relay rebuild re-binds it');
  ok(/S\.title\.assets/.test(await read('../ui/screens/title.js')), 'title.js renders the menu item from strings');
  ok(typeof S.title.assets === 'string' && S.title.assets.length > 0, 'and S.title.assets exists', String(S.title.assets));
}

// ------------------------------------- 3. one bridge handler per type, ever

{
  const bootSrc = await read('../boot.js');
  const storeSrc = await read('../ui/assetsStore.js');
  const grab = (src) => Array.from(src.matchAll(/bridge\.on\(\s*'([^']+)'/g)).map((m) => m[1]);
  const bootTypes = grab(bootSrc);
  const storeTypes = grab(storeSrc);
  const all = bootTypes.concat(storeTypes);
  const dupes = all.filter((t, i) => all.indexOf(t) !== i);
  ok(dupes.length === 0, 'no bridge.on type is registered twice across boot.js + assetsStore.js', dupes.join(','));
  for (const t of ['cache-state', 'cache-list', 'cache-progress', 'encode-request', 'cache-put-result']) {
    ok(storeTypes.indexOf(t) >= 0, 'assetsStore.js owns the "' + t + '" handler');
    ok(bootTypes.indexOf(t) < 0, 'and boot.js does NOT also claim "' + t + '"');
  }
  ok(all.indexOf('net-post-result') < 0, 'nobody re-registers net-post-result (bridge.js owns it)');
  ok(/onEncodeRequest/.test(storeSrc), 'the lane-B seam onEncodeRequest() is present for the encoder wave');
}

// ------------------------------------------------- 4. the ring is pinned

{
  const css = await read('../ui/screens.css');
  const blocks = css.match(/\.gg-asset-ring\s*\{[^}]*\}/g) || [];
  ok(blocks.length > 0, 'screens.css styles .gg-asset-ring');
  ok(blocks.some((b) => /--gg-deco-play:\s*running/.test(b)),
    'the progress ring re-declares --gg-deco-play: running — it is information, not chrome (like .gg-mon-proj)');
  ok(/\.gg-asset\[data-state="working"\]/.test(css), 'and the tile states are addressable by attribute, not by colour alone');
  ok(!/animation:/.test(blocks.join('')), 'the ring itself runs no CSS animation — JS writes stroke-dashoffset');
}

// ------------------------------------------- 5+6. strings exist, no markup

{
  const src = await read('../ui/screens/assets.js');
  const keys = Array.from(new Set(Array.from(src.matchAll(/S\.assets\.([A-Za-z0-9_]+)/g)).map((m) => m[1])));
  ok(keys.length > 20, 'the screen reads a real copy deck from strings.js', String(keys.length));
  for (const k of keys) {
    ok(S.assets[k] !== undefined, 'S.assets.' + k + ' exists (an undefined string renders literally)');
  }
  ok(typeof S.assets.ribbon === 'function', 'S.assets.ribbon(ready, size) exists for the title ribbon');
  ok(/412 ready · 3\.1 GB cached/.test(S.assets.ribbon(412, '3.1 GB')) || S.assets.ribbon(412, '3.1 GB').indexOf('412') === 0,
    'and reads like "412 ready · 3.1 GB cached"', S.assets.ribbon(412, '3.1 GB'));

  const marked = (src.match(/innerHTML/g) || []).length;
  ok(marked === 0, 'ui/screens/assets.js never touches markup assignment — filenames are user data', String(marked));
  ok(/name\.textContent = String\(item\.name/.test(src), 'the filename goes in as text, explicitly');
}

// ----------------------------------------------------- 7. the store units

function fakeBridge({ hosted = true } = {}) {
  const handlers = new Map();
  const sent = [];
  return {
    isHosted: hosted,
    on(t, fn) {
      if (handlers.has(t)) throw new Error('bridge.on: duplicate handler for "' + t + '"');
      handlers.set(t, fn);
    },
    off(t) { return handlers.delete(t); },
    send(m) { sent.push(m); },
    log() {},
    _handlers: handlers,
    _sent: sent,
    fire(m) { const h = handlers.get(m.type); if (h) h(m); return !!h; },
    ops() { return sent.filter((m) => m.type === 'cache-req').map((m) => m.op); },
  };
}

const mkItem = (i, over) => Object.assign({
  id: 'a' + i,
  name: 'clip ' + i + '.mp4',
  rel: 'videos/clip' + i + '.mp4',
  kind: 'video',
  state: 'pending',
  pct: 0,
  srcUrl: 'https://ccp.media/v/' + i,
  artUrl: '',
  prevUrl: '',
  bytes: 0,
  srcBytes: 40 * 1024 * 1024,
  w: 1920, h: 1080, durMs: 30000, fail: '',
}, over || {});

{
  // --- pure helpers -------------------------------------------------------
  ok(clampCapBytes(0) === CAP_DEFAULT_BYTES, 'a junk cap becomes the 8 GB default');
  ok(clampCapBytes(NaN) === CAP_DEFAULT_BYTES, 'so does NaN');
  ok(clampCapBytes(1) === CAP_MIN_BYTES, 'below the floor clamps to 1 GB');
  ok(clampCapBytes(999 * GB) === CAP_MAX_BYTES, 'above the ceiling clamps to 64 GB');
  ok(capGb(3.4 * GB) === 3, 'the slider position is whole gigabytes', String(capGb(3.4 * GB)));
  ok(capGb(0) === 8, 'and a junk cap parks the slider at the default');
  ok(formatBytes(3.14 * GB) === '3.1 GB', 'bytes format to one decimal at GB', formatBytes(3.14 * GB));
  ok(formatBytes(820 * 1024 * 1024) === '820 MB', 'and to whole MB below that', formatBytes(820 * 1024 * 1024));
  ok(formatBytes(0) === '0 B', 'zero is "0 B", not "NaN B"', formatBytes(0));
  ok(formatUsage(3.14 * GB, 8 * GB) === '3.1 / 8.0 GB', 'the usage chip reads "3.1 / 8.0 GB"', formatUsage(3.14 * GB, 8 * GB));
  ok(etaMinutes(12 * 60000) === 12, '12 minutes of ETA reads 12', String(etaMinutes(12 * 60000)));
  ok(etaMinutes(1500) === 1, 'and anything non-zero is at least a minute (never "0 minutes left")');
  ok(etaMinutes(0) === 0, 'while a zero ETA is the "estimating" signal');
  ok(normalizeState('SOMETHING NEW') === 'pending', 'an unknown state reads as not-compressed, never as ready');

  const items = [
    mkItem(1, { state: 'ready', name: 'sunset.gif' }),
    mkItem(2, { state: 'pending', name: 'beach.mp4' }),
    mkItem(3, { state: 'working', pct: 42, name: 'Beach party.webm' }),
    mkItem(4, { state: 'failed', fail: 'no-decoder', name: 'weird.avi' }),
    mkItem(5, { state: 'exempt', name: 'tiny.png', srcBytes: 900 * 1024 }),
  ];
  ok(items.filter((i) => matchesFilter(i, 'all', '')).length === 5, 'filter "all" keeps everything');
  ok(items.filter((i) => matchesFilter(i, 'needs', '')).length === 2, 'filter "needs" = pending + working');
  ok(items.filter((i) => matchesFilter(i, 'ready', '')).length === 1, 'filter "ready" is exact');
  ok(items.filter((i) => matchesFilter(i, 'failed', '')).length === 1, 'filter "failed" is exact');
  ok(items.filter((i) => matchesFilter(i, 'exempt', '')).length === 1, 'filter "exempt" is exact');
  ok(items.filter((i) => matchesFilter(i, 'all', 'beach')).length === 2, 'search is case-insensitive and matches the name');
  ok(items.filter((i) => matchesFilter(i, 'needs', 'beach')).length === 2, 'search AND filter compose');
  ok(items.filter((i) => matchesFilter(i, 'all', '   ')).length === 5, 'a whitespace query is not a filter');
  ok(pendingInputBytes(items) === 80 * 1024 * 1024, 'the confirm sheet counts only what still needs work',
    String(pendingInputBytes(items)));

  ok(FILTERS.length === 5 && FILTERS[0] === 'all', 'the segmented filter has its five segments');
  ok(MAX_COMPRESS_IDS === 500, 'the compress verb is capped at the host\'s 500 ids per frame');
}

{
  // --- list assembly + progress merge ------------------------------------
  const b = fakeBridge();
  const store = createAssetsStore({ bridge: b, session: { caps: {} }, logger: null, autoHello: false });
  ok(b._handlers.size === 5, 'the store registered all five cache handlers exactly once', String(b._handlers.size));

  let itemEmits = 0;
  store.onItems(() => { itemEmits++; });
  ok(itemEmits === 0, 'onItems does not fire before a list has landed');

  b.fire({ type: 'cache-list', seq: 0, last: false, items: [mkItem(1), mkItem(2)] });
  ok(store.items.length === 0, 'a half-delivered list is NOT shown — only `last` commits');
  ok(itemEmits === 0, 'and nothing is emitted mid-assembly');

  b.fire({ type: 'cache-list', seq: 1, last: true, items: [mkItem(3, { state: 'ready', artUrl: 'https://ccp.cache/art/3' })] });
  ok(store.items.length === 3, 'the committed list is every frame joined', String(store.items.length));
  ok(itemEmits === 1, 'and exactly one emit for the whole assembly');
  ok(store.isListLoaded === true, 'the store knows it has a list (the screen stops saying "reading…")');
  ok(store.item('a3').artUrl === 'https://ccp.cache/art/3', 'item fields survive assembly');

  // out-of-order frame
  b.fire({ type: 'cache-list', seq: 7, last: true, items: [mkItem(9)] });
  ok(store.items.length === 3, 'an out-of-order frame cannot replace a good list', String(store.items.length));
  ok(store.desyncs === 1, 'and it is counted as a desync', String(store.desyncs));

  // a fresh seq 0 starts a NEW assembly and replaces the old list wholesale
  b.fire({ type: 'cache-list', seq: 0, last: true, items: [mkItem(1), mkItem(2)] });
  ok(store.items.length === 2, 'seq 0 restarts the assembly and the commit replaces the table',
    String(store.items.length));

  // progress merge
  b.fire({
    type: 'cache-progress',
    items: [{ id: 'a1', state: 'working', pct: 42 }, { id: 'nope', state: 'ready', pct: 100 }],
  });
  ok(store.item('a1').state === 'working' && store.item('a1').pct === 42, 'progress merges into a known item');
  ok(store.item('a1').name === 'clip 1.mp4', 'and leaves the fields it did not carry alone');
  ok(store.item('nope') === null, 'an unknown id is dropped, not invented');
  ok(itemEmits === 3, 'the merge emitted once', String(itemEmits));

  b.fire({ type: 'cache-progress', items: [{ id: 'a1', pct: 88, artUrl: 'https://ccp.cache/art/1', bytes: 1234 }] });
  ok(store.item('a1').state === 'working', 'a partial delta does not reset the state');
  ok(store.item('a1').pct === 88 && store.item('a1').bytes === 1234, 'but does move what it carries');

  // --- state --------------------------------------------------------------
  let lastState = null;
  store.onState((s) => { lastState = s; });
  ok(lastState !== null, 'onState fires immediately with the current value');
  b.fire({
    type: 'cache-state',
    capBytes: 8 * GB, usedBytes: 3.14 * GB, overCap: false,
    ready: 412, pending: 30, working: 2, failed: 1, exempt: 55, total: 500,
    paused: true, pausedBy: 'match', etaMs: 12 * 60000, throughputBps: 900000,
    hw: true, presetId: 'p1', presetName: 'default', presetChanged: false,
    lanes: { video: 1, still: 1, page: 0 },
  });
  ok(lastState.ready === 412 && lastState.hw === true, 'cache-state lands on the store');
  ok(lastState.paused === true && lastState.pausedBy === 'match', 'including who paused it');
  ok(lastState.loaded === true, 'and the store is now "loaded" for the ribbon');
  ok(formatUsage(lastState.usedBytes, lastState.capBytes) === '3.1 / 8.0 GB', 'which is what the usage chip reads');

  // --- the request verbs --------------------------------------------------
  store.requestList();
  store.compressAll();
  store.compress(['a1', 'a2']);
  store.cancel(['a1']);
  store.pause('user');
  store.resume('match');
  store.deleteOne(['a1']);
  store.deleteAll();
  store.setCap(999 * GB);
  const ops = b.ops();
  for (const op of ['list', 'compress-all', 'compress', 'cancel', 'pause', 'resume', 'delete', 'delete-all', 'set-cap']) {
    ok(ops.indexOf(op) >= 0, 'the store can send op "' + op + '"');
  }
  const setCap = b._sent.filter((m) => m.op === 'set-cap').pop();
  ok(setCap.capBytes === CAP_MAX_BYTES, 'set-cap is clamped before it reaches the host', String(setCap.capBytes));
  const pauseFrame = b._sent.filter((m) => m.op === 'pause').pop();
  ok(pauseFrame.reason === 'user', 'pause carries its reason');
  const resumeFrame = b._sent.filter((m) => m.op === 'resume').pop();
  ok(resumeFrame.reason === 'match', 'and so does resume — the host keeps the two flags apart');
  ok(store.compress([]) === false, 'an empty compress is not a frame');

  // 1,200 ids -> three frames, none over the ceiling
  b._sent.length = 0;
  const many = [];
  for (let i = 0; i < 1200; i++) many.push('id' + i);
  store.compress(many);
  const frames = b._sent.filter((m) => m.op === 'compress');
  ok(frames.length === 3, '1,200 ids go out as three frames', String(frames.length));
  ok(frames.every((f) => f.ids.length <= MAX_COMPRESS_IDS), 'and no frame breaks the 500-id ceiling');

  // --- the lane-B seam ----------------------------------------------------
  const seen = [];
  const offEnc = store.onEncodeRequest((m) => seen.push(m.type));
  b.fire({ type: 'encode-request', jobId: 'j1' });
  b.fire({ type: 'cache-put-result', jobId: 'j1', ok: true });
  ok(seen.join(',') === 'encode-request,cache-put-result', 'both lane-B verbs reach a registered encoder', seen.join(','));
  offEnc();
  b.fire({ type: 'encode-request', jobId: 'j2' });
  ok(seen.length === 2, 'and are dropped (not thrown) once nothing is listening', String(seen.length));

  store.dispose();
  ok(b._handlers.size === 0, 'dispose() hands the bridge types back');
}

{
  // --- a duplicate store must not take the page down ---------------------
  const b = fakeBridge();
  const first = createAssetsStore({ bridge: b, autoHello: false });
  let threw = false;
  let second = null;
  try { second = createAssetsStore({ bridge: b, autoHello: false }); } catch (_e) { threw = true; }
  ok(!threw, 'a second store on the same bridge logs instead of throwing (a throw here is a white page)');
  ok(!!second, 'and still returns an object the screen can call');
  first.dispose();
  if (second) second.dispose();
}

{
  // --- hello + probe ------------------------------------------------------
  const caps = await probeEncode();
  ok(caps && typeof caps === 'object', 'probeEncode() answers even on a runtime with no WebCodecs at all');
  for (const k of ['videoEncoder', 'hw', 'gif', 'awebp', 'recorderMp4']) {
    ok(typeof caps[k] === 'boolean', 'probe reports ' + k + ' as a boolean', String(caps[k]));
  }
  ok(caps.videoEncoder === false, 'and says no under node, honestly');

  // THE SPIKE FINDING, encoded: Baseline level 3.0 (42E01E) REFUSES 720p, so a
  // probe that only asked with it would report "no encoder" on a machine that
  // encodes 720p all day. Level 3.1 (42E01F) is the one that answers yes.
  globalThis.VideoEncoder = {
    async isConfigSupported(cfg) {
      if (cfg.codec === 'avc1.42E01E' && cfg.height > 480) return { supported: false };
      if (cfg.hardwareAcceleration === 'prefer-hardware') return { supported: true };
      return { supported: cfg.codec === 'avc1.42E01F' || cfg.height <= 480 };
    },
  };
  const caps2 = await probeEncode();
  ok(caps2.videoEncoder === true, 'the 720p-capable probe asks with avc1.42E01F and gets a yes');
  ok(caps2.hw === true, 'and prefer-hardware answers the hw flag');
  delete globalThis.VideoEncoder;

  globalThis.VideoEncoder = { isConfigSupported() { throw new Error('boom'); } };
  const caps3 = await probeEncode();
  ok(caps3.videoEncoder === false, 'a runtime whose probe THROWS still returns an answer, never an exception');
  delete globalThis.VideoEncoder;

  const b = fakeBridge();
  const store = createAssetsStore({ bridge: b, session: { caps: {} }, logger: null });
  await sleep(20);
  const hello = b._sent.find((m) => m.type === 'cache-req' && m.op === 'hello');
  ok(!!hello, 'the store says hello to the host on its own');
  ok(hello && hello.caps && typeof hello.caps.videoEncoder === 'boolean', 'and the hello carries the probe');
  store.dispose();
}

{
  // --- standalone: an ANSWER, on a microtask, never a spinner ------------
  const b = fakeBridge({ hosted: false });
  const store = createAssetsStore({ bridge: b, logger: null });
  ok(store.state.available === true, 'the store starts optimistic');
  await sleep(5);
  ok(store.state.available === false, 'and standalone resolves to "unavailable" without a round-trip');
  ok(store.isLoaded === true && store.isListLoaded === true, 'both loaded flags are set, so nothing can spin');
  ok(b.ops().length === 0, 'and not one frame was sent into the void');
  store.dispose();

  // The host can also say "no cache here" through caps.
  const b2 = fakeBridge({ hosted: true });
  const store2 = createAssetsStore({ bridge: b2, session: { caps: { assetCache: false } }, logger: null });
  await sleep(5);
  ok(store2.state.available === false, 'caps.assetCache:false is honoured too');
  ok(b2.ops().indexOf('hello') < 0, 'and no hello is sent to a host without a cache');
  store2.dispose();
}

// ------------------------------------------------------- 8. the screen

function fakeSheets() {
  const calls = [];
  let answer = 'go';
  return {
    calls,
    setAnswer(v) { answer = v; },
    open(o) { calls.push(o); return Promise.resolve(answer); },
    get isOpen() { return false; },
  };
}

{
  // --- standalone renders the device picker, immediately ------------------
  const b = fakeBridge({ hosted: false });
  const store = createAssetsStore({ bridge: b, logger: null });
  await sleep(5);
  const container = dom.doc.getElementById('scr-assets');
  container.replaceChildren();
  const handle = screenMod.mount(container, { assets: store, actions: { goTitle() {} }, prefs: { get: () => false } });
  ok(!!findOne(container, 'gg-assets--local'), 'standalone mounts the local device-picker card');
  ok(!!findOne(container, 'gg-local-list'), 'with the picked-files list');
  ok(findAll(container, 'gg-assets-grid').length === 0, 'and no grid, no spinner, nothing to wait for');
  handle.unmount();
  store.dispose();
}

{
  // --- standalone local files: adopt, dedup, reject, remove ---------------
  const b = fakeBridge({ hosted: false });
  const store = createAssetsStore({ bridge: b, logger: null });
  await sleep(5);
  const file = (name, size, type) => ({
    name, size, type,
    arrayBuffer: () => Promise.resolve(new Uint8Array(Array.from({ length: size }, (_x, i) => i % 251)).buffer),
  });
  const r1 = await store.addLocalFiles([
    file('a.png', 1000, 'image/png'),
    file('b.mp4', 2000, 'video/mp4'),
    file('typed-by-ext.GIF', 500, ''),                    // empty type — the extension decides
    file('evil.exe', 100, 'application/x-msdownload'),    // wire would refuse: rejected here
    file('huge.png', 9 * 1024 * 1024, 'image/png'),       // over the exempt cap
  ]);
  ok(r1.added === 3, 'three files adopted', JSON.stringify(r1));
  ok(r1.badType === 1 && r1.tooBig === 1, 'the exe and the 9MB file were refused');
  const locals = store.items.filter((it) => it.id.indexOf('local:') === 0);
  ok(locals.length === 3, 'they surface through store.items');
  ok(locals.every((it) => it.state === 'exempt' && /^[0-9a-f]{64}$/.test(it.sha)), 'as exempt items with a real sha');
  const png = locals.find((it) => it.name === 'a.png');
  ok(!!png && png.mime === 'image/png' && png.kind === 'image', 'mime rides the item (blob urls have no extension)');
  const gif = locals.find((it) => it.name === 'typed-by-ext.GIF');
  ok(!!gif && gif.mime === 'image/gif', 'an empty file.type falls back to the extension');
  const r2 = await store.addLocalFiles([file('a-again.png', 1000, 'image/png')]);
  ok(r2.added === 0 && r2.dupes === 1, 'identical bytes dedup by sha, whatever the filename');
  ok(store.removeLocal(png.id) === true, 'removeLocal removes');
  ok(store.items.filter((it) => it.id.indexOf('local:') === 0).length === 2, 'and the list agrees');
  store.dispose();
  ok(store.localCount === 0, 'dispose clears the local library');
}

{
  // --- hosted: the grid, virtualized -------------------------------------
  globalThis.IntersectionObserver = FakeIO;
  FakeIO.all.length = 0;

  const b = fakeBridge();
  const store = createAssetsStore({ bridge: b, session: { caps: {} }, logger: null, autoHello: false });
  const sheets = fakeSheets();
  const container = dom.doc.getElementById('scr-assets');
  container.replaceChildren();

  const handle = screenMod.mount(container, {
    assets: store,
    actions: { goTitle() {} },
    sheets,
    prefs: { get: () => false },
    audio: null,
    logger: null,
  });

  ok(!!findOne(container, 'gg-assets'), 'the screen card is up');
  ok(store.listRequested === true, 'mounting asks the host for the list');
  ok(findOne(container, 'gg-assets-empty').textContent === S.assets.loading,
    'and says "reading your library…" until it arrives');

  // 30 ready + 10 pending + 1 failed + 1 exempt
  const list = [];
  for (let i = 0; i < 30; i++) {
    list.push(mkItem(i, { state: 'ready', artUrl: 'https://ccp.cache/art/' + i, prevUrl: 'https://ccp.cache/prv/' + i, name: 'ready ' + i + '.mp4' }));
  }
  for (let i = 30; i < 40; i++) list.push(mkItem(i, { state: 'pending', name: 'todo ' + i + '.mp4' }));
  list.push(mkItem(90, { state: 'failed', fail: 'no-decoder', name: 'broken.avi' }));
  list.push(mkItem(91, { state: 'exempt', name: 'tiny.png', srcUrl: 'https://ccp.media/tiny.png', srcBytes: 900 * 1024 }));
  b.fire({ type: 'cache-list', seq: 0, last: true, items: list });
  b.fire({
    type: 'cache-state',
    capBytes: 8 * GB, usedBytes: 3.14 * GB, overCap: false,
    ready: 30, pending: 10, working: 0, failed: 1, exempt: 1, total: 42,
    paused: false, pausedBy: '', etaMs: 12 * 60000, throughputBps: 900000, hw: true,
    presetId: 'p', presetName: 'default', presetChanged: false, lanes: { video: 1, still: 0, page: 0 },
  });

  const tiles = findAll(container, 'gg-asset');
  ok(tiles.length === 42, 'every item in the list got a tile', String(tiles.length));
  ok(findOne(container, 'gg-assets-empty').hidden === true, 'the "reading…" line is gone');

  // NOTHING has media until the observer says it is on screen.
  ok(findTag(container, 'img').length === 0 && findTag(container, 'video').length === 0,
    'not one media element exists while every tile is off-screen — this is the whole point of the grid');

  const tileIo = FakeIO.all.find((o) => o.targets.size > 1);
  ok(!!tileIo, 'the tiles are observed');
  tileIo.intersect();
  const vids = findTag(container, 'video');
  const imgs = findTag(container, 'img');
  ok(vids.length === 24, 'at most 24 micro-previews play at once (MAX_LIVE_PREVIEWS)', String(vids.length));
  ok(vids.every((v) => v.getAttribute('src').indexOf('#t=0.1') > 0), 'each preview uses the #t=0.1 poster trick');
  ok(vids.every((v) => v.hasAttribute('loop') && v.hasAttribute('playsinline')), 'muted, looping, inline');
  ok(vids.every((v) => v.muted === true), 'and actually muted, not merely attributed');
  ok(imgs.length === 7, 'the 6 previews over budget fall back to the still, and the exempt tile shows its original',
    String(imgs.length));
  const exemptTile = tiles.find((t) => t.dataset.id === 'a91');
  const exemptImg = findTag(exemptTile, 'img')[0];
  ok(exemptImg && exemptImg.getAttribute('src') === 'https://ccp.media/tiny.png',
    'the exempt tile shows the ORIGINAL — it is what would be sent');
  const pendingTile = tiles.find((t) => t.dataset.id === 'a30');
  ok(findTag(pendingTile, 'img').length === 0 && findTag(pendingTile, 'video').length === 0,
    'an uncompressed tile stays a grey placeholder — no decoder for a file that cannot be sent');

  // Scrolling away releases the decoders again.
  const first = tiles[0];
  tileIo.leave([first]);
  ok(findTag(first, 'video').length === 0 && findTag(first, 'img').length === 0,
    'leaving the viewport DETACHES the media, it does not merely hide it');

  // --- the words on the tiles --------------------------------------------
  const badgeOf = (id) => {
    const t = tiles.find((x) => x.dataset.id === id);
    return t ? findOne(t, 'gg-asset-badge').textContent : null;
  };
  ok(badgeOf('a0') === S.assets.badgeReady, 'a compressed tile says "ready"', String(badgeOf('a0')));
  ok(badgeOf('a30') === S.assets.badgeNotReady, 'an uncompressed one says so in words', String(badgeOf('a30')));
  ok(String(badgeOf('a90')).indexOf('no-decoder') >= 0, 'a failure names its reason', String(badgeOf('a90')));
  ok(badgeOf('a91') === S.assets.badgeExempt, 'and a small file says it sends as-is', String(badgeOf('a91')));
  const nameNode = findOne(tiles.find((x) => x.dataset.id === 'a90'), 'gg-asset-name');
  ok(nameNode.textContent === 'broken.avi' && nameNode.children.length === 0,
    'the filename is a text node and nothing else');

  // --- progress moves the tile in place ----------------------------------
  b.fire({ type: 'cache-progress', items: [{ id: 'a30', state: 'working', pct: 42 }] });
  const working = tiles.find((x) => x.dataset.id === 'a30');
  ok(working.dataset.state === 'working', 'progress moves the tile state attribute');
  ok(findOne(working, 'gg-asset-badge').textContent === S.assets.badgeWorking(42), 'and the word is the percentage',
    findOne(working, 'gg-asset-badge').textContent);
  const ring = findOne(working, 'gg-asset-ring');
  ok(!!ring, 'the tile carries a progress ring');
  const valueCircle = (ring.children || []).find((c) => c._classes && c._classes.has('gg-asset-ring-value'));
  const dash = Number(valueCircle.getAttribute('stroke-dasharray'));
  const off = Number(valueCircle.getAttribute('stroke-dashoffset'));
  ok(Math.abs(off - dash * 0.58) < 0.5, 'whose dashoffset is written from JS at 42%', off + ' of ' + dash);
  ok(findAll(container, 'gg-asset').length === 42, 'and no tile was rebuilt to do it', String(findAll(container, 'gg-asset').length));

  // --- toolbar ------------------------------------------------------------
  const btnByText = (t) => findAll(container, 'gg-btn').find((b2) => String(b2.textContent).indexOf(t) === 0);
  const compressBtn = findAll(container, 'gg-btn')[0];
  ok(String(compressBtn.textContent).indexOf('compress everything') === 0,
    'the primary action counts what is left', String(compressBtn.textContent));
  ok(/\(10\)/.test(compressBtn.textContent), 'and the count is the real one', String(compressBtn.textContent));

  const eta = findOne(container, 'gg-assets-eta');
  ok(eta.textContent.indexOf('12 minutes') > 0, 'the ETA line is honest about the time', eta.textContent);
  ok(eta.textContent.indexOf(S.assets.encoderHw) > 0, 'and about which encoder is doing it', eta.textContent);

  // paused for the match
  b.fire({ type: 'cache-state', capBytes: 8 * GB, usedBytes: 3.14 * GB, ready: 30, pending: 10, working: 0, failed: 1, exempt: 1, total: 42, paused: true, pausedBy: 'match', etaMs: 12 * 60000, hw: true, lanes: {} });
  ok(eta.textContent === S.assets.etaPausedMatch, 'a match parks the queue and the line says exactly that', eta.textContent);
  const pauseBtn = findAll(container, 'gg-btn')[1];
  ok(pauseBtn.disabled === true, 'and the pause button is not the player\'s to press while a duel owns it');

  b.fire({ type: 'cache-state', capBytes: 8 * GB, usedBytes: 3.14 * GB, ready: 30, pending: 10, working: 0, failed: 1, exempt: 1, total: 42, paused: false, pausedBy: '', etaMs: 0, hw: false, lanes: {} });
  ok(eta.textContent === S.assets.etaEstimating, 'no ETA yet reads "estimating…", never "0 minutes"', eta.textContent);
  ok(pauseBtn.disabled === false, 'and the button comes back');

  // --- the filter + the search -------------------------------------------
  const seg = findAll(container, 'gg-assets-segbtn');
  ok(seg.length === 5, 'five filter segments');
  const needsSeg = seg.find((s) => s.dataset.filter === 'needs');
  needsSeg.dispatchEvent({ type: 'click', preventDefault() {} });
  ok(findAll(container, 'gg-asset').length === 10, 'the "needs work" filter shows the 9 still pending + the 1 working',
    String(findAll(container, 'gg-asset').length));
  ok(needsSeg.getAttribute('aria-pressed') === 'true', 'and the segment reads pressed');

  const search = findOne(container, 'gg-assets-search');
  search.value = 'todo 3';
  search.dispatchEvent({ type: 'input' });
  await sleep(200);
  ok(findAll(container, 'gg-asset').length === 10, 'the search box filters by name', String(findAll(container, 'gg-asset').length));
  search.value = 'zzzz';
  search.dispatchEvent({ type: 'input' });
  await sleep(200);
  ok(findAll(container, 'gg-asset').length === 0, 'a query that matches nothing shows nothing');
  ok(findOne(container, 'gg-assets-empty').textContent === S.assets.emptyFiltered,
    'and says the filter is why, not that the library is empty');
  search.value = '';
  search.dispatchEvent({ type: 'input' });
  await sleep(200);
  seg.find((s) => s.dataset.filter === 'all').dispatchEvent({ type: 'click', preventDefault() {} });
  ok(findAll(container, 'gg-asset').length === 42, 'and clearing both brings the library back');

  // --- compress-all confirms only when it is a big ask -------------------
  b._sent.length = 0;
  compressBtn.dispatchEvent({ type: 'click', preventDefault() {} });
  await sleep(5);
  ok(sheets.calls.length === 0, 'a small job just starts — no ceremony');
  ok(b.ops().indexOf('compress-all') >= 0, 'and it really started');

  b.fire({ type: 'cache-state', capBytes: 8 * GB, usedBytes: 3.14 * GB, ready: 30, pending: 10, working: 1, failed: 1, exempt: 1, total: 42, paused: false, pausedBy: '', etaMs: 45 * 60000, hw: false, lanes: {} });
  b._sent.length = 0;
  compressBtn.dispatchEvent({ type: 'click', preventDefault() {} });
  await sleep(5);
  ok(sheets.calls.length === 1, 'a 45-minute job asks first');
  ok(String(sheets.calls[0].line).indexOf(S.assets.encoderSw) > 0,
    'and the confirm names the SLOW encoder, because that is why it is 45 minutes', String(sheets.calls[0].line));
  ok(b.ops().indexOf('compress-all') >= 0, 'answering yes starts it');

  sheets.calls.length = 0;
  sheets.setAnswer('cancel');
  b._sent.length = 0;
  compressBtn.dispatchEvent({ type: 'click', preventDefault() {} });
  await sleep(5);
  ok(b.ops().indexOf('compress-all') < 0, 'answering no does nothing at all');
  sheets.setAnswer('go');

  // --- delete-compressed always confirms ---------------------------------
  sheets.calls.length = 0;
  b._sent.length = 0;
  const deleteBtn = findAll(container, 'gg-btn')[2];
  deleteBtn.dispatchEvent({ type: 'click', preventDefault() {} });
  await sleep(5);
  ok(sheets.calls.length === 1, 'deleting every compressed copy asks first, always');
  ok(String(sheets.calls[0].line).indexOf('separately') > 0,
    'and promises what an opponent sent is stored separately', String(sheets.calls[0].line));
  ok(b.ops().indexOf('delete-all') >= 0, 'then does it');

  // --- per-tile actions ---------------------------------------------------
  b._sent.length = 0;
  const failedTile = findAll(container, 'gg-asset').find((x) => x.dataset.id === 'a90');
  const retry = findOne(failedTile, 'gg-asset-act');
  ok(retry.textContent === S.assets.tileRetry, 'a failed tile offers a retry', retry.textContent);
  retry.dispatchEvent({ type: 'click', preventDefault() {} });
  const compressFrame = b._sent.find((m) => m.op === 'compress');
  ok(compressFrame && compressFrame.ids.join() === 'a90', 'which compresses exactly that one file');

  const readyTile = findAll(container, 'gg-asset').find((x) => x.dataset.id === 'a0');
  ok(findOne(readyTile, 'gg-asset-act').textContent === S.assets.tileDelete, 'a ready tile offers to drop its copy');

  // --- the cap slider -----------------------------------------------------
  b._sent.length = 0;
  const capInput = findOne(container, 'gg-assets-cap').children.find((c) => c.tagName === 'INPUT');
  ok(capInput.getAttribute('min') === '1' && capInput.getAttribute('max') === '64',
    'the cap slider runs 1–64 GB, the same rails the host clamps to');
  capInput.value = '16';
  capInput.dispatchEvent({ type: 'input' });
  ok(b._sent.filter((m) => m.op === 'set-cap').length === 0, 'and it is debounced — dragging is not 60 writes a second');
  await sleep(500);
  const cap = b._sent.filter((m) => m.op === 'set-cap').pop();
  ok(cap && cap.capBytes === 16 * GB, 'the settled value goes to the host once', String(cap && cap.capBytes));

  // --- teardown -----------------------------------------------------------
  const before = FakeIO.all.filter((o) => !o.disconnected).length;
  handle.unmount();
  ok(before > 0 && FakeIO.all.every((o) => o.disconnected), 'unmount disconnects every observer the screen made');
  ok(findTag(container, 'video').length === 0, 'and releases every decoder it was holding');
  store.dispose();
  delete globalThis.IntersectionObserver;
}

{
  // --- no IntersectionObserver (an old runtime): still usable ------------
  const b = fakeBridge();
  const store = createAssetsStore({ bridge: b, session: { caps: {} }, logger: null, autoHello: false });
  const container = dom.doc.getElementById('scr-assets');
  container.replaceChildren();
  const handle = screenMod.mount(container, { assets: store, actions: { goTitle() {} }, prefs: { get: () => false } });
  const list = [];
  for (let i = 0; i < 300; i++) list.push(mkItem(i, { state: 'ready', artUrl: 'https://ccp.cache/art/' + i }));
  b.fire({ type: 'cache-list', seq: 0, last: true, items: list });
  const tiles = findAll(container, 'gg-asset');
  ok(tiles.length === 120, 'without an observer the grid still only builds a chunk at a time', String(tiles.length));
  const more = findOne(container, 'gg-assets-more');
  ok(more && more.hidden === false, 'and offers the rest behind a button');
  more.dispatchEvent({ type: 'click', preventDefault() {} });
  ok(findAll(container, 'gg-asset').length === 240, 'which appends the next chunk', String(findAll(container, 'gg-asset').length));
  handle.unmount();
  store.dispose();
}

{
  // --- the title ribbon --------------------------------------------------
  const b = fakeBridge();
  const store = createAssetsStore({ bridge: b, session: { caps: {} }, logger: null, autoHello: false });
  const container = dom.doc.getElementById('scr-title');
  container.replaceChildren();
  const handle = titleMod.mount(container, {
    session: { hosted: true },
    actions: { goHost() {}, goJoin() {}, goPractice() {}, goAssets() {}, quit() {} },
    prefs: { get: () => true, set() {} },
    sheets: null,
    assets: store,
  });
  const ribbon = findOne(container, 'gg-title-ribbon');
  ok(!!ribbon, 'the title still has its ribbon node');
  ok(ribbon.hidden === true, 'hidden while there is nothing true to say');
  b.fire({ type: 'cache-state', capBytes: 8 * GB, usedBytes: 3.14 * GB, ready: 412, pending: 0, working: 0, failed: 0, exempt: 0, total: 412, paused: false, pausedBy: '', etaMs: 0, hw: true, lanes: {} });
  ok(ribbon.hidden === false, 'and unhidden once a cache-state lands');
  ok(ribbon.textContent === '412 ready · 3.1 GB cached', 'reading the real numbers', ribbon.textContent);

  const menuLabels = findAll(container, 'gg-menu-item').map((x) => String(x.textContent));
  const idxPractice = menuLabels.findIndex((l) => l.indexOf(S.title.practice) === 0);
  const idxAssets = menuLabels.findIndex((l) => l.indexOf(S.title.assets) === 0);
  const idxOptions = menuLabels.findIndex((l) => l.indexOf(S.title.options) === 0);
  ok(idxAssets > idxPractice && idxAssets < idxOptions,
    'the menu item sits between Practice and Options', menuLabels.join(' | '));
  handle.unmount();
  store.dispose();
}

await sleep(60);
console.log(`\nselftest-assets: ${n - failures}/${n} checks passed`);
if (failures > 0) {
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
process.exit(0);
