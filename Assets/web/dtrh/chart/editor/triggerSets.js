/* ============================================================================
 * editor/triggerSets.js - the trigger catalogue, as data (chart/EDITOR.md, PR U5).
 *
 * Data only: no imports, no DOM, no window, so rules.js, triggers.js and
 * model.js can all read it without a cycle. Every phrase was counted over the
 * eleven aligned Bambi Sleep tracks, so a count of 0 in the tab means the file
 * really does not say it, never that the phrase is wrong. One fixed hue per
 * set, so a marker reads the same in every project (UX appendix B). Every hue
 * here clears 5.2:1 against the ink `labelInk` gives it (PR U8).
 *
 * Shared with the Track Maker: chart/maker/triggers.js re-exports this whole
 * catalogue, so a trigger means the same phrase, the same colour and the same
 * cue on both pages. Change a set here and both tools change with it.
 *
 * THE 2026-09-08 SURVEY. Every trigger, alluring word and structure phrase in
 * the eleven transcripts was counted and given one effect (the owner: "all the
 * triggers can go in"). What that wave changed here:
 *   - the word sets are REGEX now, not bare `exact` stems, so "emptying",
 *     "forgotten" and "wiped away" land the row "empty" and "forget" already had
 *   - the families the catalogue had no word for at all went in: the doll and
 *     lock words, the sleep words, the melt words, the cock words, the falling
 *     words, the drain words
 *   - `drop for cock` has its own set. It used to fall into `w-drop` and wear a
 *     spiral, which is the wrong effect for the loudest phrase in track 09
 *   - `countdown` is its own MODE. The old regex wanted the numbers side by
 *     side and found neither of the two real countdowns, because both scripts
 *     put a whole sentence between one number and the next (maker/triggers.js)
 *   - three sets carry `row: false`: they are said so often that a row each
 *     would wall the road end to end. They keep their id, their colour and
 *     their place in the Track Maker; race/cues.js gives them a bigger word
 *     bubble instead (ACCENT_WORDS)
 *   - eight sets nobody says in these eleven files stay exactly as they were.
 *     They are for the audio this shelf does not carry yet
 *
 * PRECEDENCE. A second of road holds ONE row (race/cloudChart.js thins anything
 * inside TRIGGER_GAP of a kept hit), and several sets can be true of the same
 * second: "bimbo doll bimbo doll bimbo doll" is `bimbo-doll` AND `chant`, "drop
 * for cock" is `drop-for-cock` AND `w-drop`. Which one the road laid used to be
 * whichever set id sorted first, which is not a rule, it is an accident of
 * spelling. The rule, in order:
 *   1. the lower RANK wins - a named trigger (0) beats a sequence (1) beats a
 *      word (2) beats an author's own set (3). A phrase the script wrote as a
 *      trigger outranks a shape somebody noticed it makes.
 *   2. then the LONGER match - more words matched is more of the line spoken.
 *   3. then the set id, alphabetically, so the answer is the same every run.
 * `compareHits` below is that rule, and it is the only place it is written.
 * ==========================================================================*/

export const TRIGGER_SETS = [
  /* ---- named triggers: the phrases the scripts install on purpose ---------- */
  { id: 'bambi-sleep', name: 'bambi sleep', group: 'named', phrase: '\\bbambi sleeps?\\b', mode: 'regex', color: '#b080ff', preset: 'blackout', cluster: 1.5 },
  { id: 'sleep-now', name: 'sleep now', group: 'named', phrase: '\\bsleep now\\b|\\bbrain off\\b', mode: 'regex', color: '#6f4f9c', preset: 'blackout', cluster: 1.5 },
  // the praise IS a line, so the card says it back. 147 of them wore the pink blink, and that one
  // set was most of why every road came out pink; the whisper card is the quieter, truer read.
  { id: 'good-girl', name: 'good girl', group: 'named', phrase: '\\bgood girl\\b', mode: 'regex', color: '#ff69b4', preset: 'card-whisper', cluster: 1.5 },
  { id: 'bimbo-doll', name: 'bimbo doll', group: 'named', phrase: '\\bbimbo doll\\b', mode: 'regex', color: '#ff3da5', preset: 'lock-hold', cluster: 1.5 },
  { id: 'bambi-freeze', name: 'bambi freeze', group: 'named', phrase: 'bambi freeze', mode: 'exact', color: '#8ae6ff', preset: 'freeze-snap' },
  { id: 'bambi-reset', name: 'bambi reset', group: 'named', phrase: 'bambi reset', mode: 'exact', color: '#ffffff', preset: 'snap-shake' },
  // it says lock; it should lock. It was a glitch, which shakes instead of holding.
  { id: 'uniform-lock', name: 'bambi uniform lock', group: 'named', phrase: '\\b(?:bambi )?uniform lock\\b', mode: 'regex', color: '#ffd166', preset: 'lock-hold', cluster: 1.5 },
  { id: 'bambi-limp', name: 'bambi limp', group: 'named', phrase: '\\bbambi limp\\b', mode: 'regex', color: '#ffc0dd', preset: 'lock-hold', cluster: 1.5 },
  { id: 'drip-drop', name: 'drip drop', group: 'named', phrase: 'drip drop', mode: 'exact', color: '#78ffbe', preset: 'golden-rain' },
  { id: 'snap-forget', name: 'snap and forget', group: 'named', phrase: 'snap and forget', mode: 'exact', color: '#f4f2ff', preset: 'snap-shake' },
  { id: 'zap-cock-drain', name: 'zap cock drain', group: 'named', phrase: 'zap cock drain', mode: 'exact', color: '#ff5c6c', preset: 'flash-pulse' },
  { id: 'primped', name: 'primped and pampered', group: 'named', phrase: 'primped and pampered', mode: 'exact', color: '#ffb3d9', preset: 'treats' },
  { id: 'does-as-told', name: 'bambi does as she is told', group: 'named', phrase: "does as she(?:'s| is) told", mode: 'regex', color: '#c8a8ff', preset: 'spiral-air' },
  // the cock family, and the owner's "we should also use often the fullscreen gif overlay": the
  // three phrases that ARE the takeover wear the wash, and the words around them alternate wash
  // and rain, so the loudest scene on the shelf is loud in two ways rather than one.
  { id: 'drop-for-cock', name: 'drop for cock', group: 'named', phrase: '\\bdrop for cock\\b', mode: 'regex', color: '#ff8a3d', preset: 'gif-wash', cluster: 1.5 },
  { id: 'cockslut', name: 'bambi cockslut', group: 'named', phrase: '\\bcock ?slut\\b', mode: 'regex', color: '#ee7078', preset: 'gif-wash', cluster: 1.5 },
  { id: 'takeover', name: 'bambi takeover', group: 'named', phrase: '\\btake(?:s|n)? ?over\\b|\\btakes? (?:complete |full )?control\\b', mode: 'regex', color: '#e05a80', preset: 'gif-wash', cluster: 2 },
  // the bubble tracks' own moment, and the game's own bubble too
  { id: 'burst-bubble', name: 'burst your bubble', group: 'named', phrase: '\\bburst (?:your|the) bubble\\b|\\bbubble pops?\\b|\\bfeel it pop\\b', mode: 'regex', color: '#ff9ecb', preset: 'pink-blink', cluster: 3 },
  { id: 'name-bambi', name: 'the name bambi', group: 'named', phrase: '\\b(?:the name bambi|your name is bambi|name is bambi|you are bambi)\\b', mode: 'regex', color: '#ff5fa8', preset: 'pink-blink', cluster: 2 },
  { id: 'awaken', name: 'bambi awaken', group: 'named', phrase: '\\bawaken(?:ed|s)?\\b|\\bwake up\\b|\\bwide awake\\b', mode: 'regex', color: '#ffd6a0', preset: 'flash-pulse', cluster: 2 },
  { id: 'giggle-time', name: 'giggle time', group: 'named', phrase: 'giggle ?time', mode: 'regex', color: '#ffe066', preset: 'treats' },

  /* ---- sequences: a shape in the script rather than a phrase --------------- */
  // `mode: 'countdown'` reads descending numbers with anything in between, because the scripts
  // count with a sentence between one number and the next. The phrase is the human label only.
  { id: 'countdown', name: 'countdown', group: 'sequence', phrase: 'five four three two one', mode: 'countdown', color: '#ffd166', preset: 'spiral-air' },
  { id: 'chant', name: 'chant (a word said 3+ times in a row)', group: 'sequence', mode: 'regex', color: '#ff69b4', preset: 'card-whisper',
    phrase: '\\b(\\w+(?: \\w+)?)\\b(?: \\1\\b){2,}' },
  { id: 'your-trigger', name: 'your trigger', group: 'sequence', mode: 'regex', color: '#d0a0ff', preset: 'card-whisper', cluster: 4,
    phrase: '\\b(?:your|this|the|a) (?:new |strong |common |simple )?triggers?\\b|\\btrigger phrase\\b|\\bsleep command\\b' },
  { id: 'forgetting-wonderful', name: 'forgetting feels wonderful', group: 'sequence', phrase: '\\bforgetting feels wonderful\\b', mode: 'regex', color: '#9a97b8', preset: 'drain-fog', cluster: 2 },

  /* ---- words: the blank family (the drain) -------------------------------- */
  { id: 'w-blank', name: 'blank', group: 'words', phrase: '\\bblank(?:ness|ly|ing|er|ed)?\\b', mode: 'regex', color: '#ece8ff', preset: 'drain-fog', cluster: 2 },
  { id: 'w-empty', name: 'empty', group: 'words', phrase: '\\bempt(?:y|ied|ies|ying|iness)\\b', mode: 'regex', color: '#8fd0ff', preset: 'drain-fog', cluster: 2 },
  { id: 'w-dumb', name: 'dumb', group: 'words', phrase: '\\bdumb(?:er|est)?\\b|\\bstupid\\b|\\bbrainless\\b|\\bairhead\\b', mode: 'regex', color: '#ffd166', preset: 'drain-fog', cluster: 2 },
  { id: 'w-mindless', name: 'mindless', group: 'words', phrase: '\\bmindless(?:ly)?\\b', mode: 'regex', color: '#c8a8ff', preset: 'drain-fog', cluster: 2 },
  { id: 'w-forget', name: 'forget', group: 'words', phrase: '\\bforget(?:s|ting|ful)?\\b|\\bforgotten\\b|\\berased?\\b|\\bwiped? away\\b', mode: 'regex', color: '#9a97b8', preset: 'drain-fog', cluster: 2 },
  { id: 'w-drain', name: 'drain', group: 'words', phrase: '\\bdrain(?:s|ed|ing)?\\b|\\bsucked? (?:out|away|all)\\b|\\bsucks it\\b', mode: 'regex', color: '#7d8bd0', preset: 'drain-fog', cluster: 2 },
  { id: 'w-iq', name: 'iq', group: 'words', phrase: '\\biq\\b|\\bintelligence\\b', mode: 'regex', color: '#a6b4ff', preset: 'drain-fog', cluster: 2 },

  /* ---- words: the melt family (soft, pink, sagging) ----------------------- */
  { id: 'w-cotton-candy', name: 'cotton candy', group: 'words', phrase: '\\bcotton ?candy\\b', mode: 'regex', color: '#ffc2e2', preset: 'melt', cluster: 2 },
  { id: 'w-satin', name: 'pink satin', group: 'words', phrase: '\\bpink satin\\b|\\bsatin\\b', mode: 'regex', color: '#ff9fd0', preset: 'melt', cluster: 2 },
  { id: 'w-melting', name: 'melting', group: 'words', phrase: '\\bmelt(?:s|ed|ing)?\\b', mode: 'regex', color: '#ffb0c8', preset: 'melt', cluster: 2 },

  /* ---- words: the pink family (the reward) -------------------------------- */
  { id: 'w-pink', name: 'pink', group: 'words', phrase: '\\bpink\\b(?! satin)', mode: 'regex', color: '#ff3da5', preset: 'pink-wall', cluster: 2 },
  { id: 'w-pleasure', name: 'pleasure', group: 'words', phrase: '\\bpleasure\\b|\\bfeels? so (?:good|nice|right)\\b|\\bbliss(?:ful|fully)?\\b', mode: 'regex', color: '#ff77b9', preset: 'pink-blink', cluster: 3 },

  /* ---- words: the command family (a card, quiet) -------------------------- */
  { id: 'w-obey', name: 'obey', group: 'words', phrase: '\\bobey(?:s|ed|ing)?\\b|\\bobedien(?:t|ce)\\b', mode: 'regex', color: '#b080ff', preset: 'card-whisper', cluster: 2 },
  { id: 'w-surrender', name: 'surrender', group: 'words', phrase: '\\bsurrender(?:s|ed|ing)?\\b|\\bgives? in\\b|\\bgiving in\\b', mode: 'regex', color: '#c0a0e8', preset: 'card-whisper', cluster: 2 },

  /* ---- words: the down family (the spiral) -------------------------------- */
  { id: 'deeper', name: 'deeper and deeper', group: 'words', phrase: '\\bdeeper\\b', mode: 'regex', color: '#6b8cff', preset: 'spiral-air', cluster: 2.5 },
  // "drop for cock" has its own set above and outranks this one; the lookahead keeps the two apart
  // even where the gap thinning is not what decides it.
  { id: 'w-drop', name: 'drop', group: 'words', phrase: '\\bdrop(?:s|ped|ping)?\\b(?! for cock)|\\bplummet(?:s|ed|ing)?\\b|\\bplung(?:e|es|ed|ing)\\b', mode: 'regex', color: '#78ffbe', preset: 'spiral-air', cluster: 2 },
  { id: 'w-sink', name: 'sink', group: 'words', phrase: '\\bsink(?:s|ing)?\\b|\\bsank\\b', mode: 'regex', color: '#5fc9d6', preset: 'spiral-air', cluster: 2 },
  { id: 'w-drift', name: 'drift', group: 'words', phrase: '\\bdrift(?:s|ed|ing)?\\b|\\bfloat(?:s|ed|ing)?\\b', mode: 'regex', color: '#9fe0ff', preset: 'spiral-air', cluster: 2 },
  { id: 'w-trance', name: 'trance', group: 'words', phrase: '\\btrance\\b|\\bentranced\\b', mode: 'regex', color: '#40d0c0', preset: 'spiral-air', cluster: 2 },
  // "spriling" is track 07's own spelling of it, and the road reads the script, typo and all.
  { id: 'w-spiral', name: 'spiralling', group: 'words', phrase: '\\bspiral(?:s|ed|ing|led|ling)?\\b|\\bspriling\\b', mode: 'regex', color: '#8fb0ff', preset: 'spiral-air', cluster: 2 },
  { id: 'w-further', name: 'further and further', group: 'words', phrase: '\\bfurther and further\\b', mode: 'regex', color: '#7ea0e0', preset: 'spiral-air', cluster: 2 },

  /* ---- words: the giggle family (a shake) --------------------------------- */
  { id: 'w-giggle', name: 'giggle', group: 'words', phrase: '\\bgiggl\\w*\\b', mode: 'regex', color: '#ffe066', preset: 'glitch-shake', cluster: 2 },
  { id: 'w-snap', name: 'snap', group: 'words', phrase: '\\bsnap(?:s|ped|ping)?\\b', mode: 'regex', color: '#fff4c0', preset: 'snap-shake', cluster: 2 },
  // IQ Lock's windshield wipers go side to side, and the glitch is the one effect that does too
  { id: 'w-wipers', name: 'the wipers', group: 'words', phrase: '\\bwipers?\\b|\\bwindshield\\b|\\bwindscreen\\b|\\bswoosh\\w*\\b', mode: 'regex', color: '#d8ff8f', preset: 'glitch-shake', cluster: 3 },

  /* ---- words: the doll family (held still) -------------------------------- */
  { id: 'w-lock', name: 'lock', group: 'words', phrase: '\\block(?:s|ed|ing)?\\b', mode: 'regex', color: '#ffd6a0', preset: 'lock-hold', cluster: 2 },
  { id: 'w-limp', name: 'limp', group: 'words', phrase: '\\blimp\\b', mode: 'regex', color: '#f0c8e0', preset: 'lock-hold', cluster: 2 },
  { id: 'w-plastic', name: 'plastic', group: 'words', phrase: '\\bplastic\\b|\\bbarbie\\b', mode: 'regex', color: '#ffd0e8', preset: 'lock-hold', cluster: 2 },
  { id: 'w-puppet', name: 'puppet', group: 'words', phrase: '\\bpuppet\\b', mode: 'regex', color: '#e8b0ff', preset: 'lock-hold', cluster: 2 },
  // "feminane" is track 07's spelling again; the uniform words are the outfit locking her in.
  { id: 'w-dolled-up', name: 'dolled up', group: 'words', phrase: '\\bdolled up\\b|\\bdress(?:ed|ing) up\\b|\\buniform(?:ed|ing)?\\b(?! lock)|\\bfeminane\\b', mode: 'regex', color: '#ffc0dd', preset: 'lock-hold', cluster: 3 },
  { id: 'w-helpless', name: 'helpless', group: 'words', phrase: '\\bhelpless(?:ly|ness)?\\b', mode: 'regex', color: '#dcc0ff', preset: 'lock-hold', cluster: 2 },
  { id: 'w-needles', name: 'needles', group: 'words', phrase: '\\bneedles?\\b|\\bpumps?\\b|\\bsilicone\\b|\\binject\\w*\\b', mode: 'regex', color: '#ffb0b0', preset: 'lock-hold', cluster: 3 },

  /* ---- words: the sleep family (the lights go out) ------------------------ */
  { id: 'w-gas', name: 'sleeping gas', group: 'words', phrase: '\\bgas\\b|\\bmask\\b|\\banesthetic\\b', mode: 'regex', color: '#a08fd0', preset: 'blackout', cluster: 3 },
  { id: 'w-switch-off', name: 'switch off', group: 'words', phrase: '\\bswitch(?:es|ed)? off\\b|\\bcollaps\\w*\\b|\\bwinks out\\b|\\bknocks? (?:you|bambi) out\\b|\\bshut(?:s|ting)? down\\b', mode: 'regex', color: '#8878b8', preset: 'blackout', cluster: 2 },

  /* ---- words: the cock family (wash and rain, turn about) ----------------- */
  { id: 'w-sucking-cock', name: 'sucking cock', group: 'words', phrase: '\\bsuck(?:ing|s)? (?:\\w+ ){0,3}cock\\b|\\bcock in (?:your|her) mouth\\b|\\bblowjob\\b', mode: 'regex', color: '#ff9a5c', preset: 'gif-wash', cluster: 3 },
  { id: 'w-fuck-doll', name: 'fuck doll', group: 'words', phrase: '\\b(?:fuck|cock|suck) ?(?:doll|puppet|toy|hole|slut)\\b|\\bfuck(?:doll|puppet|toy)\\b', mode: 'regex', color: '#ffc83d', preset: 'gif-rain', cluster: 2 },
  { id: 'w-cum', name: 'cum', group: 'words', phrase: '\\bcums?\\b|\\bcumming\\b|\\borgasms?\\b|\\bcome on command\\b', mode: 'regex', color: '#ffab6b', preset: 'gif-wash', cluster: 3 },
  { id: 'w-machine', name: 'the fucking machine', group: 'words', phrase: '\\bfucking machine\\b|\\bdildo\\b', mode: 'regex', color: '#ffd98f', preset: 'gif-rain', cluster: 3 },

  /* ---- words: said too often to be a row ---------------------------------- */
  // `row: false` keeps the set - its id, its colour, its place in the Track Maker's tab and every
  // saved chart that names it - and takes it off the ROAD. "accept" is said 104 times, 77 of them
  // in one twelve minute track: a row each is one wall of one card and the road stops being read.
  // race/cues.js gives all three a bigger word bubble instead (ACCENT_WORDS), which is the size the
  // word has earned without being the thing that happens to you.
  { id: 'w-accept', name: 'accept', group: 'words', phrase: 'accept', mode: 'exact', color: '#ffb3d9', preset: 'mark', row: false },
  { id: 'w-relax', name: 'relax', group: 'words', phrase: 'relax', mode: 'exact', color: '#b8ffd9', preset: 'mark', row: false },
  { id: 'w-sleep', name: 'sleep', group: 'words', phrase: 'sleep', mode: 'exact', color: '#6b8cff', preset: 'mark', row: false },
];

/** The order the tab lists the groups in, and what it calls them. */
export const SET_GROUPS = [['named', 'named triggers'], ['words', 'words'], ['sequence', 'sequences'], ['custom', 'yours']];

/** PRECEDENCE, step 1: what a group outranks. Lower wins. See the header. */
export const SET_RANK = { named: 0, sequence: 1, words: 2, custom: 3 };

/** The rank of one set: its own `rank` where it names one, else its group's. */
export function rankOf(set) {
  const r = Number(set && set.rank);
  if (Number.isFinite(r)) return r;
  const g = SET_RANK[String((set && set.group) || '')];
  return Number.isFinite(g) ? g : SET_RANK.custom;
}

/** Does this set lay a row on the road? Everything but the three accent words. */
export function laysRow(set) { return !!set && set.row !== false; }

/**
 * PRECEDENCE, the whole rule. Two hits are "the same second" when their seconds
 * round to the same tenth, because two sets that both matched one phrase start on
 * the same WORD but not always on the same hundredth of it.
 *
 * The tenth is the sort key itself, not a test on the way to comparing raw seconds:
 * a comparator that says 1.04 == 1.00, 1.09 == 1.04 and 1.09 > 1.00 is not an order
 * at all, and Array#sort hands back a list that is not even in time order for one.
 *
 * @param a,b `{ t, rank, nw, setId }` - `nw` is how many words the match covered.
 */
export function compareHits(a, b) {
  const ta = Math.round((Number(a.t) || 0) * 10), tb = Math.round((Number(b.t) || 0) * 10);
  if (ta !== tb) return ta - tb;
  const ra = Number.isFinite(Number(a.rank)) ? Number(a.rank) : SET_RANK.custom;
  const rb = Number.isFinite(Number(b.rank)) ? Number(b.rank) : SET_RANK.custom;
  if (ra !== rb) return ra - rb;
  const na = Number(a.nw) || 1, nb = Number(b.nw) || 1;
  if (na !== nb) return nb - na;
  return String(a.setId || '').localeCompare(String(b.setId || ''));
}

/** The rotation a custom set takes its colour from, and the swatch cycles through. */
export const SET_COLORS = ['#ff69b4', '#78ffbe', '#8fd0ff', '#ffd166', '#b080ff', '#ff5c6c',
  '#ffe066', '#40d0c0', '#ffb3d9', '#6b8cff', '#c8a8ff', '#ece8ff'];

const MODES = ['exact', 'contains', 'regex', 'countdown'];

/** One custom set, filled out and made safe. Junk in the file gives null, not a broken row. */
export function normalizeSet(s, i = 0) {
  if (!s || typeof s !== 'object' || !s.phrase) return null;
  return { id: String(s.id || 'c-' + i), name: String(s.name || s.phrase), group: 'custom',
    phrase: String(s.phrase), mode: MODES.includes(s.mode) ? s.mode : 'exact',
    color: /^#[0-9a-f]{6}$/i.test(String(s.color)) ? String(s.color) : SET_COLORS[i % SET_COLORS.length],
    cluster: Number(s.cluster) > 0 ? Number(s.cluster) : 0 };
}

/** `project.triggers`, filled out: what is ticked, the author's own sets, the per-set options. */
export function normalizeTriggers(json) {
  const j = json && typeof json === 'object' ? json : {};
  const seen = new Set();
  const custom = (Array.isArray(j.custom) ? j.custom : [])
    .map((s, i) => normalizeSet(s, i))
    .filter((s) => s && !seen.has(s.id) && (seen.add(s.id), true));
  const opts = {};
  const src = j.opts && typeof j.opts === 'object' ? j.opts : {};
  for (const k of Object.keys(src)) if (src[k] && typeof src[k] === 'object') opts[k] = { ...src[k] };
  const on = [...new Set((Array.isArray(j.on) ? j.on : []).filter((id) => typeof id === 'string' && id))];
  return { version: 1, on, custom, opts };
}
