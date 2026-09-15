import { catalogueStyle } from './customization-panel-style.js';
import { createVendingView } from './vending-view.js';

/** Picking an item fits it to the room at once; the one button under the miniature takes it off again. */
export function createCustomizationPanel({mount,vending,decorations=[],lex=(_,f)=>f,select,getState,restore,preview=()=>{},slotOrder=[0,1,2],hasOwnership=()=>false,onClose=()=>{}}){
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
  // Lever arrows, overlaid on the room pane (outside the sheet): they pan the close-up to the previous or
  // next cabinet, and the centred cabinet IS the Placement target, so the two never disagree.
  const SLOT_LABELS=[['rose','Candy Rose'],['violet','Candy Violet'],['mint','Candy Mint']];
  const arrows=doc.createElement('div');arrows.className='br-custom-arrows';arrows.hidden=true;arrows.tabIndex=0;arrows.setAttribute('role','group');arrows.setAttribute('aria-label',L('placement','Placement'));
  const arrow=(cls,glyph,label,dir)=>{const b=doc.createElement('button');b.type='button';b.className='br-custom-arrow '+cls;b.textContent=glyph;b.setAttribute('aria-label',label);b.title=label;b.onclick=()=>step(dir);arrows.append(b);return b;};
  arrow('is-prev','\u2039',L('prev_slot','Previous slot'),-1);
  const centred=make('output','','br-custom-centred',arrows);centred.setAttribute('aria-live','polite');
  arrow('is-next','\u203a',L('next_slot','Next slot'),1);
  function step(dir){if(arrows.hidden||!slotOrder.length)return;const i=Math.max(0,slotOrder.indexOf(target));target=slotOrder[(i+dir+slotOrder.length)%slotOrder.length];paint();showPreview();}
  function paintArrows(){const on=!panel.hidden&&chosen>=3&&chosen<6&&use==='handles';arrows.hidden=!on;if(on)centred.textContent=L(...SLOT_LABELS[target]);}
  arrows.addEventListener('keydown',e=>{e.stopPropagation();if(e.key==='ArrowLeft'||e.key==='ArrowRight'){e.preventDefault();step(e.key==='ArrowLeft'?-1:1);}else if(e.key==='Escape'){e.preventDefault();close();}else trap(e);});
  const state=()=>getState();
  const view=createVendingView({mount:stage,vending,decorations,labels:items.map(([key,label])=>L(key,label)),onSelect:choose,onDismount:dismount});
  function showPreview(){
    if(chosen<0)return;
    if(chosen>=BAYS){preview('props',chosen-BAYS,chosen-BAYS);return;}
    preview(chosen<3?'screens':chosen<6?use:'floor',chosen<3?chosen:chosen<6?chosen-3:chosen-6,chosen<3?chosen:target);}
  // Switching between lever items keeps the cabinet in view: the target is the user's, not the item's.
  // A pick IS the Use: the item goes on the focused cabinet or pedestal there and then, no second press.
  function choose(i){const keep=chosen>=3&&chosen<6&&i>=3&&i<6;chosen=i;if(!keep){target=0;use='handles';}view.focus(i);paint();showPreview();equip();}
  /** The chosen item as the room takes it: what to set, where, and whether it is already fitted there. */
  function fitting(){
    if(chosen<0)return null;
    if(chosen<3)return {category:'screens',on:true,off:false,index:chosen,fitted:!!state().screens[chosen]};
    if(chosen<6)return {category:use,on:chosen-3,off:-1,index:target,fitted:state()[use][target]===chosen-3};
    if(chosen<BAYS)return {category:'floor',on:chosen-6,off:-1,index:0,fitted:state().floor===chosen-6};
    return {category:'props',on:true,off:false,index:chosen-BAYS,fitted:!!state().props[chosen-BAYS]};}
  const isLocked=()=>chosen>=BAYS&&!hasOwnership(items[chosen][0]);
  function equip(){const f=fitting();if(f&&!isLocked()&&!f.fitted)apply(f.category,f.on,f.index);}
  function unequip(){const f=fitting();if(f)apply(f.category,f.off,f.index);}
  /** A right-click on an item: that piece comes off wherever it sits, whatever is selected. */
  function dismount(i){
    if(!Number.isInteger(i)||i<0||i>=items.length)return false;
    const s=state(),pending=[];
    if(i<3){if(s.screens[i])pending.push(select('screens',false,i));}
    else if(i<6){const piece=i-3;
      s.statues.forEach((on,spot)=>{if(on===piece)pending.push(select('statues',-1,spot));});
      s.handles.forEach((on,cabinet)=>{if(on===piece)pending.push(select('handles',-1,cabinet));});}
    else if(i<BAYS){if(s.floor===i-6)pending.push(select('floor',-1));}
    else if(s.props[i-BAYS])pending.push(select('props',false,i-BAYS));
    paint();if(chosen>=0)showPreview();
    if(pending.length)Promise.all(pending).then(()=>{if(!disposed)paint();});
    return !!pending.length;}
  function apply(category,value,index=0){const pending=select(category,value,index);paint();showPreview();if(pending?.then)pending.then(()=>{if(!disposed)paint();});}
  function row(parent,entries,active,fn){entries.forEach(([key,label],i)=>{const b=button(parent,L(key,label),()=>fn(i));b.setAttribute('aria-pressed',String(active===i));});}
  function paint(){
    const expanded=!!hud.querySelector('details[open]');
    const more=label=>{const d=make('details','','br-custom-more',hud);d.open=expanded;make('summary',label,'',d);return d;};
    const focused=hud.contains(doc.activeElement)?[...hud.querySelectorAll('button')].indexOf(doc.activeElement):-1;
    [...chooser.children].forEach((b,i)=>b.setAttribute('aria-pressed',String(page===i)));hud.replaceChildren();overview.hidden=chosen<0;paintArrows();
    if(chosen<0){make('p',L('choose_item','Choose an item'),'br-custom-prompt',hud);return;}
    const title=make('h3',L(...items[chosen]),'',hud);title.setAttribute('aria-live','polite');
    const locked=isLocked();
    if(locked)make('p',L('collect_locked','Collect this decoration from Daily Daze to use it.'),'br-custom-note',hud);
    if(chosen>=3&&chosen<6){const placement=more(L('placement','Placement'));const modes=make('div','','br-custom-actions',placement);row(modes,[['statues','Statues'],['lever','Slot lever']],use==='statues'?0:1,i=>{use=i?'handles':'statues';paint();showPreview();});
      const targets=make('div','','br-custom-actions',placement);row(targets,use==='statues'?[['spot1','Pedestal 1'],['spot2','Pedestal 2'],['spot3','Pedestal 3']]:[['rose','Candy Rose'],['violet','Candy Violet'],['mint','Candy Mint']],target,i=>{target=i;paint();showPreview();});}
    else if(chosen>=6&&chosen<BAYS){const palette=make('div','','br-custom-actions br-custom-palettes',more(L('palette','Colours')));row(palette,[['jewel','Jewel'],['lagoon','Lagoon'],['sunset','Sunset']],state().palette,i=>apply('palette',i));}
    // The one button under the miniature, where the eye already is: the item itself is the Use.
    if(!locked&&fitting()?.fitted)button(hud,L('remove','Remove'),unequip).className='br-custom-remove';
    if(focused>=0)hud.querySelectorAll('button')[focused]?.focus({preventScroll:true});
  }
  [['upgrades','Room upgrades'],['decorations','Decorations']].forEach(([key,label],i)=>button(chooser,L(key,label),()=>{page=i;chosen=-1;view.setPage(i);paint();preview('room',0,0);}));
  function close(){if(panel.hidden)return;panel.hidden=true;arrows.hidden=true;onClose();previousFocus?.isConnected&&previousFocus.focus({preventScroll:true});previousFocus=null;}
  // One Tab ring for the sheet and the pane arrows, so a keyboard reaches the arrows from the dialog.
  function trap(e){if(e.key!=='Tab')return;const nodes=[...panel.querySelectorAll('button:not([disabled]),summary')].filter(n=>!n.closest('[hidden]')&&(!n.closest('details')||n.tagName==='SUMMARY'||n.closest('details').open)).concat(arrows.hidden?[]:[...arrows.querySelectorAll('button')]),first=nodes[0],last=nodes.at(-1);if(e.shiftKey&&(doc.activeElement===first||doc.activeElement===panel)){e.preventDefault();last.focus();}else if(!e.shiftKey&&doc.activeElement===last){e.preventDefault();first.focus();}}
  panel.addEventListener('keydown',e=>{e.stopPropagation();if(e.key==='Escape'){e.preventDefault();close();return;}trap(e);});
  for(const type of ['keyup','pointerdown','pointerup','click','wheel']){panel.addEventListener(type,e=>e.stopPropagation());arrows.addEventListener(type,e=>e.stopPropagation());}
  mount.append(style,arrows,panel);paint();
  return {refresh:paint,stepSlot:step,dismount,get slotArrows(){return !arrows.hidden;},get slotTarget(){return target;},open(){if(disposed||!panel.hidden)return;snapshot=structuredClone(state());previousFocus=doc.activeElement;panel.hidden=false;chosen=-1;page=0;view.setPage(0);view.focus(-1);paint();preview('room',0,0);closeButton.focus({preventScroll:true});},close,get opened(){return !disposed&&!panel.hidden;},get selectedItem(){return chosen;},update(dt,still){if(!panel.hidden)view.update(dt,still);},draw(renderer){return !disposed&&!panel.hidden&&view.draw(renderer);},
    /* What the panel leaves the room, in viewport pixels (y up from the canvas bottom): the strip beside
       it while it is docked down one edge, or the band above it once it is a full-width sheet (phone). */
    previewBox(w,h){const r=panel.getBoundingClientRect();
      if(r.left>4)return{x:0,y:0,w:Math.max(1,Math.floor(r.left)),h};
      const band=Math.max(1,Math.floor(r.top));return{x:0,y:h-band,w,h:band};},
    viewDebug(){return view.debug();},arrowsDebug(){return {visible:!arrows.hidden,target,centred:centred.textContent,buttons:[...arrows.querySelectorAll('button')].map(b=>{const r=b.getBoundingClientRect();return {label:b.getAttribute('aria-label'),x:r.x,y:r.y,w:r.width,h:r.height};})};},
    dispose(){if(disposed)return;close();disposed=true;view.dispose();arrows.remove();panel.remove();style.remove();}};
}
