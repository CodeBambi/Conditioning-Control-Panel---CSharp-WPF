/* ============================================================================
 * race/smoke/captions-check.mjs - the voice on the glass.
 *
 *   node race/smoke/captions-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Two halves. The first is pure: the real caption track this branch ships for the
 * opening level is cut into phrases and the cut is held against its own rules. The
 * second drives a headless browser at a PHONE viewport (390x844), because the two
 * things this layer can get wrong that no unit test sees are "the caption sits on
 * the score plate" and "the plate never actually reached the DOM".
 *
 * The browser half builds one fixture chart off the real transcript, serves it to
 * `?chart=<url>`, then drives `race.trackClock(t, false)` straight at the seconds
 * it wants to look at. That is the host's own door, it is how a pause looks from
 * in here, and it means the whole run does not have to be waited through.
 *
 * It never prints a line of a transcript. Everything below is counted, and the one
 * thing it reads out of the DOM is how MANY words were inked, never which.
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
import { buildPhrases, paintTriggers, MAX_WORDS, GAP_SEC, HOLD_SEC } from '../captions.js';
import { THEME_BY_PRESET, themeFor } from '../triggerTheme.js';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
const ROW = read('words/index.json').rows[0];             // the opening level
const WORDS = read('words/' + ROW.file);

/** A curve shaped like a spoken track, the same swell lyrics-road-check.mjs uses. */
function swell(durationSec, perSec = PEAKS_PER_SEC) {
  const n = Math.ceil(durationSec * perSec);
  const peaks = new Float32Array(n * 2);
  for (let i = 0; i < n; i++) {
    const a = 0.22 + 0.5 * Math.max(0, Math.sin(((i / perSec) / 40) * Math.PI * 2)) ** 2;
    peaks[i * 2] = -a; peaks[i * 2 + 1] = a;
  }
  return peaks;
}

const road = wordedRoad({ peaks: swell(WORDS.durationSec), durationSec: WORDS.durationSec, name: ROW.title, hash: ROW.hash, words: WORDS });
const triggers = road.events.filter((e) => e.kind === 'trigger');

/* ============================================================================
 * 1. the cut
 * ==========================================================================*/
const phrases = buildPhrases(road.words);
console.log(`the track: ${road.words.length} caption words, ${triggers.length} trigger events, ${Math.round(road.source.durationSec)}s`);
ok(phrases.length > 0, `the caption track cuts into ${phrases.length} phrases`);
eq(phrases.reduce((n, p) => n + p.words.length, 0), road.words.length, 'and every word landed in exactly one of them');
ok(phrases.every((p) => p.words.length <= MAX_WORDS), 'no phrase is longer than ' + MAX_WORDS + ' words');
ok(phrases.every((p) => p.words.length >= 1), 'and none of them is empty');
const longest = Math.max(...phrases.map((p) => p.words.length));
const median = phrases.map((p) => p.words.length).sort((a, b) => a - b)[phrases.length >> 1];
console.log(`  --  phrases: ${phrases.length}, longest ${longest} words, median ${median}`);

let overlap = 0, backwards = 0, outside = 0;
for (let i = 0; i < phrases.length; i++) {
  const p = phrases[i], next = phrases[i + 1];
  if (next && next.tStart < p.tEnd - 1e-9) overlap++;
  if (p.tEnd < p.t1 - 1e-9 || p.tStart > p.t0 + 1e-9) backwards++;
  for (let k = 1; k < p.words.length; k++) if (p.words[k].t < p.words[k - 1].t) backwards++;
  for (const w of p.words) if (w.t < p.t0 - 1e-9 || w.t > p.t1 + 1e-9) outside++;
}
eq(overlap, 0, 'no two phrases are ever on the glass at once');
eq(backwards, 0, 'every phrase opens before its first word and holds past its last');
eq(outside, 0, 'and every word of a phrase is inside that phrase');
ok(phrases.every((p) => p.tEnd - p.t1 <= HOLD_SEC + 1e-9), 'none of them hangs on longer than ' + HOLD_SEC + 's past its last word');

// the cut rules did the cutting: a gap, a full stop or the word cap
let unexplained = 0;
for (let i = 0; i < phrases.length - 1; i++) {
  const p = phrases[i], next = phrases[i + 1];
  const last = p.words[p.words.length - 1];
  const gap = next.words[0].t - (last.t + last.d) > GAP_SEC;
  const stop = /[.?!]["')\]]?$/.test(last.w);
  if (!gap && !stop && p.words.length < MAX_WORDS) unexplained++;
}
eq(unexplained, 0, 'every cut is a gap, a full stop or the ninth word, and nothing else');

const painted = paintTriggers(buildPhrases(road.words), road.events);
const lit = painted.reduce((n, p) => n + p.words.filter((w) => w.color).length, 0);
ok(lit > 0, `${lit} caption words wear their trigger's own colour`);
ok(painted.every((p) => p.words.every((w) => !w.color || /^#[0-9a-f]{3,8}$/i.test(w.color))), 'and every colour is a real one');
eq(new Set(Object.values(THEME_BY_PRESET).map((r) => r.theme)).size, Object.keys(THEME_BY_PRESET).length,
  'the theme table still gives every preset its own plate class');

/* ============================================================================
 * the browser: the web folder plus the fixture chart
 * ==========================================================================*/
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8869;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const FIXTURE = JSON.stringify(road);

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path === '/fixture.json') { res.writeHead(200, { 'content-type': 'application/json' }); return res.end(FIXTURE); }
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-caps-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9339', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=390,844', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9339/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
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
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });

const site = `http://127.0.0.1:${PORT}`;
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&chart=${encodeURIComponent(`${site}/fixture.json`)}` });
let up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.track && document.querySelector('.rc-layer'))`);
}
ok(up, 'the page boots the fixture road and builds the caption layer');
if (!up) {
  console.error('    boot: ' + await ev(`JSON.stringify({ race: !!window.__race, run: !!(window.__race&&window.__race.race), track: !!(window.__race&&window.__race.race&&window.__race.race.track), layer: !!document.querySelector('.rc-layer'), hud: !!document.querySelector('.race-hud'), body: document.body.className, url: location.href, title: document.title, html: document.documentElement.outerHTML.length })`));
  if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | '));
  await done(1);
}

/** Park the clock on one second (a pause, as far as the run is concerned) and read the glass. */
const at = async (t) => {
  await ev(`window.__race.race.trackClock(${t}, false)`);
  await sleep(220);
  return json(`(()=>{ const cap=document.querySelector('.rc-cap'), plate=document.querySelector('.rc-plate');
    const r=(el)=>{ if(!el) return null; const b=el.getBoundingClientRect(); return { x:Math.round(b.left), y:Math.round(b.top), w:Math.round(b.width), h:Math.round(b.height) }; };
    return { t: window.__race.race.track.t, hidden: !cap || cap.hidden, words: cap?cap.querySelectorAll('.rc-word').length:0,
      said: cap?cap.querySelectorAll('.rc-word.is-said').length:0, now: cap?cap.querySelectorAll('.rc-word.is-now').length:0,
      theme: plate?plate.getAttribute('data-theme'):null, plateLen: plate?plate.textContent.length:0,
      cap: cap&&!cap.hidden?r(cap):null, score: r(document.querySelector('.rh-score-wrap')), item: r(document.querySelector('.rh-item')),
      speed: r(document.querySelector('.rh-speed')), vw: innerWidth, vh: innerHeight };})()`);
};

/* ============================================================================
 * 2. the caption types itself off the clock
 * ==========================================================================*/
const target = phrases.find((p) => p.words.length >= 4 && p.t0 > 3 && p.tEnd - p.t1 > 0.15) || phrases[0];
const before = await at(Math.max(0, target.t0 - 0.4));
const opened = await at(target.t0 + 0.02);
ok(!opened.hidden && opened.words === target.words.length, `the phrase opens with all ${target.words.length} of its words in the DOM, so the line cannot reflow as it types`);
ok(opened.said >= 1, 'and its first word is inked the moment the voice reaches it');
const half = await at(target.words[Math.min(2, target.words.length - 1)].t + 0.02);
ok(half.said > opened.said, `the line types itself as the clock runs (${opened.said} -> ${half.said} of ${half.words} inked)`);
const full = await at(target.t1 + 0.01);   // its last word is over, its hold is not
eq(full.said, target.words.length, 'and by the last word the whole phrase is up');
ok(full.words === target.words.length, 'without a single word having been added or dropped on the way');
// a seek BACK is a rebuild off the clock, not a rewind of frames
const again = await at(target.t0 + 0.02);
ok(again.said <= half.said && again.words === target.words.length, `a seek back to the top re-inks from the clock (${full.said} -> ${again.said})`);
void before;

// a phrase is gone before the next one opens
const gapAt = phrases.findIndex((p, i) => phrases[i + 1] && phrases[i + 1].tStart - p.tEnd > 0.5 && p.t0 > 3);
if (gapAt >= 0) {
  const p = phrases[gapAt];
  const between = await at(p.tEnd + Math.min(0.4, (phrases[gapAt + 1].tStart - p.tEnd) / 2));
  ok(between.hidden || between.words === 0, 'and the glass is clear between two phrases: one at a time, never a crossfade');
} else {
  ok(true, 'the track never leaves a gap wide enough to look between two phrases');
}

/* ============================================================================
 * 3. the caption keeps off the chrome, on a 390x844 phone
 * ==========================================================================*/
const shot = await at(target.words[1].t);
eq(shot.vw + 'x' + shot.vh, '390x844', 'the page really is a phone portrait viewport');
ok(!!shot.cap, 'the caption has a box on the glass');
const hits = (a, b) => !!a && !!b && a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
ok(!hits(shot.cap, shot.score), `it never touches the score plate (caption y ${shot.cap.y}..${shot.cap.y + shot.cap.h}, score y ${shot.score.y}..${shot.score.y + shot.score.h})`);
ok(!hits(shot.cap, shot.item), 'nor the item slot');
ok(!hits(shot.cap, shot.speed), 'nor the speed bar');
ok(shot.cap.y >= 0 && shot.cap.y + shot.cap.h <= shot.vh, 'and it is fully on the glass, not clipped off an edge');
ok(shot.cap.y > shot.vh * 0.55, `it is a lower third (its top sits at ${Math.round((shot.cap.y / shot.vh) * 100)}% of the height)`);
ok(shot.cap.h <= 100, `and it is two lines at most (${shot.cap.h}px tall)`);

/* ============================================================================
 * 4. the plate flies on a sure trigger
 * ==========================================================================*/
const trig = triggers.find((e) => e.t > target.t1 && THEME_BY_PRESET[e.cue]) || triggers.find((e) => THEME_BY_PRESET[e.cue]) || triggers[0];
ok(!!trig, 'the road has a trigger to land on');
const plated = await at(trig.t);
const wantTheme = themeFor(trig).theme;
ok(!!plated.theme, 'the trigger put a plate on the glass');
eq(plated.theme, wantTheme, `and it wears the theme the one table gives its preset`);
ok(plated.plateLen === String(trig.label || '').trim().length, 'and the plate says the phrase the road heard, nothing else');
const many = await json(`document.querySelectorAll('.rc-plate').length`);
eq(many, 1, 'never more than one plate at a time');
const themed = await json(`(()=>{ const s=[...document.styleSheets].flatMap(x=>{try{return [...x.cssRules]}catch(e){return []}});
  const want=${JSON.stringify(Object.values(THEME_BY_PRESET).map((r) => r.theme))};
  return want.filter(t=>s.some(r=>r.selectorText&&r.selectorText.indexOf('.rc-plate--'+t)>=0)).length;})()`);
eq(themed, Object.keys(THEME_BY_PRESET).length, 'and race.css carries a class for every theme in the table');

/* ============================================================================
 * 5. the demo road still runs, and nothing shouted
 * ==========================================================================*/
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&chart=demo&dur=240` });
let demo = false;
for (let i = 0; i < 120 && !demo; i++) {
  await sleep(250);
  demo = await ev(`!!(window.__race && window.__race.race && window.__race.race.track)`);
}
ok(demo, 'the demo road boots too');
const a = await ev(`window.__race.race.track.t`);
await sleep(3000);
const b = await json(`({ t: window.__race.race.track.t, running: window.__race.race.perf().running, bubbles: window.__race.race.perf().bubbles })`);
ok(b.t > a, `and it rolls on its own (${Number(a).toFixed(2)}s -> ${b.t.toFixed(2)}s)`);
ok(b.running === true, 'with the run still going');
ok(errs.length === 0, 'not one console error in either run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\ncaptions-check: all good');
  process.exit(code);
}
