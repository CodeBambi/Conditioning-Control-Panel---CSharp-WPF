/* ============================================================================
 * ui/screens/draft.js — the agreement. WHAT ARE YOU BOTH WILLING TO TAKE?
 *
 * Redesigned 2026-08-03. This is no longer a pick-three loadout screen:
 *   - every element starts ON; a tile toggles whether YOU allow it
 *   - the effective pool is the INTERSECTION of both allowed sets, so switching
 *     something off removes it for BOTH of you
 *   - moving any toggle (yours or theirs) clears BOTH signatures, exactly like
 *     the consent sheet — nobody gets advanced onto terms they never saw
 *   - the match then ROLLS one schedule from that pool and the shared seed, and
 *     both players run it: same elements, same instants, perfectly even
 *
 * Tile states, all load-bearing:
 *   is-on          — you allow it
 *   is-off         — you switched it off (it is out of the pool)
 *   is-theirs-off  — they switched it off; out of the pool whatever you do
 *   is-always-on   — bubbles: never a toggle, runs the whole match for both
 *   is-unsupported — outside match.availableDraftPool, i.e. the CAPS
 *                    INTERSECTION. Their client cannot mirror it.
 *
 * The engine, not this screen, advances the phase: both signatures on one pair
 * of sets -> host proposes the countdown. The glow sweep is the acknowledgement,
 * not the trigger.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S, ELEMENTS } from '../strings.js';
// matchRiskTier/riskMultiplier are the ENGINE's frozen names for "how heavy is this pool" and
// "what does that multiply the score by" (core/scoring.js, C#-parity). The screen uses only the
// product of the two — one honest "you both score ×N" — and shows the tier itself nowhere.
import { ALWAYS_ON_ELEMENT, matchRiskTier, riskMultiplier, sharedPool } from '../../core/draft.js';

/** One 24x24 glyph per element. Literal markup — no interpolation, ever. */
const GLYPHS = {
  0: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M13 2 4 14h6l-1 8 9-12h-6z"/></svg>',                                   // flashes
  1: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="2.5" y="5" width="19" height="14" rx="3"/><path d="M10 9.2v5.6l5-2.8z" class="cut"/></svg>', // videos
  2: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="7" width="18" height="2.4" rx="1.2"/><rect x="3" y="11.4" width="12" height="2.4" rx="1.2"/><rect x="3" y="15.8" width="16" height="2.4" rx="1.2"/></svg>', // subliminals
  3: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="9" cy="10" r="5"/><circle cx="16.5" cy="16" r="3.4"/><circle cx="17" cy="7" r="2.2"/></svg>', // bubbles
  4: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4.5" y="10" width="15" height="10" rx="2.4"/><path d="M8 10V7.5a4 4 0 0 1 8 0V10" fill="none" stroke-width="2"/></svg>', // lock cards
  5: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3c2.8 0 5 2.2 5 5v8a5 5 0 0 1-10 0V8c0-2.8 2.2-5 5-5z"/><rect x="10.6" y="6" width="2.8" height="6" rx="1.4" class="cut"/></svg>', // toy patterns
  6: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3a6 6 0 0 1 6 6v2.5a5.5 5.5 0 0 1-5.5 5.5H12v4l-3-2.4V17H8a5 5 0 0 1-5-5V9a6 6 0 0 1 6-6z"/></svg>', // brain drain
  7: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 15c3-7 6-7 9 0s6 7 9 0" fill="none" stroke-width="2.4"/><circle cx="3" cy="15" r="1.8"/><circle cx="21" cy="15" r="1.8"/></svg>', // bouncing text
  8: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.6a9.4 9.4 0 1 0 9.4 9.4 7.6 7.6 0 0 0-7.6-7.6 6 6 0 0 0-6 6 4.6 4.6 0 0 0 4.6 4.6 3.4 3.4 0 0 0 3.4-3.4 2 2 0 0 0-2-2" fill="none" stroke-width="2"/></svg>', // spiral
};

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { audio, toasts, getMatch } = ctx;
  const match = getMatch();
  if (!match) {
    container.appendChild(el('div', { class: 'gg-card', text: '…' }));
    return { unmount() { ledger.dispose(); } };
  }

  /** Inline refusal under the confirm strip. Cleared by any successful move. */
  let inlineError = '';
  /** Sticky note once a toggle has invalidated the signatures. */
  let sawChange = false;

  /* --------------------------------------------------------- duel header */

  /* NO RISK METERS (2026-08-04). Each side used to carry a seven-segment bar of
   * the pool's summed "risk tier", 0-7, plus a third copy in the footer. Three
   * meters, one number, and no player could say what a 5 bought them — so all
   * three are gone and the header is just who is signing. The tier itself lives
   * on inside core/scoring.js, where it belongs. */
  const youSig = el('span', { class: 'gg-draft-sig', text: '' });
  const themSig = el('span', { class: 'gg-draft-sig', text: '' });

  const header = el('div', { class: 'gg-draft-head' }, [
    el('div', { class: 'gg-draft-side' }, [
      el('span', { class: 'gg-draft-who', text: match.localDisplayName || S.lobby.you }),
      youSig,
    ]),
    el('span', { class: 'gg-duel-vs', text: 'vs', 'aria-hidden': 'true' }),
    el('div', { class: 'gg-draft-side gg-draft-side--them' }, [
      el('span', { class: 'gg-draft-who', text: match.opponent.displayName || S.lobby.them }),
      themSig,
    ]),
  ]);

  /* ----------------------------------------------------------- the grid */

  const tiles = new Map();   // element -> {node, badge}
  const grid = el('div', { class: 'gg-elems', role: 'group', 'aria-label': 'effects both of you allow' });

  for (const meta of ELEMENTS) {
    const alwaysOn = meta.id === ALWAYS_ON_ELEMENT;

    const badge = el('span', { class: 'gg-elem-badge', hidden: true });
    const node = el('button', {
      type: 'button',
      class: 'gg-elem' + (alwaysOn ? ' is-always-on' : ''),
      dataset: { element: String(meta.id) },
      'aria-pressed': alwaysOn ? 'true' : 'false',
    }, [
      badge,
      el('span', { class: 'gg-elem-glyph', html: GLYPHS[meta.id] || '' }),
      el('span', { class: 'gg-elem-name', text: meta.name }),
      // no risk pips: the blurb is the whole story a tile tells now
      el('span', { class: 'gg-elem-blurb', text: meta.blurb }),
      el('span', { class: 'gg-elem-why', text: S.draft.unsupported }),
    ]);

    if (alwaysOn) {
      // Not a toggle at all: it is the baseline both players run start to finish.
      node.disabled = true;
      node.setAttribute('aria-disabled', 'true');
      node.title = S.draft.alwaysOnWhy;
      badge.hidden = false;
      badge.textContent = S.draft.alwaysOn;
    } else {
      ledger.listen(node, 'click', () => toggle(meta.id));
      ledger.listen(node, 'pointerenter', () => { try { audio?.sfx?.('ui-move'); } catch (_e) { /* stub */ } });
    }

    tiles.set(meta.id, { node, badge, alwaysOn });
    grid.appendChild(node);
  }

  /* ---------------------------------------------------------- the footer */

  const multLabel = el('span', { class: 'gg-draft-mult', text: '' });
  const poolLabel = el('span', { class: 'gg-draft-pool', text: '' });
  const errLabel = el('p', { class: 'gg-draft-error', text: '', role: 'status', hidden: true });
  const confirmBtn = button(ledger, S.draft.confirm, () => confirmIn(), { variant: 'primary', audio, sfx: 'draft-lock' });

  const footer = el('div', { class: 'gg-draft-foot' }, [
    el('div', { class: 'gg-draft-meterwrap' }, [multLabel]),
    el('div', { class: 'gg-draft-poolwrap' }, [
      poolLabel,
      el('span', { class: 'gg-draft-rolled', text: S.draft.rolled }),
    ]),
    confirmBtn,
  ]);

  const card = el('div', { class: 'gg-card gg-draft' }, [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.draft.eyebrow })]),
    el('p', { class: 'gg-lead', text: S.draft.lead }),
    header, grid, footer, errLabel,
  ]);
  container.appendChild(card);

  /* -------------------------------------------------------------- logic */

  function isAvailable(id) { return match.availableDraftPool.includes(id); }
  function myAllowed() { return match.localAllowedElements || []; }
  function theirAllowed() { return match.remoteAllowedElements || []; }
  function pool() { return sharedPool(myAllowed(), theirAllowed()); }

  function showError(msg) {
    inlineError = msg || '';
    try { if (inlineError) audio?.sfx?.('ui-error'); } catch (_e) { /* stub bus */ }
  }

  function toggle(id) {
    if (id === ALWAYS_ON_ELEMENT) return;
    if (!isAvailable(id)) {
      toasts?.warn?.(S.draft.unsupported);
      try { audio?.sfx?.('ui-error'); } catch (_e) { /* stub bus */ }
      return;
    }

    const res = match.toggleAllowedElement(id);
    if (!res.ok) {
      // The engine refuses a move that would leave you under the floor. Say so inline; the
      // toggle state on screen is whatever the engine still holds.
      showError(res.error === '' ? S.draft.tooFewYours(match.minAllowedElements) : res.error);
      paint();
      return;
    }

    showError('');
    sawChange = true;
    try { audio?.sfx?.(myAllowed().includes(id) ? 'draft-pick' : 'draft-drop'); } catch (_e) { /* stub bus */ }
    paint();
  }

  function confirmIn() {
    if (match.localDraftConfirmed) {
      match.withdrawDraft();
      showError('');
      paint();
      return;
    }
    const res = match.confirmDraft();
    if (!res.ok) {
      const shared = pool().length;
      showError(shared < match.minAllowedElements ? S.draft.tooFewShared(shared) : res.error);
      paint();
      return;
    }
    showError('');
    sawChange = false;
    paint();
  }

  function paint() {
    const mine = myAllowed();
    const theirs = theirAllowed();
    const shared = pool();

    for (const meta of ELEMENTS) {
      const t = tiles.get(meta.id);
      if (t.alwaysOn) continue;                       // locked tile: nothing to repaint

      const avail = isAvailable(meta.id);
      const on = mine.includes(meta.id);
      const theirsOff = avail && !theirs.includes(meta.id);

      t.node.classList.toggle('is-on', avail && on);
      t.node.classList.toggle('is-off', avail && !on);
      t.node.classList.toggle('is-theirs-off', theirsOff);
      t.node.classList.toggle('is-unsupported', !avail);
      t.node.disabled = !avail;
      t.node.setAttribute('aria-pressed', on ? 'true' : 'false');
      t.badge.hidden = !theirsOff;
      t.badge.textContent = theirsOff ? S.draft.theirsOff : '';
    }

    // Both players endure the same pool, so there is ONE number here and it is the one a player
    // can act on: agree to more, score faster. (The 0-7 "match risk" tier this used to paint as
    // three seven-segment meters is gone — it was engine bookkeeping on a player's screen.)
    multLabel.textContent = S.draft.score(riskMultiplier(matchRiskTier(shared)));
    poolLabel.textContent = S.draft.pool(shared.length);

    youSig.textContent = match.localDraftConfirmed ? S.draft.confirmed : '';
    themSig.textContent = match.remoteDraftConfirmed ? S.draft.theirSignature : S.draft.theirWait;

    confirmBtn.textContent = match.localDraftConfirmed ? S.draft.confirmed : S.draft.confirm;
    confirmBtn.classList.toggle('is-waiting', match.localDraftConfirmed);
    confirmBtn.disabled = false;   // always live: it is also the way back out of a signature

    const note = inlineError || (sawChange && !match.localDraftConfirmed ? S.draft.changed : '');
    errLabel.textContent = note;
    errLabel.hidden = note === '';
    errLabel.classList.toggle('is-bad', !!inlineError);

    // Both signed: a single sweep, then the engine takes it to Countdown.
    const both = match.draftResolved;
    if (both && !card.classList.contains('is-sealed')) {
      card.classList.add('is-sealed');
      try { audio?.sfx?.('draft-lock'); } catch (_e) { /* stub bus */ }
    } else if (!both) {
      card.classList.remove('is-sealed');
    }
  }

  ledger.sub(match.onDraftChanged(() => {
    // The engine is authoritative on what it accepted (a toggle of theirs can have cleared our
    // signature since the last paint).
    if (!match.localDraftConfirmed && !match.remoteDraftConfirmed) inlineError = inlineError || '';
    paint();
  }));
  ledger.sub(match.onOpponentStateChanged(() => paint()));

  paint();
  return { unmount() { ledger.dispose(); } };
}

export default { mount };
