/* ============================================================================
 * shell/sharecard.js - THE PAPER SLIP THAT LEAVES THE BUILDING.
 *
 * The night's report card, drawn onto a canvas as a cream paper slip: the
 * school's wordmark, a ruled grade table with rubber-stamped letters, the
 * ten-segment attendance meter drawn as the school's own star stamps, the
 * perfect-attendance seal when it was earned, and a torn bottom edge. One PNG,
 * 1200x1600 portrait, exported at 1x or 2x depending on the device.
 *
 * TWO FILES, ONE SLIP. This half is PURE - tokens, the anonymiser and the
 * layout maths, no document and no canvas anywhere in it, so every rule the
 * card obeys can be asserted in node. Its other half, `shell/sharepaint.js`,
 * is the painter and needs a real browser.
 *
 * WHY IT IS A SEPARATE MODULE. `shell/reportcard.js` is driven by the headless
 * DOM double every suite in this school runs on, and that double has no canvas,
 * no `Image`, no `document.fonts` and no `Blob`. So the drawing lives here
 * behind `canRenderCard()`, the maths lives in `layoutCard()` (PURE - no DOM at
 * all, which is what makes it node-testable), and the report card imports this
 * file lazily and only when a player actually presses Share.
 *
 * IT IS ANONYMOUS BY CONSTRUCTION, and that is the whole point:
 *   - the header is the literal string 'The Arcademy' (reportcard.js's
 *     SHARE_HEADER), never `t('arcademy')` - a mod-skinned header would out the
 *     player's mod in a Discord paste (trap 13);
 *   - every class name comes from SHARE_NAMES, the neutral English table, never
 *     from `gameName()` or a `game_<key>` lexicon row;
 *   - nothing else on the slip is lexicon'd either. Law VII says accents come
 *     from the lexicon; this is not an accent, it is a document that leaves the
 *     app, and the one thing it must never carry is which mod you play.
 *   - the player's NAME and STUDENT NUMBER appear only when `identity` is
 *     non-null, which only happens when the player has ticked "add my name".
 *
 * HOUSE BOOK. Law IV (grades arrive as objects, never bare strings) is why the
 * letters are rubber stamps with an ink-bleed edge and a few degrees of tilt;
 * Law V (everything is seeded) is why the tilt, the grain and the torn edge all
 * come off `makeRng` and a retake of the same day draws the same slip; Law III's
 * restraint is why the hot pink `--pink` appears exactly ONCE on the whole
 * sheet, as the margin rule. Every colour and both faces are lifted from
 * styles.css and art/, never invented here.
 * ==========================================================================*/

import makeRng from '../core/rng.js';

/** The sheet, in CSS-ish px. Portrait, and 2x of this on a retina export. */
export const CARD_W = 1200;
export const CARD_H = 1600;
/** Never hand a chat client a bigger file than this (owner: under 400 KB). */
export const MAX_BYTES = 400 * 1024;

/* ----------------------------------------------------------------------------
 * THE INKS. Every one is a styles.css :root token, copied by VALUE because a
 * canvas cannot read a custom property - a `getComputedStyle` round trip per
 * fill would also make the slip depend on which mod's palette is loaded, which
 * is exactly the leak this file exists to prevent.
 * -------------------------------------------------------------------------- */
export const INK = Object.freeze({
  paper: '#F2EBDD',        // --ink, the chalk-cream. Here it is the STOCK.
  paperEdge: '#E4DAC6',    // the same cream, walked down for the grain floor
  ink: '#1A1A2E',          // --navy, the pen
  inkDim: '#4A4668',       // --navy toward --line, the ruled furniture
  inkFaint: '#8A84A8',     // --ink-faint, the small print
  stamp: '#D4488F',        // --pink-deep, the rubber stamp's pad
  pink: '#FF69B4',         // --pink. ONE use on the sheet: the margin rule.
  gold: '#F0C24B',         // --gold, the seal
  goldDeep: '#B98A1E',     // the seal's shadow side (house book gold chip)
});

/** The bundled display face (styles.css @font-face), and its whole fallback. */
export const DISPLAY_FACE = "'Arcademy Display','Copperplate Gothic Bold','Arial Black',Impact,sans-serif";
export const BODY_FACE = "'Segoe UI',system-ui,-apple-system,sans-serif";
export const MONO_FACE = "'IBM Plex Mono',Consolas,'Courier New',monospace";

/**
 * MOD-ANONYMOUS CLASS NAMES - the SHARE_NAMES pattern, extended to every class
 * in games/registry.js. These are deliberately NOT `GAME_TITLE` imported from
 * the registry: that table is a door plate a mod may re-voice through the
 * lexicon, and the two are allowed to drift. This one may never.
 */
export const SHARE_NAMES = Object.freeze({
  daily_trigger: 'Daily Trigger',
  lost_and_found: 'Lost & Found',
  deja_vu: 'Deja Vu',
  impulse_control: 'Impulse Control',
  misdirection: 'Misdirection',
  sort: 'Sort',
  echo: 'Echo',
  instant_recall: 'Instant Recall',
  anomaly: 'Anomaly',
  composure: 'Composure',
  the_deep_end: 'The Deep End',
});

/** The neutral English name for a class. Never a lexicon row. */
export function shareClassName(key) {
  const k = String(key || '');
  return SHARE_NAMES[k] || k.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

/**
 * The letter a grade prints as. Owner ruling: the slip carries S / A / B / C.
 * 'S+' prints as S (the honours letter is an in-house distinction and naming it
 * outside the building says more about the mod than the night), a zen 'pass'
 * prints as P, and anything ungraded prints as nothing at all - a pending class
 * gets a ruled row with an empty stamp box, which is what a real report card
 * does.
 */
export function shareGrade(grade) {
  const g = String(grade || '').trim().toUpperCase();
  if (g === 'S+' || g === 'S') return 'S';
  if (g === 'A' || g === 'B' || g === 'C') return g;
  if (g === 'PASS' || g === 'P') return 'P';
  return '';
}

/** SYNTHESIS #10 - ten segments, everywhere, forever. */
export const SEGMENTS = 10;

/* ----------------------------------------------------------------------------
 * THE LAYOUT - PURE. No document, no canvas, no measurement.
 *
 * It walks ONE cursor down the sheet, so a slip with an identity line, a fifth
 * class or no seal is the same code path with different numbers - and every
 * number it returns can be asserted in node.
 * -------------------------------------------------------------------------- */
const PAD_L = 132;          // right of the margin rule
export const PAD_R = 96;
const RULE_X = 104;         // the margin rule itself
const STUB_H = 96;          // the perforated tear-off stub at the top
export const TEAR_H = 46;          // the torn bottom edge
const ROW_H = 106;

/**
 * @param {Object} state
 * @param {Array} state.classes   [{gameKey, grade}] in timetable order
 * @param {number} state.streak   attendance streak, clamped to 0..SEGMENTS here
 * @param {boolean} state.perfect all of today's classes were finished
 * @param {?Object} state.identity {name, number} when the player opted in
 * @param {string} state.dateLabel
 * @returns {Object} every box the painter needs, in sheet px
 */
export function layoutCard(state) {
  const s = state || {};
  const rowsIn = Array.isArray(s.classes) ? s.classes.slice(0, 6) : [];
  const ident = (s.identity && (s.identity.name || s.identity.number)) ? s.identity : null;
  const rng = makeRng('arcademy-share|' + String(s.dateLabel || '') + '|' + rowsIn.length);
  const contentW = CARD_W - PAD_L - PAD_R;

  const out = {
    w: CARD_W,
    h: CARD_H,
    stubY: STUB_H,
    tearY: CARD_H - TEAR_H,
    margin: { x: RULE_X, top: STUB_H + 18, bottom: CARD_H - TEAR_H - 18 },
    contentX: PAD_L,
    contentW,
  };

  /* the perforation: an odd number of holes so one sits dead centre */
  const holes = [];
  const HOLE_GAP = 34;
  const holeCount = Math.floor((CARD_W - 60) / HOLE_GAP);
  const holeLeft = (CARD_W - (holeCount - 1) * HOLE_GAP) / 2;
  for (let i = 0; i < holeCount; i += 1) holes.push(holeLeft + i * HOLE_GAP);
  out.perf = { y: STUB_H, r: 5, xs: holes };

  let y = STUB_H + 62;

  /* the wordmark. 480 wide is the widest the sheet takes without the mark
   * out-shouting the grades, which are the reason anybody opens this image. */
  const markW = 480;
  out.mark = { x: (CARD_W - markW) / 2, y, w: markW, h: 180 };
  y += out.mark.h + 34;

  out.kicker = { x: CARD_W / 2, y, size: 30, text: 'REPORT CARD' };
  y += 26;
  out.kickerRule = { x: (CARD_W - 260) / 2, y, w: 260 };   // THE ONE PINK THING
  y += 46;

  out.date = { x: CARD_W / 2, y, size: 22 };
  y += ident ? 44 : 8;

  out.ident = ident ? { x: CARD_W / 2, y, size: 20 } : null;
  y += ident ? 46 : 44;

  /* the table */
  out.head = { y: y + 22, left: PAD_L, right: CARD_W - PAD_R, gradeX: CARD_W - PAD_R - 58, size: 17 };
  out.headRule = { y: y + 40, x: PAD_L, w: contentW };
  const rowTop = y + 40;

  out.rows = rowsIn.map((c, i) => {
    const top = rowTop + ROW_H * i;
    const key = String((c && c.gameKey) || '');
    return {
      key,
      name: shareClassName(key),
      grade: shareGrade(c && c.grade),
      y: top,
      h: ROW_H,
      textY: top + 62,
      ruleY: top + ROW_H,
      stamp: {
        cx: CARD_W - PAD_R - 58,
        cy: top + 54,
        r: 44,
        /* Law V: the tilt is seeded, so the same night always stamps the same
         * way and two players never compare crooked-versus-straight. */
        tilt: (rng() * 10 - 5) * Math.PI / 180,
      },
    };
  });
  y = rowTop + ROW_H * out.rows.length;

  /* attendance */
  y += 54;
  out.attend = { x: PAD_L, y, size: 18, countX: CARD_W - PAD_R };
  y += 34;
  const cell = 62;
  const gap = SEGMENTS > 1 ? (contentW - cell * SEGMENTS) / (SEGMENTS - 1) : 0;
  const lit = Math.max(0, Math.min(SEGMENTS, Math.round(Number(s.streak) || 0)));
  out.meter = {
    y, cell, gap, lit,
    cells: Array.from({ length: SEGMENTS }, (_, i) => ({
      x: PAD_L + (cell + gap) * i, y, size: cell, on: i < lit,
    })),
  };
  y += cell + 46;

  /* THE REMARKS BLOCK. A real slip does not stop dead under the last number and
   * leave four inches of blank stock - it rules the space and lets the office
   * write in it. Here it is what the seal is stamped ON, which is why the ruled
   * lines stop short of the seal instead of running under it. */
  const footRuleY = CARD_H - TEAR_H - 96;
  out.seal = s.perfect
    ? { cx: CARD_W - PAD_R - 104, cy: footRuleY - 118, r: 100 }
    : null;
  const lineRight = out.seal ? out.seal.cx - out.seal.r - 24 : CARD_W - PAD_R;
  const lines = [];
  for (let ly = y + 62; ly <= footRuleY - 46; ly += 58) lines.push(ly);
  out.remarks = {
    x: PAD_L, y, size: 18, right: lineRight, lines,
  };

  /* the date line, at the foot, always */
  out.foot = {
    ruleY: footRuleY,
    x: PAD_L,
    w: out.seal ? Math.min(420, contentW) : Math.min(560, contentW),
    labelY: footRuleY + 30,
    size: 15,
  };

  /* what a suite asserts: nothing may be drawn below the tear. */
  out.overflow = out.foot.labelY > out.tearY - 8
    || (out.seal ? out.seal.cy + out.seal.r > out.tearY - 8 : false)
    || out.meter.y + cell > footRuleY;
  return out;
}
