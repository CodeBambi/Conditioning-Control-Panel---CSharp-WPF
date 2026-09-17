import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = (await readFile(new URL('../media.js', import.meta.url), 'utf8'))
  .replace("import { kindOf } from './symbols.js';", "const kindOf = id => ({kind:id.startsWith('gif')?'gif':'sub',n:Number(id.at(-1))});")
  .replace("import { refusedMedia } from '../../room/media-limits.js';", "const refusedMedia = error => ['AbortError','MediaLimitError'].includes(error?.name);")
  .replace("import { decodedSource } from '../../room/gif-decode.js';", 'const decodedSource = (...args) => globalThis.testDecode(...args);');
const { createMedia } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const flush = () => new Promise(resolve => setImmediate(resolve));
function setup(t, decode) {
  const previous = Object.fromEntries(['Image','location','testDecode'].map(k => [k, Object.getOwnPropertyDescriptor(globalThis,k)]));
  const images = [], appended = [];
  globalThis.location = {href:'https://ccp.game/room/'};
  globalThis.testDecode = decode;
  globalThis.Image = class {
    constructor() { this.dataset={}; this.complete=false; this.naturalWidth=0; images.push(this); }
    removeAttribute(name) { if(name==='src') this.src=''; }
    remove() { this.removed=true; }
  };
  t.after(() => { for(const [k,d] of Object.entries(previous)) { if(d) Object.defineProperty(globalThis,k,d); else delete globalThis[k]; } });
  const media=createMedia({append(img){appended.push(img);}});
  t.after(()=>media.dispose());
  return {media, images, appended};
}
const deal = n => ({gifs:Array.from({length:n},(_,i)=>({key:'g'+i,url:'https://ccp.assets/'+i+'.gif'}))});

test('four decoded reel sources create zero duplicate browser image pipelines', async t => {
  let decodes=0,disposed=0;
  const {media,images}=setup(t,async()=>{decodes++;return {canvas:{},tick(){},dispose(){disposed++;}};});
  await media.deal(deal(4));
  assert.equal(decodes,4); assert.equal(images.length,0);
  assert.equal(media.animated,true); assert.ok(media.gif(3));
  media.dispose(); assert.equal(disposed,4);
});

test('only failed decoders create images and loaded images release their source on disposal', async t => {
  const {media,images}=setup(t,async url=>url.endsWith('1.gif')?null:{canvas:{},tick(){},dispose(){}});
  const loaded=media.deal(deal(4)); await flush();
  assert.equal(images.length,1);
  images[0].naturalWidth=120;images[0].complete=true;images[0].onload();await loaded;
  assert.equal(media.animated,true);media.dispose();
  assert.equal(images[0].src,'');assert.equal(images[0].removed,true);
});

test('a cleared pending fallback releases its image and cannot overwrite the next deal', async t => {
  const {media,images}=setup(t,async()=>null);
  const old=media.deal(deal(1));await flush();const lateLoad=images[0].onload;
  await media.deal({gifs:[]});await old;
  images[0].naturalWidth=100;lateLoad();
  assert.equal(media.gif(0),null);assert.equal(images[0].src,'');assert.equal(images[0].onload,null);
});

test('late decoder completion after disposal is closed without creating a fallback', async t => {
  let resolve,disposed=0;
  const {media,images}=setup(t,()=>new Promise(r=>{resolve=r;}));
  const pending=media.deal(deal(1));media.dispose();
  resolve({canvas:{},tick(){},dispose(){disposed++;}});await pending;
  assert.equal(disposed,1);assert.equal(images.length,0);assert.equal(media.gif(0),null);
});

test('duplicate identities stay absent and never start a second decoder', async t => {
  let decodes=0;
  const {media,images}=setup(t,async()=>{decodes++;return {canvas:{},tick(){},dispose(){}};});
  await media.deal({gifs:[{key:'g0',url:'https://ccp.assets/a.gif'},{key:'g1',url:'https://ccp.assets/a.gif'}]});
  assert.equal(decodes,1);assert.equal(images.length,0);assert.equal(media.keyFor('gif1'),null);assert.equal(media.gif(1),null);
});

test('a failed image load is detached and never becomes drawable', async t => {
  const {media,images}=setup(t,async()=>null);
  const pending=media.deal(deal(1));await flush();images[0].onerror();await pending;
  assert.equal(images[0].src,'');assert.equal(images[0].removed,true);assert.equal(media.gif(0),null);
});

test('an image that never loads is released at the fallback deadline', async t => {
  t.mock.timers.enable({apis:['setTimeout']});
  const {media,images}=setup(t,async()=>null);
  const pending=media.deal(deal(1));await flush();
  t.mock.timers.tick(2500);await pending;
  assert.equal(images[0].src,'');assert.equal(images[0].removed,true);assert.equal(media.gif(0),null);
});
