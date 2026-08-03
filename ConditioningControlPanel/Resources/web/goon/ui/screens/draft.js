/* ============================================================================
 * ui/screens/draft.js — pick three elements. THEY ARE YOURS TO ENDURE.
 *
 * The single most misread screen in the game, so the copy leads with it: your
 * draft builds YOUR ramp (core/draft.js buildRamp runs on your own picks), not
 * your opponent's. Higher risk is a higher score multiplier because you are
 * betting on your own endurance, not theirs.
 *
 * Three tile states, all of them load-bearing:
 *   is-picked      — in your three, with its selection-order badge
 *   is-locked-out  — you already have three; this one is simply not available
 *   is-unsupported — outside match.availableDraftPool, i.e. the CAPS
 *                    INTERSECTION. Their client cannot mirror it, so drafting it
 *                    would desync the match. The engine rejects it anyway
 *                    (setDraft -> _allPicksAvailable); the tile says so first.
 *
 * The engine, not this screen, advances the phase: both drafts locked ->
 * host proposes the countdown. The glow sweep is the acknowledgement, not the
 * trigger.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S, ELEMENTS } from '../strings.js';
import { PICKS_PER_PLAYER, MAX_MATCH_RISK_TIER, matchRiskTier, riskMultiplier, riskTierOf } from '../../core/draft.js';

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

  /** Local selection order. The engine only ever sees a complete three. */
  let picks = match.localDraft.slice();

  /* --------------------------------------------------------- duel header */

  const mkMeter = (who) => {
    const segs = [];
    const bar = el('div', { class: 'gg-riskmeter gg-riskmeter--' + who });
    for (let i = 0; i < MAX_MATCH_RISK_TIER; i++) {
      const s = el('i', { class: 'gg-riskseg' + (i === MAX_MATCH_RISK_TIER - 1 ? ' is-gold' : '') });
      segs.push(s);
      bar.appendChild(s);
    }
    return { bar, segs };
  };
  const youMeter = mkMeter('you');
  const themMeter = mkMeter('them');

  const header = el('div', { class: 'gg-draft-head' }, [
    el('div', { class: 'gg-draft-side' }, [
      el('span', { class: 'gg-draft-who', text: match.localDisplayName || S.lobby.you }),
      youMeter.bar,
    ]),
    el('span', { class: 'gg-duel-vs', text: 'vs', 'aria-hidden': 'true' }),
    el('div', { class: 'gg-draft-side gg-draft-side--them' }, [
      el('span', { class: 'gg-draft-who', text: match.opponent.displayName || S.lobby.them }),
      themMeter.bar,
    ]),
  ]);

  /* ----------------------------------------------------------- the grid */

  const tiles = new Map();   // element -> {node, badge}
  const grid = el('div', { class: 'gg-elems', role: 'group', 'aria-label': 'draft pool' });

  for (const meta of ELEMENTS) {
    const pips = el('span', { class: 'gg-riskpips', 'aria-hidden': 'true' });
    const tier = riskTierOf(meta.id);
    for (let i = 0; i < 3; i++) pips.appendChild(el('i', { class: i < tier ? 'is-on' : '' }));

    const badge = el('span', { class: 'gg-elem-badge', hidden: true });
    const node = el('button', {
      type: 'button',
      class: 'gg-elem',
      dataset: { element: String(meta.id) },
      'aria-pressed': 'false',
    }, [
      badge,
      el('span', { class: 'gg-elem-glyph', html: GLYPHS[meta.id] || '' }),
      el('span', { class: 'gg-elem-name', text: meta.name }),
      pips,
      el('span', { class: 'gg-elem-blurb', text: meta.blurb }),
      el('span', { class: 'gg-elem-why', text: S.draft.unsupported }),
    ]);
    ledger.listen(node, 'click', () => toggle(meta.id));
    ledger.listen(node, 'pointerenter', () => { try { audio?.sfx?.('ui-move'); } catch (_e) { /* stub */ } });
    tiles.set(meta.id, { node, badge });
    grid.appendChild(node);
  }

  /* ---------------------------------------------------------- the footer */

  const riskLabel = el('span', { class: 'gg-draft-risk', text: '' });
  const multLabel = el('span', { class: 'gg-draft-mult', text: '' });
  const footMeter = mkMeter('foot');
  const lockBtn = button(ledger, S.draft.lock, () => lockIn(), { variant: 'primary', audio, sfx: 'draft-lock' });

  const theirSlots = el('div', { class: 'gg-slots', 'aria-label': S.draft.theirs });
  for (let i = 0; i < PICKS_PER_PLAYER; i++) theirSlots.appendChild(el('span', { class: 'gg-slot gg-deco' }));

  const footer = el('div', { class: 'gg-draft-foot' }, [
    el('div', { class: 'gg-draft-meterwrap' }, [riskLabel, footMeter.bar, multLabel]),
    el('div', { class: 'gg-draft-theirs' }, [el('span', { class: 'gg-slots-label', text: S.draft.theirs }), theirSlots]),
    lockBtn,
  ]);

  const card = el('div', { class: 'gg-card gg-draft' }, [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.draft.eyebrow })]),
    el('p', { class: 'gg-lead', text: S.draft.lead }),
    header, grid, footer,
  ]);
  container.appendChild(card);

  /* -------------------------------------------------------------- logic */

  function isAvailable(id) { return match.availableDraftPool.includes(id); }

  function toggle(id) {
    if (match.localDraftLocked) return;
    if (!isAvailable(id)) {
      toasts?.warn?.(S.draft.unsupported);
      try { audio?.sfx?.('ui-error'); } catch (_e) { /* stub bus */ }
      return;
    }
    const at = picks.indexOf(id);
    if (at >= 0) {
      picks.splice(at, 1);
      try { audio?.sfx?.('draft-drop'); } catch (_e) { /* stub bus */ }
    } else {
      if (picks.length >= PICKS_PER_PLAYER) return;
      picks.push(id);
      try { audio?.sfx?.('draft-pick'); } catch (_e) { /* stub bus */ }
    }
    // Broadcast only a COMPLETE draft — the wire has no "partial" shape, and a
    // half-draft on their screen would just be noise.
    if (picks.length === PICKS_PER_PLAYER) {
      const res = match.setDraft(picks.slice());
      if (!res.ok) {
        ledger.logger?.warn?.('[GG ui] setDraft rejected: ' + res.error);
        toasts?.bad?.(res.error);
      }
    }
    paint();
  }

  function lockIn() {
    if (picks.length !== PICKS_PER_PLAYER || match.localDraftLocked) return;
    const set = match.setDraft(picks.slice());
    if (!set.ok) { toasts?.bad?.(set.error); return; }
    const res = match.lockDraft();
    if (!res.ok) { toasts?.bad?.(res.error); return; }
    paint();
  }

  function paintMeter(meter, tier) {
    for (let i = 0; i < meter.segs.length; i++) meter.segs[i].classList.toggle('is-on', i < tier);
  }

  function paint() {
    const locked = match.localDraftLocked;
    const full = picks.length >= PICKS_PER_PLAYER;

    for (const meta of ELEMENTS) {
      const t = tiles.get(meta.id);
      const order = picks.indexOf(meta.id);
      const avail = isAvailable(meta.id);
      t.node.classList.toggle('is-picked', order >= 0);
      t.node.classList.toggle('is-unsupported', !avail);
      t.node.classList.toggle('is-locked-out', avail && order < 0 && full);
      t.node.disabled = locked || !avail;
      t.node.setAttribute('aria-pressed', order >= 0 ? 'true' : 'false');
      t.badge.hidden = order < 0;
      t.badge.textContent = order >= 0 ? String(order + 1) : '';
    }

    const myTier = matchRiskTier(picks);
    paintMeter(youMeter, myTier);
    paintMeter(footMeter, myTier);
    paintMeter(themMeter, matchRiskTier(match.remoteDraft));
    riskLabel.textContent = S.draft.risk(myTier);
    multLabel.textContent = S.draft.score(riskMultiplier(myTier));

    const remote = match.remoteDraft;
    const slots = theirSlots.children;
    for (let i = 0; i < slots.length; i++) {
      const has = i < remote.length;
      slots[i].classList.toggle('is-filled', has);
      slots[i].textContent = has ? (ELEMENTS.find((e) => e.id === remote[i])?.name || '?') : '';
    }
    theirSlots.classList.toggle('is-locked', match.remoteDraftLocked);

    lockBtn.disabled = locked || !full;
    lockBtn.textContent = locked ? S.draft.locked : (full ? S.draft.lock : S.draft.pickCta);
    lockBtn.classList.toggle('is-waiting', locked);

    // Both locked: a single sweep, then the engine takes it to Countdown.
    const both = locked && match.remoteDraftLocked;
    if (both && !card.classList.contains('is-sealed')) {
      card.classList.add('is-sealed');
      try { audio?.sfx?.('draft-lock'); } catch (_e) { /* stub bus */ }
    }
  }

  ledger.sub(match.onDraftChanged(() => {
    // The engine is authoritative on what it accepted; adopt it whenever it
    // disagrees with our local order (a rejected pick, or a rebuild).
    if (match.localDraftLocked) picks = match.localDraft.slice();
    paint();
  }));
  ledger.sub(match.onOpponentStateChanged(() => paint()));

  paint();
  return { unmount() { ledger.dispose(); } };
}

export default { mount };
