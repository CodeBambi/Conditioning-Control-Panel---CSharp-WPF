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
import { demoKind, demoFrame, demoLabel } from './demo.js';
import { prizeState } from '../../shared/prize-state.js';
import { kit } from '../../shared/sound/kit.js';
import { reopenWelcome } from '../../room/welcome.js';

export const roomBehind = true;

const fmt = (n) => Number(n || 0).toLocaleString('en-US');
let cssLink = null;
const nowMs = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());
const raf = (fn) => (typeof requestAnimationFrame === 'function' ? requestAnimationFrame(fn) : setTimeout(() => fn(nowMs()), 16));
const unraf = (id) => (typeof cancelAnimationFrame === 'function' ? cancelAnimationFrame(id) : clearTimeout(id));

/** Only the two origins the room maps (and this page's own, for dev.html) may put a picture in a preview. */
function allowedUrl(url) {
  try {
    const u = new URL(url, typeof location !== 'undefined' ? location.href : 'https://ccp.game/');
    return u.origin === 'https://ccp.assets' || u.origin === 'https://ccp.game' || (typeof location !== 'undefined' && u.origin === location.origin);
  } catch (e) { return false; }
}

function h(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}

/** One delivery cue per successful buy. The kit is armed inside the Confirm press. */
function createChime(k = kit) {
  let off = false;
  return {
    arm() { if (!off) k.arm(); },
    play() { if (off) return; k.play('prize-drop'); },
    suspend(on) { off = !!on; },
    dispose() { off = true; },
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
  let demo = null, media = null, dealt = null, demos = 0;   // the running "Try it" preview, the deal asked for it (once per visit)
  const flipped = new Set();
  let chime = createChime();
  const counter = createCounter({ request: (op, body, idem) => ctx.request(op, body, idem), sp: () => (typeof ctx.sp === 'function' ? ctx.sp() : NaN),
    spReadout: hook, chime: () => { if (!suspended) chime.play(); }, onChange: () => paint() });

  function still() {
    const pr = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    return ['off','still','reduced'].includes(String(ctx.motion||'').toLowerCase()) || !!ctx.reduced || pr || String(ctx.intensity || '').toLowerCase() === 'calm';
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
    // Not a third card: a chip that lays the first-visit card back over the room, page 1, arrow to page 2.
    const how = h('button', 'counter-how', L('br_counter_how')); how.type = 'button';
    how.onclick = () => reopenWelcome(0);
    top.append(backButton('counter-back'), h('h2', 'counter-title', L('br_counter_title')), how, chipEl);
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
    const sold = h('span', 'counter-sold', t('br_counter_sold', 'SOLD'));
    art.append(plate, img, sold);
    const body = h('div', 'counter-body');
    body.append(h('h3', 'counter-name', t(row.nameKey, LEX[row.nameKey] || row.id)), h('p', 'counter-blurb', t(row.blurbKey, LEX[row.blurbKey] || '')));
    const detailKey = `br_prize_${row.id}_details`;
    if (LEX[detailKey]) {
      const details = h('details', 'counter-details'); details.open = true;
      details.append(h('summary', null, L('br_counter_details')), h('p', null, L(detailKey)));
      body.append(details);
    }
    if (row.noteKey) body.append(h('p', 'counter-note', t(row.noteKey, LEX[row.noteKey] || '')));
    const foot = h('div', 'counter-foot');
    const price = h('span', 'counter-price');
    const act = h('div', 'counter-act');
    let tryBtn = null;
    if (demoKind(row.id)) {
      tryBtn = h('button', 'counter-try', L('br_counter_try'));
      tryBtn.type = 'button';
      tryBtn.onclick = () => startDemo(row.id);
    }
    foot.append(price, ...(tryBtn ? [tryBtn] : []), act);
    const confirm = h('div', 'counter-confirm');
    body.append(foot, confirm);
    card.append(art, h('div', 'counter-sheet'), body);
    return { card, art, price, act, confirm, tryBtn, sig: '' };
  }

  /* ------------------------------------------------------------ "Try it" (demo.js)
   * A preview of an effect prize, drawn by the page inside the card's art box with the pictures the host dealt
   * this visit. It never posts an fx: the real effects are the app's overlays and the host plays them only for
   * an account that owns the grant, so a demo the host rendered would be the prize for free. Nothing pays SP.
   * Calm and reduced motion show the settled frame and hold it; suspend, Back and close stop it (Law VI). */
  function askMedia() {
    if (media) return;
    media = Promise.resolve().then(() => (typeof ctx.media === 'function' ? ctx.media({ count: 4 }) : null))
      .then((m) => {
        if (!alive) return;
        dealt = m && Array.isArray(m.gifs) ? m.gifs.slice(0, 4) : [];
        if (typeof Image === 'function') for (const g of dealt) {
          if (g && allowedUrl(g.url)) { const img = new Image(); img.src = g.url; }
        }
      })
      .catch(() => { dealt = []; });
  }
  function stopDemo() {
    if (!demo) return;
    unraf(demo.raf);
    demo.stage.remove();
    delete demo.art.dataset.demo;
    demo = null;
  }
  function startDemo(id) {
    if (!alive || suspended) return;
    const c = cards.get(id), kind = demoKind(id);
    if (!c || !kind) return;
    stopDemo();
    askMedia();
    const stage = h('div', 'counter-demo');
    const pics = [];
    for (let i = 0; i < 4; i++) {
      const p = h(kind === 'bubbles' ? 'span' : 'img', 'counter-demo-pic'); p.alt = ''; p.decoding = 'async'; p.dataset.pic = String(i); p.hidden = true;
      pics.push(p); stage.append(p);
    }
    const caption = h('span', 'counter-demo-caption'); stage.append(caption);
    c.art.append(stage);
    c.art.dataset.demo = kind;
    demos++;
    demo = { id, kind, art: c.art, stage, pics, t0: nowMs(), waitingSince: nowMs(), raf: 0 };
    const tick = () => {
      if (!demo) return;
      const gates = ctx.gates || {}, pictures = gates.flash !== false;
      if (demo.kind !== 'bubbles' && pictures && nowMs() - demo.waitingSince < 3500 &&
          (!dealt || (typeof Image === 'function' && !demo.pics.some(p => p.complete && p.naturalWidth > 0)))) demo.t0 = nowMs();
      const frame = demoFrame(demo.kind, nowMs() - demo.t0, { still: still() });
      if (!frame.length) { stopDemo(); return; }
      const label = demoLabel(demo.kind, still() ? 0 : nowMs() - demo.t0);
      caption.textContent = label ? L('br_counter_demo_' + label) : '';
      demo.pics.forEach((p, i) => {
        const s = frame[i];
        p.hidden = !s;
        if (!s) return;
        const g = demo.kind !== 'bubbles' && pictures && dealt ? dealt[s.pic % Math.max(1, dealt.length)] : null;
        const url = g && typeof g.url === 'string' && allowedUrl(g.url) ? g.url
          : pictures && demo.kind !== 'bubbles' ? new URL('../slot/fallback/gif' + (s.pic % 4) + '.webp', import.meta.url).href : '';
        if (url && p.dataset.url !== url) { p.dataset.url = url; p.src = url; }
        if (!url && p.dataset.url) { delete p.dataset.url; p.removeAttribute && p.removeAttribute('src'); }
        p.dataset.kind = s.kind; p.dataset.variant = s.variant || '';
        stage.dataset.variant = s.variant || '';
        if (s.pivot) p.dataset.pivot = s.pivot; else delete p.dataset.pivot;
        const st = p.style;
        st.clipPath = s.kind === 'shard' ? ['polygon(0 0,100% 0,50% 50%)','polygon(100% 0,100% 100%,50% 50%)','polygon(100% 100%,0 100%,50% 50%)','polygon(0 100%,0 0,50% 50%)'][s.shard] : '';
        st.left = (s.x * 100).toFixed(2) + '%';
        st.top = (s.y * 100).toFixed(2) + '%';
        st.width = (s.scale * 100).toFixed(2) + '%';
        st.opacity = String(Math.max(0, Math.min(1, s.alpha)));
        st.transform = (s.pivot === 'top' ? 'translate(-50%, 0)' : 'translate(-50%, -50%)') + ' rotate(' + s.rot.toFixed(2) + 'deg)';
      });
      demo.raf = raf(tick);
    };
    tick();
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
    const snapshot = counter.state && { ok:true, catalog:counter.state.catalog, prizes:{grants:counter.state.grants} };
    const unlocked = prizeState(snapshot)?.demo;
    for (const v of counter.view()) {
      let c = cards.get(v.id);
      if (!c) { c = makeCard(v); cards.set(v.id, c); grid.append(c.card); }
      c.card.hidden = v.id === 'rt_demo' && unlocked;
      c.card.dataset.face = v.face;
      c.price.textContent = L('br_counter_price', fmt(v.priceSp));
      if (c.tryBtn) c.tryBtn.hidden = v.face === 'soon';   // under the dust sheet nothing is tried
      if (still() || suspended) c.card.classList.remove('is-flip');
      if (phase === 'ready' && v.flip && !flipped.has(v.id)) {
        flipped.add(v.id);
        if (demo?.id === v.id) stopDemo();
        ctx.prizesChanged?.(snapshot, v.id);
        if (!still() && !suspended) c.card.classList.add('is-flip');
      }
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
    ctx.root.addEventListener?.("click", outside);
    if(!still())el.animate?.([{transform:"translateY(-115%)",opacity:0},{transform:"translateY(0)",opacity:1}],{duration:460,easing:"cubic-bezier(.18,.8,.24,1)"});
    globalThis.addEventListener('keydown', onKey);
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp(() => paint());
    if (typeof ctx.onSettings === 'function') unSet = ctx.onSettings(() => paint());
    paint();
    askMedia();
    await counter.open();
    if (alive && counter.state) ctx.prizesChanged?.({ok:true, catalog:counter.state.catalog, prizes:{grants:counter.state.grants}});
  }

  function outside(e){if(e.target===ctx.root)back();}

  async function close() {
    if (!alive) return Promise.resolve();
    alive = false;
    stopDemo();
    media = null; dealt = null;
    counter.close();
    globalThis.removeEventListener('keydown', onKey);
    if (typeof unSp === 'function') unSp();
    if (typeof unSet === 'function') unSet();
    unSp = null; unSet = null;
    chime.dispose(); chime = createChime();
    ctx.root.removeEventListener?.("click", outside);
    const retiring=el;
    if(retiring&&!still()&&retiring.animate){retiring.style.pointerEvents="none";await retiring.animate([{transform:"translateY(0)",opacity:1},{transform:"translateY(-115%)",opacity:0}],{duration:300,easing:"ease-in",fill:"forwards"}).finished.catch(()=>{});}
    if (retiring) retiring.remove();
    el = null; grid = null; chipEl = null; closedEl = null; cards = new Map();
    return Promise.resolve();
  }

  return {
    open, close,
    suspend(on) { suspended = !!on; chime.suspend(suspended); if (suspended) { stopDemo(); for (const c of cards.values()) c.card.classList.remove('is-flip'); } },
    destroy() { close(); if (cssLink) { cssLink.remove(); cssLink = null; } },
    /** For dev.html and the checks only. */
    debug: () => ({ alive, suspended, hostBack, still: still(), phase: counter.phase, sp: counter.sp(), confirm: counter.confirm,
      demo: demo ? { id: demo.id, kind: demo.kind, pics: demo.pics.filter((p) => !p.hidden).length } : null, demos, dealt: dealt ? dealt.length : null,
      cards: counter.view().map((v) => ({ id: v.id, face: v.face, short: v.short || 0, deliveryKey: v.deliveryKey })), log: counter.log }),
  };
}
