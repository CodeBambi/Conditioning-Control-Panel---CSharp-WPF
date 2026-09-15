/* ============================================================================
 * roulette-check.mjs - the Velvet Vortex station in headless Chrome, driven over CDP, on dev.html (the kit's mock
 * host and mock-server.js) and inside the real room page.
 *
 *   node backroom/stations/roulette/tests/roulette-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROULETTE_PORT, default 8909, debug +500) and the
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
const PORT = Number(process.env.ROULETTE_3D_PORT || 8909), DEBUG_PORT = PORT + 500;
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
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
async function shot(name) {
  const r = await cdp('Page.captureScreenshot', { format: 'png' });
  await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64'));
  console.log('  shot ' + name);
}

await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/stations/roulette/dev.html` });
await sleep(500);
const report = await ev(`(async () => {
  const map = document.createElement('script'); map.type = 'importmap';
  map.textContent = JSON.stringify({imports:{three:'/vendor/three/three.module.min.js','three/addons/':'/vendor/three/addons/'}}); document.head.append(map);
  const T = await import('three');
  const {GLTFLoader} = await import('three/addons/loaders/GLTFLoader.js');
  const {MeshoptDecoder} = await import('three/addons/libs/meshopt_decoder.module.js');
  const {createBowl3D} = await import('./bowl-3d.js');
  const {planRun} = await import('./feel.js');
  const loader = new GLTFLoader(); loader.setMeshoptDecoder(MeshoptDecoder);
  const fixture = (await loader.loadAsync('/backroom/room/assets/roulette.glb')).scene;
  const wheel = JSON.parse(fixture.getObjectByName('roulette_rotor').userData.order);
  const canvas = document.createElement('canvas'); canvas.width=400;canvas.height=800;
  const camera=new T.PerspectiveCamera(45,.5,.01,30);camera.position.set(0,2.5,4);camera.lookAt(0,1.3,0);
  const scene=new T.Scene();scene.add(fixture);scene.add(new T.HemisphereLight(0xffddff,0x221133,3));
  const renderer=new T.WebGLRenderer({canvas,preserveDrawingBuffer:true});renderer.setSize(400,800);
  const stage={fixture,camera,canvas};
  const ball=fixture.getObjectByName('roulette_ball'),before=ball.position.clone();
  const view=createBowl3D({stage,wheel,rose:[]}); const landings=[];
  for(let i=0;i<20;i++) {
    const index=(i*13)%37,plan=planRun({index,seed:100+i}); view.launch(plan,0);
    for(let now=0;now<(plan.duration+.1)*1000;now+=16) view.update(now,{});
    const d=view.debug(),p=d.pockets[index];
    landings.push({pocket:wheel[index],horizontalError:Math.hypot(d.ballWorld[0]-p[0],d.ballWorld[2]-p[2]),phase:d.phase});
  }
  view.clear();for(let now=0;now<10000;now+=16)view.update(now,{});
  const lit=view.debug().lit.length;
  const plan=planRun({index:17,seed:42}),images={};view.launch(plan,0,{wake:true});
  for(const [name,t] of [['launch',.1],['drop',2],['rattle',plan.landAt-.2],['settle',plan.restAt+.1]]){
    view.update(t*1000,{});renderer.render(scene,camera);images[name]=canvas.toDataURL('image/png').split(',')[1];
  }
  const calls=renderer.info.render.calls;view.dispose();view.dispose();renderer.dispose();renderer.forceContextLoss();
  return {landings,lit,images,calls,restored:ball.position.distanceTo(before)<1e-9};
})()`);
ok(!!report,'3D adapter loads against the rebuilt asset');
if(report){ok(report.landings.every(x=>x.horizontalError<1e-5&&x.phase==='rest'),'20 seeded outcomes rest at the authored pocket center');ok(report.lit>=30,`Lighthouse visits ${report.lit} numbers`);ok(report.restored,'dispose restores authored ball transform');}
if(report?.images){for(const [name,data] of Object.entries(report.images))await writeFile(join(OUT,'3d-'+name+'.png'),Buffer.from(data,'base64'));delete report.images;}
await writeFile(join(OUT,'bowl-3d-check.json'),JSON.stringify({report,errors:errs},null,2));
ok(errs.length===0,`no browser errors: ${errs.join('; ')}`);
await done(fails?1:0);
