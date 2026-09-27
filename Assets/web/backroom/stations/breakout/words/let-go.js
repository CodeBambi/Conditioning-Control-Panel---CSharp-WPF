/* LET GO: five seconds of protection, with a one-second expiry warning.
 * The player keeps paddle control. A full-width shield catches missed balls.
 */
const DUR = 5;
export const shieldY = (p, h) => Math.min(h - 8, p.y + p.h / 2 + 16);
export const bloomAt = t => t >= DUR ? 0 : t < 0.35 ? (Math.cos(t * Math.PI * 18) > 0 ? 1 : 0.35) : t < 4 ? 1 : (1 - (t - 4)) * (Math.cos((t - 4) * Math.PI * 6) > 0 ? 1 : 0.18);
export default {
  key: 'LET GO', heavy: false, dur: DUR,
  sim: {
    start() {},
    tick(g) { g.mod.safe = true; g.mod.shield = true; },
    end() {},
  },
  render: {
    world() {},
    over(ctx, s, fx, R) {
      if (!s.paddle) return;
      const remaining = (fx.dur || DUR) - fx.t;
      const fade = fx.fade || 1;
      const a = fx.fade ? Math.max(0, Math.min(1, remaining / fade)) *
        (R.reduced || remaining > fade || Math.cos(fx.t * Math.PI * 12) > 0 ? 1 : .18)
        : R.reduced ? Math.min(1, Math.max(0, remaining)) : bloomAt(fx.t);
      const y = shieldY(s.paddle, R.H);
      ctx.save();
      ctx.lineCap = 'round';
      ctx.strokeStyle = R.col(R.MINT, R.mix, 0.28 * a); ctx.lineWidth = 14;
      ctx.beginPath(); ctx.moveTo(5, y); ctx.lineTo(R.W - 5, y); ctx.stroke();
      ctx.strokeStyle = R.col(R.MINT, R.mix, 1 * a); ctx.lineWidth = 3;
      ctx.beginPath(); ctx.moveTo(5, y); ctx.lineTo(R.W - 5, y); ctx.stroke();
      ctx.strokeStyle = R.col(R.WHITE, 1, 0.9 * a); ctx.lineWidth = 1;
      ctx.beginPath(); ctx.moveTo(5, y); ctx.lineTo(R.W - 5, y); ctx.stroke();
      if (!R.reduced && !fx.data.shieldSparked) {
        fx.data.shieldSparked = true;
        for (let x = 12; x < R.W; x += 32) R.P.burst(x, y, R.MINT, 3, 55, 0.45);
      }
      ctx.restore();
    },
    post() {},
  },
  sound(synth, fx) {
    const { tone, play, now, dest, SEMI, ROOT_HZ } = synth;
    synth.duck(0.25, DUR - 0.3, 0.5);
    const voices = [];
    // The chord: three airy voices in a major spread, each drifting slightly upward as it holds.
    [[0, 'triangle', 0.4, 0.03], [4, 'sine', 0.5, 0.026], [7, 'triangle', 0.6, 0.024]].forEach(([semi, wave, pan, level], i) => {
      const hz = ROOT_HZ * SEMI(semi);
      voices.push(tone(hz, DUR - 0.2, level, { hzTo: hz * 1.025, wave, attack: 0.42, lp: 2400, wet: true, pan, at: i * 0.05 }));
    });
    // The bell: one soft sine with a fast decay as the shield expires, and a faint partial for the ring.
    const bell = ROOT_HZ * 4;
    voices.push(tone(bell, 0.5, 0.05, { at: DUR, attack: 0.012, hzTo: bell * 0.996, wet: true, pan: 0.5 }));
    voices.push(tone(bell * 2.76, 0.16, 0.012, { at: DUR, attack: 0.02, wet: true, pan: 0.5 }));
    play(voices, now, dest);
  },
};
