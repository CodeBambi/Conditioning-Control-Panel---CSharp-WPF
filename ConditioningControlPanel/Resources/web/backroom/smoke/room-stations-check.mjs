/* ============================================================================
 * backroom/smoke/room-stations-check.mjs - every live station through the REAL room, one page, one host.
 *
 *   node backroom/smoke/room-stations-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * What it proves that the per-station checks cannot: the room reads stations.json and sets out every fixture,
 * the fake host answers init with the stations the C# host whitelists (BackRoomApi.Ops keys: slot, wheel,
 * cards, roulette), and each of the four stations mounts from its stations.json entry through the loader,
 * opens (station-open, hostBack), plays one moment against its own mock-server.js, and closes on the room's
 * Back (station-close, the loader empty, the room loop running again, no tunnel left above 0). The slot here
 * is the REAL slot station, not smoke/mock-station.js.
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROOM_STATIONS_PORT, default 8931, debug
 * +500). The only process it stops is the Chrome it started, by its own handle.
 * CHROME: CHROME_PATH, else the usual Windows install.
 * ==========================================================================*/

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
const PORT = Number(process.env.ROOM_STATIONS_PORT || 8931), DEBUG_PORT = PORT + 500;

const REGISTRY = JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8'));
const LIVE = REGISTRY.filter((s) => s.state === 'live');

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

// One fake host for the whole room: a mock server per station, created on its first request.
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const gates = { flash: true, subliminal: true, spiral: true, brainDrain: true };
  const made = {};
  const mock = (id) => made[id] || (made[id] = import('/backroom/stations/' + id + '/mock-server.js').then((m) => {
    const s = id === 'cards' ? m.createMockServer({ sp: 57, floorMs: 600 }) : id === 'roulette' ? m.createMockServer({ sp: 57, floorMs: 0 }) : m.createMockServer({ sp: 57 });
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
        lex: { br_back: 'Back', br_balance: 'SP' }, stations: ['slot', 'wheel', 'cards', 'roulette'], open: true });
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
const report = { stations: {}, registry: LIVE.map((s) => s.id + (s.variant ? ':' + s.variant : '')) };

/* ---------------------------------------------------------------- 1. the room reads stations.json */
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until(`document.documentElement.classList.contains('br-ready')`, 45000, 100), 'the room boots');
const rows = await ev(`window.__backroom.stations.map((s) => ({ key: s.key, id: s.id, state: s.state, entry: s.entry || null }))`);
report.rows = rows;
ok(rows.length === REGISTRY.length, `the room holds all ${REGISTRY.length} stations.json rows`);
for (const id of ['slot', 'wheel', 'cards', 'roulette']) {
  ok(rows.some((r) => r.id === id && r.state === 'live' && r.entry === `stations/${id}/station.js`), `${id} is live from stations.json with entry stations/${id}/station.js`);
}
ok(rows.some((r) => r.id === 'counter' && r.state === 'soon'), 'the counter stays soon');
ok(await ev(`window.__backroom.stations.every((s) => !!window.__backroom.scene.scene.getObjectByName('station_' + s.key))`), 'every row has its fixture set out in the scene');
await sleep(1500);
await shot('room-00-boot.png');

/* ---------------------------------------------------------------- 2. each station: E, open, a moment, Back */
const STATIONS = [
  { key: 'slot:rose', id: 'slot', root: '.slot-station', ready: `document.querySelector('.slot-station') && document.querySelector('.slot-station').dataset.phase === 'play'`,
    async moment() {
      ok(await clickSel('.slot-spin'), 'slot: Spin pressed');
      ok(await until(`window.__posted.some((m) => m.type === 'station-request' && m.station === 'slot' && m.op === 'tape')`, 8000), 'slot: the press buys a tape through the relay');
      await sleep(1200); await shot('slot-02-spinning.png');
      await sleep(3500); await shot('slot-03-landed.png');
    } },
  { key: 'wheel', id: 'wheel', root: '.wheel-station', ready: `document.querySelector('.wheel-station[data-phase="play"]')`,
    async moment() {
      ok(await clickSel('.wheel-spin'), 'wheel: Spin pressed');
      ok(await until(`window.__posted.some((m) => m.type === 'fx-tunnel' && m.station === 'wheel' && m.level > 0.3)`, 15000), 'wheel: the last turn posts fx-tunnel');
      await shot('wheel-02-last-turn.png');
      ok(await until(`window.__posted.some((m) => m.type === 'fx' && m.station === 'wheel')`, 15000), 'wheel: the landing posts its fx');
      await sleep(700); await shot('wheel-03-landed.png');
    } },
  { key: 'cards', id: 'cards', root: '.cards-station', ready: `document.querySelector('.cards-station') && document.querySelector('.cards-station').dataset.phase === 'play'`,
    async moment() {
      ok(await clickSel('.cards-deal'), 'cards: Deal pressed');
      ok(await until(`!!document.querySelector('.cards-move[data-move=stand]') && !document.querySelector('.cards-move[data-move=stand]').disabled`, 8000), 'cards: a decision is open');
      ok(await ev(`window.__posted.filter((m) => m.type === 'fx' && m.station === 'cards').length === 0`), 'cards: no fx while deciding');
      await shot('cards-02-decision.png');
      ok(await clickSel('.cards-move[data-move=stand]'), 'cards: Stand pressed');
      ok(await until(`window.__posted.some((m) => m.type === 'fx' && m.station === 'cards')`, 8000), 'cards: the settled win fires its fx');
      await sleep(600); await shot('cards-03-settled.png');
    } },
  { key: 'roulette', id: 'roulette', root: '.roul-station', ready: `!!document.querySelector('.roul-station[data-phase=bet]')`,
    async moment() {
      const at = await ev(`(() => { const c = document.querySelector('.roul-stage'); const r = c.getBoundingClientRect(); const w = c.clientWidth, h = c.clientHeight;
        const mw = w * 0.47, mh = h * 0.46, cell = Math.max(14, Math.min(mw / 13, mh / 5.4)), ox = w * 0.5 + (mw - cell * 13) / 2, oy = h * 0.16 + (mh - cell * 5.4) / 2;
        return { x: r.left + ox + 12 * cell + cell / 2, y: r.top + oy + cell / 2 }; })()`);
      await click(Math.round(at.x), Math.round(at.y));
      ok(await clickSel('.roul-spin'), 'roulette: a chip on 36 and Spin pressed');
      ok(await until(`window.__posted.some((m) => m.type === 'fx-tunnel' && m.station === 'roulette' && m.level > 0)`, 12000), 'roulette: the run posts fx-tunnel');
      await shot('roulette-02-run.png');
      ok(await until(`(document.querySelector('.roul-history') || {}).childElementCount >= 1`, 14000), 'roulette: the ball lands');
      await sleep(400); await shot('roulette-03-landed.png');
    } },
];

for (const st of STATIONS) {
  const r = { key: st.key };
  report.stations[st.key] = r;
  const i0 = await ev(`window.__posted.length`);
  await ev(`window.__backroom.scene.go(window.__backroom.stations.find((s) => s.key === ${JSON.stringify(st.key)}))`);
  await sleep(300);
  r.nearest = (await ev(`window.__backroom.scene.debug().nearest`));
  ok(r.nearest === st.key, `${st.key}: standing at its approach makes it nearest`);
  await key('KeyE'); await key('KeyE', 'keyUp');
  ok(await until(`!!document.querySelector(${JSON.stringify(st.root)})`, 10000), `${st.key}: E mounts the station from its stations.json entry`);
  ok(await until(st.ready, 20000, 100), `${st.key}: the station opens to its first playable phase`);
  const since = () => ev(`window.__posted.slice(${i0})`);
  let p = await since();
  ok(p.some((m) => m.type === 'station-open' && m.station === st.id), `${st.key}: station-open ${st.id} posted`);
  ok(p.some((m) => m.type === 'station-request' && m.station === st.id && m.op === 'state'), `${st.key}: GET state relayed as ${st.id}`);
  const cur = await ev(`window.__backroom.loader.current`);
  const dbg = await ev(`(() => { const d = window.__backroom.scene.debug(); return { held: d.held, running: d.running }; })()`);
  ok(cur && cur.id === st.id && cur.kind === 'live', `${st.key}: the loader holds ${st.id} as live`);
  ok(dbg.held && !dbg.running, `${st.key}: the room loop is held while it is open`);
  ok(await ev(`!!document.querySelector(${JSON.stringify(st.root)} + '[data-host-back]') || !!document.querySelector('.br-station [data-host-back]')`), `${st.key}: the room owns Back (ctx.hostBack)`);
  await sleep(400);
  await shot(`${st.id}-01-open.png`);
  await st.moment();
  await ev(`document.querySelector('#br-back').click()`);
  ok(await until(`!document.querySelector(${JSON.stringify(st.root)}) && !window.__backroom.loader.current`, 3000), `${st.key}: Back unmounts the station`);
  ok(await until(`(() => { const d = window.__backroom.scene.debug(); return d.running && !d.held; })()`, 2000), `${st.key}: the room loop runs again`);
  p = await since();
  ok(p.some((m) => m.type === 'station-close' && m.station === st.id), `${st.key}: station-close ${st.id} posted`);
  ok(!p.some((m) => m.type === 'exit'), `${st.key}: the room itself stays open`);
  const tun = p.filter((m) => m.type === 'fx-tunnel' && m.station === st.id);
  r.tunnelPosts = tun.length;
  ok(tun.length === 0 || tun.at(-1).level === 0, `${st.key}: no tunnel left above 0 after Back (${tun.length} posts)`);
  r.fx = p.filter((m) => m.type === 'fx').map((m) => m.fxId);
  r.media = p.filter((m) => m.type === 'media-request').map((m) => m.count);
  r.ops = p.filter((m) => m.type === 'station-request').map((m) => m.op);
  await sleep(300);
  await shot(`${st.id}-04-after-back.png`);
}

/* ---------------------------------------------------------------- 3. Escape is Back too, then the room view */
await ev(`window.__backroom.scene.go(window.__backroom.stations.find((s) => s.key === 'roulette'))`);
await sleep(250);
await key('KeyE'); await key('KeyE', 'keyUp');
ok(await until(`!!document.querySelector('.roul-station')`, 10000), 'roulette reopens');
await cdp('Input.dispatchKeyEvent', { type: 'keyDown', code: 'Escape', key: 'Escape', windowsVirtualKeyCode: 27 });
await cdp('Input.dispatchKeyEvent', { type: 'keyUp', code: 'Escape', key: 'Escape', windowsVirtualKeyCode: 27 });
ok(await until(`!document.querySelector('.roul-station') && !window.__backroom.loader.current`, 3000), 'Escape closes the open station');
ok((await posted('exit')).length === 0, 'and only the station, the room stays');

report.errors = errs;
await writeFile(join(OUT, 'room-stations-check.json'), JSON.stringify(report, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall room station checks passed');
await done(fails ? 1 : 0);
