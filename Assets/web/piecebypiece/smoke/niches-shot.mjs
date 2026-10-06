/* ============================================================================
 * smoke/niches-shot.mjs - the picture choice in a real browser (2026-09-30).
 *
 * Headless Edge over CDP (node's global WebSocket, no packages), a static
 * server on Resources/web, and a FAKE HOST planted before bridge.js runs: a
 * window.chrome.webview double that keeps the choice the way the desktop does
 * (PbpMediaChosen and friends) in the browser's localStorage, so a reload is a
 * relaunch. What it proves, at every size asked for:
 *   1. the first start (Play solo) opens the picker, and nothing is dealt yet
 *   2. a pick starts the game and sends ONE media-flavour frame, chosen: true
 *   3. after a reload the same start deals at once: no picker
 *   4. the menu's Pictures link opens the manager; a pill switched off and an
 *      r/ niche added land in ONE frame when it closes, and survive a reload
 *   5. Options > Pictures carries the same manager inline
 *   6. zero page errors throughout
 * and it saves niches-first / niches-first-picked / niches-manager /
 * niches-options screenshots per size.
 *
 *   node smoke/niches-shot.mjs [--out DIR] [--sizes 1920x1080,390x844] [--port 8841] [--cdp 9241]
 * Run it from PowerShell: the Bash sandbox refuses localhost.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdirSync, writeFileSync, readFileSync, existsSync, statSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 && argv[i + 1] ? argv[i + 1] : d; };
const here = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(here, '..', '..');
const out = arg('out', path.join(here, 'out', 'niches'));
const sizes = arg('sizes', '1920x1080,390x844').split(',').map((s) => s.split('x').map(Number));
const port = Number(arg('port', '8841'));
const cdpPort = Number(arg('cdp', '9241'));
// --shim <path>: plant the SITE's host (cclabs-web scripts/pbp-web-ext/web-shim.js) instead of the fake
// desktop, to prove the browser keeps the choice too. Scrolller is blocked either way: no real pictures.
const shimPath = arg('shim', '');
const STORE_KEY = shimPath ? 'pbp.web.media.v1' : '__pbpFakeHost';
const tag = shimPath ? '-web' : '';
const EDGE = process.env.PBP_EDGE || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
mkdirSync(out, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/* ---- static server ---- */
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg',
  '.woff2': 'font/woff2', '.svg': 'image/svg+xml', '.wasm': 'application/wasm' };
const server = createServer((req, res) => {
  const u = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  const f = path.join(webRoot, u);
  if (!f.startsWith(webRoot) || !existsSync(f) || statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': TYPES[path.extname(f).toLowerCase()] || 'application/octet-stream' });
  res.end(readFileSync(f));
}).listen(port, '127.0.0.1');

/* ---- the fake host: the desktop's rule, kept in localStorage so a reload is a relaunch ---- */
const FAKE_HOST = `(() => {
  const KEY = '__pbpFakeHost';
  const read = () => { try { return JSON.parse(localStorage.getItem(KEY) || 'null') || {}; } catch { return {}; } };
  const listeners = [];
  window.__posted = [];
  const emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const state = () => { const s = read(); const online = s.online !== false, flavour = s.flavour || '';
    return { type: 'pbp:media-state', flavour: (s.chosen && online) ? flavour : '', last: flavour, custom: s.custom || {}, online,
      appWide: false, chosen: !!s.chosen || !online || !!flavour, library: true, canOnline: true }; };
  const onlineFrame = () => { const s = read(); const live = s.chosen && s.online !== false && s.flavour;
    return { type: 'pbp:online-media', state: live ? 'ready' : 'off', subs: s.subs || [], share: 70, images: [], clips: [], have: live ? 24 : 0, want: live ? 36 : 0 }; };
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(t, fn) { if (t === 'message') listeners.push(fn); },
    removeEventListener() {},
    postMessage(m) {
      if (!m || typeof m.type !== 'string') return;
      if (m.type !== 'heartbeat') window.__posted.push(m);
      switch (m.type) {
        case 'ready':
          emit({ type: 'pbp:settings', videoHoldSec: 15, reducedMotion: false, whispers: [] });
          emit({ type: 'pbp:identity', unifiedId: '', displayName: 'tester', appVersion: 'shot', online: false, net: { serverBase: '', authToken: '', viaHost: true } });
          emit(state()); emit(onlineFrame()); break;
        case 'ping': emit({ type: 'pong' }); break;
        case 'pbp:media-request': emit({ type: 'pbp:media', images: [], gifs: [], videos: [] }); break;
        case 'pbp:media-flavour':
          localStorage.setItem(KEY, JSON.stringify({ flavour: m.flavour, custom: m.custom, subs: m.subs, online: m.online, chosen: true }));
          emit(state()); emit(onlineFrame()); break;
        case 'stake-limits': emit({ type: 'stake', op: 'limits', ok: false, enabled: false }); break;
        case 'pbp:net': emit({ type: 'pbp:net-result', id: m.id, status: 0, body: '' }); break;
        default: break;
      }
    },
  };
})();`;

// With the site's shim, record what the page posts: wrap its postMessage once it exists.
const WRAP_POST = `;(() => { window.__posted = window.__posted || []; const w = window.chrome && window.chrome.webview; if (!w) return;
  const post = w.postMessage.bind(w); w.postMessage = (m) => { if (m && m.type !== 'heartbeat') window.__posted.push(m); return post(m); }; })();`;

/* ---- CDP ---- */
const profile = path.join(tmpdir(), 'pbp-niches-' + Date.now());
const edge = spawn(EDGE, ['--headless=new', '--remote-debugging-port=' + cdpPort, '--user-data-dir=' + profile, '--window-size=1920,1080',
  '--hide-scrollbars', '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--use-gl=angle', '--use-angle=swiftshader',
  '--enable-unsafe-swiftshader'], { stdio: 'ignore' });

async function target() {
  for (let i = 0; i < 60; i++) {
    try { const r = await fetch(`http://127.0.0.1:${cdpPort}/json/new?about:blank`, { method: 'PUT' }); if (r.ok) return (await r.json()).webSocketDebuggerUrl; } catch { /* starting */ }
    await sleep(250);
  }
  throw new Error('headless Edge did not open a debugging port');
}

const problems = [];
let checks = 0;
const check = (cond, name) => { if (!cond) { problems.push('FAILED: ' + name); console.log('  x ' + name); } else { checks++; console.log('  ok ' + name); } };

async function main() {
  const ws = new WebSocket(await target());
  await new Promise((res, rej) => { ws.addEventListener('open', res, { once: true }); ws.addEventListener('error', rej, { once: true }); });
  let id = 0; const pending = new Map();
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result || m); pending.delete(m.id); return; }
    if (m.method === 'Runtime.exceptionThrown') problems.push('exception: ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') problems.push('console.error: ' + m.params.args.map((a) => a.description || a.value).join(' '));
  });
  const send = (method, params = {}) => new Promise((res) => { const n = ++id; pending.set(n, res); ws.send(JSON.stringify({ id: n, method, params })); });
  const js = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error('eval: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text) + '\n' + expression);
    return r.result?.value;
  };
  const until = async (expr, ms = 20000) => { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await js(expr)) return true; await sleep(150); } return false; };
  const shot = async (name) => { const r = await send('Page.captureScreenshot', { format: 'png' }); const f = path.join(out, name); writeFileSync(f, Buffer.from(r.data, 'base64')); console.log('  shot ' + f); };

  await send('Runtime.enable');
  await send('Page.enable');
  await send('Network.enable');
  await send('Network.setBlockedURLs', { urls: ['*scrolller.com*', '*/api/pbp/*'] });
  const shimSource = shimPath ? readFileSync(shimPath, 'utf8') : '';
  await send('Page.addScriptToEvaluateOnNewDocument', { source: shimPath ? shimSource + WRAP_POST : FAKE_HOST });
  const url = `http://127.0.0.1:${port}/piecebypiece/index.html`;
  const load = async () => {
    await send('Page.navigate', { url });
    const up = await until(`!!(window.PBP && window.PBP.door && window.PBP.door.screen && window.PBP.door.screen() === 'menu' && window.PBP.pictures && window.PBP.pictures.available())`, 30000);
    check(up, 'the door opens on the menu and the host state landed');
    await sleep(900);
  };
  const posted = (type) => js(`window.__posted.filter((m) => m.type === ${JSON.stringify(type)})`);

  for (const [w, h] of sizes) {
    console.log(`\n${w}x${h}`);
    await send('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: w < 600 });
    await send('Page.navigate', { url: `http://127.0.0.1:${port}/piecebypiece/smoke/` }).catch(() => {});
    await sleep(300);
    await js(`(() => { try { localStorage.clear(); } catch {} return true; })()`);

    // 1. the first start asks
    await load();
    check(await js(`window.PBP.pictures.needsChoice()`), 'nothing saved: a start is owed the ask');
    await js(`document.querySelector('[data-act=solo]').click(), true`);
    check(await until(`!!document.querySelector('.pbp-np-modal.is-first')`, 5000), 'Play solo opens the picture card first');
    check(await js(`window.PBP.door.isUp()`), 'nothing is dealt behind it');
    await sleep(500);
    await shot(`niches-first${tag}-${w}x${h}.png`);
    await js(`document.querySelector('.np-tile[data-pick=pink]').click(), true`);
    await sleep(300);
    check(await js(`document.querySelectorAll('.np-first .np-pill').length >= 3`), 'a tile opens its niches as pills');
    await js(`(() => { const i = document.querySelector('.np-first .np-add input'); i.value = 'r/Bimbo_Lounge'; i.closest('form').requestSubmit(); return true; })()`);
    await sleep(200);
    check(await js(`[...document.querySelectorAll('.np-first .np-pill button')].some((b) => b.textContent === 'r/Bimbo_Lounge')`), 'the r/ field adds a pill');
    await js(`document.activeElement && document.activeElement.blur(), true`);
    await shot(`niches-first-picked${tag}-${w}x${h}.png`);

    // 2. the pick starts the game
    await js(`document.querySelector('.np-play').click(), true`);
    check(await until(`!document.querySelector('.pbp-np-modal') && !window.PBP.door.isUp()`, 8000), 'Play with Pink starts the game');
    const frames = await posted('pbp:media-flavour');
    check(frames.length === 1 && frames[0].chosen === true && frames[0].flavour === 'pink' && frames[0].subs.includes('Bimbo_Lounge'), 'one frame: chosen, pink, the added niche on');
    await sleep(800);

    // 3. a relaunch never asks again
    await load();
    check(!(await js(`window.PBP.pictures.needsChoice()`)), 'after a reload the choice is saved');
    await js(`document.querySelector('[data-act=solo]').click(), true`);
    await sleep(600);
    check(!(await js(`!!document.querySelector('.pbp-np-modal')`)), 'the second start does not ask');
    check(await until(`!window.PBP.door.isUp()`, 8000), 'the second start deals at once');

    // 4. the manager, from the menu
    await load();
    check(await js(`!!document.querySelector('[data-act=pictures]')`), 'the menu has a Pictures link');
    await js(`document.querySelector('[data-act=pictures]').click(), true`);
    check(await until(`!!document.querySelector('.pbp-np-modal:not(.is-first) .np-manage')`, 5000), 'Pictures opens the manager');
    await sleep(400);
    await js(`[...document.querySelectorAll('.pbp-np-modal .np-manage .np-pill button')].find((b) => b.textContent === 'r/Bimbos').click(), true`);
    await js(`(() => { const i = document.querySelector('.pbp-np-modal .np-manage .np-add input'); i.value = 'BimboHypnoPics'; i.closest('form').requestSubmit(); return true; })()`);
    await sleep(200);
    await js(`document.activeElement && document.activeElement.blur(), true`);
    await shot(`niches-manager${tag}-${w}x${h}.png`);
    const before = (await posted('pbp:media-flavour')).length;
    check(before === 0, 'the manager sends nothing while it is open');
    await js(`document.querySelector('.pbp-np-done').click(), true`);
    await sleep(300);
    const after = await posted('pbp:media-flavour');
    check(after.length === 1 && !after[0].subs.includes('Bimbos') && after[0].subs.includes('BimboHypnoPics'), 'Done sends ONE frame with both edits');
    await load();
    const st = await js(`window.PBP.pictures.state()`);
    check(st && st.custom.pink && st.custom.pink.off.includes('Bimbos') && st.custom.pink.added.includes('BimboHypnoPics') && st.flavour === 'pink', 'the manager edits survive a reload');

    // 5. Options > Pictures, inline, in a game
    await js(`document.querySelector('[data-act=hotseat]').click(), true`);
    await until(`!window.PBP.door.isUp()`, 8000);
    await sleep(800);
    await js(`document.getElementById('game-options').click(), true`);
    await sleep(400);
    check(await js(`!document.getElementById('game-pictures').hidden && !!document.querySelector('#game-pictures .np-manage')`), 'Options carries the same manager');
    await shot(`niches-options${tag}-${w}x${h}.png`);
    await js(`document.getElementById('game-options').click(), true`);

    // 6. Esc on the first-start card starts nothing
    await js(`localStorage.removeItem('${STORE_KEY}'), true`);
    await load();
    await js(`document.querySelector('[data-act=hotseat]').click(), true`);
    await until(`!!document.querySelector('.pbp-np-modal.is-first')`, 5000);
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
    await sleep(400);
    check(await js(`!document.querySelector('.pbp-np-modal') && window.PBP.door.screen() === 'menu'`), 'Esc closes the card and stays on the menu');
    check(!(await js(`window.__posted.some((m) => m.type === 'pbp:exit')`)), 'that Escape does not leave the board');
  }

  ws.close();
}

try { await main(); } catch (e) { problems.push('harness: ' + (e && e.stack || e)); }
edge.kill();
server.close();
try { rmSync(profile, { recursive: true, force: true }); } catch { /* disposable */ }
console.log(`\nniches-shot: ${checks} checks passed, ${problems.length} problems`);
for (const p of problems) console.log('  ' + p);
process.exit(problems.length ? 1 : 0);
