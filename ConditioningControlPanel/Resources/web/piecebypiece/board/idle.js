// Planted-body breathing, frequent solo gestures and reactions to the nearby game.
// Only shader offsets leave this module; it never moves a piece root or changes a move.
const FIRM = { p: .30, n: .8, b: 1, r: .35, q: .85, k: .7 };
const clamp = v => Math.max(0, Math.min(1, v));
const smooth = v => { const t = clamp(v); return t * t * (3 - 2 * t); };
export function createIdle({ pieces, available, random = Math.random } = {}) {
  let clock = 0, nextGesture = 1 + random() * 2, gesture = null, focus = null, stopped = false;
  let attention = [];
  const cooldown = new WeakMap();
  const reactions = new Map();
  const unbind = [];
  const live = () => [...pieces()].filter(p => p.parent && available(p));
  const near = (source, exclude) => live().filter(p => p !== exclude).map(p => ({ piece: p,
    dx: p.position.x - source.x, dz: p.position.z - source.z,
    distance: Math.hypot(p.position.x - source.x, p.position.z - source.z) }))
    .filter(p => p.distance > .1 && p.distance < 2.6).sort((a, b) => a.distance - b.distance).slice(0, 2);

  function follow(piece) {
    if (stopped || !piece) return;
    gesture = null; reactions.clear(); nextGesture = clock + 1.8 + random() * 2;
    focus = { piece, start: clock, until: clock + 7, settling: false };
  }
  function reset() { gesture = focus = null; attention = []; reactions.clear(); nextGesture = clock + 1 + random() * 2; }
  function update(dt, reduced) {
    clock += Math.max(0, dt);
    stopped = !!reduced;
    if (stopped) { reset(); return; }
    if (gesture && (clock >= gesture.end || !available(gesture.piece))) gesture = null;
    if (!gesture && !focus && reactions.size === 0 && clock >= nextGesture) {
      const ready = live().filter(p => (cooldown.get(p) || 0) <= clock);
      const types = [...new Set(ready.map(p => p.userData.type))];
      const type = types[Math.floor(random() * types.length)];
      const choices = ready.filter(p => p.userData.type === type);
      const piece = choices[Math.floor(random() * choices.length)];
      if (piece) {
        const duration = 1.6 + random() * .6;
        gesture = { piece, start: clock, end: clock + duration, duration, variant: random() < .5 ? -1 : 1 };
        cooldown.set(piece, clock + 10 + random() * 10);
        nextGesture = clock + duration + 1.2 + random() * 1.8;
      } else nextGesture = clock + 1;
    }
    attention = [];
    if (focus) {
      const p = focus.piece, d = p.userData;
      if (!p.parent || clock >= focus.until) focus = null;
      else {
        if (!d.held && !d.busy && !d.capturePose && !focus.settling) { focus.settling = true; focus.until = clock + .7; }
        attention = near(p.position, p);
      }
    }
    for (const [piece, reaction] of reactions) if (clock - reaction.start > .65 || !available(piece)) reactions.delete(piece);
  }

  function sample(piece, wobble = 0) {
    const out = { x: 0, z: 0, squash: 0, worldX: 0, worldZ: 0 };
    if (stopped || !available(piece)) return out;
    const type = piece.userData.type, firm = FIRM[type] || .6;
    const phase = piece.userData.phase || 0;
    // Baseline life in both modes, with firm bases and a little extra ramp energy.
    const amp = (.011 + .006 * clamp(wobble)) * firm;
    out.x = Math.sin(clock * .83 + phase) * amp;
    out.z = Math.cos(clock * .61 + phase * 1.7) * amp * .65;
    if (gesture?.piece === piece) {
      const t = clamp((clock - gesture.start) / gesture.duration);
      const envelope = Math.sin(Math.PI * t) ** 2, sign = gesture.variant;
      const wave = Math.sin(t * Math.PI * 2), pulse = Math.sin(t * Math.PI * 4);
      if (type === 'p') { out.squash += .012 * envelope; out.z += sign * .014 * wave * envelope; }
      if (type === 'n') { out.z += .075 * pulse * envelope; out.squash += .012 * wave * envelope; }
      if (type === 'b') { out.x += sign * .12 * wave * envelope; out.z += .048 * pulse * envelope; }
      if (type === 'r') { out.x += sign * .028 * wave * envelope; out.z += .01 * pulse * envelope; }
      if (type === 'q') { out.x += sign * .078 * wave * envelope; out.z += .044 * Math.cos(t * Math.PI * 2) * envelope; out.squash -= .019 * envelope; }
      if (type === 'k') { out.z += .065 * wave * envelope; out.squash -= .024 * envelope; }
    }
    const watcher = attention.find(p => p.piece === piece);
    if (watcher && focus) {
      const weight = smooth((clock - focus.start) / .35) * smooth((focus.until - clock) / .7);
      const amount = .018 * firm * weight * (1 - watcher.distance / 3);
      out.worldX -= watcher.dx / watcher.distance * amount;
      out.worldZ -= watcher.dz / watcher.distance * amount;
    }
    const reaction = reactions.get(piece);
    if (reaction) {
      const age = clock - reaction.start;
      const kick = Math.sin(age * 19) * Math.exp(-age * 8) * .075 * firm;
      out.worldX += reaction.dx * kick; out.worldZ += reaction.dz * kick;
    }
    return out;
  }

  function bindBus(bus) {
    for (const off of unbind.splice(0)) off();
    if (!bus?.on) return;
    unbind.push(bus.on('grab', p => { follow([...pieces()].find(piece => piece.userData.square === p?.square)); }));
    unbind.push(bus.on('hit', p => {
      if (stopped || p?.skipped || !p?.world || !['k', 'q', 'r'].includes(p.piece)) return;
      gesture = focus = null; attention = []; reactions.clear();
      for (const n of near(p.world)) reactions.set(n.piece, { start: clock, dx: n.dx / n.distance, dz: n.dz / n.distance });
    }));
    unbind.push(bus.on('newgame', reset), bus.on('local', reset));
  }
  return { update, sample, follow, bindBus, reset,
    stats: () => ({ gestures: gesture ? 1 : 0, gestureType: gesture?.piece?.userData.type || null,
      watching: attention.length, reacting: reactions.size, reduced: stopped }),
    dispose() { for (const off of unbind.splice(0)) off(); reset(); },
  };
}
