/* ============================================================================
 * chart/smoke/maker-check.mjs - the Track Maker's pure half, checked without a
 * browser (chart/MAKER.md, PR M4).
 *
 *   node chart/smoke/maker-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 on any failure)
 *
 * Everything the maker does to a track is a function of (hits, recipe, gap) in
 * maker/model.js, so the placing, the min gap and the exported chart can all be
 * checked here. The last section is the one that matters most: the file this
 * tool writes has to survive race/chart.js normalizeChart untouched, or the
 * author gets a download the race quietly refuses.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  EFFECTS, FX_IDS, KINDS, LEAD_SEC, MIN_GAP_DEF, RECIPES, RECIPE_BY_ID,
  buildChart, bumpIds, clampSlide, fmt, groupsOf, labelOf, newId, pickLine,
  placeAll, placeHit, recipeFor, resetIds, slide, snapshotState,
} from '../maker/model.js';
import { BIN_SEC, countRuns, chantRuns, energyFromPeaks, generate, readWords, roadLine } from '../maker/generate.js';
import { normalizeChart } from '../../race/chart.js';
import { cueFor } from '../../race/cues.js';

const HERE = dirname(fileURLToPath(import.meta.url));
const MAKER = join(HERE, '..', 'maker');

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const near = (got, want, what) => ok(Math.abs(got - want) < 1e-6, what + ' (got ' + got + ', want ' + want + ')');

/* ---- 1. the recipes only ask for things that exist ------------------------ */
for (const r of RECIPES) {
  ok(r.seq.length > 0 && r.gap > 0, r.id + ' is a run with a gap');
  for (const s of r.seq) {
    const [kind, eff] = s.split(':');
    if (kind === 'wall') ok(FX_IDS.includes(eff), r.id + ' asks for a real effect: ' + eff);
    else ok(!!KINDS[kind], r.id + ' asks for a real bubble: ' + kind);
  }
}
ok(FX_IDS.every((id) => EFFECTS[id] && EFFECTS[id].length === 3), 'every effect has a glyph, a name and a hint');

/* ---- 2. placing ----------------------------------------------------------- */
resetIds();
const hit = { id: 'h:good-girl:0', t: 10, setId: 'good-girl', n: 0 };
const shimmer = RECIPE_BY_ID.shimmer;
const run = placeHit(hit, shimmer, 0.4);
eq(run.length, shimmer.seq.length, 'a hit gets one bubble per step of its recipe');
near(run[0].t, 10 - LEAD_SEC, 'the first bubble lands a beat before the word');
near(run[1].t - run[0].t, Math.max(0.4, shimmer.gap), 'the run is spaced by max(min gap, recipe gap)');
ok(run.every((b) => b.group === hit.id && b.trig === hit.setId), 'every bubble knows the word it came from');
eq(run[run.length - 1].kind, 'wall', 'shimmer ends on a wall');
eq(run[run.length - 1].eff, 'melt', 'and the wall carries the recipe effect');
const wide = placeHit(hit, shimmer, 1.2);
near(wide[1].t - wide[0].t, 1.2, 'a wider min gap wins over the recipe gap');
near(placeHit({ ...hit, t: 0.05 }, shimmer, 0.4)[0].t, 0, 'a word in the first breath does not place before zero');

const hits = [hit, { id: 'h:w-obey:0', t: 20, setId: 'w-obey', n: 0 }];
const cfg = { 'good-girl': { on: true, recipe: 'shimmer' }, 'w-obey': { on: false, recipe: 'snap' } };
let bubs = placeAll(hits, cfg, 0.4);
eq(bubs.length, shimmer.seq.length, 'only ticked triggers are placed');
cfg['w-obey'].on = true;
bubs = placeAll(hits, cfg, 0.4);
eq(bubs.length, shimmer.seq.length + RECIPE_BY_ID.snap.seq.length, 'ticking one places the rest of it');
ok(bubs.every((b, i) => i === 0 || bubs[i - 1].t <= b.t), 'the list comes back in time order');
eq(groupsOf(bubs).size, 2, 'two words, two blocks');
eq(recipeFor({}, 'good-girl').id, 'shimmer', 'a set with no config falls back to its default recipe');

/* ---- 3. the min gap is the only thing that says no ------------------------ */
const two = [{ id: 'a', t: 10 }, { id: 'b', t: 11 }];
near(clampSlide(two, new Set(['a']), 10, 0.4, 60), 0.6, 'a shove into a neighbour stops one min gap short');
near(clampSlide(two, new Set(['a']), -100, 0.4, 60), -10, 'nothing slides past the start of the file');
near(clampSlide(two, new Set(['b']), 100, 0.4, 12), 1, 'nothing slides past the end of the file');
near(clampSlide(two, new Set(['a', 'b']), 5, 0.4, 60), 5, 'a whole pick moves together, unblocked by itself');
const tight = [{ id: 'a', t: 10 }, { id: 'b', t: 10.1 }];
ok(clampSlide(tight, new Set(['b']), 0.5, 0.4, 60) >= 0, 'a nudge right never answers by jumping left');
ok(clampSlide(tight, new Set(['b']), -0.5, 0.4, 60) <= 0, 'and a nudge left never jumps right');

const state = { bubs: [{ id: 'a', t: 10 }, { id: 'b', t: 12 }], hits: [{ id: 'h', t: 10.15, setId: 'x' }], sel: new Set(['a', 'h']), minGap: 0.4, durationSec: 60 };
near(slide(state, 0.5), 0.5, 'a clear slide moves the whole delta');
near(state.hits[0].t, 10.65, 'the word tag goes with the bubbles it placed');

/* ---- 4. the bottom line reads like a person wrote it ---------------------- */
const S2 = { bubs: [{ id: 'a', t: 1, kind: 'flash' }, { id: 'b', t: 2, kind: 'wall', eff: 'melt' }, { id: 'c', t: 3, kind: 'wall', eff: 'snap' }], hits: [{ id: 'h', t: 1, setId: 'x' }], sel: new Set(['a', 'b', 'c', 'h']) };
eq(pickLine(S2), '1 flash, 2 walls, 1 word picked. drag one, all slide.', 'the pick line counts what is picked');
eq(pickLine({ ...S2, sel: new Set() }), 'nothing picked', 'no pick, no line');
eq(fmt(75.44), '1:15.4', 'the one time format on the page');

/* ---- 5. ids survive a restore -------------------------------------------- */
resetIds();
const restored = [{ id: 'b7' }, { id: 'b3' }];
bumpIds(restored);
ok(!restored.some((b) => b.id === newId('b')), 'a new wall after a restore cannot land on a restored id');
const shot = snapshotState({ minGap: 0.4, cfg, bubs, hits });
ok(shot.v === 1 && shot.bubs.length === bubs.length && shot.hits.length === hits.length, 'the autosave carries the whole working state');

/* ---- 6. the file it writes is a chart the race accepts -------------------- */
resetIds();
const full = {
  audio: { name: '04 Bambi IQ Lock.mp3', stem: '04 Bambi IQ Lock', hash: 'abc123', durationSec: 300 },
  durationSec: 300, hits, cfg, minGap: MIN_GAP_DEF, sel: new Set(),
  setById: new Map([['good-girl', { id: 'good-girl', name: 'good girl' }], ['w-obey', { id: 'w-obey', name: 'obey' }]]),
};
full.bubs = placeAll(hits, cfg, MIN_GAP_DEF);
full.bubs.push({ id: newId('b'), t: 100, kind: 'wall', eff: 'blackout', group: null, trig: null });
const chart = buildChart(full, new Date(0));
eq(chart.version, 1, 'chart JSON v1');
eq(chart.events.length, full.bubs.length, 'one event per bubble');
eq(chart.source.hash, 'abc123', 'the chart says which copy of the audio it was made from');
eq(chart.source.name, '04 Bambi IQ Lock.mp3', 'and what it was called');
ok(chart.analysis.lexicon.includes('good girl'), 'the words it heard are named in the chart');
eq(labelOf(full, full.bubs[0]), 'good girl', 'an event is labelled with the trigger it came from');
eq(chart.events[chart.events.length - 1].kind, 'mark', 'a wall placed by hand is a mark, not a trigger');
eq(chart.events[0].kind, 'trigger', 'a bubble from a word is a trigger');
ok(chart.events.every((e) => e.hand === true), 'every event is hand made, so nothing re-generates over it');
ok(chart.events.every((e) => e.t >= 0 && e.t <= 300), 'nothing sits outside the audio');

const norm = normalizeChart(JSON.parse(JSON.stringify(chart)));
eq(norm.events.length, chart.events.length, 'normalizeChart keeps every event');
eq(norm.source.durationSec, 300, 'and the length of the file');
const wallEv = norm.events.find((e) => e.cue && e.cue.wall);
ok(!!wallEv, 'a wall event survives with its wall');
eq(wallEv.cue.fx[0].id, 'melt', 'the frame effect rides along');
ok(wallEv.cue.fx[0].dur > 0, 'with the house duration for that effect');
const spawnEv = norm.events.find((e) => e.cue && e.cue.spawn && e.cue.spawn.length);
eq(spawnEv.cue.spawn[0].placement, 'lane', 'a bubble is one lane spawn');
ok(!!KINDS[spawnEv.cue.spawn[0].kindId], 'of a kind the maker offers');
const rows = cueFor(wallEv, {}).spawn;
eq(rows.length, 6, 'race/cues.js opens a wall into a full row plus the air one');
ok(cueFor(wallEv, {}).fx.length === 1, 'and hands the frame effect to the run');
ok(rows.every((b) => b.sure === true), 'and stamps them sure, so the density knob leaves them alone');

/* ---- 7. the road generate.js lays between the triggers -------------------- */
/* A little file that says all of it: a countdown into a drop, a phrase chanted on a
   beat, structure words, a quiet stretch at the end, and a swell in the audio for the
   climb to bite on. Everything the maker writes has to come back out of normalizeChart
   and through cueFor, or the author gets a road the race quietly refuses. */
const DUR = 120, PER = 50;
const peaks = new Float32Array(DUR * PER * 2);
for (let i = 0; i < DUR * PER; i++) {
  const t = i / PER;
  const a = t > 95 ? 0.01 : 0.2 + 0.5 * Math.max(0, Math.sin((t - 20) / 12));
  peaks[i * 2] = -a; peaks[i * 2 + 1] = a;
}
const spoken = [];
let wt = 2;
const say = (w, step = 0.5, conf = 0.99) => { spoken.push({ t: Number(wt.toFixed(2)), d: 0.3, w, conf }); wt += step; };
for (const w of ['relax', 'breathe', 'listen', 'deeper', 'heavy', 'float', 'sinking']) say(w, 3);
for (const w of ['ten', 'nine', 'eight', 'seven', 'six']) say(w, 1.4);
say('drop', 2);
for (let i = 0; i < 5; i++) { say('good', 0.4); say('girl', 2.2); }
for (const w of ['obey', 'blank', 'empty', 'mindless', 'wake', 'awake', 'up']) say(w, 4);
wt = 118; say('finished');
const wordsFile = { version: 1, words: spoken.map((w, i) => ({ i, ...w })), source: { name: 'road.mp3', hash: 'road', durationSec: DUR } };

const bins = energyFromPeaks(peaks, PER, DUR, BIN_SEC);
eq(bins.length, Math.ceil(DUR / BIN_SEC), 'the energy curve is one number per half second of file');
ok(bins.every((v) => v >= 0 && v <= 1), 'and every one of them is 0..1');
ok(Math.max(...bins) > 0.9 && Math.min(...bins) < 0.2, 'the loud half and the quiet half do not flatten into each other');
// a hundred seconds climbing from nothing to half, and one single second at full: dividing by
// the loudest moment would leave the whole climb under 0.5, which is the flat file the owner sees.
const clap = new Float32Array(101 * 2);
for (let i = 0; i < 100; i++) { const a = 0.05 + 0.45 * (i / 99); clap[i * 2] = -a; clap[i * 2 + 1] = a; }
clap[200] = -1; clap[201] = 1;
const climb = energyFromPeaks(clap, 1, 101, 1);
ok(climb[99] > 0.9, 'one loud clap does not push the rest of the file into the floor');
ok(climb[0] < 0.2, 'and the quiet end is still quiet');

eq(countRuns(readWords(wordsFile)).length, 1, 'five numbers in a row are one countdown');
eq(chantRuns(readWords(wordsFile))[0].phrase, 'good girl', 'a phrase said five times on a beat is a chant');

const road = generate({ peaks, perSec: PER, durationSec: DUR, words: wordsFile, now: new Date(0),
  hits: [{ id: 'h:good-girl:0', t: 40, setId: 'good-girl', n: 0 }],
  setById: new Map([['good-girl', { id: 'good-girl', name: 'good girl' }]]) });
for (const kind of ['word', 'count', 'drop', 'chant', 'build', 'peak', 'release', 'silence']) {
  ok(road.events.some((e) => e.kind === kind), 'the road carries a ' + kind);
}
ok(road.events.every((e, i) => i === 0 || road.events[i - 1].t <= e.t), 'and comes back in time order');
ok(road.events.every((e) => e.hand !== true), 'nothing on the road pretends the author placed it');
ok(road.acts.length >= 1 && road.acts[0].t0 === 0, 'the acts start at the start of the file');
ok(road.acts[road.acts.length - 1].t1 === DUR, 'and run to the end of it');
ok(/road placed: \d+/.test(roadLine(road.counts)), 'the status line counts what it placed');

const withRoad = { ...full, road };
const rc = normalizeChart(JSON.parse(JSON.stringify(buildChart(withRoad, new Date(0)))));
eq(rc.energy.length, bins.length, 'the exported chart carries the whole curve');
eq(rc.binSec, BIN_SEC, 'in the bins it was measured in');
eq(rc.analysis.energy, 'maker', 'and says who measured it');
eq(rc.events.length, road.events.length + full.bubs.length, 'the file holds the road and the hand cues together');
eq(rc.events.filter((e) => e.hand === true).length, full.bubs.length, 'and only the hand ones are stamped by hand');
ok(rc.acts.every((a) => a.room), 'every act picked a room');
/* race/cues.js declines a few things on purpose: a mark, a lone number, a guess under the
   confidence floor, and a wake word spoken before the way up. It reads the act to know that,
   so hand it the act the event sits in, the way run.js does, and then everything the road
   placed has to be worth something. */
const actAt = (t) => rc.acts.find((a) => t >= a.t0 && t < a.t1) || null;
const WAKE = new Set(['wake', 'awake', 'waking', 'up', 'open']);
const mute = rc.events.filter((e) => !e.hand && e.kind !== 'mark' && cueFor(e, { act: actAt(e.t) }) === null);
ok(mute.every((e) => e.kind === 'word' && WAKE.has(e.label)),
  'race/cues.js spends every road event but a wake word before the way up'
  + (mute.length ? ' (silent: ' + mute.slice(0, 4).map((e) => e.kind + ' ' + e.label).join(', ') + ')' : ''));
const dropCue = cueFor(rc.events.find((e) => e.kind === 'drop'), {});
ok(dropCue.jump > 0 && dropCue.spawn.length === 3, 'a drop is a jump and three rings');
ok(cueFor(rc.events.find((e) => e.kind === 'word'), {}).spawn.length === 1, 'a word is one treat to drive through');

/* the point of the generate key: a second run replaces the road and touches nothing else */
const moved = full.bubs.map((b) => ({ ...b }));
moved[0].t += 7;
const again = generate({ peaks, perSec: PER, durationSec: DUR, words: wordsFile, hits: [], setById: null, now: new Date(0) });
const rc2 = normalizeChart(JSON.parse(JSON.stringify(buildChart({ ...full, bubs: moved, road: again }, new Date(0)))));
eq(rc2.events.filter((e) => e.hand === true).length, moved.length, 'a second run keeps every hand moved bubble');
ok(rc2.events.some((e) => e.hand === true && Math.abs(e.t - moved[0].t) < 1e-6), 'exactly where the hand left it');

/* ---- 8. the modules keep their promises ---------------------------------- */
const src = (f) => readFileSync(join(MAKER, f), 'utf8');
ok(!/getComputedStyle/.test(src('timeline.js')), 'nothing reads layout inside the draw loop');
ok(!/decodeAudioData/.test(src('app.js')), 'the decode stays in audio.js, and the buffer never leaves it');
ok(/peaks/.test(src('audio.js')), 'audio.js keeps the peaks');
ok(/fetch\(/.test(src('words.js')) && !/fetch\(/.test(src('audio.js')), 'the audio is read off disk, never uploaded');
ok(!/document\.|window\./.test(src('generate.js')), 'generate.js is pure: it never reaches for the page');
for (const f of ['model.js', 'audio.js', 'words.js', 'timeline.js', 'app.js', 'pick.js', 'save.js', 'generate.js']) {
  ok(src(f).startsWith('/* ====='), f + ' says what it is at the top');
}

console.log(fails ? '\nmaker-check: ' + fails + ' failed' : '\nmaker-check: all good');
process.exit(fails ? 1 : 0);
