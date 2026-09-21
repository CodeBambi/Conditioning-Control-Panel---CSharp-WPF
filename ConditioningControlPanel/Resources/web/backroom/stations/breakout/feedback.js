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
/** How far the WORDS follow the ball, in px. It grows with speed, and a fast ball earns up to a quarter more on top
 *  (0 at 300 px/s, +25% by 700). This was the comet's own length until the streak drowned the words (owner, 2026-09-21). */
export const COMET_BONUS = .25;
export const wordTrailLength = speed => {
  const v = Math.max(0, speed || 0), fast = Math.max(0, Math.min(1, (v - 300) / 400));
  return Math.min(225, v * .22 * (1 + COMET_BONUS * fast));
};
/** The streak behind the ball is short on purpose: about what the slowest ball used to draw, so the words own the tail. */
export const COMET_MAX = 60;
export const cometLength = speed => Math.min(COMET_MAX, wordTrailLength(speed));
/** The words on the tail: one every WORD_TRAIL.gap px of path, the first a little behind the ball. */
export const WORD_TRAIL = { gap: 34, first: 20, size: 10, alpha: .85 };
/** Multiball copies wear their own colour so three balls read as three: [body spiral, trail]. 0 is the player's own ball. */
export const BALL_TINTS = [null, [[255, 120, 196], [255, 140, 210]], [[110, 200, 255], [130, 215, 255]]];
/** A bubble's jelly after a hit. `t` is the sim's collider.jelly (1 at the hit -> 0): squashed along the hit normal, then a
 *  damped ring of three swings. `along` scales the normal, `across` the tangent, area roughly kept. */
export function jellyScale(t) {
  const u = 1 - Math.max(0, Math.min(1, t || 0));
  const k = u >= 1 ? 0 : Math.exp(-4.2 * u) * Math.cos(u * Math.PI * 5) * (1 - u);
  return { along: 1 - .2 * k, across: 1 + .16 * k };
}
/** A resting bubble is never still: it breathes, and a slow wobble trades width for height. Both from its age and a phase. */
export function bubbleIdle(age = 0, ph = 0) {
  const w = .035 * Math.sin(age * 2.1 + ph * 1.7);
  return { breath: 1 + .03 * Math.sin(age * 1.5 + ph), sx: 1 + w, sy: 1 - w, tilt: .5 * Math.sin(age * .6 + ph) };
}
export function cometSegments(ball, length = cometLength(Math.hypot(ball.vx || 0, ball.vy || 0))) {
  if (ball.stuck || ball.lost) return [];
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
/** Where the trailing words sit: points along the ball's own path, evenly spaced, fading toward the end. */
export function wordTrailPoints(ball) {
  const length = wordTrailLength(Math.hypot(ball.vx || 0, ball.vy || 0)), points = [];
  let next = WORD_TRAIL.first, walked = 0;
  for (const s of cometSegments(ball, length)) {
    const span = Math.hypot(s.nx - s.x, s.ny - s.y);
    while (span > 0 && next <= walked + span && next < length) {
      const f = (next - walked) / span;
      points.push({ x: s.x + (s.nx - s.x) * f, y: s.y + (s.ny - s.y) * f, alpha: 1 - next / length });
      next += WORD_TRAIL.gap;
    }
    walked += span;
  }
  return points;
}
