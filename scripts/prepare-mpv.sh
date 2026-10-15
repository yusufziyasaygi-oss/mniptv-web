#!/bin/bash
set -euo pipefail
# Build-time dependency only. Finished .app contains libmpv and its dependency closure.
if [[ -n "${MAVI_MPV_PREFIX:-}" ]]; then
  [[ -f "$MAVI_MPV_PREFIX/include/mpv/client.h" && -e "$MAVI_MPV_PREFIX/lib/libmpv.dylib" ]] || { echo 'MAVI_MPV_PREFIX does not contain libmpv headers/library.' >&2; exit 1; }
  printf '%s\n' "$MAVI_MPV_PREFIX"
  exit 0
fi
BREW_BIN="$(command -v brew || true)"
if [[ -z "$BREW_BIN" && -x /opt/homebrew/bin/brew ]]; then BREW_BIN=/opt/homebrew/bin/brew; fi
if [[ -z "$BREW_BIN" && -x /usr/local/bin/brew ]]; then BREW_BIN=/usr/local/bin/brew; fi
if [[ -z "$BREW_BIN" ]]; then
  echo 'İlk derleme için Homebrew gerekiyor: https://brew.sh — kurulumdan sonra Olustur.command dosyasını yeniden aç.' >&2
  echo 'Uygulamayı kullanacak kişiler için Homebrew veya ayrı MPV kurulumu gerekmez.' >&2
  exit 1
fi
if ! "$BREW_BIN" list --versions mpv >/dev/null 2>&1; then
  echo 'Yerleşik oynatıcı derleme bileşenleri hazırlanıyor…' >&2
  "$BREW_BIN" install mpv >&2
fi
"$BREW_BIN" --prefix mpv
