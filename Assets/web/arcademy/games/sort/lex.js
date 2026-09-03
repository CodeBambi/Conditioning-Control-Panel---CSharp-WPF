/* ============================================================================
 * games/sort/lex.js - every `sort_` lexicon row SORT renders, with its neutral
 * English fallback. Exported as DATA (the Impulse Control precedent, IC_LEX) so
 * ArcademyHostService.NeutralLexicon can be mirrored key-for-key at integration:
 * copy the values, never re-word them.
 *
 * RULES (CLAUDE.md trap 26): a value longer than 96 characters can never be
 * mod-skinned, so every row here is <= 96. A row is rendered ONLY through
 * ctx.lexicon(key, fallback) in index.js - accents come from the lexicon, never
 * from AI and never from a hardcoded string in the markup.
 *
 * THE STAMPS ARE THE POINT. `sort_stamp_yes` / `sort_stamp_no` are 12 chars max
 * and read at 0.75s, because being mod-neutral is what makes this class fit
 * every mod: Bambi re-voices them GOOD GIRL / NO, Circe MINE / NOT MINE, and the
 * glyph underneath (a heart and a slash) never moves.
 * ==========================================================================*/

export const SORT_LEX = Object.freeze({
  /* ---- the class ------------------------------------------------------ */
  sort_title: 'Sort',
  sort_subtitle: 'Yours to the right. Everything else to the left.',

  /* ---- the stamps (12 chars max, glyph-first) ------------------------- */
  sort_stamp_yes: 'YES',
  sort_stamp_no: 'NO',

  /* ---- keybinds (manifest.keybinds label_key) ------------------------- */
  sort_left_key: 'Swipe left (not yours)',
  sort_right_key: 'Swipe right (yours)',

  /* ---- settings row (manifest.settings) ------------------------------- */
  sort_bg_fade: 'Background fade',
  sort_bg_fade_hint: 'How brightly the sorted wall burns behind the stack.',

  /* ---- the HUD -------------------------------------------------------- */
  sort_chip_chain: 'Chain',
  sort_chip_rung: 'Rung',
  sort_chip_sorted: 'Sorted',
  sort_chip_clock: 'Time left',
  sort_ring_label: 'Time on this card',
  sort_rung_up: 'Rung up',
  sort_rung_down: 'Rung down',

  /* ---- the verdicts --------------------------------------------------- */
  sort_perfect: 'PERFECT',
  sort_just: 'JUST',
  sort_almost: 'ALMOST',
  sort_record: 'record',
  /* THE RECORD PING (casino.js): the chain is one link off your best chain and
     the house wants you to know it. The near-miss you have not lost yet. */
  sort_record_near: 'ONE OFF YOUR BEST',
  /* The jackpot ladder's own word (casino.js). ROYAL has sort_royal already. */
  sort_jackpot: 'JACKPOT',
  sort_pass: 'PASSED',
  sort_wrong: 'WRONG',
  sort_royal: 'ROYAL',

  /* ---- the class rules sheet (one drawn card, and that is the rulebook) */
  sort_rules_title: 'One rule',
  sort_rules_right: 'Right: yours.',
  sort_rules_left: 'Left: everything else.',
  sort_rules_ring: 'The ring closes. Swipe in the gold and the chain grows.',
  sort_rules_pass: 'Let it close and the card comes back. That is not a mistake.',
  sort_rules_keys: 'Arrow keys work too. A key is a swipe.',
  sort_rules_go: 'Begin',

  /* ---- the deal / the thin warning ------------------------------------ */
  sort_dealing: 'Dealing your deck',
  sort_thin: 'Thin pile: expect repeats.',
  sort_quick: 'QUICK SORT: moving to the right, still to the left.',
  sort_no_deck: 'No cards to sort. Your attendance is safe.',

  /* ---- the ticket ------------------------------------------------------ */
  sort_ticket_title: 'The sort',
  sort_ticket_sorted: 'Sorted',
  sort_ticket_perfect: 'Perfect',
  sort_ticket_chain: 'Longest chain',
  sort_ticket_rung: 'Top rung',
  sort_ticket_passed: 'Passed',
  sort_ticket_wrong: 'Wrong',
  sort_submit: 'Submit report',
  sort_gate_hint: 'An S wants near-perfect calls AND a chain that reached the top.',
  sort_perfect_class: 'Clean sort',
  sort_wall_label: 'What you sorted',
});

export default SORT_LEX;
