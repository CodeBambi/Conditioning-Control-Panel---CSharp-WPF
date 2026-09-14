/* ============================================================================
 * cards-check.mjs - Soft Hand in headless Chrome, driven over CDP, on dev.html (the kit's mock host plus
 * mock-server.js) and inside the real room page (a fake host relaying to the same mock server).
 *
 *   node backroom/stations/cards/tests/cards-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (CARDS_PORT, default 8898, debug +500) and the
 * mocks answer every call. The only process this stops is the Chrome it started, by its own handle.
 * Evidence: a screenshot of every key moment in CONTRACT 10.13.F (sit fan, Loom backs, your deck, a decision held,
 * win flash, win tunnel and chip vortex, losing edges, ace glow and blackjack bloom, ripple felt at Full, split),
 * a gated-off run, a Calm run, an OS reduced-motion bloom hold, a resume, the sit latch, a settled reopen, the room visit,
 * and cards-check.json.
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
const PORT = Number(process.env.CARDS_PORT || 8898), DEBUG_PORT = PORT + 500;
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
const until = async (expr, ms = 8000) => { for (let i = 0; i < ms / 50; i++) { if (await ev(expr)) return true; await sleep(50); } return false; };
const click = (sel) => ev(`(() => { const b = document.querySelector(${JSON.stringify(sel)}); if (!b) return false; b.click(); return true; })()`);
const fxList = () => ev('window.dev.host.fx.map((r) => ({ fxId: r.fxId, symbols: r.symbols || null, args: r.args || null, fired: r.ack.fired }))');
const levels = () => ev('window.dev.host.tunnel.map((r) => r.level)');
const moment = (id) => until(`window.dev.station.debug().log.some((x) => x.what === 'moment' && x.id === ${JSON.stringify(id)} && x.at > (window.__mark || 0))`, 12000);
const mark = () => ev('window.__mark = Math.round(performance.now())');
async function shot(name) { await writeFile(join(OUT, name), Buffer.from((await cdp('Page.captureScreenshot', { format: 'png' })).result.data, 'base64')); console.log('  shot ' + name); }
/** Frames at the given ms after `t0`, composited into one strip in the page itself. */
async function strip(name, times, t0) {
  const frames = [];
  for (const at of times) { const w = at - (Date.now() - t0); if (w > 0) await sleep(w); frames.push({ at: Date.now() - t0, data: (await cdp('Page.captureScreenshot', { format: 'jpeg', quality: 80 })).result.data }); }
  const png = await ev(`(async () => { const f = ${JSON.stringify(frames)}; const W = 426, H = 240, c = document.createElement('canvas');
    c.width = W * Math.min(4, f.length); c.height = H * Math.ceil(f.length / 4); const g = c.getContext('2d'); g.fillStyle = '#000'; g.fillRect(0, 0, c.width, c.height);
    for (let i = 0; i < f.length; i++) { const im = new Image(); im.src = 'data:image/jpeg;base64,' + f[i].data; await im.decode();
      const x = (i % 4) * W, y = Math.floor(i / 4) * H; g.drawImage(im, x, y, W, H); g.fillStyle = '#000a'; g.fillRect(x, y, 86, 20);
      g.fillStyle = '#fff'; g.font = '13px Segoe UI'; g.fillText('+' + f[i].at + ' ms', x + 6, y + 15); }
    return c.toDataURL('image/png').slice(22); })()`);
  await writeFile(join(OUT, name), Buffer.from(png, 'base64'));
  console.log('  strip ' + name);
}
async function boot(query, { play = true } = {}) {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/stations/cards/dev.html${query}` });
  await until('!!(window.dev && window.dev.station)', 10000);
  await ev('window.localStorage.clear(); window.__mark = 0; window.dev.open(); true');
  if (play) await until("window.dev.station.debug().phase === 'play'", 12000);
}
const decideNow = () => until('window.dev.station.debug().decide', 8000);
const settledNow = () => until("(() => { const d = window.dev.station.debug(); return d.queue === 0 && d.shown && d.shown.done; })()", 12000);
const dealWhenReady = async () => { await until("window.dev.station.debug().controls.deal", 12000); await mark(); return click('.cards-deal'); };

/* ---------------------------------------------------------------- 1. a sitting at Normal */
await boot('?floor=600&script=Th.9d.8c.8s,Th.Td.7c.9s,Th.Td.8c.8s,As.9d.Kh.7c', { play: false });
await sleep(2200);
let d = await dbg();
await shot('sit-fan.png');
ok(d.phase === 'sit' && d.table.fanCards === 13 && d.table.pictures >= 13, `sit fan: 13 values face up with pictures (${d.table.fanCards} cards, ${d.table.pictures} pictures)`);
ok((await ev('window.dev.host.media.length === 1 && window.dev.host.media[0].count === 13 && window.dev.host.media[0].dealt === 13')), 'the sit-down asks the host for 13 pictures');
ok(new Set(d.deck.map).size === 13 && d.deck.map[0] === 'g0' && d.deck.map[12] === 'g12', 'values A..K wear g0..g12 in deal order');
await until("window.dev.station.debug().phase === 'play'", 6000);
d = await dbg();
ok(/Pick a bet and deal/.test(d.status) && d.stake === 2 && d.hint === false && (await ev("localStorage.getItem('br_cards_hint') === null && !document.querySelector('.cards-hint-toggle input').checked")), 'ready: bet 2 at 57 SP, hint off by default');

await mark();
const pressed = await ev(`(async () => { const t0 = performance.now(); document.querySelector('.cards-deal').click();
  await new Promise((r) => requestAnimationFrame(r)); return { ms: performance.now() - t0, ring: document.querySelector('.cards-deal').classList.contains('is-ringing') }; })()`);
ok(pressed.ring && pressed.ms < 100, `Law VIII: Deal rings within ${Math.round(pressed.ms)} ms`);
await decideNow();
d = await dbg();
const hole = d.table.cards.find((c) => c.owner === 'd' && c.slot === 1);
await shot('decision-open-loom-backs.png');
ok(d.table.cards.length === 4 && hole && hole.code === null && !hole.face && d.table.backs >= 2, 'the hole card and the shoe wear Loom backs');
ok(d.moments.held === true && (await fxList()).length === 0 && (await levels()).every((l) => l === 0), 'decision open: the screen is held, no host fx, no tunnel');
ok(d.controls.moves.hit && d.controls.moves.stand && d.controls.moves.double && !d.controls.moves.split && !d.controls.deal, 'Hit, Stand, Double from legal; Split and Deal off');
ok(/18 against Emi showing 9\. Your move\./.test(d.status) && d.chip.value === 55, `status as text: "${d.status}", 55 SP`);
const r0 = await dbg(); await sleep(600); const r1 = await dbg();
ok(r1.kit.renders - r0.kit.renders <= r1.table.frames - r0.table.frames, `every card back shares one Loom render a frame (${r1.kit.renders - r0.kit.renders} renders over ${r1.table.frames - r0.table.frames} frames)`);
await click('.cards-hint-toggle input');
await sleep(150);
ok(/Hint: Stand\./.test(await ev("document.querySelector('.cards-hint').textContent")) && (await ev("document.querySelector('.cards-move[data-move=stand]').classList.contains('is-hint') && localStorage.getItem('br_cards_hint') === '1'")), 'hint on: "Hint: Stand." and remembered in br_cards_hint');
await shot('hint-on.png');
await click('.cards-hint-toggle input');

await mark();
let t0 = Date.now();
await click('.cards-move[data-move=stand]');
const winStrip = strip('strip-win.png', [0, 500, 1100, 1500, 1900, 2300, 2900, 3800], t0);
await until('window.dev.station.debug().chip.owed > 0', 2000);
d = await dbg();
ok(d.chip.server === 59 && d.chip.owed === 4 && d.chip.value === 55, `Law I: the server says 59, the chip holds ${d.chip.value} until the hand shows`);
await moment('cards.win');
d = await dbg();
ok(!d.controls.deal && d.controls.dealWhy === 'screen' && d.dealText === 'One moment' && d.screenLeftMs > 0 && d.screenLeftMs <= 900,
  `the win wash holds the next deal: "${d.dealText}", ${d.screenLeftMs} ms left`);
await shot('win-flash.png');
await sleep(700);
await shot('win-tunnel-chip-vortex.png');
d = await dbg();
await winStrip;
let fx = await fxList();
ok(fx.length === 1 && fx[0].fxId === 'fx.wash' && fx[0].args.color === '#5fffd0' && fx[0].args.strength === 0.7 && fx[0].symbols[0] === d.deck.map[9], `cards.win: fx.wash mint 0.7 with the ten's picture (${JSON.stringify(fx[0])})`);
ok(d.table.tunnel && d.table.chips > 0 && d.chip.value === 59 && /18 beats 17\. \+2 SP\./.test(d.status), `win tunnel and chip vortex; "${d.status.split('\n')[0]}"; 59 SP`);
summary.win = { fx };

await dealWhenReady();
await decideNow();
await ev('window.dev.host.clear()');
await mark();
await click('.cards-move[data-move=stand]');
await moment('cards.lose');
const loseAt = Date.now();
await sleep(1150);
d = await dbg();
ok(!d.controls.deal && d.controls.dealWhy === 'screen' && d.dealText === 'One moment', `the losing edges hold the next deal mid-breath: "${d.dealText}", ${d.screenLeftMs} ms left`);
await shot('losing-edges-deal-held.png');
await until('window.dev.station.debug().controls.deal', 4000);
const loseHeld = Date.now() - loseAt;
let lv = await levels();
ok(loseHeld >= 2300 && lv.at(-1) === 0, `Deal comes back only once the edges have closed (${loseHeld} ms after the moment, the tunnel at ${lv.at(-1)})`);
summary.loseHoldMs = loseHeld;
d = await dbg();
lv = await levels();
ok((await fxList()).length === 0 && Math.max(...lv) > 0.6 && Math.max(...lv) <= 0.75 && lv.at(-1) === 0, `cards.lose: one slow breath of tunnel, peak ${Math.max(...lv)}, back to 0, no fx`);
ok(/19 beats 17\. Emi takes 2 SP\./.test(d.status) && d.chip.value === 57, `"${d.status.split('\n')[0]}"`);
summary.lose = { levels: lv };

await dealWhenReady();
await decideNow();
await ev('window.dev.host.clear()');
await mark();
await click('.cards-move[data-move=stand]');
await moment('cards.push');
await sleep(600);
d = await dbg();
ok((await fxList()).length === 0 && (await levels()).every((l) => l === 0) && /Push at 18\. Bet returned\./.test(d.status), 'cards.push: nothing on screen, "Push at 18. Bet returned."');

await until("window.dev.station.debug().controls.deal", 12000);
await ev('window.dev.host.clear()');
await mark();
t0 = Date.now();
await click('.cards-deal');
const bjStrip = strip('strip-blackjack-bloom.png', [0, 1500, 1950, 2400, 3000, 3800, 4600, 5600], t0);
await moment('cards.bloom');
await shot('ace-glow.png');
await sleep(1300);
await shot('blackjack-bloom.png');
await bjStrip;
await moment('cards.win');
d = await dbg();
fx = await fxList();
const bloom = d.log.find((x) => x.what === 'moment' && x.id === 'cards.bloom' && x.at > 0);
const deal = d.log.filter((x) => x.what === 'reply' && x.op === 'deal').at(-1);
ok(fx.length === 3 && fx[0].fxId === 'fx.gif_from' && fx[0].symbols[0] === d.deck.map[0] && fx[0].args.ms === 4000 && fx[1].fxId === 'fx.wash' && fx[1].args.color === '#ff5fa2' && fx[1].args.strength === 0.8,
  'cards.bloom: fx.gif_from out of the ace with the ace\'s picture, then a rose wash 0.8');
ok(Math.abs(fx[0].args.from.x - bloom.from.x) <= 1 && Math.abs(fx[0].args.from.w - bloom.from.w) <= 1, `the bloom grows from the ace's rect (${JSON.stringify(fx[0].args.from)})`);
ok(bloom.at - deal.at >= 1700 && bloom.at - deal.at <= 2200, `the bloom fires as the second player card finishes turning (${bloom.at - deal.at} ms after the reply)`);
ok(fx[2].fxId === 'fx.wash' && fx[2].args.color === '#5fffd0' && fx[2].args.strength === 0.9 && !fx[2].symbols, 'cards.win after a bloom: mint 0.9, no picture');
ok(d.controls.dealWhy === 'screen' && d.dealText === 'One moment' && /Blackjack! \+4 SP\./.test(d.status), `Deal waits out the bloom ("${d.dealText}", ${d.screenLeftMs} ms left); "Blackjack! +4 SP."`);
await until('window.dev.station.debug().controls.deal', 5000);
d = await dbg();
ok(d.screenLeftMs === 0, 'and deals again once the picture has gone');
summary.blackjack = { fx, bloomAfterReplyMs: bloom.at - deal.at };

/* ---------------------------------------------------------------- 2. split and double */
await boot('?floor=600&sp=20&script=8h.6d.8c.Ts.3s.Td.9c.Kd');
await dealWhenReady();
await decideNow();
ok((await dbg()).controls.moves.split, 'a pair offers Split');
await click('.cards-move[data-move=split]');
await decideNow();
d = await dbg();
await shot('split-hands.png');
ok(d.table.hands === 2 && /Hand 1: 11 against Emi showing 6/.test(d.status) && d.controls.moves.double, `split: two hands side by side, "${d.status}"`);
await click('.cards-move[data-move=double]');
await until("window.dev.station.debug().decide && window.dev.station.debug().shown.active === 1", 8000);
await click('.cards-move[data-move=stand]');
await settledNow();
await sleep(400);
d = await dbg();
fx = await fxList();
await shot('split-settled.png');
ok(/Hand 1: .*\+2 SP/.test(d.status) && /Hand 2: .*\+1 SP/.test(d.status) && /Up 3 SP overall/.test(d.status), `split result as text: ${JSON.stringify(d.status)}`);
ok(fx.length === 1 && fx[0].symbols[0] === d.deck.map[9], 'the win picture is the highest card over both winning hands (the ten)');

/* ---------------------------------------------------------------- 3. Full: ripple felt */
await boot('?full&floor=600&script=Th.9d.8c.8s');
await dealWhenReady();
await sleep(1100);
d = await dbg();
await shot('ripple-felt-full.png');
ok(d.dress.full && d.table.ripples > 0, `Full: ripples in the felt (${d.table.ripples})`);

/* ---------------------------------------------------------------- 4. Back, resume, sit back down */
await boot('?floor=600&script=Th.9d.8c.8s.2c');
await dealWhenReady();
await sleep(300);
const closeMs = await ev('(async () => { const t = performance.now(); await window.dev.stand(); return performance.now() - t; })()');
ok(closeMs < 420 && (await ev("!document.querySelector('.cards-station')")), `Back mid-deal: closed in ${Math.round(closeMs)} ms`);
await sleep(2000);
ok((await fxList()).length === 0 && (await levels()).every((l) => l === 0), 'and nothing fired after Back');
await ev('window.dev.open()');
await sleep(1500);
await shot('resume-sit-fan.png');
ok((await dbg()).phase === 'sit' && (await ev('window.dev.host.media.length === 2')), 'reopen: a new sit-down deal and the sit fan first');
await decideNow();
d = await dbg();
await shot('resume-open-hand.png');
ok(d.moments.held && d.table.cards.length === 4 && d.table.cards.find((c) => c.owner === 'd' && c.slot === 1).code === null && /Your move/.test(d.status), 'then the open hand with its decisions live, the screen held');
await ev("window.dev.server.handle('hit', { idem: 'externalhit0000000001', handId: window.dev.station.debug().shown.id, step: 0 })");
await click('.cards-move[data-move=stand]');
await until("window.dev.station.debug().log.some((x) => x.what === 'reply' && x.reason === 'stale')", 4000);
await decideNow();
d = await dbg();
ok(d.table.cards.filter((c) => c.owner === 0).length === 3 && /20 against Emi/.test(d.status), 'stale: the server\'s hand (a hit from elsewhere) is adopted and the decision reopens');
await click('.cards-move[data-move=stand]');
await settledNow();
await until("window.dev.station.debug().controls.sit", 4000);
await click('.cards-sit');
await sleep(1800);
d = await dbg();
await shot('resit-fan.png');
ok(d.sitting === 2 && d.phase === 'sit' && (await ev('window.dev.host.media.length === 3')) && /re-dealt/.test(d.status), 'Stand up, sit back down: a fresh deal of 13 and the fan again');

/* ---------------------------------------------------------------- 5. retries, floor, door */
await boot('?floor=600&latency=30&script=Th.9d.8c.8s,Th.9d.8c.8s');
await ev("window.dev.server.fail('deal', 'busy', 2)");
await dealWhenReady();
await decideNow();
let deals = await ev("window.dev.server.log.filter((l) => l.op === 'deal')");
ok(deals.length === 3 && new Set(deals.map((l) => l.idem)).size === 1, 'busy twice: retried with the same idem');
await ev("window.dev.server.fail('stand', 'timeout', 1, { apply: true })");
await click('.cards-move[data-move=stand]');
await settledNow();
const stands = await ev("window.dev.server.log.filter((l) => l.op === 'stand')");
ok(stands.length === 2 && stands[0].idem === stands[1].idem && (await dbg()).chip.value === 59, 'a lost reply: the same idem replays the receipt, paid once (59)');
await until("window.dev.station.debug().controls.deal", 4000);
await ev('window.dev.server.user.nextDealAt = Date.now() + 2200');
await click('.cards-deal');
ok(await until("/Shuffling/.test(window.dev.station.debug().status)", 2000), 'too_fast: "Shuffling. The next deal is ready in N s."');
await decideNow();
deals = await ev("window.dev.server.log.filter((l) => l.op === 'deal')");
ok(deals.slice(-2)[0].idem === deals.slice(-2)[1].idem, 'and the deal goes through on the same idem');
await click('.cards-move[data-move=stand]');
await settledNow();
await ev('window.dev.server.setOpen(false)');
await until("window.dev.station.debug().controls.deal", 4000);
await click('.cards-deal');
ok(await until("!document.querySelector('.cards-card').hidden && /closed/.test(document.querySelector('.cards-card').textContent)", 3000), 'a closed door says so on a card');

/* ---------------------------------------------------------------- 6. gates off */
await boot('?off=flash,spiral,brainDrain,tunnel&floor=600&script=As.9d.Kh.7c,Th.Td.7c.9s', { play: false });
await sleep(2200);
d = await dbg();
await shot('gated-off-sit-fan.png');
ok(d.table.fanCards === 13 && d.table.pictures === 0 && d.table.backs === 0 && /Sitting down at the table/.test(d.status), 'flash off: plain faces in the fan; spiral off: crosshatch backs');
await until("window.dev.station.debug().phase === 'play'", 6000);
await dealWhenReady();
await moment('cards.bloom');
await sleep(300);
d = await dbg();
await shot('gated-off-blackjack.png');
await moment('cards.win');
ok((await ev('window.dev.host.fx.length')) === 0 && d.table.glow, 'gated off: a blackjack fires no host fx; the ace glow (page) stays');
await dealWhenReady();
await decideNow();
await shot('gated-off-decision.png');
await click('.cards-move[data-move=stand]');
await moment('cards.lose');
await sleep(1500);
ok((await ev('window.dev.host.tunnel.length')) === 0 && (await ev('window.dev.host.fx.length')) === 0, 'tunnel gate off: losing edges send no tunnel');

/* ---------------------------------------------------------------- 7. Calm */
await boot('?calm&floor=600&script=As.9d.Kh.7c,Th.Td.7c.9s', { play: false });
await sleep(900);
d = await dbg();
await shot('calm-sit-fan.png');
ok(d.dress.still && d.dress.k === 0.5 && d.kit.still && d.deck.still && d.table.fanCards === 13, 'Calm: still Loom, still pictures, the fan settled in place, page strength 0.5');
await until("window.dev.station.debug().phase === 'play'", 6000);
await ev('window.dev.host.clear()');
await dealWhenReady();
await moment('cards.bloom');
await sleep(1000);
await shot('calm-blackjack-bloom.png');
fx = await fxList();
ok(fx[0].fxId === 'fx.gif_from' && fx[0].args.ms === 4000 && fx[0].args.from && fx[0].args.from.w > 0 && fx[1].args.strength === 0.8, `Calm: Normal args (the host halves), the bloom grows from the ace (${JSON.stringify(fx[0].args.from)})`);
await moment('cards.win'); fx = await fxList(); ok(fx.length === 3 && fx.every((r) => r.fired.length) && fx[2].args.color === '#5fffd0', `Calm: the win wash clears the bloom's wash gap and fires (${fx.map((r) => r.fxId + ':' + r.fired.length).join(' ')})`);
await dealWhenReady();
await decideNow();
await click('.cards-move[data-move=stand]');
await moment('cards.lose');
await sleep(1200);
await shot('calm-losing-edges.png');
ok(Math.max(...(await levels())) > 0.6, 'Calm: the tunnel breath is posted at Normal levels');

/* ---------------------------------------------------------------- 7b. OS reduced motion alone, app Motion Full */
// The host is never told about prefers-reduced-motion, so its bloom picture runs the full 4 s: the deal hold must too.
await cdp('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] });
await boot('?floor=600&script=As.9d.Kh.7c,Th.Td.7c.9s');
d = await dbg();
ok(d.dress.still && d.dress.k === 0.5, `OS reduced motion: the table is still at page strength ${d.dress.k}`);
await ev('window.dev.host.clear()');
await dealWhenReady();
await moment('cards.bloom');
const bloomAt = await ev("window.dev.station.debug().log.filter((x) => x.what === 'moment' && x.id === 'cards.bloom').at(-1).at");
fx = await fxList();
d = await dbg();
ok(fx[0] && fx[0].fxId === 'fx.gif_from' && fx[0].args.ms === 4000 && !d.controls.deal && d.screenLeftMs > 3000,
  `OS reduced motion: the host plays the bloom picture 4 s and Deal is held for all of it (${d.screenLeftMs} ms left)`);
await shot('reduced-os-bloom-deal-held.png');
await until('window.dev.station.debug().controls.deal', 8000);
const reducedHeld = await ev(`Math.round(performance.now()) - ${bloomAt}`);
ok(reducedHeld >= 3950, `OS reduced motion: Deal comes back only once the 4 s picture has gone (${reducedHeld} ms after the bloom)`);
summary.reducedOsBloomHoldMs = reducedHeld;
await cdp('Emulation.setEmulatedMedia', { features: [] });

/* ---------------------------------------------------------------- 8. live settings */
await boot('?floor=600&script=Th.9d.8c.8s');
await dealWhenReady();
await decideNow();
await ev("window.dev.settings({ gates: { spiral: false, flash: false } })");
await sleep(200);
d = await dbg();
await shot('live-gates-off.png');
ok(d.table.backs === 0 && d.table.pictures === 0, 'a settings frame dresses the table plain at once');
await ev("window.dev.settings({ intensity: 'calm' })");
await sleep(200);
d = await dbg();
ok(d.dress.still && d.kit.still, 'and a Calm frame stills the Loom');

/* ---------------------------------------------------------------- 8b. the sit latch and a settled reopen */
const slowMedia = (ms) => ev(`(() => { const m = window.dev.ctx.media; window.dev.ctx.media = (o) => new Promise((r) => setTimeout(r, ${ms})).then(() => m(o)); return true; })()`);
await boot('?floor=600&script=Th.9d.8c.8s,Th.9d.8c.8s');
await dealWhenReady();
await decideNow();
await click('.cards-move[data-move=stand]');
await settledNow();
await until("window.dev.station.debug().controls.deal && window.dev.station.debug().controls.sit", 8000);
await slowMedia(700);
const deals0 = await ev("window.dev.server.log.filter((l) => l.op === 'deal').length");
const latch = await ev(`(() => { document.querySelector('.cards-sit').click(); const a = window.dev.station.debug();
  document.querySelector('.cards-deal').click(); document.querySelector('.cards-sit').click(); const b = window.dev.station.debug();
  return { phase: a.phase, seating: a.seating, deal: a.controls.deal, sit: a.controls.sit, sitting: b.sitting, moves: Object.values(b.controls.moves).some(Boolean) }; })()`);
ok(latch.phase === 'sit' && latch.seating && !latch.deal && !latch.sit && !latch.moves && latch.sitting === 2, `Sit latches on the press, before the deck is in: Deal, the moves and a second Sit refused (${JSON.stringify(latch)})`);
await until("window.dev.station.debug().phase === 'play'", 10000);
await sleep(300);
const sent0 = { deals: await ev("window.dev.server.log.filter((l) => l.op === 'deal').length"), media: await ev('window.dev.host.media.length') };
ok(sent0.deals === deals0 && sent0.media === 2, `no deal went out during the sit, and one 13-picture request, not two (${JSON.stringify(sent0)})`);
d = await dbg();
await shot('resit-latched-ready.png');
ok(d.sitting === 2 && !d.shown && !(d.state.hand && !d.state.hand.done) && !d.moments.held && /Pick a bet and deal/.test(d.status) && d.controls.deal, `after the sit: an empty table ready to deal, nothing held ("${d.status}")`);
await ev('window.dev.stand()');
await ev('window.dev.open()');
await until("window.dev.station.debug().phase === 'play'", 12000);
await ev('new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)))');   // afterSit queues the hand; the next frame lays it down
d = await dbg();
await shot('reopen-finished-hand-settled.png');
ok(d.shown && d.shown.done && d.queue === 0 && d.table.cards.length === 4 && d.table.cards.every((c) => c.landed && c.face) && d.controls.deal,
  `reopen with a finished last hand: it lies settled on the first frame, Deal live (${d.table.cards.length} cards)`);

/* ---------------------------------------------------------------- 8c. suspend keeps the deck; a suspended moment holds nothing */
await boot('?floor=600&script=Th.9d.8c.8s,Th.9d.7c.Ts');
await dealWhenReady();
await decideNow();
let sus = { media: await ev('window.dev.host.media.length'), keys: (await dbg()).deck.keys.join() };
await ev('window.dev.station.suspend(true)');
await sleep(300);
await ev('window.dev.station.suspend(false)');
await sleep(400);
d = await dbg();
ok((await ev('window.dev.host.media.length')) === sus.media && d.deck && d.deck.keys.join() === sus.keys && d.decide && d.moments.held,
  `suspend keeps the deck: no new deal (${sus.media} media requests), the same 13 keys, the decision still open and held`);
await click('.cards-move[data-move=stand]');
await settledNow();
await dealWhenReady();
await decideNow();
await ev('window.dev.host.clear()');
await mark();
await click('.cards-move[data-move=stand]');
await moment('cards.lose');
await sleep(500);
const heldBefore = (await dbg()).screenLeftMs;
await ev('window.dev.station.suspend(true)');
await sleep(200);
d = await dbg();
ok(heldBefore > 0 && d.screenLeftMs === 0 && (await levels()).at(-1) === 0, `suspend mid-breath: the tunnel goes to 0 and the deal hold with it (${heldBefore} ms -> ${d.screenLeftMs})`);
await ev('window.dev.station.suspend(false)');
const mediaBeforeLeave = await ev('window.dev.host.media.length');
await ev('window.dev.stand()');
await ev('window.dev.open()');
await until("window.dev.station.debug().phase === 'sit' || window.dev.station.debug().phase === 'play'", 6000);
ok((await ev('window.dev.host.media.length')) === mediaBeforeLeave + 1 && (await ev('window.dev.host.media.at(-1).count')) === 13,
  'leaving the station and sitting down again re-deals the 13 pictures');

/* ---------------------------------------------------------------- 9. through the room */
const PICS = ['/backroom/stations/slot/fallback/gif0.webp', '/dtrh/assets/bubbles/effects/spirals/sp6.gif', '/arcademy/art/bugle/g1.webp', '/backroom/stations/slot/fallback/gif1.webp',
  '/backroom/room/assets/ads/dtrh.webp', '/arcademy/art/bugle/g2.webp', '/backroom/stations/slot/fallback/gif2.webp', '/dtrh/assets/bubbles/effects/spirals/sp7.gif',
  '/arcademy/art/bugle/g3.webp', '/backroom/stations/slot/fallback/gif3.webp', '/backroom/room/assets/ads/arcademy.webp', '/arcademy/art/bugle/g4.webp', '/backroom/room/assets/ads/focus-gaze.webp'];
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const PICS = ${JSON.stringify(PICS)};
  window.__posted = [];
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
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until("document.documentElement.classList.contains('br-ready')", 45000), 'the room boots');
await ev("window.__backroom.visit(window.__backroom.stations.find((s) => s.id === 'cards')); true");
ok(await until("!!document.querySelector('.cards-station')", 8000), 'E on Soft Hand mounts the station from stations.json');
await sleep(2000);
await shot('room-cards-sit-fan.png');
ok(await until("document.querySelector('.cards-station') && document.querySelector('.cards-station').dataset.phase === 'play'", 10000), 'the sit fan ends at the table');
const posted = await ev("window.__posted.filter((m) => ['station-open', 'media-request', 'station-request'].includes(m.type)).map((m) => ({ type: m.type, station: m.station, count: m.count, op: m.op }))");
ok(posted.some((m) => m.type === 'station-open' && m.station === 'cards') && posted.some((m) => m.type === 'media-request' && m.station === 'cards' && m.count === 13)
  && posted.some((m) => m.type === 'station-request' && m.station === 'cards' && m.op === 'state'), 'station-open, a 13-picture media-request and GET state, all as cards');
ok(await ev("document.querySelector('.cards-back').hidden && document.querySelector('.cards-station').hasAttribute('data-host-sp')"), 'hostBack: the room\'s Back and SP chip only');
await click('.cards-deal');
await until("!!document.querySelector('.cards-move[data-move=stand]') && !document.querySelector('.cards-move[data-move=stand]').disabled", 8000);
await shot('room-cards-decision.png');
ok((await ev("document.querySelector('#br-sp-value').textContent")) === '55' && (await ev("window.__posted.filter((m) => m.type === 'fx').length")) === 0, 'the room chip shows 55 after the stake; no fx while deciding');
await click('.cards-move[data-move=stand]');
await until("/beats/.test(document.querySelector('.cards-status').textContent)", 8000);
ok((await ev("document.querySelector('#br-sp-value').textContent")) === '59' && (await ev("window.__posted.some((m) => m.type === 'fx' && m.fxId === 'fx.wash' && m.station === 'cards')")), 'the win lands on the room chip (59) and fires fx.wash as cards');
await ev('window.__backroom.back()');
ok(await until("!document.querySelector('.cards-station') && window.__posted.some((m) => m.type === 'station-close' && m.station === 'cards')", 3000), 'Back closes the station and posts station-close');
ok((await ev("document.querySelector('#br-sp-value').textContent")) === '59', 'the chip keeps 59 after Back');
await shot('room-after-back.png');

await writeFile(join(OUT, 'cards-check.json'), JSON.stringify(summary, null, 2));
ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
console.log(fails ? `\n${fails} FAILED` : '\nall cards checks passed');
await done(fails ? 1 : 0);
