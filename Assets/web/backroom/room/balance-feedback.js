/* Animate only the displayed balance, never unrevealed rewards. */
export function createBalanceFeedback({target,still,now=()=>performance.now()}) {
  let previous=null,lastChange=-Infinity,lastPulse=-Infinity,total=0,timer=0;
  return {reset(value){previous=Number.isFinite(value)?value:null;total=0;},update(value){
    if(!Number.isFinite(value))return;
    const delta=previous==null?0:value-previous;previous=value;
    if(!delta)return;
    const box=target();if(!box)return;
    const t=now(),direction=delta>0?'in':'out';
    if(t-lastChange>700||Math.sign(total)!==Math.sign(delta))total=0;
    total+=delta;lastChange=t;
    box.dataset.flow=direction;box.dataset.delta=(total>0?'+':'')+Math.round(total)+' SP';
    box.dataset.balanceStill=String(!!still());
    if(t-lastPulse>220){
      box.classList.remove('br-sp-changing');void box.offsetWidth;box.classList.add('br-sp-changing');lastPulse=t;
    }
    clearTimeout(timer);timer=setTimeout(()=>{box.classList.remove('br-sp-changing');delete box.dataset.flow;delete box.dataset.delta;},1100);
  }};
}
