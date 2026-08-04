/* ============================================================================
 * ui/screens/mediaSetup.js — "bring something to endure".
 *
 * THE FIRST-RUN STEP FOR LINK JOINERS. Somebody tapped an invite link, has never
 * opened this thing before, and is four seconds from a lobby. A duel plays the
 * player's OWN library at them — every flash, clip, subliminal and bubble is
 * theirs — so a joiner with an empty deck gets a match in which every effect
 * fires against a blank screen. It does not read as "you have no media". It
 * reads as broken, and it is the single worst first impression this product can
 * make.
 *
 * SO IT IS INTERPOSED, NOT BOLTED ON:
 *   - the ENGINE is untouched. The match walks Lobby -> Consent underneath this
 *     screen exactly as it always has; boot.js simply holds this in front of the
 *     lobby until the player has something. Nothing is gated, nothing is paused,
 *     and Escape still means "leave" (boot's pre-live rung);
 *   - the opponent is TOLD (`media_prep`, protocol §6) so the host is not left
 *     reading "waiting for them" at an empty-looking room;
 *   - it is SKIPPED ENTIRELY for anyone who already has a deck — see
 *     `needsMediaSetup`, which boot.js calls once, on the join that landed.
 *
 * IT REUSES THE MEDIA PIPELINE WHOLESALE. `store.addLocalFiles` (ui/assetsStore.js)
 * is the same call ui/screens/assets.js's picker makes: the same allowlist, the
 * same zip expansion, the same compression lanes, the same in-memory adoption,
 * and boot's `onItems` -> syncLocalDeck folds the picks into exec/media.js. There
 * is deliberately NO second media path here — a parallel one would be a second
 * set of caps, a second set of failure messages and a second thing to keep in
 * step with the wire's artifact ceiling.
 *
 * THE TWENTY IS A SUGGESTION. The confirm unlocks on the FIRST file. Twenty
 * images plus a few clips is what makes a match feel like a match, and the copy
 * says so, but a hard gate on a stranger's first thirty seconds is how you lose
 * the player instead of the match.
 *
 * …AND ADDING MORE IS THE DEFAULT, not the fine print (owner play-test,
 * 2026-08-04). A phone's photo sheet is one-shot per visit: pick from one
 * album, confirm, back here. The first cut of this screen left "I'm set" as
 * the only primary action after that first batch, and people took it — into a
 * duel armed with six photos from one folder. So after anything lands the ADD
 * button is the primary one and says "add more", the lock wears the exact
 * count it would commit, and NOTHING advances this screen except that lock
 * (or Leave): not a batch finishing, not the tally clearing the suggestion.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import { DISCORD_INVITE_URL } from '../inviteLink.js';
import { formatBytes, LOCAL_ARTIFACT_MAX_BYTES, LOCAL_MAX_BYTES, LOCAL_ZIP_MAX_ENTRIES } from '../assetsStore.js';

/** What a good first library looks like. Advisory — never a gate. See header. */
export const SUGGESTED_ITEMS = 20;

/**
 * What the picker offers the OS file sheet. Verbatim from ui/screens/assets.js
 * (the wire's allowlist plus .zip, because a phone cannot hand over a folder) —
 * the two lists are the same list, and a divergence would mean the onboarding
 * step accepted a file the library screen would refuse.
 */
export const LOCAL_ACCEPT = '.png,.jpg,.jpeg,.gif,.webp,.mp4,.m4v,.webm,.mov,.zip,'
  + 'image/png,image/jpeg,image/gif,image/webp,video/mp4,video/webm,video/quicktime,'
  + 'application/zip,application/x-zip-compressed';

/**
 * Does this player need the step? PURE, and the only definition of "has media"
 * anywhere in this feature.
 *
 * It asks the DECK (exec/media.js), not the picker, and that is the point: the
 * deck is host manifest + local picks + nothing else, so a desktop player with a
 * preset behind them is "has media" without owning a single local file, and a
 * phone that has picked twenty images is too. A pool we cannot read at all
 * answers `false` — an onboarding screen shown by accident to somebody with a
 * full library is worse than one that is quietly skipped.
 *
 * @param {{hasMedia?: () => boolean}} pool exec/media.js's `media`
 */
export function needsMediaSetup(pool) {
  try {
    if (!pool || typeof pool.hasMedia !== 'function') return false;
    return !pool.hasMedia();
  } catch (_e) {
    return false;
  }
}

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { actions, audio, logger } = ctx || {};
  const store = ctx?.assets || null;
  const L = S.mediaSetup;
  const AL = S.assets.local;

  const maxText = formatBytes(LOCAL_MAX_BYTES);
  const artMaxText = formatBytes(LOCAL_ARTIFACT_MAX_BYTES);

  /* ------------------------------------------------------------- the copy */

  const tips = el('ul', { class: 'gg-how-list gg-mediasetup-tips' }, L.tips.map((t) => el('li', { text: t })));

  /* THE DISCORD LINE. A real anchor when we have a URL and plain text when we do
   * not — the sentence has to survive either way, because "no stash yet?" with
   * nowhere to go is the one line on this screen that would read as a taunt. */
  const discordLine = el('p', { class: 'gg-assets-note gg-mediasetup-discord' }, [
    el('span', { text: L.discordLine + ' ' }),
    DISCORD_INVITE_URL
      ? el('a', {
        class: 'gg-link',
        href: DISCORD_INVITE_URL,
        target: '_blank',
        rel: 'noopener noreferrer',
        text: L.discordCta,
      })
      : null,
  ]);

  /* ------------------------------------------------------------ the picker */

  const input = el('input', {
    type: 'file', class: 'gg-sr-only', multiple: true, accept: LOCAL_ACCEPT,
    'aria-hidden': 'true', tabindex: '-1',
  });
  const addBtn = button(ledger, L.add, () => { try { input.click(); } catch (_e) { /* ignore */ } },
    { variant: 'primary', audio });

  /* TWO status lines, and they are not the same line (the assets card learned
   * this the hard way): `progress` is the wait, `status` is the answer. Folded
   * together, the last run's summary is what a player stares at while the next
   * zip is still decoding. */
  const progress = el('p', { class: 'gg-assets-eta gg-local-progress', text: '', role: 'status', hidden: true });
  const status = el('p', { class: 'gg-assets-eta', text: '', role: 'status', hidden: true });

  const tally = el('p', { class: 'gg-mediasetup-tally', text: L.countNone, role: 'status' });
  const tallyNote = el('p', { class: 'gg-assets-note', text: L.suggest(SUGGESTED_ITEMS) });
  const list = el('div', { class: 'gg-local-list', role: 'list' });

  const lockBtn = button(ledger, L.lock, () => lockIn(), { variant: 'primary', audio, sfx: 'lamp-confirm' });
  lockBtn.disabled = true;
  lockBtn.title = L.lockNeed;
  /** A batch mid-decode: the lock waits for it. Painted by the progress sub. */
  let adding = false;
  const leaveBtn = button(ledger, L.leave, () => { void actions?.leave?.('media-setup'); },
    { variant: 'ghost', audio, sfx: 'ui-back' });

  container.appendChild(el('div', { class: 'gg-card gg-assets gg-mediasetup' }, [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: L.eyebrow })]),
    el('h2', { class: 'gg-assets-standalone-head', text: L.headline }),
    el('p', { class: 'gg-lead', text: L.lead }),
    tips,
    discordLine,
    el('div', { class: 'gg-assets-tools-row gg-local-tools' }, [addBtn, input]),
    el('p', { class: 'gg-assets-note', text: AL.limits(maxText, artMaxText) }),
    progress,
    status,
    tally,
    tallyNote,
    list,
    el('p', { class: 'gg-assets-note', text: L.note }),
    el('p', { class: 'gg-assets-note gg-mediasetup-waiting', text: L.waiting }),
    el('div', { class: 'gg-host-actions' }, [leaveBtn, lockBtn]),
  ]));

  /* --------------------------------------------------------------- state */

  /** The player's own picks. `local:` is assetsStore's id namespace for them. */
  function localItems() {
    if (!store) return [];
    try {
      if (Array.isArray(store.localItems)) return store.localItems;
      return (store.items || []).filter((it) => it && String(it.id).indexOf('local:') === 0);
    } catch (_e) { return []; }
  }

  function row(it) {
    const thumb = el('div', { class: 'gg-local-thumb' });
    // A COMPRESSED pick has no srcUrl — its bytes ARE the artifact, behind
    // artUrl. (Reading only srcUrl is how a compressed photo renders as the word
    // "image" next to a perfectly good thumbnail; the assets card paid for this.)
    const previewUrl = it.srcUrl || it.artUrl || '';
    if (previewUrl && it.kind === 'image') {
      thumb.appendChild(el('img', { src: previewUrl, alt: '', decoding: 'async', loading: 'lazy' }));
    } else {
      thumb.appendChild(el('span', { class: 'gg-local-thumb-kind', text: it.kind === 'video' ? 'video' : 'image' }));
    }
    const name = el('span', { class: 'gg-local-name' });
    name.textContent = String(it.name || '');           // user data — text, always
    name.title = String(it.name || '');
    const shrunk = it.srcBytes > it.bytes;
    const size = el('span', {
      class: 'gg-local-size',
      text: shrunk ? AL.sizeShrunk(formatBytes(it.srcBytes), formatBytes(it.bytes)) : formatBytes(it.bytes),
    });
    const rm = el('button', { type: 'button', class: 'gg-asset-act', text: L.remove });
    ledger.listen(rm, 'click', () => {
      try { audio?.sfx?.('ui-back'); } catch (_e) { /* stub bus */ }
      try { store?.removeLocal?.(it.id); } catch (_e) { /* ignore */ }
    });
    return el('div', { class: 'gg-local-row', role: 'listitem' }, [thumb, name, size, rm]);
  }

  function paint() {
    const items = localItems();
    const n = items.length;

    list.replaceChildren();
    for (const it of items) list.appendChild(row(it));

    tally.textContent = n ? L.count(n) : L.countNone;
    tallyNote.textContent = n >= SUGGESTED_ITEMS ? L.enough : L.suggest(SUGGESTED_ITEMS);

    /* THE ROLES SWAP once something is in (see header): adding more becomes the
     * primary move and says so, locking becomes the explicit commitment wearing
     * its count. Classes, not new buttons — the ledger already owns these. */
    addBtn.textContent = n ? L.addMore : L.add;
    lockBtn.classList.toggle('gg-btn--primary', n > 0);
    lockBtn.textContent = n ? L.lockN(n) : L.lock;

    // ONE item unlocks it. The suggestion above is a suggestion (see header) —
    // but never mid-batch: locking in "6 picks" while a zip is still unpacking
    // its other two hundred is a commitment nobody meant to make.
    lockBtn.disabled = n < 1 || adding;
    lockBtn.title = adding ? L.lockBusy : (n < 1 ? L.lockNeed : '');
  }

  if (store && typeof store.onLocalProgress === 'function') {
    ledger.sub(store.onLocalProgress((p) => {
      const wasAdding = adding;
      adding = !!(p && p.running);
      if (adding !== wasAdding) paint();
      if (!p || !p.running) { progress.hidden = true; progress.textContent = ''; return; }
      progress.hidden = false;
      if (p.pct > 0 && p.name) progress.textContent = AL.compressing(p.name, p.pct);
      else if (p.total > 1) progress.textContent = AL.adding(Math.min(p.done + 1, p.total), p.total);
      else progress.textContent = p.name ? AL.addingOne(p.name) : AL.adding(p.done, Math.max(1, p.total));
    }));
  }

  ledger.listen(input, 'change', () => {
    const files = input.files;
    if (!files || !files.length) return;
    if (!store || typeof store.addLocalFiles !== 'function') return;
    addBtn.disabled = true;
    status.hidden = true;
    store.addLocalFiles(files).then((r) => {
      if (ledger.isDisposed) return;
      addBtn.disabled = false;
      try { input.value = ''; } catch (_e) { /* ignore */ }
      // The SAME summary the library screen builds, clause for clause — a
      // second wording of "3 over 8 MB" is a second thing to get wrong.
      const bits = [];
      if (r.added) bits.push(AL.added(r.added));
      if (r.compressed) bits.push(AL.compressed(r.compressed));
      if (r.dupes) bits.push(AL.skipDupe(r.dupes));
      if (r.tooBig) bits.push(AL.skipBig(r.tooBig, maxText));
      if (r.tooBigVideo) bits.push(AL.skipBigVideo(r.tooBigVideo, artMaxText));
      if (r.badType) bits.push(AL.skipType(r.badType));
      if (r.badCodec) bits.push(AL.skipCodec(r.badCodec));
      if (r.failed) bits.push(AL.skipFailed(r.failed));
      if (r.trimmed) bits.push(AL.trimmed(r.trimmed, LOCAL_ZIP_MAX_ENTRIES));
      if (r.zipBad) bits.push(AL.zipBad(r.zipBad));
      if (!bits.length && r.zips) bits.push(AL.zipNone);
      status.textContent = bits.join(' · ');
      status.hidden = bits.length === 0;
      if (r.added) { try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ } }
    }).catch((e) => {
      if (ledger.isDisposed) return;
      addBtn.disabled = false;
      logger?.warn?.('[GG media-setup] add threw: ' + ((e && e.message) || e));
    });
  });

  if (store && typeof store.onItems === 'function') ledger.sub(store.onItems(() => paint()));
  paint();

  /* ----------------------------------------------------------------- flow */

  function lockIn() {
    if (lockBtn.disabled) return;
    // boot.js owns the wire half (`media_prep` done) and the re-route back onto
    // whatever phase the match has reached while this was up. The screen only
    // says "I am finished" — it has no business deciding what comes next.
    try { actions?.mediaPrepDone?.(); } catch (e) { ledger._err('lock in', e); }
  }

  return { unmount() { ledger.dispose(); } };
}

export default { mount, needsMediaSetup, SUGGESTED_ITEMS };
