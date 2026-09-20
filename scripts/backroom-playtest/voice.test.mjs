import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPresetVoice } from './__phone-voice.js';
function rig(fetcher = async () => ({ok:true,arrayBuffer:async()=>new ArrayBuffer(2)})) {
 const played=[]; const samples=new Float32Array([1,2,3]);
 class Context { state='running'; destination={}; resume(){return Promise.resolve();} decodeAudioData(){return Promise.resolve({numberOfChannels:1,length:3,sampleRate:3,duration:1,getChannelData:()=>samples});} createBuffer(){const data=new Float32Array(3);return {duration:1,getChannelData:()=>data};} createGain(){return {gain:{value:0},connect(){},disconnect(){}};} createBufferSource(){const s={connect(){},disconnect(){},stop(){s.stopped=true;},start(){played.push(s);}};return s;} }
 return {voice:createPresetVoice({Context,fetcher}),played};
}
test('preset reports duration, uses recording and replaces prior speech',async()=>{const {voice,played}=rig();assert.deepEqual(await voice.speak({text:'LET GO'}),{source:'preset',durationMs:1000});await voice.speak({text:'drop'});assert.equal(played.length,2);assert.equal(played[0].stopped,true);voice.stop();assert.equal(played[1].stopped,true);});
test('reversal copies samples without changing forward clip',async()=>{const {voice,played}=rig();await voice.speak({text:'sink',reversed:true});assert.deepEqual([...played[0].buffer.getChannelData(0)],[3,2,1]);await voice.speak({text:'sink'});assert.deepEqual([...played[1].buffer.getChannelData(0)],[1,2,3]);});
test('cancel during fetch prevents late playback',async()=>{let ready;const {voice,played}=rig(()=>new Promise(r=>ready=r));const pending=voice.speak({text:'drop'});voice.stop();ready({ok:true,arrayBuffer:async()=>new ArrayBuffer(1)});assert.equal((await pending).durationMs,0);assert.equal(played.length,0);});
test('missing clip stays quiet instead of requesting browser TTS',async()=>{const {voice,played}=rig(async()=>({ok:false}));assert.deepEqual(await voice.speak({text:'drop'}),{source:'preset',durationMs:0});assert.equal(played.length,0);});
