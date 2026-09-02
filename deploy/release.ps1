# Builds, packages, and publishes a tagged release to the Forgejo repo.
# Invoked by the release workflow via `powershell -ExecutionPolicy Bypass -File` —
# the runner executes as SYSTEM, whose effective policy blocks inline .ps1 steps,
# and the explicit -File + Bypass invocation is immune to that. Secrets and refs
# arrive via environment variables, never as command-line arguments.
#   RELEASE_REF   - the pushed tag, e.g. v0.1.7
#   RELEASE_REPO  - owner/name, e.g. cheapnud/CheapFurniturePlanner
#   RELEASE_TOKEN - the workflow's repo-scoped token
$ErrorActionPreference = 'Stop'

$refName = $env:RELEASE_REF
$repository = $env:RELEASE_REPO
$token = $env:RELEASE_TOKEN
if (-not $refName -or -not $repository -or -not $token) { throw "RELEASE_REF / RELEASE_REPO / RELEASE_TOKEN must be set" }
$ver = $refName.TrimStart('v')

powershell -ExecutionPolicy Bypass -File deploy/publish.ps1 -Version $ver
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

$name = "CheapFurniturePlanner-$ver-win-x64.zip"
$zip = Join-Path (Get-Location) $name
if (Test-Path $zip) { Remove-Item $zip -Force }
# Windows' bsdtar (tar.exe) writes ZIP entries with the spec-mandated forward-slash
# separator and deflates fast (~8s, ~91 MB). Both Compress-Archive and .NET Framework's
# ZipFile (what PS 5.1 uses) emit backslash separators, which violate the ZIP spec and
# make extractors produce flat files named "CheapFurniturePlanner\..." instead of a directory tree.
tar.exe --format zip --options zip:compression=deflate -c -f $zip -C deploy/out CheapFurniturePlanner
if ($LASTEXITCODE -ne 0) { throw "zip failed ($LASTEXITCODE)" }

# Velopack package: Setup.exe + full nupkg + feed manifest for auto-updates.
# Full packages only — delta generation needs the previous release's nupkg on
# disk, which a clean runner checkout doesn't have. The tool version is PINNED
# to the Velopack library version the app links; bump both together.
dotnet tool update --global vpk --version 1.2.0
$vpk = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
$vpkOut = Join-Path (Get-Location) "deploy\velopack"
if (Test-Path $vpkOut) { Remove-Item $vpkOut -Recurse -Force }
& $vpk pack --packId CheapFurniturePlanner --packVersion $ver --packDir deploy\out\CheapFurniturePlanner --mainExe CheapFurniturePlanner.exe --outputDir $vpkOut
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }

# public https so the token never travels in the clear
$api = "https://git.cheapludes.be/api/v1/repos/$repository"
$body = @{
  tag_name = $refName
  name     = "CheapFurniturePlanner $ver"
  body     = "Windows build, two install options. Installer (auto-updates): run CheapFurniturePlanner-win-Setup.exe once, updates arrive on their own afterwards. Portable: download the zip, extract the whole CheapFurniturePlanner folder, run CheapFurniturePlanner.exe. Neither needs .NET on the target machine."
} | ConvertTo-Json

$rel = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers @{ Authorization = "token $token" } -ContentType "application/json" -Body $body

& curl.exe -sS --fail -X POST -H "Authorization: token $token" -F "attachment=@$zip" "$api/releases/$($rel.id)/assets?name=$name"
if ($LASTEXITCODE -ne 0) { throw "asset upload failed ($LASTEXITCODE)" }

# Attach every Velopack artifact under its own name — the updater reads the
# feed manifest and nupkg straight from the release's assets.
Get-ChildItem $vpkOut -File | ForEach-Object {
  & curl.exe -sS --fail -X POST -H "Authorization: token $token" -F "attachment=@$($_.FullName)" "$api/releases/$($rel.id)/assets?name=$($_.Name)"
  if ($LASTEXITCODE -ne 0) { throw "velopack asset upload failed for $($_.Name) ($LASTEXITCODE)" }
}

Write-Host "Published release $ver with the zip and $((Get-ChildItem $vpkOut -File).Count) Velopack assets"
