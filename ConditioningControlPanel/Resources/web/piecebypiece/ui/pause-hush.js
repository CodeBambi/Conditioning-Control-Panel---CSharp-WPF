/* ui/pause-hush.js - what the Esc pause does to everything the board makes.
 *
 * The panic key defaults to Escape, and with the board in front the panel lets
 * the page take a first Escape as its pause (PanicPolicy.GameClaimsEscapeAsPause,
 * the Racing Thoughts rule): a second Escape within 2 s is a full panic that
 * closes the board. So the pause IS the first half of a panic press and has to
 * go quiet at once, not only show a card (owner, 2026-09-29):
 *
 *   on   the sound (sfx, crowd, the Distraction bed and whispers) fades out in
 *        about 0.1 s, a running capture replay is cut, the Distraction layers
 *        (flashes, pictures, veils) are cleared and held off. The slow-turn
 *        spiral board reads isPaused() and drops on its own.
 *   off  the sound comes back slowly (sfx.hush eases it in over ~3.5 s) and the
 *        Distraction layers only come back RETURN_MS later, so a resume never
 *        lands the player straight back in a full trance. The spiral board reads
 *        returning() and waits out the same window, then eases in.
 *
 * Every part is optional: Classic has no ramp, an older board has no director
 * and sfx loads late. A part that throws never stops the rest going quiet.
 */

export const RETURN_MS = 3000;

export function createPauseHush({ board = {}, ramp = () => null, isPaused = () => false, later = setTimeout, cancel = clearTimeout } = {}) {
  let back = 0;
  const safe = (fn) => { try { fn(); } catch (e) { console.warn('[pbp] pause hush', e); } };

  function set(on) {
    if (back) { cancel(back); back = 0; }
    safe(() => board.sfx?.hush?.(on));
    if (on) {
      safe(() => board.director?.skip?.());
      safe(() => ramp()?.setEnabled?.(false));
      return;
    }
    back = later(() => {
      back = 0;
      if (!isPaused()) safe(() => ramp()?.setEnabled?.(true));
    }, RETURN_MS);
  }

  // True from a resume until the layers are due back: the slow-turn spiral board waits it out too.
  return { set, returning: () => !!back };
}
