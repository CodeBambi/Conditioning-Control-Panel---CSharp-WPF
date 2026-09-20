/* ============================================================================
 * backroom/room/welcome.js - the first-visit card (CONTRACT section 13).
 *
 * Shown once, over the room, the moment the loading veil lifts: three pages,
 * each a landscape picture on top, a title, three numbered steps and the buttons.
 * Page one is the room (walk in, take a seat, spend your sparkles), page two is
 * pictures and sparkles (where the pictures and the points come from), page
 * three is the Prize Parlour (what the sparkles buy). Dismissed by the last page's
 * button, Escape, Enter, Space or a tap on the veil; Next/Back and the arrow
 * keys turn the page. It is the loader's card chrome (.br-card-veil / .br-card),
 * so it looks like the room's own furniture and not a tutorial bolted on.
 *
 * The first and last pages (placard: true) hang on the counter's apron as
 * placards (welcome-placards.js): a tap on one reopens the card at that page,
 * in READ mode (o.read), where the last button says Close and nothing is
 * remembered; Next from the welcome placard still walks pages two and three.
 *
 * WHO REMEMBERS: the host. Dismissing posts one room-option {welcomeSeen:true}
 * through main.js's setOption, the host writes AppSettings.BackRoomWelcomeSeen
 * and echoes it as init.welcomeSeen on the next open. No localStorage - the
 * room keeps none, on purpose - so the card follows the account, not the
 * browser profile.
 *
 * KEYS: while the card is up every key is swallowed at the window's capture
 * phase, so W/S cannot walk a room the player cannot see and E cannot seat
 * them at a station behind the veil. The pointer never reaches the stage
 * either: the veil is a full-inset node in #br-layer. main.js's own Escape
 * handler is registered earlier on the same target, so it asks this card
 * first (dismiss() answers true when it closed something) before it walks out
 * of the room.
 *
 * DOM only through createElement / append / remove / addEventListener, so the
 * node suite can run it against a stub document (trap: #1371 shipped a render
 * loop nothing executed).
 * ==========================================================================*/

/** Page one's three steps: lexicon keys, with the phone wording where the desk one names keys. */
export const STEPS = Object.freeze([
  { lead: 'br_welcome_step1_lead', body: 'br_welcome_step1', touch: 'br_welcome_step1_touch' },
  { lead: 'br_welcome_step2_lead', body: 'br_welcome_step2', touch: 'br_welcome_step2_touch' },
  { lead: 'br_welcome_step3_lead', body: 'br_welcome_step3', touch: null },
]);

/** Page two's three steps: the pictures, the points, the prizes; one wording for every device. */
export const MEDIA_STEPS = Object.freeze([
  { lead: 'br_welcome_media_lead', body: 'br_welcome_media', touch: null },
  { lead: 'br_welcome_sp_lead', body: 'br_welcome_sp', touch: null },
  { lead: 'br_welcome_prizes_lead', body: 'br_welcome_prizes', touch: null },
]);

/** Page three's three steps: the counter, in the same shape, one wording for every device. */
export const PRIZE_STEPS = Object.freeze([
  { lead: 'br_welcome_prize1_lead', body: 'br_welcome_prize1', touch: null },
  { lead: 'br_welcome_prize2_lead', body: 'br_welcome_prize2', touch: null },
  { lead: 'br_welcome_prize3_lead', body: 'br_welcome_prize3', touch: null },
]);

/** Where the pictures live, relative to index.html like every other room asset. Both are 2:1 landscapes. */
export const HERO_SRC = 'room/assets/welcome-hero.webp';
export const PRIZES_SRC = 'room/assets/welcome-prizes.webp';

/** The three pages, in reading order: this is the one list. welcome-placards.js hangs the entries flagged
 * placard (the room and the Parlour, one per framed panel); the media page in between has no panel of its own. */
export const PAGES = Object.freeze([
  Object.freeze({ id: 'welcome', hero: HERO_SRC, title: 'br_welcome_title', sub: 'br_welcome_sub', steps: STEPS, placard: true }),
  Object.freeze({ id: 'media', hero: HERO_SRC, title: 'br_welcome_p2_title', sub: 'br_welcome_p2_sub', steps: MEDIA_STEPS, placard: false }),
  Object.freeze({ id: 'prizes', hero: PRIZES_SRC, title: 'br_welcome_prizes_title', sub: 'br_welcome_prizes_sub', steps: PRIZE_STEPS, placard: true }),
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
  br_welcome_p2_title: 'Pictures and sparkles',
  br_welcome_p2_sub: 'Where the pictures and the points come from.',
  br_welcome_media_lead: 'Pick your pictures',
  br_welcome_media: 'The room shows pictures and GIFs from the source you choose in Options, under Pictures and GIFs: your files, Scrolller, both, or the built-in art. On Scrolller you can add your own niches.',
  br_welcome_sp_lead: 'Earn your sparkles',
  br_welcome_sp: 'Sparkle Points come from the app: one for every level up and one for every 100 bubbles popped. The house is generous, so the tables pay them back over time.',
  br_welcome_prizes_lead: 'Turn them into prizes',
  br_welcome_prizes: 'The Prize Parlour, the counter at the back, trades sparkles for prizes: new effects, Racing Thoughts bundles and more.',
  br_welcome_prizes_title: 'The Prize Parlour',
  br_welcome_prizes_sub: 'What your sparkles buy.',
  br_welcome_prize1_lead: 'Walk up to the counter',
  br_welcome_prize1: 'EMI trades Sparkle Points for prizes: Jackpot Remix, Flashes v2, Bubbles v2, the High Roller role and the Racing Thoughts bundles.',
  br_welcome_prize2_lead: 'Try before you buy',
  br_welcome_prize2: 'Most prizes have a Try it button. Once bought, a prize is yours for good.',
  br_welcome_prize3_lead: 'Take it home',
  br_welcome_prize3: 'Prizes follow your account and switch on in the CCP app: Flashes, Bubble Pop and the Play tab.',
  br_welcome_go: 'Let me in',
  br_welcome_next: 'Next',
  br_welcome_close: 'Close',
  br_welcome_read: 'Tap to read',
  br_back: 'Back',
});

/** Keys that close the card. Everything else is swallowed while it is up. */
export const CLOSE_KEYS = Object.freeze(['Escape', 'Enter', ' ']);
/** Keys that turn the page, and which way. */
export const PAGE_KEYS = Object.freeze({ ArrowRight: 1, ArrowLeft: -1 });

const coarse = () => { try { return !!matchMedia('(pointer: coarse)').matches; } catch { return false; } };

/**
 * Which wording each step gets: the desk line names keys, the phone line names the stick.
 * @param {boolean} touch
 * @param {ReadonlyArray} [steps] a page's steps; page one by default
 * @returns {string[]} lexicon keys, one per step, in order
 */
export function stepKeys(touch, steps = STEPS) {
  return steps.map(s => (touch && s.touch) ? s.touch : s.body);
}

/**
 * Should the card show on this open? Only when the host has never recorded a dismissal.
 * A missing field (an older host) reads as not seen: better one card too many than a room
 * with no explanation, and the dismissal is written the first time it shows.
 */
export function shouldShow(init) {
  return !(init && init.welcomeSeen === true);
}

/**
 * Mount the card.
 * @param {Object} o { layer, lex(key, fallback), onDone(), touch?, hero?, page?, read?, doc?, win? }
 *   page: the page to open on (0 by default); read: opened from a placard, so the last button says Close.
 * @returns {{ root, open(): boolean, dismiss(): boolean, show(i): void, page(): number }}
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
  const clamp = i => Math.max(0, Math.min(PAGES.length - 1, i | 0));

  const root = el('div', 'br-card-veil br-welcome-veil');
  const card = el('section', 'br-card br-welcome');
  card.setAttribute('role', 'dialog'); card.setAttribute('aria-modal', 'true');
  const hero = el('img', 'br-welcome-hero');
  hero.setAttribute('alt', ''); hero.setAttribute('decoding', 'async');
  const title = el('h2', 'br-card-title br-welcome-title');
  const sub = el('p', 'br-welcome-sub');
  const list = el('ol', 'br-welcome-steps');
  const nav = el('div', 'br-welcome-nav');
  const back = el('button', 'br-welcome-back', L('br_back'));
  back.setAttribute('type', 'button');
  const dots = el('div', 'br-welcome-dots');
  dots.setAttribute('aria-hidden', 'true');
  const go = el('button', 'br-welcome-go');
  go.setAttribute('type', 'button');
  nav.append(back, dots, go);
  card.append(hero, title, sub, list, nav);
  root.append(card);

  let page = -1;
  /** Paint page i: the picture, the words, the steps, and which buttons this page gets. */
  function show(i) {
    page = clamp(i);
    const p = PAGES[page], last = page === PAGES.length - 1;
    hero.src = (page === 0 && o.hero) || p.hero;
    title.textContent = L(p.title);
    card.setAttribute('aria-label', L(p.title));
    card.setAttribute('data-page', String(page));
    sub.textContent = L(p.sub);
    for (const c of [...list.children]) c.remove();
    const keys = stepKeys(touch, p.steps);
    p.steps.forEach((s, n) => {
      const li = el('li', 'br-welcome-step');
      const num = el('b', 'br-welcome-num', String(n + 1));
      const text = el('div', 'br-welcome-text');
      text.append(el('strong', 'br-welcome-lead', L(s.lead)), el('span', 'br-welcome-body', L(keys[n])));
      li.append(num, text);
      list.append(li);
    });
    for (const c of [...dots.children]) c.remove();
    PAGES.forEach((_, n) => dots.append(el('span', 'br-welcome-dot' + (n === page ? ' is-on' : ''))));
    back.hidden = page === 0;
    go.textContent = last ? L(o.read ? 'br_welcome_close' : 'br_welcome_go') : L('br_welcome_next');
  }

  let opened = true;
  function dismiss() {
    if (!opened) return false;
    opened = false;
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
    else if (PAGE_KEYS[e.key]) { e.preventDefault(); show(page + PAGE_KEYS[e.key]); }
  }
  go.addEventListener('click', () => { if (page === PAGES.length - 1) dismiss(); else show(page + 1); });
  back.addEventListener('click', () => show(page - 1));
  root.addEventListener('pointerdown', (e) => { if (e.target === root) dismiss(); });
  win.addEventListener('keydown', onKey, true);
  show(o.page || 0);
  o.layer.append(root);
  try { requestAnimationFrame(() => { try { go.focus(); } catch (e) { /* noop */ } }); } catch (e) { /* no rAF in node */ }
  return { root, open: () => opened, dismiss, show, page: () => page };
}
