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

import { createHudTips } from './hudTips.js';
import { HUD_TIPS } from './catalog.js';

const DEFUSE_COST = 30; // FOCUS bar low-threshold (ChaosTuning.DEFUSE_COST)
const RIPPLE_COST = 30; // the ripple spends this much focus per cast (chaosRun RIPPLE_FOCUS_COST)
const ART = 'https://ccp.art/';

export function createChaosHud(hud, { onToyUse, onWeatherClick, isSuppressed } = {}) {
  const root = document.createElement('div');
  root.className = 'cf-hud';
  hud.appendChild(root);

  const tips = createHudTips(hud, { isSuppressed });
  let lastSt = null;   // latest update() snapshot - tooltip getters read live values from it
  let lastWx = null;   // latest setWeather() payload
  let lastBiome = null; // latest setBiome() payload (the active region mechanic)

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
  scoreRow.append(scoreVal);

  // ---- the biome banner: current place name + its live effect, up top so the
  // player always knows which room's rules are in force ----
  const biomeRow = mk('cf-hud-biome');
  biomeRow.style.display = 'none';
  const biomeArt = document.createElement('img'); biomeArt.className = 'cf-biome-art'; biomeArt.alt = '';
  biomeArt.addEventListener('error', () => { biomeArt.style.display = 'none'; });   // tile missing: text-only chip
  const biomeText = document.createElement('span'); biomeText.className = 'cf-biome-text';
  const biomeGlyph = document.createElement('span'); biomeGlyph.className = 'cf-biome-glyph';
  const biomeName = document.createElement('span'); biomeName.className = 'cf-biome-name';
  const biomeFx = document.createElement('span'); biomeFx.className = 'cf-biome-fx';
  const biomeHint = document.createElement('span'); biomeHint.className = 'cf-biome-hint';
  biomeText.append(biomeGlyph, biomeName, biomeFx, biomeHint);
  biomeRow.append(biomeArt, biomeText);

  // ---- the multiplier badge (Balatro-style: punches on combo, burns as it climbs) ----
  const badge = mk('cf-mult-badge tier-0');
  const badgeFx = document.createElement('div');
  badgeFx.className = 'cf-mult-embers';
  const badgeCore = document.createElement('div');
  badgeCore.className = 'cf-mult-core';
  const multVal = document.createElement('span');
  multVal.className = 'cf-mult-hero';
  multVal.textContent = '×1.0';
  const streakVal = document.createElement('span');
  streakVal.className = 'cf-mult-streak';
  badgeCore.append(multVal, streakVal);
  badge.append(badgeFx, badgeCore);

  // Fire tiers ride the raw streak count (the tunnel itself fully warps at 25);
  // prevCombo=null means "baseline unset" so a fresh run never reads as a break.
  let prevCombo = null;
  let curTier = 0;
  let emberTimer = 0;
  const EMBER_MS = [0, 500, 260, 150, 100]; // tier -> spawn period
  const EMBER_MAX = 14;                     // live ember node cap
  const tierOf = (c) => (c >= 100 ? 4 : c >= 50 ? 3 : c >= 25 ? 2 : c >= 15 ? 1 : 0);

  function syncEmbers() {
    clearInterval(emberTimer);
    emberTimer = 0;
    if (curTier > 0 && root.style.display !== 'none') {
      emberTimer = window.setInterval(spawnEmber, EMBER_MS[curTier]);
    }
  }
  function spawnEmber() {
    if (badgeFx.childElementCount >= EMBER_MAX) return;
    const s = document.createElement('span');
    s.className = 'cf-ember';
    s.style.left = `${8 + Math.random() * 84}%`;
    s.style.setProperty('--ex', `${(Math.random() * 26 - 13) | 0}px`);
    s.style.setProperty('--edur', `${(0.7 + Math.random() * 0.8).toFixed(2)}s`);
    s.style.setProperty('--esize', `${curTier >= 3 ? 5 + Math.random() * 4 : 3 + Math.random() * 3}px`);
    s.addEventListener('animationend', () => s.remove());
    badgeFx.appendChild(s);
  }
  function setTier(t) {
    if (t === curTier) return;
    badge.classList.remove(`tier-${curTier}`);
    badge.classList.add(`tier-${t}`);
    curTier = t;
    syncEmbers();
  }
  function punch() {
    badgeCore.style.setProperty('--punch-scale', String(1.16 + curTier * 0.05));
    badgeCore.style.setProperty('--punch-rot', `${(Math.random() * 10 - 5).toFixed(1)}deg`);
    badgeCore.classList.remove('is-punch', 'is-break', 'is-crack');
    void badgeCore.offsetWidth;
    badgeCore.classList.add('is-punch');
  }
  function breakFx(hard) {
    badgeCore.classList.remove('is-punch', 'is-break', 'is-crack');
    void badgeCore.offsetWidth;
    badgeCore.classList.add(hard ? 'is-break' : 'is-crack');
  }

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

  // ---- hover tooltips for the mechanic readouts (rect-tested; see hudTips.js) ----
  const FIRE_TIERS = [15, 25, 50, 100];
  tips.attach(scoreRow, () => HUD_TIPS.score);
  tips.attach(biomeRow, () => {
    if (!lastBiome) return null;
    return { glyph: lastBiome.glyph || '◈', kicker: 'the place', name: lastBiome.name || '',
      desc: lastBiome.tagline || '', extra: lastBiome.mechHint || '', accent: '156,232,160' };
  });
  tips.attach(badge, () => {
    if (!lastSt) return HUD_TIPS.mult;
    const c = lastSt.combo | 0;
    const next = FIRE_TIERS.find((t) => c < t);
    return {
      ...HUD_TIPS.mult,
      extra: `streak ${c} · ×${lastSt.totalMult.toFixed(1)} total` + (next ? ` · next fire at ${next}` : ' · fully ablaze'),
    };
  });
  tips.attach(shieldRow, () => {
    if (!shieldRow.textContent) return null;   // row is empty = nothing to explain
    const extra = lastSt
      ? `${lastSt.shields}/${Math.max(lastSt.startingShields, lastSt.shields)} hearts`
        + (lastSt.collarSaves > 0 ? ` · 📿 ${lastSt.collarSaves} collar save${lastSt.collarSaves > 1 ? 's' : ''} left` : '')
      : '';
    return { ...HUD_TIPS.shields, extra };
  });
  tips.attach(focusRow, () => ({ ...HUD_TIPS.focus, extra: lastSt ? `${Math.round(lastSt.focus)}/100` : '' }));
  tips.attach(heatRow, () => HUD_TIPS.lust);
  tips.attach(clockRow, () => HUD_TIPS.clock);
  tips.attach(rippleRow, () => {
    const cost = (lastSt && lastSt.rippleCost) || RIPPLE_COST;
    const ready = lastSt ? lastSt.focus >= cost : false;
    return { ...HUD_TIPS.ripple, extra: `costs ${cost} focus a cast · ${ready ? 'READY' : 'charging'}` };
  });
  tips.attach(weatherRow, () => {
    if (!lastWx) return null;
    return {
      glyph: lastWx.glyph, kicker: 'the weather', name: lastWx.name,
      desc: lastWx.desc || '',
      extra: lastWx.rerollable ? '💍 click: she changes her mind (once per descent)' : '',
    };
  });

  // ---- the run-pick ribbon (drafted mantras/sins, in pick order) ----
  const picksWrap = document.createElement('div');
  picksWrap.className = 'cf-picks';
  hud.appendChild(picksWrap);

  function setPicks(picks) {
    picksWrap.innerHTML = '';
    for (const p of picks) {
      const tile = document.createElement('div');
      tile.className = 'cf-pick' + (p.curse ? ' cf-pick--sin' : '')
        + (p.toy ? ' cf-pick--toy' : '') + (p.spent ? ' cf-pick--spent' : '');
      tips.attach(tile, () => ({
        glyph: p.glyph || (p.curse ? '☠' : '◈'),
        kicker: (p.curse ? 'sin' : p.toy ? (p.spent ? 'toy · spent' : 'toy') : p.relic ? 'relic' : 'mantra')
          + (p.rarity ? ` · ${p.rarity}` : ''),
        name: p.name,
        desc: p.desc || '',
        flavor: p.flavor || '',
        accent: p.curse ? '255,138,138' : p.toy ? '102,224,208' : p.relic ? '255,215,130' : '156,232,160',
      }));
      const img = document.createElement('img');
      img.src = `${ART}boons/${p.id}.png`;
      img.alt = '';
      img.addEventListener('error', () => { img.remove(); tile.textContent = p.glyph || (p.curse ? '☠' : '◈'); });
      tile.appendChild(img);
      picksWrap.appendChild(tile);
    }
  }

  // ---- the toy dock (equipped active skills; key + cooldown on each) ----
  const dock = document.createElement('div');
  dock.className = 'cf-toydock';
  hud.appendChild(dock);
  const toyEls = new Map();
  let lastToys = [];

  // ---- the Eye: hide/show the whole run HUD (bottom-right). The Eye itself and
  // the Fall's gear stay put, so a hidden HUD still has an escape hatch. ----
  const eyeBtn = document.createElement('button');
  eyeBtn.type = 'button';
  eyeBtn.className = 'cf-eye-btn';
  eyeBtn.textContent = '👁';
  eyeBtn.title = 'hide / show the HUD';
  eyeBtn.setAttribute('aria-label', 'hide or show the HUD');
  eyeBtn.style.display = 'none';   // shown only while the run HUD is up
  eyeBtn.addEventListener('click', (e) => {
    e.stopPropagation();
    const hidden = hud.classList.toggle('dtrh-hud-hidden');
    eyeBtn.classList.toggle('is-off', hidden);
    eyeBtn.textContent = hidden ? '🙈' : '👁';
  });
  hud.appendChild(eyeBtn);

  function updateToys(toys, status) {
    lastToys = toys;
    if (!toys.length) { dock.innerHTML = ''; toyEls.clear(); return; }
    // prune buttons for toys no longer in the dock (a consumable was spent/removed)
    const live = new Set(toys.map((t) => t.id));
    for (const [id, el] of toyEls) { if (!live.has(id)) { el.remove(); toyEls.delete(id); } }
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
        const s = document.createElement('span');
        s.className = 'cf-toy-status';
        el.append(g, n, s);
        el._status = s;
        el._name = n;
        // look the toy back up by id at show time - re-equips swap record objects
        const toyId = t.id;
        tips.attach(el, () => {
          const cur = lastToys.find((x) => x.id === toyId);
          if (!cur) return null;
          const state = cur.chargesLeft >= 0
            ? (cur.chargesLeft === 0 ? 'spent' : `${cur.chargesLeft} use${cur.chargesLeft > 1 ? 's' : ''} left`)
            : (cur.cooldownLeft > 0 ? `${Math.ceil(cur.cooldownLeft)}s cooldown` : 'ready');
          return {
            glyph: cur.glyph, kicker: 'toy', name: cur.name,
            desc: cur.desc || '',
            extra: (cur.key ? `press ${cur.key} or click · ` : '') + state,
          };
        });
        dock.appendChild(el);
        toyEls.set(t.id, el);
      }
      // refresh the label each frame - a consumable's slot number can shift as the dock drains
      el._name.textContent = t.name + (t.key ? ` · ${t.key}` : '');
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
  function announce(text, kind = 'depth', holdMs = 2000, { artKey = null, artUrl = null, artBig = false, subText = null, hint = null } = {}) {
    while (annWrap.children.length >= 2) annWrap.firstChild.remove();
    const el = document.createElement('div');
    el.className = `cf-announce cf-announce--${kind}`;
    el.style.setProperty('--hold', `${holdMs}ms`);
    if (artKey || artUrl) {
      // artUrl = a full image URL (the biome roulette tiles); artKey = a
      // shipped announce banner. When both are given the URL leads and the
      // banner is the on-error fallback (a missing tile never leaves a hole).
      const img = document.createElement('img');
      img.className = 'cf-announce-art' + (artUrl ? ' cf-announce-art--tile' : '') + (artBig ? ' cf-announce-art--big' : '');
      img.src = artUrl || `${ART}announce/${artKey}.png`;
      img.alt = '';
      img.addEventListener('error', () => {
        if (artUrl && artKey && img.src !== `${ART}announce/${artKey}.png`) {
          img.className = 'cf-announce-art';
          img.src = `${ART}announce/${artKey}.png`;
        } else img.remove();
      });
      el.appendChild(img);
    }
    const txt = document.createElement('div');
    txt.className = 'cf-announce-text';
    txt.textContent = text;
    el.appendChild(txt);
    if (subText && (artKey || artUrl)) {   // like WPF: the subline only rides under banner art
      const sub = document.createElement('div');
      sub.className = 'cf-announce-sub';
      sub.textContent = subText;
      el.appendChild(sub);
    }
    if (hint) {   // the quiet grey mechanics note under the flavour line
      const h = document.createElement('div');
      h.className = 'cf-announce-hint';
      h.textContent = hint;
      el.appendChild(h);
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
      lastSt = st;
      scoreVal.textContent = Math.floor(st.score).toLocaleString();
      multVal.textContent = `×${st.totalMult.toFixed(1)}`;
      streakVal.textContent = st.combo > 1 ? `streak ${st.combo}` : '';
      const c = st.combo | 0;
      if (prevCombo != null && c !== prevCombo) {
        if (c > prevCombo) punch();                 // +1 pops AND the private-show ×2
        else breakFx(c === 0 && prevCombo >= 5);    // detonation reset = hard break; halving / 1->0 = crack
      }
      prevCombo = c;
      setTier(tierOf(c));
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
      lastWx = wx || null;
      if (!wx) {
        weatherRow.style.display = 'none';
        weatherRow.classList.remove('is-reroll');
        return;
      }
      weatherRow.style.display = '';
      weatherRow.textContent = `${wx.glyph} ${wx.name}` + (wx.forecast ? ` → ${wx.forecast}` : '');
      weatherRow.classList.toggle('is-reroll', !!wx.rerollable);
    },
    /** THE BIOMES: the active place's name + effect, top-left. Pass the biome
     * object ({ glyph, name, tagline }) or null to hide (classic/no-biome runs). */
    setBiome(b) {
      lastBiome = b || null;
      if (!b) { biomeRow.style.display = 'none'; return; }
      biomeRow.style.display = '';
      if (b.id) {
        biomeArt.style.display = '';
        biomeArt.src = `${ART}biomes/${b.id}.png`;   // the roulette tile, as the chip's face
      } else biomeArt.style.display = 'none';
      biomeGlyph.textContent = b.glyph || '';
      biomeName.textContent = b.name || '';
      biomeFx.textContent = b.tagline || '';
      biomeHint.textContent = b.mechHint || '';
      biomeHint.style.display = b.mechHint ? '' : 'none';
    },
    /** THE BIOMES: flare the chip - fired whenever the place's mechanic
     * actually does something, so the event reads as "the biome did that". */
    biomePing() {
      if (biomeRow.style.display === 'none') return;
      biomeRow.classList.remove('is-ping');
      void biomeRow.offsetWidth;
      biomeRow.classList.add('is-ping');
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
    // A shield emblem pops at screen-centre then flies into the shields slot, so a
    // banked resistance (a timed-out / resisted draft) reads clearly. On arrival
    // the row flashes + bounces the freshly-added ♥. Purely cosmetic.
    flyShieldToSlot() {
      try {
        const el = document.createElement('div');
        el.className = 'cf-shield-fly is-pop';
        el.textContent = '🛡';
        el.style.transform = 'translate(-50%,-50%)';
        document.body.appendChild(el);
        const sx = window.innerWidth / 2, sy = window.innerHeight * 0.42;
        el.style.left = sx + 'px'; el.style.top = sy + 'px';
        const POP = 420, FLY = 720;
        const ease = (k) => (k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2);
        let t0 = null;
        const step = (ts) => {
          if (t0 == null) t0 = ts;
          const e = ts - t0;
          if (e < POP) { requestAnimationFrame(step); return; }
          el.classList.remove('is-pop');
          const k = Math.min(1, (e - POP) / FLY);
          const r = shieldRow.getBoundingClientRect();
          const tx = r.left + Math.max(8, r.width / 2), ty = r.top + r.height / 2;
          const kk = ease(k);
          el.style.left = (sx + (tx - sx) * kk) + 'px';
          el.style.top = (sy + (ty - sy) * kk) + 'px';
          el.style.transform = `translate(-50%,-50%) scale(${1 - 0.45 * kk})`;
          el.style.opacity = String(1 - 0.2 * kk);
          if (k < 1) { requestAnimationFrame(step); return; }
          el.remove();
          shieldRow.classList.remove('cf-shield-flash'); void shieldRow.offsetWidth; shieldRow.classList.add('cf-shield-flash');
          shieldRow.classList.remove('cf-shield-bounce'); void shieldRow.offsetWidth; shieldRow.classList.add('cf-shield-bounce');
        };
        requestAnimationFrame(step);
      } catch (e) { /* ignore */ }
    },
    // Grab-in-the-tube: a grabbed power-up's art flies from the tube grab point
    // (screen px) to its HUD slot - the bottom dock for a consumable, the top pick
    // strip for a relic (passive) - then shrinks in. Purely cosmetic; the item is
    // already docked/applied by the time this runs.
    flyArtToSlot(id, fromPos, target = 'relic') {
      try {
        const dest = target === 'consumable' ? dock : picksWrap;
        const el = document.createElement('img');
        el.src = `${ART}boons/${id}.png`;
        el.alt = '';
        el.addEventListener('error', () => { el.remove(); });
        const S = 108;
        Object.assign(el.style, {
          position: 'fixed', width: S + 'px', height: S + 'px', left: '0', top: '0',
          transform: 'translate(-50%,-50%)', zIndex: '99999', pointerEvents: 'none',
          borderRadius: '12px', boxShadow: '0 0 22px rgba(220,180,255,0.7)', opacity: '1',
        });
        document.body.appendChild(el);
        const sx = (fromPos && fromPos.x) || window.innerWidth / 2;
        const sy = (fromPos && fromPos.y) || window.innerHeight / 2;
        el.style.left = sx + 'px'; el.style.top = sy + 'px';
        const FLY = 620;
        const ease = (k) => (k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2);
        let t0 = null;
        const step = (ts) => {
          if (t0 == null) t0 = ts;
          const k = Math.min(1, (ts - t0) / FLY);
          const r = dest.getBoundingClientRect();
          const tx = (r.left || window.innerWidth / 2) + (r.width ? r.width / 2 : 0);
          const ty = (r.top || window.innerHeight) + (r.height ? r.height / 2 : 0);
          const kk = ease(k);
          el.style.left = (sx + (tx - sx) * kk) + 'px';
          el.style.top = (sy + (ty - sy) * kk) + 'px';
          el.style.transform = `translate(-50%,-50%) scale(${1 - 0.68 * kk})`;
          el.style.opacity = String(1 - 0.3 * kk);
          if (k < 1) { requestAnimationFrame(step); return; }
          el.remove();
          dest.classList.remove('cf-slot-flash'); void dest.offsetWidth; dest.classList.add('cf-slot-flash');
        };
        requestAnimationFrame(step);
      } catch (e) { /* ignore */ }
    },
    resetCombo() { prevCombo = null; },   // fresh run without a hide (recap "Again"): no phantom break
    setVisible(v) {
      root.style.display = v ? '' : 'none';
      picksWrap.style.display = v ? '' : 'none';
      dock.style.display = v ? '' : 'none';
      eyeBtn.style.display = v ? '' : 'none';
      if (!v) {
        // leaving a run: clear the Eye's hide state so the next run starts shown
        hud.classList.remove('dtrh-hud-hidden');
        eyeBtn.classList.remove('is-off'); eyeBtn.textContent = '👁';
        prevCombo = null; tips.hideNow(); // next run's first update() sets the baseline silently
      }
      syncEmbers();
    },
    dispose() {
      clearTimeout(pulseTimer);
      clearInterval(emberTimer);
      tips.dispose();
      root.remove(); annWrap.remove(); toastWrap.remove(); pulseEl.remove();
      picksWrap.remove(); dock.remove(); eyeBtn.remove(); heatTint.remove();
    },
  };
}
