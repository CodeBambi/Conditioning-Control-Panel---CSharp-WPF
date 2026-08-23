/* ============================================================================
 * games/sort/setup.js - STUB. LOT G2 owns the real file (the five-step door:
 * source -> target -> noise -> ghost round -> play).
 *
 * THIS EXISTS SO THE CLASS RUNS STANDALONE. index.js calls `createSetupDoor`
 * exactly the way it will call G2's, and this version answers by opening
 * immediately: it resolves whatever setup is already stored (a retake, or a
 * door G2 wrote on an earlier night) and otherwise falls through to QUICK SORT,
 * which is the one resolution that needs no picking at all. Nothing in index.js
 * changes when the real door lands - the contract below IS the seam.
 *
 * THE CONTRACT index.js DEPENDS ON
 *   createSetupDoor({ ctx, t, mount, existing, assets, onPlay, onLeave })
 *     -> { el, setBusy(bool, msgKey), ghost(rows) -> Promise, destroy() }
 *   onPlay(setup, resolved)   setup   = the SORT_SETUP blob to persist, or null
 *                                       to leave the stored one alone
 *                             resolved = { sources:[...], hot:bool, quick:bool }
 *   onLeave()                 the player backed out; the shell goes to campus
 *   setBusy(true, key)        index.js paints the door busy while it claims
 *   ghost(rows)               rows = { target: row|null, noise: row|null }, and
 *                             the door animates the two-card rulebook and
 *                             resolves. A door with no ghost round may simply
 *                             not expose the method; index.js skips it.
 *   destroy()                 index.js calls it once, after setup() settles
 *
 * The door is mounted OUTSIDE the class clock (the shell's setup hook runs it
 * before beginPlay), so nothing in here has to be fast, and Esc bubbles to the
 * shell's ordinary leave confirm.
 * ==========================================================================*/

/** The one fallback that needs no picking: moving right, still left. */
export const QUICK_RESOLUTION = Object.freeze({ sources: [], hot: false, quick: true });

/**
 * Turn a stored SORT_SETUP into provider source rows. G2 owns the real version
 * (niche expansion off the catalog, overlap removal, the "hot" verdict); this
 * one handles the shapes the blob can actually hold and nothing more.
 * @returns {?{sources:Array, hot:boolean, quick:boolean}}
 */
export function resolveSetup(setup) {
  if (!setup || typeof setup !== 'object') return null;
  const side = (s, tag) => {
    if (!s || typeof s !== 'object') return null;
    const subs = []
      .concat(Array.isArray(s.subs) ? s.subs : [])
      .concat(Array.isArray(s.niches) ? s.niches : []);
    if (subs.length) return { tag, kind: 'remote', subs: subs.map(String) };
    if (Array.isArray(s.folders) && s.folders.length) {
      return { tag, kind: 'local', folders: s.folders.map(String) };
    }
    if (s.presetId) return { tag, kind: 'local', presetId: String(s.presetId) };
    return null;
  };
  const target = side(setup.target, 'target');
  const noise = side(setup.noise, 'noise');
  if (!target || !noise) return null;
  /* A sub on BOTH sides is not noise. The real door removes it at pick time and
   * says so; here we just drop it, because a duplicated row would make the
   * ledger disagree with itself. */
  let hot = false;
  if (target.subs && noise.subs) {
    const owned = new Set(target.subs.map((s) => s.toLowerCase()));
    const kept = noise.subs.filter((s) => !owned.has(String(s).toLowerCase()));
    if (kept.length !== noise.subs.length) hot = true;
    if (!kept.length) return null;
    noise.subs = kept;
  }
  return { sources: [target, noise], hot, quick: false };
}

/**
 * The stub door. It never paints and never waits.
 */
export function createSetupDoor(o = {}) {
  const onPlay = typeof o.onPlay === 'function' ? o.onPlay : () => {};
  let dead = false;
  let el = null;
  try {
    if (typeof document !== 'undefined' && document.createElement) {
      el = document.createElement('div');
      el.className = 'g-sort-door is-stub';
      if (o.mount && o.mount.appendChild) o.mount.appendChild(el);
    }
  } catch (e) { el = null; }

  /* Open on the next tick, never synchronously: index.js is still wiring its
   * own promise when this returns, and a door that answers inside the
   * constructor would call onPlay before there is anywhere for it to land. */
  const kick = () => {
    if (dead) return;
    const resolved = resolveSetup(o.existing) || QUICK_RESOLUTION;
    /* `null` for the blob: the stub picked nothing, so it may not overwrite a
     * real door's stored setup with its own guess. */
    try { onPlay(null, resolved); } catch (e) { /* index.js logs it */ }
  };
  try { setTimeout(kick, 0); } catch (e) { kick(); }

  return {
    el,
    setBusy() { /* the stub has no chrome to busy */ },
    /* no ghost(): index.js skips the round when the door does not offer one */
    destroy() {
      dead = true;
      try { if (el && el.remove) el.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default { createSetupDoor, resolveSetup, QUICK_RESOLUTION };
