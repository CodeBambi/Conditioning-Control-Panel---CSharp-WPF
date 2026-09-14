/* ============================================================================
 * station.js - Soft Hand, Twenty-One (CONTRACT 7 and 10.13.F). The room calls
 * mount(ctx) once, then open()/close() per visit. Back is live at every frame
 * (Law VI): nothing here waits on the server, the deal or a moment to leave.
 *
 *   hand.js   reading the server's hand, the live controls, retries, Law I (pure)
 *   feel.js   the timed steps of a reply, moment ids, result text (pure)
 *   table.js  the 2D canvas table and its page effects
 *   shared/hypno  the Loom kit (card backs), the deck (your pictures), moments
 *
 * A SIT-DOWN is one visit: open deals 13 pictures to the 13 values and plays
 * the sit fan; "Stand up, sit back down" re-deals while no hand is open. The
 * server deals and decides every card; the page animates what the reply says
 * and fires moments on the frame the result SHOWS. While a decision is open the
 * screen is held (moments.holdScreen): nothing fullscreen covers the table.
 * ==========================================================================*/

import { createLoomKit, createDeck, createMoments, strengthK, viewportRect, DECK_VALUES } from '../../shared/hypno/index.js';
import { readState, readHand, legalOf, controls, classify, createIntent, mayRetry, moveBody, owedFor, shownSp, defaultStake,
  readHintPref, writeHintPref, isOpen, totalOf, MOVES } from './hand.js';
import { planSteps, momentOf, aceSlot, bestCard, vortexOf, resultLines, TIMING } from './feel.js';
import { createTable } from './table.js';

const fmt = (n) => Number(n || 0).toLocaleString('en-US');
const wait = (ms) => new Promise((r) => setTimeout(r, Math.max(0, ms)));
const mintId = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
const MOVE_LABEL = { hit: 'Hit', stand: 'Stand', double: 'Double', split: 'Split' };
const DECK_WAIT_MS = 2500;

function loadCss() {
  if (document.querySelector('link[data-cards-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = new URL('./station.css', import.meta.url).href; link.dataset.cardsCss = '';
  document.head.append(link);
}

export async function mount(ctx) {
  const t = (key, fallback, vars = {}) => {
    const s = typeof ctx.lex === 'function' ? ctx.lex(key, fallback) : fallback;
    return String(s ?? fallback).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));
  };
  const prefersReduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  const hostBack = ctx.hostBack === true;
  const storage = (() => { try { return globalThis.localStorage || null; } catch (e) { return null; } })();
  const say = (m) => { try { if (ctx.bridge && typeof ctx.bridge.log === 'function') ctx.bridge.log('warn', '[cards] ' + m); } catch (e) { /* noop */ } };
  loadCss();

  let el = null, table = null, kit = null, deck = null, moments = null, chip = null, raf = 0, session = 0, alive = false, suspended = false;
  let st = null, shownHand = null, queue = [], busy = false, decide = false, phase = 'loading', note = '', lines = [];
  let stake = 1, stakePicked = false, hint = readHintPref(storage), dealReadyAt = 0, bloomUntil = 0, sitting = 0, firstSit = true;
  let lastStill = null, unSp = null, feelLog = [], statusText = '';
  const $ = (sel) => el.querySelector(sel);
  const log = (what, extra = {}) => { feelLog = [...feelLog.slice(-99), { what, at: Math.round(performance.now()), ...extra }]; };

  /** Calm, reduced motion and the gates, read live every frame (the loader's ctx getters follow settings frames). */
  function dress() {
    const reduced = !!ctx.reduced || prefersReduced, intensity = String(ctx.intensity || 'normal').toLowerCase(), g = ctx.gates || {};
    return { still: reduced || intensity === 'calm', k: prefersReduced ? 0.5 : strengthK(ctx), full: intensity === 'full' && !reduced,
      gates: { flash: g.flash !== false, spiral: g.spiral !== false, brainDrain: g.brainDrain !== false } };
  }

  function build() {
    const root = document.createElement('div');
    root.className = 'cards-station'; root.dataset.phase = 'loading';
    root.innerHTML = `
      <canvas class="cards-stage"></canvas>
      <header class="cards-top">
        <button class="cards-back" type="button"></button>
        <span class="cards-sp"></span>
        <div class="cards-status" aria-live="polite"></div>
        <div class="cards-hint" hidden></div>
      </header>
      <div class="cards-side">
        <div class="cards-bet" role="group"><span class="cards-bet-label"></span></div>
        <label class="cards-hint-toggle"><input type="checkbox"> <span></span></label>
        <button class="cards-sit" type="button"></button>
        <span class="cards-sitting"></span>
      </div>
      <div class="cards-controls">
        ${MOVES.map((m) => `<button class="cards-move" type="button" data-move="${m}"></button>`).join('')}
        <button class="cards-deal" type="button"><span></span><small></small></button>
      </div>
      <div class="cards-card" role="status" hidden><p></p><button class="cards-card-back" type="button"></button></div>
      <div class="cards-loading"></div>`;
    const q = (s) => root.querySelector(s);
    q('.cards-stage').setAttribute('aria-label', t('br_cards_stage', 'Soft Hand, a Twenty-One table.'));
    q('.cards-back').innerHTML = '&larr; '; q('.cards-back').append(t('br_cards_back', 'Back'));
    q('.cards-card-back').textContent = t('br_cards_back', 'Back');
    q('.cards-bet').setAttribute('aria-label', t('br_cards_bet', 'Bet'));
    q('.cards-bet-label').textContent = t('br_cards_bet', 'Bet');
    q('.cards-hint-toggle span').textContent = t('br_cards_hint_toggle', 'Basic-strategy hint');
    q('.cards-sit').textContent = t('br_cards_sit', 'Stand up, sit back down');
    q('.cards-loading').textContent = t('br_cards_loading', 'Shuffling the deck');
    q('.cards-deal span').textContent = t('br_cards_deal', 'Deal');
    for (const b of root.querySelectorAll('.cards-move')) { b.textContent = t('br_cards_' + b.dataset.move, MOVE_LABEL[b.dataset.move]); b.onclick = () => move(b.dataset.move); }
    q('.cards-back').onclick = back; q('.cards-card-back').onclick = back;
    if (hostBack) { root.dataset.hostBack = ''; q('.cards-back').hidden = true; q('.cards-card-back').hidden = true; }
    q('.cards-deal').onclick = () => deal();
    q('.cards-sit').onclick = () => resit();
    const box = q('.cards-hint-toggle input');
    box.checked = hint;
    box.onchange = () => { hint = box.checked; writeHintPref(storage, hint); };
    return root;
  }

  function back() { if (typeof ctx.standUp === 'function') ctx.standUp(); else close(); }
  function card(text) { if (!el) return; $('.cards-card p').textContent = text || ''; $('.cards-card').hidden = !text; }
  function ring(btn) { if (!btn) return; btn.classList.add('is-ringing'); setTimeout(() => btn.classList.remove('is-ringing'), 400); }   // Law VIII

  /* ------------------------------------------------------------ SP chip */
  function createChip() {
    const hook = ctx.spReadout && typeof ctx.spReadout.set === 'function' && typeof ctx.spReadout.owe === 'function' ? ctx.spReadout : null;
    let server = Number(typeof ctx.sp === 'function' ? ctx.sp() : 0) || 0, owed = 0;
    const paint = () => {
      if (hook) { hook.owe(owed); return; }
      const n = el && $('.cards-sp'), text = t('br_cards_sp', '{n} SP', { n: fmt(shownSp(server, owed)) });
      if (n && n.textContent !== text) n.textContent = text;
    };
    if (hook) { el.dataset.hostSp = ''; hook.set(null); }
    paint();
    return {
      kind: hook ? 'hook' : 'own',
      get server() { return server; }, get owed() { return owed; }, get value() { return shownSp(server, owed); },
      setServer(v) { if (Number.isFinite(Number(v))) { server = Number(v); paint(); } },
      owe(n) { owed = Math.max(0, Math.trunc(Number(n) || 0)); paint(); },
      thud() {
        if (hook) { if (typeof hook.thud === 'function') hook.thud(); return; }
        const n = el && $('.cards-sp');
        if (n && typeof n.animate === 'function') n.animate([{ filter: 'brightness(2)' }, { filter: 'brightness(1)' }], { duration: 340 });
      },
      /** close(): the plain server number, nothing owed, no flight value. */
      handOver() { owed = 0; if (hook) { hook.owe(0); hook.set(null); } else paint(); },
    };
  }

  /* ------------------------------------------------------------ the felt */
  function enqueue(next, { quiet = false } = {}) {
    const steps = planSteps(shownHand, next, { still: dress().still });
    const base = performance.now(), fresh = !shownHand || shownHand.id !== next.id;
    const bets = { at: 0, op: 'bets', list: next.hands.map((h) => h.bet) };
    steps.splice(fresh ? 1 : 0, 0, bets);
    queue = queue.concat(steps.map((s) => ({ ...s, at: base + s.at, hand: next, quiet })));
    shownHand = next;
    decide = false;
    if (!next.done) moments.holdScreen(true);   // nothing fullscreen while a decision is open
  }

  function apply(s, now) {
    const d = dress(), h = s.hand;
    switch (s.op) {
      case 'clear': table.clear(); lines = []; break;
      case 'bets': table.setBets(s.list); break;
      case 'card': table.addCard(s, now); break;
      case 'split': table.split(now); break;
      case 'active': table.setActive(s.index); break;
      case 'reveal': table.reveal(s.code, now); break;
      case 'ready': decide = true; table.setActive(h.active); break;
      case 'bloom': {
        if (s.quiet) break;
        const slot = aceSlot(h), r = table.cardRect(0, slot), canvas = $('.cards-stage');
        const out = moments.play('cards.bloom', { from: r ? viewportRect(canvas, r.x, r.y, r.w, r.h) : undefined, gif: deck ? deck.keyFor(h.hands[0].cards[slot]) : undefined });
        if (out.page.includes('ace_glow')) table.glowCard(0, slot, now);
        bloomUntil = now + TIMING.bloomMs * (d.still ? 0.6 : 1);
        log('moment', { id: 'cards.bloom', tokens: out.tokens.length, page: out.page, held: out.held, from: r });
        break;
      }
      case 'settle': {
        moments.holdScreen(false);
        decide = false;
        chip.owe(0);
        lines = resultLines(h);
        if (s.quiet) { log('settled-quiet', { hand: h.id }); break; }
        chip.thud();
        const id = momentOf(h), best = bestCard(h);
        const out = moments.play(id, { gif: best && deck ? deck.keyFor(best) : undefined });
        if (out.page.includes('win_tunnel')) table.tunnel(now);
        const v = vortexOf(h);
        if (out.page.includes('chip_vortex') && v) table.vortex(v.dir, v.n, now);
        log('moment', { id, tokens: out.tokens.length, page: out.page, held: out.held, net: h.result.net, best });
        break;
      }
      default: break;
    }
  }
  /** Suspend: every step still owed goes down now, quietly (no moments). */
  function flush() { const now = performance.now(); const rest = queue; queue = []; for (const s of rest) apply({ ...s, quiet: true }, now); }

  /* ------------------------------------------------------------ requests */
  async function ask(intent, my) {
    for (;;) {
      const res = await Promise.resolve(ctx.request(intent.op, intent.body, intent.idem)).catch(() => ({ ok: false, reason: 'offline' }));
      if (my !== session) return { kind: 'gone' };
      const c = classify(res);
      if ((c.kind === 'retry' || c.kind === 'wait') && mayRetry(intent, c)) {
        if (c.kind === 'wait') note = t('br_cards_shuffling', 'Shuffling. The next deal is ready in {s} s.', { s: Math.ceil(c.waitMs / 1000) });
        log('retry', { op: intent.op, reason: c.reason, idem: intent.idem, waitMs: c.waitMs });
        await wait(c.waitMs);
        if (my !== session) return { kind: 'gone' };
        continue;
      }
      return c;
    }
  }

  /** A reply's hand goes on the felt. `quiet`: no moments (a hand stood for the player while away). */
  function adopt(body, { quiet = false } = {}) {
    if (Number.isFinite(Number(body.sp))) chip.setServer(body.sp);
    const next = readHand(body.hand);
    st = { ...st, sp: chip.server, hand: next, legal: legalOf(body.legal), hint: MOVES.includes(body.hint) ? body.hint : null };
    chip.owe(body.ok ? owedFor(body) : 0);   // Law I: a settled return lands on its settle frame
    if (next) enqueue(next, { quiet }); else { table.clear(); shownHand = null; }
  }

  async function reply(c, my, op) {
    note = '';
    if (c.kind === 'gone') return;
    if (c.kind === 'ok') {
      if (op === 'deal') dealReadyAt = performance.now() + st.floorMs;
      if (c.body.autoStood) note = t('br_cards_auto_stood', 'Your last hand was stood for you after a day away.');
      adopt(c.body);
    } else if (c.kind === 'adopt') {
      if (c.reason === 'auto_stood') note = t('br_cards_auto_stood', 'Your last hand was stood for you after a day away.');
      if (c.reason === 'illegal') note = t('br_cards_illegal', 'That move is not open on this hand.');
      if (c.reason === 'hand_open') note = t('br_cards_hand_open', 'Your open hand is back on the table.');
      adopt(c.body, { quiet: c.reason === 'auto_stood' });
    } else if (c.kind === 'refresh') await refresh(my);
    else if (c.kind === 'insufficient') { chip.setServer(c.body && c.body.sp); note = t('br_cards_insufficient', 'You need {n} SP for that bet.', { n: stake }); }
    else if (c.kind === 'closed') card(t('br_cards_closed', 'The table is closed for a moment.'));
    else note = t('br_cards_offline', 'The house is not answering. Try again in a moment.');
    log('reply', { op, kind: c.kind, reason: c.reason || null });
  }

  async function refresh(my) {
    const c = classify(await Promise.resolve(ctx.request('state', {})).catch(() => null));
    if (my !== session || c.kind !== 'ok') return;
    st = readState(c.body); chip.setServer(st.sp);
    if (st.hand) enqueue(st.hand, { quiet: st.hand.done }); else { table.clear(); shownHand = null; }
  }

  const view = (now) => controls({ phase, hand: st && st.hand, legal: st ? st.legal : [], sp: chip ? chip.server : 0, stake, busy,
    animating: queue.length > 0, dealReadyAt: Math.max(dealReadyAt, bloomUntil), now });

  async function deal() {
    if (!alive || suspended) return;
    ring($('.cards-deal'));
    const c = view(performance.now());
    if (!c.deal) { if (c.dealWhy === 'sp') note = t('br_cards_insufficient', 'You need {n} SP for that bet.', { n: stake }); return; }
    busy = true; lines = []; note = ''; card(null);
    const my = session;
    const r = await ask(createIntent('deal', { stake }, mintId), my);
    if (my !== session) return;
    busy = false;
    await reply(r, my, 'deal');
  }

  async function move(m) {
    if (!alive || suspended) return;
    ring(el && $(`.cards-move[data-move="${m}"]`));
    if (!view(performance.now()).moves[m]) return;
    busy = true; decide = false; note = '';
    const my = session;
    const r = await ask(createIntent(m, moveBody(st.hand), mintId), my);
    if (my !== session) return;
    busy = false;
    await reply(r, my, m);
  }

  /* ------------------------------------------------------------ sitting */
  async function sitDown(my, deckP) {
    const d = await Promise.race([deckP, wait(DECK_WAIT_MS).then(() => null)]);
    if (my !== session) { deckP.then((x) => x && x.dispose()); return; }
    deck = d;
    if (!d) deckP.then((x) => { if (my === session && alive && !deck) deck = x; else if (x) x.dispose(); });
    sitting++;
    phase = 'sit'; el.dataset.phase = 'sit';
    table.clear(); shownHand = null; lines = [];
    const out = moments.play('cards.sit');
    if (out.page.includes('sit_fan')) table.startFan(performance.now(), dress().still);
    log('moment', { id: 'cards.sit', sitting, page: out.page });
  }
  function afterSit() {
    phase = 'play'; el.dataset.phase = 'play';
    if (firstSit && st && st.hand) enqueue(st.hand, { quiet: st.hand.done });   // an open hand resumes with its decisions live
    firstSit = false;
  }
  function resit() {
    if (!alive || suspended) return;
    ring($('.cards-sit'));
    if (!view(performance.now()).sit) return;
    const old = deck; deck = null;
    if (old) old.dispose();
    sitDown(session, createDeck(ctx, { count: 13, still: dress().still }).catch(() => null));
  }

  /* ------------------------------------------------------------ frame */
  function lineText(l) {
    const dealer = t('br_cards_dealer', 'Emi');
    return (l.prefix ? t(l.prefix.key, l.prefix.fallback, l.prefix.vars) + ' ' : '') + t(l.key, l.fallback, { ...l.vars, dealer });
  }
  function status(c) {
    if (phase === 'loading' || phase === 'closed') return '';
    const dealer = t('br_cards_dealer', 'Emi'), parts = [];
    if (phase === 'sit') {
      parts.push(!dress().gates.flash ? t('br_cards_sit_plain', 'Sitting down at the table.')
        : sitting > 1 ? t('br_cards_sit_again', 'Sitting back down. Your pictures are re-dealt to the thirteen values.')
          : t('br_cards_sit_intro', 'Sitting down. Your pictures are dealt to the thirteen values for this sitting.'));
      return parts.join('\n');
    }
    if (note) parts.push(note);
    if (queue.length && !lines.length) parts.push(t('br_cards_dealing', 'Dealing...'));
    else if (decide && shownHand && !shownHand.done) {
      const h = shownHand.hands[shownHand.active], vars = { p: totalOf(h.cards).total, d: totalOf([shownHand.dealer[0]]).total, dealer, i: shownHand.active + 1 };
      parts.push(shownHand.hands.length > 1 ? t('br_cards_decide_split', 'Hand {i}: {p} against {dealer} showing {d}. Your move.', vars)
        : t('br_cards_decide', '{p} against {dealer} showing {d}. Your move.', vars));
    } else if (lines.length) parts.push(...lines.map(lineText));
    if (phase === 'play' && !queue.length && !busy && !isOpen(st && st.hand)) parts.push(c.dealWhy === 'sp' ? t('br_cards_insufficient', 'You need {n} SP for that bet.', { n: stake }) : t('br_cards_ready', 'Pick a bet and deal.'));
    return [...new Set(parts)].join('\n');
  }

  function sync(now) {
    const c = view(now);
    const open = isOpen(st && st.hand);
    el.toggleAttribute('data-open', open);
    const dealBtn = $('.cards-deal');
    dealBtn.disabled = !c.deal;
    const left = Math.max(dealReadyAt, bloomUntil) - now;
    const small = c.dealWhy === 'floor' ? t('br_cards_wait', '{s} s', { s: Math.ceil(left / 1000) }) : t('br_cards_bet_line', '{n} SP', { n: stake });
    if (dealBtn.querySelector('small').textContent !== small) dealBtn.querySelector('small').textContent = small;
    for (const b of el.querySelectorAll('.cards-move')) {
      const m = b.dataset.move;
      b.disabled = !c.moves[m];
      b.classList.toggle('is-hint', !!(hint && decide && st && st.hint === m && c.moves[m]));
    }
    for (const b of el.querySelectorAll('.cards-bet button')) { b.disabled = !c.bet; b.setAttribute('aria-pressed', String(Number(b.dataset.stake) === stake)); }
    $('.cards-sit').disabled = !c.sit;
    const s = status(c);
    if (s !== statusText) { statusText = s; $('.cards-status').textContent = s; }
    const hintEl = $('.cards-hint'), showHint = !!(hint && decide && st && st.hint);
    const ht = showHint ? t('br_cards_hint_is', 'Hint: {move}.', { move: t('br_cards_' + st.hint, MOVE_LABEL[st.hint]) }) : '';
    if (hintEl.textContent !== ht) hintEl.textContent = ht;
    hintEl.hidden = !showHint;
    const sit = sitting ? t('br_cards_sitting', 'Sitting {n}', { n: sitting }) : '';
    if ($('.cards-sitting').textContent !== sit) $('.cards-sitting').textContent = sit;
  }

  function frame() {
    raf = 0;
    if (!alive || suspended) return;
    const now = performance.now(), d = dress();
    if (d.still !== lastStill) { kit.setStill(d.still); if (deck) deck.setStill(d.still); lastStill = d.still; }
    while (queue.length && queue[0].at <= now) apply(queue.shift(), now);
    if (deck) deck.tick(now);
    table.draw({ now, k: d.k, still: d.still, full: d.full, gates: d.gates, deck, decide,
      print: t('br_cards_print', 'BLACKJACK PAYS 2 TO 1 · DEALER STANDS ON ALL 17s · SIX CARDS WIN'),
      dealerName: t('br_cards_dealer', 'Emi'), youName: t('br_cards_you', 'You'), handName: (i) => t('br_cards_hand_short', 'Hand {i}', { i: i + 1 }) });
    if (phase === 'sit' && table.fanDone(now)) afterSit();
    sync(now);
    raf = requestAnimationFrame(frame);
  }

  function renderStakes() {
    const box = $('.cards-bet');
    for (const b of box.querySelectorAll('button')) b.remove();
    for (const s of st.rules.stakes) {
      const b = document.createElement('button');
      b.type = 'button'; b.dataset.stake = String(s); b.textContent = t('br_cards_stake', '{n} SP', { n: s });
      b.onclick = () => { if (!view(performance.now()).bet) return; stake = s; stakePicked = true; };
      box.append(b);
    }
  }

  function onKey(e) {
    if (!alive) return;
    if (e.key === 'Escape') { if (!e.defaultPrevented) { e.preventDefault(); back(); } return; }
    if (e.target && e.target.closest && e.target.closest('button, input, label, summary, select')) return;
    // Space and Enter deal (never on a held key's repeat). Moves have no letter keys: the room walks on WASD, and a
    // key still down from walking up to the table must never hit, stand or double.
    if ((e.code === 'Space' || e.key === 'Enter') && !e.repeat) { e.preventDefault(); deal(); }
  }

  /* ------------------------------------------------------------ lifecycle */
  async function open() {
    if (alive) return;
    alive = true; suspended = false; busy = false; decide = false; queue = []; shownHand = null; note = ''; lines = []; feelLog = [];
    sitting = 0; firstSit = true; stakePicked = false; dealReadyAt = 0; bloomUntil = 0; phase = 'loading'; statusText = ''; lastStill = null;
    const my = ++session;
    el = build(); ctx.root.append(el);
    addEventListener('keydown', onKey);
    chip = createChip();
    moments = createMoments(ctx, { station: 'cards' });
    kit = createLoomKit({ still: dress().still, log: say });
    table = createTable($('.cards-stage'), { kit: () => kit });
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp((v) => { if (chip) chip.setServer(v); });
    raf = requestAnimationFrame(frame);
    const deckP = createDeck(ctx, { count: 13, still: dress().still }).catch(() => null);
    const c = classify(await Promise.resolve(ctx.request('state', {})).catch(() => null));
    if (my !== session) { deckP.then((x) => x && x.dispose()); return; }
    $('.cards-loading').hidden = true;
    if (c.kind !== 'ok') {
      deckP.then((x) => x && x.dispose());
      phase = 'closed'; el.dataset.phase = 'closed';
      card(c.kind === 'closed' ? t('br_cards_closed', 'The table is closed for a moment.') : t('br_cards_offline', 'The house is not answering. Try again in a moment.'));
      return;
    }
    st = readState(c.body); chip.setServer(st.sp);
    stake = defaultStake(st.sp, st.rules.stakes);
    renderStakes();
    await sitDown(my, deckP);
  }

  function close() {
    if (!alive) return Promise.resolve();
    alive = false;
    ++session;
    removeEventListener('keydown', onKey);
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
    if (typeof unSp === 'function') unSp();
    unSp = null;
    // Law VI: every ceremony skips; the chip gets the plain server number; the kit, the deck and every hold go.
    if (moments) { moments.cancel(); moments.dispose(); }
    if (kit) kit.dispose();
    if (deck) deck.dispose();
    if (chip) chip.handOver();
    if (table) table.dispose();
    if (el) el.remove();
    el = null; table = null; kit = null; deck = null; moments = null; chip = null; queue = []; shownHand = null; busy = false; decide = false;
    return Promise.resolve();
  }

  return {
    open, close,
    suspend(on) {
      if (!alive || suspended === !!on) return;
      suspended = !!on;
      if (suspended) {
        flush();
        moments.cancel();
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        kit.dispose(); kit = createLoomKit({ still: dress().still, log: say });   // the GL context goes; a new one is made on the next draw
      } else if (!raf) raf = requestAnimationFrame(frame);
    },
    destroy() { close(); document.querySelectorAll('link[data-cards-css]').forEach((l) => l.remove()); },
    /** For dev.html and CDP checks only. */
    debug: () => ({
      phase, alive, busy, decide, suspended, hostBack, stake, stakePicked, hint, sitting, queue: queue.length, dress: dress(),
      state: st && { sp: st.sp, legal: st.legal, hint: st.hint, hand: st.hand }, shown: shownHand,
      chip: chip && { kind: chip.kind, value: chip.value, server: chip.server, owed: chip.owed },
      status: statusText, controls: el ? view(performance.now()) : null, table: table && table.debug(), kit: kit && kit.debug(),
      deck: deck && { ...deck.debug(), keys: deck.keys, map: DECK_VALUES.map((v) => deck.keyFor(v)) }, moments: moments && moments.debug(), log: feelLog,
    }),
  };
}
