/* ============================================================================
 * games/daily-trigger/index.js - PLACEHOLDER STUB.
 *
 * Daily Trigger (Wordle, homeroom, family: word, flagship). This file exists so
 * the shell can boot, deal a board, run a full class loop and reach the report
 * card BEFORE the game agents start (BUILD-CONTRACT §12). The game agent for
 * daily_trigger REPLACES this file wholesale; the only things it must keep are
 * the exported shape below and the manifest keys the shell renders.
 *
 * What the stub does: renders a "class placeholder" card that proves the ctx
 * wiring (tier, seed, budget, lexicon, settings, engine allowlist), then ends the
 * class with a B. It also emits a real emoji-grid SHARE payload, because it is
 * the ONE payload v1's share pipeline renders (DECISIONS #6) and that path needs
 * something to exercise it.
 *
 * Games NEVER: import another game, touch bridge.js, re-expose a global setting,
 * or fire an effect outside manifest.effectsConsumed (the shell's engine handle
 * enforces the allowlist and logs the attempt).
 * ==========================================================================*/

export default {
  key: 'daily_trigger',
  family: 'word',
  meaty: false,
  flagship: true,
  timeBudgetSec: 90,
  title: 'Daily Trigger',

  manifest: {
    effectsConsumed: ['sub_flash', 'glitch_swap', 'wash', 'audio_trigger', 'ambient_field'],
    assetNeeds: { loops: 0, targets: 0, stills: 0, canvasSafe: false },
    boardSizes: null,
    keybinds: null,
    settings: [
      { key: 'dt_hard_mode', kind: 'bool', default: false, label_key: 'dt_hard_mode' },
    ],
    peek: false,
  },

  create(ctx) {
    let ended = false;
    let suspendedEl = null;
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };

    function card(spec) {
      const root = ctx.root;
      root.textContent = '';
      const wrap = document.createElement('div');
      wrap.className = 'arc-stub';

      const h = document.createElement('h3');
      h.textContent = ctx.lexicon('game_daily_trigger', 'Daily Trigger');
      wrap.appendChild(h);
      const sub = document.createElement('p');
      sub.className = 'arc-note';
      sub.textContent = ctx.lexicon('class_placeholder', 'Class Placeholder')
        + ' - the real class lands with its game agent.';
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
      row('hard mode', !!ctx.settings.dt_hard_mode);
      row('roll', ctx.rng().toFixed(4));
      wrap.appendChild(dl);

      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'btn primary';
      btn.textContent = 'End class (B)';
      btn.addEventListener('click', () => finish(spec));
      wrap.appendChild(btn);

      root.appendChild(wrap);
    }

    function finish(spec) {
      if (ended) return;
      ended = true;
      // composite 0.62 -> B through the shared rubric (core/grades.js).
      ctx.endClass({
        metrics: { composite: 0.62 },
        flavorXp: 3,
        share: {
          kind: 'emoji_grid',
          puzzleNumber: 1 + Math.floor(ctx.rng() * 400),
          attempts: 4,
          max: 6,
          solved: true,
          hardMode: !!ctx.settings.dt_hard_mode,
          rows: [
            ['miss', 'near', 'miss', 'miss', 'miss'],
            ['miss', 'hit', 'near', 'miss', 'miss'],
            ['near', 'hit', 'hit', 'miss', 'near'],
            ['hit', 'hit', 'hit', 'hit', 'hit'],
          ],
          storm: ['🫧', '🌀', '⚡'],
        },
      });
      say('stub class ended at tier ' + spec.gradeTier);
    }

    return {
      start(classSpec) {
        const spec = classSpec || { gradeTier: 1, seed: 'none', timeBudgetSec: 90 };
        card(spec);
        // Prove the engine seam (and the allowlist) without depending on it.
        try {
          ctx.engine.setHeat(0.2 + 0.2 * (spec.gradeTier - 1));
          ctx.engine.fire('sub_flash', { strength: 20 });
          ctx.engine.fire('flash_burst', {});   // NOT declared: expect a refusal log
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
      destroy() { try { ctx.root.textContent = ''; } catch (e) { /* noop */ } },
    };
  },
};
