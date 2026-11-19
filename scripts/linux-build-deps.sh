#!/usr/bin/env bash
# Development packages needed to build EagleShot on Debian / Ubuntu.
set -euo pipefail
SUDO=""
if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi
$SUDO apt-get update -q
$SUDO apt-get install -y -q --no-install-recommends \
  build-essential pkg-config clang libclang-dev \
  libgtk-3-dev libxdo-dev libayatana-appindicator3-dev \
  libxcb1-dev libxrandr-dev libxkbcommon-dev libwayland-dev libegl1-mesa-dev libgbm-dev \
  libdbus-1-dev libpipewire-0.3-dev
