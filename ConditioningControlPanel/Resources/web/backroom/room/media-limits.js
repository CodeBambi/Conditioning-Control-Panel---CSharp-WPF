// Bounds apply before native decoding as well as the compatibility path.
export const MEDIA_LIMITS = Object.freeze({ bytes:32*1024*1024, pixels:4*1024*1024, frames:2000, loadMs:10000 });
// `reason` tells a caller WHICH kind of refusal this is, because the two need opposite handling and the
// message string is not a contract. 'budget' means we declined to spend the pixels, bytes or frames, so no
// caller may go and decode the same file another way. 'transfer' means the file did not arrive or we could
// not read its header - nothing was measured and nothing was declined, so a second, independent attempt
// (a plain <img>, the browser's own pipeline) is legitimate and is often the thing that succeeds. The
// default is 'budget' so an untagged throw keeps the conservative behaviour.
export class MediaLimitError extends Error { constructor(message, reason = 'budget') { super(message); this.name='MediaLimitError'; this.reason = reason; } }
export const refusedMedia = error => error?.name === 'MediaLimitError' || error?.name === 'AbortError';
/** A refusal that spent our budget allowance, as opposed to one where the bytes never arrived. */
export const overBudget = error => error?.name === 'MediaLimitError' && error.reason !== 'transfer';
export function checkDimensions(width,height) {
  if (!(width>0 && height>0) || width*height>MEDIA_LIMITS.pixels) throw new MediaLimitError('Image dimensions exceed media budget');
}
// Read dimensions from common image headers before asking the browser to allocate pixels.
export function imageDimensions(data,type) {
  const b=new Uint8Array(data),v=new DataView(data),ascii=(p,n)=>String.fromCharCode(...b.subarray(p,p+n));
  const u24=p=>b[p]|b[p+1]<<8|b[p+2]<<16;
  if(type==='image/gif' && b.length>=10 && ascii(0,3)==='GIF') return [v.getUint16(6,true),v.getUint16(8,true)];
  if(type==='image/png' && b.length>=24 && v.getUint32(0)===0x89504e47) return [v.getUint32(16),v.getUint32(20)];
  if(type==='image/webp' && b.length>=20 && ascii(0,4)==='RIFF' && ascii(8,4)==='WEBP') {
    for(let p=12;p+8<=b.length;) {
      const size=v.getUint32(p+4,true),s=p+8;
      if(s+size>b.length) return null;
      const tag=ascii(p,4);
      if(tag==='VP8X' && size>=10) return [u24(s+4)+1,u24(s+7)+1];
      if(tag==='VP8 ' && size>=10 && ascii(s+3,3)==='\x9d\x01\x2a') return [v.getUint16(s+6,true)&0x3fff,v.getUint16(s+8,true)&0x3fff];
      if(tag==='VP8L' && size>=5 && b[s]===0x2f) return [1+(v.getUint32(s+1,true)&0x3fff),1+((v.getUint32(s+1,true)>>>14)&0x3fff)];
      p=s+size+(size&1);
    }
  }
  if(type==='image/jpeg' && b.length>=4 && b[0]===255 && b[1]===216) {
    for(let p=2;p+3<b.length;) {
      if(b[p++]!==255) return null;
      while(b[p]===255)p++;
      const marker=b[p++];
      if(marker===217 || marker===218)break;
      if(marker===1 || (marker>=208 && marker<=215))continue;
      const size=v.getUint16(p);if(size<2 || p+size>b.length)break;
      if([192,193,194,195,197,198,199,201,202,203,205,206,207].includes(marker) && size>=7) return [v.getUint16(p+5),v.getUint16(p+3)];
      p+=size;
    }
  }
  return null;
}
export async function boundedImageBytes(response, signal, maxBytes = MEDIA_LIMITS.bytes) {
  maxBytes = Math.max(1, Math.min(MEDIA_LIMITS.bytes, Number(maxBytes) || MEDIA_LIMITS.bytes));
  if(Number(response.headers.get('content-length'))>maxBytes) {
    await response.body?.cancel();throw new MediaLimitError('Image transfer exceeds media budget');
  }
  if(!response.body?.getReader) throw new MediaLimitError('Image stream unavailable','transfer');
  const reader=response.body.getReader(),chunks=[];let size=0;
  const abort=()=>{void reader.cancel().catch(()=>{});};
  signal?.addEventListener('abort',abort,{once:true});
  try {
    for(;;) {
      signal?.throwIfAborted();const {done,value}=await reader.read();signal?.throwIfAborted();
      if(done)break;
      size+=value.byteLength;
      if(size>maxBytes)throw new MediaLimitError('Image transfer exceeds media budget');
      chunks.push(value);
    }
    const out=new Uint8Array(size);let offset=0;
    for(const chunk of chunks){out.set(chunk,offset);offset+=chunk.byteLength;}
    return out.buffer;
  } catch(error) { await reader.cancel().catch(()=>{});throw error; }
  finally {signal?.removeEventListener('abort',abort);reader.releaseLock();}
}
