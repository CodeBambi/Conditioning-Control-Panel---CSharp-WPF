// Keep every wall picture visible, sharing a bounded animation clock fairly.
// Full: up to eight starts per rendered frame, i.e. every dealt picture (screens.js MAX_PICTURES) can
// advance on the frame it is due, and each source's own clock (gif-decode.js MAX_FPS) is the real cap.
// Four per frame at a 30 Hz rest render was a second throttle: eight visible spirals got 15 fps each.
// Performance: four starts per batch, six batches a second, unchanged.
export function createScreenAnimationBudget() {
  let next=0,cursor=0;
  return (sources,now,still,performanceMode) => {
    if(now<next || !sources.length)return 0;
    const start=cursor,limit=performanceMode?4:8;let started=0;
    for(let i=0;i<sources.length && started<limit;i++) {
      const index=(start+i)%sources.length,src=sources[index];
      if(src.tick?.(now,still)){started++;cursor=(index+1)%sources.length;}
    }
    if(started)next=now+1000/(performanceMode?6:60);
    return started;
  };
}
