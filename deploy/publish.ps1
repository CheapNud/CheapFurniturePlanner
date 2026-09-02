# Publishes a self-contained win-x64 build of CheapFurniturePlanner to deploy\out\CheapFurniturePlanner.
# One folder, CheapFurniturePlanner.exe to double-click — no .NET install needed on the
# target machine. Run from anywhere: .\deploy\publish.ps1 [-Version 0.1.0]
#
# Notes:
# - NO single-file bundling: Velopack packages this output and neither supports a
#   bundled exe nor can verify the update hook inside one (vpk fails with "Unable
#   to verify VelopackApp is called"). The exe ships beside its DLLs; the folder
#   was always the ship unit anyway (wwwroot assets were never bundleable).
# - NO trimming: Blazor, Avalonia and the XAML loader rely on reflection that
#   trimming breaks silently.
param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "out\CheapFurniturePlanner"

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

# DebugType=none drops managed pdbs AND the ~100 MB of native symbol files
# (libSkiaSharp.pdb alone is 80 MB) that otherwise ship in the folder.
dotnet publish (Join-Path $repoRoot "CheapFurniturePlanner.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Belt and braces: native pdbs are content-copied by some packages regardless.
Get-ChildItem $outDir -Filter "*.pdb" | Remove-Item -Force

$exe = Join-Path $outDir "CheapFurniturePlanner.exe"
$sizeMb = [math]::Round((Get-ChildItem $outDir -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ""
Write-Host "Published $Version -> $outDir ($sizeMb MB)"
Write-Host "Entry point: $exe"
