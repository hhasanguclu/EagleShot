#!/bin/bash
# EagleShot v2.0.0 - Cross-platform build script
# Run from project root: ./scripts/build-all.sh
set -e

VERSION="2.0.0"
echo "=== EagleShot v$VERSION - Building all platforms ==="

rm -rf publish
mkdir -p publish

COMMON="-c Release --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true"

for RID in win-x64 win-arm64 linux-x64 osx-x64 osx-arm64; do
    echo ""
    echo "Publishing $RID..."
    dotnet publish $COMMON -r $RID -o publish/$RID
done

echo ""
echo "=== All platforms published to ./publish/ ==="
echo ""
echo "Next steps:"
echo "  Windows : Inno Setup ile installers/windows/setup.iss derle"
echo "  Linux   : installers/linux/install.sh calistir"
echo "  macOS   : installers/macos/create-app.sh calistir"
