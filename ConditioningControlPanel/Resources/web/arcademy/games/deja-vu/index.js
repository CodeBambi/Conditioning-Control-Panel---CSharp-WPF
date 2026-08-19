/* ============================================================================
 * games/deja-vu/index.js - PLACEHOLDER STUB.
 *
 * Deja Vu (memory / pairs; family: memory). Replaced wholesale by its game agent.
 *
 * Stub coverage: the board-size row (grid size, with tier par) and the shared
 * peek verb standing in for Cram Assist - which is the same verb under a skin
 * (SYNTHESIS #6), NOT a second mechanism. It also declares a per-game bool so the
 * settings page has a second game to render rows for.
 *
 * NOTE for the game agent: DECISIONS #9 amended this game - flash_burst may not
 * be clickable over the tap board. It is left out of effectsConsumed here so the
 * allowlist itself enforces the amendment until the real class arrives.
 * ==========================================================================*/

export default {
  key: 'deja_vu',
  family: 'memory',
  meaty: false,
  flagship: false,
  timeBudgetSec: 90,
  title: 'Deja Vu',

  manifest: {
    effectsConsumed: ['glitch_swap', 'sub_flash', 'wash', 'audio_trigger', 'bubble_field', 'crt'],
    assetNeeds: { loops: 12, targets: 0, stills: 0, canvasSafe: false },
    boardSizes: { values: [8, 10, 12], par: { 1: 8, 2: 8, 3: 10, 4: 12 } },
    keybinds: null,
    settings: [
      { key: 'dv_freeze_matched', kind: 'bool', default: true, label_key: 'dv_freeze_matched' },
    ],
    peek: true,
  },

  create(ctx) {
    let ended = false;
    let suspendedEl = null;
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };

    function finish(spec) {
      if (ended) return;
      ended = true;
      ctx.endClass({ metrics: { composite: 0.62 }, flavorXp: 4 });
      say('stub class ended at tier ' + spec.gradeTier);
    }

    return {
      start(classSpec) {
        const spec = classSpec || { gradeTier: 1, seed: 'none', timeBudgetSec: 90 };
        const root = ctx.root;
        root.textContent = '';
        const wrap = document.createElement('div');
        wrap.className = 'arc-stub';

        const h = document.createElement('h3');
        h.textContent = ctx.lexicon('game_deja_vu', 'Deja Vu');
        wrap.appendChild(h);
        const sub = document.createElement('p');
        sub.className = 'arc-note';
        sub.textContent = ctx.lexicon('class_placeholder', 'Class Placeholder')
          + ' - the real pairs board lands with its game agent.';
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
        row('pairs', ctx.settings.boardSize == null ? '-' : ctx.settings.boardSize);
        row('freeze matched', !!ctx.settings.dv_freeze_matched);
        wrap.appendChild(dl);

        const hint = document.createElement('p');
        hint.className = 'arc-note';
        hint.textContent = ctx.lexicon('peek_hint', 'Hold to peek. Using it caps this class at A.');
        wrap.appendChild(hint);

        const peekBtn = document.createElement('button');
        peekBtn.type = 'button';
        peekBtn.className = 'arc-peekbtn';
        peekBtn.textContent = ctx.lexicon('peek', 'Peek');
        ctx.peek.setHandlers({
          onReveal: () => { hint.style.color = 'var(--gold)'; },
          onHide: () => { hint.style.color = ''; },
        });
        ctx.peek.attach(peekBtn);
        wrap.appendChild(peekBtn);

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn primary';
        btn.textContent = 'End class (B)';
        btn.addEventListener('click', () => finish(spec));
        wrap.appendChild(btn);

        root.appendChild(wrap);

        try {
          ctx.engine.setHeat(0.1 + 0.22 * (spec.gradeTier - 1));
          if (spec.gradeTier >= 2) ctx.engine.fire('glitch_swap', { strength: 30 });
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
