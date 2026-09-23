/* ============================================================================
 * ui/screens/title.js - the home screen: Open tables.
 *
 * REDESIGNED 2026-09-23 to the approved "open tables" mockup (the owner called
 * the old storefront menu "pretty crap"). Left: the tables people have open
 * right now, friends first. Right rail: Host a table (Prime), Practice (Free),
 * Have a code?, and a quiet row of the old menu's other doors under it.
 *
 * THE GATE. Every 1v1 is a Prime (tier 2) perk, hosting AND joining. Practice
 * against the bot is free for every account and is never locked, never asked
 * about. Free accounts still SEE the tables; their Join and Host buttons wear a
 * lock and open the Prime sheet. Who may do what comes from the server's own
 * `/open` answer, then the host's init flags, then (standalone, unknown) nobody
 * is locked on a guess and a 403 from the server opens the sheet instead.
 *
 * THE LIST POLLS every 15 s while this screen is mounted AND the page is
 * visible, never otherwise. The rivalry record on a row is LOCAL (ui/rivalry.js):
 * it never went on the wire and never will.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import { formatBytes } from '../assetsStore.js';
import { GOON_BUILD } from '../../bridge.js';
import { normalizeCode } from '../../net/signaling.js';
import {
  OPEN_POLL_MS, agoText, clock, createTicker, gateFor, groupTables, joinOutcome, resolveAvatar, swatchFor,
} from '../../net/openTables.js';

export const ICON = Object.freeze({
  song: '<svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M6 2v8.3A2.5 2.5 0 1 0 7.5 12.5V5.5l5-1.3v4.1A2.5 2.5 0 1 0 14 10.5V1z"/></svg>',
  card: '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><rect x="3" y="2" width="10" height="12" rx="1.5"/><path d="M6 6h4M6 9h4"/></svg>',
  pic: '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><rect x="2" y="3" width="12" height="10" rx="1.5"/><path d="M3 12l4-4 3 3 2-2 2 2"/></svg>',
  lock: '<svg viewBox="0 0 16 16" width="13" height="13" fill="currentColor" aria-hidden="true"><path d="M5 7V5a3 3 0 0 1 6 0v2h1v7H4V7zm1.5 0h3V5a1.5 1.5 0 0 0-3 0z"/></svg>',
});

/** A row's avatar: the server's https picture when there is one, else the initial on a swatch. */
export function avatarNode(name, avatar, { online = false } = {}) {
  const [a, b] = swatchFor(name);
  const node = el('div', {
    class: 'gg-ot-av' + (online ? ' is-on' : ''),
    style: 'background:linear-gradient(135deg,' + a + ',' + b + ')',
    'aria-hidden': 'true',
  });
  node.textContent = (String(name || '?').trim()[0] || '?').toUpperCase();
  if (avatar) {
    const img = el('img', { src: avatar, alt: '', decoding: 'async', referrerpolicy: 'no-referrer' });
    img.addEventListener?.('error', () => { try { img.remove(); } catch (_e) { /* gone */ } });
    node.appendChild(img);
  }
  return node;
}

/** One chip, optionally led by an inline glyph. */
function tagNode(cls, text, { glyph = '', attrs = null } = {}) {
  const t = el('span', Object.assign({ class: 'gg-ot-tag' + (cls ? ' ' + cls : '') }, attrs || {}));
  if (glyph) t.innerHTML = glyph;
  if (text) t.appendChild(document.createTextNode(text));
  return t;
}

/** The chips under a name. Everything shown here is a flag or a number, never a host's words. */
export function tagNodes(t, { record = '' } = {}) {
  const out = [];
  if (t.level != null) out.push(tagNode('', S.tables.level(t.level)));
  out.push(tagNode('is-rec', record || S.tables.newRival));
  if (t.song) out.push(tagNode('', S.tables.song, { glyph: ICON.song, attrs: { title: S.tables.songTip } }));
  if (t.cardSec) out.push(tagNode('', clock(t.cardSec), { glyph: ICON.card, attrs: { title: S.tables.cardTip } }));
  if (t.pictures) out.push(tagNode('', '', { glyph: ICON.pic, attrs: { title: S.tables.picsTip, 'aria-label': S.tables.picsTip } }));
  if (t.waitingSec != null) out.push(tagNode('is-wait', S.tables.waitingFor(clock(t.waitingSec))));
  return out;
}

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { session, actions, audio, prefs, sheets, toasts } = ctx;
  const standalone = !session.hosted;
  const rivalry = ctx.rivalry || null;

  /** The last /open answer, and what it lets this account do. */
  let open = null;
  let openState = 'loading';          // loading | ok | signin | offline
  let gate = gateFor({ caps: session.caps, hosted: !standalone });
  const taken = new Set();
  let busyCode = '';

  const card = el('div', { class: 'gg-ot' });

  /* --- the list column --------------------------------------------------- */
  const liveText = el('span', { text: '' });
  const live = el('span', { class: 'gg-ot-live' }, [el('i'), liveText]);
  const logo = el('img', {
    class: 'gg-ot-logo',
    src: './assets/goon_game_logo.png',
    alt: 'Goon Game',
    decoding: 'async',
  });
  ledger.listen(logo, 'error', () => { try { logo.remove(); } catch (_e) { /* ignore */ } });
  const head = el('div', { class: 'gg-ot-head' }, [
    el('div', {}, [
      el('small', { class: 'gg-ot-eyebrow', text: S.tables.eyebrow }),
      el('h2', { class: 'gg-ot-h2', text: S.tables.title }),
    ]),
    live,
  ]);
  const listBox = el('div', { class: 'gg-ot-list', 'aria-live': 'polite' });
  const moreLine = el('p', { class: 'gg-ot-more', text: S.tables.refresh });
  const col = el('section', { class: 'gg-ot-col', 'aria-label': S.tables.title }, [logo, head, listBox, moreLine]);

  /* --- the rail ------------------------------------------------------------ */
  const rail = el('aside', { class: 'gg-ot-rail' });

  function chip(prime, locked) {
    const c = el('span', { class: prime ? 'gg-ot-chip-prime' : 'gg-ot-chip-free' });
    if (locked) c.innerHTML = ICON.lock;
    c.appendChild(document.createTextNode(prime ? (locked ? ' ' : '') + S.tables.chipPrime : S.tables.chipFree));
    return c;
  }

  /* HOST. `gg-menu-item` stays on it (and on Practice) because it IS still the
   * first menu item, and the selftests find it that way. Locked, it stays
   * enabled: the click is what opens the Prime sheet, and a Prime perk nobody
   * can see is a perk nobody buys. */
  const hostBtn = el('button', { type: 'button', class: 'gg-ot-act gg-ot-act--host gg-menu-item' });
  const hostCorner = el('span', { class: 'gg-ot-corner' });
  const hostNote = el('span', { class: 'gg-menu-note', text: S.title.hostNoLab });
  hostBtn.append(el('h3', { text: S.tables.host }), el('p', { text: S.tables.hostLine }), hostCorner);
  ledger.listen(hostBtn, 'click', (e) => {
    e?.preventDefault?.();
    try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
    if (!gate.canHost) { prime(); return; }
    actions.goHost();
  });
  ledger.listen(hostBtn, 'pointerenter', () => { try { audio?.sfx?.('ui-move'); } catch (_e) { /* stub */ } });

  const pracBtn = el('button', { type: 'button', class: 'gg-ot-act gg-ot-act--prac gg-menu-item' }, [
    el('h3', { text: S.title.practice }),
    el('p', { text: S.tables.practiceLine }),
    // Last in the DOM, top-right on screen: the label is what the button reads as first.
    el('span', { class: 'gg-ot-corner' }, [chip(false, false)]),
  ]);
  ledger.listen(pracBtn, 'click', (e) => {
    e?.preventDefault?.();
    try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
    actions.goPractice();
  });

  const codeIn = el('input', {
    class: 'gg-ot-code-in',
    type: 'text',
    maxlength: '12',
    placeholder: 'K7QP2M',
    autocapitalize: 'characters',
    spellcheck: 'false',
    autocomplete: 'off',
    'aria-label': S.tables.codeLabel,
  });
  const codeBtn = el('button', { type: 'button', class: 'gg-ot-code-go' });
  const codeGo = () => {
    try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub */ }
    if (!gate.canJoin) { prime(); return; }
    const code = normalizeCode(codeIn.value);
    if (code.length === 6 && typeof actions.goJoinCode === 'function') actions.goJoinCode(code);
    else actions.goJoin();
  };
  ledger.listen(codeBtn, 'click', (e) => { e?.preventDefault?.(); codeGo(); });
  ledger.listen(codeIn, 'keydown', (e) => { if (e && e.key === 'Enter') { e.preventDefault?.(); codeGo(); } });
  const codeCard = el('div', { class: 'gg-ot-card' }, [
    el('h4', { text: S.tables.haveCode }),
    el('div', { class: 'gg-ot-code' }, [codeIn, codeBtn]),
  ]);

  /* THE REST OF THE OLD MENU, one quiet row. Same actions, same strings. */
  const menu = el('nav', { class: 'gg-ot-menu', 'aria-label': 'main menu' });
  const item = (label, onClick, { sfx = 'ui-select' } = {}) => {
    const b = button(ledger, label, onClick, { variant: 'ghost', audio, sfx });
    b.classList.add('gg-ot-menu-item', 'gg-menu-item');
    menu.appendChild(b);
    return b;
  };
  item(S.title.assets, () => actions.goAssets());
  /* THE VOICE LIBRARY. Its own item (a place you MAKE something), not a corner of Options. */
  item(S.voice.menu, () => actions.goVoice());
  item(S.title.options, () => ctx.options?.open?.());
  item(S.title.how, () => showHowItWorks());
  if (session.hosted) item(S.title.quit, () => actions.quit('title'), { sfx: 'ui-back' });

  const ribbon = el('div', { class: 'gg-title-ribbon', hidden: true });
  if (ctx.assets && typeof ctx.assets.onState === 'function') {
    ledger.sub(ctx.assets.onState((st) => {
      const show = !!(st && st.available && st.loaded && (st.ready > 0 || st.usedBytes > 0));
      ribbon.textContent = show ? S.assets.ribbon(st.ready, formatBytes(st.usedBytes)) : '';
      ribbon.hidden = !show;
    }));
  }

  rail.append(hostBtn, pracBtn, codeCard, menu, ribbon,
    el('p', { class: 'gg-title-fineprint', text: S.title.fineprint }),
    /* THE BUILD STAMP (bridge.js GOON_BUILD): a diagnostic, never branding. */
    el('p', { class: 'gg-title-buildstamp', text: 'build ' + GOON_BUILD }));

  card.append(col, rail);
  container.appendChild(card);

  /* --- painting ------------------------------------------------------------ */

  function paintGate() {
    hostCorner.replaceChildren(chip(true, !gate.canHost));
    hostBtn.classList.toggle('is-locked', !gate.canHost);
    if (!gate.canHost) {
      hostBtn.setAttribute('aria-disabled', 'true');
      if (!hostNote.parentNode) hostBtn.appendChild(hostNote);
    } else {
      hostBtn.removeAttribute('aria-disabled');
      if (hostNote.parentNode) hostNote.remove();
    }
    codeBtn.replaceChildren();
    if (gate.canJoin) {
      codeBtn.textContent = S.tables.codeJoin;
      codeBtn.removeAttribute('aria-label');
    } else {
      codeBtn.innerHTML = ICON.lock;
      codeBtn.setAttribute('aria-label', S.tables.codeJoin);
    }
  }

  function recordText(name) {
    try {
      const r = rivalry && rivalry.recordFor ? rivalry.recordFor(name) : null;
      return r && r.known ? S.tables.record(r.w, r.l) : '';
    } catch (_e) { return ''; }
  }

  function rowNode(t, i) {
    const isTaken = taken.has(t.code);
    const btn = el('button', { type: 'button', class: 'gg-ot-join' });
    if (isTaken) {
      btn.classList.add('is-taken');
      btn.textContent = S.tables.taken;
      btn.disabled = true;
    } else if (!gate.canJoin) {
      btn.classList.add('is-locked');
      btn.innerHTML = ICON.lock;
      btn.appendChild(document.createTextNode(S.tables.join));
    } else if (busyCode === t.code) {
      btn.classList.add('is-busy');
      btn.textContent = S.tables.sitting;
      btn.disabled = true;
    } else {
      btn.textContent = S.tables.sitDown;
    }
    ledger.listen(btn, 'click', (e) => { e?.preventDefault?.(); void sitDown(t); });
    return el('div', {
      class: 'gg-ot-row' + (t.friend ? ' is-friend' : '') + (isTaken ? ' is-gone' : ''),
      style: '--i:' + i,
      dataset: { code: t.code },
    }, [
      avatarNode(t.name, resolveAvatar(t.avatar, session.net && session.net.serverBase), { online: t.friend }),
      el('div', { class: 'gg-ot-who' }, [
        el('b', { text: t.name }),
        el('div', { class: 'gg-ot-meta' }, tagNodes(t, { record: recordText(t.name) })),
      ]),
      btn,
    ]);
  }

  function group(label, rows, start) {
    if (!rows.length) return null;
    return el('div', { class: 'gg-ot-group' }, [
      el('div', { class: 'gg-ot-gh' }, [label + ' ', el('em', { text: String(rows.length) })]),
      ...rows.map((t, i) => rowNode(t, start + i)),
    ]);
  }

  function emptyNode(title, line) {
    return el('div', { class: 'gg-ot-empty' }, [el('b', { text: title }), line ? el('span', { text: line }) : null]);
  }

  function recentNode() {
    let rows = [];
    try { rows = rivalry && rivalry.recent ? rivalry.recent(3) : []; } catch (_e) { rows = []; }
    if (!rows.length) return null;
    return el('div', { class: 'gg-ot-group' }, [
      el('div', { class: 'gg-ot-gh', text: S.tables.recent }),
      ...rows.map((r, i) => el('div', { class: 'gg-ot-row is-recent', style: '--i:' + i }, [
        avatarNode(r.name, ''),
        el('div', { class: 'gg-ot-who' }, [
          el('b', { text: r.name }),
          el('div', { class: 'gg-ot-meta' }, [el('span', { class: 'gg-ot-tag is-rec', text: S.tables.record(r.w, r.l) })]),
        ]),
        el('span'),
      ])),
    ]);
  }

  function paintList() {
    const tables = open ? open.tables : [];
    const n = tables.filter((t) => !taken.has(t.code)).length;
    liveText.textContent = openState === 'ok' ? (n ? S.tables.waiting(n) : S.tables.nobody) : '';
    live.classList.toggle('is-quiet', !n);
    live.hidden = openState !== 'ok';

    if (openState === 'loading') { listBox.replaceChildren(el('p', { class: 'gg-ot-more', text: S.tables.loading })); return; }
    if (openState === 'signin') { listBox.replaceChildren(emptyNode(S.tables.quiet, S.tables.signIn)); return; }
    if (openState === 'offline') { listBox.replaceChildren(emptyNode(S.tables.quiet, S.tables.offline)); return; }
    if (!tables.length) {
      const ago = open ? agoText(open.lastOpenedAgoSec) : '';
      listBox.replaceChildren(
        ...[emptyNode(S.tables.quiet, ago ? S.tables.lastOpened(ago) : S.tables.neverOpened), recentNode()].filter(Boolean),
      );
      return;
    }
    const g = groupTables(tables);
    listBox.replaceChildren(
      ...[group(S.tables.friends, g.friends, 0), group(S.tables.anyone, g.anyone, g.friends.length)].filter(Boolean),
    );
  }

  /* --- actions ------------------------------------------------------------- */

  function prime() {
    if (sheets?.showPrime) void sheets.showPrime();
    else void sheets?.showSignalError?.('no_host_access');
  }

  async function sitDown(t) {
    if (busyCode || taken.has(t.code)) return;
    try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub */ }
    if (!gate.canJoin) { prime(); return; }
    busyCode = t.code;
    paintList();
    let res = null;
    try { res = await actions.joinStart(t.code); }
    catch (e) { res = { ok: false, error: { kind: 'connect_error', detail: (e && e.message) || '' } }; }
    busyCode = '';
    if (ledger.isDisposed) return;
    if (res && res.ok) return;   // boot's phase router takes it from here (and stamps SEATED)
    const kind = (res && res.error && res.error.kind) || '';
    const outcome = joinOutcome(kind);
    if (outcome === 'taken') {
      taken.add(t.code);
      paintList();
      toasts?.warn?.(S.tables.takenToast);
      try { audio?.sfx?.('ui-error'); } catch (_e) { /* stub */ }
      return;
    }
    if (outcome === 'prime') gate = Object.assign({}, gate, { canJoin: false });
    paintGate();
    paintList();
    await sheets?.showSignalError?.(res && res.error);
  }

  async function refresh() {
    if (typeof actions.openTables !== 'function') { openState = 'offline'; paintList(); return; }
    let res = null;
    try { res = await actions.openTables(); } catch (_e) { res = null; }
    if (ledger.isDisposed) return;
    if (res && res.ok) {
      open = res.data;
      openState = 'ok';
      gate = gateFor({ you: open.you, caps: session.caps, hosted: !standalone });
      // A table that left the list is not "taken" any more, it is just gone.
      for (const c of Array.from(taken)) if (!open.tables.some((t) => t.code === c)) taken.delete(c);
    } else {
      const kind = res && res.error && res.error.kind;
      openState = (kind === 'signin' || kind === 'unauthorized') ? 'signin' : 'offline';
      if (!open) gate = gateFor({ caps: session.caps, hosted: !standalone });
    }
    paintGate();
    paintList();
  }

  const isVisible = () => {
    try { return typeof document === 'undefined' || document.visibilityState !== 'hidden'; } catch (_e) { return true; }
  };
  const ticker = createTicker({ run: refresh, intervalMs: OPEN_POLL_MS, isActive: isVisible });
  ledger.add(() => ticker.stop());
  if (typeof document !== 'undefined') {
    ledger.listen(document, 'visibilitychange', () => { if (isVisible()) ticker.bump(); });
  }

  paintGate();
  paintList();
  ticker.start();

  function showHowItWorks() {
    prefs?.set?.('seenHowItWorks', true);
    /* THE GOAL, ABOVE THE BULLETS (2026-08-05): a lead paragraph (S.coach.howGoal),
     * then the six bullets, so a first read learns what winning is. */
    const body = el('div', { class: 'gg-how' }, [
      el('h2', { class: 'gg-sheet-headline', text: S.how.headline }),
      el('p', { class: 'gg-how-goal', text: S.coach.howGoal }),
      el('ul', { class: 'gg-how-list' }, S.how.bullets.map((b) => el('li', { text: b }))),
    ]);
    sheets?.openNode?.(body, { label: S.how.headline, closeLabel: S.how.close });
  }

  // The first-ever visit opens the explainer once, unprompted.
  if (prefs && !prefs.get('seenHowItWorks')) ledger.timer(showHowItWorks, 420);

  // Build the audio graph on the FIRST screen, not on the first cue (see ui/audio.js).
  try { audio?.unlock?.(); } catch (_e) { /* stub bus */ }

  try { audio?.music?.('title'); } catch (_e) { /* stub bus */ }
  ledger.add(() => { try { audio?.stopMusic?.(); } catch (_e) { /* stub bus */ } });

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
