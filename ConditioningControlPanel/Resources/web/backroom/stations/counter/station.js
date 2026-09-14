/* ============================================================================
 * station.js - The Prize Parlour, the counter (CONTRACT 7 and 10.17.F). DOM only, no WebGL.
 * The room calls mount(ctx) once, then open()/close() per visit. Back is live at every frame
 * (Law VI): the page is on screen before `state` is asked for, and nothing waits on a buy to leave.
 *
 *   cards.js  what the server said, the card faces and the buying flow (no DOM)
 *
 * Elements are made with createElement only (no innerHTML), one card per prize id, updated in place.
 * ==========================================================================*/

import { createCounter, LEX } from './cards.js';
import { audioUrl, altAudioUrl } from '../../../dtrh/shared/audioSrc.js';

const fmt = (n) => Number(n || 0).toLocaleString('en-US');
let cssLink = null;

function h(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}

/** One chime (Brake 1), the race's own clip. The context is made inside the Confirm press. */
function createChime() {
  let ac = null, buf = null, off = false;
  const grab = (url) => fetch(url).then((r) => { if (!r.ok) throw new Error(String(r.status)); return r.arrayBuffer(); })
    .then((raw) => new Promise((res, rej) => ac.decodeAudioData(raw, res, rej)));
  return {
    arm() {
      if (ac || off) return;
      const AC = globalThis.AudioContext || globalThis.webkitAudioContext;
      if (!AC || typeof fetch !== 'function') return;
      try { ac = new AC(); } catch (e) { return; }
      const first = audioUrl(new URL('../../../dtrh/assets/bubbles/sfx/chime1.mp3', import.meta.url).href);
      grab(first).catch(() => { const alt = altAudioUrl(first); if (!alt) throw new Error('no alt'); return grab(alt); })
        .then((b) => { buf = b; }).catch(() => { /* a quiet counter is still a counter */ });
    },
    play() {
      if (!ac || !buf || off || ac.state === 'closed') return;
      try { const s = ac.createBufferSource(), g = ac.createGain(); s.buffer = buf; g.gain.value = 0.34; s.connect(g); g.connect(ac.destination); s.start(); } catch (e) { /* noop */ }
    },
    suspend(on) { off = !!on; if (ac && ac.state !== 'closed') (on ? ac.suspend() : ac.resume()).catch(() => {}); },
    dispose() { if (ac) ac.close().catch(() => {}); ac = null; buf = null; },
  };
}

export async function mount(ctx) {
  const t = (key, fallback, ...args) => {
    const s = typeof ctx.lex === 'function' ? ctx.lex(key, fallback) : fallback;
    return String(s ?? fallback).replace(/\{(\d+)\}/g, (m, i) => (i < args.length ? args[i] : m));
  };
  const L = (key, ...args) => t(key, LEX[key] || key, ...args);
  const hostBack = ctx.hostBack === true;
  const hook = ctx.spReadout && typeof ctx.spReadout.set === 'function' ? ctx.spReadout : null;
  if (!cssLink && typeof document !== 'undefined' && document.head) {
    cssLink = h('link'); cssLink.rel = 'stylesheet'; cssLink.href = new URL('./station.css', import.meta.url).href;
    document.head.append(cssLink);
  }

  let el = null, grid = null, chipEl = null, closedEl = null, cards = new Map(), alive = false, suspended = false, unSp = null, unSet = null;
  const flipped = new Set();
  let chime = createChime();
  const counter = createCounter({ request: (op, body, idem) => ctx.request(op, body, idem), sp: () => (typeof ctx.sp === 'function' ? ctx.sp() : NaN),
    spReadout: hook, chime: () => { if (!suspended) chime.play(); }, onChange: () => paint() });

  function still() {
    const pr = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    return !!ctx.reduced || pr || String(ctx.intensity || '').toLowerCase() === 'calm';
  }
  function back() { if (typeof ctx.standUp === 'function') ctx.standUp(); else close(); }
  const backButton = (cls) => { const b = h('button', cls, t('br_back', 'Back')); b.type = 'button'; b.onclick = back; b.hidden = hostBack; return b; };

  function build() {
    const root = h('div', 'counter-station');
    root.dataset.phase = 'loading';
    if (hostBack) root.dataset.hostBack = '';
    if (hook) root.dataset.hostSp = '';
    const top = h('header', 'counter-top');
    chipEl = h('span', 'counter-sp');
    top.append(backButton('counter-back'), h('h2', 'counter-title', L('br_counter_title')), chipEl);
    grid = h('div', 'counter-grid');
    closedEl = h('div', 'counter-closed');
    closedEl.hidden = true;
    closedEl.append(h('p', null, L('br_counter_closed')), backButton('counter-closed-back'));
    root.append(top, grid, closedEl);
    return root;
  }

  function makeCard(row) {
    const card = h('article', 'counter-card');
    card.dataset.id = row.id;
    const art = h('div', 'counter-art');
    const plate = h('span', 'counter-plate', t(row.nameKey, LEX[row.nameKey] || row.id));
    const img = h('img');
    img.alt = ''; img.decoding = 'async';
    img.onerror = () => { img.remove(); art.dataset.plate = ''; };
    img.src = new URL(`./art/${row.id}.webp`, import.meta.url).href;
    art.append(plate, img);
    const body = h('div', 'counter-body');
    body.append(h('h3', 'counter-name', t(row.nameKey, LEX[row.nameKey] || row.id)), h('p', 'counter-blurb', t(row.blurbKey, LEX[row.blurbKey] || '')));
    if (row.noteKey) body.append(h('p', 'counter-note', t(row.noteKey, LEX[row.noteKey] || '')));
    const foot = h('div', 'counter-foot');
    const price = h('span', 'counter-price');
    const act = h('div', 'counter-act');
    foot.append(price, act);
    const confirm = h('div', 'counter-confirm');
    body.append(foot, confirm);
    card.append(art, h('div', 'counter-sheet'), body);
    return { card, price, act, confirm, sig: '' };
  }

  function actFor(v, c) {
    const nodes = [], ae = typeof document !== 'undefined' ? document.activeElement : null;
    if (ae && (ae === c.yes || ae === c.no)) c.refocus = ae === c.no ? 'no' : 'yes';   // a repaint never drops the keyboard off the confirm
    c.buy = null; c.yes = null; c.no = null;
    if (v.face === 'owned') {
      nodes.push(h('span', 'counter-badge is-owned', L('br_counter_owned')));
      if (v.deliveryKey) nodes.push(h('p', 'counter-delivery', L(v.deliveryKey)));
    } else if (v.face === 'soon') nodes.push(h('span', 'counter-badge is-soon', L('br_counter_soon')));
    else if (v.face === 'discord') nodes.push(h('span', 'counter-badge is-discord', L('br_counter_link_discord')));
    else {
      const b = h('button', 'counter-buy', v.face === 'short' ? L('br_counter_short', fmt(v.short)) : L('br_counter_buy'));
      b.type = 'button';
      b.disabled = v.face === 'short' || !!v.confirm;
      b.onclick = () => { if (counter.ask(v.id)) focus(cards.get(v.id).yes); };
      nodes.push(b);
      c.buy = b;
    }
    c.act.replaceChildren(...nodes);
    if (!v.confirm) { c.refocus = null; c.confirm.replaceChildren(); c.confirm.hidden = true; return; }
    const k = v.confirm;
    const yes = h('button', 'counter-yes', L('br_counter_confirm')), no = h('button', 'counter-no', L('br_counter_cancel'));
    yes.type = 'button'; no.type = 'button';
    yes.disabled = k.pending; no.disabled = k.pending;
    if (k.pending) yes.setAttribute('aria-busy', 'true');
    yes.onclick = () => { chime.arm(); counter.buy(); };
    no.onclick = () => { if (counter.cancel()) focus(cards.get(v.id).buy); };
    c.yes = yes; c.no = no;
    const row = h('div', 'counter-yesno');
    row.append(yes, no);
    const lines = [h('strong', 'counter-confirm-name', t(v.nameKey, LEX[v.nameKey] || v.id)),
      h('span', 'counter-confirm-price', L('br_counter_price', fmt(k.priceSp))),
      h('span', 'counter-after', L('br_counter_after', fmt(k.after)))];
    if (k.retry) lines.push(h('p', 'counter-retry', L('br_counter_retry')));
    c.confirm.replaceChildren(...lines, row);
    c.confirm.hidden = false;
    if (c.refocus && !k.pending) { focus(c.refocus === 'no' ? no : yes); c.refocus = null; }
  }

  function focus(b) { try { if (b && typeof b.focus === 'function') b.focus(); } catch (e) { /* noop */ } }

  function paint() {
    if (!el) return;
    const phase = counter.phase;
    el.dataset.phase = phase;
    if (still()) el.dataset.still = ''; else delete el.dataset.still;
    closedEl.hidden = phase !== 'closed';
    grid.hidden = phase === 'closed';
    chipEl.textContent = L('br_counter_price', fmt(counter.sp()));
    if (phase === 'closed') return;
    for (const v of counter.view()) {
      let c = cards.get(v.id);
      if (!c) { c = makeCard(v); cards.set(v.id, c); grid.append(c.card); }
      c.card.dataset.face = v.face;
      c.price.textContent = L('br_counter_price', fmt(v.priceSp));
      if (v.flip && !flipped.has(v.id)) { flipped.add(v.id); if (!still()) c.card.classList.add('is-flip'); }
      const sig = JSON.stringify([v.face, v.short, v.deliveryKey, v.confirm, v.priceSp]);
      if (sig !== c.sig) { c.sig = sig; actFor(v, c); }
    }
  }

  function onKey(e) {
    if (!alive || e.defaultPrevented) return;
    // A held Enter on Buy would auto-repeat straight onto the focused Confirm: a repeat never presses a button.
    if (e.repeat && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); return; }
    if (e.key !== 'Escape') return;
    e.preventDefault();
    back();
  }

  async function open() {
    if (alive) return;
    alive = true; suspended = false; cards = new Map(); flipped.clear();
    el = build();
    ctx.root.append(el);
    globalThis.addEventListener('keydown', onKey);
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp(() => paint());
    if (typeof ctx.onSettings === 'function') unSet = ctx.onSettings(() => paint());
    paint();
    await counter.open();
  }

  function close() {
    if (!alive) return Promise.resolve();
    alive = false;
    counter.close();
    globalThis.removeEventListener('keydown', onKey);
    if (typeof unSp === 'function') unSp();
    if (typeof unSet === 'function') unSet();
    unSp = null; unSet = null;
    chime.dispose(); chime = createChime();
    if (el) el.remove();
    el = null; grid = null; chipEl = null; closedEl = null; cards = new Map();
    return Promise.resolve();
  }

  return {
    open, close,
    suspend(on) { suspended = !!on; chime.suspend(suspended); },
    destroy() { close(); if (cssLink) { cssLink.remove(); cssLink = null; } },
    /** For dev.html and the checks only. */
    debug: () => ({ alive, suspended, hostBack, still: still(), phase: counter.phase, sp: counter.sp(), confirm: counter.confirm,
      cards: counter.view().map((v) => ({ id: v.id, face: v.face, short: v.short || 0, deliveryKey: v.deliveryKey })), log: counter.log }),
  };
}
