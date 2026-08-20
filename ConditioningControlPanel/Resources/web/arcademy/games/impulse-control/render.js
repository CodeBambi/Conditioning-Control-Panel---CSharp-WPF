/* ============================================================================
 * games/impulse-control/render.js - every pixel this class owns.
 *
 * The approved look is the mockup's Impulse Control section (BUILD-CONTRACT §7 /
 * planning/arcademy/mockups): clinical letterhead strip, one centred aperture on
 * a radial ground, the flying ms readout, the attribution toast, the interference
 * log strip, and a mono footline. Classes are `.g-ic-*` (style.js).
 *
 * FEEDBACK IS ASYMMETRIC BY DESIGN (dossier "Dopamine design"): a correct GO is
 * LOUD (ring punch + gold ms readout), a correct withhold is SERENE (the decoy
 * dissolves to ash, one calm tick). Speed is celebrated, restraint is soothed -
 * the two virtues are taught by feel before any words.
 *
 * NO innerHTML anywhere: every node is created and textContent'd, so there is no
 * markup-injection surface for a mod string and the module runs unchanged against
 * the headless DOM double the scratch harness uses.
 *
 * The stamp / streak-meter / jackpot / near-miss beats are NOT reimplemented here
 * - they are shell primitives (SYNTHESIS #10) reached through ctx.ceremonies.
 * ==========================================================================*/

import { injectStyle } from './style.js';
import { IC_LEX } from './lex.js';

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = String(text);
  return n;
}

/** A mono "label value" chrome/footline cell. */
function cell(label, value) {
  const s = el('span');
  s.appendChild(document.createTextNode(label + ' '));
  const b = el('b', null, value == null ? '--' : String(value));
  s.appendChild(b);
  return { node: s, value: b };
}

export function createRender({ root, t, ceremonies, showRt, reduced, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  /* ONE resolver (see lies.js): IC_LEX is the canonical English for every ic_ row,
     so the inline literals below are documentation, never an override. */
  const lex = (k, f) => {
    const dflt = IC_LEX[k] != null ? IC_LEX[k] : f;
    try { return typeof t === 'function' ? t(k, dflt) : (dflt || k); }
    catch (e) { return dflt || k; }
  };
  const cer = ceremonies || null;
  const wantRt = showRt !== false;
  const soft = !!reduced;
  const timers = new Set();
  const n = {};                        // the node map

  injectStyle();

  const later = (fn, ms) => {
    const id = setTimeout(() => { timers.delete(id); try { fn(); } catch (e) { /* noop */ } }, ms);
    timers.add(id);
    return id;
  };
  const flash = (node, cls, ms) => {
    if (!node) return;
    node.classList.remove(cls);
    // A read forces the class removal to land before it is re-added, so a
    // back-to-back response actually replays the animation (shell trap #4's
    // lesson, one layer down).
    void node.offsetWidth;
    node.classList.add(cls);
    later(() => node.classList.remove(cls), ms);
  };

  /* ---------------------------------------------------------------- mount */
  function mount() {
    root.textContent = '';
    const wrap = el('div', 'g-ic');

    /* chrome */
    const chrome = el('div', 'g-ic-chrome');
    const head = el('span');
    head.appendChild(el('b', null, lex('ic_assessment', 'Reflex & Compliance Assessment')));
    chrome.appendChild(head);
    const subject = cell(lex('ic_subject', 'Subject'), '--');
    const block = cell(lex('ic_assessment_block', 'Block'), '--');
    const share = cell(lex('ic_nogo_share', 'NO-GO share'), '--');
    chrome.appendChild(subject.node);
    chrome.appendChild(block.node);
    chrome.appendChild(share.node);
    const warn = el('span', 'g-ic-warn', lex('ic_warn_armed', 'INTERFERENCE ARMED'));
    chrome.appendChild(warn);
    wrap.appendChild(chrome);

    /* arena - the whole hall is the tap surface */
    const arena = el('div', 'g-ic-arena');
    arena.setAttribute('role', 'button');
    arena.setAttribute('tabindex', '0');
    // Set dressing (pointer-events:none in CSS): the door you came in through.
    arena.appendChild(el('span', 'g-ic-door'));
    const aperture = el('div', 'g-ic-aperture');
    const ring = el('span', 'g-ic-ring');
    const ash = el('span', 'g-ic-ash');
    const stim = el('span', 'g-ic-stim');
    aperture.appendChild(ring);
    aperture.appendChild(ash);
    aperture.appendChild(stim);
    arena.appendChild(aperture);
    const rt = el('span', 'g-ic-rt');
    arena.appendChild(rt);
    const edge = el('span', 'g-ic-edge');
    arena.appendChild(edge);
    wrap.appendChild(arena);

    /* toast + log + footline + streak meter */
    const toast = el('div', 'g-ic-toast');
    wrap.appendChild(toast);
    const lielog = el('div', 'g-ic-lielog');
    wrap.appendChild(lielog);
    const meterHost = el('div', 'g-ic-meter');
    wrap.appendChild(meterHost);
    const base = el('div', 'g-ic-base');
    const fMedian = cell(lex('ic_session_median', 'session median'), '--');
    const fRecord = cell(lex('ic_personal_record', 'personal record'), '--');
    const fRestraint = cell(lex('ic_restraint', 'restraint'), '--');
    const fSplit = cell(lex('ic_induced', 'induced'), '0');
    base.appendChild(fMedian.node);
    base.appendChild(fRecord.node);
    base.appendChild(fRestraint.node);
    base.appendChild(fSplit.node);
    wrap.appendChild(base);

    root.appendChild(wrap);

    Object.assign(n, {
      wrap, chrome, warn, arena, aperture, ring, ash, stim, rt, edge, toast, lielog,
      meterHost, base,
      subject: subject.value, block: block.value, share: share.value,
      fMedian: fMedian.value, fRecord: fRecord.value, fRestraint: fRestraint.value, fSplit: fSplit.value,
      breakCard: null,
    });
    return n;
  }

  /* --------------------------------------------------------------- chrome */
  function setChrome(o) {
    const p = o || {};
    if (p.subject != null && n.subject) n.subject.textContent = String(p.subject);
    if (p.block != null && n.block) n.block.textContent = String(p.block);
    if (p.nogoPct != null && n.share) n.share.textContent = Math.round(p.nogoPct) + '%';
  }
  /** The tier-2 taste-of-the-twist is TELEGRAPHED: the player is warned first -
   *  and the whole room grows uneasy (a slow light pulse) while the warning is up. */
  function telegraph(on) {
    if (!n.warn) return;
    if (on) n.warn.classList.add('on'); else n.warn.classList.remove('on');
    if (n.wrap) {
      if (on) n.wrap.classList.add('room-armed');
      else n.wrap.classList.remove('room-armed');
    }
  }

  /* ------------------------------------------------------------- stimulus */
  let mediaNode = null;

/** The provider serves mp4 on iOS (mp4-only filter), so a media stimulus is not
 *  always an image. An <img> pointed at an mp4 paints NOTHING, and a blank
 *  aperture is an unscoreable trial. */
const VIDEO_RE = /\.(mp4|m4v|webm|mov)(\?|#|$)/i;

  function paint(dressed) {
    if (!n.stim) return;
    n.stim.textContent = '';
    mediaNode = null;
    if (dressed && dressed.render === 'media' && dressed.url) {
      const isVideo = VIDEO_RE.test(String(dressed.url));
      const node = el(isVideo ? 'video' : 'img');
      node.setAttribute('src', dressed.url);
      if (isVideo) {
        // muted + inline + loop: a stimulus must never ask for a gesture, and
        // autoplay policy only lets a muted video start on its own.
        node.setAttribute('muted', '');
        node.setAttribute('loop', '');
        node.setAttribute('playsinline', '');
        node.setAttribute('autoplay', '');
        node.muted = true;
        try { if (typeof node.play === 'function') { const p = node.play(); if (p && p.catch) p.catch(() => {}); } }
        catch (e) { /* a stimulus that will not play still paints its frame */ }
      } else {
        node.setAttribute('alt', '');
      }
      // A CSS-filter twin: no canvas read ever touches this node, which is what
      // makes a CORS-tainted remote loop legal as a stimulus.
      if (dressed.twinCls) node.className = dressed.twinCls;
      n.stim.appendChild(node);
      mediaNode = node;
    } else if (dressed) {
      n.stim.textContent = String(dressed.text == null ? '' : dressed.text);
    }
  }

  function showStimulus(dressed, cls) {
    paint(dressed);
    n.stim.classList.remove('nogo');
    if (cls === 'nogo') n.stim.classList.add('nogo');
    n.stim.classList.add('on');
    if (n.aperture) n.aperture.classList.add('hot');
  }

  /** The commitment trap: re-paint the SAME node as its twin, mid-presentation. */
  function swapStimulus(dressed) {
    paint(dressed);
    n.stim.classList.add('nogo');
  }

  function hideStimulus() {
    if (!n.stim) return;
    n.stim.classList.remove('on');
    if (n.aperture) n.aperture.classList.remove('hot');
  }

  /* ------------------------------------------------------------- feedback */
  /** THE ROOM REACTS: a class on the stage wrap animates the spotlight layer
   *  (style.js .room-*). Dresses existing beats only - never a timing change,
   *  and reduced motion collapses every one of these to a cut in CSS. */
  function roomBeat(kind, ms) {
    if (n.wrap) flash(n.wrap, 'room-' + kind, ms);
  }

  /** Correct GO: LOUD. Ring punch, gold ms readout, the light brightens. */
  function hit(o) {
    const p = o || {};
    flash(n.ring, 'go', soft ? 260 : 640);
    roomBeat('go', soft ? 260 : 640);
    if (wantRt && n.rt) {
      n.rt.textContent = (p.rtMs == null ? '--' : Math.round(p.rtMs)) + ' ms';
      n.rt.classList.remove('slow', 'best');
      if (p.best) n.rt.classList.add('best');
      else if (!p.underBaseline) n.rt.classList.add('slow');
      flash(n.rt, 'on', soft ? 420 : 980);
    }
    if (p.edge) flash(n.edge, 'on', 480);
  }

  /** Correct withhold: SERENE. The decoy dissolves to ash, the light dims
   *  approvingly - restraint is soothed at room scale. */
  function withhold() {
    flash(n.ash, 'on', soft ? 320 : 720);
    roomBeat('calm', soft ? 400 : 1200);
  }

  /** An error: no shouting from the UI - but the spotlight snaps. The toast
   *  does the attributing. */
  function errorMark() {
    if (n.aperture) flash(n.aperture, 'tight', 420);
    roomBeat('snap', 460);
  }

  /** The clean-chain notch: the aperture ring tightens as the streak climbs. */
  function tighten(on) {
    if (!n.aperture) return;
    if (on) n.aperture.classList.add('tight'); else n.aperture.classList.remove('tight');
  }

  function streak(filled) {
    if (!cer || typeof cer.streakMeter !== 'function' || !n.meterHost) return null;
    try { return cer.streakMeter({ target: n.meterHost, filled, gold: filled >= 10 }); }
    catch (e) { say('streak meter: ' + (e && e.message)); return null; }
  }

  function stamp(text, tone) {
    if (!cer || typeof cer.stamp !== 'function') return null;
    try { return cer.stamp({ text, tone, target: n.arena }); }
    catch (e) { say('stamp: ' + (e && e.message)); return null; }
  }

  function reward(kind, opts) {
    if (!cer || typeof cer.reward !== 'function') return false;
    try { return cer.reward(kind, Object.assign({ target: n.arena }, opts || {})); }
    catch (e) { say('reward: ' + (e && e.message)); return false; }
  }

  /* ------------------------------------------------------------ the toast */
  let toastTimer = 0;
  /**
   * The mid-class attribution toast. `title` is the lie's headline (bold), `body`
   * the clinical sentence. Induced reads pink (the machine working); clean reads
   * lavender (yours to fix). Never shaming, always itemised.
   */
  function toast(title, body, clean) {
    if (!n.toast) return;
    n.toast.textContent = '';
    if (title) n.toast.appendChild(el('b', null, title + ' '));
    if (body) n.toast.appendChild(document.createTextNode(body));
    if (clean) n.toast.classList.add('clean'); else n.toast.classList.remove('clean');
    n.toast.classList.add('on');
    // An induced attribution tints the whole hall pink - the machine working.
    // Clean errors leave the room neutral: that one's yours, not the room's.
    if (!clean) roomBeat('lie', soft ? 400 : 950);
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = later(() => { if (n.toast) n.toast.classList.remove('on'); }, 4200);
  }

  /* ------------------------------------------- the live interference strip */
  function logMark(pct, kind) {
    if (!n.lielog) return null;
    const i = el('i', kind === 'induced' ? 'err' : kind === 'clean' ? 'clean-err' : '');
    i.style.setProperty('left', Math.max(0, Math.min(100, pct)) + '%');
    n.lielog.appendChild(i);
    return i;
  }

  function footline(o) {
    const p = o || {};
    if (n.fMedian) n.fMedian.textContent = p.medianRt == null ? '--' : Math.round(p.medianRt) + ' ms';
    if (n.fRecord) n.fRecord.textContent = p.record == null ? '--' : Math.round(p.record) + ' ms';
    if (n.fRestraint) n.fRestraint.textContent = p.restraintPct == null ? '--' : Math.round(p.restraintPct) + '%';
    if (n.fSplit) n.fSplit.textContent = String(p.induced || 0) + ' / ' + String(p.clean || 0);
  }

  /* -------------------------------------------------------- break / cards */
  function breakCard(o) {
    const p = o || {};
    clearBreak();
    const card = el('div', 'g-ic-break');
    if (p.title) card.appendChild(el('h3', null, p.title));
    if (p.note) card.appendChild(el('p', null, p.note));
    if (p.stampline) card.appendChild(el('p', 'g-ic-stampline', p.stampline));
    if (n.arena) n.arena.appendChild(card);
    n.breakCard = card;
    return card;
  }
  function clearBreak() {
    if (n.breakCard) { try { n.breakCard.remove(); } catch (e) { /* noop */ } n.breakCard = null; }
  }

  /* -------------------------------------------------------------- debrief */
  /**
   * The report the class exists to produce. Renders BEFORE endClass on purpose:
   * ctx.endClass tears the class DOM down immediately (the shell owns the screen
   * after that), so the interference log has to be readable first. `onSubmit`
   * wires the button that actually ends the class.
   *
   * @param {Object} p {subject, medianRt, baselineMs, established, restraintPct,
   *                    induced, clean, gate, slipLine, offPct, events, errors,
   *                    durationMs, tier, buzzerLied}
   */
  function debrief(p, onSubmit, onRecalibrate) {
    const d = p || {};
    root.textContent = '';
    const wrap = el('div', 'g-ic g-ic-debrief');
    wrap.appendChild(el('h3', null, lex('ic_debrief', 'Debrief')));
    wrap.appendChild(el('p', 'g-ic-sub',
      lex('ic_subject', 'Subject') + ' #' + (d.subject || '0000')
      + '   ' + lex('ic_assessment', 'Reflex & Compliance Assessment')));

    /* the four cells */
    const grid = el('div', 'g-ic-grid');
    const putCell = (label, value, tone) => {
      const c = el('div', 'g-ic-cell');
      c.appendChild(el('span', null, label));
      c.appendChild(el('b', tone || null, value));
      grid.appendChild(c);
    };
    putCell(lex('ic_session_median', 'session median'),
      d.medianRt == null ? '--' : Math.round(d.medianRt) + ' ms',
      d.medianRt != null && d.baselineMs && d.medianRt <= d.baselineMs ? 'gold' : null);
    putCell(lex('ic_baseline', 'baseline'), d.baselineMs ? Math.round(d.baselineMs) + ' ms' : '--');
    putCell(lex('ic_restraint', 'restraint'), (d.restraintPct == null ? '--' : Math.round(d.restraintPct) + '%'),
      d.restraintPct >= 98 ? 'gold' : null);
    putCell(lex('ic_induced', 'induced') + ' / ' + lex('ic_clean', 'clean'),
      String(d.induced || 0) + ' / ' + String(d.clean || 0),
      (d.induced || 0) > 0 ? 'pink' : null);
    wrap.appendChild(grid);

    /* the timeline: lie markers on top, errors underneath - the share hook */
    wrap.appendChild(el('p', 'g-ic-sub', lex('ic_interference_log', 'Interference log')));
    const tl = el('div', 'g-ic-timeline');
    const span = Math.max(1, Number(d.durationMs) || 1);
    for (const ev of (d.events || [])) {
      const i = el('i');
      i.style.setProperty('left', Math.max(0, Math.min(99, ((ev.atMs - (d.startedAt || 0)) / span) * 100)) + '%');
      i.setAttribute('title', ev.label || ev.kind);
      tl.appendChild(i);
    }
    for (const err of (d.errors || [])) {
      const i = el('i', err.induced ? 'err' : 'clean-err');
      i.style.setProperty('left', Math.max(0, Math.min(99, ((err.atMs - (d.startedAt || 0)) / span) * 100)) + '%');
      tl.appendChild(i);
    }
    wrap.appendChild(tl);
    wrap.appendChild(el('p', 'g-ic-legend', lex('ic_legend', 'top row: interference events   bottom row: your errors')));

    /* the attributed lines - the actual product */
    const lines = el('ul', 'g-ic-lines');
    const pushLine = (induced, headline, body) => {
      const li = el('li', induced ? 'induced' : null);
      li.appendChild(el('b', null, headline + ' '));
      li.appendChild(document.createTextNode(body));
      lines.appendChild(li);
    };
    if (d.buzzerLied) {
      // DECISIONS #7: ALWAYS attributed, whether or not it caused an error.
      pushLine(true, lex('ic_debrief_buzzer_lied', 'That buzzer lied.'),
        lex('ic_debrief_buzzer_body', 'A clean GO was answered with the error buzzer.'));
    }
    for (const err of (d.errors || [])) {
      const at = ((err.atMs - (d.startedAt || 0)) / 1000).toFixed(1) + 's';
      if (err.induced) {
        pushLine(true, lex('ic_err_' + err.kind, err.kind) + ' at ' + at + ' -',
          (err.lieLabel ? err.lieLabel + ' fired ' + Math.max(0, Math.round(err.lieLagMs)) + 'ms prior. ' : '')
          + lex('ic_debrief_induced_line', 'You heard it, and you obeyed.'));
      } else {
        pushLine(false, lex('ic_err_' + err.kind, err.kind) + ' at ' + at + ' -',
          lex('ic_debrief_clean_line', "No interference was active. That one's yours."));
      }
    }
    if (!lines.children.length) {
      pushLine(false, lex('ic_debrief_no_errors', 'No errors. Nothing to attribute.'),
        (d.events || []).length ? '' : lex('ic_debrief_no_lies', 'No interference was active this round.'));
    }
    wrap.appendChild(lines);

    /* the comeback hook + the actions */
    const slip = el('p', 'g-ic-slip', d.slipLine || '');
    wrap.appendChild(slip);
    if (d.established) wrap.appendChild(el('p', 'g-ic-slip', lex('ic_baseline_new', 'Baseline established.')));

    const actions = el('div', 'g-ic-actions');
    const submit = el('button', 'btn primary', lex('ic_submit', 'Submit report'));
    submit.type = 'button';
    submit.addEventListener('click', () => { try { onSubmit(); } catch (e) { say('submit: ' + (e && e.message)); } });
    actions.appendChild(submit);

    /* recalibrate: a 2-click confirm, because it throws the yardstick away */
    const recal = el('button', 'btn ghost', lex('ic_recalibrate', 'Recalibrate baseline'));
    recal.type = 'button';
    let armed = false;
    recal.addEventListener('click', () => {
      if (!armed) {
        armed = true;
        recal.textContent = lex('ic_recalibrate_confirm', 'Tap again to confirm');
        later(() => { if (armed) { armed = false; recal.textContent = lex('ic_recalibrate', 'Recalibrate baseline'); } }, 4000);
        return;
      }
      armed = false;
      recal.disabled = true;
      recal.textContent = lex('ic_recalibrated', 'Baseline cleared.');
      try { onRecalibrate(); } catch (e) { say('recalibrate: ' + (e && e.message)); }
    });
    actions.appendChild(recal);
    wrap.appendChild(actions);
    wrap.appendChild(el('p', 'g-ic-hint', d.hint || ''));

    root.appendChild(wrap);
    n.debriefWrap = wrap;
    n.submit = submit;
    n.recal = recal;
    return { wrap, submit, recal };
  }

  return {
    nodes: n,
    mount, setChrome, telegraph,
    showStimulus, swapStimulus, hideStimulus,
    hit, withhold, errorMark, tighten, streak, stamp, reward,
    toast, logMark, footline, breakCard, clearBreak, debrief,
    get mediaNode() { return mediaNode; },
    destroy() {
      for (const id of Array.from(timers)) clearTimeout(id);
      timers.clear();
      try { root.textContent = ''; } catch (e) { /* noop */ }
    },
  };
}

export default createRender;
