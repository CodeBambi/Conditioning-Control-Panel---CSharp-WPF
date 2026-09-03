// engine/sessionMetrics.js
// -----------------------------------------------------------------------------
// Per-run engagement counters for "Down the Rabbit Hole" - a sibling of
// assetTracker.js. Where assetTracker tallies attention PER user image, this
// tallies the WHOLE run: bubbles popped, effects shown, boons taken, no-picks,
// junctions forced, subliminals shown, etc.
//
// chaosRun.js drops one-line note() calls at its existing gameplay seams (mostly
// right beside the current lessons.* / bark() calls) and ships a single
// snapshot() home inside the run-ended message's `sessionStats` block. The C#
// side (DtrhSessionStatsStore) sums each run's snapshot into a cumulative,
// local-only record so future features - foremost an end-of-run recap card -
// can read lifetime engagement.
//
// Raw UNCAPPED sums (unlike lessons.js, which caps at a target and freezes on
// completion). No drain/dirty model: the volume is one snapshot per run, so we
// keep a live object and hand out a merged copy at run end.
// -----------------------------------------------------------------------------

// Per-effect on-screen-seconds estimate, kept in sync with lessons.js's BUSY_SEC
// table (in-world effects have no real duration, so this is the canonical guess
// - it's the same number the blindfold lesson already trusts).
const BUSY_SEC = {
  flash: 1.5, subliminal: 1.2, overlay: 3.0, bambifreeze: 3.0,
  bouncingtext: 3.0, video: 15.0, gifcascade: 8.0, htlink: 8.0,
};

export function createSessionMetrics() {
  let m;

  function reset() {
    m = {
      bubblesPopped: 0,
      effectsShown: 0,
      effectsByKind: {},
      gifEffectSeconds: 0,        // BUSY_SEC estimate, summed over every effect
      videoPayloadSecEstimate: 0, // in-world video payloads only (15s each)
      boonsReceived: 0,
      cursesReceived: 0,
      draftSkips: 0,
      draftAutopicks: 0,
      junctionsTaken: 0,
      junctionsForced: 0,
      junctionsPassive: 0,
      subliminalsShown: 0,
    };
  }
  reset();

  return {
    reset,

    noteBubblePopped() { m.bubblesPopped++; },
    noteSubliminalShown() { m.subliminalsShown++; },

    // An effect/gif/flash payload was displayed. `kind` = spec.payload.kind.
    noteEffect(kind) {
      m.effectsShown++;
      const k = String(kind || '').toLowerCase();
      m.effectsByKind[k] = (m.effectsByKind[k] || 0) + 1;
      const sec = BUSY_SEC[k] || 0;
      m.gifEffectSeconds += sec;
      if (k === 'video') m.videoPayloadSecEstimate += sec;
    },

    noteBoon() { m.boonsReceived++; },
    noteCurse() { m.cursesReceived++; },
    noteSkip() { m.draftSkips++; },
    noteAutopick() { m.draftAutopicks++; },

    // A junction resolved. forced = "the only way left"; passive = "you let it
    // choose"; neither = actively chosen. Every resolution counts as taken.
    noteJunction({ forced, passive } = {}) {
      m.junctionsTaken++;
      if (forced) m.junctionsForced++;
      else if (passive) m.junctionsPassive++;
    },

    // Merge the run's live counters with st's already-owned ones (so we never
    // double-track combat outcomes) and hand back a plain object for shipping.
    snapshot(st, depthMeters) {
      return {
        ...m,
        effectsByKind: { ...m.effectsByKind },
        defused: st.defused | 0,
        detonated: st.detonated | 0,
        bestCombo: st.bestCombo | 0,
        loops: st.waveCount | 0,
        depthMeters: Math.round(depthMeters || 0),
        elapsedSec: +(st.elapsedSec || 0).toFixed(1),
      };
    },
  };
}
