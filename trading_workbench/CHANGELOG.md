# Changelog

## 0.1.13

- Keep market zooms at the newest edge following incoming quotes while
  preserving their selected time span. Leave historical selections anchored,
  show following/historical state and add Follow latest without resetting zoom.

- Fix the position-sync HTTP route returning an empty response. Add a loopback
  HTTP regression test and retain cancellation when a viewed page is hidden.
- Handle older analytics responses without a gross field by reconciling net
  plus fees, and show historical CSV positions in a collapsed, dated section
  so imported entry prices are not mistaken for current broker holdings.

- Add a transaction-derived futures position card for the selected contract.
  Sync read-only Accounts and Trading history every 30 seconds while viewed;
  display the estimated side, contracts, average entry, sync time and last fill.
  Keep accounts separate and copy estimates into manual analysis controls only
  when the user confirms Use in analysis.
- Handle partial exits, scaling and split reversals with decimal quantities
  and prices. Deduplicate transactions, replace the full 59-day window on sync,
  persist estimates across restart and show UNKNOWN for incomplete or ambiguous
  sequences. Retain historical values on sync failures and label all estimates
  as dependent on a starting-flat assumption and posted transaction availability.

## 0.1.12

- Carry the previous signal for the same contract, chart source and unchanged
  manual position into repeat analysis (up to three hours). Market default
  prompt v8 evaluates the original trigger and expiry before proposing a
  replacement, and does not silently move levels or restart the trigger clock.
- Distinguish model WAIT calls from app data/position blocks in the signal
  card and Home Assistant logs, with safe guard categories. Preserve existing
  freshness, position and price-level checks; custom prompts stay unchanged.

## 0.1.11

- Add a living broad-market and commodities brief above the market chart.
  Research hourly on weekdays from 8 AM through 3 PM Eastern, independently
  of open pages. Preserve relevant points, update meaningful developments,
  and track last checked separately from last changed.
- Persist the brief and changed revisions across restarts. Retain the previous
  text on failed checks, skip missed-hour backlogs, and defer or interrupt
  background research for manual analysis using the shared ChatGPT connection.

## 0.1.10

- Add direction estimates for 1m, 5m, 15m, 30m, 1h, and daily candles, including
  EMA/RSI/MACD votes, timestamps, and history status. Aggregate only complete
  intraday candles; cache broker daily history separately and leave unsupported
  or insufficient-history rows unavailable. Include directions in AI snapshots.
- Add selected-instrument EMA 9/21 overlays, optional Bollinger Bands, and a
  technical panel with SMA 20/50, RSI 14, MACD 12/26/9, ATR 14, relative volume,
  and prior-20-bar support/resistance. Show observed crossovers and breakouts.
- Calculate indicators from up to 180 consecutive closed minute bars; reset
  after gaps and show unavailable warm-ups. Mark stale/unverified readings as
  historical and include the same readings in AI snapshots. Market default
  prompt v7 uses supplied readings; saved custom prompts remain unchanged.

## 0.1.9

- Add Settings → Prompts with market and journal editors, persistent custom
  prompts, latest-default comparisons, reset edits, and explicit adoption of
  updated defaults. Preserve custom text across app updates and flag new defaults.
- Freeze the selected prompt when analysis starts and save its exact text and
  version with the result. Show Prompt used in analysis history; keep older
  history readable without inventing its original prompt.

## 0.1.8

- Show gross trading P/L before costs in the main journal box and fees and
  commissions in a separate box. Keep net P/L after costs in detailed statistics
  and label the net P/L chart explicitly.

## 0.1.7

- Remove journal setup tags from forms, filters, trade details, breakdowns, CSV
  exports, and portfolio analysis. Older saved trades remain readable; notes,
  risk, fills, and P/L retain their existing behavior.
- Make market analysis a decisive intraday trade call for minutes-to-hours holds:
  one NOW setup with entry/stop/target, or a conditional NEXT trigger with an
  expiry. Preserve position, data-freshness, and price-level checks. Record
  market prompt version v6; journal review v2 omits setup categories.

## 0.1.6

- Fix Market Analyze and manual position changes after moving the position form
  beside the chart. Retain control references across layout changes and add a
  DOM regression test covering Analyze, positions, contract switching, and journal
  review.

## 0.1.5

- Show the latest observed quote as a separate gold marker on the minute chart,
  updating with each quote refresh. Keep historical bars unchanged, preserve
  zoom, autoscale visible prices, and label delayed, stale, or unverified quotes.
- Automatically refresh the viewed contract's quote every second. Pause when
  the market page is hidden, cancel obsolete requests on selection changes, and
  fetch closed minute bars once per minute. Remove the polling checkbox.
- Default each market contract's manual position selector to Flat.
- Add analysis stage, elapsed-time, 15-second heartbeat, and Codex process logs
  to Home Assistant app logs without prompts, positions, credentials, or raw
  subprocess output. Report safe failure categories and restart recovery.
- Limit analysis price refreshes to 60 seconds, with a visible timeout message.

## 0.1.4

- Report unitemized daily opening-cash changes separately from sweeps, fees, and
  trade P/L, with visible preview warnings. Transaction-row cash errors still fail.
- Handle split fills of verified closing orders that reverse through flat, and
  unique one-second posting/execution timestamp differences. Keep execution
  identities stable and reject ambiguous matches or missing opening history.

- Recognize Micro Ether cash-settled expirations when one unchanged open contract,
  its preceding official mark, and the cash ledger identify a valid settlement
  price. Preserve fees, reconciliation, duplicate protection, and explicit
  settlement-derived exit labels; reject ambiguous settlement events.

## 0.1.3

- Recognize Micro Ether (MET) futures statements using the 0.1 contract point
  value; add historical METF26 accounting/reconciliation and reimport checks.
- Recognize standard WTI crude (CL) with a 1,000 point value and 0.01 price tick.
  Verify mixed CL/MCL/MES/MET/MGC imports, fee reconciliation, and duplicate skips
  with synthetic data.

- Put market Analyze beside the chart controls, name its selected contract, and
  default the chart to the resolved MES contract. Keep results/history scoped to
  the displayed contract and preserve manual selections through refresh/rollover.
- Add a separate Review portfolio button in the journal toolbar.

- Display market results as a compact trend, position-aware action, short reason,
  and entry/stop/target. Remove research references and commentary; collapse price
  follow-up until requested. Preserve journal review and recommendation guards.

- Separate market-analysis and journal-review prompts and record their distinct
  versions in saved results. Enable brief live research for market analysis only,
  with an advisory two-search budget and a 90-second hard
  execution limit. Journal review remains snapshot-only with a four-minute limit.

- Expand market analysis guidance to cover moving averages, RSI, MACD, trendlines,
  support/resistance, and volume using available observations. Allow position-size
  discussion with explicit inputs, assumptions, and per-contract risk.

- Consolidate market refresh, frozen inputs, execution, guards, and saving behind
  one background analysis workflow shared by Market and Journal.
- Reconnect to progress after leaving the page; allow explicit cancellation and
  show an existing active run instead of queuing duplicate requests.
- Preserve run status across restarts and mark unfinished work interrupted without
  automatically resubmitting it.

## 0.1.2

- Bundle Codex CLI 0.162.1 for ARM64 and add Sign in with ChatGPT using a device
  code, cancellation, connection checks, and sign-out inside the app.
- Keep the app's Codex login in persistent private storage across restarts.
- Verify the bundled auth transport in CI without credentials or model usage.
- Double-click the market chart to restore its default recent-data zoom.
- Add drag selection, pointer-centered scroll zoom, zoom buttons, and double-click
  reset to the journal cumulative P/L graph.
- Plot journal closing times on the time axis and keep cumulative balances intact
  while zooming. The vertical axis follows the visible balance range.

## 0.1.1

- Restore opening fills from saved overlapping statements when a rolling export
  starts mid-position; retain cash reconciliation and duplicate protection.
- Accept a missing Futures holdings section when the latest statement is flat.
- Drag across a chart to zoom into a selected time range; scroll to zoom around
  the pointer. Price limits automatically follow visible observations.
- Zoom locally without fetching history again; retain zoom buttons and Full range.
- Simplify CSV import to the tested Schwab futures statement with Eastern time,
  automatic checks, duplicate skipping, and optional statement details.
- Match Schwab terminology: Client ID and Client Secret in settings, placeholders,
  validation messages, and setup documentation.
- Refresh frontend asset versions so updated labels load after an app update.
- Saved credentials retain their existing storage format.

## 0.1.0 — preparation

- ARM64 container definition, persistent storage, and Home Assistant ingress.
- Existing journal, imports, reports, market, and analysis source retained.
- GitHub checks and version-tag container publishing pipeline.
- Journal is the first Pi acceptance milestone. Schwab and Codex setup follow.
- Container/Pi execution has not yet been verified; no image published.
