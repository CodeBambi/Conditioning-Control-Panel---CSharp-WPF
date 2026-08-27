/* ============================================================================
 * emi/widget.js - THE FLOATING ELEMENT (agent B).
 *
 * EMI is a free-floating widget inside the Arcademy window: the user drags her
 * anywhere, pets her, and can dismiss her into a little semi-transparent dock
 * button on the bottom-right edge. Position and the dismissed flag persist.
 *
 * This module owns the DOM, the pointer verbs and the persistence. The LOOK is
 * agent A's (`emi.css`), the FACE is agent A's (`face.js` / `chains.js` /
 * `fx.js`) and it is INJECTED - this file never imports them, so a broken
 * renderer degrades EMI to a body PNG rather than taking the shell's boot with
 * it (the same "optional module" discipline `shell.js loadOptional` uses).
 *
 * THREE LAWS THIS FILE EXISTS TO KEEP
 * 1. INPUT TRUST (CLAUDE.md §top): the layer is `pointer-events:none` and only
 *    `.emi` and `.emi-dock` re-enable it. Nothing here calls `preventDefault`
 *    on anything outside `.emi`, so a board click over EMI's hidden footprint
 *    still lands on the board.
 * 2. ESC IS NOT OURS. EMI adds no rung to the Esc ladder and binds no key
 *    listener at all (trap 29 / shell.js escapeStep own that ladder).
 * 3. A SAY IS NEVER CUT MID-LINE. Petting and dragging queue nothing and cancel
 *    nothing while a speech bubble is typing; they are ignored (a reaction that
 *    eats a sentence reads as a bug, not as a mascot).
 * ==========================================================================*/

import { isMobile, onDeviceChange } from '../core/device.js';

/* ---------------------- dials (designer-tunable) -------------------------
 * EVERY tunable EMI has is in this one frozen object, so the owner can retune
 * her feel by hand without reading the code around it. One line each. */
export const DIALS = Object.freeze({
  /* --- size ------------------------------------------------------------ */
  W_DEFAULT: 150,          // px wide on a roomy window (>= W_NARROW_VW); aspect comes from the PNG
  W_NARROW: 116,           // ...and this much on a narrow one, so she does not own the board
  W_NARROW_VW: 900,        // px of viewport width below which W_NARROW is the default
  W_MIN: 110,              // clamp floor for a stored/explicit width
  W_MAX: 220,              // clamp ceiling for the same
  /* THE PHONE CEILING (the mobile pass). On a 390px-wide screen a 150px EMI owns
   * more than a third of the glass and stands right on top of a game's board, so
   * the phone gets its own default and its own hard ceiling - roughly 57% of the
   * desktop default, which is still four times the 44px touch floor, so she is
   * every bit as easy to grab as she was. It is a DISPLAY clamp and never a
   * stored one: `userWidth` keeps whatever the player chose on their desktop and
   * that is what persists, so a phone session can never shrink the size they set
   * on the big screen (see effectiveWidth / save). */
  W_MOBILE: 86,            // px wide on a phone, whatever the window says
  W_MOBILE_MAX: 96,        // ...and the ceiling a stored desktop width is clamped to there
  MARGIN_MOBILE: 8,        // px kept between EMI and the edge on a phone (see MARGIN)
  ASPECT_W: 859,           // mascot-body-blank.png, natural size
  ASPECT_H: 869,
  MARGIN: 4,               // px kept between EMI and the viewport edge

  /* --- pointer verbs --------------------------------------------------- */
  DRAG_PX: 6,              // move this far from the press point and it is a DRAG, not a pet
  DRAG_MS: 250,            // ...or hold the button this long while moving at all
  HOLD_MS: 450,            // press-and-hold this long arms the x affordance (touch has no hover)
  FLING_SPEED: 1.5,        // px/ms; above this the drag counts as FAST
  FLING_MS: 120,           // ...sustained this long = >.< in the air and a THUD landing
  SETTLE_MS: 600,          // ^_^ held this long after a release, then back to idle

  /* --- petting --------------------------------------------------------- */
  PET_WINDOW_MS: 4000,     // PET_TARGET pets inside this window buys the glee cycle
  PET_TARGET: 3,           // ...how many that is
  PET_COOLDOWN_MS: 6000,   // ...after which she only winks, so spam cannot loop the show

  /* --- idling ---------------------------------------------------------- */
  RAW_HOLD_MS: 1400,       // a raw face string (no chain) is held this long
  BLINK_EVERY_MS: 5200,    // resting blink cadence
  BLINK_HOLD_MS: 110,      // ...and how long the eyes stay shut

  /* --- perception (the wave of 2026-08-24) ------------------------------ */
  GAZE_MAX_PX: 3,          // the face may lean at most this many px toward the cursor
  GAZE_DIV: 60,            // px of cursor distance per px of lean (bigger = subtler)
  GAZE_EASE: 0.15,         // per-frame easing toward the target (0..1)
  APPROACH_PX: 120,        // cursor within this many px of her EDGE = she notices
  APPROACH_COOLDOWN_MS: 30000, // one noticing per this window, so she is not a doorbell
  GLANCE_SPEED: 1.2,       // px/ms; entering the radius faster than this earns the glance chain
  LINGER_MS: 2000,         // hover this long without clicking = expectant
  LINGER_AWAY_MS: 4000,    // ...and this much longer with no pet = the look-away
  DANGLE_MAX_DEG: 6,       // carried tilt is clamped here (physics reads as care, not slapstick)
  DANGLE_K: 4,             // deg of tilt per px/ms of horizontal drag speed
  DANGLE_SETTLE_MS: 260,   // the spring back to upright on release

  /* --- the idle sway (five body frames; antenna + hand tips only) ------- */
  SWAY_STEP_MS: 200,       // one step of the ping-pong walk
  SWAY_CENTER_MIN_MS: 600, // ...and the pause when it passes back through centre
  SWAY_CENTER_MAX_MS: 900, // (a range, so two idle beats never land in lockstep)

  /* --- the speech bubble ------------------------------------------------ */
  BUBBLE_SHIFT_X: 15,      // px further off her ear (owner, 2026-08-24). Mirrored
                           // by `.bubble-left` and paid for in faceBubble's reach.
  /* HOW LONG A LANDED LINE HANGS. Every one of these three was multiplied by
   * 1.35 on 2026-08-25 (owner: "increase the persistence time of the bubble in
   * general, +35%") - floor 3000 -> 4050, base 1400 -> 1890, per-character
   * 45 -> 61. They are scaled HERE, at the source, because `sayHoldMs` is the
   * one function that turns a line into a hold and every call site in the page
   * goes through it; scaling at a call site would have moved some lines and
   * not others. There is deliberately no ceiling: the growth is linear in the
   * line and her longest bark is under 120 characters, so a cap would only
   * ever fire on a line nobody has written yet. */
  SAY_HOLD_MIN_MS: 4050,   // a landed line NEVER holds less than this...
  SAY_HOLD_BASE_MS: 1890,  // ...and a long one grows: BASE + PER_CHAR x length
  SAY_HOLD_PER_CHAR_MS: 61,
  /* AN ASK IS NOT AN ORDINARY LINE AND HAS NO AUTO-HIDE AT ALL (owner,
   * 2026-08-25: "keep the bubble in sight till we respond"). The question is
   * handed this as its hold, and the only thing that ever takes it down is the
   * strip's own end - an answer, a dismiss, or a screen that kills her. It is
   * a large FINITE number and not Infinity because `chains.js makeSay` does
   * `holdMs | 0`, which turns Infinity into 0 and the whole question into a
   * 400ms flash. One hour is longer than any sitting and still inside int32.
   * The timer it arms is owned: `releaseAskLine()` cancels it. */
  ASK_HOLD_MS: 3600000,
  /* A LINE THAT ARRIVED WHILE SHE WAS WAITING gets ONE slot and this long to
   * be worth saying. Past it the moment has gone and the line is dropped -
   * the same trade `index.js`'s VOICE_PENDING_MS makes for the opening beat. */
  ASK_QUEUE_MS: 8000,
  ASK_QUEUE_POLL_MS: 250,  // ...and how often the released line re-tries a busy glass

  /* --- the field trip (wave W2a) --------------------------------------- */
  CRT_MS: 200,             // ONE power-off: squish to a line (~140) + flash dot (~60).
                           // MIRRORS widget.css `emi-crt-off`/`emi-crt-on`, the way
                           // BODY_MS mirrors emi.css. Change both or she moves in the light.
  TRIP_GAP_PX: 14,         // px she stands off the fixture she came to look at
  TRIP_BEAT_MS: 320,       // the pause after the line, before the tube goes dark again

  /* --- persistence ----------------------------------------------------- */
  SAVE_DEBOUNCE_MS: 600,   // one write per interaction, never per pointermove
});

/** The one-shot line the FIRST dismiss ever earns. Shown through the shell's toast. */
export const HINT_LINE = 'EMI is in the corner.';

const IDLE_FACE = '0_0';
const BLINK_FACE = '-_-';
const DRAG_FACE = '@_@';
const FLING_FACE = '>.<';
const SETTLE_FACE = '^_^';
const BODY_CLASSES = ['breath', 'nod', 'shiver', 'bounce', 'thud', 'droop'];
/* How long each body move runs before EMI settles back into BREATH. Agent A's
 * keyframe lengths, rounded up: a move that never returns leaves `droop`'s
 * `forwards` fill (or a dead `bounce`) welded onto the root. */
const BODY_MS = { bounce: 800, thud: 800, shiver: 900, nod: 1900, droop: 800, shake: 420 };
/* SHAKE is the exit flinch's move and it is deliberately NOT a new keyframe:
 * emi.css owns the keyframes and this file owns how long a move runs, so a
 * shake is SHIVER cut short. It inherits the reduced-motion refusal with it. */
const BODY_ALIAS = { shake: 'shiver' };
/* ============================================================================
 * THE BODY FRAMES
 * ==========================================================================*/
/* EMI's body used to be ONE png - the arms-up ceremony pose - and she wore it
 * at rest, which is why she always looked like she had just won something. The
 * locked pose set gives her six, and `body.png` keeps its name so nothing else
 * in the bundle moves: it is now CEREMONY ONLY. Every frame is the same
 * 859x869 canvas with the same screen rect, so the face canvas lands in exactly
 * the same place whichever one is up. */
export const BODY_FRAME_SRC = Object.freeze({
  celebration: './art/emi/body.png',      // arms up: stamps, wins, reveals, LV UP
  idle: './art/emi/body-idle.png',        // arms down: THE RESTING POSE + sway centre
  sad: './art/emi/body-sad.png',          // cry, K.O., a broken streak, the exit flinch
  shock: './art/emi/body-shock.png',      // shock, wake, rage, glitch, a rare drop
  smug: './art/emi/body-smug.png',        // smug, suspicious, the dork-canon lines
  pet: './art/emi/body-pet.png',          // pets, love, the pet streak
  // ...and the four off-centre steps of the idle sway. Only her antenna and her
  // hand tips differ from `idle`; nothing else in the frame moves.
  sway1: './art/emi/body-sway1.png',
  sway2: './art/emi/body-sway2.png',
  sway3: './art/emi/body-sway3.png',
  sway4: './art/emi/body-sway4.png',
});

/* ============================================================================
 * THE VARSITY JACKET (Counter Stock prize `emi_varsity`)
 * ==========================================================================*/
/* A prize RE-DRESSES her, and re-dressing is all ten frames or none. The jacket
 * set is the same ten filenames one folder down, so the art is authored by
 * copying the pose sheet and painting a coat onto it - a second map typed out
 * by hand would drift the day an eleventh pose is added.
 *
 * A MIXED SET IS THE FAILURE THIS EXISTS TO PREVENT. Her idle sway walks five
 * frames 200ms apart, so a jacket missing `body-sway3.png` would flick the coat
 * off her back twice a second, which reads as a broken sprite and not as a
 * cosmetic. The swap is therefore armed by ONE probe (`armVarsity` below) and
 * the whole map moves at once or nothing does - and a probe that fails is
 * permanent for the sitting, because art is allowed to lag code. */
/* AND IT IS NOT THE ONLY COAT ANY MORE. The install of 2026-08-27 brought
 * three more ten-pose sheets in - labcoat, cheer, swim - authored the same way
 * and living in the same shape one folder down. So the jacket's lookup is
 * GENERALISED rather than copied: one directory rule, one url rule, one map
 * built by walking BODY_FRAME_SRC, and `varsitySrc`/`VARSITY_FRAME_SRC` kept
 * as the names the rest of the bundle already imports.
 *
 * ONLY VARSITY IS A PURCHASE. `emi_varsity` is an sku in the catalog and the
 * prize bag turns it on; labcoat, cheer and swim are NOT skus, carry no price
 * and unlock nothing - they are art that is simply present, armed by the same
 * neutral `setOutfit()` seam. Nothing in this file may invent a price, an sku
 * or an unlock for them.
 *
 * TODO (owner decision, unmade): there is no surface in the school for CHOOSING
 * an outfit. Until there is, the three unpriced sheets reach a player only
 * through `setOutfit()`, exactly as varsity reaches one through the bag. Where
 * the picker lives - the Records Office wardrobe, a settings row, a Prize
 * Counter tab - is a design call, not a call this file gets to make. */
/** The four sheets that exist as art. `varsity` is the only purchasable one. */
export const OUTFITS = Object.freeze(['varsity', 'labcoat', 'cheer', 'swim']);
/** Where a sheet lives. One folder down, same ten filenames. PURE. */
export function outfitDir(name) { return './art/emi/' + String(name) + '/'; }
/** `./art/emi/body-idle.png` -> `./art/emi/<outfit>/body-idle.png`. PURE. */
export function outfitSrc(name, std) {
  const s = typeof std === 'string' ? std : '';
  const i = s.lastIndexOf('/');
  return outfitDir(name) + (i < 0 ? s : s.slice(i + 1));
}
/** outfit name -> its frozen ten-frame map. Built, never typed out by hand. */
export const OUTFIT_FRAME_SRC = Object.freeze((() => {
  const all = {};
  for (const name of OUTFITS) {
    const out = {};
    for (const k of Object.keys(BODY_FRAME_SRC)) out[k] = outfitSrc(name, BODY_FRAME_SRC[k]);
    all[name] = Object.freeze(out);
  }
  return all;
})());
export const VARSITY_DIR = outfitDir('varsity');
/** `./art/emi/body-idle.png` -> `./art/emi/varsity/body-idle.png`. PURE. */
export function varsitySrc(std) { return outfitSrc('varsity', std); }
export const VARSITY_FRAME_SRC = OUTFIT_FRAME_SRC.varsity;
/** The map a set name resolves to. Junk answers the standard set, never throws. */
export function frameSetFor(name) {
  return Object.prototype.hasOwnProperty.call(OUTFIT_FRAME_SRC, name)
    ? OUTFIT_FRAME_SRC[name] : BODY_FRAME_SRC;
}

/* THE POSE WALK. Out to one side and back through the centre, then out to the
 * other - `sway1` and `sway4` are the two extremes. The centre is HELD (see
 * DIALS.SWAY_CENTER_*); every other step is one SWAY_STEP_MS. */
const SWAY_CYCLE = ['idle', 'sway2', 'sway1', 'sway2', 'idle', 'sway3', 'sway4', 'sway3'];

/* THE DEFAULT MAP: a face family -> a pose, for everything that is NOT a chain
 * with its own `bodyFrame` (raw holds, and every line `makeSay` builds - which
 * is resolved frame by frame, so the typing dots stay at `idle` and the pose
 * lands with the reaction face). Anything absent here is `idle`, deliberately:
 * a face nobody paired is a face she has no strong feeling about. */
export const FACE_BODY_FRAME = Object.freeze({
  // celebration
  '^_^': 'celebration', '^___^': 'celebration', '^_~': 'celebration',
  '\\o/': 'celebration', 'GG': 'celebration', 'LV UP': 'celebration',
  '★★★': 'celebration', ':D': 'celebration', 'XD': 'celebration',
  // sad
  ';_;': 'sad', 'T_T': 'sad', 'x_x': 'sad', '(ಥ_ಥ)': 'sad',
  '(✖╭╮✖)': 'sad', ":'(": 'sad',
  // shock
  'o_o': 'shock', '(◉_◉)': 'shock', '(⊙_⊙)': 'shock',
  '>_<': 'shock', '>.<': 'shock', '!!!': 'shock', '???': 'shock',
  '#ERR': 'shock', '404': 'shock', ':O': 'shock',
  // smug
  '¬_¬': 'smug', '(¬‿¬)': 'smug', '(ಠ‿ಠ)': 'smug',
  '(⌐■_■)': 'smug', '( ͡° ͜ʖ ͡°)': 'smug',
  '(◔_◔)': 'smug', 'B)': 'smug', '>:)': 'smug',
  // pet
  '(｡♥‿♥｡)': 'pet', '(✿◡‿◡)': 'pet',
  '(≧◡≦)': 'pet', '(◠‿◠)': 'pet', '(◕‿◕)': 'pet',
  '*_*': 'pet', '♥♥♥': 'pet', '<3': 'pet',
});

/** A frame key, or null when it is not one of ours (never throws on junk). */
function frameKey(k) {
  return (typeof k === 'string' && Object.prototype.hasOwnProperty.call(BODY_FRAME_SRC, k)) ? k : null;
}
/** The pose a raw face string wears. Unpaired faces rest at `idle`. */
export function frameForFace(text) {
  const t = typeof text === 'string' ? text : '';
  return Object.prototype.hasOwnProperty.call(FACE_BODY_FRAME, t) ? FACE_BODY_FRAME[t] : 'idle';
}

/* ============================================================================
 * EMI'S DESK TOY (Counter Stock prize `emi_desk_toy`)
 * ==========================================================================*/
/** The prop, docked at her lower corner. Missing art hides the node and that is
 *  the whole error path - art is allowed to arrive after the code does.
 *
 *  THE FALLBACK HAS TO BE A FILE THAT EXISTS (2026-08-27). This pointed at
 *  `art/emi/toy.png` from the day the prop was written until the art landed,
 *  and that file was never drawn - so the "drop back to the still plate" path
 *  below could only ever miss a second time and take the prop down with it. It
 *  is the beads' first frame now, which is the toy the `emi_desk_toy` sprite
 *  actually depicts, so the fallback is a real still of the real prize. */
export const TOY_SRC = './art/emi/toys/beads_f1.png';
/* ONE SKU, FIVE TOYS, AND THAT IS DELIBERATE. The catalog has exactly one
 * `emi_desk_toy` row and it is not getting a second: what the token buys is "a
 * toy on EMI's desk", and WHICH toy is on it rotates by day off the same seed
 * her toy LINE already rotates on. Same night, same toy, every player - and
 * tomorrow it is a different one. Nothing here adds an sku, a price or an
 * unlock; the shelf is the counter's business and this is a wardrobe.
 *
 * A toy is a FRAME LIST or a single plate. `frames` is the animated shape;
 * `src` on its own is a still, and it is legal - a toy that is drawn once and
 * never moves must not need a four-entry array of the same file.
 */
/** How long one frame of a toy loop holds. Slow enough to read as a prop
 *  fidgeting on a desk, not as a second thing on the screen asking to be
 *  looked at - the same claim `emi-toy-bob` makes in widget.css. */
export const TOY_FRAME_MS = 140;
/** `./art/emi/toys/spinner_f1.png` ... PURE. */
export function toyFrameSrc(name, n) {
  return './art/emi/toys/' + String(name) + '_f' + String(n) + '.png';
}
function loop(name, n, ms) {
  const frames = [];
  for (let i = 1; i <= n; i += 1) frames.push(toyFrameSrc(name, i));
  return Object.freeze({ key: name, frames: Object.freeze(frames), ms: ms || TOY_FRAME_MS });
}
/** The props that exist as art, in install order. */
export const TOYS = Object.freeze([
  loop('spinner', 4),
  loop('globe', 4),
  loop('lamp', 4),
  /* the beads loop was composited frame by frame and is EIGHT, not four */
  loop('beads', 8),
  /* TODO (another agent, in flight 2026-08-27): the FLIP CLOCK is the fifth
   * toy and it slots in RIGHT HERE, one more row, nothing else moves. If it
   * ships as a single drawn plate rather than a frame list, the legal shape is
   *   Object.freeze({ key: 'flipclock', src: './art/emi/toys/flipclock.png' })
   * and `toyFrames()` below already reads it. */
]);
/** A toy's frames, whichever shape it was written in. Never throws, never
 *  answers a hole: junk answers an empty list and the caller falls back. */
export function toyFrames(toy) {
  if (!toy || typeof toy !== 'object') return [];
  if (Array.isArray(toy.frames) && toy.frames.length) return toy.frames;
  if (typeof toy.src === 'string' && toy.src) return [toy.src];
  return [];
}
/** WHICH TOY TONIGHT. Seeded on the UTC day exactly like the line, and TAGGED
 *  so that adding a fifth toy can never re-deal the line under it (the
 *  corkboard's rule for its poster, corkboard.js:405). PURE. */
export function toyIndex(day) {
  const s = (typeof day === 'string' ? day : '') + '|desk-toy';
  let h = 5381;
  for (let i = 0; i < s.length; i += 1) h = ((h * 33) ^ s.charCodeAt(i)) >>> 0;
  return TOYS.length ? h % TOYS.length : 0;
}
/* THE THREE LINES, and the shipped English lives right here for the reason
 * ASK_SEND_LABEL's does: no lexicon ever reaches EMI (emi/moments.js's law), so
 * the SHELL resolves `emi_toy_1..3` and hands the ANSWERS down as
 * `strings.toy`, and a host that hands nothing still gets her voice instead of
 * a key. THE FACE IS THE LINE'S OWN and not a coin flip: two of them are her
 * caught being fond of the thing (smug) and the third is her admitting it
 * (pet), and FACE_BODY_FRAME above turns each into the pose it names. */
export const TOY_LINES = Object.freeze([
  Object.freeze({ key: 'emi_toy_1', line: "Don't wind it too far. It gets ideas.", face: '(¬‿¬)' }),
  Object.freeze({ key: 'emi_toy_2', line: "It's not a toy, it's office equipment. Okay, it's a toy.", face: '(¬‿¬)' }),
  Object.freeze({ key: 'emi_toy_3', line: 'She spins when I do good work. We have a system.', face: '(◠‿◠)' }),
]);
/** WHICH LINE TONIGHT. Seeded on the UTC day, never `Math.random` (contract §4,
 *  the corkboard's rule): the toy says one thing all evening and something else
 *  tomorrow, and two players on the same night hear the same sentence. PURE. */
export function toyLineIndex(day) {
  const s = typeof day === 'string' ? day : '';
  let h = 5381;
  for (let i = 0; i < s.length; i += 1) h = ((h * 33) ^ s.charCodeAt(i)) >>> 0;
  return TOY_LINES.length ? h % TOY_LINES.length : 0;
}

/* ============================================================================
 * THE PRIZE BAG (Counter Stock)
 * ==========================================================================*/
/**
 * WHAT THE PLAYER OWNS, ARRIVING FROM THE SHELL. EMI owns no wallet, no store
 * key and no sku list: the shell reads `wallet.inv` - the one ownership witness
 * the whole restock uses - and hands down two booleans. Anything else (a bag
 * that is null, a getter that throws, a field that is a string) is NOT OWNED,
 * because a mascot may never guess at a purchase.
 *
 * A bag that is a FUNCTION is re-read on every ask, which is what lets a prize
 * bought mid-sitting light up without a reload (contract §4). BOTH shapes of
 * lateness are taken: a getter for the WHOLE bag
 *   `prizes: () => ({ deskToy: ownsSku('emi_desk_toy'), varsity: ... })`
 * and a getter PER ROW
 *   `prizes: { deskToy: () => ownsSku('emi_desk_toy'), varsity: () => ... }`
 * resolve identically, so the shell may write whichever reads better at the
 * mount site. A row getter that throws is not owned, like every other kind of
 * junk.
 */
function ownedRow(v) {
  let x = v;
  if (typeof x === 'function') { try { x = x(); } catch (e) { return false; } }
  return x === true;
}
export function readPrizes(src) {
  let v = src;
  if (typeof v === 'function') { try { v = v(); } catch (e) { v = null; } }
  const o = v && typeof v === 'object' ? v : {};
  return { deskToy: ownedRow(o.deskToy), varsity: ownedRow(o.varsity) };
}

/**
 * HOW LONG A LANDED LINE STAYS UP (owner ruling 2026-08-24: "3 sec, or more
 * according to length", re-scaled x1.35 on 2026-08-25). The typing cadence is
 * UNCHANGED - `. .. ...` still runs 420/420/520 and the clear frame is still
 * 200 - only the hold grows. Before/after, for the record: the floor is
 * 3000 -> 4050ms, a 20-character line 3000 -> 4050 (still the floor), a
 * 40-character line 3200 -> 4330, an 80-character line 5000 -> 6770.
 * An explicit ask from a caller wins when it is LONGER; it can never pull a
 * line back under the floor.
 */
export function sayHoldMs(line, explicitMs) {
  const n = typeof line === 'string' ? line.length : 0;
  const grown = DIALS.SAY_HOLD_BASE_MS + n * DIALS.SAY_HOLD_PER_CHAR_MS;
  const asked = (typeof explicitMs === 'number' && isFinite(explicitMs)) ? Math.round(explicitMs) : 0;
  /* THE PLAYER'S OWN DIAL (owner, 2026-08-25: "make the bark bubble permanence
   * time an option in the options"). The scale multiplies the floor and the
   * growth but NOT an explicit ask - ASK_HOLD_MS is an hour because a question
   * waits, and a "short" player has not asked for short questions. Applied
   * here for the same reason the x1.35 was: every line in the page becomes a
   * hold through this one function. */
  return Math.max(Math.round(Math.max(DIALS.SAY_HOLD_MIN_MS, grown) * holdScale), asked);
}
/* The scale `sayHoldMs` reads. MODULE-level on purpose: there is one EMI on a
 * page and the pure helpers (chains.js callers included) have no instance to
 * ask. The widget seeds it from its blob on boot and `setBubbleHold` is the
 * one writer after that. */
const HOLD_SCALE_MIN = 0.6;
const HOLD_SCALE_MAX = 3;
let holdScale = 1;
export function setSayHoldScale(x) {
  const n = typeof x === 'number' && isFinite(x) ? x : 1;
  holdScale = Math.min(HOLD_SCALE_MAX, Math.max(HOLD_SCALE_MIN, n));
  return holdScale;
}
export function getSayHoldScale() { return holdScale; }
/** Everything in a locked SAY that is NOT the hold: . / .. / ... plus the clear. */
export const SAY_LEAD_MS = 420 + 420 + 520 + 200;
/* The bubble hangs UP OFF EMI's right ear (emi.css: left:58%, bottom:96%,
 * max-width:104px), so she needs roughly this multiple of her own width to the
 * right of her left edge or the line runs off the viewport - at which point it
 * flips to the left ear (.bubble-left). */
const BUBBLE_REACH = 1.55;
/* ...and it RISES, so parked within this many px of the top edge there is no
 * sky for it: it drops below the chin instead (.bubble-low). Sized for the
 * worst wrapped bark (~5 lines) plus the tail. */
const BUBBLE_RISE = 96;

/** The persisted blob's key in the C# meta store (core/store.js top-level). */
export const STORE_KEY = 'emi';

function clamp(v, lo, hi) { return v < lo ? lo : (v > hi ? hi : v); }
function num(v) { return typeof v === 'number' && isFinite(v) ? v : null; }
function nowMs() { return Date.now(); }
function isoDay() { try { return new Date().toISOString().slice(0, 10); } catch (e) { return null; } }

/** A zeroed telemetry record. Lifetime counters; no UI reads them yet. */
function blankStats() {
  return {
    pets: 0, petStreaks3: 0, drags: 0, flings: 0, hides: 0, dockRestores: 0,
    bubblesSeen: 0, firstSeenAt: null, lastSeenAt: null, msVisible: 0,
    /* THE OFF CHANNELS (W3). EXACTLY THREE counters, and they ride the same
     * debounced blob: how many channels ever played, how many times she was
     * caught on one, and how many times she actually showed you. Cooldowns and
     * the per-session cap are SESSION-LOCAL by design and are not here. */
    takeovers: 0, caught: 0, reveals: 0,
    /* WHERE SHE GETS PUT DOWN: drop counts per ninth of the viewport (z0..z8,
     * row-major). The favourite-spot beats read the count off the dropAt
     * payload; nothing else looks in here. */
    zones: {},
  };
}

function readStats(raw) {
  const s = blankStats();
  if (!raw || typeof raw !== 'object') return s;
  for (const k of Object.keys(s)) {
    const v = raw[k];
    if (k === 'zones') {
      if (v && typeof v === 'object') {
        for (const zk of Object.keys(v)) {
          const n = v[zk];
          if (/^z[0-8]$/.test(zk) && typeof n === 'number' && isFinite(n) && n >= 0) {
            s.zones[zk] = Math.round(n);
          }
        }
      }
    } else if (k === 'firstSeenAt' || k === 'lastSeenAt') {
      if (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v)) s[k] = v;
    } else if (typeof v === 'number' && isFinite(v) && v >= 0) {
      s[k] = Math.round(v);
    }
  }
  return s;
}

/* ASKS: THE NAME SANITISER, AND IT LIVES HERE ON PURPOSE ---------------------
 * `emi/asks.js` collects the name and this file reads it back off the stored
 * blob on every boot, so the rule has to be one function or the two readers
 * drift. Her voice is lowercase: trim -> strip anything outside [a-z0-9 ] ->
 * collapse the runs -> 8 characters. An empty answer is a SKIP, never an empty
 * name - `emi.name` is either a real string or absent. */
export const ASK_NAME_MAX = 8;
/** The Send button's shipped English. The live label comes from the SHELL,
 *  which resolves `emi_ask_send` through the lexicon (see createWidget). */
export const ASK_SEND_LABEL = 'send';
export function sanitizeAskName(raw) {
  if (typeof raw !== 'string') return '';
  let s = raw.toLowerCase().replace(/[^a-z0-9 ]+/g, '');
  s = s.replace(/\s+/g, ' ').trim();
  if (s.length > ASK_NAME_MAX) s = s.slice(0, ASK_NAME_MAX).trim();
  return s;
}

/* ASKS: THE LEDGER, READ OFF THE SAME `emi` BLOB EVERYTHING ELSE RIDES.
 * There is exactly ONE writer of the `emi` key on this page (save() below), so
 * the ask ledger is spliced into that blob rather than minting a second key -
 * two writers of one key is how a drag comes to eat a name. Every field is
 * re-derived defensively: an unreadable ledger starts clean and never throws. */
function readAskState(raw) {
  const src = raw && typeof raw === 'object' ? raw : {};
  const out = { ask: {}, name: sanitizeAskName(src.name), lastAskSession: 0, bedSkips: 0 };
  const n = Number(src.lastAskSession);
  if (Number.isFinite(n) && n > 0) out.lastAskSession = Math.round(n);
  const b = Number(src.bedSkips);
  if (Number.isFinite(b) && b > 0) out.bedSkips = Math.round(b);
  const rows = src.ask;
  if (rows && typeof rows === 'object' && !Array.isArray(rows)) {
    for (const k of Object.keys(rows)) {
      const r = rows[k];
      if (!r || typeof r !== 'object') continue;
      if (typeof r.a !== 'string' || !r.a) continue;
      const cnt = Number(r.n);
      const ses = Number(r.s);
      out.ask[k] = {
        a: r.a.slice(0, 16),
        n: Number.isFinite(cnt) && cnt > 0 ? Math.round(cnt) : 1,
        s: Number.isFinite(ses) && ses > 0 ? Math.round(ses) : 0,
      };
    }
  }
  return out;
}

/* ----------------------------------------------------------------------------
 * W3 "EVERY INPUT ANSWERED" (2026-08-25): SHE ANSWERS A TOUCH NOW.
 *
 * Picking her up, putting her down, patting her and the ask strip coming up
 * were all silent, which is the one gap the wave is named after: a press that
 * makes no sound reads as a press the page did not get.
 *
 * `shell/audio.js` holds the only audio node on the page (trap 18), so this is
 * a REQUEST on `document` and never a sound - the same defensive shape
 * `emi/fieldtrips.js` and `shell/ceremonies.js` already use, and a dropped cue
 * is not an error. Everything fired from this file rides the VOICE bus and sits
 * at or under her Blipese (`emi_blip` goes out at .10, and the loudest touch
 * cue is a .16 thud), because the mascot is never the loudest thing on screen.
 * -------------------------------------------------------------------------- */
function sfx(name, level, pitch) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: { name: String(name || 'blip'), level: Number(level) || 0.1, bus: 'voice', pitch },
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/* ============================================================================
 * createWidget
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Element} o.root            the `.arc-emi` layer (fixed, inset:0, pe:none)
 * @param {Object=} o.face            face.js module | createFace fn | a live face
 * @param {Object=} o.chains          chains.js module (FACES/CHAINS/playChain/makeSay)
 * @param {Object=} o.fx              fx.js module | showFx fn
 * @param {Object=} o.store           core/store.js (get/set) - the persistence seam
 * @param {Function=} o.toast         the SHELL's toast (boot.js -> createShell's `shout`)
 * @param {Object|Function=} o.prizes COUNTER STOCK: `{deskToy, varsity}` (or a
 *                                    getter for it) off the shell's `wallet.inv`.
 *                                    Absent = owns nothing, which is every
 *                                    player before the restock.
 * @param {Function=} o.log
 */
export function createWidget({ root, face, chains, fx, vox: vox0, store, toast, log, assets, settings, strings, prizes } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  /* THE ONE DISPLAY STRING EMI HAS EVER BEEN HANDED, AND IT ARRIVES RESOLVED.
   * No lexicon reaches EMI (emi/moments.js's law, and emi/channels.js says it
   * out loud) - the SHELL has `t` and hands the answer down, exactly the way
   * shell/orientation.js hands her three lines over as `payload.line`. The
   * fallback is the shipped English so a host that passes nothing still gets a
   * labelled button rather than an empty one. */
  const ASK_STRINGS = Object.freeze({
    send: (strings && typeof strings.askSend === 'string' && strings.askSend.trim())
      ? strings.askSend.trim() : ASK_SEND_LABEL,
  });
  /* COUNTER STOCK: THE DESK TOY'S THREE LINES, resolved by the shell exactly
   * the way the Send label above is (`strings.toy` = the three `emi_toy_*` rows
   * in order). Resolved PER SLOT, so a host that hands two rows and a null
   * still gets three lines and never an empty bubble. */
  const TOY_TEXT = Object.freeze(TOY_LINES.map((row, i) => {
    const got = (strings && Array.isArray(strings.toy)) ? strings.toy[i] : null;
    return (typeof got === 'string' && got.trim()) ? got.trim() : row.line;
  }));
  /* OFF CHANNELS (W3): the two things NOW WATCHING needs and nothing else in
   * this file does - the shell's provider handle (media through the HOST, the
   * only remote path this bundle has) and `init.settings` (the player's own
   * local library). Both optional; absent means the channel plans itself out. */
  const channelAssets = assets || null;
  const channelSettings = settings || null;
  /* THE SHELL'S TOAST, NOT A SECOND ONE. `#arc-toast` already exists, already
   * stacks above EMI (z 60 vs 50) and already expires its own nodes; minting a
   * rival would put two notice systems on one page. */
  const shout = typeof toast === 'function' ? toast : () => {};
  if (!root || typeof document === 'undefined' || !document.createElement) return null;

  /* ---------------------- persisted state ------------------------------- */
  let saved = {};
  try {
    const raw = store && typeof store.get === 'function' ? store.get(STORE_KEY) : null;
    if (raw && typeof raw === 'object') saved = raw;
  } catch (e) { say('emi: store read failed - ' + ((e && e.message) || e)); }

  const stats = readStats(saved.stats);
  /* ASKS: the ask ledger, on the same blob and the same writer (see above). */
  const askState = readAskState(saved);
  /* WIDTH IS A DEFAULT UNTIL SOMEBODY SETS IT. A stored `w` only exists once a
   * resize verb has run (`setWidth`; there is no resize UI yet), so out of the
   * box she follows the window: full size on a roomy one, smaller on a narrow
   * one. Persisting the auto width would freeze the first window she ever saw. */
  let userSized = num(saved.w) !== null;
  /* TWO WIDTHS, AND THE DIFFERENCE MATTERS. `userWidth` is what the player chose
   * and the ONLY thing that is ever persisted; `width` is what this device is
   * allowed to draw, which on a phone is `userWidth` clamped to the phone ceiling.
   * Collapsing them would let one phone session overwrite the size the player set
   * on their desktop, because the same blob is shared by both. */
  let userWidth = userSized ? clamp(num(saved.w), DIALS.W_MIN, DIALS.W_MAX) : null;
  let width = effectiveWidth();
  // Anchor = the TOP-LEFT corner as a fraction of the viewport, so a resize
  // moves EMI proportionally and then re-clamps instead of drifting off-screen.
  let fx0 = num(saved.x);
  let fy0 = num(saved.y);
  let hidden = saved.hidden === true;
  let hintShown = saved.hintShown === true;
  /* The bubble-hold scale rides the same blob (see `setSayHoldScale`). A blob
   * without one is a player who never touched the option: scale 1, unwritten. */
  if (typeof saved.holdScale === 'number' && isFinite(saved.holdScale) && saved.holdScale !== 1) {
    setSayHoldScale(saved.holdScale);
  }
  let enabled = true;

  let saveTimer = null;
  let visibleSince = null;

  function accrueVisible() {
    if (visibleSince == null) return;
    stats.msVisible += Math.max(0, nowMs() - visibleSince);
    visibleSince = null;
  }
  function beginVisible() { if (visibleSince == null) visibleSince = nowMs(); }

  /** msVisible INCLUDING the stretch still running, rounded to whole seconds -
   *  the counter is a story beat's number, not a metric, and rounding keeps a
   *  mid-session write from churning the blob. */
  function visibleMs() {
    const live = visibleSince == null ? 0 : Math.max(0, nowMs() - visibleSince);
    return Math.round((stats.msVisible + live) / 1000) * 1000;
  }

  function blob() {
    // `zones` is the one nested object: cloned so the store never holds a live
    // reference into the counters.
    const out = Object.assign({}, stats, { msVisible: visibleMs(), zones: Object.assign({}, stats.zones) });
    const b = { x: fx0, y: fy0, hidden, stats: out };
    // `w` is written ONLY when it was chosen, never when it was derived from the
    // window - see `userSized`. An auto width in the blob would out-vote the
    // viewport rule for ever after the first drag.
    if (userSized && userWidth != null) b.w = userWidth;
    if (hintShown) b.hintShown = true;
    // The bubble-hold option, written only when it is not the default.
    if (holdScale !== 1) b.holdScale = holdScale;
    /* ASKS: the ledger rides out with everything else. Written only when there
     * is something to write, so a player who has never been asked anything
     * keeps the blob they always had. */
    if (askState.name) b.name = askState.name;
    if (askState.lastAskSession) b.lastAskSession = askState.lastAskSession;
    if (askState.bedSkips) b.bedSkips = askState.bedSkips;
    if (askState.ask && Object.keys(askState.ask).length) {
      b.ask = JSON.parse(JSON.stringify(askState.ask));
    }
    return b;
  }

  /** Write-through to the C# meta store, DEBOUNCED. Never per pointermove. */
  function save(immediate) {
    if (!store || typeof store.set !== 'function') return;
    if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
    const doIt = () => {
      saveTimer = null;
      try { store.set(STORE_KEY, blob()); }
      catch (e) { say('emi: store write failed - ' + ((e && e.message) || e)); }
    };
    if (immediate) doIt();
    else saveTimer = setTimeout(doIt, DIALS.SAVE_DEBOUNCE_MS);
  }

  /** Called on every real interaction: keeps the two date stamps honest. */
  function touchSeen() {
    const d = isoDay();
    if (!d) return;
    if (!stats.firstSeenAt) stats.firstSeenAt = d;
    stats.lastSeenAt = d;
  }

  /* ---------------------- DOM ------------------------------------------- */
  const el = document.createElement('div');
  el.className = 'emi';
  el.setAttribute('role', 'img');
  el.setAttribute('aria-label', 'EMI');

  const body = document.createElement('img');
  body.className = 'emi-body';
  /* THE RESTING POSE IS `idle`, NOT the ceremony one. body.png (arms up) is what
   * a stamp, a win or a reveal puts on her; at rest her arms are down. */
  body.src = BODY_FRAME_SRC.idle;
  body.alt = '';
  body.draggable = false;
  if (body.setAttribute) body.setAttribute('draggable', 'false');

  /* COUNTER STOCK: THE DESK TOY. One node, minted at birth and hidden at rest -
   * the ask strip's arrangement, for the ask strip's reason: a player who does
   * not own the prize carries one hidden img and no code path below ever runs.
   * The `src` is deliberately NOT set here, so an unowned player never even
   * fetches art they cannot see (and never logs a 404 for it). */
  const toy = document.createElement('img');
  toy.className = 'emi-toy';
  toy.alt = '';
  toy.draggable = false;
  toy.hidden = true;
  if (toy.setAttribute) {
    toy.setAttribute('draggable', 'false');
    /* IT IS A PROP, NOT A CONTROL. EMI's whole pointer vocabulary is
     * pointer-only by design (petting has never been keyboard-reachable and
     * LAW 2 forbids this file a key listener), so the toy says so out loud
     * rather than offering a role no key can reach. */
    toy.setAttribute('aria-hidden', 'true');
  }

  const canvas = document.createElement('canvas');
  canvas.className = 'emi-screen';

  const fxHost = document.createElement('div');
  fxHost.className = 'emi-fx';

  const bubble = document.createElement('div');
  bubble.className = 'emi-bubble';
  bubble.hidden = true;

  /* ASKS: THE STRIP. One node, minted at birth and empty at rest, so an ask
   * costs no DOM churn and a page that never asks anything carries one hidden
   * div. It is a SIBLING of the bubble inside `.emi`, which is what makes the
   * `.bubble-left` / `.bubble-low` flips apply to both halves for free (the
   * CSS mirrors them off the same root classes - trap 61). */
  const askStrip = document.createElement('div');
  askStrip.className = 'emi-ask';
  askStrip.hidden = true;
  if (askStrip.setAttribute) askStrip.setAttribute('role', 'group');

  const xBtn = document.createElement('button');
  xBtn.className = 'emi-x';
  xBtn.type = 'button';
  xBtn.textContent = '×';
  xBtn.title = 'Hide EMI';
  if (xBtn.setAttribute) xBtn.setAttribute('aria-label', 'Hide EMI');

  /* THE BUBBLE'S OFFSET IS A DIAL, and the sheets read it as a custom property
   * so widget.css keeps one half of it and this file's reach test keeps the
   * other, both off the same number. */
  try {
    if (el.style && typeof el.style.setProperty === 'function') {
      el.style.setProperty('--emi-bubble-dx', DIALS.BUBBLE_SHIFT_X + 'px');
    }
  } catch (e) { /* noop */ }

  el.appendChild(body);
  el.appendChild(toy);           // COUNTER STOCK (z1: over the body, under the fx)
  el.appendChild(canvas);
  el.appendChild(fxHost);
  el.appendChild(bubble);
  el.appendChild(askStrip);      // ASKS
  el.appendChild(xBtn);

  const dock = document.createElement('button');
  dock.className = 'emi-dock';
  dock.type = 'button';
  dock.textContent = '0_0';
  dock.title = 'Show EMI';
  if (dock.setAttribute) dock.setAttribute('aria-label', 'Show EMI');
  dock.hidden = true;

  root.appendChild(el);
  root.appendChild(dock);

  /* ---------------------- the body frames ------------------------------- */
  /* A FRAME THAT WILL NOT LOAD IS NOT AN ERROR. EMI floats over every screen in
   * the school and she may never break one, so a missing or broken png falls
   * back to `body.png` (the one frame that has shipped since day one) silently
   * and once per key. */
  const frameFailed = Object.create(null);
  let bodyFrame = 'idle';
  /* WHICH WARDROBE IS UP (Counter Stock). One of the two frozen maps at the top
   * of this file, and it is swapped WHOLE or not at all - see THE VARSITY
   * JACKET. Every url in this file is resolved through it, so a fallback lands
   * in the SAME set she is wearing and the coat can never half come off. */
  let frameSet = BODY_FRAME_SRC;

  function frameUrl(key) {
    return frameFailed[key] ? frameSet.celebration : frameSet[key];
  }

  /** Swap the body png. A no-op when it is already up (this runs per sway step). */
  function setBodyFrame(key) {
    const k = frameKey(key) || 'idle';
    if (k === bodyFrame) return k;
    bodyFrame = k;
    try { body.src = frameUrl(k); } catch (e) { /* noop */ }
    try { if (body.setAttribute) body.setAttribute('data-frame', k); } catch (e) { /* noop */ }
    return k;
  }

  if (body.addEventListener) {
    body.addEventListener('error', () => {
      const k = bodyFrame;
      if (!k || k === 'celebration' || frameFailed[k]) return;
      frameFailed[k] = true;
      say('emi: body frame "' + k + '" failed to load - falling back to body.png');
      try { body.src = frameSet.celebration; } catch (e) { /* noop */ }
    });
  }

  /* PRELOAD, ONCE PER SET. The bundle is offline so every frame is a local file,
   * but a first decode on the beat a stamp lands would still flash an empty
   * body. `Image` does not exist in the node DOM double - no preload, no
   * problem. It takes the map as an argument because the varsity jacket is a
   * SECOND set that arrives later and wants the same warming (Counter Stock). */
  const preloaded = [];
  function preloadFrames(map) {
    if (typeof Image !== 'function' || !map) return;
    for (const k of Object.keys(map)) {
      try {
        const im = new Image();
        im.onerror = () => {
          /* A SET SHE IS NO LONGER WEARING MAY NOT MARK A FRAME BROKEN. The
           * standard preload is still in flight when the jacket lands, and its
           * 404s would otherwise pin `frameFailed` on frames the new set has. */
          if (map !== frameSet || frameFailed[k]) return;
          frameFailed[k] = true;
          say('emi: body frame "' + k + '" is missing - body.png stands in');
          if (bodyFrame === k) { try { body.src = frameSet.celebration; } catch (e) { /* noop */ } }
        };
        im.src = map[k];
        preloaded.push(im);
      } catch (e) { /* noop */ }
    }
  }
  preloadFrames(BODY_FRAME_SRC);

  /* ---------------------- the prizes (Counter Stock) --------------------- */
  /* TWO PRIZES REACH EMI and neither of them is a setting: EMI'S DESK TOY puts
   * a prop on her tube, EMI: VARSITY JACKET re-dresses every pose. Owned = on,
   * v1, no toggle (the contract's ruling) - so the whole of this section is a
   * READER of the shell's bag plus the two things it switches on.
   *
   * NOTHING HERE MOVES A BALANCE, ASKS FOR ONE, OR NAMES AN SKU. The echo law
   * is the counter's; by the time a flag reaches this file the host has already
   * banked the purchase and the shell has already read `wallet.inv`. */
  let prizeSrc = prizes || null;
  let prizeState = readPrizes(prizeSrc);
  /* 'off' -> 'probing' -> 'on' | 'failed'. `failed` is STICKY for the sitting:
   * art may lag code, and a jacket that half-loads is worse than the pose sheet
   * that shipped. There is no path back to 'off'. */
  let varsityState = 'off';
  /** The toy art 404'd once; the prop stays down however often the bag is set. */
  let toyBroken = false;

  /* ---------------------- the desk toy's loop --------------------------- */
  /* TONIGHT'S TOY, picked ONCE at mount off the same UTC day the line is picked
   * off. It does not re-roll mid-sitting: a prop that changed shape while a
   * player was looking at it would read as a bug, not as a rotation. */
  const toyPick = TOYS[toyIndex(isoDay() || '')] || TOYS[0] || null;
  /* An empty table is not a crash: TOY_SRC is what the prop wore before any of
   * this landed and it is still a legal plate to stand on. */
  let toyPlan = toyFrames(toyPick);
  if (!toyPlan.length) toyPlan = [TOY_SRC];
  /** The day's loop would not load. TOY_SRC is the last plate she falls back
   *  to, and it is tried exactly once before the prop is given up on. */
  let toyFellBack = false;
  let toyStep = 0;
  let toyTimer = null;

  function stopToyLoop() {
    if (toyTimer !== null) { clearInterval(toyTimer); toyTimer = null; }
  }

  /* THREE SEPARATE REFUSALS, and they are not the same question.
   *  - REDUCED MOTION kills the animation outright (splitflap.js's ruling): the
   *    prop is still on the desk, it simply stands still on frame 1.
   *  - A HIDDEN TAB has nothing to paint, so the interval must not exist.
   *  - A DOCKED EMI is inside `display:none`; same.
   * It is SILENT by design. shell/audio.js owns the only audio node on this
   * page and a prop that ticked every 140ms would be unbearable, so no cue is
   * fired here - the toy already has its one voice, at pokeToy(). */
  function startToyLoop() {
    stopToyLoop();
    if (toy.hidden || toyBroken || hidden) return;
    if (toyPlan.length < 2) return;              // a single plate never animates
    if (reducedMotion()) { showToyFrame(0); return; }
    if (typeof document !== 'undefined' && document.hidden === true) return;
    const ms = Math.max(60, Number(toyPick && toyPick.ms) || TOY_FRAME_MS);
    toyTimer = setInterval(() => {
      if (toy.hidden || toyBroken || hidden || reducedMotion()
        || (typeof document !== 'undefined' && document.hidden === true)) { stopToyLoop(); return; }
      showToyFrame(toyStep + 1);
    }, ms);
  }

  function showToyFrame(i) {
    if (!toyPlan.length) return;
    toyStep = ((i % toyPlan.length) + toyPlan.length) % toyPlan.length;
    try { toy.src = toyPlan[toyStep]; } catch (e) { /* noop */ }
  }

  /* The tab going away is not a hide and not a destroy, so nothing above hears
   * it. One listener, and destroy() takes it back off. */
  function onToyVisibility() {
    if (typeof document !== 'undefined' && document.hidden === true) stopToyLoop();
    else startToyLoop();
  }
  if (typeof document !== 'undefined' && document.addEventListener) {
    document.addEventListener('visibilitychange', onToyVisibility);
  }

  /** Move the whole wardrobe over, and repaint the pose she is wearing now. */
  function useFrameSet(map) {
    if (!map || map === frameSet) return false;
    frameSet = map;
    // A broken frame in the OLD set says nothing about the new one.
    for (const k of Object.keys(frameFailed)) delete frameFailed[k];
    try { body.src = frameUrl(bodyFrame); } catch (e) { /* noop */ }
    return true;
  }

  /**
   * ONE PROBE, AND THE ANSWER IS FINAL FOR THE SITTING. `body-idle.png` is the
   * frame she wears at rest and the first one any player would see, so it is
   * the honest witness for "is this folder in the bundle yet". A load swaps all
   * ten; an error (or a platform with no `Image` at all, which is the node DOM
   * double) leaves the standard set standing for ever and says so once.
   */
  function armVarsity() {
    if (outfitPick) return;          // a chosen sheet outranks the bag's default
    if (!prizeState.varsity || varsityState !== 'off') return;
    if (typeof Image !== 'function') { varsityState = 'failed'; return; }
    let im = null;
    try { im = new Image(); } catch (e) { varsityState = 'failed'; return; }
    varsityState = 'probing';
    im.onload = () => {
      if (varsityState !== 'probing') return;   // destroyed, or already answered
      varsityState = 'on';
      useFrameSet(VARSITY_FRAME_SRC);
      preloadFrames(VARSITY_FRAME_SRC);
      say('emi: varsity jacket on - the whole pose set moved');
    };
    im.onerror = () => {
      if (varsityState !== 'probing') return;
      varsityState = 'failed';
      say('emi: varsity art is not in the bundle yet - the standard set stands');
    };
    try { im.src = VARSITY_FRAME_SRC.idle; }
    catch (e) { varsityState = 'failed'; }
    preloaded.push(im);
  }

  /* ---------------------- the neutral outfit selector -------------------
   * THREE OF THE FOUR SHEETS ARE NOT PRIZES. `emi_varsity` is an sku and the
   * bag above turns it on; labcoat, cheer and swim are art that is simply in
   * the bundle, with no price, no sku and no unlock - so they reach her through
   * a plain seam that names a sheet and nothing else. This function must never
   * grow an ownership test for those three, and `setOutfit('varsity')` still
   * defers to the bag because that one IS bought.
   *
   * THE SWAP IS STILL ALL TEN FRAMES OR NONE, for the reason THE VARSITY JACKET
   * gives at the top of this file: one probe on `body-idle.png`, and a sheet
   * that is not in the bundle leaves the standard set standing.
   *
   * TODO (owner decision, unmade): nothing in the school CALLS this yet, because
   * there is no surface for choosing an outfit. Deliberately not invented here -
   * see the note at OUTFITS. */
  let outfitPick = null;
  let outfitState = 'off';

  function armOutfit(name) {
    const want = (typeof name === 'string' && OUTFITS.indexOf(name) >= 0) ? name : null;
    if (want === 'varsity' && !prizeState.varsity) return outfitState;   // the one gate
    outfitPick = want;
    if (!want) {
      outfitState = 'off';
      useFrameSet(BODY_FRAME_SRC);
      return outfitState;
    }
    const map = frameSetFor(want);
    if (typeof Image !== 'function') { outfitState = 'failed'; return outfitState; }
    let im = null;
    try { im = new Image(); } catch (e) { outfitState = 'failed'; return outfitState; }
    outfitState = 'probing';
    im.onload = () => {
      if (outfitState !== 'probing' || outfitPick !== want) return;
      outfitState = 'on';
      useFrameSet(map);
      preloadFrames(map);
      say('emi: outfit ' + want + ' on - the whole pose set moved');
    };
    im.onerror = () => {
      if (outfitState !== 'probing' || outfitPick !== want) return;
      outfitState = 'failed';
      outfitPick = null;
      useFrameSet(BODY_FRAME_SRC);
      say('emi: outfit ' + want + ' is not in the bundle yet - the standard set stands');
    };
    try { im.src = map.idle; }
    catch (e) { outfitState = 'failed'; }
    preloaded.push(im);
    return outfitState;
  }

  /** Re-read the bag and light (or unlight) what it says. Idempotent. */
  function applyPrizes() {
    prizeState = readPrizes(prizeSrc);
    const wantToy = prizeState.deskToy && !toyBroken;
    if (wantToy && !toy.src) showToyFrame(0);
    toy.hidden = !wantToy;
    if (wantToy) startToyLoop(); else stopToyLoop();
    armVarsity();
    return prizeState;
  }

  /* A PROP THAT WILL NOT LOAD IS NOT AN ERROR EITHER: the node goes away, and
   * it does not come back this sitting however often the bag is re-set. */
  if (toy.addEventListener) {
    toy.addEventListener('error', () => {
      /* THE DAY'S LOOP MAY NOT BE IN THE BUNDLE, so a broken frame drops the
       * whole loop back to the one still plate rather than taking the prop
       * down. That plate is a frame that ships in this same folder, which is
       * the only reason this branch is worth having. A second error, on the
       * plate itself, is the end of it for the sitting. */
      stopToyLoop();
      if (!toyFellBack) {
        toyFellBack = true;
        toyPlan = [TOY_SRC];
        showToyFrame(0);
        return;
      }
      toyBroken = true; toy.hidden = true;
    });
    /* INSIDE `.emi`, so stopping this stream is legal and necessary - the x
     * does exactly the same, and for the same reason: a poke at the toy is
     * never also a head-pat and never the start of a drag. Nothing OUTSIDE the
     * widget is touched, so law 1 (INPUT TRUST) is untouched. */
    toy.addEventListener('pointerdown', (ev) => { if (ev && ev.stopPropagation) ev.stopPropagation(); });
    toy.addEventListener('click', (ev) => {
      if (ev && typeof ev.stopPropagation === 'function') ev.stopPropagation();
      pokeToy();
    });
  }
  applyPrizes();

  /* ---------------------- the renderer, injected ------------------------ */
  /* A CANVAS THE PLATFORM CANNOT PAINT IS NOT AN ERROR. The node DOM double has
   * no getContext, so the widget runs faceless there instead of throwing on
   * boot - which is what keeps the shell suite green. */
  let painter = null;
  let CHAINS = null;
  let playChain = null;
  let makeSay = null;
  let showFx = null;
  /* HER VOICE (emi/vox.js), injected the same way and just as optional. It owns
   * no audio node - it only asks shell/audio.js for blips - so from here it is
   * three calls hanging off setBubble(), the one place that already knows the
   * difference between typing, a landed line and a cleared bubble. */
  let vox = null;
  /* CONSTRUCTION-TIME ATTACH IS NOT A REPAINT. `attach({face, chains, fx})` runs
   * once from inside createWidget - a caller may hand the renderer straight to
   * the constructor instead of injecting it a tick later - and at that point the
   * chain runner's own state (`current`, `timers`, `blinkTimer`) is still in its
   * temporal dead zone below. The first-paint block at the BOTTOM of this
   * function already calls idle(), so the sync paint here is only for the LATE
   * attach. Without this flag the constructor threw
   * `ReferenceError: Cannot access 'current' before initialization`. */
  let built = false;

  function attach(mods) {
    const f = mods && mods.face;
    const c = mods && mods.chains;
    const x = mods && mods.fx;
    const v = mods && mods.vox;
    if (v && typeof v.speak === 'function') vox = v;
    if (!painter && f && canvas && typeof canvas.getContext === 'function') {
      const mk = typeof f === 'function' ? f : (f && typeof f.createFace === 'function' ? f.createFace : null);
      try {
        if (mk) painter = mk(canvas, {});
        else if (f && typeof f.draw === 'function') painter = f;
      } catch (e) { say('emi: createFace threw - ' + ((e && e.message) || e)); painter = null; }
    }
    if (c) {
      if (c.CHAINS && typeof c.CHAINS === 'object') CHAINS = c.CHAINS;
      if (typeof c.playChain === 'function') playChain = c.playChain;
      if (typeof c.makeSay === 'function') makeSay = c.makeSay;
    }
    if (x) showFx = typeof x === 'function' ? x : (typeof x.showFx === 'function' ? x.showFx : null);
    if (painter && !hidden && enabled) {
      // THE BUNDLED FACE FIRST. face.ready settles once Noto Sans Mono is in;
      // painting before it would show one frame in the system monospace. This
      // one is safe from the constructor: it resolves on a later tick.
      if (painter.ready && typeof painter.ready.then === 'function') {
        painter.ready.then(() => { if (!hidden && enabled && !busy()) idle(); }).catch(() => {});
      }
      if (built) idle();
    }
  }
  attach({ face, chains, fx, vox: vox0 });

  /* ---------------------- motion preference ----------------------------- */
  function reducedMotion() {
    try {
      if (document.documentElement && document.documentElement.classList
        && document.documentElement.classList.contains('arc-reduced')) return true;
      if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
        return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches;
      }
    } catch (e) { /* noop */ }
    return false;
  }

  /* ---------------------- geometry -------------------------------------- */
  function viewport() {
    const w = (typeof window !== 'undefined' && window.innerWidth) || 1280;
    const h = (typeof window !== 'undefined' && window.innerHeight) || 800;
    return { w, h };
  }
  function sizePx() {
    return { w: width, h: Math.round(width * DIALS.ASPECT_H / DIALS.ASPECT_W) };
  }
  /** The default width for THIS window. Owner's rule: 150 on >= 900px, 116 below,
   *  and a flat W_MOBILE on a phone, where neither of those is small enough. */
  function autoWidth() {
    if (isMobile()) return DIALS.W_MOBILE;
    return viewport().w >= DIALS.W_NARROW_VW ? DIALS.W_DEFAULT : DIALS.W_NARROW;
  }
  /** What this device may DRAW: the chosen width (or the window's default),
   *  fenced by the phone ceiling. Never what gets stored - see `userWidth`. */
  function effectiveWidth() {
    const base = (userSized && userWidth != null) ? userWidth : autoWidth();
    return isMobile() ? Math.min(base, DIALS.W_MOBILE_MAX) : base;
  }
  /** The margin this device keeps between EMI and the edge. */
  function margin() { return isMobile() ? DIALS.MARGIN_MOBILE : DIALS.MARGIN; }
  /** Re-derive the drawn width after a resize or a rotate. Runs even when the
   *  player has sized her, because crossing onto or off a phone changes the
   *  ceiling and only the ceiling - `userWidth` is not touched either way. */
  function refitWidth() {
    const next = effectiveWidth();
    if (next === width) return false;
    width = next;
    return true;
  }
  /* ASKS: a06's session-only spot, {x,y} in viewport fractions. NEVER saved. */
  let askSpot = null;

  /** Place EMI from the stored fractions, clamped inside the viewport. */
  function place() {
    const vp = viewport();
    const s = sizePx();
    const m = margin();
    /* ASKS (a06): "can i sit on the other side today?" is a SESSION move, not
     * a new home. The fractions are never touched, so tomorrow she is back
     * where the player left her; this override is what keeps her on the other
     * side across a resize in the meantime, and it dies with the sitting. */
    if (askSpot) {
      const l2 = clamp(askSpot.x * vp.w, m, Math.max(m, vp.w - s.w - m));
      const t2 = clamp(askSpot.y * vp.h, m, Math.max(m, vp.h - s.h - m));
      el.style.width = s.w + 'px';
      el.style.left = Math.round(l2) + 'px';
      el.style.top = Math.round(t2) + 'px';
      faceBubble(l2, t2, s.w, vp.w);
      return { left: l2, top: t2, s, vp };
    }
    if (fx0 == null || fy0 == null) {
      /* First run: park her bottom-right, clear of the dock's corner. A phone has
       * no room for the desktop's 24/56 standoff, so she docks tight into the
       * bottom edge there and leaves the middle of the glass to the board.
       * THE 72 IS THE POSTBOX. 2026-08-25 moved the campus mail chip out of the
       * top-right cluster and into the bottom-right corner (shell/mail.css), and
       * a 6px standoff parked her flat on top of a 44px control she would then
       * eat every tap on - her body is the one `pointer-events:auto` thing on
       * that layer. 6 + 44 + 14 (the chip's own bottom inset) + 8 of daylight
       * steps her left of it and leaves the bottom edge she is meant to dock to.
       * FIRST RUN ONLY: a stored spot never comes through here, so nobody who
       * has ever moved her sees this number. */
      const padX = isMobile() ? 72 : 24;
      const padY = isMobile() ? 10 : 56;
      fx0 = (vp.w - s.w - padX) / vp.w;
      fy0 = (vp.h - s.h - padY) / vp.h;
    }
    const left = clamp(fx0 * vp.w, m, Math.max(m, vp.w - s.w - m));
    const top = clamp(fy0 * vp.h, m, Math.max(m, vp.h - s.h - m));
    el.style.width = s.w + 'px';
    el.style.left = Math.round(left) + 'px';
    el.style.top = Math.round(top) + 'px';
    faceBubble(left, top, s.w, vp.w);
    return { left, top, s, vp };
  }
  /** THE BUBBLE HANGS OFF HER RIGHT EAR and would run off the right edge of the
   *  window there, so it flips to the left ear instead (`.bubble-left`, styled in
   *  widget.css). Decided from the position, so a drag and a resize both fix it. */
  function faceBubble(left, top, w, vw) {
    // The +15px shift is part of the reach on BOTH sides: the box is mirrored
    // with the flip, so the flip has to know about the offset or it would
    // re-create the clipped line it exists to prevent (owner tweak 2026-08-24).
    const dx = DIALS.BUBBLE_SHIFT_X;
    const overRight = left + w * BUBBLE_REACH + dx > vw;
    const room = left - w * (BUBBLE_REACH - 1) - dx > 0;
    // `classList.toggle` is not in every DOM double; add/remove is.
    if (overRight && room) el.classList.add('bubble-left');
    else el.classList.remove('bubble-left');
    if (top < BUBBLE_RISE) el.classList.add('bubble-low');
    else el.classList.remove('bubble-low');
    /* ASKS: the strip rides the same two flips (the CSS mirrors it off these
     * exact root classes), and then gets the SAME viewport clamp the bubble's
     * reach test is - measured, because a strip is wider than a bark and the
     * reach heuristic above was calibrated for a 104px box. */
    clampAskStrip();
    clampBubble();
  }

  /** Record the CURRENT pixel position back into the fractions. */
  function commit(left, top) {
    const vp = viewport();
    fx0 = left / vp.w;
    fy0 = top / vp.h;
  }

  /* ---------------------- the chain player ------------------------------ */
  /* ONE runner, so there is exactly one thing to cancel and exactly one place
   * that knows whether a speech bubble is mid-line. */
  let current = null;              // {handle, protect}
  const timers = new Set();
  function later(fn, ms) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
    return id;
  }
  function killTimers() { for (const id of timers) clearTimeout(id); timers.clear(); }

  let blinkTimer = null;
  function stopBlink() { if (blinkTimer !== null) { clearInterval(blinkTimer); blinkTimer = null; } }

  /* ---------------------- the idle sway ---------------------------------
   * Five frames of ONE pose walked out and back through the centre. It is the
   * only loop the body png ever runs, it exists only at rest, and REDUCED
   * MOTION REFUSES IT OUTRIGHT - the design lock allows no breath/shiver loops
   * there and a sprite sway is the same law, not a loophole. Anything that
   * takes the glass (a chain, a say, a drag, a hide) stops it; `idle()` is the
   * only thing that starts it. */
  let swayTimer = null;
  let swayAt = 0;
  function stopSway() { if (swayTimer !== null) { clearTimeout(swayTimer); swayTimer = null; } }
  function swayHold(key) {
    if (key !== 'idle') return DIALS.SWAY_STEP_MS;
    const lo = DIALS.SWAY_CENTER_MIN_MS;
    const hi = Math.max(lo, DIALS.SWAY_CENTER_MAX_MS);
    return lo + Math.floor(Math.random() * (hi - lo + 1));
  }
  function startSway() {
    stopSway();
    if (reducedMotion() || hidden || !enabled) return;
    swayAt = 0;
    const step = () => {
      swayTimer = null;
      if (hidden || !enabled || busy() || dragging || reducedMotion()) return;
      const key = SWAY_CYCLE[swayAt % SWAY_CYCLE.length];
      swayAt += 1;
      setBodyFrame(key);
      swayTimer = setTimeout(step, swayHold(key));
    };
    // She is already on the centre frame when idle() calls this, so the first
    // beat is the centre pause, not a jump.
    swayTimer = setTimeout(step, swayHold('idle'));
  }

  function drawFace(text, o) {
    if (!painter || typeof painter.draw !== 'function') return;
    try { painter.draw(String(text == null ? '' : text), o || {}); }
    catch (e) { say('emi: draw threw - ' + ((e && e.message) || e)); }
  }

  /* THE BUBBLE IS ALSO WHERE SHE IS AUDIBLE. This function already knows the
   * only three states her voice cares about - typing, landed, gone - and EVERY
   * cancel path in this file funnels through the `null` branch, so hanging the
   * voice here (and nowhere else) is what makes "dismiss mid-babble cuts her
   * instantly" true by construction instead of by discipline. See trap 70. */
  /** The bubble's own MEASURED viewport clamp, in px, signed. See widget.css. */
  function setBubbleCx(px) {
    try {
      if (el.style && typeof el.style.setProperty === 'function') {
        el.style.setProperty('--emi-bubble-cx', Math.round(Number(px) || 0) + 'px');
      }
    } catch (e) { /* noop */ }
  }
  /**
   * KEEP THE LINE INSIDE THE WINDOW. `faceBubble`'s reach test picks the EAR
   * (trap 61) and it is a heuristic scaled off her BODY width - calibrated for
   * the desktop pair, a 150px EMI beside a 104px box. A phone runs an 86px EMI
   * beside a box that is allowed 168, so even the correct ear leaves the line
   * hanging off the edge. This is the same measured pull-back `clampAskStrip`
   * does for the strip, and it runs when a line LANDS rather than when she
   * moves, because the width is a property of the sentence.
   */
  function clampBubble() {
    try {
      if (!bubble || bubble.hidden || !el.getBoundingClientRect) return;
      setBubbleCx(0);
      /* IT IS MEASURED OFF `offsetWidth`, NOT off a rect, AND THAT IS THE WHOLE
       * TRICK. `.emi-bubble.pop` is a 280ms scale keyframe and
       * `getBoundingClientRect` reports the TRANSFORMED box (trap 74's twin
       * half), so a rect read on the frame the line lands is the box at ~60%
       * and the clamp comes out about a sixth of what it should be. The
       * offset* pair is the LAYOUT box and the pop cannot touch it. */
      const w = Number(bubble.offsetWidth) || 0;
      if (!w) return;
      const er = el.getBoundingClientRect();
      if (!er) return;
      const left = Number(er.left) + (Number(bubble.offsetLeft) || 0);
      const vp = viewport();
      const pad = 4;
      let dx = 0;
      if (left + w > vp.w - pad) dx = Math.round((vp.w - pad) - (left + w));
      if (left + dx < pad) dx = Math.round(pad - left);
      setBubbleCx(dx);
    } catch (e) { /* a clamp may never be the thing that throws */ }
  }

  function setBubble(text) {
    if (text == null || text === '') {
      bubble.hidden = true;
      bubble.textContent = '';
      bubble.classList.remove('dots', 'pop');
      setBubbleCx(0);
      if (vox) { try { vox.stop(); } catch (e) { /* a voice may never break a bubble */ } }
      return;
    }
    const s = String(text);
    const dots = /^\.{1,3}$/.test(s);
    bubble.textContent = s;
    bubble.hidden = false;
    bubble.classList.remove('dots', 'pop');
    if (dots) {
      bubble.classList.add('dots');
      if (vox) { try { vox.tick(); } catch (e) { /* noop */ } }
    } else {
      bubble.classList.add('pop'); stats.bubblesSeen += 1;
      /* THE MOOD IS DRAWN ONE LINE LATER. playChain hands us the bubble BEFORE
       * it hands the frame to `draw`, which is what resolves `bodyFrame` for a
       * makeSay line - so we ask on the microtask and read the pose she is
       * actually wearing. Same tick, same frame, chains.js untouched. */
      if (vox) {
        const speak = () => { try { vox.speak(s, { face: bodyFrame }); } catch (e) { /* noop */ } };
        if (typeof queueMicrotask === 'function') queueMicrotask(speak); else speak();
      }
    }
    clampBubble();
  }

  /* BODY MOVES GO ON THE ROOT. Agent A's keyframes animate `transform` on `.emi`
   * itself, which is also why widget.js positions her with left/top and never
   * with a transform of its own - the two would fight every frame. */
  let bodyTimer = null;
  function clearBody() {
    if (bodyTimer !== null) { clearTimeout(bodyTimer); bodyTimer = null; }
    for (const c of BODY_CLASSES) el.classList.remove(c);
  }
  function setBody(name) {
    clearBody();
    if (!name) return;
    const cls = BODY_ALIAS[name] || name;
    if (reducedMotion() && (cls === 'breath' || cls === 'shiver')) return;
    // Force a reflow so re-adding the same class re-runs the keyframe (trap 4's
    // lesson, one line instead of a whole reveal). `droop` fills FORWARDS, so a
    // move that is never taken off welds itself to the root.
    void el.offsetWidth;
    el.classList.add(cls);
    if (cls === 'breath') return;
    bodyTimer = setTimeout(() => {
      bodyTimer = null;
      el.classList.remove(cls);
      // Back to breathing, unless the player asked for stillness.
      if (!reducedMotion() && !hidden && enabled) { void el.offsetWidth; el.classList.add('breath'); }
    }, BODY_MS[name] || BODY_MS[cls] || 800);
  }

  function burst(kind) {
    if (!showFx || !kind) return;
    try { showFx(fxHost, kind); } catch (e) { say('emi: fx threw - ' + ((e && e.message) || e)); }
  }

  /** True while a SAY chain is typing or holding its line. */
  function saying() { return !!(current && current.protect); }
  /** True while any chain is running. */
  function busy() { return !!current; }

  function cancelChain() {
    /* OFF CHANNELS (W3): A SAY OUTRANKS THE GLASS. Every path that takes her
     * face - a chain, a say, a drag, a hide, a disable, destroy - already
     * funnels through here, which is why this is the ONE hook the wave needs
     * instead of ten. See emi/takeover.js law 3. */
    killTakeover();
    if (current && current.handle && typeof current.handle.cancel === 'function') {
      try { current.handle.cancel(); } catch (e) { /* noop */ }
    }
    current = null;
  }

  /* ==========================================================================
   * AN ASK OWNS THE GLASS UNTIL IT IS ANSWERED (owner bug 2026-08-25: "the how
   * can I call you prompt got removed because I think I triggered another
   * speech"). A question is a bubble that is WAITING FOR YOU, and the say law
   * one line down - a protected chain may be replaced by another protected one
   * - let any later bark walk straight over it. So an ask raises a second,
   * higher fence for as long as it is up:
   *
   *   a SAY that arrives while she is waiting is HELD (one slot, the newest
   *   wins) and released when the ask ends, if the moment has not gone stale;
   *   anything else - a raw face, a chain, an emote - is simply refused, the
   *   same answer it already gets over a live say.
   *
   * The hold is opened by the ask's own line (`opts.ask`) rather than by the
   * strip, because the question lands STRIP_LEAD_MS before the chips do and
   * that gap is the window the owner's bug actually fell through. It is closed
   * in exactly one place, `unmountAsk()`, which is the one function that ends
   * an ask however it ended. See trap 104.
   * ======================================================================== */
  let askHold = false;
  /** The one line waiting for the ask to finish: {chain, opts, at}. */
  let heldLine = null;
  /** Its own handle, NOT a `later()` one: `killTimers()` runs on every play. */
  let heldTimer = null;
  function askOwnsGlass() { return askHold || !!askLive; }
  function dropHeldLine() {
    heldLine = null;
    if (heldTimer !== null) { clearTimeout(heldTimer); heldTimer = null; }
  }
  /** Give the held line the glass, once the glass is actually free. */
  function pumpHeldLine() {
    heldTimer = null;
    if (!heldLine) return;
    if (Date.now() - heldLine.at >= DIALS.ASK_QUEUE_MS) { heldLine = null; return; }
    if (askOwnsGlass()) return;              // she is already waiting on the next one
    if (busy() || saying()) {                // a reaction line landed first, and it wins
      heldTimer = setTimeout(pumpHeldLine, DIALS.ASK_QUEUE_POLL_MS);
      return;
    }
    const h = heldLine;
    heldLine = null;
    play(h.chain, h.opts);
  }

  /**
   * Run one chain object. A protected chain (a SAY) refuses to be replaced by an
   * unprotected one - law 3 in the header. A live ASK outranks both.
   * @returns {boolean} true when it started
   */
  function play(chain, opts) {
    const o = opts || {};
    if (!chain || typeof playChain !== 'function' || !painter) return false;
    if (askOwnsGlass() && !o.ask) {
      /* ONE SLOT, NEWEST WINS - the same trade emi/index.js makes for the one
       * moment it buffers. A queue would replay a conversation nobody had. */
      if (o.protect) heldLine = { chain, opts: Object.assign({}, o), at: Date.now() };
      return false;
    }
    if (saying() && !o.protect && !o.force) return false;
    cancelChain();
    killTimers();
    stopBlink();
    stopSway();
    clearBody();
    restGaze();      // a performance owns the whole glass; the lean eases home
    /* THE POSE. A chain that declares `bodyFrame` (chains.js owns that table)
     * HOLDS it for the whole run - a pose that flickered per 90ms glitch frame
     * would read as a broken sprite, not as a mood. A chain that declares none
     * - every line `makeSay` builds - is resolved FRAME BY FRAME off the face
     * instead, which is what keeps the typing dots at `idle` and lands the pose
     * with the reaction face. `opts.bodyFrame` overrides both (the pet streak
     * borrows the GLEE chain but wants the pet pose, not the ceremony one). */
    const chainFrame = frameKey(o.bodyFrame) || frameKey(chain.bodyFrame);
    if (chainFrame) setBodyFrame(chainFrame);
    const handle = playChain(chain, {
      draw: (text, fo) => { if (!chainFrame) setBodyFrame(frameForFace(text)); drawFace(text, fo); },
      bubble: (b) => setBubble(b),
      body: (cls) => setBody(cls),
      fx: (kind) => burst(kind),
      done: () => {
        current = null;
        if (typeof o.onDone === 'function') { try { o.onDone(); } catch (e) { /* noop */ } }
        idle();
      },
    });
    current = { handle, protect: !!o.protect, ask: !!o.ask };
    if (o.ask) askHold = true;
    stampActivity(o.protect ? 'say' : 'play');
    return true;
  }

  /** Draw one raw face string and hold it, then fall back to idle. */
  function raw(text, opts) {
    const o = opts || {};
    if (!painter) return false;
    /* A FACE IS A REACTION, AND A REACTION TO A MOMENT THAT PASSED IS WORSE
     * THAN NO REACTION - so this one is refused outright and never queued.
     * `force` cannot buy past a live ask; the ask engine's own `-_-` runs
     * AFTER `unmountAsk()` has already closed the hold. */
    if (askOwnsGlass() && !o.ask) return false;
    if (saying() && !o.force) return false;
    cancelChain();
    killTimers();
    stopBlink();
    stopSway();
    restGaze();
    if (o.clearBubble !== false) setBubble(null);
    setBodyFrame(frameKey(o.bodyFrame) || frameForFace(text));
    drawFace(text, o.frameOpts || {});
    if (o.body) setBody(o.body);
    if (o.fx) burst(o.fx);
    const hold = typeof o.hold === 'number' ? o.hold : DIALS.RAW_HOLD_MS;
    if (hold > 0) later(() => idle(), hold);
    stampActivity('raw');
    return true;
  }

  /** 0_0 + blink + breath. The resting state; only runs when nothing else does. */
  function idle() {
    cancelChain();
    killTimers();
    stopBlink();
    stopSway();
    setBubble(null);
    clearBody();
    // ARMS DOWN AT REST. This runs even faceless: the body png is the half of
    // EMI that never needed a 2d context.
    setBodyFrame('idle');
    if (!painter || hidden || !enabled) return;
    drawFace(IDLE_FACE, {});
    if (!reducedMotion()) el.classList.add('breath');
    blinkTimer = setInterval(() => {
      if (busy() || hidden || !enabled || dragging) return;
      drawFace(BLINK_FACE, {});
      later(() => { if (!busy() && !dragging) drawFace(IDLE_FACE, {}); }, DIALS.BLINK_HOLD_MS);
      emitGesture('blinkIdle');
    }, DIALS.BLINK_EVERY_MS);
    startSway();
  }

  /* ---------------------- gestures (the voice's ear) --------------------
   * A READ-ONLY TAP on the pointer verbs. EMI's own reaction runs first and is
   * completely unchanged; this only lets emi/voice.js hear that a pet, a fling
   * or an idle blink happened and decide whether the moment also earns a line.
   * A subscriber that throws is swallowed here, because a listener must never
   * be able to reach into a pointer handler. */
  const gestureSubs = new Set();
  function onGesture(cb) {
    if (typeof cb !== 'function') return () => {};
    gestureSubs.add(cb);
    return () => gestureSubs.delete(cb);
  }
  function emitGesture(kind, detail) {
    /* THE HEARTBEAT'S EAR (2026-08-25). A player verb IS activity, so it resets
     * the metronome exactly as one of her own acts does - the heartbeat only
     * ever fills silence, it never competes with a hand on the mouse. The ONE
     * exclusion is `blinkIdle`, which IS the unattended idle blink: counting it
     * would re-stamp the clock every 5.2s and the beat could never come due. */
    if (kind !== 'blinkIdle') stampActivity('gesture:' + kind);
    if (!gestureSubs.size) return;
    for (const cb of gestureSubs) {
      try { cb(kind, detail || {}); } catch (e) { /* noop */ }
    }
  }

  /* ---------------------- the activity tap (HEARTBEAT, 2026-08-25) ------
   * ONE stamp for "something visible just happened", so `emi/heartbeat.js` can
   * measure silence without twenty call sites learning about it. It is fired
   * from the FOUR choke points every visible verb funnels through - `play`
   * (every chain, every say, every emote), `raw` (a held face), the deck's
   * `screenTakeover` (trap 77: the second canvas is its own path and does NOT
   * run through play) and `apparate` (a field trip) - plus every player gesture
   * above. A subscriber that throws is swallowed here: a listener may never
   * reach into a pointer handler or a chain runner. */
  const activitySubs = new Set();
  function onActivity(cb) {
    if (typeof cb !== 'function') return () => {};
    activitySubs.add(cb);
    return () => activitySubs.delete(cb);
  }
  function stampActivity(kind) {
    if (!activitySubs.size) return;
    for (const cb of activitySubs) {
      try { cb(String(kind || '')); } catch (e) { /* noop */ }
    }
  }

  /* ---------------------- pointer: drag / pet / hide -------------------- */
  let dragging = false;
  let pressing = false;
  let pressId = null;
  let pressAt = 0;
  let grabX = 0, grabY = 0;         // pointer offset inside EMI
  let startX = 0, startY = 0;       // where the press began (the 6px threshold)
  let lastX = 0, lastY = 0, lastT = 0;
  let fastSince = 0;
  let wasFling = false;
  /* WHICH FACE THE DRAG IS CURRENTLY WEARING. `onMove` runs on every pointermove
   * and drawing is a full canvas repaint (measure + stroke + fill), so it must
   * only fire when the expression actually CHANGES, not for every frame she
   * happens to be travelling fast. */
  let dragFace = null;
  /* THE TWO THINGS A GESTURE HAS TO PROVE (field bug, 2026-08-24). `beginDrag`
   * also fires on the TIME threshold, so a slow click that never moved - the
   * click that focuses the window, say, landing on EMI's corner - used to end
   * as a "drag" that committed her existing position and emitted a dropAt with
   * nobody touching anything. A gesture now needs a pointer that TRAVELLED at
   * least DRAG_PX, and an event the platform did not mark synthetic. EMI's own
   * reactions are unchanged: this gates only what the VOICE is told. */
  let dragMax = 0;            // furthest the pointer got from the press point
  let dragTold = false;       // 'drag' is announced once, when it becomes real
  let trusted = true;         // ev.isTrusted, when the platform reports one
  let armTimer = null;
  let petTimes = [];
  let petCooldownUntil = 0;
  /* THE DANGLE. Carried, she tilts a few degrees against the direction of
   * travel and springs upright on release - an inline `rotate` on the root,
   * which is safe exactly while dragging: no body-move keyframe runs then, and
   * the ones that run at release (bounce/thud) out-rank an inline transform for
   * as long as they play. Smoothed so a jittery hand does not read as a shiver. */
  let dangleV = 0;            // smoothed horizontal speed, px/ms
  let dangleDeg = 0;          // the tilt currently applied

  function clearDangle(immediate) {
    dangleV = 0;
    dangleDeg = 0;
    if (!el.style) return;
    if (immediate || reducedMotion()) {
      el.style.transition = '';
      el.style.transform = '';
      return;
    }
    el.style.transition = 'transform ' + DIALS.DANGLE_SETTLE_MS + 'ms cubic-bezier(.2,1.5,.4,1)';
    el.style.transform = '';
    // Untracked on purpose: killTimers() must not be able to strand the
    // transition on the root (it would slow every later dangle, nothing more).
    setTimeout(() => { try { el.style.transition = ''; } catch (e) { /* noop */ } },
      DIALS.DANGLE_SETTLE_MS + 50);
  }

  /** Which ninth of the viewport a point is in: z0..z8 row-major, plus the row band. */
  function zoneOf(x, y, vp) {
    const col = clamp(Math.floor(x * 3 / Math.max(1, vp.w)), 0, 2);
    const row = clamp(Math.floor(y * 3 / Math.max(1, vp.h)), 0, 2);
    return { z: row * 3 + col, row: row === 0 ? 'top' : (row === 1 ? 'mid' : 'bottom') };
  }

  function disarmHold() {
    if (armTimer !== null) { clearTimeout(armTimer); armTimer = null; }
  }

  /** Paint a drag reaction, but only when it is not already on the glass. */
  function setDragFace(text) {
    if (dragFace === text) return;
    dragFace = text;
    // The pose follows the face the way a raw hold's does: `@_@` is unpaired, so
    // a carry rests at `idle`; `>.<` is paired, so a FLING wears `shock`.
    setBodyFrame(frameForFace(text));
    drawFace(text, {});
  }

  /* A PRESS THAT NEVER GETS ITS pointerup. `hide()` and `setEnabled(false)` can
   * both take EMI out from under a live finger (the API is callable from any
   * moment), and a latched `pressing`/`dragging` would then let the NEXT
   * pointerup commit a position read from a hidden element - rect 0,0, i.e. she
   * teleports to the top-left corner the next time she is restored. */
  /** Did this press actually become a DRAG a human would recognise? */
  function realDrag() { return trusted && dragMax >= DIALS.DRAG_PX; }

  function endPress() {
    if (pressId !== null) {
      try { if (el.releasePointerCapture) el.releasePointerCapture(pressId); } catch (e) { /* noop */ }
    }
    pressing = false;
    dragging = false;
    pressId = null;
    fastSince = 0;
    wasFling = false;
    dragFace = null;
    dragMax = 0;
    dragTold = false;
    disarmHold();
    clearDangle(true);
    poiForget();                     // W2a: the drag's fixture snapshot dies with the drag
    el.classList.remove('dragging', 'armed');
  }

  function inX(ev) {
    const tgt = ev && ev.target;
    if (!tgt) return false;
    if (tgt === xBtn) return true;
    return !!(tgt.closest && tgt.closest('.emi-x'));
  }

  /* ASKS: A PRESS ON A CHIP IS THE CHIP'S, NOT EMI'S. The strip lives INSIDE
   * `.emi`, so without this every chip press would run the drag handler below -
   * which calls `setPointerCapture` on the root and `preventDefault()`, and
   * between them the browser retargets the release and never fires the chip's
   * own `click` at all. Caught by the browser pass; the DOM double cannot see
   * it, because it has neither pointer capture nor compatibility events. The
   * x button has had exactly this guard since day one, for the same reason. */
  function inAsk(ev) {
    const tgt = ev && ev.target;
    if (!tgt) return false;
    if (tgt === askStrip) return true;
    if (tgt.closest) return !!tgt.closest('.emi-ask');
    return !!(askStrip.contains && askStrip.contains(tgt));
  }

  function onDown(ev) {
    if (!enabled || hidden) return;
    /* TOUCH ALWAYS WINS (W2a, trap 75). A finger on her at ANY point of a field
     * trip ends it on the spot - she stays where the trip had put her, upright,
     * and the press below carries on into a perfectly ordinary drag. */
    cancelTrip({ stay: true });
    // The x is a real button: let it have its own click, and never start a drag
    // from it (a dismiss that could turn into a fling is a trap, not a feature).
    if (inX(ev)) return;
    // ASKS: and a chip is a real button too - see inAsk(). No drag, no pet, no
    // capture, no preventDefault: the strip's own click is the whole event.
    if (inAsk(ev)) return;
    pressing = true;
    dragging = false;
    wasFling = false;
    pressId = ev.pointerId;
    pressAt = nowMs();
    const r = el.getBoundingClientRect ? el.getBoundingClientRect() : { left: 0, top: 0 };
    grabX = ev.clientX - r.left;
    grabY = ev.clientY - r.top;
    startX = ev.clientX; startY = ev.clientY;
    lastX = ev.clientX; lastY = ev.clientY; lastT = pressAt;
    fastSince = 0;
    dragMax = 0;
    dragTold = false;
    trusted = ev.isTrusted !== false;
    try { if (el.setPointerCapture) el.setPointerCapture(ev.pointerId); } catch (e) { /* noop */ }
    // PRESS-AND-HOLD ARMS THE x. Touch has no hover, so this is the only way to
    // reach the dismiss affordance with a finger.
    disarmHold();
    armTimer = setTimeout(() => { armTimer = null; el.classList.add('armed'); }, DIALS.HOLD_MS);
    // Inside `.emi` only - this is the one place a preventDefault is legal here,
    // and it exists to stop the browser's native image-drag ghost.
    if (ev.cancelable && typeof ev.preventDefault === 'function') ev.preventDefault();
  }

  function beginDrag() {
    dragging = true;
    /* ASKS (a06): the player picking her up out-votes "the other side today".
     * A session spot that survived a drag would fight the drop on every
     * resize, and the drop is always the more recent word. */
    askSpot = null;
    stats.drags += 1;
    touchSeen();
    /* W3 P1-19: SHE IS OFF THE GROUND. A soft pad up a tone - the lightest cue
     * in the file, because a lift is a beginning and the drop is the event. It
     * fires on the DRAG, never on the press: a tap that never travelled is a
     * pet and already has its own answer. */
    sfx('pad', 0.08, 1.2);
    el.classList.add('dragging');
    // The reaction is a raw face, not a chain: a chain would fight the next
    // frame of movement. A live SAY keeps the glass (law 3).
    poiSnapshot();                   // W2a: measure the fixtures ONCE, not per move
    if (!saying()) { cancelChain(); killTimers(); stopBlink(); stopSway(); clearBody(); setDragFace(carryFace()); }
  }

  function onMove(ev) {
    if (!pressing || ev.pointerId !== pressId) return;
    const dx = ev.clientX - lastX;
    const dy = ev.clientY - lastY;
    const t = nowMs();
    const dt = Math.max(1, t - lastT);
    const moved = Math.hypot(ev.clientX - lastX, ev.clientY - lastY);

    // The threshold is measured from where the PRESS began, never from the
    // previous move - a slow drag never crosses a per-move delta.
    const total = Math.hypot(ev.clientX - startX, ev.clientY - startY);
    if (total > dragMax) dragMax = total;
    if (!dragging) {
      if (total > DIALS.DRAG_PX || (t - pressAt) > DIALS.DRAG_MS) beginDrag();
    }
    // ...and the voice only hears about it once it is a journey, not a jiggle.
    if (dragging && !dragTold && realDrag()) { dragTold = true; emitGesture('drag'); }
    if (dragging) {
      const vp = viewport();
      const s = sizePx();
      const left = clamp(ev.clientX - grabX, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.w - s.w - DIALS.MARGIN));
      const top = clamp(ev.clientY - grabY, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.h - s.h - DIALS.MARGIN));
      el.style.left = Math.round(left) + 'px';
      el.style.top = Math.round(top) + 'px';
      faceBubble(left, top, s.w, vp.w);

      // FLUNG? speed over the dial, sustained. `>.<` while it lasts. setDragFace
      // swallows the repeats, so a sustained fling repaints the canvas ONCE
      // instead of once per pointermove.
      const speed = Math.hypot(dx, dy) / dt;
      if (speed > DIALS.FLING_SPEED) {
        if (!fastSince) fastSince = t;
        else if (t - fastSince > DIALS.FLING_MS && !saying()) { wasFling = true; setDragFace(FLING_FACE); }
      } else if (fastSince) {
        fastSince = 0;
        if (!saying()) setDragFace(carryFace());
      }

      /* FIELD-TRIP ANTICIPATION (W2a). Carried over a fixture she has a line
       * about, she lights up. ENTER/LEAVE ONLY: the rects were measured once at
       * beginDrag and the repaint rides setDragFace's dedupe, so a pointermove
       * over open ground costs a handful of number compares and nothing else. */
      if (poiCache) {
        const onPoi = overPoi(left, top);
        if (onPoi !== poiOver) {
          poiOver = onPoi;
          if (!saying() && dragFace !== FLING_FACE) setDragFace(carryFace());
        }
      }

      // THE DANGLE: lean against the travel, clamped and smoothed. Style is
      // only written when the tilt moved a visible amount.
      if (!reducedMotion()) {
        dangleV = dangleV * 0.8 + (dx / dt) * 0.2;
        const deg = clamp(-dangleV * DIALS.DANGLE_K, -DIALS.DANGLE_MAX_DEG, DIALS.DANGLE_MAX_DEG);
        if (Math.abs(deg - dangleDeg) > 0.3) {
          dangleDeg = deg;
          try { el.style.transform = 'rotate(' + deg.toFixed(1) + 'deg)'; } catch (e) { /* noop */ }
        }
      }
    }
    if (moved > 0) { lastX = ev.clientX; lastY = ev.clientY; lastT = t; }
  }

  function onUp(ev) {
    if (!pressing || (pressId !== null && ev.pointerId !== pressId)) return;
    pressing = false;
    disarmHold();
    try { if (el.releasePointerCapture) el.releasePointerCapture(ev.pointerId); } catch (e) { /* noop */ }
    const held = nowMs() - pressAt;
    pressId = null;

    if (dragging) {
      dragging = false;
      dragFace = null;
      el.classList.remove('dragging');
      clearDangle(false);
      const r = el.getBoundingClientRect ? el.getBoundingClientRect() : { left: 0, top: 0 };
      commit(r.left, r.top);
      remeasureAsk();   // a question survives a drag now; its clamp does not
      /* W3 P1-19: SHE LANDS. The body move here is already NAMED `thud` for a
       * throw and `bounce` for a set-down, so the ear gets the same two things:
       * a low sawtooth knock pitched down for the fling, a lighter one for the
       * ordinary drop. It rides the DROP and not the animation, so a reduced-
       * motion player - who gets no move at all - still hears her land. */
      sfx(wasFling ? 'thud' : 'bump', wasFling ? 0.16 : 0.12, wasFling ? 0.8 : 1);
      if (wasFling) stats.flings += 1;
      // SETTLE: ^_^ for a beat, then back to resting. The landing move is the
      // playbook's BOUNCE, or a THUD when she was thrown.
      if (!saying()) {
        cancelChain(); killTimers(); stopBlink(); stopSway();
        setBodyFrame(frameForFace(SETTLE_FACE));
        drawFace(SETTLE_FACE, {});
        if (!reducedMotion()) setBody(wasFling ? 'thud' : 'bounce');
        later(() => idle(), DIALS.SETTLE_MS);
      }
      const flung = wasFling;
      const travelled = realDrag();
      wasFling = false;
      if (travelled) {
        // WHERE SHE LANDED, in viewport coordinates: her CENTRE, which is the
        // point a hit-test should ask about. voice.js does the asking. A press
        // that never travelled is NOT a drop - she is exactly where she was.
        const sz = sizePx();
        const px = Math.round(r.left + sz.w / 2);
        const py = Math.round(r.top + sz.h / 2);
        // ...and the SPOT MEMORY: which ninth of the window she was put down
        // in, counted for life. The favourite-spot beats read the count off
        // this payload, so the voice never has to open the widget's blob.
        const zi = zoneOf(px, py, viewport());
        const zk = 'z' + zi.z;
        stats.zones[zk] = (stats.zones[zk] || 0) + 1;
        // ONE write per interaction, on the END of it (never per pointermove).
        save();
        if (flung) emitGesture('fling');
        emitGesture('dropAt', { x: px, y: py, zone: zi.z, zoneRow: zi.row, zoneCount: stats.zones[zk] });
      } else {
        save();
      }
      return;
    }
    // NOT A DRAG. A short press is a PET; a long hold was the x-arming gesture
    // and is deliberately not a pet (the player was reaching for the dismiss).
    el.classList.remove('dragging');
    if (held < DIALS.HOLD_MS && ev.isTrusted !== false) pet();
  }

  function onCancel() {
    if (!pressing) return;
    const wasDragging = dragging;
    // The rect has to be read BEFORE endPress clears the classes - a cancel that
    // banked nothing would lose the whole drag.
    const r = wasDragging && el.getBoundingClientRect ? el.getBoundingClientRect() : null;
    endPress();
    if (wasDragging) {
      commit(r ? r.left : 0, r ? r.top : 0);
      remeasureAsk();   // same law as the ordinary drop
      save();
      if (!saying()) idle();
    }
  }

  /** A pet: positive cycle; PET_TARGET inside the window buys the glee chain. */
  function pet() {
    if (!enabled || hidden) return;
    // LAW 3: a line in flight is never cut for a head-pat. Ignore, do not queue -
    // a reaction that lands four seconds late reads as a glitch.
    if (saying()) return;
    /* OFF CHANNELS (W3): the press that CAUGHT her mid-channel is not also a
     * head-pat. One-shot, false on every ordinary day. */
    if (takeoverAtePet()) return;
    stats.pets += 1;
    touchSeen();
    /* W3 P1-19: EVERY PAT IS ANSWERED, INCLUDING THE COOLED ONE. A small pop,
     * under the drop cues, on the guaranteed floor - the third pat then STACKS
     * its chime over the top of this rather than replacing it, which is the
     * reward rule the plan writes down (a payoff never removes the answer the
     * input already earned). */
    sfx('pop', 0.08, 1);
    const t = nowMs();
    if (t < petCooldownUntil) {
      // Spam guard: she still notices you, she just does not do the whole show.
      if (CHAINS && CHAINS.wink) play(CHAINS.wink, { bodyFrame: 'pet' });
      else raw('^_~', { hold: 500, bodyFrame: 'pet' });
      save();
      emitGesture('pet', { cooled: true });
      return;
    }
    petTimes = petTimes.filter((p) => t - p < DIALS.PET_WINDOW_MS);
    petTimes.push(t);
    if (petTimes.length >= DIALS.PET_TARGET) {
      petTimes = [];
      petCooldownUntil = t + DIALS.PET_COOLDOWN_MS;
      stats.petStreaks3 += 1;
      /* W3 P1-19: ...and the THIRD one pays. A clean bell over the pop above:
       * the rarest sound she makes for a touch, and the only one a player can
       * work out how to earn. */
      sfx('chime', 0.14, 1);
      // THE LOCK SAYS THE THIRD PET LANDS ON (≧◡≦) - that is `glee`, not
      // `love` (which ends on the lovestruck kaomoji and is a different beat).
      /* THE POSE IS THE PET ONE, not the ceremony one. `glee` is shared: it is
       * also the streak-STAMP beat, where the arms-up frame is the right answer
       * - so the override rides the CALL SITE, never the chain. */
      const cycle = CHAINS && (CHAINS.glee || CHAINS.love);
      if (cycle) play(cycle, { bodyFrame: 'pet' });
      else raw('(≧◡≦)', { hold: 1400, fx: 'hearts', body: 'bounce', bodyFrame: 'pet' });
      save();
      emitGesture('pet');
      emitGesture('petStreak3');
      return;
    }
    raw(SETTLE_FACE, { hold: 900, fx: 'hearts', body: reducedMotion() ? null : 'bounce', bodyFrame: 'pet' });
    save();
    emitGesture('pet');
  }

  /* ---------------------- the desk toy (Counter Stock) ------------------- */
  /** Has tonight's line been spent? SESSION-LOCAL and deliberately unpersisted:
   *  "once per session" is the contract, and a flag on the blob would make the
   *  toy a thing you can only ever hear from once. */
  let toySpent = false;

  /**
   * A POKE AT THE PROP. Answered every time - the W3 law is that an input which
   * makes no sound reads as an input the page did not get - but answered with a
   * LINE only once a sitting: a mascot who says the same sentence every time
   * you tap her desk is a mascot you stop tapping.
   *
   * The three guards are pet()'s, for pet()'s reasons: LAW 3 (a say is never cut
   * mid-line), trap 104 (an ask owns the glass until it is answered) and W2a (she
   * is not at her desk to be poked while she is across the quad).
   */
  function pokeToy() {
    if (!enabled || hidden || toy.hidden) return false;
    if (saying() || askOwnsGlass() || tripping()) return false;
    touchSeen();
    /* Her own bus, over her blipese and under every game one-shot, exactly like
     * the pat's pop. A little higher, because it is a wind-up and not a hand. */
    sfx('pop', 0.08, 1.5);
    emitGesture('toy', { spent: toySpent });
    if (toySpent) {
      // She has already told you about it tonight. A look is the whole answer.
      if (CHAINS && CHAINS.wink) return play(CHAINS.wink, { bodyFrame: 'smug' });
      return raw('^_~', { hold: 500, bodyFrame: 'smug' });
    }
    const i = toyLineIndex(isoDay() || '');
    const row = TOY_LINES[i] || TOY_LINES[0];
    const line = TOY_TEXT[i] || row.line;
    toySpent = true;
    /* THE POSE RIDES THE FACE, not an override: `makeSay` resolves frame by
     * frame, so the typing dots stay at `idle` and (¬‿¬)/(◠‿◠) lands the smug
     * or the pet pose WITH the line. That is the same road every bark takes. */
    if (typeof makeSay === 'function' && painter) {
      let chain = null;
      try { chain = makeSay(line, row.face, sayHoldMs(line)); }
      catch (e) { chain = null; say('emi: makeSay threw on the toy line'); }
      if (chain && play(chain, { protect: true })) return true;
    }
    // No renderer, or the chain was refused: the POSE still lands, wordless.
    return raw(row.face, { hold: DIALS.RAW_HOLD_MS });
  }

  /* ---------------------- hide / dock ----------------------------------- */
  function hide(opts) {
    if (hidden) return;
    const o = opts || {};
    hidden = true;
    accrueVisible();
    if (!o.silent) { stats.hides += 1; touchSeen(); }
    // A live press has to be let go BEFORE she goes: a latched drag would let the
    // next pointerup commit a position read off a display:none element.
    endPress();
    cancelTrip();                    // W2a: dismissed mid-trip = home first, then the dock
    /* ASKS: A DOCKED EMI IS A SCREEN CHANGE THAT LEGITIMATELY KILLS HER, and it
     * is the one such change a SILENT (API) hide can make without the ask
     * engine hearing a gesture - so the strip is taken down here rather than
     * left hanging inside a `display:none` root. `gone` is what tells the
     * engine the question ended, and it spends no cadence. */
    emitGesture('gone', { reason: 'hide' });
    unmountAsk(false); dropHeldLine();
    cancelChain(); killTimers(); stopBlink(); stopSway(); clearBody(); setBubble(null);
    stopToyLoop();                   // COUNTER STOCK: no interval inside display:none
    clearApproachTimers();
    restGaze();
    try { canvas.style.transform = ''; } catch (e) { /* noop */ }
    el.hidden = true;
    dock.hidden = false;
    /* THE ONE-SHOT HINT. The dock is 28px and .35 opacity in a corner, so the
     * very first dismissal says where she went - once, ever, and only for the x
     * (an API hide is the shell's doing, not a thing the player needs told). */
    if (o.fromX && !hintShown) {
      hintShown = true;
      try { shout(HINT_LINE); } catch (e) { /* a toast may never hold a dismiss */ }
    }
    save(true);
    if (!o.silent) emitGesture('hide', { fromX: !!o.fromX });
  }

  function show(opts) {
    if (!hidden && el.hidden === false) return;
    hidden = false;
    if (!(opts && opts.silent)) { stats.dockRestores += 1; touchSeen(); }
    el.hidden = false;
    dock.hidden = true;
    place();
    beginVisible();
    startToyLoop();                  // COUNTER STOCK: the prop picks its loop back up
    idle();
    save(true);
    if (!(opts && opts.silent)) emitGesture('restore');
  }

  xBtn.addEventListener('click', (ev) => {
    // Inside `.emi`: stopping this one is legal and necessary (the pet handler
    // must not also fire). Nothing outside the widget is touched.
    if (ev && typeof ev.stopPropagation === 'function') ev.stopPropagation();
    hide({ fromX: true });
  });
  // The x owns its own pointer stream so a press on it can never start a drag.
  xBtn.addEventListener('pointerdown', (ev) => { if (ev && ev.stopPropagation) ev.stopPropagation(); });
  dock.addEventListener('click', () => show());

  el.addEventListener('pointerdown', onDown);
  el.addEventListener('pointermove', onMove);
  el.addEventListener('pointerup', onUp);
  el.addEventListener('pointercancel', onCancel);
  el.addEventListener('pointerleave', () => { if (!pressing) el.classList.remove('armed'); });

  /* ---------------------- perception ------------------------------------
   * She notices you BEFORE you touch her. Three pieces, all read off one
   * document-level pointermove (rAF-shaped, ~16 samples/s):
   *
   *   GAZE      the face leans a few px toward the cursor. NOT a canvas
   *             repaint - the glyph is expensive to draw (widget header), so
   *             the lean is a CSS transform on the canvas element, which is
   *             free. Idle-only: a chain, a say or a drag eases it home.
   *   APPROACH  cursor near her edge = a perk (o_o); arriving FAST = the
   *             glance chain. One per APPROACH_COOLDOWN_MS.
   *   LINGER    hovering without committing = expectant, then one look-away
   *             if the pet never comes. The episode resets when you leave.
   *
   * All of it is a spectator: it plays through raw()/play() like every other
   * reaction, so law 3 (a SAY is never cut) holds without a special case, and
   * the voice hears `approach`/`hoverLinger` through the same gesture tap as
   * every pointer verb (no pools yet - wave 2b's writing slot). */
  let gazeTX = 0, gazeTY = 0, gazeX = 0, gazeY = 0, gazeRaf = null;
  /* ASKS (a08): a standing lean toward the board, -1..1, cleared at the end of
   * the class. It is ADDED to the cursor lean and clamped with it, so "front
   * row" reads as a tilt she is holding, never as a face stuck off-centre. */
  let askGaze = 0;
  let apInside = false, apCoolUntil = 0, apPets0 = 0;
  let apLingerTimer = null, apAwayTimer = null;
  let apLastX = 0, apLastY = 0, apLastT = 0;

  function gazeActive() {
    return !!painter && !hidden && enabled && !dragging && !busy() && !reducedMotion();
  }
  function gazeStep() {
    gazeRaf = null;
    /* ASKS: the standing bias rides the same clamp the cursor lean does, so
     * "front row" can never push the glyph further than GAZE_MAX_PX. */
    const bias = askGaze * DIALS.GAZE_MAX_PX;
    const ax = gazeActive() ? clamp(gazeTX + bias, -DIALS.GAZE_MAX_PX, DIALS.GAZE_MAX_PX) : 0;
    const ay = gazeActive() ? gazeTY : 0;
    gazeX += (ax - gazeX) * DIALS.GAZE_EASE;
    gazeY += (ay - gazeY) * DIALS.GAZE_EASE;
    // SETTLED = STOP. The loop only runs while the lean is travelling; a
    // pointermove (or restGaze) nudges it back to life. A resting rAF that
    // rewrote the same transform every frame would be the repaint-per-move
    // mistake in a nicer hat.
    if (Math.abs(gazeX - ax) + Math.abs(gazeY - ay) < 0.05) {
      gazeX = ax; gazeY = ay;
      try { canvas.style.transform = ax === 0 && ay === 0 ? '' : 'translate(' + ax.toFixed(2) + 'px,' + ay.toFixed(2) + 'px)'; } catch (e) { /* noop */ }
      return;
    }
    try { canvas.style.transform = 'translate(' + gazeX.toFixed(2) + 'px,' + gazeY.toFixed(2) + 'px)'; } catch (e) { /* noop */ }
    if (typeof requestAnimationFrame === 'function') gazeRaf = requestAnimationFrame(gazeStep);
  }
  /** Wake the easing loop. (Was `nudgeGaze` until the heartbeat wave took that
   *  name for the PUBLIC verb below - this one only kicks the rAF.) */
  function kickGaze() {
    if (gazeRaf == null && typeof requestAnimationFrame === 'function') {
      gazeRaf = requestAnimationFrame(gazeStep);
    }
  }
  /* THE GAZE NUDGE (HEARTBEAT, 2026-08-25). "A nudge in a direction" is a
   * whole act on the wheel, and it is the CHEAPEST one she has: no chain, no
   * bubble, no repaint - the same CSS translate on the canvas ELEMENT the
   * cursor lean already rides (trap 71). `dx`/`dy` are a DIRECTION in -1..1,
   * scaled by GAZE_MAX_PX, held `ms` and then eased home.
   *
   * Reduced motion REFUSES it outright rather than snapping: W1's gaze is off
   * under `prefers-reduced-motion` and a heartbeat may not smuggle it back in.
   * It also refuses over any live verb, because a lean that survives a say is
   * a face stuck off-centre. */
  let gazeNudgeTimer = null;
  function clearGazeNudge() {
    if (gazeNudgeTimer !== null) { clearTimeout(gazeNudgeTimer); gazeNudgeTimer = null; }
  }
  function nudgeGaze(dx, dy, ms) {
    if (!painter || hidden || !enabled || dragging || pressing) return false;
    if (busy() || saying() || reducedMotion()) return false;
    const x = Number(dx), y = Number(dy);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return false;
    if (x === 0 && y === 0) return false;
    clearGazeNudge();
    gazeTX = clamp(x, -1, 1) * DIALS.GAZE_MAX_PX;
    gazeTY = clamp(y, -1, 1) * DIALS.GAZE_MAX_PX;
    kickGaze();
    const hold = Number.isFinite(Number(ms)) && Number(ms) > 0 ? Math.round(Number(ms)) : 1500;
    gazeNudgeTimer = setTimeout(() => { gazeNudgeTimer = null; restGaze(); }, hold);
    return true;
  }
  /** Ease the lean home NOW (a hide, a disable, a drag taking over). The
   *  ASKS bias is deliberately NOT cleared here: it belongs to the class, and
   *  `gazeActive()` already parks the whole lean at 0 while anything else owns
   *  the glass, so it simply comes back when she is idle again. */
  function restGaze() {
    /* A HEARTBEAT NUDGE IS EASED HOME BY WHATEVER TAKES THE GLASS NEXT. Every
     * path that owns her face (play / raw / idle / hide / disable / a drag)
     * already calls this, so the release timer is cleared HERE and nowhere
     * else - a nudge that outlived the reaction after it would be a lean
     * nobody asked for. */
    clearGazeNudge();
    gazeTX = 0; gazeTY = 0;
    kickGaze();
  }

  function clearApproachTimers() {
    if (apLingerTimer !== null) { clearTimeout(apLingerTimer); apLingerTimer = null; }
    if (apAwayTimer !== null) { clearTimeout(apAwayTimer); apAwayTimer = null; }
  }
  /** May a perception face go up right now? Never over a chain, a say, a press. */
  function canPerk() {
    return !!painter && !hidden && enabled && !pressing && !dragging && !busy() && !saying();
  }

  function onDocMove(ev) {
    if (!enabled || hidden || !ev) return;
    const t = nowMs();
    if (t - apLastT < 60) return;                       // ~16 samples/s is plenty
    const px = apLastX, py = apLastY, pt = apLastT;
    apLastX = ev.clientX; apLastY = ev.clientY; apLastT = t;

    let r = null;
    try { r = el.getBoundingClientRect ? el.getBoundingClientRect() : null; } catch (e) { /* noop */ }
    if (!r || !r.width) return;
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    const dx = ev.clientX - cx;
    const dy = ev.clientY - cy;
    const d = Math.hypot(dx, dy);

    // GAZE: the lean is proportional and capped, and the loop eases it home
    // on its own the moment gazeActive() goes false.
    clearGazeNudge();          // the cursor outranks a heartbeat's idle look
    gazeTX = clamp(dx / DIALS.GAZE_DIV, -DIALS.GAZE_MAX_PX, DIALS.GAZE_MAX_PX);
    gazeTY = clamp(dy / DIALS.GAZE_DIV, -DIALS.GAZE_MAX_PX, DIALS.GAZE_MAX_PX);
    kickGaze();

    // APPROACH: measured from her EDGE, so a bigger EMI is not a bigger doorbell.
    const inside = d < r.width / 2 + DIALS.APPROACH_PX;
    if (inside === apInside) return;
    apInside = inside;
    if (!inside) { clearApproachTimers(); return; }

    if (t < apCoolUntil) return;
    apCoolUntil = t + DIALS.APPROACH_COOLDOWN_MS;
    apPets0 = stats.pets;
    // Arriving fast earns the GLANCE (she tracks the fly-by); walking up earns
    // the quiet perk. Both are idle-frame on purpose - noticing is not a beat.
    const speed = pt > 0 ? Math.hypot(ev.clientX - px, ev.clientY - py) / Math.max(1, t - pt) : 0;
    if (canPerk()) {
      if (speed > DIALS.GLANCE_SPEED && CHAINS && CHAINS.glance) play(CHAINS.glance, { bodyFrame: 'idle' });
      else raw('o_o', { hold: 900, bodyFrame: 'idle' });
    }
    emitGesture('approach', { fast: speed > DIALS.GLANCE_SPEED });

    clearApproachTimers();
    apLingerTimer = setTimeout(() => {
      apLingerTimer = null;
      if (!apInside || !canPerk()) return;
      raw('^_^', { hold: 1100, bodyFrame: 'idle' });
      emitGesture('hoverLinger');
      apAwayTimer = setTimeout(() => {
        apAwayTimer = null;
        // Still hovering, still no pet: one small look-away. She is not hurt,
        // she is making a point.
        if (!apInside || !canPerk() || stats.pets > apPets0) return;
        raw('¬_¬', { hold: 1000, bodyFrame: 'idle' });
      }, DIALS.LINGER_AWAY_MS);
    }, DIALS.LINGER_MS);
  }
  if (typeof document !== 'undefined' && document.addEventListener) {
    document.addEventListener('pointermove', onDocMove, { passive: true });
  }

  /* ==========================================================================
   * THE OFF CHANNELS (W3) - ONE BLOCK, DELIBERATELY.
   *
   * EMI is a television, and a television left alone changes the channel. The
   * whole wave is ONE capability - `screenTakeover(painter, {ms})` - and it
   * lives in `emi/takeover.js` + `emi/channels.js`, both loaded the way the
   * renderer is: dynamically, optionally, in a catch. A broken deck costs EMI
   * her channels and NOTHING else; face.js is never touched and never read.
   *
   * Everything this file contributes is here plus two marked one-liners
   * (`cancelChain()`'s preempt hook and `pet()`'s swallow), so a second merge
   * into this file has exactly three places to look.
   * ======================================================================== */

  /* THE SECOND CANVAS. Same locked rect as `.emi-screen` (emi.css), laid over
   * it, hidden at rest. face.js keeps painting underneath the whole time. */
  const glass = document.createElement('canvas');
  glass.className = 'emi-glass';
  /* THE LOCKED GEOMETRY, SIZED AT BIRTH. face.js paints at res 152 and derives
   * its height as round(152 x 0.903) = 137; a canvas left on the 300x150 UA
   * default would stretch every channel off her bezel the first frame anything
   * painted. The deck re-asserts these two numbers, but a node that is right
   * before the deck ever lands is one less way to be wrong. */
  glass.width = 152;
  glass.height = 137;
  glass.hidden = true;
  el.appendChild(glass);

  let deck = null;
  /** The painter table, once the deck module lands: `screenTakeover('pong')`. */
  let CHANNEL_TABLE = null;
  /** The `cancelChain()` hook. Hoisted, so the caller above may be older. */
  function killTakeover() {
    if (!deck) return;
    try { deck.preempt(); } catch (e) { /* a channel may never break a face */ }
  }
  /** The `pet()` hook: did the last press already spend itself on a channel? */
  function takeoverAtePet() {
    if (!deck) return false;
    try { return !!deck.swallowPet(); } catch (e) { return false; }
  }

  (function loadChannels() {
    if (typeof canvas.getContext !== 'function') return;   // faceless platform: no deck
    import('./takeover.js').catch((e) => {
      say('emi: takeover.js unavailable (' + ((e && e.message) || e) + ')');
      return null;
    }).then((m) => {
      if (!m || typeof m.createDeck !== 'function') return;
      CHANNEL_TABLE = m.CHANNELS || null;
      let media = null;
      try {
        media = m.createMediaBroker({
          assets: channelAssets, settings: channelSettings,
          rand: Math.random, doc: document, log: say,
        });
      } catch (e) { media = null; say('emi: media broker unavailable - ' + ((e && e.message) || e)); }
      try {
        deck = m.createDeck({
          root, el, glass, media,
          emi: {
            raw, play, chains: () => CHAINS, busy, saying,
            /* HER VOICE THROUGH THE ONE DOOR. index.js's `say` is the same three
             * lines; a channel that minted its own would be a second call site
             * for the bubble and therefore for the vox (trap 70). */
            say(line, opts) {
              if (!makeSay || !painter) return false;
              const o = opts || {};
              let chain = null;
              try { chain = makeSay(line, o.face || '^_^', sayHoldMs(line, o.hold)); }
              catch (e) { return false; }
              return play(chain, { protect: true, force: true });
            },
          },
          state: {
            hidden: () => hidden,
            enabled: () => enabled,
            dragging: () => dragging || pressing,
            reducedMotion,
            face: () => IDLE_FACE,
          },
          data: {
            days() {
              try { return store && typeof store.get === 'function' ? store.get('days') : null; }
              catch (e) { return null; }
            },
            labSeen() {
              try {
                const v = store && typeof store.get === 'function' ? store.get('emiVoice') : null;
                return !!(v && v.labSeen);
              } catch (e) { return false; }
            },
          },
          seed: String((stats.firstSeenAt || '') + '|' + (isoDay() || '')),
          onStat(key) {
            /* TRAP 77's OTHER HALF, AND THE ONE PLACE THAT SEES EVERY CHANNEL.
             * The glass never runs through play(), and a wheel-rolled takeover
             * that waited on `prepare` starts a tick AFTER the caller returned -
             * so the heartbeat's clock is stamped off the deck's own counter
             * rather than off any one call site. */
            if (key === 'takeovers') stampActivity('takeover');
            if (!Object.prototype.hasOwnProperty.call(stats, key)) return;
            stats[key] += 1;
            save();
          },
          log: say,
        });
      } catch (e) { deck = null; say('emi: createDeck threw - ' + ((e && e.message) || e)); }
    }).catch(() => {});
  }());

  /* ---------------------- viewport ------------------------------------- */
  /* A resize re-derives the default width AND re-clamps the anchor, in that
   * order - clamping against the old size would park her by a stale edge. */
  // Seeded from the boot-time window: a page that OPENS narrow never "squished".
  let wasNarrowVp = viewport().w < DIALS.W_NARROW_VW;
  function onResize() {
    /* A RESIZE MOVED THE FIXTURE (W2a, trap 73). The anchor she is standing at
     * was solved against the old viewport, so the trip is over: she goes home
     * and the scheduler may offer another one later. */
    cancelTrip();
    refitWidth();
    if (!hidden && enabled) place();
    // THE SQUISH: crossing into the narrow-window regime, once per crossing.
    // The voice's one-shot beat makes "once ever" out of it; later crossings
    // reach a beat already seen and land as silence.
    const narrow = viewport().w < DIALS.W_NARROW_VW;
    if (narrow !== wasNarrowVp) {
      wasNarrowVp = narrow;
      if (narrow && !hidden && enabled) emitGesture('windowSquish');
    }
    // A standing question re-fits against the new box (phones rotate).
    remeasureAsk();
  }
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('resize', onResize);
  }
  /* A ROTATE IS NOT ALWAYS A RESIZE. Some engines land `orientationchange` with
   * innerWidth/innerHeight still reporting the old box, so the resize handler
   * refits against numbers that are about to change. core/device.js already
   * re-checks on the next frame and again a beat later, so riding its seam is
   * what makes the phone ceiling and the corner dock survive a turn. */
  const unDevice = onDeviceChange(onResize);

  /* THE LAST FLUSH. msVisible is only true if it is banked before the page goes,
   * and a debounced write in flight would be lost with it. */
  function flush() { accrueVisible(); beginVisible(); save(true); }
  const onPageHide = () => { accrueVisible(); save(true); };
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('pagehide', onPageHide);
  }


  /* ========================================================================
   * THE FIELD TRIP (wave W2a, 2026-08-24) - EMI'S ONE AUTONOMOUS VERB
   *
   * Everything this wave adds to the widget lives between these two rules,
   * deliberately: `feat/emi-off-channels` is editing this file at the same
   * time. The only code outside the block is eight one-line hooks, each marked
   * `W2a` - endPress, onDown, beginDrag, onMove, hide, onResize, setEnabled,
   * destroy - plus three members on the handle.
   *
   * WHAT A TRIP IS. She powers her tube off where she stands, reappears beside
   * a campus fixture, says one line about it, powers off again and comes back
   * to the player's saved spot. It is a scripted rare delight and NOT wandering:
   * WHEN it may happen is `emi/fieldtrips.js`'s problem, and it is the only
   * caller. This file owns HOW, and the how is four laws:
   *
   *   1. THE RECT IS RESOLVED AT FIRE TIME, NEVER AT SCHEDULE TIME. Screens
   *      resize, the campus repaints, the plan is `xMidYMid slice` - a rect
   *      captured when the trip was queued is a rect that has moved by the
   *      time she gets there. `apparate` therefore takes a GETTER and calls it
   *      inside the dark, one frame before she lands. See trap 73.
   *   2. TOUCH ALWAYS WINS. A pointerdown on her at any point of the ladder
   *      ends the trip on the spot: she stays where she is, upright, with no
   *      stranded animation class, and the press carries on into an ordinary
   *      drag. Trap 75.
   *   3. SHE NEVER STARTS ONE OVER HERSELF. Mid-say, mid-chain, mid-press,
   *      dismissed, disabled or already travelling, `apparate` refuses and
   *      answers null. The caller does not need a guard.
   *   4. THE SAVED SPOT IS NEVER WRITTEN. The trip moves `el.style.left/top`
   *      and never `fx0`/`fy0`, so "come home" is just `place()` and a crash,
   *      a reload or a cancel can never lose where the player put her. The one
   *      exception is the touch cancel, which commits the spot she was ACTUALLY
   *      standing on - from the trip's own bookkeeping, never from
   *      `getBoundingClientRect`, which mid-squish reports a 1px line.
   * ======================================================================*/

  /** The carried face over a registered fixture: pure delight. `*_*` is already
   *  paired to the `pet` pose in FACE_BODY_FRAME, so the body follows for free. */
  const POI_FACE = '*_*';

  /** Normalise anything rect-shaped (a DOMRect, a plain object, a getter's
   *  answer) into our own numbers, or null. Never throws on junk. */
  function rectNow(src) {
    let r = null;
    try { r = typeof src === 'function' ? src() : src; } catch (e) { return null; }
    if (!r || typeof r !== 'object') return null;
    const left = num(r.left);
    const top = num(r.top);
    if (left === null || top === null) return null;
    let w = num(r.width);
    let h = num(r.height);
    if (w === null) { const rt = num(r.right); w = rt === null ? null : rt - left; }
    if (h === null) { const bt = num(r.bottom); h = bt === null ? null : bt - top; }
    if (w === null || h === null || !(w > 0) || !(h > 0)) return null;
    return { left, top, width: w, height: h, right: left + w, bottom: top + h };
  }

  /** How much of her box would sit on top of the fixture, in square px. */
  function overlapArea(left, top, s, rect) {
    const ox = Math.min(left + s.w, rect.right) - Math.max(left, rect.left);
    const oy = Math.min(top + s.h, rect.bottom) - Math.max(top, rect.top);
    return (ox > 0 && oy > 0) ? ox * oy : 0;
  }

  /**
   * WHERE SHE STANDS TO LOOK AT SOMETHING. Four candidates - right, left, under,
   * over - each clamped into the viewport and then scored. The clamp runs BEFORE
   * the overlap test on purpose: against a fixture in the corner the clamp is
   * what drags a candidate back over the very thing she came to see, and a test
   * on the raw candidate would never notice.
   */
  function anchorFor(rect) {
    const vp = viewport();
    const s = sizePx();
    const g = DIALS.TRIP_GAP_PX;
    const lo = DIALS.MARGIN;
    const maxX = Math.max(lo, vp.w - s.w - lo);
    const maxY = Math.max(lo, vp.h - s.h - lo);
    const midY = rect.top + rect.height / 2 - s.h / 2;
    const midX = rect.left + rect.width / 2 - s.w / 2;
    const cands = [
      { left: rect.right + g, top: midY },
      { left: rect.left - g - s.w, top: midY },
      { left: midX, top: rect.bottom + g },
      { left: midX, top: rect.top - g - s.h },
    ];
    let best = null;
    for (const c of cands) {
      const left = Math.round(clamp(c.left, lo, maxX));
      const top = Math.round(clamp(c.top, lo, maxY));
      const over = overlapArea(left, top, s, rect);
      const pushed = Math.abs(left - c.left) + Math.abs(top - c.top);
      // Clean beats compromised; among compromises, least overlap then least
      // clamping. There is ALWAYS an answer - a mascot with nowhere to stand
      // would be a trip that hangs half way through.
      if (!best || over < best.over || (over === best.over && pushed < best.pushed)) {
        best = { left, top, over, pushed };
      }
      if (over === 0 && pushed < 1) break;
    }
    return { left: best.left, top: best.top };
  }

  /* ---- the drag's fixture snapshot ------------------------------------ */
  /* MEASURED ONCE PER DRAG, NEVER PER MOVE. A getBoundingClientRect for every
   * registered fixture on every pointermove is the same mistake the drag-face
   * dedupe exists to prevent, multiplied by the size of the registry - and a
   * drag cannot resize the window, so one snapshot is also the correct answer. */
  let poiRectsFn = null;
  let poiCache = null;
  let poiOver = false;

  function poiForget() { poiCache = null; poiOver = false; }

  function poiSnapshot() {
    poiForget();
    if (!poiRectsFn) return;
    let list = null;
    try { list = poiRectsFn(); } catch (e) { return; }
    if (!Array.isArray(list) || !list.length) return;
    const out = [];
    for (const r of list) { const n = rectNow(r); if (n) out.push(n); }
    if (out.length) poiCache = out;
  }

  /** Is her CENTRE inside one of the snapshotted fixtures? */
  function overPoi(left, top) {
    if (!poiCache) return false;
    const s = sizePx();
    const cx = left + s.w / 2;
    const cy = top + s.h / 2;
    for (const r of poiCache) {
      if (cx >= r.left && cx <= r.right && cy >= r.top && cy <= r.bottom) return true;
    }
    return false;
  }

  /** The face a CARRY currently wears, fixture or not. The fling face outranks
   *  both while it lasts; this is only what she falls back to. */
  function carryFace() { return poiOver ? POI_FACE : DRAG_FACE; }

  /* ---- the tube ------------------------------------------------------- */
  /* THE POWER-OFF IS A KEYFRAME AND THAT IS THE WHOLE ARGUMENT (trap 71). The
   * `.emi` root's INLINE transform belongs to the dangle; a CSS animation
   * out-ranks an inline style for as long as it runs, which is exactly why the
   * squish is allowed to own the root and the carry tilt is not disturbed.
   * The reflow is trap 4's lesson: re-adding the same class without one is a
   * keyframe the browser coalesces away. */
  function crtClear() {
    try {
      el.classList.remove('crt-off');
      el.classList.remove('crt-on');
      el.classList.remove('crt-blank');
    } catch (e) { /* noop */ }
  }
  function crt(cls) {
    crtClear();
    if (!cls) return;
    try { void el.offsetWidth; el.classList.add(cls); } catch (e) { /* noop */ }
  }
  /** 0 under reduced motion: the CSS drops the squish, so waiting for it would
   *  only be a pause in an empty room. */
  function crtMs() { return reducedMotion() ? 0 : DIALS.CRT_MS; }

  /* ---- the trip ------------------------------------------------------- */
  /* {timer, left, top, onDone} - `left`/`top` are the trip's OWN bookkeeping of
   * where it last put her, because a rect read mid-squish is a 1px line. */
  let trip = null;

  function tripping() { return !!trip; }

  /** One step at a time, on a timer this trip owns. NOT `later()`: the say in
   *  the middle goes through `play()`, and `play()` calls `killTimers()`. */
  function tripStep(ms, fn) {
    if (!trip) return;
    if (trip.timer !== null) clearTimeout(trip.timer);
    const mine = trip;
    trip.timer = setTimeout(() => {
      if (trip !== mine) return;
      trip.timer = null;
      try { fn(); }
      catch (e) { say('emi: trip step threw - ' + ((e && e.message) || e)); cancelTrip(); }
    }, Math.max(0, ms));
  }

  /** Move her for the TRIP only: style plus the bubble flip, never the fractions. */
  function tripPlaceAt(left, top) {
    const vp = viewport();
    const s = sizePx();
    if (trip) { trip.left = left; trip.top = top; }
    try {
      el.style.left = Math.round(left) + 'px';
      el.style.top = Math.round(top) + 'px';
    } catch (e) { /* noop */ }
    faceBubble(left, top, s.w, vp.w);
  }

  /**
   * END A TRIP. Two shapes and they are genuinely different:
   *   cancelTrip()            -> she comes HOME (a dismiss, a resize, a disable,
   *                              a destroy, the caller's own cancel)
   *   cancelTrip({stay:true}) -> she stops WHERE SHE IS and that spot becomes
   *                              hers (a finger landed on her: trap 75)
   * Both clear every animation class and every protected say, so whatever
   * happens next starts from a mascot in a known state.
   */
  function cancelTrip(opts) {
    if (!trip) return false;
    const t = trip;
    trip = null;
    if (t.timer !== null) { clearTimeout(t.timer); t.timer = null; }
    /* THE FILL IS FORWARDS, SO TAKING THE CLASS OFF IS NOT OPTIONAL (trap 74).
     * A welded `scaleY(1)` would out-rank the dangle's inline rotate for ever. */
    crtClear();
    cancelChain();
    killTimers();
    setBubble(null);
    const stay = !!(opts && opts.stay);
    if (stay) { commit(t.left, t.top); save(); }
    else place();
    if (typeof t.onDone === 'function') {
      try { t.onDone({ cancelled: true, stay }); } catch (e) { /* noop */ }
    }
    idle();
    return true;
  }

  /**
   * APPARATE: the whole trip, as one call.
   *
   * @param {Function|Object} getRect  a RECT-GETTER, e.g.
   *        `() => node.getBoundingClientRect()`. A bare rect is accepted and is
   *        a bug waiting to happen - see law 1 and trap 73.
   * @param {{line?:string, face?:string, stay?:boolean, onDone?:Function}=} opts
   *        ASKS (a06): `stay` ends the trip WHERE SHE LANDED instead of walking
   *        the two beats home. The spot is session-only (`askSpot`) and the
   *        stored fractions are never touched, so tomorrow she is back where
   *        the player actually put her.
   * @returns {Function|null} a cancel function, or null when she refused.
   */
  function apparate(getRect, opts) {
    const o = opts || {};
    if (!getRect) return null;
    // LAW 3: she never starts one over herself.
    if (!enabled || hidden || trip || pressing || dragging || busy() || saying()) return null;
    if (!el || !el.classList) return null;
    // Nothing to travel TO is not a failure, it is a fixture that is not on
    // screen. Answer null and let the scheduler try again another night.
    if (!rectNow(getRect)) return null;

    const line = typeof o.line === 'string' && o.line.trim() ? o.line : null;
    const face = typeof o.face === 'string' && o.face ? o.face : '^_^';
    trip = { timer: null, left: 0, top: 0, onDone: typeof o.onDone === 'function' ? o.onDone : null };
    stampActivity('apparate');

    // Where she is standing right now, in pixels, so a cancel during the very
    // first squish still knows the spot it is being asked to keep.
    const start = place();
    trip.left = start.left;
    trip.top = start.top;

    stopBlink();
    stopSway();
    clearBody();
    restGaze();

    /* 1. THE TUBE GOES OFF. */
    crt('crt-off');
    tripStep(crtMs(), () => {
      /* 2. THE DARK. The rect is resolved HERE - law 1 - and a fixture that has
       *    gone since the trip was scheduled sends her straight home. */
      crt('crt-blank');
      const rect = rectNow(getRect);
      if (!rect) { cancelTrip(); return; }
      const spot = anchorFor(rect);
      tripPlaceAt(spot.left, spot.top);
      crt('crt-on');
      tripStep(crtMs(), () => {
        /* 3. THE LINE. Through the ordinary say path, so the bubble, the pose,
         *    the hold and the Blipese babble are all the ones she already has
         *    (trap 70: the voice hangs off setBubble and nowhere else). */
        crtClear();
        let wait = DIALS.TRIP_BEAT_MS;
        if (line) {
          wait = SAY_LEAD_MS + sayHoldMs(line) + DIALS.TRIP_BEAT_MS;
          let spoke = false;
          if (makeSay && painter) {
            try { spoke = play(makeSay(line, face, sayHoldMs(line)), { protect: true, force: true }); }
            catch (e) { spoke = false; }
          }
          // FACELESS STILL TALKS. The body png and the bubble are the half of
          // EMI that never needed a 2d context, and the trip is worth more than
          // the reaction frame on top of it.
          if (!spoke) { setBubble(line); wait = sayHoldMs(line) + DIALS.TRIP_BEAT_MS; }
        }
        tripStep(wait, () => {
          /* ASKS (a06): a `stay` trip is over the moment the line is. She keeps
           * the spot for the sitting; nothing is committed and nothing saved. */
          if (o.stay) {
            const t2 = trip;
            trip = null;
            crtClear();
            const vp2 = viewport();
            askSpot = { x: (t2 ? t2.left : 0) / vp2.w, y: (t2 ? t2.top : 0) / vp2.h };
            idle();
            if (t2 && typeof t2.onDone === 'function') {
              try { t2.onDone({ cancelled: false, stay: true }); } catch (e) { /* noop */ }
            }
            return;
          }
          /* 4. HOME. The same two beats, backwards. */
          cancelChain();
          killTimers();
          setBubble(null);
          crt('crt-off');
          tripStep(crtMs(), () => {
            crt('crt-blank');
            // `place()` reads the fractions the trip never touched, which IS the
            // player's saved spot (law 4). No arithmetic, so no drift.
            const home = place();
            if (trip) { trip.left = home.left; trip.top = home.top; }
            crt('crt-on');
            tripStep(crtMs(), () => {
              const t = trip;
              trip = null;
              crtClear();
              idle();
              if (t && typeof t.onDone === 'function') {
                try { t.onDone({ cancelled: false, stay: false }); } catch (e) { /* noop */ }
              }
            });
          });
        });
      });
    });

    return () => cancelTrip();
  }


  /* ==========================================================================
   * ASKS (wave EMI ASKS, 2026-08-25) - ONE BLOCK, DELIBERATELY.
   *
   * `emi/asks.js` decides WHEN she asks and WHAT the words are. Everything
   * below is the HOW: the two chips, the seams the effects need, and the one
   * honest answer to "may she ask right now".
   *
   * INPUT TRUST (trap 59). `#arc-emi` is `pointer-events:none` and exactly two
   * nodes turned it back on (`.emi`, `.emi-dock`). `.emi-ask .emi-chip` is the
   * THIRD and last, it is live only while a strip is up, and nothing else on
   * the layer changed. `preventDefault` is still called in exactly one place
   * (the `pointerdown` on `.emi`) and still on no document listener.
   *
   * THE STRIP IS NOT A CHAIN. It is plain DOM with its own lifetime, so it
   * survives the say it hangs under and is torn down by exactly one function.
   * ======================================================================== */

  /** {onChip, onDismiss, nodes} while a strip is up; null the rest of the time. */
  let askLive = null;
  /** The late re-measure's timer - see `remeasureAsk`. */
  let askFitTimer = null;
  /** The leave animation's own timer - see `dropStrip`. */
  let askDropTimer = null;
  /** The viewport clamp, in px, shared with the sheet as `--emi-ask-dx`. */
  let askDx = 0;
  function setAskDx(px) {
    try {
      if (el.style && typeof el.style.setProperty === 'function') {
        el.style.setProperty('--emi-ask-dx', Math.round(Number(px) || 0) + 'px');
      }
    } catch (e) { /* noop */ }
  }

  /** THE STRIP NEVER LEAVES THE WINDOW. The bubble's reach test (faceBubble)
   *  is a heuristic calibrated for a 104px box; a two-chip strip is wider than
   *  that, so it is MEASURED and pulled back in with a transform. A platform
   *  with no getBoundingClientRect (the node DOM double) simply skips it. */
  function clampAskStrip() {
    if (!askLive || askStrip.hidden) return;
    try {
      if (!askStrip.getBoundingClientRect) return;
      const had = askDx;
      askDx = 0;
      setAskDx(0);
      const r = askStrip.getBoundingClientRect();
      if (!r || !r.width) { askDx = had; setAskDx(had); return; }
      const vp = viewport();
      const pad = 4;
      let dx = 0;
      if (r.right > vp.w - pad) dx = Math.round((vp.w - pad) - r.right);
      if (r.left + dx < pad) dx = Math.round(pad - r.left);
      askDx = dx;
      setAskDx(dx);
    } catch (e) { /* a clamp may never be the thing that throws */ }
  }

  /** How tall the strip is, handed to the sheet so the BUBBLE can move up out
   *  of its way. One number, one custom property - the same trick the bubble's
   *  own `--emi-bubble-dx` uses (trap 62's lesson: whole pixels, no guessing). */
  function askLift() {
    let h = 0;
    try { h = Math.round(Number(askStrip.offsetHeight) || 0); } catch (e) { h = 0; }
    try {
      if (el.style && typeof el.style.setProperty === 'function') {
        el.style.setProperty('--emi-ask-h', (h > 0 ? h + 6 : 0) + 'px');
      }
    } catch (e) { /* noop */ }
  }

  /** RE-FIT A STANDING STRIP. `askLift` reads `offsetHeight` the instant the
   *  strip mounts - but on a phone Press Start 2P lands LATE, the 44px thumb
   *  floor grows the chips, and `max-width: min(84vw, 248px)` can wrap a row
   *  that measured as one line into two. A stale `--emi-ask-h` is exactly the
   *  bubble sitting ON the chips (owner, 2026-08-25: "on mobile the prompt and
   *  the box to respond get overlapped and we cant see the question"). So the
   *  measurement is taken AGAIN: one beat after mount, once more when the
   *  document's fonts resolve, and on every resize / rotate / drag-drop while
   *  a strip stands. Idempotent and cheap - two reads, two custom props. */
  function remeasureAsk() {
    if (!askLive || askStrip.hidden) return;
    askLift();
    clampAskStrip();
  }
  function scheduleAskFit() {
    if (askFitTimer !== null) { clearTimeout(askFitTimer); askFitTimer = null; }
    askFitTimer = setTimeout(() => { askFitTimer = null; remeasureAsk(); }, 280);
    try {
      if (typeof document !== 'undefined' && document.fonts && document.fonts.ready
        && typeof document.fonts.ready.then === 'function') {
        document.fonts.ready.then(() => remeasureAsk()).catch(() => {});
      }
    } catch (e) { /* a font API may never break a question */ }
  }

  function fireChip(i, text) {
    if (!askLive) return;
    /* W3 P0-35: THE ANSWER IS HEARD. A chip press moved a real decision (a06's
     * seat, a14's name) and answered nothing at all. One pop, on the press, so
     * it lands before the strip starts leaving. Every route in - the chip, the
     * Send button, the Enter key and the suite's `pick()` - comes through here,
     * which is why the cue is here and not on the buttons. */
    sfx('pop', 0.12, 1);
    const cb = askLive.onChip;
    try { cb(Math.max(0, i | 0), typeof text === 'string' ? text : ''); }
    catch (e) { /* a chip may never break a screen transition */ }
  }

  /**
   * MOUNT ONE STRIP. `spec` is asks.js's, and this file understands exactly
   * six fields: {id, chips[2], input, maxLength, onChip(i, text), onDismiss}.
   * `input:true` turns chip one into a FIELD plus a visible Send button; both
   * of them, and the Enter key, call `onChip(0, text)`.
   * @returns {?Object} {el, pick(i), destroy()} - null when it could not build.
   */
  function mountAsk(spec) {
    if (!spec || !Array.isArray(spec.chips) || !spec.chips.length) return null;
    if (!enabled || hidden) return null;
    dropStrip(false);
    try {
      askStrip.textContent = '';
      askStrip.classList.remove('out');
      if (askStrip.setAttribute) askStrip.setAttribute('data-ask', String(spec.id || ''));
      const nodes = [];
      let field = null;

      /* THE NAME ASK IS THE ONE WITH A KEYBOARD (a14). Chip 1 becomes an
       * 8-character field; Enter submits it and an empty one is a skip, which
       * asks.js decides - this file only hands the text over. */
      if (spec.input === true) {
        field = document.createElement('input');
        field.className = 'emi-chip emi-chip-field';
        if (field.setAttribute) {
          field.setAttribute('type', 'text');
          field.setAttribute('maxlength', String(spec.maxLength || ASK_NAME_MAX));
          field.setAttribute('aria-label', 'your name');
          field.setAttribute('autocomplete', 'off');
          field.setAttribute('spellcheck', 'false');
        }
        field.addEventListener('keydown', (ev) => {
          if (!ev || ev.key !== 'Enter') return;
          fireChip(0, field.value == null ? '' : String(field.value));
        });
        askStrip.appendChild(field);
        nodes.push(field);
      }

      /* AND THE FIELD NEEDS A BUTTON (owner, 2026-08-25: "we need a send button
       * visible"). Enter still submits and always did - but Enter is invisible,
       * and on a phone it is a key on a keyboard that covers the mascot. The
       * button IS chip one, so it carries `data-chip="0"` and goes through the
       * same `fireChip(0, text)` the Enter key does: one submit path, and the
       * suite can press either. It is inserted between the field and the out
       * chip, which is the reading order the answer is given in. */
      if (spec.input === true) {
        const sendBtn = document.createElement('button');
        sendBtn.className = 'emi-chip emi-chip-send';
        sendBtn.type = 'button';
        sendBtn.textContent = ASK_STRINGS.send;
        if (sendBtn.setAttribute) sendBtn.setAttribute('data-chip', '0');
        sendBtn.addEventListener('click', () => {
          fireChip(0, field ? String(field.value || '') : '');
        });
        askStrip.appendChild(sendBtn);
        nodes.push(sendBtn);
      }

      spec.chips.forEach((label, i) => {
        if (spec.input === true && i === 0) return;   // the field + send ARE chip one
        const b = document.createElement('button');
        b.className = 'emi-chip';
        b.type = 'button';
        b.textContent = String(label == null ? '' : label);
        if (b.setAttribute) b.setAttribute('data-chip', String(i));
        b.addEventListener('click', () => {
          fireChip(i, field && i === 0 ? String(field.value || '') : '');
        });
        askStrip.appendChild(b);
        nodes.push(b);
      });

      askStrip.hidden = false;
      askLive = {
        onChip: typeof spec.onChip === 'function' ? spec.onChip : () => {},
        onDismiss: typeof spec.onDismiss === 'function' ? spec.onDismiss : () => {},
        nodes, field,
      };
      askLift();
      el.classList.add('ask-up');
      /* W3 P0-35: SHE IS WAITING FOR YOU NOW. The strip arrives STRIP_LEAD_MS
       * after the question, so it is a second event and it needs a second
       * sound - one blip of her own voice, up a tone and a half from resting,
       * which reads as the question mark the line already ended on. Deliberately
       * `emi_blip` and not a piece of chrome: an ask is EMI asking. */
      sfx('emi_blip', 0.10, 1.15);
      clampAskStrip();
      scheduleAskFit();
      /* FOCUS THE FIELD, NEVER A CHIP. Stealing focus onto a button would put
       * a school-wide Enter on EMI; the field is the one place a keystroke is
       * unambiguously hers. */
      try { if (field && field.focus) field.focus(); } catch (e) { /* noop */ }
      return {
        el: askStrip,
        pick(i) { fireChip(i, field ? String(field.value || '') : ''); },
        destroy() { unmountAsk(false); },
      };
    } catch (e) {
      say('emi: ask strip failed (' + ((e && e.message) || e) + ')');
      unmountAsk(false);
      return null;
    }
  }

  /**
   * TAKE IT DOWN. `slide` plays the leave animation first (the CSS owns it and
   * reduced motion drops it to a plain hide); anything urgent passes false.
   */
  function unmountAsk(slide) {
    /* THE HOLD IS CLOSED HERE AND NOWHERE ELSE, and BEFORE the early return -
     * a question whose strip never managed to build still opened the hold with
     * its line, and this is the call that ends it. `dropStrip` is the half
     * WITHOUT that, and `mountAsk` is its one caller: clearing a previous
     * strip on the way in must not take down the question that is landing. */
    releaseAskLine();
    return dropStrip(slide);
  }
  function dropStrip(slide) {
    /* A PENDING LEAVE DIES HERE WHATEVER HAPPENS - `mountAsk` comes through
     * this branch on its way in, and a 220ms drop left running would hide the
     * strip that is replacing it. */
    if (askDropTimer !== null) { clearTimeout(askDropTimer); askDropTimer = null; }
    if (askFitTimer !== null) { clearTimeout(askFitTimer); askFitTimer = null; }
    if (!askLive) {
      try { askStrip.hidden = true; askStrip.textContent = ''; askStrip.classList.remove('out'); } catch (e) { /* noop */ }
      return false;
    }
    askLive = null;
    try { el.classList.remove('ask-up'); } catch (e) { /* noop */ }
    try {
      if (el.style && typeof el.style.setProperty === 'function') el.style.setProperty('--emi-ask-h', '0px');
    } catch (e) { /* noop */ }
    const drop = () => {
      if (askDropTimer !== null) { clearTimeout(askDropTimer); askDropTimer = null; }
      try {
        askStrip.hidden = true;
        askStrip.textContent = '';
        askStrip.classList.remove('out');
        askDx = 0;
        setAskDx(0);
      } catch (e) { /* noop */ }
    };
    if (slide && !reducedMotion()) {
      try { askStrip.classList.add('out'); } catch (e) { /* noop */ }
      /* ITS OWN HANDLE, NOT `later()`. Every resolution an ask has is followed
       * within the same tick by a line or a face - her reaction to the answer,
       * the wordless `-_-` - and both of those run `killTimers()`, which used
       * to take this one with them. The strip then never dropped: `.out`
       * animated it to opacity 0 and left a node with two `pointer-events:auto`
       * chips sitting over the board for the rest of the sitting (trap 59's
       * failure mode, and invisible in every screenshot). */
      if (askDropTimer !== null) clearTimeout(askDropTimer);
      askDropTimer = setTimeout(() => { askDropTimer = null; drop(); }, 220);
    } else {
      drop();
    }
    return true;
  }

  /**
   * TAKE THE QUESTION OFF THE GLASS AND LET THE WAITING LINE THROUGH.
   *
   * The question's hold is an hour (DIALS.ASK_HOLD_MS) precisely so that
   * nothing but this can end it, which means this is also the only place its
   * timer is cancelled - `idle()` funnels through `cancelChain()` and clears
   * the bubble, and every resolution the ask engine has (a reaction line, the
   * wordless `-_-`) lands on the free glass a beat later.
   *
   * The held line is released on a TIMER and not on the spot, because an
   * ANSWER is followed immediately by her reaction to it: giving the stale
   * bark the glass synchronously would put it under a line that is about to
   * replace it. `pumpHeldLine` waits for a quiet glass and gives up at
   * ASK_QUEUE_MS, so an ask that sat for a minute drops the line instead of
   * saying it into a room that has moved on.
   */
  function releaseAskLine() {
    const was = askHold;
    askHold = false;
    if (was && current && current.ask) { try { idle(); } catch (e) { /* noop */ } }
    if (!heldLine) return;
    if (heldTimer !== null) { clearTimeout(heldTimer); heldTimer = null; }
    heldTimer = setTimeout(pumpHeldLine, DIALS.ASK_QUEUE_POLL_MS);
  }

  /** MAY SHE ASK RIGHT NOW. One honest answer, owned by the file that owns the
   *  verbs: no live say, no chain, no press, no drag, no field trip, no off
   *  channel, not dismissed, not disabled, and no strip already up. */
  function askReady() {
    if (!enabled || hidden || !built) return false;
    if (askLive || trip || pressing || dragging) return false;
    if (busy() || saying()) return false;
    try {
      if (deck && typeof deck.live === 'function' && deck.live()) return false;
    } catch (e) { /* a deck that cannot be asked is not a deck that is up */ }
    return true;
  }

  /* ---- the effects a YES buys ----------------------------------------- */

  /** a06: THE OTHER SIDE. The mirrored x, through `apparate` and its rect
   *  GETTER (trap 73 - the rect is resolved inside the dark, one frame before
   *  she lands, so a resize mid-trip cannot strand her). `stay` is what makes
   *  it a move rather than a round trip. */
  function parkMirrored() {
    if (trip) return false;
    const getRect = () => {
      const vp = viewport();
      const sz = sizePx();
      let cur = null;
      try { cur = el.getBoundingClientRect ? el.getBoundingClientRect() : null; } catch (e) { cur = null; }
      const left = cur && Number.isFinite(Number(cur.left)) ? Number(cur.left) : (fx0 || 0) * vp.w;
      const top = cur && Number.isFinite(Number(cur.top)) ? Number(cur.top) : (fy0 || 0) * vp.h;
      const mx = clamp(vp.w - left - sz.w, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.w - sz.w - DIALS.MARGIN));
      /* A rect she can stand BESIDE. anchorFor() puts her off the side of it,
       * so the target is a thin sliver at the mirrored x rather than a box she
       * would be pushed out of. */
      return { left: mx, top, right: mx + 1, bottom: top + sz.h, width: 1, height: sz.h };
    };
    return !!apparate(getRect, { stay: true });
  }

  /** a08: SHE WATCHES THE BOARD. A constant added to the gaze lean for the
   *  rest of the class - the same CSS translate on `.emi-screen` the cursor
   *  lean already uses (trap 71), never a canvas repaint. `dir` is -1 (toward
   *  the middle of the school, which is where every board is) or 0 to clear. */
  function setGazeBias(dir) {
    const d = Number(dir);
    askGaze = Number.isFinite(d) ? clamp(d, -1, 1) : 0;
    kickGaze();
    return askGaze;
  }

  /** a13: THE CHIP IS A PET. Same counter, same ledger, same `love` beat - a
   *  second kind of pet that did not count would be the joke not landing. */
  function creditPet() {
    if (!enabled || hidden) return false;
    stats.pets += 1;
    touchSeen();
    const cycle = CHAINS && CHAINS.love;
    if (cycle) play(cycle, { bodyFrame: 'pet', force: true });
    else raw('(\u3002\u2665\u203f\u2665\u3002)', { hold: 1400, fx: 'hearts', body: 'bounce', bodyFrame: 'pet', force: true });
    save();
    emitGesture('pet', { fromAsk: true });
    return true;
  }
  /* ===================== end of the asks ================================ */

  /** THE REGISTRY SEAM. `emi/fieldtrips.js` hands over a function answering the
   *  live rects of every fixture EMI has a line about; the widget uses them for
   *  exactly ONE thing, the carried `*_*`. Null clears it. */
  function setPoiRects(fn) {
    poiRectsFn = typeof fn === 'function' ? fn : null;
    if (!poiRectsFn) poiForget();
  }
  /* ===================== end of the field trip ========================== */

  /* ---------------------- first paint ----------------------------------- */
  built = true;
  place();
  if (hidden) { el.hidden = true; dock.hidden = false; }
  else { el.hidden = false; dock.hidden = true; beginVisible(); if (painter) idle(); }

  /* ---------------------- the handle ------------------------------------ */
  return {
    el, dock, canvas, fxHost, bubble,
    attach,
    /** Subscribe to the pointer verbs (emi/voice.js). Returns an unsubscribe. */
    onGesture,
    /* HEARTBEAT (2026-08-25): subscribe to "she just did something visible",
     * and the idle gaze nudge. `emi/heartbeat.js` is the only caller of either;
     * both are inert until something subscribes. */
    onActivity, nudgeGaze,
    play, raw, idle,
    /** Swap the body png by frame key (BODY_FRAME_SRC). Test/host seam. */
    setBodyFrame,
    /** Which pose is up right now. */
    get bodyFrame() { return bodyFrame; },
    /* ---- COUNTER STOCK: the two EMI prizes ----------------------------- */
    /**
     * WHAT THE PLAYER OWNS. The shell hands this down at mount and hands it
     * down AGAIN when a purchase settles (`wallet-result`), so a prize bought
     * mid-sitting lights on the next paint without a reload (contract §4). A
     * bag that is a FUNCTION is re-read on every apply, so a shell that hands
     * a getter once never has to call this a second time.
     * @param {{deskToy?:boolean, varsity?:boolean}|Function=} bag
     */
    setPrizes(bag) {
      if (bag !== undefined) prizeSrc = bag || null;
      const st = applyPrizes();
      return { deskToy: st.deskToy, varsity: st.varsity, jacket: varsityState };
    },
    /** What she is wearing: 'off' | 'probing' | 'on' | 'failed'. Read-only. */
    get varsity() { return varsityState; },
    /**
     * WEAR A SHEET. One of OUTFITS, or null/junk for the standard set. Returns
     * the probe state. `varsity` is the only name that can be refused, because
     * it is the only one that is bought; the other three are just art.
     * @param {?string} name
     */
    setOutfit(name) { return armOutfit(name); },
    /** The sheet a selector asked for, or null when she is in the standard set. */
    get outfit() { return outfitPick; },
    /** The selector's probe: 'off' | 'probing' | 'on' | 'failed'. Read-only. */
    get outfitState() { return outfitState; },
    /** The url the CURRENT wardrobe resolves a frame key to. Test seam. */
    frameSrc(key) { return frameUrl(frameKey(key) || 'idle'); },
    /** The desk-toy prop node (hidden unless the prize is owned). Read-only. */
    get toy() { return toy; },
    /** True once tonight's toy line has been spent. Session-local. */
    get toySpent() { return toySpent; },
    /** Poke the toy without a pointer. The click handler's one call. */
    pokeToy,
    /** True while EMI is walking the idle sway (reduced motion never is). */
    swaying() { return swayTimer !== null; },
    makeSayFn() { return makeSay; },
    chainsTable() { return CHAINS; },
    hasFace() { return !!painter; },
    saying, busy,
    setBubble,
    /* W2a - the field trip. `apparate` takes a RECT-GETTER (trap 73), refuses
     * over any live verb, and answers a cancel function or null. */
    apparate, setPoiRects, tripping,
    /* ---- ASKS (2026-08-25): the strip, and the four seams an effect needs -
     * `emi/asks.js` is the only caller of any of them. `askState()` hands back
     * the LIVE ledger object and `askSave()` is this file's own debounced
     * writer, which is what keeps ONE writer on the `emi` key. */
    mountAsk, unmountAsk, askReady, parkMirrored, setGazeBias, creditPet,
    askState() { return askState; },
    askSave(immediate) { save(immediate !== false); },
    /** True while a chip strip is up. Read-only. */
    asking() { return !!askLive; },
    hide, show,
    get hidden() { return hidden; },
    /** True once the first-dismiss hint has been spent (persisted). */
    get hintShown() { return hintShown; },
    /** The options page's one verb (owner, 2026-08-25): how long her lines
     *  hang, as a scale on the say-hold curve. Persists on the emi blob;
     *  this file stays the blob's only writer. */
    setBubbleHold(scale) {
      const v = setSayHoldScale(scale);
      save();
      return v;
    },
    get bubbleHold() { return getSayHoldScale(); },
    /**
     * THE RESIZE SEAM. There is no UI for it yet; the moment there is, calling
     * this is what turns the width from "the window's default" into "the
     * player's choice", and only then does `w` start being persisted.
     */
    setWidth(px) {
      const n = num(px);
      if (n === null) return width;
      // The CHOSEN width is stored raw; what gets drawn is that fenced by the
      // device ceiling, so setting 200 on a phone stores 200 and draws 96.
      userWidth = clamp(Math.round(n), DIALS.W_MIN, DIALS.W_MAX);
      userSized = true;
      const next = effectiveWidth();
      if (next !== width) { width = next; if (!hidden && enabled) place(); }
      save();
      return width;
    },
    get width() { return width; },
    setEnabled(on) {
      const next = !!on;
      if (next === enabled) return;
      enabled = next;
      if (!enabled) {
        accrueVisible();
        endPress();
        cancelTrip();                // W2a: switched off mid-trip = home first
        emitGesture('gone', { reason: 'disabled' });   // ASKS: see hide()
        unmountAsk(false); dropHeldLine();
        cancelChain(); killTimers(); stopBlink(); stopSway(); clearBody();
        root.hidden = true;
        save(true);
      } else {
        root.hidden = false;
        if (!hidden) { place(); beginVisible(); idle(); }
      }
    },
    get enabled() { return enabled; },
    /** Read-only lifetime telemetry (a copy - nothing outside may mutate it). */
    stats() {
      return Object.assign({}, stats, { msVisible: visibleMs(), zones: Object.assign({}, stats.zones) });
    },
    /** Test/host seam: force the debounced write out now. */
    flush,
    /* ---- OFF CHANNELS (W3): the one capability, plus its test seams ---- */
    /**
     * screenTakeover(painterOrId, {ms}) - lay a channel over her glass. Returns
     * false when the deck refused (no material, not idle, a say is up), and a
     * refusal is a perfectly normal answer.
     */
    screenTakeover(which, opts) {
      // `which`, not `painter`: `painter` is the FACE renderer in this closure.
      if (!deck) return false;
      const p = typeof which === 'string' ? (CHANNEL_TABLE && CHANNEL_TABLE[which]) : which;
      if (!p) return false;
      // The heartbeat's clock is stamped off the deck's own `onStat` counter,
      // not from here - see the loadChannels block.
      try { return !!deck.screenTakeover(p, opts || {}); } catch (e) { return false; }
    },
    /**
     * THE HEARTBEAT'S DOOR ONTO THE WHEEL (2026-08-25). One wheel tick with the
     * player-silence floor (THEATRE_IDLE_MS) lifted and every OTHER deck
     * refusal - the per-channel cooldowns, the global cooldown, PER_SESSION_CAP,
     * a class owning the screen, a live say, a hidden document - still standing.
     * The owner wants screen animations frequent; they are still the deck's.
     */
    pulseChannel() {
      if (!deck || typeof deck.pulse !== 'function') return false;
      try { return !!deck.pulse(); } catch (e) { return false; }
    },
    /** The deck itself: the suites drive the wheel and the clock through it. */
    get channels() { return deck; },
    /** A shell moment changed hands (a class, a suspend). One line in moments.js. */
    noteMoment(name) {
      if (!deck) return;
      const busyNow = name === 'classStart' || name === 'suspend' || name === 'tabAway';
      const freeNow = name === 'win' || name === 'miss' || name === 'fail' || name === 'resume'
        || name === 'reportCard' || name === 'greet' || name === 'dayDone';
      if (busyNow) { try { deck.setScene(true); } catch (e) { /* noop */ } }
      else if (freeNow) { try { deck.setScene(false); } catch (e) { /* noop */ } }
    },
    destroy() {
      accrueVisible();
      /* COUNTER STOCK: a probe in flight answers into a widget that is gone.
       * It owns no timer - closing its own gate is the whole cancellation. */
      if (varsityState === 'probing') varsityState = 'failed';
      if (outfitState === 'probing') outfitState = 'failed';
      cancelTrip();                  // W2a: never leave a trip timer behind
      unmountAsk(false);             // ASKS: never leave a live chip behind
      dropHeldLine();                // ...nor a line waiting for one to end
      if (askDropTimer !== null) { clearTimeout(askDropTimer); askDropTimer = null; }
      save(true);
      // clearBody() is the easy one to miss: `bodyTimer` outlives everything else
      // and its callback re-adds `.breath` to a node that is no longer in the page.
      cancelChain(); killTimers(); stopBlink(); stopSway(); disarmHold(); clearBody();
      stopToyLoop();                 // COUNTER STOCK: the toy's is the newest timer
      clearApproachTimers();
      clearGazeNudge();               // HEARTBEAT: the one timer restGaze owns
      activitySubs.clear();
      if (gazeRaf != null && typeof cancelAnimationFrame === 'function') {
        cancelAnimationFrame(gazeRaf); gazeRaf = null;
      }
      // Her voice is a setTimeout ladder of its own and nothing above clears it.
      if (vox) { try { vox.stop(); } catch (e) { /* noop */ } }
      // OFF CHANNELS (W3): the deck owns a rAF, a wheel timer and five document
      // listeners of its own; nothing above reaches any of them.
      if (deck) { try { deck.destroy(); } catch (e) { /* noop */ } deck = null; }
      if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
      if (typeof document !== 'undefined' && document.removeEventListener) {
        document.removeEventListener('pointermove', onDocMove);
        document.removeEventListener('visibilitychange', onToyVisibility);
      }
      if (typeof window !== 'undefined' && window.removeEventListener) {
        window.removeEventListener('resize', onResize);
        try { unDevice(); } catch (e2) { /* noop */ }
        window.removeEventListener('pagehide', onPageHide);
      }
      try { el.remove(); } catch (e) { /* noop */ }
      try { dock.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createWidget;
