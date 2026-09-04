/* ============================================================================
 * backroom/index.js - THE FLOOR.
 *
 * The Back Room is a SCENE, like the Annex and the Records Office: no registry
 * row, no timetable deal, no grade, no punch card. The shell walks the student
 * to a door and calls `openBackRoom(ctx)`; this file paints the room behind it
 * and hands back the same `{root, escapeStep, destroy, fit}` handle every other
 * scene answers with, plus `close()` for the caller that asked for one.
 *
 * WHAT IS ON THE FLOOR: a header with the chip balance and a lit way out, the
 * pot sign, THE CAGE (backroom/cage.js) and five cabinet frames. A cabinet
 * whose module has not shipped yet is a dust sheet, not an error.
 *
 * THREE LAWS THIS FILE IS WRITTEN AROUND.
 *  - EXITS SACRED (Law VI). The sign leaves mid-anything and is never covered
 *    by a modal. `escapeStep()` folds a mounted machine first and then answers
 *    false so the shell can walk home. Nothing here binds a key: the shell owns
 *    the ladder, which is exits.js's wiring law.
 *  - LEDGER TRUTH. Not one chip is added or taken away on this page. Every
 *    number painted here arrived on a server frame, and the only optimism is
 *    the BANK animation, which plays while the real balance is being written.
 *  - IT NEVER THROWS. No `send`, no `listen`, a dead module, a host that never
 *    answers: each one is a thing the room DRAWS, never a thing it dies of.
 * ==========================================================================*/

import { createCasinoApi } from './kit/api.js';
import { balanceChip, chipStack, bank, fmtChips } from './kit/chips.js';
import { makeT, BK_LEX } from './lex.js';
import { createVoice } from './kit/voice.js';
import { createCage } from './cage.js';

/** The five cabinets, in the order the room reads left to right. The stakes
 *  here are a FLOOR for the frames only: the live numbers arrive on the status
 *  frame's `config.stakes`, so the page never publishes odds of its own. */
const CABINETS = Object.freeze([
  { key: 'twentyone', nameKey: 'bk_cab_twentyone', subKey: 'bk_sub_twentyone', stake: 25 },
  { key: 'triple', nameKey: 'bk_cab_triple', subKey: 'bk_sub_triple', stake: 10 },
  { key: 'spiral', nameKey: 'bk_cab_spiral', subKey: 'bk_sub_spiral', stake: 25 },
  { key: 'scratcher', nameKey: 'bk_cab_scratcher', subKey: 'bk_sub_scratcher', stake: 50 },
  { key: 'wheel', nameKey: 'bk_cab_wheel', subKey: 'bk_sub_wheel', stake: 500 },
]);

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** The scene sfx door, copied rather than imported (the kit does not reach into
 *  the shell), in exits.js's defensive shape so the module still loads under a
 *  DOM double that has no CustomEvent. */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign({ name: String(name || 'blip'), level: Number(level) || 0.4, bus: 'fx' }, extra || {}),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/** lab.js's lazy sheet, id-guarded so a revisit costs nothing. */
function ensureSheet(id, rel, log) {
  try {
    if (document.getElementById(id)) return;
    const link = document.createElement('link');
    link.id = id;
    link.rel = 'stylesheet';
    link.href = new URL(rel, import.meta.url).href;
    document.head.appendChild(link);
  } catch (e) { log('backroom sheet failed: ' + rel); }
}

/** os.js's three-test still mode. Every rAF loop in the kit gates on this,
 *  because the CSS freeze at the bottom of styles.css cannot reach JS. */
function stillMode(ctx) {
  try { if (ctx && ctx.motion && ctx.motion.reducedMotion) return true; } catch (e) { /* noop */ }
  try {
    if (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches) return true;
  } catch (e) { /* noop */ }
  try { return document.documentElement.classList.contains('arc-reduced'); } catch (e) { return false; }
}

/**
 * openBackRoom(ctx) -> { root, close, destroy, escapeStep, fit }
 *
 * `ctx` is the scene surface plus `send(msg)` and `listen(type, fn)`. EVERY
 * field is optional. A ctx with no send/listen opens a room whose cage is dark
 * and whose cabinets are honest about it, which is exactly what a rig, a
 * fixture page and a web host without the relay should see.
 */
export function openBackRoom(ctx) {
  const c = ctx || {};
  const log = (typeof c.log === 'function') ? c.log : () => {};
  const t = makeT(c.t || c.lexicon);
  const reduced = stillMode(c);
  const api = createCasinoApi({ send: c.send, listen: c.listen, log });
  // The dealer takes the SHELL's plain t(key, fallback), not the floor's bkT:
  // her lines carry no slots, and a mod re-voices her with lexicon rows alone.
  const voice = createVoice({ t: c.t, rng: (c.rng && c.rng.next) || null });

  ensureSheet('arc-backroom-css', './backroom.css', log);

  let dead = false;
  let snap = null;        // the last ok status body
  let mounted = null;     // { key, handle, mod }

  /* --------------------------------------------------------------- the room */
  const root = el('div', 'bk-room' + (c.lite ? ' bk-lite' : ''));
  root.setAttribute('role', 'region');
  root.setAttribute('aria-label', t('bk_title'));

  const head = el('div', 'bk-head');
  const chips = balanceChip({ label: t('bk_chips'), value: 0, reduced });
  const stack = chipStack({ value: 0 });
  head.appendChild(el('h2', 'bk-title', t('bk_title')));
  head.appendChild(el('span', 'bk-pot-sub', t('bk_sub')));
  head.appendChild(el('span', 'bk-head-spacer'));
  head.appendChild(stack.el);
  head.appendChild(chips.el);

  /* THE WAY OUT, LIT, AND IT IS THE FIRST THING BUILT. Law VI is not a feature
   * that gets added at the end: the sign exists before the cage does, it is
   * never disabled, and it never waits on a request in flight. */
  const exitBtn = el('button', 'btn primary', t('bk_exit'));
  exitBtn.type = 'button';
  exitBtn.addEventListener('click', () => leave());
  try {
    if (c.exits && typeof c.exits.sign === 'function') c.exits.sign(exitBtn, { dir: 'back' });
  } catch (e) { /* an undressed button still leaves */ }
  head.appendChild(exitBtn);
  root.appendChild(head);

  /* ---------------------------------------------------------- the pot sign */
  const pot = el('div', 'bk-pot');
  const potN = el('b', 'bk-pot-n', t('bk_pot_unknown'));
  pot.appendChild(el('span', 'bk-pot-label', t('bk_pot')));
  pot.appendChild(potN);
  pot.appendChild(el('span', 'bk-pot-sub', t('bk_pot_sub')));
  root.appendChild(pot);

  /* -------------------------------------------------------------- the cage */
  const cage = createCage({
    t, api, voice, sfx, log,
    onSettled: (body, from) => takeStatus(Object.assign({}, snap || {}, body || {}), from),
  });
  root.appendChild(cage.el);

  /* ---------------------------------------------------------- the cabinets */
  const floor = el('div', 'bk-floor');
  floor.setAttribute('role', 'group');
  floor.setAttribute('aria-label', t('bk_floor_aria'));
  root.appendChild(floor);
  const stage = el('div', 'bk-stage');
  stage.hidden = true;
  root.appendChild(stage);

  const frames = new Map();
  for (const cab of CABINETS) {
    const btn = el('button', 'bk-cab');
    btn.type = 'button';
    btn.appendChild(el('span', 'bk-cab-name', t(cab.nameKey)));
    btn.appendChild(el('span', 'bk-cab-sub', t(cab.subKey)));
    const stakeEl = el('span', 'bk-cab-stake', t('bk_cab_stake', [fmtChips(cab.stake)]));
    btn.appendChild(stakeEl);
    btn.addEventListener('click', () => openCabinet(cab.key));
    floor.appendChild(btn);
    frames.set(cab.key, { cab, btn, stakeEl, mod: null });
  }

  /** Paint the balances a frame carried. `bankFrom` runs THE BANK on the way. */
  function paintBalances(body, bankFrom) {
    if (!body || typeof body !== 'object') return;
    const next = Math.max(0, Math.round(Number(body.chips) || 0));
    const land = () => { chips.set(next, { reduced }); stack.set(next); };
    if (bankFrom) {
      sfx('bank', 0.4);
      bank(bankFrom, chips.el, { count: 6, reduced }).then(land, land);
    } else land();
    if (body.pot != null) {
      const p = Math.max(0, Math.round(Number(body.pot) || 0));
      potN.textContent = fmtChips(p);
      pot.classList.toggle('bk-live', p > 0);
    }
  }

  /** One status frame, folded in. The only writer of `snap`. */
  function takeStatus(body, bankFrom) {
    if (!body || typeof body !== 'object') return;
    snap = body;
    api.seed(body);
    paintBalances(body, bankFrom);
    if (body.config && body.config.stakes && typeof body.config.stakes === 'object') {
      for (const [key, f] of frames) {
        const v = Number(body.config.stakes[key]);
        if (v > 0) f.stakeEl.textContent = t('bk_cab_stake', [fmtChips(Math.round(v))]);
      }
    }
    const scr = frames.get('scratcher');
    if (scr && scr.mod && body.free_scratch_available === true) scr.stakeEl.textContent = t('bk_cab_free');
    cage.paint(snap);
  }

  /** What a machine module is handed. One object, built once, shared by all. */
  const kit = {
    api,
    t,
    voice,
    lex: BK_LEX,
    reduced,
    lite: !!c.lite,
    log,
    sfx,
    fmtChips,
    balanceChip,
    chipStack,
    bank,
    /** The last status body, or null. Machines read it, never write it. */
    status: () => snap,
    /** The live stake table off the status frame. */
    config: () => (snap && snap.config) || {},
    /** Fold a play answer's balances back into the room, optionally banking. */
    settle: (body, bankFrom) => takeStatus(Object.assign({}, snap || {}, body || {}), bankFrom),
    /** Ask the counter again, when a machine could not fold an answer in. */
    refresh: () => api.status().then((r) => { if (r.ok) takeStatus(r.body); return r; }),
    /** Put the cabinet away and stand on the floor again. */
    toFloor: () => unmountMachine(),
    /** The header's chip plate, so a machine can BANK into the real thing. */
    balanceEl: () => chips.el,
  };

  /**
   * A cabinet's module, imported the first time the floor is painted. A module
   * that has not shipped yet is not a bug and not a console error a player can
   * see: the frame becomes a DUST SHEET and stops being a button.
   */
  function probeCabinet(f) {
    return import('./' + f.cab.key + '.js').then((m) => {
      const mod = (m && (m.default || m[f.cab.key] || m.machine)) || null;
      if (!mod || typeof mod.mount !== 'function') throw new Error('no mount()');
      f.mod = mod;
    }).catch(() => {
      f.mod = null;
      dustSheet(f);
    });
  }

  function dustSheet(f) {
    f.btn.classList.add('bk-sheet');
    f.btn.disabled = true;
    f.btn.textContent = '';
    f.btn.appendChild(el('span', 'bk-cab-name', t(f.cab.nameKey)));
    f.btn.appendChild(el('span', 'bk-cab-sub', t('bk_sheet_sub')));
    f.btn.appendChild(el('span', 'bk-cab-stake', t('bk_sheet')));
  }

  function openCabinet(key) {
    const f = frames.get(key);
    if (dead || !f || !f.mod || mounted) return;
    sfx('door', 0.3);
    floor.hidden = true;
    stage.hidden = false;
    stage.textContent = '';
    let handle = null;
    try { handle = f.mod.mount(stage, kit, c) || null; }
    catch (e) {
      // A machine that throws on the way in leaves the player on the floor,
      // never on a blank stage.
      log('backroom: ' + key + ' failed to mount: ' + ((e && e.message) || e));
      stage.hidden = true;
      floor.hidden = false;
      dustSheet(f);
      return;
    }
    mounted = { key, handle, mod: f.mod };
  }

  function unmountMachine() {
    if (!mounted) return false;
    const m = mounted;
    mounted = null;
    try { if (m.handle && typeof m.handle.unmount === 'function') m.handle.unmount(); }
    catch (e) { log('backroom: ' + m.key + ' unmount threw'); }
    try { if (typeof m.mod.unmount === 'function') m.mod.unmount(); }
    catch (e) { /* a module-level unmount is optional */ }
    stage.textContent = '';
    stage.hidden = true;
    floor.hidden = false;
    sfx('slide', 0.26);
    return true;
  }

  /* ------------------------------------------------------------- the doors */
  function leave() {
    sfx('door', 0.32);
    try { if (typeof c.onExit === 'function') { c.onExit(); return; } } catch (e) { /* noop */ }
    close();
  }

  /** The shell's ladder asks; this binds no key of its own (exits.js law). */
  function escapeStep() {
    if (dead) return false;
    return unmountMachine();
  }

  function close() {
    if (dead) return;
    dead = true;
    unmountMachine();
    try { cage.destroy(); } catch (e) { /* noop */ }
    try { api.destroy(); } catch (e) { /* noop */ }
    try { root.remove(); } catch (e) { /* noop */ }
  }

  /* --------------------------------------------------------------- opening */
  for (const [, f] of frames) probeCabinet(f);
  api.status().then((r) => {
    if (dead) return;
    if (r.ok) takeStatus(r.body);
    else cage.paint(null);
  });

  return {
    root,
    close,
    destroy: close,
    escapeStep,
    /** Nothing here is a fixed stage, so a refit is a no-op. The shell calls it. */
    fit() { /* the floor is a flow layout: it fits itself */ },
  };
}

export default openBackRoom;
