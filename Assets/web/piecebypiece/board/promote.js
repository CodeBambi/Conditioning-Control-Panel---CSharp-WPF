// Promotion stays a real choice, including under time pressure. The clock runs
// normally and the pawn stays on its original square until a piece is chosen.
// The sprite fan and keyboard-accessible buttons offer the same four choices.

import * as THREE from 'three';
import { squareToWorld } from './scene.js';

/** Every number the offer is made of. One place, on purpose. */
export const TUNING = Object.freeze({
  size: 0.62,          // sprite size in world units; a square is 1.0
  rise: 0.55,          // how far the fan climbs out of the square
  base: 0.34,          // and where it starts, over the board
  spread: 0.92,        // how wide the fan opens, in squares
  arc: 0.34,           // how much the outer two sit higher than the inner two
  riseSec: 0.22,       // how long the fan takes to come up
  hoverScale: 1.22,    // the one under the cursor
  hoverDim: 0.62,      // ... and how far the others fall back
  ease: 14,            // per second, the chase on both of those
  stretch: -1.9,       // the pawn holds himself tall while you decide
  stretchEvery: 0.5,   // s, topped up so the hold does not sag
  px: 128,
  ink: '#241A3B',
  white: '#F5E6C8',
  black: '#AB90FF',
});

const T = TUNING;
const KINDS = ['q', 'r', 'b', 'n'];
const WHITE = { q: '♕', r: '♖', b: '♗', n: '♘' };
const BLACK = { q: '♛', r: '♜', b: '♝', n: '♞' };
const LETTER = { q: 'Q', r: 'R', b: 'B', n: 'N' };
const NAMES = { q: 'Queen', r: 'Rook', b: 'Bishop', n: 'Knight' };
const FONT = '"Segoe UI Symbol", "Segoe UI", "DejaVu Sans", "Arial Unicode MS", system-ui, sans-serif';

function reducedMotion() {
  if (typeof window === 'undefined') return false;
  const pbp = window.PBP;
  if (pbp && (pbp.reducedMotion || (pbp.settings && pbp.settings.reducedMotion))) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

/** Is this figure in the fonts we asked for? A missing one is drawn as a box. */
function has(ctx, ch) {
  const tofu = ctx.measureText('\u{10FFFF}').width;
  const w = ctx.measureText(ch).width;
  return w > 0 && Math.abs(w - tofu) > 0.1;
}

function glyphTexture(kind, side) {
  const px = T.px;
  const canvas = document.createElement('canvas');
  canvas.width = px; canvas.height = px;
  const ctx = canvas.getContext('2d');
  ctx.font = Math.round(px * 0.72) + 'px ' + FONT;
  const wanted = (side === 'w' ? WHITE : BLACK)[kind];
  const ch = has(ctx, wanted) ? wanted : LETTER[kind];
  if (ch !== wanted) ctx.font = '700 ' + Math.round(px * 0.58) + 'px ' + FONT;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.lineJoin = 'round';
  ctx.shadowColor = 'rgba(20, 14, 40, 0.55)';
  ctx.shadowBlur = px * 0.10;
  ctx.shadowOffsetY = px * 0.03;
  ctx.lineWidth = px * 0.085;
  ctx.strokeStyle = T.ink;
  ctx.strokeText(ch, px / 2, px * 0.53);
  ctx.shadowColor = 'transparent';
  ctx.fillStyle = side === 'w' ? T.white : T.black;
  ctx.fillText(ch, px / 2, px * 0.53);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 2;
  return tex;
}

/**
 * @param view    what createScene handed back
 * @param pieces  what createPieces handed back
 * @param anim    for the new man's landing
 * @param game    the hotseat: rules, clocks and tryMove
 * @param drag    so the board can be stood down while the fan is open
 */
export function createPromote({ view, pieces, anim, bus, game, drag = null, jiggle = null }) {
  const canvas = view.renderer.domElement;
  const group = new THREE.Group();
  group.name = 'promote';
  group.renderOrder = 950;
  view.scene.add(group);

  const textures = new Map();       // "kind:side" -> CanvasTexture
  const ghosts = [];                // { kind, sprite, want, live }
  const ray = new THREE.Raycaster();
  const ndc = new THREE.Vector2();
  let open = null;                  // { from, to, side, at, t, pawn }
  let hoverKind = null;
  let stretchIn = 0;
  let previousFocus = null;
  const chooser = document.createElement('div');
  chooser.setAttribute('role', 'dialog');
  chooser.setAttribute('aria-modal', 'true');
  chooser.setAttribute('aria-label', 'Promote pawn');
  chooser.hidden = true;
  chooser.style.cssText = 'position:fixed;z-index:1600;left:50%;bottom:24px;transform:translateX(-50%);max-width:calc(100% - 24px);padding:16px;border:1px solid #ad8edf;border-radius:16px;background:#21182f;color:#fff;box-shadow:0 12px 48px #0009;text-align:center;font:14px system-ui';
  const heading = document.createElement('strong');
  heading.textContent = 'Promote pawn';
  chooser.append(heading);
  const choices = document.createElement('div');
  choices.style.cssText = 'display:flex;gap:8px;flex-wrap:wrap;justify-content:center;margin:12px 0';
  const buttons = KINDS.map(kind => {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = `${NAMES[kind]} (${LETTER[kind]})`;
    button.setAttribute('aria-keyshortcuts', LETTER[kind]);
    button.style.cssText = 'padding:10px 14px;border:1px solid #ae90d9;border-radius:10px;background:#382848;color:#fff;font:inherit;cursor:pointer';
    button.addEventListener('click', () => choose(kind));
    button.addEventListener('focus', () => { hoverKind = kind; });
    choices.append(button);
    return button;
  });
  chooser.append(choices);
  const hint = document.createElement('div');
  hint.textContent = 'Choose a piece. Esc cancels. Your clock keeps running.';
  hint.style.cssText = 'color:#d6c8e8;font-size:12px';
  chooser.append(hint);
  document.body.append(chooser);

  function textureFor(kind, side) {
    const key = kind + ':' + side;
    if (!textures.has(key)) textures.set(key, glyphTexture(kind, side));
    return textures.get(key);
  }

  function ghostFor(kind, side) {
    const sprite = new THREE.Sprite(new THREE.SpriteMaterial({
      map: textureFor(kind, side), transparent: true, depthTest: false,
      depthWrite: false, fog: false, sizeAttenuation: true, opacity: 0,
    }));
    sprite.renderOrder = 950;
    sprite.userData.promoteKind = kind;
    group.add(sprite);
    return sprite;
  }

  /** Where ghost i sits over the square, fully out. */
  function seat(i, at) {
    const t = (i - (KINDS.length - 1) / 2) / ((KINDS.length - 1) / 2);   // -1 .. 1
    return {
      x: at.x + t * T.spread * 0.5,
      y: T.base + T.rise - Math.abs(t) * T.arc,
      z: at.z,
    };
  }

  /**
   * Is this move a promotion, and does it get to ask? Returns true when the
   * fan has taken the move over, which is the caller's cue to do nothing.
   */
  function intercept(from, to) {
    if (open) return true;                      // one offer at a time
    const move = game.rules.legalMove(from, to);
    if (!move || !String(move.flags || '').includes('p')) return false;
    const side = game.rules.turn();
    // Keep the rules and piece map untouched until the choice is committed.
    const pawn = pieces.pieceAt(from);
    const at = squareToWorld(to, 0);
    if (pawn) anim?.slide?.(pawn, pawn.position.clone(), squareToWorld(from, 0), .08, { dur: .16 });
    open = { from, to, side, at, t: 0, pawn };
    previousFocus = document.activeElement;
    chooser.hidden = false;
    buttons[0].focus({ preventScroll: true });
    hoverKind = null;
    stretchIn = 0;
    if (drag && drag.suspend) drag.suspend(true);
    for (const kind of KINDS) ghosts.push({ kind, sprite: ghostFor(kind, side), want: 1, live: 0 });
    bus.emit('promote', { from, to, side, choosing: true });
    return true;
  }

  /** The normal move hooks preserve the capture and transfer it to the new sculpt. */
  function choose(kind) {
    if (!open || !KINDS.includes(kind)) return null;
    const { from, to } = open;
    close();
    return game.tryMove(from, to, kind);
  }

  function close() {
    if (!open) return;
    for (const g of ghosts) {
      group.remove(g.sprite);
      g.sprite.material.dispose();
    }
    ghosts.length = 0;
    const offer = open;
    open = null;
    hoverKind = null;
    chooser.hidden = true;
    canvas.style.cursor = '';
    if (previousFocus?.isConnected) previousFocus.focus?.({ preventScroll: true });
    previousFocus = null;
    bus.emit('promote', { from: offer.from, to: offer.to, side: offer.side, choosing: false });
    if (drag && drag.suspend) drag.suspend(false);
  }

  function aim(ev) {
    if (!open) return null;
    const r = canvas.getBoundingClientRect();
    ndc.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(ndc, view.camera);
    const hit = ray.intersectObjects(ghosts.map((g) => g.sprite), false)[0];
    return hit ? hit.object.userData.promoteKind : null;
  }

  function onMove(ev) {
    if (!open) return;
    hoverKind = aim(ev);
    canvas.style.cursor = hoverKind ? 'pointer' : 'default';
  }

  function onDown(ev) {
    if (!open || ev.button > 0) return;
    ev.preventDefault();
    ev.stopPropagation();
    const kind = aim(ev);
    if (kind) choose(kind); else close();
  }

  function onKey(ev) {
    if (!open || ev.altKey || ev.ctrlKey || ev.metaKey) return;
    const key = ev.key.toLowerCase();
    if (!['escape', 'tab', 'arrowleft', 'arrowright', 'arrowup', 'arrowdown', ...KINDS].includes(key)) return;
    ev.preventDefault();
    ev.stopImmediatePropagation();
    if (key === 'escape') { close(); return; }
    if (KINDS.includes(key)) { choose(key); return; }
    const current = Math.max(0, buttons.indexOf(document.activeElement));
    const backwards = key === 'arrowleft' || key === 'arrowup' || (key === 'tab' && ev.shiftKey);
    buttons[(current + (backwards ? -1 : 1) + buttons.length) % buttons.length].focus();
  }

  function update(dt) {
    if (!open) return;
    open.t += dt;
    const still = reducedMotion();
    const up = still ? 1 : Math.min(1, open.t / T.riseSec);
    const k = still ? 1 : 1 - Math.exp(-T.ease * dt);
    for (let i = 0; i < ghosts.length; i++) {
      const g = ghosts[i];
      const s = seat(i, open.at);
      g.sprite.position.set(s.x, T.base + (s.y - T.base) * up, s.z);
      const want = hoverKind && g.kind !== hoverKind ? T.hoverDim : 1;
      g.live += (want - g.live) * k;
      g.sprite.material.opacity = up * g.live;
      g.sprite.scale.setScalar(T.size * (g.kind === hoverKind ? T.hoverScale : 1));
    }
    // He holds himself tall while you decide, topped up so the hold does not sag.
    if (!still && jiggle && open.pawn) {
      stretchIn -= dt;
      if (stretchIn <= 0) { stretchIn = T.stretchEvery; jiggle.impulse(open.pawn, { squash: T.stretch }); }
    }
  }

  canvas.addEventListener('pointermove', onMove, true);
  canvas.addEventListener('pointerdown', onDown, true);
  window.addEventListener('keydown', onKey, true);
  const off = ['gameover', 'newgame', 'local', 'resync'].map(name => bus.on(name, close));

  return {
    intercept, choose, close, update,
    isOpen: () => !!open,
    /** For the harness: what is on offer and which one the cursor is on. */
    debug() {
      return {
        open: open ? open.to : null,
        side: open ? open.side : null,
        kinds: ghosts.map((g) => g.kind),
        hover: hoverKind,
        rise: open ? Number((ghosts[0] ? ghosts[0].sprite.position.y : 0).toFixed(3)) : 0,
        held: open ? Number(open.t.toFixed(2)) : 0,
      };
    },
    dispose() {
      close();
      for (const stop of off) stop();
      chooser.remove();
      canvas.removeEventListener('pointermove', onMove, true);
      canvas.removeEventListener('pointerdown', onDown, true);
      window.removeEventListener('keydown', onKey, true);
      for (const tex of textures.values()) tex.dispose();
      textures.clear();
      view.scene.remove(group);
    },
  };
}
