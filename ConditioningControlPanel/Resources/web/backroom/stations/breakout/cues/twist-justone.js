/* ============================================================================
 * stations/breakout/cues/twist-justone.js - Just One (The Ward).
 * Four sounds: the offer, the taking, the letting go, and the bill.
 *
 * House rules (AGENTS.md): every pitch is ROOT_HZ * SEMI(pentatonic(k)), every
 * NOTE lands on quantise(), a BODY plays at synth.now. Quiet (0.03 to 0.13) and
 * short. Failure subtracts: the comedown takes the top off the sound, it never
 * drops a pitch and it never gets louder than the catch it is paying for.
 * The catch itself already sings (cues/power.js fireball motif), so treatCatch
 * only adds the sparkle on top of it, an eighth in.
 * ==========================================================================*/

const pan = d => (Number.isFinite(d && d.xN) ? d.xN : 0.5);

export const CUES = {
  /** THE OFFER: one small bright bell, a fifth over the root. It is a question, not an alarm. */
  treatDrop(s, d) {
    const hz = s.ROOT_HZ * 2 * s.SEMI(s.pentatonic(3));
    s.play([s.tone(hz, 0.16, 0.035, { pan: pan(d), wet: true }),
      s.tone(hz * 2, 0.07, 0.012, { pan: pan(d) })], s.quantise(), s.bus.sfx);
  },

  /** TAKEN: a sparkle climbing over the fireball motif, a sixteenth apart, thinner each step. */
  treatCatch(s, d) {
    const six = s.beat.sixteenth, cut = s.hitCutoff(s.saturation);
    const notes = [7, 9, 12].map((k, i) => s.tone(s.ROOT_HZ * 2 * s.SEMI(s.pentatonic(k)), 0.1 + i * 0.03, 0.036 - i * 0.008,
      { at: six * (i + 1), wave: 'triangle', lp: Math.min(cut, 5200), pan: pan(d), wet: i > 0 }));
    s.play(notes, s.quantise(), s.bus.sfx);
  },

  /** LET GO: a soft wooden tick, nothing else. Missing a treat costs nothing, so it cannot sound like a loss. */
  treatMiss(s, d) {
    s.play([s.noise(1100, 0.04, 0.022, { type: 'bandpass', q: 2, pan: pan(d) })], s.now, s.bus.sfx);
  },

  /**
   * THE BILL: the room closes over. A low root under a breath whose filter shuts
   * (the high being taken back, not a fall), and the fifth leaning onto the root
   * an eighth later so it settles in key. Immediate: it is a body, not a note.
   */
  comedown(s, d) {
    const root = s.ROOT_HZ / 4, fifth = root * s.SEMI(s.pentatonic(3));
    s.play([
      s.tone(root, 0.5, 0.075, { wave: 'triangle', attack: 0.12, lp: 420, pan: 0.5 }),
      s.noise(1800, 0.42, 0.03, { hzTo: 300, q: 0.9, attack: 0.1, pan: 0.5, wet: true }),
      s.tone(fifth, 0.26, 0.05, { at: s.beat.sixteenth * 2, hzTo: root, wave: 'triangle', lp: 520, pan: 0.5 }),
    ], s.now, s.bus.sfx);
  },
};

export default CUES;
