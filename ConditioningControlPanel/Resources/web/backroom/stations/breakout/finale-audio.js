// Browser stations pass only a beat clock into the sim. Route these cues once here.
export function routeFinaleAudio(name, data, audio, width) {
  if(name==='finaleInterrupt')audio.finaleInterrupt();
  else if(name==='finaleLocked')audio.finaleGrey(true);
  else if(name==='finaleRelease')audio.finaleGrey(false);
  else if(name==='metalHit')audio.metal({x:data.x/width});
}
