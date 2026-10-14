<#
.SYNOPSIS
    Produit le paquet Microsoft Store de Hublot : .\dist\Hublot-<version>.msix

.DESCRIPTION
    1. Publie le service Hublot.exe (autonome, « trimmé ») et Hublot Core (WPF, sans installateur
       embarqué : dans le Store, installation et mises à jour sont faites par Windows) ;
    2. génère les logos du paquet (Hublot.exe --store-assets) ;
    3. écrit AppxManifest.xml à partir de store\AppxManifest.template.xml et store\identity.ini ;
    4. assemble le paquet avec makeappx.exe (SDK Windows, ou paquet NuGet Microsoft.Windows.SDK.BuildTools).

    Le paquet n'est pas signé : Partner Center le signe lors de la soumission.
    Essai local sans signature (mode développeur de Windows activé) :
        Add-AppxPackage -Register .\obj\store\layout\AppxManifest.xml

.EXAMPLE
    .\build-store.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$service = Join-Path $root 'src\Hublot\Hublot.csproj'
$core = Join-Path $root 'src\Hublot.Core\Hublot.Core.csproj'
$layout = Join-Path $root 'obj\store\layout'
$outDir = Join-Path $root $Output

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }

function Publish([string]$Project, [string[]]$Extra) {
    $arguments = @(
        'publish', $Project, '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none',
        '-o', $layout, '--nologo'
    ) + $Extra
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation de $Project (code $LASTEXITCODE)." }
}

Write-Host "1/4 Compilation..." -ForegroundColor Cyan
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
Publish $service @('-p:PublishTrimmed=true')
Publish $core @('-p:EnableCompressionInSingleFile=true')
Get-ChildItem $layout -File | Where-Object { $_.Name -notin 'Hublot.exe', 'HublotCore.exe' } | Remove-Item -Force

Write-Host "2/4 Logos du paquet..." -ForegroundColor Cyan
$exe = Join-Path $layout 'Hublot.exe'
& $exe --store-assets (Join-Path $layout 'Assets') | Out-Null
if (-not (Test-Path (Join-Path $layout 'Assets\StoreLogo.png'))) { throw "Logos non générés." }

Write-Host "3/4 Manifeste..." -ForegroundColor Cyan
$identity = @{}
foreach ($line in Get-Content (Join-Path $root 'store\identity.ini') -Encoding UTF8) {
    if ($line -match '^\s*([^#=][^=]*)=(.*)$') { $identity[$Matches[1].Trim()] = $Matches[2].Trim() }
}
$version = ((Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', '') + '.0'
$manifest = Get-Content (Join-Path $root 'store\AppxManifest.template.xml') -Raw -Encoding UTF8
$manifest = $manifest.Replace('{IdentityName}', $identity['IdentityName']).Replace('{Publisher}', $identity['Publisher'])
$manifest = $manifest.Replace('{PublisherDisplayName}', $identity['PublisherDisplayName']).Replace('{Version}', $version)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))
if ($identity['IdentityName'] -eq 'Hublot.Dev') {
    Write-Host "   Identité d'essai (store\identity.ini) : à remplacer par celle de Partner Center avant la soumission." -ForegroundColor Yellow
}

Write-Host "4/4 Paquet..." -ForegroundColor Cyan
$makeappx = @(
    Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue
    Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue
) | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) {
    Write-Host ""
    Write-Host "makeappx.exe introuvable : le contenu du paquet est prêt dans $layout" -ForegroundColor Yellow
    Write-Host "Installez le SDK Windows (winget install Microsoft.WindowsSDK.10.0.26100) ou restaurez le paquet NuGet" -ForegroundColor Yellow
    Write-Host "Microsoft.Windows.SDK.BuildTools, puis relancez ce script." -ForegroundColor Yellow
    exit 1
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$package = Join-Path $outDir "Hublot-$version.msix"
& $makeappx.FullName pack /d $layout /p $package /o
if ($LASTEXITCODE -ne 0) { throw "makeappx a échoué (code $LASTEXITCODE)." }

$size = [Math]::Round((Get-Item $package).Length / 1MB, 1)
Write-Host ""
Write-Host "OK -> $package ($size Mo, version $version)" -ForegroundColor Green
Write-Host "À téléverser dans Partner Center (Soumission › Packages)." -ForegroundColor Gray
