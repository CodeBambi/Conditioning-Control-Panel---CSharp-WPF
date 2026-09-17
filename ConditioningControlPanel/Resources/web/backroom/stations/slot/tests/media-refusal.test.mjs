/* The refusal fork in stations/slot/media.js (the Sep 17 all-faces-fallback reel).
 * room/gif-decode.js can only ever reject with MediaLimitError or AbortError, so a consumer that
 * treats "refused" as one thing gives up on a transfer that never arrived exactly as readily as on
 * a picture it measured and declined - and then, because the <img> fallback sits after that return,
 * it never makes the second attempt the file header promises. These tests hold the fork open: the
 * real media-limits.js is imported (not stubbed), so `overBudget` means here what it means in the room.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { MediaLimitError, refusedMedia, overBudget } from '../../../room/media-limits.js';

// Only symbols.js (a DOM module) and the decoder are stood in for. media-limits.js stays real.
const source = (await readFile(new URL('../media.js', import.meta.url), 'utf8'))
  .replace("import { kindOf } from './symbols.js';", "const kindOf = id => ({kind:id.startsWith('gif')?'gif':'sub',n:Number(id.at(-1))});")
  .replace("import { decodedSource } from '../../room/gif-decode.js';", 'const decodedSource = (...args) => globalThis.testDecode(...args);')
  // A data: url cannot resolve a relative import, so media-limits.js is pointed at its file url and stays REAL.
  .replace("'../../room/media-limits.js'", JSON.stringify(new URL('../../../room/media-limits.js', import.meta.url).href));
const { createMedia } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const flush = () => new Promise(resolve => setImmediate(resolve));

function setup(t, decode) {
  const previous = Object.fromEntries(['Image','location','testDecode'].map(k => [k, Object.getOwnPropertyDescriptor(globalThis,k)]));
  const images = [], logged = [];
  globalThis.location = {href:'https://ccp.game/room/'};
  globalThis.testDecode = decode;
  globalThis.Image = class {
    constructor() { this.dataset={}; this.complete=false; this.naturalWidth=0; images.push(this); }
    removeAttribute(name) { if(name==='src') this.src=''; }
    remove() { this.removed=true; }
  };
  t.after(() => { for(const [k,d] of Object.entries(previous)) { if(d) Object.defineProperty(globalThis,k,d); else delete globalThis[k]; } });
  const media = createMedia({append(){}}, (k,f)=>f, (level,msg)=>logged.push(level+': '+msg));
  t.after(()=>media.dispose());
  return {media, images, logged};
}
const one = ({gifs:[{key:'g0',url:'https://ccp.assets/.temp/a.webp'}]});

test('every rejection gif-decode.js can produce is a refusal, so "refused" cannot mean "too big"', () => {
  // The guarantee this whole fork rests on, asserted against the real module rather than assumed.
  for (const e of [new MediaLimitError('Media transfer failed','transfer'), new MediaLimitError('Image dimensions exceed media budget'),
                   Object.assign(new Error('x'), {name:'AbortError'})])
    assert.equal(refusedMedia(e), true, e.message);
  assert.equal(overBudget(new MediaLimitError('Media transfer failed','transfer')), false);
  assert.equal(overBudget(new MediaLimitError('Image transfer exceeds media budget')), true, 'an untagged refusal stays conservative');
});

test('a transfer that never arrived still gets the browser image pipeline, and says so', async t => {
  // The swallowed branch: a warm remote file swept out from under its url 404s, gif-decode turns that
  // into MediaLimitError('Media transfer failed'), and the reel used to keep its built-in art in silence.
  const {media, images, logged} = setup(t, async () => { throw new MediaLimitError('Media transfer failed','transfer'); });
  const pending = media.deal(one); await flush();
  assert.equal(images.length, 1, 'the <img> fallback ran');
  images[0].naturalWidth = 120; images[0].complete = true; images[0].dataset.readable = '1'; images[0].onload();
  await pending;
  assert.equal(media.gif(0), images[0], 'the reel has a picture');
  assert.match(logged.join('|'), /^warn: slot media g0 did not decode \(Media transfer failed\)/);
});

test('a picture we measured and declined is never handed to the browser decoder instead', async t => {
  const {media, images, logged} = setup(t, async () => { throw new MediaLimitError('Image dimensions exceed media budget'); });
  await media.deal(one);
  assert.equal(images.length, 0, 'no second decode of something the budget already refused');
  assert.equal(media.gif(0), null);
  assert.deepEqual(logged, ['warn: slot media g0 refused: Image dimensions exceed media budget']);
});

test('a cancelled deal is silent and starts nothing: it is the next deal, not a failure', async t => {
  const {media, images, logged} = setup(t, async () => { throw Object.assign(new Error('gone'), {name:'AbortError'}); });
  await media.deal(one);
  assert.equal(images.length, 0);
  assert.deepEqual(logged, []);
});

test('CORS-tainted art reports through the log seam, because console.warn never reaches the app log', async t => {
  const {media, images, logged} = setup(t, async () => { throw new MediaLimitError('Media transfer failed','transfer'); });
  globalThis.document = { createElement: () => ({ getContext: () => ({ drawImage(){}, getImageData(){ throw new Error('tainted'); } }) }) };
  t.after(() => { delete globalThis.document; });
  const pending = media.deal(one); await flush();
  images[0].naturalWidth = 120; images[0].complete = true; images[0].onload(); await pending;
  assert.equal(media.gif(0), null, 'a tainted image cannot be drawn');
  assert.ok(logged.includes('warn: slot media is not CORS-readable, using built-in art'));
  assert.equal(media.gif(0), null);
  assert.equal(logged.filter(l => l.includes('CORS')).length, 1, 'once per dealt picture, not once per frame');
});

test('gif-decode.js tags every throw that is a transfer failure, so the retry cannot be lost again', async () => {
  // The default reason is 'budget' (the conservative one), so a new untagged throw here would silently
  // put the reel back to giving up on a file that simply did not arrive. Read the source and hold the tags.
  const decode = await readFile(new URL('../../../room/gif-decode.js', import.meta.url), 'utf8');
  for (const m of decode.match(/new MediaLimitError\([^)]*\)/g) || []) {
    const transfer = /transfer failed|Not an image|Unsupported image header/.test(m);
    assert.equal(/'transfer'/.test(m), transfer, m);
  }
});
