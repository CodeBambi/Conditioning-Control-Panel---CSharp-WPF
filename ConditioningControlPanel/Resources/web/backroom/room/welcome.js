/* ============================================================================
 * backroom/room/welcome.js - the first-visit card (CONTRACT section 13).
 *
 * Shown once, over the room, the moment the loading veil lifts: the hero art on
 * top, then two pages under the same hero. Page 1 is the intro (the title, three
 * numbered steps: walk in, take a seat, spend your sparkles). Page 2, "Pictures
 * and sparkles", says where the pictures come from (the source picked in Options
 * > Pictures and GIFs), how Sparkle Points are earned in the app, and that the
 * Prize Parlour is where they become prizes. Next / Back, page dots, the arrow
 * keys, and "Let me in" on the last page. Dismissed by the button, Escape,
 * Enter, Space or a tap on the veil. It is the loader's card chrome
 * (.br-card-veil / .br-card), so it looks like the room's own furniture and not
 * a tutorial bolted on.
 *
 * WHO REMEMBERS: the host. Dismissing posts one room-option {welcomeSeen:true}
 * through main.js's setOption, the host writes AppSettings.BackRoomWelcomeSeen
 * and echoes it as init.welcomeSeen on the next open. No localStorage - the
 * room keeps none, on purpose - so the card follows the account, not the
 * browser profile.
 *
 * RE-OPENING: the Prize Parlour's "How it works" chip (stations/counter) calls
 * reopenWelcome(), which lays the same card at page 1 with the options main.js
 * registered through installWelcome(). That copy writes nothing on dismiss.
 *
 * KEYS: while the card is up every key is swallowed at the window's capture
 * phase, so W/S cannot walk a room the player cannot see and E cannot seat
 * them at a station behind the veil. The pointer never reaches the stage
 * either: the veil is a full-inset node in #br-layer. main.js's own Escape
 * handler is registered earlier on the same target, so it asks this card
 * first (dismissWelcome() answers true when it closed something) before it
 * walks out of the room.
 *
 * DOM only through createElement / append / remove / addEventListener, so the
 * node suite can run it against a stub document (trap: #1371 shipped a render
 * loop nothing executed).
 * ==========================================================================*/

/** The three steps: lexicon keys, with the phone wording where the desk one names keys. */
export const STEPS = Object.freeze([
  { lead: 'br_welcome_step1_lead', body: 'br_welcome_step1', touch: 'br_welcome_step1_touch' },
  { lead: 'br_welcome_step2_lead', body: 'br_welcome_step2', touch: 'br_welcome_step2_touch' },
  { lead: 'br_welcome_step3_lead', body: 'br_welcome_step3', touch: null },
]);

/** Page 2: pictures, sparkles, prizes. One wording for desk and phone. */
export const PAGE2 = Object.freeze([
  { lead: 'br_welcome_media_lead', body: 'br_welcome_media', touch: null },
  { lead: 'br_welcome_sp_lead', body: 'br_welcome_sp', touch: null },
  { lead: 'br_welcome_prizes_lead', body: 'br_welcome_prizes', touch: null },
]);

export const FALLBACK = Object.freeze({
  br_welcome_title: 'Welcome to the Back Room',
  br_welcome_sub: 'EMI keeps score. Three things to know.',
  br_welcome_step1_lead: 'Walk in',
  br_welcome_step1: 'W/S or the arrows to walk, drag to look around.',
  br_welcome_step1_touch: 'Push the stick to walk, drag to look around.',
  br_welcome_step2_lead: 'Take a seat',
  br_welcome_step2: 'Walk up to a station and press E, or tap it. Step back to stand up.',
  br_welcome_step2_touch: 'Walk up to a station and tap it. Pull the stick back to stand up.',
  br_welcome_step3_lead: 'Spend your sparkles',
  br_welcome_step3: 'Every play costs Sparkle Points, every win pays them back. The counter trades them for prizes.',
  br_welcome_go: 'Let me in',
  br_welcome_p2_title: 'Pictures and sparkles',
  br_welcome_p2_sub: 'Where the pictures and the points come from.',
  br_welcome_media_lead: 'Pick your pictures',
  br_welcome_media: 'The room shows pictures and GIFs from the source you choose in Options, under Pictures and GIFs: your files, Scrolller, both, or the built-in art. On Scrolller you can add your own niches.',
  br_welcome_sp_lead: 'Earn your sparkles',
  br_welcome_sp: 'Sparkle Points come from the app: one for every level up and one for every 100 bubbles popped. The house is generous, so the tables pay them back over time.',
  br_welcome_prizes_lead: 'Turn them into prizes',
  br_welcome_prizes: 'The Prize Parlour, the counter at the back, trades sparkles for prizes: new effects, Racing Thoughts bundles and more.',
  br_welcome_next: 'Next',
  br_welcome_prev: 'Back',
});

/** Keys that close the card. Everything else is swallowed while it is up. */
export const CLOSE_KEYS = Object.freeze(['Escape', 'Enter', ' ']);

/** Where the hero lives, relative to index.html like every other room asset. */
export const HERO_SRC = 'room/assets/welcome-hero.webp';

const coarse = () => { try { return !!matchMedia('(pointer: coarse)').matches; } catch { return false; } };

/**
 * Which wording each step gets: the desk line names keys, the phone line names the stick.
 * @param {boolean} touch
 * @returns {string[]} lexicon keys, one per step, in order
 */
export function stepKeys(touch) {
  return STEPS.map(s => (touch && s.touch) ? s.touch : s.body);
}

/**
 * Should the card show on this open? Only when the host has never recorded a dismissal.
 * A missing field (an older host) reads as not seen: better one card too many than a room
 * with no explanation, and the dismissal is written the first time it shows.
 */
export function shouldShow(init) {
  return !(init && init.welcomeSeen === true);
}

/* ------------------------------------------------------------- the one live card
 * main.js registers { layer, lex } once (installWelcome); the first-visit card and every
 * re-open from the Parlour share it. Only one card is up at a time. */
let installed = null;
let live = null;

/** Remember where the card goes and how it reads, so a station can re-open it later. */
export function installWelcome(o) { installed = o || null; }

/** Re-open the card (page 1 by default) with the installed options. Writes nothing on dismiss. */
export function reopenWelcome(page = 0) {
  if (live && live.open()) { live.go(page); return live; }
  if (!installed) return null;
  return createWelcome({ ...installed, onDone: null, page });
}

/** Close whichever card is up. True when one closed. */
export function dismissWelcome() { return !!(live && live.dismiss()); }

/**
 * Mount the card.
 * @param {Object} o { layer, lex(key, fallback), onDone(), touch?, hero?, page?, doc?, win? }
 * @returns {{ root, open(): boolean, dismiss(): boolean, page(): number, go(n): number }}
 */
export function createWelcome(o) {
  const doc = o.doc || globalThis.document;
  const win = o.win || globalThis.window;
  const L = (k) => (typeof o.lex === 'function' ? o.lex(k, FALLBACK[k]) : FALLBACK[k]) || FALLBACK[k];
  const touch = typeof o.touch === 'boolean' ? o.touch : coarse();
  const el = (tag, cls, text) => {
    const n = doc.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  };
  const button = (cls, text) => { const b = el('button', cls, text); b.setAttribute('type', 'button'); return b; };

  const root = el('div', 'br-card-veil br-welcome-veil');
  const card = el('section', 'br-card br-welcome');
  card.setAttribute('role', 'dialog'); card.setAttribute('aria-modal', 'true');
  const hero = el('img', 'br-welcome-hero');
  hero.setAttribute('alt', ''); hero.setAttribute('decoding', 'async');
  hero.src = o.hero || HERO_SRC;

  /** One page: title, sub, three numbered rows. */
  const page = (titleKey, subKey, rows, keys) => {
    const sec = el('div', 'br-welcome-page');
    sec.append(el('h2', 'br-card-title br-welcome-title', L(titleKey)), el('p', 'br-welcome-sub', L(subKey)));
    const list = el('ol', 'br-welcome-steps');
    rows.forEach((s, i) => {
      const li = el('li', 'br-welcome-step');
      const num = el('b', 'br-welcome-num', String(i + 1));
      const text = el('div', 'br-welcome-text');
      text.append(el('strong', 'br-welcome-lead', L(s.lead)), el('span', 'br-welcome-body', L(keys[i])));
      li.append(num, text);
      list.append(li);
    });
    sec.append(list);
    return { sec, title: L(titleKey) };
  };
  const pages = [
    page('br_welcome_title', 'br_welcome_sub', STEPS, stepKeys(touch)),
    page('br_welcome_p2_title', 'br_welcome_p2_sub', PAGE2, PAGE2.map(s => s.body)),
  ];

  const nav = el('div', 'br-welcome-nav');
  const prev = button('br-welcome-prev', L('br_welcome_prev'));
  const dots = el('div', 'br-welcome-dots');
  dots.setAttribute('role', 'tablist');
  const dotList = pages.map((p, i) => {
    const d = button('br-welcome-dot', '');
    d.setAttribute('aria-label', String(i + 1)); d.setAttribute('role', 'tab');
    d.addEventListener('click', () => go(i));
    dots.append(d);
    return d;
  });
  const next = button('br-welcome-next', L('br_welcome_next'));
  const goBtn = button('br-welcome-go', L('br_welcome_go'));
  nav.append(prev, dots, next, goBtn);
  card.append(hero, ...pages.map(p => p.sec), nav);
  root.append(card);

  let idx = -1;
  const last = pages.length - 1;
  /** Show page n (clamped). Returns the page now showing. */
  function go(n) {
    n = Math.max(0, Math.min(last, Number(n) || 0));
    if (n === idx) return idx;
    idx = n;
    pages.forEach((p, i) => { p.sec.hidden = i !== idx; });
    dotList.forEach((d, i) => { d.setAttribute('data-on', i === idx ? '1' : '0'); d.setAttribute('aria-selected', i === idx ? 'true' : 'false'); });
    prev.hidden = idx === 0;
    next.hidden = idx === last;
    goBtn.hidden = idx !== last;
    card.setAttribute('aria-label', pages[idx].title);
    return idx;
  }

  let opened = true;
  const handle = { root, open: () => opened, dismiss, page: () => idx, go };
  function dismiss() {
    if (!opened) return false;
    opened = false;
    if (live === handle) live = null;
    win.removeEventListener('keydown', onKey, true);
    try { root.remove(); } catch (e) { /* a stub layer with no remove */ }
    try { o.onDone && o.onDone(); } catch (e) { /* the host write is the caller's */ }
    return true;
  }
  function onKey(e) {
    if (!opened) return;
    // Every key stops here while the card is up: the room behind the veil must not walk or seat.
    e.stopPropagation();
    if (CLOSE_KEYS.includes(e.key)) { e.preventDefault(); dismiss(); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); go(idx + 1); }
    else if (e.key === 'ArrowLeft') { e.preventDefault(); go(idx - 1); }
  }
  /* A tap on the veil closes it on pointerdown; the click that follows lands on whatever is under
   * the veil by then (a station's "tap outside to stand up", say), so that one click is eaten. */
  function eatNextClick() {
    const eat = (ev) => { try { ev.stopPropagation(); } catch (e) { /* noop */ } win.removeEventListener('click', eat, true); };
    win.addEventListener('click', eat, true);
    try { setTimeout(() => win.removeEventListener('click', eat, true), 600); } catch (e) { /* noop */ }
  }
  prev.addEventListener('click', () => go(idx - 1));
  next.addEventListener('click', () => go(idx + 1));
  goBtn.addEventListener('click', dismiss);
  root.addEventListener('pointerdown', (e) => { if (e.target === root) { eatNextClick(); dismiss(); } });
  if (live && live !== handle) live.dismiss();
  win.addEventListener('keydown', onKey, true);
  go(o.page || 0);
  o.layer.append(root);
  try { requestAnimationFrame(() => { try { (idx === last ? goBtn : next).focus(); } catch (e) { /* noop */ } }); } catch (e) { /* no rAF in node */ }
  live = handle;
  return handle;
}
