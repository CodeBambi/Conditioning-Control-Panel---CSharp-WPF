/* ============================================================================
 * backroom/smoke/room-check.mjs - the room shell, pure first, then the real page
 * in headless Chrome with a fake host on chrome.webview.
 *
 *   node backroom/smoke/room-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: the page is served from Resources/web on
 * 127.0.0.1, stations/slot/station.js is replaced by smoke/mock-station.js, and
 * the fake host answers ready/station-request/media-request itself.
 *
 * Screenshots (into evidenceDir, default ./_evidence): the room at 1280x720 and
 * 1920x1080, a walk to the slot hotspot mid-stride and on arrival with the mock
 * station open, a soon station's dust-sheet card, and the reduced-motion walk.
 * CHROME: CHROME_PATH, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { FLOOR, DOOR, ROOM_W, ROOM_H, onFloor, normaliseStations, resolveClick, toArt } from '../room/geometry.js';

let fails = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const BACKROOM = resolve(HERE, '..');
const WEB = resolve(BACKROOM, '..');
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

/* ---------------------------------------------------------------- 1. pure */
const rows = JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8'));
const stations = normaliseStations(rows);
ok(stations.length === 5, 'stations.json has the five contract rows');
ok(['slot', 'wheel', 'scratcher', 'cards', 'counter'].every((id) => stations.some((s) => s.id === id)), 'with the contract ids');
ok(stations.find((s) => s.id === 'slot').state === 'live', 'slot is live');
ok(stations.filter((s) => s.state === 'soon').length === 4, 'the other four are soon');
ok(stations.every((s) => s.placed), 'every hotspot has a stand on the floor');
ok(stations.every((s) => s.hotspot[0] >= 0 && s.hotspot[1] >= 0 && s.hotspot[0] + s.hotspot[2] <= ROOM_W && s.hotspot[1] + s.hotspot[3] <= ROOM_H), 'every hotspot is inside the art');
ok(onFloor(DOOR), 'the door stands on the floor');
ok(resolveClick([700, 460], stations).kind === 'floor', 'a click on the spiral carpet is a floor walk');
ok(resolveClick([20, 20], stations).kind === 'none', 'a click on the dark outside the walls is nothing');
ok(resolveClick([200, 500], stations).station?.id === 'slot', 'a click on the machines is the slot');
ok(normaliseStations([{ id: 'x', state: 'live', entry: 'https://evil/x.js', hotspot: [0, 0, 1, 1], stand: [700, 400] }])[0].state === 'soon', 'a live row with a foreign entry is demoted to soon');
{
  const [x, y] = toArt(640, 360, { left: 0, top: 0, width: 1280, height: 720 });
  ok(Math.abs(x - ROOM_W / 2) < 1 && Math.abs(y - ROOM_H / 2) < 1, 'client centre maps to art centre');
}

/* ---------------------------------------------------------------- 2. page */
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = 8891, DEBUG_PORT = 9391;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.webp': 'image/webp', '.svg': 'image/svg+xml' };
const server = createServer(async (req, res) => {
  let path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  const file = path === '/backroom/stations/slot/station.js' ? join(HERE, 'mock-station.js') : join(WEB, path);
  try {
    const body = await readFile(file);
    res.writeHead(200, { 'content-type': MIME[extname(file).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch (e) { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

/* The fake host, installed before any page script. It records every frame and
 * answers the three requests the room and the mock make. */
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
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: q.get('reduced') === '1',
        motion: q.get('reduced') === '1' ? 'reduced' : 'full', intensity: 'normal', lang: 'en',
        lex: { br_back: 'Back', br_balance: 'SP', br_station_slot: 'The Slot', br_station_wheel: 'Daily Spin',
          br_soon_body: 'Under a dust sheet for now. This one opens soon.' }, stations: ['slot'], open: null });
      if (m.type === 'station-request') emit({ type: 'station-result', reqId: m.reqId, ok: true, status: 200, body: { ok: true, sp: 57 } });
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, gifs: [], words: [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-room-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', 'about:blank'], { stdio: 'ignore' });

async function done(code) {
  try { chrome.kill(); } catch (e) { /* noop */ }
  server.close();
  await sleep(300);
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
  if (m.method === 'Network.loadingFailed' && !m.params.canceled) errs.push('load failed: ' + m.params.errorText);
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });

async function shot(name) {
  const r = await cdp('Page.captureScreenshot', { format: 'png' });
  await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64'));
  console.log('  shot ' + join(OUT, name));
}
async function boot(w, h, query) {
  await cdp('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html${query || ''}` });
  for (let i = 0; i < 80; i++) {
    await sleep(100);
    if (await ev(`document.documentElement.classList.contains('br-ready') && !!document.querySelector('.br-art') && document.querySelector('.br-art').getBBox().width > 0`)) break;
  }
  await sleep(400);   // let the png decode
}
/** Press at art coordinates through the real pointer path. */
async function pressArt(x, y) {
  const [cx, cy] = await ev(`(() => { const b = document.querySelector('.br-scene').getBoundingClientRect();
    const s = Math.min(b.width / ${ROOM_W}, b.height / ${ROOM_H});
    return [b.left + (b.width - ${ROOM_W} * s) / 2 + ${x} * s, b.top + (b.height - ${ROOM_H} * s) / 2 + ${y} * s]; })()`);
  await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x: cx, y: cy, button: 'left', clickCount: 1 });
  await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x: cx, y: cy, button: 'left', clickCount: 1 });
}
const posted = (type) => ev(`window.__posted.filter((m) => m.type === ${JSON.stringify(type)})`);

// 2a. the room at two sizes
await boot(1280, 720);
ok((await posted('ready')).length === 1, 'the page posts ready once');
ok(await ev(`document.querySelectorAll('.br-spot').length`) === 5, 'five hotspots drawn');
ok(await ev(`document.querySelector('#br-sp-value').textContent`) === '57', 'the SP chip shows init.sp');
ok(await ev(`!!document.querySelector('.br-you .gh-sprite')`), 'the player wears the campus student sprite');
await shot('room-1280x720.png');
await boot(1920, 1080);
await shot('room-1920x1080.png');

// 2b. walk to the slot, arrive, mock station opens
await boot(1280, 720);
await pressArt(210, 520);
await sleep(250);
ok(await ev(`window.__backroom.scene.walking()`), 'a press on the slot starts a walk');
await shot('walk-to-slot-mid.png');
for (let i = 0; i < 40 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.state)`)); i++) await sleep(100);
const at = await ev(`window.__backroom.scene.at()`);
ok(Math.abs(at[0] - 362) < 1 && Math.abs(at[1] - 566) < 1, 'the player stands on the slot stand');
ok((await posted('station-open')).some((m) => m.station === 'slot'), 'station-open slot was posted');
ok(await ev(`window.__mockStation.seen.opened && window.__mockStation.seen.state.ok === true`), 'the mock opened and its ctx.request got a result');
ok(await ev(`['root','bridge','request','fx','media','sp','onSp','reduced','motion','intensity','lex','standUp'].every((k) => window.__mockStation.ctxKeys.includes(k))`), 'ctx carries every section 7 field');
await sleep(200);
await shot('walk-to-slot-station-open.png');
await ev(`document.querySelector('#br-back').click()`);
await sleep(300);
ok((await posted('station-close')).some((m) => m.station === 'slot'), 'Back closes the station (station-close posted)');
ok(await ev(`window.__mockStation.seen.closed && window.__mockStation.seen.destroyed`), 'close() then destroy() were called');
ok((await posted('exit')).length === 0, 'and the room itself stays open');

// 2c. a soon station gets the dust-sheet card and never loads code
await pressArt(385, 230);
for (let i = 0; i < 40 && !(await ev(`!!document.querySelector('.br-card-veil.is-soon')`)); i++) await sleep(100);
ok(await ev(`!!document.querySelector('.br-card-veil.is-soon')`), 'the wheel shows its dust-sheet card');
ok((await posted('station-open')).every((m) => m.station !== 'wheel'), 'a soon station never posts station-open');
await sleep(400);
await shot('soon-station-card.png');
await cdp('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
await sleep(150);
ok(!(await ev(`!!document.querySelector('.br-card-veil')`)), 'Escape closes the card');
await cdp('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
await sleep(200);
ok((await posted('exit')).some((m) => m.reason === 'key'), 'Escape in the empty room posts exit (key)');
ok((await posted('exit-done')).length === 1, 'and exit-done follows');

// 2d. reduced motion: the state, not the travel
await boot(1280, 720, '?reduced=1');
await pressArt(880, 560);
await sleep(60);
const floorSnap = await ev(`window.__backroom.scene.at()`);
ok(Math.abs(floorSnap[0] - 880) < 1 && Math.abs(floorSnap[1] - 560) < 1 && !(await ev(`window.__backroom.scene.walking()`)), 'reduced motion: a floor walk lands on the same frame');
await shot('reduced-motion-snap.png');
await pressArt(210, 520);
await sleep(30);
const snapped = await ev(`window.__backroom.scene.at()`);
ok(Math.abs(snapped[0] - 362) < 1 && Math.abs(snapped[1] - 566) < 1, 'reduced motion: the player is on the stand at once');
ok(await ev(`document.documentElement.classList.contains('br-reduced')`), 'reduced motion flag reaches the page');
for (let i = 0; i < 20 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.state)`)); i++) await sleep(100);
ok(await ev(`!!(window.__mockStation && window.__mockStation.seen.opened)`), 'and the station still opens');
await sleep(200);
await shot('reduced-motion-station-open.png');

// 2e. host close with a station open: exit-done inside the 300 ms budget
await boot(1280, 720);
await pressArt(210, 520);
for (let i = 0; i < 40 && !(await ev(`!!(window.__mockStation && window.__mockStation.seen.opened)`)); i++) await sleep(100);
const settleMs = await ev(`new Promise((r) => { const t0 = performance.now();
  window.__hostEmit({ type: 'close', reason: 'panic' });
  const tick = () => window.__posted.some((m) => m.type === 'exit-done') ? r(performance.now() - t0) : setTimeout(tick, 5);
  tick(); })`);
ok(settleMs < 300, 'host close: exit-done after ' + Math.round(settleMs) + ' ms (budget 300)');
ok((await posted('station-close')).some((m) => m.station === 'slot'), 'and the open station was closed on the way out');
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall room checks passed');
await done(fails ? 1 : 0);
