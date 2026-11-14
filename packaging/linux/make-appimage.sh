#!/usr/bin/env bash
# IP-TRX-CTRL - AppImage (un solo file, gira su quasi tutte le distribuzioni Linux con glibc).
# Si aggiorna da sola: il programma sostituisce il file .AppImage con quello della versione nuova.
# Hamlib (rigctld) e le librerie audio (ALSA) sono quelle del sistema.
# Uso: packaging/linux/make-appimage.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$3"; OUT="$4"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
[ "$(uname -m)" = "$ARCH" ] || { echo "l'AppImage $ARCH si costruisce su un runner $ARCH"; exit 1; }
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
A="$(mktemp -d)/IP-TRX-CTRL.AppDir"
mkdir -p "$A/usr/bin" "$A/usr/share/icons/hicolor/256x256/apps" "$A/usr/share/doc/ip-trx-ctrl"
install -m 755 "$SRC/IP-TRX-CTRL" "$A/usr/bin/IP-TRX-CTRL"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$ROOT/LICENSE" "$A/usr/share/doc/ip-trx-ctrl/"
cp "$ROOT/src/IpTrxCtrl/Assets/icon.png" "$A/ip-trx-ctrl.png"
cp "$ROOT/src/IpTrxCtrl/Assets/icon.png" "$A/usr/share/icons/hicolor/256x256/apps/ip-trx-ctrl.png"
cat > "$A/ip-trx-ctrl.desktop" <<'DESK'
[Desktop Entry]
Type=Application
Name=IP-TRX-CTRL
Comment=Comando a distanza della radio via internet: audio, CAT, PTT, keyer, CW
Exec=IP-TRX-CTRL
Icon=ip-trx-ctrl
Terminal=false
Categories=HamRadio;Network;AudioVideo;
DESK
cat > "$A/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/IP-TRX-CTRL" "$@"
RUN
chmod 755 "$A/AppRun"
T="$(mktemp -d)"
curl -fsSL -o "$T/appimagetool" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${ARCH}.AppImage"
chmod +x "$T/appimagetool"
mkdir -p "$OUT"
ARCH=$ARCH "$T/appimagetool" --appimage-extract-and-run "$A" "$OUT/IP-TRX-CTRL-${VERSION}-${ARCH}.AppImage"
ls -la "$OUT/IP-TRX-CTRL-${VERSION}-${ARCH}.AppImage"
