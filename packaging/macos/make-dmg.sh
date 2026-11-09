#!/usr/bin/env bash
# Crea IP-TRX-CTRL.app e il .dmg per macOS.
# Uso: packaging/macos/make-dmg.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$3"; OUT="$4"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
APP="$WORK/dmg/IP-TRX-CTRL.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$OUT"

cp "$SRC/IP-TRX-CTRL" "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/IP-TRX-CTRL"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$APP/Contents/Resources/"

# icona .icns
ICONSET="$WORK/icon.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ROOT/packaging/icon-1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d "$ROOT/packaging/icon-1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/IP-TRX-CTRL.icns"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>IP-TRX-CTRL</string>
  <key>CFBundleDisplayName</key><string>IP-TRX-CTRL</string>
  <key>CFBundleIdentifier</key><string>it.ic8tem.iptrxctrl</string>
  <key>CFBundleExecutable</key><string>IP-TRX-CTRL</string>
  <key>CFBundleIconFile</key><string>IP-TRX-CTRL</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSMicrophoneUsageDescription</key><string>IP-TRX-CTRL usa il microfono per trasmettere la tua voce alla radio remota.</string>
</dict>
</plist>
EOF

codesign --force --deep --sign - "$APP"
ln -s /Applications "$WORK/dmg/Applicazioni"
hdiutil create -volname "IP-TRX-CTRL" -srcfolder "$WORK/dmg" -ov -format UDZO "$OUT/IP-TRX-CTRL-${VERSION}-${RID}.dmg"
ditto -c -k --keepParent "$APP" "$OUT/IP-TRX-CTRL-${VERSION}-${RID}-app.zip"
