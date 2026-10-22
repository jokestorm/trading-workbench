# Transaction-derived futures positions

The market monitor's Schwab transaction position card estimates the selected
dated contract's position from Accounts and Trading history. Journal imports
and accounting remain separate. No order endpoints are called.

## Data and calculation

The existing OAuth connection calls `GET /trader/v1/accounts/accountNumbers`,
then each linked account's `GET /trader/v1/accounts/{hash}/transactions` with
`types=TRADE` and a 59-day time window. Account numbers/hashes stay within the
transport; an opaque digest identifies each account in the report. Multiple
accounts remain separate.

Only FUTURE transfer items with stable activity identifiers, VALID status,
integer signed quantities, positive prices and OPENING/CLOSING effects enter
the calculation. Exchange suffixes are removed from symbols. Identical repeated
activity/leg identifiers deduplicate; conflicting duplicates or unresolved
statuses block the sync. A response of 3,000 or more transactions is treated as
potentially incomplete. Each sync replaces the fetched window rather than
accumulating overlapping transactions or old corrected versions.

Decimal weighted entry changes on adds; partial exits retain entry; flat resets
it. Equal-time fills group by order. Closing legs precede opening legs within
reversal orders. For simultaneous distinct orders, valid orderings must converge
on one quantity/average; ambiguity or an impossible sequence yields UNKNOWN.
Buckets with more than six distinct orders are rejected as ambiguous.

## Limits and confirmation

Every estimate assumes flat before the first available fill. Even an apparently
consistent sequence cannot prove that assumption or detect every missing trade.
A first closing fill, invalid sequence or ambiguous ordering cannot invent a
holding. No matching transaction history does not imply FLAT. Recent executions
may not yet be posted. Sync time and last-fill time are displayed separately.

The user checks the estimate against their broker, then selects **Use in analysis**.
This copies side, quantity and entry into the existing manual controls. It does
not enable automatic following: subsequent syncs or trades do not silently change
those controls. Analysis continues to use the explicit manual position. A failed
sync, stale report or UNKNOWN estimate disables Use.

## Runtime and verification

Visible market pages sync every 30 seconds, with one in-flight request per page;
hidden pages abort their requests. The server serializes syncs, throttles attempts
for 25 seconds, and bounds broker work to 15 seconds. It persists only the derived
report under `<DataDirectory>/broker-positions/positions.json`. Failed syncs retain
the previous values and successful timestamp as historical. Existing ingress
access checks and no-store headers apply to `/api/schwab/positions` and
`/api/schwab/positions/sync`.

`BrokerPositionChecks`, simulated transport checks and browser tests cover fills,
reversals, account isolation, deduplication, corrections, persistence, stale/failure
states and the explicit Use-to-analysis handoff. The parser and calculator were
also checked against live read-only transactions during development; routine
self-tests use synthetic data and require no credentials.
