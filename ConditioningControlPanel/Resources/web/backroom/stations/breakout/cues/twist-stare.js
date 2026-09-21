/* ============================================================================
 * stations/breakout/cues/twist-stare.js - the sound of being watched.
 * Twist: Stare (act 3). Registered through cues.js; signature (synth, data) => void.
 *
 * House rules (AGENTS.md, "Audio design rules learned from Breakout"):
 *   - one key: every pitch is ROOT_HZ * SEMI(pentatonic step), some octave.
 *   - one clock: a NOTE lands on quantise(); a BODY plays at now. The eye has
 *     no impacts, so almost everything here is a note.
 *   - failure SUBTRACTS: being judged ducks the mix and puts a low root under
 *     it. Nothing falls, nothing stings. Looking away is what rises.
 *   - quiet and short: the eye is never louder than a brick.
 * ==========================================================================*/

const pan = d => (Number.isFinite(d && d.xN) ? d.xN : 0.5);
const grey = (s, d) => ((d && d.state) || s.state) === 'grey';
/** The note the eye holds: the fifth, two octaves down, where it sits under everything. */
const holdHz = s => s.ROOT_HZ / 4 * s.SEMI(s.pentatonic(3));

export const CUES = {
  /**
   * STAREON: the room turns and looks. Not an alarm: a low fifth fading in
   * under the bed and one breath of air. Uneasy because it is close, not loud.
   */
  stareOn(s, d) {
    const p = pan(d);
    s.play([s.noise(240, 0.22, 0.03, { hzTo: 150, q: 0.7, attack: 0.09, pan: p })], s.now, s.bus.sfx);
    if (grey(s, d)) return;
    s.play([s.tone(holdHz(s), 0.44, 0.045, { wave: 'triangle', lp: 320, attack: 0.16, pan: p })],
      s.quantise(), s.bus.sfx);
  },

  /**
   * STAREJUDGE: bricks went grey. The mix loses something for a moment and the
   * root settles under it. No hit, no sting: this is the sound of the music
   * being taken down a notch, which is the whole lesson of the twist.
   */
  stareJudge(s, d) {
    const n = Math.max(1, Number(d && d.n) || 1), p = pan(d);
    s.play([s.noise(330, 0.13, 0.035, { hzTo: 120, q: 0.9, attack: 0.03, pan: p })], s.now, s.bus.sfx);
    if (grey(s, d)) return;
    if (typeof s.duck === 'function') { try { s.duck(0.18 + 0.03 * n, 0.55); } catch (e) { /* ducking is optional */ } }
    s.play([
      s.tone(s.ROOT_HZ / 8, 0.42, 0.055, { wave: 'triangle', lp: 210, attack: 0.05, pan: p }),
      s.tone(holdHz(s), 0.3, 0.035, { wave: 'sine', lp: 280, attack: 0.08, pan: p, wet: true }),
    ], s.quantise(), s.bus.sfx);
  },

  /**
   * STAREOFF: out of sight, and the judgement lifts. The release: two steps up
   * the scale on the grid, brighter than anything the eye plays. Giving back is
   * allowed to be pretty.
   */
  stareOff(s, d) {
    if (grey(s, d)) return;
    const p = pan(d), six = s.beat.sixteenth, cut = s.hitCutoff(s.saturation);
    s.play([2, 4].map((step, i) => s.tone(s.ROOT_HZ * s.SEMI(s.pentatonic(step)), 0.18 + i * 0.05, 0.035,
      { at: i * six, wave: 'triangle', lp: cut, attack: 0.01, pan: p, wet: i > 0 })), s.quantise(), s.bus.sfx);
  },
};

export default CUES;
