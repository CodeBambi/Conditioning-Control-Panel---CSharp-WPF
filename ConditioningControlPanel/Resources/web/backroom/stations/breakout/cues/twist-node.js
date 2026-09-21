/* ============================================================================
 * stations/breakout/cues/twist-node.js - the Node twist's sounds.
 * Registered through cues.js; signature (synth, data) => void.
 *
 * House rules (AGENTS.md, "Audio design rules learned from Breakout"):
 *   - one key: every pitch is ROOT_HZ * SEMI(pentatonic step), some octave.
 *   - one clock: a NOTE lands on quantise(); a BODY plays at now. A zap is a
 *     body: the pops come 60 ms apart and the grid is 156 ms, so quantising the
 *     wave would stack three cracks on one sixteenth and lose the run. Its spark
 *     still takes its pitch from the scale, so the wave is in key either way.
 *   - failure subtracts: the current going out is a muffled switch, never a fall.
 *   - quiet and short: nothing here is louder than a brick.
 * ==========================================================================*/

const pan = d => (Number.isFinite(d && d.xN) ? d.xN : 0.5);
const grey = (s, d) => ((d && d.state) || s.state) === 'grey';
/** The pentatonic step a pop at this wire distance takes, wrapped so a long arm keeps climbing in key. */
export const zapStep = depth => (Number.isFinite(depth) ? Math.max(0, Math.floor(depth)) : 0) % 7;

export const CUES = {
  /**
   * NODECUT: a branch lost the current. A breaker throwing, not a defeat: a
   * short dull body, then the low root settling under it. Nothing sweeps down.
   */
  nodeCut(s, d) {
    const p = pan(d), size = Math.min(1, (Number(d && d.n) || 1) / 12);
    s.play([
      s.noise(430, 0.07, 0.05 + 0.03 * size, { q: 1.2, pan: p }),
      s.noise(180, 0.11, 0.035, { q: 0.9, attack: 0.02, pan: p }),
    ], s.now, s.bus.sfx);
    if (grey(s, d)) return;
    s.play([s.tone(s.ROOT_HZ / 4, 0.2, 0.05 + 0.03 * size, { wave: 'triangle', lp: 420, attack: 0.03, pan: p })],
      s.quantise(), s.bus.sfx);
  },

  /**
   * NODEZAP: one wire in the wave letting go. A crack with a spark on top, and
   * the spark climbs the scale with the wire distance, so the whole net reads as
   * a run outward rather than a pile of identical ticks.
   */
  nodeZap(s, d) {
    const p = pan(d), step = zapStep(d && d.depth);
    const hz = s.ROOT_HZ * 2 * s.SEMI(s.pentatonic(step));
    s.play([
      s.noise(3400, 0.018, 0.04, { hzTo: 6200, q: 2.2, pan: p }),
      s.tone(hz, 0.05, 0.035, { wave: 'triangle', lp: Math.min(s.hitCutoff(s.saturation), 5200), pan: p }),
    ], s.now, s.bus.sfx);
  },

  /**
   * COREDOWN: the player took the heart out. That is a win, so it rises: a body
   * first, then root and fifth climbing on the grid. Bigger net, brighter climb.
   */
  coreDown(s, d) {
    const p = pan(d), size = Math.min(1, (Number(d && d.n) || 1) / 20);
    s.play([
      s.tone(s.ROOT_HZ / 8, 0.26, 0.11, { wave: 'triangle', lp: 260, pan: p }),
      s.noise(260, 0.16, 0.05, { hzTo: 90, q: 0.8, pan: p }),
    ], s.now, s.bus.sfx);
    if (grey(s, d)) return;
    if (typeof s.duck === 'function') { try { s.duck(0.22, 0.4); } catch (e) { /* ducking is optional */ } }
    const six = s.beat.sixteenth, cut = s.hitCutoff(s.saturation);
    s.play([0, 3, 5].map((step, i) => s.tone(s.ROOT_HZ * s.SEMI(s.pentatonic(step)), 0.22 + i * 0.06, 0.05 + 0.04 * size,
      { at: i * six, wave: 'triangle', lp: cut, pan: p, wet: i > 0 })), s.quantise(), s.bus.sfx);
  },
};

export default CUES;
