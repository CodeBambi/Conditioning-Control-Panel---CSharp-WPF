import {defaultDecorationLayout,ownedLayout} from './decoration-catalog.js';
const clone=value=>JSON.parse(JSON.stringify(value));
/** Server-authoritative purchases. A lost buy reply keeps the original idempotency key. */
export function createDecorationShop({request,onBalance=()=>{},getBalance=()=>null,makeId=()=>globalThis.crypto.randomUUID()}) {
  let dead=false,busy=false,ready=false,open=false,revision=-1,catalogVersion=0,sp=null,status='loading',pending=null;
  let owned=new Set(),layout=defaultDecorationLayout(),catalog=[];
  const listeners=new Set();
  const balance=()=>{try{const value=getBalance();return Number.isFinite(value)?value:sp;}catch{return sp;}};
  const state=()=>({ready,open,busy,status,sp:balance(),revision,catalogVersion,catalog:clone(catalog),owned:[...owned],layout:clone(layout),retry:!!pending});
  const emit=()=>{if(!dead)for(const fn of listeners){try{fn(state());}catch{/* Presentation cannot prevent settlement. */}}};
  function accept(body,operation){
    const data=body?.decorations;
    if(!data||!Number.isInteger(data.revision??body.revision))return false;
    const next=data.revision??body.revision;
    if(next<revision)return false;
    revision=next;owned=new Set([...owned,...(Array.isArray(data.owned)?data.owned:[])]);
    layout=ownedLayout(data.layout,owned);open=body.open===true;ready=true;
    if(Number.isInteger(body.catalogVersion))catalogVersion=body.catalogVersion;
    if(Array.isArray(body.catalog))catalog=body.catalog.filter(x=>typeof x?.id==='string'&&Number.isFinite(x.priceSp)&&x.priceSp>=0).map(x=>({...x}));
    if(Number.isFinite(body.sp)){sp=body.sp;if(operation==='buy')try{onBalance(sp);}catch{/* The receipt remains adopted. */}}
    return true;
  }
  async function run(operation,args,retrying=false){
    if(dead||busy)return false;
    if(operation!=='state'&&(!ready||!open||pending&&!retrying))return false;
    busy=true;status=operation==='state'?'loading':'saving';emit();
    const intent={operation,args:clone(args)};
    try{
      const body=await request(operation,args);
      if(dead)return false;
      const adopted=accept(body,operation);
      if(body?.ok!==true){
        const reason=body?.reason||body?.error||'unavailable';
        pending=operation!=='state'&&['timeout','unavailable','network','offline','internal'].includes(reason)?intent:null;status=pending?'retry':reason;
        if(body?.open===false)open=false;
        return false;
      }
      if(!adopted){pending=operation==='state'?null:intent;status=pending?'retry':'stale';return false;}
      pending=null;status='ready';return true;
    }catch{
      if(!dead){pending=operation==='state'?null:intent;status=operation==='state'?'unavailable':'retry';}
      return false;
    }finally{busy=false;emit();}
  }
  return {
    get state(){return state();},
    subscribe(fn){listeners.add(fn);try{fn(state());}catch{}return()=>listeners.delete(fn);},
    load(){return pending?Promise.resolve(false):run('state',{});},
    buy(id){
      if(busy||pending||!ready||!open||owned.has(id)||!catalog.some(x=>x.id===id))return Promise.resolve(false);
      return run('buy',{idem:makeId(),decorationId:id,catalogVersion});
    },
    save(next){return run('layout',{revision,layout:ownedLayout(next,owned)});},
    retry(){return pending?run(pending.operation,pending.args,true):this.load();},
    grant(ids){for(const id of ids||[])if(typeof id==='string')owned.add(id);emit();},
    dispose(){dead=true;listeners.clear();},
  };
}
