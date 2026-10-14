<#
.SYNOPSIS
    Compile Hublot et produit l'installateur unique .\dist\Hublot-Setup.exe

.DESCRIPTION
    1. Publie le service Hublot.exe (autonome, « trimmé », un seul fichier) ;
    2. publie Hublot Core (WPF) en y embarquant le service : ce même exécutable installe Hublot
       quand on le lance (Hublot-Setup.exe), puis sert de Hublot Core une fois copié dans
       %LocalAppData%\Programs\Hublot ;
    3. dépose la version dans le dossier des mises à jour (Hublot-Setup.exe + version.txt).
    Installation par utilisateur, sans droits administrateur ; désinstallation depuis
    Paramètres Windows › Applications installées.

.PARAMETER Updates
    Dossier des mises à jour (vide : ne rien déposer).

.EXAMPLE
    .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'dist',
    [string]$Updates = 'C:\Projets Claude\MAJ Projet Hublot'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$service = Join-Path $root 'src\Hublot\Hublot.csproj'
$core = Join-Path $root 'src\Hublot.Core\Hublot.Core.csproj'
$staging = Join-Path $root 'obj\payload'
$outDir = Join-Path $root $Output

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) {
    throw "SDK .NET introuvable. Installez-le : winget install Microsoft.DotNet.SDK.10"
}

function Publish([string]$Project, [string]$Destination, [string[]]$Extra) {
    $arguments = @(
        'publish', $Project,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        '-o', $Destination,
        '--nologo'
    ) + $Extra
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation de $Project (code $LASTEXITCODE)." }
}

Write-Host "1/3 Service Hublot.exe..." -ForegroundColor Cyan
Publish $service $staging @('-p:PublishTrimmed=true')
$payload = Join-Path $staging 'Hublot.exe'

Write-Host "2/3 Hublot Core et installateur..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Get-ChildItem $outDir -File | Remove-Item -Force
Publish $core $staging @('-p:EnableCompressionInSingleFile=true', "-p:ServicePayload=$payload")
$setup = Join-Path $outDir 'Hublot-Setup.exe'
Copy-Item (Join-Path $staging 'HublotCore.exe') $setup -Force
$version = (Get-Item $payload).VersionInfo.ProductVersion -replace '\+.*$', ''

if ($Updates) {
    Write-Host "3/3 Dépôt de la version $version dans $Updates..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $Updates | Out-Null
    Copy-Item $setup (Join-Path $Updates 'Hublot-Setup.exe') -Force
    Set-Content -Path (Join-Path $Updates 'version.txt') -Value $version -Encoding ASCII
}

$size = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "OK -> $setup ($size Mo, version $version)" -ForegroundColor Green
Write-Host "Double-cliquez dessus pour installer ou mettre à jour Hublot." -ForegroundColor Gray
