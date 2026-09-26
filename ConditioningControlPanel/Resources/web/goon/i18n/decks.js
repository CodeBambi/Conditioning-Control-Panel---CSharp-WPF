/* ============================================================================
 * i18n/decks.js - every English copy deck on the Goon page, by key prefix.
 *
 * test/gen-i18n-en.js flattens these into i18n/en.js, and
 * test/selftest-i18n.js fails when the two disagree. A new deck goes on this
 * list or its words never reach a translation table.
 * ==========================================================================*/

import { RAW, ELEMENTS_RAW } from '../ui/strings.js';
import { DUEL_COPY_RAW } from '../ui/duel/copy.js';
import { MERCY_COPY_RAW } from '../ui/mercy.js';

export const DECKS = Object.freeze([
  ['gg', RAW],
  ['gg_element', ELEMENTS_RAW],
  ['gg_duel', DUEL_COPY_RAW],
  ['gg_mercy', MERCY_COPY_RAW],
]);
