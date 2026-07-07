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
  lessonById, RANKS, RANK, GLYPHS,
  DIARY_VERBS, DIARY_CODEX, POOL_VARIANTS, POOL_PRESETS, DIFF_PILLS,
  HOWTO_CARDS, metaView,
} from './catalog.js';
import { BOONS } from './boons.js';
import * as reveals from './reveals.js';

const ART = 'https://ccp.art/';
const KEY_OPTS = ['Q', 'E', 'R', 'F', 'Z', 'X', 'C', 'V', '1', '2', '3', '4'];
const nfmt = (n) => Math.floor(n).toLocaleString();

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
  const setup = Object.assign({
    difficulty: 'Easy', durationSec: 180, waveCount: 5, motion: 'Mixed',
    enabledVariants: null, effectIntensity: 0.85, colorFlashes: true,
    boonDraftEnabled: true, allowCurses: true, dartersEnabled: true,
    key1: 'Q', key2: 'E',
  }, runSetup || {});

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
    rule('🌊', '122,224,255', 'RIGHT-CLICK', 'ripple the water. ', 'a right-click near the bubbles sends out a wave — treats pop, trances snap, rabbits go flying. it takes a while to gather another.');
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
    run: 'dress up the fall, then FALL IN.',
    improve: 'the bench, the mantras, how far you’ve fallen.',
    diary: 'everything you’ve met down there. click an entry to read it.',
  };

  function countAffordableToybox(v) {
    let n = 0;
    for (const u of UPGRADES) {
      if (!v.isOwned(u.id) && !v.isPurchaseRankLocked(u.id)
        && !v.isLessonBlocked(u.id, setup.allowCurses) && v.canAffordUpgrade(u.id)) n++;
    }
    for (const b of LIFETIME_BOONS) {
      const lvl = v.boonLevel(b.id);
      if (lvl <= 0) {
        if (!v.isBoonRankLocked(b.id) && !v.isAccessoryScriptLocked(b.id)
          && !v.isLessonBlocked(b.id, setup.allowCurses) && v.canAffordUnlock(b.id)) n++;
      } else if (lvl < b.levelValues.length && !v.isCapstoneRankLocked(b.id) && v.canAffordDeepen(b.id)) n++;
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
    else if (tab === 'run') renderDescent(panel, v);
    else if (tab === 'improve') renderLookingGlass(panel, v);
    else if (tab === 'diary') renderDiary(panel, v);

    el('wr-hint', wrap, TAB_HINTS[tab] || '');
  }

  // ============================ shared row/tile builders ============================

  function lessonLockPanel(id, accent) {
    const def = lessonById(id);
    const panel = el('wr-lesson');
    if (!def) return panel;
    const target = Math.max(1, def.target);
    const progress = Math.max(0, Math.min(target, view().lessonProgress(id)));
    el('wr-lesson-text', panel, '🔒 ' + def.text).style.color = `rgba(${accent},0.75)`;
    const bar = el('wr-lesson-bar', panel);
    const fill = el('wr-lesson-fill', bar);
    fill.style.width = progress <= 0 ? '0' : `${Math.max(6, (progress / target) * 100)}%`;
    el('wr-lesson-count', panel, `lesson: ${progress} of ${target}`);
    tip(panel, def.detail || def.text);
    return panel;
  }

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
    } else if (v.isLessonBlocked(u.id, setup.allowCurses)) {
      right.appendChild(lessonLockPanel(u.id, accent));
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
    const level = v.boonLevel(b.id);
    const unlocked = level >= 1;
    const active = v.isBoonActive(b.id);
    const maxed = unlocked && level >= b.levelValues.length;
    const rankLocked = v.isBoonRankLocked(b.id);
    const accent = '232,67,147';
    const row = el('wr-row' + (active ? ' is-on' : unlocked ? ' is-owned' : ''));
    row.style.setProperty('--acc', accent);

    // Rank-locked = a MYSTERY: keyhole, "???", the depth gate - never the boon's face.
    const icon = rankLocked
      ? artIcon(`${ART}hub/tile_unknown.png`, '?', accent, 72)
      : artIcon(`${ART}boons/${b.id}.png`, b.glyph, accent, 72);
    if (!unlocked) icon.style.opacity = rankLocked ? '0.6' : '0.55';
    row.appendChild(icon);

    const mid = el('wr-row-mid', row);
    el('wr-row-name', mid, rankLocked ? '? ? ?' : unlocked ? `${b.name} · L${level}` : b.name);
    if (rankLocked) {
      el('wr-row-flavor', mid, `${RANKS.lockedTip} ${RANKS.specifics(b.rankFloor, v.runs)}`);
    } else {
      el('wr-row-desc', mid, b.desc);
      if (b.flavor) el('wr-row-flavor', mid, b.flavor);
      if (b.activeUse) {
        const useHint = b.cooldownSec > 0 ? `${b.cooldownSec}s cooldown` : 'limited uses';
        el('wr-row-active', mid, active
          ? `ACTIVE · fires on ${setup.key1} mid-descent · ${useHint}`
          : `ACTIVE · fires on your toy key mid-descent · ${useHint}`);
      }
      if (b.capstone) {
        const cap = el('wr-row-capstone' + (maxed ? ' is-maxed' : ''), mid, 'max: ' + b.capstone);
        void cap;
      }
      const pips = el('wr-row-pips', mid);
      for (let i = 1; i <= b.levelValues.length; i++) {
        el('wr-pip' + (i <= level ? ' is-full' : ''), pips, i <= level ? '●' : '○');
      }
      el('wr-pip-value', pips, '  ' + (unlocked ? b.value(b.levelValues[level - 1]) : 'locked'));
    }

    const right = el('wr-row-right', row);
    const habitVoice = b.cat === 'utility';
    if (unlocked) {
      if (active) el('wr-row-state', right, habitVoice ? 'ON ✓' : 'EQUIPPED ✓');
      const pocketFree = active || v.hasFreePocket(b.cat);
      const eq = btn('wr-pill', active ? (habitVoice ? 'switch off' : 'Unequip')
        : pocketFree ? (habitVoice ? 'switch on' : 'Equip') : 'pockets full', () => {
        if (!active && !pocketFree) return;
        cmd('set-lifetime-boon', { id: b.id, active: !active });
        sfx(!active ? 'ui_equip' : 'ui_unequip', 0.45);
      }, right);
      eq.disabled = !pocketFree && !active;
    }
    if (!unlocked) {
      if (rankLocked) {
        const held = buyBtn(`🔒 ${RANKS.name(b.rankFloor)}`, false, () => {});
        tip(held, `${RANKS.lockedTip}\n${RANKS.specifics(b.rankFloor, v.runs)}`);
        right.appendChild(held);
      } else if (v.isAccessoryScriptLocked(b.id)) {
        const held = buyBtn(`Unlock  ${GLYPHS.drops}${b.unlockCost}`, false, () => {});
        tip(held, 'she sells these in an order of her own.');
        right.appendChild(held);
      } else if (v.isLessonBlocked(b.id, setup.allowCurses)) {
        right.appendChild(lessonLockPanel(b.id, accent));
      } else {
        right.appendChild(buyBtn(`Unlock  ${GLYPHS.drops}${b.unlockCost}`, v.canAffordUnlock(b.id), () => {
          // Level 1 + auto-equip when a pocket is free (TryUnlockBoon semantics).
          const autoEquip = v.hasFreePocket(b.cat);
          cmd('set-lifetime-boon', { id: b.id, level: 1, cost: b.unlockCost, ...(autoEquip ? { active: true } : {}) });
          window.setTimeout(() => { showUnlockCard(cardForBoonUnlock(b.id, view())); runRevealFlashes('purchase'); }, 250);
        }));
      }
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

  // ============================ BAG ============================

  function renderBag(panel, v) {
    // ---- pockets ----
    const pocketCard = el('wr-card', panel);
    el('wr-card-h', pocketCard, 'POCKETS');
    el('wr-card-sub', pocketCard, v.toyPockets + v.accessoryPockets === 0
      ? 'you fall in with empty hands, for now.'
      : 'what you take down with you. choose like it matters.');
    const pockets = el('wr-pockets', pocketCard);
    let anyPockets = false;
    for (const [label, cat] of [['TOY', 'skill'], ['ACCESSORY', 'accessory']]) {
      const slots = v.slotsFor(cat);
      const equipped = boonsInCat(cat).filter((b) => v.isBoonActive(b.id));
      if (slots <= 0 && equipped.length === 0) continue;
      anyPockets = true;
      const col = el('wr-pocket-col', pockets);
      el('wr-pocket-label', col, label);
      const rowEl = el('wr-pocket-row', col);
      for (const b of equipped) {
        rowEl.appendChild(bagTile({
          glyph: b.glyph, title: `${b.name} · L${v.boonLevel(b.id)}`, art: `${ART}boons/${b.id}.png`,
          state: 'equipped', size: 114,
          tipText: `${b.name} — click to unequip`,
          onClick: () => { cmd('set-lifetime-boon', { id: b.id, active: false }); sfx('ui_unequip', 0.45); },
        }));
      }
      for (let i = equipped.length; i < slots; i++) {
        rowEl.appendChild(bagTile({
          glyph: '+', title: `empty ${label.toLowerCase()} pocket`, caption: 'empty',
          state: 'empty', size: 114,
          tipText: 'pick one from the shelf below, or go shopping in the Toybox.',
          onClick: () => { tab = 'enhance'; rerender(); },
        }));
      }
    }
    if (!anyPockets) el('wr-empty-note', pockets, 'no pockets sewn yet.');

    // ---- collections (mirror the Toybox reveals: a naked start shows neither) ----
    const grids = [
      ['toybox_accessories', 'ACCESSORIES', 'accessory'],
      ['toybox_toys', 'TOYS', 'skill'],
    ];
    for (const [gate, label, cat] of grids) {
      if (!reveals.isUnlocked(gate, v)) continue;
      const card = el('wr-card', panel);
      el('wr-card-h', card, label);
      el('wr-card-sub', card, `${v.equippedCountIn(cat)}/${v.slotsFor(cat) === Infinity ? '∞' : v.slotsFor(cat)} equipped`);
      const grid = el('wr-tiles', card);
      const boons = boonsInCat(cat);
      for (const b of boons) {
        const level = v.boonLevel(b.id);
        const unlocked = level >= 1;
        const active = v.isBoonActive(b.id);
        const rankLocked = v.isBoonRankLocked(b.id);
        grid.appendChild(bagTile({
          glyph: rankLocked ? '?' : b.glyph,
          title: rankLocked ? '? ? ?' : unlocked ? `${b.name} · L${level}` : b.name,
          art: rankLocked ? `${ART}hub/tile_unknown.png` : `${ART}boons/${b.id}.png`,
          state: active ? 'equipped' : unlocked ? 'owned' : 'locked',
          tipText: rankLocked ? `${RANKS.lockedTip} ${RANKS.specifics(b.rankFloor, v.runs)}`
            : active ? `${b.name} — click to unequip`
            : unlocked ? `${b.name} — click to equip`
            : `${b.name} — unlock for ${GLYPHS.drops}${b.unlockCost} in the Toybox`,
          onClick: active
            ? () => { cmd('set-lifetime-boon', { id: b.id, active: false }); sfx('ui_unequip', 0.45); }
            : unlocked
              ? () => {
                  // Equip into a full 1-slot pocket by quietly swapping the occupant out.
                  if (!v.hasFreePocket(cat)) {
                    const current = boonsInCat(cat).find((x) => v.isBoonActive(x.id));
                    if (current) cmd('set-lifetime-boon', { id: current.id, active: false });
                  }
                  cmd('set-lifetime-boon', { id: b.id, active: true });
                  sfx('ui_equip', 0.45);
                }
              : () => { tab = 'enhance'; rerender(); },
        }));
      }
      const target = Math.max(8, Math.ceil(boons.length / 4) * 4);
      for (let i = boons.length; i < target; i++) {
        grid.appendChild(bagTile({
          glyph: '+', title: cat === 'skill' ? 'another toy is being stitched' : 'another accessory is being stitched',
          state: 'empty', tipText: 'it’ll hang here when it’s ready.',
        }));
      }
    }

    // ---- habits 4x4 (trained = on/off toggle; untrained = go train it) ----
    const card = el('wr-card', panel);
    el('wr-card-h', card, 'HABITS');
    const grid = el('wr-tiles', card);
    let trained = 0, switchedOn = 0, shown = 0;
    for (const u of UPGRADES) {
      if (!v.onShelfNow(u.id)) continue;
      shown++;
      const owned = v.isOwned(u.id);
      const on = owned && v.isUpgradeActive(u.id);
      if (owned) trained++;
      if (on) switchedOn++;
      grid.appendChild(bagTile({
        glyph: u.glyph, title: u.name, art: `${ART}upgrades/${u.id}.png`,
        state: on ? 'equipped' : owned ? 'owned' : 'locked',
        tipText: on ? `${u.name} — click to switch off` : owned ? `${u.name} — click to switch on`
          : `${u.name} — train for ${GLYPHS.drops}${u.cost} in the Toybox`,
        onClick: owned
          ? () => { cmd('toggle-upgrade', { id: u.id, on: !on }); sfx(!on ? 'ui_equip' : 'ui_unequip', 0.45); }
          : () => { tab = 'enhance'; rerender(); },
      }));
    }
    for (const b of boonsInCat('utility')) {
      if (!v.onShelfNow(b.id)) continue;
      shown++;
      const unlocked = v.isBoonUnlocked(b.id);
      const active = v.isBoonActive(b.id);
      const rankLocked = v.isBoonRankLocked(b.id);
      if (unlocked) trained++;
      if (active) switchedOn++;
      grid.appendChild(bagTile({
        glyph: rankLocked ? '?' : b.glyph,
        title: rankLocked ? '? ? ?' : unlocked ? `${b.name} · L${v.boonLevel(b.id)}` : b.name,
        art: rankLocked ? `${ART}hub/tile_unknown.png` : `${ART}boons/${b.id}.png`,
        state: active ? 'equipped' : unlocked ? 'owned' : 'locked',
        tipText: rankLocked ? RANKS.specifics(b.rankFloor, v.runs)
          : active ? `${b.name} — click to switch off`
          : unlocked ? `${b.name} — click to switch on`
          : `${b.name} — unlock for ${GLYPHS.drops}${b.unlockCost} in the Toybox`,
        onClick: unlocked
          ? () => { cmd('set-lifetime-boon', { id: b.id, active: !active }); sfx(!active ? 'ui_equip' : 'ui_unequip', 0.45); }
          : () => { tab = 'enhance'; rerender(); },
      }));
    }
    const targetN = Math.max(16, Math.ceil(shown / 4) * 4);
    for (let i = shown; i < targetN; i++) {
      grid.appendChild(bagTile({
        glyph: '+', title: 'a habit not yet formed', state: 'empty',
        tipText: 'more training arrives in a later fitting.',
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

    // ---- her corner (early bench access; the registry closes it at Slipping) ----
    if (reveals.isUnlocked('toybox_her_corner', v)) {
      const card = el('wr-card', panel);
      const hdr = el('wr-card-h', card, 'HER CORNER · a table by the door');
      revealEls.set('toybox_her_corner', hdr);
      el('wr-gold-line', card, `you’re carrying ${GLYPHS.gold} ${nfmt(v.gold)}`);
      for (const id of ['toy_pocket_1', 'accessory_pocket_1']) {
        const item = BENCH_ITEMS.find((i) => i.id === id);
        if (item) card.appendChild(benchRow(item, v));
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

    // ---- length ----
    const len = pillRow('length', card);
    for (const secs of [120, 180, 300]) {
      btn('wr-seg' + (setup.durationSec === secs ? ' is-on' : ''), `${secs / 60} min`, () => {
        setup.durationSec = secs;
        rerender();
      }, len);
    }

    // ---- motion ----
    const mot = pillRow('motion', card);
    for (const m of ['Mixed', 'FloatUp', 'RainDown', 'RoamBounce']) {
      btn('wr-seg' + (setup.motion === m ? ' is-on' : ''), m === 'FloatUp' ? 'Float Up' : m === 'RainDown' ? 'Rain Down' : m === 'RoamBounce' ? 'Roam' : m, () => {
        setup.motion = m;
        rerender();
      }, mot);
    }

    // ---- loops (waves) ----
    const waves = pillRow('loops', card);
    btn('wr-seg', '−', () => { setup.waveCount = Math.max(1, setup.waveCount - 1); rerender(); }, waves);
    el('wr-waves-n', waves, String(setup.waveCount));
    btn('wr-seg', '+', () => { setup.waveCount = Math.min(12, setup.waveCount + 1); rerender(); }, waves);

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
      setup.durationSec = [120, 180, 300][(Math.random() * 3) | 0];
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

    // ---- toy keybinds (mirror the sewn pockets: none = no hints at all) ----
    if (v.toyPockets >= 1) {
      const keyCard = el('wr-card', panel);
      el('wr-card-h', keyCard, 'TOY KEYS');
      el('wr-card-sub', keyCard, v.toyPockets >= 2
        ? 'your equipped toys fire on these, mid-descent.'
        : 'your equipped toy fires on this, mid-descent. the second pocket isn’t sewn yet.');
      const mkKey = (label, key) => {
        const rowEl = el('wr-setup-row', keyCard);
        el('wr-setup-label', rowEl, label);
        const sel = document.createElement('select');
        sel.className = 'wr-select';
        for (const k of KEY_OPTS) {
          const o = document.createElement('option');
          o.value = o.textContent = k;
          if (setup[key] === k) o.selected = true;
          sel.appendChild(o);
        }
        sel.addEventListener('change', () => { setup[key] = sel.value; });
        rowEl.appendChild(sel);
      };
      mkKey('toy pocket 1', 'key1');
      if (v.toyPockets >= 2) mkKey('toy pocket 2', 'key2');
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
