/* ============================================================================
 * core/device.js - THE ONE MOBILE DECISION.
 *
 * The Arcademy runs in two very different frames: a desktop WebView2 window
 * inside the WPF app, and a phone browser at app.cclabs.app/arcademy. The phone
 * needs a different campus fit, a rotate gate, slimmer class chrome and a
 * smaller EMI. Every one of those needs the SAME answer to "is this a phone",
 * or the CSS and the JS drift apart and you get a gate over a desktop window or
 * a strip with nothing under it.
 *
 * So there is exactly one rule and exactly one seam:
 *
 *     isMobile()  ->  `html.arc-mobile` + `html[data-arc-orient]`
 *
 * CSS reads the class, JS reads the function, and `installDeviceClass()` keeps
 * them in step across a rotate or a resize. Desktop never gets the class, so
 * every mobile rule in the sheets is dead code on a desktop window and the
 * WebView2 build stays pixel-identical to what it was before this file existed.
 * (Since 2026-08-25 the same paint also ARMS the engine's `html.ae-touch` GPU
 * ceiling on mobile - see THE GLOBAL GPU CEILING below; it rides the same
 * isMobile() verdict, so the desktop invariant above is untouched.)
 *
 * THE RULE (and why it is shaped like this)
 *   1. The PRIMARY pointer is coarse - `matchMedia('(pointer: coarse)')`.
 *   2. There is NO fine pointer anywhere - `!matchMedia('(any-pointer: fine)')`.
 *   3. The SHORT side of the viewport is at most 820 CSS px.
 *
 * Rule 2 is the important one and it is where this deliberately parts company
 * with the engine's `.ae-touch` probe (CLAUDE.md trap 42). That probe is a
 * PERFORMANCE ceiling, so it takes `navigator.maxTouchPoints > 1` as well and
 * happily catches a Windows touchscreen laptop - being cautious with a laptop's
 * GPU costs nobody anything. This is a LAYOUT decision, and catching that same
 * laptop would put a full-screen "turn your phone" card over a 1080p window,
 * which is a bug the owner would rightly file. A touchscreen laptop reports a
 * FINE primary pointer and a fine `any-pointer`, so rules 1 and 2 both refuse
 * it. No user-agent sniffing anywhere: the UA string is a rumour, the pointer
 * media queries are the device.
 *
 * Rule 3's 820px lands under an iPad Pro 11" (834) and over every phone and
 * small tablet, so a big tablet keeps the desktop campus it has room for.
 *
 * THE OVERRIDE. `?forcemobile=1` forces the answer true and `?forcemobile=0`
 * forces it false, whatever the hardware says. This SHIPS on purpose: it is the
 * only way to look at the phone layout in a desktop browser's devtools or in a
 * headless screenshot run, both of which report a fine pointer no matter how
 * the viewport is sized. It reads the query string once per call, so flipping it
 * is a reload, and it is inert unless somebody types it.
 *
 * IMPORT SAFETY: nothing here touches document or window at module scope, same
 * contract boot.js keeps, so the headless DOM double the suites drive can import
 * it without a global in sight.
 * ==========================================================================*/

/** The class this module writes on <html>. CSS keys every mobile rule off it. */
export const MOBILE_CLASS = 'arc-mobile';

/** The engine's GPU-ceiling class (engine/style.js reads it, engine/util.js's
 *  decoder budget reads it). This module may ARM it; it never removes it. */
export const TOUCH_CLASS = 'ae-touch';

/** The marker that says the ARMING WAS GLOBAL (this module's), so per-class
 *  owners of the same seam (The Deep End) know not to toggle it on their own
 *  lifecycle - a destroy() that removed a page-wide ceiling would hand the
 *  lobby back its desktop-cost effects. */
export const TOUCH_GLOBAL_ATTR = 'data-ae-touch-global';

/** Short-side ceiling, in CSS px, for "this is a phone-shaped screen". */
export const MOBILE_MAX_SHORT_SIDE = 820;

/** Read the `?forcemobile=` override. Returns true, false, or null for absent. */
function forcedAnswer() {
  try {
    if (typeof window === 'undefined' || !window.location) return null;
    const q = String(window.location.search || '');
    const m = /[?&]forcemobile=([^&#]*)/i.exec(q);
    if (!m) return null;
    const v = decodeURIComponent(m[1] || '').toLowerCase();
    return !(v === '0' || v === 'false' || v === 'no');
  } catch (e) { return null; }
}

function mq(query) {
  try {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false;
    const m = window.matchMedia(query);
    return !!(m && m.matches);
  } catch (e) { return false; }
}

/** {w,h} of the viewport, with the same 1280x800 fallback widget.js uses. */
export function viewportSize() {
  const w = (typeof window !== 'undefined' && Number(window.innerWidth)) || 1280;
  const h = (typeof window !== 'undefined' && Number(window.innerHeight)) || 800;
  return { w, h };
}

/**
 * THE ANSWER. Recomputed on every call (it is three media queries and two
 * numbers) so a rotate or a window resize can never leave a stale verdict
 * behind; callers that need to react should use `onDeviceChange`.
 * @returns {boolean}
 */
export function isMobile() {
  const f = forcedAnswer();
  if (f !== null) return f;
  if (!mq('(pointer: coarse)')) return false;
  if (mq('(any-pointer: fine)')) return false;
  const vp = viewportSize();
  return Math.min(vp.w, vp.h) <= MOBILE_MAX_SHORT_SIDE;
}

/**
 * 'portrait' | 'landscape', decided from the viewport box rather than from
 * `screen.orientation`, which reports the DEVICE's idea of upright and answers
 * "portrait" for a landscape browser window on a tablet in a stand. A square
 * viewport counts as landscape, so the campus (which is 16:9) wins the tie.
 * @returns {string}
 */
export function orientation() {
  const vp = viewportSize();
  return vp.h > vp.w ? 'portrait' : 'landscape';
}

/**
 * Does the current device state satisfy a wanted orientation?
 * 'any' (and anything unrecognised, and any non-mobile frame) is always happy:
 * the gate is a phone affordance and must never appear on a desktop window.
 * @param {?string} want 'landscape' | 'portrait' | 'any'
 * @returns {boolean}
 */
export function orientationOk(want) {
  if (!isMobile()) return true;
  if (want !== 'landscape' && want !== 'portrait') return true;
  return orientation() === want;
}

/**
 * CAN THIS VIEWPORT EVER TURN? A device fact, and the only one on this page that
 * the page cannot work out for itself.
 *
 * Everything else in this module is measured: a viewport is wide or it is tall,
 * and if the phone turns the numbers change. But "this window will never be any
 * shape but the one it is" is not measurable from inside the frame, because it
 * looks exactly like a phone somebody simply has not turned yet. Only the host
 * knows, so the host says: `init.platform.orientationLocked`, copied onto the
 * window by boot.js the moment init lands.
 *
 * The one host that sets it today is the iOS App Store build, whose plist lists
 * portrait alone, so no amount of asking rotates it. The Discord Activity iframe
 * is the same shape of problem and may set it later. Every other host leaves it
 * undefined, which reads as "it can rotate", which is what a phone does.
 *
 * WHAT READS IT: shell/orientgate.js, and nothing else. A requirement that
 * cannot be satisfied is not a requirement, it is a locked door, so the gate
 * stops asking and says something useful instead.
 *
 * @returns {boolean} false only when the host declared the viewport locked
 */
export function viewportCanRotate() {
  try {
    return !(typeof window !== 'undefined' && window.__ccpOrientationLocked === true);
  } catch (e) {
    return true;
  }
}

/* ----------------------------------------------------------------------------
 * THE SEAM
 * One listener pair for the whole page, however many callers there are. iOS
 * fires `orientationchange` BEFORE innerWidth/innerHeight have caught up, so
 * every notification is re-run once on the next frame and once again a beat
 * later - cheap, idempotent, and the difference between a gate that lifts when
 * you turn the phone and one that needs a nudge to notice.
 * -------------------------------------------------------------------------- */

const subs = new Set();
let bound = false;
let lastKey = '';

/** A stable signature of everything the callers care about. */
function stateKey() {
  return (isMobile() ? 'm' : 'd') + ':' + orientation();
}

/** The Deep End's own hardware probe, verbatim: coarse primary pointer OR more
 *  than one touch point. Broader than isMobile() on purpose - it is a GPU
 *  ceiling, not a layout verdict - but see armTouchClass() for why the arming
 *  below still gates on isMobile() as well. */
function touchProbe() {
  if (mq('(pointer: coarse)')) return true;
  try {
    if (typeof navigator !== 'undefined' && Number(navigator.maxTouchPoints) > 1) return true;
  } catch (e) { /* ignore */ }
  return false;
}

/* ----------------------------------------------------------------------------
 * THE GLOBAL GPU CEILING (mobile perf, 2026-08-25).
 *
 * engine/style.js's `.ae-touch` block kills what WebKit charges a phone most
 * for (backdrop-filter, full-screen blend surfaces, the scanline re-raster,
 * filters over live decodes) and engine/util.js drops the decoder budget 6->3
 * under it - but the ONLY writer used to be The Deep End's class lifecycle, so
 * nine of ten classes and the whole shell ran desktop-cost effects on phones.
 * Arm it here instead, once, page-wide, from the same installDeviceClass()
 * boot.js and shell.js already call.
 *
 * WHY `isMobile() && touchProbe()` AND NOT the probe alone: the bare probe
 * catches a touch-screen Windows laptop, and on the desktop WebView2 host that
 * would CHANGE DESKTOP VISUALS (blend modes off, scanline still) - desktop must
 * stay pixel-identical. A host probe is no help either: the web shim
 * (cclabs-web arcademy-web-ext/web-shim.js) deliberately fakes
 * `window.chrome.webview`, so "am I the desktop app" is unanswerable from here.
 * `isMobile()` IS provably false on desktop WebView2 - it requires a coarse
 * PRIMARY pointer and NO fine pointer anywhere, and every desktop/laptop
 * WebView2 window has a fine pointer (`?forcemobile=1` never appears in the
 * host's fixed URL) - so gating on it makes desktop immunity structural.
 * The cost of the narrower gate: a big tablet (iPad Pro 11"+) or touch laptop
 * browser does not get the ceiling globally; The Deep End's own per-class
 * probe still covers it there, exactly as before.
 *
 * ONCE ON, NEVER OFF: a rotate or resize can flip isMobile(), but a GPU that
 * needed the ceiling a minute ago still needs it now. TOUCH_GLOBAL_ATTR is the
 * marker that tells per-class writers the class is not theirs to toggle.
 * -------------------------------------------------------------------------- */
function armTouchClass(html) {
  try {
    html.classList.add(TOUCH_CLASS);
    if (typeof html.setAttribute === 'function') html.setAttribute(TOUCH_GLOBAL_ATTR, '1');
  } catch (e) { /* never fatal */ }
}

function paintRoot() {
  try {
    const html = typeof document !== 'undefined' && document.documentElement;
    if (!html || !html.classList) return;
    if (isMobile()) {
      html.classList.add(MOBILE_CLASS);
      if (touchProbe()) armTouchClass(html);
    } else {
      html.classList.remove(MOBILE_CLASS);
      // ae-touch stays if it was ever armed - see the block comment above.
    }
    if (typeof html.setAttribute === 'function') html.setAttribute('data-arc-orient', orientation());
  } catch (e) { /* the DOM double has no classList - never fatal */ }
}

function announce(force) {
  const key = stateKey();
  if (!force && key === lastKey) return;
  lastKey = key;
  paintRoot();
  for (const fn of Array.from(subs)) {
    try { fn({ mobile: isMobile(), orientation: orientation() }); } catch (e) { /* a bad subscriber must not stop the next */ }
  }
}

function onViewportEvent() {
  announce(false);
  try {
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => announce(false));
    if (typeof setTimeout === 'function') setTimeout(() => announce(false), 260);
  } catch (e) { /* noop */ }
}

function bind() {
  if (bound) return;
  bound = true;
  try {
    if (typeof window === 'undefined' || typeof window.addEventListener !== 'function') return;
    window.addEventListener('resize', onViewportEvent);
    window.addEventListener('orientationchange', onViewportEvent);
    try {
      const m = window.matchMedia && window.matchMedia('(pointer: coarse)');
      if (m && typeof m.addEventListener === 'function') m.addEventListener('change', onViewportEvent);
    } catch (e) { /* Safari < 14 has addListener only; the resize handler covers us */ }
  } catch (e) { /* noop */ }
}

/**
 * Put the verdict on <html> and keep it there. Idempotent - call it from boot,
 * from the shell, from a test harness, as often as you like.
 * @returns {boolean} the answer it just painted
 */
export function installDeviceClass() {
  bind();
  announce(true);
  return isMobile();
}

/**
 * Subscribe to "the device state changed" (mobile flipped, or the phone turned).
 * Fires only on a real change, never on a resize that means nothing.
 * @param {Function} fn  ({mobile, orientation}) => void
 * @returns {Function} unsubscribe
 */
export function onDeviceChange(fn) {
  if (typeof fn !== 'function') return () => {};
  bind();
  if (!lastKey) lastKey = stateKey();
  subs.add(fn);
  return () => { try { subs.delete(fn); } catch (e) { /* noop */ } };
}

export default {
  MOBILE_CLASS, MOBILE_MAX_SHORT_SIDE, TOUCH_CLASS, TOUCH_GLOBAL_ATTR,
  isMobile, orientation, orientationOk, viewportSize,
  installDeviceClass, onDeviceChange,
};
