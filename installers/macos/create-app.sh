#!/bin/bash
# EagleShot v2.0.0 - macOS .app Bundle & DMG Creator
set -e

APP_NAME="EagleShot"
VERSION="2.0.0"
BUNDLE_ID="com.eagleshot.app"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DIST_DIR="$PROJECT_ROOT/dist"

mkdir -p "$DIST_DIR"

# Build both architectures
for RID in osx-x64 osx-arm64; do

    PUBLISH_DIR="$PROJECT_ROOT/publish/$RID"
    APP_DIR="$PROJECT_ROOT/publish/${APP_NAME}_${RID}.app"
    OUTPUT_DMG="$DIST_DIR/EagleShot_v${VERSION}_${RID}.dmg"

    if [ "$RID" = "osx-arm64" ]; then
        ARCH_LABEL="Apple Silicon"
    else
        ARCH_LABEL="Intel"
    fi

    echo ""
    echo "=== Creating $APP_NAME.app ($ARCH_LABEL) ==="

    if [ ! -d "$PUBLISH_DIR" ]; then
        echo "Publish directory not found: $PUBLISH_DIR"
        echo "Run scripts/build-all.sh first."
        continue
    fi

    rm -rf "$APP_DIR"

    # .app structure
    mkdir -p "$APP_DIR/Contents/MacOS"
    mkdir -p "$APP_DIR/Contents/Resources"

    cp -r "$PUBLISH_DIR"/* "$APP_DIR/Contents/MacOS/"
    chmod +x "$APP_DIR/Contents/MacOS/EagleShot"

    # Create .icns icon
    if command -v sips &> /dev/null && command -v iconutil &> /dev/null; then
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
            echo "  Icon created"
        fi
    fi

    # Info.plist with all required permission descriptions
    cat > "$APP_DIR/Contents/Info.plist" << 'PLIST_HEADER'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
PLIST_HEADER

    cat >> "$APP_DIR/Contents/Info.plist" << EOF
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
    <string>EagleShot needs Screen Recording permission to capture your screen.</string>
    <key>NSAccessibilityUsageDescription</key>
    <string>EagleShot needs Accessibility permission for the global screenshot hotkey (F12).</string>
    <key>NSAppleEventsUsageDescription</key>
    <string>EagleShot needs Automation permission to check system permissions.</string>
</dict>
</plist>
EOF

    echo "  App bundle created: $APP_DIR"

    # Create DMG
    if command -v hdiutil &> /dev/null; then
        echo "  Creating DMG..."
        DMG_TEMP="$PROJECT_ROOT/publish/dmg_temp_${RID}"
        rm -rf "$DMG_TEMP"
        mkdir -p "$DMG_TEMP"

        # Copy .app with correct name
        cp -r "$APP_DIR" "$DMG_TEMP/$APP_NAME.app"
        ln -s /Applications "$DMG_TEMP/Applications"

        rm -f "$OUTPUT_DMG"
        hdiutil create -volname "$APP_NAME" \
            -srcfolder "$DMG_TEMP" \
            -ov -format UDZO \
            "$OUTPUT_DMG"

        rm -rf "$DMG_TEMP"
        echo "  DMG created: $OUTPUT_DMG"
    else
        echo "  hdiutil not available (not on macOS?) — skipping DMG"
    fi

done

echo ""
echo "=== Done ==="
echo "DMG files in: $DIST_DIR"
ls -lh "$DIST_DIR"/*.dmg 2>/dev/null || echo "  (no DMGs — run this on macOS)"
