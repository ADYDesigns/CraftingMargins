#!/bin/bash
# Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
#
# Builds "Crafting Margins.app" for a Mac and zips it.  Run it on a Mac (GitHub's Mac
# builder runs it for each release; see .github/workflows/mac-app.yml):
#
#   packaging/macos/make-app.sh osx-arm64     # Apple silicon (M1 and later)
#   packaging/macos/make-app.sh osx-x64       # Intel Macs
#
# The app is not signed with an Apple Developer ID or notarized; as a free project it doesn't
# use Apple's paid developer program.  It is "ad-hoc" signed, which needs no account and is
# what lets an Apple silicon Mac run it at all.  The README explains how to open it the first time.

set -euo pipefail

RID="${1:?Give the runtime: osx-arm64 or osx-x64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/publish-mac/$RID"
APP="$OUT/Crafting Margins.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -n 1)"

case "$RID" in
  osx-arm64) LABEL="Apple_Silicon" ;;
  osx-x64)   LABEL="Intel" ;;
  *) echo "Unknown runtime $RID" >&2; exit 1 ;;
esac

echo "Building Crafting Margins $VERSION for $RID"
rm -rf "$OUT"
dotnet publish "$ROOT/src/CraftingMargins.App" -c Release -r "$RID" -o "$OUT/files"

# The standard bundle layout: the program in Contents/MacOS, the icon in Contents/Resources.
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUT/files/." "$APP/Contents/MacOS/"
cp "$ROOT/packaging/macos/CraftingMargins.icns" "$APP/Contents/Resources/"
sed "s/VERSION/$VERSION/g" "$ROOT/packaging/macos/Info.plist" > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/Crafting Margins"

# Ad-hoc signature ("-" means no certificate).  Free; required on Apple silicon.
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"

# ditto keeps the bundle's permissions and signature intact inside the zip.  Underscores, not
# spaces, because GitHub release downloads can't have spaces in their names.
ZIP="$ROOT/publish-mac/Crafting_Margins_${VERSION}_macOS_${LABEL}.zip"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
shasum -a 256 "$ZIP"
echo "Made $ZIP"
