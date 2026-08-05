/* ============================================================================
 * ui/screens/title.js — the storefront.
 *
 * Logo, one kicker, a five-item menu, and the fineprint that tells the player
 * the truth about the machine boundary before anything else does. Nothing on
 * this screen touches the network: Host and Join only ROUTE, and the actual
 * signaling call happens on the screen that can render its failure.
 *
 * "Practice" is always present and becomes the PRIMARY action when the page is
 * standalone, because a dev/play-tester with no server should never have to
 * guess which button works.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import { formatBytes } from '../assetsStore.js';
import { GOON_BUILD } from '../../bridge.js';

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { session, actions, audio, prefs, sheets } = ctx;
  const standalone = !session.hosted;

  const card = el('div', { class: 'gg-card gg-title' });

  /* --- brand ---------------------------------------------------------- */
  const logo = el('img', {
    class: 'gg-title-logo',
    src: './assets/goon_game_logo.png',
    alt: 'Goon Game',
    decoding: 'async',
  });
  // A missing/blocked image must never leave a blank title screen.
  ledger.listen(logo, 'error', () => {
    try { logo.remove(); } catch (_e) { /* ignore */ }
    card.prepend(el('h1', { class: 'gg-grad gg-title-wordmark', text: 'Goon Game' }));
  });
  card.appendChild(logo);
  card.appendChild(el('p', { class: 'gg-title-kicker', text: S.title.kicker }));

  /* --- menu ----------------------------------------------------------- */
  const menu = el('nav', { class: 'gg-menu', 'aria-label': 'main menu' });

  const item = (label, onClick, { variant = '', note = '', sfx = 'ui-select' } = {}) => {
    const b = button(ledger, label, onClick, { variant, audio, sfx });
    b.classList.add('gg-menu-item');
    if (note) {
      b.appendChild(el('span', { class: 'gg-menu-note', text: note }));
      b.classList.add('has-note');
    }
    ledger.listen(b, 'pointerenter', () => { try { audio?.sfx?.('ui-move'); } catch (_e) { /* stub */ } });
    menu.appendChild(b);
    return b;
  };

  /* THE HOST GATE (2026-08-04). Hosting is a tier-2 perk — the server refuses below it with 403
   * no_host_access — and the C# host answers the same question locally in `caps.canHost`, so
   * HOSTED we can say so on the menu instead of routing the player to a screen whose entire
   * content is a refusal. The item stays VISIBLE and dims: a missing menu row reads as a broken
   * build, and a perk nobody can see is a perk nobody buys.
   *
   * STANDALONE it stays enabled. Out here entitlement is unknowable until a server has answered
   * (the same reason the status ribbon below still renders no premium state), and a page that
   * dimmed Host on a guess would lock a paying supporter out of the thing they paid for. The 403
   * sheet is the fallback and it says the same sentence.
   *
   * `=== false` on purpose, exactly how the page reads every other cap: a host that predates the
   * flag leaves Host alone rather than locking it. */
  const hostLocked = !standalone && (session.caps || {}).canHost === false;
  const hostItem = item(S.title.host, () => { if (!hostLocked) actions.goHost(); }, {
    variant: hostLocked ? '' : (standalone ? '' : 'primary'),
    note: hostLocked ? S.title.hostNoLab : '',
  });
  if (hostLocked) {
    // The lobby's transfer-row pattern: disable the control and put the WHY directly under it.
    // `disabled` carries the dimming too (.gg-btn:disabled) and keeps it out of the tab order.
    hostItem.disabled = true;
    hostItem.classList.add('is-disabled');
    hostItem.setAttribute('aria-disabled', 'true');
  }
  item(S.title.join, () => actions.goJoin());
  item(S.title.practice, () => actions.goPractice(), {
    variant: standalone ? 'primary' : '',
    note: S.title.practiceNote,
  });
  item(S.title.assets, () => actions.goAssets());
  /* THE VOICE LIBRARY. Its own menu item rather than a corner of Options,
   * because it is a place you MAKE something (like Assets), not a switch you
   * flip — and because the opt-in behind it is a consent decision that deserves
   * a screen of its own rather than a row in a drawer. */
  item(S.voice.menu, () => actions.goVoice(), { note: S.voice.menuNote });
  item(S.title.options, () => ctx.options?.open?.());
  item(S.title.how, () => showHowItWorks(), { variant: 'ghost' });
  if (session.hosted) item(S.title.quit, () => actions.quit('title'), { variant: 'ghost', sfx: 'ui-back' });

  card.appendChild(menu);

  /* --- status ribbon --------------------------------------------------
   * Premium/pass state is still NOT rendered here: it is only knowable after a
   * server round-trip, and a ribbon that says "free match available" before we
   * have asked is a lie the player would plan around.
   *
   * What it does carry is the one fact we already hold locally — how much of
   * the library is send-ready ("412 ready · 3.1 GB cached"). It stays hidden
   * until a cache-state actually lands, and stays hidden forever when there is
   * no host cache at all (standalone), because an empty ribbon reads as a
   * broken one.
   * ------------------------------------------------------------------- */
  const ribbon = el('div', { class: 'gg-title-ribbon', hidden: true });
  card.appendChild(ribbon);

  if (ctx.assets && typeof ctx.assets.onState === 'function') {
    ledger.sub(ctx.assets.onState((st) => {
      const show = !!(st && st.available && st.loaded && (st.ready > 0 || st.usedBytes > 0));
      ribbon.textContent = show ? S.assets.ribbon(st.ready, formatBytes(st.usedBytes)) : '';
      ribbon.hidden = !show;
    }));
  }

  card.appendChild(el('p', { class: 'gg-title-fineprint', text: S.title.fineprint }));

  /* THE BUILD STAMP (bridge.js GOON_BUILD). One muted line so a play-tester can
   * tell at a glance whether a device — the iPhone especially, whose Safari
   * cache outlives deploys — is running the build that was just shipped. It is
   * a diagnostic, not branding: it must never grow, animate, or move up. */
  card.appendChild(el('p', { class: 'gg-title-buildstamp', text: 'build ' + GOON_BUILD }));

  // NOTE: #gg-boot-ok is owned by boot.js and lives on <body> — it has to stay
  // readable on every screen, not just this one, or the play-test driver loses
  // its probe the moment the player leaves the menu.

  container.appendChild(card);

  function showHowItWorks() {
    prefs?.set?.('seenHowItWorks', true);
    // Built as a sheet body rather than through sheets.open(), because six
    // bullets is a list, not a one-line notice.
    /* THE GOAL, ABOVE THE BULLETS (2026-08-05). Six bullets described the LOOP
     * accurately and never once said what winning is, so a first-time reader
     * finished the card knowing how to throw a flash and not what for. It is a
     * lead PARAGRAPH and deliberately not a seventh bullet — the list is pinned
     * at six by its own note in ui/strings.js, and "the goal" is not a step in a
     * sequence anyway. Same sentence the desk's own caption carries, one screen
     * earlier (ui/hud.js THE OBJECTIVE LINE). */
    const body = el('div', { class: 'gg-how' }, [
      el('h2', { class: 'gg-sheet-headline', text: S.how.headline }),
      el('p', { class: 'gg-how-goal', text: S.coach.howGoal }),
      el('ul', { class: 'gg-how-list' }, S.how.bullets.map((b) => el('li', { text: b }))),
    ]);
    sheets?.openNode?.(body, { label: S.how.headline, closeLabel: S.how.close });
  }

  // The first-ever visit opens the explainer once, unprompted. After that it
  // is a menu item like any other.
  if (prefs && !prefs.get('seenHowItWorks')) ledger.timer(showHowItWorks, 420);

  // Build the audio graph on the FIRST screen, not on the first cue. Under
  // WebView2 the context comes up running (the host passes
  // --autoplay-policy=no-user-gesture-required) so this is simply an early warm;
  // in a plain browser it is what installs the gesture listener that resumes a
  // suspended context, so the very first menu click is already audible instead
  // of being the click that merely unlocks the bus.
  try { audio?.unlock?.(); } catch (_e) { /* stub bus */ }

  try { audio?.music?.('title'); } catch (_e) { /* stub bus */ }
  ledger.add(() => { try { audio?.stopMusic?.(); } catch (_e) { /* stub bus */ } });

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
