/* ============================================================================
 * stations/breakout/cues/twist-keys.js - Keys and Gates, heard.
 *
 * Two sounds, and they are deliberately different kinds of sound:
 *   keyTurn  is a BODY. A lock is metal moving against metal, so it plays at
 *            synth.now, unquantised, and it is two clicks and a low thunk.
 *   gateOpen is a NOTE. The box letting go is the reward, so it lands on
 *            quantise() and climbs the pentatonic, one step per plate, gold in
 *            the middle octave and cyan an octave above it, so the ear knows
 *            which box opened before the eye gets there.
 * House rules (AGENTS.md, "Audio design rules learned from Breakout"): one key,
 * one clock, quiet (under 0.09) and short (under 300 ms a voice).
 * ==========================================================================*/

/** How many notes the arpeggio may climb, however many plates are moving. */
export const ARP_MAX = 5;
/** The pentatonic steps it climbs through. Five plates is a whole small phrase. */
export const ARP_STEPS = [0, 2, 3, 5, 7];

const pan = d => (Number.isFinite(d && d.xN) ? d.xN : 0.5);
const grey = (s, d) => ((d && d.state) || s.state) === 'grey';
/** Gold sits in the middle octave, cyan an octave up, the cracked key an octave down. */
export const octaveFor = gate => (gate === 'W' ? 2 : gate === 'clay' ? 0.5 : 1);
/** One note per plate, capped, so a two-plate box is a flick and a big one is a phrase. */
export const arpLength = n => Math.max(2, Math.min(ARP_MAX, Math.round(Number(n) || 0)));

export const CUES = {
  /**
   * KEY TURN: the lock. Two dry clicks a beat apart in miniature, plus a low
   * triangle thunk for the bolt drawing back. A body: it plays now.
   */
  keyTurn(s, d) {
    const p = pan(d), gate = d && d.gate;
    const notes = [
      s.noise(2400, 0.02, 0.07, { type: 'bandpass', q: 3.2, pan: p }),
      s.noise(1700, 0.035, 0.055, { at: 0.045, type: 'bandpass', q: 2.4, pan: p }),
      s.tone(s.ROOT_HZ / 4 * s.SEMI(s.pentatonic(0)), 0.09, 0.05, { at: 0.045, wave: 'triangle', lp: 420, pan: p }),
    ];
    if (gate === 'clay' && !grey(s, d)) {                    // the cracked key: the same lock, with grit in it
      notes.push(s.noise(900, 0.09, 0.035, { type: 'bandpass', q: 1.2, at: 0.06, pan: p }));
    }
    s.play(notes, s.now, s.bus.sfx);
  },

  /**
   * GATE OPEN: the box letting go, a rising arpeggio in the bed's key, one note
   * per plate, sixteenths, under a short breath of air that opens as it rises.
   * Grey is payload-free: one low triangle and nothing else.
   */
  gateOpen(s, d) {
    const p = pan(d), six = s.beat.sixteenth, t = s.quantise();
    if (grey(s, d)) { s.play([s.tone(s.ROOT_HZ / 2, 0.1, 0.05, { wave: 'triangle', lp: 500, pan: p })], t, s.bus.sfx); return; }
    const oct = octaveFor(d && d.gate), count = arpLength(d && d.n);
    const cut = s.hitCutoff(Number.isFinite(d && d.sat) ? d.sat : s.saturation);
    const notes = [];
    for (let i = 0; i < count; i++) {
      notes.push(s.tone(s.ROOT_HZ * oct * s.SEMI(s.pentatonic(ARP_STEPS[i])), 0.13 + i * 0.02, 0.042,
        { at: i * six, wave: 'triangle', attack: 0.02, lp: Math.min(cut, 5200), pan: p, wet: i >= count - 2 }));
    }
    notes.push(s.tone(s.ROOT_HZ * oct * 2 * s.SEMI(s.pentatonic(ARP_STEPS[count - 1])), 0.28, 0.016,
      { at: count * six, attack: 0.05, pan: p, wet: true }));                  // the lid, an octave over the top note
    notes.push(s.noise(700, six * (count + 1), 0.022, { hzTo: 4200, q: 1.3, attack: 0.8, pan: p, wet: true }));
    s.play(notes, t, s.bus.sfx);
  },
};

export default CUES;
