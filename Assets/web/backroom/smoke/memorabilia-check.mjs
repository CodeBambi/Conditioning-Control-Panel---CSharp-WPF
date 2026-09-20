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

/* ---------------------------------------------------------------- 2. page */
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.BR_ROOM_TEST_PORT || 8897), DEBUG_PORT = Number(process.env.BR_ROOM_DEBUG_PORT || 9397);
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

/* The fake host. `?reduced=1` and `?calm=1` shape init; `?pictures=1` deals an animated GIF, a still WebP and a fallback.
 * The floor bell (10.16.B) is answered from smoke/mock-bell.js's fixture, resolved against the page clock. The check
 * drives it through `window.__bell` (reads, opts, optIn, mustHit, refuse) and `?bell=off` empties the list. */
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
          body: m.station === 'bell' ? bellBody(m.op, m.body) : { ok: true, sp: 57 } });
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
try {
  await boot(1280, 800);
  ok(await ev(`!!window.__backroom?.scene`), 'room boots');
  ok(await ev(`window.__backroom.scene.scene.getObjectByName('room_memorabilia').children.length===16`), 'eight photos, five posters, two notices and plaque present');
  await ev(`window.__backroom.scene.pose([-1.75,1.94,5.2],Math.PI,0)`);
  await sleep(600); await shot('memorabilia-entrance.png');
  ok(await ev(`import('./room/memorabilia.js').then(m=>m.PHOTO_GROUPS.map(g=>g.photos.length).join(',')==='1,2,1,2,2')`),'photo groups are 1,2,1,2,2');
  const point=await ev(`(()=>{const s=window.__backroom.scene,o=s.scene.getObjectByName('memorabilia_photo-0-0');s.scene.updateMatrixWorld(true);const p=o.getWorldPosition(s.camera.position.clone()).project(s.camera);return {x:(p.x+1)*innerWidth/2,y:(1-p.y)*innerHeight/2};})()`);
  await cdp('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...point});
  await cdp('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...point});
  await sleep(350);
  ok(await ev(`window.__backroom.scene.documents.opened`),'actual paper mesh tap opens viewer');
  ok(await ev(`document.querySelector('.memorabilia-paper img').naturalWidth>0`),'archive image loads');
  await shot('memorabilia-photo-desktop.png');
  await ev(`document.querySelector('.memorabilia-paper').click()`);
  ok(await ev(`window.__backroom.scene.documents.opened`),'document click stays open');
  const before=await dbg();
  await hold('KeyW',180);
  ok(before.held && !before.running,'room loop pauses while reading');
  ok(JSON.stringify((await dbg()).position)===JSON.stringify(before.position),'movement stays paused');
  await ev(`window.__backroom.back('test')`);
  ok(!(await ev(`window.__backroom.scene.documents.opened`)),'room Back closes viewer');
  ok((await posted('exit')).length===0,'Back does not exit room');
  await ev(`window.__backroom.scene.documents.open({kind:'note',title:'LOST & FOUND',caption:'One train of thought.'})`);
  await key('Escape'); await key('Escape','keyUp');
  ok(!(await ev(`window.__backroom.scene.documents.opened`)),'Escape closes only viewer');
  ok((await posted('exit')).length===0,'Escape does not exit room');
  for(const [w,h] of [[390,844],[844,390]]){
    await cdp('Emulation.setDeviceMetricsOverride',{width:w,height:h,deviceScaleFactor:1,mobile:false});
    await ev(`window.__backroom.scene.documents.open({kind:'poster',title:'Eyes front',src:'room/assets/memorabilia/eyes-front.jpg'})`);
    await sleep(350);
    ok(await ev(`(()=>{const r=document.querySelector('.memorabilia-paper').getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1})()`),'paper fits '+w+'x'+h);
    await shot('memorabilia-poster-'+w+'.png');
    await ev(`document.querySelector('.memorabilia-viewer').click()`);
    ok(!(await ev(`window.__backroom.scene.documents.opened`)),'outside click closes '+w);
  }
  for(const id of ['eyes','work','late','attend','listen','photo-0-0','photo-1-0','photo-1-1','photo-2-0','photo-3-0','photo-3-1','photo-4-0','photo-4-1','lost','meeting','plaque']) {
    await cdp('Emulation.setDeviceMetricsOverride',{width:1280,height:800,deviceScaleFactor:1,mobile:false});
    await ev(`(()=>{const s=window.__backroom.scene,o=s.scene.getObjectByName('memorabilia_${id}'),a=o.rotation.y,p=o.position;s.pose([p.x+Math.sin(a)*2.5,p.y,p.z+Math.cos(a)*2.5],a,0);})()`);
    await sleep(250); await shot('memorabilia-wall-'+id+'.png');
    await cdp('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x:640,y:400});
    await cdp('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x:640,y:400});
    ok(await ev(`window.__backroom.scene.documents.opened`),'wall item opens: '+id);
    await ev(`window.__backroom.scene.documents.close()`);
  }
  ok(errs.length===0,'no browser errors: '+errs.join('; '));
  console.log('Memorabilia failures: '+fails);
  await done(fails?1:0);
}catch(e){console.error(e);await done(1);}