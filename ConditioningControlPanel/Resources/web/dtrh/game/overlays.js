/* ============================================================================
 * overlays.js - the run's overlay screens: the 3·2·1·GO countdown, the boon
 * DRAFT table on every loop boundary (pick a mantra / accept a sin / resist
 * for +1 resistance, with the Taking Chances reroll and the WPF auto-resume
 * countdown), the post-pick "Ready? -> GO!" beat, and the results recap with
 * the payout lines that arrive over the bridge as payout-result.
 * ==========================================================================*/

const ART = 'https://ccp.art/';

export function createOverlays(hud) {
  // ---- countdown ----
  const cd = document.createElement('div');
  cd.className = 'cf-overlay cf-countdown';
  cd.hidden = true;
  hud.appendChild(cd);
  const cdNum = document.createElement('div');
  cdNum.className = 'cf-countdown-num';
  cd.appendChild(cdNum);
  let cdTimers = [];
  const clearCd = () => { for (const t of cdTimers) clearTimeout(t); cdTimers = []; };

  /** 3 · 2 · 1 · GO! (or a short lone GO! on restart). onTick fires per beat
   * (the host plays countdown_tick), onGo when the run should begin. */
  function showCountdown({ short = false, onTick, onGo }) {
    clearCd();
    cd.hidden = false;
    const beats = short ? ['GO!'] : ['3', '2', '1', 'GO!'];
    beats.forEach((b, i) => {
      cdTimers.push(window.setTimeout(() => {
        cdNum.textContent = b;
        cdNum.classList.toggle('is-go', b === 'GO!');
        cdNum.classList.remove('is-beat');
        void cdNum.offsetWidth;
        cdNum.classList.add('is-beat');
        if (onTick) onTick(b);
        if (b === 'GO!') {
          cdTimers.push(window.setTimeout(() => { cd.hidden = true; if (onGo) onGo(); }, short ? 450 : 650));
        }
      }, i * 1000));
    });
  }

  /** RETIRED closing beat (3 · 2 · 1, then onDone): replaced by the Surfacing -
   * showSurfaceWash below + chaosRun's tickSurfacing ramps. Kept callable. */
  function showFinishCountdown(onDone, { onTick } = {}) {
    clearCd();
    cd.hidden = false;
    cdNum.classList.remove('is-go');
    const beats = ['3', '2', '1'];
    beats.forEach((b, i) => {
      cdTimers.push(window.setTimeout(() => {
        cdNum.textContent = b;
        cdNum.classList.remove('is-beat');
        void cdNum.offsetWidth;
        cdNum.classList.add('is-beat');
        if (onTick) onTick(b);
      }, i * 1000));
    });
    cdTimers.push(window.setTimeout(() => { cd.hidden = true; if (onDone) onDone(); }, beats.length * 1000));
  }

  /** The post-draft beat: "Ready? :3" then a GO! flash, then resume. */
  function showReadyGo(onResume, { onTick } = {}) {
    clearCd();
    cd.hidden = false;
    cdNum.classList.remove('is-go');
    cdNum.textContent = 'Ready? :3';
    cdNum.classList.remove('is-beat');
    void cdNum.offsetWidth;
    cdNum.classList.add('is-beat');
    if (onTick) onTick('ready');
    cdTimers.push(window.setTimeout(() => {
      cdNum.textContent = 'GO!';
      cdNum.classList.add('is-go');
      cdNum.classList.remove('is-beat');
      void cdNum.offsetWidth;
      cdNum.classList.add('is-beat');
      if (onTick) onTick('GO!');
      cdTimers.push(window.setTimeout(() => { cd.hidden = true; if (onResume) onResume(); }, 450));
    }, 900));
  }

  // ---- surface wash (the run's diegetic close) ----
  // A soft white-out that replaces the finish 3·2·1: the world bleaches over
  // inMs, onPeak fires under full white (endRun -> the recap renders beneath
  // it), then the white lifts over outMs and the recap is simply... there.
  const wash = document.createElement('div');
  wash.className = 'cf-surface-wash';
  wash.hidden = true;
  hud.appendChild(wash);
  let washTimers = [];
  const clearWashTimers = () => { for (const t of washTimers) clearTimeout(t); washTimers = []; };

  function showSurfaceWash(onPeak, { inMs = 3400, holdMs = 300, outMs = 1600 } = {}) {
    clearWashTimers();
    wash.hidden = false;
    wash.style.transition = 'none';
    wash.style.opacity = '0';
    void wash.offsetWidth;
    wash.style.transition = `opacity ${inMs}ms ease-in`;
    wash.style.opacity = '1';
    washTimers.push(window.setTimeout(() => {
      if (onPeak) onPeak();
      washTimers.push(window.setTimeout(() => {
        wash.style.transition = `opacity ${outMs}ms ease-out`;
        wash.style.opacity = '0';
        washTimers.push(window.setTimeout(() => { wash.hidden = true; }, outMs + 80));
      }, holdMs));
    }, inMs));
  }

  /** Abort the wash (a run torn down mid-close must not leave the screen white
   * or fire a stale endRun from the pending peak timer). */
  function cancelSurfaceWash() {
    clearWashTimers();
    wash.style.transition = 'none';
    wash.style.opacity = '0';
    wash.hidden = true;
  }

  // ---- boon draft table ----
  const dr = document.createElement('div');
  dr.className = 'cf-overlay cf-draft';
  dr.hidden = true;
  hud.appendChild(dr);
  let draftTimer = 0, draftCountdown = 0;

  function clearDraftTimers() {
    clearInterval(draftTimer);
    draftTimer = 0;
  }

  /** Deal the table: pick a card, take the SKIP (+1 resistance), or reroll
   * (Taking Chances). Untouched for autoResumeSec -> auto-resolve, so an
   * unattended run never freezes forever. allowSkip mirrors the draft_skip
   * reveal (run 3+): before it, there is no resist button and the timeout
   * PICKS a card for you instead of banking resistance. */
  function showDraft({ wave, options, autoResumeSec = 15, rerollsLeft = 0, allowSkip = true, onPick, onSkip, onReroll }) {
    clearDraftTimers();
    dr.innerHTML = '';
    dr.hidden = false;

    const card = document.createElement('div');
    card.className = 'cf-draft-card';
    dr.appendChild(card);

    const h = document.createElement('h2');
    h.textContent = `loop ${wave} clear — she offers`;
    card.appendChild(h);

    const row = document.createElement('div');
    row.className = 'cf-draft-row';
    card.appendChild(row);

    const finish = (fn, arg) => {
      clearDraftTimers();
      dr.hidden = true;
      if (fn) fn(arg);
    };

    const renderOptions = (opts) => {
      row.innerHTML = '';
      for (const b of opts) {
        const c = document.createElement('button');
        c.type = 'button';
        c.className = 'cf-boon' + (b.curse ? ' cf-boon--sin' : '')
          + (b.requiresAny || b.requiresAll ? ' cf-boon--duo' : '')
          + ` cf-boon--${(b.rarity || 'Common').toLowerCase()}`;
        const img = document.createElement('img');
        img.className = 'cf-boon-art';
        img.src = `${ART}boons/${b.id}.png`;
        img.alt = '';
        img.addEventListener('error', () => img.remove());
        const name = document.createElement('div');
        name.className = 'cf-boon-name';
        name.textContent = `${b.curse ? '☠ ' : '◈ '}${b.name}`;
        const desc = document.createElement('div');
        desc.className = 'cf-boon-desc';
        desc.textContent = b.desc;
        const flavor = document.createElement('div');
        flavor.className = 'cf-boon-flavor';
        flavor.textContent = b.flavor || '';
        const tag = document.createElement('div');
        tag.className = 'cf-boon-tag';
        tag.textContent = b.curse ? 'a sin' : (b.rarity || '').toLowerCase();
        c.append(img, name, desc, flavor, tag);
        c.addEventListener('click', () => finish(onPick, b));
        row.appendChild(c);
      }
    };
    let currentOptions = options;
    renderOptions(currentOptions);

    const btns = document.createElement('div');
    btns.className = 'cf-draft-btns';
    card.appendChild(btns);

    if (rerollsLeft > 0 && onReroll) {
      const rr = document.createElement('button');
      rr.type = 'button';
      rr.className = 'sf-btn';
      rr.textContent = `🎲 reroll (${rerollsLeft})`;
      rr.addEventListener('click', () => {
        const res = onReroll();
        if (!res) { rr.disabled = true; return; }
        currentOptions = res.options;
        renderOptions(currentOptions);
        rr.textContent = `🎲 reroll (${res.rerollsLeft})`;
        if (res.rerollsLeft <= 0) rr.remove();
      });
      btns.appendChild(rr);
    }

    if (allowSkip) {
      const skip = document.createElement('button');
      skip.type = 'button';
      skip.className = 'sf-btn';
      skip.textContent = '♥ resist (+1 resistance)';
      skip.addEventListener('click', () => finish(onSkip, false));
      btns.appendChild(skip);
    }

    if (autoResumeSec > 0) {
      const auto = document.createElement('div');
      auto.className = 'cf-draft-auto';
      card.appendChild(auto);
      draftCountdown = autoResumeSec;
      auto.textContent = `she chooses for you in ${draftCountdown}s`;
      draftTimer = window.setInterval(() => {
        draftCountdown--;
        if (draftCountdown <= 0) {
          // Skip revealed: she banks the resistance. Before that: she PICKS.
          if (allowSkip) finish(onSkip, true);
          else {
            const pick = currentOptions[(Math.random() * currentOptions.length) | 0];
            finish((b) => onPick(b, true), pick);
          }
          return;
        }
        auto.textContent = `she chooses for you in ${draftCountdown}s`;
      }, 1000);
    }
  }

  // ---- recap ----
  const rc = document.createElement('div');
  rc.className = 'cf-overlay cf-recap';
  rc.hidden = true;
  hud.appendChild(rc);
  const card = document.createElement('div');
  card.className = 'cf-recap-card';
  rc.appendChild(card);

  const DIFF_NAMES = { Easy: 'Gentle', Medium: 'Teasing', Hard: 'Relentless', Extreme: 'Inescapable' };
  const line = (cls, text) => {
    const p = document.createElement('p');
    p.className = cls;
    p.textContent = text;
    card.appendChild(p);
    return p;
  };

  let payoutSlot = null;
  let lastScore = 0;

  /** The recap shell goes up immediately with the run stats; the payout lines
   * fill in when the bridge answers run-ended with payout-result. */
  function showRecap(stats, { onAgain, onSurface }) {
    lastScore = stats.score;
    card.innerHTML = '';
    const h = document.createElement('h2');
    h.textContent = 'you surface…';
    card.appendChild(h);
    line('cf-recap-score', `${Math.floor(stats.score).toLocaleString()} pts`);
    line('', `${DIFF_NAMES[stats.difficulty] || stats.difficulty} · ${stats.waveCount} loops · you sank ${Math.round(stats.depth).toLocaleString()} m`);
    line('', `best streak ×${Math.max(1, stats.bestCombo)} · ${stats.defused} snapped · ${stats.detonated} triggered`);
    // THE BIOMES: the route this fall took (one rolled place per room). When THE
    // COMPACT is owned the route moves into the mirror section below instead.
    if (!stats.compact && stats.biomes && stats.biomes.length) {
      line('', `the fall took you through ${stats.biomes.map((b) => `${b.glyph} ${b.name}`).join(' → ')}`);
    }
    if (stats.trickleDrops > 0) line('', `💧 drip feed gathered ${Math.floor(stats.trickleDrops)} ✦`);
    // THE PAPERWALL (Part 3): a torn Lookbook page pinned itself on the way up
    if (stats.pageTorn) line('cf-recap-page', '🗞 a page tore loose from her Lookbook — it’s pinned in THE BOUDOIR.');

    // THE COMPACT (crafted, Part 2): flip it open - picks, haul, route. Owners
    // only; without it the recap above is byte-identical to the shipped one.
    if (stats.compact) {
      const mirror = document.createElement('div');
      mirror.className = 'cf-recap-mirror';
      const mh = document.createElement('p');
      mh.className = 'cf-recap-mirror-h';
      mh.textContent = '— the mirror —';
      mirror.appendChild(mh);
      if (stats.picks && stats.picks.length) {
        const chips = document.createElement('div');
        chips.className = 'cf-recap-mirror-picks';
        for (const p of stats.picks) {
          const c = document.createElement('span');
          c.className = 'cf-recap-mirror-chip'
            + (p.curse ? ' is-curse' : '') + (p.toy ? ' is-toy' : '');
          c.textContent = `${p.glyph} ${p.name}`;
          chips.appendChild(c);
        }
        mirror.appendChild(chips);
      } else {
        const none = document.createElement('p');
        none.textContent = 'you took nothing. the mirror remembers that too.';
        mirror.appendChild(none);
      }
      if (stats.materials && stats.materials.length) {
        const haul = document.createElement('p');
        haul.textContent = 'the haul: ' + stats.materials.map((m) => `${m.glyph} ×${m.count}`).join(' · ');
        mirror.appendChild(haul);
      }
      if (stats.biomes && stats.biomes.length) {
        const route = document.createElement('p');
        route.textContent = `the route: ${stats.biomes.map((b) => `${b.glyph} ${b.name}`).join(' → ')}`;
        mirror.appendChild(route);
      }
      card.appendChild(mirror);
    }
    payoutSlot = document.createElement('div');
    payoutSlot.className = 'cf-recap-payout';
    payoutSlot.textContent = 'tallying…';
    card.appendChild(payoutSlot);

    const row = document.createElement('div');
    row.className = 'cf-recap-btns';
    const again = document.createElement('button');
    again.type = 'button';
    again.className = 'sf-btn sf-btn-primary';
    again.textContent = 'fall again';
    again.addEventListener('click', onAgain);
    const leave = document.createElement('button');
    leave.type = 'button';
    leave.className = 'sf-btn';
    leave.textContent = 'wake up';
    leave.addEventListener('click', onSurface);
    row.append(again, leave);
    card.appendChild(row);
    rc.hidden = false;
  }

  function showPayout(p) {
    if (!payoutSlot || rc.hidden) return;
    payoutSlot.innerHTML = '';
    const l1 = document.createElement('p');
    l1.textContent = `+${Math.round(p.baseXp)} XP` + (p.skillMult > 1 ? ` × ${p.skillMult.toFixed(1)} skills = ${Math.round(p.finalXp)} XP` : '');
    payoutSlot.appendChild(l1);
    if (p.sparksEarned > 0) {
      const l2 = document.createElement('p');
      l2.textContent = `+${p.sparksEarned} ✦ emotes banked`;
      payoutSlot.appendChild(l2);
    }
    if (p.rankUp) {
      const l3 = document.createElement('p');
      l3.className = 'cf-recap-rankup';
      l3.textContent = `RANK UP — ${p.rankUp}`;
      payoutSlot.appendChild(l3);
    }
    if (p.previousBest > 0 && lastScore > p.previousBest) {
      const l4 = document.createElement('p');
      l4.textContent = 'new personal best!';
      payoutSlot.appendChild(l4);
    }
  }

  return {
    showCountdown,
    showFinishCountdown,
    showReadyGo,
    hideCountdown() { clearCd(); cd.hidden = true; },
    showSurfaceWash,
    cancelSurfaceWash,
    showDraft,
    isDraftUp: () => !dr.hidden,
    showRecap,
    showPayout,
    hideRecap() { rc.hidden = true; payoutSlot = null; },
    isRecapUp: () => !rc.hidden,
    dispose() { clearCd(); clearDraftTimers(); clearWashTimers(); cd.remove(); dr.remove(); rc.remove(); wash.remove(); },
  };
}
