/* ============================================================================
 * ui/screens/host.js - your table: open the seat, wait for someone to sit down.
 *
 * REDESIGNED 2026-09-23 to the approved "open tables" mockup. The screen shows
 * four things and hides the rest: the empty seat, WHO CAN SEE IT (Friends /
 * Anyone / Off), Pick a song, and a CLOSED Customize. The code and the link sit
 * in the rail, with a preview of the row other people see.
 *
 * The whole screen is still one promise's lifetime: actions.hostStart() resolves
 * with a code or a machine-readable failure, and every failure has a SENTENCE
 * (ui/sheets.js showSignalError). `no_host_access` opens the Prime sheet: every
 * 1v1 is Prime now, hosting and joining, and practice stays free.
 *
 * THE LISTING. Once the code exists the table is listed at the chosen
 * visibility (Friends by default) and RENEWED every 60 s while nobody has sat
 * down; every switch change renews at once. Off unlists it; the code still
 * works. Nothing the host typed goes on the list: the code, a visibility and
 * four flags (song yes/no, game card length, pictures yes/no). The song TITLE
 * never leaves this page before somebody sits down. A listing that fails is a
 * quiet line, never a sheet: the code is the whole of the room and it still works.
 *
 * The five-minute expiry is a CLIENT-SIDE countdown against the server's TTL,
 * which slides while this page is visible (the host's poll re-arms it, and the
 * listing renew does too).
 *
 * boot's phase router leaves this screen mounted while the match sits in Lobby
 * with no remote hello; the swap to the lobby screen happens on Consent (or on a
 * hello landing), which is the first moment there is a second person to show.
 * Unmounting stops the renew, so a seated table is never re-listed.
 *
 * TWO WAYS TO HAND THE ROOM OVER, and the LINK is the primary one: the link
 * (ui/inviteLink.js) opens the standalone client straight into the join flow.
 * The plain-code copy stays right beside it.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import { buildInviteUrl } from '../inviteLink.js';
import { buildSongRow } from './songRow.js';
import { customizeSection, getDuelLength } from './customize.js';
import { finishedMatches } from '../nightProgress.js';
import { LIST_RENEW_MS, VISIBILITY, createTicker } from '../../net/openTables.js';
import { avatarNode, tagNodes } from './title.js';

const EXPIRY_MS = 5 * 60 * 1000;
const GOLD_UNDER_MS = 60 * 1000;

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { session, actions, audio, toasts, sheets, prefs } = ctx;
  let code = null;
  let expiresAt = 0;
  let cancelled = false;
  let visibility = VISIBILITY.Friends;
  let renew = null;

  /* --- the seat column ------------------------------------------------------ */
  const eyebrow = el('small', { class: 'gg-ot-eyebrow', text: S.table.eyebrow });
  const heading = el('h2', { class: 'gg-ot-h2', text: S.table.opening });
  const seeHint = el('div', { class: 'gg-ot-hint', text: S.table.hintFriends });
  const waiting = el('div', { class: 'gg-ot-waiting' }, [
    el('div', { class: 'gg-ot-seat', 'aria-hidden': 'true', text: '?' }),
    el('div', {}, [el('b', { text: S.table.seatEmpty }), seeHint]),
  ]);

  const segBtns = [
    [VISIBILITY.Friends, S.table.visFriends],
    [VISIBILITY.Anyone, S.table.visAnyone],
    [VISIBILITY.Off, S.table.visOff],
  ].map(([v, label]) => {
    const b = el('button', {
      type: 'button',
      class: 'gg-ot-seg-btn' + (v === VISIBILITY.Anyone ? ' is-any' : ''),
      'aria-pressed': v === visibility ? 'true' : 'false',
      dataset: { v },
      text: label,
    });
    ledger.listen(b, 'click', (e) => { e?.preventDefault?.(); setVisibility(v); });
    return b;
  });
  const whoSees = el('div', { class: 'gg-ot-who-sees' }, [
    el('div', { class: 'gg-ot-gh', text: S.table.whoSees }),
    el('div', { class: 'gg-ot-seg', role: 'group', 'aria-label': S.table.whoSees }, segBtns),
  ]);
  const listNote = el('p', { class: 'gg-ot-more', role: 'status', hidden: true, text: S.table.listFailed });

  // Song + Customize arrive once the room (and so the match) exists.
  const songSlot = el('div', { class: 'gg-ot-card gg-ot-song', hidden: true });
  const custSlot = el('div', { class: 'gg-ot-cust-slot', hidden: true });

  const closeBtn = button(ledger, S.table.close, () => cancel(), { variant: 'ghost', audio, sfx: 'ui-back' });
  closeBtn.classList.add('gg-ot-leave');

  const main = el('section', { class: 'gg-ot-col gg-ot-hostcol' }, [
    el('div', { class: 'gg-ot-head' }, [el('div', {}, [eyebrow, heading])]),
    waiting, whoSees, listNote, songSlot, custSlot, closeBtn,
  ]);

  /* --- the rail: share the code, and what they see ------------------------ */
  const codeRow = el('div', { class: 'gg-ot-bigcode-c', role: 'group', 'aria-label': S.aria.inviteCode, text: '------' });
  const copyChip = el('span', { class: 'gg-code-chip', text: S.host.copied, hidden: true });
  const expiryBar = el('div', { class: 'gg-expiry' }, [el('i', { class: 'gg-expiry-fill' })]);
  const expiryText = el('p', { class: 'gg-expiry-text', text: '' });

  const linkBtn = button(ledger, S.host.copyLink, () => copyLink(), { variant: 'primary', audio, sfx: 'code-copy' });
  linkBtn.disabled = true;
  const copyBtn = button(ledger, S.host.copy, () => copyInvite(), { variant: 'ghost', audio, sfx: 'code-copy' });
  copyBtn.disabled = true;

  const shareCard = el('div', { class: 'gg-ot-card' }, [
    el('h4', { text: S.table.share }),
    el('div', { class: 'gg-ot-bigcode' }, [codeRow, copyChip]),
    el('div', { class: 'gg-host-actions' }, [linkBtn, copyBtn]),
    expiryBar, expiryText,
    el('div', { class: 'gg-ot-small', text: S.table.shareNote }),
  ]);
  const previewBox = el('div', { class: 'gg-ot-preview' });
  const previewCard = el('div', { class: 'gg-ot-card' }, [el('h4', { text: S.table.preview }), previewBox]);
  const rail = el('aside', { class: 'gg-ot-rail' }, [shareCard, previewCard]);

  container.appendChild(el('div', { class: 'gg-ot gg-ot--host' }, [main, rail]));

  /* ------------------------------------------------------------ the listing */

  function listingNow() {
    const match = ctx.getMatch ? ctx.getMatch() : null;
    const cards = finishedMatches() >= 1;
    return {
      visibility,
      song: !!(match && match.song),
      cardSec: cards ? getDuelLength() : 0,
      pictures: !!(session && session.caps && session.caps.mediaTransfer === true),
    };
  }

  async function sendListing() {
    if (!code || cancelled || ledger.isDisposed || typeof actions.listTable !== 'function') return;
    let res = null;
    try { res = await actions.listTable(code, listingNow()); } catch (_e) { res = null; }
    if (ledger.isDisposed) return;
    const failed = !res || !res.ok;
    const kind = res && res.error && res.error.kind;
    // A listing that could not land is a quiet line, and only when it matters: a room
    // that filled or went away stops renewing, the rest keep trying on the clock.
    if (kind === 'not_lobby' || kind === 'no_room' || kind === 'not_host') { renew?.stop(); return; }
    listNote.hidden = !failed || visibility === VISIBILITY.Off || kind === 'not_deployed';
  }

  function setVisibility(v) {
    if (v === visibility) return;
    visibility = v;
    try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub */ }
    for (const b of segBtns) b.setAttribute('aria-pressed', b.dataset.v === v ? 'true' : 'false');
    seeHint.textContent = v === VISIBILITY.Anyone ? S.table.hintAnyone
      : (v === VISIBILITY.Off ? S.table.hintOff : S.table.hintFriends);
    paintPreview();
    if (renew) renew.bump();
  }

  function paintPreview() {
    if (visibility === VISIBILITY.Off) {
      previewBox.replaceChildren(el('div', { class: 'gg-ot-small', text: S.table.previewOff }));
      return;
    }
    const me = (session && session.identity && session.identity.displayName) || S.table.you;
    const l = listingNow();
    const row = el('div', { class: 'gg-ot-row is-friend is-preview' }, [
      avatarNode(me, '', { online: true }),
      el('div', { class: 'gg-ot-who' }, [
        el('b', { text: me }),
        el('div', { class: 'gg-ot-meta' }, tagNodes({ level: null, song: l.song, cardSec: l.cardSec, pictures: l.pictures, waitingSec: null }, { record: ' ' })
          .filter((n) => !(n.classList && n.classList.contains('is-rec')))),
      ]),
      el('span'),
    ]);
    previewBox.replaceChildren(row, el('div', { class: 'gg-ot-small', text: visibility === VISIBILITY.Anyone ? S.table.previewAnyone : S.table.previewFriends }));
  }

  /* ------------------------------------------------------------------ code */

  function inviteUrl() {
    if (!code) return '';
    const loc = (typeof location !== 'undefined') ? location : null;
    return buildInviteUrl(code, {
      hosted: !!(session && session.hosted),
      origin: (loc && loc.origin) || '',
      pathname: (loc && loc.pathname) || '',
    });
  }

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
      // Clipboard is permission-gated in a plain browser; select the code instead of saying "no".
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
    if (!url) { copyInvite(); return; }
    void copyText(S.host.inviteLinkLine(url), S.host.copiedLink, S.toasts.linkCopied);
  }
  ledger.listen(codeRow, 'click', () => { if (code) copyInvite(); });

  /* --------------------------------------------------------------- expiry */

  function tickExpiry() {
    if (!code) return;
    // THE TTL SLIDES while this page is visible (host poll + listing renew re-arm it).
    let vis = true;
    try { vis = typeof document === 'undefined' || document.visibilityState !== 'hidden'; } catch (_e) { /* assume visible */ }
    if (vis && expiresAt - Date.now() < EXPIRY_MS / 2) expiresAt = Date.now() + EXPIRY_MS;
    const left = expiresAt - Date.now();
    const frac = Math.max(0, Math.min(1, left / EXPIRY_MS));
    const fill = expiryBar.firstChild;
    if (fill) fill.style.width = (frac * 100).toFixed(1) + '%';
    expiryBar.classList.toggle('is-urgent', left < GOLD_UNDER_MS);
    if (left <= 0) {
      expiryText.textContent = S.host.expired;
      expiryBar.classList.add('is-dead');
      copyBtn.disabled = true;
      // A dead link is worse than no link: it opens the app and then says "no room".
      linkBtn.disabled = true;
      renew?.stop();
      return;
    }
    expiryText.textContent = S.host.expiresIn(left);
  }

  /* ----------------------------------------------------------------- flow */

  function cancel() {
    cancelled = true;
    renew?.stop();
    try { actions.cancelPending('host'); } catch (_e) { /* ignore */ }
    toasts?.show?.(S.table.closed);
    actions.goTitle();
  }

  paintPreview();

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
      // The Prime sheet's "Play practice instead" is already routing: do not land on top of it.
      if (answer === 'practice') return;
      actions.goTitle();
      return;
    }

    code = res.code;
    expiresAt = Date.now() + EXPIRY_MS;
    heading.textContent = S.table.title;
    codeRow.textContent = code;
    codeRow.classList.add('is-live');
    copyBtn.disabled = false;
    linkBtn.disabled = false;
    tickExpiry();
    ledger.interval(tickExpiry, 500);
    try { audio?.sfx?.('title-unlock'); } catch (_e) { /* stub bus */ }

    // Pick a song and the closed Customize, now that there is a match to hold them.
    const match = ctx.getMatch ? ctx.getMatch() : null;
    if (match && match.isHost) {
      try {
        const songRow = buildSongRow({
          ledger, match, audio, prefs,
          origin: (typeof location !== 'undefined' && location && location.origin) || null,
        });
        songSlot.replaceChildren(songRow.node);
        songSlot.hidden = false;
        if (typeof match.onSongChanged === 'function') {
          ledger.sub(match.onSongChanged(() => { paintPreview(); renew?.bump(); }));
        }
      } catch (e) { ledger._err?.('songRow', e); }
    }
    /* GAME NIGHT: Customize holds only the game card length, and game cards drop from a
     * player's second finished match; before that it would be a dial for nothing. */
    if (finishedMatches() >= 1) {
      const box = customizeSection({ isHost: true });
      if (box) {
        custSlot.replaceChildren(box);
        custSlot.hidden = false;
        ledger.listen(box, 'click', () => ledger.timer(() => { paintPreview(); renew?.bump(); }, 0));
      }
    }
    paintPreview();

    renew = createTicker({ run: sendListing, intervalMs: LIST_RENEW_MS });
    ledger.add(() => renew.stop());
    renew.start();
  })();

  return { unmount() { cancelled = true; renew?.stop(); ledger.dispose(); } };
}

export default { mount };
