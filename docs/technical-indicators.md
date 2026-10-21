# Market monitor indicators

The selected instrument's latest 180 completed one-minute bars feed the monitor
and market AI snapshot. Zoom changes only the viewport. The one-second quote
marker never becomes a candle or indicator input. A missing minute resets the
calculation window; insufficient periods produce null values, displayed as —.

- EMA 9/21 and MACD 12/26/9 use SMA-seeded exponential averages, alpha 2/(N+1).
  The MACD signal is a nine-period EMA of the valid MACD values.
- SMA 20/50 averages closing prices. Bollinger Bands use SMA 20 ± two population
  standard deviations of the same 20 closing prices.
- RSI 14 applies Wilder smoothing to gains/losses, starting with 14 changes.
  No movement produces 50; gains without losses produce 100.
- ATR 14 applies Wilder smoothing to true range using the previous close.
  Both RSI and ATR require 15 bars for their first complete 14-change seed.
- Relative volume divides the last bar's volume by the preceding 20-bar average.
  Zero comparison volume produces no ratio. Support/resistance are the low/high
  of the preceding 20 bars, excluding the current bar.

Crossover signals require the latest two calculated readings to cross. Breakout
signals compare the latest close to the preceding 20-bar high/low. RSI extremes
and volume spikes describe the latest closed bar; they do not execute trades.
Signals retain the bar timestamp and are labeled historical unless both bars
and a verified realtime quote are within 120 seconds. Stock and futures sessions
can contain gaps; these restart warm-up rather than supplying invented bars.

Primary formula references: [moving averages](https://www.tradingview.com/support/solutions/43000502589-moving-averages/),
[RSI](https://www.tradingview.com/support/solutions/43000502338-relative-strength-index-rsi/),
[MACD](https://www.tradingview.com/support/solutions/43000502344-moving-average-convergence-divergence-macd-indicator/),
and [ATR](https://www.tradingview.com/support/solutions/43000501823-average-true-range-atr/).

## Direction by timeframe

The table estimates direction independently for 1m, 5m, 15m, 30m, 1h, and daily
candles. It uses its own seven-day intraday history window, not the chart zoom
or chart's selected day range. Intraday buckets align to clock boundaries; only
complete buckets containing every observed minute qualify. Partial and gapped
buckets are excluded. Higher-timeframe averages carry across session gaps using
up to 180 observed complete candles; no missing candle is interpolated.

Each row requires 34 candles: EMA 9/21 votes by alignment, RSI votes above 55 or
below 45, and MACD votes by histogram sign. Their net vote determines bullish or
bearish; all three agreeing is Strong. Canceling opposing votes are Mixed, and
all neutral readings are Neutral. These are transparent indicator heuristics,
not probabilities, broker orders, or a forecast for the next N minutes.

Daily candles come from a separate authenticated Schwab daily-history request
covering one year. The current Eastern calendar day is excluded. Results persist
under `daily-history` in the app's data directory and refresh once per Eastern
date; failed requests retry after five minutes while viewed. Unsupported symbols
keep an unavailable row rather than substituting intraday data. Daily timestamps
describe the broker's candle dates, not an intraday execution time. API behavior
is covered with simulated responses; live daily support depends on the symbol
and Schwab entitlement. The daily fetch runs separately from quote polling and
is canceled when the page is hidden or the selected symbol changes.
