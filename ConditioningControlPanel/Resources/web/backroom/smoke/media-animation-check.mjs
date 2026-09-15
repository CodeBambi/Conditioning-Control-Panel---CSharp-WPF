/* Real media pixels: native and compatibility decode, wall/projector shaders, live reel textures. */

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
const PORT = 8992, DEBUG_PORT = 9492;

const REGISTRY = JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8'));
const LIVE = REGISTRY.filter((s) => s.state === 'live');

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { if(path.endsWith('/cards/station.js'))await sleep(400); const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
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
const report = [];
for (const compatibility of [false, true]) {
 await cdp('Page.addScriptToEvaluateOnNewDocument', { source: compatibility ? 'window.ImageDecoder = undefined' : '' });
 await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});
 ok(await until("document.documentElement.classList.contains('br-ready')",45000),'room boots');
 await ev(`(async()=>{
   const T=await import('three'); window.T=T;
   const {decodedSource}=await import('/backroom/room/gif-decode.js'); window.sources=[];
   for(const url of [0,1,2,3].map(i=>'/backroom/stations/slot/fallback/gif'+i+'.webp').concat('/backroom/shared/hypno/spirals/screen.gif'))
     sources.push(await decodedSource(url));
   window.hash=c=>{const a=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let h=2166136261;for(const v of a)h=Math.imul(h^v,16777619);return h>>>0};
   window.sampleMaterial=mat=>{const r=__backroom.scene.renderer,rt=new T.WebGLRenderTarget(128,128),s=new T.Scene(),g=new T.PlaneGeometry(2,2),m=new T.Mesh(g,mat),c=new T.OrthographicCamera(-1,1,1,-1,0,2);c.position.z=1;s.add(m);const prev=r.getRenderTarget(),scissor=r.getScissorTest();r.setScissorTest(false);r.setRenderTarget(rt);r.render(s,c);const a=new Uint8Array(128*128*4);r.readRenderTargetPixels(rt,0,0,128,128,a);r.setRenderTarget(prev);r.setScissorTest(scissor);rt.dispose();g.dispose();let h=2166136261;for(const v of a)h=Math.imul(h^v,16777619);return h>>>0};
 })()`);
 ok(await ev('sources.every(s=>s?.animated)'), 'all four WebP loops and real GIF animate, compatibility='+compatibility);
 const sequence=[];
 for(let n=0;n<6;n++){sequence.push(await ev('sources.map(s=>{s.tick(performance.now(),false);return hash(s.canvas)})'));await sleep(160);}
 for(let i=0;i<5;i++)ok(new Set(sequence.map(row=>row?.[i])).size>1,'decoded picture '+i+' changes');
 await ev('sources.forEach(s=>s.tick(performance.now(),true))');await sleep(200);
 const fixed=await ev('sources.map(s=>hash(s.canvas))');await sleep(250);
 ok(JSON.stringify(fixed)===JSON.stringify(await ev('sources.map(s=>{s.tick(performance.now(),true);return hash(s.canvas)})')),'Motion Off holds decoded pixels');
 const gpu=[];
 for(let n=0;n<4;n++){gpu.push(await ev(`sources.map(s=>{s.tick(performance.now(),false);const t=new T.CanvasTexture(s.canvas),m=new T.MeshBasicMaterial({map:t});const h=sampleMaterial(m);t.dispose();m.dispose();return h})`));await sleep(170);}
 for(let i=0;i<5;i++)ok(new Set(gpu.map(row=>row?.[i])).size>1,'GPU picture '+i+' changes');
 await ev(`window.wall=(()=>{let m;__backroom.scene.scene.traverse(o=>{if(!m&&o.material?.uniforms?.ratioA)m=o});return m})()`);
 const walls=[];for(let n=0;n<5;n++){walls.push(await ev('sampleMaterial(wall.material)'));await sleep(220);}
 ok(new Set(walls).size>1,'live wall shader changes');
 await ev('__backroom.scene.setStill(true)');await sleep(1200);
 const a=await ev('sampleMaterial(wall.material)');await sleep(500);ok(a===await ev('sampleMaterial(wall.material)'),'live wall holds with Motion Off');
 await ev('__backroom.scene.setStill(false)');
 await ev("window.projector=__backroom.scene.scene.getObjectByName('screen_surface_ceiling_projection')");
 const projection=[];for(let n=0;n<5;n++){projection.push(await ev('sampleMaterial(projector.material)'));await sleep(220);}
 ok(new Set(projection).size>1,'projector shader advances animated media');
 await ev('__backroom.scene.setStill(true)');await sleep(800);
 const stillProjection=await ev('sampleMaterial(projector.material)');await sleep(300);
 ok(stillProjection===await ev('sampleMaterial(projector.material)'),'projector holds with Motion Off');
 await ev('__backroom.scene.setStill(false)');
 await ev(`(async()=>{const {createMedia}=await import('/backroom/stations/slot/media.js');window.slotMedia=createMedia(document.createElement('div'));await slotMedia.deal({gifs:[{key:'g0',url:'/backroom/stations/slot/fallback/gif0.webp'}]})})()`);
 const slot=[];for(let n=0;n<5;n++){slot.push(await ev('hash(slotMedia.gif(0))'));await sleep(160);}
 ok(new Set(slot).size>1,'slot drawable changes');
 await ev(`window.reelCanvases=new Set();window.originalContext=HTMLCanvasElement.prototype.getContext;HTMLCanvasElement.prototype.getContext=function(...args){if(this.height===256&&this.width>1024)reelCanvases.add(this);return originalContext.apply(this,args)}`);
 await ev("__backroom.visit(__backroom.stations.find(s=>s.key==='slot:rose'))");
 ok(await until("__backroom.loader.current?.id==='slot'&&!__backroom.scene.transitioning",15000),'live slot arrives');await sleep(700);
 const liveReels=[];for(let n=0;n<5;n++){liveReels.push(await ev('[...reelCanvases].map(hash)'));await sleep(220);}
 ok(liveReels[0]?.length===3&&liveReels[0].every((_,i)=>new Set(liveReels.map(r=>r[i])).size>1),'all three live reel textures animate');
 await ev("[...document.querySelectorAll('button')].find(b=>b.textContent==='Motion on').click()");await sleep(600);
 const heldReels=await ev('[...reelCanvases].map(hash)');await sleep(400);
 ok(JSON.stringify(heldReels)===JSON.stringify(await ev('[...reelCanvases].map(hash)')),'live reel textures hold with Motion Off');
 await ev('__backroom.back("back")');await sleep(2200);await ev('HTMLCanvasElement.prototype.getContext=originalContext');
 report.push({compatibility,sequence,gpu,walls,slot,projection,liveReels,decoder:await ev('typeof ImageDecoder'),errors:[...errs]});
 await shot('media-'+(compatibility?'compat':'native')+'.png');
 await ev('sources.forEach(s=>s.dispose());slotMedia.dispose()');
}
await writeFile(join(OUT,'animation.json'),JSON.stringify(report,null,2));
ok(!errs.length,'no browser errors');await done(fails?1:0);
