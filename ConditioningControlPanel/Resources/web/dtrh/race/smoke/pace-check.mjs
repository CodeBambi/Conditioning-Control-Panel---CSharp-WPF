/* ============================================================================
 * race/smoke/pace-check.mjs - the gentle opening, and the rows that still land.
 *
 *   node race/smoke/pace-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * The owner, on the phone build: "the start feels too fast (122mph)". race/pace.js
 * answers that, and this holds it to the numbers.
 *
 * Two halves. The first is pure: the envelope and the act table are read straight
 * out of pace.js and consts.js and checked second by second, in m/s and in the
 * km/h the plate would show. The second drives a headless browser at a phone
 * viewport through the first 75 seconds of a real worded road and watches what
 * the kart actually did: it never passes the ceiling it was given, the speed
 * plate says the number the kart has and nothing else, and a trigger's row is
 * still under the kart when the word arrives even though the speed is moving
 * under the placement.
 *
 * It never prints a line of a transcript. Everything below is counted.
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
import { envelopeAt, paceForKind, openEndOf, createPace } from '../pace.js';
import { KART_BASE_SPEED, KART_MAX_SPEED, OPEN_PACE, OPEN_BOOST_PACE, OPEN_SEC, OPEN_RAMP_SEC, ACT_PACE, ACT_BLEND_SEC } from '../consts.js';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const near = (a, b, eps) => Math.abs(a - b) <= eps;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const kmh = (ms) => Math.round(ms * 3.6);

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
const ROW = read('words/index.json').rows[0];             // the opening level
// the aligner stamp lives on the index row, not on the race copy of the transcript, and
// race/words.js loadWords() hands it to the road: the fixture is read the same way here.
const WORDS = { ...read('words/' + ROW.file), engine: ROW.engine };

/** A curve shaped like a spoken track, the same swell the other road smokes use. */
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

/* ============================================================================
 * 1. the envelope: 0.7 of the cruise, a lift instead of a boost, then the curve
 * ==========================================================================*/
const OPEN_CAP = OPEN_BOOST_PACE * KART_BASE_SPEED;
ok(kmh(KART_BASE_SPEED) === 79 && kmh(KART_MAX_SPEED) === 122, `the road as written is ${kmh(KART_BASE_SPEED)} km/h with a boost to ${kmh(KART_MAX_SPEED)}`);
const e0 = envelopeAt(0, 45);
ok(near(e0.pace, OPEN_PACE, 1e-9), `at 0 s the cruise is ${OPEN_PACE} of the base (${(KART_BASE_SPEED * e0.pace).toFixed(1)} m/s, ${kmh(KART_BASE_SPEED * e0.pace)} km/h)`);
ok(near(e0.cap, OPEN_CAP, 1e-9), `and a boost only lifts to ${OPEN_BOOST_PACE} of it (${OPEN_CAP.toFixed(1)} m/s, ${kmh(OPEN_CAP)} km/h, not ${kmh(KART_MAX_SPEED)})`);
for (const t of [10, 30, 44.9]) {
  const e = envelopeAt(t, 45);
  ok(near(e.pace, OPEN_PACE, 1e-9) && near(e.cap, OPEN_CAP, 1e-9), `at ${t} s the first act is still the gentle one (${kmh(KART_BASE_SPEED * e.pace)} km/h, lift ${kmh(e.cap)})`);
}
const mid = envelopeAt(45 + OPEN_RAMP_SEC / 2, 45);
ok(near(mid.pace, OPEN_PACE + (1 - OPEN_PACE) / 2, 1e-9), `half way up the ramp the cruise is half way back (${kmh(KART_BASE_SPEED * mid.pace)} km/h)`);
ok(near(mid.cap, OPEN_CAP + (KART_MAX_SPEED - OPEN_CAP) / 2, 1e-9), 'and so is the ceiling');
const arrived = envelopeAt(45 + OPEN_RAMP_SEC + 5, 45);
ok(near(arrived.pace, 1, 1e-9) && near(arrived.cap, KART_MAX_SPEED, 1e-9), `${OPEN_RAMP_SEC} s after the first act the kart is the kart it always was (${kmh(KART_BASE_SPEED)} / ${kmh(KART_MAX_SPEED)} km/h)`);
const noActs = envelopeAt(OPEN_SEC - 0.1, 0);
ok(near(noActs.pace, OPEN_PACE, 1e-9) && noActs.openEnd === OPEN_SEC, `a road with no acts opens for ${OPEN_SEC} s on its own`);
eq(openEndOf(null), OPEN_SEC, 'and a run with no chart at all is the same');
ok(openEndOf(road) > 0 && openEndOf(road) === road.acts[0].t1, `the real track's first act ends at ${openEndOf(road).toFixed(1)} s, and that is where its ramp starts`);

/* ============================================================================
 * 2. the act kind colours the pace, and leans into it
 * ==========================================================================*/
const WANT_KIND = { induction: 0.85, deepening: 0.85, triggers: 1, mantra: 1, build: 1.1, wake: 1.05, silence: 0.75 };
for (const kind of Object.keys(WANT_KIND)) {
  eq(paceForKind(kind), WANT_KIND[kind], `an act of ${kind} runs at ${WANT_KIND[kind]} of the pace`);
  eq(ACT_PACE[kind], WANT_KIND[kind], 'and consts.js is the only place that says so');
}
eq(paceForKind('free'), 1, 'free running is the road as written');
eq(paceForKind(null), 1, 'and so is no act at all');

const pace = createPace();
const past = 200;                                    // well clear of the opening ramp
const act = (kind, t0) => ({ id: kind + t0, kind, t0, t1: t0 + 60 });
const a1 = act('triggers', past);
pace.at(past + 10, a1, road);                        // settled in the triggers act
const a2 = act('silence', past + 60);
const atChange = pace.at(a2.t0 + 0.01, a2, road);
const half = pace.at(a2.t0 + ACT_BLEND_SEC / 2, a2, road);
const settled = pace.at(a2.t0 + ACT_BLEND_SEC + 1, a2, road);
ok(near(atChange.mult, 1, 0.02), `an act change starts from the pace it is leaving (${atChange.mult.toFixed(3)})`);
ok(near(half.mult, (1 + WANT_KIND.silence) / 2, 0.02), `it is half way there ${ACT_BLEND_SEC / 2} s in (${half.mult.toFixed(3)})`);
ok(near(settled.mult, WANT_KIND.silence, 1e-9), `and arrives after ${ACT_BLEND_SEC} s (${settled.mult.toFixed(3)})`);
ok(settled.base < KART_BASE_SPEED, `a silence really is slower road (${settled.base.toFixed(1)} m/s, ${kmh(settled.base)} km/h)`);
const build = pace.at(past + 400, act('build', past + 300), road);
ok(build.base > KART_BASE_SPEED && build.base <= KART_MAX_SPEED, `and a build really is faster (${build.base.toFixed(1)} m/s, ${kmh(build.base)} km/h)`);
const opener = createPace();
const loud = opener.at(1, act('build', 0), road);
ok(loud.cap <= OPEN_CAP + 1e-9 && loud.base <= KART_BASE_SPEED * OPEN_PACE + 1e-9,
  `nothing the act table says can lift the opening past its own ceiling (a first act of build still opens at ${kmh(loud.base)} km/h with a lift to ${kmh(loud.cap)})`);
const quiet = opener.at(1.6, act('silence', 0), road);
ok(quiet.base < KART_BASE_SPEED * OPEN_PACE, `but it can still make those seconds gentler (a silence opens at ${kmh(quiet.base)} km/h)`);
ok(createPace().at(1e6, act('build', 0), road).cap <= KART_MAX_SPEED, 'and nothing can lift the kart past the one hard maximum');

// the numbers the report wants, off the real track
console.log('  --  the pace on the real track:');
const p2 = createPace();
for (const t of [0, 10, 30, 60]) {
  const a = road.acts.find((x) => t >= x.t0 && t < x.t1) || null;
  const s = p2.at(t, a, road);
  console.log(`      t=${String(t).padStart(3)}s  act ${(a && a.kind) || 'none'}  cruise ${s.base.toFixed(1)} m/s (${kmh(s.base)} km/h)  ceiling ${s.cap.toFixed(1)} m/s (${kmh(s.cap)} km/h)`);
}

/* ============================================================================
 * the browser: the web folder plus the fixture chart
 * ==========================================================================*/
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8873;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
/* The real opening level puts its first trigger at 105 s, which is no use to a smoke that has to
 * watch a row land inside the ramp. So the browser half drives a RAMP FIXTURE cut from the same
 * road: the real chart, its first act shortened to 30 s so the ramp runs 30..50 s, a build after
 * it (the sharpest blend the act table can ask for, 0.85 to 1.1), the real triggers re-timed one
 * every 6 s across all of it, and no other events or words at all. That last part is the point:
 * the only bubbles on this road are the trigger rows, so a pop can only be a row landing. */
const RAMP_ACT_END = 30;
const T = road.events.filter((e) => e.kind === 'trigger');
const RAMP = {
  ...road,
  words: [],
  acts: [
    { ...road.acts[0], id: 'ramp-a0', kind: 'induction', t0: 0, t1: RAMP_ACT_END },
    { ...(road.acts[1] || road.acts[0]), id: 'ramp-a1', kind: 'build', t0: RAMP_ACT_END, t1: Math.max(180, road.durationSec) },
  ],
  events: Array.from({ length: 11 }, (_, i) => ({ ...T[i % T.length], id: `ramp-t${i}`, t: 8 + i * 6 })),
};
const FIXTURE = JSON.stringify(RAMP);

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

const prof = mkdtempSync(join(tmpdir(), 'race-pace-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9343', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=390,844', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9343/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await finish(1); }

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
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.track && window.__race.race.perf().world)`);
}
ok(up, 'the page boots the fixture road at a phone viewport');
if (!up) { if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | ')); await finish(1); }

/* ============================================================================
 * 3. what the kart actually did, and what the plate said about it
 * ==========================================================================*/
// every toast the run makes, stamped with the track second it was made on: a row
// under the kart is a pop, and a pop is the only honest witness to when it landed
await ev(`(()=>{ const h=window.__race.race.hud; window.__pops=[]; const was=h.toast.bind(h);
  h.toast=(text,kind)=>{ try{ window.__pops.push({ t: window.__race.race.track.t, kind: kind||'pop' }); }catch(e){}
  return was(text,kind); }; return 1; })()`);

const SAMPLE_MS = 500, RUN_SEC = 78;
const samples = [];
const grab = async () => samples.push(await json(`(()=>{const r=window.__race.race, p=r.perf();
  const n=document.querySelector('.rh-speed-n');
  return { t:+r.track.t.toFixed(3), speed:+(p.speed||0).toFixed(3), boosting:!!p.boosting,
    base:p.pace?+p.pace.base.toFixed(3):null, cap:p.pace?+p.pace.cap.toFixed(3):null,
    mult:p.pace?+p.pace.mult.toFixed(3):null, kind:p.pace?p.pace.kind:null,
    plate:n?+n.textContent:null };})()`));
await grab();
const t0 = Date.now();
while ((Date.now() - t0) / 1000 < RUN_SEC) { await sleep(SAMPLE_MS); await grab(); }
const ran = samples.filter((s) => s.t > 0.2);
ok(ran.length > 20 && ran[ran.length - 1].t > 60, `the run rolled ${ran.length ? ran[ran.length - 1].t.toFixed(1) : 0}s of the file on its own`);

const fake = ran.filter((s) => s.plate !== Math.round(s.speed * 3.6));
eq(fake.length, 0, 'the speed plate says the number the kart has, on every frame it was read');
const overCap = ran.filter((s) => s.cap != null && s.speed > s.cap + 0.35);
eq(overCap.length, 0, 'and the kart never passed the ceiling the pace gave it');

const openEnd = openEndOf(RAMP);
const early = ran.filter((s) => s.t > 2 && s.t < Math.min(openEnd, 40));
const late = ran.filter((s) => s.t > openEnd + OPEN_RAMP_SEC + 1);
const worstEarly = early.reduce((m, s) => Math.max(m, s.speed), 0);
ok(early.length > 5, `${early.length} samples inside the opening act`);
ok(worstEarly <= OPEN_CAP + 0.35, `nothing in the opening went past the lift (${worstEarly.toFixed(1)} m/s, ${kmh(worstEarly)} km/h, ceiling ${kmh(OPEN_CAP)})`);
ok(worstEarly < KART_MAX_SPEED - 6, `and the plate never showed the ${kmh(KART_MAX_SPEED)} km/h the owner photographed (worst ${kmh(worstEarly)} km/h)`);
if (late.length) {
  const bestLate = late.reduce((m, s) => Math.max(m, s.speed), 0);
  ok(bestLate > worstEarly, `the road gets its legs back after the ramp (${kmh(worstEarly)} -> ${kmh(bestLate)} km/h)`);
} else {
  ok(true, 'the run did not reach the far side of the ramp in the time given');
}
console.log('  --  the kart, second by second:');
for (const want of [0, 10, 30, 60]) {
  const s = ran.reduce((best, x) => (best && Math.abs(best.t - want) <= Math.abs(x.t - want) ? best : x), null);
  if (s) console.log(`      t=${String(want).padStart(3)}s  act ${s.kind || 'none'}  ${s.speed.toFixed(1)} m/s  plate ${s.plate} km/h  cruise ${s.base} ceiling ${s.cap}`);
}

/* ============================================================================
 * 4. the row still lands on the word while the speed is moving under it
 * ==========================================================================*/
const pops = await json('window.__pops');
const lastT = ran.length ? ran[ran.length - 1].t : 0;
// Only the sure triggers: a phrase under cues.js TRIGGER_SURE is a loose treat at a random lane,
// placed once and never re-placed (a guess at an unsure word, not a pop on a beat), so what it
// measures is the seeded lane roll, not the sync. The last two rows of this fixture are such guesses.
const trig = RAMP.events.filter((e) => e.kind === 'trigger' && e.t > 3 && e.t < lastT - 2 && !(Number(e.conf) < 0.55));
const deltas = [];
for (const e of trig) {
  let best = null;
  for (const p of pops) {
    const d = p.t - e.t;
    if (Math.abs(d) <= 1.2 && (best === null || Math.abs(d) < Math.abs(best))) best = d;
  }
  if (best !== null) deltas.push(best);
}
ok(deltas.length >= 3, `${deltas.length} of the ${trig.length} triggers inside the ramp had a row under the kart to time`);
if (deltas.length) {
  const abs = deltas.map((d) => Math.abs(d)).sort((a, b) => a - b);
  const median = abs[Math.floor(abs.length / 2)];
  const worst = abs[abs.length - 1];
  console.log(`  --  row under the kart: median ${median.toFixed(3)}s, worst ${worst.toFixed(3)}s, over ${deltas.length} rows`);
  // A ROW STILL LANDS ON ITS WORD WHILE THE SPEED IS MOVING UNDER IT. Two things could have
  // gone wrong here and neither did. Measured on this same fixture with the pace neutralised
  // (OPEN_PACE 1, every act 1, i.e. the road without this PR): median 0.224s, worst 1.096s.
  // With the ramp in: median 0.062s, worst 0.148s, because race/sync.js (Task S1, in this
  // branch base) re-places a row every frame off the speed the kart has now, so a road that
  // changes pace mid-lookahead is exactly what that nudge was written for. If this ever fails,
  // read it as "the pace and the sync disagree about where the kart will be", not as a slow road.
  ok(median <= 0.15, `a row is under the kart within 0.15s of its word (median ${median.toFixed(3)}s)`);
  ok(worst <= 0.5, `and the worst of them is still inside half a second (${worst.toFixed(3)}s)`);
}
ok(errs.length === 0, 'not one console error in the whole run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

await finish(fails ? 1 : 0);

async function finish(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\npace-check: all good');
  process.exit(code);
}
