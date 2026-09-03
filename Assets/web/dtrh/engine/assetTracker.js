// engine/assetTracker.js
// -----------------------------------------------------------------------------
// Per-asset engagement accumulator for "Down the Rabbit Hole".
//
// Every user image/gif/video that rides the tube earns two kinds of signal:
//   - attention  : weighted on-screen time (a card centred + close counts far
//                  more than one drifting past the edge)
//   - interaction: deliberate acts - grabbing it as a paddle, and the pops /
//                  defuses / flings it lands while held
//
// The run brain drains the dirty delta on a heartbeat and at run end and posts
// it home over the bridge; the C# side (DtrhAssetStatsStore) sums deltas into a
// cumulative record so future features can bias toward what the user likes.
//
// Delta model: drain() returns only what changed since the last drain and zeroes
// those counters (the row itself lingers so `kind` stays known). The host adds.
// -----------------------------------------------------------------------------

export function createAssetTracker() {
  const stats = new Map(); // name -> { kind, seconds, weighted, grabs, pops, defuses, flings }
  const dirty = new Set(); // names touched since the last drain

  function row(name, kind) {
    let r = stats.get(name);
    if (!r) {
      r = { kind: kind || 'image', seconds: 0, weighted: 0, grabs: 0, pops: 0, defuses: 0, flings: 0 };
      stats.set(name, r);
    } else if (kind && r.kind !== kind) {
      r.kind = kind;
    }
    return r;
  }

  return {
    // per-frame: `dt` seconds of on-screen time carrying attention `weight` (0..1).
    noteVisible(name, kind, dt, weight) {
      if (!name || !(dt > 0)) return;
      const r = row(name, kind);
      r.seconds += dt;
      r.weighted += dt * (weight > 0 ? weight : 0);
      dirty.add(name);
    },
    noteGrab(name, kind) {
      if (!name) return;
      row(name, kind).grabs += 1;
      dirty.add(name);
    },
    // counts = { popped, defused, flung } landed while this asset was the paddle.
    noteInteraction(name, counts) {
      if (!name || !counts) return;
      const r = row(name);
      if (counts.popped) r.pops += counts.popped;
      if (counts.defused) r.defuses += counts.defused;
      if (counts.flung) r.flings += counts.flung;
      dirty.add(name);
    },
    // Return + reset the delta since the last drain. Empty array when nothing moved.
    drain() {
      if (!dirty.size) return [];
      const out = [];
      for (const name of dirty) {
        const r = stats.get(name);
        if (!r) continue;
        out.push({
          name, kind: r.kind,
          seconds: +r.seconds.toFixed(3),
          weighted: +r.weighted.toFixed(3),
          grabs: r.grabs, pops: r.pops, defuses: r.defuses, flings: r.flings,
        });
        r.seconds = 0; r.weighted = 0; r.grabs = 0; r.pops = 0; r.defuses = 0; r.flings = 0;
      }
      dirty.clear();
      return out;
    },
    hasPending() { return dirty.size > 0; },
  };
}
