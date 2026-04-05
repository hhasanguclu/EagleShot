#!/bin/bash
# EagleShot - Linux Uninstaller
set -e

APP_ID="com.eagleshot.app"
INSTALL_DIR="$HOME/.local/share/eagleshot"
BIN_LINK="$HOME/.local/bin/eagleshot"
DESKTOP_DIR="$HOME/.local/share/applications"
AUTOSTART_DIR="$HOME/.config/autostart"

echo "=== EagleShot Uninstaller ==="

# Kill running instance
pkill -f "EagleShot" 2>/dev/null || true

# Remove files
rm -rf "$INSTALL_DIR"
rm -f "$BIN_LINK"
rm -f "$DESKTOP_DIR/$APP_ID.desktop"
rm -f "$AUTOSTART_DIR/$APP_ID.desktop"

echo "EagleShot has been uninstalled."
