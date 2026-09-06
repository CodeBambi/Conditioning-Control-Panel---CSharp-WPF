/* ============================================================================
 * shell/prizecounter.js - THE PRIZE COUNTER.
 *
 * The one room in the school where the money goes back out. Two surfaces and
 * nothing else: the SHELF, which takes tickets and is stacked with the sort of
 * thing a ticket buys, and the CASE, which is glass, which is locked, and which
 * only opens for a token. Every graded class pays tickets; the first S-rank of
 * your day drops exactly one token in the tray. So the shelf is where a good
 * week goes and the case is where a good NIGHT goes, and the room says that by
 * standing them side by side rather than by explaining it.
 *
 * THE ROOM IS DRAWN, THE GOODS ARE PAINTED. The counter, the shelf timber, the
 * glass and the little price flags are still borders and gradients out of
 * prizecounter.css, the way the campus draws its buildings - the ROOM has to
 * survive a catalog the host can change without anybody repainting a plate.
 * What changed on 2026-08-27 is the GOODS: every sku in the catalog now has a
 * sprite in `art/prizes/`, and a row wears it. The glyph did not go away and
 * must not - it is the bed the sprite sits on, so a sku whose art has not been
 * drawn yet, or whose png fails to load, still reads as a thing on a shelf
 * instead of as a hole. See THE TWO REGISTERS below.
 *
 * THE ECHO LAW IS THE WHOLE FILE (trap 1). Pressing Trade does not buy
 * anything. It sends `prize-buy` up the seam, it puts the row to sleep with a
 * "asking the counter" label, and then it waits. Nothing on this page moves -
 * not a balance, not a badge, not a button - until `settle()` is called with
 * the host's own `wallet-result` frame. If that frame never comes, the row
 * wakes back up unchanged and the wallet reads exactly what it read before.
 * The page proposes; the counter disposes. A page that spent its own money
 * would be a page that can be lied to, and a currency you can lie to is not a
 * currency.
 *
 * NARROW CAPS, THE ANNEX'S LAW. Nothing under this line imports the store, the
 * bridge or EMI. Every fact about the player - what they hold, what they can
 * afford, what is already theirs - arrives as a function in `caps`, and every
 * consequence leaves as a callback. That is what makes the room testable in
 * bare node against the DOM double, which is also why it walks its own children
 * BY INDEX (`findCls`) and never touches `querySelector`.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';
/* THE HOUSE MOVES, and motion is ALL they are. Nothing imported on this line
 * can read a wallet, send a message or decide that a purchase happened - see
 * counterfx.js's own header. The echo law lives above them: they are handed
 * pictures of answers this file has already received. */
import {
  thud, shiver, warmGlow, ghostGold, bankSpend, armBoughtHold,
  sparkBurst, chargeHold, onRevealOn,
} from './counterfx.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 * -------------------------------------------------------------------------- */

/** The shop's surfaces, top to bottom. CHIPS COME LAST because the Back Room
 *  is last: you walk past the shelf and the case to reach it, and this is that
 *  walk sat down. No chip rows in the catalog draws no third shelf, which is
 *  the same nothing an empty token case has always drawn. */
export const SURFACES = Object.freeze(['t', 'k', 'c']);

/**
 * WHICH SURFACE A ROW BELONGS TO, and this is the only place that decides.
 * Eight callers each asked `row.cur === 'k' ? 'k' : 't'` in their own words,
 * which was fine at two surfaces and quietly wrong at three - a chip row filed
 * on the ticket shelf and priced with a stub. The old fallback is kept on
 * purpose: a currency this build never heard of is a TICKET row, which is what
 * all eight already assumed and the safest thing an unknown row can be.
 * @param {*} row a catalog row, or anything at all
 * @returns {'t'|'k'|'c'}
 */
export function curOf(row) {
  const cur = row && row.cur;
  return (cur === 'k' || cur === 'c') ? cur : 't';
}

/** How long a pressed row waits for the counter before it wakes back up. The
 *  host answers a prize-buy in the same tick it receives it, so six seconds is
 *  not a timeout, it is a promise that the room can never wedge. */
export const ECHO_WAIT_MS = 6000;

/**
 * THE CHARGE-HOLD's floor, and its master switch.
 *
 * A sixty-ticket poster is a click, because a held verb on a poster is friction
 * with no ceremony in it. A thousand and up is a week of good nights, and a
 * week of good nights should not leave on one stray press - that row gets the
 * ring. The switch is here rather than in a settings file because it governs
 * the ONE path in this bundle that spends a player's money: if the hold ever
 * misbehaves, this line turns it off and every row goes back to the plain click
 * that shipped, with no other edit anywhere.
 */
export const HOLD_TO_BUY = true;
/**
 * The ticket floor, and it is the audit's own number: a thousand tickets.
 *
 * THE SHELF DOES NOT REACH IT YET. The dearest ticket row in the school today
 * is the swim outfit at 340, so this floor is a promise about a shelf that has
 * not arrived rather than a rule anybody will meet this week. It is left at the
 * audited number on purpose - the day a four-figure row lands, it arrives with
 * its ceremony already fitted and nobody has to remember this file exists.
 */
export const HOLD_FROM = 1000;
/**
 * The token floor, which is where the hold actually lives today.
 *
 * A TOKEN IS NOT A SMALL NUMBER. One token is the first S-rank of a day - you
 * cannot grind them, you cannot buy them, and there is exactly one on offer per
 * day however well the night goes. So the case's two-token rows are the
 * dearest thing in the school by a wide margin: two perfect days, spent in one
 * press, on a click with no way back. That is the purchase the House Book's
 * held verb was written for, and pinning the hold to the ticket floor alone
 * would have shipped the primitive with nothing on the shelf able to trigger
 * it.
 *
 * ONE TOKEN STAYS A CLICK. The two lever unlocks cost one, they are the first
 * thing a player spends a token on, and a hold on somebody's first purchase
 * reads as the room not trusting them.
 */
export const HOLD_FROM_K = 2;

/** The chip floor: the ticket floor's own reasoning in the Back Room's money.
 *  A hundred chips is one Sparkle (BACKROOM-CONTRACT §1), so this is twenty-five
 *  Sparkle of table time in one press - the same "a week of good nights should
 *  not leave on one stray click". Nothing on the shelf reaches it yet; chip
 *  rows arrive with the catalog wave, so re-audit it the day prices land. */
export const HOLD_FROM_C = 2500;

/** The floor this row has to clear to be worth holding down. */
export function holdFloor(cur) {
  if (cur === 'k') return HOLD_FROM_K;
  if (cur === 'c') return HOLD_FROM_C;
  return HOLD_FROM;
}

/**
 * THE TRAY BEAT's three cuts and the line between a cheap row and a dear one.
 *
 * 200 tickets is the shelf's own waist: below it are the posters and the little
 * consumables a good week pays for twice over, above it are the things a player
 * saves toward. The token case is dear by definition - there is no cheap token.
 */
export const BEAT_DEAR_T = 200;
/** The chip shelf's waist: five Sparkle. Enough that the row was saved toward,
 *  not so much that only the top of the shelf ever gets the full party.
 *  Re-audit with real prices, exactly like HOLD_FROM_C. */
export const BEAT_DEAR_C = 500;

/** The price a row has to clear on its own surface to be worth the full beat. */
export function dearFloor(cur) {
  return (cur === 'c') ? BEAT_DEAR_C : BEAT_DEAR_T;
}
export const BEAT_MS_SMALL = 1000;
export const BEAT_MS_FULL = 1600;
export const BEAT_MS_STILL = 1200;
/** The full party this many times a sitting, then the compact cut. */
export const BEAT_FULL_RUNS = 3;
/**
 * The gap between the tray withdrawing and B's card whooshing up. Long enough
 * that they read as two strokes of one gesture rather than a collision, short
 * enough that nobody wonders whether the room forgot.
 */
export const HANDOVER_MS = 200;

/** THE ALMOST's band: within forty, or within a seventh of a dear row. */
export function shortfallBand(cost) {
  return Math.max(40, Math.round((Number(cost) || 0) * 0.15));
}

/**
 * THE MARQUEE's heat, 0.2 at a flat wallet and 0.55 at a wallet that reaches
 * the top of the shelf.
 *
 * READ THE PHRASE HONESTLY. "wallet / dearest affordable" is a ratio that is
 * always >= 1 the moment you can afford anything at all, so the number that is
 * actually wanted is the one that phrase is REACHING for: how far up the shelf
 * your money reaches. Dearest row you can afford over dearest row there is. A
 * player who can buy the cheapest poster and nothing else runs a dim ring; a
 * player who can take the wide board home runs a hot one. Both currencies are
 * measured and the warmer one wins, because a purse fat in tokens is not a cold
 * night just because the ticket shelf is out of reach.
 *
 * @param {Array} catalog rows as the host sent them
 * @param {{t:number,k:number,c:number}} purse
 * @returns {number} 0.2 .. 0.55
 */
export function marqueeHeat(catalog, purse) {
  const rows = Array.isArray(catalog) ? catalog : [];
  const have = {
    t: Number(purse && purse.t) || 0,
    k: Number(purse && purse.k) || 0,
    c: Number(purse && purse.c) || 0,
  };
  let best = 0;
  for (const cur of SURFACES) {
    let top = 0;      // the dearest row on this surface
    let reach = 0;    // the dearest one this purse covers
    for (const r of rows) {
      if (!r || curOf(r) !== cur) continue;
      if (r.locked === true) continue;
      const cost = Math.max(0, Math.round(Number(r.cost) || 0));
      if (cost > top) top = cost;
      if (cost <= have[cur] && cost > reach) reach = cost;
    }
    if (top > 0) best = Math.max(best, Math.min(1, reach / top));
  }
  return 0.2 + (0.35 * best);
}

/** Every refusal the host can send back, mapped to the line the counter says.
 *  An unknown reason falls through to the same shrug an unknown sku gets, so a
 *  host that grows a new reason tomorrow degrades to a sentence instead of to
 *  an empty toast. */
export const REFUSALS = Object.freeze({
  poor: ['prize_poor', 'Not quite enough on you for that one yet.'],
  owned: ['prize_owned_msg', 'You have that one already.'],
  full: ['prize_full', 'Your pockets are full of those. Use one first.'],
  locked: ['prize_locked_msg', 'That one stays in the case for now.'],
  unknown: ['prize_unknown', 'The counter does not know that one. Odd.'],
  /* THE SHARED WALLET (2026-08-28). The wallet is on the account now, so two
   * refusals arrive that no local till could ever have produced: `offline` when
   * the host could not reach the bank, and `busy` when one of your OWN other
   * devices is mid-write on the same wallet. Neither is a refusal the player did
   * anything to earn, and in both cases nothing was charged - which is the half
   * worth saying out loud. Without these rows both fell to the unknown shrug. */
  offline: ['prize_offline', 'The counter cannot reach the bank right now. Nothing was charged.'],
  busy: ['prize_busy', 'Somebody is already at the drawer. Give it a second and ask again.'],
  /* The tier gate, told apart from the wire being down (2026-08-29): the bank
   * DID answer, and the answer was that this account cannot bank here yet.
   * Without this row the tier refusal wore the offline line. */
  tier: ['prize_tier', 'The bank does not serve this account yet. Nothing was charged.'],
});

/** The glyph each sku wears on its little drawn box. Text, not art: a sku the
 *  catalog grows before this table does gets the plain parcel and still reads
 *  as a thing on a shelf. */
export const GLYPHS = Object.freeze({
  id_frame_gold: '▧',
  id_frame_navy: '▨',
  confetti_stamp: '✶',
  late_slip: '✉',
  honors_lever: '⌇',
  free_swim_key: '⚿',
  de_5x5: '▦',
  jukebox: '♪',
  /* THE RESTOCK (Counter Stock, 2026-08-26). Eleven more boxes on the shelf,
   * and the register above is all any of them get: one character, no art, no
   * second table. Two rules were followed picking them and both are cheap to
   * break by accident - NOTHING may be the plain parcel `▤` (that is the
   * fallback a sku the catalog grew first wears, so a glyph that IS the
   * fallback reads as "this row has no glyph" for ever), and no two rows may
   * wear the same character (the shelf is read at a glance and two identical
   * boxes read as one thing listed twice). */
  away_colors: '▥',
  sparkler_steps: '✧',
  brass_bell: '♩',
  emi_desk_toy: '❀',
  poster_drop_1: '◫',
  pa_pack: '◍',
  theme_drone: '▚',
  ghost_walk: '░',
  theme_snowday: '❄',
  emi_varsity: '✦',
  tube_midnight: '◗',
  /* THE WARDROBE (Locker wave, 2026-08-28). Three outfits standing beside the
   * varsity jacket, and the two rules above picked their characters the same
   * way: an alembic for the lab coat, a pennant for the cheer squad, still
   * water for the swim team. None of them is the parcel and none of them is
   * already on the shelf. */
  emi_labcoat: '⚗',
  emi_cheer: '⚑',
  emi_swim: '≋',
});

/* ----------------------------------------------------------------------------
 * THE SECOND REGISTER: THE SPRITES (art install, 2026-08-27).
 *
 * GLYPHS above is UNTOUCHED and stays that way - it is the fallback, and its
 * two rules (nothing may be the parcel `▤`, no two rows may wear the same
 * character) still hold for exactly the same reasons. This table is the layer
 * over it: sku -> a 192x192 pixel sprite, drawn one object on a clear field.
 *
 * THREE RULES OF ITS OWN, all cheap to break by accident:
 *  1. THE GLYPH IS ALWAYS PAINTED FIRST and the sprite covers it. An `onerror`
 *     takes the IMAGE off and leaves the glyph standing, so a missing png is a
 *     row that looks like it did last week, never a broken picture.
 *  2. NO SKU MAY BE INVENTED HERE. The catalog is the host's; a name in this
 *     table that the catalog does not carry paints nothing, and a sku the
 *     catalog grows before the artist does falls to its glyph.
 *  3. `image-rendering: pixelated` OR IT IS NOT PIXEL ART. prizecounter.css
 *     sizes the box at an exact integer divisor of 192 (48px, 32px on a phone)
 *     so the browser drops whole pixels rather than smearing them.
 * -------------------------------------------------------------------------- */
export const ART = Object.freeze({
  id_frame_gold: 'id_frame_gold.png',
  id_frame_navy: 'id_frame_navy.png',
  confetti_stamp: 'confetti_stamp.png',
  late_slip: 'late_slip.png',
  honors_lever: 'honors_lever.png',
  free_swim_key: 'free_swim_key.png',
  de_5x5: 'de_5x5.png',
  jukebox: 'jukebox.png',
  away_colors: 'away_colors.png',
  sparkler_steps: 'sparkler_steps.png',
  brass_bell: 'brass_bell.png',
  emi_desk_toy: 'emi_desk_toy.png',
  poster_drop_1: 'poster_drop_1.png',
  pa_pack: 'pa_pack.png',
  theme_drone: 'theme_drone.png',
  ghost_walk: 'ghost_walk.png',
  theme_snowday: 'theme_snowday.png',
  emi_varsity: 'emi_varsity.png',
  tube_midnight: 'tube_midnight.png',
  /* The wardrobe, drawn the way the varsity jacket is drawn: one garment on a
   * clear field at 192 square, so the shelf box and the tray beat both take it
   * at an exact divisor. Rule 1 above is the safety net while the artist is
   * still working - a png that is not there yet leaves the glyph standing. */
  emi_labcoat: 'emi_labcoat.png',
  emi_cheer: 'emi_cheer.png',
  emi_swim: 'emi_swim.png',
});

/** The token price glyph, straight out of the contract: ◉1 / ◉2 / ◉3. */
export const TOKEN_MARK = '◉';

/* ----------------------------------------------------------------------------
 * THE WARDROBE VERB (Locker wave, 2026-08-28).
 *
 * A cosmetic you have just bought is a cosmetic you are not wearing, and the
 * old counter's answer to that was a shrug: the row went gold, said "Yours",
 * and left you to walk to the Locker and find it. This table is the second half
 * of the purchase - the one press that puts the thing on where you are standing.
 *
 * IT IS A PROMISE, NOT AN AUTHORITY. A row here says "shell/locker.js can wear
 * this one"; the `equip` cap is still asked at the press, and a FALSE answer
 * takes the verb straight back off (the sku turned out not to be wearable, or
 * the Locker is not there at all). A sku with no row gets the plain toast it has
 * always got, which is the whole of what happens when the verb is not offered.
 *
 * TWO VERBS, because two different things happen. You PUT ON an outfit or a
 * frame, and you HANG UP a campus look. One word for both would be the school
 * talking like a form.
 *
 * THE DESK TOY HAS NO VERB ON PURPOSE. Buying it already switches the prop on
 * and the nightly rotation is already turning, so there is no single toy here
 * to put anywhere. WHICH one is pinned is a choice, and the Locker's desk group
 * is where that choice is made. A verb that quietly pinned the spinner would be
 * the toast deciding something the student walked to RM 004 to decide, so the
 * sku is left off the table and gets the plain toast. `equipFromToast` answers
 * it false from the other side too, which is the same ruling written twice.
 * -------------------------------------------------------------------------- */
export const EQUIP_VERBS = Object.freeze({
  emi_labcoat: ['booth_put_it_on', 'Put it on'],
  emi_cheer: ['booth_put_it_on', 'Put it on'],
  emi_swim: ['booth_put_it_on', 'Put it on'],
  emi_varsity: ['booth_put_it_on', 'Put it on'],
  id_frame_gold: ['booth_put_it_on', 'Put it on'],
  id_frame_navy: ['booth_put_it_on', 'Put it on'],
  theme_drone: ['booth_hang_it', 'Hang it up'],
  theme_snowday: ['booth_hang_it', 'Hang it up'],
});

/* ----------------------------------------------------------------------------
 * PLUMBING
 * -------------------------------------------------------------------------- */

/* ONE AUDIO DOOR (trap 18): shell/audio.js owns the only audio node on the
 * page, so every sound this room makes is a REQUEST on `document`. Only cue
 * names that already exist in the SOUNDS table are ever fired (trap 115) - an
 * invented name degrades to a blip, which is a worse sound than the one you
 * meant and a much worse bug than no sound at all. */
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

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* the DOM double may not carry attributes - never fatal */ }
}

function addCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.add === 'function') node.classList.add(cls); }
  catch (e) { /* noop */ }
}

function dropCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.remove === 'function') node.classList.remove(cls); }
  catch (e) { /* noop */ }
}

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/** Where a sku's sprite lives. Module-relative through `urlFor`, exactly like
 *  the sheet below and corkboard.js's `posterUrl` - a page-relative path breaks
 *  the moment the room is mounted from anywhere but the shell (campus.js:320).
 *  Answers null for a sku with no art, which is the glyph's cue. */
export function spriteUrl(sku) {
  const file = Object.prototype.hasOwnProperty.call(ART, sku) ? ART[sku] : null;
  if (!file) return null;
  return urlFor('../art/prizes/' + file, 'art/prizes/' + file);
}

/** The plate behind the tray beat (see `trayBeat` below). The counter's own
 *  half of the booth's painted set, so it lives with the counter. */
export function trayPlateUrl() {
  return urlFor('../art/vn/vn-20-prize-tray.png', 'art/vn/vn-20-prize-tray.png');
}

/**
 * TONIGHT'S HOT ROOM, IN PIECES, so two rooms can say it in one voice.
 *
 * The counter has always drawn this line and the booth's ticket tray wants to
 * read the same one out at the sill. Copying three keys across a second file is
 * how a school ends up with two payday sentences that disagree the week
 * somebody edits one of them, so the STRINGS live here, once, and each room
 * builds its own little box around them.
 *
 * @param {?Object} pd  the host's projection, `{gameKey, mult}` or null
 * @param {Function} t  the caller's lexicon lookup
 * @param {?Function} gameName  (key) -> that room's display name, optional
 * @returns {?{label:string, name:string, tail:string, mult:number}} null when
 *          there is no payday worth announcing (no projection, or a multiplier
 *          of one, which is not a payday, it is a Tuesday)
 */
export function paydayParts(pd, lookup, gameName) {
  if (!pd || !pd.gameKey) return null;
  /* Shadowed as `t` ON PURPOSE and not as `look`. The seam suite greps this
   * page for its literal lookup call sites and proves the host ships a row for
   * every word the school says (trap 123). A lookup renamed on its way into a
   * helper is a lookup that check cannot see, and a key nothing can see is a
   * key that reads fine in English and can never be translated or modded. */
  const t = typeof lookup === 'function' ? lookup : lexT;
  const mult = Number(pd.mult) || 0;
  if (mult <= 1) return null;
  let name = String(pd.gameKey);
  try { if (typeof gameName === 'function') name = gameName(pd.gameKey) || name; }
  catch (e) { /* the key is a fine last resort */ }
  return {
    label: t('prize_payday_label', 'Hot room tonight'),
    name: name,
    tail: mult >= 5
      ? t('prize_payday_5', 'is paying five times over')
      : t('prize_payday_2', 'is paying double'),
    mult: mult,
  };
}

/** prizecounter.css, linked once and lazily - recordsroom.js's pattern. */
function ensureSheet(doc, log) {
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById('pc-styles') : null;
    if (had) return had;
    const link = doc.createElement('link');
    link.id = 'pc-styles';
    link.rel = 'stylesheet';
    link.href = urlFor('./prizecounter.css', 'shell/prizecounter.css');
    const head = doc.head || doc.body || null;
    if (head && typeof head.appendChild === 'function') head.appendChild(link);
    return link;
  } catch (e) {
    if (typeof log === 'function') log('prize counter stylesheet failed to link');
    return null;
  }
}

function htmlReduced() {
  try {
    const de = (typeof document !== 'undefined') ? document.documentElement : null;
    if (de && de.classList && typeof de.classList.contains === 'function'
      && de.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/** Walk a subtree for the first node wearing `cls`, BY INDEX and never with a
 *  selector - `querySelector` does not exist in the node double, and a counter
 *  that can only find its own shelf in a browser is a counter with no suite. */
export function findCls(node, cls) {
  if (!node) return null;
  try {
    if (node.classList && typeof node.classList.contains === 'function'
      && node.classList.contains(cls)) return node;
  } catch (e) { /* noop */ }
  const kids = node.children;
  if (!kids) return null;
  for (let i = 0; i < kids.length; i += 1) {
    const hit = findCls(kids[i], cls);
    if (hit) return hit;
  }
  return null;
}

/** Every node under `node` wearing `cls`, in document order, by index. */
export function findAllCls(node, cls, out) {
  const acc = out || [];
  if (!node) return acc;
  try {
    if (node.classList && typeof node.classList.contains === 'function'
      && node.classList.contains(cls)) acc.push(node);
  } catch (e) { /* noop */ }
  const kids = node.children;
  if (!kids) return acc;
  for (let i = 0; i < kids.length; i += 1) findAllCls(kids[i], cls, acc);
  return acc;
}

/* ----------------------------------------------------------------------------
 * THE INVENTORY READER
 *
 * The host is allowed to write an inventory row three different ways over the
 * life of this feature - a bare count, a `{n, at}` bag, a bare `true` for an
 * unlock - and the room must read all three without ever asking which one it
 * got. A cosmetic you own once is `1`; a consumable you hold two of is `2`.
 * -------------------------------------------------------------------------- */

/** How many of `sku` the player is holding, 0 when none. */
export function heldCount(inv, sku) {
  if (!inv || typeof inv !== 'object') return 0;
  const row = inv[sku];
  if (row == null || row === false) return 0;
  if (row === true) return 1;
  if (typeof row === 'number') return Number.isFinite(row) && row > 0 ? Math.floor(row) : 0;
  if (typeof row === 'object') {
    const n = Number(row.n);
    if (Number.isFinite(n)) return n > 0 ? Math.floor(n) : 0;
    return 1;                      // a bag with no count is still a thing owned
  }
  return 0;
}

/* ----------------------------------------------------------------------------
 * THE ROOM
 * -------------------------------------------------------------------------- */

/**
 * createPrizeCounter(caps) -> the handle, or null with no DOM.
 *
 * @param {Object} caps
 *  mount     - where the counter's root goes. It was the shell's screen and it
 *              is the booth's overlay panel now; this file has never cared, and
 *              `embedded` is the only thing it has to be told about the change.
 *  embedded  - true when the counter is folded into somebody else's box (the
 *              booth's panel). It swaps the fixed full-page root for an in-flow
 *              one; the SCROLLER is then the box, not the page.
 *  beatMount - optional () -> the element the tray beat hangs off. The beat is
 *              a full-viewport picture, and inside an overlay panel (which is
 *              transformed, so it would be the containing block for a fixed
 *              child) it has to hang one layer out. Defaults to the root.
 *  equip     - optional (sku) -> boolean. The Locker's one press: "put it on",
 *              offered after a confirmed buy of a sku in EQUIP_VERBS. A false
 *              answer takes the verb off; see offerVerb below.
 *  t / log / lite / reduced
 *  catalog   - () -> [{sku, cur, cost, kind, nameKey, nameEn, blurbKey, blurbEn, locked}]
 *              exactly as the host projected it through init.economy. The page
 *              never invents a row and never prices one.
 *  balance   - () -> {t, k}
 *  inv       - () -> the wallet's `inv` bag (see heldCount above).
 *  unlocks   - () -> {extra, honors} (only read for the badge on honors_lever).
 *  stackMax  - optional (sku) -> how many of a consumable may be held. Purely a
 *              display cap for the "2 of 3" badge; the host is the one that
 *              refuses a third with reason "full".
 *  payday    - optional () -> {gameKey, mult} for tonight's hot room, or null.
 *  gameName  - optional (key) -> display name for that line.
 *  onBuy     - (sku) -> the shell's send of `prize-buy`. Fire and forget: the
 *              answer arrives at settle(), or it never arrives and nothing here
 *              has changed.
 *  onBack    - the way out.
 * @returns {?Object} the handle
 */
export function createPrizeCounter(caps) {
  const c = caps || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof c.t === 'function' ? c.t : lexT;
  const log = typeof c.log === 'function' ? c.log : function () {};
  const reduced = () => !!c.reduced || htmlReduced();

  ensureSheet(doc, log);

  let dead = false;
  /** The sku whose row is asleep waiting for an echo, or null. ONE at a time:
   *  a counter clerk serves one customer, and two in-flight buys is two ways to
   *  read the same balance. */
  let pending = null;
  let pendingTimer = null;
  /** The last line the counter said, kept as a test seam. */
  let note = '';
  /** THE TRAY BEAT's own two holds. One at a time, like the pending row: two
   *  overlapping beats would be two trays, and there is one tray. */
  let beatEl = null;
  let beatTimer = null;
  /** The "put it on" button in the note strip, or null. ONE at a time, and any
   *  new line the counter says takes it off (see say()). */
  let verbEl = null;
  /** The watch that takes the verb off when B's card puts its own on screen. */
  let verbWatch = null;

  /* The mirrors. Seeded from caps, and thereafter moved ONLY by settle(). */
  let wallet = readBalance();
  let inv = readInv();
  let unlocks = readUnlocks();

  const rows = new Map();          // sku -> the item element currently painted

  /* THE MOVES' OWN BOOKKEEPING, and every line of it is PRESENTATION. Not one
   * of these is a fact about the player, not one of them outlives a destroy,
   * and not one of them is ever read to decide what a purchase did. */
  const chipEls = { t: null, k: null, c: null };   // the wallet chips, re-seated each render
  const chipNums = { t: null, k: null, c: null };  // the <b> inside each, for the bank's ticking
  const holds = [];                       // live charge-hold handles, torn down each render
  let refreshTimer = null;                // a repaint waiting for a finger to lift
  let lastSig = '';                       // what the shelf on screen is painted FROM
  /** Purchases the host has confirmed this sitting. THE REPETITION RULE: the
   *  full party three times, and after that the room stays glad but stops
   *  throwing itself at you. */
  let buys = 0;
  /** The one row allowed to breathe (Law III), chosen fresh every render. */
  let breathSku = null;

  function readBalance() {
    try {
      const b = (typeof c.balance === 'function') ? c.balance() : null;
      const tt = Number(b && b.t); const kk = Number(b && b.k); const cc = Number(b && b.c);
      return {
        t: Number.isFinite(tt) ? tt : 0,
        k: Number.isFinite(kk) ? kk : 0,
        /* A SHELL THAT PREDATES THE WING sends no `c` at all, and a purse that
         * has never held a chip reads zero either way. Neither is an error and
         * neither draws a third shelf - the CATALOG decides that. */
        c: Number.isFinite(cc) ? cc : 0,
      };
    } catch (e) { return { t: 0, k: 0, c: 0 }; }
  }

  function readInv() {
    try {
      const v = (typeof c.inv === 'function') ? c.inv() : null;
      return (v && typeof v === 'object') ? v : {};
    } catch (e) { return {}; }
  }

  function readUnlocks() {
    try {
      const v = (typeof c.unlocks === 'function') ? c.unlocks() : null;
      return (v && typeof v === 'object') ? v : {};
    } catch (e) { return {}; }
  }

  function readCatalog() {
    try {
      const v = (typeof c.catalog === 'function') ? c.catalog() : null;
      return Array.isArray(v) ? v.filter((r) => r && r.sku) : [];
    } catch (e) { log('prize catalog read threw'); return []; }
  }

  /** One row out of the host's catalog by sku, or null. Read fresh: the
   *  catalog is the host's, this file keeps no copy of it. */
  function catalogRow(sku) {
    if (!sku) return null;
    try {
      const all = readCatalog();
      for (const r of all) if (r && r.sku === sku) return r;
    } catch (e) { /* noop */ }
    return null;
  }

  /**
   * The cheapest row the player could put on the counter right now, or null -
   * the one verb allowed to breathe (Law III).
   *
   * TICKETS BEFORE TOKENS, always, and not because a token row is dearer in
   * arithmetic. The two currencies are not comparable: one token is a good
   * night and sixty tickets is a good week, so a `1` on a token row is not
   * "cheaper" than `60` on a ticket row in any sense a player would recognise.
   * The shelf is the everyday surface, so the shelf is where the room beckons,
   * and the case only gets the breath when nothing on the shelf is within
   * reach at all. CHIPS ARE LAST for the same reason one step further out: a
   * chip is money that was won at a table rather than earned in a class, and a
   * shop that beckons you toward the thing you gambled for before the thing you
   * studied for is a shop with an opinion nobody asked it to have.
   */
  function cheapestAffordable(catalog) {
    let pick = null;
    for (const cur of SURFACES) {
      let best = Infinity;
      for (const item of (catalog || [])) {
        if (!item || !item.sku) continue;
        if (curOf(item) !== cur) continue;
        if (item.locked === true) continue;
        if (isOwned(item)) continue;
        const cost = Math.max(0, Math.round(Number(item.cost) || 0));
        const purse = Number(wallet[cur]) || 0;
        if (purse < cost) continue;
        if (item.kind === 'consumable') {
          const cap = stackMaxFor(item.sku);
          if (cap && heldCount(inv, item.sku) >= cap) continue;
        }
        if (cost < best) { best = cost; pick = item.sku; }
      }
      if (pick) return pick;
    }
    return pick;
  }

  function stackMaxFor(sku) {
    try {
      const n = Number((typeof c.stackMax === 'function') ? c.stackMax(sku) : NaN);
      return Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
    } catch (e) { return 0; }
  }

  /* --------------------------------------------------------------- the root */

  const root = el('div', 'pc-root');
  if (c.lite) addCls(root, 'is-lite');
  if (reduced()) addCls(root, 'is-reduced');
  /* FOLDED INTO SOMEBODY ELSE'S BOX. One class, and prizecounter.css takes the
   * root off `position:fixed` so the panel it is sitting in owns the scroll. */
  if (c.embedded) addCls(root, 'is-embedded');

  /** Where the tray beat hangs. The root when the counter owns the screen, and
   *  one layer out (the booth's scene root) when it is a panel: `.pc-beat` is
   *  `position:fixed`, and a transformed ancestor - which every overlay panel in
   *  this school is, mid-slide - would become its containing block and shrink a
   *  full-viewport picture to the size of the box that opened it. */
  function beatHost() {
    try {
      const h = (typeof c.beatMount === 'function') ? c.beatMount() : c.beatMount;
      if (h && typeof h.appendChild === 'function') return h;
    } catch (e) { /* the room is a fine floor */ }
    return root;
  }

  /* ------------------------------------------------------------ the chrome */

  /** The icon a currency wears: a drawn ticket stub, the token's mark, or the
   *  Back Room's drawn disc. DRAWN, not typed - a third unicode mark would be a
   *  third font risk on a page that may not fetch one (trap 2), and the stub
   *  already proved that a shape in the stylesheet reads better than a glyph. */
  function curIcon(cur) {
    if (cur === 'k') return el('i', 'arc-tok', TOKEN_MARK);
    if (cur === 'c') return el('i', 'arc-chipmark');
    return el('i', 'arc-tick');
  }

  /** What a currency is called, in the school's own words. */
  function curLabel(cur) {
    if (cur === 'k') return t('wallet_tokens', 'Tokens');
    if (cur === 'c') return t('wallet_chips', 'Chips');
    return t('wallet_tickets', 'Tickets');
  }

  /**
   * A currency chip. Tickets get a drawn stub, tokens get the mark, chips get
   * the disc, and all three carry the number FIRST because the number is the
   * thing a player came to read. `cur` is 't', 'k' or 'c'.
   */
  function chip(cur) {
    const box = el('span', 'pc-chip pc-chip-' + cur);
    const ico = curIcon(cur);
    attr(ico, 'aria-hidden', 'true');
    box.appendChild(ico);
    const num = el('b', 'pc-chip-n', String(Number(wallet[cur]) || 0));
    box.appendChild(num);
    box.appendChild(el('span', 'pc-chip-lbl', curLabel(cur)));
    /* THE BANK spends FROM here, so the chip and its number are kept by hand.
     * They are re-seated on every render and read through thunks, so a repaint
     * landing mid-flight retargets the ticking instead of orphaning it. */
    chipEls[cur] = box;
    chipNums[cur] = num;
    return box;
  }

  /** The price flag on a row. Tickets read as a plain count beside a stub,
   *  tokens read as the contract's ◉N, which is short enough to sit on glass,
   *  and chips read as a count beside the disc - a chip price is a four-figure
   *  number and a mark in front of it would only make it longer. */
  function priceTag(item) {
    const cur = curOf(item);
    const flag = el('span', 'pc-price pc-price-' + cur);
    const cost = Math.max(0, Math.round(Number(item.cost) || 0));
    if (cur === 'k') {
      flag.appendChild(el('b', 'pc-price-n', TOKEN_MARK + String(cost)));
    } else {
      const mark = curIcon(cur);
      attr(mark, 'aria-hidden', 'true');
      flag.appendChild(mark);
      flag.appendChild(el('b', 'pc-price-n', String(cost)));
    }
    return flag;
  }

  /* -------------------------------------------------------- the note strip */

  const noteStrip = el('p', 'pc-note');
  attr(noteStrip, 'aria-live', 'polite');

  function say(line) {
    /* A NEW LINE SPENDS THE OLD VERB. `textContent` would take the button off
     * anyway; this is what keeps the HANDLE honest, so nothing downstream is
     * holding a button that is no longer in the document. */
    clearVerb();
    note = String(line == null ? '' : line);
    try { noteStrip.textContent = note; } catch (e) { /* noop */ }
    if (note) addCls(noteStrip, 'is-on'); else dropCls(noteStrip, 'is-on');
  }

  /* ------------------------------------------------------------- THE VERB */

  /**
   * "Put it on", and it is the only button on this page that is not a purchase.
   *
   * IT HANGS OFF THE ECHO, exactly the way the tray beat does: the host has
   * confirmed the buy, the thing is yours, and this is the one press that puts
   * it on without a walk to the Locker. It sits in the note strip beside the
   * line the counter just said, because that line IS the receipt.
   *
   * A FALSE ANSWER TAKES IT OFF. `equip` is the Locker's own reader: it knows
   * what is wearable and what is not, and if it says no there was nothing to put
   * on and a button that says otherwise is a lie. Either answer spends the
   * press - a verb you can hit twice is a verb that looks broken the second time.
   */
  function clearVerb() {
    if (verbWatch) { try { verbWatch(); } catch (e) { /* noop */ } verbWatch = null; }
    if (!verbEl) return;
    try { verbEl.remove(); } catch (e) { /* noop */ }
    verbEl = null;
    /* THE STRIP GOES DARK WITH IT when there is no sentence under the button.
     * A receipt that is one word of furniture and no words at all is not a
     * receipt, it is a gap where the shelf used to be. */
    if (!note) dropCls(noteStrip, 'is-on');
  }

  function offerVerb(sku) {
    clearVerb();
    if (dead || !sku) return null;
    if (typeof c.equip !== 'function') return null;
    const row = Object.prototype.hasOwnProperty.call(EQUIP_VERBS, sku) ? EQUIP_VERBS[sku] : null;
    if (!row) return null;
    const btn = el('button', 'btn ghost pc-verb', t(row[0], row[1]));
    btn.type = 'button';
    try {
      if (typeof btn.addEventListener === 'function') {
        btn.addEventListener('click', function () {
          let ok = false;
          try { ok = !!c.equip(sku); }
          catch (e) { log('prize equip threw: ' + ((e && e.message) || e)); ok = false; }
          clearVerb();
          if (ok) sfx('chime', 0.3);
        });
      }
    } catch (e) { /* the DOM double carries no listeners - never fatal */ }
    verbEl = btn;
    try { noteStrip.appendChild(btn); } catch (e) { verbEl = null; return null; }
    /* The strip carries the verb even when it carries no sentence - which is
     * every successful buy now, because the receipt is the row turning gold. */
    addCls(noteStrip, 'is-on');
    /* ONE VERB PER SCREEN. B's purchase card opens a beat after this and it
     * carries its own "Put it on" - the same offer, twice, a centimetre apart,
     * which is a room asking whether you heard it the first time. The CARD
     * wins: it is the thing holding the thing. So the strip's copy stands down
     * the moment `html.arc-reveal-on` appears, and if no card ever opens (a
     * consumable, reduced motion, a kind the reveal declines) the strip's verb
     * is the only one there is and it stays. */
    verbWatch = onRevealOn(function () {
      if (verbEl === btn) clearVerb();
    }, 4000);
    return btn;
  }

  /* ------------------------------------------------------------- one shelf */

  /**
   * Is this row already the player's? An unlock or a cosmetic is owned once and
   * then it is furniture; a consumable is never "owned", it is HELD, and it can
   * be held again tomorrow.
   */
  function isOwned(item) {
    if (item.kind === 'consumable') return false;
    if (heldCount(inv, item.sku) > 0) return true;
    // The two lever unlocks live in `unlocks` as well as in `inv`, because the
    // lever reads them there. Either witness is proof.
    if (item.sku === 'honors_lever' && unlocks.honors === true) return true;
    if (item.sku === 'free_swim_key' && unlocks.freeSwim === true) return true;
    return false;
  }

  function itemNode(item) {
    const owned = isOwned(item);
    const held = item.kind === 'consumable' ? heldCount(inv, item.sku) : 0;
    const cap = item.kind === 'consumable' ? stackMaxFor(item.sku) : 0;
    const locked = item.locked === true;
    const cost = Math.max(0, Math.round(Number(item.cost) || 0));
    const purse = Number(wallet[curOf(item)]) || 0;
    const poor = !locked && !owned && purse < cost;
    const full = !!(cap && held >= cap);
    const short = cost - purse;
    /* THE ALMOST, and it is the oldest trick on any floor with a counter on it.
     * Twenty tickets short of the swim outfit is not the same feeling as two
     * thousand short, and until this line the row said it was: one flat
     * `is-poor` dim for both. The band is the book's - within forty, or within
     * a seventh on a dear row, whichever is kinder.
     *
     * LAW I IS UNTOUCHED and it is worth being exact about why. The price has
     * not moved. The wallet has not moved. The host will refuse this press for
     * the same reason it would have refused it before, and the row will take
     * the same no. All that changes is WHICH TRUE SENTENCE the row tells while
     * you look at it, and "you are twenty short" was always the truer one. */
    const almost = poor && short > 0 && short <= shortfallBand(cost);

    let cls = 'pc-item pc-item-' + curOf(item);
    if (owned) cls += ' is-owned';
    if (locked) cls += ' is-locked';
    if (poor) cls += ' is-poor';
    if (almost) cls += ' is-almost';
    if (full) cls += ' is-full';
    if (pending === item.sku) cls += ' is-busy';
    const box = el('article', cls);
    attr(box, 'data-sku', item.sku);

    /* The drawn box on the shelf. Decoration, no pointer events, and TWO
     * layers: the glyph is laid down first as the bed, and the sprite - when
     * there is one - is laid over it. An image that will not load takes ITSELF
     * off and the bed is what is left, so this row can never be a hole. */
    const art = el('div', 'pc-art');
    attr(art, 'aria-hidden', 'true');
    art.appendChild(el('span', 'pc-glyph', GLYPHS[item.sku] || '▤'));
    const spr = spriteUrl(item.sku);
    if (spr) {
      const img = el('img', 'pc-sprite');
      attr(img, 'alt', '');
      attr(img, 'draggable', 'false');
      attr(img, 'loading', 'lazy');
      attr(img, 'decoding', 'async');
      try {
        if (typeof img.addEventListener === 'function') {
          img.addEventListener('error', () => {
            try { img.remove(); } catch (e) { /* the bed is already under it */ }
          });
        }
      } catch (e) { /* the DOM double carries no listeners - never fatal */ }
      try { img.src = spr; } catch (e) { /* noop */ }
      art.appendChild(img);
    }
    box.appendChild(art);

    const body = el('div', 'pc-body');
    body.appendChild(el('h4', 'pc-name', t(item.nameKey || ('prize_name_' + item.sku),
      item.nameEn || item.sku)));
    const blurb = item.blurbEn || item.blurbKey
      ? t(item.blurbKey || ('prize_blurb_' + item.sku), item.blurbEn || '')
      : '';
    if (blurb) body.appendChild(el('p', 'pc-blurb', blurb));

    const foot = el('div', 'pc-foot');
    const flag = priceTag(item);
    if (almost) {
      /* The shortfall rides the price flag rather than sitting beside it: the
       * number you are short is a fact ABOUT the price, and a `-20` floating in
       * the row's furniture is a `-20` about nothing. Micro-label register
       * (`--pix`, 9px), so it reads as a stamp on the flag and never competes
       * with the price itself. The button carries the same fact in words for a
       * reader who is not looking at it. */
      const tag = el('span', 'pc-short', '-' + String(short));
      attr(tag, 'aria-hidden', 'true');
      flag.appendChild(tag);
    }
    foot.appendChild(flag);

    if (owned) {
      foot.appendChild(el('span', 'pc-badge pc-badge-owned', t('prize_owned', 'Yours')));
    } else if (locked) {
      foot.appendChild(el('span', 'pc-badge pc-badge-soon', t('prize_soon', 'Arriving soon')));
    } else {
      if (held > 0) {
        foot.appendChild(el('span', 'pc-badge pc-badge-held',
          t('prize_held', 'Holding') + ' ' + String(held) + (cap ? '/' + String(cap) : '')));
      }
      const waiting = pending === item.sku;
      const label = waiting
        ? t('prize_wait', 'Asking the counter')
        : (almost ? t('pc_verb_almost') : t('prize_buy', 'Trade'));
      const btn = el('button', 'btn primary pc-buy', label);
      btn.type = 'button';
      if (pending) attr(btn, 'disabled', 'disabled');
      if (almost && !waiting) {
        addCls(btn, 'is-almost');
        attr(btn, 'aria-label', label + ', ' + String(short) + ' ' + t('prize_short'));
      }
      /* THE ONE BREATH (Law III). Exactly one thing on this screen may breathe,
       * and it is the cheapest row the player can actually afford right now -
       * the verb that is ready to be pressed, never the dearest one that is
       * not. The sheet stops it while a celebration is up, because a room does
       * not beckon and cheer at the same time. */
      if (!pending && item.sku === breathSku) addCls(btn, 'is-breath');
      wireBuy(btn, item, cost, almost);
      foot.appendChild(btn);
    }

    body.appendChild(foot);
    box.appendChild(body);
    rows.set(item.sku, box);
    return box;
  }

  /**
   * THE PRESS, and there are two of them now.
   *
   * A cheap row keeps its click. A row over its currency's floor (see
   * `holdFloor` - a thousand tickets, or two tokens) gets THE CHARGE-HOLD: a ring fills over 700ms with the tone climbing under it, and a
   * release before it is full sends NOTHING - no message, no pending row, no
   * wallet touched, just the shiver and the row exactly as it was.
   *
   * NOTE WHAT IS NOT WIRED on the held road: there is no click listener on a
   * held row at all. One way in, one way to finish it, and no second path that
   * could disagree with the ring about whether the player meant it. Both roads
   * end at the same single line - `propose` - which is still the only thing in
   * this file that says a word to the host.
   *
   * If HOLD_TO_BUY is ever turned off, or the primitive declines to mount
   * (a DOM double, a browser without pointer events), the row silently falls
   * back to the click that shipped. The expensive road is allowed to lose its
   * ceremony; it is not allowed to lose its verb.
   */
  function wireBuy(btn, item, cost, almost) {
    const press = () => {
      /* THE ALMOST's ghost: the grade letter flickering to the better one, a
       * beat before the counter says no. It goes on the ROW, not the button,
       * because the row is what is about to be refused. */
      if (almost) ghostGold(rows.get(item.sku) || btn, { reduced: reduced(), lite: !!c.lite });
      propose(item);
    };
    if (HOLD_TO_BUY && cost >= holdFloor(curOf(item))) {
      let h = null;
      try {
        h = chargeHold(btn, {
          reduced: reduced(),
          log,
          fire: press,
          onShort() { say(t('prize_hold_hint')); },
        });
      } catch (e) { log('prize hold mount threw: ' + ((e && e.message) || e)); h = null; }
      if (h) {
        holds.push(h);
        if (!almost) attr(btn, 'aria-label', t('prize_hold_aria'));
        return;
      }
    }
    try {
      if (typeof btn.addEventListener === 'function') btn.addEventListener('click', press);
    } catch (e) { /* the DOM double carries no listeners - never fatal */ }
  }

  /** Every charge-hold on the shelf, off. Called before a render tears the
   *  rows down under them, and again on destroy. */
  function dropHolds() {
    while (holds.length) {
      const h = holds.pop();
      try { if (h && typeof h.destroy === 'function') h.destroy(); } catch (e) { /* noop */ }
    }
  }

  /**
   * WHAT THE PAINTED SHELF IS PAINTED FROM, as one string.
   *
   * Every pixel this room draws comes from four things: the purse, what is
   * held, what is unlocked, and which row is waiting. If none of them moved,
   * a repaint would draw the identical shelf - and destroy every one-shot mark
   * standing on the old nodes to do it (the shiver on a row that was just
   * refused, the thud on a row that was just bought, the charge under a finger).
   *
   * That is not a hypothetical either. The host answers `set-meta` with a
   * `meta` frame, the shell answers a `meta` frame with `refresh()`, and plenty
   * of things write meta for reasons that have nothing to do with the shelf.
   * A no-op refresh landing inside a 250ms shiver is a room that flinched and
   * then pretended it had not.
   */
  function shelfSig() {
    let s = wallet.t + '/' + wallet.k + '/' + wallet.c + '|' + (pending || '');
    try {
      const keys = Object.keys(inv || {}).sort();
      for (const k of keys) {
        const v = inv[k];
        s += ';' + k + ':' + ((v && Number(v.n)) || 0);
      }
    } catch (e) { s += ';?'; }
    try {
      const u = unlocks || {};
      const uk = Object.keys(u).sort();
      for (const k of uk) s += '|' + k + '=' + (u[k] ? 1 : 0);
    } catch (e) { s += '|?'; }
    return s;
  }

  /** Is a finger (or an Enter key) down on a charge right now? */
  function anyCharging() {
    for (const h of holds) {
      try { if (h && h.charging) return true; } catch (e) { /* noop */ }
    }
    return false;
  }

  /**
   * THE HELD REPAINT.
   *
   * A render tears every row down and builds new ones, which means it also
   * destroys the charge-hold the player currently has their finger on - quietly,
   * with no shiver and no bump, because a torn-down hold is not a short press.
   * The press then dies in the player's hand: the ring stops, the tone stops,
   * and releasing does nothing at all. That is the worst answer this room can
   * give, and it is not hypothetical - the host pushes a `meta` snapshot for
   * reasons that have nothing to do with the shelf (a payout, the seep writing
   * its state, another window moving the theme), the shell answers it with
   * `refresh`, and the odds of one landing inside a 700ms hold are not small.
   *
   * So a refresh that arrives mid-charge waits. It has already read the new
   * wallet into `wallet`; only the paint is late, by at most a few hundred
   * milliseconds, and it re-tries rather than being dropped. LAW I is untouched:
   * nothing here decides what anything costs or who owns what.
   */
  function refreshSoon(tries) {
    if (refreshTimer) { try { clearTimeout(refreshTimer); } catch (e) { /* noop */ } }
    refreshTimer = setTimeout(function () {
      refreshTimer = null;
      if (dead) return;
      if (anyCharging() && tries < 8) { refreshSoon(tries + 1); return; }
      render();
    }, 220);
  }

  /** What each shelf is called and what it says under its own name. */
  function surfaceWords(cur) {
    if (cur === 'k') {
      return [t('prize_case', 'Token Case'),
        t('prize_case_hint', 'Tokens only. Your first S of the day drops one in the tray.')];
    }
    if (cur === 'c') {
      return [t('prize_shelf_chips', 'The Back Room shelf'),
        t('prize_shelf_chips_hint',
          'Chips only. What you carried out of the Back Room buys these.')];
    }
    return [t('prize_shelf', 'Ticket Shelf'),
      t('prize_shelf_hint', 'Every graded class pays tickets. This is where they go.')];
  }

  function surface(cur, items) {
    const sect = el('section', 'pc-sect pc-sect-' + cur);
    const head = el('div', 'pc-sect-head');
    const words = surfaceWords(cur);
    head.appendChild(el('h3', 'pc-sect-title', words[0]));
    head.appendChild(el('p', 'pc-sect-hint', words[1]));
    sect.appendChild(head);

    const goods = el('div', 'pc-goods');
    for (const item of items) goods.appendChild(itemNode(item));
    sect.appendChild(goods);
    return sect;
  }

  /* ---------------------------------------------------------- the buy flow */

  /**
   * THE PROPOSAL. Everything this does is cosmetic and reversible: it names the
   * sku it is waiting on, repaints so the row reads asleep, and hands the sku
   * to the shell. It does not touch `wallet`, it does not touch `inv`, and if
   * the answer never comes the watchdog puts the room back exactly as it was.
   */
  function propose(item) {
    if (dead || pending) return;
    if (item.locked === true) { say(t('prize_locked_msg', 'That one stays in the case for now.')); sfx('bump', 0.32); return; }
    pending = item.sku;
    say(t('prize_wait', 'Asking the counter'));
    sfx('commit', 0.4);
    render();
    pendingTimer = setTimeout(() => {
      if (dead || pending !== item.sku) return;
      pending = null; pendingTimer = null;
      // No echo, so no purchase: the room wakes up owing exactly what it owed.
      say(t('prize_quiet', 'The counter went quiet on that one. Try again in a moment.'));
      render();
    }, ECHO_WAIT_MS);
    try { if (typeof c.onBuy === 'function') c.onBuy(item.sku); }
    catch (e) { log('prize buy send threw: ' + ((e && e.message) || e)); }
  }

  /**
   * THE ECHO. The host's `wallet-result` frame, and the ONLY thing in this file
   * allowed to move a balance or a badge.
   * @param {Object} res {ok, sku, reason, wallet:{t,k}, inv, unlocks}
   * @returns {boolean} true when the frame was for a row this room was waiting
   *          on (the shell does not need it, the suite does)
   */
  function settle(res) {
    if (dead) return false;
    const r = res || {};
    const was = pending;
    if (pendingTimer) { try { clearTimeout(pendingTimer); } catch (e) { /* noop */ } pendingTimer = null; }
    pending = null;

    /* THE TWO NUMBERS THE BANK TRAVELS BETWEEN, taken before the host's frame
     * lands on top of them. This is a snapshot of a PICTURE, not a ledger: a
     * line below, `wallet` becomes whatever the frame says it is, and the
     * bank's only use for this is knowing which digits to count down through on
     * the way there. If the two ever disagree the frame wins and the next
     * render says so. */
    const before = { t: wallet.t, k: wallet.k, c: wallet.c };

    /* The host's numbers win outright. A missing bag means "unchanged", never
     * "empty" - a refusal frame is allowed to carry only the reason. */
    if (r.wallet && typeof r.wallet === 'object') {
      const tt = Number(r.wallet.t); const kk = Number(r.wallet.k);
      const cc = Number(r.wallet.c);
      wallet = {
        t: Number.isFinite(tt) ? tt : wallet.t,
        k: Number.isFinite(kk) ? kk : wallet.k,
        c: Number.isFinite(cc) ? cc : wallet.c,
      };
    }
    if (r.inv && typeof r.inv === 'object') inv = r.inv;
    if (r.unlocks && typeof r.unlocks === 'object') unlocks = r.unlocks;

    let won = null;
    if (r.ok === true) {
      /* THE RECEIPT IS THE ROW. The counter used to say "Wrapped up and yours."
       * over a row that had just turned gold, which is one fact told twice
       * before EMI arrives to tell it a third time. The row's thud and its
       * `Yours` mark are the receipt now; the strip keeps nothing but the verb,
       * so the one SENTENCE on the screen belongs to her. */
      say('');
      winChime(r.sku || was, r.cost);
      won = String(r.sku || was || '');
    } else {
      const row = REFUSALS[String(r.reason || '')] || REFUSALS.unknown;
      say(t(row[0], row[1]));
      sfx('bump', 0.34);
    }
    render();
    /* THE HAND-OVER, AND IT HANGS OFF THE ECHO. Not off the press: a press only
     * asks, and a tray that slid out when the player pressed Buy would be the
     * room promising a thing the host has not agreed to hand over yet. Nothing
     * below this line can reach a refusal or a timeout. */
    if (won !== null) {
      buys += 1;
      offerVerb(won);
      spendBank(won, before);
    } else {
      /* THE NO, WITH SYMPATHY. A shiver on the row that was refused and, when
       * the wallet is what was wrong, a warm light on the number that was
       * short - so the eye goes to the fact rather than to the sentence. No
       * red, no buzzer, no second telling. */
      const missed = String(r.sku || was || '');
      const row = missed ? rows.get(missed) : null;
      const red = reduced();
      if (row) shiver(row, { reduced: red });
      if (String(r.reason || '') === 'poor') {
        const item = catalogRow(missed);
        warmGlow(chipEls[curOf(item)], { reduced: red });
      }
    }
    return !!was && (!r.sku || r.sku === was);
  }

  /* ------------------------------------------------------------- THE BANK */

  /**
   * THE BANK, REVERSED, and then the whole hand-over that hangs off it.
   *
   * The House Book's floor map has had a row for this surface since the day it
   * was written - "Shop / Vault purchase: reverse BANK -> THUD, cost visibly
   * leaves the wallet, item thuds into inventory" - and it has been the one row
   * on the map with nothing built under it. Until this function the entire
   * felt experience of spending a week of tickets was a number being a
   * different number in the next frame.
   *
   * THE ORDER OF THE THREE STROKES, which is the whole design decision here:
   *
   *   1. THE BANK      the money leaves the chip and flies to the row (~980ms)
   *   2. THE TRAY      the thing is in the tray at the sill        (1.0-1.6s)
   *   3. THE CARD      B's reveal, the thing in your hands          (its own)
   *
   * They do not overlap, and the reason they do not is the Brake: two
   * celebrations at once cancel each other, and a tray sliding out UNDER a card
   * whooshing up is two rooms talking over one another about the same purchase.
   * So this arms a HOLD (counterfx.js) for the length of strokes one and two,
   * and shell.js's `arcademy-bought` dispatch waits it out. One gesture, three
   * strokes, in the order the exchange actually happens: it leaves you, it
   * arrives at the sill, it is yours.
   *
   * NOTHING HERE IS LOAD-BEARING. The wallet, the inventory and the row all
   * moved at `settle()` a beat ago, on the host's word. If the bank cannot
   * measure (a node double, a shelf mid-repaint) it returns 0, nothing is
   * armed, and the card opens exactly as it does today - a purchase that
   * happened quietly is still a purchase.
   */
  function spendBank(sku, before) {
    const item = catalogRow(sku);
    const cur = curOf(item);
    const cost = Math.max(0, Math.round(Number(item && item.cost) || 0));
    const red = reduced();

    const ms = bankSpend({
      chipAt: () => chipEls[cur],
      numAt: () => chipNums[cur],
      landAt: () => findCls(rows.get(sku) || null, 'pc-art'),
      rowAt: () => rows.get(sku) || null,
      markAt: () => findCls(rows.get(sku) || null, 'pc-badge-owned'),
      cur, cost, log,
      from: before ? before[cur] : NaN,
      to: wallet[cur],
      reduced: red,
      lite: !!c.lite,
    });

    /* REDUCED MOTION TAKES THE STATE AND KEEPS THE SOUND. No flight, no
     * ticking - the chip already reads the new number - but the row still
     * declares itself changed for half a second and the thud still lands, so
     * the beat is intact and only the travel is gone. */
    if (!ms) {
      const row = rows.get(sku) || null;
      if (row) thud(row, { reduced: red });
      const mark = findCls(row, 'pc-badge-owned');
      if (mark) thud(mark, { reduced: red, mark: true });
      sfx('thud', 0.34);
      trayBeat(sku);
      armBoughtHold(BEAT_MS_STILL + HANDOVER_MS);
      return 0;
    }

    /* The tray comes out AS THE LAST COIN LANDS, not before it: the money is
     * gone, and that is the moment there is something to put in the tray. */
    if (beatTimer) { try { clearTimeout(beatTimer); } catch (e) { /* noop */ } beatTimer = null; }
    beatTimer = setTimeout(function () {
      beatTimer = null;
      if (dead) return;
      trayBeat(sku);
    }, ms);

    armBoughtHold(ms + beatHold(cost, cur, red) + HANDOVER_MS);
    return ms;
  }

  /**
   * THE CHIME, TIERED, because a poster and a wide board should not sound the
   * same. Tickets under the shelf's own threshold get the quieter ring; the
   * dear half of the shelf keeps the full one; and a token buy climbs to the
   * chime ladder's third rung, which is two semitones up - the pitch seam
   * multiplies the frequency and leaves the length alone, so it reads as
   * HIGHER rather than as faster (audio.js, one door, trap 18).
   */
  function winChime(sku, hintCost) {
    const item = catalogRow(sku);
    const cur = curOf(item);
    const cost = Math.max(0, Math.round(Number(
      (item && item.cost) != null ? item.cost : hintCost) || 0));
    if (cur === 'k') { sfx('chime', 0.55, { pitch: 1.1225 }); return; }
    /* A CHIP ROW IS NOT A TOKEN ROW. The third rung belongs to the day's one
     * token and would be a lie over money that was won at a table, so chips
     * take the shelf's own two-step ladder against their own waist. */
    sfx('chime', cost >= dearFloor(cur) ? 0.55 : 0.4);
  }

  /* -------------------------------------------------------- THE TRAY BEAT */

  /**
   * A second and a half at the sill: the booth's ticket tray, painted, with the
   * thing that was just bought sitting in it. It is the only picture of the
   * exchange this feature has, and it is worth the second and a half because
   * everything else about a purchase in this school is a number moving.
   *
   * WHAT IT IS NOT: a dialog, a confirmation, or a thing to press. It cannot be
   * dismissed because it cannot be answered, it takes no pointer and no focus,
   * and it takes itself off. A player who has already looked away has lost
   * nothing at all - the shelf underneath it has already been repainted with
   * the new balance by the time this is on screen.
   *
   * REDUCED MOTION GETS THE PICTURE, NOT LESS OF IT. The plate stands still for
   * 1.2s with no slide and no fade under `.is-still`; the sprite is not
   * animated in either cut. Motion is the thing being turned down, and the
   * picture was never the motion.
   *
   * ONE CUE, `paper`, out of the SOUNDS table (trap 115) and through the event
   * (trap 18). The chime for the purchase has already gone; this is the sound
   * of the tray coming out, and it is the quieter of the two on purpose.
   *
   * @param {string} sku the sku the host confirmed, for the sprite
   */
  /**
   * How long the plate stays at the sill, in the three cuts the audit asked
   * for. LAW IX, celebrate small ON PURPOSE: a sixty-ticket poster that gets
   * the same second and a half as a two-thousand-token board teaches a player
   * that the room's enthusiasm means nothing.
   *
   * AND THE REPETITION RULE ON TOP OF IT. The full party three times; from the
   * fourth purchase of a sitting the plate takes the compact cut whatever it
   * cost. A player buying out the shelf in one sitting is a player who has
   * stopped reading the ceremony, and a room that keeps throwing it is a room
   * that has stopped noticing them.
   */
  function beatHold(cost, cur, still) {
    if (still) return BEAT_MS_STILL;
    const dear = (cur === 'k') || (Number(cost) || 0) >= dearFloor(cur);
    if (!dear || buys > BEAT_FULL_RUNS) return BEAT_MS_SMALL;
    return BEAT_MS_FULL;
  }

  function trayBeat(sku) {
    if (dead) return null;
    clearBeat();
    const still = reduced();
    const item = catalogRow(sku);
    const cur = curOf(item);
    const cost = Math.max(0, Math.round(Number(item && item.cost) || 0));
    /* `is-lite` rides the BEAT rather than the root, because the beat does not
     * always hang off the root any more (see beatHost). The old
     * `.pc-root.is-lite .pc-beat-*` rules are still in the sheet and still
     * correct for a counter that owns its screen; this is the same two savings
     * reached through the node itself. */
    const hold = beatHold(cost, cur, still);
    const wrap = el('div', 'pc-beat' + (still ? ' is-still' : '') + (c.lite ? ' is-lite' : '')
      + (hold <= BEAT_MS_SMALL && !still ? ' is-compact' : ''));
    attr(wrap, 'aria-hidden', 'true');

    const plate = el('img', 'pc-beat-plate');
    try { plate.src = trayPlateUrl(); plate.alt = ''; } catch (e) { /* noop */ }
    /* A plate that will not load takes ITSELF off and leaves the sprite over a
     * dark card, the way a sku with no art falls back to its glyph. */
    try {
      plate.addEventListener('error', function () {
        try { plate.remove(); } catch (e2) { /* noop */ }
        addCls(wrap, 'is-bare');
      });
    } catch (e) { /* noop */ }
    wrap.appendChild(plate);

    /* Centred ON THE TRAY rather than on the card: the tray's mouth sits a
     * little below the middle of a 1:1 plate, and a sprite in the geometric
     * centre of the picture is a sprite floating above the tray. */
    const seat = el('div', 'pc-beat-seat');
    const url = spriteUrl(sku);
    if (url) {
      const sp = el('img', 'pc-beat-sprite');
      try { sp.src = url; sp.alt = ''; } catch (e) { /* noop */ }
      try {
        sp.addEventListener('error', function () {
          try { sp.remove(); } catch (e2) { /* noop */ }
          seat.appendChild(el('span', 'pc-beat-glyph', GLYPHS[sku] || '▤'));
        });
      } catch (e) { /* noop */ }
      seat.appendChild(sp);
    } else {
      seat.appendChild(el('span', 'pc-beat-glyph', GLYPHS[sku] || '▤'));
    }
    wrap.appendChild(seat);

    beatEl = wrap;
    /* THE ROOM HOLDS ITS BREATH while the tray is out. The mark goes on the
     * root rather than being inferred from the beat's own presence, because the
     * beat does not always hang inside the root any more (see beatHost) and a
     * sheet cannot reach across that. */
    addCls(root, 'is-beating');
    try { beatHost().appendChild(wrap); } catch (e) { /* noop */ }
    sfx('paper', 0.3);
    /* THE GARNISH, AND ONLY ON THE CASE. A token is the first S of a day; the
     * shelf's own rows do not get sparks, so that when they do appear they
     * still mean something. Never on lite, never on a still cut - the burst is
     * the one move in this room that is decoration and nothing else. */
    if (cur === 'k') sparkBurst(seat, { reduced: still, lite: !!c.lite });
    beatTimer = setTimeout(function () {
      beatTimer = null;
      clearBeat();
    }, hold);
    return wrap;
  }

  /** Take the beat off, from anywhere, twice if you like. */
  function clearBeat() {
    if (beatTimer) { try { clearTimeout(beatTimer); } catch (e) { /* noop */ } beatTimer = null; }
    if (beatEl) { try { beatEl.remove(); } catch (e) { /* noop */ } beatEl = null; }
    dropCls(root, 'is-beating');
  }

  /* ------------------------------------------------------------ the render */

  function render() {
    if (dead) return root;
    rows.clear();
    /* THE HOLDS COME OFF FIRST. Their buttons are about to be thrown away, and
     * a charge-hold still holding a timer for a node that has left the document
     * is a ring that fills in the dark and fires into nothing. */
    dropHolds();
    for (const cur of SURFACES) { chipEls[cur] = null; chipNums[cur] = null; }
    try { root.textContent = ''; } catch (e) { /* noop */ }

    /* THE SHELF IS READ FIRST NOW. Both of the room's new judgements - how hot
     * the marquee burns and which single verb is allowed to breathe - are facts
     * about the catalog measured against the purse, so the catalog has to be in
     * hand before the sign goes up. Reading it is pure (`readCatalog` asks the
     * cap and filters), so nothing has moved by asking early. */
    const catalog = readCatalog();
    breathSku = cheapestAffordable(catalog);

    /* THE MARQUEE. The room's own sign, and the wallet reading under it - a
     * shop tells you what you can spend before it tells you what it sells.
     *
     * AND IT IS A MARQUEE NOW. The bulb ring is the school's own kit out of
     * styles.css, unchanged: `.arc-sign-lamps` carries the mask and the dim
     * fill, its child `.arc-sign-chase` carries the oversized gradient and
     * travels exactly one period, so the bulbs stand still and the LIGHT moves.
     * What is local is the HEAT - how bright the ring burns is how far up this
     * shelf the player's money currently reaches, so a fat night walks into a
     * room that is visibly lit for them. No mount at all under `lite`; the
     * chase stops of its own accord under reduced motion (styles.css). */
    const marquee = el('div', 'pc-marquee');
    if (!c.lite) {
      const lamps = el('i', 'arc-sign-lamps');
      attr(lamps, 'aria-hidden', 'true');
      lamps.appendChild(el('i', 'arc-sign-chase'));
      marquee.appendChild(lamps);
    }
    try {
      marquee.style.setProperty('--pc-heat', String(marqueeHeat(catalog, wallet).toFixed(3)));
    } catch (e) { /* the sheet's own default is a fine dim ring */ }
    marquee.appendChild(el('h2', 'pc-title', t('prize_counter_title', 'Prize Counter')));
    marquee.appendChild(el('p', 'pc-sub', t('prize_counter_sub',
      'Tickets on the shelf, tokens in the case')));
    const purse = el('div', 'pc-wallet');
    const label = el('span', 'pc-wallet-lbl', t('prize_you_have', 'On you'));
    purse.appendChild(label);
    purse.appendChild(chip('t'));
    purse.appendChild(chip('k'));
    /* THE THIRD CHIP IS ALWAYS THERE, even at zero, and that is deliberate. A
     * purse that hides an empty currency teaches a player that the currency
     * does not exist; a purse that shows a zero teaches them there is somewhere
     * they have not been yet. The two above it have always read zero on a first
     * night for exactly the same reason. */
    purse.appendChild(chip('c'));
    marquee.appendChild(purse);
    root.appendChild(marquee);

    /* TONIGHT'S HOT ROOM. Projected by the host, seeded per UTC day, and shown
     * because a payday nobody knows about is a payday nobody plays. */
    const pd = paydayLine();
    if (pd) root.appendChild(pd);

    root.appendChild(noteStrip);

    if (!catalog.length) {
      root.appendChild(el('p', 'arc-note pc-bare', t('prize_empty',
        'Shelf is bare tonight. Come back when the truck has been.')));
    } else {
      for (const cur of SURFACES) {
        const items = catalog.filter((r) => curOf(r) === cur);
        if (items.length) root.appendChild(surface(cur, items));
      }
    }

    /* THE WAY OUT, and it is sticky (trap 46): the catalog is a scroller and an
     * exit that scrolls away is not an exit. */
    const back = el('button', 'btn primary', t('back', 'Back'));
    back.type = 'button';
    back.addEventListener('click', () => {
      try { if (typeof c.onBack === 'function') c.onBack(); }
      catch (e) { log('prize back threw: ' + ((e && e.message) || e)); }
    });
    signExit(back, { dir: 'back' });
    root.appendChild(exitBar([back]));
    /* The render empties `root`, and settle() renders BEFORE it fires the beat.
     * A refresh landing mid-beat (a payout, say) would otherwise sweep the tray
     * off half a second in, so the beat is re-seated last and stays its second
     * and a half however many times the shelf is repainted under it. */
    if (beatEl) { try { beatHost().appendChild(beatEl); } catch (e) { /* noop */ } }
    lastSig = shelfSig();
    return root;
  }

  function paydayLine() {
    let pd = null;
    try { pd = (typeof c.payday === 'function') ? c.payday() : null; } catch (e) { pd = null; }
    const parts = paydayParts(pd, t, c.gameName);
    if (!parts) return null;
    const line = el('div', 'pc-payday');
    line.appendChild(el('span', 'pc-payday-lbl', parts.label));
    line.appendChild(el('b', 'pc-payday-name', parts.name));
    line.appendChild(el('span', 'pc-payday-mult', parts.tail));
    return line;
  }

  /* ------------------------------------------------------------- the mount */

  render();
  try {
    if (c.mount && typeof c.mount.appendChild === 'function') c.mount.appendChild(root);
  } catch (e) { log('prize counter failed to mount'); }

  function destroy() {
    if (dead) return;
    dead = true;
    if (pendingTimer) { try { clearTimeout(pendingTimer); } catch (e) { /* noop */ } pendingTimer = null; }
    pending = null;
    dropHolds();
    if (refreshTimer) { try { clearTimeout(refreshTimer); } catch (e) { /* noop */ } refreshTimer = null; }
    clearBeat();
    clearVerb();
    rows.clear();
    try { root.remove(); } catch (e) { /* noop */ }
  }

  return {
    root,
    render,
    settle,
    /** Re-read every cap and repaint. The shell calls this when the wallet moved
     *  for a reason that was not a purchase (a payout, say). */
    refresh() {
      wallet = readBalance();
      inv = readInv();
      unlocks = readUnlocks();
      /* NOTHING MOVED, SO NOTHING IS REDRAWN. The cheapest and by far the most
       * common case, and the one that used to sweep a shiver or a thud off the
       * screen a few milliseconds after it landed. */
      if (shelfSig() === lastSig) return;
      // A finger is down on a charge: repaint the moment it lifts, not now.
      if (anyCharging()) { refreshSoon(0); return; }
      render();
    },
    /** The sku this room is waiting on, or null (test seam). */
    get pending() { return pending; },
    /** The last line the counter said (test seam). */
    get note() { return note; },
    /** What the room believes it can spend (test seam). Never authored here. */
    get balance() { return { t: wallet.t, k: wallet.k, c: wallet.c }; },
    /** One painted row by sku, or null (test seam). */
    rowFor(sku) { return rows.get(sku) || null; },
    /** The tray beat currently on screen, or null (test seam). */
    get beat() { return beatEl; },
    /** The "put it on" button in the note strip, or null (test seam). */
    get verb() { return verbEl; },
    destroy,
  };
}

export default createPrizeCounter;
