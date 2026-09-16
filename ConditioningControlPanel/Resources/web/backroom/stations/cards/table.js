/* ============================================================================
 * stations/cards/table.js - the Soft Hand table, a 2D canvas filling the
 * station view (CONTRACT 10.13.F: the mockup's table, no three.js, no guessed
 * geometry).
 *
 * Draws the felt under a breathing lamp, the printed arc, the shoe, the chip
 * spot, the dealer's and the player's hands (split hands side by side), and
 * the page effects: Loom backs, your deck, ripple felt (Full), chip vortex,
 * win tunnel, ace glow, sit fan. Every spiral is the kit's Loom (kit.draw
 * 'backs'); every picture is the kit's deck (deck.draw). Nothing here fires a
 * host effect or reads the server: the station tells the table what to put
 * down and when.
 *
 * Strength: every page effect takes `k` (strengthK: 0.5 under Calm or reduced
 * motion) and `still` puts cards straight on their spots, face up at once,
 * with no flights. Gates dress: `flash` off draws plain faces, `spiral` off a
 * brass crosshatch back.
 * ==========================================================================*/

import { cardTilt, landing, flipLift, deckRecoil, touchdown } from './juice.js';
import { TIMING, fanCard, lampBreath } from './feel.js';
import { rankLabel, suitOf, totalOf } from './hand.js';
import { DECK_VALUES } from '../../shared/hypno/media.js';
import { HIGHLIGHT_MS, HIGHLIGHT_GAP_MS } from '../../shared/hypno/callout.js';

const TAU = Math.PI * 2;
const COL = { rose: '#ff5fa2', mint: '#5fffd0', brass: '#e8c27a', ink: '#1a0f2b', text: '#efe6ff', paper: '#f7f0fb' };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const ease = (p) => 1 - Math.pow(1 - p, 3);
const lerp = (a, b, t) => a + (b - a) * t;

function rrect(g, x, y, w, h, r) {
  g.beginPath(); g.moveTo(x + r, y); g.arcTo(x + w, y, x + w, y + h, r); g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r); g.arcTo(x, y, x + w, y, r); g.closePath();
}

/** Where everything sits on a w x h table (CSS px). Pure. */
export function tableLayout(w, h) {
  const cw = clamp(Math.min(w * 0.07, h * 0.125), 40, 92), ch = cw * 1.4;
  return { w, h, cw, ch, gap: cw * 0.64, shoe: { x: w * 0.85, y: h * 0.17 }, dealerY: h * 0.26, playerY: h * 0.66,
    spot: { x: w / 2, y: h * 0.86, r: Math.min(w, h) * 0.03 }, dealerSpot: { x: w / 2, y: h * 0.07 }, splitGap: Math.max(cw * 3.2, w * 0.26) };
}

/** A card's resting centre: dealer slots in a row, a player hand around its own centre. Pure. */
export function slotXY(L, owner, slot, count, hands = 1) {
  const off = (slot - (count - 1) / 2) * L.gap;
  if (owner === 'd') return [L.w / 2 + off, L.dealerY];
  const cx = hands === 2 ? L.w / 2 + (owner - 0.5) * L.splitGap : L.w / 2;
  return [cx + off, L.playerY];
}

/** @param {{kit?: Object | (() => Object)}} o  the Loom kit, or a getter (a station swaps its kit on suspend) */
export function createTable(canvas, { kit = null, onCue = () => {} } = {}) {
  const g = canvas.getContext('2d');
  const loom = () => (typeof kit === 'function' ? kit() : kit);
  let cards = [], seq = 0, hands = 1, active = -1, bets = [], betsShown = true;
  let fan = null, ripples = [], chips = [], tunnelAt = -1, glow = null, lastNow = 0;
  let deckAt = -Infinity;
  let hits = [];   // THE GLYPH HIT (callout.js): { id, t0 } per winning card, a mint rim and a 1.06 pop over HIGHLIGHT_MS
  const hitPulse = (c, now) => { const h = hits.find((x) => x.id === c.id); if (!h) return 0; const q = (now - h.t0) / HIGHLIGHT_MS; return q >= 0 && q < 1 ? Math.sin(q * Math.PI) : 0; };
  let W = 0, H = 0, D = 1, cache = { key: '', weave: null, print: null };
  const stats = { backs: 0, pictures: 0, fan: 0, frame: 0 };

  function fit() {
    const r = canvas.getBoundingClientRect();
    D = Math.min(globalThis.devicePixelRatio || 1, 1.5);
    W = Math.max(1, r.width); H = Math.max(1, r.height);
    const pw = Math.round(W * D), ph = Math.round(H * D);
    if (canvas.width !== pw || canvas.height !== ph) { canvas.width = pw; canvas.height = ph; }
    g.setTransform(D, 0, 0, D, 0, 0);
  }

  /** The weave and the printed arc never move: drawn once per size into offscreen canvases. */
  function statics(L, printText) {
    const key = W + 'x' + H + '@' + D + '|' + printText;
    if (cache.key === key) return cache;
    const mk = () => { const c = document.createElement('canvas'); c.width = Math.round(W * D); c.height = Math.round(H * D); const x = c.getContext('2d'); x.setTransform(D, 0, 0, D, 0, 0); return [c, x]; };
    const [weave, wg] = mk();
    weaveLines(wg, null, 0, 1);
    const [print, pg] = mk();
    pg.fillStyle = 'rgba(232,194,122,.55)'; pg.font = `500 ${Math.max(9, W * 0.012)}px Consolas, "DM Mono", monospace`;
    pg.textAlign = 'center'; pg.textBaseline = 'middle';
    const acx = W / 2, acy = -H * 0.5, ar = H * 0.98, chars = [...printText], widths = chars.map((c) => pg.measureText(c).width * 1.12);
    let cur = -widths.reduce((a, b) => a + b, 0) / 2;
    chars.forEach((c, i) => {
      const ang = Math.PI / 2 - (cur + widths[i] / 2) / ar;
      pg.save(); pg.translate(acx + Math.cos(ang) * ar, acy + Math.sin(ang) * ar); pg.rotate(ang - Math.PI / 2); pg.fillText(c, 0, 0); pg.restore();
      cur += widths[i];
    });
    cache = { key, weave, print };
    return cache;
  }
  function weaveLines(x, rip, now, k) {
    x.save(); x.strokeStyle = 'rgba(95,255,208,.10)'; x.lineWidth = 1; x.beginPath();
    for (let j = 0; j <= 24; j++) {
      const y0 = H * 0.04 + H * 0.92 * j / 24;
      for (let i = 0; i <= 64; i++) {
        const px = W * i / 64;
        let y = y0;
        if (rip) for (const r of rip) { const t = (now - r.t0) / 1000, d = Math.hypot(px - r.x, y0 - r.y) - t * 150; y += 7 * k * Math.exp(-t * 1.1) * Math.exp(-d * d / 2200) * Math.cos(d / 11); }
        if (i === 0) x.moveTo(px, y); else x.lineTo(px, y);
      }
    }
    x.stroke(); x.restore();
  }

  function drawCard(code, cw, ch, flipP, o) {
    g.scale(Math.max(Math.abs(Math.cos(Math.PI * flipP)), 0.02), 1);
    rrect(g, -cw / 2, -ch / 2, cw, ch, cw * 0.1);
    if (flipP >= 0.5 && code) {
      g.fillStyle = COL.paper; g.fill();
      const deck = o.deck, pic = o.gates.flash && deck && deck.draw;
      if (pic) {
        g.save(); rrect(g, -cw / 2 + 3, -ch / 2 + 3, cw - 6, ch - 6, cw * 0.08); g.clip();
        if (deck.draw(g, deck.keyFor(code), -cw / 2 + 3, -ch / 2 + 3, cw - 6, ch - 6, { alpha: 0.85 })) stats.pictures++;
        g.fillStyle = 'rgba(247,240,251,.3)'; g.fillRect(-cw / 2, -ch / 2, cw, ch);
        const cg = g.createRadialGradient(-cw * 0.26, -ch * 0.28, 2, -cw * 0.26, -ch * 0.28, cw * 0.42);
        cg.addColorStop(0, 'rgba(247,240,251,.75)'); cg.addColorStop(1, 'rgba(247,240,251,0)');
        g.fillStyle = cg; g.fillRect(-cw / 2, -ch / 2, cw, ch);
        g.restore();
      }
      const suit = suitOf(code);
      g.fillStyle = suit.red ? '#d6246e' : '#22123a'; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.shadowColor = 'rgba(255,255,255,.95)'; g.shadowBlur = pic ? 5 : 0;
      g.font = `700 ${cw * 0.26}px "Segoe UI", system-ui, sans-serif`; g.fillText(rankLabel(code), -cw * 0.26, -ch * 0.34);
      g.font = `${cw * 0.2}px "Segoe UI Symbol", "Segoe UI", sans-serif`; g.fillText(suit.glyph, -cw * 0.26, -ch * 0.17);
      g.font = `${cw * 0.5}px "Segoe UI Symbol", "Segoe UI", sans-serif`; g.fillText(suit.glyph, 0, ch * 0.1);
      g.shadowBlur = 0;
    } else {
      g.fillStyle = '#2a1745'; g.fill();
      g.save(); g.clip();
      const lk = loom();
      if (o.gates.spiral && lk && lk.draw(g, 'backs', -cw / 2, -ch / 2, cw, ch, { now: o.now, backing: 'small' })) stats.backs++;
      else {
        g.strokeStyle = 'rgba(232,194,122,.35)'; g.lineWidth = 1;
        for (let q = -6; q <= 6; q++) {
          g.beginPath(); g.moveTo(q * cw * 0.18 - ch, -ch); g.lineTo(q * cw * 0.18 + ch, ch); g.stroke();
          g.beginPath(); g.moveTo(q * cw * 0.18 + ch, -ch); g.lineTo(q * cw * 0.18 - ch, ch); g.stroke();
        }
      }
      g.restore();
    }
    rrect(g, -cw / 2, -ch / 2, cw, ch, cw * 0.1);
    g.strokeStyle = COL.brass; g.lineWidth = 1.5; g.stroke();
  }

  function chipDisc(x, y, r, col, alpha) {
    g.save(); g.globalAlpha *= alpha; g.translate(x, y);
    g.strokeStyle = 'rgba(232,194,122,.6)'; g.lineWidth = 1.5; g.beginPath(); g.arc(0, 0, r * 1.5, 0, TAU); g.stroke();
    g.fillStyle = col; g.beginPath(); g.arc(0, 0, r, 0, TAU); g.fill();
    g.strokeStyle = COL.text; g.setLineDash([3, 3]); g.beginPath(); g.arc(0, 0, r * 0.72, 0, TAU); g.stroke(); g.setLineDash([]);
    g.restore();
  }

  const byOwner = (owner) => cards.filter((c) => c.owner === owner);
  function target(L, c) { return slotXY(L, c.owner, c.slot, byOwner(c.owner).length, hands); }

  /** Advance one card to `now`: flight, landing, flip. */
  function move(L, c, now, still, dt) {
    const [tx, ty] = target(L, c);
    if (still) {
      if (!c.landed) { c.landed = true; if (c.code) c.faceAt = c.bornAt; touchdown(c, now, true, onCue); }
      c.x = tx; c.y = ty; c.lift = 0; c.rot = 0;
      return;
    }
    const p = clamp((now - c.bornAt) / TIMING.flyMs, 0, 1), e = ease(p);
    if (!c.landed) {
      c.x = lerp(L.shoe.x, tx, e); c.y = lerp(L.shoe.y, ty, e) - Math.sin(p * Math.PI) * H * 0.08;
      c.rot = (1 - e) * -0.9; c.lift = Math.sin(p * Math.PI);
      if (p >= 1) { touchdown(c, now, false, onCue); c.landed = true; c.lift = 0; c.rot = 0; if (c.code) c.faceAt = now; ripples.push({ x: tx, y: ty, t0: now }); }
    } else {
      if (!Number.isFinite(c.x)) { c.x = tx; c.y = ty; }   // put down settled: straight onto its spot
      const s = Math.min(1, dt * 0.012);
      c.x += (tx - c.x) * s; c.y += (ty - c.y) * s;
    }
  }
  const flipOf = (c, now, still) => (c.faceAt == null ? 0 : still ? 1 : clamp((now - c.faceAt) / TIMING.flipMs, 0, 1));

  function shadow(L, x, y, rot, lift, breath) {
    const dx = ((x - W / 2) / W) * 14 * (0.4 + breath * 0.8), dy = 3 + 9 * breath;
    g.save(); g.translate(x + dx * (1 + lift * 2), y + dy * (1 + lift * 2)); g.rotate(rot);
    g.globalAlpha = 0.32 - lift * 0.12; g.fillStyle = '#031014'; rrect(g, -L.cw / 2, -L.ch / 2, L.cw, L.ch, L.cw * 0.1); g.fill(); g.restore();
  }

  const api = {
    clear() { cards = []; hands = 1; active = -1; bets = []; betsShown = true; chips = []; tunnelAt = -1; glow = null; hits = []; deckAt = -Infinity; },
    /** `settled`: already on its spot and turned (a quiet adopt, a suspend's flush), no flight. */
    addCard({ owner, slot, code, settled = false }, now) {
      if (!settled) deckAt = now;
      cards.push({ quiet: settled, touchdown: settled, id: ++seq, owner, slot, code: code || null, bornAt: now, faceAt: settled && code ? now - TIMING.flipMs : null, landed: !!settled, x: NaN, y: NaN, rot: 0, lift: 0 });
    },
    // Suspend keeps the dealt state and discards unfinished presentation, including its sounds.
    skip(now) {
      for (const c of cards) {
        c.quiet = c.touchdown = c.landed = true; c.landAt = -Infinity;
        if (c.code) c.faceAt = now - TIMING.flipMs;
        c.x = c.y = NaN; c.rot = c.lift = 0;
      }
      fan = null; deckAt = -Infinity; chips = []; ripples = []; hits = []; glow = null; tunnelAt = -1;
    },
    split() { const c = cards.find((x) => x.owner === 0 && x.slot === 1); if (c) { c.owner = 1; c.slot = 0; } hands = 2; },
    reveal(code, now, settled = false) { const c = cards.find((x) => x.owner === 'd' && x.slot === 1); if (c) { c.code = code; c.faceAt = c.landed ? (settled ? now - TIMING.flipMs : now) : null; } },
    setActive(i) { active = i; },
    setBets(list) { bets = Array.isArray(list) ? list.slice() : []; betsShown = true; },
    startFan(now, still) { fan = { t0: now, still: !!still, landed: new Set() }; },
    fanDone(now) { return !fan || now - fan.t0 >= (fan.still ? TIMING.sitStillMs : TIMING.sitMs); },
    vortex(dir, n, now) {
      if (dir < 0) betsShown = false;
      for (let i = 0; i < n; i++) chips.push({ t0: now + (dir > 0 ? 200 + i * 280 : 300 + i * 160), dir, col: dir > 0 ? COL.mint : COL.rose });
    },
    tunnel(now) { tunnelAt = now; },
    glowCard(owner, slot, now) { const c = cards.find((x) => x.owner === owner && x.slot === slot); if (c) glow = { id: c.id, t0: now }; },
    /** THE GLYPH HIT: the cards in `list` ([{ owner, slot }]) glow in turn from `now`, HIGHLIGHT_GAP_MS apart. */
    hitCards(list, now) {
      hits = (Array.isArray(list) ? list : []).map((h, i) => { const c = cards.find((x) => x.owner === h.owner && x.slot === h.slot); return c ? { id: c.id, t0: now + i * HIGHLIGHT_GAP_MS } : null; }).filter(Boolean);
    },
    /** A card's box on its resting spot in canvas CSS px (for fx.gif_from's `from`), known before any frame has moved it. */
    cardRect(owner, slot) {
      const c = cards.find((x) => x.owner === owner && x.slot === slot), L = tableLayout(W, H);
      if (!c) return null;
      const [x, y] = target(L, c);
      return { x: x - L.cw / 2, y: y - L.ch / 2, w: L.cw, h: L.ch };
    },
    /** THE POT's box in canvas CSS px: the chip spot the bets sit on, and where THE BANK's tokens leave from
     *  on a paid hand (Law XII, lane BR2-rw-cards). The same space as `cardRect`. Never null - the spot is
     *  printed on the felt whether a bet is down on it or not. */
    potRect() {
      const L = tableLayout(W, H), r = L.spot.r * 1.6;
      return { x: L.spot.x - r, y: L.spot.y - r, w: r * 2, h: r * 2 };
    },
    /** Every card has landed and turned. */
    settled(now, still) { return cards.every((c) => c.landed && (c.faceAt == null || flipOf(c, now, still) >= 1)); },

    /**
     * One frame. o = { now, k, still, full, gates:{flash, spiral}, deck, print, dealerName, youName, handName(i), decide }
     */
    draw(o) {
      fit();
      const now = o.now, k = o.k, still = !!o.still, L = tableLayout(W, H), dt = Math.max(0, Math.min(100, now - lastNow || 16));
      lastNow = now; stats.backs = 0; stats.pictures = 0; stats.fan = 0; stats.frame++;
      ripples = ripples.filter((r) => now - r.t0 < TIMING.rippleMs);
      chips = chips.filter((c) => now - c.t0 < TIMING.vortexMs + 600);
      const tunnelOn = tunnelAt >= 0 && now - tunnelAt < TIMING.tunnelMs;
      const glowOn = glow && now - glow.t0 < TIMING.glowMs;
      const fanMs = fan ? now - fan.t0 : -1;
      if (fan && api.fanDone(now)) { fan = null; onCue('deck-square'); }
      const sitLift = fan ? Math.sin(clamp(fanMs / (fan.still ? TIMING.sitStillMs : TIMING.sitMs), 0, 1) * Math.PI) : 0;
      const breath = 0.5 + (lampBreath(now, tunnelOn || glowOn) - 0.5) * k;
      const lampR = W * (0.55 + 0.16 * (breath - 0.5) * 2 + 0.12 * sitLift);
      const lamp = g.createRadialGradient(W / 2, H * 0.42, 20, W / 2, H * 0.42, lampR);
      lamp.addColorStop(0, '#13494d'); lamp.addColorStop(0.7, '#0b2c34'); lamp.addColorStop(1, '#061820');
      g.fillStyle = lamp; g.fillRect(0, 0, W, H);
      const st = statics(L, o.print || '');
      if (o.full && ripples.length && !still) weaveLines(g, ripples, now, k); else g.drawImage(st.weave, 0, 0, W, H);
      g.drawImage(st.print, 0, 0, W, H);

      // shoe: a brass box with the top card's back showing
      g.fillStyle = '#2a1a3c'; rrect(g, L.shoe.x - L.cw * 0.75, L.shoe.y - L.ch * 0.5, L.cw * 1.5, L.ch, 8); g.fill();
      g.strokeStyle = COL.brass; g.lineWidth = 1.5; g.stroke();
      const recoil = deckRecoil(now - deckAt, still) * k;
      for (let layer = 2; layer >= 0; layer--) {
        g.save(); g.translate(L.shoe.x + layer * (1 + recoil * 5), L.shoe.y + layer * 2 - recoil * 5);
        g.rotate(-0.12 + recoil * .04 * layer); g.scale(.82, .82); drawCard(null, L.cw, L.ch, 0, { ...o, now }); g.restore();
      }

      // chip spot and the bet
      g.strokeStyle = 'rgba(232,194,122,.5)'; g.lineWidth = 1.5; g.beginPath(); g.arc(L.spot.x, L.spot.y, L.spot.r * 1.6, 0, TAU); g.stroke();
      if (betsShown) bets.forEach((b, i) => { for (let n = 0; n < b; n++) chipDisc(L.spot.x + (i - (bets.length - 1) / 2) * L.spot.r * 2.6, L.spot.y - n * 4, L.spot.r, COL.rose, 1); });

      // win tunnel (v1): seven nested frames zooming into the table, behind the cards
      if (tunnelOn) {
        const t = (now - tunnelAt) / 1000, fade = Math.min(1, t * 3) * Math.min(1, (TIMING.tunnelMs / 1000 - t) * 1.5);
        g.save(); g.globalCompositeOperation = 'lighter';
        for (let q = 0; q < 7; q++) {
          const p = still ? q / 7 : (t * 0.55 + q / 7) % 1, sc = 1 - p;
          g.globalAlpha = Math.sin(p * Math.PI) * 0.5 * fade * k;
          g.strokeStyle = q % 2 ? COL.brass : COL.rose; g.lineWidth = 2.5;
          const rw = W * 0.94 * sc, rh = H * 0.9 * sc;
          rrect(g, W / 2 - rw / 2, H * 0.47 - rh / 2, rw, rh, 18 * sc + 2); g.stroke();
        }
        g.restore();
      }

      // the hands
      for (const c of cards) move(L, c, now, still, dt);
      for (const c of cards) {
        if (!Number.isFinite(c.x)) continue;
        const flip = flipOf(c, now, still), lift = c.lift + flipLift(flip, still);
        const slide = landing(now - (c.landAt ?? -Infinity), still) * L.cw * .1 * k;
        const rotation = c.rot + (still ? 0 : cardTilt(c.owner, c.slot) * k);
        shadow(L, c.x, c.y, rotation, lift, breath);
        if (glowOn && glow.id === c.id) {
          const gp = clamp((now - glow.t0) / TIMING.glowMs, 0, 1);
          g.save(); g.globalAlpha = (still ? 0.6 : Math.sin(gp * Math.PI) * 0.9) * k; g.shadowColor = COL.rose; g.shadowBlur = 30; g.fillStyle = COL.rose;
          rrect(g, c.x - L.cw / 2 - 4, c.y - L.ch / 2 - 4, L.cw + 8, L.ch + 8, L.cw * 0.12); g.fill(); g.restore();
        }
        const pulse = hitPulse(c, now);
        if (pulse > 0) {   // the winning frame's rim: mint, out and back over 400 ms, the card popping 1.06 with it
          g.save(); g.globalAlpha = 0.85 * pulse * k; g.shadowColor = COL.mint; g.shadowBlur = 26; g.strokeStyle = COL.mint; g.lineWidth = 3;
          g.translate(c.x, c.y - c.lift * 6); g.rotate(c.rot); g.scale(1 + 0.06 * pulse, 1 + 0.06 * pulse);
          rrect(g, -L.cw / 2 - 2, -L.ch / 2 - 2, L.cw + 4, L.ch + 4, L.cw * 0.12); g.stroke(); g.restore();
        }
        g.save(); g.translate(c.x + slide, c.y - lift * 6); g.rotate(rotation); if (pulse > 0) g.scale(1 + 0.06 * pulse, 1 + 0.06 * pulse);
        drawCard(c.code, L.cw, L.ch, flipOf(c, now, still), { ...o, now }); g.restore();
      }
      // totals as the felt shows them (face-up cards only)
      g.font = `600 ${Math.max(12, W * 0.013)}px "Segoe UI", system-ui, sans-serif`; g.textAlign = 'center'; g.textBaseline = 'middle';
      const shown = (list) => list.filter((c) => c.landed && c.code && flipOf(c, now, still) >= 0.5).map((c) => c.code);
      const dealer = shown(byOwner('d'));
      if (byOwner('d').length) { g.fillStyle = COL.text; g.fillText(`${o.dealerName || ''}  ${dealer.length ? totalOf(dealer).total : ''}`, W / 2, L.dealerY + L.ch / 2 + 18); }
      for (let i = 0; i < hands; i++) {
        const mine = byOwner(i);
        if (!mine.length) continue;
        const [cx] = slotXY(L, i, 0, 1, hands), y = L.playerY - L.ch / 2 - 16, v = shown(mine);
        const name = hands === 2 ? (o.handName ? o.handName(i) : `${i + 1}`) : (o.youName || '');
        const on = o.decide && i === active;
        g.fillStyle = on ? COL.mint : COL.text;
        const value = v.length ? totalOf(v).total : '', newest = mine.filter((c) => !c.quiet && c.landed && c.code && flipOf(c, now, still) >= .5).reduce((a, c) => Math.max(a, c.faceAt + TIMING.flipMs / 2), -Infinity);
        const bump = Math.max(0, Math.sin(Math.min(1, (now - newest) / 260) * Math.PI)) * (still ? 0 : k);
        g.save(); g.translate(cx, y + (value > 21 ? 3 * bump : 0)); g.scale(1 + .08 * bump, 1 + .08 * bump);
        g.fillText(`${name}  ${value}`, 0, 0); g.restore();
        if (on && hands === 2) { g.strokeStyle = COL.mint; g.lineWidth = 2; g.beginPath(); g.moveTo(cx - L.cw * 0.9, y + 11); g.lineTo(cx + L.cw * 0.9, y + 11); g.stroke(); }
      }

      // the sit fan: thirteen values out of the shoe, face up in a row with their pictures, then home
      if (fan) {
        const cws = Math.min(L.cw * 0.8, (W * 0.9) / 13 * 0.92), chs = cws * 1.4, gap = Math.min(W * 0.068, (W * 0.9) / 13);
        for (let i = 0; i < 13; i++) {
          const f = fanCard(i, fanMs, fan.still);
          if (!f.visible) continue;
          stats.fan++;
          const tx = W / 2 + (i - 6) * gap, ty = H * 0.47;
          let x = tx, y = ty, rot = 0, lift = 0;
          if (!fan.still) {
            if (f.q > 0) { const e = ease(f.q); x = lerp(tx, L.shoe.x, e); y = lerp(ty, L.shoe.y, e) - Math.sin(f.q * Math.PI) * H * 0.08; rot = e * 0.9; lift = Math.sin(f.q * Math.PI); }
            else { const e = ease(f.p); x = lerp(L.shoe.x, tx, e); y = lerp(L.shoe.y, ty, e) - Math.sin(f.p * Math.PI) * H * 0.08; rot = (1 - e) * -0.9; lift = Math.sin(f.p * Math.PI); }
            if (f.p >= 1 && !fan.landed.has(i)) { fan.landed.add(i); ripples.push({ x: tx, y: ty, t0: now }); }
          }
          g.save(); g.globalAlpha = f.alpha; g.translate(x, y - lift * 6); g.rotate(rot);
          drawCard(DECK_VALUES[i] + (i % 2 ? 'h' : 's'), cws, chs, f.flip, { ...o, now });
          g.restore();
        }
      }

      // chip vortex: winnings spiral in to the player, a lost bet spirals away to the dealer (paths trail the chip)
      for (const c of chips) {
        const t = clamp((now - c.t0) / TIMING.vortexMs, 0, 1);
        if (t <= 0) continue;
        const from = L.spot, to = L.dealerSpot, R0 = Math.hypot(from.x - to.x, from.y - to.y), a0 = Math.atan2(to.y - from.y, to.x - from.x);
        if (still) { const at = c.dir > 0 ? from : to; chipDisc(at.x, at.y - 10, L.spot.r, c.col, Math.sin(t * Math.PI) * k); continue; }   // settled: no travel
        const u = c.dir > 0 ? ease(t) : 1 - ease(t);
        // turns below the spot are squashed flat so the path stays on the table (a smooth squash: no bend where it starts)
        const pos = (uu) => { const r = R0 * (1 - uu), a = a0 + uu * TAU * 1.25, s = Math.sin(a); return [from.x + Math.cos(a) * r, from.y + s * r * (0.575 - 0.425 * Math.tanh(s * 4))]; };
        g.save(); g.strokeStyle = c.col; g.globalAlpha = 0.35 * k; g.lineWidth = 2; g.beginPath();
        for (let q = 0; q <= 24; q++) { const [px, py] = pos(clamp(u - c.dir * q * 0.01, 0, 1)); if (q === 0) g.moveTo(px, py); else g.lineTo(px, py); }
        g.stroke(); g.restore();
        const [x, y] = pos(u);
        chipDisc(x, y, L.spot.r * (0.8 + 0.2 * (1 - Math.abs(u - 0.5) * 2)), c.col, c.dir > 0 ? 1 : 1 - t * 0.7);
      }
    },

    /** Test seam. */
    debug() {
      return { cards: cards.map((c) => ({ owner: c.owner, slot: c.slot, code: c.code, landed: c.landed, face: c.faceAt != null })), hands, active,
        fan: !!fan, fanCards: stats.fan, backs: stats.backs, pictures: stats.pictures, chips: chips.length, ripples: ripples.length,
        tunnel: tunnelAt >= 0 && lastNow - tunnelAt < TIMING.tunnelMs, glow: !!(glow && lastNow - glow.t0 < TIMING.glowMs), frames: stats.frame,
        hits: hits.map((h) => { const c = cards.find((x) => x.id === h.id); return { owner: c ? c.owner : null, slot: c ? c.slot : null, t0: Math.round(h.t0) }; }) };
    },
    dispose() { cards = []; chips = []; ripples = []; fan = null; hits = []; cache = { key: '', weave: null, print: null }; },
  };
  return api;
}
