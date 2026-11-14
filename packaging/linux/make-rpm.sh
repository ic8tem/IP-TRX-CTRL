#!/usr/bin/env bash
# IP-TRX-CTRL - pacchetto .rpm (Fedora, openSUSE, RHEL e derivate), ricavato dal .deb dello stesso rid:
# stessi file (/opt/ip-trx-ctrl, /usr/bin/ip-trx-ctrl, icona e voce di menu). Serve rpmbuild (pacchetto «rpm» su Ubuntu).
# Hamlib (rigctld) è consigliato e non obbligatorio: su openSUSE sta nel repository hamradio.
# Uso: packaging/linux/make-rpm.sh <rid> <versione> <file .deb> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; DEB="$3"; OUT="$4"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  linux-arm)   ARCH=armv7hl ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
TOP="$(mktemp -d)"
TREE="$TOP/albero"
mkdir -p "$TREE" "$TOP/SPECS" "$TOP/RPMS"
dpkg-deb -x "$DEB" "$TREE"
echo rpm > "$TREE/opt/ip-trx-ctrl/PACCHETTO"
RV="${VERSION//-/_}"   # la versione rpm non accetta il trattino

cat > "$TOP/SPECS/ip-trx-ctrl.spec" <<SPEC
Name:           ip-trx-ctrl
Version:        ${RV}
Release:        1
Summary:        Comando a distanza di una radio HF via internet
License:        GPL-3.0-or-later
URL:            https://github.com/ic8tem/IP-TRX-CTRL
Packager:       ic8tem <ic8tem@users.noreply.github.com>
AutoReqProv:    no
Requires:       fontconfig, libX11, libICE, libSM, alsa-lib
Recommends:     hamlib, alsa-utils
%global debug_package %{nil}
%global __strip /bin/true
%global __os_install_post %{nil}
%global _build_id_links none

%description
Audio bidirezionale a 8 kHz cifrato AES-256, controllo CAT con Hamlib,
PTT (CAT, RTS, DTR, VOX, TCI), keyer con messaggi WAV e macro CW,
cascata e decodificatore CW, VFO B con split, potenza TX, panadapter,
accesso dal browser. Per la porta seriale della radio: usermod -aG dialout.

%install
mkdir -p %{buildroot}
cp -a ${TREE}/. %{buildroot}/
rm -rf %{buildroot}/DEBIAN

%post
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
exit 0

%files
/opt/ip-trx-ctrl
/usr/bin/ip-trx-ctrl
/usr/share/applications/ip-trx-ctrl.desktop
/usr/share/icons/hicolor/256x256/apps/ip-trx-ctrl.png
%doc /usr/share/doc/ip-trx-ctrl
SPEC

rpmbuild --define "_topdir $TOP" --target "$ARCH" -bb "$TOP/SPECS/ip-trx-ctrl.spec" >/dev/null
mkdir -p "$OUT"
cp "$TOP"/RPMS/*/ip-trx-ctrl-*.rpm "$OUT/ip-trx-ctrl-${VERSION}.${ARCH}.rpm"
rpm -qpi "$OUT/ip-trx-ctrl-${VERSION}.${ARCH}.rpm" | head -8 || true
