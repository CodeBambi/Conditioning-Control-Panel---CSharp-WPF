/* ============================================================================
 * warren.js - the Warren: DtRH's hub, ported from ChaosHubWindow (main menu +
 * "the Dollhouse") as a DOM SPA floating over the idling tunnel. Views:
 *
 *   MENU        FALL IN · THE DOLLHOUSE · HOW TO PLAY · wake up (exit)
 *   DOLLHOUSE   BAG (pockets + collections + habits grid) · the Toybox
 *               (habits/toys/accessories shelves + her corner) · THE DESCENT
 *               (run setup) · the Looking Glass (bench/mantras/stats) · Diary
 *
 * Everything renders from the chaos_meta snapshot through metaView(); all
 * writes are meta-commands (the bridge answers with a fresh snapshot, which
 * re-renders). The reveal framework (reveals.js) keeps the UI naked until
 * each surface's predicate flips - freshly pending surfaces flash once.
 * ==========================================================================*/

import {
  UPGRADES, upgradeById, BRANCH_LABEL, BRANCH_COLOR,
  LIFETIME_BOONS, boonDefById, boonsInCat,
  BENCH_ITEMS, BENCH_RESERVED, BENCH_CLAIMED_RESERVED,
  WALL_TIP, DEEPER_TIP, BOTTOM_TIP,
  RANKS, RANK, GLYPHS,
  DIARY_VERBS, DIARY_CODEX, POOL_VARIANTS, POOL_PRESETS, DIFF_PILLS,
  HOWTO_CARDS, metaView, MAX_CONSUMABLE_SLOTS,
} from './catalog.js';
import { BOONS } from './boons.js';
import * as reveals from './reveals.js';
import { UNLOCK_LADDER, RANK_NAMES } from '../engine/settings.js';

const ART = 'https://ccp.art/';
const KEY_OPTS = ['Q', 'E', 'R', 'F', 'Z', 'X', 'C', 'V', '1', '2', '3', '4'];
const nfmt = (n) => Math.floor(n).toLocaleString();

// A boon's identity colour: its rarity tier, red for a curse, gold once maxed.
// (Habits keep their branch colour - that's their identity.) Mirrors the loot
// palette players already read in the mid-descent draft.
const RARITY_ACC = { Common: '150,162,196', Uncommon: '90,214,150', Rare: '167,120,255' };
const CURSE_ACC = '255,110,110';
const GOLD_ACC = '255,215,0';
function boonAcc(b, maxed) {
  if (maxed) return GOLD_ACC;
  if (b && b.curse) return CURSE_ACC;
  return (b && RARITY_ACC[b.rarity]) || '232,67,147';
}
// Ribbon text for a card: skip it on plain Commons so the shelf stays calm and
// only the notable pieces (uncommon/rare/curse/maxed) wear a badge.
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

export function createWarren({ hud, bridge, getMeta, getMediaStats, runSetup, onDescend, onExit, onOptions }) {
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
  // ending in a boon Landing. Total run length is 4 x per-chamber minutes.
  const CHAMBER_TOTALS = [720, 960, 1200];   // 12 / 16 / 20 min (3 / 4 / 5 min per chamber)
  const setup = Object.assign({
    difficulty: 'Easy', durationSec: 960, waveCount: 4, motion: 'Mixed',
    enabledVariants: null, effectIntensity: 0.85, colorFlashes: true,
    boonDraftEnabled: true, allowCurses: true, dartersEnabled: true,
    key1: 'Q', key2: 'E',
  }, runSetup || {});
  // Coerce any stale saved length/loops (e.g. a pre-redesign 180s / 5-loop) into
  // the chamber model, so the first descent already feels the four-chamber pace.
  setup.waveCount = 4;
  if (!CHAMBER_TOTALS.includes(setup.durationSec)) setup.durationSec = 960;

  let visible = false;
  let screen = 'menu';       // 'menu' | 'dollhouse'
  let tab = 'loadout';       // 'loadout' | 'enhance' | 'run' | 'improve' | 'diary'
  let descending = false;    // a request-run is in flight
  let modal = null;          // open modal element (howto / intro)
  const revealEls = new Map();   // reveal id -> element to flash

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

  const cardForBoonUnlock = (id, v) => {
    const b = boonDefById(id);
    if (!b) return null;
    const ribbons = { skill: ['NEW TOY UNLOCKED', '122,255,210'], accessory: ['NEW ACCESSORY UNLOCKED', '255,210,122'], utility: ['NEW CHARM UNLOCKED', '122,224,255'] };
    const [ribbon, accent] = ribbons[b.cat] || ribbons.utility;
    const context = b.cat === 'utility' ? 'switched on — it works every descent.'
      : v.isBoonActive(id) ? 'slipped straight into a pocket — it rides with you next descent.'
      : v.slotsFor(b.cat) === 0 ? 'no pocket to carry it yet — she sells one at her bench.'
      : 'your pockets are full — swap it in from the BAG.';
    return { ribbon, accent, title: b.name, desc: b.desc, flavor: b.flavor, context, glyph: b.glyph, art: `${ART}boons/${id}.png` };
  };
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
  const cardForPocket = (isToy, label, line, n) => {
    const kind = isToy ? 'toy' : 'accessory';
    const desc = n <= 1
      ? `you can now carry one ${kind} into the descent. unlocked ${kind}s equip from the BAG.`
      : `you can now carry ${n} ${kind}s into the descent at once. pick yours from the BAG.`;
    return { ribbon: 'POCKET SEWN', accent: '232,67,147', title: label, desc, flavor: line, glyph: '👝', cue: 'pocket_sewn' };
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

  /** "The invitation" - the one-time spoiler-free intro card (first Dollhouse open). */
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
      const elm = revealEls.get(id);
      if (elm && elm.isConnected && !elm.hidden) {
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

  // ============================ menu screen ============================

  function renderMenu(host) {
    const v = view();
    const wrap = el('wr-menu', host);
    const left = el('wr-menu-left', wrap);
    el('wr-menu-title', left, 'DOWN THE RABBIT HOLE');
    el('wr-menu-sub', left, 'the fall is the easy part.');
    const chips = el('wr-chips', left);
    el('wr-chip', chips, v.rankName);
    el('wr-chip', chips, `${GLYPHS.drops} ${nfmt(v.sparks)}`);
    el('wr-chip', chips, `${GLYPHS.gold} ${nfmt(v.gold)}`);

    const btns = el('wr-menu-btns', left);
    const fall = btn('wr-menu-btn wr-menu-btn--hero', descending ? 'she opens the hole…' : 'FALL IN', () => beginDescend(), btns);
    fall.disabled = descending;
    btn('wr-menu-btn', 'THE DOLLHOUSE', () => enterDollhouse(), btns);
    if (onOptions) btn('wr-menu-btn', 'OPTIONS', () => onOptions(), btns);
    btn('wr-menu-btn', 'HOW TO PLAY', () => openHowTo(), btns);
    btn('wr-menu-btn wr-menu-btn--dim', 'wake up (exit)', () => onExit(), btns);
    el('wr-menu-hint', left, v.runs === 0
      ? 'your first descent is a gentle one. she’ll show you the verbs.'
      : `${v.runs} descents finished · hold ESC to wake up any time`);

    // Character art panel (the WPF menu flipbook's first frame; art-optional).
    const art = el('wr-menu-art', wrap);
    const img = document.createElement('img');
    img.src = `${ART}menu_1.png`;
    img.alt = '';
    img.addEventListener('error', () => art.remove());
    art.appendChild(img);
  }

  function beginDescend() {
    if (descending) return;
    descending = true;
    onDescend({ ...setup });
    rerender();
  }

  function enterDollhouse() {
    const v = view();
    screen = 'dollhouse';
    tab = 'loadout';
    rerender();
    const proceed = () => {
      if (!v.seenDollhouse) {
        setFlag('seenDollhouse');
        bark('dollhouse-first-open');
      }
      runRevealFlashes('hub_open');
    };
    if (!v.seenIntroGuide) {
      setFlag('seenIntroGuide');
      openIntro(proceed);
    } else proceed();
  }

  // ============================ dollhouse chrome ============================

  const TAB_HINTS = {
    loadout: 'click a tile to slip it into a pocket. + takes you where it’s sold.',
    enhance: 'spend your drops. deepen what you like.',
    dials: 'the fall came pre-set. buy the console back with drops - one dial at a time.',
    run: 'dress up the fall, then FALL IN.',
    improve: 'the bench, the mantras, how far you’ve fallen.',
    diary: 'everything you’ve met down there. click an entry to read it.',
  };

  function countAffordableToybox(v) {
    let n = 0;
    for (const u of UPGRADES) {
      if (!v.isOwned(u.id) && !v.isPurchaseRankLocked(u.id) && v.canAffordUpgrade(u.id)) n++;
    }
    for (const b of LIFETIME_BOONS) {
      const lvl = v.boonLevel(b.id);
      if (lvl <= 0) {
        if (!v.isBoonRankLocked(b.id) && !v.isAccessoryScriptLocked(b.id) && v.canAffordUnlock(b.id)) n++;
      } else if (lvl < b.levelValues.length && !v.isCapstoneRankLocked(b.id) && v.canAffordDeepen(b.id)) n++;
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

  function renderDollhouse(host) {
    const v = view();
    const wrap = el('wr-dollhouse', host);

    // ---- top bar ----
    const top = el('wr-topbar', wrap);
    btn('wr-back', '‹ menu', () => { screen = 'menu'; rerender(); }, top);
    el('wr-topbar-title', top, 'THE DOLLHOUSE');
    const chips = el('wr-chips', top);
    el('wr-chip', chips, v.rankName);
    el('wr-chip', chips, `${GLYPHS.drops} ${nfmt(v.sparks)}`);
    el('wr-chip', chips, `${GLYPHS.gold} ${nfmt(v.gold)}`);

    // ---- tabs ----
    const tabs = el('wr-tabs', wrap);
    const mkTab = (id, label, opts = {}) => {
      const t = btn('wr-tab' + (tab === id ? ' is-on' : ''), label, () => {
        if (opts.locked) { sfx('ui_denied', 0.4); return; }
        tab = id;
        rerender();
      }, tabs);
      if (opts.locked) t.classList.add('is-locked');
      if (opts.badge > 0) {
        const b = el('wr-badge', t, String(opts.badge));
        t.appendChild(b);
      }
      if (opts.tip) tip(t, opts.tip);
      return t;
    };
    mkTab('loadout', 'BAG');
    mkTab('enhance', 'the Toybox', { badge: countAffordableToybox(v) });
    mkTab('dials', 'THE DIALS', { badge: countAffordableDials(v) });
    mkTab('run', 'THE DESCENT');
    const lgOpen = reveals.isUnlocked('tab_looking_glass', v);
    const lgTab = mkTab('improve', lgOpen ? 'the Looking Glass' : '??? 🔒', {
      locked: !lgOpen,
      badge: lgOpen ? countAffordableBench(v) : 0,
      tip: lgOpen ? null : `${WALL_TIP}\n${RANKS.specifics(RANK.Slipping, v.runs)}`,
    });
    revealEls.set('tab_looking_glass', lgTab);
    if (reveals.isUnlocked('diary', v)) {
      const dTab = mkTab('diary', 'Diary');
      revealEls.set('diary', dTab);
    }

    // a vanished/locked tab can't stay selected
    if ((tab === 'improve' && !lgOpen) || (tab === 'diary' && !reveals.isUnlocked('diary', v))) tab = 'loadout';

    // ---- panel ----
    const panel = el('wr-panel', wrap);
    if (tab === 'loadout') renderBag(panel, v);
    else if (tab === 'enhance') renderToybox(panel, v);
    else if (tab === 'dials') renderDials(panel, v);
    else if (tab === 'run') renderDescent(panel, v);
    else if (tab === 'improve') renderLookingGlass(panel, v);
    else if (tab === 'diary') renderDiary(panel, v);

    el('wr-hint', wrap, TAB_HINTS[tab] || '');
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

  /** One Toybox lifetime-boon row (BuildLifetimeBoonRow). */
  function boonRow(b, v) {
    // Grab-in-the-tube rework: items are DISCOVERED by grabbing them in the fall, not
    // bought. Undiscovered (level 0) = a mystery keyhole; once discovered it becomes
    // level-uppable here with Sparks. Rank still gates when it can first appear in the fall.
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

  /** A BAG tile (LoadoutTile). state: 'equipped' | 'owned' | 'locked' | 'empty'. */
  function bagTile({ glyph, title, caption, art, state, onClick, tipText, size = 96 }) {
    const cell = el('wr-tile-cell');
    const t = el(`wr-tile is-${state}`, cell);
    t.style.width = t.style.height = `${size}px`;
    if (art && state !== 'empty') {
      const img = document.createElement('img');
      img.src = art;
      img.alt = '';
      img.addEventListener('error', () => { img.remove(); el('wr-tile-glyph', t, glyph || '◈'); });
      t.appendChild(img);
    } else if (state === 'empty' && caption == null) {
      const img = document.createElement('img');
      img.className = 'wr-tile-keyhole';
      img.src = `${ART}hub/tile_unknown.png`;
      img.alt = '';
      img.addEventListener('error', () => { img.remove(); el('wr-tile-glyph', t, '+'); });
      t.appendChild(img);
    } else {
      el('wr-tile-glyph', t, glyph || '◈');
    }
    const label = caption != null ? caption
      : (state === 'locked' || state === 'empty') ? '???' : String(title).split(' · ')[0];
    el('wr-tile-label', cell, label);
    if (onClick) {
      cell.classList.add('is-click');
      cell.addEventListener('click', () => { sfx('ui_click', 0.3); onClick(); });
    }
    tip(cell, tipText || title);
    return cell;
  }

  /**
   * A Display-Shelf card - the BAG's collectible. The art is the hero (full-bleed
   * top), a rarity-coloured frame gives each piece its identity, and one status
   * corner + a foot line (name / pips / price) says what it is at a glance. Replaces
   * the old flat bagTile grid that padded itself out with rows of empty keyholes.
   * state: 'equipped' | 'owned' | 'buyable' | 'mystery' | 'empty'.
   */
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

  // ============================ BAG ============================

  function renderBag(panel, v) {
    // ---- hands (consumable HUD slots) ----
    // Grab-in-the-tube rework: no loadout pockets. You fall in empty-handed and grab
    // power-ups live; HANDS is how many consumables (active toys) you can hold at once.
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

    // ---- collections (discovery-gated: grab in the fall to reveal, deepen with Sparks) ----
    // Every piece is a card: discovered (level + deepen) or an undiscovered mystery
    // keyhole (rank-locked ones name the depth gate). Clicking a discovered card jumps
    // to the Toybox to deepen it; charms live here too now (they're grabbed, not toggled).
    const grids = [
      ['toybox_accessories', 'ACCESSORIES', 'accessory'],
      ['toybox_toys', 'TOYS (consumables)', 'skill'],
      ['toybox_accessories', 'CHARMS', 'utility'],
    ];
    for (const [gate, label, cat] of grids) {
      if (cat !== 'utility' && !reveals.isUnlocked(gate, v)) continue;
      const boons = boonsInCat(cat);
      const found = boons.filter((b) => v.boonLevel(b.id) >= 1).length;
      const card = el('wr-card', panel);
      el('wr-card-h', card, label);
      el('wr-card-sub', card, `${found}/${boons.length} discovered`);
      const grid = el('wr-shelf-grid', card);
      for (const b of boons) {
        const level = v.boonLevel(b.id);
        const found1 = level >= 1;
        const rankLocked = v.isBoonRankLocked(b.id);
        const maxed = found1 && level >= b.levelValues.length;
        grid.appendChild(shelfCard({
          art: `${ART}boons/${b.id}.png`, glyph: b.glyph,
          title: found1 ? b.name : '? ? ?',
          acc: found1 ? boonAcc(b, maxed) : '232,67,147',
          state: found1 ? 'owned' : 'mystery',
          ribbon: found1 ? (maxed ? 'MAX' : rarityTag(b)) : null,
          pips: found1 ? { level, max: b.levelValues.length } : null,
          foot: found1 ? (maxed ? 'MAX' : `L${level} · deepen`)
            : rankLocked ? RANKS.name(b.rankFloor) : 'undiscovered',
          tipText: found1 ? `${b.name} — click to deepen in the Toybox`
            : rankLocked ? `${RANKS.lockedTip} ${RANKS.specifics(b.rankFloor, v.runs)}`
            : `${b.name} — grab it in the fall to discover it`,
          onClick: found1 ? () => { tab = 'enhance'; rerender(); } : null,
        }));
      }
    }

    // ---- habits (still TRAINED here with Sparks; on/off toggle — not grabbed) ----
    const card = el('wr-card', panel);
    el('wr-card-h', card, 'HABITS');
    const grid = el('wr-shelf-grid', card);
    let trained = 0, switchedOn = 0, shown = 0;
    for (const u of UPGRADES) {
      if (!v.onShelfNow(u.id)) continue;
      shown++;
      const owned = v.isOwned(u.id);
      const on = owned && v.isUpgradeActive(u.id);
      if (owned) trained++;
      if (on) switchedOn++;
      grid.appendChild(shelfCard({
        art: `${ART}upgrades/${u.id}.png`, glyph: u.glyph, title: u.name,
        acc: on ? GOLD_ACC : (BRANCH_COLOR[u.branch] || '232,67,147'),
        state: on ? 'equipped' : owned ? 'owned' : 'buyable',
        corner: on ? '✓' : null,
        ribbon: BRANCH_LABEL[u.branch] || null,
        foot: on ? 'ON' : owned ? 'off' : `train ${GLYPHS.drops}${u.cost}`,
        tipText: on ? `${u.name} — click to switch off` : owned ? `${u.name} — click to switch on`
          : `${u.name} — train for ${GLYPHS.drops}${u.cost} in the Toybox`,
        onClick: owned
          ? () => { cmd('toggle-upgrade', { id: u.id, on: !on }); sfx(!on ? 'ui_equip' : 'ui_unequip', 0.45); }
          : () => { tab = 'enhance'; rerender(); },
      }));
    }
    el('wr-card-sub', card, `${switchedOn} on · ${trained}/${shown} trained`);
  }

  // ============================ Toybox ============================

  function renderToybox(panel, v) {
    // ---- habits (the trainable passives + utility charms) ----
    const habits = el('wr-card', panel);
    el('wr-card-h', habits, 'HABITS · train them once, wear them always');
    for (const u of UPGRADES) if (v.onShelfNow(u.id)) habits.appendChild(upgradeRow(u, v));
    for (const b of boonsInCat('utility')) if (v.onShelfNow(b.id)) habits.appendChild(boonRow(b, v));

    // ---- toys (reveal: first toy pocket) ----
    if (reveals.isUnlocked('toybox_toys', v)) {
      const card = el('wr-card', panel);
      const hdr = el('wr-card-h', card, 'TOYS · you press them mid-descent');
      revealEls.set('toybox_toys', hdr);
      for (const b of boonsInCat('skill')) card.appendChild(boonRow(b, v));
    } else {
      const stub = hazyRow('???', `${WALL_TIP} opens with your first toy pocket. she sews pockets for gold (her corner, later her bench).`);
      panel.appendChild(stub);
    }

    // ---- accessories (reveal: first accessory pocket) ----
    if (reveals.isUnlocked('toybox_accessories', v)) {
      const card = el('wr-card', panel);
      const hdr = el('wr-card-h', card, 'ACCESSORIES · they shape the whole fall');
      revealEls.set('toybox_accessories', hdr);
      for (const b of boonsInCat('accessory')) card.appendChild(boonRow(b, v));
    } else {
      panel.appendChild(hazyRow('???', `${WALL_TIP} opens with your first accessory pocket. she sews pockets for gold (her corner, later her bench).`));
    }

    // ---- more hands (grab-in-the-tube: Sparks sew consumable slots; pockets retired) ----
    if (reveals.isUnlocked('toybox_her_corner', v)) {
      const card = el('wr-card', panel);
      const hdr = el('wr-card-h', card, 'MORE HANDS · hold more at once');
      revealEls.set('toybox_her_corner', hdr);
      el('wr-card-sub', card, `${v.consumableSlots}/${MAX_CONSUMABLE_SLOTS} consumable slots. grabbed toys wait in these to be fired.`);
      const slotCost = v.nextConsumableSlotCost();
      if (slotCost != null) {
        card.appendChild(buyBtn(`sew a hand  ${GLYPHS.drops}${slotCost}`, v.canBuyConsumableSlot(), () => {
          cmd('buy-consumable-slot', { cost: slotCost });
          sfx('ui_deepen', 0.5);
        }));
      } else {
        el('wr-card-sub', card, 'you can hold no more.');
      }
    }
  }

  // ============================ THE DIALS (options-panel unlocks) ============================
  // The gear-panel controls start locked; each rung here flips one open for
  // drops (authoritative `purchase-dial` meta-command). The two feral dials
  // (hydra generations, glitch timer) also need a meta-rank. Mirrors the Toybox
  // row idiom; the panel reveals the control the moment the snapshot re-broadcasts.
  function renderDials(panel, v) {
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

  // ============================ THE DESCENT (run setup) ============================

  function renderDescent(panel, v) {
    const card = el('wr-card', panel);
    el('wr-card-h', card, 'DRESS UP THE FALL');

    const pillRow = (label, parent) => {
      const box = el('wr-setup-row', parent);
      el('wr-setup-label', box, label);
      return el('wr-pills', box);
    };

    // ---- difficulty (reveal-gated pills; Inescapable keeps its own lock) ----
    const diff = pillRow('difficulty', card);
    let effDiff = setup.difficulty;
    const diffAvailable = DIFF_PILLS.filter((d) =>
      (!d.revealGate || reveals.isUnlocked(d.revealGate, v)));
    if (!diffAvailable.some((d) => d.id === effDiff)) effDiff = 'Easy';
    if (effDiff === 'Extreme' && !v.extremeUnlocked) effDiff = 'Hard';
    for (const d of DIFF_PILLS) {
      if (d.revealGate && !reveals.isUnlocked(d.revealGate, v)) continue;
      const locked = d.extremeGate && !v.extremeUnlocked;
      const p = btn('wr-seg' + (effDiff === d.id ? ' is-on' : '') + (locked ? ' is-locked' : ''),
        locked ? `${d.label} 🔒` : d.label, () => {
          if (locked) { sfx('ui_denied', 0.4); return; }
          setup.difficulty = d.id;
          rerender();
        }, diff);
      if (locked) {
        tip(p, `a deeper door. she sells the key in the Toybox: finish 10 relentless descents, reach Devoted (${RANKS.thresholds[RANK.Devoted]} descents), then train it for ${GLYPHS.drops}${upgradeById('extreme_tier')?.cost ?? 350}.`);
      } else tip(p, d.tip);
      if (d.revealGate) revealEls.set(d.revealGate, p);
      if (d.extremeGate) revealEls.set('pill_inescapable', p);
    }

    // ---- length (4 chambers x per-chamber minutes) ----
    const len = pillRow('descent', card);
    for (const secs of CHAMBER_TOTALS) {
      btn('wr-seg' + (setup.durationSec === secs ? ' is-on' : ''), `${secs / 60} min`, () => {
        setup.durationSec = secs;
        rerender();
      }, len);
    }
    tip(len, 'four chambers, always in order. this is the whole descent - each chamber runs about a quarter of it and ends in a boon.');

    // ---- motion ----
    const mot = pillRow('motion', card);
    for (const m of ['Mixed', 'FloatUp', 'RainDown', 'RoamBounce']) {
      btn('wr-seg' + (setup.motion === m ? ' is-on' : ''), m === 'FloatUp' ? 'Float Up' : m === 'RainDown' ? 'Rain Down' : m === 'RoamBounce' ? 'Roam' : m, () => {
        setup.motion = m;
        rerender();
      }, mot);
    }

    // ---- chambers (fixed at 4; the descent's identity, not a dial) ----
    const waves = pillRow('chambers', card);
    for (const r of ['I', 'II', 'III', 'IV']) el('wr-seg wr-seg--ghost is-on', waves, r);

    // ---- bubble pool ----
    const poolCard = el('wr-card', panel);
    el('wr-card-h', poolCard, 'THE BUBBLE POOL');
    const enabled = new Set(setup.enabledVariants || POOL_VARIANTS.map((x) => x.id));
    const pool = el('wr-pills wr-pills--wrap', poolCard);
    for (const pv of POOL_VARIANTS) {
      const gateLocked = pv.revealGate && !reveals.isUnlocked(pv.revealGate, v);
      const on = enabled.has(pv.id);
      const p = btn('wr-seg' + (on && !gateLocked ? ' is-on' : '') + (gateLocked ? ' is-locked' : ''),
        gateLocked ? '???' : pv.name, () => {
          if (gateLocked) { sfx('ui_denied', 0.4); return; }
          if (on) enabled.delete(pv.id); else enabled.add(pv.id);
          if (enabled.size === 0) enabled.add('flash');
          setup.enabledVariants = enabled.size === POOL_VARIANTS.length ? null : [...enabled];
          rerender();
        }, pool);
      if (gateLocked) { tip(p, `${WALL_TIP} ${RANKS.specifics(RANK.Entranced, v.runs)}`); revealEls.set(pv.revealGate, p); }
    }
    const presets = el('wr-pills', poolCard);
    for (const pr of POOL_PRESETS) {
      btn('wr-seg wr-seg--ghost', pr.name, () => {
        setup.enabledVariants = pr.ids.length === POOL_VARIANTS.length ? null : [...pr.ids];
        rerender();
      }, presets);
    }
    btn('wr-seg wr-seg--ghost', '🎲 randomize', () => {
      const diffs = ['Easy'];
      if (reveals.isUnlocked('pill_teasing', v)) diffs.push('Medium');
      if (reveals.isUnlocked('pill_relentless', v)) diffs.push('Hard');
      if (v.extremeUnlocked) diffs.push('Extreme');
      setup.difficulty = diffs[(Math.random() * diffs.length) | 0];
      setup.durationSec = CHAMBER_TOTALS[(Math.random() * CHAMBER_TOTALS.length) | 0];
      setup.motion = ['Mixed', 'FloatUp', 'RainDown', 'RoamBounce'][(Math.random() * 4) | 0];
      const roll = POOL_VARIANTS.filter((pv) => (!pv.revealGate || reveals.isUnlocked(pv.revealGate, v)) && Math.random() < 0.6).map((x) => x.id);
      if (!roll.length) roll.push('flash');
      setup.enabledVariants = roll;
      rerender();
    }, presets);

    // ---- preset honesty note (codec UX): the host's manifest reported files it
    // can't hand the browser (wmv/avi/mkv...) or a sampled-down huge preset.
    const ms = getMediaStats ? getMediaStats() : null;
    if (ms && (ms.skipped > 0 || ms.truncated)) {
      const bits = [];
      if (ms.skipped > 0) {
        bits.push(`${nfmt(ms.skipped)} file${ms.skipped === 1 ? '' : 's'} in your preset can't swim in here (old video formats) - they still fire as desktop effects`);
      }
      if (ms.truncated) {
        bits.push(`your preset is huge, so she deals a fresh random ${nfmt(ms.images + ms.videos)}-piece sample each visit`);
      }
      el('wr-card-sub', poolCard, '🎞 ' + bits.join(' · '));
    }

    // ---- toggles + intensity ----
    const optCard = el('wr-card', panel);
    el('wr-card-h', optCard, 'THE MOOD');
    const check = (label, key) => {
      const rowEl = el('wr-check' + (setup[key] ? ' is-on' : ''), optCard);
      rowEl.textContent = `${setup[key] ? '☑' : '☐'}  ${label}`;
      rowEl.addEventListener('click', () => { sfx('ui_click', 0.3); setup[key] = !setup[key]; rerender(); });
    };
    check('color flashes on the edges', 'colorFlashes');
    check('mantra drafts between loops', 'boonDraftEnabled');
    check('sins on the table', 'allowCurses');
    check('white rabbits', 'dartersEnabled');
    const intRow = el('wr-setup-row', optCard);
    el('wr-setup-label', intRow, `effect intensity · ${Math.round(setup.effectIntensity * 100)}%`);
    const slider = document.createElement('input');
    slider.type = 'range';
    slider.min = '20'; slider.max = '150'; slider.step = '5';
    slider.value = String(Math.round(setup.effectIntensity * 100));
    slider.className = 'wr-slider';
    slider.addEventListener('input', () => {
      setup.effectIntensity = Number(slider.value) / 100;
      intRow.querySelector('.wr-setup-label').textContent = `effect intensity · ${slider.value}%`;
    });
    intRow.appendChild(slider);

    // ---- how consumables fire (grab-in-the-tube: no pre-bound keys) ----
    {
      const kc = el('wr-card', panel);
      el('wr-card-h', kc, 'YOUR HANDS');
      el('wr-card-sub', kc, `you fall in empty-handed. grab power-up cards as you fall — toys dock at the bottom (${v.consumableSlots} slot${v.consumableSlots === 1 ? '' : 's'}) and fire on their number key or a click; charms & accessories apply the instant you grab them.`);
    }

    // ---- FALL IN ----
    const go = btn('wr-fallin', descending ? 'she opens the hole…' : 'FALL IN', () => beginDescend(), panel);
    go.disabled = descending;
  }

  // ============================ Looking Glass ============================

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
      // Stays clickable when short - her one gift rides on a short first-pocket buy.
      const clickable = afford || (item.id === 'toy_pocket_1' && !v.giftGiven);
      const b = btn('wr-buy wr-buy--gold' + (afford ? '' : ' is-short'), `buy  ${GLYPHS.gold} ${nfmt(item.cost)}`, () => {
        if (!clickable) { sfx('ui_denied', 0.45); return; }
        cmd('bench-buy', { id: item.id, cost: item.cost });
        window.setTimeout(() => {
          const nv = view();
          if (!nv.bench.has(item.id)) { sfx('ui_denied', 0.45); return; }
          if (item.pocket) {
            const n = item.pocket === 'toy' ? nv.toyPockets : nv.accessoryPockets;
            showUnlockCard(cardForPocket(item.pocket === 'toy', item.label, item.line, n));
          } else sfx('ui_unlock', 0.55);
          runRevealFlashes('purchase');
        }, 300);
      }, right);
      if (!clickable) b.classList.add('is-off');
    }
    return row;
  }

  function renderLookingGlass(panel, v) {
    // ---- her bench (the gold shop) ----
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
  }

  // ============================ Diary ============================

  function renderDiary(panel, v) {
    const verbs = el('wr-card', panel);
    el('wr-card-h', verbs, 'VERBS · how to play down there');
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

  // ============================ render root ============================

  function rerender() {
    if (!visible) return;
    revealEls.clear();
    root.innerHTML = '';
    root.appendChild(cardLayer);
    const host = el('wr-screen', root);
    if (screen === 'menu') renderMenu(host);
    else renderDollhouse(host);
    // An open modal survives re-renders (every meta-command answers with a
    // snapshot -> refresh -> rerender, which must not eat the invitation).
    if (modal) root.appendChild(modal);
  }

  // ============================ public surface ============================

  return {
    show(which) {
      visible = true;
      root.hidden = false;
      descending = false;
      if (which) screen = which;
      rerender();
      if (screen === 'dollhouse') runRevealFlashes('hub_open');
      else { const v = view(); reveals.sync(v, bridge, 'hub_open'); }
    },
    hide() {
      visible = false;
      root.hidden = true;
      closeModal();
    },
    /** The meta snapshot moved (a command was answered) - re-render in place. */
    refresh() { if (visible) rerender(); },
    /** The descend request failed / was superseded - re-arm the buttons. */
    descendFailed() { descending = false; rerender(); },
    isVisible: () => visible,
    currentSetup: () => ({ ...setup }),
    /** Esc: close modal -> back out of the dollhouse -> not consumed (menu). */
    handleEsc() {
      if (!visible) return false;
      if (modal) { closeModal(); return true; }
      if (screen === 'dollhouse') { screen = 'menu'; rerender(); return true; }
      return true;   // at the menu: swallow the tap (hold-to-exit still works)
    },
    dispose() { root.remove(); },
  };
}
