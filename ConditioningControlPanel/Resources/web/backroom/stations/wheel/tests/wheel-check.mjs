/* ============================================================================
 * wheel-check.mjs - the wheel station in headless Chrome, driven over CDP, on dev.html and mock-server.js.
 *
 *   node backroom/stations/wheel/tests/wheel-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/ is served on 127.0.0.1 and the mock answers every request. The only
 * process this stops is the Chrome it started, by its own handle. Evidence: frame strips (a normal win, Snooze,
 * the jackpot, reduced motion, an already-spun reopen) and wheel-check.json.
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
const sleep = ms => new Promise(r => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RES = resolve(HERE, '../../../../..');   // ConditioningControlPanel/Resources
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.WHEEL_PORT || 8897), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(RES, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise(r => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-wheel-'));
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
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });

const dbg = () => ev('window.dev.station.debug()');
async function shot() { return (await cdp('Page.captureScreenshot', { format: 'jpeg', quality: 80 })).result.data; }
/** Frames at the given ms after `t0`, then one strip image (composited in the page itself). */
async function strip(name, times, t0) {
  const frames = [];
  for (const at of times) { const w = at - (Date.now() - t0); if (w > 0) await sleep(w); frames.push({ at: Date.now() - t0, data: await shot() }); }
  const png = await ev(`(async () => { const f = ${JSON.stringify(frames)}; const W = 426, H = 240, c = document.createElement('canvas');
    c.width = W * Math.min(4, f.length); c.height = H * Math.ceil(f.length / 4); const g = c.getContext('2d'); g.fillStyle = '#000'; g.fillRect(0, 0, c.width, c.height);
    for (let i = 0; i < f.length; i++) { const im = new Image(); im.src = 'data:image/jpeg;base64,' + f[i].data; await im.decode();
      const x = (i % 4) * W, y = Math.floor(i / 4) * H; g.drawImage(im, x, y, W, H); g.fillStyle = '#000a'; g.fillRect(x, y, 86, 20);
      g.fillStyle = '#fff'; g.font = '13px Segoe UI'; g.fillText('+' + f[i].at + ' ms', x + 6, y + 15); }
    return c.toDataURL('image/png').slice(22); })()`);
  await writeFile(join(OUT, name), Buffer.from(png, 'base64'));
  console.log('  strip ' + name);
}
async function boot(query) {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/web/backroom/stations/wheel/dev.html${query}` });
  for (let i = 0; i < 100 && !(await ev('!!(window.dev && window.dev.station)')); i++) await sleep(100);
  await ev('window.dev.open()');
  for (let i = 0; i < 100 && (await ev("window.dev.station.debug().phase")) !== 'play'; i++) await sleep(100);
}
const summary = {};
const pressAndMeasure = () => ev(`(async () => { const t0 = performance.now(); document.querySelector('.wheel-spin').click();
  await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  const d = window.dev.station.debug(); return { ms: performance.now() - t0, coasting: d.feel.scene.coasting, busy: d.busy, answer: d.feel.log.find(x => x.what === 'answer') }; })()`);

// 1. A normal win: Glow 5 SP. Answer inside 100 ms, Law I readout, lands on the slice, THE BANK ticks up.
await boot('?next=glow&sp=57');
let d = await dbg();
ok(d.phase === 'play' && d.state.spun === false && /ready/.test(d.status), 'opens ready, spin not taken');
ok(d.feel.scene.under !== null && d.readout.value === 57 && d.readout.kind === 'own', 'standalone: own SP chip on 57');
let t0 = Date.now();
const pressed = pressAndMeasure();
const stripWin = strip('strip-win-glow.png', [0, 300, 900, 1800, 2800, 3800, 4700, 5600], t0);
const ans = await pressed;
ok(ans.coasting && ans.busy && ans.answer && ans.answer.ms < 100, `Law VIII: turning within ${Math.round(ans.ms)} ms of the press (answer logged at ${ans.answer && ans.answer.ms} ms)`);
await sleep(400);
d = await dbg();
ok(d.readout.server === 62 && d.readout.value === 57 && d.readout.owed === 5, 'Law I: the server says 62, the readout holds 57 until it lands');
await stripWin;
for (let i = 0; i < 60 && (await dbg()).busy; i++) await sleep(100);
await sleep(1200);
d = await dbg();
ok(d.feel.scene.landed === 'glow' && d.feel.scene.under === 'glow', 'lands on result.sliceIndex (glow)');
ok(d.readout.value === 62 && d.readout.owed === 0 && (await ev("document.querySelector('.wheel-sp').textContent")) === '62 SP', 'THE BANK landed: 62 SP');
ok(d.feel.log.some(x => x.what === 'fx' && x.fxId === 'fx.gif_burst') && (await ev("window.dev.sent.some(m => m.type === 'fx' && m.fxId === 'fx.gif_burst' && m.symbols && m.symbols.length === 4)")), 'fx.gif_burst fired with the dealt GIF keys');
ok(d.feel.cues.some(c => c.name === 'thud') && d.feel.cues.some(c => c.name === 'two') && d.feel.cues.some(c => c.name === 'bank-thud'), 'THE THUD, two notes, the bank thud');
const ticks = d.feel.cues.filter(c => c.name === 'tick');
const gaps = ticks.slice(1).map((c, i) => c.at - ticks[i].at);
ok(ticks.length >= 3 && gaps.every(g => g >= 1000 / 6 - 2) && Math.max(...ticks.map(c => c.semis)) <= 7, `CHIME LADDER: ${ticks.length} ticks, min gap ${Math.min(...gaps)} ms (6 Hz cap), top step ${Math.max(...ticks.map(c => c.semis))}`);
ok(d.feel.scene.face === 'hearts' && /Glow: \+5 SP/.test(d.status) && /Next spin in \d\d:\d\d:\d\d/.test(d.status), `status as text: "${d.status}"`);
ok(await ev("document.querySelector('.wheel-spin').disabled && /Come back/.test(document.querySelector('.wheel-spin').textContent)"), 'the spin button waits for tomorrow');
summary.win = { answerMs: ans.answer.ms, ticks: ticks.length, minTickGapMs: Math.min(...gaps) };

// Reopen after spinning (same page, same mock): the stored landing and the countdown, no second bank.
await ev('window.dev.stand()');
await ev('window.dev.open()');
for (let i = 0; i < 80 && (await ev("window.dev.station.debug().phase")) !== 'play'; i++) await sleep(100);
d = await dbg();
ok(d.state.spun && d.feel.scene.landed === 'glow' && d.feel.scene.under === 'glow' && !d.feel.log.some(x => x.what === 'fx'), 'reopen: the day\'s landing, no party');
const c1 = await ev("document.querySelector('.wheel-spin small').textContent");
await sleep(2100);   // the readout repaints once a second, so two samples 2.1 s apart always differ
const c2 = await ev("document.querySelector('.wheel-spin small').textContent");
ok(/^\d\d:\d\d:\d\d$/.test(c1) && c1 !== c2, `countdown ticks: ${c1} -> ${c2}`);

// 2. Back mid-spin skips to settled inside the 420 ms budget.
await boot('?next=deep&latency=60');
await ev("document.querySelector('.wheel-spin').click()");
await sleep(900);
const closeMs = await ev('(async () => { const t = performance.now(); await window.dev.stand(); return performance.now() - t; })()');
ok(closeMs < 420 && (await ev("!document.querySelector('.wheel-station')")), `Back mid-spin: closed in ${Math.round(closeMs)} ms`);
ok(await ev('window.dev.sent.filter(m => m.type === "fx").length === 0'), 'and skipped the ceremony (no fx after Back)');

// 3. Snooze: sleepy EMI, "+2 tomorrow", a muted thud and the yawn, THE SHIVER.
await boot('?next=snooze');
t0 = Date.now();
await ev("document.querySelector('.wheel-spin').click()");
await strip('strip-snooze.png', [0, 1200, 2600, 3900, 4700, 5100, 6000, 7400], t0);
d = await dbg();
ok(d.feel.scene.landed === 'snooze' && d.feel.scene.mood === 'sleepy' && d.pose === 'melt', 'Snooze: sleepy EMI');
ok(await ev("!document.querySelector('.wheel-zzz').hidden") && /Snooze\. \+2 SP tomorrow/.test(d.status), `"+2 tomorrow" as text: "${d.status}"`);
ok(d.feel.cues.some(c => c.name === 'thud-muted') && d.feel.cues.some(c => c.name === 'snooze'), 'a muted thud and a yawn, never silence');
ok(d.readout.value === 57 && !d.feel.log.some(x => x.what === 'fx'), 'no pay, no overlay');

// 4. The jackpot: a 12-day pot (550), THE REVEAL, fx.jackpot, gold.
await boot('?next=jackpot&pot=12');
t0 = Date.now();
await ev("document.querySelector('.wheel-spin').click()");
await strip('strip-jackpot.png', [0, 1500, 3600, 4700, 5000, 5300, 6200, 7600], t0);
d = await dbg();
ok(d.feel.scene.landed === 'jackpot' && d.pose === 'jackpot' && d.readout.value === 607, 'jackpot: lands on the star, 57 + 550 = 607');
ok(d.feel.log.some(x => x.what === 'fx' && x.fxId === 'fx.jackpot') && d.feel.cues.some(c => c.name === 'reveal'), 'fx.jackpot and THE REVEAL cue');
ok(/JACKPOT! \+550 SP/.test(d.status), `status "${d.status}"`);

// 5. Reduced motion: no travel, the landing is simply there once the server answers; no tokens.
await boot('?next=deep&reduced&latency=200');
t0 = Date.now();
const r5 = await pressAndMeasure();
ok(!r5.coasting && r5.busy && (await ev("document.querySelector('.wheel-spin').classList.contains('is-ringing')")), 'reduced: the press answers with a ring, no spin');
await strip('strip-reduced.png', [0, 150, 400, 1200], t0);
d = await dbg();
ok(d.feel.scene.landed === 'deep' && !d.busy && d.readout.value === 97 && (await ev("document.querySelectorAll('.wheel-token').length")) === 0, 'reduced: settled on Deep, 97 SP, no tokens');

// 6. Already spun today, opened fresh: the stored landing, countdown, the button waits.
await boot('?spun&next=twinkle');
t0 = Date.now();
await strip('strip-already-spun-reopen.png', [0, 1000], t0);
d = await dbg();
ok(d.state.spun && d.feel.scene.landed === 'twinkle' && /Next spin in/.test(d.status), 'fresh open after spinning shows the landing and the countdown');

// 7. Drag both ways: the direction is kept, the landing is the server's.
for (const [dir, next] of [[1, 'dazzle'], [-1, 'sip_b']]) {
  await boot(`?next=${next}`);
  const c = await ev("(() => { const r = document.querySelector('.wheel-stage').getBoundingClientRect(); return { x: r.width / 2, y: r.height / 2 }; })()");
  const cx = c.x, cy = c.y - 20, R = 150, pts = Array.from({ length: 9 }, (_, i) => { const a = Math.PI / 2 + dir * i * 0.12; return [cx + R * Math.cos(a), cy - R * Math.sin(a)]; });
  await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x: pts[0][0], y: pts[0][1], button: 'left', buttons: 1, clickCount: 1 });
  for (const [x, y] of pts.slice(1)) { await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'left', buttons: 1 }); await sleep(16); }
  await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x: pts.at(-1)[0], y: pts.at(-1)[1], button: 'left', clickCount: 1 });
  await sleep(60);
  const s0 = await dbg(), r0 = s0.feel.scene.rotation;
  await sleep(300);
  const r1 = (await dbg()).feel.scene.rotation;
  for (let i = 0; i < 80 && (await dbg()).busy; i++) await sleep(100);
  d = await dbg();
  const ans7 = s0.feel.log.find(x => x.what === 'answer');
  ok(s0.feel.scene.coasting || s0.feel.scene.planning, `drag ${dir > 0 ? 'anticlockwise' : 'clockwise'}: a fling spins at once`);
  ok(Math.sign(r1 - r0) === dir && d.feel.scene.landed === next, `and keeps its direction (${(r1 - r0).toFixed(2)} rad) onto ${next}`);
  ok(ans7 && Math.sign(ans7.omega) === dir, 'the release speed carries the sign');
}

// 8. busy twice then the answer (HTTP 200 refusals on the wheel), and a closed door.
await boot('?next=glow');
await ev("window.dev.server.fail('spin', 'busy', 2)");
await ev("document.querySelector('.wheel-spin').click()");
for (let i = 0; i < 100 && (await dbg()).busy; i++) await sleep(100);
d = await dbg();
ok(d.feel.scene.landed === 'glow' && (await ev("window.dev.server.log.filter(l => l.op === 'spin').length")) === 3 && (await ev("new Set(window.dev.server.log.filter(l => l.op === 'spin').map(l => l.idem)).size")) === 1, 'busy x2 retries with the same idem, then lands');
await boot('?next=glow');
await ev("window.dev.server.setOpen(false)");
await ev("document.querySelector('.wheel-spin').click()");
for (let i = 0; i < 40 && (await dbg()).busy; i++) await sleep(100);
d = await dbg();
ok(await ev("!document.querySelector('.wheel-card').hidden && /closed/.test(document.querySelector('.wheel-card').textContent)") && d.feel.scene.landed === null, 'a closed door winds the wheel down and says so, nothing lit');

// 9. The day turns: spun at 23:59:57 UTC, the countdown hits zero, state refreshes, the spin is free again.
const nearMidnight = new Date(Math.ceil(Date.now() / 86400000) * 86400000 - 3000).toISOString();
await boot(`?spun&day=${nearMidnight}`);
ok((await dbg()).state.spun, 'spun just before midnight');
for (let i = 0; i < 80 && (await dbg()).state.spun; i++) await sleep(100);
d = await dbg();
ok(!d.state.spun && /ready/.test(d.status) && !(await ev("document.querySelector('.wheel-spin').disabled")), 'after the reset the wheel is free again');

// 10. With the room's ctx.spReadout hook: the chip shows the held-back value, then the landed one.
await boot('?next=shimmer&hook');
await ev("document.querySelector('.wheel-spin').click()");
await sleep(500);
const chipMid = await ev("document.querySelector('#fakechip b').textContent");
for (let i = 0; i < 80 && (await dbg()).busy; i++) await sleep(100);
await sleep(1200);
d = await dbg();
ok(d.readout.kind === 'hook' && chipMid === '57' && (await ev("document.querySelector('#fakechip b').textContent")) === '65' && (await ev("window.dev.sent.some(m => m.type === 'chip-thud')")), `spReadout hook: chip ${chipMid} while turning, 65 after the bank, chip thud`);

await writeFile(join(OUT, 'wheel-check.json'), JSON.stringify(summary, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall wheel checks passed');
await done(fails ? 1 : 0);
