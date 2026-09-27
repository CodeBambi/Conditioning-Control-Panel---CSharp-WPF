import test from 'node:test';
import assert from 'node:assert/strict';
import {createMusic} from '../../shared/sound/music.js';
test('room soundtrack freezes across gestures and resumes same track slower',async()=>{
 const handlers={};let el;
 class Audio {constructor(){el=this;this.dataset={};this.currentTime=17;this.paused=true;}addEventListener(){}removeEventListener(){}getAttribute(){return this.src;}play(){this.paused=false;return Promise.resolve();}pause(){this.paused=true;}removeAttribute(){this.src='';}load(){}}
 const host={addEventListener:(name,fn)=>handlers[name]=fn,removeEventListener(){}};
 const page={hidden:false,addEventListener(){},removeEventListener(){}};
 const music=createMusic({AudioCtor:Audio,host,page});
 handlers.pointerdown();await Promise.resolve();const src=el.src;
 music.setScene('freeze');assert.equal(el.paused,true);
 handlers.pointerdown();await Promise.resolve();assert.equal(el.paused,true);
 music.setScene('grey');assert.equal(el.paused,false);assert.equal(el.src,src);assert.equal(el.currentTime,17);
 assert.equal(el.playbackRate,.72);assert.equal(el.preservesPitch,false);
 music.setScene('normal');assert.equal(el.playbackRate,1);assert.equal(el.preservesPitch,true);
 music.dispose();
});
