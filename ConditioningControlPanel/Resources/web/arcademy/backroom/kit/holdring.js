/* ============================================================================
 * backroom/kit/holdring.js - press and HOLD.
 *
 * A casino needs one gesture that costs a beat of your attention. A tap is
 * free and a tap is what a habit does; nine hundred milliseconds of holding a
 * button down while a ring closes around it is a decision, and the ring is
 * there so you can watch yourself make it.
 *
 * THE RULE THAT MAKES IT SAFE: LETTING GO IS ALWAYS FREE. Leaving the button,
 * lifting the finger, Escape, the page hiding, a pointer the browser takes
 * away - every one of those cancels and nothing is spent. The only way to
 * commit is to hold it to the end on purpose. That is also why there is no
 * confirm dialog anywhere in this room: this IS the confirm, and unlike a
 * dialog it never stands between the player and the way out (Law VI).
 *
 * REDUCED MOTION KEEPS THE HOLD AND DROPS THE SWEEP. The hold is unchanged -
 * same nine hundred milliseconds, same commit, same free release - and the
 * ring simply does not sweep: it sits empty and then reads full. The sweep was
 * the part that moved, and the CSS freeze in styles.css cannot reach a JS loop
 * (trap 92), so the loop is not started rather than being flattened.
 * ==========================================================================*/

/** The hold, in milliseconds. Long enough to be a decision, short enough that
 *  a player who meant it does not start wondering whether the button works. */
export const HOLD_MS = 900;

/** rAF with a timer floor, the same pair kit/chips.js uses. Here it only ever
 *  paints: the decision itself hangs off a setTimeout, see `commit` below. */
function raf(fn) {
  if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(fn);
  return setTimeout(() => fn(Date.now()), 16);
}
function unraf(h) {
  if (typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(h); return; } catch { /* noop */ } }
  try { clearTimeout(h); } catch { /* noop */ }
}
const clock = () => ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now());

/**
 * holdRing(btn, { ms, reduced, sfx, onCommit, onProgress, onCancel }) ->
 *   { cancel(), destroy(), holding() }
 *
 * `btn` is any element that can take focus. The ring itself is a CSS conic
 * gradient driven by the --bk-hold custom property, so the sheet owns how it
 * looks and this file only ever owns how far along it is.
 *
 * BOTH ROADS IN. Pointer down starts it and pointer up or leave cancels it;
 * Space or Enter held down does exactly the same thing, because a keyboard
 * player is not a second-class player and a hold that only works with a mouse
 * is a hold half the room cannot use.
 */
export function holdRing(btn, opts) {
  const o = opts || {};
  const ms = Math.max(200, Math.round(Number(o.ms) || HOLD_MS));
  const reduced = !!o.reduced;
  const sfx = (typeof o.sfx === 'function') ? o.sfx : () => {};
  const onCommit = (typeof o.onCommit === 'function') ? o.onCommit : () => {};
  const onProgress = (typeof o.onProgress === 'function') ? o.onProgress : () => {};
  const onCancel = (typeof o.onCancel === 'function') ? o.onCancel : () => {};

  let handle = null;
  let commitTimer = null;
  let t0 = 0;
  let live = false;
  let dead = false;
  let keyDown = false;

  function paint(p) {
    try { btn.style.setProperty('--bk-hold', (p * 100).toFixed(1) + '%'); } catch (e) { /* noop */ }
    onProgress(p);
  }

  /** Down tools. `committed` only says whether to announce a cancel, because a
   *  commit has already announced itself by the time this runs. */
  function stop(committed) {
    if (!live) return;
    live = false;
    if (handle != null) { unraf(handle); handle = null; }
    if (commitTimer != null) { try { clearTimeout(commitTimer); } catch (e) { /* noop */ } commitTimer = null; }
    try { btn.classList.remove('bk-holding'); } catch (e) { /* noop */ }
    paint(0);
    if (!committed) { sfx('slide', 0.18); onCancel(); }
  }

  /* THE COMMIT IS A TIMER, THE RING IS ONLY PAINT. Hanging the decision off
   * requestAnimationFrame made it hostage to a frame clock: a tab with no
   * compositor (a headless probe is the honest case, a heavily throttled
   * background tab is the real one) simply never finished the hold, and a
   * gesture that silently stops working is worse than one that never existed.
   * A setTimeout keeps the promise the copy makes - hold it for nine hundred
   * milliseconds and it commits - and cancelling clears the timer, so letting
   * go is still free, which is the half that has to be exactly right. */
  function commit() {
    commitTimer = null;
    if (!live || dead) return;
    live = false;
    if (handle != null) { unraf(handle); handle = null; }
    paint(1);
    try { btn.classList.remove('bk-holding'); } catch (e) { /* noop */ }
    try { btn.classList.add('bk-held'); } catch (e) { /* noop */ }
    sfx('commit', 0.42);
    onCommit();
  }

  function tick() {
    if (!live || dead) return;
    paint(Math.max(0, Math.min(1, (clock() - t0) / ms)));
    handle = raf(tick);
  }

  function start() {
    if (dead || live || btn.disabled) return;
    live = true;
    t0 = clock();
    try { btn.classList.remove('bk-held'); } catch (e) { /* noop */ }
    try { btn.classList.add('bk-holding'); } catch (e) { /* noop */ }
    sfx('tell', 0.2);
    commitTimer = setTimeout(commit, ms);
    // The ring is decoration and says so: reduced motion never starts the loop
    // at all, and the fifths the module would have stepped through are simply
    // the empty ring and then the full one.
    if (!reduced) handle = raf(tick);
  }

  const onDown = (e) => { if (e && e.button != null && e.button !== 0) return; start(); };
  const onUp = () => stop(false);
  const onKeyDown = (e) => {
    if (e.key !== ' ' && e.key !== 'Enter' && e.key !== 'Spacebar') return;
    // A held key repeats; only the first one starts the ring.
    if (keyDown) { e.preventDefault(); return; }
    keyDown = true;
    e.preventDefault();
    start();
  };
  const onKeyUp = (e) => {
    if (e.key !== ' ' && e.key !== 'Enter' && e.key !== 'Spacebar') return;
    keyDown = false;
    stop(false);
  };

  btn.addEventListener('pointerdown', onDown);
  btn.addEventListener('pointerup', onUp);
  btn.addEventListener('pointerleave', onUp);
  btn.addEventListener('pointercancel', onUp);
  btn.addEventListener('keydown', onKeyDown);
  btn.addEventListener('keyup', onKeyUp);
  btn.addEventListener('blur', onUp);

  return {
    holding: () => live,
    cancel: () => stop(false),
    destroy() {
      dead = true;
      stop(false);
      btn.removeEventListener('pointerdown', onDown);
      btn.removeEventListener('pointerup', onUp);
      btn.removeEventListener('pointerleave', onUp);
      btn.removeEventListener('pointercancel', onUp);
      btn.removeEventListener('keydown', onKeyDown);
      btn.removeEventListener('keyup', onKeyUp);
      btn.removeEventListener('blur', onUp);
    },
  };
}

export default holdRing;
