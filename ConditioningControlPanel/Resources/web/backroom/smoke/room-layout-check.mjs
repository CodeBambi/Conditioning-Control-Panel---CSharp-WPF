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
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp?source=' + i, w: 180, h: 180, src: 'pool' })) });
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
ok(await ev(`(()=>{const a=__backroom.scene.annex?.debug();return !!a&&a.doors.length===3&&a.clipped>0&&!!__backroom.scene.scene.getObjectByName('annex_door_annex_race')})()`),'the annex stands through the west wall with three doors and the wall cut');

try {
const coverage=await ev(`(async()=>{const T=await import('three'),root=__backroom.scene.scene,out=[];
root.updateMatrixWorld(true);for(let i=0;i<4;i++){const m=root.getObjectByName('media_screen_'+i),a=m.geometry.attributes.position,normal=new T.Vector3(0,0,1).transformDirection(m.matrixWorld),ray=new T.Raycaster();let visible=0;
for(let x=0;x<3;x++)for(let y=0;y<3;y++){const b=m.geometry.boundingBox||new T.Box3().setFromBufferAttribute(a),p=new T.Vector3(b.min.x+(b.max.x-b.min.x)*(.15+x*.35),b.min.y+(b.max.y-b.min.y)*(.15+y*.35),(b.min.z+b.max.z)/2).applyMatrix4(m.matrixWorld);ray.set(p.clone().addScaledVector(normal,.4),normal.clone().negate());ray.far=.6;const hits=ray.intersectObjects(root.children,true).filter(h=>{for(let n=h.object;n;n=n.parent)if(!n.visible)return false;return h.object.isMesh&&!h.object.material?.transparent;});if(hits[0]?.object===m)visible++;}out.push({name:m.name,visible});}return out})()`);
ok(coverage.every(s=>s.visible===9),'all wall screens uncovered at nine points '+JSON.stringify(coverage));
await ev(`__backroom.scene.customization.open();document.querySelectorAll('.br-custom-items button')[1].click()`);
await sleep(600);
ok(await ev(`document.querySelectorAll('.br-vending-pick:not([hidden])').length===9`),'Decorations has six props and three statue pedestals');
for(let piece=0;piece<3;piece++){
 await ev(`document.querySelectorAll('.br-vending-pick')[${piece+6}].click()`);
 await ev(`document.querySelector('.br-custom-remove').click()`);
 ok(await ev(`__backroom.scene.customization.getState().statues[${piece}]===-1&&['knight','queen','rook'].every(k=>!__backroom.scene.scene.getObjectByName('statue_spot_${piece}_'+k).visible)`),'Remove hides statue and pedestal '+piece);
 await ev(`document.querySelector('.br-custom-remove').click()`);
 ok(await ev(`__backroom.scene.customization.getState().statues[${piece}]===${piece}`),'Use restores statue and pedestal '+piece);
}
await ev(`__backroom.scene.customization.setOwned(['monstera','ivy','terrarium','gallery','portraits','billboard'])`);
for(let prop=0;prop<6;prop++){
 const name=['prop_monstera','prop_hanging_ivy','prop_terrarium','prop_gallery_landscape','prop_portrait_pair','prop_deco_billboard'][prop];
 await ev(`document.querySelectorAll('.br-vending-pick')[${prop}].click()`);
 await ev(`document.querySelector('.br-custom-remove').click()`);
 ok(await ev(`!__backroom.scene.scene.getObjectByName('${name}').visible&&!__backroom.scene.customization.getState().props[${prop}]`),'Remove keeps decoration hidden while selected '+prop);
 await ev(`document.querySelector('.br-custom-remove').click()`);
 ok(await ev(`__backroom.scene.scene.getObjectByName('${name}').visible&&__backroom.scene.customization.getState().props[${prop}]`),'Use restores decoration '+prop);
}
await shot('decorations.png');
await ev(`__backroom.scene.customization.select('screens',true,2);__backroom.scene.customization.preview('screens',2,2)`);await sleep(500);
const grid=await ev(`(()=>{const u=__backroom.scene.scene.getObjectByName('screen_surface_ceiling_projection').material.uniforms;return {grid:u.grid.value,sources:new Set(['a','ax','ay','b','bx','by'].map(k=>u[k].value.uuid)).size}})()`);
ok(grid.grid===1&&grid.sources===6,'ceiling uses six distinct shared media sources '+JSON.stringify(grid));await shot('projector.png');
await ev(`__backroom.scene.customization.select('screens',false,2);__backroom.scene.customization.select('screens',true,0);__backroom.scene.customization.select('screens',true,1);__backroom.scene.customization.preview('screens',0,0)`);await sleep(500);await shot('entrance-screens.png');
await ev(`document.querySelectorAll('.br-custom-items button')[0].click();document.querySelectorAll('.br-vending-pick')[4].click()`);await sleep(700);await shot('queen-lever-room.png');
const sizes=await ev(`(async()=>{const T=await import('three'),rig=__backroom.scene.scene.getObjectByName('station_slot:rose'),box=new T.Box3().setFromObject(rig.getObjectByName('chess_handle_socket'));return box.getSize(new T.Vector3()).toArray()})()`);
ok(sizes[1]>.24,'custom lever reads at full handle size '+JSON.stringify(sizes));
for(const [w,h] of [[390,844],[844,390]]){await cdp('Emulation.setDeviceMetricsOverride',{width:w,height:h,deviceScaleFactor:1,mobile:true});await sleep(500);await shot('room-service-'+w+'.png');}
await ev(`__backroom.scene.customization.dismiss()`);
if(!process.env.ROOM_LAYOUT_QUICK)for(const [w,h] of [[1280,720],[390,844],[844,390]]){
 await cdp('Emulation.setDeviceMetricsOverride',{width:w,height:h,deviceScaleFactor:1,mobile:w<1000});
 for(let style=0;style<3;style++){
  await ev(`__backroom.scene.customization.select('handles',${style},0)`);
  await ev(`__backroom.visit(__backroom.stations.find(s=>s.key==='slot:rose'))`);
  ok(await until("document.querySelector('.slot-station')?.dataset.phase==='play'",20000),'custom handle entry '+w+'/'+style);
  await sleep(250);
  const fit=await ev(`(async()=>{const T=await import('three'),s=__backroom.scene,n=s.scene.getObjectByName('station_slot:rose').getObjectByName('chess_handle_socket'),b=new T.Box3().setFromObject(n),p=[];for(const x of [b.min.x,b.max.x])for(const y of [b.min.y,b.max.y])for(const z of [b.min.z,b.max.z])p.push(new T.Vector3(x,y,z).project(s.camera));return p.every(v=>Math.abs(v.x)<=1&&Math.abs(v.y)<=1)})()`);
  ok(fit,'full-size custom lever fits seated view '+w+'/'+style);
  if(style===1)await shot('queen-playing-'+w+'.png');
  await ev(`__backroom.back()`);await until("!__backroom.loader.current&&!__backroom.scene.transitioning&&!__backroom.scene.seated",8000);await sleep(150);
 }
}
await writeFile(join(OUT,'report.json'),JSON.stringify({coverage,grid,sizes,errs},null,2));
}catch(e){ok(false,e.stack||String(e));}
ok(errs.length===0,'no browser errors '+errs.join(' | '));await done(fails?1:0);
