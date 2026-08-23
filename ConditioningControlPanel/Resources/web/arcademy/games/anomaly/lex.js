/* ============================================================================
 * games/anomaly/lex.js - every `an_` lexicon row ANOMALY renders, with its
 * neutral English fallback. Exported as DATA (the Impulse Control / Deep End
 * precedent, `IC_LEX` / `DE_LEX`) so the host's `NeutralLexicon` can be
 * mirrored key-for-key: copy the values, never re-word them.
 *
 * RULES (CLAUDE.md trap 26 + the Semester II/III contract): a value longer
 * than 96 characters can never be mod-skinned, so every row here is <= 96; a
 * row is rendered ONLY through ctx.lexicon(key, fallback) - Law VII, accents
 * come from the lexicon, never AI. The DECKS (style/casino/trickster/pressure)
 * may `t()` the same keys; they must not invent new ones without adding the
 * row here, because this table is what the C# mirror is generated from.
 *
 * GROUND-RULES section 2 names four product words nothing Arcademy-side may
 * use, in code, strings or comments. None appears in any row below; the
 * scratch suite greps every file in this folder for the list.
 *
 * NAMING: the anomaly KINDS are internal keys (hue/mirror/scale/rotate/blur/
 * bright/speed/frame - rounds.js compares those strings). The `an_kind_*` rows
 * are DISPLAY ONLY, used on the reveal line and the end card; no rule ever
 * reads a kind NAME.
 * ==========================================================================*/

export const AN_LEX = Object.freeze({
  /* ---- setting row (manifest.settings) -------------------------------- */
  an_kinds: 'Difference kinds',
  an_kinds_hint: 'Gentle keeps colour, mirror and size only. Mirror is always in the pool.',

  /* ---- HUD chips (aria labels; the chip TEXT is live data) ------------- */
  an_chip_round: 'Round',
  an_chip_clock: 'Time left',
  an_chip_streak: 'Streak',

  /* ---- the drawn class-rules sheet (Deck VI, Law IV) ------------------- */
  an_howto_title: 'Class rules',
  an_howto_same: 'Every tile is the same loop, playing in step.',
  an_howto_find: 'One is not. Tap it. The first tap is the one that counts.',
  an_howto_lie: 'The room tints, drifts and glitches every tile at once. That is noise.',
  an_howto_go: 'Open your eyes',

  /* ---- proctor lines (.g-an-msg, one at a time) ------------------------ */
  an_brief: 'One tile is not like the others. Find it before the round runs out.',
  an_play_hint: 'Tap the odd tile.',
  an_found: 'Found.',
  an_found_fast: 'Fast.',
  an_wrong: 'Not that one. That tile is out.',
  an_moved: 'It moved.',
  an_timeout: 'Gone. Next grid.',
  an_reveal: 'It was here.',
  an_streak_lit: 'Five straight. The frame is lit.',
  an_breather: 'Breathe. This one is easy.',
  an_bell: 'Time.',
  an_stamp_bell: 'Bell',
  an_stamp_found: 'Found',

  /* ---- deck lines -------------------------------------------------------
   * casino.js renders an_almost / an_fast / an_royal (single words, < 600ms
   * on screen); trickster.js renders an_refund / an_trick_seen /
   * an_trick_melt. Their English here is COPIED from the decks' own
   * fallbacks - never re-worded, or the two disagree in a mod-less build. */
  an_almost: 'ALMOST',
  an_fast: 'FAST',
  an_royal: 'ROYAL',
  an_refund: '+1s',
  an_trick_seen: 'Did you see that?',
  an_trick_melt: 'The frame runs like wax',
  an_jackpot: 'Sharp eyes.',

  /* ---- end card -------------------------------------------------------- */
  an_end_title: 'Eyes up',
  an_end_found: 'Found',
  an_end_rounds: 'Rounds offered',
  an_end_accuracy: 'First-tap accuracy',
  an_end_median: 'Median find',
  an_end_streak: 'Longest streak',
  an_end_tracked: 'Tracked after a shift',
  an_end_kind: 'Hardest to see',
  an_end_none: 'None',
  an_end_line: 'Global changes are noise. Only local difference is true.',

  /* ---- kind names (display only; the logic compares the KEYS) ---------- */
  an_kind_hue: 'colour',
  an_kind_mirror: 'mirrored',
  an_kind_scale: 'size',
  an_kind_rotate: 'tilt',
  an_kind_blur: 'focus',
  an_kind_bright: 'light',
  an_kind_speed: 'speed',
  an_kind_frame: 'timing',
});

export default AN_LEX;
