// Shared room/play cabinet identity, input, animation and responsive browser regression.
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
const WEB = resolve(HERE, '../..');   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.ROULETTE_PORT || 8953), DEBUG_PORT = PORT + 500;
const PAGE_URL = process.env.ROULETTE_URL || `http://127.0.0.1:${PORT}/backroom/index.html`;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

// One fake host: the slot's own mock-server.js, the bell's mock, fallback art for the deal.
const BELL_MOCK_URL = 'data:text/javascript;base64,' + (await readFile(join(HERE, 'mock-bell.js'))).toString('base64');
const FAKE_HOST = `(() => {
  const original = HTMLCanvasElement.prototype.getContext, contexts = new Set();
  window.__webgl = contexts;
  HTMLCanvasElement.prototype.getContext = function(type, ...args) { const result = original.call(this, type, ...args); if (/^webgl/.test(type) && result) contexts.add(result); return result; };
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const made = {};
  window.__mocks = made;   // the flow check below scripts a row on the slot's own mock (a promise per station id)
  const mock = (id) => made[id] || (made[id] = import(id === 'bell' ? '${BELL_MOCK_URL}' : '/backroom/stations/' + id + '/mock-server.js')
    .then((m) => (id === 'bell' ? m.createBellMock({}) : m.createMockServer({ sp: 57 }))));
  window.__posted = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(JSON.parse(JSON.stringify(m)));
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en',
        gates: { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true }, lex: { br_back: 'Back', br_balance: 'SP' },
        stations: ['slot', 'wheel', 'cards', 'roulette', 'counter', 'bell'], open: true });
      if (m.type === 'station-request') mock(m.station).then((s) => s.handle(m.op, m.body || {}, m.idem))
        .then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-shared-slot-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=390,844', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
  '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
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
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable');
if(process.env.ROULETTE_URL){await cdp('Network.enable');await cdp('Network.setBlockedURLs',{urls:['*__phone*.js']});}
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const metrics = (width, height) => cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: width <= 800 || height <= 500, screenOrientation: { type: width > height ? 'landscapePrimary' : 'portraitPrimary', angle: width > height ? 90 : 0 } });
const pt = (id, x, y) => ({ id, x, y, radiusX: 4, radiusY: 4, force: 1 });
const send = async (type, points) => { const r = await cdp('Input.dispatchTouchEvent', { type, touchPoints: points }); if (r.error) throw new Error(JSON.stringify(r.error)); };
const tap = async (x, y) => { await send('touchStart', [pt(9, x, y)]); await send('touchEnd', []); };

const RECTS = `(async () => { const T = await import('three'); const s = window.__backroom.scene, mat = s.scene.getObjectByName('roulette_runtime_mat'); if (!mat) return null;
  const r = s.renderer.domElement.getBoundingClientRect(), out = {}, v = new T.Vector3();
  mat.traverse((o) => { if (!o.userData.spot) return; const g = o.geometry.parameters; let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const x of [-g.width / 2, g.width / 2]) for (const y of [-g.height / 2, g.height / 2]) { o.localToWorld(v.set(x, y, 0)).project(s.camera); const px = r.left + (v.x + 1) * r.width / 2, py = r.top + (1 - v.y) * r.height / 2; x0 = Math.min(x0, px); y0 = Math.min(y0, py); x1 = Math.max(x1, px); y1 = Math.max(y1, py); }
    out[o.userData.spot] = { x: x0, y: y0, w: x1 - x0, h: y1 - y0, cover: document.elementFromPoint((x0 + x1) / 2, (y0 + y1) / 2)?.tagName || null }; });
  return out; })()`;
const POCKETS = `(async () => { const T = await import('three'); const s = window.__backroom.scene, f = s.scene.getObjectByName('station_roulette'), r = s.renderer.domElement.getBoundingClientRect(), out = [];
  for (let n = 0; n < 37; n++) { const v = new T.Box3().setFromObject(f.getObjectByName('pocket_' + n)).getCenter(new T.Vector3()).project(s.camera); out.push({ n, x: r.left + (v.x + 1) * r.width / 2, y: r.top + (1 - v.y) * r.height / 2 }); }
  return out; })()`;

const report={cases:[],errors:errs};
for(const [width,height] of [[1280,720],[390,844],[844,390]]){
 await metrics(width,height);
 await cdp('Page.navigate',{url:PAGE_URL});
 ok(await until("document.documentElement.classList.contains('br-ready')",45000),'room boot '+width);
 const contexts=await ev('window.__webgl.size');
 await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='roulette'));true");
 ok(await until("!!document.querySelector('.roul-station[data-phase=bet]')",15000),'roulette ready');
 await until('!window.__backroom.scene.transitioning',10000);await sleep(400);
 const rects=await ev(RECTS),list=Object.values(rects||{}),pockets=await ev(POCKETS);
 ok(list.length===42&&list.every(r=>r.x>=0&&r.y>=0&&r.x+r.w<=width&&r.y+r.h<=height),'42 bet targets inside viewport');
 ok(list.every(r=>r.cover==='CANVAS'),'42 bet targets unobscured by UI');
 ok(pockets.length===37&&pockets.every(p=>p.x>=0&&p.y>=0&&p.x<=width&&p.y<=height),'all 37 pockets visible');
 const controls=await ev("[...document.querySelectorAll('.roul-controls button,.roul-odds summary,.br-back')].map(b=>{const r=b.getBoundingClientRect();return {text:b.textContent,x:r.x,y:r.y,w:r.width,h:r.height}})");
 ok(controls.every(r=>r.x>=0&&r.y>=0&&r.x+r.w<=width&&r.y+r.h<=height),'controls inside viewport');
 ok(await ev('window.__webgl.size')===contexts,'no extra graphics context');
 await shot(width+'-roulette-play.png');
 const cell=rects.s36,x=cell.x+cell.w/2,y=cell.y+cell.h/2;
 if(width>800&&height>500){await cdp('Input.dispatchMouseEvent',{type:'mousePressed',x,y,button:'left',buttons:1,clickCount:1});await cdp('Input.dispatchMouseEvent',{type:'mouseReleased',x,y,button:'left',clickCount:1});}else await tap(x,y);
 await sleep(200);
 ok(await ev("window.__backroom.scene.scene.getObjectByName('roulette_live_chips')?.count===1"),'real tap places chip on 36');
 await ev("document.querySelector('.roul-spin').click();true");await sleep(700);await shot(width+'-roulette-spin.png');
 ok(await until("(document.querySelector('.roul-history')||{}).childElementCount>=1",25000),'spin lands');
 report.cases.push({width,height,rects,pockets,controls,contexts});
 if(width===390){for(const [rw,rh] of [[844,390],[390,844]]){await metrics(rw,rh);await sleep(1800);const rotated=Object.values(await ev(RECTS)||{});ok(rotated.length===42&&rotated.every(r=>r.x>=0&&r.y>=0&&r.x+r.w<=rw&&r.y+r.h<=rh&&r.cover==='CANVAS'),'rotation keeps42 targets visible and clear '+rw);ok(await ev('window.__webgl.size')===contexts,'rotation adds no context');await shot(rw+'-roulette-rotated.png');}}

 await ev('window.__backroom.back();true');await until('!window.__backroom.scene.transitioning',10000);
 ok(!await ev("!!document.querySelector('.roul-station')"),'Back exits');
}
ok(errs.length===0,'zero page errors');report.fails=fails;await writeFile(join(OUT,'roulette-closeup-check.json'),JSON.stringify(report,null,2));
console.log(JSON.stringify({fails,errors:errs}));await done(fails?1:0);
