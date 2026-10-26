# Futures trade caller

The Market page has a server-owned, continuous futures trade caller. Select
**Start trade calls**; the trader supplies the products, dated contracts and
contract specifications. The active universe is micro-only: MES, MNQ, M2K, MYM,
MGC, MCL, M6E, MET and MBT. Full-size counterparts are excluded from discovery
and new entries, including symbols in older watchlists or transaction history.
The broader specification catalog is retained for historical data. Existing
non-micro holdings continue receiving position management until closed. One quote
batch selects the highest-volume nonexpired contract for each available product.
Discovery repeats every 15 minutes. Actual held expiries stay monitored through
rollovers. There is no manual contract-entry form.

Programmatic screening compares closed-bar signals, relative volume, volume and
executable spread. AI receives the cross-product comparison, chooses a
`preferredSymbol`, and can redirect the next analysis to another ready product.
A different product must receive its own refreshed full-chart analysis before an
entry can be published. The choice and reason appear above the calls, and the
chart follows the selected/analyzed product and its position. Optional account
equity is user-provided context; broker margin availability is not inferred.

## Calls and wakeups

The default scan interval is 15 seconds, with a 90-second AI cooldown per contract
and one shared five-minute opportunity review across flat contracts. Each review
already compares all ready products; it does not start another routine model run
for every remaining flat contract. Material signals, price displacement and
open-position reviews still have their own triggers. These are resource settings, not trading
hours. There is no weekday filter, forced break, daily loss shutdown or profit
ceiling. The configurable daily dollar target is displayed for reporting only;
it does not affect admission, exposure or model instructions.

Closed-bar EMA/MACD crosses, support/resistance breaks, a 0.75 ATR displacement
since the previous analysis, and periodic reviews can request analysis. Open
positions have priority and also trigger thesis reviews on new completed bars.
Signals are observations to assess, not a backtested entry strategy. At least
21 consecutive completed minute bars and fresh realtime futures quotes are
required for analysis admission. A failed market does not abort other contracts.

Only one model run is admitted through the existing `AnalysisWorkflow`. Busy
periods coalesce into the latest observations rather than queuing old requests.
The saved run records its monitor origin, triggering observation, specifications,
position version and scoped memory. Current calls show enter, hold, exit or wait,
with entry/stop/target when appropriate.

Trader chat has priority over background inference. A chat turn interrupts an
in-flight caller review or brief and reserves the shared model slot; price/fill
monitoring and deterministic exits continue. The caller reconsiders deferred
work after the chat finishes. Routine caller reviews use the timestamped shared
market brief instead of performing separate web research for each contract.

## Trade context

The caller stores the last three hours of position confirmations, decisions and
invalidations, bounded to 240 events overall and 16 events per model request.
Memory is specific to the exact dated contract and current position scope. It
never recursively includes earlier snapshots. A new holding can carry its prior
entry recommendation into its trade context. A repeated flat confirmation keeps
the same observation scope; a closed trade ends its holding scope.

Schwab `TRADE` transactions are the single fill source. The server syncs every ten
seconds while the caller runs. Stable transaction activity IDs prevent repeated
polls from applying a fill twice. Partial exits retain average entry, completed
exits return to flat, and reversals establish a new trade scope. Fill updates
invalidate a position recommendation still in progress and enter the AI's memory.
The shared futures position calculator remains responsible for decimal entry
accounting and simultaneous-fill reconciliation.

Discovered products initialize position estimates from the fetched transaction
window, assuming flat before that window; absence of contract fills yields a flat
estimate under that same assumption. This is not a broker holdings response.
Unreconciled histories remain unknown. Position basis is retained in the model
context, displayed as an estimate, and persisted. The position form can correct
an estimate or confirm a skipped entry; subsequent fills update it automatically.
Manually corrected positions require confirmation after restart.
With multiple linked accounts, choose the account under scan settings. The
**Transaction fills** panel displays recent fills, actual execution times and the
last successful sync separately. Transaction posting can lag thinkorswim. There is
no order-history reader or alternate fill source. Account equity, working broker
stops, affordable quantities and order submission remain outside this integration.

## Continuous management

Committed stop, target and optional time expiry are evaluated without waiting for
AI. A breach remains an exit requirement until the position is confirmed flat.
A late HOLD cannot clear it. Stops can tighten within a recorded trade; they
cannot widen, and a committed expiry cannot extend. Broker orders remain the
protection during application, polling or network outages.

Before saving a model answer, the caller rechecks its settings and position
version, current quote and entry availability. Opening calls expire after 60
seconds; other model decisions expire at the review interval. Reads also suppress
calls whose quotes are older than 30 seconds or whose entry has moved away. Entry
drift allowance is the greater of two ticks and 25% of the proposed stop distance,
and the current executable-side price must remain between stop and target.

Polling is best effort, not streaming: slow broker responses lengthen a sweep.
History refresh has an eight-second budget so a history failure can still return
the fetched quote for exit checks. Missing/outdated observations remain visible.
An exit alert is not evidence of an executed close or a guaranteed fill.

## Persistence and checks

`<DataDirectory>/continuous-monitor/current.json` atomically stores configuration,
positions, exit requirements, compact latest calls and bounded memory. Full
snapshots and model results remain in the existing analysis history. Credentials
and raw account identifiers are not included. `/api/ai/monitor` and its settings
and position routes retain the existing ingress/origin checks and no-store policy.

`<DataDirectory>/broker-executions/current.json` stores the transaction feed.
`GET /api/schwab/transactions` reads it; `POST /api/schwab/transactions/sync` refreshes
it. These routes use the same access checks and no-store policy. The monitor keeps
an execution cursor per contract and account relative to its confirmed baseline.

Run `--monitor-test` for synthetic lifecycle and workflow integration checks, or
the complete backend `--self-test`. Browser checks run through the normal Node
suite. `tests/http-monitor-smoke.mjs` targets a fresh isolated local instance;
it neither connects to Schwab nor runs inference. These checks establish software
behavior, not trading profitability.

## Contract specifications

The built-in `FuturesProducts` registry records outright tick sizes, point values
and listed month cycles; it does not contain broker margin requirements. Sources
include CME's [Micro E-mini fact card](https://www.cmegroup.com/trading/equity-index/files/cme-micro-e-mini-futures-fact-card.pdf),
[E-mini specifications](https://www.cmegroup.com/articles/files/2023/cme-group-micro-e-mini-futures-and-options-webinar-slides.pdf),
[Micro metals overview](https://www.cmegroup.com/education/lessons/micro-gold-and-micro-silver-futures-product-overview),
[Micro EUR/USD specifications](https://www.cmegroup.com/markets/fx/g10/e-micro-euro/specs),
and the corresponding CME energy, Treasury, grain and crypto product specifications.
`--universe-test` covers discovery, product calendars, partial quote batches and
expiration selection. Monitor checks cover AI product handoff and held-expiry
retention.
