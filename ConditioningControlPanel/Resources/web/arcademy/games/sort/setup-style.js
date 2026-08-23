/* ============================================================================
 * games/sort/setup-style.js - STUB. LOT G2 owns the real file.
 *
 * The setup door's stylesheet, scoped under `.g-sort-door`. G1's style loader
 * concatenates it onto the game's own sheet (games/sort/style.js imports
 * SETUP_CSS and appends), so the door needs no injection of its own and there
 * is exactly one style element with id `g-sort-style` in the document either
 * way.
 *
 * Empty string on purpose: the stub door paints nothing. When G2 fills this in,
 * two house rules travel with it - every rule scoped under `.g-sort-door`, and
 * NEVER a backtick inside a CSS comment (CLAUDE.md trap 37: it ends the
 * template literal and takes the whole sheet down, and node --check passes it).
 * ==========================================================================*/

export const SETUP_CSS = '';

export default SETUP_CSS;
