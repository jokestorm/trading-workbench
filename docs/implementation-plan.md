# Trading Journal and Market Monitor — Implementation Plan

## 1. Product goal

Build one local application containing two independently useful tools:

1. **Trade Journal:** track trades and realized profit/loss, graph performance, and analyze any selected date range.
2. **Market Monitor:** use the user's Schwab API to monitor equity and futures prices where supported, retain useful history, and provide a plug-in point for AI signal analysis.

The journal is the first priority. It must work without a live market connection or an AI provider. The monitor must work without imported trades. Linking a trade to its market chart is a later shared feature.

This plan proposes implementation; it does not authorize placing orders or enabling automated execution.

## 2. Starting assumptions and scope

- Personal, single-user application running on Windows with a browser UI.
- Optimize for personal usefulness and easy iteration. Add operational machinery only when actual use requires it.
- Local storage, one backend process, and no cloud deployment required.
- Initial instruments: equities and dated futures contracts. Options, multi-leg positions, foreign-currency accounting, and corporate-action processing are deferred until needed.
- Start with CSV imports and manual entry. Add broker synchronization when actual account data has been validated.
- Capture setup, notes, and optional planned risk early; do not require them to calculate P/L.
- Start market monitoring with a small user-selected watchlist.
- Collect market data while the application is running. Continuous collection while the PC sleeps or is off would require a later hosting decision.
- AI provider and model remain configurable decisions. The initial integration accepts structured snapshots and returns validated structured analysis.

## 3. Application structure

Recommended baseline: ASP.NET Core backend, SQLite database, and React/TypeScript frontend. Confirm development prerequisites before scaffolding.

Implementation status: .NET SDK 10.0.401 is available. ASP.NET Core now hosts the dependency-free HTML/CSS and JavaScript frontend, with Microsoft.Data.Sqlite storage. Retain the simple frontend while it meets workflow needs; adopt React/TypeScript only if growing UI complexity justifies the change.

Start with one backend project and one frontend project. Organize the backend into folders for Journal, Analytics, Market, and Integrations. Split projects only when a concrete dependency or build problem makes it worthwhile. Use straightforward functions/services and explicit input/output types; avoid a generic repository layer, plug-in framework, event bus, or abstractions for hypothetical providers.

Keep this as one application with internal modules, not separate deployed services:

```text
Browser
  Trade Journal | Market Monitor | Settings
                         |
                 ASP.NET Core application
  Accounting | Analytics | Broker Adapter | Market Data | AI Adapter
                         |
                       SQLite
```

Share instrument definitions, settings, chart components, authentication infrastructure, and background-job management. Keep accounting independent of market streaming and AI.

Use a provider-neutral market-data interface with Schwab as its initial implementation. Keep broker trade imports separate from market quotes: support for one must not be assumed to imply support for the other.

### Boundaries that make changes inexpensive

- Keep P/L and trade reconstruction in ordinary functions/services that can run without the UI, database, or Schwab.
- Normalize CSV and broker records into the same small fill representation before accounting. Manual round-trip entry also creates entry/exit fills underneath.
- Preserve source fills and keep derived trade totals rebuildable. Keep user notes and corrections attached through stable identities; regrouping must preserve or explicitly remap them.
- Use one shared filter definition for journal tables, charts, and summary calculations.
- Put Schwab response formats inside its adapter. Return only the instrument, quote, and bar types the app actually uses.
- Add the AI adapter when AI work begins; define its interface from the first real request and response.
- Keep the database schema minimal and evolve it through small migrations with a backup before changes. Do not pre-create tables for later research features.
- Add abstractions at actual change boundaries, rather than an interface for every class.

These boundaries allow chart/layout changes without rewriting accounting, import changes without rewriting reports, and AI prompt changes without changing quote collection. They reduce refactoring scope; discoveries in real data may still require changes.

## 4. Tool A — Trade Journal

### First useful release

**Trade entry and import**

- Import one confirmed broker CSV format through a preview screen.
- Show validation errors and duplicates before committing an import.
- Enter or correct trades manually.
- Preserve source records and record corrections separately.
- Support long/short positions, multiple fills, partial exits, fees, and commissions.
- Show open positions separately from closed-trade statistics.

**Journal screens**

- Summary: realized net P/L, trade count, win rate, average winner/loser, and expectancy.
- Trade list: instrument, direction, dates, quantity, entry/exit, fees, and P/L.
- Trade detail: fills, notes, setup, optional planned risk, and plan compliance.
- Charts: cumulative realized P/L and P/L by day; add P/L distribution and instrument/setup breakdowns next.
- Filters: date range, account, instrument, direction, and setup. Offer common presets and a custom range.
- CSV export of the filtered trades.

### Accounting rules to settle before implementation

- Reconstruct trades per account and actual instrument contract, from flat position to flat position.
- Default to FIFO lot matching; document that this is analytical accounting and may differ from a broker's chosen basis method.
- Split reversal fills between the closing trade and newly opened trade, allocating quantities and fees.
- Use decimal arithmetic and configurable instrument multipliers.
- Attribute closed-trade dashboard results to closing time in the selected reporting timezone. Any later realized-fill view must be clearly distinguished.
- Treat break-even trades as a separate class. Define expectancy as mean net P/L over all selected closed trades.
- Calculate R only when a positive initial planned-risk amount is recorded. Show coverage, rather than treating missing R as zero.
- Label the equity curve as cumulative realized trading P/L. Initial drawdown is dollar drawdown of this curve, not account-equity percentage drawdown.
- If an import begins with an existing position, require opening-position information or flag its cost basis as unresolved.
- Deferred corporate actions or unsupported records must be flagged, not silently included with incorrect accounting.

### Minimum data model

- `Account`: source and display identity.
- `Instrument`: asset type, root/contract symbols, currency, expiration, tick size, point value.
- `ImportBatch` and source records: provenance, validation status, deduplication keys.
- `Fill`: account, instrument, side, quantity, price, execution timestamp, fees, source identity.
- `Trade` and `TradeFillAllocation`: reconstruction plus quantity and fee allocation.
- `TradeJournal`: stable notes, setup, initial risk, compliance, and overrides, preserved during reconstruction.

## 5. Tool B — Market Monitor and AI Hook

### Initial market monitor

- Connect the user's Schwab application using OAuth and encrypted token storage.
- Add/remove watchlist instruments; distinguish actual futures contracts from their roots.
- Display latest price, timestamp, price change, connection state, and delayed/stale data indicators when available.
- Provide an intraday candlestick chart and selected-period price change.
- Retain one-minute bars initially, with historical backfill only where supported.
- Use streaming where verified; use polling where supported and sufficient.
- Recover from disconnections, prevent duplicate bars, and record data gaps.
- Aggregate larger intervals from the selected canonical bars; do not mix sources invisibly.

### Capability validation gate

Before committing to detailed Schwab integration, verify with the user's approved app and account:

1. Equity quotes and requested dated futures symbols.
2. Real-time versus delayed data and timestamps.
3. Streaming availability, subscription constraints, and supported services.
4. Historical intervals, retention, and dated-contract coverage.
5. Trade execution and fee availability, independently of market-data availability.
6. OAuth callback requirements, refresh behavior, and operational limits.

Record results in a capability matrix. Unsupported features remain explicit gaps; a different provider is a separate decision. CSV journal imports remain usable regardless.

Schwab's public developer portal advertises OAuth and real-time API products, but that alone does not establish account-specific futures history, execution, or streaming support. Reference: [Schwab Developer Portal](https://developer.schwab.com/).

### AI integration contract

Start with on-demand analysis of one selected symbol. Later add scheduled analysis with a configurable cadence and budget.

Inputs:

- Instrument, as-of timestamp, data freshness, and recent completed candles.
- Deterministic features such as short-horizon returns, moving averages, and volatility.
- Optional user-selected analysis intent.

Outputs:

- Market classification.
- Candidate signal or no signal, direction, and horizon.
- Brief rationale, invalidation conditions, and optional confidence score.

Persist the input snapshot, provider/model, prompt version, response, validation status, latency, and usage. Separate analysis-request time, latest input time, and response-available time. AI failures must not interrupt quote collection.

Show AI output in a dedicated panel and signal history. A confidence score is descriptive until calibrated through evaluation. AI does not submit orders.

Start evaluation with fixed horizons and a simple deterministic baseline. Measure outcomes only after the signal was available, using documented reference prices; report these as signal outcomes, not simulated executable profits.

## 6. Delivery milestones

Each stage ends with a runnable version and a short review of the actual workflow. Capture what works, what feels awkward, and requested changes before advancing. Treat later-stage scope as provisional. Finish and check the current stage, then get user feedback before committing to the next substantial stage.

### Stage 0 — Small app shell and workflow preview

Check prerequisites; create the local app with Journal and Market tabs. Use clearly labeled sample data to preview a trade list, summary, date filter, and market-panel layout. Document initial accounting assumptions without freezing them.

**Checkpoint:** try the navigation and trade-review layout. Adjust the workflow before implementing substantial persistence or integrations.

### Stage 1 — Manual trades and trustworthy P/L

Add SQLite, a small instrument configuration, and manual entry of completed long/short trades with fees. Store entry/exit fills underneath. Add editing, notes, the trade list, net P/L summary, and a date range. Support partial fills when demonstrated by the first real examples.

**Checkpoint:** save and edit a few actual trades; restart the app and confirm persistence. Verify known P/L examples and ensure the entry form is comfortable. This is the first useful personal version.

### Stage 2 — One real CSV import

Inspect a representative export and implement only that format. Add preview, validation, deduplication, and the reconstruction cases present in the records. Flag unsupported cases clearly. Preserve imported evidence and manual corrections.

**Checkpoint:** import a real period, reconcile its supported records, and repeat the import without duplication or lost notes. Add broader import support only when another format is needed.

### Stage 3 — Graphing and ranged analysis

Start with cumulative net P/L and daily P/L. Then add instrument/setup breakdowns, expectancy, distribution, dollar drawdown, optional R, and export according to usefulness.

**Checkpoint:** change the date range and filters; verify that table and chart totals agree. Review chart usefulness before adding more metrics.

**Stages 1–3 complete the initial journal. Each intermediate stage remains usable.**

### Stage 4 — Schwab connection and one live symbol

Check actual capabilities as part of this stage. Implement OAuth and display one supported symbol's quote, timestamp, connection status, and freshness. Verify equities and the requested futures contracts separately before widening the watchlist.

**Checkpoint:** the connection and reauthorization work with the user's app. Unsupported capabilities are documented; journal development does not depend on them.

### Stage 5 — Watchlist and price charts

Expand to a small watchlist, one-minute bars, selected-period price changes, and one chart. Add backfill, streaming/polling, and reconnect behavior according to verified support.

**Checkpoint:** monitor a real session, inspect freshness and gaps, and verify bar storage avoids duplicates. Refine symbols, chart behavior, and retention based on actual use.

### Stage 6 — One on-demand AI analysis

Implement one provider and an Analyze button for a selected symbol. Send a compact snapshot, validate the response, and store the request/response with timestamps. Add signal history and a basic subsequent-outcome view once the analysis presentation is useful.

**Checkpoint:** inspect what the AI receives and whether its output is helpful. Change features, prompts, or presentation before adding scheduling or alerts.

**Stages 4–6 complete the initial market monitor with an AI hook.**

### Later additions — Chosen from actual use

Consider broker trade synchronization, trade/chart overlays, approximate MFE/MAE, scheduled AI analysis, alerts, and weekly summaries individually. Full replay, counterfactual execution simulation, continuous futures construction, and automated trading remain outside the initial plan.

## 7. Reliability and validation

Keep verification proportional to this personal tool: a small accounting fixture set, a few meaningful integration checks, and hands-on review of each stage. Add backup/restore alongside real persisted data and token protection alongside OAuth. Avoid production infrastructure, multi-user permissions, deployment pipelines, monitoring services, and broad test suites unless a later need warrants them.

- Bind the application locally by default; protect OAuth flow and redact credentials from logs.
- Provide database backup and verified restore, including journal data and settings; document reauthorization requirements on another machine.
- Version schema migrations and derived calculations.
- Make imports transactional and record background-job status/errors.
- Verify accounting with representative fixtures and reconciliation, not dashboard appearance alone.
- Test that filtered summaries match exported records and that timezone boundaries behave consistently.
- Verify stream reconnects, bar deduplication, and AI response validation at their milestones.
- Use one-minute data only for metrics it can support; mark partial-bar excursion estimates approximate. Sub-minute entry analysis requires a separate data-resolution decision.

## 8. Immediate next implementation task

Stage 0 app shell is implemented: Journal and Market tabs, synthetic trades/prices, date/instrument/direction/setup filters, P/L summaries, equity curve, setup breakdown, and trade details. Accounting/filter fixtures pass and HTTP assets have been checked. Automated visual review was blocked by the in-app browser URL policy; hands-on layout review remains the checkpoint.

Stage 1 is implemented: saved manual single-entry/single-exit trades, editing, notes, optional risk, instrument presets with editable futures point value, decimal server P/L, separate sample mode, and SQLite backup. Entry/exit fills are persisted transactionally. Accounting, validation, database reopening, backup restore, and HTTP create/edit/list checks pass. The journal currently assumes USD, one account, and user-entered Eastern local timestamps; partial exits and import timestamp normalization are deferred to Stage 2. Automated visual review remains blocked by the browser URL policy.

Stage 2 import foundation is implemented: execution CSV mapping/preview, flat-to-flat reconstruction with partial exits and reversal allocation, duplicate/conflict detection, atomic commit, original-source preservation, pre-import/schema backups, account filtering, and allocated-fill details. Imported accounting remains read-only while journal metadata is editable. Fixture and isolated HTTP checks pass. No representative Schwab/thinkorswim export has been supplied yet, so broker-specific format validation and reconciliation remain the Stage 2 checkpoint.

The thinkorswim statement adapter reconciles executions, closed trades, fees, holdings, and the settlement ledger. It preserves split fills and stores open inventory separately.

Stage 3 is implemented: server-calculated filtered summaries, daily net P/L, instrument/setup breakdowns, fixed-dollar trade distribution, profit factor, average winner/loser, current and maximum dollar drawdown, mean/median R and recorded-risk coverage. Reusable date presets use Eastern calendar dates, with sample dates anchored to the latest synthetic trade. Filtered CSV export shares the same server filter and decimal calculations. Date boundaries, missing risk, empty populations, distribution boundaries, export totals, and statement report totals pass isolated checks. Automated visual review remains blocked by the browser URL policy; hands-on report layout review is the checkpoint.

The user reviewed Stage 3 and accepted its layout. Stage 4 connection foundation is implemented: protected local app settings, state-checked OAuth with a manual HTTPS loopback callback handoff, automatic access-token refresh on quote requests, local disconnect, and one on-demand quote with timestamps and realtime/delayed status. Credentials use Windows DPAPI-backed Data Protection separately from journal data and backups. Simulated OAuth/refresh/quote tests and isolated HTTP settings/state/origin checks pass without Schwab network calls. No order endpoints are implemented.

The user's Schwab app is awaiting approval. Live authorization, Market Data access, equity quotes and dated futures quotes remain unverified. Official portal documentation was not fully readable without interactive access; implementation was cross-checked against published client source. This stage is not complete until actual capability checks succeed.

Next: after approval, authorize and fetch one equity and one dated futures quote. Then build the Stage 5 watchlist and stored price history according to verified capabilities. The journal remains usable independently.

The user requested proceeding with AI while Schwab approval is pending and explicitly chose ChatGPT subscription access rather than API billing. An independent AI pilot is implemented using noninteractive Codex CLI: filtered journal review and synthetic market-bar analysis, local preview before transmission, structured output validation, cancellation/timeout, and saved input/result/model/prompt history. Notes are opt-in; credentials and account labels are excluded. CLI authentication is forced to ChatGPT, without an API-key fallback. Fixture checks and a real synthetic-data CLI run passed. Live market analysis still needs actual historical bars; price history/watchlist work and Schwab verification remain incomplete.

Schwab app authorization is now working. Stage 5 adds a persisted watchlist, deduplicated quote observations, upserted one-minute historical bars, rolling range charts and optional polling while the visible Market tab is open. Real requests succeeded for AAPL and `/MESZ26`, including historical bars. The AI pilot now accepts selected saved market data with provenance, range and observation age; previews use the latest 180 observations and require at least five. Accounting/storage/chart fixtures pass, including unfinished bars, historical gaps and atomic invalid-batch rejection. Journal totals are preserved. Watchlist/history live in a separate market database with manual backup; streaming, unattended collection, retention controls and subsequent-outcome evaluation remain later checkpoints.

Saved-market analysis follow-up is now implemented. The reference price must occur after the AI result became available, avoiding retrospective price movement being treated as an actionable outcome. Bounded 5/15/30-minute reports show actual timestamps, changes, observed extrema and coverage; pending/missing data stays explicit. Original AI input/result stays immutable. Updating prices calls Schwab only, with no extra AI invocation. Fixtures verify future-data exclusion, horizon gaps, missing references, sparse quotes and stored historical windows. This is price follow-up, not a trading-strategy backtest. Next: use a real market analysis, inspect follow-up usefulness, then choose whether structured candidate signals or unattended collection adds value.

Estimate implementation effort after reviewing the sample export and Schwab capability results. Those dependencies determine more of the work than the choice of chart library.
