# Changelog

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
