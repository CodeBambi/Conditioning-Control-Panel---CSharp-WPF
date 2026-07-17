/* ============================================================================
 * hubStations.js - the Dollhouse as an in-ambient 3D menu (2026-07 rework).
 *
 * The Warren hub used to be a full-screen DOM SPA parked over the idling tube.
 * Now the tube IS the menu: floating billboarded "stations" hang in the bore
 * (TOYBOX / THE DIALS / VANITY) around a FALL IN portal dead-center down the
 * hole. Click a station and the camera swings onto it (nav.setFaceTarget, the
 * wall-poster hold pattern) while the game layer opens a compact DOM panel
 * anchored to its projected screen position. Click the portal and it shatters
 * (the boonPick dive) as the run request goes out.
 *
 * This is a dumb presenter in the boonPick/powerupDrops visual language (glow +
 * tinted frame + CDN art with a canvas name-panel fallback, billboarded to the
 * camera). WHAT the stations are and what clicking them means lives in the game
 * layer (game/warren.js), reached via scene.js pointer routing:
 *   game.onStationPick(id) / game.onStationMiss().
 *
 * The hub does NOT park the fall - the tube keeps its idle crawl (director
 * timeFactor 0.3), so every station re-anchors per frame at a fixed lead ahead
 * of the camera (layout.frameAtDepth(nav.getDepth() + AHEAD)) with a soft lerp:
 * the world glides past, the menu rides with you.
 * ==========================================================================*/

import * as THREE from 'three';

const ART = 'https://ccp.art/';          // station art on the CDN (boonPick idiom)
const AHEAD = 11;                        // depth lead: the menu hangs this far ahead
const CARD_W = 2.55, CARD_H = 3.1;       // station card content size (portrait)
const PORTAL_R = 2.1;                    // FALL IN portal disc radius
const ANCHOR_LERP = 3.2;                 // per-frame re-anchor smoothing
const FOCUS_SCALE = 1.15;                // focused station lift
const DIM_SIBLING = 0.35;                // unfocused siblings dim to this
const FLASH_DUR = 3.0;                   // reveal-flash seconds
const FOCUS_GLIDE = 0.9;                 // seconds: eased camera pan onto a focused station

// ---- shared canvas textures (built once per instance) -----------------------
function makeGlowTex() {
  const s = 128, c = document.createElement('canvas'); c.width = c.height = s;
  const x = c.getContext('2d');
  const g = x.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  g.addColorStop(0, 'rgba(255,255,255,0.9)');
  g.addColorStop(0.4, 'rgba(255,255,255,0.35)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  x.fillStyle = g; x.fillRect(0, 0, s, s);
  return new THREE.CanvasTexture(c);
}
function makeDotTex() {
  const s = 64, c = document.createElement('canvas'); c.width = c.height = s;
  const x = c.getContext('2d');
  const g = x.createRadialGradient(s / 2, s / 2, 0, s / 2, s / 2, s / 2);
  g.addColorStop(0, 'rgba(255,255,255,1)');
  g.addColorStop(0.35, 'rgba(255,255,255,0.5)');
  g.addColorStop(1, 'rgba(255,255,255,0)');
  x.fillStyle = g; x.beginPath(); x.arc(s / 2, s / 2, s / 2, 0, Math.PI * 2); x.fill();
  return new THREE.CanvasTexture(c);
}
function makeFrameTex() {
  const w = 256, h = 320, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const r = 26, pad = 8, lw = 12;
  x.strokeStyle = '#ffffff'; x.lineWidth = lw; x.lineJoin = 'round';
  x.beginPath();
  x.moveTo(pad + r, pad);
  x.arcTo(w - pad, pad, w - pad, h - pad, r);
  x.arcTo(w - pad, h - pad, pad, h - pad, r);
  x.arcTo(pad, h - pad, pad, pad, r);
  x.arcTo(pad, pad, w - pad, pad, r);
  x.closePath(); x.stroke();
  return new THREE.CanvasTexture(c);
}
// the FALL IN portal ring: concentric soft strokes + a faint inner swirl
function makeRingTex() {
  // 1024px source (was 256): the portal ring upscales to ~4.2 world units, so a
  // low-res texture read visibly pixelated on 2K/hi-DPI monitors. k keeps every
  // stroke weight proportional to the original 256px look — same art, crisper.
  const s = 1024, c = document.createElement('canvas'); c.width = c.height = s;
  const k = s / 256;
  const x = c.getContext('2d');
  x.translate(s / 2, s / 2);
  x.strokeStyle = '#ffffff';
  x.lineWidth = 10 * k; x.globalAlpha = 0.95;
  x.beginPath(); x.arc(0, 0, s * 0.44, 0, Math.PI * 2); x.stroke();
  x.lineWidth = 3 * k; x.globalAlpha = 0.5;
  x.beginPath(); x.arc(0, 0, s * 0.36, 0, Math.PI * 2); x.stroke();
  x.globalAlpha = 0.35;
  // three swirl arms spiralling inward
  for (let a = 0; a < 3; a++) {
    x.beginPath();
    const a0 = (a / 3) * Math.PI * 2;
    for (let t = 0; t <= 1; t += 0.04) {
      const ang = a0 + t * 3.6;
      const rr = s * 0.33 * (1 - t * 0.85);
      const px = Math.cos(ang) * rr, py = Math.sin(ang) * rr;
      if (t === 0) x.moveTo(px, py); else x.lineTo(px, py);
    }
    x.lineWidth = 4 * k; x.stroke();
  }
  const tex = new THREE.CanvasTexture(c);
  tex.anisotropy = 8;   // keep the ring sharp at the oblique portal angle
  return tex;
}
// fallback content: glyph + wrapped label on a dark panel, tinted per station
function makeNamePanelTex(def, hexFrame) {
  const w = 256, h = 320, c = document.createElement('canvas'); c.width = w; c.height = h;
  const x = c.getContext('2d');
  const col = '#' + ('000000' + (hexFrame >>> 0).toString(16)).slice(-6);
  const g = x.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, 'rgba(24,16,34,0.96)');
  g.addColorStop(1, 'rgba(12,8,20,0.96)');
  x.fillStyle = g; x.fillRect(0, 0, w, h);
  x.fillStyle = col; x.globalAlpha = 0.16; x.fillRect(0, 0, w, h); x.globalAlpha = 1;
  x.fillStyle = 'rgba(255,255,255,0.92)';
  x.font = '72px "Segoe UI Emoji", "Segoe UI", sans-serif';
  x.textAlign = 'center'; x.textBaseline = 'middle';
  x.fillText(def.glyph || '◈', w / 2, h * 0.36);
  x.fillStyle = col;
  x.font = 'bold 26px "Segoe UI", sans-serif';
  const words = String(def.label || '').split(' ');
  const lines = []; let line = '';
  for (const wd of words) {
    if ((line + ' ' + wd).length > 12 && line) { lines.push(line); line = wd; }
    else line = line ? line + ' ' + wd : wd;
  }
  if (line) lines.push(line);
  const y0 = h * 0.68 - (lines.length - 1) * 16;
  lines.forEach((l, i) => x.fillText(l, w / 2, y0 + i * 32));
  return new THREE.CanvasTexture(c);
}
// the affordable-count badge: a filled disc with the number, redrawn on demand
function drawBadge(canvas, n) {
  const s = 96, x = canvas.getContext('2d');
  x.clearRect(0, 0, s, s);
  if (n > 0) {
    // Deeper magenta + a soft dark rim: the old #ff69b4 washed out against the
    // pale hub bloom, so the "you can upgrade" count was hard to read.
    x.save();
    x.shadowColor = 'rgba(0,0,0,0.6)'; x.shadowBlur = 9;
    x.fillStyle = '#d6187a';
    x.beginPath(); x.arc(s / 2, s / 2, s * 0.42, 0, Math.PI * 2); x.fill();
    x.restore();
    x.strokeStyle = 'rgba(255,255,255,0.92)'; x.lineWidth = 5; x.stroke();
    x.fillStyle = '#fff';
    x.font = 'bold 46px "Segoe UI", sans-serif';
    x.textAlign = 'center'; x.textBaseline = 'middle';
    x.fillText(n > 9 ? '9+' : String(n), s / 2, s / 2 + 2);
  }
}

export function createHubStations({ scene, camera, layout, nav, fx, hud }) {
  const unitPlane = new THREE.PlaneGeometry(1, 1);
  const glowTex = makeGlowTex();
  const dotTex = makeDotTex();
  const frameTex = makeFrameTex();
  const ringTex = makeRingTex();
  const _euler = new THREE.Euler(), _q = new THREE.Quaternion();
  const _wp = new THREE.Vector3(), _tgt = new THREE.Vector3(), _right = new THREE.Vector3();

  let visible = false;
  let stations = [];        // live station handles
  let focused = null;       // focused station id (camera swung onto it)
  let focusBias = 0;        // screen-right offset of the face target (world units);
                            // 0 = the focused card centers between the flanking pane columns
  let aimedId = null;       // hover lift
  // camera pan: the face target eases from where you were looking to the new
  // station over FOCUS_GLIDE (card-to-card glides too, never a snap)
  const _faceFrom = new THREE.Vector3(), _faceCur = new THREE.Vector3();
  let faceT = 1, hasFace = false;
  const shatters = [];      // portal-dive shard bursts (ticked even after hide)

  // default cross-section slots (binormal = x, normal = y); defs may override
  const DEFAULT_SLOTS = {
    // Pulled in from ±3.4: at the old spread the side stations sat near the tube
    // wall radius, so their glow halos bled past it and read as "cut off."
    toybox:  { x: -2.9, y: 1.4 },
    dials:   { x: 2.9,  y: 1.4 },
    vanity:  { x: 0,    y: 2.15 },   // ducks under the DOM logo marquee
    portal:  { x: 0,    y: -0.4 },
    // crafting (bottom-right). Cards are CARD_H (3.1) tall, so at y=-1.15 its top
    // edge (0.40) overlapped THE DIALS' bottom edge (dials y=1.4 -> -0.15). Dropped
    // to -1.95 so the two right-column cards clear each other (~0.25 gap).
    boudoir: { x: 2.9,  y: -1.95 },
  };

  // ---- one station -----------------------------------------------------------
  function buildStation(def) {
    let disposed = false;
    const accent = def.accent != null ? def.accent : 0xff69b4;
    const glowCol = def.glowCol != null ? def.glowCol : accent;
    const group = new THREE.Group();
    group.userData = { type: 'hubstation', id: def.id };
    const isPortal = def.kind === 'portal';
    const w = isPortal ? PORTAL_R * 2 : CARD_W;
    const h = isPortal ? PORTAL_R * 2 : CARD_H;

    const glowMat = new THREE.MeshBasicMaterial({
      map: glowTex, color: new THREE.Color(glowCol), transparent: true, opacity: 0,
      depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide });
    const glow = new THREE.Mesh(unitPlane, glowMat);
    // Slightly tighter halo (was 1.7×1.6) so the side stations' glow stays inside
    // the tube bore and isn't clipped by the wall mesh.
    glow.scale.set(w * 1.55, h * 1.5, 1); glow.position.z = -0.08; group.add(glow);
    // the halo bleeds far past the card (the portal's reaches under the side
    // stations) - it must never catch the pointer, only the card/ring may
    glow.raycast = () => {};

    // frame: rounded border for cards, the swirl ring for the portal
    const frameMat = new THREE.MeshBasicMaterial({
      map: isPortal ? ringTex : frameTex, color: new THREE.Color(accent),
      transparent: true, opacity: 0, depthWrite: false,
      blending: isPortal ? THREE.AdditiveBlending : THREE.NormalBlending,
      side: THREE.DoubleSide });
    const frame = new THREE.Mesh(unitPlane, frameMat);
    frame.scale.set(w * 1.08, h * 1.08, 1); frame.position.z = 0.01; group.add(frame);

    // content: CDN art with the glyph/name panel as the always-there fallback.
    // The portal carries no art plane - the ring + glow IS the look.
    let contentMat = null, content = null;
    if (!isPortal) {
      contentMat = new THREE.MeshBasicMaterial({
        map: makeNamePanelTex(def, accent), transparent: true, opacity: 0,
        depthWrite: false, side: THREE.DoubleSide });
      content = new THREE.Mesh(unitPlane, contentMat);
      content.scale.set(w, h, 1); content.position.z = 0.02; group.add(content);
      if (def.artUrl !== null) {
        new THREE.TextureLoader().load(def.artUrl || `${ART}hub/${def.id}.png`, (tex) => {
          if (disposed) { tex.dispose(); return; }
          tex.colorSpace = THREE.SRGBColorSpace;
          const old = contentMat.map; contentMat.map = tex; contentMat.needsUpdate = true;
          if (old) old.dispose();
        }, undefined, () => { /* keep the glyph panel */ });
      }
    }

    // badge sprite (top-right corner), hidden until setBadge gives it a count
    const badgeCanvas = document.createElement('canvas');
    badgeCanvas.width = badgeCanvas.height = 96;
    const badgeTex = new THREE.CanvasTexture(badgeCanvas);
    const badgeMat = new THREE.MeshBasicMaterial({
      map: badgeTex, transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide });
    const badge = new THREE.Mesh(unitPlane, badgeMat);
    badge.scale.set(0.62, 0.62, 1);
    badge.position.set(w * 0.52, h * 0.5, 0.05);
    group.add(badge);

    // reveal-flash sparkles (idle until flashStation lights them)
    const SPARKS = 36;
    const sparkArr = new Float32Array(SPARKS * 3);
    for (let i = 0; i < SPARKS; i++) {
      const a = (i / SPARKS) * Math.PI * 2;
      const rr = Math.max(w, h) * (0.6 + Math.random() * 0.2);
      sparkArr[i * 3] = Math.cos(a) * rr;
      sparkArr[i * 3 + 1] = Math.sin(a) * rr * 0.95;
      sparkArr[i * 3 + 2] = 0.04;
    }
    const sparkGeo = new THREE.BufferGeometry();
    sparkGeo.setAttribute('position', new THREE.BufferAttribute(sparkArr, 3));
    const sparkMat = new THREE.PointsMaterial({
      map: dotTex, color: new THREE.Color(glowCol), size: 0.42, sizeAttenuation: true,
      transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false });
    const sparks = new THREE.Points(sparkGeo, sparkMat);
    sparks.raycast = () => {};   // decorative only - Points thresholds give phantom hits
    group.add(sparks);

    scene.add(group);

    const slot = def.slot || DEFAULT_SLOTS[def.id] || DEFAULT_SLOTS.portal;
    let appear = 0, sway = Math.random() * 6, fade = 0, flashT = 0;
    let badgeN = 0, seeded = false;

    return {
      id: def.id, def, group, slot,
      getContent: () => content || frame,
      setBadge(n) {
        n = n | 0;
        if (n === badgeN) return;
        badgeN = n;
        drawBadge(badgeCanvas, n);
        badgeTex.needsUpdate = true;
      },
      flash() { flashT = FLASH_DUR; },
      startFade() { if (fade <= 0) fade = 0.001; },
      isFading: () => fade > 0,
      screenAnchor() {
        group.getWorldPosition(_wp);
        _wp.y -= (isPortal ? PORTAL_R : CARD_H / 2) * 0.2; // anchor a touch below center
        _wp.project(camera);
        if (_wp.z > 1) return { x: 0, y: 0, visible: false };
        return {
          x: (_wp.x * 0.5 + 0.5) * window.innerWidth,
          y: (-_wp.y * 0.5 + 0.5) * window.innerHeight,
          visible: true,
        };
      },
      worldPos(out) { return group.getWorldPosition(out); },
      update(dt, fr, camQuat) {
        // re-anchor: ride the idle crawl at a fixed lead ahead of the camera
        _tgt.copy(fr.pos).addScaledVector(fr.binormal, slot.x).addScaledVector(fr.normal, slot.y);
        if (!seeded) { group.position.copy(_tgt); seeded = true; }
        else group.position.lerp(_tgt, Math.min(1, dt * ANCHOR_LERP));

        appear = Math.min(1, appear + dt * 2.4);
        sway += dt;
        if (isPortal) {
          // the portal spins slowly instead of swaying
          _euler.set(0, 0, sway * 0.25);
        } else {
          _euler.set(0, 0, Math.sin(sway * 1.1) * 0.04);
        }
        _q.setFromEuler(_euler);
        group.quaternion.copy(camQuat).multiply(_q);

        if (fade > 0) fade += dt;
        const fk = fade > 0 ? Math.min(1, fade / 0.3) : 0;
        if (flashT > 0) flashT = Math.max(0, flashT - dt);
        const flash = flashT > 0 ? Math.min(1, flashT / (FLASH_DUR * 0.35)) : 0;

        // focus/aim/hazy drive the brightness + scale
        const isFocused = focused === def.id;
        const dimmed = focused && !isFocused ? DIM_SIBLING : 1;
        const hazy = def.hazy ? 0.4 : 1;
        const lift = (isFocused ? 1 : 0) + (aimedId === def.id && !focused ? 0.45 : 0);
        const vis = appear * (1 - fk) * dimmed * hazy;

        glowMat.opacity = 0.9 * vis * (0.65 + 0.35 * lift + 0.16 * Math.sin(sway * 2.2) + 0.6 * flash);
        frameMat.opacity = vis * (isPortal ? 0.85 + 0.15 * Math.sin(sway * 1.7) + 0.4 * flash : 1);
        if (contentMat) contentMat.opacity = vis;
        badgeMat.opacity = badgeN > 0 ? vis : 0;
        sparkMat.opacity = flash * 0.9 * (1 - fk);
        if (flash > 0) sparks.rotation.z += dt * 1.2;

        const s = (1 + (FOCUS_SCALE - 1) * (isFocused ? 1 : 0) + 0.05 * lift * (isFocused ? 0 : 1)) * (1 - 0.12 * fk);
        group.scale.setScalar(s);
        return fk >= 1;  // fully faded -> disposable
      },
      dispose() {
        disposed = true;
        scene.remove(group);
        glowMat.dispose(); frameMat.dispose();
        if (contentMat) { if (contentMat.map) contentMat.map.dispose(); contentMat.dispose(); }
        badgeTex.dispose(); badgeMat.dispose();
        sparkGeo.dispose(); sparkMat.dispose();
      },
    };
  }

  // ---- portal-dive shatter (ported from boonPick.spawnShatter) ---------------
  function spawnShatter(st) {
    const content = st.getContent();
    if (!content || !content.material || !content.material.map) { st.dispose(); return; }
    const tex = content.material.map;
    content.updateWorldMatrix(true, false);
    const wm = content.matrixWorld.clone();
    const sc = new THREE.Vector3(); wm.decompose(new THREE.Vector3(), new THREE.Quaternion(), sc);
    const w = sc.x, h = sc.y;
    const grp = new THREE.Group();
    grp.applyMatrix4(wm); grp.scale.set(1, 1, 1);
    const N = 4, sw = w / N, sh = h / N;
    const shards = [];
    for (let iy = 0; iy < N; iy++) {
      for (let ix = 0; ix < N; ix++) {
        const g = new THREE.PlaneGeometry(sw * 0.92, sh * 0.92);
        const uv = g.attributes.uv;
        for (let k = 0; k < uv.count; k++) uv.setXY(k, (ix + uv.getX(k)) / N, (iy + uv.getY(k)) / N);
        uv.needsUpdate = true;
        const m = new THREE.MeshBasicMaterial({
          map: tex, transparent: true, side: THREE.DoubleSide, depthWrite: false,
          blending: THREE.AdditiveBlending, color: new THREE.Color(0xffffff) });
        const mesh = new THREE.Mesh(g, m);
        mesh.position.set((ix - (N - 1) / 2) * sw, (iy - (N - 1) / 2) * sh, 0);
        grp.add(mesh);
        const vel = new THREE.Vector3((ix - (N - 1) / 2) + (Math.random() - 0.5), (iy - (N - 1) / 2) + (Math.random() - 0.5), (Math.random() - 0.3) * 2).multiplyScalar(0.9);
        shards.push({ mesh, mat: m, geo: g, vel, spin: (Math.random() - 0.5) * 8 });
      }
    }
    scene.add(grp);
    shatters.push({ grp, shards, life: 0, dur: 0.6, st });
    st.group.visible = false; // the shards carry the look; station disposed when the burst ends
  }

  function tickShatters(dt) {
    for (let i = shatters.length - 1; i >= 0; i--) {
      const s = shatters[i]; s.life += dt;
      const k = Math.min(1, s.life / s.dur);
      for (const sh of s.shards) {
        sh.mesh.position.addScaledVector(sh.vel, dt * 6);
        sh.vel.y -= dt * 4;
        sh.mesh.rotation.z += sh.spin * dt;
        sh.mat.opacity = 1 - k;
      }
      if (k >= 1) {
        for (const sh of s.shards) { sh.geo.dispose(); sh.mat.dispose(); }
        scene.remove(s.grp);
        if (s.st && s.st.dispose) { try { s.st.dispose(); } catch (e) { /* ignore */ } }
        shatters.splice(i, 1);
      }
    }
  }

  const byId = (id) => stations.find((s) => s.id === id) || null;

  return {
    // defs = [{ id, kind:'station'|'portal', label, glyph, artUrl, accent, badge, hazy, slot }]
    show(defs) {
      this.hide(true);
      visible = true;
      focused = null; aimedId = null;
      stations = (defs || []).map((d) => {
        const st = buildStation(d);
        if (d.badge) st.setBadge(d.badge);
        return st;
      });
    },

    // instant=true tears down without the fade (used by show() rebuilds)
    hide(instant) {
      if (instant) { for (const s of stations) s.dispose(); stations = []; }
      else for (const s of stations) s.startFade();
      visible = false;
      focused = null; aimedId = null;
      faceT = 1; hasFace = false;
      try { nav.setFaceTarget(null); } catch (e) { /* ignore */ }
    },

    update(dt) {
      tickShatters(dt);
      if (!stations.length) return;
      const fr = layout.frameAtDepth(nav.getDepth() + AHEAD);
      for (let i = stations.length - 1; i >= 0; i--) {
        const done = stations[i].update(dt, fr, camera.quaternion);
        if (done && stations[i].isFading()) { stations[i].dispose(); stations.splice(i, 1); }
      }
      // keep the camera swung onto the focused station as it rides the crawl
      // (bias 0 centers the card between the two pane columns; a nonzero bias
      // still offsets screen-right). The face point PANS: it eases from where
      // you were looking to the station over FOCUS_GLIDE, then tracks it -
      // nav's own FACE_LERP smooths on top, so focus and card-to-card both
      // read as a camera glide.
      if (focused) {
        const st = byId(focused);
        if (st) {
          st.worldPos(_wp);
          if (focusBias) {
            _right.set(1, 0, 0).applyQuaternion(camera.quaternion);
            _wp.addScaledVector(_right, focusBias);
          }
          if (faceT < 1) {
            faceT = Math.min(1, faceT + dt / FOCUS_GLIDE);
            const k = faceT * faceT * (3 - 2 * faceT);   // smoothstep ease in-out
            _faceCur.lerpVectors(_faceFrom, _wp, k);
          } else _faceCur.copy(_wp);
          hasFace = true;
          nav.setFaceTarget(_faceCur);
        }
      }
    },

    isBusy: () => visible,
    getPickables: () => (visible ? stations.filter((s) => !s.isFading() && !s.def.hazy).map((s) => s.group) : []),

    // Resolve a pointer ray to a station id (or null). Owns the pick rules the
    // raw raycast can't express: halos/sparkles never hit (raycast no-ops),
    // CARDS ALWAYS WIN over the portal (their quads can brush the portal frame's
    // corners near the center), and the portal only counts INSIDE its ring -
    // its frame quad is square, and a square corner is empty tube, not FALL IN.
    pick(raycaster) {
      if (!visible) return null;
      const picks = this.getPickables();
      if (!picks.length) return null;
      const hits = raycaster.intersectObjects(picks, true);
      let portalId = null;
      for (const h of hits) {
        let o = h.object;
        while (o && !(o.userData && o.userData.type === 'hubstation')) o = o.parent;
        if (!o) continue;
        const st = byId(o.userData.id);
        if (!st) continue;
        if (st.def.kind === 'portal') {
          st.worldPos(_tgt);
          if (!portalId && h.point.distanceTo(_tgt) <= PORTAL_R) portalId = st.id;
          continue;
        }
        return st.id;
      }
      return portalId;
    },

    focus(id, bias) {
      const st = byId(id);
      if (!st) return;
      if (focused === id) { focusBias = bias || 0; return; }
      // pan start: the current face point when gliding card-to-card, else the
      // point the camera is looking at right now (at the station's distance)
      st.worldPos(_tgt);
      if (hasFace) _faceFrom.copy(_faceCur);
      else {
        const dist = _tgt.distanceTo(camera.position);
        camera.getWorldDirection(_faceFrom).multiplyScalar(dist).add(camera.position);
      }
      focused = id;
      focusBias = bias || 0;
      faceT = 0;
      // the per-frame update eases the face target onto the station from here on
    },
    blur() {
      focused = null;
      focusBias = 0;
      faceT = 1; hasFace = false;
      try { nav.setFaceTarget(null); } catch (e) { /* ignore */ }
    },
    focusedId: () => focused,

    setAimed(id) { aimedId = id; },

    // screen-space anchor for the game layer's DOM panel
    anchorFor(id) {
      const st = byId(id);
      if (!st || !visible) return { x: 0, y: 0, visible: false };
      return st.screenAnchor();
    },

    setBadge(id, n) { const st = byId(id); if (st) st.setBadge(n); },
    flashStation(id) { const st = byId(id); if (st) st.flash(); },

    // the FALL IN moment: shatter the portal + brightness punch. Cosmetic only -
    // the actual run start is the game's request-run -> run-config handshake.
    portalDive(id) {
      const st = byId(id || 'portal');
      if (!st) return;
      try { fx && fx.pulseFlash && fx.pulseFlash(0.7); } catch (e) { /* ignore */ }
      stations = stations.filter((s) => s !== st);  // ownership moves to the shatter
      spawnShatter(st);
      for (const s of stations) s.startFade();      // the rest of the menu falls away
      visible = false;
      focused = null; aimedId = null;
      faceT = 1; hasFace = false;
      try { nav.setFaceTarget(null); } catch (e) { /* ignore */ }
    },

    dispose() {
      for (const s of stations) s.dispose();
      stations = [];
      for (let i = shatters.length - 1; i >= 0; i--) {
        for (const sh of shatters[i].shards) { sh.geo.dispose(); sh.mat.dispose(); }
        scene.remove(shatters[i].grp);
        if (shatters[i].st && shatters[i].st.dispose) { try { shatters[i].st.dispose(); } catch (e) { /* ignore */ } }
      }
      shatters.length = 0;
      visible = false; focused = null;
      unitPlane.dispose(); glowTex.dispose(); dotTex.dispose(); frameTex.dispose(); ringTex.dispose();
    },
  };
}
