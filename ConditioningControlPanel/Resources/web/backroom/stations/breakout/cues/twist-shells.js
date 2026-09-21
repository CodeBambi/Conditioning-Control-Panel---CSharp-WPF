/* ============================================================================
 * stations/breakout/cues/twist-shells.js - the sound of an old self.
 * Twist: Shells (act 4). Two sounds: going through one, and letting it go.
 *
 * House rules (AGENTS.md): a BODY plays at synth.now, a NOTE lands on quantise()
 * and comes from pentatonic() + ROOT_HZ. Quiet (0.03 to 0.13) and short.
 * FAILURE SUBTRACTS: the touch is a muffled breath whose filter SHUTS - the room
 * being taken down a shade - and it never drops a pitch and never gets louder
 * than the release that follows it. The pop is the only pitched cue here.
 * ==========================================================================*/

const pan = d => (Number.isFinite(d && d.xN) ? d.xN : 0.5);

export const CUES = {
  /**
   * THROUGH ONE. A body, so it plays now: a short breath closing from 900 Hz down
   * to 260, with a low root under it that only takes the room's brightness away.
   * The higher its remaining hp the softer it is: the first touch is the surprise.
   */
  shellTouch(s, d) {
    const left = Math.max(0, Math.min(2, (d && d.hp) | 0));
    const gain = 0.042 - 0.006 * left;
    s.play([
      s.noise(900, 0.2, gain, { hzTo: 260, q: 0.8, attack: 0.02, pan: pan(d), wet: true }),
      s.tone(s.ROOT_HZ / 4, 0.24, gain * 0.7, { wave: 'triangle', attack: 0.05, lp: 380, pan: pan(d) }),
    ], s.now, s.bus.sfx);
  },

  /**
   * LET IT GO. The release, and the only thing on this board that sings: two notes
   * a sixteenth apart, rising into the key, thinner as they go. Quantised, because
   * it is a note and not a body. It is quiet: the shell gave a little colour back,
   * it did not win anything.
   */
  shellPop(s, d) {
    const six = s.beat.sixteenth;
    const notes = [5, 9].map((k, i) => s.tone(s.ROOT_HZ * s.SEMI(s.pentatonic(k)), 0.3 - i * 0.08, 0.05 - i * 0.014,
      { at: six * i, wave: 'triangle', attack: 0.02, lp: 3600, pan: pan(d), wet: true }));
    notes.push(s.noise(2600, 0.16, 0.016, { hzTo: 5200, q: 1.1, attack: 0.03, pan: pan(d), wet: true }));
    s.play(notes, s.quantise(), s.bus.sfx);
  },
};

export default CUES;
