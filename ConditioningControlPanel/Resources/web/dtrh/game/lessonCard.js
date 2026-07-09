/* ============================================================================
 * lessonCard.js - the in-run "first discovery" explainer. The FIRST time ever
 * you meet something in a run - grab a power-up, a new bubble type surfaces, a
 * pickup/weather debuts, a core verb comes up - the field freezes and a single
 * card explains it. Click (or press any key) to dismiss and the fall resumes.
 *
 * It reuses the VN's world-hold contract verbatim: the caller passes the same
 * `haltTunnel` (director.setTimeFactor(0)) and `hold` (vnHold+syncHeld + audio
 * duck) callbacks it builds for createVnPortrait, so both freeze the run the
 * same way and never talk over each other (a discovery can't fire while a card
 * or a VN beat holds the field).
 *
 * Cards QUEUE: while one is up the world is held, so nothing new discovers
 * mid-card; if several are marked in one tick (e.g. a wave start), they show
 * back-to-back with no resume/flicker between them.
 *
 * API:  const lc = createLessonCard(hud, { haltTunnel, hold });
 *       lc.enqueue({ glyph, name, desc, flavor, accent, kicker, onDismiss });
 *       lc.isBusy();  lc.dispose();
 * ==========================================================================*/

const DEFAULT_ACCENT = '255,105,180';
const ARM_MS = 300;     // ignore dismiss input this long after a card appears (a grab click won't bounce it off)
const FADE_MS = 460;    // out-fade before the node hides
const RESUME_MS = 400;  // matches vnPortrait.scheduleResume: hold across back-to-back cards

const CSS = `
.lc-root{position:absolute;inset:0;z-index:42;pointer-events:none;overflow:hidden;opacity:0;
  transition:opacity .38s ease}
.lc-root.lc-on{opacity:1;pointer-events:auto;cursor:pointer}
.lc-dim{position:absolute;inset:0;background:
  radial-gradient(70% 80% at 50% 42%, rgba(var(--lcA),.16), transparent 62%),
  linear-gradient(180deg, rgba(4,2,9,.74) 0%, rgba(4,2,9,.82) 60%, rgba(4,2,9,.9) 100%)}
.lc-card{position:absolute;left:50%;top:50%;transform:translate(-50%,-46%) scale(.965);
  width:min(78vw,620px);z-index:3;opacity:0;transition:opacity .3s ease, transform .34s cubic-bezier(.2,.8,.2,1);
  background:rgba(9,4,15,.94);border:2.5px solid rgb(var(--lcA));border-radius:22px;
  padding:34px 40px 30px;text-align:center;
  box-shadow:0 0 44px rgba(var(--lcA),.5), 0 18px 50px rgba(0,0,0,.6), inset 0 0 30px rgba(var(--lcA),.12)}
.lc-root.lc-on .lc-card{opacity:1;transform:translate(-50%,-50%) scale(1)}
.lc-kicker{color:rgb(var(--lcA));font-weight:800;letter-spacing:.3em;text-transform:uppercase;
  font-size:.72rem;margin-bottom:1.1em;text-shadow:0 0 14px rgba(var(--lcA),.7)}
.lc-glyph{font-size:clamp(46px,8vh,74px);line-height:1;margin-bottom:.18em;
  filter:drop-shadow(0 0 18px rgba(var(--lcA),.6));animation:lcPulse 2.6s ease-in-out infinite}
@keyframes lcPulse{0%,100%{transform:scale(1)}50%{transform:scale(1.07)}}
.lc-name{font-size:clamp(28px,4vh,40px);font-weight:800;color:#fdf1ff;letter-spacing:.01em;
  margin-bottom:.5em;text-shadow:0 1px 10px #000,0 0 22px rgba(var(--lcA),.4)}
.lc-desc{font-size:clamp(16px,2.2vh,21px);line-height:1.5;color:#e8dcf2;font-weight:500}
.lc-flavor{margin-top:1em;font-size:clamp(14px,1.9vh,18px);line-height:1.45;color:rgba(255,255,255,.62);
  font-style:italic}
.lc-hint{margin-top:1.5em;font-size:.72rem;letter-spacing:.18em;text-transform:uppercase;
  color:rgba(255,255,255,.42);opacity:0;transition:opacity .4s}
.lc-root.lc-on .lc-hint{opacity:.8;transition-delay:.5s}
`;

export function createLessonCard(hud, opts = {}) {
  const haltTunnel = typeof opts.haltTunnel === 'function' ? opts.haltTunnel : null;
  const hold = typeof opts.hold === 'function' ? opts.hold : null;

  if (!document.getElementById('lc-style')) {
    const st = document.createElement('style');
    st.id = 'lc-style'; st.textContent = CSS; document.head.appendChild(st);
  }

  const root = document.createElement('div');
  root.className = 'lc-root'; root.hidden = true;
  root.innerHTML =
    '<div class="lc-dim"></div>' +
    '<div class="lc-card">' +
      '<div class="lc-kicker"></div>' +
      '<div class="lc-glyph"></div>' +
      '<div class="lc-name"></div>' +
      '<div class="lc-desc"></div>' +
      '<div class="lc-flavor"></div>' +
      '<div class="lc-hint">click to continue ▸</div>' +
    '</div>';
  hud.appendChild(root);

  const $ = (s) => root.querySelector(s);
  const kickerEl = $('.lc-kicker'), glyphEl = $('.lc-glyph'), nameEl = $('.lc-name'),
        descEl = $('.lc-desc'), flavorEl = $('.lc-flavor');

  const queue = [];
  let current = null;      // the card on screen
  let armAt = 0;           // performance.now() before which dismiss input is ignored
  let restoreTimer = 0;
  let fadeTimer = 0;

  function sceneHold(on) {
    if (on && restoreTimer) { clearTimeout(restoreTimer); restoreTimer = 0; }
    try { if (haltTunnel) haltTunnel(on); } catch (e) { /* ignore */ }
    try { if (hold) hold(on); } catch (e) { /* ignore */ }
  }
  function scheduleResume() {
    if (!haltTunnel && !hold) return;
    if (restoreTimer) clearTimeout(restoreTimer);
    // brief delay so a back-to-back card keeps the field held (no flicker between cards)
    restoreTimer = setTimeout(() => {
      restoreTimer = 0;
      try { if (haltTunnel) haltTunnel(false); } catch (e) {}
      try { if (hold) hold(false); } catch (e) {}
    }, RESUME_MS);
  }

  function showNext() {
    if (current || !queue.length) return;
    const copy = queue.shift();
    current = copy;
    if (fadeTimer) { clearTimeout(fadeTimer); fadeTimer = 0; }

    const accent = copy.accent || DEFAULT_ACCENT;
    root.style.setProperty('--lcA', accent);
    kickerEl.textContent = copy.kicker || 'discovered';
    kickerEl.style.display = copy.kicker === '' ? 'none' : '';
    glyphEl.textContent = copy.glyph || '';
    glyphEl.style.display = copy.glyph ? '' : 'none';
    nameEl.textContent = copy.name || '';
    descEl.textContent = copy.desc || '';
    flavorEl.textContent = copy.flavor || '';
    flavorEl.style.display = copy.flavor ? '' : 'none';

    root.hidden = false;
    requestAnimationFrame(() => root.classList.add('lc-on'));
    sceneHold(true);
    armAt = performance.now() + ARM_MS;
    try { if (copy.onShown) copy.onShown(); } catch (e) { /* ignore */ }
  }

  function dismiss() {
    if (!current) return;
    const copy = current; current = null;
    try { if (copy.onDismiss) copy.onDismiss(); } catch (e) { /* ignore */ }

    root.classList.remove('lc-on');
    if (queue.length) {
      // keep the field held; swap to the next card once this one has faded
      if (fadeTimer) clearTimeout(fadeTimer);
      fadeTimer = setTimeout(() => { fadeTimer = 0; showNext(); }, FADE_MS);
    } else {
      scheduleResume();
      if (fadeTimer) clearTimeout(fadeTimer);
      fadeTimer = setTimeout(() => { fadeTimer = 0; if (!current) root.hidden = true; }, FADE_MS);
    }
  }

  function tryDismiss() {
    if (!current) return;
    if (performance.now() < armAt) return;   // ignore the grab-click / stray input that rode in with the card
    dismiss();
  }

  const onPointer = () => { if (root.classList.contains('lc-on')) tryDismiss(); };
  const onKey = (e) => {
    if (!current) return;
    // ignore lone modifier presses; anything else advances
    if (e.key === 'Shift' || e.key === 'Control' || e.key === 'Alt' || e.key === 'Meta') return;
    // Swallow the key so it can't ALSO leak to the game's window handlers (ESC->pause,
    // number keys->consumables) while the card holds the stage. Capture phase beats them.
    e.preventDefault(); e.stopPropagation();
    if (e.stopImmediatePropagation) e.stopImmediatePropagation();
    tryDismiss();
  };
  root.addEventListener('pointerdown', onPointer);
  window.addEventListener('keydown', onKey, true);

  function enqueue(copy) {
    if (!copy || (!copy.name && !copy.desc)) return;
    queue.push(copy);
    if (!current) showNext();
  }

  function dispose() {
    if (restoreTimer) { clearTimeout(restoreTimer); restoreTimer = 0; }
    if (fadeTimer) { clearTimeout(fadeTimer); fadeTimer = 0; }
    queue.length = 0; current = null;
    try { sceneHold(false); } catch (e) {}
    root.removeEventListener('pointerdown', onPointer);
    window.removeEventListener('keydown', onKey, true);
    try { root.remove(); } catch (e) {}
  }

  return {
    enqueue,
    isBusy: () => !!current || queue.length > 0,
    dispose,
  };
}
