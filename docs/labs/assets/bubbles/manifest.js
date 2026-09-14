/* ============================================================================
 * manifest.js — what the Bubble Pop preview is allowed to show/play.
 *
 * Hand-picked content only. The explicit flash gifs and spoken voice lines are
 * NOT auto-imported; you choose exactly which go live, and this file is the
 * single source of truth the preview reads.
 *
 * To add flash clips: drop web-optimized .webm/.mp4 files in assets/bubbles/flash/
 * and list their filenames in FLASH_CLIPS. (Use tools/optimize-flash.sh to turn
 * raw gifs into small muted webms.)
 * To add voice lines: drop .mp3 files in assets/bubbles/voices/ and list them in
 * VOICES. These only play when the visitor opts into "Voices".
 * ==========================================================================*/

// Flash-image clips shown by Trigger bubbles (filenames under assets/bubbles/flash/).
// Empty = Trigger bubbles fall back to the built-in CSS flash.
export const FLASH_CLIPS = [
  '20240328_163344_.webm',
  '20241021_110458_.webm',
  'c1b77x9zpb8g1_.webm',
  'SPOILER_zx2ku23ddn_.webm',
  'TrackMatteDFC_.webm',
  'SPOILER_Tumblr_l_358184837126956_.webm',
  'Tumblr_l_879195283460911_.webm',
  '0_bambi2_.webm',
  '8csmnbzhgt8g1_.webm',
  'bg3_.webm',
  'UG9zdDpiNzNkMmMwMGYxMDlkMDc3ZTVmODg0NDkwOGUyNmQw.webm',
  'user3318173_39186c0bc51f_.webm',
];

// Spoken voice lines under assets/bubbles/voices/. Voices are OFF by default; the
// visitor must flip the "Voices" toggle. FLASH_VOICES play on Flash/Trigger pops,
// SUB_VOICES play on Subliminal pops. Empty = no voice playback for that type.
export const FLASH_VOICES = [
  'flash_dumb_bimbo_is_fun.mp3',
  'flash_good_girls_dont_think.mp3',
  'flash_dont_think_silly.mp3',
  'flash_turn_your_brain_off.mp3',
  'flash_being_dumb_is_sexy.mp3',
  'flash_thinking_is_too_hard.mp3',
  'flash_hypnotized_turns_me_on.mp3',
  'flash_brainwashed_bimbo.mp3',
  'flash_accept_your_conditioning.mp3',
  'flash_cant_resist_triggers.mp3',
  'flash_hypnosis_brain_off.mp3',
  'flash_love_being_stupid.mp3',
  'flash_too_dumb_to_think.mp3',
  'flash_empty_heads.mp3',
  'flash_hypnotized_horny_bimbo.mp3',
  'flash_sexy_bimbo.mp3',
  'flash_horny_brainwashed.mp3',
  'flash_perfect_bimbo.mp3',
];
export const SUB_VOICES = [
  'sub_good_girl.mp3',
  'sub_just_obey.mp3',
  'sub_bambi_sleep.mp3',
  'sub_bimbo_doll.mp3',
  'sub_dont_think_silly.mp3',
  'sub_turn_brain_off.mp3',
  'sub_good_girls_dont_think.mp3',
  'sub_snap_and_forget.mp3',
  'sub_bambi_freeze.mp3',
  'sub_bambi_reset.mp3',
  'sub_does_as_told.mp3',
  'sub_uniform_lock.mp3',
  'sub_giggletime.mp3',
  'sub_cant_resist_triggers.mp3',
  'sub_primped_pampered.mp3',
  'sub_no_need_to_think.mp3',
];

// Subliminal words flashed by Subliminal trigger pops. Edit freely.
export const SUBLIMINAL_WORDS = [
  'Obey', 'Drop', 'Good girl', 'Sink', 'Blank',
  'Empty', "Don't think", 'Mindless', 'Bimbo', 'Sleep',
  'Submit', 'Deeper',
];

// Whispered "trigger drop" SFX for the built-in SUBLIMINAL_WORDS, under
// assets/bubbles/drops/. Keyed by the same slug the generator uses (lowercased,
// runs of non-alphanumerics -> '_'). When a word flashes on a Subliminal pop,
// bubbles.js plays its matching drop; custom user words have none and stay
// silent. Produced by ccp-trailer/gen_sub_drops.py.
export const SUBLIMINAL_DROPS = {
  obey: 'drop_obey.mp3',
  drop: 'drop_drop.mp3',
  good_girl: 'drop_good_girl.mp3',
  sink: 'drop_sink.mp3',
  blank: 'drop_blank.mp3',
  empty: 'drop_empty.mp3',
  don_t_think: 'drop_don_t_think.mp3',
  mindless: 'drop_mindless.mp3',
  bimbo: 'drop_bimbo.mp3',
  sleep: 'drop_sleep.mp3',
  submit: 'drop_submit.mp3',
  deeper: 'drop_deeper.mp3',
};
