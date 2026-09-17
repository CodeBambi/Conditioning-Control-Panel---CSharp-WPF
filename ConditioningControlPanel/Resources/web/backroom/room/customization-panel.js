import { decorationId } from './decoration-catalog.js';
import { catalogueStyle } from './customization-panel-style.js';
import { createVendingView } from './vending-view.js';

/** Picking previews temporarily. Buying and fitting are separate explicit actions. */
export function createCustomizationPanel({mount,vending,decorations=[],lex=(_,f)=>f,select,getState,restore,endPreview=()=>{},saveLayout=()=>Promise.resolve(false),shop=()=>null,preview=()=>{},slotOrder=[0,1,2],hasOwnership=()=>false,onClose=()=>{}}){
  const doc=mount.ownerDocument,L=(k,f)=>lex('br_custom_'+k,f)||f;
  // Two cabinet displays: nine room upgrades, then six collectible decorations.
  const BAYS=9;
  const items=[['screens4','4 extra screens'],['screens6','6 extra screens'],['mega','Ceiling projector'],['knight','Knight sculpture'],['queen','Queen sculpture'],['rook','Rook sculpture'],['vortex','Velvet Vortex'],['ribbon','Ribbon Galaxy'],['bloom','Prism Bloom'],['monstera','Monstera'],['ivy','Hanging ivy'],['terrarium','Terrarium'],['gallery','Gallery frame'],['portraits','Portrait pair'],['billboard','Wide billboard'],['knight_pedestal','Knight pedestal'],['queen_pedestal','Queen pedestal'],['rook_pedestal','Rook pedestal']];
  const style=doc.createElement('style');style.textContent=catalogueStyle;
  const panel=doc.createElement('section');panel.className='br-custom-panel';panel.hidden=true;panel.tabIndex=-1;panel.setAttribute('role','dialog');panel.setAttribute('aria-modal','true');panel.setAttribute('aria-label',L('title','Room Service'));
  const make=(tag,text,cls,parent=panel)=>{const e=doc.createElement(tag);e.textContent=text;if(cls)e.className=cls;parent.append(e);return e;};
  const button=(parent,text,fn)=>{const e=make('button',text,'',parent);e.type='button';e.onclick=fn;return e;};
  const header=make('header','','br-custom-header');
  const closeButton=button(header,lex('br_back','Back')||'Back',()=>close());closeButton.className='br-custom-back';closeButton.title=L('close','Close');
  make('h2',L('title','Room Service'),'',header);
  const stage=make('div','','br-custom-stage');
  const overview=button(stage,L('all_items','All items'),()=>{chosen=-1;paletteChoice=null;view.focus(-1);paint();preview('room',0,0);});overview.className='br-custom-overview';
  const chooser=make('div','','br-custom-items');chooser.setAttribute('role','group');chooser.setAttribute('aria-label',L('choose_item','Choose an item'));
  const hud=make('div','','br-custom-hud');
  const footer=make('footer','','br-custom-footer');button(footer,L('end_preview','End preview'),()=>{paletteChoice=null;endPreview();paint();});

  let chosen=-1,page=0,target=0,use='handles',paletteChoice=null,snapshot,previousFocus,disposed=false;
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
    if(paletteChoice!==null){preview('palette',paletteChoice);return;}
    if(chosen>=15){preview('statues',chosen-15,target);return;}
    if(chosen>=BAYS){preview('props',chosen-BAYS,chosen-BAYS);return;}
    preview(chosen<3?'screens':chosen<6?use:'floor',chosen<3?chosen:chosen<6?chosen-3:chosen-6,chosen<3?chosen:target);}
  // Switching between lever items keeps the cabinet in view: the target is the user's, not the item's.
  // A pick previews only; the explicit action below buys or fits the selection.
  function choose(i){paletteChoice=null;const keep=chosen>=3&&chosen<6&&i>=3&&i<6;chosen=i;if(!keep){target=i>=15?i-15:0;use=i>=15?'statues':'handles';}view.focus(i);paint();showPreview();}
  /** The chosen item as the room takes it: what to set, where, and whether it is already fitted there. */
  function fitting(){
    if(chosen<0)return null;
    if(paletteChoice!==null)return {category:'palette',on:paletteChoice,off:0,index:0,fitted:state().palette===paletteChoice};
    if(chosen>=15)return {category:'statues',on:chosen-15,off:-1,index:target,fitted:state().statues[target]===chosen-15};
    if(chosen<3)return {category:'screens',on:true,off:false,index:chosen,fitted:!!state().screens[chosen]};
    if(chosen<6)return {category:use,on:chosen-3,off:-1,index:target,fitted:state()[use][target]===chosen-3};
    if(chosen<BAYS)return {category:'floor',on:chosen-6,off:-1,index:0,fitted:state().floor===chosen-6};
    return {category:'props',on:true,off:false,index:chosen-BAYS,fitted:!!state().props[chosen-BAYS]};}
  const itemId=()=>{const f=fitting();return f?decorationId(f.category,f.on,f.index):null;};
  const isLocked=()=>!!itemId()&&!hasOwnership(itemId());
  function equip(){const f=fitting();if(f&&!isLocked()&&!f.fitted)apply(f.category,f.on,f.index);}
  function unequip(){const f=fitting();if(f)apply(f.category,f.off,f.index);}
  /** A right-click on an item: that piece comes off wherever it sits, whatever is selected. */
  function dismount(i){
    if(!Number.isInteger(i)||i<0||i>=items.length||!shop()?.state.ready||shop()?.state.busy)return false;
    const next=structuredClone(state());
    if(i<3)next.screens[i]=false;
    else if(i<6){next.statues=next.statues.map(v=>v===i-3?-1:v);next.handles=next.handles.map(v=>v===i-3?-1:v);}
    else if(i<BAYS){if(next.floor===i-6)next.floor=-1;}
    else if(i>=15)next.statues=next.statues.map(v=>v===i-15?-1:v);
    else next.props[i-BAYS]=false;
    endPreview();void saveLayout(next).then(()=>{if(!disposed)paint();});return true;
  }
  function apply(category,value,index=0){endPreview();const pending=select(category,value,index);paint();if(pending?.then)pending.then(()=>{if(!disposed)paint();});}
  function row(parent,entries,active,fn){entries.forEach(([key,label],i)=>{const b=button(parent,L(key,label),()=>fn(i));b.setAttribute('aria-pressed',String(active===i));});}
  function paint(){
    const expanded=!!hud.querySelector('details[open]');
    const more=label=>{const d=make('details','','br-custom-more',hud);d.open=expanded;make('summary',label,'',d);return d;};
    const focused=hud.contains(doc.activeElement)?[...hud.querySelectorAll('button')].indexOf(doc.activeElement):-1;
    [...chooser.children].forEach((b,i)=>b.setAttribute('aria-pressed',String(page===i)));hud.replaceChildren();overview.hidden=chosen<0;paintArrows();
    if(chosen<0){make('p',L('choose_item','Choose an item'),'br-custom-prompt',hud);return;}
    const title=make('h3',paletteChoice===null?L(...items[chosen]):L(...[['jewel','Jewel'],['lagoon','Lagoon'],['sunset','Sunset']][paletteChoice]),'',hud);title.setAttribute('aria-live','polite');
    const locked=isLocked();
    make('p',L('preview_only','Preview only. Nothing is spent until you buy.'),'br-custom-note',hud);
    if(locked&&chosen>=BAYS&&chosen<15)make('p',L('collect_locked','Also available from Daily Daze.'),'br-custom-note',hud);
    if(chosen>=3&&chosen<6){const placement=more(L('placement','Placement'));const modes=make('div','','br-custom-actions',placement);row(modes,[['statues','Statues'],['lever','Slot lever']],use==='statues'?0:1,i=>{use=i?'handles':'statues';paint();showPreview();});
      const targets=make('div','','br-custom-actions',placement);row(targets,use==='statues'?[['spot1','Pedestal 1'],['spot2','Pedestal 2'],['spot3','Pedestal 3']]:[['rose','Candy Rose'],['violet','Candy Violet'],['mint','Candy Mint']],target,i=>{target=i;paint();showPreview();});}
    else if(chosen>=15){const targets=make('div','','br-custom-actions',more(L('placement','Placement')));row(targets,[['spot1','Pedestal 1'],['spot2','Pedestal 2'],['spot3','Pedestal 3']],target,i=>{target=i;paint();showPreview();});}
    else if(chosen>=6&&chosen<BAYS){const palette=make('div','','br-custom-actions br-custom-palettes',more(L('palette','Colours')));row(palette,[['jewel','Jewel'],['lagoon','Lagoon'],['sunset','Sunset']],paletteChoice??state().palette,i=>{paletteChoice=i;paint();showPreview();});}
    const store=shop(),sale=store?.state;
    const statusKeys={loading:['loading_shop','Loading shop...'],saving:['saving','Saving...'],retry:['retry_note','The reply was lost. Retry safely to check the same purchase.'],low_sp:['low_sp','Not enough SP.'],insufficient_sp:['low_sp','Not enough SP.'],insufficient:['low_sp','Not enough SP.'],catalog_changed:['price_changed','Prices changed. Review the price before buying.'],revision_conflict:['layout_changed','Your room changed elsewhere. Choose again.'],owned:['already_owned','Already owned.'],unowned:['unowned','Own this item before using it.'],stale:['layout_changed','Your room changed elsewhere. Choose again.']};
    if(sale?.status!=='ready'){const label=statusKeys[sale?.status]||['shop_unavailable','The shop is unavailable. You can still preview.'];make('p',L(...label),'br-custom-note',hud);}
    if(sale?.retry)button(hud,L('retry','Retry safely'),()=>void store.retry()).disabled=!!sale.busy;
    else if(locked){
      const entry=sale?.catalog.find(row=>row.id===itemId());
      const buy=button(hud,entry?L('buy_sp','Buy for {n} SP').replace('{n}',String(entry.priceSp)):L('shop_unavailable','The shop is unavailable. You can still preview.'),()=>{endPreview();void store?.buy(itemId());});
      buy.className='br-custom-buy';buy.disabled=!sale?.ready||!sale.open||sale.busy||!entry;
    }else if(fitting()){
      const useButton=button(hud,fitting().fitted?L('remove','Remove'):L('use','Use'),fitting().fitted?unequip:equip);
      useButton.className='br-custom-remove';useButton.disabled=!sale?.ready||!sale.open||sale.busy;
    }
    if(focused>=0)hud.querySelectorAll('button')[focused]?.focus({preventScroll:true});
  }
  [['upgrades','Room upgrades'],['decorations','Decorations']].forEach(([key,label],i)=>button(chooser,L(key,label),()=>{page=i;chosen=-1;paletteChoice=null;view.setPage(i);paint();preview('room',0,0);}));
  function close(){if(panel.hidden)return;panel.hidden=true;arrows.hidden=true;onClose();previousFocus?.isConnected&&previousFocus.focus({preventScroll:true});previousFocus=null;}
  // One Tab ring for the sheet and the pane arrows, so a keyboard reaches the arrows from the dialog.
  function trap(e){if(e.key!=='Tab')return;const nodes=[...panel.querySelectorAll('button:not([disabled]),summary')].filter(n=>!n.closest('[hidden]')&&(!n.closest('details')||n.tagName==='SUMMARY'||n.closest('details').open)).concat(arrows.hidden?[]:[...arrows.querySelectorAll('button')]),first=nodes[0],last=nodes.at(-1);if(e.shiftKey&&(doc.activeElement===first||doc.activeElement===panel)){e.preventDefault();last.focus();}else if(!e.shiftKey&&doc.activeElement===last){e.preventDefault();first.focus();}}
  panel.addEventListener('keydown',e=>{e.stopPropagation();if(e.key==='Escape'){e.preventDefault();close();return;}trap(e);});
  for(const type of ['keyup','pointerdown','pointerup','click','wheel']){panel.addEventListener(type,e=>e.stopPropagation());arrows.addEventListener(type,e=>e.stopPropagation());}
  mount.append(style,arrows,panel);paint();
  return {refresh:paint,stepSlot:step,dismount,get slotArrows(){return !arrows.hidden;},get slotTarget(){return target;},open(){if(disposed||!panel.hidden)return;snapshot=structuredClone(state());previousFocus=doc.activeElement;panel.hidden=false;chosen=-1;paletteChoice=null;page=0;view.setPage(0);view.focus(-1);paint();preview('room',0,0);closeButton.focus({preventScroll:true});},close,get opened(){return !disposed&&!panel.hidden;},get selectedItem(){return chosen;},update(dt,still){if(!panel.hidden)view.update(dt,still);},draw(renderer){return !disposed&&!panel.hidden&&view.draw(renderer);},
    /* What the panel leaves the room, in viewport pixels (y up from the canvas bottom): the strip beside
       it while it is docked down one edge, or the band above it once it is a full-width sheet (phone). */
    previewBox(w,h){const r=panel.getBoundingClientRect();
      if(r.left>4)return{x:0,y:0,w:Math.max(1,Math.floor(r.left)),h};
      const band=Math.max(1,Math.floor(r.top));return{x:0,y:h-band,w,h:band};},
    viewDebug(){return view.debug();},arrowsDebug(){return {visible:!arrows.hidden,target,centred:centred.textContent,buttons:[...arrows.querySelectorAll('button')].map(b=>{const r=b.getBoundingClientRect();return {label:b.getAttribute('aria-label'),x:r.x,y:r.y,w:r.width,h:r.height};})};},
    dispose(){if(disposed)return;close();disposed=true;view.dispose();arrows.remove();panel.remove();style.remove();}};
}
