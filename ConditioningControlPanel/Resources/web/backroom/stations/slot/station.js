/* ============================================================================
 * station.js - the slot station (CONTRACT.md section 7). The room calls
 * mount(ctx) once, then open()/close() per sit-down. Back is live at every
 * frame (Law VI): it never waits on the glb, the server or an animation.
 *
 *   tape.js   what plays next, and the readouts (server-settled outcomes)
 *   scene.js  the cabinet, one WebGL context per open(), freed in close()
 *   media.js  the dealt GIFs and words painted on the reel cells
 * ==========================================================================*/

import { createTape, stopsFor } from './tape.js';
import { createScene, FACES } from './scene.js';
import { createMedia, fxSymbols } from './media.js';

const STATION = 'slot';
const LINE_LABELS = {
  emi3: '3 EMI', gif3same: '3 of the same GIF', sub3: '3 subliminals', spiral3: '3 spirals',
  gif3: '3 GIFs', sub2: '2 subliminals', spiral2: '2 spirals', melt: 'Melt',
};
const fmt = n => Number(n || 0).toLocaleString('en-US');

function loadCss() {
  if (document.querySelector('link[data-slot-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.slotCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  const t = (key, fallback, vars = {}) => {
    const s = typeof ctx.lex === 'function' ? ctx.lex(key, fallback) : fallback;
    return String(s ?? fallback).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
  };
  const reduced = !!ctx.reduced || (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  loadCss();

  let el = null, scene = null, tape = null, media = null, session = 0, alive = false;
  let busy = false, suspended = false, unSp = null, lastMelt = null;
  const $ = sel => el.querySelector(sel);

  function sendMelt(left) {
    if (left === lastMelt) return;
    lastMelt = left;
    const frame = { type: 'melt', station: STATION, left };
    if (typeof ctx.melt === 'function') ctx.melt(left);
    else if (ctx.bridge && typeof ctx.bridge.send === 'function') ctx.bridge.send(frame);
  }

  function build() {
    const root = document.createElement('div');
    root.className = 'slot-station'; root.dataset.phase = 'loading';
    root.innerHTML = `
      <div class="slot-dim"></div>
      <canvas class="slot-stage" aria-label="${t('br_slot_stage', 'Slot cabinet')}"></canvas>
      <div class="slot-media" aria-hidden="true"></div>
      <div class="slot-hint" hidden>${t('br_slot_pull', 'Pull down')} &darr;</div>
      <header class="slot-top">
        <button class="slot-back" type="button">&larr; ${t('br_slot_back', 'Back')}</button>
        <div class="slot-readouts">
          <span class="slot-sp"></span><span class="slot-jackpot"></span>
        </div>
        <div class="slot-status" aria-live="polite"></div>
        <canvas class="slot-face" width="152" height="137" aria-hidden="true"></canvas>
      </header>
      <div class="slot-controls">
        <div class="slot-freeze">${[0, 1, 2].map(i => `<button type="button" data-col="${i}" aria-pressed="false"></button>`).join('')}</div>
      </div>
      <button class="slot-spin" type="button"><span></span><small></small></button>
      <details class="slot-odds"><summary>${t('br_slot_odds', 'Odds')}</summary><table></table><p></p></details>
      <div class="slot-card" role="status" hidden><p></p><button class="slot-card-back" type="button">${t('br_slot_back', 'Back')}</button></div>
      <div class="slot-loading">${t('br_slot_loading', 'Preparing the cabinet')}</div>`;
    root.querySelector('.slot-back').onclick = back;
    root.querySelector('.slot-card-back').onclick = back;
    root.querySelector('.slot-spin').onclick = () => press();
    root.querySelectorAll('[data-col]').forEach(b => { b.onclick = () => toggleFreeze(Number(b.dataset.col)); });
    return root;
  }

  function back() {
    if (typeof ctx.standUp === 'function') ctx.standUp();
    else close();
  }

  function card(text) {
    if (!el) return;
    $('.slot-card p').textContent = text || '';
    $('.slot-card').hidden = !text;
  }

  function refusalText(reason) {
    if (reason === 'insufficient') return t('br_slot_insufficient', 'Not enough SP for this spin.');
    if (reason === 'closed') return t('br_slot_closed', 'The cabinet is closed for a moment.');
    if (reason === 'empty' || reason === 'tape_unplayed') return t('br_slot_stuck', 'The reels stuck. Pull again.');
    return t('br_slot_offline', 'The house is not answering. Try again in a moment.');
  }

  function sync() {
    if (!el || !tape) return;
    const s = tape.snapshot();
    $('.slot-sp').textContent = t('br_slot_sp', '{n} SP', { n: fmt(s.shownSp) });
    $('.slot-jackpot').textContent = t('br_slot_jackpot', 'Jackpot {n}', { n: fmt(s.jackpot) });
    const parts = [t('br_slot_last_win', 'Last win {n}', { n: fmt(s.lastWin) }), t('br_slot_free_left', 'Free spins {n}', { n: s.free })];
    parts.push(s.melt ? t('br_slot_melt_left', 'Melt: {n} spins at half', { n: s.melt }) : t('br_slot_ready', 'Ready'));
    $('.slot-status').textContent = parts.join('  ·  ');
    const playing = el.dataset.phase === 'play';
    el.querySelectorAll('[data-col]').forEach((b, i) => {
      const on = s.hold === i, roman = ['I', 'II', 'III'][i];
      b.setAttribute('aria-pressed', String(on));
      b.textContent = on ? t('br_slot_frozen', 'Frozen {n}', { n: roman }) : t('br_slot_freeze', 'Freeze {n}', { n: roman });
      b.disabled = !playing || busy || !s.canFreeze;
    });
    const spin = $('.slot-spin');
    spin.disabled = !playing || busy;
    spin.querySelector('span').textContent = t('br_slot_spin', 'Spin');
    spin.querySelector('small').textContent =
      s.hold !== null ? t('br_slot_cost', '{n} SP', { n: s.freezeCost })
      : s.nextKind === 'free' || s.nextKind === 'respin' ? t('br_slot_free_spin', 'Free spin')
      : s.onTape ? t('br_slot_on_tape', '{n} left on tape', { n: s.onTape })
      : s.sp >= 1 ? t('br_slot_tape_cost', '{n} SP for {n} spins', { n: Math.min(10, Math.floor(s.sp)) })
      : t('br_slot_cost', '{n} SP', { n: s.stake });
    if (scene) {
      scene.setHold(s.hold);
      scene.screen('marquee', t('br_slot_marquee', 'CANDY'));   // the slot name is still open (6)
      scene.screen('screen_jackpot', t('br_slot_screen_jackpot', 'JACKPOT {n}', { n: fmt(s.jackpot) }));
      scene.screen('screen_status', s.melt ? t('br_slot_screen_melt', 'MELT · {n} SPINS AT HALF', { n: s.melt })
        : t('br_slot_screen_status', 'WIN {n} · FREE {m}', { n: fmt(s.lastWin), m: s.free }));
    }
  }

  function faceFor(o) {
    if (!o) return 'idle0_0';
    if (o.line === 'emi3') return 'jackpot';
    if (o.pay > 0) return 'hearts';
    if (o.meltLeft > 0) return 'melt';
    if (o.freeLeft > 0) return 'spirals';
    return 'idle0_0';
  }

  function drawHud(name) {
    const c = el && $('.slot-face'), img = scene && scene.faceImage;
    if (!c || !img) return;
    const g = c.getContext('2d');
    g.clearRect(0, 0, 152, 137);
    g.drawImage(img, (FACES[name] ?? 3) * 152, 0, 152, 137, 0, 0, 152, 137);
  }
  function setFace(name) { if (scene) scene.setFace(name); drawHud(name); }

  function renderOdds() {
    const s = tape.snapshot();
    const table = $('.slot-odds table');
    table.replaceChildren(...s.lines.map(l => {
      const tr = document.createElement('tr');
      for (const [tag, text] of [['th', t(`br_slot_line_${l.id}`, LINE_LABELS[l.id] || String(l.id))], ['td', fmt(l.pays)], ['td', String(l.odds || '')]]) {
        const cell = document.createElement(tag); cell.textContent = text; tr.append(cell);
      }
      return tr;
    }));
    $('.slot-odds p').textContent = t('br_slot_stake', 'Each spin costs {n} SP. A freeze costs {m} SP.', { n: s.stake, m: s.freezeCost });
  }

  function toggleFreeze(col) {
    if (!alive || busy || !tape || el.dataset.phase !== 'play') return;
    tape.toggleHold(col);
    sync();
  }

  function fire(o) {
    if (suspended || typeof ctx.fx !== 'function') return;
    for (const fxId of Array.isArray(o.fx) ? o.fx : []) {
      try {
        const keys = fxSymbols(fxId, o, media);
        const p = ctx.fx(fxId, keys.length ? keys : undefined);   // never awaited: fx-ack is advisory
        if (p && typeof p.catch === 'function') p.catch(() => {});
      } catch (e) { console.warn('[slot] fx failed', e); }
    }
  }

  async function press() {
    if (!alive || busy || suspended || !scene || el.dataset.phase !== 'play') return;
    const my = session, before = tape.snapshot();
    busy = true; card(null); sync();
    const r = await tape.press();
    if (my !== session || !alive) return;
    if (r.kind !== 'play') {
      busy = false;
      if (r.kind === 'refused') card(refusalText(r.reason));
      sync();
      return;
    }
    const o = r.outcome;
    setFace('idle0_0');
    sync();
    await scene.spin(Array.isArray(o.stops) ? o.stops : stopsFor(before.strips, o.symbols), o.kind === 'freeze' ? before.hold : null);
    if (my !== session || !alive) return;
    tape.land(o);
    fire(o);
    if (o.line === 'emi3') scene.celebrate(o.pay);
    setFace(faceFor(o));
    busy = false;
    sync();
  }

  function onKey(e) {
    if (!alive) return;
    if (e.key === 'Escape') { e.preventDefault(); back(); return; }
    if (e.target && e.target.closest && e.target.closest('button, summary, input, select')) return;
    if (e.code === 'Space') { e.preventDefault(); press(); }
    if (['1', '2', '3'].includes(e.key) && tape && tape.snapshot().canFreeze) toggleFreeze(Number(e.key) - 1);
  }
  const onResize = () => scene && scene.resize();

  async function open() {
    if (alive) return;
    alive = true; busy = false; suspended = false; lastMelt = null;
    const my = ++session;
    el = build();
    ctx.root.append(el);
    addEventListener('keydown', onKey); addEventListener('resize', onResize);
    tape = createTape({ request: (op, body, idem) => ctx.request(op, body, idem), onMelt: sendMelt });
    media = createMedia($('.slot-media'), ctx.lex);
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp(v => { if (tape) { tape.setServerSp(v); sync(); } });
    const dealt = Promise.resolve().then(() => (typeof ctx.media === 'function' ? ctx.media() : null))
      .then(m => (my === session ? media.deal(m) : null)).catch(() => null);
    const [made, state] = await Promise.all([
      createScene({ canvas: $('.slot-stage'), reduced, hint: $('.slot-hint'), canPull: () => !busy && !suspended,
                    onLever: () => press(), onFreeze: col => toggleFreeze(col) }).catch(e => ({ error: e })),
      tape.open(),
    ]);
    if (my !== session) { if (made && made.dispose) made.dispose(); return; }
    $('.slot-loading').hidden = true;
    if (made.error) { console.error('[slot] cabinet failed to load', made.error); el.dataset.phase = 'closed'; card(refusalText('closed')); return; }
    if (made.missing.length) {
      made.dispose();
      el.dataset.phase = 'closed';
      card(t('br_slot_model_missing', 'Model missing {n}', { n: made.missing.join(', ') }));
      return;
    }
    if (!state.ok) { made.dispose(); el.dataset.phase = 'closed'; card(refusalText('closed')); return; }   // 3.4: no state, no fallback table
    scene = made;
    const s = tape.snapshot();
    scene.setStrips(s.strips);
    scene.setStops(s.last && Array.isArray(s.last.stops) ? s.last.stops : stopsFor(s.strips, s.shown));
    scene.setLook({ gif: i => media.gif(i), word: i => media.word(i) });
    dealt.then(() => { if (my === session && scene) scene.setLook({ gif: i => media.gif(i), word: i => media.word(i) }); });
    renderOdds();
    setFace(!s.last && s.melt ? 'melt' : faceFor(s.last));
    el.dataset.phase = 'rise';
    sync();
    await scene.rise();
    if (my !== session) return;
    el.dataset.phase = 'play';
    sync();
  }

  async function close() {
    if (!alive) return;
    alive = false;
    const my = ++session;
    if (tape) {
      tape.abort();
      const cur = tape.cursor();
      if (cur) Promise.resolve(ctx.request('cursor', cur)).catch(() => {});
    }
    removeEventListener('keydown', onKey); removeEventListener('resize', onResize);
    if (typeof unSp === 'function') unSp();
    unSp = null;
    const s = scene, root = el, m = media;
    if (root) root.dataset.phase = 'leaving';
    if (s) await Promise.race([s.sink(), new Promise(r => setTimeout(r, 340))]);
    if (s) s.dispose();
    if (m) m.dispose();
    if (root) root.remove();
    if (my === session) { scene = null; el = null; tape = null; media = null; busy = false; }
  }

  return {
    open,
    close,
    suspend(on) {
      suspended = !!on;
      if (suspended && scene) scene.cancelPull();
      if (el) sync();
    },
    async destroy() {
      await close();
      document.querySelectorAll('link[data-slot-css]').forEach(l => l.remove());
    },
    /** For dev.html and CDP checks only. */
    debug: () => ({ phase: el && el.dataset.phase, busy, alive, snapshot: tape && tape.snapshot(),
                    spinning: !!(scene && scene.spinning), sceneAlive: !!scene }),
  };
}
