// Small stylized audience, entirely nonspoken. Every source uses the game's master.
export function createCrowdVoices({ ctx, master, noise, random = Math.random }) {
  const sources = new Set();
  function voice({ at = 0, sec = .1, gain = .03, hz = 1000, end = hz, noiseVoice = false, pan = 0, attack = .015 }) {
    const source = noiseVoice ? ctx.createBufferSource() : ctx.createOscillator();
    if (noiseVoice) { source.buffer = noise; source.loop = true; }
    else { source.type = 'triangle'; source.frequency.setValueAtTime(hz, ctx.currentTime + at);
      for (let i = 1; i <= 12; i++) source.frequency.linearRampToValueAtTime(hz + (end - hz) * i / 12 + Math.sin(i * 2.3) * hz * .012, ctx.currentTime + at + sec * i / 12); }
    const filter = ctx.createBiquadFilter(); filter.type = 'bandpass';
    filter.frequency.value = noiseVoice ? hz : hz * 2.5;
    if (filter.Q) filter.Q.value = noiseVoice ? .75 : 1.3;
    const env = ctx.createGain(), start = ctx.currentTime + at;
    env.gain.setValueAtTime(.0001, start);
    env.gain.exponentialRampToValueAtTime(gain, start + Math.min(attack, sec / 2));
    env.gain.exponentialRampToValueAtTime(.0001, start + sec);
    source.connect(filter); filter.connect(env);
    if (ctx.createStereoPanner) { const panner = ctx.createStereoPanner(); panner.pan.value = pan; env.connect(panner); panner.connect(master); }
    else env.connect(master);
    sources.add(source); source.onended = () => sources.delete(source);
    source.start(start); source.stop(start + sec + .03);
  }
  function play(kind) {
    const positive = kind !== 'crowdBoo';
    if (positive) for (let i = 0; i < (kind === 'crowdCheer' ? 24 : 13); i++) {
      const at = .04 + i * .047 + random() * .075, pan = random() * 1.6 - .8;
      voice({ at, sec: .045 + random() * .055, gain: .030 + random() * .025, hz: 850 + random() * 2100, noiseVoice: true, pan, attack: .003 });
      if (i % 3 === 0) voice({ at, sec: .05, gain: .015, hz: 175 + random() * 90, end: 100, pan });
    }
    if (kind !== 'crowdApplause') {
      for (let i = 0; i < 5; i++) {
        const hz = (positive ? 270 : 150) + random() * 95;
        voice({ at: i * .055, sec: .7 + random() * .3, gain: .014, hz, end: hz * (positive ? 1.35 : .78), pan: (i - 2) * .3, attack: .18 });
      }
      voice({ sec: 1.05, gain: .055, hz: positive ? 1500 : 550, noiseVoice: true, attack: .2 });
      voice({ at: .16 + random() * .15, sec: .42, gain: .024, hz: positive ? 1900 : 2300,
        end: positive ? 2450 : 1400, pan: random() - .5, attack: .07 });
    }
  }
  return { play, cancel() { for (const source of sources) { try { source.stop(); } catch { /* already ended */ } } sources.clear(); } };
}
