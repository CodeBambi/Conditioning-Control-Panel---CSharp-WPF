/* ============================================================================
 * games/sort/setup-lex.js - every `sort_` lexicon row THE DOOR renders, with
 * its neutral English fallback. Exported as DATA (the IC_LEX / DE_LEX / EC_LEX
 * precedent) so `ArcademyHostService.NeutralLexicon` can be mirrored key for
 * key: copy the values, never re-word them.
 *
 * RULES (web CLAUDE.md trap 26 + the SORT contract):
 *   - every value <= 96 characters, or a mod can never re-voice it
 *   - a row is rendered ONLY through ctx.lexicon(key, fallback)
 *   - no em-dashes anywhere
 *   - the eight noise starter subs (cats, aww, pokemon, ...) are DATA, not
 *     lexicon: they are subreddit names, and a mod re-voicing them would ask
 *     the host for a subreddit that does not exist.
 *
 * G1 merges this table into the module's lexicon as
 *   lexicon: { ...SORT_LEX, ...SETUP_LEX }
 * `sort_stamp_yes` / `sort_stamp_no` live in BOTH tables on purpose (the door's
 * ghost round stamps the same two words the room stamps). The values are
 * identical, so the spread is a no-op either way; if G1 would rather own them
 * alone, delete them here, not there.
 * ==========================================================================*/

export const SETUP_LEX = Object.freeze({
  /* ---- the door itself -------------------------------------------------- */
  sort_door_title: 'Set your sort',
  sort_door_sub: 'Right is yours. Left is the rest.',
  sort_door_step: 'Step',
  sort_leave: 'Leave',
  sort_back: 'Back',
  sort_next: 'Next',
  sort_play: 'Deal me in',
  sort_dealing: 'Dealing your deck',
  sort_vetting: 'Checking your cards',
  sort_vet_more: 'Fetching more cards',
  sort_vs: 'vs',

  /* ---- first night hand holding (hidden by Skip class tutorials) --------- */
  sort_tut_rule: 'One rule all class: yours goes right, the rest goes left.',
  sort_tut_pick: 'You pick both piles now. They do not change once the bell rings.',
  sort_tut_ghost: 'Watch two cards sort themselves, then it is your turn.',

  /* ---- step 1: the source ----------------------------------------------- */
  sort_step_source: 'Where from',
  sort_source_online: 'Online',
  sort_source_online_hint: 'Niches and subs from the web feed',
  sort_source_online_off: 'Online media is off in your settings',
  sort_source_local: 'My folders',
  sort_source_local_hint: 'Folders and presets from your own assets',
  sort_source_local_off: 'Not enough folders or presets to make two piles',

  /* ---- step 2 / 3: the piles -------------------------------------------- */
  sort_step_target: 'Your pile',
  sort_step_noise: 'The rest',
  sort_target_head: 'What do you want?',
  sort_target_hint: 'These go RIGHT. Pick one or more.',
  sort_noise_head: 'What is the rest?',
  sort_noise_hint: 'These go LEFT. Pick one or more.',
  sort_catalog_head: 'Niches',
  sort_lib_head: 'My library',
  sort_lib_empty: 'Nothing here yet. Search for a sub below.',
  sort_starter_head: 'Easy noise',
  sort_starter_hint: 'One tap. Checked once, then yours forever.',

  /* ---- the search box ---------------------------------------------------- */
  sort_search_head: 'Add a sub',
  sort_search_ph: 'subreddit name',
  sort_search_btn: 'Add',
  sort_probe_probing: 'Checking',
  sort_probe_ok: 'Added to your library',
  sort_probe_missing: 'Not found',
  sort_probe_bad: 'That is not a subreddit name',
  sort_probe_dupe: 'Already on your list',

  /* ---- library pills ----------------------------------------------------- */
  sort_clips: 'clips',
  sort_stills_only: 'stills only',
  sort_verified: 'verified',
  sort_unverified: 'never checked',
  sort_missing: 'gone from the feed',
  sort_remove: 'Remove from my library',

  /* ---- local picking ----------------------------------------------------- */
  sort_folders_head: 'Folders',
  sort_presets_head: 'Or a whole preset',
  sort_preset_none: 'No preset',
  sort_folder_taken: 'on the other pile',
  sort_counts: 'items',

  /* ---- the rules the door enforces --------------------------------------- */
  sort_need_pick: 'Pick at least one first',
  sort_need_split: 'The two piles cannot be the same',
  sort_overlap_note: 'Shared subs were dropped from the rest',
  sort_spice_mild: 'Mild. These two are easy to tell apart.',
  sort_spice_mid: 'Warm. Your own picks on both sides.',
  sort_spice_hot: 'Hot. These two niches share ground.',
  sort_thin: 'Thin pick. Expect repeats.',
  sort_thin_add: 'Add another pick',

  /* ---- night two --------------------------------------------------------- */
  sort_same: 'Same sort',
  sort_change: 'Change my sort',
  sort_stale: 'That sort is gone. Pick again.',

  /* ---- QUICK SORT (the dark campus fallback) ----------------------------- */
  sort_quick_head: 'Quick sort',
  sort_quick_rule: 'Moving goes right. Still goes left.',
  sort_quick_nag: 'Turn on online media or add a second folder for a real sort.',

  /* ---- the ghost round --------------------------------------------------- */
  sort_ghost_head: 'Like this',
  sort_ghost_target: 'yours',
  sort_ghost_noise: 'the rest',
  sort_stamp_yes: 'YES',
  sort_stamp_no: 'NO',
});

export default SETUP_LEX;
