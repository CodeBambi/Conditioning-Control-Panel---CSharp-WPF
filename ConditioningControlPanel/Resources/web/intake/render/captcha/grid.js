/* ============================================================================
 * render/captcha/grid.js — VerifyGrid ("The Hydrant Grid")   [Wave-2 stub]
 *
 * OWNER: Wave-2 Agent — GRID. Implements CAPTCHA_BRAINSTORM.md SYNTHESIS #1
 * (the seed reCAPTCHA 3x3 whose tiles flicker into the user's own gifs; the
 * grade is which of their own files they called a hydrant). Full 5-band ladder
 * + the Recovery frozen-tile callback in the brainstorm.
 *
 * This is a STUB: it returns false so beats.js falls back to plain MC4-shape
 * rendering (the beat stays completable + gradable). The Wave-2 agent overwrites
 * this file wholesale, building the card via `helpers.chrome` (frame + gridShell
 * + mundaneTileSrc) and committing through the ctx.submit* helpers per the render
 * contract in ./index.js. Nothing throws at import.
 * ==========================================================================*/

/** @param {import('./index.js').CaptchaCtx} ctx @param {import('./index.js').CaptchaHelpers} helpers */
export function render(ctx, helpers) {
  return false;   // not implemented -> beats.js falls back to plain rendering
}

export default { render };
