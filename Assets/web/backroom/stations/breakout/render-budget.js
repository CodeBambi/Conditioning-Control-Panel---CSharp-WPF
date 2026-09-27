// Software canvas quality follows sustained frame cost, never simulation speed.
export function createRenderBudget(enabled) {
  const ceiling=enabled?900000:1500000, floor=720000;
  let pixels=ceiling, start=0, count=0, cost=0, misses=0, readyAt=2000, fastSince=0;
  function clear(now) {start=now;count=cost=misses=0;}
  return {
    get pixels(){return pixels;},
    idle(now){clear(now);readyAt=now+1000;fastSince=0;},
    sample(now,cpuMs,frameMs) {
      if(!enabled||!Number.isFinite(cpuMs)||frameMs>100||frameMs<=0){clear(now);return false;}
      if(now<readyAt){clear(now);return false;}
      count++;cost+=cpuMs;if(frameMs>20)misses++;
      if(now-start<1000||count<24)return false;
      const mean=cost/count, missed=misses/count;clear(now);
      const old=pixels;
      if(mean>13||missed>.2) {
        const factor=Math.max(.65,Math.min(.85,12/Math.max(12,mean)));
        pixels=Math.max(floor,Math.round(pixels*factor));fastSince=0;readyAt=now+1500;
      } else if(mean<9&&missed<.05) {
        if(!fastSince)fastSince=now;
        if(now-fastSince>=8000){pixels=Math.min(ceiling,Math.round(pixels*1.1));fastSince=0;readyAt=now+2000;}
      } else fastSince=0;
      return pixels!==old;
    },
  };
}
