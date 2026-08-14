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

# Sign one library or helper binary. Deliberately not a bundle-wide --deep: Apple
# documents that as unsuitable for distribution, and it gives no say over what gets
# signed with what. No entitlements here — they only mean anything on the executable
# a process is launched from, and codesign is entitled to complain about them
# anywhere else.
sign_nested() {
    if [ "$have_identity" = yes ]; then
        codesign --force --sign "$identity" --options runtime --timestamp "$1"
    else
        codesign --force --sign "$identity" "$1"
    fi
}

# Sign the bundle itself: the hardened runtime, which notarisation requires, and the
# entitlements that let .NET and libvlc survive it.
#
# Both only apply with a real identity. An ad-hoc signature cannot be notarised
# whatever options it carries, so turning the hardened runtime on for a local build
# would add nothing but a way for it to fail.
sign_app() {
    if [ "$have_identity" = yes ]; then
        codesign --force --sign "$identity" --options runtime \
            --entitlements "$entitlements" --timestamp "$1"
    else
        codesign --force --sign "$identity" "$1"
    fi
}

# Sign a bundle from the inside out.
#
# A bundle's signature covers its contents, so anything signed after it invalidates
# it. The 340-odd libraries therefore go first and the bundle last. That is the whole
# reason this is a loop rather than one command.
sign_bundle() {
    local bundle="$1"

    echo "        signing nested code (identity: $identity)"

    # Everything codesign counts as code, which is more than it first appears:
    #
    #   *.dylib  libvlc, its 339 plugins, and the native halves of .NET
    #   *.dll    the managed assemblies, including the satellite ones under cs/
    #
    # The .dll files are the ones worth explaining. They are PE files, not Mach-O, and
    # look like data from a distance — but codesign treats the extension as code and
    # --strict verification refuses a bundle where they are unsigned:
    #
    #   Luma.app: code object is not signed at all
    #   In subcomponent: .../System.Diagnostics.Contracts.dll
    #
    # --deep had been signing them all along without ever saying so, and the old
    # verification was too shallow to notice either way.
    find "$bundle/Contents/MacOS" -type f \
        \( -name '*.dylib' -o -name '*.so' -o -name '*.dll' \) -print0 |
        while IFS= read -r -d '' library; do
            sign_nested "$library"
        done

    # Shipped by a self-contained .NET publish and a Mach-O executable in its own
    # right, so it needs a signature like everything else. No extension to match on.
    if [ -f "$bundle/Contents/MacOS/createdump" ]; then
        sign_nested "$bundle/Contents/MacOS/createdump"
    fi

    echo "        signing the bundle"
    sign_app "$bundle"

    # Verify the bundle seal, which covers every file inside it by hash.
    #
    # Deliberately not --deep. That treats everything under Contents/MacOS as a nested
    # code object needing a signature of its own, and a self-contained .NET publish
    # puts its whole payload there — including Luma.runtimeconfig.json, which --deep
    # duly rejected as "not signed at all". That is a question about bundle layout,
    # not about signing: the executable's own directory is where the .NET host looks
    # for its runtime configuration and where libvlc looks for its plugins, so the
    # payload cannot simply move to Resources.
    codesign --verify --strict --verbose=2 "$bundle"

    # What --deep was wanted for: proof the libraries are signed and that signing the
    # bundle afterwards did not invalidate them. Checked directly instead, on the two
    # that matter most — the media engine and one of its plugins.
    codesign --verify --verbose=2 "$bundle/Contents/MacOS/libvlc.dylib"
    codesign --verify --verbose=2 \
        "$(find "$bundle/Contents/MacOS/plugins" -name '*.dylib' | head -1)"

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
