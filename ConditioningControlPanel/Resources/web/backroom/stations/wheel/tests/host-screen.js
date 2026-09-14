/* ============================================================================
 * tests/host-screen.js - dev.html and wheel-check.mjs only. NOT product code.
 *
 * The host draws the fullscreen set (wash, gif-from, spiral-loom, tunnel) as app
 * overlays on the desktop (CONTRACT 10.13.B, lane H1). A headless page has no
 * desktop, so this canvas stands in for them: it reads what the kit's mock host
 * recorded and draws each primitive the way 10.13.B describes it, Calm halved
 * here as the host halves it (the page always sends Normal values). Screenshots
 * of a moment then show the whole moment, and the check still asserts on the
 * recorded calls, never on this drawing.
 * ==========================================================================*/

import { createLoomKit } from '../../../shared/hypno/index.js';

const easeIO = p => (p < 0.5 ? 2 * p * p : 1 - Math.pow(-2 * p + 2, 2) / 2);

/**
 * @param {{host, canvas: HTMLCanvasElement, urls: string[]}} o  urls: the pool the mock host deals, in deal order
 */
export function createHostScreen({ host, canvas, urls }) {
  const g = canvas.getContext('2d');
  const imgs = urls.map(u => Object.assign(new Image(), { src: u }));
  const kit = createLoomKit({ log: m => host.logs.push('host-screen: ' + m) });
  const s = { seen: 0, washes: [], gifs: [], spiral: null, tunnel: 0, want: 0, wantAt: -1e9, last: performance.now(), raf: 0 };
  const calm = () => host.ctx.intensity === 'calm' || host.ctx.reduced;
  const imgFor = key => { const i = /^g(\d{1,2})$/.exec(String(key || '')); return i ? imgs[Number(i[1]) % imgs.length] : null; };
  function cover(img, x, y, w, h, alpha) {
    if (!img || !img.complete || !img.naturalWidth) return;
    const k = Math.max(w / img.naturalWidth, h / img.naturalHeight), dw = img.naturalWidth * k, dh = img.naturalHeight * k;
    g.save(); g.beginPath(); g.rect(x, y, w, h); g.clip(); g.globalAlpha = alpha; g.drawImage(img, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh); g.restore();
  }
  function frame(now) {
    const W = innerWidth, H = innerHeight, K = calm() ? 0.5 : 1, DUR = calm() ? 0.6 : 1;
    if (canvas.width !== W || canvas.height !== H) { canvas.width = W; canvas.height = H; }
    const dt = Math.min(0.05, (now - s.last) / 1000); s.last = now;
    for (; s.seen < host.calls.length; s.seen++) {
      const c = host.calls[s.seen];
      if (c.type === 'fx-tunnel') { s.want = c.applied ? c.level * K : 0; s.wantAt = now; continue; }
      if (c.type === 'fx-release') { if (s.spiral && s.spiral.token === c.token) s.spiral.ending = now; continue; }
      if (c.type !== 'fx' || !c.ack.fired.length) continue;
      const a = c.args || {}, key = c.symbols && c.symbols[0];
      if (c.fxId === 'fx.wash') s.washes.push({ t0: now, color: a.color || '#9b6bff', k: (a.strength || 0.7) * K, key });
      if (c.fxId === 'fx.gif_from') s.gifs = [{ t0: now, dur: (a.ms || 3400) * DUR, key, from: a.from || { x: W / 2, y: H / 2, w: 8, h: 8 }, scale: a.scale || 1 }];
      if (c.fxId === 'fx.loom_spiral') s.spiral = { t0: now, dur: a.hold ? 20000 : (a.ms || 4200) * DUR, alpha: (a.alpha || 0.85) * K, preset: a.preset || 'screen', token: c.token };
    }
    if (now - s.wantAt > 1500) s.want = 0;   // the host's auto-release
    s.tunnel += (s.want - s.tunnel) * Math.min(1, dt * (s.want > s.tunnel ? 1.4 : 2.2));
    g.clearRect(0, 0, W, H);
    const sp = s.spiral;
    if (sp) {
      const age = now - sp.t0;
      let a = Math.min(1, age / 800);
      if (sp.ending) a *= 1 - Math.min(1, (now - sp.ending) / 1200); else if (age > sp.dur) a *= 1 - Math.min(1, (age - sp.dur) / 1200);
      if (a <= 0 && age > 800) s.spiral = null; else kit.draw(g, sp.preset, 0, 0, W, H, { now, alpha: a * sp.alpha });
    }
    s.gifs = s.gifs.filter(q => now - q.t0 < q.dur);
    for (const q of s.gifs) {
      const age = now - q.t0, grow = easeIO(Math.min(1, age / 700)), fade = age > q.dur - 900 ? Math.max(0, (q.dur - age) / 900) : 1;
      const tw = Math.max(W, (H * 4) / 3) * q.scale, th = (tw * 3) / 4, L = (x, y) => x + (y - x) * grow;
      if (q.scale >= 1) { g.globalAlpha = fade * (0.55 + 0.45 * grow) * (K < 1 ? 0.8 : 1); g.fillStyle = 'rgba(8,4,14,.55)'; g.fillRect(0, 0, W, H); g.globalAlpha = 1; }
      cover(imgFor(q.key), L(q.from.x, (W - tw) / 2), L(q.from.y, (H - th) / 2), L(q.from.w, tw), L(q.from.h, th), fade);
    }
    s.washes = s.washes.filter(f => now - f.t0 < 900);
    for (const f of s.washes) {
      const age = (now - f.t0) / 1000, env = age < 0.08 ? age / 0.08 : Math.exp(-(age - 0.08) * 4.5);
      g.globalAlpha = env * 0.42 * f.k; g.fillStyle = f.color; g.fillRect(0, 0, W, H); g.globalAlpha = 1;
      if (f.key) { const h = H * 0.42, w = (h * 4) / 3; cover(imgFor(f.key), (W - w) / 2, (H - h) / 2, w, h, Math.min(1, env * 1.3) * 0.85 * f.k); }
    }
    if (s.tunnel > 0.01) {
      const k = s.tunnel, R = Math.hypot(W, H) / 2;
      const gr = g.createRadialGradient(W / 2, H / 2, R * (1 - 0.72 * k), W / 2, H / 2, R * (1.12 - 0.5 * k));
      gr.addColorStop(0, 'rgba(6,3,12,0)'); gr.addColorStop(1, `rgba(6,3,12,${0.94 * Math.min(1, k * 1.3)})`);
      g.fillStyle = gr; g.fillRect(0, 0, W, H);
    }
    s.raf = requestAnimationFrame(frame);
  }
  s.raf = requestAnimationFrame(frame);
  return { get tunnel() { return s.tunnel; }, dispose() { cancelAnimationFrame(s.raf); kit.dispose(); } };
}
