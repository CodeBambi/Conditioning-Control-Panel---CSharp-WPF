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
    // Passed straight through (the `platform` precedent) so makeCaps owns the default. This one is
    // NOT a set and takes part in NO intersection: it is the version discriminator for the P2P
    // media-transfer protocol, and the caller decides it — boot.js advertises it true for every
    // build that ships net/mediaChannel.js, while a headless/test caller that omits it stays false
    // and behaves exactly like a client that predates the feature.
    transfer: overrides.transfer,
    // The voice-note revision, on the exact same terms as `transfer` above: passed straight
    // through so makeCaps owns the default, in NO intersection, and never able to fail a lobby.
    // boot.js advertises VOICE_CAP_VERSION for every build that ships ui/voice/voiceService.js;
    // a headless/test caller that omits it stays 0 and is indistinguishable from a peer built
    // before the feature existed — which is the ONLY thing that keeps us from sending into the
    // dark, because `t:'voice'` is fire-and-forget and an old peer drops it without a word.
    voice: overrides.voice,
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
