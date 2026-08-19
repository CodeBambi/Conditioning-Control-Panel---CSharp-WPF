/* ============================================================================
 * games/lost-and-found/index.js - PLACEHOLDER STUB.
 *
 * Lost & Found (Where's Waldo over a drifting gif mosaic; family: search;
 * flagship; heaviest asset-provider consumer). Replaced wholesale by its game
 * agent - the exported shape and the manifest keys the shell renders are the
 * only contract.
 *
 * This stub is the one that exercises the two PROMOTED mechanisms end to end:
 *   - the shared PEEK verb (manifest.peek + a `peek` keybind slot). Holding it
 *     caps the class at A, and the shell - not this file - applies the cap.
 *   - the board-size row (manifest.boardSizes), including the "below tier par
 *     caps at A" rule the shell computes from `par`.
 * It also claims an asset pool so the provider seam is proven before the real
 * mosaic exists.
 *
 * NOTE for the game agent: flash_burst is deliberately NOT in effectsConsumed.
 * A clickable hydra flash over a click-precision board poisons input trust
 * (DECISIONS #9); bubble_field covers the occlusion role instead.
 * ==========================================================================*/

export default {
  key: 'lost_and_found',
  family: 'search',
  meaty: false,
  flagship: true,
  timeBudgetSec: 120,
  title: 'Lost & Found',

  manifest: {
    effectsConsumed: ['glitch_swap', 'row_drift', 'sub_flash', 'bubble_field', 'wash',
      'audio_trigger', 'crt', 'ambient_field'],
    assetNeeds: { loops: 24, targets: 1, stills: 0, canvasSafe: false },
    boardSizes: { values: [16, 20, 30, 40], par: { 1: 16, 2: 20, 3: 30, 4: 40 } },
    keybinds: [{ verb: 'peek', label_key: 'lf_peek_key', default: 'Space' }],
    settings: [
      { key: 'lf_zen', kind: 'bool', default: false, label_key: 'lf_zen' },
    ],
    peek: true,
  },

  create(ctx) {
    let ended = false;
    let pool = null;
    let suspendedEl = null;
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };

    function finish(spec) {
      if (ended) return;
      ended = true;
      const zen = !!ctx.settings.lf_zen;
      ctx.endClass({
        metrics: { composite: 0.62 },
        zen,
        flavorXp: 2,
      });
      say('stub class ended at tier ' + spec.gradeTier + (zen ? ' (zen -> pass)' : ''));
    }

    function card(spec) {
      const root = ctx.root;
      root.textContent = '';
      const wrap = document.createElement('div');
      wrap.className = 'arc-stub';

      const h = document.createElement('h3');
      h.textContent = ctx.lexicon('game_lost_and_found', 'Lost & Found');
      wrap.appendChild(h);
      const sub = document.createElement('p');
      sub.className = 'arc-note';
      sub.textContent = ctx.lexicon('class_placeholder', 'Class Placeholder')
        + ' - the real mosaic lands with its game agent.';
      wrap.appendChild(sub);

      const dl = document.createElement('dl');
      const row = (k, v) => {
        const dt = document.createElement('dt'); dt.textContent = k;
        const dd = document.createElement('dd'); dd.textContent = String(v);
        dl.appendChild(dt); dl.appendChild(dd);
      };
      row('grade_tier', spec.gradeTier);
      row('seed', spec.seed);
      row('budget', spec.timeBudgetSec + 's');
      row('board', ctx.settings.boardSize == null ? '-' : ctx.settings.boardSize);
      row('zen', !!ctx.settings.lf_zen);
      row('peek key', ctx.keys.labelFor('peek'));
      wrap.appendChild(dl);

      // The shared peek verb: one button + the declared keybind, both wired to
      // the SAME shell primitive so the A-cap can only be applied once.
      const reveal = document.createElement('p');
      reveal.className = 'arc-note';
      reveal.textContent = ctx.lexicon('peek_hint', 'Hold to peek. Using it caps this class at A.');
      wrap.appendChild(reveal);

      const peekBtn = document.createElement('button');
      peekBtn.type = 'button';
      peekBtn.className = 'arc-peekbtn';
      peekBtn.textContent = ctx.lexicon('peek', 'Peek') + ' (' + ctx.keys.labelFor('peek') + ')';
      ctx.peek.attach(peekBtn);
      ctx.peek.bindKeys(ctx.keys, 'peek');
      wrap.appendChild(peekBtn);

      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'btn primary';
      btn.textContent = 'End class (B)';
      btn.addEventListener('click', () => finish(spec));
      wrap.appendChild(btn);

      root.appendChild(wrap);
      return { reveal };
    }

    return {
      start(classSpec) {
        const spec = classSpec || { gradeTier: 1, seed: 'none', timeBudgetSec: 120 };
        const ui = card(spec);
        // Peek's reveal/hide are the game's business; the verb (and its A-cap)
        // belong to the shell.
        ctx.peek.setHandlers({
          onReveal: () => { ui.reveal.style.color = 'var(--gold)'; },
          onHide: () => { ui.reveal.style.color = ''; },
        });
        // Provider seam: claim what the manifest declares, never block on it.
        Promise.resolve()
          .then(() => ctx.assets.claim({
            loops: 24, targets: 1, stills: 0, canvasSafe: false,
          }))
          .then((p) => {
            pool = p;
            const first = pool && pool.next ? pool.next('loop') : null;
            ui.reveal.textContent += first
              ? '  [pool ready: ' + (first.remote ? 'remote' : 'local') + ']'
              : '  [pool empty - local fallback]';
          })
          .catch((e) => say('asset claim failed (degrading): ' + ((e && e.message) || e)));
        try {
          ctx.engine.setHeat(0.15 + 0.2 * (spec.gradeTier - 1));
          ctx.engine.sustain('row_drift', { strength: 15 * spec.gradeTier });
        } catch (e) { say('engine seam: ' + ((e && e.message) || e)); }
      },
      pause() { say('paused'); },
      resume() { say('resumed'); },
      suspend(on) {
        if (on && !suspendedEl) {
          suspendedEl = document.createElement('div');
          suspendedEl.className = 'arc-suspended';
          const h = document.createElement('h2');
          h.className = 'arc-h2';
          h.textContent = ctx.lexicon('class_suspended', 'Class Suspended');
          suspendedEl.appendChild(h);
          ctx.root.appendChild(suspendedEl);
        } else if (!on && suspendedEl) {
          suspendedEl.remove();
          suspendedEl = null;
        }
      },
      destroy() {
        try { if (pool && pool.release) pool.release(); } catch (e) { /* noop */ }
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
      },
    };
  },
};
