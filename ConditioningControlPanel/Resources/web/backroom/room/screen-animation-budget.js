// Keep every wall picture visible, sharing a bounded animation clock fairly.
export function createScreenAnimationBudget() {
  let next=0,cursor=0;
  return (sources,now,still,performanceMode) => {
    if(now<next || !sources.length)return 0;
    const start=cursor;let started=0;
    for(let i=0;i<sources.length && started<4;i++) {
      const index=(start+i)%sources.length,src=sources[index];
      if(src.tick?.(now,still)){started++;cursor=(index+1)%sources.length;}
    }
    if(started)next=now+1000/(performanceMode?6:60);
    return started;
  };
}
