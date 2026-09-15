
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
const PORT = Number(process.env.CARDS_3D_PORT || 8908), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-cards-'));
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
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });

const summary = {};
const dbg = () => ev('window.dev.station.debug()');

/* ---------------------------------------------------------------- 9. through the room */
const PICS = ['/backroom/stations/slot/fallback/gif0.webp', '/dtrh/assets/bubbles/effects/spirals/sp6.gif', '/arcademy/art/bugle/g1.webp', '/backroom/stations/slot/fallback/gif1.webp',
  '/backroom/room/assets/ads/dtrh.webp', '/arcademy/art/bugle/g2.webp', '/backroom/stations/slot/fallback/gif2.webp', '/dtrh/assets/bubbles/effects/spirals/sp7.gif',
  '/arcademy/art/bugle/g3.webp', '/backroom/stations/slot/fallback/gif3.webp', '/backroom/room/assets/ads/arcademy.webp', '/arcademy/art/bugle/g4.webp', '/backroom/room/assets/ads/focus-gaze.webp'];
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const PICS = ${JSON.stringify(PICS)};
  window.__posted = []; window.__emit = emit;
  const serverP = import('/backroom/stations/cards/mock-server.js').then((m) => { const s = m.createMockServer({ sp: 57, floorMs: 600 }); s.script('Th', '9d', '8c', '8s'); window.__server = s; return s; });
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(m);
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', open: null,
        gates: { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true }, lex: { br_back: 'Back', br_balance: 'SP' }, stations: ['slot', 'wheel', 'cards'] });
      if (m.type === 'station-request') serverP.then((s) => s.handle(m.op, m.body, m.idem)).then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') { const n = m.count || 4; emit({ type: 'media', reqId: m.reqId, seed: 1, words: [], gifs: PICS.slice(0, n).map((url, i) => ({ key: 'g' + i, url, w: 0, h: 0, src: 'pool' })) }); }
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;
const until = async (expr, ms = 12000) => { for (let i = 0; i < ms / 50; i++) { if (await ev(expr)) return true; await sleep(50); } return false; };
const click = (selector) => ev(`document.querySelector(${JSON.stringify(selector)}).click()`);
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); };
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
const phone = process.argv.includes('--phone');
if (phone) {
  await cdp('Emulation.setDeviceMetricsOverride', { width: 400, height: 800, deviceScaleFactor: 1, mobile: true });
  await cdp('Emulation.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (Linux; Android 12; Pixel 5) AppleWebKit/537.36 Chrome/120.0 Mobile Safari/537.36' });
  await cdp('Page.addScriptToEvaluateOnNewDocument', { source: "Object.defineProperty(navigator,'deviceMemory',{get:()=>4});Object.defineProperty(navigator,'hardwareConcurrency',{get:()=>4});" });
}
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until("document.documentElement.classList.contains('br-ready')", 45000), 'room boots');
const openAt = performance.now();
await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='cards'));true");
ok(await until("!!window.__backroom.scene.scene.getObjectByName('cards_runtime')"), '3D cards mount on room renderer');
const openMs = performance.now() - openAt;
const view = () => ev("window.__backroom.scene.scene.getObjectByName('cards_runtime')?.userData.debug()");
const pose = await ev('window.__backroom.scene.debug()');
await sleep(1500); await shot('sit-fan.png');
ok(await until("document.querySelector('.cards-station')?.dataset.phase==='play'"), 'sit fan completes');
ok((await ev('document.querySelectorAll("canvas").length')) === 1, 'only room canvas attached');
ok((await ev('window.__backroom.scene.debug().pitch')) === pose.pitch, 'sit fan preserves seated pitch');
const rows = [];
for (let run = 0; run < 3; run++) {
  if (run === 1) await ev("window.__server.script('5s','9d','6h','7c','3d','2s')");
  if (run === 2) await ev("window.__server.script('8s','9d','8h','7c','3s','2c','Th','2d')");
  ok(await until("!document.querySelector('.cards-deal').disabled"), 'deal unlocks');
  await click('.cards-deal');
  ok(await until("!document.querySelector('.cards-move[data-move=stand]').disabled"), 'decision opens');
  const d = await view(); ok(d?.cards.length === 4 && d.cards.find(c=>c.owner==='d'&&c.slot===1).code === null, 'only server-known cards visible; hole is hidden');
  await shot('deal-' + run + '.png');
  if (run === 1) {
    await click('.cards-move[data-move=hit]');
    ok(await until("window.__backroom.scene.scene.getObjectByName('cards_runtime').userData.debug().cards.length===5"), 'hit adds exactly one server card');
    ok(await until("!document.querySelector('.cards-move[data-move=stand]').disabled"), 'hit settles before next move');
  }
  if (run === 2) {
    await click('.cards-move[data-move=split]');
    ok(await until("window.__backroom.scene.scene.getObjectByName('cards_runtime').userData.debug().hands===2 && !document.querySelector('.cards-move[data-move=double]').disabled"), 'split puts two hands on authored slots');
    await shot('split.png');
    await click('.cards-move[data-move=double]');
    ok(await until("window.__backroom.scene.scene.getObjectByName('cards_runtime').userData.debug().active===1 && !document.querySelector('.cards-move[data-move=stand]').disabled"), 'double deals once and moves to hand two');
  }
  await click('.cards-move[data-move=stand]');
  ok(await until("window.__backroom.scene.scene.getObjectByName('cards_runtime').userData.debug().cards.every(c=>c.code&&c.landed)"), 'dealer reveals exact reply');
  if (run === 0) {
    ok(await until("document.querySelector('.cards-deal').hasAttribute('data-held')", 4000), 'fullscreen win moment holds Deal');
    const locked = await ev(`(async()=>{
      const T=await import('three'), s=window.__backroom.scene, shoe=s.scene.getObjectByName('deck_shoe_base');
      const p=new T.Box3().setFromObject(shoe).getCenter(new T.Vector3()).project(s.camera), r=s.renderer.domElement.getBoundingClientRect();
      const e={clientX:r.left+(p.x+1)*r.width/2,clientY:r.top+(1-p.y)*r.height/2,button:0};
      const hits=s.pickAt(e,[shoe]).length, before=window.__posted.filter(m=>m.op==='deal').length;
      s.renderer.domElement.dispatchEvent(new PointerEvent('pointerdown',{...e,bubbles:true}));
      await new Promise(r=>setTimeout(r,30));
      return {hits,before,after:window.__posted.filter(m=>m.op==='deal').length};
    })()`);
    ok(locked.hits>0 && locked.before===locked.after, 'raycast shoe press during fullscreen moment sends no Deal');
  }
  await sleep(3000); rows.push(await ev('window.__backroom.scene.debug()'));
  const exact = await ev(`(async()=>{
    const h=(await window.__server.handle('state',{})).body.hand;
    const shown=window.__backroom.scene.scene.getObjectByName('cards_runtime').userData.debug().cards;
    const expected=h.hands.flatMap((x,owner)=>x.cards.map((code,slot)=>({owner,slot,code})));
    h.dealer.forEach((code,slot)=>expected.push({owner:'d',slot,code}));
    const keys=(xs)=>xs.map(c=>[c.owner,c.slot,c.code].join(':')).sort().join('|');
    return keys(expected)===keys(shown);
  })()`);
  ok(exact, 'every public reply card occupies its exact slot with no extra cards');
  await shot('settle-' + run + '.png');
}
// A losing hand and a blackjack bloom retain the same host moments in the 3D view.
await ev("window.__server.script('Th','9d','7c','Ts')");
ok(await until("!document.querySelector('.cards-deal').disabled"), 'loss scenario can deal');
await click('.cards-deal');
ok(await until("!document.querySelector('.cards-move[data-move=stand]').disabled"), 'loss scenario decision opens');
await click('.cards-move[data-move=stand]');
ok(await until("/takes/.test(document.querySelector('.cards-status').textContent)"), 'losing reply reaches existing moment');
await shot('lose.png');
await ev("window.__server.script('As','9d','Kh','7c')");
ok(await until("!document.querySelector('.cards-deal').disabled"), 'loss moment releases Deal');
await click('.cards-deal');
ok(await until("window.__posted.some(m=>m.fxId==='fx.gif_from')"), 'blackjack bloom fires from the 3D ace');
await shot('bloom.png');
await ev("window.__emit({type:'settings',motion:'off',intensity:'calm',reduced:true,gates:{flash:false,spiral:false,brainDrain:false,tunnel:false}})");
await sleep(150); await shot('calm-motion-off.png');
const closeAt = performance.now(); await ev('window.__backroom.back()');
ok(await until("!document.querySelector('.cards-station')", 1000), 'Back closes immediately');
const closeMs = performance.now() - closeAt;
ok((await ev("window.__backroom.scene.scene.getObjectByName('cards_runtime')===undefined")), 'runtime mesh disposed on close');
ok((await ev('document.querySelectorAll("canvas").length')) === 1, 'close leaves one room canvas');
ok(!errs.length, 'no page exceptions: ' + errs.join('\n'));
await writeFile(join(OUT, phone ? 'phone-perf.json' : 'desktop-perf.json'), JSON.stringify({ device: phone ? '400x800 headless Android emulation, not physical phone' : '1280x720 headless', openMs, closeMs, runs: rows, errors: errs, fails }, null, 2));
await done(fails ? 1 : 0);
