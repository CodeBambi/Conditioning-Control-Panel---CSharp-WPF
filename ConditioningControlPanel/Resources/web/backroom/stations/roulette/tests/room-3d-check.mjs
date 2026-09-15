/* ============================================================================
 * room-3d-check.mjs - the Velvet Vortex seated on the room's own fixture, on a phone, driven over CDP.
 *
 *   node backroom/stations/roulette/tests/room-3d-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Both phone orientations (ROULETTE_WIDTH x ROULETTE_HEIGHT to run one): the room seats the player on the
 * same top-down camera a desktop gets, framed on the mat while bets are open; every one of the 42 authored
 * bet targets projects inside the viewport, under no control, and a real touch on each lands a chip on that
 * cell. Spin eases the camera out to the whole table (all 37 pockets in view), the landing fires through the
 * real bridge, and the camera returns to the mat once bets reopen. No DOM betting grid exists any more.
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROULETTE_ROOM_PORT, default 8919, debug
 * +500). The only process it stops is the Chrome it started, by its own handle.
 * CHROME: CHROME_PATH, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../../../..');   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.ROULETTE_ROOM_PORT || 8919), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-roulette-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
  'about:blank'], { stdio: 'ignore' });
async function done(code) { try { chrome.kill(); } catch { /* our own child only */ } server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch { /* noop */ } process.exit(code); }

let target = null;
for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch { /* not up */ } }
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
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => {
  const r = await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true });
  if (r.result?.exceptionDetails) errs.push('eval: ' + (r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text));
  return r.result?.result?.value;
};
await cdp('Runtime.enable'); await cdp('Page.enable');
async function shot(name) {
  const r = await cdp('Page.captureScreenshot', { format: 'png' });
  await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64'));
  console.log('  shot ' + name);
}
async function until(expr, ms = 15000, step = 50) { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
async function tap(x, y) {
  await cdp('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] });
  await cdp('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
}
const report = {};

await cdp('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 2 });
await cdp('Emulation.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 Chrome/120.0 Mobile Safari/537.36' });
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: "Object.defineProperty(navigator,'deviceMemory',{get:()=>4});Object.defineProperty(navigator,'hardwareConcurrency',{get:()=>4});" });
const FAKE_HOST = `(() => {
  const listeners = [];
  const emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  let srv = null;
  const mock = () => srv || (srv = import('/backroom/stations/roulette/mock-server.js').then((m) => { const s = m.createMockServer({ sp: 57, floorMs: 0 }); s.script({ pocket: 18 }); window.__srv = s; return s; }));
  window.__posted = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(m);
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', lex: {},
        gates: { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true }, stations: ['slot', 'wheel', 'roulette'], open: true });
      if (m.type === 'station-request') mock().then((s) => s.handle(m.op, m.body)).then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body }));
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', src: 'pool' })) });
    },
  };
})();`;
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });

/** Every bet target's cell through the room camera, as viewport rects, with what the DOM shows at its centre. */
const RECTS = `(async () => { const T = await import('three'); const s = window.__backroom.scene, mat = s.scene.getObjectByName('roulette_runtime_mat'); if (!mat) return null;
  const r = s.renderer.domElement.getBoundingClientRect(), out = {}, v = new T.Vector3();
  mat.traverse((o) => { if (!o.userData.spot) return; const g = o.geometry.parameters; let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const x of [-g.width / 2, g.width / 2]) for (const y of [-g.height / 2, g.height / 2]) { o.localToWorld(v.set(x, y, 0)).project(s.camera); const px = r.left + (v.x + 1) * r.width / 2, py = r.top + (1 - v.y) * r.height / 2; x0 = Math.min(x0, px); y0 = Math.min(y0, py); x1 = Math.max(x1, px); y1 = Math.max(y1, py); }
    out[o.userData.spot] = { x: x0, y: y0, w: x1 - x0, h: y1 - y0, cover: document.elementFromPoint((x0 + x1) / 2, (y0 + y1) / 2)?.tagName || null, position: o.position.toArray() }; });
  return out; })()`;
/** The 37 pocket centres through the room camera. */
const POCKETS = `(async () => { const T = await import('three'); const s = window.__backroom.scene, f = s.scene.getObjectByName('station_roulette'), r = s.renderer.domElement.getBoundingClientRect(), out = [];
  for (let n = 0; n < 37; n++) { const v = new T.Box3().setFromObject(f.getObjectByName('pocket_' + n)).getCenter(new T.Vector3()).project(s.camera); out.push({ n, x: r.left + (v.x + 1) * r.width / 2, y: r.top + (1 - v.y) * r.height / 2 }); }
  return out; })()`;
const CHIP = `(async () => { const T = await import('three'), mesh = window.__backroom.scene.scene.getObjectByName('roulette_live_chips'), matrix = new T.Matrix4(); mesh.getMatrixAt(0, matrix); return { count: mesh.count, position: new T.Vector3().setFromMatrixPosition(matrix).toArray() }; })()`;
const pose = () => ev('(() => { const d = window.__backroom.scene.debug(); return { position: d.position, yaw: d.yaw, pitch: d.pitch, offset: d.viewOffset }; })()');
const samePose = (a, b) => a.position.every((v, i) => Math.abs(v - b.position[i]) < .01) && Math.abs(a.yaw - b.yaw) < .001 && Math.abs(a.pitch - b.pitch) < .001 && Math.abs(a.offset - b.offset) < .001;

const SIZES = process.env.ROULETTE_WIDTH ? [[Number(process.env.ROULETTE_WIDTH), Number(process.env.ROULETTE_HEIGHT || 800)]] : [[400, 800], [800, 400]];
for (const [width, height] of SIZES) {
  const name = width + 'x' + height, portrait = height > width, floor = portrait ? 20 : 26;   // css px, the narrowest number cell
  await cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: true });
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
  await until(`document.documentElement.classList.contains('br-ready')`, 40000, 100);
  await ev(`window.__posted.length = 0; window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'roulette'))`);
  ok(await until('!!document.querySelector(".roul-station[data-phase=bet]")', 12000, 100), `${name}: the room mounts and opens the roulette through the loader`);
  ok(await until('!window.__backroom.scene.transitioning', 5000), `${name}: the seat camera has arrived`);
  await sleep(300);
  ok(await ev("document.querySelectorAll('canvas').length === 1 && !document.querySelector('.roul-mat-strip') && getComputedStyle(document.querySelector('.roul-stage')).display === 'none'"), `${name}: one room canvas, no DOM betting grid: the 3D mat takes the bets`);
  ok(await ev("(() => { const f = window.__backroom.scene.scene.getObjectByName('station_roulette'); return f.getObjectByName('bet_number_assembly').visible === false && !!f.getObjectByName('roulette_mat_prints'); })()"), `${name}: the authored cream glyphs give way to the printed atlas`);
  const rects = await ev(RECTS), list = Object.entries(rects || {});
  const inside = list.filter(([, r]) => r.x >= 0 && r.y >= 0 && r.x + r.w <= width && r.y + r.h <= height), clear = list.filter(([, r]) => r.cover === 'CANVAS');
  ok(list.length === 42 && inside.length === 42 && clear.length === 42, `${name}: all 42 targets project inside the viewport under no control (${inside.length} inside, ${clear.length} clear)`);
  const numbers = list.filter(([k]) => /^s[1-9]/.test(k)), minW = Math.min(...numbers.map(([, r]) => r.w)), minH = Math.min(...numbers.map(([, r]) => r.h));
  ok(minW >= floor && minH >= floor * .85, `${name}: number cells at least ${floor} px wide while bets are open (min ${minW.toFixed(1)} x ${minH.toFixed(1)})`);
  const controls = await ev("(() => { const b = document.querySelector('.roul-controls').getBoundingClientRect(); return { x: b.x, y: b.y, w: b.width, h: b.height }; })()");
  const covered = list.filter(([, r]) => r.x < controls.x + controls.w && r.x + r.w > controls.x && r.y < controls.y + controls.h && r.y + r.h > controls.y).map(([k]) => k);
  ok(covered.length === 0, `${name}: the control bar covers no target` + (covered.length ? ': ' + covered.join(', ') : ''));
  report[name] = { cells: { minW, minH }, controls, rects };
  await shot(`phone-3d-${name}-idle.png`);
  let placed = 0;
  for (const [spot, r] of list) {
    await tap(Math.round(r.x + r.w / 2), Math.round(r.y + r.h / 2)); await sleep(40);
    const shown = await ev(CHIP);
    if (shown?.count === 1 && Math.hypot(shown.position[0] - r.position[0], shown.position[2] - r.position[2]) < .001) placed++;
    else console.log('  miss ' + spot + ' ' + JSON.stringify(shown));
    if (spot === 's18') await shot(`phone-3d-${name}-chip.png`);
    await ev("document.querySelector('.roul-clear').click()");
  }
  ok(placed === 42, `${name}: a touch on each of the 42 cells lands a chip on that cell (${placed}/42)`);
  // A fingertip beside a narrow cell still picks it: a touch just off the mat's right edge, inside the 40 px pad, reads as 36.
  const s36 = rects.s36, pad = Math.max(0, (40 - s36.w) / 2);
  if (pad >= 2) {
    await tap(Math.round(s36.x + s36.w + Math.max(1, Math.floor(pad) - 1)), Math.round(s36.y + s36.h / 2)); await sleep(60);
    const padded = await ev(CHIP);
    ok(padded?.count === 1 && Math.hypot(padded.position[0] - s36.position[0], padded.position[2] - s36.position[2]) < .001, `${name}: a touch ${Math.floor(pad) - 1} px off the mat's edge still picks 36 (the 40 px pick pad round a ${s36.w.toFixed(0)} px cell)`);
    await ev("document.querySelector('.roul-clear').click()");
  } else console.log(`  skip ${name}: cells already ${s36.w.toFixed(0)} px wide, no pick pad needed`);
  // The spin: the chip on 18, the camera eases out to the whole table, the landing comes through the real bridge.
  const betPose = await pose();
  const s18 = rects.s18;
  await tap(Math.round(s18.x + s18.w / 2), Math.round(s18.y + s18.h / 2)); await sleep(60);
  await ev("document.querySelector('.roul-spin').click()");
  ok(await until('window.__backroom.scene.transitioning', 1500), `${name}: Spin eases the camera out to the whole table`);
  ok(await until('!window.__backroom.scene.transitioning', 3000), `${name}: the table frame arrives`);
  const pockets = await ev(POCKETS), run = Object.values(await ev(RECTS) || {});
  ok(pockets.length === 37 && pockets.every((p) => p.x >= 0 && p.x <= width && p.y >= 0 && p.y <= height), `${name}: all 37 pockets are in view for the run`);
  ok(run.length === 42 && run.every((r) => r.x >= 0 && r.y >= 0 && r.x + r.w <= width && r.y + r.h <= height), `${name}: the mat and its chip stay in view while the ball runs`);
  await sleep(500); await shot(`phone-3d-${name}-run.png`);
  ok(await until(`(document.querySelector('.roul-history') || {}).childElementCount >= 1`, 14000), `${name}: the ball lands`);
  await sleep(400); await shot(`phone-3d-${name}-landing.png`);
  const room = await ev(`({ open: window.__posted.filter((m) => m.type === 'station-open').map((m) => m.station), fx: window.__posted.filter((m) => m.type === 'fx').map((m) => ({ id: m.fxId, station: m.station, args: m.args, symbols: m.symbols })),
    tunnel: window.__posted.filter((m) => m.type === 'fx-tunnel').length, media: window.__posted.filter((m) => m.type === 'media-request').map((m) => ({ station: m.station, count: m.count })),
    chip: document.querySelector('#br-sp-value').textContent, sp: window.__srv.user.sp, status: document.querySelector('.roul-status').textContent })`);
  report[name].room = room;
  ok(room.open.includes('roulette') && room.media.some((m) => m.station === 'roulette' && m.count === 4), `${name}: station-open posted; a 4-GIF deal asked for the roulette`);
  ok(room.fx.some((f) => f.id === 'fx.gif_from' && f.station === 'roulette' && f.args.from && /^g\d$/.test(f.symbols[0])) && room.tunnel > 3, `${name}: the straight on 18 fired its landing through the real bridge (${room.fx.map((f) => f.id).join(', ')})`);
  ok(room.chip === String(room.sp) && /18 Rose, Sink row/.test(room.status), `${name}: the room chip lands at ${room.chip} with the text "${room.status.split('\n')[0]}"`);
  ok(await until('window.__backroom.scene.transitioning', 9000), `${name}: the camera returns to the mat once bets reopen`);
  await until('!window.__backroom.scene.transitioning', 3000); await sleep(100);
  ok(samePose(await pose(), betPose), `${name}: the bet frame is the pose it left`);
  await shot(`phone-3d-${name}-after.png`);
  const perf = await ev('window.__backroom.scene.debug()');
  await writeFile(join(OUT, `phone-perf-${name}.json`), JSON.stringify({ viewport: [width, height], ...perf }, null, 2));
  await ev(`document.querySelector('#br-back').click()`);
  await sleep(700);
  ok(await ev(`window.__posted.some((m) => m.type === 'station-close' && m.station === 'roulette') && !document.querySelector('.roul-station')`), `${name}: Back closes the roulette and posts station-close`);
  ok(await ev("window.__backroom.scene.scene.getObjectByName('station_roulette').getObjectByName('bet_number_assembly').visible === true && !window.__backroom.scene.scene.getObjectByName('roulette_runtime_mat')"), `${name}: leaving restores the authored glyphs and drops the runtime mat`);
}

await writeFile(join(OUT, 'room-3d-check.json'), JSON.stringify(report, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall room 3D roulette checks passed');
await done(fails ? 1 : 0);
