[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Minecraft Dungeons II',
    [switch]$Verificar
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DungeonsMenu.Package.psm1') -Force
Remove-DmPackage -PackagePath $PSScriptRoot -GamePath $GamePath -DryRun:$Verificar
