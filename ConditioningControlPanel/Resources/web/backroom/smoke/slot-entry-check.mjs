/* ============================================================================
 * backroom/smoke/slot-entry-check.mjs - the slot cabinet on a phone: the tap target, the pan-in, the sideways nudge.
 *
 *   node backroom/smoke/slot-entry-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * The phone desk run (2026-09-15) found three things at Candy Rose / Violet / Mint, and this check holds each:
 *   1. A tap anywhere on a cabinet (base, reels, marquee, topper, lever) enters it, portrait and landscape. The room's
 *      bulb auras are one scene-level Points cloud, and a Points raycast answers within a metre of every glow, so
 *      every tap but the base used to land on glow and on no station (fixtures.js: the cloud is not a surface).
 *   2. Entering shows no "Preparing the cabinet" card: the root stays see-through (loader.js roomBehind), the room
 *      camera pans in closer than the approach point, the cabinet rises over the room's held frame, and Back pans out
 *      to the exact walking pose. Motion off and reduced motion snap both ways.
 *   3. Portrait on a coarse pointer shows the sideways nudge once a session; landscape frames the reel window on the
 *      band between the side columns, clear of Spin, Freeze, Odds and the pills.
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (SLOT_ENTRY_PORT, default 8936, debug +500).
 * The only process it stops is the Chrome it started, by its own handle. CHROME: CHROME_PATH, else the usual install.
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
const WEB = resolve(HERE, '../..');   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.SLOT_ENTRY_PORT || 8936), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

// One fake host: the slot's own mock-server.js, the bell's mock, fallback art for the deal.
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const made = {};
  window.__mocks = made;   // the flow check below scripts a row on the slot's own mock (a promise per station id)
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
        .then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-slot-entry-'));
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
await cdp('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 2 });
await cdp('Emulation.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1' });
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const metrics = (width, height) => cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: true, screenOrientation: { type: width > height ? 'landscapePrimary' : 'portraitPrimary', angle: width > height ? 90 : 0 } });
const pt = (id, x, y) => ({ id, x, y, radiusX: 4, radiusY: 4, force: 1 });
const send = async (type, points) => { const r = await cdp('Input.dispatchTouchEvent', { type, touchPoints: points }); if (r.error) throw new Error(JSON.stringify(r.error)); };
const tap = async (x, y) => { await send('touchStart', [pt(9, x, y)]); await send('touchEnd', []); };
const state = () => ev('window.__backroom.scene.debug()');
const opens = () => ev(`window.__posted.filter((m) => m.type === 'station-open' && m.station === 'slot').length`);
const goTo = async (key) => { await ev(`window.__backroom.scene.go(window.__backroom.stations.find((s) => s.key === ${JSON.stringify(key)}))`); await until('!window.__backroom.scene.transitioning', 6000); await sleep(120); };
// Back only with a station open: an empty room's Back leaves the room (Law VI), which is not this check's business.
const leave = async () => { if (!(await ev('!!window.__backroom.loader.current || window.__backroom.scene.seated || window.__backroom.scene.transitioning'))) return; await ev('window.__backroom.back(); true'); ok(await until('!window.__backroom.loader.current && !window.__backroom.scene.transitioning && !window.__backroom.scene.seated && window.__backroom.scene.debug().running', 8000), 'Back returns to walking'); await sleep(150); };
const report = { taps: {}, entry: {}, landscape: {}, errors: errs };

/* Screen points over one cabinet at its approach pose: the projected centres of the room copy's named nodes (the
 * base is the holder box's bottom front) and the cabinet's right edge at reel height for the lever side. */
const POINTS = `(() => { const s = window.__backroom.scene, T = s.camera, key = KEY; const holder = s.scene.getObjectByName('station_' + key); if (!holder) return null;
  const w = innerWidth, h = innerHeight, out = {};
  const toScreen = (v) => { const p = v.clone().project(T); return { x: Math.round((p.x + 1) * w / 2), y: Math.round((1 - p.y) * h / 2) }; };
  const V = holder.position.constructor;
  const centre = (name) => { const n = holder.getObjectByName(name); if (!n) return null; n.updateWorldMatrix(true, false); const c = new V(); const pts = []; n.traverse((m) => { if (m.isMesh && m.geometry) { m.geometry.computeBoundingBox(); const bb = m.geometry.boundingBox; for (const x of [bb.min.x, bb.max.x]) for (const y of [bb.min.y, bb.max.y]) for (const z of [bb.min.z, bb.max.z]) pts.push(new V(x, y, z).applyMatrix4(m.matrixWorld)); } });
    if (!pts.length) return null; const mn = pts[0].clone(), mx = pts[0].clone(); for (const p of pts) { mn.min(p); mx.max(p); } return { min: mn, max: mx, mid: mn.clone().add(mx).multiplyScalar(.5) }; };
  const all = (() => { const pts = []; holder.traverse((m) => { if (m.isMesh && m.geometry && m.visible) { m.geometry.computeBoundingBox(); const bb = m.geometry.boundingBox; for (const x of [bb.min.x, bb.max.x]) for (const y of [bb.min.y, bb.max.y]) for (const z of [bb.min.z, bb.max.z]) pts.push(new V(x, y, z).applyMatrix4(m.matrixWorld)); } });
    const mn = pts[0].clone(), mx = pts[0].clone(); for (const p of pts) { mn.min(p); mx.max(p); } return { min: mn, max: mx }; })();
  const toward = T.position.clone().sub(holder.position).setY(0).normalize();   // the face of the cabinet the camera sees
  const front = holder.position.clone().addScaledVector(toward, (all.max.x - all.min.x) / 2 * .9);
  out.base = toScreen(new V(front.x, all.min.y + .18, front.z)); out.base.y = Math.min(out.base.y, h - 24);   // the foot is below a phone's frame at the approach: the lowest point on screen
  for (const [tag, name] of [['reels', 'reel_2'], ['marquee', 'marquee'], ['topper', 'EMI_glass']]) { const c = centre(name); out[tag] = c ? toScreen(c.mid) : null; }
  const reel = centre('reel_2'); const side = new V(-toward.z, 0, toward.x);   // the cabinet's right as seen from the camera
  if (reel) { const edge = reel.mid.clone(); const ext = Math.max(all.max.x - all.min.x, all.max.z - all.min.z) / 2; out.lever = toScreen(edge.addScaledVector(side, -ext * .93).addScaledVector(toward, .1)); out.lever.x = Math.min(out.lever.x, w - 16); }   // the lever side, or the cabinet's right edge when a phone frame cuts it
  return out; })()`;
const pointsFor = (key) => ev(POINTS.replace('KEY', JSON.stringify(key)));

/* ---------------------------------------------------------------- 1. the whole cabinet is the tap target */
for (const [width, height] of [[390, 844], [844, 390]]) {
  const label = width > height ? 'landscape' : 'portrait';
  await metrics(width, height);
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
  ok(await until(`document.documentElement.classList.contains('br-ready')`, 45000, 100), `${label}: the room boots`);
  await ev("try { sessionStorage.clear(); } catch (e) {}");
  await sleep(300);
  for (const key of ['slot:rose', 'slot:violet', 'slot:mint']) {
    await goTo(key);
    const points = await pointsFor(key);
    ok(points && ['base', 'reels', 'marquee', 'topper', 'lever'].every((k) => points[k] && points[k].x > 0 && points[k].x < width && points[k].y > 0 && points[k].y < height),
      `${label} ${key}: five points over the cabinet ` + JSON.stringify(points));
    if (!points) continue;
    report.taps[label + ' ' + key] = points;
    if (key === 'slot:rose') await shot(`${label}-00-approach-${key.split(':')[1]}.png`);
    for (const tag of ['base', 'reels', 'marquee', 'topper', 'lever']) {
      if (!points[tag]) continue;
      const before = await opens();
      await tap(points[tag].x, points[tag].y);
      const entered = await until('window.__backroom.scene.seated || window.__backroom.scene.transitioning', 2500);
      const opened = entered && await until(`window.__posted.filter((m) => m.type === 'station-open' && m.station === 'slot').length > ${before}`, 12000);
      ok(opened, `${label} ${key}: a tap on the ${tag} (${points[tag].x},${points[tag].y}) enters and posts station-open slot`);
      if (opened) { ok(await until(`document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`, 15000, 100), `${label} ${key}: the cabinet is playable after the ${tag} tap`); }
      if (key === 'slot:rose' && tag === 'reels') await shot(`${label}-01-entered-by-reels.png`);
      await leave();
      await goTo(key);
    }
  }
}

/* ---------------------------------------------------------------- 2. the pan-in is the load screen; no card, no cut */
await metrics(390, 844);
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until(`document.documentElement.classList.contains('br-ready')`, 45000, 100), 'entry: the room boots');
await ev("try { sessionStorage.clear(); } catch (e) {}");
await goTo('slot:rose');
const walking = await state();
const row = await ev("window.__backroom.stations.find((s) => s.key === 'slot:rose').fixture.position");
const dist = (p) => Math.hypot(p[0] - row[0], p[2] - row[2]);
await ev("window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'slot:rose')); true");
ok(await until('window.__backroom.scene.transitioning', 3000), 'entry: the camera starts travelling toward the cabinet');
let sawCard = false, sawGround = false, sawStation = false, samples = 0;
for (let i = 0; i < 80; i++) {
  const s = await ev(`(() => { const st = document.querySelector('.br-station'); const c = st && getComputedStyle(st).backgroundColor; return { card: !!document.querySelector('.slot-loading'), ground: !!st && c !== 'rgba(0, 0, 0, 0)', station: !!document.querySelector('.slot-station'), phase: document.querySelector('.slot-station')?.dataset.phase || null, moving: window.__backroom.scene.transitioning }; })()`);
  samples++; sawCard ||= s.card; sawGround ||= s.ground; sawStation ||= s.station;
  if (i === 6) await shot('entry-01-pan-in.png');
  if (s.station && !s.moving && i > 8 && !report.entry.firstStation) { report.entry.firstStation = i; await shot('entry-02-cabinet-rising.png'); }
  if (s.phase === 'play') break;
  await sleep(60);
}
ok(!sawCard, `entry: no "Preparing the cabinet" card at any of ${samples} samples`);
ok(sawStation && !sawGround, 'entry: the station root stays see-through over the room (br-through, no ground colour)');
ok(await ev("!!document.querySelector('.br-station.br-through')"), 'entry: the loader marked the slot root see-through');
ok(await until(`document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`, 15000, 100), 'entry: the cabinet is playable');
const seatedAt = await state();
report.entry.walking = { pos: walking.position, dist: dist(walking.position) };
report.entry.seated = { pos: seatedAt.position, dist: dist(seatedAt.position), held: seatedAt.held };
ok(dist(seatedAt.position) < dist(walking.position) - 0.4, `entry: the pan-in ends closer to the cabinet (${dist(seatedAt.position).toFixed(2)} m from ${dist(walking.position).toFixed(2)} m)`);
ok(seatedAt.held && !seatedAt.running, 'entry: the room holds its context once the cabinet is up (one drawing context)');
ok(await ev("getComputedStyle(document.querySelector('.br-nav')).visibility === 'hidden' && getComputedStyle(document.querySelector('#br-back')).visibility === 'visible'"), 'entry: the walking pills are hidden while the cabinet holds the screen, Back stays');
await sleep(500);
await shot('entry-03-playing.png');
await ev('window.__backroom.back(); true');
ok(await until('window.__backroom.scene.transitioning', 2500), 'exit: Back pans out');
await sleep(300); await shot('entry-04-pan-out.png');
ok(await until('!window.__backroom.scene.transitioning && !window.__backroom.loader.current', 8000), 'exit: the pan out finishes with the station gone');
const backAt = await state();
ok(backAt.position.every((v, i) => Math.abs(v - walking.position[i]) < .01) && Math.abs(backAt.yaw - walking.yaw) < .001, 'exit: the exact walking pose is back');
ok(await ev("!document.querySelector('.slot-station') && !document.querySelector('.br-station')"), 'exit: nothing of the station is left in the layer');

// Motion off and reduced motion: no travel either way, the cabinet is simply there.
await ev("window.__backroom.state.motion = 'off'");
await ev("window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'slot:rose')); true");
await sleep(120);
ok(!(await ev('window.__backroom.scene.transitioning')), 'motion off: the entry snaps, no travel');
ok(await until(`document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`, 15000, 100), 'motion off: the cabinet is playable');
ok(!(await ev("!!document.querySelector('.slot-loading')")), 'motion off: still no loading card');
await ev('window.__backroom.back(); true');
await sleep(120);
ok(!(await ev('window.__backroom.scene.transitioning')), 'motion off: Back snaps, no travel');
ok(await until('!window.__backroom.loader.current && window.__backroom.scene.debug().running', 6000), 'motion off: back to walking');
await ev("window.__backroom.state.motion = 'full'; window.__backroom.state.reduced = true");
await ev("window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'slot:rose')); true");
ok(await until(`document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`, 15000, 100), 'reduced: the cabinet is playable');
await shot('entry-05-reduced.png');
await ev('window.__backroom.back(); true');
ok(await until('!window.__backroom.loader.current && window.__backroom.scene.debug().running && !window.__backroom.scene.transitioning', 8000), 'reduced: back to walking');
await ev("window.__backroom.state.reduced = false");

/* ---------------------------------------------------------------- 3. the sideways nudge, and the landscape frame */
const nudge = () => ev("(() => { const n = document.querySelector('.slot-rotate'); return n ? { hidden: n.hidden, text: n.textContent.trim() } : null; })()");
const boxOf = (sel) => ev(`(() => { const n = document.querySelector(${JSON.stringify(sel)}); if (!n || n.hidden) return null; const b = n.getBoundingClientRect(); return b.width && b.height ? { left: b.left, top: b.top, right: b.right, bottom: b.bottom } : null; })()`);
const overlap = (a, b) => !!a && !!b && a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
await goTo('slot:rose');
await ev("window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'slot:rose')); true");
ok(await until(`document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`, 15000, 100), 'nudge: the cabinet is playable in portrait');
await sleep(200);
let n = await nudge();
ok(n && !n.hidden && n.text.replace(/^↻\s*/, '') === 'Turn your phone sideways for a bigger view', 'nudge: portrait on a coarse pointer shows the sideways hint ' + JSON.stringify(n));
const nudgeBox = await boxOf('.slot-rotate');
ok(!overlap(nudgeBox, await boxOf('.slot-controls')) && !overlap(nudgeBox, await boxOf('.slot-spin')) && !overlap(nudgeBox, await boxOf('.slot-top')), 'nudge: the pill overlaps neither the controls nor the pills');
await shot('nudge-01-portrait.png');
report.landscape.portrait = await ev('window.__backroom.loader.current.debug().feel.scene');
// Turn sideways: the nudge goes, the reels fill the band between the side columns.
await metrics(844, 390);
await ev("window.dispatchEvent(new Event('resize')); true");
await sleep(400);
n = await nudge();
ok(n && n.hidden, 'nudge: hidden in landscape');
const land = await ev('window.__backroom.loader.current.debug().feel.scene');
report.landscape.landscape = land;
ok(land && land.band && land.band.left === 110 && land.band.right === 12, 'landscape: the frame uses the side-column band ' + JSON.stringify(land && land.band));
const win = land && land.window;
ok(win && win.height >= 390 * 0.5 && win.height <= 390 - 24, `landscape: the reel window fills the height comfortably (${win && Math.round(win.height)} of 390 px)`);
ok(win && win.left >= 110 - 12 && win.left + win.width <= 844 - 12, `landscape: the reels sit inside the band (${win && Math.round(win.left)}..${win && Math.round(win.left + win.width)})`);
const wbox = win && { left: win.left, top: win.top, right: win.left + win.width, bottom: win.top + win.height };
for (const sel of ['.slot-spin', '.slot-controls .slot-freeze', '.slot-odds', '.slot-top', '.slot-face', '#br-back', '.br-sp']) {
  const b = await boxOf(sel);
  ok(b && !overlap(wbox, b), `landscape: ${sel} is clear of the reels ` + JSON.stringify(b && { l: Math.round(b.left), t: Math.round(b.top), r: Math.round(b.right), b: Math.round(b.bottom) }));
}
const jar = await boxOf('.slot-jar');
ok(!jar || (!overlap(jar, await boxOf('.slot-controls .slot-freeze')) && !overlap(jar, await boxOf('.slot-top'))), 'landscape: the spiral jar stands clear of the left column ' + JSON.stringify(jar && { l: Math.round(jar.left), t: Math.round(jar.top) }));
for (const sel of ['.slot-spin', '.slot-controls .slot-freeze', '.slot-odds', '.slot-top']) {
  const b = await boxOf(sel);
  ok(b && b.left >= 0 && b.right <= 844 && b.top >= 0 && b.bottom <= 390, `landscape: ${sel} is on screen`);
}
ok(!overlap(await boxOf('.slot-controls .slot-freeze'), await boxOf('.slot-spin')) && !overlap(await boxOf('.slot-controls .slot-freeze'), await boxOf('.slot-face')), 'landscape: Freeze, Spin and the face keep their own places');
await shot('landscape-01-playing.png');
// THE FLOW (shared/hypno/callout.js): a paid landing lights its glyphs, then the word and the host fx fire TOGETHER at
// 400 ms; the next row is a two-GIF `none`, the GIF tease: one fx.gif_burst { count: 1 }, no word, no SP.
ok(await ev(`(async () => { const s = await window.__mocks.slot; s.script(['sub0', 'gif1', 'sub2'], ['gif0', 'gif1', 'sub0']); return true; })()`), 'flow: an Echo row and a two-GIF tease row scripted on the mock');
const fxBefore = await ev(`window.__posted.filter((m) => m.type === 'fx').length`);
ok(await ev(`(() => { const b = document.querySelector('.slot-spin'); if (!b || b.disabled) return false; b.click(); return true; })()`), 'flow: Spin pressed');
ok(await until(`(() => { const d = window.__backroom.loader.current && window.__backroom.loader.current.debug(); return !!(d && d.flow && d.flow.line === 'sub2' && d.flow.fxAt !== null && d.callout && d.callout.shown.length); })()`, 15000, 50), 'flow: the Echo row lands, its word shows and its fx fires');
const flow = await ev(`(() => { const d = window.__backroom.loader.current.debug(); return { flow: d.flow, shown: d.callout.shown, fx: window.__posted.filter((m) => m.type === 'fx').slice(${fxBefore}) }; })()`);
report.flow = flow;
ok(flow && flow.shown.at(-1).key === 'br_callout_echo' && flow.shown.at(-1).tier === 'small', 'flow: debug().callout.shown ends on br_callout_echo (small)');
ok(flow && flow.flow.fxAt - flow.flow.landedAt >= 400 && flow.flow.fxAt - flow.flow.landedAt < 800, `flow: the fx fired ${flow && flow.flow.fxAt - flow.flow.landedAt} ms after the landing (>= 400)`);
ok(flow && Math.abs(flow.flow.calloutAt - flow.flow.fxAt) <= 50, `flow: the word and the fx share the frame (${flow && flow.flow.calloutAt - flow.flow.fxAt} ms apart)`);
ok(flow && flow.fx.some((m) => m.fxId === 'fx.sub_pair'), 'flow: the row\'s own fx.sub_pair went to the host');
ok(flow && flow.flow.hits.length === 2 && flow.flow.hits[0].reel === 0 && flow.flow.hits[1].reel === 2 && flow.flow.hits[1].at === 80, 'flow: the two sub glyphs are the hit, 80 ms apart');
ok(flow && flow.flow.unlockMs === 2000, 'flow: a paid line unlocks at landing + 2000 ms');
await shot('flow-01-echo.png');
const shownBefore = flow ? flow.shown.length : 0;
ok(await until(`(() => { const b = document.querySelector('.slot-spin'); if (!b || b.disabled) return false; b.click(); return true; })()`, 8000, 100), 'tease: Spin pressed again');
ok(await until(`(() => { const d = window.__backroom.loader.current && window.__backroom.loader.current.debug(); return !!(d && d.flow && d.flow.line === 'none' && d.flow.fxAt !== null); })()`, 15000, 50), 'tease: the two-GIF none row lands and its flash fires');
const tease = await ev(`(() => { const d = window.__backroom.loader.current.debug(); return { flow: d.flow, shown: d.callout.shown.length, fx: window.__posted.filter((m) => m.type === 'fx' && m.fxId === 'fx.gif_burst').at(-1) || null }; })()`);
report.tease = tease;
ok(tease && tease.fx && tease.fx.args && tease.fx.args.count === 1 && (tease.fx.symbols || []).length === 2, 'tease: one fx.gif_burst { count: 1 } with the two GIF keys ' + JSON.stringify(tease && tease.fx && { args: tease.fx.args, symbols: tease.fx.symbols }));
ok(tease && tease.shown === shownBefore, 'tease: no callout for a tease');
ok(tease && tease.flow.unlockMs === 0 && tease.flow.hits.length === 0, 'tease: a loss keeps the pace and lights no glyph');
// Spin still works sideways, and the reels stay put while the tape plays.
ok(await ev(`(() => { const b = document.querySelector('.slot-spin'); if (!b || b.disabled) return false; b.click(); return true; })()`), 'landscape: Spin pressed');
ok(await until(`window.__posted.some((m) => m.type === 'station-request' && m.station === 'slot' && m.op === 'tape')`, 8000), 'landscape: the press buys a tape');
await sleep(1500); await shot('landscape-02-spinning.png');
// Back to portrait: the nudge returns (not yet dismissed), the whole pill dismisses it for the session.
await metrics(390, 844);
await ev("window.dispatchEvent(new Event('resize')); true");
await sleep(300);
n = await nudge();
ok(n && !n.hidden, 'nudge: portrait again shows it until dismissed');
const nb = await boxOf('.slot-rotate');
await tap(Math.round((nb.left + nb.right) / 2), Math.round((nb.top + nb.bottom) / 2));
await sleep(150);
n = await nudge();
ok(n && n.hidden, 'nudge: a tap on the pill dismisses it');
ok(await ev("(() => { try { return sessionStorage.getItem('br_slot_rotate_seen') === '1'; } catch (e) { return false; } })()"), 'nudge: remembered for the session');
await ev('window.__backroom.back(); true');
ok(await until('!window.__backroom.loader.current && window.__backroom.scene.debug().running && !window.__backroom.scene.transitioning', 8000), 'nudge: back to walking');
await goTo('slot:violet');
await ev("window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'slot:violet')); true");
ok(await until(`document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`, 15000, 100), 'nudge: another cabinet opens');
n = await nudge();
ok(n && n.hidden, 'nudge: stays dismissed on the next cabinet this session');
await shot('nudge-02-dismissed-next-cabinet.png');
await ev('window.__backroom.back(); true');
await until('!window.__backroom.loader.current', 8000);
// A desk never sees it: no touch, a fine pointer.
await cdp('Emulation.setTouchEmulationEnabled', { enabled: false });
await cdp('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: false });
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until(`document.documentElement.classList.contains('br-ready')`, 45000, 100), 'desk: the room boots');
await ev("try { sessionStorage.clear(); } catch (e) {}");
await goTo('slot:rose');
await ev("window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'slot:rose')); true");
ok(await until(`document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`, 15000, 100), 'desk: the cabinet is playable');
n = await nudge();
ok(n && n.hidden, 'desk: a fine pointer in a tall window gets no sideways nudge');
await ev('window.__backroom.back(); true');
await until('!window.__backroom.loader.current', 8000);

ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
await writeFile(join(OUT, 'slot-entry-check.json'), JSON.stringify({ ...report, fails }, null, 2));
console.log(fails ? `\n${fails} FAILED` : '\nall slot entry checks passed');
await done(fails ? 1 : 0);
