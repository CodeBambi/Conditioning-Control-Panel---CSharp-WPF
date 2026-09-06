/* ============================================================================
 * chart/smoke/generate-check.mjs - the road generator and the peak walk, checked
 * under node with no browser.
 *
 *   node chart/smoke/generate-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 on any failure)
 *
 * CARVE-OUT: this is the generate + peaks half of chart/smoke/maker-check.mjs
 * and chart/smoke/peaks-check.mjs from the Track Maker chain, trimmed to the two
 * files this branch carries (maker/generate.js, editor/audio.js). The canonical
 * checks live on the chain; this one exists so app main can prove the road it
 * generates still comes back out of race/chart.js normalizeChart and through
 * race/cues.js cueFor, or the web build gets a road the race quietly refuses.
 * ==========================================================================*/

import { BIN_SEC, countRuns, chantRuns, energyFromPeaks, generate, readWords, roadLine } from '../maker/generate.js';
import { peaksFromChannels, peaksInto, binsPer, binCount, hashFile } from '../editor/audio.js';
import { normalizeChart } from '../../race/chart.js';
import { cueFor } from '../../race/cues.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const near = (a, b, eps = 0.02) => Math.abs(a - b) <= eps;

/* ---- 1. the peak walk ----------------------------------------------------- */
/* A 2 s 16 kHz sweep with a silent half second in the middle: the bin count matches
   the file length, the loud bins reach the rails, the silent ones sit at zero, a
   stereo pair mixes down to the average, and a chunked walk lands on the same numbers. */
const RATE = 16000, SECS = 2, PER_SEC = 50, N = RATE * SECS;
const left = new Float32Array(N), right = new Float32Array(N);
for (let i = 0; i < N; i++) {
  const t = i / RATE;
  const v = t >= 0.5 && t < 1 ? 0 : Math.sin(2 * Math.PI * (200 + 400 * t) * t);
  left[i] = v; right[i] = v * 0.5;
}
const per = binsPer(RATE, PER_SEC), nb = binCount(N, per);
ok(per === 320 && nb === 100, 'a 2 s 16 kHz file at 50 bins per second is 100 bins of 320 samples');
const mono = peaksFromChannels([left], N, RATE, PER_SEC);
ok(mono.length === nb * 2, 'the peaks are one min and one max per bin');
ok(near(mono[1], 1, 0.05) && near(mono[0], -1, 0.05), 'the first bin runs rail to rail');
const silentFrom = Math.floor(0.5 * PER_SEC), silentTo = Math.floor(1 * PER_SEC);
let quiet = true, loud = true;
for (let b = silentFrom + 1; b < silentTo - 1; b++) if (mono[b * 2] !== 0 || mono[b * 2 + 1] !== 0) quiet = false;
for (let b = silentTo + 1; b < nb; b++) if (mono[b * 2 + 1] < 0.5) loud = false;
ok(quiet, 'the silent half second reads as flat zero');
ok(loud, 'every bin after the gap is loud again');
const stereo = peaksFromChannels([left, right], N, RATE, PER_SEC);
ok(near(stereo[1], 0.75, 0.05), 'a stereo pair mixes down to the average of the two');
const chunked = new Float32Array(nb * 2);
for (let b = 0; b < nb; b += 7) peaksInto(chunked, [left], N, per, b, Math.min(nb, b + 7));
ok(chunked.every((v, i) => v === mono[i]), 'a chunked walk lands on exactly the same numbers');
ok(peaksFromChannels([new Float32Array(0)], 0, RATE, PER_SEC).length === 2, 'an empty file is one flat bin, not a crash');

/* ---- 2. the hash ---------------------------------------------------------- */
/* CHART.md: SHA1 of the byte length as 8 bytes LE plus the first 1 MiB. Checked against
   the same sum computed here by hand, so align.py and the browser keep agreeing. */
const bytes = new Uint8Array(3000);
for (let i = 0; i < bytes.length; i++) bytes[i] = (i * 31) & 255;
const fakeFile = { size: bytes.length, slice: (a, b) => ({ arrayBuffer: async () => bytes.slice(a, b).buffer }) };
const want = new Uint8Array(8 + bytes.length);
new DataView(want.buffer).setBigUint64(0, BigInt(bytes.length), true);
want.set(bytes, 8);
const wantHex = [...new Uint8Array(await crypto.subtle.digest('SHA-1', want))].map((b) => b.toString(16).padStart(2, '0')).join('');
eq(await hashFile(fakeFile), wantHex, 'the file hash is the CHART.md length plus head sum');
eq(wantHex.length, 40, 'and it is 40 hex characters');

/* ---- 3. the road generate.js lays between the triggers -------------------- */
/* A little file that says all of it: a countdown into a drop, a phrase chanted on a
   beat, structure words, a quiet stretch at the end, and a swell for the climb. */
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

/* the same road with no words at all: an energy-only file still gets a climb */
const bare = generate({ peaks, perSec: PER, durationSec: DUR, words: null, now: new Date(0) });
ok(bare.events.length > 0 && bare.events.every((e) => e.kind !== 'word'), 'no words file means a road of energy alone');
ok(bare.acts.length >= 1, 'and it still picks a room');

/* ---- 4. the race takes what it wrote ------------------------------------- */
const chart = { version: 1, source: wordsFile.source, binSec: road.binSec, energy: road.energy, acts: road.acts,
  events: road.events, analysis: { energy: 'maker', words: 'words', generatedAt: road.generatedAt } };
const rc = normalizeChart(JSON.parse(JSON.stringify(chart)));
eq(rc.energy.length, bins.length, 'the exported chart carries the whole curve');
eq(rc.binSec, BIN_SEC, 'in the bins it was measured in');
eq(rc.events.length, road.events.length, 'normalizeChart keeps every road event');
ok(rc.acts.every((a) => a.room), 'every act picked a room');
/* cues.js is allowed to say nothing about a plain word (a wake word before the wake act, a
   number outside a countdown); everything else on the road has to become something. */
const actAt = (t) => rc.acts.find((a) => t >= a.t0 && t < a.t1) || rc.acts[rc.acts.length - 1];
ok(rc.events.filter((e) => e.kind !== 'word').every((e) => cueFor(e, { act: actAt(e.t) }) !== null), 'race/cues.js has something to say about every road event that is not a bare word');
ok(rc.events.some((e) => e.kind === 'word' && cueFor(e, { act: actAt(e.t) }) !== null), 'and about most of the words');
const dropCue = cueFor(rc.events.find((e) => e.kind === 'drop'), {});
ok(dropCue.jump > 0 && dropCue.spawn.length === 3, 'a drop is a jump and three rings');
ok(cueFor(rc.events.find((e) => e.kind === 'word'), {}).spawn.length === 1, 'a word is one treat to drive through');

console.log(fails ? fails + ' FAILED' : 'generate-check: all good');
process.exit(fails ? 1 : 0);
