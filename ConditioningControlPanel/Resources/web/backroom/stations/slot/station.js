/* ============================================================================
 * station.js - the slot station (CONTRACT.md section 7). The room calls
 * mount(ctx) once, then open()/close() per sit-down. Back is live at every
 * frame (Law VI): it never waits on the glb, the server or an animation.
 *
 *   tape.js   what plays next, and the readouts (server-settled outcomes)
 *   scene.js  the cabinet, one WebGL context per open(), freed in close()
 *   media.js  the dealt GIFs and words painted on the reel cells
 *   pace.js   THE PACE: about 4 s an outcome
 *   feel.js   THE HOUSE BOOK: which move plays, how big, and the Brake (lane F1)
 *   bank.js / sound.js  THE BANK's tokens and the cues
 * ==========================================================================*/

import { createTape, stopsFor } from './tape.js';
import { createScene, FACES } from './scene.js';
import { createMedia, fxSymbols } from './media.js';
import { PACE } from './pace.js';
import { recipe, tierOf, meltedBy, ladderSemis, winTokens, spendTokens, glance, landPose, restPose, pressPose, glanceHoldMs } from './feel.js';
import { createBank } from './bank.js';
import { createSound } from './sound.js';

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
  // CONTRACT 7: inside the room, the room's Back is the only Back (ctx.hostBack). Standalone (dev.html) keeps ours.
  const hostBack = ctx.hostBack === true;
  // CONTRACT 7: one station, three cabinets in the room. variant = { id, name, palette } or null.
  const variant = ctx.variant && typeof ctx.variant === 'object' ? ctx.variant : null;
  // CONTRACT 7.1: the room's SP chip, { set(value), owe(n), thud(), target() }. Standalone keeps .slot-sp.
  const hostSp = ctx.spReadout && typeof ctx.spReadout.set === 'function' && typeof ctx.spReadout.owe === 'function' ? ctx.spReadout : null;
  const lite = String(ctx.intensity || '').toLowerCase() === 'calm';   // Brake 8: Calm bank flies 4 tokens at most
  loadCss();

  let el = null, scene = null, tape = null, media = null, session = 0, alive = false;
  let busy = false, suspended = false, unSp = null, lastMelt = null;
  let pace = 'idle', queued = false, breathEnds = 0, marks = [];   // pace phase: idle | breath | spin | reveal
  // Feel state for one sit-down (lane F1): the readout override while THE BANK flies, the win streak for
  // THE CHIME LADDER, how often each tier has partied (Brake 3), and EMI's current pose (THE MASCOT GLANCE).
  let sound = null, bank = null, shown = null, owing = false, streak = 0, seen = [0, 0, 0, 0, 0], jackpots = 0;
  let pose = 'idle0_0', glanceTimer = 0, gainTimer = 0, playing = null, feelLog = [], bankFrom = 0, bankTo = 0;
  const $ = sel => el.querySelector(sel), wait = ms => new Promise(r => setTimeout(r, Math.max(0, ms)));
  function mark(phase) { pace = phase; marks = [...marks.slice(-79), { phase, at: Math.round(performance.now()) }]; }
  function note(what, extra = {}) { feelLog = [...feelLog.slice(-79), { what, at: Math.round(performance.now()), ...extra }]; }

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
        <span class="slot-sp"></span><span class="slot-jackpot"></span>
        <div class="slot-status" aria-live="polite"></div>
      </header>
      <canvas class="slot-face" width="152" height="137" aria-hidden="true"></canvas>
      <span class="slot-gain" hidden></span>
      <div class="slot-tokens" aria-hidden="true"></div>
      <div class="slot-controls">
        <div class="slot-freeze">${[0, 1, 2].map(i => `<button type="button" data-col="${i}" aria-pressed="false"></button>`).join('')}</div>
      </div>
      <button class="slot-spin" type="button"><span></span><small></small></button>
      <details class="slot-odds"><summary>${t('br_slot_odds', 'Odds')}</summary><table></table><p></p></details>
      <div class="slot-card" role="status" hidden><p></p><button class="slot-card-back" type="button">${t('br_slot_back', 'Back')}</button></div>
      <div class="slot-loading">${t('br_slot_loading', 'Preparing the cabinet')}</div>`;
    root.querySelector('.slot-back').onclick = back;
    root.querySelector('.slot-card-back').onclick = back;
    if (hostSp) root.dataset.hostSp = '';
    if (hostBack) {
      root.dataset.hostBack = '';
      root.querySelector('.slot-back').hidden = true;
      root.querySelector('.slot-card-back').hidden = true;
    }
    root.querySelector('.slot-spin').onclick = () => press();
    root.querySelectorAll('[data-col]').forEach(b => { b.onclick = () => toggleFreeze(Number(b.dataset.col)); });
    return root;
  }

  function back() {
    // Law VIII: Back rings on the frame it is asked (standalone; in the room the room's chip answers its own press).
    if (el && !hostBack) $('.slot-back').classList.add('is-ringing');
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

  /* ONE SP READOUT (in-room tidy, lane F1). In the room, ctx.spReadout is the only SP on screen and THE
   * BANK's target (CONTRACT 7.1): the room keeps its chip on Law I shownSp from what the tape owes (owe), and
   * the station only hands it a number while THE BANK flies (set). Standalone, .slot-sp is both. */
  const readout = () => (hostSp ? hostSp.target() : el && $('.slot-sp'));
  const shownSp = () => (shown ?? (tape ? tape.snapshot().shownSp : 0));
  function paintSp() {
    if (hostSp) { hostSp.set(shown); return; }
    const node = el && $('.slot-sp'), text = t('br_slot_sp', '{n} SP', { n: fmt(shownSp()) });
    if (node && node.textContent !== text) node.textContent = text;
  }

  function sync() {
    if (!el || !tape) return;
    const s = tape.snapshot();
    paintSp();
    $('.slot-jackpot').textContent = t('br_slot_jackpot', 'Jackpot {n}', { n: fmt(s.jackpot) });
    const parts = [t('br_slot_last_win', 'Last win {n}', { n: fmt(s.lastWin) }), t('br_slot_free_left', 'Free spins {n}', { n: s.free })];
    parts.push(s.melt ? t('br_slot_melt_left', 'Melt: {n} spins at half', { n: s.melt }) : t('br_slot_ready', 'Ready'));
    $('.slot-status').textContent = parts.join('  ·  ');
    const playable = el.dataset.phase === 'play';
    el.dataset.pace = pace;
    el.querySelectorAll('[data-col]').forEach((b, i) => {
      const on = s.hold === i, roman = ['I', 'II', 'III'][i];
      b.setAttribute('aria-pressed', String(on));
      b.textContent = on ? t('br_slot_frozen', 'Frozen {n}', { n: roman }) : t('br_slot_freeze', 'Freeze {n}', { n: roman });
      b.disabled = !playable || busy || !s.canFreeze;
    });
    const spin = $('.slot-spin');
    spin.disabled = !playable || (busy && pace !== 'reveal');   // a press in the reveal waits for the breath
    spin.querySelector('span').textContent = t('br_slot_spin', 'Spin');
    spin.querySelector('small').textContent =
      s.hold !== null ? t('br_slot_cost', '{n} SP', { n: s.freezeCost })
      : s.nextKind === 'free' || s.nextKind === 'respin' ? t('br_slot_free_spin', 'Free spin')
      : s.onTape ? t('br_slot_on_tape', '{n} left on tape', { n: s.onTape })
      : s.tapeCount >= 1 ? t('br_slot_tape_cost', '{n} SP for {n} spins', { n: s.tapeCount })
      : t('br_slot_cost', '{n} SP', { n: s.stake });
    if (scene) {
      scene.setHold(s.hold);
      scene.screen('marquee', variant && variant.name ? String(variant.name).toUpperCase() : t('br_slot_marquee', 'CANDY'));
      scene.screen('screen_jackpot', t('br_slot_screen_jackpot', 'JACKPOT {n}', { n: fmt(s.jackpot) }));
      scene.screen('screen_status', s.melt ? t('br_slot_screen_melt', 'MELT · {n} SPINS AT HALF', { n: s.melt })
        : t('br_slot_screen_status', 'WIN {n} · FREE {m}', { n: fmt(s.lastWin), m: s.free }));
    }
  }

  function drawHud(name) {
    const c = el && $('.slot-face'), img = scene && scene.faceImage;
    if (!c || !img) return;
    const g = c.getContext('2d');
    g.clearRect(0, 0, 152, 137);
    g.drawImage(img, (FACES[name] ?? 3) * 152, 0, 152, 137, 0, 0, 152, 137);
  }
  function setFace(name) { pose = name; if (scene) scene.setFace(name); drawHud(name); }
  /** THE MASCOT GLANCE: react now (inside Law VIII's 100 ms), hold, then settle on the rest pose. Never the
   *  same pose twice in a row; the rest is skipped when she is already there. */
  function glanceTo(want, holdMs, rest) {
    clearTimeout(glanceTimer);
    setFace(glance(pose, want));
    note('glance', { pose });
    if (rest) glanceTimer = setTimeout(() => { if (alive && pose !== rest) { setFace(rest); note('rest', { pose }); } }, holdMs);
  }

  /** THE THUD on the SP readout: the bank's last token (a mini-thud). Reduced motion: a lit state, no scale. */
  function thudReadout() {
    if (hostSp) { hostSp.thud(); return; }
    const box = readout();
    if (!box || typeof box.animate !== 'function') return;
    if (reduced) { box.animate([{ boxShadow: '0 0 0 2px #ffcf6b' }, { boxShadow: '0 0 0 2px #ffcf6b' }], { duration: 520 }); return; }
    box.animate([{ transform: 'scale(1.3)', filter: 'brightness(2.2)' }, { transform: 'scale(.94)', offset: 0.55 }, { transform: 'scale(1)', filter: 'brightness(1)' }],
      { duration: 340, easing: 'cubic-bezier(.2,1.5,.4,1)' });
  }
  function gain(n) {
    const g = el && $('.slot-gain');
    if (!g) return;
    g.textContent = t(n >= 0 ? 'br_slot_gain' : 'br_slot_spent', n >= 0 ? '+{n} SP' : '-{n} SP', { n: fmt(Math.abs(n)) });
    g.hidden = false; g.dataset.sign = n >= 0 ? 'up' : 'down';
    clearTimeout(gainTimer); gainTimer = setTimeout(() => { if (el) $('.slot-gain').hidden = true; }, 1600);
  }
  const readoutAt = () => { const b = readout() && readout().getBoundingClientRect(); return b && b.width ? { x: b.left + b.width / 2, y: b.top + b.height / 2 } : null; };
  /** THE BANK forwards (a win) or reversed (a tape or freeze debit). */
  function flyBank(kind, fromValue, toValue, n) {
    shown = fromValue;
    const from = kind === 'pay' ? () => scene && (scene.project('payout_spawn') || scene.project('payout_tray')) : readoutAt;
    const to = kind === 'pay' ? readoutAt : () => scene && (scene.project('payout_tray') || scene.project('payout_spawn'));
    if (kind === 'pay' && scene) scene.trayThud();
    if (!(bank.busy && bank.kind === 'pay' && kind === 'pay')) bankFrom = fromValue;   // a merged pay keeps its first value
    bankTo = toValue;
    const how = bank.start({ kind, n, fromValue, toValue, from, to });
    note('bank', { kind, fromValue, toValue, n, how });
    paintSp();
  }

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
    sync();   // scene.setHold dips the button this frame (Law VIII)
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

  /** The landing beat (Law X): the party the Brake allows, the ladder, the tokens and EMI, all on one frame.
   *  Melt reads from the tape cursor, never a freeze outcome's own meltLeft (the stored tape's end melt). */
  function land(landed, before) {
    const melt = tape.snapshot().melt, o = (landed.meltLeft || 0) === melt ? landed : { ...landed, meltLeft: melt };
    const tier = tierOf(o), melted = meltedBy(o), r = recipe(o, { seen: seen[tier], jackpots });
    if (tier > 0) { seen[tier]++; if (tier === 4) jackpots++; }
    if (tier > 0) { sound.win(r.sound, ladderSemis(streak, melted)); streak++; } else streak = 0;   // the no-pay cue was the last reel's muted thud
    scene.setMelted(melted);
    scene.celebrate(r, o.pay, t('br_slot_screen_win', 'WIN +{n}', { n: fmt(o.pay) }));
    if (r.tokens) flyBank('pay', before, tape.snapshot().shownSp, winTokens(tier, lite));
    glanceTo(landPose(o), glanceHoldMs(melted), restPose(o.meltLeft));
    note('land', { line: o.line, pay: o.pay, tier, party: r.party, sound: r.sound, melted, streak });
  }

  async function press() {
    if (!alive || suspended || !scene || el.dataset.phase !== 'play') return;
    sound.arm();
    if (busy) { if (pace === 'reveal') { queued = true; scene.answer(); note('answer', { queued: true }); } return; }
    const my = session, before = tape.snapshot();
    // Law VIII: the lever leans and EMI glances on this frame, before the tape or the server answers.
    scene.answer(); clearTimeout(glanceTimer); setFace(glance(pose, pressPose()));
    note('answer', { pose });
    busy = true; queued = false; card(null); mark('breath'); sync();
    // THE BREATH (PACE): the next spin starts no sooner than BREATH_MS after the last reveal; the buy runs meanwhile.
    const [r] = await Promise.all([tape.press(), wait(breathEnds - performance.now())]);
    if (my !== session || !alive) return;
    if (r.kind !== 'play') {
      busy = false; mark('idle'); scene.letGo(); setFace(glance(pose, restPose(before.melt)));
      if (r.kind === 'refused') card(refusalText(r.reason));
      sync();
      return;
    }
    const o = r.outcome, after = tape.snapshot();
    if (after.shownSp < before.shownSp) flyBank('spend', before.shownSp, after.shownSp, spendTokens(before.shownSp - after.shownSp, lite));
    scene.setMelted(before.melt > 0);
    playing = { o, lastReel: [2, 1, 0].find(i => i !== r.held) };
    mark('spin'); sync();
    await scene.spin(Array.isArray(o.stops) ? o.stops : stopsFor(before.strips, o.symbols), r.held);
    if (my !== session || !alive) return;
    playing = null;
    const shownBefore = shownSp();
    tape.land(o); fire(o);
    land(o, shownBefore);
    scene.reveal(o.pay > 0); mark('reveal'); sync();
    await wait(PACE.REVEAL_MS);
    if (my !== session || !alive) return;
    breathEnds = performance.now() + PACE.BREATH_MS;
    busy = false; mark('idle'); sync();
    if (queued) press();
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
    alive = true; busy = false; suspended = false; lastMelt = null; pace = 'idle'; queued = false; breathEnds = 0;
    shown = null; streak = 0; seen = [0, 0, 0, 0, 0]; jackpots = 0; pose = 'idle0_0'; playing = null;
    const my = ++session;
    el = build();
    ctx.root.append(el);
    addEventListener('keydown', onKey); addEventListener('resize', onResize);
    tape = createTape({ request: (op, body, idem) => ctx.request(op, body, idem), onMelt: sendMelt });
    media = createMedia($('.slot-media'), ctx.lex);
    sound = createSound();
    bank = createBank({
      layer: $('.slot-tokens'), reduced,
      onTick: (value, kind, quiet) => { shown = value; paintSp(); if (!quiet) sound.token(false); note('tick', { kind, value }); },
      onLand: kind => { if (kind === 'spend' && scene) scene.trayThud(); else { sound.token(true); thudReadout(); }
                        gain(bankTo - bankFrom); },
      onDone: () => { shown = null; paintSp(); },
    });
    if (typeof ctx.onSp === 'function') unSp = ctx.onSp(v => { if (tape) { tape.setServerSp(v); sync(); } });
    const dealt = Promise.resolve().then(() => (typeof ctx.media === 'function' ? ctx.media() : null))
      .then(m => (my === session ? media.deal(m) : null)).catch(() => null);
    const [made, state] = await Promise.all([
      createScene({ canvas: $('.slot-stage'), reduced, palette: variant && variant.palette, hint: $('.slot-hint'), canPull: () => (!busy || pace === 'reveal') && !suspended,
                    onLever: () => press(), onFreeze: col => toggleFreeze(col),
                    onReelStop: i => { const p = playing; sound.thud(i, !!p && i === p.lastReel && !(p.o.pay > 0)); note('thud', { reel: i }); } }).catch(e => ({ error: e })),
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
    // The tape is back: from here the room reads what it owes live (until close hands it a number).
    if (hostSp) { owing = true; hostSp.owe(() => (tape ? tape.snapshot().owed : 0)); }
    const s = tape.snapshot();
    scene.setStrips(s.strips);
    scene.setStops(s.last && Array.isArray(s.last.stops) ? s.last.stops : stopsFor(s.strips, s.shown));
    scene.setLook({ gif: i => media.gif(i), word: i => media.word(i) });
    dealt.then(() => { if (my === session && scene) scene.setLook({ gif: i => media.gif(i), word: i => media.word(i) }); });
    renderOdds();
    setFace(!s.last ? restPose(s.melt) : landPose(s.last));
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
    // Law VI: Back skips every ceremony to its settled state, then hands the room its chip back.
    clearTimeout(glanceTimer); clearTimeout(gainTimer);
    if (bank) { bank.skip(); bank.dispose(); }
    // What a reopen will show: the stored tape's unplayed pays (a freeze's own outcomes are not stored).
    // Before the tape came back the room keeps what it had, so a quick Back never dips the chip either.
    if (hostSp && owing && tape) hostSp.owe(tape.snapshot().tapeOwed);
    if (hostSp) hostSp.set(null);
    owing = false;
    if (sound) sound.dispose();
    const s = scene, root = el, m = media;
    if (s) s.skip();
    if (root) root.dataset.phase = 'leaving';
    if (s) await Promise.race([s.sink(), new Promise(r => setTimeout(r, 340))]);
    if (s) s.dispose();
    if (m) m.dispose();
    if (root) root.remove();
    if (my === session) { scene = null; el = null; tape = null; media = null; busy = false; bank = null; sound = null; shown = null; }
  }

  return {
    open,
    close,
    suspend(on) {
      suspended = !!on;
      if (sound) sound.suspend(suspended);
      if (suspended && bank) bank.skip();
      if (suspended && scene) { scene.cancelPull(); scene.skip(); }
      if (el) sync();
    },
    async destroy() {
      await close();
      document.querySelectorAll('link[data-slot-css]').forEach(l => l.remove());
    },
    /** For dev.html and CDP checks only. */
    debug: () => ({ phase: el && el.dataset.phase, busy, alive, pace, marks, snapshot: tape && tape.snapshot(),
                    hostBack, variant: variant && variant.id, palette: !!(scene && scene.recoloured),
                    spinning: !!(scene && scene.spinning), sceneAlive: !!scene,
                    feel: { log: feelLog, pose, streak, seen, shown, readout: String(shownSp()), hostSp: !!hostSp,
                            cues: sound ? sound.trace.slice() : [], scene: scene && scene.debug() } }),
  };
}
