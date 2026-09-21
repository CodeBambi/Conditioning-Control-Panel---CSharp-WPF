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
