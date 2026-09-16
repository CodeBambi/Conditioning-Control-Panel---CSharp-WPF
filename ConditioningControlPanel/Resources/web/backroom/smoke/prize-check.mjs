// Prize purchase, ownership restore, display placement, and reduced-motion browser check.

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, readdirSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { START, normaliseStations, blocked, reachable, nearestStation, facing } from '../room/walk.js';
import { bellLines, ROTATE_MS } from '../room/bell.js';
import { BELL_FIXTURE } from './mock-bell.js';
let fails = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const BACKROOM = resolve(HERE, '..');
const WEB = resolve(BACKROOM, '..');
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const stations=normaliseStations(JSON.parse(readFileSync(join(BACKROOM,'stations.json'),'utf8')));
for(const row of stations)ok(reachable(START,row.approach,stations),'approach stays reachable: '+row.key);
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.BR_ROOM_TEST_PORT || 8891), DEBUG_PORT = Number(process.env.BR_ROOM_DEBUG_PORT || 9391);
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  if (path === '/backroom/smoke/seated-station.js') {
    res.writeHead(200, { 'content-type': 'text/javascript' });
    return res.end((await readFile(join(HERE, 'mock-station.js'), 'utf8')) + '\nexport const roomStage = true;');
  }
  const file = path === '/backroom/stations/slot/station.js' ? join(HERE, 'mock-station.js') : join(WEB, path);
  try {
    const body = await readFile(file);
    res.writeHead(200, { 'content-type': MIME[extname(file).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch (e) { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const FAKE_HOST = `(() => {
  const q = new URLSearchParams(location.search);
  window.__bell = { reads: 0, opts: [], optIn: false, mustHit: q.get('musthit') === '1', open: true, refuse: null,
    rows: q.get('bell') === 'off' ? [] : ${JSON.stringify(BELL_FIXTURE)} };
  const bellBody = (op, body) => {
    const b = window.__bell;
    if (op === 'state') {
      b.reads++;
      return { ok: true, open: b.open, optIn: b.optIn, visit: { day: Math.floor(Date.now() / 86400000), comp: 0 },
        jackpot: { mustHit: b.mustHit }, entries: b.rows.map((e) => Object.assign({}, e, { t: Date.now() + e.t })) };
    }
    if (op === 'opt') {
      b.opts.push(body);
      if (typeof (body && body.on) !== 'boolean') return { ok: false, reason: 'bad_input' };
      b.optIn = body.on;
      return { ok: true, optIn: b.optIn };
    }
    return { ok: false, reason: 'bad_op' };
  };
  window.__owned = [];
  window.__prizeReply = m => {
    const ids=['jackpot_remix','rt_demo','high_roller','flashes_v2','bubbles_v2','rt_bundle_1','rt_bundle_2','rt_bundle_3'];
    if(m.op==='buy'&&!window.__owned.includes(m.body.prizeId))window.__owned.push(m.body.prizeId);
    return {ok:true,sp:57,prizes:{grants:window.__owned.some(id=>['rt_demo','rt_bundle_2','rt_bundle_3'].includes(id))?['rt.original.00']:[]},catalog:ids.map((id,order)=>({id,order,priceSp:1,sale:'on',owned:window.__owned.includes(id)?{at:1}:null}))};
  };
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
      if (m.type === 'station-request') {
        if (m.station === 'bell' && window.__bell.refuse) { window.__bell.reads++; emit({ type: 'station-result', reqId: m.reqId, ok: false, status: 0, reason: window.__bell.refuse }); }
        else emit({ type: 'station-result', reqId: m.reqId, ok: true, status: 200,
          body: m.station === 'bell' ? bellBody(m.op, m.body) : m.station === 'counter' ? window.__prizeReply(m) : {ok:true,sp:57} });
      }
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
  try { chrome.kill(); } catch (e) {  }
  server.close();
  await sleep(500);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) {  }
  process.exit(code);
}
let target = null;
for (let i = 0; i < 60 && !target; i++) {
  await sleep(250);
  try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) {  }
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
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `
  window.__glContexts = new Set();
  const get = HTMLCanvasElement.prototype.getContext;
  HTMLCanvasElement.prototype.getContext = function(type, ...args) {
    const ctx = get.call(this, type, ...args);
    if (ctx && /^(webgl2?|experimental-webgl)$/.test(type)) window.__glContexts.add(ctx);
    return ctx;
  };
` + FAKE_HOST });
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
await boot(1280,720);
ok(await ev(`document.documentElement.classList.contains('br-ready')`),'room boots');
ok(await ev(`!window.__backroom.scene.scene.getObjectByName('unlocked_arcade').visible`),'cabinet hidden before ownership');
await ev(`window.__backroom.scene.pose([1.6,1.8,-2.8],-.52,-.06)`); await sleep(500); await shot('prizes-before.png');
await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='counter'))`);
await sleep(1000);
await ev(`document.querySelector('[data-id="flashes_v2"] .counter-buy').click();document.querySelector('[data-id="flashes_v2"] .counter-yes').click()`);
await sleep(250); await shot('purchase-motion.png'); await sleep(800);
ok(await ev(`document.querySelector('[data-id="flashes_v2"]').dataset.face==='owned'`),'purchase is owned');
await shot('purchase-sold.png');
await ev(`document.querySelector('[data-id="rt_bundle_2"] .counter-buy').click();document.querySelector('[data-id="rt_bundle_2"] .counter-yes').click()`);
await sleep(80);
ok(await ev(`document.querySelector('[data-id="rt_demo"]').hidden`),'bundle demo grant removes demo listing');
await ev(`window.__backroom.back()`); await sleep(100);
await ev(`window.__backroom.scene.pose([1.6,1.8,-2.8],-.52,-.06)`); await sleep(300); await shot('arcade-reveal.png'); await sleep(1700);
await shot('prizes-owned.png');
ok(await ev(`window.__backroom.scene.scene.getObjectByName('unlocked_arcade').visible`),'arcade revealed');
ok(await ev(`window.__backroom.scene.scene.getObjectByName('owned_rt_bundle_2').visible&&!window.__backroom.scene.scene.getObjectByName('owned_rt_bundle_1').visible`),'only owned expansion shown');
ok(await ev(`!window.__backroom.scene.scene.getObjectByName('shelf_rt_demo').visible`),'demo mockup removed');
const displayBounds=await ev(`(async()=>{const T=await import('/vendor/three/three.module.min.js');return ['unlocked_arcade','owned_expansions'].map(name=>{const box=new T.Box3().setFromObject(window.__backroom.scene.scene.getObjectByName(name));return {name,min:box.min.toArray(),max:box.max.toArray()};});})()`);
console.log('display bounds',JSON.stringify(displayBounds));
ok(displayBounds.every(b=>b.max[0]<4.73&&b.min[2]>-6.3),'display clears card table and rear sculpture');
await ev(`window.__backroom.scene.pose([3.65,1.25,-2.6],0,-.04)`);await sleep(400);await shot('cabinet-and-expansions.png');
await cdp('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
await ev(`window.__backroom.scene.pose([3.65,1.7,-2.3],0,-.07)`);await sleep(700);await shot('prizes-phone.png');
await ev(`window.__hostEmit({type:'settings',motion:'off',intensity:'calm',reduced:true})`);await sleep(100);
ok(await ev(`window.__backroom.scene.debug().still`),'motion settings settle room');
ok(errs.length===0,'no browser errors: '+errs.join(' | '));
await writeFile(join(OUT,'prize-check.json'),JSON.stringify({fails,errors:errs},null,2));
await done(fails?1:0);
