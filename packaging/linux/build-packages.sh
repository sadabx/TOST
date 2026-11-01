#!/usr/bin/env bash
set -euo pipefail

version="${1:?usage: build-packages.sh VERSION DESKTOP_DIR CLI_DIR OUTPUT_DIR}"
desktop_dir="$(realpath "${2:?missing desktop publish directory}")"
cli_dir="$(realpath "${3:?missing CLI publish directory}")"
mkdir -p "${4:?missing output directory}"
output_dir="$(realpath "$4")"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work_dir="$(mktemp -d)"
trap 'rm -rf -- "$work_dir"' EXIT

test -x "$desktop_dir/TOST.Desktop"
test -x "$cli_dir/tost"
install -Dm755 "$desktop_dir/TOST.Desktop" "$work_dir/portable/tost"
install -Dm755 "$cli_dir/tost" "$work_dir/portable/tost-cli"
install -Dm644 "$repo_root/LICENSE" "$work_dir/portable/LICENSE"
install -Dm644 "$repo_root/packaging/linux/tost.desktop" "$work_dir/portable/tost.desktop"
install -Dm644 "$repo_root/packaging/linux/tost.appdata.xml" "$work_dir/portable/tost.appdata.xml"
install -Dm644 "$repo_root/docs/logo.png" "$work_dir/portable/tost.png"
(cd "$work_dir/portable" && tar -czf "$output_dir/TOST-${version}-linux-x64.tar.gz" *)

appdir="$work_dir/TOST.AppDir"
install -Dm755 "$desktop_dir/TOST.Desktop" "$appdir/usr/bin/tost"
install -Dm755 "$cli_dir/tost" "$appdir/usr/bin/tost-cli"
install -Dm644 "$repo_root/LICENSE" "$appdir/usr/share/licenses/tost/LICENSE"
install -Dm644 "$repo_root/packaging/linux/tost.desktop" "$appdir/usr/share/applications/tost.desktop"
install -Dm644 "$repo_root/packaging/linux/tost.appdata.xml" "$appdir/usr/share/metainfo/tost.appdata.xml"
install -Dm644 "$repo_root/docs/logo.png" "$appdir/usr/share/icons/hicolor/512x512/apps/tost.png"
cp "$repo_root/packaging/linux/tost.desktop" "$appdir/tost.desktop"
cp "$repo_root/docs/logo.png" "$appdir/tost.png"
ln -s usr/bin/tost "$appdir/AppRun"

appimagetool="${APPIMAGETOOL:-}"
if [[ -z "$appimagetool" || ! -x "$appimagetool" ]]; then
  echo "APPIMAGETOOL must point to an executable appimagetool" >&2
  exit 1
fi
ARCH=x86_64 VERSION="$version" APPIMAGE_EXTRACT_AND_RUN=1 "$appimagetool" --no-appstream "$appdir" "$output_dir/TOST-${version}-x86_64.AppImage"

pkgroot="$work_dir/arch-root"
install -Dm755 "$desktop_dir/TOST.Desktop" "$pkgroot/usr/bin/tost"
install -Dm755 "$cli_dir/tost" "$pkgroot/usr/bin/tost-cli"
install -Dm644 "$repo_root/LICENSE" "$pkgroot/usr/share/licenses/tost/LICENSE"
install -Dm644 "$repo_root/packaging/linux/tost.desktop" "$pkgroot/usr/share/applications/tost.desktop"
install -Dm644 "$repo_root/packaging/linux/tost.appdata.xml" "$pkgroot/usr/share/metainfo/tost.appdata.xml"
install -Dm644 "$repo_root/docs/logo.png" "$pkgroot/usr/share/icons/hicolor/512x512/apps/tost.png"
installed_size="$(du -sk "$pkgroot" | cut -f1)"
arch_version="${version//-/.}"
cat > "$pkgroot/.PKGINFO" <<EOF
pkgname = tost
pkgbase = tost
pkgver = $arch_version-1
pkgdesc = TOST Steam integration manager
url = https://github.com/sadabx/TOST
builddate = $(date +%s)
packager = TOST release workflow
size = $((installed_size * 1024))
arch = x86_64
license = GPL-3.0-only
depend = fontconfig
depend = freetype2
depend = gtk3
depend = glibc
depend = libx11
depend = libxcursor
depend = libxext
depend = libxinerama
depend = libxrandr
depend = hicolor-icon-theme
EOF

if command -v bsdtar >/dev/null 2>&1; then
  (cd "$pkgroot" && LANG=C bsdtar -czf .MTREE --format=mtree --options='!all,use-set,type,uid,gid,mode,time,size,md5,sha256,link' .PKGINFO usr)
fi

(
  cd "$pkgroot"
  arch_files=(.PKGINFO)
  [[ -f .MTREE ]] && arch_files+=(.MTREE)
  for item in *; do
    [[ -e "$item" ]] && arch_files+=("$item")
  done
  tar --zstd --numeric-owner --owner=0 --group=0 -cf "$output_dir/tost-${version}-1-x86_64.pkg.tar.zst" "${arch_files[@]}"
)

debroot="$work_dir/debian-root"
install -Dm755 "$desktop_dir/TOST.Desktop" "$debroot/usr/bin/tost"
install -Dm755 "$cli_dir/tost" "$debroot/usr/bin/tost-cli"
install -Dm644 "$repo_root/LICENSE" "$debroot/usr/share/doc/tost/copyright"
install -Dm644 "$repo_root/packaging/linux/tost.desktop" "$debroot/usr/share/applications/tost.desktop"
install -Dm644 "$repo_root/docs/logo.png" "$debroot/usr/share/icons/hicolor/512x512/apps/tost.png"
deb_version="${version//-/\~}"
install -Dm644 /dev/stdin "$debroot/DEBIAN/control" <<EOF
Package: tost
Version: $deb_version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: TOST <noreply@github.com>
Depends: libc6 (>= 2.35), libfontconfig1, libfreetype6, libglib2.0-0, libgtk-3-0, libice6, libsm6, libx11-6, libxcursor1, libxext6, libxinerama1, libxrandr2
Description: TOST Steam integration manager
 Manage TOST Steam integrations on Linux.
EOF
dpkg-deb --build --root-owner-group "$debroot" "$output_dir/tost_${deb_version}_amd64.deb"

(cd "$output_dir" && sha256sum "TOST-${version}-linux-x64.tar.gz" "TOST-${version}-x86_64.AppImage" "tost-${version}-1-x86_64.pkg.tar.zst" "tost_${deb_version}_amd64.deb" > SHA256SUMS-linux.txt)
