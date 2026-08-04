// xoshiro256** — bit-for-bit port of Services/GoonGame/GoonRng.cs.
//
// Both clients build this from the same 64-bit seed and MUST produce the same stream, so every
// line here is a transcription, not an implementation. All state is BigInt masked to 64 bits
// after every add/multiply/shift; C# ulong arithmetic wraps, BigInt does not.
//
// NOT reusable across consumers: a shared sequence drawn from two places desyncs by construction.
// Give each system its own instance via derive().

const M = (1n << 64n) - 1n;            // also ulong.MaxValue — NextInt's limit math relies on that
const GOLDEN = 0x9E3779B97F4A7C15n;

function rotl(x, k) {
  return ((x << k) | (x >> (64n - k))) & M;
}

/** splitmix64. Returns [value, nextState] — C# passes the state by ref. */
function splitmix64(state) {
  const s = (state + GOLDEN) & M;
  let z = s;
  z = ((z ^ (z >> 30n)) * 0xBF58476D1CE4E5B9n) & M;
  z = ((z ^ (z >> 27n)) * 0x94D049BB133111EBn) & M;
  return [z ^ (z >> 31n), s];
}

export class GoonRng {
  constructor(seed) {
    let s = BigInt.asUintN(64, BigInt(seed));
    this.seed = s;
    let v;
    [v, s] = splitmix64(s); this._s0 = v;
    [v, s] = splitmix64(s); this._s1 = v;
    [v, s] = splitmix64(s); this._s2 = v;
    [v, s] = splitmix64(s); this._s3 = v;
  }

  /** @returns {bigint} */
  nextULong() {
    const result = (rotl((this._s1 * 5n) & M, 7n) * 9n) & M;
    const t = (this._s1 << 17n) & M;
    this._s2 ^= this._s0;
    this._s3 ^= this._s1;
    this._s1 ^= this._s2;
    this._s0 ^= this._s3;
    this._s2 ^= t;
    this._s3 = rotl(this._s3, 45n);
    return result;
  }

  /**
   * Uniform in [minInclusive, maxExclusive) by rejection sampling. The rejection loop is part of
   * the deterministic stream — a modulo shortcut would consume a different number of draws and
   * desync the peer the moment a range isn't a power of two.
   */
  nextInt(minInclusive, maxExclusive) {
    if (maxExclusive <= minInclusive) return minInclusive;
    const range = BigInt(maxExclusive - minInclusive);
    const limit = M - (M % range);
    let draw;
    do { draw = this.nextULong(); } while (draw >= limit);
    return minInclusive + Number(draw % range);
  }

  /** Uniform in [0,1). Top 53 bits — the shifted value is exactly representable as a double. */
  nextDouble() {
    return Number(this.nextULong() >> 11n) * 2 ** -53;
  }

  /** Fisher-Yates, in place. */
  shuffle(list) {
    if (!list) return list;
    for (let i = list.length - 1; i > 0; i--) {
      const j = this.nextInt(0, i + 1);
      const tmp = list[i]; list[i] = list[j]; list[j] = tmp;
    }
    return list;
  }

  pick(list) {
    if (!list || list.length === 0) return undefined;
    return list[this.nextInt(0, list.length)];
  }

  /**
   * A sub-stream for one purpose. FNV-1a 64 over UTF-16 CODE UNITS (charCodeAt, never a
   * for...of which would yield code POINTS and diverge from C#'s char enumeration on any
   * astral character), XOR'd into the parent seed, then one splitmix64 step.
   */
  static derive(parentSeed, purpose) {
    let h = 0xcbf29ce484222325n;
    const s = purpose == null ? '' : String(purpose);
    for (let i = 0; i < s.length; i++) {
      h ^= BigInt(s.charCodeAt(i));
      h = (h * 0x100000001b3n) & M;
    }
    const mixed = BigInt.asUintN(64, BigInt(parentSeed)) ^ h;
    return new GoonRng(splitmix64(mixed)[0]);
  }
}

// ------------------------------------------------------------------ seeds

/**
 * This client's contribution to a shared seed. Cryptographic by requirement: the XOR commit only
 * denies control to the opponent if neither side can predict the other's word. Math.random is
 * never acceptable here.
 */
export function newSeedContribution() {
  const c = globalThis.crypto;
  if (!c || typeof c.getRandomValues !== 'function') {
    throw new Error('newSeedContribution: no CSPRNG (crypto.getRandomValues) available');
  }
  if (typeof BigUint64Array === 'function') {
    const buf = new BigUint64Array(1);
    c.getRandomValues(buf);
    return buf[0] & M;
  }
  const w = new Uint32Array(2);
  c.getRandomValues(w);
  return ((BigInt(w[0]) << 32n) | BigInt(w[1])) & M;
}

/** Shared seed = XOR of both contributions. Commutative on purpose — no ordering to agree on. */
export function combineSeeds(a, b) {
  return (BigInt.asUintN(64, BigInt(a)) ^ BigInt.asUintN(64, BigInt(b))) & M;
}

/**
 * Per-element sub-seed for the draft cue generator.
 * NOTE: lives in GoonDraft.SaltSeed on the C# side, exported here so the Wave-2 draft port has
 * exactly one blessed implementation to call.
 */
export function saltSeed(matchSeed, elementCode) {
  const salt = ((BigInt.asUintN(64, BigInt(elementCode) + 1n) * GOLDEN) & M);
  return (BigInt.asUintN(64, BigInt(matchSeed)) ^ salt) & M;
}

// ------------------------------------------------------------------ seed transport

/** Seeds cross the wire as decimal strings; they live in memory as BigInt. */
export function seedToString(s) {
  return BigInt.asUintN(64, BigInt(s)).toString();
}

/**
 * Accepts bigint | decimal string | number. Mirrors UInt64AsStringConverter.ReadJson: anything
 * unparseable or out of ulong range reads as 0 rather than throwing.
 *
 * WARNING on the number path: a JSON number above 2^53 has ALREADY lost its low bits by the time
 * it reaches us — the value is wrong and the round will render differently than the peer's. It is
 * accepted only because the spec says readers must tolerate a naive peer's encoding.
 */
export function seedFromAny(v) {
  if (v === null || v === undefined) return 0n;
  if (typeof v === 'bigint') return BigInt.asUintN(64, v);
  if (typeof v === 'number') {
    if (!Number.isFinite(v) || v < 0) return 0n;
    const b = BigInt(Math.trunc(v));
    return b > M ? 0n : b;
  }
  if (typeof v === 'string') {
    const s = v.trim();
    if (s === '') return 0n;
    try {
      const b = BigInt(s);
      return b < 0n || b > M ? 0n : b;
    } catch {
      return 0n;
    }
  }
  return 0n;
}

export const UINT64_MASK = M;
