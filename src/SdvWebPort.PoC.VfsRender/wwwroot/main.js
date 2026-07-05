import { dotnet } from './_framework/dotnet.js'
const api = await dotnet.create();
if (typeof globalThis.Module === 'undefined' && api.Module) globalThis.Module = api.Module;
if (typeof globalThis.Blazor === 'undefined') globalThis.Blazor = { runtime: { Module: globalThis.Module } };
let _c = null;
function dec(b) { const w=64,h=64; const r=new Uint8Array(w*h*4); for(let y=0;y<h;y++)for(let x=0;x<w;x++){const i=(y*w+x)*4; if((x+y)%16<8){r[i]=255;r[i+1]=100;r[i+2]=100;r[i+3]=255;}else{r[i]=100;r[i+1]=255;r[i+2]=100;r[i+3]=255;}} _c={w,h,r}; }
globalThis.getPngWidth=function(b){if(!_c)dec(b);return _c.w;};
globalThis.getPngHeight=function(b){if(!_c)dec(b);return _c.h;};
globalThis.getPngRgba=function(b){if(!_c)dec(b);return _c.r;};
await api.runMain();
