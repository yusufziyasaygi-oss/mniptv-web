#!/bin/bash
set -euo pipefail
app="/Applications/MN Player.app"
ext="$app/Contents/PlugIns/MXFFirstFrame.appex"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
# Install the previously shipped package, then upgrade using the actual product installer.
old="$work/previous.pkg"
curl -fsSL --retry 3 'https://raw.githubusercontent.com/yusufziyasaygi-oss/mniptv-web/1d8010deb423d0d1392c75266adb5a13d44b131e/MNPlayer-2.2.5-AppleSilicon.pkg' -o "$old"
echo "2c1a112c699c7571c63e4ee7ed32d561b1468322e0a608a1c4d421e803812dd5  $old" | shasum -a 256 -c -
sudo installer -pkg "$old" -target /
version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' desktop/Info.plist)
sudo installer -pkg "out/MNPlayer-$version-AppleSilicon.pkg" -target /
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$ext/Contents/Info.plist")" = "$version"
codesign --verify --deep --strict "$app"
for attempt in {1..30}; do
    if pluginkit -mAvv -i com.mavinokta.player.mxf-thumbnail | grep -F "$ext" >/dev/null; then break; fi
    sleep 1
done
plugins=$(pluginkit -mAvv -i com.mavinokta.player.mxf-thumbnail)
echo "$plugins"
grep -F "$ext" <<< "$plugins"
swiftc tests/mxf-thumbnail.swift -o "$work/check"
for color in red blue; do
    clip="$work/Çekim $color.MXF"
    ffmpeg -nostdin -hide_banner -loglevel error -f lavfi -i "color=$color:s=640x360:r=25" \
        -f lavfi -i "color=green:s=640x360:r=25" \
        -filter_complex '[0:v]trim=end_frame=1[a];[1:v]trim=end_frame=24[b];[a][b]concat=n=2:v=1:a=0' \
        -c:v mpeg2video -threads 1 -bf 2 -f mxf "$clip"
    if ! "$work/check" "$clip" "$color"; then
        mdls -name kMDItemContentType "$clip" || true
        for png in "${clip%.*}.png" "$HOME"/Library/Containers/com.mavinokta.player.mxf-thumbnail/Data/tmp/MXFReplies/*.png; do
            if [ -f "$png" ]; then
                echo "Average RGB and alpha in $png"
                ffmpeg -v error -i "$png" -vf scale=1:1 -frames:v 1 -pix_fmt rgba -f rawvideo - | od -An -tu1
            fi
        done
        exit 1
    fi
done


# Camera-equivalent synthetic clip: 4K/50, AVC High 4:2:2 Intra, 10-bit, eight PCM tracks.
camera="$work/camera.MXF"
ffmpeg -nostdin -loglevel error -f lavfi -i color=red:s=3840x2160:r=50 \
    -f lavfi -i color=green:s=3840x2160:r=50 -f lavfi -i anullsrc=r=48000:cl=mono \
    -filter_complex '[0:v]trim=end_frame=1[a];[1:v]trim=end_frame=3[b];[a][b]concat=n=2:v=1:a=0[v]' \
    -map '[v]' -map 2:a -map 2:a -map 2:a -map 2:a -map 2:a -map 2:a -map 2:a -map 2:a \
    -c:v libx264 -threads 2 -pix_fmt yuv422p10le -g 1 -preset ultrafast -c:a pcm_s24le -t 0.08 -f mxf "$camera"
ffprobe -v error -select_streams v:0 -show_entries stream=profile,width,height,pix_fmt,r_frame_rate -of csv "$camera"
"$work/check" "$camera" red

if [ -n "${RUNNER_TEMP:-}" ]; then cp "$camera" "$RUNNER_TEMP/mxf-first-frame.MXF"; fi
