/* ============================================================================
 * panel.js - the gear panel: live sliders + subliminal word picker.
 *
 * Pure DOM, slides in from the right over the HUD. Every control writes the
 * settings store (settings.js) directly; consumers read S live, so changes
 * apply to the next bubble/card/effect with the game still running - handy
 * for tuning by eye. ESC closes it (the scene owns that key).
 * ==========================================================================*/

import { S, updateSetting, resetOptions, allWords, activeWords, setWordOn, addCustomWord, setCustomSpiral, getCustomSpiral,
  rungForKey, rungForFeature, UNLOCK_LADDER, RANK_NAMES } from './settings.js';
import { peekAudioState } from './audioBus.js';

const SLIDERS = [
  { key: 'bubbleDensity', label: 'bubbles', min: 0, max: 1.25, step: 0.05, fmt: (v) => v <= 0 ? 'off' : `${Math.round(v * 100)}%` },
  { key: 'bubbleSize', label: 'bubble size', min: 0.5, max: 2.5, step: 0.05, fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'gifSize', label: 'gif size', min: 0.5, max: 1.6, step: 0.05, fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'gifOpacity', label: 'gif opacity', min: 0.1, max: 1, step: 0.05, fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'flashCount', label: 'flash gifs', min: 1, max: 10, step: 1, fmt: (v) => `${v}` },
  { key: 'hydraGen', label: 'gif hydra generations', min: 0, max: 5, step: 1, fmt: (v) => `${v}` },
  { key: 'spiralOpacity', label: 'spiral opacity', min: 0, max: 1, step: 0.05, fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'pinkOpacity', label: 'pink filter opacity', min: 0, max: 1, step: 0.05, fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'glitch', label: 'glitch intensity', min: 0, max: 1, step: 0.05, fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'glitchSeconds', label: 'glitch timer', min: 1, max: 8, step: 0.5, fmt: (v) => `${v}s` },
  { key: 'spotSeconds', label: 'video spotlight time', min: 10, max: 30, step: 1, fmt: (v) => `${v}s` },
];

// Spark glyph for the lock hints (matches catalog GLYPHS.drops).
const SPARK = '✦';
const lockHint = (r) => r.rankReq != null ? `🔒 ${SPARK}${r.price} · ${RANK_NAMES[r.rankReq]}` : `🔒 ${SPARK}${r.price}`;
const lockTitle = (r) => {
  const rank = r.rankReq != null ? ` and reaching rank ${RANK_NAMES[r.rankReq]}` : '';
  return `Locked — earn it on the Warren's Dials board for ${r.price} sparks${rank}.`;
};
const rungById = (id) => UNLOCK_LADDER.find((x) => x.id === id);

export function createPanel(hud) {
  const panel = document.createElement('div');
  panel.className = 'sf-panel';

  const head = document.createElement('div');
  head.className = 'sf-panel-head';
  const title = document.createElement('h2');
  title.textContent = 'options';
  const closeBtn = document.createElement('button');
  closeBtn.type = 'button';
  closeBtn.className = 'sf-panel-close';
  closeBtn.setAttribute('aria-label', 'close options');
  closeBtn.textContent = '×';
  head.append(title, closeBtn);
  panel.appendChild(head);

  // Progression: which ladder rungs the player has bought. Injected from the meta
  // snapshot via setUnlocks() (authoritative C# state - see settings.js). A dial
  // whose rung isn't here renders locked. Starter dials map to no rung => always
  // open. lockRefreshers/featureRefreshers re-skin every gated control when the
  // set changes (a purchase lands) without rebuilding the panel.
  const unlockedRungs = new Set();
  const lockRefreshers = [];
  const featureRefreshers = [];
  const keyUnlocked = (key) => { const r = rungForKey(key); return !r || unlockedRungs.has(r); };
  const featureUnlocked = (feat) => { const r = rungForFeature(feat); return !r || unlockedRungs.has(r); };

  // Gate a whole section: while locked, hide the wrap and show a padlock stub in
  // its place; reveal the wrap once the owning rung is bought. No-op for ungated
  // features. Call AFTER the wrap is appended to the panel - the stub is inserted
  // right after it. `labelText` names the section on the stub.
  function gateFeature(wrap, feature, labelText) {
    const rungId = rungForFeature(feature);
    if (!rungId) return;
    const r = rungById(rungId);
    const stub = document.createElement('div');
    stub.className = 'sf-lockstub';
    stub.textContent = `🔒 ${labelText} — ${lockHint(r)}`;
    stub.title = lockTitle(r);
    wrap.insertAdjacentElement('afterend', stub);
    const refresh = () => {
      const locked = !unlockedRungs.has(rungId);
      wrap.style.display = locked ? 'none' : '';
      stub.style.display = locked ? '' : 'none';
    };
    refresh();
    featureRefreshers.push(refresh);
  }

  // Reset to default - sits at the top of the option list. Scoped to the sliders
  // below (bubbles/gifs/spiral/glitch/etc.); leaves custom words, custom spiral
  // and the chosen theme untouched. Each slider registers a refresher so the
  // reset snaps every visible control back without reopening the panel.
  const sliderRefreshers = [];
  const resetRow = document.createElement('div');
  resetRow.className = 'sf-reset-row';
  const resetBtn = document.createElement('button');
  resetBtn.type = 'button';
  resetBtn.className = 'sf-chip sf-reset';
  resetBtn.textContent = 'reset to default';
  resetBtn.addEventListener('click', () => {
    resetOptions(SLIDERS.map((s) => s.key));
    sliderRefreshers.forEach((fn) => fn());
  });
  resetRow.appendChild(resetBtn);
  panel.appendChild(resetRow);

  for (const def of SLIDERS) {
    const row = document.createElement('div');
    row.className = 'sf-row';
    const lab = document.createElement('div');
    lab.className = 'sf-row-label';
    const name = document.createElement('span');
    name.textContent = def.label;
    const val = document.createElement('span');
    val.textContent = def.fmt(S[def.key]);
    lab.append(name, val);
    const input = document.createElement('input');
    input.type = 'range';
    input.min = String(def.min);
    input.max = String(def.max);
    input.step = String(def.step);
    input.value = String(S[def.key]);
    input.addEventListener('input', () => {
      const v = parseFloat(input.value);
      updateSetting(def.key, def.step >= 1 ? Math.round(v) : v);
      val.textContent = def.fmt(S[def.key]);
    });
    row.append(lab, input);
    panel.appendChild(row);
    // Re-sync this control's slider position + readout from S (used on reset).
    // Skip the readout while locked so a reset never overwrites the lock hint.
    sliderRefreshers.push(() => {
      input.value = String(S[def.key]);
      if (keyUnlocked(def.key)) val.textContent = def.fmt(S[def.key]);
    });

    // Lock skin: a padlocked, disabled row until its rung is bought. Starter
    // dials (rungForKey === null) are never locked.
    const rung = rungForKey(def.key);
    if (rung) {
      const refreshLock = () => {
        const locked = !unlockedRungs.has(rung);
        row.classList.toggle('is-locked', locked);
        input.disabled = locked;
        if (locked) {
          val.textContent = lockHint(rungById(rung));
          row.title = lockTitle(rungById(rung));
        } else {
          val.textContent = def.fmt(S[def.key]);
          row.title = '';
        }
      };
      refreshLock();
      lockRefreshers.push(refreshLock);
    }
  }

  // ---- custom spiral (session-only: blob URLs die with the tab) --------------
  const customWrap = document.createElement('div');
  customWrap.className = 'sf-section';
  const spiralHead = document.createElement('div');
  spiralHead.className = 'sf-row-label sf-words-head';
  const spiralName = document.createElement('span');
  spiralName.textContent = 'spiral gif';
  const spiralState = document.createElement('span');
  spiralHead.append(spiralName, spiralState);
  customWrap.appendChild(spiralHead);

  const spiralZone = document.createElement('div');
  spiralZone.className = 'sf-spiralzone';
  const spiralLine = document.createElement('span');
  spiralZone.appendChild(spiralLine);
  const spiralClear = document.createElement('button');
  spiralClear.type = 'button';
  spiralClear.className = 'sf-chip';
  spiralClear.textContent = 'reset to default';
  const spiralInput = document.createElement('input');
  spiralInput.type = 'file';
  spiralInput.accept = 'image/*,video/*';
  spiralInput.hidden = true;
  customWrap.append(spiralZone, spiralClear, spiralInput);
  panel.appendChild(customWrap);
  gateFeature(customWrap, 'custom', 'custom spiral gif');

  function renderSpiral() {
    const c = getCustomSpiral();
    spiralState.textContent = c ? c.name : 'default';
    spiralLine.textContent = c
      ? `using "${c.name}" - drop another to swap`
      : 'drop a gif / image / video here (or click)';
    spiralClear.style.display = c ? '' : 'none';
  }
  renderSpiral();

  spiralZone.addEventListener('click', () => spiralInput.click());
  spiralInput.addEventListener('change', () => {
    if (spiralInput.files && spiralInput.files[0]) { setCustomSpiral(spiralInput.files[0]); renderSpiral(); }
    spiralInput.value = '';
  });
  spiralZone.addEventListener('dragover', (e) => { e.preventDefault(); e.stopPropagation(); spiralZone.classList.add('is-over'); });
  spiralZone.addEventListener('dragleave', () => spiralZone.classList.remove('is-over'));
  spiralZone.addEventListener('drop', (e) => {
    e.preventDefault();
    e.stopPropagation(); // must not fall through to any page-level drop handling
    spiralZone.classList.remove('is-over');
    const f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
    if (f) { setCustomSpiral(f); renderSpiral(); }
  });
  spiralClear.addEventListener('click', () => { setCustomSpiral(null); renderSpiral(); });

  // ---- subliminal word picker ------------------------------------------------
  const wordsWrap = document.createElement('div');
  wordsWrap.className = 'sf-section';
  const wordsHead = document.createElement('div');
  wordsHead.className = 'sf-row-label sf-words-head';
  const wordsName = document.createElement('span');
  wordsName.textContent = 'subliminal words';
  const wordsCount = document.createElement('span');
  wordsHead.append(wordsName, wordsCount);
  wordsWrap.appendChild(wordsHead);

  const chips = document.createElement('div');
  chips.className = 'sf-chips';
  wordsWrap.appendChild(chips);
  panel.appendChild(wordsWrap);
  gateFeature(wordsWrap, 'words', 'subliminal words');

  function renderChips() {
    chips.innerHTML = '';
    const active = new Set(activeWords());
    for (const w of allWords()) {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'sf-chip' + (active.has(w) ? ' is-on' : '');
      chip.textContent = w;
      chip.addEventListener('click', () => {
        setWordOn(w, !activeWords().includes(w));
        renderChips();
      });
      chips.appendChild(chip);
    }
    wordsCount.textContent = `${active.size} on`;
  }
  renderChips();

  const addWrap = document.createElement('div');
  addWrap.className = 'sf-section';
  const addRow = document.createElement('div');
  addRow.className = 'sf-wordadd';
  const addInput = document.createElement('input');
  addInput.type = 'text';
  addInput.maxLength = 24;
  addInput.placeholder = 'add your own…';
  const addBtn = document.createElement('button');
  addBtn.type = 'button';
  addBtn.className = 'sf-btn';
  addBtn.textContent = 'add';
  function submitWord() {
    if (addCustomWord(addInput.value)) { addInput.value = ''; renderChips(); }
  }
  addBtn.addEventListener('click', submitWord);
  addInput.addEventListener('keydown', (e) => {
    e.stopPropagation(); // typing must not trim the fall speed / toggle pause
    if (e.key === 'Enter') submitWord();
  });
  addRow.append(addInput, addBtn);
  addWrap.appendChild(addRow);
  panel.appendChild(addWrap);
  gateFeature(addWrap, 'custom', 'add your own words');

  // ---- diagnostics -------------------------------------------------------------
  // The on-phone black box: the card pipeline has broken silently on iPhone
  // more than once, and there are no devtools there. This mirrors the console
  // globals (__sfCards / __sfPipe / __sfMedia / __sfErrors) into the panel so
  // a screenshot of it tells the whole story.
  const diagHead = document.createElement('div');
  diagHead.className = 'sf-row-label sf-words-head';
  const diagName = document.createElement('span');
  diagName.textContent = 'diagnostics';
  const diagBtn = document.createElement('button');
  diagBtn.type = 'button';
  diagBtn.className = 'sf-chip';
  diagBtn.textContent = 'show';
  diagHead.append(diagName, diagBtn);
  const diagOut = document.createElement('div');
  diagOut.className = 'sf-diag';
  diagOut.hidden = true;
  panel.append(diagHead, diagOut);

  let diagTimer = 0;
  const n = (v) => (v == null ? '?' : v);
  function renderDiag() {
    const d = window.__sfCards || {};
    const p = window.__sfPipe ? window.__sfPipe() : {};
    const m = window.__sfMedia ? window.__sfMedia() : {};
    const f = window.__sfPerf ? window.__sfPerf() : {};
    const errs = window.__sfErrors || [];
    diagOut.textContent = [
      `render  ${n(f.fps)} fps | scale ${n(f.scale)} | dpr ${n(f.dpr)} | ${n(f.tier)}`,
      `media   ${n(m.images)} img | ${n(m.videos)} vid | skipped ${n(m.skipped)}`,
      `pipe    ready ${n(p.ready)} | inflight ${n(p.inflight)} | stalls ${n(d.prefetchStall)}`,
      `live    cards ${n(p.live)} | videos ${n(p.liveVideos)}`,
      `cards   draws ${n(d.draws)} | spawned ${n(d.added)} | img ${n(d.imgOk)} | vid ${n(d.vidOk)}`,
      `gif     try ${n(d.gifTry)} | ok ${n(d.gifOk)} | fail ${n(d.gifFail)} | shown ${n(d.gifApplied)}`,
      `worker  ok ${n(d.gifWorkerOk)} | timeout ${n(d.gifWorkerTimeout)} | dead ${p.workerDead ? 'YES' : 'no'}`,
      `audio   ${peekAudioState()}`,
      errs.length ? 'errors:\n' + errs.map((x) => '· ' + x).join('\n') : 'errors  none',
    ].join('\n');
  }
  function stopDiag() {
    if (diagTimer) { clearInterval(diagTimer); diagTimer = 0; }
  }
  function startDiag() {
    stopDiag();
    renderDiag();
    diagTimer = window.setInterval(renderDiag, 800);
  }
  diagBtn.addEventListener('click', () => {
    const show = diagOut.hidden;
    diagOut.hidden = !show;
    diagBtn.textContent = show ? 'hide' : 'show';
    show ? startDiag() : stopDiag();
  });

  const done = document.createElement('button');
  done.type = 'button';
  done.className = 'sf-btn sf-btn-primary sf-panel-done';
  done.textContent = 'done';
  panel.appendChild(done);

  hud.appendChild(panel);

  let openState = false;
  function open() {
    openState = true;
    panel.classList.add('is-open');
    if (!diagOut.hidden) startDiag(); // resume the readout with the panel
  }
  function close() {
    openState = false;
    panel.classList.remove('is-open');
    stopDiag(); // never tick behind a closed panel
  }
  closeBtn.addEventListener('click', close);
  done.addEventListener('click', close);

  return {
    open,
    close,
    toggle() { openState ? close() : open(); },
    isOpen: () => openState,
    // Push the set of purchased rung ids (from the meta snapshot) - reveals every
    // dial/section whose rung is now owned. Cheap; safe to call on every snapshot.
    setUnlocks(ids) {
      unlockedRungs.clear();
      for (const id of (ids || [])) unlockedRungs.add(id);
      lockRefreshers.forEach((fn) => fn());
      featureRefreshers.forEach((fn) => fn());
    },
    dispose() { stopDiag(); panel.remove(); },
  };
}
