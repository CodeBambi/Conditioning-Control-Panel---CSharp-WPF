/* ============================================================================
 * backroom/smoke/room-check.mjs - the 3D room, pure first, then the real page
 * in headless Chrome with a fake host on chrome.webview.
 *
 *   node backroom/smoke/room-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: the page is served from Resources/web on
 * 127.0.0.1, stations/slot/station.js is replaced by smoke/mock-station.js, and
 * the fake host answers ready/station-request/media-request itself. The only
 * process this ever stops is the Chrome it started, by its own handle.
 *
 * Evidence: screenshots at every approach, the room view, the mock slot, the
 * soon card and the still room, plus room-perf.json (calls, triangles, frame
 * median at 1280x720, build and resume times).
 * CHROME: CHROME_PATH, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, readdirSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { START, normaliseStations, blocked, reachable, nearestStation, facing } from '../room/walk.js';

let fails = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const BACKROOM = resolve(HERE, '..');
const WEB = resolve(BACKROOM, '..');
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

/* ---------------------------------------------------------------- 1. pure */
const stations = normaliseStations(JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8')));
ok(stations.length === 7, 'stations.json has seven fixture rows');
ok(stations.some((s) => s.id === 'counter' && s.state === 'soon'), 'the counter is soon');
ok(stations.some((s) => s.id === 'cards' && s.state === 'live' && s.entry === 'stations/cards/station.js'), 'cards (Soft Hand) is live');
ok(stations.some((s) => s.id === 'roulette' && s.state === 'live' && s.entry === 'stations/roulette/station.js'), 'the roulette is live on its own station');
ok(stations.some((s) => s.id === 'wheel' && s.state === 'live' && s.entry === 'stations/wheel/station.js'), 'the wheel is live on its own station');
ok(!stations.some((s) => s.id === 'scratcher'), 'no scratcher');
const slots = stations.filter((s) => s.id === 'slot');
ok(slots.length === 3 && slots.every((s) => s.state === 'live' && s.entry === 'stations/slot/station.js'), 'three live slot rows, one station');
ok(slots.map((s) => s.variant).join() === 'rose,violet,mint', 'with the rose, violet and mint variants');
ok(normaliseStations([{ ...JSON.parse(JSON.stringify(slots[0])), entry: 'https://evil/x.js' }])[0].state === 'soon', 'a live row with a foreign entry is demoted to soon');
for (const s of stations) {
  ok(!blocked(s.approach[0], s.approach[2], stations), s.key + ': approach stands clear of every body');
  ok(reachable(START, s.approach, stations), s.key + ': approach is reachable from the door');
  ok(nearestStation(s.approach, stations) === s, s.key + ': standing there makes it the nearest');
}
ok(blocked(0, -5, stations) && blocked(-5.9, 0.5, stations) && blocked(7, 0, stations), 'the counter, a slot and the wall block');
{
  const f = facing([0, 1.65, 0], [0, 1.65, -1]);
  ok(Math.abs(f.yaw) < 1e-9 && Math.abs(f.pitch) < 1e-9, 'facing -z is yaw 0');
}

// Every glb the registry names is on disk and carries the names the room reads.
function glbNodes(file) {
  const b = readFileSync(file);
  if (b.readUInt32LE(0) !== 0x46546c67) return null;
  const json = JSON.parse(b.subarray(20, 20 + b.readUInt32LE(12)).toString('utf8'));
  return new Set((json.nodes || []).map((n) => n.name).filter(Boolean));
}
const NEED = {
  'shell.glb': ['ceiling', 'spiral_inlay', 'media_screen_0', 'media_screen_1', 'media_screen_2', 'media_screen_3', 'sconce_globe'],
  'slot.glb': ['EMI_glass', 'marquee', 'screen_jackpot', 'screen_status', 'reel_1', 'reel_2', 'reel_3', 'lights_chase_00', 'lights_chase_29'],
  'wheel.glb': ['EMI_glass', 'title_screen', 'status_screen', 'bulb_00'],
  'counter.glb': ['golden_emi_attendant', 'EMI_glass'],
  'card-table.glb': ['emi_dealer', 'alcove_return', 'EMI_glass'],
  'roulette.glb': ['center_spiral', 'emi_dealer', 'rim_bulb_0', 'canopy_bulb_0', 'inset_spiral_0', 'EMI_glass'],
};
const ASSETS = join(BACKROOM, 'room', 'assets');
for (const [file, names] of Object.entries(NEED)) {
  const nodes = existsSync(join(ASSETS, file)) ? glbNodes(join(ASSETS, file)) : null;
  ok(!!nodes && names.every((n) => nodes.has(n)), file + ' has ' + names.length + ' looked-up nodes');
}
ok(stations.every((s) => readdirSync(ASSETS).includes(s.fixture.file)), 'every registry fixture file is present');

/* ---------------------------------------------------------------- 2. page */
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = 8891, DEBUG_PORT = 9391;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  const file = path === '/backroom/stations/slot/station.js' ? join(HERE, 'mock-station.js') : join(WEB, path);
  try {
    const body = await readFile(file);
    res.writeHead(200, { 'content-type': MIME[extname(file).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch (e) { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

/* The fake host. `?reduced=1` and `?calm=1` shape init; `?pictures=1` deals an animated GIF, a still WebP and a fallback. */
const FAKE_HOST = `(() => {
  const q = new URLSearchParams(location.search);
  const listeners = [];
  const emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  window.__hostEmit = emit;
  window.__posted = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(m);
      const reduced = q.get('reduced') === '1';
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced, motion: reduced ? 'reduced' : 'full',
        intensity: reduced || q.get('calm') === '1' ? 'calm' : 'normal', lang: 'en',
        lex: { br_back: 'Back', br_balance: 'SP', br_station_slot_rose: 'Candy Rose', br_soon_body: 'Under a dust sheet for now. This one opens soon.' },
        stations: ['slot'], open: null });
      if (m.type === 'station-request') emit({ type: 'station-result', reqId: m.reqId, ok: true, status: 200, body: { ok: true, sp: 57 } });
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: q.get('pictures') === '1' ? [{ key: 'g0', url: '/dtrh/assets/bubbles/effects/spirals/sp6.gif', w: 0, h: 0, src: 'pool' },
          { key: 'g2', url: '/backroom/room/assets/ads/dtrh.webp', w: 1280, h: 720, src: 'pool' },
          { key: 'g1', url: 'https://ccp.game/backroom/stations/slot/fallback/gif1.webp', src: 'fallback' }] : [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-room-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720',
  '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader', 'about:blank'], { stdio: 'ignore' });

async function done(code) {
  try { chrome.kill(); } catch (e) { /* noop: only our own child */ }
  server.close();
  await sleep(500);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* noop */ }
  process.exit(code);
}

let target = null;
for (let i = 0; i < 60 && !target; i++) {
  await sleep(250);
  try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!target) { console.error('FAIL chrome never answered'); await done(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push('console: ' + m.params.args.map((a) => a.value || a.description).join(' '));
  if (m.method === 'Network.loadingFailed' && !m.params.canceled && !/gif1\.webp/.test(m.params.errorText)) errs.push('load failed: ' + m.params.errorText);
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });

async function shot(name) {
  const r = await cdp('Page.captureScreenshot', { format: 'png' });
  await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64'));
  console.log('  shot ' + name);
}
const dbg = () => ev('window.__backroom.scene.debug()');
const posted = (type) => ev(`window.__posted.filter((m) => m.type === ${JSON.stringify(type)})`);
const key = async (code, type = 'keyDown') => cdp('Input.dispatchKeyEvent', { type, code,
  key: code === 'Escape' ? 'Escape' : code.slice(3).toLowerCase(), windowsVirtualKeyCode: code === 'Escape' ? 27 : code.charCodeAt(3) });
async function hold(code, ms) { await key(code); await sleep(ms); await key(code, 'keyUp'); }
async function boot(w, h, query) {
  await cdp('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
  const t0 = Date.now();
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html${query || ''}` });
  for (let i = 0; i < 300; i++) {
    await sleep(100);
    if (await ev(`document.documentElement.classList.contains('br-ready')`)) break;
  }
  await sleep(600);
  return Date.now() - t0;
}
const row = (k) => `window.__backroom.stations.find((s) => s.key === ${JSON.stringify(k)})`;
const perf = {};

// 2a. boot, fixtures, perf at 1280x720
perf.bootMs = await boot(1280, 720);
ok(await ev(`document.documentElement.classList.contains('br-ready')`), 'the room boots to br-ready');
ok((await posted('ready')).length === 1, 'the page posts ready once');
let d = await dbg();
ok(d.fixtures === 7 && d.screens === 4 && d.bulbs > 100, `seven fixtures, four screens, ${d.bulbs} bulbs`);
ok(await ev(`window.__backroom.scene.scene.getObjectByName('station_slot:mint') !== undefined`), 'the mint slot is set out');
ok((await posted('media-request')).some((m) => m.station === 'room'), 'the room asks the feed for its wall pictures');
await sleep(3000);
d = await dbg();
Object.assign(perf, { calls: d.calls, triangles: d.triangles, frameMedianMs: d.frameMedian, buildMs: d.buildMs });
ok(d.calls < 1312, `draw calls ${d.calls} (preview 1,312), triangles ${d.triangles}, frame median ${d.frameMedian?.toFixed(1)} ms`);
await shot('room-entry-1280x720.png');

// 2b. walk and look
const z0 = d.position[2];
await hold('KeyW', 600);
d = await dbg();
ok(d.position[2] < z0 - 0.5, 'W walks forward');
await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x: 640, y: 360, button: 'left', clickCount: 1 });
await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 740, y: 360, button: 'left', buttons: 1 });
await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x: 740, y: 360, button: 'left', clickCount: 1 });
ok(Math.abs((await dbg()).yaw) > 0.2, 'a drag turns the view');

// 2c. every approach: stand there, shot, push forward into the fixture, never clip
for (const s of stations) {
  await ev(`window.__backroom.scene.go(${row(s.key)})`);
  await sleep(250);
  await shot(`approach-${s.key.replace(':', '-')}.png`);
  await hold('KeyW', 1500);
  d = await dbg();
  const clear = await ev(`import('/backroom/room/walk.js').then((w) => !w.blocked(${d.position[0]}, ${d.position[2]}, window.__backroom.stations))`);
  ok(clear, s.key + ': walking into it stops outside its body');
}

// 2d. E at the violet slot opens the one slot station with the violet variant; Back restores the pose
await ev(`window.__backroom.scene.go(${row('slot:violet')})`);
await ev(`window.__backroom.scene.pose([-3.9, 1.65, 2.25], 1.4, -0.1)`);
await sleep(200);
const before = await dbg();
ok(before.nearest === 'slot:violet', 'standing by the violet slot makes it nearest');
ok(await ev(`!document.querySelector('.br-visit').hidden && /Visit/.test(document.querySelector('.br-visit').textContent)`), 'the Visit prompt shows');
await key('KeyE'); await key('KeyE', 'keyUp');
for (let i = 0; i < 40 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.state)`)); i++) await sleep(100);
ok((await posted('station-open')).some((m) => m.station === 'slot'), 'E posts station-open slot');
ok(await ev(`window.__mockStation.seen.variant && window.__mockStation.seen.variant.id === 'violet' && window.__mockStation.seen.variant.palette.candy_rose === 'ac83ed'`), 'ctx.variant carries violet and its palette');
ok(await ev(`['root','bridge','request','fx','media','sp','onSp','reduced','motion','intensity','lex','standUp','variant','hostBack','spReadout'].every((k) => window.__mockStation.ctxKeys.includes(k))`), 'ctx carries every section 7 field');
ok(await ev(`window.__mockStation.seen.hostBack === true`), 'ctx.hostBack tells the station the room owns Back');
d = await dbg();
ok(d.held && !d.running, 'the room loop stops while the station is open');
await sleep(300);
await shot('slot-violet-station-open.png');
const tBack = await ev(`(async () => { const t = performance.now(); document.querySelector('#br-back').click();
  for (let i = 0; i < 200; i++) { await new Promise((r) => requestAnimationFrame(r)); const d = window.__backroom.scene.debug(); if (d.running && !d.held) return performance.now() - t; }
  return -1; })()`);
perf.resumeMs = Math.round(tBack);
d = await dbg();
ok((await posted('station-close')).some((m) => m.station === 'slot'), 'Back closes the station');
ok(d.position.every((v, i) => Math.abs(v - before.position[i]) < 1e-9) && d.yaw === before.yaw && d.pitch === before.pitch, 'and the room resumes on the same spot and facing');
ok(tBack >= 0 && tBack < 1000, `resume took ${Math.round(tBack)} ms (rebuild would be ${perf.buildMs} ms)`);
ok((await posted('exit')).length === 0, 'the room itself stays open');
await ev(`window.__mockStation = null`);
await ev(`window.__backroom.scene.go(${row('slot:rose')})`);
await sleep(150);
await key('KeyE'); await key('KeyE', 'keyUp');
for (let i = 0; i < 40 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.opened)`)); i++) await sleep(100);
ok(await ev(`window.__mockStation && window.__mockStation.seen.variant && window.__mockStation.seen.variant.id === 'rose' && window.__mockStation.seen.variant.palette === null`), 'the rose slot opens the same station, no palette');
await key('Escape');
await sleep(300);

// 2d2. Back never keeps the keys (desk run: a clicked Back kept focus, a later Space closed the station, then the room)
{
  const press = async (which) => {
    const k = which === 'Space' ? { code: 'Space', key: ' ', text: ' ', windowsVirtualKeyCode: 32 } : { code: 'Enter', key: 'Enter', text: '\r', windowsVirtualKeyCode: 13 };
    await cdp('Input.dispatchKeyEvent', { type: 'keyDown', ...k });
    await sleep(40);
    await cdp('Input.dispatchKeyEvent', { type: 'keyUp', code: k.code, key: k.key, windowsVirtualKeyCode: k.windowsVirtualKeyCode });
    await sleep(160);
  };
  const openSlot = async () => {
    await ev(`window.__mockStation = null`);
    await ev(`window.__backroom.scene.go(${row('slot:violet')})`);
    await sleep(150);
    await key('KeyE'); await key('KeyE', 'keyUp');
    for (let i = 0; i < 40 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.opened)`)); i++) await sleep(100);
  };
  const stationOpen = () => ev(`!!window.__backroom.loader.current`);
  const counts = async () => [(await posted('station-close')).length, (await posted('exit')).length];
  await openSlot();
  const closed0 = (await counts())[0];
  const [bx, by] = await ev(`(() => { const r = document.querySelector('#br-back').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()`);
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x: bx, y: by });
  await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x: bx, y: by, button: 'left', clickCount: 1, buttons: 1 });
  await sleep(40);
  await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x: bx, y: by, button: 'left', clickCount: 1 });
  for (let i = 0; i < 20 && ((await stationOpen()) || (await counts())[0] === closed0); i++) await sleep(50);
  await sleep(200);
  ok(!(await stationOpen()), 'a real click on Back closes the station');
  ok(await ev(`document.activeElement !== document.querySelector('#br-back')`), 'and Back does not keep the focus');
  let before = await counts();
  await press('Space'); await press('Enter');
  ok(JSON.stringify(await counts()) === JSON.stringify(before) && (await dbg()).running, 'Space and Enter while walking leave nothing and keep walking');
  await openSlot();
  await ev(`document.querySelector('#br-back').focus()`);
  before = await counts();
  await press('Space'); await press('Enter');
  ok((await stationOpen()) && JSON.stringify(await counts()) === JSON.stringify(before), 'Space and Enter on a focused Back with a station open do not close it');
  ok(await ev(`document.activeElement !== document.querySelector('#br-back')`), 'and the focus is dropped for the station');
  await key('Escape');
  await sleep(600);
  await ev(`document.querySelector('#br-back').focus()`);
  before = await counts();
  await press('Space'); await press('Enter');
  ok(JSON.stringify(await counts()) === JSON.stringify(before), 'Space and Enter on a focused Back while walking do not leave the room');
  await ev(`document.querySelector('.br-nav .br-pill').focus()`);
  await press('Enter');
  ok(!(await dbg()).overview, 'Enter on a focused Room view button while walking does nothing');
}

// 2d3. the SP chip is the room's: Law I shownSp through a flight, a station-result, Back, a balance frame and a reopen
{
  const chipText = () => ev(`document.querySelector('#br-sp-value').textContent`);
  const frame = () => ev(`new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)))`);
  const openViolet = async () => {
    await ev(`window.__mockStation = null`);
    await ev(`window.__backroom.scene.go(${row('slot:violet')})`);
    await sleep(150);
    await key('KeyE'); await key('KeyE', 'keyUp');
    for (let i = 0; i < 40 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.state)`)); i++) await sleep(100);
  };
  await openViolet();
  ok(await ev(`(() => { const r = window.__mockStation.ctx.spReadout; return ['set','owe','thud','target'].every((k) => typeof r[k] === 'function') && r.target() === document.querySelector('.br-sp'); })()`), 'ctx.spReadout has set, owe, thud and target (the chip box)');
  await ev(`window.__mockStation.ctx.spReadout.owe(() => 12)`);
  ok(await chipText() === '45', 'owe 12 at SP 57: the chip reads 45');
  await ev(`window.__mockStation.ctx.spReadout.set(50)`);
  ok(await chipText() === '50', 'set(50) shows a bank tick as is');
  await ev(`window.__mockStation.ctx.spReadout.set(null)`);
  await ev(`window.__mockStation.ctx.request('state', {})`);
  await frame();
  ok(await chipText() === '45', 'set(null) and a station-result that carried sp both repaint to the rule, 45');
  await ev(`window.__mockStation.ctx.spReadout.thud()`);
  await key('Escape');
  await sleep(500);
  ok(!(await ev(`!!window.__backroom.loader.current`)) && await chipText() === '45', 'Back with wins unplayed: the chip stays 45');
  await ev(`window.__hostEmit({ type: 'balance', sp: 60, why: 'earn' })`);
  await sleep(100);
  ok(await chipText() === '48', 'a balance frame after close keeps what the tape owes: 60 - 12 = 48');
  await ev(`window.__hostEmit({ type: 'balance', sp: 57, why: 'spend' })`);
  await openViolet();
  await frame();
  ok(await chipText() === '45', 'reopen (its state reply carried sp 57): the chip does not dip before the station has its tape');
  await ev(`window.__mockStation.ctx.spReadout.owe(0)`);
  ok(await chipText() === '57', 'the tape played out (owe 0): the chip meets the balance');
  await key('Escape');
  await sleep(500);
}

// 2e. a soon station: the dust-sheet card, no code
await ev(`window.__backroom.scene.go(${row('counter')})`);
await sleep(150);
await key('KeyE'); await key('KeyE', 'keyUp');
for (let i = 0; i < 20 && !(await ev(`!!document.querySelector('.br-card-veil.is-soon')`)); i++) await sleep(100);
ok(await ev(`!!document.querySelector('.br-card-veil.is-soon')`), 'the counter shows its coming-soon card');
ok((await posted('station-open')).every((m) => m.station !== 'counter'), 'a soon station never posts station-open');
await sleep(400);
await shot('soon-counter-card.png');
await key('Escape');
await sleep(200);
ok(!(await ev(`!!document.querySelector('.br-card-veil')`)) && (await dbg()).running, 'Escape closes the card and the room walks again');

// 2f. the room view
await key('KeyM'); await key('KeyM', 'keyUp');
await sleep(400);
d = await dbg();
ok(d.overview && d.ceiling === false && await ev(`!document.querySelector('.br-map-list').hidden`), 'M opens the room view with the ceiling off and the list');
await shot('room-view.png');
await key('KeyM'); await key('KeyM', 'keyUp');
await sleep(200);
ok(!(await dbg()).overview, 'M again walks');

// 2f2. the room's Options (CONTRACT 10.14): effects intensity, tunnel vision and melt, each a room-option to the host
{
  const q = (sel) => `document.querySelector('.br-options ${sel}')`;
  const pressed = (sel) => ev(`${q(sel)}.getAttribute('aria-pressed')`);
  ok(await ev(`document.querySelector('.br-nav .br-pill:nth-child(3)').textContent === 'Options' && document.querySelector('.br-options').hidden`), 'an Options pill, its card closed');
  await ev(`document.querySelector('.br-nav .br-pill:nth-child(3)').click()`);
  ok(await ev(`!document.querySelector('.br-options').hidden`), 'Options opens the card');
  ok(await pressed('[data-option="tunnel"]') === 'true' && await pressed('[data-option="melt"]') === 'true' && await pressed('[data-value="normal"]') === 'true',
    'a host with no gates reads tunnel vision and melt on, Normal chosen');
  await ev(`${q('[data-option="tunnel"]')}.click()`);
  let sent = (await posted('room-option')).at(-1);
  ok(sent && sent.key === 'tunnel' && sent.value === false && await ev(`${q('[data-option="tunnel"]')}.textContent`) === 'Off', 'tunnel vision off: room-option {tunnel:false}, the switch reads Off');
  await ev(`${q('[data-value="calm"]')}.click()`);
  sent = (await posted('room-option')).at(-1);
  ok(sent && sent.key === 'intensity' && sent.value === 'calm', 'Calm: room-option {intensity:calm}');
  await ev(`window.__hostEmit({ type: 'settings', motion: 'reduced', intensity: 'calm', intensityChoice: 'full', reduced: true,
    gates: { flash: true, subliminal: true, spiral: true, brainDrain: false, tunnel: true, melt: false } })`);
  await sleep(150);
  ok(await pressed('[data-option="tunnel"]') === 'true' && await pressed('[data-option="melt"]') === 'false' && await pressed('[data-value="full"]') === 'true'
    && await ev(`!${q('.br-opt-note')}.hidden`), 'the host frame has the last word: tunnel on, melt off, Full chosen, the forced-Calm note shown');
  ok(await ev(`JSON.stringify(Object.keys(window.__backroom.state.gates))`) === '["flash","subliminal","spiral","brainDrain","tunnel","melt"]', 'the room gates carry tunnel and melt');
  await shot('room-options.png');
  const exits = (await posted('exit')).length;
  await key('Escape');
  await sleep(150);
  ok(await ev(`document.querySelector('.br-options').hidden`) && (await posted('exit')).length === exits, 'Escape closes the card and does not leave the room');
  // Anchored to its pill, and a press outside closes it (a press inside does not).
  await ev(`document.querySelector('.br-nav .br-pill:nth-child(3)').click()`);
  const fit = await ev(`(() => { const p = document.querySelector('.br-nav .br-pill:nth-child(3)').getBoundingClientRect();
    const c = document.querySelector('.br-options').getBoundingClientRect(); return { dr: Math.abs(c.right - p.right), gap: c.top - p.bottom }; })()`);
  ok(fit && fit.dr <= 1 && fit.gap >= 0 && fit.gap <= 16, 'the card hangs under the Options pill, right edges aligned (' + JSON.stringify(fit) + ')');
  await ev(`${q('[data-value="normal"]')}.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))`);
  ok(await ev(`!document.querySelector('.br-options').hidden`), 'a press inside the card keeps it open');
  await ev(`document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))`);
  ok(await ev(`document.querySelector('.br-options').hidden && document.querySelector('.br-nav .br-pill:nth-child(3)').getAttribute('aria-expanded') === 'false'`), 'a press outside the card closes it');
  await ev(`window.__hostEmit({ type: 'settings', motion: 'full', intensity: 'normal', reduced: false })`);
  await sleep(100);
}

// 2g. host close with a station open: exit-done inside the 300 ms budget
await ev(`window.__backroom.scene.go(${row('slot:mint')})`);
await sleep(150);
await key('KeyE'); await key('KeyE', 'keyUp');
for (let i = 0; i < 40 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.opened)`)); i++) await sleep(100);
const settleMs = await ev(`new Promise((r) => { const t0 = performance.now();
  window.__hostEmit({ type: 'close', reason: 'panic' });
  const tick = () => window.__posted.some((m) => m.type === 'exit-done') ? r(performance.now() - t0) : setTimeout(tick, 5);
  tick(); })`);
ok(settleMs < 300, 'host close: exit-done after ' + Math.round(settleMs) + ' ms (budget 300)');

// 2h. reduced motion: the floor and pictures hold, no sway, the toggle is locked
await boot(1280, 720, '?reduced=1');
const a0 = (await dbg()).floorAngle;
await hold('KeyW', 700);
d = await dbg();
ok(d.still && d.floorAngle === a0 && d.ambient === 0 && d.sway === 0, 'reduced motion: floor frozen, no sway while walking');
ok(await ev(`document.querySelector('.br-nav .br-pill:nth-child(2)').disabled`), 'and the Motion button cannot switch it back on');
await shot('reduced-motion-still.png');
await boot(1280, 720, '?calm=1');
const c0 = (await dbg()).floorAngle;
await sleep(500);
ok((await dbg()).floorAngle === c0, 'Calm intensity also holds the floor');
await ev(`window.__hostEmit({ type: 'settings', motion: 'full', intensity: 'normal', reduced: false })`);
await sleep(500);
ok((await dbg()).floorAngle !== c0, 'a settings change back to Normal lets it turn');

// 2i. the feed's pictures reach the walls, the GIF plays while its screen is in view; fallback entries do not
ok(await ev(`typeof ImageDecoder === 'function'`), 'this Chromium has ImageDecoder');
await boot(1280, 720, '?reduced=1&pictures=1');
await ev(`window.__backroom.scene.pose([0, 1.65, 2], Math.PI / 2, 0.25)`);
await sleep(1500);
d = await dbg();
ok(d.pictures === 2 && d.animation.animated === 1 && d.animation.frames === 0, 'reduced motion: the GIF is dealt but holds its first frame');
await boot(1280, 720, '?pictures=1');
for (let i = 0; i < 30 && (await dbg()).pictures !== 2; i++) await sleep(100);
d = await dbg();
ok(d.pictures === 2 && d.animation.animated === 1, 'two dealt pictures on the walls (one animated), the fallback entry skipped');
await ev(`window.__backroom.scene.pose([0, 1.65, 2], -Math.PI / 2, 0.25)`);   // facing away from screen 0
await sleep(700);
const away = (await dbg()).animation.frames;
await sleep(700);
ok((await dbg()).animation.frames - away <= 2, 'a GIF whose screens are out of view does not advance');
await ev(`window.__backroom.scene.pose([0, 1.65, 2], Math.PI / 2, 0.25)`);
const f0 = (await dbg()).animation.frames;
await sleep(1000);
const f1 = (await dbg()).animation.frames;
ok(f1 - f0 >= 5 && f1 - f0 <= 13, `in view it plays, capped: ${f1 - f0} frames in 1 s (max 12)`);
await shot('wall-picture-from-feed.png');

// 2j. Escape in the empty room leaves
await key('Escape');
await sleep(200);
ok((await posted('exit')).some((m) => m.reason === 'key') && (await posted('exit-done')).length === 1, 'Escape in the room posts exit (key) and exit-done');

perf.gpu = await ev(`(() => { const gl = document.createElement('canvas').getContext('webgl2'); const x = gl && gl.getExtension('WEBGL_debug_renderer_info'); return x ? gl.getParameter(x.UNMASKED_RENDERER_WEBGL) : 'unknown'; })()`);
await writeFile(join(OUT, 'room-perf.json'), JSON.stringify(perf, null, 2));
console.log('  perf ' + JSON.stringify(perf));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall room checks passed');
await done(fails ? 1 : 0);
