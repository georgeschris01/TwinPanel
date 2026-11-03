#!/bin/bash
# TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
# Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
#
# Fabrique le paquet .deb et l'AppImage de TwinPanel, pour Linux 64 bits (x86-64).
# À lancer sous Linux, à la racine du projet :  ./build-linux.sh [version]
# Sans version, LinuxVersion de Directory.Build.props est utilisée. Résultat dans dist/linux/.
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")"

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
VERSION="${1:-$(sed -n 's:.*<LinuxVersion>\(.*\)</LinuxVersion>.*:\1:p' Directory.Build.props)}"
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
    -p:Version="$VERSION" -p:DebugType=none -p:PathMap="$PWD=." -o "$APP" --nologo -v quiet
# Rien de personnel ne doit partir : ni symboles de débogage, ni chemin de la machine de fabrication dans les programmes.
find "$APP" -name '*.pdb' -delete
# Les options avant « -- » : après lui, grep lit tout comme des noms de fichiers. Code 1 = rien trouvé ; autre chose = erreur.
set +e
grep -rlaF --include='TwinPanel*' -- "$PWD" "$APP" > /dev/null
found=$?
set -e
if [ "$found" -eq 0 ]; then echo "Chemin de fabrication trouvé dans les programmes."; exit 1; fi
if [ "$found" -ne 1 ]; then echo "Contrôle du chemin de fabrication impossible."; exit 1; fi
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

# Fiche AppStream : sans elle, les logithèques (Discover, GNOME Logiciels) ignorent l'application.
mkdir -p "$DEB/usr/share/metainfo"
# Les logithèques chargent les captures depuis internet : elles sont lues au tag de la version, qui ne bouge plus.
SHOTS=https://raw.githubusercontent.com/georgeschris01/TwinPanel/linux-v$VERSION/docs/screenshots
cat > "$DEB/usr/share/metainfo/be.chrypox.TwinPanel.metainfo.xml" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
  <id>be.chrypox.TwinPanel</id>
  <name>TwinPanel</name>
  <summary>Two-panel file manager</summary>
  <summary xml:lang="fr">Gestionnaire de fichiers à deux panneaux</summary>
  <metadata_license>CC0-1.0</metadata_license>
  <!-- « LicenseRef-proprietary » afficherait « code source fermé » dans Discover. Sans adresse : Discover montre la valeur telle quelle. -->
  <project_license>LicenseRef-TwinPanel-1.0</project_license>
  <developer id="be.chrypox">
    <name>Georges Christophe (chrypox)</name>
  </developer>
  <description>
    <p>TwinPanel is a two-panel file manager with tabs, archives, SFTP, FTP and WebDAV connections, an integrated terminal, search and batch renaming. The Linux version is a beta.</p>
    <p xml:lang="fr">TwinPanel est un gestionnaire de fichiers à deux panneaux, avec onglets, archives, connexions SFTP, FTP et WebDAV, terminal intégré, recherche et renommage par lot. La version Linux est une bêta.</p>
    <p>The source code is public. The TwinPanel 1.0 license allows free use, but forbids selling the software and distributing modified versions.</p>
    <p xml:lang="fr">Le code source est public. La licence TwinPanel 1.0 permet un usage gratuit, mais interdit de vendre le logiciel et de diffuser des versions modifiées.</p>
  </description>
  <!-- Discover relie la fiche au paquet par ce nom : sans lui, l'application n'apparaît pas. -->
  <pkgname>twinpanel</pkgname>
  <icon type="stock">twinpanel</icon>
  <launchable type="desktop-id">twinpanel.desktop</launchable>
  <url type="homepage">https://github.com/georgeschris01/TwinPanel</url>
  <url type="bugtracker">https://github.com/georgeschris01/TwinPanel/issues</url>
  <url type="vcs-browser">https://github.com/georgeschris01/TwinPanel</url>
  <categories>
    <category>System</category>
    <category>FileTools</category>
    <category>FileManager</category>
  </categories>
  <screenshots>
    <screenshot type="default">
      <caption>Two panels with the dark theme and the icons of the desktop</caption>
      <caption xml:lang="fr">Deux panneaux, thème sombre et icônes du bureau</caption>
      <image type="source" width="1280" height="800">$SHOTS/linux-fr.png</image>
    </screenshot>
    <screenshot>
      <caption>The light theme</caption>
      <caption xml:lang="fr">Le thème clair</caption>
      <image type="source" width="1280" height="800">$SHOTS/linux-clair-fr.png</image>
    </screenshot>
    <screenshot>
      <caption>The integrated terminal below the two panels</caption>
      <caption xml:lang="fr">Le terminal intégré sous les deux panneaux</caption>
      <image type="source" width="1280" height="800">$SHOTS/linux-terminal-fr.png</image>
    </screenshot>
    <screenshot>
      <caption>Several terminals side by side, with tabs on each side</caption>
      <caption xml:lang="fr">Plusieurs terminaux côte à côte, avec des onglets de chaque côté</caption>
      <image type="source" width="1280" height="800">$SHOTS/linux-terminaux-fr.png</image>
    </screenshot>
  </screenshots>
  <content_rating type="oars-1.1"/>
  <releases>
    <release version="$VERSION" date="$(date +%F)" type="development"/>
  </releases>
</component>
EOF

# Discover 6.3 ne sait pas afficher une icône « stock » (il compare un nom à une liste de chemins) : il lui faut un fichier
# désigné par un catalogue, comme pour les applications de Debian. La même fiche sert, avec ces icônes en plus.
CATALOG="$DEB/usr/share/swcatalog"
mkdir -p "$CATALOG/xml" "$CATALOG/icons/twinpanel/64x64"
cp "$ASSETS/twinpanel-64.png" "$CATALOG/icons/twinpanel/64x64/twinpanel.png"
{
    echo '<?xml version="1.0" encoding="UTF-8"?>'
    echo '<components version="1.0" origin="twinpanel">'
    sed -e '1d' -e 's#^  <launchable #  <icon type="cached" width="64" height="64">twinpanel.png</icon>\n  <icon type="local" width="256" height="256">/usr/share/icons/hicolor/256x256/apps/twinpanel.png</icon>\n&#' \
        "$DEB/usr/share/metainfo/be.chrypox.TwinPanel.metainfo.xml"
    echo '</components>'
} > "$CATALOG/xml/twinpanel.xml"

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
