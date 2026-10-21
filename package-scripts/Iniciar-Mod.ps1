[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Minecraft Dungeons II',
    [string]$SteamPath = 'C:\Program Files (x86)\Steam\steam.exe',
    [switch]$Verificar
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DungeonsMenu.Package.psm1') -Force
$saved = Join-Path $env:LOCALAPPDATA 'Dungeons2\Saved'
Start-DmMod -PackagePath $PSScriptRoot -GamePath $GamePath -SavedPath $saved -SteamPath $SteamPath -DryRun:$Verificar
