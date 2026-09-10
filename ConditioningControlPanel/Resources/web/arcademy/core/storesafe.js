/* ============================================================================
 * core/storesafe.js - IS THIS PAGE RUNNING INSIDE A STORE BUILD?
 *
 * One question, one door, one answer for the whole page. The iOS App Store
 * build of the mobile app is the same vendored tree as every other host, and it
 * cannot be a different tree: one zip ships to Android and to iOS, so a
 * build-time swap of a file is not available to us and the difference has to be
 * a flag the page reads at runtime.
 *
 * WHO SETS IT. The HOST, in `init.platform.storeSafe`, and boot.js copies it
 * onto `window.__ccpStoreSafe` the moment init lands. A host that predates the
 * field sets nothing, which reads as false, which is the full Arcademy - so the
 * WebView2 desktop, the web build and the Android app are all untouched by
 * every branch that hangs off this.
 *
 * WHY A GLOBAL AND NOT A PARAMETER. The two callers are a game's word bank and
 * a game's lexicon table, both of them module-scope data built at import time,
 * three and four hops below the shell that holds `init`. Threading a boolean
 * down that chain would mean changing the ctx contract for every game to answer
 * a question about the building rather than about the class. The global is set
 * once, before any game module is imported (games load lazily, on the way into
 * a class, long after the handshake), and nothing ever writes it twice.
 *
 * WHAT IT IS NOT. It is not a content rating, not a tier, and not a mod. It
 * says one thing: this page is inside a build that went through an app store
 * review, so the adult word bands and the copy that names outside communities
 * stay out of sight. Everything else about the school is identical.
 * ==========================================================================*/

/**
 * True only when the host declared a store build.
 *
 * Read at CALL time rather than captured at import time, so a module that is
 * imported unusually early (a test harness, a direct-launch path) still sees
 * the answer once the handshake has set it.
 *
 * @returns {boolean}
 */
export function isStoreSafe() {
  try {
    return typeof window !== 'undefined' && window.__ccpStoreSafe === true;
  } catch (e) {
    return false;
  }
}

export default { isStoreSafe };
