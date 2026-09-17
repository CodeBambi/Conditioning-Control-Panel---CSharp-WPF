import { FOLEY, SAMPLE_CUES, loadFoleySample } from './foley.js';

/* ============================================================================
 * shared/sound/kit.js - THE ONE KIT. Every sound the Back Room makes.
 *
 * One AudioContext for the whole room (made inside the first gesture), one
 * master gain, one small delay-feedback room behind a send bus, and a table of
 * cues that are SCORED first (pure data: notes, times, pitches, a tail) and
 * only then rendered onto Web Audio nodes. score() is exported on its own so
 * the schedules can be checked in bare node with no audio at all, and so a
 * station can read what a cue will do before it does it.
 *
 * The scored palette is synthesised. Three local ElevenLabs foley samples replace
 * their scored fallback once decoded; failed loads never delay an action. The race's
 * mp3 chimes the stations used to borrow are retired here; the bell family
 * below is the one voice the whole floor shares.
 *
 * THE RULES OF THE FLOOR (owner, 2026-09-15):
 *   - wins always rise in pitch, and the bell ladder rolls with the count-up;
 *   - losses are near-silent: a soft settle, never a descending "fail" sound;
 *   - a near miss gets a rising anticipation that resolves quietly;
 *   - the ambient bed never stops while the room is open;
 *   - every win tier has a longer and richer tail than the one below.
 *
 * Lifecycle (Law VI): arm() inside a gesture, play(name, opts) at the frame,
 * stop(name) to take a cue back, suspend(on) to hold every voice (the beds come
 * back on resume, the one-shots do not), dispose() to close the context. A cue
 * that cannot sound (no context, suspended, disposed) is still traced, so the
 * dev pages and the CDP checks see the beat.
 *
 * NODE-SAFE: no AudioContext means every call is a no-op that still traces.
 * ==========================================================================*/

const SEMI = s => 2 ** (s / 12);
const PENTA = [0, 2, 4, 7, 9];
/** Major pentatonic step k (0, 1, 2, ...) as semitones above the root: 0 2 4 7 9 12 14 16 19 21 24 ... */
export const pentatonic = k => { const i = Math.max(0, Math.floor(Number(k) || 0)); return 12 * Math.floor(i / 5) + PENTA[i % 5]; };

export const ROOT_HZ = 523.25;   // C5, the room's key
const NOISE_S = 2;               // the shared noise buffer, seconds

/** Per tier: the tail (seconds, to the last note's end). Each tier longer and richer than the one below. */
export const TIERS = Object.freeze({
  small: Object.freeze({ tail: 0.6, parts: ['arp'] }),
  mid: Object.freeze({ tail: 1.3, parts: ['chord'] }),
  big: Object.freeze({ tail: 2.4, parts: ['chord', 'arp', 'shimmer'] }),
  hero: Object.freeze({ tail: 5.2, parts: ['sub', 'chord', 'arp', 'crown', 'shimmer'] }),
});
export const TIER_ORDER = Object.freeze(['small', 'mid', 'big', 'hero']);
/** -24 dB under master for the bed, a whisper is quieter still. */
export const BED_LEVEL = 0.0126;
export const DEFAULT_MASTER = 0.8;
/** A cue on this list plays once per window even when two lanes call it on the same frame (the slot's muted
 *  last thud and its dead-spin lane both say `settle`). Milliseconds. */
export const DEDUPE_MS = Object.freeze({ settle: 120, clicker: 25 });
/** THE LEVER and THE REEL ROLL come in four voices each, so the cabinet can be auditioned and picked
 *  (shared/sound/audition.html; the slot picks in stations/slot/sound.js). A: Iron, B: Candy, C: Toy,
 *  D: Vintage on the lever; A: Ticker, B: Purr, C: Rattle and bell, D: Hybrid casino on the reels. */
export const LEVER_VARIANTS = Object.freeze(['A', 'B', 'C', 'D']);
export const REEL_VARIANTS = Object.freeze(['A', 'B', 'C', 'D']);
/** The owner's pair: a candy lever over a ticker drum. */
export const DEFAULT_SFX = Object.freeze({ lever: 'B', reel: 'A' });

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const num = (v, d) => (Number.isFinite(Number(v)) ? Number(v) : d);
const pick = (v, list, d) => { const k = String(v == null ? '' : v).trim().toUpperCase(); return list.includes(k) ? k : d; };

/* ----------------------------------------------------------------------------
 * SCORES. A note is one of:
 *   { k:'tone', wave, hz, hzTo?, at, dur, level, attack?, lp?, lpTo?, part?, dry? }
 *   { k:'noise', type?, hz, hzTo?, q?, at, dur, level, attack?, part?, dry? }
 * `at` and `dur` in seconds from the cue's start; `hzTo` is a sweep that ends
 * at the note's end; `attack` a fraction of dur (default a click, 0.01 s);
 * `dry` keeps the note off the room send. `part` names the voice for a test.
 * -------------------------------------------------------------------------- */
const tone = (hz, at, dur, level, o = {}) => ({ k: 'tone', wave: 'sine', hz, at, dur, level, ...o });
const noise = (hz, at, dur, level, o = {}) => ({ k: 'noise', type: 'bandpass', q: 1, hz, at, dur, level, ...o });
/** A bell: a fundamental and one bright inharmonic partial, both decaying. */
const bell = (hz, at, dur, level, part) => [
  tone(hz, at, dur, level, { part }),
  tone(hz * 2.76, at, dur * 0.45, level * 0.22, { part: part + '+' }),
];

const SCORES = {
  ...FOLEY,
  /** A reel's tick train: decelerating clicks with a rising pitch ladder per reel. `ms` is the travel. */
  ticks({ reel = 0, ms = 1800 } = {}) {
    const r = clamp(Math.floor(num(reel, 0)), 0, 4), span = clamp(num(ms, 1800), 120, 12000) / 1000;
    const base = 1400 * SEMI(r * 4), notes = [];
    let t = 0, i = 0;
    while (t < span) {
      const p = t / span, gap = 0.045 + 0.13 * p * p;   // 45 ms at speed, 175 ms into the stop
      notes.push(noise(base * SEMI(Math.min(6, i * 0.35)), t, 0.022, 0.11 * (0.7 + 0.3 * p), { q: 6, part: 'tick', dry: true }));
      t += gap; i++;
    }
    return notes;
  },
  /** One filtered click, `reel` picks the ladder rung, `step` climbs it. */
  tick({ reel = 0, step = 0 } = {}) {
    const r = clamp(Math.floor(num(reel, 0)), 0, 4);
    return [noise(1400 * SEMI(r * 4 + Math.min(6, num(step, 0) * 0.35)), 0, 0.022, 0.11, { q: 6, part: 'tick', dry: true })];
  },
  /** THE ANTICIPATION: a slow filtered sweep rising over `ms` (1.5-2.5 s), resolving on a quiet top note. */
  riser({ ms = 2000, level = 1 } = {}) {
    const dur = clamp(num(ms, 2000), 120, 6000) / 1000, lv = clamp(num(level, 1), 0, 1);
    return [
      tone(180, 0, dur, 0.2 * lv, { wave: 'triangle', hzTo: 520, attack: 0.4, lp: 400, lpTo: 2400, part: 'rise' }),
      tone(520, dur, 0.35, 0.09 * lv, { attack: 0.1, part: 'resolve' }),
    ];
  },
  /** THE ALMOST resolving: the riser's top note again, soft and flat, then nothing. Never a fall. */
  almost({ level = 1 } = {}) {
    const lv = clamp(num(level, 1), 0, 1);
    return [tone(520, 0, 0.4, 0.06 * lv, { attack: 0.2, part: 'resolve' }), noise(600, 0, 0.18, 0.03 * lv, { type: 'lowpass', part: 'settle', dry: true })];
  },
  /** A DEAD SPIN, A LOST HAND, A MISSED POCKET: a felt-wrapped chime touched once (C4, the room's root an octave
   *  down, no bright partial to speak of) and one breath in the ambience that opens, never closes. Under 700 ms,
   *  flat or rising in every voice, never a "fail" cue. */
  settle({ level = 1 } = {}) {
    const lv = clamp(num(level, 1), 0, 1), root = ROOT_HZ / 2;
    return [
      tone(root, 0, 0.55, 0.05 * lv, { attack: 0.12, lp: 900, part: 'felt' }),
      tone(root * 2, 0, 0.3, 0.012 * lv, { attack: 0.1, part: 'felt+' }),
      noise(320, 0.05, 0.6, 0.025 * lv, { type: 'lowpass', hzTo: 520, attack: 0.5, part: 'breath', dry: true }),
    ];
  },
  /** A snooze / a sigh (the wheel's "tomorrow"): breathy, soft, flat. Not a loss sound, not a fall. */
  sigh({ level = 1 } = {}) {
    const lv = clamp(num(level, 1), 0, 1);
    return [noise(600, 0, 0.9, 0.05 * lv, { type: 'lowpass', attack: 0.35, part: 'settle', dry: true }), tone(330, 0.1, 0.6, 0.04 * lv, { attack: 0.3, part: 'settle' })];
  },
  /** THE BELL LADDER under a count-up: pentatonic, ascending, as long as the win. `plan` [{at ms, semis}]
   *  (the slot's ladderPlan) fixes the times; else `ms` sets the length (one bell per ~140 ms, 2..16). */
  ladder({ ms = 1000, plan = null, semis = 0, n = 0 } = {}) {
    const root = ROOT_HZ * SEMI(clamp(num(semis, 0), -24, 24));
    let times;
    if (Array.isArray(plan) && plan.length) times = plan.map(s => clamp(num(s && s.at, 0), 0, 30000) / 1000);
    else {
      const count = n > 0 ? clamp(Math.floor(n), 1, 16) : clamp(Math.round(clamp(num(ms, 1000), 0, 20000) / 140), 2, 16);
      const gap = count > 1 ? clamp(num(ms, 1000) / 1000 / count, 0.09, 0.4) : 0;
      times = Array.from({ length: count }, (_, i) => i * gap);
    }
    const notes = [];
    times.forEach((at, i) => notes.push(...bell(root * SEMI(pentatonic(i)), at, 0.32, 0.12 - i * 0.002, 'arp')));
    return notes;
  },
  /** THE WIN by tier. Every voice rises or holds; every tier's tail is longer and richer than the one below.
   *   small  two soft notes (root, fifth)                                   0.6 s
   *   mid    a warm chord, four voices opening upward                      1.3 s
   *   big    the chord, a four-bell arpeggio an octave up, a 2 s shimmer  2.4 s
   *   hero   a sub-bass bloom, a low chord, a ten-bell chime tree climbing from the root, a crown bell two
   *          octaves up, two shimmers handing over                        5.2 s */
  win({ tier = 'small', semis = 0 } = {}) {
    const t = TIER_ORDER.includes(tier) ? tier : 'small', root = ROOT_HZ * SEMI(clamp(num(semis, 0), -24, 24));
    const notes = [];
    if (t === 'small') {
      notes.push(tone(root, 0, 0.5, 0.2, { attack: 0.02, part: 'arp' }), tone(root * SEMI(7), 0.1, 0.5, 0.17, { attack: 0.02, part: 'arp' }));
    } else if (t === 'mid') {
      [0, 4, 7, 12].forEach((s, i) => notes.push(tone(root * SEMI(s), i * 0.03, 1.21, 0.15, { wave: 'triangle', attack: 0.06, lp: 2200, part: 'chord' })));
    } else if (t === 'big') {
      [0, 4, 7, 12].forEach((s, i) => notes.push(tone(root * SEMI(s), i * 0.03, 1.6, 0.14, { wave: 'triangle', attack: 0.06, lp: 2600, part: 'chord' })));
      [12, 16, 19, 24].forEach((s, i) => notes.push(tone(root * SEMI(s), 0.25 + i * 0.11, 0.9, 0.11, { attack: 0.02, part: 'arp' })));
      notes.push(noise(5000, 0.3, 2.1, 0.05, { hzTo: 7000, q: 1.5, attack: 0.25, part: 'shimmer' }));
    } else {
      notes.push(tone(45, 0, 1.8, 0.3, { attack: 0.35, part: 'sub' }));
      [0, 4, 7].forEach((s, i) => notes.push(tone(root * SEMI(s) / 2, i * 0.04, 3.0, 0.12, { wave: 'triangle', attack: 0.15, lp: 1800, part: 'chord' })));
      for (let k = 0; k < 10; k++) notes.push(...bell(root * SEMI(pentatonic(k)), 0.2 + k * 0.22, 0.7, 0.1, 'arp'));
      notes.push(...bell(root * 4, 2.4, 2.8, 0.09, 'crown'));
      notes.push(noise(4000, 0.4, 2.4, 0.05, { hzTo: 7000, q: 1.5, attack: 0.3, part: 'shimmer' }));
      notes.push(noise(6000, 2.6, 2.6, 0.04, { hzTo: 9000, q: 1.5, attack: 0.3, part: 'shimmer' }));
    }
    return notes;
  },
  /** THE WORD's bed: the whisper shimmer under a spoken subliminal word (the speech is another lane's
   *  speechSynthesis call, never made here). 0.9 s of breathy filtered noise with a gentle downward filter
   *  sweep and one faint high sine. `index` 0..2 puts the second and third words of a chain a whole tone up. */
  word({ index = 0, level = 1 } = {}) {
    const i = clamp(Math.floor(num(index, 0)), 0, 2), up = SEMI(2 * i), lv = clamp(num(level, 1), 0, 1);
    return [
      noise(2400 * up, 0, 0.9, 0.04 * lv, { hzTo: 1500 * up, q: 0.9, attack: 0.3, part: 'bed' }),
      tone(ROOT_HZ * 2 * up, 0.05, 0.8, 0.012 * lv, { attack: 0.45, part: 'bed' }),
    ];
  },
  /** A deep slow breath under the melt: 6 s, in and out. */
  breath({ ms = 6000 } = {}) {
    const dur = clamp(num(ms, 6000), 2000, 12000) / 1000, h = dur / 2;
    return [
      noise(250, 0, h, 0.06, { type: 'lowpass', hzTo: 900, attack: 0.85, part: 'bed', dry: true }),
      noise(900, h, h, 0.06, { type: 'lowpass', hzTo: 250, attack: 0.1, part: 'bed', dry: true }),
      tone(50, 0, dur, 0.12, { attack: 0.42, part: 'sub' }),
    ];
  },
  /** Chips and coins: short bright clinks at random pitches. */
  chips({ n = 1, gap = 0.06 } = {}, rand) {
    const count = clamp(Math.floor(num(n, 1)), 1, 12), g = clamp(num(gap, 0.06), 0.02, 0.4), notes = [];
    for (let i = 0; i < count; i++) {
      const hz = 2400 + 1800 * rand(), at = i * g * (0.8 + 0.4 * rand());
      notes.push(tone(hz, at, 0.07, 0.09, { part: 'clink', dry: true }), tone(hz * 1.5, at, 0.04, 0.03, { part: 'clink', dry: true }),
        noise(7000, at, 0.01, 0.05, { q: 0.7, part: 'clink', dry: true }));
    }
    return notes;
  },
  /** A card: `slide` off the shoe (a noise burst and a soft tap), `flip` shorter and brighter. */
  card({ kind = 'slide' } = {}) {
    if (kind === 'flip') return [noise(2200, 0, 0.07, 0.06, { hzTo: 1200, part: 'card', dry: true }), tone(260, 0.05, 0.03, 0.07, { part: 'card', dry: true })];
    return [noise(1400, 0, 0.13, 0.07, { hzTo: 700, part: 'card', dry: true }), tone(180, 0.11, 0.04, 0.08, { part: 'card', dry: true })];
  },
  /** The ball's fret rattle: clicks that spread out and die down. */
  rattle({ n = 8 } = {}) {
    const count = clamp(Math.floor(num(n, 8)), 3, 12), notes = [];
    let at = 0;
    for (let k = 0; k < count; k++) {
      notes.push(noise(2600, at, 0.015, 0.14 * 0.82 ** k, { q: 4, part: 'click', dry: true }));
      at += 0.05 * 1.28 ** k;
    }
    return notes;
  },
  /** The pocket drop: one damped thud. */
  drop() {
    return [tone(160, 0, 0.16, 0.35, { hzTo: 55, part: 'thud' }), noise(900, 0, 0.02, 0.1, { type: 'lowpass', part: 'thud', dry: true })];
  },
  /** The wheel's peg: a ratchet click, `semis` from the feel. */
  clack({ semis = 0 } = {}) {
    const s = clamp(num(semis, 0), -24, 24);
    return [noise(2500 * SEMI(s), 0, 0.014, 0.1, { q: 3, part: 'click', dry: true }), tone(900 * SEMI(s), 0, 0.02, 0.05, { part: 'click', dry: true })];
  },
  /** THE CLICKER: one dry mechanical click, a slide projector's relay. 30-60 ms, a little random in pitch. */
  clicker({ level = 1 } = {}, rand) {
    const lv = clamp(num(level, 1), 0, 1), dur = 0.03 + 0.03 * rand(), hz = 2600 * SEMI((rand() - 0.5) * 4);
    return [
      noise(hz, 0, dur, 0.16 * lv, { q: 5, part: 'click', dry: true }),
      tone(190, 0, 0.012, 0.05 * lv, { part: 'click', dry: true }),
    ];
  },
  /** THE THUD: a reel or the pointer settling. `muted` for a no-pay stop (Brake 6: muted, never silent). */
  thud({ semis = 0, muted = false, level = 1 } = {}) {
    const s = clamp(num(semis, 0), -24, 24), lv = clamp(num(level, 1), 0, 1) * (muted ? 0.22 : 0.5);
    return [tone(120 * SEMI(s), 0, 0.32, lv, { hzTo: 38, lp: muted ? 700 : 0, part: 'thud' }), noise(1200, 0, 0.05, lv * 0.2, { type: 'lowpass', part: 'thud', dry: true })];
  },
  /** THE BANK's token landing: a small bell that rises with the count (`i` of `n`), the last one a mini-thud. */
  cash() {
    return [noise(1800,0,.055,.11,{part:'cash',dry:true}), noise(2300,.065,.045,.09,{part:'cash',dry:true}),
      ...bell(1318,.11,.38,.18,'cash'), ...bell(1760,.15,.42,.13,'cash')];
  },
  token({ i = 0, last = false } = {}) {
    if (last) return SCORES.thud({ semis: 5, level: 0.6 });
    return bell(ROOT_HZ * 2 * SEMI(pentatonic(clamp(Math.floor(num(i, 0)), 0, 14))), 0, 0.18, 0.1, 'arp');
  },
  /**
   * THE LEVER PULL, the whole gesture in one cue: the stroke down, the stop at the bottom, the spring back.
   * 500-700 ms, and the four voices are four cabinets:
   *   A Iron     ratchet ticks up the stroke, a heavy metallic clank at the bottom, a spring click coming back
   *   B Candy    a velvety whoosh, a muted pop, two little chimes on the release (the pink cabinet's own)
   *   C Toy      plastic click-clack, a knock, a cartoon spring boing: the shortest and the bounciest
   *   D Vintage  one long creaking ratchet, a deep wooden thunk, a "krrr" that hands the beat to the reels
   */
  lever({ variant = DEFAULT_SFX.lever, level = 1 } = {}) {
    const v = pick(variant, LEVER_VARIANTS, DEFAULT_SFX.lever), lv = clamp(num(level, 1), 0, 1), out = [];
    if (v === 'A') {
      for (let i = 0; i < 4; i++) out.push(noise(1700 * SEMI(i * 1.5), i * 0.072, 0.02, 0.085 * lv, { q: 7, part: 'ratchet', dry: true }));
      out.push(noise(1500, 0.3, 0.16, 0.12 * lv, { hzTo: 430, q: 1.2, part: 'clank', dry: true }),
        tone(150, 0.3, 0.26, 0.24 * lv, { hzTo: 62, wave: 'triangle', lp: 1400, part: 'body' }),
        tone(300, 0.3, 0.1, 0.05 * lv, { wave: 'triangle', part: 'body' }),
        noise(2300, 0.52, 0.03, 0.055 * lv, { q: 5, part: 'spring', dry: true }),
        tone(700, 0.52, 0.04, 0.035 * lv, { part: 'spring', dry: true }));
    } else if (v === 'B') {
      out.push(noise(1250, 0, 0.022, 0.06 * lv, { q: 3.4, part: 'catch', dry: true }),
        noise(320, 0, 0.3, 0.07 * lv, { hzTo: 1500, q: 0.9, attack: 0.18, part: 'whoosh', dry: true }),
        tone(250, 0.29, 0.14, 0.2 * lv, { hzTo: 96, wave: 'triangle', lp: 900, part: 'pop' }),
        noise(700, 0.29, 0.05, 0.045 * lv, { type: 'lowpass', part: 'pop', dry: true }),
        ...bell(ROOT_HZ, 0.44, 0.2, 0.07 * lv, 'chime'), ...bell(ROOT_HZ * SEMI(7), 0.53, 0.17, 0.065 * lv, 'chime'));
    } else if (v === 'C') {
      out.push(noise(2600, 0, 0.016, 0.095 * lv, { q: 4, part: 'clack', dry: true }),
        noise(2200, 0.07, 0.016, 0.085 * lv, { q: 4, part: 'clack', dry: true }),
        noise(1900, 0.15, 0.02, 0.095 * lv, { q: 3, part: 'clack', dry: true }),
        tone(210, 0.22, 0.09, 0.15 * lv, { hzTo: 120, wave: 'square', lp: 1200, part: 'knock' }),
        tone(300, 0.3, 0.07, 0.09 * lv, { hzTo: 720, wave: 'triangle', part: 'boing' }),
        tone(720, 0.37, 0.08, 0.075 * lv, { hzTo: 330, wave: 'triangle', part: 'boing' }),
        tone(330, 0.45, 0.06, 0.055 * lv, { hzTo: 560, wave: 'triangle', part: 'boing' }));
    } else {
      out.push(noise(850, 0, 0.34, 0.055 * lv, { hzTo: 1600, q: 8, attack: 0.3, part: 'creak', dry: true }));
      for (let i = 0; i < 6; i++) out.push(noise(1200 * SEMI(i), 0.03 + i * 0.055, 0.014, 0.05 * lv, { q: 9, part: 'ratchet', dry: true }));
      out.push(tone(170, 0.36, 0.24, 0.22 * lv, { hzTo: 58, wave: 'triangle', lp: 700, part: 'thunk' }),
        noise(520, 0.36, 0.07, 0.065 * lv, { type: 'lowpass', part: 'thunk', dry: true }),
        noise(1700, 0.52, 0.13, 0.05 * lv, { hzTo: 2300, q: 2.5, attack: 0.15, part: 'krrr', dry: true }));
    }
    return out;
  },
  /**
   * THE REEL STOP: the gesture that lands drum REEL (0..2), one per reel voice. It carries the thud the floor
   * already knew, so a stop is still one beat (Law X).
   *   A Ticker           the thud and a short damped bell
   *   B Purr             a soft thump, nothing else
   *   C Rattle and bell  a ding that climbs reel to reel: first low, second mid, third high
   *   D Hybrid casino    the thud and a digital chime, a rung higher each reel
   */
  reelStop({ variant = DEFAULT_SFX.reel, reel = 0, level = 1 } = {}) {
    const v = pick(variant, REEL_VARIANTS, DEFAULT_SFX.reel);
    const r = clamp(Math.floor(num(reel, 0)), 0, 4), lv = clamp(num(level, 1), 0, 1), semis = (r - 1) * 2;
    if (v === 'A') return [...SCORES.thud({ semis, level: lv }), ...bell(ROOT_HZ * SEMI(pentatonic(r)), 0.01, 0.16, 0.05 * lv, 'ding')];
    if (v === 'B') {
      return [tone(110 * SEMI(semis), 0, 0.26, 0.28 * lv, { hzTo: 44, lp: 600, part: 'thump' }),
        noise(600, 0, 0.05, 0.035 * lv, { type: 'lowpass', part: 'thump', dry: true })];
    }
    if (v === 'C') {
      return [...bell(ROOT_HZ * SEMI(pentatonic(r * 2)), 0, 0.34, 0.095 * lv, 'ding'),
        noise(1800, 0, 0.018, 0.055 * lv, { q: 5, part: 'click', dry: true }),
        tone(120 * SEMI(semis), 0, 0.14, 0.13 * lv, { hzTo: 52, lp: 700, part: 'thud' })];
    }
    return [...SCORES.thud({ semis, level: lv * 0.9 }),
      tone(ROOT_HZ * SEMI(pentatonic(r + 2)), 0.02, 0.2, 0.075 * lv, { wave: 'square', lp: 2600, part: 'chime' }),
      tone(ROOT_HZ * 2 * SEMI(pentatonic(r + 2)), 0.02, 0.12, 0.028 * lv, { part: 'chime' })];
  },
  /** A button, a lever, "no more bets": one soft tap. */
  tap() { return [tone(220, 0, 0.03, 0.06, { part: 'tap', dry: true }), noise(1500, 0, 0.012, 0.03, { part: 'tap', dry: true })]; },
  /** The ball's launch: a short rising whoosh. */
  launch() { return [noise(400, 0, 0.3, 0.06, { hzTo: 1600, q: 0.8, attack: 0.3, part: 'rise' })]; },
  /**
   * THE THROW's spin-up (the roulette). The wheel has no lever, so the throw IS the lever and this is its
   * whole gesture in one cue, about 0.45 s and about the pull's own level: the bearing taking the hand (one
   * dry catch), a whir opening upward as the rotor picks up, and a low body under it. The loop that follows
   * the rotor from here is the 'wheel' roll below; the landing is still the thud's own frame (Law X).
   */
  whir({ level = 1 } = {}) {
    const lv = clamp(num(level, 1), 0, 1);
    return [
      noise(1900, 0, 0.018, 0.06 * lv, { q: 6, part: 'catch', dry: true }),
      noise(240, 0.01, 0.44, 0.085 * lv, { hzTo: 1150, q: 1.1, attack: 0.38, part: 'whir', dry: true }),
      tone(116, 0, 0.38, 0.19 * lv, { hzTo: 232, wave: 'triangle', lp: 820, part: 'bearing' }),
      tone(232, 0.02, 0.2, 0.05 * lv, { hzTo: 348, wave: 'triangle', part: 'bearing' }),
    ];
  },
};

/* The beds are not scored: they loop until stopped. */
export const BEDS = Object.freeze(['ambience', 'spiral']);
/** THE ROLLS loop like a bed but one per reel, and they follow the drum: play('reel', { reel, variant, speed }),
 *  setRollSpeed(reel, speed) every frame, stop('reel', { reel }) on the stop frame. */
export const ROLLS = Object.freeze(['reel', 'wheel']);
export const CUES = Object.freeze([...Object.keys(SCORES), ...BEDS, ...ROLLS]);

/**
 * Pure: the schedule for cue `name`: { name, notes, tail } (tail = seconds to the last note's end), or null for a
 * bed or an unknown name. `rand` (0..1) makes the chips and the clicker deterministic in a test.
 */
export function score(name, opts = {}, rand = Math.random) {
  const fn = SCORES[name];
  if (typeof fn !== 'function') return null;
  const notes = fn(opts || {}, typeof rand === 'function' ? rand : Math.random);
  const tail = notes.reduce((m, n) => Math.max(m, n.at + n.dur), 0);
  return { name, notes, tail };
}

/** Which layers a reel voice turns on. A ticks alone, B purrs alone, C ticks over a resonant hum, D layers
 *  the purr under the ticks. */
const ROLL_LAYER = Object.freeze({
  A: Object.freeze({ ticks: 1, purr: 0, hum: 0 }),
  B: Object.freeze({ ticks: 0, purr: 1, hum: 0 }),
  C: Object.freeze({ ticks: 1, purr: 0.3, hum: 1 }),
  D: Object.freeze({ ticks: 1, purr: 0.6, hum: 0 }),
});
/** A tick peaks here, clear of the bed at -24 dB without being harsh; the purr and the hum together sit just
 *  over it at full blur and fall away with the drum. */
export const ROLL_TICK = 0.038;
export const ROLL_BED = 0.034;
const TICK_AHEAD = 0.14, TICK_MS = 45;
const rollKey = reel => 'reel:' + clamp(Math.floor(num(reel, 0)), 0, 4);
/** THE TICK RATE IS THE REEL SPEED: 38 ms a tick at full blur, 260 ms crawling into the stop. */
export const tickGap = speed => 0.038 + 0.222 * (1 - clamp(num(speed, 1), 0, 1)) ** 1.7;

/** THE WHEEL ROLL: the roulette rotor turning after a throw. One loop for the room, not one per reel, and it
 *  sits UNDER the slot's drums on purpose - it runs for seconds at a time behind the ball's own voices. */
export const WHEEL_ROLL = 'wheel';
export const WHEEL_TICK = 0.034;
export const WHEEL_BED = 0.03;
/** THE RATCHET RATE IS THE ROTOR SPEED: 54 ms a click on a hard throw, 340 ms as the wheel gives up. */
export const wheelGap = speed => 0.054 + 0.286 * (1 - clamp(num(speed, 1), 0, 1)) ** 1.6;

/* ----------------------------------------------------------------------------
 * THE KIT. createKit({ AudioContext?, master?, random?, now? }) for a test; the
 * module singleton `kit` below for the room and the stations.
 * -------------------------------------------------------------------------- */
export function createKit({ AudioContext: AC = null, master = DEFAULT_MASTER, random = Math.random, trim = 1, now = null, loadSample = loadFoleySample } = {}) {
  let ctx = null, out = null, dry = null, send = null, noiseBuf = null;
  let masterLevel = clamp(num(master, DEFAULT_MASTER), 0, 1), trimLevel = clamp(num(trim, 1), 0, 1), muted = false;
  let suspended = false;
  const live = new Set();       // every voice not yet ended (the leak check)
  const voices = new Map();     // name -> Set of voices
  const beds = new Map();       // name -> { nodes, gain, stop(fade) }
  const rolls = new Map();      // 'reel:i' -> the drum turning on reel i
  const wanted = new Set();     // beds to bring back after a suspend
  const lastAt = new Map();     // name -> page time of the last play, for DEDUPE_MS
  const trace = [];
  const samples = new Map();
  let sampleContext = null;
  const rand = () => { const v = Number(random()); return Number.isFinite(v) ? clamp(v, 0, 1) : 0.5; };
  const clock = typeof now === 'function' ? now : () => (typeof performance !== 'undefined' ? performance.now() : Date.now());

  const Ctor = () => AC || globalThis.AudioContext || globalThis.webkitAudioContext || null;
  const level = () => (muted ? 0 : masterLevel * trimLevel);

  function graph() {
    if (ctx) return ctx;
    const C = Ctor();
    if (!C) return null;
    try {
      ctx = new C();
      out = ctx.createGain(); out.gain.value = level(); out.connect(ctx.destination);
      dry = ctx.createGain(); dry.gain.value = 1; dry.connect(out);
      // The room: a short delay fed back on itself under a lowpass, on a send bus. Convolver-free, cheap.
      send = ctx.createGain(); send.gain.value = 0.22;
      const delay = ctx.createDelay(1); delay.delayTime.value = 0.093;
      const fb = ctx.createGain(); fb.gain.value = 0.34;
      const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 2800;
      send.connect(delay); delay.connect(lp); lp.connect(fb); fb.connect(delay); lp.connect(out);
      const n = ctx.createBuffer(1, Math.floor(ctx.sampleRate * NOISE_S), ctx.sampleRate), d = n.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = rand() * 2 - 1;
      noiseBuf = n;
    } catch (e) { ctx = null; out = null; dry = null; send = null; return null; }
    return ctx;
  }
  const ready = () => !suspended && !!ctx && ctx.state !== 'closed';   // arm() builds the graph; a play before the gesture only traces
  function note(name, opts) { trace.push({ name, at: Math.round(clock()), opts: opts || {} }); if (trace.length > 80) trace.shift(); }

  function envelope(g, t, dur, lvl, attackFrac) {
    const a = Math.max(0.004, Math.min(dur * 0.9, dur * (attackFrac == null ? 0.01 : attackFrac)));
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(Math.max(0.0002, lvl), t + a);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  }
  /** One note onto the graph at absolute time t0 + n.at. The voice takes every node down with it when it ends. */
  function render(n, t0, set) {
    const t = t0 + n.at, dur = Math.max(0.005, n.dur), g = ctx.createGain(), nodes = [g];
    let src, head;
    if (n.k === 'tone') {
      src = ctx.createOscillator(); src.type = n.wave || 'sine';
      src.frequency.setValueAtTime(n.hz, t);
      if (n.hzTo && n.hzTo !== n.hz) src.frequency.exponentialRampToValueAtTime(n.hzTo, t + dur);
      head = src;
      if (n.lp) { const f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.setValueAtTime(n.lp, t); if (n.lpTo) f.frequency.exponentialRampToValueAtTime(n.lpTo, t + dur); src.connect(f); head = f; nodes.push(f); }
    } else {
      src = ctx.createBufferSource(); src.buffer = noiseBuf; src.loop = true;
      const f = ctx.createBiquadFilter(); f.type = n.type || 'bandpass'; f.Q.value = n.q || 1;
      f.frequency.setValueAtTime(n.hz, t);
      if (n.hzTo && n.hzTo !== n.hz) f.frequency.exponentialRampToValueAtTime(n.hzTo, t + dur);
      src.connect(f); head = f; nodes.push(f);
    }
    nodes.push(src);
    envelope(g, t, dur, n.level, n.attack);
    head.connect(g); g.connect(dry);
    if (!n.dry && send) g.connect(send);
    const voice = { src, nodes, set };
    live.add(voice); set.add(voice);
    src.onended = () => end(voice);
    src.start(t); src.stop(t + dur + 0.02);
    return voice;
  }
  function warmSamples() {
    if (!ctx || sampleContext === ctx || typeof loadSample !== 'function') return;
    const owner = ctx; sampleContext = owner;
    for (const name of SAMPLE_CUES) {
      Promise.resolve().then(() => loadSample(name, owner)).then(buffer => {
        if (ctx === owner && buffer && buffer.duration > 0 && buffer.duration <= 1.5) samples.set(name, buffer);
      }).catch(() => { /* Optional foley: the scored fallback remains available. */ });
    }
  }
  function renderSample(name, buffer, opts, t0, set) {
    const src = ctx.createBufferSource(), gain = ctx.createGain();
    src.buffer = buffer;
    const rate = 0.98 + rand() * 0.04;
    src.playbackRate.value = rate;
    gain.gain.value = 0.65 * clamp(num(opts.level, 1), 0, 1);
    src.connect(gain); gain.connect(dry);
    const voice = { src, nodes: [src, gain], set };
    live.add(voice); set.add(voice); src.onended = () => end(voice);
    src.start(t0); src.stop(t0 + buffer.duration / rate + 0.02);
    return 1;
  }
  function end(voice) {
    live.delete(voice); voice.set.delete(voice);
    for (const n of voice.nodes) { try { n.disconnect(); } catch (e) { /* gone */ } }
  }

  /* ---- the beds ---- */
  function bedAmbience() {
    const g = ctx.createGain(), nodes = [];
    const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 220; lp.connect(g);
    for (const [hz, wave, lv] of [[55, 'sine', 0.55], [55.4, 'triangle', 0.3], [110.2, 'sine', 0.12]]) {
      const o = ctx.createOscillator(), og = ctx.createGain(); o.type = wave; o.frequency.value = hz; og.gain.value = lv;
      o.connect(og); og.connect(lp); o.start(); nodes.push(o);
    }
    // A very slow shimmer: filtered noise, its band wandering over 20 s.
    const sh = ctx.createBufferSource(); sh.buffer = noiseBuf; sh.loop = true;
    const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.Q.value = 3; bp.frequency.value = 3000;
    const lfo = ctx.createOscillator(), lg = ctx.createGain(); lfo.frequency.value = 0.05; lg.gain.value = 1000; lfo.connect(lg); lg.connect(bp.frequency);
    const sg = ctx.createGain(); sg.gain.value = 0.045; sh.connect(bp); bp.connect(sg); sg.connect(g); sh.start(); lfo.start(); nodes.push(sh, lfo);
    // Two high sines breathing under a 12 s tremolo, barely there.
    const tg = ctx.createGain(); tg.gain.value = 0.05;
    const tr = ctx.createOscillator(), trg = ctx.createGain(); tr.frequency.value = 0.08; trg.gain.value = 0.04; tr.connect(trg); trg.connect(tg.gain); tr.start(); nodes.push(tr);
    for (const hz of [1318.5, 1975.5]) { const o = ctx.createOscillator(), og = ctx.createGain(); o.frequency.value = hz; og.gain.value = 0.5; o.connect(og); og.connect(tg); o.start(); nodes.push(o); }
    tg.connect(g);
    return { g, nodes, level: BED_LEVEL, fadeIn: 2 };
  }
  /** THE SPIRAL's hum: two slow-beating pairs (4 Hz and 4.5 Hz apart), in over 250 ms, out over 500 ms. */
  function bedSpiral() {
    const g = ctx.createGain(), nodes = [];
    for (const [hz, lv] of [[110, 0.5], [114, 0.5], [220, 0.25], [224.5, 0.25]]) {
      const o = ctx.createOscillator(), og = ctx.createGain(); o.frequency.value = hz; og.gain.value = lv; o.connect(og); og.connect(g); o.start(); nodes.push(o);
    }
    return { g, nodes, level: 0.09, fadeIn: 0.25 };
  }
  function startBed(name, opts) {
    if (beds.has(name)) return true;
    const make = name === 'ambience' ? bedAmbience : bedSpiral;
    let b;
    try { b = make(); } catch (e) { return false; }
    const t = ctx.currentTime;
    b.g.gain.setValueAtTime(0.0001, t);
    b.g.gain.exponentialRampToValueAtTime(b.level, t + b.fadeIn);
    b.g.connect(dry);
    const bed = {
      nodes: b.nodes, gain: b.g, timer: 0,
      stop(fade) {
        const at = ctx.currentTime, f = Math.max(0.02, fade);
        try { b.g.gain.cancelScheduledValues(at); b.g.gain.setValueAtTime(Math.max(0.0001, b.g.gain.value), at); b.g.gain.exponentialRampToValueAtTime(0.0001, at + f); } catch (e) { /* closed */ }
        for (const n of b.nodes) { try { n.stop(at + f + 0.02); } catch (e) { /* already */ } }
        if (bed.timer) clearTimeout(bed.timer);
      },
    };
    beds.set(name, bed);
    const ms = num(opts && opts.ms, 0);
    if (name === 'spiral' && ms > 0) bed.timer = setTimeout(() => { if (beds.get(name) === bed) api.stop(name); }, ms);
    return true;
  }
  function stopBed(name, fade) {
    const bed = beds.get(name);
    if (!bed) return;
    beds.delete(name);
    try { bed.stop(fade); } catch (e) { /* closed */ }
  }
  /* ---- THE REEL ROLL. One loop per reel, following the drum: ticks whose rate IS the reel's speed, a purr
   * whose band opens with it, or both. A roll is not a bed: it belongs to one reel, it never comes back on a
   * resume, and it dies on the stop frame under the stop gesture (Law X). ---- */
  function shapeRoll(roll) {
    if (!ctx || !roll.gain) return;
    const t = ctx.currentTime, sp = clamp(roll.speed, 0, 1);
    const lvl = Math.max(0.0001, ROLL_BED * roll.level * (0.22 + 0.78 * sp)), g = roll.gain.gain;
    try { g.cancelScheduledValues(t); g.setValueAtTime(Math.max(0.0001, g.value), t); g.linearRampToValueAtTime(lvl, t + 0.06); } catch (e) { g.value = lvl; }
    if (!roll.band) return;
    const hz = 150 + 520 * sp + roll.reel * 30, f = roll.band.frequency;
    try { f.cancelScheduledValues(t); f.setValueAtTime(Math.max(20, f.value), t); f.linearRampToValueAtTime(hz, t + 0.06); } catch (e) { f.value = hz; }
  }
  /** One tick of the train. The pitch climbs as the drum slows: on C that rise IS A1's anticipation tell. */
  function tickNote(roll) {
    const sp = clamp(roll.speed, 0, 1), up = (roll.variant === 'C' ? 7 : 2) * (1 - sp);
    return noise(roll.base * SEMI(up), 0, 0.02, ROLL_TICK * roll.level * (0.72 + 0.28 * sp), { q: 6, part: 'tick', dry: true });
  }
  /** Ticks are scheduled a beat ahead and topped up on a timer, so the rate follows the drum without a frame
   *  of its own. The timer is unref'd where the runtime has it: a bare node test never waits on a drum. */
  function pump(roll) {
    if (!ctx || rolls.get(roll.key) !== roll) return;
    const until = ctx.currentTime + TICK_AHEAD;
    if (roll.next < ctx.currentTime) roll.next = ctx.currentTime;
    for (let i = 0; i < 24 && roll.next < until; i++) {
      try { render(tickNote(roll), roll.next, roll.set); } catch (e) { /* a tick never breaks a beat */ }
      roll.next += tickGap(roll.speed);
    }
    roll.timer = setTimeout(() => pump(roll), TICK_MS);
    if (roll.timer && typeof roll.timer.unref === 'function') roll.timer.unref();
  }
  function freeRoll(roll) { for (const n of roll.nodes) { try { n.disconnect(); } catch (e) { /* gone */ } } }
  /** Start the drum on `reel`, or follow it if it is already turning. */
  function startRoll(reel, opts = {}) {
    const key = rollKey(reel), speed = clamp(num(opts.speed, 1), 0, 1), have = rolls.get(key);
    if (have) { have.speed = speed; shapeRoll(have); return 1; }
    const r = clamp(Math.floor(num(reel, 0)), 0, 4);
    const variant = pick(opts.variant, REEL_VARIANTS, DEFAULT_SFX.reel), layer = ROLL_LAYER[variant];
    let set = voices.get(key);
    if (!set) { set = new Set(); voices.set(key, set); }
    const roll = { key, reel: r, variant, layer, speed, level: clamp(num(opts.level, 1), 0, 1),
                   base: 1250 * SEMI(r * 3), nodes: [], gain: null, band: null, next: 0, timer: 0, set };
    try {
      if (layer.purr || layer.hum) {
        const g = ctx.createGain(); g.gain.value = 0.0001; g.connect(dry); roll.gain = g; roll.nodes.push(g);
        if (layer.purr) {
          const src = ctx.createBufferSource(); src.buffer = noiseBuf; src.loop = true;
          const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.Q.value = 1.4; bp.frequency.value = 240;
          const pg = ctx.createGain(); pg.gain.value = layer.purr;
          src.connect(bp); bp.connect(pg); pg.connect(g); src.start(); src.onended = () => freeRoll(roll);
          roll.band = bp; roll.nodes.push(src, bp, pg);
        }
        if (layer.hum) {
          const hg = ctx.createGain(); hg.gain.value = 0.34 * layer.hum;
          const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 900;
          lp.connect(hg); hg.connect(g);
          for (const hz of [96 * SEMI(r * 2), 144 * SEMI(r * 2)]) {
            const o = ctx.createOscillator(), og = ctx.createGain();
            o.type = 'triangle'; o.frequency.value = hz; og.gain.value = 0.5;
            o.connect(og); og.connect(lp); o.start(); o.onended = () => freeRoll(roll);
            roll.nodes.push(o, og);
          }
          roll.nodes.push(lp, hg);
        }
      }
    } catch (e) { /* a drum that cannot purr still ticks */ }
    rolls.set(key, roll);
    roll.next = ctx.currentTime;
    shapeRoll(roll);
    if (layer.ticks) pump(roll);
    return 1;
  }
  function stopRoll(reel) {
    const key = rollKey(reel), roll = rolls.get(key);
    if (!roll) return;
    rolls.delete(key);
    if (roll.timer) clearTimeout(roll.timer);
    killVoices(key);                       // every tick scheduled ahead goes with it (Law VI)
    if (!ctx) return;
    const at = ctx.currentTime;
    if (roll.gain) { const g = roll.gain.gain; try { g.cancelScheduledValues(at); g.setValueAtTime(Math.max(0.0001, g.value), at); g.exponentialRampToValueAtTime(0.0001, at + 0.05); } catch (e) { /* closed */ } }
    for (const n of roll.nodes) { try { if (typeof n.stop === 'function') n.stop(at + 0.07); } catch (e) { /* already */ } }
  }
  function stopRolls() { for (const key of Array.from(rolls.keys())) stopRoll(key.slice(5)); }
  /* ---- THE WHEEL ROLL. The roulette's rotor has no lever, so the throw is the lever: the gesture plays the
   * 'whir' one-shot and starts this loop, one for the room. A bearing bed whose band opens with the rotor and
   * a ratchet click whose rate IS the rotor's speed, both dying away as the wheel gives up. The station stops
   * it on the landing frame, so the drop and the settle keep that frame to themselves (Law X). ---- */
  let wheelRoll = null;
  function freeWheel(w) { for (const n of w.nodes) { try { n.disconnect(); } catch (e) { /* gone */ } } }
  function shapeWheel(w) {
    if (!ctx || !w || !w.gain) return;
    const t = ctx.currentTime, sp = clamp(w.speed, 0, 1);
    const lvl = Math.max(0.0001, WHEEL_BED * w.level * (0.12 + 0.88 * sp)), g = w.gain.gain;
    try { g.cancelScheduledValues(t); g.setValueAtTime(Math.max(0.0001, g.value), t); g.linearRampToValueAtTime(lvl, t + 0.08); } catch (e) { g.value = lvl; }
    if (!w.band) return;
    const hz = 120 + 330 * sp, f = w.band.frequency;
    try { f.cancelScheduledValues(t); f.setValueAtTime(Math.max(20, f.value), t); f.linearRampToValueAtTime(hz, t + 0.08); } catch (e) { f.value = hz; }
  }
  /** One ratchet click. It goes up a little as the rotor slows: the bearing tightening, the wheel's own tell. */
  function wheelTick(w) {
    const sp = clamp(w.speed, 0, 1);
    return noise(880 * SEMI(5 * (1 - sp)), 0, 0.016, WHEEL_TICK * w.level * (0.3 + 0.7 * sp), { q: 8, part: 'ratchet', dry: true });
  }
  function pumpWheel(w) {
    if (!ctx || wheelRoll !== w) return;
    const until = ctx.currentTime + TICK_AHEAD;
    if (w.next < ctx.currentTime) w.next = ctx.currentTime;
    for (let i = 0; i < 24 && w.next < until; i++) {
      try { render(wheelTick(w), w.next, w.set); } catch (e) { /* a click never breaks a beat */ }
      w.next += wheelGap(w.speed);
    }
    w.timer = setTimeout(() => pumpWheel(w), TICK_MS);
    if (w.timer && typeof w.timer.unref === 'function') w.timer.unref();
  }
  /** Start the rotor's loop, or follow it if it is already turning. */
  function startWheel(opts = {}) {
    const speed = clamp(num(opts.speed, 1), 0, 1);
    if (wheelRoll) { wheelRoll.speed = speed; shapeWheel(wheelRoll); return 1; }
    let set = voices.get(WHEEL_ROLL);
    if (!set) { set = new Set(); voices.set(WHEEL_ROLL, set); }
    const w = { speed, level: clamp(num(opts.level, 1), 0, 1), nodes: [], gain: null, band: null, next: 0, timer: 0, set };
    try {
      const g = ctx.createGain(); g.gain.value = 0.0001; g.connect(dry); w.gain = g; w.nodes.push(g);
      const src = ctx.createBufferSource(); src.buffer = noiseBuf; src.loop = true;
      const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.Q.value = 2.2; bp.frequency.value = 140;
      src.connect(bp); bp.connect(g); src.start(); src.onended = () => freeWheel(w);
      w.band = bp; w.nodes.push(src, bp);
    } catch (e) { /* a rotor that cannot hum still ratchets */ }
    wheelRoll = w;
    w.next = ctx.currentTime;
    shapeWheel(w);
    pumpWheel(w);
    return 1;
  }
  function stopWheel() {
    const w = wheelRoll;
    if (!w) return;
    wheelRoll = null;
    if (w.timer) clearTimeout(w.timer);
    killVoices(WHEEL_ROLL);                // every click scheduled ahead goes with it (Law VI)
    if (!ctx) return;
    const at = ctx.currentTime;
    if (w.gain) { const g = w.gain.gain; try { g.cancelScheduledValues(at); g.setValueAtTime(Math.max(0.0001, g.value), at); g.exponentialRampToValueAtTime(0.0001, at + 0.12); } catch (e) { /* closed */ } }
    for (const n of w.nodes) { try { if (typeof n.stop === 'function') n.stop(at + 0.15); } catch (e) { /* already */ } }
  }
  function killVoices(name) {
    const set = voices.get(name);
    if (!set) return;
    const at = ctx ? ctx.currentTime : 0;
    for (const v of Array.from(set)) { try { v.src.stop(at); } catch (e) { /* not started or gone */ } end(v); }
  }
  function killAll() { for (const name of Array.from(voices.keys())) killVoices(name); }

  const api = {
    /** Wake the context inside a gesture (a press, a pull, a key). True when there is a context to play on. */
    arm() { const c = graph(); if (!c) return false; warmSamples(); if (c.state === 'suspended' && !suspended) c.resume().catch(() => {}); return true; },
    /**
     * Play cue `name`. One-shots take `at` (seconds ahead) and the cue's own options; a bed loops until stop().
     * Returns the number of notes scheduled (0 when the kit cannot sound: still traced).
     */
    play(name, opts = {}) {
      note(name, opts);
      const window = DEDUPE_MS[name];
      if (window) { const t = clock(), was = lastAt.get(name); if (was != null && t - was < window) return 0; lastAt.set(name, t); }
      if (BEDS.includes(name)) { wanted.add(name); if (!ready()) return 0; return startBed(name, opts) ? 1 : 0; }
      if (name === WHEEL_ROLL) { if (!ready()) return 0; return startWheel(opts || {}); }
      if (ROLLS.includes(name)) { if (!ready()) return 0; return startRoll(opts && opts.reel, opts || {}); }
      const s = score(name, opts, rand);
      if (!s) return 0;
      if (!ready()) return 0;
      const t0 = ctx.currentTime + Math.max(0, num(opts.at, 0));
      let set = voices.get(name);
      if (!set) { set = new Set(); voices.set(name, set); }
      if (samples.has(name)) {
        try { return renderSample(name, samples.get(name), opts, t0, set); }
        catch (e) { samples.delete(name); } // A bad sample never silences the scored cue.
      }
      let n = 0;
      for (const nt of s.notes) { try { render(nt, t0, set); n++; } catch (e) { /* a note never breaks a beat */ } }
      return n;
    },
    /** Take a cue back: a bed fades (500 ms), a one-shot and everything it scheduled ahead stops now. */
    stop(name, opts = {}) {
      if (BEDS.includes(name)) { wanted.delete(name); if (ctx) stopBed(name, 0.5); return; }
      if (name === WHEEL_ROLL) { stopWheel(); return; }
      if (ROLLS.includes(name)) { if (opts && opts.reel != null) stopRoll(opts.reel); else stopRolls(); return; }
      if (ctx) killVoices(name);
    },
    /** Every one-shot stops; the beds fade. Law VI's skip and the suspend both come here. */
    stopAll({ beds: bedsToo = true } = {}) {
      if (!ctx) return;
      stopRolls();
      stopWheel();
      killAll();
      if (bedsToo) for (const name of Array.from(beds.keys())) stopBed(name, 0.15);
    },
    /** Suspend holds everything; resume brings the ambience back (the one-shots are gone for good, a spiral belongs
     *  to an fx hold that re-fires on its own). */
    suspend(on) {
      const want = !!on;
      if (want === suspended) return;
      suspended = want;
      if (!ctx || ctx.state === 'closed') return;
      if (suspended) {
        stopRolls();
        stopWheel();
        killAll();
        for (const name of Array.from(beds.keys())) stopBed(name, 0.05);
        ctx.suspend().catch(() => {});
      } else {
        ctx.resume().catch(() => {});
        for (const name of Array.from(wanted)) { if (name === 'ambience') startBed(name); }
      }
    },
    setMaster(v) { masterLevel = clamp(num(v, DEFAULT_MASTER), 0, 1); applyLevel(); },
    /** A quieter room under Calm (the slot and the wheel already play their cues softer there). */
    setTrim(v) { trimLevel = clamp(num(v, 1), 0, 1); applyLevel(); },
    mute(on) { muted = !!on; applyLevel(); },
    /** Follow a drum. Untraced on purpose: the reels call this every frame while they travel. */
    setRollSpeed(reel, speed) { const roll = rolls.get(rollKey(reel)); if (!roll) return; roll.speed = clamp(num(speed, roll.speed), 0, 1); shapeRoll(roll); },
    /** Follow the rotor. Untraced on purpose: the wheel calls this every frame while it turns. */
    setWheelSpeed(speed) { if (!wheelRoll) return; wheelRoll.speed = clamp(num(speed, wheelRoll.speed), 0, 1); shapeWheel(wheelRoll); },
    get master() { return masterLevel; },
    get muted() { return muted; },
    get suspended() { return suspended; },
    get context() { return ctx; },
    /** Close the context. A later arm() builds a fresh one (the room may be opened again on the same page). */
    dispose() {
      wanted.clear(); samples.clear(); sampleContext = null;
      if (ctx) {
        try { stopRolls(); stopWheel(); killAll(); for (const name of Array.from(beds.keys())) stopBed(name, 0.02); } catch (e) { /* closing */ }
        try { ctx.close().catch(() => {}); } catch (e) { /* already */ }
      }
      ctx = null; out = null; dry = null; send = null; noiseBuf = null; live.clear(); voices.clear(); beds.clear(); rolls.clear(); wheelRoll = null; lastAt.clear(); suspended = false;
    },
    trace,
    /** Test seam. */
    debug() { return { live: live.size, voices: Array.from(voices.keys()), beds: Array.from(beds.keys()), rolls: Array.from(rolls.keys()), wheel: !!wheelRoll, wanted: Array.from(wanted), master: masterLevel, trim: trimLevel, muted, suspended, has: !!ctx, state: ctx ? ctx.state : 'none' }; },
  };
  function applyLevel() {
    if (!out) return;
    const t = ctx.currentTime;
    try { out.gain.cancelScheduledValues(t); out.gain.setValueAtTime(out.gain.value, t); out.gain.linearRampToValueAtTime(level(), t + 0.05); } catch (e) { out.gain.value = level(); }
  }
  return api;
}

/** The room's one kit. The room arms it on the first gesture and owns its life; the stations only play on it. */
export const kit = createKit();
export default kit;
