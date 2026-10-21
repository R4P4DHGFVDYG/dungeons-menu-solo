Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ExecutableHash = '3a8703406fd50520f83c4f70a0212c000cb3b584ef28eb38032902230c01ebdd'
$script:Names = @('BetterBlueprintLoader_P.pak', 'BetterBlueprintLoader_P.utoc', 'BetterBlueprintLoader_P.ucas', 'DungeonsMenu_P.pak', 'DungeonsMenu_P.utoc', 'DungeonsMenu_P.ucas')

function Assert-DmPlainPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Link ou juncao recusado: $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Get-DmChild([string]$Root, [string]$Relative) {
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)' -or $Relative.Contains(':')) {
        throw 'Caminho relativo inseguro.'
    }
    $result = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $result.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Caminho fora da pasta permitida.' }
    Assert-DmPlainPath $result
    return $result
}

function Get-DmHash([string]$Path) {
    Assert-DmPlainPath $Path
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}

function Assert-DmGameClosed {
    $running = @(Get-Process | Where-Object { $_.ProcessName -match '^(Dungeons|Dungeons-Win64-Shipping)$' })
    if ($running.Count -gt 0) { throw 'Feche Minecraft Dungeons II antes de continuar. Nenhum processo sera encerrado automaticamente.' }
}

function Assert-DmGame([string]$GamePath) {
    Assert-DmPlainPath $GamePath
    $exe = Get-DmChild $GamePath 'Dungeons/Binaries/Win64/Dungeons-Win64-Shipping.exe'
    if ((Get-DmHash $exe) -ne $script:ExecutableHash) { throw 'Executavel desconhecido. Este pacote exige a versao Steam 1.1.2.0 analisada.' }
    $config = Get-DmChild $GamePath 'MicrosoftGame.config'
    $settings = New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($config, $settings)
    try {
        $xml = New-Object Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.Load($reader)
    } finally { $reader.Dispose() }
    $identity = $xml.SelectSingleNode('/Game/Identity')
    if ($null -eq $identity -or $identity.GetAttribute('Name') -ne 'Microsoft.MinecraftDungeons2' -or $identity.GetAttribute('Version') -ne '1.1.2.0') {
        throw 'Identidade ou versao de MicrosoftGame.config incompativel.'
    }
    $steamApps = Split-Path (Split-Path ([IO.Path]::GetFullPath($GamePath)) -Parent) -Parent
    $acf = Get-DmChild $steamApps 'appmanifest_1912410.acf'
    $text = Get-Content -LiteralPath $acf -Raw
    if ($text -notmatch '"appid"\s+"1912410"' -or $text -notmatch '"buildid"\s+"25754144"') {
        throw 'Manifesto Steam incompativel: exige app 1912410, build 25754144.'
    }
    return $exe
}

function Get-DmManifest([string]$PackagePath) {
    $manifest = Get-Content -LiteralPath (Get-DmChild $PackagePath 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or @($manifest.files).Count -ne $script:Names.Count) { throw 'Manifesto de pacote invalido.' }
    $seen = @{}
    foreach ($file in $manifest.files) {
        if ($script:Names -cnotcontains $file.name -or $seen.ContainsKey($file.name) -or $file.sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw 'Nome duplicado, arquivo inesperado ou SHA-256 invalido no manifesto.'
        }
        $seen[$file.name] = $true
        $source = Get-DmChild $PackagePath ('payload/' + $file.name)
        if ((Get-DmHash $source) -ne $file.sha256) { throw "Integridade do pacote falhou: $($file.name)" }
    }
    $payload = Get-DmChild $PackagePath 'payload'
    $actual = @(Get-ChildItem -LiteralPath $payload -Force)
    if ($actual.Count -ne $script:Names.Count -or @($actual | Where-Object { $_.PSIsContainer -or $script:Names -cnotcontains $_.Name }).Count) {
        throw 'A pasta payload deve conter somente os seis arquivos do manifesto.'
    }
    return $manifest
}

function Write-DmJson([object]$Value, [string]$Path) {
    Assert-DmPlainPath $Path
    $json = $Value | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

function New-DmDirectory([string]$Path) {
    Assert-DmPlainPath $Path
    [IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Copy-DmVerified([string]$Source, [string]$Destination, [string]$ExpectedHash) {
    if ((Get-DmHash $Source) -ne $ExpectedHash) { throw 'O arquivo de origem mudou durante a operacao.' }
    Assert-DmPlainPath $Destination
    New-DmDirectory (Split-Path $Destination -Parent)
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    if ((Get-DmHash $Destination) -ne $ExpectedHash) { throw 'Falha ao verificar a copia gravada.' }
}

function Get-DmSaveInventory([string]$SavedPath, [switch]$AllowIncomplete) {
    Assert-DmPlainPath $SavedPath
    if (-not (Test-Path -LiteralPath $SavedPath -PathType Container)) { throw 'Pasta Saved ausente.' }
    $result = @()
    foreach ($folder in @('SaveGames', 'Config', 'SaveGames_BACKUP')) {
        $source = Get-DmChild $SavedPath $folder
        if (-not (Test-Path -LiteralPath $source -PathType Container)) {
            if ($folder -eq 'SaveGames_BACKUP' -or $AllowIncomplete) { continue }
            throw "Pasta de perfil ausente: $folder. Inicie o jogo normalmente e crie um personagem primeiro."
        }
        foreach ($item in @(Get-ChildItem -LiteralPath $source -Recurse -Force)) {
            Assert-DmPlainPath $item.FullName
            if (-not $item.PSIsContainer) {
                $relative = $item.FullName.Substring([IO.Path]::GetFullPath($SavedPath).TrimEnd('\').Length + 1)
                $result += [pscustomobject]@{ path = $relative; sha256 = (Get-DmHash $item.FullName); bytes = $item.Length }
            }
        }
    }
    if (-not $AllowIncomplete -and @($result | Where-Object { $_.path -like 'SaveGames\*' }).Count -eq 0) { throw 'Nenhum save encontrado; backup recusado.' }
    return @($result | Sort-Object path)
}

function New-DmSaveBackup([string]$PackagePath, [string]$SavedPath, [switch]$AllowIncomplete) {
    Assert-DmGameClosed
    $savedFull = [IO.Path]::GetFullPath($SavedPath).TrimEnd('\', '/')
    $packageFull = [IO.Path]::GetFullPath($PackagePath).TrimEnd('\', '/')
    if ($packageFull -eq $savedFull -or $packageFull.StartsWith($savedFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'O pacote e seus backups devem ficar fora da pasta Saved.' }
    $before = @(Get-DmSaveInventory $SavedPath -AllowIncomplete:$AllowIncomplete)
    $name = 'backups/profile-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $backup = Get-DmChild $PackagePath $name
    New-DmDirectory $backup
    foreach ($file in $before) {
        Copy-DmVerified (Get-DmChild $SavedPath $file.path) (Get-DmChild $backup $file.path) $file.sha256
    }
    Assert-DmGameClosed
    $after = @(Get-DmSaveInventory $SavedPath -AllowIncomplete:$AllowIncomplete)
    if (($before | ConvertTo-Json -Compress) -cne ($after | ConvertTo-Json -Compress)) { throw 'Perfil mudou durante o backup. O jogo nao sera iniciado.' }
    Write-DmJson ([ordered]@{ schemaVersion = 1; gameVersion = '1.1.2.0'; savedPath = $savedFull; createdUtc = [DateTime]::UtcNow.ToString('o'); files = $before }) (Get-DmChild $backup 'backup-manifest.json')
    return [pscustomobject]@{ Path = $backup; FileCount = $before.Count }
}

function Restore-DmSaveBackup([string]$PackagePath, [string]$BackupPath, [string]$SavedPath, [switch]$DryRun, [string]$ArquivoPersonagem) {
    Assert-DmGameClosed
    $characterOnly = $PSBoundParameters.ContainsKey('ArquivoPersonagem')
    if ($characterOnly -and $ArquivoPersonagem -cnotmatch '\ACharacter[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.sav\z') {
        throw 'ArquivoPersonagem exige somente o nome Character<GUID>.sav, sem caminho.'
    }
    $savedFull = [IO.Path]::GetFullPath($SavedPath).TrimEnd('\', '/')
    $backupFull = [IO.Path]::GetFullPath($BackupPath).TrimEnd('\', '/')
    Assert-DmPlainPath $savedFull
    Assert-DmPlainPath $backupFull
    if ($backupFull -eq $savedFull -or $backupFull.StartsWith($savedFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'O backup deve ficar fora da pasta Saved.' }
    $manifest = Get-Content -LiteralPath (Get-DmChild $backupFull 'backup-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.gameVersion -ne '1.1.2.0' -or $manifest.savedPath -ne $savedFull) { throw 'Backup de outra pasta de perfil ou versao; restauracao recusada.' }
    $current = @(Get-DmSaveInventory $savedFull -AllowIncomplete)
    $seen = @{}
    $restores = @()
    foreach ($file in @($manifest.files)) {
        if ($file.path -cnotmatch '^(SaveGames|Config|SaveGames_BACKUP)[\\/].+' -or $file.sha256 -cnotmatch '^[0-9a-f]{64}$' -or ($file.bytes -isnot [int] -and $file.bytes -isnot [long]) -or $file.bytes -lt 0) { throw 'Arquivo invalido no manifesto de backup.' }
        $source = Get-DmChild $backupFull $file.path
        $target = Get-DmChild $savedFull $file.path
        if ($seen.ContainsKey($target)) { throw 'Arquivo duplicado no manifesto de backup.' }
        $seen[$target] = $true
        if ((Get-DmHash $source) -ne $file.sha256 -or (Get-Item -LiteralPath $source).Length -ne $file.bytes) { throw 'Integridade do backup falhou; nenhum perfil foi alterado.' }
        $previous = if (Test-Path -LiteralPath $target) { Get-DmHash $target } else { $null }
        $restores += [pscustomobject]@{ source = $source; target = $target; sha256 = $file.sha256; previousHash = $previous; relativePath = $file.path }
    }
    if (@($manifest.files | Where-Object { $_.path -cmatch '^SaveGames[\\/]' }).Count -eq 0) { throw 'Backup sem SaveGames; restauracao recusada.' }
    if ($characterOnly) {
        $characterPath = 'SaveGames\' + $ArquivoPersonagem
        $restores = @($restores | Where-Object { [string]::Equals($_.relativePath.Replace('/', '\'), $characterPath, [StringComparison]::OrdinalIgnoreCase) })
        if ($restores.Count -eq 0) { throw 'Personagem ausente no manifesto raiz de SaveGames; restauracao recusada.' }
        if ($restores.Count -ne 1) { throw 'Nome de personagem ambiguo no manifesto; restauracao recusada.' }
    }
    if ($DryRun) {
        $result = [ordered]@{ Action = 'VerificarRestauracao'; Destination = $savedFull; FileCount = $restores.Count; PreviousSnapshotPending = $true; ExtraFilesPreserved = $true }
        if ($characterOnly) { $result.Action = 'VerificarRestauracaoPersonagem'; $result.CharacterFile = $ArquivoPersonagem }
        return [pscustomobject]$result
    }
    $snapshot = New-DmSaveBackup $PackagePath $savedFull -AllowIncomplete
    try {
        $afterSnapshot = @(Get-DmSaveInventory $savedFull -AllowIncomplete)
        if (($current | ConvertTo-Json -Compress) -cne ($afterSnapshot | ConvertTo-Json -Compress)) { throw 'Perfil mudou antes da restauracao.' }
        foreach ($file in $restores) {
            Assert-DmGameClosed
            $now = if (Test-Path -LiteralPath $file.target) { Get-DmHash $file.target } else { $null }
            if ($now -ne $file.previousHash) { throw 'Perfil mudou durante a restauracao.' }
            Copy-DmVerified $file.source $file.target $file.sha256
        }
    } catch { throw "Restauracao interrompida. Estado anterior preservado em $($snapshot.Path). $($_.Exception.Message)" }
    $result = [ordered]@{ Action = 'PerfilRestaurado'; Destination = $savedFull; FileCount = $restores.Count; PreviousSnapshot = $snapshot.Path; ExtraFilesPreserved = $true }
    if ($characterOnly) { $result.Action = 'PersonagemRestaurado'; $result.CharacterFile = $ArquivoPersonagem }
    return [pscustomobject]$result
}

function Install-DmPackage([string]$PackagePath, [string]$GamePath, [switch]$DryRun) {
    Assert-DmGameClosed
    $null = Assert-DmGame $GamePath
    $manifest = Get-DmManifest $PackagePath
    $destination = Get-DmChild $GamePath 'Dungeons/Content/Paks/~mods'
    $receiptPath = Get-DmChild $PackagePath 'installation.json'
    if (Test-Path -LiteralPath $receiptPath) { throw 'Este pacote ja tem um registro de instalacao. Use Remover.ps1 antes de reinstalar.' }
    $backupRelative = 'backups/install-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $files = @()
    foreach ($file in $manifest.files) {
        $target = Get-DmChild $destination $file.name
        $previousHash = $null
        if (Test-Path -LiteralPath $target) {
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Destino nao e arquivo: $($file.name)" }
            $previousHash = Get-DmHash $target
        }
        $files += [pscustomobject]@{ name = $file.name; sha256 = $file.sha256; previousHash = $previousHash; changed = ($previousHash -ne $file.sha256) }
    }
    if ($DryRun) { return [pscustomobject]@{ Action = 'VerificarInstalacao'; Destination = $destination; FileCount = $files.Count; ConflictCount = @($files | Where-Object { $_.changed -and $_.previousHash }).Count } }
    New-DmDirectory $destination
    New-DmDirectory (Get-DmChild $PackagePath $backupRelative)
    foreach ($file in $files) {
        if ($file.changed -and $file.previousHash) {
            Copy-DmVerified (Get-DmChild $destination $file.name) (Get-DmChild $PackagePath ($backupRelative + '/' + $file.name)) $file.previousHash
        }
    }
    # A recovery receipt is written before the first game-file change.
    $receipt = [ordered]@{ schemaVersion = 1; gamePath = [IO.Path]::GetFullPath($GamePath); backup = $backupRelative; status = 'installing'; files = $files }
    Write-DmJson $receipt $receiptPath
    try {
        foreach ($file in $files) {
            Assert-DmGameClosed
            $target = Get-DmChild $destination $file.name
            $currentHash = if (Test-Path -LiteralPath $target -PathType Leaf) { Get-DmHash $target } else { $null }
            if ($currentHash -ne $file.previousHash) { throw 'Destino mudou durante a instalacao; operacao interrompida.' }
            if ($file.changed) { Copy-DmVerified (Get-DmChild $PackagePath ('payload/' + $file.name)) $target $file.sha256 }
        }
        $receipt.status = 'installed'
        Write-DmJson $receipt $receiptPath
    } catch {
        $receipt.status = 'incomplete'
        Write-DmJson $receipt $receiptPath
        throw "Instalacao incompleta. Use Remover.ps1 para reverter somente arquivos ainda intactos. $($_.Exception.Message)"
    }
    return [pscustomobject]@{ Action = 'Instalado'; Destination = $destination; FileCount = $files.Count }
}

function Remove-DmPackage([string]$PackagePath, [string]$GamePath, [switch]$DryRun) {
    Assert-DmGameClosed
    $receiptPath = Get-DmChild $PackagePath 'installation.json'
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.schemaVersion -ne 1 -or $receipt.gamePath -ne [IO.Path]::GetFullPath($GamePath) -or $receipt.backup -cnotmatch '^backups/install-[0-9TZ]+-[0-9a-f]{8}$') { throw 'Registro de instalacao invalido ou de outra pasta do jogo.' }
    $destination = Get-DmChild $GamePath 'Dungeons/Content/Paks/~mods'
    $seen = @{}
    if (@($receipt.files).Count -ne $script:Names.Count) { throw 'Registro de arquivos incompleto.' }
    $actions = @()
    foreach ($file in $receipt.files) {
        if ($script:Names -cnotcontains $file.name -or $seen.ContainsKey($file.name) -or $file.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $file.changed -isnot [bool]) { throw 'Arquivo invalido no registro de instalacao.' }
        $seen[$file.name] = $true
        if ($null -ne $file.previousHash -and $file.previousHash -cnotmatch '^[0-9a-f]{64}$') { throw 'Hash anterior invalido.' }
        $target = Get-DmChild $destination $file.name
        $action = 'Ausente'
        $current = $null
        if (-not $file.changed) { $action = 'Preexistente' }
        elseif (Test-Path -LiteralPath $target) {
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'Destino da remocao nao e arquivo.' }
            $current = Get-DmHash $target
            if ($current -eq $file.sha256) {
                $action = 'Remover'
                if ($file.previousHash) {
                    $original = Get-DmChild $PackagePath ($receipt.backup + '/' + $file.name)
                    if ((Get-DmHash $original) -ne $file.previousHash) { throw 'Backup do arquivo anterior ausente ou alterado. Nada sera removido.' }
                    $action = 'Restaurar'
                }
            } elseif ($file.previousHash -and $current -eq $file.previousHash) { $action = 'JaRestaurado' }
            else { $action = 'PreservarAlterado' }
        }
        elseif ($file.previousHash) {
            $original = Get-DmChild $PackagePath ($receipt.backup + '/' + $file.name)
            if ((Get-DmHash $original) -ne $file.previousHash) { throw 'Backup do arquivo anterior ausente ou alterado. Nada sera removido.' }
            $action = 'Restaurar'
        }
        $actions += [pscustomobject]@{ name = $file.name; action = $action; sha256 = $file.sha256; previousHash = $file.previousHash; expectedCurrentHash = $current }
    }
    if ($DryRun) { return $actions }
    foreach ($file in $actions) {
        if ($file.action -notin @('Remover', 'Restaurar')) { continue }
        Assert-DmGameClosed
        $target = Get-DmChild $destination $file.name
        $now = if (Test-Path -LiteralPath $target) { Get-DmHash $target } else { $null }
        if ($now -ne $file.expectedCurrentHash) { throw 'Arquivo mudou durante a remocao; preservado.' }
        if ($file.action -eq 'Restaurar') { Copy-DmVerified (Get-DmChild $PackagePath ($receipt.backup + '/' + $file.name)) $target $file.previousHash }
        else { Remove-Item -LiteralPath $target }
    }
    if (@($actions | Where-Object { $_.action -eq 'PreservarAlterado' }).Count -eq 0) {
        $archive = Get-DmChild $PackagePath ($receipt.backup + '/installation-removed.json')
        Copy-DmVerified $receiptPath $archive (Get-DmHash $receiptPath)
        Remove-Item -LiteralPath $receiptPath
    }
    return $actions
}

function Start-DmMod([string]$PackagePath, [string]$GamePath, [string]$SavedPath, [string]$SteamPath, [switch]$DryRun) {
    Assert-DmGameClosed
    $null = Assert-DmGame $GamePath
    $manifest = Get-DmManifest $PackagePath
    foreach ($file in $manifest.files) {
        $installed = Get-DmChild $GamePath ('Dungeons/Content/Paks/~mods/' + $file.name)
        if ((Get-DmHash $installed) -ne $file.sha256) { throw 'Instalacao ausente ou alterada. Verifique Instalar.ps1.' }
    }
    Assert-DmPlainPath $SteamPath
    if (-not (Test-Path -LiteralPath $SteamPath -PathType Leaf) -or [IO.Path]::GetFileName($SteamPath) -ine 'steam.exe') { throw 'Informe o caminho do executavel steam.exe.' }
    $null = Get-DmSaveInventory $SavedPath
    if ($DryRun) { return [pscustomobject]@{ Action = 'VerificarInicio'; Version = '1.1.2.0'; BackupPending = $true; LaunchPending = $true } }
    $backup = New-DmSaveBackup $PackagePath $SavedPath
    # Recheck immediately before launching; the stamp is passed only on this launch.
    Assert-DmGameClosed
    $null = Assert-DmGame $GamePath
    $manifest = Get-DmManifest $PackagePath
    foreach ($file in $manifest.files) {
        $installed = Get-DmChild $GamePath ('Dungeons/Content/Paks/~mods/' + $file.name)
        if ((Get-DmHash $installed) -ne $file.sha256) { throw 'Instalacao ausente ou alterada apos o backup. O jogo nao sera iniciado.' }
    }
    Start-Process -FilePath $SteamPath -ArgumentList @('-applaunch', '1912410', ('-DungeonsMenu.compat=' + $script:ExecutableHash)) -WindowStyle Hidden
    return [pscustomobject]@{ Action = 'InicioSolicitadoAoSteam'; Backup = $backup.Path; VerifiedFiles = $backup.FileCount }
}

Export-ModuleMember -Function Install-DmPackage, Remove-DmPackage, Start-DmMod, Restore-DmSaveBackup
