# Account dashboard and legacy journal sync

The account dashboard uses Schwab transaction history exclusively. Its trade
table, analytics, charts, exports and journal AI review share the same
transaction-derived closed trades. CSV imports remain in the separate archive;
they do not contribute account totals. Matching archived notes and risk values
can transfer without copying CSV accounting.

Transactions refresh automatically every 30 seconds. The dashboard shows the
available history window and synchronization time. Requests cover January 1
midnight Eastern through the current time. Archived fills survive restarts and
calendar-year changes. New contracts require no CSV anchor. Open
cycles appear separately, including remaining quantity after partial exits;
completed-trade P/L updates when the cycle becomes flat. These are trade
performance totals, not account equity or cash balances.

`AccountDashboardChecks` verifies transaction-only totals, duplicate exclusion,
annotations, open cycles, persistence, filters, exports and allocated fills. A
failed sync retains the last transaction snapshot and shows the error.

The sections below describe the older CSV-anchored journal migration endpoint,
which is separate from the account dashboard.

The journal's **Sync Schwab trades** button uses the existing combined-product
Schwab authorization to fetch futures transactions. It is explicit, with no
scheduled journal writes. CSV importing remains available during migration.

## Boundaries

`SchwabConnection.FuturesTransactions` supplies signed contract fills. Recognized
USD fee transfer-item costs are allocated across futures legs, including USD
cash debits on trade records where Schwab omits `feeType` but supplies matching
positive amount and negative cost. These unclassified charges are retained
separately on each normalized fill. Existing archived fills can acquire those
charges once without changing the original commissions or execution economics.
Absent or unsupported fee forms remain unknown. The position monitor may still display
an estimate with unknown fees, but journal accounting rejects them. Fee credits,
inline fees and corrections require reconciliation rather than zero-cost guesses.
Futures `netAmount` is not treated as trade P/L or a cash-balance reconciliation.
Expiration settlement charges remain included in trade costs even when a
broker's YTD commission counter omits them.

The adapter normalizes exchange suffixes, uses configured point values, converts
UTC execution timestamps to Eastern time, and feeds canonical execution rows
through `CsvImporter.Build`. This preserves the tested flat-to-flat accounting,
proportional reversal fees and decimal totals. Partly realized open cycles stay
pending; they do not count as completed journal trades.

## Persistence and matching

SQLite tables `broker_journal_fills`, `broker_journal_links`,
`broker_journal_verified` and `broker_journal_state` retain execution history
beyond individual API fetches. New queries must retain previously observed
overlapping fills. Changed fills or disappearing records block the update.
An account mapping change blocks sync; this initial implementation supports one
linked account mapped to the existing `Schwab futures` journal label.

Each contract needs an equivalent CSV trade before API-only trades are eligible
for import. Matching requires the same account, symbol, side, quantity, multiplier,
average entry/exit, rounded gross P/L and fees, and opening/closing times within
one second. Multiple matches or different overlapping economics block writes.
Matched CSV IDs, notes and risk stay intact. The candidate API identity links to
the existing trade; later syncs and subsequent equivalent CSV imports skip it.
Verification persists per account and contract. Unmatched contracts remain in
the archive and are reported as needing statement reconciliation.

Closed-trade additions and identity links commit atomically. Archive persistence
is separate: raw normalized fills may be saved even if no trade passes migration
checks. Deleting a linked trade does not cause repeated sync to recreate it.
Known-source changes require reconciliation rather than silently overwriting
previously saved accounting. API trade edits allow notes and risk only.

## Validation

`BrokerJournalChecks` covers fee allocation, independent P/L expectations,
partial cycles, reversals, CSV/API duplicate protection, corrections, overlap
rollback, moving windows, restart persistence, account isolation and real HTTP
JSON responses. Browser tests cover explicit sync and failure handling. Routine
checks use synthetic fixtures. Live fee mapping and statement comparisons still
need a current local authorization; first-stage verification remains mandatory.

Technical source for the fee transfer-item shape:
[Schwabdev transaction examples](https://github.com/tylerebowers/Schwabdev/blob/main/docs/pages-raw/api.md).
