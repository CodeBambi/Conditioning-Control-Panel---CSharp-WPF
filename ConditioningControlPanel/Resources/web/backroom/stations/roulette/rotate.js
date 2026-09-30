/* rotate.js - when the roulette's "turn your phone upright" nudge may show (pure).
 *
 * Touch alone is not a phone. A Windows desktop with a touch screen or a pen reports touch points
 * too, and a maximised 1920x1080 room read "Turn your phone upright" (support ticket, 2026-09-22).
 * So the nudge wants touch, a sideways screen AND a phone-sized short side. */

/** A phone's short side in CSS px. The largest phones held sideways land near 480; a desktop window does not. */
export const PHONE_SHORT_SIDE = 540;

export function rotateWanted({ touch, w, h }) {
  return !!touch && w > h && Math.min(w, h) <= PHONE_SHORT_SIDE;
}
