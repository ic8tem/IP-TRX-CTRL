#!/usr/bin/env bash
# IP-TRX-CTRL - pacchetto Arch Linux / Manjaro / EndeavourOS / Arch Linux ARM (.pkg.tar.zst), ricavato dal .deb
# dello stesso rid (stessi file). Si costruisce con makepkg in un contenitore Docker archlinux (si impacchettano
# file già compilati, quindi l'architettura del contenitore non conta).
# Si installa con: sudo pacman -U ip-trx-ctrl-<versione>-1-<arch>.pkg.tar.zst
# Uso: packaging/linux/make-arch.sh <rid> <versione> <file .deb> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; DEB="$3"; OUT="$4"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
W="$(mktemp -d)"
mkdir -p "$W/albero"
dpkg-deb -x "$DEB" "$W/albero"
echo arch > "$W/albero/opt/ip-trx-ctrl/PACCHETTO"
PV="${VERSION//-/_}"
cat > "$W/PKGBUILD" <<PKG
pkgname=ip-trx-ctrl
pkgver=${PV}
pkgrel=1
pkgdesc="Comando a distanza di una radio HF via internet: audio, CAT, PTT, keyer, CW"
arch=('${ARCH}')
url="https://github.com/ic8tem/IP-TRX-CTRL"
license=('GPL-3.0-or-later')
depends=('fontconfig' 'libx11' 'libice' 'libsm' 'alsa-lib' 'hamlib')
optdepends=('alsa-utils: prova delle schede audio' 'libpulse: cavi audio virtuali per i programmi digitali')
options=('!strip' '!debug')
package() {
  cp -a "\$startdir/albero/." "\$pkgdir/"
}
PKG
# il contenitore archlinux esiste solo per x86_64: su un runner ARM gira emulato con qemu
if [ "$(uname -m)" != "x86_64" ]; then docker run --privileged --rm tonistiigi/binfmt --install amd64 >/dev/null; fi
docker run --rm --platform linux/amd64 -v "$W:/w" archlinux:latest bash -c "
  set -e
  pacman --disable-sandbox -Sy --noconfirm --needed base-devel >/dev/null
  sed -i 's/^CARCH=.*/CARCH=\"${ARCH}\"/' /etc/makepkg.conf
  useradd -m b && chown -R b /w
  su b -c 'cd /w && makepkg --nodeps --skipinteg -f'
  chmod -R a+rwX /w
"
mkdir -p "$OUT"
cp "$W"/ip-trx-ctrl-*.pkg.tar.zst "$OUT/"
ls -la "$OUT"/ip-trx-ctrl-*.pkg.tar.zst
