# Installation and updates

## Publishing and installing

1. Push development changes to `https://github.com/jokestorm/trading-workbench`.
   Do not put access tokens in the remote URL.
2. The app image is `ghcr.io/jokestorm/trading-workbench`. The workflow checks this
   against the actual GitHub repository before publishing a version tag.
3. Enable GitHub Actions. The workflow uses GitHub-hosted Ubuntu runners with
   QEMU for ARM64. It builds and runs container self-tests on development pushes
   and pull requests. The release job uses `GITHUB_TOKEN` with `packages: write`;
   no separate registry token is required for publication.
4. Run checks, then tag the version from `config.yaml`, for example `v0.1.0`.
   The tag pipeline publishes the matching image after the checks pass.
5. Add `https://github.com/jokestorm/trading-workbench` in Home Assistant's app
   store repository menu.
   Install Trading Workbench, start it, and use Open Web UI or the sidebar.

Home Assistant needs permission to clone the repository and pull the container.
Private GitHub repository/registry access must be arranged and tested before
installation; publishing an image from CI does not grant HA access automatically.
For straightforward installation, a public repository and public GHCR package
allow anonymous downloads. GHCR packages start private by default; change the
package visibility explicitly if public downloads are intended. Do not use
GitHub workflow tokens as long-lived HA credentials.

## Statement cash settlements

The importer recognizes `cash settle future` for a single unchanged open Micro
Ether contract in its expiration month. It derives the closing price from the
preceding official mark and the reported cash adjustment, verifies the price
tick, and retains the closing fees. Preview warnings and allocated-fill labels
identify the settlement-derived exit. Ambiguous positions or missing settlement
basis remain blocked. Explicit cash sweeps stay separate from trade P/L. A change
between reported daily opening balances with no ledger event is shown as an
unitemized cash adjustment with its date and amount; its cause remains unverified.
The preview flags these adjustments for review instead of claiming a clean cash
reconciliation. Trade cashflows, fees, and holdings must still reconcile, and
transaction-row balance errors remain blocked.

Split fills may retain an order-level `TO CLOSE` label after the order reverses
through flat. Continuations require the same verified order reference, contract,
side, and source date. A ledger posting may match a Trade History execution within
one second only when contract, side, quantity, and price agree and the nearby
match is unique. Reporting uses the verified execution time; ambiguity is blocked.

## Data and credentials

Start with an empty journal. No old database or credentials are bundled.
Files live under `/data/workbench`, which survives app updates and restarts.
Use Home Assistant backups including this app. The journal's Download backup
button exports only the journal, not market history, settings, or AI credentials.
Uninstalling can remove app data; take a backup first.

Linux data-protection keys and Schwab settings require private filesystem access;
the startup script creates files with a restrictive umask. HA backups containing
them must also be treated as private. Windows DPAPI files cannot be reused.

## ChatGPT analysis (0.1.2 onward)

The container bundles Codex CLI 0.162.1 for ARM64. Open Market, expand **ChatGPT
connection** under Market analysis, and choose **Sign in with ChatGPT**. Open
the official OpenAI link on your phone or computer and enter the one-time code.
Keep the app running until it shows **ChatGPT connected**. If device-code login
is unavailable, enable it in your ChatGPT account's security settings first.

Then select an instrument, enter your current position, and choose **Analyze**.
The button sits beside the chart controls and names the selected dated contract.
The chart defaults to MES when it is available in the watchlist. Changing the
contract changes the analysis target and its visible signal history. Use
**Review portfolio** in the journal toolbar for a separate review of the filtered
closed trades; it opens the journal's Portfolio review section.
Journal analysis shares the same login. Check connection, Cancel sign-in, and
Sign out are available in the connection section. Analysis sends the selected
snapshot to OpenAI using the ChatGPT account; it does not place broker orders.

One analysis runs at a time across Market and Journal. Progress appears in both
sections. You can close the page or lose your phone connection; the run continues
and the app reconnects to its progress or saved result when reopened. Use Cancel
to stop it explicitly. Restarting the app interrupts unfinished analysis without
automatically submitting another request. Start a fresh analysis to retry.

Market analysis may perform brief live web research for relevant catalysts and
economic releases. The prompt requests at most two searches and two page opens,
targeting 20 seconds of research; these are model instructions, not separate
enforced tool limits. The entire market analysis execution has a 90-second hard
timeout. Market results display only trend, a position-aware action, a short
reason, and relevant entry/stop/target levels. Research runs quietly; references
and unavailable-research messages are omitted. Price follow-up is collapsed.
Frozen Schwab observations remain authoritative for prices and position
checks. Research cannot override stale-data WAIT results. Journal review uses a
separate prompt with web search disabled and a four-minute execution timeout.

Run status is stored under `/data/workbench/analysis/jobs`; completed results
remain in `/data/workbench/analysis`. Both survive app updates and restarts.

The app uses `/data/workbench/codex` for its own Codex credentials. This directory
survives restarts and updates and is included in app backups. Codex manages token
refresh. Its file credential cache is plaintext in private storage, so protect
HA backups as well. No credentials are embedded in the image or Git repository.
The desktop PC's login is not transferred. A pending sign-in must be restarted
if the app restarts before authorization finishes.

GitHub checks the bundled ARM64 CLI and local auth protocol without a real login
or model call. First actual ChatGPT sign-in and inference on the Pi remain
post-install checks; this is a personal app with manual analysis.

## Updating

Increment `version` in `config.yaml`, update CHANGELOG.md, and publish a matching
`vX.Y.Z` Git tag. After the image exists, refresh the repository in HA and install
the update. Version tags are immutable release artifacts: do not replace an old
image with a different build. Back up before updating; downgrade compatibility
depends on the database schema.

## Local checks

```powershell
node --test tests/*.test.mjs
dotnet build src/TradingWorkbench -o data/repo-check
dotnet data/repo-check/TradingWorkbench.dll --self-test
```

With Docker available, build from the repository root:

```sh
docker buildx build --platform linux/arm64 --load -t trading-workbench:0.1.0 .
```

Real ingress execution needs Home Assistant: the app accepts only the HA ingress
gateway in ingress mode, and the app config deliberately publishes no host port.
