// Independent cent-based reconciliation; no writes to the personal journal.
import {readFile} from 'node:fs/promises';
import {parseCsv} from '../web/csv.js';
const raw=await readFile(process.argv[2],'utf8');
const rows=parseCsv(raw.replace(/="([0-9]+)"/g,'"$1"'));
const start=rows.findIndex(r=>r[0]==='Futures Statements');
const end=rows.findIndex((r,i)=>i>start&&r[0].startsWith('Total Cash'));
const ledger=rows.slice(start+2,end).filter(r=>r.length===10);
const cents=s=>!s||s==='--'?0:Math.round(Number(s.replace(/[$,]/g,''))*100);
const book=new Map(),closed=[];let fees=0,settlement=0,tradeAmounts=0,sweeps=0,count=0;
for(const row of ledger){if(row[3]==='ADJ')settlement+=cents(row[8]);if(row[3]==='FSWP')sweeps+=cents(row[8]);if(row[3]!=='TRD')continue;
 count++;const match=row[5].match(/^(BOT|SOLD) ([+-]\d+) \/(\w+):\w+ @([\d.]+)$/);if(!match)throw new Error('Unsupported trade description');
 const [,side,qty,symbol,price]=match;const multiplier=symbol.startsWith('MCL')?100:symbol.startsWith('MES')?5:symbol.startsWith('MGC')?10:null;if(!multiplier)throw new Error('Unknown instrument');
 const cost=-cents(row[6])-cents(row[7]);fees+=cost;tradeAmounts+=cents(row[8]);
 const state=book.get(symbol)??{qty:0,cash:0,fees:0,entryQty:0,entryCost:0,symbol};book.set(symbol,state);
 let remaining=Math.abs(Number(qty)),remainingFee=cost;const sign=side==='BOT'?1:-1;
 while(remaining){const closing=state.qty!==0&&Math.sign(state.qty)!==sign;const portion=closing?Math.min(Math.abs(state.qty),remaining):remaining;const fee=portion===remaining?remainingFee:cost*portion/Math.abs(Number(qty));state.cash-=sign*portion*cents(price)*multiplier;state.fees+=fee;if(!closing){state.entryQty+=portion;state.entryCost+=portion*Number(price);}state.qty+=sign*portion;remaining-=portion;remainingFee-=fee;if(state.qty===0){closed.push({symbol,gross:state.cash/100,fees:state.fees/100,net:(state.cash-state.fees)/100});Object.assign(state,{cash:0,fees:0,entryQty:0,entryCost:0});}}
}
const gross=closed.reduce((s,t)=>s+Math.round(t.gross*100),0),closedFees=closed.reduce((s,t)=>s+Math.round(t.fees*100),0);
console.log(JSON.stringify({executions:count,closedTrades:closed.length,gross:gross/100,closedFees:closedFees/100,net:(gross-closedFees)/100,allExecutionFees:fees/100,settlements:settlement/100,tradeAmounts:tradeAmounts/100,sweeps:sweeps/100,ledgerNet:(settlement+tradeAmounts-fees)/100,endFuturesCash:(settlement+tradeAmounts-fees+sweeps)/100,open:[...book.values()].filter(p=>p.qty).map(p=>({symbol:p.symbol,quantity:p.qty,averageEntry:p.entryCost/p.entryQty,entryFees:p.fees/100})),bySymbol:[...new Set(closed.map(t=>t.symbol))].map(symbol=>({symbol,trades:closed.filter(t=>t.symbol===symbol).length,gross:closed.filter(t=>t.symbol===symbol).reduce((s,t)=>s+Math.round(t.gross*100),0)/100,net:closed.filter(t=>t.symbol===symbol).reduce((s,t)=>s+Math.round(t.net*100),0)/100}))},null,2));
