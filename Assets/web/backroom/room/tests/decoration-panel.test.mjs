import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {defaultDecorationLayout} from '../decoration-catalog.js';
const source=(await readFile(new URL('../customization-panel.js',import.meta.url),'utf8'))
 .replace("import { catalogueStyle } from './customization-panel-style.js';", "const catalogueStyle='';")
 .replace("import { createVendingView } from './vending-view.js';", "const createVendingView=options=>globalThis.testVending(options);")
 .replace("'./decoration-catalog.js'",JSON.stringify(new URL('../decoration-catalog.js',import.meta.url).href));
const {createCustomizationPanel}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
class Element {
 constructor(tag,doc){this.tagName=tag.toUpperCase();this.ownerDocument=doc;this.children=[];this.hidden=false;this.attrs={};}
 append(...nodes){for(const n of nodes){n.parent=this;this.children.push(n);}}
 setAttribute(k,v){this.attrs[k]=v;}
 addEventListener(){} remove(){if(this.parent)this.parent.children=this.parent.children.filter(n=>n!==this);}
 replaceChildren(){this.children=[];}
 contains(target){return target===this||this.children.some(n=>n.contains(target));}
 querySelectorAll(selector){const all=this.children.flatMap(n=>[n,...n.querySelectorAll('*')]);return selector==='*'?all:all.filter(n=>selector.startsWith('button')?n.tagName==='BUTTON':selector.startsWith('details')?n.tagName==='DETAILS'&&n.open:false);}
 querySelector(s){return this.querySelectorAll(s)[0]||null;}
 focus(){this.ownerDocument.activeElement=this;} get isConnected(){return true;}
}
function fixture(t){
 const doc={activeElement:null,createElement(tag){return new Element(tag,this);}},mount=new Element('div',doc);
 let choose;globalThis.testVending=o=>{choose=o.onSelect;return {focus(){},setPage(){},dispose(){},debug(){return {};}};};t.after(()=>delete globalThis.testVending);
 const layout=defaultDecorationLayout(),actions=[],previews=[];
 const store={state:{ready:true,open:true,busy:false,status:'ready',retry:false,catalog:[{id:'screens4',priceSp:30}],owned:[]},buy(id){actions.push(['buy',id]);},save(){},retry(){}};
 const panel=createCustomizationPanel({mount,getState:()=>layout,select:(...args)=>actions.push(['select',...args]),shop:()=>store,hasOwnership:id=>store.state.owned.includes(id),preview:(...args)=>previews.push(args),endPreview:()=>actions.push(['end']),onClose:()=>actions.push(['close'])});
 const button=label=>mount.querySelectorAll('button').find(b=>b.textContent===label);
 panel.open();return {panel,store,actions,previews,choose:i=>choose(i),button};
}
test('picking a locked screen previews without buying or fitting; Buy is explicit',t=>{
 const f=fixture(t);f.choose(0);assert.equal(f.actions.length,0);assert.deepEqual(f.previews.at(-1),['screens',0,0]);
 const buy=f.button('Buy for 30 SP');assert.ok(buy);assert.equal(buy.disabled,false);buy.onclick();assert.deepEqual(f.actions,[['end'],['buy','screens4']]);
 f.panel.close();assert.equal(f.actions.at(-1)[0],'close');
});
test('owned selection still requires explicit Use and cannot issue a second buy',t=>{
 const f=fixture(t);f.store.state.owned=['screens4'];f.choose(0);assert.equal(f.actions.length,0);assert.equal(f.button('Buy for 30 SP'),undefined);
 f.button('Use').onclick();assert.deepEqual(f.actions,[['end'],['select','screens',true,0]]);
});
test('busy or unavailable shop disables the purchase action',t=>{
 const f=fixture(t);f.store.state.busy=true;f.choose(0);assert.equal(f.button('Buy for 30 SP').disabled,true);
 f.store.state.busy=false;f.store.state.open=false;f.panel.refresh();assert.equal(f.button('Buy for 30 SP').disabled,true);
});
