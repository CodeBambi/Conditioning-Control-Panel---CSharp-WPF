/* ============================================================================
 * games/password.js - THE PASSWORD GAME (Emergency Exit game #2).
 *
 * Five rounds of stacking rules. One text input, a live checklist of every
 * active rule (ember check / crimson x, a small glitch on every flip), a Confirm
 * button that only enables when all rules pass. Each round adds one rule and
 * keeps the previous ones valid. Rules are picked per run with api.rng from a
 * pool of 14 in escalating absurdity (tier 1 -> 3), with conflict guards so the
 * set is always satisfiable. Round 4 or 5 carries ONE rule that changes once
 * after it is satisfied: the Confirm press that would finish that round instead
 * updates the rule with a visible ember "oops, rule updated" and the player has
 * to fix it (deterministic, always fires once per run). After round 5 Confirm
 * -> finish("completed"). No fail state except quitting: this is friction.
 *
 * Photosafe: no shake on rule updates / flips (soft tint instead) via the
 * .ee-photosafe class in password.css.
 *
 * Test seam: EE.games.password._test = { RULES, pickRules, check, romanValue,
 *            isPrime, digitSum, hasDigitRun } (pure where possible).
 * ==========================================================================*/
(function () {
  'use strict';
  var EE = window.EE = window.EE || {};
  EE.games = EE.games || {};

  var ROUNDS = 5;
  var PRIMES = {};
  (function () { for (var i = 2; i < 200; i++) { var p = true; for (var j = 2; j * j <= i; j++) if (i % j === 0) { p = false; break; } if (p) PRIMES[i] = true; } })();
  function isPrime(n) { return !!PRIMES[n]; }
  function digitSum(s) { var m = String(s).match(/\d/g); if (!m) return 0; return m.reduce(function (a, d) { return a + (d | 0); }, 0); }
  /** does s contain the number n (plain substring; "07" also satisfies 7, and vice versa) */
  function hasDigitRun(s, n) {
    var str = String(s), want = String(n);
    if (str.indexOf(want) >= 0) return true;
    var alt = (want.length === 1) ? '0' + want : (want.length === 2 && want[0] === '0' ? want.slice(1) : null);
    return !!(alt && str.indexOf(alt) >= 0);
  }
  var ROMAN_STRICT = /^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$/;
  var ROMAN_VAL = { I: 1, V: 5, X: 10, L: 50, C: 100, D: 500, M: 1000 };
  function romanValue(r) {
    if (!r || !ROMAN_STRICT.test(r)) return 0;
    var v = 0;
    for (var i = 0; i < r.length; i++) {
      var a = ROMAN_VAL[r[i]], b = ROMAN_VAL[r[i + 1]] || 0;
      v += (a < b) ? -a : a;
    }
    return v;
  }
  /** best (largest) roman numeral value among runs of >= 2 roman letters in s (0 if none) */
  function bestRoman(s) {
    var runs = String(s).match(/[IVXLCDM]{2,}/g) || [], best = 0;
    for (var i = 0; i < runs.length; i++) {
      var run = runs[i];
      // test the run and its substrings (so "BambiXIVx" or "XIVII" still yield something valid)
      for (var a = 0; a < run.length; a++) for (var b = run.length; b - a >= 2; b--) {
        var v = romanValue(run.slice(a, b)); if (v > best) best = v;
      }
    }
    return best;
  }
  function countEmoji(s) { var m = String(s).match(/\p{Extended_Pictographic}/gu); return m ? m.length : 0; }
  function charsOf(s) { return Array.from(String(s)); }
  function lc(s) { return String(s || '').toLowerCase(); }

  /* ---------------------------------------------------------------------------
   * RULE POOL
   * Each: { id, tier, text(ctx), ok(pw, ctx), needsText?: string used by the
   *         no-letter-e conflict guard, mutate?(ctx) -> void (changes the
   *         rule ONCE; ctx.st[id] holds per-rule state), live?: bool }
   * ctx = { mod, honorific, subject, weekday, weekdayLocal, minutesLeft(), hour(), st, rng }
   * ------------------------------------------------------------------------- */
  var RULES = [
    { id: 'min8', tier: 1,
      text: function (c) { return 'at least <b>' + (c.st.min8 || 8) + '</b> characters'; },
      ok: function (pw, c) { return charsOf(pw).length >= (c.st.min8 || 8); },
      mutate: function (c) { c.st.min8 = 12; } },
    { id: 'number', tier: 1,
      text: function () { return 'contains a <b>number</b>'; },
      ok: function (pw) { return /\d/.test(pw); } },
    { id: 'subject', tier: 1,
      text: function (c) { return 'contains <b>' + esc(c.subject) + '</b>'; },
      ok: function (pw, c) { return lc(pw).indexOf(lc(c.subject)) >= 0; },
      needsText: function (c) { return c.subject; } },
    { id: 'sameEnds', tier: 1,
      text: function (c) { return c.st.sameEnds ? 'starts and ends with the same <b>letter</b>' : 'starts and ends with the same <b>character</b>'; },
      ok: function (pw, c) { var ch = charsOf(pw); if (ch.length < 2) return false; var a = ch[0], b = ch[ch.length - 1]; if (a !== b) return false; return c.st.sameEnds ? /\p{L}/u.test(a) : true; },
      mutate: function (c) { c.st.sameEnds = true; } },
    { id: 'digitsum', tier: 2,
      text: function (c) { return 'the digits add up to <b>' + dsTarget(c) + '</b>'; },
      ok: function (pw, c) { return digitSum(pw) === dsTarget(c); },
      mutate: function (c) { c.st.dsBump = 3 + Math.floor(c.rng() * 3); } },
    { id: 'weekday', tier: 2,
      text: function (c) { return 'contains <b>today\'s weekday</b>'; },
      ok: function (pw, c) { var p = lc(pw); return p.indexOf(lc(c.weekday)) >= 0 || (c.weekdayLocal && p.indexOf(lc(c.weekdayLocal)) >= 0); },
      needsText: function (c) { return c.weekday; } },
    { id: 'honorific', tier: 2,
      text: function (c) { return 'contains <b>' + esc(c.honorific) + '</b>'; },
      ok: function (pw, c) { return lc(pw).indexOf(lc(c.honorific)) >= 0; },
      needsText: function (c) { return c.honorific; } },
    { id: 'please', tier: 2,
      text: function (c) { return 'contains <b>' + (c.st.please ? 'pretty please' : 'please') + '</b>'; },
      ok: function (pw, c) { return lc(pw).indexOf(c.st.please ? 'pretty please' : 'please') >= 0; },
      needsText: function () { return 'please'; },
      mutate: function (c) { c.st.please = true; } },
    { id: 'roman', tier: 2,
      text: function (c) { return c.st.roman ? 'contains a <b>roman numeral</b> worth at least <b>' + c.st.roman + '</b>' : 'contains a <b>roman numeral</b> (two letters or more, like XIV)'; },
      ok: function (pw, c) { var v = bestRoman(pw); return c.st.roman ? v >= c.st.roman : v > 0; },
      mutate: function (c) { c.st.roman = 10 + Math.floor(c.rng() * 3) * 5; } },
    { id: 'hour', tier: 2,
      text: function (c) { return 'contains the <b>current hour</b> (24h, right now <b>' + pad2(c.hour()) + '</b>)'; },
      ok: function (pw, c) { return hasDigitRun(pw, c.hour()); },
      live: true },
    { id: 'prime', tier: 2,
      text: function (c) { return c.st.prime ? 'the length is a <b>prime number</b> of at least <b>' + c.st.prime + '</b>' : 'the length is a <b>prime number</b>'; },
      ok: function (pw, c) { var n = charsOf(pw).length; return isPrime(n) && (!c.st.prime || n >= c.st.prime); },
      mutate: function (c) { c.st.prime = 17; } },
    { id: 'minutes', tier: 3,
      text: function (c) { return 'contains the number of <b>lockdown minutes left</b> (right now <b class="pw-live">' + c.minutesLeft() + '</b>)'; },
      ok: function (pw, c) { return hasDigitRun(pw, c.minutesLeft()); },
      live: true },
    { id: 'emoji', tier: 3,
      text: function (c) { return c.st.emoji ? 'contains <b>two emoji</b>' : 'contains an <b>emoji</b>'; },
      ok: function (pw, c) { return countEmoji(pw) >= (c.st.emoji ? 2 : 1); },
      mutate: function (c) { c.st.emoji = true; } },
    { id: 'noE', tier: 3,
      text: function () { return 'does <b>not</b> contain the letter <b>e</b>'; },
      ok: function (pw) { return !/e/i.test(pw); } }
  ];

  function dsTarget(c) {
    // base 24..33, never below what the required numbers already force (+2 so there is room)
    var base = c.st.dsBase || 24;
    var forced = 0;
    if (c.active.indexOf('minutes') >= 0) forced += digitSum(String(c.minutesLeft()));
    if (c.active.indexOf('hour') >= 0) forced += digitSum(pad2(c.hour()));
    var t = Math.max(base, forced + 2) + (c.st.dsBump || 0);
    return t;
  }
  function pad2(n) { return (n < 10 ? '0' : '') + n; }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (ch) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]; }); }

  function byId(id) { for (var i = 0; i < RULES.length; i++) if (RULES[i].id === id) return RULES[i]; return null; }

  /**
   * Pick 5 rule ids in escalating absurdity (tiers 1,1-2,2,2-3,2-3), honouring:
   * - noE never with a rule whose required text contains an "e"
   * - round 4 or 5 holds exactly one rule with mutate() (the "oops, rule updated" beat)
   */
  function pickRules(rng, ctx) {
    var tiersPerRound = [[1], [1, 2], [2], [2, 3], [2, 3]];
    var picked = [], tries = 0;
    function conflicts(rule, set) {
      var all = set.concat([rule]);
      var hasNoE = all.some(function (r) { return r.id === 'noE'; });
      if (hasNoE) {
        for (var i = 0; i < all.length; i++) {
          var r = all[i]; if (!r.needsText) continue;
          if (/e/i.test(String(r.needsText(ctx) || ''))) return true;
        }
      }
      // hour + minutes together with sameEnds is fine; subject + honorific both fine.
      return false;
    }
    while (tries++ < 200) {
      picked = [];
      for (var round = 0; round < ROUNDS; round++) {
        var tiers = tiersPerRound[round];
        var cands = RULES.filter(function (r) {
          return tiers.indexOf(r.tier) >= 0 && picked.indexOf(r) < 0 && !conflicts(r, picked);
        });
        if (!cands.length) break;
        picked.push(cands[Math.floor(rng() * cands.length)]);
      }
      if (picked.length < ROUNDS) continue;
      // mutator: exactly one of rounds 4/5 must have mutate(); prefer the one with it, swap if none
      var m3 = !!picked[3].mutate, m4 = !!picked[4].mutate;
      if (!m3 && !m4) {
        var alt = RULES.filter(function (r) { return r.mutate && (r.tier === 2 || r.tier === 3) && picked.indexOf(r) < 0 && !conflicts(r, picked.slice(0, 4)); });
        if (!alt.length) continue;
        picked[4] = alt[Math.floor(rng() * alt.length)];
      }
      var mutatorRound = (picked[4].mutate) ? 4 : 3;
      if (!picked[3].mutate && !picked[4].mutate) continue;
      return { ids: picked.map(function (r) { return r.id; }), mutatorRound: mutatorRound };
    }
    // deterministic fallback (always satisfiable, has a mutator in round 5)
    return { ids: ['min8', 'number', 'weekday', 'roman', 'digitsum'], mutatorRound: 4 };
  }

  /** evaluate all active rules -> [{id, ok}] */
  function check(pw, ctx) {
    return ctx.active.map(function (id) { var r = byId(id); var ok = false; try { ok = !!r.ok(pw, ctx); } catch (_e) { ok = false; } return { id: id, ok: ok }; });
  }

  /* ---------------------------------------------------------------------------
   * GAME
   * ------------------------------------------------------------------------- */
  var G = null;
  var DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

  function start(init, api) {
    var now = new Date();
    var weekdayLocal = '';
    try { weekdayLocal = now.toLocaleDateString(init.lang || 'en', { weekday: 'long' }); } catch (_e) {}
    var mod = init.mod || {};
    var ctx = {
      mod: mod,
      honorific: mod.honorific || 'good girl',
      subject: mod.subject || mod.name || 'Bambi',
      weekday: DAYS[now.getDay()],
      weekdayLocal: (weekdayLocal && lc(weekdayLocal) !== lc(DAYS[now.getDay()])) ? weekdayLocal : '',
      minutesLeft: function () { return Math.max(0, Math.floor((api.remainingSec ? api.remainingSec() : (init.remainingSec || 0)) / 60)); },
      hour: function () { return new Date().getHours(); },
      st: { dsBase: 24 + Math.floor(api.rng() * 10) },
      rng: api.rng,
      active: []
    };
    var plan = pickRules(api.rng, ctx);

    var mount = api.mount; mount.innerHTML = '';
    var card = document.createElement('section'); card.className = 'pw-card ee-card';
    card.innerHTML =
      '<div class="pw-head">' +
        '<h2 class="pw-title">the <em>password</em> game</h2>' +
        '<div class="pw-round"><span class="pw-pips" id="pw-pips"></span><span id="pw-roundtxt">round 1 of ' + ROUNDS + '</span></div>' +
      '</div>' +
      '<p class="pw-sub" id="pw-sub">Choose a password for the exit. Every round adds a rule. All rules stay. Confirm five times and the door opens.</p>' +
      '<div class="pw-field">' +
        '<input class="pw-input" id="pw-input" type="text" autocomplete="off" autocorrect="off" autocapitalize="off" spellcheck="false" maxlength="96" placeholder="type the password here" aria-label="password" aria-describedby="pw-rules">' +
        '<div class="pw-meta"><b id="pw-len">0</b> chars<br><b id="pw-ok">0</b>/<span id="pw-total">1</span> rules</div>' +
      '</div>' +
      '<ul class="pw-rules" id="pw-rules" aria-live="polite"></ul>' +
      '<div class="pw-foot">' +
        '<span class="pw-hint"><kbd>Enter</kbd> confirms when every rule is satisfied</span>' +
        '<button type="button" class="ee-btn is-primary pw-confirm" id="pw-confirm" disabled>Confirm</button>' +
      '</div>';
    mount.appendChild(card);

    G = {
      api: api, ctx: ctx, plan: plan, round: 0, mutated: false, states: {}, els: {
        input: card.querySelector('#pw-input'), rules: card.querySelector('#pw-rules'), confirm: card.querySelector('#pw-confirm'),
        pips: card.querySelector('#pw-pips'), roundTxt: card.querySelector('#pw-roundtxt'), len: card.querySelector('#pw-len'),
        ok: card.querySelector('#pw-ok'), total: card.querySelector('#pw-total'), sub: card.querySelector('#pw-sub')
      },
      rows: {}, liveTick: 0, lastLive: {}, done: false
    };
    for (var i = 0; i < ROUNDS; i++) { var p = document.createElement('span'); p.className = 'pw-pip'; G.els.pips.appendChild(p); }

    G.els.input.addEventListener('input', onInput);
    G.els.input.addEventListener('keydown', function (e) { if (e.key === 'Enter' && !G.els.confirm.disabled) { e.preventDefault(); onConfirm(); } });
    G.els.confirm.addEventListener('click', onConfirm);
    G.liveTick = setInterval(onLive, 1000);

    api.hud.timer(null);
    api.say('a password. for the door. simple, right? hihi');
    addRound();
    setTimeout(function () { try { G && G.els.input.focus({ preventScroll: true }); } catch (_e) {} }, 60);
  }

  function addRound() {
    var id = G.plan.ids[G.round];
    G.ctx.active.push(id);
    var li = document.createElement('li');
    li.className = 'pw-rule is-new is-bad'; li.dataset.id = id;
    li.innerHTML = '<span class="pw-ico" aria-hidden="true">×</span><span class="pw-text"></span><span class="pw-num">rule ' + (G.round + 1) + '</span>';
    G.els.rules.appendChild(li);
    G.rows[id] = li;
    G.states[id] = false;
    renderRuleText(id);
    G.els.total.textContent = String(G.ctx.active.length);
    G.els.roundTxt.textContent = 'round ' + (G.round + 1) + ' of ' + ROUNDS;
    var pips = G.els.pips.children;
    for (var i = 0; i < pips.length; i++) { pips[i].classList.toggle('is-done', i < G.round); pips[i].classList.toggle('is-now', i === G.round); }
    setTimeout(function () { try { li.classList.remove('is-new'); } catch (_e) {} }, 600);
    evaluate(true);
    try { li.scrollIntoView({ block: 'nearest' }); } catch (_e) {}
  }

  function renderRuleText(id) {
    var r = byId(id), li = G.rows[id]; if (!r || !li) return;
    var t = li.querySelector('.pw-text');
    var html = r.text(G.ctx);
    if (li.dataset.updated === '1') html += '<span class="pw-tag">oops, rule updated</span>';
    t.innerHTML = html;
  }

  function evaluate(silent) {
    var pw = G.els.input.value;
    var res = check(pw, G.ctx), okCount = 0;
    res.forEach(function (x) {
      var li = G.rows[x.id]; if (!li) return;
      var was = G.states[x.id];
      if (was !== x.ok) {
        G.states[x.id] = x.ok;
        li.classList.toggle('is-ok', x.ok); li.classList.toggle('is-bad', !x.ok);
        li.querySelector('.pw-ico').textContent = x.ok ? '✓' : '×';
        if (!silent) { li.classList.remove('is-flip'); void li.offsetWidth; li.classList.add('is-flip'); }
      }
      if (x.ok) okCount++;
    });
    G.els.len.textContent = String(charsOf(pw).length);
    G.els.ok.textContent = String(okCount);
    var all = okCount === res.length && res.length > 0;
    G.els.confirm.disabled = !all || G.done;
    G.els.input.classList.toggle('is-all-ok', all);
  }

  function onInput() { if (G && !G.done) evaluate(false); }

  /** live rules (hour / minutes / digit sum target) re-render when their value changes */
  function onLive() {
    if (!G || G.done) return;
    var changed = false;
    G.ctx.active.forEach(function (id) {
      var r = byId(id); if (!r) return;
      var key = null;
      if (id === 'minutes') key = String(G.ctx.minutesLeft());
      else if (id === 'hour') key = String(G.ctx.hour());
      else if (id === 'digitsum') key = String(dsTarget(G.ctx));
      if (key == null) return;
      if (G.lastLive[id] !== undefined && G.lastLive[id] !== key) {
        renderRuleText(id);
        var li = G.rows[id]; li.classList.remove('ee-glitch'); void li.offsetWidth; li.classList.add('ee-glitch');
        setTimeout(function () { try { li.classList.remove('ee-glitch'); } catch (_e) {} }, 600);
        changed = true;
        if (id === 'minutes') G.api.say('the minutes keep changing. that is not a bug, {honorific}. that is the lockdown.');
      }
      G.lastLive[id] = key;
    });
    if (changed) evaluate(false);
  }

  var ROUND_LINES = [
    'good start. one more rule.',
    'see? easy. let us make it less easy.',
    'you are doing so well. here is something silly.',
    'almost there. the rules have one more surprise.',
    'last one. really. probably.'
  ];

  function onConfirm() {
    if (!G || G.done) return;
    evaluate(true);
    if (G.els.confirm.disabled) return;
    // the mutator beat: the Confirm that would close the mutator round updates the rule instead
    if (!G.mutated && G.round === G.plan.mutatorRound) {
      var mid = G.plan.ids[G.round];
      var r = byId(mid);
      if (r && r.mutate) {
        G.mutated = true;
        r.mutate(G.ctx);
        var li = G.rows[mid];
        li.dataset.updated = '1';
        renderRuleText(mid);
        li.classList.remove('is-updated'); void li.offsetWidth; li.classList.add('is-updated');
        G.api.say('oops. rule updated. hihi. read it again, {honorific}.');
        evaluate(false);
        try { G.els.input.focus({ preventScroll: true }); } catch (_e) {}
        return;
      }
    }
    G.round++;
    if (G.round >= ROUNDS) { onWin(); return; }
    G.api.say(ROUND_LINES[Math.min(G.round - 1, ROUND_LINES.length - 1)]);
    addRound();
    try { G.els.input.focus({ preventScroll: true }); } catch (_e) {}
  }

  function onWin() {
    G.done = true;
    G.els.confirm.disabled = true;
    G.els.input.readOnly = true;
    var pips = G.els.pips.children;
    for (var i = 0; i < pips.length; i++) { pips[i].classList.add('is-done'); pips[i].classList.remove('is-now'); }
    G.els.roundTxt.textContent = 'password accepted';
    G.els.sub.textContent = 'Password accepted. Checking it against the door...';
    G.api.say('password accepted. let me just check that with the door.');
    var len = charsOf(G.els.input.value).length, rules = G.plan.ids.slice();
    setTimeout(function () {
      if (!G) return;
      G.api.finish('completed', { rounds: ROUNDS, length: len, rules: rules });
    }, 800);
  }

  function destroy() {
    if (!G) return;
    clearInterval(G.liveTick);
    try { G.els.input.removeEventListener('input', onInput); G.els.confirm.removeEventListener('click', onConfirm); } catch (_e) {}
    G = null;
  }

  EE.games.password = {
    id: 'password', start: start, destroy: destroy,
    _test: { RULES: RULES, pickRules: pickRules, check: check, romanValue: romanValue, bestRoman: bestRoman, isPrime: isPrime, digitSum: digitSum, hasDigitRun: hasDigitRun, dsTarget: dsTarget, countEmoji: countEmoji }
  };
  if (typeof EE.registerGame === 'function') EE.registerGame(EE.games.password);
})();
