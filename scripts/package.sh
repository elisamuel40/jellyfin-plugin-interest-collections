#!/usr/bin/env bash
# Builds the plugin in Release and produces the zip Jellyfin expects, plus its MD5 checksum.
#
# Usage: ./scripts/package.sh [version]
#   version defaults to 0.1.0. Jellyfin wants a four-part assembly version, so ".0" is appended.

set -euo pipefail

VERSION="${1:-0.1.0}"
ASSEMBLY_VERSION="${VERSION}.0"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="${ROOT}/src/Jellyfin.Plugin.InterestCollections"
OUTPUT="${ROOT}/dist"
STAGING="${OUTPUT}/staging"

rm -rf "${OUTPUT}"
mkdir -p "${STAGING}"

echo "Building version ${ASSEMBLY_VERSION}…"
dotnet build "${PROJECT}" \
  --configuration Release \
  -p:Version="${ASSEMBLY_VERSION}" \
  -p:AssemblyVersion="${ASSEMBLY_VERSION}" \
  -p:FileVersion="${ASSEMBLY_VERSION}"

# Jellyfin loads the plugin assembly from the root of the zip. Only the plugin's own
# assembly belongs there; the server supplies everything else.
cp "${PROJECT}/bin/Release/net9.0/Jellyfin.Plugin.InterestCollections.dll" "${STAGING}/"

ZIP="${OUTPUT}/interest-collections-${VERSION}.zip"
(cd "${STAGING}" && zip -q -r "${ZIP}" .)
rm -rf "${STAGING}"

if command -v md5sum >/dev/null 2>&1; then
  (cd "${OUTPUT}" && md5sum "$(basename "${ZIP}")" > checksum.md5)
else
  # macOS ships md5 rather than md5sum.
  (cd "${OUTPUT}" && md5 -q "$(basename "${ZIP}")" | awk -v f="$(basename "${ZIP}")" '{print $1"  "f}' > checksum.md5)
fi

echo
echo "Package: ${ZIP}"
echo "MD5:     $(cut -d' ' -f1 "${OUTPUT}/checksum.md5")"
