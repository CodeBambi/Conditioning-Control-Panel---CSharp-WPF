/* ============================================================================
 * ui/discord.js — the page's whole view of Discord, and the lobby panel.
 *
 * Work Item D of docs/GOON_DISCORD_CONTRACT.md. This module is the ONLY thing
 * on the page that talks the discord verbs, and it owns two of them
 * (`discord`, `peer-card`) at the bridge — bridge.on throws on a duplicate, so
 * ownership here is enforced by the loader, not by convention.
 *
 * THE PAGE NEVER SEES A SNOWFLAKE. Not once, not in a data attribute, not in a
 * log line. `dm` is a BOOLEAN both for us and for the peer; opening a DM posts
 * `discord-open-dm {which}` and the HOST resolves the id from its own store
 * (§4). There is deliberately no field on this object that could hold one.
 *
 * THE ECHO IS THE TRUTH, THE REQUEST IS NOT. Every toggle in the lobby panel
 * renders from the last `discord` frame the host sent, never from the click
 * that caused it — the fullscreen pattern. A flag that the host refused (no
 * link, a settings write that failed) has to read as OFF, and the only way to
 * guarantee that is to never paint a value we invented. This is why writePrefs
 * returns nothing useful and why nothing here keeps a shadow copy.
 *
 * THE PEER CARD NEVER GATES ANYTHING. `notePeerCardVer` is fire-and-forget: it
 * posts at most one request per version, it is skipped entirely when the local
 * viewer pref is off, and a card that never arrives leaves a tile fallback on
 * screen (§7). Nothing in the lobby, the countdown or Live waits on it.
 *
 * `peer-card-req` CARRIES THE ROOM CREDENTIALS, and that is not an oversight in
 * the direction it looks like. /v2/goon/peercard is roomAuth(requireJoined) and
 * the C# host never joined the room — only THIS page holds {code, token, role}
 * (boot.js stashes them off the transport). So the page hands the host the three
 * fields to forward and the host keeps the URL, the timeout and the disk cache.
 * The token is a per-room signaling credential, never the Patreon bearer.
 *
 * The `discord` ECHO IS A FLAT FRAME — `{type:'discord', avatarState, ...}`, not
 * a nested block. Only `init` nests it (and only `init` carries lastOpponent).
 * normalizeState reads the frame itself, which is what makes both shapes work.
 *
 * Import-safe under node: no DOM and no bridge traffic at import.
 * ==========================================================================*/

import * as bridgeMod from '../bridge.js';
import { el, button } from './router.js';
import { S } from './strings.js';
import { avatarNode, avatarSlot } from './avatar.js';

/** Host -> page. One owner, and it is this module. */
export const DISCORD_VERBS_IN = Object.freeze(['discord', 'peer-card']);

/** Page -> host. Every one of them is posted from this file and nowhere else. */
export const DISCORD_VERBS_OUT = Object.freeze([
  'discord-prefs', 'peer-card-req', 'discord-open-dm', 'discord-link-request',
  'rp-state', 'last-opponent-clear',
]);

/** The `rp-state` enum, frozen by §4. Anything else is dropped, never sent. */
export const RP_STATES = Object.freeze(['lobby', 'live', 'recap', 'off']);

/** What a page with no host (or a host that never spoke) shows. */
export const DEFAULT_DISCORD = Object.freeze({
  avatarState: 'unlinked',
  avatarDataUri: null,
  dmShared: false,
  richPresence: false,
  seenSharePrompt: false,
});

const AVATAR_STATES = ['shared', 'off', 'unlinked'];

function str(v) { return typeof v === 'string' ? v : ''; }
function dataUri(v) { return (typeof v === 'string' && v.slice(0, 5) === 'data:') ? v : null; }

/** Normalize a `discord` frame. A malformed one degrades to "nothing shared". */
export function normalizeState(raw) {
  const s = (raw && typeof raw === 'object') ? raw : {};
  const av = str(s.avatarState);
  return {
    avatarState: AVATAR_STATES.indexOf(av) >= 0 ? av : 'unlinked',
    avatarDataUri: dataUri(s.avatarDataUri),
    dmShared: !!s.dmShared,
    richPresence: !!s.richPresence,
    seenSharePrompt: !!s.seenSharePrompt,
  };
}

/** Normalize `init.discord.lastOpponent`. Null is the normal case. */
export function normalizeLastOpponent(raw) {
  if (!raw || typeof raw !== 'object') return null;
  const name = str(raw.name).slice(0, 32);
  if (!name) return null;
  const ts = Number(raw.ts);
  return {
    name,
    avatarDataUri: dataUri(raw.avatarDataUri),
    dm: !!raw.dm,
    ts: isFinite(ts) && ts > 0 ? ts : 0,
  };
}

/** Normalize a `peer-card` frame. */
export function normalizePeerCard(raw) {
  if (!raw || typeof raw !== 'object') return null;
  return {
    name: str(raw.name).slice(0, 32),
    avatarDataUri: dataUri(raw.avatarDataUri),
    reason: str(raw.reason) || 'none',
    dm: !!raw.dm,
    ver: str(raw.ver) || null,
  };
}

/** "just now" / "14 min ago" / "3 days ago" — the last-opponent card's stamp. */
export function relativeTime(ts, now) {
  const t = Number(ts) || 0;
  const n = Number(now) || Date.now();
  if (!t) return S.discord.agoUnknown;
  const secs = Math.max(0, Math.round((n - t) / 1000));
  if (secs < 90) return S.discord.agoNow;
  const mins = Math.round(secs / 60);
  if (mins < 60) return S.discord.agoMinutes(mins);
  const hours = Math.round(mins / 60);
  if (hours < 36) return S.discord.agoHours(hours);
  return S.discord.agoDays(Math.round(hours / 24));
}

/* ========================================================================= */

/**
 * @param {object} [o]
 * @param {object} [o.prefs]   ui/prefs.js handle — read for showOpponentAvatars
 * @param {object} [o.logger]
 * @param {Function} [o.send]  test seam; defaults to bridge.send
 * @param {Function} [o.on]    test seam; defaults to bridge.on
 * @param {boolean} [o.hosted] test seam; defaults to bridge.isHosted
 * @param {Function} [o.getRoom] () => {code, token, role} — boot's session.room.
 *                               Required for peer-card-req; without it the
 *                               request is SKIPPED rather than sent to 403.
 */
export function createDiscord({ prefs = null, logger = null, send = null, on = null,
  hosted = null, getRoom = null } = {}) {
  const post = typeof send === 'function' ? send : ((m) => bridgeMod.send(m));
  const subscribeBridge = typeof on === 'function' ? on : ((t, fn) => bridgeMod.on(t, fn));
  const isHosted = hosted === null ? bridgeMod.isHosted : !!hosted;

  /** {code, token, role} or null. Read fresh every time — a relay fallback rebuilds it. */
  function room() {
    if (typeof getRoom !== 'function') return null;
    let r = null;
    try { r = getRoom(); } catch (_e) { return null; }
    if (!r || typeof r !== 'object') return null;
    const code = str(r.code);
    const token = str(r.token);
    const role = r.role === 'guest' ? 'guest' : (r.role === 'host' ? 'host' : '');
    if (!code || !token || !role) return null;
    return { code, token, role };
  }

  let state = normalizeState(null);
  let lastOpponent = null;
  let peer = null;
  /** The last version we ASKED for. Also advanced by an arriving card. */
  let askedVer = null;
  /** The last rp-state actually posted, so a repeat is one frame, not twenty. */
  let rp = 'off';
  let disposed = false;

  const listeners = new Set();
  const counters = { prefsWrites: 0, cardReqs: 0, dmOpens: 0, linkReqs: 0, rpPosts: 0, clears: 0 };

  function warn(m) { try { logger?.warn?.('[GG discord] ' + m); } catch (_e) { /* optional */ } }

  function emit(what) {
    for (const fn of Array.from(listeners)) {
      try { fn(what, api); } catch (e) { warn('listener threw: ' + ((e && e.message) || e)); }
    }
  }

  function showOpponentAvatars() {
    if (!prefs || typeof prefs.get !== 'function') return true;   // no store = the default, which is ON
    try { return prefs.get('showOpponentAvatars') !== false; } catch (_e) { return true; }
  }

  /* ------------------------------------------------------- inbound verbs */

  function onDiscordFrame(m) {
    if (disposed) return;
    state = normalizeState(m);
    // `discord` carries no lastOpponent by contract (§4) — only `init` does, and
    // the host rewrites the record itself on match-result. Leave ours alone.
    emit('state');
  }

  function onPeerCardFrame(m) {
    if (disposed) return;
    const card = normalizePeerCard(m);
    if (!card) return;
    if (card.ver) askedVer = card.ver;      // never re-ask for a version we hold
    // The viewer pref is enforced on ARRIVAL as well as on request: it can be
    // flipped off between the two, and a card that landed after that must not
    // repaint a face onto the desk.
    peer = showOpponentAvatars() ? card : Object.assign({}, card, { avatarDataUri: null });
    emit('peer');
  }

  try {
    subscribeBridge('discord', onDiscordFrame);
    subscribeBridge('peer-card', onPeerCardFrame);
  } catch (e) {
    // A duplicate registration is a WIRING bug and the loader is right to shout,
    // but it must not be a white page: the panel degrades to defaults instead.
    warn('bridge wiring failed: ' + ((e && e.message) || e));
  }

  /* ------------------------------------------------------ outbound verbs */

  const api = {
    get hosted() { return isHosted; },
    get state() { return Object.assign({}, state); },
    get lastOpponent() { return lastOpponent ? Object.assign({}, lastOpponent) : null; },
    get peer() { return peer ? Object.assign({}, peer) : null; },
    get linked() { return state.avatarState !== 'unlinked'; },
    get sharingAvatar() { return state.avatarState === 'shared'; },
    get sharingDm() { return !!state.dmShared; },
    get richPresence() { return !!state.richPresence; },
    get anySharing() { return api.sharingAvatar || api.sharingDm; },
    get rpState() { return rp; },
    get askedVer() { return askedVer; },
    get counters() { return Object.assign({}, counters); },
    get showOpponentAvatars() { return showOpponentAvatars(); },

    /** THE one-time first-duel confirm's condition (§1). */
    needsSharePrompt() { return api.anySharing && !state.seenSharePrompt; },

    /** boot.js hands us `init.discord` — the only frame that carries lastOpponent. */
    applyInit(raw) {
      const d = (raw && typeof raw === 'object') ? raw : null;
      state = normalizeState(d);
      lastOpponent = normalizeLastOpponent(d && d.lastOpponent);
      emit('init');
      return api;
    },

    /** fn(what, api) on every state / peer / lastOpponent change. */
    subscribe(fn) {
      if (typeof fn !== 'function') return () => {};
      listeners.add(fn);
      return () => listeners.delete(fn);
    },

    /**
     * `discord-prefs` — a REQUEST. Nothing local moves; the panel repaints when
     * (and only when) the host echoes `discord` back.
     * @param {{shareAvatar?:boolean, shareDm?:boolean, richPresence?:boolean, seenSharePrompt?:boolean}} partial
     */
    writePrefs(partial) {
      const p = (partial && typeof partial === 'object') ? partial : {};
      const msg = { type: 'discord-prefs' };
      if (p.shareAvatar !== undefined) msg.shareAvatar = !!p.shareAvatar;
      if (p.shareDm !== undefined) msg.shareDm = !!p.shareDm;
      if (p.richPresence !== undefined) msg.richPresence = !!p.richPresence;
      if (p.seenSharePrompt !== undefined) msg.seenSharePrompt = !!p.seenSharePrompt;
      // A frame with no fields would be a write of nothing that still costs an
      // echo and a settings save.
      if (Object.keys(msg).length < 2) return false;
      counters.prefsWrites++;
      try { post(msg); } catch (_e) { /* host gone */ }
      return true;
    },

    /**
     * The peer's card version moved. Fire-and-forget, at most one request per
     * version, and never at all with the viewer pref off (§1: OFF suppresses the
     * FETCH, not merely the render).
     *
     * The request carries {code, token, role} because /v2/goon/peercard is
     * room-authed and the page is the only side holding the room credentials
     * (see the header). No room, no request — a 403 would look identical to a
     * peer who shares nothing, and the difference matters in a support log.
     * @returns {{requested:boolean, reason:string}}
     */
    notePeerCardVer(ver) {
      const v = (typeof ver === 'string' && ver) ? ver : null;
      if (!v) return { requested: false, reason: 'none' };       // peer shares nothing
      if (!showOpponentAvatars()) return { requested: false, reason: 'pref-off' };
      if (v === askedVer) return { requested: false, reason: 'same' };
      const r = room();
      if (!r) return { requested: false, reason: 'no-room' };
      // Marked asked BEFORE the post, so a host that answers synchronously in a
      // test cannot re-enter this and double-request the same version.
      askedVer = v;
      counters.cardReqs++;
      try { post({ type: 'peer-card-req', code: r.code, token: r.token, role: r.role }); }
      catch (_e) { /* host gone */ }
      return { requested: true, reason: 'sent' };
    },

    /**
     * Watch a GoonSignalingClient for the `peer_card_ver` it sees on /join and
     * every /signal poll. The client is per-session, so boot re-arms this on
     * every match; the returned unsubscribe is put on the match's ledger.
     */
    watchSignaling(signaling) {
      if (!signaling || typeof signaling.onPeerCard !== 'function') return () => {};
      try {
        return signaling.onPeerCard((ver) => { try { api.notePeerCardVer(ver); } catch (_e) { /* ignore */ } });
      } catch (_e) { return () => {}; }
    },

    /** `discord-open-dm {which}` — the host owns the id, always. */
    openDm(which) {
      const w = which === 'last' ? 'last' : 'peer';
      counters.dmOpens++;
      try { post({ type: 'discord-open-dm', which: w }); } catch (_e) { /* host gone */ }
      return w;
    },

    /**
     * `discord-link-request {}` — restore the window and show the Discord tab.
     * It MUST NOT start OAuth and this end never asks it to: the verb carries no
     * arguments precisely so it cannot grow one.
     */
    linkRequest() {
      counters.linkReqs++;
      try { post({ type: 'discord-link-request' }); } catch (_e) { /* host gone */ }
      return true;
    },

    /**
     * `last-opponent-clear {}`. This ONE write is optimistic on purpose: the
     * player asked to forget someone, and a card that sits there for a round
     * trip afterwards reads as a refusal. There is nothing to be wrong about —
     * the local copy is a cache of a record the host is deleting.
     */
    clearLastOpponent() {
      lastOpponent = null;
      counters.clears++;
      try { post({ type: 'last-opponent-clear' }); } catch (_e) { /* host gone */ }
      emit('lastOpponent');
      return true;
    },

    /** `rp-state {s}` — enum only, and never the same value twice in a row. */
    setRpState(s) {
      const v = String(s || '');
      if (RP_STATES.indexOf(v) < 0) return false;
      if (v === rp) return false;
      rp = v;
      counters.rpPosts++;
      try { post({ type: 'rp-state', s: v }); } catch (_e) { /* host gone */ }
      return true;
    },

    /** Practice mode's opponent: a name, a tile, and no DM (§6). */
    setSoloPeer(name) {
      peer = {
        name: String(name || S.discord.practiceBot).slice(0, 32),
        avatarDataUri: null,
        reason: 'none',
        dm: false,
        ver: null,
      };
      emit('peer');
      return api.peer;
    },

    /** A match ended / a new one is starting: forget whose face was on the desk. */
    clearPeer() {
      if (!peer) return false;
      peer = null;
      askedVer = null;
      emit('peer');
      return true;
    },

    dispose() {
      disposed = true;
      listeners.clear();
    },
  };

  return api;
}

/* ============================================================================
 * THE LOBBY PANEL
 *
 * A card of its own under the duel card, because it is not part of the terms:
 * nothing here is negotiated, countersigned or sent on the wire, and putting it
 * inside the consent sheet would imply it was. It is prominent because the
 * decision it holds — "a stranger is about to see my face" — is the one thing
 * on this screen a player must not discover afterwards.
 * ==========================================================================*/

/**
 * @param {object} o
 * @param {object} o.discord   createDiscord handle
 * @param {object} o.ledger    the screen's teardown ledger (router.js)
 * @param {object} [o.prefs]
 * @param {object} [o.audio]
 * @param {string} [o.youName] the local display name, for the tile letter
 * @returns {{node:HTMLElement, paint:Function}}
 */
export function buildDiscordSection({ discord, ledger, prefs = null, audio = null, youName = '' } = {}) {
  const meSlot = avatarSlot({ side: 'you', name: youName, dataUri: null, size: 'lobby' });
  const shareFlag = el('p', { class: 'gg-dc-flag', text: S.discord.visible, hidden: true });

  const head = el('div', { class: 'gg-dc-head' }, [
    meSlot.node,
    el('div', { class: 'gg-dc-headtext' }, [
      el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.discord.eyebrow })]),
      el('p', { class: 'gg-dc-lead', text: S.discord.lead }),
      shareFlag,
    ]),
  ]);

  /** The house toggle (ui/options.js idiom): a button that RENDERS a value. */
  function toggleRow(label, note, read, write) {
    const b = el('button', { type: 'button', class: 'gg-toggle', 'aria-pressed': 'false' });
    b.appendChild(el('i'));
    ledger.listen(b, 'click', () => {
      if (b.disabled) return;
      try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
      // NOT a local flip: write the OPPOSITE of what the echo currently says and
      // wait to be told. paint() runs again when the `discord` frame lands.
      write(!read());
    });
    const row = el('div', { class: 'gg-row gg-dc-row' }, [
      el('span', { class: 'gg-row-label', text: label }),
      b,
    ]);
    const sub = el('p', { class: 'gg-row-sub', text: note });
    return {
      row, sub, button: b,
      paint(enabled) {
        try { b.setAttribute('aria-pressed', String(!!read())); } catch (_e) { /* stub */ }
        b.disabled = !enabled;
        row.classList.toggle('is-disabled', !enabled);
      },
    };
  }

  const avatarToggle = toggleRow(S.discord.toggleAvatar, S.discord.toggleAvatarNote,
    () => discord.sharingAvatar,
    (v) => discord.writePrefs({ shareAvatar: v }));
  const dmToggle = toggleRow(S.discord.toggleDm, S.discord.toggleDmNote,
    () => discord.sharingDm,
    (v) => discord.writePrefs({ shareDm: v }));
  const rpToggle = toggleRow(S.discord.toggleRp, S.discord.toggleRpNote,
    () => discord.richPresence,
    (v) => discord.writePrefs({ richPresence: v }));

  /* --- the unlinked state. A CTA, and it starts NOTHING (§4 banned verbs):
   * it asks the app to come to the front on the Discord tab, and the player
   * signs in there, with the app's own consent copy in front of them. */
  const linkBtn = button(ledger, S.discord.connectCta, () => { discord.linkRequest(); },
    { variant: 'primary', audio });
  const linkLine = el('p', { class: 'gg-dc-linkline', text: S.discord.connectLine });
  const linkBox = el('div', { class: 'gg-dc-link' }, [linkLine, linkBtn]);

  /* --- last opponent ------------------------------------------------------ */
  const lastBox = el('div', { class: 'gg-dc-last' });

  function paintLast() {
    const last = discord.lastOpponent;
    lastBox.replaceChildren();
    lastBox.appendChild(el('h3', { class: 'gg-dc-lasth', text: S.discord.lastTitle }));
    if (!last) {
      lastBox.appendChild(el('p', { class: 'gg-dc-lastnone', text: S.discord.lastNone }));
      return;
    }
    const ava = avatarNode({ side: 'opp', name: last.name, dataUri: last.avatarDataUri, size: 'last' });
    const clear = el('button', {
      type: 'button',
      class: 'gg-dc-forget',
      'aria-label': S.discord.lastClear,
      title: S.discord.lastClear,
      text: '✕',
    });
    ledger.listen(clear, 'click', () => {
      try { audio?.sfx?.('ui-back'); } catch (_e) { /* stub bus */ }
      discord.clearLastOpponent();
    });

    const meta = el('div', { class: 'gg-dc-lastmeta' }, [
      el('p', { class: 'gg-dc-lastname', text: last.name }),
      el('p', { class: 'gg-dc-lastwhen', text: relativeTime(last.ts, Date.now()) }),
    ]);
    // The Message button exists ONLY when THEY shared DMs. No flag, no button —
    // never a button that explains why it cannot work.
    if (last.dm) {
      const dm = button(ledger, S.discord.messageOn(last.name), () => { discord.openDm('last'); },
        { variant: 'discord', audio });
      meta.appendChild(dm);
    }
    lastBox.appendChild(el('div', { class: 'gg-dc-lastcard' }, [ava, meta, clear]));
  }

  const card = el('section', { class: 'gg-card gg-dc' }, [
    head,
    el('div', { class: 'gg-dc-rows' }, [
      avatarToggle.row, avatarToggle.sub,
      dmToggle.row, dmToggle.sub,
      rpToggle.row, rpToggle.sub,
    ]),
    linkBox,
    lastBox,
  ]);

  function paint() {
    const linked = discord.linked;
    const hosted = discord.hosted;
    // Standalone there is no app to write settings into and no account to read,
    // so the panel SHOWS (it is the only way to look at it during dev) with its
    // controls inert and one line saying why.
    const live = hosted && linked;

    meSlot.setName(youName || S.discord.you);
    meSlot.setPicture(discord.sharingAvatar ? discord.state.avatarDataUri : null);
    shareFlag.hidden = !discord.sharingAvatar;

    avatarToggle.paint(live);
    dmToggle.paint(live);
    // Rich presence needs the app, not the link: it is a local switch on the
    // app's own Discord connection, so it stays offerable to a linked-less
    // hosted player exactly as the app offers it.
    rpToggle.paint(hosted);

    linkBox.hidden = live;
    linkLine.textContent = hosted ? S.discord.connectLine : S.discord.hostedOnly;
    linkBtn.disabled = !hosted;
    paintLast();
  }

  ledger.add(discord.subscribe(() => { try { paint(); } catch (_e) { /* a repaint must not break the lobby */ } }));
  paint();
  return { node: card, paint };
}

/* ----------------------------------------------------------------------------
 * THE ONE-TIME FIRST-DUEL CONFIRM (§1).
 *
 * Shown once, before the first match a player enters with any sharing flag on.
 * It is NOT dismissible: a scrim click that quietly meant "yes" would be the
 * worst possible reading of a consent sheet. It can still be swept — boot's
 * closeChrome() closes every sheet at Countdown and at Recap — and a swept
 * sheet resolves null, which this treats as "nothing agreed, nothing written".
 * -------------------------------------------------------------------------- */

/**
 * @param {object} o
 * @param {object} o.discord
 * @param {object} o.sheets   ui/sheets.js handle
 * @returns {Promise<'confirm'|'decline'|null>}
 */
export async function askSharePrompt({ discord, sheets } = {}) {
  if (!discord || !sheets || typeof sheets.open !== 'function') return null;
  if (!discord.needsSharePrompt()) return null;

  const picked = await sheets.open({
    icon: S.discord.sharePrompt.icon,
    headline: S.discord.sharePrompt.headline,
    line: S.discord.sharePrompt.line,
    dismissible: false,
    actions: [
      { id: 'decline', label: S.discord.sharePrompt.cancel, variant: 'ghost' },
      { id: 'confirm', label: S.discord.sharePrompt.go, variant: 'primary' },
    ],
  });

  if (picked === 'confirm') {
    discord.writePrefs({ seenSharePrompt: true });
    return 'confirm';
  }
  if (picked === 'decline') {
    // Declining turns the sharing OFF rather than remembering that it was
    // refused: "no" here means no, and with both flags down needsSharePrompt()
    // is false anyway, so the sheet does not come back to nag.
    discord.writePrefs({ shareAvatar: false, shareDm: false });
    return 'decline';
  }
  return null;
}

/**
 * The confirm in front of every "message them on Discord". Opening a browser
 * out of a fullscreen duel is a big, surprising thing to do to somebody.
 * @returns {Promise<boolean>} true if it was opened
 */
export async function confirmOpenDm({ discord, sheets, name = '', which = 'peer' } = {}) {
  if (!discord) return false;
  if (!sheets || typeof sheets.open !== 'function') { discord.openDm(which); return true; }
  const picked = await sheets.open({
    icon: S.discord.dmConfirm.icon,
    headline: S.discord.dmConfirm.headline,
    line: S.discord.dmConfirm.line(name),
    actions: [
      { id: 'cancel', label: S.discord.dmConfirm.cancel, variant: 'ghost' },
      { id: 'go', label: S.discord.dmConfirm.go, variant: 'primary' },
    ],
  });
  if (picked !== 'go') return false;
  discord.openDm(which);
  return true;
}

export default createDiscord;
