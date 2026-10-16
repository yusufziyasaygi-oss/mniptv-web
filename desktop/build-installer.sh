#!/bin/sh
set -eu
BASE=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD="$BASE/../../work/installer"
mkdir -p "$BUILD/payload/Applications" "$BUILD/scripts"
ditto "$BASE/MN Player.app" "$BUILD/payload/Applications/MN Player.app"
cp "$BASE/desktop/postinstall" "$BUILD/scripts/postinstall"
chmod 755 "$BUILD/scripts/postinstall"
pkgbuild --analyze --root "$BUILD/payload" "$BUILD/components.plist"
component_index=0
while /usr/libexec/PlistBuddy -c "Print :$component_index:RootRelativeBundlePath" "$BUILD/components.plist" >/dev/null 2>&1; do
    /usr/libexec/PlistBuddy -c "Set :$component_index:BundleIsRelocatable false" "$BUILD/components.plist"
    component_index=$((component_index + 1))
done
pkgbuild --root "$BUILD/payload" --component-plist "$BUILD/components.plist" --scripts "$BUILD/scripts" --identifier com.mavinokta.player.installer --version 2.2.2 --install-location / "$BUILD/MNPlayer-component.pkg"
productbuild --distribution "$BASE/desktop/Distribution.xml" --package-path "$BUILD" --resources "$BASE/desktop/installer-resources" "$BASE/../MNPlayer-2.2.2-AppleSilicon.pkg"
