/* ============================================================================
 * backroom/smoke/tap-anywhere-check.mjs - a tap anywhere on a fixture enters it, on a phone and on a desk.
 *
 *   node backroom/smoke/tap-anywhere-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * The owner's iPhone (2026-09-15, after #1280) still needed "precise spots" on the slots and the vending machine.
 * Two things in scene.js answer that, and this check holds both:
 *   1. A finger tap is a tap: the slop between down and up is 14 css px (a finger rolls; 8 dropped real taps), the
 *      tap keeps its own record so a capture lost before the up (Safari) cannot clear it, and a pointer whose up
 *      never arrived does not hold the room. Every tap here wobbles 10 px and holds 250 ms, as a thumb does.
 *   2. The generous pick: after the exact ray, the fixture whose projected screen box (plus a 24 px margin) holds
 *      the point enters, so the edges, the gaps between parts, the space just above the topper, all enter.
 * Every station fixture (three cabinets, the vending machine, the wheel, the cards table, the roulette, the counter)
 * gets six points at its approach pose: the centre, four near-edge points and a halo point 12 px outside the box,
 * portrait and landscape on the iPhone emulation, then a mouse on a desk window.
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (TAP_ANYWHERE_PORT, default 8942, debug +500).
 * The only process it stops is the Chrome it started, by its own handle. CHROME: CHROME_PATH, else the usual install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0, passes = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else { console.log('  ok  ' + what); passes++; } };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../..');   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.TAP_ANYWHERE_PORT || 8942), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

// One fake host: each station's own mock-server.js, the bell's mock, fallback art for the deal.
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const made = {};
  const mock = (id) => made[id] || (made[id] = import(id === 'bell' ? '/backroom/smoke/mock-bell.js' : '/backroom/stations/' + id + '/mock-server.js')
    .then((m) => (id === 'bell' ? m.createBellMock({}) : m.createMockServer({ sp: 57 }))));
  window.__posted = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(JSON.parse(JSON.stringify(m)));
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en',
        gates: { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true }, lex: { br_back: 'Back', br_balance: 'SP' },
        stations: ['slot', 'wheel', 'cards', 'roulette', 'counter', 'bell'], open: true });
      if (m.type === 'station-request') mock(m.station).then((s) => s.handle(m.op, m.body || {}, m.idem))
        .then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }))
        .catch(() => emit({ type: 'station-result', reqId: m.reqId, ok: false, status: 500, reason: 'mock', body: {} }));
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-tap-anywhere-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=390,844', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
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
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const phone = (width, height) => cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: true, screenOrientation: { type: width > height ? 'landscapePrimary' : 'portraitPrimary', angle: width > height ? 90 : 0 } });
const pt = (id, x, y) => ({ id, x, y, radiusX: 5, radiusY: 5, force: 1 });
const send = async (type, points) => { const r = await cdp('Input.dispatchTouchEvent', { type, touchPoints: points }); if (r.error) throw new Error(JSON.stringify(r.error)); };
/* A thumb, not a stylus: down, a 10 px roll over the hold, up after 250 ms. */
const fingerTap = async (x, y) => { await send('touchStart', [pt(9, x, y)]); await sleep(90); await send('touchMove', [pt(9, x + 7, y - 7)]); await sleep(90); await send('touchMove', [pt(9, x + 4, y - 9)]); await sleep(70); await send('touchEnd', []); };
const mouseClick = async (x, y) => {
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y }); await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount: 1 });
  await sleep(60); await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x: x + 2, y: y + 1 }); await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x: x + 2, y: y + 1, button: 'left', clickCount: 1 });
};
const boot = async (label) => {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
  ok(await until(`document.documentElement.classList.contains('br-ready')`, 45000, 100), `${label}: the room boots`);
  await ev("try { sessionStorage.clear(); } catch (e) {}");
  // Motion off: the entry and Back snap (slot-entry-check holds the pan); this check is about the pick, ninety-odd times.
  await ev("window.__backroom.state.motion = 'off'; true");
  await sleep(300);
};
const ROWS = [['slot:rose', 'slot'], ['slot:violet', 'slot'], ['slot:mint', 'slot'], ['customization', null], ['wheel', 'wheel'], ['cards', 'cards'], ['roulette', 'roulette'], ['counter', 'counter']];
const rowExpr = (key) => key === 'customization' ? 'window.__backroom.scene.customization.row' : `window.__backroom.stations.find((s) => s.key === ${JSON.stringify(key)})`;
const goTo = async (key) => { await ev(`window.__backroom.scene.go(${rowExpr(key)})`); await until('!window.__backroom.scene.transitioning', 6000); await sleep(150); };
const entered = (id) => id ? `(window.__backroom.scene.seated || window.__backroom.scene.transitioning || !!window.__backroom.loader.current)` : `window.__backroom.scene.debug().customization.opened`;
const opened = (id, before) => id ? `window.__posted.filter((m) => m.type === 'station-open' && m.station === ${JSON.stringify(id)}).length > ${before}` : entered(null);
const openCount = (id) => id ? ev(`window.__posted.filter((m) => m.type === 'station-open' && m.station === ${JSON.stringify(id)}).length`) : 0;
const leave = async () => {
  if (!(await ev('!!window.__backroom.loader.current || window.__backroom.scene.seated || window.__backroom.scene.transitioning || window.__backroom.scene.debug().customization.opened'))) return true;
  await ev('window.__backroom.back(); true');
  const back = await until('!window.__backroom.loader.current && !window.__backroom.scene.transitioning && !window.__backroom.scene.seated && !window.__backroom.scene.debug().customization.opened && window.__backroom.scene.debug().running', 8000);
  await sleep(150); return back;
};

/* Six points over one fixture at the current pose. The screen box comes from the visible meshes' world corners (the
 * same box scene.js uses), clipped to the viewport; the centre is the box's. The four edge points sit 8 px OUTSIDE the
 * fixture's silhouette, found by walking a ray inward from each side of the box until the exact pick meets the fixture:
 * there the exact ray misses and only the generous pick answers. The halo sits 20 px outside the silhouette from the
 * top (within the 24 px margin), and a fixture whose box fills the screen keeps its edge points inside the viewport.
 * A point under a HUD pill or the thumbstick steps toward the centre until the canvas is what the finger meets. */
const POINTS = `(() => { const s = window.__backroom.scene, cam = s.camera, key = KEY;
  const node = key === 'customization' ? s.scene.getObjectByName('customization_vending') : s.scene.getObjectByName('station_' + key); if (!node) return null;
  const w = innerWidth, h = innerHeight; node.updateWorldMatrix(true, false); cam.updateMatrixWorld();
  const V = node.position.constructor; let mn = null, mx = null; const c = new V();
  node.traverseVisible((m) => { if (!m.isMesh || m.isInstancedMesh || !m.geometry) return; if (!m.geometry.boundingBox) m.geometry.computeBoundingBox(); const b = m.geometry.boundingBox; if (!b) return;
    for (let i = 0; i < 8; i++) { c.set(i & 1 ? b.max.x : b.min.x, i & 2 ? b.max.y : b.min.y, i & 4 ? b.max.z : b.min.z).applyMatrix4(m.matrixWorld); if (!mn) { mn = c.clone(); mx = c.clone(); } else { mn.min(c); mx.max(c); } } });
  if (!mn) return null;
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (let i = 0; i < 8; i++) { c.set(i & 1 ? mx.x : mn.x, i & 2 ? mx.y : mn.y, i & 4 ? mx.z : mn.z).applyMatrix4(cam.matrixWorldInverse); if (c.z > -cam.near) return { behind: true }; c.applyMatrix4(cam.projectionMatrix);
    const sx = (c.x + 1) * w / 2, sy = (1 - c.y) * h / 2; minX = Math.min(minX, sx); maxX = Math.max(maxX, sx); minY = Math.min(minY, sy); maxY = Math.max(maxY, sy); }
  const raw = { minX, minY, maxX, maxY };
  const L = Math.max(8, minX), R = Math.min(w - 8, maxX), Tp = Math.max(8, minY), B = Math.min(h - 8, maxY);
  if (R - L < 24 || B - Tp < 24) return { raw, small: true };
  const cx = (L + R) / 2, cy = (Tp + B) / 2;
  const onCanvas = (x, y) => { const el = document.elementFromPoint(x, y); return !!el && el.classList.contains('br-canvas'); };
  const settle = (x, y, tag) => { for (let i = 0; i < 30 && !onCanvas(x, y); i++) { x += Math.sign(cx - x) * Math.min(12, Math.abs(cx - x)); y += Math.sign(cy - y) * Math.min(12, Math.abs(cy - y)); } return onCanvas(x, y) ? { x: Math.round(x), y: Math.round(y), ...(tag ? { outside: tag } : {}) } : null; };
  const onFixture = (x, y) => { const hit = s.pickAt({ clientX: x, clientY: y }, s.scene.children).find((hh) => { for (let n = hh.object; n; n = n.parent) if (!n.visible) return false; return true; }); if (!hit) return false; for (let n = hit.object; n; n = n.parent) if (n === node) return true; return false; };
  // Walk inward from a side of the box until the exact pick meets the fixture; the point 'out' px back is just off the silhouette.
  const rim = (x, y, dx, dy, out) => { for (let i = 0; i < 400; i++, x += dx * 4, y += dy * 4) { if (x < 8 || x > w - 8 || y < 8 || y > h - 8) return null; if (onFixture(x, y)) { const px = x - dx * out, py = y - dy * out; return px >= 8 && px <= w - 8 && py >= 8 && py <= h - 8 && onCanvas(px, py) && !onFixture(px, py) ? { x: Math.round(px), y: Math.round(py), outside: out + ' px off the silhouette' } : settle(x + dx * 2, y + dy * 2); } } return null; };
  const out = { raw, box: { L, T: Tp, R, B }, centre: settle(cx, cy),
    left: rim(L, cy, 1, 0, 8) || settle(L + 6, cy), right: rim(R, cy, -1, 0, 8) || settle(R - 6, cy), top: rim(cx, Tp, 0, 1, 8) || settle(cx, Tp + 6), bottom: rim(cx, B, 0, -1, 8) || settle(cx, B - 6),
    halo: rim(cx, Tp, 0, 1, 20) || rim(cx, B, 0, -1, 20) || rim(L, cy, 1, 0, 20) || settle(L + (R - L) * .8, Tp + (B - Tp) * .2) };
  return out; })()`;
const pointsFor = (key) => ev(POINTS.replace('KEY', JSON.stringify(key)));
const TAGS = ['centre', 'left', 'right', 'top', 'bottom', 'halo'];
const report = { runs: {}, errors: errs };

async function sweep(label, press, keys, tags) {
  for (const [key, id] of keys) {
    await goTo(key);
    const points = await pointsFor(key);
    ok(points && !points.behind && !points.small && tags.every((t) => points[t]), `${label} ${key}: ${tags.length} points on the canvas over the fixture ` + JSON.stringify(points && points.box));
    if (!points || points.behind || points.small) continue;
    report.runs[label + ' ' + key] = points;
    if (key === 'slot:rose' || key === 'customization') await shot(`${label}-approach-${key.replace(':', '-')}.png`);
    for (const tag of tags) {
      const p = points[tag]; if (!p) continue;
      const before = await openCount(id);
      await press(p.x, p.y);
      let got = (await until(entered(id), 2500)) && (await until(opened(id, before), 12000));
      // A point over the fixture's own mascot barks instead (the NPC guard): that is the room's rule, and it counts.
      const bark = !got && await ev(`(() => { const b = window.__backroom.scene.debug().emiBubble; return b && b.id === ${JSON.stringify(id)} ? b.id : null; })()`);
      if (bark) { got = true; await ev('window.__backroom.scene.dismissEmi(); true'); }
      ok(got, `${label} ${key}: a tap on the ${tag}${p.outside ? ' (' + p.outside + ')' : ''} (${p.x},${p.y}) ${bark ? 'meets the mascot and barks' : 'enters'}`);
      if (got && key === 'slot:rose' && tag === 'halo') await shot(`${label}-entered-by-halo.png`);
      if (got && key === 'customization' && tag === 'left') await shot(`${label}-vending-opened-by-left-edge.png`);
      ok(await leave(), `${label} ${key}: Back returns to walking after the ${tag} tap`);
      await goTo(key);
    }
  }
}

/* ---------------------------------------------------------------- 1. the iPhone, portrait and landscape, a thumb */
await cdp('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 2 });
await cdp('Emulation.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1' });
for (const [width, height] of [[390, 844], [844, 390]]) {
  const label = width > height ? 'landscape' : 'portrait';
  await phone(width, height);
  await boot(label);
  await sweep(label, fingerTap, ROWS, TAGS);
}

/* ---------------------------------------------------------------- 2. a mascot still barks, never visits */
await phone(390, 844);
await goTo('counter');
const npc = await ev(`(() => { const s = window.__backroom.scene, cam = s.camera; const e = s.debug().emis.find((x) => x.id === 'counter'); if (!e) return null;
  const root = s.scene.getObjectByName('station_counter'); const emi = root.getObjectByName('EMI_root') || root.getObjectByName('emi_idle_counter'); if (!emi) return null;
  const V = root.position.constructor; let mn = null, mx = null; const q = new V(); emi.updateWorldMatrix(true, false);
  emi.traverseVisible((m) => { if (!m.isMesh || !m.geometry) return; if (!m.geometry.boundingBox) m.geometry.computeBoundingBox(); const b = m.geometry.boundingBox; for (let i = 0; i < 8; i++) { q.set(i & 1 ? b.max.x : b.min.x, i & 2 ? b.max.y : b.min.y, i & 4 ? b.max.z : b.min.z).applyMatrix4(m.matrixWorld); if (!mn) { mn = q.clone(); mx = q.clone(); } else { mn.min(q); mx.max(q); } } });
  if (!mn) return null; const c = mn.add(mx).multiplyScalar(.5).project(cam);
  return { x: Math.round((c.x + 1) * innerWidth / 2), y: Math.round((1 - c.y) * innerHeight / 2) }; })()`);
if (npc) {
  const before = await openCount('counter');
  await fingerTap(npc.x, npc.y);
  await sleep(400);
  const bark = await ev('window.__backroom.scene.debug().emiBubble');
  const visited = await ev(entered('counter'));
  report.npc = { at: npc, bark, visited };
  ok(bark && bark.id === 'counter' && !visited && (await openCount('counter')) === before, `npc: a thumb on the counter mascot barks (${bark && bark.id}) and does not visit`);
  await shot('portrait-npc-bark.png');
  await ev('window.__backroom.scene.dismissEmi(); true');
} else console.log('  skip  npc: no counter mascot mesh to aim at');

/* ---------------------------------------------------------------- 3. a stale pointer does not hold the room */
await goTo('slot:rose');
{
  // A finger that went down and never came up (Safari over the browser chrome): the next tap must still enter.
  await ev(`(() => { const c = document.querySelector('.br-canvas'); c.dispatchEvent(new PointerEvent('pointerdown', { pointerId: 77, pointerType: 'touch', button: 0, isPrimary: true, clientX: 100, clientY: 400, bubbles: true })); return true; })()`);
  const points = await pointsFor('slot:rose');
  const before = await openCount('slot');
  await fingerTap(points.centre.x, points.centre.y);
  ok((await until(entered('slot'), 2500)) && (await until(opened('slot', before), 12000)), 'stale: a tap after a pointer whose up never came still enters');
  ok(await leave(), 'stale: Back returns to walking');
}

/* ---------------------------------------------------------------- 4. a desk: the mouse, every fixture */
await cdp('Emulation.setTouchEmulationEnabled', { enabled: false });
await cdp('Emulation.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36' });
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
await boot('desk');
await sweep('desk', mouseClick, ROWS, ['centre', 'left', 'top', 'halo']);

ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
await writeFile(join(OUT, 'tap-anywhere-check.json'), JSON.stringify({ ...report, passes, fails }, null, 2));
console.log(fails ? `\n${fails} FAILED, ${passes} passed` : `\nall ${passes} tap-anywhere checks passed`);
await done(fails ? 1 : 0);
