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
const WEB = process.env.BACKROOM_TEST_ROOT || resolve(BACKROOM, '..');
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = 8993, DEBUG_PORT = 9493;

const REGISTRY = JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8'));
const LIVE = REGISTRY.filter((s) => s.state === 'live');

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (process.env.BACKROOM_TEST_ROOT && path === '/backroom/smoke/mock-bell.js') { res.writeHead(200, {'content-type':'text/javascript'}); return res.end(await readFile(join(BACKROOM,'smoke/mock-bell.js'))); }
  if (process.env.BACKROOM_TEST_ROOT && path === '/backroom/shared/hypno/tests/handedness.js') { res.writeHead(200, {'content-type':'text/javascript'}); return res.end(await readFile(join(BACKROOM,'shared/hypno/tests/handedness.js'))); }
  if (process.env.BACKROOM_TEST_ROOT && /^\/__phone-(host|fx)\.js$/.test(path)) { res.writeHead(200, {'content-type':'text/javascript'}); return res.end(''); }
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

await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});
ok(await until("document.documentElement.classList.contains('br-ready')",45000),'room boots');
ok((await posted('media-request')).some(m=>m.station==='room'&&m.count===8),'room requests eight pictures');
const walls = await ev(`(async()=>{
  const T=await import('three'),{createScreens}=await import('/backroom/room/screens.js');
  const meshes=Array.from({length:8},()=>new T.Mesh(new T.PlaneGeometry(1,1)));
  let batch=0,now=0,freed=0;
  const screens=await createScreens({meshes,ads:[],media:async()=>({gifs:Array.from({length:8},(_,i)=>({url:'/backroom/stations/slot/fallback/gif'+i%4+'.webp?batch='+batch+'&i='+i,src:'pool'}))})});
  await screens.deal(()=>now);screens.update(0,null,false);
  const unique=new Set(meshes.map(m=>m.material.uniforms.a.value)).size;
  for(const m of meshes)m.material.uniforms.a.value.addEventListener('dispose',()=>freed++);
  batch++;now=80;screens.update(now,null,true);
  const held=freed===0;
  screens.update(now,null,false);
  for(let i=0;i<100&&freed<8;i++) await new Promise(r=>setTimeout(r,30));
  screens.update(now,null,false);
  const second=new Set(meshes.map(m=>m.material.uniforms.a.value)).size;
  const squarePanels=meshes[0].material.uniforms.panelsA.value;
  const image=meshes[0].material.uniforms.a.value.image;image.height=image.width*2;
  screens.update(now,null,false);const portraitPanels=meshes[0].material.uniforms.panelsA.value;
  screens.dispose();meshes.forEach(m=>m.geometry.dispose());return {unique,second,freed,held,squarePanels,portraitPanels};
})()`);
ok(walls?.unique===8&&walls.second===8&&walls.freed===8&&walls.held,'automatic batch refresh frees old textures; Motion Off holds the batch '+JSON.stringify(walls));
ok(walls?.squarePanels===2&&walls.portraitPanels===3,'square GIFs use two panels and portraits three');
const fixture=await ev(`(()=>{const root=__backroom.scene.scene;const colors=new Set();root.traverse(n=>{if(n.name==='bulbs_wheel'&&n.instanceColor)for(let i=0;i<n.count;i++)colors.add(Array.from(n.instanceColor.array.slice(i*3,i*3+3)).map(x=>x.toFixed(2)).join(','))});return {screenScale:root.getObjectByName('media_screen_0').scale.x,colors:colors.size}})()`);
ok(fixture?.screenScale>1.2&&fixture.colors>5,'larger screens and distinct colorful wheel bulbs');
await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.key==='wheel'))`);
ok(await until("__backroom.loader.current?.id==='wheel'&&!__backroom.scene.transitioning",20000),'wheel arrives');
ok(await until("__backroom.loader.current.debug().hypno.deck?.ready===8",15000),'eight wheel sources finish loading');
const before=await ev('__backroom.loader.current.debug()');await sleep(1200);
const after=await ev('__backroom.loader.current.debug()');
ok(after?.feel.scene.face.key.length>=8&&new Set(after.feel.scene.face.key).size===8,'every slice receives art from the bounded eight-picture deck');
ok(after?.hypno.deck.frames>before?.hypno.deck.frames,'wheel GIFs advance');
ok(after?.hypno.deck.decodes-before?.hypno.deck.decodes<=90,'decode budget stays below one per rendered frame');
const winding=await ev(`(async()=>{const root=__backroom.scene.scene,hub=root.getObjectByName('hub_loom'),{ring,shift}=await import('/backroom/shared/hypno/tests/handedness.js');
 const src=hub.material.map.image,c=document.createElement('canvas');c.width=c.height=256;const g=c.getContext('2d');g.translate(256,0);g.scale(-1,1);g.drawImage(src,0,0);const a=g.getImageData(0,0,256,256).data;
 return {mirror:hub.material.map.repeat.x,rim:[45,64,83].map(r=>shift(ring(a,256,256,r),ring(a,256,256,r+3),239))};})()`);
ok(winding?.mirror===-1&&winding.rim.every(x=>x<0),'hub arms wind the requested opposite way '+JSON.stringify(winding));
await shot('wheel-desktop.png');
await cdp('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await sleep(900);await shot('wheel-phone.png');
await ev("__hostEmit({type:'settings',reduced:true,motion:'off',intensity:'calm'})");await sleep(1000);
const fixed=await ev('__backroom.loader.current.debug().hypno.deck.frames');await sleep(700);
ok(fixed===await ev('__backroom.loader.current.debug().hypno.deck.frames'),'Motion Off holds all wheel pictures');
await ev("document.querySelector('#br-back').click()");
ok(await until("!document.querySelector('.wheel-station')",3000),'Back releases the wheel');
await ev("__hostEmit({type:'settings',reduced:false,motion:'full',intensity:'normal'})");
await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.key==='cards'))`);
ok(await until("__backroom.loader.current?.id==='cards'&&__backroom.loader.current.debug().deck?.ready===13",20000),'blackjack loads all thirteen rank pictures');
await shot('cards-phone.png');
await ev("document.querySelector('#br-back').click()");
ok(errs.length===0,'no browser errors '+errs.join(' | '));
await writeFile(join(OUT,'report.json'),JSON.stringify({walls,before,after,errs,fails},null,2));
await done(fails?1:0);
