/* ============================================================================
 * loomSpirals.js - the tiny live store of the player's saved Loom spirals.
 *
 * boot.js fills it from the host's loom-list message (urls point at the
 * ccp.spirals virtual host); bubbles.js fireEffect('spiral') mixes them into
 * the overlay pool ~50/50 with the bundled spirals. Deliberately dumb - all
 * authority (files, caps, validation) is C#-side (DtrhLoomStore).
 * ==========================================================================*/

let spirals = [];   // [{ slug, url, params }]

export function setLoomSpirals(list) {
  spirals = Array.isArray(list) ? list.filter((s) => s && s.url) : [];
}

export function getLoomSpirals() { return spirals; }
