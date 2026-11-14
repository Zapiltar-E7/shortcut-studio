#!/bin/sh
# Copies the built plugin into dist/, which the Shortcut Studio app embeds and installs into Steam.
set -e
cd "$(dirname "$0")"
rm -rf dist
mkdir -p dist/backend dist/.millennium/Dist
cp plugin.json dist/
cp backend/*.lua dist/backend/
cp .millennium/Dist/index.js dist/.millennium/Dist/
echo "staged:"; find dist -type f
