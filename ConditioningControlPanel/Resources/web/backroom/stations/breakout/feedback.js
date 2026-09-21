// Shared visual rules. Themes change the hue; remaining hits control brightness.
export function durabilityColour(hp, grey = false, hue = 270) {
  const light = [0, .78, .56, .34][Math.max(1, Math.min(3, hp || 1))];
  if (grey) return Array(3).fill(Math.round(light * 255));
  const saturation = .55, a = saturation * Math.min(light, 1 - light);
  return [0, 8, 4].map(n => {
    const k = (n + hue / 30) % 12;
    return Math.round(255 * (light - a * Math.max(-1, Math.min(k - 3, 9 - k, 1))));
  });
}
export const cometLength = speed => Math.min(180, Math.max(0, speed) * .22);
export function cometSegments(ball) {
  if (ball.stuck || ball.lost) return [];
  const length = cometLength(Math.hypot(ball.vx || 0, ball.vy || 0));
  if (!length) return [];
  const points = ball.trail || [], segments = [];
  let x = ball.x, y = ball.y, distance = 0;
  for (let i = points.length - 2; i >= 0 && distance < length; i -= 2) {
    const dx = points[i] - x, dy = points[i + 1] - y, span = Math.hypot(dx, dy);
    if (!span) continue;
    // Never draw a streak across a teleport.
    if (span > 80) break;
    const portion = Math.min(1, (length - distance) / span);
    const nx = x + dx * portion, ny = y + dy * portion;
    segments.push({ x, y, nx, ny, alpha: 1 - distance / length });
    distance += span * portion; x = nx; y = ny;
  }
  return segments;
}
export function paddleMood(balls, paddle, happy) {
  if (happy > 0) return 'happy';
  const active = balls.filter(b => !b.lost && !b.stuck);
  return active.length && active.every(b => Math.hypot(b.x - paddle.x, b.y - paddle.y) > 260) ? 'sad' : 'neutral';
}
/** Impact squash. `t` is the sim's ball.squash (1 at the bounce -> 0): flat against the surface at once, round again by
 *  40% of the way, then one small springy overshoot. `along` scales the surface normal, `across` the tangent. */
export function squashScale(t) {
  const u = 1 - Math.max(0, Math.min(1, t || 0));
  const k = u < .4 ? 1 - u / .4 : -.3 * Math.sin((u - .4) / .6 * Math.PI);
  return { along: 1 - .38 * k, across: 1 + .24 * k };
}
/** The last-brick push-in: zoom by seconds since the event. Up to 1.06 in 120 ms, held, eased back to 1 by PUSH_IN_S. */
export const PUSH_IN_S = 0.85;
export function pushInZoom(t) {
  if (!(t >= 0) || t >= PUSH_IN_S) return 1;
  const rise = 1 - Math.pow(1 - Math.min(1, t / .12), 3), fall = Math.max(0, (t - .35) / (PUSH_IN_S - .35));
  return 1 + .06 * rise * (1 - fall * fall * (3 - 2 * fall));
}
/** PERFECT, then PERFECT x2, x3: the text grows a little with the streak and stops growing at x6. */
export const perfectLabel = streak => (streak >= 2 ? 'PERFECT x' + Math.floor(streak) : 'PERFECT');
export const perfectSize = streak => 20 + 3 * Math.min(5, Math.max(0, (streak || 1) - 1));
/** Touch steers by RELATIVE drag so the finger never hides the paddle: the paddle moves by the finger's delta from where the
 *  touch began, times TOUCH_GAIN. At a wall the anchor follows the finger, so a reversal answers at once. Mutates `drag`. */
export const TOUCH_GAIN = 1.15;
export function relativeDrag(drag, x, half, width) {
  const want = drag.px + (x - drag.sx) * TOUCH_GAIN, got = Math.max(half, Math.min(width - half, want));
  if (got !== want) { drag.sx = x; drag.px = got; }
  return got;
}
/** Paddle lean (a skew factor) from its smoothed velocity in px/s: a slight tilt into the direction of travel. */
export const paddleLean = vx => Math.max(-1, Math.min(1, (vx || 0) / 1600)) * .2;
