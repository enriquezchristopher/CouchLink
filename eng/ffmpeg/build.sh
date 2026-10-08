#!/usr/bin/env bash
# Builds CouchLink's minimal FFmpeg for Windows x64 from the pinned sources in versions.env, and
# zips the DLLs and their complete source (the GPL "corresponding source").
# Needs Ubuntu 24.04 with: mingw-w64 nasm pkg-config make git curl xz-utils zip
# Usage: bash eng/ffmpeg/build.sh <work-dir>
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=versions.env
source "$here/versions.env"
work="$(realpath -m "${1:?usage: build.sh <work-dir>}")"
src="$work/src" build="$work/build" prefix="$work/prefix" dist="$work/dist" out="$work/out"
host=x86_64-w64-mingw32
jobs="$(nproc)"

rm -rf "$work"
mkdir -p "$src" "$build" "$prefix" "$dist/bin" "$out"

# 1. Pinned sources, kept as downloaded for the source zip.
curl -fsSL -o "$src/ffmpeg-$FFMPEG_VERSION.tar.xz" "https://ffmpeg.org/releases/ffmpeg-$FFMPEG_VERSION.tar.xz"
echo "$FFMPEG_SHA256  $src/ffmpeg-$FFMPEG_VERSION.tar.xz" | sha256sum -c -

# git_snapshot <name> <url> <commit> [paths...]: a .tar.gz of exactly that commit (optionally only some paths).
git_snapshot() {
    local name=$1 url=$2 commit=$3 repo="$work/git-$1"
    shift 3
    git init -q "$repo"
    git -C "$repo" fetch -q --depth 1 --filter=blob:none "$url" "$commit"
    if [ "$(git -C "$repo" rev-parse FETCH_HEAD)" != "$commit" ]; then
        echo "$name: fetched $(git -C "$repo" rev-parse FETCH_HEAD), expected $commit" >&2
        exit 1
    fi
    git -C "$repo" archive --format=tar.gz --prefix="$name/" -o "$src/$name.tar.gz" FETCH_HEAD "$@"
}
git_snapshot "x264-$X264_COMMIT" "$X264_URL" "$X264_COMMIT"
git_snapshot "nv-codec-headers-$NVCODEC_TAG" "$NVCODEC_URL" "$NVCODEC_COMMIT"
git_snapshot "amf-headers-$AMF_TAG" "$AMF_URL" "$AMF_COMMIT" amf/public/include LICENSE.txt

for archive in "$src"/*; do tar -xf "$archive" -C "$build"; done

# 2. NVIDIA and AMD encoder headers (MIT, header-only; the encoders load from the driver).
make -C "$build/nv-codec-headers-$NVCODEC_TAG" PREFIX="$prefix" install
mkdir -p "$prefix/include/AMF"
cp -r "$build/amf-headers-$AMF_TAG/amf/public/include/." "$prefix/include/AMF/"

# 3. x264, static, linked into avcodec.
(
    cd "$build/x264-$X264_COMMIT"
    ./configure --host="$host" --cross-prefix="$host-" --prefix="$prefix" \
        --enable-static --enable-pic --disable-cli --disable-opencl --enable-win32thread
    make -j"$jobs"
    make install
)

# 4. FFmpeg with only what CouchLink calls (see FfmpegLibrary, H264Decoder, H264Encoder, H264Frames).
configure_args=(
    --prefix="$prefix" --target-os=mingw32 --arch=x86_64 --cross-prefix="$host-"
    --pkg-config=pkg-config --pkg-config-flags=--static
    --extra-cflags="-I$prefix/include" --extra-ldflags="-L$prefix/lib -static-libgcc"
    --enable-gpl --enable-shared --disable-static
    --disable-autodetect --disable-everything --disable-programs --disable-doc --disable-network --disable-debug
    --disable-avformat --disable-avdevice --disable-avfilter
    --enable-w32threads --enable-d3d11va --enable-ffnvcodec --enable-nvenc --enable-amf --enable-libx264
    --enable-decoder=h264 --enable-parser=h264 --enable-hwaccel=h264_d3d11va,h264_d3d11va2
    --enable-encoder=libx264,h264_nvenc,h264_amf
)
(
    cd "$build/ffmpeg-$FFMPEG_VERSION"
    PKG_CONFIG_PATH="$prefix/lib/pkgconfig" ./configure "${configure_args[@]}"
    make -j"$jobs"
    make install
)

# 5. Checks: every component configure may have dropped silently is really in, and no DLL needs
#    a mingw runtime DLL that a clean Windows PC doesn't have.
components="$build/ffmpeg-$FFMPEG_VERSION/config_components.h"
for name in H264_DECODER H264_PARSER H264_D3D11VA_HWACCEL H264_D3D11VA2_HWACCEL \
            LIBX264_ENCODER H264_NVENC_ENCODER H264_AMF_ENCODER; do
    grep -q "^#define CONFIG_$name 1" "$components" || { echo "FFmpeg was configured without $name" >&2; exit 1; }
done
dlls=(avcodec-63.dll avutil-61.dll swscale-10.dll swresample-7.dll)
for dll in "${dlls[@]}"; do
    cp "$prefix/bin/$dll" "$dist/bin/"
    "$host-strip" "$dist/bin/$dll"
    bad="$("$host-objdump" -p "$dist/bin/$dll" | awk '/DLL Name:/ {print $3}' | grep -i '^lib' || true)"
    if [ -n "$bad" ]; then echo "$dll needs $bad, which Windows doesn't have" >&2; exit 1; fi
done

# 6. License text and the two zips.
ff="$build/ffmpeg-$FFMPEG_VERSION"
{
    echo "CouchLink's FFmpeg $FFMPEG_VERSION build ($BUILD_ID), with x264 $X264_COMMIT."
    echo "This build is licensed under the GNU GPL version 2 or later. Its complete source is"
    echo "couchlink-ffmpeg-$BUILD_ID-source.zip, attached to every CouchLink release."
    echo
    cat "$ff/LICENSE.md"
    echo
    cat "$ff/COPYING.GPLv2"
} > "$dist/LICENSE.txt"
{
    echo "Toolchain: $("$host-gcc" --version | head -n 1)"
    echo "NASM: $(nasm -v)"
    echo "FFmpeg configure: ${configure_args[*]}"
    echo "x264 configure: --host=$host --enable-static --enable-pic --disable-cli --disable-opencl --enable-win32thread"
} > "$src/CONFIGURE.txt"
cp "$here/build.sh" "$here/versions.env" "$src/"

(cd "$dist" && zip -qr "$out/couchlink-ffmpeg-$BUILD_ID-win64.zip" bin LICENSE.txt)
(cd "$src" && zip -qr "$out/couchlink-ffmpeg-$BUILD_ID-source.zip" .)
ls -l "$out"
