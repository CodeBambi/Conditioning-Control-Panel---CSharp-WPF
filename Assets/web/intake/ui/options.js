/* ============================================================================
 * OPTIONS PANEL — the intake's one settings screen, mounted in TWO places:
 * the main menu (ui/menu.js) and the in-game pause menu (ui/pause.js). Both call
 * the same openOptions() and await the same promise; there is deliberately no
 * second copy of these rows anywhere.
 *
 *   openOptions({ parent, audio }) -> Promise<void>   // resolves when it closes
 *
 * Everything it edits lives in ui/prefs.js (localStorage-backed, synchronous, no
 * host round-trip). Writes happen LIVE on `input` — there is no OK/Cancel, and
 * closing the panel is not a commit. render/audio.js subscribes to prefs and
 * glides its three pref buses, so a slider dragged from the pause menu retunes
 * the bed that is still sounding underneath.
 *
 * AUDITION ON RELEASE: dragging is silent, but letting go of a slider plays one
 * representative cue on THAT bus so you hear what you just set. Binaural has no
 * audition cue on purpose — the live bed IS the audition, and firing a second
 * bed voice over it would only muddy the thing you are trying to judge.
 *
 * Styling is entirely ui/kawaii.css (`.kw-*`); the only markup-specific rules are
 * in that file's "OPTIONS PANEL" section. Offline-safe: no fonts, no assets.
 * ========================================================================== */

import {
  getPrefs, setPref, resetPrefs, subscribe as subscribePrefs,
} from './prefs.js';
import * as fullscreen from './fullscreen.js';

/* ----------------------------------------------------------------------------
 * AUDITION CUES — both ids are verified to exist on disk:
 *   'surface-bloom'   -> assets/sfx/sfx_manifest.json + assets/sfx/surface-bloom-1.mp3
 *   'q_bmb_h0_math'   -> assets/vo/vo_manifest.json  + assets/vo/q_bmb_h0_math.mp3
 * The VO line is "What is 2 + 2?" — the shortest tonally-neutral line in the
 * corpus (heat 0, composed-clinical delivery), so auditioning the voice slider
 * never drops you into a taunt you did not ask for.
 * -------------------------------------------------------------------------- */
const AUDITION_SFX = 'surface-bloom';
const AUDITION_VO  = 'q_bmb_h0_math';

/** The rows, in display order. `kind` drives which control gets built.
 *  `late: true` rows are built out of the main loop (below the separator) but
 *  still repaint with everything else, so paint() stays a single pass. */
const SLIDER_ROWS = [
  { key: 'volMaster',   label: 'Master',   hint: 'Everything, all at once.' },
  { key: 'volBinaural', label: 'Binaural', hint: 'The tone underneath.' },
  { key: 'volEffects',  label: 'Effects',  hint: 'Chimes, pops, stings.' },
  { key: 'volVoice',    label: 'Voice',    hint: 'The spoken lines.' },
  { key: 'volMusic',    label: 'Music',    hint: 'The menu song only.', late: true },
];

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/** 0..1 pref -> the "87%" readout. */
function pct(v) { return Math.round(Math.max(0, Math.min(1, Number(v) || 0)) * 100) + '%'; }

/**
 * Open the options panel.
 * @param {{parent?:Element, audio?:object}} [opts]
 * @returns {Promise<void>} resolves once the panel is fully torn down.
 */
export function openOptions(opts) {
  opts = opts || {};
  const audio = opts.audio || null;
  const parent = (opts.parent && opts.parent.appendChild) ? opts.parent
    : (typeof document !== 'undefined' ? document.body : null);
  if (!parent) return Promise.resolve();

  return new Promise((resolve) => {
    /* --- audio helpers: EVERY call guarded; a stub/absent audio module or a
     *     dead AudioContext must never break the panel. ---------------------- */
    const tryCall = (fn) => { try { return fn(); } catch (_e) { return null; } };

    // Warm the audition sample the moment the panel opens. playSfx() fetches +
    // decodes lazily and returns null (silently) until the buffer is ready, so a
    // zero-intensity call warms it without ever making a sound — otherwise the
    // very first slider release of a session would be silent.
    tryCall(() => { if (audio && typeof audio.sfx === 'function') audio.sfx(AUDITION_SFX, 0); });

    const auditionEffects = () => tryCall(() => {
      if (audio && typeof audio.sfx === 'function') audio.sfx(AUDITION_SFX, 1);
    });
    // Only ever stop a VO line WE started — the pause menu can be opened over a
    // run, and killing the host's line would be a side effect nobody asked for.
    let voAuditioned = false;
    const auditionVoice = () => tryCall(() => {
      if (audio && typeof audio.voice === 'function') {
        voAuditioned = true;
        audio.voice(AUDITION_VO);
      }
    });
    const stopAudition = () => {
      if (!voAuditioned) return;
      voAuditioned = false;
      tryCall(() => {
        if (audio && typeof audio.voiceStop === 'function') audio.voiceStop(0.12);
      });
    };

    /* --- DOM ------------------------------------------------------------- */
    const root = el('div', 'kw-root kw-options');
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.setAttribute('aria-label', 'Options');

    const scrim = el('div', 'kw-scrim');
    root.appendChild(scrim);

    const panel = el('div', 'kw-panel kw-pop-in kw-options__panel');
    root.appendChild(panel);

    panel.appendChild(el('h2', 'kw-options__title', 'Options'));
    panel.appendChild(el('hr', 'kw-rainbow-rule'));

    const body = el('div', 'kw-options__body');
    panel.appendChild(body);

    /* --- control builders -------------------------------------------------- */
    const listeners = [];   // [target, type, fn] — every one removed on close
    const on = (target, type, fn, o) => {
      target.addEventListener(type, fn, o);
      listeners.push([target, type, fn, o]);
    };

    /** A boolean row backed by the kawaii pill toggle (ARIA switch). */
    function buildToggle(key, label, hint, onFlip) {
      const row = el('div', 'kw-row');
      const lab = el('div', 'kw-row__label', label);
      const btn = el('button', 'kw-toggle');
      btn.type = 'button';
      btn.setAttribute('role', 'switch');
      btn.setAttribute('aria-label', label);
      const note = el('div', 'kw-options__hint', hint);

      const flip = () => {
        const next = !(btn.getAttribute('aria-checked') === 'true');
        setPref(key, next);
        paint();
        if (typeof onFlip === 'function') onFlip(next);
      };
      on(btn, 'click', flip);
      // Native <button> already fires click on Enter AND Space, but Space also
      // scrolls the page in some hosts — swallow it and drive the flip ourselves.
      on(btn, 'keydown', (e) => {
        if (e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); flip(); }
      });

      row.appendChild(lab);
      row.appendChild(btn);
      row.appendChild(note);
      body.appendChild(row);
      return btn;
    }

    /**
     * A boolean row that is NOT backed by prefs.js — same pill, but it reads and
     * writes live state somewhere else. Used for the window mode, which is owned
     * by the C# host (see ui/fullscreen.js) and must never be duplicated into
     * localStorage, or the two stores would disagree the first time the host
     * launched the window in the remembered mode.
     */
    function buildSwitch(label, hint, read, flip) {
      const row = el('div', 'kw-row');
      const lab = el('div', 'kw-row__label', label);
      const btn = el('button', 'kw-toggle');
      btn.type = 'button';
      btn.setAttribute('role', 'switch');
      btn.setAttribute('aria-label', label);
      const note = el('div', 'kw-options__hint', hint);

      const go = () => { flip(); };
      on(btn, 'click', go);
      on(btn, 'keydown', (e) => {
        if (e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); go(); }
      });

      row.appendChild(lab);
      row.appendChild(btn);
      row.appendChild(note);
      body.appendChild(row);
      return { btn, read };
    }

    /** A 0..100 slider row backed by a native <input type=range>. */
    function buildSlider(key, label, hint, onRelease) {
      const row = el('div', 'kw-row');
      const lab = el('div', 'kw-row__label', label);
      const input = document.createElement('input');
      input.type = 'range';
      input.className = 'kw-slider';
      input.min = '0'; input.max = '100'; input.step = '1';
      input.setAttribute('aria-label', label + ' volume');
      const val = el('div', 'kw-row__value', '');

      on(input, 'input', () => {
        setPref(key, Number(input.value) / 100);   // setPref no-ops on no change
        val.textContent = pct(Number(input.value) / 100);
        syncEnabled();
      });
      // `change` fires on pointer release / after a keyboard nudge settles.
      on(input, 'change', () => { if (typeof onRelease === 'function') onRelease(); });

      row.appendChild(lab);
      row.appendChild(input);
      row.appendChild(val);
      body.appendChild(row);
      const note = el('div', 'kw-options__hint kw-options__hint--under', hint);
      body.appendChild(note);
      return { input, val };
    }

    /* --- rows -------------------------------------------------------------- */
    const voToggle = buildToggle(
      'voEnabled', 'Voiceover', 'She speaks the questions out loud.',
      (nowOn) => { if (nowOn) auditionVoice(); else stopAudition(); },
    );

    const sliders = {};
    for (const r of SLIDER_ROWS) {
      if (r.late) continue;                        // built below the separator
      const release =
        r.key === 'volVoice'    ? auditionVoice :
        r.key === 'volBinaural' ? null            // the live bed IS the audition
                                : auditionEffects; // master + effects
      sliders[r.key] = buildSlider(r.key, r.label, r.hint, release);
    }

    body.appendChild(el('hr', 'kw-options__sep'));

    /* --- the menu song ------------------------------------------------------
     * Its own switch and its own level, deliberately below the separator and
     * deliberately NOT under Master. This is one named licensed track that some
     * players will simply not want, and taste on a specific song is not the same
     * kind of setting as "how loud is the room" — see musicScale() in prefs.js.
     * ui/menuMusic.js subscribes to these prefs directly and does the fade, so
     * there is nothing to call here: writing the pref IS the wiring, and the
     * menu's corner mute button stays in sync through the same subscription. */
    const musicToggle = buildToggle(
      'musicOn', 'Menu music', 'The song on the menu screens. Never plays during the intake.',
    );
    sliders.volMusic = buildSlider('volMusic', 'Music', 'The menu song only.', null);

    body.appendChild(el('hr', 'kw-options__sep'));

    const sparkleToggle = buildToggle(
      'sparkles', 'Sparkles', 'Twinkles and rainbows on the menus.',
    );

    /* --- the window itself --------------------------------------------------
     * The discoverable home for the mode: settable from the main menu BEFORE a
     * run, and from the pause menu during one. The pause menu carries its own
     * direct button as well — that one is the escape hatch, this one is the
     * setting. Both drive ui/fullscreen.js, and the subscription below repaints
     * this pill when the other affordance (or F11) moves it, so they cannot
     * disagree. Hidden entirely where the platform has no fullscreen at all,
     * rather than sitting here dead. */
    let fsSwitch = null;
    let unsubFs = null;
    if (fullscreen.supported) {
      body.appendChild(el('hr', 'kw-options__sep'));
      fsSwitch = buildSwitch(
        'Fullscreen', 'Fills the screen. F11 also toggles it, any time.',
        () => fullscreen.isActive(), () => fullscreen.toggle(),
      );
      unsubFs = fullscreen.subscribe(() => paint());
    }

    /* --- footer ------------------------------------------------------------ */
    const foot = el('div', 'kw-options__foot');
    panel.appendChild(foot);

    const resetBtn = el('button', 'kw-btn kw-btn--soft', 'Reset to defaults');
    resetBtn.type = 'button';
    on(resetBtn, 'click', () => { resetPrefs(); paint(); auditionEffects(); });
    foot.appendChild(resetBtn);

    const backBtn = el('button', 'kw-btn', 'Back');
    backBtn.type = 'button';
    on(backBtn, 'click', () => close());
    foot.appendChild(backBtn);

    /* --- painting ---------------------------------------------------------- */
    let painting = false;

    /** Grey the Voice slider out when the voiceover switch is off. */
    function syncEnabled() {
      const p = getPrefs();
      const voiceOff = !p.voEnabled;
      sliders.volVoice.input.disabled = voiceOff;
      sliders.volVoice.input.setAttribute('aria-disabled', voiceOff ? 'true' : 'false');
      const musicOff = !p.musicOn;
      sliders.volMusic.input.disabled = musicOff;
      sliders.volMusic.input.setAttribute('aria-disabled', musicOff ? 'true' : 'false');
      root.classList.toggle('kw-no-decor', !p.sparkles);
    }

    /** Push the whole store into the controls (open, reset, external change). */
    function paint() {
      if (painting) return;
      painting = true;
      try {
        const p = getPrefs();
        voToggle.setAttribute('aria-checked', p.voEnabled ? 'true' : 'false');
        musicToggle.setAttribute('aria-checked', p.musicOn ? 'true' : 'false');
        sparkleToggle.setAttribute('aria-checked', p.sparkles ? 'true' : 'false');
        if (fsSwitch) fsSwitch.btn.setAttribute('aria-checked', fsSwitch.read() ? 'true' : 'false');
        for (const r of SLIDER_ROWS) {
          const s = sliders[r.key];
          const v = Math.round((p[r.key] || 0) * 100);
          if (Number(s.input.value) !== v) s.input.value = String(v);
          s.val.textContent = pct(p[r.key]);
        }
        syncEnabled();
      } finally { painting = false; }
    }

    // Repaint on any external write (another surface, a host reset) so the panel
    // can never show a stale number. Our own writes land here too and are a no-op
    // repaint, which is why `painting` guards re-entry.
    const unsubPrefs = subscribePrefs(() => paint());

    paint();

    /* --- dismissal --------------------------------------------------------- */
    let closed = false;
    const prevFocus = (typeof document !== 'undefined') ? document.activeElement : null;

    function close() {
      if (closed) return;
      closed = true;
      stopAudition();
      try { unsubPrefs(); } catch (_e) {}
      try { if (unsubFs) unsubFs(); } catch (_e) {}
      for (const [t, type, fn, o] of listeners) {
        try { t.removeEventListener(type, fn, o); } catch (_e) {}
      }
      listeners.length = 0;
      try { if (root.parentNode) root.parentNode.removeChild(root); } catch (_e) {}
      try { if (prevFocus && prevFocus.focus) prevFocus.focus(); } catch (_e) {}
      resolve();
    }

    on(scrim, 'click', () => close());
    // Capture phase + stopPropagation so Escape closes THIS panel and does not
    // also reach the pause menu / run underneath and pop two layers at once.
    on(document, 'keydown', (e) => {
      if (e.key === 'Escape' || e.key === 'Esc') {
        e.preventDefault(); e.stopPropagation();
        close();
        return;
      }
      if (e.key !== 'Tab') return;
      // Tiny focus trap: the panel is modal, so Tab must not escape into the
      // frozen run behind it.
      const focusables = Array.prototype.filter.call(
        panel.querySelectorAll('button, input, [tabindex]:not([tabindex="-1"])'),
        (n) => !n.disabled && n.offsetParent !== null,
      );
      if (!focusables.length) return;
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    }, true);

    parent.appendChild(root);
    try { backBtn.focus(); } catch (_e) {}
  });
}

export default openOptions;
