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
 *
 * TWO WAYS TO HAND THE ROOM OVER, and the LINK is the primary one. Six
 * characters read down a voice call is fine; six characters thumbed into a
 * phone by somebody who has never seen this page is where invites die. The link
 * (ui/inviteLink.js) opens the standalone client straight into the join flow
 * with nothing to type. The plain-code line stays right beside it, because a URL
 * is eaten or mangled in more places than a six-character word is.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import { buildInviteUrl } from '../inviteLink.js';

const EXPIRY_MS = 5 * 60 * 1000;
const GOLD_UNDER_MS = 60 * 1000;

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { session, actions, audio, toasts, sheets } = ctx;
  let code = null;
  let expiresAt = 0;
  let cancelled = false;

  const eyebrow = el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.host.minting })]);
  const codeRow = el('div', { class: 'gg-code', role: 'group', 'aria-label': 'invite code' });
  const copyChip = el('span', { class: 'gg-code-chip', text: S.host.copied, hidden: true });
  const expiryBar = el('div', { class: 'gg-expiry' }, [el('i', { class: 'gg-expiry-fill' })]);
  const expiryText = el('p', { class: 'gg-expiry-text', text: '' });

  const linkBtn = button(ledger, S.host.copyLink, () => copyLink(), { variant: 'primary', audio, sfx: 'code-copy' });
  linkBtn.disabled = true;
  const copyBtn = button(ledger, S.host.copy, () => copyInvite(), { variant: '', audio, sfx: 'code-copy' });
  copyBtn.disabled = true;
  const linkNote = el('p', { class: 'gg-host-linknote', text: S.host.linkNote, hidden: true });

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
    el('div', { class: 'gg-host-actions' }, [linkBtn, copyBtn, cancelBtn]),
    linkNote,
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

  /**
   * THE LINK, built from where this page actually is.
   *
   * Standalone that is `location.origin + location.pathname`, so a play-test on
   * a LAN address links to the LAN address. HOSTED it is the public deployment
   * constant instead — the WebView2 page lives on the virtual host
   * `https://ccp.game/…`, which resolves on exactly one machine, and pasting
   * that into a chat window would hand somebody a link that cannot load.
   */
  function inviteUrl() {
    if (!code) return '';
    const loc = (typeof location !== 'undefined') ? location : null;
    return buildInviteUrl(code, {
      hosted: !!(session && session.hosted),
      origin: (loc && loc.origin) || '',
      pathname: (loc && loc.pathname) || '',
    });
  }

  /**
   * One clipboard path for both buttons. `chipText` is what the inline chip says,
   * because "Copied" over a code and "Link copied" over a URL are the difference
   * between a player pasting the right thing and pasting it twice.
   */
  async function copyText(text, chipText, toastText) {
    if (!text) return;
    let ok = false;
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(text);
        ok = true;
      }
    } catch (_e) { ok = false; }
    if (ledger.isDisposed) return;
    if (!ok) {
      // Clipboard is permission-gated in a plain browser; select the code so the
      // player can copy it by hand instead of being told "no".
      try {
        const range = document.createRange();
        range.selectNodeContents(codeRow);
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
      } catch (_e) { /* ignore */ }
      toasts?.warn?.(S.toasts.copyFailed);
      return;
    }
    copyChip.textContent = chipText;
    copyChip.hidden = false;
    ledger.timer(() => { copyChip.hidden = true; }, 1600);
    toasts?.good?.(toastText);
  }

  function copyInvite() {
    if (!code) return;
    void copyText(S.host.inviteLine(code), S.host.copied, S.toasts.copied);
  }

  function copyLink() {
    const url = inviteUrl();
    // No usable base (a `file:` page with no public constant to fall back on):
    // the code path is still whole, so hand them that rather than an empty
    // clipboard and a chip that lies.
    if (!url) { copyInvite(); return; }
    void copyText(S.host.inviteLinkLine(url), S.host.copiedLink, S.toasts.linkCopied);
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
      // A dead link is worse than no link: it opens the app and then says "no
      // room with that code", which reads as the game being broken.
      linkBtn.disabled = true;
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
    linkBtn.disabled = false;
    linkNote.hidden = false;
    waiting.hidden = false;
    tickExpiry();
    ledger.interval(tickExpiry, 500);
    try { audio?.sfx?.('title-unlock'); } catch (_e) { /* stub bus */ }
  })();

  return { unmount() { cancelled = true; ledger.dispose(); } };
}

export default { mount };
