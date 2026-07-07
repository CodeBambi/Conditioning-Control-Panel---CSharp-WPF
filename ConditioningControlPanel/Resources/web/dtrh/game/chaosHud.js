/* ============================================================================
 * chaosHud.js - the M3 minimal run HUD + the center-screen announcer.
 *
 * A compact top-left strip in the Fall's visual language (the Fall's own
 * sf-score is hidden in game mode): score + total multiplier, streak, the
 * FOCUS bar (dims + pulses under a snap's price), run clock + loop counter,
 * and the Ripple charge. The announcer is the WPF ChaosAnnouncerOverlay's
 * spirit - one or two big lines center-screen that fade on their own - and
 * toast() is the quiet event-feed replacement for gold tips and the like.
 * ==========================================================================*/

const DEFUSE_COST = 30; // FOCUS bar low-threshold (ChaosTuning.DEFUSE_COST)

export function createChaosHud(hud) {
  const root = document.createElement('div');
  root.className = 'cf-hud';
  hud.appendChild(root);

  const mk = (cls, parent) => {
    const d = document.createElement('div');
    d.className = cls;
    (parent || root).appendChild(d);
    return d;
  };

  const scoreRow = mk('cf-hud-score');
  const scoreVal = document.createElement('span');
  scoreVal.className = 'cf-score-val';
  scoreVal.textContent = '0';
  const multVal = document.createElement('span');
  multVal.className = 'cf-mult-val';
  multVal.textContent = '×1.0';
  scoreRow.append(scoreVal, multVal);

  const streakRow = mk('cf-hud-streak');
  streakRow.textContent = '';

  const focusRow = mk('cf-hud-focus');
  const focusLabel = document.createElement('span');
  focusLabel.className = 'cf-focus-label';
  focusLabel.textContent = 'FOCUS';
  const focusBar = document.createElement('div');
  focusBar.className = 'cf-focus-bar';
  const focusFill = document.createElement('div');
  focusFill.className = 'cf-focus-fill';
  focusBar.appendChild(focusFill);
  focusRow.append(focusLabel, focusBar);

  const clockRow = mk('cf-hud-clock');
  const rippleRow = mk('cf-hud-ripple');

  // ---- announcer (center-screen, self-fading) ----
  const annWrap = document.createElement('div');
  annWrap.className = 'cf-announce-wrap';
  hud.appendChild(annWrap);
  function announce(text, kind = 'depth', holdMs = 2000) {
    while (annWrap.children.length >= 2) annWrap.firstChild.remove();
    const el = document.createElement('div');
    el.className = `cf-announce cf-announce--${kind}`;
    el.textContent = text;
    el.style.setProperty('--hold', `${holdMs}ms`);
    annWrap.appendChild(el);
    el.addEventListener('animationend', () => el.remove(), { once: true });
    window.setTimeout(() => el.remove(), holdMs + 900);
  }

  // ---- toasts (quiet feed lines, bottom-left) ----
  const toastWrap = document.createElement('div');
  toastWrap.className = 'cf-toast-wrap';
  hud.appendChild(toastWrap);
  function toast(text) {
    while (toastWrap.children.length >= 4) toastWrap.firstChild.remove();
    const el = document.createElement('div');
    el.className = 'cf-toast';
    el.textContent = text;
    toastWrap.appendChild(el);
    window.setTimeout(() => { el.classList.add('is-gone'); window.setTimeout(() => el.remove(), 700); }, 4200);
  }

  // ---- full-screen pulse (the WPF ChaosFxWindow color flash, config-gated upstream) ----
  const pulseEl = document.createElement('div');
  pulseEl.className = 'cf-pulse';
  hud.appendChild(pulseEl);
  let pulseTimer = 0;
  function pulse(rgb, strength) {
    pulseEl.style.background = `radial-gradient(ellipse at center, transparent 40%, rgba(${rgb},${Math.min(0.55, strength)}) 100%)`;
    pulseEl.classList.remove('is-on');
    void pulseEl.offsetWidth; // restart the fade animation
    pulseEl.classList.add('is-on');
    clearTimeout(pulseTimer);
    pulseTimer = window.setTimeout(() => pulseEl.classList.remove('is-on'), 450);
  }

  const fmtClock = (s) => `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(Math.floor(s % 60)).padStart(2, '0')}`;

  return {
    announce,
    toast,
    pulse,
    update(st) {
      scoreVal.textContent = Math.floor(st.score).toLocaleString();
      multVal.textContent = `×${st.totalMult.toFixed(1)}`;
      streakRow.textContent = st.combo > 1 ? `🔥 streak ×${st.combo}` : '';
      focusFill.style.width = `${Math.round(st.focus)}%`;
      focusRow.classList.toggle('is-low', st.focus < DEFUSE_COST);
      clockRow.textContent = `${fmtClock(st.elapsedSec)} / ${fmtClock(st.runDurationSec)} · LOOP ${st.waveIndex}/${st.waveCount}`;
      rippleRow.textContent = st.rippleCooldown <= 0 ? '🌊 ripple READY · right-click' : `🌊 ${Math.ceil(st.rippleCooldown)}s`;
      rippleRow.classList.toggle('is-ready', st.rippleCooldown <= 0);
    },
    flashFocus() {
      focusBar.classList.remove('cf-focus-flash');
      void focusBar.offsetWidth;
      focusBar.classList.add('cf-focus-flash');
    },
    setVisible(v) { root.style.display = v ? '' : 'none'; },
    dispose() {
      clearTimeout(pulseTimer);
      root.remove(); annWrap.remove(); toastWrap.remove(); pulseEl.remove();
    },
  };
}
