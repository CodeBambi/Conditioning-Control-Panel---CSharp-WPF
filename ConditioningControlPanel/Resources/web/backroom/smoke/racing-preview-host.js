// Local casino preview adapter. Uses only this browser's existing test ledger.
import { handle } from '/__phone-server.js';
const optionsStyle=document.createElement('style');
optionsStyle.textContent='#__opt-btn{right:76px!important}';document.head.append(optionsStyle);
const back = '/backroom/index.html?raceReturn=1';
const listeners = new Set();
function publishMedia() {
  const pool=window.__fxMedia, mode=window.__brMedia?.get().mode;
  const urls=mode==='local' ? pool?.local || [] : mode==='scrolller' ? [
    ...(pool?.clips || []).map(u=>'/api/clip?u='+encodeURIComponent(u)),
    ...(pool?.stills || []).map(u=>'/api/m?u='+encodeURIComponent(u))
  ] : [];
  const images=(urls.length ? urls : [0,1,2,3].map(n=>'/backroom/stations/slot/fallback/gif'+n+'.webp'))
    .map((url,i)=>({name:'casino-'+i+'.webp',url}));
  emit({type:'manifest',images,videos:[],skipped:0});
}
window.addEventListener('br-media-changed',publishMedia);
const emit = data => setTimeout(() => { for (const fn of listeners) fn({data}); }, 0);
const status = await handle('counter', 'state', {});
const grants = status?.ok && Array.isArray(status.body?.prizes?.grants) ? status.body.prizes.grants : [];
const tracks = grants.filter(id => /^rt\.original\.(0[0-9]|10)$/.test(id)).map(id => Number(id.slice(-2)));
if (!tracks.length) location.replace(back);
else {
  let opt = {};
  try { opt = JSON.parse(localStorage.getItem('br.opt.v1') || '{}'); } catch {}
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.add(fn); },
    postMessage(m) {
      if (m.type === 'ready') {
        emit({type:'init',protocol:1,settings:{masterVolume:Math.round((opt.volume ?? .8)*100),
          reducedMotion:opt.motion === 'off' || opt.motion === 'still' || opt.motion === 'reduced',
          racingTracks:tracks,returnToCasino:true,canSurface:true,hostSfx:false,trackPick:false,cloud:true}});
        publishMedia();
        Promise.resolve(window.__fxReady).then(publishMedia);
      }
      if (m.type === 'exit-done') location.replace(back);
      // Race points are not casino SP. Preview runs never grant account currency.
      if (m.type === 'run-ended') emit({type:'payout-result',finalXp:0,sparksEarned:0});
      if (m.type === 'fullscreen-set') {
        const p = m.on ? document.documentElement.requestFullscreen?.() : document.exitFullscreen?.();
        p?.catch(() => {});
      }
    }
  };
  await import('./raceBoot.js');
  setTimeout(() => document.getElementById('preview-return')?.remove(), 100);
}
