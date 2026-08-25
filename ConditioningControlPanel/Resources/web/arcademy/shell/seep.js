/* ============================================================================
 * shell/seep.js - THE SEEP. One director, not eleven timers.
 *
 * Something under the Records Office watches the school through nine green
 * screens, and long before the last card seals the feed starts bleeding
 * through. This module is the ONE thing that decides whether a tell may run.
 * Nothing fires on its own clock, so two tells can never collide and the whole
 * haunting tunes from one table.
 *
 * ---------------------------------------------------------------------------
 * THE PUBLIC SURFACE  (this is the contract the class-side kit is built on)
 * ---------------------------------------------------------------------------
 *   createSeep({ store, games, seams, gate, emi, reducedMotion, lite,
 *                utcDay, search, log }) -> director | null
 *
 *   director.tier()               -> 0..4, from SEALED PUNCH CARDS (the reveal's
 *                                    own masteredCount recipe). Memo-free: it is
 *                                    re-derived off the store every call.
 *   director.sealed()             -> the raw sealed-card count behind tier().
 *   director.postReveal()         -> true once `annexRevealSeen` is set. After
 *                                    the reveal ONLY slip / looker / hum remain,
 *                                    at T2 rates; every other tell retires.
 *   director.active(name)         -> is this tell in tonight's live set? (tier +
 *                                    post-reveal + reducedMotion + lite)
 *   director.eligible(opts)       -> THE CHECKLIST, and it is a checklist, not a
 *                                    vibe: not lite, past the first two minutes,
 *                                    past the 90s global cooldown, nothing in
 *                                    flight, and (campus side) campus chrome up
 *                                    with no class, no ceremony, no annex reveal;
 *                                    then, both sides: EMI not mid-line and the
 *                                    pointer up.  opts = {side:'campus'|'class'|
 *                                    'boot', ignoreCooldown?:bool}
 *   director.maybe(name, opts)    -> seeded roll + eligibility. Returns a CLAIM
 *                                    ({name, release()}) or null. The caller MUST
 *                                    call release() on the tell's last frame.
 *   director.claim(name, opts)    -> the same without the roll (the caller has
 *                                    already decided; governance still applies).
 *   director.beat(name, payload)  -> THE CLASS-SIDE DOOR. A game/engine names a
 *                                    DEAD MOMENT it is standing in; the director
 *                                    answers a claim or null. The game never
 *                                    guesses and never rolls.
 *                                      'door_card'  the class card, pre-input
 *                                                   (asks the CAMPUS list -
 *                                                    see BEAT_SIDE below)
 *                                      'round_gap'  between rounds / after a clear
 *                                      'resume'     the suspend lift, pre-rearm
 *                                      'resurface'  The Deep End's exhale
 *                                      'stream'     Instant Recall's watch-only wall
 *                                    A claim carries `{name, tell, ms, release}`:
 *                                    `tell` is the tell id, `ms` the duration the
 *                                    director wants it held. Unknown beats and
 *                                    beats whose tell is not active answer null.
 *   director.note(kind, payload)  -> the director's EAR. The shell tells it about
 *                                    'classEnd' (weights the Cold Draft toward
 *                                    the minute after a class), 'classStart'
 *                                    (re-arms the once-per-class tells) and
 *                                    'campusUnmount' (re-arms the once-per-visit
 *                                    ones). Unknown kinds are ignored.
 *   director.feedTag(gameKey)     -> `FEED 05`, this room's camera per the Annex
 *                                    map. The class-side kit prints it on the
 *                                    Resume Slate; the shell asks for it rather
 *                                    than keeping a second copy of CAM_NO.
 *   director.subjectCode()        -> the Other Stamp's `SUBJ <code>/02` code. A
 *                                    SHORT LOCAL HASH of the identity the page
 *                                    already holds. Never a server call.
 *   director.debug()              -> {forced, tier, plan, fired, cooldownMs}
 *   director.destroy()
 *
 * WHAT A CLASS-SIDE CALLER MAY ASSUME
 *   - `beat()` is cheap, synchronous and safe to call every round. It answers
 *     null the overwhelming majority of the time and that is the feature.
 *   - a claim is EXCLUSIVE: while one is out, every other door answers null.
 *     Call `release()` from a finally-shaped path; a claim that is never
 *     released closes the seep for the session, which is a bug, not a mood.
 *   - the director NEVER draws anything class-side. It answers "yes, and hold it
 *     for N ms"; the kit owns the pixels, on the engine's own fx layer
 *     (pointer-events:none by construction).
 *   - a class-side tell may only be asked for from a DEAD MOMENT. That law is the
 *     caller's to keep - the director cannot see whether input is armed.
 *
 * ---------------------------------------------------------------------------
 * LAWS THIS FILE LIVES UNDER
 * ---------------------------------------------------------------------------
 *  - SEEDED, NEVER RANDOM. Every roll comes off `makeRng`/`makeTaggedRoll` over
 *    `seep|<utcDay>` (core/rng.js). A reload replays the same shift, so nobody
 *    farms a spooky screenshot by refreshing. There is not one Math.random() in
 *    this file.
 *  - NO STORE WRITES, NO TELEMETRY, NO TOASTS, NO NEW META KEYS. A haunting that
 *    keeps records is a feature with a settings page. The store handle here is
 *    read-only by discipline: `punchCard()` and `get()` and nothing else.
 *  - IT NEVER TOUCHES THE ESC LADDER AND NEVER COSTS AN INPUT. The only listeners
 *    are three PASSIVE pointer reads on `document` (trap 80's shape: no
 *    preventDefault, no stopPropagation, no key inspection, removed on destroy).
 *    Every layer it mints is `pointer-events:none`.
 *  - SAFETY RUNGS WIN. `reducedMotion` retires every ANIMATED tell (the Hum, which
 *    is audio, survives). The lite rung (`html.ae-lite`, or `lite:true`) retires
 *    the lot. Photosensitivity: one flicker per tell, sub-200ms, no strobes.
 *  - TRANSFORM, NEVER background-position (trap 36). The scanline recipes here
 *    are annex/cams.css's, verbatim in spirit: one oversized sheet translated by
 *    exactly one tile period. No blend modes, no filters over a live decode.
 *  - THE HANDLES ARE GETTERS, NOT HANDLES (trap 73). A campus captured at plan
 *    time is a campus that has gone; `seams` is a bag of functions.
 *  - node DOM double safe (trap 60): no `Image`, maybe no `CustomEvent`, no
 *    `matchMedia`, no layout. This module must import clean and no-op headless.
 * ==========================================================================*/

import { makeRng, makeTaggedRoll, hash01 } from '../core/rng.js';
import { ROOMS } from './campus.js';

/* ----------------------------------------------------------------------------
 * THE TABLE. Every dial the haunting has is here, and nowhere else.
 * -------------------------------------------------------------------------- */

/** 90 seconds minimum between ANY two tells. One global cooldown. */
export const COOLDOWN_MS = 90000;
/** The first two minutes of every session are always clean. */
export const QUIET_BOOT_MS = 120000;
/** Mean seconds between roaming tells, by tier. T0 has no roaming schedule at
 *  all - the Feed Slate is a boot roll, not a plan entry. */
export const TIER_RATE_MS = Object.freeze([0, 1500000, 900000, 480000, 210000]);
/** How far ahead the night is planned. A sitting longer than this simply runs
 *  out of slots, which is the correct answer for a five-hour session. */
export const PLAN_HORIZON_MS = 3 * 3600 * 1000;
/** After a class ends the thing downstairs exhales: the Cold Draft is offered
 *  once inside this window, on its own roll, outside the plan. */
export const DRAFT_WINDOW_MS = 60000;

/** A full card is ten holes; enrolling is worth three (PUNCHCARD.md's PACE).
 *  Mirrored here rather than imported so `sealedFromMeta` stays free of the
 *  card module - shell/punchcard.js is a DOM file and boot.js reads this while
 *  the splash is up. The suite asserts the numbers agree. */
const PUNCH_HOLES = 10;
const ENROLL_HOLES = 3;

/** THE ROOM KEYS, verbatim from campus.js ROOMS. These are DIEGETIC CODE KEYS on
 *  purpose: the File Name flashes what the FILING SYSTEM calls the room, so they
 *  are hardcoded and deliberately NOT lexicon-skinnable (owner ruling 0824). */
export const ROOM_KEYS = Object.freeze(Object.keys(ROOMS));

/** THE RETIRED TENANT. Misdirection wore the 201 plate before SORT substituted
 *  for it (campus.js's ROOMS header). The Sorting Room's plate very rarely
 *  flashes the old key instead of its own - pure archaeology, and the rarest
 *  entry in the whole File Name table by construction: it can only be drawn on a
 *  roll that already drew `sort`, and then only on MISDIRECTION_P of those. */
export const RETIRED_KEY = 'misdirection';
export const MISDIRECTION_P = 0.06;

/** Room key -> the cam that watches it. MIRRORS `annex/cams.js` CAM_DEFS and is
 *  duplicated here on purpose (the GAME_META parachute law): importing the whole
 *  camera wall to read a nine-row table would evaluate the Annex at shell boot.
 *  The suite asserts the two tables agree. A room with no cam gets no tag. */
export const CAM_NO = Object.freeze({
  daily_trigger: '01', deja_vu: '02', impulse_control: '03', lost_and_found: '04',
  the_deep_end: '05', sort: '06', echo: '07', instant_recall: '08',
});
/** FEED 09 is the Main Gate - the camera you walk in through. */
export const GATE_CAM = '09';

/**
 * THE FEED TAG a class-side tell wears: `FEED 05` for The Deep End, per the map
 * above. PURE, and it lives here rather than in the shell because the map does:
 * a caller that had to import the nine-row table to build a five-character
 * string would be a caller keeping a second copy of it.
 *
 * A room with no camera gets the Main Gate rather than a blank, which is the
 * honest answer - every feed in that building comes through the Annex, and 09 is
 * the one you walked in past.
 * @param {string} gameKey
 * @returns {string}
 */
export function feedTagFor(gameKey) {
  const key = String(gameKey || '');
  return 'FEED ' + (Object.prototype.hasOwnProperty.call(CAM_NO, key) ? CAM_NO[key] : GATE_CAM);
}

/**
 * THE LADDER. `from` is the first tier the tell exists at, `side` is which door
 * it comes through, `animated` marks the ones reduced motion retires, and
 * `postReveal` is the survivor list (owner ruling 0824: understood is not
 * unhaunted).
 *
 * `once` is a lifetime scope: 'session' | 'visit' | 'class' | null.
 */
export const TELLS = Object.freeze({
  /* ---- campus, roaming (the plan deals these) ---- */
  slip:        Object.freeze({ from: 1, side: 'campus', animated: true,  postReveal: true,  once: null,      weight: 30, ms: 140 }),
  hum:         Object.freeze({ from: 1, side: 'campus', animated: false, postReveal: true,  once: null,      weight: 22, ms: 700 }),
  file_name:   Object.freeze({ from: 2, side: 'campus', animated: true,  postReveal: false, once: null,      weight: 18, ms: 90 }),
  cold_draft:  Object.freeze({ from: 2, side: 'campus', animated: true,  postReveal: false, once: null,      weight: 16, ms: 2400 }),
  looker:      Object.freeze({ from: 3, side: 'campus', animated: true,  postReveal: true,  once: 'session', weight: 10, ms: 900 }),
  slow_second: Object.freeze({ from: 3, side: 'campus', animated: true,  postReveal: false, once: 'visit',   weight: 10, ms: 3000 }),
  bad_frame:   Object.freeze({ from: 3, side: 'campus', animated: true,  postReveal: false, once: 'session', weight: 6,  ms: 120 }),
  /* ---- campus, event-ridden (a surface asks at its own moment) ---- */
  misprint:    Object.freeze({ from: 2, side: 'campus', animated: true,  postReveal: false, once: null, weight: 0, ms: 400, p: 1 / 12 }),
  /* `side` here is where it BELONGS (the hub half of the school), not the list
   * it clears: the report card is its own screen, so `stampGhost()` asks the
   * common checklist - see the note there, and BEAT_SIDE for the same shape on
   * the class side. */
  other_stamp: Object.freeze({ from: 3, side: 'campus', animated: true,  postReveal: false, once: null, weight: 0, ms: 80,  p: 1 / 10 }),
  /* ---- the boot loader. The ONLY tell that exists at T0. ---- */
  feed_slate:  Object.freeze({ from: 0, side: 'boot',   animated: true,  postReveal: false, once: 'session', weight: 0, ms: 80 }),
  /* ---- furniture. Not scheduled, not flashed: a paper that hangs there being
   *      innocent. The corkboard pins it on a `sealedAtLeast` gate; the director
   *      only answers whether it is in tonight's set. ---- */
  wet_ink:     Object.freeze({ from: 1, side: 'static', animated: false, postReveal: true,  once: null, weight: 0, ms: 0 }),
  /* ---- THE CLASS-SIDE KIT (tells 12-16). The whole kit unlocks at T3, when the
   *      campus is already talking: class is where attention is dearest, so the
   *      school whispers there last. A later agent owns the pixels; the ladder,
   *      the rarity and the governance are ALREADY HERE and are not theirs to
   *      re-implement. ---- */
  chalk_ghost:     Object.freeze({ from: 3, side: 'class', animated: true, postReveal: false, once: null,    weight: 0, ms: 90,   p: 1 / 12, beat: 'door_card' }),
  overseen:        Object.freeze({ from: 3, side: 'class', animated: true, postReveal: false, once: 'class', weight: 0, ms: 240,  p: 1 / 6,  beat: 'round_gap' }),
  resume_slate:    Object.freeze({ from: 3, side: 'class', animated: true, postReveal: false, once: null,    weight: 0, ms: 120,  p: 1 / 8,  beat: 'resume' }),
  something_below: Object.freeze({ from: 3, side: 'class', animated: true, postReveal: false, once: 'class', weight: 0, ms: 1600, p: 1 / 5,  beat: 'resurface' }),
  unlisted_frame:  Object.freeze({ from: 4, side: 'class', animated: true, postReveal: false, once: 'class', weight: 0, ms: 400,  p: 1 / 4,  beat: 'stream' }),
});

/** The three that survive the reveal, at T2 rates. */
export const POST_REVEAL_TELLS = Object.freeze(['slip', 'looker', 'hum', 'wet_ink']);

/** Beat name -> the tell that lives in it. ONE tell per dead moment: two tells
 *  competing for one breath is how a haunting turns into a light show. */
export const BEATS = Object.freeze({
  door_card: 'chalk_ghost',
  round_gap: 'overseen',
  resume: 'resume_slate',
  resurface: 'something_below',
  stream: 'unlisted_frame',
});

/**
 * WHICH ELIGIBILITY LIST A DEAD MOMENT CLEARS. Every class-side tell is
 * `side:'class'` in the ladder above and that is not negotiable - but ONE of the
 * five dead moments does not happen inside a running class: `door_card` is the
 * class card on the CAMPUS, between the door click and the first input, and the
 * class has not started yet (the pitch's own list: "the door card before first
 * input ... the game has not started"). Asked on the class list it would fail
 * the `classRunning` rung for ever and the Chalk Ghost would be dead code.
 *
 * So the DOOR asks the campus list, which is exactly the right one for it: the
 * campus chrome is up, no class is running, and the shell's `busy` rung still
 * answers for the card itself (`cardIsOpen()`) - which is why the caller must
 * ask BEFORE it pops the card. A beat with no row here asks the class list, as
 * all four of the others do.
 */
export const BEAT_SIDE = Object.freeze({ door_card: 'campus' });

/** The boot slate's odds: ~1 in 40 boots, easing to 1 in 20 by T3. */
const SLATE_P_LOW = 1 / 40;
const SLATE_P_HIGH = 1 / 20;

/** THE DOUBLE BLINK. T3 and up, a slip may stutter twice (140, dark 60, 70). */
const SLIP_DOUBLE_P = 0.28;
const SLIP_DARK_MS = 60;
const SLIP_SECOND_MS = 70;
/** A File Name rides a Slip about one time in three from T2. */
const SLIP_CARRIES_KEY_P = 1 / 3;
/** How likely the Cold Draft is to be offered in the minute after a class. */
const DRAFT_AFTER_CLASS_P = 0.5;

/** How long after a tell's declared length a claim releases itself if the caller
 *  never did. See `claim()` - a wedged director is a bug, not a mood. */
const CLAIM_SLACK_MS = 6000;

/** The debug rungs (`?seep=force`) squeeze the governance so a headless rig can
 *  shoot a tell without waiting out a real evening. INERT with no query string. */
const FORCED_COOLDOWN_MS = 3000;
const FORCED_RATE_MS = 6000;

/* ----------------------------------------------------------------------------
 * PURE HALVES - imported directly by the suite, no DOM, no timers.
 * -------------------------------------------------------------------------- */

/**
 * SEALED PUNCH CARDS -> the escalation tier. The count is the reveal's own
 * (shell.js masteredCount): a card is sealed when its tenth hole has landed.
 * @param {number} sealed
 * @returns {number} 0..4
 */
export function tierForSealed(sealed) {
  const n = Math.max(0, Math.round(Number(sealed) || 0));
  if (n <= 0) return 0;
  if (n <= 3) return 1;
  if (n <= 6) return 2;
  if (n <= 8) return 3;
  return 4;
}

/**
 * How many cards are sealed. THE SAME RECIPE `shell.js masteredCount` runs -
 * `store.punchCard(key).complete` over the registry's list - read off the store
 * so the school counts nothing of its own. A card that cannot be read is simply
 * not a seal.
 * @param {Object} store  the shell's live store (read-only here)
 * @param {Array}  list   games.list (entries with a `key`)
 */
export function sealedCount(store, list) {
  if (!store || typeof store.punchCard !== 'function') return 0;
  const rows = Array.isArray(list) ? list : [];
  let n = 0;
  for (let i = 0; i < rows.length; i += 1) {
    const key = rows[i] && rows[i].key;
    if (!key) continue;
    try { if (store.punchCard(key).complete) n += 1; }
    catch (e) { /* an unreadable card is not an unlock */ }
  }
  return n;
}

/**
 * The same count, off a RAW `init.meta` blob rather than off the live store.
 * boot.js needs the tier while the splash is up - before `createStore`, before
 * the registry, before the shell exists at all - and the Feed Slate is the only
 * tell that has to know anything that early.
 *
 * The derivation is PUNCHCARD.md's one line and it MIRRORS `core/store.js
 * punchCard()` step for step - clean and de-duplicate both lists, fold the
 * ENROLLMENT DAY out of `dates`, intersect `sDates` with what is left, then:
 *
 *     punches = min(10, (enrolledAt ? 3 : 0) + dates.length + sDates.length)
 *
 * so a card heals DOWN, never up. Miss any one of those steps and this reads a
 * card differently from the store the shell hands the director thirty seconds
 * later; the suite asserts the two agree. A blob that cannot be read is nought
 * sealed, which is the right answer for a page that does not yet know anything.
 *
 * @param {Object} meta  init.meta
 * @returns {number}
 */
export function sealedFromMeta(meta) {
  const cards = (meta && typeof meta === 'object' && meta.punchCards) || null;
  if (!cards || typeof cards !== 'object') return 0;
  const clean = (list) => {
    const out = [];
    for (const d of (Array.isArray(list) ? list : [])) {
      if (typeof d === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(d) && out.indexOf(d) < 0) out.push(d);
    }
    return out;
  };
  let n = 0;
  for (const key of Object.keys(cards)) {
    const card = cards[key];
    if (!card || typeof card !== 'object') continue;
    const enrolledAt = (typeof card.enrolledAt === 'string' && card.enrolledAt) ? card.enrolledAt : null;
    const seen = clean(card.dates).filter((d) => d !== enrolledAt).slice(0, PUNCH_HOLES);
    const sSeen = clean(card.sDates).filter((d) => seen.indexOf(d) >= 0);
    const punches = Math.min(PUNCH_HOLES,
      (enrolledAt ? ENROLL_HOLES : 0) + seen.length + sSeen.length);
    if (punches >= PUNCH_HOLES) n += 1;
  }
  return n;
}

/** Is `name` in the live set for these conditions? PURE. */
export function tellActive(name, o) {
  const spec = Object.prototype.hasOwnProperty.call(TELLS, name) ? TELLS[name] : null;
  if (!spec) return false;
  const opts = o || {};
  if (opts.lite) return false;
  if (opts.postReveal && POST_REVEAL_TELLS.indexOf(name) < 0) return false;
  if (opts.reducedMotion && spec.animated) return false;
  const tier = Math.max(0, Math.min(4, Math.round(Number(opts.tier) || 0)));
  return tier >= spec.from;
}

/**
 * THE NIGHT'S SCHEDULE, planned once and walked by a timer. PURE and exported so
 * the suite can assert determinism without a clock.
 *
 * A slot whose eligibility fails at fire time is SPENT, never queued (the
 * ghosts.js encounter law) - which is what stops a player who sat in the settings
 * page for ten minutes from being ambushed by ten tells on the way out.
 *
 * @param {Object} o
 * @param {string} o.seed          `<utcDay>` (the caller namespaces)
 * @param {number} o.tier          0..4
 * @param {boolean=} o.postReveal
 * @param {boolean=} o.reducedMotion
 * @param {boolean=} o.lite
 * @param {number=} o.horizonMs
 * @param {number=} o.quietMs
 * @param {number=} o.cooldownMs
 * @param {number=} o.rateMs       override the tier rate (the forced rung)
 * @returns {Array<{atMs:number, name:string}>} ascending by atMs
 */
export function planSeep(o) {
  const opts = o || {};
  const lite = !!opts.lite;
  const reducedMotion = !!opts.reducedMotion;
  const postReveal = !!opts.postReveal;
  const tier = Math.max(0, Math.min(4, Math.round(Number(opts.tier) || 0)));
  if (lite || tier <= 0) return [];

  const pool = Object.keys(TELLS).filter((name) => {
    const spec = TELLS[name];
    if (spec.side !== 'campus' || !(spec.weight > 0)) return false;
    return tellActive(name, { tier, postReveal, reducedMotion, lite });
  });
  if (!pool.length) return [];

  /* Post-reveal the school keeps the T2 pace whatever the card count says. */
  const rateTier = postReveal ? 2 : tier;
  const rate = Number(opts.rateMs) > 0 ? Number(opts.rateMs) : TIER_RATE_MS[rateTier] || TIER_RATE_MS[2];
  const horizon = Number(opts.horizonMs) > 0 ? Number(opts.horizonMs) : PLAN_HORIZON_MS;
  const quiet = opts.quietMs == null ? QUIET_BOOT_MS : Math.max(0, Number(opts.quietMs) || 0);
  const cool = opts.cooldownMs == null ? COOLDOWN_MS : Math.max(0, Number(opts.cooldownMs) || 0);

  const rng = makeRng('seep|' + String(opts.seed == null ? '' : opts.seed) + '|plan|t' + tier
    + (postReveal ? '|pr' : '') + (reducedMotion ? '|rm' : ''));
  const total = pool.reduce((s, n) => s + TELLS[n].weight, 0);
  const spent = Object.create(null);      // once:'session' names, spent for the night
  const out = [];
  let at = quiet;
  let last = '';

  while (out.length < 64) {
    /* A JITTERED GAP, floored by the cooldown: the pace is a rate, never a
     * metronome, and no two tells can ever land inside 90 seconds of each other. */
    const gap = Math.max(cool, Math.round(rate * (0.55 + rng() * 0.9)));
    at += gap;
    if (at > horizon) break;

    /* Draw a name: weighted, never the same twice in a row, never a spent
     * once-per-session tell. A pool that has nothing left simply stops. */
    let name = '';
    for (let tries = 0; tries < 8 && !name; tries += 1) {
      let r = rng() * total;
      let pick = pool[pool.length - 1];
      for (let i = 0; i < pool.length; i += 1) {
        r -= TELLS[pool[i]].weight;
        if (r <= 0) { pick = pool[i]; break; }
      }
      if (pick === last) continue;
      if (TELLS[pick].once === 'session' && spent[pick]) continue;
      name = pick;
    }
    if (!name) continue;
    if (TELLS[name].once === 'session') spent[name] = true;
    out.push({ atMs: at, name });
    last = name;
  }
  return out;
}

/**
 * `?seep=force` is the play-test path, `?seep=<0-4>` pins the tier with it, and
 * `?seep=off` stands the whole director down. `?seepTell=<name>` pins the plan to
 * one tell so a rig can shoot exactly the frame it came for. All query-gated, so
 * a production launch (no query string at all) sees none of it.
 * @param {string=} search
 * @returns {{forced:boolean, off:boolean, tier:?number, only:?string}}
 */
export function seepOptions(search) {
  let q = search;
  if (q == null) {
    try { q = (typeof location !== 'undefined' && location && location.search) || ''; }
    catch (e) { q = ''; }
  }
  const s = String(q || '');
  const m = /(^|[?&])seep=([a-z0-9_]+)(&|$)/i.exec(s);
  const val = m ? String(m[2]).toLowerCase() : '';
  const off = val === 'off';
  const tier = /^[0-4]$/.test(val) ? Number(val) : null;
  const forced = !off && (val === 'force' || tier != null);
  const tm = /(^|[?&])seepTell=([a-z_]+)(&|$)/i.exec(s);
  const only = (forced && tm && Object.prototype.hasOwnProperty.call(TELLS, String(tm[2]).toLowerCase()))
    ? String(tm[2]).toLowerCase() : null;
  return { forced, off, tier, only };
}

/**
 * THE SUBJECT CODE. `SUBJ <code>/02` on the Other Stamp, and the Annex's Your
 * File bench will read the same one, so two artifacts agree when players compare
 * screenshots.
 *
 * There is NO unified player hash on `init` (shell.js's walker seeds its body off
 * a constant for the same reason), and inventing a server call for a ghost frame
 * would be absurd. So the code is a SHORT LOCAL HASH of the most stable things
 * this page already holds, in order of preference, all of them read-only:
 *   1. an explicit `uid` if a future init ever carries one
 *   2. EMI's `firstSeenAt` (the first night she was drawn, banked once)
 *   3. the earliest `enrolledAt` across the punch cards
 *   4. the earliest day row
 * A save with none of those is brand new and gets the school's own constant -
 * which is correct: a subject with no file yet is filed under the building.
 *
 * @param {Object} store
 * @param {Array=} list  games.list
 * @param {string=} uid
 * @returns {string} four uppercase hex characters
 */
export function subjectCode(store, list, uid) {
  const parts = [];
  if (typeof uid === 'string' && uid) parts.push('u:' + uid);
  try {
    const emi = (store && typeof store.get === 'function') ? store.get('emi') : null;
    const seen = emi && emi.stats && emi.stats.firstSeenAt;
    if (typeof seen === 'string' && seen) parts.push('e:' + seen);
  } catch (e) { /* a missing mascot is not an identity crisis */ }
  try {
    const rows = Array.isArray(list) ? list : [];
    const dates = [];
    for (let i = 0; i < rows.length; i += 1) {
      const key = rows[i] && rows[i].key;
      if (!key || !store || typeof store.punchCard !== 'function') continue;
      const card = store.punchCard(key);
      if (card && typeof card.enrolledAt === 'string' && card.enrolledAt) dates.push(card.enrolledAt);
    }
    dates.sort();
    if (dates.length) parts.push('c:' + dates[0]);
  } catch (e) { /* noop */ }
  try {
    const days = (store && typeof store.get === 'function' && store.get('days')) || null;
    if (days && typeof days === 'object') {
      const keys = Object.keys(days).sort();
      if (keys.length) parts.push('d:' + keys[0]);
    }
  } catch (e) { /* noop */ }
  const seed = parts.length ? parts.join('|') : 'the-arcademy';
  const n = Math.floor(hash01('seep|subj|' + seed) * 0xFFFF) & 0xFFFF;
  let hex = n.toString(16).toUpperCase();
  while (hex.length < 4) hex = '0' + hex;
  return hex;
}

/* ----------------------------------------------------------------------------
 * PLUMBING - every one of these is guarded for the node DOM double.
 * -------------------------------------------------------------------------- */

const SVGNS = 'http://www.w3.org/2000/svg';

function doc() { return (typeof document !== 'undefined' && document) ? document : null; }

function el(tag, cls) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls) n.className = cls;
  return n;
}

function svgEl(tag, attrs, cls) {
  const d = doc();
  if (!d) return null;
  let n = null;
  try { n = d.createElementNS ? d.createElementNS(SVGNS, tag) : d.createElement(tag); }
  catch (e) { return null; }
  if (!n) return null;
  try { if (cls) n.setAttribute('class', cls); } catch (e) { /* noop */ }
  if (attrs) {
    for (const k of Object.keys(attrs)) {
      try { n.setAttribute(k, String(attrs[k])); } catch (e) { /* noop */ }
    }
  }
  return n;
}

function setAttr(node, k, v) { try { if (node && node.setAttribute) node.setAttribute(k, v); } catch (e) { /* noop */ } }
function dropAttr(node, k) { try { if (node && node.removeAttribute) node.removeAttribute(k); } catch (e) { /* noop */ } }
function addCls(node, c) { try { if (node && node.classList) node.classList.add(c); } catch (e) { /* noop */ } }
function delCls(node, c) { try { if (node && node.classList) node.classList.remove(c); } catch (e) { /* noop */ } }
function drop(node) { try { if (node && node.parentNode) node.parentNode.removeChild(node); } catch (e) { /* noop */ } }

/** ONE AUDIO DOOR (trap 18): a cue is a REQUEST on `document`, never a node. */
function cue(name, level, extra) {
  try {
    const d = doc();
    if (!d || typeof d.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    d.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign({ name: String(name), level: Number(level) || 0.3, bus: 'fx' }, extra || {}),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/** The lite rung is a document-root class a game sets and everything else READS
 *  (trap 36's seam). Re-read every time: a class can leave one behind. */
function htmlHasLite() {
  try {
    const d = doc();
    const h = d && d.documentElement;
    return !!(h && h.classList && h.classList.contains && h.classList.contains('ae-lite'));
  } catch (e) { return false; }
}

/* ----------------------------------------------------------------------------
 * THE BOOT SLATE. Lives outside the director because it has to run while the
 * loader is up, before the shell exists at all (boot.js calls it).
 * -------------------------------------------------------------------------- */

/**
 * Should this boot show `FEED 09 · NO SIGNAL`? Seeded on the UTC day plus the
 * HOUR the page opened, so a reload inside a sitting replays the same answer
 * (nobody refreshes their way to a screenshot) while a long night is not one
 * single verdict. Reduced motion never sees it (trap 66's photosensitivity note).
 *
 * @param {{utcDay?:string, tier?:number, reducedMotion?:boolean, lite?:boolean,
 *          hour?:number, forced?:boolean}} o
 */
export function slateWanted(o) {
  const opts = o || {};
  if (opts.forced) return true;
  if (opts.lite || opts.reducedMotion) return false;
  const tier = Math.max(0, Math.min(4, Math.round(Number(opts.tier) || 0)));
  const p = tier >= 3 ? SLATE_P_HIGH : SLATE_P_LOW;
  let hour = opts.hour;
  if (hour == null) { try { hour = new Date().getHours(); } catch (e) { hour = 0; } }
  return hash01('seep|slate|' + String(opts.utcDay || '') + '|' + String(hour)) < p;
}

/**
 * THE FEED SLATE (tell 10). One ~80ms frame inside the boot loader, painted and
 * removed without delaying the boot by so much as a tick: the node is appended
 * to the loader and taken away again, and the loader's whole contract with
 * boot.js (`hidden` + `.is-done`) is untouched (trap 66).
 *
 * @param {Element} loader   #arc-loader
 * @param {{atMs?:number, ms?:number}=} o
 * @returns {Function} cancel
 */
export function mountFeedSlate(loader, o) {
  const opts = o || {};
  if (!loader || typeof loader.appendChild !== 'function') return () => {};
  const slate = el('div', 'arc-seep-slate');
  if (!slate) return () => {};
  const head = el('span', 'arc-seep-slate-id');
  const sub = el('span', 'arc-seep-slate-sub');
  if (head) head.textContent = 'FEED ' + GATE_CAM;
  if (sub) sub.textContent = 'NO SIGNAL';
  if (head) slate.appendChild(head);
  if (sub) slate.appendChild(sub);
  setAttr(slate, 'aria-hidden', 'true');

  let onT = 0;
  let offT = 0;
  let mounted = false;
  const hold = Math.max(40, Math.round(Number(opts.ms) || TELLS.feed_slate.ms));
  const at = Math.max(0, Math.round(Number(opts.atMs) == null ? 900 : Number(opts.atMs)));

  const off = () => { offT = 0; if (mounted) { mounted = false; drop(slate); } };
  const on = () => {
    onT = 0;
    /* The splash may already be gone (a fast boot, or failBoot snapped it): a
     * frame that has missed its beat is never struck late. */
    if (loader.hidden) return;
    try { loader.appendChild(slate); } catch (e) { return; }
    mounted = true;
    if (typeof setTimeout === 'function') offT = setTimeout(off, hold);
  };

  if (typeof setTimeout === 'function') onT = setTimeout(on, at);
  return function cancel() {
    try { if (onT) clearTimeout(onT); } catch (e) { /* noop */ }
    try { if (offT) clearTimeout(offT); } catch (e) { /* noop */ }
    onT = 0; offT = 0;
    off();
  };
}

/* ============================================================================
 * THE DIRECTOR
 * ==========================================================================*/

/**
 * @param {Object} o
 * @param {Object} o.store          the shell's live store. READ-ONLY here.
 * @param {Object=} o.games         the registry result (`games.list`)
 * @param {Object=} o.seams         GETTERS, never handles (trap 73):
 *                                  {campus(), ghosts(), emi()}
 * @param {Object=} o.gate          the shell's own answers:
 *                                  {campusVisible(), classRunning(), busy()}
 * @param {boolean=} o.reducedMotion
 * @param {boolean=} o.lite
 * @param {string=} o.utcDay        init.utcDateSeed - UTC seeds content (trap 8)
 * @param {string=} o.search        test seam for the query knobs
 * @param {Object=} o.clock         test seam {now(), later(fn,ms), cancel(id)}
 * @param {Function=} o.log
 * @returns {Object} the director
 */
export function createSeep(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const store = opts.store || null;
  const gamesList = (opts.games && Array.isArray(opts.games.list)) ? opts.games.list : [];
  const seams = opts.seams || {};
  const gate = opts.gate || {};
  const utcDay = String(opts.utcDay || '');
  const knobs = seepOptions(opts.search);

  const clock = opts.clock || {
    now: () => Date.now(),
    later: (fn, ms) => (typeof setTimeout === 'function' ? setTimeout(fn, ms) : 0),
    cancel: (id) => { try { if (id) clearTimeout(id); } catch (e) { /* noop */ } },
  };

  const reducedMotion = !!opts.reducedMotion;
  const forced = !!knobs.forced;
  const cooldownMs = forced ? FORCED_COOLDOWN_MS : COOLDOWN_MS;
  const quietMs = forced ? 0 : QUIET_BOOT_MS;

  const t0 = clock.now();
  let destroyed = knobs.off;
  let inFlight = null;                 // the one claim that may be out
  let lastFireAt = 0;
  let lastName = '';
  let pointerDown = false;
  let draftUntil = 0;                  // the minute after a class ended
  const sessionSpent = Object.create(null);
  const visitSpent = Object.create(null);
  const classSpent = Object.create(null);
  const fired = [];                    // debug only, capped; never persisted

  const roll = makeTaggedRoll('seep|' + utcDay);
  const pickRng = makeRng('seep|' + utcDay + '|pick');

  /* ---------------------------------------------------------------- state -- */

  function sealed() { return sealedCount(store, gamesList); }

  function tier() {
    if (knobs.tier != null) return knobs.tier;
    return tierForSealed(sealed());
  }

  function postReveal() {
    try { return !!(store && typeof store.get === 'function' && store.get('annexRevealSeen')); }
    catch (e) { return false; }
  }

  function isLite() { return !!opts.lite || htmlHasLite(); }

  function active(name) {
    return tellActive(name, {
      tier: tier(), postReveal: postReveal(), reducedMotion, lite: isLite(),
    });
  }

  /* ---------------------------------------------------- the eligibility -- */

  function emiTalking() {
    try {
      const emi = typeof seams.emi === 'function' ? seams.emi() : null;
      return !!(emi && emi.saying);
    } catch (e) { return false; }
  }

  /**
   * THE CHECKLIST. Campus tells need the campus chrome with no class running;
   * class-side tells trade that rung for a dead moment the class itself declared
   * (which the caller owns - the director cannot see whether input is armed).
   * Both sides then clear the rest of the list.
   */
  function eligible(o2) {
    const q = o2 || {};
    const side = q.side || 'campus';
    if (destroyed) return false;
    if (isLite()) return false;
    const now = clock.now();
    if (now - t0 < quietMs) return false;
    if (inFlight) return false;
    if (!q.ignoreCooldown && lastFireAt && now - lastFireAt < cooldownMs) return false;
    if (pointerDown) return false;
    if (emiTalking()) return false;
    /* The BUSY rung is the shell's own answer: a ceremony, the annex reveal, a
     * punch card, an open modal, a global suspend. Absent = not busy. */
    try { if (typeof gate.busy === 'function' && gate.busy()) return false; }
    catch (e) { /* an unanswerable gate is not a gate */ }
    if (side === 'campus') {
      try { if (typeof gate.classRunning === 'function' && gate.classRunning()) return false; }
      catch (e) { /* noop */ }
      try { if (typeof gate.campusVisible === 'function' && !gate.campusVisible()) return false; }
      catch (e) { /* noop */ }
    } else if (side === 'class') {
      try { if (typeof gate.classRunning === 'function' && !gate.classRunning()) return false; }
      catch (e) { /* noop */ }
    }
    return true;
  }

  /* --------------------------------------------------------- the claims -- */

  function spentFor(name) {
    const spec = TELLS[name];
    if (!spec || !spec.once) return false;
    if (spec.once === 'session') return !!sessionSpent[name];
    if (spec.once === 'visit') return !!visitSpent[name];
    if (spec.once === 'class') return !!classSpent[name];
    return false;
  }

  function markSpent(name) {
    const spec = TELLS[name];
    if (!spec || !spec.once) return;
    if (spec.once === 'session') sessionSpent[name] = true;
    else if (spec.once === 'visit') visitSpent[name] = true;
    else if (spec.once === 'class') classSpent[name] = true;
  }

  /**
   * Take the floor. Governance only - no roll. Returns a claim or null.
   * A claim is EXCLUSIVE and the caller MUST release it.
   */
  function claim(name, o2) {
    const spec = Object.prototype.hasOwnProperty.call(TELLS, name) ? TELLS[name] : null;
    if (!spec) return null;
    if (!active(name)) return null;
    if (spentFor(name)) return null;
    /* NO TELL EVER FIRES TWICE IN A ROW. The doctrine's first line. */
    if (name === lastName && !forced) return null;
    if (!eligible(Object.assign({ side: spec.side === 'boot' ? 'campus' : spec.side }, o2 || {}))) return null;

    lastFireAt = clock.now();
    lastName = name;
    markSpent(name);
    let released = false;
    const token = {
      name,
      tell: name,
      ms: spec.ms,
      release() {
        if (released) return;
        released = true;
        if (inFlight === token) inFlight = null;
      },
    };
    inFlight = token;
    /* THE WATCHDOG. A claim the caller never releases would close the seep for
     * the whole session - and there is at least one honest path to that (the
     * board's own cue timers refuse to run once it has left the page, so a
     * misprint on a board the player clicked away from never reaches its own
     * correction). A wedged director is a bug, not a mood: every claim releases
     * itself a generous slack after its declared length, and a caller that has
     * already released pays nothing. */
    later(token.release, spec.ms + CLAIM_SLACK_MS);
    if (fired.length < 64) fired.push({ at: lastFireAt - t0, name });
    return token;
  }

  /** Roll first, then claim. `p` is the tell's own per-event probability; a tell
   *  with no `p` is a plan tell and rolls 1 (the plan already decided). */
  function maybe(name, o2) {
    const spec = Object.prototype.hasOwnProperty.call(TELLS, name) ? TELLS[name] : null;
    if (!spec) return null;
    if (!active(name)) return null;
    if (spentFor(name)) return null;
    const p = (o2 && o2.p != null) ? Number(o2.p) : (spec.p == null ? 1 : spec.p);
    /* THE ROLL IS ALWAYS CONSUMED, even when the gate below refuses - the
     * pattern is a function of the seed, not of the wall clock (Echo's clip
     * law). A roll spent on a refused beat is spent. */
    const r = roll(name);
    if (!forced && !(r < p)) return null;
    return claim(name, o2);
  }

  /**
   * THE CLASS-SIDE DOOR. `name` is a DEAD MOMENT, not a tell: the game says
   * where it is standing and the director says whether anything lives there.
   * @param {string} name
   * @param {Object=} payload  free-form; reserved for the kit (e.g. {gameKey})
   * @returns {?{name:string, tell:string, ms:number, release:Function}}
   */
  function beat(name, payload) {
    const key = String(name || '');
    const tellName = Object.prototype.hasOwnProperty.call(BEATS, key) ? BEATS[key] : null;
    if (!tellName) return null;
    if (knobs.only && knobs.only !== tellName) return null;
    /* THE DOOR CARD ASKS THE CAMPUS LIST (see BEAT_SIDE). Everything else asks
     * the class list, which is what a dead moment inside a running class is. */
    const side = Object.prototype.hasOwnProperty.call(BEAT_SIDE, key) ? BEAT_SIDE[key] : 'class';
    const token = maybe(tellName, { side });
    if (!token) return null;
    token.beat = key;
    token.payload = payload || null;
    return token;
  }

  /* --------------------------------------------------------- the plan ---- */

  let plan = [];
  let planCursor = 0;
  let tickTimer = 0;
  let planTier = -1;
  let planReveal = false;

  function buildPlan() {
    const tr = tier();
    const pr = postReveal();
    planTier = tr;
    planReveal = pr;
    planCursor = 0;
    plan = planSeep({
      seed: utcDay,
      tier: tr,
      postReveal: pr,
      reducedMotion,
      lite: isLite(),
      quietMs,
      cooldownMs,
      rateMs: forced ? FORCED_RATE_MS : 0,
    });
    if (knobs.only) {
      /* `?seepTell=` PINS A TELL; it must never SPEND one. Only the roaming
       * campus tells come through the plan's door, so those are the only ones a
       * slot may be re-labelled to. Pin anything else - the class-side kit, the
       * boot slate, the event-ridden tells - and the plan stands down instead,
       * because a slot that claimed a `once:'class'` tell on the shell's own
       * clock would burn it before the game ever reached the dead moment it
       * lives in, and the pinned tell could then never fire at all. */
      const only = TELLS[knobs.only];
      plan = (only && only.side === 'campus' && only.weight > 0)
        ? plan.map((s) => ({ atMs: s.atMs, name: knobs.only }))
        : [];
    }
    say('seep: tier ' + tr + (pr ? ' (post-reveal)' : '') + ', ' + plan.length + ' slots planned'
      + (forced ? ' [FORCED]' : ''));
  }

  const TICK_MS = 5000;

  function tick() {
    tickTimer = 0;
    if (destroyed) return;
    try {
      /* THE LADDER MOVES UNDER YOU. A card sealed mid-session (or the reveal
       * firing) re-plans the rest of the night rather than waiting for a
       * relaunch - re-planned from the SAME seed, so the schedule a reload
       * would have replayed is the schedule you get. */
      if (tier() !== planTier || postReveal() !== planReveal) buildPlan();
      const elapsed = clock.now() - t0;
      while (planCursor < plan.length && plan[planCursor].atMs <= elapsed) {
        const slot = plan[planCursor];
        planCursor += 1;
        /* A slot inside the exhale after a class becomes the Cold Draft, if the
         * draft is live and it is not what just fired. No extra roll: the
         * weighting is a re-label, which keeps the plan replayable. */
        let name = slot.name;
        if (draftUntil && clock.now() < draftUntil && active('cold_draft')
            && name !== 'cold_draft' && lastName !== 'cold_draft') {
          name = 'cold_draft';
        }
        const token = claim(name);
        if (token) { runTell(name, token); break; }
        /* A slot the gate refused is SPENT, never queued. */
      }
    } catch (e) { say('seep tick threw (ignored): ' + ((e && e.message) || e)); }
    arm();
  }

  function arm() {
    if (destroyed || tickTimer) return;
    tickTimer = clock.later(tick, TICK_MS);
  }

  /* ------------------------------------------------------- the campus ---- */

  function campusSeam() {
    try {
      const c = typeof seams.campus === 'function' ? seams.campus() : null;
      if (!c || typeof c.seepSeam !== 'function') return null;
      return c.seepSeam();
    } catch (e) { return null; }
  }

  const timers = new Set();
  function later(fn, ms) {
    if (destroyed) return 0;
    const id = clock.later(() => { timers.delete(id); if (!destroyed) { try { fn(); } catch (e) { /* noop */ } } }, ms);
    timers.add(id);
    return id;
  }
  function clearTimers() {
    for (const id of Array.from(timers)) { try { clock.cancel(id); } catch (e) { /* noop */ } }
    timers.clear();
  }

  /** Which room slips / flashes its key tonight. Seeded, and it skips a room the
   *  pointer is actually resting on (a plate that changes under the cursor reads
   *  as a bug, not as a tell). */
  function pickRoom(seam) {
    if (!seam || typeof seam.roomKeys !== 'function') return '';
    const keys = seam.roomKeys().filter((k) => !!CAM_NO[k] || !!ROOMS[k]);
    if (!keys.length) return '';
    const start = Math.floor(pickRng() * keys.length);
    for (let i = 0; i < keys.length; i += 1) {
      const key = keys[(start + i) % keys.length];
      const g = seam.roomNode(key);
      if (!g) continue;
      try { if (typeof g.matches === 'function' && g.matches(':hover')) continue; }
      catch (e) { /* a double with no matches() is a room nobody is hovering */ }
      return key;
    }
    return '';
  }

  /* ---- 01 THE SLIP ------------------------------------------------------ */
  /* One room drops its varsity paint and renders the way the monitors downstairs
   * render it: cold linework, scanlines, a camera tag. Class toggles plus one
   * <g> of furniture inside the room's own group - no filters, and the scanlines
   * drift by TRANSFORM (trap 36). */
  function runSlip(token) {
    const seam = campusSeam();
    const key = pickRoom(seam);
    const g = key && seam.roomNode(key);
    if (!g) { token.release(); return; }

    const rect = (ROOMS[key] && ROOMS[key].rect) || null;
    let furniture = null;
    if (rect) {
      const [x, y, w, h] = rect;
      furniture = svgEl('g', null, 'campus-seepcam');
      if (furniture) {
        const scan = svgEl('rect', { x, y: y - 6, width: w, height: h + 12 }, 'campus-seepscan');
        const tag = svgEl('text', { x: x + 8, y: y + 16 }, 'campus-seeposd');
        const rec = svgEl('text', { x: x + w - 8, y: y + 16, 'text-anchor': 'end' }, 'campus-seeposd campus-seeprec');
        if (tag) tag.textContent = 'CAM ' + (CAM_NO[key] || GATE_CAM);
        if (rec) rec.textContent = 'REC ·';
        if (scan) furniture.appendChild(scan);
        if (tag) furniture.appendChild(tag);
        if (rec) furniture.appendChild(rec);
        setAttr(furniture, 'pointer-events', 'none');
        setAttr(furniture, 'aria-hidden', 'true');
        try { g.appendChild(furniture); } catch (e) { furniture = null; }
      }
    }

    /* THE FILE NAME RIDES A SLIP about one time in three from T2 - and inside a
     * Slip it wears the cam tag (owner ruling 0824: bare key on plates, cam tag
     * only inside Slips). The plate swap is the same recipe either way. */
    let restoreKey = null;
    if (active('file_name') && roll('slip_key') < SLIP_CARRIES_KEY_P) {
      restoreKey = flashKey(seam, key, true);
    }

    const double = tier() >= 3 && roll('slip_double') < SLIP_DOUBLE_P && !reducedMotion;
    setAttr(g, 'data-seep', 'slip');
    /* W3 P2-10: the room drops to the monitors downstairs, and the feed picks
     * it up. Barely-there BY DOCTRINE - a fiftieth of a game pop, under the
     * threshold at which a player would say they heard something rather than
     * that something happened. On the SLIP itself, once; the second flick of a
     * double is the same room still slipped, not a new tell. */
    cue('glitch', 0.05);
    later(() => {
      dropAttr(g, 'data-seep');
      if (restoreKey) { restoreKey(); restoreKey = null; }
      if (!double) { drop(furniture); token.release(); return; }
      later(() => {
        setAttr(g, 'data-seep', 'slip');
        later(() => { dropAttr(g, 'data-seep'); drop(furniture); token.release(); }, SLIP_SECOND_MS);
      }, SLIP_DARK_MS);
    }, TELLS.slip.ms);
  }

  /* ---- 02 THE FILE NAME ------------------------------------------------- */
  /* The room plate stops saying what the school calls the room and, for two
   * frames, says what the CODE calls it. Real keys, monospace phosphor green,
   * and deliberately NOT lexicon-skinnable: this is the filing system talking. */
  function devKeyFor(key) {
    if (key === 'sort' && roll('retired') < MISDIRECTION_P) return RETIRED_KEY;
    return key;
  }

  /** Swap the plate; returns a restore function (or null if there was nothing
   *  to swap). `camTag` is the in-a-Slip flavour. */
  function flashKey(seam, key, camTag) {
    if (!seam || typeof seam.roomSub !== 'function') return null;
    const node = seam.roomSub(key);
    if (!node) return null;
    let before = '';
    try { before = String(node.textContent == null ? '' : node.textContent); } catch (e) { return null; }
    const dev = devKeyFor(key);
    const text = camTag ? ('CAM ' + (CAM_NO[key] || GATE_CAM) + ' · ' + dev) : dev;
    try { node.textContent = text; } catch (e) { return null; }
    addCls(node, 'arc-seep-key');
    setAttr(node, 'data-seep', 'key');
    let done = false;
    return function restore() {
      if (done) return;
      done = true;
      try { node.textContent = before; } catch (e) { /* noop */ }
      delCls(node, 'arc-seep-key');
      dropAttr(node, 'data-seep');
    };
  }

  function runFileName(token) {
    const seam = campusSeam();
    const key = pickRoom(seam);
    const restore = key ? flashKey(seam, key, false) : null;
    if (!restore) { token.release(); return; }
    later(() => { restore(); token.release(); }, TELLS.file_name.ms);
  }

  /* ---- 04 THE HUM ------------------------------------------------------- */
  /* Half a second of fluorescent ballast under the campus quiet. Campus-ambient
   * only, and it rides every mute / duck / level law the mixer already keeps
   * because it is an ordinary `arcademy-sfx` request (trap 18). */
  function runHum(token) {
    cue('seep_hum', 0.22);
    later(() => token.release(), TELLS.hum.ms);
  }

  /* ---- 05 THE LOOKER, UPSTAIRS ----------------------------------------- */
  function runLooker(token) {
    let ok = false;
    try {
      const g = typeof seams.ghosts === 'function' ? seams.ghosts() : null;
      ok = !!(g && typeof g.lookOut === 'function' && g.lookOut({ ms: TELLS.looker.ms }));
    } catch (e) { ok = false; }
    if (!ok) { token.release(); return; }
    later(() => token.release(), TELLS.looker.ms + 120);
  }

  /* ---- 06 EMI'S BAD FRAME ---------------------------------------------- */
  /* ONE phosphor frame on her widget. STRICTLY VISUAL: her voice lock is
   * absolute pre-reveal (owner ruling 0824) - this touches no chain, no line and
   * no state machine, only a class on the `.emi` root, and only while she idles.
   * NO `forwards` fill and NO transform: trap 74's welded scaleY is exactly the
   * bug this would otherwise reproduce. */
  function runBadFrame(token) {
    let node = null;
    try {
      const emi = typeof seams.emi === 'function' ? seams.emi() : null;
      if (!emi || emi.saying || emi.hidden) { token.release(); return; }
      node = emi.el || null;
    } catch (e) { node = null; }
    if (!node) { token.release(); return; }
    addCls(node, 'arc-seep-frame');
    later(() => { delCls(node, 'arc-seep-frame'); token.release(); }, TELLS.bad_frame.ms);
  }

  /* ---- 07 THE SLOW SECOND ---------------------------------------------- */
  /* DISPLAY ONLY. campus.js freezes the PICTURE of the countdown and then paints
   * the two skipped seconds and the true value in three quick ticks. The
   * underlying timer never lies, so nothing downstream drifts. */
  function runSlowSecond(token) {
    const seam = campusSeam();
    if (!seam || typeof seam.holdBell !== 'function') { token.release(); return; }
    let ok = false;
    try { ok = !!seam.holdBell(TELLS.slow_second.ms); } catch (e) { ok = false; }
    if (!ok) { token.release(); return; }
    later(() => token.release(), TELLS.slow_second.ms + 400);
  }

  /* ---- 11 THE COLD DRAFT ----------------------------------------------- */
  /* The edges of the plan breathe faintly green for 2.4 seconds. One vignette
   * layer, OPACITY ONLY: no filters, no blend modes (cam-wall budget law). */
  function runColdDraft(token) {
    const seam = campusSeam();
    const layer = seam && seam.layer;
    if (!layer || typeof layer.appendChild !== 'function') { token.release(); return; }
    const vig = el('div', 'arc-seep-draft');
    if (!vig) { token.release(); return; }
    setAttr(vig, 'aria-hidden', 'true');
    try { layer.appendChild(vig); } catch (e) { token.release(); return; }
    /* remove -> reflow -> add is splitflap's replay recipe (trap 4); a fresh
     * node needs none of it, it just has to be in the tree for a frame. */
    later(() => addCls(vig, 'is-on'), 16);
    later(() => { drop(vig); token.release(); }, TELLS.cold_draft.ms + 120);
  }

  const RUNNERS = {
    slip: runSlip,
    file_name: runFileName,
    hum: runHum,
    looker: runLooker,
    bad_frame: runBadFrame,
    slow_second: runSlowSecond,
    cold_draft: runColdDraft,
  };

  function runTell(name, token) {
    const fn = RUNNERS[name];
    if (!fn) { token.release(); return; }
    try { fn(token); }
    catch (e) {
      say('seep: ' + name + ' threw (' + ((e && e.message) || e) + ')');
      try { token.release(); } catch (e2) { /* noop */ }
    }
  }

  /* ------------------------------------------------------ event doors ---- */

  /**
   * THE MISPRINT (tell 03). The split-flap board asks at DEAL time and nowhere
   * else - a repaint that is not a reveal must not re-flap (trap 4). Answers
   * `{row, text, holdMs}` or null.
   * @param {Array} rows   the rows about to be dealt
   */
  function misprintFor(rows) {
    const list = Array.isArray(rows) ? rows : [];
    if (!list.length) return null;
    const token = maybe('misprint');
    if (!token) return null;
    /* The wrong strip is a DEV KEY in board voice: DEJA_VU where MEMORY LAB
     * belongs. The board deals it, holds, and flaps itself right. */
    const i = Math.floor(pickRng() * list.length);
    const row = list[i] || list[0];
    const key = String((row && row.id) || '');
    const wrong = (key && ROOMS[key] ? key : key || 'records').toUpperCase();
    /* The board's own re-flap frees the claim: it hands us back the moment the
     * true strip has settled, and we hold it until then. */
    return {
      row: i,
      text: wrong,
      holdMs: TELLS.misprint.ms,
      done() { token.release(); },
    };
  }

  /**
   * THE OTHER STAMP (tell 08). The report card asks as the grade stamp comes
   * down; the ghost frame is 80ms of `SUBJ <code>/02` under the real mark.
   * Answers `{text, ms, done()}` or null.
   */
  function stampGhost() {
    /* THE REPORT CARD IS ITS OWN SURFACE, and it is BEAT_SIDE's problem one
     * screen along: `showReport()` sets `screen = 'report'`, so the campus list
     * asked here would fail `campusVisible()` on every single card for ever and
     * the Other Stamp would be dead code that never once fired.
     *
     * It asks the COMMON checklist instead, which is the right one for a paper
     * the player just opened: the quiet boot, the global cooldown, one claim at
     * a time, the pointer, EMI mid-line - and the shell's own `busy` rung,
     * which is what already covers the ceremony this stamp rides (an end card,
     * a punch card, the annex reveal). */
    const token = maybe('other_stamp', { side: 'report' });
    if (!token) return null;
    return {
      text: 'SUBJ ' + subjectCode(store, gamesList, opts.uid) + '/02',
      ms: TELLS.other_stamp.ms,
      done() { token.release(); },
    };
  }

  /* ------------------------------------------------------------ the ear -- */

  function note(kind) {
    if (destroyed) return;
    const k = String(kind || '');
    if (k === 'classEnd') {
      draftUntil = clock.now() + DRAFT_WINDOW_MS;
      /* THE EXHALE. Offered once, on its own roll, outside the plan - you just
       * fed the thing downstairs. Delayed a breath so it never lands on the
       * report card's own paper cue. */
      later(() => {
        if (!active('cold_draft')) return;
        const token = maybe('cold_draft', { p: DRAFT_AFTER_CLASS_P });
        if (token) runTell('cold_draft', token);
      }, 2400);
      return;
    }
    if (k === 'classStart') {
      for (const n of Object.keys(classSpent)) delete classSpent[n];
      return;
    }
    if (k === 'campusUnmount') {
      for (const n of Object.keys(visitSpent)) delete visitSpent[n];
    }
  }

  /* ---------------------------------------------------------- listeners -- */
  /* THREE PASSIVE POINTER READS AND NOTHING ELSE. Trap 80's shape: no
   * preventDefault, no stopPropagation, no key listener of any kind (the Esc
   * ladder is boot.js's and shell.js's, and the seep adds no rung to it). */
  let bound = false;
  const onDown = () => { pointerDown = true; };
  const onUp = () => { pointerDown = false; };
  try {
    const d = doc();
    if (d && typeof d.addEventListener === 'function') {
      d.addEventListener('pointerdown', onDown, { passive: true, capture: true });
      d.addEventListener('pointerup', onUp, { passive: true, capture: true });
      d.addEventListener('pointercancel', onUp, { passive: true, capture: true });
      bound = true;
    }
  } catch (e) { bound = false; }

  if (!destroyed) { buildPlan(); arm(); }

  return {
    tier,
    sealed,
    postReveal,
    active,
    eligible,
    maybe,
    claim,
    beat,
    note,
    misprintFor,
    stampGhost,
    feedTag: (gameKey) => feedTagFor(gameKey),
    subjectCode: () => subjectCode(store, gamesList, opts.uid),
    /** Test/playtest seam: run a named campus tell right now, governance and all. */
    fire(name) {
      const token = claim(name, { ignoreCooldown: forced });
      if (!token) return false;
      runTell(name, token);
      return true;
    },
    debug() {
      return {
        forced,
        off: knobs.off,
        only: knobs.only,
        tier: tier(),
        sealed: sealed(),
        postReveal: postReveal(),
        lite: isLite(),
        reducedMotion,
        cooldownMs,
        quietMs,
        plan: plan.slice(),
        cursor: planCursor,
        fired: fired.slice(),
        inFlight: inFlight ? inFlight.name : null,
      };
    },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      try { clock.cancel(tickTimer); } catch (e) { /* noop */ }
      tickTimer = 0;
      clearTimers();
      if (inFlight) { try { inFlight.release(); } catch (e) { /* noop */ } inFlight = null; }
      if (bound) {
        try {
          const d = doc();
          d.removeEventListener('pointerdown', onDown, true);
          d.removeEventListener('pointerup', onUp, true);
          d.removeEventListener('pointercancel', onUp, true);
        } catch (e) { /* noop */ }
        bound = false;
      }
    },
  };
}

export default createSeep;
