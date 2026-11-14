#!/usr/bin/env bash
# IP-TRX-CTRL - pacchetto Slackware (.txz), ricavato dal .deb dello stesso rid (stessi file).
# Si installa da root con: installpkg ip-trx-ctrl-<versione>-<arch>-1_ic8tem.txz (aggiornamento: upgradepkg).
# Hamlib (rigctld) va installato a parte, per esempio da SlackBuilds.org.
# Uso: packaging/linux/make-txz.sh <rid> <versione> <file .deb> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; DEB="$3"; OUT="$4"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
T="$(mktemp -d)"
dpkg-deb -x "$DEB" "$T"
echo slackware > "$T/opt/ip-trx-ctrl/PACCHETTO"
mkdir -p "$T/install"
# slack-desc: 11 righe che iniziano con il nome del pacchetto
cat > "$T/install/slack-desc" <<'DESC'
           |-----handy-ruler------------------------------------------------------|
ip-trx-ctrl: ip-trx-ctrl (comando a distanza di una radio HF via internet)
ip-trx-ctrl:
ip-trx-ctrl: Audio bidirezionale cifrato AES-256, controllo CAT con Hamlib, PTT,
ip-trx-ctrl: keyer con messaggi WAV e macro CW, cascata e decodificatore CW,
ip-trx-ctrl: split, potenza TX, panadapter, accesso dal browser.
ip-trx-ctrl: Serve Hamlib (rigctld), per esempio da SlackBuilds.org.
ip-trx-ctrl:
ip-trx-ctrl: https://github.com/ic8tem/IP-TRX-CTRL
ip-trx-ctrl:
ip-trx-ctrl:
ip-trx-ctrl:
DESC
cat > "$T/install/doinst.sh" <<'INST'
if [ -x /usr/bin/update-desktop-database ]; then /usr/bin/update-desktop-database -q usr/share/applications >/dev/null 2>&1; fi
if [ -x /usr/bin/gtk-update-icon-cache ]; then /usr/bin/gtk-update-icon-cache -q usr/share/icons/hicolor >/dev/null 2>&1; fi
INST
SV="${VERSION//-/_}"
mkdir -p "$OUT"
tar -C "$T" --owner=0 --group=0 -cJf "$OUT/ip-trx-ctrl-${SV}-${ARCH}-1_ic8tem.txz" install opt usr
ls -la "$OUT/ip-trx-ctrl-${SV}-${ARCH}-1_ic8tem.txz"
