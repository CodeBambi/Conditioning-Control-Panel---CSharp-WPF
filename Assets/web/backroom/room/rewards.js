export const DECORATIONS=Object.freeze(['monstera','ivy','terrarium','gallery','portraits','billboard']);
export function createRoomRewards(){
  let owned=new Set(),bonus={until:0,multiplier:1},revision=0;
  return {reset(){owned=new Set();bonus={until:0,multiplier:1};revision++;},apply(body){
    if(!body||typeof body!=='object')return false;
    let changed=false;
    if(Array.isArray(body.decorations?.owned)){owned=new Set([...owned,...body.decorations.owned.filter(id=>DECORATIONS.includes(id))]);changed=true;}
    if(body.bonus&&Number.isFinite(body.bonus.until)&&[1,2].includes(body.bonus.multiplier)){
      bonus={until:Math.max(bonus.until,body.bonus.multiplier===2?body.bonus.until:0),multiplier:2};changed=true;
    }
    if(changed)revision++;return changed;
  },has:id=>owned.has(id),get revision(){return revision;},
  snapshot(now=Date.now()){return {owned:[...owned],until:bonus.until,multiplier:bonus.multiplier===2&&bonus.until>now?2:1};}};
}

export function createDoubleCharm({mount,lex,read,now=()=>Date.now()}){
  const doc=mount.ownerDocument,button=doc.createElement('button'),note=doc.createElement('div'),style=doc.createElement('style');
  button.type='button';button.className='br-double-charm';button.textContent='x2';button.hidden=true;
  note.className='br-double-note';note.id='br-double-note';note.hidden=true;note.setAttribute('role','status');
  button.setAttribute('aria-controls',note.id);button.setAttribute('aria-expanded','false');
  style.textContent='.br-double-charm{margin-left:8px;border:1px solid #e8c27a;border-radius:20px;background:#392142;color:#ffe0a0;font:700 12px Segoe UI;min-width:34px;min-height:30px;pointer-events:auto}.br-double-note{position:fixed;right:12px;top:108px;max-width:min(310px,calc(100vw - 24px));padding:12px;border:1px solid #e8c27a;border-radius:10px;background:#23122ff5;color:#efdfef;z-index:80;font:12px/1.5 Segoe UI;pointer-events:auto}.br-double-charm[hidden],.br-double-note[hidden]{display:none}body:has(.br-double-charm:not([hidden])) .br-nav,body:has(.br-double-charm:not([hidden])) .br-bell{right:270px}@media(max-width:600px){body:has(.br-double-charm:not([hidden])) .br-nav{top:58px;left:12px;right:12px;justify-content:flex-end;pointer-events:none}body:has(.br-double-charm:not([hidden])) .br-nav>*{pointer-events:auto}body:has(.br-double-charm:not([hidden])) .br-bell{top:98px;right:12px;max-width:calc(100vw - 24px)}}';
  const hide=()=>{note.hidden=true;button.setAttribute('aria-expanded','false');};
  button.onclick=()=>{note.hidden=!note.hidden;button.setAttribute('aria-expanded',String(!note.hidden));};
  button.onkeydown=e=>{if(e.key==='Escape'){e.stopPropagation();hide();}};
  function paint(){const state=read(now()),active=state.multiplier===2;button.hidden=!active;if(!active){hide();return;}
    const text=lex('br_double_remaining','Seeing Double: {minutes} min left').replace('{minutes}',String(Math.ceil((state.until-now())/60000)));
    const explanation=lex('br_double_tape_note','Prepaid spins keep the multiplier from purchase.');
    button.title=text+' '+explanation;button.setAttribute('aria-label',button.title);note.textContent=button.title;
  }
  mount.append(button);doc.body.append(style,note);const timer=setInterval(paint,1000);paint();
  return {paint,dispose(){clearInterval(timer);button.remove();note.remove();style.remove();}};
}
