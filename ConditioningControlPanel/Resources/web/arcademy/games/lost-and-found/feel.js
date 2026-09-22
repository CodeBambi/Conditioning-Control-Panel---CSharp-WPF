import { deal, references, peel } from './feel-model.js';

const PALETTES = [['#f6bbbc', '#6b2e50'], ['#b9dad2', '#245855'], ['#f5dca1', '#71502b']];
const NAMES = ['moon', 'flower', 'eye', 'planet', 'key', 'leaf', 'shell', 'star', 'door'];
const INKS = ['rose', 'mint', 'gold'];
const DRAWINGS = [
  '<path d="M66 19a32 32 0 1 0 15 54A34 34 0 0 1 66 19Z"/>',
  '<path d="M50 37C20 3 5 50 35 50C3 78 48 95 50 64C75 96 96 52 65 50C95 24 52 3 50 37Z"/><circle cx="50" cy="50" r="10"/>',
  '<path d="M12 50Q50 5 88 50Q50 95 12 50Z"/><circle cx="50" cy="50" r="14"/><circle cx="54" cy="46" r="3"/>',
  '<circle cx="50" cy="50" r="25"/><ellipse cx="50" cy="50" rx="43" ry="12" transform="rotate(-28 50 50)"/><path d="M18 16v10m-5-5h10"/>',
  '<circle cx="36" cy="36" r="17"/><path d="m48 48 32 32m-6-6 9-9m-20 9 9-9"/><circle cx="33" cy="33" r="4"/>',
  '<path d="M22 79Q6 24 79 18Q92 86 22 79Zm0 0 48-49M40 60V40m0 20h23"/>',
  '<path d="M24 76C-3 36 44 1 76 24C104 49 73 88 47 69C25 53 49 30 64 43C76 55 60 65 55 54M24 76l40 7"/>',
  '<path d="m50 10 12 26 29 4-21 21 5 29-25-14-25 14 5-29L9 40l29-4Z"/>',
  '<path d="M23 85V41a27 27 0 0 1 54 0v44Zm13 0V43a14 14 0 0 1 28 0v42"/><circle cx="57" cy="61" r="2"/>',
];

function picture(id) {
  const [paper, ink] = PALETTES[Math.floor(id / 9)];
  return `<svg viewBox="0 0 100 100" aria-hidden="true" style="background:${paper};color:${ink}"><g fill="none" stroke="currentColor" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round">${DRAWINGS[id % 9]}</g></svg>`;
}

const STYLE = `
.lf-feel{--cream:#f9ecda;color:var(--cream);font:16px/1.35 system-ui;max-width:590px;margin:auto;padding:12px;isolation:isolate}
.lf-feel *{box-sizing:border-box}.lf-feel button{font:inherit;color:inherit;cursor:pointer;touch-action:manipulation;-webkit-tap-highlight-color:transparent}
.lf-feel button:focus-visible{outline:3px solid #fff;outline-offset:3px}.lf-feel h2{font:600 26px Georgia;margin:0}.lf-top{display:flex;align-items:center;justify-content:space-between;gap:12px}
.lf-count{font-variant-numeric:tabular-nums;color:#f6d8b8;font-size:14px}.lf-message{min-height:26px;margin:7px 0 10px;color:#decad5;font-size:14px}
.lf-refs{display:flex;gap:12px;justify-content:center;margin-bottom:15px;min-height:75px}.lf-ref{width:75px;height:75px;padding:5px;background:#fff3df;border-radius:9px;box-shadow:0 4px 0 #0003;position:relative}
.lf-ref svg{width:100%;height:100%;border-radius:5px}.lf-ref::after{content:'FIND';position:absolute;bottom:-7px;left:20%;right:20%;text-align:center;font-size:9px;letter-spacing:1px;background:#403440;border-radius:4px;padding:2px}
.lf-wall{position:relative;border-radius:20px;background:#241e34;padding:12px;overflow:hidden;box-shadow:inset 0 0 0 1px #d5b6c329,0 14px 40px #0003}
.lf-final{position:absolute;inset:0;display:grid;place-items:center;background:radial-gradient(ellipse at 50% 75%,#536068,#241e34 72%)}
.lf-final svg{width:90%;height:90%}.lf-grid{position:relative;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
.lf-seat{position:relative;aspect-ratio:1.16;border:0;background:none;padding:0;min-width:48px;min-height:48px}
.lf-face{position:absolute;inset:0;border:6px solid #fbefda;border-bottom-width:13px;border-radius:6px;box-shadow:0 4px 0 #bda995,0 8px 0 #8b7788,0 12px 15px #0006;transform:rotate(var(--angle));transition:transform 150ms cubic-bezier(.2,1.6,.4,1),box-shadow 150ms;pointer-events:none;overflow:hidden}
.lf-face svg{width:100%;height:100%;display:block}.lf-seat:active .lf-face{transform:translateY(-5px) rotate(0deg) scale(1.025);box-shadow:0 12px 18px #0008}
.lf-seat[data-depth="1"] .lf-face{box-shadow:0 4px 10px #0006}.lf-seat[data-depth="2"] .lf-face{box-shadow:0 4px 0 #bda995,0 8px 12px #0006}
.lf-seat:disabled{cursor:default}.lf-seat:disabled .lf-face{display:none}.lf-ghost{position:fixed;pointer-events:none;z-index:1000;border:5px solid #fbefda;border-bottom-width:12px;border-radius:6px;overflow:hidden;box-shadow:0 8px 24px #0006}.lf-ghost svg{width:100%;height:100%}
.lf-speck{position:absolute;width:5px;height:9px;border-radius:1px;background:#f6d8b8;pointer-events:none;z-index:4}.lf-progress{height:4px;background:#ffffff12;border-radius:4px;margin-top:18px;overflow:hidden}.lf-progress i{display:block;height:100%;background:linear-gradient(90deg,#c593c0,#efd1a4);transition:width 220ms;width:0}
.lf-feel[data-paused="true"] .lf-wall{filter:brightness(.4)}.lf-feel[data-paused="true"] .lf-seat{pointer-events:none}.lf-feel[data-reduced="true"] *{transition:none!important}.lf-feel[data-reduced="true"] .lf-seat:active .lf-face{transform:none}
@media(max-height:700px){.lf-feel{padding:8px}.lf-refs{min-height:57px;margin-bottom:12px;gap:10px}.lf-ref{width:57px;height:57px}.lf-wall{padding:10px}.lf-grid{gap:10px}.lf-seat{aspect-ratio:1.35}.lf-feel h2{font-size:22px}.lf-message{margin:5px 0}.lf-progress{margin-top:12px}}
`;

export function create(ctx) {
  const t = (key, fallback) => ctx.lexicon?.(key, fallback) ?? fallback;
  const reduced = ctx.motion?.motionLevel === 0 || ctx.motion?.reducedMotion;
  let state, stage, message, grid, refs, meter, count, audio, master;
  let paused = false, dead = false, ended = false, started = false;
  const animations = new Set(), floating = new Set(), voices = new Set();
  const abort = new AbortController();
  function sound(success) {
    if (ctx.audioAudible === false || paused || dead) return;
    try {
      if (!audio) {
        audio = new (window.AudioContext || window.webkitAudioContext)();
        master = audio.createGain(); master.gain.value = .13; master.connect(audio.destination);
      }
      void audio.resume();
      const notes = success ? [261.63, 293.66, 329.63, 392, 440] : [146.83];
      const tone = notes[state.found % notes.length];
      if (voices.size > 8) return;
      const osc = audio.createOscillator(), gain = audio.createGain(), now = audio.currentTime;
      osc.type = 'triangle'; osc.frequency.setValueAtTime(tone, now);
      gain.gain.setValueAtTime(0, now); gain.gain.linearRampToValueAtTime(.5, now + .008);
      gain.gain.exponentialRampToValueAtTime(.001, now + .22);
      osc.connect(gain); gain.connect(master); osc.start(now); osc.stop(now + .25); voices.add(osc);
      osc.onended = () => { voices.delete(osc); osc.disconnect(); gain.disconnect(); };
    } catch { /* Visual feedback remains complete if audio is unavailable. */ }
  }
  function animate(node, frames, duration, remove = false) {
    if (reduced || !node.animate) { if (remove) node.remove(); return; }
    const animation = node.animate(frames, { duration, easing: 'cubic-bezier(.2,.8,.25,1)', fill: 'forwards' });
    animations.add(animation);
    animation.finished.catch(() => {}).finally(() => {
      animations.delete(animation);
      if (remove) { node.remove(); floating.delete(node); }
    });
  }
  function flakes(seat, finale = false) {
    if (reduced) return;
    const wall = stage.querySelector('.lf-wall'), origin = seat.getBoundingClientRect(), bounds = wall.getBoundingClientRect();
    for (let i = 0; i < (finale ? 32 : 10) && floating.size < 70; i++) {
      const fleck = document.createElement('i'); fleck.className = 'lf-speck';
      fleck.style.left = `${origin.x - bounds.x + origin.width / 2}px`;
      fleck.style.top = `${origin.y - bounds.y + origin.height / 2}px`;
      fleck.style.background = PALETTES[i % 3][0]; wall.append(fleck); floating.add(fleck);
      const angle = i * 2.399, distance = finale ? 100 : 35;
      animate(fleck, [{ transform: 'translate(0,0) rotate(0)', opacity: 1 }, { transform: `translate(${Math.cos(angle) * distance}px,${Math.sin(angle) * distance + 35}px) rotate(${i * 43}deg)`, opacity: 0 }], 420 + i * 9, true);
    }
  }
  function draw() {
    refs.replaceChildren();
    for (const id of state.targets) {
      const ref = document.createElement('div'); ref.className = 'lf-ref'; ref.innerHTML = picture(id);
      ref.setAttribute('aria-label', `${INKS[Math.floor(id / 9)]} ${NAMES[id % 9]}`); refs.append(ref);
    }
    [...grid.children].forEach((seat, i) => {
      const pile = state.piles[i], id = pile.at(-1); seat.dataset.depth = pile.length;
      seat.disabled = id === undefined; seat.replaceChildren();
      if (id === undefined) { seat.setAttribute('aria-label', t('lf2_clear', 'Cleared')); return; }
      seat.setAttribute('aria-label', `${INKS[Math.floor(id / 9)]} ${NAMES[id % 9]}`);
      const face = document.createElement('span'); face.className = 'lf-face'; face.innerHTML = picture(id);
      face.style.setProperty('--angle', `${(id % 5 - 2) * 1.3}deg`); seat.append(face);
    });
    count.textContent = `${state.found} / 27`;
    meter.style.width = `${state.found / 27 * 100}%`;
  }
  function choose(index) {
    if (paused || dead || ended) return;
    const seat = grid.children[index], id = state.piles[index].at(-1);
    if (id === undefined) return;
    const targetIndex = state.targets.indexOf(id), from = seat.getBoundingClientRect();
    const to = refs.children[targetIndex]?.getBoundingClientRect();
    if (!peel(state, index)) {
      sound(false); message.textContent = t('lf2_miss', 'Find one of the three pictures above.');
      animate(seat.firstElementChild, [{ transform: 'translateX(-3px)' }, { transform: 'translateX(3px)' }, { transform: 'translateX(0)' }], 160);
      return;
    }
    sound(true); flakes(seat, state.found === 27);
    if (!reduced && to) {
      const ghost = document.createElement('div'); ghost.className = 'lf-ghost'; ghost.innerHTML = picture(id);
      Object.assign(ghost.style, { left: `${from.x}px`, top: `${from.y}px`, width: `${from.width}px`, height: `${from.height}px` });
      stage.append(ghost); floating.add(ghost);
      animate(ghost, [{ transform: 'perspective(500px) translateY(-4px) rotateX(0deg)', opacity: 1 }, { transform: 'perspective(500px) translateY(-20px) rotateX(32deg) rotate(-8deg)', opacity: 1, offset: .35 }, { transform: `translate(${to.x - from.x}px,${to.y - from.y}px) rotate(8deg) scale(.45)`, opacity: 0 }], 360, true);
    }
    draw();
    message.textContent = state.found === 27 ? t('lf2_done', 'You found the quiet underneath.') : t('lf2_next', 'A little more of the room. Keep peeling.');
    if (state.found === 27) {
      ended = true;
      animate(stage.querySelector('.lf-final'), [{ filter: 'brightness(1)' }, { filter: 'brightness(1.5)' }, { filter: 'brightness(1)' }], 1100);
      ctx.endClass({ metrics: { composite: Math.max(.5, 1 - state.misses / 54) }, hardGates: {}, flavorXp: 0, feelVersion: 2 });
    }
  }
  const instance = {
    start(spec = {}) {
      if (started || dead) return;
      started = true; state = deal(spec.seed || 'paper-room'); references(state);
      stage = document.createElement('section'); stage.className = 'lf-feel'; stage.dataset.reduced = !!reduced;
      stage.innerHTML = `<style>${STYLE}</style><div class="lf-top"><h2></h2><span class="lf-count"></span></div><p class="lf-message" aria-live="polite"></p><div class="lf-refs" role="group" aria-label="Find these pictures"></div><div class="lf-wall"><div class="lf-final"><svg viewBox="0 0 360 320" aria-label="A moonlit garden"><circle cx="244" cy="65" r="35" fill="#f5dca1"/><path d="M0 280Q90 110 180 255Q280 150 360 265V320H0" fill="#799b90"/><path d="M0 302Q120 205 240 290Q300 230 360 292V320H0" fill="#bbccd0"/><g stroke="#f6bbbc" stroke-width="5" fill="none"><path d="M72 280V165m0 48-30-30m30 12 25-27M290 290V200m0 37-23-15m23-3 20-24"/></g><g fill="#f6bbbc"><circle cx="72" cy="156" r="15"/><circle cx="39" cy="176" r="10"/><circle cx="100" cy="162" r="11"/><circle cx="290" cy="194" r="12"/></g></svg></div><div class="lf-grid"></div></div><div class="lf-progress"><i></i></div>`;
      stage.querySelector('h2').textContent = t('lf2_title', 'Peel the room open');
      message = stage.querySelector('.lf-message'); message.textContent = t('lf2_intro', 'Find any picture above. Tap it to peel.');
      grid = stage.querySelector('.lf-grid'); refs = stage.querySelector('.lf-refs'); meter = stage.querySelector('.lf-progress i'); count = stage.querySelector('.lf-count');
      for (let i = 0; i < 9; i++) {
        const button = document.createElement('button'); button.className = 'lf-seat'; button.type = 'button';
        button.addEventListener('click', () => choose(i), { signal: abort.signal }); grid.append(button);
      }
      ctx.root.replaceChildren(stage); draw();
    },
    pause() {
      if (paused || dead) return;
      paused = true; if (stage) { stage.dataset.paused = 'true'; grid.inert = true; }
      animations.forEach(a => a.pause()); if (audio) void audio.suspend();
    },
    resume() {
      if (!paused || dead) return;
      paused = false; if (stage) { stage.dataset.paused = 'false'; grid.inert = false; }
      animations.forEach(a => a.play()); if (audio) void audio.resume();
    },
    suspend(on) { if (on) instance.pause(); else instance.resume(); },
    setAudioAudible(on) { ctx.audioAudible = !!on; if (master) master.gain.value = on ? .13 : 0; },
    destroy() {
      dead = true; abort.abort(); animations.forEach(a => a.cancel()); animations.clear();
      floating.forEach(n => n.remove()); floating.clear(); voices.forEach(v => { try { v.stop(); } catch {} });
      if (audio) void audio.close(); stage?.remove();
    },
  };
  return instance;
}
