/* ============================================================================
 * biomeMech.js - the part that makes a biome a PLACE, not a re-tint.
 *
 * Each biome (game/biomes.js) names a mechanic here by id; the run brain owns
 * one controller and forwards its existing seams into it:
 *
 *   setBiome(regionIndex, biome)  chamber entry (applyRegionSky) - exits the
 *                                 old mechanic (restoring anything it bent),
 *                                 enters the new one
 *   reset()                       run end / surface - exit + restore
 *   tick(dt)                      each 0.25s RunTick, AFTER ambientTick (so a
 *                                 mechanic's rush/speed writes win the frame),
 *                                 only while truly running (not held/paused)
 *   treatPop(spec, x, y, src)     the standard-treat branch of onBenignPopped;
 *                                 returns { payMult } folded into the points
 *   onGoldenPop(gold, x, y)       a golden popped - return true to OWN the
 *                                 banking (Fool's Casino's pot)
 *   onDefused(spec, fuseSecLeft,  a fused bubble came down clean - return
 *             viaChannel, x, y)   { payMult } folded into the snap's points
 *                                 (Vertigo's mid-flip double, the Undertow's
 *                                 beaten current, the Chain Court's bargains)
 *   onDetonated(spec, x, y)       a real (unshielded) detonation landed
 *   grabPolicy(kind, rec)         'allow' | 'melt' - consulted by the spawner/
 *                                 wall grab paths (kind 'card' | 'poster')
 *   onGrabbed(kind, rec)          a grab actually held (policy allowed it)
 *   onMelted(kind, rec)           the engine finished melting a denied grab
 *
 * Mechanics compose EXISTING verbs (sonarAt, setMirror, floatText, pickups,
 * branchTint, phys) - they never own rendering. Everything a mechanic bends
 * on entry it restores on exit, so chamber -> chamber is always clean.
 * ==========================================================================*/

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const rand = (a, b) => a + Math.random() * (b - a);

export function createBiomeMech(g) {
  // ---- run-wide counters (fed regardless of which biome is up: the
  // Coronation reads the WHOLE run back, not just chamber IV) ----------------
  let runGrabs = 0;
  const grabTally = new Map();   // assetName -> grabs this run

  let impl = null;      // the active mechanic instance
  let activeBiome = null;

  // ---------------------------------------------------------------------------
  // THE TOYBOX - Mimics: it's all just toys… until one flips in your hand.
  const mimics = () => {
    const LINES = [
      'that meant nothing~', 'just a toy. obviously.', 'you can stop giggling',
      'it doesn’t count if it’s cute', 'nobody saw that',
    ];
    return {
      treatPop(spec, x, y) {
        if (Math.random() >= 0.15) return null;
        // the toy flips: real content flashes out of the harmless pop
        g.firePayload({ variantId: 'flash', strength: 45, payload: { kind: 'flash' } });
        g.field.floatText('🧸→ oh.', x, y - 10, 'cf-pop--gold');
        g.toast(`🧸 ${LINES[(Math.random() * LINES.length) | 0]}`);
        g.sfx('boon_pick', 0.35);
        g.addHeat(0.03);
        return { payMult: 3.0 };
      },
    };
  };

  // ---------------------------------------------------------------------------
  // THE KEYHOLE - Only Looking: the field is dark; two warm keyhole beams roam,
  // and only what the light falls on is vivid (and worth full points).
  const keyhole = () => {
    const beams = [
      { px: 0.35, py: 0.42, sx: 0.23, sy: 0.31, ph: 0.0, ph2: 1.7, x: 0, y: 0, r: 170 },
      { px: 0.65, py: 0.55, sx: 0.19, sy: 0.27, ph: 2.6, ph2: 0.6, x: 0, y: 0, r: 150 },
    ];
    let t = Math.random() * 20, sonarCd = 0;
    let saved = null;
    const inBeam = (x, y) => beams.some((b) => Math.hypot(x - b.x, y - b.y) <= b.r);
    return {
      enter() {
        const st = g.st();
        saved = { active: st.blindfoldActive, opacity: st.blindfoldOpacity };
        st.blindfoldActive = true;         // reuse the Blindfold's dim plumbing…
        st.blindfoldOpacity = 0.25;        // …but NOT its pay bonus (blindfoldPayMult untouched)
        g.syncPhys();
      },
      tick(dt) {
        t += dt;
        const W = window.innerWidth, H = window.innerHeight;
        const R = clamp(Math.min(W, H) * 0.17, 120, 230);
        for (const b of beams) {
          b.x = W * (b.px + 0.36 * Math.sin(t * b.sx + b.ph));
          b.y = H * (b.py + 0.30 * Math.sin(t * b.sy + b.ph2));
          b.r = R;
        }
        g.ffx.setBeams(beams.map((b) => ({ x: b.x, y: b.y, r: b.r })));
        // the light itself does the revealing (sonar un-dims what it touches)
        sonarCd -= dt;
        if (sonarCd <= 0) {
          sonarCd = 0.55;
          for (const b of beams) g.field.sonarAt(b.x, b.y, b.r * 1.05);
        }
      },
      payMultAt(x, y) { return inBeam(x, y) ? 1.6 : 0.8; },
      exit() {
        const st = g.st();
        if (saved && st) { st.blindfoldActive = saved.active; st.blindfoldOpacity = saved.opacity; g.syncPhys(); }
        g.ffx.setBeams(null);
      },
    };
  };

  // ---------------------------------------------------------------------------
  // HALL OF MIRRORS - Mirror Moments: every so often EVERY card and poster
  // becomes the one you engage with most. The room knows. It shows you.
  const mirrors = () => {
    let momentIn = rand(20, 32), left = 0, chain = 0;
    const clear = () => {
      g.spawner.setMirror(null);
      g.wall.setMirror(null);
    };
    return {
      tick(dt) {
        if (left > 0) {
          left -= dt;
          if (left <= 0) {
            clear();
            if (chain >= 3) g.toast(`🪞 you knew every one of them · ×${chain}`);
          }
          return;
        }
        momentIn -= dt;
        if (momentIn > 0) return;
        const fav = g.favoriteAsset('image');
        if (!fav) { momentIn = 40; return; }   // no engagement history yet: try again later
        momentIn = rand(34, 48);
        left = 9;
        chain = 0;
        g.spawner.setMirror(fav.url);
        g.wall.setMirror(fav.url);
        g.announce('🪞 …it’s this one, isn’t it?', 'depth', 2600, { subText: 'everywhere you look' });
        g.sfx('tunnel_zone', 0.5);
        g.pulse('200,216,255', 0.35);
      },
      treatPop() {
        if (left <= 0) return null;
        chain++;
        return { payMult: 1.5 };
      },
      exit() { if (left > 0) { left = 0; } clear(); },
    };
  };

  // ---------------------------------------------------------------------------
  // THE GREY WARD - Color Starvation: the world is ash; only the media is in
  // color. Stop touching it and your streak greys out with everything else.
  const greyward = () => {
    let sinceTouch = 0, decayCd = 0, warned = false;
    const touch = () => {
      sinceTouch = 0;
      warned = false;
      const st = g.st();
      // the color floods back for a beat (branchTint decays on its own)
      st.branchTint = { color: '#ff69b4', strength: 0.45 };
      g.addHeat(0.02);
    };
    return {
      tick(dt) {
        sinceTouch += dt;
        if (sinceTouch <= 7) return;
        decayCd -= dt;
        if (decayCd > 0) return;
        decayCd = 1.5;
        const st = g.st();
        if (st.combo <= 0) return;
        st.combo = Math.floor(st.combo * 0.7);
        if (!warned) { warned = true; g.toast('🌫 the grey creeps in — keep touching the color'); }
        g.hudNow();
      },
      treatPop() { touch(); return null; },
      onGrabbed() { touch(); },
      exit() { },
    };
  };

  // ---------------------------------------------------------------------------
  // THE GALLERY - Look, Don't Touch: grab anything and it melts in your hands.
  // Restraint pays; the brief TOUCH PERMITTED windows let you binge.
  const gallery = () => {
    let restraint = 0, windowIn = rand(40, 55), windowLeft = 0;
    return {
      enter() { g.toast('🏛 the gallery — look. don’t touch.'); },
      tick(dt) {
        restraint = Math.min(1, restraint + dt / 40);
        if (windowLeft > 0) {
          windowLeft -= dt;
          if (windowLeft <= 0) g.toast('🏛 the ropes go back up');
          return;
        }
        windowIn -= dt;
        if (windowIn > 0) return;
        windowIn = rand(50, 70);
        windowLeft = 10;
        g.announce('👐 TOUCH PERMITTED', 'powerup', 2200, { subText: 'ten seconds. go.' });
        g.sfx('streak_milestone', 0.5);
        g.pulse('255,215,0', 0.35);
      },
      grabPolicy() { return windowLeft > 0 ? 'allow' : 'melt'; },
      onGrabbed() {
        if (windowLeft <= 0) return;
        const st = g.st();
        st.combo += 2;
        g.toast('👐 permitted — savor it');
        g.hudNow();
      },
      onMelted() {
        restraint = 0;
        const st = g.st();
        st.combo = Math.max(0, st.combo - 3);
        g.toast('🫠 it melts in your hands — look, don’t touch');
        g.sfx('focus_empty', 0.35);
        g.hudNow();
      },
      payMultAt() { return 1 + restraint; },
      exit() { },
    };
  };

  // ---------------------------------------------------------------------------
  // FOOL'S CASINO - Double or Nothing: a golden pop opens a pot. Golden again
  // doubles it (x8 cap). A detonation burns it. 20 idle seconds cash it out.
  const casino = () => {
    let pot = 0, doubles = 0, timer = 0;
    const cashout = (why) => {
      if (pot <= 0) return;
      g.bankGold(pot);
      g.toast(`🎰 ${why} — +${pot} 🪙 cashed out`);
      g.sfx('golden_pop', 0.55);
      pot = 0; doubles = 0; timer = 0;
    };
    return {
      enter() { g.toast('🎰 fool’s casino — the house smiles at you'); },
      tick(dt) {
        if (pot <= 0) return;
        timer -= dt;
        if (timer <= 0) cashout('she pays out');
      },
      onGoldenPop(gold, x, y) {
        if (pot <= 0) {
          pot = gold; doubles = 0; timer = 20;
          g.field.floatText(`${pot} 🪙 riding`, x, y + 30, 'cf-pop--gold');
          g.toast(`🎰 the pot opens — ${pot} 🪙 riding. golden again to double`);
          g.sfx('golden_pop', 0.5);
        } else {
          pot *= 2; doubles++; timer = 20;
          g.field.floatText(`×2 → ${pot} 🪙`, x, y + 30, 'cf-pop--gold');
          g.announce(`🎰 DOUBLE — ${pot} 🪙 riding`, 'powerup', 1800);
          g.sfx('streak_milestone', 0.5);
          g.pulse('255,215,0', 0.4);
          if (doubles >= 3) cashout('table limit');
        }
        return true;   // the mech owns the banking - nothing lands until it resolves
      },
      onDetonated() {
        if (pot <= 0) return;
        g.announce(`🎰 BUST — ${pot} 🪙 burns`, 'bad', 2000);
        g.sfx('fx_drain', 0.4);
        pot = 0; doubles = 0; timer = 0;
      },
      exit() { cashout('closing time'); },
    };
  };

  // ---------------------------------------------------------------------------
  // TERMINAL VELOCITY - Freefall Flow: nothing can hurt you and nothing waits.
  // The tube runs flat-out; only an idle streak bleeds.
  const velocity = () => {
    let sincePop = 0, bleedCd = 0, warned = false;
    return {
      enter() { g.toast('☄ terminal velocity — nothing here can hurt you. fall.'); },
      tick(dt) {
        // last write of the frame wins: outrun ambientTick's rush/speed
        g.fx.setRushOverride(0.9);
        g.nav.setSpeedCapMult(1.6);
        sincePop += dt;
        if (sincePop <= 4) { warned = false; return; }
        bleedCd -= dt;
        if (bleedCd > 0) return;
        bleedCd = 0.5;
        const st = g.st();
        if (st.combo <= 0) return;
        st.combo--;
        if (!warned) { warned = true; g.toast('☄ the flow thins — keep popping'); }
        g.hudNow();
      },
      treatPop() { sincePop = 0; return null; },
      exit() { g.nav.setSpeedCapMult(1); },   // rush restores itself next ambientTick
    };
  };

  // ---------------------------------------------------------------------------
  // THE CORONATION - The Verdicts: the run reads itself back to you, one fact
  // at a time. Clicking a verdict is accepting it. Accepting pays.
  const coronation = () => {
    let verdictIn = 16, verdicts = 0, wallMirrorLeft = 0;
    const usedFacts = new Set();
    const facts = () => {
      const st = g.st();
      return [
        runGrabs > 0 ? `${runGrabs} grabbed. zero hesitation.` : null,
        st.defused > 0 ? `${st.defused} held to the snap. every one.` : null,
        st.detonated > 0 ? `${st.detonated} you let trigger. you watched them all.` : `not one let go. you wanted every second.`,
        st.bestCombo >= 5 ? `a chain of ${st.bestCombo}. it felt natural.` : null,
        st.heat >= 0.4 ? `${Math.round(st.heat * 100)}% lust when the verdict came.` : null,
        `you came back. you always come back.`,
      ].filter(Boolean).filter((f) => !usedFacts.has(f));
    };
    const topGrabbed = () => {
      let best = null, n = 0;
      for (const [name, c] of grabTally) if (c > n) { n = c; best = name; }
      return best;
    };
    return {
      tick(dt) {
        if (wallMirrorLeft > 0) {
          wallMirrorLeft -= dt;
          if (wallMirrorLeft <= 0) g.wall.setMirror(null);
        }
        verdictIn -= dt;
        if (verdictIn > 0) return;
        verdictIn = rand(24, 34);
        const pool = facts();
        if (!pool.length) return;
        const fact = pool[(Math.random() * pool.length) | 0];
        usedFacts.add(fact);
        g.spawner.spawnPickup({
          kind: 'verdict',
          spriteUrl: 'https://ccp.art/bubbles/gold_droplet.png',
          w: 1.7, aheadDepth: 42, ttlSec: 13, glowColor: 0xffd700,
          onClick: () => {
            verdicts++;
            const st = g.st();
            st.boonMult += 0.12;
            g.announce(`👑 ${fact}`, 'powerup', 2800, { subText: 'accepted' });
            g.sfx('streak_milestone', 0.55);
            g.pulse('255,215,0', 0.4);
            g.hudNow();
            // the walls answer: your most-grabbed one, everywhere, for a beat
            const top = topGrabbed();
            const url = top && g.assetUrl(top);
            if (url) { g.wall.setMirror(url); wallMirrorLeft = 12; }
          },
        });
        g.toast('👑 a verdict drifts down — accept it');
      },
      exit() { if (wallMirrorLeft > 0) { wallMirrorLeft = 0; g.wall.setMirror(null); } },
    };
  };

  // ---------------------------------------------------------------------------
  // LATE NIGHT STATIC - Flicker Windows: the signal comes and goes on a slow
  // carrier. Pops while the picture is IN pay double; pops in the snow pay half.
  const flicker = () => {
    const PERIOD = 2.6, VIVID = 1.0;   // seconds: 1.6 of snow, 1.0 of signal
    let t = 0, vivid = false, taught = false;
    return {
      enter() { g.toast('📺 late night static — wait for the picture'); },
      tick(dt) {
        t = (t + dt) % PERIOD;
        const now = t < VIVID;
        if (now !== vivid) {
          vivid = now;
          if (vivid) {
            g.pulse('110,240,220', 0.18);
            g.st().branchTint = { color: '#5ee8d0', strength: 0.30 };
          }
        }
      },
      payMultAt() { return vivid ? 2.0 : 0.5; },
      treatPop() {
        if (vivid) { g.addHeat(0.015); return null; }
        if (!taught) { taught = true; g.toast('📺 snow — wait for the signal to come in'); }
        return null;
      },
      exit() { },
    };
  };

  // ---------------------------------------------------------------------------
  // INCOGNITO - Clear History: the room wipes itself on a timer. Whatever the
  // wipe takes melts for gold; whatever you popped first already paid in full.
  // Each wipe comes a little sooner. You always open another tab.
  const incognito = () => {
    let gap = 44, wipeIn = rand(34, 44), warned = false;
    return {
      enter() { g.toast('🕶 incognito — nothing here is saved'); },
      tick(dt) {
        wipeIn -= dt;
        if (wipeIn <= 5 && !warned) {
          warned = true;
          g.toast('🕶 clearing history in 5…');
          g.sfx('tunnel_zone', 0.35);
        }
        if (wipeIn > 0) return;
        gap = Math.max(26, gap - 5);
        wipeIn = gap; warned = false;
        const n = g.spawner.meltAll() + g.wall.meltAll();
        if (n > 0) {
          const refund = n * 2;
          g.bankGold(refund);
          g.announce('🕶 HISTORY CLEARED', 'depth', 2200, { subText: `${n} erased · +${refund} 🪙 — you always come back` });
          g.sfx('fx_drain', 0.45);
          g.pulse('90,110,160', 0.40);
          g.addHeat(0.04);
        } else {
          g.toast('🕶 nothing to clear. this time.');
        }
      },
      exit() { },
    };
  };

  // ---------------------------------------------------------------------------
  // VERTIGO - The Flip: gravity loses its nerve. Everything reverses on a
  // telegraphed count, fuses burn hot for the ride, and a snap completed
  // mid-flip pays double.
  const vertigo = () => {
    let flipIn = rand(14, 20), flipLeft = 0, warnAt = 3;
    const setFlip = (on) => {
      g.field.flipDrift();
      g.field.phys.fuseTickMult = on ? 1.4 : 1.0;
    };
    return {
      enter() { g.toast('🙃 vertigo — trust nothing. especially down.'); },
      tick(dt) {
        if (flipLeft > 0) {
          flipLeft -= dt;
          if (flipLeft <= 0) { setFlip(false); g.toast('🙃 …and back. for now.'); }
          return;
        }
        flipIn -= dt;
        if (flipIn <= warnAt && warnAt >= 1) {
          g.field.floatText(`🙃 ${warnAt}`, window.innerWidth / 2, window.innerHeight * 0.30, 'cf-pop--word');
          warnAt--;
        }
        if (flipIn > 0) return;
        flipIn = rand(16, 24); warnAt = 3;
        flipLeft = 7;
        setFlip(true);
        g.announce('🙃 THE FLIP', 'depth', 1800, { subText: 'fuses burn hot while the world is wrong' });
        g.sfx('tunnel_zone', 0.5);
        g.pulse('154,123,255', 0.45);
      },
      onDefused(spec, fuseSecLeft, viaChannel, x, y) {
        if (flipLeft <= 0) return null;
        g.field.floatText('×2 mid-flip', x, y - 24, 'cf-pop--gold');
        return { payMult: 2 };
      },
      exit() {
        if (flipLeft > 0) { flipLeft = 0; setFlip(false); }
        else g.field.phys.fuseTickMult = 1.0;
      },
    };
  };

  // ---------------------------------------------------------------------------
  // THE SEARCHLIGHT - Exposed: two cold beams sweep the dark for exactly you.
  // What you do in the dark pays more; linger in the light and it SPOTS you -
  // every fuse it can see enrages. The Keyhole's mirror: there the light was
  // permission, here it's the thing you hide from.
  const searchlight = () => {
    const COLD = [175, 195, 255];
    const beams = [
      { px: 0.30, py: 0.40, sx: 0.30, sy: 0.22, ph: 0.0, ph2: 2.2, x: 0, y: 0, r: 160 },
      { px: 0.70, py: 0.58, sx: 0.26, sy: 0.34, ph: 3.1, ph2: 0.8, x: 0, y: 0, r: 150 },
    ];
    let t = Math.random() * 20, sonarCd = 0, seenHold = 0, spotCd = 0, saved = null;
    const inBeam = (x, y) => beams.some((b) => Math.hypot(x - b.x, y - b.y) <= b.r);
    return {
      enter() {
        const st = g.st();
        saved = { active: st.blindfoldActive, opacity: st.blindfoldOpacity };
        st.blindfoldActive = true;         // the ward is dim…
        st.blindfoldOpacity = 0.45;        // …but workable: you CAN pop in the dark
        g.syncPhys();
        g.audioColor('muffled');           // the whole mix holds its breath
        g.toast('🔦 the searchlight — do it in the dark');
      },
      tick(dt) {
        t += dt;
        const W = window.innerWidth, H = window.innerHeight;
        const R = clamp(Math.min(W, H) * 0.16, 110, 210);
        for (const b of beams) {
          b.x = W * (b.px + 0.40 * Math.sin(t * b.sx + b.ph));
          b.y = H * (b.py + 0.32 * Math.sin(t * b.sy + b.ph2));
          b.r = R;
        }
        g.ffx.setBeams(beams.map((b) => ({ x: b.x, y: b.y, r: b.r, color: COLD })));
        sonarCd -= dt;
        if (sonarCd <= 0) {
          sonarCd = 0.55;
          for (const b of beams) g.field.sonarAt(b.x, b.y, b.r * 1.05);
        }
        // the light checks for YOU: linger inside it and you're SPOTTED
        spotCd -= dt;
        const cur = g.field.cursor();
        if (inBeam(cur.x, cur.y)) seenHold += dt; else seenHold = Math.max(0, seenHold - dt * 2);
        if (seenHold > 1.2 && spotCd <= 0) {
          spotCd = 7; seenHold = 0;
          const n = g.field.enrageNear(cur.x, cur.y, 300);
          g.announce('🔦 SPOTTED', 'bad', 1800, { subText: n > 0 ? 'the fuses saw you too' : 'caught in the light' });
          g.addHeat(0.05);
          g.sfx('detonate_thud', 0.4);
          g.pulse('255,80,80', 0.45);
        }
      },
      payMultAt(x, y) { return inBeam(x, y) ? 0.6 : 1.35; },
      exit() {
        const st = g.st();
        if (saved && st) { st.blindfoldActive = saved.active; st.blindfoldOpacity = saved.opacity; g.syncPhys(); }
        g.ffx.setBeams(null);
        g.audioColor(null);
      },
    };
  };

  // ---------------------------------------------------------------------------
  // THE CHAIN COURT - Terms & Conditions: the room spawns bound pairs by
  // profile; every snap is a bargain kept (small gold, every time), and the
  // drifting contracts pay NOW and bill later. Read the terms.
  const contracts = () => {
    let contractIn = rand(16, 24);
    const debts = [];
    return {
      enter() { g.toast('⛓ the chain court — every bargain here is kept'); },
      tick(dt) {
        for (let i = debts.length - 1; i >= 0; i--) {
          debts[i].left -= dt;
          if (debts[i].left > 0) continue;
          debts.splice(i, 1);
          g.addHeat(0.10);
          g.announce('⛓ THE BALANCE COMES DUE', 'bad', 2200, { subText: 'you knew the terms' });
          g.sfx('fx_drain', 0.45);
          g.pulse('230,90,110', 0.45);
        }
        contractIn -= dt;
        if (contractIn > 0) return;
        contractIn = rand(26, 36);
        g.spawner.spawnPickup({
          kind: 'contract',
          spriteUrl: 'https://ccp.art/bubbles/gold_droplet.png',
          w: 1.5, aheadDepth: 42, ttlSec: 12, glowColor: 0xc23b4e,
          onClick: () => {
            g.bankGold(20);
            debts.push({ left: 45 });
            g.announce('⛓ SIGNED — 20 🪙 now', 'powerup', 2200, { subText: 'the balance comes due in 45s' });
            g.sfx('golden_pop', 0.5);
            g.hudNow();
          },
        });
        g.toast('⛓ a contract drifts down — read the terms');
      },
      onDefused(spec, fuseSecLeft, viaChannel, x, y) {
        g.bankGold(2);
        g.field.floatText('⛓ kept +2 🪙', x, y + 26, 'cf-pop--gold');
        return null;
      },
      exit() {
        if (debts.length) { g.toast('⛓ the court adjourns — your debts are forgiven'); debts.length = 0; }
      },
    };
  };

  // ---------------------------------------------------------------------------
  // THE UNDERTOW - The Current: wandering vortices drag everything toward
  // their eye, and holding a snap inside one takes half again as long. Win
  // that fight and it pays triple - and the water goes still. For a while.
  const undertow = () => {
    const BLUE = [90, 150, 255];
    const cur = [
      { px: 0.32, py: 0.45, sx: 0.11, sy: 0.16, ph: 0.0, ph2: 2.1, x: 0, y: 0, r: 190, stilled: 0 },
      { px: 0.68, py: 0.55, sx: 0.13, sy: 0.10, ph: 3.4, ph2: 0.9, x: 0, y: 0, r: 170, stilled: 0 },
    ];
    let t = Math.random() * 20;
    return {
      enter() {
        g.field.phys.currentChannelMult = 1.5;
        g.audioColor('underwater');
        g.toast('🌊 the undertow — it pulls. pull back.');
      },
      tick(dt) {
        t += dt;
        const W = window.innerWidth, H = window.innerHeight;
        const R = clamp(Math.min(W, H) * 0.18, 130, 240);
        const act = [];
        for (const c of cur) {
          if (c.stilled > 0) { c.stilled -= dt; continue; }
          c.x = W * (c.px + 0.26 * Math.sin(t * c.sx + c.ph));
          c.y = H * (c.py + 0.24 * Math.sin(t * c.sy + c.ph2));
          c.r = R;
          act.push(c);
        }
        g.field.phys.currents = act.map((c) => ({ x: c.x, y: c.y, r: c.r, pull: 60 }));
        g.ffx.setBeams(act.map((c) => ({ x: c.x, y: c.y, r: c.r, color: BLUE })));
      },
      onDefused(spec, fuseSecLeft, viaChannel, x, y) {
        if (!viaChannel) return null;
        let hit = null;
        for (const c of cur) if (c.stilled <= 0 && Math.hypot(x - c.x, y - c.y) <= c.r) { hit = c; break; }
        if (!hit) return null;
        hit.stilled = 12;
        g.announce('🌊 YOU BEAT THE CURRENT — ×3', 'powerup', 2200, { subText: 'it stills. for a while.' });
        g.sfx('streak_milestone', 0.5);
        g.pulse('120,180,255', 0.40);
        return { payMult: 3 };
      },
      exit() {
        g.field.phys.currents = null;
        g.field.phys.currentChannelMult = 1.0;
        g.ffx.setBeams(null);
        g.audioColor(null);
      },
    };
  };

  // ---------------------------------------------------------------------------
  // THE PINK CHAPEL - Communion: the room keeps time on a soft bell. Pop ON
  // the bell and the chapel answers - chaining, gilding, forgiving. Fight the
  // tempo and it pays you like a stranger. The bell runs on the real clock
  // (the 0.25s RunTick is too coarse to be a metronome); it goes quiet the
  // moment the RunTick does (hold/pause/draft).
  const communion = () => {
    const PERIOD = 1.8, WINDOW = 0.28;
    const t0 = performance.now() / 1000;
    let chain = 0, taught = false, timer = 0, lastTickAt = 0;
    const beatDist = () => {
      const ph = (((performance.now() / 1000 - t0) % PERIOD) + PERIOD) % PERIOD;
      return Math.min(ph, PERIOD - ph);
    };
    const scheduleBell = () => {
      const now = performance.now() / 1000;
      const next = PERIOD - ((((now - t0) % PERIOD) + PERIOD) % PERIOD);
      timer = window.setTimeout(() => {
        if (performance.now() - lastTickAt < 600) {   // the run is actually running
          g.sfx('countdown_tick', 0.14);
          g.pulse('255,214,230', 0.07);
        }
        scheduleBell();
      }, Math.max(30, next * 1000));
    };
    return {
      enter() { g.toast('🕊 the pink chapel — pop with the bell'); scheduleBell(); },
      tick() { lastTickAt = performance.now(); },
      treatPop(spec, x, y) {
        if (beatDist() <= WINDOW) {
          chain++;
          g.field.floatText(`♥ ×${chain}`, x, y - 12, 'cf-pop--gold');
          if (chain % 4 === 0 && g.field.gildNear(x, y, 190)) g.field.floatText('🕊 gilded', x, y - 34, 'cf-pop--gold');
          if (chain === 8) g.announce('🕊 COMMUNION', 'powerup', 2200, { subText: 'you stopped fighting the tempo' });
          return { payMult: 1.4 + 0.08 * Math.min(chain, 10) };
        }
        if (chain >= 3) g.toast('🕊 the chain breaks — listen for the bell');
        else if (!taught) { taught = true; g.toast('🕊 pop ON the bell — it pays like faith'); }
        chain = 0;
        return { payMult: 0.8 };
      },
      exit() { if (timer) { clearTimeout(timer); timer = 0; } },
    };
  };

  // ---------------------------------------------------------------------------
  // MIRROR LAKE - Acceptance: the answer to the Hall of Mirrors. Your armor
  // converts to reward on the way in (you don't resist here - you're paid for
  // it), the water shows you your favorite and calls it lovely, and every
  // grab is met with an affirmation instead of a question.
  const acceptance = () => {
    const AFFIRM = [
      'yes. this is you.', 'it was never a phase', 'you don’t have to explain it here',
      'you kept coming back for a reason', 'it fits. it always fit.',
      'nothing to fight. nothing to win. just this.',
    ];
    let reflIn = rand(22, 32), reflLeft = 0, affCd = 0, affIx = (Math.random() * AFFIRM.length) | 0;
    return {
      enter() {
        const st = g.st();
        const n = st.shields | 0;
        if (n > 0) {
          st.shields = 0;
          st.boonMult += 0.15 * n;
          g.announce('🪷 you won’t need armor here', 'powerup', 2600, { subText: `${n} shield${n > 1 ? 's' : ''} become ×${(0.15 * n).toFixed(2)} mult` });
          g.hudNow();
        }
        g.toast('🪷 mirror lake — the water is calm because you are');
      },
      tick(dt) {
        affCd -= dt;
        if (reflLeft > 0) {
          reflLeft -= dt;
          if (reflLeft <= 0) g.wall.setMirror(null);
          return;
        }
        reflIn -= dt;
        if (reflIn > 0) return;
        const fav = g.favoriteAsset('image');
        if (!fav) { reflIn = 40; return; }   // no engagement history yet: try again later
        reflIn = rand(30, 42);
        reflLeft = 10;
        g.wall.setMirror(fav.url);
        g.announce('🪷 look at the water', 'depth', 2400, { subText: 'that’s you. it’s okay.' });
        g.pulse('220,200,235', 0.30);
      },
      treatPop() { return reflLeft > 0 ? { payMult: 1.5 } : null; },
      onGrabbed() {
        if (affCd > 0) return;
        affCd = 8;
        g.toast(`🪷 ${AFFIRM[affIx++ % AFFIRM.length]}`);
        g.addHeat(0.02);
      },
      exit() { reflLeft = 0; g.wall.setMirror(null); },
    };
  };

  const REGISTRY = {
    mimics, keyhole, flicker, incognito,          // I  - Curiosity & Denial
    mirrors, greyward, vertigo, searchlight,      // II - Fear & Confusion
    gallery, casino, contracts, undertow,         // III - Bargain & Struggle
    velocity, coronation, communion, acceptance,  // IV - Surrender & Acceptance
  };

  const safe = (fn, ...a) => { try { return fn && fn(...a); } catch (e) { console.warn('[dtrh] biome mech:', e); return null; } };

  return {
    /** Chamber entry: swap mechanics (exit restores whatever the old one bent). */
    setBiome(regionIndex, biome) {
      if (activeBiome && biome && activeBiome.id === biome.id && impl) return;   // relapse re-entry: keep it running
      if (impl) safe(impl.exit);
      impl = null;
      activeBiome = biome || null;
      const make = biome && biome.mech && REGISTRY[biome.mech];
      if (make) { impl = make(); safe(impl.enter); }
    },
    reset() {
      if (impl) safe(impl.exit);
      impl = null; activeBiome = null;
      runGrabs = 0; grabTally.clear();
      if (g.audioColor) safe(g.audioColor, null);   // belt-and-braces: no biome color outlives a run
    },
    active: () => activeBiome,
    tick(dt) { if (impl && impl.tick) safe(impl.tick, dt); },
    treatPop(spec, x, y, src) { return impl && impl.treatPop ? safe(impl.treatPop, spec, x, y, src) : null; },
    payMultAt(x, y, spec) { return impl && impl.payMultAt ? (safe(impl.payMultAt, x, y, spec) || 1) : 1; },
    onGoldenPop(gold, x, y) { return impl && impl.onGoldenPop ? !!safe(impl.onGoldenPop, gold, x, y) : false; },
    onDefused(spec, fuseSecLeft, viaChannel, x, y) { return impl && impl.onDefused ? safe(impl.onDefused, spec, fuseSecLeft, viaChannel, x, y) : null; },
    onDetonated(spec, x, y) { if (impl && impl.onDetonated) safe(impl.onDetonated, spec, x, y); },
    grabPolicy(kind, rec) { return impl && impl.grabPolicy ? (safe(impl.grabPolicy, kind, rec) || 'allow') : 'allow'; },
    onGrabbed(kind, rec) {
      runGrabs++;
      const name = rec && rec.assetName;
      if (name) grabTally.set(name, (grabTally.get(name) || 0) + 1);
      if (impl && impl.onGrabbed) safe(impl.onGrabbed, kind, rec);
    },
    onMelted(kind, rec) { if (impl && impl.onMelted) safe(impl.onMelted, kind, rec); },
  };
}
