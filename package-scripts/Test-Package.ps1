$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'DungeonsMenu.Package.psm1') -Force
$module = Get-Module DungeonsMenu.Package
$script:Passed = 0
$script:FixtureId = 0
$testRoot = Join-Path $PSScriptRoot ('test-runs/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null

function Assert([bool]$Value, [string]$Message) {
    if (-not $Value) { throw "FALHOU: $Message" }
    $script:Passed++
}
function Assert-Throws([scriptblock]$Action, [string]$Contains) {
    $failed = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike ('*' + $Contains + '*')) { throw }
        $failed = $true
    }
    Assert $failed "Era esperado erro contendo '$Contains'"
}
function Put([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory((Split-Path $Path -Parent)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}
function Hash([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash([IO.File]::ReadAllBytes($Path)))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function Json([object]$Value, [string]$Path) { Put $Path ($Value | ConvertTo-Json -Depth 10) }
function Fixture {
    $script:FixtureId++
    $root = Join-Path $testRoot ('case' + $script:FixtureId)
    $package = Join-Path $root 'package'
    $game = Join-Path $root 'steamapps/common/Game'
    $saved = Join-Path $root 'Saved'
    $steam = Join-Path $root 'steam.exe'
    $exe = Join-Path $game 'Dungeons/Binaries/Win64/Dungeons-Win64-Shipping.exe'
    Put $exe 'fixture-executable'
    Put (Join-Path $game 'MicrosoftGame.config') '<Game><Identity Name="Microsoft.MinecraftDungeons2" Version="1.1.3.0" /></Game>'
    Put (Join-Path $root 'steamapps/appmanifest_1912410.acf') '"appid" "1912410" "buildid" "25910487"'
    Put (Join-Path $saved 'SaveGames/hero.dat') 'fixture-hero'
    Put (Join-Path $saved 'Config/Windows/Game.ini') 'fixture-settings'
    Put (Join-Path $saved 'SaveGames_BACKUP/hero.dat') 'fixture-old-hero'
    Put $steam 'fixture-steam-never-executed'
    $files = @()
    foreach ($name in (& $module { $script:Names })) {
        $file = Join-Path $package ('payload/' + $name)
        Put $file ('fixture-' + $name)
        $files += [pscustomobject]@{ name = $name; sha256 = (Hash $file) }
    }
    Json ([ordered]@{ schemaVersion = 1; packageVersion = 'test'; files = $files }) (Join-Path $package 'manifest.json')
    & $module { param($ExpectedHash) $script:ExecutableHash = $ExpectedHash; $script:GameRunning = $false; $script:Launches = @() } (Hash $exe)
    return [pscustomobject]@{ root = $root; package = $package; game = $game; saved = $saved; steam = $steam; exe = $exe; mods = (Join-Path $game 'Dungeons/Content/Paks/~mods') }
}

# Only this isolated test module instance replaces process checks and launch.
& $module {
    function script:Assert-DmGameClosed { if ($script:GameRunning) { throw 'Feche Minecraft Dungeons II antes de continuar.' } }
    function script:Start-Process { param($FilePath, $ArgumentList, $WindowStyle) $script:Launches += [pscustomobject]@{ FilePath = $FilePath; Arguments = $ArgumentList; WindowStyle = $WindowStyle } }
}

foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in @('.ps1', '.psm1') }) {
    $parseErrors = $null
    $tokens = $null
    [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    Assert (@($parseErrors).Count -eq 0) "Sintaxe valida: $($file.Name)"
}

$f = Fixture
$plan = Install-DmPackage $f.package $f.game -DryRun
Assert ($plan.FileCount -eq 6) 'Dry run reconhece seis arquivos'
Assert (-not (Test-Path -LiteralPath $f.mods)) 'Dry run nao cria pasta do jogo'
Assert (-not (Test-Path -LiteralPath (Join-Path $f.package 'installation.json'))) 'Dry run nao grava registro'

& $module { $script:GameRunning = $true }
Assert-Throws { Install-DmPackage $f.package $f.game } 'Feche Minecraft'
& $module { $script:GameRunning = $false }
Put $f.exe 'updated-executable'
Assert-Throws { Install-DmPackage $f.package $f.game } 'Executavel desconhecido'
Assert (-not (Test-Path -LiteralPath $f.mods)) 'Versao desconhecida nao escreve'

$f = Fixture
Put (Join-Path $f.game 'MicrosoftGame.config') '<Game><Identity Name="WrongGame" Version="1.1.3.0" /></Game>'
Assert-Throws { Install-DmPackage $f.package $f.game } 'Identidade ou versao'
$f = Fixture
Put (Join-Path $f.root 'steamapps/appmanifest_1912410.acf') '"appid" "1912410" "buildid" "999"'
Assert-Throws { Install-DmPackage $f.package $f.game } 'Manifesto Steam'
$f = Fixture
Put (Join-Path $f.package 'payload/DungeonsMenu_P.pak') 'tampered'
Assert-Throws { Install-DmPackage $f.package $f.game } 'Integridade'
$f = Fixture
Put (Join-Path $f.package 'payload/Unrelated.pak') 'unexpected'
Assert-Throws { Install-DmPackage $f.package $f.game } 'somente os seis'
$f = Fixture
$manifestPath = Join-Path $f.package 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.files[0].name = '../outside.pak'
Json $manifest $manifestPath
Assert-Throws { Install-DmPackage $f.package $f.game } 'arquivo inesperado'
$f = Fixture
$manifestPath = Join-Path $f.package 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.files[0] = $manifest.files[1]
Json $manifest $manifestPath
Assert-Throws { Install-DmPackage $f.package $f.game } 'duplicado'

$f = Fixture
$conflict = Join-Path $f.mods 'DungeonsMenu_P.pak'
Put $conflict 'previous-mod'
$unrelated = Join-Path $f.mods 'OtherMod.pak'
Put $unrelated 'unrelated'
$plan = Install-DmPackage $f.package $f.game -DryRun
Assert ($plan.ConflictCount -eq 1) 'Conflito detectado antes de escrever'
$null = Install-DmPackage $f.package $f.game
Assert ((Hash $conflict) -eq (Hash (Join-Path $f.package 'payload/DungeonsMenu_P.pak'))) 'Instala hash exato'
$receipt = Get-Content -LiteralPath (Join-Path $f.package 'installation.json') -Raw | ConvertFrom-Json
Assert ((Get-Content -LiteralPath (Join-Path $f.package ($receipt.backup + '/DungeonsMenu_P.pak')) -Raw) -eq 'previous-mod') 'Conflito tem copia recuperavel'
Assert ((Get-Content -LiteralPath $unrelated -Raw) -eq 'unrelated') 'Mod nao relacionado preservado'
Assert-Throws { Install-DmPackage $f.package $f.game } 'ja tem um registro'
$removePlan = @(Remove-DmPackage $f.package $f.game -DryRun)
Assert (@($removePlan | Where-Object { $_.action -eq 'Restaurar' }).Count -eq 1) 'Dry run identifica restauracao de conflito'
Assert ((Hash $conflict) -eq (Hash (Join-Path $f.package 'payload/DungeonsMenu_P.pak'))) 'Dry run remocao nao altera'
$null = Remove-DmPackage $f.package $f.game
Assert ((Get-Content -LiteralPath $conflict -Raw) -eq 'previous-mod') 'Remocao restaura original'
Assert ((Get-Content -LiteralPath $unrelated -Raw) -eq 'unrelated') 'Remocao preserva outro mod'
Assert (-not (Test-Path -LiteralPath (Join-Path $f.mods 'DungeonsMenu_P.utoc'))) 'Remocao apaga arquivo criado intacto'
Assert (-not (Test-Path -LiteralPath (Join-Path $f.package 'installation.json'))) 'Registro concluido arquivado'

$f = Fixture
Put (Join-Path $f.mods 'BetterBlueprintLoader_P.pak') 'fixture-BetterBlueprintLoader_P.pak'
$null = Install-DmPackage $f.package $f.game
Put (Join-Path $f.mods 'DungeonsMenu_P.pak') 'user-change'
$actions = @(Remove-DmPackage $f.package $f.game)
Assert (@($actions | Where-Object { $_.action -eq 'PreservarAlterado' }).Count -eq 1) 'Remocao informa arquivo alterado'
Assert ((Get-Content -LiteralPath (Join-Path $f.mods 'DungeonsMenu_P.pak') -Raw) -eq 'user-change') 'Alteracao posterior preservada'
Assert (Test-Path -LiteralPath (Join-Path $f.mods 'BetterBlueprintLoader_P.pak')) 'Arquivo identico preexistente nao pertence ao pacote'
Assert (Test-Path -LiteralPath (Join-Path $f.package 'installation.json')) 'Registro preservado quando existe conflito'
$receiptPath = Join-Path $f.package 'installation.json'
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$receipt.backup = '../../outside'
Json $receipt $receiptPath
Assert-Throws { Remove-DmPackage $f.package $f.game } 'Registro de instalacao invalido'

$f = Fixture
$null = Install-DmPackage $f.package $f.game
$dry = Start-DmMod $f.package $f.game $f.saved $f.steam -DryRun
Assert ($dry.BackupPending -and $dry.LaunchPending) 'Inicio dry run informa efeitos pendentes'
Assert ((& $module { $script:Launches.Count }) -eq 0) 'Dry run nao inicia Steam'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $f.package 'backups') -Directory | Where-Object { $_.Name -like 'profile-*' }).Count -eq 0) 'Dry run nao copia perfil'
$result = Start-DmMod $f.package $f.game $f.saved $f.steam
Assert ($result.VerifiedFiles -eq 3) 'Backup inclui SaveGames, Config e SaveGames_BACKUP'
$backupManifest = Get-Content -LiteralPath (Join-Path $result.Backup 'backup-manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $backupManifest.files) {
    Assert ((Hash (Join-Path $result.Backup $file.path)) -eq $file.sha256) 'Arquivo de backup com hash verificado'
    Assert ((Hash (Join-Path $f.saved $file.path)) -eq $file.sha256) 'Perfil original nao alterado'
}
$launch = & $module { $script:Launches[0] }
Assert ($launch.Arguments[0] -eq '-applaunch' -and $launch.Arguments[1] -eq '1912410') 'Argumentos Steam corretos'
Assert ($launch.Arguments[2] -eq ('-DungeonsMenu.compat=' + (Hash $f.exe))) 'Compatibilidade somente por argumento de inicio'
Assert ($launch.WindowStyle -eq 'Hidden') 'Steam inicia sem janela auxiliar'
Assert ((Get-Content -LiteralPath (Join-Path $f.package 'installation.json') -Raw) -notmatch 'DungeonsMenu.compat') 'Registro nao persiste permissao'

Put (Join-Path $f.mods 'DungeonsMenu_P.ucas') 'changed-install'
Assert-Throws { Start-DmMod $f.package $f.game $f.saved $f.steam } 'Instalacao ausente ou alterada'
Assert ((& $module { $script:Launches.Count }) -eq 1) 'Falha de integridade nao inicia outra vez'
$f = Fixture
$null = Install-DmPackage $f.package $f.game
Remove-Item -LiteralPath (Join-Path $f.saved 'SaveGames/hero.dat')
Assert-Throws { Start-DmMod $f.package $f.game $f.saved $f.steam } 'Nenhum save'
Assert ((& $module { $script:Launches.Count }) -eq 0) 'Ausencia de save bloqueia inicio'
Assert-Throws { & $module { param($Root) Get-DmChild $Root '../outside' } $f.package } 'inseguro'

$f = Fixture
$null = Install-DmPackage $f.package $f.game
Put $f.exe 'updated-after-install'
Assert-Throws { Start-DmMod $f.package $f.game $f.saved $f.steam } 'Executavel desconhecido'
Assert ((& $module { $script:Launches.Count }) -eq 0) 'Atualizacao posterior a instalacao bloqueia inicio'

$f = Fixture
$null = Install-DmPackage $f.package $f.game
$originalBackup = & $module { ${function:New-DmSaveBackup} }
& $module {
    param($Original, $ChangedPath)
    $script:OriginalBackup = $Original
    $script:ChangedPath = $ChangedPath
    function script:New-DmSaveBackup([string]$PackagePath, [string]$SavedPath) {
        $result = & $script:OriginalBackup $PackagePath $SavedPath
        [IO.File]::WriteAllText($script:ChangedPath, 'changed-during-backup')
        return $result
    }
} $originalBackup (Join-Path $f.mods 'DungeonsMenu_P.ucas')
try {
    Assert-Throws { Start-DmMod $f.package $f.game $f.saved $f.steam } 'apos o backup'
    Assert ((& $module { $script:Launches.Count }) -eq 0) 'Alteracao da instalacao durante backup bloqueia inicio'
} finally { & $module { param($Original) Set-Item -LiteralPath function:script:New-DmSaveBackup -Value $Original } $originalBackup }

$f = Fixture
$conflict = Join-Path $f.mods 'DungeonsMenu_P.pak'
Put $conflict 'previous-mod'
$null = Install-DmPackage $f.package $f.game
Remove-Item -LiteralPath $conflict
$null = Remove-DmPackage $f.package $f.game
Assert ((Get-Content -LiteralPath $conflict -Raw) -eq 'previous-mod') 'Mod anterior recuperado mesmo se arquivo instalado foi apagado'

$f = Fixture
$conflict = Join-Path $f.mods 'DungeonsMenu_P.pak'
Put $conflict 'previous-mod'
$null = Install-DmPackage $f.package $f.game
$receipt = Get-Content -LiteralPath (Join-Path $f.package 'installation.json') -Raw | ConvertFrom-Json
Put (Join-Path $f.package ($receipt.backup + '/DungeonsMenu_P.pak')) 'tampered-original'
Assert-Throws { Remove-DmPackage $f.package $f.game } 'Backup do arquivo anterior'
Assert ((Hash $conflict) -eq (Hash (Join-Path $f.package 'payload/DungeonsMenu_P.pak'))) 'Backup alterado impede sobrescrita do mod'
Assert (Test-Path -LiteralPath (Join-Path $f.mods 'BetterBlueprintLoader_P.pak')) 'Preflight de remocao falha antes de remover qualquer arquivo'

$f = Fixture
$backup = & $module { param($Package, $Saved) New-DmSaveBackup $Package $Saved } $f.package $f.saved
$manifestPath = Join-Path $backup.Path 'backup-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert ($manifest.savedPath -eq [IO.Path]::GetFullPath($f.saved)) 'Backup registra destino original exato'
$hero = Join-Path $f.saved 'SaveGames/hero.dat'
$newHero = Join-Path $f.saved 'SaveGames/new-hero.dat'
Put $hero 'updated-hero'
Put $newHero 'new-hero'
$dry = Restore-DmSaveBackup $f.package $backup.Path $f.saved -DryRun
Assert ($dry.FileCount -eq 3 -and $dry.ExtraFilesPreserved) 'Dry run identifica arquivos do backup e preservacao de extras'
Assert ((Get-Content -LiteralPath $hero -Raw) -eq 'updated-hero') 'Dry run restauracao nao altera perfil'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $f.package 'backups') -Directory).Count -eq 1) 'Dry run restauracao nao cria snapshot'
$restored = Restore-DmSaveBackup $f.package $backup.Path $f.saved
Assert ((Get-Content -LiteralPath $hero -Raw) -eq 'fixture-hero') 'Restauracao recupera conteudo original'
Assert ((Get-Content -LiteralPath $newHero -Raw) -eq 'new-hero') 'Restauracao preserva personagens criados depois'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot 'SaveGames/hero.dat') -Raw) -eq 'updated-hero') 'Snapshot previo guarda conteudo sobrescrito'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot 'SaveGames/new-hero.dat') -Raw) -eq 'new-hero') 'Snapshot previo guarda personagens adicionais'
$null = Restore-DmSaveBackup $f.package $restored.PreviousSnapshot $f.saved
Assert ((Get-Content -LiteralPath $hero -Raw) -eq 'updated-hero') 'Snapshot previo permite desfazer a restauracao'

& $module { $script:GameRunning = $true }
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved } 'Feche Minecraft'
& $module { $script:GameRunning = $false }
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path (Join-Path $f.root 'AnotherSaved') } 'outra pasta de perfil'
Assert ((Get-Content -LiteralPath $hero -Raw) -eq 'updated-hero') 'Destino incorreto nao altera perfil'
Put (Join-Path $backup.Path 'SaveGames/hero.dat') 'corrupt-backup'
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved } 'Integridade do backup'
Assert ((Get-Content -LiteralPath $hero -Raw) -eq 'updated-hero') 'Backup corrompido nao altera perfil'

$f = Fixture
$backup = & $module { param($Package, $Saved) New-DmSaveBackup $Package $Saved } $f.package $f.saved
$manifestPath = Join-Path $backup.Path 'backup-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.files[0].path = 'SaveGames/../../outside'
Json $manifest $manifestPath
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved } 'inseguro'

$f = Fixture
$backup = & $module { param($Package, $Saved) New-DmSaveBackup $Package $Saved } $f.package $f.saved
$manifestPath = Join-Path $backup.Path 'backup-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.files[0] = $manifest.files[1]
Json $manifest $manifestPath
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved } 'duplicado'

$f = Fixture
$backup = & $module { param($Package, $Saved) New-DmSaveBackup $Package $Saved } $f.package $f.saved
Remove-Item -LiteralPath (Join-Path $f.saved 'SaveGames/hero.dat')
$restored = Restore-DmSaveBackup $f.package $backup.Path $f.saved
Assert ((Get-Content -LiteralPath (Join-Path $f.saved 'SaveGames/hero.dat') -Raw) -eq 'fixture-hero') 'Recupera save ausente com snapshot do restante do perfil'
Assert (Test-Path -LiteralPath (Join-Path $restored.PreviousSnapshot 'Config/Windows/Game.ini')) 'Snapshot de perfil incompleto preserva configuracao'

$characterFile = 'Characteraaaaaaaa-2222-3333-4444-555555555555.sav'
$otherCharacter = 'Characterbbbbbbbb-2222-3333-4444-555555555555.sav'
$f = Fixture
$character = Join-Path $f.saved ('SaveGames/' + $characterFile)
$other = Join-Path $f.saved ('SaveGames/' + $otherCharacter)
$global = Join-Path $f.saved 'SaveGames/GlobalSaveDataDefault.sav'
$config = Join-Path $f.saved 'Config/Windows/Game.ini'
$characterBackup = Join-Path $f.saved ('SaveGames_BACKUP/' + $characterFile)
Put $character 'character-original'
Put $other 'other-original'
Put $global 'cloud-on-original'
Put $characterBackup 'old-character-backup'
$backup = & $module { param($Package, $Saved) New-DmSaveBackup $Package $Saved } $f.package $f.saved
Put $character 'character-current'
Put $other 'other-current'
Put $global 'cloud-off-current'
Put $config 'cloud-off-config-current'
Put $characterBackup 'character-backup-current'
$extraCharacter = Join-Path $f.saved 'SaveGames/Charactercccccccc-2222-3333-4444-555555555555.sav'
Put $extraCharacter 'extra-character-current'
$dry = Restore-DmSaveBackup $f.package $backup.Path $f.saved -ArquivoPersonagem $characterFile -DryRun
Assert ($dry.Action -eq 'VerificarRestauracaoPersonagem' -and $dry.FileCount -eq 1 -and $dry.CharacterFile -eq $characterFile) 'Dry run de personagem informa acao, arquivo e quantidade'
Assert ((Get-Content -LiteralPath $character -Raw) -eq 'character-current') 'Dry run de personagem preserva save'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $f.package 'backups') -Directory).Count -eq 1) 'Dry run de personagem nao cria snapshot'
$restored = Restore-DmSaveBackup $f.package $backup.Path $f.saved -ArquivoPersonagem $characterFile
Assert ($restored.Action -eq 'PersonagemRestaurado' -and $restored.FileCount -eq 1 -and $restored.CharacterFile -eq $characterFile) 'Restauracao de personagem informa acao, arquivo e quantidade'
Assert ((Get-Content -LiteralPath $character -Raw) -eq 'character-original') 'Somente personagem solicitado recupera conteudo original'
Assert ((Get-Content -LiteralPath $other -Raw) -eq 'other-current') 'Outro personagem permanece atual'
Assert ((Get-Content -LiteralPath $global -Raw) -eq 'cloud-off-current') 'Save global preserva estado atual da nuvem'
Assert ((Get-Content -LiteralPath $config -Raw) -eq 'cloud-off-config-current') 'Configuracao permanece atual'
Assert ((Get-Content -LiteralPath $characterBackup -Raw) -eq 'character-backup-current') 'SaveGames_BACKUP permanece atual'
Assert ((Get-Content -LiteralPath $extraCharacter -Raw) -eq 'extra-character-current') 'Personagem posterior ao backup preservado'
$snapshotManifest = Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot 'backup-manifest.json') -Raw | ConvertFrom-Json
Assert (@($snapshotManifest.files).Count -eq 8) 'Snapshot anterior continua integral com oito arquivos'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot ('SaveGames/' + $characterFile)) -Raw) -eq 'character-current') 'Snapshot integral guarda personagem sobrescrito'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot ('SaveGames/' + $otherCharacter)) -Raw) -eq 'other-current') 'Snapshot integral guarda outro personagem'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot 'SaveGames/GlobalSaveDataDefault.sav') -Raw) -eq 'cloud-off-current') 'Snapshot integral guarda global atual'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot 'Config/Windows/Game.ini') -Raw) -eq 'cloud-off-config-current') 'Snapshot integral guarda configuracao atual'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot ('SaveGames_BACKUP/' + $characterFile)) -Raw) -eq 'character-backup-current') 'Snapshot integral guarda SaveGames_BACKUP atual'
Assert ((Get-Content -LiteralPath (Join-Path $restored.PreviousSnapshot 'SaveGames/Charactercccccccc-2222-3333-4444-555555555555.sav') -Raw) -eq 'extra-character-current') 'Snapshot integral guarda personagem adicional'

foreach ($invalid in @('', ('../' + $characterFile), ('SaveGames/' + $characterFile), ('C:\' + $characterFile), 'Characterinvalid.sav', 'Characteraaaaaaaa222233334444555555555555.sav', ($characterFile + ':stream'), ($characterFile + "`n"))) {
    Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved -ArquivoPersonagem $invalid } 'ArquivoPersonagem exige'
}
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved -ArquivoPersonagem 'Characterdddddddd-2222-3333-4444-555555555555.sav' } 'ausente no manifesto raiz'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $f.package 'backups') -Directory).Count -eq 2) 'Selecao invalida nao cria snapshot nem altera perfil'
Assert ((Get-Content -LiteralPath $character -Raw) -eq 'character-original') 'Selecao invalida preserva personagem'

$manifestPath = Join-Path $backup.Path 'backup-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.files += $manifest.files | Where-Object { $_.path -like ('SaveGames\' + $characterFile) } | Select-Object -First 1
Json $manifest $manifestPath
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved -ArquivoPersonagem $characterFile } 'duplicado'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $f.package 'backups') -Directory).Count -eq 2) 'Nome ambiguo rejeitado antes de snapshot'

$f = Fixture
$character = Join-Path $f.saved ('SaveGames/' + $characterFile)
Put $character 'character-original'
Put (Join-Path $f.saved ('SaveGames/nested/' + $otherCharacter)) 'nested-only'
$backup = & $module { param($Package, $Saved) New-DmSaveBackup $Package $Saved } $f.package $f.saved
Put $character 'character-current'
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved -ArquivoPersonagem $otherCharacter } 'ausente no manifesto raiz'
Put (Join-Path $backup.Path 'Config/Windows/Game.ini') 'corrupt-unselected-config'
Assert-Throws { Restore-DmSaveBackup $f.package $backup.Path $f.saved -ArquivoPersonagem $characterFile } 'Integridade do backup'
Assert ((Get-Content -LiteralPath $character -Raw) -eq 'character-current') 'Integridade global falha antes de alterar personagem selecionado'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $f.package 'backups') -Directory).Count -eq 1) 'Arquivo nao selecionado corrompido impede snapshot e restauracao'

Write-Output "PASS: $script:Passed verificacoes; somente fixtures em $testRoot; nenhum jogo iniciado ou mod instalado."
