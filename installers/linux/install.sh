#!/bin/bash
# EagleShot v2.0.0 - Linux Installer
set -e

APP_NAME="EagleShot"
APP_ID="com.eagleshot.app"
INSTALL_DIR="$HOME/.local/share/eagleshot"
BIN_LINK="$HOME/.local/bin/eagleshot"
DESKTOP_DIR="$HOME/.local/share/applications"
AUTOSTART_DIR="$HOME/.config/autostart"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

echo "=== $APP_NAME Installer ==="
echo ""

# Create directories
mkdir -p "$INSTALL_DIR"
mkdir -p "$HOME/.local/bin"
mkdir -p "$DESKTOP_DIR"
mkdir -p "$AUTOSTART_DIR"

# Copy files
echo "[1/4] Installing application files..."
cp -r "$PROJECT_ROOT"/publish/linux-x64/* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/EagleShot"

# Create symlink in PATH
echo "[2/4] Creating command link..."
ln -sf "$INSTALL_DIR/EagleShot" "$BIN_LINK"

# Create .desktop file
echo "[3/4] Creating desktop entry..."
cat > "$DESKTOP_DIR/$APP_ID.desktop" << EOF
[Desktop Entry]
Type=Application
Name=$APP_NAME
Comment=Cross-platform screenshot tool
Exec=$INSTALL_DIR/EagleShot
Icon=$INSTALL_DIR/Resources/logo.png
Terminal=false
Categories=Utility;Graphics;
StartupNotify=true
Keywords=screenshot;capture;snip;
EOF

# Create autostart entry
echo "[4/4] Setting up autostart..."
cat > "$AUTOSTART_DIR/$APP_ID.desktop" << EOF
[Desktop Entry]
Type=Application
Name=$APP_NAME
Exec=$INSTALL_DIR/EagleShot
Hidden=false
X-GNOME-Autostart-enabled=true
EOF

echo ""
echo "=== Installation complete! ==="
echo "  Run:  eagleshot"
echo "  Or find '$APP_NAME' in your application menu."
echo "  Press PrintScreen to take a screenshot."
echo ""
echo "To uninstall, run: $SCRIPT_DIR/uninstall.sh"
