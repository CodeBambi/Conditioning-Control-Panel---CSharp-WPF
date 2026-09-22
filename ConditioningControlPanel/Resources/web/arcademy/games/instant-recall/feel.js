import { makeRound, picture, PICTURES, report } from './feel-model.js';
import { css } from './feel-style.js';

// Preview and future host use this same game. No progression is read or written.
export function create(ctx) {
  const root = ctx.root;
  const doc = root.ownerDocument;
  const win = doc.defaultView;
  const tr = (key, fallback) => typeof ctx.lexicon === 'function' ? ctx.lexicon(key, fallback) : fallback;
  let dead = false, started = false, paused = false, suspended = false, sent = false;
  let hidden = doc.hidden, frame = 0, last = 0, clock = 0, tasks = [];
  let phase = 'idle', number = 0, correct = 0, round, rng = Math.random;
  let audio, master;
  const voices = new Set(), animations = new Set();
  const host = doc.createElement('section');
  host.className = 'ir-feel';
  const style = doc.createElement('style');
  style.textContent = css;
  host.innerHTML = `<div class="ir-top"><span>Instant Recall</span><span class="ir-step"></span></div>
    <h2 class="ir-title"></h2><p class="ir-instruction" aria-live="polite"></p>
    <div class="ir-stage"></div><div class="ir-feedback"></div>
    <div class="ir-mosaic" aria-label="Restored pictures">${'<div class="ir-memory">·</div>'.repeat(6)}</div>
    <div class="ir-mosaic-label">A little more of the room, restored.</div>`;
  root.append(style, host);
  const at = selector => host.querySelector(selector);
  const stage = at('.ir-stage'), feedback = at('.ir-feedback');
  const isPaused = () => paused || suspended || hidden;
  const muted = () => (typeof ctx.audioAudible === 'function' ? !ctx.audioAudible() : ctx.audioAudible === false);
  const reduced = () => {
    const value = typeof ctx.motion?.motionLevel === 'function' ? ctx.motion.motionLevel() : ctx.motion?.motionLevel;
    return value === 0 || value === 'off' || value === 'reduced' || win.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  };
  function animate(node, keys, options) {
    if (reduced() || !node.animate) return;
    const animation = node.animate(keys, options);
    animations.add(animation);
    animation.finished.then(() => animations.delete(animation), () => animations.delete(animation));
  }
  function after(ms, fn) { tasks.push({ at: clock + ms, fn }); }
  function stopVoices() {
    for (const voice of voices) { try { voice.stop(); } catch {} }
    voices.clear();
  }
  function unlockSound() {
    try {
      const Audio = win.AudioContext || win.webkitAudioContext;
      if (!audio && Audio) {
        audio = new Audio(); master = audio.createGain();
        master.gain.value = muted() ? 0 : .13; master.connect(audio.destination);
      }
      if (audio && !isPaused()) audio.resume().catch(() => {});
    } catch { /* Silent play preserves every rule. */ }
  }
  function tone(notes, soft = false) {
    if (!audio || muted() || isPaused() || voices.size >= 8) return;
    notes.forEach((frequency, i) => {
      const start = audio.currentTime + i * .105;
      const osc = audio.createOscillator(), gain = audio.createGain();
      osc.type = 'sine'; osc.frequency.value = frequency;
      gain.gain.setValueAtTime(.001, start);
      gain.gain.exponentialRampToValueAtTime(soft ? .25 : .55, start + .007);
      gain.gain.exponentialRampToValueAtTime(.001, start + .23);
      osc.connect(gain); gain.connect(master); voices.add(osc);
      osc.onended = () => { voices.delete(osc); osc.disconnect(); gain.disconnect(); };
      osc.start(start); osc.stop(start + .25);
    });
  }
  function burst(button, amount = 16) {
    if (reduced()) return;
    const layer = doc.createElement('div'); layer.className = 'ir-burst';
    const box = button.getBoundingClientRect(), base = host.getBoundingClientRect();
    for (let i = 0; i < amount; i++) {
      const spark = doc.createElement('i'); spark.className = 'ir-spark';
      const angle = (i / amount) * Math.PI * 2, distance = 35 + (i % 4) * 12;
      spark.style.left = `${box.left - base.left + box.width / 2}px`;
      spark.style.top = `${box.top - base.top + box.height / 2}px`;
      layer.append(spark);
      animate(spark, [{ transform: 'translate(0,0) scale(1)', opacity: 1 },
        { transform: `translate(${Math.cos(angle) * distance}px,${Math.sin(angle) * distance}px) rotate(${i * 47}deg) scale(.2)`, opacity: 0 }],
      { duration: 380 + i % 3 * 65, easing: 'cubic-bezier(.15,.6,.35,1)', fill: 'forwards' });
    }
    host.append(layer); after(560, () => layer.remove());
  }
  function title(text, instruction) {
    at('.ir-title').textContent = text;
    at('.ir-instruction').textContent = instruction;
    at('.ir-step').textContent = `${number + 1} / 6`;
  }
  function showOriginal(index) {
    stage.innerHTML = `<div class="ir-watch">${picture(round.original[index])}</div>
      <div class="ir-count" aria-label="Picture ${index + 1} of 3">${[0, 1, 2].map(i => i <= index ? '●' : '○').join('')}</div>`;
    animate(at('.ir-watch'), [{ opacity: 0, transform: 'translateY(9px) scale(.96)' }, { opacity: 1, transform: 'none' }], { duration: 220, easing: 'cubic-bezier(.15,.75,.3,1)' });
    tone([[261.63, 329.63, 392][index]], true);
  }
  function beginRound() {
    phase = 'watch'; feedback.replaceChildren();
    round = makeRound(rng, number);
    title(tr('feel_recall_watch', 'Keep these three.'), tr('feel_recall_watch_hint', 'One will change. Remember the pictures.'));
    showOriginal(0);
    after(1350, () => showOriginal(1));
    after(2700, () => showOriginal(2));
    after(4050, () => {
      stage.replaceChildren();
      title(tr('feel_recall_turn', 'The room is changing.'), tr('feel_recall_turn_hint', 'Same places. One different picture.'));
    });
    after(4530, showReplay);
  }
  function showReplay() {
    phase = 'answer';
    title(tr('feel_recall_answer', 'Catch the lie.'), tr('feel_recall_answer_hint', 'Tap the picture that changed. Take your time.'));
    stage.innerHTML = `<div class="ir-choices">${round.replay.map((id, i) => `<button class="ir-choice" data-answer="${i}" aria-label="${i + 1}: ${PICTURES[id][0]}"><span class="ir-face">${picture(id)}</span></button>`).join('')}</div>`;
    animate(at('.ir-choices'), [{ opacity: 0 }, { opacity: 1 }], { duration: 180 });
  }
  function answer(index) {
    if (phase !== 'answer' || isPaused()) return;
    phase = 'reveal';
    const hit = index === round.changed;
    if (hit) correct++;
    const buttons = [...host.querySelectorAll('.ir-choice')];
    buttons.forEach(button => { button.disabled = true; });
    const target = buttons[round.changed], original = round.original[round.changed], falseId = round.replay[round.changed];
    title(hit ? tr('feel_recall_caught', 'You caught it.') : tr('feel_recall_truth', 'Here is the switch.'),
      hit ? tr('feel_recall_caught_hint', 'That is how it really was.') : tr('feel_recall_truth_hint', 'No rush. You keep the repair.'));
    if (hit) {
      for (const side of [-1, 1]) {
        const shard = doc.createElement('span'); shard.className = 'ir-face ir-shard';
        shard.innerHTML = picture(falseId);
        shard.style.clipPath = side < 0 ? 'polygon(0 0,57% 0,43% 35%,58% 63%,42% 100%,0 100%)' : 'polygon(57% 0,100% 0,100% 100%,42% 100%,58% 63%,43% 35%)';
        target.append(shard);
        animate(shard, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: `translate(${side * 30}px,8px) rotate(${side * 12}deg)` }], { duration: 300, fill: 'forwards' });
        after(310, () => shard.remove());
      }
      burst(target); tone([392, 523.25]);
    } else tone([293.66], true);
    after(reduced() ? 0 : 180, () => {
      target.querySelector('.ir-face').innerHTML = picture(original);
      target.classList.add('ir-restored');
      target.insertAdjacentHTML('beforeend', '<span class="ir-mark" aria-hidden="true">✓</span>');
      buttons.forEach((button, i) => { if (i !== round.changed) button.classList.add('ir-other'); });
      animate(target.querySelector('.ir-face'), [{ transform: 'scale(.94)' }, { transform: 'scale(1.04)' }, { transform: 'scale(1)' }], { duration: 320 });
    });
    feedback.innerHTML = `<div class="ir-evidence"><figure>${picture(original, false)}<figcaption>Was ${PICTURES[original][0]}</figcaption></figure><span class="ir-arrow" aria-hidden="true">→</span><figure>${picture(falseId, false)}<figcaption>Became ${PICTURES[falseId][0]}</figcaption></figure></div>`;
    after(550, () => {
      const memory = host.querySelectorAll('.ir-memory')[number];
      memory.innerHTML = picture(original, false); memory.classList.add('ir-filled');
      animate(memory, [{ transform: 'scale(.8)', opacity: .3 }, { transform: 'scale(1.12)', opacity: 1 }, { transform: 'scale(1)' }], { duration: 360 });
      if (number === 5) { after(1000, finish); return; }
      const next = doc.createElement('button'); next.className = 'ir-next'; next.dataset.next = '';
      next.textContent = tr('feel_recall_next', 'Next room'); feedback.append(next);
      phase = 'next';
    });
  }
  function finish() {
    if (sent || dead) return;
    phase = 'complete'; host.classList.add('ir-final');
    title(tr('feel_recall_finished', 'Room restored.'), `${correct} of 6 caught. Every picture back where it belongs.`);
    stage.innerHTML = '<div class="ir-seal" aria-hidden="true">✧</div>';
    feedback.innerHTML = '<p>Nothing missing. Nicely done.</p>';
    at('.ir-mosaic-label').textContent = tr('feel_recall_mosaic', 'Your restored room.');
    tone([261.63, 329.63, 392, 523.25]);
    const sweep = doc.createElement('div'); sweep.className = 'ir-finale';
    if (!reduced()) {
      host.append(sweep);
      animate(sweep, [{ transform: 'translateX(-100%)' }, { transform: 'translateX(100%)' }], { duration: 1000 });
    }
    after(1100, () => { sweep.remove(); if (!sent) { sent = true; ctx.endClass?.(report(correct)); } });
  }
  function tick(now) {
    frame = 0;
    if (dead || isPaused() || !started) return;
    const elapsed = last ? Math.max(0, now - last) : 0; last = now;
    // A stalled foreground frame cannot fast-forward the three memory pictures.
    clock += Math.min(elapsed, 64);
    const due = tasks.filter(task => task.at <= clock); tasks = tasks.filter(task => task.at > clock);
    for (const task of due) { if (!dead) task.fn(); }
    host.classList.toggle('ir-reduced', !!reduced());
    if (master) master.gain.value = muted() ? 0 : .13;
    if (!dead && !sent) frame = win.requestAnimationFrame(tick);
  }
  function syncPause() {
    if (dead) return;
    host.classList.toggle('ir-paused', isPaused());
    host.inert = isPaused();
    last = 0;
    if (isPaused()) {
      win.cancelAnimationFrame(frame); frame = 0;
      for (const animation of animations) animation.pause();
      audio?.suspend().catch(() => {});
    } else {
      for (const animation of animations) animation.play();
      audio?.resume().catch(() => {});
      if (started && !frame) frame = win.requestAnimationFrame(tick);
    }
  }
  function visibility() { hidden = doc.hidden; syncPause(); }
  function click(event) {
    if (dead || isPaused()) return;
    const button = event.target.closest('button');
    if (!button || !host.contains(button)) return;
    unlockSound();
    if (button.dataset.answer !== undefined) answer(Number(button.dataset.answer));
    else if (button.dataset.next !== undefined && phase === 'next') { number++; beginRound(); }
  }
  host.addEventListener('click', click);
  doc.addEventListener('visibilitychange', visibility);
  return {
    start(spec = {}) {
      if (dead || started) return;
      started = true; rng = typeof spec.random === 'function' ? spec.random : Math.random;
      unlockSound(); beginRound(); syncPause();
    },
    pause() { paused = true; syncPause(); },
    resume() { paused = false; syncPause(); },
    suspend(on) { suspended = !!on; syncPause(); },
    setAudioAudible(on) {
      if (dead) return;
      ctx.audioAudible = !!on;
      if (master) master.gain.value = on ? .13 : 0;
      if (on) unlockSound();
    },
    destroy() {
      if (dead) return;
      dead = true; tasks = []; win.cancelAnimationFrame(frame);
      for (const animation of animations) animation.cancel(); animations.clear(); stopVoices();
      audio?.close().catch(() => {});
      host.removeEventListener('click', click); doc.removeEventListener('visibilitychange', visibility);
      host.remove(); style.remove();
    },
  };
}
