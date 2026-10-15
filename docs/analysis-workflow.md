# Shared background analysis workflow

## Agreed behavior

- Market and journal analysis use one run lifecycle, with distinct preparation.
- One run is active across both workspaces. Another Analyze request returns the
  active run; it neither queues nor starts another model call.
- Runs continue after page closure or network disconnection. Reopening observes
  current progress and retrieves completed results without resubmitting work.
- Explicit Cancel stops unfinished work and prevents its result from being saved.
- On app restart, unfinished runs become interrupted. Completed results remain;
  retry requires a new explicit request and fresh preparation.
- Market inputs retain the requested contract and manual position. Refresh must
  succeed before freezing observations. Journal filters and optional notes are
  resolved at admission, preserving the original closed-trade population.
- Existing single-use snapshots, four-minute analysis timeout, freshness and
  position guards, structured results, history, and journal accounting remain.

## Ownership

`AnalysisWorkflow` owns admission, background lifetime, cancellation, durable
status, and the result commit. `AnalysisService` prepares frozen snapshots and
executes validated analysis through the existing provider seam. The browser
starts runs and observes status; it does not coordinate market refresh or tokens.

Run status lives under `data/analysis/jobs/`; existing result files remain under
`data/analysis/`. Status includes identifiers, kind, symbol, phase, and timestamps,
not credentials or journal notes. A result saved immediately before a crash is
recognized during recovery. No unfinished work is automatically resubmitted.

## Verification

Test through the workflow and browser observer interfaces: journal freezing,
refresh-to-guarded-result, refresh failure, overlapping requests, cancellation,
disconnect/reconnect, and actual process restart. Use synthetic prices and a
controlled provider; routine checks require no broker credentials or inference.
