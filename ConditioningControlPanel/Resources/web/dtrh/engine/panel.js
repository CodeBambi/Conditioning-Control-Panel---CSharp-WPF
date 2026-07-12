/* ============================================================================
 * panel.js - the gear panel: live sliders + subliminal word picker.
 *
 * Pure DOM, slides in from the right over the HUD. Every control writes the
 * settings store (settings.js) directly; consumers read S live, so changes
 * apply to the next bubble/card/effect with the game still running - handy
 * for tuning by eye. ESC closes it (the scene owns that key).
 * ==========================================================================*/

import { S, updateSetting, resetOptions, allWords, activeWords, setWordOn, addCustomWord, setCustomSpiral, getCustomSpiral,
  rungForKey, rungForFeature, UNLOCK_LADDER, RANK_NAMES, MOTIONS } from './settings.js';
import { POOL_VARIANTS, POOL_PRESETS } from '../game/catalog.js';
import { peekAudioState } from './audioBus.js';
import { audioGroups, getLevel, setLevel, onLevels } from './audioLevels.js';

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

// Gold glyph for the lock hints (matches catalog GLYPHS.gold - gold unlocks dials).
const GOLD = '🪙';
const lockHint = (r) => r.rankReq != null ? `🔒 ${GOLD}${r.price} · ${RANK_NAMES[r.rankReq]}` : `🔒 ${GOLD}${r.price}`;
const lockTitle = (r) => {
  const rank = r.rankReq != null ? ` and reaching rank ${RANK_NAMES[r.rankReq]}` : '';
  return `Locked — buy it at the Dollhouse DIALS console for ${r.price} gold${rank}.`;
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

  // ---- audio (per-group volumes) ----------------------------------------------
  // The same groups the in-descent audio dock exposes, surfaced here so they're
  // reachable from the MAIN MENU too (the dock is hidden in the hub). Reads/writes
  // audioLevels.js live; onLevels keeps these in sync if the dock moves a level.
  const audioHead = document.createElement('div');
  audioHead.className = 'sf-row-label sf-words-head';
  const audioName = document.createElement('span');
  audioName.textContent = 'audio';
  audioHead.appendChild(audioName);
  panel.appendChild(audioHead);
  const audioRefreshers = [];
  for (const g of audioGroups()) {
    const max = g.key === 'music' ? 2 : 1;   // music is a 0..2 multiplier
    const row = document.createElement('div');
    row.className = 'sf-row';
    const lab = document.createElement('div');
    lab.className = 'sf-row-label';
    const name = document.createElement('span');
    name.textContent = g.label;
    const val = document.createElement('span');
    const fmt = (v) => `${Math.round(v * 100)}%`;
    val.textContent = fmt(getLevel(g.key));
    lab.append(name, val);
    const input = document.createElement('input');
    input.type = 'range';
    input.min = '0'; input.max = String(max); input.step = '0.05';
    input.value = String(getLevel(g.key));
    input.addEventListener('input', () => {
      const v = parseFloat(input.value);
      setLevel(g.key, v);
      val.textContent = fmt(getLevel(g.key));
    });
    row.append(lab, input);
    panel.appendChild(row);
    audioRefreshers.push((key) => {
      if (key && key !== g.key) return;
      input.value = String(getLevel(g.key));
      val.textContent = fmt(getLevel(g.key));
    });
  }
  // Keep the panel's sliders live if the in-descent dock moves a level.
  onLevels((key) => audioRefreshers.forEach((fn) => fn(key)));

  // ---- THE DESCENT (the old Warren run-setup tab, now regular options) --------
  // Motion / effect intensity / moods / bubble pool. These write the run* keys in
  // settings.js; warren.currentSetup() folds descentSetup() into request-run, so
  // they take effect on the NEXT descent (not live like the sliders above).
  const descWrap = document.createElement('div');
  descWrap.className = 'sf-section';
  const descHead = document.createElement('div');
  descHead.className = 'sf-row-label sf-words-head';
  const descName = document.createElement('span');
  descName.textContent = 'the descent (next fall)';
  descHead.appendChild(descName);
  descWrap.appendChild(descHead);

  // motion pills
  const motionRow = document.createElement('div');
  motionRow.className = 'sf-chips';
  const motionLabel = { Mixed: 'Mixed', FloatUp: 'Float Up', RainDown: 'Rain Down', RoamBounce: 'Roam' };
  const motionChips = [];
  for (const m of MOTIONS) {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'sf-chip' + (S.runMotion === m ? ' is-on' : '');
    chip.textContent = motionLabel[m] || m;
    chip.addEventListener('click', () => {
      updateSetting('runMotion', m);
      for (const c of motionChips) c.classList.toggle('is-on', c === chip);
    });
    motionChips.push(chip);
    motionRow.appendChild(chip);
  }
  descWrap.appendChild(motionRow);

  // effect intensity slider
  {
    const row = document.createElement('div');
    row.className = 'sf-row';
    const lab = document.createElement('div');
    lab.className = 'sf-row-label';
    const name = document.createElement('span');
    name.textContent = 'effect intensity';
    const val = document.createElement('span');
    val.textContent = `${Math.round(S.runEffectIntensity * 100)}%`;
    lab.append(name, val);
    const input = document.createElement('input');
    input.type = 'range';
    input.min = '20'; input.max = '150'; input.step = '5';
    input.value = String(Math.round(S.runEffectIntensity * 100));
    input.addEventListener('input', () => {
      updateSetting('runEffectIntensity', Number(input.value) / 100);
      val.textContent = `${input.value}%`;
    });
    row.append(lab, input);
    descWrap.appendChild(row);
  }

  // mood checkboxes
  const moods = [
    ['runColorFlashes', 'color flashes on the edges'],
    ['runBoonDraft', 'mantra drafts between loops'],
    ['runAllowCurses', 'sins on the table'],
    ['runDarters', 'white rabbits'],
  ];
  for (const [key, label] of moods) {
    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'sf-chip sf-chip--wide' + (S[key] ? ' is-on' : '');
    row.textContent = `${S[key] ? '☑' : '☐'}  ${label}`;
    row.addEventListener('click', () => {
      updateSetting(key, !S[key]);
      row.classList.toggle('is-on', S[key]);
      row.textContent = `${S[key] ? '☑' : '☐'}  ${label}`;
    });
    descWrap.appendChild(row);
  }

  // bubble pool: variant chips (toggle OFF into runVariantsOff) + presets.
  // No rank locks here anymore (2026-07): the giants are open from the first
  // run; the spawner chamber-gates them (chambers III-IV) in-run instead.
  const poolHead = document.createElement('div');
  poolHead.className = 'sf-row-label sf-words-head';
  const poolName = document.createElement('span');
  poolName.textContent = 'the bubble pool';
  poolHead.appendChild(poolName);
  descWrap.appendChild(poolHead);
  const poolRow = document.createElement('div');
  poolRow.className = 'sf-chips';
  descWrap.appendChild(poolRow);
  const presetRow = document.createElement('div');
  presetRow.className = 'sf-chips';
  descWrap.appendChild(presetRow);

  function setVariantsOff(offIds) {
    // never let the pool go empty - 'flash' is the floor
    const valid = new Set(POOL_VARIANTS.map((v) => v.id));
    let off = offIds.filter((id) => valid.has(id));
    if (off.length >= POOL_VARIANTS.length) off = off.filter((id) => id !== 'flash');
    updateSetting('runVariantsOff', off);
  }

  function renderPool() {
    poolRow.innerHTML = '';
    const off = new Set(S.runVariantsOff);
    for (const pv of POOL_VARIANTS) {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'sf-chip' + (!off.has(pv.id) ? ' is-on' : '');
      chip.textContent = pv.name;
      chip.addEventListener('click', () => {
        const next = new Set(S.runVariantsOff);
        if (next.has(pv.id)) next.delete(pv.id); else next.add(pv.id);
        setVariantsOff([...next]);
        renderPool();
      });
      poolRow.appendChild(chip);
    }
  }
  renderPool();

  for (const pr of POOL_PRESETS) {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'sf-chip';
    chip.textContent = pr.name;
    chip.addEventListener('click', () => {
      const on = new Set(pr.ids);
      setVariantsOff(POOL_VARIANTS.filter((v) => !on.has(v.id)).map((v) => v.id));
      renderPool();
    });
    presetRow.appendChild(chip);
  }
  {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'sf-chip';
    chip.textContent = '🎲 randomize';
    chip.addEventListener('click', () => {
      const roll = POOL_VARIANTS
        .filter(() => Math.random() < 0.6)
        .map((v) => v.id);
      if (!roll.length) roll.push('flash');
      const on = new Set(roll);
      setVariantsOff(POOL_VARIANTS.filter((v) => !on.has(v.id)).map((v) => v.id));
      renderPool();
    });
    presetRow.appendChild(chip);
  }

  panel.appendChild(descWrap);

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

  // ---- the lessons (guided FTUE replay) ---------------------------------------
  // Hidden until the game wires setGameHooks (the standalone Fall has no meta
  // bridge, so it never shows the row). Two-step inline confirm - this re-arms
  // every tutorial card and the next descent becomes the classroom again.
  let hooks = null;
  let lessonsRevert = 0;
  const lessonsWrap = document.createElement('div');
  lessonsWrap.className = 'sf-section';
  lessonsWrap.style.display = 'none';
  const lessonsHead = document.createElement('div');
  lessonsHead.className = 'sf-row-label sf-words-head';
  const lessonsName = document.createElement('span');
  lessonsName.textContent = 'the lessons';
  lessonsHead.appendChild(lessonsName);
  const lessonsBtn = document.createElement('button');
  lessonsBtn.type = 'button';
  lessonsBtn.className = 'sf-chip sf-chip--wide';
  const lessonsIdle = () => {
    lessonsBtn.textContent = '↺ replay her lessons';
    lessonsBtn.classList.remove('is-on');
  };
  lessonsIdle();
  lessonsBtn.addEventListener('click', () => {
    if (!hooks) return;
    if (lessonsRevert) {
      // second click: do it. Close the drawer so the welcome doesn't play under it.
      clearTimeout(lessonsRevert);
      lessonsRevert = 0;
      close();   // close FIRST - the reset snapshot can land within ~700ms and start the welcome under the drawer
      hooks.resetOnboarding();
      lessonsIdle();
      return;
    }
    lessonsBtn.textContent = 'sure? every lesson replays — progress stays';
    lessonsBtn.classList.add('is-on');
    lessonsRevert = window.setTimeout(() => { lessonsRevert = 0; lessonsIdle(); }, 4000);
  });
  // Opt out of the Cheshire guide entirely (some players find the portrait
  // unsettling). ON = no portrait, no scenes, no tutorial voice; the plain
  // lesson cards teach in her place. cheshireGuide reads S.hideTutorial live.
  const hideGuideBtn = document.createElement('button');
  hideGuideBtn.type = 'button';
  const hideGuideLabel = 'hide the guide (Cheshire)';
  const syncHideGuide = () => {
    hideGuideBtn.className = 'sf-chip sf-chip--wide' + (S.hideTutorial ? ' is-on' : '');
    hideGuideBtn.textContent = `${S.hideTutorial ? '☑' : '☐'}  ${hideGuideLabel}`;
  };
  syncHideGuide();
  hideGuideBtn.addEventListener('click', () => {
    updateSetting('hideTutorial', !S.hideTutorial);
    syncHideGuide();
  });
  lessonsWrap.append(lessonsHead, lessonsBtn, hideGuideBtn);
  panel.appendChild(lessonsWrap);

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
    // Kept as a no-op seam: the deep-variant rank lock is gone (the giants are
    // chamber-gated in-run now), but scene still calls this on every snapshot.
    setProgress() {},
    // Game-only actions (reset-onboarding lives on the meta bridge). Passing
    // hooks reveals the lessons row; null hides it again.
    setGameHooks(h) {
      hooks = h || null;
      lessonsWrap.style.display = hooks ? '' : 'none';
    },
    dispose() { stopDiag(); panel.remove(); },
  };
}
