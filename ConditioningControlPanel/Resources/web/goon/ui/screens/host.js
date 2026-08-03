/* ============================================================================
 * ui/screens/host.js — mint a room, show the code, wait for the opponent.
 *
 * The whole screen is one promise's lifetime: actions.hostStart() resolves with
 * a code or a machine-readable failure, and every failure has a SENTENCE (see
 * ui/sheets.js showSignalError) rather than a status number. "no_pass" in
 * particular is a product message, not an error — the player has not done
 * anything wrong, their free match is simply spent.
 *
 * The five-minute expiry is a CLIENT-SIDE countdown against the server's own
 * TTL. It is deliberately advisory: when it runs out we say so and offer a new
 * room instead of silently letting the player read a dead code aloud.
 *
 * boot's phase router leaves this screen mounted while the match sits in Lobby
 * with no remote hello — the swap to the lobby screen happens on Consent (or on
 * a hello landing), which is the first moment there is a second person to show.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';

const EXPIRY_MS = 5 * 60 * 1000;
const GOLD_UNDER_MS = 60 * 1000;

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { actions, audio, toasts, sheets } = ctx;
  let code = null;
  let expiresAt = 0;
  let cancelled = false;

  const eyebrow = el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.host.minting })]);
  const codeRow = el('div', { class: 'gg-code', role: 'group', 'aria-label': 'invite code' });
  const copyChip = el('span', { class: 'gg-code-chip', text: S.host.copied, hidden: true });
  const expiryBar = el('div', { class: 'gg-expiry' }, [el('i', { class: 'gg-expiry-fill' })]);
  const expiryText = el('p', { class: 'gg-expiry-text', text: '' });

  const copyBtn = button(ledger, S.host.copy, () => copyInvite(), { variant: '', audio, sfx: 'code-copy' });
  copyBtn.disabled = true;

  const waiting = el('div', { class: 'gg-waiting', hidden: true }, [
    el('span', { class: 'gg-dots gg-deco', 'aria-hidden': 'true' }, [el('i'), el('i'), el('i')]),
    el('span', { text: S.host.waiting }),
  ]);

  const cancelBtn = button(ledger, S.host.cancel, () => cancel(), { variant: 'ghost', audio, sfx: 'ui-back' });

  const card = el('div', { class: 'gg-card gg-host' }, [
    eyebrow,
    codeRow,
    copyChip,
    expiryBar,
    expiryText,
    el('div', { class: 'gg-host-actions' }, [copyBtn, cancelBtn]),
    waiting,
  ]);
  container.appendChild(card);

  /* ------------------------------------------------------------------ code */

  function renderCode(text) {
    codeRow.replaceChildren();
    const chars = String(text || '······').split('');
    for (const ch of chars) {
      codeRow.appendChild(el('span', { class: 'gg-code-cell gg-grad', text: ch }));
    }
    codeRow.classList.toggle('is-live', !!text);
  }
  renderCode(null);

  ledger.listen(codeRow, 'click', () => { if (code) copyInvite(); });

  async function copyInvite() {
    if (!code) return;
    const line = S.host.inviteLine(code);
    let ok = false;
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(line);
        ok = true;
      }
    } catch (_e) { ok = false; }
    if (!ok) {
      // Clipboard is permission-gated in a plain browser; select the code so the
      // player can copy it by hand instead of being told "no".
      try {
        const range = document.createRange();
        range.selectNodeContents(codeRow);
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
        ok = true;
      } catch (_e) { /* ignore */ }
      toasts?.warn?.(S.toasts.copyFailed);
      return;
    }
    copyChip.hidden = false;
    ledger.timer(() => { copyChip.hidden = true; }, 1600);
    toasts?.good?.(S.toasts.copied);
  }

  /* --------------------------------------------------------------- expiry */

  function tickExpiry() {
    if (!code) return;
    const left = expiresAt - Date.now();
    const frac = Math.max(0, Math.min(1, left / EXPIRY_MS));
    const fill = expiryBar.firstChild;
    if (fill) fill.style.width = (frac * 100).toFixed(1) + '%';
    expiryBar.classList.toggle('is-urgent', left < GOLD_UNDER_MS);
    if (left <= 0) {
      expiryText.textContent = S.host.expired;
      expiryBar.classList.add('is-dead');
      copyBtn.disabled = true;
      return;
    }
    expiryText.textContent = S.host.expiresIn(left);
  }

  /* ----------------------------------------------------------------- flow */

  function cancel() {
    cancelled = true;
    try { actions.cancelPending('host'); } catch (_e) { /* ignore */ }
    actions.goTitle();
  }

  (async () => {
    let res = null;
    try {
      res = await actions.hostStart();
    } catch (e) {
      res = { ok: false, error: { kind: 'connect_error', detail: (e && e.message) || '' } };
    }
    if (cancelled || ledger.isDisposed) return;

    if (!res || !res.ok) {
      const answer = await sheets?.showSignalError?.(res && res.error, { retryLabel: S.sheets.retry });
      if (ledger.isDisposed) return;
      if (answer === 'retry') { actions.goHost(); return; }
      actions.goTitle();
      return;
    }

    code = res.code;
    expiresAt = Date.now() + EXPIRY_MS;
    eyebrow.lastChild.textContent = S.host.open;
    renderCode(code);
    copyBtn.disabled = false;
    waiting.hidden = false;
    tickExpiry();
    ledger.interval(tickExpiry, 500);
    try { audio?.sfx?.('title-unlock'); } catch (_e) { /* stub bus */ }
  })();

  return { unmount() { cancelled = true; ledger.dispose(); } };
}

export default { mount };
