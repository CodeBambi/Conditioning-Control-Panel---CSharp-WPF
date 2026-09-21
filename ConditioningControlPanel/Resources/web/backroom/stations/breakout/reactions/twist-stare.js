/* ============================================================================
 * stations/breakout/reactions/twist-stare.js - what the eye throws off.
 * Twist: Stare (act 3). Registered through reactions.js; signature (fx, d, snapshot) => void.
 *
 * Cosmetic only, and deliberately thin: the eye itself is drawn every frame in
 * stare-render.js, so an event only has to say WHEN. Grey is payload free
 * (fx.colour). Reduced motion keeps the rings and the word, and drops every
 * burst, spray and aberration.
 * ==========================================================================*/

/** The eye has no colour of its own: bone white and the dead grey it leaves behind. */
const BONE = [226, 226, 234];
const DEAD = [112, 110, 124];

export const REACTIONS = {
  /** STAREON: one slow pale ring closing IN, so the attention reads as arriving. */
  stareOn(fx, d) {
    if (!fx.colour) return;
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 52, r1: 14, life: 0.5, rgb: BONE });
    if (fx.reduced) return;
    fx.aberr(0.08);
  },

  /** STAREJUDGE: the bricks went grey. A dull ring, a word, and a little ash off it. */
  stareJudge(fx, d) {
    if (!fx.colour) return;
    const n = Math.max(1, Number(d && d.n) || 1);
    fx.stamps.push({ kind: 'ring', x: d.x, y: d.y, r0: 6, r1: 30 + n * 8, life: 0.42, rgb: DEAD });
    fx.stamps.push({ kind: 'text', text: 'SEEN', x: d.x, y: d.y - 20, life: 0.8, rgb: fx.colours.GREY, size: 15 });
    if (fx.reduced) return;
    fx.aberr(0.14);
    if (!fx.rungs(3)) return;
    fx.P.burst(d.x, d.y, DEAD, Math.min(14, 4 + n * 3), 110, 0.7, { rise: 8, gv: 280, r0: 1.3, r1: 2.1 });
  },

  /** STAREOFF: out of sight. A pale ring opening out, the opposite move to stareOn. */
  stareOff(fx, d, s) {
    if (!fx.colour) return;
    const at = d && Number.isFinite(d.x) ? d : { x: (s && s.w ? s.w / 2 : 480), y: (s && s.h ? s.h * 0.16 : 90) };
    fx.stamps.push({ kind: 'ring', x: at.x, y: at.y, r0: 10, r1: 64, life: 0.4, rgb: BONE });
    if (fx.reduced || !fx.rungs(3)) return;
    fx.P.burst(at.x, at.y, BONE, 8, 150, 0.5, { rise: 22, gv: 60, r0: 1.2, r1: 2 });
  },
};

export default REACTIONS;
