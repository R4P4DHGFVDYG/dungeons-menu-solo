# Builds the mod and runs the rule checks. Needs the .NET 10 SDK on PATH and NuGet access for NeoRuneExtended.Sdk 1.0.0.
# The build only writes dungeons-menu/bin/NeoRune/Pak; it never copies anything into the game.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build (Join-Path $root 'dungeons-menu\DungeonsMenu.csproj') -c Release -p:NeoRuneInstall=false --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project (Join-Path $root 'validation-tests\ValidationTests.csproj') -c Release
exit $LASTEXITCODE
