/* ============================================================================
 * ui/duel/arcCss.js - the Arcademy's shell stylesheet, fenced for the Goon page.
 * PURE: text in, text out. No DOM.
 *
 * A duel mounts a real Arcademy class (ui/duel/arcademyHost.js). Each class
 * injects its own sheet (games/<id>/style.js), but it also leans on the shell's
 * `arcademy/styles.css`: the palette variables (--lav, --pink, --ground, --disp
 * ...), the stamp and meter the ceremonies draw (.arc-stamp, .arc-meter), the
 * exit signs and the `.btn` a class borrows for its ticket. Loading that file
 * whole would restyle the Goon page (it owns `body`, `html`, `button` ...), so
 * this keeps only what a class can reach and pins it under one scope class:
 *
 *   :root { vars }               -> <scope> { vars }        (the palette)
 *   .arc-x / .btn rules          -> <scope> .arc-x          (class-only rules)
 *   @keyframes arc-*             -> kept as is              (names are arc- owned)
 *   @font-face                   -> kept, url() made absolute
 *   @media / @supports           -> the same filter inside, kept when non-empty
 *   anything else (html, body, #arc-..., campus rooms) -> dropped
 * ==========================================================================*/

const KEEP_PREFIXES = Object.freeze(['.arc-', '.btn']);

function stripComments(css) {
  return String(css || '').replace(/\/\*[\s\S]*?\*\//g, '');
}

/** Split top-level `prelude { body }` blocks (bodies may nest). */
export function topBlocks(css) {
  const out = [];
  const s = String(css || '');
  let i = 0;
  while (i < s.length) {
    const open = s.indexOf('{', i);
    if (open < 0) break;
    const prelude = s.slice(i, open).trim();
    let depth = 1;
    let j = open + 1;
    while (j < s.length && depth > 0) {
      const c = s[j];
      if (c === '{') depth++;
      else if (c === '}') depth--;
      j++;
    }
    out.push({ prelude: prelude.replace(/^[^@.:#a-zA-Z*\[]*;/, '').trim(), body: s.slice(open + 1, j - 1) });
    i = j;
  }
  return out;
}

function absolutize(body, base) {
  if (!base) return body;
  return body.replace(/url\(\s*(['"]?)([^'")]+)\1\s*\)/g, (m, q, u) => {
    if (/^(data:|https?:|blob:|#)/i.test(u)) return m;
    try { return 'url(' + q + new URL(u, base).href + q + ')'; } catch (_e) { return m; }
  });
}

function scopeSelector(sel, scope) {
  const s = sel.trim();
  if (!s) return null;
  if (s === ':root') return scope;
  for (const p of KEEP_PREFIXES) {
    if (s.indexOf(p) === 0) return scope + ' ' + s;
  }
  return null;
}

function scopeBlocks(css, scope, base) {
  let out = '';
  for (const b of topBlocks(css)) {
    const pre = b.prelude;
    if (!pre) continue;
    if (/^@(-webkit-)?keyframes\s+arc-/i.test(pre)) { out += pre + '{' + b.body + '}\n'; continue; }
    if (/^@font-face/i.test(pre)) { out += pre + '{' + absolutize(b.body, base) + '}\n'; continue; }
    if (/^@(media|supports)/i.test(pre)) {
      const inner = scopeBlocks(b.body, scope, base);
      if (inner.trim()) out += pre + '{\n' + inner + '}\n';
      continue;
    }
    if (pre[0] === '@') continue;
    const sels = pre.split(',').map((x) => scopeSelector(x, scope)).filter(Boolean);
    if (!sels.length) continue;
    out += sels.join(', ') + '{' + absolutize(b.body, base) + '}\n';
  }
  return out;
}

/**
 * @param {string} css   arcademy/styles.css, as text
 * @param {string} scope the fence, e.g. '.gg-arc'
 * @param {string} [base] the stylesheet's own url, for its relative url()s
 */
export function scopeArcCss(css, scope, base) {
  return scopeBlocks(stripComments(css), scope, base || '');
}

export default scopeArcCss;
