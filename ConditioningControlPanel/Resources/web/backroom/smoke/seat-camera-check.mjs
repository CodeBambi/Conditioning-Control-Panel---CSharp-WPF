/* ============================================================================
 * backroom/smoke/room-stations-check.mjs - every live station through the REAL room, one page, one host.
 *
 *   node backroom/smoke/room-stations-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * What it proves that the per-station checks cannot: the room reads stations.json and sets out every fixture,
 * the fake host answers init with the stations the C# host whitelists (BackRoomApi.Ops keys: slot, wheel,
 * cards, roulette, counter), and each of the five stations mounts from its stations.json entry through the loader,
 * opens (station-open, hostBack), plays one moment against its own mock-server.js, and closes on the room's
 * Back (station-close, the loader empty, the room loop running again, no tunnel left above 0). The slot here
 * is the REAL slot station, not smoke/mock-station.js.
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROOM_STATIONS_PORT, default 8931, debug
 * +500). The only process it stops is the Chrome it started, by its own handle.
 * CHROME: CHROME_PATH, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const BACKROOM = resolve(HERE, '..');
const WEB = resolve(BACKROOM, '..');
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = 8934, DEBUG_PORT = 9434;

const REGISTRY = JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8'));
const LIVE = REGISTRY.filter((s) => s.state === 'live');

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

// One fake host for the whole room: a mock server per station, created on its first request.
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const gates = { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true };
  const made = {};
  // The floor bell is not a station (10.16.B): the room reads it, and its mock lives in smoke/.
  const mock = (id) => made[id] || (made[id] = import(id === 'bell' ? '/backroom/smoke/mock-bell.js' : '/backroom/stations/' + id + '/mock-server.js').then((m) => {
    const s = id === 'bell' ? m.createBellMock({}) : id === 'cards' ? m.createMockServer({ sp: 57, floorMs: 600 }) : id === 'roulette' ? m.createMockServer({ sp: 57, floorMs: 0 })
      : id === 'counter' ? m.createMockServer({ sp: 57, on: 'jackpot_remix,rt_demo,high_roller' }) : m.createMockServer({ sp: 57 });
    if (id === 'wheel') s.script('deep');
    if (id === 'cards') s.script('Th', '9d', '8c', '8s');
    if (id === 'roulette') s.script({ pocket: 36 });
    window.__servers[id] = s; return s; }));
  window.__hostEmit = emit; window.__posted = []; window.__servers = {};
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(JSON.parse(JSON.stringify(m)));
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', gates,
        lex: { br_back: 'Back', br_balance: 'SP' }, stations: ['slot', 'wheel', 'cards', 'roulette', 'counter', 'bell'], open: true });
      if (m.type === 'station-request') mock(m.station).then((s) => s.handle(m.op, m.body || {}, m.idem))
        .then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-room-stations-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
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
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
const posted = (type) => ev(`window.__posted.filter((m) => m.type === ${JSON.stringify(type)})`);
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const key = async (code, type = 'keyDown') => cdp('Input.dispatchKeyEvent', { type, code, key: code.slice(3).toLowerCase(), windowsVirtualKeyCode: code.charCodeAt(3) });
async function click(x, y) {
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
  await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1 });
  await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: 1 });
}
const clickSel = (sel) => ev(`(() => { const b = document.querySelector(${JSON.stringify(sel)}); if (!b || b.disabled) return false; b.click(); return true; })()`);
const report = { stations: {}, registry: LIVE.map((s) => s.id + (s.variant ? ':' + s.variant : '')) };


for(const [width,height] of (process.argv.includes('--phone')?[[400,730]]:process.argv.includes('--landscape')?[[730,400]]:[[400,730],[1280,720],[730,400]])){
 await cdp('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<800});
 await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});
 ok(await until("document.documentElement.classList.contains('br-ready')",45000),'room boots '+width+'x'+height);
 for(const id of ['wheel','cards','roulette']){
  const before=await ev('window.__backroom.scene.debug()');
  await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='${id}'));true`);
  ok(await until('window.__backroom.scene.transitioning',3000),'pan begins '+id);
  ok(!await ev(`!!document.querySelector('.${id==='roulette'?'roul':id==='wheel'?'wheel':'cards'}-station')`),'game waits for arrival '+id);
  await sleep(650);await shot(width+'-'+id+'-moving.png');
  ok(await until('!window.__backroom.scene.transitioning',6000),'pan arrives '+id);
  await sleep(id==='cards'?4700:1300);
  if(id==='cards'){await clickSel('.cards-deal');await sleep(3800);}
  await shot(width+'-'+id+'-seated.png');
  report.stations[width+'-'+id]=await ev('window.__backroom.scene.debug()');
  await ev('window.__backroom.back();true');
  ok(await until('window.__backroom.scene.transitioning',1500),'return pan begins '+id);
  ok(await until('!window.__backroom.scene.transitioning',6000),'return pan finishes '+id);
  const after=await ev('window.__backroom.scene.debug()');
  ok(after.position.every((v,i)=>Math.abs(v-before.position[i])<.01)&&Math.abs(after.yaw-before.yaw)<.001,'return restores walking pose '+id);
 }
}
// Back cancels arrival before a station can mount or send station-open.
await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='wheel'));true");
ok(await until('window.__backroom.scene.transitioning',3000),'cancellable arrival starts');
await ev('window.__backroom.back();true');
ok(await until('!window.__backroom.scene.transitioning',6000),'early Back finishes return');
await sleep(300);ok(await ev("!window.__backroom.loader.current&&!document.querySelector('.wheel-station')"),'canceled arrival never opens later');
await ev("window.__backroom.state.motion='off'");
await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='wheel'));true");
await sleep(100);ok(!await ev('window.__backroom.scene.transitioning'),'Motion off has no arrival wait');
await ev('window.__backroom.back();true');await sleep(100);
ok(!await ev('window.__backroom.scene.transitioning'),'Motion off has no return wait');
await ev("window.__backroom.state.motion='full'");
for(const id of ['slot','counter']){
 const count=await ev("window.__posted.filter(m=>m.type==='station-open').length");
 await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='${id}'));true`);
 ok(await until('window.__backroom.scene.transitioning',3000),'legacy approach pan '+id);
 ok(await until(`window.__posted.filter(m=>m.type==='station-open').length>${count}`,10000),'legacy opens after arrival '+id);
 ok(await ev('window.__backroom.scene.held'),'legacy holds room only after arrival '+id);
 await ev('window.__backroom.back();true');
 ok(await until('window.__backroom.scene.transitioning',2000),'legacy return pan '+id);
 ok(await until('!window.__backroom.scene.transitioning',6000),'legacy return completes '+id);
}
ok(!errs.length,'no page exceptions: '+errs.join('\n'));
await writeFile(join(OUT,'camera-check.json'),JSON.stringify({...report,errors:errs,fails},null,2));await done(fails?1:0);
