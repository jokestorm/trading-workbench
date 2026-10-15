#!/bin/sh
set -eu
# Private persistent files: Linux data-protection keys are not Windows DPAPI keys.
umask 077
mkdir -p "$DataDirectory"
mkdir -p "$CodexHome"
chmod 700 "$DataDirectory" "$CodexHome"
exec dotnet /app/TradingWorkbench.dll
