// Original duet in C major pentatonic. Seconds, never frame counts, judge a note.
export const BEAT = 60 / 96;
export const WINDOW = .3;
export const PITCHES = [60, 64, 67, 69];
const PHRASES = [
  [0, 1, 2, 1], [0, 2, 3, 2], [1, 2, 3, 2],
  [3, 2, 1, 0], [0, 1, 3, 2], [2, 1, 2, 0],
];
export function phrase(round, branch = 0) {
  const pads = PHRASES[round % PHRASES.length];
  return pads.map((pad, i) => ({ pad: branch && round >= 2 ? [2, 1, 0, 2][i] : pad, beat: i * 2 }));
}
export function cuesFor(notes, start = 0) {
  return notes.map(n => ({ pad: n.pad, time: start + (8 + n.beat) * BEAT, hit: false }));
}
export function matchCue(cues, pad, time) {
  return cues.filter(c => !c.hit && c.pad === pad && Math.abs(c.time - time) <= WINDOW)
    .sort((a, b) => Math.abs(a.time - time) - Math.abs(b.time - time))[0] || null;
}
export function scoreFor(notes, layers = 0, echo = []) {
  const score = [];
  for (let beat = 0; beat < 16; beat++) {
    if (beat % 4 === 0) score.push({ time: beat * BEAT, midi: beat % 8 ? 43 : 48, gain: .12, length: 1.1 });
    if (layers > 0 && beat % 2 === 1) score.push({ time: beat * BEAT, midi: 55, gain: .025, length: .3 });
    if (layers > 1 && beat % 4 === 2) score.push({ time: beat * BEAT, midi: 64, gain: .045, length: .55 });
    if (layers > 2 && beat % 4 === 3) score.push({ time: beat * BEAT, midi: 72, gain: .022, length: .3 });
  }
  // The latest completed phrase returns softly between lead notes.
  for (const n of echo) for (const half of [0, 8]) {
    score.push({ time: (half + n.beat + 1) * BEAT, midi: PITCHES[n.pad] - 12, gain: .055, length: .45 });
  }
  for (const n of notes) score.push({ time: n.beat * BEAT, midi: PITCHES[n.pad], gain: .16, length: .6 });
  return score.sort((a, b) => a.time - b.time);
}
export function makeClock(source) {
  let base = source(), frozen = 0, paused = false;
  return {
    time: () => paused ? frozen : source() - base,
    pause() { if (!paused) { frozen = source() - base; paused = true; } },
    resume() { if (paused) { base = source() - frozen; paused = false; } },
  };
}
