import { BEAT, PITCHES, WINDOW, phrase, cuesFor, matchCue, scoreFor, makeClock } from './feel-score.js';
import { STYLE } from './feel-style.js';

// A self-contained feel slice. The host owns progression; this module owns one duet.
export function create(ctx) {
  const root = ctx.root, listeners = [], voices = new Set();
  const t = (key, fallback) => ctx.lexicon?.(`echo_feel_${key}`, fallback) ?? fallback;
  let stage, title, copy, hint, progress, overlay, pads, dots, layers, ac, master, clock;
  let mode = 'ready', dead = false, started = false, ended = false, paused = false;
  let hidden = document.hidden, suspended = false, frame = 0, timer = 0, round = 0, branch = 0;
  let roundStart = 0, notes = [], cues = [], score = [], savedPhrase = [], scheduled = 0, earned = 0, totalHits = 0;
  let pressedUntil = [0, 0, 0, 0], hitUntil = [0, 0, 0, 0], phase = '';
  const listen = (node, event, fn) => { node.addEventListener(event, fn); listeners.push(() => node.removeEventListener(event, fn)); };
  const stopped = () => dead || paused || hidden || suspended;
  function el(tag, className, text) {
    const node = document.createElement(tag); node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }
  function action(text, fn) {
    const b = el('button', 'ef-action', text); b.type = 'button'; listen(b, 'click', fn); return b;
  }
  function modal(heading, detail, actions = []) {
    overlay.replaceChildren(el('h2', '', heading), el('p', '', detail));
    const row = el('div', 'ef-choices'); actions.forEach(a => row.append(a)); overlay.append(row); overlay.hidden = false;
  }
  function mount() {
    stage = el('section', 'ef'); stage.setAttribute('aria-label', t('title', 'Echo'));
    const style = document.createElement('style'); style.textContent = STYLE; stage.append(style);
    const head = el('div', 'ef-head'); head.append(el('span', '', t('eyebrow', 'ECHO / YOUR DUET')));
    progress = el('span', 'ef-progress', '0 / 6'); head.append(progress); stage.append(head);
    title = el('h2', '', t('title', 'Finish the tune'));
    copy = el('p', 'ef-copy', t('instruction', 'Hear the phrase. Tap the glowing pads when their rings fill.'));
    stage.append(title, copy);
    const arrangement = el('div', 'ef-layers'); arrangement.setAttribute('aria-label', t('layers', 'Your musical layers'));
    layers = Array.from({ length: 6 }, () => { const n = el('span', 'ef-layer'); arrangement.append(n); return n; });
    const beats = el('div', 'ef-beats'); beats.setAttribute('aria-hidden', 'true');
    dots = Array.from({ length: 8 }, () => { const n = el('span', 'ef-beat'); beats.append(n); return n; });
    stage.append(arrangement, beats);
    const grid = el('div', 'ef-pads'), colors = ['#efb79f', '#d7b5ef', '#a6d8ce', '#e6d697'];
    const symbols = ['\u25cb', '\u25c7', '\u25b3', '\u2726'];
    pads = colors.map((color, i) => {
      const b = el('button', 'ef-pad'); b.type = 'button'; b.style.setProperty('--pad', color);
      b.setAttribute('aria-label', `${t('pad', 'Pad')} ${i + 1}`);
      const face = el('span', 'ef-face'); face.append(el('span', 'ef-symbol', symbols[i]), el('small', '', `${i + 1}`));
      b.append(face, el('span', 'ef-ring')); grid.append(b);
      listen(b, 'pointerdown', e => { if (e.button === 0) { e.preventDefault(); hit(i); } });
      listen(b, 'click', e => { if (e.detail === 0) hit(i); });
      listen(b, 'keydown', e => {
        if ((e.key === ' ' || e.key === 'Enter') && !e.repeat) { e.preventDefault(); hit(i); }
      });
      return b;
    });
    stage.append(grid); hint = el('p', 'ef-hint', t('hint', 'Four pads. One little song.'));
    hint.setAttribute('aria-live', 'polite'); stage.append(hint); overlay = el('div', 'ef-overlay'); stage.append(overlay);
    root.replaceChildren(stage);
    modal(t('title', 'Finish the tune'), t('intro', 'The room starts. You answer. Each phrase you finish adds to the song.'),
      [action(t('start', 'Play duet'), begin)]);
    listen(document, 'keydown', e => {
      if (/^[1-4]$/.test(e.key) && !e.repeat && !e.altKey && !e.ctrlKey && !e.metaKey) { e.preventDefault(); hit(Number(e.key) - 1); }
    });
    listen(document, 'visibilitychange', () => { hidden = document.hidden; syncPause(); });
  }
  async function begin() {
    if (started || dead) return;
    started = true;
    try {
      const Audio = window.AudioContext || window.webkitAudioContext;
      if (Audio) {
        ac = new Audio(); master = ac.createGain(); master.gain.value = ctx.audioAudible === false ? 0 : .7;
        master.connect(ac.destination); await ac.resume();
      }
    } catch { ac?.close().catch(() => {}); ac = null; }
    if (dead) { ac?.close().catch(() => {}); return; }
    clock = makeClock(() => ac ? ac.currentTime : performance.now() / 1000);
    overlay.hidden = true; startRound(); syncPause();
  }
  function sound(midi, when, gain = .15, length = .65) {
    if (!ac || voices.size >= 28 || dead) return;
    // Two quiet partials: a soft wooden body and a brief brighter attack.
    const at = Math.max(ac.currentTime, when), frequency = 440 * 2 ** ((midi - 69) / 12);
    for (const [multiple, level, decay] of [[1, 1, length], [2, .16, .085]]) {
      const oscillator = ac.createOscillator(), envelope = ac.createGain();
      oscillator.type = 'sine'; oscillator.frequency.value = frequency * multiple;
      envelope.gain.setValueAtTime(.0001, at);
      envelope.gain.exponentialRampToValueAtTime(Math.max(.0002, gain * level), at + .006);
      envelope.gain.exponentialRampToValueAtTime(.0001, at + decay);
      oscillator.connect(envelope); envelope.connect(master); voices.add(oscillator);
      oscillator.onended = () => { oscillator.disconnect(); envelope.disconnect(); voices.delete(oscillator); };
      oscillator.start(at); oscillator.stop(at + decay + .02);
    }
  }
  function silence() {
    for (const voice of voices) { try { voice.stop(); } catch {} }
    voices.clear();
  }
  function schedule() {
    if (stopped() || !clock || (mode !== 'play' && mode !== 'outro')) return;
    const now = clock.time();
    if (master) master.gain.setTargetAtTime(ctx.audioAudible === false ? 0 : .7, ac.currentTime, .025);
    while (scheduled < score.length && score[scheduled].time + roundStart < now + .12) {
      const n = score[scheduled++], due = n.time + roundStart;
      if (due >= now - .08 && ac) sound(n.midi, ac.currentTime + Math.max(0, due - now), n.gain, n.length);
    }
  }
  function startRound() {
    mode = 'play'; phase = ''; roundStart = clock.time() + 2 * BEAT;
    notes = phrase(round, branch); cues = cuesFor(notes, roundStart); score = scoreFor(notes, earned, savedPhrase); scheduled = 0;
    hint.textContent = round === 0 ? t('first_hint', 'Listen first. The rings show you when to join.') : t('next_hint', 'The next phrase fits right in.');
    progress.textContent = `${round + 1} / 6`;
  }
  function hit(pad) {
    if (stopped() || mode !== 'play' || !clock) return;
    const now = clock.time(); pressedUntil[pad] = now + .15;
    pads[pad].dataset.pressed = 'true';
    sound(PITCHES[pad], ac?.currentTime || 0, .18);
    const cue = matchCue(cues, pad, now);
    if (cue) {
      cue.hit = true; totalHits++; hitUntil[pad] = now + .42;
      pads[pad].dataset.hit = 'true';
      hint.textContent = t('hit', 'Yes. Keep it going.');
    } else if (now >= roundStart + 8 * BEAT - WINDOW) hint.textContent = t('rejoin', 'Keep the song going. Catch the next ring.');
  }
  function finishRound() {
    if (cues.filter(c => c.hit).length >= 3) {
      savedPhrase = notes;
      layers[earned++].dataset.on = 'true';
      layers[earned - 1].setAttribute('aria-label', `${t('layer_added', 'Layer added')} ${earned}`);
    }
    round++;
    if (round === 2) {
      mode = 'choice'; clock.pause(); stopLoops(); silence();
      const choose = value => {
        if (mode !== 'choice' || dead) return;
        branch = value; overlay.hidden = true; clock.resume(); startRound(); syncPause();
      };
      modal(t('choice', 'Where next?'), t('choice_copy', 'Choose the next phrase. Both belong in your song.'), [
        action(t('rise', 'Let it rise'), () => choose(0)), action(t('fall', 'Bring it home'), () => choose(1)),
      ]);
    } else if (round >= 6) finishSong();
    else startRound();
  }
  function finishSong() {
    mode = 'outro'; roundStart = clock.time(); scheduled = 0; stage.dataset.finale = 'true';
    score = [
      { time: 0, midi: 55, gain: .12, length: .9 }, { time: 0, midi: 62, gain: .08, length: .8 },
      ...[48, 60, 64, 67].map(midi => ({ time: BEAT * 2, midi, gain: .075, length: 1.4 })),
    ];
    title.textContent = t('ending', 'Your little song');
    copy.textContent = t('ending_copy', 'One last chord. Let it land.');
    hint.textContent = `${totalHits} / 24 ${t('notes', 'notes joined')}`;
  }
  function draw() {
    frame = 0;
    if (stopped() || !clock || (mode !== 'play' && mode !== 'outro')) return;
    const now = clock.time(), local = now - roundStart;
    const reduced = Number(ctx.motion?.motionLevel ?? 2) < 2 || window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    stage.dataset.reduced = String(reduced);
    if (mode === 'play') {
      const nextPhase = local < 0 ? 'ready' : local < 8 * BEAT ? 'listen' : 'answer';
      if (nextPhase !== phase) {
        phase = nextPhase;
        title.textContent = phase === 'listen' ? t('listen', 'The room plays') : phase === 'answer' ? t('answer', 'Your turn') : t('ready', 'Here comes the phrase');
        copy.textContent = phase === 'answer' ? t('answer_copy', 'Tap when each ring reaches its pad.') : t('listen_copy', 'Listen and watch. Your part comes next.');
      }
      pads.forEach((pad, i) => {
        const cue = cues.find(c => c.pad === i && !c.hit && c.time - now < BEAT && c.time - now >= -WINDOW);
        pad.dataset.cue = String(Boolean(cue));
        pad.style.setProperty('--arrival', cue ? String(.65 + .35 * Math.min(1, 1 - (cue.time - now) / BEAT)) : '.65');
        pad.dataset.lit = String(phase === 'listen' && notes.some(n => n.pad === i && local >= n.beat * BEAT && local < n.beat * BEAT + .24));
        pad.dataset.pressed = String(now < pressedUntil[i]); pad.dataset.hit = String(now < hitUntil[i]);
      });
      if (local >= 16 * BEAT) finishRound();
    } else if (local >= BEAT * 2 + 1.6) {
      mode = 'done'; stopLoops();
      modal(t('done', 'You made that'), `${totalHits} / 24 ${t('joined', 'notes joined. A complete duet.')}`);
      if (!ended) {
        ended = true;
        ctx.endClass?.({ metrics: { composite: totalHits / 24 }, hardGates: {}, flavorXp: 0, feelVersion: 2 });
      }
    }
    dots.forEach((dot, i) => { dot.dataset.on = String(local >= 0 && Math.floor(local / BEAT) % 8 === i); });
    if (!dead && (mode === 'play' || mode === 'outro')) frame = requestAnimationFrame(draw);
  }
  function stopLoops() { cancelAnimationFrame(frame); clearInterval(timer); frame = timer = 0; }
  function syncPause() {
    if (!clock || dead) return;
    if (stopped() || mode === 'choice') {
      clock.pause(); stopLoops(); silence(); ac?.suspend().catch(() => {});
    } else if (mode === 'play' || mode === 'outro') {
      const restart = () => {
        if (stopped() || dead) return;
        clock.resume();
        scheduled = score.findIndex(n => n.time + roundStart >= clock.time() - .02);
        if (scheduled < 0) scheduled = score.length;
        stopLoops(); schedule(); timer = setInterval(schedule, 25); frame = requestAnimationFrame(draw);
      };
      if (ac) ac.resume().then(restart).catch(restart); else restart();
    }
  }
  return {
    start() { if (!stage && !dead) mount(); },
    pause() { paused = true; syncPause(); },
    resume() { paused = false; syncPause(); },
    suspend(on) { suspended = Boolean(on); syncPause(); },
    setAudioAudible(on) {
      ctx.audioAudible = Boolean(on);
      if (master && ac) master.gain.setTargetAtTime(on ? .7 : 0, ac.currentTime, .015);
    },
    destroy() {
      if (dead) return; dead = true; stopLoops(); silence(); listeners.splice(0).forEach(fn => fn());
      ac?.close().catch(() => {}); stage?.remove();
    },
  };
}
