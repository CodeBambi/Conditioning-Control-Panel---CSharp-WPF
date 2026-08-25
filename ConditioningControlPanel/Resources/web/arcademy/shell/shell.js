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
import { createCampus, campusState, currentSemester } from './campus.js';
import { createGhosts, presenceOptions } from './ghosts.js';
import { createWalker, roomStop } from './walk.js';
import { createOrientation, needsOrientation, orientationOptions } from './orientation.js';
import { createReportCard } from './reportcard.js';
import { createSettingsPage, boardSizeKey, SETTING_KEYS, isGlobalSettingKey, PRESENCE_RUNGS } from './settings.js';
import { createCeremonies } from './ceremonies.js';
import { createPeek } from './peek.js';
import { createKeybinds } from './keybinds.js';
import { campusPill, createConfirm, exitBar, sign as signExit } from './exits.js';
import { installDeviceClass, isMobile } from '../core/device.js';
import { requireOrientation, clearOrientation } from './orientgate.js';
import { createEnrollmentIntro, createPunchCeremony } from './enrollment.js';
/* FIRST BELL - the once-ever opening (vn/). It mints its own layer, owns its own
 * ledger (`vnSeen`, a sibling of EMI's `emiVoice`) and NEVER gates a shipped
 * seam: every entry point takes a continuation and runs it exactly once - on
 * success, on a throw, on a missing plate and on a watchdog. A returning
 * player's whole experience of this import is one controller that stands down. */
import { createFirstBell } from '../vn/index.js';
import { createRecordsRoom } from './recordsroom.js';
import { createIdSpotlight, idReducedMotion } from './idcard.js';
import { createAnnexReveal } from './annexreveal.js';
/* THE SEEP - the foreshadowing layer. ONE director, and the shell's whole
 * relationship with it is: build it, hand it read-only seams and three gate
 * answers, tell it when a class ended, tear it down. It writes nothing, it
 * posts nothing, it mints no meta key and it adds no rung to the Esc ladder. */
import { createSeep } from './seep.js';
/* THE LAB ITSELF (ANNEX-OS.md). A screen, not an overlay: the room, the paper
 * props and the fake OS all live in annex/lab.js behind narrow caps - the
 * shell keeps the store, the gate, EMI's bracket and the bridge, records.js
 * style. */
import { createAnnexLab } from '../annex/lab.js';
/* THE ROOM SCENE (the VN antechamber). A painted room between the campus door
 * and a class, replacing the door card for the rooms that have art - room.js
 * owns the SCENES table and the stage; the shell keeps the walk, the launch
 * path and the Esc rung (narrow caps, records.js style). */
import { createRoomScene, hasRoomScene, prefetchRoomArt } from './room.js';
/* THE PHANTOM POST: the mail engine and its three paper overlays. The engines
 * hold NO storage of their own (their STATE-NEEDS law) - the shell hands each
 * an injected blob below and banks it through the store like any page key. */
import { initMail, triggerHolds } from './mail.js';
import { openMailbox, closeMailbox, isMailboxOpen } from './mailbox.js';
import { initCorkboard, openCorkboard, currentCorkboard } from './corkboard.js';
import { initBugle, openBugle, currentBugle } from './bugle.js';
import { loadFaceGeometry, ENROLL_PUNCHES } from './punchcard.js';
/* EMI, the mascot. Two of B's own modules: `mountEmi` builds the floating widget
 * (which dynamic-imports agent A's renderer optionally, so a broken face costs
 * EMI's expression and never the shell's boot) and `fireMoment` is the ONE verb
 * every seam below uses. Both are no-ops when there is no `#arc-emi` layer -
 * which is exactly the case in the node DOM double. */
import { mountEmi, getEmi, getVoice } from '../emi/index.js';
import { fireMoment } from '../emi/moments.js';

const FLAVOR_XP_CAP = 15;          // BUILD-CONTRACT §8 - the page clamps too
const MEATY_MAX_SEC = 300;
const QUICK_MAX_SEC = 180;
/** How often the shell's own class clock repaints the time bar. */
const CLOCK_TICK_MS = 250;

/* ----------------------------------------------------------------------------
 * THE SHELL'S OWN VOICE (AV Club, 2026-08-24). Two beats and nothing more: the
 * swap between screens, and the first bell of the night. shell/audio.js owns the
 * only audio node on the page (trap 18) - this is a REQUEST on `document`, the
 * same shape shell/ceremonies.js sfx() and shell/punchcard.js thud() use, and a
 * dropped cue is not an error.
 * -------------------------------------------------------------------------- */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/** Screen depth, so a swap knows which way it went. An ORDER, not a router -
 *  the router is `screen` and it stays exactly where it was. */
const SCREEN_DEPTH = Object.freeze({ board: 0, room: 1, records: 1, annex: 2, report: 2, settings: 3, class: 4 });

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
  /* THE SEEP'S CLASS-SIDE DOOR. Null is the answer every caller already handles
   * (it is the answer seven times in eight on a live engine too), so a class
   * that runs undistracted simply never sees a tell. */
  deadBeat() { return null; },
});

/* A provider that failed to load must still answer every verb a class can call,
 * or "the media agent's module threw" becomes "the class threw". The tagged half
 * (SORT's door) answers an EMPTY pool rather than null: `empty('target')` is
 * true, which is exactly the state the door already knows how to refuse. */
const NULL_ASSETS_FACTORY = () => ({
  claim() { return Promise.resolve({ next() { return null; }, release() {} }); },
  claimTagged() {
    return Promise.resolve({
      next() { return null; },
      counts() { return { target: { distinct: 0, served: 0 }, noise: { distinct: 0, served: 0 } }; },
      thin() { return true; },
      empty() { return true; },
      prewarm() { return 0; },
      dealt() { return []; },
      onUpdate() { return () => {}; },
      stats() { return { resolved: true, tags: [] }; },
      dispose() {},
    });
  },
  catalog() {
    return {
      remoteCatalog: [], subLibrary: [], localFolders: [], assetPresets: [],
      remoteConsent: false, remoteMediaEnabled: false, offlineMode: true, mediaSource: '',
    };
  },
  probeSub(name) {
    return Promise.resolve({ name: String(name || ''), ok: false, videoCount: 0, stillOnly: false, offline: true });
  },
  removeLibrarySub() { return Promise.resolve(); },
  onLibrary() { return () => {}; },
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
  let reducedMotion = !!src.reducedMotion || src.motionLevel === 0;
  if (reducedMotion && document.documentElement) {
    document.documentElement.classList.add('arc-reduced');
  }
  /** THE WEB'S MOTION CONTROL. The settings page's 'This device' sheet posts
   *  `motionLevel` (0 off / 1 reduced / 2 full) and the shim echoes it; this is
   *  the echo landing. The desktop never echoes the key (its Motion row is
   *  read-only, the app owns it), so on the app this is never reached. CSS
   *  rides html.arc-reduced at once; the JS consumers read `reducedMotion` at
   *  their next build (a class start, a screen change), which is the same
   *  contract every other setting on that page already makes. */
  function applyMotionLevel(level) {
    const n = Number(level);
    if (!Number.isFinite(n)) return;
    src.motionLevel = n;
    src.reducedMotion = n !== 2;
    reducedMotion = n === 0 || !!src.reducedMotion;
    const html = document.documentElement;
    if (html && html.classList) html.classList.toggle('arc-reduced', reducedMotion);
  }
  /* THE MOBILE SEAM (core/device.js). One decision, painted on <html> as
   * `arc-mobile` and kept there across a rotate, so the stylesheet's phone rules
   * and every `isMobile()` in the JS can never disagree. boot.js installs it too;
   * it is idempotent, and a shell driven straight by a test harness needs its own
   * call rather than boot's. */
  installDeviceClass();

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

  /* ------------------------- THE PHANTOM POST ------------------------------
   * The school's paper: mail, the noticeboard, the Bugle. Three PAGE-OWNED
   * store keys (`mail`, `board`, `bugle`), each an injected blob the modules
   * mutate in place and this shell banks - the modules never import the store.
   * Chrome mounts on the campus through the `post` bag (createCampus below);
   * the overlays open over the board and close on the Esc ladder like every
   * other modal. Reads and visits live inside the blobs themselves
   * (deliveredAt/readAt, visits, opens) - there is no separate counter key. */
  const postState = {
    mail: store.get('mail') || {},
    board: store.get('board') || {},
    bugle: store.get('bugle') || {},
  };
  /** What the postman may ask about tonight (mail.js CONTEXT contract).
   *  `days` keeps a 30-day window (store DAY_HISTORY_MAX), so `day` saturates
   *  there - a letter gating deeper than a month asks punches or streak. */
  function postCtx() {
    const cards = store.get('punchCards') || {};
    let punches = 0;
    for (const k of Object.keys(cards)) punches += Number(cards[k] && cards[k].punches) || 0;
    const d = new Date();
    const mm = String(d.getMonth() + 1);
    const dd = String(d.getDate());
    /* The Bugle's read ledger rides into the clause context so a letter can
     * answer the paper (issueRead / daysAfterIssueRead) - the one direction
     * the season chains across surfaces. Values are the local day strings the
     * Bugle banks; the clauses read both those and epoch ms. */
    const issuesReadAt = {};
    const issues = (postState.bugle && postState.bugle.issues) || {};
    for (const id of Object.keys(issues)) {
      if (issues[id] && issues[id].readAt) issuesReadAt[id] = issues[id].readAt;
    }
    return {
      day: Object.keys(store.get('days') || {}).length,
      punches,
      /* SEALED CARDS, not stamps: `punchesAtLeast` counts holes and this counts
       * finished cards. It is the number THE SEEP's ladder runs on, and Wet Ink
       * (corkboard n19) is the one notice gated on it - which is how a sheet of
       * paper goes up at the same moment the school starts getting thin without
       * the noticeboard ever hearing of the seep. Derived, never stored. */
      sealed: (() => {
        /* The whole read is guarded, and not only for a junk card: postCtx is
         * declared well above `games`, so a caller that somehow asked before the
         * registry resolved would meet a temporal-dead-zone throw. Nought sealed
         * is the honest answer for a school that has not opened yet. */
        try {
          let n = 0;
          for (const entry of games.list) {
            try { if (store.punchCard(entry.key).complete) n += 1; } catch (e) { /* noop */ }
          }
          return n;
        } catch (e) { return 0; }
      })(),
      streak: Number(store.get('streak')) || 0,
      dateIs: (mm.length < 2 ? '0' + mm : mm) + '-' + (dd.length < 2 ? '0' + dd : dd),
      seenFlags: { annex: !!store.get('annexRevealSeen') },
      issuesReadAt,
    };
  }
  const mail = initMail({
    ctx: postCtx,
    state: postState.mail,
    save: (s) => store.set('mail', s),
    log: say,
  });
  /* The one evaluator all three surfaces share: a `when` gate on a notice or
   * an issue is judged against the SAME facts the postman uses (the shell's
   * context plus the mailbox's own delivered/read ledger), so the season
   * cannot contradict itself between surfaces. */
  const postWhen = (trigger) => triggerHolds(trigger, mail.context(), say);
  initCorkboard({ state: postState.board, save: (s) => store.set('board', s), daySeed: utcDateSeed, log: say, when: postWhen });
  initBugle({ state: postState.bugle, save: (s) => store.set('bugle', s), log: say, when: postWhen });
  /** Repaint the campus post furniture after an overlay closes (fresh dots,
   *  unread pip) - a no-op anywhere but the board. */
  function refreshCampusPost() {
    if (screen === 'board' && campus) {
      try { campus.update(buildCampusState(), campusStats()); } catch (e) { /* noop */ }
    }
  }
  function openMailboxOverlay() {
    if (isMailboxOpen()) return;
    openMailbox({ mail, ownEscape: false, onClose: refreshCampusPost, log: say });
  }
  function openCorkboardOverlay() {
    const up = currentCorkboard();
    if (up && !up.closed) return;
    openCorkboard({ daySeed: utcDateSeed, onClose: refreshCampusPost, log: say });
  }
  function openBugleOverlay() {
    const up = currentBugle();
    if (up && !up.closed) return;
    openBugle(null, { onClose: refreshCampusPost, log: say });
  }

  /** gameKey -> {grade, zen, composite, capped, tier, xp, levelUp} for TODAY. */
  const results = Object.create(null);
  /** gameKey -> share payload handed to the one share pipeline. */
  const shares = Object.create(null);

  let screen = 'board';            // 'board' | 'class' | 'report' | 'settings' | 'records'
  /* THE OPENING DEAL IS AN ARRIVAL TOO. `screen` starts on 'board', so the very
   * first showBoard() reads as a REPAINT to the wasScreen guard below and the
   * opening greet never fired at all (field bug, 2026-08-24: two unattended
   * boots, an empty seen ledger and no introduction). This flag says "the board
   * has been arrived at once"; everything after it is the guard as it was. */
  let greeted = false;
  /* FIRST BELL. The opening's controller (vn/index.js) or null - null is the
   * whole story on a platform with no document, and a returning player's
   * controller is live but permanently stood down. `splashSpent` makes
   * onSplashDone idempotent: boot.js calls it once, but a second call must be
   * free rather than a second cold open. */
  let vn = null;
  let splashSpent = false;
  /* Has the first-night stage cleared? False until `onSplashDone` has run AND
   * FIRST BELL has finished (or was never armed). Orientation Day may not start
   * before both: the boot campus runs its 4.5s entry reveal UNDER the ~3s
   * splash, so `revealDone` lands ~1.5s after the splash falls - which on a
   * genuine first night is the middle of the cold open's first scene (measured
   * live; the beat played under the VN plates and was spent unseen). */
  let stageClear = false;
  let board = null;
  let campus = null;               // the night-campus hub (OPTIONAL - see showBoard)
  // THE STUDENT BODY (PRESENCE.md). Optional in exactly the way the campus is:
  // it hangs off the campus's own ghost layer, so it lives and dies with it and
  // no other screen has ever heard of it.
  let ghosts = null;
  /* THE WALK (ORIENTATION.md §2). The player miniature. Optional in exactly the
   * way the campus and the ghosts are - it hangs off the campus's own walker
   * layer, lives and dies with it, and no other screen has ever heard of it. */
  let walker = null;
  /* ORIENTATION DAY (ORIENTATION.md §3). The once-ever first-visit beat, and
   * optional in exactly the way the campus, the ghosts and the walker are: it
   * hangs off the campus, lives and dies with it, and a module that fails to
   * load or throws on build costs the beat and NOTHING else - the card is shown
   * immediately in that case (see the build below). */
  let orientation = null;
  /* WHERE YOU ARE STANDING, and it is SESSION-ONLY. null = the Main Gate, i.e.
   * you have just arrived at school; otherwise the room key you last walked
   * into, so coming back out of a class puts you at its door instead of
   * teleporting you to the gate. Nothing persisted, nothing posted - a reload
   * puts you back at the gate, which is diegetically correct (§2.2). */
  let lastRoomKey = null;
  /* TONIGHT'S RESIDUE: the finished traces, as DATA, so they survive a screen
   * change and are re-rendered on the next campus mount. FIFO-capped inside
   * walk.js (RESIDUE_MAX). Cleared at end-run with the rest of the shell. */
  let residueTrail = [];
  /* THE ONE DOOR ORIENTATION DAY STARTS THROUGH. Every seam that could be "the
   * moment the player can finally see the campus" funnels here - the campus's
   * revealDone hook, the build-time already-revealed check, `onSplashDone`'s
   * post-FIRST-BELL callback and the suspend lift - and the conditions are
   * checked in ONE place so no seam can rediscover the mid-VN bug on its own:
   * the stage must be clear (splash down, cold open done or never armed), the
   * campus must exist, be revealed and be the live screen, and nothing may be
   * suspended. `start()` is state-guarded, so N callers cost nothing. */
  const maybeStartOrientation = () => {
    if (!orientation || !stageClear || suspendedGlobally) return;
    if (screen !== 'board') return;
    if (!campus || typeof campus.revealDone !== 'function' || !campus.revealDone()) return;
    try { orientation.start(); } catch (e) { say('orientation start threw: ' + ((e && e.message) || e)); }
  };
  let extrasBox = null;            // replay/report bar + yesterday strip container
  let settingsPage = null;
  /** WHERE THE SETTINGS PAGE OWES ITS WAY BACK. Normally there is nothing to
   *  remember: the page came from the campus or a class and goes home to the
   *  one it came from. A caller with somewhere else in mind (the room scene's
   *  rail, which reopens the room it left) parks that route here and both
   *  exits - the page's own Back and the Esc rung - spend it exactly once. */
  let settingsBack = null;
  let reportCard = null;
  /** The Deck V one-more card. It sits OVER the report card, never instead of
   *  it (see showEndCard) - `{root, destroy}` while live, null otherwise. */
  let endCard = null;
  /** THE RECORDS OFFICE (shell/recordsroom.js). A painted ROOM since the 0825
   *  wave, not a page: built fresh per visit and destroyed on every path out
   *  through clearScreen, which is the room scene's lifecycle and it is not
   *  optional here - the scene chassis hangs its apron on <body>, so a cached
   *  handle would leave a band over the next screen. */
  let recordsRoom = null;
  /* ---------------------- THE STUDENT ID (shell/idcard.js) ----------------
   * The card in the corner of the campus is a document you can pick up now.
   * The shell owns three things about it and the card owns none of them: the
   * PROFILE (init.profile plus every `profile` frame the host pushes), the
   * SPOTLIGHT (minted on the first open, like the Records one) and the CHIP's
   * one verb - which posts a frame and then waits, because only the echo moves
   * a setting (trap 1).
   * `idChipWait` is the ONE optimistic paint: 'wait' while a Discord link-up is
   * in the air, 'pending' while a set-setting is waiting on its echo. Both are
   * cleared by the frame that answers, or by the 90s timeout below. */
  let idSpotlight = null;
  let idEmiPrev = false;
  let idChipWait = null;
  let idLinkTimer = 0;
  let profile = {
    name: null,
    avatarUrl: null,
    discordLinked: false,
    presenceShare: idRung(src.presenceShare, 'off'),
  };
  if (src.profile && typeof src.profile === 'object') {
    profile = {
      name: typeof src.profile.name === 'string' && src.profile.name ? src.profile.name : null,
      avatarUrl: typeof src.profile.avatarUrl === 'string' && src.profile.avatarUrl
        ? src.profile.avatarUrl : null,
      discordLinked: !!src.profile.discordLinked,
      presenceShare: idRung(src.profile.presenceShare, profile.presenceShare),
    };
  }
  /** THE LAB (ANNEX-OS.md). Built fresh per visit and destroyed on every path
   *  out - it runs a cam-wall rAF and holds EMI's bracket, neither of which
   *  may survive the screen. `annexEmiPrev` remembers whether EMI was enabled
   *  before the bracket so a settings-disabled EMI never comes back by accident. */
  let annexPage = null;
  let annexEmiPrev = false;
  /** THE ROOM SCENE (shell/room.js). Built fresh per visit, destroyed on every
   *  path out via clearScreen - the annex's lifecycle without the bracket. */
  let roomPage = null;
  /** The one in-flight annex stats request ({promise, resolve, timer}|null). */
  let annexStatsWait = null;
  /* THE PUNCH-CARD CEREMONY. `punchStage` is the live overlay; `punchArm` is
   * what the shell is WAITING for while the host answers `class-ended`.
   * Both are null the rest of the time. */
  let punchStage = null;
  /* THE ANNEX REVEAL (shell/annexreveal.js). One-shot cinematic: `annexStage`
   * is the live overlay, `annexProbe` the pending catch-up timer. Same
   * lifecycle laws as the punch ceremony: dismissed at every real screen
   * change, exactly one Esc rung, never a routed screen. */
  let annexStage = null;
  let annexProbe = 0;
  let punchArm = null;             // {gameKey, mode:'daily'|'enrollment', timer}
  /* THE SWAP CUE'S MEMORY. `screen` has already moved by the time clearScreen()
   * runs (every call site sets it first), so this is the other end of the swap -
   * and it starts on 'board' for the same reason `greeted` exists: the opening
   * deal is not a swap, it is the page arriving. */
  let lastScreenCued = 'board';
  /* FIRST BELL OF THE NIGHT. One 'bell' per boot, and it wants three things to
   * be true at once: the splash has landed, the campus's entry reveal has
   * finished, and the opening (vn/) has closed. The two latches below are set by
   * the campus's own revealDone hook and by `onSplashDone`'s continuation -
   * `splashDone` runs its callback EXACTLY once however the opening ended
   * (trap 76), which is why this never has to guess whether a scene is up. */
  let bellRung = false;
  let bellSplashCleared = false;
  let bellBoardRevealed = false;
  /* ...and the same two latches are what EMI has been waiting on. She is told
   * once, on the edge; see maybeFirstBell. */
  let introToldEmi = false;
  let active = null;               // the running class (see startClass)
  let suspendedGlobally = false;
  let destroyed = false;
  // Host opened the building through the dev switch (`--arcademy`). Unlocks
  // Begin on every campus door regardless of the seed; never true for players.
  const devPass = src.devDoor === true;
  /* THE ANNEX PEEK (web only, owner only). The web lobby stamps it for an
   * account on the server's ARCADEMY_ANNEX_PEEK_EMAILS allow-list arriving at
   * /arcademy?annex=1; the C# host has never sent the field and absent is
   * false, so the desktop cannot see this branch at all.
   *
   * IT IS A PEEK, NOT A REVEAL. It makes the lab REACHABLE - the campus hatch
   * and the office's ajar door - and touches nothing else. It never writes
   * `annexRevealSeen`, so maybeAnnexReveal below is untouched and the real
   * beat still waits for the tenth card and lands once, for real, later. It
   * deliberately does NOT reach `seenFlags.annex` either: that flag gates the
   * postman, and a delivered letter is persisted state a peek must not mint. */
  const annexPeek = src.devAnnex === true;
  if (annexPeek) say('ANNEX PEEK (owner dev): lab reachable, nothing stamped');

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
      /* THE PICKABLE WORLD (SORT's setup door). Same bag, but named through
       * rather than scraped: these four lists plus the two flags are what the
       * door draws its chips, pills, folders and presets from, and a host that
       * predates them ships nothing, which the provider reads as an empty
       * catalog (the door then offers QUICK SORT). See provider catalog(). */
      remoteCatalog: src.settings && src.settings.remoteCatalog,
      subLibrary: src.settings && src.settings.subLibrary,
      localFolders: src.settings && src.settings.localFolders,
      assetPresets: src.settings && src.settings.assetPresets,
      remoteConsent: !!(src.settings && src.settings.remoteConsent),
      mediaSource: (src.settings && src.settings.mediaSource) || '',
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

  /* ---------------------- THE SEEP ---------------------------------------
   * Built here because everything it needs already exists (the store, the
   * registry list, the day seed) and two things below it want it: the report
   * card asks it for the Other Stamp, and the split-flap board asks it for the
   * Misprint. It is OPTIONAL in exactly the way the campus and the ghosts are -
   * a director that fails to build costs the haunting and nothing else.
   *
   * THE SEAMS ARE GETTERS, NOT HANDLES (trap 73): the campus, the ghost layer
   * and the mascot are all torn down and rebuilt under it, and a handle captured
   * at mount is a handle that has gone by the time a tell fires.
   *
   * THE GATE IS THE SHELL'S OWN ANSWER to three questions the director cannot
   * see for itself. `busy` is the important one: it is every overlay, ceremony
   * and modal that owns the screen, and it is what stops a slip landing under
   * the annex reveal or over a punch-card beat. */
  let seep = null;
  try {
    seep = createSeep({
      store,
      games,
      utcDay: utcDateSeed,
      reducedMotion,
      log: say,
      seams: {
        campus: () => campus,
        ghosts: () => ghosts,
        emi: () => { try { return getEmi(); } catch (e) { return null; } },
      },
      gate: {
        campusVisible: () => screen === 'board' && !!campus && !active,
        classRunning: () => !!active,
        busy: () => {
          if (suspendedGlobally || destroyed) return true;
          if (punchStage || annexStage || annexProbe || endCard) return true;
          if (active && (active.suspendEl || active.paused)) return true;
          /* FIRST BELL is deliberately NOT a rung here. `vn.armed` means "there
           * are scenes this save has not seen", not "a scene is on screen", and
           * every place one actually plays is already covered: the cold open and
           * the walk sit inside the seep's two-minute quiet window, s03 runs out
           * of startClass (so `classRunning`), and m01 rides the punch ceremony
           * (so `punchStage`). A rung on `armed` would mute the whole first
           * night instead. */
          try { if (orientation && orientation.active()) return true; } catch (e) { /* noop */ }
          try { if (isMailboxOpen()) return true; } catch (e) { /* noop */ }
          try { const c = currentCorkboard(); if (c && !c.closed) return true; } catch (e) { /* noop */ }
          try { const b = currentBugle(); if (b && !b.closed) return true; } catch (e) { /* noop */ }
          /* A door card is a modal over the plan; a slip behind it is a slip
           * nobody sees. `seepSeam().cardIsOpen()` is a READ - `closeCard()`
           * would have closed it. */
          try {
            const cs = campus && campus.seepSeam && campus.seepSeam();
            if (cs && cs.cardIsOpen && cs.cardIsOpen()) return true;
          } catch (e) { /* noop */ }
          return false;
        },
      },
    });
  } catch (e) {
    say('the seep failed to build (' + ((e && e.message) || e) + ') - the school is simply quiet');
    seep = null;
  }
  /** THE ONE SUPPLIER the split-flap board asks at deal time. Null-safe both
   *  ways: no director, no misprint, and the board is what it always was. */
  const seepMisprint = (rows) => {
    try { return seep ? seep.misprintFor(rows) : null; } catch (e) { return null; }
  };

  reportCard = createReportCard({ ceremonies, seep, toast: shout, log: say });

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

  /** How many cards are MASTERED once `gameKey`'s tenth hole lands (EMI seam).
   *  Read off the store like every other card question; the shell counts
   *  nothing of its own, it only folds in the card the ceremony is drawing. */
  function masteredCount(gameKey) {
    const done = unlockedMap();
    if (gameKey) done[gameKey] = true;
    return Object.keys(done).length;
  }

  /** How many classes the player holds a card for, counting `gameKey` (EMI seam). */
  function enrolledCount(gameKey) {
    const out = Object.create(null);
    for (const entry of games.list) {
      try { if (store.punchCard(entry.key).enrolled) out[entry.key] = true; }
      catch (e) { /* a card that cannot be read is not an enrollment */ }
    }
    if (gameKey) out[gameKey] = true;
    return Object.keys(out).length;
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

  /**
   * HAS THIS SCHOOL EVER BEEN ATTENDED? The FIRST BELL opening is first-run only
   * (its law 3), and this is the whole test: not one card enrolled and not one
   * graded day on the books. Both are already-derived reads - `enrolledAt` is
   * the host's own enrollment flag and `days` is the page's graded view - so
   * nothing new is stored to answer it, and a player restored from the server
   * mirror reads as a returning player for free.
   * A throw answers FALSE, which stands the opening down: the safe answer to
   * "I cannot tell" is the one that shows nobody anything.
   */
  function isFirstNight() {
    try {
      for (const entry of games.list) if (store.punchCard(entry.key).enrolled) return false;
      const days = store.get('days');
      if (days && typeof days === 'object' && Object.keys(days).length) return false;
      return true;
    } catch (e) { return false; }
  }

  /** Is the intro splash still up? The loader owns the opening beat (trap 66)
   *  and its whole contract is `hidden`, so nothing in the chrome speaks under
   *  it. A page with no loader at all (the DOM double) answers false. */
  function splashUp() {
    try {
      if (typeof document === 'undefined' || !document.getElementById) return false;
      const l = document.getElementById('arc-loader');
      return !!(l && !l.hidden);
    } catch (e) { return false; }
  }

  /**
   * THE SCREEN-SWAP CUE. clearScreen() is the ONE funnel every screen change
   * goes through, and `screen` is already the destination when it runs, so this
   * is the only place on the page that knows both ends of a swap. Deeper is a
   * 'lift', shallower is a 'slide', and the same screen twice is a REPAINT
   * (showBoard({silent}), onPayout re-rendering the report) which says nothing -
   * the same suppression the EMI seams make one line above.
   */
  function screenCue() {
    const from = lastScreenCued;
    const to = screen;
    lastScreenCued = to;
    if (from === to || splashUp()) return;
    const a = SCREEN_DEPTH[from];
    const b = SCREEN_DEPTH[to];
    if (a == null || b == null) return;
    sfx(b > a ? 'lift' : 'slide', 0.3);
  }

  /** Ring it, once, when all three gates are open and the campus is the screen. */
  function maybeFirstBell() {
    if (bellRung || destroyed) return;
    if (!bellSplashCleared || !bellBoardRevealed) return;
    /* THE INTRO IS OVER, AND EMI IS THE ONE WHO NEEDS TELLING (owner playtest,
     * 2026-08-24: "the mascot is speaking underneath the intro"). Both latches
     * are set here and nowhere else, so this is the page's one honest edge for
     * it: the splash has landed AND the opening has closed. Her gate has been
     * holding the day-1 introduction all this time; this is the call it lands
     * on. Deliberately ABOVE the screen test - her introduction is owed to her
     * whatever screen the player wandered to while a scene played, and the bell
     * is not. Once only, and a mascot may never hold up the bell. */
    if (!introToldEmi) {
      introToldEmi = true;
      try { const mascot = getEmi(); if (mascot && mascot.introDone) mascot.introDone(); }
      catch (e) { say('EMI intro flush threw (ignored): ' + ((e && e.message) || e)); }
    }
    if (screen !== 'board' || active) return;
    bellRung = true;
    sfx('bell', 0.5);
  }

  function clearScreen() {
    screenCue();
    // The card ceremony is deliberately NOT dropped here. It rides ON the report
    // card, and `onPayout` re-renders that report on the same screen - a wipe
    // that took the ceremony with it would delete the card the instant the host
    // paid out, which is the exact moment it is meant to be up. It is dropped
    // beside dismissEndCard() at every real screen change instead, and
    // showReport() re-seats it after a repaint (same treatment, same reason).
    // The campus owns a 1s bell interval; a screen wipe must not orphan it.
    /* ORIENTATION GOES FIRST, WHILE THERE IS STILL A CAMPUS TO LAND IN. Its
     * destroy() is the abort path (§3.3): the student ID lands in its home slot
     * and `seenAt` is banked, and both of those need `campus.idCardEl()` to
     * still be attached. Tear it down after the campus and a player who clicked
     * a door mid-beat would own no card and be offered the beat again. */
    if (orientation) {
      const ob = orientation;
      orientation = null;
      try { ob.destroy(); } catch (e) { /* noop */ }
    }
    /* THE ID SPOTLIGHT DIES WITH ITS CAMPUS. It was lifted off a node that is
     * about to be destroyed, so a card left up would be holding a dead `from`
     * to hand focus back to - and EMI comes back on the same wipe (the annex
     * bracket's discipline). */
    dismissIdCard(true);
    if (ghosts) { try { ghosts.destroy(); } catch (e) { /* noop */ } ghosts = null; }
    if (campus) {
      try { campus.destroy(); } catch (e) { /* noop */ }
      campus = null;
      /* A campus that has gone is a VISIT that has ended: the once-per-visit
       * tells (the Slow Second) re-arm for the next one. */
      try { if (seep) seep.note('campusUnmount'); } catch (e) { /* noop */ }
    }
    /* THE WALKER GOES LAST AND ITS RESIDUE IS BANKED FIRST. `campus` is already
     * null by the time destroy() runs, which is what makes a pending onDone
     * safe: walk.js pays it (a launch is never silently swallowed), the funnel
     * below sees the campus gone and declines to deal a class into a screen
     * that has already moved on. Nulling `walker` before destroying it is the
     * other half - a re-entrant clearScreen finds nothing left to tear down. */
    if (walker) {
      const w = walker;
      walker = null;
      try { residueTrail = w.residue(); } catch (e) { /* noop */ }
      try { w.destroy(); } catch (e) { /* noop */ }
    }
    /* THE LAB DIES WITH ITS SCREEN. Every path out funnels through here, which
     * is what makes the EMI bracket safe: she comes back on the same wipe that
     * removes the room, and only to the state she held before it (a player who
     * keeps her off in settings never sees her flicker on). */
    if (annexPage) {
      const ap = annexPage;
      annexPage = null;
      try { ap.destroy(); } catch (e) { /* noop */ }
      if (annexEmiPrev) {
        annexEmiPrev = false;
        try { const emi = getEmi(); if (emi && emi.setEnabled) emi.setEnabled(true); } catch (e) { /* noop */ }
      }
    }
    /* The room scene dies with its screen too - no bracket to restore (a
     * classroom keeps its mascot), just the resize listener its destroy drops. */
    if (roomPage) {
      const rp = roomPage;
      roomPage = null;
      try { rp.destroy(); } catch (e) { /* noop */ }
    }
    /* ...AND SO DOES THE OFFICE, for one reason the class rooms only share by
     * accident: the scene chassis hangs its midway apron on <body>, outside
     * anything `dom.screen.textContent = ''` can reach. destroy() is the only
     * thing that takes that band off, so a records room that is not torn down
     * here leaves a slab across the bottom of whatever comes next. */
    if (recordsRoom) {
      const rr = recordsRoom;
      recordsRoom = null;
      try { rr.destroy(); } catch (e) { /* noop */ }
    }
    extrasBox = null;
    /* THE ROTATE GATE BELONGS TO A SCREEN, NOT TO THE PAGE. Every screen change
     * funnels through here, so dropping it here is what stops a gate the campus
     * asked for from hanging over the report card behind it. The screens that
     * still want one (the campus, a class whose board has a shape) re-arm after
     * they have built, which is also how the card knows which words to wear. */
    try { clearOrientation(); } catch (e) { /* noop */ }
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
      html.classList.remove('arc-class-on', 'arc-report-on', 'arc-settings-on');
      if (mode) html.classList.add(mode);
    }
    if (dom && dom.topbar) dom.topbar.hidden = !!mode;
  }

  /* ---------------------- the way home ----------------------------------
   * ONE VERB, and it routes rather than tearing anything down. A live class
   * walks its own leave-confirm (askLeaveClass, which ends in the ordinary
   * showBoard()); everything else is already one showBoard() from the gates.
   * Nothing here is a second teardown path, and nothing here touches the Esc
   * ladder - trap 29's corollary still owns every rung of that walk.
   * -------------------------------------------------------------------- */
  function backToCampus() {
    if (!active) { showBoard(); return; }
    /* The confirm mounts on the CLASS's own root, so the class has to be the
     * thing on screen before it can be asked about. The settings page is the
     * one way to be standing here with a live class parked off-screen, and
     * showClassScreen is the same re-seat its own Back button uses. */
    if (screen !== 'class') showClassScreen();
    askLeaveClass();
  }

  /* ---------------------- top bar --------------------------------------- */
  function renderTopbar() {
    if (!dom || !dom.topbar) return;
    const bar = dom.topbar;
    // Stay retired while the night campus is up: the scene carries the bar's
    // job diegetically (crest, ID card, bell, gear), and any store write mid-scene
    // (attendance tick, EMI's voice ledger) lands here via store.onChange - the
    // old unconditional unhide resurrected the bar OVER the campus and buried
    // the hanging timetable plaque under it (owner screenshot, 0824).
    bar.hidden = !!campus;
    bar.textContent = '';
    /* THE WORDMARK IS THE DOOR (owner ruling 2026-08-24). On the campus the
     * name is just the name - a back button pointing at the room you are
     * standing in is noise, and the hub wears its own crest in the scene. On
     * every other screen this bar is up for (the settings page, and the plain
     * board fallback's siblings) the same slot carries the campus pill, so the
     * school's name is the thing you press to get back to the school. It is
     * shell/exits.js's node, not a lookalike: one mint, one behaviour. */
    if (screen === 'board') {
      bar.appendChild(el('span', 'arc-title', t('arcademy', 'The Arcademy')));
    } else {
      bar.appendChild(campusPill({ onActivate: () => backToCampus() }));
    }
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
        /* A CLOCKLESS CLASS SHOWS NO SECONDS, ANYWHERE (the class-length wave).
         * The budget is still real and still rings the bell; Daily Trigger just
         * does not wear it. Same suppression on the campus room card and in the
         * proctor strip, and the time bar is not mounted at all. */
        if (!c.clockless) chips.push({ text: c.timeBudgetSec + 's', kind: 'num' });
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
      clockless: !!c.clockless,          // the room card hides the seconds chip
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

    /* No "flip the board again" button any more: the flaps roll every time the
     * hanging board is EXPANDED (the plaque is the flip - see boardToggle
     * below), and the plain fallback screen still rolls them on entry. */
    if (allDone()) {
      const bar = el('div', 'arc-classbar');
      const rc = el('button', 'btn', t('report_card', 'Report Card'));
      rc.type = 'button';
      rc.addEventListener('click', () => showReport());
      bar.appendChild(rc);
      extrasBox.appendChild(bar);
    }

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
    dismissAnnexStage();

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
    /* THE SEEP'S EAR, and this is the ONE hook the Cold Draft wants: walking
     * back out onto the campus from a class or a report card. You just fed the
     * thing downstairs; it exhales. The director owns the roll, the cooldown and
     * the two-minute quiet window - this line only says what happened. */
    if (wasScreen === 'class' || wasScreen === 'report') {
      try { if (seep) seep.note('classEnd', { from: wasScreen }); } catch (e) { /* noop */ }
    }
    // EMI SEAM: arriving at the campus, not repainting it - and the FIRST deal
    // of the night is an arrival (see `greeted`, above).
    if (!greeted || wasScreen !== 'board') {
      const firstArrival = !greeted;
      greeted = true;
      fireMoment('greet');
      if (firstArrival) noteStreakTurn();
      /* THE POSTMAN CALLS ON ARRIVAL, and only then: at most one letter lands
       * (mail.js law 2), the campus built below paints the chip already
       * knowing about it, and a silent repaint is not an arrival - walking in
       * and out of the settings page cannot stack the box. */
      try { mail.deliver(); } catch (e) { say('mail deliver threw: ' + ((e && e.message) || e)); }
    }
    /* THE MORNING-AFTER CATCH-UP: a save that sealed its last card before this
     * wave shipped (or whose final-seal ceremony was torn down by a host-forced
     * class before onDone could fire the beat) gets the reveal on its next
     * arrival at the campus: a breath after the board paints, once the loader
     * has landed. No-ops forever once seen - the flag stamps at mount. */
    if (!store.get('annexRevealSeen')) {
      if (annexProbe) clearTimeout(annexProbe);
      /* 5600ms: past the greet bubble's whole life (1560 lead + 3000 hold + a
       * breath) - the cut must never land on top of "oh! hi." (playtest 0824). */
      annexProbe = setTimeout(() => { annexProbe = 0; maybeAnnexReveal(); }, 5600);
    }
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
    /* Built composed, never flipping: the board is BEHIND the collapsed plaque
     * when the campus stands up, and the reveal cascade belongs to the moment
     * the player opens it (boardToggle below). The plain fallback screen keeps
     * its entry roll - it has no plaque. */
    board = createBoard({
      rows: buildRows(true),
      reducedMotion,
      animate: false,
      onSelect: onSelectRow,
      misprintFor: seepMisprint,
    });

    /* ========================= THE ONE WALK FUNNEL =======================
     * Every diegetic way into a room goes through here: `begin`, `freeSwim`,
     * the Records door and the Front Office DOOR. You walk there, and the
     * thing you were going to do happens when you arrive.
     *
     * Three laws, and the third is the one that matters:
     *   - the topbar gear is NOT in here. It is the shortcut; the Front Office
     *     room is the diegetic door (ORIENTATION §2.3, and campus.js routes the
     *     two apart through `registrarRoom`).
     *   - `lastRoomKey` is written HERE and only here, so "where you are
     *     standing" is exactly "the last door you actually walked through".
     *   - THE WALK IS DECORATION AND THE LAUNCH IS NOT. No walker, a walker
     *     that throws, an unknown room, a zero-length path - every one of them
     *     runs the action anyway. The single case that does NOT launch is a
     *     campus that was torn down mid-walk (a suspend, an end-run): the
     *     screen has moved on and dealing a class into it would be the bug.
     * ==================================================================== */
    const walkThen = (targetKey, action) => {
      const fire = () => { try { action(); } catch (e) { say('room action threw: ' + ((e && e.message) || e)); } };
      if (!walker) { lastRoomKey = targetKey; fire(); return; }
      let spent = false;
      const owner = campus;
      const go = () => {
        if (spent) return;
        spent = true;
        try { if (walker) residueTrail = walker.residue(); } catch (e) { /* noop */ }
        if (!campus || campus !== owner) {
          say('walk: the campus went away mid-walk - ' + targetKey + ' dropped');
          return;
        }
        lastRoomKey = targetKey;
        fire();
      };
      try { walker.walkTo(targetKey, { onDone: go }); }
      catch (e) { say('walk refused (' + ((e && e.message) || e) + ') - straight in'); go(); }
    };

    /* ==================== IS THIS THE FIRST NIGHT? =======================
     * ORIENTATION.md §3.1/§3.2. The decision is SYNCHRONOUS and taken HERE,
     * before the campus is built, because it changes how the campus is built:
     * a first-night student's ID card is withheld so the beat can hand it over,
     * and everybody else's is the furniture it has always been. Meta is already
     * local, so there is no async gap and no flash - and if orientation.js is
     * absent or throws below, `idCardMode` reverts to 'shown' the same turn
     * (the decoration law, and the card is never the thing that is lost).
     *
     * `?orientation=force` is the play-test path: it plays the beat over any
     * meta at all and deliberately writes nothing.
     * =================================================================== */
    let orientForced = false;
    try { orientForced = !!orientationOptions().force; } catch (e) { orientForced = false; }
    let wantsOrientation = false;
    try { wantsOrientation = orientForced || needsOrientation(store); }
    catch (e) { say('orientation gate threw: ' + ((e && e.message) || e)); wantsOrientation = false; }

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
        // The collapsed plaque's clock pulses until the timetable has been
        // opened once TODAY (local date - it is attendance furniture, not
        // content; regression #978's rule).
        boardPulse: store.get('boardOpenedDate') !== localDate,
        // ORIENTATION DAY §3.2: the card is WITHHELD on a first night so the
        // beat can hand over the very same node. One card object either way.
        idCardMode: wantsOrientation ? 'withheld' : 'shown',
        /* THE CHALK GHOST'S DOOR (tell 12). A GETTER, never a handle (trap 73):
         * the campus is torn down and rebuilt under the director, and a handle
         * captured at mount is a handle that has gone. The campus never imports
         * seep.js - it asks this and paints what it is told, exactly the way the
         * split-flap board takes `misprintFor`. */
        seep: () => seep,
        // ...and the idle attract may not deal a cursor over the handover (§4).
        // Asked at the ONE place attract decides to begin; a beat that is over
        // (or was never built) says false and attract arms exactly as before.
        holdAttract: () => {
          try { return !!(orientation && orientation.active()); } catch (e) { return false; }
        },
        /* THE PHANTOM POST furniture. Campus mounts the chip and the two props;
         * the shell keeps the engines, the overlays and every byte of state. */
        post: {
          openMail: openMailboxOverlay,
          mailUnread: () => mail.unreadCount(),
          mailTotal: () => mail.all().length,
          openBoard: openCorkboardOverlay,
          openBugle: openBugleOverlay,
          boardState: postState.board,
          bugleState: postState.bugle,
          daySeed: utcDateSeed,
        },
        /* THE ANNEX SHORTCUT (ANNEX-OS.md §1, the owner's entry ruling): the
         * hatch joins the plan only after the reveal AND a first visit through
         * the office panel - revisits skip the walk, discovery never does.
         * Same bag contract as `post`: campus draws, the shell keeps state. */
        annex: (annexPeek || (store.get('annexRevealSeen') && (store.get('annex') || {}).visited))
          ? { open: () => walkThen('annex', () => showAnnex()) }
          : null,
        on: {
          /* THE STUDENT BODY STARTS HERE AND NOWHERE ELSE (PRESENCE §4): after
           * the entry reveal, never during it. The campus fires this once; a
           * layer that failed to build simply never answers.
           * ORIENTATION DAY rides the SAME slot, after the ghosts - through
           * `maybeStartOrientation`, because this hook alone is NOT enough: the
           * boot campus runs this reveal UNDER the splash, and on a first night
           * FIRST BELL owns the screen after that. The funnel waits for all of
           * them; the post-VN start is paid by `onSplashDone`. */
          revealDone: () => {
            try { if (ghosts) ghosts.start(); } catch (e) { say('presence start threw: ' + ((e && e.message) || e)); }
            // FIRST BELL's second gate: the school has finished standing up.
            bellBoardRevealed = true;
            maybeFirstBell();
            maybeStartOrientation();
          },
          boardToggle: (expanded) => {
            if (!expanded) return;
            // Opening IS the flip: roll the flaps through, and remember the
            // open so tomorrow is the next time the clock nags.
            try {
              if (store.get('boardOpenedDate') !== localDate) store.set('boardOpenedDate', localDate);
            } catch (e) { say('boardOpenedDate write failed: ' + ((e && e.message) || e)); }
            if (board) board.replay();
          },
          begin: (gameKey) => walkThen(gameKey, () => launchGraded(gameKey)),
          /* THE ROOM SCENE TAKEOVER. campus.js offers every enterable door
           * here before it pops the card; the shell takes the ones with art
           * (room.js's SCENES table) and declines the rest with `false`, which
           * pops the card exactly as it always did. The walk is unchanged -
           * door first, THEN the room, because that is what a door is for. */
          roomScene: (gameKey, info) => {
            if (!hasRoomScene(gameKey)) return false;
            walkThen(gameKey, () => showRoomScene(gameKey, info));
            return true;
          },
          freeSwim: (gameKey) => walkThen(gameKey, () => startFreeSwim(gameKey)),
          records: () => walkThen('records', () => showRecords()),
          annex: () => walkThen('annex', () => showAnnex()),
          /* THE DOOR walks; THE GEAR does not. campus.js calls `registrarRoom`
           * for the Front Office room and falls back to `registrar` for the
           * topbar gear, so these two lines are the whole split. */
          registrarRoom: () => walkThen('registrar', () => showSettings()),
          registrar: () => showSettings(),
          /* THE STUDENT ID. The card OFFERS its press here and the shell owns
           * the spotlight, exactly the way it owns the Records one - the campus
           * never mints an overlay. No walk: the card is in your hand, not
           * across the quad. */
          idCard: () => showIdCard(),
          /* The photo consent, leaving for the host. ONE function, because
           * there is ONE switch (owner ruling 1). */
          idChip: () => onIdChip(),
        },
        log: say,
      });
      campus.noteDescriptors(campusDescriptors());
      /* THE CARD KNOWS WHO YOU ARE BEFORE IT IS EVER SEEN. The campus is torn
       * down and rebuilt on every visit, so the profile is handed over here
       * rather than held by the card - and an in-flight chip keeps its look
       * across the rebuild. */
      try { if (campus.setProfile) campus.setProfile(idProfile()); }
      catch (e) { say('id card profile seed threw: ' + ((e && e.message) || e)); }
      if (idChipWait) setIdChipState(idChipWait);
      campus.boardMount.appendChild(board.root);
      renderBoardExtras(campus.footMount);
      dom.screen.appendChild(campus.root);
      /* WARM TONIGHT'S ROOMS. The campus is up and the player is about to spend
       * a while looking at it; the plates behind tonight's painted doors are
       * ~1MB each and on a phone they arrive AFTER the room does, which is how
       * a lit room ships as a black rectangle with a glowing rect in it. One
       * `<link rel=prefetch as=image>` per painted key, lowest priority, once
       * per boot (room.js keeps the ledger, so the rebuild on every campus
       * visit adds nothing the second time). Unpainted keys are skipped. A
       * desktop WebView2 window reads these off local disk, where it costs a
       * file open that was going to happen anyway - harmless, and not worth a
       * "which build am I" test the page has no business taking. */
      try {
        const warmed = prefetchRoomArt(timetable.classes.map((c) => c.gameKey));
        if (warmed) say('prefetched ' + warmed + ' room plate(s)');
      } catch (e) { say('room art prefetch threw: ' + ((e && e.message) || e)); }
      /* CAMPUS PRESENCE. Built AFTER the stage is in the document and started
       * only by the campus's revealDone hook. It is its own try/catch for the
       * campus's reason: a ghost layer that throws costs the player some company,
       * never the school. With no host push and no `?presence=fixture` it draws
       * nothing at all and logs nothing - the feature is silently absent, which
       * is the whole reason it can merge before the server half exists. */
      try {
        const pOpts = presenceOptions();
        ghosts = createGhosts({
          mount: campus.ghostMount,
          bridge,
          mode: pOpts.mode,
          fast: pOpts.fast,
          reducedMotion,
          log: say,
        });
        if (campus.revealDone && campus.revealDone()) ghosts.start();
      } catch (e) {
        say('presence layer unavailable (' + ((e && e.message) || e) + ')');
        ghosts = null;
      }
      /* THE WALK. Its own try/catch for the ghost layer's reason: a walker that
       * fails to build costs the player a walk, never the school - `walkThen`
       * above sees `walker === null` and launches the class directly. Built
       * after the stage is in the document so the miniature has a plan to stand
       * on, and seeded with where the session says you are standing: the door
       * you last walked into, or the Main Gate on a fresh campus. */
      try {
        walker = createWalker({
          mount: campus.walkMount,
          reducedMotion,
          // There is no unified player hash on `init` today, so the body is
          // dealt from a stable constant. The day presence projects a self id,
          // this is the one line that changes.
          spriteId: 'self',
          log: say,
        });
        walker.setResidue(residueTrail);
        walker.mountAt(lastRoomKey ? roomStop(lastRoomKey) : null);
      } catch (e) {
        say('the walk is unavailable (' + ((e && e.message) || e) + ')');
        walker = null;
      }
      /* ORIENTATION DAY (§3.3). Built last (it consumes the campus AND the
       * walker) and started from `revealDone` above, never during the entry
       * reveal. Its own try/catch, and the catch has a JOB the other two do not:
       * the campus was built with the card WITHHELD on the strength of this
       * module, so a beat that cannot be built must hand the card over on the
       * spot. That is the whole decoration law here - the school is unharmed
       * and the player is not missing their ID, they simply never got a show. */
      if (wantsOrientation) {
        try {
          orientation = createOrientation({
            campus,
            walker,
            store,
            fireMoment,
            t,
            localDate,
            reducedMotion,
            forced: orientForced,
            log: say,
          });
          /* A reveal that already ran only counts once the STAGE is clear: the
           * boot mount reaches here revealed-but-curtained (splash, then on a
           * first night FIRST BELL), and its start is onSplashDone's to pay. A
           * LATER mount - a `?orientation=force` replay after a class - is past
           * all of that and starts right here like everything else. */
          maybeStartOrientation();
        } catch (e) {
          say('orientation unavailable (' + ((e && e.message) || e) + ') - the card is simply given');
          orientation = null;
          try { const c = campus.idCardEl && campus.idCardEl(); if (c) c.hidden = false; }
          catch (e2) { /* noop */ }
        }
      }
      // The window IS the campus: the topbar's content is diegetic in-scene
      // (crest, student ID, Front Office/gear), so the bar itself steps aside.
      // Every other screen re-shows it through renderTopbar().
      if (dom.topbar) dom.topbar.hidden = true;
      /* THE CAMPUS WANTS THE PHONE SIDEWAYS (owner bug A). The plan is a fixed
       * 16:9 geography and `meet` now fits all of it, but "all of it" inside a
       * 9:19.5 slot is a strip of architecture two rooms wide with the rest of
       * the school as sky. There is nothing to pan, so there is nothing to fix
       * by scrolling: the card asks for the turn and lifts itself on the way
       * back. It is armed ONLY for the real campus - the plain-board fallback
       * below is an ordinary scrolling panel and reads fine upright. */
      requireOrientation('landscape', { reason: 'campus' });
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
        misprintFor: seepMisprint,
      });
      const panel = el('div', 'arc-panel');
      panel.appendChild(el('p', 'arc-kicker', t('timetable', 'Timetable')));
      panel.appendChild(el('h1', 'arc-h1', t('arcademy', 'The Arcademy')));
      if (timetable.banner) panel.appendChild(el('p', 'arc-lede', timetable.banner));
      panel.appendChild(board.root);
      renderBoardExtras(panel);
      dom.screen.appendChild(panel);
      // No campus means no revealDone hook, and a night with no scenery is
      // still a night that opens: the plain board IS the school standing up.
      bellBoardRevealed = true;
      maybeFirstBell();
    }
  }

  /* ============================ SCREEN: SETTINGS ======================== */
  /**
   * THE SPLIT (owner ruling 2026-08-24). `gameKey` scopes the page to one
   * class: the pause card passes the running class's key so a player mid-class
   * sees the globals plus THEIR room's knobs, never the other eight. The
   * campus gear and the Front Office keep calling with no argument and get the
   * full sheet - the between-classes page is the right home for "everything".
   */
  function showSettings(gameKey, opts) {
    settingsBack = (opts && opts.onClose) || null;
    if (active) pauseClass(true);
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    screen = 'settings';
    clearScreen();
    renderTopbar();
    /* The marker styles.css keys the phone's settings rules off (the opaque
     * bottom bar, the scroller's padding, EMI stepping off the title). Not a
     * stage: the topbar stays up. clearScreen() -> setStage(null) unwinds it. */
    if (document.documentElement && document.documentElement.classList) {
      document.documentElement.classList.add('arc-settings-on');
    }
    settingsPage = createSettingsPage({
      init: src,
      bridge,
      games: games.list,
      keybinds,
      // The folds bank their open state here (`optionsOpen.<section>`).
      store,
      /* THE DOOR'S TWO WRITE VERBS, lent to the web Media group so its add
       * and remove buttons ride SORT's `probe-sub` / `library-remove` frames
       * rather than a second copy of them. The group only renders behind
       * `init.settings.mediaControls === true`, so on the app this is an
       * unread argument. */
      assets,
      log: say,
      gameKey: gameKey || null,
      onClose: () => {
        const back = settingsBack;
        settingsBack = null;
        if (active) { showClassScreen(); }
        else if (back) { back(); }
        else { showBoard(); }
      },
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
      // A REPAINT IS NOT AN ARRIVAL - the same guard the EMI seam below makes,
      // handed to the card so its paper only lands when the screen changed.
      arrived: wasScreen !== 'report',
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

  /* ======================= THE STUDENT ID ===============================
   * PUNCHCARD's neighbour: the one document the school hands YOU. Three seams
   * live here and nowhere else.
   *
   *   THE PROFILE   `init.profile` (additive - a host that predates it simply
   *                 never sends one, and the card draws "Student", the drawn
   *                 stand-in portrait and the unlinked chip) plus every
   *                 `profile` frame. The page derives NOTHING about your
   *                 account: no Discord handle, no snowflake and no CDN url
   *                 ever reaches this document (PRESENCE.md §10).
   *   THE STATS     read off the store the way every other screen reads it -
   *                 host-owned attendance, host-owned punch cards, the page's
   *                 own graded `days`. Nothing here counts anything twice.
   *   THE CHIP      ONE verb. Unlinked, it asks the host to run the link-up;
   *                 linked, it moves the `presenceShare` rung. Either way it
   *                 paints a waiting look and then waits for the answer.
   * ==================================================================== */

  /** Roman numerals for the term line. The campus names the same one. */
  const ID_ROMAN = ['', 'I', 'II', 'III', 'IV'];

  /** Coerce anything at all to one of the four presence rungs. */
  function idRung(value, fallback) {
    const v = String(value == null ? '' : value);
    return PRESENCE_RUNGS.indexOf(v) >= 0 ? v : (fallback || 'off');
  }

  /** ONE cue request on `document` - shell/audio.js owns the only audio node on
   *  the page (trap 18). A dropped cue is not an error. */
  function idSfx(name, level, extra) {
    try {
      if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
      const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
      if (!Ctor) return;
      document.dispatchEvent(new Ctor('arcademy-sfx', {
        detail: Object.assign(
          { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
          extra || {}
        ),
      }));
    } catch (e) { /* noop */ }
  }

  /** The presence layer's own opaque `self` id, when the host has pushed one.
   *  READ, never held: the ghost layer is rebuilt with the campus under it. */
  function idSelfId() {
    try {
      const d = ghosts && typeof ghosts.diagnostics === 'function' ? ghosts.diagnostics() : null;
      return (d && d.self) ? String(d.self) : null;
    } catch (e) { return null; }
  }

  /** The earliest date the school has on you: the first enrolment across the
   *  ten cards, and failing that the oldest day it wrote down. */
  function idEnrolled() {
    let best = null;
    for (const entry of games.list) {
      try {
        const at = store.punchCard(entry.key).enrolledAt;
        if (typeof at === 'string' && at && (!best || at < best)) best = at;
      } catch (e) { /* a card that cannot be read is not a date */ }
    }
    if (best) return best;
    try {
      const days = store.get('days') || {};
      const keys = Object.keys(days).sort();
      if (keys.length) return keys[0];
    } catch (e) { /* noop */ }
    return null;
  }

  /** Distinct local dates on which ANY class graded S. Counted off the page's
   *  own graded view, which is the only place a grade is written down. */
  function idSDays() {
    let n = 0;
    try {
      const days = store.get('days') || {};
      for (const key of Object.keys(days)) {
        const classes = (days[key] && days[key].classes) || {};
        for (const g of Object.keys(classes)) {
          if (String((classes[g] || {}).grade).toUpperCase() === 'S') { n++; break; }
        }
      }
    } catch (e) { /* noop */ }
    return n;
  }

  /** What the card says about YOU. One object, read fresh on every paint. */
  function idProfile() {
    return {
      name: profile.name,
      avatarUrl: profile.avatarUrl,
      discordLinked: !!profile.discordLinked,
      presenceShare: profile.presenceShare,
      selfId: idSelfId(),
      enrolled: idEnrolled(),
      // The web build sends you to Connections; the app opens the link-up in
      // place. The chip's hint has to say which, so it has to know.
      web: src.mediaControls === true,
    };
  }

  /** What the card says about your ATTENDANCE. Every number is somebody else's
   *  truth: the host's streak, the host's cards, the page's graded days. */
  function idStats() {
    const s = store.streak();
    const cards = games.list.length;
    let stamps = 0;
    for (const entry of games.list) {
      try { stamps += store.punchCard(entry.key).punches | 0; } catch (e) { /* noop */ }
    }
    let mastered = 0;
    try { mastered = Object.keys(unlockedMap()).length; } catch (e) { mastered = 0; }
    return {
      streak: s.count | 0,
      perfect: s.perfectDays | 0,
      stamps,
      stampCap: cards * (store.holes || 10),
      sDays: idSDays(),
      termRoman: ID_ROMAN[currentSemester()] || 'I',
      tier: maxTier(),
      enrolled: idEnrolled(),
      mastered,
      cards,
    };
  }

  /** THE OPEN COUNTER, page-owned (`idOpens`). The full cue sheet plays for the
   *  first three opens and the compact cut every time after - repetition
   *  shrinks the party, and the sheet lands on the same numbers either way. */
  function idOpenCount() {
    let n = 0;
    try { n = Math.max(0, Math.round(Number(store.get('idOpens')) || 0)); } catch (e) { n = 0; }
    n += 1;
    try { store.set('idOpens', n); } catch (e) { /* a counter may never hold a door */ }
    return n;
  }

  /** Repaint both surfaces from the ONE profile. Every echo lands here. */
  function paintIdProfile() {
    const p = idProfile();
    try { if (campus && campus.setProfile) campus.setProfile(p); }
    catch (e) { say('id card repaint threw: ' + ((e && e.message) || e)); }
    try { if (idSpotlight) idSpotlight.setProfile(); } catch (e) { /* noop */ }
    if (idChipWait) setIdChipState(idChipWait);
  }

  /** The chip's in-flight look, on whichever surfaces exist. */
  function setIdChipState(state) {
    try { if (campus && campus.setChipState) campus.setChipState(state); } catch (e) { /* noop */ }
    try { if (idSpotlight) idSpotlight.setChipState(state); } catch (e) { /* noop */ }
  }

  function clearIdLinkTimer() {
    if (!idLinkTimer) return;
    try { clearTimeout(idLinkTimer); } catch (e) { /* noop */ }
    idLinkTimer = 0;
  }

  /** PHOTO DAY, on whichever surface the player is actually looking at. */
  function runIdPhotoDay() {
    try {
      if (idSpotlight && idSpotlight.isOpen()) idSpotlight.photoDay();
      else if (campus && campus.photoDay) campus.photoDay();
    } catch (e) { say('photo day threw: ' + ((e && e.message) || e)); }
    /* EMI's beat. There is no `photoDay` row in emi/moments.js, so this asks
     * her for the face directly - null-safe both ways (an unmounted or a
     * dismissed mascot is a silent no-op, moments.js's own rule). */
    try { const emi = getEmi(); if (emi && emi.emote) emi.emote('glee'); } catch (e) { /* noop */ }
  }

  /**
   * THE CHIP'S ONE VERB, and the ONLY place either frame is posted.
   *   not linked  -> ask the host to run the link-up, and say we are waiting.
   *                  90 seconds later, with no answer at all, the chip goes
   *                  back to its rung: an OAuth flow can be abandoned in a
   *                  browser tab the page will never hear about again.
   *   linked      -> move the `presenceShare` rung, and paint `pending` until
   *                  the host's `setting` echo says what actually stuck.
   * A second press while either is in flight is refused - one frame per state.
   */
  function onIdChip() {
    if (idChipWait) return;
    if (!profile.discordLinked) {
      idChipWait = 'wait';
      setIdChipState('wait');
      try { bridge.send({ type: 'link-discord', thenShare: 'discord' }); }
      catch (e) { say('link-discord send failed: ' + ((e && e.message) || e)); }
      try {
        idLinkTimer = setTimeout(() => {
          idLinkTimer = 0;
          if (idChipWait !== 'wait') return;
          idChipWait = null;
          paintIdProfile();
          say('id chip: no profile frame in 90s - the chip goes back to its rung');
        }, 90000);
      } catch (e) { idLinkTimer = 0; }
      return;
    }
    const next = profile.presenceShare === 'discord' ? 'username' : 'discord';
    idChipWait = 'pending';
    setIdChipState('pending');
    try { bridge.send({ type: 'set-setting', key: SETTING_KEYS.presenceShare, value: next }); }
    catch (e) { say('presenceShare send failed: ' + ((e && e.message) || e)); }
  }

  /**
   * OPEN THE ID SPOTLIGHT. Minted on the first press and kept after that, the
   * Records page's own shape. EMI is bracketed off while it is up and restored
   * on EVERY path out through `onClose` - the annex bracket's discipline.
   */
  function showIdCard() {
    if (!idSpotlight) {
      try {
        idSpotlight = createIdSpotlight({
          t,
          reducedMotion: () => reducedMotion || idReducedMotion(),
          lite: !!src.performanceMode,
          isMobile,
          profile: idProfile,
          stats: idStats,
          onChip: onIdChip,
          onRecords: () => showRecords(),
          onClose: releaseIdCard,
          onOpenCount: idOpenCount,
          sfx: idSfx,
          log: say,
        });
      } catch (e) {
        say('the ID spotlight is unavailable (' + ((e && e.message) || e) + ')');
        idSpotlight = null;
        return;
      }
    }
    let from = null;
    try { from = campus && campus.idCardEl ? campus.idCardEl() : null; } catch (e) { from = null; }
    try {
      const emi = getEmi();
      if (emi && emi.setEnabled) { idEmiPrev = !!emi.enabled; emi.setEnabled(false); }
    } catch (e) { /* noop */ }
    try { idSpotlight.open(from); } catch (e) {
      say('the ID spotlight refused to open (' + ((e && e.message) || e) + ')');
      releaseIdCard();
      return;
    }
    if (idChipWait) setIdChipState(idChipWait);
  }

  /** The bracket's other half. Safe to call twice, and it only ever gives EMI
   *  back the state she held BEFORE the card came up (a player who keeps her
   *  off in settings never sees her flicker on). */
  function releaseIdCard() {
    if (!idEmiPrev) return;
    idEmiPrev = false;
    try { const emi = getEmi(); if (emi && emi.setEnabled) emi.setEnabled(true); } catch (e) { /* noop */ }
  }

  /** Put the card back, wherever the ask came from. True when one was up. */
  function dismissIdCard(silent) {
    if (!idSpotlight) return false;
    let was = false;
    try { was = !!idSpotlight.dismiss(silent); } catch (e) { was = false; }
    releaseIdCard();
    return was;
  }

  /* ============================ SCREEN: RECORDS =========================
   * PUNCHCARD §6, and it is a ROOM now (0825). The campus's Records door used
   * to open the day's report card, then the office as a page; it opens the
   * painted office itself - the tray of cards, the noticeboard, the book on the
   * desk and, once the wall has moved, the storeroom. The old page is still the
   * old page: shell/records.js renders it verbatim inside the tray's panel, and
   * the report card is still one press further in and still the ONE share
   * pipeline (trap 13).
   *
   * Everything that touches state stays HERE. The room gets narrow caps - the
   * annex's law - and three page-owned meta keys it can only read and write
   * through this seam: `recordsRoomVisits`, `recordsRoomSeenStamps` and
   * `recordsBookPage`. None of them is host-owned; `ArcademyMetaStore.Set`
   * takes any new top-level key under its own caps, so no C# change is owed.
   * ==================================================================== */

  /** Every stamp on every card, right now. The tray's fresh tab is this number
   *  against the one banked at the last time the drawer was opened. */
  function totalStamps() {
    let n = 0;
    for (const entry of games.list) {
      try { n += Math.max(0, Math.round(Number(store.punchCard(entry.key).punches) || 0)); }
      catch (e) { /* a junk card is worth nothing, never a throw */ }
    }
    return n;
  }

  function showRecords() {
    screen = 'records';
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    clearScreen();
    renderTopbar();
    recordsRoom = createRecordsRoom({
      mount: dom && dom.screen,
      t,
      log: say,
      lite: !!src.performanceMode,
      reduced: reducedMotion,
      // Registry order, every registered class - including the ones never
      // played. An unattended card on the wall IS the advertisement (§6).
      gameKeys: games.list.map((e) => e.key),
      gameName,
      // The card reader, not the blob: store.punchCard normalizes, so the
      // room never has to guess what a half-written card means.
      punchCard: (key) => store.punchCard(key),
      stampTotal: () => totalStamps(),
      seenStamps: () => Number(store.get('recordsRoomSeenStamps')) || 0,
      markSeen: (n) => { try { store.set('recordsRoomSeenStamps', n); } catch (e) { say('records seen write failed'); } },
      visits: () => Number(store.get('recordsRoomVisits')) || 0,
      markVisit: (n) => { try { store.set('recordsRoomVisits', n); } catch (e) { say('records visit write failed'); } },
      bookPage: () => Number(store.get('recordsBookPage')) || 0,
      saveBookPage: (n) => { try { store.set('recordsBookPage', n); } catch (e) { say('records book page write failed'); } },
      // THE SAME NIGHT'S WALL the hall prop shows: one seed, one table, one
      // state (corkboard.js is already initCorkboard'd with both above).
      daySeed: utcDateSeed,
      onCorkRead: () => { /* the read rows are the module's own; nothing owed here */ },
      // THE STOREROOM. The shell keeps the gate: the door exists only once the
      // reveal has fired (ANNEX-OS.md §1), and the room just draws it. The
      // owner's peek is the second key to the same door and stamps nothing.
      ajar: !!store.get('annexRevealSeen') || annexPeek,
      onBack: () => showBoard(),
      onReport: () => showReport(),
      onAnnex: () => showAnnex(),
      reportLabel: t('report_card', 'Report Card'),
    });
    setStage('arc-report-on');
    if (recordsRoom && typeof recordsRoom.fit === 'function') recordsRoom.fit();
  }

  /* ---------------------- the graded launch ----------------------------
   * ONE launch path for a door, wherever the press came from (the campus
   * card's Begin, the room scene's lit furniture). Board first, then:
   * THE EARNED DOOR, then the dev one. A completed punch card opens its room
   * every night - through the SAME path the dev pass uses, so an unlocked run
   * is an ordinary graded class end to end (grades in `days`, XP once per UTC
   * day, attendance idempotent). Nothing host-side gates which room may
   * start; this is the whole feature. THE DEV DOOR: a room off tonight's
   * board still opens when the host came in through `--arcademy` - graded +
   * timed like a dealt class, built from the registry descriptor
   * (freeSwimClass's parachute).
   * -------------------------------------------------------------------- */
  function launchGraded(gameKey) {
    const cls = timetable.classes.find((c) => c.gameKey === gameKey);
    if (cls) { startClass(cls); return; }
    if (isUnlocked(gameKey)) {
      say('punch card unlock: ' + gameKey + ' is off the board - graded run anyway');
      startClass(freeSwimClass(gameKey));
      return;
    }
    if (devPass) {
      say('dev pass: ' + gameKey + ' is off the board - graded run anyway');
      startClass(freeSwimClass(gameKey));
    }
  }

  /* ========================== SCREEN: ROOM ==============================
   * THE ROOM SCENE. The VN antechamber: the walk brought the student to the
   * door, this is the other side of it - the painted set with the class's
   * own furniture lit. A screen like records (same funnel, same ladder
   * shape); room.js holds the stage and the shell keeps everything that
   * touches state. It shows only for enterable doors (campus.js gates the
   * offer on scheduled/unlocked/devPass and never while suspended), so the
   * dark-room card, its EMI seam and the suspend card are all untouched.
   * The facts on the plate are the door card's, computed FRESH here so a
   * retake entered seconds after a grade wears tonight's letter.
   * ==================================================================== */
  function showRoomScene(gameKey, info) {
    if (!hasRoomScene(gameKey)) { showBoard(); return; }
    screen = 'room';
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    clearScreen();
    renderTopbar();
    /* the door-open thump the card used to own - one verb, one sound */
    sfx('door', 0.35);
    const rec = todaysRecord(gameKey);
    const done = !!rec;
    const scheduled = timetable.classes.some((c) => c.gameKey === gameKey);
    let statusLine, actionLabel, xpLine;
    if (done) {
      statusLine = t('retake', 'Retake');
      actionLabel = t('retake', 'Retake');
      xpLine = t('campus_xp_retake', 'Retakes pay no XP - pride only.');
    } else if (scheduled) {
      statusLine = t('campus_in_session', 'In Session');
      actionLabel = t('begin_class', 'Begin');
      xpLine = t('campus_xp_first', 'First pass of the day pays XP.');
    } else if (isUnlocked(gameKey)) {
      statusLine = t('campus_unlocked_sign', 'Open');
      actionLabel = t('begin_class', 'Begin');
      xpLine = t('campus_unlocked_hint',
        'Card complete. This room opens every night, board or no board.');
    } else {
      /* the dev pass - campus.js's gate means this branch IS enterable */
      statusLine = t('campus_dev_pass', 'Dev pass · Begin');
      actionLabel = t('begin_class', 'Begin');
      xpLine = t('campus_dev_pass_hint', "Dev pass: off tonight's board, graded anyway.");
    }
    const swim = suspendedGlobally ? null : endlessFor(gameKey);
    roomPage = createRoomScene({
      gameKey,
      t,
      lite: !!src.performanceMode,
      log: say,
      name: gameName(gameKey),
      plate: (info && info.plate) || '',
      statusLine,
      actionLabel,
      stamp: done && rec && rec.grade ? String(rec.grade) : '',
      xpLine,
      onEnter: () => launchGraded(gameKey),
      onBack: () => showBoard(),
      /* THE OFFER THE CARD USED TO CARRY. The room REPLACES the door card, and
       * the card was where a game with `manifest.endless` showed its second,
       * subordinate Free Swim button - so the room owes the same offer or the
       * fan-out quietly takes it away. room.js paints it twice (the ladder in
       * the art, the side slab on the apron) and spends one latch for both;
       * startFreeSwim re-checks endlessFor itself, so this is a hint, not an
       * authority. A game with nothing declared gets undefined = neither. */
      onFreeSwim: swim ? () => startFreeSwim(gameKey) : undefined,
      freeSwimLabel: swim ? t(swim.labelKey || 'free_swim', 'Free Swim') : '',
      /* THE RAIL'S THIRD ERRAND. The knobs are asked for from the doorway more
       * than anywhere else, so the room lends them its own way back: settings
       * opened from here folds into the room it left, never past it. */
      onOptions: () => showSettings(gameKey, { onClose: () => showRoomScene(gameKey, info) }),
    });
    if (dom && dom.screen) dom.screen.appendChild(roomPage.root);
    setStage('arc-report-on');
    if (typeof roomPage.fit === 'function') roomPage.fit();
  }

  /* ============================ THE ANNEX ===============================
   * ANNEX-OS.md. The lab under the Records Office: a screen like records,
   * not an overlay - same funnel, same ladder shape, same narrow-caps law.
   * EMI is absent while the player is inside (the 0823 lock: dock gone, not
   * a hint, not once); the bracket is setEnabled and clearScreen() restores
   * it on EVERY path out. First entry flips the voice's labSeen - the one
   * verb the lab wave owns - which arms b27, the glitch and the wrong
   * channel without adding a word of dialogue.
   * ==================================================================== */
  function showAnnex() {
    screen = 'annex';
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    clearScreen();
    renderTopbar();
    try { const v = getVoice(); if (v && v.setLabSeen) v.setLabSeen(true); } catch (e) { /* noop */ }
    try {
      const emi = getEmi();
      if (emi && emi.setEnabled) { annexEmiPrev = !!emi.enabled; emi.setEnabled(false); }
    } catch (e) { /* noop */ }
    annexPage = createAnnexLab({
      t,
      lite: !!src.performanceMode,
      log: say,
      subject: src.subject || {},
      gamesList: games.list.map((e) => e.key),
      gameName,
      // ONE page-owned blob, not N keys (the meta-store headroom law).
      annexState: () => store.get('annex') || {},
      saveAnnex: (patch) => { try { store.merge('annex', patch); } catch (e) { say('annex merge failed'); } },
      liveFile: () => subjectFile(),
      fetchStats: () => requestAnnexStats(),
      onExit: () => showRecords(),
    });
    if (dom && dom.screen) dom.screen.appendChild(annexPage.root);
    setStage('arc-report-on');
    if (typeof annexPage.fit === 'function') annexPage.fit();
  }

  /* THE SUBJECT FILE (punch 3B). Built fresh at every open so the numbers are
   * current - that is the gut punch, the paper upstairs was old. Sources:
   * the host-resolved `subject` init key (app-lifetime counters, quote-only-
   * what-is-counted law), the store (attendance, cards), and EMI's sanctioned
   * stats() accessor. A missing source drops its rows, never fakes them. */
  function subjectFile() {
    const sub = src.subject || {};
    const secs = [];
    const gen = [];
    const num = (v) => (typeof v === 'number' && isFinite(v));
    if (sub.date) gen.push([t('annex_f_since', 'on record since'), sub.date]);
    if (num(sub.level)) gen.push([t('annex_f_level', 'level'), sub.level]);
    if (num(sub.xp)) gen.push([t('annex_f_xp', 'experience, lifetime'), Math.round(sub.xp)]);
    if (num(sub.minutes)) gen.push([t('annex_f_minutes', 'supervised minutes'), Math.round(sub.minutes)]);
    if (num(sub.videoMinutes)) gen.push([t('annex_f_video', 'screening minutes'), Math.round(sub.videoMinutes)]);
    if (num(sub.spiralMinutes)) gen.push([t('annex_f_spiral', 'focus minutes'), Math.round(sub.spiralMinutes)]);
    if (num(sub.achievements)) gen.push([t('annex_f_ach', 'citations on file'), sub.achievements]);
    if (gen.length) secs.push({ title: t('annex_f_general', 'GENERAL'), rows: gen });

    const att = [];
    try {
      const streak = store.get('streak');
      if (num(streak)) att.push([t('annex_f_streak', 'attendance streak'), streak]);
      const perfect = store.get('perfectAttendance');
      if (num(perfect)) att.push([t('annex_f_perfect', 'perfect nights'), perfect]);
      att.push([t('annex_f_cards', 'cards mastered'), masteredCount()]);
    } catch (e) { /* rows drop, never fake */ }
    if (num(sub.appStreak)) att.push([t('annex_f_appstreak', 'reporting streak'), sub.appStreak]);
    if (num(sub.appStreakBest)) att.push([t('annex_f_appbest', 'reporting streak, best'), sub.appStreakBest]);
    if (num(sub.sessionsStarted)) att.push([t('annex_f_sessions', 'sessions opened'), sub.sessionsStarted]);
    if (att.length) secs.push({ title: t('annex_f_attend', 'ATTENDANCE'), rows: att });

    const dev = [];
    if (num(sub.flashes)) dev.push([t('annex_f_flashes', 'exposures delivered'), sub.flashes]);
    if (num(sub.bubbles)) dev.push([t('annex_f_bubbles', 'targets cleared'), sub.bubbles]);
    if (num(sub.lockCards)) dev.push([t('annex_f_lockcards', 'sentences typed'), sub.lockCards]);
    if (num(sub.keywordTriggers)) dev.push([t('annex_f_triggers', 'cue firings'), sub.keywordTriggers]);
    if (dev.length) secs.push({ title: t('annex_f_devices', 'DEVICES'), rows: dev });

    const obs = [];
    try {
      const emi = getEmi();
      const st2 = emi && typeof emi.stats === 'function' ? emi.stats() : null;
      if (st2) {
        const pick = [
          ['pets', 'annex_f_pets', 'pets received'],
          ['drags', 'annex_f_drags', 'relocations'],
          ['flings', 'annex_f_flings', 'ejections'],
          ['hides', 'annex_f_hides', 'dismissals'],
          ['dockRestores', 'annex_f_restores', 'recalls from dock'],
          ['bubblesSeen', 'annex_f_lines', 'lines delivered'],
          ['sessions', 'annex_f_emisessions', 'sessions observed'],
          ['days', 'annex_f_emidays', 'days observed'],
        ];
        pick.forEach(([k, lk, fb]) => { if (num(st2[k])) obs.push([t(lk, fb), st2[k]]); });
        if (num(st2.msVisible)) {
          obs.push([t('annex_f_hours', 'hours observed'), (st2.msVisible / 3600000).toFixed(1)]);
        }
      }
    } catch (e) { /* rows drop, never fake */ }
    if (obs.length) secs.push({ title: t('annex_f_unit', 'UNIT OBSERVATION'), rows: obs });
    return secs;
  }

  /* THE REGISTRY LINK (punch 2). The page cannot reach the server (CORS is a
   * wall, and the wall is right): the host fetches the public aggregate and
   * posts it back as `annex-stats`. One request in flight, an 8s deadline,
   * and every failure resolves null - the OS renders LINK DOWN, never a
   * fabricated number (C6). Offline mode never sends at all. */
  function requestAnnexStats() {
    if (src.offlineMode) return Promise.resolve(null);
    if (annexStatsWait) return annexStatsWait.promise;
    const w = {};
    w.promise = new Promise((resolve) => {
      w.resolve = resolve;
      w.timer = setTimeout(() => { annexStatsWait = null; resolve(null); }, 8000);
    });
    annexStatsWait = w;
    try { bridge.send({ type: 'annex-stats' }); }
    catch (e) {
      clearTimeout(w.timer);
      annexStatsWait = null;
      return Promise.resolve(null);
    }
    return w.promise;
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
  /* ==================================================================== *
   * THE RECORDS ANNEX REVEAL (one night only)
   * ==================================================================== */

  function dismissAnnexStage() {
    if (annexProbe) { clearTimeout(annexProbe); annexProbe = 0; }
    if (!annexStage) return false;
    const st = annexStage;
    annexStage = null;
    try { st.destroy(); } catch (e) { /* noop */ }
    return true;
  }

  /** Fire the reveal if - and only if - the whole school is sealed, the save
   *  has never seen it, and there is a quiet stage to fire it on. `extraKey`
   *  folds an in-flight tenth hole the way masteredCount always has. */
  function maybeAnnexReveal(extraKey) {
    if (annexStage || destroyed) return;
    if (screen === 'class' || active) return;
    if (store.get('annexRevealSeen')) return;
    if (masteredCount(extraKey) < games.list.length) return;
    /* trap 66: the loader's whole contract is `hidden` - a reveal that fires
     * under the splash is a reveal that never happened. Probe until it lands. */
    let loader = null;
    try { loader = document.getElementById('arc-loader'); } catch (e) { /* noop */ }
    if (loader && !loader.hidden) {
      if (annexProbe) clearTimeout(annexProbe);
      annexProbe = setTimeout(() => { annexProbe = 0; maybeAnnexReveal(extraKey); }, 450);
      return;
    }
    /* Never cut on top of a sentence: the greet (which can queue behind the
     * face decode and land seconds after showBoard) or the all-mastered beat
     * may still be on the bubble. Wait for her to finish and try again -
     * the THUD interrupting EMI mid-line would read as a renderer hiccup,
     * not a story beat (playtest 0824). */
    let talking = false;
    try { const emi = getEmi(); talking = !!(emi && emi.saying); } catch (e) { /* noop */ }
    if (talking) {
      if (annexProbe) clearTimeout(annexProbe);
      annexProbe = setTimeout(() => { annexProbe = 0; maybeAnnexReveal(extraKey); }, 450);
      return;
    }
    /* Seen is stamped at MOUNT, not at finish - a beat half-watched is a beat
     * watched; it must never replay into a farce. */
    store.set('annexRevealSeen', true);
    /* THE OFFICE LEARNS IT THE SAME BREATH THE SAVE DOES. `ajar` is the one
     * gate the storeroom door and its patch read, and it is set from this key
     * on room entry - so a room already standing when the wall moves has to be
     * told, or the door stays painted shut until the next visit. It cannot
     * happen mid-class (the reveal refuses one) and a room that is not up is
     * simply null: one line, both orderings, mirroring exactly the read
     * showRecords does. */
    try { if (recordsRoom) recordsRoom.setAjar(true); } catch (e) { /* noop */ }
    annexStage = createAnnexReveal({
      mount: document.body,
      reducedMotion,
      onDone: () => { annexStage = null; },
    });
  }

  /* EMI SEAM (EMI COLOR, 2026-08-24): THE STREAK OBITUARY. The host rolls the
   * attendance streak at local midnight, so the one moment the shell can catch
   * a streak DYING is the first arrival of a session: the snapshot already
   * carries the rolled number while `emiLastStreak` still holds the height it
   * reached last time (written on every stamp below). A drop from >= 3 to the
   * floor fires the `streakBroken` cry - delayed a breath so it never talks
   * over the greet - and any ambiguity (host not yet synced, first boot ever)
   * reads as NO, never as a guessed funeral. */
  function noteStreakTurn() {
    try {
      const cur = Number((store.streak() || {}).count) || 0;
      const prev = Number(store.get('emiLastStreak')) || 0;
      if (prev >= 3 && cur <= 1 && cur < prev) {
        setTimeout(() => {
          try { fireMoment('streakBroken', { prev, streak: cur }); } catch (e) { /* noop */ }
        }, 6500);
      }
      if (cur !== prev) store.set('emiLastStreak', cur);
    } catch (e) { /* a mascot may never break an arrival */ }
  }

  function runPunchCeremony(o) {
    // EMI SEAM: the stamp lands. ^_^ + hearts, (≧◡≦) on a 3-day streak, COOL on
    // the tenth hole - moments.js owns which.
    try {
      const liveStreak = store.streak().count;
      if (liveStreak !== (Number(store.get('emiLastStreak')) || 0)) {
        store.set('emiLastStreak', liveStreak);   // the obituary's high-water mark
      }
      fireMoment('stamp', {
        gameKey: o && o.gameKey,
        streak: store.streak().count,
        perfect: !!(o && o.justUnlocked),
        grade: (o && o.justUnlocked) ? 's' : null,
        // The post-mint total and how many holes this finish bought: the card
        // the ceremony is about to draw, never a count of our own.
        holes: (o && o.card && Number(o.card.punches)) || (o && Number(o.to)) || 0,
        minted: (o && Number(o.minted)) || 1,
      });
    } catch (e) { /* a mascot may never break a ceremony */ }
    // EMI SEAM: the card turned MASTERED - and, when it was the last one, the
    // whole school. `justUnlocked` is the host's own signal; nothing is diffed.
    try {
      if (o && o.justUnlocked) fireMoment('cardMastered', { gameKey: o.gameKey, count: masteredCount(o.gameKey) });
    } catch (e) { /* a mascot may never break a ceremony */ }
    try {
      if (o && o.justUnlocked && masteredCount(o.gameKey) >= games.list.length) fireMoment('allMastered', { count: masteredCount(o.gameKey) });
    } catch (e) { /* a mascot may never break a ceremony */ }
    // EMI SEAM: the enrollment mint (the three first-run punches).
    try {
      if (o && o.reason === 'enrollment') fireMoment('enrolMint', { gameKey: o.gameKey, enrolled: enrolledCount(o.gameKey), total: games.list.length });
    } catch (e) { /* a mascot may never break a ceremony */ }
    dismissPunchStage();
    dismissAnnexStage();
    if (!dom || !dom.screen) return null;
    const spec = o || {};
    /* Whether THIS seal is the school's LAST - decided now, while the mint is
     * in hand (the store echo may lag; masteredCount folds the in-flight card
     * exactly the way the allMastered seam above does). Consumed in onDone:
     * the reveal fires after the player has SEEN the tenth stamp and pressed
     * Done - never over the ceremony it is about. */
    const finalSeal = !!spec.justUnlocked && masteredCount(spec.gameKey) >= games.list.length;
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
      onDone: () => {
        punchStage = null;
        /* FIRST BELL SEAM (m01, the second slip). The FIRST-EVER card ceremony
         * has cleared: the front desk's note slides out from under the live
         * board, and EMI's one new line lands after it. Once-ever, guarded, and
         * the VN itself refuses to mount over a live class (canInterrupt) - so
         * the ordinary "Done" path and a screen change both read the same. */
        try { if (vn) vn.afterCeremony(); }
        catch (e) { say('first bell mail skipped (' + ((e && e.message) || e) + ')'); }
        /* THE ANNEX REVEAL: the school's LAST seal. The two seams can never
         * collide - a first-ever ceremony cannot be the tenth card's tenth
         * hole - so this simply runs after the mail check. */
        if (finalSeal) maybeAnnexReveal(spec.gameKey);
      },
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
     * A FREE SWIM has no budget, so it has no bar (panel carries arc-endless).
     * A CLOCKLESS CLASS has a budget and still refuses one: the bar is the loud
     * half of "you are being timed", and homeroom's minute is a ritual, not an
     * exam. Nothing downstream needs a guard - timeBarSet / timeBarTick /
     * timeBarPaint all return on a null `timebar`, exactly as they do for a
     * free swim, so the clock simply never starts. */
    const clockless = !!cls.clockless;
    let timebar = null;
    let timefill = null;
    if (!endless && !clockless) {
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
    // replaces it names what this is instead. A CLOCKLESS class gets neither:
    // it IS timed, it simply never says so, so there is nothing honest to put
    // in the slot and the strip closes over it.
    const clock = endless
      ? el('span', 'chip', t('free_swim', 'Free Swim'))
      : (clockless ? null : el('span', 'chip num', cls.timeBudgetSec + 's'));
    if (clock) bar.appendChild(clock);
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
       *   deadBeat(beat, opts)           THE SEEP'S CLASS-SIDE DOOR. The class
       *                                  names a DEAD MOMENT it is standing in and
       *                                  gets a handle or (almost always) null. NOT
       *                                  kind-addressed and NOT an effect: it spends
       *                                  no channel, clears no ceiling and has
       *                                  nothing to do with the manifest allowlist,
       *                                  because a seep frame is the school talking,
       *                                  not the class. engine/index.js's header
       *                                  carries the whole contract.
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
      deadBeat: safe('deadBeat'),
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
    /* THE SEEP'S EAR. A new class resets the per-class scopes of the class-side
     * kit; nothing else about a start interests it. */
    try { if (seep) seep.note('classStart', { gameKey: cls && cls.gameKey }); } catch (e) { /* noop */ }
    /* FIRST BELL SEAM (s03, the walk to Homeroom). ADDITIVE AND ONCE-EVER: the
     * VN plays one caption on the midway and a beat on the Homeroom threshold,
     * then re-enters this function with the flag already spent, so the shipped
     * class takeover is byte-for-byte the next thing that happens. A false, a
     * throw, a spent flag, a missing plate and a class that is not Homeroom all
     * fall straight through to the class - the VN may never be a gate. */
    try {
      if (vn && vn.gateClass({ gameKey: cls.gameKey, homeroom: !!cls.homeroom },
        () => startClass(cls, opts))) return;
    } catch (e) { say('first bell walk skipped (' + ((e && e.message) || e) + ')'); }
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    if (active) teardownClass();

    const endless = !!(opts && opts.endless);
    const entry = games.byKey[cls.gameKey]
      || { key: cls.gameKey, ok: false, mod: suspendedStub(cls.gameKey, 'not in this build') };
    const mod = entry.mod;
    const manifest = (mod && mod.manifest) || {};
    const gameMeta = store.gameMeta(cls.gameKey);
    const gradeTier = tierFor(gameMeta);
    /* A free swim has NO budget: 0 is the contract the game reads as "no bell".
     * Otherwise the CLASS's own budget, fenced by the ceiling for its type -
     * and since the class-length wave both ceilings are 300 and `meaty` is the
     * anchor-slot flag rather than a length, so this Math.min only ever bites a
     * calendar override that asked for more than five minutes. The `||` arm is
     * a belt: the timetable clamps every dealt budget to >= MIN_BUDGET_SEC and
     * freeSwimClass now carries the descriptor's own, so nothing reaches here
     * with a falsy one. */
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
    // EMI SEAM. `family` rides along for the place-awareness table in
    // moments.js (EMI COLOR): the arrival face knows what kind of room it is.
    /* ASKS: `soft` is a01's YES, and it only ever softens a FACE. */
    fireMoment('classStart', { gameKey: cls.gameKey, tier: gradeTier,
      family: manifest.family || null, soft: askSoft() });
    const chrome = classScreenChrome(
      Object.assign({}, cls, { timeBudgetSec }), gradeTier, retake, endless);

    /* --- engine for this class --- */
    /* ONE spiral per class, picked off the class seed and then held: the
     * engine asks per wash, and a fresh answer every time would swap the image
     * mid-fade. Never awaited, never preloaded here - the browser fetches it
     * when the first spiral wash actually mounts, and the CSS conic gradient
     * is still the fallback if it 404s. */
    const classSpiral = pickSpiralUrl(seed, src.settings);
    /* THIS ROOM'S CAMERA, for the class-side kit's Resume Slate (`FEED 05 - SYNC`
     * in The Deep End). The DIRECTOR owns the Annex map, so it resolves the tag
     * and the shell keeps no second copy of a table it does not own. */
    const seepFeedTag = seep ? seep.feedTag(cls.gameKey) : '';
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
        /* THE SEEP, CLASS-SIDE. An INJECTION, never an import: the engine must
         * not know shell/seep.js exists. Two fields and no more - the door
         * itself, and this room's camera tag, which the shell resolves because
         * the shell is the half that knows the game key (FEED 05 is The Deep
         * End's, per the Annex map, and a player who cross-checks it against the
         * camera wall is meant to find it agrees). A director that failed to
         * build hands the engine nothing and every dead beat answers null. */
        seep: seep ? {
          beat: (name, payload) => {
            try { return seep ? seep.beat(name, payload) : null; } catch (e) { return null; }
          },
          feed: seepFeedTag,
        } : null,
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

    /* ---- ctx.mood (EMI COLOR, 2026-08-24): THE TENSION MIRROR ------------
     * A game may TELL the mascot how the room feels; it may not make her talk.
     * `tense`/`clutch` reach only the wordless MOMENTS table (no pool exists on
     * either name, by design - mid-class speech stays barred), `stumble` fires
     * the small 'miss' face whose pool always said "one wrong answer, one
     * dropped tile" and finally means it (its own maxPerClass:1 still rations
     * the words), and `runLost` is the mid-class K.O. All throttled HERE so no
     * game can flood her: tense latches until calm, everything shares a 15s
     * spacing, stumbles cap at 3 a class, the K.O. spends once. Opt-in per
     * game; a class that never calls it plays exactly as before. */
    const mood = (() => {
      let tenseLatch = false, stumbles = 0, lastAt = 0, koSpent = false;
      const MOOD_SPACING_MS = 15000;
      const fire = (name, extra) => {
        try { fireMoment(name, Object.assign({ gameKey: cls.gameKey, midClass: true }, extra || {})); }
        catch (e) { /* a mascot may never break a class */ }
      };
      return Object.freeze({
        tense() {
          if (tenseLatch) return; const now = Date.now();
          if (now - lastAt < MOOD_SPACING_MS) return;
          tenseLatch = true; lastAt = now; fire('tense');
        },
        calm() { tenseLatch = false; },
        clutch() {
          const now = Date.now();
          if (now - lastAt < MOOD_SPACING_MS) return;
          lastAt = now; fire('clutch');
        },
        stumble() {
          if (stumbles >= 3) return; const now = Date.now();
          if (now - lastAt < MOOD_SPACING_MS) return;
          stumbles += 1; lastAt = now; fire('miss');
        },
        runLost() { if (koSpent) return; koSpent = true; fire('runLost'); },
      });
    })();

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
      mood,

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
      // true while a game's setup() door is open (S3) - the clock is not armed
      // yet and Esc means "leave", not "pause".
      inSetup: false,
      pill: chrome.pill, timeBudgetSec, endless,
      // the shell's own class clock (S1) - see timeBar* above
      timebar: chrome.timebar, timefill: chrome.timefill,
      clockTimer: 0, clockElapsedMs: 0, clockLastAt: NaN,
      clockRunning: false, clockDone: false, clockArmed: false,
    };

    dom.screen.appendChild(chrome.panel);
    setStage('arc-class-on');
    /* THE ROOM ASKS FOR A SHAPE (games/registry.js `orientation`). Phones only,
     * and 'any' arms nothing at all, so nine tenths of the desktop code path is
     * a no-op here. Armed AFTER the stage is mounted so the card lands over a
     * built class rather than over the screen it replaced, and the freeze rides
     * the same hook in both directions - the card going up stops the clock, the
     * card coming down starts it again. */
    requireOrientation(entry.orientation, {
      reason: 'class',
      onChange: (blocking) => orientFreeze(blocking),
    });
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

    /* ---------------------- THE SETUP DOOR (S3) --------------------------
     * A class may own a DOOR that runs BEFORE its clock: `manifest.setup:true`
     * plus an `instance.setup()` on the object create() returned. SORT's is the
     * pile picker - which piles you are sorting is the whole game, so asking it
     * inside the 120s would charge the player for reading their own question.
     *
     * Four things make this safe to hand a game:
     *   - THE CLOCK IS STILL ARMED IN beginPlay AND NOWHERE ELSE, so a door
     *     that takes a minute costs the class nothing.
     *   - `false` IS THE ONLY WAY OUT. A resolved `false` means "the player
     *     backed out" and walks to campus through the ordinary path (which
     *     tears the class down and posts class-left). Any other value - true,
     *     undefined, a throw, a rejection - starts the class, because a door
     *     that breaks must never be able to strand a player on an empty stage.
     *   - IT RUNS AFTER THE ENROLLMENT INTRO, never instead of it: the intro is
     *     the school's, the door is the game's, and the school speaks first.
     *   - Esc while it is up is the ordinary LEAVE CONFIRM (see escapeStep):
     *     there is no class to pause yet, so "get me out of here" is the only
     *     honest reading of the key.
     * A game with no setup() is untouched - beginPlay is still called directly.
     * ------------------------------------------------------------------- */
    function runSetup() {
      // Same guard beginPlay carries: the stage can be gone by now.
      if (!active || active.instance !== instance) return;
      const declared = manifest.setup === true;
      const has = typeof instance.setup === 'function';
      if (declared !== has) {
        say('game ' + cls.gameKey + ': manifest.setup=' + declared + ' but setup() '
          + (has ? 'exists' : 'is missing') + ' - the FUNCTION decides');
      }
      if (!has) { beginPlay(); return; }
      active.inSetup = true;
      const go = (why) => {
        if (!active || active.instance !== instance) return;
        active.inSetup = false;
        if (why) say('game ' + cls.gameKey + ' setup() ' + why);
        beginPlay();
      };
      let p;
      try { p = Promise.resolve(instance.setup()); }
      catch (e) { go('threw (' + ((e && e.message) || e) + ') - starting anyway'); return; }
      p.then((ok) => {
        if (!active || active.instance !== instance) return;
        active.inSetup = false;
        if (ok === false) {
          say('game ' + cls.gameKey + ' setup() declined - back to campus');
          showBoard();
          return;
        }
        beginPlay();
      }, (e) => go('rejected (' + ((e && e.message) || e) + ') - starting anyway'));
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
        onDone: () => { if (active) active.enrollEl = null; runSetup(); },
        log: say,
      });
      if (!active.enrollEl) runSetup();
    } else {
      runSetup();
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
    if (onBoard) return Object.assign({}, onBoard);
    const d = descriptors(games.list).find((x) => x.key === gameKey)
      || { key: gameKey, family: 'comfort', meaty: false };
    return {
      gameKey,
      family: d.family || 'comfort',
      meaty: !!d.meaty,
      clockless: !!d.clockless,
      homeroom: false,
      timeLabel: '',
      /* THE DESCRIPTOR'S OWN BUDGET, not zero and not a ceiling. Two callers,
       * and only one of them is a swim: `startFreeSwim` passes {endless:true}
       * and startClass zeroes the budget itself, so an untimed swim is untimed
       * whatever is written here - but THE EARNED DOOR and the dev door start
       * this object as an ordinary GRADED, TIMED class, and they used to land
       * on startClass's `|| QUICK_MAX_SEC` fallback. That was already wrong
       * (Daily Trigger ran 180s through the unlock door) and the class-length
       * wave made it wronger by moving the quick ceiling to 300. Budgets are
       * per-module now, so the door runs the module's own class length. */
      timeBudgetSec: Number(d.timeBudgetSec) > 0 ? Number(d.timeBudgetSec) : 120,
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
     * do not talk over each other.
     * EMI COLOR (2026-08-24): a C is 'fail' now, not 'miss' - the rage chain and
     * the its-my-fault pool were written for "the class went badly" and never
     * fired, while 'miss' is reserved for the mid-class stumble seam (ctx.mood)
     * whose "one wrong answer, one dropped tile" register it always carried.
     * The payload grew gameKey + perfect: the per-game colour pools key on the
     * first, and the perfect-gated dork line was unreachable without the second. */
    lastGraded = { grade: graded.grade, perfect: allDone(), fresh: true };
    /* ASKS: `newBest` is impulse-control's, reported additively on its own
     * endClass frame (trap 54's spirit), and it is what a11's dare resolves
     * against. Every other class simply never carries it. */
    const endMoment = /^[sab]$/i.test(String(graded.grade)) || graded.grade === 'pass' ? 'win' : 'fail';
    const endPayload = {
      grade: graded.grade, streak: store.streak().count,
      gameKey: cls.gameKey, perfect: allDone(),
      newBest: r && r.newBest === true,
    };
    fireMoment(endMoment, endPayload);

    /* ASKS: THE DARE RESOLVES ON THE CLASS'S OWN END, and the answer rides the
     * frame that already pays for the class. A read of one session flag: no
     * dare armed answers null for ever, and the host awards nothing. */
    let dareWon = null;
    try {
      const e = getEmi();
      if (e && e.asks && typeof e.asks.classResult === 'function') {
        dareWon = e.asks.classResult(endMoment, endPayload);
      }
    } catch (e) { dareWon = null; }

    const endedFrame = {
      type: 'class-ended',
      gameKey: cls.gameKey,
      gradeTier,
      grade: graded.grade,
      zen: graded.zen,
      flavorXp,
      dayUtc: utcDateSeed,
    };
    // Additive: an older host ignores it, and a page with no dare never sets it.
    if (typeof dareWon === 'string' && dareWon) endedFrame.dareWon = dareWon;
    bridge.send(endedFrame);

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
        // Read the row BEFORE the write: a retake of the last class finds the
        // day already complete, and the bell rings once per LOCAL day.
        const dayWasDone = !!(store.day(localDate) || {}).complete;
        store.completeDay(localDate, { classCount: timetable.classes.length, perfect: true });
        // EMI SEAM: that was the last class of the night.
        if (!dayWasDone) { try { fireMoment('dayDone', { classes: timetable.classes.length }); } catch (e) { /* noop */ } }
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
  /* ---------------------- the rotate freeze -----------------------------
   * The turn-your-phone card covers the class, so the clock and the game behind
   * it have to stop: grading a player on seconds they could not see would be the
   * shell making the flow of time lie, which is the one thing the class clock
   * exists to prevent.
   *
   * IT IS NOT pauseClass, DELIBERATELY. pauseClass mints the Paused card with its
   * own Resume / Settings / Leave buttons, and a card stacked under a card the
   * player cannot reach is exactly the two-dialog race trap 29 was written about.
   * This is the quiet half of the same funnel - the bar and the game instance and
   * nothing else - and it REFUSES to act while the class is already frozen for a
   * reason of its own (a real pause, a host suspend), so lifting the gate can
   * never resume something the player stopped on purpose.
   * -------------------------------------------------------------------- */
  let orientFrozen = false;
  function orientFreeze(on) {
    const want = !!on;
    if (!active) { orientFrozen = false; return; }
    if (want === orientFrozen) return;
    /* THE GUARD RUNS IN BOTH DIRECTIONS. Refusing to freeze an already-frozen
     * class is the obvious half; the half that bites is the LIFT. A host suspend
     * can land while the card is up, and un-freezing then would call resume() on
     * a class the host stopped - the card would come down and the game would
     * carry on playing behind a suspend overlay. Dropping the bookkeeping without
     * resuming hands the class back to whoever actually owns its pause. */
    if (active.paused || active.suspendEl) { orientFrozen = false; return; }
    orientFrozen = want;
    timeBarSet(!want);
    try { want ? active.instance.pause() : active.instance.resume(); }
    catch (e) { say('rotate freeze threw: ' + ((e && e.message) || e)); }
  }

  function pauseClass(on) {
    if (!active) return;
    active.paused = !!on;
    // THE ONE FUNNEL every freeze walks through - the pause card, the settings
    // screen and applySuspend(true) all land here, so the class clock only has
    // to be hooked once. (applySuspend(false) deliberately leaves the class
    // paused behind its Resume button, and that button comes back through here.)
    if (on) {
      timeBarSet(false);
      try { active.instance.pause(); }
      catch (e) { say('game pause/resume threw: ' + ((e && e.message) || e)); }
      try { active.peek.forceHide(); } catch (e) { /* noop */ }
      if (!active.pauseEl) {
        const overlay = el('div', 'arc-suspended');
        overlay.appendChild(el('h2', 'arc-h2', 'Paused'));
        const bar = el('div', 'arc-classbar');
        const resume = el('button', 'btn primary', 'Resume');
        resume.type = 'button';
        resume.addEventListener('click', () => pauseClass(false));
        /* THE MISSING DOOR (owner ruling 2026-08-24). The topbar gear is hidden
         * while a class is up, so until now NO path on the class stage reached
         * settings at all. The pause card is the natural place: it is already
         * the freeze state showSettings() induces, and onClose walks straight
         * back here. The running class's key scopes the page to this room. */
        const options = el('button', 'btn ghost', t('settings', 'Settings'));
        options.type = 'button';
        options.addEventListener('click', () =>
          showSettings(active && active.cls ? active.cls.gameKey : null));
        const leave = el('button', 'btn ghost', t('leave_class', 'Leave class'));
        leave.type = 'button';
        leave.addEventListener('click', () => showBoard());
        bar.appendChild(resume); bar.appendChild(options); bar.appendChild(leave);
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
    } else {
      /* THE LIFT, AND THE ONE PIECE OF SEQUENCING IN THE CLASS-SIDE KIT.
       * The card comes down FIRST and unconditionally: the door the player just
       * pressed must open at once, whatever paints behind it. Then the clock and
       * the game go back together, through `resumeAfterSlate` - which runs them
       * on the Resume Slate's LAST FRAME when the seep took the beat, and right
       * here, in this same tick, when it did not (seven resumes in eight).
       * THE CLOCK IS INSIDE THE THUNK on purpose: a slate that held the game
       * while the class clock ran would cost the player a tenth of a second of
       * graded time, and a tell may never cost a timing window. */
      if (active.pauseEl) {
        active.pauseEl.remove();
        active.pauseEl = null;
      }
      const cls0 = active;
      resumeAfterSlate(() => {
        /* torn down, or re-paused while the slate was up: not ours any more */
        if (active !== cls0 || cls0.paused) return;
        timeBarSet(true);
        try { cls0.instance.resume(); }
        catch (e) { say('game pause/resume threw: ' + ((e && e.message) || e)); }
      });
    }
  }

  /**
   * TELL 14, THE RESUME SLATE. About one resume in eight the seep wants a tenth
   * of a second of `FEED nn - SYNC` before the game comes back, and the law is
   * that INPUT RE-ARMS ONLY AFTER THE SLATE CLEARS. `engine.deadBeat('resume')`
   * is the whole mechanism: it paints on the engine's own fx layer (which is
   * pointer-events:none by construction, so the slate cannot eat the press even
   * if it were mistimed) and runs `onClear` on the tell's last frame, after the
   * pixels are gone and the director's claim is released.
   *
   * A null answer - the common one - means the lift runs here, synchronously,
   * and the resume is byte-for-byte what it always was.
   */
  function resumeAfterSlate(lift) {
    if (!active) return;
    let held = null;
    try {
      held = (active.engine && typeof active.engine.deadBeat === 'function')
        ? active.engine.deadBeat('resume', {
          payload: { gameKey: active.cls && active.cls.gameKey },
          onClear: lift,
        })
        : null;
    } catch (e) { held = null; }
    if (!held) lift();
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

  /* ==========================================================================
   * ASKS: THE EXIT AIM (wave EMI ASKS, 2026-08-25)
   *
   * a15 is "bed?", and the whole point of it is that it lands while the player
   * is REACHING for the way out - not once they have already committed. The
   * ladder's own `exitIntent` is 450ms into a 1200ms Esc hold (boot.js), which
   * is the wrong end of the gesture for a question: the window is already
   * closing behind it. So this is a second, EARLIER and much softer signal.
   *
   * THE FENCE, and it is the feature (EMI-VOICE-LOCK): this NEVER blocks,
   * delays, cancels or even observes the exit. It is a `pointermove` in the
   * BUBBLE phase with `{passive:true}` that does exactly one thing once per
   * sitting - fire a moment when the cursor reaches the corner the host
   * window's close button lives in. It calls no preventDefault, adds no rung
   * to the Esc ladder, registers no `beforeunload`, and if EMI is not mounted
   * `fireMoment` is a silent no-op like every other call site.
   * ======================================================================== */
  const EXIT_AIM_W = 200;         // px of the top-RIGHT corner that counts
  const EXIT_AIM_H = 120;
  let exitAimSpent = false;
  function onExitAim(ev) {
    if (exitAimSpent || !ev) return;
    /* Not while a class is up: she may not stop a board to ask a question, and
     * `asks.js` would refuse it anyway - this is the cheaper of the two nos. */
    if (screen === 'class' || active) return;
    let vw = 1280;
    try { if (typeof window !== 'undefined') vw = Number(window.innerWidth) || vw; } catch (e) { /* noop */ }
    const x = Number(ev.clientX);
    const y = Number(ev.clientY);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return;
    if (y > EXIT_AIM_H || x < vw - EXIT_AIM_W) return;
    exitAimSpent = true;
    try { fireMoment('exitAim', { reason: 'corner' }); } catch (e) { /* noop */ }
  }
  if (typeof document !== 'undefined' && document.addEventListener) {
    document.addEventListener('pointermove', onExitAim, { passive: true });
  }

  /* ASKS: a01's YES buys a SOFT night - comfort faces on arrival and one extra
   * line on the report card. The flag lives in the ask engine (it is
   * session-only and it is hers); this is the shell's read of it, and a page
   * with no EMI, no asks module or no answer all read the same false. */
  function askSoft() {
    try {
      const e = getEmi();
      return !!(e && e.asks && e.asks.flags && e.asks.flags.soft);
    } catch (e) { return false; }
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
    // NO GHOSTS UNDER A MANDATORY VIDEO. The layer rides the same one funnel the
    // pause card does, and it is a LEVEL (trap 28), so both edges are written.
    if (ghosts) { try { ghosts.setPaused(!!on); } catch (e) { /* noop */ } }
    /* AND NO BEAT UNDER ONE EITHER. A RUNNING beat is ENDED rather than paused:
     * the card lands, `seenAt` is banked, and it never replays after the video
     * (trap 28's spirit - a beat that came back afterwards would be a beat the
     * player watched twice). skip() refuses an IDLE beat on purpose - one still
     * waiting behind the splash or FIRST BELL has shown nothing yet, so it
     * simply stands down (onSplashDone's `beat` checks this flag) and the LIFT
     * edge below hands it its stage back. start() is state-guarded, so a beat
     * that already ran, skipped or never existed makes the lift a no-op. */
    if (on && orientation) { try { orientation.skip(); } catch (e) { /* noop */ } }
    if (!on) maybeStartOrientation();
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
      } else {
        if (active.suspendEl) {
          active.suspendEl.remove();
          active.suspendEl = null;
        }
        /* THE PILL IS A LEVEL, SO BOTH EDGES ARE WRITTEN (trap 28). The
         * re-enable used to hang off the overlay still being up, so a suspend
         * that had already lost its card (a screen change, a teardown race, a
         * second suspend:false frame) left the way home greyed out for the rest
         * of the class with nothing on screen to explain why. */
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
    // The freeze belongs to the class, not to the page: leaving it armed would
    // have the NEXT class start life believing it was already stopped.
    orientFrozen = false;
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
    /** {type:'annex-stats'} - the host's answer to the registry link. Resolves
     *  the one pending request; an unsolicited frame is dropped on the floor. */
    onAnnexStats(m) {
      const w = annexStatsWait;
      if (!w) return;
      annexStatsWait = null;
      try { clearTimeout(w.timer); } catch (e) { /* noop */ }
      w.resolve(m && m.body && typeof m.body === 'object' ? m.body : null);
    },

    /** {type:'setting'} post-clamp echo. THE only path that moves a setting. */
    onSetting(m) {
      if (!m || typeof m.key !== 'string') return;
      // Keep the flat bag current so a class started later sees the new value.
      // Only per-game keys live in the bag; a global echo would shadow one.
      if (src.settings && typeof src.settings === 'object' && !isGlobalSettingKey(m.key)) {
        src.settings[m.key] = m.value;
      }
      if (m.key === 'motionLevel') applyMotionLevel(m.value);
      if (settingsPage) { settingsPage.noteEcho(m.key, m.value); settingsPage.applyEcho(m.key, m.value); }
      if (m.key === SETTING_KEYS.keybinds) keybinds.applyEcho(m.value);
      /* THE PHOTO CHIP IS THE `presenceShare` DISCORD RUNG (owner ruling 1), so
       * the settings page and the student ID move together whichever one was
       * pressed - and BOTH of them paint from this echo and never from a click
       * (trap 1). The avatar shows only at `discord`, which is the whole
       * consent matrix in one line. */
      if (m.key === SETTING_KEYS.presenceShare) {
        const before = profile.presenceShare;
        profile.presenceShare = idRung(m.value, profile.presenceShare);
        clearIdLinkTimer();
        idChipWait = null;
        paintIdProfile();
        if (profile.presenceShare === 'discord' && before !== 'discord'
          && profile.discordLinked && profile.avatarUrl) runIdPhotoDay();
      }
    },

    /**
     * {type:'profile'} - the host's word on who you are (STUDENT-ID contract).
     * ADDITIVE: a host that predates it never sends one and the card keeps the
     * stand-in portrait it was built with. This frame is the ONLY thing that
     * moves the name, the photo or the linked flag - the page derives none of
     * them, and no Discord id or CDN url is ever on it (PRESENCE.md §10).
     *
     * `result` is what the chip was waiting for:
     *   'linked'    the link-up succeeded and the host applied the rung itself
     *               (owner ruling 2 - one click was the consent). PHOTO DAY.
     *   'failed'    one toast, and the chip goes back to the rung it was on.
     *   'cancelled' the chip goes back, silently. Nothing was promised.
     */
    onProfile(m) {
      const p = (m && m.profile && typeof m.profile === 'object') ? m.profile : null;
      if (p) {
        profile = {
          name: (typeof p.name === 'string' && p.name) ? p.name : null,
          avatarUrl: (typeof p.avatarUrl === 'string' && p.avatarUrl) ? p.avatarUrl : null,
          discordLinked: !!p.discordLinked,
          presenceShare: idRung(p.presenceShare, profile.presenceShare),
        };
      }
      clearIdLinkTimer();
      idChipWait = null;
      paintIdProfile();
      const result = m && typeof m.result === 'string' ? m.result : null;
      if (result === 'linked') {
        runIdPhotoDay();
        /* ONE line, once per link. The beat is on the card; the toast is what
         * says it happened when the card is only 236px of furniture. */
        try { shout(t('id_photo_day', 'Photo day')); } catch (e) { /* noop */ }
      }
      else if (result === 'failed') {
        try { shout(t('id_photo_failed', 'Discord did not pick up. Try again in a minute.')); }
        catch (e) { /* a toast may never hold a door */ }
      }
      say('profile frame' + (result ? ' (' + result + ')' : '')
        + ': ' + (profile.discordLinked ? 'linked' : 'not linked') + ', ' + profile.presenceShare);
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

    /**
     * THE SPLASH IS DOWN (boot.js, trap 66's happy path only). The campus is
     * already painted underneath with `animate:false` and has not been touched
     * yet, which is exactly the frame FIRST BELL's cold open wants. Idempotent,
     * guarded, and a no-op for everyone except a genuine first night - and the
     * failBoot path never calls it at all, so an error card is never held up by
     * a scene (the same law that keeps the splash itself off that path).
     */
    onSplashDone() {
      if (splashSpent) return;
      splashSpent = true;
      /* THE BELL AND ORIENTATION DAY BOTH RIDE THIS ONE FUNNEL, deliberately
       * not two: `splashDone` calls back exactly once whether the opening
       * played, stood down or threw (trap 76). The continuation pays BOTH
       * debts - the bell's latch (maybeFirstBell is latched, a double call is
       * free) and the stage-clear that lets `maybeStartOrientation` re-check
       * everything else (campus revealed, live screen, no suspend), so a beat
       * can never start under a curtain from ANY seam. */
      const cleared = () => {
        bellSplashCleared = true; maybeFirstBell();
        stageClear = true; maybeStartOrientation();
      };
      try { if (vn) { vn.splashDone(cleared); return; } }
      catch (e) { say('first bell cold open skipped (' + ((e && e.message) || e) + ')'); }
      cleared();
    },

    /** {type:'suspend'} */
    onSuspend(m) { applySuspend(!!(m && m.on), m && m.reason); },

    /** {type:'meta'} is consumed by the store; this just repaints the chrome. */
    onMeta() { renderTopbar(); if (screen === 'board') showBoard({ silent: true }); },

    /**
     * One rung of the Esc ladder. Returns true when the shell consumed it, so
     * boot.js only reaches for fullscreen/exit once the page has nothing to close.
     */
    /** THE SEEP's director, for the play-test rig and the suites. Null when it
     *  failed to build. Nothing in the shipped shell reads this. */
    seepDirector() { return seep; },
    escapeStep() {
      // THE CONFIRM IS THE INNERMOST RUNG. It is a modal the player opened one
      // press ago, so Esc closing it first is the only answer that is not a
      // surprise - and it is the ONE rung this wave added. Everything below is
      // byte-for-byte the ladder it always was (trap 29's corollary).
      // dismissConfirm carries the undo: it unfreezes a class the pill froze,
      // and hands a class that was ALREADY paused its pause card back.
      /* THE ANNEX REVEAL is the topmost thing that can possibly be up (z 48,
       * input-blocking): while it runs, nothing beneath it can hold a modal
       * the player opened more recently. One new rung, at the top (trap 48).
       * Esc SKIPS the beat - it never un-sees it (seen stamps at mount). */
      if (annexStage) {
        try { annexStage.skip(); } catch (e) { dismissAnnexStage(); }
        return true;
      }
      /* THE POST OVERLAYS (mailbox / noticeboard / Bugle, z 38): modals the
       * player opened one press ago, over the board only, so they close before
       * anything beneath them (trap 48). They can never be up alongside a
       * class modal - opening them is a campus-chrome click - so these rungs
       * cost the rest of the ladder nothing. */
      if (isMailboxOpen()) { closeMailbox(); return true; }
      {
        const up = currentCorkboard();
        if (up && !up.closed) { try { up.close(); } catch (e) { /* noop */ } return true; }
      }
      {
        const up = currentBugle();
        if (up && !up.closed) { try { up.close(); } catch (e) { /* noop */ } return true; }
      }
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
      /* ORIENTATION DAY, WHILE IT IS ON STAGE AND NEVER OTHERWISE. Esc during
       * the beat means "yes, fine, give me the card" - it lands instantly, the
       * beat is banked as seen, and the press is spent. `active()` is false the
       * rest of the night, so this is a rung that EXISTS for ~6 seconds once
       * ever and adds nothing permanent to the ladder (traps 29/48). It sits
       * above the door card because a beat is the newer thing on screen; in
       * practice they cannot both be up, since the click that opens a door card
       * has already aborted the beat. */
      if (orientation && orientation.active()) {
        try { orientation.skip(); } catch (e) { /* noop */ }
        return true;
      }
      // The campus door card is the outermost thing a tap can close.
      if (screen === 'board' && campus) {
        try { if (campus.closeCard()) return true; } catch (e) { /* noop */ }
      }
      if (screen === 'settings') {
        if (settingsPage) { try { settingsPage.destroy(); } catch (e) { /* noop */ } settingsPage = null; }
        /* Esc is the page's other Back, so it owes the same route home - a
         * settings page opened from a room folds back into the room, not past
         * it to the campus. Same one-shot spend, same order of precedence. */
        const back = settingsBack;
        settingsBack = null;
        if (active) showClassScreen();
        else if (back) back();
        else showBoard();
        return true;
      }
      // THE ID SPOTLIGHT is the same shape as the Records one and sits one rung
      // ABOVE it: the card is a modal the player opened one press ago, and it
      // is not tied to a screen (it lifts off the campus). Trap 48's shape -
      // one rung, and everything below it is the ladder it always was.
      if (dismissIdCard(false)) return true;
      // THE OFFICE FOLDS INWARD-OUT, the annex's shape one room over. The
      // room's own escapeStep runs the ladder in the order the player built it:
      // the SPOTLIGHT first (a card lifted off the wall one press ago - trap
      // 48), then the chassis's fold (the card panel, then a close-up back to
      // the wide shot), and FALSE at the wide shot so the rung below walks out
      // of the building. No key is bound anywhere down there; this asks.
      if (screen === 'records' && recordsRoom) {
        try { if (recordsRoom.escapeStep()) return true; } catch (e) { /* noop */ }
      }
      // The Records Office is a screen like settings: Esc walks back to campus.
      if (screen === 'records') { showBoard(); return true; }
      // A ROOM SCENE is a screen like records: Esc walks back to campus. Its
      // hotspots are buttons, not modals - the room owns no inner rungs.
      if (screen === 'room') { showBoard(); return true; }
      // THE ANNEX folds inward-out (trap 48's shape, one ladder both sides of
      // the seam): the lab's own rungs first - paper down, OS window shut,
      // laptop closed, close-up stepped back - then the stairs walk home to
      // the office, never straight to campus. You leave the way you came in.
      if (screen === 'annex' && annexPage) {
        try { if (annexPage.escapeStep()) return true; } catch (e) { /* noop */ }
      }
      if (screen === 'annex') { showRecords(); return true; }
      // A host suspend owns the screen. The panic ladder's second press (host
      // side) is the exit, and walking to the board here would destroy the
      // Resume card ~60ms after it appeared (both ladders fire on one Esc -
      // verified live 3/3). Consume the press and change nothing; the overlay's
      // own Resume / Leave class buttons remain the page-side way out.
      if (active && active.suspendEl) return true;
      /* A SETUP DOOR IS NOT A CLASS YET (S3). Nothing has started, so "pause"
       * would be a card over a question - the honest reading of Esc here is
       * "let me out", which is the pill's own confirm, asked from the one place
       * it is ever asked. Cancel puts the door back exactly as it was. */
      if (active && active.inSetup && !active.confirmEl) { askLeaveClass(); return true; }
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
      dismissAnnexStage();
      dismissIdCard(true);
      clearIdLinkTimer();
      idSpotlight = null;
      disarmPunch();
      teardownClass();
      /* the lab (and EMI's bracket) cannot outlive the shell */
      if (annexPage) {
        const ap = annexPage;
        annexPage = null;
        try { ap.destroy(); } catch (e) { /* noop */ }
        if (annexEmiPrev) {
          annexEmiPrev = false;
          try { const emi = getEmi(); if (emi && emi.setEnabled) emi.setEnabled(true); } catch (e) { /* noop */ }
        }
      }
      if (annexStatsWait) {
        try { clearTimeout(annexStatsWait.timer); annexStatsWait.resolve(null); } catch (e) { /* noop */ }
        annexStatsWait = null;
      }
      setStage(null);
      /* ASKS: the exit-aim listener is the shell's, so the shell takes it. */
      if (typeof document !== 'undefined' && document.removeEventListener) {
        try { document.removeEventListener('pointermove', onExitAim); } catch (e) { /* noop */ }
      }
      if (orientation) { const ob = orientation; orientation = null; try { ob.destroy(); } catch (e) { /* noop */ } }
      if (ghosts) { try { ghosts.destroy(); } catch (e) { /* noop */ } ghosts = null; }
      if (campus) { try { campus.destroy(); } catch (e) { /* noop */ } campus = null; }
      if (walker) { const w = walker; walker = null; try { w.destroy(); } catch (e) { /* noop */ } }
      // END OF RUN: the residue is tonight's, and tonight is over (§2.4).
      residueTrail = [];
      lastRoomKey = null;
      if (vn) { try { vn.destroy(); } catch (e) { /* noop */ } vn = null; }
      if (seep) { try { seep.destroy(); } catch (e) { /* noop */ } seep = null; }
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
    /* `assets` + `settings` are THE OFF CHANNELS' media seam (W3): NOW WATCHING
     * draws through the provider the same way a class does (the host fetches,
     * the page never does) and falls back to `init.settings.localAssets`. Both
     * are optional to her - absent means the channel is absent. */
    if (emiLayer) mountEmi({ layer: emiLayer, store, toast: shout, log: say, assets, settings: src.settings });
  } catch (e) { say('EMI failed to mount (the shell is unaffected): ' + ((e && e.message) || e)); }

  /* FIRST BELL. Built here, BEFORE the first showBoard(), for the same reason
   * EMI is: the controller warms its plates while the campus paints, so nothing
   * waits on a decode when boot.js hands the splash edge over. Nothing plays at
   * construction - `onSplashDone` above is the only thing that starts a scene,
   * and on any night but the first the controller banks its whole ledger and
   * never speaks. A throw here costs the opening and nothing else. */
  try {
    vn = createFirstBell({
      store,
      // The SAME rows the campus board deals, so the wall over the admissions
      // desk and the board behind the plaque can never disagree.
      rows: () => buildRows(true),
      firstNight: isFirstNight,
      // A slip may not land over live play; the mail defers (and is spent).
      canInterrupt: () => !active && screen !== 'class',
      // EMI's ONE verb, injected. vn/ imports nothing from emi/ (trap 60).
      onMoment: (name, payload) => fireMoment(name, payload),
      reducedMotion,
      log: say,
    });
    if (vn) say('first bell: ' + (vn.armed ? 'armed' : 'nothing left to play'));
  } catch (e) { say('first bell unavailable (the shell is unaffected): ' + ((e && e.message) || e)); }

  showBoard();
  return api;
}

export default createShell;
