/* ============================================================================
 * games/the-deep-end/lex.js - every `de_` lexicon row THE DEEP END renders,
 * with its neutral English fallback. Exported as DATA (the Impulse Control
 * precedent, `ic/lex.js` IC_LEX) so the host's `NeutralLexicon` can be
 * mirrored key-for-key: copy the values, never re-word them.
 *
 * RULES (CLAUDE.md trap 26 + the contract): a value longer than 96 characters
 * can never be mod-skinned, so every row here is <= 96; a row is rendered ONLY
 * through ctx.lexicon(key, fallback) in index.js (Law VII - accents come from
 * the lexicon, never AI). Tier names are the trance-depth ladder the mod
 * re-voices; the LOGIC compares integers (tier_1..tier_11) and never a name.
 *
 * GROUND-RULES §2 names four product words this game may never use, in code,
 * strings or comments. None appears in any row below; the scratch test suite
 * holds the list and greps every file in this folder for it.
 * ==========================================================================*/

export const DE_LEX = Object.freeze({
  /* ---- the ladder (data-tier 1..11) ----------------------------------- */
  de_tier_1: 'Awake',
  de_tier_2: 'Fuzzy',
  de_tier_3: 'Drowsy',
  de_tier_4: 'Heavy',
  de_tier_5: 'Drifting',
  de_tier_6: 'Sinking',
  de_tier_7: 'Sunken',
  de_tier_8: 'Submerged',
  de_tier_9: 'Fathoms',
  de_tier_10: 'Trench',
  de_tier_11: 'Blackout',
  /** The inert tile (tier 0). It slides; it never sinks. */
  de_tier_silt: 'Silt',

  /* ---- setting row (manifest.settings) -------------------------------- */
  de_board_size: 'Board size',
  de_board_size_hint: '5x5 slows the pressure for a longer soak. Only 4x4 can earn an S.',

  /* ---- HUD chips (aria labels; the chip TEXT is live data) ------------- */
  de_chip_depth: 'Depth',
  de_chip_clock: 'Time left',
  de_chip_score: 'Score',
  de_chip_chain: 'Chain',

  /* ---- proctor lines (.g-de-msg, one at a time) ------------------------ */
  de_brief: 'Swipe. Equal tiles sink together. Every depth makes the room heavier.',
  de_play_hint: 'Arrows, WASD or swipe.',
  de_new_depth: 'New depth.',
  de_lifetime_new: 'A new lifetime depth.',
  de_strain: 'Almost. They strain toward each other.',
  de_exhale_line: 'Exhale. The room eases for ten seconds, and the next tile will fit.',
  de_resurface_line: 'The board locked. The depth is banked. Fresh water.',
  de_silt_line: 'Silt. It slides, it never sinks, it never leaves.',
  de_bell_warn: 'Twenty seconds.',
  de_bell_line: 'The bell. Up you come.',
  de_ceiling_line: 'Tier eleven. The ladder ends here, warm.',
  de_retake: 'Retake',

  /* ---- the trickster's two taunts (trickster.js, via opts.announce) ---- */
  de_trick_melt: 'The shallows run like wax',
  de_trick_seen: 'Did you see that?',

  /* ---- stamps + ceremonies --------------------------------------------- */
  de_stamp_depth: 'NEW DEPTH',
  de_stamp_resurface: 'RESURFACE',
  de_stamp_bell: 'BELL',
  de_stamp_ceiling: 'ALL THE WAY DOWN',
  de_jackpot: 'JACKPOT',
  de_near_miss: 'SO CLOSE',

  /* ---- the end card (.g-de-end) --------------------------------------- */
  de_end_title: 'Dive report',
  de_end_best: 'Best dive',
  de_end_chains: 'Chains',
  de_end_efficiency: 'Merges per swipe',
  de_end_survival: 'Survived the bell',
  de_end_resurfaces: 'Resurfaces',
  de_end_score: 'Score',
  de_end_ceiling: 'Reached the end of the ladder',
  de_end_yes: 'Yes',
  de_end_no: 'No',
  de_end_dare: 'Lifetime deepest',
  de_end_dare_line: 'Your standing dare. Beat it next class.',
  de_end_dare_first: 'Your first mark on the ladder. Beat it next class.',

  /* ---- pass 2: tile faces setting + the stuck hint ---------------------- */
  de_tile_faces: 'Tile faces',
  de_tile_faces_hint: 'Your own media on every tile, tinted by depth. Still = no loops. Plain = colour only.',
  de_tile_faces_media: 'Media',
  de_tile_faces_still: 'Still',
  de_tile_faces_plain: 'Plain',
  de_stuck_hint: 'Nothing is locked. The lit edges still move.',

  /* ---- pass 2: FREE SWIM (endless, untimed, ungraded) ------------------- */
  de_free_swim: 'Free Swim',
  de_free_swim_hint: 'No bell, no grade - swim until you surface.',
  de_surface: 'Surface',
  de_end_title_free: 'Free swim over',
  de_end_dives: 'Dives',
  de_end_time: 'Time',
  de_brief_free: 'No bell tonight. Sink as far as you like - tap Surface when you are done.',
});

export default DE_LEX;
