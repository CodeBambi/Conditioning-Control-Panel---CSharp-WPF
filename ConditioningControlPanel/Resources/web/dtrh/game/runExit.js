/* ============================================================================
 * runExit.js - booking a descent exactly once, whichever exit reaches us first.
 *
 * A run used to be banked only by the recap path: the clock ran out (or the
 * pause menu surfaced) and endRun sent `run-ended`. Every other way out sent
 * nothing. A TIMED descent always reaches its own clock, so nobody noticed -
 * but an ENDLESS one has no clock, so holding Escape or closing the window was
 * the normal way to leave it, and the run counter never moved (Beppu, 2026-09-20).
 *
 * Now every exit books, and this little state machine is why that cannot
 * double-count: the page may send the summary from the recap, from the Escape
 * hold, or not at all (a killed window, where the HOST books from the last
 * progress ping instead). Whoever gets there first spends the run.
 * ==========================================================================*/

/**
 * @param {(msg: object) => void} send - the bridge's send.
 * @returns a booker. `progress` is a live snapshot for the host to fall back on;
 *   `end` is the booking itself and answers whether THIS call was the one.
 */
export function createRunBooker(send) {
  let booked = true;   // nothing is falling yet, so nothing is bookable yet

  return {
    /** A descent started: this run has not been banked. */
    begin() { booked = false; },

    /**
     * The run's figures while it is still falling. The host keeps the last one
     * so a window that dies without a word still books what was earned.
     */
    progress(summary) {
      if (booked) return false;
      send({ type: 'run-progress', ...summary });
      return true;
    },

    /**
     * Bank the run. Returns false when somebody already did, which is what
     * keeps the recap and the Escape hold from paying out twice.
     */
    end(summary, extra) {
      if (booked) return false;
      booked = true;
      send({ type: 'run-ended', ...summary, ...(extra || {}) });
      return true;
    },

    /** True once this run has been banked. */
    get booked() { return booked; },
  };
}
