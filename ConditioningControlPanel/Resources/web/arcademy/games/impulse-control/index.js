/* ============================================================================
 * games/impulse-control/index.js - PLACEHOLDER STUB.
 *
 * Impulse Control (Go/No-Go reflex + restraint assessment; family: reflex).
 * Replaced wholesale by its game agent.
 *
 * Stub coverage: a declared keybind slot (`go`, default Space) so the keybind
 * framework has a second consumer and the PanicKey conflict check has something
 * to refuse, plus a HARD GATE on endClass - this is the game whose dual
 * speed+restraint S-gate made hard gates legal (SYNTHESIS #14), so the stub sends
 * `hardGates:{sGate:false}` to prove the rubric caps it at A.
 *
 * NOTE for the game agent: gif_rain / gif_burst are in effectsConsumed on purpose
 * (SYNTHESIS #11 - the commendation gif_burst is declared, DECISIONS #8 keeps the
 * fiction-crack), and the inverse audio lie is a grade_tier-4 set-piece with an
 * attributing debrief line (DECISIONS #7).
 * ==========================================================================*/

export default {
  key: 'impulse_control',
  family: 'reflex',
  meaty: false,
  flagship: false,
  timeBudgetSec: 90,
  title: 'Impulse Control',

  manifest: {
    effectsConsumed: ['sub_flash', 'audio_trigger', 'wash', 'gif_rain', 'gif_burst',
      'crt', 'ambient_field', 'glitch_swap'],
    assetNeeds: { loops: 8, targets: 1, stills: 0, canvasSafe: false },
    boardSizes: null,
    keybinds: [{ verb: 'go', label_key: 'ic_go_key', default: 'Space' }],
    settings: [
      { key: 'ic_inverse_audio', kind: 'bool', default: true, label_key: 'ic_inverse_audio' },
    ],
    peek: false,
  },

  create(ctx) {
    let ended = false;
    let suspendedEl = null;
    let offGo = null;
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };

    function finish(spec) {
      if (ended) return;
      ended = true;
      ctx.endClass({
        metrics: { composite: 0.62 },
        // The dual S-gate: declared and FAILED here, so the rubric caps at A. A
        // stub that always claimed the gate would hide the cap path.
        hardGates: { sGate: false },
        flavorXp: 5,
      });
      say('stub class ended at tier ' + spec.gradeTier + ' (sGate failed -> max A)');
    }

    return {
      start(classSpec) {
        const spec = classSpec || { gradeTier: 1, seed: 'none', timeBudgetSec: 90 };
        const root = ctx.root;
        root.textContent = '';
        const wrap = document.createElement('div');
        wrap.className = 'arc-stub';

        const h = document.createElement('h3');
        h.textContent = ctx.lexicon('game_impulse_control', 'Impulse Control');
        wrap.appendChild(h);
        const sub = document.createElement('p');
        sub.className = 'arc-note';
        sub.textContent = ctx.lexicon('class_placeholder', 'Class Placeholder')
          + ' - the real assessment lands with its game agent.';
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
        row('go key', ctx.keys.labelFor('go'));
        row('inverse audio', !!ctx.settings.ic_inverse_audio);
        // Per-game meta (SYNTHESIS #15): the real class keeps its RT baseline here.
        row('baseline', (ctx.store.gameMeta('impulse_control').baselineMs || 0) + 'ms');
        wrap.appendChild(dl);

        const tally = document.createElement('p');
        tally.className = 'arc-note';
        let taps = 0;
        tally.textContent = 'GO presses: 0';
        wrap.appendChild(tally);
        offGo = ctx.keys.on('go', () => {
          taps++;
          tally.textContent = 'GO presses: ' + taps;
        });

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn primary';
        btn.textContent = 'End class (B)';
        btn.addEventListener('click', () => finish(spec));
        wrap.appendChild(btn);

        root.appendChild(wrap);

        try {
          ctx.engine.setHeat(0.2 + 0.2 * (spec.gradeTier - 1));
          ctx.engine.fire('audio_trigger', { id: 'go_cue' });
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
        try { if (offGo) offGo(); } catch (e) { /* noop */ }
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
      },
    };
  },
};
