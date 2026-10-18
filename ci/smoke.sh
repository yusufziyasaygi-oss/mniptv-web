#!/usr/bin/env bash
# CI smoke test: drives the app on an Android TV emulator with remote-control key events
# against ci/mock_xtream.py, saving a screenshot and the foreground activity for every step.
set -x
OUT="$PWD/smoke"
mkdir -p "$OUT/shots"
PKG=com.mavinokta.iptv.tv
BT="$ANDROID_HOME/build-tools/35.0.0"
APK=app/build/outputs/apk/release/app-release-unsigned.apk

step() {
  local name="$1"
  adb exec-out screencap -p > "$OUT/shots/$name.png"
  local top
  top=$(adb shell dumpsys activity activities | grep -m1 -E "topResumedActivity|mResumedActivity" | sed 's/^ *//')
  if adb shell pidof "$PKG" >/dev/null; then
    echo "$name | ALIVE | $top" >> "$OUT/steps.txt"
  else
    echo "$name | DEAD | $top" >> "$OUT/steps.txt"
  fi
}
# UI text assertions (uiautomator dump), results in asserts.txt
ui_has() { adb shell uiautomator dump /sdcard/ui.xml >/dev/null 2>&1; adb shell cat /sdcard/ui.xml | grep -qF "$1"; }
check() {
  if ui_has "$2"; then echo "PASS $1" >> "$OUT/asserts.txt"; else echo "FAIL $1 [$2]" >> "$OUT/asserts.txt"; fi
}
key() { for k in "$@"; do adb shell input keyevent "$k"; sleep 0.7; done; }
longok() { adb shell input keyevent --longpress KEYCODE_DPAD_CENTER; sleep 1; }
top_activity() { adb shell dumpsys activity activities | grep -m1 -E "topResumedActivity|mResumedActivity"; }
# Leave player/series screens until the catalogue is in front.
to_main() {
  for i in 1 2 3 4 5; do
    if top_activity | grep -q "ui.MainActivity"; then return; fi
    adb shell input keyevent KEYCODE_BACK; sleep 1.5
  done
}
# Deterministic menu selection: far left, top, then N rows down, then OK.
menu() {
  to_main
  key KEYCODE_DPAD_LEFT KEYCODE_DPAD_LEFT KEYCODE_DPAD_LEFT KEYCODE_DPAD_LEFT
  key KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP
  local n=$1
  while [ "$n" -gt 0 ]; do key KEYCODE_DPAD_DOWN; n=$((n-1)); done
  sleep 1
  key KEYCODE_DPAD_CENTER
  sleep 3
}

hide_ime() {
  if adb shell dumpsys input_method | grep -q "mInputShown=true"; then
    adb shell input keyevent KEYCODE_BACK
    sleep 1
  fi
}

python3 -m pip install -q cryptography >/dev/null 2>&1 || pip3 install -q --break-system-packages cryptography >/dev/null 2>&1
python3 ci/mock_xtream.py > "$OUT/server.log" 2>&1 &
sleep 2
curl -s "http://127.0.0.1:8080/player_api.php?username=u&password=p" > "$OUT/server_check.txt"

keytool -genkeypair -keystore "$OUT/ci.jks" -storepass android -keypass android -alias ci \
  -keyalg RSA -keysize 2048 -validity 1000 -dname CN=ci >/dev/null 2>&1
"$BT/apksigner" sign --ks "$OUT/ci.jks" --ks-pass pass:android --key-pass pass:android \
  --out "$OUT/test.apk" "$APK"

adb shell settings put secure show_ime_with_hard_keyboard 0
adb shell getprop ro.build.characteristics > "$OUT/device.txt"
adb shell wm size >> "$OUT/device.txt"
adb install -r "$OUT/test.apk" > "$OUT/install.txt" 2>&1
adb logcat -c

# 1. Login
adb shell am start -n "$PKG/.ui.MainActivity" --es sync_test_base http://10.0.2.2:8080
sleep 5
step 01-login
adb shell input text "10.0.2.2:8080"; sleep 1; hide_ime
key KEYCODE_DPAD_DOWN
adb shell input text "u"; sleep 1; hide_ime
key KEYCODE_DPAD_DOWN
adb shell input text "p"; sleep 1; hide_ime
step 02-login-filled
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
sleep 6
step 03-home-live

# 2. Live TV (menu index 1)
menu 1
step 04-live-categories-focus
key KEYCODE_DPAD_RIGHT
sleep 1
step 05-live-grid-focus
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_RIGHT
step 06-live-grid-moved
key KEYCODE_DPAD_CENTER
sleep 7
step 07-live-playing
key KEYCODE_DPAD_DOWN
sleep 2
step 08a-live-info-next
sleep 4
step 08-live-zap-next
key KEYCODE_DPAD_LEFT
sleep 5
step 09-live-previous
key KEYCODE_DPAD_CENTER
sleep 2
step 10-live-channel-panel
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
sleep 5
step 11-live-panel-picked
key KEYCODE_1 KEYCODE_2
sleep 3
step 12-live-number-12
longok
step 13-live-options
key KEYCODE_DPAD_CENTER
sleep 2
step 14-live-favorited
longok
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
sleep 1
step 14a-sleep-dialog
check sleep-dialog "Uyku zamanlayıcısı"
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
sleep 0.5
step 14b-sleep-set
to_main
sleep 1
step 15-back-home
# Recently watched channels appear as a live category
menu 1
sleep 1
step 15a-live-categories-recent
check recent-category "Son İzlenenler"
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN
sleep 2
step 15b-live-recent-grid
check recent-grid-count "Son İzlenenler · "
key KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP
sleep 1.5

# 3. Movies (menu index 2)
menu 2
step 16-movies-categories
key KEYCODE_DPAD_RIGHT
sleep 1
step 17-movies-grid
key KEYCODE_DPAD_CENTER
sleep 25
step 18-movie-playing
key KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT
step 19-movie-seek
key KEYCODE_DPAD_CENTER
sleep 1
step 20-movie-paused
key KEYCODE_DPAD_CENTER
sleep 3
to_main
sleep 1
step 21-after-movie

# 4. Continue watching (menu index 0)
menu 0
step 22-continue-watching
key KEYCODE_DPAD_CENTER
sleep 8
step 23-continue-resumed
to_main

# 5. Series (menu index 3)
menu 3
step 24-series-categories
key KEYCODE_DPAD_RIGHT
sleep 1
step 25-series-grid
key KEYCODE_DPAD_CENTER
sleep 4
step 26-series-detail
key KEYCODE_DPAD_CENTER
sleep 22
step 27-episode-playing
# playback speed from the options dialog (video/toast are checked on the screenshots)
longok
step 27a-episode-options
check episode-options-speed "Oynatma hızı: 1x"
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_CENTER
sleep 0.5
step 27b-episode-speed
key KEYCODE_BACK
sleep 3
step 28-series-detail-resume
key KEYCODE_DPAD_UP
step 29a-series-season-row
key KEYCODE_DPAD_RIGHT
sleep 2
step 29-series-season-2
key KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT
step 29b-series-right-edge-stays
key KEYCODE_DPAD_DOWN
sleep 1
step 30-series-episode-focus
key KEYCODE_DPAD_UP KEYCODE_DPAD_UP
step 30b-series-buttons
to_main

# 6. Series with empty episode list must not crash (last tile: Dizi 8)
menu 3
key KEYCODE_DPAD_RIGHT
key KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT KEYCODE_DPAD_RIGHT
key KEYCODE_DPAD_DOWN KEYCODE_DPAD_DOWN
step 31-series-last-tile
key KEYCODE_DPAD_CENTER
sleep 4
step 32-series-empty
to_main

# 7. Favourite a movie with long press, then favourites (menu index 4)
menu 2
key KEYCODE_DPAD_RIGHT
longok
step 33-movie-favorited
menu 4
step 34-favorites

# 7b. Slow panel (before search, which caches the whole live list): one list download at a time, "Spor" answers in 10 s. Leaving "Spor" for "Tümü"
# must abort the Spor download, so "Tümü" shows within a few seconds.
curl -s "http://127.0.0.1:8080/__slow?on=1" >> "$OUT/server_check.txt"
menu 1
key KEYCODE_DPAD_DOWN
sleep 1.5
key KEYCODE_DPAD_DOWN
sleep 4
step 40-slow-switch-all
key KEYCODE_DPAD_UP
sleep 8.5
step 41-slow-hint
sleep 4
step 42-slow-loaded
curl -s "http://127.0.0.1:8080/__slow?on=0" >> "$OUT/server_check.txt"

# 8. Search (menu index 5)
menu 5
adb shell input text "kanal%s1"
sleep 1; hide_ime
sleep 5
step 35-search-results
key KEYCODE_DPAD_DOWN
step 36-search-focus
key KEYCODE_DPAD_CENTER
sleep 6
step 37-search-play
to_main

# 9. Account dialog (menu index 6)
menu 6
step 38-account
key KEYCODE_BACK
sleep 1
step 39-final

# 11. Cross-device sync: this device's changes must be on the (mock) GitHub API, encrypted the
# way the reference implementation expects; then "another device" adds a movie position and a
# favourite, and the app has to show them after coming back to the front.
sleep 50
python3 ci/sync_tool.py http://127.0.0.1:8080 http://10.0.2.2:8080 u p read "$OUT/sync_read.json" > /dev/null 2>&1
python3 ci/sync_tool.py http://127.0.0.1:8080 http://10.0.2.2:8080 u p inject "$OUT/sync_inject.json" > /dev/null 2>&1
adb shell input keyevent KEYCODE_HOME
sleep 3
adb shell am start -n "$PKG/.ui.MainActivity"
sleep 6
menu 0
sleep 1
step 47-sync-continue
check sync-continue "Film 5"
menu 4
sleep 1
step 48-sync-favorites
check sync-favorite "Dizi 2"
curl -s http://127.0.0.1:8080/__synclog > "$OUT/sync_log.json"

# 10. Starts where it was left (Filmler / Komedi), then again with the panel switched off:
# the lists must come from the disk cache.
menu 2
key KEYCODE_DPAD_DOWN
sleep 2
step 43-before-restart
check category-count "Komedi · 10"
adb shell am force-stop "$PKG"
sleep 1
adb shell am start -n "$PKG/.ui.MainActivity"
sleep 5
step 44-restart-online
check restart-section "Komedi · 10"
adb shell am force-stop "$PKG"
pkill -f mock_xtream.py
sleep 2
adb shell am start -n "$PKG/.ui.MainActivity"
sleep 6
step 45-restart-offline
check offline-lists "Komedi · 10"
key KEYCODE_DPAD_LEFT KEYCODE_DPAD_LEFT
key KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP KEYCODE_DPAD_UP
key KEYCODE_DPAD_DOWN
sleep 3
step 46-offline-live
check offline-live "Kanal 1 HD"

adb logcat -d > "$OUT/logcat.txt"
grep -n -A40 "FATAL EXCEPTION" "$OUT/logcat.txt" > "$OUT/crash.txt" || true
grep -n -E "ExoPlayerImpl.*Playback error|PlaybackException" "$OUT/logcat.txt" | head -50 > "$OUT/playback_errors.txt" || true
echo done > "$OUT/finished.txt"
