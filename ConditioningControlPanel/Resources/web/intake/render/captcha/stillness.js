/* ============================================================================
 * render/captcha/stillness.js — VerifyStillness ("Passive Integrity Scan") [Wave-2 stub]
 *
 * OWNER: Wave-2 Agent — STILLNESS. Implements CAPTCHA_BRAINSTORM.md #9 — the
 * Turnstile stillness check: "Verifying subject… Do not click. Do not scroll.
 * Breathe normally." Any input visibly rewinds a progress bar a few percent
 * ("movement detected — recalibrating"); the bar always completes within ~20s;
 * stillness IS the endorsement. The answer shape is bool + interference count.
 * Consent-shaped cousin of the freeze gate; zero assets.
 *
 * forceComplete is the tiny "skip verification (flagged)" hatch link.
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
