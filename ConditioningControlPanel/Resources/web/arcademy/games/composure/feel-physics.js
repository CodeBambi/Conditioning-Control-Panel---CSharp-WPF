export const PIECES = [
  { name: 'Foundation', kind: 'rect', w: 110, h: 30, color: '#e4ab75' },
  { name: 'Pebble', kind: 'poly', sides: 8, r: 28, color: '#b9d6ce' },
  { name: 'Bridge', kind: 'rect', w: 102, h: 24, color: '#c6b9e2' },
  { name: 'Counterweight', kind: 'rect', w: 40, h: 44, color: '#edb8b0' },
  { name: 'Wedge', kind: 'trap', w: 68, h: 30, color: '#e2cd81' },
  { name: 'Crown', kind: 'poly', sides: 6, r: 22, color: '#86c6d1' }
];

export function createWorld(M) {
  const engine = M.Engine.create({ enableSleeping: true, positionIterations: 8 });
  engine.gravity.y = 0.8;
  const platform = M.Bodies.rectangle(180, 354, 150, 20, { isStatic: true, friction: 0.9 });
  M.Composite.add(engine.world, platform);
  const pieces = new Map();
  function make(id, x = 180, y = 70, angle = 0) {
    const p = PIECES[id];
    const options = { friction: 0.75, frictionStatic: 1.2, restitution: 0.08,
      frictionAir: 0.012, density: 0.002, sleepThreshold: 90, label: String(id) };
    const body = p.kind === 'poly' ? M.Bodies.polygon(x, y, p.sides, p.r, options)
      : p.kind === 'trap' ? M.Bodies.trapezoid(x, y, p.w, p.h, 0.32, options)
      : M.Bodies.rectangle(x, y, p.w, p.h, { ...options, chamfer: { radius: 3 } });
    M.Body.setAngle(body, angle);
    return body;
  }
  function valid(body) {
    return body.bounds.min.y >= 8 && body.bounds.max.y < 345 &&
      M.Query.collides(body, [platform, ...pieces.values()]).every(hit => hit.depth < 0.5);
  }
  function drop(body, id) {
    if (!valid(body) || pieces.has(id)) return false;
    pieces.set(id, body);
    M.Composite.add(engine.world, body);
    return true;
  }
  function take(id) {
    const body = pieces.get(id);
    if (!body) return null;
    M.Composite.remove(engine.world, body);
    pieces.delete(id);
    M.Body.setVelocity(body, { x: 0, y: 0 });
    M.Body.setAngularVelocity(body, 0);
    M.Sleeping.set(body, false);
    return body;
  }
  function step() {
    M.Engine.update(engine, 1000 / 60);
    const fallen = [];
    for (const [id, body] of pieces) {
      if (body.position.y > 465 || body.position.x < -100 || body.position.x > 460) {
        take(id);
        fallen.push(id);
      }
    }
    return fallen;
  }
  function settled() {
    return pieces.size > 0 && [...pieces.values()].every(b => b.speed < 0.13 &&
      Math.abs(b.angularVelocity) < 0.012 && b.bounds.max.y < 367);
  }
  function destroy() {
    M.Events.off(engine);
    M.Composite.clear(engine.world, false);
    M.Engine.clear(engine);
    pieces.clear();
  }
  return { engine, platform, pieces, make, valid, drop, take, step, settled, destroy };
}
