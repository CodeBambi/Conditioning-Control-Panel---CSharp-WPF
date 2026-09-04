/* ============================================================================
 * backroom/lex.js - every string the floor can print, in one table.
 *
 * Exported as DATA the way impulse-control/lex.js does it, for two reasons.
 * The page's call sites are authoritative (trap 123), so this table IS the
 * spec that `ArcademyHostService.NeutralLexicon` mirrors - copy the values,
 * do not re-word them. And a table can be diffed against the C# side by a
 * scratch script, where thirty scattered `t('bk_...', '...')` calls cannot.
 *
 * TWO RULES WHEN YOU ADD A ROW.
 *  - Keep it under 96 characters. `MergeModTable` drops anything longer, so a
 *    long row can never be re-voiced by a mod (trap 26). Split it in two
 *    instead of writing one long sentence.
 *  - No em-dashes, and no raw newlines. House prose law, both sides of the seam.
 *
 * The three `{...}` slots are filled by `fill()` below rather than by template
 * literals, so a translator can move them around inside their sentence.
 * ==========================================================================*/

export const BK_LEX = Object.freeze({
  /* ---- the room ---- */
  bk_title:          'The Back Room',
  bk_sub:            'Cash only. Chips only.',
  bk_exit:           'step out',
  bk_back:           'back to the floor',
  bk_chips:          'chips',
  bk_sparkle:        'sparkle',

  /* ---- the pot sign ---- */
  bk_pot:            'the pot',
  bk_pot_sub:        'a quarter of every rake. one royal takes the lot.',
  bk_pot_unknown:    'counting',

  /* ---- the cage ---- */
  bk_cage:           'The Cage',
  bk_cage_rate:      '1 sparkle buys 100 chips. one way, never back.',
  bk_cage_hand:      'sparkle on hand',
  bk_cage_get:       'chips you get',
  bk_cage_go:        'change it',
  bk_cage_custom:    'or name an amount',
  bk_cage_custom_lbl:'a custom amount of sparkle, up to 500',
  bk_cage_dark:      'Cash only. Come back when the line is up.',
  bk_cage_working:   'Counting it out.',
  /* {0} chips credited */
  bk_cage_done:      'That is {0} chips. Try not to spend it all at one cabinet.',
  bk_cage_poor:      'Not quite enough sparkle for that one. The floor keeps.',
  bk_cage_offline:   'The line to the counter is down. Your sparkle is safe where it is.',
  bk_cage_bad:       'The cage takes one to five hundred at a time. Typo guard, nothing more.',
  bk_cage_busy:      'The cashier is with someone. Give her a moment.',
  /* THE TIER GATE, in the counter's own words. `prize_tier` is the standard
     line the Prize Counter says when the bank answers 403 with min_tier, and
     the two rooms must not disagree about what that means, so this row is a
     copy of it rather than a new sentence. */
  bk_cage_locked:    'The bank does not serve this account yet. Nothing was charged.',
  bk_closed:         'The house is closed tonight. Your chips keep. Come back when the sign is lit.',
  bk_cage_reset:     'Your skills are mid reset. The cage waits for that to settle.',
  bk_cage_refused:   'The cage would not take that one. Nothing moved.',

  /* THE VISIBLE CHOICE (owner ruling): what the same sparkle buys upstairs.
     {0} sparkle, {1} the cheapest unowned skill, {2} sparkle still needed. */
  bk_cage_tree:      'The same {0} sparkle goes toward {1} on the tree, {2} short of it.',
  bk_cage_tree_buy:  'The same {0} sparkle buys {1} on the tree outright.',
  bk_cage_tree_done: 'The tree is finished. There is nothing up there left to buy.',

  /* ---- the cabinets ---- */
  bk_cab_twentyone:  'Twenty-One',
  bk_cab_triple:     'Triple Trigger',
  bk_cab_spiral:     'The Spiral',
  bk_cab_scratcher:  'The Scratcher',
  bk_cab_wheel:      'The Prize Wheel',
  bk_sub_twentyone:  'the dealer stands on all seventeens.',
  bk_sub_triple:     'three reels, and they are your own words.',
  bk_sub_spiral:     'twenty-four wedges. one of them is a drop.',
  bk_sub_scratcher:  'three in a row pays. one card a day is free.',
  bk_sub_wheel:      'the big one. seven wedges, two of them worth waiting for.',
  /* {0} the stake in chips */
  bk_cab_stake:      '{0} chips a go',
  bk_cab_free:       'one free card today',

  /* ---- a cabinet with no module behind it yet ---- */
  bk_sheet:          'Not open yet.',
  bk_sheet_sub:      'Still under a sheet. The house is building it.',
  bk_floor_aria:     'the cabinets',
});

/** `fill('a {0} b', ['x'])` -> 'a x b'. Slots a translator may reorder. */
export function fill(text, args) {
  const list = Array.isArray(args) ? args : [];
  return String(text == null ? '' : text).replace(/\{(\d)\}/g, (m, i) => {
    const v = list[Number(i)];
    return v == null ? '' : String(v);
  });
}

/**
 * Wrap the shell's `t` so a caller cannot forget the English floor. A key with
 * no host row behind it still renders its authored value; a missing `t`
 * (a rig, a fixture page) renders the table straight.
 */
export function makeT(t) {
  const base = (typeof t === 'function') ? t : null;
  return function bkT(key, args) {
    const en = BK_LEX[key];
    const raw = base ? base(key, en == null ? key : en) : (en == null ? key : en);
    return args ? fill(raw, args) : raw;
  };
}

export default BK_LEX;
