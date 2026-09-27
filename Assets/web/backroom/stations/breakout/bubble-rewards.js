// One shuffled cycle per distinct dealt source, with no repeat at cycle boundaries.
export function createRewardPicker(rng=Math.random) {
  let signature='', bag=[], last=null;
  return keys=>{
    const next=JSON.stringify(keys);
    if(next!==signature){signature=next;bag=[];}
    if(!keys.length)return -1;
    if(!bag.length){
      bag=[...new Set(keys)];
      for(let i=bag.length-1;i>0;i--){const j=Math.floor(rng()*(i+1));[bag[i],bag[j]]=[bag[j],bag[i]];}
      if(bag.length>1&&bag[bag.length-1]===last)[bag[0],bag[bag.length-1]]=[bag[bag.length-1],bag[0]];
    }
    last=bag.pop();return keys.indexOf(last);
  };
}
// Contain the original frame; small source images never expand beyond twice native size.
export function rewardBounds(w,h,fw,fh,duo=false,hero=false) {
  const k=Math.min(w*(duo?.30:hero?.60:.46)/fw,h*(hero?.65:.52)/fh,2);
  return {w:fw*k,h:fh*k};
}
