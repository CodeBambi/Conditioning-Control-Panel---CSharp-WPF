/* ============================================================================
 * stations/breakout/cues/twist-crumble.js - the sound of clay.
 * Twist: Crumble (Pink Fog). Cracks are BODIES: dry, tiny, now, never on the
 * grid (AGENTS.md rule 2, "quantise what is a note, never a body"). The chain
 * is the star, so each link is a pop with a pentatonic ring riding it, climbing
 * a degree per link - the player hears the run before they can count it. Only
 * the two payoffs, ready and the end of a run, land on quantise().
 * ==========================================================================*/

const pan = d => (Number.isFinite(d.xN) ? d.xN : 0.5);
/** The ladder: one degree per link, capped so a long vein does not end in a dog whistle. */
export const chainSemis = (synth, n) => synth.pentatonic(2 + Math.min(Math.max(1, n | 0) - 1, 14));

export const CUES = {
  /** A crack, not a break: dry grit at three hits, glass at two. Never quantised. */
  clayCrack(synth, d) {
    const { noise, tone } = synth, x = pan(d), last = (d.hp | 0) <= 1;
    synth.play([
      noise(last ? 2600 : 1250, 0.045, 0.05, { q: last ? 1.8 : 1.1, hzTo: last ? 1400 : 700, pan: x }),
      noise(520, 0.03, 0.035, { q: 0.9, pan: x }),
      ...(last ? [tone(synth.ROOT_HZ * 4, 0.05, 0.02, { hzTo: synth.ROOT_HZ * 3, pan: x })] : []),
    ], synth.now, synth.bus.sfx);
  },

  /** One hit left. A high, quiet tick, on the grid: it is a fuse being armed, so it joins the song. */
  clayReady(synth, d) {
    const { tone, ROOT_HZ, SEMI, pentatonic } = synth;
    synth.play([tone(ROOT_HZ * 2 * SEMI(pentatonic(6)), 0.11, 0.032, { wave: 'triangle', lp: 5200, pan: pan(d), wet: true })],
      synth.quantise(), synth.bus.sfx);
  },

  /**
   * A LINK. The shatter is a body and plays now; the ring on top of it is in the
   * bed's key and climbs a degree per link, so a vein going up reads as a run.
   */
  clayChain(synth, d) {
    const { tone, noise, ROOT_HZ, SEMI } = synth, x = pan(d), hz = ROOT_HZ * SEMI(chainSemis(synth, d.n)) * 2;
    synth.play([
      noise(2200, 0.03, 0.042, { q: 1.6, hzTo: 1100, pan: x }),
      tone(hz, 0.13, 0.055, { attack: 0.004, lp: 6000, pan: x, wet: true }),
      tone(hz * 2, 0.05, 0.016, { hzTo: hz, attack: 0.003, pan: x }),
    ], synth.now, synth.bus.sfx);
  },

  /** The run ends. A small payoff, and it grows with the run: one bell, then a chord under it. */
  clayChainEnd(synth, d) {
    const { tone, ROOT_HZ, SEMI, pentatonic } = synth, n = Math.max(1, d.n | 0);
    const gain = 0.035 + Math.min(n, 10) * 0.006, six = synth.beat.sixteenth;
    const notes = [tone(ROOT_HZ * 2, 0.4, gain, { attack: 0.01, lp: 6000, wet: true })];
    if (n >= 3) [0, 2, 4].forEach((deg, i) =>
      notes.push(tone(ROOT_HZ * SEMI(pentatonic(deg)), 0.5, gain * 0.55, { at: i * six, wave: 'triangle', lp: 3400, wet: true })));
    if (n >= 6) notes.push(tone(ROOT_HZ / 2, 0.6, gain * 0.7, { attack: 0.06, wave: 'triangle', lp: 900 }));
    synth.play(notes, synth.quantise(), synth.bus.sfx);
  },

  /** The whole wall went wobbly at once: a low swell with a shimmer sitting on it. */
  clayPrimed(synth, d) {
    const { tone, noise, ROOT_HZ, SEMI, pentatonic } = synth, six = synth.beat.sixteenth;
    const gain = 0.04 + Math.min(Math.max(1, d.n | 0), 16) * 0.002;
    synth.play([
      tone(ROOT_HZ / 2, 0.7, gain, { attack: 0.09, wave: 'triangle', lp: 1100, wet: true }),
      ...[7, 9, 12].map((deg, i) => tone(ROOT_HZ * 2 * SEMI(pentatonic(deg)), 0.22, gain * 0.4,
        { at: i * six, wave: 'triangle', lp: 6000, wet: true })),
      noise(3600, 0.5, 0.02, { q: 0.8, hzTo: 1800, attack: 0.3, wet: true }),
    ], synth.quantise(), synth.bus.sfx);
  },
};

export default CUES;
