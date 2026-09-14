/* ============================================================================
 * roulette-check.mjs - the Velvet Vortex station in headless Chrome, driven over CDP, on dev.html (the kit's mock
 * host and mock-server.js) and inside the real room page.
 *
 *   node backroom/stations/roulette/tests/roulette-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROULETTE_PORT, default 8899, debug +500) and the
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
const PORT = Number(process.env.ROULETTE_PORT || 8899), DEBUG_PORT = PORT + 500;
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
const dbg = () => ev('window.dev.station.debug()');
async function until(expr, ms = 15000, step = 50) { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
async function boot(query) {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/stations/roulette/dev.html${query}` });
  await until('!!(window.dev && window.dev.station)', 10000, 100);
  await ev('window.dev.open()');
  await until('window.dev.station.debug().phase === "bet"', 10000, 100);
}
async function click(x, y, button = 'left') {
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
  await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button, buttons: button === 'left' ? 1 : 2, clickCount: 1 });
  await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button, clickCount: 1 });
}
async function clickSpot(spot) {
  const r = (await dbg()).mat.rects[spot];
  await click(Math.round(r.x + r.w / 2), Math.round(r.y + r.h / 2));
}
const clickSel = (sel) => ev(`(() => { const b = document.querySelector(${JSON.stringify(sel)}); if (!b || b.disabled) return false; b.click(); return true; })()`);
const host = (expr) => ev(`(() => { const h = window.dev.host; return ${expr}; })()`);
const logOf = (what) => ev(`window.dev.station.debug().log.filter((x) => x.what === ${JSON.stringify(what)})`);
const report = {};

/* ------------------------------------------------ 1. the table at rest: drifting rim, the Lighthouse on its own clock */
await boot('?sp=40&next=17w,5,0');
let d = await dbg();
ok(d.phase === 'bet' && d.bowl && d.mat.cells === 42, `opens on the bet phase with a 42-spot mat (${d.mat && d.mat.cells})`);
ok(d.status && /Place your chips/.test(d.status) && d.spinDisabled, `ready text, Spin waits for chips: "${d.status}"`);
await sleep(600);
await shot('01-idle-table.png');
const lit = new Set();
const beam0 = (await dbg()).bowl;
for (let i = 0; i < 100; i++) { for (const n of (await dbg()).bowl.lit) lit.add(n); await sleep(100); if (i === 50) await shot('02-lighthouse-sweep.png'); }
const beam1 = (await dbg()).bowl;
report.lighthouse = { distinctLit: lit.size, beamTurned: beam1.beamAngle - beam0.beamAngle, rotorTurned: beam1.rot - beam0.rot };
ok(lit.size >= 30, `law 4: the beam lit ${lit.size} distinct numbers in 10 s`);
ok(beam1.beamAngle < beam0.beamAngle - 6 && beam1.rot > beam0.rot + 3, `the beam turns against the rotor on its own clock (beam ${report.lighthouse.beamTurned.toFixed(2)} rad, rotor +${report.lighthouse.rotorTurned.toFixed(2)} rad)`);
ok(beam1.ringBuilds === 1, `the drifting rim is printed once and cached (${beam1.ringBuilds} build)`);

/* ------------------------------------------------ 2. the mat: five bet kinds, the stake cap, a refused cover-all */
await clickSpot('s17'); await clickSpot('sink'); await clickSpot('plum');
d = await dbg();
ok(JSON.stringify(d.chips) === JSON.stringify({ s17: 1, sink: 1, plum: 1 }) && !d.spinDisabled, 'clicks on the mat place a straight, a row and a colour');
await clickSpot('deep');
d = await dbg();
ok(!('deep' in d.chips) && /3 SP a spin at most/.test(d.why), `a fourth chip is refused as text: "${d.why}"`);
await click(Math.round(d.mat.rects.plum.x + 20), Math.round(d.mat.rects.plum.y + 10), 'right');
d = await dbg();
ok(!('plum' in d.chips), 'a right click takes a chip off');
await clickSpot('rose');
await sleep(300);
await shot('03-mat-chips.png');
await ev('window.dev.station.dev.clearChips()');
await clickSpot('rose'); await clickSpot('plum');
d = await dbg();
ok(d.spinDisabled && d.check.why === 'covers_all' && /covers every number/.test(d.why), `rose + plum: Spin off with the server's reason (${d.check.why}): "${d.why}"`);
await sleep(200);
await shot('04-cover-all-refused.png');
await ev('window.dev.station.dev.clearChips()');
for (const s of ['sip', 'sink', 'deep']) await clickSpot(s);
d = await dbg();
ok(d.spinDisabled && d.check.why === 'covers_all', 'sip + sink + deep: refused client-side too');
await ev('window.dev.station.dev.clearChips()');
for (const s of ['rose', 'sip', 'sink']) await clickSpot(s);
ok(!(await dbg()).spinDisabled, 'a colour and two rows (30 of 36) may spin');
await ev('window.dev.station.dev.clearChips()');

/* ------------------------------------------------ 3. a 3-spin tape: wake straight (big), rose (win), zero (miss) */
for (const s of ['s17', 'sink', 'rose']) await clickSpot(s);
await clickSel('.roul-spins button[data-n="3"]');
d = await dbg();
ok(d.count === 3 && /3 SP x 3 = 9 SP/.test(d.spin), `three spins of 3 SP: "${d.spin}"`);
const pressed = await ev(`(async () => { const t0 = performance.now(); document.querySelector('.roul-spin').click();
  await new Promise((r) => requestAnimationFrame(() => r())); const d = window.dev.station.debug();
  return { ms: performance.now() - t0, rotVel: d.bowl.rotVel, phase: d.phase, answer: d.log.find((x) => x.what === 'answer') }; })()`);
ok(pressed.phase === 'asking' && pressed.rotVel > 1.4 && pressed.answer.ms < 100, `Law VIII: the rotor picks up within ${Math.round(pressed.ms)} ms, before the reply`);
await until('window.dev.station.debug().cur !== null', 5000);
const t1 = Date.now();
d = await dbg();
ok(d.shown === 40 - 9 && d.owed === d.tape.outcomes.reduce((s, o) => s + o.pay, 0), `Law I: the stake leaves at once, ${d.owed} SP owed until the spins land (shows ${d.shown})`);
ok(d.cur.wake && d.cur.page.includes('turret_whirl') && d.cur.page.includes('lighthouse') && d.cur.page.includes('fret_rattle') && !d.cur.page.includes('velvet_wake'),
  `roulette.run + roulette.wake page effects: ${d.cur.page.join(', ')}`);
let fx = await host('h.fx.map((f) => ({ id: f.fxId, args: f.args, fired: f.ack.fired.length > 0, token: f.token }))');
const wakeSpiral = fx.find((f) => f.id === 'fx.loom_spiral');
ok(wakeSpiral && wakeSpiral.args.preset === 'wake' && wakeSpiral.args.hold === true && wakeSpiral.args.alpha === 0.65 && wakeSpiral.fired, 'fx.loom_spiral {preset wake, hold, alpha 0.65} at the Spin frame');
ok(!fx.some((f) => f.id === 'fx.haze'), 'no haze below Full');
await sleep(900);
await shot('05-tunnel-run-spiral-wake-screen.png');
ok((await dbg()).bowl.whirlDrawn, 'the turret dish is the Loom whirl (kit.draw)');
await ev("document.getElementById('preview').style.visibility = 'hidden'");
await shot('06-spiral-wake-turret.png');
await ev("document.getElementById('preview').style.visibility = ''");
await until('window.dev.station.debug().bowl.phase === "rattle"', 8000, 30);
await sleep(350);
d = await dbg();
ok(d.bowl.tscale < 0.6 && d.bowl.planned.hits >= 1, `fret rattle in slow motion (time scale ${d.bowl.tscale.toFixed(2)}, ${d.bowl.planned.hits} clips planned)`);
await shot('07-fret-rattle-slowly.png');
await until('window.dev.station.debug().cur && window.dev.station.debug().cur.landed', 8000, 16);
const land1 = (await logOf('land'))[0];
await sleep(250);
await shot('08-land-big-wake-gif.png');
d = await dbg();
fx = await host('h.fx.map((f) => ({ id: f.fxId, args: f.args, symbols: f.symbols, fired: f.ack.fired.length > 0, token: f.token }))');
const rel = await host('h.release.map((r) => r.token)');
ok(land1.pocket === 17 && land1.moment === 'roulette.land.big' && land1.page.includes('pulled_pair'), `17 woken with a straight on it: ${land1.moment} (${land1.page.join(', ')})`);
ok(rel.includes(wakeSpiral.token), 'the landing released the wake spiral first');
const big = fx.slice(-2);
ok(big[0].id === 'fx.wash' && big[0].args.color === '#9b6bff' && big[0].args.strength === 1, `wash in the pocket colour at 1: ${JSON.stringify(big[0].args)}`);
ok(big[1].id === 'fx.gif_from' && big[1].args.ms === 3600 && big[1].args.from.w === 40 && big[1].args.from.h === 30 && /^g\d{1,2}$/.test(big[1].symbols[0]),
  `the double win GIF grows out of the pocket ${JSON.stringify(big[1].args.from)} with ${big[1].symbols[0]}`);
ok(/17 Plum, Sink row/.test(d.status) && /Spiral Wake/.test(d.status) && /won 78 SP/.test(d.status) && /Spin 1 of 3/.test(d.status), `result as text: "${d.status}"`);
ok(d.history.at(-1) === '17 Plum +78 ~' && d.owed === d.tape.outcomes.slice(1).reduce((s, o) => s + o.pay, 0), `history "${d.history.at(-1)}", Law I owes only the rest`);
const tun = await host('h.tunnel.map((t) => ({ at: t.at, level: t.level }))');
const gaps = tun.slice(1).map((t, i) => t.at - tun[i].at);
report.tunnel = { posts: tun.length, max: Math.max(...tun.map((t) => t.level)), minGapMs: Math.min(...gaps) };
ok(tun.length >= 5 && Math.max(...tun.map((t) => t.level)) <= 0.75 && Math.min(...gaps) >= 90 && tun.at(-1).level === 0,
  `tunnel run: ${tun.length} posts, max ${report.tunnel.max}, gaps >= ${report.tunnel.minGapMs} ms, 0 on landing`);
await sleep(1000);
await ev("document.getElementById('preview').style.visibility = 'hidden'");
await shot('09-pulled-pair-chips.png');
await ev("document.getElementById('preview').style.visibility = ''");
await until('window.dev.station.debug().log.filter((x) => x.what === "land").length === 2', 12000, 16);
await sleep(200);
await shot('10-land-win-wash.png');
const land2 = (await logOf('land'))[1];
fx = await host('h.fx.map((f) => ({ id: f.fxId, args: f.args }))');
ok(land2.pocket === 5 && land2.moment === 'roulette.land.win' && fx.at(-1).id === 'fx.wash' && fx.at(-1).args.strength === 0.6 && fx.at(-1).args.color === '#ff5fa2',
  `5 rose on the rose chip: roulette.land.win, a rose wash at 0.6`);
await until('window.dev.station.debug().log.filter((x) => x.what === "land").length === 3', 12000, 16);
const fxBefore3 = await host('h.fx.length');
await sleep(900);
await shot('11-land-miss-vortex.png');
const land3 = (await logOf('land'))[2];
ok(land3.pocket === 0 && land3.moment === 'roulette.land.miss' && land3.page.includes('chip_vortex') && (await host('h.fx.length')) === fxBefore3, '0: roulette.land.miss, the chips spiral into the bowl, no host fx');
const launches = await logOf('launch');
const pace = launches.slice(1).map((l, i) => l.at - launches[i].at);
report.pace = pace;
ok(launches.length === 3 && pace.every((p) => p >= 7800 && p <= 8800), `about 8 s a spin: ${pace.join(', ')} ms`);
await until('window.dev.station.debug().phase === "bet" && window.dev.sent.some((m) => m.type === "station-request" && m.op === "cursor")', 12000);
d = await dbg();
const cursorReq = await ev('window.dev.sent.filter((m) => m.type === "station-request" && m.op === "cursor").map((m) => m.body)');
ok(d.owed === 0 && d.shown === d.sp && cursorReq.some((c) => c.played === 3), `tape over: Law I settled at ${d.shown} SP, cursor flushed played 3`);
ok((await logOf('land')).length === 3 && (await logOf('launch')).length === 3, 'each spin ran once and landed exactly once');
report.normalTape = { ms: Date.now() - t1, lands: [land1, land2, land3].map((l) => ({ pocket: l.pocket, pay: l.pay, moment: l.moment })) };

/* ------------------------------------------------ 4. Back mid-run, the reopen resumes the unwatched spins */
await boot('?sp=40&next=32w,19,26&latency=40');
for (const s of ['rose', 'deep']) await clickSpot(s);
await clickSel('.roul-spins button[data-n="3"]');
await clickSel('.roul-spin');
await until('window.dev.station.debug().cur !== null', 5000);
await sleep(1500);
const closeMs = await ev('(async () => { const t = performance.now(); await window.dev.stand(); return performance.now() - t; })()');
const after = await host('({ tunnel: h.tunnel.at(-1), rel: h.release.length, fx: h.fx.map((f) => f.fxId) })');
ok(closeMs < 420 && (await ev('!document.querySelector(".roul-station")')), `Back mid-run closes in ${Math.round(closeMs)} ms`);
ok(after.tunnel.level === 0 && after.rel >= 1, 'and cancels its moments: tunnel 0, the wake spiral released');
await ev('window.dev.open()');
await until('window.dev.station.debug().phase === "bet"', 8000);
d = await dbg();
ok(d.resume && /still on the table/.test(d.status) && /Watch the rest/.test(d.spin) && d.tape.played === 0, `reopen: "${d.status}" / "${d.spin}"`);
await clickSel('.roul-spin');
await until('window.dev.station.debug().log.filter((x) => x.what === "land").length === 1', 12000, 30);
ok((await logOf('land'))[0].pocket === 32, 'the unwatched spin plays and lands where the server said (32)');

/* ------------------------------------------------ 5. Full: haze held, velvet wake behind the ball */
await boot('?sp=40&full&next=12');
await clickSpot('sip');
await clickSel('.roul-spin');
await until('window.dev.station.debug().cur !== null', 5000);
await sleep(1600);
d = await dbg();
fx = await host('h.fx.map((f) => ({ id: f.fxId, args: f.args, fired: f.ack.fired.length > 0, token: f.token }))');
const haze = fx.find((f) => f.id === 'fx.haze');
ok(haze && haze.args.hold === true && haze.fired && d.cur.page.includes('velvet_wake'), 'Full: fx.haze {hold} fired and the velvet wake runs');
await shot('12-full-velvet-wake-haze.png');
await until('window.dev.station.debug().cur && window.dev.station.debug().cur.landed', 9000, 16);
ok((await host('h.release.map((r) => r.token)')).includes(haze.token), 'the landing releases the haze');
await sleep(300);
await shot('12b-full-land-win.png');

/* ------------------------------------------------ 6. gated off: dressed plain, nothing fires */
await boot('?sp=40&gates=off&next=17w');
await clickSpot('s17');
await clickSel('.roul-spin');
await until('window.dev.station.debug().cur !== null', 5000);
await sleep(1400);
d = await dbg();
ok(!d.bowl.whirlDrawn && d.bowl.whirlA > 0.3, 'spiral gate off: the dish stays velvet, the wake glows gold instead');
await shot('13-gated-off-wake-run.png');
await until('window.dev.station.debug().cur && window.dev.station.debug().cur.landed', 9000, 16);
await sleep(300);
d = await dbg();
const gatedCalls = await host('({ fx: h.fx.length, tunnel: h.tunnel.length })');
ok(gatedCalls.fx === 0 && gatedCalls.tunnel === 0, `gates off: no host fx, no tunnel (${JSON.stringify(gatedCalls)})`);
ok(/Spiral Wake/.test(d.status) && /won 72 SP/.test(d.status), `a Spiral Wake still shows as text: "${d.status}"`);
await shot('14-gated-off-landing.png');

/* ------------------------------------------------ 7. Calm: halved and still at rest, Normal args to the host */
await boot('?sp=40&calm&next=17w');
d = await dbg();
const calmBeam = d.bowl.beamT;
await sleep(1200);
ok(d.still && d.k === 0.5 && (await dbg()).bowl.beamT === calmBeam, 'Calm: page strength 0.5, the beam holds still at rest');
await shot('15-calm-table.png');
await clickSpot('s17'); await clickSpot('rose');
await clickSel('.roul-spin');
await until('window.dev.station.debug().bowl.phase === "rattle"', 9000, 30);
await sleep(500);
d = await dbg();
ok(d.bowl.tscale > 0.62, `Calm rattle floor raised to 0.7 (time scale ${d.bowl.tscale.toFixed(2)})`);
await shot('16-calm-rattle.png');
await until('window.dev.station.debug().cur && window.dev.station.debug().cur.landed', 9000, 16);
await sleep(300);
fx = await host('h.fx.map((f) => ({ id: f.fxId, args: f.args }))');
ok(fx.find((f) => f.id === 'fx.loom_spiral').args.alpha === 0.65 && fx.find((f) => f.id === 'fx.wash').args.strength === 1, 'Calm: the page still sends Normal args (the host halves)');
await shot('17-calm-landing.png');

/* ------------------------------------------------ 8. refusals: busy retried with one idem, insufficient, a closed door */
await boot('?sp=40&next=3');
await clickSpot('s3');
await ev("window.dev.server.fail('spin', 'busy', 2)");
await clickSel('.roul-spin');
await until('window.dev.station.debug().cur !== null', 8000);
const spins = await ev("window.dev.server.log.filter((l) => l.op === 'spin').map((l) => l.body.idem)");
ok(spins.length === 3 && new Set(spins).size === 1, 'busy twice, then the spin: three sends, one idem');
await boot('?sp=2');
for (const s of ['rose', 'sip', 's9']) await clickSpot(s);
d = await dbg();
ok(d.spinDisabled && /needs 3 SP/.test(d.why), `insufficient shown before sending: "${d.why}"`);
await boot('?sp=40');
await clickSpot('rose');
await ev('window.dev.server.setOpen(false)');
await clickSel('.roul-spin');
await until('window.dev.station.debug().phase === "closed"', 5000);
ok(await ev('!document.querySelector(".roul-card").hidden && /closed/.test(document.querySelector(".roul-card").textContent)'), 'a closed door says so');

/* ------------------------------------------------ 9. inside the real room: mount, open, a spin, Back */
const FAKE_HOST = `(() => {
  const listeners = [];
  const emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  let srv = null;
  const mock = () => srv || (srv = import('/backroom/stations/roulette/mock-server.js').then((m) => { const s = m.createMockServer({ sp: 57, floorMs: 0 }); s.script({ pocket: 36 }); window.__srv = s; return s; }));
  window.__posted = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(m);
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', lex: {},
        gates: { flash: true, subliminal: true, spiral: true, brainDrain: true }, stations: ['slot', 'wheel', 'roulette'], open: true });
      if (m.type === 'station-request') mock().then((s) => s.handle(m.op, m.body)).then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body }));
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', src: 'pool' })) });
    },
  };
})();`;
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
await until(`document.documentElement.classList.contains('br-ready')`, 40000, 100);
await ev(`window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'roulette'))`);
ok(await until('!!document.querySelector(".roul-station[data-phase=bet]")', 10000, 100), 'the room mounts and opens the roulette through the loader');
const roomRect = await ev(`(() => { const c = document.querySelector('.roul-stage'); const r = c.getBoundingClientRect(); return { x: r.left, y: r.top }; })()`);
ok(await ev(`!!document.querySelector('.roul-station[data-host-back][data-host-sp]') && document.querySelector('.roul-back').hidden`), 'ctx.hostBack and ctx.spReadout: the station hides its own Back and SP chip');
await sleep(500);
await shot('18-room-roulette-open.png');
// place 36 through the canvas in the room page (the room page has no window.dev; find the cell by the mat layout)
const cellCentre = await ev(`(async () => {
  const c = document.querySelector('.roul-stage'); const w = c.clientWidth, h = c.clientHeight;
  const mw = w * 0.47, mh = h * 0.46, cell = Math.max(14, Math.min(mw / 13, mh / 5.4)), ox = w * 0.5 + (mw - cell * 13) / 2, oy = h * 0.16 + (mh - cell * 5.4) / 2;
  return { x: ox + 12 * cell + cell / 2, y: oy + cell / 2 }; })()`);
await click(Math.round(roomRect.x + cellCentre.x), Math.round(roomRect.y + cellCentre.y));
await ev(`document.querySelector('.roul-spin').click()`);
await until(`(document.querySelector('.roul-history') || {}).childElementCount >= 1`, 12000, 50);
await sleep(400);
const room = await ev(`({ open: window.__posted.filter((m) => m.type === 'station-open').map((m) => m.station), fx: window.__posted.filter((m) => m.type === 'fx').map((m) => ({ id: m.fxId, station: m.station, args: m.args, symbols: m.symbols })),
  tunnel: window.__posted.filter((m) => m.type === 'fx-tunnel').length, media: window.__posted.filter((m) => m.type === 'media-request').map((m) => ({ station: m.station, count: m.count })),
  chip: document.querySelector('#br-sp-value').textContent, sp: window.__srv.user.sp, status: document.querySelector('.roul-status').textContent })`);
report.room = room;
ok(room.open.includes('roulette') && room.media.some((m) => m.station === 'roulette' && m.count === 4), 'station-open posted; a 4-GIF deal asked for the roulette');
ok(room.fx.some((f) => f.id === 'fx.gif_from' && f.station === 'roulette' && f.args.from && /^g\d$/.test(f.symbols[0])) && room.tunnel > 3, `the straight on 36 fired its landing through the real bridge (${room.fx.map((f) => f.id).join(', ')})`);
ok(room.chip === String(room.sp) && room.sp === 57 - 1 + 36 && /36 Rose, Deep row/.test(room.status), `the room chip lands at ${room.chip} with the text "${room.status.split('\n')[0]}"`);
await shot('19-room-roulette-landing.png');
await ev(`document.querySelector('#br-back').click()`);
await sleep(700);
ok(await ev(`window.__posted.some((m) => m.type === 'station-close' && m.station === 'roulette') && !document.querySelector('.roul-station')`), 'Back closes the roulette and posts station-close');

await writeFile(join(OUT, 'roulette-check.json'), JSON.stringify(report, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall roulette checks passed');
await done(fails ? 1 : 0);
