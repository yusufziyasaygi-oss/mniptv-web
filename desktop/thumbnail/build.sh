#!/bin/sh
set -eu
base=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
content="$base/MN Player.app/Contents"
extension="$content/PlugIns/MXFFirstFrame.appex"
work="$base/.build/mxf-thumbnail"
prefix="$work/libs"
version=7.1.5
archive="ffmpeg-$version.tar.xz"
digest=c0649ef9edd4176b90459e92b636be18077ca898f24fe1a8dc60f3c5fd58cf718db9720bbe804c6f6894453e21c0625d706ea37d3523fcb40d61779763d7d965
export MACOSX_DEPLOYMENT_TARGET=13.0
mkdir -p "$work" "$extension/Contents/MacOS" "$extension/Contents/Frameworks"
cd "$work"
if [ ! -f "$archive" ]; then
    curl --fail --location --retry 3 "https://ffmpeg.org/releases/$archive" -o "$archive.tmp"
    mv "$archive.tmp" "$archive"
fi
echo "$digest  $archive" | shasum -a 512 -c -
if [ ! -d "ffmpeg-$version" ]; then tar xf "$archive"; fi
cd "ffmpeg-$version"
./configure --prefix="$prefix" --install-name-dir=@rpath --arch=arm64 --cc=clang \
    --disable-autodetect --disable-static --enable-shared --disable-programs --disable-doc \
    --disable-network --disable-everything --disable-avdevice --disable-avfilter --disable-swresample \
    --enable-avformat --enable-avcodec --enable-swscale --enable-protocol=file --enable-demuxer=mxf \
    --enable-decoder=mpeg2video,mpeg4,h264,hevc,dnxhd,prores,dvvideo,jpeg2000,mjpeg,rawvideo,v210 \
    --enable-parser=mpegvideo,mpeg4video,h264,hevc,dnxhd,dvvideo,mjpeg,jpeg2000
make -j"$(sysctl -n hw.logicalcpu)"
make install
# Load only bundled libraries. No Homebrew paths or separate codec installation.
for lib in avformat avcodec avutil swscale; do
    dylib="$prefix/lib/lib${lib}.dylib"
    installName=$(otool -D "$dylib" | tail -1)
    target="$extension/Contents/Frameworks/${installName##*/}"
    cp -L "$dylib" "$target"
    codesign --force --sign - "$target"
done
cp COPYING.LGPLv2.1 "$content/Resources/Licenses/FFmpeg-LGPL-2.1.txt"
printf 'FFmpeg %s, source: https://ffmpeg.org/releases/%s\n' "$version" "$archive" > "$content/Resources/Licenses/FFmpeg-source.txt"
cp "$base/desktop/thumbnail/Info.plist" "$extension/Contents/Info.plist"
clang -arch arm64 -fobjc-arc -fapplication-extension -O2 \
    -I"$prefix/include" -L"$prefix/lib" -lavformat -lavcodec -lavutil -lswscale \
    -framework Foundation -framework CoreGraphics -framework ImageIO -framework QuickLookThumbnailing \
    -Wl,-e,_NSExtensionMain -Wl,-rpath,@executable_path/../Frameworks \
    "$base/desktop/thumbnail/Provider.m" -o "$extension/Contents/MacOS/MXFFirstFrame"
codesign --force --sign - --entitlements "$base/desktop/thumbnail/Entitlements.plist" "$extension"
