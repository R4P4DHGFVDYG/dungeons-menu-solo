[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$ArquivoPersonagem,
    [switch]$Verificar
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DungeonsMenu.Package.psm1') -Force
if (-not [IO.Path]::IsPathRooted($BackupPath)) { $BackupPath = Join-Path $PSScriptRoot $BackupPath }
$saved = Join-Path $env:LOCALAPPDATA 'Dungeons2\Saved'
$options = @{ PackagePath = $PSScriptRoot; BackupPath = $BackupPath; SavedPath = $saved; DryRun = $Verificar }
if ($PSBoundParameters.ContainsKey('ArquivoPersonagem')) { $options.ArquivoPersonagem = $ArquivoPersonagem }
Restore-DmSaveBackup @options
