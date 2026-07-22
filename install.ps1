#Requires -Version 5.1
# One-command installer for FastExplorer - downloads the latest self-contained
# release (bundles the .NET runtime and Windows App SDK runtime, so nothing
# needs to be installed separately first), installs it, creates Start Menu /
# Desktop shortcuts, best-effort pins it to the taskbar, and launches it.
#
# Usage (run in PowerShell):
#   irm https://raw.githubusercontent.com/nienowjux-hash/FastExplorer/main/install.ps1 | iex

$ErrorActionPreference = "Stop"

Write-Host "=== Instalando FastExplorer ===" -ForegroundColor Cyan

$repo = "nienowjux-hash/FastExplorer"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\FastExplorer"
$tempZip = Join-Path $env:TEMP "FastExplorer-install.zip"

Write-Host "Buscando a versao mais recente..."
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "FastExplorer-Installer" }
$asset = $release.assets | Where-Object { $_.name -like "*win-x64*.zip" } | Select-Object -First 1
if (-not $asset) {
    throw "Nao foi encontrado um pacote de instalacao (win-x64) na ultima release do GitHub."
}

Write-Host ("Baixando {0} ({1:N1} MB)..." -f $asset.name, ($asset.size / 1MB))
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempZip -UseBasicParsing

Write-Host "Instalando em $installDir..."
if (Test-Path $installDir) {
    # Re-running this script (to update) shouldn't fail because the exe from a
    # previous install is still running and its files are locked.
    Get-Process FastExplorer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Remove-Item $installDir -Recurse -Force
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Expand-Archive -Path $tempZip -DestinationPath $installDir -Force
Remove-Item $tempZip -Force

$exePath = Join-Path $installDir "FastExplorer.exe"
if (-not (Test-Path $exePath)) {
    throw "Instalacao falhou: FastExplorer.exe nao foi encontrado apos extrair o pacote."
}

Write-Host "Criando atalhos..."
$shell = New-Object -ComObject WScript.Shell

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuDir "FastExplorer.lnk"))
$startMenuShortcut.TargetPath = $exePath
$startMenuShortcut.WorkingDirectory = $installDir
$startMenuShortcut.IconLocation = $exePath
$startMenuShortcut.Save()

$desktopShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "FastExplorer.lnk"))
$desktopShortcut.TargetPath = $exePath
$desktopShortcut.WorkingDirectory = $installDir
$desktopShortcut.IconLocation = $exePath
$desktopShortcut.Save()

# Best-effort only: Windows (especially since Win11 22H2+) has progressively
# locked down programmatic taskbar pinning to stop malware from doing exactly
# this, so this classic "invoke the shell verb" trick may silently no-op on
# newer builds. There's no supported, guaranteed-to-work API for an unpackaged
# app to pin itself - the fallback instruction below always prints regardless
# of whether this looked like it worked, since "the verb existed and ran" and
# "the pin actually appeared" aren't the same thing.
Write-Host "Tentando fixar na barra de tarefas..."
$pinned = $false
try {
    $folder = $shell.Namespace($installDir)
    $item = $folder.ParseName("FastExplorer.exe")
    $pinVerbNames = @("Fixar na barra de tarefas", "Pin to taskbar", "Pin to Taskbar")
    $verb = $item.Verbs() | Where-Object { $pinVerbNames -contains ($_.Name -replace "&", "") }
    if ($verb) {
        $verb.DoIt()
        $pinned = $true
    }
}
catch {
    # Ignored - falls through to the manual-fallback message below either way.
}

if ($pinned) {
    Write-Host "Comando de fixar enviado." -ForegroundColor Yellow
}
else {
    Write-Host "Nao foi possivel fixar automaticamente nesta versao do Windows." -ForegroundColor Yellow
}
Write-Host "Se o icone do FastExplorer nao aparecer fixado na barra de tarefas, clique com o botao direito nele (com o programa aberto) e escolha 'Fixar na barra de tarefas'." -ForegroundColor Yellow

Write-Host "Abrindo o FastExplorer..." -ForegroundColor Cyan
Start-Process -FilePath $exePath

Write-Host ""
Write-Host "=== Instalacao concluida! ===" -ForegroundColor Green
