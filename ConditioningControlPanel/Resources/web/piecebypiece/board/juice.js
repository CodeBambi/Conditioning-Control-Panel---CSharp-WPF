// Breakout's quick, weighted impact camera adapted to a quiet 3D board.
// Only the rendered camera moves. Picking, orbit state and game clocks stay exact.
export function createJuice({ bus, camera, reduced = () => !!(window.PBP?.settings?.reducedMotion
  || window.PBP?.reducedMotion || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) }) {
  let energy = 0, age = 0, sign = 1, kicks = 0;
  const position = camera.position.clone(), rotation = camera.quaternion.clone();
  const mass = { p: .65, n: .85, b: .75, r: 1.15, q: 1, k: 1.25 };
  const off = [];
  function kick(amount, p) {
    if (reduced() || p?.skipped || p?.refused) return;
    energy = Math.min(1, Math.max(energy, amount * (mass[p?.piece] || 1)));
    age = 0; sign = -sign; kicks++;
  }
  for (const [name, amount] of [['hit', .72], ['contact', .30], ['land', .22], ['hopLand', .12]]) {
    off.push(bus.on(name, p => kick(amount * (name === 'hopLand' && p?.small ? .65 : 1), p)));
  }
  function reset() { energy = age = 0; }
  off.push(bus.on('local', reset));
  return {
    update(dt) {
      if (reduced()) { reset(); return; }
      age += dt;
      energy *= Math.exp(-11 * dt);
      if (energy < .001) energy = 0;
    },
    render(draw) {
      if (!energy || reduced()) { draw(); return; }
      position.copy(camera.position); rotation.copy(camera.quaternion);
      // owner, 2026-09-29: a hit should be felt in normal play too, still subtle
      camera.translateX(sign * .06 * energy * Math.cos(age * 75));
      camera.translateY(.048 * energy * Math.sin(age * 91 + .8));
      camera.translateZ(-.07 * energy);
      camera.rotateZ(sign * .005 * energy * Math.cos(age * 52));
      camera.updateMatrixWorld();
      try { draw(); } finally {
        camera.position.copy(position); camera.quaternion.copy(rotation); camera.updateMatrixWorld();
      }
    },
    stats: () => ({ energy, kicks }),
    dispose() { reset(); for (const unsubscribe of off) unsubscribe(); },
  };
}
