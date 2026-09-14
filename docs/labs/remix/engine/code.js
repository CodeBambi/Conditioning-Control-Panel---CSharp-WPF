/* ============================================================================
 * code.js - the remix code, CCP-XXXX.
 *
 * Four characters from a 32 glyph alphabet with the ambiguous ones (0/O, 1/I/L)
 * removed, so a code read off a screenshot types back in cleanly. The code IS
 * the project seed: same code, same dice.
 *
 * seed range is 0 .. 32^4-1 (1048575). Round trip is exact both ways.
 * ==========================================================================*/

export const ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
export const CODE_LEN = 4;
export const SEED_MAX = Math.pow(ALPHABET.length, CODE_LEN); // 1048576, exclusive

// Glyphs we dropped, folded onto the survivor they get mistaken for, so a code
// retyped off a screenshot still resolves instead of erroring.
const FOLD = { '0': 'Q', 'O': 'Q', '1': 'J', 'I': 'J' };

const LOOKUP = new Map();
for (let i = 0; i < ALPHABET.length; i++) LOOKUP.set(ALPHABET[i], i);
for (const k of Object.keys(FOLD)) LOOKUP.set(k, ALPHABET.indexOf(FOLD[k]));

/** seed (int) -> 'CCP-XXXX'. Seeds outside the range wrap. */
export function seedToCode(seed) {
  let n = ((seed % SEED_MAX) + SEED_MAX) % SEED_MAX;
  let out = '';
  for (let i = 0; i < CODE_LEN; i++) {
    out = ALPHABET[n % ALPHABET.length] + out;
    n = Math.floor(n / ALPHABET.length);
  }
  return 'CCP-' + out;
}

/** 'CCP-XXXX' or 'xxxx' -> seed int. Throws on anything unreadable. */
export function codeToSeed(code) {
  const body = normalizeCode(code);
  let n = 0;
  for (let i = 0; i < CODE_LEN; i++) {
    const v = LOOKUP.get(body[i]);
    if (v === undefined) throw new Error('That is not a remix code');
    n = n * ALPHABET.length + v;
  }
  return n;
}

/** Strip prefix, punctuation and case. Returns the 4 raw chars. */
export function normalizeCode(code) {
  const s = String(code || '').toUpperCase().replace(/[^0-9A-Z]/g, '');
  const body = s.startsWith('CCP') ? s.slice(3) : s;
  if (body.length !== CODE_LEN) throw new Error('That is not a remix code');
  return body;
}

/** True when the string parses. Never throws. */
export function isCode(code) {
  try { codeToSeed(code); return true; } catch { return false; }
}

/** A fresh seed. Pass a source of randomness to keep it testable. */
export function randomSeed(rand = Math.random) {
  return Math.floor(rand() * SEED_MAX) % SEED_MAX;
}

/** A fresh code. */
export function randomCode(rand = Math.random) {
  return seedToCode(randomSeed(rand));
}
