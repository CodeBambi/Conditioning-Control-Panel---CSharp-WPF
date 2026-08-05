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
import { buildDiscordSection, askSharePrompt } from '../discord.js';
import { GoonMatchPhase, GoonTransportState } from '../../core/contracts.js';

const DUR_MIN_SEC = 60;
const DUR_MAX_SEC = 3600;
const GAP_MIN_SEC = 15;
const GAP_MAX_SEC = 60;

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { actions, audio, prefs, sheets, discord, getMatch, getTransport } = ctx;
  const match = getMatch();

  /* RICH PRESENCE, FROM THE MOMENT THERE IS SOMETHING TO SAY. Posted before the
   * early return below, because "connecting…" is still the lobby as far as
   * anyone reading a Discord status is concerned — and boot posts `off` on every
   * road out, so this can never be the frame that strands one. */
  try { discord?.setRpState?.('lobby'); } catch (_e) { /* presence is never load-bearing */ }

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

  /* "<name> joined — picking their media…" (the `media_prep` frame, protocol §6).
   * A first-time guest who arrived on an invite link is held on the media-setup
   * step before this screen, which from the HOST's side used to look identical
   * to an empty room: the code was read, somebody tapped it, and the lobby still
   * said "waiting for them". This line is the difference, and it costs nothing
   * against a peer that never sends the frame — absent reads as "ready", so an
   * older build simply never lights it. */
  const prepLine = el('p', { class: 'gg-lobby-prep', text: '', hidden: true, role: 'status' });

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

  /**
   * The media-transfer opt-in. A CHECKBOX, not a slider, because it is not a term
   * both sides negotiate — it is a PER-SIDE DECLARATION that rides the consent
   * frame (core/match.js setMediaTransfer; `sameSheet()` must never learn about it
   * or an older peer would wedge the lobby forever). The chip on the right is what
   * THEY declared, which is the same both-sides display the sliders get for free.
   *
   * Flipping it clears both lamps by hand in the engine, so the existing ripple
   * covers it with no extra wiring here.
   */
  function mkCheck(labelText) {
    const value = el('span', { class: 'gg-row-value', text: '' });
    const input = el('input', { type: 'checkbox', 'aria-label': labelText });
    const row = el('div', { class: 'gg-row gg-row--check' }, [
      el('span', { class: 'gg-row-label', text: labelText }),
      input,
      value,
    ]);
    const sub = el('p', { class: 'gg-row-sub', text: S.lobby.transferSub });
    return { row, input, value, sub };
  }

  const durRow = mkSlider(S.lobby.duration, { min: DUR_MIN_SEC, max: DUR_MAX_SEC, step: 60 });
  const toyRow = mkSlider(S.lobby.toyCap, { min: 0, max: 100, step: 5, disabled: true });
  toyRow.value.textContent = S.lobby.toyCapDisabled;
  const gapRow = mkSlider(S.lobby.gap, { min: GAP_MIN_SEC, max: GAP_MAX_SEC, step: 5 });
  const xferRow = mkCheck(S.lobby.transfer);

  /* THE VOICE-NOTE ROW (2026-08-05) — a real toggle now, not a status line.
   *
   * IT WAS READ-ONLY UNTIL TODAY, and that was the whole bug. The reasoning was
   * sound and the consequence was not: turning voice notes on is an
   * acknowledgment (your real voice goes to them, theirs can come back), so the
   * switch was put on ONE screen — ui/screens/voice.js, off the title menu —
   * and the lobby only reported what somebody had already decided somewhere
   * else. Nobody ever decided it. The owner play-tested the mic three times
   * across three setups and never saw it once, because their own seat's
   * `voiceNotesEnabled` had never been true and nothing on the road into a duel
   * asked. A consent gate that nobody can find is not protecting anyone.
   *
   * So the toggle is here as well, and the gate came WITH it: the checkbox is
   * `disabled` until `voiceAckSeen`, and a click on the ROW (which is not
   * disabled) opens the very same acknowledgment sheet ui/screens/voice.js
   * opens, from the same S.voice.ack copy. There is still exactly ONE way to
   * switch this on for the first time and it still goes through both
   * paragraphs. This is a second door to the same gate, not a way around it.
   *
   * IT WRITES BOTH HALVES, AND THAT ORDER MATTERS FOR *THIS* MATCH:
   *   prefs.voiceNotesEnabled   the standing answer, and the one the RECEIVE
   *                             path reads (ui/voice/voiceService.js drops an
   *                             inbound note unread without it) — set first, so
   *                             a consent frame can never announce a mic the
   *                             local gate would still refuse.
   *   match.setLocalVoiceNotes  the declaration the OPPONENT gets told, on the
   *                             consent frame, immediately. Without this call a
   *                             tick here would only take effect on the NEXT
   *                             attachMatch, i.e. after a page reload — which
   *                             is exactly the trap the raw-localStorage
   *                             play-test hit (ui/prefs.js caches in memory).
   * The engine clears both signatures and re-sends the sheet, so the lamp
   * ripple this screen already paints is the whole visible half. */
  const voiceRow = mkCheck(S.voice.toggle);
  voiceRow.row.classList.add('gg-voice-lobbyrow');
  voiceRow.sub.classList.add('gg-voice-lobbyline');

  const sheetBox = el('div', { class: 'gg-consent' }, [
    durRow.row, toyRow.row, gapRow.row, xferRow.row, xferRow.sub, voiceRow.row, voiceRow.sub,
  ]);

  /* ---------------------------------------------------------- confirm UI */

  const lampYou = el('span', { class: 'gg-lamp' }, [el('i'), el('span', { text: S.lobby.lampYou })]);
  const lampThem = el('span', { class: 'gg-lamp' }, [el('i'), el('span', { text: S.lobby.lampThem })]);
  const lamps = el('div', { class: 'gg-lamps' }, [lampYou, lampThem]);
  const changedLine = el('p', { class: 'gg-consent-changed', text: S.lobby.changed, hidden: true });

  // `void`, because onConfirm is async now (the share sheet): the ledger's click
  // wrapper is synchronous and a returned promise would reject into nowhere.
  const confirmBtn = button(ledger, S.lobby.confirm, () => { void onConfirm(); }, { variant: 'primary', audio, sfx: 'lamp-confirm' });
  const leaveBtn = button(ledger, S.lobby.leave, () => actions.leave('lobby'), { variant: 'ghost', audio, sfx: 'ui-back' });

  const eyebrow = el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.lobby.eyebrowWaiting })]);

  container.appendChild(el('div', { class: 'gg-card gg-lobby' }, [
    eyebrow, duel, connLine, prepLine, sheetBox, lamps, changedLine,
    el('div', { class: 'gg-lobby-actions' }, [leaveBtn, confirmBtn]),
  ]));

  /* --- THE DISCORD PANEL ---------------------------------------------------
   * Its own card UNDER the duel card, deliberately not inside it: nothing on it
   * is negotiated, countersigned or put on the wire, and folding it into the
   * consent sheet would imply the opponent had a say in it. It is prominent
   * because the decision it holds is the one on this screen a player must never
   * find out about afterwards.
   *
   * The whole thing is optional: a page built before ui/discord.js existed (or
   * a ctx without it) simply gets the duel card, the way it does today. */
  if (discord) {
    try {
      const dc = buildDiscordSection({
        discord, ledger, prefs, audio,
        youName: match.localDisplayName || (ctx.session && ctx.session.identity && ctx.session.identity.displayName) || '',
      });
      if (dc && dc.node) container.appendChild(dc.node);
    } catch (e) {
      // A panel that cannot build must never take the lobby down with it — this
      // is the screen the terms get signed on.
      ledger._err('discord section', e);
    }
  }

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

    // THEY ARE HERE, THEY ARE JUST BUSY. `remoteMediaPrep` outranks the plain
    // "waiting for them" eyebrow because it answers a different question: not
    // "has anybody arrived" (they have) but "why is nothing happening".
    const picking = !!match.remoteMediaPrep;
    prepLine.textContent = picking
      ? S.lobby.prepPicking(opp.displayName || (hello && hello.display_name) || S.lobby.them)
      : '';
    prepLine.hidden = !picking;

    eyebrow.lastChild.textContent = picking
      ? S.lobby.eyebrowPicking
      : (known ? S.lobby.eyebrowReady : S.lobby.eyebrowWaiting);
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

  /**
   * The transfer row. THREE separate reasons it can be off, and each one gets its
   * own line, because "greyed out with no explanation" is the thing players report
   * as a bug: the send capability is off for this seat, the peer's build does not
   * speak the protocol, or the link is relayed (media never touches the server,
   * so a relay physically cannot carry it).
   *
   * THE FIRST REASON USED TO BE "not a supporter" AND IS NOT ANY MORE (owner call
   * 2026-08-05). Sending is free for every seat — the paid perk is hosting — so
   * caps.mediaTransfer is true on every current host and this arm is now reserved
   * for a server that vetoes sending (`media_send:false`) or a frame too old to
   * carry the flag. Kept as an arm rather than deleted so the row can never go
   * dark in silence; see S.lobby.transferOff.
   */
  function paintTransfer() {
    const caps = (ctx.session && ctx.session.caps) || {};
    const t = getTransport ? getTransport() : null;
    const onP2P = !!(t && t.state === GoonTransportState.ConnectedP2P);
    /* Only a transport that has SETTLED somewhere else gets the terminal
     * "relayed" sentence. During Signaling/ConnectingP2P the honest answer is
     * "still deciding" — showing the verdict early is how the 2026-08-05
     * play-test phone confirmed consent with the box greyed and spent the
     * whole match reading "their toggle is off" on the other screen. */
    const stillDeciding = !!(t && (t.state === GoonTransportState.Signaling
      || t.state === GoonTransportState.ConnectingP2P));
    const editable = match.phase === GoonMatchPhase.Consent || match.phase === GoonMatchPhase.Lobby;

    let why = '';
    if (caps.mediaTransfer !== true) why = S.lobby.transferOff;
    else if (!match.peerSupportsTransfer) why = S.lobby.transferPeerOld;
    else if (!onP2P) why = stillDeciding ? S.lobby.transferConnecting : S.lobby.transferRelay;

    /* Ammo honesty (round-11's last root cause): everything above can pass and
     * the lane still starves when nothing local is compressed. A HINT, not a
     * block — the box stays live (receiving works regardless) and the 1s
     * repaint updates the line as compression fills the count. */
    let hint = '';
    if (!why && ctx.mediaQueue && typeof ctx.mediaQueue.sendableCount === 'function') {
      try { if (match.localMediaTransfer && ctx.mediaQueue.sendableCount() === 0) hint = S.lobby.transferNoAmmo; }
      catch (_e) { /* never let the hint break the paint */ }
    }

    if (document.activeElement !== xferRow.input) xferRow.input.checked = !!match.localMediaTransfer;
    xferRow.input.disabled = !!why || !editable;
    xferRow.row.classList.toggle('is-disabled', !!why);
    /* THE DISCLOSURE IS NOT OPTIONAL FURNITURE (2026-08-05 privacy pass). `hint`
     * used to REPLACE S.lobby.transferSub, which meant a player whose library had
     * nothing compressed yet could tick the box having read only "nothing ready to
     * send yet" — never the sentence that says the media goes to their opponent.
     * `why` still replaces it, and that is correct: those three arms all mean
     * nothing can cross at all. Whenever the box is live, the sentence is there. */
    xferRow.sub.textContent = why || (hint ? hint + ' ' + S.lobby.transferSub : S.lobby.transferSub);
    xferRow.value.textContent = match.remoteMediaTransfer
      ? S.lobby.transferTheirsOn : S.lobby.transferTheirsOff;
  }

  /**
   * The voice-note row. The CHECKBOX is our own opt-in; the chip is theirs; the
   * sub-line is the old four-state status sentence, in the order that answers
   * the question a player is actually asking ("can they hear me?"): both on,
   * the peer's build cannot, only mine, only theirs.
   *
   * UNLIKE THE TRANSFER ROW ABOVE, IT IS NEVER DISABLED FOR A PEER REASON. The
   * peer's build being too old is said in the sub-line and nowhere else: this
   * switch is also the RECEIVE gate and the standing answer for every future
   * duel, so greying it out on account of the person you happen to be facing
   * would hide a decision that is not about them. The only thing that disables
   * it is the acknowledgment (below) and the phase.
   */
  function paintVoice() {
    const mine = !!match.localVoiceNotes;
    const theirs = !!match.remoteVoiceNotes;
    const acked = !!(prefs && prefs.get && prefs.get('voiceAckSeen'));
    const editable = match.phase === GoonMatchPhase.Consent || match.phase === GoonMatchPhase.Lobby;

    let status = '';
    if (mine && theirs && match.peerSupportsVoice) status = S.voice.lobbyBoth;
    else if ((mine || theirs) && !match.peerSupportsVoice) status = S.voice.lobbyPeerOld;
    else if (mine) status = S.voice.lobbyYours;
    else if (theirs) status = S.voice.lobbyTheirs;

    if (document.activeElement !== voiceRow.input) voiceRow.input.checked = mine;
    voiceRow.input.disabled = !acked || !editable;
    voiceRow.row.classList.toggle('is-disabled', !acked);
    // The status sentence when there is one, the explanation when there is not,
    // and "read this first" ahead of both — a locked switch always says why.
    voiceRow.sub.textContent = !acked ? S.voice.lobbyLocked : (status || S.voice.lobbySub);
    voiceRow.sub.classList.toggle('is-on', status === S.voice.lobbyBoth);
    voiceRow.value.textContent = theirs ? S.voice.lobbyTheirsOn : S.voice.lobbyTheirsOff;
  }

  /**
   * The acknowledgment sheet, byte for byte the one ui/screens/voice.js opens —
   * same icon, same two paragraphs, same two actions. It is duplicated as a
   * CALL rather than shared as a module because ui/sheets.js takes one `line`
   * and this consent needs two, and the injection that adds the second one is
   * six lines of DOM either screen can do for itself; what must never diverge
   * is the COPY, and the copy is S.voice.ack in both.
   *
   * Cancel changes nothing at all. "I understand" is the switch: the player got
   * here by reaching for it, and making them press the same thing twice for one
   * decision is how a safety gate turns into an annoyance people learn to click
   * through.
   */
  async function openVoiceAck() {
    if (!sheets || typeof sheets.open !== 'function') return;
    const promise = sheets.open({
      icon: S.voice.ack.icon,
      headline: S.voice.ack.headline,
      line: S.voice.ack.line,
      actions: [
        { id: 'cancel', label: S.voice.ack.cancel, variant: 'ghost' },
        { id: 'go', label: S.voice.ack.go, variant: 'primary' },
      ],
    });
    injectAckSecondLine();
    let answer = null;
    try { answer = await promise; } catch (e) { ledger._err('voice ack', e); }
    if (ledger.isDisposed) return;
    if (answer !== 'go') { paintVoice(); return; }
    prefs?.set?.('voiceAckSeen', true);
    setVoiceEnabled(true);
  }

  /** The second paragraph, appended to the sheet that has just been built. See
   *  ui/screens/voice.js injectSecondLine — same seam, same merge fallback,
   *  because the line that says they may HEAR you is never the one that goes
   *  missing. */
  function injectAckSecondLine() {
    let line = null;
    try {
      const modal = typeof document !== 'undefined' && document ? document.getElementById('gg-modal') : null;
      line = modal && modal.querySelector ? modal.querySelector('.gg-sheet-line') : null;
      if (!line) return;
      const p = el('p', { class: 'gg-sheet-line gg-voice-ack-2', text: S.voice.ack.lineTwo });
      if (line.parentNode && typeof line.parentNode.insertBefore === 'function') {
        line.parentNode.insertBefore(p, line.nextSibling);
        return;
      }
    } catch (e) { ledger._err('voice ack line', e); }
    try { if (line) line.textContent = S.voice.ack.line + ' ' + S.voice.ack.lineTwo; }
    catch (_e) { /* no DOM at all */ }
  }

  /**
   * Both halves, in the one order that is safe (see the row's block above):
   * the local gate first, then the declaration the opponent reads.
   *
   * The engine REFUSES the declaration outside Lobby/Consent, which is the
   * whole reason the pref is written unconditionally — a refusal there must
   * still leave the standing answer recorded for the next attachMatch rather
   * than silently discarding what the player just asked for.
   */
  function setVoiceEnabled(on) {
    const want = !!on;
    prefs?.set?.('voiceNotesEnabled', want);
    try { match.setLocalVoiceNotes(want); } catch (e) { ledger._err('setLocalVoiceNotes', e); }
    paintVoice();
    paintConfirm();
  }

  function paintAll() { paintIdentity(); paintConnection(); paintSheet(); paintConfirm(); paintTransfer(); paintVoice(); }

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

  // One call, and the engine does the rest: it flips the local declaration, clears
  // BOTH confirmations and re-sends the sheet, so the lamp ripple this screen
  // already paints on a clear is the whole visible half.
  ledger.listen(xferRow.input, 'change', () => {
    match.setMediaTransfer(!!xferRow.input.checked);
    // The STANDING answer, not just this match's: boot seeds the next lobby
    // from it (default ON since 2026-08-05; an untick here is the opt-out).
    prefs?.set?.('mediaTransferEnabled', !!xferRow.input.checked);
    paintTransfer();
    paintConfirm();
  });

  /* THE VOICE ROW'S TWO AFFORDANCES, for the one reason ui/screens/voice.js
   * gives: "a disabled control that does nothing when you click it is the
   * single most reported bug in this codebase's history", so the ROW answers
   * when the INPUT cannot. */
  ledger.listen(voiceRow.input, 'change', () => {
    if (!(prefs && prefs.get && prefs.get('voiceAckSeen'))) {
      // Belt and braces — a disabled input should never fire this, but a host
      // that ignores `disabled` must not be able to switch a microphone on
      // without the two paragraphs having been on screen.
      voiceRow.input.checked = false;
      void openVoiceAck();
      return;
    }
    setVoiceEnabled(!!voiceRow.input.checked);
  });
  ledger.listen(voiceRow.row, 'click', (e) => {
    if (prefs && prefs.get && prefs.get('voiceAckSeen')) return;   // the input handles itself
    if (e && e.target === voiceRow.input) return;                  // a disabled input's click
    void openVoiceAck();
  });

  /**
   * THE ONE-TIME FIRST-DUEL CONFIRM (contract §1) hangs off "I'm in", and that
   * is the only honest place for it: signing the terms is the act of entering
   * the match, so it is the last moment a player can still find out that a
   * stranger is about to see their face and say no.
   *
   * NOT at Countdown, and this is load-bearing: boot's closeChrome() sweeps
   * every sheet the instant the clock starts (a forgotten scrim eats every
   * bubble pop for the whole run), so a sheet raised there would be torn off
   * screen a moment later — and a consent sheet that vanishes unanswered is
   * worse than no sheet at all. Swept HERE it resolves null, which writes
   * nothing and leaves the lamp unlit: the player simply presses again.
   *
   * Asked at most once per mount as well as once per account: `askSharePrompt`
   * re-checks needsSharePrompt() against the ECHO, and a confirm writes
   * seenSharePrompt through the same round trip everything else uses.
   */
  let sharePromptBusy = false;

  async function onConfirm() {
    if (match.phase !== GoonMatchPhase.Consent) return;
    if (match.localConsentConfirmed) { match.withdrawConsent(); paintConfirm(); return; }

    if (discord && !sharePromptBusy && discord.needsSharePrompt()) {
      sharePromptBusy = true;
      let answer = null;
      try { answer = await askSharePrompt({ discord, sheets }); }
      catch (e) { ledger._err('share prompt', e); answer = 'confirm'; }
      finally { sharePromptBusy = false; }
      // Dismissed (or swept by the chrome closer): nothing was decided, so
      // nothing is signed. The button is still there and still says "I'm in".
      if (answer === null) return;
      // The screen may have gone while the sheet was up (they left, the peer
      // dropped, the phase moved on) — re-check before touching the engine.
      if (ledger.isDisposed || match.phase !== GoonMatchPhase.Consent) return;
    }

    match.confirmConsent();
    paintConfirm();
  }

  /* --------------------------------------------------------- engine wiring */

  ledger.sub(match.onConsentChanged(() => { paintSheet(); paintConfirm(); paintTransfer(); paintVoice(); }));
  // The hello is what sets peerSupportsTransfer (and peerSupportsVoice), and it
  // arrives with the opponent.
  ledger.sub(match.onOpponentStateChanged(() => { paintIdentity(); paintTransfer(); paintVoice(); }));
  // `media_prep` (protocol §6). Optional seam on purpose: a match object built
  // before this message existed simply has no subscribe verb, and the line stays
  // hidden — exactly the state an absent frame means.
  if (typeof match.onMediaPrepChanged === 'function') {
    ledger.sub(match.onMediaPrepChanged(() => paintIdentity()));
  }
  ledger.sub(match.onPhaseChanged(() => { paintAll(); }));
  /* The pref is the truth and this screen is one of its readers — ui/screens/
   * voice.js and the options drawer's Reset can both move it out from under us,
   * and a checkbox that disagrees with the thing it controls is worse than no
   * checkbox. (voiceAckSeen too: reading the modal anywhere unlocks it here.) */
  if (prefs && typeof prefs.subscribe === 'function') {
    ledger.sub(prefs.subscribe((key) => {
      if (ledger.isDisposed) return;
      if (key === 'voiceNotesEnabled' || key === 'voiceAckSeen') paintVoice();
    }));
  }
  ledger.sub(match.onLobbyFailed((reason) => {
    sheets?.open?.({
      icon: S.sheets.lobbyFailed.icon,
      headline: S.sheets.lobbyFailed.headline,
      line: S.sheets.lobbyFailed.line(reason),
    }).then(() => { if (!ledger.isDisposed) actions.goTitle(); });
  }));

  const transport = getTransport ? getTransport() : null;
  if (transport && typeof transport.onStateChanged === 'function') {
    // Relay vs direct decides whether the transfer row is even offerable.
    ledger.sub(transport.onStateChanged(() => { paintConnection(); paintTransfer(); }));
  } else {
    ledger.interval(() => { paintConnection(); paintTransfer(); }, 1000);   // no event seam: poll cheaply
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
