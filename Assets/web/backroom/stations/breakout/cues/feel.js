/* ============================================================================
 * stations/breakout/cues/feel.js - the feel cues: the small events that used to
 * be silent (launch, one ball lost, the last brick, a layer entering) and the
 * perfect streak. Registered through cues.js; signature (synth, data) => void.
 *
 * House rules (AGENTS.md, "Audio design rules learned from Breakout"):
 *   - one key: every pitched note is ROOT_HZ * SEMI(pentatonic step), any octave.
 *     A note flagged `partial` is a bell's inharmonic overtone, not a pitch.
 *   - one clock: NOTES land on quantise(); BODIES (a tap, a breath) play at now.
 *   - failure subtracts: the lost ball settles, it never falls.
 *   - quiet and short: nothing here is louder than the hits it sits beside.
 * GREY is payload-free and dull: one low triangle under a 500 Hz low-pass, or nothing.
 * ==========================================================================*/

export const STREAK_CLIMB_MAX = 6;        // the perfect stamp's second note climbs at most this many pentatonic steps
const FIFTH = 3;                          // pentatonic(3) = 7 semitones: the stamp's resting second note

const pan = d => (Number.isFinite(d && d.xN) ? d.xN : 0.5);
const cutOf = (s, d) => s.hitCutoff(Number.isFinite(d && d.sat) ? d.sat : s.saturation);
const grey = (s, d) => ((d && d.state) || s.state) === 'grey';
/** The next BEAT boundary on the bed's grid (quantise() gives the next sixteenth; walk on to a multiple of four). */
function nextBeat(s) {
  const t = s.quantise(), k = Math.round((t - s.beat.origin) / s.beat.sixteenth);
  return t + ((4 - (((k % 4) + 4) % 4)) % 4) * s.beat.sixteenth;
}

export default {
  /** LAUNCH: a small upward blip, root then the fifth, on the grid. A note, so it is quantised. */
  launch(s, d) {
    const p = pan(d), t = s.quantise();
    if (grey(s, d)) { s.play([s.tone(s.ROOT_HZ / 2, 0.06, 0.06, { wave: 'triangle', lp: 500, pan: p })], t, s.bus.sfx); return; }
    const cut = cutOf(s, d);
    s.play([
      s.tone(s.ROOT_HZ, 0.06, 0.05, { wave: 'triangle', lp: cut, pan: p }),
      s.tone(s.ROOT_HZ * s.SEMI(s.pentatonic(FIFTH)), 0.11, 0.055, { at: 0.05, wave: 'triangle', lp: cut, pan: p }),
    ], t, s.bus.sfx);
  },

  /**
   * LOST: one ball of several is gone. A muffled low tap that settles on the root: a body, so it plays now. The only
   * downward motion is a 70 ms lean from the low fifth onto the root (both in key); nothing sweeps, nothing falls.
   */
  lost(s, d) {
    const p = pan(d), root = s.ROOT_HZ / 4;
    const notes = [
      s.tone(root, 0.16, 0.07, { wave: 'triangle', attack: 0.05, lp: 400, pan: p }),
      s.noise(240, 0.05, 0.035, { type: 'lowpass', pan: p }),
    ];
    if (!grey(s, d)) notes.push(s.tone(root * s.SEMI(s.pentatonic(FIFTH)), 0.07, 0.045, { hzTo: root, wave: 'triangle', lp: 600, pan: p }));
    s.play(notes, s.now, s.bus.sfx);
  },

  /**
   * LAST BRICK: the held breath before the wall-clear bells. A reverse swell of filtered noise (the envelope's attack is
   * nine tenths of its length) and a soft root an octave UNDER the bells, so wallCleared() has the top to itself.
   * It accompanies a hit-stop, so it is immediate, never quantised.
   */
  lastBrick(s, d) {
    const p = pan(d);
    if (grey(s, d)) { s.play([s.tone(s.ROOT_HZ / 4, 0.3, 0.05, { wave: 'triangle', attack: 0.5, lp: 500, pan: p })], s.now, s.bus.sfx); return; }
    s.play([
      s.noise(500, 0.4, 0.045, { hzTo: 3600, q: 1.4, attack: 0.9, pan: p, wet: true }),
      s.tone(s.ROOT_HZ / 2, 0.45, 0.04, { attack: 0.5, lp: Math.min(cutOf(s, d), 2400), pan: p, wet: true }),
    ], s.now, s.bus.sfx);
  },

  /**
   * LAYER: the song announcing its own new part, from the next beat. The melody gets a four-note pentatonic fill in
   * sixteenths (soft sines, the melody's voice); the arp gets a two-beat riser under four climbing eighth-note plucks
   * (triangles under 4.2 kHz, the arp's voice). Quiet on purpose: it should feel like the song doing it.
   */
  layer(s, d) {
    if (grey(s, d)) return false;
    const name = d && d.name, six = s.beat.sixteenth;
    if (name !== 'melody' && name !== 'arp') return false;
    const t = nextBeat(s);
    if (name === 'melody') {
      const notes = [1, 2, 3, 4].map((k, i) => s.tone(s.ROOT_HZ * s.SEMI(s.pentatonic(k)), six * 1.6, 0.04, { at: i * six, attack: 0.08, wet: true }));
      notes.push(s.tone(s.ROOT_HZ * 2 * s.SEMI(s.pentatonic(4)), 0.3, 0.012, { at: 3 * six, attack: 0.1, wet: true }));
      s.play(notes, t, s.bus.sfx);
    } else {
      const notes = [0, 2, 3, 5].map((k, i) => s.tone(s.ROOT_HZ * 2 * s.SEMI(s.pentatonic(k)), 0.13, 0.03, { at: i * 2 * six, wave: 'triangle', attack: 0.02, lp: 4200 }));
      notes.push(s.noise(600, six * 8, 0.016, { hzTo: 4800, q: 1.4, attack: 0.85, wet: true }));
      s.play(notes, t, s.bus.sfx);
    }
  },

  /**
   * PERFECT: skill is a melody. The first perfect returns false, so the older audio.perfect() stamp plays unchanged.
   * From the second in a row the same stamp plays here with its second note climbing the pentatonic with the streak
   * (capped at STREAK_CLIMB_MAX steps), and audio.js skips station.js's own au('perfect') for this moment.
   */
  perfect(s, d) {
    const streak = Math.floor(Number(d && d.streak) || 0);
    if (streak <= 1 || grey(s, d)) return false;
    const hz = s.ROOT_HZ * 2, up = hz * s.SEMI(s.pentatonic(FIFTH + Math.min(STREAK_CLIMB_MAX, streak - 1)));
    s.play([
      s.tone(hz, 0.16, 0.12, { wet: true }), s.tone(up, 0.28, 0.12, { at: 0.07, wet: true }),
      s.tone(up * 2.76, 0.1, 0.03, { at: 0.07, partial: true }),
    ], s.quantise(), s.bus.sfx);
    return true;
  },
};
