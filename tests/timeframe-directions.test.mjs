import test from 'node:test';
import assert from 'node:assert/strict';
import {renderTimeframes} from '../web/timeframe-directions.js';
const row={timeframe:'5 min',minutes:5,direction:'STRONG_UP',ema:'UP',rsi:'UP',macd:'UP',bars:60,asOf:'2026-10-21T14:00:00Z',status:'Latest closed bars'};
test('timeframe table separates combined direction, indicator votes, timestamp and data status',()=>{
  const html=renderTimeframes([row,{...row,timeframe:'1 day',direction:'UNAVAILABLE',status:'Daily history unavailable from Schwab'}],null);
  for(const text of ['Direction by timeframe','5 min','Strong bullish','EMA 9/21','RSI 14','MACD','1 day','Daily history unavailable','Data through (ET)','fresh realtime quote unverified'])assert.ok(html.includes(text),text);
  assert.doesNotMatch(html,/BUY|SELL|probability/);
});
test('opposing estimates, warm-up and absent data remain explicit and text is escaped',()=>{
  assert.match(renderTimeframes(null,null),/Loading/);
  const html=renderTimeframes([{...row,direction:'MIXED',ema:'DOWN',rsi:'NEUTRAL',status:'<img src=x>',timeframe:'<script>'}],null);
  assert.match(html,/Mixed/);assert.match(html,/Bearish/);assert.match(html,/Neutral/);assert.match(html,/&lt;img/);assert.doesNotMatch(html,/<img|<script>/);
});
