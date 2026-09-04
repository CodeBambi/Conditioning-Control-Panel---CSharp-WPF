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

import { t, setLexicon, tierLabel, gradeLabel } from '../core/lexicon.js';
import { makeRng } from '../core/rng.js';
import { dayVocabulary } from '../core/vocab.js';
import { buildTimetable, dayAdd } from '../core/timetable.js';
import { gradeClass, capsRaised, isSPlus, gradeKey } from '../core/grades.js';
import { createStore } from '../core/store.js';
import {
  loadGames, descriptors, tierFor, tierForPromotions, advance, suspendedStub, MAX_TIER,
  DISCORD_COMMAND,
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
import { createPrizeCounter } from './prizecounter.js';
import { createPrizeBooth } from './prizebooth.js';
/* THE LOCKER (Locker wave). The room where a bought cosmetic is actually worn.
 * `equipFromToast` is the second half of a purchase: the counter's receipt
 * offers one press and this is what answers it, so the shell never has to know
 * which key an outfit, a frame, a desk toy or a campus look is written to. */
import { createLocker, showLocker, equipFromToast, installLocker } from './locker.js';
/* THE PURCHASE REVEAL (Locker wave). The beat that answers "what did I just
 * buy" rather than "did it go through". It hangs off the `arcademy-bought`
 * event, which this file fires from the wallet echo and nowhere else, and it
 * owns its own body-level overlay - the shell hands it narrow caps and asks for
 * exactly one thing back, the top rung of the Esc ladder. */
import { installReveal, revealEscape, fireBought, kindOf } from './reveal.js';
/* Only the hand-over clock. counterfx.js is the counter's motion kit; this is
 * the one number the page outside the counter needs from it - how long the
 * money is still visibly in the air. */
import { boughtHoldMs } from './counterfx.js';
/* CAMPUS LOOK (COUNTER STOCK). themes.js is a pure TABLE (no DOM, no store);
 * themefx.js is the weather canvas and the school's newest render surface. The
 * shell owns the meta key, the ownership question and the order of application
 * - those three are exactly what neither module may reach. */
import {
  THEME_META_KEY, STANDARD_ID, themeById, ownedThemes, clampThemeId, themeFxFor,
} from './themes.js';
import { createThemeFx } from './themefx.js';
/* THE BRASS BELL (COUNTER STOCK). audio.js is built in boot.js; this is its one
 * module-level seam - the shell hands it an ownership GETTER, audio.js still
 * imports nothing (trap 18's discipline). */
import { setBellCosmetic } from './audio.js';
/* THE PA PACK (COUNTER STOCK). pa.js owns two timers and a seeded plan; every
 * cue leaves by the one audio door. It dispatches its own cue (bus 'voice',
 * maxMs 8000, a duck) - the shell only builds it and feeds it moments. */
import { createPa } from './pa.js';
/* ...AND ITS CAPTION (owner, 2026-08-28: the announcer wants words on screen).
 * A SEPARATE module because pa.js renders nothing by law. It hears the same
 * `arcademy-sfx` cue the mixer hears, so the shell's whole job is to build it
 * and to keep EMI off it (campusDoorRects, below). */
import { installPaCaption } from './pacaption.js';
/* THE SOUNDTRACK. ost.js is a table and two verbs; it holds one keyed slot on
 * the `music` bus through the same door the beds use. The shell tells it where
 * the player is (campus, records, a class) and clearScreen tells it they left. */
import { createOst } from './ost.js';
import { createIdSpotlight, idReducedMotion, studentNumber } from './idcard.js';
import { createAccountChip, readAccount } from './accountchip.js';
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
/* THE TIME CAPSULE: the trophy case in the entrance hall. An overlay, never a
 * screen - the campus offers the press, the shell mints the case. */
import { openCapsule, currentCapsule, capsuleDoor, CAPSULE_NIGHTS } from './capsule.js';
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

/** A plain document event, the glue of the Locker wave (0828): the counter
 *  and the Locker say what happened and emi/shop.js listens. Same shape as
 *  `sfx` above, same rule - a dropped event is not an error. */
function docEvent(name, detail) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor(String(name), { detail: detail || {} }));
  } catch (e) { /* noop */ }
}

/** Screen depth, so a swap knows which way it went. An ORDER, not a router -
 *  the router is `screen` and it stays exactly where it was. */
/* `prizebooth` sits at the counter's own depth and there is no rung under it any
 * more: the shelf used to be a screen one deeper and it is a PANEL inside the
 * booth now (the Records Office's arrangement, one alley over), so `prizes` is
 * not a screen this router can ever be on. A depth for a screen that cannot
 * exist is a line that reads as a route somebody could still take. */
const SCREEN_DEPTH = Object.freeze({ board: 0, room: 1, records: 1, prizebooth: 1, locker: 1, backroom: 1, annex: 2, report: 2, settings: 3, class: 4 });

/** Walk targets EMI never remarks on arriving at. The three office doors are
 *  voice.js's geofence read from the other end: she is silent on the Records
 *  side of them anyway, so a `campus.walkArrived` there would be a line spent
 *  on the last step before she is switched off (heartbeat wave, 2026-08-25). */
const SILENT_WALK_TARGETS = new Set(['records', 'registrar', 'annex']);

/* THE BACK ROOM'S WIRE, AND EXACTLY THIS MUCH OF IT (BACKROOM-CONTRACT §3).
 * The wing is a page like any other page in this bundle and it is under the
 * annex's law: it imports no bridge, so it cannot post a frame the shell has
 * not agreed to carry. These two lists ARE the agreement. A room that asks for
 * anything else is not refused quietly - it is logged and dropped, because the
 * only reason a room would ask is that somebody has widened its contract
 * without widening this line, and that should be visible in the console rather
 * than at a bank balance. */
const BACKROOM_SENDS = Object.freeze(['casino-request', 'triggers-request']);
const BACKROOM_HEARS = Object.freeze(['casino-result', 'triggers-result']);

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
 *
 * SINCE 2026-08-25 this gif pool is the FLOOR, not the default: pickClassLoom
 * (below) weaves the class spiral with the vendored Loom generator, and the
 * gif pick survives as the wrapper's `href` (the no-WebGL fallback) and as the
 * whole answer when the generator module is unavailable.
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

/* ----------------------------------------------------------------------------
 * THE GENERATED LOOM (owner directive, 2026-08-25: "on ALL the games the
 * spirals should be generated with the Loom"). The class spiral is now WOVEN
 * per class by the vendored Loom generator (engine/loom/seeded.js) instead of
 * dealt from the gif pool - the engine's wash routes a params WRAPPER to a
 * live shader canvas (engine/loomWash.js). The wrapper's shape is the page's
 * contract with every consumer:
 *
 *   { loom: true,
 *     id:     'loom:xxxxxxxx'   stable hash of the normalized params - the
 *                               LEDGER id a class records and quizzes on,
 *     params: <loomField v2>,   what the shader draws,
 *     href:   <bundled gif url> the WebGL floor AND the paintable url for any
 *                               consumer that needs a static image (a pin, a
 *                               legacy string-path wash),
 *     toString() -> id }        so accidental string coercion prints the id
 *
 * POLICY: with SAVED user looms (init.settings.loomSpirals), a seeded coin
 * gives the owner's own hand-woven gifs half the classes and the generator the
 * other half; with none, every class weaves. The `href` fallback is the OLD
 * pick (pickSpiralUrl, its own '|spiral' stream, untouched), so a no-WebGL
 * player sees exactly the class spiral they always saw. All new dice ride the
 * NEW '|loom' tagged stream - the append-only stream law; nothing existing
 * moved a pick for any seed. `loomSeed` is the shell's dynamically-imported
 * {seededParams2, loomId} pair, null when the module refused to load - and
 * null simply hands the class the old gif pick.
 */
function pickClassLoom(seed, settings, loomSeed) {
  if (!loomSeed || typeof loomSeed.seededParams2 !== 'function'
    || typeof loomSeed.loomId !== 'function') return null;
  try {
    const rng = makeRng(String(seed == null ? '' : seed) + '|loom');
    const userRows = loomSpiralRows(settings);
    // the seeded coin: hand-woven half the time, when there is a hand to honour
    if (userRows.length && rng() < 0.5) {
      const row = userRows[Math.floor(rng() * userRows.length)];
      return row && typeof row[0] === 'string' ? row[0] : null;
    }
    /* centerpiece:false - the live wash shader draws no centerpiece, and the
     * quiz thumbnails (composeFrame) must show the same picture the wash wore */
    const params = loomSeed.seededParams2(rng, { centerpiece: false });
    const id = loomSeed.loomId(params);
    return Object.freeze({
      loom: true, id, params,
      href: pickSpiralUrl(seed, settings),
      toString() { return id; },
    });
  } catch (e) { return null; }
}
/** The generated loom's weight among the class's OFFERED spirals (ctx.spiralPool):
 *  level with sp6, the pool's current top row - prominent, never dominant. */
const LOOM_GEN_WEIGHT = 34;

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

/** Palette keys we accept from init.palette, and the CSS token each one drives.
 *
 *  COUNTER STOCK grew this map by five (`panel2`, `inkDim`, `inkFaint`,
 *  `slate`, `pinkDeep`) so a CAMPUS THEME can move the FULL set through the one
 *  seam a mod skin already uses. Every hue in styles.css - the campus plan, the
 *  counter's shelves, the report card - is a color-mix off these thirteen, so
 *  thirteen is the whole surface. Append-only: a mod that writes the old seven
 *  is unchanged, and an unknown key is still logged and ignored. */
const PALETTE_TOKENS = Object.freeze({
  ground: '--ground', navy: '--navy', panel: '--panel', ink: '--ink',
  accent: '--pink', accent2: '--lav', gold: '--gold',
  // COUNTER STOCK additions - the rest of the token plan
  panel2: '--panel2', inkDim: '--ink-dim', inkFaint: '--ink-faint',
  slate: '--slate', pinkDeep: '--pink-deep',
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

  /* THE LITE RUNG'S DEVICE HALF (mobile dig 2026-08-30). Every shell surface
   * ships a diet twin (`.arm-lite`, `is-lite`, `asc-lite`: rooms.css holds the
   * breath, alleysign.css stops the bloom and the chase, prizecounter.css
   * drops the lamps, scene.js skips the dust canvas, reveal/walk thin their
   * particles) - but `lite` only ever read `performanceMode`, a desktop dial
   * the phone never sets, so every one of those twins was dead code exactly
   * where it was written for. The device half reads core/device.js's GLOBAL
   * arm marker (`data-ae-touch-global`), NOT the bare `ae-touch` class: The
   * Deep End arms that class per-class on desktop too, and a desktop that
   * enters The Deep End must not walk out into a lit-down school. The marker
   * is stamped once, at boot, on the mobile verdict only - desktop WebView2
   * never carries it, so this reads false there on every ask. */
  const shellLite = () => {
    if (src.performanceMode) return true;
    try {
      const html = typeof document !== 'undefined' ? document.documentElement : null;
      return !!(html && typeof html.getAttribute === 'function'
        && html.getAttribute('data-ae-touch-global') === '1');
    } catch (e) { return false; }
  };

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
    /* THE WEATHER LAYER IS A DECORATION, so Motion owns it outright: turning
     * motion down takes the canvas away entirely (not a paused one - the nodes
     * stop existing), and turning it back up rebuilds it with the new flags.
     * This is the ONE echo that can move the layer without a screen change. */
    try { syncThemeFx(); } catch (e) { /* the layer is never the thing that throws */ }
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
  /* THE FLOOR UNDER THE FLOOR (core/vocab.js). With nothing enabled in the
   * player's pool - or a creator mod that ships none - init.words arrives empty
   * and the word layer simply stops existing. dayVocabulary() lends the school's
   * own 24-word vocabulary in that one case and hands the host's list straight
   * back in every other, so a configured day is byte-identical. src.words is
   * never mutated: init is the host's frame and other readers index it. */
  const dayVocab = dayVocabulary(src.words, makeRng(utcDateSeed + '|vocab'));
  const dayWords = dayVocab.words;
  const wordSource = dayVocab.source;      // 'host' | 'house', for the settings row
  let absorbed = 0;

  /* THE DAY'S TRIGGERS: init.triggers = [{text, audio}] - the same phrases as
   * init.words, each with its whisper clip url (ccp.subaudio / ccp.modaudio) or
   * null. Frozen, never absorbed into: a game that wants a clip reads
   * ctx.triggers, a game that wants words reads ctx.words. Echo's pads are the
   * consumer (0823). Garbage rows are dropped, never thrown on.
   *
   * ON A HOUSE DAY they come from init.houseTriggers instead, filtered to the
   * words actually dealt. ctx.triggers must always describe ctx.words: rows for
   * a pool the classes are not flashing would have echo's triggerPool() and
   * instant-recall's clipRows() reading a vocabulary nobody sees. */
  const triggerRows = (rows) => (Array.isArray(rows) ? rows : [])
    .filter((t) => t && typeof t.text === 'string' && t.text.trim())
    .map((t) => Object.freeze({ text: t.text, audio: typeof t.audio === 'string' && t.audio ? t.audio : null }));
  const dayTriggers = Object.freeze(wordSource === 'house'
    ? triggerRows(src.houseTriggers).filter((r) => dayWords.indexOf(r.text) >= 0)
    : triggerRows(src.triggers));

  /* THE VOICED WORD MAP the engine gets (engine/oneshots.js `voice`). Built from
   * the rows that actually carry a clip, and ONLY when the app-wide whisper mute
   * is off - which is why no game has to gate its own `voice:true` on
   * ctx.audioAudible: on a silent day this is empty and the flag does nothing. */
  const dayWordAudio = Object.freeze((src.audioAudible ? dayTriggers : [])
    .filter((r) => r.audio)
    .reduce((m, r) => { m[r.text] = r.audio; return m; }, Object.create(null)));

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
  /* THE POSTER DROP (COUNTER STOCK). A GETTER, not a boolean: initCorkboard
   * runs at boot, and a purchase settled mid-session has to light by the next
   * board paint without a reload (trap 73's shape). Ownership arrives at this
   * ONE place - overlay board, Records cork wall and the office miniature all
   * read the same injected getter, so three walls can never disagree. */
  initCorkboard({ state: postState.board, save: (s) => store.set('board', s), daySeed: utcDateSeed, log: say, when: postWhen,
    posters: () => ownsSku('poster_drop_1') });
  initBugle({ state: postState.bugle, save: (s) => store.set('bugle', s), log: say, when: postWhen });
  /* ================== EMI KEEPS OFF THE DOORS ==========================
   * Her layer sits OVER the quad and her body is the one thing on it that
   * takes a pointer, so parked in the bottom-right corner she ate every tap
   * meant for RM 004 and the bottom half of the Prize Counter. The widget owns
   * the rule (see "EMI KEEPS OFF THE ALLEY" in emi/widget.js); the campus owns
   * the boxes, and hands them over as a GETTER rather than a snapshot for the
   * usual reason (trap 73): the stage is torn down and rebuilt on every visit,
   * so a rect measured at mount is a rect from a campus that has gone.
   * ==================================================================== */
  /* The doors, AND the postbox. The envelope chip is the other 44px control on
   * this screen she can sit on top of - on a phone it leaves the top-right
   * cluster for the bottom-right corner, which is the exact corner she parks
   * in (shell/mail.css, and the first-run standoff in widget.js is named after
   * it). A tap eaten by a mascot is a tap eaten by a mascot either way. */
  const KEEP_OFF_SEL = 'g.campus-room.facility, .arc-mailchip';
  /* ...and on a PHONE, the two bits of campus chrome that share her band: the
   * hint line (bottom, over the folded ID tag - upright campus re-homes it) and
   * the folded plaque on the spine. Desktop keeps the doors-only list on purpose
   * (the hint is inert there and folding it in only drags her over the Lecture
   * Hall); on a 390px glass she covers the hint with her whole body. Only the
   * COLLAPSED plaque: expanded, the wrap IS the full-screen scrim (styles.css
   * UPRIGHT CAMPUS B) and a rect the size of the viewport has no way out, which
   * the rule would answer by leaving her put - harmless, but a wasted turn. Read
   * per call, never at mount: the plaque re-homes on every orientation flip. */
  const KEEP_OFF_SEL_PHONE = KEEP_OFF_SEL + ', .campus-hint, .campus-boardwrap.collapsed';
  const onPhone = () => { try { return document.documentElement.classList.contains('arc-mobile'); } catch (e) { return false; } };
  /** ...AND THE PA'S CAPTION, which is the one keep-off box that is NOT on the
   *  campus. shell/pacaption.js hangs its bubble on <body> (it has to outlive a
   *  screen change), so `campus.root.querySelectorAll` above can never see it
   *  and folding the class into KEEP_OFF_SEL would silently do nothing. It is
   *  also the only box in the list that COMES AND GOES mid-screen, which is
   *  exactly why the rule takes a getter rather than a snapshot (trap 73): she
   *  steps aside while the announcement is up and takes the corner back when it
   *  is gone. `.is-on` and not just `.pa-cap` - the hidden layer is full-width
   *  and would pin her off the bottom of the quad all night. */
  const KEEP_OFF_SEL_CAPTION = '.pa-cap.is-on .pa-cap-box';
  /** Everything on the quad she may not stand on, in viewport px. [] off-campus. */
  function campusDoorRects() {
    if (screen !== 'board' || !campus || !campus.root) return [];
    const out = [];
    try {
      const nodes = campus.root.querySelectorAll(onPhone() ? KEEP_OFF_SEL_PHONE : KEEP_OFF_SEL);
      for (const n of nodes) {
        const b = n.getBoundingClientRect ? n.getBoundingClientRect() : null;
        if (b && b.width > 0 && b.height > 0) out.push(b);
      }
    } catch (e) { return []; }
    try {
      const cap = document.querySelector(KEEP_OFF_SEL_CAPTION);
      const b = (cap && cap.getBoundingClientRect) ? cap.getBoundingClientRect() : null;
      if (b && b.width > 0 && b.height > 0) out.push(b);
    } catch (e) { /* no caption is not an error; the doors still stand */ }
    return out;
  }
  /** Arm the rule on the campus, drop it on the way out. A host whose mascot
   *  never built - or an older widget with no seam - simply never hears it. */
  function keepEmiOffTheDoors(on) {
    try {
      const emi = getEmi();
      if (!emi || typeof emi.keepClear !== 'function') return;
      emi.keepClear(on ? campusDoorRects : null);
    } catch (e) { /* noop */ }
  }

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
    fireMoment('campus.corkboardOpened', { inClass: false });   // EMI SEAM
  }
  function openBugleOverlay() {
    const up = currentBugle();
    if (up && !up.closed) return;
    openBugle(null, { onClose: refreshCampusPost, log: say });
    fireMoment('campus.bugleOpened', { inClass: false });       // EMI SEAM
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
  /* EMI's handle. She was mounted and forgotten until COUNTER STOCK: the desk
   * toy and the varsity jacket are prizes, so a settled purchase has to be able
   * to tell her to look again. Null on a platform with no layer, and every call
   * against it is guarded - she is the one module whose absence must cost the
   * shell nothing at all. */
  let emi = null;
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
  let boardScrimArmed = false;     // the upright board's tap-out (armBoardScrim)
  /* WHICH FULL-BLEED STAGE IS UP, or null. setStage() is the one writer and
   * renderTopbar() is the one reader - a stage owns the window, so the bar
   * stays retired for as long as one is up, whoever asks for the repaint. */
  let stageMode = null;
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
  /** THE PRIZE COUNTER (shell/prizecounter.js). Same lifecycle as the office:
   *  built fresh per visit, destroyed by clearScreen. The handle is kept while
   *  it is up for exactly one reason - the `wallet-result` echo has to find the
   *  live room to settle into, and a purchase settled into a room that is no
   *  longer on screen is a purchase the player never sees land. */
  let prizeRoom = null;
  /* THE ANTECHAMBER. The painted booth the walk actually arrives at; the
   * shelf above is what its window opens. Held separately because they are
   * two screens now, and because the booth hangs an apron band on <body>
   * that only its own destroy() can take off. */
  let prizeBooth = null;
  /* THE LOCKER (shell/locker.js). RM 004, and the same lifecycle as the office
   * and the booth for the same one reason: it is a scene, and the scene chassis
   * hangs its apron band on <body> where `dom.screen.textContent = ''` cannot
   * reach. Only its destroy() takes that band off, so the handle is held here
   * purely so clearScreen has something to tear down. */
  let lockerRoom = null;
  /** THE LEVER THIS CLASS WAS STARTED ON. Latched at the opening bracket rather
   *  than read again at the end, so a player who buys the Honors lever DURING a
   *  run does not retroactively promote the run they are already in - the host
   *  clamps the same way from its own stored pending lever, and the two answers
   *  have to agree or an S+ the page painted would be degraded to an S by C#. */
  let activeLever = 'standard';
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
  /* ---------------------- THE ACCOUNT CHIP (shell/accountchip.js) ----------
   * A HOST SLOT. `init.account` (and `account` on a `profile` frame) is the
   * web host's word that there is a login to control from this bar: name,
   * a same-origin photo path and the ACTIONS it will honour. The desktop
   * never sends it, so `account` stays null and no chip is ever mounted.
   * ONE state, TWO chips (the topbar's and the campus cluster's) - both paint
   * from `account` and both post the verb through `postAccountAction`. */
  let account = readAccount(src.account);
  let topAccountChip = null;
  function postAccountAction(action) {
    try { bridge.send({ type: 'account-action', action: String(action) }); }
    catch (e) { say('account-action ' + action + ' refused: ' + ((e && e.message) || e)); }
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
  /** THE BACK ROOM (backroom/index.js, loaded on the click and never before).
   *  The one page in this bundle the shell does not import at the top: the wing
   *  is a whole floor of tables and it may not cost a boot on a phone that is
   *  never going to open the door. `backRoomMod` is the module once it has
   *  landed, so a second visit is a call and not a second fetch. */
  let backRoomPage = null;
  let backRoomMod = null;
  /** The one in-flight annex stats request ({promise, resolve, timer}|null). */
  let annexStatsWait = null;
  let shareImageWait = null;
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
  /* The reason the CURRENT suspend level was set with (null when lifted).
   * Written only by applySuspend; read only by the web tab-visibility wiring
   * below, so it can refuse to lift a suspend it did not apply itself. */
  let suspendReason = null;
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

  /* THE ACTIVITY LAUNCH (the Discord Activity wave, 2026-08-28). A hosted shell
   * that was opened through one of the per-class slash commands names the room
   * it was asked for; absent (the desktop host NEVER sends it, and does not
   * need to - it has a campus) means "just open the campus".
   *
   * IT IS A REQUEST, NOT A GRANT. The gate is the player's own punch card, read
   * from the SAME `isUnlocked` the campus door reads, so this door can never be
   * wider than the one on the quad. The hosted shells wall on the server's
   * `complete` before the page boots; this is the belt-and-braces half, and it
   * refuses with a toast rather than a screen so the player lands on the campus
   * they can actually use. Fired exactly ONCE per boot (see `launchFired`). */
  const launchRequest = (typeof src.launchGame === 'string' && src.launchGame) ? src.launchGame : '';
  let launchFired = false;
  if (launchRequest) say('activity launch requested: ' + launchRequest);

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

  /* THE DIRECT LAUNCH (the Discord per-class commands, 2026-08-30). A request
   * for a room this build actually HAS is opened without a campus: no board, no
   * entry reveal, no ghosts, no walk and no Orientation Day - boot.js keeps its
   * own splash up over the whole thing and letters the room's name on it, so the
   * player who typed `/deepend` sees the school's opening and then the pool.
   *
   * IT IS STILL ONLY A REQUEST. The gate has not moved: `maybeLaunchRequested`
   * is the one thing that ever fires a launch and it still asks `isUnlocked`.
   * This flag decides whether the CAMPUS is built first, and nothing else.
   *
   * An unknown or retired key is NOT a direct launch (`games.byKey` is the same
   * open-semester pool the board deals from): that boot opens the ordinary
   * campus and the refusal lands on it as a toast, which is the answer the quad
   * door would give. */
  const directLaunch = !!launchRequest && !!games.byKey[launchRequest];
  if (launchRequest && !directLaunch) {
    say('activity launch: ' + launchRequest + ' is not a room in this build - campus');
  }

  /* ---------------------- engine + provider ----------------------------- */
  /* THE CARD FACES. Optional, guarded, once: art/punchcard/faces.json says
   * where the stamps, the crest and the live text sit on each class's face
   * image. Missing (it ships with the art batch) leaves every card on the
   * gradient floor, which is a finished card - so this is awaited for ordering
   * only and can never fail the boot. */
  await loadFaceGeometry(say);

  const createEngine = await loadOptional('../engine/index.js', 'createEngine', NULL_ENGINE_FACTORY, say);
  const createAssets = await loadOptional('../provider/index.js', 'createAssets', NULL_ASSETS_FACTORY, say);

  /* THE LOOM GENERATOR - optional in exactly the engine's way: a module that
   * fails to import costs the woven spirals and nothing else (pickClassLoom
   * answers null and every class wears the old gif pick). Pure math at import
   * time; the shader only wakes inside the engine when a wash mounts. */
  let loomSeed = null;
  try {
    const m = await import('../engine/loom/seeded.js');
    if (m && typeof m.seededParams2 === 'function' && typeof m.loomId === 'function') loomSeed = m;
    else say('engine/loom/seeded.js: no seededParams2/loomId export - gif spirals only');
  } catch (e) {
    say('engine/loom/seeded.js import failed (' + ((e && e.message) || e) + ') - gif spirals only');
  }

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

  /* THE BOOT ASK (0826). The provider's first 'assets-request' used to leave
   * the page when a class was CLAIMED, which made the whole door / menu /
   * rules-sheet stretch network dead air - 10-30 seconds of it on the owner's
   * cellular phone, after which the first board still dressed itself in
   * placeholders because the host was only then doing its round trip. This
   * starts the remote pools filling NOW; the web shim has already preconnected
   * the Scrolller API and the CDN origins by this point, and this is the ask
   * that makes that preconnect pay off instead of letting the sockets go cold.
   * Modest on purpose - a head start, not a supply run; the class's own claim
   * still asks for what it needs. Mints no pool and draws no rand, so no
   * class's served media moves, and it is a silent no-op with remote media
   * off, under OfflineMode, or with no bridge at all. */
  try { if (assets && typeof assets.warmPool === 'function') assets.warmPool({ loop: 8, still: 8 }); }
  catch (e) { say('[assets] boot warm refused (' + ((e && e.message) || e) + ')'); }

  const ceremonies = createCeremonies({
    engine: null,                  // rebound per class (the engine is per class)
    layer: dom && dom.ceremony,
    reducedMotion,
    // The Prize Counter's one cosmetic that shows up mid-play. A getter, so a
    // stamp bought tonight garnishes the very next one.
    confetti: () => ownsSku('confetti_stamp'),
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
          try { const cp = currentCapsule(); if (cp && !cp.closed) return true; } catch (e) { /* noop */ }
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

  /* THE TILL IS A DOOR (counter shortcut wave, 2026-08-30). The report card is
   * the one screen that TELLS you what you just earned and, until now, had no
   * road to spend it on - the campus wallet chip is two screens back behind a
   * Done. So the night's ticket chip presses through to the same place the
   * campus chip does, and the card is handed the verb rather than the reason:
   * the catalog, the shutter and the ceremonies are all the shell's business.
   *
   * OFFERED ONLY WHEN THERE IS A SHELF AT ALL. `init.economy.catalog` is fixed
   * for the life of the page, so this is asked once here; a host that projects
   * no economy gets the card it always got, with an inert <span> on the till.
   *
   * IT REFUSES, IT NEVER TRAVELS BADLY. Two rungs, and they are two different
   * facts: a shut counter (a suspend, a lapsed entitlement) is answered with
   * the shutter's own line exactly as the campus chip answers it, and a
   * ceremony still on screen is answered with silence - `showPrizeBooth` calls
   * dismissEndCard/dismissPunchStage/dismissAnnexStage on its way in, so a
   * press mid-beat would stomp the beat that is doing the telling. */
  const counterFromReport = economyCatalog().length ? () => {
    if (endCard || punchStage || annexStage) return;
    if (counterClosed()) {
      shout(t('prize_closed_line',
        'The shutter is down and the sign above it has been switched off at the wall.'));
      return;
    }
    showPrizes();
  } : null;
  reportCard = createReportCard({
    ceremonies, seep, toast: shout, log: say, onCounter: counterFromReport,
    /* THE SLIP's two seams. `identity` is only ever CALLED when the player has
     * ticked the box - the card is anonymous by default and the report card
     * never reads a name it was not asked for - and `shareNamed` is where that
     * tick lives. It is a page-owned meta key (it is not in HOST_OWNED_KEYS),
     * so store.set write-throughs are legal and the tick survives the night. */
    identity: () => {
      const p = idProfile();
      const num = studentNumber(p.selfId, p.enrolled, p.name);
      return { name: p.name, number: num && num.no };
    },
    shareNamed: {
      get: () => store.get('shareNamed', false) === true,
      set: (v) => store.set('shareNamed', v === true),
    },
    /* The app only. On the web `src.mediaControls` is true and there is no
     * host behind the bridge to ask, so that rung of the ladder is absent
     * rather than present-and-broken. */
    shareToHost: src.mediaControls === true ? null : shareImageToHost,
  });

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
    /* A CAMEO IS STANDING IN SOMETHING THAT IS ABOUT TO BE REMOVED. Every
     * screen change funnels through here, so this is the one place the visit
     * has to be ended - the game's ring goes with the wipe and she must not
     * be left parked in the middle of whatever comes next. */
    cancelLiveVisit();
    /* THE TUNE LEAVES WITH THE SCREEN. Silence is the default between places:
     * the next screen that has a track asks for it, and one that does not
     * (the lab, the prize counter, settings) gets none. A campus rebuilt after
     * a class starts its loop from the top, which is what a hub theme does. */
    try { if (ost) ost.leave(); } catch (e) { /* noop */ }
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
    /* The doors go with the campus, so the keep-off rule goes with them: off
     * the quad she is back in the corner the player actually chose. */
    keepEmiOffTheDoors(false);
    if (campus) {
      try { campus.destroy(); } catch (e) { /* noop */ }
      campus = null;
      /* A campus that has gone is a VISIT that has ended: the once-per-visit
       * tells (the Slow Second) re-arm for the next one. */
      try { if (seep) seep.note('campusUnmount'); } catch (e) { /* noop */ }
      /* The PA falls silent with the campus - a line planned for a quad that
       * has gone would speak into the void. */
      try { if (pa) pa.notify('campusUnmount'); } catch (e) { /* noop */ }
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
    /* THE PRIZE COUNTER hangs nothing on <body>, but it DOES hold a watchdog
     * timer for the buy it is waiting on, and a timer that outlives its room is
     * a timer that repaints a dead node. destroy() clears it, so the counter is
     * torn down here for the same reason every other screen is: one funnel, no
     * survivors. */
    if (prizeRoom) {
      const pr = prizeRoom;
      prizeRoom = null;
      try { pr.destroy(); } catch (e) { /* noop */ }
    }
    /* AND THE BOOTH, which is the records case again rather than the counter's:
     * the scene chassis hangs its midway apron on <body>, where
     * `dom.screen.textContent = ''` cannot reach it. destroy() is the only thing
     * that takes that band off, so a booth left standing here would lay a slab
     * across the bottom of the shelf it just opened. */
    if (prizeBooth) {
      const pb = prizeBooth;
      prizeBooth = null;
      try { pb.destroy(); } catch (e) { /* noop */ }
    }
    /* AND THE LOCKER, third of the three scenes and torn down for the third
     * time for the same reason: the apron on <body>. It also holds a device
     * listener for the landscape rail, which destroy() drops. */
    if (lockerRoom) {
      const lr = lockerRoom;
      lockerRoom = null;
      try { lr.destroy(); } catch (e) { /* noop */ }
    }
    /* AND THE BACK ROOM, which is torn down here for every reason the four
     * rooms above it are AND one nobody else on this floor has: it is the only
     * page holding a live subscription to the host. A handler left on
     * `casino-result` would paint a cage that has been thrown away, and worse,
     * would keep doing it for the rest of the night. The room's own destroy()
     * drops the ones it took out; the sweep after it is the safety net, because
     * a page that forgets one must not be able to leak it into the next screen. */
    if (backRoomPage) {
      const br = backRoomPage;
      backRoomPage = null;
      try { br.destroy(); } catch (e) { /* noop */ }
    }
    if (backRoomOffs.length) {
      const offs = backRoomOffs;
      backRoomOffs = [];
      offs.forEach((off) => { try { off(); } catch (e) { /* noop */ } });
    }
    extrasBox = null;
    /* THE ROTATE GATE BELONGS TO A SCREEN, NOT TO THE PAGE. Every screen change
     * funnels through here, so dropping it here is what stops a gate one screen
     * asked for from hanging over the report card behind it. The screens that
     * still want one (a class whose board has a shape - the campus stands
     * upright now and asks for nothing) re-arm after they have built, which is
     * also how the card knows which words to wear. */
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
    stageMode = mode || null;
    if (dom && dom.topbar) dom.topbar.hidden = !!mode;
    /* THE WEATHER LAYER'S FOURTH KILL SWITCH. Every screen change in the school
     * funnels through here (clearScreen ends in setStage(null); the class and
     * the report set their own), so this is the one line that keeps the campus
     * theme's snow off a running class. Games own the screen. */
    noteThemeFxScreen();
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
    /* AND RETIRED UNDER A FULL-BLEED STAGE, for the same reason (owner bug
     * 2026-08-25, "the hub header is sitting on top of the corkboard"). The
     * guard read `campus` alone, so any UNCONDITIONAL repaint - a `meta` push
     * from the host landing mid-visit, a payout frame - hoisted the bar back
     * over the Records room, the report card and the annex. It paints at z30
     * over a room fixed at z10, so it swallowed the close-up's whole top band,
     * step-back pill and all. A stage owns the window until it hands it back. */
    bar.hidden = !!campus || !!stageMode;
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
    /* THE ACCOUNT CHIP, far right, beside SETTINGS. Web host only: the slot
     * stays empty on a desktop that never sent `init.account`. The bar is
     * rebuilt on every render, so the chip is too - one mint per render, the
     * old one destroyed so its document listeners go with it. */
    if (topAccountChip) { try { topAccountChip.destroy(); } catch (e) { /* noop */ } topAccountChip = null; }
    if (account) {
      try {
        topAccountChip = createAccountChip({
          t, account, isMobile,
          onOpenCard: () => showIdCard(),
          onAction: postAccountAction,
          log: say,
        });
        if (topAccountChip) bar.appendChild(topAccountChip.el);
      } catch (e) { say('account chip unavailable (' + ((e && e.message) || e) + ')'); topAccountChip = null; }
    }
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

  /* ------------------------- THE TIME CAPSULE ---------------------------
   * THE DOOR IS MONOTONIC: the case opens at a streak of CAPSULE_NIGHTS and
   * never closes again. `streak` is HOST-OWNED and is a CURRENT count, so it
   * cannot answer "has this player EVER reached thirty" - the high-water mark
   * is banked here in ONE page-owned meta blob, `capsule = {opened, best}`.
   *
   * A PAGE KEY AND NOT A SYNCED ONE, by necessity: the synced-state whitelist
   * lives in the web shim, which is not in this repo, and no synced field
   * means "best streak ever" - `perfectAttendance` counts perfect NIGHTS, a
   * different rule that would open the case for a player who never held a
   * streak. So the mark rides ArcademyMetaStore on the desktop (new top-level
   * keys are accepted; only host-owned ones are refused) and localStorage on
   * the web, and the worst case is a player who crosses thirty on one device
   * seeing the parcel on another until their streak is read there once.
   *
   * The ARITHMETIC is capsule.js's pure `capsuleDoor`; bankCapsule is only the
   * read and the conditional write, run on every campus mount - the one moment
   * a night's streak is certainly known.
   * ------------------------------------------------------------------- */
  function bankCapsule() {
    try {
      const d = capsuleDoor(store.get('capsule'), store.streak().count | 0, CAPSULE_NIGHTS);
      if (d.changed) store.set('capsule', { opened: d.opened, best: d.best });
      return d;
    } catch (e) {
      say('capsule bank failed: ' + ((e && e.message) || e));
      return { opened: false, best: 0, changed: false };
    }
  }

  /** Nights on the tag: what the case has seen, against what it asks for. */
  function capsuleNights() { return { have: bankCapsule().best, need: CAPSULE_NIGHTS }; }

  function capsuleSealed() { return !bankCapsule().opened; }

  /** Open the case. One at a time, the post overlays' own re-entry guard. */
  function openCapsuleOverlay() {
    const up = currentCapsule();
    if (up && !up.closed) return;
    const n = capsuleNights();
    openCapsule({
      t, sealed: capsuleSealed(), have: n.have, need: n.need,
      reducedMotion, onClose: refreshCampusPost, log: say,
    });
    fireMoment('campus.trophyOpened', { inClass: false });      // EMI SEAM
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
        const g = gradeKey(r.grade);
        const cell = el('span', 'rcell');
        cell.appendChild(el('span', 'grade ' + (g === 'pass' ? 'pass' : g || 'none'),
          r.grade ? gradeLabel(r.grade) : '--'));
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

  /* THE DARK BEHIND THE UPRIGHT TIMETABLE CLOSES IT.
   *
   * Held upright the open board is a modal: `.campus-boardwrap` stops being a
   * hung object and becomes a full-glass scrim with the panel centred in it
   * (styles.css, "UPRIGHT CAMPUS (B: chrome)"). A scrim you cannot tap out of
   * is a trap, and the plaque is at the TOP of that panel with a whole board
   * between it and the thumb, so the dark has to be a way out.
   *
   * One delegated listener, armed once, and it is deliberately narrow:
   *   - it only ever fires when the click lands on the WRAP ITSELF, which in
   *     this mode is only ever the scrim (the panel is `.campus-boardsway`, a
   *     child, and every control inside it is a child of that);
   *   - it is gated on the portrait-upright switch, so a landscape phone and a
   *     desktop evaluate two `classList` reads and return - neither of them has
   *     a scrim, and neither changes behaviour by a byte;
   *   - it closes by CLICKING THE PLAQUE rather than by touching classes, so
   *     `aria-expanded`, the pulse and the shell's `boardToggle` seam all stay
   *     the plaque's business and there is still exactly one way to fold this
   *     board (campus.js owns the toggle; this is a second finger on the same
   *     button, not a second mechanism).
   * The campus is rebuilt on every non-silent arrival, so this guards itself
   * rather than stacking a listener per visit. */
  function armBoardScrim() {
    if (boardScrimArmed) return;
    boardScrimArmed = true;
    try {
      document.addEventListener('click', (ev) => {
        try {
          const root = document.documentElement;
          if (!root || !root.classList.contains('arc-mobile')) return;
          if (root.getAttribute('data-arc-orient') !== 'portrait') return;
          const hit = ev && ev.target;
          if (!hit || !hit.classList || !hit.classList.contains('campus-boardwrap')) return;
          if (hit.classList.contains('collapsed')) return;
          const tab = hit.querySelector('.campus-boardtab');
          if (tab && typeof tab.click === 'function') tab.click();
        } catch (e) { /* a scrim must never be the thing that throws */ }
      });
    } catch (e) { say('board scrim arm failed: ' + ((e && e.message) || e)); }
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
      // EMI SEAM: the pip on the envelope chip is a moment. Null = nothing landed.
      try {
        const landed = mail.deliver();
        if (landed) fireMoment('campus.mailLanded', { letter: landed.id, inClass: false });
      } catch (e) { say('mail deliver threw: ' + ((e && e.message) || e)); }
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
        // EMI SEAM: the miniature arrived. The three office doors are the
        // geofence's, so she never comments on the walk to Records.
        if (!SILENT_WALK_TARGETS.has(String(targetKey))) {
          fireMoment('campus.walkArrived', {
            targetKey, trips: (residueTrail && residueTrail.length) || 0, inClass: false,
          });
        }
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
    /* THE CAPSULE'S HIGH-WATER MARK, banked before the plan is drawn: the
     * case about to be drawn reads the latch a line later. */
    bankCapsule();
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
        /* THE TROPHY CASE. Same bag contract as `post` and `annex`: the campus
         * draws the door and the tooltip, the shell keeps the state and mints
         * the overlay. Handed unconditionally - the case is always pressable,
         * and what is BEHIND the glass is the thing that changes. */
        capsule: { open: openCapsuleOverlay, sealed: capsuleSealed },
        /* THE ECONOMY, handed down rather than read. campus.js is under the
         * header law (it imports no store and no bridge), so the wallet chip in
         * its top-right cluster and the Extra Credit lever on its door card
         * both live entirely on these two getters. Same bag contract as `post`
         * and `annex`: the campus draws, the shell keeps every byte of state.
         * A host with no economy in `init` hands null and the campus is exactly
         * the campus it was - no chip, no lever, no gap where one used to be. */
        economy: economyCatalog().length ? {
          balance: () => walletBalance(),
          lever: leverCaps(),
        } : null,
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
            /* THE PA PACK's arrival line - after the ghosts, so a line lands
             * on a campus that has finished standing up. */
            try { if (pa) pa.notify('campusReveal'); } catch (e) { /* noop */ }
            // FIRST BELL's second gate: the school has finished standing up.
            bellBoardRevealed = true;
            /* ...and EMI measures the quad AGAIN now that it has finished
             * standing up. The postbox chip is the reason: it is painted after
             * the stage lands, so a keep-off set read at mount knows about the
             * doors and not about the one control that shares her corner. */
            keepEmiOffTheDoors(true);
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
            fireMoment('campus.boardOpened', { inClass: false });   // EMI SEAM
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
          /* THE PRIZE COUNTER'S DOOR. A walk like the office's, because it is
           * across the quad like the office is - the wallet chip in the chrome
           * is the thing you can read without going anywhere.
           * THE WALK NOW ENDS AT THE BOOTH rather than at the shelf. A door on a
           * plan should open onto a place, and the place is the alley window;
           * the shelf is a thing you ask for once you are standing at it. The
           * campus only ever offers this when the counter is LIT (it draws
           * itself shuttered and raises the sealed card otherwise), so nothing
           * here has to second-guess the plan. */
          prizes: () => walkThen('prizes', () => showPrizeBooth()),
          /* THE LOCKER'S DOOR, one window further down the same alley, and it
           * walks like every other door on the plan. It is UNDER the counter on
           * purpose: the counter is where a thing is bought and the locker is
           * where it is kept, so walking past the shop to reach your own door is
           * the right way round. Never shuttered - nothing is sold in there, so
           * there is no closing time to keep and no sealed card to raise. */
          locker: () => walkThen('locker', () => showLockerScreen()),
          /* THE BACK ROOM'S DOOR, the last one in the alley and the only one
           * whose room is fetched rather than imported. It walks like every
           * other door here; what happens at the end of the walk depends on
           * whether the module is on this host at all (see showBackRoom). */
          backroom: () => walkThen('backroom', () => showBackRoom()),
          /* THE PURSE IN THE CHROME. The wallet chip is the shortcut half of
           * the same split the gear and the Front Office door already run: no
           * walk, no antechamber, straight to the shelf - because the chip is a
           * reading of your wallet and the thing you want after reading it is
           * the shelf. campus.js falls back to `prizes` when this is absent, so
           * a caller that predates the booth is unchanged.
           * IT LANDS AT THE WINDOW WITH THE SHELF ALREADY OPEN (Locker wave).
           * The shop is a panel over the booth's plate now, so "straight to the
           * shelf" means the plate is there under it - what the chip skips is
           * the walk and the arrival down the alley, never the room.
           *
           * IT REFUSES IN PLACE WHEN THE SHUTTER IS DOWN (counter shortcut
           * wave, 2026-08-30). `applySuspend` re-shows the board and leaves
           * this chip live and pressable, so a press during a suspend used to
           * BUILD the booth with `closed:true` - the shelf no-ops on the way in
           * (prizebooth.js openShop) and the player is left standing in an
           * unusable dark room they then have to find their way back out of. A
           * toast is the whole answer: `screen` never moves, no dialog is
           * minted, and the sentence is the one the shutter itself is lettered
           * with one screen further in, so the school says it in one voice. */
          prizesShelf: () => {
            if (counterClosed()) {
              shout(t('prize_closed_line',
                'The shutter is down and the sign above it has been switched off at the wall.'));
              return;
            }
            showPrizes();
          },
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
        /* THE ACCOUNT CHIP in the campus's top-right cluster (web host only -
         * null on the desktop, and the cluster is what it always was). Same
         * state and the same two verbs as the topbar's chip. */
        account: {
          get: () => account,
          isMobile,
          onOpenCard: () => showIdCard(),
          onAction: postAccountAction,
        },
        /* THE FRAME, on the card in the corner of the quad. The SAME getter the
         * spotlight takes, so the 560px card and the laminated one can never
         * show different frames - and it is the getter and not a value because
         * the campus outlives an equip made in the Locker. */
        idFrame,
        // THE SIGN OVER THE BACK ROOM DOOR: the last answer, and false until
        // there is one. askBackRoomOpen below lights it when the server says so.
        backroomOpen: backRoomOpen,
        log: say,
      });
      campus.noteDescriptors(campusDescriptors());
      askBackRoomOpen();
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
      /* ...and the doors are measurable the instant they are in the document,
       * so EMI steps off them now rather than at the next resize. The entry
       * reveal is opacity only, which is why a mount-time rect is honest. */
      keepEmiOffTheDoors(true);
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
          /* THE WALKER'S PRIZES (COUNTER STOCK). Read at BUILD, which is
           * correct here and nowhere else: the campus (and with it the walker)
           * is torn down and rebuilt on every visit, so a purchase settled at
           * the counter is wearing by the time the player is back on the quad.
           * `lite` gates the cosmetics ONLY (walk.js keeps walking under
           * performance mode - it refuses to mint the afterimages, and it
           * THINS the spark pool rather than dropping it, house doctrine:
           * lite keeps the move and drops the particle count). Only
           * reducedMotion deletes the sparks outright. */
          lite: shellLite(),
          cosmetics: {
            awayColors: ownsSku('away_colors'),
            sparklerSteps: ownsSku('sparkler_steps'),
            ghostWalk: ownsSku('ghost_walk'),
          },
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
      /* THE CAMPUS STANDS UPRIGHT NOW (2026-08-28), so it no longer asks for
       * the phone sideways. It used to: the plan was a fixed 16:9 geography and
       * `meet` inside a 9:19.5 slot was a strip of architecture two rooms wide
       * with the rest of the school as sky, so `requireOrientation('landscape',
       * { reason: 'campus' })` stood here and put the turn-your-phone card up.
       * Held upright the plan now TURNS WITH THE GLASS - same pixels, same fit -
       * and everything a human reads turns back the right way up (shell/campus.js
       * `planUpright()` / `applyOrientation()`), while the HTML chrome re-homes
       * into the bands the turned plan leaves (styles.css, "UPRIGHT CAMPUS").
       * The gate itself is untouched and still armed per class further down -
       * a class with a landscape-only board is a different question from a
       * floor plan, and orientgate.js keeps its `rotate_campus_*` copy. */
      armBoardScrim();
      /* THE CAMPUS THEME, once the hub has actually built. Under the reveal,
       * under the PA (which ducks it), gone with the campus via clearScreen. */
      try { if (ost) ost.enter('campus'); } catch (e2) { /* noop */ }
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
      /* The Mascot group's writer - a getter, because the controller mounts
       * async and the page must render either way. */
      emi: () => { try { return getEmi(); } catch (e) { return null; } },
      // The folds bank their open state here (`optionsOpen.<section>`).
      store,
      /* THE DOOR'S TWO WRITE VERBS, lent to the web Media group so its add
       * and remove buttons ride SORT's `probe-sub` / `library-remove` frames
       * rather than a second copy of them. The group only renders behind
       * `init.settings.mediaControls === true`, so on the app this is an
       * unread argument. */
      assets,
      /* THE RESOLVED WORD POOL, not init.words. With the house floor under it
       * the two can disagree, and the row that says "0 words" while the classes
       * flash twelve is a straight lie about a layer the player cannot audit. */
      vocab: { count: dayWords.length, source: wordSource },
      /* CAMPUS LOOK (COUNTER STOCK). Narrow caps, the annex's law: the sheet
       * lists what it is handed and calls `select`. It never reads a wallet,
       * never touches the store and never learns that an unowned theme exists -
       * `list()` simply does not contain one, which is what keeps a restock a
       * surprise instead of a padlock. No `pending`, either: the pick is a
       * page-owned meta key, so there is no host echo to wait for. */
      themes: themeCaps(),
      /* THE SIGNPOST. The campus look USED to be drawn here; it is a group in
       * the Locker now, and what Options keeps is the sentence that says so
       * plus the door. Two pages that both pick the palette is two places to
       * find a stale one. */
      openLocker: () => showLocker(),
      /* WHICH OPTIONS A PRIZE OPENS. The 5x5 board is the first: the row is
       * drawn either way, disabled until it is bought, because a knob that
       * appears out of nowhere the night you buy something is a knob nobody
       * knew they were shopping for. */
      settingUnlocks,
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
          /* An S+ is an S with the lever pulled, and a day the card counts. */
          const letter = String((classes[g] || {}).grade).toUpperCase();
          if (letter === 'S' || letter === 'S+') { n++; break; }
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

  /**
   * The frame the card wears tonight. TWO ANSWERS, in the school's usual order:
   * explicit pick > bag default.
   *
   * The ladder underneath is what this function was for two waves - gold wins
   * when both are owned, because gold is the dearer of the two and nobody buys
   * the dear one to be shown the cheap one - and it is still the whole answer
   * for a player who has never opened the Locker. What sits above it now is
   * `lockerFrame`, RM 004's pick, and it needs THREE values rather than two
   * because "no pick" and "no frame" are different states: an unset key means
   * the ladder decides, and the sentinel 'plain' is a frame owner deciding to
   * wear none. A pick whose sku is not owned is ignored here and cleared by the
   * Locker the next time it renders (the lapse is real - an entitlement can end
   * while the key outlives it).
   */
  function idFrame() {
    let pick = '';
    try { pick = String(store.get('lockerFrame') || ''); } catch (e) { pick = ''; }
    if (pick === 'plain') return '';
    if (pick === 'gold' && ownsSku('id_frame_gold')) return 'gold';
    if (pick === 'navy' && ownsSku('id_frame_navy')) return 'navy';
    if (ownsSku('id_frame_gold')) return 'gold';
    if (ownsSku('id_frame_navy')) return 'navy';
    return '';
  }

  /* ------------------------ THE LOCKER'S THREE KEYS ----------------------
   * `lockerOutfit` / `lockerFrame` / `lockerToy` are PAGE-OWNED meta, minted
   * the way `campusTheme` was and needing no C# for the same reason. The Locker
   * writes them; these two readers are what everything ELSE in the shell asks,
   * because both answers are wanted somewhere the Locker is not on screen (EMI
   * is mounted for the whole session and the ID card can be opened from the
   * quad). Same law both times: a pick is only a pick, the wallet is the
   * ownership witness, and an unowned pick reads as no pick at all.
   * -------------------------------------------------------------------- */

  /** The outfit EMI is wearing, or null for whatever the bag says. */
  function lockerOutfit() {
    let want = '';
    try { want = String(store.get('lockerOutfit') || ''); } catch (e) { want = ''; }
    if (!want) return null;
    return ownsSku('emi_' + want) ? want : null;
  }

  /** The toy pinned to her desk, or null for tonight's rotation. */
  function lockerToyPin() {
    if (!ownsSku('emi_desk_toy')) return null;
    let want = '';
    try { want = String(store.get('lockerToy') || ''); } catch (e) { want = ''; }
    return want || null;
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
          lite: shellLite(),
          isMobile,
          profile: idProfile,
          stats: idStats,
          frame: idFrame,
          onChip: onIdChip,
          onRecords: () => showRecords(),
          /* THE SECOND DOOR ON THE BACK OF THE CARD. Records is where the card
           * is READ; the Locker is where it is DRESSED, and the frame it wears
           * is the thing you are looking at while you press it. */
          onLocker: () => showLocker(),
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
    // EMI SEAM: she is only switched back on HERE, so this is the one edge of
    // the spotlight she is allowed to have watched.
    fireMoment('campus.idCardClosed', { inClass: false });
  }

  /** Put the card back, wherever the ask came from. True when one was up. */
  function dismissIdCard(silent) {
    if (!idSpotlight) return false;
    let was = false;
    try { was = !!idSpotlight.dismiss(silent); } catch (e) { was = false; }
    releaseIdCard();
    return was;
  }

  /* ============================ THE ECONOMY =============================
   * TWO CURRENCIES, AND THE PAGE MINTS NEITHER OF THEM. `wallet` is a
   * HOST-OWNED meta key: the shell reads it and never writes it, the same
   * arrangement `days`, `streak` and the punch cards have had since day one.
   * Tickets land on a graded finish, the token lands on the first S-rank of a
   * local day, and both of those sums are computed in C# where the page cannot
   * reach them. Everything below is a READER.
   *
   * WHY THE SHELL HOLDS THESE AND NOT THE COUNTER. Two surfaces need the same
   * numbers - the campus chip and the Prize Counter - and one of them (the
   * campus) is under the header law and may not touch the store at all. One
   * reader here, handed down as caps to both, is the only arrangement where the
   * chip and the shelf can never disagree about what you are holding.
   * ==================================================================== */

  /** THE LAST THING THE HOST SAID ABOUT THE WALLET, laid over the meta snapshot
   *  until the next `meta` frame replaces it. This is NOT an optimistic paint -
   *  every byte of it arrived on a host frame (`payout-result`, `wallet-result`)
   *  and the host had already banked it before it was posted. It exists because
   *  the meta snapshot is pushed on its own schedule, and a player who watches
   *  a token drop into the tray and then finds the campus chip still reading
   *  yesterday's number has been told two different truths. */
  let walletEcho = null;

  /** Fold a host frame's wallet fields into the echo. Missing means unchanged. */
  function noteWalletEcho(frame) {
    const f = frame || {};
    const next = walletEcho ? Object.assign({}, walletEcho) : {};
    let moved = false;
    if (f.wallet && typeof f.wallet === 'object') {
      const tt = Number(f.wallet.t); const kk = Number(f.wallet.k);
      if (Number.isFinite(tt)) { next.t = tt; moved = true; }
      if (Number.isFinite(kk)) { next.k = kk; moved = true; }
      /* CHIPS RIDE THE SAME ECHO (the Back Room, W1). `c` is what is on the
       * player and `earnedC` is what they have ever won, and both are the
       * HOST's numbers on the host's frame - the page banks neither. A host
       * that has never heard of the wing sends neither field and this reads
       * exactly as it read before the wing existed. */
      const cc = Number(f.wallet.c); const ec = Number(f.wallet.earnedC);
      if (Number.isFinite(cc)) { next.c = cc; moved = true; }
      if (Number.isFinite(ec)) { next.earnedC = ec; moved = true; }
    }
    if (f.inv && typeof f.inv === 'object') { next.inv = f.inv; moved = true; }
    if (f.unlocks && typeof f.unlocks === 'object') { next.unlocks = f.unlocks; moved = true; }
    if (moved) walletEcho = next;
  }

  /** The wallet blob, shaped. A wallet that has never been written is an empty
   *  purse and never a throw - a player who has not finished a class yet is the
   *  ordinary case on a first night. */
  function walletBag() {
    let base = {};
    try {
      const w = store.get('wallet');
      if (w && typeof w === 'object') base = w;
    } catch (e) { base = {}; }
    return walletEcho ? Object.assign({}, base, walletEcho) : base;
  }

  /** {t, k, c}: what is on the player right now. A purse that has never held a
   *  chip reads zero, which is the same nothing an empty ticket purse reads. */
  function walletBalance() {
    const w = walletBag();
    const tt = Number(w.t); const kk = Number(w.k); const cc = Number(w.c);
    return {
      t: Number.isFinite(tt) && tt > 0 ? tt : 0,
      k: Number.isFinite(kk) && kk > 0 ? kk : 0,
      c: Number.isFinite(cc) && cc > 0 ? cc : 0,
    };
  }

  function walletInv() {
    const v = walletBag().inv;
    return (v && typeof v === 'object') ? v : {};
  }

  /** The lever's two permanent unlocks, plus anything else the host banked
   *  there. init.economy.leverUnlocks is the projection at boot; the wallet's
   *  own copy is what a purchase echo moves, so the wallet WINS where it says
   *  something - a token spent on the Honors lever lights it that same second. */
  function walletUnlocks() {
    const boot = (src.economy && src.economy.leverUnlocks) || {};
    const live = walletBag().unlocks;
    const out = { extra: !!boot.extra, honors: !!boot.honors };
    if (live && typeof live === 'object') {
      for (const k of Object.keys(live)) out[k] = live[k] === true;
    }
    return out;
  }

  /** The host's catalog, exactly as init projected it. The page never prices a
   *  row and never invents one: an empty catalog is a bare shelf, which the
   *  counter has a sentence for. */
  function economyCatalog() {
    const rows = src.economy && src.economy.catalog;
    return Array.isArray(rows) ? rows.filter((r) => r && r.sku) : [];
  }

  function catalogRow(sku) {
    for (const r of economyCatalog()) if (r.sku === sku) return r;
    return null;
  }

  /** THE SETTINGS A PRIZE OPENS. The host projects one row per gated option
   *  ({key, value, sku, owned}); the Options sheet reads it to render the rung
   *  it cannot yet pick, with the counter named as the way to get it. The page
   *  never decides ownership here - `owned` is the host's word and the wallet
   *  behind it is the same wallet ownsSku reads. */
  function settingUnlocks() {
    const rows = src.economy && src.economy.settingUnlocks;
    return Array.isArray(rows) ? rows.filter((r) => r && r.key) : [];
  }

  /** How many of a consumable may be held at once. Display only - the host is
   *  the one that refuses an over-full stack, with reason "full". The fallback
   *  is the Tardy Slip's own ceiling, because it is the only consumable on the
   *  shelf and a row that reached here without a `max` is a host that predates
   *  the field. */
  function stackMaxFor(sku) {
    const row = catalogRow(sku);
    if (!row || row.kind !== 'consumable') return 0;
    const n = Number(row.max);
    return Number.isFinite(n) && n > 0 ? Math.floor(n) : 2;
  }

  /** Tonight's hot room, or null. Seeded per UTC day in C#; the page displays. */
  function economyPayday() {
    const pd = src.economy && src.economy.payday;
    if (!pd || !pd.gameKey) return null;
    const mult = Number(pd.mult);
    return Number.isFinite(mult) && mult > 1 ? { gameKey: String(pd.gameKey), mult } : null;
  }

  /** Does the player own a thing? Unlocks live in two places (see walletUnlocks)
   *  and either witness counts. */
  function ownsSku(sku) {
    const inv = walletInv();
    const row = inv[sku];
    if (row === true) return true;
    if (typeof row === 'number' && row > 0) return true;
    if (row && typeof row === 'object') {
      const n = Number(row.n);
      return Number.isFinite(n) ? n > 0 : true;
    }
    const un = walletUnlocks();
    if (sku === 'honors_lever') return un.honors === true;
    if (sku === 'free_swim_key') return un.freeSwim === true;
    return false;
  }

  /* ============================ THE CAMPUS LOOK =========================
   * COUNTER STOCK. Two of the restock's rows are THEMES (`theme_drone`,
   * `theme_snowday`): a palette over the whole school, plus a weather layer.
   *
   * THREE THINGS LIVE HERE AND NOWHERE ELSE, because all three are state:
   *   1. THE KEY. `campusTheme` is PAGE-OWNED, like `leverPick` and the three
   *      recordsRoom* keys - `ArcademyMetaStore.Set` takes any new top-level
   *      key under its own caps, so this wave owes C# nothing for it.
   *   2. THE CLAMP. A pick is only legal while the wallet still says the sku is
   *      owned. Same shape as leverPick(): junk, an unknown id and a theme the
   *      player does not own all answer `standard`, so a stale pick can never
   *      paint. (It can only ever have been WON, never lost - re-clamping is
   *      free and it is the line that makes that true rather than assumed.)
   *   3. THE ORDER. At boot the MOD SKIN goes on first (`init.palette`, the
   *      applyPalette call in the look block above), then the owned+selected
   *      theme goes OVER it. So: A MOD SKIN AND A THEME MEANS THE THEME WINS,
   *      for exactly as long as it is selected. Reverting is `removeProperty`
   *      for every token the theme set - back to the stylesheet's own value -
   *      and then the mod skin is re-laid, because removeProperty cannot tell
   *      a token the theme wrote from a token the mod wrote. NEVER a cached hex:
   *      a remembered colour is a second source of truth for the palette.
   *
   * The purchase lights the same second it settles: `wallet-result` runs
   * onWalletResult, which re-clamps the pick the way it re-clamps the lever.
   * ==================================================================== */

  /** Tokens the ACTIVE theme has written on :root, so revert knows what to
   *  take back. Rebuilt on every apply; empty means the house look is up. */
  let themeTokensSet = [];
  /** The weather canvas (shell/themefx.js). Built lazily - a player on the
   *  house look never pays for the module's layer at all. */
  let themeFx = null;

  /** The PA announcer (shell/pa.js). Declared beside the weather canvas so the
   *  campus callbacks above can guard on it; built once, further down, after
   *  the wallet echo exists. */
  let pa = null;
  /** The PA's caption surface (shell/pacaption.js). Built beside the PA and
   *  declared up here for the same reason: `campusDoorRects` needs its box so
   *  EMI does not park on the announcement. */
  let paCaption = null;
  /** THE SOUNDTRACK (shell/ost.js). Built beside the PA, driven from three
   *  places (the campus stand-up, startClass, showRecords) and let go of in
   *  clearScreen. Null when the module fails to build; every call site guards. */
  let ost = null;

  /** The pick, clamped to what is actually owned. Never returns junk. */
  function themePick() {
    let want = STANDARD_ID;
    try { want = String(store.get(THEME_META_KEY) || STANDARD_ID); }
    catch (e) { want = STANDARD_ID; }
    return clampThemeId(want, ownsSku);
  }

  /** Take back every token the last theme wrote, then re-lay the mod skin. */
  function revertThemePalette() {
    const style = document.documentElement && document.documentElement.style;
    if (style && typeof style.removeProperty === 'function') {
      for (const token of themeTokensSet) {
        try { style.removeProperty(token); } catch (e) { /* noop */ }
      }
    }
    themeTokensSet = [];
    // The mod skin was underneath and removeProperty does not know that.
    applyPalette(src.palette, say);
  }

  /**
   * Paint the pick. THE ONE WRITER of theme tokens and of the fx layer's kind.
   * Idempotent: calling it with the same pick twice repaints the same thirteen
   * values, which is what makes it safe to call from the wallet echo.
   */
  function applyTheme() {
    const id = themePick();
    revertThemePalette();
    const th = themeById(id);
    if (th) {
      applyPalette(th.palette, say);
      const style = document.documentElement && document.documentElement.style;
      if (style) {
        for (const key of Object.keys(th.palette)) {
          const token = PALETTE_TOKENS[key];
          if (token && themeTokensSet.indexOf(token) < 0) themeTokensSet.push(token);
        }
      }
    }
    /* A marker for anything that has to know WITHOUT reading the palette (the
     * fx sheet's belt rules, a future room dressing). Never a styling hook for
     * a colour - the colour is the palette's, and one source of truth is the
     * whole design (shell/themes.css says so at the top). */
    const html = document.documentElement;
    if (html && html.setAttribute) {
      try {
        if (id === STANDARD_ID) html.removeAttribute('data-campus-theme');
        else html.setAttribute('data-campus-theme', id);
      } catch (e) { /* noop */ }
    }
    syncThemeFx(id);
    return id;
  }

  /** Write the pick, clamped the same way, and answer what actually stuck. */
  function setThemePick(id) {
    const next = clampThemeId(id, ownsSku);
    try { store.set(THEME_META_KEY, next); }
    catch (e) { say('campus theme write failed'); }
    return applyTheme();
  }

  /**
   * THE WEATHER LAYER's one reconciler. Built on demand and torn down the
   * moment there is nothing to paint, so the standard look, a reduced-motion
   * player and a lite device all cost exactly zero.
   */
  function syncThemeFx(id) {
    const kind = themeFxFor(id == null ? themePick() : id, ownsSku);
    if (!kind || reducedMotion || shellLite()) {
      if (themeFx) { try { themeFx.destroy(); } catch (e) { /* noop */ } themeFx = null; }
      return;
    }
    if (!themeFx) {
      try {
        themeFx = createThemeFx({
          reduced: reducedMotion,
          lite: shellLite(),
          mobile: isMobile(),
          seed: utcDateSeed,
          log: say,
        });
      } catch (e) { say('theme fx unavailable (' + ((e && e.message) || e) + ')'); themeFx = null; return; }
    }
    try {
      themeFx.setInClass(!!active || screen === 'class');
      themeFx.setKind(kind);
    } catch (e) { say('theme fx sync threw: ' + ((e && e.message) || e)); }
  }

  /** Kill switch 4, wired to the ONE funnel every screen change already goes
   *  through (setStage). A class owns the screen. */
  function noteThemeFxScreen() {
    if (!themeFx) return;
    try { themeFx.setInClass(!!active || stageMode === 'arc-class-on'); }
    catch (e) { /* noop */ }
  }

  /**
   * A CAMPUS LOOK, WORN FOR TWO SECONDS AND THEN GIVEN BACK (Locker wave).
   *
   * The purchase reveal wants to SHOW a theme rather than describe it, and the
   * only honest preview of a palette is the palette, on the page. So this lays
   * the bought theme's thirteen tokens and hands back the way out.
   *
   * IT WRITES NOTHING THE PLAYER OWNS. `campusTheme` is untouched, so a reload
   * mid-preview lands on the pick that was already there; and the restore is
   * `applyTheme()`, THE one writer, rather than a hand-rolled unset - which is
   * why a preview can be cancelled twice, cancelled by a second preview, or
   * simply left to its timer, and the page always ends up wearing the pick.
   *
   * @param {string} id   a theme id (junk answers false)
   * @param {number=} ms  how long to hold it, clamped to 0.4s-4s
   * @returns {(function()|boolean)} a canceller that restores immediately, or
   *          false when there was nothing to lay
   */
  function previewThemeLook(id, ms) {
    const th = themeById(String(id || ''));
    if (!th || !th.palette) return false;
    const back = () => {
      try { applyTheme(); }
      catch (e) { say('theme preview restore threw: ' + ((e && e.message) || e)); }
    };
    try { applyPalette(th.palette, say); }
    catch (e) { say('theme preview threw: ' + ((e && e.message) || e)); return false; }
    const hold = Math.max(400, Math.min(4000, Number(ms) || 2000));
    const timer = setTimeout(() => { back(); }, hold);
    return function cancelPreview() {
      try { clearTimeout(timer); } catch (e) { /* noop */ }
      back();
    };
  }

  /** The narrow caps the options row is handed. The settings page draws a list
   *  and calls back; it never reads a wallet and never touches the store. */
  function themeCaps() {
    return {
      list: () => ownedThemes(ownsSku),
      current: () => themePick(),
      select: (id) => setThemePick(id),
      swatch: (id) => {
        const th = themeById(id);
        return th ? { accent: th.palette.accent, panel: th.palette.panel, ink: th.palette.ink } : null;
      },
    };
  }

  /* THE BOOT PAINT, and it runs HERE rather than beside the store because every
   * `let` this reaches has to have been evaluated first (a function body's
   * temporal dead zone is per-declaration, and `walletEcho` is right above).
   * Still long before a screen exists - the first showBoard() is the last line
   * of createShell - so there is no frame of the wrong palette. */
  try { applyTheme(); }
  catch (e) { say('campus theme apply threw: ' + ((e && e.message) || e)); }

  /* ------------------------------ THE BRASS BELL -------------------------
   * `bell` resolves to the sample `bell_brass` while the player owns
   * `brass_bell`. shell/audio.js is built in boot.js, not here, so the shell
   * has no handle on it - setBellCosmetic is audio.js's one module-level seam.
   * A GETTER, so a bell bought mid-session rings brass on the very next cue,
   * and so audio.js still imports nothing (trap 18's discipline: the shell
   * hands it an answer, it never goes looking for a wallet). ONE road only -
   * the `set_bell` bus message writes the same slot and must stay unused here.
   * -------------------------------------------------------------------- */
  try { setBellCosmetic(() => ownsSku('brass_bell')); }
  catch (e) { say('bell cosmetic: ' + ((e && e.message) || e)); }

  /* ------------------------------- THE LOCKER ----------------------------
   * The bell's shape again, and for the same reason. Three callers want to open
   * RM 004 (the campus door, the Options signpost, the back of the Student ID)
   * and a fourth wants to EQUIP from a purchase toast, and not one of them is
   * holding a caps bag - `walkThen` in particular calls its action with no
   * arguments at all. So the shell hands locker.js its opener and its caps
   * factory ONCE, here, and `showLocker()` / `equipFromToast(sku)` work from
   * anywhere on the page afterwards. A FACTORY, not a bag: every cap inside it
   * is re-asked at the press, so a jacket bought thirty seconds ago is wearable
   * without a reload (the counter's rule for its own getters).
   * -------------------------------------------------------------------- */
  try { installLocker({ open: () => showLockerScreen(), caps: () => lockerCaps() }); }
  catch (e) { say('locker install: ' + ((e && e.message) || e)); }

  /* ---------------------------- THE PURCHASE REVEAL ----------------------
   * The Locker's shape a third time, and for the third same reason: the module
   * is driven by an EVENT (`arcademy-bought`) rather than by a call site, so
   * there is nobody at the moment it runs to hand it a bag. Every cap is a
   * getter and is re-asked when the card opens, which is what lets a jacket
   * bought thirty seconds ago be wearable from the ceremony without a reload.
   *
   * IT CANNOT SPEND, PRICE OR PERSIST ANYTHING. `equip` is the Locker's own
   * verb (the same one the counter's receipt offers) and `previewTheme` hands
   * back a canceller rather than a setter - the shell stays the one writer of
   * a palette exactly as it is the one writer of a pick.
   * -------------------------------------------------------------------- */
  try {
    installReveal({
      t,
      log: say,
      lite: () => shellLite(),
      reduced: () => reducedMotion,
      isMobile,
      /* The same seed the corkboard deals tonight's poster from and the same
       * one EMI's desk picks tonight's toy with, so the reveal shows the thing
       * the school is actually about to put in front of the player. */
      daySeed: () => utcDateSeed,
      catalog: () => economyCatalog(),
      equip: (sku) => {
        try { return !!equipFromToast(sku); }
        catch (e) { say('reveal equip threw: ' + ((e && e.message) || e)); return false; }
      },
      previewTheme: (id, ms) => previewThemeLook(id, ms),
    });
  } catch (e) { say('reveal install: ' + ((e && e.message) || e)); }

  /* ------------------------------- THE PA PACK ---------------------------
   * Built once, with narrow caps; pa.js dispatches its OWN cue (the line needs
   * bus 'voice', maxMs 8000 and a duck - the tutorial-bus path would cap it at
   * 1.2 s). Every gate is a getter and is re-asked at speak time, so a pack
   * bought while a timer is in the air still speaks tonight, and a mid-session
   * flip of performance mode or motion level is honoured. The "never during a
   * class" gate is the shell's `active`, handed in rather than guessed. */
  try {
    pa = createPa({
      owned: () => ownsSku('pa_pack'),
      t,
      log: say,
      lite: () => shellLite(),
      reduced: () => reducedMotion,
      inClass: () => !!active,
      /* THE DUCK CAP. pa.js scales LINE_DUCK by this exactly the way
       * engine/index.js scales an effect's duck, so the announcement is not the
       * one cue in the school that ignores the player's ducking channel. The
       * HOST's echo is the only thing that moves `src.caps` (settings.js trap
       * 1), and a getter means a change lands on the next line without a
       * rebuild. Absent = 1, which is caps.js's own default. */
      duckDepth: () => {
        const v = src.caps ? src.caps.duckDepth : null;
        return Number.isFinite(+v) ? +v : 1;
      },
      daySeed: utcDateSeed,
    });
  } catch (e) { say('pa unavailable (' + ((e && e.message) || e) + ')'); pa = null; }

  /* ...AND THE WORDS UNDER IT. Built even when `pa` itself failed: the caption
   * listens on the CUE bus, not on pa.js, so a host that fires a PA line some
   * other way still gets its line captioned and the two can never disagree. */
  try {
    paCaption = installPaCaption({
      t,
      log: say,
      lite: () => shellLite(),
      reduced: () => reducedMotion,
      /* SHE STEPS ASIDE WHILE THE SCHOOL TALKS. campusDoorRects() already hands
       * the caption's box to her keep-off rule, but a rule is only read when
       * she re-places - re-arming it with the same getter makes her do that on
       * the spot (widget.js keepClear). Board only: off the campus the rule is
       * not armed at all and this would be a place() for nothing. */
      onChange: () => { if (screen === 'board') keepEmiOffTheDoors(true); },
    });
  } catch (e) { say('pa caption unavailable (' + ((e && e.message) || e) + ')'); paCaption = null; }

  try {
    ost = createOst({ log: say });
  } catch (e) { say('ost unavailable (' + ((e && e.message) || e) + ')'); ost = null; }

  /* ---------------------------- THE EXTRA CREDIT LEVER -------------------
   * ONE pick for the night, not one per door. A player who pulls Honors at the
   * Music Room has decided how they want to play tonight, and asking them again
   * at the next door would be a form rather than a lever. It rides the
   * page-owned meta key `leverPick`, so it survives a screen change and a
   * reload; the HOST re-clamps it on every class-started anyway (an unlock the
   * page thinks it has and the host does not simply grades as Standard).
   * -------------------------------------------------------------------- */

  /** Legal positions, worst to best. Order matters: the lever walks it. */
  const LEVER_POSITIONS = ['standard', 'extra', 'honors'];

  /** The pick, clamped to what is actually unlocked. Never returns junk. */
  function leverPick() {
    let want = 'standard';
    try { want = String(store.get('leverPick') || 'standard'); } catch (e) { want = 'standard'; }
    if (LEVER_POSITIONS.indexOf(want) < 0) return 'standard';
    const un = walletUnlocks();
    if (want === 'honors' && un.honors !== true) return 'standard';
    if (want === 'extra' && un.extra !== true) return 'standard';
    return want;
  }

  /** Set it, clamped the same way, and answer what actually stuck. */
  function setLeverPick(pos) {
    const want = LEVER_POSITIONS.indexOf(String(pos || '')) >= 0 ? String(pos) : 'standard';
    const un = walletUnlocks();
    const ok = want === 'standard'
      || (want === 'extra' && un.extra === true)
      || (want === 'honors' && un.honors === true);
    const next = ok ? want : 'standard';
    try { store.set('leverPick', next); } catch (e) { say('lever pick write failed'); }
    return next;
  }

  /** The bag both class-start surfaces hand their lever control. Narrow caps:
   *  the door card and the room scene draw a lever, they never read a wallet. */
  function leverCaps() {
    return {
      positions: LEVER_POSITIONS.slice(),
      get: () => leverPick(),
      set: (pos) => setLeverPick(pos),
      unlocks: () => walletUnlocks(),
    };
  }

  /* ============================ THE SHELF ===============================
   * The counter is a page under the annex's law (shell/prizecounter.js): it
   * takes readers and callbacks and it imports no store, no bridge and no EMI.
   * The one thing worth reading twice is `onBuy`: it SENDS and returns. There
   * is no optimistic paint anywhere in this screen, because the host owns every
   * balance in it and a page that spent its own money is a page that can be
   * lied to (trap 1, and the whole reason `wallet` is host-owned).
   *
   * IT IS NOT A SCREEN ANY MORE (Locker wave, 2026-08-28). The shelf used to be
   * `screen = 'prizes'`, reached by LEAVING the window you were standing at,
   * which is a shop in a different building from the booth that advertises it.
   * It is a panel inside the booth's plate now - shell/prizebooth.js opens it
   * through scene.js's overlay, the Records Office's arrangement one alley over
   * - and the only thing that moved is where the counter's root is appended.
   * Every cap below is the cap it was.
   * ==================================================================== */

  /** The booth's own close for the panel the counter is sitting in, or null.
   *  Handed down by prizebooth.js at mount and spent by the counter's Back. */
  let prizeShelfClose = null;

  /**
   * Fill the booth's panel with the shop. The BOOTH owns the box and the
   * lifecycle; this owns the catalog, the wallet and the echo, which is the
   * same split the Records Office runs with records.js.
   */
  function mountPrizeShelf(panel, close) {
    dropPrizeShelf();
    prizeShelfClose = (typeof close === 'function') ? close : null;
    docEvent('arcademy-shelf', { open: true });        // emi/shop.js: only browsing?
    prizeRoom = createPrizeCounter({
      mount: panel,
      /* THE ONE LINE THE FOLD COSTS: in flow, in somebody else's box, and the
       * box is the scroller. See THE COUNTER, FOLDED INTO THE BOOTH. */
      embedded: true,
      /* The tray beat is a full-viewport picture and the panel it is mounted in
       * is transformed mid-slide, which would make the panel its containing
       * block. It hangs off the room instead. */
      beatMount: () => (prizeBooth && prizeBooth.root) || null,
      t,
      log: say,
      lite: shellLite(),
      reduced: reducedMotion,
      catalog: () => economyCatalog(),
      balance: () => walletBalance(),
      inv: () => walletInv(),
      unlocks: () => walletUnlocks(),
      stackMax: (sku) => stackMaxFor(sku),
      payday: () => economyPayday(),
      gameName,
      /* THE PROPOSAL, and nothing else. The counter has already put the row to
       * sleep; the host's `wallet-result` lands at onWalletResult below and
       * settles it, or it never lands and the row wakes up unchanged. */
      onBuy: (sku) => {
        try { bridge.send({ type: 'prize-buy', sku: String(sku) }); }
        catch (e) { say('prize-buy send failed: ' + ((e && e.message) || e)); }
      },
      /* THE SECOND HALF OF A PURCHASE. The receipt offers one press for a thing
       * that can be worn, and shell/locker.js answers it - which key an outfit,
       * a frame, a desk toy or a campus look is written to is the Locker's
       * business and has never been the counter's. A FALSE answer means there
       * was nothing to put on and the counter takes the verb back off. */
      equip: (sku) => {
        try { return !!equipFromToast(sku); }
        catch (e) { say('locker equip threw: ' + ((e && e.message) || e)); return false; }
      },
      /* YOU LEAVE THE WAY YOU CAME IN (the annex's rule, one alley over), and
       * now there is only one way in: the panel closes onto the window it was
       * opened over. The purse chip in the chrome arrives with the panel
       * already up, so its Back lands on the same window and its second press
       * walks out to the quad. */
      onBack: () => leavePrizeShelf(),
    });
  }

  /** Take the counter down, from any road, twice if you like. */
  function dropPrizeShelf() {
    prizeShelfClose = null;
    if (!prizeRoom) return;
    const pr = prizeRoom;
    prizeRoom = null;
    docEvent('arcademy-shelf', { open: false });
    try { pr.destroy(); } catch (e) { /* noop */ }
  }

  /**
   * COMPAT. `showPrizes()` was the shelf's own screen for two waves and every
   * caller that predates the fold still asks for it by name. It is one line
   * now: walk nowhere, stand at the window, and open the shelf over it.
   */
  function showPrizes() {
    showPrizeBooth({ skipWalk: true, openShelf: true });
  }

  /* ========================= SCREEN: THE BOOTH =========================
   * THE PRIZE COUNTER'S ANTECHAMBER. The walk brought the student down the
   * office alley; this is standing at the third window. A screen like records
   * in every mechanical way (same funnel, same teardown, same apron), and the
   * shell keeps everything it always kept - the wallet is read through the
   * same getters the shelf reads, and the booth proposes nothing.
   *
   * THE SHUTTER IS THE SHELL'S ANSWER, NOT THE ROOM'S. `counterClosed()` below
   * is the one predicate, and it is the same pair of facts every other dark
   * thing on this campus is drawn from: no catalog projected in `init` (no
   * economy at all, which is also an entitlement that has lapsed - the host
   * simply stops projecting one) or a global suspend in force. The room draws
   * the answer and re-draws it on `setClosed`; it never works it out.
   * ==================================================================== */

  /** Is the counter shut right now? One predicate, read at build and again on
   *  every suspend edge. */
  function counterClosed() {
    if (suspendedGlobally) return true;
    try { return !economyCatalog().length; } catch (e) { return true; }
  }

  /**
   * @param {Object=} o
   *   skipWalk  - true when the player did not cross the quad to get here (the
   *               purse chip in the chrome). The ARRIVAL BEAT is skipped with
   *               the walk, because an arrival you did not travel to is a
   *               cutscene. The plate is never skipped.
   *   openShelf - true to land with the shop already open over the window.
   */
  function showPrizeBooth(o) {
    const opt = o || {};
    screen = 'prizebooth';
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    clearScreen();
    renderTopbar();
    /* THE COUNTER'S TUNE. One track for the whole place now: the shelf opens
     * over this plate rather than replacing it, so there is no second enter to
     * defer past (ost.js law 5 still holds, it simply has nothing to do). */
    try { if (ost) ost.enter('prizes'); } catch (e) { /* noop */ }
    prizeBooth = createPrizeBooth({
      mount: dom && dom.screen,
      t,
      log: say,
      lite: shellLite(),
      reduced: reducedMotion,
      closed: counterClosed(),
      alley: !opt.skipWalk,
      /* The same getters the shelf takes, and for the same reason: the tray on
       * the sill READS a wallet, it never moves one. `catalog`/`inv`/`stackMax`
       * joined the list for the holdings tray (counter shortcut wave,
       * 2026-08-30) - what you are holding is three reads of the same three
       * facts the shelf already reads, and not one of them is a proposal.
       *
       * NO `onUse`. Nothing on this shelf can be spent by hand: the one
       * consumable, `late_slip` (THE TARDY SLIP), is burned by the HOST inside
       * the attendance credit (ArcademyEconomy.ConsumeLateSlips), so there is no
       * press to wire
       * and the tray says so in words instead of growing a button that lies. */
      balance: () => walletBalance(),
      payday: () => economyPayday(),
      catalog: () => economyCatalog(),
      inv: () => walletInv(),
      stackMax: (sku) => stackMaxFor(sku),
      gameName,
      /* THE BOOTH OWNS THE BOX, THE SHELL OWNS WHAT IS IN IT. The room hands
       * over a panel and the way out of it; the counter and every cap it reads
       * are minted here, where the store and the bridge live. */
      onShop: (panel, close) => mountPrizeShelf(panel, close),
      onShopClosed: () => dropPrizeShelf(),
      onBack: () => showBoard(),
      /* ONE WINDOW FURTHER DOWN. `prizebooth` and `locker` are SIBLINGS in
       * SCREEN_DEPTH, both depth 1, so this is a walk along the alley and not a
       * step into anything: the Locker's own back slab still says campus and
       * still means it, and the Locker's left-hand sign is the return leg. */
      onLocker: () => showLocker(),
    });
    setStage('arc-report-on');
    if (prizeBooth && typeof prizeBooth.fit === 'function') prizeBooth.fit();
    /* THE PURSE CHIP'S ARRIVAL. Asked AFTER the fit, so the panel opens over a
     * plate that has already been measured to the window. */
    if (opt.openShelf && prizeBooth && typeof prizeBooth.openShop === 'function') {
      try { prizeBooth.openShop(); } catch (e) { say('prize shelf failed to open'); }
    }
  }

  /** The shelf's one way out, so the counter's Back button and the Esc fold can
   *  never disagree about where it goes: BOTH close the panel and leave the
   *  player at the window. The second press is the booth's own rung, and that
   *  one walks out to the quad. */
  function leavePrizeShelf() {
    const off = prizeShelfClose;
    prizeShelfClose = null;
    if (off) { try { off(); return; } catch (e) { /* fall through to the quad */ } }
    /* No panel to close means no booth either (the counter cannot be up without
     * one), so the honest answer is the campus. */
    showBoard();
  }

  /**
   * THE ECHO (host `wallet-result`). Everything a purchase changes is already
   * in the host's meta by the time this arrives, so the counter is handed the
   * frame and the rest of the page simply re-reads. When the counter is not up
   * (a buy answered after the player walked out) the frame is still worth
   * having: the lever's unlocks may have moved.
   */
  function onWalletResult(m) {
    const frame = m || {};
    noteWalletEcho(frame);
    if (prizeRoom && typeof prizeRoom.settle === 'function') {
      try { prizeRoom.settle(frame); }
      catch (e) { say('wallet settle threw: ' + ((e && e.message) || e)); }
    }
    /* A lever position that was legal a second ago may not be any more (it can
     * only ever have been WON here, never lost, but re-clamping is free and it
     * is the one line that stops a stale pick outliving a wallet reset). */
    setLeverPick(leverPick());
    /* THE CAMPUS LOOK rides the same line for the same two reasons, plus one of
     * its own: a theme BOUGHT this second must light no later than the next
     * screen paint without a reload (the restock's activation law). applyTheme
     * is idempotent, so an echo about a late slip repaints the same thirteen
     * values and costs nothing. */
    try { applyTheme(); }
    catch (e) { say('campus theme re-apply threw: ' + ((e && e.message) || e)); }
    /* EMI's prizes ride the same line for the same reason (the restock's
     * activation law). setPrizes is idempotent - it re-reads two booleans. */
    try { if (emi && typeof emi.setPrizes === 'function') emi.setPrizes(); }
    catch (e) { say('EMI prize re-read threw: ' + ((e && e.message) || e)); }
    if (screen === 'board' && campus) {
      try { campus.update(buildCampusState(), campusStats()); } catch (e) { /* noop */ }
    }

    /* ------------------------- THE ANNOUNCEMENT --------------------------
     * `arcademy-bought` on `document`, and this is the ONLY place in the page
     * it is ever fired. Three things hang off it - the purchase reveal, EMI's
     * line about what you just bought, and anything a later wave adds - and
     * none of them has to know what a `wallet-result` frame looks like.
     *
     * IT IS THE LAST LINE OF THE ECHO ON PURPOSE. Everything above has already
     * run: the wallet says the thing is owned, the theme is laid, EMI's props
     * are re-read and the campus is repainted. So a ceremony that asks "do I
     * own this" gets yes, and one that lays a palette for two seconds lays it
     * OVER the settled look rather than under it.
     *
     * A REFUSAL OPENS NOTHING. `ok !== true` is a frame where nothing was
     * bought and nothing was charged - there is no moment to celebrate, and
     * the counter has already said the sentence that fits. It gets its OWN
     * quieter event (`arcademy-refused`) rather than a ceremony: EMI has one
     * sympathy line for `reason === 'poor'` and ignores every other reason,
     * and a listener that cannot tell a yes from a no is a listener that will
     * eventually congratulate somebody for being broke.
     * ------------------------------------------------------------------ */
    if (frame.ok !== true && frame.sku) {
      docEvent('arcademy-refused', {
        sku: String(frame.sku),
        reason: String(frame.reason || ''),
      });
    }
    if (frame.ok === true && frame.sku) {
      const sku = String(frame.sku);
      const row = catalogRow(sku);
      const detail = {
        sku,
        kind: kindOf(sku, row),
        name: row ? t(row.nameKey || '', row.nameEn || sku) : sku,
        cost: row ? Number(row.cost) || 0 : 0,
        cur: row && row.cur === 'k' ? 'k' : 't',
      };
      const send = () => {
        try { fireBought(detail); }
        catch (e) { say('bought announce threw: ' + ((e && e.message) || e)); }
      };
      /* ONE CEREMONY, NOT TWO (wave 0828, F). `prizeRoom.settle` ran at the top
       * of this function and, on a yes, started THE BANK: the cost leaving the
       * wallet chip and landing on the row, and then the tray at the sill. The
       * card is the third stroke of that same gesture, so it waits for the
       * first two - fired here and now it would whoosh up THROUGH the coins,
       * and two celebrations at once cancel each other.
       *
       * IT IS A DELAY AND NEVER A GATE. `boughtHoldMs` answers 0 whenever there
       * is no bank in the air - reduced motion, a counter that could not
       * measure, a purchase settled from anywhere but the shelf - and it can
       * never answer more than its own ceiling, so the worst a bug in the
       * counter's motion kit can do is make the card late. The frame is already
       * settled: the wallet, the inventory and the row moved before this line,
       * and nothing below it can change what was bought. */
      const wait = boughtHoldMs();
      if (wait > 0) setTimeout(send, wait); else send();
    }
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
    try { if (ost) ost.enter('records'); } catch (e) { /* noop */ }
    recordsRoom = createRecordsRoom({
      mount: dom && dom.screen,
      t,
      log: say,
      lite: shellLite(),
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

  /* ============================ SCREEN: THE LOCKER ======================
   * RM 004, and the Records Office's arrangement one alley over: a painted room
   * with the whole of its content folded into a scene overlay, so the shell
   * owns the screen and shell/locker.js owns everything inside the plate.
   *
   * NARROW CAPS, the annex's law. The room is handed readers and callbacks and
   * imports no store, no bridge and no wallet - which is what lets it be the
   * page that decides what EMI wears without ever being the page that decides
   * whether you own it.
   * ==================================================================== */

  /**
   * The bag `installLocker` re-asks on every open and on every toast equip.
   * Nothing in here is a value; they are all questions, because the Locker is
   * the one screen in the school whose whole job is to be right about a wallet
   * that moved while the player was looking at it.
   */
  function lockerCaps() {
    return {
      mount: dom && dom.screen,
      t,
      log: say,
      lite: shellLite(),
      reduced: reducedMotion,
      isMobile,
      /* THE OWNERSHIP WITNESS, and the only one. Law 1 in locker.js hangs off
       * it: an unowned thing is ABSENT from that room, never padlocked. */
      ownsSku,
      catalog: economyCatalog,
      inv: walletInv,
      settingUnlocks,
      /* THE THREE KEYS. Page-owned meta, the theme picker's road exactly. The
       * write normalises "no pick" to an empty string rather than null so the
       * store only ever holds strings for these three - a reader that finds ''
       * and a reader that finds a missing key then agree without a special
       * case, which is what `lockerOutfit()` above is counting on. */
      meta: {
        get: (key) => store.get(key),
        set: (key, value) => {
          const next = (typeof value === 'string' && value) ? value : '';
          try { return store.set(key, next); }
          catch (e) { say('locker meta write failed (' + key + ')'); return null; }
        },
      },
      /* THE CAMPUS LOOK, moved house. The same four caps the Options sheet drew
       * with for two waves, handed to the room the picker lives in now; the key
       * and the palette are still the shell's and always were. */
      themes: themeCaps(),
      emi: () => { try { return getEmi(); } catch (e) { return null; } },
      /* THE BELL'S PREVIEW is a plain `bell` cue through the one audio door -
       * audio.js already resolves brass off the cosmetic getter installed
       * above, so the room hears the real thing rather than a second copy of
       * it, and the `set_bell` bus message stays unused (it writes the
       * ownership slot, which a preview must never touch). */
      bellOwned: () => ownsSku('brass_bell'),
      /* A FRAME CHANGED. BOTH CARDS WEAR IT, and for two waves only one did -
       * the spotlight's 560px card repainted and the laminated card in the
       * corner of the quad never heard about it, so a 300-ticket frame read as
       * "does nothing" to anyone who bought one and walked back outside.
       * The spotlight is never up while the Locker is (clearScreen dismisses
       * it) but the toast's equip verb can fire from anywhere, and the campus
       * IS still mounted underneath - so it is the one that actually needs
       * telling. setProfile() is the spotlight's own repaint and it ends in
       * paintFrame(); refreshFrame() is the campus's. */
      refreshIdCard: () => {
        try { if (campus && campus.refreshFrame) campus.refreshFrame(); }
        catch (e) { /* the corner card repaints on the next update() regardless */ }
        try { if (idSpotlight && idSpotlight.isOpen && idSpotlight.isOpen()) idSpotlight.setProfile(); }
        catch (e) { /* the card repaints on its next open regardless */ }
      },
      toast: shout,
      /* THE FOOTER'S ONE LINK. "N more at the counter" is the only place the
       * Locker admits an unowned thing exists, and it never names one. */
      openCounter: () => showPrizes(),
      /* THE LEFT-HAND SIGN, and note it is NOT showPrizes(). That verb arrives
       * with the shelf already open because it is the shortcut a shopper asked
       * for; this one arrives standing at the window, because somebody who
       * pressed a sign on a wall is WALKING. `skipWalk` for the same reason the
       * purse chip takes it: a lateral bounce between two neighbours does not
       * get to replay the corridor cutscene every time. */
      onCounter: () => showPrizeBooth({ skipWalk: true }),
      onBack: () => showBoard(),
    };
  }

  function showLockerScreen() {
    const from = screen;             // emi/shop.js: sent down the hall, or walked?
    screen = 'locker';
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    clearScreen();
    renderTopbar();
    /* NO TUNE. clearScreen has already left whatever was playing, and silence
     * is the default between places (the lab and Options take it too). The
     * counter's track belongs to the counter; carrying it one window down would
     * make the Locker sound like part of the shop, which is the exact reading
     * the room was built to correct. */
    lockerRoom = createLocker(lockerCaps());
    if (!lockerRoom) {
      say('the Locker would not mount');
      showBoard();
      return;
    }
    setStage('arc-report-on');
    if (typeof lockerRoom.fit === 'function') lockerRoom.fit();
    docEvent('arcademy-locker-opened', { from: from === 'prizebooth' ? 'counter' : from });
  }

  /* ========================= THE BACK ROOM =============================
   * BACKROOM-CONTRACT §4. The fifth window in the alley, and the one room in
   * this bundle the shell does not import at the top of the file.
   *
   * WHY IT IS A DYNAMIC IMPORT. Every other screen here is a page that most
   * players open on most nights. The casino wing is a floor of tables, a cage
   * and a cabinet, and a player who never opens that door should not pay for
   * any of it on a phone that is already the slowest thing in the room. So the
   * module is fetched on the CLICK, cached once it lands, and a host that does
   * not ship the directory at all simply never resolves it - which is not an
   * error, it is a school with one door still boarded up.
   *
   * AND THE SCREEN DOES NOT MOVE UNTIL IT LANDS. showBackRoom is called at the
   * end of the walk with the campus still standing, and it stays standing while
   * the import is in the air: a failure raises the plan's own dust sheet over a
   * quad that never went anywhere, so there is nothing to "return" to and no
   * empty screen for a player to be stranded on.
   *
   * THE CTX (documented here because the module is not in this repo yet):
   *   t(key, fallback)      the lexicon, already merged with the mod skin
   *   lite / reduced        the phone's cut-down flag, the motion setting
   *   log(line)             the shell's console channel
   *   subject               init.subject, read-only
   *   localDay              'yyyy-mm-dd', the LOCAL day the cage counts by
   *   gameName(key)         a class's display name
   *   balance()             {t, k, c} - the purse, host-owned
   *   earnedC()             lifetime chips won, host-owned
   *   state() / save(patch) one page-owned blob in the meta store
   *   send(msg)             casino-request / triggers-request, and nothing else
   *   listen(type, fn)      casino-result / triggers-result; returns unsubscribe
   *   onExit()              the way out, and the way out is the quad
   * ==================================================================== */

  /** Every live `listen` the room took out, so a room that forgets to drop one
   *  cannot leave a handler painting into a page that has been torn down. */
  let backRoomOffs = [];

  /* THE SIGN OVER THE DOOR (the soft launch). The door is drawn ONLY when the
   * server says the house is open for this player: a `casino-request` for
   * status answers `body.open`, and anything else - offline, a 403, a timeout,
   * a server that predates the flag - is a wall with no door in it. Asked on
   * the campus build, throttled, and never blocking: the quad stands first
   * and the door is lit when the answer lands. The ask is the shell's own and
   * filters by its own reqId, so it can never eat a receipt the room is
   * waiting on. No host change: both hosts already carry this frame. */
  let backRoomOpen = false;
  let backRoomAskedAt = 0;
  const BACKROOM_ASK_EVERY_MS = 5 * 60 * 1000;
  const BACKROOM_ASK_TIMEOUT_MS = 4000;
  function setBackRoomOpen(open) {
    backRoomOpen = open === true;
    if (campus && typeof campus.setBackroomOpen === 'function') {
      try { campus.setBackroomOpen(backRoomOpen); } catch (e) { /* noop */ }
    }
  }
  function askBackRoomOpen() {
    const now = Date.now();
    if (now - backRoomAskedAt < BACKROOM_ASK_EVERY_MS) { setBackRoomOpen(backRoomOpen); return; }
    backRoomAskedAt = now;
    const reqId = 'bkgate' + now.toString(16)
      + Math.floor(Math.random() * 0xFFFFFFFF).toString(16).padStart(8, '0');
    let off = () => {};
    const timer = setTimeout(() => { try { off(); } catch (e) { /* noop */ } }, BACKROOM_ASK_TIMEOUT_MS);
    off = bridge.on('casino-result', (m) => {
      if (!m || m.reqId !== reqId) return;
      clearTimeout(timer);
      try { off(); } catch (e) { /* noop */ }
      setBackRoomOpen(m.ok === true && !!m.body && m.body.open === true);
    });
    try {
      bridge.send({ type: 'casino-request', reqId, op: 'status', body: {}, localDay: localDate });
    } catch (e) {
      clearTimeout(timer);
      try { off(); } catch (e2) { /* noop */ }
      say('backroom gate ask failed: ' + ((e && e.message) || e));
    }
  }

  /** The module, fetched once. A FAILURE IS NOT CACHED - a fetch that lost the
   *  network is worth trying again the next time somebody tries the door. */
  function loadBackRoom() {
    if (backRoomMod) return Promise.resolve(backRoomMod);
    return import('../backroom/index.js').then((mod) => {
      backRoomMod = mod;
      return mod;
    });
  }

  /** The dust sheet, and it is the PLAN's card - every other shut door on this
   *  campus is refused with that same card, and a second refusal surface here
   *  would be the school speaking in two voices about one locked room. */
  function refuseBackRoom(why) {
    say('the Back Room did not open: ' + why);
    if (campus && typeof campus.backroomDust === 'function') {
      try { campus.backroomDust(); return; } catch (e) { /* fall through to a line */ }
    }
    try {
      shout(t('backroom_dust_line',
        'Sheets over the tables and the lights off at the wall. Another night.'));
    } catch (e) { /* a toast may never hold a door */ }
  }

  /** NARROW CAPS, THE ANNEX'S LAW, plus the two wire verbs the contract adds. */
  function backRoomCaps() {
    return {
      t,
      lite: shellLite(),
      reduced: reducedMotion,
      log: say,
      subject: src.subject || {},
      localDay: localDate,
      gameName,
      balance: () => walletBalance(),
      /* LIFETIME CHIPS, off the same echo the balance rides. It is a READ of
       * what the host banked and never a count this page keeps. */
      earnedC: () => {
        const n = Number(walletBag().earnedC);
        return Number.isFinite(n) && n > 0 ? n : 0;
      },
      // ONE page-owned blob, not N keys (the meta-store headroom law).
      state: () => store.get('backroom') || {},
      save: (patch) => {
        try { store.merge('backroom', patch); }
        catch (e) { say('backroom merge failed: ' + ((e && e.message) || e)); }
      },
      /* THE WIRE. Two verbs, two whitelists, and the room never sees `bridge`. */
      send: (msg) => {
        const type = String((msg && msg.type) || '');
        if (BACKROOM_SENDS.indexOf(type) < 0) {
          say('backroom send refused: ' + (type || '(no type)'));
          return false;
        }
        try { bridge.send(msg); return true; }
        catch (e) { say('backroom send failed: ' + ((e && e.message) || e)); return false; }
      },
      listen: (type, fn) => {
        const key = String(type || '');
        if (BACKROOM_HEARS.indexOf(key) < 0 || typeof fn !== 'function') {
          say('backroom listen refused: ' + (key || '(no type)'));
          return () => {};
        }
        let off = () => {};
        try { off = bridge.on(key, fn) || (() => {}); }
        catch (e) { say('backroom listen failed: ' + ((e && e.message) || e)); return () => {}; }
        backRoomOffs.push(off);
        return () => {
          const i = backRoomOffs.indexOf(off);
          if (i >= 0) backRoomOffs.splice(i, 1);
          try { off(); } catch (e) { /* noop */ }
        };
      },
      onExit: () => showBoard(),
    };
  }

  function showBackRoom() {
    // A door that is not drawn cannot be walked to, but a keybind or a stale
    // handle could still ask. The sealed card, same as any other shut door.
    if (!backRoomOpen) { refuseBackRoom('the house is closed'); return; }
    /* THE QUAD STAYS UP while the module is in the air. See the header. */
    loadBackRoom().then((mod) => {
      if (destroyed) return;
      /* The player walked out while the fetch was running (Esc, a toast's Back,
       * a suspend). A room minted into a screen that has moved on is exactly
       * the class-into-a-dead-screen bug the walk funnel is written to avoid. */
      if (screen !== 'board') return;
      if (!mod || typeof mod.openBackRoom !== 'function') {
        refuseBackRoom('the module has no openBackRoom');
        return;
      }
      screen = 'backroom';
      dismissEndCard();
      dismissPunchStage();
      dismissAnnexStage();
      clearScreen();
      renderTopbar();
      let page = null;
      try { page = mod.openBackRoom(backRoomCaps()); }
      catch (e) { page = null; say('the Back Room threw on open: ' + ((e && e.message) || e)); }
      if (!page || !page.root) {
        /* It refused to build AFTER the quad came down, which is the one path
         * that can strand somebody. The board is the honest place to put them
         * back, and the dust sheet is raised on the campus that comes with it. */
        backRoomPage = null;
        showBoard();
        refuseBackRoom('the room would not mount');
        return;
      }
      backRoomPage = page;
      if (dom && dom.screen) dom.screen.appendChild(page.root);
      setStage('arc-report-on');
      if (typeof page.fit === 'function') page.fit();
      docEvent('arcademy-backroom-opened', {});
    }).catch((e) => {
      if (destroyed) return;
      refuseBackRoom('module load failed: ' + ((e && e.message) || e));
    });
  }

  /* IS THE BOOT SPLASH STILL THE THING ON SCREEN? `#arc-toast` is z 60 and
   * `.arc-loader` is z 70, so a toast fired during construction is a toast
   * nobody ever sees - it expires (2.2s) under a splash whose own floor is
   * longer than that. boot.js owns the loader, so this is a READ of its node
   * and nothing more; a page without one (every headless suite) answers false
   * and every toast is spoken the moment it is asked for, exactly as before. */
  function splashUp() {
    try {
      if (typeof document === 'undefined' || !document.getElementById) return false;
      const node = document.getElementById('arc-loader');
      return !!(node && !node.hidden);
    } catch (e) { return false; }
  }
  /** The one line parked behind the splash, or '' (see splashUp). */
  let pendingShout = '';
  /** Say it now, or on the splash edge - whichever the player can actually read. */
  function shoutOnScreen(msg) {
    const line = String(msg || '');
    if (!line) return;
    if (!splashUp()) { try { shout(line); } catch (e) { /* noop */ } return; }
    pendingShout = line;
  }
  /** Called by `onSplashDone`, once, and only ever on the happy path. */
  function flushPendingShout() {
    if (!pendingShout) return;
    const line = pendingShout;
    pendingShout = '';
    try { shout(line); } catch (e) { /* a toast may never hold a door */ }
  }

  /**
   * ONE SHOT, ONE BOOT. The host asked for a room by key (`init.launchGame`);
   * open it if the card is full, otherwise stay on the campus and say why. An
   * unknown or retired key has no card and therefore no grant, which is the
   * same refusal by the same test - there is no second table to keep in step.
   *
   * STILL THE ONLY THING THAT FIRES A LAUNCH, and still exactly once per boot:
   * the direct-launch road below does not launch anything itself, it decides
   * WHEN this is called and whether a campus is built around it (see the tail
   * of createShell). `launchFired` is the whole guard and it is spent here.
   */
  function maybeLaunchRequested() {
    if (launchFired || destroyed) return;
    launchFired = true;
    if (!launchRequest) return;
    if (isUnlocked(launchRequest)) {
      say('activity launch: ' + launchRequest + ' - card complete, opening the room');
      launchGraded(launchRequest);
      /* THE SPLASH IS TOLD (boot.js listens). Fired AFTER `launchGraded` and
       * read off `screen` rather than off the intent, so a class the suspend
       * gate turned away is reported as the refusal it actually is and the
       * splash takes the room's name back off. */
      docEvent('arcademy-direct-launch', { gameKey: launchRequest, ok: screen === 'class' });
      return;
    }
    say('activity launch refused: ' + launchRequest + ' - card not complete');
    docEvent('arcademy-direct-launch', { gameKey: launchRequest, ok: false });
    shoutOnScreen(t('launch_card_locked', 'That card is not complete yet. Fill it first.'));
  }

  /** How long a DIRECT LAUNCH waits for the host to say anything at all about
   *  punch cards before it gives up and opens the campus instead. The hosted
   *  shells send `meta` with `init`, so this is the never-arrives parachute and
   *  not a step anybody is expected to stand on. */
  const DIRECT_LAUNCH_WAIT_MS = 10000;

  /** Has the host spoken about punch cards at all? `undefined` is "not yet";
   *  an empty object is a real answer (a player who has never filled one). */
  function cardsKnown() { return store.get('punchCards') !== undefined; }

  /**
   * THE DIRECT LAUNCH's ONE WAIT, and the only thing between a `/deepend` and
   * the pool. It resolves as soon as the requested card reads complete, or as
   * soon as the host has said anything about cards at all (a real "no"), or on
   * the parachute above.
   *
   * `store.onChange` is the seam because the store subscribes to `meta` frames
   * ITSELF, at construction - a snapshot that lands while the shell is still
   * being built reaches the cache, where boot.js's own `if (shell)` handler
   * would have dropped it.
   */
  function awaitDirectLaunchCards() {
    if (isUnlocked(launchRequest) || cardsKnown()) return Promise.resolve();
    say('direct launch: no punch cards yet - holding the splash for them');
    return new Promise((resolve) => {
      let spent = false;
      let off = () => {};
      const finish = (why) => {
        if (spent) return;
        spent = true;
        clearTimeout(timer);
        try { off(); } catch (e) { /* noop */ }
        say('direct launch: ' + why);
        resolve();
      };
      const timer = setTimeout(
        () => finish('the cards never arrived - opening the campus'),
        DIRECT_LAUNCH_WAIT_MS
      );
      off = store.onChange(() => {
        if (isUnlocked(launchRequest) || cardsKnown()) finish('cards arrived');
      });
    });
  }

  /**
   * The unlock box's / end card's Discord sentence for a class, or '' when the
   * class has no slash command (a retired key). `{cmd}` comes from the registry
   * table - the two-repo contract with CCP-Server `bot/arcademy-activity.js` -
   * so a mod may re-voice the sentence but can never rename the command.
   */
  function discordLine(gameKey) {
    const cmd = DISCORD_COMMAND[gameKey] || '';
    if (!cmd) return '';
    return String(t('punchcard_unlocked_discord',
      'Even in Discord: type {cmd} in the CCP server to play it anytime.'))
      .replace('{cmd}', cmd);
  }

  /** Two sentences in one line, and one of them may be missing. */
  function joinNote(a, b) {
    const first = String(a || '').trim();
    const second = String(b || '').trim();
    if (!second) return first;
    return first ? (first + ' ' + second) : second;
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
      lite: shellLite(),
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
      /* THE WAGER, on the second surface. The room replaces the door card for
       * every painted room, and the card is where the Extra Credit rail lives -
       * so a painted room without this would be a room where the lever does not
       * exist, which is nine rooms out of ten. Null when the host sent no
       * catalog: no economy, no rail, exactly like the card. */
      lever: economyCatalog().length ? leverCaps() : null,
    });
    if (dom && dom.screen) dom.screen.appendChild(roomPage.root);
    setStage('arc-report-on');
    if (typeof roomPage.fit === 'function') roomPage.fit();
    // EMI SEAM: standing in the painted room, nothing armed yet. Always BEFORE
    // classStart, which has eleven pools of its own further in.
    fireMoment('campus.roomEntered', { gameKey, done, scheduled, inClass: false });
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
  /** 'yyyy-mm-dd' as a whole day count. Calendar arithmetic only - the keys
   *  ARE local dates (trap 8) and never go near a timezone on the way back. */
  function annexDayNumber(key) {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(key || ''));
    if (!m) return 0;
    return Math.floor(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3])) / 86400000);
  }

  function showAnnex() {
    screen = 'annex';
    dismissEndCard();
    dismissPunchStage();
    dismissAnnexStage();
    clearScreen();
    try { if (ost) ost.enter('annex'); } catch (e) { /* noop */ }
    renderTopbar();
    try { const v = getVoice(); if (v && v.setLabSeen) v.setLabSeen(true); } catch (e) { /* noop */ }
    try {
      const emi = getEmi();
      if (emi && emi.setEnabled) { annexEmiPrev = !!emi.enabled; emi.setEnabled(false); }
    } catch (e) { /* noop */ }
    annexPage = createAnnexLab({
      t,
      lite: shellLite(),
      log: say,
      subject: src.subject || {},
      gamesList: games.list.map((e) => e.key),
      gameName,
      // ONE page-owned blob, not N keys (the meta-store headroom law).
      annexState: () => store.get('annex') || {},
      saveAnnex: (patch) => { try { store.merge('annex', patch); } catch (e) { say('annex merge failed'); } },
      liveFile: () => subjectFile(),
      fetchStats: () => requestAnnexStats(),
      /* THE ONE LIVE THING ON PAPER: nights attended per week for the last
       * six weeks, oldest first, off the page's own graded `days`. A night
       * counts once whatever it graded. No rows at all answers [] and the
       * paper draws nothing - a strip that invents a bar is the one lie this
       * room does not tell. */
      attendance: () => {
        const out = [0, 0, 0, 0, 0, 0];
        let any = false;
        try {
          const today = annexDayNumber(localDate);
          if (!today) return [];
          const days = store.get('days') || {};
          for (const key of Object.keys(days)) {
            const row = days[key];
            const classes = (row && row.classes) || null;
            if (!classes || !Object.keys(classes).length) continue;
            const n = annexDayNumber(key);
            if (!n) continue;
            const back = today - n;
            if (back < 0 || back > 41) continue;
            out[5 - Math.floor(back / 7)] += 1;
            any = true;
          }
        } catch (e) { return []; }
        return any ? out : [];
      },
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

  /* THE SHARE CARD'S LAST RUNG BEFORE THE FLOOR. WebView2 has no async
   * clipboard image write the page can use, so the desktop app carries the PNG
   * over the bridge and C# puts it on the Windows clipboard itself. Same shape
   * as the registry link above: one request in flight, a deadline, and every
   * failure resolves FALSE rather than hanging a button forever. A web build
   * never gets here - reportcard.js is handed this only on the app. */
  function shareImageToHost(png) {
    if (typeof png !== 'string' || !png) return Promise.resolve(false);
    if (shareImageWait) return Promise.resolve(false);
    const w = {};
    w.promise = new Promise((resolve) => {
      w.resolve = resolve;
      w.timer = setTimeout(() => { shareImageWait = null; resolve(false); }, 8000);
    });
    shareImageWait = w;
    try { bridge.send({ type: 'share-image', png }); }
    catch (e) {
      clearTimeout(w.timer);
      shareImageWait = null;
      return Promise.resolve(false);
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
  function streakFacts() {
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
    return { n, credited };
  }

  function jeopardyLine() {
    const f = streakFacts();
    if (!f) return null;
    const tpl = f.credited
      ? t('rake_streak_credited', 'Attendance x{n} is banked for today already.')
      : t('rake_streak_cold', 'Attendance x{n} goes cold if today ends here.');
    return String(tpl).replace('{n}', String(f.n));
  }

  /* ---------------- THE TARDY SLIP, OFFERED ONCE AND QUIETLY -------------
   * House Book Deck V: the loss is disguised as a purchase. The jeopardy line
   * above is the LOSS; this is the purchase, and it is ONE SMALL BUTTON that
   * WALKS TO THE COUNTER. It never buys anything (the counter's echo law is
   * the only thing that may move a wallet, trap 1), it never appears when a
   * slip is already in the bag - the cover is bought, and saying so a second
   * time is the nag Law VI forbids - never on a night the streak is already
   * banked, never when the shutter is down, and never when the shelf does not
   * stock the row at all. The line above it reads exactly the same whether the
   * button is there or not, so nothing about the jeopardy changes to sell.
   * -------------------------------------------------------------------- */

  /** The wire id of the Tardy Slip. It is `late_slip` and it always will be:
   *  the sku is a KEY (held inventory, the server catalog, EMI's bark pool),
   *  and only the words on the shelf were ever rebranded. */
  const SKU_TARDY_SLIP = 'late_slip';

  /** How many slips are in the bag. A COUNT - the desk holds two. */
  function slipsHeld() {
    try {
      const row = walletInv()[SKU_TARDY_SLIP];
      const n = row && typeof row === 'object' ? Number(row.n) : 0;
      return Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
    } catch (e) { return 0; }
  }

  /** Does the offer stand right now? Five facts, and every one of them is the
   *  host's own - the page invents no jeopardy and no stock. */
  function slipOfferWanted() {
    if (slipsHeld() > 0) return false;                  // already covered
    if (counterClosed()) return false;                  // the shutter answers first
    if (!catalogRow(SKU_TARDY_SLIP)) return false;      // the shelf does not stock it
    const f = streakFacts();
    return !!f && !f.credited;                          // only a COLD streak is jeopardy
  }

  /** OUT OF THE CLASS AND INTO RM 003. `teardownClass()` first because the
   *  counter's own screen does not tear a live class down - only showBoard()
   *  does - and a class left standing behind the shelf would keep its clock,
   *  its listeners and its cameo. Then the booth, WITH its walk: the player
   *  did cross the quad to get here, even if the quad never painted. */
  function walkToCounter() {
    say('tardy slip offer taken - walking to the counter');
    try { teardownClass(); } catch (e) { /* noop */ }
    showPrizeBooth({});
  }

  /** The one small button, or null when the offer does not stand. Mounting it
   *  is the caller's business; it is never a second answer to the question the
   *  card is asking, and it never holds focus. */
  function slipOfferButton() {
    if (!slipOfferWanted()) return null;
    const name = t('prize_late_slip', 'Tardy Slip');
    const label = String(t('rake_slip_offer', 'The counter sells a {name}.'))
      .replace('{name}', name);
    const b = el('button', 'btn ghost arc-slip-offer', label);
    b.type = 'button';
    b.addEventListener('click', () => walkToCounter());
    return b;
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
    /* W3 P0-28: THE CARD IS A SEQUENCE, NOT A FRAME. The stamp and the payoff
     * used to land on the same tick as the card itself, so the biggest beat in
     * the night arrived from nowhere and three sounds fought over one moment.
     * Now: the card arrives on a whoosh, 350ms of nothing (that silence IS the
     * anticipation), the stamp comes down, 500ms more, and the payoff pays.
     * The timers are ceremonies' own, so a screen change sweeps them; each one
     * still checks the card is the card it was built for, because `endCard` is
     * assigned below and a dismiss can beat either beat to the frame.
     * The payoff keeps its own jackpot duck - that lives in ceremonies. */
    const mine = () => !!(endCard && endCard.root === root);
    sfx('whoosh', 0.2);
    ceremonies.later(() => {
      if (!mine()) return;
      try {
        ceremonies.gradeObject({
          grade: graded && graded.grade,
          zen: !!(graded && graded.zen),
          target: gradeHost,
          hold: 600000,             // it is the card's subject, not a flash
        });
      } catch (e) { say('end card grade object failed: ' + ((e && e.message) || e)); }
    }, 350);

    card.appendChild(el('h2', 'arc-h2', gameName(gameKey)));

    /* --- LOSSES DISGUISED: a C, or a dropped hard gate, still gets a beat --- */
    ceremonies.later(() => {
      if (!mine()) return;
      try {
        const kind = ceremonies.payoff({
          grade: graded && graded.grade,
          zen: !!(graded && graded.zen),
          capped,
          target: gradeHost,
        });
        say('end card payoff (' + gameKey + '): ' + kind);
      } catch (e) { say('end card payoff failed: ' + ((e && e.message) || e)); }
    }, 850);

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
    /* W3 P1-18: THE METER SAYS HOW CLOSE. It is the only cross-class progress
     * the player ever sees and it painted in silence. One `streak` note, pitched
     * by the fill, 400ms after the payoff so it is the last thing on the card
     * rather than a fourth voice in the pile-up. Drops bus (it is progress
     * toward a payout), and it never runs ahead of a dismiss. */
    ceremonies.later(() => {
      if (!mine()) return;
      sfx('streak', 0.3, { bus: 'drops', pitch: 1 + 0.5 * (Number(prog.eased) || 0) });
    }, 1250);

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
      /* EMI SEAM (heartbeat wave, 2026-08-25). `rareDrop` has carried a MOMENTS
       * row and a bark pool since the first wave and had NO caller anywhere, so
       * the whole beat was unreachable. This is the caller. */
      fireMoment('rareDrop', {
        gameKey, drop: drop.id, grade: graded && graded.grade, isRetake: false, inClass: false,
      });
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

    /* --- THE CARD LINE (T2 tester report, 2026-08-26: "card not updating past
     *     1st game"). A no-op mint still gets NO CEREMONY and never will - a
     *     beat for a hole that was not punched is the one lie this screen must
     *     not tell - but silence was being read as a broken stamp card. So the
     *     card keeps a slot for one quiet sentence, filled only when
     *     `punchcard-result` comes back having minted nothing, in the same
     *     voice as the retake chip above. A LINE, NEVER A BEAT: no sound, no
     *     stamp, no overlay, nothing that claims a hole was punched.
     *     It sits above the buttons so the fixed exits row never re-flows. --- */
    let punchNote = null;
    function setPunchNote(text) {
      const line = String(text == null ? '' : text);
      if (!line || !mine()) return false;
      if (!punchNote) {
        punchNote = el('p', 'arc-note arc-endcard-chip arc-endcard-punchnote');
        try { card.insertBefore(punchNote, actions); }
        catch (e) { card.appendChild(punchNote); }
      }
      punchNote.textContent = line;
      return true;
    }

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
      fireMoment('campus.endCardRetake', { gameKey, grade: graded && graded.grade });  // EMI SEAM
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
      /** The card's one quiet slot - see THE CARD LINE above. */
      setPunchNote,
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
      // ...and the counter's one small offer under it. Null nine nights in ten.
      noteAction: slipOfferButton(),
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

  /** Which rooms have already rung tonight (W3 P1-18). A first sitting rings;
   *  a retake of the same room does not ring twice. */
  const classBellRung = new Set();

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
    /* THE PA PACK's second belt on the same moment: retires a line caught in
     * flight (spent, not deferred - a spoken line into a class would fight the
     * class's own audio). */
    try { if (pa) pa.notify('classStart'); } catch (e) { /* noop */ }
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
    /* W3 P1-18: CLASS BEGINS. A school rings a bell when a lesson starts and
     * this one never did. BELOW the VN gate on purpose - while the first-bell
     * walk owns the beat the school stays quiet and lets it - and once per
     * room per night, so a retake does not re-ring what already rang. Lighter
     * and higher than the night's own first bell at maybeFirstBell. */
    if (cls && cls.gameKey && !classBellRung.has(cls.gameKey)) {
      classBellRung.add(cls.gameKey);
      sfx('bell', 0.3, { pitch: 1.15 });
    }
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
    /* THE ROOM'S OWN TUNE, if the table has one for this class; a class with
     * no track is played in silence, which is the honest default (ost law 3). */
    try { if (ost) ost.enter(cls.gameKey); } catch (e) { /* noop */ }
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
     * is still the fallback if it 404s.
     * SINCE 2026-08-25 the pick is WOVEN first: pickClassLoom answers the
     * params wrapper (or a saved user-loom gif url), and only a dead generator
     * falls back to the old gif pick - see the contract block above. */
    const classSpiral = pickClassLoom(seed, src.settings, loomSeed)
      || pickSpiralUrl(seed, src.settings);
    /* THIS ROOM'S CAMERA, for the class-side kit's Resume Slate (`FEED 05 - SYNC`
     * in The Deep End). The DIRECTOR owns the Annex map, so it resolves the tag
     * and the shell keeps no second copy of a table it does not own. */
    const seepFeedTag = seep ? seep.feedTag(cls.gameKey) : '';
    /* The whole pool, frozen, for a class that needs to OFFER spirals rather
     * than only wear one (Instant Recall's SPIRAL question). Same rows, same
     * order, same weights the pick walks - see spiralPoolRows.
     * PLUS the woven row (2026-08-25): when the class spiral is a generated
     * loom, its wrapper rides the pool tail as {loom:true, id, params, href,
     * weight} - deliberately with NO `url` key, so a consumer that normalises
     * rows to string urls (Instant Recall's rowsOf today) skips it cleanly
     * instead of trying to paint 'loom:...' as an image. A loom-aware consumer
     * reads `row.loom` and takes id/params/href whole. */
    const spiralPool = Object.freeze(spiralPoolRows(src.settings)
      .map((r) => Object.freeze({ url: r.url, weight: r.weight }))
      .concat((classSpiral && typeof classSpiral === 'object' && classSpiral.loom === true)
        ? [Object.freeze({
          loom: true, id: classSpiral.id, params: classSpiral.params,
          href: classSpiral.href, weight: LOOM_GEN_WEIGHT,
        })]
        : []));
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
        // ...and which of those words the school can SAY. Empty on a day the
        // whisper mute is on, so `voice:true` in a class is inert by itself.
        wordAudio: dayWordAudio,
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
      engine, layer: (dom && dom.ceremony) || null, reducedMotion,
      confetti: () => ownsSku('confetti_stamp'),
      log: say,
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
     * A game may TELL the mascot how the room feels. `tense`/`clutch` are the
     * two big ones and are still mostly wordless, `stumble` fires the small
     * 'miss' face whose pool always said "one wrong answer, one dropped tile"
     * and finally means it (its own maxPerClass:1 still rations the words), and
     * `runLost` is the mid-class K.O. All throttled HERE so no game can flood
     * her: tense latches until calm, everything shares a 15s spacing, stumbles
     * cap at 3 a class, the K.O. spends once. Opt-in per game; a class that
     * never calls it plays exactly as before.
     *
     * THE 2026-08-25 HEARTBEAT WAVE ADDED THE OTHER TWO VERBS, and reversed one
     * rule while it was there. "Mid-class speech is barred" is gone - the owner
     * wants her commenting during a class - so a pool on `tense`/`clutch` is
     * legal now and voice.js rations mid-class WORDS on its own (a 20s floor, a
     * per-class ceiling, and the danger gate below).
     *
     *   note(id, extra)  the ordinary road for class commentary. Fires the
     *                    moment `game:<id>`; moments.js answers EVERY note with
     *                    a face keyed off `extra.kind` and the voice decides
     *                    separately whether it also earns a line. The throttles
     *                    here are a FLOOD GUARD, not a ration - the voice does
     *                    the rationing, and a game must never build its own.
     *   hold(on)         THE DANGER GATE. A timing-critical window (a go/no-go,
     *                    a playback, a shuffle) where a sentence would actually
     *                    cost the player the round. While held she may not spend
     *                    WORDS on class commentary; faces still land, because a
     *                    one-second face on her own glass is the mirror working.
     *                    It auto-releases when the class ends, so a game that
     *                    throws mid-window cannot mute her for the sitting. */
    const mood = (() => {
      let tenseLatch = false, stumbles = 0, lastAt = 0, koSpent = false;
      const MOOD_SPACING_MS = 15000;
      /* THE NOTE THROTTLES, and they are deliberately NOT the 15s spacing
       * above: a note is a small true thing about the board and there are many
       * of them, where a `tense` is a whole change of weather. */
      const NOTE_SPACING_MS = 2500;    // between any two notes
      const NOTE_SAME_MS = 6000;       // between two notes of the SAME id
      const NOTE_CAP = 40;             // per class - a flood guard, nothing more
      let notes = 0, lastNoteAt = 0, held = false;
      const noteAt = Object.create(null);
      /* THE HINT CHANNEL (2026-08-30) - see `askHelp` at the bottom of this
       * object. Its ration is its own; the 15s spacing it shares. */
      let helpAsks = 0;
      const HELP_ASKS_PER_CLASS = 2;
      /* IT RETURNS NOW. Every verb above ignores the answer (a face that did
       * not land is not a class's problem), but `askHelp` has to know whether
       * the QUESTION actually reached the glass, because a game that thinks it
       * offered help when nobody was there would spend its one offer on
       * nothing. `fireMoment` answers true only when EMI took the moment. */
      const fire = (name, extra) => {
        try { return fireMoment(name, Object.assign({ gameKey: cls.gameKey, midClass: true }, extra || {})); }
        catch (e) { return false; /* a mascot may never break a class */ }
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
        /**
         * ONE THING THAT JUST HAPPENED ON THE BOARD.
         * @param {string} id     stable, dotted, the game's own namespace
         *                        ('lf.tile.repeat'). It IS the pool key
         *                        (`game:lf.tile.repeat`) and the bark data
         *                        hangs off it, so renaming one orphans a pool.
         * @param {Object=} extra `kind` picks the face (celebrate, commiserate,
         *                        tease, tension, curiosity, ambient) and the
         *                        payload tokens {n} {tile} {word} {left}
         *                        {streak} {grade} are read off it by voice.js.
         */
        note(id, extra) {
          if (typeof id !== 'string' || !id) return;
          if (notes >= NOTE_CAP) return;
          const now = Date.now();
          if (now - lastNoteAt < NOTE_SPACING_MS) return;
          if (now - (noteAt[id] || 0) < NOTE_SAME_MS) return;
          notes += 1; lastNoteAt = now; noteAt[id] = now;
          fire('game:' + id, extra);
        },
        /** The danger gate. Edge-triggered: a game may call it every frame. */
        hold(on) {
          const next = on !== false;
          if (next === held) return;
          held = next;
          fire('moodHold', { on: next });
        },
        /**
         * SHE OFFERS A HAND, AND SHE ASKS FIRST (2026-08-30).
         *
         * THE OWNER'S AMENDMENT TO TRAPS 90 AND 97, and the whole of it:
         * "im actually not oki with this law anymore, i think emi might wanna
         * speak during the games and hoestly it alredy does. Lets not overdo it
         * tho, have it speak somewhat during the games and the hints are an
         * exception they trigger if they are having troubles". So a class that
         * can SEE a player struggling may put ONE question on the glass - never
         * a hint, never a dump, a question - and the player answers it.
         *
         * "Let's not overdo it" is the ration, and it is enforced here rather
         * than in any game: two a class, the 15s mood spacing, and the ask
         * engine's own gates on top (one strip at a time, EMI has to be
         * reachable, and it leaves on its own after `STUCK_GIVE_UP_MS` because
         * a chip strip may not camp over a live board - trap 59/97's rule
         * survives the amendment intact).
         *
         * THIS IS THE ONE MOOD VERB THAT IS NOT FACE-ONLY, and it is a narrow
         * channel, not a bus: a second kind of in-class question gets its own
         * verb and its own ration rather than riding `stuck`.
         *
         * @param {Object} spec  the class's whole question, already localised -
         *   {q, face, chips:[yes,no], yes:{say,face}, no:{say,face}, onYes}.
         *   The words are the GAME's (its lexicon rows, its `{cat}`
         *   substitution) because emi/asks.js has no `t()`; `onYes` is the
         *   callback the ask engine holds and invokes if - and only if - the
         *   player says yes.
         * @returns {boolean} true when the question actually landed on the
         *   glass. False is ordinary (no EMI, she is busy, the ration is spent)
         *   and a class must treat it as "not offered", never as "declined".
         */
        askHelp(spec) {
          if (!spec || typeof spec !== 'object') return false;
          if (typeof spec.onYes !== 'function') return false;
          if (helpAsks >= HELP_ASKS_PER_CLASS) return false;
          const now = Date.now();
          if (now - lastAt < MOOD_SPACING_MS) return false;
          const landed = fire('stuck', spec) === true;
          /* THE SPACING IS SPENT ON A LANDING, NOT ON AN ATTEMPT. A question
           * that never reached the glass must not also mute the faces for the
           * next fifteen seconds; the caller's own retry floor stops the
           * flood. */
          if (landed) { helpAsks += 1; lastAt = now; }
          return landed;
        },
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
      /* IC EMI CAMEOS (2026-08-29): the mascot seam, beside the mood one and
       * under the same law - a class may borrow her, it may not drive her.
       * `visit(spec)` answers a handle or NULL (synchronously; deal your
       * ordinary bubble on the same tick). `fileTag()` answers the subject
       * code once the seep is post-reveal, else null. Both are opt-in and
       * null-safe: a rig stubs ctx without them, so `if (ctx.emi)` first. */
      emi: Object.freeze({ visit: emiVisit, fileTag: emiFileTag }),

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
      // The class's spiral POOL (frozen `{url, weight}` rows, plus at most one
      // `{loom:true, id, params, href, weight}` woven row) beside the one
      // spiral the engine wears. A class that never reads it is unaffected.
      spiralPool,
      /* THE CLASS SPIRAL ITSELF, opaque (2026-08-25): the exact value the
       * engine's spiralUrl() provider answers - a url string, or the loom
       * wrapper {loom:true, id, params, href}. For a game that paints a STATIC
       * image of it (The Deep End's pin), `classSpiral.href || classSpiral` is
       * always a paintable url. Pass it to sustain('wash', {url}) whole -
       * never stringify the wrapper. Additive; nothing that ignores it moves. */
      classSpiral,

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
    /* `onLeave` is the card's second button (orientgate Law 2's coda), and it is
     * THE PILL'S OWN ROUTINE - the same ask, the same jeopardy note, the same
     * door - because a phone that can never produce a landscape viewport (an iOS
     * system portrait lock has no in-page override; the Discord Activity iframe
     * hands us whatever shape it likes) would otherwise be held against a card
     * with no way off it. The gate reveals it only after a grace period, so a
     * phone that was merely slow to turn never sees it. */
    requireOrientation(entry.orientation, {
      reason: 'class',
      onChange: (blocking) => orientFreeze(blocking),
      onLeave: () => askLeaveClass(),
    });
    // The host only flips _classActive off this frame and ignores fields it does
    // not know, so `endless` is free to carry: a free swim opens the same
    // bracket and closes it with `class-left` from teardownClass (never with
    // `class-ended`, which is what would credit attendance and pay XP).
    /* THE LEVER RIDES THE OPENING BRACKET AND NOWHERE ELSE. It is a PROPOSAL:
     * the host clamps it against the unlocks it holds, stores the clamped value
     * as this game's pending lever, and applies the multiplier itself at
     * class-ended. The page never echoes a multiplier back and never sees one -
     * which is also why a free swim carries the lever and is paid nothing for
     * it (an endless run ends with `class-left`, and no bracket closes). */
    activeLever = leverPick();
    bridge.send(endless
      ? { type: 'class-started', gameKey: cls.gameKey, gradeTier, endless: true, lever: activeLever }
      : { type: 'class-started', gameKey: cls.gameKey, gradeTier, lever: activeLever });

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
    if (!e || typeof e !== 'object') {
      /* THE FREE SWIM KEY (Prize Counter, 1 token). It does not unlock a room
       * and it does not unlock a grade: it opens the UNGRADED door on a class
       * you are enrolled in but whose game never declared one. Enrollment is
       * the floor on purpose - the key is a way to practise a room you already
       * go to, never a way to walk into one you have not been let into. Every
       * downstream reader (the door card's second button, the room scene's
       * furniture, startFreeSwim's own re-check) rides this one function, so
       * the key lights all three surfaces at once and none of them had to hear
       * about it. */
      if (ownsSku('free_swim_key') && isEnrolled(gameKey)) {
        return { labelKey: 'free_swim', hintKey: 'free_swim_key_hint' };
      }
      return null;
    }
    return {
      labelKey: typeof e.label_key === 'string' ? e.label_key : '',
      hintKey: typeof e.hint_key === 'string' ? e.hint_key : '',
    };
  }

  /** Has this room's card ever been opened? The key's floor. */
  function isEnrolled(gameKey) {
    try {
      const card = store.punchCard(gameKey) || {};
      return typeof card.enrolledAt === 'string' && !!card.enrolledAt;
    } catch (e) { return false; }
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
      /* THE HONORS INPUT, and it is exactly one boolean. The rubric does the
       * rest (core/grades.js): honors plus a composite at or above 0.97 is the
       * only road to S+, and every cap still bites it the way it bites an S. */
      honors: activeLever === 'honors',
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
    /* S+ IS A WIN AND THE REGEX DID NOT KNOW IT. `/^[sab]$/` matched exactly one
     * letter, so the best result in the school would have fired EMI's FAIL pool
     * - the honours run gets the rage chain. `isSPlus` is the additive answer
     * and every other branch here is byte-for-byte what it was. */
    const endMoment = isSPlus(graded.grade) || /^[sab]$/i.test(String(graded.grade))
      || graded.grade === 'pass' ? 'win' : 'fail';
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
    // THE PAGE PAUSE IS A CAMEO'S END TOO: the pause card lands over the
    // board she is standing on, and there is no pat behind a scrim.
    if (on) cancelLiveVisit();
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
        /* W3 P0-27: THE FREEZE HAS A SOUND NOW. Every road into a pause - the
         * card, the settings screen, applySuspend(true) - lands here, and until
         * now the loudest thing in the school went silent with no cue at all.
         * A low thud and a short duck: the room stops. Inside the card-build
         * guard, so a second pause on an already-paused class is silent, which
         * is what keeps this the strict inverse of the lift below. */
        sfx('thud', 0.3, { pitch: 0.85, duck: { target: 'spotlight', mult: 0.2, ms: 400 } });
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
          /* ...and the ONE small button under it, when the streak is cold and
           * the bag is empty. Under the line, below Resume/Options/Leave, so
           * it can never be mistaken for the answer to "paused". */
          const offer = slipOfferButton();
          if (offer) overlay.appendChild(offer);
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
        // W3 P0-27: the inverse, on the removal line itself - the card goes and
        // the room comes back up. No duck: this one is the un-ducking.
        sfx('lift', 0.28);
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

  /* WEB TAB SUSPEND (perf/arcademy-mobile-web). On the desktop host the bridge
   * drives applySuspend (onSuspend below); in a browser nothing ever did, so a
   * backgrounded tab kept every loop warm. Wired ONLY when the host is not the
   * desktop (init.platform.host), and with its own reason string so it can
   * never be confused with - or lift - a suspend the host initiated:
   * - hidden: refuse while ANY suspend already holds the level (trap 28: it is
   *   a LEVEL, and whoever set it owns it), and refuse while an Orientation
   *   Day beat is on stage - applySuspend(true) SKIPS a running beat and banks
   *   it as seen, which must not be spent on a mere tab switch (background
   *   throttling stills the beat anyway; the same shape as orientFreeze's
   *   both-direction guard).
   * - visible: lift ONLY a 'tabhidden' suspend this listener applied itself.
   *   A mid-class return lands on the pause card with its Resume button -
   *   applySuspend(false) deliberately leaves the class paused. */
  const platformHost = String((src.platform && src.platform.host) || 'desktop');
  function onTabVisibility() {
    try {
      if (document.visibilityState === 'hidden') {
        if (suspendedGlobally) return;
        try { if (orientation && orientation.active()) return; } catch (e) { /* noop */ }
        applySuspend(true, 'tabhidden');
      } else if (suspendedGlobally && suspendReason === 'tabhidden') {
        applySuspend(false, 'tabhidden');
      }
    } catch (e) { say('tab suspend threw: ' + ((e && e.message) || e)); }
  }
  if (platformHost !== 'desktop'
      && typeof document !== 'undefined' && document.addEventListener) {
    document.addEventListener('visibilitychange', onTabVisibility);
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

  /* ======================= ctx.emi - THE CAMEO SEAM =======================
   * IC EMI CAMEOS (2026-08-29). A game may not call the widget: it holds
   * `ctx.mood` and, from this wave, `ctx.emi` - the same shape and the same
   * discipline. `ctx.mood` may TELL the mascot how the room feels; `ctx.emi`
   * may BORROW her for one bubble. Neither is a door into emi/: a class never
   * imports the mascot and never sees a widget handle.
   *
   * THE SHELL OWNS THE RATION, NOT THE GAME. Every throttle a cameo has lives
   * here, exactly as the mood throttles do, so ten classes cannot each invent
   * their own and one broken class cannot flood her:
   *   - ONE visit in flight, page-wide. A second call answers null.
   *   - VISIT_FLOOR_MS since the last visit ENDED (not started, and not per
   *     class: two classes back to back must not stack).
   *   - never while a takeover or an ask owns the glass, and never under a
   *     global suspend. The widget re-checks all three of those for itself;
   *     the copy here is what makes the refusal SYNCHRONOUS and cheap.
   * A refusal is `null`, on the same tick, and it is the normal answer: the
   * game deals its ordinary bubble and rolls nothing again for that slot.
   *
   * AND THE SHELL OWNS THE CANCEL. A visit is torn down from four funnels -
   * `clearScreen()`, `teardownClass()`, `applySuspend(true)` and
   * `pauseClass(true)` - because a mascot standing in a bubble that has just
   * been removed is the one way this feature could strand her on a dead
   * screen. The widget cancels on its OWN edges as well (hide,
   * setEnabled(false), a resize, a destroy); these four are the shell's half.
   * ==================================================================== */

  /** At most one cameo anywhere in the page, and the floor between two. */
  const VISIT_FLOOR_MS = 8000;
  let liveVisit = null;
  let lastVisitEndAt = 0;

  /** Send her home from any of the shell's four funnels. Idempotent. */
  function cancelLiveVisit() {
    const v = liveVisit;
    if (!v) return;
    liveVisit = null;                 // FIRST: `cancel()` calls straight back in
    try { v.cancel(); } catch (e) { /* a mascot may never break a teardown */ }
  }

  /**
   * ctx.emi.visit(spec) -> handle | null. See emi/widget.js, THE CAMEO VISIT,
   * for the spec and the handle; this adds the floor and nothing else.
   */
  function emiVisit(spec) {
    const o = spec || {};
    if (liveVisit) return null;
    if (suspendedGlobally) return null;
    if (Date.now() - lastVisitEndAt < VISIT_FLOOR_MS) return null;
    let mascot = null;
    try { mascot = getEmi(); } catch (e) { mascot = null; }
    if (!mascot || typeof mascot.visit !== 'function') return null;
    /* A QUESTION OUTRANKS A CAMEO (trap 104): she is waiting for an answer and
     * a class may not walk her off mid-sentence. */
    try { if (mascot.asks && mascot.asks.active && mascot.asks.active()) return null; }
    catch (e) { /* no ask engine is not a live ask */ }
    /* ...and so does a channel that is up: the second canvas owns her glass. */
    try {
      const deck = mascot.channels;
      if (deck && typeof deck.live === 'function' && deck.live()) return null;
    } catch (e) { /* a deck that cannot be asked is not a deck that is up */ }

    let handle = null;
    let settled = false;
    /* ONE EXIT on this side too: the floor is stamped from here and nowhere
     * else, so every road out of a visit - pat, timeout, cancel - pays it. */
    const finish = (reason) => {
      if (settled) return;
      settled = true;
      if (liveVisit === handle) liveVisit = null;
      lastVisitEndAt = Date.now();
      if (typeof o.onDone === 'function') {
        try { o.onDone(reason); } catch (e) { /* a class may never break her */ }
      }
    };
    let inner = null;
    try { inner = mascot.visit(Object.assign({}, o, { onDone: finish })); }
    catch (e) { say('emi visit threw: ' + ((e && e.message) || e)); inner = null; }
    if (!inner) return null;
    handle = Object.freeze({
      pat(src) { try { return !!inner.pat(src); } catch (e) { return false; } },
      end() { try { return !!inner.end(); } catch (e) { return false; } },
      cancel() { try { return !!inner.cancel(); } catch (e) { return false; } },
    });
    liveVisit = handle;
    return handle;
  }

  /**
   * ctx.emi.fileTag() -> the Other Stamp's subject code, or null.
   *
   * THE FOLDER CURDLES AFTER THE REVEAL (owner ruling). Before it the dossier
   * tab reads "field notes" and the stamp says "for you"; after it the tab
   * carries the code the camera wall already writes on the player and the
   * stamp goes under a redaction bar. The SHELL resolves it, because the
   * director is the shell's - a game never learns that shell/seep.js exists,
   * and a page with no director reads the same honest null.
   */
  function emiFileTag() {
    try {
      if (!seep || typeof seep.postReveal !== 'function') return null;
      if (typeof seep.subjectCode !== 'function') return null;
      if (!seep.postReveal()) return null;
      const code = seep.subjectCode();
      return (typeof code === 'string' && code) ? code : null;
    } catch (e) { return null; }
  }

  /**
   * The host says everything must stop NOW (mandatory video, audio-only session
   * starting mid-class, panic). Freeze the class, drop every effect, and show the
   * class_suspended treatment. Attendance for the day is preserved either way -
   * the streak has already been rolled or will be when a class actually ends.
   */
  function applySuspend(on, reason) {
    suspendedGlobally = !!on;
    suspendReason = on ? (reason || '') : null;
    // NO CAMEO UNDER A MANDATORY VIDEO. One edge only: a lift re-deals
    // nothing, because a visit is not a plan slot (the game re-rolls).
    if (on) cancelLiveVisit();
    fireMoment(on ? 'suspend' : 'resume', { reason });   // EMI SEAM
    // NO GHOSTS UNDER A MANDATORY VIDEO. The layer rides the same one funnel the
    // pause card does, and it is a LEVEL (trap 28), so both edges are written.
    if (ghosts) { try { ghosts.setPaused(!!on); } catch (e) { /* noop */ } }
    /* AND THE SHUTTER COMES DOWN WHILE YOU ARE STANDING THERE. A LEVEL like
     * every other line in this function (trap 28), so both edges are written:
     * the counter shuts under a mandatory video and opens again after it. The
     * room folds its own tray panel away on the way down. */
    if (prizeBooth) { try { prizeBooth.setClosed(counterClosed()); } catch (e) { /* noop */ } }
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
          : reason === 'panic' ? 'Stopped. Press the panic key again to leave.'
            /* 'tabhidden' is the web wiring's own reason - the desktop host
             * never sends it, so its three lines above are byte-identical. */
            : reason === 'tabhidden' ? 'Paused while you were away.'
              : 'Paused for a video.',
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
    /* THE PA PACK's after-class line. THIS funnel and not `class-ended`:
     * every leave path ends here, finished or abandoned, so an Esc out of a
     * class cannot buy a second announcement. It sits after the `active`
     * guard and before `active` is dropped below. */
    try { if (pa) pa.notify('classEnded'); } catch (e) { /* noop */ }
    // ...and a cameo dies with the class that borrowed her, from whichever
    // road out this is (finished, abandoned, Esc). See ctx.emi above.
    cancelLiveVisit();
    //
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

    /** {type:'share-image-result'} - the host either put the PNG on the
     *  Windows clipboard or it did not. Resolves the one pending request;
     *  an unsolicited frame is dropped on the floor. */
    onShareImageResult(m) {
      const w = shareImageWait;
      if (!w) return;
      shareImageWait = null;
      try { clearTimeout(w.timer); } catch (e) { /* noop */ }
      w.resolve(!!(m && m.ok));
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
      /* THE ACCOUNT CHIP rides the same frame (a late fetch, a name that
       * landed after init). Additive: a frame without `account` changes
       * nothing, and a desktop host never sends one. */
      const acct = readAccount(m && m.account);
      if (acct) {
        const first = !account;
        account = acct;
        // A chip that never existed (init shipped without `account`) is minted
        // now; one that did repaints in place.
        // Only a bar that is UP is re-rendered here: a hidden one (campus, class)
        // is the next renderTopbar's job, and un-hiding it mid-class would be a
        // regression of its own.
        try {
          if (topAccountChip) topAccountChip.setAccount(acct);
          else if (first && dom && dom.topbar && !dom.topbar.hidden) renderTopbar();
        } catch (e) { /* noop */ }
        try { if (campus && campus.setAccount) campus.setAccount(acct); } catch (e) { /* noop */ }
      }
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

    /** {type:'payout-result'} - the ONLY source of an XP number on this page,
     *  and since the economy wave the only source of a TICKET number too. */
    onPayout(m) {
      if (!m || !m.gameKey) return;
      const r = results[m.gameKey];
      if (r) { r.xp = m.xp; r.levelUp = !!m.levelUp; }
      // The same frame carries the host's authoritative attendance figures.
      try { store.applyPayout(m); } catch (e) { say('applyPayout: ' + ((e && e.message) || e)); }
      /* THE PAYDAY. Tickets and the token are minted in C# and simply reported
       * here; the report card draws the beat. Stashing it on the result row is
       * what lets showReport() repaint the same card without re-firing the
       * ceremony (the card's own `arrived` gate does the rest). */
      noteWalletEcho(m);
      /* AND THE CHIP MOVES WITH IT. A payout normally lands while the report is
       * up, but an end-run that dropped the player straight back on the board
       * would otherwise leave the purse reading the number it had before the
       * class - the same silent repaint `wallet-result` takes. */
      if (screen === 'board' && campus) {
        try { campus.update(buildCampusState(), campusStats()); } catch (e) { /* noop */ }
      }
      if (r) {
        const tk = Math.max(0, Math.round(Number(m.tickets) || 0));
        if (tk > 0 || m.tokenMinted === true) {
          r.payout = {
            tickets: tk,
            base: Math.max(0, Math.round(Number(m.ticketBase) || 0)),
            mult: Number(m.ticketMult) || 1,
            token: m.tokenMinted === true,
            balance: walletBalance(),
          };
        }
      }
      /* THE TARDY SLIP. The host consumed one (or two) inside the attendance
       * path, which is the one purchase a player never sees happen - so it is
       * the one that has to be said out loud. */
      if (m.lateSlipUsed === true) {
        try { shout(t('late_slip_used', 'A tardy slip was handed in for you. Your streak never noticed.')); }
        catch (e) { /* a toast may never hold a door */ }
      }
      if (m.levelUp) shout('Level up');
      renderTopbar();
      if (screen === 'report') showReport();
    },

    /** {type:'wallet-result'} - the answer to a `prize-buy`, and the ONLY thing
     *  on this page allowed to move a balance, a badge or a lever unlock. */
    onWalletResult(m) { onWalletResult(m); },

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
      // class the host declined to stamp: nothing was punched, so no beat is
      // played. A card beat for a hole that did not happen is the one lie this
      // screen must never tell.
      //
      // IT DOES GET A LINE, THOUGH (T2 tester report, 2026-08-26: "card not
      // updating past 1st game"). No ceremony read to the tester as no card, so
      // the end card says WHY in one quiet sentence - a full card gets the
      // unlock line it earned, everything else the come-back-tomorrow one. The
      // frame carries the post-mint card, so the shell is reading the host's
      // own truth here and still counting nothing of its own.
      if (!m.minted) {
        if (endCard && endCard.gameKey === m.gameKey && endCard.setPunchNote) {
          // The frame names its own cap; `store.holes` is the parachute.
          const cap = Math.round(Number(m.holes) || Number(store.holes) || 10);
          const full = !!(norm && (norm.complete || Number(norm.punches) >= cap));
          try {
            endCard.setPunchNote(full
              /* A FULL CARD GETS THE DISCORD SENTENCE TOO. The ceremony is the
               * loud half of this and it does not run on a no-op mint, so the
               * quiet note is the only place a player who filled the card on an
               * earlier night is told the room travels. Two sentences in one
               * line - setPunchNote paints ONE <p> - and the second is dropped
               * whole when the class has no command (a retired key). */
              ? joinNote(t('punchcard_unlocked_line',
                'This room is now open even when the course is not in session.'),
                discordLine(m.gameKey))
              : t('punchcard_next_hole', 'Come back tomorrow for the next stamp.'));
          } catch (e) { say('punch note failed: ' + ((e && e.message) || e)); }
        }
        return;
      }
      if (!norm) return;
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
      /* ...and anything said UNDER the splash is said again now that there is
       * something to say it on (see shoutOnScreen). Before the continuation, so
       * a parked refusal is already up as the campus finishes arriving. */
      flushPendingShout();
      try { if (vn) { vn.splashDone(cleared); return; } }
      catch (e) { say('first bell cold open skipped (' + ((e && e.message) || e) + ')'); }
      cleared();
    },

    /** {type:'suspend'} */
    onSuspend(m) { applySuspend(!!(m && m.on), m && m.reason); },

    /** {type:'meta'} is consumed by the store; this just repaints the chrome. */
    /* A fresh meta snapshot IS the wallet now, so the frame-by-frame echo laid
     * over it has done its job and is dropped - two truths about one purse is
     * one truth too many. */
    onMeta() {
      walletEcho = null;
      renderTopbar();
      /* THE CAMPUS LOOK rides the snapshot too: `campusTheme` is a meta key, so
       * a host that pushed a different one (another window, a fresh sync) has
       * just moved the pick under us - and the wallet that decides whether it
       * is legal moved in the same frame. Idempotent; standard costs nothing. */
      try { applyTheme(); }
      catch (e) { say('campus theme meta re-apply threw: ' + ((e && e.message) || e)); }
      if (screen === 'board') showBoard({ silent: true });
      if (prizeRoom && typeof prizeRoom.refresh === 'function') {
        try { prizeRoom.refresh(); } catch (e) { /* noop */ }
      }
    },

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
      /* THE PURCHASE REVEAL sits above even the annex beat: it is a body-level
       * overlay at z 62 (over the toast), and the player opened it zero presses
       * ago by buying the thing. Nothing beneath it can hold a modal that is
       * more recent, so it takes Esc first. revealEscape() answers false when
       * no reveal is up, which is every other moment in the page, and then the
       * ladder below runs byte-for-byte as it always did (trap 48). */
      try { if (revealEscape()) return true; } catch (e) { /* noop */ }
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
      /* THE TROPHY CASE is the fourth of that set and takes the same rung: a
       * campus-chrome overlay at z 38, opened one press ago, and it binds no
       * key of its own (trap 29 - the ladder owns Escape). */
      {
        const up = currentCapsule();
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
      // THE BOOTH FOLDS INWARD-OUT, the office's shape one alley over: the
      // ARRIVAL first if the corridor is still up (one press means "get on with
      // it"), then the panel that is open - the shelf or the ticket tray - and
      // then FALSE at the window, so the rung under it walks out to the quad.
      // The shelf's own rung used to be a screen of its own up here; it is the
      // booth's first rung now, which is why there is nothing left above this
      // line. A buy still in the air is not a rung either: the frame that
      // answers it lands on a counter that has gone, which is exactly what
      // onWalletResult is written to survive (prizeRoom is null by then and
      // only the lever re-clamps). No key is bound down there; this asks.
      if (screen === 'prizebooth' && prizeBooth) {
        try { if (prizeBooth.escapeStep()) return true; } catch (e) { /* noop */ }
      }
      if (screen === 'prizebooth') { showBoard(); return true; }
      /* THE LOCKER FOLDS INWARD-OUT, which by now is simply what a room does
       * here: the wardrobe panel first (it is the thing the player opened one
       * press ago, and on arrival it opened itself), then FALSE at the plate so
       * the rung under this one walks out to the quad. The panel can also be
       * closed by its own scrim, and locker.js asks the DOM rather than a flag
       * so a scrim press cannot make this rung swallow the next Esc. */
      if (screen === 'locker' && lockerRoom) {
        try { if (lockerRoom.escapeStep()) return true; } catch (e) { /* noop */ }
      }
      if (screen === 'locker') { showBoard(); return true; }
      /* THE BACK ROOM FOLDS INWARD-OUT like everything else on this floor: the
       * room's own rungs first (a table it has dealt, a cage it has opened),
       * then FALSE, and the rung below walks out to the quad. A wing that is
       * not on this host has no rung at all - the door never opened, so `screen`
       * never moved and this line is never reached. */
      if (screen === 'backroom' && backRoomPage) {
        try {
          if (typeof backRoomPage.escapeStep === 'function' && backRoomPage.escapeStep()) return true;
        } catch (e) { /* noop */ }
      }
      if (screen === 'backroom') { showBoard(); return true; }
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

    /** THE CAMPUS LOOK, for the play-test rig and the suites. Read-only: what
     *  is picked, what is on offer, which tokens the theme currently holds and
     *  what the weather layer is doing. Nothing shipped reads this. */
    themeState() {
      return {
        pick: themePick(),
        offered: ownedThemes(ownsSku).map((x) => x.id),
        tokens: themeTokensSet.slice(),
        fx: themeFx ? themeFx.stats() : null,
      };
    },
    /** The same write the options row makes, for a rig. Clamped identically. */
    setCampusTheme(id) { return setThemePick(id); },

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
      /* ASKS: the exit-aim listener is the shell's, so the shell takes it.
       * The web tab-suspend listener is the shell's too (a no-op remove on the
       * desktop host, where it was never added). */
      if (typeof document !== 'undefined' && document.removeEventListener) {
        try { document.removeEventListener('pointermove', onExitAim); } catch (e) { /* noop */ }
        try { document.removeEventListener('visibilitychange', onTabVisibility); } catch (e) { /* noop */ }
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
      /* THE WEATHER LAYER hangs its canvas on <body>, outside anything
       * `dom.screen.textContent = ''` can reach, and holds a rAF and two
       * document listeners. destroy() is the only thing that takes all three
       * off - the records room's reason, one layer up. */
      if (themeFx) { try { themeFx.destroy(); } catch (e) { /* noop */ } themeFx = null; }
      if (pa) { try { pa.destroy(); } catch (e) { /* noop */ } pa = null; }
      if (paCaption) { try { paCaption.destroy(); } catch (e) { /* noop */ } paCaption = null; }
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
    /* `strings` is the ONE lexicon row EMI renders (the ask strip's Send
     * button). Resolved HERE because this side of the page has `t` and she has
     * never imported the lexicon - the same seam orientation.js uses for her
     * three opening lines. */
    if (emiLayer) {
      emi = mountEmi({
        layer: emiLayer, store, toast: shout, log: say, assets, settings: src.settings,
        strings: {
          askSend: t('emi_ask_send', 'send'),
          /* COUNTER STOCK: the desk toy's three lines. Resolved HERE for the
           * same reason askSend is: she has never imported the lexicon. */
          toy: [
            t('emi_toy_1', "Don't wind it too far. It gets ideas."),
            t('emi_toy_2', "It's not a toy, it's office equipment. Okay, it's a toy."),
            t('emi_toy_3', 'She spins when I do good work. We have a system.'),
          ],
        },
        /* COUNTER STOCK: what the player owns. Row getters, re-read on every
         * paint, so a prize bought mid-sitting lights without a reload. EMI
         * keeps her half (the prop, the pose, the frame map) - the shell only
         * answers "does the player own it", which is the one thing she cannot
         * ask, because she has never seen a wallet. */
        prizes: {
          deskToy: () => ownsSku('emi_desk_toy'),
          varsity: () => ownsSku('emi_varsity'),
          /* THE LOCKER WAVE. Three more getters, and the split is the one
           * above: she owns the wardrobe (which sheets exist, which pose she
           * is in, which prop bobs on the tube) and the shell owns the two
           * things she cannot see - what the player OWNS and what the player
           * PICKED. `setOutfit` is the one road in, and it asks `outfitOwned`
           * before it dresses her, so a lapsed entitlement takes the jacket
           * off at the next paint without anybody writing a lapse handler.
           * A host with no economy hands none of this down and she wears
           * exactly what she wore before the Locker existed. */
          outfitOwned: (name) => ownsSku('emi_' + String(name || '')),
          outfit: () => lockerOutfit(),
          toyPin: () => lockerToyPin(),
        },
      });
    }
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

  /* ================= THE OPENING, AND THERE ARE TWO OF THEM ==============
   * THE ORDINARY BOOT builds the campus and then asks the activity hook whether
   * a room was requested: the campus has mounted (showBoard) and the punch cards
   * have been known since `init.meta` reached the store at construction, which
   * are the launch's two preconditions. `launchGraded` sorts the rest out - a
   * room tonight's board already deals starts as THAT class, a room off the
   * board starts as a free swim's graded twin - so the hook only decides
   * WHETHER.
   *
   * THE DIRECT LAUNCH turns that round. A `/deepend` boot is not a visit to a
   * school, it is a visit to ONE ROOM, so the campus is not built at all:
   * boot.js's splash (lettered with that room's name) is the only thing on
   * screen, the cards are waited for when the host has not sent them yet, and
   * the class starts straight into it. The quad's arrival rituals are not lost,
   * they are DEFERRED - the greet, the postman, the streak reading and
   * Orientation Day all belong to ARRIVING on the campus, and the player does
   * that for real when the class ends and `showBoard()` runs its first time.
   *
   * THE REFUSAL NEVER STRANDS ANYBODY: a card that is not full (or a class the
   * suspend gate turned away) falls through to exactly the ordinary opening one
   * beat later, with the toast the quad door would have given.
   * `maybeLaunchRequested` is spent exactly once on both roads.
   * ==================================================================== */
  if (directLaunch) {
    await awaitDirectLaunchCards();
    if (!destroyed) {
      if (isUnlocked(launchRequest)) {
        maybeLaunchRequested();
        // startClass refuses under a global suspend: that boot is a campus after all.
        if (screen !== 'class') showBoard();
      } else {
        // Campus FIRST, so the refusal has a screen to land on.
        showBoard();
        maybeLaunchRequested();
      }
    }
  } else {
    showBoard();
    maybeLaunchRequested();
  }
  return api;
}

export default createShell;
