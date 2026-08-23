/* ============================================================================
 * shell/shell.js - the screen router and the class runner.
 *
 * THREE SCREENS, one mount: the split-flap timetable board -> a class -> the
 * report card (plus the settings page, which is a screen too). Nothing here
 * renders a game: it deals the board, builds the §11 ctx, hands the class its
 * root and gets out of the way.
 *
 * DEFENSIVE BY DEFAULT (intake's guarded-registry philosophy):
 *   - engine/ and provider/ are OPTIONAL imports. If either is missing, still
 *     loading, or throws at import, the shell substitutes a null object and the
 *     class still runs - silent, but playable. A missing Distraction Engine must
 *     cost you the distractions, not the school.
 *   - every game call (create/start/pause/suspend/destroy) is try/caught. A game
 *     that throws on start gets the class_suspended treatment instead of taking
 *     the shell down with it.
 *   - the engine handle a class receives is ALLOWLISTED to its manifest's
 *     effectsConsumed, and deliberately does NOT expose suspend()/dispose() -
 *     lifecycle belongs to the shell, so a game cannot un-suspend itself while a
 *     mandatory video is playing.
 *
 * WHO OWNS WHAT
 *   grade  -> core/grades.js (never a game)
 *   tier   -> games/registry.js + the meta store (never a game)
 *   XP     -> C# (the page only reads payout-result)
 *   dates  -> UTC seeds content, LOCAL date rolls attendance (regression #978)
 * ==========================================================================*/

import { t, setLexicon, tierLabel } from '../core/lexicon.js';
import { makeRng } from '../core/rng.js';
import { buildTimetable, dayAdd } from '../core/timetable.js';
import { gradeClass, capsRaised } from '../core/grades.js';
import { createStore } from '../core/store.js';
import {
  loadGames, descriptors, tierFor, tierForPromotions, advance, suspendedStub, MAX_TIER,
} from '../games/registry.js';
import { createBoard } from './splitflap.js';
import { createCampus, campusState } from './campus.js';
import { createReportCard } from './reportcard.js';
import { createSettingsPage, boardSizeKey, SETTING_KEYS, isGlobalSettingKey } from './settings.js';
import { createCeremonies } from './ceremonies.js';
import { createPeek } from './peek.js';
import { createKeybinds } from './keybinds.js';
import { campusPill, createConfirm, exitBar, sign as signExit } from './exits.js';
import { createEnrollmentIntro, createPunchCeremony } from './enrollment.js';
import { createRecords } from './records.js';
import { loadFaceGeometry, ENROLL_PUNCHES } from './punchcard.js';
/* EMI, the mascot. Two of B's own modules: `mountEmi` builds the floating widget
 * (which dynamic-imports agent A's renderer optionally, so a broken face costs
 * EMI's expression and never the shell's boot) and `fireMoment` is the ONE verb
 * every seam below uses. Both are no-ops when there is no `#arc-emi` layer -
 * which is exactly the case in the node DOM double. */
import { mountEmi } from '../emi/index.js';
import { fireMoment } from '../emi/moments.js';

const FLAVOR_XP_CAP = 15;          // BUILD-CONTRACT §8 - the page clamps too
const MEATY_MAX_SEC = 300;
const QUICK_MAX_SEC = 180;
/** How often the shell's own class clock repaints the time bar. */
const CLOCK_TICK_MS = 250;

/* ----------------------------------------------------------------------------
 * DECK V - THE RAKE (house-rules.txt). Built ONCE here so all ten classes wear
 * it, and bounded by Law VI end to end: nothing below blocks, delays or moves an
 * exit, and the Esc ladder is untouched (escapeStep still walks exactly the
 * rungs it walked before this landed - see trap 29's corollary).
 *
 *   the end card      one-more framing over the report card, never instead of it
 *   streak jeopardy   what leaving actually costs, from the HOST-owned numbers
 *   the sunk-cost bar cross-class progress toward the next tier promotion
 *   the drop          a seeded ~1-in-6 surprise stamp. NO XP - C# owns XP.
 *   losses disguised  a C still gets a beat (shell/ceremonies.js payoff)
 * -------------------------------------------------------------------------- */

/** ~1 in 6 classes mint a drop. Seeded per (UTC day, game) - see rollRakeDrop. */
const RAKE_DROP_ODDS = 1 / 6;

/** THE EASING EXPONENT. house-rules.txt asks for a bar that is "always slightly
 *  more than half full by design of its easing curve": 0.5 ^ 0.72 = 0.606, so
 *  one promotion of two READS as more than half without the number lying (the
 *  label next to it always prints the honest "1 of 2"). */
const RAKE_EASE = 0.72;

/** The bonus archetypes a drop can mint. APPEND-ONLY: inserting a row would
 *  re-deal every past seed. Names and lines are lexicon rows with English
 *  fallbacks; nothing here is XP, a grade, or anything the ledger can see. */
const RAKE_DROPS = Object.freeze([
  Object.freeze({
    id: 'gold_star',
    nameKey: 'rake_drop_gold_star', name: 'Gold Star',
    lineKey: 'rake_drop_line_gold_star',
    line: 'Pinned to the board where everyone can see it.',
  }),
  Object.freeze({
    id: 'hall_pass',
    nameKey: 'rake_drop_hall_pass', name: 'Hall Pass',
    lineKey: 'rake_drop_line_hall_pass',
    line: 'Signed and dated. Good for exactly one wandering.',
  }),
  Object.freeze({
    id: 'gold_seal',
    nameKey: 'rake_drop_gold_seal', name: 'Gold Seal',
    lineKey: 'rake_drop_line_gold_seal',
    line: 'Pressed while the wax was still soft. It kept the shape.',
  }),
  Object.freeze({
    id: 'merit_mark',
    nameKey: 'rake_drop_merit_mark', name: 'Merit Mark',
    lineKey: 'rake_drop_line_merit_mark',
    line: 'Someone wrote your name in the good column tonight.',
  }),
]);

/**
 * The sunk-cost meter's fill curve. PURE and exported so the campus door card
 * and the suite can read the same number the end card paints.
 * @param {number} frac 0..1 honest fraction
 * @returns {number} 0..1 eased fill (strictly greater than `frac` in between)
 */
export function easedFill(frac) {
  const f = Math.max(0, Math.min(1, Number(frac) || 0));
  if (f <= 0) return 0;
  if (f >= 1) return 1;
  return Math.pow(f, RAKE_EASE);
}

/** PROMOTIONS_PER_TIER is registry-private, so derive it from the one exported
 *  fact (tier = 1 + floor(promotions / N)) instead of duplicating the constant -
 *  a curve change there must not silently desync this bar. Memoised. */
let promosPerTierMemo = 0;
function promosPerTier() {
  if (promosPerTierMemo) return promosPerTierMemo;
  const base = tierForPromotions(0);
  let n = 2;
  for (let p = 1; p <= 64; p++) { if (tierForPromotions(p) > base) { n = p; break; } }
  promosPerTierMemo = n;
  return n;
}

/**
 * Cross-class progress toward the next tier promotion, from a game's meta row.
 * PURE - `shell.progressFor(gameKey)` is the same thing read off the store, and
 * that is what the campus door card consumes.
 * @param {Object} gameMeta  store.gameMeta(gameKey)
 * @returns {{tier:number, nextTier:number, have:number, need:number,
 *            frac:number, eased:number, top:boolean}}
 */
export function tierProgress(gameMeta) {
  const meta = (gameMeta && typeof gameMeta === 'object') ? gameMeta : {};
  const need = promosPerTier();
  const tier = tierFor(meta);
  const promotions = Math.max(0, Math.round(Number(meta.promotions) || 0));
  if (tier >= MAX_TIER) {
    return { tier, nextTier: MAX_TIER, have: need, need, frac: 1, eased: 1, top: true };
  }
  const have = need > 0 ? Math.max(0, Math.min(need, promotions % need)) : 0;
  const frac = need > 0 ? have / need : 0;
  return {
    tier, nextTier: Math.min(MAX_TIER, tier + 1),
    have, need, frac, eased: easedFill(frac), top: false,
  };
}

/* ----------------------------------------------------------------------------
 * THE SPIRAL POOL
 * The engine's wash('spiral') paints a CSS conic gradient unless somebody
 * hands it a url (engine/index.js spiralUrl()). DTRH already ships real
 * spirals, and the host maps the whole of Resources/web, so they cost the
 * build nothing: /dtrh/assets/bubbles/effects/spirals/. Resolved RELATIVE to
 * this module so the app origin (ccp.game) and the scratch rig (127.0.0.1,
 * which junctions the same folder) both serve it.
 *
 * The weights are BYTE SIZES, not taste: sp6 is 123K and sp7 721K, the other
 * five are 2.2-2.7MB and sp5 alone is 5.3MB. One url is picked per class off
 * the class seed and then held, so a class loads at most ONE of them and a
 * retake dives the same water. Adding a file to the list reshuffles the pick
 * for every seed - append, never insert.
 *
 * TWO SEAMS OFF THE SAME ROWS (2026-08-23): `pickSpiralUrl` is the one-url pick
 * every class's `spiralUrl()` provider wears; `spiralPoolRows` is the whole
 * resolved pool a class may OFFER as answers (`ctx.spiralPool`). They walk the
 * same rows in the same order at the same weights, so factoring the second one
 * out moved no pick for any seed.
 * -------------------------------------------------------------------------- */
const SPIRAL_POOL = Object.freeze([
  ['sp6.gif', 34], ['sp7.gif', 26], ['sp1.gif', 9], ['sp2.webp', 9],
  ['sp4.webp', 9], ['sp3.gif', 8], ['sp5.gif', 5],
]);
/** THE LOOM: the player's own saved spirals ride `init.settings.loomSpirals` as
 *  absolute `https://ccp.spirals/loom_<slug>.gif` urls (the host maps the same
 *  folder DTRH exposes). They are APPENDED to the bundled pool at a mid weight,
 *  so a player with no Loom gets byte-identical picks and a player with one
 *  sees their own work in roughly a quarter of classes. Validated: string,
 *  https:, capped, de-duplicated. */
const LOOM_WEIGHT = 20;
const LOOM_CAP = 24;
function loomSpiralRows(settings) {
  const out = [];
  try {
    const list = settings && Array.isArray(settings.loomSpirals) ? settings.loomSpirals : [];
    const seen = new Set();
    for (const u of list) {
      if (typeof u !== 'string' || !/^https:\/\/ccp\.spirals\//.test(u) || seen.has(u)) continue;
      seen.add(u);
      out.push([u, LOOM_WEIGHT]);
      if (out.length >= LOOM_CAP) break;
    }
  } catch (e) { /* a bad list is an empty list */ }
  return out;
}
/** Resolve one pool row's file to the url the engine would actually paint. */
function spiralUrlFor(file) {
  if (/^https:\/\//.test(file)) return file;
  return new URL('../../dtrh/assets/bubbles/effects/spirals/' + file, import.meta.url).href;
}
/**
 * THE WHOLE POOL, resolved: `[{url, weight}]` - the seven bundled files as
 * absolute urls, then the player's Loom rows, de-duplicated by url (first wins,
 * so a Loom url that somehow shadowed a bundled one never doubles its weight).
 *
 * Factored out of `pickSpiralUrl` so a class can OFFER the pool as answers
 * ("which spiral did you just see") instead of only ever receiving one url.
 * `pickSpiralUrl` still walks the same rows in the same order with the same
 * weights and the same `|spiral` stream, so every class's `classSpiral` is
 * byte-identical to before. Additive: nothing that ignores `ctx.spiralPool`
 * moves. Never awaited, never fetched here.
 */
function spiralPoolRows(settings) {
  const rows = [];
  const seen = new Set();
  try {
    for (const row of SPIRAL_POOL.concat(loomSpiralRows(settings))) {
      const url = spiralUrlFor(row[0]);
      if (!url || seen.has(url)) continue;
      seen.add(url);
      rows.push({ url, weight: row[1] });
    }
  } catch (e) { /* a bad list is an empty list */ }
  return rows;
}
function pickSpiralUrl(seed, settings) {
  try {
    const roll = makeRng(String(seed == null ? '' : seed) + '|spiral');
    const pool = SPIRAL_POOL.concat(loomSpiralRows(settings));
    let total = 0;
    for (const row of pool) total += row[1];
    let r = roll() * total;
    let file = pool[0][0];
    for (const row of pool) { r -= row[1]; if (r < 0) { file = row[0]; break; } }
    return spiralUrlFor(file);
  } catch (e) { return null; }
}

/** Monotonic-if-available clock for the time bar (a wall-clock step must not
 *  teleport the fill; performance.now cannot be moved by the system clock). */
function nowMs() {
  try {
    if (typeof performance === 'object' && performance && typeof performance.now === 'function') {
      return performance.now();
    }
  } catch (e) { /* noop */ }
  return Date.now();
}

/** Palette keys we accept from init.palette, and the CSS token each one drives. */
const PALETTE_TOKENS = Object.freeze({
  ground: '--ground', navy: '--navy', panel: '--panel', ink: '--ink',
  accent: '--pink', accent2: '--lav', gold: '--gold',
  // tolerated aliases so a mod skin authored against the mockup's names works
  pink: '--pink', lav: '--lav', lavender: '--lav', line: '--line',
});

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/* ----------------------------------------------------------------------------
 * OPTIONAL MODULES - intake's loadOptional, verbatim in spirit.
 * -------------------------------------------------------------------------- */
async function loadOptional(path, factoryName, fallback, say) {
  try {
    const mod = await import(path);
    const f = mod && (mod[factoryName] || (mod.default && typeof mod.default === 'function' ? mod.default : null));
    if (typeof f === 'function') return f;
    say('module ' + path + ': no ' + factoryName + '() export - using fallback');
  } catch (e) {
    say('module ' + path + ': import failed (' + ((e && e.message) || e) + ') - using fallback');
  }
  return fallback;
}

const NULL_ENGINE_FACTORY = () => ({
  setHeat() {}, fire() { return false; }, sustain() { return false; }, stop() {},
  setpiece() {}, beat() {}, ceremony() { return false; }, suspend() {}, dispose() {},
});

const NULL_ASSETS_FACTORY = () => ({
  claim() { return Promise.resolve({ next() { return null; }, release() {} }); },
});

/* ============================================================================
 * THE SHELL
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Object} o.init      the init projection
 * @param {Object} o.bridge
 * @param {Object} o.dom       {topbar, screen, fx, ceremony}
 * @param {Function=} o.toast
 * @param {Function=} o.log
 */
export async function createShell({ init, bridge, dom, toast, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const shout = typeof toast === 'function' ? toast : () => {};
  const src = init || {};

  /* ---------------------- look & lexicon -------------------------------- */
  setLexicon(src.lexicon);
  applyPalette(src.palette, say);
  const reducedMotion = !!src.reducedMotion || src.motionLevel === 0;
  if (reducedMotion && document.documentElement) {
    document.documentElement.classList.add('arc-reduced');
  }

  /* ---------------------- state ----------------------------------------- */
  /* THE GRADE EMI SPEAKS ABOUT. Set by finishClass, read once by showReport, so
   * a report opened cold from the Records Office gets the day's line and a report
   * opened ON a finish does not have her talk over her own win face. */
  let lastGraded = null;

  const utcDateSeed = String(src.utcDateSeed || '');
  const localDate = String(src.localDate || utcDateSeed);

  /* THE DAY'S WORD POOL. init.words is the floor (it may legally be EMPTY); a
   * class may ADD to it through ctx.absorb / ctx.sessionWords, and every engine
   * built after that gets the longer list - so a word absorbed in Homeroom rides
   * the rest of today's classes. SESSION-ONLY, both ways: nothing here is
   * persisted, nothing is sent to the host, and SubliminalPool is never written
   * (DECISIONS #10, the ramp-never-writes precedent). Reload and it is gone. */
  const ABSORB_MAX_LEN = 40;         // a subliminal card, not a paragraph
  const ABSORB_MAX_ADDED = 64;       // a runaway game cannot grow this unbounded
  const dayWords = (Array.isArray(src.words) ? src.words : [])
    .filter((w) => typeof w === 'string' && w.trim());
  let absorbed = 0;

  /* THE DAY'S TRIGGERS: init.triggers = [{text, audio}] - the same phrases as
   * init.words, each with its whisper clip url (ccp.subaudio / ccp.modaudio) or
   * null. Frozen, never absorbed into: a game that wants a clip reads
   * ctx.triggers, a game that wants words reads ctx.words. Echo's pads are the
   * consumer (0823). Garbage rows are dropped, never thrown on. */
  const dayTriggers = Object.freeze((Array.isArray(src.triggers) ? src.triggers : [])
    .filter((t) => t && typeof t.text === 'string' && t.text.trim())
    .map((t) => Object.freeze({ text: t.text, audio: typeof t.audio === 'string' && t.audio ? t.audio : null })));

  /** @returns {boolean} true when the word was taken (new, legal, under the cap). */
  function absorbWord(word) {
    if (typeof word !== 'string') return false;
    const w = word.trim();
    // Control characters would render as tofu in a sub_flash card; a newline
    // would break the one-line layout outright.
    if (!w || w.length > ABSORB_MAX_LEN || /[\u0000-\u001f\u007f]/.test(w)) return false;
    if (absorbed >= ABSORB_MAX_ADDED) return false;
    if (dayWords.some((x) => x.toUpperCase() === w.toUpperCase())) return false;
    dayWords.push(w);
    absorbed += 1;
    say('absorbed a session word (' + dayWords.length + ' in the day pool)');
    return true;
  }

  /** The sink a class reaches for. `add` is the verb Daily Trigger probes. */
  const sessionWords = {
    add: absorbWord,
    list: () => dayWords.slice(),
    get size() { return dayWords.length; },
  };

  const store = createStore({ bridge, initialMeta: src.meta, log: say });
  const keybinds = createKeybinds({ init: src, bridge, log: say });

  /** gameKey -> {grade, zen, composite, capped, tier, xp, levelUp} for TODAY. */
  const results = Object.create(null);
  /** gameKey -> share payload handed to the one share pipeline. */
  const shares = Object.create(null);

  let screen = 'board';            // 'board' | 'class' | 'report' | 'settings' | 'records'
  let board = null;
  let campus = null;               // the night-campus hub (OPTIONAL - see showBoard)
  let extrasBox = null;            // replay/report bar + yesterday strip container
  let settingsPage = null;
  let reportCard = null;
  /** The Deck V one-more card. It sits OVER the report card, never instead of
   *  it (see showEndCard) - `{root, destroy}` while live, null otherwise. */
  let endCard = null;
  /** The Records Office screen (PUNCHCARD §6) - built lazily, kept for reuse. */
  let recordsPage = null;
  /* THE PUNCH-CARD CEREMONY. `punchStage` is the live overlay; `punchArm` is
   * what the shell is WAITING for while the host answers `class-ended`.
   * Both are null the rest of the time. */
  let punchStage = null;
  let punchArm = null;             // {gameKey, mode:'daily'|'enrollment', timer}
  let active = null;               // the running class (see startClass)
  let suspendedGlobally = false;
  let destroyed = false;
  // Host opened the building through the dev switch (`--arcademy`). Unlocks
  // Begin on every campus door regardless of the seed; never true for players.
  const devPass = src.devDoor === true;

  /* ---------------------- registry + timetable -------------------------- */
  const games = await loadGames(say);
  const timetable = buildTimetable({
    dateSeed: utcDateSeed,
    registry: descriptors(games.list),
    overrideCalendar: src.overrideCalendar,
  });
  say('timetable ' + timetable.dateSeed + ' [' + timetable.source + ']: '
    + timetable.classes.map((c) => c.gameKey).join(', ')
    + (timetable.relaxed.length ? ' (relaxed: ' + timetable.relaxed.join(',') + ')' : ''));

  /* ---------------------- engine + provider ----------------------------- */
  /* THE CARD FACES. Optional, guarded, once: art/punchcard/faces.json says
   * where the stamps, the crest and the live text sit on each class's face
   * image. Missing (it ships with the art batch) leaves every card on the
   * gradient floor, which is a finished card - so this is awaited for ordering
   * only and can never fail the boot. */
  await loadFaceGeometry(say);

  const createEngine = await loadOptional('../engine/index.js', 'createEngine', NULL_ENGINE_FACTORY, say);
  const createAssets = await loadOptional('../provider/index.js', 'createAssets', NULL_ASSETS_FACTORY, say);

  let assets;
  try {
    assets = createAssets({
      bridge,
      remoteMediaEnabled: !!src.remoteMediaEnabled,
      remoteMediaRatio: src.remoteMediaRatio == null ? 0 : src.remoteMediaRatio,
      offlineMode: !!src.offlineMode,
      platform: src.platform || { isTouch: false, hasHaptics: false, host: 'desktop' },
      // The page cannot enumerate a virtual host, so the host ships the local
      // inventory as `localAssets` riding init.settings; the provider reads it
      // off this very object (provider/index.js resolveManifest).
      settings: src.settings,
      // Seeded so the same UTC day serves the same media order to everyone, and
      // logged straight to Serilog instead of the CustomEvent seam.
      rng: makeRng(utcDateSeed + '|assets'),
      log: (m) => say('[assets] ' + m),
    }) || NULL_ASSETS_FACTORY();
  } catch (e) {
    say('createAssets threw (' + ((e && e.message) || e) + ') - local-only null provider');
    assets = NULL_ASSETS_FACTORY();
  }

  const ceremonies = createCeremonies({
    engine: null,                  // rebound per class (the engine is per class)
    layer: dom && dom.ceremony,
    reducedMotion,
    log: say,
  });

  reportCard = createReportCard({ ceremonies, toast: shout, log: say });

  /* ---------------------- helpers --------------------------------------- */
  function gameName(key) {
    const entry = games.byKey[key];
    const title = entry && entry.mod && entry.mod.title;
    return t('game_' + key, title || String(key).replace(/_/g, ' '));
  }

  function maxTier() {
    let best = 1;
    for (const entry of games.list) {
      const tier = tierFor(store.gameMeta(entry.key));
      if (tier > best) best = tier;
    }
    return best;
  }

  function allDone() {
    const keys = timetable.classes.map((c) => c.gameKey);
    return keys.length > 0 && keys.every((k) => !!results[k]);
  }

  /**
   * Today's recorded row for a class, from this session OR from the meta store
   * (the player may have graded it, closed the Arcademy and come back). Its
   * presence is what makes the next run a RETAKE: still playable, still graded,
   * still stamped - but the host has already paid the day's XP for it and the
   * day's record keeps the FIRST grade.
   * @returns {?{grade:string, zen:boolean}}
   */
  function todaysRecord(gameKey) {
    if (results[gameKey]) return results[gameKey];
    try {
      const day = store.day(localDate);
      const row = day && day.classes ? day.classes[gameKey] : null;
      return (row && row.grade) ? row : null;
    } catch (e) { return null; }
  }

  /* ---------------------- punch cards (PUNCHCARD §2.3) ------------------
   * The cards are HOST-owned and read-only here (core/store.js refuses a page
   * write). Two things the shell derives from them and nothing else does:
   * which rooms are permanently unlocked, and whether a class still owes its
   * once-ever enrollment. Both are pure reads off `store.punchCard`, so a card
   * restored from the server mirror moves them without any other bookkeeping.
   * -------------------------------------------------------------------- */

  /** gameKey -> true for every card that reached its tenth hole. */
  function unlockedMap() {
    const out = Object.create(null);
    for (const entry of games.list) {
      try { if (store.punchCard(entry.key).complete) out[entry.key] = true; }
      catch (e) { /* a card that cannot be read is simply not an unlock */ }
    }
    return out;
  }

  /** Is this room open tonight regardless of the board? (a full card, §2.3) */
  function isUnlocked(gameKey) {
    try { return !!store.punchCard(gameKey).complete; } catch (e) { return false; }
  }

  /**
   * Does this class still owe its enrollment? DERIVED from `enrolledAt` and
   * nothing else (§2.2) - there is no separate seen-flag, so a reinstall that
   * restores cards suppresses the intro for free and a card the host has not
   * enrolled yet always gets it, once.
   */
  function needsEnrollment(gameKey) {
    try { return !store.punchCard(gameKey).enrolled; } catch (e) { return false; }
  }

  function clearScreen() {
    // The card ceremony is deliberately NOT dropped here. It rides ON the report
    // card, and `onPayout` re-renders that report on the same screen - a wipe
    // that took the ceremony with it would delete the card the instant the host
    // paid out, which is the exact moment it is meant to be up. It is dropped
    // beside dismissEndCard() at every real screen change instead, and
    // showReport() re-seats it after a repaint (same treatment, same reason).
    // The campus owns a 1s bell interval; a screen wipe must not orphan it.
    if (campus) { try { campus.destroy(); } catch (e) { /* noop */ } campus = null; }
    extrasBox = null;
    // Every screen switch funnels through here, so no throw path can strand
    // the immersive stage lock (the campus's arc-campus-on unwinds in its own
    // destroy above, same guarantee).
    setStage(null);
    if (!dom || !dom.screen) return;
    dom.screen.textContent = '';
  }

  /**
   * The immersive stage lock (the campus pattern, generalised). While a class
   * or the report card is up the page IS the scene: <html> carries the marker
   * class (styles.css pins the stage to the viewport and locks body scroll)
   * and the topbar retires - the proctor strip / the report's own chrome carry
   * its job. clearScreen() always unwinds it.
   * @param {?string} mode  'arc-class-on' | 'arc-report-on' | null
   */
  function setStage(mode) {
    const html = document.documentElement;
    if (html && html.classList) {
      html.classList.remove('arc-class-on', 'arc-report-on');
      if (mode) html.classList.add(mode);
    }
    if (dom && dom.topbar) dom.topbar.hidden = !!mode;
  }

  /* ---------------------- top bar --------------------------------------- */
  function renderTopbar() {
    if (!dom || !dom.topbar) return;
    const bar = dom.topbar;
    bar.hidden = false;
    bar.textContent = '';
    bar.appendChild(el('span', 'arc-title', t('arcademy', 'The Arcademy')));
    bar.appendChild(el('span', 'chip year', tierLabel(maxTier())));

    const streak = store.streak();
    const flame = el('span', 'chip flame');
    flame.appendChild(el('span', null, '🔥 ' + t('attendance', 'Attendance') + ' '));
    flame.appendChild(el('b', null, String(streak.count | 0)));
    bar.appendChild(flame);
    if (streak.perfectDays) {
      bar.appendChild(el('span', 'chip', t('perfect_attendance', 'Perfect Attendance')
        + ' x' + (streak.perfectDays | 0)));
    }

    bar.appendChild(el('span', 'arc-spacer'));
    if (suspendedGlobally) bar.appendChild(el('span', 'chip warn', t('class_suspended', 'Class Suspended')));

    const gear = el('button', 'btn ghost', t('settings', 'Settings'));
    gear.type = 'button';
    gear.addEventListener('click', () => showSettings());
    bar.appendChild(gear);
  }

  /* ============================ SCREEN: BOARD =========================== */
  /**
   * @param {boolean=} compact  the campus departure board carries only period,
   *   flaps and status - family/budget chips live in the room's door card. The
   *   plain fallback screen keeps the full chip row.
   */
  function buildRows(compact) {
    return timetable.classes.map((c) => {
      const entry = games.byKey[c.gameKey];
      const record = todaysRecord(c.gameKey);
      const done = !!record;
      const suspended = c.missing || !entry || !entry.ok;
      const chips = [];
      if (!compact) {
        if (c.homeroom) chips.push({ text: t('homeroom', 'Homeroom'), kind: 'homeroom' });
        chips.push({ text: t('family_' + c.family, c.family) });
        chips.push({ text: c.timeBudgetSec + 's', kind: 'num' });
      }
      if (suspended) chips.push({ text: t('class_suspended', 'Class Suspended'), kind: 'warn' });
      if (done) {
        chips.push({ text: t('grade', 'Grade') + ' ' + String(record.grade).toUpperCase() });
        // The row stays CLICKABLE on purpose - a graded class is replayable, it
        // just pays nothing (the host's per-UTC-day XP ledger) and keeps the
        // grade it already earned. The note is the whole warning.
        chips.push({ text: t('retake', 'Retake') });
      }
      return {
        id: c.gameKey,
        time: c.timeLabel,
        label: gameName(c.gameKey),
        chips,
        done,
        disabled: suspendedGlobally,
        ariaLabel: c.timeLabel + ' ' + gameName(c.gameKey),
      };
    });
  }

  /** Today's rows for the campus state mapper (session results OR meta rows). */
  function buildCampusState() {
    const records = Object.create(null);
    for (const c of timetable.classes) records[c.gameKey] = todaysRecord(c.gameKey);
    return campusState({
      classes: timetable.classes,
      records,
      suspended: suspendedGlobally,
      devPass,
      // A FULL CARD IS A PERMANENT DOOR (PUNCHCARD §2.3). It sits between the
      // board and the dev pass in the CTA order, and it is the only one of the
      // three a player can ever earn.
      unlocked: unlockedMap(),
      // Free Swim is a property of the GAME, not of tonight's board: a room the
      // timetable never dealt can still be swum, so the map covers every
      // registered game and campusState keeps the ones that have a room.
      endless: endlessMap(),
    });
  }

  function campusStats() {
    const streak = store.streak();
    return {
      streak: streak.count | 0,
      perfectDays: streak.perfectDays | 0,
      tier: maxTier(),
    };
  }

  /** Descriptor detail the campus door card shows (family, budget, tier). */
  function campusDescriptors() {
    return timetable.classes.map((c) => ({
      gameKey: c.gameKey,
      family: c.family,
      timeBudgetSec: c.timeBudgetSec,
      homeroom: !!c.homeroom,
      tier: tierFor(store.gameMeta(c.gameKey)),
      endless: endlessFor(c.gameKey),
    }));
  }

  /** Replay/report bar + yesterday strip + failed-games note, re-renderable. */
  function renderBoardExtras(mount) {
    if (!extrasBox || extrasBox.parentNode !== mount) {
      extrasBox = el('div', 'arc-boardextras');
      mount.appendChild(extrasBox);
    }
    extrasBox.textContent = '';

    const bar = el('div', 'arc-classbar');
    const replay = el('button', 'btn ghost', t('replay_board', 'Flip the board again'));
    replay.type = 'button';
    replay.addEventListener('click', () => board && board.replay());
    bar.appendChild(replay);
    if (allDone()) {
      const rc = el('button', 'btn', t('report_card', 'Report Card'));
      rc.type = 'button';
      rc.addEventListener('click', () => showReport());
      bar.appendChild(rc);
    }
    extrasBox.appendChild(bar);

    /* yesterday's strip (the mockup's report-card row) */
    const y = store.day(dayAdd(localDate, -1));
    const yClasses = y && y.classes ? Object.keys(y.classes) : [];
    if (yClasses.length) {
      const strip = el('div', 'reportcard');
      strip.appendChild(el('span', 'rlabel', 'Yesterday'));
      for (const key of yClasses.slice(0, 4)) {
        const r = y.classes[key] || {};
        const g = String(r.grade || '').toLowerCase();
        const cell = el('span', 'rcell');
        cell.appendChild(el('span', 'grade ' + (g === 'pass' ? 'pass' : g || 'none'),
          r.grade ? String(r.grade).toUpperCase() : '--'));
        cell.appendChild(el('span', null, gameName(key)));
        strip.appendChild(cell);
      }
      extrasBox.appendChild(strip);
    }

    if (games.failed.length) {
      extrasBox.appendChild(el('p', 'arc-note',
        games.failed.length + ' class(es) could not load and show as '
        + t('class_suspended', 'Class Suspended') + '.'));
    }
  }

  function showBoard(opts) {
    const silent = !!(opts && opts.silent);
    // Leaving the report drops the one-more card AND the punch card with it (an
    // overlay that outlived its screen would be a second, invisible Esc rung).
    dismissEndCard();
    dismissPunchStage();

    // FAST REPAINT: a live campus is patched, never rebuilt - tearing the stage
    // down on every meta echo would restart every ambient animation mid-frame.
    if (silent && screen === 'board' && campus && board) {
      board.setRows(buildRows(true), { animate: false });
      campus.update(buildCampusState(), campusStats());
      campus.noteDescriptors(campusDescriptors());
      if (extrasBox && extrasBox.parentNode) renderBoardExtras(extrasBox.parentNode);
      return;
    }

    const wasScreen = screen;
    screen = 'board';
    teardownClass();
    clearScreen();
    renderTopbar();
    // EMI SEAM: arriving at the campus, not repainting it.
    if (wasScreen !== 'board') fireMoment('greet');
    if (!dom || !dom.screen) return;

    if (src.audioOnlySession) {
      // The host gate should have refused the launch (BUILD-CONTRACT §3); this is
      // the belt-and-braces path so a stale page can never start a visual class.
      const panel = el('div', 'arc-panel');
      panel.appendChild(el('p', 'arc-kicker', t('timetable', 'Timetable')));
      panel.appendChild(el('h1', 'arc-h1', t('arcademy', 'The Arcademy')));
      panel.appendChild(el('p', 'arc-lede',
        'The Arcademy is closed during an audio-only session. Your attendance is safe.'));
      dom.screen.appendChild(panel);
      return;
    }

    const onSelectRow = (gameKey) => {
      const cls = timetable.classes.find((c) => c.gameKey === gameKey);
      if (cls) startClass(cls);
    };
    board = createBoard({
      rows: buildRows(true),
      reducedMotion,
      animate: !silent,
      onSelect: onSelectRow,
    });

    /* THE CAMPUS IS OPTIONAL. A hub that throws costs the scenery, never the
     * school: the catch below renders the plain panel the shell shipped with
     * (full chip rows, same buttons), which is also what the headless suites
     * drive when the stage cannot build. */
    try {
      campus = createCampus({
        state: buildCampusState(),
        gameName,
        banner: timetable.banner,
        stats: campusStats(),
        reducedMotion,
        // DECK V's sunk-cost meter, for the door card. Same numbers the end card
        // paints (shell.progressFor is the public twin) - a door that promised a
        // different fraction than the end card would be the one lie the rake
        // cannot afford. Optional on the campus side: an older hub ignores it.
        progressFor,
        on: {
          begin: (gameKey) => {
            const cls = timetable.classes.find((c) => c.gameKey === gameKey);
            if (cls) { startClass(cls); return; }
            // THE DEV DOOR: a room off tonight's board still opens when the host
            // came in through `--arcademy`. Graded + timed like a dealt class,
            // built from the registry descriptor (freeSwimClass's parachute).
            // THE EARNED DOOR, then the dev one. A completed punch card opens
            // its room every night - through the SAME path the dev pass uses, so
            // an unlocked run is an ordinary graded class end to end (grades in
            // `days`, XP once per UTC day, attendance idempotent). Nothing
            // host-side gates which room may start; this is the whole feature.
            if (isUnlocked(gameKey)) {
              say('punch card unlock: ' + gameKey + ' is off the board - graded run anyway');
              startClass(freeSwimClass(gameKey));
              return;
            }
            if (devPass) {
              say('dev pass: ' + gameKey + ' is off the board - graded run anyway');
              startClass(freeSwimClass(gameKey));
            }
          },
          freeSwim: (gameKey) => startFreeSwim(gameKey),
          records: () => showRecords(),
          registrar: () => showSettings(),
        },
        log: say,
      });
      campus.noteDescriptors(campusDescriptors());
      campus.boardMount.appendChild(board.root);
      renderBoardExtras(campus.footMount);
      dom.screen.appendChild(campus.root);
      // The window IS the campus: the topbar's content is diegetic in-scene
      // (crest, student ID, Registrar/gear), so the bar itself steps aside.
      // Every other screen re-shows it through renderTopbar().
      if (dom.topbar) dom.topbar.hidden = true;
    } catch (e) {
      say('campus hub failed (' + ((e && e.message) || e) + ') - plain board fallback');
      // If the stage built and a LATER line threw, its bell interval and the
      // arc-campus-on body lock must not outlive it.
      if (campus) { try { campus.destroy(); } catch (e2) { /* noop */ } }
      campus = null;
      try { board.destroy(); } catch (e2) { /* noop */ }
      board = createBoard({
        rows: buildRows(false),
        reducedMotion,
        animate: !silent,
        onSelect: onSelectRow,
      });
      const panel = el('div', 'arc-panel');
      panel.appendChild(el('p', 'arc-kicker', t('timetable', 'Timetable')));
      panel.appendChild(el('h1', 'arc-h1', t('arcademy', 'The Arcademy')));
      if (timetable.banner) panel.appendChild(el('p', 'arc-lede', timetable.banner));
      panel.appendChild(board.root);
      renderBoardExtras(panel);
      dom.screen.appendChild(panel);
    }
  }

  /* ============================ SCREEN: SETTINGS ======================== */
  function showSettings() {
    if (active) pauseClass(true);
    dismissEndCard();
    dismissPunchStage();
    screen = 'settings';
    clearScreen();
    renderTopbar();
    settingsPage = createSettingsPage({
      init: src,
      bridge,
      games: games.list,
      keybinds,
      log: say,
      onClose: () => (active ? showClassScreen() : showBoard()),
    });
    dom.screen.appendChild(settingsPage.root);
  }

  /* ============================ SCREEN: REPORT ========================== */
  function showReport() {
    const wasScreen = screen;
    screen = 'report';
    clearScreen();
    renderTopbar();
    reportCard.render({
      timetable,
      results,
      shares,
      streak: store.streak(),
      perfect: allDone(),
      tier: maxTier(),
      title: t('report_card', 'Report Card'),
      dateLabel: localDate,
      onDone: () => showBoard(),
    });
    dom.screen.appendChild(reportCard.root);
    // THE END CARD OUTLIVES A REPORT REPAINT. onPayout re-renders the report on
    // the same screen; without this the one-more card would vanish the instant
    // the host paid out, which is the exact moment it is meant to be up.
    if (endCard && endCard.root) dom.screen.appendChild(endCard.root);
    // ...and so does the punch card, for exactly the same reason. It is mounted
    // last so it stays the topmost thing on the screen it was earned on.
    if (punchStage && punchStage.root) dom.screen.appendChild(punchStage.root);
    setStage('arc-report-on');
    /* EMI SEAM: the one moment she uses words (the talk rule puts them in the
     * bubble, never on the face). Two suppressions, both load-bearing:
     * a REPAINT is not an arrival (onPayout re-renders this very screen the
     * instant the host pays out - trap 50's neighbour), and a FRESH finish
     * already got its win/miss reaction in finishClass one screen ago. */
    if (wasScreen !== 'report') {
      if (lastGraded && lastGraded.fresh) lastGraded.fresh = false;
      else fireMoment('reportCard', lastGraded || { grade: null, perfect: allDone() });
    }
  }

  /* ============================ SCREEN: RECORDS =========================
   * PUNCHCARD §6. The campus's Records door used to open the day's report card
   * and nothing else; it opens the office now, and the report card is one press
   * further in - still the ONE share pipeline (trap 13), still unchanged.
   * ==================================================================== */
  function showRecords() {
    screen = 'records';
    dismissEndCard();
    dismissPunchStage();
    clearScreen();
    renderTopbar();
    if (!recordsPage) {
      recordsPage = createRecords({
        gameName,
        // The card reader, not the blob: store.punchCard normalizes, so the
        // screen never has to guess what a half-written card means.
        punchCard: (key) => store.punchCard(key),
        log: say,
      });
    }
    recordsPage.render({
      // Registry order, every registered class - including the ones never
      // played. An unattended card on the wall IS the advertisement (§6).
      gameKeys: games.list.map((e) => e.key),
      onBack: () => showBoard(),
      onReport: () => showReport(),
      reportLabel: t('report_card', 'Report Card'),
    });
    if (dom && dom.screen) dom.screen.appendChild(recordsPage.root);
    setStage('arc-report-on');
  }

  /* ============================ THE PUNCH CARD ==========================
   * PUNCHCARD §4. The ceremony is the ONLY place the page draws a hole, and it
   * draws the one the HOST minted: `punchcard-result` carries the post-mint
   * card and `justUnlocked`, so nothing here counts anything.
   *
   * TWO PATHS, ONE OVERLAY:
   *   daily       arm on `class-ended`, wait for the frame, punch one hole.
   *               A no-op mint (same-day retake, full card) shows nothing at
   *               all - a ceremony for a hole that was not punched is a lie.
   *   enrollment  the first graded finish of a class. The shell KNOWS it is
   *               owed (the card has no `enrolledAt`), so it suppresses that
   *               run's daily beat, runs the three-punch ceremony on its own
   *               clock, and posts `enrollment-done` when the punches land.
   *               The host's answer supersedes the daily stamp it already made,
   *               which is what keeps day one at exactly two either way round.
   * ==================================================================== */

  /** Drop the ceremony overlay. Idempotent; every screen change funnels here. */
  function dismissPunchStage() {
    if (!punchStage) return false;
    const st = punchStage;
    punchStage = null;
    try { st.destroy(); } catch (e) { /* noop */ }
    return true;
  }

  /** Stop waiting for a `punchcard-result`. */
  function disarmPunch() {
    if (!punchArm) return;
    if (punchArm.timer) { try { clearTimeout(punchArm.timer); } catch (e) { /* noop */ } }
    punchArm = null;
  }

  /**
   * Arm the ceremony for a class that has just ended.
   * @param {string} gameKey
   * @param {'daily'|'enrollment'} mode
   */
  function armPunch(gameKey, mode) {
    disarmPunch();
    punchArm = { gameKey, mode, timer: 0 };
    // A HOST THAT NEVER ANSWERS IS NOT AN ERROR. An older build (or a frame
    // dropped on a slow save) simply means no card beat this run; the report
    // card behind it is untouched. Stop listening rather than waiting forever.
    punchArm.timer = setTimeout(() => {
      if (punchArm && punchArm.gameKey === gameKey) {
        say('punch card: no punchcard-result for ' + gameKey + ' - no ceremony this run');
        disarmPunch();
      }
    }, 4000);
  }

  /** Mount the card, full screen, over the report it was earned on. */
  function runPunchCeremony(o) {
    // EMI SEAM: the stamp lands. ^_^ + hearts, (≧◡≦) on a 3-day streak, COOL on
    // the tenth hole - moments.js owns which.
    try {
      fireMoment('stamp', {
        streak: store.streak().count,
        perfect: !!(o && o.justUnlocked),
        grade: (o && o.justUnlocked) ? 's' : null,
      });
    } catch (e) { /* a mascot may never break a ceremony */ }
    dismissPunchStage();
    if (!dom || !dom.screen) return null;
    const spec = o || {};
    punchStage = createPunchCeremony({
      mount: dom.screen,
      gameKey: spec.gameKey,
      name: gameName(spec.gameKey),
      card: spec.card,
      reason: spec.reason,
      minted: spec.minted,
      from: spec.from,
      to: spec.to,
      justUnlocked: !!spec.justUnlocked,
      reducedMotion,
      onPunched: spec.onPunched,
      onDone: () => { punchStage = null; },
      log: say,
    });
    return punchStage;
  }

  /* ============================ DECK V: THE RAKE ========================
   * Everything below is ADDITIVE and bounded by Law VI. Read the three rules
   * before touching it:
   *   1. the report card is never replaced - the end card is an overlay on the
   *      same screen, and the report's own Done button still works underneath;
   *   2. nothing here handles Esc. escapeStep() below is byte-for-byte the
   *      ladder it always was, so a suspend overlay still owns the key
   *      (trap 29's corollary) and boot.js still gets its rung back;
   *   3. no XP, ever. C# owns the XP table (trap 23) - a drop is a stamp and a
   *      line of lexicon, nothing the ledger can see.
   * ==================================================================== */

  /**
   * STREAK JEOPARDY. What leaving actually costs, in the HOST's own numbers
   * (core/store.js reads them off `payout-result` / the blob snapshot; the page
   * may not write them). Three honest answers and no fourth:
   *   - today is NOT credited yet -> the streak goes cold
   *   - today IS credited          -> say so; leaving costs nothing
   *   - the numbers are unknown    -> say NOTHING (never invent jeopardy)
   * @returns {?string}
   */
  function jeopardyLine() {
    let s;
    try { s = store.streak(); } catch (e) { return null; }
    if (!s) return null;
    const n = s.count | 0;
    // A zero (or absent) streak is also the "we do not know" answer, and both
    // deserve silence: a fake number here would be the shell lying about the
    // one thing it does not own.
    if (!Number.isFinite(n) || n <= 0) return null;
    const credited = (s.lastLocalDate && String(s.lastLocalDate) === localDate)
      || (s.classesToday | 0) > 0;
    const tpl = credited
      ? t('rake_streak_credited', 'Attendance x{n} is banked for today already.')
      : t('rake_streak_cold', 'Attendance x{n} goes cold if today ends here.');
    return String(tpl).replace('{n}', String(n));
  }

  /** Say the jeopardy on the way out. Never blocks, never delays, never asks. */
  function announceJeopardy() {
    const line = jeopardyLine();
    if (!line) return null;
    say('leave-class jeopardy: ' + line);
    try { shout(line); } catch (e) { /* a toast may never hold a door */ }
    return line;
  }

  /**
   * THE VARIABLE-RATIO DROP. Seeded per (UTC day, game) so a retake replays the
   * same night and two players on the same day see the same board - Law V. The
   * caller gates it: never on a retake, never on a free swim.
   * @returns {?Object} one RAKE_DROPS row, or null
   */
  function rollRakeDrop(gameKey) {
    try {
      const roll = makeRng(utcDateSeed + '|rake-drop|' + String(gameKey));
      if (roll() >= RAKE_DROP_ODDS) return null;
      const i = Math.min(RAKE_DROPS.length - 1,
        Math.max(0, Math.floor(roll() * RAKE_DROPS.length)));
      return RAKE_DROPS[i];
    } catch (e) { say('rake drop roll failed (skipped): ' + ((e && e.message) || e)); return null; }
  }

  /** Progress toward this game's next promotion (the campus reads this too). */
  function progressFor(gameKey) {
    try { return tierProgress(store.gameMeta(gameKey)); }
    catch (e) { return tierProgress(null); }
  }

  /** Drop the one-more card. Idempotent; every screen change funnels here. */
  function dismissEndCard() {
    if (!endCard) return;
    const card = endCard;
    endCard = null;
    try { card.destroy(); } catch (e) { /* noop */ }
  }

  /**
   * THE ONE-MORE END CARD (house-rules Deck V + Deck VI).
   *
   * Retake is LIT, pulsing and pre-focused (Enter takes it); the way out is
   * small and dim but LIVE, honest and in a fixed slot that never moves. The
   * grade arrives as an OBJECT through the stamp ceremony - the shell never
   * prints the letter as a bare string first (Deck VI, "grades as objects").
   *
   * @param {Object} o
   * @param {Object} o.cls        the timetable class (what a retake replays)
   * @param {Object} o.graded     core/grades.js result {grade, zen, capped}
   * @param {boolean} o.isRetake  this run was already a replay
   * @param {Array=} o.capped     the A-caps the rubric raised
   */
  function showEndCard({ cls, graded, isRetake, capped }) {
    dismissEndCard();
    if (!dom || !dom.screen || !cls) return null;

    const gameKey = cls.gameKey;
    const root = el('div', 'arc-endcard');
    const card = el('div', 'arc-endcard-card');
    root.appendChild(card);

    card.appendChild(el('p', 'arc-kicker', t('rake_class_dismissed', 'Class dismissed')));

    /* --- the grade, as a thing --- */
    const gradeHost = el('div', 'arc-endcard-grade');
    card.appendChild(gradeHost);
    try {
      ceremonies.gradeObject({
        grade: graded && graded.grade,
        zen: !!(graded && graded.zen),
        target: gradeHost,
        hold: 600000,               // it is the card's subject, not a flash
      });
    } catch (e) { say('end card grade object failed: ' + ((e && e.message) || e)); }

    card.appendChild(el('h2', 'arc-h2', gameName(gameKey)));

    /* --- LOSSES DISGUISED: a C, or a dropped hard gate, still gets a beat --- */
    try {
      const kind = ceremonies.payoff({
        grade: graded && graded.grade,
        zen: !!(graded && graded.zen),
        capped,
        target: gradeHost,
      });
      say('end card payoff (' + gameKey + '): ' + kind);
    } catch (e) { say('end card payoff failed: ' + ((e && e.message) || e)); }

    /* --- THE SUNK-COST METER --- */
    const prog = progressFor(gameKey);
    const meter = el('div', 'arc-rake-meter');
    const fill = el('div', 'arc-rake-meter-fill');
    meter.appendChild(fill);
    try { meter.style.setProperty('--arc-p', prog.eased.toFixed(4)); } catch (e) { /* noop */ }
    meter.setAttribute('role', 'img');
    const meterText = prog.top
      ? t('rake_top_of_class', 'Top of the class')
      : String(t('rake_promo_progress', '{have} of {need} to {tier}'))
        .replace('{have}', String(prog.have))
        .replace('{need}', String(prog.need))
        .replace('{tier}', tierLabel(prog.nextTier));
    meter.setAttribute('aria-label', meterText);
    card.appendChild(meter);
    card.appendChild(el('p', 'arc-rake-meterlabel', meterText));

    /* --- THE DROP. Never on a retake (the day already paid), never endless. --- */
    const drop = isRetake ? null : rollRakeDrop(gameKey);
    if (drop) {
      const box = el('div', 'arc-endcard-drop');
      const stampHost = el('div', 'arc-endcard-dropstamp');
      box.appendChild(stampHost);
      try {
        ceremonies.stamp({
          text: t(drop.nameKey, drop.name),
          target: stampHost,
          hold: 600000,
        });
      } catch (e) { say('drop stamp failed: ' + ((e && e.message) || e)); }
      box.appendChild(el('p', 'arc-note', t(drop.lineKey, drop.line)));
      card.appendChild(box);
      say('rake drop minted for ' + gameKey + ': ' + drop.id + ' (no XP - C# owns that)');
    }

    /* --- THE BUTTONS. Order is fixed: the lit one first, then the two small
     *     dim ones in a row that never re-orders and never re-flows. --- */
    const actions = el('div', 'arc-endcard-actions');
    const again = el('button', 'btn primary arc-retake', t('retake', 'Retake'));
    again.type = 'button';
    // Pre-focused. `autofocus` is the declarative half (a fresh node inserted
    // into a live document does not always take focus() in every engine).
    again.setAttribute('autofocus', '');
    again.setAttribute('data-endcard-focus', '1');
    again.addEventListener('click', () => doRetake());
    actions.appendChild(again);

    /* THE EXITS. They keep their small footprint and their fixed row height -
     * the retake above is still the lit, pre-focused, pulsing one - but they no
     * longer hide: each wears the QUIET sign (an arrow and a glow, no bulbs, so
     * it cannot out-shout the retake) and the row is sticky, so a tall card on
     * a short window can never park them below the fold. Law VI says exits are
     * sacred; dim was a step too far and real players lost them. */
    const exits = el('div', 'arc-endcard-exits');
    // The report card is UNDER this overlay, so the way to it is one press -
    // the card may sit on top of the report, never in place of it.
    const toReport = el('button', 'btn ghost arc-endcard-small', t('report_card', 'Report Card'));
    toReport.type = 'button';
    toReport.addEventListener('click', () => dismissEndCard());
    signExit(toReport, { dir: 'back', quiet: true });
    exits.appendChild(toReport);

    const out = el('button', 'btn ghost arc-endcard-small', t('rake_back_to_campus', 'Back to campus'));
    out.type = 'button';
    out.addEventListener('click', () => { dismissEndCard(); showBoard(); });
    signExit(out, { dir: 'back', quiet: true });
    exits.appendChild(out);
    actions.appendChild(exits);
    card.appendChild(actions);

    /* --- the honest chip (trap 23). A retake is FREE, and the day keeps the
     *     grade it already recorded - the card says so instead of implying the
     *     replay is worth something it is not. --- */
    card.appendChild(el('p', 'arc-note arc-endcard-chip',
      t('rake_retake_chip', 'Free replay. It pays nothing, and today keeps your first grade.')));

    /** Enter = one more. Esc is NOT touched: it walks boot.js's ladder exactly
     *  as it did before this card existed. */
    function onKey(e) {
      if (!endCard || endCard.root !== root) return;
      if (active) return;                       // never over a live class/suspend
      const k = e && (e.key || e.code);
      if (k !== 'Enter' && k !== 'NumpadEnter') return;
      const tag = e && e.target && e.target.tagName ? String(e.target.tagName).toUpperCase() : '';
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
      try { e.preventDefault(); } catch (e2) { /* noop */ }
      doRetake();
    }

    /* ONE RETAKE PER CARD. A focused <button> answers Enter with a click of its
     * own, so the keydown path and the click path can BOTH fire from one press -
     * without this latch that press would start the class twice (the second
     * start tearing down the first mid-frame). */
    let taken = false;
    function doRetake() {
      if (taken) return;
      taken = true;
      dismissEndCard();
      // A free swim never reaches this card, so this is always a real class.
      try { startClass(cls); }
      catch (e) { say('retake failed to start: ' + ((e && e.message) || e)); showBoard(); }
    }

    if (typeof window !== 'undefined' && window.addEventListener) {
      window.addEventListener('keydown', onKey);
    }

    endCard = {
      root,
      gameKey,
      destroy() {
        if (typeof window !== 'undefined' && window.removeEventListener) {
          window.removeEventListener('keydown', onKey);
        }
        try { root.remove(); } catch (e) { /* noop */ }
      },
    };

    dom.screen.appendChild(root);
    try { if (typeof again.focus === 'function') again.focus(); } catch (e) { /* noop */ }
    return endCard;
  }

  /* ============================ THE CLASS RUNNER ======================== */
  /**
   * The classroom is the window: a viewport-pinned stage (game root fills it,
   * edge to edge) under a slim floating "proctor strip" that carries what the
   * old boxed class bar did. The strip ignores the pointer outside its own
   * children, and games keep critical interactives clear of the top ~56px.
   */
  function classScreenChrome(cls, gradeTier, retake, endless) {
    const panel = el('div', 'arc-classstage' + (endless ? ' arc-endless' : ''));

    /* THE TIME BAR. The FIRST child of the panel and the only thing above the
     * proctor strip: a 4px hairline of the class's own budget draining left to
     * right. The shell owns the clock (see timeBar* below) because a game must
     * never be able to make the flow of time lie - and it never ENDS a class:
     * the game's own bell is authoritative, the bar just goes pink and holds.
     * A FREE SWIM has no budget, so it has no bar (panel carries arc-endless). */
    let timebar = null;
    let timefill = null;
    if (!endless) {
      timebar = el('div', 'arc-timebar');
      timefill = el('div', 'arc-timebar-fill');
      timebar.appendChild(timefill);
      panel.appendChild(timebar);
    }

    const root = el('div', 'arc-classroot');
    panel.appendChild(root);

    const bar = el('div', 'arc-proctor');
    /* THE CAMPUS PILL (shell/exits.js). The way home, in the same corner of
     * every one of the ten classes, for as long as the class is up. It replaces
     * the old ghost "Leave class" button in the same slot on purpose: the strip
     * already reserves the top ~56px of the stage and every game already draws
     * clear of it, so the pill collides with nothing and costs no game a pixel.
     * CALM by design (no bulbs, no pulse - see the sign, which is for terminal
     * screens), and it asks before it takes anything: a class in progress is
     * work, and one stray click must not be able to bin it. */
    const pill = campusPill({
      label: t('arcademy', 'The Arcademy'),
      onActivate: () => askLeaveClass(),
    });
    bar.appendChild(pill);
    bar.appendChild(el('span', 'arc-title', gameName(cls.gameKey)));
    bar.appendChild(el('span', 'chip year', tierLabel(gradeTier)));
    bar.appendChild(el('span', 'chip', t('family_' + cls.family, cls.family)));
    if (retake) bar.appendChild(el('span', 'chip', t('retake', 'Retake')));
    bar.appendChild(el('span', 'arc-spacer'));
    // A free swim is untimed, so the clock chip would be a lie. The chip that
    // replaces it names what this is instead.
    const clock = endless
      ? el('span', 'chip', t('free_swim', 'Free Swim'))
      : el('span', 'chip num', cls.timeBudgetSec + 's');
    bar.appendChild(clock);
    panel.appendChild(bar);

    return { panel, root, clock, timebar, timefill, pill };
  }

  /* ---------------------- the leave-class confirm -----------------------
   * ONE question, asked from ONE place. The pill is the only caller today;
   * Esc's ladder is deliberately NOT routed through here (trap 29's corollary
   * keeps that walk byte-for-byte what it was), and neither are the pause card
   * or the suspend overlay, which already show what leaving costs.
   * -------------------------------------------------------------------- */

  /** Drop the confirm if it is up and put the class back the way it was.
   *  Idempotent; every path out funnels here (cancel, Esc, a host suspend,
   *  teardown), which is why the undo lives ON the dialog rather than in four
   *  copies at the call sites. */
  function dismissConfirm() {
    if (!active || !active.confirmEl) return false;
    try { active.confirmEl.close(); } catch (e) { /* noop */ }
    active.confirmEl = null;
    const restore = active.confirmRestore;
    active.confirmRestore = null;
    if (restore) { try { restore(); } catch (e) { /* noop */ } }
    return true;
  }

  /**
   * Ask, then go. The class freezes while the question is up (the clock, the
   * game and every effect all ride pauseClass), and CANCEL puts it back exactly
   * as it was - including leaving it paused if it already was.
   */
  function askLeaveClass() {
    if (!active || active.confirmEl) return;
    // A host suspend owns the screen and its overlay carries its own Leave
    // class button. A second, competing door on top of it would be the exact
    // ~60ms card-destroying race trap 29 was written about.
    if (active.suspendEl) return;

    const wasPaused = !!active.paused;
    if (!wasPaused) pauseClass(true);
    /* ONE CARD ON SCREEN AT A TIME. pauseClass mints the Paused overlay with its
     * own Resume / Leave class buttons, and two stacked cards asking two
     * different versions of the same question is worse than either alone (it
     * also reads as a bug). The pause card steps behind the hidden attribute
     * while the question is up and comes back if the player stays - and the
     * `[hidden]` reset at the top of styles.css is exactly what makes that work
     * on a display:flex node (trap 27). */
    if (active.pauseEl) active.pauseEl.hidden = true;

    /** Put the class back the way the pill found it. */
    const restore = () => {
      if (!active) return;
      active.confirmEl = null;
      active.confirmRestore = null;
      if (!wasPaused) pauseClass(false);
      else if (active.pauseEl) active.pauseEl.hidden = false;
    };

    const dialog = createConfirm({
      mount: active.root,
      title: t('leave_confirm_title', 'Head back to campus?'),
      body: t('leave_confirm_body',
        'This class is not finished. Nothing from it is saved.'),
      confirmLabel: t('back_to_campus', 'Back to campus'),
      cancelLabel: t('leave_confirm_stay', 'Stay in class'),
      // What leaving actually costs, in the HOST's own attendance numbers.
      // Unknown numbers print nothing at all - never invent jeopardy.
      note: jeopardyLine(),
      onConfirm: () => {
        // No toast on the way out: the card's own note has already said what
        // this costs, and saying it twice reads as a scold.
        if (active) { active.confirmEl = null; active.confirmRestore = null; }
        showBoard();
      },
      onCancel: restore,
    });
    if (!dialog) { restore(); return; }
    active.confirmEl = dialog;
    active.confirmRestore = restore;
  }

  /* ---------------------- the class clock (S1) --------------------------
   * PAUSE-AWARE AND SHELL-OWNED. It starts only once instance.start() has come
   * back without throwing, banks elapsed ms across every pause (the pause card,
   * the settings screen, a host suspend - all of them funnel through
   * pauseClass), and its interval dies in teardownClass with everything else.
   * Nothing is exposed to games: ctx has no handle on any of this.
   * -------------------------------------------------------------------- */
  function timeBarPaint() {
    if (!active || !active.timebar) return;
    const budgetMs = Math.max(0, (Number(active.timeBudgetSec) || 0) * 1000);
    const frac = budgetMs > 0 ? Math.min(1, active.clockElapsedMs / budgetMs) : 0;
    try { active.timebar.style.setProperty('--arc-t', frac.toFixed(4)); }
    catch (e) { /* noop */ }
    const cl = active.timebar.classList;
    if (!cl) return;
    // The last tenth is GOLD and flows faster; at (and past) the budget the bar
    // is PINK and held - the class is over as far as the clock is concerned and
    // the game decides what that means.
    try {
      if (frac >= 1) { cl.remove('warn'); cl.add('over'); }
      else if (frac >= 0.9) { cl.remove('over'); cl.add('warn'); }
      else { cl.remove('warn'); cl.remove('over'); }
    } catch (e) { /* noop */ }
  }

  function timeBarTick() {
    if (!active || !active.timebar || !active.clockRunning) return;
    const at = nowMs();
    const from = active.clockLastAt;
    active.clockLastAt = at;
    if (Number.isFinite(from)) active.clockElapsedMs += Math.max(0, at - from);
    timeBarPaint();
    // Held at full: stop burning a timer for a bar that can no longer move.
    if (!active.clockDone && active.timeBudgetSec > 0
      && active.clockElapsedMs >= active.timeBudgetSec * 1000) {
      active.clockDone = true;
      timeBarStopTimer();
    }
  }

  function timeBarStopTimer() {
    if (!active || !active.clockTimer) return;
    try { clearInterval(active.clockTimer); } catch (e) { /* noop */ }
    active.clockTimer = 0;
  }

  /** @param {boolean} on  true = running, false = paused. Idempotent both ways. */
  function timeBarSet(on) {
    if (!active || !active.timebar) return;
    const run = !!on;
    // Only a class that reached a clean start() ever arms the clock, so no
    // resume path (returning from settings, lifting a pause) can start a bar
    // draining over a class that never began.
    if (run && !active.clockArmed) return;
    if (active.clockRunning === run) return;
    if (run) {
      active.clockRunning = true;
      active.clockLastAt = nowMs();
      if (!active.clockDone && !active.clockTimer && typeof setInterval === 'function') {
        active.clockTimer = setInterval(timeBarTick, CLOCK_TICK_MS);
      }
    } else {
      timeBarTick();                 // bank the ms up to this instant first
      active.clockRunning = false;
      timeBarStopTimer();
    }
    timeBarPaint();
  }

  /** Re-show the running class's DOM (returning from settings). */
  function showClassScreen() {
    if (!active) { showBoard(); return; }
    screen = 'class';
    clearScreen();
    renderTopbar();
    dom.screen.appendChild(active.panel);
    setStage('arc-class-on');
    pauseClass(false);
  }

  /** The allowlisted, lifecycle-free engine handle one class gets. */
  function engineHandleFor(engine, manifest, gameKey) {
    const allowed = new Set(Array.isArray(manifest && manifest.effectsConsumed)
      ? manifest.effectsConsumed : []);
    const warned = new Set();
    const refuse = (verb, kind) => {
      const id = verb + ':' + kind;
      if (!warned.has(id)) {
        warned.add(id);
        say('engine: ' + gameKey + ' called ' + verb + '("' + kind
          + '") outside its manifest - refused');
      }
      return false;
    };
    const guarded = (verb, fn) => (kind, opts) => {
      if (!allowed.has(kind)) return refuse(verb, kind);
      try { return fn(kind, opts); }
      catch (e) { say('engine.' + verb + '(' + kind + ') threw: ' + ((e && e.message) || e)); return false; }
    };
    const safe = (name) => (...args) => {
      try { return engine[name] ? engine[name].apply(engine, args) : undefined; }
      catch (e) { say('engine.' + name + ' threw: ' + ((e && e.message) || e)); return undefined; }
    };
    return {
      setHeat: safe('setHeat'),
      fire: guarded('fire', (k, o) => engine.fire(k, o)),
      sustain: guarded('sustain', (k, o) => engine.sustain(k, o)),
      stop: safe('stop'),
      setpiece: safe('setpiece'),
      beat: safe('beat'),
      ceremony: safe('ceremony'),

      /* The engine's additive helpers (engine/index.js header). None of them is
       * kind-addressed, so the manifest allowlist has nothing to say about them -
       * they only READ the clamped state or advance a director the class already
       * drives through beat()/setpiece(). Exposed for consistency: every game
       * currently reimplements a local fallback for the ones it wants, and the
       * next one should not have to.
       *   setPhase(phaseKeyOrIndex)      eligiblePhases + anti-clump bookkeeping
       *   armTail(on)                    arm forceTail for the class's last beat
       *   rewardRoll(opts) -> outcome    the variable-ratio canon (schedule.js)
       *   isPlainBeat(i01, floor, early) rolls the plain-share on the seeded rng
       *   plainShare(i01, floor, early)  the .80 -> .30 ramp itself
       *   cadenceMs(kind) -> ms          heat-scaled cadence (Infinity = silent)
       *   channels() -> {…}              the CLAMPED channel vector (a copy)
       *   diagnostics() -> {…}           live node counts, setpieces, refusals
       * A null engine answers undefined for all of them, which is why every game
       * keeps its own fallback: presence is not a promise of an effect. */
      setPhase: safe('setPhase'),
      armTail: safe('armTail'),
      rewardRoll: safe('rewardRoll'),
      isPlainBeat: safe('isPlainBeat'),
      plainShare: safe('plainShare'),
      cadenceMs: safe('cadenceMs'),
      channels: safe('channels'),
      diagnostics: safe('diagnostics'),
      // suspend()/dispose() are deliberately absent: the shell owns lifecycle.
    };
  }

  /**
   * @param {Object} cls   a timetable class (or the synthetic one freeSwimClass
   *   builds for a room that is not on tonight's board)
   * @param {{endless?:boolean}=} opts  endless = a FREE SWIM: untimed, ungraded,
   *   outside the timetable. See startFreeSwim and finishClass.
   */
  function startClass(cls, opts) {
    if (suspendedGlobally) { shout(t('class_suspended', 'Class Suspended')); return; }
    dismissEndCard();
    dismissPunchStage();
    if (active) teardownClass();

    const endless = !!(opts && opts.endless);
    const entry = games.byKey[cls.gameKey]
      || { key: cls.gameKey, ok: false, mod: suspendedStub(cls.gameKey, 'not in this build') };
    const mod = entry.mod;
    const manifest = (mod && mod.manifest) || {};
    const gameMeta = store.gameMeta(cls.gameKey);
    const gradeTier = tierFor(gameMeta);
    // A free swim has NO budget: 0 is the contract the game reads as "no bell".
    const timeBudgetSec = endless ? 0 : Math.min(
      cls.timeBudgetSec || QUICK_MAX_SEC,
      cls.meaty ? MEATY_MAX_SEC : QUICK_MAX_SEC
    );
    // The timed seed is the DAY's script (same seed on a retake, on purpose).
    // A free swim is not the day's script, so it counts its own swims instead -
    // every free swim of a game deals a board nobody has seen.
    const seed = endless
      ? utcDateSeed + '|' + cls.gameKey + '|free|' + (Number(gameMeta && gameMeta.swims) || 0)
      : utcDateSeed + '|' + cls.gameKey + '|t' + gradeTier;
    // RETAKE: this class already has today's row. The seed is unchanged on
    // purpose (the day's script IS the day's script), so a game with an
    // identical-script replay needs nothing from this flag; it is here so a
    // game that wants to dress a replay differently can, and so the chrome can
    // say what is happening. A free swim is never a retake: it writes no row.
    const retake = endless ? false : !!todaysRecord(cls.gameKey);
    /* THE ONCE-EVER ENROLLMENT (PUNCHCARD §4). A free swim never enrolls: it
     * sends no `class-ended`, so it could never pay the ceremony off, and an
     * intro that led nowhere would be the worst version of this. */
    const enrolling = !endless && needsEnrollment(cls.gameKey);

    screen = 'class';
    clearScreen();
    renderTopbar();
    fireMoment('classStart', { gameKey: cls.gameKey, tier: gradeTier });   // EMI SEAM
    const chrome = classScreenChrome(
      Object.assign({}, cls, { timeBudgetSec }), gradeTier, retake, endless);

    /* --- engine for this class --- */
    /* ONE spiral per class, picked off the class seed and then held: the
     * engine asks per wash, and a fresh answer every time would swap the image
     * mid-fade. Never awaited, never preloaded here - the browser fetches it
     * when the first spiral wash actually mounts, and the CSS conic gradient
     * is still the fallback if it 404s. */
    const classSpiral = pickSpiralUrl(seed, src.settings);
    /* The whole pool, frozen, for a class that needs to OFFER spirals rather
     * than only wear one (Instant Recall's SPIRAL question). Same rows, same
     * order, same weights the pick walks - see spiralPoolRows. */
    const spiralPool = Object.freeze(spiralPoolRows(src.settings)
      .map((r) => Object.freeze({ url: r.url, weight: r.weight })));
    let engine;
    try {
      engine = createEngine({
        mount: (dom && dom.fx) || chrome.root,
        caps: src.caps || {},
        masterIntensity: src.masterIntensity == null ? 1 : src.masterIntensity,
        effectIntensity: src.effectIntensity == null ? 0.85 : src.effectIntensity,
        rng: makeRng(seed + '|engine'),
        // The DAY pool, not init.words: a word absorbed by an earlier class this
        // session is in here (ctx.absorb), and createEngine copies at construction.
        words: dayWords.slice(),
        assets,
        motionLevel: src.motionLevel == null ? 2 : src.motionLevel,
        reducedMotion,
        // a real spiral behind wash('spiral') instead of the conic fallback
        spiralUrl: () => classSpiral,
        // No extra bus: the engine already emits its CustomEvents on `document`,
        // and boot.js listens there. Passing window as well would double every
        // arcademy-log line (engine/index.js emit() fires document AND bus).
        bus: null,
      }) || NULL_ENGINE_FACTORY();
    } catch (e) {
      say('createEngine threw (' + ((e && e.message) || e) + ') - class runs undistracted');
      engine = NULL_ENGINE_FACTORY();
    }

    /* --- shared verbs --- */
    keybinds.declare(cls.gameKey, manifest.keybinds);
    const keys = keybinds.runtime(cls.gameKey, window);
    const peek = createPeek({ log: say });
    const classCeremonies = createCeremonies({
      engine, layer: (dom && dom.ceremony) || null, reducedMotion, log: say,
    });

    /* --- per-game settings view (never a global) --- */
    const settingsView = (settingsPage
      ? settingsPage.gameSettingsFor(cls.gameKey, manifest)
      : gameSettingsFallback(cls.gameKey, manifest));

    /* --- below-par board detection (SYNTHESIS #7, shell-computed) --- */
    let belowPar = false;
    const bs = manifest.boardSizes;
    if (bs && bs.par && settingsView.boardSize != null) {
      const par = Number(bs.par[gradeTier]);
      const chosen = Number(settingsView.boardSize);
      if (Number.isFinite(par) && Number.isFinite(chosen) && chosen < par) belowPar = true;
    }

    let ended = false;

    const ctx = {
      root: chrome.root,
      engine: engineHandleFor(engine, manifest, cls.gameKey),
      assets,
      lexicon: t,
      caps: src.caps || {},
      rng: makeRng(seed),
      settings: settingsView,
      keys,
      peek,
      ceremonies: classCeremonies,
      store,

      /* ---- additive read-only projection (all from init) ----------------
       * A class that needs to know the shape of the machine it is running on
       * should not have to re-probe the browser for it: the host already told
       * us, and its answer outranks a media query (`performanceMode` and the
       * app's own motion policy have no CSS equivalent). */
      platform: src.platform || { isTouch: false, hasHaptics: false, host: 'desktop' },
      motion: {
        reducedMotion,
        motionLevel: src.motionLevel == null ? 2 : src.motionLevel,
      },
      // Resolved SubAudioAudible: FALSE means a cue will be MIXED but inaudible,
      // so a class that leans on audio has to carry a visual tell instead.
      audioAudible: !!src.audioAudible,
      // The "Skip class tutorials" switch. The settings hint is the contract a
      // game must honour: even with this ON, a class still explains itself once
      // per grade tier - the once-per-tier memory is the game's own
      // (store.gameMeta), never the shell's.
      hideTutorial: !!src.hideTutorial,

      /* The day's word pool, and the sink that adds to it. `words` is a COPY
       * (a game may not splice the shared array); `absorb` is the only way in
       * and is SESSION-ONLY - never persisted, never sent to the host, and
       * never a write into SubliminalPool. A word taken here reaches LATER
       * classes because their engines are built from the same day pool. */
      words: dayWords.slice(),
      absorb: absorbWord,
      sessionWords,
      // The phrases WITH their whisper clips (frozen rows; see dayTriggers).
      triggers: dayTriggers.slice(),
      // The class's spiral POOL (frozen `{url, weight}` rows) beside the one
      // spiral the engine wears. A class that never reads it is unaffected.
      spiralPool,

      /* ---- THE EXITS (shell/exits.js) ------------------------------------
       * A shell PRIMITIVE, like peek and ceremonies: a class never mints its
       * own way out, it borrows the school's. Two calls, and both are pure
       * decoration - neither wires a handler, neither can move a screen.
       *
       *   exits.sign(btn, {dir:'back'|'go', quiet})  dress a button as the lit
       *       arrow board. TERMINAL SCREENS ONLY (a class-rules sheet, a
       *       debrief) - a sign on a live board would fight the board.
       *   exits.bar([nodes], {card:true})            a sticky footer row, so a
       *       card that scrolls can never park its own dismiss below the fold.
       *
       * The CSS is the shell's (styles.css, the EXITS section), so a class gets
       * the look for free and ten classes cannot drift apart. */
      exits: {
        sign: (btn, o) => { try { return signExit(btn, o); } catch (e) { return btn; } },
        bar: (children, o) => {
          const b = exitBar(children, o);
          if (o && o.card) b.className += ' arc-exitbar-card';
          return b;
        },
      },

      log: (m) => say('[' + cls.gameKey + '] ' + m),
      endClass: (result) => {
        if (ended) { say('[' + cls.gameKey + '] endClass called twice - ignored'); return; }
        ended = true;
        // `enrolling` is decided at the DOOR, not at the bell: the intro that
        // ran is the one this finish pays for, so a card the host enrolled
        // mid-class (it cannot, but a snapshot could) can never turn the
        // three-punch ceremony into a one-punch one halfway through.
        finishClass(cls, gradeTier, result, { peek, belowPar, endless, enrolling });
      },
    };

    let instance = null;
    try {
      instance = mod.create(ctx);
      if (!instance || typeof instance.start !== 'function') throw new Error('create() returned no start()');
    } catch (e) {
      say('game ' + cls.gameKey + ' create() failed: ' + ((e && e.message) || e));
      instance = suspendedStub(cls.gameKey, 'create failed').create(ctx);
    }

    active = {
      cls, gradeTier, instance, engine, keys, peek, ceremonies: classCeremonies,
      panel: chrome.panel, root: chrome.root, paused: false, pauseEl: null,
      suspendEl: null, confirmEl: null, confirmRestore: null, enrollEl: null,
      pill: chrome.pill, timeBudgetSec, endless,
      // the shell's own class clock (S1) - see timeBar* above
      timebar: chrome.timebar, timefill: chrome.timefill,
      clockTimer: 0, clockElapsedMs: 0, clockLastAt: NaN,
      clockRunning: false, clockDone: false, clockArmed: false,
    };

    dom.screen.appendChild(chrome.panel);
    setStage('arc-class-on');
    // The host only flips _classActive off this frame and ignores fields it does
    // not know, so `endless` is free to carry: a free swim opens the same
    // bracket and closes it with `class-left` from teardownClass (never with
    // `class-ended`, which is what would credit attendance and pay XP).
    bridge.send(endless
      ? { type: 'class-started', gameKey: cls.gameKey, gradeTier, endless: true }
      : { type: 'class-started', gameKey: cls.gameKey, gradeTier });

    /* THE GAME STARTS HERE, AND NOT BEFORE. Everything above is the stage; this
     * is the class. Enrollment (below) delays it by a few cards on a first run
     * and nothing else about it moves - the game's own howto sheet is still the
     * very next thing the player sees, untouched. */
    function beginPlay() {
      // The stage can be gone by the time an intro is dismissed (Esc, a panic
      // suspend, a leave) - starting a game into a torn-down class would leave
      // an orphan running behind the board.
      if (!active || active.instance !== instance) return;
      let started = false;
      try {
        instance.start(endless
          ? { gradeTier, seed, timeBudgetSec: 0, retake: false, endless: true }
          : { gradeTier, seed, timeBudgetSec, retake });
        started = true;
      } catch (e) {
        say('game ' + cls.gameKey + ' start() threw: ' + ((e && e.message) || e));
        showSuspendedOverlay('This class could not start. Your attendance is safe.');
      }
      // The clock runs only for a class that actually started: a bar draining over
      // a dead class would be the most confident lie the shell could tell.
      if (started) { active.clockArmed = true; timeBarSet(true); }
      if (suspendedGlobally) applySuspend(true, 'video');
    }

    if (enrolling) {
      /* Mounted on the class ROOT, not over the whole stage: the proctor strip
       * (and the campus pill on it) stays live, so the way out is exactly where
       * it is in every other class. A class with no copy of its own gets no
       * intro and starts immediately - createEnrollmentIntro answers null. */
      active.enrollEl = createEnrollmentIntro({
        mount: chrome.root,
        gameKey: cls.gameKey,
        name: gameName(cls.gameKey),
        hideTutorial: !!src.hideTutorial,
        reducedMotion,
        onDone: () => { if (active) active.enrollEl = null; beginPlay(); },
        log: say,
      });
      if (!active.enrollEl) beginPlay();
    } else {
      beginPlay();
    }
  }

  /* ---------------------- free swim (S2) --------------------------------
   * ENDLESS PLAY LIVES OUTSIDE THE TIMETABLE. A game may declare
   * `manifest.endless = { label_key, hint_key }`; the campus door card then
   * carries a second, secondary button that runs the game with no budget, no
   * grade and no row. A game that declares nothing shows no button, which is
   * why every reader here is defensive rather than assumed.
   * -------------------------------------------------------------------- */
  /**
   * @returns {?{labelKey:string, hintKey:string}} the game's endless
   *   declaration, or null. A class that failed to load (entry.ok false, so the
   *   suspended stub is in its slot) can never offer one.
   */
  function endlessFor(gameKey) {
    const entry = games.byKey[gameKey];
    if (!entry || !entry.ok) return null;
    const m = entry.mod && entry.mod.manifest;
    const e = m && m.endless;
    if (!e || typeof e !== 'object') return null;
    return {
      labelKey: typeof e.label_key === 'string' ? e.label_key : '',
      hintKey: typeof e.hint_key === 'string' ? e.hint_key : '',
    };
  }

  /** gameKey -> endless declaration, for every game that has one. */
  function endlessMap() {
    const out = Object.create(null);
    for (const entry of games.list) {
      const e = endlessFor(entry.key);
      if (e) out[entry.key] = e;
    }
    return out;
  }

  /**
   * The class-shaped object a free swim runs on. A room can be free-swum when it
   * is NOT on tonight's board, so the timetable cannot always supply one: fall
   * back to the registry descriptor (the same parachute the suspended stub
   * flies under).
   */
  function freeSwimClass(gameKey) {
    const onBoard = timetable.classes.find((c) => c.gameKey === gameKey);
    if (onBoard) return Object.assign({}, onBoard, { timeBudgetSec: 0 });
    const d = descriptors(games.list).find((x) => x.key === gameKey)
      || { key: gameKey, family: 'comfort', meaty: false };
    return {
      gameKey,
      family: d.family || 'comfort',
      meaty: !!d.meaty,
      homeroom: false,
      timeLabel: '',
      timeBudgetSec: 0,
    };
  }

  function startFreeSwim(gameKey) {
    if (suspendedGlobally) { shout(t('class_suspended', 'Class Suspended')); return; }
    if (!endlessFor(gameKey)) {
      say('free swim refused: ' + gameKey + ' declares no manifest.endless');
      return;
    }
    startClass(freeSwimClass(gameKey), { endless: true });
  }

  /** Fallback per-game settings when the settings page has not been built yet. */
  function gameSettingsFallback(gameKey, manifest) {
    const bag = (src.settings && typeof src.settings === 'object') ? src.settings : {};
    const out = {};
    const pick = (key, dflt) => (
      Object.prototype.hasOwnProperty.call(bag, key) ? bag[key] : dflt
    );
    for (const s of (Array.isArray(manifest.settings) ? manifest.settings : [])) {
      if (s && s.key) out[s.key] = pick(s.key, s.default);
    }
    if (manifest.boardSizes && Array.isArray(manifest.boardSizes.values) && manifest.boardSizes.values.length) {
      const bk = boardSizeKey(gameKey);
      out[bk] = pick(bk, manifest.boardSizes.values[0]);
      out.boardSize = out[bk];
    }
    return out;
  }

  /* ---------------------- end of class ---------------------------------- */
  function finishClass(cls, gradeTier, result, shellState) {
    const r = result || {};

    /* A FREE SWIM IS NOT A CLASS. Untimed comfort play outside the timetable
     * earns nothing and costs nothing: no grade, no results row, no day row, no
     * `class-ended` frame (which is what mints attendance and pays XP host-side)
     * and no promotion. The only thing it writes is its own counter, which is
     * also the seed's swim number, so the next swim deals a new board.
     * BOTH sides are consulted on purpose: the shell knows how it launched the
     * class, so a game that forgets the flag can never accidentally forge a
     * graded attendance out of a swim. */
    if (r.endless === true || (shellState && shellState.endless === true)) {
      try {
        const meta = store.gameMeta(cls.gameKey) || {};
        store.mergeGameMeta(cls.gameKey, { swims: (Number(meta.swims) || 0) + 1 });
      } catch (e) { say('free swim meta write failed (screen unaffected): ' + ((e && e.message) || e)); }
      say('free swim ended: ' + cls.gameKey + ' - ungraded by design, nothing recorded');
      // The game already showed its own end card before it called endClass.
      teardownClass();
      showBoard();
      return;
    }

    const assists = Object.assign({}, r.assists || {});
    if (shellState.peek && shellState.peek.used) assists.peek = true;
    if (shellState.belowPar) assists.below_par_board = true;

    const input = {
      metrics: r.metrics || {},
      hardGates: r.hardGates,
      zen: !!r.zen,
      assists,
    };
    const graded = gradeClass(input);
    // The A-caps the rubric RAISED, whether or not they moved the letter. The
    // report card already prints these; the end card's payoff reads the same
    // list so a dropped hard gate still gets its scaled-down beat even when the
    // grade was under the cap anyway.
    const capsList = capsRaised(input);
    const flavorXp = Math.max(0, Math.min(FLAVOR_XP_CAP, Math.round(Number(r.flavorXp) || 0)));

    // THE FIRST GRADE OF THE DAY IS THE ONE THAT COUNTS. A retake is a free
    // replay: it re-runs, re-stamps and re-shares, but it does not overwrite the
    // row - otherwise a bad second attempt would erase an S, and the host has
    // already paid the day's XP for the first (payout-result carries retake:true
    // and xp 0, which is what the report card's XP line then shows).
    const priorRow = todaysRecord(cls.gameKey);
    const isRetake = !!priorRow;

    if (!isRetake) {
      results[cls.gameKey] = {
        grade: graded.grade,
        zen: graded.zen,
        composite: graded.composite,
        capped: capsList,
        tier: gradeTier,
        xp: null,                   // filled by payout-result; C# owns the table
      };
    } else {
      // Keep the recorded grade; note the replay so the card can explain the 0.
      const keep = results[cls.gameKey] || {
        grade: priorRow.grade, zen: !!priorRow.zen, composite: 0, capped: [], tier: gradeTier,
      };
      keep.retake = true;
      keep.xp = null;
      results[cls.gameKey] = keep;
    }
    // The share card always reflects what you just played - it is a transcript,
    // not a record, and a retake you are proud of is the one you want to paste.
    if (r.share && typeof r.share === 'object') shares[cls.gameKey] = r.share;

    say('class ended: ' + cls.gameKey + ' tier ' + gradeTier + ' -> ' + graded.grade
      + ' (composite ' + graded.composite.toFixed(3)
      + (graded.capped.length ? ', capped: ' + graded.capped.join(',') : '')
      + (isRetake ? ', RETAKE - the day keeps ' + String(priorRow.grade).toUpperCase() : '') + ')');

    /* EMI SEAM: she reacts to the letter. The report card lands one screen later
     * and its SAY is suppressed for this finish (see showReport) so the two beats
     * do not talk over each other. */
    lastGraded = { grade: graded.grade, perfect: allDone(), fresh: true };
    fireMoment(/^[sab]$/i.test(String(graded.grade)) || graded.grade === 'pass' ? 'win' : 'miss',
      { grade: graded.grade, streak: store.streak().count });

    bridge.send({
      type: 'class-ended',
      gameKey: cls.gameKey,
      gradeTier,
      grade: graded.grade,
      zen: graded.zen,
      flavorXp,
      dayUtc: utcDateSeed,
    });

    /* meta writes: per-game progression, the day's row, the streak */
    try {
      // Retakes advance nothing: a free replay that still fed `promotions`
      // would let grinding replays climb tiers (and tomorrow's XP base) for
      // free. The first attempt of the day is the only one that progresses.
      if (!isRetake) {
        const adv = advance(store.gameMeta(cls.gameKey), graded.grade);
        store.mergeGameMeta(cls.gameKey, {
          tier: adv.tier, promotions: adv.promotions, played: adv.played,
          best: adv.best, lastGrade: adv.lastGrade,
        });
        if (adv.promoted) shout(tierLabel(adv.tier) + ' unlocked');
      }
      // Not on a retake: the day's row is written once, by the first attempt.
      if (!isRetake) {
        store.recordClass(localDate, cls.gameKey, {
          grade: graded.grade, zen: graded.zen, at: Date.now(),
        });
      }
      // ATTENDANCE IS NOT WRITTEN HERE. ArcademyMetaStore mints the streak and
      // perfect-attendance count from this very `class-ended` frame (so a stale
      // page cannot forge one) and ships the numbers back on `payout-result`;
      // store.applyPayout folds them in. Writing them locally would be refused
      // by the host anyway - see core/store.js HOST_OWNED_KEYS.
      if (allDone()) {
        store.completeDay(localDate, { classCount: timetable.classes.length, perfect: true });
      }
    } catch (e) {
      say('meta write failed (screen unaffected): ' + ((e && e.message) || e));
    }

    teardownClass();
    showReport();
    // DECK V: the one-more card lands ON the report card - after `class-ended`,
    // over the transcript, never in place of it. A free swim returned above and
    // never gets here, which is also why a swim can never mint a drop.
    try {
      showEndCard({ cls, graded, isRetake, capped: capsList });
    } catch (e) { say('end card failed (the report card still stands): ' + ((e && e.message) || e)); }

    /* THE PUNCH CARD, over both of them (PUNCHCARD §4). It is the last thing
     * mounted and the first thing dismissed, so the report and the one-more
     * card are exactly where they were when it goes. */
    try {
      if (shellState && shellState.enrolling) {
        /* FIRST RUN. The host has already taken today's daily stamp off this
         * very `class-ended`; the THREE enrollment punches SUPERSEDE it (owner
         * ruling 2026-08-23), and the frame that does so is the one we post at
         * the end of the ceremony. The daily beat for this run is suppressed by
         * arming 'enrollment'. */
        armPunch(cls.gameKey, 'enrollment');
        runPunchCeremony({
          gameKey: cls.gameKey,
          reason: 'enrollment',
          card: store.punchCard(cls.gameKey),
          minted: ENROLL_PUNCHES,
          from: 0,
          to: ENROLL_PUNCHES,
          onPunched: () => {
            // ONCE, AFTER THE CEREMONY (§4). Repeats are host-side no-ops, but
            // the shell does not lean on that: the card it just drew is the one
            // this frame pays for, and it is sent exactly once per first run.
            bridge.send({ type: 'enrollment-done', gameKey: cls.gameKey });
            say('enrollment-done posted for ' + cls.gameKey);
          },
        });
      } else {
        armPunch(cls.gameKey, 'daily');
      }
    } catch (e) { say('punch card ceremony failed (nothing else moved): ' + ((e && e.message) || e)); }
  }

  /* ---------------------- pause / suspend / teardown -------------------- */
  function pauseClass(on) {
    if (!active) return;
    active.paused = !!on;
    // THE ONE FUNNEL every freeze walks through - the pause card, the settings
    // screen and applySuspend(true) all land here, so the class clock only has
    // to be hooked once. (applySuspend(false) deliberately leaves the class
    // paused behind its Resume button, and that button comes back through here.)
    timeBarSet(!on);
    try { on ? active.instance.pause() : active.instance.resume(); }
    catch (e) { say('game pause/resume threw: ' + ((e && e.message) || e)); }
    if (on) {
      try { active.peek.forceHide(); } catch (e) { /* noop */ }
      if (!active.pauseEl) {
        const overlay = el('div', 'arc-suspended');
        overlay.appendChild(el('h2', 'arc-h2', 'Paused'));
        const bar = el('div', 'arc-classbar');
        const resume = el('button', 'btn primary', 'Resume');
        resume.type = 'button';
        resume.addEventListener('click', () => pauseClass(false));
        const leave = el('button', 'btn ghost', t('leave_class', 'Leave class'));
        leave.type = 'button';
        leave.addEventListener('click', () => showBoard());
        bar.appendChild(resume); bar.appendChild(leave);
        overlay.appendChild(bar);
        /* DECK V - STREAK JEOPARDY. What "Leave class" actually costs, in the
         * HOST's own attendance numbers. It is a LINE, not a gate: the button
         * beside it is unchanged, un-delayed and still the first thing under
         * your hand. Unknown numbers print nothing at all. */
        const jeopardy = jeopardyLine();
        if (jeopardy) {
          overlay.appendChild(el('p', 'arc-note arc-jeopardy', jeopardy));
        }
        overlay.appendChild(el('p', 'arc-note', 'Hold Esc to leave the Arcademy.'));
        active.root.appendChild(overlay);
        active.pauseEl = overlay;
      }
    } else if (active.pauseEl) {
      active.pauseEl.remove();
      active.pauseEl = null;
    }
  }

  function showSuspendedOverlay(note, opts) {
    if (!active || active.suspendEl) return;
    const overlay = el('div', 'arc-suspended');
    overlay.appendChild(el('h2', 'arc-h2', t('class_suspended', 'Class Suspended')));
    if (note) overlay.appendChild(el('p', 'arc-note', note));
    const bar = el('div', 'arc-classbar');
    // THE PANIC RUNG'S WAY BACK. A video suspend un-suspends itself when the video
    // ends, and an audio-only suspend when the session does - but a PANIC suspend
    // has no natural end, so the page has to ask for it. The host owns the answer
    // (it replies with suspend:false, reason 'panic'), which keeps the one law that
    // the host is the only thing that may un-freeze a class.
    if (opts && opts.resumable) {
      const resume = el('button', 'btn primary', 'Resume');   // literal, like pauseClass's
      resume.type = 'button';
      resume.addEventListener('click', () => {
        try { bridge.send({ type: 'resume-request', reason: 'panic' }); }
        catch (e) { say('resume-request send failed: ' + ((e && e.message) || e)); }
      });
      bar.appendChild(resume);
    }
    const leave = el('button', 'btn ghost', t('leave_class', 'Leave class'));
    leave.type = 'button';
    leave.addEventListener('click', () => showBoard());
    bar.appendChild(leave);
    overlay.appendChild(bar);
    active.root.appendChild(overlay);
    active.suspendEl = overlay;
  }

  /**
   * The host says everything must stop NOW (mandatory video, audio-only session
   * starting mid-class, panic). Freeze the class, drop every effect, and show the
   * class_suspended treatment. Attendance for the day is preserved either way -
   * the streak has already been rolled or will be when a class actually ends.
   */
  function applySuspend(on, reason) {
    suspendedGlobally = !!on;
    fireMoment(on ? 'suspend' : 'resume', { reason });   // EMI SEAM
    if (active) {
      try { active.engine.suspend(!!on); } catch (e) { say('engine.suspend threw: ' + ((e && e.message) || e)); }
      try { active.peek.forceHide(); } catch (e) { /* noop */ }
      try { active.instance.suspend(!!on); } catch (e) { say('game.suspend threw: ' + ((e && e.message) || e)); }
      // A LIFTED SUSPEND MUST NOT UN-PAUSE THE GAME. The class stays behind its
      // pause card (see below) but a game's suspend(false) typically restarts its
      // own loop - so re-assert the pause the card still shows (Misdirection and
      // the Deep End both ran on behind the overlay before this line).
      if (!on && active.paused) {
        try { active.instance.pause(); } catch (e) { say('game.pause threw: ' + ((e && e.message) || e)); }
      }
      if (on) {
        // THE HOST'S WORD BEATS THE QUESTION. A suspend overlay carries its own
        // Leave class button, so a confirm still sitting under it would be a
        // second, conflicting door - drop it and retire the pill until the
        // class is the player's again.
        dismissConfirm();
        if (active.pill) active.pill.disabled = true;
        pauseClass(true);
        showSuspendedOverlay(reason === 'audio-only'
          ? 'An audio-only session started. Your attendance is safe.'
          : reason === 'panic' ? 'Stopped. Press the panic key again to leave.' : 'Paused for a video.',
          { resumable: reason === 'panic' });
      } else if (active.suspendEl) {
        active.suspendEl.remove();
        active.suspendEl = null;
        if (active.pill) active.pill.disabled = false;
      }
    }
    renderTopbar();
    if (screen === 'board') showBoard({ silent: true });
  }

  function teardownClass() {
    if (!active) return;
    // The question dies with the class it was asked about - a dialog that
    // outlived its stage would be a second, invisible Esc rung.
    dismissConfirm();
    const a = active;
    // The intro dies with the stage it was mounted on. Closing it here (rather
    // than letting the panel removal take it) keeps its onDone from starting a
    // game into a class that no longer exists.
    if (a.enrollEl) { const intro = a.enrollEl; a.enrollEl = null; try { intro.close(); } catch (e) { /* noop */ } }
    // NO RAW TIMER OUTLIVES A CLASS. The class clock is the shell's only
    // interval per class and it dies here, before `active` is dropped - a tick
    // that fired afterwards would paint a bar that is no longer on the page.
    if (a.clockTimer) { try { clearInterval(a.clockTimer); } catch (e) { /* noop */ } a.clockTimer = 0; }
    a.clockRunning = false;
    active = null;
    // TELL THE HOST THE CLASS IS OVER. `class-started` has a closing bracket now:
    // without it the host's `_classActive` stayed true for the rest of the session
    // once you left a class with Esc (only `class-ended` ever cleared it), which
    // tightened the heartbeat watchdog to its mid-class 12s limit forever and made
    // the log lie about where the page was. Idempotent host-side, and deliberately
    // sent from the ONE funnel every leave path already goes through - including
    // the finished-class path, where `class-ended` has just cleared it anyway.
    try { bridge.send({ type: 'class-left', gameKey: a.cls && a.cls.gameKey }); }
    catch (e) { say('class-left send failed: ' + ((e && e.message) || e)); }
    try { a.instance.destroy(); } catch (e) { say('game destroy threw: ' + ((e && e.message) || e)); }
    // Cut any trigger clip still playing (Echo's pads): shell/audio.js owns the
    // voices, so this rides the sfx bus as its one control message.
    try { document.dispatchEvent(new CustomEvent('arcademy-sfx', { detail: { name: 'stop_clips' } })); } catch (e) { /* no DOM double */ }
    try { a.peek.destroy(); } catch (e) { /* noop */ }
    try { a.keys.destroy(); } catch (e) { /* noop */ }
    try { a.ceremonies.destroy(); } catch (e) { /* noop */ }
    try { a.engine.dispose(); } catch (e) { say('engine dispose threw: ' + ((e && e.message) || e)); }
    try { a.panel.remove(); } catch (e) { /* noop */ }
    if (dom && dom.fx) dom.fx.textContent = '';
  }

  /* ---------------------- palette --------------------------------------- */
  function applyPalette(palette, sayFn) {
    if (!palette || typeof palette !== 'object' || !document.documentElement) return;
    const style = document.documentElement.style;
    const unknown = [];
    for (const key of Object.keys(palette)) {
      const token = PALETTE_TOKENS[key];
      const value = palette[key];
      if (!token) { unknown.push(key); continue; }
      if (typeof value === 'string' && /^#[0-9a-fA-F]{3,8}$/.test(value.trim())) {
        style.setProperty(token, value.trim());
      }
    }
    if (unknown.length) sayFn('palette: ignored unknown keys ' + unknown.join(','));
  }

  /* ---------------------- host frames ----------------------------------- */
  const api = {
    /** {type:'setting'} post-clamp echo. THE only path that moves a setting. */
    onSetting(m) {
      if (!m || typeof m.key !== 'string') return;
      // Keep the flat bag current so a class started later sees the new value.
      // Only per-game keys live in the bag; a global echo would shadow one.
      if (src.settings && typeof src.settings === 'object' && !isGlobalSettingKey(m.key)) {
        src.settings[m.key] = m.value;
      }
      if (settingsPage) { settingsPage.noteEcho(m.key, m.value); settingsPage.applyEcho(m.key, m.value); }
      if (m.key === SETTING_KEYS.keybinds) keybinds.applyEcho(m.value);
    },

    /** {type:'payout-result'} - the ONLY source of an XP number on this page. */
    onPayout(m) {
      if (!m || !m.gameKey) return;
      const r = results[m.gameKey];
      if (r) { r.xp = m.xp; r.levelUp = !!m.levelUp; }
      // The same frame carries the host's authoritative attendance figures.
      try { store.applyPayout(m); } catch (e) { say('applyPayout: ' + ((e && e.message) || e)); }
      if (m.levelUp) shout('Level up');
      renderTopbar();
      if (screen === 'report') showReport();
    },

    /**
     * {type:'punchcard-result'} - the host's same-frame truth about a card
     * (PUNCHCARD §2/§4). It arrives on BOTH mint paths, minted or not, so the
     * shell can tell "nothing was punched" apart from "the host never answered".
     *
     * `card.punches` is the POST-mint total: the ceremony animates TO it and
     * never increments a counter of its own, and `justUnlocked` is the ONLY
     * signal that this finish was the tenth hole (a snapshot could only say so
     * by diffing, which is exactly the guess this frame exists to avoid).
     */
    onPunchCard(m) {
      if (!m || typeof m.gameKey !== 'string') return;
      const card = m.card && typeof m.card === 'object' ? m.card : null;
      const norm = card ? Object.assign({}, card, {
        // The frame carries the host's raw card; give the ceremony the same
        // derived flag every other reader sees.
        enrolled: typeof card.enrolledAt === 'string' && !!card.enrolledAt,
      }) : null;

      // A ceremony already on screen reconciles - it may have been animating on
      // its own clock (the enrollment path) or drawing a stale number.
      if (punchStage && punchStage.gameKey === m.gameKey && norm) {
        try { punchStage.reconcile(norm, !!m.justUnlocked); }
        catch (e) { say('punch reconcile: ' + ((e && e.message) || e)); }
      }

      if (!punchArm || punchArm.gameKey !== m.gameKey) return;

      if (punchArm.mode === 'enrollment') {
        // The three-punch beat is already running and has just reconciled above;
        // the daily frame for this same finish is the one enrollment supersedes,
        // so it is deliberately ignored rather than played as a second ceremony.
        if (m.reason === 'enrollment') disarmPunch();
        return;
      }

      if (m.reason !== 'daily') return;
      disarmPunch();
      // A NO-OP MINT GETS NO CEREMONY. Same local day twice, a full card, a
      // class the host declined to stamp: nothing was punched, so nothing is
      // shown. A card beat for a hole that did not happen is the one lie this
      // screen must never tell.
      if (!m.minted || !norm) return;
      try {
        runPunchCeremony({
          gameKey: m.gameKey,
          reason: 'daily',
          card: norm,
          // `minted` is a COUNT now, not a flag: 2 on a day the class graded S,
          // 1 otherwise. The ceremony walks that many beats; the shell still
          // grades nothing and counts nothing of its own.
          minted: Math.round(Number(m.minted) || 1),
          to: Math.round(Number(norm.punches) || 0),
          justUnlocked: !!m.justUnlocked,
        });
      } catch (e) { say('punch ceremony failed: ' + ((e && e.message) || e)); }
    },

    /** {type:'suspend'} */
    onSuspend(m) { applySuspend(!!(m && m.on), m && m.reason); },

    /** {type:'meta'} is consumed by the store; this just repaints the chrome. */
    onMeta() { renderTopbar(); if (screen === 'board') showBoard({ silent: true }); },

    /**
     * One rung of the Esc ladder. Returns true when the shell consumed it, so
     * boot.js only reaches for fullscreen/exit once the page has nothing to close.
     */
    escapeStep() {
      // THE CONFIRM IS THE INNERMOST RUNG. It is a modal the player opened one
      // press ago, so Esc closing it first is the only answer that is not a
      // surprise - and it is the ONE rung this wave added. Everything below is
      // byte-for-byte the ladder it always was (trap 29's corollary).
      // dismissConfirm carries the undo: it unfreezes a class the pill froze,
      // and hands a class that was ALREADY paused its pause card back.
      if (active && active.confirmEl) { dismissConfirm(); return true; }
      /* THE CARD CEREMONY IS A TERMINAL OVERLAY over the report card, and it
       * can only be up when there is no class at all - so it cannot race the
       * suspend rung below and it closes the way any full-screen card closes.
       * The report (and the one-more card) are underneath, untouched. */
      if (punchStage) { dismissPunchStage(); return true; }
      /* THE ENROLLMENT INTRO. The class has not started yet, so Esc means "get
       * on with it" rather than "leave" - the campus pill on the strip is still
       * the way out and has not moved. Skipping starts the class, which is what
       * every other dismissal of this overlay does too. */
      if (active && active.enrollEl) {
        const intro = active.enrollEl;
        active.enrollEl = null;
        try { intro.skip(); } catch (e) { /* noop */ }
        return true;
      }
      // The campus door card is the outermost thing a tap can close.
      if (screen === 'board' && campus) {
        try { if (campus.closeCard()) return true; } catch (e) { /* noop */ }
      }
      if (screen === 'settings') {
        if (settingsPage) { try { settingsPage.destroy(); } catch (e) { /* noop */ } settingsPage = null; }
        if (active) showClassScreen(); else showBoard();
        return true;
      }
      // The Records Office is a screen like settings: Esc walks back to campus.
      if (screen === 'records') { showBoard(); return true; }
      // A host suspend owns the screen. The panic ladder's second press (host
      // side) is the exit, and walking to the board here would destroy the
      // Resume card ~60ms after it appeared (both ladders fire on one Esc -
      // verified live 3/3). Consume the press and change nothing; the overlay's
      // own Resume / Leave class buttons remain the page-side way out.
      if (active && active.suspendEl) return true;
      if (active && !active.paused) { pauseClass(true); return true; }
      if (active && active.paused) {
        // DECK V: say what it costs on the way past, then go. The rung itself
        // is unchanged - same condition, same showBoard(), same `true`.
        announceJeopardy();
        showBoard();
        return true;
      }
      return false;
    },

    get screen() { return screen; },
    get inClass() { return !!active; },

    /* ---------------------- DECK V, read-only seams ---------------------- */
    /** Cross-class progress toward a game's next tier promotion (the sunk-cost
     *  meter's numbers). `{tier, nextTier, have, need, frac, eased, top}` -
     *  `eased` is the fill, `have`/`need` are the honest label. The campus door
     *  card reads the same function through its `progressFor` option. */
    progressFor,
    /** What leaving right now would cost, or null when the shell does not know.
     *  Sourced ONLY from the host-owned attendance numbers in the store. */
    jeopardy() { return jeopardyLine(); },
    /** The one-more card, while it is up (null otherwise). Test seam. */
    get endCard() { return endCard; },

    /** Read-only provider diagnostics (local/remote pool sizes, placeholderFloor).
     *  The one window onto the asset seam: `placeholderFloor:true` means the host
     *  shipped no `settings.localAssets` and every draw is a bundled tile. */
    assetStats() { try { return assets.stats(); } catch (e) { return null; } },

    destroy() {
      if (destroyed) return;
      destroyed = true;
      dismissEndCard();
      dismissPunchStage();
      disarmPunch();
      teardownClass();
      setStage(null);
      if (campus) { try { campus.destroy(); } catch (e) { /* noop */ } campus = null; }
      try { ceremonies.destroy(); } catch (e) { /* noop */ }
      if (settingsPage) { try { settingsPage.destroy(); } catch (e) { /* noop */ } }
    },
  };

  store.onChange(() => { if (screen === 'board' || screen === 'report') renderTopbar(); });

  /* EMI. Mounted before the first board so the opening `greet` has a face to
   * wear. The layer is optional on purpose: `dom.emi` if the host passed one,
   * else `#arc-emi` from the document, else nothing at all (the node DOM double
   * registers no ids, so every suite runs EMI-less and unchanged). */
  try {
    const emiLayer = (dom && dom.emi)
      || (typeof document !== 'undefined' && document.getElementById
        ? document.getElementById('arc-emi') : null);
    // `shout` is boot.js's toast (#arc-toast). EMI borrows it for exactly one
    // line - the first time the player ever dismisses her with the x.
    if (emiLayer) mountEmi({ layer: emiLayer, store, toast: shout, log: say });
  } catch (e) { say('EMI failed to mount (the shell is unaffected): ' + ((e && e.message) || e)); }

  showBoard();
  return api;
}

export default createShell;
