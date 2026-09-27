// Spell wall typography and letter flights. All effects stay behind the live ball.
export function createSpellRender({ W, H, FONT, reduced, particles }) {
  const flights = [];
  let finish = null;
  const layout = word => {
    const size = Math.min(78, W * 0.86 / Math.max(1, word.length * 0.66));
    const step = size * 0.66;
    return { size, step, x: W / 2 - (word.length - 1) * step / 2, y: H * 0.48 };
  };
  const reset = () => { flights.length = 0; finish = null; };
  function event(name, d) {
    if (name === 'relapse' || name === 'relapseStart') reset();
    if (name === 'spellFill') {
      flights.push({ ...d, t: 0, arrived: false });
      if (flights.length > 24) flights.shift();
    }
    if (name === 'spellComplete') {
      finish = { word: d.word, t: 0 };
      if (!reduced) {
        const L = layout(d.word);
        for (let i = 0; i < d.word.length; i++) if (d.word[i] !== ' ')
          particles.burst(L.x + i * L.step, L.y, [255, 207, 107], 6, 100, 0.8);
      }
    }
  }
  function background(g, s) {
    if (!s.spell || s.state === 'grey') return;
    const { word, filled } = s.spell, L = layout(word);
    g.save(); g.font = `900 ${L.size}px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
    for (let i = 0; i < word.length; i++) {
      const inFlight = flights.some(f => f.word === word && f.index === i && f.t < 0.55);
      g.fillStyle = filled[i] && !inFlight ? 'rgba(230,207,255,.3)' : 'rgba(205,190,225,.07)';
      g.fillText(word[i], L.x + i * L.step, L.y);
    }
    g.restore();
  }
  function front(g, s, dt) {
    if (!s.spell || s.state === 'grey') { reset(); return; }
    g.save(); g.textAlign = 'center'; g.textBaseline = 'middle';
    for (let i = flights.length - 1; i >= 0; i--) {
      const f = flights[i]; f.t += dt;
      if (f.t > 1) { flights.splice(i, 1); continue; }
      const L = layout(f.word), targetX = L.x + f.index * L.step;
      const t = Math.min(1, f.t / 0.55), ease = 1 - (1 - t) ** 3;
      const drift = !reduced && f.style === 'drift' ? Math.sin(t * Math.PI) * 35 : 0;
      const stationary = reduced || f.style === 'static';
      const x = stationary ? targetX : f.x + (targetX - f.x) * ease + drift;
      const y = stationary ? L.y : f.y + (L.y - f.y) * ease;
      const stamp = !reduced && f.style === 'stamp' ? 1 + 0.3 * Math.sin(t * Math.PI) : 1;
      g.font = `900 ${(stationary ? L.size : 15 + (L.size - 15) * ease) * stamp}px ${FONT}`;
      g.fillStyle = `rgba(245,223,255,${Math.max(0, 1 - Math.max(0, f.t - 0.55) / 0.45) * 0.8})`;
      g.fillText(f.letter, x, y);
      if (t === 1 && !f.arrived) {
        f.arrived = true;
        if (!reduced) particles.burst(targetX, L.y, [200, 160, 255], 5, 40, 0.35);
      }
    }
    if (finish) {
      finish.t += dt;
      const t = finish.t / 1.6, L = layout(finish.word);
      if (t >= 1) finish = null;
      else {
        const alpha = Math.min(1, finish.t / 0.18) * (1 - t);
        const flicker = !reduced && t > 0.8 && Math.floor(finish.t * 14) % 2 ? 0.2 : 1;
        for (let echo = reduced ? 0 : 2; echo >= 0; echo--) {
          const scale = reduced ? 1 : 1 + t * 1.8 + echo * 0.12;
          g.save(); g.translate(W / 2, L.y); g.scale(scale, scale);
          g.font = `900 ${L.size}px ${FONT}`;
          g.fillStyle = `rgba(${echo ? '170,125,230' : '255,235,255'},${alpha * flicker * (echo ? 0.1 : 0.7)})`;
          g.fillText(finish.word, 0, 0); g.restore();
        }
      }
    }
    g.restore();
  }
  return { event, background, front, reset };
}
