// Accepted spends only. This is presentation; balances always remain server-owned.
const flights=new Set();
export function clearSpendFlights(){for(const stop of [...flights])stop();}
export function spendCount(amount) { return Math.min(12, Math.max(0, Math.ceil(Number(amount) || 0))); }
export function spendFlight({ from, to, amount, still = false }) {
  const count = spendCount(amount);
  if (!count || still || !from?.isConnected || !to?.isConnected) return;
  const a = from.getBoundingClientRect(), b = to.getBoundingClientRect();
  const x = a.left+a.width/2, y = a.top+a.height/2;
  const dx = b.left+b.width/2-x, dy = b.top+b.height/2-y;
  const layer = document.createElement('div'); layer.className = 'br-sp-spend-flight';
  layer.setAttribute('aria-hidden','true');
  layer.style.cssText='position:fixed;inset:0;pointer-events:none;z-index:65;overflow:hidden';
  document.body.append(layer);
  for(let i=0;i<count;i++) {
    const shard=document.createElement('i'), side=(i%3-1)*14;
    shard.style.cssText=`position:absolute;left:${x}px;top:${y}px;width:14px;height:22px;background:linear-gradient(135deg,#fff6b8,#ffb65e 48%,#ff63c9 50%,#9c5aff);clip-path:polygon(50% 0,100% 42%,50% 100%,0 42%);filter:drop-shadow(0 0 5px #ffadff)`;
    layer.append(shard);
    shard.animate([{transform:'translate(-7px,-11px) scale(.6)',opacity:0},{transform:`translate(${dx*.25+side}px,${dy*.15-35}px) rotate(70deg) scale(1.2)`,opacity:1,offset:.25},{transform:`translate(${dx-7}px,${dy-11}px) rotate(190deg) scale(.35)`,opacity:0}],{duration:620,delay:i*42,easing:'cubic-bezier(.25,.6,.35,1)',fill:'both'});
  }
  const stop=()=>{clearTimeout(timer);layer.remove();flights.delete(stop);};
  const timer=setTimeout(stop,650+count*42);flights.add(stop);
  return stop;
}
