#!/bin/bash
# TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
# Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
#
# Fabrique le paquet .deb et l'AppImage de TwinPanel, pour Linux 64 bits (x86-64).
# À lancer sous Linux, à la racine du projet :  ./build-linux.sh [version]
# Sans version, celle de Directory.Build.props est utilisée. Résultat dans dist/linux/.
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")"

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)}"
# Pour dpkg, « ~ » classe une bêta avant la version finale.
DEB_VERSION="${VERSION/-/\~}"
OUT=dist/linux
APP="$OUT/app"
ASSETS=src/TwinPanel.App/Assets

echo "TwinPanel $VERSION"
rm -rf "$OUT/app" "$OUT/deb" "$OUT/AppDir"
mkdir -p "$OUT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
"$DOTNET" publish src/TwinPanel.App/TwinPanel.App.csproj -c Release -r linux-x64 --self-contained true \
    -p:Version="$VERSION" -o "$APP" --nologo -v quiet
# Les symboles de débogage ne servent pas aux utilisateurs, et rien de personnel ne doit partir.
find "$APP" -name '*.pdb' -delete
test -x "$APP/TwinPanel"

desktop_file() { # $1 : commande à lancer
    cat <<EOF
[Desktop Entry]
Type=Application
Name=TwinPanel
GenericName=File Manager
GenericName[fr]=Gestionnaire de fichiers
Comment=Two-panel file manager
Comment[fr]=Gestionnaire de fichiers à deux panneaux
Exec=$1
Icon=twinpanel
Terminal=false
Categories=System;FileTools;FileManager;
StartupWMClass=TwinPanel
EOF
}

# ---- Paquet .deb ----------------------------------------------------------------------------
DEB="$OUT/deb"
mkdir -p "$DEB/DEBIAN" "$DEB/usr/lib/twinpanel" "$DEB/usr/bin" "$DEB/usr/share/applications" "$DEB/usr/share/doc/twinpanel"
cp -r "$APP/." "$DEB/usr/lib/twinpanel/"
ln -s ../lib/twinpanel/TwinPanel "$DEB/usr/bin/twinpanel"
desktop_file twinpanel > "$DEB/usr/share/applications/twinpanel.desktop"
for size in 64 256; do
    mkdir -p "$DEB/usr/share/icons/hicolor/${size}x${size}/apps"
    cp "$ASSETS/twinpanel-$size.png" "$DEB/usr/share/icons/hicolor/${size}x${size}/apps/twinpanel.png"
done
cp LICENSE "$DEB/usr/share/doc/twinpanel/copyright"
cp THIRD-PARTY-NOTICES.md "$DEB/usr/share/doc/twinpanel/"

find "$DEB" -type d -exec chmod 755 {} +
find "$DEB" -type f -exec chmod 644 {} +
chmod 755 "$DEB/usr/lib/twinpanel/TwinPanel"
find "$DEB/usr/lib/twinpanel" -name '*.so*' -exec chmod 755 {} +

cat > "$DEB/DEBIAN/control" <<EOF
Package: twinpanel
Version: $DEB_VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Georges Christophe (chrypox) <admin@chrypox.be>
Installed-Size: $(du -sk "$DEB/usr" | cut -f1)
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libssl3t64 | libssl3, libicu76 | libicu74 | libicu72, libfontconfig1, libx11-6, libice6, libsm6, libxcursor1, libxrandr2, libxi6
Recommends: libsecret-tools, xdg-utils
Suggests: cups-client
Homepage: https://github.com/georgeschris01/TwinPanel
Description: Two-panel file manager (beta for Linux)
 TwinPanel is a two-panel file manager with tabs, archives, SFTP, FTP and
 WebDAV connections, an integrated terminal, search and batch renaming.
EOF

dpkg-deb --root-owner-group -Zxz --build "$DEB" "$OUT/twinpanel_${VERSION}_amd64.deb" >/dev/null

# ---- AppImage -------------------------------------------------------------------------------
DIR="$OUT/AppDir"
mkdir -p "$DIR/usr/lib/twinpanel"
cp -r "$APP/." "$DIR/usr/lib/twinpanel/"
desktop_file TwinPanel > "$DIR/twinpanel.desktop"
cp "$ASSETS/twinpanel-256.png" "$DIR/twinpanel.png"
cp LICENSE THIRD-PARTY-NOTICES.md "$DIR/usr/lib/twinpanel/"
cat > "$DIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/twinpanel/TwinPanel" "$@"
EOF
chmod 755 "$DIR/AppRun"
chmod -R a+rX "$DIR"

# L'outil n'est jamais téléchargé par ce script : il doit déjà être là, installé en connaissance de cause.
TOOL="${APPIMAGETOOL:-$(command -v appimagetool || true)}"
if [ -n "$TOOL" ]; then
    ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream "$DIR" "$OUT/TwinPanel-$VERSION-x86_64.AppImage" >/dev/null 2>"$OUT/appimage.log" \
        || { cat "$OUT/appimage.log"; exit 1; }
else
    echo "AppImage non fabriquée : appimagetool est absent (variable APPIMAGETOOL ou commande appimagetool)."
fi

echo
ls -lh "$OUT"/twinpanel_*.deb "$OUT"/*.AppImage 2>/dev/null || true
sha256sum "$OUT"/twinpanel_*.deb "$OUT"/*.AppImage 2>/dev/null || true
