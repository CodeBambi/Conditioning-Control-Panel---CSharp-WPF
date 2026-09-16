/* ============================================================================
 * race/cloudChart.js - the W2 seam: a real chart for a track on the CDN.
 *
 *   createChartSource({ ... }) -> { chartFor, prefetch, cancel, dispose }
 *
 * `chartFor(info)` is what `race/cloud.js` calls through `hooks.chart`. It answers
 * a CHART.md chart for a track the player pasted, and it answers FAST: if the road
 * is not in hand inside PARTIAL_MS it hands back a plain road marked
 * `analysis.partial` and swaps the real one in later through `onUpgrade`, so the
 * kart is already moving while an hour of audio is being read.
 *
 * THE FOUR DOORS (race/charts/README.md, and the log says which one was used):
 *
 *   authored by cloudId   an index row keyed off the url. No download at all.
 *   authored by hash      an index row keyed off the CHART.md hash, which is a
 *                         length and the first megabyte. Still no download.
 *   cached                a road this browser generated before, by hash.
 *   generated             fetch, decode, walk the peaks, lay a road.
 *
 * AUTHORED ALWAYS WINS and is used as it was written: no merge, no regeneration,
 * and `chartCache.put` refuses to keep one.
 *
 * THE BRIGHT LINE. The mp3 is fetched by the player's browser straight from the
 * CDN, which answers `Access-Control-Allow-Origin: *`. Nothing of ours is in the
 * middle of it. The bytes are decoded, walked for peaks and dropped; what is kept
 * is a chart, which is timestamps and labels. No byte of audio is uploaded, and
 * the cache lives in this browser.
 *
 * OFF THE FRAME BUDGET. `decodeAudioData` runs off the main thread and the peak
 * walk yields every YIELD_BINS bins, so the kart never stalls behind a decode. The
 * decoded buffer is dropped the moment the peaks are out of it: an hour of 16 kHz
 * mono is 230 MB, which is a lot to be holding on a phone and nothing at all to be
 * holding for four hundred milliseconds. A file longer than MAX_DECODE_SEC is not
 * decoded at all - it gets the demo road and a line saying so, because a phone that
 * runs out of memory mid lap is worse than a road that is not the file's own.
 *
 * NOTHING DIES SILENTLY. A track that will not fetch, will not decode or will not
 * chart falls back to `demoChart` cut to the real duration and renamed to the real
 * track, plus a toast. There is never a dead run.
 * ==========================================================================*/

import { demoChart, normalizeChart } from './chart.js';
import { generate } from '../chart/maker/generate.js';
import { peaksInto, binsPer, binCount } from '../chart/editor/audio.js';
import { cloudIdFrom, hashUrl, hashBytes, loadIndex, findAuthored, isAuthored } from './chartSource.js';
import { loadWords, loadWordsRow } from './words.js';
import { shiftRoad, readNudge } from './wordSync.js';
import { TRIGGER_SETS, rankOf, laysRow, compareHits } from '../chart/editor/triggerSets.js';
import { detect } from '../chart/maker/triggers.js';
import { wordEventsFrom, coverageOf, coverageLine } from './wordBubbles.js';
import { themeFor } from './triggerTheme.js';

/**
 * THE GENERATOR ID. It is the cache key's second half: change any knob below and
 * change this, and every chart the old road wrote is dropped on sight instead of
 * being played back at a player who is owed the new one.
 *
 * v2 (the lyrics wave): a road built on a transcript has words on it and a v1 road
 * has none, so every v1 entry in the cache must regenerate rather than be served.
 * v3 (the sync wave): the trigger seconds come off the fingerprinted `hits` in the
 * words file (tools/racechart/fingerprint.py) when it has them, so a v2 road, laid
 * on the aligner's guess, must regenerate too.
 * v4 (the word bubbles): the whole script is on the road now, one word a bubble in
 * its phrase's lane (race/wordBubbles.js), so a v3 road carries a few dozen loose
 * treats where the new one carries the lyric and cannot be served in its place.
 * v5 (the trigger catalogue, 2026-09-08): the catalogue says every trigger and every
 * alluring word the eleven scripts use, so a v4 road holds a fraction of the rows the
 * new one does and half of those wear the wrong effect. It has to be laid again.
 */
export const GENERATOR_ID = 'web-road-v5';

/* ---- the knobs, and why each one is what it is --------------------------- */
/**
 * BIN_SEC. generate.js defaults to 0.5 s bins. With words in hand that is right:
 * the road is mostly words and the curve only has to carry the mood. With NO words
 * the curve is the whole road, and `buildEvents` reads a peak as a local maximum
 * over +/- 8 bins - four seconds either side at 0.5, which swallows the swell of a
 * spoken sentence whole. At 0.25 the window is +/- 2 s, which is the length of the
 * swell itself, so the build lands on the rise it belongs to instead of on
 * whichever of six rises happened to be loudest.
 */
export const BIN_SEC = 0.25;
/**
 * And the bin the WORDED road uses, which is generate.js's own default. With a
 * transcript the road is mostly words and the curve only has to carry the mood, so
 * the reason BIN_SEC was halved does not apply: at 0.5 the build lands on the swell
 * the way the maker's own roads have it, and the events the player meets are the
 * ones the voice said.
 */
export const WORD_BIN_SEC = 0.5;
/**
 * A word this unsure is not a caption: the transcript guessed and the plate would lie.
 * THE FALLBACK ONLY. On a SCRIPT-ALIGNED file this cut is the wrong question and it is
 * not asked: see isAligned() and captionWords() below.
 */
export const CAPTION_CONF = 0.35;
/**
 * The aligner stamp that says a transcript was written against a SCRIPT rather than
 * heard cold. Every file on race/words/ is whisper large-v3 run over the audio and then
 * aligned to the written script, so the text of every word is the script's text and a low
 * `conf` on one of them means the aligner was unsure of its SECOND, not of the word. That
 * is why the confidence cut has to go for these files: 790 words on the shelf, four
 * percent of everything the voice says, were being thrown away for being hard to time.
 *
 * The stamp is read off the words file's own `engine` when it carries one, and off the
 * race/words/index.json row otherwise (the race copies of these transcripts drop every
 * field the road does not read, `engine` among them, so the row is where it lives here).
 * A file with no stamp, or with an engine that never saw a script, keeps CAPTION_CONF.
 */
export const ALIGNED_ENGINE = /script/i;
/**
 * No two triggers land closer together than this: generate.js's own WORD_GAP, so a
 * phrase said six times in ten seconds is a moment on the road and not a wall of them.
 * The owner's law for this whole lane: too many bubbles is no bubbles.
 */
export const TRIGGER_GAP = 2.2;
/** Peaks per second off the waveform. `energyFromPeaks` bins these down; this is the walk's own rate. */
export const PEAKS_PER_SEC = 50;
/** The rate everything is decoded at. Charts are timestamps: nothing here needs music bandwidth. */
export const DECODE_RATE = 16000;
/**
 * QUIET. generate.js finds silence in the GAPS BETWEEN WORDS, so with no words it
 * sees one gap - the whole file - and lays a 20 s fog over the start line. Those
 * are dropped and silence is read off the energy curve instead, which is what
 * CHART.md says a silence event is anyway: under QUIET_LEVEL for QUIET_MIN_SEC.
 * The floor is 0.06 in CHART.md; 0.08 here because the curve is normalised to the
 * file's own loud end and a hypno file's quiet is room tone, not digital silence.
 */
export const QUIET_LEVEL = 0.08, QUIET_MIN_SEC = 5, QUIET_MAX_SEC = 20;
/**
 * ACTS. `actsFrom` scores act kinds off words. With none, every window scores zero
 * and carries the last kind forward, so an hour of audio comes out as ONE act -
 * one room, one mood, for the whole run. So the acts are read off the curve here:
 * ACT_WIN seconds per window, LOUD/QUIET as the two levels, nothing shorter than
 * ACT_MIN_SEC and no more than ACT_MAX of them, which are generate.js's own limits.
 */
export const ACT_WIN = 30, ACT_MIN_SEC = 45, ACT_MAX = 16, ACT_LOUD = 0.55, ACT_QUIET = 0.22;
/** How long the panel waits for a road before it starts the run on a plain one. */
export const PARTIAL_MS = 2500;
/** Longer than this is not decoded: the buffer would be most of a phone's memory. */
export const MAX_DECODE_SEC = 5400;
/** Bins between yields in the peak walk. 4000 bins is 80 s of audio, about 8 ms of work. */
export const YIELD_BINS = 4000;
/** Said when a track could not be charted and the demo road is standing in for it. */
export const FALLBACK_LINE = 'could not read that file, driving a stand-in road';
export const TOO_LONG_LINE = 'that file is too long to chart here, driving a stand-in road';

const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);
const r3 = (v) => Math.round(v * 1000) / 1000;
/** Hand the frame back. `setTimeout` and not a microtask: a microtask does not let anything draw. */
const yieldFrame = () => new Promise((r) => setTimeout(r, 0));

/* ---- the no-word road ---------------------------------------------------- */

/**
 * Quiet stretches, read off the energy curve. CHART.md's own definition of a
 * silence event, and the reason generate.js's word-gap silences are dropped for a
 * wordless file: it would see one gap, the whole file, and fog the start line.
 */
export function quietFromEnergy(energy, binSec, durationSec) {
  const out = [];
  let run = -1;
  const close = (endBin) => {
    if (run < 0) return;
    const t0 = run * binSec, len = (endBin - run) * binSec;
    if (len >= QUIET_MIN_SEC) out.push({ kind: 'silence', t: r3(t0 + 0.15), dur: r3(Math.min(QUIET_MAX_SEC, len - 0.3)), label: 'quiet', conf: 1, weight: 1 });
    run = -1;
  };
  for (let i = 0; i < energy.length; i++) {
    if (energy[i] <= QUIET_LEVEL) { if (run < 0) run = i; } else close(i);
  }
  close(energy.length);
  // The tail of a file is usually a fade to nothing. Fogging the finish line reads as a
  // broken run rather than as quiet, so a silence that reaches the end is not laid.
  return out.filter((e) => e.t + e.dur < durationSec - 2);
}

/**
 * Acts off the curve alone: which room the racer is in when nobody said anything
 * about it. Window by window, loud is `triggers`, quiet is `deepening`, silent is
 * `silence`, the rest is `free`; the first window is the settle and the last
 * stretch is the way up when the file is going out louder than it came in.
 * Then the same tidy-up generate.js does: no act under ACT_MIN_SEC, no more than
 * ACT_MAX of them.
 */
export function actsFromEnergy(energy, binSec, durationSec) {
  const n = Math.max(1, Math.ceil(durationSec / ACT_WIN)), per = Math.max(1, Math.round(ACT_WIN / binSec));
  const mean = (a, b) => { let s = 0, c = 0; for (let i = a; i < b && i < energy.length; i++) { s += energy[i]; c++; } return c ? s / c : 0; };
  const whole = mean(0, energy.length);
  const kinds = new Array(n);
  for (let i = 0; i < n; i++) {
    const m = mean(i * per, (i + 1) * per);
    kinds[i] = m <= QUIET_LEVEL ? 'silence' : m >= ACT_LOUD ? 'triggers' : m <= ACT_QUIET ? 'deepening' : 'free';
  }
  kinds[0] = 'induction';
  const tail = Math.max(1, Math.floor(n * 0.88));
  if (mean(tail * per, energy.length) > whole) for (let i = tail; i < n; i++) kinds[i] = 'wake';
  for (let i = 1; i < n - 1; i++) if (kinds[i - 1] === kinds[i + 1] && kinds[i] !== kinds[i - 1]) kinds[i] = kinds[i - 1];

  const acts = [];
  for (let i = 0; i < n; i++) {
    const last = acts[acts.length - 1];
    if (last && last.kind === kinds[i]) last.t1 = Math.min(durationSec, (i + 1) * ACT_WIN);
    else acts.push({ kind: kinds[i], t0: i * ACT_WIN, t1: Math.min(durationSec, (i + 1) * ACT_WIN) });
  }
  for (let i = acts.length - 1; i > 0; i--) {
    if (acts[i].t1 - acts[i].t0 >= ACT_MIN_SEC) continue;
    acts[i - 1].t1 = acts[i].t1;
    acts.splice(i, 1);
  }
  while (acts.length > ACT_MAX) {
    let s = 1;
    for (let i = 2; i < acts.length; i++) if (acts[i].t1 - acts[i].t0 < acts[s].t1 - acts[s].t0) s = i;
    acts[s - 1].t1 = acts[s].t1;
    acts.splice(s, 1);
  }
  acts[0].t0 = 0;
  acts[acts.length - 1].t1 = durationSec;
  return acts.map((a) => ({ t0: r3(a.t0), t1: r3(a.t1), kind: a.kind, name: a.kind }));
}

/**
 * The whole road for a file with no words: generate.js lays the build, peak and
 * release events off the curve, and the two passes that need words - its silences
 * and its acts - are replaced by the two above. Pure, so the smoke runs it in node.
 *
 * @param {Float32Array} peaks   min/max pairs, PEAKS_PER_SEC of them a second
 */
export function roadFromPeaks({ peaks, perSec = PEAKS_PER_SEC, durationSec, name = 'track', hash = '', now = new Date() }) {
  const g = generate({ peaks, perSec, durationSec, words: { words: [] }, hits: [], binSec: BIN_SEC, now });
  const energy = g.energy.map(clamp01);
  const events = g.events
    .filter((e) => e.kind !== 'silence')                       // word-gap silences: one fog over the whole file
    .concat(quietFromEnergy(energy, BIN_SEC, durationSec))
    .sort((a, b) => a.t - b.t)
    .map((e, i) => ({ ...e, id: 'g' + i }));
  return {
    version: 1, binSec: BIN_SEC, energy, events,
    acts: actsFromEnergy(energy, BIN_SEC, durationSec),
    source: { name, hash, durationSec, sampleRate: DECODE_RATE },
    analysis: { energy: 'web-rms-v1', words: 'none', generatedAt: g.generatedAt, partial: false },
  };
}

/* ---- the worded road ----------------------------------------------------- */

/** The catalogue, by id, the way generate.js wants it for the labels on its events. */
export const SET_BY_ID = new Map(TRIGGER_SETS.map((s) => [s.id, s]));

/**
 * Every trigger the transcript says, over the whole catalogue: the maker's own
 * scan (chart/maker/words.js `scan`), in the shape generate.js reads, so a hit here
 * means what a hit means on the Track Maker page.
 *
 * A set marked `row: false` in the catalogue is skipped: it keeps its id and its place
 * in the Track Maker, but it is an ACCENT WORD on this road, not a row (triggerSets.js).
 *
 * The order is the catalogue's own precedence rule (`compareHits`), not the alphabet:
 * the thinning below keeps the FIRST hit of a second, so the sort IS which set wins a
 * second two of them are true of. Each hit carries the two things that rule reads -
 * `rank` (the set's group) and `nw` (how many words the match covered).
 *
 * @returns [{ id, t, dur, setId, n, rank, nw }] in precedence order
 */
export function scanTriggers(words, durationSec) {
  const list = (words && Array.isArray(words.words)) ? words.words : [];
  // The phrase is only as sure as the least sure word in it: cues.js reads `conf` and
  // spends anything under TRIGGER_SURE as a plain treat with no word on the chrome,
  // which is the right answer for a phrase the transcript was guessing at.
  const conf = (i0, i1) => {
    let low = 1;
    for (let i = i0; i <= i1 && i < list.length; i++) {
      const c = list[i] && typeof list[i].conf === 'number' ? list[i].conf : 1;
      if (c < low) low = c;
    }
    return Math.round(clamp01(low) * 100) / 100;
  };
  const out = [];
  for (const set of TRIGGER_SETS) {
    if (!laysRow(set)) continue;
    const rank = rankOf(set);
    const ms = detect(words, set, [], { durationSec });
    ms.forEach((m, n) => out.push({ id: 'h:' + set.id + ':' + n, t: m.t, dur: r3(Number(m.dur) || 0),
      setId: set.id, n, conf: conf(m.i0, m.i1), rank, nw: Math.max(1, (m.i1 - m.i0) + 1) }));
  }
  return out.sort(compareHits);
}

/** How far a scanned hit may be moved onto a fingerprinted second of the same set. The
 *  fingerprint shifts a phrase by about a quarter second in the median; a whole second is
 *  room to spare, and still much less than the gap a catalogue set clusters two sayings by,
 *  so a hit can never be snapped onto the NEXT time she says the same thing. */
export const FP_SNAP_SEC = 1;

/**
 * THE FINGERPRINT IS THE CLOCK, THE SCAN IS THE ROLL CALL (2026-09-08).
 *
 * `tools/racechart/fingerprint.py` correlates a handful of phrases against the audio
 * itself and writes the seconds it finds into the words file's `hits`, which are truer
 * than the aligner's guess and worth keeping. But that list was written against the
 * catalogue OF THE DAY: every one of the eleven shelf transcripts carries hits for a
 * dozen or so of the old sets and none at all for the fifty the survey added. Reading
 * the file's hits INSTEAD of the scan, which is what this did, meant a new set could
 * never reach the road however plainly the voice says it - the catalogue would grow and
 * nothing would change, silently.
 *
 * So the two are merged, each doing the half it is good at. The live scan says WHICH
 * phrases are said, over the whole catalogue. Then every scanned hit that has a
 * fingerprinted hit of its own set within FP_SNAP_SEC takes that second, that length and
 * that confidence: the phrase keeps the truer clock it had. A fingerprinted hit the scan
 * does not claim is dropped, because the scan is the roll call and the phrase it was
 * found for has either moved to another set (the old flat `pink` hits inside "pink
 * satin") or is not a row any more (`accept`, `relax`, `sleep`).
 */
function snapToFingerprint(scan, fp) {
  const bySet = new Map();
  for (const h of fp) {
    if (!bySet.has(h.setId)) bySet.set(h.setId, []);
    bySet.get(h.setId).push(h);
  }
  const used = new Set();
  return scan.map((s) => {
    const list = bySet.get(s.setId);
    if (!list) return s;
    let best = null, bestD = FP_SNAP_SEC;
    for (const h of list) {
      if (used.has(h)) continue;
      const d = Math.abs((Number(h.t) || 0) - s.t);
      if (d <= bestD) { bestD = d; best = h; }
    }
    if (!best) return s;
    used.add(best);
    return { ...s, t: r3(Number(best.t) || 0), dur: r3(Number(best.dur) || s.dur), src: best.src === 'fp' ? 'fp' : 'words',
      conf: Math.round(clamp01(typeof best.conf === 'number' ? best.conf : s.conf) * 100) / 100 };
  });
}

/**
 * The trigger seconds for a transcript: the live scan over the whole catalogue, with
 * every phrase the words file was fingerprinted for moved onto the second the audio
 * itself put it (see snapToFingerprint). A file with no `hits` is the scan, exactly.
 */
export function triggerHits(words, durationSec) {
  const scan = scanTriggers(words, durationSec);
  const raw = (words && Array.isArray(words.hits)) ? words.hits : null;
  if (!raw || !raw.length) return scan;
  const fp = raw.filter((h) => h && typeof h.setId === 'string' && SET_BY_ID.has(h.setId)
    && laysRow(SET_BY_ID.get(h.setId)) && Number(h.t) >= 0
    && !(durationSec > 0 && Number(h.t) > durationSec));
  if (!fp.length) return scan;
  const perSet = new Map();
  return snapToFingerprint(scan, fp).sort(compareHits).map((h) => {
    const n = perSet.get(h.setId) || 0;
    perSet.set(h.setId, n + 1);
    return { ...h, n, id: 'h:' + h.setId + ':' + n };
  });
}

/**
 * THE TRIGGERS OWN THEIR SECOND.
 *
 * generate.js spends a hit as a `word` event, because on the maker page the trigger
 * itself is a hand-placed recipe sitting on top and the road underneath is only the
 * road. Here there is no recipe: the trigger IS the road, and it is the whole point of
 * this lane, so it is placed first and everything else gives way to it.
 *
 * That matters more than it sounds. `wordEvents` will not put a word inside 1.2 s of a
 * count, a drop or a chant, and a trigger phrase is very often exactly where a drop is
 * (the phrase contains a drop word, which is why it is a trigger at all), so the road
 * generated straight out of generate.js loses most of them. Measured on the opening
 * track: nineteen hits, four of them the phrase this whole road is built on, and not
 * one of the four survived to be a trigger event.
 *
 * So: the hits are thinned against each other by TRIGGER_GAP, first one wins, and then
 * every `word` event generate laid inside TRIGGER_GAP of one is dropped, because it was
 * only ever filler standing where a trigger could not. Counts, drops and chants stay:
 * a jump and a trigger on the same second is the file doing both, and cues.js spends
 * them differently.
 */
/**
 * THE CONFIDENCE ON A SCRIPT-ALIGNED FILE IS A TIMING DOUBT, NOT A WORD DOUBT (2026-09-08).
 * `aligned` says the transcript was aligned to a script that already had the words in it, so a low
 * `conf` means the aligner was unsure WHEN it was said and never whether it was said. captionWords
 * above has lifted CAPTION_CONF for those files since the words road landed; the trigger rows never
 * got the same lift, and race/cues.js was quietly throwing 37 of them away set-wide - eleven of the
 * twelve "bimbo doll" rows of Bubble Induction among them. The flag rides the event so the cue pass
 * can tell the two kinds of doubt apart without knowing where the file came from.
 */
export function triggersFromHits(events, hits, setById, { aligned = false } = {}) {
  const kept = [];
  for (const h of hits) {
    const set = setById.get(h.setId);
    if (!set) continue;
    // `prev` is a { h, set } pair, so `prev.t` was undefined and `h.t - undefined` is NaN, and
    // NaN < TRIGGER_GAP is false: the gap has never thinned a single hit (fixed 2026-09-08). It
    // did not show, because fingerprint.py had already spaced the seconds it wrote into the words
    // files and the whole catalogue only landed a dozen phrases a track. With the survey's
    // catalogue on it the live scan finds twelve hundred, and the gap is the only thing between
    // the player and a wall of rows.
    const prev = kept[kept.length - 1];
    if (prev && h.t - prev.h.t < TRIGGER_GAP) continue;
    kept.push({ h, set });
  }
  const triggers = kept.map(({ h, set }) => ({
    kind: 'trigger', t: r3(h.t), dur: r3(h.dur || 0), label: String(set.name).toLowerCase(),
    conf: typeof h.conf === 'number' ? h.conf : 1, weight: 1, setId: set.id, cue: set.preset,
    ...(aligned ? { aligned: true } : null),
  }));
  const near = (t) => triggers.some((x) => Math.abs(x.t - t) < TRIGGER_GAP);
  return events.filter((e) => !(e.kind === 'word' && near(e.t))).concat(triggers);
}

/**
 * Was this transcript aligned to a script? The file's own `engine` stamp answers first,
 * the race/words/index.json row (which race/words.js hands along on the loaded file)
 * second. Anything else is a transcript somebody heard cold, and its `conf` is a real
 * measure of whether the word is the word.
 */
export function isAligned(words) {
  const e = words && typeof words.engine === 'string' ? words.engine : '';
  return !!e && ALIGNED_ENGINE.test(e);
}

/**
 * The caption track: what the voice says and when, and nothing the plate cannot show.
 * On a script-aligned file that is EVERY word, because the aligner's `conf` is its
 * confidence in the timing and the text came off the script either way. A word that
 * was hard to place is still the word she said, and it is inked and sized like any
 * other: the road makes nothing of a low `conf` and shows no sign of one.
 */
export function captionWords(words) {
  const raw = (words && Array.isArray(words.words)) ? words.words : [];
  const aligned = isAligned(words);
  return raw
    .filter((w) => w && typeof w.w === 'string' && w.w && typeof w.t === 'number'
      && (aligned || typeof w.conf !== 'number' || w.conf >= CAPTION_CONF))
    .map((w) => ({ t: r3(w.t), d: r3(Number(w.d) || 0), w: w.w }));
}

/**
 * The road for a file we have the transcript of. generate.js does the whole of it -
 * counts, drops, chants, the words themselves, the build/peak/release off the curve,
 * the silences in the gaps between words and the acts off what is being said - because
 * every one of those passes has the words it was written for. The two replacements the
 * WORDLESS road needs are exactly the two that only exist because it has none.
 *
 * Then the trigger events, the caption track, and the lexicon the plate and the bubble
 * mapping both read. Pure, so the smoke runs it in node.
 */
export function wordedRoad({ peaks, perSec = PEAKS_PER_SEC, durationSec, name = 'track', hash = '', words, now = new Date() }) {
  const hits = triggerHits(words, durationSec);
  const g = generate({ peaks, perSec, durationSec, words, hits, setById: SET_BY_ID, binSec: WORD_BIN_SEC, now });
  const energy = g.energy.map(clamp01);
  const caps = captionWords(words);
  const road = triggersFromHits(g.events, hits, SET_BY_ID, { aligned: isAligned(words) });
  const triggers = road.filter((e) => e.kind === 'trigger');
  // THE SCRIPT IS THE ROAD. generate.js spends a handful of STRUCTURE words as loose treats in
  // random lanes, which was the right answer while the road had no transcript on it and is the
  // wrong one now: the transcript has every word the voice says, so those few are dropped and
  // race/wordBubbles.js lays the whole script instead - one word a bubble, a phrase a lane, and
  // never inside a trigger's own span (plus the pop box margin either side), because those
  // seconds are the phrase the row of five is already saying on all its faces.
  const events = road.filter((e) => e.kind !== 'word')
    .concat(inkWords(wordEventsFrom(caps, triggers), triggers))
    .sort((a, b) => a.t - b.t)
    .map((e, i) => ({ ...e, id: 'g' + i }));
  const lexicon = [...new Set(events.filter((e) => e.kind === 'trigger').map((e) => e.label))].sort();
  // THE COVERAGE CHECK (2026-09-08, the owner's "recheck after generating the track that actually
  // all the words gets displayed"). Counted here, where the road is finished and the transcript is
  // still in hand, and carried on the chart so the cache keeps it and window.__race can read it back.
  const coverage = coverageOf(caps, triggers, events);
  return {
    version: 1, binSec: WORD_BIN_SEC, energy, events, acts: g.acts,
    words: caps,
    source: { name, hash, durationSec, sampleRate: DECODE_RATE },
    analysis: { energy: 'web-rms-v1', words: 'script-align-v1', lexicon, coverage, generatedAt: g.generatedAt, partial: false },
  };
}

/** How near a trigger a word has to be to be part of what that trigger is doing. */
export const WORD_INK_SEC = 6;
/**
 * The colour a word bubble's tag is written in (race/cues.js reads it back through
 * race/triggerTheme.js). A word inside WORD_INK_SEC of a trigger belongs to that set and wears its
 * theme; everything else stays pink. The field is `cue`, which race/chart.js already carries for
 * every kind, so it survives the round trip without a new one. Both lists are in time order.
 */
export function inkWords(words, triggers) {
  const list = Array.isArray(triggers) ? triggers : [];
  if (!list.length) return words;
  let j = 0;
  for (const e of words) {
    while (j + 1 < list.length && Math.abs(list[j + 1].t - e.t) <= Math.abs(list[j].t - e.t)) j++;
    if (Math.abs(list[j].t - e.t) > WORD_INK_SEC) continue;
    const row = themeFor(list[j]);
    if (row && row.preset) e.cue = row.preset;
  }
  return words;
}

/* ---- the decode ---------------------------------------------------------- */

/**
 * Bytes in, peaks out. Decoded ONCE into a throwaway OfflineAudioContext at
 * DECODE_RATE, walked in chunks with the frame handed back between them, and the
 * buffer dropped on the way out of this function.
 */
export async function peaksFromBytes(buf, { ctx = null, onProgress = null } = {}) {
  const Ctx = ctx ? null : (typeof OfflineAudioContext !== 'undefined' ? OfflineAudioContext : (typeof webkitOfflineAudioContext !== 'undefined' ? webkitOfflineAudioContext : null));
  if (!ctx && !Ctx) throw new Error('this browser has no audio decoder');
  const ac = ctx || new Ctx(1, DECODE_RATE, DECODE_RATE);
  const audio = await ac.decodeAudioData(buf);
  const rate = audio.sampleRate, length = audio.length;
  const chans = [];
  for (let c = 0; c < audio.numberOfChannels; c++) chans.push(audio.getChannelData(c));
  const per = binsPer(rate, PEAKS_PER_SEC), bins = binCount(length, per);
  const peaks = new Float32Array(bins * 2);
  for (let b = 0; b < bins; b += YIELD_BINS) {
    const end = Math.min(bins, b + YIELD_BINS);
    peaksInto(peaks, chans, length, per, b, end);
    if (onProgress) { try { onProgress(end / bins); } catch (e) { /* nobody listening */ } }
    if (end < bins) await yieldFrame();
  }
  return { peaks, perSec: PEAKS_PER_SEC, durationSec: audio.duration, sampleRate: rate };
}

/* ---- the source ---------------------------------------------------------- */

/** A chart with no events at all: the plain road a run starts on while the real one is read. */
function plainRoad(name, durationSec) {
  return normalizeChart({
    version: 1, binSec: 0.5, energy: [], events: [],
    acts: [{ t0: 0, t1: durationSec, kind: 'induction', name: 'the settle' }],
    source: { name, hash: '', durationSec, sampleRate: DECODE_RATE },
    analysis: { energy: '', words: 'none', partial: true },
  });
}

/**
 * @param {object}   o
 * @param {string}   o.indexUrl        where race/charts/index.json lives
 * @param {object}   [o.cache]         race/chartCache.js, or null for none
 * @param {function} [o.onUpgrade]     (chart) -> void, the real road landing on a partial one
 * @param {function} [o.onStage]       (id, word) -> void, which of the four steps this track is on
 * @param {function} [o.toast]         (line) -> void
 * @param {function} [o.log]
 * @param {function} [o.fetch]         the smoke's seam
 * @param {number}   [o.partialMs]
 */
export function createChartSource({ indexUrl, cache = null, onUpgrade = null, onStage = null, toast = null, log = null, fetch: f = null, partialMs = PARTIAL_MS } = {}) {
  const get = f || (typeof fetch !== 'undefined' ? fetch : null);
  const say = (m) => { try { if (log) log('chart: ' + m); } catch (e) { /* no log */ } };
  const shout = (m) => { try { if (toast) toast(m); } catch (e) { /* no toast */ } };
  /** Which step a track is on, for whatever is drawing its row. '' takes the word back off. */
  const stage = (id, word) => { try { if (onStage) onStage(id, word); } catch (e) { /* nobody listening */ } };
  // README: a row's `chart` is written relative to `race/`, not to the index beside it, so
  // `charts/x.chart.json` is one readable path in the file instead of `./x.chart.json`.
  const chartUrl = (rel) => new URL(String(rel), new URL('../', indexUrl)).href;
  let ahead = null;                    // { id, promise } - the one prefetch allowed at a time
  let dead = false;

  /** An authored chart file. Used as written, or not used: a broken one is not repaired. */
  async function authoredChart(row, fallbackName) {
    const res = await get(chartUrl(row.chart), { credentials: 'omit' });
    if (!res || !res.ok) throw new Error('authored chart answered ' + (res ? res.status : 'nothing'));
    const json = await res.json();
    if (!isAuthored(json)) throw new Error('that file is in the index but is not marked `hand: true`');
    const src = (json.source && typeof json.source === 'object') ? json.source : {};
    return normalizeChart({ ...json, source: { ...src, name: src.name || row.title || fallbackName } });
  }

  /**
   * A road out of the cache, wearing THIS track's name. The cache is keyed on the file,
   * and one file can sit at two urls under two names: the road is the same road, but the
   * plate, the marquee and the results card all read `source.name` and they must say what
   * the player pasted, not what the last person to paste those bytes called them.
   */
  const named = (chart, title) => normalizeChart({ ...chart, source: { ...chart.source, name: title || (chart.source && chart.source.name) || 'track' } });

  /**
   * THE OFFSET (race/wordSync.js), applied ONCE, here, as a worded road leaves the source: the
   * words/index.json row's `offsetSec` plus the `[` `]` nudge kept in localStorage for this track.
   * Every word-derived second moves together (bubbles, rows, plates, band) and the cache holds the
   * road at offset 0, so a nudge made after a road was cached still lands. `row` may be handed in
   * by the generated door; the cached doors look it up (one index read, no transcript fetched).
   */
  async function tuned(chart, { cloudId, hash, row = null }) {
    if (!chart.words || !chart.words.length) return chart;
    let r = row;
    if (!r) { try { r = await loadWordsRow({ cloudId, hash, fetch: get, log }); } catch (e) { r = null; } }
    const key = (r && r.cloudId) || cloudId || hash || '';
    const sec = (r ? r.offsetSec : 0) + readNudge(key);
    if (!key) return chart;
    return normalizeChart(shiftRoad(chart, sec, { trackId: key }));
  }

  /** Fetch, decode, walk, lay a road. The only path that downloads the whole file. */
  async function generated({ id, url, title, durationSec, head, cloudId }) {
    stage(id, 'reading');
    const res = await get(url, { mode: 'cors', credentials: 'omit' });
    if (!res || !res.ok) throw new Error('the file answered ' + (res ? res.status : 'nothing'));
    let bytes = new Uint8Array(await res.arrayBuffer());
    // The hash could not be worked out ahead of the download (no HEAD, no range): it can be
    // worked out now, so the cache still hits next time even on a CDN that will do neither.
    const hash = head && head.hash ? head.hash : await hashBytes(bytes, bytes.length);
    if (hash && cache) {
      const hit = await cache.get(hash, GENERATOR_ID);
      if (hit) { say('cached (late hash) ' + hash.slice(0, 8)); return { chart: await tuned(named(hit, title), { cloudId, hash }), door: 'cached' }; }
    }
    const buf = bytes.buffer;
    bytes = null;
    stage(id, 'decoding');
    const walked = await peaksFromBytes(buf);
    stage(id, 'charting');
    const dur = durationSec > 0 ? durationSec : walked.durationSec;
    if (Math.abs(walked.durationSec - dur) > 1) say(`the element says ${Math.round(dur)}s and the file decodes to ${Math.round(walked.durationSec)}s; the element is the clock`);
    // The transcript, if this track has one. Same origin, small, and never fatal: a track
    // with no words gets exactly the road it got before this lane landed.
    const words = await loadWords({ cloudId, hash, fetch: get, log });
    const road = (words && words.words.length)
      ? wordedRoad({ peaks: walked.peaks, perSec: walked.perSec, durationSec: dur, name: title, hash, words })
      : roadFromPeaks({ peaks: walked.peaks, perSec: walked.perSec, durationSec: dur, name: title, hash });
    if (words && words.words.length) {
      say(`${title}: ${road.words.length} words and ${road.analysis.lexicon.length} triggers on the road`);
      if (road.analysis.coverage) say(coverageLine(title, road.analysis.coverage));   // the owner's recheck
    }
    if (hash && cache) await cache.put(hash, road, GENERATOR_ID);   // at offset 0: tuned() shifts on the way out
    return { chart: await tuned(normalizeChart(road), { cloudId, hash, row: words }), door: 'generated' };
  }

  /** The four doors, in order, for one track. Throws only when every one of them failed. */
  async function resolve(info) {
    const { url, title } = info;
    const durationSec = Number(info.durationSec) || 0;
    const cloudId = cloudIdFrom(url);
    stage(info.id, 'naming');
    const index = await loadIndex(indexUrl, { fetch: get, log });

    const byId = findAuthored(index, { cloudId });
    if (byId) return { chart: await authoredChart(byId.row, title), door: 'authored by cloudId' };

    // The hash is a length and the first megabyte, so both remaining lookups that can
    // answer without the file get their chance BEFORE anything is downloaded.
    let head = null;
    // race/levels.json wrote this file's length down, so the hash is ONE ranged GET and no HEAD.
    try { head = await hashUrl(url, { fetch: get, log, byteLength: Number(info.bytes) || 0 }); } catch (e) { say('hash: ' + ((e && e.message) || e)); }
    const hash = head && head.hash ? head.hash : '';
    if (hash) {
      const byHash = findAuthored(index, { hash });
      if (byHash) return { chart: await authoredChart(byHash.row, title), door: 'authored by hash' };
      if (cache) {
        const hit = await cache.get(hash, GENERATOR_ID);
        if (hit) return { chart: await tuned(named(hit, title), { cloudId, hash }), door: 'cached' };
      }
    }
    if (durationSec > MAX_DECODE_SEC) { shout(TOO_LONG_LINE); throw new Error(`${Math.round(durationSec / 60)} minutes is past the ${Math.round(MAX_DECODE_SEC / 60)} minute decode limit`); }
    return generated({ id: info.id, url, title, durationSec, head, cloudId });
  }

  /** Resolve, log the door, and fall back to the demo road rather than to a dead run. */
  async function settle(info) {
    const t0 = Date.now();
    try {
      const { chart, door } = await resolve(info);
      say(`${info.title}: ${door} in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
      stage(info.id, '');
      return chart;
    } catch (err) {
      say(`${info.title}: ${(err && err.message) || err}`);
      shout(FALLBACK_LINE);
      stage(info.id, '');
      const dur = Number(info.durationSec) > 0 ? Number(info.durationSec) : 240;
      const demo = demoChart({ durationSec: dur });
      return normalizeChart({ ...demo, source: { ...demo.source, name: info.title || demo.source.name, hash: '' } });
    }
  }

  /** The one in-flight resolve, so `chartFor` on the next lap takes what prefetch already has. */
  function claim(info) {
    if (ahead && ahead.id === info.id) { const p = ahead.promise; ahead = null; return p; }
    return settle(info);
  }

  return {
    /**
     * `hooks.chart`. Answers inside partialMs whatever happens: the real road if it
     * is ready by then, otherwise a plain road that the real one replaces through
     * `onUpgrade` when it lands.
     */
    async chartFor(info) {
      const work = claim(info);
      let landed = false;
      work.then(() => { landed = true; }, () => { landed = true; });
      await Promise.race([work, new Promise((r) => setTimeout(r, partialMs))]);
      if (landed) return work;
      say(info.title + ': still reading, starting on a plain road');
      work.then((chart) => { if (!dead && onUpgrade) { try { onUpgrade(chart); } catch (e) { say('upgrade: ' + e); } } }, () => { /* settle never rejects */ });
      return plainRoad(info.title || 'track', Number(info.durationSec) || 240);
    },

    /**
     * Get the NEXT track's road while this one is being driven, so the next lap
     * starts with its chart in hand. One at a time, and a new one replaces the old.
     */
    prefetch(info) {
      if (dead || !info || !info.url || !info.id) { ahead = null; return; }
      if (ahead && ahead.id === info.id) return;
      say('reading ahead: ' + info.title);
      ahead = { id: info.id, promise: settle(info) };
    },
    /** The panel closed. Nothing is aborted mid flight; it is simply not waited on. */
    cancel() { ahead = null; },
    dispose() { dead = true; ahead = null; },
    get pending() { return ahead ? ahead.id : null; },
  };
}

export default createChartSource;
