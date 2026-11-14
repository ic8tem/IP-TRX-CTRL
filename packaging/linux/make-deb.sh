#!/usr/bin/env bash
# Crea il pacchetto .deb di IP-TRX-CTRL.
# Uso: packaging/linux/make-deb.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$3"; OUT="$4"
case "$RID" in
  linux-x64)   ARCH=amd64 ;;
  linux-arm64) ARCH=arm64 ;;
  linux-arm)   ARCH=armhf ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PKG="$(mktemp -d)/ip-trx-ctrl_${VERSION}_${ARCH}"

install -d "$PKG/DEBIAN" "$PKG/opt/ip-trx-ctrl" "$PKG/usr/bin" "$PKG/usr/share/applications" \
           "$PKG/usr/share/icons/hicolor/256x256/apps" "$PKG/usr/share/doc/ip-trx-ctrl"
install -m 755 "$SRC/IP-TRX-CTRL" "$PKG/opt/ip-trx-ctrl/IP-TRX-CTRL"
# segno del pacchetto di sistema: qui il programma non si aggiorna da solo (cartella di root), avvisa soltanto
echo deb > "$PKG/opt/ip-trx-ctrl/PACCHETTO"
chmod 644 "$PKG/opt/ip-trx-ctrl/PACCHETTO"
ln -s /opt/ip-trx-ctrl/IP-TRX-CTRL "$PKG/usr/bin/ip-trx-ctrl"
install -m 644 "$ROOT/src/IpTrxCtrl/Assets/icon.png" "$PKG/usr/share/icons/hicolor/256x256/apps/ip-trx-ctrl.png"
{ printf "IP-TRX-CTRL - Copyright (C) 2026 IC8TEM\nLicenza: GNU GPL 3.0 o successiva (/usr/share/common-licenses/GPL-3)\n\n"; cat "$ROOT/THIRD-PARTY-NOTICES.txt"; } > "$PKG/usr/share/doc/ip-trx-ctrl/copyright"
chmod 644 "$PKG/usr/share/doc/ip-trx-ctrl/copyright"
install -m 644 "$ROOT/README.md" "$PKG/usr/share/doc/ip-trx-ctrl/README.md"

cat > "$PKG/usr/share/applications/ip-trx-ctrl.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=IP-TRX-CTRL
Comment=Comando a distanza della radio via internet: audio, CAT, PTT, keyer, CW
Exec=/opt/ip-trx-ctrl/IP-TRX-CTRL
Icon=ip-trx-ctrl
Terminal=false
Categories=HamRadio;Network;AudioVideo;
EOF

SIZE=$(du -sk "$PKG" | cut -f1)
cat > "$PKG/DEBIAN/control" <<EOF
Package: ip-trx-ctrl
Version: ${VERSION}
Section: hamradio
Priority: optional
Architecture: ${ARCH}
Installed-Size: ${SIZE}
Depends: libhamlib-utils, libasound2t64 | libasound2, libfontconfig1, libx11-6, libice6, libsm6
Recommends: pulseaudio-utils | pipewire-pulse, alsa-utils
Maintainer: ic8tem <ic8tem@users.noreply.github.com>
Homepage: https://github.com/ic8tem/IP-TRX-CTRL
Description: comando a distanza di una radio HF via internet
 Audio bidirezionale a 8 kHz cifrato AES-256, controllo CAT con Hamlib,
 PTT (CAT, RTS, DTR, VOX, TCI), keyer con messaggi WAV e macro CW,
 cascata e decodificatore CW, VFO B con split, potenza TX.
EOF

cat > "$PKG/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
# accesso alle porte seriali della radio: aggiungi il tuo utente al gruppo dialout
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
exit 0
EOF
chmod 755 "$PKG/DEBIAN/postinst"

mkdir -p "$OUT"
dpkg-deb --root-owner-group --build "$PKG" "$OUT/ip-trx-ctrl_${VERSION}_${ARCH}.deb"
