/* ============================================================================
 * games/composure/lex.js - every `cp_` lexicon row COMPOSURE renders, with its
 * neutral English fallback. Exported as DATA (the Impulse Control / Deep End
 * precedent, IC_LEX / DE_LEX) so the host's `NeutralLexicon` can be mirrored
 * key-for-key: copy the values, never re-word them.
 *
 * RULES (CLAUDE.md trap 26 + the build contract): a value longer than 96
 * characters can never be mod-skinned, so every row here is <= 96; a row is
 * rendered ONLY through ctx.lexicon(key, fallback) (Law VII - accents come
 * from the lexicon, never AI).
 *
 * TWO ROWS THIS GAME DELIBERATELY DOES NOT MINT: `peek` and `peek_hint` are
 * the SHELL's shared rows (Lost & Found renders the same two). Peek is one
 * verb across the campus - see shell/peek.js - so its label is one string.
 *
 * The `cp_trick_*` rows are the trickster deck's (CREATIVE owns trickster.js;
 * CORE ships the rows so the mirror script sees them). A deck that needs a
 * string this table does not carry must ask for the row, never invent one.
 * ==========================================================================*/

export const CP_LEX = Object.freeze({
  /* ---- setting rows (manifest.settings) -------------------------------- */
  cp_mode: 'Mode',
  cp_mode_hint: 'Timed is one graded class. Zen is untimed, gentle, and always a pass.',
  cp_zen_grid: 'Zen board',
  cp_zen_grid_hint: 'Zen only. A timed class plays the board your year has earned.',

  /* ---- HUD chips (aria labels; the chip TEXT is live data) -------------- */
  cp_chip_moves: 'Moves',
  cp_chip_clock: 'Time left',
  cp_chip_locked: 'Pieces home',
  cp_chip_calm: 'Composure',

  /* ---- the drawn class-rules sheet (Deck VI, Law IV) -------------------- */
  cp_howto_title: 'Class rules',
  cp_howto_slide: 'Tap a piece beside the gap and it slides in. Arrows, WASD and swipes do the same.',
  cp_howto_lock: 'A piece that reaches its own place locks with a snap. It can still be slid.',
  cp_howto_wash: 'The room will bury the board. Keep sliding - the picture underneath never moved.',
  cp_howto_go: 'Start the picture',

  /* ---- proctor lines (.g-cp-msg, one at a time) ------------------------- */
  cp_brief: 'One picture, cut apart and still moving. Put it back together.',
  cp_brief_zen: 'No clock tonight. Slide until it is whole again.',
  cp_play_hint: 'Tap a piece beside the gap. Arrows, WASD or swipe.',
  cp_lock_line: 'That one is home.',
  cp_backtrack_line: 'Back where it was. Breathe.',
  cp_wash_line: 'Keep sliding. The board is still exactly where you left it.',
  cp_rescue_line: 'Take the lit piece. The grade eases; the class does not end.',
  cp_solved_line: 'Whole. Watch it play.',
  cp_bell_warn: 'Twenty seconds.',
  cp_bell_line: 'The bell. Hands off the board.',
  cp_zen_done: 'Whole, in your own time.',
  cp_retake: 'Retake',
  cp_finish: 'Finish',
  cp_peek_ref: 'The finished picture',

  /* ---- stamps + ceremonies ---------------------------------------------- */
  cp_stamp_solved: 'COMPOSED',
  cp_stamp_lock: 'HOME',
  cp_stamp_bell: 'BELL',
  cp_stamp_assist: 'ASSIST',
  cp_jackpot: 'JACKPOT',
  cp_near_miss: 'SO CLOSE',

  /* ---- the end card (.g-cp-end) ----------------------------------------- */
  cp_end_title: 'Composure report',
  cp_end_title_zen: 'Zen board',
  cp_end_solved: 'Solved',
  cp_end_moves: 'Moves',
  cp_end_par: 'Baseline',
  cp_end_locked: 'Pieces home',
  cp_end_backtracks: 'Backtracks',
  cp_end_thrash: 'Panic moves',
  cp_end_assists: 'Assists',
  cp_end_time: 'Time',
  cp_end_yes: 'Yes',
  cp_end_no: 'No',
  cp_end_best: 'Best solve',
  cp_end_best_line: 'Your standing mark on this board. Beat it next class.',
  cp_end_best_first: 'Your first finished picture on this board.',

  /* ---- the trickster deck's taunts (trickster.js, via announce) ---------- */
  cp_trick_preview: 'Did it move?',
  cp_trick_seen: 'That is not where that piece is.',
  cp_trick_melt: 'One of them is running.',
});

export default CP_LEX;
