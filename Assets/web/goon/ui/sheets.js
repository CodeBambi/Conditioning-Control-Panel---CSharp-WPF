/* ============================================================================
 * ui/sheets.js — the .gg-sheet modal: icon, headline, one line, 1–2 actions.
 *
 * This is where every failure that needs a DECISION lands: no pass, server not
 * deployed, rate limited, the network gone, an incompatible pairing. It is
 * deliberately the same component for all of them, because the difference the
 * player cares about is the SENTENCE, not the chrome.
 *
 * Contract notes that are not cosmetic:
 *   - the sheet mounts into #gg-modal (z70), which sits ABOVE the drawer and
 *     BELOW #gg-mercy — a sheet can never cover the mercy button;
 *   - focus moves to the primary action on open and returns to whatever had it
 *     on close, so keyboard players are not dumped at the top of the document;
 *   - Escape is NOT handled here. Escape belongs to boot's ladder (mercy first,
 *     always); boot closes the topmost overlay via `closeTop()` when — and only
 *     when — there is no match to mercy.
 * ==========================================================================*/

import { el, createLedger } from './router.js';
import { S } from './strings.js';

/**
 * @param {object} [o]
 * @param {HTMLElement} [o.root] defaults to #gg-modal
 * @param {object} [o.audio]
 * @param {object} [o.logger]
 */
export function createSheets({ root = null, audio = null, logger = null } = {}) {
  const doc = (typeof document !== 'undefined') ? document : null;
  const host = root || (doc ? doc.getElementById('gg-modal') : null);
  let openState = null;   // {ledger, resolve, restoreFocus}

  function close(value) {
    const st = openState;
    if (!st) return;
    openState = null;
    try { st.ledger.dispose(); } catch (_e) { /* ignore */ }
    if (host) { host.replaceChildren(); host.hidden = true; }
    try { st.restoreFocus?.focus?.(); } catch (_e) { /* the node may be gone */ }
    try { st.resolve(value); } catch (_e) { /* ignore */ }
  }

  /**
   * @param {object} o
   * @param {string} [o.icon] one glyph
   * @param {string} o.headline
   * @param {string} [o.line]
   * @param {Array<{id:string, label:string, variant?:string}>} [o.actions] 1–2; defaults to OK
   * @param {boolean} [o.dismissible] scrim click closes with null (default true)
   * @returns {Promise<string|null>} the chosen action id, or null
   */
  function open({ icon = '', headline = '', line = '', actions = null, dismissible = true } = {}) {
    if (!host) return Promise.resolve(null);
    close(null);

    const acts = (actions && actions.length) ? actions.slice(0, 2) : [{ id: 'ok', label: S.sheets.ok, variant: 'primary' }];
    const ledger = createLedger();
    ledger.logger = logger;

    let resolve = () => {};
    const promise = new Promise((r) => { resolve = r; });
    const restoreFocus = (doc && doc.activeElement && doc.activeElement.focus) ? doc.activeElement : null;
    openState = { ledger, resolve, restoreFocus };

    const scrim = el('div', { class: 'gg-scrim' });
    if (dismissible) ledger.listen(scrim, 'click', () => close(null));

    const buttons = acts.map((a, i) => {
      const b = el('button', {
        type: 'button',
        class: 'gg-btn' + (a.variant ? ' gg-btn--' + a.variant : (i === acts.length - 1 ? ' gg-btn--primary' : ' gg-btn--ghost')),
        text: a.label,
      });
      ledger.listen(b, 'click', () => {
        try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
        close(a.id);
      });
      return b;
    });

    const sheet = el('div', {
      class: 'gg-sheet gg-sheet--modal',
      role: 'alertdialog',
      'aria-modal': 'true',
      'aria-label': headline || 'notice',
    }, [
      icon ? el('div', { class: 'gg-sheet-icon', text: icon, 'aria-hidden': 'true' }) : null,
      el('h2', { class: 'gg-sheet-headline', text: headline }),
      line ? el('p', { class: 'gg-sheet-line', text: line }) : null,
      el('div', { class: 'gg-sheet-actions' }, buttons),
    ]);

    // Focus trap, minimal but real: Tab cycles the action row only.
    ledger.listen(sheet, 'keydown', (e) => {
      if (e.key !== 'Tab' || buttons.length < 2) return;
      e.preventDefault();
      const idx = buttons.indexOf(doc.activeElement);
      const next = e.shiftKey ? (idx <= 0 ? buttons.length - 1 : idx - 1) : ((idx + 1) % buttons.length);
      buttons[next].focus();
    });

    host.replaceChildren(scrim, el('div', { class: 'gg-sheet-dock' }, [sheet]));
    host.hidden = false;
    try { audio?.sfx?.('ui-error'); } catch (_e) { /* stub bus */ }
    ledger.timer(() => { try { buttons[buttons.length - 1].focus(); } catch (_e) { /* ignore */ } }, 0);

    return promise;
  }

  /**
   * Renders a signaling failure ({kind, detail, retryAfterSeconds} from
   * signaling.lastErrorInfo, or a bare reason string from session.onConnectFailed)
   * as the right sheet. Unknown kinds fall through to the network sheet, which is
   * the honest "we could not reach the other side" message.
   */
  function showSignalError(info, { retryLabel = null } = {}) {
    const kind = typeof info === 'string' ? info : (info && info.kind) || '';
    const detail = (info && info.detail) || '';
    const retryAfter = (info && info.retryAfterSeconds) || 0;
    const two = (label) => [
      { id: 'cancel', label: S.sheets.cancel, variant: 'ghost' },
      { id: 'retry', label: label, variant: 'primary' },
    ];

    switch (kind) {
      // The host gate, and the weekly pass it replaced. `no_pass` is retired server-side and is
      // kept here only so an OLD server in front of a new client still produces a product message
      // instead of falling through to "check your connection" about a server that answered fine.
      case 'no_host_access':
      case 'no_pass':
        return open({
          icon: S.sheets.noHostAccess.icon,
          headline: S.sheets.noHostAccess.headline,
          line: S.sheets.noHostAccess.line,
        });
      case 'not_deployed':
        return open({ icon: S.sheets.notDeployed.icon, headline: S.sheets.notDeployed.headline, line: S.sheets.notDeployed.line });
      case 'rate_limited':
        return open({
          icon: S.sheets.rateLimited.icon,
          headline: S.sheets.rateLimited.headline,
          line: S.sheets.rateLimited.line(retryAfter),
        });
      case 'unauthorized':
        return open({ icon: S.sheets.unauthorized.icon, headline: S.sheets.unauthorized.headline, line: S.sheets.unauthorized.line });
      // The join SCREEN renders these two inline; they only reach a sheet from a
      // path with no red line to put them on. Either way they must not fall through
      // to the network sheet — the room answered fine, it just said no.
      case 'already_joined':
        return open({ icon: S.sheets.seatTaken.icon, headline: S.sheets.seatTaken.headline, line: S.sheets.seatTaken.line });
      case 'self_join':
        return open({ icon: S.sheets.selfJoin.icon, headline: S.sheets.selfJoin.headline, line: S.sheets.selfJoin.line });
      case 'ice_timeout':
      case 'relay_failed':
      case 'signaling_failed':
      case 'connect_error':
        return open({
          icon: S.sheets.connectFailed.icon,
          headline: S.sheets.connectFailed.headline,
          line: S.sheets.connectFailed.line,
          actions: two(retryLabel || S.sheets.retry),
        });
      default:
        return open({
          icon: S.sheets.network.icon,
          headline: S.sheets.network.headline,
          line: S.sheets.network.line + (detail ? ' (' + detail + ')' : ''),
          actions: two(retryLabel || S.sheets.retry),
        });
    }
  }

  /**
   * Same chrome, arbitrary body — for content that is a LIST or a form rather
   * than a one-line notice ("how it works"). Single dismiss action.
   * @returns {Promise<string|null>}
   */
  function openNode(node, { label = '', closeLabel = null } = {}) {
    if (!host || !node) return Promise.resolve(null);
    const p = open({ headline: '', line: '', actions: [{ id: 'ok', label: closeLabel || S.sheets.ok, variant: 'primary' }] });
    // open() has just rebuilt the DOM; swap the (empty) headline block for the body.
    const sheet = host.querySelector('.gg-sheet');
    const headline = sheet ? sheet.querySelector('.gg-sheet-headline') : null;
    if (sheet && headline) sheet.replaceChild(node, headline);
    if (sheet && label) sheet.setAttribute('aria-label', label);
    return p;
  }

  return {
    open,
    openNode,
    showSignalError,
    close: (v) => close(v === undefined ? null : v),
    get isOpen() { return openState !== null; },
    dispose() { close(null); },
  };
}

export default createSheets;
