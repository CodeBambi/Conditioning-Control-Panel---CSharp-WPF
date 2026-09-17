// The doorway. The loading card used to just fade off a room that was already standing there, which
// read as a dropped frame rather than an arrival. This covers the handoff: a slat shutter slams over
// the card, tears for a beat while the card is removed underneath, then rips open on the room, and
// the 3D stage settles out of a small over-zoomed roll so the first moment inside is unsteady.
//
// Everything here is CSS on two elements the compositor already owns. No canvas, no texture, no
// audio, nothing added to room/scene.js's frame loop - the room is drawing normally the whole time,
// it is simply behind a shutter. Cost is one extra layer for under a second, then the node is gone.
const SLATS = 11;
const SHUT_MS = 190, GLITCH_MS = 340, OPEN_MS = 420, ARRIVE_MS = 1050;
// Slats do not move as one sheet: each waits its turn, so the shutter reads as slats and not a wipe.
const STAGGER_MS = 16;

let played = false;

const reduced = () => {
  try { return matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { return false; }
};

/**
 * Play the shutter once, over a room that is already built and drawing.
 * @param {boolean} still true when the room is held still (motion off, reduced, suspended tab).
 *        A still room gets no shutter and no roll: the point of still is that nothing moves.
 * @returns {number} milliseconds the caller should wait before the loading card is safe to remove.
 */
export function playEntry(still = false) {
  if (played) return 0;
  played = true;
  if (still || reduced() || !document.body) return 0;

  const stage = document.getElementById('br-stage');
  const node = document.createElement('div');
  node.className = 'br-entry';
  node.setAttribute('aria-hidden', 'true');
  const slats = document.createElement('div');
  slats.className = 'br-entry-slats';
  const height = 100 / SLATS;
  for (let i = 0; i < SLATS; i++) {
    const slat = document.createElement('div');
    slat.className = 'br-entry-slat';
    slat.style.top = `${i * height}%`;
    slat.style.height = `${height + .2}%`;
    // Shut from the middle outwards, open from the edges inwards, so the two halves are not mirrors.
    slat.dataset.shut = String(Math.abs(i - (SLATS - 1) / 2) * STAGGER_MS);
    slat.dataset.open = String((SLATS - 1 - Math.abs(i - (SLATS - 1) / 2)) * STAGGER_MS);
    slat.style.animationDelay = `${slat.dataset.shut}ms`;
    slats.appendChild(slat);
  }
  const tear = document.createElement('div');
  tear.className = 'br-entry-tear';
  node.append(slats, tear);
  document.body.appendChild(node);

  // Shut.
  node.classList.add('br-entry-shut');
  const half = SHUT_MS + (SLATS - 1) / 2 * STAGGER_MS;

  setTimeout(() => {
    if (!node.isConnected) return;
    node.classList.add('br-entry-glitch');
  }, half);

  setTimeout(() => {
    if (!node.isConnected) return;
    node.classList.remove('br-entry-shut', 'br-entry-glitch');
    for (const slat of slats.children) slat.style.animationDelay = `${slat.dataset.open}ms`;
    // One frame with no animation class so the open keyframes restart rather than being ignored.
    requestAnimationFrame(() => {
      if (!node.isConnected) return;
      node.classList.add('br-entry-open');
      if (stage) {
        stage.classList.add('br-arrive');
        setTimeout(() => stage.classList.remove('br-arrive'), ARRIVE_MS + 120);
      }
    });
  }, half + GLITCH_MS);

  const done = half + GLITCH_MS + OPEN_MS + (SLATS - 1) * STAGGER_MS + 80;
  setTimeout(() => node.remove(), done);
  // The card is hidden from the moment the slats meet; anything after that is cosmetic.
  return Math.round(half);
}

/** Test seam. */
export const ENTRY_TIMING = { SLATS, SHUT_MS, GLITCH_MS, OPEN_MS, ARRIVE_MS, STAGGER_MS };
