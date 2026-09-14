// The doors: three ways out of the room and into the rest of the building. A door is a poster with a
// shade, an eyebrow, a title, a second line and a chevron. Desktop hovers it and the clip runs inline;
// a phone taps it and a bottom sheet comes up with the clip and one button. Nothing opens by itself,
// nothing waits for you, and the room works the same with every door ignored.
//
// Two placements share this one card: the deck on the Roll page (one door, three dots, the next door
// on every roll) and the strip under the export sheet once a save has landed.

const BASE = 'assets/doors/';

export const DOORS = [
  { id: 'loom', eyebrow: 'free, right now', title: 'The Loom', line: 'spin a spiral of your own',
    button: 'Try the Loom', href: 'https://cclabs.app/loom/?from=remix' },
  { id: 'intake', eyebrow: 'free weekly, with an account', title: 'The Intake', line: 'join the CCP and take your weekly intake',
    button: 'Take the intake', href: 'https://app.cclabs.app/intake?from=remix' },
  { id: 'desktop', eyebrow: 'free download', title: 'Conditioning Control Panel', line: 'your whole screen, not one gif',
    button: 'Get the app', href: 'https://cclabs.app/?from=remix' },
];

export const clipSrc = door => `${BASE}${door.id}.mp4`;
export const posterSrc = door => `${BASE}${door.id}.webp`;
export const doorById = id => DOORS.find(d => d.id === id) || null;

// "not now" on the after-save strip: gone for the session, and a locked storage jar is not an error
export const NOT_NOW_KEY = 'remix.doors.notnow';
export const doorsDismissed = () => { try { return sessionStorage.getItem(NOT_NOW_KEY) === '1'; } catch { return false; } };
export const dismissDoors = () => { try { sessionStorage.setItem(NOT_NOW_KEY, '1'); } catch { /* no jar, no memory */ } };
export const AFTER_LINE = 'Your gif carries the address. This is where it leads.';

const CHEV = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 5 7 7-7 7"/></svg>';
const CLOSE = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12"/><path d="M18 6 6 18"/></svg>';
const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
const el = (tag, cls) => { const n = document.createElement(tag); if (cls) n.className = cls; return n; };

/* ------------------------------------------------------------------ card */
// One door. The clip has no src until the first hover or focus, so a door that is never touched costs
// the poster and nothing else. The poster stays under the video and comes back when the clip pauses.
export function doorCard(door, ctx, { compact = false } = {}) {
  const a = el('a', 'door' + (compact ? ' compact' : ''));
  a.href = door.href; a.target = '_blank'; a.rel = 'noopener';
  a.dataset.door = door.id;
  a.setAttribute('aria-label', `${door.title}: ${door.line}`);
  const poster = el('img', 'door-poster');
  poster.src = posterSrc(door); poster.alt = ''; poster.loading = 'lazy'; poster.decoding = 'async';
  const shade = el('i', 'door-shade'); shade.setAttribute('aria-hidden', 'true');
  const tx = el('div', 'door-tx');
  const ey = el('span', 'door-ey'); ey.textContent = door.eyebrow;
  const ti = el('span', 'door-ti'); ti.textContent = door.title;
  const su = el('span', 'door-su'); su.textContent = door.line;
  tx.append(ey, ti, su);
  const chev = el('i', 'door-chev'); chev.innerHTML = CHEV; chev.setAttribute('aria-hidden', 'true');
  a.append(poster, shade, tx, chev);

  let video = null;
  const clip = () => {
    if (video) return video;
    video = el('video', 'door-clip');
    video.muted = true; video.loop = true; video.playsInline = true; video.preload = 'none';
    video.setAttribute('muted', ''); video.setAttribute('playsinline', ''); video.setAttribute('aria-hidden', 'true');
    video.src = clipSrc(door);
    a.insertBefore(video, shade);
    return video;
  };
  const start = () => {
    if (reduced() || isMobile(ctx)) return; // motion off: the poster is the whole card
    const v = clip();
    a.classList.add('playing');
    v.play?.().catch(() => { a.classList.remove('playing'); }); // a blocked clip just leaves the poster up
  };
  const stop = () => { a.classList.remove('playing'); try { video?.pause(); } catch { /* fine */ } };
  a.addEventListener('pointerenter', e => { if (e.pointerType !== 'touch') start(); });
  a.addEventListener('pointerleave', stop);
  a.addEventListener('focus', start);
  a.addEventListener('blur', stop);

  // a phone opens the sheet instead of the tab: the clip first, the button after
  a.addEventListener('click', e => {
    if (!isMobile(ctx)) return;
    e.preventDefault();
    openDoorSheet(door, ctx);
  });
  return a;
}

const isMobile = ctx => (ctx?.isMobile ? ctx.isMobile() : document.documentElement.classList.contains('is-mobile'));

/* ----------------------------------------------------------------- sheet */
// The phone sheet, in the room's own sheet style: the clip playing, the words, one pink button, a
// close. The scrim closes it. The clip runs because a tap asked for it, reduced motion included.
export function openDoorSheet(door, ctx) {
  closeDoorSheet();
  const host = el('div', 'sheet-host door-host');
  host.dataset.door = door.id;
  const sheet = el('div', 'sheet door-sheet');
  sheet.setAttribute('role', 'dialog'); sheet.setAttribute('aria-modal', 'true'); sheet.setAttribute('aria-label', door.title);
  const grab = el('div', 'sheet-grab'); grab.setAttribute('aria-hidden', 'true');
  const head = el('div', 'sheet-head');
  const title = el('div', 'sheet-title'); title.textContent = door.title;
  const x = el('button', 'sheet-close'); x.type = 'button'; x.innerHTML = CLOSE; x.setAttribute('aria-label', 'Close');
  head.append(title, x);
  const box = el('div', 'door-sheet-box');
  const v = el('video', 'door-sheet-clip');
  v.muted = true; v.loop = true; v.playsInline = true; v.autoplay = true;
  v.setAttribute('muted', ''); v.setAttribute('playsinline', ''); v.setAttribute('aria-hidden', 'true');
  v.poster = posterSrc(door); v.src = clipSrc(door);
  box.appendChild(v);
  const line = el('div', 'door-sheet-line'); line.textContent = door.line;
  const go = el('a', 'btn-big primary door-sheet-go');
  go.href = door.href; go.target = '_blank'; go.rel = 'noopener'; go.textContent = door.button;
  sheet.append(grab, head, box, line, go);
  host.appendChild(sheet);
  document.body.appendChild(host);
  x.addEventListener('click', closeDoorSheet);
  host.addEventListener('pointerdown', e => { if (e.target === host) closeDoorSheet(); });
  go.addEventListener('click', () => setTimeout(closeDoorSheet, 0));
  v.play?.().catch(() => { /* the poster holds the frame */ });
  setTimeout(() => { try { go.focus({ preventScroll: true }); } catch { /* fine */ } }, 0);
  ctx?.emit?.('doorsheet', door.id);
  return host;
}

export function closeDoorSheet() {
  const host = document.querySelector('.door-host');
  if (!host) return false;
  host.querySelector('video')?.pause();
  host.remove();
  return true;
}

export const doorSheetOpen = () => !!document.querySelector('.door-host');

/* ------------------------------------------------------------------ deck */
// One door at a time with three dots under it. Every roll walks to the next one, with a 200 ms
// crossfade, or a cut if motion is off. It starts on the loom and never asks for anything.
export function mountDeck(host, ctx) {
  if (!host) return null;
  let i = 0, fade = 0;
  host.classList.add('door-deck');
  const slot = el('div', 'door-slot');
  const dots = el('div', 'door-dots'); dots.setAttribute('aria-hidden', 'true');
  for (const d of DOORS) { const dot = el('i'); dot.dataset.door = d.id; dots.appendChild(dot); }
  host.append(slot, dots);

  function paint(fadeIn) {
    const card = doorCard(DOORS[i], ctx);
    if (fadeIn) {
      const old = slot.firstElementChild;
      card.classList.add('door-in');
      slot.appendChild(card);
      if (old) {
        old.classList.add('door-out');
        clearTimeout(fade);
        fade = setTimeout(() => old.remove(), 220);
      }
    } else {
      slot.innerHTML = ''; slot.appendChild(card);
    }
    dots.querySelectorAll('i').forEach((dot, n) => dot.classList.toggle('on', n === i));
  }
  function advance(step = 1) {
    i = (i + step + DOORS.length) % DOORS.length;
    paint(!reduced());
  }
  paint(false);
  const off = ctx.on('roll', () => advance());
  return { el: host, advance, current: () => DOORS[i], destroy: () => { off?.(); clearTimeout(fade); host.innerHTML = ''; } };
}

/* ----------------------------------------------------------------- strip */
// The after-save strip: the line, a quiet "not now", and the three doors in a row that scrolls
// sideways on a phone. Returns null once somebody has said not now, so the caller shows nothing.
export function doorStrip(ctx) {
  if (doorsDismissed()) return null;
  const wrap = el('div', 'door-after');
  const head = el('div', 'door-after-head');
  const line = el('span', 'door-after-line'); line.textContent = AFTER_LINE;
  const not = el('button', 'quiet-link door-notnow'); not.type = 'button'; not.textContent = 'not now';
  head.append(line, not);
  const row = el('div', 'door-row');
  for (const d of DOORS) row.appendChild(doorCard(d, ctx, { compact: true }));
  wrap.append(head, row);
  not.addEventListener('click', () => { dismissDoors(); wrap.remove(); });
  return wrap;
}
