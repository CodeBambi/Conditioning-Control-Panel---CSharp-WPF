/* ============================================================================
 * render/captcha/transcribe.js — VerifyTranscribe ("Transcription ladder") [Wave-2 stub]
 *
 * OWNER: Wave-2 Agent — TRANSCRIBE. Implements CAPTCHA_BRAINSTORM.md SYNTHESIS #2
 * (v1 warped-word captcha -> word-stem completion -> "type the characters you
 * see" over a blank/gif; the Climax free-type echo is the richest tag-vote
 * source in the feature). The answer shape is a verbatim string.
 *
 * HARD RULE (CAPTCHA_HANDOFF.md §4.7): the free text is echoed LOCALLY, verbatim,
 * only — NEVER routed to the /intake/ai seam. Empty submit is always valid
 * ("transcription: silent.").
 *
 * This is a STUB: returns false so beats.js falls back to plain Mantra-shape
 * (type-it) rendering. The Wave-2 agent overwrites it wholesale, building via
 * `helpers.chrome` and committing through ctx.submitValue / forceComplete per the
 * render contract in ./index.js. Nothing throws at import.
 * ==========================================================================*/

/** @param {import('./index.js').CaptchaCtx} ctx @param {import('./index.js').CaptchaHelpers} helpers */
export function render(ctx, helpers) {
  return false;   // not implemented -> beats.js falls back to plain rendering
}

export default { render };
