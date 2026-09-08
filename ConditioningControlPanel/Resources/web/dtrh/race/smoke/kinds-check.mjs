/* ============================================================================
 * race/smoke/kinds-check.mjs - the effect kinds the Bambi Sleep survey added.
 *
 *   node race/smoke/kinds-check.mjs        (from Resources/web/dtrh; 0 on pass)
 *
 * Two halves, the same shape as subliminal-check.mjs. The first is pure: the new rows of
 * race/bubbleKinds.js are held against what they were commissioned to be (a payload that
 * exists, a THE MIX slot that exists, the weights the owner asked for), and cocktail.js is
 * asked whether every recipe still resolves now that there is one more category.
 *
 * The second drives a headless browser through a real run and FIRES EACH KIND BY HAND
 * (`__race.race.debugPayload`, which never touches THE MIX), because the things this change
 * can get wrong that no unit test sees are "the layer never reached the DOM", "the gif wash
 * is as timid as the drain it was supposed to stop being" and "it is still on the glass a
 * second after it should have let go".
 *
 * THE ROAD IS ALSO DRIVING. The run pops its own bubbles while this runs, and one of those
 * can refresh the very hold being measured. `debugPayload` is not counted by `fxStats()` and
 * a road pop is, so every window below is bracketed by that tally and retried if the road
 * poured something into it.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { BUBBLE_KINDS, KIND_BY_ID, rollKind } from '../bubbleKinds.js';
import { CATEGORIES, RECIPES, categoryOf } from '../cocktail.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web

/* ============================================================================
 * 1. the rows, before the browser is ever started
 * ==========================================================================*/
/** id -> what it was commissioned to be. The ids are fixed: the trigger catalogue names them. */
const WANTED = {
  gifwash:  { payload: 'gifWash',  category: 'wash',    weight: [6, 8],   points: 20, minIntensity: [0, 0.2] },
  blackout: { payload: 'blackout', category: 'overlay', weight: [1, 2.5], points: 30, minIntensity: [0.4, 0.6] },
};
for (const [id, want] of Object.entries(WANTED)) {
  const k = KIND_BY_ID[id];
  ok(!!k, `bubbleKinds.js has a \`${id}\` row`);
  if (!k) continue;
  eq(k.kind, 'effect', `${id} is an effect`);
  eq(k.payload, want.payload, `${id} fires the ${want.payload} payload`);
  eq(categoryOf(id), want.category, `${id} lands in THE MIX slot '${want.category}'`);
  eq(k.points, want.points, `${id} pays ${want.points}`);
  ok(k.weight >= want.weight[0] && k.weight <= want.weight[1], `${id} rolls at weight ${k.weight}`);
  ok(k.minIntensity >= want.minIntensity[0] && k.minIntensity <= want.minIntensity[1], `${id} is gated at intensity ${k.minIntensity}`);
  ok(k.spawn !== false, `${id} actually spawns`);
  ok(typeof k.label === 'string' && k.label.length > 0, `${id} wears a plate glyph (${k.label})`);
  ok(/^\/dtrh\/assets\//.test(k.sprite), `${id} has a page-relative sprite (${k.sprite.split('/').pop()})`);
}
// the owner asked for the gif wash OFTEN: level with the two effect kinds that already are
const w = (id) => KIND_BY_ID[id].weight;
ok(w('gifwash') >= w('pink') && w('gifwash') >= w('subliminal'),
  `the wash rolls at least as often as pink (${w('pink')}) and the subliminal (${w('subliminal')})`);
ok(w('blackout') < w('braindrain'), `and the blackout stays rare (${w('blackout')} against the drain's ${w('braindrain')})`);
// no two rows share a sprite file, so a new kind is never the old kind wearing its face
const sprites = BUBBLE_KINDS.filter((k) => k.spawn !== false).map((k) => k.sprite);
eq(new Set(sprites).size, sprites.length, 'every spawning row has a sprite of its own');

// the new MIX slot, and the promise that came with it
ok(!!CATEGORIES.wash, "cocktail.js has a 'wash' slot");
eq(CATEGORIES.wash.mode, 'replace', 'the wash slot replaces rather than stacks');
ok(!RECIPES.some((r) => r.needs.includes('wash')), 'no recipe needs it, so every recipe resolves off what it always did');
for (const r of RECIPES) ok(r.needs.every((c) => CATEGORIES[c]), `recipe ${r.id} still names live categories`);

// a deep run can still roll the deep kinds, and a shallow one cannot roll the blackout
const rolled = (intensity, n) => { const s = new Set(); for (let i = 0; i < n; i++) s.add(rollKind(intensity).id); return s; };
ok(!rolled(0.3, 4000).has('blackout'), 'a shallow run never rolls a blackout');
ok(rolled(0.9, 4000).has('blackout') && rolled(0.9, 4000).has('gifwash'), 'a deep one rolls both');

/* ============================================================================
 * the browser
 * ==========================================================================*/
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8881, DEBUG_PORT = 9351;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.gif': 'image/gif', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

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

const prof = mkdtempSync(join(tmpdir(), 'race-kinds-'));
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

/** One layer, read off the live DOM: is it there, what is it drawing, what is moving it. */
const LAYER = (sel) => `(() => {
  const el = document.querySelector(${JSON.stringify(sel)});
  if (!el) return JSON.stringify({ there: false });
  const cs = getComputedStyle(el);
  return JSON.stringify({ there: true, n: document.querySelectorAll(${JSON.stringify(sel)}).length,
    opacity: parseFloat(cs.opacity), anim: cs.animationName, cls: el.className,
    img: (el.style.backgroundImage || '').slice(0, 120), blur: cs.backdropFilter,
    z: parseInt(getComputedStyle(el.parentElement).zIndex, 10) });
})()`;

/** The road's own pours, so a window that one of them landed in can be thrown away and retried. */
const FX = `window.__race.race.fxStats()`;
/** Run `fn` and hand back its value only if the ROAD poured nothing while it ran. */
async function isolated(what, fn) {
  for (let attempt = 1; attempt <= 4; attempt++) {
    const before = JSON.stringify(await json(FX));
    const got = await fn();
    if (JSON.stringify(await json(FX)) === before) return got;
    console.log(`  --  the road poured something during "${what}", attempt ${attempt}, going again`);
  }
  ok(false, `${what}: the road never left a quiet window (four tries)`);
  return null;
}

/* ============================================================================
 * 2. gifwash: a gif takes the screen, STRONGLY, and lets go
 * ==========================================================================*/
// strength 45, durMult 1 -> payloadFx: opacity 0.55 + 0.25*0.45 = 0.66, hold 2175 ms
const WASH_HOLD = 2175;
const wash = await isolated('the gif wash', async () => {
  await ev(`window.__race.race.debugPayload('gifWash', { strength: 45 })`);
  await sleep(500);
  const peak = await parse(LAYER('.sf-pfx-gifwash'));
  await sleep(WASH_HOLD + 900 - 500);
  return { peak, after: await parse(LAYER('.sf-pfx-gifwash')) };
});
if (wash) {
  ok(wash.peak.there, 'the wash layer reached the DOM');
  eq(wash.peak.n, 1, 'and there is exactly one of it, ever');
  ok(wash.peak.opacity >= 0.5, `it is drawing at ${wash.peak.opacity.toFixed(2)}, so it reads as a gif and not as the drain (the floor is 0.50)`);
  ok(/url\(/.test(wash.peak.img), 'with a picture in it');
  ok(wash.peak.blur === 'none' || !/blur/.test(wash.peak.blur), `no backdrop blur: that is braindrain's thing (${wash.peak.blur})`);
  eq(wash.peak.anim, 'sf-pfx-wash', 'and the light shudder is running');
  eq(wash.after.opacity, 0, 'it has let go of the glass after its hold');
}

/* ============================================================================
 * 3. blackout: the cut, the hold, and the drain underneath on the way back
 * ==========================================================================*/
// strength 45 -> hold 735 ms, then a 1 s release with showBraindrain behind it
const black = await isolated('the blackout', async () => {
  await ev(`window.__race.race.debugPayload('blackout', { strength: 45 })`);
  await sleep(300);
  const cut = await parse(LAYER('.sf-pfx-black'));
  await sleep(700);   // 1 s in: the 735 ms hold has just let go and the drain is coming up behind it
  const back = await parse(LAYER('.sf-pfx-drain'));
  await sleep(1600);
  return { cut, back, after: await parse(LAYER('.sf-pfx-black')) };
});
if (black) {
  ok(black.cut.there, 'the black card reached the DOM');
  eq(black.cut.n, 1, 'one card, reused');
  eq(black.cut.opacity, 1, 'the screen is all the way out');
  ok(/is-cut/.test(black.cut.cls), 'on the 120 ms cut transition, not the layer\'s own 450 ms ease');
  ok(black.back.there && black.back.opacity > 0.1, `the braindrain blur is fading back in underneath (${black.back.opacity})`);
  eq(black.after.opacity, 0, 'and the black has let go a second and a half later');
  ok(!/is-cut/.test(black.after.cls), 'with the hard cut taken off it, so the release had its full second');
}

/* ============================================================================
 * 4. z: both layers live under the countdown and the End card
 * ==========================================================================*/
const z = await parse(`(() => {
  const zOf = (sel) => { const e = document.querySelector(sel); return e ? parseInt(getComputedStyle(e).zIndex, 10) : null; };
  return JSON.stringify({ pfx: zOf('.sf-pfx'), count: zOf('.rh-count'), shutter: zOf('.rh-shutter') });
})()`);
console.log('  --  z: ' + JSON.stringify(z));
ok(z.count == null || z.pfx < z.count, 'the payload layers sit under the countdown, which never gets covered');
ok(z.shutter == null || z.pfx < z.shutter, 'and under the shutter');

/* ============================================================================
 * 5. reduced motion: the wash still washes, it just stops breathing
 * ==========================================================================*/
await cdp('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] });
await ev(`window.__race.race.debugPayload('gifWash', { strength: 45 })`);
await sleep(400);
const rm = await parse(LAYER('.sf-pfx-gifwash'));
eq(rm.anim, 'none', 'no shudder under reduced motion');
ok(rm.opacity >= 0.5, `at the same ${rm.opacity.toFixed(2)}: reduced motion loses the movement, never the picture`);
await cdp('Emulation.setEmulatedMedia', { features: [] });

/* ============================================================================
 * 6. and nothing said a word about it
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
  else console.log('\nkinds-check: all good');
  process.exit(code);
}
