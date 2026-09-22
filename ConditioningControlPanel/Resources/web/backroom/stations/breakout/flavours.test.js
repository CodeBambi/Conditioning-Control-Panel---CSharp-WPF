import { test } from 'node:test';
import assert from 'node:assert/strict';
import { FLAVOURS, MINE, MAX_NICHES, flavourHost, flavourById, currentFlavour, applyFlavour, cleanNiche, nichesOf, liveSubs, toggleNiche, addNiche, removeNiche, readCustom, writeCustom, shellNiches } from './flavours.js';

const shell = (config) => { const calls = []; return { calls, get: () => ({ ...config }), set: async (...a) => { calls.push(a); if (config.refuse) throw new Error('no'); return {}; } }; };
const memory = () => { const m = new Map(); return { get: k => (m.has(k) ? m.get(k) : null), set: (k, v) => m.set(k, v) }; };
const by = id => flavourById(id);

test('five flavours, a censored one among them, each a short list of niche names the shell will accept', () => {
  assert.equal(FLAVOURS.length, 5); assert.ok(by('censored'));
  assert.equal(new Set(FLAVOURS.map(f => f.id)).size, 5);
  for (const f of FLAVOURS) {
    assert.ok(f.name && f.line && /^#[0-9a-f]{6}$/.test(f.tint));
    assert.ok(f.subs.length >= 2 && f.subs.length + f.extras.length <= MAX_NICHES);
    for (const s of [...f.subs, ...f.extras]) assert.equal(cleanNiche(s), s, 'the shell drops any other shape without a word');
    assert.ok(!(f.name + f.line).includes('!'), 'no exclamation marks in chrome');
  }
  assert.equal(by('mine'), MINE); assert.equal(by('nope'), null);
});

test('a typed niche: a name, r/name or a link all come down to the bare name; junk is refused', () => {
  for (const typed of ['chastity', 'r/chastity', '/r/chastity', ' chastity ', 'https://www.reddit.com/r/chastity/', 'scrolller.com/r/chastity?sort=top', 'https://old.reddit.com/r/chastity'])
    assert.equal(cleanNiche(typed), 'chastity', typed);
  for (const typed of ['', 'a', 'two words', 'semi;colon', 'a,b', null, 'x'.repeat(41)]) assert.equal(cleanNiche(typed), '', String(typed));
});

test('the player sees everything inside: core niches on, suggestions off, and can flip either', () => {
  const pink = by('pink');
  assert.deepEqual(nichesOf(pink).map(n => [n.name, n.kind, n.on]), [['bimbofication', 'core', true], ['Bimbos', 'core', true], ['BimboOrNot', 'core', true], ['bimbo', 'extra', false], ['BimboHypno', 'extra', false]]);
  let c = toggleNiche(pink, null, 'bimbos');                       // any case
  c = toggleNiche(pink, c, 'bimbo');
  assert.deepEqual(liveSubs(pink, c), ['bimbofication', 'BimboOrNot', 'bimbo']);
  c = toggleNiche(pink, toggleNiche(pink, c, 'Bimbos'), 'bimbo');
  assert.deepEqual(liveSubs(pink, c), pink.subs, 'and back again');
  assert.deepEqual(toggleNiche(pink, c, 'nobody'), c, 'an unknown name changes nothing');
});

test('adding a niche of their own: it goes in switched on, can be switched off and removed, and junk or a repeat says why', () => {
  const trance = by('trance');
  let r = addNiche(trance, null, 'r/chastity'); assert.equal(r.error, '');
  assert.deepEqual(liveSubs(trance, r.custom), [...trance.subs, 'chastity']);
  assert.deepEqual(nichesOf(trance, r.custom).at(-1), { name: 'chastity', kind: 'added', on: true });
  assert.equal(addNiche(trance, r.custom, 'CHASTITY').error, 'Already in.');
  assert.match(addNiche(trance, r.custom, 'two words').error, /not a niche name/);
  const off = toggleNiche(trance, r.custom, 'chastity'); assert.deepEqual(liveSubs(trance, off), trance.subs);
  assert.equal(addNiche(trance, off, 'chastity').error, '', 'typing a switched off niche again switches it back on');
  assert.deepEqual(liveSubs(trance, addNiche(trance, off, 'chastity').custom), [...trance.subs, 'chastity']);
  assert.deepEqual(liveSubs(trance, addNiche(trance, null, 'GoonCaves').custom), [...trance.subs, 'GoonCaves'], 'typing a suggestion switches it on');
  assert.deepEqual(nichesOf(trance, removeNiche(off, 'chastity')).map(n => n.name), [...trance.subs, ...trance.extras]);
  let many = null; for (let i = 0; i < 12; i++) many = addNiche(MINE, many, 'niche_' + i).custom;
  assert.ok(nichesOf(MINE, many).length <= MAX_NICHES + 2); assert.equal(liveSubs(MINE, many).length, MAX_NICHES, 'the shell gets eight at most');
});

test('the changes survive in a store, and a broken store is just the defaults', () => {
  const store = memory(), all = { pink: addNiche(by('pink'), null, 'bimbo').custom };
  writeCustom(store, all); assert.deepEqual(readCustom(store), all);
  assert.deepEqual(readCustom({ get: () => 'not json' }), {}); assert.deepEqual(readCustom({ get: () => '[1]' }), {});
  assert.deepEqual(readCustom({ get() { throw new Error('x'); } }), {});
});

test('no shell, no prompt: the host is only there when it can both answer and switch', () => {
  assert.equal(flavourHost(null), null); assert.equal(flavourHost({}), null);
  assert.equal(flavourHost({ __brMedia: { get() {} } }), null);
  const m = { get() {}, set() {} }; assert.equal(flavourHost({ __brMedia: m }), m);
});

test('the current flavour is recognised whatever the order or case, with the player changes counted; a list of nobody is nobody', () => {
  const pink = by('pink');
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: [...pink.subs].reverse().map(s => s.toUpperCase()) })), pink);
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: [...pink.subs, 'chastity'] })), null);
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: [...pink.subs, 'chastity'], disabledSources: ['chastity'] })), pink, 'a niche switched off in the shell does not count');
  const mine = { pink: addNiche(pink, null, 'chastity').custom };
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: [...pink.subs, 'chastity'] }), mine), pink, 'their Pink has chastity in it');
  assert.equal(currentFlavour(shell({ mode: 'scrolller', sources: ['a_b', 'c_d'] }), { mine: { added: ['c_d', 'a_b'] } }), MINE);
  assert.equal(currentFlavour(shell({ mode: 'bundled', sources: pink.subs })), null);
  assert.equal(currentFlavour({ get() { throw new Error('x'); } }), null);
  assert.deepEqual(shellNiches(shell({ mode: 'scrolller', sources: ['a_b', 'c_d'], disabledSources: ['A_B'] })), ['c_d']);
});

test('using a flavour switches the shell to Scrolller with exactly the niches switched on; an empty list or a refusal is a quiet no', async () => {
  const s = shell({ mode: 'bundled', sources: [] }), shiny = by('shiny');
  assert.equal(await applyFlavour(s, shiny, toggleNiche(shiny, null, 'latexcosplay')), true);
  assert.deepEqual(s.calls, [['scrolller', 'ShinyPorn,Dronification', []]]);
  assert.equal(await applyFlavour(shell({ refuse: true }), shiny), false, 'a refusal is an answer, never a throw');
  const none = shell({}); assert.equal(await applyFlavour(none, MINE, null), false); assert.equal(none.calls.length, 0, 'an empty list never reaches the shell');
  assert.equal(await applyFlavour(s, null), false); assert.equal(await applyFlavour(null, shiny), false);
});
