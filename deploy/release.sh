#!/usr/bin/env bash
# Builds, packages, and publishes a tagged release to the Forgejo repo.
# Linux runner counterpart of deploy/release.ps1 (kept as the windows-runner
# fallback). Secrets and refs arrive via environment variables, never as
# command-line arguments.
#   RELEASE_REF   - the pushed tag, e.g. v0.1.7
#   RELEASE_REPO  - owner/name, e.g. cheapnud/CheapFurniturePlanner
#   RELEASE_TOKEN - the workflow's repo-scoped token
set -euo pipefail

if [[ -z "${RELEASE_REF:-}" || -z "${RELEASE_REPO:-}" || -z "${RELEASE_TOKEN:-}" ]]; then
  echo "RELEASE_REF / RELEASE_REPO / RELEASE_TOKEN must be set" >&2
  exit 1
fi

ver="${RELEASE_REF#v}"
outDir="deploy/out/CheapFurniturePlanner"

# NO single-file bundling: Velopack packages this output and neither supports a
# bundled exe nor can verify the update hook inside one (vpk fails with "Unable
# to verify VelopackApp is called"). The exe ships beside its DLLs; the folder
# was always the ship unit anyway (wwwroot assets were never bundleable).
# NO trimming: Blazor, Avalonia and the XAML loader rely on reflection that
# trimming breaks silently.
# DebugType=none drops managed pdbs AND the ~100 MB of native symbol files
# (libSkiaSharp.pdb alone is 80 MB) that otherwise ship in the folder.
rm -rf "$outDir"
dotnet publish CheapFurniturePlanner.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:Version="$ver" \
  -o "$outDir"

# Belt and braces: native pdbs are content-copied by some packages regardless.
find "$outDir" -name "*.pdb" -delete

name="CheapFurniturePlanner-$ver-win-x64.zip"
zip_path="$(pwd)/$name"
rm -f "$zip_path"
# Info-ZIP's zip writes ZIP entries with the spec-mandated forward-slash
# separator natively on Linux (it's the platform's own path separator), so no
# extra flags are needed to match tar.exe's forward-slash guarantee on Windows.
# If a future runner image drops zip, fall back to `python3 -m zipfile -c`.
command -v zip >/dev/null 2>&1 || { echo "zip not found on runner" >&2; exit 1; }
(cd deploy/out && zip -rq "$zip_path" CheapFurniturePlanner)

# Velopack package: Setup.exe + full nupkg + feed manifest for auto-updates.
# Full packages only — delta generation needs the previous release's nupkg on
# disk, which a clean runner checkout doesn't have. The tool version is PINNED
# to the Velopack library version the app links; bump both together.
dotnet tool update --global vpk --version 1.2.0
export PATH="$HOME/.dotnet/tools:$PATH"
vpkOut="$(pwd)/deploy/velopack"
rm -rf "$vpkOut"
# The [win] directive tells vpk (running on Linux) to build a Windows package
# instead of a Linux one — vpk 1.2.0 supports this cross-compile mode natively,
# no extra runtime flag needed beyond the directive itself.
vpk "[win]" pack --packId CheapFurniturePlanner --packVersion "$ver" --packDir "$outDir" --mainExe CheapFurniturePlanner.exe --outputDir "$vpkOut"

# public https so the token never travels in the clear
api="https://git.cheapludes.be/api/v1/repos/$RELEASE_REPO"
body=$(printf '{"tag_name":"%s","name":"CheapFurniturePlanner %s","body":"Windows build, two install options. Installer (auto-updates): run CheapFurniturePlanner-win-Setup.exe once, updates arrive on their own afterwards. Portable: download the zip, extract the whole CheapFurniturePlanner folder, run CheapFurniturePlanner.exe. Neither needs .NET on the target machine."}' "$RELEASE_REF" "$ver")

release_response=$(curl -sS --fail -X POST \
  -H "Authorization: token $RELEASE_TOKEN" \
  -H "Content-Type: application/json" \
  -d "$body" \
  "$api/releases")
# top-level "id" is the first field Forgejo serializes, ahead of any nested
# author/publisher id — grep avoids a jq/python3 dependency for one field.
release_id=$(grep -o '"id":[0-9]*' <<< "$release_response" | head -1 | grep -o '[0-9]*')
if [[ -z "$release_id" ]]; then
  echo "could not parse release id from response: $release_response" >&2
  exit 1
fi

curl -sS --fail -X POST -H "Authorization: token $RELEASE_TOKEN" \
  -F "attachment=@$zip_path" "$api/releases/$release_id/assets?name=$name"

# Attach every Velopack artifact under its own name — the updater reads the
# feed manifest and nupkg straight from the release's assets.
asset_count=0
for asset in "$vpkOut"/*; do
  [ -f "$asset" ] || continue
  curl -sS --fail -X POST -H "Authorization: token $RELEASE_TOKEN" \
    -F "attachment=@$asset" "$api/releases/$release_id/assets?name=$(basename "$asset")"
  asset_count=$((asset_count + 1))
done

echo "Published release $ver with the zip and $asset_count Velopack assets"
