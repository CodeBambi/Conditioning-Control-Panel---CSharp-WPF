// The Auto page, the room's front door. Add your gifs, press the button, roll until you like what you
// see, put a word on it (or skip), save. One project and one stage serve both this page and the editor:
// #stage-box moves between the editor's centre and the Auto page (mount / unmount), so the clock never
// stops and a roll never shows an empty canvas. Steps: add, roll, save; caption is opt in, a link on the
// Roll column opens it before you advance, and it only shows in the strip once you have taken it.
import { ICONS } from './hud.js';
import { fmtMB } from './export-sheet.js';
import { codeToSeed } from './engine-bridge.js';
import { FONTS } from './panels.js';
import { mountDeck } from './doors.js';

const STEPS = [['add', 'Add'], ['roll', 'Roll'], ['caption', 'Caption'], ['save', 'Save']];
const ORIENTS = ['landscape', 'portrait', 'square'];
const CAP_MODES = [['text', 'Text'], ['window', 'Window'], ['flash', 'Flash']];
const HOLD_DELAY = 450, HOLD_EVERY = 700, SWIPE = 50, TAP = 8, DRAG = 12, IDLE_MS = 25000;
// the room takes the colour of the roll: the first tint block picks the halo behind the stage
const HALO = { pink: 'rgba(255,105,180,.16)', lavender: 'rgba(184,166,232,.16)', gold: 'rgba(240,194,75,.14)' };
export const haloFor = blocks => HALO[(blocks || []).find(b => b.effect === 'tint')?.params?.colour] || HALO.pink;
const CHEV_L = '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m15 5-7 7 7 7"/></svg>';
const CLOSE = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>';
const CHEV_R = '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 5 7 7-7 7"/></svg>';
const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
const cap = s => s.charAt(0).toUpperCase() + s.slice(1);

export function initAuto(ctx, { exportSheet, pool }) {
  const p = ctx.project;
  const root = document.getElementById('auto');
  const box = document.getElementById('stage-box');
  const centre = document.getElementById('centre');
  let step = 'add', mounted = false, hold = null, ghostTimer = 0, swipe = null, capTouched = false;
  let charge = 0, heldN = 0, pendingReveal = false, revealTimer = 0, idleTimer = 0, idleShown = false;

  root.innerHTML = `
    <header class="auto-top">
      <div class="wordmark">Remix Room</div>
      <nav class="auto-steps" id="auto-steps" aria-label="Steps"></nav>
    </header>
    <section class="auto-add" id="auto-add">
      <div class="add-card">
        <div class="add-title">Load your gifs.<br>Press the button.</div>
        <p class="add-line">The room makes the composition. You roll until you like it.</p>
        <button type="button" class="btn-big primary add-btn" id="auto-pick">${ICONS.add}<span>Add gifs</span></button>
        <p class="add-sub">gifs, pictures, short videos, or a zip of them. As many as you like. Drop them anywhere, or paste.</p>
        <p class="add-routes">Long list? <button type="button" class="quiet-link" id="pick-gifs">only gifs</button><button type="button" class="quiet-link" id="pick-any">browse folders</button></p>
        <p class="add-privacy">nothing leaves your browser</p>
      </div>
      <button type="button" class="quiet-link" id="open-editor-add">Open the editor instead</button>
    </section>
    <section class="auto-work" id="auto-work" hidden>
      <div class="auto-pic">
        <div class="auto-stage" id="auto-stage"></div>
        <div class="pool-strip" id="pool-strip" hidden></div>
      </div>
      <div class="auto-side">
        <div class="side-roll" id="side-roll">
          <div class="roll-row">
            <button type="button" class="roll-arrow" id="roll-back" aria-label="Previous roll">${CHEV_L}</button>
            <button type="button" class="roll-pill" id="btn-roll"><span class="roll-dice">${ICONS.dice}</span><span class="roll-label">Roll again</span></button>
            <button type="button" class="roll-arrow" id="roll-fwd" aria-label="Next roll">${CHEV_R}</button>
          </div>
          <p class="roll-whisper" id="roll-whisper" hidden>still there? hold the button and it keeps rolling.</p>
          <div class="auto-chips">
            <button type="button" class="chip code-chip" id="code-chip" aria-label="Remix code, tap to copy"><span class="code-text" id="code-text">CCP-0000</span></button>
            <button type="button" class="chip" id="auto-orient">Landscape</button>
            <button type="button" class="chip" id="auto-length">5 s</button>
            <span class="chip static" id="auto-size">-- MB</span>
          </div>
          <button type="button" class="quiet-link code-link" id="code-type" aria-expanded="false" aria-controls="code-entry">have a code?</button>
          <div class="code-entry" id="code-entry" hidden>
            <input type="text" class="text-field code-field" id="code-field" maxlength="8" placeholder="CCP-XXXX" autocomplete="off" spellcheck="false" autocapitalize="characters" aria-label="Type a remix code">
            <button type="button" class="btn-big" id="code-load">Load</button>
            <button type="button" class="code-close" id="code-close" aria-label="Close the code field">${CLOSE}</button>
          </div>
          <div class="auto-foot">
            <button type="button" class="quiet-link" id="open-editor">Open the editor</button>
            <button type="button" class="btn-big caption-it" id="caption-it">Caption it</button>
            <button type="button" class="btn-big primary auto-next" id="auto-next">Next</button>
          </div>
          <div class="side-doors" id="side-doors"></div>
        </div>
        <div class="side-caption" id="side-caption" hidden>
          <div class="cap-sheet">
            <div class="sheet-grab" aria-hidden="true"></div>
            <div class="cap-head"><span class="cap-title">Caption</span><span class="cap-sub">a word on it, or skip</span></div>
            <input type="text" class="text-field cap-field" id="cap-text" maxlength="40" placeholder="say something" autocomplete="off" spellcheck="false" aria-label="Caption text">
            <div class="chips fill" id="cap-modes" role="radiogroup" aria-label="Caption mode"></div>
            <div class="chips fonts" id="cap-fonts" role="radiogroup" aria-label="Font"></div>
            <div class="cap-actions">
              <button type="button" class="btn-big" id="cap-skip">Skip</button>
              <button type="button" class="btn-big primary" id="cap-done">Done</button>
            </div>
          </div>
        </div>
      </div>
    </section>`;
  const $ = id => root.querySelector('#' + id);
  const els = { steps: $('auto-steps'), add: $('auto-add'), work: $('auto-work'), stage: $('auto-stage'), sideRoll: $('side-roll'), sideCap: $('side-caption'),
    back: $('roll-back'), fwd: $('roll-fwd'), pill: $('btn-roll'), code: $('code-chip'), codeText: $('code-text'), orient: $('auto-orient'), length: $('auto-length'), size: $('auto-size'), next: $('auto-next'),
    codeLink: $('code-type'), codeEntry: $('code-entry'), codeField: $('code-field'), capText: $('cap-text'), capIt: $('caption-it'), capModes: $('cap-modes'), capFonts: $('cap-fonts'), whisper: $('roll-whisper') };
  // the strip is a filmstrip: a sprocketed frame per step, a link between them that fills as you pass
  for (const [key, label] of STEPS) {
    if (els.steps.children.length) { const l = document.createElement('i'); l.className = 'link'; l.dataset.into = key; l.setAttribute('aria-hidden', 'true'); l.innerHTML = '<b></b>'; els.steps.appendChild(l); }
    const b = document.createElement('button'); b.type = 'button'; b.className = 'auto-step'; b.dataset.step = key;
    b.innerHTML = `<i class="frame" aria-hidden="true"></i><span>${label}</span>`;
    els.steps.appendChild(b);
  }
  // the doors: one at a time under the roll column, the next one on every roll. Nothing opens by itself.
  mountDeck($('side-doors'), ctx);
  // the pool strip under the stage: the gifs in this roll, pins first, and the count that opens the grid
  pool.mountStrip($('pool-strip'));

  // the paused tag: a still frame is never a broken page
  const pausedTag = document.createElement('span'); pausedTag.className = 'stage-paused'; pausedTag.textContent = 'paused'; pausedTag.hidden = true;
  els.stage.appendChild(pausedTag);

  // ---- mount: the stage box lives here or in the editor's centre, never both
  function mount() { if (mounted) return; mounted = true; els.stage.appendChild(box); root.hidden = false; syncHalo(); }
  function unmount() { if (!mounted) return; mounted = false; stopIdle(); centre.insertBefore(box, centre.firstChild); root.hidden = true; }

  // ---- steps
  function go(s) {
    if (s !== 'add' && ctx.isEmpty()) s = 'add';
    if (s === 'roll' && step === 'add') pendingReveal = true; // the first drop is the one that earns the show
    if (step === 'caption' && s !== 'caption') leaveCaption(true);
    if (s === 'save') { exportSheet.open(); step = 'save'; render(); return; }
    step = s;
    if (s === 'caption') enterCaption(); else render();
    if (s === 'roll' && !p.playing) p.play();
  }
  function render() {
    if (!mounted) return;
    if (ctx.isEmpty()) step = 'add';
    else if (step === 'add') { step = 'roll'; pendingReveal = true; } // the drop promotes the page before go() gets here
    if (step === 'save' && !exportSheet.isOpen()) step = 'roll';
    root.dataset.step = step;
    const wasAdd = !els.add.hidden;
    if (!els.add.classList.contains('leaving')) els.add.hidden = step !== 'add';
    els.work.hidden = step === 'add';
    els.sideRoll.hidden = step === 'caption'; els.sideCap.hidden = step !== 'caption';
    if (pendingReveal && step !== 'add') { pendingReveal = false; reveal(wasAdd); }
    pausedTag.hidden = p.playing || step !== 'roll';
    if (step === 'roll') armIdle(); else stopIdle();
    const capOn = step === 'caption' || hasCaption();
    els.steps.querySelector('.auto-step[data-step="caption"]').hidden = !capOn;
    els.steps.querySelector('.link[data-into="caption"]').hidden = !capOn;
    const shown = STEPS.filter(s => s[0] !== 'caption' || capOn).map(s => s[0]);
    const idx = shown.indexOf(step);
    els.steps.querySelectorAll('.link:not([hidden])').forEach((l, i) => l.classList.toggle('filled', i < idx));
    els.steps.querySelectorAll('.auto-step').forEach(b => {
      const i = shown.indexOf(b.dataset.step);
      b.classList.toggle('on', b.dataset.step === step); b.classList.toggle('done', i > -1 && i < idx);
      b.disabled = b.dataset.step !== 'add' && ctx.isEmpty();
      if (b.dataset.step === step) b.setAttribute('aria-current', 'step'); else b.removeAttribute('aria-current');
    });
    if (step !== 'roll') closeCode(false);
    if (step === 'add') return;
    els.back.disabled = !p.canRollBack; els.fwd.disabled = !p.canRollForward;
    if (els.codeText.textContent !== p.code) setCode(p.code);
    els.capIt.textContent = hasCaption() ? 'Edit the caption' : 'Caption it';
    els.orient.textContent = cap(p.orientation); els.length.textContent = `${Math.round(p.frames / p.fps)} s`;
    if (!box.classList.contains('auto-box')) box.classList.add('auto-box');
    if (step === 'caption') renderCaption();
  }

  // ---- the first drop: the composition lands bigger and brighter than it should, settles, throws sparks,
  // and the pill arrives a beat later, so the eye goes picture, then handle. Once per trip through Add.
  function reveal(fromAdd) {
    const fit = document.getElementById('stage-fit');
    if (reduced() || !fit) return;
    if (fromAdd) { els.add.hidden = false; els.add.classList.add('leaving'); setTimeout(() => { els.add.classList.remove('leaving'); els.add.hidden = step !== 'add'; }, 170); }
    root.classList.add('celebrating');
    fit.classList.remove('reveal'); void fit.offsetWidth;
    fit.classList.add('reveal'); els.pill.classList.add('arrive');
    setTimeout(sparks, 320);
    clearTimeout(revealTimer);
    revealTimer = setTimeout(() => { fit.classList.remove('reveal'); els.pill.classList.remove('arrive'); root.classList.remove('celebrating'); }, 720);
  }
  function sparks() {
    for (let i = 0; i < 7; i++) {
      const s = document.createElement('i');
      s.className = i % 2 ? 'spark gold' : 'spark';
      const a = (-90 + (i - 3) * 26) * Math.PI / 180, r = 40 + (i % 3) * 25;
      s.style.setProperty('--dx', Math.round(Math.cos(a) * r) + 'px');
      s.style.setProperty('--dy', Math.round(Math.sin(a) * r) + 'px');
      s.addEventListener('animationend', () => s.remove(), { once: true });
      els.stage.appendChild(s);
    }
  }

  // ---- the room drifts: the halo takes the colour of the roll, and 25 s of nothing brings a whisper
  const syncHalo = () => root.style.setProperty('--halo', haloFor(p.blocks));
  function armIdle() { clearTimeout(idleTimer); if (idleShown) return; idleTimer = setTimeout(showIdle, IDLE_MS); }
  function showIdle() { if (step !== 'roll' || idleShown) return; idleShown = true; root.classList.add('idle'); els.whisper.hidden = false; }
  function stopIdle() { clearTimeout(idleTimer); root.classList.remove('idle'); els.whisper.hidden = true; }
  function onInput() { if (root.classList.contains('idle')) { root.classList.remove('idle'); els.whisper.hidden = true; } if (step === 'roll') armIdle(); }
  root.addEventListener('pointerdown', onInput, true);
  root.addEventListener('keydown', onInput, true);

  // ---- the shiver: a quiet no, no new words
  function shiver(el) {
    if (!el || reduced()) return;
    el.classList.remove('shiver'); void el.offsetWidth; el.classList.add('shiver');
    el.addEventListener('animationend', () => el.classList.remove('shiver'), { once: true });
  }

  // ---- caption: a word on the whole canvas, centre bottom, dragged on the stage. The block is made on
  // the way in with an empty word and dropped on the way out if the word is still empty, so Skip and a
  // Done with nothing typed leave nothing behind. A roll keeps the caption, so it survives the arrows.
  for (const [m, label] of CAP_MODES) { const c = document.createElement('button'); c.type = 'button'; c.className = 'chip'; c.dataset.mode = m; c.textContent = label; c.addEventListener('click', () => setCapMode(m)); els.capModes.appendChild(c); }
  for (const [key, label, cls] of FONTS) { const c = document.createElement('button'); c.type = 'button'; c.className = 'chip ' + cls; c.dataset.font = key; c.textContent = label; c.addEventListener('click', () => setCapFont(key)); els.capFonts.appendChild(c); }
  const capBlock = () => p.blocks.find(b => b.effect === 'caption' && b.target === 'canvas') || null;
  const hasCaption = () => { const b = capBlock(); return !!(b && (b.params.text || '').trim()); };
  function enterCaption() {
    let b = capBlock();
    if (!b) { b = p.blockFor('caption', 'canvas'); p.updateBlock(b.id, { params: { text: p.captionText || '' } }); capTouched = false; }
    ctx.state.selectedBlockId = b.id; // the drag handle on the stage follows the selected block
    els.capText.value = b.params.text || '';
    ctx.emit('state');
    if (!p.playing) p.play();
    if (!ctx.isMobile()) setTimeout(() => { try { els.capText.focus({ preventScroll: true }); } catch { /* fine */ } }, 220);
  }
  function leaveCaption(keep) {
    const b = capBlock();
    if (b && (!keep || !(b.params.text || '').trim())) { p.removeBlock(b.id); p.setCaptionText(''); if (capTouched) ctx.commit('caption'); }
    if (ctx.state.selectedBlockId && (!b || ctx.state.selectedBlockId === b.id)) ctx.state.selectedBlockId = null;
    capTouched = false;
  }
  function renderCaption() {
    const b = capBlock(); if (!b) return;
    els.capModes.querySelectorAll('.chip').forEach(c => { c.classList.toggle('on', c.dataset.mode === b.mode); c.setAttribute('aria-checked', String(c.dataset.mode === b.mode)); });
    els.capFonts.querySelectorAll('.chip').forEach(c => { c.classList.toggle('on', c.dataset.font === (b.params.font || 'display')); c.setAttribute('aria-checked', String(c.dataset.font === (b.params.font || 'display'))); });
    if (document.activeElement !== els.capText && els.capText.value !== (b.params.text || '')) els.capText.value = b.params.text || '';
  }
  function setCapMode(m) {
    const b = capBlock(); if (!b || b.mode === m) return;
    const patch = { mode: m };
    if (m === 'flash') { const s = b.end - b.start <= 1 ? b.start : Math.round(p.frames * .62); patch.start = s; patch.end = s + 1; }
    else if (b.end - b.start <= 1) { patch.start = 0; patch.end = p.frames; }
    p.updateBlock(b.id, patch); capTouched = true; ctx.commit('caption mode');
  }
  function setCapFont(key) { const b = capBlock(); if (!b) return; p.updateBlock(b.id, { params: { font: key } }); capTouched = true; ctx.commit('font'); }
  els.capText.addEventListener('input', () => { const b = capBlock(); if (!b) return; p.updateBlock(b.id, { params: { text: els.capText.value } }); p.setCaptionText(els.capText.value); capTouched = true; });
  els.capText.addEventListener('change', () => { if (capBlock()) ctx.commit('caption text'); });
  els.capText.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); done(); } e.stopPropagation(); });
  function done() { els.capText.blur(); leaveCaption(true); go('save'); }
  $('cap-done').addEventListener('click', done);
  $('cap-skip').addEventListener('click', () => { els.capText.blur(); leaveCaption(false); go('save'); });

  // ---- the roll. The pool picks the gifs and decodes what the canvas is missing (the pill reads
  // Loading meanwhile); the fade, the code flip and the halo land the moment the roll does.
  const landed = () => { setCode(p.code, true); syncHalo(); if (!p.playing) p.play(); };
  function doRoll(seed, opts) {
    if (!pool.size()) return;
    tumble();
    pool.rollSet(seed, Object.assign({}, opts, { before: () => crossfade(), after: landed }));
  }
  function back() { if (!p.canRollBack) return shiver(document.getElementById('stage-fit')); pool.walk(-1, { before: () => crossfade('left'), after: () => { landed(); ctx.emit('roll', { held: false, n: 0, dir: -1 }); } }); }
  function forward() { if (!p.canRollForward) return shiver(document.getElementById('stage-fit')); pool.walk(1, { before: () => crossfade('right'), after: () => { landed(); ctx.emit('roll', { held: false, n: 0, dir: 1 }); } }); }
  ctx.on('busy', on => { els.pill.classList.toggle('busy', on); els.pill.setAttribute('aria-busy', String(on)); syncLabel(); });
  // the old frame stays on a ghost canvas over the new one and fades in 200 ms; the stage settles in from
  // a hair smaller. Reduced motion: neither, the new roll is just there.
  function crossfade(dir) {
    if (reduced() || step === 'add' || root.classList.contains('celebrating')) return; // the first drop has nothing to fade from
    const fit = document.getElementById('stage-fit'), stage = document.getElementById('stage');
    if (!fit || !stage || !stage.width) return;
    let g = fit.querySelector('.roll-ghost');
    if (!g) { g = document.createElement('canvas'); g.className = 'roll-ghost'; fit.insertBefore(g, document.getElementById('stage-overlay')); }
    if (g.width !== stage.width || g.height !== stage.height) { g.width = stage.width; g.height = stage.height; }
    try { g.getContext('2d').drawImage(stage, 0, 0); } catch { /* a tainted frame just skips the fade */ }
    g.classList.remove('fade'); fit.classList.remove('settle', 'from-left', 'from-right'); void g.offsetWidth;
    g.classList.add('fade'); fit.classList.add('settle');
    if (dir) fit.classList.add('from-' + dir); // the new frame enters from the side you walked in from
    clearTimeout(ghostTimer); ghostTimer = setTimeout(() => { g.remove(); fit.classList.remove('settle', 'from-left', 'from-right'); }, 360);
  }
  function tumble() { const d = els.pill.querySelector('.roll-dice'); d.classList.remove('tumble'); void d.offsetWidth; d.classList.add('tumble'); }
  function setCode(code, flip) { els.codeText.textContent = code; if (flip) { els.code.classList.remove('flip'); void els.code.offsetWidth; els.code.classList.add('flip'); } }
  els.code.addEventListener('animationend', e => { if (e.animationName === 'flip') els.code.classList.remove('flip'); }); // the glow blooms with the flip and drains after it

  // press = one roll; hold = a roll every 700 ms until the finger lifts. The ring around the pill fills
  // over those first 450 ms so the hold stops being a hidden feature: let go early and you keep your roll.
  const label = () => els.pill.querySelector('.roll-label');
  const syncLabel = () => { label().textContent = els.pill.classList.contains('rolling') ? 'Rolling' : pool.isBusy() ? 'Loading' : 'Roll again'; };
  const setCharge = v => els.pill.style.setProperty('--charge', v);
  function startCharge() {
    if (reduced()) return;
    const t0 = performance.now();
    const tick = () => { if (!charge) return; const pct = Math.min(100, (performance.now() - t0) / HOLD_DELAY * 100); setCharge(pct.toFixed(1)); if (pct < 100) charge = requestAnimationFrame(tick); };
    charge = requestAnimationFrame(tick);
  }
  function armHold() {
    heldN = 0; setCharge(100);
    els.pill.classList.add('rolling'); syncLabel();
    hold.i = setInterval(heldRoll, HOLD_EVERY);
  }
  function heldRoll() { doRoll(); beat(); ctx.emit('roll', { held: true, n: ++heldN }); }
  function beat() { if (reduced()) return; els.pill.classList.remove('beat'); void els.pill.offsetWidth; els.pill.classList.add('beat'); }
  els.pill.addEventListener('animationend', e => { if (e.animationName === 'rollbeat') els.pill.classList.remove('beat'); });
  els.pill.addEventListener('pointerdown', e => {
    if (e.button) return; e.preventDefault();
    els.pill.classList.add('pressed'); try { els.pill.setPointerCapture(e.pointerId); } catch { /* fine */ }
    doRoll(); ctx.emit('roll', { held: false, n: 0 });
    startCharge();
    hold = { t: setTimeout(armHold, HOLD_DELAY) };
  });
  const release = () => {
    els.pill.classList.remove('pressed', 'beat');
    if (charge) { cancelAnimationFrame(charge); charge = 0; }
    if (hold) { clearTimeout(hold.t); clearInterval(hold.i); hold = null; }
    if (els.pill.classList.contains('rolling')) { els.pill.classList.remove('rolling'); syncLabel(); }
    setTimeout(() => { if (!els.pill.classList.contains('pressed')) setCharge(0); }, 300);
  };
  for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) els.pill.addEventListener(ev, release);
  els.pill.addEventListener('click', e => { if (e.detail === 0) { doRoll(); ctx.emit('roll', { held: false, n: 0 }); } }); // keyboard: Enter or Space
  els.back.addEventListener('click', back); els.fwd.addEventListener('click', forward);

  // the stage: swipe left for the next roll, right for the one before, tap to play or pause
  const fitEl = () => document.getElementById('stage-fit');
  const dropDrag = () => { const f = fitEl(); if (f) { f.classList.remove('dragging'); f.style.transform = ''; } };
  els.stage.addEventListener('pointerdown', e => { if (e.target.closest('.stamp-btn, .caption-handle')) return; swipe = { id: e.pointerId, x: e.clientX, y: e.clientY }; });
  els.stage.addEventListener('pointermove', e => {
    if (!swipe || e.pointerId !== swipe.id || reduced()) return;
    const dx = e.clientX - swipe.x;
    if (!swipe.drag) { if (Math.abs(dx) < DRAG) return; swipe.drag = true; try { els.stage.setPointerCapture(e.pointerId); } catch { /* fine */ } }
    const f = fitEl(); if (!f) return;
    f.classList.add('dragging'); f.style.transform = `translateX(${Math.max(-18, Math.min(18, dx * .15))}px)`; // the picture leans with the finger
  });
  els.stage.addEventListener('pointerup', e => {
    if (!swipe || e.pointerId !== swipe.id) return;
    const dx = e.clientX - swipe.x, dy = e.clientY - swipe.y; swipe = null;
    dropDrag();
    if (Math.abs(dx) > SWIPE && Math.abs(dy) < Math.abs(dx) / 2) { if (dx < 0) forward(); else back(); return; }
    if (Math.hypot(dx, dy) < TAP) { ctx.togglePlay(); stageMark(); }
  });
  els.stage.addEventListener('pointercancel', () => { swipe = null; dropDrag(); });
  // a tap gets a round answer at the centre, and a paused stage wears a tag so a still frame reads as a choice
  function stageMark() {
    const m = document.createElement('div');
    m.className = 'stage-mark'; m.innerHTML = p.playing ? ICONS.play : ICONS.pause;
    m.addEventListener('animationend', () => m.remove(), { once: true });
    if (reduced()) setTimeout(() => m.remove(), 140); // motion off: it blinks on and off, no pop
    els.stage.appendChild(m);
  }
  p.on('play', on => { pausedTag.hidden = on || step !== 'roll'; });

  // chips
  els.code.addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(p.code); ctx.toast(`Copied ${p.code}. Same code, same roll.`); }
    catch { ctx.toast(`${p.code}. Same code, same roll.`); }
  });
  els.orient.addEventListener('click', () => ctx.setOrientation(ORIENTS[(ORIENTS.indexOf(p.orientation) + 1) % ORIENTS.length]));
  // have a code? the field is a quiet link until somebody wants it. Open, it takes the link's place and,
  // on a phone, sits on the soft keyboard the way the caption sheet does.
  function showCode(on, focus = true) {
    els.codeEntry.hidden = !on; els.codeLink.hidden = on;
    els.codeLink.setAttribute('aria-expanded', String(on));
    if (on) { els.codeField.value = ''; if (focus) els.codeField.focus(); }
    else if (focus) { try { els.codeLink.focus({ preventScroll: true }); } catch { /* fine */ } }
  }
  const closeCode = (focus) => { if (!els.codeEntry.hidden) { els.codeField.blur(); showCode(false, focus); } };
  els.codeLink.addEventListener('click', () => showCode(true));
  $('code-close').addEventListener('click', () => closeCode(true));
  const loadCode = () => {
    let seed; try { seed = codeToSeed(els.codeField.value); } catch { shiver(els.codeField); ctx.toast('That is not a remix code.', { gold: true }); return; }
    closeCode(false); doRoll(seed); ctx.toast(`${p.code} loaded. Same code, same roll.`);
  };
  $('code-load').addEventListener('click', loadCode);
  els.codeField.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); loadCode(); } if (e.key === 'Escape') { closeCode(true); } e.stopPropagation(); });
  els.length.addEventListener('click', ctx.cycleLength);
  ctx.on('size', bytes => { els.size.textContent = bytes == null ? '-- MB' : `${fmtMB(bytes)} MB`; els.size.classList.toggle('hot', bytes != null && bytes > 10 * 1048576); });

  // add, next, editor
  els.add.addEventListener('click', e => { if (!e.target.closest('.quiet-link')) ctx.pickFiles(); });
  $('auto-pick').addEventListener('click', e => { e.stopPropagation(); ctx.pickFiles(); });
  $('pick-gifs').addEventListener('click', e => { e.stopPropagation(); ctx.pickFiles('gif'); });
  $('pick-any').addEventListener('click', e => { e.stopPropagation(); ctx.pickFiles('any'); });
  $('open-editor-add').addEventListener('click', () => ctx.setScreen('editor'));
  $('open-editor').addEventListener('click', () => ctx.setScreen('editor'));
  els.next.addEventListener('click', () => go('save'));
  els.capIt.addEventListener('click', () => go('caption'));
  els.steps.addEventListener('click', e => { const b = e.target.closest('.auto-step'); if (!b || b.disabled) return; if (b.dataset.step === 'add') ctx.pickFiles(); else go(b.dataset.step); });

  return { mount, unmount, render, go, roll: doRoll, back, forward, step: () => step, isMounted: () => mounted };
}
