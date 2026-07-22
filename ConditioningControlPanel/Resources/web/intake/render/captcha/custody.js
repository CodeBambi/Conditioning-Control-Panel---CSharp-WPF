/* ============================================================================
 * render/captcha/custody.js — VerifyCustody ("Chain of Custody")   [Wave-2 stub]
 *
 * OWNER: Wave-2 Agent — CUSTODY. Implements CAPTCHA_BRAINSTORM.md #8 — THE
 * metadata detonation: a vertical evidence docket (thumbnail + metadata card per
 * row, CONFIRM/DENY bools) whose FILENAMES ARE REAL (derived client-side from the
 * BootConfig `media` URLs) while the stats are fabricated. The answer shape is a
 * bool per row.
 *
 * HARD RULE (CAPTCHA_HANDOFF.md §4.2 + brainstorm): real filenames appear in THIS
 * item and NOWHERE ELSE in the feature. Sanitize (length/unicode) + prefer
 * descriptive names; fabricate a realistic one before shipping an "Untitled(3).gif".
 *
 * This is a STUB: returns false so beats.js falls back to plain YesNo-shape
 * rendering. The Wave-2 agent overwrites it wholesale, building via
 * `helpers.chrome` and committing through ctx.submitValue / submitIndex /
 * forceComplete per the render contract in ./index.js. Nothing throws at import.
 * ==========================================================================*/

/** @param {import('./index.js').CaptchaCtx} ctx @param {import('./index.js').CaptchaHelpers} helpers */
export function render(ctx, helpers) {
  return false;   // not implemented -> beats.js falls back to plain rendering
}

export default { render };
