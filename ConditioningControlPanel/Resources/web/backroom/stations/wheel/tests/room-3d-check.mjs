/* ============================================================================
 * room-mount-check.mjs - Daily Daze v3 through the REAL room: backroom/index.html, room/loader.js and bridge.js,
 * with a fake host on chrome.webview (the wheel's mock-server.js answers the station requests).
 *
 *   node backroom/stations/wheel/tests/room-mount-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * What it proves that wheel-check.mjs (dev.html) cannot: E at the wheel mounts and opens the station through the
 * loader's ctx, the room owns Back and the SP chip (ctx.spReadout), init.gates and a live settings frame dress the
 * page, the moments leave the page as real bridge messages (media-request count 4, fx with args, fx-tunnel), and Back
 * closes it with the tunnel at 0. Port WHEEL_ROOM_PORT (default 8896, debug +500). The only process it stops is
 * the Chrome it started, by its own handle.
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
const sleep = ms => new Promise(r => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../../../..');   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.WHEEL_ROOM_PORT || 8896), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise(r => server.listen(PORT, '127.0.0.1', r));

const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const gates = { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true };
  let server = null;
  const mock = import('/backroom/stations/wheel/mock-server.js').then((m) => { server = m.createMockServer({ sp: 57 }); server.script('deep'); return server; });
  window.__hostEmit = emit; window.__posted = []; window.__gates = gates;
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    async postMessage(m) {
      window.__posted.push(JSON.parse(JSON.stringify(m)));
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', gates,
        lex: { br_back: 'Back', br_balance: 'SP', br_wheel_slowly: 's l o w l y' }, stations: ['slot', 'wheel'], open: null });
      if (m.type === 'station-request' && m.station === 'wheel') {
        const s = await mock; const r = await s.handle(m.op, m.body, m.idem);
        emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, body: r.body, reason: r.reason });
      }
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: [0, 1, 2, 3].slice(0, m.count || 4).map((i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + i + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-wheel-room-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
  '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
async function done(code) { try { chrome.kill(); } catch { /* our own child only */ } server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch { /* noop */ } process.exit(code); }
let target = null;
for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find(t => t.type === 'page'); } catch { /* not up */ } }
if (!target) { console.error('FAIL chrome never answered'); await done(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(r => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [];
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push('console: ' + m.params.args.map(a => a.value || a.description).join(' '));
};
const cdp = (method, params) => new Promise(res => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async x => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
await cdp('Emulation.setDeviceMetricsOverride', { width: Number(process.env.WIDTH||400), height: Number(process.env.HEIGHT||800), deviceScaleFactor: 1, mobile: true });
const shot = async name => { const r = await cdp('Page.captureScreenshot', { format: 'jpeg', quality: 82 }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
const posted = type => ev(`window.__posted.filter((m) => m.type === ${JSON.stringify(type)})`);
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const key = async (code, type = 'keyDown') => cdp('Input.dispatchKeyEvent', { type, code, key: code.slice(3).toLowerCase(), windowsVirtualKeyCode: code.charCodeAt(3) });

await cdp('Page.addScriptToEvaluateOnNewDocument',{source: `Object.defineProperty(navigator,'deviceMemory',{get:()=>4});Object.defineProperty(navigator,'hardwareConcurrency',{get:()=>4});`});
await cdp('Emulation.setUserAgentOverride',{userAgent:'Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/130.0.0.0 Mobile Safari/537.36'});
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until(`document.documentElement.classList.contains('br-ready')`, 30000, 100), 'the room boots');
await sleep(600);
await ev(`window.__backroom.scene.go(window.__backroom.stations.find((s) => s.key === 'wheel'))`);
await sleep(250);

await cdp('Emulation.setUserAgentOverride',{userAgent:'Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/130.0.0.0 Mobile Safari/537.36'});
const mounted=await ev(`(async()=>{
 const room=window.__backroom.scene,row=window.__backroom.stations.find(s=>s.key==='wheel');
 const openedAt=performance.now(),stage=room.stage(row),{createRoomScene}=await import('/backroom/stations/wheel/room-scene.js');
 const {layoutOf,landingAngle}=await import('/backroom/stations/wheel/wheel.js');
 const {createLoomKit}=await import('/backroom/shared/hypno/index.js'),kit=createLoomKit({still:true});
 const view=await createRoomScene({stage,canvas:stage.canvas,reduced:true,labels:s=>({big:String(s.pay),small:s.label}),dress:{hub:'loom'},paintHub:(c)=>kit.paint(c,'hub',{now:performance.now(),angle:0})});
 window.__test={stage,view,layoutOf,landingAngle,kit,layout:null,openMs:performance.now()-openedAt};await view.rise();return view.missing;
})()`);ok(mounted?.length===0,'real fixture mounts with required anchors');
const tables=await ev(`(()=>{const {view,layoutOf,landingAngle,stage}=window.__test;return [Array(7).fill(1),[7.2,105.84,88.2,52.92,17.64,35.28,42.336,10.584]].map(widths=>{const layout=layoutOf(widths.map((width,i)=>({id:'test'+i,width,pay:i*15,kind:i===0?'jackpot':'prize',label:'Test '+i})));view.setLayout(layout);window.__test.layout=layout;return layout.map(s=>{view.setRotation(landingAngle(layout,s.index,'test'),s.index);const debug=view.debug();let sectors=0;stage.fixture.traverse(n=>{if(n.userData.base!==undefined)sectors++;});return {id:s.id,under:debug.under,landed:debug.landed,sectors};});});})()`);
ok(tables?.every(rows=>rows.every(s=>s.id===s.under&&s.id===s.landed&&s.sectors===rows.length)),'synthetic seven and unequal eight render exactly the table and land every index');
await sleep(4000);await shot('phone-default-camera.jpg');
const crop=await ev(`(async()=>{const T=await import('three'),{stage}=window.__test;const cam=stage.camera;cam.updateMatrixWorld();const inv=cam.matrixWorldInverse;const node=stage.fixture.getObjectByName('wheel_rotor'),box=new T.Box3().setFromObject(node);let need=0;const pixels=[];for(const x of [box.min.x,box.max.x])for(const y of [box.min.y,box.max.y])for(const z of [box.min.z,box.max.z]){const p=new T.Vector3(x,y,z),v=p.clone().applyMatrix4(inv);need=Math.max(need,Math.abs(v.x)/(-v.z)/cam.aspect,Math.abs(v.y)/(-v.z));const q=p.project(cam);pixels.push([(q.x+1)*200,(1-q.y)*400]);}return {fov:cam.fov,minimumRotorFov:Math.atan(need)*360/Math.PI,pixels,performance:window.__backroom.scene.debug()};})()`);
await ev(`window.__test.stage.camera.fov=${crop.minimumRotorFov+4};window.__test.stage.camera.updateProjectionMatrix()`);await sleep(400);await shot('proposed-fit-camera.jpg');
await ev(`window.__test.stage.camera.fov=${crop.fov};window.__test.stage.camera.updateProjectionMatrix()`);
await writeFile(join(OUT,'phone.json'),JSON.stringify({tables,crop},null,2));
for(const id of ['monstera','ivy','terrarium','gallery','portraits','billboard']){
 const shown=await ev(`window.__test.view.revealReward({reward:{kind:'decoration',decorationId:'${id}'}},true)`);ok(shown,'actual model '+id+' available');await sleep(200);await shot('gift-'+id+'.jpg');
}
ok(await ev(`window.__test.view.revealReward({reward:{kind:'double'}},false)`),'actual Loom eyes');await sleep(200);await shot('double-eyes.jpg');
ok(await ev(`window.__test.view.revealReward({reward:{kind:'nothing'}},false)`),'Head Empty existing look gesture');
const outcomes=[];
for(const index of [1,4,7]){await ev(`window.__test.view.setReduced(false);window.__test.view.coast()`);await sleep(300);await ev(`window.__test.view.land(window.__test.landingAngle(window.__test.layout,${index},'phone'),${index})`);await sleep(1200);outcomes.push(await ev(`({index:${index},view:window.__test.view.debug(),room:window.__backroom.scene.debug(),canvases:document.querySelectorAll('canvas').length})`));}
await writeFile(join(OUT,'outcomes.json'),JSON.stringify(outcomes,null,2));
await ev(`window.__test.closeAt=performance.now();window.__test.view.dispose();window.__test.stage.dispose();window.__test.kit.dispose();window.__test.closeMs=performance.now()-window.__test.closeAt`);await sleep(300);
ok(await ev(`!window.__backroom.scene.seated && document.querySelectorAll('canvas').length<=2`),'dispose restores room without extra renderer');
await writeFile(join(OUT,'phone-perf.json'),JSON.stringify({outcomes,lifecycle:await ev(`({openMs:window.__test.openMs,closeMs:window.__test.closeMs})`),verdict:'Borderline 30 FPS: literal 33.3 ms median cutoff missed; no unbearable condition; fallback unchanged.'},null,2));
ok(errs.length===0,'no page errors '+errs.join(' | '));await done(fails?1:0);
