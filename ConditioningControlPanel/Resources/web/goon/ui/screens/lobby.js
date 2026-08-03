/* ============================================================================
 * ui/screens/lobby.js — the duel card AND the consent sheet, on one screen.
 *
 * They are one screen because they are one decision: you are looking at who you
 * are about to play and the terms you are about to play under, and you sign
 * both at once. Splitting them would let a player agree to terms before seeing
 * the opponent's client version — which is exactly the mismatch that produces a
 * LobbyFailed teardown three seconds later.
 *
 * THE CONFIRM STRIP IS THE SAFETY SURFACE. Any change to any term clears BOTH
 * lamps (core/match.js proposeConsent does this on the engine side; the ripple
 * here is only the visible half). Nobody is ever advanced onto terms they did
 * not see land. The line "Settings changed — both of you confirm again." is the
 * explanation, not decoration.
 *
 * The toy row renders DISABLED in v1: the local caps advertise no ToyPatterns
 * (boot narrows them), so a toy cap the player could drag would be a control
 * that changes nothing. It stays visible, greyed, saying why.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S, minutes } from '../strings.js';
import { GoonMatchPhase, GoonTransportState } from '../../core/contracts.js';

const DUR_MIN_SEC = 60;
const DUR_MAX_SEC = 3600;
const GAP_MIN_SEC = 15;
const GAP_MAX_SEC = 60;

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { actions, audio, prefs, sheets, getMatch, getTransport } = ctx;
  const match = getMatch();
  if (!match) {
    container.appendChild(el('div', { class: 'gg-card', text: S.lobby.connecting }));
    return { unmount() { ledger.dispose(); } };
  }

  let lastLocalConfirmed = false;
  let lastRemoteConfirmed = false;

  /* --------------------------------------------------------- duel header */

  const mkSide = (who) => {
    const name = el('p', { class: 'gg-duel-name', text: S.lobby.unknown });
    const badges = el('div', { class: 'gg-duel-badges' });
    const version = el('p', { class: 'gg-duel-version', text: '' });
    const side = el('div', { class: 'gg-duel-side gg-duel-side--' + who }, [
      el('span', { class: 'gg-duel-role', text: who === 'you' ? S.lobby.you : S.lobby.them }),
      name, badges, version,
    ]);
    return { side, name, badges, version };
  };

  const you = mkSide('you');
  const them = mkSide('them');
  const duel = el('div', { class: 'gg-duel' }, [
    you.side,
    el('span', { class: 'gg-duel-vs', text: 'vs', 'aria-hidden': 'true' }),
    them.side,
  ]);

  const connLine = el('p', { class: 'gg-conn', text: S.lobby.connecting });

  /* -------------------------------------------------------- consent rows */

  function mkSlider(labelText, { min, max, step, disabled = false }) {
    const value = el('span', { class: 'gg-row-value', text: '' });
    const input = el('input', {
      type: 'range',
      min: String(min), max: String(max), step: String(step),
      disabled: disabled ? true : null,
      'aria-label': labelText,
    });
    const row = el('div', { class: 'gg-row' + (disabled ? ' is-disabled' : '') }, [
      el('span', { class: 'gg-row-label', text: labelText }),
      input,
      value,
    ]);
    return { row, input, value };
  }

  const durRow = mkSlider(S.lobby.duration, { min: DUR_MIN_SEC, max: DUR_MAX_SEC, step: 60 });
  const toyRow = mkSlider(S.lobby.toyCap, { min: 0, max: 100, step: 5, disabled: true });
  toyRow.value.textContent = S.lobby.toyCapDisabled;
  const gapRow = mkSlider(S.lobby.gap, { min: GAP_MIN_SEC, max: GAP_MAX_SEC, step: 5 });

  const sheetBox = el('div', { class: 'gg-consent' }, [durRow.row, toyRow.row, gapRow.row]);

  /* ---------------------------------------------------------- confirm UI */

  const lampYou = el('span', { class: 'gg-lamp' }, [el('i'), el('span', { text: S.lobby.lampYou })]);
  const lampThem = el('span', { class: 'gg-lamp' }, [el('i'), el('span', { text: S.lobby.lampThem })]);
  const lamps = el('div', { class: 'gg-lamps' }, [lampYou, lampThem]);
  const changedLine = el('p', { class: 'gg-consent-changed', text: S.lobby.changed, hidden: true });

  const confirmBtn = button(ledger, S.lobby.confirm, () => onConfirm(), { variant: 'primary', audio, sfx: 'lamp-confirm' });
  const leaveBtn = button(ledger, S.lobby.leave, () => actions.leave('lobby'), { variant: 'ghost', audio, sfx: 'ui-back' });

  const eyebrow = el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.lobby.eyebrowWaiting })]);

  container.appendChild(el('div', { class: 'gg-card gg-lobby' }, [
    eyebrow, duel, connLine, sheetBox, lamps, changedLine,
    el('div', { class: 'gg-lobby-actions' }, [leaveBtn, confirmBtn]),
  ]));

  /* ------------------------------------------------------------- painting */

  function paintIdentity() {
    you.name.textContent = match.localDisplayName || 'you';
    you.badges.replaceChildren(el('span', { class: 'gg-badge', text: S.lobby.noCam }));
    you.version.textContent = match.localAppVersion ? 'v' + match.localAppVersion : '';

    const hello = match.remoteHello;
    const opp = match.opponent;
    const known = !!hello;
    them.side.classList.toggle('is-unknown', !known);
    them.name.textContent = known ? (opp.displayName || 'them') : S.lobby.unknown;
    them.badges.replaceChildren(known
      ? el('span', { class: 'gg-badge', text: opp.attentionMode === 0 ? S.lobby.cam : S.lobby.noCam })
      : el('span', { class: 'gg-badge is-ghost', text: '…' }));
    them.version.textContent = known && opp.appVersion ? 'v' + opp.appVersion : '';

    eyebrow.lastChild.textContent = known ? S.lobby.eyebrowReady : S.lobby.eyebrowWaiting;
    sheetBox.classList.toggle('is-waiting', !known);
  }

  function paintConnection() {
    const t = getTransport ? getTransport() : null;
    const st = t ? t.state : GoonTransportState.Disconnected;
    let text = S.lobby.offline;
    let cls = 'is-off';
    if (st === GoonTransportState.ConnectedP2P) { text = S.lobby.direct; cls = 'is-direct'; }
    else if (st === GoonTransportState.ConnectedRelay) { text = S.lobby.relay; cls = 'is-relay'; }
    else if (st === GoonTransportState.Signaling || st === GoonTransportState.ConnectingP2P
      || st === GoonTransportState.Reconnecting) { text = S.lobby.connecting; cls = 'is-pending'; }
    connLine.textContent = text;
    connLine.className = 'gg-conn ' + cls;
  }

  /** Renders the ENGINE's sheet, never a local shadow copy — the engine clamps. */
  function paintSheet() {
    const s = match.consentSheet;
    const durSec = s.live_duration_sec;
    const gapSec = Math.round(s.payload_min_gap_ms / 1000);

    if (document.activeElement !== durRow.input) durRow.input.value = String(durSec);
    durRow.value.textContent = minutes(durSec);
    if (document.activeElement !== gapRow.input) gapRow.input.value = String(gapSec);
    gapRow.value.textContent = S.lobby.gapValue(gapSec);

    const editable = match.phase === GoonMatchPhase.Consent;
    durRow.input.disabled = !editable;
    gapRow.input.disabled = !editable;
  }

  function paintConfirm() {
    const localOk = match.localConsentConfirmed;
    const remoteOk = match.remoteConsentConfirmed;

    lampYou.classList.toggle('is-on', localOk);
    lampThem.classList.toggle('is-on', remoteOk);

    // The ripple fires only on a CLEAR (a change wiped a signature), which is
    // the moment that needs explaining. Turning a lamp ON needs no apology.
    const cleared = (lastLocalConfirmed && !localOk) || (lastRemoteConfirmed && !remoteOk);
    if (cleared) {
      lamps.classList.remove('is-rippling');
      void lamps.offsetWidth;              // restart the animation
      lamps.classList.add('is-rippling');
      changedLine.hidden = false;
      try { audio?.sfx?.('lamp-clear'); } catch (_e) { /* stub bus */ }
      ledger.timer(() => lamps.classList.remove('is-rippling'), 700);
    }
    if (localOk && remoteOk) changedLine.hidden = true;
    lastLocalConfirmed = localOk;
    lastRemoteConfirmed = remoteOk;

    confirmBtn.textContent = localOk
      ? S.lobby.confirmed(match.opponent.displayName || S.lobby.them)
      : S.lobby.confirm;
    confirmBtn.classList.toggle('is-ready', localOk);
    confirmBtn.disabled = match.phase !== GoonMatchPhase.Consent;
    confirmBtn.title = localOk ? 'click to withdraw' : '';
  }

  function paintAll() { paintIdentity(); paintConnection(); paintSheet(); paintConfirm(); }

  /* --------------------------------------------------------------- input */

  function propose() {
    const durSec = clampNum(durRow.input.value, DUR_MIN_SEC, DUR_MAX_SEC);
    const gapSec = clampNum(gapRow.input.value, GAP_MIN_SEC, GAP_MAX_SEC);
    prefs?.set?.('matchLengthSec', durSec);
    prefs?.set?.('payloadGapSec', gapSec);
    // toy_cap rides through untouched: the row is disabled, so the value the
    // engine already holds is the one both sides agreed to.
    match.proposeConsent(durSec, match.consentSheet.toy_cap, gapSec * 1000);
    paintSheet();
  }

  function clampNum(v, lo, hi) {
    const n = Math.round(Number(v) || 0);
    return n < lo ? lo : n > hi ? hi : n;
  }

  for (const row of [durRow, gapRow]) {
    // `input` = live label only (no wire traffic while a thumb is moving);
    // `change` = commit, which clears both lamps on both machines.
    ledger.listen(row.input, 'input', () => {
      const durSec = clampNum(durRow.input.value, DUR_MIN_SEC, DUR_MAX_SEC);
      const gapSec = clampNum(gapRow.input.value, GAP_MIN_SEC, GAP_MAX_SEC);
      durRow.value.textContent = minutes(durSec);
      gapRow.value.textContent = S.lobby.gapValue(gapSec);
    });
    ledger.listen(row.input, 'change', propose);
  }

  function onConfirm() {
    if (match.phase !== GoonMatchPhase.Consent) return;
    if (match.localConsentConfirmed) match.withdrawConsent();
    else match.confirmConsent();
    paintConfirm();
  }

  /* --------------------------------------------------------- engine wiring */

  ledger.sub(match.onConsentChanged(() => { paintSheet(); paintConfirm(); }));
  ledger.sub(match.onOpponentStateChanged(() => paintIdentity()));
  ledger.sub(match.onPhaseChanged(() => { paintAll(); }));
  ledger.sub(match.onLobbyFailed((reason) => {
    sheets?.open?.({
      icon: S.sheets.lobbyFailed.icon,
      headline: S.sheets.lobbyFailed.headline,
      line: S.sheets.lobbyFailed.line(reason),
    }).then(() => { if (!ledger.isDisposed) actions.goTitle(); });
  }));

  const transport = getTransport ? getTransport() : null;
  if (transport && typeof transport.onStateChanged === 'function') {
    ledger.sub(transport.onStateChanged(() => paintConnection()));
  } else {
    ledger.interval(paintConnection, 1000);   // no event seam: poll cheaply
  }

  // Seed the sheet from the player's last duel the FIRST time we reach Consent
  // as the proposer. The engine's host-authored opening proposal has already
  // fired by then; this only replaces it when the player has a remembered
  // preference that differs, so a guest never sees two proposals for nothing.
  if (match.isHost && match.phase === GoonMatchPhase.Consent && prefs) {
    const wantDur = clampNum(prefs.get('matchLengthSec'), DUR_MIN_SEC, DUR_MAX_SEC);
    const wantGap = clampNum(prefs.get('payloadGapSec'), GAP_MIN_SEC, GAP_MAX_SEC);
    const s = match.consentSheet;
    if (s.live_duration_sec !== wantDur || Math.round(s.payload_min_gap_ms / 1000) !== wantGap) {
      match.proposeConsent(wantDur, s.toy_cap, wantGap * 1000);
    }
  }

  paintAll();
  try { audio?.music?.('lobby'); } catch (_e) { /* stub bus */ }

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
