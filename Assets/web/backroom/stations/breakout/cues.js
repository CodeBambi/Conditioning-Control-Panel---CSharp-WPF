/* ============================================================================
 * stations/breakout/cues.js - the cue registry. name -> (synth, data) => void.
 *
 * station.js offers every game event to audio.cue(name, data) before its own
 * switch, so a lane adds a sound by adding a key here, never by editing
 * audio.js or station.js. `data` is the event payload plus xN (0..1 pan), sat,
 * state and combo. The synth is the word synth plus the grid:
 *   { ctx, now, play, tone, noise, bus, glide, SEMI, ROOT_HZ, pentatonic,
 *     hitSemis, hitCutoff, duck, beat, quantise(lead), saturation, state, room }
 * House rules (AGENTS.md, "Audio design rules learned from Breakout"): every
 * pitched cue comes from pentatonic() + ROOT_HZ and lands on quantise(); a
 * physical impact plays at synth.now and is never quantised. Return false to
 * let the caller play its older fallback.
 * ==========================================================================*/
import POWER from './cues/power.js';
import FEEL from './cues/feel.js';

export const CUES = { ...POWER, ...FEEL };
export default CUES;
