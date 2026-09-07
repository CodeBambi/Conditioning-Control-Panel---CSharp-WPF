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
 * The browser half boots the same fixture TWICE, because the band has two halves of
 * its own now. `?cap=type` is the old word-timed typewriter (sections 2, 3 and 7);
 * the default build is THE FLASH (section 7b), where the words are on the road and
 * the band answers a pop instead of typing the file. The flash section holds the
 * whole rule: within 50 ms of a pop, joined inside 0.3 s, a fresh line after the
 * fade, a ghost at 0.35 for a word driven past, two lines at most, no typewriter,
 * and never a pixel on the score plate, the toast rail or the plate's rest spot.
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
import { buildPhrases, paintTriggers, MAX_WORDS, GAP_SEC, HOLD_SEC, PLATE_MS,
  FLASH_HOLD_MS, FLASH_FADE_MS, FLASH_JOIN_MS, GHOST_ALPHA } from '../captions.js';
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
const FIXTURE_URL = `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&chart=${encodeURIComponent(`${site}/fixture.json`)}`;
/** Boot the fixture road and wait for the caption layer. Returns false if it never came up. */
async function boot(url) {
  await cdp('Page.navigate', { url });
  for (let i = 0; i < 120; i++) {
    await sleep(250);
    if (await ev(`!!(window.__race && window.__race.race && window.__race.race.track && document.querySelector('.rc-layer'))`)) return true;
  }
  return false;
}
// sections 2, 3 and 7 are the TYPEWRITER, which is one release behind ?cap=type now
const up = await boot(`${FIXTURE_URL}&cap=type`);
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
    const line=document.querySelector('.rc-cap-line');
    const tops=new Set(); for(const w of document.querySelectorAll('.rc-cap-word')){ const b=w.getBoundingClientRect(); if(b.height>0) tops.add(Math.round(b.top)); }
    return { t: window.__race.race.track.t, hidden: !cap || cap.hidden, words: cap?cap.querySelectorAll('.rc-cap-word').length:0,
      said: cap?cap.querySelectorAll('.rc-cap-word.is-said').length:0, now: cap?cap.querySelectorAll('.rc-cap-word.is-now').length:0,
      theme: plate?plate.getAttribute('data-theme'):null, plateLen: plate?plate.textContent.length:0,
      cap: cap&&!cap.hidden?r(cap):null, score: r(document.querySelector('.rh-score-wrap')), chips: r(document.querySelector('.rh-passive')),
      speed: r(document.querySelector('.rh-speed')), mute: r(document.querySelector('.rt-mute')), pause: r(document.querySelector('.rt-pause')),
      lines: tops.size, hidWords: line ? Math.max(0, line.scrollHeight - line.clientHeight) : 0,
      vw: innerWidth, vh: innerHeight };})()`);
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
 * 3. the caption keeps off the chrome, at the TOP, on a 390x844 phone
 *
 * The owner's phone cut the second line in half and the band sat under the road.
 * So: the band opens UNDER the score plate row, it is two lines at most, and
 * every line of it is whole and on the glass. The cut had two causes and both
 * are asserted here, not described: an `em` max-height on a box whose rendered
 * lines were taller than its own font said (nothing is capped in `em` any more,
 * so `hidWords` is 0), and menu.css owning `.rc-word` under #race-root at the
 * intro card's 30px, which an id-carrying selector won over anything this layer
 * could say (the caption's classes are its own now: .rc-cap-line, .rc-cap-word).
 * ==========================================================================*/
const shot = await at(target.words[1].t);
eq(shot.vw + 'x' + shot.vh, '390x844', 'the page really is a phone portrait viewport');
ok(!!shot.cap, 'the caption has a box on the glass');
const hits = (a, b) => !!a && !!b && a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
ok(!hits(shot.cap, shot.score), `it never touches the score plate (caption y ${shot.cap.y}..${shot.cap.y + shot.cap.h}, score y ${shot.score.y}..${shot.score.y + shot.score.h})`);
ok(!hits(shot.cap, shot.speed), 'nor the speed bar');
ok(!hits(shot.cap, shot.chips), 'nor the pickup chips');
ok(shot.cap.y >= 0 && shot.cap.y + shot.cap.h <= shot.vh, 'and it is fully on the glass, not clipped off an edge');
ok(shot.cap.y < shot.vh * 0.45, `it is a TOP band (its top sits at ${Math.round((shot.cap.y / shot.vh) * 100)}% of the height)`);
ok(shot.cap.y >= shot.score.y + shot.score.h, `and it opens under the score plate row (band top ${shot.cap.y}, plate bottom ${shot.score.y + shot.score.h})`);
if (shot.mute || shot.pause) {
  ok(!hits(shot.cap, shot.mute) && !hits(shot.cap, shot.pause), 'nor under the sound and pause buttons');
} else {
  ok(true, 'the sound and pause buttons are a touch build; this run has none to clear');
}
ok(shot.lines <= 2, `it draws two lines at most (${shot.lines})`);
ok(shot.hidWords === 0, `and not a pixel of it is hidden by its own box (${shot.hidWords}px over)`);


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
 * 5. the plate rests in its own air: never on the band, never on a toast
 *
 * The owner's screenshot has the zoom plate sitting on top of "jackpot +200" and
 * "+130 x3" in the same spot. captions.js measures the score plate, the band and
 * the toast rail and parks the plate in what is left, so this holds a plate at
 * the peak of its zoom against a jackpot and a pop fired the same second.
 * ==========================================================================*/
await at(trig.t);
await ev(`window.__race.race.hud.toast('jackpot +200', 'jackpot')`);
await sleep(200);
await ev(`window.__race.race.hud.toast('+130 x3', 'pop')`);
await sleep(480);                    // the zoom is at its peak by now, the toasts are up
const air = await json(`(()=>{ const r=(el)=>{ if(!el) return null; const b=el.getBoundingClientRect();
    return { x:Math.round(b.left), y:Math.round(b.top), w:Math.round(b.width), h:Math.round(b.height) }; };
  return { plate:r(document.querySelector('.rc-plate')), cap:r(document.querySelector('.rc-cap')),
    score:r(document.querySelector('.rh-score-wrap')), rail:r(document.querySelector('.rh-toasts')),
    // only the toasts a player can SEE: one on its way out is a transparent box, not a clash
    toasts:[...document.querySelectorAll('.rh-toast')].filter(t=>+getComputedStyle(t).opacity>0.05).map(r),
    vh: innerHeight };})()`);
ok(!!air.plate, 'a plate is on the glass with the toasts');
ok(air.toasts.length >= 1, `and the rail is carrying ${air.toasts.length}`);
ok(!hits(air.plate, air.cap), `the plate rests under the caption band (band ${air.cap ? air.cap.y + air.cap.h : '?'}, plate top ${air.plate.y})`);
ok(!hits(air.plate, air.score), 'and clear of the score plate');
const onToast = air.toasts.filter((t) => hits(air.plate, t));
eq(onToast.length, 0, `and not one toast is under it (plate ${air.plate.y}..${air.plate.y + air.plate.h}, rail ${air.rail.y}..${air.rail.y + air.rail.h})`);
ok(air.plate.y + air.plate.h <= air.rail.y, 'the plate is finished before the rail begins');
ok(air.plate.y > air.vh * 0.15 && air.plate.y + air.plate.h < air.vh * 0.55, 'and it lands in the upper middle of the glass, where nothing else lives');

/* ============================================================================
 * 6. the toasts read as chatter: shorter, smaller, quieter
 *
 * The owner: "the items notification and the jackpot as well as the streak ones
 * are noisy, make them last less and be kinda faded so they dont clash". Every
 * hold is 60 percent of the wave 2 one, the two chatter kinds are capped, and
 * the rail is 0.85 of its size at 70 percent. The score plate, the combo and the
 * speed plate are not in this table and are not touched.
 * ==========================================================================*/
const WANT_HOLD = { pop: 660, almost: 700, jackpot: 1080, bank: 960, item: 840, effect: 840, recipe: 1020 };
const WAS_HOLD = { pop: 1100, almost: 1300, jackpot: 1800, bank: 1600, item: 1400, effect: 1400, recipe: 1700 };
for (const kind of Object.keys(WANT_HOLD)) {
  await ev(`window.__race.race.hud.toast(${JSON.stringify(kind === 'bank' ? 'kept 40' : '+10')}, '${kind}')`);
  await sleep(260);                  // longer than the chatter gap, so every kind gets its turn
  const hold = await ev(`(()=>{const t=[...document.querySelectorAll('.rh-toast--${kind}')].pop();
    return t ? t.style.getPropertyValue('--rh-hold') : null;})()`);
  eq(hold, `${WANT_HOLD[kind]}ms`, `a ${kind === 'bank' ? 'kept' : kind} toast holds ${WANT_HOLD[kind]}ms`);
  ok(WANT_HOLD[kind] <= Math.round(WAS_HOLD[kind] * 0.6), `and that is 40 percent off the wave 2 hold (${WAS_HOLD[kind]}ms)`);
}
ok(WANT_HOLD.pop <= 700 && WANT_HOLD.almost <= 700, 'the two chatter kinds are capped at 0.7s however loud the run gets');
const railStyle = await json(`(()=>{const n=document.querySelector('.rh-toasts'); const cs=getComputedStyle(n);
  const sc=document.querySelector('.rh-score-wrap'), sp=document.querySelector('.rh-speed');
  return { t: cs.transform, o: +cs.opacity, scoreO: +getComputedStyle(sc).opacity, speedO: +getComputedStyle(sp).opacity };})()`);
ok(/matrix\(0\.85,/.test(railStyle.t), `the rail is 0.85 of its old size (${railStyle.t})`);
ok(Math.abs(railStyle.o - 0.7) < 0.01, `and sits at 70 percent (${railStyle.o})`);
ok(railStyle.scoreO === 1 && railStyle.speedO === 1, 'while the score plate and the speed plate stay at full strength: those are the honest numbers');

/* ============================================================================
 * 7. EVERY phrase of the track, not the lucky one
 *
 * The cut the owner photographed was a long phrase's second line, so walk the
 * whole caption track and hold each phrase to the same two rules: two lines at
 * most, and not a pixel of it hidden by its own box. (Last, because it drives
 * the clock past every trigger on the road.)
 * ==========================================================================*/
let widest = 0, worst = null, lines3 = 0, clipped = 0;
for (const p of phrases.filter((x) => x.t0 > 3)) {
  const s = await at(p.words[Math.min(1, p.words.length - 1)].t + 0.02);
  if (s.hidden || !s.cap) continue;
  if (s.lines > 2) lines3++;
  if (s.hidWords > 0) clipped++;
  if (s.cap.h > widest) { widest = s.cap.h; worst = s; }
}
eq(lines3, 0, `no phrase of the ${phrases.length} on this track draws a third line`);
eq(clipped, 0, 'and none of them is cut off by its own box');
ok(!!worst && worst.cap.y + worst.cap.h <= worst.vh, `the tallest band on the track is whole on the glass (${widest}px, bottom ${worst ? worst.cap.y + worst.cap.h : '?'} of ${shot.vh})`);
ok(!!worst && !hits(worst.cap, worst.score), 'and even the tallest keeps off the score plate');

/* ============================================================================
 * 7b. THE FLASH: the band answers the pops, and nothing types
 *
 * The default build. The words are on the road (race/wordBubbles.js), so the band
 * is where a POP is answered: the word lands whole, holds, joins the line if
 * another pop is inside 0.3 s, and a word the kart drove past still lands as a
 * grey ghost. `race.debugWord` is the same call race/run.js onPop makes, so the
 * timings below are the layer's own and not a re-implementation of them.
 *
 * It never prints a word of the transcript: everything here is counted, measured
 * or read off a class, and the one word it puts on the glass itself is 'ok'.
 * ==========================================================================*/
const upFlash = await boot(FIXTURE_URL);
ok(upFlash, 'the default build boots the same road');
if (!upFlash) await done(1);
eq(await ev(`window.__race.race.capMode()`), 'flash', 'and the band is the flash, not the typewriter');

/** What the band is holding right now: the words, their classes, and the boxes around them. */
const band = () => json(`(()=>{ const cap=document.querySelector('.rc-cap');
  const r=(el)=>{ if(!el) return null; const b=el.getBoundingClientRect(); return { x:Math.round(b.left), y:Math.round(b.top), w:Math.round(b.width), h:Math.round(b.height) }; };
  const words=[...document.querySelectorAll('.rc-flash-word')];
  const tops=new Set(); for(const w of words){ const b=w.getBoundingClientRect(); if(b.height>0) tops.add(Math.round(b.top)); }
  const cs=(w)=>getComputedStyle(w);
  return { n: words.length, typed: document.querySelectorAll('.rc-cap-word:not(.rc-flash-word)').length,
    ghosts: words.filter(w=>w.classList.contains('is-ghost')).length,
    accents: words.filter(w=>w.classList.contains('is-accent')).length,
    alpha: words.map(w=>Math.round(+cs(w).opacity*100)/100), fs: words.map(w=>Math.round(parseFloat(cs(w).fontSize))),
    lines: tops.size, hidden: !cap || cap.hidden, capOut: +getComputedStyle(cap).opacity,
    cap: cap&&!cap.hidden?r(cap):null, score: r(document.querySelector('.rh-score-wrap')),
    rail: r(document.querySelector('.rh-toasts')), plateY: parseFloat(getComputedStyle(document.querySelector('.rc-layer')).getPropertyValue('--rc-plate-y'))||0,
    vw: innerWidth, vh: innerHeight };})()`);

// Everything from here to the plate runs on the WORDLESS opening this file has (the road's own
// first word is a minute and a half in), so no real bubble can land on the band mid-measurement.
const quiet = await band();
ok(quiet.hidden || quiet.n === 0, 'the band is off the glass until something pops: nothing types itself');

// 1. a pop puts the whole word on the band, now
const first = await json(`(()=>{ const t0=performance.now(); window.__race.race.debugWord('ok');
  return { ms: Math.round(performance.now()-t0), n: document.querySelectorAll('.rc-flash-word').length,
    len: (document.querySelector('.rc-flash-word')||{}).textContent?.length||0 };})()`);
ok(first.ms <= 50, `the word is on the band ${first.ms}ms after the pop (50ms is the bar)`);
eq(first.n, 1, 'and it is one whole word, not a letter of one');
eq(first.len, 2, 'with the whole of it in the DOM at once: no typing');

// 2. pops inside FLASH_JOIN_MS join the line; the sentence is what a clean line reads back as.
// All three go in ONE evaluate: the join window is 0.3 s and a debug round trip is not free.
await sleep(FLASH_HOLD_MS + FLASH_FADE_MS + 250);
const joined = await json(`(()=>{ const r=window.__race.race; r.debugWord('ok'); r.debugWord('ok'); r.debugWord('ok');
  const words=[...document.querySelectorAll('.rc-flash-word')], tops=new Set();
  for (const w of words) { const b=w.getBoundingClientRect(); if (b.height>0) tops.add(Math.round(b.top)); }
  return { n: words.length, lines: tops.size };})()`);
eq(joined.n, 3, `three pops inside ${FLASH_JOIN_MS}ms are one line of three`);
ok(joined.lines <= 2, `and the line is ${joined.lines} row(s), never more than 2`);

// 3. a pop after the line has gone starts a fresh one
await sleep(FLASH_HOLD_MS + FLASH_FADE_MS + 250);
const gone = await band();
ok(gone.hidden || gone.n === 0, `the line lets go ${FLASH_HOLD_MS}ms after the last pop and fades over ${FLASH_FADE_MS}ms`);
await ev(`window.__race.race.debugWord('ok')`);
const restarted = await band();
eq(restarted.n, 1, 'and the next pop starts a fresh line rather than joining a dead one');

// 4. the ghost: a word the kart drove past is still read, faint and grey
await ev(`window.__race.race.debugWord('ok', { ghost: true })`);
await sleep(220);        // past its 160ms fade in: an alpha read mid-animation is the animation's, not the rule's
const ghosted = await band();
eq(ghosted.ghosts, 1, 'a word the kart drove past still writes itself');
ok(Math.abs(ghosted.alpha[ghosted.alpha.length - 1] - GHOST_ALPHA) < 0.02,
  `and it sits at ${GHOST_ALPHA} where a popped word sits at 1 (got ${ghosted.alpha[ghosted.alpha.length - 1]})`);

// 5. an accent word is bigger and wears the set's ink
await sleep(FLASH_HOLD_MS + FLASH_FADE_MS + 250);
await ev(`window.__race.race.debugWord('ok'); window.__race.race.debugWord('ok', { accent: true, ink: '#ff69b4' })`);
const accented = await band();
eq(accented.accents, 1, 'an accent word is marked as one');
ok(accented.fs[1] > accented.fs[0], `and drawn bigger than the plain word beside it (${accented.fs[0]}px -> ${accented.fs[1]}px)`);

// 6. a long chain never spills a third line, and never leaves the band's own box
await sleep(FLASH_HOLD_MS + FLASH_FADE_MS + 250);
await ev(`for (let i = 0; i < 12; i++) window.__race.race.debugWord('conditioning')`);
const chain = await band();
ok(chain.lines <= 2, `twelve long pops in a row still draw ${chain.lines} line(s), never a third`);
ok(!!chain.cap && chain.cap.y >= 0 && chain.cap.y + chain.cap.h <= chain.vh, 'and the band is whole on the glass');
const hitB = (a, b) => !!a && !!b && a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
ok(!hitB(chain.cap, chain.score), `it never touches the score plate (band ${chain.cap.y}..${chain.cap.y + chain.cap.h}, plate ${chain.score.y}..${chain.score.y + chain.score.h})`);
ok(!hitB(chain.cap, chain.rail), 'nor the toast rail');
ok(chain.plateY > chain.cap.y + chain.cap.h, `and the plate's rest spot is still clear under it (band bottom ${chain.cap.y + chain.cap.h}, plate y ${chain.plateY})`);

// 7. the typewriter really is behind ?cap=type: the clock inside a phrase writes nothing
await sleep(FLASH_HOLD_MS + FLASH_FADE_MS + 250);
await ev(`window.__race.race.trackClock(${target.words[1].t}, false)`);
await sleep(300);
eq((await band()).typed, 0, 'the clock inside a phrase types nothing: the typewriter only lives behind ?cap=type');

// 8. the plate owns the glass: a trigger clears the band under it. The count is taken AT the frame
// the plate reaches the DOM, because the kart is still driving and a real pop is 60ms away.
await ev(`window.__race.race.debugWord('ok')`);
ok((await band()).n >= 1, 'a word is on the band');
await ev(`(()=>{ window.__atPlate = -1;
  new MutationObserver((ms)=>{ for (const m of ms) for (const n of m.addedNodes) {
    if (n.classList && n.classList.contains('rc-plate') && window.__atPlate < 0) window.__atPlate = document.querySelectorAll('.rc-flash-word').length;
  } }).observe(document.querySelector('.rc-plates'), { childList: true });
  window.__race.race.debugWord('ok'); window.__race.race.trackClock(${trig.t}, false); })()`);
await sleep(500);
eq(await ev(`window.__atPlate`), 0, 'and the trigger plate clears it: one thing at a time on the glass');
eq(await json(`document.querySelectorAll('.rc-plate').length`), 1, 'with the plate itself flying');

// 9. THE WIRING: real pops on the real road, counted as they land
await ev(`(()=>{ window.__flash = 0; window.__ghost = 0;
  const line = document.querySelector('.rc-cap-line');
  new MutationObserver((ms)=>{ for (const m of ms) for (const n of m.addedNodes) {
    if (!n.classList || !n.classList.contains('rc-flash-word')) continue;
    window.__flash++; if (n.classList.contains('is-ghost')) window.__ghost++; } }).observe(line, { childList: true });
  window.__race.race.trackClock(${Math.max(0, road.words[0].t - 0.7)}, true); })()`);
await sleep(6000);
const live = await json(`({ flash: window.__flash, ghost: window.__ghost, t: window.__race.race.track.t })`);
ok(live.t > road.words[0].t, `the road rolled through the first spoken words (to ${live.t.toFixed(1)}s)`);
ok(live.flash > 0, `and ${live.flash} of them reached the band off real pops and passes (${live.ghost} as ghosts)`);

/* ============================================================================
 * 8. the demo road still runs, and nothing shouted
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
