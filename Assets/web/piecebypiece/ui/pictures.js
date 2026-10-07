/* ============================================================================
 * ui/pictures.js - where Distraction's pictures come from (2026-09-28), and the
 * one-time ask at the first start (2026-09-30).
 *
 * Owner, 2026-09-30: "no images were selected because we just let them start a
 * game. Record this choice and don't make them pick again." So a start (solo,
 * two here, quick match, a table, a join, a friend's challenge) with Distraction
 * on and NO saved choice opens the picker first; the pick is sent to the host,
 * which saves it, and the game starts. The picker never opens on its own again.
 * The same choice is edited any time from the niche manager: Options > Pictures,
 * the Pictures link on the menu, and the Pictures button on the pause card.
 *
 * The widget is shared (../../backroom/shared/niches/picker.js, one look for
 * every game), and so is the state logic (choice.js). This file only wires them
 * to the chess host:
 *   host -> page  pbp:media-state  { flavour, last, custom, online, appWide, chosen, library, canOnline }
 *   host -> page  pbp:online-media { state, have, want, ... }   (status line only)
 *   page -> host  pbp:media-flavour { flavour, custom, subs, online, chosen }
 * The HOST owns persistence (desktop: PieceByPieceHostService.Media.cs, the site:
 * the web shim's localStorage). A host that never sends pbp:media-state (a plain
 * browser, the phone today) gets no picker and no ask: nothing here touches storage.
 * ==========================================================================*/

import { readChoice, needsChoice, choiceFrame, sameChoice, statusLine } from '../../backroom/shared/niches/choice.js';
import { createNichePicker } from '../../backroom/shared/niches/picker.js';
import { onHostMessage, postToHost } from '../bridge.js';
import { presentation } from '../game/preferences.js';

export { statusLine };

/** The frame a choice sends, typed for the chess host. Pure. */
export function picturesFrame(state) {
  return { ...choiceFrame(state), type: 'pbp:media-flavour' };
}

/* ---- the one state, for every surface ----------------------------------- */

let state = null;       // null until the host speaks
let online = null;      // the last pbp:online-media, for the status line
const watchers = new Set();
let listening = false;

function tell(what) {
  for (const fn of [...watchers]) { try { fn(what); } catch (e) { console.warn('[pbp] pictures watcher', e); } }
}

function listen() {
  if (listening) return;
  listening = true;
  onHostMessage((m) => {
    if (m.type === 'pbp:media-state') {
      const next = readChoice(m);
      if (!next) return;
      state = next;
      tell('state');
    } else if (m.type === 'pbp:online-media') {
      online = { state: m.state, have: Number(m.have) || 0, want: Number(m.want) || 0 };
      tell('online');
    }
  });
}

/** Send a choice to the host and hold it here at once (the host echoes it back). */
function commit(next) {
  if (!next) return;
  state = { ...next, chosen: true };
  postToHost(picturesFrame(state));
  tell('state');
}

const status = () => statusLine(state, online);
const experience = () => { try { return presentation().experience; } catch { return 'distraction'; } };

/* ---- the modal: first-start card and the manager from the menu or pause ---- */

let modal = null;       // { root, done(ok) }

function openModal(mode, { still = false } = {}) {
  if (typeof document === 'undefined' || !state) return Promise.resolve(mode !== 'first');
  if (modal) modal.done(false);
  return new Promise((resolve) => {
    const root = document.createElement('div');
    root.className = 'pbp-np-modal' + (mode === 'first' ? ' is-first' : '');
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.setAttribute('aria-label', mode === 'first' ? 'Pick your pictures' : 'Pictures');
    const card = document.createElement('div');
    card.className = 'pbp-np-card';
    const start = state;
    let settled = false;
    const picker = createNichePicker({
      mode, state, still,
      onChoose: (s) => { commit(s); finish(true); },
    });
    if (mode === 'manage') {
      const h = document.createElement('h2');
      h.className = 'pbp-np-title';
      h.textContent = 'Pictures';
      const sub = document.createElement('p');
      sub.className = 'pbp-np-sub';
      sub.textContent = 'What Distraction mixes in. Saved when you close this.';
      const done = document.createElement('button');
      done.type = 'button';
      done.className = 'pbp-np-done';
      done.textContent = 'Done';
      done.addEventListener('click', () => finish(true));
      card.append(h, sub, picker.el, done);
      picker.status(status());
    } else {
      card.append(picker.el);
    }
    root.append(card);
    document.body.append(root);

    const watch = (what) => { if (what === 'online') picker.status(status()); };
    watchers.add(watch);
    // Escape leaves the card and nothing else: the menu, the pause card and the board
    // never see it (capture, and the door checks isOpen before it acts). The manager
    // keeps its edits on the way out; the first-start card starts nothing.
    const onKey = (e) => {
      if (e.key !== 'Escape') return;
      e.stopImmediatePropagation();
      e.preventDefault();
      if (!e.repeat) finish(mode === 'manage');
    };
    window.addEventListener('keydown', onKey, { capture: true });

    function finish(ok) {
      if (settled) return;
      settled = true;
      if (mode === 'manage' && ok) {
        const end = picker.value();
        if (end && (!sameChoice(end, start) || (!start.chosen && end !== start))) commit(end);
      }
      watchers.delete(watch);
      window.removeEventListener('keydown', onKey, { capture: true });
      picker.dispose();
      root.remove();
      if (modal && modal.root === root) modal = null;
      resolve(!!ok);
    }
    modal = { root, done: finish };
    setTimeout(() => { if (!settled) picker.focus(); }, 30);
  });
}

/** The page-wide handle: the door gates its starts on it, the menu and pause open the manager. */
export const pictureChoice = {
  /** A host has spoken, so there is something to choose. */
  available: () => !!state,
  state: () => state,
  /** Would a start ask first right now. */
  needsChoice: () => needsChoice(state, experience()),
  /** Ask if nothing is saved. Resolves true to go on (chosen, or nothing to ask), false when the player backed out. */
  ask(opts) { return needsChoice(state, experience()) ? openModal('first', opts) : Promise.resolve(true); },
  /** The niche manager as a card over whatever is up. */
  manage(opts) { return openModal('manage', opts); },
  isOpen: () => !!modal,
  /** For the harness. */
  debug: { status, close(ok) { if (modal) modal.done(ok); } },
};
listen();

/* ---- Options > Pictures: the same manager, inline ------------------------ */

export function attachPictures(root) {
  if (!root || typeof document === 'undefined') return { setVisible() {}, dispose() {} };
  root.textContent = '';
  const head = document.createElement('strong');
  head.textContent = 'Pictures';
  const hint = document.createElement('small');
  hint.textContent = 'Flavours mix in pictures from Scrolller\'s Reddit communities. Saved when Options closes.';
  const picker = createNichePicker({ mode: 'manage', state });
  root.append(head, picker.el, hint);
  let base = state;          // the state the edits started from

  const paint = () => { root.hidden = !state || root.dataset.off === '1'; };
  const watch = (what) => {
    if (what === 'state') { base = state; picker.set(state); }
    picker.status(status());
    paint();
  };
  watchers.add(watch);
  picker.set(state);
  picker.status(status());
  paint();

  // the options panel closing commits the edits, once, and only when something moved
  const flush = () => {
    const end = picker.value();
    if (!end || !base) return;
    if (!sameChoice(end, base) || (!base.chosen && end !== base)) commit(end);
  };
  const panel = root.closest('.game-options-panel');
  let watcher = null;
  if (panel && typeof MutationObserver === 'function') {
    watcher = new MutationObserver(() => { if (panel.hidden) flush(); });
    watcher.observe(panel, { attributes: true, attributeFilter: ['hidden'] });
  }

  return {
    /** Distraction only: Classic has no pictures to choose. */
    setVisible(on) { root.dataset.off = on ? '0' : '1'; paint(); },
    flush,
    dispose() {
      watchers.delete(watch);
      if (watcher) watcher.disconnect();
      picker.dispose();
    },
  };
}
