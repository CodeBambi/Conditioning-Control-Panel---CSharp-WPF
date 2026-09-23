/* ============================================================================
 * ui/screens/flavour.js - the flavour card and the options Pictures section.
 *
 * NOT a router screen of its own (the router's id table is another lane's): the
 * card is mounted by ui/screens/mediaSetup.js, which shows it FIRST whenever the
 * host can fetch online pictures, and keeps the old file picker one tap away as
 * "use my own files". The Pictures section is mounted inside ui/options.js.
 *
 * Both talk to boot.js through ONE small api (boot's `mediaFlavour`):
 *   get()            -> { flavour, custom, online }  (a copy; the host's echo plus local edits)
 *   pick(id)         -> send `media-flavour` now (the first-run tap)
 *   commit(state)    -> send `media-flavour` if the state moved (options close)
 *   info()           -> the last online-media frame, read by ui/flavours.js readOnlineFrame
 *   subscribe(fn)    -> fn() on every online-media frame; returns unsubscribe
 *
 * RULES THE CARD KEEPS: one tap picks, and nothing waits on the fetch - the pick
 * sends the frame and the caller moves on after a short beat. The card's sub line
 * is the opt-in: it says where pictures come from.
 * ==========================================================================*/

import { el } from '../router.js';
import { S } from '../strings.js';
import {
  FLAVOURS, MINE, flavourById, nichesOf, toggleNiche, addNiche, removeNiche,
} from '../flavours.js';

/** The live line's words for one online-media frame (pure, exported for the tests). */
export function liveLine(info, picked) {
  const L = S.flavour.live;
  if (!info) return picked ? L.loading : L.idle;
  const n = (info.images ? info.images.length : 0) + (info.videos ? info.videos.length : 0);
  switch (info.state) {
    case 'ready': return n > 0 ? L.ready(n) : L.empty;
    case 'loading': return n > 0 ? L.loadingN(n) : L.loading;
    case 'empty': return L.empty;
    case 'off': return L.off;
    case 'error': return n > 0 ? L.ready(n) : L.error;
    default: return picked ? L.loading : L.idle;
  }
}

function reduced() {
  try { return document.documentElement.getAttribute('data-gg-motion') === 'reduced'; } catch (_e) { return false; }
}

/**
 * The first-run card. `onDone()` is called once, a beat after a pick (or at once
 * for "use my own files" via `onOwn`).
 */
export function mountFlavourCard(container, { ledger, api, audio = null, onDone, onOwn, onNone } = {}) {
  const F = S.flavour;
  let picked = '';
  const live = el('p', { class: 'gg-flv-live', 'aria-live': 'polite', text: liveLine(null, false) });
  const paintLive = () => { live.textContent = liveLine(picked ? api?.info?.() : null, !!picked); };

  const tiles = FLAVOURS.map((f, i) => {
    const t = el('button', {
      type: 'button', class: 'gg-flv-tile', 'data-flavour': f.id,
      style: '--flv-tint:' + f.tint + ';--flv-i:' + i,
    }, [
      el('span', { class: 'gg-flv-name', text: f.name }),
      el('span', { class: 'gg-flv-line', text: f.line }),
      el('span', { class: 'gg-flv-niches', text: f.subs.map((s) => 'r/' + s).join('  ') }),
    ]);
    ledger.listen(t, 'click', (e) => {
      e?.preventDefault?.();
      if (picked) return;
      picked = f.id;
      for (const o of tiles) o.classList.toggle(o === t ? 'is-picked' : 'is-dim', true);
      try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
      try { api?.pick?.(f.id); } catch (_e) { /* the pick is never load-bearing */ }
      paintLive();
      // A beat so the thud reads, then on: nothing here waits on the fetch.
      ledger.timer(() => { try { onDone?.(); } catch (_e) { /* ignore */ } }, reduced() ? 0 : 650);
    });
    return t;
  });

  const own = el('button', { type: 'button', class: 'gg-btn gg-btn--ghost gg-flv-own', text: F.own });
  ledger.listen(own, 'click', (e) => { e?.preventDefault?.(); if (!picked) onOwn?.(); });
  const foot = [own];
  if (typeof onNone === 'function') {
    const none = el('button', { type: 'button', class: 'gg-btn gg-btn--ghost gg-flv-none', text: F.noneForNow });
    ledger.listen(none, 'click', (e) => { e?.preventDefault?.(); if (!picked) onNone(); });
    foot.push(none);
  }

  const card = el('div', { class: 'gg-card gg-flv' }, [
    el('p', { class: 'gg-eyebrow', text: F.eyebrow }),
    el('h2', { class: 'gg-grad gg-flv-head', text: F.headline }),
    el('p', { class: 'gg-flv-sub', text: F.sub }),
    el('div', { class: 'gg-flv-grid' }, tiles),
    live,
    el('div', { class: 'gg-flv-foot' }, foot),
  ]);
  container.appendChild(card);
  if (api && typeof api.subscribe === 'function') ledger.add(api.subscribe(paintLive));
  return card;
}

/**
 * The options sheet's Pictures section. Edits a LOCAL copy of the state; the
 * caller hands that copy to api.commit() when the sheet closes (one refetch per
 * close, never one per keystroke). Returns { node, state() }.
 */
export function buildPicturesSection({ ledger, api, audio = null } = {}) {
  const F = S.flavour;
  const start = (api && api.get && api.get()) || { flavour: '', custom: {}, online: true };
  const st = { flavour: start.flavour || '', custom: JSON.parse(JSON.stringify(start.custom || {})), online: start.online !== false };
  // The tab on show: the picked flavour, or the first one when nothing is picked yet.
  let tab = flavourById(st.flavour) ? st.flavour : FLAVOURS[0].id;

  const tabsRow = el('div', { class: 'gg-flv-tabs', role: 'tablist' });
  const pills = el('div', { class: 'gg-flv-pills' });
  const err = el('p', { class: 'gg-flv-err', text: '' });
  const live = el('p', { class: 'gg-flv-live gg-panel-note', 'aria-live': 'polite', text: '' });
  const input = el('input', {
    type: 'text', class: 'gg-flv-input', placeholder: F.addPlaceholder,
    autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', maxlength: '80', 'aria-label': F.addPlaceholder,
  });
  const addBtn = el('button', { type: 'button', class: 'gg-btn gg-flv-addbtn', text: F.add });
  const onlineBtn = el('button', { type: 'button', class: 'gg-toggle', 'aria-pressed': String(st.online) });
  onlineBtn.appendChild(el('i'));

  const sfx = (n) => { try { audio?.sfx?.(n); } catch (_e) { /* stub bus */ } };
  const custom = (id) => st.custom[id] || {};

  function paintTabs() {
    tabsRow.replaceChildren();
    for (const f of [...FLAVOURS, MINE]) {
      const b = el('button', {
        type: 'button', role: 'tab', class: 'gg-flv-tab' + (f.id === tab ? ' is-on' : ''),
        'aria-selected': String(f.id === tab), style: '--flv-tint:' + f.tint, text: f.id === MINE.id ? F.mine : f.name,
      });
      ledger.listen(b, 'click', (e) => {
        e?.preventDefault?.();
        tab = f.id; st.flavour = f.id; err.textContent = '';
        sfx('ui-move'); paint();
      });
      tabsRow.appendChild(b);
    }
  }

  function paintPills() {
    const f = flavourById(tab);
    pills.replaceChildren();
    pills.style.setProperty('--flv-tint', f.tint);
    const list = nichesOf(f, custom(f.id));
    if (!list.length) pills.appendChild(el('p', { class: 'gg-panel-note', text: F.mineEmpty }));
    for (const n of list) {
      const pill = el('button', {
        type: 'button', class: 'gg-flv-pill' + (n.on ? ' is-on' : ''), 'aria-pressed': String(n.on), text: 'r/' + n.name,
      });
      ledger.listen(pill, 'click', (e) => {
        e?.preventDefault?.();
        st.custom[f.id] = toggleNiche(f, custom(f.id), n.name);
        st.flavour = f.id;
        sfx('ui-select'); paint();
      });
      if (n.kind === 'added') {
        const x = el('button', { type: 'button', class: 'gg-flv-pill-x', 'aria-label': F.removeNiche(n.name), text: '×' });
        ledger.listen(x, 'click', (e) => {
          e?.preventDefault?.();
          st.custom[f.id] = removeNiche(custom(f.id), n.name);
          sfx('ui-back'); paint();
        });
        pills.appendChild(el('span', { class: 'gg-flv-pillwrap' }, [pill, x]));
      } else {
        pills.appendChild(pill);
      }
    }
  }

  function paintLive() { live.textContent = liveLine(api?.info?.() || null, !!st.flavour); }
  function paint() { paintTabs(); paintPills(); onlineBtn.setAttribute('aria-pressed', String(st.online)); paintLive(); }

  function add() {
    const f = flavourById(tab);
    const r = addNiche(f, custom(f.id), input.value);
    st.custom[f.id] = r.custom;
    err.textContent = r.error ? (F.errors[r.error] || '') : '';
    if (!r.error) { input.value = ''; st.flavour = f.id; sfx('ui-select'); }
    paint();
  }
  ledger.listen(addBtn, 'click', (e) => { e?.preventDefault?.(); add(); });
  /* TYPING IS NOT PLAYING. The window-level game hotkeys already skip a focused
   * input, and this keeps every key but Escape (the drawer's own) inside the field
   * besides, so no future listener can mistake a niche name for a command. */
  ledger.listen(input, 'keydown', (e) => {
    if (!e) return;
    if (e.key !== 'Escape') e.stopPropagation?.();
    if (e.key === 'Enter') { e.preventDefault?.(); add(); }
  });
  ledger.listen(onlineBtn, 'click', (e) => {
    e?.preventDefault?.();
    st.online = !st.online;
    sfx('ui-select'); paint();
  });
  if (api && typeof api.subscribe === 'function') ledger.add(api.subscribe(paintLive));

  paint();
  const node = el('div', { class: 'gg-flv-section' }, [
    el('h3', { class: 'gg-panel-subhead', text: F.section }),
    tabsRow,
    pills,
    el('div', { class: 'gg-flv-addrow' }, [input, addBtn]),
    err,
    el('div', { class: 'gg-panel-row' }, [el('span', { class: 'gg-panel-label', text: F.online }), onlineBtn]),
    el('p', { class: 'gg-panel-note', text: F.onlineNote }),
    live,
  ]);
  return { node, state: () => ({ flavour: st.flavour, custom: JSON.parse(JSON.stringify(st.custom)), online: st.online }) };
}

export default { mountFlavourCard, buildPicturesSection, liveLine };
