/* ============================================================================
 * race/smoke/subliminal-check.mjs - THE WHISPER, COMING AT YOU.
 *
 *   node race/smoke/subliminal-check.mjs        (from Resources/web/dtrh; 0 on pass)
 *
 * Two halves, the same shape as word-flash-check.mjs. The first is pure: race/subliminal.js
 * fitPx is asked for the sizes at the two viewports that matter, and the eight word phrase
 * on a 390 px phone is held against the width it actually has.
 *
 * The second drives a headless browser through a real run, because the four things this
 * change can get wrong that no unit test sees are "the race card never reached the DOM",
 * "it is drawn at the HUD's own size after all", "two pops fight over the middle of the
 * screen" and "it is still there a second after it should have let go". It also builds the
 * TUBE's own `.sf-pfx-sub` card on the same page and measures it, which is the guard that
 * race.css never leaks onto dtrh.html: that one must still be 9vmin of white.
 *
 * It never prints a line of a transcript; the only phrases below are its own fixtures.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fitPx, LIFE_MS, MIN_SHOW_MS } from '../subliminal.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web

/** The tube's card is `font-size: 9vmin` (styles.css .sf-pfx-word) - the size this replaces. */
const TUBE_PX = (w, h) => 0.09 * Math.min(w, h);
const SHORT = 'good girl';
const LONG = 'every thought you have belongs to the panel';   // eight words, the phone's worst case

/* ============================================================================
 * 1. the sizes, before the browser is ever started
 * ==========================================================================*/
const deskShort = fitPx(SHORT, 1280, 720), deskTube = TUBE_PX(1280, 720);
const phoneShort = fitPx(SHORT, 390, 844), phoneTube = TUBE_PX(390, 844);
const phoneLong = fitPx(LONG, 390, 844);
console.log(`  --  1280x720: ${Math.round(deskTube)}px -> ${Math.round(deskShort)}px    390x844: ${Math.round(phoneTube)}px -> ${Math.round(phoneShort)}px (8 words: ${Math.round(phoneLong)}px)`);
ok(deskShort >= 2.4 * deskTube, `a short phrase on a desktop is ${(deskShort / deskTube).toFixed(1)}x the card it replaces`);
ok(phoneShort >= 2 * phoneTube, `and ${(phoneShort / phoneTube).toFixed(1)}x on a phone, where the old one was barely a caption`);
// the eight word cap: its LONGEST word has to sit inside 86% of a 390 px phone, or it goes off both sides
const longest = LONG.split(' ').reduce((n, s) => Math.max(n, s.length), 0);
ok(phoneLong * 0.56 * longest <= 390 * 0.87, `an eight word phrase steps down to ${Math.round(phoneLong)}px, which fits "${'x'.repeat(longest)}" across the glass`);
ok(phoneLong >= 21, 'and never below the floor, so a long phrase is still a payload and not fine print');
ok(fitPx('', 1280, 720) === 0 && fitPx(null, 1280, 720) === 0, 'no phrase, no size');
ok(LIFE_MS >= 1100 && LIFE_MS <= 1400, `the whole card is ${LIFE_MS} ms (the rush, the hold, and past the POV)`);
ok(MIN_SHOW_MS > 0 && MIN_SHOW_MS < LIFE_MS, `and a card cannot be pushed off inside its first ${MIN_SHOW_MS} ms`);
// the presentation is the RACE's alone: run.js is the only file that passes payloadFx the seam
const pfx = readFileSync(resolve(RACE, '../game/payloadFx.js'), 'utf8');
const run = readFileSync(resolve(RACE, 'run.js'), 'utf8');
const css = readFileSync(resolve(RACE, 'race.css'), 'utf8');
ok(/subliminalFx\s*=\s*null/.test(pfx), 'payloadFx defaults the seam to null, so the tube passes nothing and keeps the blip');
ok(/createSubliminal\(hudRoot,\s*\{\s*reducedMotion\s*\}\)/.test(run), 'run.js hands the card the run\'s own reducedMotion');
// comments stripped first: this file TALKS about the tube's card at length, it just never selects it
ok(!/sf-pfx-sub|sf-pfx-word/.test(css.replace(/\/\*[\s\S]*?\*\//g, '')),
  'and no RULE in race.css names the tube\'s card, so dtrh.html cannot be restyled from here');

/* ============================================================================
 * the browser
 * ==========================================================================*/
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8874, DEBUG_PORT = 9344;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-subl-'));
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=1280,720', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push('thrown: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text || '?'));
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push(m.params.args.map((a) => a.value ?? a.description ?? '?').join(' '));
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
/** For an expression that ALREADY returns a JSON string (the readers below). */
const parse = async (x) => JSON.parse(await ev(x));
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });

const site = `http://127.0.0.1:${PORT}`;
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0` });
let up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.perf().running)`);
}
ok(up, 'the page boots and the run is going');
if (!up) { if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | ')); await done(1); }

/** Read the live card: what it is set in, how big, and which keyframes are moving it. */
const CARD = `(() => {
  const el = document.querySelector('.rh-sub-card');
  const layer = document.querySelector('.rh-sub-layer');
  if (!el || !layer) return JSON.stringify({ there: false });
  const cs = getComputedStyle(el), ls = getComputedStyle(layer);
  const r = el.getBoundingClientRect();
  return JSON.stringify({ there: true, on: layer.classList.contains('is-on'), text: el.textContent,
    px: parseFloat(cs.fontSize), weight: cs.fontWeight, color: cs.color, anim: cs.animationName,
    dur: cs.animationDuration, z: ls.zIndex, w: Math.round(r.width), cards: document.querySelectorAll('.rh-sub-card').length });
})()`;

/* ============================================================================
 * 2. one pop: the card is there, it is BIG, and it is the rush and not a label
 * ==========================================================================*/
ok(await ev(`window.__race.race.debugPayload('subliminal', { text: ${JSON.stringify(SHORT)} })`), 'a subliminal payload fires');
await sleep(400);   // the readable hold
const c = await parse(CARD);
ok(c.there && c.on, 'the race card is on the glass');
eq(c.text, SHORT, 'wearing the phrase it was handed');
eq(c.cards, 1, 'and there is exactly one card element, ever');
ok(c.px >= 150, `set at ${Math.round(c.px)}px at 1280x720 (the floor this check holds is 150)`);
ok(Math.abs(c.px - deskShort) < 2, `which is the size race/subliminal.js asked for (${Math.round(deskShort)}px)`);
ok(c.px >= 2.4 * deskTube, `${(c.px / deskTube).toFixed(1)}x the ${Math.round(deskTube)}px card it replaces`);
ok(c.w > 1280 * 0.5, `and it fills ${Math.round((c.w / 1280) * 100)}% of the glass, so it is not a caption`);
eq(c.weight, '900', 'heavy, where the chrome is 700/800');
ok(c.color !== 'rgb(255, 255, 255)', `and cream rather than the chrome's white (${c.color})`);
eq(c.anim, 'rhSubRush', 'the motion is the rush at the POV');
ok(parseFloat(c.dur) * 1000 >= 1100 && parseFloat(c.dur) * 1000 <= 1400, `over ${c.dur}, so it is a beat and not a hold`);

/* ============================================================================
 * 3. the tube's own card, on this very page: still 9vmin of white
 * ==========================================================================*/
const tube = await parse(`(() => {
  const root = document.querySelector('.sf-pfx') || document.body;
  const el = document.createElement('div');
  el.className = 'sf-pfx-sub sf-pfx-word';
  el.textContent = ${JSON.stringify(SHORT)};
  root.appendChild(el);
  const cs = getComputedStyle(el);
  const got = { px: parseFloat(cs.fontSize), color: cs.color, anim: cs.animationName };
  el.remove();
  return JSON.stringify(got);
})()`);
ok(Math.abs(tube.px - deskTube) < 0.5, `the tube's .sf-pfx-sub is still ${Math.round(tube.px)}px (9vmin), untouched by race.css`);
eq(tube.color, 'rgb(255, 255, 255)', 'still white');
eq(tube.anim, 'sf-pfx-sub', 'and still on its own keyframes');

/* ============================================================================
 * 4. z: over the payload layers, under the countdown and the shutter
 * ==========================================================================*/
const z = await parse(`(() => {
  const zOf = (sel) => { const e = document.querySelector(sel); return e ? parseInt(getComputedStyle(e).zIndex, 10) : null; };
  return JSON.stringify({ sub: zOf('.rh-sub-layer'), hud: zOf('.sf-hud'), count: zOf('.rh-count'), shutter: zOf('.rh-shutter'), chrome: zOf('.rh-chrome') });
})()`);
console.log('  --  z: ' + JSON.stringify(z));
ok(z.sub > z.hud, `over every payloadFx layer (.sf-hud is ${z.hud})`);
ok(z.sub > z.chrome, 'and over the HUD chrome');
ok(z.count == null || z.sub < z.count, 'under the countdown, which never gets covered');
ok(z.shutter == null || z.sub < z.shutter, 'and under the shutter');

/* ============================================================================
 * 5. it lets go: nothing is left on the glass 2 s later
 * ==========================================================================*/
await sleep(2000 - 400);
const after = await parse(CARD);
ok(!after.on, 'the card has let go of the glass');
ok(await ev(`getComputedStyle(document.querySelector('.rh-sub-card')).opacity === '0'`), 'and it is drawing nothing at all');
eq(after.cards, 1, 'still one element: the card is reused, never piled up');

/* ============================================================================
 * 6. two pops on each other's heels: one queues, they never fight
 * ==========================================================================*/
await ev(`window.__race.race.subliminalStats()`);   // the counters carry the run; the deltas are what matter
const base = await json(`window.__race.race.subliminalStats()`);
await ev(`(() => { window.__race.race.debugPayload('subliminal', { text: 'sink' }); window.__race.race.debugPayload('subliminal', { text: 'deeper' }); return true; })()`);
await sleep(200);
const mid = await parse(CARD);
eq(mid.cards, 1, 'two pops inside the hold, and still exactly one card on the glass');
eq(mid.text, 'sink', 'the first one keeps its readable moment');
const q = await json(`window.__race.race.subliminalStats()`);
eq(q.queued - base.queued, 1, 'the second is queued, not thrown away');
await sleep(MIN_SHOW_MS);
const late = await parse(CARD);
eq(late.text, 'deeper', 'and it takes the card over once the first has been read');
eq(late.cards, 1, 'still one');
eq((await json(`window.__race.race.subliminalStats()`)).shown - base.shown, 2, 'both were shown, one after the other');

/* ============================================================================
 * 7. reduced motion: nothing comes at you
 * ==========================================================================*/
await sleep(LIFE_MS);
await cdp('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] });
await ev(`window.__race.race.debugPayload('subliminal', { text: ${JSON.stringify(SHORT)} })`);
await sleep(400);
const rm = await parse(CARD);
eq(rm.anim, 'rhSubHold', 'the card fades in place instead of rushing');
ok(rm.px >= 150, `at the same ${Math.round(rm.px)}px: reduced motion loses the zoom, never the size`);
await cdp('Emulation.setEmulatedMedia', { features: [] });

/* ============================================================================
 * 8. and nothing said a word about it
 * ==========================================================================*/
ok(errs.length === 0, 'not one console error in the whole run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\nsubliminal-check: all good');
  process.exit(code);
}
