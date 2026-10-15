# Trading Workbench

A personal trade journal with P/L charts and ranged reports, plus a futures market
workspace for Schwab data and position-aware AI analysis. Built with .NET 10,
SQLite, and vanilla JavaScript. AI uses Codex CLI with a ChatGPT subscription.

## Home Assistant target

Raspberry Pi 4 running HAOS `rpi4-64` / ARM64. The app runs in its own container
and opens through Home Assistant ingress. Start with an empty database.

The local repository includes the app source, Home Assistant store metadata,
container definition, ingress changes, tests, and a GitHub Actions pipeline.
Repository: https://github.com/jokestorm/trading-workbench
Container image: `ghcr.io/jokestorm/trading-workbench`.
No image is published yet, and Pi/container execution is not verified.

- [Staged migration plan](docs/home-assistant-plan.md)
- [Install, release, storage, and update instructions](trading_workbench/DOCS.md)
- [Desktop workflows and existing features](docs/desktop-guide.md)
- [Original implementation plan](docs/implementation-plan.md)

## Local development

```powershell
dotnet run --project src/TradingWorkbench --no-launch-profile -- --urls http://127.0.0.1:5175
node --test tests/*.test.mjs
```

If another instance is using the app binary, build checks into a separate folder:

```powershell
dotnet build src/TradingWorkbench -o data/repo-check
dotnet data/repo-check/TradingWorkbench.dll --self-test
```

## Releases

GitHub runs backend and frontend checks on development commits. Matching version
tags publish an ARM64 container. Home Assistant pulls that version from the
registry; releases do not compile on the Pi. See the installation instructions
for runner and private registry requirements.

The journal, ingress, and Schwab workflows have been verified on the Pi. Version
0.1.2 bundles Codex CLI 0.162.1 with in-app ChatGPT device sign-in. Its connection
and analysis need verification on the actual Pi after updating and signing in.
Use **Market → ChatGPT connection → Sign in with ChatGPT**, open the official
link, and enter the one-time code. No OpenAI API key is required.

Databases, keys, authentication caches, runtime files, and the personal statement
review are excluded from Git and container builds. Synthetic fixtures are tests
only. This app provides analysis and does not submit broker orders.
