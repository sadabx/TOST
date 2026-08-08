#!/usr/bin/env bash
set -euo pipefail

version="${1:?usage: build-packages.sh VERSION PUBLISH_DIR OUTPUT_DIR}"
publish_dir="$(realpath "${2:?missing publish directory}")"
mkdir -p "${3:?missing output directory}"
output_dir="$(realpath "$3")"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work_dir="$(mktemp -d)"
trap 'rm -rf -- "$work_dir"' EXIT

test -x "$publish_dir/tost"
install -Dm755 "$publish_dir/tost" "$work_dir/portable/tost"
install -Dm644 "$repo_root/LICENSE" "$work_dir/portable/LICENSE"
tar -C "$work_dir/portable" -czf "$output_dir/TOST-${version}-linux-x64.tar.gz" .

appdir="$work_dir/TOST.AppDir"
install -Dm755 "$publish_dir/tost" "$appdir/usr/bin/tost"
install -Dm644 "$repo_root/LICENSE" "$appdir/usr/share/licenses/tost/LICENSE"
install -Dm644 "$repo_root/packaging/linux/tost.desktop" "$appdir/usr/share/applications/tost.desktop"
install -Dm644 "$repo_root/packaging/linux/tost.appdata.xml" "$appdir/usr/share/metainfo/tost.appdata.xml"
install -Dm644 "$repo_root/Assets/TOST.png" "$appdir/usr/share/icons/hicolor/512x512/apps/tost.png"
cp "$repo_root/packaging/linux/tost.desktop" "$appdir/tost.desktop"
cp "$repo_root/Assets/TOST.png" "$appdir/tost.png"
ln -s usr/bin/tost "$appdir/AppRun"

appimagetool="${APPIMAGETOOL:-}"
if [[ -z "$appimagetool" || ! -x "$appimagetool" ]]; then
  echo "APPIMAGETOOL must point to an executable appimagetool" >&2
  exit 1
fi
ARCH=x86_64 VERSION="$version" APPIMAGE_EXTRACT_AND_RUN=1 "$appimagetool" --no-appstream "$appdir" "$output_dir/TOST-${version}-x86_64.AppImage"

pkgroot="$work_dir/arch-root"
install -Dm755 "$publish_dir/tost" "$pkgroot/usr/bin/tost"
install -Dm644 "$repo_root/LICENSE" "$pkgroot/usr/share/licenses/tost/LICENSE"
install -Dm644 "$repo_root/packaging/linux/tost.desktop" "$pkgroot/usr/share/applications/tost.desktop"
install -Dm644 "$repo_root/packaging/linux/tost.appdata.xml" "$pkgroot/usr/share/metainfo/tost.appdata.xml"
install -Dm644 "$repo_root/Assets/TOST.png" "$pkgroot/usr/share/icons/hicolor/512x512/apps/tost.png"
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
EOF
tar --zstd --numeric-owner --owner=0 --group=0 -C "$pkgroot" -cf "$output_dir/tost-${version}-1-x86_64.pkg.tar.zst" .

(cd "$output_dir" && sha256sum "TOST-${version}-linux-x64.tar.gz" "TOST-${version}-x86_64.AppImage" "tost-${version}-1-x86_64.pkg.tar.zst" > SHA256SUMS-linux.txt)
