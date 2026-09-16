/* ============================================================================
 * race/smoke/wall-dom-check.mjs - pictures on the tube wall from the first metre.
 *
 *   node race/smoke/wall-dom-check.mjs   (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * THE BUG THIS FILE EXISTS FOR. rooms.js rollRoomOrder puts the Tea Garden first in
 * every single run, and the Tea Garden used to be the one room with no wall pictures
 * at all: walls.js handed it wallPosters' Region I, which that file keeps deliberately
 * BARE (REGION_MAX[1] === 0), and wallDom.js gave it density 0. The room runs 222..560 m,
 * which is 10..25 s at KART_BASE_SPEED, so every run opened on a bare wall no matter what
 * media the player had. A property nobody wrote down is a property somebody puts back.
 *
 * Two halves.
 *
 * PURE. race/wallWarm.js is the reason a run can OPEN on pictures instead of loading
 * them once the flag has dropped, so its contract is held here with a stubbed Image:
 * it tops up to what was asked, holds only what finished, is idempotent, and lets the
 * set go when the pool empties (a feed switched off). Plus a source guard on walls.js,
 * because that file imports three and cannot be loaded in node.
 *
 * BROWSER. A phone viewport (390x844) with a STUB FEED - pngs this file makes and
 * serves itself, so nothing is ever fetched from a third party - and the transport
 * double the web host installs. Two runs: the manifest landing with `ready` (the wall
 * must carry >= 6 loaded posters inside the first 5 s) and the manifest landing AFTER
 * the world was built (the layer used to be inert for the rest of that run).
 *
 * REMOTE URLS ARE NEVER TEXTURES. Every stub row here is an absolute url, which is what
 * hostMedia.js splits on, so the pool it lands in is the DOM-only one by construction.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, readFile as read } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web
void read;

/* ============================================================================
 * 1. race/wallWarm.js: the pictures a run opens on
 * ==========================================================================*/
/** A stubbed Image: a src lands one turn later, exactly the way a cached decode does. */
let imgsMade = 0;
globalThis.Image = class {
  constructor() { this.decoding = ''; this.naturalWidth = 0; this.naturalHeight = 0; this.onload = null; this.onerror = null; this._src = ''; imgsMade++; }
  get src() { return this._src; }
  set src(v) {
    this._src = v;
    if (!v) return;
    setTimeout(() => {
      if (String(v).indexOf('broken') >= 0) { if (this.onerror) this.onerror(); return; }
      this.naturalWidth = 120; this.naturalHeight = 180;
      if (this.onload) this.onload();
    }, 0);
  }
};

const { warmWallPosters, warmShots, clearWarmPosters, WARM_MAX } = await import('../wallWarm.js');

/** hostMedia's drawRemoteDom, in miniature: a pool of remote rows handed out one at a time. */
function fakeMedia(n, prefix = 'https://cdn.example/pic') {
  let left = n;
  return { drawRemoteDom: () => (left-- > 0 ? { kind: 'image', name: 'p', remote: true, acquire: async () => ({ url: `${prefix}${left}.jpg`, release() {} }) } : null) };
}

eq(warmShots().length, 0, 'the warm set starts empty');
warmWallPosters(fakeMedia(20), 12);
await sleep(30);
eq(warmShots().length, 12, 'a manifest with a feed in it warms the number of pictures the wall wants');
ok(warmShots().every((s) => s.url && s.aspect > 0 && s.img), 'and every warmed picture carries its url, its aspect and its own decoded element');

const madeAfterFirst = imgsMade;
warmWallPosters(fakeMedia(20), 12);
await sleep(30);
eq(warmShots().length, 12, 'a second manifest with the set already full warms nothing again');
eq(imgsMade, madeAfterFirst, 'and asks the pool for no further pictures');

warmWallPosters(fakeMedia(200), 999);
await sleep(30);
ok(warmShots().length <= WARM_MAX, `the set is capped at ${WARM_MAX} however many the caller asks for (got ${warmShots().length})`);

clearWarmPosters();
eq(warmShots().length, 0, 'clearing lets the whole set go');
warmWallPosters({ drawRemoteDom: () => null }, 12);
await sleep(30);
eq(warmShots().length, 0, 'a pool with no feed in it (the desktop, or consent off) warms nothing and costs nothing');
warmWallPosters({}, 12);
eq(warmShots().length, 0, 'and a media source with no drawRemoteDom at all is simply ignored');

/* ============================================================================
 * 2. the source guard walls.js cannot have as a unit test (it imports three)
 * ==========================================================================*/
const wallsSrc = readFileSync(resolve(RACE, 'walls.js'), 'utf8');
const setRegionLine = (wallsSrc.match(/posters\.setRegion\([^;]*\);/) || [''])[0];
ok(setRegionLine.length > 0, 'walls.js still sets a poster region per room');
ok(!/teagarden['"]\s*\?\s*1\b/.test(setRegionLine),
  'and the Tea Garden is no longer pinned to Region I, which wallPosters keeps bare - it is the first room of every run');
const domSrc = readFileSync(resolve(RACE, 'wallDom.js'), 'utf8');
const densityLine = (domSrc.match(/const density = [^;]*;/) || [''])[0];
ok(densityLine.length > 0 && !/teagarden['"]\s*\?\s*0\b/.test(densityLine) && !/teagarden['"]\s*\)\s*\?\s*0\b/.test(densityLine),
  'and the DOM wall gives the Tea Garden a density above zero for the same reason');

/* ============================================================================
 * 3. the browser: a phone, a stub feed, and a wall that has pictures on it
 * ==========================================================================*/
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8871;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

/** A solid png, built here. NOTHING in this file is fetched from a third party. */
const CRC = (() => { const t = []; for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
const crc32 = (buf) => { let c = 0xffffffff; for (const b of buf) c = CRC[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function png(w, h, seed) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  const raw = Buffer.alloc(h * (1 + w * 3));
  for (let y = 0; y < h; y++) {
    const off = y * (1 + w * 3);
    for (let x = 0; x < w; x++) { raw[off + 1 + x * 3] = (seed * 7 + x) & 255; raw[off + 2 + x * 3] = (seed * 13 + y) & 255; raw[off + 3 + x * 3] = 200; }
  }
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
const STUBS = new Map();
for (let i = 0; i < 24; i++) STUBS.set(`${i}.png`, png(120 + (i % 3) * 30, 170 + (i % 4) * 20, i + 1));

/** The transport double the web host installs (cclabs-web scripts/race-web-ext/race-web-ext.js),
 *  cut down to what this check needs: init, favorites, the pile's own empty manifest, then the
 *  feed's manifest `?feedDelay` ms later. `?feedDelay=0` is the ordinary case. */
const STUB_HOST = `(function () {
  var Q = new URLSearchParams(location.search);
  var delay = Number(Q.get('feedDelay') || 0) || 0, handlers = [];
  function deliver(f) { handlers.slice().forEach(function (h) { try { h({ data: f }); } catch (e) { console.error(e); } }); }
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener: function (t, fn) { if (t === 'message' && typeof fn === 'function') handlers.push(fn); },
    removeEventListener: function (t, fn) { handlers = handlers.filter(function (h) { return h !== fn; }); },
    postMessage: function (m) {
      if (!m || m.type !== 'ready') { if (m && m.type === 'ping') deliver({ type: 'pong', t: m.t }); return; }
      deliver({ type: 'init', protocol: 1, modId: null, modContent: null, settings: {
        masterVolume: 0, reducedMotion: false, trackPick: false, hostSfx: false, canSurface: false,
        cloud: true, mediaControls: true, remoteConsent: true, remoteMediaRatio: 1,
        remoteCatalog: [{ id: 'a', label: 'a' }], niches: ['a'],
        localMedia: { images: 0, videos: 0, skipped: 0, active: false, folder: false } } });
      deliver({ type: 'favorites', names: [] });
      deliver({ type: 'manifest', images: [], videos: [], skipped: 0, truncated: false });
      setTimeout(function () {
        var images = [];
        for (var i = 0; i < 24; i++) images.push({ name: 'online100:s' + i + '.png', url: location.origin + '/_stub/' + (i % 24) + '.png' });
        deliver({ type: 'manifest', images: images, videos: [], skipped: 0, truncated: false });
      }, delay);
    },
  };
  window.__raceWebExt = { version: 1, deliver: deliver, sources: { register: function () { return function () {}; } }, refreshManifest: function () {} };
})();`;

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  if (path === '/_stub/host.js') { res.writeHead(200, { 'content-type': 'text/javascript' }); return res.end(STUB_HOST); }
  if (path.startsWith('/_stub/')) {
    const b = STUBS.get(path.slice(7));
    if (!b) { res.writeHead(404); return res.end(); }
    res.writeHead(200, { 'content-type': 'image/png' }); return res.end(b);
  }
  try {
    let body = await readFile(join(WEB, path));
    // the double has to be a CLASSIC script above raceBoot's module tag or bridge.js latches
    // isHosted false; that is the whole reason race-web-ext.js is injected the same way
    if (extname(path).toLowerCase() === '.html') {
      body = Buffer.from(String(body).replace('<script type="module" src="./raceBoot.js">', '<script src="/_stub/host.js"></script>\n    <script type="module" src="./raceBoot.js">'), 'utf8');
    }
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-wall-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9341', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=390,844', 'about:blank',
], { stdio: 'ignore' });

async function done(code) {
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* windows holds the profile */ }
  process.exit(code);
}

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9341/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map();
let errs = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push('thrown: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text || '?'));
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push(m.params.args.map((a) => a.value ?? a.description ?? '?').join(' '));
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });

const site = `http://127.0.0.1:${PORT}`;
/** What the wall is carrying: elements up, pictures decoded, and how many are actually painted.
 *  It reports the PAGE's own clock with them, so every second below is counted from the
 *  navigation. The layer is built on the first frame that has a picture to hang, so a clock
 *  started when `.rh-wall3d` appears would measure nothing at all. */
const WALL = `(()=>{ const els=[...document.querySelectorAll('.rh-wall3d-shot')];
  const loaded=els.filter((e)=>e.complete&&e.naturalWidth>0);
  const shown=loaded.filter((e)=>{const s=getComputedStyle(e);return s.visibility!=='hidden'&&Number(s.opacity)>0.01&&e.offsetParent!==null;});
  const box=document.querySelector('.rh-wall3d');
  return { t: performance.now()/1000, layer: !!box, slots: els.length, loaded: loaded.length, shown: shown.length,
    over: shown.filter((e)=>{const b=e.getBoundingClientRect();return b.bottom>innerHeight*0.98||b.width<=0;}).length }; })()`;

/** Poll the wall until the page's own clock passes `sec`, and report the first moment it
 *  carried `want` painted posters, measured from the navigation. */
async function watch(want, sec) {
  let best = { t: 0, shown: 0, loaded: 0, slots: 0, layer: false, over: 0 }, at = null, layer = false;
  for (;;) {
    let w = null;
    try { w = await json(WALL); } catch (e) { w = null; }   // still navigating
    if (w) {
      layer = layer || w.layer;
      if (w.shown > best.shown) best = w;
      if (at == null && w.shown >= want) { at = Math.round(w.t * 1000) / 1000; break; }
      if (w.t >= sec) break;
    }
    await sleep(120);
  }
  return { best, at, layer };
}

// ---- run one: the manifest lands with `ready`, the way the web host posts it ----------
errs = [];
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&feedDelay=0` });
const first = await watch(6, 5);
ok(first.layer, 'the run builds the DOM wall layer');
ok(first.at != null, `>= 6 posters with a loaded picture are on the wall inside the first 5 s of the page (${first.best.shown} up, ${first.best.loaded} of ${first.best.slots} decoded, first six at ${first.at}s)`);
eq(first.best.over, 0, 'and not one of them hangs off the bottom of the glass');
eq(errs.length, 0, 'with nothing on the console' + (errs.length ? ': ' + errs.slice(0, 4).join(' | ') : ''));

// ---- run two: the manifest lands AFTER the world was built ----------------------------
// The feed's own build asks its source twice and waits on the network, so on a phone the
// merged manifest routinely lands seconds after the page did. The layer used to draw its
// whole set once, at world build, and a layer built before that frame stayed dead all run.
errs = [];
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&feedDelay=3500` });
const late = await watch(6, 12);
ok(late.at != null && late.at > 3, `the feed only answered at 3.5 s, so nothing was on the wall before it (first six at ${late.at}s)`);
ok(late.at != null && late.at < 9, `and it still reached the wall mid-run, without a rebuild (${late.best.shown} posters, ${late.best.loaded} of ${late.best.slots} decoded)`);
eq(errs.length, 0, 'with nothing on the console' + (errs.length ? ': ' + errs.slice(0, 4).join(' | ') : ''));

console.log(fails ? `\n${fails} FAILED` : '\nall good');
await done(fails ? 1 : 0);
