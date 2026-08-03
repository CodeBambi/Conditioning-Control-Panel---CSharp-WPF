// Capability advertisement + intersection — port of GoonCapabilities in
// Services/GoonGame/GoonMatchTypes.cs.
//
// Every cross-client rule is an INTERSECTION of the two hellos: the draft pool is the
// intersection of both `elements` sets, a sender may only send payload kinds the RECEIVER
// advertised, and sudden-death kinds come from the `rounds` intersection. ReactionDuel is the
// universal fallback every client MUST support (protocol §7).

import { GoonPayloadKind, GoonRoundKind, GoonConsts, makeCaps } from './contracts.js';
import { PoolV1 } from './draft.js';

/** Every client must be able to run a reaction duel. */
export const UNIVERSAL_ROUND = GoonRoundKind.ReactionDuel;

const ALL_PAYLOADS = Object.freeze(Object.values(GoonPayloadKind));
const ALL_ROUNDS = Object.freeze(Object.values(GoonRoundKind));

/**
 * What this client advertises. C# `GoonCapabilities.Local()` hard-codes platform "windows" and
 * the full sets; this binding advertises platform "web" (contracts.js makeCaps default) with the
 * same full sets, and takes an override so the page can honestly narrow any list it cannot run
 * without editing this file. Advertising a SHORTER list than you can run is always safe; a
 * longer one is not.
 */
export function local(overrides = {}) {
  return makeCaps({
    platform: overrides.platform,
    payloads: overrides.payloads ?? ALL_PAYLOADS.slice(),
    elements: overrides.elements ?? PoolV1.slice(),
    rounds: overrides.rounds ?? ALL_ROUNDS.slice(),
    min_v: overrides.min_v ?? GoonConsts.ProtocolVersion,
  });
}

/**
 * Intersection of two advertisements. A peer that advertised nothing (older client, or a hello
 * without caps) is treated as "everything we support" so v1 peers still work.
 */
export function intersect(mine, theirs) {
  const m = Array.from(new Set(mine || []));
  const t = Array.from(new Set(theirs || []));
  if (t.length === 0) return m;
  if (m.length === 0) return t;
  return m.filter((x) => t.includes(x));
}
