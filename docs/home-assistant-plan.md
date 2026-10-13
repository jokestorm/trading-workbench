# Home Assistant migration plan

Target: Raspberry Pi 4, HAOS `rpi4-64`, container architecture `aarch64` / ARM64.
Start empty; no transfer of historical trades or encrypted Windows credentials.
Develop on the PC; build and release through GitHub; run on HAOS.

## Stage 1: journal and installation

- Preserve the .NET/SQLite backend and vanilla JS frontend.
- Package the app separately from HA Core. No Supervisor API, host networking,
  privileged app mode, or public host port is required.
- HA ingress authenticates users; accept requests only from its gateway.
- Resolve assets, API requests, and downloads relative to the ingress base path.
- Persist databases, analysis files, and new credentials under `/data/workbench`.
- GitHub runs backend/frontend checks and publishes a versioned ARM64 image.
- Acceptance: install, empty state, manual trade, import/dedup, reports, exports,
  restart persistence, phone navigation, and HA backup/restore.

## Stage 2: Schwab

- Enter credentials fresh on HA; verify callback paste flow from a phone/desktop.
- Preserve the registered OAuth redirect URI unless Schwab requires a change;
  the current manual paste flow does not require the app to host that redirect.
- Verify token refresh, MES/MGC/MCL contract selection, quote and history capture.
- Check Linux key persistence and protect backups; market data only.
- Acceptance: real quotes and bars after restart, no journal dependency.

## Stage 3: subscription AI

- Install a pinned ARM64 Codex CLI and verify its runtime/sandbox requirements
  under HA's default container restrictions. Do not grant broad host privileges.
- Provide a one-time headless ChatGPT device-login flow and private persistent
  Codex credentials. No API billing or credentials in Git/CI images.
- Confirm read-only, tool-disabled structured analysis, timeout/cancellation,
  position guards, and follow-up work on the Pi.
- Acceptance: actual authenticated analysis and successful token reuse after
  restart. Device login/account availability is verified on the actual host.

## Release boundaries

The remote and registry address are not set yet. The source and pipeline are
local preparation, not a published or Pi-verified release. Docker is unavailable
on the current PC; Linux/ARM64/container and HA ingress acceptance remain to run.
Keep the working Windows instance available during staged validation.

References:
- https://developers.home-assistant.io/docs/apps/repository/
- https://developers.home-assistant.io/docs/apps/publishing/
- https://developers.home-assistant.io/docs/apps/presentation/
- https://learn.chatgpt.com/docs/auth
