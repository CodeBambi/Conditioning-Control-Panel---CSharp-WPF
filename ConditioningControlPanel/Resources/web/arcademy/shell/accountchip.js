/* ============================================================================
 * shell/accountchip.js - THE ACCOUNT CHIP: the round photo miniature at the
 * far right of the top bar (and of the campus's top-right cluster), and the
 * small front-desk menu it opens.
 *
 * A HOST SLOT, NOT A FEATURE OF THE SCHOOL. The desktop host never sends
 * `init.account`, so the desktop is byte-for-byte unchanged: `createAccountChip`
 * returns null and nothing is mounted. The web host (cclabs-web's shim) fills
 * the slot because a browser tab has a login to control and a profile page to
 * reach, exactly the way the main web app's account bubble does.
 *
 *   init.account = { name, avatarUrl, actions }      (additive, legally absent)
 *   profile frame may carry `account` with the same shape (a late fetch)
 *   page -> host  { type:'account-action', action:'profile'|'signout' }
 *
 * THREE RULES
 *  1. THE PAGE HOLDS NO URL. The host lists the ACTIONS it can honour and the
 *     page posts the verb; where a verb leads is the host's business. Nothing
 *     here can navigate, so nothing here can leak where a login lives.
 *  2. THE PHOTO IS `avatarUrl` OR A MONOGRAM. The url is a same-origin path the
 *     host chose (the snowflake rule, PRESENCE.md §10). `<img>` onerror falls
 *     back to the drawn monogram and never retries.
 *  3. "OPEN MY CARD" IS PAGE-LOCAL. It is the same door the furniture card is -
 *     `onOpenCard` is the shell's `showIdCard` - so on a phone, where the card
 *     is a small tag, the full ID is still one tap from the bar.
 *
 * No Esc rung (trap 59's shape): Escape closes an open menu on its way past
 * and the ladder never notices. The two document listeners are passive.
 * ==========================================================================*/

const ACTIONS = Object.freeze(['profile', 'signout']);

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/** A same-origin, single-slash path. Anything else is dropped (rule 2). */
function samePath(v) {
  if (typeof v !== 'string') return null;
  const s = v.trim();
  if (s.length < 2 || s.length > 512) return null;
  if (s.charAt(0) !== '/' || s.charAt(1) === '/' || s.charAt(1) === '\\') return null;
  if (/[\s<>"']/.test(s)) return null;
  return s;
}

/**
 * Clamp whatever the host sent into the ONE shape the chip paints from, or
 * null when there is nothing worth a chip (no object, or no action at all -
 * a chip that can only be looked at is furniture the desktop already has).
 * @param {*} raw
 * @returns {{name:string|null, avatarUrl:string|null, actions:string[]}|null}
 */
export function readAccount(raw) {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const actions = Array.isArray(raw.actions)
    ? raw.actions.filter((a) => ACTIONS.indexOf(a) >= 0)
    : [];
  if (!actions.length) return null;
  const name = (typeof raw.name === 'string' && raw.name.trim())
    ? raw.name.trim().slice(0, 40) : null;
  return { name, avatarUrl: samePath(raw.avatarUrl), actions };
}

/** The one letter the monogram wears. */
export function monogram(name, fallback) {
  const src = (typeof name === 'string' && name.trim()) ? name.trim() : String(fallback || '?');
  try { return String.fromCodePoint(src.codePointAt(0)).toUpperCase(); }
  catch (e) { return src.charAt(0).toUpperCase(); }
}

/**
 * @param {Object} o
 * @param {Function} o.t                lexicon
 * @param {Object|null} o.account       readAccount()'s shape (null -> no chip)
 * @param {Function=} o.isMobile        core/device.js's isMobile
 * @param {Function=} o.onOpenCard      the shell's showIdCard
 * @param {Function=} o.onAction        (action) -> the shell posts it
 * @param {Function=} o.log
 * @returns {{el, setAccount, close, destroy}|null}
 */
export function createAccountChip({ t, account, isMobile, onOpenCard, onAction, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const tt = typeof t === 'function' ? t : (k, f) => f;
  let acct = readAccount(account);
  if (!acct) return null;
  const mobile = typeof isMobile === 'function' ? isMobile : () => false;

  const root = el('div', 'arc-acct');
  const btn = el('button', 'arc-acct-btn');
  btn.type = 'button';
  btn.setAttribute('aria-haspopup', 'menu');
  btn.setAttribute('aria-expanded', 'false');
  const img = el('img', 'arc-acct-img');
  img.alt = '';
  img.decoding = 'async';
  img.referrerPolicy = 'no-referrer';
  const mono = el('span', 'arc-acct-mono');
  mono.setAttribute('aria-hidden', 'true');
  btn.appendChild(img);
  btn.appendChild(mono);
  root.appendChild(btn);

  const menu = el('div', 'arc-acct-menu');
  menu.setAttribute('role', 'menu');
  menu.hidden = true;
  const who = el('div', 'arc-acct-who');
  const whoCap = el('span', 'arc-acct-cap');
  const whoName = el('b', 'arc-acct-name');
  who.appendChild(whoCap);
  who.appendChild(whoName);
  menu.appendChild(who);
  root.appendChild(menu);

  let open = false;
  let destroyed = false;
  const items = [];

  function item(cls, label, fn) {
    const b = el('button', 'arc-acct-item ' + cls, label);
    b.type = 'button';
    b.setAttribute('role', 'menuitem');
    b.tabIndex = -1;
    b.addEventListener('click', () => { close(true); try { fn(); } catch (e) { say('account item threw: ' + ((e && e.message) || e)); } });
    menu.appendChild(b);
    items.push(b);
    return b;
  }

  function buildItems() {
    for (const b of items.splice(0)) { try { b.remove(); } catch (e) { /* noop */ } }
    if (typeof onOpenCard === 'function') {
      item('is-card', tt('account_open_card', 'Open my card'), () => onOpenCard());
    }
    if (acct.actions.indexOf('profile') >= 0) {
      item('is-profile', tt('account_profile', 'Profile'), () => { if (onAction) onAction('profile'); });
    }
    if (acct.actions.indexOf('signout') >= 0) {
      item('is-signout', tt('account_sign_out', 'Sign out'), () => { if (onAction) onAction('signout'); });
    }
  }

  function paint() {
    const name = acct.name || tt('student', 'Student');
    const label = tt('account_menu', 'Account') + ': ' + name;
    btn.setAttribute('aria-label', label);
    btn.setAttribute('title', label);
    mono.textContent = monogram(acct.name, name);
    whoCap.textContent = tt('account_signed_in_as', 'Signed in as');
    whoName.textContent = name;
    root.classList.toggle('has-photo', !!acct.avatarUrl);
    if (acct.avatarUrl) {
      if (img.getAttribute('src') !== acct.avatarUrl) img.setAttribute('src', acct.avatarUrl);
    } else {
      img.removeAttribute('src');
    }
    buildItems();
  }

  img.addEventListener('error', () => {
    // Rule 2: the monogram, and no retry. The url stays what the host said.
    root.classList.remove('has-photo');
  });
  img.addEventListener('load', () => { root.classList.add('has-photo'); });

  /* On a phone the menu is a sheet under the bar, full width: measured at
   * open so it sits under whichever bar the chip lives in (topbar or campus
   * cluster) and never over the campus's own controls. */
  function place() {
    if (!mobile()) { menu.style.top = ''; return; }
    try {
      const r = btn.getBoundingClientRect();
      menu.style.top = Math.round(r.bottom + 8) + 'px';
    } catch (e) { menu.style.top = ''; }
  }

  function onDocPointer(e) {
    if (!open) return;
    try { if (root.contains(e.target)) return; } catch (err) { /* noop */ }
    close(false);
  }
  function onDocKey(e) {
    if (!open) return;
    if (e.key === 'Escape') { close(true); return; }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      const list = items.filter((b) => b.isConnected !== false);
      if (!list.length) return;
      const i = list.indexOf(document.activeElement);
      const d = e.key === 'ArrowDown' ? 1 : -1;
      const n = list[(i + d + list.length) % list.length];
      try { n.focus(); } catch (err) { /* noop */ }
    }
  }

  function show() {
    if (open || destroyed) return;
    open = true;
    place();
    menu.hidden = false;
    root.classList.add('is-open');
    btn.setAttribute('aria-expanded', 'true');
    try { document.addEventListener('pointerdown', onDocPointer, { passive: true, capture: true }); } catch (e) { /* noop */ }
    try { document.addEventListener('keydown', onDocKey, { passive: true }); } catch (e) { /* noop */ }
    try { if (items[0]) items[0].focus(); } catch (e) { /* noop */ }
  }
  function close(refocus) {
    if (!open) return;
    open = false;
    menu.hidden = true;
    root.classList.remove('is-open');
    btn.setAttribute('aria-expanded', 'false');
    try { document.removeEventListener('pointerdown', onDocPointer, { capture: true }); } catch (e) { /* noop */ }
    try { document.removeEventListener('keydown', onDocKey); } catch (e) { /* noop */ }
    if (refocus) { try { btn.focus(); } catch (e) { /* noop */ } }
  }

  btn.addEventListener('click', () => { if (open) close(false); else show(); });

  paint();

  return {
    el: root,
    isOpen: () => open,
    /** A later `profile` frame with an `account` on it repaints in place. */
    setAccount(next) {
      const a = readAccount(next);
      if (!a) return;
      acct = a;
      paint();
    },
    close: () => close(false),
    destroy() {
      close(false);
      destroyed = true;
      try { root.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createAccountChip;
