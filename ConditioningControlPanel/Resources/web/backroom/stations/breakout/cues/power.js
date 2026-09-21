/* ============================================================================
 * Power-up cues: the laser as an instrument, a motif per catch, drop, miss,
 * warning and expiry. House rules (AGENTS.md, "Audio design rules learned from
 * Breakout"): every pitch is pentatonic() or a chord tone of the bed over
 * ROOT_HZ, every NOTE lands on quantise(), a BODY (the bolt's impact) plays at
 * synth.now. Quiet and short: nothing here is louder than a brick.
 * ==========================================================================*/

/** The bed's chords by bar, semitones above the root. A copy of audio.js CHORDS (private there, and audio.js
 *  imports this registry, so it cannot be imported back). power-cues.test.js fails if the two drift apart. */
export const CHORDS = [[0, 4, 7], [-3, 0, 4], [-5, 0, 4], [-3, 2, 7]];
const STEPS_PER_BAR = 16, LOOP_STEPS = 64;

/** The loop step (0..63) the bed plays at audio time `t`. */
export const loopStep = (synth, t) => { const i = synth.beat.stepIndex(t) % LOOP_STEPS; return i < 0 ? i + LOOP_STEPS : i; };
/** The chord under audio time `t`. */
export const chordAt = (synth, t) => CHORDS[Math.floor(loopStep(synth, t) / STEPS_PER_BAR) % CHORDS.length];
/**
 * The laser's note at `t`: the chord tone NEXT to the one the arp plays on that eighth, in the arp's octave, so
 * the two interlock as a harmony and the laser never just doubles the bed.
 */
export const laserSemis = (synth, t) => { const chord = chordAt(synth, t); return chord[(Math.floor(loopStep(synth, t) / 2) + 1) % chord.length] + 12; };

const pan = d => (Number.isFinite(d.xN) ? d.xN : 0.5);

/** kind -> notes. Two or three steps a sixteenth apart, so the motif itself sits on the grid. */
function catchMotif(synth, kind, d) {
  const { tone, noise, SEMI, ROOT_HZ, pentatonic } = synth, six = synth.beat.sixteenth, x = pan(d);
  const cut = synth.hitCutoff(synth.saturation), hz = k => ROOT_HZ * SEMI(pentatonic(k));
  switch (kind) {
    case 'multiball':                                   // the old split, tuned: a detuned pair climbing root, fifth, octave
      return [0, 7, 12].flatMap((semi, i) => [-1, 1].map(side => tone(ROOT_HZ * 2 * SEMI(semi) * (1 + side * 0.004), 0.14, 0.05,
        { at: i * six, wave: 'triangle', lp: 5000, pan: 0.5 + side * 0.22, wet: true })));
    case 'fireball':                                    // warm and low, G C E rising under a breath of air
      return [tone(hz(3) / 2, 0.2, 0.08, { wave: 'triangle', lp: Math.min(cut, 2200), pan: x }),
        tone(hz(5) / 2, 0.2, 0.08, { at: six, wave: 'triangle', lp: Math.min(cut, 2600), pan: x, wet: true }),
        tone(hz(7) / 2, 0.34, 0.07, { at: six * 2, wave: 'triangle', lp: Math.min(cut, 3200), pan: x, wet: true }),
        noise(700, 0.4, 0.03, { hzTo: 3200, q: 1.1, attack: 0.5, pan: x, wet: true })];
    case 'laser':                                       // bright, thin, high: the instrument tuning up
      return [hz(7), hz(9), hz(12)].map((f, i) => tone(f, 0.09 + i * 0.04, 0.045, { at: i * six, wave: 'sawtooth', lp: 3600, pan: x, wet: i === 2 }));
    case 'shield':                                      // two glass bells, the fifth then the root above it: something solid arrived
      return [hz(3), hz(5)].flatMap((f, i) => [tone(f, 0.36, 0.07, { at: i * six * 2, pan: x, wet: true }),
        tone(f * 4, 0.1, 0.014, { at: i * six * 2, pan: x })]);
    default: return null;
  }
}

export default {
  /** A pickup left its brick: one tiny bell, so the eye goes looking. */
  powerDrop(synth, d) {
    const { tone, ROOT_HZ, SEMI, pentatonic } = synth;
    synth.play([tone(ROOT_HZ * 2 * SEMI(pentatonic(2)), 0.12, 0.03, { pan: pan(d), wet: true })], synth.quantise(), synth.bus.sfx);
  },
  powerCatch(synth, d) {
    const notes = catchMotif(synth, d.kind, d);
    if (!notes) return false;                           // an unknown kind keeps the caller's fallback
    synth.play(notes, synth.quantise(), synth.bus.sfx);
    return true;
  },
  /** It fell past: one dull, low, short note. Nothing falls in pitch; the chance is simply gone. */
  powerMiss(synth, d) {
    const { tone, ROOT_HZ } = synth;
    synth.play([tone(ROOT_HZ / 4, 0.16, 0.04, { wave: 'triangle', lp: 380, attack: 0.05, pan: pan(d) })], synth.quantise(), synth.bus.sfx);
  },
  /** Two seconds left: two soft ticks stepping down, an eighth apart. */
  powerWarn(synth) {
    const { tone, ROOT_HZ, SEMI, pentatonic } = synth, six = synth.beat.sixteenth;
    synth.play([pentatonic(8), pentatonic(7)].map((semi, i) => tone(ROOT_HZ * SEMI(semi), 0.06, 0.04, { at: i * six * 2, wave: 'triangle', lp: 3000 })),
      synth.quantise(), synth.bus.sfx);
  },
  /** It ended: one soft settle on the root. */
  powerExpire(synth) {
    const { tone, ROOT_HZ } = synth;
    synth.play([tone(ROOT_HZ, 0.32, 0.04, { attack: 0.12, lp: 1800, wet: true })], synth.quantise(), synth.bus.sfx);
  },
  /**
   * THE LASER PLAYS ALONG. One pluck per volley from the chord of the bar it lands in. powerups.js fires the volley
   * a hair before the eighth, so quantise() puts this exactly on it, beside the arp.
   */
  laserShot(synth, d) {
    const { tone, ROOT_HZ, SEMI } = synth, t = synth.quantise(), hz = ROOT_HZ * SEMI(laserSemis(synth, t)), x = pan(d);
    synth.play([tone(hz, 0.11, 0.05, { wave: 'triangle', attack: 0.015, lp: Math.max(2400, synth.hitCutoff(synth.saturation)), pan: x, wet: true }),
      tone(hz * 2, 0.05, 0.016, { hzTo: hz, attack: 0.01, pan: x })], t, synth.bus.sfx);   // the octave falling into the note: the "pew"
  },
  /** The bolt lands: a body, so it is dry, tiny and NOW. */
  laserHit(synth, d) {
    const { noise } = synth, x = pan(d);
    synth.play([noise(5200, 0.012, 0.035, { q: 2, pan: x }), noise(1900, 0.02, 0.02, { q: 1.4, pan: x })], synth.now, synth.bus.sfx);
  },
};
