/* ============================================================================
 * chaosHud.js - the run HUD + the center-screen announcer (M4).
 *
 * The top-left strip in the Fall's visual language: score + total multiplier,
 * streak, resistance hearts (+ collar saves), the FOCUS bar (dims + pulses
 * under a snap's price), the LUST (heat) bar, run clock + loop counter, and
 * the Ripple charge. Along the top ride the run's drafted picks (the WPF
 * boon-bar ribbon); bottom-center is the toy dock (hero buttons + keybinds);
 * bottom-left the quiet toast feed. The announcer carries optional banner art
 * (ccp.art/announce/{key}.png) like the WPF ChaosAnnouncerOverlay.
 * ==========================================================================*/

const DEFUSE_COST = 30; // FOCUS bar low-threshold (ChaosTuning.DEFUSE_COST)
const RIPPLE_COST = 30; // the ripple spends this much focus per cast (chaosRun RIPPLE_FOCUS_COST)
const ART = 'https://ccp.art/';

export function createChaosHud(hud, { onToyUse, onWeatherClick } = {}) {
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
  const shieldRow = mk('cf-hud-shields');

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

  const heatRow = mk('cf-hud-heat');
  const heatLabel = document.createElement('span');
  heatLabel.className = 'cf-focus-label';
  heatLabel.textContent = 'LUST';
  const heatBar = document.createElement('div');
  heatBar.className = 'cf-heat-bar';
  const heatFill = document.createElement('div');
  heatFill.className = 'cf-heat-fill';
  heatBar.appendChild(heatFill);
  heatRow.append(heatLabel, heatBar);

  const clockRow = mk('cf-hud-clock');

  // ---- the weather chip (Wave 2): named sky + Mood Ring forecast/reroll ----
  const weatherRow = mk('cf-hud-weather');
  weatherRow.style.display = 'none';
  weatherRow.addEventListener('click', () => { if (onWeatherClick) onWeatherClick(); });

  const rippleRow = mk('cf-hud-ripple');

  // ---- the run-pick ribbon (drafted mantras/sins, in pick order) ----
  const picksWrap = document.createElement('div');
  picksWrap.className = 'cf-picks';
  hud.appendChild(picksWrap);

  function setPicks(picks) {
    picksWrap.innerHTML = '';
    for (const p of picks) {
      const tile = document.createElement('div');
      tile.className = 'cf-pick' + (p.curse ? ' cf-pick--sin' : '');
      tile.title = p.name;
      const img = document.createElement('img');
      img.src = `${ART}boons/${p.id}.png`;
      img.alt = '';
      img.addEventListener('error', () => { img.remove(); tile.textContent = p.curse ? '☠' : '◈'; });
      tile.appendChild(img);
      picksWrap.appendChild(tile);
    }
  }

  // ---- the toy dock (equipped active skills; key + cooldown on each) ----
  const dock = document.createElement('div');
  dock.className = 'cf-toydock';
  hud.appendChild(dock);
  const toyEls = new Map();

  function updateToys(toys, status) {
    if (!toys.length) { dock.innerHTML = ''; toyEls.clear(); return; }
    for (const t of toys) {
      let el = toyEls.get(t.id);
      if (!el) {
        el = document.createElement('button');
        el.type = 'button';
        el.className = 'cf-toy';
        el.addEventListener('click', () => onToyUse && onToyUse(t.id));
        const g = document.createElement('span');
        g.className = 'cf-toy-glyph';
        g.textContent = t.glyph;
        const n = document.createElement('span');
        n.className = 'cf-toy-name';
        n.textContent = t.name + (t.key ? ` · ${t.key}` : '');
        const s = document.createElement('span');
        s.className = 'cf-toy-status';
        el.append(g, n, s);
        el._status = s;
        dock.appendChild(el);
        toyEls.set(t.id, el);
      }
      const ready = t.cooldownLeft <= 0 && t.chargesLeft !== 0;
      el._status.textContent = t.chargesLeft >= 0
        ? (t.chargesLeft === 0 ? 'spent' : `${t.chargesLeft} left`)
        : (t.cooldownLeft > 0 ? `${Math.ceil(t.cooldownLeft)}s` : 'ready');
      el.classList.toggle('is-ready', ready);
      el.classList.toggle('is-active', !!t.effectActive);
    }
  }

  // ---- announcer (center-screen, self-fading, optional banner art) ----
  const annWrap = document.createElement('div');
  annWrap.className = 'cf-announce-wrap';
  hud.appendChild(annWrap);
  function announce(text, kind = 'depth', holdMs = 2000, { artKey = null, subText = null } = {}) {
    while (annWrap.children.length >= 2) annWrap.firstChild.remove();
    const el = document.createElement('div');
    el.className = `cf-announce cf-announce--${kind}`;
    el.style.setProperty('--hold', `${holdMs}ms`);
    if (artKey) {
      const img = document.createElement('img');
      img.className = 'cf-announce-art';
      img.src = `${ART}announce/${artKey}.png`;
      img.alt = '';
      img.addEventListener('error', () => img.remove());
      el.appendChild(img);
    }
    const txt = document.createElement('div');
    txt.className = 'cf-announce-text';
    txt.textContent = text;
    el.appendChild(txt);
    if (subText && artKey) {   // like WPF: the subline only rides under banner art
      const sub = document.createElement('div');
      sub.className = 'cf-announce-sub';
      sub.textContent = subText;
      el.appendChild(sub);
    }
    annWrap.appendChild(el);
    el.addEventListener('animationend', (e) => { if (e.target === el) el.remove(); });
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

  // ---- heat edge tint (the WPF rising-temperature held edge; heat > 0.3) ----
  const heatTint = document.createElement('div');
  heatTint.className = 'cf-heat-tint';
  hud.appendChild(heatTint);

  const fmtClock = (s) => `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(Math.floor(s % 60)).padStart(2, '0')}`;

  return {
    announce,
    toast,
    pulse,
    setPicks,
    updateToys,
    update(st) {
      scoreVal.textContent = Math.floor(st.score).toLocaleString();
      multVal.textContent = `×${st.totalMult.toFixed(1)}`;
      streakRow.textContent = st.combo > 1 ? `🔥 streak ×${st.combo}` : '';
      const capacity = Math.max(st.startingShields, st.shields);
      shieldRow.textContent = (capacity > 0 || st.collarSaves > 0)
        ? '♥'.repeat(st.shields) + '♡'.repeat(Math.max(0, st.startingShields - st.shields))
          + (st.collarSaves > 0 ? ` 📿×${st.collarSaves}` : '')
        : '';
      focusFill.style.width = `${Math.round(st.focus)}%`;
      focusRow.classList.toggle('is-low', st.focus < DEFUSE_COST);
      heatFill.style.width = `${Math.round(st.heat * 100)}%`;
      heatTint.style.opacity = st.heat > 0.3 ? String(((st.heat - 0.3) / 0.7) * 0.30) : '0';
      clockRow.textContent = `${fmtClock(st.elapsedSec)} / ${fmtClock(st.runDurationSec)} · LOOP ${st.waveIndex}/${st.waveCount}`;
      const rippleCost = st.rippleCost || RIPPLE_COST;
      const rippleReady = st.focus >= rippleCost;
      rippleRow.textContent = rippleReady ? '🌊 ripple READY · right-click' : `🌊 ${rippleCost} focus`;
      rippleRow.classList.toggle('is-ready', rippleReady);
    },
    /** Wave 2: show/hide the weather chip. wx = { glyph, name, desc,
     * forecast: 'glyph NAME' | null, rerollable: bool } or null to hide. */
    setWeather(wx) {
      if (!wx) {
        weatherRow.style.display = 'none';
        weatherRow.classList.remove('is-reroll');
        return;
      }
      weatherRow.style.display = '';
      weatherRow.textContent = `${wx.glyph} ${wx.name}` + (wx.forecast ? ` → ${wx.forecast}` : '');
      weatherRow.title = (wx.desc || '')
        + (wx.rerollable ? '\n💍 click: she changes her mind (once per descent)' : '');
      weatherRow.classList.toggle('is-reroll', !!wx.rerollable);
    },
    flashFocus() {
      focusBar.classList.remove('cf-focus-flash');
      void focusBar.offsetWidth;
      focusBar.classList.add('cf-focus-flash');
    },
    flashShields() {
      shieldRow.classList.remove('cf-shield-flash');
      void shieldRow.offsetWidth;
      shieldRow.classList.add('cf-shield-flash');
    },
    setVisible(v) { root.style.display = v ? '' : 'none'; picksWrap.style.display = v ? '' : 'none'; dock.style.display = v ? '' : 'none'; },
    dispose() {
      clearTimeout(pulseTimer);
      root.remove(); annWrap.remove(); toastWrap.remove(); pulseEl.remove();
      picksWrap.remove(); dock.remove(); heatTint.remove();
    },
  };
}
