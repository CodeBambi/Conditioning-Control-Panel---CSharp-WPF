/* ============================================================================
 * race/wordBubbles.js - THE SCRIPT WRITTEN ON THE ROAD.
 *
 *   phrasesFrom(words, hits, opts) -> [{ t, t1, lane, x, words: [{ t, d, w }] }]
 *
 * One word, one bubble. The owner's picture, and the only picture: a bubble does
 * not carry a sentence, it carries a word, and the sentence is what a LINE of them
 * spells out down one lane of the road.
 *
 * THE LANE RULE, which is the whole of this file. Words abut: eleven of every
 * fifteen gaps in these transcripts are under half a second, and a lane change is
 * 0.12 s of steering plus the easing either side of it. So a line of word bubbles
 * scattered one lane per word would be a road nobody can read and nobody can pop.
 * Instead the words are grouped into PHRASES - a run of speech with no gap in it
 * longer than PHRASE_GAP_SEC, cut on punctuation and at PHRASE_MAX_WORDS - and
 * every bubble of one phrase sits in ONE lane, in a line, on its own second. The
 * lane may only step BETWEEN phrases, one lane at a time, and only when the
 * silence between them is long enough to steer through. The player reads the line,
 * drives the line, and steers in the gap where the voice stopped.
 *
 * A phrase of five words or more is cut at four and simply CONTINUES the line: the
 * gap inside a sentence is far under the steering gap, so the next four words
 * inherit the lane they were already in. The cut is there so a tag never has to
 * hold a whole sentence, not to move the road.
 *
 * WHAT THE ROW OWNS: ITS OWN SPAN, AND NOT THE SENTENCE AROUND IT. A fingerprinted
 * trigger keeps its row of five across the whole road (race/cues.js `case 'trigger'`)
 * and every face of that row already wears the phrase. So the row owns the seconds
 * the phrase is being said, `hit.t` to `hit.t + hit.dur`, plus a physical margin
 * either side, and nothing else. The words leading INTO the phrase and the words
 * coming out of it are not the row's and they stay on the road.
 *
 * That is the whole of this fix. The guard used to reach HIT_GUARD_SEC (0.4 s) either
 * side of the whole span, which took the sentence around every drop, sleep and good
 * girl on the shelf: 2,270 words, eleven percent of every transcript we have, and
 * 274 s of Bubble Acceptance where the road could only say set labels. The phrase is
 * still CUT at a row rather than straddling it, so a line that runs into a trigger
 * stops there and the words on the far side start a new line in a new lane.
 *
 * THE MARGIN IS PHYSICS, not taste. race/consts.js POP_HIT_D is 1.4 m, and the pop
 * test in race/bubbles.js is `|rel| < POP_HIT_D`, so two bubbles less than 2.8 m of
 * road apart can sit in the box together and one pass takes both. The slowest the
 * road ever cruises is the opening ramp at OPEN_PACE of KART_BASE_SPEED, 15.4 m/s,
 * where HIT_GUARD_PRE_SEC buys 3.1 m; at cruise it is 4.4 m and at the boost cap
 * 6.8 m. Only a held brake (KART_MIN_SPEED, 8 m/s) gets under the 2.8 m, and a kart
 * on the brake is not driving a line anyway.
 *
 * THE MERGE. Two words closer together than MERGE_SEC share one bubble, because at
 * 22 m/s the pop box is only poppable for 0.06 s and two bubbles that close are one
 * bubble the player can only take once. Never three: a third word that close is a
 * transcript collision (one file has eight words sharing a single second) and is
 * dropped, which is also what keeps the spacing between bubbles at MERGE_SEC or more.
 *
 * SPEED DOES NOT LIVE HERE. Every number below is in SECONDS of the file. The kart's
 * speed decides how far apart the bubbles are laid on the road, never how many there
 * are: run.js places each one at the depth the kart will have reached by its word
 * (race/sync.js depthFor) and keeps it there. A faster lap is a longer road, not a
 * busier one.
 *
 * Pure: no three, no DOM, no clock, no fetch. `node race/smoke/words-rate-check.mjs`
 * runs it over all eleven transcripts.
 * ==========================================================================*/

import { LANE_X } from './cues.js';

/**
 * The rule, in one table. Every one of these is in seconds of the file except the
 * two counts. Change one and re-run race/smoke/words-rate-check.mjs: it holds the
 * whole shelf of transcripts against them.
 */
export const WORD_RULE = Object.freeze({
  /** Two words closer than this share one bubble. A third that close is dropped. */
  MERGE_SEC: 0.12,
  /** Silence longer than this between two words ends the phrase. */
  PHRASE_GAP_SEC: 0.3,
  /** And so does a fourth bubble, so a tag never has to hold a sentence. */
  PHRASE_MAX_WORDS: 4,
  /**
   * THE GUARD, which is the trigger's own span and a margin. No word bubble in the
   * seconds `hit.t - HIT_GUARD_PRE_SEC` to `hit.t + hit.dur + HIT_GUARD_POST_SEC`:
   * the words inside the span are the phrase the row is already saying, and the two
   * margins are the road that keeps a word bubble out of the pop box the row's line
   * is in. PRE is the wider of the two because the player meets it first, at speed,
   * with the row filling the glass behind it: 0.2 s is 3.1 m at the slowest cruise
   * the road has (opening ramp, 15.4 m/s) against the 2.8 m two bubbles need to be
   * apart not to share the box. A hit with no length of its own still keeps PRE
   * behind it, so the far side of the line is cleared by the same margin.
   */
  HIT_GUARD_PRE_SEC: 0.2,
  HIT_GUARD_POST_SEC: 0.1,
  /** The silence a line needs before the next one may sit in another lane. */
  LANE_MOVE_SEC: 0.3,
  /** And how many lanes it may move when it does. One. */
  LANE_STEP_MAX: 1,
});

/** A word that ends a clause ends the phrase, whatever the timing says. */
export const CUT_PUNCT = /[,.;:!?…]["')\]]?$/;

const num = (v, d) => (typeof v === 'number' && isFinite(v) ? v : d);
/** The centre lane, where every track's first line starts. */
export const LANE_MID = LANE_X.length >> 1;

/** Only what a bubble can be made of: a word with a second, sorted, nothing else. */
function readWords(raw) {
  return (Array.isArray(raw) ? raw : [])
    .filter((w) => w && typeof w === 'object' && typeof w.w === 'string' && w.w && isFinite(num(w.t, NaN)))
    .map((w) => ({ t: num(w.t, 0), d: Math.max(0, num(w.d, 0)), w: w.w }))
    .sort((a, b) => a.t - b.t);
}

/**
 * The seconds a trigger row owns: the span of the phrase itself, `hit.t` to
 * `hit.t + hit.dur`, with PRE of road in front of it and POST behind. A hit with no
 * length of its own is held open to PRE on the far side too, so the line is cleared
 * by the same margin whichever way the kart meets it. `hits` may be the chart's
 * trigger EVENTS or the raw hits off the words file; both carry `t` and `dur`.
 */
export function guardWindows(hits, pre = WORD_RULE.HIT_GUARD_PRE_SEC, post = WORD_RULE.HIT_GUARD_POST_SEC) {
  const p = Math.max(0, num(pre, WORD_RULE.HIT_GUARD_PRE_SEC));
  const q = Math.max(0, num(post, WORD_RULE.HIT_GUARD_POST_SEC));
  return (Array.isArray(hits) ? hits : [])
    .filter((h) => h && isFinite(num(h.t, NaN)))
    .map((h) => {
      const t = num(h.t, 0), dur = Math.max(0, num(h.dur, 0));
      return { t0: t - p, t1: t + Math.max(dur + q, p) };
    })
    .sort((a, b) => a.t0 - b.t0);
}

/** Is this second inside a row's window? The list is sorted, so a walking cursor does it. */
function inGuard(windows, i, t) {
  let k = i;
  while (k < windows.length && windows[k].t1 < t) k++;
  return { i: k, hit: k < windows.length && t >= windows[k].t0 };
}

/**
 * The road's script.
 *
 * @param words  the caption track: [{ t, d, w }], the shape `chart.words` is in
 *               (race/cloudChart.js captionWords, which on a script-aligned file is
 *               every word the voice says and on any other one is cut at CAPTION_CONF)
 * @param hits   the trigger events (or the words file's own hits): [{ t, dur }]
 * @param opts   { rng, rule } - rng seeds the lane walk, rule overrides WORD_RULE
 * @returns phrases, in order: `{ t, t1, lane, x, words }` where `words` are the
 *          BUBBLES of the line (one word each, two when they merged), `lane` is an
 *          index into cues.js LANE_X and `x` is that lane in metres.
 */
export function phrasesFrom(words, hits = [], opts = {}) {
  const R = { ...WORD_RULE, ...(opts.rule || {}) };
  const rng = typeof opts.rng === 'function' ? opts.rng : Math.random;
  const list = readWords(words);
  const windows = guardWindows(hits, R.HIT_GUARD_PRE_SEC, R.HIT_GUARD_POST_SEC);

  // ---- 1. the bubbles: guarded words dropped, close words merged ------------
  // `cut` on a bubble means "the phrase ends here": a row took the words that
  // followed, or the word itself closed a clause. `guarded` is the same flag it
  // always was, only on the narrower window: it rides to the next word that gets
  // through so the line RESUMES on the far side of the row instead of running
  // through it as if the trigger had never been said.
  const bubbles = [];
  let gi = 0, guarded = false, collided = 0;
  for (const w of list) {
    const g = inGuard(windows, gi, w.t);
    gi = g.i;
    if (g.hit) { guarded = true; continue; }        // the row says this word; the phrase stops here
    const last = bubbles[bubbles.length - 1];
    if (last && !last.cut && !guarded && w.t - last.t < R.MERGE_SEC) {
      if (last.n >= 2) { collided++; continue; }    // three words on one second: a transcript collision
      last.w += ' ' + w.w;
      last.n = 2;
      last.d = Math.max(last.d, w.t + w.d - last.t);
      last.cut = CUT_PUNCT.test(w.w);
      continue;
    }
    bubbles.push({ t: w.t, d: w.d, w: w.w, n: 1, cut: CUT_PUNCT.test(w.w), guarded });
    guarded = false;
  }

  // ---- 2. the phrases: a run of speech with no steerable gap in it ----------
  const phrases = [];
  let cur = null;
  for (const b of bubbles) {
    const gap = cur ? b.t - (cur.t1) : Infinity;
    const full = cur && cur.words.length >= R.PHRASE_MAX_WORDS;
    if (!cur || b.guarded || gap > R.PHRASE_GAP_SEC || cur.closed || full) {
      cur = { t: b.t, t1: b.t + b.d, gap: cur ? gap : Infinity, closed: false, words: [] };
      phrases.push(cur);
    }
    cur.words.push({ t: b.t, d: b.d, w: b.w });
    cur.t1 = Math.max(cur.t1, b.t + b.d);
    cur.closed = b.cut;
  }

  // ---- 3. the lane walk: one step per phrase, and only through a real gap ---
  // Start in the middle. A phrase that follows another too closely to steer through
  // keeps the lane it is in (this is the "a phrase of five words continues the
  // line" case, and every four-word cut inside a sentence). A phrase with room in
  // front of it ALWAYS moves, because a line that could have moved and did not
  // reads as one long line rather than as a new thing being said.
  const step = Math.max(1, Math.round(num(R.LANE_STEP_MAX, 1)));
  const top = LANE_X.length - 1;
  let lane = LANE_MID;
  for (const p of phrases) {
    if (p.gap >= R.LANE_MOVE_SEC) {
      const dir = lane <= 0 ? 1 : lane >= top ? -1 : (rng() < 0.5 ? -1 : 1);
      lane = Math.max(0, Math.min(top, lane + dir * step));
    }
    p.lane = lane;
    p.x = LANE_X[lane];
    delete p.closed;
  }
  phrases.collided = collided;
  return phrases;
}

/**
 * The phrases flattened back into the chart events run.js spends: one `word` event
 * per bubble, carrying the text it wears and the lane its phrase put it in.
 * race/chart.js normalizeEvents keeps both fields; race/cues.js `case 'word'` reads
 * them and lays a tracked bubble of one on that second.
 */
export function wordEventsFrom(words, hits = [], opts = {}) {
  const out = [];
  let n = 0;
  for (const p of phrasesFrom(words, hits, opts)) {
    // `p` is the index of the LINE this bubble belongs to, and it is the only thing that tells the
    // rest of the game where one thing said ends and the next begins: race/score.js steps the combo
    // ladder once per phrase taken whole (forty word pops is twenty seconds of a chant and an 8x
    // nobody drove for) and race/chart.js stats() counts a phrase as one thing to take.
    for (const b of p.words) out.push({ kind: 'word', t: b.t, dur: b.d, label: '', conf: 1, weight: 1, w: b.w, x: p.x, p: n });
    n++;
  }
  return out;
}

/**
 * What the smoke counts: bubbles, phrases, the worst five second window, the
 * tightest spacing, the lane changes. Pure arithmetic over the phrases above, kept
 * here so the rule and its own measure never drift apart.
 */
export function rateOf(phrases, winSec = 5) {
  const flat = [];
  for (const p of phrases) for (const b of p.words) flat.push({ t: b.t, lane: p.lane });
  let minGap = Infinity, changes = 0, worstBubbles = 0, worstChanges = 0, worstAt = 0;
  for (let i = 1; i < flat.length; i++) minGap = Math.min(minGap, flat[i].t - flat[i - 1].t);
  const changeT = [];
  for (let i = 1; i < phrases.length; i++) {
    if (phrases[i].lane !== phrases[i - 1].lane) { changes++; changeT.push(phrases[i].t); }
  }
  for (let i = 0, j = 0; i < flat.length; i++) {          // bubbles in any winSec window
    while (flat[j].t < flat[i].t - winSec) j++;
    if (i - j + 1 > worstBubbles) worstBubbles = i - j + 1;
  }
  for (let i = 0, j = 0; i < changeT.length; i++) {       // lane changes in any winSec window
    while (changeT[j] < changeT[i] - winSec) j++;
    if (i - j + 1 > worstChanges) { worstChanges = i - j + 1; worstAt = changeT[i]; }
  }
  return { bubbles: flat.length, phrases: phrases.length, minGap, changes, worstBubbles, worstChanges, worstAt,
    perPhrase: phrases.length ? flat.length / phrases.length : 0 };
}

/* ---- THE COVERAGE CHECK -------------------------------------------------- *
 * "we should recheck after generating the track that actually all the words gets
 * displayed" - the owner, 2026-09-08. So every road built off a transcript counts
 * itself as it leaves race/cloudChart.js wordedRoad, stamps the count on
 * `analysis.coverage`, and says one line to the host log.
 *
 * A word is ON THE ROAD if it is either wearing a bubble of its own (`placed`) or
 * inside a trigger row's own span (`rows`), because the row of five is a line of
 * faces all saying that phrase - the words of a `good girl` hit are not missing, they
 * are what the whole width of the road says for those seconds.
 *
 * A word is DROPPED three ways, and the line names each so nobody has to guess:
 *   margin - inside HIT_GUARD_PRE/POST_SEC of a row but not in its span. This is the
 *            pop box, not taste: two bubbles under 2 x POP_HIT_D of road apart sit in
 *            the box together and one pass takes both, so a word this close to the
 *            wall could not be taken separately anyway. 3.3 percent of the shelf.
 *   piled  - three or more words the aligner collapsed onto one instant (see
 *            MERGE_SEC). Physically impossible speech; race/wordBubbles.js un-piles
 *            what it can and merges the rest.
 *   short  - anything else, which should be nothing.
 */

/** The one number: how much of what she said the player can read, and what took the rest. */
export function coverageOf(words, hits = [], events = [], opts = {}) {
  const R = { ...WORD_RULE, ...(opts.rule || {}) };
  const list = readWords(words);
  const windows = guardWindows(hits, R.HIT_GUARD_PRE_SEC, R.HIT_GUARD_POST_SEC);
  const spans = (Array.isArray(hits) ? hits : [])
    .filter((h) => h && isFinite(num(h.t, NaN)))
    .map((h) => ({ t0: num(h.t, 0), t1: num(h.t, 0) + Math.max(0, num(h.dur, 0)) }));

  let placed = 0, lines = 0, singles = 0;
  const seen = new Set();
  for (const e of Array.isArray(events) ? events : []) {
    if (!e || e.kind !== 'word' || typeof e.w !== 'string' || !e.w) continue;
    placed += e.w.trim().split(/\s+/).length;
    const key = e.p == null ? 'e' + e.t : 'p' + e.p;
    if (!seen.has(key)) { seen.add(key); lines++; }
  }
  // a line of ONE bubble: the thing the owner saw when the field was thinning them
  const perLine = new Map();
  for (const e of Array.isArray(events) ? events : []) {
    if (!e || e.kind !== 'word' || typeof e.w !== 'string' || !e.w) continue;
    const key = e.p == null ? 'e' + e.t : 'p' + e.p;
    perLine.set(key, (perLine.get(key) || 0) + 1);
  }
  for (const n of perLine.values()) if (n === 1) singles++;

  let rows = 0, margin = 0, gi = 0;
  for (const w of list) {
    const g = inGuard(windows, gi, w.t);
    gi = g.i;
    if (!g.hit) continue;
    if (spans.some((s) => w.t >= s.t0 && w.t <= s.t1)) rows++; else margin++;
  }
  const said = list.length;
  const onRoad = placed + rows;
  const short = Math.max(0, said - onRoad - margin);
  return { said, placed, rows, onRoad, margin, piled: short, lines, singles,
    pct: said ? onRoad / said : 1 };
}

/** The line the host log gets, and the one race/smoke/coverage-check.mjs prints. Never quotes a word. */
export function coverageLine(name, cov) {
  const pc = (100 * (cov.pct || 0)).toFixed(1);
  return `[race-coverage] ${name || 'track'}: ${pc}% (${cov.onRoad}/${cov.said}) - ${cov.placed} on their own bubble, `
    + `${cov.rows} said by a trigger row; dropped ${cov.said - cov.onRoad}: ${cov.margin} in a row's margin, `
    + `${cov.piled} piled; ${cov.lines} lines, ${cov.singles} of one word`;
}

export default phrasesFrom;

// self-check: node race/smoke/words-rate-check.mjs holds all eleven transcripts against WORD_RULE,
// and node race/smoke/coverage-check.mjs drives the whole shelf through the field's own refusals.
