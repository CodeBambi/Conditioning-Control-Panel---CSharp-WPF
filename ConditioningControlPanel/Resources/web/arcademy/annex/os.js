/* ============================================================================
 * THE ANNEX OS. The dated windowed desktop on the lab laptop: login, FILES,
 * REGISTRY, SUBJECT SEARCH, TERMINAL. Carries punches 1, 2 and 3 of the
 * reveal; punch 4 (the mascot dossier) is paper on the desk and lab.js owns it.
 *
 * CHROME ONLY, same law as cams.js: no bridge, no store, no fetch. Everything
 * live arrives through the caps the lab injects (liveFile, fetchStats,
 * subject), everything written comes from ./docs.js, and every consented or
 * derived value was resolved host-side before it got here. The OS never
 * computes a truth, it only displays the ones it was handed.
 *
 * Laws inherited from the build docs (ANNEX-OS.md):
 * - The REGISTRY's LIVE panel renders real counts or LINK DOWN. It never
 *   draws a number from fiction; the written archive notes sit below,
 *   visibly paper, never mixed into the table. (C6: the room never lies on
 *   a live screen.)
 * - Counts under REDACT_UNDER render as black bars, the count withheld.
 * - The TERMINAL's final line renders only when all four punches are open,
 *   and it is the only surface in the Arcademy allowed the glyph it ends on.
 * - The login password is written on the note. The puzzle is noticing, not
 *   guessing; wrong entries get a dry line and never a lockout.
 * - Esc never closes the OS from in here: escapeStep() only folds windows,
 *   and the lab owns the rung that puts the laptop down (shell ladder law:
 *   the modal you opened one press ago closes first).
 * ==========================================================================*/

import { TREE, DOCS, REGISTRY_NOTES, LOG_LINES, FINAL_LINE, SEARCH_DENIED } from './docs.js';

/** The note on the bezel. Case folds, the hyphen is optional, mercy is law. */
const OS_PASSWORD = 'CYBER-PUNK';

/** Registry cells below this render as a bar. Theater threshold, page-side. */
const REDACT_UNDER = 5;

/** App order: desktop icons, taskbar, and the openers all share it. */
const APPS = Object.freeze(['files', 'registry', 'search', 'term']);

export function createAnnexOs(opts) {
  const o = opts || {};
  const t = typeof o.t === 'function' ? o.t : (k, f) => f;
  const subject = o.subject || {};
  const liveFile = typeof o.liveFile === 'function' ? o.liveFile : () => [];
  const fetchStats = typeof o.fetchStats === 'function' ? o.fetchStats : () => Promise.resolve(null);
  const gameName = typeof o.gameName === 'function' ? o.gameName : (k) => k;
  const gamesList = Array.isArray(o.gamesList) ? o.gamesList : [];
  const getPunches = typeof o.getPunches === 'function' ? o.getPunches : () => ({});
  const onPunch = typeof o.onPunch === 'function' ? o.onPunch : () => {};
  const lite = !!o.lite;

  const doc = document;
  let dead = false;
  let statsCache;           /* last fetchStats body, kept for re-opens        */
  const readDocs = {};      /* docId -> true, session-local dimming only      */
  const wins = {};          /* app -> { el, body, barBtn }                    */
  let frontApp = null;
  let clockTimer = null;

  /* Records.js's sfx template, copied not imported (trap 18). */
  function sfx(name, level, extra) {
    try {
      const detail = Object.assign({ name, level: level == null ? 0.5 : level, bus: 'fx' }, extra || {});
      doc.dispatchEvent(new CustomEvent('arcademy-sfx', { detail }));
    } catch (e) { /* sound is decoration */ }
  }

  function el(tag, cls, text) {
    const n = doc.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }

  const root = el('div', 'aos-root' + (lite ? ' aos-lite' : ''));
  root.setAttribute('role', 'region');
  root.setAttribute('aria-label', t('annex_os_label', 'Annex terminal'));

  /* ------------------------------------------------------------------ boot */

  function showBoot() {
    const b = el('div', 'aos-boot');
    const lines = [
      t('annex_os_boot_1', 'RECORDS ANNEX / UNIT TERMINAL'),
      t('annex_os_boot_2', 'memory check: fine, thanks for asking'),
      t('annex_os_boot_3', 'feed wall link: up'),
      t('annex_os_boot_4', 'archive index: 26 files, 5 drawers'),
      '',
    ];
    lines.forEach((s, i) => {
      const ln = el('div', 'aos-boot-line', s);
      ln.style.animationDelay = lite ? '0ms' : (i * 260) + 'ms';
      b.appendChild(ln);
    });
    root.appendChild(b);
    const hold = lite ? 60 : lines.length * 260 + 420;
    later(() => { b.remove(); showLogin(); }, hold);
  }

  /* ----------------------------------------------------------------- login */

  function normPw(s) { return String(s || '').trim().toUpperCase().replace(/[\s-]+/g, ''); }

  function showLogin() {
    const scr = el('div', 'aos-login');
    const box = el('div', 'aos-login-box');
    box.appendChild(el('div', 'aos-login-title', t('annex_os_boot_1', 'RECORDS ANNEX / UNIT TERMINAL')));
    box.appendChild(el('div', 'aos-login-sub', t('annex_os_login_sub', 'authorised staff. there is no other kind of staff.')));

    const field = el('div', 'aos-field');
    const lab = el('label', null, t('annex_os_pass', 'password'));
    lab.setAttribute('for', 'aos-pw');
    const input = el('input', 'aos-input');
    input.id = 'aos-pw';
    input.type = 'password';
    input.autocomplete = 'off';
    field.appendChild(lab);
    field.appendChild(input);
    box.appendChild(field);

    const go = el('button', 'aos-btn', t('annex_os_enter', 'log in'));
    const err = el('div', 'aos-login-err', '');
    box.appendChild(go);
    box.appendChild(err);
    scr.appendChild(box);

    /* the whole puzzle */
    const note = el('div', 'aos-note', t('annex_os_note', 'PW: CYBER-PUNK') + '\n-M');
    note.style.whiteSpace = 'pre-line';
    scr.appendChild(note);

    function attempt() {
      if (normPw(input.value) === normPw(OS_PASSWORD)) {
        sfx('commit', 0.4);
        scr.remove();
        try { o.onUnlock && o.onUnlock(); } catch (e) { /* noop */ }
        showDesktop();
      } else {
        sfx('bump', 0.2);
        err.textContent = t('annex_os_wrong', 'no. the note is right there.');
        input.value = '';
        input.focus();
      }
    }
    go.addEventListener('click', attempt);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') attempt(); });

    root.appendChild(scr);
    later(() => { try { input.focus(); } catch (e) { /* noop */ } }, 40);
  }

  /* --------------------------------------------------------------- desktop */

  let desk = null;
  let bar = null;

  function showDesktop() {
    desk = el('div', 'aos-desk');
    const icons = el('div', 'aos-icons');
    APPS.forEach((app) => {
      const b = el('button', 'aos-icon');
      b.dataset.app = app;
      const g = el('span', 'aos-icon-glyph');
      g.setAttribute('aria-hidden', 'true');
      b.appendChild(g);
      b.appendChild(el('span', null, appTitle(app)));
      b.addEventListener('click', () => { sfx('blip', 0.2, { pitch: 1.05 }); openApp(app); });
      icons.appendChild(b);
    });
    desk.appendChild(icons);
    root.appendChild(desk);

    bar = el('div', 'aos-bar');
    const clock = el('span', 'aos-clock');
    function tickClock() {
      const d = new Date();
      clock.textContent = pad2(d.getHours()) + ':' + pad2(d.getMinutes());
    }
    tickClock();
    clockTimer = setInterval(tickClock, 30000);
    bar.appendChild(clock);
    root.appendChild(bar);

    fit();
  }

  function appTitle(app) {
    if (app === 'files') return t('annex_os_files', 'FILES');
    if (app === 'registry') return t('annex_os_registry', 'REGISTRY');
    if (app === 'search') return t('annex_os_search', 'SUBJECT SEARCH');
    return t('annex_os_term', 'TERMINAL');
  }

  function fit() {
    try { root.classList.toggle('aos-cramped', root.clientWidth < 560); }
    catch (e) { /* noop */ }
  }

  /* --------------------------------------------------------------- windows */

  const SPAWN = Object.freeze({
    files: [110, 26, 470, 300],
    registry: [150, 60, 430, 300],
    search: [180, 44, 400, 310],
    term: [130, 90, 430, 250],
  });

  function openApp(app) {
    if (dead || !desk) return;
    let w = wins[app];
    if (!w) {
      w = buildWindow(app);
      wins[app] = w;
      desk.appendChild(w.el);
      addBarBtn(app, w);
      renderApp(app, w);
    }
    w.el.hidden = false;
    bringToFront(app);
    if (app === 'files') onPunch('p1obs'); /* observed, not read; the read stamps p1 */
    if (app === 'registry') onPunch('p2');
    if (app === 'term') renderTerm(w); /* the log re-reads the punches each open */
  }

  function buildWindow(app) {
    const [x, y, wpx, hpx] = SPAWN[app];
    const win = el('div', 'aos-win');
    win.style.left = x + 'px';
    win.style.top = y + 'px';
    win.style.width = wpx + 'px';
    win.style.height = hpx + 'px';

    const title = el('div', 'aos-title', appTitle(app));
    const x2 = el('button', 'aos-title-x', '×');
    x2.setAttribute('aria-label', t('annex_os_close', 'close'));
    x2.addEventListener('click', (e) => { e.stopPropagation(); closeApp(app); });
    title.appendChild(x2);
    win.appendChild(title);

    const body = el('div', 'aos-body');
    win.appendChild(body);

    win.addEventListener('pointerdown', () => bringToFront(app));
    wireDrag(win, title);
    return { el: win, body, barBtn: null };
  }

  function addBarBtn(app, w) {
    const b = el('button', 'aos-bar-btn is-open', appTitle(app));
    b.addEventListener('click', () => {
      if (w.el.hidden) { w.el.hidden = false; bringToFront(app); }
      else if (frontApp === app) { w.el.hidden = true; pickNewFront(); }
      else bringToFront(app);
    });
    bar.insertBefore(b, bar.lastChild); /* clock stays right */
    w.barBtn = b;
  }

  function closeApp(app) {
    const w = wins[app];
    if (!w) return;
    sfx('blip', 0.14, { pitch: 0.92 });
    w.el.remove();
    if (w.barBtn) w.barBtn.remove();
    delete wins[app];
    if (frontApp === app) pickNewFront();
  }

  function bringToFront(app) {
    frontApp = app;
    Object.keys(wins).forEach((k) => {
      wins[k].el.classList.toggle('is-front', k === app);
      if (wins[k].barBtn) wins[k].barBtn.classList.toggle('is-open', k === app && !wins[k].el.hidden);
    });
  }

  function pickNewFront() {
    const open = Object.keys(wins).filter((k) => !wins[k].el.hidden);
    frontApp = open.length ? open[open.length - 1] : null;
    if (frontApp) bringToFront(frontApp);
  }

  function wireDrag(win, handle) {
    let sx = 0, sy = 0, ox = 0, oy = 0, drag = false;
    handle.addEventListener('pointerdown', (e) => {
      if (e.target && e.target.classList && e.target.classList.contains('aos-title-x')) return;
      if (root.classList.contains('aos-cramped')) return;
      drag = true;
      sx = e.clientX; sy = e.clientY;
      ox = win.offsetLeft; oy = win.offsetTop;
      try { handle.setPointerCapture(e.pointerId); } catch (err) { /* noop */ }
    });
    handle.addEventListener('pointermove', (e) => {
      if (!drag) return;
      const nx = Math.max(-40, Math.min((desk ? desk.clientWidth : 800) - 60, ox + e.clientX - sx));
      const ny = Math.max(0, Math.min((desk ? desk.clientHeight : 500) - 30, oy + e.clientY - sy));
      win.style.left = nx + 'px';
      win.style.top = ny + 'px';
    });
    const drop = () => { drag = false; };
    handle.addEventListener('pointerup', drop);
    handle.addEventListener('pointercancel', drop);
  }

  /* ------------------------------------------------------------------ apps */

  function renderApp(app, w) {
    if (app === 'files') renderFiles(w);
    else if (app === 'registry') renderRegistry(w);
    else if (app === 'search') renderSearch(w);
    else renderTerm(w);
  }

  /* FILES: tree left, docs right, viewer replaces the list (back row on top). */
  function renderFiles(w) {
    w.body.textContent = '';
    const wrap = el('div', 'aos-files');
    const tree = el('div', 'aos-tree');
    const list = el('div', 'aos-doclist');
    let sel = TREE[0];

    function paintTree() {
      tree.textContent = '';
      TREE.forEach((folder) => {
        const b = el('button', 'aos-folder' + (folder === sel ? ' is-sel' : ''), folder.label);
        b.addEventListener('click', () => { sel = folder; sfx('blip', 0.14, { pitch: 1.05 }); paintTree(); paintList(); });
        tree.appendChild(b);
      });
    }
    function paintList() {
      list.textContent = '';
      sel.docs.forEach((id) => {
        const d = DOCS[id];
        if (!d) return;
        const b = el('button', 'aos-docrow' + (readDocs[id] ? ' is-read' : ''), d.name);
        b.addEventListener('click', () => openDoc(id));
        list.appendChild(b);
      });
    }
    function openDoc(id) {
      const d = DOCS[id];
      if (!d) return;
      sfx('paper', 0.25);
      readDocs[id] = true;
      onPunch('p1');
      list.textContent = '';
      const back = el('button', 'aos-folder', '‹ ' + sel.label);
      back.addEventListener('click', () => { sfx('paper', 0.18); paintList(); });
      list.appendChild(back);
      list.appendChild(el('div', 'aos-doc-head', d.name + '\n' + d.head));
      list.appendChild(el('div', 'aos-doc-body', d.body));
    }

    paintTree();
    paintList();
    wrap.appendChild(tree);
    wrap.appendChild(list);
    w.body.appendChild(wrap);
  }

  /* REGISTRY: the LIVE table (real or LINK DOWN, never fiction), then paper. */
  function renderRegistry(w) {
    w.body.textContent = '';
    const live = el('div');
    live.appendChild(el('div', 'aos-reg-h', t('annex_os_live', 'LIVE')));
    const slot = el('div');
    live.appendChild(slot);
    w.body.appendChild(live);

    const arch = el('div');
    arch.appendChild(el('div', 'aos-reg-h', t('annex_os_archive', 'ARCHIVE')));
    REGISTRY_NOTES.forEach((n) => arch.appendChild(el('p', 'aos-reg-note', n)));
    w.body.appendChild(arch);

    function cell(n) {
      if (typeof n !== 'number' || !isFinite(n)) n = 0;
      if (n < REDACT_UNDER) {
        const s = el('span', 'aos-redact');
        s.setAttribute('role', 'img');
        s.setAttribute('aria-label', t('annex_os_redacted', 'withheld'));
        return s;
      }
      return doc.createTextNode(String(n));
    }

    function paint(body) {
      slot.textContent = '';
      if (!body || !body.games) {
        const down = el('div', 'aos-reg-down', t('annex_os_linkdown', 'LINK DOWN'));
        const retry = el('button', 'aos-btn', t('annex_os_retry', 'retry'));
        retry.style.marginLeft = '12px';
        retry.addEventListener('click', () => { statsCache = undefined; load(); });
        down.appendChild(retry);
        slot.appendChild(down);
        return;
      }
      const tbl = el('table', 'aos-reg-table');
      const hr = el('tr');
      [t('annex_os_room', 'room'), t('annex_os_enrolled', 'enrolled'), t('annex_os_completed', 'completed')]
        .forEach((h) => hr.appendChild(el('th', null, h)));
      tbl.appendChild(hr);
      const keys = gamesList.length ? gamesList : Object.keys(body.games);
      keys.forEach((k) => {
        const g = body.games[k];
        if (!g) return;
        const tr = el('tr');
        tr.appendChild(el('td', null, gameName(k)));
        const td1 = el('td'); td1.appendChild(cell(g.enrolled)); tr.appendChild(td1);
        const td2 = el('td'); td2.appendChild(cell(g.complete)); tr.appendChild(td2);
        tbl.appendChild(tr);
      });
      if (body.totals) {
        const tr = el('tr', 'aos-reg-total');
        tr.appendChild(el('td', null, t('annex_os_all', 'all subjects')));
        const td1 = el('td'); td1.appendChild(cell(body.totals.enrolled)); tr.appendChild(td1);
        const td2 = el('td'); td2.appendChild(cell(body.totals.complete)); tr.appendChild(td2);
        tbl.appendChild(tr);
      }
      slot.appendChild(tbl);
    }

    function load() {
      if (statsCache !== undefined) { paint(statsCache); return; }
      slot.textContent = '';
      slot.appendChild(el('div', 'aos-reg-wait', t('annex_os_linkwait', 'link…')));
      Promise.resolve()
        .then(() => fetchStats())
        .then((body) => { if (dead) return; statsCache = body || null; paint(statsCache); })
        .catch(() => { if (dead) return; statsCache = null; paint(null); });
    }
    load();
  }

  /* SUBJECT SEARCH: the code and password from the paper open the live file. */
  function renderSearch(w) {
    w.body.textContent = '';
    const form = el('div', 'aos-search-form');
    const f1 = el('div', 'aos-field');
    f1.appendChild(el('label', null, t('annex_os_code', 'subject code')));
    const code = el('input', 'aos-input');
    code.autocomplete = 'off';
    f1.appendChild(code);
    const f2 = el('div', 'aos-field');
    f2.appendChild(el('label', null, t('annex_os_pass', 'password')));
    const pw = el('input', 'aos-input');
    pw.type = 'password';
    pw.autocomplete = 'off';
    f2.appendChild(pw);
    const go = el('button', 'aos-btn', t('annex_os_open_file', 'open file'));
    const err = el('div', 'aos-login-err', '');
    form.appendChild(f1); form.appendChild(f2); form.appendChild(go); form.appendChild(err);
    w.body.appendChild(form);

    function norm(s) { return String(s || '').trim().toUpperCase().replace(/[^A-Z0-9]/g, ''); }
    function attempt() {
      const okCode = subject.code && norm(code.value) === norm(subject.code);
      const okPw = subject.password && normPw(pw.value) === normPw(subject.password);
      if (okCode && okPw) { sfx('commit', 0.4); onPunch('p3'); renderFile(w); }
      else {
        sfx('bump', 0.2);
        err.textContent = t('annex_os_notfound', SEARCH_DENIED);
      }
    }
    go.addEventListener('click', attempt);
    pw.addEventListener('keydown', (e) => { if (e.key === 'Enter') attempt(); });
  }

  /* The live file. Sections come from the lab's cap, freshly computed, so the
   * numbers are current at open. The paper upstairs was old. This is not. */
  function renderFile(w) {
    w.body.textContent = '';
    w.body.appendChild(el('div', 'aos-doc-head',
      t('annex_os_file_title', 'SUBJECT FILE') + '  ' + (subject.code || '')));
    let sections = [];
    try { sections = liveFile() || []; } catch (e) { sections = []; }
    sections.forEach((sec) => {
      const box = el('div', 'aos-file-sec');
      box.appendChild(el('h4', null, sec.title));
      const dl = el('dl');
      (sec.rows || []).forEach((r) => {
        const row = el('div', 'aos-file-row');
        row.appendChild(el('dt', null, r[0]));
        row.appendChild(el('dd', null, String(r[1])));
        dl.appendChild(row);
      });
      box.appendChild(dl);
      w.body.appendChild(box);
    });
    w.body.appendChild(el('span', 'aos-file-stamp', t('annex_os_ongoing', 'ONGOING')));
  }

  /* TERMINAL: the log. Re-rendered on every open so the final line can land. */
  function renderTerm(w) {
    w.body.textContent = '';
    const now = new Date();
    const t1 = new Date(now.getTime() - 63 * 60000);
    const t2 = new Date(now.getTime() - 7 * 60000);
    const stamp = (d) => pad2(d.getHours()) + ':' + pad2(d.getMinutes());
    const box = el('div', 'aos-term');
    LOG_LINES.forEach((ln) => {
      box.appendChild(el('div', null,
        ln.replace('{t1}', stamp(t1)).replace('{t2}', stamp(t2))));
    });
    const p = getPunches() || {};
    if (p.p1 && p.p2 && p.p3 && p.p4) {
      box.appendChild(el('div', 'aos-term-final', FINAL_LINE));
    }
    w.body.appendChild(box);
  }

  /* ----------------------------------------------------------------- misc */

  const timers = [];
  function later(fn, ms) {
    const id = setTimeout(() => { if (!dead) fn(); }, ms);
    timers.push(id);
    return id;
  }
  function pad2(n) { return (n < 10 ? '0' : '') + n; }

  /** One rung: fold the front window. The lab decides when the laptop drops. */
  function escapeStep() {
    if (frontApp && wins[frontApp] && !wins[frontApp].el.hidden) {
      closeApp(frontApp);
      return true;
    }
    return false;
  }

  function destroy() {
    if (dead) return;
    dead = true;
    timers.forEach((id) => clearTimeout(id));
    if (clockTimer) clearInterval(clockTimer);
    try { root.remove(); } catch (e) { /* noop */ }
  }

  /* boot: revisits skip the theater, the room already earned it once */
  if (o.osUnlocked) showDesktop();
  else showBoot();

  return { root, escapeStep, destroy, fit };
}

export default createAnnexOs;
