# Trading Workbench

Stage 5: journal, Schwab connection, a saved watchlist, quote snapshots and historical price charts. Live quote and one-minute history requests have succeeded for AAPL and `/MESZ26`. The supplied October 6 statement has been validated and reconciled in isolated databases.

## Run

Requires .NET SDK 10. The first run restores the Microsoft.Data.Sqlite dependency from NuGet.

```powershell
.\start.ps1
```

Open http://127.0.0.1:5173 in your browser. Stop the foreground server with Ctrl+C. If a background instance is already running, stop that instance before starting another on the same port.

Alternatively: `dotnet run --project src/TradingWorkbench --no-launch-profile`.

## Verify

```powershell
dotnet build src/TradingWorkbench
dotnet src/TradingWorkbench/bin/Debug/net10.0/TradingWorkbench.dll --self-test
node --test tests/analytics.test.mjs
```

## Current scope

- My trades: add and edit completed single-entry/single-exit trades, including notes, optional planned risk, and total fees.
- Inclusive closed-date, account, instrument, direction, and setup filtering. Today, yesterday, Monday-based week, month-to-date and last 30 days use Eastern dates.
- Net P/L, win rate, expectancy, equity curve, maximum/current dollar drawdown, daily P/L, instrument/setup breakdowns, fixed-dollar P/L distribution, and trade details.
- Profit factor, average winner/loser and mean/median R with visible recorded-risk coverage. Break-even trades count toward win rate and expectancy. Missing risk is excluded from R statistics; no losses with positive wins displays infinite profit factor.
- Export filtered closed trades to CSV with authoritative gross/net P/L, fees, risk, R and notes. Numeric columns retain their values; text resembling spreadsheet formulas is escaped. The CSV is a report, not an execution-import format.
- Decimal accounting on the .NET backend; saved-trade P/L is authoritative.
- Responsive layout with no remote fonts or frontend dependencies.
- A downloadable SQLite backup.
- Execution CSV import with explicit column mapping, read-only preview, validation, duplicate detection, and atomic commit.
- Flat-to-flat grouping for long/short trades, scaling, partial exits, and reversals with proportional fee allocation.
- Account filter and allocated-fill detail view. Imported accounting is read-only; notes, setup, and planned risk remain editable.

No broker trade synchronization yet. On-demand AI review uses Codex CLI with ChatGPT login. Schwab quotes and price history require successful authorization. This stage assumes USD. Manual dates/times are Eastern local time; imported executions retain seconds when supplied. Point values are configurable on each manual trade. Imported dated contracts recognized by the catalog must match its type and multiplier. Manual fees are the total round-trip amount; CSV fees are per execution. Generic CSV imports still require completed flat-to-flat positions. Recognized statements can save separate open snapshots when their holdings and cash ledger reconcile.

## Watchlist and price history

The current background instance runs at **http://127.0.0.1:5175/** because another app occupies 5173. In **Market monitor**, save up to 10 comma-separated symbols. AAPL and `/MESZ26` are the initial defaults. **Refresh quotes** captures quotes into local `data/market.db`. Identical source quotes are deduplicated, while quote timestamps, retrieval times and realtime/delayed flags remain available.

Select a symbol, rolling 24-hour/3-day/7-day range and chart source. **Update prices** requests one-minute OHLCV history including extended hours. Only bars whose full minute has elapsed are saved. Repeated loads update overlapping timestamps. Historical chart lines break across missing minutes; sampled-quote charts connect irregular observations and do not imply observed OHLCV or volume.

Optional 30-second polling runs only while the page is visible and Market monitor is selected. Closing the browser or leaving this tab stops collection. No streaming/background collector is implemented. Unsupported symbols/history requests display errors without removing saved data; futures support is verified for `/MESZ26`, not every possible contract.

In **Market monitor**, choose an instrument and click **Analyze**. That one action refreshes its Schwab data, prepares a frozen snapshot locally, and sends it through your ChatGPT subscription. The latest 180 bars or priced quote samples in the selected range are included (at least five required), with provenance, timestamps and observation age. Results stay on the market page; no prompt editing or review step is required.

Market storage is independent of journal accounting. Keep a copy of `data/market.db` while the app is stopped to retain the watchlist/history; the downloadable journal backup does not include this database. No automatic retention/deletion policy is applied yet.

## Analysis follow-up

The latest market result appears in **Market analysis**, with **Price follow-up** directly below it. Open **Previous analyses** to select an older result. Journal analyses do not receive market outcomes. **Update follow-up** fetches Schwab data for the original symbol and reevaluates without another AI call.

The reference is the first observed price at/after analysis completion within two minutes, so a stale snapshot or price move before the result existed does not become an outcome. Horizons are 5, 15 and 30 minutes after that reference. Each uses the first saved observation at/after the target within two minutes, with the actual timestamp shown. Missing prices are never interpolated or replaced by distant observations.

Reports show price/percentage change, observed low/high and coverage. Future windows remain pending; absent observations are marked missing data. Historical minute bars can show complete or incomplete coverage; sampled quotes always have partial path coverage. These observations exclude execution, fees and position sizing, and do not establish strategy profitability. Old saved market observations remain queryable for a bounded follow-up window even after the dashboard's rolling range advances.

## AI pilot using your ChatGPT subscription

Market analysis stays in **Market monitor**. For journal analysis, expand **AI trade analysis** below Trade history and click **Analyze trades**. Both buttons prepare and send their data in one action. Journal notes are excluded unless selected; optional notes are limited to their first 500 characters. Account labels and broker credentials are excluded. Journal analyses are limited to 250 trades.

Codex must be on PATH and logged in with ChatGPT (`codex login`). Check login in the panel. The application forces ChatGPT authentication, removes the API-key environment variable from its child process, and never implements an API-key fallback. Analysis consumes your subscription allowance and can encounter its limits. See [official authentication documentation](https://learn.chatgpt.com/docs/auth).

Each run uses an isolated working folder, an ephemeral `codex exec` session, a read-only sandbox, disabled shell/web search, and a structured result schema. User configuration and execution rules are ignored for analysis runs so user-configured hooks/MCP servers are not loaded. The CLI default model is used; the model reported in CLI output is saved when available. The provider interface remains replaceable without changing journal accounting.

The application validates output, saves input/result/model/prompt version, and shows the newest 50 results in **Previous analyses**. Saved analyses live in `data/analysis/*.json`; retain that folder separately because the SQLite journal backup does not include it. Cancel terminates the CLI process tree when the request is canceled. Four minutes is the run limit; failed/canceled runs consume their preview and can be retried with the Analyze button.

Synthetic market analysis is for testing the workflow, not a live signal. Journal statements lack the market path needed to assess entry quality or reconstruct signals. Saved Schwab bars and quote samples now support market analysis; outcomes/replay remain future steps. Fixture checks and one actual ChatGPT/Codex run on synthetic bars passed; no personal trades were transmitted during testing.

## Schwab connection

In **Market monitor**, enter the app key, app secret and exact registered HTTPS loopback callback URL from the [Schwab Developer Portal](https://developer.schwab.com/). Keep credentials in the app's local fields, not chat. Save settings now; authorize after app approval and Market Data access are available.

1. Click **Start authorization**, then the generated Schwab link. Sign in directly with Schwab.
2. After consent, copy the complete callback URL from the address bar. This stage does not run an HTTPS callback listener, so the callback page may fail to load; the address still contains the returned code and state.
3. Paste that URL into the app and click **Finish authorization** within 10 minutes. Restarting the app invalidates an unfinished authorization attempt.
4. Fetch **AAPL** first. Then try a dated futures symbol, such as `/MESZ26`, to verify that specific symbol and entitlement. Live quotes and historical bars have succeeded for `/MESZ26`.

The quote shows the returned realtime/delayed flag, exchange quote timestamp, retrieval time and age when fetched. Missing realtime status remains unknown. Quotes are on demand, watchlist quote snapshots and historical bars are retained separately; streaming is not implemented. Token refresh happens when a quote needs it; rejected authorization prompts another login.

Credentials and tokens are protected with ASP.NET Data Protection; its keys use current-user Windows DPAPI on Windows. They live separately under `data/secrets`, are never returned by status APIs, and are **not included in journal database backups**. Reauthorize on another Windows account or machine. **Disconnect** clears local tokens and retains app settings; it does not revoke consent at Schwab. Changing credentials or callback clears the old authorization. No trading/order APIs are implemented.

The implementation was checked against the [schwab-py OAuth client source](https://github.com/alexgolec/schwab-py/blob/master/schwab/auth.py) and [quote client source](https://github.com/alexgolec/schwab-py/blob/master/schwab/client/base.py). Schwab's official [OAuth documentation](https://developer.schwab.com/products/trader-api--individual/details/documentation/Retail%20Trader%20API%20Production) and [Market Data specifications](https://developer.schwab.com/products/trader-api--individual/details/specifications/Market%20Data%20Production) require interactive portal access and could not be fully read here. Successful live authorization and quote checks remain the Stage 4 checkpoint.

## thinkorswim account statements

Choose the original statement in **Import CSV**. It is detected automatically, so manual column mapping is unnecessary. Use a stable account label and the correct execution timezone (Eastern / EDT for the supplied statement). Preview displays completed trades, both fee categories, open snapshots, and settlement/cash reconciliation before saving.

- Actual fills come from **Futures Statements**, matched individually to **Account Trade History**, including identical split fills sharing an order reference.
- Execution date is used instead of futures trade date. Eastern and Central execution timezones are supported; invalid/ambiguous DST timestamps are rejected.
- Canceled/rejected orders, requested order prices, transfers, YTD totals and equity holdings do not become completed trades.
- Separate open snapshots are excluded from closed-trade metrics and are not live positions. Their display remains the latest imported statement snapshot when date filters change.
- A statement with unknown starting holdings, unmatched executions, unsupported instruments, a holdings discrepancy, or a cash reconciliation discrepancy cannot be committed.
- Open positions with prior partial exits are not supported yet; the importer blocks those cases.
- Later statements must include consistent overlapping opening history for saved open positions. They cannot silently replace that history.

The statement adapter reconciles execution fees, open inventory, and the settlement ledger. Schema migrations create backups before changing journal storage.

## CSV import

In **My trades**, choose **Import CSV**, select a file, check its column mapping and defaults, then **Preview import**. Review calculated trades, warnings, errors and duplicates before committing. All errors must be resolved; no partial batch is saved.

- Generic files: UTF-8 comma-separated CSV, first row headers, one account per file, at most 2 MB / 5,000 executions. Recognized statements have their sections extracted automatically.
- Required columns: symbol, buy/sell side, positive quantity, price, and full execution timestamp.
- Optional columns: execution ID, total fees, asset type, point value. An unmapped fee column means zero fees and generates a warning.
- Timestamps: `yyyy-MM-dd HH:mm[:ss]`, ISO local `yyyy-MM-ddTHH:mm[:ss]`, or `M/d/yyyy H:mm[:ss]`, all in Eastern Time.
- Sides: BUY/SELL, BOT/SOLD, or BOUGHT/SOLD. Account balance movements, dividends, orders, options, and date-only transactions are rejected rather than interpreted as executions.
- The file must include complete flat-to-flat history. Unclosed positions block the batch. A sell-first record is treated as a short entry only after the user confirms starting flat.
- Same-timestamp executions use CSV row order; mixed-side ties generate a warning.
- Mixed instruments need correct per-row type/point values. Include all commissions/charges in one positive fee column.
- Stable execution IDs are preferred. Without them, identity uses normalized execution values; identical fills need distinct IDs. Changes to prior sources or overlapping regroupings require reconciliation and are not silently overwritten.
- Manual trades are not matched automatically to CSV executions. Potential same-day overlap is flagged.

The importer stores the original CSV and mapping, allocated execution records, and a backup before every nonempty import. Schema 1 databases also get a backup before migration to schema 2. Imported IDs remain stable; repeated imports preserve journal metadata.

Synthetic CSV fixtures are kept under `tests/fixtures/` for isolated import checks; they are not served or offered in the app.

Saved data lives in `data/trading.db`, excluded from source control. Each manual trade stores entry and exit fills in the same transaction. UI rendering, analytics, and backend persistence are separate modules; the simple frontend remains in place.

Use **Download backup** in My trades to save a consistent database copy. To restore: stop the application, keep a copy of the existing `data/trading.db`, replace it with the downloaded backup named `trading.db`, and restart. Restore was checked against an isolated fixture database.

`server.mjs` is retained only for the old static preview (`node server.mjs`); it does not support saved-trade APIs. Use the .NET host for normal operation.

See [the staged plan](docs/implementation-plan.md). Next checkpoint: review saved market charts and AI analysis on actual bars. Then add outcome evaluation or unattended collection according to usefulness.

The default watchlist is /MES, /MGC and /MCL. Root symbols resolve to dated contracts using Schwab active-contract metadata when available; otherwise the highest-volume quoted, unexpired contract among nearby quarterly MES, primary even-month MGC, or monthly MCL candidates is selected. Resolutions are cached for one hour, checked on price updates, and rechecked hourly while Market monitor is visible. The selected dated symbol is shown explicitly; bars, quotes, analysis snapshots and follow-ups remain keyed to that dated contract. Dated symbols in watchlist options stay pinned. MES/MGC/MCL without a leading slash are accepted as auto roots. The chart opens at four hours of saved observations, with zoom buttons, a full-range reset, larger axes and a cursor readout that selects actual observations without interpolation.

## Position-aware day-trading recommendations

Market analysis now includes a manual position selector for the selected dated contract. Choose Flat, Long or Short; Long/Short requires current contract quantity and average entry. Unknown is the default, and positions are not inferred from an old statement or a missing broker response. Inputs are retained only in the page's memory per dated symbol and attached to the saved analysis snapshot as manual, not broker-verified data.

Analyze refreshes minute bars and a quote, then requests BUY (open long), SELL SHORT, SELL TO CLOSE a long, BUY TO COVER a short, HOLD, or WAIT. Entry/stop/target levels accompany opening recommendations. Stops and targets must follow the action's direction and instrument tick size. Adding to or reversing an existing position is not recommended in this first version. No orders are sent. Entry-to-stop dollars per contract exclude fees and slippage.

The app checks data freshness again when the AI finishes. Unknown position, quote-only history, data older than two minutes, delayed/unknown realtime status, incompatible position/actions, or invalid opening levels result in WAIT with an explicit reason. Results save the exact input position and market observations used. Older analyses retain their original format. The current Schwab integration remains market data only; futures position availability through Accounts and Trading has not been verified from public official documentation.
