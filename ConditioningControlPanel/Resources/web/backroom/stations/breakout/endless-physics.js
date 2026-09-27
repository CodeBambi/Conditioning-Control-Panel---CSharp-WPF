// Demolition balls share the dome's field, but never its ordinary-ball slot.
const TAU = Math.PI * 2;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
export const DEMOLITION_CAPTURES = 2;
export const DEMOLITION_FLIGHT_SECONDS = 4.5;

function launch(p, g, emit) {
  const targets = g.bricks.filter(b => b.alive && !(g.state === 'grey' && b.greyMetal));
  // A hinge makes the most satisfying chain. Otherwise pick a close fresh piece.
  targets.sort((a, b) => Number(!!b.pendulumAnchor) - Number(!!a.pendulumAnchor) ||
    Math.hypot(a.x + a.w / 2 - p.x, a.y + a.h / 2 - p.y) -
    Math.hypot(b.x + b.w / 2 - p.x, b.y + b.h / 2 - p.y));
  const target = targets[0];
  const tx = target ? target.x + target.w / 2 : g.w / 2;
  const ty = target ? target.y + target.h / 2 : g.h * .18;
  const distance = Math.hypot(tx - p.x, ty - p.y) || 1;
  const speed = Math.min(650, Math.max(390, g.speed * 1.35 || 480));
  p.mode = 'flight'; p.age = 0; p.vx = (tx - p.x) / distance * speed;
  p.vy = (ty - p.y) / distance * speed; p.demolitionCooldown = 1.1;
  p.struck.clear(); p.pulse = 1; p.orbitWell = null;
  emit('demolitionLaunch', { x: p.x, y: p.y, id: p.id, tx, ty, vx: p.vx, vy: p.vy, ghost: g.state === 'grey' });
}

/** Advance only the new states. A released sweep can be caught by a live dome. */
export function advanceEndlessDemolition(p, g, dt, emit = () => {}) {
  if (!g.endless || p.mode === 'hung' || p.mode === 'spent') return;
  p.demolitionCooldown = Math.max(0, (p.demolitionCooldown || 0) - dt);
  const well = g.state === 'colour' && g.well?.persistent ? g.well : null;
  if (p.mode === 'orbit') {
    p.age += dt; p.pulse = Math.max(0, p.pulse - dt * 3);
    if (well !== p.orbitWell) { launch(p, g, emit); return; }
    p.orbitAngle -= dt * TAU / .95;
    const radius = p.orbitRadius * (1 - .18 * Math.min(1, p.age));
    p.x = well.x + Math.cos(p.orbitAngle) * radius;
    p.y = well.y + Math.sin(p.orbitAngle) * radius;
    well.hitPulse = Math.max(well.hitPulse, .35);
    if (p.age >= 1.05) launch(p, g, emit);
  } else if (p.mode === 'flight') {
    p.age += dt; p.pulse = Math.max(0, p.pulse - dt * 3);
    p.x += p.vx * dt; p.y += p.vy * dt;
    const top = p.r + 12, bottom = g.h * .72 - p.r;
    if (p.x < p.r || p.x > g.w - p.r) {
      p.x = clamp(p.x, p.r, g.w - p.r); p.vx *= -1;
    }
    if (p.y < top || p.y > bottom) {
      p.y = clamp(p.y, top, bottom); p.vy *= -1;
    }
    if (p.age >= DEMOLITION_FLIGHT_SECONDS) { p.mode = 'spent'; p.trail.length = 0; return; }
  }
  if (well && ['sweep', 'flight'].includes(p.mode) && !p.demolitionCooldown &&
      (p.demolitionCaptures || 0) < DEMOLITION_CAPTURES &&
      Math.hypot(p.x - well.x, p.y - well.y) < well.r + well.pull * .6) {
    p.mode = 'orbit'; p.age = 0; p.demolitionCaptures = (p.demolitionCaptures || 0) + 1;
    p.orbitWell = well; p.orbitAngle = Math.atan2(p.y - well.y, p.x - well.x);
    p.orbitRadius = clamp(Math.hypot(p.x - well.x, p.y - well.y), 65, 135);
    p.pulse = 1; well.hitPulse = 1;
    emit('demolitionCapture', { x: p.x, y: p.y, id: p.id, cx: well.x, cy: well.y });
  }
  if (p.mode === 'orbit' || p.mode === 'flight') {
    if (g.reduced) p.trail.length = 0;
    else { p.trail.push({ x: p.x, y: p.y }); if (p.trail.length > 18) p.trail.shift(); }
  }
}
