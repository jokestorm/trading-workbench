# Repository Guidelines

## Project Structure & Module Organization

Trading Workbench is a personal trade journal and futures market analysis app built with .NET 10, SQLite, and vanilla JavaScript.

- `src/TradingWorkbench/`: backend routes, accounting, statement imports, Schwab integration, Codex analysis, and backend self-tests (`*Checks.cs`).
- `web/`: browser ES modules, HTML, and CSS; shared chart controls live in `chart-viewport.js`.
- `tests/`: Node tests (`*.test.mjs`) and synthetic CSV fixtures in `tests/fixtures/`.
- `trading_workbench/`: Home Assistant app metadata, changelog, and installation documentation.
- `deployment/`, `Dockerfile`, and `.github/workflows/`: container startup and ARM64 build/release automation.
- `docs/`: design plans and desktop workflow documentation. `data/` is ignored private runtime storage.

## Build, Test, and Development Commands

Run commands from the repository root. Use the .NET 10 SDK and Node.js 22 for parity with CI.

```powershell
dotnet run --project src/TradingWorkbench --no-launch-profile -- --urls http://127.0.0.1:5175
dotnet build src/TradingWorkbench -o data/repo-check
dotnet data/repo-check/TradingWorkbench.dll --self-test
node --test tests/*.test.mjs
```

These start the app, build into a separate directory, run backend checks, and run browser-module tests. The separate output directory avoids replacing binaries used by a running instance. `npm test` runs the same Node suite.

## Coding Style & Naming Conventions

Follow the surrounding module's formatting; use four-space indentation for expanded C# blocks and two spaces for JavaScript. C# uses nullable references, PascalCase types/methods, and camelCase locals. JavaScript uses ES modules and camelCase functions. Use descriptive kebab-case browser filenames. No dedicated formatter or linter is configured.

Keep journal accounting, market storage, and AI providers separate. Preserve decimal accounting and authoritative backend totals.

## Testing Guidelines

Use Node's built-in test runner and backend `*Checks.cs` self-tests. Add regression checks for changed accounting, import overlap/deduplication, authentication, or chart behavior. Use synthetic fixtures and isolated databases; routine tests must not require live credentials or paid inference. No numerical coverage threshold is configured.

## Commit & Pull Request Guidelines

History uses concise, imperative subjects, such as `Configure GitHub repository and container image`; releases use `Release 0.1.2: ...`. PRs should explain the behavior change, validation, and relevant issue links. Include screenshots for visible UI changes.

## Security & Releases

Never commit statements, databases, credentials, or Codex authentication caches. Preserve Home Assistant ingress access checks. Keep analysis separate from broker order execution.

For releases, update `trading_workbench/config.yaml` and `CHANGELOG.md`, then publish a matching `vX.Y.Z` tag. CI tests the ARM64 image before publishing to GHCR; keep released tags immutable.

## Agent skills

### Issue tracker

Use GitHub Issues for tasks and specifications. Before tracker operations,
read `docs/agents/issue-tracker.md`.

### Triage labels

Before triaging issues, read the label mapping in `docs/agents/triage-labels.md`.

### Domain docs

Use a single-context layout. Before codebase exploration, read
`docs/agents/domain.md` for context and architectural decision rules.
