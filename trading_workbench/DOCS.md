# Installation and updates

## When the remote is available

1. Add the GitHub remote to this local Git repository. Do not put access tokens in its URL.
2. Replace `image` in `trading_workbench/config.yaml` with
   `ghcr.io/<owner>/<repository>` in lowercase. The workflow checks this against
   the actual GitHub repository before publishing a version tag.
3. Enable GitHub Actions. The workflow uses GitHub-hosted Ubuntu runners with
   QEMU for ARM64. It builds and runs container self-tests on development pushes
   and pull requests. The release job uses `GITHUB_TOKEN` with `packages: write`;
   no separate registry token is required for publication.
4. Run checks, then tag the version from `config.yaml`, for example `v0.1.0`.
   The tag pipeline publishes the matching image after the checks pass.
5. Add the GitHub repository URL in Home Assistant's app store repository menu.
   Install Trading Workbench, start it, and use Open Web UI or the sidebar.

Home Assistant needs permission to clone the repository and pull the container.
Private GitHub repository/registry access must be arranged and tested before
installation; publishing an image from CI does not grant HA access automatically.
For straightforward installation, a public repository and public GHCR package
allow anonymous downloads. GHCR packages start private by default; change the
package visibility explicitly if public downloads are intended. Do not use
GitHub workflow tokens as long-lived HA credentials.

## Data and credentials

Start with an empty journal. No old database or credentials are bundled.
Files live under `/data/workbench`, which survives app updates and restarts.
Use Home Assistant backups including this app. The journal's Download backup
button exports only the journal, not market history, settings, or AI credentials.
Uninstalling can remove app data; take a backup first.

Linux data-protection keys and Schwab settings require private filesystem access;
the startup script creates files with a restrictive umask. HA backups containing
them must also be treated as private. Windows DPAPI files cannot be reused.

## Staged availability

The initial container includes the existing application, but does not yet install
Codex CLI. The journal is the first installation test. Schwab routes remain in
the source; reconnect and verify OAuth/quotes through ingress in the next stage.
AI should report Codex unavailable until the later CLI/authentication stage.
Do not enable unattended analysis or broker order submission.

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
