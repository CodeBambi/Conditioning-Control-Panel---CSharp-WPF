// The on-ramp: shown once per session after the first media lands. Four cards, Surprise me, skip.
import { ICONS } from './hud.js';

const CARDS = [
  { id: 'grow', name: 'Grow', line: 'One becomes two, then eight. The pink creeps in from the corner and snaps clean at the loop.',
    mini: `<div class="f grid" style="grid-template-columns:1fr"><i class="g1"></i></div><div class="f grid" style="grid-template-columns:1fr 1fr"><i class="g1"></i><i class="g2"></i></div><div class="f grid" style="grid-template-columns:1fr 1fr;grid-template-rows:1fr 1fr"><i class="g1" style="grid-row:span 2"></i><i class="g2"></i><i class="g3"></i></div><div class="f grid" style="grid-template-columns:1fr 1fr;grid-template-rows:1fr 1fr"><i class="g1"></i><i class="g2"></i><i class="g3"></i><i class="g4"></i></div>` },
  { id: 'haunt', name: 'Haunt', line: 'Halfway through, a second gif bleeds in over the first. Colour takes the frame, then it snaps clean.',
    mini: `<div class="f g2"></div><div class="f g2"><i style="position:absolute;inset:0;background:linear-gradient(20deg,#C46A9E,#2A2A55);opacity:.28"></i></div><div class="f g2"><i style="position:absolute;inset:0;background:linear-gradient(20deg,#C46A9E,#2A2A55);opacity:.5"></i></div><div class="f g1"><i style="position:absolute;inset:0;background:rgba(255,105,180,.5)"></i></div>` },
  { id: 'flashdeck', name: 'Flash Deck', line: 'Your whole library, one second each, a word between every cut. Show off the collection.',
    mini: `<div class="f g1"></div><div class="f word">drop</div><div class="f g2"></div><div class="f g4"></div><div class="f word">obey</div><div class="f g3"></div>` },
  { id: 'didyouseeit', name: 'Did You See It', line: 'Calm on the surface. One hidden frame. They will rewatch it to catch the word.',
    mini: `<div class="f g2"></div><div class="f g2"></div><div class="f g2"></div><div class="f word" style="flex:0 0 10px;font-size:0">.</div><div class="f g2"></div><div class="f g2"></div>` },
];

export function initVibes(ctx) {
  const p = ctx.project;
  const host = document.getElementById('vibes');
  let lastFocus = null;

  function show() {
    const n = p.media.length;
    host.innerHTML = `
      <div class="vibes-title"><h1>${n} ${n === 1 ? 'gif' : 'gifs'} in. Pick a vibe.</h1><p>Each one fills the timeline for you. Tap anything after to tweak it.</p></div>
      <div class="vibe-cards" role="list"></div>
      <div class="vibes-actions">
        <button type="button" class="btn-outline" data-act="surprise">${ICONS.dice.replace('width="16" height="16"', 'width="20" height="20"')}<span>Surprise me</span></button>
        <button type="button" class="btn-quiet" data-act="skip">Skip, start from scratch</button>
      </div>
      <div class="vibes-foot"></div>`;
    const cards = host.querySelector('.vibe-cards');
    CARDS.forEach((c, i) => {
      const b = document.createElement('button'); b.type = 'button'; b.className = 'vcard' + (i === 0 ? ' lead' : ''); b.setAttribute('role', 'listitem'); b.dataset.vibe = c.id;
      b.innerHTML = `<div class="mini" aria-hidden="true">${c.mini}</div><div class="vcard-text"><div class="vcard-name">${c.name}</div><div class="vcard-line">${c.line}</div></div>`;
      b.addEventListener('click', () => pick(c));
      cards.appendChild(b);
    });
    const foot = host.querySelector('.vibes-foot');
    for (const m of p.media) { const t = document.createElement('div'); t.className = 'thumb'; if (m.thumb) { const cv = document.createElement('canvas'); cv.width = m.thumb.width; cv.height = m.thumb.height; cv.getContext('2d').drawImage(m.thumb, 0, 0); t.appendChild(cv); } foot.appendChild(t); }
    if (n < 8) { const add = document.createElement('button'); add.type = 'button'; add.className = 'thumb add'; add.innerHTML = ICONS.add.replace('<svg', '<svg width="18" height="18"'); add.setAttribute('aria-label', 'Add more'); add.addEventListener('click', () => ctx.pickFiles()); foot.appendChild(add); }
    const note = document.createElement('span'); note.style.marginLeft = '8px'; note.textContent = `DROP MORE ANYWHERE · 8 MAX`; foot.appendChild(note);
    host.querySelector('[data-act="surprise"]').addEventListener('click', () => { p.surprise(); ctx.commit('surprise'); hide(); ctx.toast(`Rolled ${p.code}. Same code, same rolls.`); });
    host.querySelector('[data-act="skip"]').addEventListener('click', () => { hide(); });
    lastFocus = document.activeElement; host.hidden = false; host.querySelector('.vcard')?.focus();
    ctx.state.screen = 'vibes'; document.getElementById('app').dataset.screen = 'vibes';
  }
  function pick(c) { p.applyVibe(c.id); ctx.commit(`vibe ${c.id}`); hide(); ctx.toast(`${c.name} is on. Tap anything to tweak it.`); }
  function hide() {
    if (host.hidden) return;
    host.hidden = true; host.innerHTML = ''; ctx.state.screen = 'editor'; document.getElementById('app').dataset.screen = 'editor';
    ctx.emit('state'); lastFocus?.focus?.();
  }
  function refresh() { if (!host.hidden) { const h = host.querySelector('h1'); const n = p.media.length; if (h) h.textContent = `${n} ${n === 1 ? 'gif' : 'gifs'} in. Pick a vibe.`; } }
  return { show, hide, refresh, isOpen: () => !host.hidden };
}
