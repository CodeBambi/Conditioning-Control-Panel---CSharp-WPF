/* ============================================================================
 * ui/wakeLock.js — keep the phone's screen on while a match is running.
 *
 * A duel is a hands-off experience by design: the player is WATCHING, not
 * tapping, and a phone that dims and locks two minutes into Live is a mercy
 * button nobody pressed. The Screen Wake Lock API is the standard fix and it
 * is deliberately treated as a NICE-TO-HAVE here:
 *
 *   - unsupported browser / WebView -> every call is a silent no-op;
 *   - a refused request (low battery, browser policy) -> logged once, game on;
 *   - the lock DIES on its own whenever the page is hidden (tab switch, screen
 *     off) — the `visibilitychange` re-arm below is what makes the lock
 *     survive the player checking a notification mid-match, and it only fires
 *     while a start() is outstanding, so an idle title screen never holds one.
 *
 * Lifecycle is owned by boot.js: start() when a match attaches, stop() when it
 * detaches. Symmetric, idempotent, and never throws — a screen convenience
 * must not be able to take the match down.
 *
 * Import-safe under node: no navigator/document at import time; the factory
 * takes injectable stand-ins so the whole state machine tests headlessly.
 * ==========================================================================*/

/**
 * @param {object} [o]
 * @param {object} [o.logger]
 * @param {object|null} [o.nav] navigator stand-in ({wakeLock: {request}})
 * @param {object|null} [o.doc] document stand-in (visibilityState + events)
 */
export function createWakeLock({
  logger = null,
  nav = (typeof navigator !== 'undefined' ? navigator : null),
  doc = (typeof document !== 'undefined' ? document : null),
} = {}) {
  const warn = (m) => { try { logger?.warn?.('[wakelock] ' + m); } catch (_e) { /* never */ } };

  const supported = !!(nav && nav.wakeLock && typeof nav.wakeLock.request === 'function');

  let wanted = false;       // a start() is outstanding (the re-arm condition)
  let sentinel = null;      // the live WakeLockSentinel, if any
  let requesting = false;   // one request in flight at a time
  let refusedLogged = false;
  let visListener = null;

  async function acquire() {
    if (!supported || !wanted || requesting) return;
    if (sentinel && !sentinel.released) return;
    if (doc && doc.visibilityState === 'hidden') return;   // request() rejects while hidden
    requesting = true;
    try {
      const s = await nav.wakeLock.request('screen');
      // stop() may have run while the request was in flight — hand it straight back.
      if (!wanted) { try { await s.release(); } catch (_e) { /* already gone */ } return; }
      sentinel = s;
      // The UA releases the lock itself on hide; nothing to do but forget it so
      // the next visible re-arm requests a fresh one.
      try { s.addEventListener?.('release', () => { if (sentinel === s) sentinel = null; }); }
      catch (_e) { /* an event-less stand-in is fine */ }
    } catch (e) {
      if (!refusedLogged) {
        refusedLogged = true;
        warn('screen wake lock refused (' + ((e && e.name) || e) + ') — the match runs without it');
      }
    } finally {
      requesting = false;
    }
  }

  function armVisibility() {
    if (visListener || !doc || typeof doc.addEventListener !== 'function') return;
    visListener = () => { if (wanted && doc.visibilityState === 'visible') acquire(); };
    doc.addEventListener('visibilitychange', visListener);
  }

  function disarmVisibility() {
    if (!visListener || !doc || typeof doc.removeEventListener !== 'function') { visListener = null; return; }
    doc.removeEventListener('visibilitychange', visListener);
    visListener = null;
  }

  return {
    get supported() { return supported; },
    /** True while a start() is outstanding (NOT "the lock is currently held"). */
    get wanted() { return wanted; },
    /** True while a live sentinel is held. */
    get active() { return !!(sentinel && !sentinel.released); },

    /** Idempotent. Never throws, never rejects — fire and forget. */
    start() {
      wanted = true;
      armVisibility();
      acquire();
    },

    /** Idempotent. Releases the lock and stands the re-arm down. */
    stop() {
      wanted = false;
      disarmVisibility();
      const s = sentinel;
      sentinel = null;
      if (s && !s.released) { try { s.release().catch?.(() => {}); } catch (_e) { /* gone */ } }
    },

    dispose() { this.stop(); },
  };
}
