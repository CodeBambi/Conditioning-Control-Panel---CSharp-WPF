// Captions carry a colour, the same way a tint does, and now the word is
// actually drawn in it: ink, glow and the rim under it. White and black are
// caption only, since a wash over the whole picture never wants either.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createBlock, defaultParams, CAPTION_COLOURS, TINT_COLOURS } from '../engine/blocks.js';
import { colourFor, contrastTo, luma, GROUND, INK } from '../engine/effects/util.js';
import { render } from '../engine/effects/caption.js';
import { createProject } from '../engine/project.js';

const RECT = { x: 0, y: 0, w: 480, h: 270 };

/** A context that remembers every fill and stroke of text, with its colour. */
function stubCtx() {
  const marks = [];
  const c = {
    marks,
    canvas: { width: 480, height: 270 },
    globalAlpha: 1, globalCompositeOperation: 'source-over',
    fillStyle: '', strokeStyle: '', font: '', filter: 'none',
    shadowColor: '', shadowBlur: 0, lineJoin: '', lineWidth: 0,
    textAlign: '', textBaseline: '',
    save() {}, restore() {}, beginPath() {}, rect() {}, clip() {},
    setTransform() {}, clearRect() {}, translate() {}, scale() {}, rotate() {},
    measureText(t) { return { width: String(t).length * 20 }; },
    createRadialGradient() { return { addColorStop() {} }; },
    drawImage() {},
    fillRect() { marks.push({ op: 'fillRect', style: c.fillStyle, comp: c.globalCompositeOperation }); },
    fillText(t) { marks.push({ op: 'fillText', text: t, style: c.fillStyle, comp: c.globalCompositeOperation, blur: c.shadowBlur }); },
    strokeText(t) { marks.push({ op: 'strokeText', text: t, style: c.strokeStyle }); },
  };
  return c;
}

function env() {
  return {
    ramp: 1,
    rect: RECT,
    buf(_key, w, h) { return { canvas: { width: w, height: h }, ctx: stubCtx() }; },
  };
}

function draw(params, mode = 'text') {
  const ctx = stubCtx();
  const block = createBlock({ effect: 'caption', mode, start: 0, end: 20, params });
  render(ctx, null, block, 0.5, env());
  return ctx.marks;
}

/** The ink: the last plain fill of the word, after any glow passes. */
const inkOf = (marks) => marks.filter((m) => m.op === 'fillText' && m.comp === 'source-over').pop();

/* -------------------------------------------------------------- the table */

test('captions know the tint three plus white and black', () => {
  assert.deepEqual(Object.keys(CAPTION_COLOURS), ['pink', 'lavender', 'gold', 'white', 'black']);
  for (const k of Object.keys(TINT_COLOURS)) assert.equal(CAPTION_COLOURS[k], TINT_COLOURS[k]);
});

test('colourFor reads the caption table when it is handed one', () => {
  assert.equal(colourFor({ colour: 'white' }, null, CAPTION_COLOURS), '#FFFFFF');
  assert.equal(colourFor({ colour: 'black' }, null, CAPTION_COLOURS), '#101018');
  assert.equal(colourFor({ colour: 'gold' }, null, CAPTION_COLOURS), TINT_COLOURS.gold);
});

test('the tint table is untouched by the two new names', () => {
  assert.equal(colourFor({ colour: 'pink' }), TINT_COLOURS.pink);
  assert.equal(TINT_COLOURS.white, undefined);
  assert.equal(TINT_COLOURS.black, undefined);
});

test('custom still means the colour pulled from the media', () => {
  assert.equal(colourFor({ colour: 'custom' }, { mediaColour: '#22CC88' }, CAPTION_COLOURS), '#22CC88');
  assert.equal(colourFor({ colour: 'custom' }, {}, CAPTION_COLOURS), TINT_COLOURS.pink);
});

test('a rim is picked against the ink, not fixed', () => {
  assert.ok(luma('#FFFFFF') > luma('#101018'));
  assert.equal(contrastTo('#FFFFFF'), GROUND);
  assert.equal(contrastTo('#101018'), INK);
});

/* --------------------------------------------------------------- the draw */

test('the word is drawn in the colour it was given', () => {
  assert.equal(inkOf(draw({ text: 'drop', colour: 'white' })).style, '#FFFFFF');
  assert.equal(inkOf(draw({ text: 'drop', colour: 'black' })).style, '#101018');
  assert.equal(inkOf(draw({ text: 'drop', colour: 'gold' })).style, TINT_COLOURS.gold);
});

test('a caption with no colour in its params is pink', () => {
  const marks = draw({ text: 'drop', colour: undefined });
  assert.equal(inkOf(marks).style, TINT_COLOURS.pink);
});

test('the glow takes the ink colour, and a dark word glows cream instead', () => {
  const bright = draw({ text: 'drop', colour: 'gold', glow: 80 });
  const lit = bright.filter((m) => m.op === 'fillText' && m.comp === 'lighter');
  assert.ok(lit.length >= 3);
  for (const m of lit) assert.ok(m.style.includes('240,194,75'), m.style);
  const dark = draw({ text: 'drop', colour: 'black', glow: 80 });
  for (const m of dark.filter((x) => x.op === 'fillText' && x.comp === 'lighter')) {
    assert.ok(m.style.includes('242,235,221'), m.style);
  }
});

test('glow at zero draws no lighter pass at all', () => {
  const marks = draw({ text: 'drop', colour: 'pink', glow: 0 });
  assert.equal(marks.filter((m) => m.comp === 'lighter').length, 0);
});

test('the rim under the word flips with the ink', () => {
  const light = draw({ text: 'drop', colour: 'white' }).filter((m) => m.op === 'strokeText').pop();
  assert.ok(light.style.includes('20,20,43'), light.style);
  const dark = draw({ text: 'drop', colour: 'black' }).filter((m) => m.op === 'strokeText').pop();
  assert.ok(dark.style.includes('242,235,221'), dark.style);
});

test('a flash plate keeps a word that can be read on it', () => {
  const black = draw({ text: 'drop', colour: 'black' }, 'flash');
  assert.equal(black.find((m) => m.op === 'fillRect').style, '#101018');
  assert.equal(inkOf(black).style, INK);
  const white = draw({ text: 'drop', colour: 'white' }, 'flash');
  assert.equal(inkOf(white).style, GROUND);
});

test('an empty word draws nothing', () => {
  assert.equal(draw({ text: '   ', colour: 'white' }).length, 0);
});

/* ---------------------------------------------------------- the round trip */

test('a caption colour survives a save and a load', () => {
  const p = createProject();
  p.addBlock({ effect: 'caption', mode: 'text', start: 0, end: 20, params: { text: 'drop', colour: 'black' } });
  const json = JSON.parse(JSON.stringify(p.toJSON()));
  assert.equal(json.blocks[0].params.colour, 'black');
  const q = createProject.fromJSON(json, {});
  const back = q.blocks.find((x) => x.effect === 'caption');
  assert.equal(back.params.colour, 'black');
  assert.equal(back.params.text, 'drop');
  p.dispose(); q.dispose();
});

test('an undo and a redo keep the colour', () => {
  const p = createProject();
  const b = p.addBlock({ effect: 'caption', mode: 'text', start: 0, end: 20, params: { text: 'drop' } });
  p.commit('add caption');
  p.updateBlock(b.id, { params: { colour: 'white' } });
  p.commit('colour');
  p.undo();
  assert.equal(p.blocks.find((x) => x.id === b.id).params.colour, 'pink');
  p.redo();
  assert.equal(p.blocks.find((x) => x.id === b.id).params.colour, 'white');
  p.dispose();
});

test('a saved remix from before the swatch row loads and stays pink', () => {
  const p = createProject();
  p.addBlock({ effect: 'caption', mode: 'text', start: 0, end: 20, params: { text: 'drop' } });
  const json = JSON.parse(JSON.stringify(p.toJSON()));
  for (const blk of json.blocks) delete blk.params.colour;
  assert.equal(colourFor(json.blocks[0].params, null, CAPTION_COLOURS), TINT_COLOURS.pink);
  const q = createProject.fromJSON(json, {});
  const back = q.blocks.find((x) => x.effect === 'caption');
  assert.equal(back.params.colour, 'pink');
  p.dispose(); q.dispose();
});

test('a fresh caption still starts pink', () => {
  assert.equal(defaultParams('caption').colour, 'pink');
});
