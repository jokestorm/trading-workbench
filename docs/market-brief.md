# Living market brief

The market page shows one current brief covering financial markets and
commodities generally. It is independent of chart selection, positions and
journal data. It uses the existing subscription-backed Codex connection with
live web research and a separate `market-brief-v1` prompt.

## Schedule and lifecycle

The server checks every 30 seconds for the current eligible hourly slot:
weekdays at 08:00 through 15:00 America/New_York, including DST. A startup
within that window performs one current-hour catch-up, without replaying
missed hours. This is a weekday schedule, not an exchange holiday calendar.

Each admitted check persists its slot before calling Codex. Completed or
failed attempts are not automatically repeated in the same hour. A restart
during research marks the attempt interrupted and preserves the previous
brief. A new hourly slot permits another attempt.

Manual analysis takes priority. Existing manual jobs defer admission; a manual
provider request cancels active brief research and takes the shared operation
lock. Deferred/preempted briefs may retry within the current hour. Sign-in
and inference cannot run concurrently. Research has a 120-second execution
limit and a prompt budget of 60 seconds, four searches and three page opens.

## Content and storage

The prompt receives only the previous brief, its successful-check time and
the current Eastern timestamp. It preserves useful wording, revises changed
facts, removes stale items and adds material news. The visible result is an
outlook, 3–5 short bullets and the next significant event. The model is asked
to verify current news and fail rather than fabricate a fresh check.

`<DataDirectory>/market-brief/current.json` atomically stores content and
status. Last checked advances on successful research; last changed advances
only when content differs. Changed snapshots are retained as `revision-*.json`.
Transient provider files under `work/` are removed after each check. Failed
checks retain prior content and successful timestamps. Home Assistant logs
show the hourly slot, stage and safe failure type, without raw model output.

`GET /api/ai/market-brief` returns saved state through existing ingress access
checks and no-store caching. Visible pages poll it every 30 seconds; reads
never launch inference. Closing the page does not affect server scheduling.

Run backend `--self-test` and `node --test tests/*.test.mjs` for synthetic
scheduling, persistence, interruption, provider priority and rendering checks.
These do not contact live news sources or consume subscription inference.
