/* ============================================================================
 * room-mount-check.mjs - Daily Daze v3 through the REAL room: backroom/index.html, room/loader.js and bridge.js,
 * with a fake host on chrome.webview (the wheel's mock-server.js answers the station requests).
 *
 *   node backroom/stations/wheel/tests/room-mount-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * What it proves that wheel-check.mjs (dev.html) cannot: E at the wheel mounts and opens the station through the
 * loader's ctx, the room owns Back and the SP chip (ctx.spReadout), init.gates and a live settings frame dress the
 * page, the moments leave the page as real bridge messages (media-request count 4, fx with args, fx-tunnel), and Back
 * closes it with the tunnel at 0. Port WHEEL_ROOM_PORT (default 8894, debug +500). The only process it stops is
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
const PORT = Number(process.env.WHEEL_ROOM_PORT || 8894), DEBUG_PORT = PORT + 500;
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
  const gates = { flash: true, subliminal: true, spiral: true, brainDrain: true };
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
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
const shot = async name => { const r = await cdp('Page.captureScreenshot', { format: 'jpeg', quality: 82 }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
const posted = type => ev(`window.__posted.filter((m) => m.type === ${JSON.stringify(type)})`);
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const key = async (code, type = 'keyDown') => cdp('Input.dispatchKeyEvent', { type, code, key: code.slice(3).toLowerCase(), windowsVirtualKeyCode: code.charCodeAt(3) });

await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until(`document.documentElement.classList.contains('br-ready')`, 30000, 100), 'the room boots');
await sleep(600);
await ev(`window.__backroom.scene.go(window.__backroom.stations.find((s) => s.key === 'wheel'))`);
await sleep(250);
await key('KeyE'); await key('KeyE', 'keyUp');
ok(await until(`document.querySelector('.br-station[data-station="wheel"] .wheel-station[data-phase="play"]')`, 20000), 'E at the wheel mounts and opens the station through the loader');
ok((await posted('station-open')).some(m => m.station === 'wheel'), 'station-open wheel posted');
ok((await posted('media-request')).some(m => m.station === 'wheel' && m.count === 4), 'the sit-down deal asks count 4');
ok(await ev(`document.querySelector('.wheel-station').hasAttribute('data-host-back') && document.querySelector('.wheel-back').hidden`), 'the room owns Back (ctx.hostBack)');
ok(await ev(`document.querySelector('.wheel-station').dataset.hub === 'loom'`), 'init.gates spiral on: the Loom hub');
await sleep(400);
await shot('room-01-wheel-open.jpg');
await ev(`window.__hostEmit({ type: 'settings', motion: 'full', intensity: 'normal', reduced: false, gates: { flash: true, subliminal: true, spiral: false, brainDrain: true } })`);
ok(await until(`document.querySelector('.wheel-station').dataset.hub === 'star'`, 3000), 'a live settings frame with spiral off: the brass star');
await ev(`window.__hostEmit({ type: 'settings', motion: 'full', intensity: 'normal', reduced: false, gates: { flash: true, subliminal: true, spiral: true, brainDrain: true } })`);
ok(await until(`document.querySelector('.wheel-station').dataset.hub === 'loom'`, 3000), 'and back on');
await ev(`document.querySelector('.wheel-spin').click()`);
ok(await until(`window.__posted.some((m) => m.type === 'fx-tunnel' && m.station === 'wheel' && m.level > 0.3)`, 15000), 'the long last turn posts fx-tunnel through the bridge');
await shot('room-02-wheel-last-turn.jpg');
ok(await until(`window.__posted.some((m) => m.type === 'fx' && m.fxId === 'fx.gif_from')`, 15000), 'the landing posts its fx');
const fx = (await posted('fx')).filter(m => m.station === 'wheel');
ok(fx.length === 2 && fx[0].fxId === 'fx.wash' && fx[0].args.strength === 0.9 && fx[1].args.ms === 3400 && fx[1].args.from.w === 60 && /^g[0-3]$/.test(fx[1].symbols[0]),
  `wheel.land.gif over the bridge: ${JSON.stringify(fx.map(m => ({ fxId: m.fxId, symbols: m.symbols, args: m.args })))}`);
await sleep(700);
await shot('room-03-wheel-landed-quiet.jpg');
ok(await until(`/Deep: \\+40 SP/.test(document.querySelector('.wheel-status').textContent)`, 3000), 'the result as text');
ok(await until(`/97/.test(document.querySelector('#br-sp-value') ? document.querySelector('#br-sp-value').textContent : '')`, 6000), 'the room SP chip lands on 97');
await ev(`document.querySelector('#br-back').click()`);
ok(await until(`!document.querySelector('.wheel-station')`, 2000), 'Back closes the wheel');
ok((await posted('station-close')).some(m => m.station === 'wheel'), 'station-close wheel posted');
const tun = (await posted('fx-tunnel')).filter(m => m.station === 'wheel');
ok(tun.length > 0 && tun.at(-1).level === 0, `the last fx-tunnel is 0 (${tun.length} posts)`);
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall wheel room checks passed');
await done(fails ? 1 : 0);
