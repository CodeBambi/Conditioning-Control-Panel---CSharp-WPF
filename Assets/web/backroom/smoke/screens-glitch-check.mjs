/* Silent screen handoff smoke: boot the real room, render its screen shader at
 * deterministic transition times, and verify still mode. Local fake host only;
 * this harness stops only its own headless Chrome process.
 * node backroom/smoke/screens-glitch-check.mjs [evidenceDir]
 */

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, readdirSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
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
const PORT = Number(process.env.BR_ROOM_TEST_PORT || 8898), DEBUG_PORT = Number(process.env.BR_ROOM_DEBUG_PORT || 9398);
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
  await boot(1280,800);
  ok(await ev(`!!window.__backroom?.scene`),'room boots');
  await ev(`window.__backroom.scene.pose([0,2.2,2],Math.PI/2,.18)`);
  // Exercise the actual ShaderMaterial in isolation for deterministic captures.
  await ev(`(async()=>{
    const T=await import('/vendor/three/three.module.min.js');
    const {createScreens}=await import('/backroom/room/screens.js');
    const canvas=document.createElement('canvas');canvas.id='screen-proof';canvas.style='position:fixed;inset:0;width:100%;height:100%;z-index:999';document.body.append(canvas);
    const renderer=new T.WebGLRenderer({canvas});renderer.setSize(960,540);
    const scene=new T.Scene(),camera=new T.PerspectiveCamera(45,960/540,.1,10);camera.position.z=3;
    const mesh=new T.Mesh(new T.PlaneGeometry(3.4,1.96),new T.MeshBasicMaterial());scene.add(mesh);
    const screens=await createScreens({meshes:[mesh],ads:[{url:'/backroom/room/assets/ads/dtrh.webp',caption:'ONE'},{url:'/backroom/room/assets/ads/arcademy.webp',caption:'TWO'}],media:async()=>({gifs:[]})});
    window.__screenProof={renderer,scene,camera,mesh,screens,draw(t,still=false){screens.update(t,camera,still);renderer.render(scene,camera);return {mix:mesh.material.uniforms.mixAmount.value,glitch:mesh.material.uniforms.glitch.value};}};
  })()`);
  ok(await ev(`__screenProof.mesh.material.uniforms.glitch!==undefined`),'real screen shader has static transition');
  await ev(`__screenProof.draw(8.4)`);await shot('screen-before.png');
  const mid=await ev(`__screenProof.draw(8.76)`);await shot('screen-static.png');
  ok(mid.glitch>.99,'static masks the picture handoff');
  await ev(`__screenProof.draw(9)`);await shot('screen-after.png');
  const still=await ev(`__screenProof.draw(8.76,true)`);
  ok(still.glitch===0,'still mode removes the glitch');
  await ev(`__screenProof.screens.dispose();__screenProof.mesh.geometry.dispose();__screenProof.renderer.dispose();document.querySelector('#screen-proof').remove()`);
  ok(errs.length===0,'no browser errors: '+errs.join('; '));
  console.log('Screen failures: '+fails);
  await done(fails?1:0);
}catch(e){console.error(e);await done(1);}
