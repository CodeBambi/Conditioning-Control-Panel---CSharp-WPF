/* ============================================================================
 * ui/screens/join.js — the six-cell code entry.
 *
 * ONE real <input> (off-screen but focusable, so mobile keyboards, IMEs, paste
 * and autofill all behave) with six rendered cells mirroring its value. Six
 * separate inputs is the classic version of this control and it is the wrong
 * one: it breaks paste, breaks backspace-across-cells, and confuses screen
 * readers about how many fields there are.
 *
 * NORMALISATION IS THE SERVER'S, VERBATIM: net/signaling.js normalizeCode is
 * trim + uppercase + strip dashes/spaces, and nothing else. It deliberately does
 * NOT fold Crockford's I/L->1 or O->0, so neither may we — silently rewriting a
 * character turns "you typed it wrong" into "that room does not exist", which is
 * the single most confusing failure this screen can produce.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import { normalizeCode } from '../../net/signaling.js';

const CODE_LEN = 6;

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { actions, audio, prefs, sheets } = ctx;
  let busy = false;

  const cells = [];
  const cellRow = el('div', { class: 'gg-code gg-code--input', 'aria-hidden': 'true' });
  for (let i = 0; i < CODE_LEN; i++) {
    const c = el('span', { class: 'gg-code-cell' });
    cells.push(c);
    cellRow.appendChild(c);
  }

  const input = el('input', {
    class: 'gg-code-input',
    type: 'text',
    inputmode: 'latin',
    autocomplete: 'one-time-code',
    autocapitalize: 'characters',
    spellcheck: 'false',
    maxlength: '32',            // paste room; normalization trims it back to six
    'aria-label': 'invite code, six characters',
  });

  const error = el('p', { class: 'gg-inline-error', hidden: true });
  const joinBtn = button(ledger, S.join.action, () => submit(), { variant: 'primary', audio });
  joinBtn.disabled = true;
  const backBtn = button(ledger, S.join.back, () => actions.goTitle(), { variant: 'ghost', audio, sfx: 'ui-back' });

  const card = el('div', { class: 'gg-card gg-join' }, [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.join.eyebrow })]),
    el('p', { class: 'gg-lead', text: S.join.lead }),
    el('div', { class: 'gg-code-wrap' }, [input, cellRow]),
    error,
    el('div', { class: 'gg-join-actions' }, [backBtn, joinBtn]),
  ]);
  container.appendChild(card);

  /* ---------------------------------------------------------------- input */

  function value() { return normalizeCode(input.value).slice(0, CODE_LEN); }

  function paint() {
    const v = value();
    for (let i = 0; i < CODE_LEN; i++) {
      const c = cells[i];
      c.textContent = v[i] || '';
      c.classList.toggle('is-filled', i < v.length);
      c.classList.toggle('is-cursor', i === v.length && !busy);
    }
    cellRow.classList.toggle('is-focused', document.activeElement === input);
    joinBtn.disabled = busy || v.length !== CODE_LEN;
  }

  ledger.listen(input, 'input', () => {
    // Rewrite the field to the normalized form so what the cells show and what
    // goes on the wire can never diverge (paste of "ab-c d3f" -> "ABCD3F").
    const v = value();
    if (input.value !== v) input.value = v;
    if (!error.hidden) { error.hidden = true; }
    paint();
    if (v.length === CODE_LEN) { try { audio?.sfx?.('code-cell'); } catch (_e) { /* stub */ } }
  });
  ledger.listen(input, 'keydown', (e) => {
    if (e.key === 'Enter' && !joinBtn.disabled) { e.preventDefault(); submit(); }
  });
  ledger.listen(input, 'focus', paint);
  ledger.listen(input, 'blur', paint);
  ledger.listen(cellRow, 'click', () => { try { input.focus(); } catch (_e) { /* ignore */ } });

  const remembered = prefs?.get?.('lastCode') || '';
  if (remembered) input.value = normalizeCode(remembered).slice(0, CODE_LEN);
  paint();
  ledger.timer(() => { try { input.focus(); } catch (_e) { /* ignore */ } }, 0);

  /* ----------------------------------------------------------------- flow */

  function fail(message) {
    error.textContent = message;
    error.hidden = false;
    cellRow.classList.add('is-bad');
    ledger.timer(() => cellRow.classList.remove('is-bad'), 500);
    try { audio?.sfx?.('ui-error'); } catch (_e) { /* stub bus */ }
  }

  function inlineFor(kind) {
    switch (kind) {
      case 'unknown_code': return S.join.errUnknown;
      case 'already_joined': return S.join.errFull;
      case 'self_join': return S.join.errSelf;
      case 'expired': return S.join.errExpired;
      default: return null;
    }
  }

  async function submit() {
    const v = value();
    if (busy) return;
    if (v.length !== CODE_LEN) { fail(S.join.errShort); return; }

    busy = true;
    joinBtn.disabled = true;
    joinBtn.classList.add('is-busy');
    joinBtn.textContent = S.join.joining;
    input.disabled = true;
    paint();

    let res = null;
    try {
      res = await actions.joinStart(v);
    } catch (e) {
      res = { ok: false, error: { kind: 'connect_error', detail: (e && e.message) || '' } };
    }
    if (ledger.isDisposed) return;

    if (res && res.ok) {
      prefs?.set?.('lastCode', v);
      return;   // boot's phase router takes it from here (Lobby -> Consent)
    }

    busy = false;
    input.disabled = false;
    joinBtn.classList.remove('is-busy');
    joinBtn.textContent = S.join.action;
    paint();

    const kind = (res && res.error && res.error.kind) || '';
    const inline = inlineFor(kind);
    if (inline) { fail(inline); try { input.focus(); } catch (_e) { /* ignore */ } return; }

    // Everything else is a condition the player cannot fix by re-reading the
    // code (server down, no pass, offline) — that is a sheet, not a red line.
    const answer = await sheets?.showSignalError?.(res && res.error);
    if (ledger.isDisposed) return;
    if (answer === 'cancel') actions.goTitle();
  }

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
