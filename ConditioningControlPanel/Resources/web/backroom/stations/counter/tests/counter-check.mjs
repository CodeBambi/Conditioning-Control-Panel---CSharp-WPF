/* ============================================================================
 * counter-check.mjs - the counter page in headless Chrome, driven over CDP on dev.html and mock-server.js.
 *
 *   node backroom/stations/counter/tests/counter-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/ is served on 127.0.0.1 and the mock answers every request. The only
 * process this stops is the Chrome it started, by its own handle. Stills: all soon, mixed faces, confirm open,
 * success, refusals (insufficient, busy, catalog_changed), reduced motion, the plate fallback, closed, phone width.
 * CHROME: CHROME_PATH, else the usual Windows install. COUNTER_PORT default 8899 (debug +500).
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
const RES = resolve(fileURLToPath(import.meta.url), '../../../../../..');   // ConditioningControlPanel/Resources
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.COUNTER_PORT || 8899), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(RES, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-counter-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', 'about:blank'], { stdio: 'ignore' });
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
const cdp = (method, params) => new Promise((r) => { const i = ++msgId; waits.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable');
const size = (width, height) => cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: width < 500 });
await size(1280, 720);

const shots = [];
async function still(name, why) {
  await sleep(250);
  await writeFile(join(OUT, name), Buffer.from((await cdp('Page.captureScreenshot', { format: 'png' })).result.data, 'base64'));
  shots.push({ name, why }); console.log('  shot  ' + name);
}
async function until(expr, ms = 8000) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(40); } return false; }
async function boot(query) {
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/web/backroom/stations/counter/dev.html${query}` });
  for (let i = 0; i < 100 && !(await ev('!!(window.dev && window.dev.station)')); i++) await sleep(100);
  await ev('window.dev.open()');
  await until("['ready','closed'].includes(window.dev.station.debug().phase)");
}
const faces = () => ev("Array.from(document.querySelectorAll('.counter-card')).map(c => c.dataset.id + ':' + c.dataset.face)");
const click = (id, cls) => ev(`document.querySelector('.counter-card[data-id="${id}"] .${cls}').click()`);
const cardText = (id) => ev(`document.querySelector('.counter-card[data-id="${id}"]').innerText`);
const summary = { shots };

// 1. Everything ships OFF: eight dust sheets, art loaded, standalone Back and SP chip.
await boot('?sp=812&latency=60');
let f = await faces();
ok(f.length === 8 && f.every((x) => x.endsWith(':soon')), 'all soon: ' + f.join(' '));
ok(await until("Array.from(document.querySelectorAll('.counter-art img')).every(i => i.complete && i.naturalWidth === 320)"), 'eight 320 px stills decoded');
ok(await ev("!document.querySelector('.counter-back').hidden && document.querySelector('.counter-sp').textContent === '812 SP'"), 'standalone: own Back, own chip 812 SP');
ok(await ev("document.querySelectorAll('.counter-card[data-id^=rt_] .counter-note').length === 4"), 'RT cards carry the note');
await still('01-all-soon.png', 'every row soon (BACKROOM_COUNTER_ON unset)');

// 2-4. In the room (hook): mixed faces, confirm, success.
await boot('?sp=230&on=jackpot_remix,rt_demo,high_roller,flashes_v2&owned=jackpot_remix&nodiscord&hook&latency=400');
f = await faces();
ok(f.join(' ') === 'jackpot_remix:owned rt_demo:buy high_roller:discord flashes_v2:short bubbles_v2:soon rt_bundle_1:soon rt_bundle_2:soon rt_bundle_3:soon', 'mixed faces ' + f.join(' '));
ok(/Short by 10/.test(await cardText('flashes_v2')) && /Link Discord first/.test(await cardText('high_roller')), 'Short by 10 and Link Discord first');
ok(await ev("document.querySelector('.counter-back').hidden && getComputedStyle(document.querySelector('.counter-sp')).display === 'none'"), 'hostBack: no station Back, no station chip');
await still('02-mixed.png', 'owned, buy, link Discord first, short by N, soon (room mode)');
await click('rt_demo', 'counter-buy');
ok(/Balance after: 210/.test(await cardText('rt_demo')) && (await ev("document.activeElement.className")) === 'counter-yes', 'confirm open, balance after 210, focus on Confirm');
await still('03-confirm.png', 'inline confirm: name, price, balance after');
await click('rt_demo', 'counter-yes');
await sleep(80);
ok(await ev("document.querySelector('.counter-card[data-id=rt_demo] .counter-yes').getAttribute('aria-busy') === 'true'"), 'Confirm shows pending');
ok(await until("document.querySelector('.counter-card[data-id=rt_demo]').dataset.face === 'owned'"), 'the card flips to Owned');
const chipLog = await ev('window.dev.chipLog');
ok(JSON.stringify(chipLog) === '[["set",210],["set",null],"thud"]', 'spReadout set(210), set(null), thud: ' + JSON.stringify(chipLog));
ok(await ev("document.querySelector('.counter-card[data-id=rt_demo]').classList.contains('is-flip')"), 'flip animation (not still)');
await still('04-success.png', 'rt_demo owned, room chip at 210');

// 5-7. Refusals.
await boot('?sp=500&on=*&latency=60');
await click('flashes_v2', 'counter-buy');
await ev('window.dev.server.setSp(100)');
await click('flashes_v2', 'counter-yes');
ok(await until("document.querySelector('.counter-card[data-id=flashes_v2]').dataset.face === 'short'"), 'insufficient: the card repaints as Short by 140');
await still('05-refusal-insufficient.png', 'insufficient: confirm closed, Short by 140 from a fresh state');
await ev('window.dev.server.setSp(500)');
await ev('window.dev.station.debug()');
await boot('?sp=500&on=*&latency=60');
await click('high_roller', 'counter-buy');
await ev("window.dev.server.fail('buy', 'busy')");
await click('high_roller', 'counter-yes');
ok(await until("!!document.querySelector('.counter-card[data-id=high_roller] .counter-retry')"), 'busy: confirm stays with the retry line');
await still('06-refusal-busy.png', 'busy: confirm open, retry line');
await click('high_roller', 'counter-yes');
ok(await until("document.querySelector('.counter-card[data-id=high_roller]').dataset.face === 'owned'"), 'retry with the same idem buys');
const idems = await ev("window.dev.sent.filter(m => m.op === 'buy').map(m => m.idem)");
ok(idems.length === 2 && idems[0] === idems[1], 'one idem per confirm, reused on retry');
ok(/on your Discord account/.test(await cardText('high_roller')), 'High Roller delivery line');
await sleep(300);   // the follow-up state read settles first
await click('bubbles_v2', 'counter-buy');
await ev("window.dev.server.reprice('bubbles_v2', 300)");
await click('bubbles_v2', 'counter-yes');
ok(await until("window.dev.sent.filter(m => m.op === 'buy').length === 3 && /Balance after: 160/.test(document.querySelector('.counter-card[data-id=bubbles_v2]').innerText)"), 'catalog_changed: asks again at 300 (460 - 300)');
const buys = await ev("window.dev.sent.filter(m => m.op === 'buy').map(m => m.body.prizeId + ':' + (m.result.body && (m.result.body.reason || 'ok')))");
ok(buys.length === 3 && (await ev('window.dev.server.user.sp')) === 460, 'never auto-buys at the new price: ' + buys.join(' '));
await still('07-refusal-catalog-changed.png', 'catalog_changed: the confirm asks again at the new price');

// 8. Reduced motion: no tilt, no flip, instant swap.
await boot('?sp=500&on=*&reduced&latency=60');
ok(await ev("document.querySelector('.counter-station').dataset.still === '' && getComputedStyle(document.querySelector('.counter-card')).transform === 'none'"), 'reduced: still, no card transform');
await click('jackpot_remix', 'counter-buy');
await click('jackpot_remix', 'counter-yes');
ok(await until("document.querySelector('.counter-card[data-id=jackpot_remix]').dataset.face === 'owned'"), 'reduced: owned');
ok(await ev("!document.querySelector('.counter-card[data-id=jackpot_remix]').classList.contains('is-flip') && getComputedStyle(document.querySelector('.counter-card[data-id=jackpot_remix]')).animationName === 'none'"), 'reduced: no flip');
await click('rt_demo', 'counter-buy');
await still('08-reduced.png', 'reduced motion: owned without a flip, confirm open');
await boot('?sp=500&on=*&calm&latency=60');
ok(await ev("document.querySelector('.counter-station').dataset.still === ''"), 'Calm is still too');

// 9-11. Plate fallback, closed, Back during a buy, phone width.
await boot('?sp=812&on=*&noart&latency=60');
ok(await until("document.querySelectorAll('.counter-art[data-plate]').length === 8"), 'missing art: eight CSS plates');
await still('09-plate-fallback.png', 'CSS plate with the name when the art is missing');
await boot('?closed');
ok(await ev("window.dev.station.debug().phase === 'closed' && !document.querySelector('.counter-closed').hidden"), 'a failed state shows closed and Back');
await still('10-closed.png', 'closed');
await boot('?sp=500&on=*&latency=1200');
await click('rt_demo', 'counter-buy');
await click('rt_demo', 'counter-yes');
const t0 = Date.now();
await ev("document.querySelector('.counter-back').click()");
ok(await until("!document.getElementById('sit').hidden && !document.querySelector('.counter-station')", 400) && Date.now() - t0 < 400, `Back during an in-flight buy leaves at once (${Date.now() - t0} ms)`);
await sleep(1500);
ok((await ev('window.dev.server.user.sp')) === 480, 'the buy still settled on the server');
await ev('window.dev.open()');
ok(await until("document.querySelector('.counter-card[data-id=rt_demo]')?.dataset.face === 'owned'"), 'reopen shows it owned');
await size(400, 800);
await boot('?sp=260&on=jackpot_remix,rt_demo,flashes_v2&latency=60');
ok(await ev('document.documentElement.scrollWidth <= 400'), 'phone width: no sideways scroll');
await still('11-phone.png', 'phone width, two columns');

ok(errs.length === 0, 'no page errors' + (errs.length ? ': ' + errs.join(' | ') : ''));
summary.errors = errs; summary.fails = fails;
await writeFile(join(OUT, 'counter-check.json'), JSON.stringify(summary, null, 2));
console.log(fails ? `\n${fails} FAILED` : '\nALL PASS');
await done(fails ? 1 : 0);
