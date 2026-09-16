/* ============================================================================
 * race/shutter.js - THE SHUTTER: two hard-edged panels that close over the
 * screen and open again, the way a garage door does. It is the seam between
 * the three places this page can be - the menu, the intro and the run - so a
 * cut never lands as a jump cut.
 *
 *   createShutter({ root, reducedMotion, log })
 *     -> { close(ms), open(ms), sweep({ closeMs, holdMs, openMs, mid }),
 *          flash(), setReduced(b), closed, el, dispose() }
 *
 * Where it plays (raceBoot.js + race/run.js):
 *   `race` on the menu   close -> the menu goes and the world is built behind
 *                        it -> open on the intro. The build hitch is behind a
 *                        shut door, which is half the point of closing it.
 *   the countdown ending flash(): 0.25 s each way over the first moment of the
 *                        run. It is never awaited, so the first steer is the
 *                        player's, not the shutter's.
 *   the End screen       close -> the run tears itself down and the menu comes
 *                        back -> open on the menu.
 *
 * REDUCED MOTION (`menu.options.motion`, `settings.reducedMotion`) drops the
 * two moving panels for one flat 150 ms fade. Same calls, same promises.
 *
 * It is DECORATION and nothing else: `pointer-events: none` throughout, no
 * input of its own, hidden outright (`display: none`) whenever it is open, and
 * transform-only while it moves, so a run pays nothing for it. Nothing here
 * ever gates a start: every caller may await it, and none of them has to.
 * Skin: race.css `.rh-shutter` (the panels, the pink seam, the flat fade).
 * ==========================================================================*/

const CLOSE_MS = 350, OPEN_MS = 350, HOLD_MS = 120, FAST_MS = 250, FADE_MS = 150;

export function createShutter({ root, reducedMotion = false, log = null } = {}) {
  let reduced = !!reducedMotion, disposed = false, shut = false;
  const el = document.createElement('div');
  el.className = 'rh-shutter';
  el.setAttribute('aria-hidden', 'true');   // a curtain says nothing to a screen reader
  const top = document.createElement('i'), bottom = document.createElement('i');
  top.className = 'rh-shutter-p rh-shutter-a';
  bottom.className = 'rh-shutter-p rh-shutter-b';
  el.appendChild(top); el.appendChild(bottom);
  if (root) root.appendChild(el);

  // every wait is its own timer: a shutter told to close mid-open settles the old promise
  // (false, "that one did not finish") instead of leaving whoever awaited it hanging.
  const pending = new Set();
  function wait(ms) {
    return new Promise((res) => {
      const h = { res, id: 0 };
      h.id = setTimeout(() => { pending.delete(h); res(true); }, ms);
      pending.add(h);
    });
  }
  function stop() { for (const h of pending) { clearTimeout(h.id); h.res(false); } pending.clear(); }
  const span = (want, fast) => (reduced ? FADE_MS : (Number(want) > 0 ? Number(want) : (fast ? FAST_MS : CLOSE_MS)));

  function close(ms) {
    if (disposed) return Promise.resolve(false);
    stop();
    const d = span(ms, false);
    el.classList.toggle('is-flat', reduced);
    el.style.setProperty('--rh-shut-ms', d + 'ms');
    el.classList.add('is-on');
    void el.offsetWidth;   // the panels are parked off-screen NOW, so the class change is a move and not a jump
    el.classList.add('is-closed');
    shut = true;
    return wait(d);
  }
  function open(ms) {
    if (disposed) return Promise.resolve(false);
    stop();
    const d = span(ms, false);
    el.style.setProperty('--rh-shut-ms', d + 'ms');
    el.classList.remove('is-closed');
    shut = false;
    return wait(d).then((done) => { if (done && !shut) el.classList.remove('is-on'); return done; });
  }
  /** Close, do the thing nobody should see, hold a beat, open. `mid` may be async. */
  async function sweep(o = {}) {
    await close(o.closeMs);
    if (o.mid) { try { await o.mid(); } catch (e) { if (log) log('shutter mid: ' + e); } }
    const hold = o.holdMs == null ? HOLD_MS : Number(o.holdMs) || 0;
    if (hold > 0 && !reduced) await wait(hold);
    return open(o.openMs == null ? o.closeMs : o.openMs);
  }
  /** The countdown ending: fast, no hold, and never in anybody's way. */
  const flash = () => sweep({ closeMs: FAST_MS, openMs: FAST_MS, holdMs: 0 });

  return {
    close, open, sweep, flash, el,
    setReduced(v) { reduced = !!v; el.classList.toggle('is-flat', reduced && shut); },
    get reduced() { return reduced; },
    get closed() { return shut; },
    dispose() {
      if (disposed) return;
      disposed = true; stop();
      el.remove();
    },
  };
}
