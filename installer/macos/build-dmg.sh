#!/usr/bin/env bash
#
# Builds a macOS disk image for Luma.
#
#   ./installer/macos/build-dmg.sh            build at the version in Directory.Build.props
#   ./installer/macos/build-dmg.sh 1.2.0      build at an explicit version
#
# Output: dist/Luma-<version>-arm64.dmg
#
# This is the macOS counterpart to instalator.bat, and for the same reason: the
# packaging used to live only inside a workflow step, where the only way to try a
# change was to push it and wait. Everything here runs on any Mac with the .NET SDK.
#
# Requires macOS — sips, iconutil, hdiutil and codesign are all system tools.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "[ERROR] This script builds a macOS bundle and needs macOS to do it." >&2
    exit 1
fi

# ---- Version -----------------------------------------------------------------
# Single source of truth is Directory.Build.props, the same one the MSI reads.
version="${1:-}"
if [ -z "$version" ]; then
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
fi

if [ -z "$version" ]; then
    echo "[ERROR] Could not determine the version. Pass one explicitly: build-dmg.sh 1.2.0" >&2
    exit 1
fi

# macos-latest is Apple silicon, and so is every Mac sold since 2020. An Intel
# build would need its own VLC disk image and its own pinned checksum.
rid="osx-arm64"
arch="arm64"

# libvlc comes out of the official VLC release rather than from NuGet: the
# VideoLAN.LibVLC.Mac package contains one x64 libvlc.dylib and no plugin
# directory, so it can neither load on Apple silicon nor decode anything.
vlc_version="3.0.21"

# Pinned, not fetched alongside the download: get.videolan.org redirects to
# community mirrors, and a checksum taken from the same place as the file proves
# only that the transfer was intact. This value was read from two independent
# mirrors (ftp.sh.cvut.cz, ftp.fau.de) and matches VideoLAN's published
# vlc-3.0.21-arm64.dmg.sha256. Bumping vlc_version means replacing it — the build
# stops until someone does.
vlc_sha256="15dd65bf6489da9ec6a67f5585c74c40a58993acff41a82958a916dd74178044"

publish_dir="artifacts/publish/$rid"
build_dir="build/macos"
app="$build_dir/Luma.app"
dmg="dist/Luma-$version-$arch.dmg"

echo
echo "=== Luma disk image ========================================"
echo " version : $version"
echo " runtime : $rid"
echo " output  : $dmg"
echo "============================================================"
echo

# A previous run must not leak into this one: a stale Luma.app would be signed
# and shipped with whatever it happened to contain.
rm -rf "$build_dir" "$publish_dir"
mkdir -p "$build_dir" dist

# ---- Publish -----------------------------------------------------------------
# Self-contained: the installed app must not require a .NET runtime on the Mac.
echo "[1/5] Publishing $rid (self-contained)..."
dotnet publish src/Luma.Presentation/Luma.Presentation.csproj \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    -p:Version="$version" \
    --output "$publish_dir"

# ---- libvlc ------------------------------------------------------------------
echo "[2/5] Fetching libvlc $vlc_version from the official VLC release..."
vlc_dmg="$build_dir/vlc.dmg"
curl -fsSL -o "$vlc_dmg" \
    "https://get.videolan.org/vlc/${vlc_version}/macosx/vlc-${vlc_version}-${arch}.dmg"

echo "${vlc_sha256}  ${vlc_dmg}" | shasum -a 256 -c -

mountpoint="/Volumes/VLC-luma-$$"
hdiutil attach "$vlc_dmg" -mountpoint "$mountpoint" -nobrowse -quiet
# Unmount even if a copy fails, otherwise the volume is left attached and the
# next run collides with it.
trap 'hdiutil detach "$mountpoint" -quiet >/dev/null 2>&1 || true' EXIT

mkdir -p "$build_dir/libvlc"
cp -R "$mountpoint/VLC.app/Contents/MacOS/lib"     "$build_dir/libvlc/lib"
cp -R "$mountpoint/VLC.app/Contents/MacOS/plugins" "$build_dir/libvlc/plugins"

hdiutil detach "$mountpoint" -quiet
trap - EXIT

echo "        dylibs:  $(find "$build_dir/libvlc/lib" -name '*.dylib' | wc -l | tr -d ' ')"
echo "        plugins: $(find "$build_dir/libvlc/plugins" -name '*.dylib' | wc -l | tr -d ' ')"
test -f "$build_dir/libvlc/lib/libvlc.dylib"

# ---- Icon --------------------------------------------------------------------
echo "[3/5] Building the icon..."
iconset="$build_dir/luma.iconset"
mkdir -p "$iconset"
# Downscale from the 1024px source rather than upscaling the 256px one.
source_png="src/Luma.Presentation/Assets/luma-1024.png"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$source_png" \
        --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$source_png" \
        --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$build_dir/luma.icns"

# ---- Bundle ------------------------------------------------------------------
echo "[4/5] Assembling Luma.app..."
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp -R "$publish_dir/." "$app/Contents/MacOS/"

# LibVLCSharp looks for libvlc next to the executable, and libvlc looks for its
# plugins in ./plugins relative to itself. Flattening both into MacOS/ is the
# layout that satisfies the pair.
cp "$build_dir"/libvlc/lib/*.dylib "$app/Contents/MacOS/"
cp -R "$build_dir/libvlc/plugins"  "$app/Contents/MacOS/plugins"

cp "$build_dir/luma.icns" "$app/Contents/Resources/luma.icns"
sed "s/VERSION/$version/g" installer/macos/Info.plist > "$app/Contents/Info.plist"

chmod +x "$app/Contents/MacOS/Luma"

# Ad-hoc signature. Without any signature at all Gatekeeper refuses to run the
# bundle outright; with it, the app is merely unidentified. Proper notarisation
# needs a paid Apple Developer account — see the README.
codesign --force --deep --sign - "$app"
codesign --verify --verbose "$app"

# ---- Disk image --------------------------------------------------------------
echo "[5/5] Building the disk image..."
staging="$build_dir/dmg"
mkdir -p "$staging"
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"

rm -f "$dmg"
hdiutil create \
    -volname "Luma $version" \
    -srcfolder "$staging" \
    -ov -format UDZO \
    "$dmg"

echo
echo " $(basename "$dmg")  ($(du -h "$dmg" | cut -f1))"
echo " $repo_root/$dmg"
echo
