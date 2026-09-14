/* ============================================================================
 * shared/hypno/tests/kit-check.mjs - the hypno kit in headless Chrome, driven over CDP, on tests/kit.html and the
 * real room page.
 *
 *   node backroom/shared/hypno/tests/kit-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (KIT_PORT, default 8896, debug +500), the mock
 * host answers every call, and on the room page tests/probe-station.js stands in for the slot station. The only
 * process this stops is the Chrome it started, by its own handle.
 * Evidence: the Loom presets, the handedness probe (WebGL and 2D fallback), the 13 deal, the moments on a Normal
 * run, a gated-off run and a Calm run, and kit-check.json.
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
const PORT = Number(process.env.KIT_PORT || 8896), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  const file = path === '/backroom/stations/slot/station.js' ? join(HERE, 'probe-station.js') : join(WEB, path);
  try { const body = await readFile(file); res.writeHead(200, { 'content-type': MIME[extname(file).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-kit-'));
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
async function open(query) {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/shared/hypno/tests/kit.html${query}` });
  for (let i = 0; i < 100 && !(await ev('!!window.__kitReady')); i++) await sleep(100);
  return ev('K.boot()');
}
const report = {};
const near = (a, b, tol) => Math.abs(a - b) <= tol;

/* ------------------------------------------------ 1. Normal: presets, handedness, deck, moments */
let boot = await open('');
ok(boot && boot.size === 8, `a 13 deal from a pool of 8 pictures: ${boot && boot.size} keys, no padding`);
ok(await ev('K.kit.webgl'), 'the kit draws through WebGL');
await ev(`K.show('presets')`);
await sleep(1200);
const presets = await ev('({ backs: K.scene.backRenders, dbg: K.kit.debug() })');
ok(presets.backs === 1, `13 card backs in a frame cost ${presets.backs} render`);
ok(presets.dbg.users === 1 && /^\d+x\d+$/.test(presets.dbg.backing), 'one shared context for the page, backing ' + presets.dbg.backing);
ok(await ev(`(() => { const [w, h] = K.kit.debug().backing.split('x').map(Number); return Math.max(w, h) <= 512; })()`), 'the backing store stays at or under 512 px');
await shot('loom-presets.png');
const still = await ev(`(() => { const g = document.createElement('canvas').getContext('2d'), k = K.kit;
  k.draw(g, 'hub', 0, 0, 64, 64, { angle: 1.25 }); k.setStill(true); const r0 = k.debug().renders;
  for (const now of [10, 900, 5000]) k.draw(g, 'backs', 0, 0, 64, 90, { now, backing: 'small' });
  for (const now of [10, 900, 5000]) k.draw(g, 'hub', 0, 0, 64, 64, { angle: now });
  const r1 = k.debug().renders; k.setStill(false); return { renders: r1 - r0 }; })()`);
ok(still.renders <= 2, `still: backs hold phase 0 and the hub its last angle whatever the clock (${still.renders} renders for 6 draws)`);

await ev(`K.show('hand')`);
const hand = await ev('K.handedness()');
report.handednessWebgl = hand;
for (const [n, m] of Object.entries(hand)) {
  const want = m.delta * 180 / Math.PI;
  ok(m.turn.every((v) => v > 0), `${n}: a clockwise angle turns the field clockwise (${m.turn.map((v) => v.toFixed(1)).join(', ')} deg for +${want.toFixed(1)})`);
  // screen's gold layer 2 counter-turns; a wobble (whirl, wake) moves with the phase too: those only need the sign
  const tol = n === 'screen' ? null : (n === 'whirl' || n === 'wake') ? 0.6 : 0.35;
  if (tol) ok(m.turn.every((v) => near(v, want, want * tol)), `${n}: by the angle given, within ${tol * 100}%`);
  ok(m.rim.every((v) => v > 0), `${n}: arms lead at the rim, so it reads inward (${m.rim.map((v) => v.toFixed(1)).join(', ')} deg)`);
}
await sleep(300);
await shot('loom-handedness.png');
for (const name of ['screen', 'wake']) {
  const g = await ev(`K.measureGif(${JSON.stringify(name)})`);
  report['woven_' + name] = g;
  ok(g && g.ok, `woven spirals/${name}.gif (${g && g.size}, ${g && g.frames} frames): frame 0 -> 2 turns clockwise (${g && g.turn.map((v) => v.toFixed(1)).join(', ')} deg, ~${g && g.expectDeg.toFixed(1)} expected) and its arms lead at the rim`);
}

await ev(`K.show('deck')`);
await sleep(3000);
let deck = await ev('({ d: K.deck.debug(), map: ["A","2","3","4","5","6","7","8","9","T","J","Q","K"].map((v) => K.deck.keyFor(v)), pick: K.deck.pickKey("spin-7") === K.deck.pickKey("spin-7") })');
report.deck = deck;
ok(deck.map.join() === 'g0,g1,g2,g3,g4,g5,g6,g7,g0,g1,g2,g3,g4', 'values A..K wear gifs[i % 8]: ' + deck.map.join(' '));
ok(deck.d.ready === 8 && deck.d.animated >= 4, `every picture loaded (${deck.d.ready}), ${deck.d.animated} of them animated`);
ok(deck.d.frames > 10, `the drawn pictures play: ${deck.d.frames} frames`);
ok(deck.d.decodes + deck.d.loads <= deck.d.ticks, `at most one decode started per tick (${deck.d.decodes} frames + ${deck.d.loads} loads in ${deck.d.ticks} ticks)`);
await shot('deck-13-values.png');
const idle = await ev(`(async () => { K.show('presets'); const f0 = K.deck.debug().frames; for (let i = 0; i < 20; i++) { K.deck.tick(performance.now()); await new Promise((r) => setTimeout(r, 50)); } return K.deck.debug().frames - f0; })()`);
ok(idle === 0, 'a picture nobody drew since the last tick does not advance');

let wheel = await ev('K.wheel()');
report.normalWheel = wheel;
ok(wheel.calls.map((c) => c.fxId).join() === 'fx.loom_spiral,fx.gif_from,fx.wash', 'wheel.land.jackpot: loom_spiral, gif_from, wash, in order');
ok(JSON.stringify(wheel.calls[0].args) === JSON.stringify({ preset: 'screen', ms: 4200, alpha: 0.9 }), 'the spiral: preset screen, 4200 ms, alpha 0.9');
const gf = wheel.calls[1];
ok(gf.args.ms === 4600 && gf.args.scale === 0.46 && gf.args.from && gf.args.from.w === 60 && gf.args.from.h === 44 && /^g\d$/.test(gf.symbols[0]),
  `the picture ${gf.symbols[0]} grows from the slice rect ${JSON.stringify(gf.args.from)} at scale 0.46`);
ok(wheel.calls[2].args.color === '#e8c27a' && wheel.calls[2].args.strength === 1, 'a brass wash at strength 1');
ok(wheel.tunnel >= 2 && wheel.tunnel <= 18, `the long last turn posted fx-tunnel ${wheel.tunnel} times in 1.6 s (at most 10 a second)`);
await sleep(1000);
await shot('moment-normal-wheel-jackpot.png');

let cards = await ev('K.cards()');
report.normalCards = cards;
ok(cards.held.held === true && cards.tunnel === 0, 'a cards decision holds the screen: no fx, no tunnel');
ok(cards.calls.map((c) => c.fxId).join() === 'fx.gif_from,fx.wash' && cards.calls[0].args.ms === 4000 && cards.calls[1].args.color === '#ff5fa2',
  'cards.bloom: the ace picture grows (4000 ms), a rose wash 0.8');
await sleep(900);
await shot('moment-normal-cards-bloom.png');
await sleep(3400);
const win = await ev('K.cardsWin().then((r) => ({ r, last: K.host.fx.at(-1) }))');
ok(win.last.fxId === 'fx.wash' && win.last.args.strength === 0.9 && !win.last.symbols, 'a win right after a bloom: mint wash 0.9, no picture');
await sleep(200);
await shot('moment-normal-cards-win.png');
await sleep(900);
await ev('K.cardsLose()');
await sleep(1300);
const lose = await ev('K.host.tunnel.map((t) => t.level)');
ok(Math.max(...lose) >= 0.7 && Math.max(...lose) <= 0.75, `cards.lose breathes the tunnel up to ${Math.max(...lose)}`);
await shot('moment-normal-cards-lose-tunnel.png');
await sleep(1600);

let roul = await ev('K.roulette()');
report.normalRoulette = roul;
ok(roul.calls[0].fxId === 'fx.loom_spiral' && roul.calls[0].args.preset === 'wake' && roul.calls[0].args.hold === true, 'roulette.wake holds a wake spiral (no haze below Full)');
ok(roul.tunnel.length >= 2 && roul.tunnel.length <= 20 && roul.tunnel.every((v) => v <= 0.75), `the ball run tunnel: ${roul.tunnel.length} posts, max ${Math.max(...roul.tunnel)}`);
await shot('moment-normal-roulette-wake.png');
const land = await ev('K.rouletteLand()');
ok(land.releases === 1 && land.r.page.join() === 'chips_in,pulled_pair', 'the landing releases the held spiral first; chips and the pulled pair');
await sleep(900);
await shot('moment-normal-roulette-big.png');

/* ------------------------------------------------ 2. every gate off: dressed plain, nothing fires */
boot = await open('?gates=off');
wheel = await ev('K.wheel()');
report.gatedWheel = wheel;
ok(wheel.calls.length === 0 && wheel.tunnel === 0, 'gates off: the jackpot fires no host fx and no tunnel');
ok(wheel.r.page.join() === 'quiet_room,reveal', 'and the page effects still come back');
await sleep(900);
await shot('gated-off-wheel-jackpot.png');
cards = await ev('K.cards()');
ok(cards.calls.length === 0, 'gates off: no bloom picture, no wash');
roul = await ev('K.roulette()');
ok(roul.calls.length === 0 && roul.tunnel.length === 0, 'gates off: no wake spiral, no tunnel');
await sleep(300);
await shot('gated-off-table.png');

/* ------------------------------------------------ 3. Calm: the page still sends Normal values, the host halves */
boot = await open('?calm=1');
wheel = await ev('K.wheel()');
report.calmWheel = wheel;
ok(JSON.stringify(wheel.calls.map((c) => c.args.alpha ?? c.args.strength ?? c.args.ms)) === JSON.stringify([0.9, 4600, 1]), 'Calm: args are the Normal values (nobody halves twice)');
ok(await ev('K.ctx.intensity === "calm"'), 'strengthK halves what the page draws');
await sleep(1000);
await shot('calm-wheel-jackpot.png');
roul = await ev('K.roulette()');
ok(roul.calls.some((c) => c.fxId === 'fx.loom_spiral'), 'Calm: the wake spiral still fires (the host halves its alpha)');
await shot('calm-roulette-wake.png');

/* ------------------------------------------------ 4. no WebGL: the 2D fallback, same handedness */
boot = await open('?nogl=1');
ok(!(await ev('K.kit.webgl')), 'no WebGL: the kit says so');
await ev(`K.show('presets')`);
await sleep(800);
await shot('loom-fallback-presets.png');
await ev(`K.show('hand')`);
const fb = await ev('K.handedness()');
report.handednessFallback = fb;
for (const [n, m] of Object.entries(fb)) ok(m.turn.every((v) => v > 0) && m.rim.every((v) => v > 0), `fallback ${n}: turns clockwise, arms lead at the rim`);
await sleep(300);
await shot('loom-fallback-handedness.png');
ok(await ev('(() => { K.kit.dispose(); return K.kit.debug().users === 0 && K.kit.draw(document.createElement("canvas").getContext("2d"), "hub", 0, 0, 9, 9) === false; })()'), 'dispose frees the kit');

/* ------------------------------------------------ 5. the real room page: the loader ctx, driven by the kit */
const FAKE_HOST = `(() => {
  const listeners = [];
  const emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  window.__hostEmit = emit; window.__posted = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(m);
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', lex: {},
        gates: { flash: true, subliminal: true, spiral: false, brainDrain: true }, stations: ['slot'], open: null });
      if (m.type === 'station-request') emit({ type: 'station-result', reqId: m.reqId, ok: true, status: 200, body: { ok: true, sp: 57 } });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', src: 'pool' })) });
    },
  };
})();`;
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
for (let i = 0; i < 300 && !(await ev(`document.documentElement.classList.contains('br-ready')`)); i++) await sleep(100);
await ev(`window.__backroom.visit(window.__backroom.stations.find((s) => s.key === 'slot:violet'))`);
for (let i = 0; i < 40 && !(await ev('!!(window.__probe && window.__probe.opened)')); i++) await sleep(100);
const room = await ev(`(async () => {
  const kit = await import('/backroom/shared/hypno/index.js');
  const ctx = window.__probe.ctx, out = {};
  out.gates = { ...ctx.gates }; out.frozen = Object.isFrozen(ctx.gates);
  const got = []; ctx.onSettings((f) => got.push(f));
  const m = kit.createMoments(ctx, { station: 'slot' });
  const deck = await kit.createDeck(ctx, { count: 13 });
  out.deck = deck.size;
  const el = document.querySelector('#br-layer');
  const r = m.play('wheel.land.jackpot', { color: '#9b6bff', from: kit.viewportRect(el, 400, 200, 60, 44), gif: deck.pickKey('j') });
  const p = ctx.fx('fx.wash', ['g1'], { color: '#5fffd0', strength: 0.7 });
  out.ack = await p; out.token = p.token;
  m.tunnel(0.5);
  await new Promise((res) => setTimeout(res, 150));
  m.play('roulette.wake'); m.cancel();
  window.__hostEmit({ type: 'settings', motion: 'full', intensity: 'calm', reduced: false, gates: { flash: false, subliminal: true, spiral: true, brainDrain: false } });
  await new Promise((res) => setTimeout(res, 50));
  out.settings = got; out.gatesAfter = { ...ctx.gates }; out.r = r;
  out.posted = window.__posted.filter((x) => ['fx', 'fx-tunnel', 'fx-release', 'media-request'].includes(x.type));
  return out;
})()`);
report.room = room;
ok(room.gates.spiral === false && room.gates.flash === true && room.frozen, 'the room hands init.gates to the station, frozen');
ok(room.deck === 13 && room.posted.some((x) => x.type === 'media-request' && x.count === 13 && x.station === 'slot'), 'createDeck asks the real bridge for 13');
const rf = room.posted.filter((x) => x.type === 'fx');
ok(rf.length === 3 && rf[0].fxId === 'fx.gif_from' && rf[0].args.scale === 0.46 && rf[0].args.from.w === 60 && rf[1].fxId === 'fx.wash',
  'spiral gate off in init: the jackpot posts gif_from and wash with args through the bridge');
ok(rf[2].token === room.token && room.ack.fired[0] === 'fx.wash', 'ctx.fx carries its token on the promise and resolves with the ack');
ok(room.posted.some((x) => x.type === 'fx-tunnel' && x.level === 0.5) && room.posted.filter((x) => x.type === 'fx-tunnel').at(-1).level === 0, 'moments.tunnel posts fx-tunnel, cancel posts 0');
ok(!room.posted.some((x) => x.type === 'fx' && x.fxId === 'fx.loom_spiral'), 'the wake spiral is dressed off by the spiral gate');
ok(room.settings.length === 1 && room.settings[0].intensity === 'calm' && room.settings[0].gates.flash === false && room.gatesAfter.brainDrain === false,
  'a settings frame reaches onSettings and ctx.gates live');
await shot('room-probe-station.png');
await ev(`document.querySelector('#br-back').click()`);
await sleep(600);
ok(await ev(`window.__posted.some((m) => m.type === 'station-close' && m.station === 'slot') && window.__probe.closed === 1`), 'Back closes the probe station');

await writeFile(join(OUT, 'kit-check.json'), JSON.stringify(report, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall kit checks passed');
await done(fails ? 1 : 0);
