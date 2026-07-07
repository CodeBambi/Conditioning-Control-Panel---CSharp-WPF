/* ============================================================================
 * overlays.js - the run's bookend screens: the 3·2·1·GO countdown and the
 * results recap (score breakdown + the payout lines that arrive over the
 * bridge as payout-result). Both render over the live tunnel - the countdown
 * during the opening plunge, the recap while the fall idles at a crawl.
 * ==========================================================================*/

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
      l2.textContent = `+${p.sparksEarned} ✦ sparks banked`;
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
    hideCountdown() { clearCd(); cd.hidden = true; },
    showRecap,
    showPayout,
    hideRecap() { rc.hidden = true; payoutSlot = null; },
    isRecapUp: () => !rc.hidden,
    dispose() { clearCd(); cd.remove(); rc.remove(); },
  };
}
