/* ============================================================================
 * games/sort/setup-lex.js - STUB. LOT G2 owns the real file.
 *
 * The setup door's own lexicon rows (`sort_door_*`). G1 merges this table into
 * the module's `lexicon` export so the host mirror picks up both halves in one
 * pass: `lexicon: { ...SORT_LEX, ...SETUP_LEX }` in index.js.
 *
 * It is EMPTY on purpose. The stub door renders no copy of its own, so there is
 * nothing to mirror yet; G2 replaces this file wholesale and index.js does not
 * change. Same rules apply when it does: keys prefixed `sort_`, every value
 * <= 96 characters (CLAUDE.md trap 26), no em-dashes.
 * ==========================================================================*/

export const SETUP_LEX = Object.freeze({});

export default SETUP_LEX;
