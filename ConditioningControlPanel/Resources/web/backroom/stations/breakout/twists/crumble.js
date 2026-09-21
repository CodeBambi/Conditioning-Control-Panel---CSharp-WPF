/* ============================================================================
 * stations/breakout/twists/crumble.js - the pure sim half of the twist.
 * Twist: Crumble (Pink Fog). Clay takes three hits and gains a crack per hit. A
 * clay brick with one hit left is precarious; popping one pops every adjacent
 * precarious clay brick, in sequence, and those chain on.
 * SCAFFOLD STUB apart from `prime`, which the keys lane calls for the `P` key.
 * The crumble lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

export const id = 'crumble';

/** A clay brick with one hit left: it wobbles, it sheds dust, and it joins a chain. */
export const precarious = br => !!br && br.alive && !!br.clay && br.hp <= 1;

/**
 * Prime every living clay brick to precarious (the `P` key, and anything else
 * that wants the whole board wobbly). Pure sim: one `clayReady` per brick that
 * moved, then one `clayPrimed { n }`. Returns how many bricks it moved.
 * The crumble lane may make this stagger, but it keeps this signature and these
 * events, because the keys lane calls it.
 */
export function prime(g, ctx) {
  let n = 0;
  for (const br of g.bricks) {
    if (!br.alive || !br.clay || br.hp <= 1) continue;
    br.hp = 1;
    n++;
    ctx.emit('clayReady', { x: br.x + br.w / 2, y: br.y + br.h / 2 });
  }
  if (n) ctx.emit('clayPrimed', { n });
  return n;
}

/** Called once after the authored wall is built. */
export function build(g, ctx) { /* the crumble lane fills this in */ }

export default { id, build, prime, precarious };
