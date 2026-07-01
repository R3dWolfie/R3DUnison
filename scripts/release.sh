#!/usr/bin/env bash
# Build a Release zip and publish it as a GitHub release.
# UMM auto-update flow: Repository.json (main branch) advertises the version;
# the DownloadUrl always points at releases/latest, so a release only needs
# Info.json + csproj + Repository.json versions bumped together — this script
# refuses to ship if they disagree.
set -euo pipefail
cd "$(dirname "$0")/.."

VER=$(grep -oP '"Version":\s*"\K[^"]+' src/R3DUnison/Info.json)
REPO_VER=$(grep -oP '"Version":\s*"\K[^"]+' Repository.json)
CSPROJ_VER=$(grep -oP '<Version>\K[^<]+' src/R3DUnison/R3DUnison.csproj)

if [[ "$VER" != "$REPO_VER" || "$VER" != "$CSPROJ_VER" ]]; then
    echo "VERSION MISMATCH: Info.json=$VER Repository.json=$REPO_VER csproj=$CSPROJ_VER" >&2
    exit 1
fi

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
"$DOTNET" build src/R3DUnison -c Release

rm -rf dist
mkdir -p dist/R3DUnison
cp src/R3DUnison/bin/Release/R3DUnison.dll src/R3DUnison/Info.json dist/R3DUnison/
(cd dist && zip -r R3DUnison.zip R3DUnison)

gh release create "v$VER" dist/R3DUnison.zip \
    --title "R3D Unison v$VER" \
    --notes "${NOTES:-R3D Unison v$VER}"
echo "Released v$VER"
