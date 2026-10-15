#!/bin/bash
# Builds "build/MN IPTV.app" (Apple Silicon) with libmpv embedded, then build/MN-IPTV-Mac.dmg.
set -euo pipefail
cd "$(dirname "$0")/.."
[[ "$(uname -s)" == Darwin ]] || { echo "macOS required"; exit 1; }
rm -rf build && mkdir -p build
MPV_PREFIX="$(bash scripts/prepare-mpv.sh)"
ARCH="$(uname -m)"
TARGET="${ARCH}-apple-macos14.0"
SDK="$(xcrun --show-sdk-path)"
APP="build/MN IPTV.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "1/4 Compile"
xcrun clang -c Native/MaviMPVBridge.m -o build/MaviMPVBridge.o -fobjc-arc -arch "$ARCH" -isysroot "$SDK" \
  -mmacosx-version-min=14.0 -I "$MPV_PREFIX/include" -Wno-deprecated-declarations
xcrun swiftc -parse-as-library -module-cache-path "$PWD/build/module-cache" Sources/*.swift build/MaviMPVBridge.o \
  -import-objc-header Native/MaviMPVBridge.h -L "$MPV_PREFIX/lib" -lmpv \
  -Xlinker -rpath -Xlinker @executable_path/../Frameworks \
  -swift-version 5 -O -target "$TARGET" -sdk "$SDK" \
  -framework Cocoa -framework SwiftUI -framework OpenGL -framework QuartzCore \
  -o "$APP/Contents/MacOS/MaviNoktaIPTV"

echo "2/4 Resources"
cp Info.plist "$APP/Contents/Info.plist"
ICONSET=build/AppIcon.iconset
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) Resources/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
CA="${MAVI_CA_FILE:-/etc/ssl/cert.pem}"
cp "$CA" "$APP/Contents/Resources/CACertificates.pem"

echo "3/4 Embed libmpv"
python3 scripts/bundle-mpv.py "$APP" "$MPV_PREFIX" "$ARCH"
MPV_MIN="$(cat "$APP/Contents/Resources/MinimumMPVSystem.txt")"
MIN_OS="$(python3 -c "import sys;a=tuple(map(int,'$MPV_MIN'.split('.')));print('.'.join(map(str,max(a,(14,0)))))")"
/usr/libexec/PlistBuddy -c "Set :LSMinimumSystemVersion $MIN_OS" "$APP/Contents/Info.plist"
echo "$MIN_OS" > build/minimum-macos.txt
for LIB in "$APP"/Contents/Frameworks/*.dylib; do codesign --force --sign - "$LIB"; done
codesign --force --sign - "$APP"
codesign --verify --deep --strict "$APP"

echo "4/4 DMG"
STAGE="$(mktemp -d)"
ditto "$APP" "$STAGE/MN IPTV.app"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "MN IPTV" -srcfolder "$STAGE" -format UDZO -ov build/MN-IPTV-Mac.dmg
hdiutil verify build/MN-IPTV-Mac.dmg
echo "Ready: build/MN-IPTV-Mac.dmg (minimum macOS $MIN_OS)"
