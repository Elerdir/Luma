#!/usr/bin/env bash
#
# Builds a macOS disk image for Luma.
#
#   ./installer/macos/build-dmg.sh              Apple silicon, version from Directory.Build.props
#   ./installer/macos/build-dmg.sh 1.2.0        an explicit version
#   ./installer/macos/build-dmg.sh 1.2.0 x64    an Intel build
#
# Output: dist/Luma-<version>-<arm64|x64>.dmg
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

# ---- Architecture ------------------------------------------------------------
#
# Apple silicon by default, because that is every Mac sold since 2020. Intel is
# still built and published: the update client asks the server for the architecture
# it is running on, so a Mac that asks for x64 and finds nothing is told nothing —
# update checks are deliberately silent, and the user never learns why.
#
# VLC names the Intel image "intel64" rather than "x64", which is why the disk image
# name is tracked separately from the one Luma's own artifacts use.
target="${2:-arm64}"

case "$target" in
    arm64)
        rid="osx-arm64"
        arch="arm64"
        vlc_arch="arm64"
        # Pinned, not fetched alongside the download: get.videolan.org redirects to
        # community mirrors, and a checksum taken from the same place as the file
        # proves only that the transfer was intact. Read from two independent
        # mirrors (ftp.sh.cvut.cz, ftp.fau.de).
        vlc_sha256="15dd65bf6489da9ec6a67f5585c74c40a58993acff41a82958a916dd74178044"
        ;;
    x64)
        rid="osx-x64"
        arch="x64"
        vlc_arch="intel64"
        # Read from three independent mirrors (ftp.fau.de, mirror.csclub.uwaterloo.ca,
        # mirrors.tuna.tsinghua.edu.cn), all agreeing.
        vlc_sha256="d431fd051c3dc7af02bd313c6d05d90cf604b70ed3ec5bba6fd4c49ef3e638d9"
        ;;
    *)
        echo "[ERROR] Unknown architecture '$target'. Use arm64 or x64." >&2
        exit 1
        ;;
esac

# libvlc comes out of the official VLC release rather than from NuGet: the
# VideoLAN.LibVLC.Mac package contains one x64 libvlc.dylib and no plugin
# directory, so it can neither load on Apple silicon nor decode anything.
#
# Bumping this means replacing both checksums above — the build stops until someone
# does, which is the point of pinning them.
vlc_version="3.0.21"

publish_dir="artifacts/publish/$rid"
# Per architecture, so building both in turn on one machine does not have the second
# run signing and packaging whatever the first left behind.
build_dir="build/macos/$arch"
app="$build_dir/Luma.app"
dmg="dist/Luma-$version-$arch.dmg"
entitlements="installer/macos/Luma.entitlements"

# ---- Signing -----------------------------------------------------------------
#
# CODESIGN_IDENTITY names a Developer ID Application certificate in the keychain.
# Without one the bundle is signed ad-hoc, which is the difference between "merely
# unidentified" and "Gatekeeper refuses outright" — but it is not distributable:
# only a real identity can be notarised. See the README.
# Written as two branches rather than an array of options: the bash macOS ships is
# 3.2, where expanding an empty array under `set -u` is itself an error.
identity="${CODESIGN_IDENTITY:-}"
have_identity=yes
if [ -z "$identity" ]; then
    identity="-"
    have_identity=no
fi

# Sign the bundle and everything in it.
#
# --deep, which Apple's own documentation calls unsuitable for distribution signing.
# It is the right tool here anyway, and it took three failed release builds to work
# out why. Signing the nested code by hand and the bundle afterwards — the shape the
# documentation recommends — does not verify:
#
#   Luma.app: code object is not signed at all
#   In subcomponent: .../Luma.runtimeconfig.json
#
# Contents/MacOS is, to codesign, a directory of executables: anything in it that is
# not the main binary is nested code needing a signature of its own, and that includes
# the JSON. A self-contained .NET publish puts its entire payload there, and the
# layout is not free to change — the .NET host reads its runtime configuration beside
# the executable and libvlc looks for its plugins beside itself. --deep signs the lot,
# which is what makes it work.
#
# The documented objection to --deep is that it applies one set of options to every
# executable it reaches, so a bundle with several that need different entitlements
# cannot be signed correctly. Luma has one executable. The libraries receive the
# entitlements too and ignore them, because only the entitlements of the binary a
# process is launched from are ever consulted.
sign_bundle() {
    local bundle="$1"

    echo "        signing (identity: $identity)"

    if [ "$have_identity" = yes ]; then
        # The hardened runtime is what notarisation requires, and the entitlements are
        # what let .NET and libvlc survive it. Both only apply with a real identity: an
        # ad-hoc signature cannot be notarised whatever options it carries, so turning
        # the hardened runtime on for a local build would add nothing but a way for it
        # to fail.
        codesign --force --deep --sign "$identity" \
            --options runtime --entitlements "$entitlements" --timestamp "$bundle"
    else
        codesign --force --deep --sign "$identity" "$bundle"
    fi

    codesign --verify --verbose=2 "$bundle"

    # Nested code, checked directly. This is what a deep verification was wanted for,
    # and it cannot be had that way — but "did libvlc and its plugins actually get
    # signed" is answerable on its own, and it is the part that matters.
    codesign --verify --verbose=2 "$bundle/Contents/MacOS/libvlc.dylib"

    first_plugin="$(find "$bundle/Contents/MacOS/plugins" -name '*.dylib' | head -1)"
    codesign --verify --verbose=2 "$first_plugin"

    # And that the entitlements actually landed on the executable, rather than being
    # passed to a codesign invocation that ignored them.
    echo "        entitlements on the bundle:"
    codesign --display --entitlements - "$bundle" 2>&1 | sed 's/^/          /' || true

    # What Gatekeeper will make of it. Ad-hoc fails this by design — the point of
    # printing it is that the gap is visible in the build log rather than discovered
    # by whoever downloads the disk image.
    echo "        Gatekeeper assessment:"
    spctl --assess --type exec --verbose=4 "$bundle" 2>&1 | sed 's/^/          /' || true
}

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

# get.videolan.org redirects to whichever community mirror it picks, and one of them
# stalling used to hang the release: a run was cancelled after eleven minutes sitting
# on a curl that had stopped receiving bytes and would have waited for ever.
#
# --speed-limit/--speed-time is the part that matters. A flat timeout has to be
# generous enough for a slow mirror to finish, which makes it useless against a
# stalled one; giving up after a minute below 10 kB/s catches the stall in a minute
# and no legitimate download in any. Each retry goes back through the redirect, so it
# is a fresh chance at a different mirror.
curl --fail --location --silent --show-error \
    --connect-timeout 30 \
    --speed-limit 10240 --speed-time 60 \
    --max-time 900 \
    --retry 3 --retry-delay 5 --retry-all-errors \
    -o "$vlc_dmg" \
    "https://get.videolan.org/vlc/${vlc_version}/macosx/vlc-${vlc_version}-${vlc_arch}.dmg"

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

sign_bundle "$app"

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
