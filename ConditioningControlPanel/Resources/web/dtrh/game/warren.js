/* ============================================================================
 * warren.js - the Warren: DtRH's hub, reworked (2026-07) as an IN-AMBIENT 3D
 * menu. The old full-screen DOM SPA (menu screen + six dollhouse tabs) is gone:
 * the idling tube IS the menu. engine/hubStations.js hangs floating stations in
 * the bore and this module decides what they are and what clicking one means:
 *
 *   🕳 FALL IN (portal)  dive straight into a descent (difficulty + length
 *                        chips sit under it - everything else lives in ⚙ options)
 *   🧸 TOYBOX            level things up with drops ✦ (shelves + habits + hands)
 *   🎛 THE DIALS         unlock things with gold 🪙 (the options-console ladder)
 *   📔 VANITY            the mirror: mantra, stats, rank, diary
 *
 * Clicking a station swings the camera onto it (hubStations.focus) and opens a
 * compact DOM sheet beside it; Esc / click-away closes. Persistent chrome is
 * tiny: currency chips (top-right), a corner dock (options / how to / wake up).
 *
 * Everything still renders from the chaos_meta snapshot through metaView();
 * all writes are meta-commands (the bridge answers with a fresh snapshot,
 * which re-renders). reveals.js keeps surfaces hidden until earned - freshly
 * pending STATION reveals flash in 3D (hubStations.flashStation).
 * ==========================================================================*/

import {
  UPGRADES, upgradeById, BRANCH_LABEL, BRANCH_COLOR,
  LIFETIME_BOONS, boonDefById, boonsInCat,
  BENCH_ITEMS, BENCH_RESERVED, BENCH_CLAIMED_RESERVED,
  WALL_TIP, DEEPER_TIP, BOTTOM_TIP,
  RANKS, RANK, GLYPHS,
  DIARY_VERBS, DIARY_CODEX, DIFF_PILLS,
  HOWTO_CARDS, metaView, MAX_CONSUMABLE_SLOTS,
} from './catalog.js';
import { BOONS } from './boons.js';
import * as reveals from './reveals.js';
import { UNLOCK_LADDER, RANK_NAMES } from '../engine/settings.js';

const ART = 'https://ccp.art/';
const nfmt = (n) => Math.floor(n).toLocaleString();

// A boon's identity colour: its rarity tier, red for a curse, gold once maxed.
const RARITY_ACC = { Common: '150,162,196', Uncommon: '90,214,150', Rare: '167,120,255' };
const CURSE_ACC = '255,110,110';
const GOLD_ACC = '255,215,0';
function boonAcc(b, maxed) {
  if (maxed) return GOLD_ACC;
  if (b && b.curse) return CURSE_ACC;
  return (b && RARITY_ACC[b.rarity]) || '232,67,147';
}
function rarityTag(b) {
  if (!b) return null;
  if (b.curse) return 'curse';
  if (b.rarity && b.rarity !== 'Common') return b.rarity.toLowerCase();
  return null;
}

function fmtPlaytime(seconds) {
  const s = Math.max(0, seconds | 0);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60);
  return h >= 1 ? `${h}h ${m}m` : `${m}m ${s % 60}s`;
}

// The stations: what hangs in the tube. TOYBOX/DIALS appear after the first
// fall (the 'dollhouse' reveal); VANITY keeps the old Looking-Glass gate.
const STATION_META = {
  toybox: { label: 'TOYBOX', glyph: '🧸', accent: 0x66e0d0, sub: 'level up · drops ✦' },
  dials:  { label: 'THE DIALS', glyph: '🎛', accent: 0xe84393, sub: 'unlock · gold 🪙' },
  vanity: { label: 'VANITY', glyph: '📔', accent: 0xc178ff, sub: 'the mirror' },
};

export function createWarren({ hud, bridge, stations, getMeta, getMediaStats, runSetup, onDescend, onExit, onOptions }) {
  const root = document.createElement('div');
  root.className = 'wr-root';
  root.hidden = true;
  hud.appendChild(root);

  const sfx = (name, scale = 0.45) => bridge.send({ type: 'sfx', name, scale });
  const bark = (event, data) => bridge.send({ type: 'bark', event, ...(data || {}) });
  const cmd = (op, extra) => bridge.send({ type: 'meta-command', op, ...(extra || {}) });
  const setFlag = (key) => cmd('set-flag', { key });

  // ---- local run-setup state (seeded from the saved settings in init) ----
  // Four Chambers: the descent is always 4 regions (I->IV), each ~3-5 min and
  // ending in a boon Landing. Only difficulty + length are picked here at the
  // portal; the rest of the old DESCENT tab lives in ⚙ options now.
  const CHAMBER_TOTALS = [720, 960, 1200];   // 12 / 16 / 20 min (3 / 4 / 5 min per chamber)
  const setup = Object.assign({
    difficulty: 'Easy', durationSec: 960, waveCount: 4, motion: 'Mixed',
    enabledVariants: null, effectIntensity: 0.85, colorFlashes: true,
    boonDraftEnabled: true, allowCurses: true, dartersEnabled: true,
    key1: 'Q', key2: 'E',
  }, runSetup || {});
  setup.waveCount = 4;
  if (!CHAMBER_TOTALS.includes(setup.durationSec)) setup.durationSec = 960;

  let visible = false;
  let openId = null;         // station whose sheet is open (null = idle)
  let descending = false;    // a request-run is in flight
  let modal = null;          // open modal element (howto / intro)
  const revealEls = new Map();       // reveal id -> DOM element to flash (in-sheet rows)
  // reveal id -> station to flash in 3D (station-level surfaces)
  const STATION_REVEALS = { dollhouse: ['toybox', 'dials'], tab_looking_glass: ['vanity'] };

  const view = () => metaView(getMeta());

  // ============================ small builders ============================

  const el = (cls, parent, text) => {
    const d = document.createElement('div');
    d.className = cls;
    if (text != null) d.textContent = text;
    if (parent) parent.appendChild(d);
    return d;
  };
  const btn = (cls, label, onClick, parent) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = cls;
    b.textContent = label;
    b.addEventListener('click', () => { sfx('ui_click', 0.3); onClick(); });
    if (parent) parent.appendChild(b);
    return b;
  };

  /** Square art icon with glyph fallback (the WPF ArtIcon). */
  function artIcon(url, glyph, accent, size) {
    const box = el('wr-icon');
    box.style.width = box.style.height = `${size}px`;
    box.style.setProperty('--acc', accent || '232,67,147');
    const img = document.createElement('img');
    img.src = url;
    img.alt = '';
    img.addEventListener('error', () => {
      img.remove();
      el('wr-icon-glyph', box, glyph || '◈');
    });
    box.appendChild(img);
    return box;
  }

  function tip(elm, text) { if (text) elm.title = text; }

  // ============================ unlock cards ============================

  const cardLayer = el('wr-cards', root);
  const cardQueue = [];
  let cardShowing = false;

  function showUnlockCard(data) {
    if (!data) return;
    cardQueue.push(data);
    if (!cardShowing) nextUnlockCard();
  }

  function nextUnlockCard() {
    const data = cardQueue.shift();
    if (!data) { cardShowing = false; return; }
    cardShowing = true;
    sfx(data.cue || 'unlock_card', 0.6);
    const card = el('wr-unlock');
    card.style.setProperty('--acc', data.accent || '232,67,147');
    el('wr-unlock-ribbon', card, data.ribbon);
    const row = el('wr-unlock-row', card);
    if (data.art) row.appendChild(artIcon(data.art, data.glyph, data.accent, 64));
    else {
      const g = el('wr-icon', row);
      g.style.width = g.style.height = '64px';
      g.style.setProperty('--acc', data.accent || '232,67,147');
      el('wr-icon-glyph', g, data.glyph || '◈');
    }
    const txt = el('wr-unlock-text', row);
    el('wr-unlock-title', txt, data.title);
    el('wr-unlock-desc', txt, data.desc);
    if (data.flavor) el('wr-unlock-flavor', txt, data.flavor);
    if (data.context) el('wr-unlock-context', txt, '→ ' + data.context);
    cardLayer.appendChild(card);
    requestAnimationFrame(() => card.classList.add('is-in'));
    window.setTimeout(() => {
      card.classList.remove('is-in');
      window.setTimeout(() => { card.remove(); nextUnlockCard(); }, 300);
    }, 4500);
  }

  const cardForCapstone = (id) => {
    const b = boonDefById(id);
    if (!b || !b.capstone) return null;
    return { ribbon: 'CAPSTONE REACHED', accent: '255,200,60', title: b.name, desc: b.capstone,
      flavor: b.flavor, context: 'fully deepened — its final gift is yours.', glyph: b.glyph,
      art: `${ART}boons/${id}.png`, cue: 'capstone_reached' };
  };
  const cardForHabit = (id) => {
    const u = upgradeById(id);
    if (!u) return null;
    return { ribbon: 'HABIT TRAINED', accent: '156,232,160', title: u.name, desc: u.desc, flavor: u.flavor,
      context: 'always on from your next descent — switch it off in the toybox anytime.',
      glyph: u.glyph, art: `${ART}upgrades/${id}.png` };
  };

  // ============================ modals (how-to / intro) ============================

  function closeModal() {
    if (modal) { modal.remove(); modal = null; }
  }

  function openHowTo() {
    closeModal();
    modal = el('wr-modal', root);
    const card = el('wr-modal-card wr-howto', modal);
    let idx = 0;
    modal.addEventListener('click', (e) => { if (e.target === modal) closeModal(); });

    const render = () => {
      card.innerHTML = '';
      const c = HOWTO_CARDS[idx];
      el('wr-howto-step', card, `STEP ${idx + 1} / ${HOWTO_CARDS.length}`);
      el('wr-howto-title', card, c.title);
      const img = document.createElement('img');
      img.className = 'wr-howto-img';
      img.src = `${ART}howto/${c.image}.png`;
      img.alt = '';
      img.addEventListener('error', () => img.remove());
      card.appendChild(img);
      const body = el('wr-howto-body', card);
      for (const line of c.lines) {
        const row = el('wr-howto-line', body);
        if (line.emoji) {
          const g = el('wr-howto-emoji', row, line.emoji);
          if (line.color) g.style.color = `rgb(${line.color})`;
        }
        const p = el('wr-howto-text', row);
        if (line.lead) {
          const lead = document.createElement('strong');
          lead.textContent = line.lead + '  ';
          if (line.color) lead.style.color = `rgb(${line.color})`;
          p.appendChild(lead);
        }
        // **bold** spans
        let bold = false;
        for (const part of line.body.split('**')) {
          if (part) {
            const span = document.createElement(bold ? 'b' : 'span');
            span.textContent = part;
            p.appendChild(span);
          }
          bold = !bold;
        }
      }
      const dots = el('wr-howto-dots', card);
      HOWTO_CARDS.forEach((_, i) => el('wr-dot' + (i === idx ? ' is-on' : ''), dots));
      const nav = el('wr-howto-nav', card);
      const back = btn('sf-btn', '‹  BACK', () => { if (idx > 0) { idx--; render(); } }, nav);
      back.style.visibility = idx > 0 ? 'visible' : 'hidden';
      btn('sf-btn sf-btn-primary', idx < HOWTO_CARDS.length - 1 ? 'NEXT  ›' : 'DONE', () => {
        if (idx < HOWTO_CARDS.length - 1) { idx++; render(); } else closeModal();
      }, nav);
    };
    render();
  }

  /** "The invitation" - the one-time spoiler-free verb primer. Now fires on the
   * FIRST portal click (before the first fall), where the verbs actually help. */
  function openIntro(onDone) {
    closeModal();
    modal = el('wr-modal', root);
    const card = el('wr-modal-card wr-intro', modal);
    const hero = document.createElement('img');
    hero.className = 'wr-intro-hero';
    hero.src = `${ART}guide/intro.png`;
    hero.alt = '';
    hero.addEventListener('error', () => hero.remove());
    card.appendChild(hero);
    el('wr-intro-title', card, '🐇 DOWN THE RABBIT HOLE');
    el('wr-intro-sub', card, 'you don’t have to understand it. you just have to fall.');
    const rules = el('wr-intro-rules', card);
    const rule = (glyph, color, verb, head, rest) => {
      const row = el('wr-intro-rule', rules);
      const g = el('wr-intro-glyph', row, glyph);
      g.style.color = `rgb(${color})`;
      const col = el('wr-intro-col', row);
      if (verb) {
        const pill = el('wr-intro-verb', col, verb);
        pill.style.color = `rgb(${color})`;
        pill.style.borderColor = `rgba(${color},0.6)`;
        pill.style.background = `rgba(${color},0.2)`;
      }
      const p = el('wr-intro-text', col);
      const b = document.createElement('b');
      b.textContent = head;
      p.appendChild(b);
      p.appendChild(document.createTextNode(rest));
    };
    rule('🫧', '255,208,232', 'LEFT-CLICK', 'pop the treats. ', 'a click is enough. they feed your streak.');
    rule('◉', '255,210,40', 'PRESS & HOLD', 'hold the burning ones. ', 'press and keep pressing until they snap. let one finish its trance and it goes off.');
    rule('🌊', '122,224,255', 'RIGHT-CLICK', 'ripple the water. ', 'a right-click near the bubbles sends out a wave — treats pop, trances snap, rabbits go flying. it spends 30 focus, no cooldown — chain them while your focus holds.');
    rule('🐇', '255,105,180', null, 'follow the white rabbit. ', 'everything else down there is yours to find out.');
    btn('wr-intro-btn', 'i understand. take me down', () => { closeModal(); if (onDone) onDone(); }, card);
  }

  // ============================ reveal flash pass ============================

  function runRevealFlashes(reason) {
    const v = view();
    reveals.sync(v, bridge, reason);
    const pending = [...v.pending];
    if (!pending.length) return;
    let stagger = 0;
    let firstFlashed = null;
    for (const id of pending) {
      const stationIds = STATION_REVEALS[id];
      const elm = revealEls.get(id);
      if (stationIds && stations) {
        // a station-level surface: flash the 3D card(s) instead of a DOM row
        firstFlashed = firstFlashed || id;
        const delay = stagger;
        stagger += 600;
        window.setTimeout(() => {
          sfx('reveal_chime', 0.5);
          for (const sid of stationIds) stations.flashStation(sid);
        }, delay);
        reveals.markSeen(id, v, bridge);
      } else if (elm && elm.isConnected && !elm.hidden) {
        firstFlashed = firstFlashed || id;
        const delay = stagger;
        stagger += 600;
        window.setTimeout(() => {
          sfx('reveal_chime', 0.5);
          elm.classList.add('wr-reveal-flash');
          window.setTimeout(() => elm.classList.remove('wr-reveal-flash'), 3100);
        }, delay);
        reveals.markSeen(id, v, bridge);
      } else {
        reveals.markSeen(id, v, bridge);   // no visible surface: settle silently
      }
    }
    if (firstFlashed) bark('reveal-flash', { id: firstFlashed });
  }

  // ============================ affordable-count badges ============================

  function countAffordableToybox(v) {
    let n = 0;
    for (const u of UPGRADES) {
      if (!v.isOwned(u.id) && !v.isPurchaseRankLocked(u.id) && v.canAffordUpgrade(u.id)) n++;
    }
    for (const b of LIFETIME_BOONS) {
      const lvl = v.boonLevel(b.id);
      if (lvl >= 1 && lvl < b.levelValues.length && !v.isCapstoneRankLocked(b.id) && v.canAffordDeepen(b.id)) n++;
    }
    return n;
  }

  function countAffordableDials(v) {
    let n = 0;
    for (const r of UNLOCK_LADDER) {
      if (v.hasDial(r.id) || v.isDialRankLocked(r.rankReq)) continue;
      if (v.canAffordDial(r.id, r.price)) n++;
    }
    return n;
  }

  function countAffordableBench(v) {
    let n = 0;
    for (const item of BENCH_ITEMS) {
      if (v.bench.has(item.id)) continue;
      if (item.rankNeed != null && !v.atLeast(item.rankNeed)) continue;
      if (item.revealGate && !reveals.isUnlocked(item.revealGate, v)) continue;
      if (v.gold >= item.cost) n++;
    }
    return n;
  }

  // ============================ the 3D stations ============================

  function buildStationDefs(v) {
    const defs = [];
    if (v.runs >= 1) {
      defs.push({ id: 'toybox', kind: 'station', label: STATION_META.toybox.label,
        glyph: STATION_META.toybox.glyph, accent: STATION_META.toybox.accent,
        artUrl: `${ART}hub/station_toybox.png`, badge: countAffordableToybox(v) });
      defs.push({ id: 'dials', kind: 'station', label: STATION_META.dials.label,
        glyph: STATION_META.dials.glyph, accent: STATION_META.dials.accent,
        artUrl: `${ART}hub/station_dials.png`, badge: countAffordableDials(v) });
    }
    if (reveals.isUnlocked('tab_looking_glass', v)) {
      defs.push({ id: 'vanity', kind: 'station', label: STATION_META.vanity.label,
        glyph: STATION_META.vanity.glyph, accent: STATION_META.vanity.accent,
        artUrl: `${ART}hub/station_vanity.png`, badge: countAffordableBench(v) });
    }
    defs.push({ id: 'portal', kind: 'portal', label: 'FALL IN', glyph: '🕳', accent: 0xe84393 });
    return defs;
  }

  function refreshBadges(v) {
    if (!stations) return;
    stations.setBadge('toybox', countAffordableToybox(v));
    stations.setBadge('dials', countAffordableDials(v));
    stations.setBadge('vanity', countAffordableBench(v));
  }

  // ============================ persistent chrome ============================

  let chrome = null;   // { chips, fall, hint, dock }

  function renderChrome() {
    root.innerHTML = '';
    root.appendChild(cardLayer);
    const v = view();

    // top-left: a quiet title
    const title = el('wr-corner-title', root);
    el('wr-corner-name', title, 'DOWN THE RABBIT HOLE');
    el('wr-corner-sub', title, 'the fall is the easy part.');

    // top-right: rank + currencies
    const chips = el('wr-chips wr-chips--corner', root);

    // bottom-center: the portal caption + difficulty/length chips
    const fall = el('wr-fall', root);

    // bottom-left: the corner dock
    const dock = el('wr-dock', root);
    if (onOptions) btn('wr-dock-btn', '⚙ options', () => onOptions(), dock);
    btn('wr-dock-btn', 'how to play', () => openHowTo(), dock);
    btn('wr-dock-btn wr-dock-btn--dim', 'wake up (exit)', () => onExit(), dock);

    // bottom hint line
    const hint = el('wr-hub-hint', root);

    chrome = { chips, fall, hint, dock };
    refreshChrome(v);
    if (modal) root.appendChild(modal);
  }

  function refreshChrome(v) {
    if (!chrome) return;
    // currency chips
    chrome.chips.innerHTML = '';
    el('wr-chip', chrome.chips, v.rankName);
    el('wr-chip', chrome.chips, `${GLYPHS.drops} ${nfmt(v.sparks)}`);
    el('wr-chip', chrome.chips, `${GLYPHS.gold} ${nfmt(v.gold)}`);

    // FALL IN caption + chips under the portal
    chrome.fall.innerHTML = '';
    el('wr-fall-cap', chrome.fall, descending ? 'she opens the hole…' : 'FALL IN');
    if (!descending) {
      const diffRow = el('wr-pills wr-pills--center', chrome.fall);
      let effDiff = setup.difficulty;
      const diffAvailable = DIFF_PILLS.filter((d) => (!d.revealGate || reveals.isUnlocked(d.revealGate, v)));
      if (!diffAvailable.some((d) => d.id === effDiff)) effDiff = 'Easy';
      if (effDiff === 'Extreme' && !v.extremeUnlocked) effDiff = 'Hard';
      setup.difficulty = effDiff;
      for (const d of DIFF_PILLS) {
        if (d.revealGate && !reveals.isUnlocked(d.revealGate, v)) continue;
        const locked = d.extremeGate && !v.extremeUnlocked;
        const p = btn('wr-seg' + (effDiff === d.id ? ' is-on' : '') + (locked ? ' is-locked' : ''),
          locked ? `${d.label} 🔒` : d.label, () => {
            if (locked) { sfx('ui_denied', 0.4); return; }
            setup.difficulty = d.id;
            refreshChrome(view());
          }, diffRow);
        if (locked) {
          tip(p, `a deeper door. she sells the key in the TOYBOX: finish 10 relentless descents, reach Devoted (${RANKS.thresholds[RANK.Devoted]} descents), then train it for ${GLYPHS.drops}${upgradeById('extreme_tier')?.cost ?? 350}.`);
        } else tip(p, d.tip);
        if (d.revealGate) revealEls.set(d.revealGate, p);
        if (d.extremeGate) revealEls.set('pill_inescapable', p);
      }
      const lenRow = el('wr-pills wr-pills--center', chrome.fall);
      for (const secs of CHAMBER_TOTALS) {
        btn('wr-seg wr-seg--small' + (setup.durationSec === secs ? ' is-on' : ''), `${secs / 60} min`, () => {
          setup.durationSec = secs;
          refreshChrome(view());
        }, lenRow);
      }
      tip(lenRow, 'four chambers, always in order. each runs about a quarter of the descent and ends in a boon.');
    }

    // hint
    chrome.hint.textContent = v.runs === 0
      ? 'your first descent is a gentle one. she’ll show you the verbs. click the hole to fall.'
      : `${v.runs} descents finished · click the hole to fall · hold ESC to wake up`;
  }

  // ============================ the station sheet ============================

  let sheet = null;

  function closeSheet() {
    if (sheet) { sheet.remove(); sheet = null; }
    if (openId && stations) stations.blur();
    openId = null;
  }

  function openStation(id) {
    if (!STATION_META[id]) return;
    const v = view();
    closeSheet();
    openId = id;
    if (stations) stations.focus(id, 1.7);   // station rests left-of-center; sheet on the right
    sheet = el('wr-sheet', root);
    const head = el('wr-sheet-head', sheet);
    el('wr-sheet-title', head, STATION_META[id].label);
    el('wr-sheet-sub', head, STATION_META[id].sub);
    btn('wr-sheet-close', '✕', () => closeSheet(), head);
    const body = el('wr-sheet-body', sheet);
    if (id === 'toybox') renderToyboxPanel(body, v);
    else if (id === 'dials') renderDialsPanel(body, v);
    else if (id === 'vanity') renderVanityPanel(body, v);

    // first-ever station visit: her welcome + the reveal pass
    if (!v.seenDollhouse) {
      setFlag('seenDollhouse');
      bark('dollhouse-first-open');
    }
    runRevealFlashes('hub_open');
  }

  /** Re-render the open sheet in place (a meta-command answered). */
  function refreshSheet() {
    if (!openId || !sheet) return;
    const v = view();
    const body = sheet.querySelector('.wr-sheet-body');
    if (!body) return;
    const scrollTop = body.scrollTop;
    body.innerHTML = '';
    if (openId === 'toybox') renderToyboxPanel(body, v);
    else if (openId === 'dials') renderDialsPanel(body, v);
    else if (openId === 'vanity') renderVanityPanel(body, v);
    body.scrollTop = scrollTop;
  }

  // ============================ shared row/tile builders ============================

  function buyBtn(label, enabled, onClick) {
    const b = btn('wr-buy' + (enabled ? '' : ' is-off'), label, () => { if (enabled) onClick(); else sfx('ui_denied', 0.45); });
    return b;
  }

  /** One Toybox habit row (BuildUpgradeRow). */
  function upgradeRow(u, v) {
    const owned = v.isOwned(u.id);
    const on = owned && v.isUpgradeActive(u.id);
    const accent = BRANCH_COLOR[u.branch] || '232,67,147';
    const row = el('wr-row' + (on ? ' is-on' : owned ? ' is-owned' : ''));
    row.style.setProperty('--acc', accent);
    const icon = artIcon(`${ART}upgrades/${u.id}.png`, u.glyph, accent, 72);
    if (!owned) icon.style.opacity = '0.55';
    row.appendChild(icon);
    const mid = el('wr-row-mid', row);
    el('wr-row-name', mid, u.name);
    el('wr-row-desc', mid, u.desc);
    if (u.flavor) el('wr-row-flavor', mid, u.flavor);
    el('wr-row-tag', mid, BRANCH_LABEL[u.branch] || '').style.color = `rgba(${accent},0.8)`;
    const right = el('wr-row-right', row);
    if (owned) {
      if (on) el('wr-row-state', right, 'ON ✓');
      btn('wr-pill', on ? 'switch off' : 'switch on', () => {
        cmd('toggle-upgrade', { id: u.id, on: !on });
        sfx(!on ? 'ui_equip' : 'ui_unequip', 0.45);
      }, right);
    } else {
      const rankLocked = v.isPurchaseRankLocked(u.id);
      const b = buyBtn(`Train  ${GLYPHS.drops}${u.cost}`, v.canAffordUpgrade(u.id) && !rankLocked, () => {
        cmd('purchase-upgrade', { id: u.id, cost: u.cost });
        showUnlockCard(cardForHabit(u.id));
        window.setTimeout(() => runRevealFlashes('purchase'), 300);
      });
      if (rankLocked) tip(b, `${RANKS.lockedTip}\n${RANKS.specifics(RANK.Devoted, v.runs)}`);
      right.appendChild(b);
    }
    return row;
  }

  /** One lifetime-boon row: discovered pieces deepen inline with Sparks; the
   * undiscovered stay mystery keyholes that only the fall can open. */
  function boonRow(b, v) {
    const level = v.boonLevel(b.id);
    const unlocked = level >= 1;                 // discovered
    const maxed = unlocked && level >= b.levelValues.length;
    const rankLocked = v.isBoonRankLocked(b.id);
    const accent = !unlocked ? '232,67,147' : boonAcc(b, maxed);
    const row = el('wr-row' + (unlocked ? ' is-owned' : ''));
    row.style.setProperty('--acc', accent);

    // Undiscovered = a MYSTERY: keyhole, "???", never the boon's face.
    const icon = unlocked
      ? artIcon(`${ART}boons/${b.id}.png`, b.glyph, accent, 84)
      : artIcon(`${ART}hub/tile_unknown.png`, '?', accent, 84);
    if (!unlocked) icon.style.opacity = '0.6';
    row.appendChild(icon);

    const mid = el('wr-row-mid', row);
    el('wr-row-name', mid, unlocked ? `${b.name} · L${level}` : '? ? ?');
    if (!unlocked) {
      el('wr-row-flavor', mid, rankLocked
        ? `${RANKS.lockedTip} ${RANKS.specifics(b.rankFloor, v.runs)}`
        : 'it waits in the fall. grab it once to keep it — then deepen it here.');
    } else {
      el('wr-row-desc', mid, b.desc);
      if (b.flavor) el('wr-row-flavor', mid, b.flavor);
      if (b.activeUse) {
        const useHint = b.cooldownSec > 0 ? `${b.cooldownSec}s cooldown` : 'limited uses';
        el('wr-row-active', mid, `CONSUMABLE · grab it in the fall, fire from the dock · ${useHint}`);
      }
      if (b.capstone) el('wr-row-capstone' + (maxed ? ' is-maxed' : ''), mid, 'max: ' + b.capstone);
      const pips = el('wr-row-pips', mid);
      for (let i = 1; i <= b.levelValues.length; i++) {
        el('wr-pip' + (i <= level ? ' is-full' : ''), pips, i <= level ? '●' : '○');
      }
      el('wr-pip-value', pips, '  ' + b.value(b.levelValues[level - 1]));
    }

    const right = el('wr-row-right', row);
    if (!unlocked) {
      const held = buyBtn(rankLocked ? `🔒 ${RANKS.name(b.rankFloor)}` : '↯ find it below', false, () => {});
      tip(held, rankLocked
        ? `${RANKS.lockedTip}\n${RANKS.specifics(b.rankFloor, v.runs)}`
        : 'grab this card as you fall to discover it.');
      right.appendChild(held);
    } else if (maxed) {
      el('wr-row-max', right, 'MAX  ✓');
    } else {
      const cost = v.nextUpgradeCostOf(b.id) ?? 0;
      const capLocked = v.isCapstoneRankLocked(b.id);
      const deepen = buyBtn(`deepen  ${GLYPHS.drops}${cost}`, !capLocked && v.canAffordDeepen(b.id), () => {
        cmd('set-lifetime-boon', { id: b.id, level: level + 1, cost });
        const willMax = level + 1 >= b.levelValues.length;
        if (willMax && b.capstone) window.setTimeout(() => showUnlockCard(cardForCapstone(b.id)), 250);
        else sfx('ui_deepen', 0.5);
      });
      if (capLocked) tip(deepen, `${RANKS.capstoneLockedTip}\n${RANKS.specifics(RANK.Devoted, v.runs)}`);
      right.appendChild(deepen);
    }
    return row;
  }

  function hazyRow(name, tipText) {
    const row = el('wr-row wr-row--hazy');
    el('wr-hazy-mark', row, '▢');
    el('wr-hazy-name', row, name);
    tip(row, tipText);
    return row;
  }

  /** A Display-Shelf card (used for the HANDS strip). */
  function shelfCard({ art, glyph, title, acc, state, corner, ribbon, foot, pips, tipText, onClick }) {
    const card = el(`wr-shelf is-${state}`);
    card.style.setProperty('--acc', acc || '232,67,147');
    const artBox = el('wr-shelf-art', card);
    if (state === 'mystery') {
      const img = document.createElement('img');
      img.className = 'wr-shelf-keyhole'; img.src = `${ART}hub/tile_unknown.png`; img.alt = '';
      img.addEventListener('error', () => { img.remove(); el('wr-shelf-glyph', artBox, '?'); });
      artBox.appendChild(img);
    } else if (art && state !== 'empty') {
      const img = document.createElement('img');
      img.src = art; img.alt = '';
      img.addEventListener('error', () => { img.remove(); el('wr-shelf-glyph', artBox, glyph || '◈'); });
      artBox.appendChild(img);
    } else {
      el('wr-shelf-glyph', artBox, glyph || '◈');
    }
    if (ribbon) el('wr-shelf-ribbon', artBox, ribbon);
    if (corner) el('wr-shelf-corner', artBox, corner);
    const footEl = el('wr-shelf-foot', card);
    el('wr-shelf-name', footEl, title);
    const line = el('wr-shelf-line', footEl);
    if (pips && pips.max > 0) {
      const p = el('wr-shelf-pips', line);
      for (let i = 1; i <= pips.max; i++) el('wr-pip' + (i <= pips.level ? ' is-full' : ''), p, i <= pips.level ? '●' : '○');
    }
    if (foot) el('wr-shelf-sub', line, foot);
    if (onClick) {
      card.classList.add('is-click');
      card.addEventListener('click', () => { sfx('ui_click', 0.3); onClick(); });
    }
    tip(card, tipText || title);
    return card;
  }

  // ============================ 🧸 TOYBOX (level up · Sparks) ============================

  function renderToyboxPanel(panel, v) {
    // ---- hands (consumable HUD slots) ----
    const handsCard = el('wr-card', panel);
    el('wr-card-h', handsCard, 'HANDS');
    el('wr-card-sub', handsCard, `${v.consumableSlots} consumable slot${v.consumableSlots === 1 ? '' : 's'} · grab toys in the fall and fire them from the dock.`);
    const handsRow = el('wr-pockets', handsCard);
    for (let i = 0; i < MAX_CONSUMABLE_SLOTS; i++) {
      const owned = i < v.consumableSlots;
      handsRow.appendChild(shelfCard({
        glyph: owned ? '✋' : '＋', title: owned ? `slot ${i + 1}` : 'locked',
        state: owned ? 'owned' : 'empty', foot: owned ? 'ready' : 'sew below',
        tipText: owned ? 'a hand free to hold a grabbed toy' : 'buy another hand below',
      }));
    }
    const slotCost = v.nextConsumableSlotCost();
    if (slotCost != null) {
      const buy = buyBtn(`sew a hand  ${GLYPHS.drops}${slotCost}`, v.canBuyConsumableSlot(), () => {
        cmd('buy-consumable-slot', { cost: slotCost });
        sfx('ui_deepen', 0.5);
      });
      handsCard.appendChild(buy);
    } else {
      el('wr-card-sub', handsCard, 'both hands and then some — you can hold no more.');
    }

    // ---- the shelves (discovery-gated; deepen inline) ----
    const shelves = [
      ['TOYS · you press them mid-descent', 'skill', 'toybox_toys'],
      ['ACCESSORIES · they shape the whole fall', 'accessory', 'toybox_accessories'],
      ['CHARMS · they work every descent', 'utility', null],
    ];
    for (const [label, cat, gate] of shelves) {
      const boons = boonsInCat(cat).filter((b) => v.onShelfNow(b.id));
      if (!boons.length) continue;
      const card = el('wr-card', panel);
      const hdr = el('wr-card-h', card, label);
      if (gate) revealEls.set(gate, hdr);
      const found = boons.filter((b) => v.boonLevel(b.id) >= 1).length;
      el('wr-card-sub', card, `${found}/${boons.length} discovered · grab the rest in the fall`);
      for (const b of boons) card.appendChild(boonRow(b, v));
    }

    // ---- habits (trained here with Sparks; on/off toggle — not grabbed) ----
    const habits = el('wr-card', panel);
    el('wr-card-h', habits, 'HABITS · train them once, wear them always');
    for (const u of UPGRADES) if (v.onShelfNow(u.id)) habits.appendChild(upgradeRow(u, v));
  }

  // ============================ 🎛 THE DIALS (unlock · gold) ============================
  // The gear-panel controls start locked; each rung here flips one open. The two
  // feral dials (hydra generations, glitch timer) also need a meta-rank. The
  // panel reveals the control the moment the snapshot re-broadcasts.
  function renderDialsPanel(panel, v) {
    const card = el('wr-card', panel);
    el('wr-card-h', card, 'THE DIALS · take the console back');
    for (const r of UNLOCK_LADDER) {
      const owned = v.hasDial(r.id);
      const rankLocked = !owned && v.isDialRankLocked(r.rankReq);
      const row = el('wr-row' + (owned ? ' is-owned' : ''), card);
      row.style.setProperty('--acc', '232,67,147');
      const mid = el('wr-row-mid', row);
      el('wr-row-name', mid, r.label);
      if (r.rankReq != null) el('wr-row-flavor', mid, `a deep dial · opens at ${RANK_NAMES[r.rankReq]}`);
      const right = el('wr-row-right', row);
      if (owned) {
        el('wr-row-state', right, 'YOURS ✓');
      } else if (rankLocked) {
        const held = buyBtn(`🔒 ${RANK_NAMES[r.rankReq]}`, false, () => {});
        tip(held, `${RANKS.lockedTip}\n${RANKS.specifics(r.rankReq, v.runs)}`);
        right.appendChild(held);
      } else {
        right.appendChild(buyBtn(`unlock  ${GLYPHS.drops}${r.price}`, v.canAffordDial(r.id, r.price), () => {
          cmd('purchase-dial', { id: r.id, cost: r.price });
          bark('dial-unlocked', { id: r.id });
          window.setTimeout(() => runRevealFlashes('purchase'), 250);
        }));
      }
    }
  }

  // ============================ 📔 VANITY (the mirror) ============================

  function benchRow(item, v) {
    const revealed = !item.revealGate || reveals.isUnlocked(item.revealGate, v);
    if (!revealed) return hazyRow('???', WALL_TIP);
    const owned = v.bench.has(item.id);
    const rankShort = item.rankNeed != null && !v.atLeast(item.rankNeed);
    const row = el('wr-row wr-row--bench' + (rankShort ? ' is-dim' : ''));
    if (item.revealGate) revealEls.set(item.revealGate, row);
    el('wr-bench-glyph', row, item.glyph);
    const mid = el('wr-row-mid', row);
    el('wr-row-name', mid, item.label);
    el('wr-row-flavor', mid, item.line);
    const right = el('wr-row-right', row);
    if (owned) {
      el('wr-row-max', right, 'sewn ✓');
    } else if (rankShort) {
      el('wr-row-state', right, '🔒');
      tip(row, `${DEEPER_TIP}\n${RANKS.specifics(item.rankNeed, v.runs)}`);
    } else {
      const afford = v.gold >= item.cost;
      const b = btn('wr-buy wr-buy--gold' + (afford ? '' : ' is-short is-off'), `buy  ${GLYPHS.gold} ${nfmt(item.cost)}`, () => {
        if (!afford) { sfx('ui_denied', 0.45); return; }
        cmd('bench-buy', { id: item.id, cost: item.cost });
        window.setTimeout(() => {
          const nv = view();
          if (!nv.bench.has(item.id)) { sfx('ui_denied', 0.45); return; }
          sfx('ui_unlock', 0.55);
          runRevealFlashes('purchase');
        }, 300);
      }, right);
    }
    return row;
  }

  function renderVanityPanel(panel, v) {
    // ---- her bench (gold comforts; folds into the DIALS console in the
    //      gold-economy cutover - parked here meanwhile) ----
    const benchCard = el('wr-card', panel);
    el('wr-card-h', benchCard, 'HER BENCH · gold buys comfort, never power');
    el('wr-gold-line', benchCard, `you’re carrying ${GLYPHS.gold} ${nfmt(v.gold)}`);
    for (const item of BENCH_ITEMS) benchCard.appendChild(benchRow(item, v));
    for (const name of BENCH_RESERVED) benchCard.appendChild(hazyRow(name, WALL_TIP));
    if (v.atLeast(RANK.Devoted)) {
      for (const name of BENCH_CLAIMED_RESERVED) benchCard.appendChild(hazyRow(name, BOTTOM_TIP));
    }

    // ---- the starting mantra (bench purchase reveals it) ----
    if (reveals.isUnlocked('start_picker', v)) {
      const card = el('wr-card', panel);
      const hdr = el('wr-card-h', card, 'THE STARTING MANTRA · whispered on the way down');
      revealEls.set('start_picker', hdr);
      for (const b of BOONS) {
        const seen = v.isDiscovered('boon:' + b.id);
        const isStart = v.equippedStartBoon === b.id;
        const pickable = seen && !b.curse;
        const row = el('wr-mantra' + (isStart ? ' is-start' : '') + (b.curse ? ' is-sin' : '') + (seen ? '' : ' is-hazy'), card);
        el('wr-mantra-glyph', row, seen ? (b.curse ? '☠' : '◈') : '?');
        const mid = el('wr-row-mid', row);
        el('wr-row-name', mid, seen ? b.name : '???');
        el('wr-row-desc', mid, seen ? b.desc : 'hazy. go back down and look closer.');
        if (seen && b.flavor) el('wr-row-flavor', mid, b.flavor);
        if (seen) {
          el('wr-mantra-badge', row, isStart ? 'start ★' : b.curse ? 'taken, never chosen' : 'set start');
        }
        if (pickable) {
          row.classList.add('is-click');
          row.addEventListener('click', () => {
            sfx('ui_click', 0.3);
            cmd('equip-boon', { id: isStart ? null : b.id });
          });
        }
      }
    }

    // ---- stats (bench purchase reveals it) ----
    if (reveals.isUnlocked('stats_panel', v)) {
      const card = el('wr-card', panel);
      const hdr = el('wr-card-h', card, 'THE NUMBERS');
      revealEls.set('stats_panel', hdr);
      const grid = el('wr-stats', card);
      const stat = (label, val) => {
        const s = el('wr-stat', grid);
        el('wr-stat-v', s, val);
        el('wr-stat-l', s, label);
      };
      stat('drops carried', nfmt(v.sparks));
      stat('descents finished', nfmt(v.runs));
      stat('time under', fmtPlaytime(v.totalRunSeconds));
      stat('best score', nfmt(v.bestScore));
      stat('best streak', '×' + nfmt(v.bestCombo));
      stat('trances snapped', nfmt(v.totalDefused));
      stat('time holding on', fmtPlaytime(v.totalChannelSeconds));
    }

    // ---- how far you've fallen ----
    const rankCard = el('wr-card wr-card--rank', panel);
    el('wr-rank-word', rankCard, RANKS.lower[v.rankIndex]);
    const line = RANKS.line(v.rankIndex);
    if (line) el('wr-rank-line', rankCard, line);
    const nextIdx = v.rankIndex + 1;
    if (nextIdx < RANKS.thresholds.length) {
      el('wr-rank-next', rankCard, `${RANKS.thresholds[nextIdx] - v.runs} descents until she calls you ${RANKS.lower[nextIdx]}.`);
    }

    // ---- diary (reveal-gated; verbs + codex) ----
    if (reveals.isUnlocked('diary', v)) {
      const verbs = el('wr-card', panel);
      const hdr = el('wr-card-h', verbs, 'VERBS · how to play down there');
      revealEls.set('diary', hdr);
      for (const verb of DIARY_VERBS) {
        const row = el('wr-row wr-row--verb', verbs);
        el('wr-verb-glyph', row, verb.glyph);
        const mid = el('wr-row-mid', row);
        el('wr-row-name', mid, verb.name);
        el('wr-row-desc', mid, verb.desc);
      }
      const met = el('wr-card', panel);
      el('wr-card-h', met, 'WHAT YOU’VE MET');
      for (const c of DIARY_CODEX) {
        const seen = v.isDiscovered(c.codex);
        const row = el('wr-row wr-row--codex' + (seen ? '' : ' is-hazy'), met);
        row.style.setProperty('--acc', c.tint);
        if (seen) {
          const icon = artIcon(`${ART}bubbles/${c.codex.split(':')[1]}.png`, c.glyph, c.tint, 39);
          row.appendChild(icon);
        } else {
          el('wr-verb-glyph', row, '?');
        }
        const mid = el('wr-row-mid', row);
        el('wr-row-name', mid, seen ? c.name : '???');
        el('wr-row-desc', mid, seen ? c.desc : 'hazy. go back down and look closer.');
      }
    }
  }

  // ============================ descend ============================

  function beginDescend() {
    if (descending) return;
    descending = true;
    closeSheet();
    if (stations) stations.portalDive('portal');
    onDescend({ ...setup });
    refreshChrome(view());
  }

  function portalClicked() {
    if (descending) return;
    const v = view();
    if (!v.seenIntroGuide) {
      // the invitation: the one-time verb primer, right before the first fall
      setFlag('seenIntroGuide');
      openIntro(() => beginDescend());
    } else beginDescend();
  }

  // ============================ public surface ============================

  return {
    show() {
      visible = true;
      root.hidden = false;
      descending = false;
      openId = null;
      revealEls.clear();
      renderChrome();
      if (stations) stations.show(buildStationDefs(view()));
      runRevealFlashes('hub_open');
    },
    hide() {
      visible = false;
      root.hidden = true;
      closeSheet();
      closeModal();
      if (stations) stations.hide();
    },
    /** The meta snapshot moved (a command was answered) - refresh in place. */
    refresh() {
      if (!visible) return;
      const v = view();
      refreshChrome(v);
      refreshBadges(v);
      refreshSheet();
    },
    /** The descend request failed / was superseded - re-arm the hub. */
    descendFailed() {
      descending = false;
      if (!visible) return;
      if (stations) stations.show(buildStationDefs(view()));   // rebuild the shattered portal
      refreshChrome(view());
    },
    /** scene.js pointer routing: a station (or the portal) was clicked. */
    onStationPick(id) {
      if (!visible || descending) return;
      if (id === 'portal') { sfx('ui_click', 0.35); portalClicked(); return; }
      if (openId === id) { closeSheet(); return; }   // toggle
      sfx('ui_click', 0.3);
      openStation(id);
    },
    /** scene.js pointer routing: a click on empty tube - close what's open. */
    onStationMiss() {
      if (!visible) return;
      if (modal) return;          // modals own their own dismissal
      if (openId) closeSheet();
    },
    isVisible: () => visible,
    currentSetup: () => ({ ...setup }),
    /** Esc: close modal -> close the open sheet -> swallow (hold-to-exit works). */
    handleEsc() {
      if (!visible) return false;
      if (modal) { closeModal(); return true; }
      if (openId) { closeSheet(); return true; }
      return true;
    },
    dispose() { root.remove(); },
  };
}
