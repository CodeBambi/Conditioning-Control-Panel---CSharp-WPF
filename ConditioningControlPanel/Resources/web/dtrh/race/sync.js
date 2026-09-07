/* ============================================================================
 * race/sync.js - the visible half of a cue waits for the word.
 *
 *   createCueSync({ aheadSec, lateSec, trace }) -> { depthFor, defer, claim, trackRow, update, reset, trace }
 *
 * WHY THIS EXISTS. The scheduler (race/chart.js createScheduler) hands an event
 * over LEAD_SEC (2.5 s) before its second, on purpose: a bubble has to be placed
 * down the road early enough that the kart reaches it exactly when the voice says
 * the word. run.js used to spend the WHOLE cue at that handover, spawns and all,
 * so the plate flew, the toast landed, EMI reached, the fog rolled in and the mix
 * poured two and a half seconds before the word was said. That is the "too soon
 * by 2 sec or so" the owner heard on the phone, measured at 2.3 to 2.5 s early in
 * race/smoke/sync-check.mjs.
 *
 * So a cue is now two halves. The SPAWNS still go down at handover, at the depth
 * the kart will have reached by the word. Everything the player sees or hears
 * (plate, toast, mood, pose, jump, mix, fog, boost, density, hold) is held here
 * and fired from the frame loop when the track clock reaches `event.t`. Nothing
 * here reads a clock of its own: `update(t)` is handed the track second, so a
 * pause drains nothing (the second stops), a resume needs no telling, and a seek
 * rebuilds the queue (a jump back drops what the scheduler is about to hand over
 * again; a jump forward drops what the voice already said past LATE_SEC).
 *
 * THE ROW. A row is placed at `d = kartD + speed * dueIn`, which assumes the kart
 * holds its speed for the whole lookahead. It does not: a boost, a slow, the
 * opening ramp all move the arrival. So a row is tracked here until the kart is
 * on it and re-placed every frame off the CURRENT speed and the time still to go,
 * until only AHEAD_SEC is left; over that last quarter second the kart cannot
 * change speed enough to matter (under 0.05 s at the hardest boost). The row is
 * under the kart's nose when the word lands, whatever the throttle did.
 *
 * Pure: no three, no DOM, no clock, so `node race/smoke/rows-check.mjs` drives it
 * with a simulated kart whose speed ramps mid-lookahead.
 * ==========================================================================*/

/** A spawn never lands nearer than this many seconds of road ahead of the kart (run.js's own floor). */
export const CUE_AHEAD_SEC = 0.25;
/** A held cue found this far past its second is let go, not fired: the voice already said it. One
 *  slow frame (track.js MAX_STEP_SEC) is inside this; a seek forward is not. */
export const LATE_SEC = 1.0;
/** A clock that jumps back further than this is a seek (chart.js SEEK_SEC, the same number). */
export const SEEK_SEC = 1.0;
/** The standalone trace keeps this many events; the smokes read it, the game never does. */
export const TRACE_MAX = 64;

const num = (v, d) => (typeof v === 'number' && isFinite(v) ? v : d);

export function createCueSync({ aheadSec = CUE_AHEAD_SEC, lateSec = LATE_SEC, trace = false } = {}) {
  const ahead = Math.max(0, num(aheadSec, CUE_AHEAD_SEC));
  const late = Math.max(0, num(lateSec, LATE_SEC));
  let queue = [];         // { at, event, cue }: the visible half, waiting for the second
  let rows = [];          // { rowId, at, d }: a row being kept under its word
  let lastT = 0, dropped = 0;
  const log = trace ? new Map() : null;

  /** One trace line per event id, capped; `firedAt` is the frame the visible half went out on. */
  function rec(event, patch) {
    if (!log || !event) return;
    const id = String(event.id);
    let row = log.get(id);
    if (!row) {
      if (log.size >= TRACE_MAX) log.delete(log.keys().next().value);
      row = { id, kind: event.kind, t: event.t, handedAt: null, firedAt: null, dropped: false, rowPlacedAt: null, rowAt: null };
      log.set(id, row);
    }
    Object.assign(row, patch);
  }

  /** The depth a spawn due at track second `at` belongs at, seen from the kart right now. */
  function depthFor(t, kartD, speed, at) {
    return num(kartD, 0) + Math.max(0, num(speed, 0)) * Math.max(num(at, t) - t, ahead);
  }

  return {
    depthFor,

    /** Hold the visible half of a cue until the clock reaches the event's second. `t` is now. */
    defer(event, cue, t) {
      if (!event || !cue) return;
      const at = num(event.t, 0);
      queue.push({ at, event, cue });
      rec(event, { handedAt: num(t, null) });
    },

    /**
     * THE PLATE, ON THE POP. A row taken EARLY: the deferred cue is marked plated and handed back,
     * so run.js can fly the word at the camera the moment the player takes it rather than making
     * them wait for a second they already beat. Everything else in that cue (the mix, the mood, the
     * fog, the boost) still fires at `event.t` and nothing here moves it: only the plate travels.
     * Returns the held `{ event, cue }` the FIRST time an id is claimed and null every time after,
     * so a row of five popped bubbles plates once and a row nobody takes still plates on its second.
     */
    claim(eventId) {
      if (!eventId) return null;
      for (const q of queue) {
        if (q.event.id !== eventId) continue;
        if (q.plated) return null;
        q.plated = true;
        return { event: q.event, cue: q.cue };
      }
      return null;
    },

    /** A row went down at depth `d` for the word at `at`: keep it under the word until the kart is on it. */
    trackRow(rowId, event, at, d, t) {
      if (!rowId || !event) return;
      rows.push({ rowId, event, at: num(at, num(event.t, 0)), d: num(d, 0) });
      rec(event, { rowPlacedAt: num(t, null) });
    },

    /**
     * One frame. `t` is the track second, `kartD` the kart's depth, `speed` metres a second.
     * Returns everything to do this frame: `fire` in the order it was held, `move` per row.
     */
    update(t, kartD, speed) {
      const now = num(t, 0);
      if (now < lastT - SEEK_SEC) {           // a seek back: the scheduler re-hands the future, so let it go here
        queue = queue.filter((q) => q.at <= now);
        rows = rows.filter((r) => r.at <= now);
      }
      lastT = now;
      const fire = [], move = [];
      if (queue.length) {
        const keep = [];
        for (const q of queue) {
          if (now < q.at) { keep.push(q); continue; }
          if (now - q.at > late) { dropped++; rec(q.event, { dropped: true }); continue; }   // the voice already said it
          fire.push({ event: q.event, cue: q.cue, late: now - q.at, plated: !!q.plated });
          rec(q.event, { firedAt: now });
        }
        queue = keep;
      }
      if (rows.length) {
        const keep = [];
        for (const r of rows) {
          const rem = r.at - now;
          if (rem > ahead) {                    // still in the lookahead: re-place off the speed the kart has NOW
            const d = depthFor(now, kartD, speed, r.at);
            if (Math.abs(d - r.d) > 1e-3) { r.d = d; move.push({ rowId: r.rowId, d }); }
            keep.push(r);
          } else if (num(kartD, 0) >= r.d - 1e-6) {   // the kart is on it
            rec(r.event, { rowAt: now });
          } else if (rem < -late) {              // never met (a lap wrap, a seek): stop watching it
            /* let it go */
          } else keep.push(r);
        }
        rows = keep;
      }
      return { fire, move };
    },

    /** A new run: nothing held over from the last one. */
    reset() { queue = []; rows = []; lastT = 0; dropped = 0; if (log) log.clear(); },
    /** The standalone trace, oldest first. Empty unless the sync was built with `trace: true`. */
    trace() { return log ? [...log.values()].map((r) => ({ ...r })) : []; },
    get pending() { return queue.length; },
    get tracked() { return rows.length; },
    get dropped() { return dropped; },
  };
}

// self-check: node race/smoke/rows-check.mjs section 6 drives a row through this under a speed ramp.
