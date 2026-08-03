/* ============================================================================
 * ui/options.js — the side drawer (#gg-drawer, .sf-panel geometry from the
 * Fall's fall-styles.css, re-namespaced to gg-*).
 *
 * TWO MODES, one drawer:
 *   out of a match — volumes, motion, fullscreen, reset;
 *   IN a match     — volumes, motion, fullscreen ONLY, plus the line
 *                    "Match settings are locked once you start."
 * The match terms are on the wire and countersigned; a drawer that appeared to
 * let you change them mid-duel would be lying, and the engine would ignore it.
 *
 * THE MERCY CLEARANCE IS NOT COSMETIC. #gg-drawer is z70 and #gg-mercy is z60,
 * so an unclipped drawer WOULD cover the mercy button. While the HUD is up the
 * drawer clips its own bottom 96px (--gg-drawer-clip) so the player's guaranteed
 * way out is never behind a settings panel. See the isolation banner in
 * goon.css — same contract, enforced from the other side.
 * ==========================================================================*/

import { createLedger, el, button } from './router.js';
import { S } from './strings.js';

/** Height reserved at the bottom of the drawer for the mercy button. */
const MERCY_CLEARANCE_PX = 96;

/**
 * @param {object} o
 * @param {object} o.prefs
 * @param {object} [o.audio]
 * @param {object} [o.session] boot's session (hosted / fullscreen state)
 * @param {(on:boolean)=>void} [o.setFullscreen] hosted only
 * @param {()=>boolean} [o.isInMatch]
 * @param {object} [o.logger]
 */
export function createOptions({ prefs, audio = null, session = null, setFullscreen = null, isInMatch = null, logger = null } = {}) {
  const doc = (typeof document !== 'undefined') ? document : null;
  const host = doc ? doc.getElementById('gg-drawer') : null;
  let ledger = null;
  let open = false;

  function close() {
    if (!open) return;
    open = false;
    const panel = host ? host.querySelector('.gg-panel') : null;
    const done = () => {
      try { ledger?.dispose(); } catch (_e) { /* ignore */ }
      ledger = null;
      if (host) { host.replaceChildren(); host.hidden = true; }
    };
    if (panel && !prefs?.get?.('reduceMotion')) {
      panel.classList.remove('is-open');
      setTimeout(done, 260);
    } else { done(); }
  }

  function row(label, node, extraClass) {
    return el('div', { class: 'gg-panel-row' + (extraClass ? ' ' + extraClass : '') }, [
      el('span', { class: 'gg-panel-label', text: label }),
      node,
    ]);
  }

  function volumeRow(key, label) {
    const value = el('span', { class: 'gg-panel-value', text: '' });
    const input = el('input', {
      type: 'range', min: '0', max: '100', step: '1',
      value: String(Math.round((prefs.get(key) || 0) * 100)),
      'aria-label': label,
    });
    const paint = () => { value.textContent = Math.round(Number(input.value)) + '%'; };
    paint();
    ledger.listen(input, 'input', () => {
      paint();
      prefs.set(key, Number(input.value) / 100);
      audio?.setVolume?.(key.replace('Volume', ''), Number(input.value) / 100);
    });
    ledger.listen(input, 'change', () => { try { audio?.sfx?.('ui-move'); } catch (_e) { /* stub */ } });
    return el('div', { class: 'gg-panel-row gg-panel-row--slider' }, [
      el('span', { class: 'gg-panel-label' }, [el('span', { text: label }), value]),
      input,
    ]);
  }

  function toggleRow(label, get, set) {
    const b = el('button', { type: 'button', class: 'gg-toggle', 'aria-pressed': String(!!get()) });
    b.appendChild(el('i'));
    const paint = () => b.setAttribute('aria-pressed', String(!!get()));
    ledger.listen(b, 'click', () => {
      set(!get());
      paint();
      try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
    });
    return { node: row(label, b), paint };
  }

  function build() {
    const inMatch = !!(isInMatch && isInMatch());

    const body = el('div', { class: 'gg-panel-body' }, [
      volumeRow('masterVolume', S.options.master),
      volumeRow('musicVolume', S.options.music),
      volumeRow('sfxVolume', S.options.sfx),
    ]);

    const motion = toggleRow(S.options.motion,
      () => prefs.get('reduceMotion'),
      (v) => {
        prefs.set('reduceMotion', v);
        try { doc.documentElement.setAttribute('data-gg-motion', v ? 'reduced' : 'full'); } catch (_e) { /* ignore */ }
      });
    body.appendChild(motion.node);

    if (session && session.hosted && setFullscreen) {
      const fs = toggleRow(S.options.fullscreen,
        () => !!session.fullscreen,
        (v) => setFullscreen(v));
      body.appendChild(fs.node);
      // The host echoes `fullscreen` back; repaint whenever the drawer is open.
      ledger.interval(fs.paint, 500);
    }

    if (inMatch) {
      body.appendChild(el('p', { class: 'gg-panel-note', text: S.options.lockedNote }));
    } else {
      body.appendChild(button(ledger, S.options.reset, () => {
        prefs.reset();
        close();
        setTimeout(() => { if (!open) openDrawer(); }, 30);
      }, { variant: 'ghost', audio, sfx: 'ui-back' }));
    }

    const closeBtn = el('button', { type: 'button', class: 'gg-panel-close', 'aria-label': S.options.close, text: '×' });
    ledger.listen(closeBtn, 'click', close);

    const panel = el('aside', { class: 'gg-panel', role: 'dialog', 'aria-label': S.options.headline }, [
      el('div', { class: 'gg-panel-head' }, [
        el('h2', { class: 'gg-grad', text: S.options.headline }),
        closeBtn,
      ]),
      body,
    ]);
    // The clearance the mercy button is entitled to (see the banner above).
    panel.style.setProperty('--gg-drawer-clip', inMatch ? MERCY_CLEARANCE_PX + 'px' : '0px');
    panel.classList.toggle('is-inmatch', inMatch);
    return panel;
  }

  function openDrawer() {
    if (!host || open) return;
    open = true;
    ledger = createLedger();
    ledger.logger = logger;

    const panel = build();
    host.replaceChildren(panel);
    host.hidden = false;
    // One frame so the slide-in transition has a start state to animate from.
    ledger.timer(() => panel.classList.add('is-open'), 16);
    ledger.timer(() => { try { panel.querySelector('input,button')?.focus(); } catch (_e) { /* ignore */ } }, 40);
    try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
  }

  return {
    open: openDrawer,
    close,
    toggle() { if (open) close(); else openDrawer(); },
    get isOpen() { return open; },
    dispose() { close(); },
  };
}

export default createOptions;
