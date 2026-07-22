/* ============================================================================
 * render/captcha/checkbox.js — VerifyCheckbox ("I am not a robot")  [Wave-2 stub]
 *
 * OWNER: Wave-2 Agent — CHECKBOX. Implements CAPTCHA_BRAINSTORM.md SYNTHESIS #3
 * (the most-trusted widget on the internet, played 100% straight at Calibration,
 * then a click -> 3s hold -> 8s hold ladder with the spinner label cycling
 * "verifying… verifying you… verifying yours…"; per-niche label inversion). The
 * answer shape is bool + hold telemetry. Cheapest item — no images.
 *
 * This is a STUB: returns false so beats.js falls back to plain YesNo-shape
 * rendering. The Wave-2 agent overwrites it wholesale, building via
 * `helpers.chrome` (frame + spinner) and committing through ctx.submitValue /
 * forceComplete per the render contract in ./index.js. Nothing throws at import.
 * ==========================================================================*/

/** @param {import('./index.js').CaptchaCtx} ctx @param {import('./index.js').CaptchaHelpers} helpers */
export function render(ctx, helpers) {
  return false;   // not implemented -> beats.js falls back to plain rendering
}

export default { render };
