$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try { dotnet run --project src/TradingWorkbench --no-launch-profile }
finally { Pop-Location }
