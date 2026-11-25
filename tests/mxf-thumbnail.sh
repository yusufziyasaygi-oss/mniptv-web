#!/bin/bash
set -euo pipefail
app="$PWD/MN Player.app"
ext="$app/Contents/PlugIns/MXFFirstFrame.appex"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
codesign --verify --strict "$ext"
lsregister=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
"$lsregister" -f "$app"
# Installation registers the containing app. Wait for automatic discovery,
# without explicitly registering or enabling the extension in the test.
for attempt in {1..30}; do
    if pluginkit -mAvv -i com.mavinokta.player.mxf-thumbnail | grep -F "$ext" >/dev/null; then break; fi
    sleep 1
done
pluginkit -mAvv -i com.mavinokta.player.mxf-thumbnail | grep -F "$ext"
swiftc tests/mxf-thumbnail.swift -o "$work/check"
for color in red blue; do
    clip="$work/Çekim $color.MXF"
    ffmpeg -nostdin -hide_banner -loglevel error -f lavfi -i "color=$color:s=640x360:r=25" \
        -f lavfi -i "color=green:s=640x360:r=25" \
        -filter_complex '[0:v]trim=end_frame=1[a];[1:v]trim=end_frame=24[b];[a][b]concat=n=2:v=1:a=0' \
        -c:v mpeg2video -threads 1 -bf 2 -f mxf "$clip"
    "$work/check" "$clip" "$color"
done
