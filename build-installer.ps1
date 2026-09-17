# Builds the offline installer package:
#   dist/SadrApp-Setup/  SadrSetup.exe      <- setup wizard (run as admin)
#                        redist/…           <- .NET runtime exe + SqlLocalDB MSI
#                        app/…              <- SadrApp publish output (framework-dependent)
# The whole folder is what you give to testers (zip it or copy it).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

$frameworkVer = '10.0'
$distDir      = Join-Path $root 'dist\SadrApp-Setup'
$appDir       = Join-Path $distDir 'app'
$redistDir    = Join-Path $distDir 'redist'
$setupDir     = Join-Path $root 'installer\SadrSetup'

Write-Host '== 1) publishing SadrApp (framework-dependent, win-x64) =='
dotnet publish SadrApp\SadrApp.csproj -c Release -r win-x64 --self-contained false `
    -o $appDir -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'SadrApp publish failed' }

Write-Host '== 2) publishing SadrSetup (.NET Framework 4.7.2 bootstrapper — runs on any Windows 10/11) =='
dotnet publish $setupDir\SadrSetup.csproj -c Release -o $distDir
if ($LASTEXITCODE -ne 0) { throw 'SadrSetup publish failed' }

Write-Host '== 3) gathering redistributables =='
New-Item -ItemType Directory -Force -Path $redistDir | Out-Null

$dotnetExe = Join-Path $redistDir "windowsdesktop-runtime-$frameworkVer-win-x64.exe"
if (-not (Test-Path $dotnetExe)) {
    Write-Host "   downloading .NET Desktop Runtime 10..."
    # Official stable channel alias (resolves to builds.dotnet.microsoft.com).
    $url = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'
    Invoke-WebRequest -Uri $url -OutFile $dotnetExe -UserAgent 'Mozilla/5.0'
}

$localDbMsi = Join-Path $redistDir 'SqlLocalDB-2022.msi'
if (-not (Test-Path $localDbMsi)) {
    Write-Host "   downloading SqlLocalDB 2022 MSI..."
    # Official per-language URL pair (extracted from the SQL 2022 SSE bootstrapper).
    $urls = @(
        'https://download.microsoft.com/download/0/8/2/082e540c-cd55-4b2b-adf1-b6f16ef1deaf/SqlLocalDB.msi'
    )
    Invoke-WebRequest -Uri $urls[0] -OutFile $localDbMsi -UserAgent 'Mozilla/5.0'
}

Write-Host '== 4) done =='
Write-Host "Package folder: $distDir"
Write-Host "Zip it (or copy it) and give it to testers. Run SadrSetup.exe as administrator."
Pop-Location
