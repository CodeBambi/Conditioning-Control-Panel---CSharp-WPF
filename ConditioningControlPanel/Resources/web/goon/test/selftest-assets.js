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
const compressMod = await import('../ui/localCompress.js');

const {
  createAssetsStore, probeEncode, clampCapBytes, capGb, formatBytes, formatUsage,
  etaMinutes, matchesFilter, normalizeState, pendingInputBytes, classifyLocalSize,
  CAP_MIN_BYTES, CAP_MAX_BYTES, CAP_DEFAULT_BYTES, MAX_COMPRESS_IDS, FILTERS, GB,
  LOCAL_MAX_BYTES, LOCAL_ARTIFACT_MAX_BYTES, LOCAL_DECODE_MAX_BYTES, LOCAL_ZIP_MAX_ENTRIES,
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
    // Past the DECODE cap, so it is refused without a compressor being asked —
    // and without 90 MB being allocated: the gate is on `size`, before the read.
    file('monster.png', 90 * 1024 * 1024, 'image/png'),
  ]);
  ok(r1.added === 3, 'three files adopted', JSON.stringify(r1));
  ok(r1.badType === 1 && r1.tooBig === 1, 'the exe and the 90MB source were refused');
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
  // --- standalone zips: expand, skip the junk, hold the ceilings ----------
  //
  // The archives are built HERE, byte by byte — local file headers, central
  // directory, EOCD, method 0 (STORED) — so this coverage depends on no
  // compressor being present anywhere. The deflate leg is packed with the
  // vendored fflate, which is also what reads it back: nothing on this path
  // asks for DecompressionStream, in node or in the page.
  const CRC = (() => {
    const t = new Uint32Array(256);
    for (let i = 0; i < 256; i++) {
      let c = i;
      for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
      t[i] = c >>> 0;
    }
    return t;
  })();
  const crc32 = (u8) => {
    let c = 0xFFFFFFFF;
    for (let i = 0; i < u8.length; i++) c = CRC[(c ^ u8[i]) & 0xFF] ^ (c >>> 8);
    return (c ^ 0xFFFFFFFF) >>> 0;
  };
  const enc = new TextEncoder();

  /**
   * Per entry, optionally:
   *   localExtra   — bytes in the LOCAL header's extra field that the central
   *                  record does NOT mirror. Real writers pad these two
   *                  differently all the time (zip64 placeholders, unix
   *                  timestamps, alignment), and a reader that computes the
   *                  data offset from the central copy lands inside the data.
   *   declaredSize — the originalSize written into both headers, regardless of
   *                  how many bytes are really there. Lets a 100 MB claim be
   *                  tested without 100 MB of anything.
   *   method       — the compression method id written into both headers.
   */
  function storedZip(entries) {
    const locals = [];
    const central = [];
    let offset = 0;
    for (const e of entries) {
      const name = enc.encode(e.name);
      const data = e.bytes || new Uint8Array(0);
      const crc = crc32(data);
      const lex = e.localExtra || new Uint8Array(0);
      const oSize = (e.declaredSize === undefined) ? data.length : e.declaredSize;
      const method = (e.method === undefined) ? 0 : e.method;
      const lh = new Uint8Array(30 + name.length + lex.length);
      const lv = new DataView(lh.buffer);
      lv.setUint32(0, 0x04034b50, true); lv.setUint16(4, 20, true);
      lv.setUint16(8, method, true);
      lv.setUint32(14, crc, true);
      lv.setUint32(18, data.length, true); lv.setUint32(22, oSize, true);
      lv.setUint16(26, name.length, true); lv.setUint16(28, lex.length, true);
      lh.set(name, 30); lh.set(lex, 30 + name.length);
      const ch = new Uint8Array(46 + name.length);
      const cv = new DataView(ch.buffer);
      cv.setUint32(0, 0x02014b50, true); cv.setUint16(4, 20, true); cv.setUint16(6, 20, true);
      cv.setUint16(10, method, true);
      cv.setUint32(16, crc, true);
      cv.setUint32(20, data.length, true); cv.setUint32(24, oSize, true);
      cv.setUint16(28, name.length, true); cv.setUint32(42, offset, true);
      ch.set(name, 46);
      locals.push(lh, data); central.push(ch);
      offset += lh.length + data.length;
    }
    const cdSize = central.reduce((a, c) => a + c.length, 0);
    const eocd = new Uint8Array(22);
    const ev = new DataView(eocd.buffer);
    ev.setUint32(0, 0x06054b50, true);
    ev.setUint16(8, entries.length, true); ev.setUint16(10, entries.length, true);
    ev.setUint32(12, cdSize, true); ev.setUint32(16, offset, true);
    const all = locals.concat(central, [eocd]);
    const out = new Uint8Array(all.reduce((a, c) => a + c.length, 0));
    let p = 0;
    for (const c of all) { out.set(c, p); p += c.length; }
    return out;
  }
  const fill = (n, seed) => {
    const u = new Uint8Array(n);
    for (let i = 0; i < n; i++) u[i] = (i * 7 + seed) % 251;
    return u;
  };
  /**
   * A REAL File, because the reader now takes the pick itself and slices it.
   * Node has had File (with Blob.slice().arrayBuffer()) since 20, so the same
   * object the browser hands over is the object under test here.
   */
  const asFile = (name, bytes, type) => new File([bytes], name, { type: type || '' });
  const zipOf = (name, entries) => asFile(name, storedZip(entries), 'application/zip');

  const b = fakeBridge({ hosted: false });
  const store = createAssetsStore({ bridge: b, logger: null });
  await sleep(5);

  const library = zipOf('library.zip', [
    { name: 'a.png', bytes: fill(800, 1) },
    { name: 'nested/b.mp4', bytes: fill(1200, 2) },
    { name: 'photos/', bytes: new Uint8Array(0) },                      // directory record
    { name: 'readme.txt', bytes: fill(64, 3) },                         // not media
    { name: '__MACOSX/._a.png', bytes: fill(32, 4) },                   // resource fork
    { name: '.hidden.png', bytes: fill(48, 5) },                        // dotfile
    { name: 'inner.zip', bytes: storedZip([{ name: 'c.png', bytes: fill(100, 6) }]) },
    // 90 MB DECLARED, ten bytes present: past the decode cap, so the directory
    // pass refuses it and the bytes are never touched.
    { name: 'monster.png', bytes: fill(10, 7), declaredSize: 90 * 1024 * 1024 },
  ]);

  const r1 = await store.addLocalFiles([library]);
  ok(r1.zips === 1, 'the picked archive was EXPANDED, not adopted', JSON.stringify(r1));
  ok(r1.added === 2, 'both eligible entries came out of it', JSON.stringify(r1));
  ok(r1.tooBig === 1, 'and the 90 MB entry hit the very same decode cap a hand-picked file would');
  const locals = store.items.filter((it) => it.id.indexOf('local:') === 0);
  ok(locals.length === 2, 'they surface through store.items like any other pick');
  ok(locals.every((it) => it.state === 'exempt' && /^[0-9a-f]{64}$/.test(it.sha)),
    'as exempt items with a real sha');
  const png = locals.find((it) => it.name === 'a.png');
  ok(!!png && png.mime === 'image/png' && png.kind === 'image' && png.bytes === 800,
    'mime, kind and size ride the extracted item (zip entries carry no file.type)');
  const mp4 = locals.find((it) => it.name === 'b.mp4');
  ok(!!mp4 && mp4.mime === 'video/mp4' && mp4.kind === 'video' && mp4.ext === 'mp4',
    'a nested path keeps only its base name');
  ok(!locals.some((it) => /readme|MACOSX|hidden|inner\.zip|photos/.test(it.name)),
    'the txt, the resource fork, the dotfile, the directory record and the NESTED ZIP are all skipped',
    locals.map((it) => it.name).join(','));

  const r2 = await store.addLocalFiles([asFile('copy-of-a.png', fill(800, 1), 'image/png')]);
  ok(r2.added === 0 && r2.dupes === 1,
    'a zip entry and the same bytes picked by hand share one sha — the dedup sees straight through the archive');
  const r3 = await store.addLocalFiles([library]);
  ok(r3.added === 0 && r3.dupes === 2 && r3.zips === 1, 'picking the same archive twice adopts nothing new',
    JSON.stringify(r3));

  const junk = fill(64, 13);
  const r4 = await store.addLocalFiles([asFile('broken.zip', junk, 'application/zip')]);
  ok(r4.zipBad === 1 && r4.added === 0 && r4.zips === 0 && r4.tooBig === 0,
    'a corrupt archive is ONE zipBad — it never throws, never empties the screen, and NEVER lands in tooBig '
    + '(the per-file 8 MB line on an archive failure is the whole bug this path had)', JSON.stringify(r4));

  const r5 = await store.addLocalFiles([
    zipOf('more.zip', [{ name: 'd.webp', bytes: fill(700, 8) }]),
    asFile('e.gif', fill(300, 9), ''),
  ]);
  ok(r5.added === 2 && r5.zips === 1, 'one call can mix an archive and a loose file', JSON.stringify(r5));

  const { zipSync } = await import('../vendor/fflate/fflate.module.js');
  const squished = zipSync({ 'squished.png': [fill(4096, 11), { level: 6 }] });
  const r6 = await store.addLocalFiles([asFile('deflated.zip', squished, 'application/zip')]);
  ok(r6.added === 1 && r6.zips === 1,
    'a DEFLATED entry inflates and is adopted too — fflate does it in-process, so no DecompressionStream is needed',
    JSON.stringify(r6));
  ok(store.items.some((it) => it.name === 'squished.png' && it.bytes === 4096),
    'and it lands at its ORIGINAL size, not its packed one');

  // The ceilings, driven straight — the store passes only the per-entry cap.
  const four = asFile('four.zip',
    storedZip([1, 2, 3, 4].map((i) => ({ name: 'p' + i + '.png', bytes: fill(500, 20 + i) }))),
    'application/zip');
  const cut = await zipMod.readZipMedia(four, { maxEntries: 2 });
  ok(cut.ok && cut.entries.length === 2 && cut.truncated === true && cut.trimmed === 2 && cut.failed === 0,
    'the entry ceiling truncates and counts the rest as TRIMMED, never as failed — "took the first N" of a '
    + 'real library is not the same news as "unreadable"',
    JSON.stringify({ n: cut.entries.length, trimmed: cut.trimmed, failed: cut.failed }));
  const bomb = await zipMod.readZipMedia(four, { maxTotalBytes: 900 });
  ok(bomb.ok && bomb.entries.length === 1 && bomb.truncated === true,
    'and the zip-bomb guard stops on the DECLARED size, before a byte is inflated',
    JSON.stringify({ n: bomb.entries.length }));
  const garbage = await zipMod.readZipMedia(asFile('junk.zip', junk, 'application/zip'));
  ok(garbage.ok === false && garbage.entries.length === 0,
    'readZipMedia answers ok:false for garbage instead of throwing', garbage.reason);
  ok((await zipMod.readZipMedia(asFile('tiny.zip', new Uint8Array(4), ''))).ok === false,
    'a file too short to hold an EOCD is refused early');

  /* --- the local header's extra field is NOT the central one ---------------
   * The classic offset bug: compute the data start from the central record's
   * extraLen and every byte read is shifted. STORED entries make it silent —
   * the slice is still the right LENGTH — so this compares the bytes. */
  {
    const want = fill(600, 41);
    const skew = asFile('skew.zip', storedZip([
      { name: 'front-padded.png', bytes: want, localExtra: fill(37, 99) },
      { name: 'plain.png', bytes: fill(240, 42) },
    ]), 'application/zip');
    const r = await zipMod.readZipMedia(skew);
    const got = r.entries.find((e) => e.name === 'front-padded.png');
    ok(r.ok && !!got && got.bytes.length === want.length && got.bytes.every((b, i) => b === want[i]),
      'an entry whose LOCAL extra field is longer than its central record still reads byte-exact '
      + '— the data offset comes from the local header, never the directory',
      JSON.stringify({ ok: r.ok, n: r.entries.length, failed: r.failed }));
    ok(r.entries.length === 2 && r.failed === 0, 'and the entry after it is still found', JSON.stringify(r.failed));
  }

  /* --- a fat DECLARED size is refused without being touched --------------- */
  {
    // 100 MB claimed, ten bytes of junk present, method 8. Anything that tried
    // to inflate this would count `failed`; the ceiling must catch it first,
    // from the directory alone.
    const liar = asFile('liar.zip', storedZip([
      { name: 'whopper.png', bytes: fill(10, 3), declaredSize: 100 * 1024 * 1024, method: 8 },
      { name: 'ok.png', bytes: fill(120, 4) },
    ]), 'application/zip');
    const r = await zipMod.readZipMedia(liar, { maxEntryBytes: 8 * 1024 * 1024 });
    ok(r.ok && r.tooBig === 1 && r.failed === 0 && r.entries.length === 1,
      'an entry declaring more than the per-entry cap is counted tooBig from the DIRECTORY — '
      + 'never sliced, never inflated (a bogus deflate stream would have failed loudly)',
      JSON.stringify({ tooBig: r.tooBig, failed: r.failed, n: r.entries.length }));
  }

  /* --- a corrupt EOCD is an archive-level answer, not an exception -------- */
  {
    const good = storedZip([{ name: 'x.png', bytes: fill(300, 51) }]);
    const noSig = good.slice();
    noSig[noSig.length - 22] ^= 0xFF;                       // murder the EOCD signature
    const a = await zipMod.readZipMedia(asFile('nosig.zip', noSig, 'application/zip'));
    ok(a.ok === false && a.reason === 'not-a-zip' && a.entries.length === 0,
      'an EOCD with a broken signature answers ok:false, and says which kind of broken', a.reason);

    const badOff = good.slice();
    new DataView(badOff.buffer).setUint32(badOff.length - 22 + 16, 0x7FFFFFF0, true);  // cd offset past EOF
    const b2 = await zipMod.readZipMedia(asFile('badoff.zip', badOff, 'application/zip'));
    ok(b2.ok === false && b2.reason === 'unreadable' && b2.entries.length === 0,
      'and an EOCD pointing its central directory past the end of the file does too', b2.reason);

    const r5b = await store.addLocalFiles([asFile('nosig2.zip', noSig, 'application/zip')]);
    ok(r5b.zipBad === 1 && r5b.tooBig === 0 && r5b.failed === 0,
      'the store reports that as zipBad — the ONE counter the 8 MB copy never reads', JSON.stringify(r5b));
  }

  /* --- the memory profile, measured -------------------------------------
   * The whole point of the rework: a big archive must cost its tail, its
   * directory and one entry — never its size. This wraps a real File in a
   * counter and checks the bytes actually pulled. */
  {
    const big = storedZip([1, 2, 3, 4, 5, 6].map((i) => ({ name: 'm' + i + '.png', bytes: fill(64 * 1024, i) })));
    const real = asFile('big.zip', big, 'application/zip');
    let sliced = 0;
    let wholeReads = 0;
    const watched = {
      name: real.name, type: real.type, size: real.size,
      slice(a, b) { sliced += (b - a); return real.slice(a, b); },
      arrayBuffer() { wholeReads++; return real.arrayBuffer(); },
    };
    const r = await zipMod.readZipMedia(watched, { isEligible: (n) => n === 'm3.png' });
    ok(r.ok && r.entries.length === 1, 'a selective read still finds its one entry', JSON.stringify(r.entries.length));
    ok(wholeReads === 0, 'and NOTHING on the path called arrayBuffer() on the archive itself');
    ok(sliced < real.size / 2,
      'the bytes actually pulled are a fraction of the archive — tail + directory + one entry',
      sliced + ' of ' + real.size);

    // ...and the caller can refuse to let the extracted bytes pile up at all.
    const seen = [];
    const streamed = await zipMod.readZipMedia(real, { onEntry: (e) => { seen.push(e.name); } });
    ok(streamed.ok && streamed.took === 6 && seen.length === 6 && streamed.entries.length === 0,
      'onEntry streams each entry out and the result array stays EMPTY — 500 photos are never in the heap together',
      JSON.stringify({ took: streamed.took, held: streamed.entries.length }));
    ok(/onEntry/.test(await read('../ui/assetsStore.js')),
      'and the store is what uses it: entries are adopted one at a time, not collected first');
  }
  ok(zipMod.isZipFile({ name: 'x.ZIP', type: '' }) === true
    && zipMod.isZipFile({ name: 'x', type: 'application/x-zip-compressed' }) === true
    && zipMod.isZipFile({ name: 'x.png', type: 'image/png' }) === false,
    'a zip is spotted by extension OR by mime, and nothing else is');
  ok(zipMod.isJunkEntry('__MACOSX/foo.png') && zipMod.isJunkEntry('a/') && zipMod.isJunkEntry('a/.b.png')
    && !zipMod.isJunkEntry('a/b.png'),
    'the junk filter knows directory records, resource forks and dotfiles at any depth');

  store.dispose();
  ok(store.localCount === 0, 'dispose clears everything the archives added, too');
}

/* ===========================================================================
 * THE ADOPTION POLICY — what standalone does with media that is TOO BIG TO SEND
 * AS-IS.
 *
 * The wire has two rails, not one: MAX_EXEMPT_BYTES (8 MB) for an untouched
 * original and MAX_ARTIFACT_BYTES (64 MB) for a compressed product. Everything
 * over the first used to be skipped, which meant a 1 GB zip of real photos
 * adopted nothing and said "over 8 MB" — a limit invented by the picker, not by
 * the protocol.
 *
 * The compressor is INJECTED here. There is no canvas, no createImageBitmap and
 * no WebCodecs under node, so what these checks pin is the POLICY — which lane a
 * file is routed to, which counter it lands in, what identity it ends up with —
 * and never the pixels. The real lanes are exercised in a browser.
 * ======================================================================== */
{
  const MB = 1024 * 1024;
  const hexOf = (buf) => Array.from(new Uint8Array(buf), (b) => b.toString(16).padStart(2, '0')).join('');
  const sha256 = async (u8) => hexOf(await crypto.subtle.digest('SHA-256', u8));
  const fill = (n, seed) => {
    const u = new Uint8Array(n);
    for (let i = 0; i < n; i++) u[i] = (i * 31 + seed) % 251;
    return u;
  };
  const realFile = (name, bytes, type) => new File([bytes], name, { type: type || '' });

  /**
   * The stub. It records what it was handed and answers with bytes of a size
   * the test chose, so "did the policy pick the still lane or the animated one"
   * and "whose bytes became the wire identity" are both directly observable.
   */
  function makeStub(o = {}) {
    const calls = { image: [], gif: [] };
    const imgOut = o.imageOut || fill(600 * 1024, 7);
    const gifOut = o.gifOut || fill(900 * 1024, 9);
    return {
      calls, imgOut, gifOut,
      compressImage(src, mime) {
        calls.image.push({ mime, bytes: src.byteLength });
        if (o.throwOnImage) return Promise.reject(new Error('stub-refused'));
        return Promise.resolve({
          blob: new Blob([imgOut], { type: 'image/webp' }), mime: 'image/webp', w: 1920, h: 1080, kind: 'image',
        });
      },
      compressGif(src, mime) {
        calls.gif.push({ mime, bytes: src.byteLength });
        return Promise.resolve({
          blob: new Blob([gifOut], { type: 'video/mp4' }), mime: 'video/mp4', w: 720, h: 720, durMs: 3200, kind: 'video',
        });
      },
    };
  }

  const chan = await import('../net/mediaChannel.js');
  ok(LOCAL_MAX_BYTES === chan.MAX_EXEMPT_BYTES && LOCAL_ARTIFACT_MAX_BYTES === chan.MAX_ARTIFACT_BYTES,
    'the two local rails ARE the wire\'s two rails, imported — a copy of 8/24 would drift the day either moves',
    LOCAL_MAX_BYTES + ' / ' + LOCAL_ARTIFACT_MAX_BYTES);

  /* --- the routing table, as a pure function ----------------------------- */
  ok(classifyLocalSize('image/png', 1000) === 'take'
    && classifyLocalSize('image/png', 12 * MB) === 'take'
    && classifyLocalSize('image/png', 90 * MB) === 'too-big'
    && classifyLocalSize('video/mp4', 2 * MB) === 'take'
    && classifyLocalSize('video/mp4', 20 * MB) === 'take'
    && classifyLocalSize('video/mp4', 70 * MB) === 'too-big-video'
    && classifyLocalSize('image/gif', 0) === 'too-big',
    'classifyLocalSize routes by family AND size: a still is compressible to the decode cap, a clip only to '
    + 'the artifact cap, and past that it is its own answer');

  /* --- a 12 MB still becomes an artifact, identified by the OUTPUT bytes -- */
  {
    const stub = makeStub();
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null, compressor: stub });
    await sleep(5);

    const png = fill(12 * MB, 3);
    const r = await store.addLocalFiles([realFile('holiday.png', png, 'image/png')]);
    ok(r.added === 1 && r.compressed === 1 && r.tooBig === 0 && r.failed === 0,
      'a 12 MB png is COMPRESSED and adopted, not refused — the whole bug, in one line', JSON.stringify(r));
    ok(stub.calls.image.length === 1 && stub.calls.gif.length === 0,
      'it went down the still lane', JSON.stringify(stub.calls.image));
    ok(stub.calls.image[0].bytes === 12 * MB, 'with the SOURCE bytes, whole', String(stub.calls.image[0].bytes));

    const it = store.items.find((x) => x.name === 'holiday.png');
    ok(!!it && it.state === 'ready', 'it lands as a READY artifact, not as exempt — exempt means "sent as-is", '
      + 'and this is not the file the player picked', it && it.state);
    ok(!!it && it.bytes === stub.imgOut.length && it.srcBytes === 12 * MB,
      'bytes is what TRAVELS and srcBytes what it came from', it && (it.bytes + '/' + it.srcBytes));
    ok(!!it && it.sha === await sha256(stub.imgOut),
      'and the sha is the sha of the COMPRESSED bytes — the artifact is the thing with the wire identity, '
      + 'and the receiver re-hashes exactly these');
    ok(!!it && it.mime === 'image/webp' && it.kind === 'image',
      'the mime is the OUTPUT mime and the kind agrees with it (mediaChannel declines any offer where they do not)',
      it && (it.mime + '/' + it.kind));
    ok(!!it && it.artUrl !== '' && it.srcUrl === '',
      'the bytes hang off artUrl, which is where boot.js listSendable() looks for a non-exempt item');

    // Picking it again must not pay for the compression a second time.
    const again = await store.addLocalFiles([realFile('holiday-copy.png', png, 'image/png')]);
    ok(again.dupes === 1 && again.added === 0 && stub.calls.image.length === 1,
      'the SOURCE sha is remembered, so the same photo is never compressed twice', JSON.stringify(again));
    store.dispose();
  }

  /* --- 8..64 MB of video rides as-is; past that it is said out loud ------- */
  {
    const stub = makeStub();
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null, compressor: stub });
    await sleep(5);

    const clip = fill(20 * MB, 5);
    const r = await store.addLocalFiles([realFile('clip.mp4', clip, 'video/mp4')]);
    ok(r.added === 1 && r.compressed === 0 && r.tooBigVideo === 0,
      'a 20 MB mp4 is adopted AS-IS — there is no browser transcode and it does not need one', JSON.stringify(r));
    const it = store.items.find((x) => x.name === 'clip.mp4');
    ok(!!it && it.state === 'ready' && it.kind === 'video' && it.mime === 'video/mp4',
      'as a non-exempt READY artifact (exempt is capped at 8 MB — this would never be offered)',
      it && (it.state + '/' + it.kind));
    ok(!!it && it.bytes === 20 * MB && it.sha === await sha256(clip),
      'carrying its OWN bytes and its own sha — nothing was re-encoded');
    ok(!!it && it.artUrl !== '', 'behind artUrl, like every other artifact');
    ok(stub.calls.image.length === 0 && stub.calls.gif.length === 0, 'and the compressor was never asked');

    const big = await store.addLocalFiles([realFile('feature.mp4', fill(70 * MB, 6), 'video/mp4')]);
    ok(big.tooBigVideo === 1 && big.added === 0 && big.tooBig === 0 && big.failed === 0,
      'a 30 MB clip lands in tooBigVideo — its OWN counter, because "too big to send" and "over the 8 MB '
      + 'as-is limit" are different sentences and only one of them is true here', JSON.stringify(big));
    store.dispose();
  }

  /* --- animated vs still, decided by the BYTES ---------------------------- */
  {
    // A minimal but structurally real GIF: header, no global colour table, then
    // graphic-control-extension + image-descriptor pairs, then the trailer.
    const gifOf = (frames) => {
      const out = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 2, 0, 2, 0, 0x00, 0, 0];
      for (let i = 0; i < frames; i++) {
        out.push(0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00);        // GCE + sub-block terminator
        out.push(0x2C, 0, 0, 0, 0, 2, 0, 2, 0, 0x00);                    // image descriptor
        out.push(0x02, 0x01, 0x00, 0x00);                                // LZW size + one sub-block
      }
      out.push(0x3B);
      return new Uint8Array(out);
    };
    ok(compressMod.gifFrameCount(gifOf(1), 2) === 1 && compressMod.gifFrameCount(gifOf(3), 2) === 2,
      'gifFrameCount walks the block structure and stops at the limit',
      compressMod.gifFrameCount(gifOf(1), 2) + '/' + compressMod.gifFrameCount(gifOf(3), 2));
    ok(compressMod.gifFrameCount(new Uint8Array([1, 2, 3]), 2) === -1,
      'and answers -1 rather than guessing when the walk hits something it does not know');
    ok(compressMod.sniffAnimated(gifOf(4), 'image/gif') === 'animated'
      && compressMod.sniffAnimated(gifOf(1), 'image/gif') === 'still',
      'so a one-frame GIF takes the STILL lane and a four-frame one does not');

    const webpHead = (flags, fourcc) => {
      const u = new Uint8Array(24);
      u.set([0x52, 0x49, 0x46, 0x46], 0);                                 // RIFF
      u.set([0x57, 0x45, 0x42, 0x50], 8);                                 // WEBP
      u.set(fourcc, 12);
      u[20] = flags;
      return u;
    };
    ok(compressMod.webpIsAnimated(webpHead(0x02, [0x56, 0x50, 0x38, 0x58])) === 'animated'
      && compressMod.webpIsAnimated(webpHead(0x00, [0x56, 0x50, 0x38, 0x58])) === 'still'
      && compressMod.webpIsAnimated(webpHead(0x00, [0x56, 0x50, 0x38, 0x20])) === 'still',
      'and an animated WebP is read from the VP8X flag byte — a plain VP8 chunk is one frame by construction');

    const stub = makeStub();
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null, compressor: stub });
    await sleep(5);

    // A 9 MB animated gif: over the exempt cap, so it must be encoded.
    const anim = new Uint8Array(9 * MB);
    anim.set(gifOf(6), 0);
    const r = await store.addLocalFiles([realFile('loop.gif', anim, 'image/gif')]);
    ok(r.added === 1 && r.compressed === 1 && stub.calls.gif.length === 1 && stub.calls.image.length === 0,
      'a 9 MB ANIMATED gif goes to the animated lane — lane B\'s own worker, not the canvas',
      JSON.stringify({ r, gif: stub.calls.gif.length, img: stub.calls.image.length }));
    const it = store.items.find((x) => x.name === 'loop.gif');
    ok(!!it && it.mime === 'video/mp4' && it.kind === 'video',
      'and it changes FAMILY on the way: the artifact is an mp4, so the kind must say video or the offer gate '
      + 'declines it as bad_mime', it && (it.mime + '/' + it.kind));
    ok(!!it && it.ext === 'mp4' && it.durMs === 3200, 'the extension and duration come off the encode, not the source');

    // ...and a still one of the same size does not.
    const still = new Uint8Array(9 * MB);
    still.set(gifOf(1), 0);
    const r2 = await store.addLocalFiles([realFile('static.gif', still, 'image/gif')]);
    ok(r2.added === 1 && stub.calls.image.length === 1 && stub.calls.gif.length === 1,
      'a single-frame gif of the same size takes the STILL lane — an mp4 of one frame is nobody\'s idea of a photo',
      JSON.stringify({ img: stub.calls.image.length, gif: stub.calls.gif.length }));
    store.dispose();
  }

  /* --- every adopted local item satisfies the offer gate's own rules ------ */
  {
    const stub = makeStub();
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null, compressor: stub });
    await sleep(5);
    await store.addLocalFiles([
      realFile('small.png', fill(2000, 1), 'image/png'),
      realFile('mid.mp4', fill(12 * MB, 2), 'video/mp4'),
      realFile('big.jpg', fill(30 * MB, 3), 'image/jpeg'),
    ]);
    const locals = store.items.filter((x) => String(x.id).indexOf('local:') === 0);
    ok(locals.length === 3, 'three picks, three adoption roads, three items', String(locals.length));
    const family = (m) => (String(m).indexOf('video/') === 0 ? 'video' : 'image');
    ok(locals.every((x) => chan.ACCEPT_MIME.has(x.mime)), 'every adopted mime is one the wire carries');
    ok(locals.every((x) => family(x.mime) === x.kind),
      'every adopted kind agrees with its mime — mediaChannel.familyOf is the rule and it declines the pair '
      + 'when they disagree, so an item that fails this is un-sendable and invisible about it');
    ok(locals.every((x) => x.bytes > 0 && x.bytes <= LOCAL_ARTIFACT_MAX_BYTES),
      'and nothing adopted is past the 64 MB rail the transfer queue enforces');
    ok(locals.every((x) => (x.state === 'exempt' ? x.bytes <= LOCAL_MAX_BYTES : x.state === 'ready' && !!x.artUrl)),
      'exempt items are the small ones; everything else is ready + artUrl, which is what listSendable reads');
    store.dispose();
  }

  /* --- a pathological output, and a compressor that refuses --------------- */
  {
    const stub = makeStub({ imageOut: fill(1024, 2) });
    // 25 MB of "compressed" output: past the wire cap, so there is nothing to
    // adopt. Adopting it anyway would put a row on the screen that can never be
    // offered, which is a worse lie than "that one did not work".
    const fat = makeStub({ imageOut: new Uint8Array(65 * MB) });
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null, compressor: fat });
    await sleep(5);
    const r = await store.addLocalFiles([realFile('vast.png', fill(20 * MB, 4), 'image/png')]);
    ok(r.failed === 1 && r.added === 0 && r.compressed === 0,
      'an output past the 64 MB artifact cap is counted failed, never adopted', JSON.stringify(r));
    store.dispose();

    const cross = makeStub({ throwOnImage: true });
    const b2 = fakeBridge({ hosted: false });
    const store2 = createAssetsStore({ bridge: b2, logger: null, compressor: cross });
    await sleep(5);
    const src = fill(11 * MB, 8);
    const r2 = await store2.addLocalFiles([realFile('bad.png', src, 'image/png')]);
    ok(r2.failed === 1 && r2.added === 0, 'a compressor that throws is one failed file, not a dead picker',
      JSON.stringify(r2));
    const r3 = await store2.addLocalFiles([realFile('bad-again.png', src, 'image/png')]);
    ok(r3.dupes === 1 && cross.calls.image.length === 1,
      'and the same bytes are not put through it again — a refusal is remembered for the session too');
    store2.dispose();
    ok(stub.imgOut.length === 1024, 'the unused stub is untouched');   // keeps the linter honest
  }

  /* --- the zip ceilings trim a real library, and SAY so ------------------- */
  {
    const enc = new TextEncoder();
    const CRC = (() => {
      const t = new Uint32Array(256);
      for (let i = 0; i < 256; i++) {
        let c = i;
        for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
        t[i] = c >>> 0;
      }
      return t;
    })();
    const crc32 = (u8) => {
      let c = 0xFFFFFFFF;
      for (let i = 0; i < u8.length; i++) c = CRC[(c ^ u8[i]) & 0xFF] ^ (c >>> 8);
      return (c ^ 0xFFFFFFFF) >>> 0;
    };
    function storedZip(entries) {
      const locals = [];
      const central = [];
      let offset = 0;
      for (const e of entries) {
        const name = enc.encode(e.name);
        const data = e.bytes || new Uint8Array(0);
        const crc = crc32(data);
        const oSize = (e.declaredSize === undefined) ? data.length : e.declaredSize;
        const lh = new Uint8Array(30 + name.length);
        const lv = new DataView(lh.buffer);
        lv.setUint32(0, 0x04034b50, true); lv.setUint16(4, 20, true);
        lv.setUint32(14, crc, true);
        lv.setUint32(18, data.length, true); lv.setUint32(22, oSize, true);
        lv.setUint16(26, name.length, true);
        lh.set(name, 30);
        const ch = new Uint8Array(46 + name.length);
        const cv = new DataView(ch.buffer);
        cv.setUint32(0, 0x02014b50, true); cv.setUint16(4, 20, true); cv.setUint16(6, 20, true);
        cv.setUint32(16, crc, true);
        cv.setUint32(20, data.length, true); cv.setUint32(24, oSize, true);
        cv.setUint16(28, name.length, true); cv.setUint32(42, offset, true);
        ch.set(name, 46);
        locals.push(lh, data); central.push(ch);
        offset += lh.length + data.length;
      }
      const cdSize = central.reduce((a, c) => a + c.length, 0);
      const eocd = new Uint8Array(22);
      const ev = new DataView(eocd.buffer);
      ev.setUint32(0, 0x06054b50, true);
      ev.setUint16(8, entries.length & 0xFFFF, true); ev.setUint16(10, entries.length & 0xFFFF, true);
      ev.setUint32(12, cdSize, true); ev.setUint32(16, offset, true);
      const all = locals.concat(central, [eocd]);
      const out = new Uint8Array(all.reduce((a, c) => a + c.length, 0));
      let p = 0;
      for (const c of all) { out.set(c, p); p += c.length; }
      return out;
    }

    const stub = makeStub();
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null, compressor: stub });
    await sleep(5);

    // Byte-unique per entry: `fill` repeats every 251 seeds, and 500 photos that
    // dedup down to 251 would make this measure the wrong ceiling entirely.
    const uniq = (i) => { const u = fill(40, i % 251); new DataView(u.buffer).setUint32(0, i, true); return u; };
    const many = [];
    for (let i = 0; i < LOCAL_ZIP_MAX_ENTRIES + 3; i++) many.push({ name: 'p' + i + '.png', bytes: uniq(i) });
    const huge = new File([storedZip(many)], 'library.zip', { type: 'application/zip' });
    const r = await store.addLocalFiles([huge]);
    ok(r.added === LOCAL_ZIP_MAX_ENTRIES && r.trimmed === 3,
      'a zip past the entry ceiling adopts the first ' + LOCAL_ZIP_MAX_ENTRIES + ' and TRIMS the rest',
      JSON.stringify({ added: r.added, trimmed: r.trimmed, failed: r.failed }));
    ok(r.failed === 0 && r.zipBad === 0 && r.tooBig === 0,
      'and none of that is failed, zipBad or tooBig — the archive was fine, the ceiling is ours to admit to',
      JSON.stringify(r));
    ok(typeof S.assets.local.trimmed === 'function'
      && !/unreadable|couldn't|broken/i.test(S.assets.local.trimmed(3, LOCAL_ZIP_MAX_ENTRIES)),
      'and the string says what happened without calling a good library broken',
      S.assets.local.trimmed(3, LOCAL_ZIP_MAX_ENTRIES));
    store.dispose();
  }

  /* --- the progress seam: a long add is not a silent one ------------------ */
  {
    const stub = makeStub();
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null, compressor: stub });
    await sleep(5);
    const seen = [];
    const off = store.onLocalProgress((p) => seen.push({ running: p.running, done: p.done, total: p.total }));
    ok(seen.length === 1 && seen[0].running === false,
      'onLocalProgress answers immediately — a subscriber never waits for the first pick');
    await store.addLocalFiles([
      realFile('one.png', fill(1000, 1), 'image/png'),
      realFile('two.png', fill(1000, 2), 'image/png'),
      realFile('three.png', fill(1000, 3), 'image/png'),
    ]);
    ok(seen.some((p) => p.running && p.total === 3), 'it reports the denominator up front', JSON.stringify(seen[1]));
    ok(seen.some((p) => p.running && p.done === 2), 'and counts up as each pick lands');
    const last = seen[seen.length - 1];
    ok(last.running === false && last.done === 3 && last.total === 3,
      'and finishes at done === total with running false, so the line can hide itself',
      JSON.stringify(last));
    off();
    store.dispose();
  }

  /* --- the LOCAL encode driver: lane B's worker, without lane B's protocol -
   * The hosted driver answers a host `encode-request` and base64-chunks the
   * result back over the bridge. This one is called directly and hands the
   * bytes to its caller — same worker, same messages, no bridge in sight, which
   * is exactly why the hosted path cannot regress from any of it. */
  {
    function fakeWorker() {
      const w = {
        posted: [], killed: false, onmessage: null, onerror: null,
        postMessage(m, transfer) { w.posted.push({ m, transfer }); },
        terminate() { w.killed = true; },
        reply(m) { if (w.onmessage) w.onmessage({ data: m }); },
      };
      return w;
    }

    let made = null;
    const driver = compressMod.createLocalEncodeDriver({ workerFactory: () => (made = fakeWorker()) });
    const src = new Uint8Array(64).fill(9).buffer;
    const pcts = [];
    const p = driver.encode(src, 'image/gif', { maxBox: 720 }, (pct) => pcts.push(pct));
    await sleep(2);
    ok(!!made && made.posted.length === 1 && made.posted[0].m.kind === 'encode',
      'the local driver posts an `encode` to the same worker lane B uses');
    ok(made.posted[0].transfer && made.posted[0].transfer[0] === src,
      'with the source bytes TRANSFERRED, not copied — a 40 MB gif is not cloned onto the worker heap');
    ok(made.posted[0].m.cfg.maxBox === 720, 'and the caller\'s config, verbatim');
    ok(!/jobId/.test(JSON.stringify(made.posted[0].m)) === false && String(made.posted[0].m.jobId).length > 0,
      'the job carries its own id, minted here — the host never hears about it');

    const id = made.posted[0].m.jobId;
    made.reply({ kind: 'progress', jobId: id, pct: 42 });
    made.reply({ kind: 'done', jobId: id, art: new Uint8Array(96).fill(1).buffer, w: 480, h: 270, durMs: 1500 });
    const out = await p;
    ok(pcts.length === 1 && pcts[0] === 42, 'progress is forwarded to the caller, not to a bridge', JSON.stringify(pcts));
    ok(out && out.art.byteLength === 96 && out.w === 480 && out.durMs === 1500,
      'and the finished mp4 comes back as BYTES — there is no cache to put it in');

    const p2 = driver.encode(new Uint8Array(8).buffer, 'image/webp', {}, null);
    await sleep(2);
    const id2 = made.posted[1].m.jobId;
    ok(made.posted[1].m.mime === 'image/webp', 'an animated WebP asks the decoder for image/webp, not gif');
    made.reply({ kind: 'fail', jobId: id2, reason: 'unsupported' });
    let threw = '';
    try { await p2; } catch (e) { threw = String(e.message || e); }
    ok(threw === 'unsupported', 'a worker failure rejects with the worker\'s own reason', threw);
    driver.dispose();
    ok(made.killed === true, 'and dispose terminates the worker — an encoder thread must not outlive the store');

    // The blob wrapper, end to end, through the same seam the store calls.
    const w2 = fakeWorker();
    const d2 = compressMod.createLocalEncodeDriver({ workerFactory: () => w2 });
    const gifP = compressMod.compressGif(new Uint8Array(32).buffer, 'image/gif', { driver: d2 });
    await sleep(2);
    w2.reply({ kind: 'done', jobId: w2.posted[0].m.jobId, art: new Uint8Array(64).fill(2).buffer, w: 300, h: 300, durMs: 900 });
    const gifOut = await gifP;
    ok(w2.posted[0].m.cfg.wantPrev === false,
      'compressGif asks for NO micro-preview: the preview is a hosted concept (a second cache-put stream on the '
      + 'bridge) and standalone has nowhere to put one');
    ok(gifOut.mime === 'video/mp4' && gifOut.kind === 'video' && gifOut.blob.size === 64,
      'compressGif answers with an mp4 Blob and the VIDEO kind — the store copies both onto the item, and the '
      + 'offer gate declines any pair that disagrees', gifOut.mime + '/' + gifOut.kind);
    d2.dispose();

    ok(compressMod.fitDown(4000, 3000, 1920).w === 1920 && compressMod.fitDown(4000, 3000, 1920).h === 1440,
      'fitDown scales the long edge to the box and keeps the ratio',
      JSON.stringify(compressMod.fitDown(4000, 3000, 1920)));
    ok(compressMod.fitDown(800, 600, 1920).scaled === false && compressMod.fitDown(800, 600, 1920).w === 800,
      'and never scales a small image UP');
    ok(compressMod.canEncodeAnimated() === false,
      'canEncodeAnimated is a SYNCHRONOUS call-time probe — node has no WebCodecs and says so without '
      + 'building a worker or transferring a 40 MB buffer into it first');
    const real = compressMod.createLocalCompressor();
    let refused = '';
    try { await real.compressGif(new Uint8Array(8).buffer, 'image/gif'); } catch (e) { refused = String(e.message || e); }
    ok(refused === 'no-encoder',
      'so the real compressor refuses the animated lane honestly here — which is exactly what a Safari without '
      + 'WebCodecs gets, and the store counts it as one failed file');
    real.dispose();

    ok(compressMod.extForMime('video/mp4') === 'mp4' && compressMod.extForMime('image/webp') === 'webp'
      && compressMod.extForMime('image/jpeg') === 'jpg' && compressMod.extForMime('application/zip') === '',
      'and the artifact extension comes from the OUTPUT mime');
  }

  /* --- the card says all of it ------------------------------------------- */
  {
    const src = await read('../ui/screens/assets.js');
    ok(/r\.compressed[\s\S]{0,120}L\.compressed/.test(src), 'the summary line reports what was compressed');
    ok(/r\.tooBigVideo[\s\S]{0,160}L\.skipBigVideo/.test(src),
      'and gives an un-sendable video its own sentence, with the ARTIFACT cap in it');
    ok(/r\.trimmed[\s\S]{0,120}L\.trimmed/.test(src), 'and admits to a trimmed library');
    ok(/onLocalProgress/.test(src) && /ledger\.sub\(store\.onLocalProgress/.test(src),
      'the progress line is a LEDGER subscription — an add that outlives the screen must not write to a dead node');
    ok(/role: 'status'[\s\S]{0,200}gg-local-progress|gg-local-progress[\s\S]{0,200}role: 'status'/.test(src),
      'and it is a role=status, so a screen reader hears the wait too');
    for (const k of ['adding', 'addingOne', 'compressing', 'compressed', 'skipBigVideo', 'trimmed', 'sizeShrunk']) {
      ok(S.assets.local[k] !== undefined, 'S.assets.local.' + k + ' exists');
    }
    ok(/64 MB/.test(S.assets.local.skipBigVideo(2, formatBytes(LOCAL_ARTIFACT_MAX_BYTES))),
      'the video refusal quotes the 64 MB rail, not the 8 MB one',
      S.assets.local.skipBigVideo(2, formatBytes(LOCAL_ARTIFACT_MAX_BYTES)));
  }
}

{
  // --- the picker and the copy both admit that zips are allowed ----------
  const src = await read('../ui/screens/assets.js');
  ok(/const LOCAL_ACCEPT[\s\S]{0,300}\.zip/.test(src), 'the device picker offers .zip to the OS file sheet');
  ok(/application\/zip/.test(src), 'by mime as well as by extension (android hands over one or the other)');
  ok(/zip/.test(S.assets.local.limits('8 MB', '64 MB')), 'and the limits line says so out loud',
    S.assets.local.limits('8 MB', '64 MB'));
  ok(typeof S.assets.local.zipNone === 'string',
    'S.assets.local.zipNone exists — an archive holding nothing sendable must still answer');
  ok(typeof S.assets.local.zipBad === 'function'
    && !/\bMB\b/.test(S.assets.local.zipBad(1) + S.assets.local.zipBad(3)),
    'S.assets.local.zipBad speaks about the ARCHIVE and never quotes a per-file size',
    S.assets.local.zipBad(1) + ' / ' + S.assets.local.zipBad(3));
  ok(/r\.zipBad[\s\S]{0,80}L\.zipBad/.test(src),
    'and the summary line renders it on its own, instead of folding an archive failure into skipBig');
  ok(!/LOCAL_ZIP_MAX_BYTES|too big to open/.test(await read('../ui/assetsStore.js')),
    'the archive-SIZE refusal is gone entirely — a 1 GB library is the use case, not an edge case');
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

/* ==========================================================================
 * THE LOCAL LIBRARY IS A PLAYABLE LIBRARY (the phone bug, 2026-08-04).
 *
 * A player loaded a zip on a phone, saw the assets in the manager, backed out,
 * started a practice session — and every effect fired with no media. The picks
 * had only ever been wired into the SEND path (boot.js listSendable); the
 * PLAYBACK deck (exec/media.js `entries`) is fed by the host `manifest` frame,
 * which bridge.js synthesizes EMPTY standalone. These pin the join: the store
 * can hand its picks over as deck entries, and boot really asks it to.
 * ======================================================================== */
{
  const { localPlayableEntries } = storeMod;
  ok(typeof localPlayableEntries === 'function', 'assetsStore exports localPlayableEntries()');

  /* --- the pure mapping -------------------------------------------------- */
  const mapped = localPlayableEntries([
    // An EXEMPT original IS the file: its bytes are behind srcUrl.
    { id: 'local:1', name: 'pic.png', kind: 'image', state: 'exempt', srcUrl: 'blob:x/1', artUrl: '', mime: 'image/png' },
    // A compressed pick has no srcUrl at all — the artifact is the thing.
    { id: 'local:2', name: 'holiday.webp', kind: 'image', state: 'ready', srcUrl: '', artUrl: 'blob:x/2', mime: 'image/webp' },
    { id: 'local:3', name: 'clip.mp4', kind: 'video', state: 'ready', srcUrl: '', artUrl: 'blob:x/3', mime: 'video/mp4' },
    // Nothing to play: skipped rather than pushed as an entry that can only 404.
    { id: 'local:4', name: 'urlless.png', kind: 'image', state: 'exempt', srcUrl: '', artUrl: '', mime: 'image/png' },
    null,
  ]);
  ok(mapped.length === 3, 'three of five rows are playable — a URL-less row and a null are dropped',
    String(mapped.length));
  ok(mapped[0].url === 'blob:x/1' && mapped[1].url === 'blob:x/2',
    'an exempt pick plays from srcUrl and a compressed one from artUrl — the same rule listSendable uses',
    mapped.map((e) => e.url).join(','));
  ok(mapped[2].kind === 'video' && mapped[1].kind === 'image',
    'the KIND is taken from the item, never re-derived: a blob: URL has no extension to sniff',
    mapped.map((e) => e.kind).join(','));
  ok(mapped.every((e) => typeof e.name === 'string' && typeof e.url === 'string' && e.kind),
    'and every entry is the {kind,name,url} shape exec/media.js setLocalLibrary takes');

  // A row that somehow lost its kind still classifies by mime rather than
  // vanishing — the deck would rather have it as an image than not at all.
  const byMime = localPlayableEntries([
    { id: 'local:5', name: 'a', kind: '', state: 'exempt', srcUrl: 'blob:x/5', mime: 'video/mp4' },
    { id: 'local:6', name: 'b', kind: '', state: 'exempt', srcUrl: 'blob:x/6', mime: 'image/gif' },
    { id: 'local:7', name: 'c', kind: '', state: 'exempt', srcUrl: 'blob:x/7', mime: '' },
  ]);
  ok(byMime.length === 2 && byMime[0].kind === 'video' && byMime[1].kind === 'image',
    'a kindless row falls back to its mime family, and a row with neither is dropped',
    byMime.map((e) => e.kind).join(','));

  /* --- the store really hands its picks over ----------------------------- */
  {
    const fill2 = (nb, seed) => {
      const u = new Uint8Array(nb);
      for (let i = 0; i < nb; i++) u[i] = (i * 17 + seed) % 251;
      return u;
    };
    const b = fakeBridge({ hosted: false });
    const store = createAssetsStore({ bridge: b, logger: null });
    await sleep(5);

    ok(store.localVersion === 0 && store.localDeck().length === 0,
      'a fresh standalone store has an empty local deck (this is the state the phone was stuck in)');

    const r = await store.addLocalFiles([
      new File([fill2(4000, 1)], 'zip-pic-a.png', { type: 'image/png' }),
      new File([fill2(5000, 2)], 'zip-pic-b.jpg', { type: 'image/jpeg' }),
      new File([fill2(6000, 3)], 'zip-clip.mp4', { type: 'video/mp4' }),
    ]);
    ok(r.added === 3, 'three picks adopted', JSON.stringify(r));
    const v1 = store.localVersion;
    ok(v1 === 3, 'localVersion counted every one of them', String(v1));

    const deck = store.localDeck();
    ok(deck.length === 3, 'and localDeck() hands all three to the media pool', String(deck.length));
    ok(deck.filter((e) => e.kind === 'video').length === 1 && deck.filter((e) => e.kind === 'image').length === 2,
      'with the kinds the adoption road decided, not the URL', deck.map((e) => e.kind).join(','));
    ok(deck.every((e) => e.url.indexOf('blob:') === 0), 'behind the store\'s own object URLs',
      deck.map((e) => e.url.slice(0, 12)).join(','));
    ok(store.localItems.length === 3 && store.localItems !== store.localItems,
      'store.localItems is a snapshot array, not the live map');

    // THE OTHER HALF OF THE JOIN: the real pool takes them and draws them.
    const { createGoonMediaPool } = await import('../exec/media.js');
    const pool = createGoonMediaPool();
    pool.setManifest({ images: [], videos: [], skipped: 0, truncated: false });   // what bridge.js synthesizes
    ok(pool.hasMedia() === false, 'the standalone pool starts empty, exactly as the player found it');
    const counts = pool.setLocalLibrary(store.localDeck());
    ok(counts.images === 2 && counts.videos === 1,
      'and setLocalLibrary(store.localDeck()) fills it — a solo session now has something to draw',
      counts.images + 'i/' + counts.videos + 'v');
    const drawn = pool.drawKind('image');
    ok(drawn && drawn.url.indexOf('blob:') === 0, 'the deck really hands back one of the picked files',
      drawn ? drawn.url.slice(0, 12) : 'null');

    // Removing a pick moves the version, so boot re-feeds the deck.
    const gone = store.localItems.find((it) => it.name === 'zip-clip.mp4');
    ok(store.removeLocal(gone.id) === true && store.localVersion === v1 + 1,
      'removeLocal bumps localVersion too — the deck must lose it as well');
    ok(pool.setLocalLibrary(store.localDeck()).videos === 0, 'and re-feeding drops the clip from the pool');
    store.dispose();
  }

  /* --- a HOSTED cache-list must NOT move the local version --------------- */
  {
    const b = fakeBridge();
    const store = createAssetsStore({ bridge: b, session: { caps: {} }, logger: null, autoHello: false });
    const before = store.localVersion;
    b.fire({ type: 'cache-list', seq: 0, last: true, items: [mkItem(1, { state: 'ready' }), mkItem(2, { state: 'ready' })] });
    ok(store.items.length === 2, 'the hosted list landed');
    ok(store.localVersion === before && store.localDeck().length === 0,
      'but localVersion did not move and the local deck stays empty — the host manifest owns the hosted deck, '
      + 'and boot\'s version guard is what keeps a cache-list from re-dealing the pool');
    store.dispose();
  }

  /* --- boot really wires it ---------------------------------------------- */
  {
    const bootSrc = await read('../boot.js');
    ok(/import \{ createAssetsStore, localPlayableEntries \} from '\.\/ui\/assetsStore\.js'/.test(bootSrc),
      'boot.js imports the mapper from the store (one definition of "playable", not two)');
    ok(/function syncLocalDeck\(\)/.test(bootSrc), 'boot.js has syncLocalDeck()');
    ok(/media\.setLocalLibrary\(/.test(bootSrc), 'which feeds exec/media.js the local half of the deck');
    ok(/assets\.onItems\(\(\) => syncLocalDeck\(\)\)/.test(bootSrc),
      'and is re-run on every store change, so a pick made mid-session lands in the deck');
    ok(/if \(v === syncedLocalVersion\) return;/.test(bootSrc),
      'guarded on localVersion, so a hosted cache-list never re-deals the pool');
    const mediaSrc = await read('../exec/media.js');
    ok(/setLocalLibrary\(list\)/.test(mediaSrc) && /let localEntries = \[\]/.test(mediaSrc),
      'exec/media.js keeps the local half as its own list…');
    ok(/`localEntries` is not touched/.test(mediaSrc),
      '…and says out loud that setManifest must not wipe it');
  }
}

await sleep(60);
console.log(`\nselftest-assets: ${n - failures}/${n} checks passed`);
if (failures > 0) {
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
process.exit(0);
