export const PROP_IDS = Object.freeze(['monstera','ivy','terrarium','gallery','portraits','billboard']);
export const SCREEN_IDS = Object.freeze(['screens4','screens6','mega']);
export const LEVER_IDS = Object.freeze(['lever_knight','lever_queen','lever_rook']);
export const FLOOR_IDS = Object.freeze([null,'floor_ribbon','floor_bloom']);
export const PALETTE_IDS = Object.freeze([null,'palette_lagoon','palette_sunset']);
export function decorationId(category,index,target=0) {
  if(category==='props')return index?PROP_IDS[target]:null;
  if(category==='screens')return index?SCREEN_IDS[target]:null;
  if(category==='handles')return LEVER_IDS[index]||null;
  if(category==='floor')return FLOOR_IDS[index]||null;
  if(category==='palette')return PALETTE_IDS[index]||null;
  return null;
}
export const defaultDecorationLayout = () => ({screens:[false,false,false],props:Array(6).fill(false),statues:[0,1,2],handles:[-1,-1,-1],floor:0,palette:0});
export function ownedLayout(layout,owned=[]) {
  const out=defaultDecorationLayout(),have=new Set(owned),value=(v,min,max,fallback)=>Number.isInteger(v)&&v>=min&&v<=max?v:fallback;
  for(const [category,ids] of [['screens',SCREEN_IDS],['props',PROP_IDS]])out[category]=ids.map((id,i)=>!!layout?.[category]?.[i]&&have.has(id));
  out.statues=out.statues.map((v,i)=>value(layout?.statues?.[i],-1,2,v));
  out.handles=out.handles.map((v,i)=>{const n=value(layout?.handles?.[i],-1,2,-1);return n<0||have.has(LEVER_IDS[n])?n:-1;});
  for(const [category,ids,max]of [['floor',FLOOR_IDS,2],['palette',PALETTE_IDS,2]]){const n=value(layout?.[category],category==='floor'?-1:0,max,0);out[category]=n<=0||have.has(ids[n])?n:0;}
  return out;
}
