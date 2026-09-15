import { catalogueStyle } from './customization-panel-style.js';
import { createVendingView } from './vending-view.js';

/** Selection previews an item; the contextual controls apply it to the room. */
export function createCustomizationPanel({mount,vending,decorations=[],lex=(_,f)=>f,select,getState,restore,preview=()=>{},hasOwnership=()=>false,onClose=()=>{}}){
  const doc=mount.ownerDocument,L=(k,f)=>lex('br_custom_'+k,f)||f;
  // Two cabinet displays: nine room upgrades, then six collectible decorations.
  const BAYS=9;
  const items=[['screens4','4 extra screens'],['screens6','6 extra screens'],['mega','Ceiling projector'],['knight','Knight sculpture'],['queen','Queen sculpture'],['rook','Rook sculpture'],['vortex','Velvet Vortex'],['ribbon','Ribbon Galaxy'],['bloom','Prism Bloom'],['monstera','Monstera'],['ivy','Hanging ivy'],['terrarium','Terrarium'],['gallery','Gallery frame'],['portraits','Portrait pair'],['billboard','Wide billboard']];
  const style=doc.createElement('style');style.textContent=catalogueStyle;
  const panel=doc.createElement('section');panel.className='br-custom-panel';panel.hidden=true;panel.tabIndex=-1;panel.setAttribute('role','dialog');panel.setAttribute('aria-modal','true');panel.setAttribute('aria-label',L('title','Room Service'));
  const make=(tag,text,cls,parent=panel)=>{const e=doc.createElement(tag);e.textContent=text;if(cls)e.className=cls;parent.append(e);return e;};
  const button=(parent,text,fn)=>{const e=make('button',text,'',parent);e.type='button';e.onclick=fn;return e;};
  const header=make('header','','br-custom-header');make('h2',L('title','Room Service'),'',header);
  const closeButton=button(header,L('close','Close'),()=>close());
  const stage=make('div','','br-custom-stage');
  const overview=button(stage,L('all_items','All items'),()=>{chosen=-1;view.focus(-1);paint();preview('room',0,0);});overview.className='br-custom-overview';
  const chooser=make('div','','br-custom-items');chooser.setAttribute('role','group');chooser.setAttribute('aria-label',L('choose_item','Choose an item'));
  const hud=make('div','','br-custom-hud');
  const footer=make('footer','','br-custom-footer');button(footer,L('restore','Restore preview'),async()=>{await restore?.(structuredClone(snapshot));paint();if(chosen>=0)showPreview();});

  let chosen=-1,page=0,target=0,use='handles',snapshot,previousFocus,disposed=false;
  const state=()=>getState();
  const view=createVendingView({mount:stage,vending,decorations,labels:items.map(([key,label])=>L(key,label)),onSelect:choose});
  function showPreview(){
    if(chosen<0)return;
    if(chosen>=BAYS){preview('props',chosen-BAYS,chosen-BAYS);return;}
    preview(chosen<3?'screens':chosen<6?use:'floor',chosen<3?chosen:chosen<6?chosen-3:chosen-6,chosen<3?chosen:target);}
  function choose(i){chosen=i;target=0;use='handles';view.focus(i);paint();showPreview();}
  function apply(category,value,index=0){const pending=select(category,value,index);paint();showPreview();if(pending?.then)pending.then(()=>{if(!disposed)paint();});}
  function row(parent,entries,active,fn){entries.forEach(([key,label],i)=>{const b=button(parent,L(key,label),()=>fn(i));b.setAttribute('aria-pressed',String(active===i));});}
  function paint(){
    const expanded=!!hud.querySelector('details[open]');
    const more=label=>{const d=make('details','','br-custom-more',hud);d.open=expanded;make('summary',label,'',d);return d;};
    const focused=hud.contains(doc.activeElement)?[...hud.querySelectorAll('button')].indexOf(doc.activeElement):-1;
    [...chooser.children].forEach((b,i)=>b.setAttribute('aria-pressed',String(page===i)));hud.replaceChildren();overview.hidden=chosen<0;
    if(chosen<0){make('p',L('choose_item','Choose an item'),'br-custom-prompt',hud);return;}
    const title=make('h3',L(...items[chosen]),'',hud);title.setAttribute('aria-live','polite');
    if(chosen>=BAYS && !hasOwnership(items[chosen][0])){make('p',L('collect_locked','Collect this decoration from Daily Daze to use it.'),'br-custom-note',hud);}
    else if(chosen>=BAYS){const actions=make('div','','br-custom-actions',hud);row(actions,[['on','On'],['off','Off']],state().props[chosen-BAYS]?0:1,i=>apply('props',i===0,chosen-BAYS));}
    else if(chosen<3){const actions=make('div','','br-custom-actions',hud);row(actions,[['on','On'],['off','Off']],state().screens[chosen]?0:1,i=>apply('screens',i===0,chosen));}
    else if(chosen<6){const placement=more(L('placement','Placement'));const modes=make('div','','br-custom-actions',placement);row(modes,[['statues','Statues'],['lever','Slot lever']],use==='statues'?0:1,i=>{use=i?'handles':'statues';paint();showPreview();});
      const targets=make('div','','br-custom-actions',placement);row(targets,use==='statues'?[['spot1','Pedestal 1'],['spot2','Pedestal 2'],['spot3','Pedestal 3']]:[['rose','Candy Rose'],['violet','Candy Violet'],['mint','Candy Mint']],target,i=>{target=i;paint();showPreview();});
      const actions=make('div','','br-custom-actions',hud);row(actions,[['use','Use'],[use==='statues'?'remove':'original',use==='statues'?'Remove':'Original handle']],state()[use][target]===chosen-3?0:state()[use][target]===-1?1:-1,i=>apply(use,i?-1:chosen-3,target));
    }else{const actions=make('div','','br-custom-actions',hud);row(actions,[['on','On'],['off','Off']],state().floor===chosen-6?0:state().floor===-1?1:-1,i=>apply('floor',i?-1:chosen-6));
      const palette=make('div','','br-custom-actions br-custom-palettes',more(L('palette','Colours')));row(palette,[['jewel','Jewel'],['lagoon','Lagoon'],['sunset','Sunset']],state().palette,i=>apply('palette',i));
    }
    if(focused>=0)hud.querySelectorAll('button')[focused]?.focus({preventScroll:true});
  }
  [['upgrades','Room upgrades'],['decorations','Decorations']].forEach(([key,label],i)=>button(chooser,L(key,label),()=>{page=i;chosen=-1;view.setPage(i);paint();preview('room',0,0);}));
  function close(){if(panel.hidden)return;panel.hidden=true;onClose();previousFocus?.isConnected&&previousFocus.focus({preventScroll:true});previousFocus=null;}
  panel.addEventListener('keydown',e=>{e.stopPropagation();if(e.key==='Escape'){e.preventDefault();close();return;}if(e.key==='Tab'){const nodes=[...panel.querySelectorAll('button:not([disabled]),summary')].filter(n=>!n.closest('[hidden]')&&(!n.closest('details')||n.tagName==='SUMMARY'||n.closest('details').open)),first=nodes[0],last=nodes.at(-1);if(e.shiftKey&&(doc.activeElement===first||doc.activeElement===panel)){e.preventDefault();last.focus();}else if(!e.shiftKey&&doc.activeElement===last){e.preventDefault();first.focus();}}});
  for(const type of ['keyup','pointerdown','pointerup','click','wheel'])panel.addEventListener(type,e=>e.stopPropagation());
  mount.append(style,panel);paint();
  return {refresh:paint,open(){if(disposed||!panel.hidden)return;snapshot=structuredClone(state());previousFocus=doc.activeElement;panel.hidden=false;chosen=-1;page=0;view.setPage(0);view.focus(-1);paint();preview('room',0,0);closeButton.focus({preventScroll:true});},close,get opened(){return !disposed&&!panel.hidden;},get selectedItem(){return chosen;},update(dt,still){if(!panel.hidden)view.update(dt,still);},draw(renderer){return !disposed&&!panel.hidden&&view.draw(renderer);},
    /* What the panel leaves the room, in viewport pixels (y up from the canvas bottom): the strip beside
       it while it is docked down one edge, or the band above it once it is a full-width sheet (phone). */
    previewBox(w,h){const r=panel.getBoundingClientRect();
      if(r.left>4)return{x:0,y:0,w:Math.max(1,Math.floor(r.left)),h};
      const band=Math.max(1,Math.floor(r.top));return{x:0,y:h-band,w,h:band};},
    viewDebug(){return view.debug();},dispose(){if(disposed)return;close();disposed=true;view.dispose();panel.remove();style.remove();}};
}
