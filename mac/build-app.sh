#!/usr/bin/env bash
set -euo pipefail

# TimeSink macOS App Bundler
# Builds TimeSinkApp via SwiftPM and packages it into a proper TimeSink.app bundle.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAC_DIR="$REPO_ROOT/mac/TimeSinkCore"
CONFIG="release"
INSTALL_TO_APPS=false
OPEN_AFTER_BUILD=false

for arg in "$@"; do
    case "$arg" in
        --install)
            INSTALL_TO_APPS=true
            ;;
        --open)
            OPEN_AFTER_BUILD=true
            ;;
        debug|Debug)
            CONFIG="debug"
            ;;
        release|Release)
            CONFIG="release"
            ;;
        *)
            echo "Unknown argument: $arg"
            echo "Usage: ./mac/build-app.sh [release|debug] [--install] [--open]"
            exit 1
            ;;
    esac
done

echo "==> Building TimeSink ($CONFIG) with SwiftPM..."
cd "$MAC_DIR"
swift build -c "$CONFIG"

BIN_DIR="$(swift build -c "$CONFIG" --show-bin-path)"
APP_DIR="$REPO_ROOT/artifacts/mac/TimeSink.app"

echo "==> Staging app bundle at $APP_DIR..."
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"
mkdir -p "$APP_DIR/Contents/Frameworks"

# 1. Binary executable
cp "$BIN_DIR/TimeSinkApp" "$APP_DIR/Contents/MacOS/TimeSinkApp"

# 2. Resource bundle (icons, assets for Bundle.module)
if [ -d "$BIN_DIR/TimeSinkCore_TimeSinkCore.bundle" ]; then
    cp -R "$BIN_DIR/TimeSinkCore_TimeSinkCore.bundle" "$APP_DIR/Contents/Resources/"
fi

# 3. Dynamic frameworks (SQLCipher)
if [ -d "$BIN_DIR/SQLCipher.framework" ]; then
    cp -R "$BIN_DIR/SQLCipher.framework" "$APP_DIR/Contents/Frameworks/"
elif [ -d "$BIN_DIR/PackageFrameworks/SQLCipher.framework" ]; then
    cp -R "$BIN_DIR/PackageFrameworks/SQLCipher.framework" "$APP_DIR/Contents/Frameworks/"
fi

# 4. Icon if available
if [ -f "$REPO_ROOT/mac/AppIcon.icns" ]; then
    cp "$REPO_ROOT/mac/AppIcon.icns" "$APP_DIR/Contents/Resources/AppIcon.icns"
fi

# 5. Info.plist
cat << 'EOF' > "$APP_DIR/Contents/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleName</key>
    <string>TimeSink</string>
    <key>CFBundleDisplayName</key>
    <string>TimeSink</string>
    <key>CFBundleIdentifier</key>
    <string>com.timesink.mac</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleExecutable</key>
    <string>TimeSinkApp</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>14.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
</dict>
</plist>
EOF

# 6. Set rpath so dynamic linker finds Contents/Frameworks
echo "==> Configuring library runpaths..."
install_name_tool -add_rpath "@executable_path/../Frameworks" "$APP_DIR/Contents/MacOS/TimeSinkApp" 2>/dev/null || true

# 7. Ad-hoc codesign
echo "==> Signing app bundle (ad-hoc)..."
if [ -d "$APP_DIR/Contents/Frameworks/SQLCipher.framework" ]; then
    codesign --force --timestamp=none --sign - "$APP_DIR/Contents/Frameworks/SQLCipher.framework"
fi
codesign --force --timestamp=none --sign - "$APP_DIR"

echo "==> App bundle ready: $APP_DIR"

# 8. Optional install to /Applications
TARGET_APP="$APP_DIR"
if [ "$INSTALL_TO_APPS" = true ]; then
    echo "==> Installing to /Applications/TimeSink.app..."
    killall TimeSinkApp 2>/dev/null || true
    rm -rf "/Applications/TimeSink.app"
    cp -R "$APP_DIR" "/Applications/"
    TARGET_APP="/Applications/TimeSink.app"
    echo "==> Installed to /Applications/TimeSink.app"
fi

if [ "$OPEN_AFTER_BUILD" = true ]; then
    echo "==> Launching TimeSink..."
    open "$TARGET_APP"
fi
