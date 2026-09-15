/* ============================================================================
 * roulette-check.mjs - the Velvet Vortex station in headless Chrome, driven over CDP, on dev.html (the kit's mock
 * host and mock-server.js) and inside the real room page.
 *
 *   node backroom/stations/roulette/tests/roulette-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROULETTE_PORT, default 8899, debug +500) and the
 * mocks answer every call. The only process this stops is the Chrome it started, by its own handle.
 * Evidence: a screenshot of every key moment in CONTRACT 10.13.F (idle table, lighthouse sweep, the mat, a
 * refused cover-all, the tunnel run, the fret rattle, the Spiral Wake turret, big / win / miss landings, Full,
 * a gated-off run, a Calm run, the room) and roulette-check.json. The dev page's "host preview" layer paints
 * what the mock host acked, so the fullscreen moments show in the shots; the real overlays are the app's.
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
await cdp('Emulation.setDeviceMetricsOverride', { width: 400, height: 800, deviceScaleFactor: 1, mobile: true });
async function shot(name) {
  const r = await cdp('Page.captureScreenshot', { format: 'png' });
  await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64'));
  console.log('  shot ' + name);
}
const dbg = () => ev('window.dev.station.debug()');
async function until(expr, ms = 15000, step = 50) { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
async function boot(query) {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/stations/roulette/dev.html${query}` });
  await until('!!(window.dev && window.dev.station)', 10000, 100);
  await ev('window.dev.open()');
  await until('window.dev.station.debug().phase === "bet"', 10000, 100);
}
async function click(x, y, button = 'left') {
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
  await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button, buttons: button === 'left' ? 1 : 2, clickCount: 1 });
  await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button, clickCount: 1 });
}
async function clickSpot(spot) {
  const r = (await dbg()).mat.rects[spot];
  await click(Math.round(r.x + r.w / 2), Math.round(r.y + r.h / 2));
}
const clickSel = (sel) => ev(`(() => { const b = document.querySelector(${JSON.stringify(sel)}); if (!b || b.disabled) return false; b.click(); return true; })()`);
const host = (expr) => ev(`(() => { const h = window.dev.host; return ${expr}; })()`);
const logOf = (what) => ev(`window.dev.station.debug().log.filter((x) => x.what === ${JSON.stringify(what)})`);
const report = {};

await cdp('Emulation.setUserAgentOverride',{userAgent:'Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 Chrome/120.0 Mobile Safari/537.36'});
await cdp('Page.addScriptToEvaluateOnNewDocument',{source: "Object.defineProperty(navigator,'deviceMemory',{get:()=>4});Object.defineProperty(navigator,'hardwareConcurrency',{get:()=>4});"});
const FAKE_HOST = `(() => {
  const listeners = [];
  const emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  let srv = null;
  const mock = () => srv || (srv = import('/backroom/stations/roulette/mock-server.js').then((m) => { const s = m.createMockServer({ sp: 57, floorMs: 0 }); s.script({ pocket: 36 }); window.__srv = s; return s; }));
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
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
await until(`document.documentElement.classList.contains('br-ready')`, 40000, 100);
await ev(`window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'roulette'))`);
ok(await until('!!document.querySelector(".roul-station[data-phase=bet]")', 10000, 100), 'the room mounts and opens the roulette through the loader');
const allPicks = await ev(`(() => {
  const buttons=[...document.querySelectorAll('.roul-mat-strip button')];
  return buttons.length===42 && buttons.every(button=>{button.click();const shown=document.querySelector('.roul-chips-label').textContent.includes('1 of 3');document.querySelector('.roul-clear').click();return shown;});
})()`);
ok(allPicks,'all 42 bet strip choices reach the current bet action');
const roomRect = await ev(`(() => { const c = document.querySelector('.roul-stage'); const r = c.getBoundingClientRect(); return { x: r.left, y: r.top }; })()`);
ok(await ev(`!!document.querySelector('.roul-station[data-host-back][data-host-sp]') && document.querySelector('.roul-back').hidden`), 'ctx.hostBack and ctx.spReadout: the station hides its own Back and SP chip');
await sleep(500);
await shot('18-room-roulette-open.png');
// place 36 through the canvas in the room page (the room page has no window.dev; find the cell by the mat layout)
const cellCentre = await ev(`(async () => {
  const button = document.querySelector('.roul-mat-strip [data-spot=s36]');
  if (button) { const r = button.getBoundingClientRect(); return { x:r.x+r.width/2, y:r.y+r.height/2 }; }
  const c = document.querySelector('.roul-stage'); const w = c.clientWidth, h = c.clientHeight;
  const mw = w * 0.47, mh = h * 0.46, cell = Math.max(14, Math.min(mw / 13, mh / 5.4)), ox = w * 0.5 + (mw - cell * 13) / 2, oy = h * 0.16 + (mh - cell * 5.4) / 2;
  return { x: ox + 12 * cell + cell / 2, y: oy + cell / 2 }; })()`);
await click(Math.round(roomRect.x + cellCentre.x), Math.round(roomRect.y + cellCentre.y));
await ev(`document.querySelector('.roul-spin').click()`);
await until(`(document.querySelector('.roul-history') || {}).childElementCount >= 1`, 12000, 50);
await sleep(400);
const room = await ev(`({ open: window.__posted.filter((m) => m.type === 'station-open').map((m) => m.station), fx: window.__posted.filter((m) => m.type === 'fx').map((m) => ({ id: m.fxId, station: m.station, args: m.args, symbols: m.symbols })),
  tunnel: window.__posted.filter((m) => m.type === 'fx-tunnel').length, media: window.__posted.filter((m) => m.type === 'media-request').map((m) => ({ station: m.station, count: m.count })),
  chip: document.querySelector('#br-sp-value').textContent, sp: window.__srv.user.sp, status: document.querySelector('.roul-status').textContent })`);
report.room = room;
ok(room.open.includes('roulette') && room.media.some((m) => m.station === 'roulette' && m.count === 4), 'station-open posted; a 4-GIF deal asked for the roulette');
ok(room.fx.some((f) => f.id === 'fx.gif_from' && f.station === 'roulette' && f.args.from && /^g\d$/.test(f.symbols[0])) && room.tunnel > 3, `the straight on 36 fired its landing through the real bridge (${room.fx.map((f) => f.id).join(', ')})`);
ok(room.chip === String(room.sp) && room.sp === 57 - 1 + 36 && /36 Rose, Deep row/.test(room.status), `the room chip lands at ${room.chip} with the text "${room.status.split('\n')[0]}"`);
await shot('19-room-roulette-landing.png');
const perf = await ev('window.__backroom.scene.debug()');
await writeFile(join(OUT,'phone-perf.json'),JSON.stringify({viewport:[400,800],...perf},null,2));
ok(await ev('document.querySelectorAll("canvas").length===1'),'one room canvas while seated');

await ev(`document.querySelector('#br-back').click()`);
await sleep(700);
ok(await ev(`window.__posted.some((m) => m.type === 'station-close' && m.station === 'roulette') && !document.querySelector('.roul-station')`), 'Back closes the roulette and posts station-close');

await writeFile(join(OUT, 'roulette-check.json'), JSON.stringify(report, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall roulette checks passed');

await done(fails?1:0);
