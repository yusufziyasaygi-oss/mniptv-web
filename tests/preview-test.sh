#!/bin/bash
# Drives the Finder preview the way a user does. Needs the runner's accessibility access for System Events.
set +e
APP="$PWD/MN Player.app"
PREV="$APP/Contents/Helpers/MN Preview.app/Contents/MacOS/MNPreview"
M="$PWD/media"
O="$PWD/out/test"
mkdir -p "$O"
title() { osascript -e "tell application \"System Events\" to get name of every window of (first process whose unix id is $1)" 2>&1; }
now() { python3 -c 'import time;print(int(time.time()*1000))'; }
wait_title() { # pid text timeout_s
  local end=$(( $(now) + $3*1000 ))
  while [ $(now) -lt $end ]; do title $1 | grep -q "$2" && return 0; sleep 0.1; done; return 1; }
check() { if [ "$2" = 0 ]; then echo "OK   $1" | tee -a "$O/checks.txt"; else echo "FAIL $1" | tee -a "$O/checks.txt"; fi; }
swiftc -O tests/notify.swift -o notify && swiftc -O tests/service.swift -o service

# Engine start time with and without the plugin cache
"$APP/Contents/MacOS/MNPlayer" --verify-media "$M/a-yatay.mp4" | grep ENGINE_START | sed 's/^/cache: /' > "$O/engine-start.txt"
MN_NO_PLUGIN_CACHE=1 "$APP/Contents/MacOS/MNPlayer" --verify-media "$M/a-yatay.mp4" | grep ENGINE_START | sed 's/^/no-cache: /' >> "$O/engine-start.txt"
ls -la "$APP/Contents/MacOS/plugins/" | grep -i "\.dat" >> "$O/engine-start.txt"

# 1. Main player plays a video
MN_LOG=1 "$APP/Contents/MacOS/MNPlayer" "$M/a-yatay.mp4" 2> "$O/main-stderr.txt" &
MAIN=$!
sleep 6

# 2. Cold preview start (as MN Finder launches it)
T0=$(now)
MN_LOG=1 "$PREV" --quick-look "$M/b-dikey.mov" 2> "$O/preview-stderr.txt" &
P=$!
wait_title $P "b-dikey.mov" 20; check "preview opens with file name as title" $?
echo "cold preview: $(( $(now) - T0 )) ms" >> "$O/timing.txt"
sleep 2
grep -q MAIN_PAUSED_FOR_PREVIEW "$O/main-stderr.txt"; check "main player paused when preview opened" $?
screencapture -x "$O/1-preview-dikey.png"

# 3. Down arrow -> next video in the same folder
osascript -e 'tell application "System Events" to key code 125'
wait_title $P "c-hevc.mkv" 8; check "down arrow -> next video in folder" $?
sleep 1
osascript -e 'tell application "System Events" to key code 126'
wait_title $P "b-dikey.mov" 8; check "up arrow -> previous video" $?
screencapture -x "$O/2-preview-back.png"

# 4. Esc hides the preview, process stays warm
osascript -e 'tell application "System Events" to key code 53'
sleep 1.5
W=$(title $P); echo "after esc windows: [$W]" >> "$O/timing.txt"
[ -z "$(echo "$W" | tr -d ' ,')" ]; check "esc closes the preview window" $?
kill -0 $P; check "preview process kept for the next Control+M" $?

# 5. Next Control+M reuses it (MN Finder posts this message)
T0=$(now)
./notify "$M/d-vp9.webm"
wait_title $P "d-vp9.webm" 10; check "second preview opens in the running process" $?
echo "warm preview: $(( $(now) - T0 )) ms" >> "$O/timing.txt"
screencapture -x "$O/3-preview-reused.png"
osascript -e 'tell application "System Events" to key code 53'
sleep 1

# 6. Real Services path (MN Finder), when the runner lets us register the service
lsregister=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
$lsregister -f "$APP" "$APP/Contents/Helpers/MN Finder.app" >/dev/null 2>&1
/System/Library/CoreServices/pbs -update >/dev/null 2>&1
sleep 2
./service "$M/e-mpeg2.ts" > "$O/service.txt" 2>&1
sleep 4
pgrep -fl MNFinder >> "$O/service.txt"
title $P >> "$O/service.txt"
screencapture -x "$O/4-service.png"

kill $P $MAIN 2>/dev/null
pkill -f MNFinder 2>/dev/null
pkill -f MNPreview 2>/dev/null
cat "$O/checks.txt" "$O/timing.txt" "$O/engine-start.txt"
exit 0
