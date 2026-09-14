/* ============================================================================
 * wheel-check.mjs - the wheel station in headless Chrome, driven over CDP, on dev.html, mock-server.js and the
 * hypno kit's mock host.
 *
 *   node backroom/stations/wheel/tests/wheel-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/ is served on 127.0.0.1 and the mocks answer every request. The only
 * process this stops is the Chrome it started, by its own handle. Evidence: a still of every v3 key moment
 * (CONTRACT 10.13.F: Loom hub, taffy and moire, the long last turn with the tunnel, the quiet room, the landing wash,
 * the big win GIF, the jackpot spiral with its picture), a gated-off run, a Calm run, the base lane's strips, the
 * on-screen hub handedness and wheel-check.json. The fullscreen set is drawn by tests/host-screen.js from the calls
 * the mock host recorded; the asserts read the calls. CHROME: CHROME_PATH, else the usual Windows install.
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
const hyp = async () => (await dbg()).feel.scene.hypno;
async function shot(format = 'jpeg') { return (await cdp('Page.captureScreenshot', format === 'png' ? { format: 'png' } : { format: 'jpeg', quality: 82 })).result.data; }
const shots = [];
async function still(name, why) { await writeFile(join(OUT, name), Buffer.from(await shot(), 'base64')); shots.push({ name, why }); console.log('  shot  ' + name); }
async function until(expr, ms = 15000, step = 40) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
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
  shots.push({ name, why: 'strip' });
  console.log('  strip ' + name);
}
async function boot(query) {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/web/backroom/stations/wheel/dev.html${query}` });
  for (let i = 0; i < 100 && !(await ev('!!(window.dev && window.dev.station)')); i++) await sleep(100);
  await ev('window.dev.open()');
  for (let i = 0; i < 100 && (await ev("window.dev.station.debug().phase")) !== 'play'; i++) await sleep(100);
}
const summary = { shots };
const pressAndMeasure = () => ev(`(async () => { const t0 = performance.now(); document.querySelector('.wheel-spin').click();
  await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  const d = window.dev.station.debug(); return { ms: performance.now() - t0, coasting: d.feel.scene.coasting, busy: d.busy, answer: d.feel.log.find(x => x.what === 'answer') }; })()`);
const fxCalls = () => ev('window.dev.host.fx.map(r => ({ fxId: r.fxId, symbols: r.symbols, args: r.args, fired: r.ack.fired.length > 0 }))');
const tunnels = () => ev('window.dev.host.tunnel.map(r => r.level)');
const HEX = /^#[0-9a-fA-F]{6}$/;
const boxOk = from => !!from && from.w === 60 && from.h === 44 && Number.isInteger(from.x) && Number.isInteger(from.y);
const waitLanded = (ms = 16000) => until('!window.dev.station.debug().busy', ms, 20);

/** The hub disc measured ON SCREEN (law 3): two lossless screenshots, ring shifts around the projected hub centre.
 *  turn > 0 = the pattern moved clockwise; rim > 0 = its arms lead at the rim, so a clockwise turn reads inward. */
async function hubHandedness(gapMs) {
  const A = await shot('png'); await sleep(gapMs); const B = await shot('png');
  const hub = (await hyp()).hubScreen;
  return ev(`(async () => {
    const { ring, shift } = await import('/web/backroom/shared/hypno/tests/handedness.js');
    const hub = ${JSON.stringify(hub)}, S = Math.floor(hub.r * 2) - 4;
    const grab = async b64 => { const im = new Image(); im.src = 'data:image/png;base64,' + b64; await im.decode();
      const c = Object.assign(document.createElement('canvas'), { width: S, height: S }), g = c.getContext('2d', { willReadFrequently: true });
      g.drawImage(im, hub.x - S / 2, hub.y - S / 2, S, S, 0, 0, S, S); return g.getImageData(0, 0, S, S).data; };
    const a = await grab(${JSON.stringify(A)}), b = await grab(${JSON.stringify(B)}), max = Math.floor(1440 / 3 / 2) - 1, deg = s => s / 1440 * 360;
    const rs = [0.35, 0.5, 0.65].map(f => Math.round(S / 2 * f));
    const turn = rs.map(r => deg(shift(ring(a, S, S, r), ring(b, S, S, r), max)));
    const rim = rs.map(r => deg(shift(ring(a, S, S, r), ring(a, S, S, r + 3), max)));
    return { hubPx: Math.round(hub.r), turn, rim };
  })()`);
}

// 1. Full intensity, a 5 SP win (Glow): Loom hub, taffy + moire, the long last turn, the landing wash, the quiet room.
await boot('?next=glow&sp=57&full');
let d = await dbg();
ok(d.phase === 'play' && d.state.spun === false && /ready/.test(d.status), 'opens ready, spin not taken');
ok(d.feel.scene.under !== null && d.readout.value === 57 && d.readout.kind === 'own', 'standalone: own SP chip on 57');
ok(d.feel.scene.hypno.hub === 'loom' && d.feel.scene.hypno.hubRadius > 0.1 && d.feel.scene.hypno.moire, `Loom hub disc (r ${d.feel.scene.hypno.hubRadius && d.feel.scene.hypno.hubRadius.toFixed(3)} from hub_lip), moire rim on at Full`);
await sleep(500);
ok(((await dbg()).hypno.kit || {}).draws > 5, 'the kit paints the hub every frame');
const handRest = await hubHandedness(400);
ok(handRest.turn.every(v => v > 0) && handRest.rim.every(v => v > 0), `hub on screen at rest: turns clockwise ${handRest.turn.map(v => v.toFixed(1))} deg, arms lead at the rim ${handRest.rim.map(v => v.toFixed(1))} deg`);
await still('st-wheel-01-idle-loom-hub.jpg', 'Loom hub at rest, moire rim (Full)');
await ev('window.dev.host.clear()');
let t0 = Date.now();
const ans = await pressAndMeasure();
ok(ans.coasting && ans.busy && ans.answer && ans.answer.ms < 100, `Law VIII: turning within ${Math.round(ans.ms)} ms of the press (answer logged at ${ans.answer && ans.answer.ms} ms)`);
await sleep(350);
d = await dbg();
ok(d.readout.server === 62 && d.readout.value === 57 && d.readout.owed === 5, 'Law I: the server says 62, the readout holds 57 until it lands');
ok(await until('(() => { const h = window.dev.station.debug().feel.scene.hypno; return h.ghosts && h.shear > 0.5; })()', 3000), 'taffy at speed: slices sheared, smear ghosts on');
const handSpin = await hubHandedness(60);
await still('st-wheel-02-full-taffy-moire.jpg', 'Taffy slices with smear ghosts and the moire rim at speed');
ok(handSpin.turn.every(v => v > 0), `hub on screen while the wheel turns clockwise: ${handSpin.turn.map(v => v.toFixed(1))} deg`);
ok(await until('(() => { const d = window.dev.station.debug(); return d.feel.scene.hypno.slowing && d.feel.scene.hypno.dim > 0.7; })()', 12000, 30), 'the long last turn starts under 1.6 rad/s');
d = await dbg();
ok(d.feel.scene.hypno.timeScale < 0.8 && d.hypno.edges > 0.3 && d.hypno.caption > 0.3 && d.hypno.turnPlayed, `slow motion x${d.feel.scene.hypno.timeScale.toFixed(2)}, stage edges ${d.hypno.edges}, "s l o w l y" ${d.hypno.caption}`);
ok(await ev("document.querySelector('.wheel-slowly').textContent === 's l o w l y'"), 'caption from br_wheel_slowly');
await sleep(250);
await still('st-wheel-03-last-turn-tunnel.jpg', 'The long last turn: stage edges, caption, host tunnel vision');
const tl = await tunnels();
ok(tl.some(v => v > 0.5) && (await ev("window.dev.station.debug().feel.log.filter(x => x.what === 'moment' && x.id === 'wheel.turn').length")) === 1, `wheel.turn played once, tunnel posted up to ${Math.max(...tl)}`);
ok(await waitLanded(), 'lands');
await sleep(90);
await still('st-wheel-04-land-flash-wash.jpg', 'Landing wash in the slice colour (5 SP)');
const spinMs = Date.now() - t0;
await sleep(650);
await still('st-wheel-05-quiet-room.jpg', 'Quiet room: others grey, the landed slice keeps its colour with a mint outline');
d = await dbg();
ok(d.feel.scene.landed === 'glow' && d.feel.scene.under === 'glow', `lands on result.sliceIndex (glow) after ${spinMs} ms`);
ok(d.feel.scene.hypno.quiet !== null && d.feel.scene.hypno.outline, 'quiet room ran with the mint outline');
let fx = await fxCalls();
ok(fx.length === 1 && fx[0].fxId === 'fx.wash' && fx[0].args.strength === 0.55 && HEX.test(fx[0].args.color) && !fx[0].symbols, `wheel.land.flash: ${JSON.stringify(fx)}`);
ok(d.hypno.lastMoment && d.hypno.lastMoment.id === 'wheel.land.flash', 'moment wheel.land.flash on the landing frame');
await sleep(2200);
const tl2 = await tunnels();
ok(tl2.at(-1) === 0, `tunnel back to 0 after the landing (${tl2.length} posts)`);
d = await dbg();
ok(d.readout.value === 62 && d.readout.owed === 0 && (await ev("document.querySelector('.wheel-sp').textContent")) === '62 SP', 'THE BANK landed: 62 SP');
ok(d.feel.cues.some(c => c.name === 'thud') && d.feel.cues.some(c => c.name === 'two') && d.feel.cues.some(c => c.name === 'bank-thud'), 'THE THUD, two notes, the bank thud');
const ticks = d.feel.cues.filter(c => c.name === 'tick');
const gaps = ticks.slice(1).map((c, i) => c.at - ticks[i].at);
ok(ticks.length >= 3 && gaps.every(g => g >= 1000 / 6 - 2) && Math.max(...ticks.map(c => c.semis)) <= 7, `CHIME LADDER: ${ticks.length} ticks, min gap ${Math.min(...gaps)} ms (6 Hz cap), top step ${Math.max(...ticks.map(c => c.semis))}`);
ok(d.feel.scene.face === 'hearts' && /Glow: \+5 SP/.test(d.status) && /Next spin in \d\d:\d\d:\d\d/.test(d.status), `status as text: "${d.status}"`);
ok(await ev("document.querySelector('.wheel-spin').disabled && /Come back/.test(document.querySelector('.wheel-spin').textContent)"), 'the spin button waits for tomorrow');
summary.win = { answerMs: ans.answer.ms, spinMs, ticks: ticks.length, minTickGapMs: Math.min(...gaps), handRest, handSpin, tunnelMax: Math.max(...tl) };

// Reopen after spinning (same page, same mock): the stored landing and the countdown, no party, no moment.
await ev('window.dev.stand()');
ok((await dbg()).hypno.kit === null, 'standing up frees the kit');
await ev('window.dev.host.clear()');
await ev('window.dev.open()');
for (let i = 0; i < 80 && (await ev("window.dev.station.debug().phase")) !== 'play'; i++) await sleep(100);
d = await dbg();
ok(d.state.spun && d.feel.scene.landed === 'glow' && d.feel.scene.under === 'glow' && (await fxCalls()).length === 0 && d.feel.scene.hypno.quiet === null, 'reopen: the day\'s landing, no party, no moment, no quiet room');
ok((await ev('window.dev.host.media.at(-1).count')) === 4, 'each sit-down deals count 4');
const c1 = await ev("document.querySelector('.wheel-spin small').textContent");
await sleep(2100);
const c2 = await ev("document.querySelector('.wheel-spin small').textContent");
ok(/^\d\d:\d\d:\d\d$/.test(c1) && c1 !== c2, `countdown ticks: ${c1} -> ${c2}`);

// 2. Back mid-spin skips to settled inside the 420 ms budget, and cancels the moments.
await boot('?next=deep&latency=60');
await ev("document.querySelector('.wheel-spin').click()");
await until('window.dev.station.debug().feel.scene.hypno.slowing', 12000, 30);
await sleep(300);
const closeMs = await ev('(async () => { const t = performance.now(); await window.dev.stand(); return performance.now() - t; })()');
ok(closeMs < 420 && (await ev("!document.querySelector('.wheel-station')")), `Back in the long last turn: closed in ${Math.round(closeMs)} ms`);
await sleep(200);
ok((await fxCalls()).length === 0 && (await tunnels()).at(-1) === 0, 'and skipped the landing (no fx), the tunnel posted 0');

// 3. Snooze (Normal): quiet, no fx; sleepy EMI, "+2 tomorrow", a muted thud and the yawn, THE SHIVER. No taffy or moire below Full.
await boot('?next=snooze');
await ev("document.querySelector('.wheel-spin').click()");
await sleep(600);
d = await dbg();
ok(!d.feel.scene.hypno.moire && !d.feel.scene.hypno.ghosts && d.feel.scene.hypno.shear === 0, 'Normal: no moire, no taffy');
await waitLanded();
await sleep(500);
await still('st-wheel-06-land-quiet-snooze.jpg', 'Snooze lands quiet: no fullscreen, quiet room, EMI sleepy');
d = await dbg();
ok(d.feel.scene.landed === 'snooze' && d.feel.scene.mood === 'sleepy' && d.pose === 'melt', 'Snooze: sleepy EMI');
ok(await ev("!document.querySelector('.wheel-zzz').hidden") && /Snooze\. \+2 SP tomorrow/.test(d.status), `"+2 tomorrow" as text: "${d.status}"`);
ok(d.feel.cues.some(c => c.name === 'thud-muted') && d.feel.cues.some(c => c.name === 'snooze'), 'a muted thud and a yawn, never silence');
ok(d.readout.value === 57 && (await fxCalls()).length === 0 && d.hypno.lastMoment.id === 'wheel.land.quiet' && d.feel.scene.hypno.quiet !== null, 'wheel.land.quiet: no pay, no fx, the quiet room');

// 4. A big win (Deep, 40 SP): the wash, then the GIF growing out of the slice.
await boot('?next=deep');
await ev('window.dev.host.clear()');
await ev("document.querySelector('.wheel-spin').click()");
ok(await waitLanded(), 'deep lands');
await sleep(60);
await still('st-wheel-07-land-gif-wash.jpg', 'Big win: the wash in the slice colour');
await sleep(380);
await still('st-wheel-08-land-gif-grow.jpg', 'Big win: the picture growing out of the slice');
await sleep(1100);
await still('st-wheel-09-land-gif-full.jpg', 'Big win: the picture fills the screen');
fx = await fxCalls();
const moment4 = (await dbg()).hypno.lastMoment;
ok(fx.length === 2 && fx[0].fxId === 'fx.wash' && fx[0].args.strength === 0.9 && HEX.test(fx[0].args.color)
   && fx[1].fxId === 'fx.gif_from' && fx[1].args.ms === 3400 && boxOk(fx[1].args.from) && /^g[0-3]$/.test(fx[1].symbols[0]),
   `wheel.land.gif: ${JSON.stringify(fx)}`);
ok(moment4.gif === fx[1].symbols[0], `the picture key is the deck's pick for the result (${moment4.gif})`);

// 5. The jackpot: a 12-day pot (550), the fullscreen Loom spiral with the picture inside, the brass wash, THE REVEAL.
await boot('?next=jackpot&pot=12');
await ev('window.dev.host.clear()');
await ev("document.querySelector('.wheel-spin').click()");
ok(await waitLanded(), 'jackpot lands');
await sleep(500);
await still('st-wheel-10-jackpot-spiral-in.jpg', 'Jackpot: the Loom spiral fading in, picture growing');
await sleep(1100);
await still('st-wheel-11-jackpot-spiral-picture.jpg', 'Jackpot: the fullscreen Loom spiral with the picture inside at 46%');
d = await dbg();
fx = await fxCalls();
ok(d.feel.scene.landed === 'jackpot' && d.pose === 'jackpot', 'jackpot: lands on the star');
ok(fx.map(f => f.fxId).join() === 'fx.loom_spiral,fx.gif_from,fx.wash'
   && fx[0].args.preset === 'screen' && fx[0].args.ms === 4200 && fx[0].args.alpha === 0.9
   && fx[1].args.ms === 4600 && fx[1].args.scale === 0.46 && boxOk(fx[1].args.from) && /^g\d$/.test(fx[1].symbols[0])
   && fx[2].args.color === '#e8c27a' && fx[2].args.strength === 1, `wheel.land.jackpot in order: ${JSON.stringify(fx)}`);
ok(d.feel.cues.some(c => c.name === 'reveal') && d.hypno.lastMoment.page.includes('reveal'), 'THE REVEAL cue and page');
await sleep(2400);
d = await dbg();
ok(d.readout.value === 607 && /JACKPOT! \+550 SP/.test(d.status), `57 + 550 = 607, status "${d.status}"`);

// 6. Reduced motion: no travel, the landing is simply there once the server answers; no tokens, no tunnel.
await boot('?next=deep&reduced&latency=200');
t0 = Date.now();
const r5 = await pressAndMeasure();
ok(!r5.coasting && r5.busy && (await ev("document.querySelector('.wheel-spin').classList.contains('is-ringing')")), 'reduced: the press answers with a ring, no spin');
await strip('strip-reduced.png', [0, 150, 400, 1200], t0);
d = await dbg();
ok(d.feel.scene.landed === 'deep' && !d.busy && d.readout.value === 97 && (await ev("document.querySelectorAll('.wheel-token').length")) === 0, 'reduced: settled on Deep, 97 SP, no tokens');
ok(!(await tunnels()).some(v => v > 0) && d.hypno.kit && d.hypno.kit.still, 'reduced: no tunnel, the hub held still');

// 7. Already spun today, opened fresh: the stored landing, countdown, the button waits.
await boot('?spun&next=twinkle');
t0 = Date.now();
await strip('strip-already-spun-reopen.png', [0, 1000], t0);
d = await dbg();
ok(d.state.spun && d.feel.scene.landed === 'twinkle' && /Next spin in/.test(d.status) && (await fxCalls()).length === 0, 'fresh open after spinning shows the landing and the countdown, fires nothing');

// 8. Drag both ways: the direction is kept, the landing is the server's, and the Loom hub still pulls inward (law 3).
summary.hubAfterDrag = {};
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
  // In the long last turn the wheel is slow enough to read on screenshots 200 ms apart: an anticlockwise fling must
  // still turn the hub clockwise with its arms leading at the rim (a hub read off -rotation.z ran backward here).
  const slow = await until('(() => { const h = window.dev.station.debug().feel.scene.hypno; return h.slowing && h.speed < 1.2 && h.speed > 0.2; })()', 15000, 20);
  const hand = slow ? await hubHandedness(200) : null;
  summary.hubAfterDrag[dir > 0 ? 'anticlockwise' : 'clockwise'] = hand;
  ok(slow && hand.turn.every(v => v > 0) && hand.rim.every(v => v > 0), `hub in the long last turn after a${dir > 0 ? 'n anticlockwise' : ' clockwise'} drag: turns clockwise ${hand ? hand.turn.map(v => v.toFixed(1)) : '-'} deg, arms lead at the rim ${hand ? hand.rim.map(v => v.toFixed(1)) : '-'} deg (reads inward)`);
  if (dir > 0 && hand) await still('st-wheel-17-anticlockwise-last-turn-hub.jpg', 'After an anticlockwise drag: the Loom hub still turns clockwise in the long last turn');
  await waitLanded(20000);
  d = await dbg();
  const ans7 = s0.feel.log.find(x => x.what === 'answer');
  ok(s0.feel.scene.coasting || s0.feel.scene.planning, `drag ${dir > 0 ? 'anticlockwise' : 'clockwise'}: a fling spins at once`);
  ok(Math.sign(r1 - r0) === dir && d.feel.scene.landed === next, `and keeps its direction (${(r1 - r0).toFixed(2)} rad) onto ${next}`);
  ok(ans7 && Math.sign(ans7.omega) === dir, 'the release speed carries the sign');
}

// 9. busy twice then the answer (HTTP 200 refusals on the wheel), and a closed door.
await boot('?next=glow');
await ev("window.dev.server.fail('spin', 'busy', 2)");
await ev("document.querySelector('.wheel-spin').click()");
await waitLanded(20000);
d = await dbg();
ok(d.feel.scene.landed === 'glow' && (await ev("window.dev.server.log.filter(l => l.op === 'spin').length")) === 3 && (await ev("new Set(window.dev.server.log.filter(l => l.op === 'spin').map(l => l.idem)).size")) === 1, 'busy x2 retries with the same idem, then lands');
await boot('?next=glow');
await ev("window.dev.server.setOpen(false)");
await ev("document.querySelector('.wheel-spin').click()");
for (let i = 0; i < 40 && (await dbg()).busy; i++) await sleep(100);
d = await dbg();
ok(await ev("!document.querySelector('.wheel-card').hidden && /closed/.test(document.querySelector('.wheel-card').textContent)") && d.feel.scene.landed === null && (await fxCalls()).length === 0, 'a closed door winds the wheel down and says so, nothing lit, no fx');

// 10. The day turns: spun at 23:59:57 UTC, the countdown hits zero, state refreshes, the spin is free again.
const nearMidnight = new Date(Math.ceil(Date.now() / 86400000) * 86400000 - 3000).toISOString();
await boot(`?spun&day=${nearMidnight}`);
ok((await dbg()).state.spun, 'spun just before midnight');
for (let i = 0; i < 80 && (await dbg()).state.spun; i++) await sleep(100);
d = await dbg();
ok(!d.state.spun && /ready/.test(d.status) && !(await ev("document.querySelector('.wheel-spin').disabled")), 'after the reset the wheel is free again');

// 11. With the room's ctx.spReadout hook: the chip shows the held-back value, then the landed one.
await boot('?next=shimmer&hook');
await ev("document.querySelector('.wheel-spin').click()");
await sleep(500);
const chipMid = await ev("document.querySelector('#fakechip b').textContent");
await waitLanded(20000);
await sleep(1200);
d = await dbg();
ok(d.readout.kind === 'hook' && chipMid === '57' && (await ev("document.querySelector('#fakechip b').textContent")) === '65' && (await ev("window.dev.sent.some(m => m.type === 'chip-thud')")), `spReadout hook: chip ${chipMid} while turning, 65 after the bank, chip thud`);

// 12. Gated off (flash, spiral, brainDrain all off): plain dress from the first frame, no fx, no tunnel; the in-station dim stays.
await boot('?gates=off&next=deep');
d = await dbg();
ok(d.feel.scene.hypno.hub === 'star' && d.hypno.dress.hub === 'star', 'spiral off: the hub is a brass star');
await still('st-wheel-12-gated-off-idle-star.jpg', 'Gated off: the brass star hub');
await ev("document.querySelector('.wheel-spin').click()");
ok(await until('(() => { const d = window.dev.station.debug(); return d.feel.scene.hypno.slowing && d.hypno.edges > 0.3; })()', 12000, 30), 'gated off: the long last turn still dims the stage edges');
await still('st-wheel-13-gated-off-last-turn.jpg', 'Gated off: stage edges and caption only, no tunnel');
await waitLanded();
await sleep(700);
await still('st-wheel-14-gated-off-landed.jpg', 'Gated off: 40 SP lands with the quiet room and text only');
d = await dbg();
ok((await fxCalls()).length === 0 && !(await tunnels()).some(v => v > 0) && d.hypno.lastMoment.id === 'wheel.land.gif' && d.feel.scene.hypno.quiet !== null, 'gated off: wheel.land.gif fires no host fx and no tunnel, the quiet room still runs');
ok(/Deep: \+40 SP/.test(d.status), `gated off: the result as text "${d.status}"`);
await ev("window.dev.host.settings({ gates: { flash: true, spiral: true, brainDrain: true } })");
await sleep(300);
ok((await hyp()).hub === 'loom', 'a live settings frame turns the Loom hub back on');
await ev("window.dev.host.settings({ gates: { spiral: false } })");
await sleep(200);
ok((await hyp()).hub === 'star', 'and off again, live');

// 13. Calm: the settled landing, Normal args sent (the host halves), the quiet room at half strength, the hub held.
await boot('?calm&next=jackpot&pot=12');
await ev('window.dev.host.clear()');
await ev("document.querySelector('.wheel-spin').click()");
ok(await waitLanded(6000), 'Calm: lands once the server answers');
await sleep(300);
await still('st-wheel-15-calm-jackpot.jpg', 'Calm: jackpot, spiral at half alpha and 60% duration on the host stand-in');
d = await dbg();
fx = await fxCalls();
ok(fx.length === 3 && fx[0].args.alpha === 0.9 && fx[2].args.strength === 1 && d.hypno.dress.k === 0.5 && d.hypno.kit && d.hypno.kit.still, 'Calm: the page sends Normal args (nobody halves twice), k 0.5, the Loom held still');
ok(!d.feel.scene.hypno.moire && !(await tunnels()).some(v => v > 0), 'Calm: no moire, no travel so no tunnel');
await boot('?calm&next=shimmer');
await ev("document.querySelector('.wheel-spin').click()");
await waitLanded(6000);
await sleep(700);
await still('st-wheel-16-calm-quiet-room.jpg', 'Calm: an 8 SP landing, the wash halved on the host stand-in, the quiet room at half strength');
d = await dbg();
ok(d.hypno.lastMoment.id === 'wheel.land.flash' && d.feel.scene.hypno.quiet !== null && (await fxCalls())[0].args.strength === 0.55, 'Calm: 8 SP is still wheel.land.flash with Normal args');

// 14. Suspend in the long last turn: moments cancelled (tunnel 0), kit and deck freed; resume re-deals.
await boot('?next=dreamy');
await ev("document.querySelector('.wheel-spin').click()");
await until('(() => { const d = window.dev.station.debug(); return d.feel.scene.hypno.slowing && d.feel.scene.hypno.dim > 0.5; })()', 12000, 30);
await sleep(300);
await ev('window.dev.station.suspend(true)');
await sleep(250);
d = await dbg();
ok((await tunnels()).at(-1) === 0 && d.hypno.kit === null && d.hypno.deck === null, 'suspend: tunnel 0, kit and deck freed');
const media0 = await ev('window.dev.host.media.length');
await ev('window.dev.station.suspend(false)');
await sleep(600);
ok((await ev('window.dev.host.media.length')) === media0 + 1 && (await dbg()).hypno.kit, 'resume: a fresh deal, the hub paints again');

// 15. A live settings frame lowers motion mid-visit (law 6): Full dress off at once, the next spin lands settled, halved.
await boot('?full&next=glow');
d = await dbg();
ok(d.hypno.dress.full && d.feel.scene.hypno.moire && !d.still, 'live motion: opens at Full with the moire rim');
await ev("window.dev.host.settings({ reduced: true, motion: 'reduced' })");
await sleep(250);
d = await dbg();
ok(d.still && !d.hypno.dress.full && d.hypno.dress.calm && d.hypno.dress.k === 0.5 && !d.feel.scene.hypno.moire && d.hypno.kit && d.hypno.kit.still,
  'live motion: a reduced settings frame drops Full, k 0.5, no moire, the hub held still, without a reopen');
await ev('window.dev.host.clear()');
await ev("document.querySelector('.wheel-spin').click()");
await sleep(120);
d = await dbg();
ok(!d.feel.scene.coasting, 'live motion: the next press does not travel (the scene took the flag)');
await waitLanded(6000);
await sleep(300);
d = await dbg();
ok(d.feel.scene.landed === 'glow' && !d.feel.scene.hypno.ghosts && d.feel.scene.hypno.shear === 0 && !(await tunnels()).some(v => v > 0) && d.feel.scene.hypno.quiet !== null,
  'live motion: settled on Glow, no taffy, no tunnel, the quiet room at k 0.5');
await still('st-wheel-18-live-reduced-landed.jpg', 'A reduced settings frame mid-visit: Full texture gone, settled landing');
await ev("window.dev.host.settings({ reduced: false, motion: 'full' })");
await sleep(250);
d = await dbg();
ok(!d.still && d.hypno.dress.full && d.feel.scene.hypno.moire && d.hypno.kit && !d.hypno.kit.still, 'live motion: back to full motion, Full dress again, the hub turns');

// 13. MUST HIT (CONTRACT 10.16.E, C2): the pot at the line reads MUST HIT in place of the odds, and nothing else moves.
{
  const chip = () => ev("document.querySelector('.wheel-jackpot').textContent");
  const gold = () => ev("document.querySelector('.wheel-jackpot').classList.contains('is-must-hit')");
  await boot('?musthit');
  d = await dbg();
  ok(d.state.jackpot.mustHit === true && d.state.jackpot.amount === 1000, 'the mock parks the pot at the must-hit line, 1,000 SP');
  ok(await chip() === 'Jackpot 1,000 SP, MUST HIT', `the jackpot chip: "${await chip()}"`);
  ok(await gold(), 'and takes the jackpot gold, with no new fx and no new sound');
  ok((await fxCalls()).length === 0, 'nothing fired just for the sign');
  await ev("document.querySelector('.wheel-odds summary').click()");
  await sleep(150);
  ok(await ev("/has to fall today/.test(document.querySelector('.wheel-odds p').textContent)"), 'the Odds panel says the pot has to fall today');
  await still('st-wheel-19-must-hit.jpg', 'MUST HIT: the pot at the line, in place of the odds');
  await ev("document.querySelector('.wheel-odds summary').click()");
  await ev("document.querySelector('.wheel-spin').click()");
  await waitLanded(20000);
  await sleep(600);
  d = await dbg();
  ok(d.feel.scene.landed === 'jackpot' && d.state.jackpot.mustHit === false, 'an eligible spinner takes it, and nothing must fall any more');
  ok(await chip() === 'Jackpot 250 SP, 1 in 6,644' && !(await gold()), `the chip goes back to the odds at the re-seeded pot: "${await chip()}"`);
  // A young account still sees the room fact; its own spin is never forced (10.16.E).
  await boot('?musthit&young&next=glow');
  d = await dbg();
  ok(d.state.jackpot.mustHit === true && d.state.jackpot.eligible === false, 'a young account sees MUST HIT too: it is a room fact');
  ok(await chip() === 'Jackpot 1,000 SP, MUST HIT', 'the chip says so');
  ok(await ev("/accounts a few days old/.test(document.querySelector('.wheel-odds p').textContent)"), 'and the Odds panel still says the jackpot is not open to it');
}

await writeFile(join(OUT, 'wheel-check.json'), JSON.stringify(summary, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall wheel checks passed');
await done(fails ? 1 : 0);
