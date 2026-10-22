import {clampWindow} from './chart-viewport.js?v=ha-0.1.13';

export function atLatest(window,bounds){
  return !!(window&&bounds&&Math.abs(bounds.end-window.end)<=1000);
}
export function updateMarketWindow(window,bounds,following){
  if(!bounds)return null;
  const span=window?window.end-window.start:4*3600000;
  return clampWindow(following?{start:bounds.end-span,end:bounds.end}:window??bounds,bounds);
}
export function selectMarketWindow(window,previous,bounds,gesture={},wasFollowing=false){
  const following=atLatest(window,bounds)||(gesture.nearRight===true&&(wasFollowing||atLatest(previous,bounds)));
  return {window:updateMarketWindow(window,bounds,following),following};
}
