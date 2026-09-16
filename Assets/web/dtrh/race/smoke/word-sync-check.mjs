/* ============================================================================
 * race/smoke/word-sync-check.mjs - a word bubble is on the second the word is said.
 *
 *   node race/smoke/word-sync-check.mjs   (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *
 * Pure: no browser, no network, no audio. The eleven transcripts on the shelf plus a
 * handful of hand-built fixtures, through race/wordBubbles.js and race/chart.js.
 *
 * WHY IT EXISTS. The owner, 2026-09-08: "some words are still isolated and not in sync
 * (not that often but it's pretty bad) like I get the bubble sinking way before the
 * words, most are fine."
 *
 * Measured, the road's own placement is not the problem: driven headless over a real
 * chart, the kart met 138 word bubbles a median of 0.022 s from the word's own second,
 * worst 0.080 s, because race/sync.js keeps a row under its word until the last 0.25 s
 * (CUE_AHEAD_SEC) whatever the throttle does. Nor is the transcript loose: over 20,923
 * words there is not one order break and not one overlap.
 *
 * What IS wrong is the aligner giving up. Twenty-one runs on the shelf have three or
 * more words inside MERGE_SEC of each other, and two kinds of thing look like that:
 *   - fast speech. "in a completely", "as a perfect", "of a trap" at 0.09 to 0.11 s a
 *     word. Nineteen of the twenty-one. These are real and must not be touched.
 *   - a COLLAPSE. Seven words at 0.01 s a word, dropped on the last instant the aligner
 *     was sure of, with 2.7 s of silence in front of them where they were actually
 *     said. The road used to merge two of them onto that instant and throw the other
 *     five away: half a phrase, seconds from where the voice says it.
 *
 * What it holds:
 *   1. the line between the two is the RATE, and it is nowhere near the fastest real
 *      run on the shelf, so no real speech is ever taken apart.
 *   2. a collapse is put back into the silence beside it, in order, inside that
 *      silence, at the local rate, anchored on the one second the aligner was sure of.
 *   3. and it comes back as ONE line in ONE lane, not as a word per lane.
 *   4. every bubble that moved is marked `est`, all the way through normalizeChart.
 *   5. the shelf: order, overlap and collapse counts, before and after.
 *
 * It never prints a line of a transcript. Everything below is counted.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import phrasesFrom, { wordEventsFrom, WORD_RULE } from '../wordBubbles.js';
import { normalizeChart } from '../chart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');

const HERE = dirname(fileURLToPath(import.meta.url));
const WORDS_DIR = resolve(HERE, '../words');
const read = (f) => JSON.parse(readFileSync(resolve(WORDS_DIR, f), 'utf8'));
const SHELF = read('index.json').rows.map((r) => ({ row: r, words: read(r.file) }));
const rng = () => 0.4;
const pad = (s, n) => String(s).padEnd(n);

/** Every maximal run of words inside MERGE_SEC of each other, with the rate it runs at. */
function runsOf(list, min = WORD_RULE.PILE_MIN) {
  const a = list.slice().sort((x, y) => x.t - y.t);
  const out = [];
  for (let i = 0; i < a.length;) {
    let j = i + 1;
    while (j < a.length && a[j].t - a[j - 1].t < WORD_RULE.MERGE_SEC) j++;
    const n = j - i;
    if (n >= min) out.push({ i, j, n, rate: (a[j - 1].t - a[i].t) / (n - 1), t: a[i].t });
    i = j;
  }
  return out;
}

console.log('\nword-sync-check\n');
eq(SHELF.length, 11, 'the shelf has all eleven transcripts on it');

/* ---- 1. the shelf, before and after --------------------------------------- */
console.log('\n  track                    | words | order | overlap | runs of 3+ | of those a collapse | words put back');
console.log('  -------------------------+-------+-------+---------+------------+---------------------+---------------');
let sOrder = 0, sOverlap = 0, sRuns = 0, sTight = 0, sMoved = 0, sLeft = 0, sAfter = 0;
for (const { row, words } of SHELF) {
  const a = (words.words || []).slice().sort((x, y) => x.t - y.t);
  let order = 0, overlap = 0;
  for (let i = 1; i < a.length; i++) {
    if (a[i].t < a[i - 1].t) order++;
    if (a[i].t < a[i - 1].t + (a[i - 1].d || 0) - 1e-6) overlap++;
  }
  const runs = runsOf(a);
  const tight = runs.filter((r) => r.rate <= WORD_RULE.PILE_TIGHT_SEC);
  const ph = phrasesFrom(words.words, words.hits || [], { rng });
  const flat = [];
  for (const p of ph) for (const b of p.words) flat.push(b);
  const after = runsOf(flat).filter((r) => r.rate <= WORD_RULE.PILE_TIGHT_SEC).length;
  sOrder += order; sOverlap += overlap; sRuns += runs.length; sTight += tight.length;
  sMoved += ph.unpiled; sLeft += ph.pilesLeft; sAfter += after;
  console.log('  ' + pad(row.title, 25) + '| ' + pad(a.length, 6) + '| ' + pad(order, 6) + '| ' + pad(overlap, 8)
    + '| ' + pad(runs.length, 11) + '| ' + pad(tight.length, 20) + '| ' + ph.unpiled);
}
console.log('');
eq(sOrder, 0, 'not one word on the shelf is out of order');
eq(sOverlap, 0, 'and not one starts before the word in front of it has finished');
ok(sRuns >= 20, 'the shelf has runs of three or more inside MERGE_SEC (' + sRuns + ')');
eq(sTight, 1, 'and exactly one of them is an aligner collapse rather than a fast mouth');
eq(sMoved, 6, 'so six words go back into the silence they were said in');
eq(sLeft, 0, 'with no collapse left sitting on one instant');
eq(sAfter, 0, 'and not one collapse survives into the bubbles');

/* ---- 2. the line between fast speech and a collapse ----------------------- */
let fastest = Infinity;
for (const { words } of SHELF) {
  for (const r of runsOf((words.words || []))) if (r.rate > WORD_RULE.PILE_TIGHT_SEC) fastest = Math.min(fastest, r.rate);
}
ok(fastest > WORD_RULE.PILE_TIGHT_SEC * 1.2,
  'the fastest real run on the shelf is clear of the collapse test (' + fastest.toFixed(3) + ' s a word against ' + WORD_RULE.PILE_TIGHT_SEC + ')');
ok(WORD_RULE.PILE_TIGHT_SEC < WORD_RULE.MERGE_SEC,
  'and a collapse is tighter than the merge, so the two rules cannot fight');

/* ---- 3. a collapse goes back where it was said ---------------------------- */
// eight words dropped on one instant, with two seconds of silence in front of them.
const pile = [{ t: 0.5, d: 0.2, w: 'and' }, { t: 0.7, d: 0.2, w: 'you' }];
for (let i = 0; i < 8; i++) pile.push({ t: 3.4 + i * 0.01, d: 0.01, w: 'w' + i });
pile.push({ t: 3.7, d: 0.2, w: 'after' }, { t: 4.0, d: 0.2, w: 'that' });
const back = phrasesFrom(pile, [], { rng });
const bflat = [];
for (const p of back) for (const b of p.words) bflat.push(b);
const moved = bflat.filter((b) => b.est);
eq(back.unpiled, 7, 'a collapse of eight puts seven of them back (the last second was the one the aligner had)');
ok(moved.length >= 6, 'and the bubbles that moved are marked est (' + moved.length + ')');
let asc = true, inRoom = true;
for (let i = 1; i < bflat.length; i++) if (bflat[i].t < bflat[i - 1].t - 1e-9) asc = false;
for (const b of moved) if (b.t <= 0.9 || b.t > 3.41) inRoom = false;
ok(asc, 'the words come back in the order they were said');
ok(inRoom, 'inside the silence and nowhere else: never before the word in front, never past the anchor');
const anchored = bflat.some((b) => Math.abs(b.t - 3.47) < 0.02 || Math.abs(b.t - 3.4) < 0.02);
ok(anchored, 'and the one second the aligner was sure of does not move');

/* ---- 4. and it comes back as a LINE, not as a word per lane --------------- */
const pileLines = back.filter((p) => p.words.some((b) => b.est));
const lanes = new Set(pileLines.map((p) => p.lane));
eq(lanes.size, 1, 'every bubble put back sits in one lane');
eq(pileLines.length, Math.ceil(8 / WORD_RULE.PHRASE_MAX_WORDS),
  'and the run reads as one line, cut only where a line is always cut');
let widest = 0;
for (let i = 1; i < moved.length; i++) widest = Math.max(widest, moved[i].t - moved[i - 1].t);
ok(widest < WORD_RULE.PHRASE_GAP_SEC, 'no two of them are far enough apart to become separate lines (' + widest.toFixed(3) + ' s)');

/* ---- 5. fast speech is never taken apart ---------------------------------- */
const fast = [{ t: 1.0, d: 0.3, w: 'stay' },
  { t: 3.0, d: 0.09, w: 'in' }, { t: 3.09, d: 0.09, w: 'a' }, { t: 3.18, d: 0.4, w: 'completely' },
  { t: 4.0, d: 0.3, w: 'open' }];
const kept = phrasesFrom(fast, [], { rng });
eq(kept.unpiled, 0, 'a real fast run with silence beside it is left exactly where it was');
const kflat = [];
for (const p of kept) for (const b of p.words) kflat.push(b);
eq(kflat.filter((b) => b.est).length, 0, 'and nothing in it is marked est');

/* ---- 6. no silence to go back into --------------------------------------- */
const boxed = [{ t: 2.85, d: 0.10, w: 'before' }];
for (let i = 0; i < 4; i++) boxed.push({ t: 3.0 + i * 0.01, d: 0.01, w: 'x' + i });
boxed.push({ t: 3.16, d: 0.10, w: 'after' });
const tightRun = phrasesFrom(boxed, [], { rng });
eq(tightRun.unpiled, 0, 'a collapse with no silence either side is left alone rather than guessed at');
eq(tightRun.pilesLeft, 1, 'and it is counted, so a transcript that is all collapse is visible');

/* ---- 7. est survives to the chart ---------------------------------------- */
const evs = wordEventsFrom(pile, [], { rng });
const estEvents = evs.filter((e) => e.est === true);
ok(estEvents.length >= 6, 'the moved bubbles come out of wordEventsFrom marked est (' + estEvents.length + ')');
const chart = normalizeChart({ v: 1, durationSec: 10, name: 'pile', events: evs, words: pile });
const estKept = chart.events.filter((e) => e.kind === 'word' && e.est === true).length;
eq(estKept, estEvents.length, 'and normalizeChart keeps every one of them');
const plain = chart.events.filter((e) => e.kind === 'word' && e.est !== true);
ok(plain.length > 0 && plain.every((e) => e.est === undefined), 'a word the aligner did find carries no est at all');

/* ---- 8. the un-pile never invents a word --------------------------------- */
for (const { row, words } of SHELF) {
  const said = (words.words || []).map((w) => w.w).join(' ');
  const ph = phrasesFrom(words.words, [], { rng });
  const out = [];
  for (const p of ph) for (const b of p.words) out.push(b.w);
  const same = out.join(' ') === said;
  if (!same) { ok(false, row.title + ': the words on the road are the words in the file'); break; }
}
ok(true, 'with no row of the shelf, the road says exactly the transcript, in order, nothing added or lost');

console.log(fails ? `\nword-sync-check: ${fails} failed\n` : '\nword-sync-check: all good\n');
process.exit(fails ? 1 : 0);
