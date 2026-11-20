#!/bin/bash
# EagleShot installer for macOS.
#
#   curl -fsSL https://hhasanguclu.github.io/EagleShot/install-macos.sh | bash
#
# Downloads the latest release for this Mac (Apple Silicon or Intel), copies EagleShot.app to
# /Applications and opens it. Files downloaded with curl are not quarantined, so macOS does not
# show the "EagleShot is damaged / can't be verified - Move to Trash" dialog that browsers trigger
# for apps without a paid Apple developer certificate.
set -euo pipefail

REPO="hhasanguclu/EagleShot"
case "$(uname -m)" in
  arm64) ARCH="osx-arm64" ;;
  *) ARCH="osx-x64" ;;
esac

echo "EagleShot installer ($ARCH)"
URL="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" | grep -o "https://[^\"]*_${ARCH}\.dmg" | head -1)"
if [ -z "$URL" ]; then
  echo "Could not find the download. Get it from https://github.com/$REPO/releases/latest" >&2
  exit 1
fi

TMP="$(mktemp -d)"
MNT="$TMP/mnt"
cleanup() {
  hdiutil detach "$MNT" -quiet 2>/dev/null || true
  rm -rf "$TMP"
}
trap cleanup EXIT

echo "Downloading $URL"
curl -fL --progress-bar -o "$TMP/EagleShot.dmg" "$URL"

mkdir -p "$MNT"
hdiutil attach "$TMP/EagleShot.dmg" -nobrowse -readonly -mountpoint "$MNT" -quiet

DEST="/Applications"
if [ ! -w "$DEST" ]; then
  DEST="$HOME/Applications"
  mkdir -p "$DEST"
fi

# Quit a running EagleShot before replacing it.
osascript -e 'tell application "EagleShot" to quit' >/dev/null 2>&1 || true
pkill -x EagleShot 2>/dev/null || true
sleep 1

rm -rf "$DEST/EagleShot.app"
ditto "$MNT/EagleShot.app" "$DEST/EagleShot.app"
xattr -dr com.apple.quarantine "$DEST/EagleShot.app" 2>/dev/null || true

echo "Installed to $DEST/EagleShot.app"
echo "On first start, allow Screen Recording and Accessibility in System Settings → Privacy & Security."
open "$DEST/EagleShot.app"
