#!/bin/bash
# EagleShot v2.0.0 - macOS .app Bundle Creator
# Run this after dotnet publish to create EagleShot.app
set -e

APP_NAME="EagleShot"
VERSION="2.0.0"
BUNDLE_ID="com.eagleshot.app"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

PUBLISH_DIR="$PROJECT_ROOT/publish/$RID"
APP_DIR="$PROJECT_ROOT/publish/$APP_NAME.app"
OUTPUT_DMG="$PROJECT_ROOT/publish/EagleShot_v${VERSION}_${RID}.dmg"

echo "=== Creating $APP_NAME.app bundle ($RID) ==="

if [ ! -d "$PUBLISH_DIR" ]; then
    echo "Error: Publish directory not found: $PUBLISH_DIR"
    echo "Run 'dotnet publish -c Release -r $RID --self-contained true -o publish/$RID' first."
    exit 1
fi

# Clean previous
rm -rf "$APP_DIR"

# Create .app structure
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

# Copy published files
cp -r "$PUBLISH_DIR"/* "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/EagleShot"

# Copy icon (convert png to icns if possible, otherwise use png)
if command -v sips &> /dev/null && command -v iconutil &> /dev/null; then
    echo "Creating .icns icon..."
    ICONSET="$APP_DIR/Contents/Resources/AppIcon.iconset"
    mkdir -p "$ICONSET"
    LOGO="$PROJECT_ROOT/Resources/logo.png"
    if [ -f "$LOGO" ]; then
        sips -z 16 16     "$LOGO" --out "$ICONSET/icon_16x16.png" 2>/dev/null
        sips -z 32 32     "$LOGO" --out "$ICONSET/icon_16x16@2x.png" 2>/dev/null
        sips -z 32 32     "$LOGO" --out "$ICONSET/icon_32x32.png" 2>/dev/null
        sips -z 64 64     "$LOGO" --out "$ICONSET/icon_32x32@2x.png" 2>/dev/null
        sips -z 128 128   "$LOGO" --out "$ICONSET/icon_128x128.png" 2>/dev/null
        sips -z 256 256   "$LOGO" --out "$ICONSET/icon_128x128@2x.png" 2>/dev/null
        sips -z 256 256   "$LOGO" --out "$ICONSET/icon_256x256.png" 2>/dev/null
        sips -z 512 512   "$LOGO" --out "$ICONSET/icon_256x256@2x.png" 2>/dev/null
        sips -z 512 512   "$LOGO" --out "$ICONSET/icon_512x512.png" 2>/dev/null
        sips -z 1024 1024 "$LOGO" --out "$ICONSET/icon_512x512@2x.png" 2>/dev/null
        iconutil -c icns "$ICONSET" -o "$APP_DIR/Contents/Resources/AppIcon.icns"
        rm -rf "$ICONSET"
    fi
else
    echo "sips/iconutil not available, skipping .icns creation"
fi

# Create Info.plist
cat > "$APP_DIR/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleExecutable</key>
    <string>EagleShot</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSUIElement</key>
    <true/>
    <key>NSScreenCaptureUsageDescription</key>
    <string>EagleShot needs screen capture permission to take screenshots.</string>
</dict>
</plist>
EOF

echo "Created: $APP_DIR"

# Create DMG if hdiutil is available
if command -v hdiutil &> /dev/null; then
    echo "Creating DMG..."
    
    DMG_TEMP="$PROJECT_ROOT/publish/dmg_temp"
    rm -rf "$DMG_TEMP"
    mkdir -p "$DMG_TEMP"
    cp -r "$APP_DIR" "$DMG_TEMP/"
    ln -s /Applications "$DMG_TEMP/Applications"
    
    rm -f "$OUTPUT_DMG"
    hdiutil create -volname "$APP_NAME" \
        -srcfolder "$DMG_TEMP" \
        -ov -format UDZO \
        "$OUTPUT_DMG"
    
    rm -rf "$DMG_TEMP"
    echo "Created: $OUTPUT_DMG"
fi

echo ""
echo "=== Done! ==="
echo "To install: drag EagleShot.app to /Applications"
