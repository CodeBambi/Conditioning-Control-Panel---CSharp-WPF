#!/usr/bin/env node
/* ============================================================================
 * render-counter-prize-cards.mjs - the Prize Parlour's card art (CONTRACT 10.17.F): one still per prize,
 * rendered from the approved shelf prop in the room's own counter.glb.
 *
 *   node ConditioningControlPanel/Scripts/render-counter-prize-cards.mjs [--size 320] [--max-kb 60]
 *
 * Loads Resources/web/backroom/room/assets/counter.glb in headless Chrome with the vendored three.js (GLTFLoader and
 * the meshopt decoder, as the room does), finds every `shelf_<prizeId>` group by its `prize_id` extras, hides the rest
 * of the counter, frames the group from a slight three-quarter angle and writes
 * Resources/web/backroom/stations/counter/art/<prizeId>.webp: square, transparent, under --max-kb (quality steps
 * down until it fits). Never at runtime. Nothing leaves the machine: Resources/web is served on 127.0.0.1 and the
 * only process this stops is the Chrome it started, by its own handle. CHROME: CHROME_PATH, else the usual install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const arg = (k, d) => { const i = process.argv.indexOf(k); return i > 0 ? Number(process.argv[i + 1]) : d; };
const SIZE = arg('--size', 320), MAX_KB = arg('--max-kb', 60);
const WEB = resolve(fileURLToPath(import.meta.url), '../../Resources/web');
const OUT = join(WEB, 'backroom/stations/counter/art');
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('no chrome at ' + CHROME); process.exit(1); }
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const PAGE = `<!doctype html><meta charset="utf-8"><body style="margin:0;background:transparent">
<script type="importmap">{"imports":{"three":"/vendor/three/three.module.min.js","three/addons/":"/vendor/three/addons/"}}</script>
<script type="module">
import * as T from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
window.render = async (size, maxKb) => {
  const canvas = document.createElement('canvas'); canvas.width = canvas.height = size; document.body.append(canvas);
  const renderer = new T.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(1); renderer.setSize(size, size, false); renderer.setClearColor(0x000000, 0);
  renderer.toneMapping = T.ACESFilmicToneMapping; renderer.toneMappingExposure = 1.1;
  const scene = new T.Scene();
  const pm = new T.PMREMGenerator(renderer), studio = new T.Scene();   // the room's soft studio, baked once
  studio.background = new T.Color('#54405f');
  for (const p of [[0, 4, 0], [-4, 1, 1], [4, 1, -1], [0, 1, 4]]) {
    const panel = new T.Mesh(new T.PlaneGeometry(3, 3), new T.MeshBasicMaterial({ color: new T.Color(2, 1.5, 1.9), side: T.DoubleSide }));
    panel.position.set(...p); panel.lookAt(0, 0, 0); studio.add(panel);
  }
  scene.environment = pm.fromScene(studio, 0.04).texture; scene.environmentIntensity = 0.5;
  scene.add(new T.HemisphereLight(0xf4c3e8, 0x40314c, 1.4));
  const key = new T.DirectionalLight(0xffe4f2, 2.4), rim = new T.DirectionalLight(0xbbaeff, 1.4);
  scene.add(key, rim, key.target, rim.target);
  const loader = new GLTFLoader(); loader.setMeshoptDecoder(MeshoptDecoder);
  const gltf = await loader.loadAsync('/backroom/room/assets/counter.glb');
  scene.add(gltf.scene); gltf.scene.updateMatrixWorld(true);
  const groups = []; gltf.scene.traverse((o) => { if (o.userData && o.userData.prize_id && /^shelf_/.test(o.name)) groups.push(o); });
  const camera = new T.PerspectiveCamera(28, 1, 0.01, 50), out = {};
  for (const g of groups) {
    gltf.scene.traverse((o) => { o.visible = false; });
    for (let a = g; a; a = a.parent) a.visible = true;
    g.traverse((o) => { o.visible = true; });
    const box = new T.Box3().setFromObject(g), c = box.getCenter(new T.Vector3()), s = box.getSize(new T.Vector3());
    const r = Math.max(s.x, s.y, s.z) * 0.62, dist = r / Math.tan(T.MathUtils.degToRad(camera.fov / 2)) * 1.08;
    const dir = new T.Vector3(0.34, 0.2, 1).normalize();
    camera.position.copy(c).addScaledVector(dir, dist); camera.near = dist / 20; camera.far = dist * 4;
    camera.lookAt(c); camera.updateProjectionMatrix();
    key.position.copy(c).add(new T.Vector3(1.2, 1.6, 2)); key.target.position.copy(c);
    rim.position.copy(c).add(new T.Vector3(-1.5, 0.8, -1.2)); rim.target.position.copy(c);
    renderer.render(scene, camera);
    let q = 0.9, url = canvas.toDataURL('image/webp', q);
    while (url.length * 0.75 > maxKb * 1024 && q > 0.3) { q -= 0.08; url = canvas.toDataURL('image/webp', q); }
    out[g.userData.prize_id] = { data: url.slice(url.indexOf(',') + 1), q: Math.round(q * 100), tris: (() => { let n = 0; g.traverse((o) => { if (o.isMesh) n += (o.geometry.index ? o.geometry.index.count : o.geometry.attributes.position.count) / 3; }); return n; })() };
  }
  return out;
};
window.ready = true;
</script>`;

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.glb': 'model/gltf-binary', '.webp': 'image/webp', '.png': 'image/png', '.wasm': 'application/wasm' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path === '/__render.html') { res.writeHead(200, { 'content-type': 'text/html' }); return res.end(PAGE); }
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
const PORT = Number(process.env.RENDER_PORT || 8911), DEBUG_PORT = PORT + 500;
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'counter-art-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader', 'about:blank'], { stdio: 'ignore' });
async function done(code) { try { chrome.kill(); } catch { /* our own child only */ } server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch { /* noop */ } process.exit(code); }

let target = null;
for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch { /* not up */ } }
if (!target) { console.error('chrome never answered'); await done(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map();
ws.onmessage = (e) => { const m = JSON.parse(e.data); if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') console.error('page: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text)); };
const cdp = (method, params) => new Promise((r) => { const i = ++msgId; waits.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => { const r = (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result; if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text); return r.result.value; };
await cdp('Runtime.enable');
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/__render.html` });
for (let i = 0; i < 100 && !(await ev('!!window.ready').catch(() => false)); i++) await sleep(100);

try {
  const out = await ev(`window.render(${SIZE}, ${MAX_KB})`);
  const ids = Object.keys(out);
  if (ids.length !== 8) throw new Error(`expected 8 prize groups, found ${ids.length}: ${ids.join(', ')}`);
  await mkdir(OUT, { recursive: true });
  for (const id of ids.sort()) {
    const buf = Buffer.from(out[id].data, 'base64');
    if (buf.length > MAX_KB * 1024) throw new Error(`${id}.webp is ${buf.length} bytes, over ${MAX_KB} KB`);
    await writeFile(join(OUT, `${id}.webp`), buf);
    console.log(`  ${id}.webp  ${(buf.length / 1024).toFixed(1)} KB  q${out[id].q}  ${Math.round(out[id].tris)} tris`);
  }
  await done(0);
} catch (e) { console.error('render failed: ' + e.message); await done(1); }
