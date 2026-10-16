#!/bin/sh
set -eu
BASE=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
APP="$BASE/MN Player.app"
VLC="/Applications/VLC.app/Contents/MacOS"
mkdir -p "$APP/Contents/MacOS/lib" "$APP/Contents/MacOS/plugins" "$APP/Contents/Resources/Licenses"
cp -R "$VLC/lib/" "$APP/Contents/MacOS/lib/"
cp -R "$VLC/plugins/" "$APP/Contents/MacOS/plugins/"
swiftc -O -target arm64-apple-macosx13.0 -module-cache-path "$BASE/.build/module-cache" -I "$VLC/include" -import-objc-header "$BASE/desktop/Bridge.h" -L "$VLC/lib" -lvlc -Xlinker -rpath -Xlinker '@executable_path/lib' -framework Cocoa -framework UniformTypeIdentifiers "$BASE/desktop/MNPlayer.swift" -o "$APP/Contents/MacOS/MNPlayer"
cp "$BASE/desktop/MNPlayer.icns" "$APP/Contents/Resources/"
cp "$BASE/desktop/video-types.json" "$APP/Contents/Resources/"
cp "$BASE/desktop/Info.plist" "$APP/Contents/Info.plist"
cp -R "$BASE/web" "$APP/Contents/Resources/"
cp -R "$BASE/desktop/Licenses/" "$APP/Contents/Resources/Licenses/"
find "$APP/Contents/MacOS" -type f \( -name '*.dylib' -o -name '*.jar' \) -exec sh -c 'codesign --force --sign - "$1" 2>/dev/null' sh {} \;
# Plugin cache: built after the plugins are signed, so file sizes and dates match at run time.
"$APP/Contents/MacOS/MNPlayer" --build-plugin-cache
PREVIEW="$APP/Contents/Helpers/MN Preview.app"
mkdir -p "$PREVIEW/Contents/MacOS"
cp "$BASE/desktop/Preview-Info.plist" "$PREVIEW/Contents/Info.plist"
swiftc -O -target arm64-apple-macosx13.0 -module-cache-path "$BASE/.build/module-cache" -I "$VLC/include" -import-objc-header "$BASE/desktop/Bridge.h" -L "$VLC/lib" -lvlc -Xlinker -rpath -Xlinker '@executable_path/../../../../MacOS/lib' -framework Cocoa -framework UniformTypeIdentifiers "$BASE/desktop/MNPlayer.swift" -o "$PREVIEW/Contents/MacOS/MNPreview"
codesign --force --sign - "$PREVIEW"
FINDER="$APP/Contents/Helpers/MN Finder.app"
mkdir -p "$FINDER/Contents/MacOS"
cp "$BASE/desktop/Finder-Info.plist" "$FINDER/Contents/Info.plist"
swiftc -O -target arm64-apple-macosx13.0 -module-cache-path "$BASE/.build/module-cache" -framework Cocoa "$BASE/desktop/FinderService.swift" -o "$FINDER/Contents/MacOS/MNFinder"
codesign --force --sign - "$FINDER"
codesign --force --sign - "$APP"
printf 'Hazır: %s\n' "$APP"
