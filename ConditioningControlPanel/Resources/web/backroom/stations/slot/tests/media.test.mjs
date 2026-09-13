import test from 'node:test';
import assert from 'node:assert/strict';
import { fxSymbols } from '../media.js';

// keyFor as media.js deals it: gif{n} -> g{n}, sub{n} -> s{n}, gif3 never dealt.
const media = { keyFor: id => (/^gif[0-2]$/.test(id) ? `g${id[3]}` : /^sub\d$/.test(id) ? `s${id[3]}` : null) };

test('fx carry dealt KEYS for the symbols they are about, never urls', () => {
  const o = { symbols: ['gif1', 'sub0', 'gif3'], subs: ['sub0'] };
  assert.deepEqual(fxSymbols('fx.sub_single', o, media), ['s0']);
  assert.deepEqual(fxSymbols('fx.gif_burst', o, media), ['g1'], 'undealt gif3 is left out');
  assert.deepEqual(fxSymbols('fx.jackpot', o, media), ['g1', 's0']);
  assert.deepEqual(fxSymbols('fx.melt', { symbols: ['melt', 'spiral0', 'emi'] }, media), []);
  assert.deepEqual(fxSymbols('fx.gif_storm', { symbols: ['gif2', 'gif2', 'gif2'] }, media), ['g2']);
});
