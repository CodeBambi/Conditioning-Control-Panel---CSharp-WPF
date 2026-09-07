/* ============================================================================
 * race/smoke/pickups-check.mjs - THE PASSIVE PICKUPS (race/pickups.js).
 *
 *   node race/smoke/pickups-check.mjs            (from Resources/web/dtrh)
 *   CHROME_PATH=... to point at a Chrome that is not in the default place
 *
 * Under node: the table (the seven rows, their pictures on disk, the pool weights at x1 and x8),
 * a whole seeded run of the brain (the gentle start, one on the road at a time, the gap, a take,
 * a drop), the families (two chips stack, the same one again refreshes, another of the family
 * replaces), track.js's one door into the file (nextEvent, gild / takeGold, the golden centre a
 * trigger row gets), and THE LYRIC PLACEMENT RULE on the opening level's real transcript: never in
 * the first act, never within CLEAR_SEC of a chart event, golden touch only on the lead.
 *
 * Then one headless Chrome at a phone viewport lights each of the seven with the ?pickup= aid on
 * the demo road: it is taken, its chip comes up, riptide runs the cruise faster and the pump rides
 * a boost, and not one console error or missing file across all seven.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { TUNE, PICKUPS, PICKUP_BY_ID, weightFor, rollPickup, createPickups } from '../pickups.js';
import { createTrackState } from '../track.js';
import { cueFor, ROW_X } from '../cues.js';
import { demoChart } from '../chart.js';
import { makeRng } from '../consts.js';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';

let fails = 0;
const ok = (c, m) => { console.log(`  ${c ? 'ok ' : 'FAIL'} ${m}`); if (!c) fails++; };
const eq = (a, b, m) => ok(a === b, `${m} (${JSON.stringify(a)}${a === b ? '' : ' != ' + JSON.stringify(b)})`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');
const SPEED = 22, DT = 1 / 60;

/* ============================================================================
 * 1. the table
 * ==========================================================================*/
console.log('the table');
const WANT = { poppers: 8, pocket_watch: 10, the_wand: 7, rabbit_foot: 12, golden_touch: 8, the_pump: 5, riptide: 6 };
eq(PICKUPS.map((p) => p.id).sort().join(','), Object.keys(WANT).sort().join(','), 'the seven rows, and only those');
ok(PICKUPS.every((p) => p.sec === WANT[p.id]), 'every effect runs the seconds the owner picked');
ok(PICKUPS.every((p) => p.name === p.id.replace(/_/g, ' ') && p.name === p.name.toLowerCase()), 'every name is the house name, lowercase');
ok(PICKUPS.every((p) => (p.family === 'bonus' || p.family === 'sweep') && ['catch', 'mid', 'risk'].includes(p.pool)), 'every row has a family and a pool');
eq(PICKUPS.filter((p) => p.family === 'sweep').map((p) => p.id).join(','), 'the_pump,riptide', 'the pump and riptide are the sweep family');
ok(PICKUPS.every((p) => existsSync(resolve(WEB, '.' + p.sprite))), 'every picture is on disk under assets/items');
ok(['FIRST_SEC', 'GAP_SEC', 'AHEAD_M', 'TAKE_X', 'POINTS', 'DROP_M', 'CLEAR_SEC', 'GOLD_LEAD_SEC'].every((k) => k in TUNE), 'every knob is in the one table');
const w1 = (pool) => weightFor({ pool }, 1), w8 = (pool) => weightFor({ pool }, 8);
ok(w1('catch') > w1('mid') && w1('mid') > w1('risk'), `at x1 catch-up leads (${w1('catch')} > ${w1('mid')} > ${w1('risk')})`);
ok(w8('risk') > w8('mid') && w8('mid') > w8('catch'), `at x8 risk leads (${w8('risk')} > ${w8('mid')} > ${w8('catch').toFixed(2)})`);
{
  const rng = makeRng(7), n = {};
  for (let i = 0; i < 3000; i++) { const p = rollPickup(1, rng); n[p.pool] = (n[p.pool] || 0) + 1; }
  ok(n.catch > n.mid && n.mid > n.risk, `3000 rolls at x1 land that way (catch ${n.catch}, mid ${n.mid}, risk ${n.risk})`);
  let gold = 0;
  for (let i = 0; i < 500; i++) if (rollPickup(8, rng, new Set(['golden_touch'])).id === 'golden_touch') gold++;
  eq(gold, 0, 'an excluded id is never rolled, even at x8');
}

/* ============================================================================
 * 2. a seeded run of the brain: the gentle start, one at a time, the gap, a take, a drop
 * ==========================================================================*/
console.log('a seeded run');
{
  const LAP = 1200, spots = [];
  for (let d = 30; d < LAP; d += 50) spots.push({ d, x: 0 });
  const pk = createPickups({ rng: makeRng(3), spots, totalDepth: LAP });
  const ev = []; pk.onEvent((e) => ev.push({ ...e, at: elapsed, kd: kartD }));
  let elapsed = 0, kartD = 0, liveMax = 0, kartX = 0;
  const OPEN = 20;
  while (elapsed < 420) {
    kartD = (kartD + SPEED * DT) % LAP; elapsed += DT;
    kartX = ev.filter((e) => e.type === 'pickupSpawn').length === 3 ? 1.5 : 0;   // the third one is let go by
    pk.update(DT, { d: kartD, x: kartX, speed: SPEED, elapsed, opening: elapsed < OPEN, mult: 1 });
    liveMax = Math.max(liveMax, (pk.live ? 1 : 0));
  }
  const spawns = ev.filter((e) => e.type === 'pickupSpawn'), takes = ev.filter((e) => e.type === 'pickupTake'), drops = ev.filter((e) => e.type === 'pickupDrop');
  ok(spawns.length >= 5, `${spawns.length} pickups lit over 420 s of driving`);
  ok(spawns[0] && spawns[0].at >= TUNE.FIRST_SEC, `nothing lit before ${TUNE.FIRST_SEC} s (first at ${spawns[0] ? spawns[0].at.toFixed(1) : '-'} s)`);
  const rel = (s) => { let r = (s.d - s.kd) % LAP; if (r > LAP / 2) r -= LAP; return r; };
  ok(spawns.every((s) => rel(s) >= TUNE.AHEAD_M[0] && rel(s) <= TUNE.AHEAD_M[1]), 'every one lit inside the AHEAD_M window');
  eq(liveMax, 1, 'never more than one on the road');
  eq(takes.length, spawns.length - 1, 'every one crossed at the kart\'s x was taken');
  eq(drops.length, 1, 'the one the kart steered past was dropped');
  ok(takes.every((t) => t.refresh === false || t.refresh === true), 'a take says whether it was a refresh');
  const ends = [...takes, ...drops].sort((a, b) => a.at - b.at), gaps = [];
  for (let i = 1; i < spawns.length; i++) gaps.push(spawns[i].at - ends.find((e) => e.at > spawns[i - 1].at).at);
  ok(gaps.every((g) => g >= TUNE.GAP_SEC[0] - 0.5), `the next waits at least ${TUNE.GAP_SEC[0]} s of driving (gaps ${gaps.map((g) => g.toFixed(0)).join(' ')})`);
}

/* ============================================================================
 * 3. the families: two chips stack, the same again refreshes, another of the family replaces
 * ==========================================================================*/
console.log('the families');
{
  const pk = createPickups({ rng: makeRng(1), spots: [{ d: 10, x: 0 }], totalDepth: 100 });
  const ev = []; pk.onEvent((e) => ev.push(e));
  const grab = (id) => { pk.light(id, { d: 10, x: 0 }); return pk.take(); };
  grab('poppers'); eq(pk.chips().length, 1, 'poppers: one chip');
  grab('the_pump'); eq(pk.chips().map((c) => c.id).join('+'), 'poppers+the_pump', 'the pump on top: two chips, one per family');
  pk.update(3, { d: 0, x: 0, speed: 0, elapsed: 0, opening: true, mult: 1 });
  ok(pk.chips().find((c) => c.id === 'poppers').frac < 0.7, 'three seconds in, the poppers bar has drained');
  grab('poppers');
  ok(ev.at(-1).type === 'pickupTake' && ev.at(-1).refresh === true, 'poppers again is a refresh');
  ok(pk.chips().length === 2 && pk.chips().find((c) => c.id === 'poppers').frac === 1, 'still two chips, its bar full again');
  grab('the_wand');
  const ended = ev.filter((e) => e.type === 'pickupEnd').map((e) => e.id);
  eq(ended.join(','), 'poppers', 'the wand replaces poppers: its end fires');
  eq(pk.chips().map((c) => c.id).sort().join('+'), 'the_pump+the_wand', 'and there are still two chips, never three');
  pk.update(2.5, { d: 0, x: 0, speed: 0, elapsed: 0, opening: true, mult: 1 });
  eq(pk.chips().map((c) => c.id).join('+'), 'the_wand', 'the pump runs out on its own clock (5 s) and its chip goes');
  pk.reset(); eq(pk.chips().length, 0, 'a reset clears every chip');
}

/* ============================================================================
 * 4. track.js: the one door into the file, and the golden centre of a trigger row
 * ==========================================================================*/
console.log('track.js');
{
  const TR = createTrackState();
  ok(TR.nextEvent(0) === null, 'no track: nextEvent is null');
  TR.setTrack(demoChart({ durationSec: 240 }));
  const ch = TR.track.chart, mid = TR.track.durationSec / 2;
  eq(TR.nextEvent(0).id, ch.events[0].id, 'nextEvent(0) is the first event');
  const e = TR.nextEvent(mid);
  ok(e && e.t >= mid && ch.events.every((x) => x.t < mid || x.t >= e.t), 'nextEvent(t) is the first at or after t');
  const tr = TR.nextEvent(mid, 'trigger');
  ok(tr && tr.kind === 'trigger' && tr.t >= mid, 'and asks for one kind');
  ok(TR.nextEvent(1e9) === null, 'nothing after the end');
  TR.gild(2);
  eq([TR.takeGold(), TR.takeGold(), TR.takeGold()].join(','), 'true,true,false', 'gild(2) hands out two goldens and no more');
  TR.gild(2); TR.setTrack(null); eq(TR.takeGold(), false, 'a new track forgets the gold owed');
  const trig = ch.events.find((x) => x.kind === 'trigger') || { kind: 'trigger', label: 'sleep', t: 10, conf: 1 };
  const ctx = (gold) => ({ rng: makeRng(2), triggerKinds: new Map(), gold: () => gold });
  const plain = cueFor({ ...trig, conf: 1 }, ctx(false)).spawn, gilded = cueFor({ ...trig, conf: 1 }, ctx(true)).spawn;
  eq(plain.filter((s) => s.kindId === 'golden').length, 0, 'a plain trigger row has no golden in it');
  const centre = gilded.filter((s) => Math.abs(s.x) < 1e-6);
  ok(centre.length === 1 && centre[0].kindId === 'golden' && centre[0].row === true, 'with gold owed its centre is a golden, still part of the row');
  eq(gilded.filter((s) => s.kindId === 'golden').length, 1, 'and only the centre');
  eq(gilded.length, ROW_X.length, 'the row is still the full row');
}

/* ============================================================================
 * 5. THE LYRIC PLACEMENT RULE on the opening level's transcript
 * ==========================================================================*/
console.log('the lyric placement rule');
{
  const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
  const ROW = read('words/index.json').rows[0], WORDS = read('words/' + ROW.file);
  const n = Math.ceil(WORDS.durationSec * PEAKS_PER_SEC), peaks = new Float32Array(n * 2);
  for (let i = 0; i < n; i++) { const a = 0.22 + 0.5 * Math.max(0, Math.sin(((i / PEAKS_PER_SEC) / 40) * Math.PI * 2)) ** 2; peaks[i * 2] = -a; peaks[i * 2 + 1] = a; }
  const road = wordedRoad({ peaks, durationSec: WORDS.durationSec, name: ROW.title, hash: ROW.hash, words: WORDS });
  /** Drive the brain down a whole track at cruise, x 0, and hold every spawn to the rule. */
  function drive(chart, label, want) {
    const TR = createTrackState();
    TR.setTrack(chart);
    const ch = TR.track.chart, dur = TR.track.durationSec, openEnd = ch.acts[0].t1;
    const nextEventT = (t, kind) => { const x = TR.nextEvent(t, kind); return x ? x.t : null; };
    const LAP = 3000, spots = [];
    for (let d = 20; d < LAP; d += 30) spots.push({ d, x: 0 });
    const pk = createPickups({ rng: makeRng(5), spots, totalDepth: LAP });
    const spawns = []; pk.onEvent((e) => { if (e.type === 'pickupSpawn') spawns.push({ ...e, t, kd: kartD }); });
    let t = 0, kartD = 0;
    while (t < dur) {
      kartD = (kartD + SPEED * DT) % LAP; t += DT;
      pk.update(DT, { d: kartD, x: 0, speed: SPEED, elapsed: t, opening: t < openEnd, mult: 1, t, nextEventT });
    }
    const arrive = (s) => s.t + (s.d - s.kd) / SPEED;
    ok(spawns.length >= want, `${spawns.length} pickups lit over ${dur.toFixed(0)} s of ${label} (${ch.events.length} events)`);
    ok(spawns.every((s) => s.t >= openEnd), `none in the first act (it ends at ${openEnd.toFixed(0)} s)`);
    const tooClose = spawns.filter((s) => ch.events.some((e) => Math.abs(e.t - arrive(s)) < TUNE.CLEAR_SEC));
    eq(tooClose.length, 0, `every take lands ${TUNE.CLEAR_SEC} s clear of every event`);
    const lead = (s) => { const x = nextEventT(arrive(s), 'trigger'); return x == null ? -1 : x - arrive(s); };
    const inLead = (s) => lead(s) >= TUNE.GOLD_LEAD_SEC[0] && lead(s) <= TUNE.GOLD_LEAD_SEC[1];
    ok(spawns.every((s) => (s.id === 'golden_touch') === inLead(s)), 'golden touch exactly when the next trigger is 6 to 9 s past the take, never otherwise');
    console.log('      ' + spawns.map((s) => `${s.id}@${s.t.toFixed(0)}s`).join(' '));
  }
  // the real transcript is dense (an event every second or two past the first act): the rule leaves
  // it very few clear takes, and that is the rule doing its job, not a starved brain
  drive(road, ROW.title, 1);
  drive(demoChart({ durationSec: 300 }), 'the demo road', 3);
}

/* ============================================================================
 * 6. the browser: each of the seven, lit, taken, its chip up, on the demo road
 * ==========================================================================*/
console.log('the browser');
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8875, DBG = 9345;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg',
  '.webp': 'image/webp', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.gltf': 'model/gltf+json', '.mp3': 'audio/mpeg', '.ogg': 'audio/ogg', '.wav': 'audio/wav', '.woff2': 'font/woff2', '.txt': 'text/plain' };
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
const prof = mkdtempSync(join(tmpdir(), 'race-pickups-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DBG}`, `--user-data-dir=${prof}`, '--no-first-run', '--no-default-browser-check',
  '--disable-gpu', '--mute-audio', '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required', '--window-size=390,844', 'about:blank'], { stdio: 'ignore' });
let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch(`http://127.0.0.1:${DBG}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await finish(1); }
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0, errs = [], logs = [], missing = [];
const waits = new Map();
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push('thrown: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text || '?'));
  if (m.method === 'Runtime.consoleAPICalled') { const line = m.params.args.map((a) => a.value ?? a.description ?? '?').join(' '); (m.params.type === 'error' ? errs : logs).push(line); }
  if (m.method === 'Network.responseReceived' && m.params.response.status >= 400 && !m.params.response.url.endsWith('favicon.ico')) missing.push(m.params.response.url);
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const perf = async () => JSON.parse(await ev('JSON.stringify(window.__race.race.perf())'));
await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
const site = `http://127.0.0.1:${PORT}`;
for (const p of PICKUPS) {
  errs = []; logs = []; missing = [];
  await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&chart=demo&dur=240&pickup=${p.id}` });
  let up = false;
  for (let i = 0; i < 120 && !up; i++) { await sleep(250); up = await ev('!!(window.__race && window.__race.race && window.__race.race.track && window.__race.race.perf().world)'); }
  let lit = false;
  for (let i = 0; i < 200 && up && !lit; i++) { await sleep(50); lit = logs.some((l) => l.includes(`pickup: ${p.id} lit`)); }
  let chip = 0;
  for (let i = 0; i < 100 && lit && !chip; i++) { await sleep(50); chip = await ev('document.querySelectorAll(\'.rh-pchip\').length'); }
  const name = await ev('(document.querySelector(\'.rh-pchip-name\') || {}).textContent');
  ok(up && lit && chip === 1 && name === p.name, `${p.name}: lit on a spot ahead, taken, its chip up${chip === 1 ? '' : ` (chips ${chip}, lit ${lit})`}`);
  if (p.id === 'riptide' && chip) {
    await sleep(2500);
    const s = await perf();
    ok(s.speed >= s.pace.base * 1.08, `riptide runs the cruise faster (${s.speed.toFixed(1)} m/s over a base of ${s.pace.base.toFixed(1)})`);
  }
  if (p.id === 'the_pump' && chip) { await sleep(300); ok((await perf()).boosting, 'the pump rides a boost'); }
  ok(errs.length === 0 && missing.length === 0, `no console error and nothing missing for ${p.name}` + (errs.length || missing.length ? ':\n    ' + [...errs, ...missing].slice(0, 4).join('\n    ') : ''));
}
await finish(fails ? 1 : 0);

async function finish(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\npickups-check: all good');
  process.exit(code);
}
